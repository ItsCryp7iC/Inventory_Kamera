using System;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementStageProgressResolverTests
    {
        private readonly AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
        private readonly AchievementStageProgressResolver resolver = new();

        [Theory]
        [InlineData(5, 1, 80490, new int[0])]
        [InlineData(10, 2, 80491, new int[] { 80490 })]
        [InlineData(20, 3, 80492, new int[] { 80490, 80491 })]
        public void MoonmistIncompleteCompletesOnlyEarlierStages(
            int reward,
            int expectedIndex,
            int expectedCurrentId,
            int[] expectedCompleted)
        {
            AchievementTitleGroup family = catalog.GetTitleGroup("Moonmist Treasure Hunter");
            AchievementIdentityResult identity = Identity(family);

            AchievementStageProgressResult result = resolver.Resolve(
                identity,
                Completion(identity, AchievementCompletionState.Incomplete),
                Reward(reward),
                family);

            Assert.Equal(AchievementStageProgressState.Resolved, result.State);
            Assert.Equal(80490, result.StageGroupId);
            Assert.Equal(expectedIndex, result.CurrentStageIndex);
            Assert.Equal(expectedCurrentId, result.CurrentStageId);
            Assert.Equal(expectedCompleted, result.CompletedStageIds);
            Assert.Contains("stageCandidateCount=3", result.Diagnostics);
            Assert.Contains("stageResolution=Resolved", result.Diagnostics);
        }

        [Theory]
        [InlineData(5, new int[] { 80490 })]
        [InlineData(10, new int[] { 80490, 80491 })]
        [InlineData(20, new int[] { 80490, 80491, 80492 })]
        public void MoonmistCompletedClaimedIncludesCurrentButNeverLaterStages(
            int reward,
            int[] expectedCompleted)
        {
            AchievementTitleGroup family = catalog.GetTitleGroup("Moonmist Treasure Hunter");
            AchievementIdentityResult identity = Identity(family);

            AchievementStageProgressResult result = resolver.Resolve(
                identity,
                Completion(identity, AchievementCompletionState.CompletedClaimed),
                Reward(reward),
                family);

            Assert.Equal(AchievementStageProgressState.Resolved, result.State);
            Assert.Equal(expectedCompleted, result.CompletedStageIds);
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(20)]
        public void PendingClaimBlocksAllStageInferenceEvenWithReadableReward(int reward)
        {
            AchievementTitleGroup family = catalog.GetTitleGroup("Moonmist Treasure Hunter");
            AchievementIdentityResult identity = Identity(family);

            AchievementStageProgressResult result = resolver.Resolve(
                identity,
                Completion(identity, AchievementCompletionState.CompletedUnclaimed),
                Reward(reward),
                family);

            Assert.Equal(AchievementStageProgressState.PendingClaim, result.State);
            Assert.Empty(result.CompletedStageIds);
            Assert.Null(result.CurrentStageId);
        }

        [Fact]
        public void FinalFontaDuplicateRewardsRemainAmbiguousAndInferNothing()
        {
            AchievementTitleGroup family = catalog.GetTitleGroup("The Final Fonta Sea");
            AchievementIdentityResult identity = Identity(family);

            AchievementStageProgressResult result = resolver.Resolve(
                identity,
                Completion(identity, AchievementCompletionState.Incomplete),
                Reward(5),
                family);

            Assert.Equal(AchievementStageProgressState.AmbiguousReward, result.State);
            Assert.Empty(result.CompletedStageIds);
            Assert.Null(result.CurrentStageId);
            Assert.Contains("multiple stages", result.Reason);
        }

        [Fact]
        public void MissingOrUnmappedRewardRemainsUnresolvedAndInferNothing()
        {
            AchievementTitleGroup family = catalog.GetTitleGroup("Moonmist Treasure Hunter");
            AchievementIdentityResult identity = Identity(family);

            AchievementStageProgressResult missing = resolver.Resolve(
                identity,
                Completion(identity, AchievementCompletionState.Incomplete),
                AchievementRewardResult.Unresolved("test OCR failure"),
                family);
            AchievementStageProgressResult unmapped = resolver.Resolve(
                identity,
                Completion(identity, AchievementCompletionState.Incomplete),
                Reward(15),
                family);

            Assert.Equal(AchievementStageProgressState.UnresolvedReward, missing.State);
            Assert.Empty(missing.CompletedStageIds);
            Assert.Equal(AchievementStageProgressState.UnresolvedReward, unmapped.State);
            Assert.Empty(unmapped.CompletedStageIds);
        }

        [Fact]
        public void NonStageIdentityIsInvalidContext()
        {
            AchievementTitleGroup family = catalog.GetTitleGroup("Moonmist Treasure Hunter");
            AchievementIdentityResult unique = AchievementCompletionClassifierTests.Identity();

            AchievementStageProgressResult result = resolver.Resolve(
                unique,
                Completion(unique, AchievementCompletionState.Incomplete),
                Reward(5),
                family);

            Assert.Equal(AchievementStageProgressState.InvalidContext, result.State);
            Assert.Empty(result.CompletedStageIds);
        }

        private static AchievementIdentityResult Identity(AchievementTitleGroup family) =>
            new(
                AchievementIdentityState.StageGroupIdentified,
                family.Entries[0].Id,
                family.CanonicalTitle,
                null,
                family.StageGroupId,
                family.CandidateIds,
                family.CanonicalTitle,
                AchievementTitleNormalizer.Normalize(family.CanonicalTitle),
                100,
                0,
                100,
                0.95f,
                Array.Empty<AchievementIdentityOcrAttempt>(),
                AchievementCompletionClassifierTests.Detection(AchievementScreenState.SearchActive, 1),
                "test staged identity",
                "identity diagnostics");

        private static AchievementCompletionResult Completion(
            AchievementIdentityResult identity,
            AchievementCompletionState state) =>
            new(
                state,
                identity,
                AchievementCompletionClassifierTests.Detection(AchievementScreenState.SearchActive, 1),
                state == AchievementCompletionState.CompletedUnclaimed,
                state == AchievementCompletionState.CompletedClaimed,
                string.Empty,
                string.Empty,
                state == AchievementCompletionState.Incomplete ? "0/1" : string.Empty,
                state == AchievementCompletionState.Incomplete ? 0 : null,
                state == AchievementCompletionState.Incomplete ? 1 : null,
                string.Empty,
                string.Empty,
                0.95f,
                Array.Empty<AchievementCompletionOcrAttempt>(),
                "test completion",
                "completion diagnostics");

        private static AchievementRewardResult Reward(int value)
        {
            var attempt = new AchievementRewardOcrAttempt(
                AchievementRewardOcrVariant.GrayscaleContrast,
                value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                0.95f);
            return new AchievementRewardResult(
                new[] { attempt },
                attempt,
                value,
                "test reward",
                "reward diagnostics");
        }
    }
}
