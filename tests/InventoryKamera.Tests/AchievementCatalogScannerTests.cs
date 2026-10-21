using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementScanTargetTests
    {
        [Fact]
        public void PackagedCatalogProducesOneStableTargetPerTitleGroup()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;

            IReadOnlyList<AchievementScanTarget> first = AchievementScanTarget.FromCatalog(catalog);
            IReadOnlyList<AchievementScanTarget> second = AchievementScanTarget.FromCatalog(catalog);

            Assert.Equal(1559, first.Count);
            Assert.True(first.Count < catalog.All.Count);
            Assert.Equal(catalog.TitleGroups.Select(group => group.CanonicalTitle),
                first.Select(target => target.CanonicalTitle));
            Assert.Equal(first.Select(TargetSignature), second.Select(TargetSignature));
            Assert.Equal(first.Count, first.Select(target => target.CanonicalTitle).Distinct().Count());
            Assert.Equal(first.Count, first.Select(target => target.RequestedCanonicalId).Distinct().Count());
        }

        [Fact]
        public void PackagedStageFamiliesAndCollisionEachProduceOneCompleteTarget()
        {
            IReadOnlyList<AchievementScanTarget> targets = AchievementScanTarget.FromCatalog(
                new AchievementCatalogProvider().Catalog);

            AchievementScanTarget zoo = Assert.Single(
                targets.Where(target => target.CanonicalTitle == "Zoo Tycoon"));
            Assert.Equal(AchievementTitleGroupKind.StageFamily, zoo.TitleGroupKind);
            Assert.Equal(80127, zoo.RequestedCanonicalId);
            Assert.Equal(80127, zoo.StageGroupId);
            Assert.Equal(new[] { 80127, 80128, 80129 }, zoo.CandidateIds);

            AchievementScanTarget moonmist = Assert.Single(
                targets.Where(target => target.CanonicalTitle == "Moonmist Treasure Hunter"));
            Assert.Equal(AchievementTitleGroupKind.StageFamily, moonmist.TitleGroupKind);
            Assert.Equal(80490, moonmist.RequestedCanonicalId);
            Assert.Equal(new[] { 80490, 80491, 80492 }, moonmist.CandidateIds);

            AchievementScanTarget collision = Assert.Single(
                targets.Where(target => target.CanonicalTitle == "Let the Wind Lead"));
            Assert.Equal(AchievementTitleGroupKind.UnrelatedCollision, collision.TitleGroupKind);
            Assert.Equal(84004, collision.RequestedCanonicalId);
            Assert.Null(collision.StageGroupId);
            Assert.Equal(new[] { 80032, 84004 }, collision.CandidateIds);
        }

        [Fact]
        public void RepresentativesUseStageRootAndCollisionSourceOrderWithoutChangingCandidates()
        {
            AchievementCatalog catalog = new(new[]
            {
                Entry(2, "Stage", stageGroupId: 1, stageIndex: 2, stageCount: 2),
                Entry(1, "Stage", stageGroupId: 1, stageIndex: 1, stageCount: 2),
                Entry(20, "Collision"),
                Entry(10, "Collision"),
            });

            IReadOnlyList<AchievementScanTarget> targets = AchievementScanTarget.FromCatalog(catalog);

            Assert.Equal(new[] { "Stage", "Collision" }, targets.Select(target => target.CanonicalTitle));
            Assert.Equal(1, targets[0].RequestedCanonicalId);
            Assert.Equal(new[] { 1, 2 }, targets[0].CandidateIds);
            Assert.Equal(20, targets[1].RequestedCanonicalId);
            Assert.Equal(new[] { 10, 20 }, targets[1].CandidateIds);
        }

        [Fact]
        public void TargetCandidateCollectionIsReadOnly()
        {
            AchievementScanTarget target = AchievementScanTarget.FromCatalog(SmallCatalog())[0];
            var candidates = Assert.IsAssignableFrom<IList<int>>(target.CandidateIds);

            Assert.Throws<NotSupportedException>(() => candidates.RemoveAt(0));
        }

        private static string TargetSignature(AchievementScanTarget target) =>
            $"{target.CanonicalTitle}|{target.TitleGroupKind}|{target.RequestedCanonicalId}|" +
            $"{target.StageGroupId}|{string.Join(",", target.CandidateIds)}";

        internal static AchievementCatalog SmallCatalog() => new(new[]
        {
            Entry(1, "Alpha"),
            Entry(2, "Beta"),
            Entry(3, "Gamma"),
        });

        internal static AchievementCatalogEntry Entry(
            int id,
            string name,
            int? stageGroupId = null,
            int? stageIndex = null,
            int? stageCount = null) => new(
                id,
                name,
                "description",
                categoryId: 1,
                hidden: false,
                version: "1.0",
                order: id,
                stageGroupId,
                stageIndex,
                stageCount);
    }

    public sealed class AchievementCatalogScannerTests
    {
        [Fact]
        public void AllRecoverableOutcomesCompleteAndAggregateWithoutFlatteningResults()
        {
            AchievementCatalog catalog = OutcomeCatalog();
            var expected = new Dictionary<int, AchievementSingleScanResult>
            {
                [1] = Result(catalog, 1, AchievementSingleScanState.Completed),
                [2] = Result(catalog, 2, AchievementSingleScanState.Incomplete),
                [3] = Result(catalog, 3, AchievementSingleScanState.Unresolved),
                [5] = Result(catalog, 5, AchievementSingleScanState.AmbiguousCollision),
                [6] = Result(catalog, 6, AchievementSingleScanState.AmbiguousMultiMatch),
                [7] = Result(catalog, 7, AchievementSingleScanState.PendingClaimRequiresUserAction),
            };
            var single = new RecordingSingleScanner((id, _) => expected[id]);
            var progress = new RecordingProgress();
            var scanner = Scanner(catalog, single);

            AchievementCatalogScanResult result = scanner.Scan(CancellationToken.None, progress);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(6, result.TotalTargets);
            Assert.Equal(6, result.ProcessedTargets);
            Assert.Equal(1, result.CompletedCount);
            Assert.Equal(1, result.IncompleteCount);
            Assert.Equal(1, result.UnresolvedCount);
            Assert.Equal(2, result.AmbiguousCount);
            Assert.Equal(1, result.PendingClaimCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(0, result.CancelledCount);
            Assert.False(result.StoppedEarly);
            Assert.Equal(new[] { 1, 2, 3, 5, 6, 7 }, single.RequestedIds);
            Assert.Equal(expected.Values, result.Results);
            Assert.Equal(7, progress.Values.Count);
            Assert.Equal(Enumerable.Range(0, 7), progress.Values.Select(value => value.CurrentIndex));
            Assert.Equal(
                new[] { string.Empty, "Completed", "Incomplete", "Unresolved", "Collision", "Multi", "Stage" },
                progress.Values.Select(value => value.CurrentTitle));
            Assert.Equal(
                new int?[] { null, 1, 2, 3, 5, 6, 7 },
                progress.Values.Select(value => value.CurrentRequestedId));
            Assert.Equal(new[] { 0, 1, 1, 1, 1, 1, 1 },
                progress.Values.Select(value => value.CompletedCount));
            Assert.Equal(new[] { 0, 0, 1, 1, 1, 1, 1 },
                progress.Values.Select(value => value.IncompleteCount));
            Assert.Equal(new[] { 0, 0, 0, 1, 1, 1, 1 },
                progress.Values.Select(value => value.UnresolvedCount));
            Assert.Equal(new[] { 0, 0, 0, 0, 1, 2, 2 },
                progress.Values.Select(value => value.AmbiguousCount));
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 1 },
                progress.Values.Select(value => value.PendingClaimCount));
            Assert.Equal(6, progress.Values[^1].TotalTargets);
            Assert.Equal("Stage", progress.Values[^1].CurrentTitle);
            Assert.Equal(7, progress.Values[^1].CurrentRequestedId);
            Assert.Equal(1, progress.Values[^1].PendingClaimCount);
        }

        [Fact]
        public void HiddenZeroResultIncompleteCountsAndContinuesAfterSafeClear()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                id == 1
                    ? AchievementSingleScanState.Incomplete
                    : AchievementSingleScanState.Completed,
                ready: true,
                reason: id == 1
                    ? "hidden exact-title no results"
                    : "completed"));

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(3, result.ProcessedTargets);
            Assert.Equal(1, result.IncompleteCount);
            Assert.Equal(2, result.CompletedCount);
            Assert.Equal(0, result.UnresolvedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(new[] { 1, 2, 3 }, single.RequestedIds);
        }

        [Fact]
        public void UnsafeResetStopsBeforeNextTargetAndPreservesEvidence()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            AchievementSingleScanResult unsafeResult = Result(
                catalog,
                2,
                AchievementSingleScanState.Incomplete,
                ready: false,
                reason: "idle not verified");
            var single = new RecordingSingleScanner((id, _) => id == 1
                ? Result(catalog, id, AchievementSingleScanState.Completed)
                : unsafeResult);
            var progress = new RecordingProgress();

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None, progress);

            Assert.Equal(AchievementCatalogScanState.StoppedUnsafe, result.State);
            Assert.Equal(2, result.ProcessedTargets);
            Assert.True(result.StoppedEarly);
            Assert.Same(unsafeResult, result.Results[1]);
            Assert.Equal(new[] { 1, 2 }, single.RequestedIds);
            Assert.Equal(new[] { 0, 1, 2 }, progress.Values.Select(value => value.CurrentIndex));
            Assert.DoesNotContain(progress.Values, value => value.CurrentIndex == 3);
        }

        [Theory]
        [InlineData((int)AchievementSingleScanState.Completed)]
        [InlineData((int)AchievementSingleScanState.Unresolved)]
        [InlineData((int)AchievementSingleScanState.AmbiguousCollision)]
        [InlineData((int)AchievementSingleScanState.AmbiguousMultiMatch)]
        [InlineData((int)AchievementSingleScanState.PendingClaimRequiresUserAction)]
        public void ReadyForNextFalseIsHardGateForEveryRecoverableOutcome(int stateValue)
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                (AchievementSingleScanState)stateValue,
                ready: false));

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.StoppedUnsafe, result.State);
            Assert.Equal(1, result.ProcessedTargets);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
        }

        [Fact]
        public void OperationalFailureStopsEvenWhenResetWasSafe()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => id == 1
                ? Result(catalog, id, AchievementSingleScanState.Completed)
                : Result(catalog, id, AchievementSingleScanState.Failed, reason: "capture failed"));

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.Equal(2, result.ProcessedTargets);
            Assert.Equal(1, result.FailedCount);
            Assert.True(result.StoppedEarly);
            Assert.Equal(new[] { 1, 2 }, single.RequestedIds);
            Assert.Contains("operational failure", result.StopReason);
        }

        [Fact]
        public void UnsafeResetTakesPrecedenceOverOperationalFailure()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                AchievementSingleScanState.Failed,
                ready: false,
                reason: "capture and reset failed"));

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.StoppedUnsafe, result.State);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
        }

        [Fact]
        public void CancelledBeforeFirstTargetStartsNothing()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));
            var progress = new RecordingProgress();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(cancellation.Token, progress);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(0, result.ProcessedTargets);
            Assert.Empty(single.RequestedIds);
            Assert.Empty(progress.Values);
        }

        [Fact]
        public void CancellationAfterProcessedTargetStopsBeforeNextTarget()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            using var cancellation = new CancellationTokenSource();
            var single = new RecordingSingleScanner((id, _) =>
            {
                AchievementSingleScanResult result = Result(
                    catalog,
                    id,
                    AchievementSingleScanState.Completed);
                cancellation.Cancel();
                return result;
            });

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(cancellation.Token);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(1, result.ProcessedTargets);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
            Assert.Same(single.ReturnedResults[0], result.Results[0]);
        }

        [Fact]
        public void DelegatedCancelledResultIsRetainedAndWinsOverUnsafeState()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            AchievementSingleScanResult cancelled = Result(
                catalog,
                1,
                AchievementSingleScanState.Cancelled,
                ready: false,
                reason: "single scan cancelled");
            var single = new RecordingSingleScanner((_, _) => cancelled);

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(1, result.CancelledCount);
            Assert.Same(cancelled, Assert.Single(result.Results));
            Assert.Equal(new[] { 1 }, single.RequestedIds);
        }

        [Fact]
        public void DelegatedCancellationExceptionPreservesPriorResults()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            using var cancellation = new CancellationTokenSource();
            var single = new RecordingSingleScanner((id, _) =>
            {
                if (id == 1) return Result(catalog, id, AchievementSingleScanState.Completed);
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(cancellation.Token);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(1, result.ProcessedTargets);
            Assert.Equal(new[] { 1, 2 }, single.RequestedIds);
        }

        [Fact]
        public void ProgressCallbackFailureStopsBeforeNextTarget()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));
            var progress = new RecordingProgress(throwAtIndex: 1);

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None, progress);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.Equal(1, result.ProcessedTargets);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
            Assert.Contains("progress reporting failed", result.StopReason);
        }

        [Fact]
        public void InitialProgressCallbackFailureStartsNoTarget()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));
            var progress = new RecordingProgress(throwAtIndex: 0);

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None, progress);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.Equal(0, result.ProcessedTargets);
            Assert.Empty(single.RequestedIds);
        }

        [Fact]
        public void SingleScannerExceptionFailsWithoutOuterRetry()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((_, _) =>
                throw new InvalidOperationException("scanner boom"));

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.Equal(0, result.ProcessedTargets);
            Assert.Equal(new[] { 1 }, single.RequestedIds);
            Assert.Contains("scanner boom", result.StopReason);
        }

        [Fact]
        public void ResultsAreReadOnlyAndFullResultStoresNoBitmap()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));
            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);
            var results = Assert.IsAssignableFrom<IList<AchievementSingleScanResult>>(result.Results);

            Assert.Throws<NotSupportedException>(() => results.RemoveAt(0));
            Assert.DoesNotContain(
                typeof(AchievementCatalogScanResult).GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                property => typeof(Bitmap).IsAssignableFrom(property.PropertyType));
            Assert.DoesNotContain(
                typeof(AchievementCatalogScanResult).GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                field => typeof(Bitmap).IsAssignableFrom(field.FieldType));
        }

        [Fact]
        public void DiagnosticsAreBoundedSummaryOnly()
        {
            AchievementCatalog catalog = AchievementScanTargetTests.SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                AchievementSingleScanState.Incomplete,
                ready: false,
                reason: new string('X', 5000),
                diagnostics: new string('Y', 5000)));

            AchievementCatalogScanResult result = Scanner(catalog, single)
                .Scan(CancellationToken.None);

            Assert.Contains("operation=achievement-catalog-scan", result.Diagnostics);
            Assert.Contains("totalTargets=3", result.Diagnostics);
            Assert.Contains("processedTargets=1", result.Diagnostics);
            Assert.Contains("completed=0", result.Diagnostics);
            Assert.Contains("incomplete=1", result.Diagnostics);
            Assert.Contains("unresolved=0", result.Diagnostics);
            Assert.Contains("ambiguous=0", result.Diagnostics);
            Assert.Contains("pendingClaim=0", result.Diagnostics);
            Assert.Contains("failed=0", result.Diagnostics);
            Assert.Contains("cancelled=0", result.Diagnostics);
            Assert.Contains("stoppedEarly=True", result.Diagnostics);
            Assert.Contains("stopReason=", result.Diagnostics);
            Assert.DoesNotContain(new string('Y', 100), result.Diagnostics);
            Assert.True(result.Diagnostics.Length < 1024, result.Diagnostics.Length.ToString());
            Assert.True(result.StopReason.Length <= 243, result.StopReason.Length.ToString());
        }

        private static AchievementCatalog OutcomeCatalog() => new(new[]
        {
            AchievementScanTargetTests.Entry(1, "Completed"),
            AchievementScanTargetTests.Entry(2, "Incomplete"),
            AchievementScanTargetTests.Entry(3, "Unresolved"),
            AchievementScanTargetTests.Entry(5, "Collision"),
            AchievementScanTargetTests.Entry(4, "Collision"),
            AchievementScanTargetTests.Entry(6, "Multi"),
            AchievementScanTargetTests.Entry(7, "Stage", 7, 1, 2),
            AchievementScanTargetTests.Entry(8, "Stage", 7, 2, 2),
        });

        private static AchievementCatalogScanner Scanner(
            AchievementCatalog catalog,
            IAchievementSingleScanner singleScanner) =>
            new(new TestCatalogProvider(catalog), singleScanner);

        private static AchievementSingleScanResult Result(
            AchievementCatalog catalog,
            int requestedId,
            AchievementSingleScanState state,
            bool ready = true,
            string reason = "test result",
            string diagnostics = "single diagnostics")
        {
            AchievementCatalogEntry entry = catalog.GetById(requestedId);
            return new AchievementSingleScanResult(
                state,
                requestedId,
                entry.Name,
                catalog.GetTitleGroupForId(requestedId),
                startDetection: null,
                searchResult: null,
                identityResult: null,
                completionResult: null,
                exitResult: null,
                exitAttempted: true,
                exitSucceeded: ready,
                finalScreenState: ready ? AchievementScreenState.Idle : AchievementScreenState.Unknown,
                pendingClaim: state == AchievementSingleScanState.PendingClaimRequiresUserAction,
                reason,
                diagnostics);
        }

        private sealed class TestCatalogProvider : IAchievementCatalogProvider
        {
            internal TestCatalogProvider(AchievementCatalog catalog) => Catalog = catalog;
            public AchievementCatalog Catalog { get; }
        }

        private sealed class RecordingSingleScanner : IAchievementSingleScanner
        {
            private readonly Func<int, CancellationToken, AchievementSingleScanResult> scan;

            internal RecordingSingleScanner(
                Func<int, CancellationToken, AchievementSingleScanResult> scan) =>
                this.scan = scan;

            internal List<int> RequestedIds { get; } = new();
            internal List<AchievementSingleScanResult> ReturnedResults { get; } = new();

            public AchievementSingleScanResult Scan(
                int canonicalAchievementId,
                CancellationToken cancellationToken)
            {
                RequestedIds.Add(canonicalAchievementId);
                AchievementSingleScanResult result = scan(canonicalAchievementId, cancellationToken);
                ReturnedResults.Add(result);
                return result;
            }
        }

        private sealed class RecordingProgress : IAchievementCatalogScanProgressReporter
        {
            private readonly int? throwAtIndex;

            internal RecordingProgress(int? throwAtIndex = null) =>
                this.throwAtIndex = throwAtIndex;

            internal List<AchievementCatalogScanProgress> Values { get; } = new();

            public void Report(AchievementCatalogScanProgress value)
            {
                Values.Add(value);
                if (value.CurrentIndex == throwAtIndex)
                    throw new InvalidOperationException("progress boom");
            }
        }
    }
}
