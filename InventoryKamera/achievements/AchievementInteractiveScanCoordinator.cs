using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

namespace InventoryKamera
{
    internal sealed class AchievementTimingSummary
    {
        private AchievementTimingSummary(
            int count,
            double mean,
            double median,
            double p95,
            TimingBucket fastPath,
            TimingBucket recovered)
        {
            CompletedTargetCount = count;
            MeanTotalMilliseconds = mean;
            MedianTotalMilliseconds = median;
            P95TotalMilliseconds = p95;
            FastPath = fastPath;
            Recovered = recovered;
        }

        internal int CompletedTargetCount { get; }
        internal double MeanTotalMilliseconds { get; }
        internal double MedianTotalMilliseconds { get; }
        internal double P95TotalMilliseconds { get; }
        internal TimingBucket FastPath { get; }
        internal TimingBucket Recovered { get; }

        internal sealed class TimingBucket
        {
            internal TimingBucket(int count, double mean, double median, double p95)
            {
                Count = count;
                Mean = mean;
                Median = median;
                P95 = p95;
            }
            internal int Count { get; }
            internal double Mean { get; }
            internal double Median { get; }
            internal double P95 { get; }
        }

        internal static AchievementTimingSummary Calculate(
            IEnumerable<AchievementSingleScanResult> results)
        {
            AchievementSingleScanResult[] completed = (results ?? Array.Empty<AchievementSingleScanResult>())
                .Where(result => result?.ReadyForNextAchievement == true && result.Timing != null)
                .ToArray();
            TimingBucket all = CalculateBucket(completed.Select(result => result.Timing.TotalMilliseconds));
            TimingBucket fast = CalculateBucket(completed
                .Where(result => !result.Timing.RecognitionRecoveryUsed)
                .Select(result => result.Timing.TotalMilliseconds));
            TimingBucket recovered = CalculateBucket(completed
                .Where(result => result.Timing.RecognitionRecoveryUsed)
                .Select(result => result.Timing.TotalMilliseconds));
            return new AchievementTimingSummary(
                all.Count, all.Mean, all.Median, all.P95, fast, recovered);
        }

        private static TimingBucket CalculateBucket(IEnumerable<long> source)
        {
            long[] values = source.OrderBy(value => value).ToArray();
            if (values.Length == 0) return new TimingBucket(0, 0, 0, 0);
            double mean = values.Average(value => (double)value);
            double median = values.Length % 2 == 1
                ? values[values.Length / 2]
                : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2.0;
            int p95Index = Math.Max(0, (int)Math.Ceiling(values.Length * 0.95) - 1);
            return new TimingBucket(values.Length, mean, median, values[p95Index]);
        }

