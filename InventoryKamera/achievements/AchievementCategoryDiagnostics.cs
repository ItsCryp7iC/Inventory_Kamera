using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using InventoryKamera.game;

namespace InventoryKamera
{
    internal enum AchievementUnresolvedCause
    {
        TitleOcrUnresolved,
        CanonicalIdentityAmbiguous,
        CompletionEvidenceUnresolved,
        GeometryMatchAmbiguous,
        InsufficientMotionEvidence,
        UncertainRowBoundary,
        UnrecoveredPartial,
        ConflictingObservation,
        OtherUnresolved,
    }

    /// <summary>
    /// Bitmap-free, bounded diagnostic evidence for one reconciled row interval. It deliberately
    /// reports candidate counts rather than identifiers and never infers identity or completion.
    /// </summary>
    internal sealed class AchievementUnresolvedIntervalDiagnostic
    {
        internal const int MaximumOcrExcerptLength = 80;
        internal const int MaximumRejectionReasonLength = 160;

        internal AchievementUnresolvedIntervalDiagnostic(int intervalNumber,
            int sourceFrameIndex, IEnumerable<int> otherFrameIndexes, int rowIndex,
            NormalizedRectangle normalizedBounds, AchievementVisibleRowVisibility visibility,
            string ocrExcerpt, AchievementIdentityState identityState, int candidateCount,
            AchievementCompletionState completionState,
            AchievementObservationReconciliationStatus reconciliationStatus,
            string rejectionReason, bool movementVerified, bool overlapVerified,
            bool candidateCanonicalGroupEstablished, bool exactTitleVerificationUsefulLater,
            AchievementUnresolvedCause primaryCause,
            IEnumerable<AchievementUnresolvedCause> secondaryCauses,
            bool identityOutstanding)
        {
            IntervalNumber = intervalNumber;
            SourceFrameIndex = sourceFrameIndex;
            OtherFrameIndexes = new ReadOnlyCollection<int>((otherFrameIndexes ?? Array.Empty<int>())
                .Distinct().OrderBy(value => value).ToArray());
            RowIndex = rowIndex;
            NormalizedBounds = normalizedBounds;
            Visibility = visibility;
            OcrExcerpt = Sanitize(ocrExcerpt, MaximumOcrExcerptLength);
            IdentityState = identityState;
            CandidateCount = Math.Max(0, candidateCount);
            CompletionState = completionState;
            ReconciliationStatus = reconciliationStatus;
            RejectionReason = Sanitize(rejectionReason, MaximumRejectionReasonLength);
            MovementVerified = movementVerified;
            OverlapVerified = overlapVerified;
            CandidateCanonicalGroupEstablished = candidateCanonicalGroupEstablished;
            ExactTitleVerificationUsefulLater = exactTitleVerificationUsefulLater;
            PrimaryCause = primaryCause;
            SecondaryCauses = new ReadOnlyCollection<AchievementUnresolvedCause>(
                (secondaryCauses ?? Array.Empty<AchievementUnresolvedCause>())
                    .Where(value => value != primaryCause).Distinct().ToArray());
            IdentityOutstanding = identityOutstanding;
        }

        internal int IntervalNumber { get; }
        internal int SourceFrameIndex { get; }
        internal IReadOnlyList<int> OtherFrameIndexes { get; }
        internal int RowIndex { get; }
        internal NormalizedRectangle NormalizedBounds { get; }
        internal AchievementVisibleRowVisibility Visibility { get; }
        internal string OcrExcerpt { get; }
        internal AchievementIdentityState IdentityState { get; }
        internal int CandidateCount { get; }
        internal AchievementCompletionState CompletionState { get; }
        internal AchievementObservationReconciliationStatus ReconciliationStatus { get; }
        internal string RejectionReason { get; }
        internal bool MovementVerified { get; }
        internal bool OverlapVerified { get; }
        internal bool CandidateCanonicalGroupEstablished { get; }
        internal bool ExactTitleVerificationUsefulLater { get; }
        internal AchievementUnresolvedCause PrimaryCause { get; }
        internal IReadOnlyList<AchievementUnresolvedCause> SecondaryCauses { get; }
        internal bool IdentityOutstanding { get; }
        internal string NormalizedBoundsText => string.Create(CultureInfo.InvariantCulture,
            $"x={NormalizedBounds.X:0.0000},y={NormalizedBounds.Y:0.0000},w={NormalizedBounds.Width:0.0000},h={NormalizedBounds.Height:0.0000}");

        internal static string Sanitize(string value, int maximumLength)
        {
            string sanitized = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            sanitized = Regex.Replace(sanitized, @"(?<!\d)\d{9,}(?!\d)", "[redacted-number]");
            if (sanitized.Length <= maximumLength) return sanitized;
            return sanitized.Substring(0, Math.Max(0, maximumLength - 1)) + "…";
        }
    }

