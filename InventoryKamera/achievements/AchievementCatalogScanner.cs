using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    internal interface IAchievementCatalogScanProgressReporter
    {
        void Report(AchievementCatalogScanProgress progress);
    }

    internal enum AchievementCatalogScanState
    {
        Completed,
        Cancelled,
        StoppedUnsafe,
        Failed,
    }

    internal sealed class AchievementCatalogScanProgress
    {
        internal AchievementCatalogScanProgress(
            int currentIndex,
            int totalTargets,
            string currentTitle,
            int? currentRequestedId,
            ScanCounts counts)
        {
            CurrentIndex = currentIndex;
            TotalTargets = totalTargets;
            CurrentTitle = currentTitle ?? string.Empty;
            CurrentRequestedId = currentRequestedId;
            CompletedCount = counts.Completed;
            IncompleteCount = counts.Incomplete;
            UnresolvedCount = counts.Unresolved;
            AmbiguousCount = counts.Ambiguous;
            PendingClaimCount = counts.PendingClaim;
            FailedCount = counts.Failed;
        }

        internal int CurrentIndex { get; }
        internal int TotalTargets { get; }
        internal string CurrentTitle { get; }
        internal int? CurrentRequestedId { get; }
        internal int CompletedCount { get; }
        internal int IncompleteCount { get; }
        internal int UnresolvedCount { get; }
        internal int AmbiguousCount { get; }
        internal int PendingClaimCount { get; }
        internal int FailedCount { get; }
    }

    internal sealed class AchievementCatalogScanResult
    {
        internal AchievementCatalogScanResult(
            AchievementCatalogScanState state,
            int totalTargets,
            IEnumerable<AchievementSingleScanResult> results,
            ScanCounts counts,
            string stopReason,
            string diagnostics)
        {
            State = state;
            TotalTargets = totalTargets;
            Results = new ReadOnlyCollection<AchievementSingleScanResult>(
                (results ?? Array.Empty<AchievementSingleScanResult>()).ToArray());
            ProcessedTargets = Results.Count;
            CompletedCount = counts.Completed;
            IncompleteCount = counts.Incomplete;
            UnresolvedCount = counts.Unresolved;
            PendingClaimCount = counts.PendingClaim;
            AmbiguousCount = counts.Ambiguous;
            FailedCount = counts.Failed;
            CancelledCount = counts.Cancelled;
            StoppedEarly = ProcessedTargets < TotalTargets;
            StopReason = stopReason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementCatalogScanState State { get; }
        internal int TotalTargets { get; }
        internal int ProcessedTargets { get; }
        internal int CompletedCount { get; }
        internal int IncompleteCount { get; }
        internal int UnresolvedCount { get; }
        internal int PendingClaimCount { get; }
        internal int AmbiguousCount { get; }
        internal int FailedCount { get; }
        internal int CancelledCount { get; }
        internal IReadOnlyList<AchievementSingleScanResult> Results { get; }
        internal bool StoppedEarly { get; }
        internal string StopReason { get; }
        internal string Diagnostics { get; }
    }

    internal sealed class AchievementCatalogScanner
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IAchievementSingleScanner singleScanner;

        internal AchievementCatalogScanner(
            IAchievementCatalogProvider catalogProvider,
            IAchievementSingleScanner singleScanner)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            AchievementCatalog catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            this.singleScanner = singleScanner ?? throw new ArgumentNullException(nameof(singleScanner));
            Targets = AchievementScanTarget.FromCatalog(catalog);
        }

        internal IReadOnlyList<AchievementScanTarget> Targets { get; }

        internal void PrepareForScan(CancellationToken cancellationToken) =>
            singleScanner.PrepareForScan(cancellationToken);

        internal void ReacquireAfterUserInteraction(CancellationToken cancellationToken) =>
            singleScanner.ReacquireAfterUserInteraction(cancellationToken);

        internal AchievementCatalogScanResult Scan(
            CancellationToken cancellationToken,
            IAchievementCatalogScanProgressReporter progress = null) =>
            ScanTargets(Targets, cancellationToken, progress);

        internal AchievementCatalogScanResult ScanTargets(
            IReadOnlyList<AchievementScanTarget> targets,
            CancellationToken cancellationToken,
            IAchievementCatalogScanProgressReporter progress = null)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));

            var results = new List<AchievementSingleScanResult>(targets.Count);
            var counts = new ScanCounts();

            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    AchievementCatalogScanState.Cancelled,
                    targets.Count,
                    results,
                    counts,
                    "Full achievement scanning was cancelled before the first target.");

            if (!TryReport(
                progress,
                new AchievementCatalogScanProgress(0, targets.Count, string.Empty, null, counts),
                out string progressFailure))
            {
                return Finish(
                    AchievementCatalogScanState.Failed,
                    targets.Count,
                    results,
                    counts,
                    progressFailure);
            }

            foreach (AchievementScanTarget target in targets)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Finish(
                        AchievementCatalogScanState.Cancelled,
                        targets.Count,
                        results,
                        counts,
                        "Full achievement scanning was cancelled before the next target.");

                AchievementSingleScanResult result;
                try
                {
                    result = singleScanner.Scan(target.RequestedCanonicalId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Finish(
                        AchievementCatalogScanState.Cancelled,
                        targets.Count,
                        results,
                        counts,
                        "Full achievement scanning was cancelled during the delegated single scan.");
                }
                catch (Exception ex)
                {
                    return Finish(
                        AchievementCatalogScanState.Failed,
                        targets.Count,
                        results,
                        counts,
                        $"The delegated single scanner threw {ex.GetType().Name}: {ex.Message}");
                }

                if (result == null)
                    return Finish(
                        AchievementCatalogScanState.Failed,
                        targets.Count,
                        results,
                        counts,
                        "The delegated single scanner returned no structured result.");

                results.Add(result);
                counts.Add(result.State);

                bool progressReported = TryReport(
                    progress,
                    new AchievementCatalogScanProgress(
                        results.Count,
                        targets.Count,
                        target.CanonicalTitle,
                        target.RequestedCanonicalId,
                        counts),
                    out progressFailure);

                // Stop precedence is deterministic: cancellation, unsafe reset, operational
                // failure, then progress callback failure.
                if (cancellationToken.IsCancellationRequested ||
                    result.State == AchievementSingleScanState.Cancelled)
                {
                    return Finish(
                        AchievementCatalogScanState.Cancelled,
                        targets.Count,
                        results,
                        counts,
                        result.Reason.Length > 0
                            ? result.Reason
                            : "The delegated single scan was cancelled.");
                }

                if (!result.ReadyForNextAchievement)
                    return Finish(
                        AchievementCatalogScanState.StoppedUnsafe,
                        targets.Count,
                        results,
                        counts,
                        $"Target '{target.CanonicalTitle}' did not positively return to Idle. {result.Reason}");

                if (result.State == AchievementSingleScanState.Failed)
                    return Finish(
                        AchievementCatalogScanState.Failed,
                        targets.Count,
                        results,
                        counts,
                        $"Target '{target.CanonicalTitle}' reported an operational failure. {result.Reason}");

                if (!progressReported)
                    return Finish(
                        AchievementCatalogScanState.Failed,
                        targets.Count,
                        results,
                        counts,
                        progressFailure);
            }

            return Finish(
                AchievementCatalogScanState.Completed,
                targets.Count,
                results,
                counts,
                "All achievement scan targets were processed.");
        }

        private AchievementCatalogScanResult Finish(
            AchievementCatalogScanState state,
            int totalTargets,
            IReadOnlyList<AchievementSingleScanResult> results,
            ScanCounts counts,
            string stopReason)
        {
            string boundedReason = Bound(stopReason, 240);
            bool stoppedEarly = results.Count < totalTargets;
            string diagnostics = BuildDiagnostics(
                state,
                totalTargets,
                results.Count,
                counts,
                stoppedEarly,
                boundedReason);
            if (state == AchievementCatalogScanState.Completed)
                Logger.Info(diagnostics);
            else
                Logger.Warn(diagnostics);
            return new AchievementCatalogScanResult(
                state,
                totalTargets,
                results,
                counts,
                boundedReason,
                diagnostics);
        }

        private static bool TryReport(
            IAchievementCatalogScanProgressReporter progress,
            AchievementCatalogScanProgress value,
            out string failure)
        {
            failure = null;
            if (progress == null) return true;
            try
            {
                progress.Report(value);
                return true;
            }
            catch (Exception ex)
            {
                failure = $"Achievement scan progress reporting failed: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static string BuildDiagnostics(
            AchievementCatalogScanState state,
            int totalTargets,
            int processedTargets,
            ScanCounts counts,
            bool stoppedEarly,
            string stopReason)
        {
            var builder = new StringBuilder(640);
            builder.AppendLine("operation=achievement-catalog-scan");
            builder.AppendLine($"totalTargets={totalTargets.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"processedTargets={processedTargets.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"completed={counts.Completed.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"incomplete={counts.Incomplete.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"unresolved={counts.Unresolved.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"ambiguous={counts.Ambiguous.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"pendingClaim={counts.PendingClaim.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"failed={counts.Failed.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"cancelled={counts.Cancelled.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"stoppedEarly={stoppedEarly}");
            builder.AppendLine($"state={state}");
            builder.AppendLine($"stopReason={stopReason}");
            return builder.ToString().TrimEnd();
        }

        private static string Bound(string value, int maximumLength)
        {
            string cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length <= maximumLength
                ? cleaned
                : cleaned.Substring(0, maximumLength) + "...";
        }
    }

    internal sealed class ScanCounts
    {
        internal int Completed { get; private set; }
        internal int Incomplete { get; private set; }
        internal int Unresolved { get; private set; }
        internal int Ambiguous { get; private set; }
        internal int PendingClaim { get; private set; }
        internal int Failed { get; private set; }
        internal int Cancelled { get; private set; }

        internal void Add(AchievementSingleScanState state)
        {
            switch (state)
            {
                case AchievementSingleScanState.Completed:
                    Completed++;
                    break;
                case AchievementSingleScanState.Incomplete:
                    Incomplete++;
                    break;
                case AchievementSingleScanState.Unresolved:
                    Unresolved++;
                    break;
                case AchievementSingleScanState.AmbiguousCollision:
                case AchievementSingleScanState.AmbiguousMultiMatch:
                    Ambiguous++;
                    break;
                case AchievementSingleScanState.PendingClaimRequiresUserAction:
                    PendingClaim++;
                    break;
                case AchievementSingleScanState.Failed:
                    Failed++;
                    break;
                case AchievementSingleScanState.Cancelled:
                    Cancelled++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, "Unsupported single-scan state.");
            }
        }
    }
}
