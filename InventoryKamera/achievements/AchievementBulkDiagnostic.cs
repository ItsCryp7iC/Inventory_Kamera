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
    internal sealed class AchievementBulkDiagnosticOptions
    {
        internal const int DefaultMaximumCapturedFrames = 5;
        internal const int DefaultMaximumScrollBatches = 4;
        internal static AchievementBulkDiagnosticOptions Default { get; } = new();

        internal AchievementBulkDiagnosticOptions(
            int maximumCapturedFrames = DefaultMaximumCapturedFrames,
            int maximumScrollBatches = DefaultMaximumScrollBatches,
            int wheelNotchesPerBatch = 21)
        {
            if (maximumCapturedFrames < 1 || maximumCapturedFrames > 6)
                throw new ArgumentOutOfRangeException(nameof(maximumCapturedFrames));
            if (maximumScrollBatches < 1 || maximumScrollBatches > 5)
                throw new ArgumentOutOfRangeException(nameof(maximumScrollBatches));
            if (wheelNotchesPerBatch < 1 || wheelNotchesPerBatch > 24)
                throw new ArgumentOutOfRangeException(nameof(wheelNotchesPerBatch));
            MaximumCapturedFrames = maximumCapturedFrames;
            MaximumScrollBatches = maximumScrollBatches;
            WheelNotchesPerBatch = wheelNotchesPerBatch;
        }

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
        internal AchievementBulkDiagnosticResult(AchievementCategoryCrawlResult crawlResult)
        {
            CrawlResult = crawlResult ?? throw new ArgumentNullException(nameof(crawlResult));
            State = crawlResult.State;
            Reason = crawlResult.Reason;
        }

        internal AchievementBulkDiagnosticResult(AchievementCategoryCrawlState state, string reason)
        {
            State = state;
            Reason = reason ?? string.Empty;
        }

        internal AchievementCategoryCrawlResult CrawlResult { get; }
        internal AchievementCategoryCrawlState State { get; }
        internal string Reason { get; }
        internal string CategorySignature => CrawlResult?.FrameObservations
            .Where(frame => frame.CategorySignatureVerified)
            .Select(frame => frame.CategorySignature)
            .FirstOrDefault() ?? string.Empty;
        internal bool HasBottomProof => CrawlResult?.IsComplete == true;
    }

    /// <summary>
    /// Read-only application orchestration for one manually selected category. Its dependency
    /// surface intentionally contains no persistence, export, reward, or completion-update service.
    /// </summary>
    internal sealed class AchievementBulkDiagnosticRunner
    {
        private readonly IGameScreenCapture screenCapture;
        private readonly IAchievementCategoryCrawler crawler;
        private int active;

        internal AchievementBulkDiagnosticRunner(
            IGameScreenCapture screenCapture,
            IAchievementCategoryCrawler crawler)
        {
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.crawler = crawler ?? throw new ArgumentNullException(nameof(crawler));
        }

        internal bool IsActive => Volatile.Read(ref active) != 0;

        internal AchievementBulkDiagnosticResult Run(
            CancellationToken cancellationToken,
            IProgress<AchievementCategoryCrawlProgress> progress = null)
        {
            if (Interlocked.CompareExchange(ref active, 1, 0) != 0)
                return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Failed,
                    "A bulk-scrolling diagnostic is already active.");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stopwatch capture = Stopwatch.StartNew();
                using Bitmap startingFrame = screenCapture.CaptureWindow();
                capture.Stop();
                if (startingFrame == null)
                    return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Failed,
                        "The initial Genshin capture returned no image.");
                cancellationToken.ThrowIfCancellationRequested();

                AchievementCategoryCrawlResult result = crawler.Crawl(
                    new AchievementCategoryCrawlContext(
                        "Manually selected category",
                        requireVerifiedCategorySignature: true),
                    startingFrame,
                    cancellationToken,
                    progress,
                    capture.Elapsed);
                return new AchievementBulkDiagnosticResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Cancelled,
                    "The bulk-scrolling diagnostic was cancelled before crawler evidence was available.");
            }
            catch (Exception ex)
            {
                return new AchievementBulkDiagnosticResult(AchievementCategoryCrawlState.Failed,
                    $"The bulk-scrolling diagnostic failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Volatile.Write(ref active, 0);
            }
        }
    }

    internal static class AchievementBulkDiagnosticReportBuilder
    {
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
                $"{progress.UnresolvedObservations} unresolved, {progress.RepeatedObservations} repeated, " +
                $"{progress.PartialRowsRecovered} partial recovered; state={progress.ScrollState}";
        }

        internal static string BuildSummary(AchievementBulkDiagnosticResult diagnostic)
        {
            if (diagnostic == null) return "No bulk-scrolling diagnostic result is available.";
            AchievementCategoryCrawlResult result = diagnostic.CrawlResult;
            if (result == null)
                return $"Category: not verified\nState: {diagnostic.State}\nReason: {diagnostic.Reason}";

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
            builder.AppendLine($"Bottom proof: {(result.IsComplete ? result.CompletenessEvidence : "not established")}");
            builder.AppendLine($"Rows: {result.IdentifiedRows.Count} unique identified, {result.UnresolvedRows.Count} unresolved, {result.ConflictingRows.Count} conflicting");
            builder.AppendLine($"Repeated observations: {result.RepeatedObservations}; partial rows recovered: {result.PartialRowsRecovered}");
            builder.AppendLine($"Traversal state: {result.FinalScrollState}");
            builder.AppendLine($"Elapsed: {result.Timing.Total.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms");
            builder.AppendLine($"Reason: {result.Reason}");
            return builder.ToString().TrimEnd();
        }

        private static string JoinMeasurements(System.Collections.Generic.IEnumerable<int> values)
        {
            int[] measurements = values.ToArray();
            return measurements.Length == 0
                ? "not measured"
                : string.Join(", ", measurements.Select(value => value.ToString(CultureInfo.InvariantCulture) + "px"));
        }
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