    internal static class AchievementUnresolvedIntervalDiagnosticBuilder
    {
        internal static IReadOnlyList<AchievementUnresolvedIntervalDiagnostic> Build(
            IEnumerable<AchievementObservedRow> sourceRows,
            IEnumerable<AchievementScrollObservation> sourceScrolls)
        {
            AchievementScrollObservation[] scrolls =
                (sourceScrolls ?? Array.Empty<AchievementScrollObservation>()).ToArray();
            AchievementObservedRow[] rows = (sourceRows ?? Array.Empty<AchievementObservedRow>())
                .Where(row => !row.Resolved || !row.HasTrustedCompletionEvidence)
                .OrderBy(row => row.FirstObservedFrame)
                .ThenBy(row => row.Observations.Select(value => value.Row.NormalizedBounds.Y)
                    .DefaultIfEmpty(double.MaxValue).Min())
                .ThenBy(row => row.Key, StringComparer.Ordinal)
                .ToArray();
            var results = new List<AchievementUnresolvedIntervalDiagnostic>(rows.Length);
            for (int index = 0; index < rows.Length; index++)
                results.Add(Build(rows[index], scrolls, index + 1));
            return new ReadOnlyCollection<AchievementUnresolvedIntervalDiagnostic>(results);
        }

        private static AchievementUnresolvedIntervalDiagnostic Build(AchievementObservedRow row,
            AchievementScrollObservation[] scrolls, int intervalNumber)
        {
            AchievementRowFrameObservation selected = row.Observations
                .OrderByDescending(value => Strength(value.Row))
                .ThenBy(value => value.FrameIndex)
                .ThenBy(value => value.Row.RowIndex)
                .First();
            int[] frames = row.Observations.Select(value => value.FrameIndex)
                .Distinct().OrderBy(value => value).ToArray();
            AchievementScrollObservation[] relatedScrolls = scrolls.Where(scroll =>
                frames.Contains(scroll.BatchIndex - 1) || frames.Contains(scroll.BatchIndex)).ToArray();
            List<AchievementUnresolvedCause> causes = Causes(row, selected).Distinct().ToList();
            AchievementUnresolvedCause primary = causes.Count == 0
                ? AchievementUnresolvedCause.OtherUnresolved
                : causes[0];
            bool exactTitleUseful = selected.Row.Visibility == AchievementVisibleRowVisibility.Complete &&
                (selected.Row.IdentityState == AchievementIdentityState.Unresolved ||
                 selected.Row.IdentityState == AchievementIdentityState.Ambiguous ||
                 row.Conflicting ||
                 (row.Canonical && !row.HasTrustedCompletionEvidence));
            return new AchievementUnresolvedIntervalDiagnostic(intervalNumber,
                selected.FrameIndex, frames.Where(value => value != selected.FrameIndex),
                selected.Row.RowIndex, selected.Row.NormalizedBounds, selected.Row.Visibility,
                selected.Row.RawTitle, selected.Row.IdentityState, selected.Row.CandidateIds.Count,
                selected.Row.CompletionState, selected.ReconciliationStatus,
                selected.ReconciliationReason,
                relatedScrolls.Any(value => value.MovementVerified),
                relatedScrolls.Any(value => value.OverlapVerified),
                row.Observations.Any(value => value.Row.Fingerprint.SuitableForDeduplication),
                exactTitleUseful, primary, causes.Skip(1), !row.Resolved);
        }

        private static IEnumerable<AchievementUnresolvedCause> Causes(AchievementObservedRow row,
            AchievementRowFrameObservation selected)
        {
            if (row.Conflicting) yield return AchievementUnresolvedCause.ConflictingObservation;
            if (row.Observations.Any(value => value.Row.Visibility == AchievementVisibleRowVisibility.Uncertain))
                yield return AchievementUnresolvedCause.UncertainRowBoundary;
            if (row.Observations.Any(value => value.ReconciliationReason.Contains(
                "ambiguous-geometric-match", StringComparison.Ordinal)))
                yield return AchievementUnresolvedCause.GeometryMatchAmbiguous;
            if (row.Observations.Any(value => value.ReconciliationReason.Contains(
                "missing-or-unverified-movement", StringComparison.Ordinal)))
                yield return AchievementUnresolvedCause.InsufficientMotionEvidence;
            if (!row.EverFullyVisible && row.Observations.Any(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Partial))
                yield return AchievementUnresolvedCause.UnrecoveredPartial;
            if (row.Observations.Any(value =>
                value.Row.IdentityState == AchievementIdentityState.Ambiguous))
                yield return AchievementUnresolvedCause.CanonicalIdentityAmbiguous;
            if (!row.Resolved && selected.Row.IdentityState == AchievementIdentityState.Unresolved)
                yield return AchievementUnresolvedCause.TitleOcrUnresolved;
            if (row.Resolved && !row.HasTrustedCompletionEvidence)
                yield return AchievementUnresolvedCause.CompletionEvidenceUnresolved;
        }

        private static int Strength(AchievementVisibleRow row)
        {
            int value = row.Visibility == AchievementVisibleRowVisibility.Complete ? 4 :
                row.Visibility == AchievementVisibleRowVisibility.Partial ? 2 : 0;
            if (row.Fingerprint.SuitableForDeduplication) value += 4;
            if (row.CompletionState != AchievementCompletionState.Unresolved) value++;
            return value;
        }
    }
}
