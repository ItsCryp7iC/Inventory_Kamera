using InventoryKamera.game;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace InventoryKamera
{
    internal enum AchievementBulkDiagnosticMode
    {
        Short,
        FullTraversal,
    }

    internal sealed class AchievementBulkDiagnosticOptions
    {
        internal const int DefaultMaximumCapturedFrames = 5;
        internal const int DefaultMaximumScrollBatches = 4;
        internal const int FullTraversalMaximumCapturedFrames = 80;
        internal const int FullTraversalMaximumScrollBatches = 79;
        internal static AchievementBulkDiagnosticOptions ShortProfile { get; } = new(
            AchievementBulkDiagnosticMode.Short,
            DefaultMaximumCapturedFrames,
            DefaultMaximumScrollBatches);
        internal static AchievementBulkDiagnosticOptions FullTraversalProfile { get; } = new(
            AchievementBulkDiagnosticMode.FullTraversal,
            FullTraversalMaximumCapturedFrames,
            FullTraversalMaximumScrollBatches);
        internal static AchievementBulkDiagnosticOptions Default => ShortProfile;

        internal AchievementBulkDiagnosticOptions(
            AchievementBulkDiagnosticMode mode = AchievementBulkDiagnosticMode.Short,
            int maximumCapturedFrames = DefaultMaximumCapturedFrames,
            int maximumScrollBatches = DefaultMaximumScrollBatches,
            int wheelNotchesPerBatch = 21)
        {
            if (!Enum.IsDefined(mode))
                throw new ArgumentOutOfRangeException(nameof(mode));
            if (maximumCapturedFrames < 1 || maximumCapturedFrames > FullTraversalMaximumCapturedFrames)
                throw new ArgumentOutOfRangeException(nameof(maximumCapturedFrames));
            if (maximumScrollBatches < 1 || maximumScrollBatches > FullTraversalMaximumScrollBatches ||
                maximumScrollBatches >= maximumCapturedFrames)
                throw new ArgumentOutOfRangeException(nameof(maximumScrollBatches));
            if (wheelNotchesPerBatch < 1 || wheelNotchesPerBatch > 24)
                throw new ArgumentOutOfRangeException(nameof(wheelNotchesPerBatch));
            Mode = mode;
            MaximumCapturedFrames = maximumCapturedFrames;
            MaximumScrollBatches = maximumScrollBatches;
            WheelNotchesPerBatch = wheelNotchesPerBatch;
        }

        internal AchievementBulkDiagnosticMode Mode { get; }
        internal int MaximumCapturedFrames { get; }
        internal int MaximumScrollBatches { get; }
        internal int WheelNotchesPerBatch { get; }

        internal AchievementCategoryCrawlOptions ToCrawlerOptions() => new(
            maximumCapturedFrames: MaximumCapturedFrames,
            maximumScrollActions: MaximumScrollBatches,
            wheelNotchesPerBatch: WheelNotchesPerBatch);
    }

    internal sealed class AchievementBulkDiagnosticResult
    {
        internal AchievementBulkDiagnosticResult(AchievementCategoryCrawlResult crawlResult,
            AchievementBulkDiagnosticMode mode = AchievementBulkDiagnosticMode.Short)
        {
            CrawlResult = crawlResult ?? throw new ArgumentNullException(nameof(crawlResult));
            Mode = mode;
            State = crawlResult.State;
            Reason = crawlResult.Reason;
        }

        internal AchievementBulkDiagnosticResult(AchievementCategoryCrawlState state, string reason,
            AchievementBulkDiagnosticMode mode = AchievementBulkDiagnosticMode.Short)
        {
            Mode = mode;
            State = state;
            Reason = reason ?? string.Empty;
        }

        internal AchievementBulkDiagnosticMode Mode { get; }
        internal AchievementCategoryCrawlResult CrawlResult { get; }
        internal AchievementCategoryCrawlState State { get; }
        internal string Reason { get; }
        internal string CategorySignature => CrawlResult?.FrameObservations
            .Where(frame => frame.CategorySignatureVerified)
            .Select(frame => frame.CategorySignature)
            .FirstOrDefault() ?? string.Empty;
        internal bool HasBottomProof => CrawlResult?.IsComplete == true;
        internal bool HasBottomPositionEvidence => CrawlResult?.FrameObservations
            .TakeLast(2).Count(frame => frame.BottomAtExtent) == 2;
        internal bool RecognitionCoverageSufficient =>
            CrawlResult?.RecognitionCoverageSufficient == true;
    }

    /// <summary>
    /// Read-only application orchestration for one manually selected category. Its dependency
    /// surface intentionally contains no persistence, export, reward, or completion-update service.
    /// </summary>
    internal sealed class AchievementBulkDiagnosticRunner
    {
        private readonly IGameScreenCapture screenCapture;
        private readonly IAchievementCategoryCrawler crawler;
        private readonly AchievementBulkDiagnosticMode mode;
        private int active;

        internal AchievementBulkDiagnosticRunner(
            IGameScreenCapture screenCapture,
            IAchievementCategoryCrawler crawler,
            AchievementBulkDiagnosticMode mode = AchievementBulkDiagnosticMode.Short)
        {
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.crawler = crawler ?? throw new ArgumentNullException(nameof(crawler));
            if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            this.mode = mode;
        }

        internal bool IsActive => Volatile.Read(ref active) != 0;

        internal AchievementBulkDiagnosticResult Run(
            CancellationToken cancellationToken,
            IProgress<AchievementCategoryCrawlProgress> progress = null)
        {
            if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Failed,
                    "A bulk-scrolling diagnostic is already active.", mode);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stopwatch capture = Stopwatch.StartNew();
                using Bitmap startingFrame = screenCapture.CaptureWindow();
                capture.Stop();
                if (startingFrame == null)
                    return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Failed,
                        "The initial Genshin capture returned no image.", mode);
                cancellationToken.ThrowIfCancellationRequested();

                AchievementCategoryCrawlResult result = crawler.Crawl(
                    new AchievementCategoryCrawlContext(
                        "Manually selected category",
                        requireVerifiedCategorySignature: true),
                    startingFrame,
                    cancellationToken,
                    progress,
                    capture.Elapsed);
                return new AchievementBulkDiagnosticResult(result, mode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Cancelled,
                    "The bulk-scrolling diagnostic was cancelled before crawler evidence was available.", mode);
            }
            catch (Exception ex)
            {
                return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Failed,
                    $"The bulk-scrolling diagnostic failed: {ex.GetType().Name}: {ex.Message}", mode);
            }
            finally
            {
                Volatile.Write(ref active, 0);
            }
        }
    }

    internal static class AchievementBulkDiagnosticReportBuilder
    {
        internal const int MaximumDiagnosticDetailCharacters = 32768;
        internal const int MaximumDetailedIntervals = 128;
        internal const int MaximumDetailedFrames = 80;

        internal static string BuildProgress(AchievementCategoryCrawlProgress progress)
        {
            if (progress == null) return "Preparing bulk-scrolling diagnostic";
            string category = progress.FrameObservation?.CategorySignatureVerified == true
                ? progress.FrameObservation.CategorySignature
                : "category not yet verified";
            return $"{category}: frame {progress.FrameCount}, batch {progress.ScrollActions}";
        }

        internal static string BuildProgressDetails(AchievementCategoryCrawlProgress progress)
        {
            if (progress == null) return string.Empty;
            AchievementScrollObservation scroll = progress.ScrollObservation;
            string input = scroll == null
                ? "Requested input: none"
                : $"Requested: {scroll.RequestedNotches} notches / {scroll.RequestedLogicalWheelDelta} delta; sent: {scroll.WheelEventsSent} events";
            string movement = scroll == null
                ? "Observed movement: not available"
                : "Observed: " + (scroll.ObservedVisualDisplacementPixels.HasValue
                    ? $"visual {scroll.ObservedVisualDisplacementPixels.Value}px"
                    : "visual unavailable") + (scroll.ObservedRowDisplacementPixels.HasValue
                    ? $", rows {scroll.ObservedRowDisplacementPixels.Value:0.0}px"
                    : ", rows unavailable");
            AchievementCategoryFrameObservation frame = progress.FrameObservation;
            string extent = frame == null
                ? "Scrollbar: unavailable"
                : $"Scrollbar: top={frame.TopAtExtent}, bottom={frame.BottomAtExtent}";
            string detected = frame == null ? "unknown" : frame.DetectedRows.ToString(CultureInfo.InvariantCulture);
            return $"{input}\n{movement}\n{extent}\nDetected in current frame: {detected}\nRows: {progress.UniqueIdentifiedRows} unique, " +
                $"{progress.UnresolvedObservations} unresolved, {progress.AmbiguousGroups} ambiguous, " +
                $"{progress.ConflictingRows} conflicting, {progress.RepeatedObservations} repeated, " +
                $"{progress.PartialRowsRecovered} partial recovered, {progress.PartialRowsOutstanding} partial outstanding; " +
                $"movement gaps={progress.UnobservedMovementGaps}; state={progress.ScrollState}";
        }

        internal static string BuildSummary(AchievementBulkDiagnosticResult diagnostic)
        {
            if (diagnostic == null) return "No bulk-scrolling diagnostic result is available.";
            AchievementCategoryCrawlResult result = diagnostic.CrawlResult;
            if (result == null)
                return $"Diagnostic mode: {ModeName(diagnostic.Mode)}\nCategory: not verified\n" +
                    $"State: {diagnostic.State}\nReason: {diagnostic.Reason}";

            int requestedNotches = result.ScrollObservations.Sum(value => value.RequestedNotches);
            int requestedDelta = result.ScrollObservations.Sum(value => value.RequestedLogicalWheelDelta);
            int eventsSent = result.ScrollObservations.Sum(value => value.WheelEventsSent);
            string visual = JoinMeasurements(result.ScrollObservations
                .Where(value => value.ObservedVisualDisplacementPixels.HasValue)
                .Select(value => value.ObservedVisualDisplacementPixels.Value));
            string rows = JoinMeasurements(result.ScrollObservations
                .Where(value => value.ObservedRowDisplacementPixels.HasValue)
                .Select(value => (int)Math.Round(value.ObservedRowDisplacementPixels.Value)));
            AchievementCategoryFrameObservation last = result.FrameObservations.LastOrDefault();
            var builder = new StringBuilder(720);
            builder.AppendLine($"Diagnostic mode: {ModeName(diagnostic.Mode)}");
            builder.AppendLine($"Category: {(diagnostic.CategorySignature.Length > 0 ? diagnostic.CategorySignature : "not verified")}");
            builder.AppendLine($"State: {result.State}");
            builder.AppendLine($"Frames captured: {result.ProcessedFrames}");
            builder.AppendLine($"Scroll batches requested: {result.ScrollActions}");
            builder.AppendLine($"Requested input: {requestedNotches} notches, logical delta {requestedDelta}");
            builder.AppendLine($"Wheel events confirmed sent: {eventsSent}");
            builder.AppendLine($"Observed visual displacement by batch: {visual}");
            builder.AppendLine($"Observed row displacement by batch: {rows}");
            builder.AppendLine($"Latest scrollbar evidence: top={last?.TopAtExtent == true}, bottom={last?.BottomAtExtent == true}");
            builder.AppendLine($"Latest frame evidence: {last?.EvidenceDiagnostics ?? "unavailable"}");
            builder.AppendLine($"Positive top evidence: {result.FrameObservations.FirstOrDefault()?.TopAtExtent == true}");
            builder.AppendLine($"Positive bottom-position evidence: {diagnostic.HasBottomPositionEvidence}");
            builder.AppendLine($"Stable final viewport: {result.ScrollObservations.LastOrDefault()?.ViewportStable == true}");
            builder.AppendLine($"Traversal complete: {(result.IsComplete ? "yes" : "no")}" +
                (result.IsComplete ? $" - {result.CompletenessEvidence}" : string.Empty));
            builder.AppendLine($"Recognition coverage sufficient: {(result.RecognitionCoverageSufficient ? "yes" : "no")}");
            builder.AppendLine($"Trusted completion coverage sufficient: {(result.TrustedCompletionCoverageSufficient ? "yes" : "no")}");
            builder.AppendLine($"Screen validation: {result.Timing.FastScreenValidationCount} fast, " +
                $"{result.Timing.FullScreenValidationCount} full, {result.Timing.FullScreenFallbackCount} fallback");
            builder.AppendLine($"Completed-total evidence: {result.CompletedTotalObservations.Count} positive observations; " +
                $"last={result.LastObservedCompletedTotal?.ToString(CultureInfo.InvariantCulture) ?? "unresolved"} " +
                $"at frame {result.LastObservedCompletedTotalFrame?.ToString(CultureInfo.InvariantCulture) ?? "none"}; " +
                $"inconsistent={result.CompletedTotalInconsistent}; dedicated OCR={result.Timing.CompletedTotalOcrExecutions}; " +
                $"context reuse={result.Timing.CompletedTotalReuseCount}");
            builder.AppendLine($"Rows: {result.IdentifiedRows.Count} unique identified, {result.UnresolvedRows.Count} unresolved, " +
                $"{result.AmbiguousGroupCount} ambiguous groups, {result.ConflictingRows.Count} conflicting");
            builder.AppendLine($"Raw observations: {result.TotalRawObservations}; raw unresolved: {result.RawUnresolvedObservations}; " +
                $"reconciled to identified rows: {result.ReconciledUnresolvedObservations}; " +
                $"linked to previously identified rows: {result.ObservationsLinkedToPreviouslyIdentifiedRows}; " +
                $"outstanding unresolved: {result.OutstandingUnresolvedObservations}");
            builder.AppendLine($"Repeated canonical observations: {result.RepeatedObservations}; partial observations recovered: " +
                $"{result.PartialObservationsRecovered}; unmatched partial observations: {result.UnmatchedPartialObservations}; " +
                $"distinct unresolved intervals: {result.DistinctUnresolvedRowIntervals}");
            builder.AppendLine($"Partial rows recovered: {result.PartialRowsRecovered}; partial rows outstanding: " +
                $"{result.PartialRowsOutstanding}; reconciliation conflicts: {result.ReconciliationConflicts}; " +
                $"identified groups lacking trusted completion evidence: {result.IdentifiedGroupsLackingTrustedCompletionEvidence}");
            if (result.DiagnosticIntervals.Count > 0)
            {
                string intervalSummary = string.Join("; ", result.DiagnosticIntervals.Take(8)
                    .Select(value => $"#{value.IntervalNumber} frame {value.SourceFrameIndex}: {value.PrimaryCause}"));
                if (result.DiagnosticIntervals.Count > 8)
                    intervalSummary += $"; +{result.DiagnosticIntervals.Count - 8} more";
                builder.AppendLine($"Intervals needing review: {intervalSummary}");
            }
            builder.AppendLine($"Frame overlap: {result.OverlapVerifiedScrolls} batches verified; unobserved movement gaps: {result.UnobservedMovementGaps}");
            builder.AppendLine($"Traversal state: {result.FinalScrollState}");
            TimeSpan crawlerAnalysis = result.Timing.StabilizationAnalysis +
                result.Timing.FrameEvidenceDetection + result.Timing.ViewportFingerprinting +
                result.Timing.LayoutProcessing + result.Timing.ObservationCollection +
                result.Timing.MotionAnalysis + result.Timing.Reconciliation +
                result.Timing.ProgressReporting + result.Timing.OtherFrameProcessing;
            builder.AppendLine($"Top-level exclusive timing: capture={Milliseconds(result.Timing.Capture)} ms; scrolling={Milliseconds(result.Timing.Scrolling)} ms; " +
                $"settling={Milliseconds(result.Timing.Settling)} ms; frame reader={Milliseconds(result.Timing.FrameReader)} ms; " +
                $"crawler analysis={Milliseconds(crawlerAnalysis)} ms; " +
                $"unattributed={Milliseconds(result.Timing.Unattributed)} ms");
            builder.AppendLine($"Nested frame-reader timing (not additive): row detection={Milliseconds(result.Timing.RowDetection)} ms; " +
                $"title pipeline={Milliseconds(result.Timing.TitleOcr)} ms; completion pipeline={Milliseconds(result.Timing.CompletionOcr)} ms; " +
                $"reward pipeline={Milliseconds(result.Timing.RewardOcr)} ms; OCR engine={Milliseconds(result.Timing.OcrEngine)} ms");
            builder.AppendLine($"Elapsed: {result.Timing.Total.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms");
            builder.AppendLine($"Reason: {result.Reason}");
            return builder.ToString().TrimEnd();
        }

        internal static string BuildDiagnosticDetails(AchievementBulkDiagnosticResult diagnostic)
        {
            if (diagnostic == null)
                return "Achievement bulk diagnostic\nresult=unavailable";
            AchievementCategoryCrawlResult result = diagnostic.CrawlResult;
            if (result == null)
            {
                return Bound($"Achievement bulk diagnostic\nmode={ModeName(diagnostic.Mode)}\n" +
                    $"state={diagnostic.State}\nreason={Sanitize(diagnostic.Reason, 512)}");
            }

            var builder = new StringBuilder(4096);
            builder.AppendLine("Achievement bulk diagnostic details (sanitized; no screenshots or canonical IDs)");
            builder.AppendLine($"mode={ModeName(diagnostic.Mode)}; state={result.State}; traversalComplete={result.IsComplete}; " +
                $"identityCoverage={result.RecognitionCoverageSufficient}; trustedCompletionCoverage={result.TrustedCompletionCoverageSufficient}");
            builder.AppendLine($"frames={result.ProcessedFrames}; scrolls={result.ScrollActions}; diagnosticIntervals={result.DiagnosticIntervals.Count}; " +
                $"identityOutstanding={result.OutstandingIntervals.Count}; completionLimited={result.CompletionLimitedIntervals.Count}");
            builder.AppendLine($"reason={Sanitize(result.Reason, 512)}");
            builder.AppendLine("timing-semantics: top-level fields below are exclusive and additive; frame-reader and OCR breakdowns are nested and must not be added again");
            builder.AppendLine($"timing-exclusive-ms: capture={Milliseconds(result.Timing.Capture)}; scroll={Milliseconds(result.Timing.Scrolling)}; " +
                $"settle={Milliseconds(result.Timing.Settling)}; stabilizationAnalysis={Milliseconds(result.Timing.StabilizationAnalysis)}; " +
                $"frameEvidence={Milliseconds(result.Timing.FrameEvidenceDetection)}; viewportFingerprint={Milliseconds(result.Timing.ViewportFingerprinting)}; " +
                $"frameReader={Milliseconds(result.Timing.FrameReader)}; layout={Milliseconds(result.Timing.LayoutProcessing)}; " +
                $"observationCollection={Milliseconds(result.Timing.ObservationCollection)}; motion={Milliseconds(result.Timing.MotionAnalysis)}; " +
                $"reconciliation={Milliseconds(result.Timing.Reconciliation)}; progressReporting={Milliseconds(result.Timing.ProgressReporting)}; " +
                $"accounted={Milliseconds(result.Timing.AccountedExclusive)}; unattributed={Milliseconds(result.Timing.Unattributed)}; total={Milliseconds(result.Timing.Total)}");
            builder.AppendLine($"timing-nested-frame-reader-ms: screenDetection={Milliseconds(result.Timing.ScreenDetection)}; " +
                $"fastScreenValidation={Milliseconds(result.Timing.FastScreenValidation)}; fullScreenValidation={Milliseconds(result.Timing.FullScreenValidation)}; " +
                $"completedTotal={Milliseconds(result.Timing.CompletedTotal)}; rowDetection={Milliseconds(result.Timing.RowDetection)}; " +
                $"titleOcr={Milliseconds(result.Timing.TitleOcr)}; completionOcr={Milliseconds(result.Timing.CompletionOcr)}; " +
                $"rewardOcr={Milliseconds(result.Timing.RewardOcr)}; readerOther={Milliseconds(result.Timing.FrameReaderOther)}");
            builder.AppendLine($"validation-counts: fast={result.Timing.FastScreenValidationCount}; full={result.Timing.FullScreenValidationCount}; " +
                $"fallback={result.Timing.FullScreenFallbackCount}; completedTotalOcr={result.Timing.CompletedTotalOcrExecutions}; " +
                $"completedTotalReuse={result.Timing.CompletedTotalReuseCount}; positiveCompletedTotals={result.CompletedTotalObservations.Count}; " +
                $"lastCompletedTotal={result.LastObservedCompletedTotal?.ToString(CultureInfo.InvariantCulture) ?? "none"}; " +
                $"lastCompletedTotalFrame={result.LastObservedCompletedTotalFrame?.ToString(CultureInfo.InvariantCulture) ?? "none"}; " +
                $"completedTotalInconsistent={result.CompletedTotalInconsistent}");
            builder.AppendLine($"timing-nested-ocr-ms: cropResize={Milliseconds(result.Timing.OcrCropResize)}; " +
                $"preprocessing={Milliseconds(result.Timing.OcrPreprocessing)}; engine={Milliseconds(result.Timing.OcrEngine)}");

            builder.AppendLine("intervals:");
            foreach (AchievementUnresolvedIntervalDiagnostic interval in
                result.DiagnosticIntervals.Take(MaximumDetailedIntervals))
            {
                builder.AppendLine($"#{interval.IntervalNumber}: primary={interval.PrimaryCause}; secondary=" +
                    $"{Join(interval.SecondaryCauses)}; identityOutstanding={interval.IdentityOutstanding}");
                builder.AppendLine($"  sourceFrame={interval.SourceFrameIndex}; otherFrames={Join(interval.OtherFrameIndexes)}; " +
                    $"row={interval.RowIndex}; bounds={interval.NormalizedBoundsText}; visibility={interval.Visibility}");
                builder.AppendLine($"  ocrExcerpt={Quote(interval.OcrExcerpt)}; identity={interval.IdentityState}; " +
                    $"candidateCount={interval.CandidateCount}; completion={interval.CompletionState}");
                builder.AppendLine($"  geometry={interval.ReconciliationStatus}; rejection={Quote(interval.RejectionReason)}; " +
                    $"movementVerified={interval.MovementVerified}; overlapVerified={interval.OverlapVerified}");
                builder.AppendLine($"  canonicalGroupEstablished={interval.CandidateCanonicalGroupEstablished}; " +
                    $"exactTitleVerificationUsefulLater={interval.ExactTitleVerificationUsefulLater}");
            }
            if (result.DiagnosticIntervals.Count > MaximumDetailedIntervals)
                builder.AppendLine($"intervals-truncated={result.DiagnosticIntervals.Count - MaximumDetailedIntervals}");

            builder.AppendLine("per-frame-timing-ms (exclusive crawler stages; reader breakdown nested; aggregate capture also includes discarded stabilization candidates):");
            foreach (AchievementCategoryFrameObservation frame in result.FrameObservations.Take(MaximumDetailedFrames))
            {
                AchievementCategoryFrameTiming timing = frame.Timing;
                AchievementBulkFrameTiming reader = timing.ReaderTiming;
                builder.AppendLine($"frame={frame.FrameIndex}; rows={frame.DetectedRows}; capture={Milliseconds(timing.Capture)}; " +
                    $"evidence={Milliseconds(timing.EvidenceDetection)}; fingerprint={Milliseconds(timing.ViewportFingerprint)}; " +
                    $"reader={Milliseconds(timing.FrameReader)}; layout={Milliseconds(timing.LayoutProcessing)}; " +
                    $"observation={Milliseconds(timing.ObservationCollection)}; other={Milliseconds(timing.ProcessingOther)}; " +
                    $"readerRowDetection={Milliseconds(reader?.RowDetection ?? TimeSpan.Zero)}; " +
                    $"readerTitleOcr={Milliseconds(reader?.TitleOcr ?? TimeSpan.Zero)}; " +
                    $"readerCompletionOcr={Milliseconds(reader?.CompletionOcr ?? TimeSpan.Zero)}; " +
                    $"readerOcrEngine={Milliseconds(reader?.OcrEngine ?? TimeSpan.Zero)}; " +
                    $"screenValidation={frame.ScreenValidationPath}; fallback={Quote(frame.ScreenValidationFallbackReason)}; " +
                    $"completedTotalSource={frame.CompletedTotalEvidence.Source}; completedTotalObserved={frame.CompletedTotalEvidence.PositivelyObserved}; " +
                    $"completedTotalReused={frame.CompletedTotalEvidence.Reused}; completedTotalObservationFrame={frame.CompletedTotalEvidence.ObservationFrameIndex?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
            }
            if (result.FrameObservations.Count > MaximumDetailedFrames)
                builder.AppendLine($"frames-truncated={result.FrameObservations.Count - MaximumDetailedFrames}");
            return Bound(builder.ToString().TrimEnd());
        }

        private static string Join<T>(System.Collections.Generic.IEnumerable<T> values)
        {
            string[] content = (values ?? Array.Empty<T>())
                .Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)).ToArray();
            return content.Length == 0 ? "none" : string.Join(",", content);
        }

        private static string Quote(string value) => '"' + Sanitize(value, 512).Replace("\"", "'") + '"';

        private static string Sanitize(string value, int maximumLength) =>
            AchievementUnresolvedIntervalDiagnostic.Sanitize(value, maximumLength);

        private static string Bound(string value)
        {
            if ((value ?? string.Empty).Length <= MaximumDiagnosticDetailCharacters) return value ?? string.Empty;
            const string suffix = "\n[diagnostic details truncated at the fixed size limit]";
            return value.Substring(0, MaximumDiagnosticDetailCharacters - suffix.Length) + suffix;
        }

        private static string JoinMeasurements(System.Collections.Generic.IEnumerable<int> values)
        {
            int[] measurements = values.ToArray();
            return measurements.Length == 0
                ? "not measured"
                : string.Join(", ", measurements.Select(value => value.ToString(CultureInfo.InvariantCulture) + "px"));
        }

        private static string Milliseconds(TimeSpan value) =>
            value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture);

        internal static string ModeName(AchievementBulkDiagnosticMode mode) =>
            mode == AchievementBulkDiagnosticMode.FullTraversal
                ? "Verify Full Category Traversal"
                : "Test Bulk Scrolling";
    }

    internal static class AchievementBulkDiagnosticUiPolicy
    {
        internal const string RunningStatus = "Bulk diagnostic";
        internal const string CancelledStatus = "Diagnostic cancelled";
        internal const string FinishedStatus = "Diagnostic finished";
        internal const string AbortedStatus = "Diagnostic aborted";

        internal static bool IsControlVisible(bool achievementModeSelected) => achievementModeSelected;
        internal static bool IsControlEnabled(bool achievementModeSelected, bool activeRun) =>
            achievementModeSelected && !activeRun;
    }
}
