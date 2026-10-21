using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InventoryKamera.game;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementInteractiveScanCoordinatorTests
    {
        [Fact]
        public void TimingSummary_UsesOnlyCompletedTargetsAndCalculatesMeanMedianAndP95()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta", "Gamma", "Not Ready");
            AchievementTimingSummary summary = AchievementTimingSummary.Calculate(new[]
            {
                Result(catalog, 1, AchievementSingleScanState.Completed, totalMilliseconds: 100),
                Result(catalog, 2, AchievementSingleScanState.Incomplete, totalMilliseconds: 200),
                Result(catalog, 3, AchievementSingleScanState.Completed, totalMilliseconds: 1_000),
                Result(catalog, 4, AchievementSingleScanState.Completed, ready: false, totalMilliseconds: 9_000),
            });

            Assert.Equal(3, summary.CompletedTargetCount);
            Assert.Equal(433.3, summary.MeanTotalMilliseconds, precision: 1);
            Assert.Equal(200, summary.MedianTotalMilliseconds);
            Assert.Equal(1_000, summary.P95TotalMilliseconds);
            Assert.Equal(3, summary.FastPath.Count);
            Assert.Equal(0, summary.Recovered.Count);
        }

        [Fact]
        public void TimingSummary_SeparatesFastPathAndRecoveredTargets()
        {
            AchievementCatalog catalog = Catalog("Fast A", "Recovered", "Fast B");
            AchievementTimingSummary summary = AchievementTimingSummary.Calculate(new[]
            {
                Result(catalog, 1, AchievementSingleScanState.Completed, totalMilliseconds: 100),
                Result(catalog, 2, AchievementSingleScanState.Completed, totalMilliseconds: 400,
                    recognitionRecoveryUsed: true),
                Result(catalog, 3, AchievementSingleScanState.Completed, totalMilliseconds: 200),
            });

            Assert.Equal(2, summary.FastPath.Count);
            Assert.Equal(150, summary.FastPath.Mean);
            Assert.Equal(1, summary.Recovered.Count);
            Assert.Equal(400, summary.Recovered.Mean);
            Assert.Contains("fastPathCount=2", summary.Diagnostics);
            Assert.Contains("recoveredCount=1", summary.Diagnostics);
        }

        [Fact]
        public void PendingClaimResume_RescansSameTargetBeforeNextTarget()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.PendingClaimRequiresUserAction),
                Result(catalog, 1, AchievementSingleScanState.Completed),
                Result(catalog, 2, AchievementSingleScanState.Incomplete));
            var decisions = new SequenceDecisions(AchievementPendingClaimDecision.Resume);
            var coordinator = Coordinator(catalog, single, decisions, () => { });

            AchievementInteractiveScanResult result = coordinator.Scan(
                resumeSeed: null,
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(new[] { 1, 1, 2 }, single.RequestedIds);
            Assert.Single(decisions.Requests);
            Assert.Equal(0, decisions.Requests[0].PriorResumeCount);
            Assert.Equal(2, result.ProcessedScanTargets);
            Assert.Equal(3, result.ScanAttemptCount);
            Assert.Equal(new[] { 1 }, result.TrustedCompletedIds);
            Assert.Equal(1, single.PrepareCount);
            Assert.Equal(1, single.ReacquireCount);
        }

        [Fact]
        public void PendingClaimTwice_TwoExplicitResumesRetrySameTargetTwice()
        {
            AchievementCatalog catalog = Catalog("Alpha");
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.PendingClaimRequiresUserAction),
                Result(catalog, 1, AchievementSingleScanState.PendingClaimRequiresUserAction),
                Result(catalog, 1, AchievementSingleScanState.Completed));
            var decisions = new SequenceDecisions(
                AchievementPendingClaimDecision.Resume,
                AchievementPendingClaimDecision.Resume);

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                decisions,
                () => { }).Scan(null, CancellationToken.None);

            Assert.Equal(new[] { 1, 1, 1 }, single.RequestedIds);
            Assert.Equal(new[] { 0, 1 }, decisions.Requests.Select(request => request.PriorResumeCount));
            Assert.Equal(3, result.ScanAttemptCount);
            Assert.Equal(1, result.ProcessedScanTargets);
        }

        [Fact]
        public void PendingClaimSkip_RecordsNoEvidenceAndContinuesOnlyAfterSafeReset()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.PendingClaimRequiresUserAction),
                Result(catalog, 2, AchievementSingleScanState.Completed));
            var decisions = new SequenceDecisions(AchievementPendingClaimDecision.SkipAchievement);

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                decisions,
                () => { }).Scan(null, CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(new[] { 1, 2 }, single.RequestedIds);
            AchievementUserSkippedTarget skipped = Assert.Single(result.UserSkippedTargets);
            Assert.Equal("Alpha", skipped.CanonicalTitle);
            Assert.Equal(AchievementUserSkippedReason.PendingUnclaimedReward, skipped.Reason);
            Assert.Equal(new[] { 2 }, result.TrustedCompletedIds);
            Assert.DoesNotContain(result.Results, item => item.RequestedId == 1);
        }

        [Fact]
        public void PendingClaimCancel_RequestsExistingCancellationAndNeverStartsNextTarget()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.PendingClaimRequiresUserAction));
            var decisions = new SequenceDecisions(AchievementPendingClaimDecision.CancelScan);
            int cancellationRequests = 0;

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                decisions,
                () => cancellationRequests++).Scan(null, CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
            Assert.Equal(1, cancellationRequests);
            Assert.Empty(result.TrustedCompletedIds);
        }

        [Fact]
        public void CancellationDuringInteractivePause_PropagatesWithoutStartingNextTarget()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.PendingClaimRequiresUserAction));
            using var cancellation = new CancellationTokenSource();
            var decisions = new CancellingDecisionProvider(cancellation);

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                decisions,
                cancellation.Cancel).Scan(null, cancellation.Token);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
            Assert.Single(decisions.Requests);
        }

        [Fact]
        public void CancellationBetweenTargets_DoesNotStartNextTarget()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            using var cancellation = new CancellationTokenSource();
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.Completed),
                Result(catalog, 2, AchievementSingleScanState.Completed))
            {
                AfterScan = _ => cancellation.Cancel(),
            };

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                new SequenceDecisions(),
                cancellation.Cancel).Scan(null, cancellation.Token);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
        }

        [Fact]
        public void UnsafePendingClaim_NeverPromptsOrStartsNextTarget()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var single = new SequenceSingleScanner(
                Result(
                    catalog,
                    1,
                    AchievementSingleScanState.PendingClaimRequiresUserAction,
                    ready: false));
            var decisions = new SequenceDecisions(AchievementPendingClaimDecision.SkipAchievement);

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                decisions,
                () => { }).Scan(null, CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.StoppedUnsafe, result.State);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
            Assert.Empty(decisions.Requests);
            Assert.Empty(result.UserSkippedTargets);
        }

        [Fact]
        public void OperationalFailure_StopsBeforeNextTarget()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.Failed, ready: true));

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                new SequenceDecisions(),
                () => { }).Scan(null, CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
        }

        [Fact]
        public void UpdateSeed_SkipsFullyTrustedTargetAndPreservesTrustedIds()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var single = new SequenceSingleScanner(
                Result(catalog, 2, AchievementSingleScanState.Completed));
            var progress = new RecordingInteractiveProgress();

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                new SequenceDecisions(),
                () => { }).Scan(new AchievementResumeSeed(new[] { 1 }), CancellationToken.None, progress);

            Assert.Equal(new[] { 2 }, single.RequestedIds);
            Assert.Equal(1, result.SkippedTrustedTargets);
            Assert.Equal(new[] { 1, 2 }, result.TrustedCompletedIds);
            Assert.All(progress.Values, value => Assert.Equal(1, value.SkippedTrustedTargets));
        }

        [Fact]
        public void FreshAndPartialResults_ExportOnlyCoordinatorTrustedIds()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var provider = new TestCatalogProvider(catalog);
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.Completed),
                Result(catalog, 2, AchievementSingleScanState.PendingClaimRequiresUserAction));
            AchievementInteractiveScanResult partial = new AchievementInteractiveScanCoordinator(
                provider,
                new AchievementCatalogScanner(provider, single),
                new SequenceDecisions(AchievementPendingClaimDecision.CancelScan),
                () => { }).Scan(null, CancellationToken.None);

            AchievementToolkitExportResult export =
                new AchievementToolkitExporter(provider).ExportGood(partial.TrustedCompletedIds);

            Assert.Equal(AchievementCatalogScanState.Cancelled, partial.State);
            Assert.Equal(new[] { 1 }, partial.TrustedCompletedIds);
            Assert.True(export.Success);
            Assert.Equal("{\n  \"achievements\": [\n    1\n  ]\n}", export.Json);
        }

        [Fact]
        public void UpdateResultExport_PreservesPreviousAndAddsOnlyNewTrustedIds()
        {
            AchievementCatalog catalog = Catalog("Alpha", "Beta");
            var provider = new TestCatalogProvider(catalog);
            var single = new SequenceSingleScanner(
                Result(catalog, 2, AchievementSingleScanState.Completed));
            AchievementInteractiveScanResult update = new AchievementInteractiveScanCoordinator(
                provider,
                new AchievementCatalogScanner(provider, single),
                new SequenceDecisions(),
                () => { }).Scan(new AchievementResumeSeed(new[] { 1 }), CancellationToken.None);

            AchievementToolkitExportResult export =
                new AchievementToolkitExporter(provider).ExportGood(update.TrustedCompletedIds);

            Assert.Equal(new[] { 1, 2 }, update.TrustedCompletedIds);
            Assert.Equal(new[] { 1, 2 }, export.CanonicalIds);
        }

        [Fact]
        public void CancelledTokenBeforeStart_UsesSameTokenAndIssuesNoInputWork()
        {
            AchievementCatalog catalog = Catalog("Alpha");
            var single = new SequenceSingleScanner(
                Result(catalog, 1, AchievementSingleScanState.Completed));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            AchievementInteractiveScanResult result = Coordinator(
                catalog,
                single,
                new SequenceDecisions(),
                () => { }).Scan(null, cancellation.Token);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Empty(single.RequestedIds);
        }

        [Fact]
        public void Report_PreservesSkippedAndUnresolvedUserFacingReasons()
        {
            AchievementCatalog catalog = new(new[]
            {
                Entry(1, "Pending"),
                Entry(2, "Collision"),
                Entry(3, "Collision"),
                Entry(4, "Multi"),
                Entry(5, "Stage", 5, 1, 2),
                Entry(6, "Stage", 5, 2, 2),
                Entry(7, "OCR"),
            });
            AchievementScanTarget pendingTarget = AchievementScanTarget.FromCatalog(catalog)
                .Single(target => target.CanonicalTitle == "Pending");
            var stageProgress = new AchievementStageProgressResult(
                AchievementStageProgressState.AmbiguousReward,
                5,
                null,
                null,
                Array.Empty<int>(),
                null,
                "duplicate reward",
                "diagnostics");
            var result = new AchievementInteractiveScanResult(
                AchievementCatalogScanState.Completed,
                5,
                5,
                0,
                5,
                5,
                0,
                Array.Empty<int>(),
                new[]
                {
                    Result(catalog, 2, AchievementSingleScanState.AmbiguousCollision),
                    Result(catalog, 4, AchievementSingleScanState.AmbiguousMultiMatch),
                    Result(catalog, 5, AchievementSingleScanState.Unresolved, stageProgressResult: stageProgress),
                    Result(catalog, 7, AchievementSingleScanState.Unresolved),
                },
                new[] { new AchievementUserSkippedTarget(pendingTarget, AchievementUserSkippedReason.PendingUnclaimedReward) },
                0, 0, 2, 2, 1, 0,
                "complete");

            string report = AchievementScanReportBuilder.BuildIssueReport(result);

            Assert.Contains("Pending - skipped: unclaimed reward", report);
            Assert.Contains("Collision - unresolved: title collision", report);
            Assert.Contains("Multi - unresolved: multiple matches", report);
            Assert.Contains("Stage - unresolved: stage reward ambiguity", report);
            Assert.Contains("OCR - unresolved: could not identify safely", report);
            Assert.DoesNotContain("diagnostics", report, StringComparison.OrdinalIgnoreCase);
        }

        private static AchievementInteractiveScanCoordinator Coordinator(
            AchievementCatalog catalog,
            IAchievementSingleScanner scanner,
            IAchievementPendingClaimDecisionProvider decisions,
            Action cancel)
        {
            var provider = new TestCatalogProvider(catalog);
            return new AchievementInteractiveScanCoordinator(
                provider,
                new AchievementCatalogScanner(provider, scanner),
                decisions,
                cancel);
        }

        private static AchievementCatalog Catalog(params string[] names) => new(
            names.Select((name, index) => Entry(index + 1, name)));

        private static AchievementCatalogEntry Entry(
            int id,
            string name,
            int? stageGroupId = null,
            int? stageIndex = null,
            int? stageCount = null) => new(
                id,
                name,
                "description",
                1,
                false,
                "1.0",
                id,
                stageGroupId,
                stageIndex,
                stageCount,
                primogems: stageIndex.GetValueOrDefault() * 5);

        private static AchievementSingleScanResult Result(
            AchievementCatalog catalog,
            int requestedId,
            AchievementSingleScanState state,
            bool ready = true,
            AchievementStageProgressResult stageProgressResult = null,
            long? totalMilliseconds = null,
            bool recognitionRecoveryUsed = false)
        {
            AchievementCatalogEntry entry = catalog.GetById(requestedId);
            AchievementSearchNavigationResult searchResult = null;
            if (totalMilliseconds.HasValue)
            {
                var timing = new AchievementTargetTiming { TotalMilliseconds = totalMilliseconds.Value };
                timing.IdentityRecoveryUsed = recognitionRecoveryUsed;
                searchResult = new AchievementSearchNavigationResult(
                    "Search", true, false, string.Empty,
                    null, null, null, null, null, 1, string.Empty,
                    timing: timing);
            }
            return new AchievementSingleScanResult(
                state,
                requestedId,
                entry.Name,
                catalog.GetTitleGroupForId(requestedId),
                null,
                searchResult,
                null,
                null,
                null,
                exitAttempted: true,
                exitSucceeded: ready,
                finalScreenState: ready ? AchievementScreenState.Idle : AchievementScreenState.Unknown,
                pendingClaim: state == AchievementSingleScanState.PendingClaimRequiresUserAction,
                "test reason",
                "test diagnostics",
                stageProgressResult);
        }

        private sealed class TestCatalogProvider : IAchievementCatalogProvider
        {
            internal TestCatalogProvider(AchievementCatalog catalog) => Catalog = catalog;
            public AchievementCatalog Catalog { get; }
        }

        private sealed class SequenceSingleScanner : IAchievementSingleScanner
        {
            private readonly Queue<AchievementSingleScanResult> results;
            internal SequenceSingleScanner(params AchievementSingleScanResult[] results) =>
                this.results = new Queue<AchievementSingleScanResult>(results);
            internal List<int> RequestedIds { get; } = new();
            internal int PrepareCount { get; private set; }
            internal int ReacquireCount { get; private set; }
            internal Action<int> AfterScan { get; set; }

            public void PrepareForScan(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrepareCount++;
            }

            public void ReacquireAfterUserInteraction(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReacquireCount++;
            }

            public AchievementSingleScanResult Scan(int canonicalAchievementId, CancellationToken cancellationToken)
            {
                RequestedIds.Add(canonicalAchievementId);
                AchievementSingleScanResult result = results.Dequeue();
                AfterScan?.Invoke(canonicalAchievementId);
                return result;
            }
        }

        private sealed class SequenceDecisions : IAchievementPendingClaimDecisionProvider
        {
            private readonly Queue<AchievementPendingClaimDecision> decisions;
            internal SequenceDecisions(params AchievementPendingClaimDecision[] decisions) =>
                this.decisions = new Queue<AchievementPendingClaimDecision>(decisions);
            internal List<AchievementPendingClaimRequest> Requests { get; } = new();

            public AchievementPendingClaimDecision Decide(
                AchievementPendingClaimRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return decisions.Dequeue();
            }
        }

        private sealed class RecordingInteractiveProgress : IAchievementInteractiveScanProgressReporter
        {
            internal List<AchievementInteractiveScanProgress> Values { get; } = new();
            public void Report(AchievementInteractiveScanProgress progress) => Values.Add(progress);
        }

        private sealed class CancellingDecisionProvider : IAchievementPendingClaimDecisionProvider
        {
            private readonly CancellationTokenSource cancellation;
            internal CancellingDecisionProvider(CancellationTokenSource cancellation) =>
                this.cancellation = cancellation;
            internal List<AchievementPendingClaimRequest> Requests { get; } = new();

            public AchievementPendingClaimDecision Decide(
                AchievementPendingClaimRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                cancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }
}
