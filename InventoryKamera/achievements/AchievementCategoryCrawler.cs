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
            int maximumStabilizationCaptures = 4,
            int completedTotalCheckpointInterval = 5)
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
            CompletedTotalCheckpointInterval = Positive(completedTotalCheckpointInterval,
                nameof(completedTotalCheckpointInterval));
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
        internal int CompletedTotalCheckpointInterval { get; }
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
            string diagnostics,
            Size captureSize = default,
            bool scrollbarResolved = false)
        {
            TopAtExtent = topAtExtent;
            BottomAtExtent = bottomAtExtent;
            CategorySignature = categorySignature ?? string.Empty;
            CategorySignatureVerified = categorySignatureVerified;
            Diagnostics = diagnostics ?? string.Empty;
            CaptureSize = captureSize;
            ScrollbarResolved = scrollbarResolved;
        }

        internal bool TopAtExtent { get; }
        internal bool BottomAtExtent { get; }
        internal string CategorySignature { get; }
        internal bool CategorySignatureVerified { get; }
        internal string Diagnostics { get; }
        internal Size CaptureSize { get; }
        internal bool ScrollbarResolved { get; }
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

    internal enum AchievementObservationReconciliationStatus
    {
        CanonicalIdentity,
        ReconciledToCanonical,
        AssociatedUnresolved,
        Unresolved,
    }

    internal sealed class AchievementRowFrameObservation
    {
        internal AchievementRowFrameObservation(int frameIndex, AchievementVisibleRow row,
            AchievementObservationReconciliationStatus reconciliationStatus =
                AchievementObservationReconciliationStatus.Unresolved,
            int? matchedFrameIndex = null,
            int? matchedRowIndex = null,
            int? movementBatchIndex = null,
            double? expectedNormalizedY = null,
            double? geometricErrorPixels = null,
            string canonicalFingerprint = null,
            string reconciliationReason = null)
        {
            FrameIndex = frameIndex;
            Row = row ?? throw new ArgumentNullException(nameof(row));
            ReconciliationStatus = reconciliationStatus;
            MatchedFrameIndex = matchedFrameIndex;
            MatchedRowIndex = matchedRowIndex;
            MovementBatchIndex = movementBatchIndex;
            ExpectedNormalizedY = expectedNormalizedY;
            GeometricErrorPixels = geometricErrorPixels;
            CanonicalFingerprint = canonicalFingerprint ?? string.Empty;
            ReconciliationReason = reconciliationReason ?? string.Empty;
        }

        internal int FrameIndex { get; }
        internal AchievementVisibleRow Row { get; }
        internal AchievementObservationReconciliationStatus ReconciliationStatus { get; }
        internal int? MatchedFrameIndex { get; }
        internal int? MatchedRowIndex { get; }
        internal int? MovementBatchIndex { get; }
        internal double? ExpectedNormalizedY { get; }
        internal double? GeometricErrorPixels { get; }
        internal string CanonicalFingerprint { get; }
        internal string ReconciliationReason { get; }
        internal bool ReconciledToCanonical =>
            ReconciliationStatus == AchievementObservationReconciliationStatus.ReconciledToCanonical;
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
            ReconciledObservationCount = Observations.Count(value => value.ReconciledToCanonical);
            HasTrustedCompletionEvidence = Observations.Any(value =>
                value.Row.Fingerprint.SuitableForDeduplication &&
                HasTrustedCompletion(value.Row));
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
        internal int ReconciledObservationCount { get; }
        internal bool HasTrustedCompletionEvidence { get; }

        private static bool HasTrustedCompletion(AchievementVisibleRow row) =>
            row.CompletionState != AchievementCompletionState.Unresolved &&
            (row.IdentityState != AchievementIdentityState.StageGroupIdentified ||
             row.StageProgressResult?.State == AchievementStageProgressState.Resolved);
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
            AchievementCategoryFrameEvidence evidence,
            AchievementCategoryFrameTiming timing = null,
            AchievementCompletedTotalFrameEvidence completedTotalEvidence = null,
            AchievementBulkScreenValidationPath screenValidationPath =
                AchievementBulkScreenValidationPath.Full,
            string screenValidationFallbackReason = null)
        {
            FrameIndex = frameIndex;
            ScreenState = screenState;
            DetectedRows = detectedRows;
            CategorySignature = evidence?.CategorySignature ?? string.Empty;
            CategorySignatureVerified = evidence?.CategorySignatureVerified == true;
            TopAtExtent = evidence?.TopAtExtent == true;
            BottomAtExtent = evidence?.BottomAtExtent == true;
            EvidenceDiagnostics = evidence?.Diagnostics ?? string.Empty;
            Timing = timing ?? AchievementCategoryFrameTiming.Empty;
            CompletedTotalEvidence = completedTotalEvidence ??
                AchievementCompletedTotalFrameEvidence.Empty;
            ScreenValidationPath = screenValidationPath;
            ScreenValidationFallbackReason = screenValidationFallbackReason ?? string.Empty;
        }

        internal int FrameIndex { get; }
        internal AchievementScreenState ScreenState { get; }
        internal int DetectedRows { get; }
        internal string CategorySignature { get; }
        internal bool CategorySignatureVerified { get; }
        internal bool TopAtExtent { get; }
        internal bool BottomAtExtent { get; }
        internal string EvidenceDiagnostics { get; }
        internal AchievementCategoryFrameTiming Timing { get; }
        internal AchievementCompletedTotalFrameEvidence CompletedTotalEvidence { get; }
        internal AchievementBulkScreenValidationPath ScreenValidationPath { get; }
        internal string ScreenValidationFallbackReason { get; }
    }

    internal sealed class AchievementCategoryFrameTiming
    {
        internal static AchievementCategoryFrameTiming Empty { get; } = new(
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, null);

        internal AchievementCategoryFrameTiming(TimeSpan capture,
            TimeSpan evidenceDetection, TimeSpan viewportFingerprint,
            TimeSpan frameReader, TimeSpan layoutProcessing,
            TimeSpan observationCollection, TimeSpan totalProcessing,
            AchievementBulkFrameTiming readerTiming)
        {
            Capture = capture;
            EvidenceDetection = evidenceDetection;
            ViewportFingerprint = viewportFingerprint;
            FrameReader = frameReader;
            LayoutProcessing = layoutProcessing;
            ObservationCollection = observationCollection;
            TotalProcessing = totalProcessing;
            ReaderTiming = readerTiming;
        }

        internal TimeSpan Capture { get; }
        internal TimeSpan EvidenceDetection { get; }
        internal TimeSpan ViewportFingerprint { get; }
        internal TimeSpan FrameReader { get; }
        internal TimeSpan LayoutProcessing { get; }
        internal TimeSpan ObservationCollection { get; }
        internal TimeSpan TotalProcessing { get; }
        internal AchievementBulkFrameTiming ReaderTiming { get; }
        internal TimeSpan ProcessingOther => NonNegative(TotalProcessing - EvidenceDetection -
            ViewportFingerprint - FrameReader - LayoutProcessing - ObservationCollection);

        private static TimeSpan NonNegative(TimeSpan value) =>
            value > TimeSpan.Zero ? value : TimeSpan.Zero;
    }

    internal sealed class AchievementCategoryCrawlTiming
    {
        internal AchievementCategoryCrawlTiming(TimeSpan capture, TimeSpan frameProcessing,
            TimeSpan scrolling, TimeSpan settling, TimeSpan deduplication, TimeSpan total,
            TimeSpan rowDetection = default, TimeSpan titleOcr = default,
            TimeSpan completionOcr = default, TimeSpan rewardOcr = default,
            TimeSpan stabilizationAnalysis = default,
            TimeSpan frameEvidenceDetection = default,
            TimeSpan viewportFingerprinting = default,
            TimeSpan frameReader = default,
            TimeSpan layoutProcessing = default,
            TimeSpan observationCollection = default,
            TimeSpan motionAnalysis = default,
            TimeSpan reconciliation = default,
            TimeSpan progressReporting = default,
            TimeSpan screenDetection = default,
            TimeSpan completedTotal = default,
            TimeSpan ocrCropResize = default,
            TimeSpan ocrPreprocessing = default,
            TimeSpan ocrEngine = default,
            TimeSpan frameReaderOther = default,
            TimeSpan fastScreenValidation = default,
            TimeSpan fullScreenValidation = default,
            int fastScreenValidationCount = 0,
            int fullScreenValidationCount = 0,
            int fullScreenFallbackCount = 0,
            int completedTotalOcrExecutions = 0,
            int completedTotalReuseCount = 0)
        {
            Capture = capture;
            FrameProcessing = frameProcessing;
            Scrolling = scrolling;
            Settling = settling;
            Deduplication = deduplication;
            Total = total;
            RowDetection = rowDetection;
            TitleOcr = titleOcr;
            CompletionOcr = completionOcr;
            RewardOcr = rewardOcr;
            StabilizationAnalysis = stabilizationAnalysis;
            FrameEvidenceDetection = frameEvidenceDetection;
            ViewportFingerprinting = viewportFingerprinting;
            FrameReader = frameReader;
            LayoutProcessing = layoutProcessing;
            ObservationCollection = observationCollection;
            MotionAnalysis = motionAnalysis;
            Reconciliation = reconciliation;
            ProgressReporting = progressReporting;
            ScreenDetection = screenDetection;
            CompletedTotal = completedTotal;
            OcrCropResize = ocrCropResize;
            OcrPreprocessing = ocrPreprocessing;
            OcrEngine = ocrEngine;
            FrameReaderOther = frameReaderOther;
            FastScreenValidation = fastScreenValidation;
            FullScreenValidation = fullScreenValidation;
            FastScreenValidationCount = fastScreenValidationCount;
            FullScreenValidationCount = fullScreenValidationCount;
            FullScreenFallbackCount = fullScreenFallbackCount;
            CompletedTotalOcrExecutions = completedTotalOcrExecutions;
            CompletedTotalReuseCount = completedTotalReuseCount;
        }

        internal TimeSpan Capture { get; }
        internal TimeSpan FrameProcessing { get; }
        internal TimeSpan Scrolling { get; }
        internal TimeSpan Settling { get; }
        internal TimeSpan Deduplication { get; }
        internal TimeSpan Total { get; }
        internal TimeSpan RowDetection { get; }
        internal TimeSpan TitleOcr { get; }
        internal TimeSpan CompletionOcr { get; }
        internal TimeSpan RewardOcr { get; }
        internal TimeSpan StabilizationAnalysis { get; }
        internal TimeSpan FrameEvidenceDetection { get; }
        internal TimeSpan ViewportFingerprinting { get; }
        internal TimeSpan FrameReader { get; }
        internal TimeSpan LayoutProcessing { get; }
        internal TimeSpan ObservationCollection { get; }
        internal TimeSpan MotionAnalysis { get; }
        internal TimeSpan Reconciliation { get; }
        internal TimeSpan ProgressReporting { get; }
        internal TimeSpan ScreenDetection { get; }
        internal TimeSpan CompletedTotal { get; }
        internal TimeSpan OcrCropResize { get; }
        internal TimeSpan OcrPreprocessing { get; }
        internal TimeSpan OcrEngine { get; }
        internal TimeSpan FrameReaderOther { get; }
        internal TimeSpan FastScreenValidation { get; }
        internal TimeSpan FullScreenValidation { get; }
        internal int FastScreenValidationCount { get; }
        internal int FullScreenValidationCount { get; }
        internal int FullScreenFallbackCount { get; }
        internal int CompletedTotalOcrExecutions { get; }
        internal int CompletedTotalReuseCount { get; }
        internal TimeSpan OtherFrameProcessing => NonNegative(FrameProcessing -
            FrameEvidenceDetection - ViewportFingerprinting - FrameReader -
            LayoutProcessing - ObservationCollection);
        internal TimeSpan AccountedExclusive => Capture + Scrolling + Settling +
            StabilizationAnalysis + FrameEvidenceDetection + ViewportFingerprinting +
            FrameReader + LayoutProcessing + ObservationCollection + MotionAnalysis +
            Reconciliation + ProgressReporting;
        internal TimeSpan Unattributed => NonNegative(Total - AccountedExclusive);

        private static TimeSpan NonNegative(TimeSpan value) =>
            value > TimeSpan.Zero ? value : TimeSpan.Zero;
    }

    internal sealed class AchievementScrollObservation
    {
        internal AchievementScrollObservation(int batchIndex,
            AchievementListScrollRequest request,
            AchievementViewportMotion motion,
            AchievementListScrollCommandResult command = null,
            int captureHeight = 0)
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
            CaptureHeight = Math.Max(0, captureHeight);
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
        internal int CaptureHeight { get; }
        internal double? ObservedNormalizedDisplacement => CaptureHeight <= 0
            ? null
            : (ObservedRowDisplacementPixels ?? ObservedVisualDisplacementPixels) / CaptureHeight;
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
            CompletedTotalObservations = new ReadOnlyCollection<AchievementCategoryFrameObservation>(
                FrameObservations.Where(value =>
                    value.CompletedTotalEvidence.PositivelyObserved &&
                    value.CompletedTotalEvidence.Value.HasValue).ToArray());
            int[] completedTotals = CompletedTotalObservations
                .Select(value => value.CompletedTotalEvidence.Value.Value)
                .Distinct().ToArray();
            CompletedTotalInconsistent = completedTotals.Length > 1;
            AchievementCategoryFrameObservation lastCompletedTotal =
                CompletedTotalObservations.LastOrDefault();
            LastObservedCompletedTotal = lastCompletedTotal?.CompletedTotalEvidence.Value;
            LastObservedCompletedTotalFrame = lastCompletedTotal?.FrameIndex;
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
            TotalRawObservations = ObservedRows.Sum(row => row.ObservationCount);
            RawUnresolvedObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                !value.Row.Fingerprint.SuitableForDeduplication));
            ReconciledUnresolvedObservations = ObservedRows.Sum(row =>
                row.ReconciledObservationCount);
            ObservationsLinkedToPreviouslyIdentifiedRows = ObservedRows.Sum(row =>
                row.Observations.Count(value => value.ReconciledToCanonical &&
                    row.Observations.Any(canonical =>
                        canonical.Row.Fingerprint.SuitableForDeduplication &&
                        canonical.FrameIndex < value.FrameIndex)));
            OutstandingUnresolvedObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                !value.Row.Fingerprint.SuitableForDeduplication && !value.ReconciledToCanonical));
            RepeatedObservations = ObservedRows.Where(row => row.Canonical)
                .Sum(row => Math.Max(0, row.Observations.Count(value =>
                    value.Row.Fingerprint.SuitableForDeduplication) - 1));
            PartialRowsRecovered = ObservedRows.Count(row => row.Canonical && row.EverFullyVisible &&
                row.Observations.Any(value => value.Row.Visibility != AchievementVisibleRowVisibility.Complete));
            PartialObservationsRecovered = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.ReconciledToCanonical &&
                value.Row.Visibility == AchievementVisibleRowVisibility.Partial));
            PartialRowsOutstanding = ObservedRows.Count(row => !row.EverFullyVisible &&
                row.Observations.Any(value =>
                    value.Row.Visibility == AchievementVisibleRowVisibility.Partial &&
                    !value.ReconciledToCanonical));
            UnmatchedPartialObservations = ObservedRows.Sum(row => row.Observations.Count(value =>
                value.Row.Visibility == AchievementVisibleRowVisibility.Partial &&
                !value.ReconciledToCanonical && !row.EverFullyVisible));
            DistinctUnresolvedRowIntervals = ObservedRows.Count(row => !row.Canonical);
            IdentifiedGroupsLackingTrustedCompletionEvidence = IdentifiedRows.Count(row =>
                !row.HasTrustedCompletionEvidence);
            ReconciliationConflicts = ObservedRows.Count(row => row.Conflicting &&
                row.Observations.Where(value =>
                        value.Row.Fingerprint.SuitableForDeduplication)
                    .Select(value => value.Row.Fingerprint.Value)
                    .Distinct(StringComparer.Ordinal).Skip(1).Any());
            OverlapVerifiedScrolls = ScrollObservations.Count(value => value.OverlapVerified);
            UnobservedMovementGaps = ScrollObservations.Count(value =>
                value.MovementVerified && !value.OverlapVerified);
            DiagnosticIntervals = AchievementUnresolvedIntervalDiagnosticBuilder.Build(
                ObservedRows, ScrollObservations);
            OutstandingIntervals = new ReadOnlyCollection<AchievementUnresolvedIntervalDiagnostic>(
                DiagnosticIntervals.Where(value => value.IdentityOutstanding).ToArray());
            CompletionLimitedIntervals = new ReadOnlyCollection<AchievementUnresolvedIntervalDiagnostic>(
                DiagnosticIntervals.Where(value => !value.IdentityOutstanding &&
                    (value.PrimaryCause == AchievementUnresolvedCause.CompletionEvidenceUnresolved ||
                     value.SecondaryCauses.Contains(AchievementUnresolvedCause.CompletionEvidenceUnresolved)))
                    .ToArray());
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
        internal IReadOnlyList<AchievementCategoryFrameObservation> CompletedTotalObservations { get; }
        internal bool CompletedTotalInconsistent { get; }
        internal int? LastObservedCompletedTotal { get; }
        internal int? LastObservedCompletedTotalFrame { get; }
        internal IReadOnlyList<AchievementObservedRow> IdentifiedRows { get; }
        internal IReadOnlyList<AchievementObservedRow> UnresolvedRows { get; }
        internal IReadOnlyList<AchievementObservedRow> ConflictingRows { get; }
        internal IReadOnlyList<AchievementObservedRow> AmbiguousRows { get; }
        internal int AmbiguousGroupCount { get; }
        internal int CompleteRowObservations { get; }
        internal int PartialRowObservations { get; }
        internal int UncertainRowObservations { get; }
        internal int RepeatedObservations { get; }
        internal int TotalRawObservations { get; }
        internal int RawUnresolvedObservations { get; }
        internal int ReconciledUnresolvedObservations { get; }
        internal int ObservationsLinkedToPreviouslyIdentifiedRows { get; }
        internal int OutstandingUnresolvedObservations { get; }
        internal int PartialRowsRecovered { get; }
        internal int PartialObservationsRecovered { get; }
        internal int PartialRowsOutstanding { get; }
        internal int UnmatchedPartialObservations { get; }
        internal int DistinctUnresolvedRowIntervals { get; }
        internal int IdentifiedGroupsLackingTrustedCompletionEvidence { get; }
        internal int ReconciliationConflicts { get; }
        internal int OverlapVerifiedScrolls { get; }
        internal int UnobservedMovementGaps { get; }
        internal IReadOnlyList<AchievementUnresolvedIntervalDiagnostic> DiagnosticIntervals { get; }
        internal IReadOnlyList<AchievementUnresolvedIntervalDiagnostic> OutstandingIntervals { get; }
        internal IReadOnlyList<AchievementUnresolvedIntervalDiagnostic> CompletionLimitedIntervals { get; }
        internal bool RecognitionCoverageSufficient => IsComplete &&
            OutstandingUnresolvedObservations == 0 && AmbiguousRows.Count == 0 &&
            ConflictingRows.Count == 0 && ReconciliationConflicts == 0 &&
            PartialRowsOutstanding == 0 &&
            UnobservedMovementGaps == 0;
        internal bool TrustedCompletionCoverageSufficient => IsComplete &&
            RecognitionCoverageSufficient &&
            IdentifiedGroupsLackingTrustedCompletionEvidence == 0;
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
            TimeSpan rowDetectionTime = TimeSpan.Zero;
            TimeSpan titleOcrTime = TimeSpan.Zero;
            TimeSpan completionOcrTime = TimeSpan.Zero;
            TimeSpan rewardOcrTime = TimeSpan.Zero;
            TimeSpan stabilizationAnalysisTime = TimeSpan.Zero;
            TimeSpan motionAnalysisTime = TimeSpan.Zero;
            TimeSpan reconciliationTime = TimeSpan.Zero;
            TimeSpan progressReportingTime = TimeSpan.Zero;
            var aggregate = new ObservationAccumulator();
            var scrollObservations = new List<AchievementScrollObservation>();
            var frameObservations = new List<AchievementCategoryFrameObservation>();
            var diagnostics = new List<string>();
            int frames = 0;
            int scrolls = 0;
            int uncertainFrames = 0;
            int noMovementAttempts = 0;
            int? contextualCompletedTotal = null;
            int? contextualCompletedTotalFrame = null;
            Size traversalCaptureSize = startingFrame.Size;
            string stableCategorySignature = context.ExpectedCategorySignature;
            AchievementScrollProgressState scrollState = AchievementScrollProgressState.NoMovementUnknown;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                FrameSnapshot previous = ProcessFrame(startingFrame, 0, startingCaptureDuration ?? TimeSpan.Zero,
                    traversalCaptureSize, context.ExpectedCategorySignature,
                    ref contextualCompletedTotal, ref contextualCompletedTotalFrame,
                    aggregate, ref processingTime, ref deduplicationTime,
                    ref rowDetectionTime, ref titleOcrTime,
                    ref completionOcrTime, ref rewardOcrTime);
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
                ReportProgress(frameObservations[^1], null);

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
                            cancellationToken, ref captureTime, ref settlingTime,
                            ref stabilizationAnalysisTime);
                        captured = stabilized.Frame;
                        diagnostics.Add($"scroll={scrolls}; {stabilized.Diagnostics}");
                        if (!stabilized.Stable)
                            return Finish(AchievementCategoryCrawlState.Incomplete,
                                "The post-scroll viewport did not stabilize within the bounded capture limit.",
                                "stabilization=unresolved");

                        FrameSnapshot current = ProcessFrame(captured, frames, stabilized.FinalCaptureDuration,
                            traversalCaptureSize, stableCategorySignature,
                            ref contextualCompletedTotal, ref contextualCompletedTotalFrame,
                            aggregate, ref processingTime, ref deduplicationTime,
                            ref rowDetectionTime, ref titleOcrTime,
                            ref completionOcrTime, ref rewardOcrTime);
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

                        part.Restart();
                        AchievementViewportMotion motion = AchievementViewportMotion.Compare(
                            previous.Result, previous.Fingerprint,
                            current.Result, current.Fingerprint,
                            captured.Height);
                        part.Stop();
                        motionAnalysisTime += part.Elapsed;
                        var scrollObservation = new AchievementScrollObservation(
                            scrolls, options.ScrollRequest, motion, command, captured.Height);
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
                            ReportProgress(frameObservations[^1], scrollObservation);
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
                        ReportProgress(frameObservations[^1], scrollObservation);
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
                AchievementObservedRow[] rows = BuildRows();
                total.Stop();
                TimeSpan frameEvidenceTime = SumFrameTiming(value => value.EvidenceDetection);
                TimeSpan fingerprintTime = SumFrameTiming(value => value.ViewportFingerprint);
                TimeSpan frameReaderTime = SumFrameTiming(value => value.FrameReader);
                TimeSpan layoutTime = SumFrameTiming(value => value.LayoutProcessing);
                TimeSpan observationTime = SumFrameTiming(value => value.ObservationCollection);
                TimeSpan screenDetectionTime = SumReaderTiming(value => value.ScreenDetection);
                TimeSpan completedTotalTime = SumReaderTiming(value => value.CompletedTotal);
                TimeSpan cropResizeTime = SumReaderTiming(value => value.OcrCropResize);
                TimeSpan preprocessingTime = SumReaderTiming(value => value.OcrPreprocessing);
                TimeSpan ocrEngineTime = SumReaderTiming(value => value.OcrEngine);
                TimeSpan frameReaderOtherTime = SumReaderTiming(value => value.FrameReaderOther);
                TimeSpan fastScreenValidationTime = SumReaderTiming(value => value.FastScreenValidation);
                TimeSpan fullScreenValidationTime = SumReaderTiming(value => value.FullScreenValidation);
                int fastScreenValidationCount = SumReaderCount(value => value.FastScreenValidationCount);
                int fullScreenValidationCount = SumReaderCount(value => value.FullScreenValidationCount);
                int fullScreenFallbackCount = SumReaderCount(value => value.FullScreenFallbackCount);
                int completedTotalOcrExecutions = SumReaderCount(value => value.CompletedTotalOcrExecutions);
                int completedTotalReuseCount = SumReaderCount(value => value.CompletedTotalReuseCount);
                var timing = new AchievementCategoryCrawlTiming(captureTime, processingTime,
                    scrollingTime, settlingTime, deduplicationTime, total.Elapsed,
                    rowDetectionTime, titleOcrTime, completionOcrTime, rewardOcrTime,
                    stabilizationAnalysisTime, frameEvidenceTime, fingerprintTime,
                    frameReaderTime, layoutTime, observationTime, motionAnalysisTime,
                    reconciliationTime, progressReportingTime, screenDetectionTime,
                    completedTotalTime, cropResizeTime, preprocessingTime, ocrEngineTime,
                    frameReaderOtherTime, fastScreenValidationTime, fullScreenValidationTime,
                    fastScreenValidationCount, fullScreenValidationCount,
                    fullScreenFallbackCount, completedTotalOcrExecutions,
                    completedTotalReuseCount);
                diagnostics.Add($"state={state}; frames={frames}; scrolls={scrolls}; uniqueIdentified={rows.Count(row => row.Resolved)}; " +
                    $"unresolved={rows.Count(row => !row.Resolved)}; repeated={rows.Where(row => row.Canonical).Sum(row => Math.Max(0, row.Observations.Count(value => value.Row.Fingerprint.SuitableForDeduplication) - 1))}; " +
                    $"ambiguous={rows.Count(row => row.Observations.Any(value => value.Row.IdentityState == AchievementIdentityState.Ambiguous))}; " +
                    $"conflicting={rows.Count(row => row.Conflicting)}; rawObservations={rows.Sum(row => row.ObservationCount)}; " +
                    $"rawUnresolved={rows.Sum(row => row.Observations.Count(value => !value.Row.Fingerprint.SuitableForDeduplication))}; " +
                    $"reconciled={rows.Sum(row => row.ReconciledObservationCount)}; outstandingUnresolved={rows.Sum(row => row.Observations.Count(value => !value.Row.Fingerprint.SuitableForDeduplication && !value.ReconciledToCanonical))}; " +
                    $"partialOutstanding={rows.Count(row => !row.Canonical && row.Observations.Any(value => value.Row.Visibility == AchievementVisibleRowVisibility.Partial))}; " +
                    $"unobservedMovementGaps={scrollObservations.Count(value => value.MovementVerified && !value.OverlapVerified)}; " +
                    $"captureMs={captureTime.TotalMilliseconds:0.00}; processingMs={processingTime.TotalMilliseconds:0.00}; " +
                    $"scrollMs={scrollingTime.TotalMilliseconds:0.00}; settleMs={settlingTime.TotalMilliseconds:0.00}; " +
                    $"rowDetectionMs={rowDetectionTime.TotalMilliseconds:0.00}; titleOcrMs={titleOcrTime.TotalMilliseconds:0.00}; " +
                    $"completionOcrMs={completionOcrTime.TotalMilliseconds:0.00}; rewardOcrMs={rewardOcrTime.TotalMilliseconds:0.00}; " +
                    $"evidenceMs={frameEvidenceTime.TotalMilliseconds:0.00}; fingerprintMs={fingerprintTime.TotalMilliseconds:0.00}; " +
                    $"frameReaderMs={frameReaderTime.TotalMilliseconds:0.00}; layoutMs={layoutTime.TotalMilliseconds:0.00}; " +
                    $"observationMs={observationTime.TotalMilliseconds:0.00}; stabilizationAnalysisMs={stabilizationAnalysisTime.TotalMilliseconds:0.00}; " +
                    $"motionAnalysisMs={motionAnalysisTime.TotalMilliseconds:0.00}; reconciliationMs={reconciliationTime.TotalMilliseconds:0.00}; " +
                    $"progressReportingMs={progressReportingTime.TotalMilliseconds:0.00}; ocrEngineMs={ocrEngineTime.TotalMilliseconds:0.00}; " +
                    $"fastScreenValidationMs={fastScreenValidationTime.TotalMilliseconds:0.00}; fullScreenValidationMs={fullScreenValidationTime.TotalMilliseconds:0.00}; " +
                    $"fastScreenValidations={fastScreenValidationCount}; fullScreenValidations={fullScreenValidationCount}; " +
                    $"screenValidationFallbacks={fullScreenFallbackCount}; completedTotalOcrExecutions={completedTotalOcrExecutions}; " +
                    $"completedTotalReuses={completedTotalReuseCount}; completedTotalPositiveObservations={frameObservations.Count(value => value.CompletedTotalEvidence.PositivelyObserved)}; " +
                    $"completedTotalInconsistent={frameObservations.Where(value => value.CompletedTotalEvidence.PositivelyObserved && value.CompletedTotalEvidence.Value.HasValue).Select(value => value.CompletedTotalEvidence.Value.Value).Distinct().Skip(1).Any()}; " +
                    $"dedupMs={deduplicationTime.TotalMilliseconds:0.00}; accountedExclusiveMs={timing.AccountedExclusive.TotalMilliseconds:0.00}; " +
                    $"unattributedMs={timing.Unattributed.TotalMilliseconds:0.00}; totalMs={total.Elapsed.TotalMilliseconds:0.00}");
                return new AchievementCategoryCrawlResult(context, state, completenessEvidence,
                    frames, scrolls, rows, scrollObservations, frameObservations, scrollState, reason, timing,
                    string.Join(Environment.NewLine, diagnostics));
            }

            AchievementObservedRow[] BuildRows()
            {
                Stopwatch reconciliation = Stopwatch.StartNew();
                AchievementObservedRow[] rows = aggregate.Build(scrollObservations);
                reconciliation.Stop();
                reconciliationTime += reconciliation.Elapsed;
                return rows;
            }

            void ReportProgress(AchievementCategoryFrameObservation frameObservation,
                AchievementScrollObservation scrollObservation)
            {
                if (progress == null) return;
                AchievementObservedRow[] rows = BuildRows();
                Stopwatch reporting = Stopwatch.StartNew();
                progress.Report(Progress(rows, frames, scrolls, scrollState,
                    frameObservation, scrollObservation, scrollObservations));
                reporting.Stop();
                progressReportingTime += reporting.Elapsed;
            }

            TimeSpan SumFrameTiming(Func<AchievementCategoryFrameTiming, TimeSpan> selector) =>
                TimeSpan.FromTicks(frameObservations.Sum(value => selector(value.Timing).Ticks));

            TimeSpan SumReaderTiming(Func<AchievementBulkFrameTiming, TimeSpan> selector) =>
                TimeSpan.FromTicks(frameObservations.Sum(value =>
                    value.Timing.ReaderTiming == null ? 0 : selector(value.Timing.ReaderTiming).Ticks));

            int SumReaderCount(Func<AchievementBulkFrameTiming, int> selector) =>
                frameObservations.Sum(value =>
                    value.Timing.ReaderTiming == null ? 0 : selector(value.Timing.ReaderTiming));
        }

        private StabilizedCapture CaptureStabilizedFrame(
            CancellationToken cancellationToken,
            ref TimeSpan captureTime,
            ref TimeSpan settlingTime,
            ref TimeSpan stabilizationAnalysisTime)
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
                        Stopwatch analysis = Stopwatch.StartNew();
                        AchievementViewportComparison comparison =
                            AchievementViewportFingerprint.Create(previous).CompareTo(
                                AchievementViewportFingerprint.Create(current),
                                maximumDisplacementPixels: 48);
                        analysis.Stop();
                        stabilizationAnalysisTime += analysis.Elapsed;
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
            Size traversalCaptureSize, string expectedCategorySignature,
            ref int? contextualCompletedTotal, ref int? contextualCompletedTotalFrame,
            ObservationAccumulator aggregate, ref TimeSpan processingTime, ref TimeSpan deduplicationTime,
            ref TimeSpan rowDetectionTime, ref TimeSpan titleOcrTime,
            ref TimeSpan completionOcrTime, ref TimeSpan rewardOcrTime)
        {
            Stopwatch total = Stopwatch.StartNew();
            Stopwatch part = Stopwatch.StartNew();
            AchievementCategoryFrameEvidence evidence = frameEvidenceDetector.Detect(screenshot);
            part.Stop();
            TimeSpan evidenceTime = part.Elapsed;
            part.Restart();
            AchievementViewportFingerprint fingerprint = AchievementViewportFingerprint.Create(screenshot);
            part.Stop();
            TimeSpan fingerprintTime = part.Elapsed;
            part.Restart();
            bool observeCompletedTotal = frameIndex == 0 ||
                frameIndex % options.CompletedTotalCheckpointInterval == 0 ||
                frameIndex == options.MaximumCapturedFrames - 1 ||
                evidence.BottomAtExtent;
            AchievementBulkFrameResult result;
            if (frameReader is IContextualAchievementListFrameReader contextualReader)
            {
                var readContext = new AchievementBulkFrameReadContext(
                    frameIndex,
                    traversalCaptureSize,
                    evidence,
                    expectedCategorySignature,
                    forceFullScreenValidation: frameIndex == 0,
                    observeCompletedTotal,
                    contextualCompletedTotal,
                    contextualCompletedTotalFrame);
                result = contextualReader.Read(screenshot, readContext, captureDuration);
            }
            else
            {
                result = frameReader.Read(screenshot, captureDuration);
            }
            part.Stop();
            TimeSpan readerTime = part.Elapsed;
            if (result.CompletedTotalEvidence.PositivelyObserved &&
                result.CompletedTotalEvidence.Value.HasValue)
            {
                contextualCompletedTotal = result.CompletedTotalEvidence.Value;
                contextualCompletedTotalFrame = frameIndex;
            }
            rowDetectionTime += result.Timing.RowDetection;
            titleOcrTime += result.Timing.TitleOcr;
            completionOcrTime += result.Timing.CompletionOcr;
            rewardOcrTime += result.Timing.RewardOcr;
            part.Restart();
            string layout = string.Join("|", result.VisibleRows.Select(row =>
                $"{row.Visibility}:{row.Fingerprint.Value}:{row.IdentityState}:{row.CompletionState}"));
            part.Stop();
            TimeSpan layoutTime = part.Elapsed;
            part.Restart();
            aggregate.Add(frameIndex, result.VisibleRows);
            part.Stop();
            TimeSpan observationTime = part.Elapsed;
            deduplicationTime += observationTime;
            string diagnostics = $"frame={frameIndex}; screen={result.ScreenState}; rows={result.VisibleRows.Count}; " +
                $"fingerprint={fingerprint}; layout={layout}; categorySignature={evidence.CategorySignature}; " +
                $"categoryVerified={evidence.CategorySignatureVerified}; top={evidence.TopAtExtent}; bottom={evidence.BottomAtExtent}; " +
                $"screenValidation={result.ScreenValidationPath}; screenFallback={result.ScreenValidationFallbackReason}; " +
                $"completedTotalSource={result.CompletedTotalEvidence.Source}; completedTotalObserved={result.CompletedTotalEvidence.PositivelyObserved}; " +
                $"completedTotalReused={result.CompletedTotalEvidence.Reused}; {evidence.Diagnostics}";
            total.Stop();
            processingTime += total.Elapsed;
            var timing = new AchievementCategoryFrameTiming(captureDuration,
                evidenceTime, fingerprintTime, readerTime, layoutTime,
                observationTime, total.Elapsed, result.Timing);
            return new FrameSnapshot(result, evidence, fingerprint, layout, diagnostics, timing);
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
            new(frameIndex, frame.Result.ScreenState, frame.Result.VisibleRows.Count,
                frame.Evidence, frame.Timing, frame.Result.CompletedTotalEvidence,
                frame.Result.ScreenValidationPath, frame.Result.ScreenValidationFallbackReason);

        private static AchievementCategoryCrawlProgress Progress(AchievementObservedRow[] rows,
            int frames, int scrolls, AchievementScrollProgressState state,
            AchievementCategoryFrameObservation frameObservation,
            AchievementScrollObservation scrollObservation,
            IEnumerable<AchievementScrollObservation> scrollObservations)
        {
            AchievementScrollObservation[] movements =
                (scrollObservations ?? Array.Empty<AchievementScrollObservation>()).ToArray();
            return new AchievementCategoryCrawlProgress(frames, scrolls,
                rows.Count(row => row.Resolved), rows.Sum(row => row.Observations.Count(value =>
                    !value.Row.Fingerprint.SuitableForDeduplication && !value.ReconciledToCanonical)),
                rows.Where(row => row.Observations.Any(value =>
                        value.Row.IdentityState == AchievementIdentityState.Ambiguous))
                    .Select(row => (row.SelectedObservation ?? row.Observations[0].Row).NormalizedTitle + ":" +
                        string.Join(",", (row.SelectedObservation ?? row.Observations[0].Row).CandidateIds.OrderBy(value => value)))
                    .Distinct(StringComparer.Ordinal).Count(),
                rows.Count(row => row.Conflicting),
                rows.Where(row => row.Canonical).Sum(row => Math.Max(0,
                    row.Observations.Count(value => value.Row.Fingerprint.SuitableForDeduplication) - 1)),
                rows.Count(row => row.Canonical && row.EverFullyVisible &&
                    row.Observations.Any(value => value.Row.Visibility != AchievementVisibleRowVisibility.Complete)),
                rows.Count(row => !row.EverFullyVisible && row.Observations.Any(value =>
                    value.Row.Visibility == AchievementVisibleRowVisibility.Partial &&
                    !value.ReconciledToCanonical)),
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
                string diagnostics,
                AchievementCategoryFrameTiming timing)
            {
                Result = result;
                Evidence = evidence;
                Fingerprint = fingerprint;
                LayoutFingerprint = layoutFingerprint;
                Diagnostics = diagnostics;
                Timing = timing ?? AchievementCategoryFrameTiming.Empty;
            }
            internal AchievementBulkFrameResult Result { get; }
            internal AchievementCategoryFrameEvidence Evidence { get; }
            internal AchievementViewportFingerprint Fingerprint { get; }
            internal string LayoutFingerprint { get; }
            internal string Diagnostics { get; }
            internal AchievementCategoryFrameTiming Timing { get; }
        }

        private sealed class ObservationAccumulator
        {
            private const double MaximumAssociationErrorPixels = 6.0;
            private readonly List<RawObservation> observations = new();
            private AchievementObservedRow[] cachedRows;
            private int cachedObservationCount = -1;
            private AchievementScrollObservation[] cachedScrolls = Array.Empty<AchievementScrollObservation>();

            internal void Add(int frameIndex, IEnumerable<AchievementVisibleRow> rows)
            {
                foreach (AchievementVisibleRow row in rows ?? Array.Empty<AchievementVisibleRow>())
                    observations.Add(new RawObservation(observations.Count, frameIndex, row));
                cachedRows = null;
            }

            internal AchievementObservedRow[] Build(
                IEnumerable<AchievementScrollObservation> sourceScrolls = null)
            {
                AchievementScrollObservation[] scrolls =
                    (sourceScrolls ?? Array.Empty<AchievementScrollObservation>()).ToArray();
                if (cachedRows != null && cachedObservationCount == observations.Count &&
                    cachedScrolls.SequenceEqual(scrolls))
                    return cachedRows;
                var sets = new DisjointObservationSets(observations.Count);
                foreach (IGrouping<string, RawObservation> group in observations
                    .Where(value => value.Row.Fingerprint.SuitableForDeduplication)
                    .GroupBy(value => value.Row.Fingerprint.Value, StringComparer.Ordinal))
                {
                    RawObservation first = group.First();
                    foreach (RawObservation value in group.Skip(1))
                        sets.Union(first.Id, value.Id);
                }

                foreach (AchievementScrollObservation scroll in scrolls)
                    AssociateAdjacentFrames(scroll, sets);

                cachedRows = observations
                    .GroupBy(value => sets.Find(value.Id))
                    .Select((group, index) => BuildObservedRow(group.ToArray(), index))
                    .OrderBy(value => value.FirstObservedFrame)
                    .ThenBy(value => value.Key, StringComparer.Ordinal)
                    .ToArray();
                cachedObservationCount = observations.Count;
                cachedScrolls = scrolls;
                return cachedRows;
            }

            private void AssociateAdjacentFrames(
                AchievementScrollObservation scroll,
                DisjointObservationSets sets)
            {
                int previousFrame = scroll.BatchIndex - 1;
                int currentFrame = scroll.BatchIndex;
                RawObservation[] previous = observations.Where(value =>
                    value.FrameIndex == previousFrame).ToArray();
                RawObservation[] current = observations.Where(value =>
                    value.FrameIndex == currentFrame).ToArray();
                RawObservation[] relevant = previous.Concat(current)
                    .Where(IsEligiblePartial).ToArray();
                if (relevant.Length == 0) return;

                double? normalizedMovement = scroll.ObservedNormalizedDisplacement;
                if (!scroll.InputSucceeded || scroll.Contradictory ||
                    (!scroll.MovementVerified && !scroll.ViewportStable) ||
                    !scroll.OverlapVerified ||
                    !normalizedMovement.HasValue || scroll.CaptureHeight <= 0)
                {
                    foreach (RawObservation value in relevant)
                        value.NoteRejection("missing-or-unverified-movement");
                    return;
                }

                var candidates = new List<AssociationCandidate>();
                foreach (RawObservation left in previous)
                {
                    foreach (RawObservation right in current)
                    {
                        if (!IsEligiblePair(left, right)) continue;
                        double expectedY = left.Row.NormalizedVerticalPosition + normalizedMovement.Value;
                        double error = Math.Abs(right.Row.NormalizedVerticalPosition - expectedY) *
                            scroll.CaptureHeight;
                        if (error <= MaximumAssociationErrorPixels)
                            candidates.Add(new AssociationCandidate(left, right, expectedY, error));
                    }
                }

                foreach (RawObservation value in relevant)
                {
                    int count = candidates.Count(candidate => candidate.Contains(value));
                    if (count == 0) value.NoteRejection("geometry-incompatible-with-measured-movement");
                    else if (count > 1) value.NoteRejection("ambiguous-geometric-match");
                }

                foreach (AssociationCandidate candidate in candidates)
                {
                    if (candidates.Count(value => value.Left == candidate.Left) != 1 ||
                        candidates.Count(value => value.Right == candidate.Right) != 1)
                        continue;
                    sets.Union(candidate.Left.Id, candidate.Right.Id);
                    candidate.Left.NoteMatch(candidate.Right, scroll.BatchIndex,
                        candidate.ExpectedNormalizedY, candidate.ErrorPixels);
                    candidate.Right.NoteMatch(candidate.Left, scroll.BatchIndex,
                        candidate.Right.Row.NormalizedVerticalPosition, candidate.ErrorPixels);
                }
            }

            private static bool IsEligiblePartial(RawObservation value) =>
                !value.Row.Fingerprint.SuitableForDeduplication &&
                value.Row.Visibility == AchievementVisibleRowVisibility.Partial;

            private static bool IsEligiblePair(RawObservation left, RawObservation right) =>
                (IsEligiblePartial(left) || IsEligiblePartial(right)) &&
                (!left.Row.Fingerprint.SuitableForDeduplication ||
                 !right.Row.Fingerprint.SuitableForDeduplication);

            private static AchievementObservedRow BuildObservedRow(
                RawObservation[] values, int intervalIndex)
            {
                string[] canonicalFingerprints = values
                    .Where(value => value.Row.Fingerprint.SuitableForDeduplication)
                    .Select(value => value.Row.Fingerprint.Value)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .ToArray();
                bool canonical = canonicalFingerprints.Length == 1;
                bool identityConflict = canonicalFingerprints.Length > 1;
                string canonicalFingerprint = canonical
                    ? canonicalFingerprints[0]
                    : string.Empty;
                AchievementVisibleRow selected = values.Select(value => value.Row)
                    .OrderByDescending(Strength)
                    .FirstOrDefault();
                int selectedStrength = selected == null ? int.MinValue : Strength(selected);
                bool evidenceConflict = values.Select(value => value.Row)
                    .Where(value => Strength(value) == selectedStrength)
                    .Any(value => selected != null && Conflicts(selected, value));
                bool conflicting = identityConflict || evidenceConflict;
                string key = canonical
                    ? canonicalFingerprint
                    : $"interval:{values.Min(value => value.FrameIndex)}:{intervalIndex}";
                AchievementRowFrameObservation[] finalized = values
                    .OrderBy(value => value.FrameIndex)
                    .ThenBy(value => value.Row.RowIndex)
                    .Select(value => value.Build(canonicalFingerprint,
                        canonical, identityConflict))
                    .ToArray();
                return new AchievementObservedRow(key, canonical, finalized,
                    selected, conflicting);
            }

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

            private sealed class RawObservation
            {
                private RawObservation matched;
                private int? movementBatchIndex;
                private double? expectedNormalizedY;
                private double? geometricErrorPixels;
                private string reason = "No verified geometric association was established.";

                internal RawObservation(int id, int frameIndex, AchievementVisibleRow row)
                {
                    Id = id;
                    FrameIndex = frameIndex;
                    Row = row;
                }

                internal int Id { get; }
                internal int FrameIndex { get; }
                internal AchievementVisibleRow Row { get; }

                internal void NoteMatch(RawObservation other, int batchIndex,
                    double expectedY, double errorPixels)
                {
                    matched = other;
                    movementBatchIndex = batchIndex;
                    expectedNormalizedY = expectedY;
                    geometricErrorPixels = errorPixels;
                    reason = $"accepted-adjacent-geometric-match(error={errorPixels:0.0}px)";
                }

                internal void NoteRejection(string value)
                {
                    if (matched == null) reason = value ?? string.Empty;
                }

                internal AchievementRowFrameObservation Build(string canonicalFingerprint,
                    bool canonicalComponent, bool identityConflict)
                {
                    AchievementObservationReconciliationStatus status;
                    if (Row.Fingerprint.SuitableForDeduplication)
                        status = AchievementObservationReconciliationStatus.CanonicalIdentity;
                    else if (canonicalComponent)
                        status = AchievementObservationReconciliationStatus.ReconciledToCanonical;
                    else if (matched != null && !identityConflict)
                        status = AchievementObservationReconciliationStatus.AssociatedUnresolved;
                    else
                        status = AchievementObservationReconciliationStatus.Unresolved;
                    string finalReason = identityConflict
                        ? "conflicting-canonical-identities-through-geometric-association"
                        : reason;
                    return new AchievementRowFrameObservation(FrameIndex, Row, status,
                        matched?.FrameIndex, matched?.Row.RowIndex, movementBatchIndex,
                        expectedNormalizedY, geometricErrorPixels,
                        canonicalComponent || identityConflict ? canonicalFingerprint : string.Empty,
                        finalReason);
                }
            }

            private sealed class AssociationCandidate
            {
                internal AssociationCandidate(RawObservation left, RawObservation right,
                    double expectedNormalizedY, double errorPixels)
                {
                    Left = left;
                    Right = right;
                    ExpectedNormalizedY = expectedNormalizedY;
                    ErrorPixels = errorPixels;
                }

                internal RawObservation Left { get; }
                internal RawObservation Right { get; }
                internal double ExpectedNormalizedY { get; }
                internal double ErrorPixels { get; }
                internal bool Contains(RawObservation value) => Left == value || Right == value;
            }

            private sealed class DisjointObservationSets
            {
                private readonly int[] parents;

                internal DisjointObservationSets(int count)
                {
                    parents = Enumerable.Range(0, count).ToArray();
                }

                internal int Find(int value)
                {
                    while (parents[value] != value)
                    {
                        parents[value] = parents[parents[value]];
                        value = parents[value];
                    }
                    return value;
                }

                internal void Union(int left, int right)
                {
                    int leftRoot = Find(left);
                    int rightRoot = Find(right);
                    if (leftRoot != rightRoot) parents[rightRoot] = leftRoot;
                }
            }
        }
    }
}