        internal string Diagnostics => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "operation=achievement-timing-summary; completedTargets={0}; meanTotalMs={1:0.0}; medianTotalMs={2:0.0}; p95TotalMs={3:0.0}; fastPathCount={4}; fastPathMeanMs={5:0.0}; fastPathMedianMs={6:0.0}; fastPathP95Ms={7:0.0}; recoveredCount={8}; recoveredMeanMs={9:0.0}; recoveredMedianMs={10:0.0}; recoveredP95Ms={11:0.0}",
            CompletedTargetCount,
            MeanTotalMilliseconds,
            MedianTotalMilliseconds,
            P95TotalMilliseconds,
            FastPath.Count,
            FastPath.Mean,
            FastPath.Median,
            FastPath.P95,
            Recovered.Count,
            Recovered.Mean,
            Recovered.Median,
            Recovered.P95);
    }

    internal enum AchievementPendingClaimDecision
    {
        Resume,
        SkipAchievement,
        CancelScan,
    }

    internal enum AchievementUserSkippedReason
    {
        PendingUnclaimedReward,
        UserSkipped,
    }

    internal sealed class AchievementUserSkippedTarget
    {
        internal AchievementUserSkippedTarget(
            AchievementScanTarget target,
            AchievementUserSkippedReason reason)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            CanonicalTitle = target.CanonicalTitle;
            RequestedCanonicalId = target.RequestedCanonicalId;
            CandidateIds = new ReadOnlyCollection<int>(target.CandidateIds.ToArray());
            Reason = reason;
        }

        internal string CanonicalTitle { get; }
        internal int RequestedCanonicalId { get; }
        internal IReadOnlyList<int> CandidateIds { get; }
        internal AchievementUserSkippedReason Reason { get; }
    }

    internal sealed class AchievementPendingClaimRequest
    {
        internal AchievementPendingClaimRequest(
            AchievementScanTarget target,
            AchievementSingleScanResult result,
            int priorResumeCount)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            PriorResumeCount = priorResumeCount;
        }

        internal AchievementScanTarget Target { get; }
        internal AchievementSingleScanResult Result { get; }
        internal int PriorResumeCount { get; }
    }

    internal interface IAchievementPendingClaimDecisionProvider
    {
        AchievementPendingClaimDecision Decide(
            AchievementPendingClaimRequest request,
            CancellationToken cancellationToken);
    }

    internal interface IAchievementInteractiveScanProgressReporter
    {
        void Report(AchievementInteractiveScanProgress progress);
    }

    internal sealed class AchievementInteractiveScanProgress
    {
        internal AchievementInteractiveScanProgress(
            int totalCatalogTargets,
            int plannedScanTargets,
            int processedScanTargets,
            int skippedTrustedTargets,
            int scanAttemptCount,
            string currentTitle,
            int completedCount,
            int incompleteCount,
            int unresolvedCount,
            int ambiguousCount,
            int pendingClaimCount,
            int userSkippedCount,
            int trustedCompletedCount)
        {
            TotalCatalogTargets = totalCatalogTargets;
            PlannedScanTargets = plannedScanTargets;
            ProcessedScanTargets = processedScanTargets;
            SkippedTrustedTargets = skippedTrustedTargets;
            ScanAttemptCount = scanAttemptCount;
            CurrentTitle = currentTitle ?? string.Empty;
            CompletedCount = completedCount;
            IncompleteCount = incompleteCount;
            UnresolvedCount = unresolvedCount;
            AmbiguousCount = ambiguousCount;
            PendingClaimCount = pendingClaimCount;
            UserSkippedCount = userSkippedCount;
            TrustedCompletedCount = trustedCompletedCount;
        }

        internal int TotalCatalogTargets { get; }
        internal int PlannedScanTargets { get; }
        internal int ProcessedScanTargets { get; }
        internal int SkippedTrustedTargets { get; }
        internal int ScanAttemptCount { get; }
        internal string CurrentTitle { get; }
        internal int CompletedCount { get; }
        internal int IncompleteCount { get; }
        internal int UnresolvedCount { get; }
        internal int AmbiguousCount { get; }
        internal int PendingClaimCount { get; }
        internal int UserSkippedCount { get; }
        internal int TrustedCompletedCount { get; }
    }

    internal sealed class AchievementInteractiveScanResult
    {
        internal AchievementInteractiveScanResult(
            AchievementCatalogScanState state,
            int totalCatalogTargets,
            int plannedScanTargets,
            int skippedTrustedTargets,
            int processedScanTargets,
            int scanAttemptCount,
            int previousTrustedCompletedCount,
            IEnumerable<int> trustedCompletedIds,
            IEnumerable<AchievementSingleScanResult> results,
            IEnumerable<AchievementUserSkippedTarget> userSkippedTargets,
            int completedCount,
            int incompleteCount,
            int unresolvedCount,
            int ambiguousCount,
            int pendingClaimCount,
            int failedCount,
            string stopReason)
        {
            State = state;
            TotalCatalogTargets = totalCatalogTargets;
            PlannedScanTargets = plannedScanTargets;
            SkippedTrustedTargets = skippedTrustedTargets;
            ProcessedScanTargets = processedScanTargets;
            ScanAttemptCount = scanAttemptCount;
            PreviousTrustedCompletedCount = previousTrustedCompletedCount;
            TrustedCompletedIds = new ReadOnlyCollection<int>(
                (trustedCompletedIds ?? Array.Empty<int>()).Distinct().OrderBy(id => id).ToArray());
            NewlyTrustedCompletedCount = Math.Max(
                0,
                TrustedCompletedIds.Count - PreviousTrustedCompletedCount);
            Results = new ReadOnlyCollection<AchievementSingleScanResult>(
                (results ?? Array.Empty<AchievementSingleScanResult>()).ToArray());
            UserSkippedTargets = new ReadOnlyCollection<AchievementUserSkippedTarget>(
                (userSkippedTargets ?? Array.Empty<AchievementUserSkippedTarget>()).ToArray());
            CompletedCount = completedCount;
            IncompleteCount = incompleteCount;
            UnresolvedCount = unresolvedCount;
            AmbiguousCount = ambiguousCount;
            PendingClaimCount = pendingClaimCount;
            FailedCount = failedCount;
            StopReason = stopReason ?? string.Empty;
            TimingSummary = AchievementTimingSummary.Calculate(Results);
        }

        internal AchievementCatalogScanState State { get; }
        internal int TotalCatalogTargets { get; }
        internal int PlannedScanTargets { get; }
        internal int SkippedTrustedTargets { get; }
        internal int ProcessedScanTargets { get; }
        internal int RemainingScanTargets => Math.Max(0, PlannedScanTargets - ProcessedScanTargets);
        internal int ScanAttemptCount { get; }
        internal int PreviousTrustedCompletedCount { get; }
        internal int NewlyTrustedCompletedCount { get; }
        internal IReadOnlyList<int> TrustedCompletedIds { get; }
        internal IReadOnlyList<AchievementSingleScanResult> Results { get; }
        internal IReadOnlyList<AchievementUserSkippedTarget> UserSkippedTargets { get; }
        internal int CompletedCount { get; }
        internal int IncompleteCount { get; }
        internal int UnresolvedCount { get; }
        internal int AmbiguousCount { get; }
        internal int PendingClaimCount { get; }
        internal int FailedCount { get; }
        internal bool IsUpdateScan => PreviousTrustedCompletedCount > 0 || SkippedTrustedTargets > 0;
        internal bool IsPartial => State != AchievementCatalogScanState.Completed ||
            UserSkippedTargets.Count > 0 || UnresolvedCount > 0 || AmbiguousCount > 0;
        internal string StopReason { get; }
        internal AchievementTimingSummary TimingSummary { get; }
    }

    /// <summary>
    /// Adds only the user-driven Pending Claim branch around the deterministic Phase 6H scan core.
    /// Each delegated slice contains one canonical title target, preserving the lower-level safety
    /// gates while allowing an explicit Resume to repeat that exact target.
    /// </summary>
    internal sealed class AchievementInteractiveScanCoordinator
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly AchievementCatalog catalog;
        private readonly AchievementCatalogScanner catalogScanner;
        private readonly IAchievementPendingClaimDecisionProvider pendingDecisionProvider;
        private readonly Action requestCancellation;

        internal AchievementInteractiveScanCoordinator(
            IAchievementCatalogProvider catalogProvider,
            AchievementCatalogScanner catalogScanner,
            IAchievementPendingClaimDecisionProvider pendingDecisionProvider,
            Action requestCancellation)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            this.catalogScanner = catalogScanner ?? throw new ArgumentNullException(nameof(catalogScanner));
            this.pendingDecisionProvider = pendingDecisionProvider ??
                throw new ArgumentNullException(nameof(pendingDecisionProvider));
            this.requestCancellation = requestCancellation ?? throw new ArgumentNullException(nameof(requestCancellation));
        }

        internal AchievementInteractiveScanResult Scan(
            AchievementResumeSeed resumeSeed,
            CancellationToken cancellationToken,
            IAchievementInteractiveScanProgressReporter progress = null)
        {
            IReadOnlyList<AchievementScanTarget> targets;
            int skippedTrustedTargets;
            var trustedIds = new HashSet<int>();

            if (resumeSeed == null)
            {
                targets = catalogScanner.Targets;
                skippedTrustedTargets = 0;
            }
            else
            {
                if (!AchievementUpdatePlan.TryCreate(
                    catalog,
                    resumeSeed,
                    out AchievementUpdatePlan plan,
                    out IReadOnlyList<int> invalidIds,
                    out string failureReason))
                {
                    string invalid = invalidIds.Count == 0
                        ? string.Empty
                        : $" Invalid IDs: {string.Join(", ", invalidIds)}.";
                    return EmptyResult(
                        AchievementCatalogScanState.Failed,
                        catalogScanner.Targets.Count,
                        failureReason + invalid);
                }

                targets = plan.ScanTargets;
                skippedTrustedTargets = plan.SkippedTargets.Count;
                trustedIds.UnionWith(plan.TrustedCompletedIds);
            }

            int previousTrustedCount = trustedIds.Count;
            var results = new List<AchievementSingleScanResult>(targets.Count);
            var skipped = new List<AchievementUserSkippedTarget>();
            int processed = 0;
            int attempts = 0;
            int completed = 0;
            int incomplete = 0;
            int unresolved = 0;
            int ambiguous = 0;
            int pending = 0;
            int failed = 0;

            AchievementInteractiveScanResult Finish(
                AchievementCatalogScanState state,
                string reason)
            {
                var result = new AchievementInteractiveScanResult(
                    state,
                    catalogScanner.Targets.Count,
                    targets.Count,
                    skippedTrustedTargets,
                    processed,
                    attempts,
                    previousTrustedCount,
                    trustedIds,
                    results,
                    skipped,
                    completed,
                    incomplete,
                    unresolved,
                    ambiguous,
                    pending,
                    failed,
                    reason);
                Logger.Info(result.TimingSummary.Diagnostics);
                return result;
            }

            bool Report(string title, out string failure)
            {
                failure = null;
                if (progress == null) return true;
                try
                {
                    progress.Report(new AchievementInteractiveScanProgress(
                        catalogScanner.Targets.Count,
                        targets.Count,
                        processed,
                        skippedTrustedTargets,
                        attempts,
                        title,
                        completed,
                        incomplete,
                        unresolved,
                        ambiguous,
                        pending,
                        skipped.Count,
                        trustedIds.Count));
                    return true;
                }
                catch (Exception ex)
                {
                    failure = $"Achievement progress reporting failed: {ex.GetType().Name}: {ex.Message}";
                    return false;
                }
            }

            if (cancellationToken.IsCancellationRequested)
                return Finish(AchievementCatalogScanState.Cancelled,
                    "Achievement scanning was cancelled before the first target.");
            if (!Report(string.Empty, out string progressFailure))
                return Finish(AchievementCatalogScanState.Failed, progressFailure);

            try
            {
                catalogScanner.PrepareForScan(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(AchievementCatalogScanState.Cancelled,
                    "Achievement scanning was cancelled while positioning the search pointer.");
            }
            catch (Exception ex)
            {
                return Finish(AchievementCatalogScanState.Failed,
                    $"Achievement search pointer preparation failed: {ex.GetType().Name}: {ex.Message}");
            }

            foreach (AchievementScanTarget target in targets)
            {
                int resumeCount = 0;
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return Finish(AchievementCatalogScanState.Cancelled,
                            "Achievement scanning was cancelled before the next target attempt.");

                    attempts++;
                    AchievementCatalogScanResult slice = catalogScanner.ScanTargets(
                        Array.AsReadOnly(new[] { target }),
                        cancellationToken);
                    AchievementSingleScanResult result = slice.Results.SingleOrDefault();

                    if (slice.State == AchievementCatalogScanState.Cancelled ||
                        cancellationToken.IsCancellationRequested)
                    {
                        return Finish(AchievementCatalogScanState.Cancelled, slice.StopReason);
                    }
                    if (slice.State == AchievementCatalogScanState.StoppedUnsafe)
                    {
                        if (result != null) results.Add(result);
                        return Finish(AchievementCatalogScanState.StoppedUnsafe, slice.StopReason);
                    }
                    if (slice.State == AchievementCatalogScanState.Failed || result == null)
                    {
                        if (result != null)
                        {
                            results.Add(result);
                            failed++;
                        }
                        return Finish(
                            AchievementCatalogScanState.Failed,
                            result == null
                                ? "The single-target scan returned no structured result."
                                : slice.StopReason);
                    }

                    if (result.State == AchievementSingleScanState.PendingClaimRequiresUserAction)
                    {
                        pending++;
                        if (!Report(target.CanonicalTitle, out progressFailure))
                            return Finish(AchievementCatalogScanState.Failed, progressFailure);

                        AchievementPendingClaimDecision decision;
                        try
                        {
                            decision = pendingDecisionProvider.Decide(
                                new AchievementPendingClaimRequest(target, result, resumeCount),
                                cancellationToken);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            results.Add(result);
                            return Finish(AchievementCatalogScanState.Cancelled,
                                "Achievement scanning was cancelled during the unclaimed-reward pause.");
                        }
                        catch (Exception ex)
                        {
                            return Finish(
                                AchievementCatalogScanState.Failed,
                                $"The unclaimed-reward interaction failed: {ex.GetType().Name}: {ex.Message}");
                        }

                        if (decision == AchievementPendingClaimDecision.Resume)
                        {
                            try
                            {
                                catalogScanner.ReacquireAfterUserInteraction(cancellationToken);
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                results.Add(result);
                                return Finish(AchievementCatalogScanState.Cancelled,
                                    "Achievement scanning was cancelled while restoring the search pointer.");
                            }
                            catch (Exception ex)
                            {
                                results.Add(result);
                                return Finish(AchievementCatalogScanState.Failed,
                                    $"Achievement search pointer recovery failed: {ex.GetType().Name}: {ex.Message}");
                            }
                            pending--;
                            resumeCount++;
                            continue;
                        }
                        if (decision == AchievementPendingClaimDecision.CancelScan)
                        {
                            results.Add(result);
                            requestCancellation();
                            return Finish(AchievementCatalogScanState.Cancelled,
                                "The user cancelled during an unclaimed-reward pause.");
                        }

                        skipped.Add(new AchievementUserSkippedTarget(
                            target,
                            AchievementUserSkippedReason.PendingUnclaimedReward));
                        processed++;
                        if (!Report(target.CanonicalTitle, out progressFailure))
                            return Finish(AchievementCatalogScanState.Failed, progressFailure);
                        break;
                    }

                    results.Add(result);
                    processed++;
                    AchievementTrustedCompletionExtractor.MergeInto(trustedIds, new[] { result });
                    switch (result.State)
                    {
                        case AchievementSingleScanState.Completed:
                            completed++;
                            break;
                        case AchievementSingleScanState.Incomplete:
                            incomplete++;
                            break;
                        case AchievementSingleScanState.Unresolved:
                            unresolved++;
                            break;
                        case AchievementSingleScanState.AmbiguousCollision:
                        case AchievementSingleScanState.AmbiguousMultiMatch:
                            ambiguous++;
                            break;
                        case AchievementSingleScanState.Failed:
                            failed++;
                            break;
                    }

                    if (!Report(target.CanonicalTitle, out progressFailure))
                        return Finish(AchievementCatalogScanState.Failed, progressFailure);
                    break;
                }
            }

            return Finish(AchievementCatalogScanState.Completed,
                "All planned achievement targets were processed.");
        }

        internal AchievementInteractiveScanResult CancelledBeforeStart(
            AchievementResumeSeed resumeSeed,
            string reason)
        {
            int previousCount = resumeSeed?.TrustedCompletedIds.Count ?? 0;
            int plannedTargets = catalogScanner.Targets.Count;
            int skippedTrustedTargets = 0;
            if (resumeSeed != null && AchievementUpdatePlan.TryCreate(
                catalog,
                resumeSeed,
                out AchievementUpdatePlan plan,
                out _,
                out _))
            {
                plannedTargets = plan.ScanTargets.Count;
                skippedTrustedTargets = plan.SkippedTargets.Count;
            }
            return new AchievementInteractiveScanResult(
                AchievementCatalogScanState.Cancelled,
                catalogScanner.Targets.Count,
                plannedTargets,
                skippedTrustedTargets,
                0,
                0,
                previousCount,
                resumeSeed?.TrustedCompletedIds ?? Array.Empty<int>(),
                Array.Empty<AchievementSingleScanResult>(),
                Array.Empty<AchievementUserSkippedTarget>(),
                0, 0, 0, 0, 0, 0,
                reason);
        }

        private AchievementInteractiveScanResult EmptyResult(
            AchievementCatalogScanState state,
            int totalTargets,
            string reason) => new(
                state,
                totalTargets,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<int>(),
                Array.Empty<AchievementSingleScanResult>(),
                Array.Empty<AchievementUserSkippedTarget>(),
                0, 0, 0, 0, 0, 0,
                reason);
    }
}
