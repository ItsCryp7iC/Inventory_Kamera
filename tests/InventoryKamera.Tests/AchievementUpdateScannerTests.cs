using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementResumeSeedTests
    {
        [Fact]
        public void SeedDeduplicatesAndOrdersIdsNumerically()
        {
            var seed = new AchievementResumeSeed(new[] { 30, 10, 20, 10, 30 });

            Assert.Equal(new[] { 10, 20, 30 }, seed.TrustedCompletedIds);
            var ids = Assert.IsAssignableFrom<IList<int>>(seed.TrustedCompletedIds);
            Assert.Throws<NotSupportedException>(() => ids.RemoveAt(0));
        }
    }

    public sealed class AchievementUpdatePlanTests
    {
        [Fact]
        public void PartialTrustPartitionsByWholeTitleGroupInSourceOrder()
        {
            AchievementCatalog catalog = PartitionCatalog();
            var seed = new AchievementResumeSeed(new[] { 41, 1, 22, 10, 20, 30, 40, 21 });

            bool succeeded = AchievementUpdatePlan.TryCreate(
                catalog,
                seed,
                out AchievementUpdatePlan plan,
                out IReadOnlyList<int> invalidIds,
                out string failure);

            Assert.True(succeeded, failure);
            Assert.Empty(invalidIds);
            Assert.Equal(new[] { 1, 10, 20, 21, 22, 30, 40, 41 }, plan.TrustedCompletedIds);
            Assert.Equal(
                new[] { "Unique Trusted", "Stage Full", "Collision Full" },
                plan.SkippedTargets.Select(target => target.CanonicalTitle));
            Assert.Equal(
                new[] { 1, 20, 41 },
                plan.SkippedTargets.Select(target => target.RequestedCanonicalId));
            Assert.All(
                plan.SkippedTargets,
                target => Assert.Equal(
                    AchievementSkippedTargetReason.PreviouslyTrustedComplete,
                    target.Reason));
            Assert.Equal(
                new[] { "Unique Scan", "Stage Partial", "Collision Partial" },
                plan.ScanTargets.Select(target => target.CanonicalTitle));
            Assert.Equal(new[] { 2, 10, 31 },
                plan.ScanTargets.Select(target => target.RequestedCanonicalId));
        }

        [Fact]
        public void InputOrderAndDuplicatesDoNotChangeUpdatePlan()
        {
            AchievementCatalog catalog = PartitionCatalog();
            int[] firstInput = { 1, 10, 20, 21, 22, 30, 40, 41 };
            int[] secondInput = { 41, 22, 1, 30, 10, 21, 40, 20, 1, 41 };

            AchievementUpdatePlan first = Plan(catalog, firstInput);
            AchievementUpdatePlan second = Plan(catalog, secondInput);

            Assert.Equal(first.TrustedCompletedIds, second.TrustedCompletedIds);
            Assert.Equal(
                first.SkippedTargets.Select(SkippedSignature),
                second.SkippedTargets.Select(SkippedSignature));
            Assert.Equal(
                first.ScanTargets.Select(TargetSignature),
                second.ScanTargets.Select(TargetSignature));
        }

        [Fact]
        public void PlanCollectionsAreReadOnly()
        {
            AchievementUpdatePlan plan = Plan(PartitionCatalog(), new[] { 1 });
            var trusted = Assert.IsAssignableFrom<IList<int>>(plan.TrustedCompletedIds);
            var skipped = Assert.IsAssignableFrom<IList<AchievementSkippedTarget>>(plan.SkippedTargets);
            var scan = Assert.IsAssignableFrom<IList<AchievementScanTarget>>(plan.ScanTargets);
            var candidates = Assert.IsAssignableFrom<IList<int>>(plan.SkippedTargets[0].CandidateIds);

            Assert.Throws<NotSupportedException>(() => trusted.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => skipped.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => scan.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => candidates.RemoveAt(0));
        }

        [Fact]
        public void PackagedCatalogEmptySeedPlansEveryTarget()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            AchievementUpdatePlan plan = Plan(catalog, Array.Empty<int>());

            Assert.Equal(1559, catalog.TitleGroups.Count);
            Assert.Empty(plan.SkippedTargets);
            Assert.Equal(1559, plan.ScanTargets.Count);
            Assert.Equal(
                AchievementScanTarget.FromCatalog(catalog).Select(TargetSignature),
                plan.ScanTargets.Select(TargetSignature));
        }

        [Fact]
        public void PackagedUniqueTrustSkipsExactlyOneTarget()
        {
            AchievementUpdatePlan plan = Plan(
                new AchievementCatalogProvider().Catalog,
                new[] { 81665 });

            AchievementSkippedTarget skipped = Assert.Single(plan.SkippedTargets);
            Assert.Equal("You There! Fly!", skipped.CanonicalTitle);
            Assert.Equal(new[] { 81665 }, skipped.CandidateIds);
            Assert.Equal(1558, plan.ScanTargets.Count);
        }

        [Fact]
        public void PreviouslyTrustedNoAirForceIsSkippedBeforeZeroResultHandling()
        {
            AchievementUpdatePlan plan = Plan(
                new AchievementCatalogProvider().Catalog,
                new[] { 81581 });

            AchievementSkippedTarget skipped = Assert.Single(plan.SkippedTargets);
            Assert.Equal("No Air Force", skipped.CanonicalTitle);
            Assert.Equal(new[] { 81581 }, skipped.CandidateIds);
            Assert.DoesNotContain(plan.ScanTargets, target => target.RequestedCanonicalId == 81581);
        }

        [Theory]
        [InlineData("Zoo Tycoon", 80127, 80128, 80129)]
        [InlineData("Moonmist Treasure Hunter", 80490, 80491, 80492)]
        public void PackagedStageFamilySkipsOnlyWhenEveryStageIsTrusted(
            string title,
            int first,
            int second,
            int third)
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;

            AchievementUpdatePlan partial = Plan(catalog, new[] { first, second });
            AchievementUpdatePlan complete = Plan(catalog, new[] { first, second, third });

            Assert.Contains(partial.ScanTargets, target => target.CanonicalTitle == title);
            Assert.DoesNotContain(partial.SkippedTargets, target => target.CanonicalTitle == title);
            AchievementSkippedTarget skipped = Assert.Single(
                complete.SkippedTargets.Where(target => target.CanonicalTitle == title));
            Assert.Equal(new[] { first, second, third }, skipped.CandidateIds);
        }

        [Fact]
        public void PackagedCollisionSkipsOnlyWhenBothCandidatesAreTrusted()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;

            AchievementUpdatePlan partial = Plan(catalog, new[] { 80032 });
            AchievementUpdatePlan complete = Plan(catalog, new[] { 80032, 84004 });

            Assert.Contains(partial.ScanTargets, target => target.CanonicalTitle == "Let the Wind Lead");
            Assert.DoesNotContain(partial.SkippedTargets, target => target.CanonicalTitle == "Let the Wind Lead");
            AchievementSkippedTarget skipped = Assert.Single(
                complete.SkippedTargets.Where(target => target.CanonicalTitle == "Let the Wind Lead"));
            Assert.Equal(84004, skipped.RequestedCanonicalId);
            Assert.Equal(new[] { 80032, 84004 }, skipped.CandidateIds);
        }

        internal static AchievementCatalog PartitionCatalog() => new(new[]
        {
            Entry(1, "Unique Trusted"),
            Entry(2, "Unique Scan"),
            Entry(10, "Stage Partial", 10, 1, 3),
            Entry(11, "Stage Partial", 10, 2, 3),
            Entry(12, "Stage Partial", 10, 3, 3),
            Entry(20, "Stage Full", 20, 1, 3),
            Entry(21, "Stage Full", 20, 2, 3),
            Entry(22, "Stage Full", 20, 3, 3),
            Entry(31, "Collision Partial"),
            Entry(30, "Collision Partial"),
            Entry(41, "Collision Full"),
            Entry(40, "Collision Full"),
        });

        internal static AchievementCatalogEntry Entry(
            int id,
            string title,
            int? stageGroupId = null,
            int? stageIndex = null,
            int? stageCount = null) => new(
                id,
                title,
                "description",
                categoryId: 1,
                hidden: false,
                version: "1.0",
                order: id,
                stageGroupId,
                stageIndex,
                stageCount);

        internal static AchievementUpdatePlan Plan(
            AchievementCatalog catalog,
            IEnumerable<int> ids)
        {
            bool succeeded = AchievementUpdatePlan.TryCreate(
                catalog,
                new AchievementResumeSeed(ids),
                out AchievementUpdatePlan plan,
                out _,
                out string failure);
            Assert.True(succeeded, failure);
            return plan;
        }

        private static string SkippedSignature(AchievementSkippedTarget target) =>
            $"{target.CanonicalTitle}|{target.RequestedCanonicalId}|{string.Join(",", target.CandidateIds)}";

        private static string TargetSignature(AchievementScanTarget target) =>
            $"{target.CanonicalTitle}|{target.RequestedCanonicalId}|{string.Join(",", target.CandidateIds)}";
    }

    public sealed class AchievementUpdateScannerTests
    {
        [Theory]
        [MemberData(nameof(InvalidSeedCases))]
        public void InvalidSeedFailsBeforeDelegatedScanning(int[] ids, int[] expectedInvalid)
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));
            var progress = new RecordingUpdateProgress();

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(ids),
                CancellationToken.None,
                progress);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.False(result.ValidationSucceeded);
            Assert.Equal(expectedInvalid, result.InvalidIds);
            Assert.Empty(result.UpdatedTrustedCompletedIds);
            Assert.Empty(result.Results);
            Assert.Empty(single.RequestedIds);
            Assert.Empty(progress.Values);
        }

        [Fact]
        public void MissingSeedFailsBeforeDelegatedScanning()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                seed: null,
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.False(result.ValidationSucceeded);
            Assert.Empty(single.RequestedIds);
            Assert.Contains("No trusted", result.StopReason);
        }

        [Fact]
        public void DuplicateValidIdsAreAcceptedOnce()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Incomplete));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1, 1, 1 }),
                CancellationToken.None);

            Assert.True(result.ValidationSucceeded);
            Assert.Equal(1, result.PreviousTrustedCompletedCount);
            Assert.Equal(new[] { 1 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(1, result.SkippedTrustedTargets);
            Assert.Equal(new[] { 2, 3 }, single.RequestedIds);
        }

        [Fact]
        public void PartialTrustDelegatesOnlySelectedTargetsInPhase6HOrder()
        {
            AchievementCatalog catalog = AchievementUpdatePlanTests.PartitionCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Incomplete));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 41, 1, 22, 10, 20, 30, 40, 21 }),
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(new[] { 2, 10, 31 }, single.RequestedIds);
            Assert.Equal(3, result.SkippedTrustedTargets);
            Assert.Equal(3, result.PlannedScanTargets);
            Assert.Equal(3, result.ProcessedScanTargets);
        }

        [Fact]
        public void AllTargetsTrustedCompletesWithoutSingleScannerCall()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            var single = new RecordingSingleScanner((_, _) =>
                throw new InvalidOperationException("must not scan"));
            var progress = new RecordingUpdateProgress();
            int[] allIds = catalog.All.Select(entry => entry.Id).ToArray();

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(allIds),
                CancellationToken.None,
                progress);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(1559, result.TotalCatalogTargets);
            Assert.Equal(1559, result.SkippedTrustedTargets);
            Assert.Equal(0, result.PlannedScanTargets);
            Assert.Equal(0, result.ProcessedScanTargets);
            Assert.Equal(0, result.RemainingUnprocessedTargets);
            Assert.Equal(1854, result.PreviousTrustedCompletedCount);
            Assert.Equal(allIds.OrderBy(id => id), result.UpdatedTrustedCompletedIds);
            Assert.Empty(single.RequestedIds);
            AchievementUpdateScanProgress only = Assert.Single(progress.Values);
            Assert.Equal(1559, only.TotalCatalogTargets);
            Assert.Equal(1559, only.SkippedTrustedTargets);
            Assert.Equal(0, only.PlannedScanTargets);
            Assert.Equal(0, only.ProcessedScanTargets);
        }

        [Fact]
        public void UniqueCompletedResultMergesWithPreviousTrustedIds()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                id == 2 ? AchievementSingleScanState.Completed : AchievementSingleScanState.Incomplete));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(new[] { 1, 2 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(1, result.NewlyVerifiedCompletedCount);
            Assert.Equal(1, result.CompletedCount);
            Assert.Equal(1, result.IncompleteCount);
        }

        [Theory]
        [InlineData((int)AchievementSingleScanState.Incomplete)]
        [InlineData((int)AchievementSingleScanState.Unresolved)]
        [InlineData((int)AchievementSingleScanState.AmbiguousCollision)]
        [InlineData((int)AchievementSingleScanState.AmbiguousMultiMatch)]
        [InlineData((int)AchievementSingleScanState.PendingClaimRequiresUserAction)]
        public void NonCompletedUniqueResultsDoNotAddIds(int stateValue)
        {
            AchievementCatalog catalog = OneUniqueCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                (AchievementSingleScanState)stateValue));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(Array.Empty<int>()),
                CancellationToken.None);

            Assert.Empty(result.UpdatedTrustedCompletedIds);
            Assert.Equal(0, result.NewlyVerifiedCompletedCount);
        }

        [Fact]
        public void StageFamilyWithoutResolvedProgressPreservesPartialTrustWithoutAddingStages()
        {
            AchievementCatalog catalog = StageCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 10 }),
                CancellationToken.None);

            Assert.Equal(new[] { 10 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(0, result.NewlyVerifiedCompletedCount);
            Assert.Equal(new[] { 10 }, single.RequestedIds);
        }

        [Fact]
        public void ResolvedStageProgressMergesWithPriorIdsAndPreservesThemOnUpdate()
        {
            AchievementCatalog catalog = StageCatalog();
            AchievementStageProgressResult progress = StageProgress(
                AchievementStageProgressState.Resolved,
                currentStageIndex: 3,
                currentStageId: 12,
                completedIds: new[] { 10, 11 });
            var single = new RecordingSingleScanner((id, _) =>
                Result(
                    catalog,
                    id,
                    AchievementSingleScanState.Incomplete,
                    stageProgressResult: progress));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 10 }),
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(new[] { 10, 11 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(1, result.NewlyVerifiedCompletedCount);
            Assert.Equal(new[] { 10 }, single.RequestedIds);
        }

        [Theory]
        [InlineData((int)AchievementStageProgressState.PendingClaim)]
        [InlineData((int)AchievementStageProgressState.AmbiguousReward)]
        [InlineData((int)AchievementStageProgressState.UnresolvedReward)]
        public void UnresolvedStageProgressNeverAddsIds(int stateValue)
        {
            AchievementCatalog catalog = StageCatalog();
            AchievementStageProgressResult progress = StageProgress(
                (AchievementStageProgressState)stateValue,
                currentStageIndex: null,
                currentStageId: null,
                completedIds: Array.Empty<int>());
            var single = new RecordingSingleScanner((id, _) =>
                Result(
                    catalog,
                    id,
                    AchievementSingleScanState.Incomplete,
                    stageProgressResult: progress));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 10 }),
                CancellationToken.None);

            Assert.Equal(new[] { 10 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(0, result.NewlyVerifiedCompletedCount);
        }

        [Fact]
        public void CollisionCompletedResultCannotAddEitherCandidate()
        {
            AchievementCatalog catalog = CollisionCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 30 }),
                CancellationToken.None);

            Assert.Equal(new[] { 30 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(0, result.NewlyVerifiedCompletedCount);
            Assert.Equal(new[] { 31 }, single.RequestedIds);
        }

        [Fact]
        public void CancellationBeforeFirstTargetPreservesValidatedTrustedIds()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                cancellation.Token);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.True(result.ValidationSucceeded);
            Assert.Equal(new[] { 1 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(2, result.RemainingUnprocessedTargets);
            Assert.Empty(single.RequestedIds);
        }

        [Fact]
        public void CancellationAfterNewUniqueCompletionPreservesMergedIdAndStops()
        {
            AchievementCatalog catalog = SmallCatalog();
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

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                cancellation.Token);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(new[] { 1, 2 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(1, result.NewlyVerifiedCompletedCount);
            Assert.Equal(1, result.ProcessedScanTargets);
            Assert.Equal(new[] { 2 }, single.RequestedIds);
        }

        [Fact]
        public void DelegatedCancelledResultPreservesPriorIdsAndStops()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                AchievementSingleScanState.Cancelled,
                ready: false));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Cancelled, result.State);
            Assert.Equal(new[] { 1 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(1, result.CancelledCount);
            Assert.Equal(new[] { 2 }, single.RequestedIds);
        }

        [Fact]
        public void UnsafeResetStopsBeforeNextTargetAndPreservesPreviousIds()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                AchievementSingleScanState.Incomplete,
                ready: false));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.StoppedUnsafe, result.State);
            Assert.Equal(new[] { 1 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(1, result.ProcessedScanTargets);
            Assert.Equal(1, result.RemainingUnprocessedTargets);
            Assert.Equal(new[] { 2 }, single.RequestedIds);
        }

        [Fact]
        public void OperationalFailureStopsBeforeNextTarget()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                AchievementSingleScanState.Failed));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                CancellationToken.None);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(new[] { 2 }, single.RequestedIds);
        }

        [Fact]
        public void ProgressReportsActualScanCountsAndTrustedSkipsSeparately()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) => Result(
                catalog,
                id,
                id == 2 ? AchievementSingleScanState.Completed : AchievementSingleScanState.Incomplete));
            var progress = new RecordingUpdateProgress();

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                CancellationToken.None,
                progress);

            Assert.Equal(AchievementCatalogScanState.Completed, result.State);
            Assert.Equal(new[] { 0, 1, 2 }, progress.Values.Select(value => value.ProcessedScanTargets));
            Assert.All(progress.Values, value => Assert.Equal(3, value.TotalCatalogTargets));
            Assert.All(progress.Values, value => Assert.Equal(1, value.SkippedTrustedTargets));
            Assert.All(progress.Values, value => Assert.Equal(2, value.PlannedScanTargets));
            Assert.Equal(new[] { string.Empty, "Beta", "Gamma" },
                progress.Values.Select(value => value.CurrentTitle));
            Assert.Equal(new int?[] { null, 2, 3 },
                progress.Values.Select(value => value.CurrentRequestedId));
            Assert.Equal(new[] { 0, 1, 1 },
                progress.Values.Select(value => value.NewlyVerifiedCompletedCount));
        }

        [Fact]
        public void ProgressFailureAfterUniqueCompletionPreservesMergedIdAndResult()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));
            var progress = new RecordingUpdateProgress(throwAtProcessed: 1);

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                CancellationToken.None,
                progress);

            Assert.Equal(AchievementCatalogScanState.Failed, result.State);
            Assert.Equal(new[] { 1, 2 }, result.UpdatedTrustedCompletedIds);
            Assert.Equal(1, result.NewlyVerifiedCompletedCount);
            Assert.Single(result.Results);
            Assert.Equal(new[] { 2 }, single.RequestedIds);
            Assert.Contains("progress reporting failed", result.StopReason);
        }

        [Fact]
        public void ResultCollectionsAreReadOnlyAndContainNoBitmap()
        {
            AchievementCatalog catalog = SmallCatalog();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Incomplete));
            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(new[] { 1 }),
                CancellationToken.None);

            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<int>>(result.UpdatedTrustedCompletedIds).RemoveAt(0));
            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<AchievementSkippedTarget>>(result.SkippedTargets).RemoveAt(0));
            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<AchievementSingleScanResult>>(result.Results).RemoveAt(0));
            Assert.DoesNotContain(
                typeof(AchievementUpdateScanResult).GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                property => typeof(Bitmap).IsAssignableFrom(property.PropertyType));
            Assert.DoesNotContain(
                typeof(AchievementUpdateScanResult).GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
                field => typeof(Bitmap).IsAssignableFrom(field.FieldType));
        }

        [Fact]
        public void DiagnosticsAreBoundedSummaryOnly()
        {
            AchievementCatalog catalog = SmallCatalog();
            int[] invalid = Enumerable.Range(9000, 100).ToArray();
            var single = new RecordingSingleScanner((id, _) =>
                Result(catalog, id, AchievementSingleScanState.Completed));

            AchievementUpdateScanResult result = Scanner(catalog, single).Scan(
                new AchievementResumeSeed(invalid),
                CancellationToken.None);

            Assert.Contains("operation=achievement-update-scan", result.Diagnostics);
            Assert.Contains("catalogTargets=3", result.Diagnostics);
            Assert.Contains("previousTrustedIds=0", result.Diagnostics);
            Assert.Contains("skippedTrustedTargets=0", result.Diagnostics);
            Assert.Contains("plannedScanTargets=0", result.Diagnostics);
            Assert.Contains("processedScanTargets=0", result.Diagnostics);
            Assert.Contains("newlyVerifiedUniqueCompleted=0", result.Diagnostics);
            Assert.Contains("updatedTrustedIds=0", result.Diagnostics);
            Assert.Contains("validationSucceeded=False", result.Diagnostics);
            Assert.Contains("invalidIds=", result.Diagnostics);
            Assert.Contains(",...", result.Diagnostics);
            Assert.DoesNotContain("single diagnostics", result.Diagnostics);
            Assert.True(result.Diagnostics.Length < 1200, result.Diagnostics.Length.ToString());
        }

        public static IEnumerable<object[]> InvalidSeedCases()
        {
            yield return new object[] { new[] { 999 }, new[] { 999 } };
            yield return new object[] { new[] { 999, 1000 }, new[] { 999, 1000 } };
            yield return new object[] { new[] { 1, 999 }, new[] { 999 } };
            yield return new object[] { new[] { 0 }, new[] { 0 } };
            yield return new object[] { new[] { -7 }, new[] { -7 } };
        }

        private static AchievementCatalog SmallCatalog() => new(new[]
        {
            AchievementUpdatePlanTests.Entry(1, "Alpha"),
            AchievementUpdatePlanTests.Entry(2, "Beta"),
            AchievementUpdatePlanTests.Entry(3, "Gamma"),
        });

        private static AchievementCatalog OneUniqueCatalog() => new(new[]
        {
            AchievementUpdatePlanTests.Entry(1, "Unique"),
        });

        private static AchievementCatalog StageCatalog() => new(new[]
        {
            AchievementUpdatePlanTests.Entry(10, "Stage", 10, 1, 3),
            AchievementUpdatePlanTests.Entry(11, "Stage", 10, 2, 3),
            AchievementUpdatePlanTests.Entry(12, "Stage", 10, 3, 3),
        });

        private static AchievementCatalog CollisionCatalog() => new(new[]
        {
            AchievementUpdatePlanTests.Entry(31, "Collision"),
            AchievementUpdatePlanTests.Entry(30, "Collision"),
        });

        private static AchievementUpdateScanner Scanner(
            AchievementCatalog catalog,
            IAchievementSingleScanner singleScanner) =>
            new(new TestCatalogProvider(catalog), singleScanner);

        private static AchievementSingleScanResult Result(
            AchievementCatalog catalog,
            int requestedId,
            AchievementSingleScanState state,
            bool ready = true,
            AchievementStageProgressResult stageProgressResult = null)
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
                reason: "test result",
                diagnostics: "single diagnostics",
                stageProgressResult: stageProgressResult);
        }

        private static AchievementStageProgressResult StageProgress(
            AchievementStageProgressState state,
            int? currentStageIndex,
            int? currentStageId,
            int[] completedIds) =>
            new(
                state,
                stageGroupId: 10,
                currentStageIndex,
                currentStageId,
                completedIds,
                rewardResult: null,
                reason: "test stage progress",
                diagnostics: "stage diagnostics");

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

            public AchievementSingleScanResult Scan(
                int canonicalAchievementId,
                CancellationToken cancellationToken)
            {
                RequestedIds.Add(canonicalAchievementId);
                return scan(canonicalAchievementId, cancellationToken);
            }
        }

        private sealed class RecordingUpdateProgress : IAchievementUpdateScanProgressReporter
        {
            private readonly int? throwAtProcessed;

            internal RecordingUpdateProgress(int? throwAtProcessed = null) =>
                this.throwAtProcessed = throwAtProcessed;

            internal List<AchievementUpdateScanProgress> Values { get; } = new();

            public void Report(AchievementUpdateScanProgress value)
            {
                Values.Add(value);
                if (value.ProcessedScanTargets == throwAtProcessed)
                    throw new InvalidOperationException("progress boom");
            }
        }
    }
}
