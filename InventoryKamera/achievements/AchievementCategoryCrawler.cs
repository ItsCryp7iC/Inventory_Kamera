using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace InventoryKamera
{
    internal enum AchievementCategoryCrawlState { Complete, Incomplete, Cancelled, Failed }
    internal enum AchievementScrollProgressState
    {
        ScrollAdvanced,
        ReachedBottom,
        NoMovementUnknown,
        LostScreen,
        Cancelled,
    }

    internal sealed class AchievementCategoryCrawlContext
    {
        internal AchievementCategoryCrawlContext(
            string categoryName,
            string expectedCategorySignature = null,
            bool knownTraversalOrigin = false,
            bool requireVerifiedCategorySignature = false)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
                throw new ArgumentException("A category name is required.", nameof(categoryName));
            CategoryName = categoryName.Trim();
            ExpectedCategorySignature = expectedCategorySignature?.Trim() ?? string.Empty;
            KnownTraversalOrigin = knownTraversalOrigin;
            RequireVerifiedCategorySignature = requireVerifiedCategorySignature;
        }

        internal string CategoryName { get; }
        internal string ExpectedCategorySignature { get; }
        internal bool KnownTraversalOrigin { get; }
        internal bool RequireVerifiedCategorySignature { get; }
    }

    internal sealed class AchievementCategoryCrawlOptions
    {
        internal const int MeasuredPixelsPerPhysicalNotch = 18;
        internal const int ProvisionalTargetDisplacementPixels = 378;
        internal static AchievementCategoryCrawlOptions Default { get; } = new();

        internal AchievementCategoryCrawlOptions(
            int maximumCapturedFrames = 100,
            int maximumScrollActions = 100,
            int maximumConsecutiveUncertainFrames = 3,
            int maximumNoMovementRecoveryAttempts = 1,
            int scrollSettleMilliseconds = 350,
            int wheelDeltaPerNotch = -120,
            int wheelNotchesPerBatch = 21,
            int stabilizationPollMilliseconds = 75,
            int maximumStabilizationCaptures = 4)
        {
            MaximumCapturedFrames = Positive(maximumCapturedFrames, nameof(maximumCapturedFrames));
            MaximumScrollActions = Positive(maximumScrollActions, nameof(maximumScrollActions));
            MaximumConsecutiveUncertainFrames = NonNegative(
                maximumConsecutiveUncertainFrames, nameof(maximumConsecutiveUncertainFrames));
            MaximumNoMovementRecoveryAttempts = NonNegative(
                maximumNoMovementRecoveryAttempts, nameof(maximumNoMovementRecoveryAttempts));
            ScrollSettleMilliseconds = NonNegative(
                scrollSettleMilliseconds, nameof(scrollSettleMilliseconds));
            if (wheelDeltaPerNotch != -120)
                throw new ArgumentOutOfRangeException(nameof(wheelDeltaPerNotch),
                    "Each downward wheel event must remain one standard -120 notch.");
            if (wheelNotchesPerBatch < 1 || wheelNotchesPerBatch > 24)
                throw new ArgumentOutOfRangeException(nameof(wheelNotchesPerBatch));
            WheelDeltaPerNotch = wheelDeltaPerNotch;
            WheelNotchesPerBatch = wheelNotchesPerBatch;
            StabilizationPollMilliseconds = NonNegative(
                stabilizationPollMilliseconds, nameof(stabilizationPollMilliseconds));
            if (maximumStabilizationCaptures < 1 || maximumStabilizationCaptures > 8)
                throw new ArgumentOutOfRangeException(nameof(maximumStabilizationCaptures));
            MaximumStabilizationCaptures = maximumStabilizationCaptures;
        }

        internal int MaximumCapturedFrames { get; }
        internal int MaximumScrollActions { get; }
        internal int MaximumConsecutiveUncertainFrames { get; }
        internal int MaximumNoMovementRecoveryAttempts { get; }
        internal int ScrollSettleMilliseconds { get; }
        internal int WheelDeltaPerNotch { get; }
        internal int WheelNotchesPerBatch { get; }
        internal int StabilizationPollMilliseconds { get; }
        internal int MaximumStabilizationCaptures { get; }
        internal AchievementListScrollRequest ScrollRequest => new(
            WheelDeltaPerNotch,
            WheelNotchesPerBatch,
            MeasuredPixelsPerPhysicalNotch * WheelNotchesPerBatch);

        private static int Positive(int value, string name) =>
            value > 0 ? value : throw new ArgumentOutOfRangeException(name);
        private static int NonNegative(int value, string name) =>
            value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
    }

    internal sealed class AchievementCategoryFrameEvidence
    {
        internal AchievementCategoryFrameEvidence(
            bool topAtExtent,
            bool bottomAtExtent,
            string categorySignature,
            bool categorySignatureVerified,
            string diagnostics)
        {
            TopAtExtent = topAtExtent;
            BottomAtExtent = bottomAtExtent;
            CategorySignature = categorySignature ?? string.Empty;
            CategorySignatureVerified = categorySignatureVerified;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal bool TopAtExtent { get; }
        internal bool BottomAtExtent { get; }
        internal string CategorySignature { get; }
        internal bool CategorySignatureVerified { get; }
        internal string Diagnostics { get; }
    }

    internal interface IAchievementCategoryFrameEvidenceDetector
    {
        AchievementCategoryFrameEvidence Detect(Bitmap screenshot);
    }

    /// <summary>
    /// Fail-closed fallback for callers that do not have a calibrated frame-evidence detector.
    /// It intentionally supplies no positional or category proof.
    /// </summary>
    internal sealed class UncalibratedAchievementCategoryFrameEvidenceDetector :
        IAchievementCategoryFrameEvidenceDetector
    {
        public AchievementCategoryFrameEvidence Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            return new AchievementCategoryFrameEvidence(
                topAtExtent: false,
                bottomAtExtent: false,
                categorySignature: string.Empty,
                categorySignatureVerified: false,
                diagnostics: "Right-list scrollbar/category thresholds are not live-calibrated; no positional proof was asserted.");
        }
    }

    internal sealed class AchievementListScrollCommandResult
    {
        internal AchievementListScrollCommandResult(
            bool succeeded,
            bool foregroundVerified,
            string failureReason,
            string diagnostics,
            int wheelEventsSent = 0,
            bool cancelled = false)
        {
            Succeeded = succeeded;
            ForegroundVerified = foregroundVerified;
            FailureReason = failureReason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
            WheelEventsSent = Math.Max(0, wheelEventsSent);
            Cancelled = cancelled;
        }

        internal bool Succeeded { get; }
        internal bool ForegroundVerified { get; }
        internal string FailureReason { get; }
        internal string Diagnostics { get; }
        internal int WheelEventsSent { get; }
        internal bool Cancelled { get; }
    }

    internal sealed class AchievementListScrollRequest
    {
        internal AchievementListScrollRequest(
            int wheelDeltaPerNotch,
            int notchCount,
            int provisionalTargetDisplacementPixels)
        {
            if (wheelDeltaPerNotch != -120)
                throw new ArgumentOutOfRangeException(nameof(wheelDeltaPerNotch));
            if (notchCount < 1 || notchCount > 24)
                throw new ArgumentOutOfRangeException(nameof(notchCount));
            WheelDeltaPerNotch = wheelDeltaPerNotch;
            NotchCount = notchCount;
            ProvisionalTargetDisplacementPixels = provisionalTargetDisplacementPixels;
        }

        internal int WheelDeltaPerNotch { get; }
        internal int NotchCount { get; }
        internal int LogicalWheelDelta => WheelDeltaPerNotch * NotchCount;
        internal int ProvisionalTargetDisplacementPixels { get; }
    }

    internal interface IAchievementListScrollInput
    {
        AchievementListScrollCommandResult Scroll(
            AchievementListScrollRequest request,
            CancellationToken cancellationToken);
    }

    internal readonly struct AchievementViewportFingerprint : IEquatable<AchievementViewportFingerprint>
    {
        private readonly byte[] rowProfile;

        private AchievementViewportFingerprint(ulong value, byte[] rowProfile)
        {
            Value = value;
            this.rowProfile = rowProfile;
        }

        internal ulong Value { get; }

        internal static AchievementViewportFingerprint Create(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            Rectangle viewport = AchievementListRegions.For(screenshot.Size).RowViewport;
            int cursorX = (int)Math.Round((screenshot.Width - 1) * AchievementWindowsListScrollInput.ScrollTargetX);
            int cursorY = (int)Math.Round((screenshot.Height - 1) * AchievementWindowsListScrollInput.ScrollTargetY);
            var profile = new byte[viewport.Height];
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int y = viewport.Top; y < viewport.Bottom; y++)
            {
                int total = 0;
                int samples = 0;
                for (int x = viewport.Left; x < viewport.Right; x += 12)
                {
                    if (Math.Abs(x - cursorX) <= 36 && Math.Abs(y - cursorY) <= 36) continue;
                    Color pixel = screenshot.GetPixel(x, y);
                    total += ((54 * pixel.R) + (183 * pixel.G) + (19 * pixel.B)) >> 8;
                    samples++;
                }
                byte average = (byte)(samples == 0 ? 0 : total / samples);
                profile[y - viewport.Top] = average;
                hash ^= average;
                hash *= prime;
            }
            return new AchievementViewportFingerprint(hash, profile);
        }

        internal AchievementViewportComparison CompareTo(
            AchievementViewportFingerprint other,
            int maximumDisplacementPixels = 500)
        {
            if (rowProfile == null || other.rowProfile == null || rowProfile.Length != other.rowProfile.Length)
                return AchievementViewportComparison.Unavailable;
            int maximum = Math.Min(maximumDisplacementPixels, rowProfile.Length / 2);
            int bestDisplacement = 0;
            double zeroDifference = Difference(rowProfile, other.rowProfile, 0);
            double bestDifference = zeroDifference;
            for (int displacement = -maximum; displacement <= maximum; displacement++)
            {
                if (displacement == 0) continue;
                double difference = Difference(rowProfile, other.rowProfile, displacement);
                if (difference + 0.0001 < bestDifference)
                {
                    bestDifference = difference;
                    bestDisplacement = displacement;
                }
            }
            return new AchievementViewportComparison(
                available: true,
                displacementPixels: bestDisplacement,
                alignedMeanDifference: bestDifference,
                zeroShiftMeanDifference: zeroDifference);
        }

        private static double Difference(byte[] left, byte[] right, int displacement)
        {
            long difference = 0;
            int samples = 0;
            for (int index = 0; index < left.Length; index++)
            {
                int other = index + displacement;
                if (other < 0 || other >= right.Length) continue;
                difference += Math.Abs(left[index] - right[other]);
                samples++;
            }
            return samples == 0 ? double.MaxValue : difference / (double)samples;
        }

        public bool Equals(AchievementViewportFingerprint other) => Value == other.Value;
        public override bool Equals(object obj) => obj is AchievementViewportFingerprint other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString("X16", CultureInfo.InvariantCulture);
    }

    internal readonly struct AchievementViewportComparison
    {
        internal static AchievementViewportComparison Unavailable { get; } =
            new(false, 0, double.MaxValue, double.MaxValue);

        internal AchievementViewportComparison(bool available, int displacementPixels,
            double alignedMeanDifference, double zeroShiftMeanDifference)
        {
            Available = available;
            DisplacementPixels = displacementPixels;
            AlignedMeanDifference = alignedMeanDifference;
            ZeroShiftMeanDifference = zeroShiftMeanDifference;
        }

        internal bool Available { get; }
        internal int DisplacementPixels { get; }
        internal double AlignedMeanDifference { get; }
        internal double ZeroShiftMeanDifference { get; }
        internal bool Stable => Available && Math.Abs(DisplacementPixels) <= 1 && ZeroShiftMeanDifference <= 1.0;
        internal bool ReliableMovement => Available && Math.Abs(DisplacementPixels) >= 2 &&
            AlignedMeanDifference <= 2.0 && ZeroShiftMeanDifference - AlignedMeanDifference >= 1.0;
    }

    internal sealed class AchievementViewportMotion
    {
        private AchievementViewportMotion(AchievementViewportComparison visual,
            double? rowDisplacementPixels, int sharedCanonicalRows, bool contradictory)
        {
            Visual = visual;
            RowDisplacementPixels = rowDisplacementPixels;
            SharedCanonicalRows = sharedCanonicalRows;
            Contradictory = contradictory;
        }

        internal AchievementViewportComparison Visual { get; }
        internal double? RowDisplacementPixels { get; }
        internal int SharedCanonicalRows { get; }
        internal bool Contradictory { get; }
        internal bool Stable => !Contradictory && Visual.Stable &&
            (!RowDisplacementPixels.HasValue || Math.Abs(RowDisplacementPixels.Value) <= 1.0);
        internal bool Advanced => !Contradictory &&
            ((RowDisplacementPixels.HasValue && Math.Abs(RowDisplacementPixels.Value) >= 2.0) ||
             Visual.ReliableMovement);

        internal static AchievementViewportMotion Compare(
            AchievementBulkFrameResult previous,
            AchievementViewportFingerprint previousFingerprint,
            AchievementBulkFrameResult current,
            AchievementViewportFingerprint currentFingerprint,
            int captureHeight)
        {
            AchievementViewportComparison visual = previousFingerprint.CompareTo(currentFingerprint);
            Dictionary<string, double> priorPositions = previous.VisibleRows
                .Where(row => row.Fingerprint.SuitableForDeduplication)
                .GroupBy(row => row.Fingerprint.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.First().NormalizedVerticalPosition,
                    StringComparer.Ordinal);
            double[] rowMovements = current.VisibleRows
                .Where(row => row.Fingerprint.SuitableForDeduplication &&
                    priorPositions.ContainsKey(row.Fingerprint.Value))
                .Select(row => (row.NormalizedVerticalPosition -
                    priorPositions[row.Fingerprint.Value]) * captureHeight)
                .OrderBy(value => value)
                .ToArray();
            double? rowMovement = rowMovements.Length == 0
                ? null
                : rowMovements[rowMovements.Length / 2];
            bool rowMoved = rowMovement.HasValue && Math.Abs(rowMovement.Value) >= 2.0;
            bool contradictory = rowMovement.HasValue &&
                ((rowMoved && visual.Stable) ||
                 (visual.ReliableMovement &&
                  (Math.Sign(rowMovement.Value) != Math.Sign(visual.DisplacementPixels) ||
                   Math.Abs(rowMovement.Value - visual.DisplacementPixels) > 4.0)));
            return new AchievementViewportMotion(
                visual, rowMovement, rowMovements.Length, contradictory);
        }

        public override string ToString() =>
            $"visualShift={Visual.DisplacementPixels}px; visualAlignedDifference={Visual.AlignedMeanDifference:0.000}; " +
            $"visualZeroDifference={Visual.ZeroShiftMeanDifference:0.000}; rowShift=" +
            (RowDisplacementPixels.HasValue ? $"{RowDisplacementPixels.Value:0.0}px" : "unavailable") +
            $"; sharedCanonicalRows={SharedCanonicalRows}; contradictory={Contradictory}";
    }

    internal sealed class AchievementRowFrameObservation
    {
        internal AchievementRowFrameObservation(int frameIndex, AchievementVisibleRow row)
        {
            FrameIndex = frameIndex;
            Row = row ?? throw new ArgumentNullException(nameof(row));
        }

        internal int FrameIndex { get; }
        internal AchievementVisibleRow Row { get; }
    }

    internal sealed class AchievementObservedRow
    {
        internal AchievementObservedRow(
            string key,
            bool canonical,
            IEnumerable<AchievementRowFrameObservation> observations,
            AchievementVisibleRow selectedObservation,
            bool conflicting)
        {
            Key = key ?? string.Empty;
            Canonical = canonical;
            Observations = new ReadOnlyCollection<AchievementRowFrameObservation>(
                (observations ?? Array.Empty<AchievementRowFrameObservation>()).ToArray());
            SelectedObservation = selectedObservation;
            Conflicting = conflicting;
            FirstObservedFrame = Observations.Count == 0 ? -1 : Observations.Min(value => value.FrameIndex);
            LastObservedFrame = Observations.Count == 0 ? -1 : Observations.Max(value => value.FrameIndex);
            EverFullyVisible = Observations.Any(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Complete);
        }

        internal string Key { get; }
        internal bool Canonical { get; }
        internal IReadOnlyList<AchievementRowFrameObservation> Observations { get; }
        internal AchievementVisibleRow SelectedObservation { get; }
        internal int FirstObservedFrame { get; }
        internal int LastObservedFrame { get; }
        internal int ObservationCount => Observations.Count;
        internal bool EverFullyVisible { get; }
        internal bool Conflicting { get; }
        internal bool Resolved => Canonical && !Conflicting && SelectedObservation != null;
    }

    internal sealed class AchievementCategoryCrawlProgress
    {
        internal AchievementCategoryCrawlProgress(int frameCount, int scrollActions,
            int uniqueIdentifiedRows, int unresolvedObservations,
            int ambiguousGroups, int conflictingRows,
            int repeatedObservations, int partialRowsRecovered,
            int partialRowsOutstanding, int unobservedMovementGaps,
            AchievementScrollProgressState scrollState,
            AchievementCategoryFrameObservation frameObservation = null,
            AchievementScrollObservation scrollObservation = null)
        {
            FrameCount = frameCount;
            ScrollActions = scrollActions;
            UniqueIdentifiedRows = uniqueIdentifiedRows;
            UnresolvedObservations = unresolvedObservations;
            AmbiguousGroups = ambiguousGroups;
            ConflictingRows = conflictingRows;
            RepeatedObservations = repeatedObservations;
            PartialRowsRecovered = partialRowsRecovered;
            PartialRowsOutstanding = partialRowsOutstanding;
            UnobservedMovementGaps = unobservedMovementGaps;
            ScrollState = scrollState;
            FrameObservation = frameObservation;
            ScrollObservation = scrollObservation;
        }

        internal int FrameCount { get; }
        internal int ScrollActions { get; }
        internal int UniqueIdentifiedRows { get; }
        internal int UnresolvedObservations { get; }
        internal int AmbiguousGroups { get; }
        internal int ConflictingRows { get; }
        internal int RepeatedObservations { get; }
        internal int PartialRowsRecovered { get; }
        internal int PartialRowsOutstanding { get; }
        internal int UnobservedMovementGaps { get; }
        internal AchievementScrollProgressState ScrollState { get; }
        internal AchievementCategoryFrameObservation FrameObservation { get; }
        internal AchievementScrollObservation ScrollObservation { get; }
    }

    /// <summary>
    /// Bitmap-free evidence retained for diagnostic presentation. Raw captures never leave the
    /// crawler's processing lifetime.
    /// </summary>
    internal sealed class AchievementCategoryFrameObservation
    {
        internal AchievementCategoryFrameObservation(int frameIndex,
            AchievementScreenState screenState, int detectedRows,
            AchievementCategoryFrameEvidence evidence)
        {
            FrameIndex = frameIndex;
            ScreenState = screenState;
            DetectedRows = detectedRows;
            CategorySignature = evidence?.CategorySignature ?? string.Empty;
            CategorySignatureVerified = evidence?.CategorySignatureVerified == true;
            TopAtExtent = evidence?.TopAtExtent == true;
            BottomAtExtent = evidence?.BottomAtExtent == true;
            EvidenceDiagnostics = evidence?.Diagnostics ?? string.Empty;
        }

        internal int FrameIndex { get; }
        internal AchievementScreenState ScreenState { get; }
        internal int DetectedRows { get; }
        internal string CategorySignature { get; }
        internal bool CategorySignatureVerified { get; }
        internal bool TopAtExtent { get; }
        internal bool BottomAtExtent { get; }
        internal string EvidenceDiagnostics { get; }
    }

    internal sealed class AchievementCategoryCrawlTiming
    {
        internal AchievementCategoryCrawlTiming(TimeSpan capture, TimeSpan frameProcessing,
            TimeSpan scrolling, TimeSpan settling, TimeSpan deduplication, TimeSpan total)
        {
            Capture = capture;
            FrameProcessing = frameProcessing;
            Scrolling = scrolling;
            Settling = settling;
            Deduplication = deduplication;
            Total = total;
        }

        internal TimeSpan Capture { get; }
        internal TimeSpan FrameProcessing { get; }
        internal TimeSpan Scrolling { get; }
        internal TimeSpan Settling { get; }
        internal TimeSpan Deduplication { get; }
        internal TimeSpan Total { get; }
    }

    internal sealed class AchievementScrollObservation
    {
        internal AchievementScrollObservation(int batchIndex,
            AchievementListScrollRequest request,
            AchievementViewportMotion motion,
            AchievementListScrollCommandResult command = null)
        {
            BatchIndex = batchIndex;
            RequestedNotches = request?.NotchCount ?? 0;
            RequestedLogicalWheelDelta = request?.LogicalWheelDelta ?? 0;
            ProvisionalTargetDisplacementPixels =
                request?.ProvisionalTargetDisplacementPixels ?? 0;
            ObservedVisualDisplacementPixels = motion?.Visual.DisplacementPixels;
            ObservedRowDisplacementPixels = motion?.RowDisplacementPixels;
            MovementVerified = motion?.Advanced == true;
            ViewportStable = motion?.Stable == true;
            Contradictory = motion?.Contradictory == true;
            SharedCanonicalRows = motion?.SharedCanonicalRows ?? 0;
            OverlapVerified = motion != null &&
                (motion.Stable || motion.SharedCanonicalRows > 0);
            WheelEventsSent = command?.WheelEventsSent ?? 0;
            InputSucceeded = command?.Succeeded ?? true;
            InputCancelled = command?.Cancelled == true;
            InputFailureReason = command?.FailureReason ?? string.Empty;
        }

        internal int BatchIndex { get; }
        internal int RequestedNotches { get; }
        internal int RequestedLogicalWheelDelta { get; }
        internal int ProvisionalTargetDisplacementPixels { get; }
        internal int? ObservedVisualDisplacementPixels { get; }
        internal double? ObservedRowDisplacementPixels { get; }
        internal bool MovementVerified { get; }
        internal bool ViewportStable { get; }
        internal bool Contradictory { get; }
        internal int SharedCanonicalRows { get; }
        internal bool OverlapVerified { get; }
        internal int WheelEventsSent { get; }
        internal bool InputSucceeded { get; }
        internal bool InputCancelled { get; }
        internal string InputFailureReason { get; }
    }

    internal sealed class AchievementCategoryCrawlResult
    {
        internal AchievementCategoryCrawlResult(
            AchievementCategoryCrawlContext context,
            AchievementCategoryCrawlState state,
            string completenessEvidence,
            int processedFrames,
            int scrollActions,
            IEnumerable<AchievementObservedRow> observedRows,
            IEnumerable<AchievementScrollObservation> scrollObservations,
            IEnumerable<AchievementCategoryFrameObservation> frameObservations,
            AchievementScrollProgressState finalScrollState,
            string reason,
            AchievementCategoryCrawlTiming timing,
            string diagnostics)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            State = state;
            CompletenessEvidence = completenessEvidence ?? string.Empty;
            ProcessedFrames = processedFrames;
            ScrollActions = scrollActions;
            ObservedRows = new ReadOnlyCollection<AchievementObservedRow>(
                (observedRows ?? Array.Empty<AchievementObservedRow>()).ToArray());
            ScrollObservations = new ReadOnlyCollection<AchievementScrollObservation>(
                (scrollObservations ?? Array.Empty<AchievementScrollObservation>()).ToArray());
            FrameObservations = new ReadOnlyCollection<AchievementCategoryFrameObservation>(
                (frameObservations ?? Array.Empty<AchievementCategoryFrameObservation>()).ToArray());
            IdentifiedRows = new ReadOnlyCollection<AchievementObservedRow>(ObservedRows
                .Where(row => row.Resolved).ToArray());
            UnresolvedRows = new ReadOnlyCollection<AchievementObservedRow>(ObservedRows
                .Where(row => !row.Resolved).ToArray());
            ConflictingRows = new ReadOnlyCollection<AchievementObservedRow>(ObservedRows
                .Where(row => row.Conflicting).ToArray());
            AmbiguousRows = new ReadOnlyCollection<AchievementObservedRow>(ObservedRows
                .Where(row => row.Observations.Any(value =>
                    value.Row.IdentityState == AchievementIdentityState.Ambiguous)).ToArray());
            AmbiguousGroupCount = AmbiguousRows
                .Select(row => AmbiguousGroupKey(row.SelectedObservation ??
                    row.Observations.Select(value => value.Row).FirstOrDefault()))
                .Distinct(StringComparer.Ordinal)
                .Count();
            CompleteRowObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Complete));
            PartialRowObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Partial));
            UncertainRowObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Uncertain));
            RepeatedObservations = ObservedRows.Where(row => row.Canonical)
                .Sum(row => Math.Max(0, row.ObservationCount - 1));
            PartialRowsRecovered = ObservedRows.Count(row => row.Canonical && row.EverFullyVisible &&
                row.Observations.Any(value => value.Row.Visibility != AchievementVisibleRowVisibility.Complete));
            PartialRowsOutstanding = ObservedRows.Count(row => !row.EverFullyVisible &&
                row.Observations.Any(value =>
                    value.Row.Visibility != AchievementVisibleRowVisibility.Complete));
            OverlapVerifiedScrolls = ScrollObservations.Count(value => value.OverlapVerified);
            UnobservedMovementGaps = ScrollObservations.Count(value =>
                value.MovementVerified && !value.OverlapVerified);
            FinalScrollState = finalScrollState;
            Reason = reason ?? string.Empty;
            Timing = timing ?? throw new ArgumentNullException(nameof(timing));
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementCategoryCrawlContext Context { get; }
        internal AchievementCategoryCrawlState State { get; }
        internal bool IsComplete => State == AchievementCategoryCrawlState.Complete;
        internal string CompletenessEvidence { get; }
        internal int ProcessedFrames { get; }
        internal int ScrollActions { get; }
        internal IReadOnlyList<AchievementObservedRow> ObservedRows { get; }
        internal IReadOnlyList<AchievementScrollObservation> ScrollObservations { get; }
        internal IReadOnlyList<AchievementCategoryFrameObservation> FrameObservations { get; }
        internal IReadOnlyList<AchievementObservedRow> IdentifiedRows { get; }
        internal IReadOnlyList<AchievementObservedRow> UnresolvedRows { get; }
        internal IReadOnlyList<AchievementObservedRow> ConflictingRows { get; }
        internal IReadOnlyList<AchievementObservedRow> AmbiguousRows { get; }
        internal int AmbiguousGroupCount { get; }
        internal int CompleteRowObservations { get; }
        internal int PartialRowObservations { get; }
        internal int UncertainRowObservations { get; }
        internal int RepeatedObservations { get; }
        internal int PartialRowsRecovered { get; }
        internal int PartialRowsOutstanding { get; }
        internal int OverlapVerifiedScrolls { get; }
        internal int UnobservedMovementGaps { get; }
        internal bool RecognitionCoverageSufficient => IsComplete &&
            UnresolvedRows.Count == 0 && AmbiguousRows.Count == 0 &&
            ConflictingRows.Count == 0 && PartialRowsOutstanding == 0 &&
            UnobservedMovementGaps == 0;
        internal AchievementScrollProgressState FinalScrollState { get; }
        internal string Reason { get; }
        internal AchievementCategoryCrawlTiming Timing { get; }
        internal string Diagnostics { get; }

        private static string AmbiguousGroupKey(AchievementVisibleRow row) => row == null
            ? "unknown"
            : row.NormalizedTitle + ":" + string.Join(",", row.CandidateIds.OrderBy(value => value));
    }

    internal interface IAchievementCategoryCrawler
    {
        AchievementCategoryCrawlResult Crawl(
            AchievementCategoryCrawlContext context,
            Bitmap startingFrame,
            CancellationToken cancellationToken,
            IProgress<AchievementCategoryCrawlProgress> progress = null,
            TimeSpan? startingCaptureDuration = null);
    }

    internal sealed class AchievementCategoryCrawler : IAchievementCategoryCrawler
    {
        private readonly IAchievementListFrameReader frameReader;
        private readonly IGameScreenCapture screenCapture;
        private readonly IAchievementListScrollInput scrollInput;
        private readonly IAchievementCategoryFrameEvidenceDetector frameEvidenceDetector;
        private readonly AchievementCategoryCrawlOptions options;
        private readonly Action<int, CancellationToken> wait;

        internal AchievementCategoryCrawler(
            IAchievementListFrameReader frameReader,
            IGameScreenCapture screenCapture,
            IAchievementListScrollInput scrollInput,
            IAchievementCategoryFrameEvidenceDetector frameEvidenceDetector,
            AchievementCategoryCrawlOptions options = null,
            Action<int, CancellationToken> wait = null)
        {
            this.frameReader = frameReader ?? throw new ArgumentNullException(nameof(frameReader));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.scrollInput = scrollInput ?? throw new ArgumentNullException(nameof(scrollInput));
            this.frameEvidenceDetector = frameEvidenceDetector ?? throw new ArgumentNullException(nameof(frameEvidenceDetector));
            this.options = options ?? AchievementCategoryCrawlOptions.Default;
            this.wait = wait ?? WaitWithCancellation;
        }

        public AchievementCategoryCrawlResult Crawl(
            AchievementCategoryCrawlContext context,
            Bitmap startingFrame,
            CancellationToken cancellationToken,
            IProgress<AchievementCategoryCrawlProgress> progress = null,
            TimeSpan? startingCaptureDuration = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (startingFrame == null) throw new ArgumentNullException(nameof(startingFrame));

            Stopwatch total = Stopwatch.StartNew();
            TimeSpan captureTime = startingCaptureDuration ?? TimeSpan.Zero;
            TimeSpan processingTime = TimeSpan.Zero;
            TimeSpan scrollingTime = TimeSpan.Zero;
            TimeSpan settlingTime = TimeSpan.Zero;
            TimeSpan deduplicationTime = TimeSpan.Zero;
            var aggregate = new ObservationAccumulator();
            var scrollObservations = new List<AchievementScrollObservation>();
            var frameObservations = new List<AchievementCategoryFrameObservation>();
            var diagnostics = new List<string>();
            int frames = 0;
            int scrolls = 0;
            int uncertainFrames = 0;
            int noMovementAttempts = 0;
            string stableCategorySignature = context.ExpectedCategorySignature;
            AchievementScrollProgressState scrollState = AchievementScrollProgressState.NoMovementUnknown;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FrameSnapshot previous = ProcessFrame(startingFrame, 0, startingCaptureDuration ?? TimeSpan.Zero,
                    aggregate, ref processingTime, ref deduplicationTime);
                frameObservations.Add(Observe(previous, 0));
                frames = 1;
                diagnostics.Add(previous.Diagnostics);
                if (!TryValidateFrame(context, previous, ref stableCategorySignature, firstFrame: true,
                    out AchievementCategoryCrawlState invalidState, out string invalidReason))
                    return Finish(invalidState, invalidReason, string.Empty);
                if (!context.KnownTraversalOrigin && !previous.Evidence.TopAtExtent)
                    return Finish(AchievementCategoryCrawlState.Incomplete,
                        "The category crawl did not begin from a positively established top or known traversal origin.",
                        "startPosition=unsupported");

                uncertainFrames = IsUncertain(previous.Result) ? 1 : 0;
                if (uncertainFrames > options.MaximumConsecutiveUncertainFrames)
                    return Finish(AchievementCategoryCrawlState.Incomplete,
                        "The starting frame exceeded the uncertain-frame safety limit.",
                        "limit=consecutive-uncertain-frames");
                progress?.Report(Progress(aggregate, frames, scrolls, scrollState,
                    frameObservations[^1], null, scrollObservations));

                while (frames < options.MaximumCapturedFrames && scrolls < options.MaximumScrollActions)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Stopwatch part = Stopwatch.StartNew();
                    AchievementListScrollCommandResult command;
                    try
                    {
                        command = scrollInput.Scroll(options.ScrollRequest, cancellationToken);
                    }
                    catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                    {
                        part.Stop();
                        scrollingTime += part.Elapsed;
                        scrolls++;
                        int wheelEventsSent = ex.Data["AchievementWheelEventsSent"] is int sent
                            ? sent
                            : 0;
                        var cancelledCommand = new AchievementListScrollCommandResult(
                            succeeded: false,
                            foregroundVerified: false,
                            failureReason: ex.Message,
                            diagnostics: $"cancelled=true; wheelEventsSent={wheelEventsSent}",
                            wheelEventsSent: wheelEventsSent,
                            cancelled: true);
                        scrollObservations.Add(new AchievementScrollObservation(
                            scrolls, options.ScrollRequest, null, cancelledCommand));
                        diagnostics.Add($"scroll={scrolls}; {cancelledCommand.Diagnostics}");
                        throw;
                    }
                    part.Stop();
                    scrollingTime += part.Elapsed;
                    scrolls++;
                    diagnostics.Add($"scroll={scrolls}; succeeded={command.Succeeded}; foregroundVerified={command.ForegroundVerified}; {command.Diagnostics}");
                    scrollObservations.Add(new AchievementScrollObservation(
                        scrolls, options.ScrollRequest, null, command));
                    if (command.Cancelled || cancellationToken.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    if (!command.Succeeded || !command.ForegroundVerified)
                    {
                        return Finish(AchievementCategoryCrawlState.Failed,
                            command.FailureReason.Length == 0 ? "Achievement-list scrolling was not safely performed." : command.FailureReason,
                            string.Empty);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    Bitmap captured = null;
                    try
                    {
                        part.Restart();
                        wait(options.ScrollSettleMilliseconds, cancellationToken);
                        part.Stop();
                        settlingTime += part.Elapsed;
                        cancellationToken.ThrowIfCancellationRequested();
                        StabilizedCapture stabilized = CaptureStabilizedFrame(
                            cancellationToken, ref captureTime, ref settlingTime);
                        captured = stabilized.Frame;
                        diagnostics.Add($"scroll={scrolls}; {stabilized.Diagnostics}");
                        if (!stabilized.Stable)
                            return Finish(AchievementCategoryCrawlState.Incomplete,
                                "The post-scroll viewport did not stabilize within the bounded capture limit.",
                                "stabilization=unresolved");

                        FrameSnapshot current = ProcessFrame(captured, frames, stabilized.FinalCaptureDuration,
                            aggregate, ref processingTime, ref deduplicationTime);
                        frameObservations.Add(Observe(current, frames));
                        frames++;
                        diagnostics.Add(current.Diagnostics);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!TryValidateFrame(context, current, ref stableCategorySignature, firstFrame: false,
                            out invalidState, out invalidReason))
                        {
                            scrollState = AchievementScrollProgressState.LostScreen;
                            return Finish(invalidState, invalidReason, string.Empty);
                        }

                        uncertainFrames = IsUncertain(current.Result) ? uncertainFrames + 1 : 0;
                        if (uncertainFrames > options.MaximumConsecutiveUncertainFrames)
                            return Finish(AchievementCategoryCrawlState.Incomplete,
                                "The maximum consecutive uncertain-frame limit was reached.",
                                "limit=consecutive-uncertain-frames");

                        AchievementViewportMotion motion = AchievementViewportMotion.Compare(
                            previous.Result, previous.Fingerprint,
                            current.Result, current.Fingerprint,
                            captured.Height);
                        var scrollObservation = new AchievementScrollObservation(
                            scrolls, options.ScrollRequest, motion, command);
                        scrollObservations[^1] = scrollObservation;
                        diagnostics.Add($"scroll={scrolls}; observedMotion: {motion}");
                        if (motion.Contradictory)
                            return Finish(AchievementCategoryCrawlState.Incomplete,
                                "Visual and canonical-row displacement evidence contradicted each other.",
                                "movementEvidence=contradictory");
                        bool viewportStable = motion.Stable;
                        bool layoutStable = StringComparer.Ordinal.Equals(current.LayoutFingerprint, previous.LayoutFingerprint);
                        bool recognizableFinalRow = HasRecognizableFinalRow(current.Result);
                        bool bottomProof = viewportStable && layoutStable && recognizableFinalRow &&
                            current.Evidence.BottomAtExtent && previous.Evidence.BottomAtExtent;
                        if (bottomProof)
                        {
                            scrollState = AchievementScrollProgressState.ReachedBottom;
                            progress?.Report(Progress(aggregate, frames, scrolls, scrollState,
                                frameObservations[^1], scrollObservation, scrollObservations));
                            return Finish(AchievementCategoryCrawlState.Complete,
                                "The right-list bottom extent, stable viewport, stable row layout, and recognizable final-row evidence agreed after a bounded scroll.",
                                $"bottomAtExtent=true; viewportStable=true; layoutStable=true; finalRowRecognized=true; fingerprint={current.Fingerprint}");
                        }

                        if (viewportStable)
                        {
                            scrollState = AchievementScrollProgressState.NoMovementUnknown;
                            noMovementAttempts++;
                            if (noMovementAttempts > options.MaximumNoMovementRecoveryAttempts)
                                return Finish(AchievementCategoryCrawlState.Incomplete,
                                    "The viewport did not move and independent bottom proof was unavailable.",
                                    "noMovementWithoutBottomProof=true");
                        }
                        else if (motion.Advanced)
                        {
                            scrollState = AchievementScrollProgressState.ScrollAdvanced;
                            noMovementAttempts = 0;
                        }
                        else
                        {
                            scrollState = AchievementScrollProgressState.NoMovementUnknown;
                            noMovementAttempts++;
                            if (noMovementAttempts > options.MaximumNoMovementRecoveryAttempts)
                                return Finish(AchievementCategoryCrawlState.Incomplete,
                                    "Actual viewport displacement could not be verified after the logical scroll request.",
                                    "movementEvidence=insufficient");
                        }

                        previous = current;
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(Progress(aggregate, frames, scrolls, scrollState,
                            frameObservations[^1], scrollObservation, scrollObservations));
                    }
                    finally
                    {
                        captured?.Dispose();
                    }
                }

                string limit = frames >= options.MaximumCapturedFrames
                    ? "maximum captured-frame limit"
                    : "maximum scroll-action limit";
                return Finish(AchievementCategoryCrawlState.Incomplete,
                    $"The {limit} was reached without positive bottom proof.",
                    "limitReached=true");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                scrollState = AchievementScrollProgressState.Cancelled;
                return Finish(AchievementCategoryCrawlState.Cancelled,
                    "The category crawl was cancelled; retained observations do not establish completeness.",
                    "cancelled=true");
            }
            catch (Exception ex)
            {
                return Finish(AchievementCategoryCrawlState.Failed,
                    $"The category crawl failed: {ex.GetType().Name}: {ex.Message}",
                    "exception=true");
            }

            AchievementCategoryCrawlResult Finish(
                AchievementCategoryCrawlState state,
                string reason,
                string completenessEvidence)
            {
                total.Stop();
                AchievementObservedRow[] rows = aggregate.Build();
                var timing = new AchievementCategoryCrawlTiming(captureTime, processingTime,
                    scrollingTime, settlingTime, deduplicationTime, total.Elapsed);
                diagnostics.Add($"state={state}; frames={frames}; scrolls={scrolls}; uniqueIdentified={rows.Count(row => row.Resolved)}; " +
                    $"unresolved={rows.Count(row => !row.Resolved)}; repeated={rows.Where(row => row.Canonical).Sum(row => Math.Max(0, row.ObservationCount - 1))}; " +
                    $"ambiguous={rows.Count(row => row.Observations.Any(value => value.Row.IdentityState == AchievementIdentityState.Ambiguous))}; " +
                    $"conflicting={rows.Count(row => row.Conflicting)}; partialOutstanding={rows.Count(row => !row.EverFullyVisible && row.Observations.Any(value => value.Row.Visibility != AchievementVisibleRowVisibility.Complete))}; " +
                    $"unobservedMovementGaps={scrollObservations.Count(value => value.MovementVerified && !value.OverlapVerified)}; " +
                    $"captureMs={captureTime.TotalMilliseconds:0.00}; processingMs={processingTime.TotalMilliseconds:0.00}; " +
                    $"scrollMs={scrollingTime.TotalMilliseconds:0.00}; settleMs={settlingTime.TotalMilliseconds:0.00}; " +
                    $"dedupMs={deduplicationTime.TotalMilliseconds:0.00}; totalMs={total.Elapsed.TotalMilliseconds:0.00}");
                return new AchievementCategoryCrawlResult(context, state, completenessEvidence,
                    frames, scrolls, rows, scrollObservations, frameObservations, scrollState, reason, timing,
                    string.Join(Environment.NewLine, diagnostics));
            }
        }

        private StabilizedCapture CaptureStabilizedFrame(
            CancellationToken cancellationToken,
            ref TimeSpan captureTime,
            ref TimeSpan settlingTime)
        {
            Bitmap previous = null;
            TimeSpan finalCaptureDuration = TimeSpan.Zero;
            try
            {
                for (int attempt = 1; attempt <= options.MaximumStabilizationCaptures; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (attempt > 1)
                    {
                        Stopwatch settling = Stopwatch.StartNew();
                        wait(options.StabilizationPollMilliseconds, cancellationToken);
                        settling.Stop();
                        settlingTime += settling.Elapsed;
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    Stopwatch capture = Stopwatch.StartNew();
                    Bitmap current = screenCapture.CaptureWindow();
                    capture.Stop();
                    captureTime += capture.Elapsed;
                    finalCaptureDuration = capture.Elapsed;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        current.Dispose();
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (previous == null && options.MaximumStabilizationCaptures == 1)
                        return new StabilizedCapture(current, true, finalCaptureDuration,
                            "stabilizationCaptures=1; stabilizationCheck=configured-bypass");
                    if (previous != null)
                    {
                        AchievementViewportComparison comparison =
                            AchievementViewportFingerprint.Create(previous).CompareTo(
                                AchievementViewportFingerprint.Create(current),
                                maximumDisplacementPixels: 48);
                        if (comparison.Stable)
                        {
                            previous.Dispose();
                            previous = null;
                            return new StabilizedCapture(current, true, finalCaptureDuration,
                                $"stabilizationCaptures={attempt}; stable=true; displacement={comparison.DisplacementPixels}px; difference={comparison.ZeroShiftMeanDifference:0.000}");
                        }
                    }

                    previous?.Dispose();
                    previous = current;
                }

                Bitmap last = previous;
                previous = null;
                return new StabilizedCapture(last, false, finalCaptureDuration,
                    $"stabilizationCaptures={options.MaximumStabilizationCaptures}; stable=false");
            }
            catch
            {
                previous?.Dispose();
                throw;
            }
        }

        private FrameSnapshot ProcessFrame(Bitmap screenshot, int frameIndex, TimeSpan captureDuration,
            ObservationAccumulator aggregate, ref TimeSpan processingTime, ref TimeSpan deduplicationTime)
        {
            Stopwatch part = Stopwatch.StartNew();
            AchievementCategoryFrameEvidence evidence = frameEvidenceDetector.Detect(screenshot);
            AchievementViewportFingerprint fingerprint = AchievementViewportFingerprint.Create(screenshot);
            AchievementBulkFrameResult result = frameReader.Read(screenshot, captureDuration);
            part.Stop();
            processingTime += part.Elapsed;
            string layout = string.Join("|", result.VisibleRows.Select(row =>
                $"{row.Visibility}:{row.Fingerprint.Value}:{row.IdentityState}:{row.CompletionState}"));
            part.Restart();
            aggregate.Add(frameIndex, result.VisibleRows);
            part.Stop();
            deduplicationTime += part.Elapsed;
            string diagnostics = $"frame={frameIndex}; screen={result.ScreenState}; rows={result.VisibleRows.Count}; " +
                $"fingerprint={fingerprint}; layout={layout}; categorySignature={evidence.CategorySignature}; " +
                $"categoryVerified={evidence.CategorySignatureVerified}; top={evidence.TopAtExtent}; bottom={evidence.BottomAtExtent}; {evidence.Diagnostics}";
            return new FrameSnapshot(result, evidence, fingerprint, layout, diagnostics);
        }

        private static bool TryValidateFrame(AchievementCategoryCrawlContext context,
            FrameSnapshot frame, ref string stableSignature, bool firstFrame,
            out AchievementCategoryCrawlState state, out string reason)
        {
            if (frame.Result.ScreenState != AchievementScreenState.Idle ||
                !frame.Result.IsKeyboardMouseIdle)
            {
                state = AchievementCategoryCrawlState.Failed;
                reason = firstFrame
                    ? $"The starting frame was not a supported ordinary Keyboard/Mouse Achievement list ({frame.Result.ScreenState})."
                    : $"The Keyboard/Mouse Achievement list was lost after scrolling ({frame.Result.ScreenState}).";
                return false;
            }

            if (context.ExpectedCategorySignature.Length > 0)
            {
                if (!frame.Evidence.CategorySignatureVerified ||
                    !StringComparer.Ordinal.Equals(context.ExpectedCategorySignature, frame.Evidence.CategorySignature))
                {
                    state = AchievementCategoryCrawlState.Failed;
                    reason = "The selected Achievement category signature was not positively preserved.";
                    return false;
                }
            }
            else if (context.RequireVerifiedCategorySignature &&
                !frame.Evidence.CategorySignatureVerified)
            {
                state = AchievementCategoryCrawlState.Failed;
                reason = firstFrame
                    ? "The selected Achievement category could not be positively verified in the starting frame."
                    : "The selected Achievement category could not be positively verified after scrolling.";
                return false;
            }
            else if (frame.Evidence.CategorySignatureVerified)
            {
                if (stableSignature.Length == 0) stableSignature = frame.Evidence.CategorySignature;
                else if (!StringComparer.Ordinal.Equals(stableSignature, frame.Evidence.CategorySignature))
                {
                    state = AchievementCategoryCrawlState.Failed;
                    reason = "The selected Achievement category changed unexpectedly during the crawl.";
                    return false;
                }
            }

            state = default;
            reason = string.Empty;
            return true;
        }

        private static bool IsUncertain(AchievementBulkFrameResult result) =>
            result.VisibleRows.Count == 0 || result.VisibleRows.All(row =>
                row.Visibility != AchievementVisibleRowVisibility.Complete ||
                row.IdentityState == AchievementIdentityState.Unresolved ||
                row.IdentityState == AchievementIdentityState.Ambiguous);

        private static bool HasRecognizableFinalRow(AchievementBulkFrameResult result) =>
            result.VisibleRows.OrderBy(row => row.NormalizedVerticalPosition).LastOrDefault() is AchievementVisibleRow row &&
            row.Visibility == AchievementVisibleRowVisibility.Complete &&
            row.Fingerprint.SuitableForDeduplication &&
            (row.IdentityState == AchievementIdentityState.Identified ||
             row.IdentityState == AchievementIdentityState.StageGroupIdentified);

        private static AchievementCategoryFrameObservation Observe(FrameSnapshot frame, int frameIndex) =>
            new(frameIndex, frame.Result.ScreenState, frame.Result.VisibleRows.Count, frame.Evidence);

        private static AchievementCategoryCrawlProgress Progress(ObservationAccumulator aggregate,
            int frames, int scrolls, AchievementScrollProgressState state,
            AchievementCategoryFrameObservation frameObservation,
            AchievementScrollObservation scrollObservation,
            IEnumerable<AchievementScrollObservation> scrollObservations)
        {
            AchievementObservedRow[] rows = aggregate.Build();
            AchievementScrollObservation[] movements =
                (scrollObservations ?? Array.Empty<AchievementScrollObservation>()).ToArray();
            return new AchievementCategoryCrawlProgress(frames, scrolls,
                rows.Count(row => row.Resolved), rows.Count(row => !row.Resolved),
                rows.Where(row => row.Observations.Any(value =>
                        value.Row.IdentityState == AchievementIdentityState.Ambiguous))
                    .Select(row => (row.SelectedObservation ?? row.Observations[0].Row).NormalizedTitle + ":" +
                        string.Join(",", (row.SelectedObservation ?? row.Observations[0].Row).CandidateIds.OrderBy(value => value)))
                    .Distinct(StringComparer.Ordinal).Count(),
                rows.Count(row => row.Conflicting),
                rows.Where(row => row.Canonical).Sum(row => Math.Max(0, row.ObservationCount - 1)),
                rows.Count(row => row.Canonical && row.EverFullyVisible &&
                    row.Observations.Any(value => value.Row.Visibility != AchievementVisibleRowVisibility.Complete)),
                rows.Count(row => !row.EverFullyVisible && row.Observations.Any(value =>
                    value.Row.Visibility != AchievementVisibleRowVisibility.Complete)),
                movements.Count(value => value.MovementVerified && !value.OverlapVerified),
                state, frameObservation, scrollObservation);
        }

        private static void WaitWithCancellation(int milliseconds, CancellationToken token)
        {
            if (milliseconds <= 0) { token.ThrowIfCancellationRequested(); return; }
            if (token.WaitHandle.WaitOne(milliseconds)) token.ThrowIfCancellationRequested();
        }

        private sealed class StabilizedCapture
        {
            internal StabilizedCapture(Bitmap frame, bool stable,
                TimeSpan finalCaptureDuration, string diagnostics)
            {
                Frame = frame;
                Stable = stable;
                FinalCaptureDuration = finalCaptureDuration;
                Diagnostics = diagnostics ?? string.Empty;
            }

            internal Bitmap Frame { get; }
            internal bool Stable { get; }
            internal TimeSpan FinalCaptureDuration { get; }
            internal string Diagnostics { get; }
        }

        private sealed class FrameSnapshot
        {
            internal FrameSnapshot(AchievementBulkFrameResult result,
                AchievementCategoryFrameEvidence evidence,
                AchievementViewportFingerprint fingerprint,
                string layoutFingerprint,
                string diagnostics)
            {
                Result = result;
                Evidence = evidence;
                Fingerprint = fingerprint;
                LayoutFingerprint = layoutFingerprint;
                Diagnostics = diagnostics;
            }
            internal AchievementBulkFrameResult Result { get; }
            internal AchievementCategoryFrameEvidence Evidence { get; }
            internal AchievementViewportFingerprint Fingerprint { get; }
            internal string LayoutFingerprint { get; }
            internal string Diagnostics { get; }
        }

        private sealed class ObservationAccumulator
        {
            private readonly Dictionary<string, MutableObservedRow> canonical = new(StringComparer.Ordinal);
            private readonly List<MutableObservedRow> unresolved = new();

            internal void Add(int frameIndex, IEnumerable<AchievementVisibleRow> rows)
            {
                foreach (AchievementVisibleRow row in rows ?? Array.Empty<AchievementVisibleRow>())
                {
                    var observation = new AchievementRowFrameObservation(frameIndex, row);
                    if (row.Fingerprint.SuitableForDeduplication)
                    {
                        if (!canonical.TryGetValue(row.Fingerprint.Value, out MutableObservedRow value))
                        {
                            value = new MutableObservedRow(row.Fingerprint.Value, canonical: true);
                            canonical.Add(row.Fingerprint.Value, value);
                        }
                        value.Add(observation);
                    }
                    else
                    {
                        var value = new MutableObservedRow(
                            $"observation:{frameIndex}:{row.RowIndex}:{unresolved.Count}", canonical: false);
                        value.Add(observation);
                        unresolved.Add(value);
                    }
                }
            }

            internal AchievementObservedRow[] Build() => canonical.Values.Concat(unresolved)
                .Select(value => value.Build())
                .OrderBy(value => value.FirstObservedFrame)
                .ThenBy(value => value.Key, StringComparer.Ordinal)
                .ToArray();
        }

        private sealed class MutableObservedRow
        {
            private readonly List<AchievementRowFrameObservation> observations = new();
            private AchievementVisibleRow selected;
            private int selectedStrength = int.MinValue;
            private bool conflicting;

            internal MutableObservedRow(string key, bool canonical)
            {
                Key = key;
                Canonical = canonical;
            }

            internal string Key { get; }
            internal bool Canonical { get; }

            internal void Add(AchievementRowFrameObservation observation)
            {
                observations.Add(observation);
                int strength = Strength(observation.Row);
                if (selected == null || strength > selectedStrength)
                {
                    selected = observation.Row;
                    selectedStrength = strength;
                    conflicting = false;
                }
                else if (strength == selectedStrength && Conflicts(selected, observation.Row))
                {
                    conflicting = true;
                }
            }

            internal AchievementObservedRow Build() => new(Key, Canonical,
                observations, selected, conflicting);

            private static int Strength(AchievementVisibleRow row)
            {
                int value = row.Visibility == AchievementVisibleRowVisibility.Complete ? 8 :
                    row.Visibility == AchievementVisibleRowVisibility.Partial ? 4 : 2;
                if (row.IdentityState == AchievementIdentityState.Identified ||
                    row.IdentityState == AchievementIdentityState.StageGroupIdentified) value += 4;
                if (row.CompletionState != AchievementCompletionState.Unresolved) value += 2;
                if (row.SuitableForTrustedCompletionExtraction) value++;
                return value;
            }

            private static bool Conflicts(AchievementVisibleRow left, AchievementVisibleRow right) =>
                left.CompletionState != right.CompletionState ||
                !left.CandidateIds.SequenceEqual(right.CandidateIds) ||
                left.StageGroupId != right.StageGroupId;
        }
    }
}
