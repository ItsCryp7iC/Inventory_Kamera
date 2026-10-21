using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    public sealed class AchievementSingleScannerTests
    {
        private const int UniqueId = 81665;
        private const int StageGroupId = 80127;
        private const int CollisionIdA = 80032;
        private const int CollisionIdB = 84004;
        private const int NoAirForceId = 81581;
        private const int HiddenStageGroupId = 90001;

        [Fact]
        public void NoAirForce_ExplicitNoResultsIsIncompleteClearsAndTrustsNoId()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NoResultsNavigationResult(
                "No Air Force",
                new Bitmap(8, 8));

            AchievementSingleScanResult result = harness.Scan(NoAirForceId);

            Assert.Equal(AchievementSingleScanState.Incomplete, result.State);
            Assert.Equal("No Air Force", result.CanonicalTitle);
            Assert.True(result.ReadyForNextAchievement);
            Assert.True(result.ExitSucceeded);
            Assert.Equal(AchievementScreenState.Idle, result.FinalScreenState);
            Assert.Null(result.IdentityResult);
            Assert.Null(result.CompletionResult);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(0, harness.RewardReader.CallCount);
            Assert.Empty(AchievementTrustedCompletionExtractor.Extract(new[] { result }));
            Assert.Contains("zeroResultDecision=hidden-unique-incomplete", result.Diagnostics);
        }

        [Fact]
        public void ExplicitNoResultsForNonHiddenTargetIsUnresolvedButSafeToContinue()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NoResultsNavigationResult(
                "You There! Fly!",
                new Bitmap(8, 8));

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Unresolved, result.State);
            Assert.True(result.ReadyForNextAchievement);
            Assert.Contains("non-hidden canonical achievement", result.Reason);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Classifier.CallCount);
        }

        [Fact]
        public void ExplicitNoResultsForAllHiddenStageFamilyMeansZeroStagesComplete()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NoResultsNavigationResult(
                "Hidden Stage Family",
                new Bitmap(8, 8));

            AchievementSingleScanResult result = harness.Scan(HiddenStageGroupId);

            Assert.Equal(AchievementSingleScanState.Incomplete, result.State);
            Assert.Equal(AchievementStageProgressState.Resolved, result.StageProgressResult.State);
            Assert.Empty(result.StageProgressResult.CompletedStageIds);
            Assert.True(result.ReadyForNextAchievement);
            Assert.Empty(AchievementTrustedCompletionExtractor.Extract(new[] { result }));
        }

        [Fact]
        public void ZeroResultClearFailureIsUnsafeAndStopsBeforeAnotherTarget()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NoResultsNavigationResult(
                "No Air Force",
                new Bitmap(8, 8));
            harness.Navigator.ExitResult = NavigationResult(
                "exit",
                succeeded: false,
                cancelled: false,
                AchievementScreenState.SearchNoResults,
                AchievementScreenState.SearchNoResults,
                "clear failed");

            AchievementSingleScanResult result = harness.Scan(NoAirForceId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.False(result.ReadyForNextAchievement);
            Assert.True(result.ExitAttempted);
        }

        [Fact]
        public void CancellationDuringZeroResultHandlingIsPromptAndRunsNoOcr()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NoResultsNavigationResult(
                "No Air Force",
                new Bitmap(8, 8));
            using var cancellation = new CancellationTokenSource();
            harness.Navigator.AfterSearch = cancellation.Cancel;

            AchievementSingleScanResult result = harness.Scan(
                NoAirForceId,
                cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.False(result.ExitAttempted);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Classifier.CallCount);
        }

        [Fact]
        public void SuccessfulZeroResultClearSuppliesIdleProofForNextTarget()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NoResultsNavigationResult(
                "No Air Force",
                new Bitmap(8, 8));
            AchievementSingleScanResult zero = harness.Scan(NoAirForceId);
            int capturesAfterZero = harness.Capture.CaptureCount;
            harness.Navigator.SearchResult = NavigationResult(
                "search",
                succeeded: true,
                cancelled: false,
                AchievementScreenState.Idle,
                AchievementScreenState.SearchActive,
                null,
                resultScreenshot: new Bitmap(8, 8));

            AchievementSingleScanResult next = harness.Scan(UniqueId);

            Assert.True(zero.ReadyForNextAchievement);
            Assert.Equal(AchievementSingleScanState.Completed, next.State);
            Assert.Equal(capturesAfterZero, harness.Capture.CaptureCount);
            Assert.Equal(2, harness.Navigator.SearchCount);
        }

        [Fact]
        public void NavigatorOwnedResultFrame_IsReusedWithoutSecondResultCapture()
        {
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(),
                AchievementCompletionState.CompletedClaimed);
            harness.Navigator.SearchResult = NavigationResult(
                "search",
                succeeded: true,
                cancelled: false,
                AchievementScreenState.Idle,
                AchievementScreenState.SearchActive,
                null,
                resultScreenshot: new Bitmap(8, 8));

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Completed, result.State);
            Assert.Equal(1, harness.Capture.CaptureCount);
            Assert.True(result.ExitSucceeded);
        }

        [Fact]
        public void PreparedSteadyState_ReusesReadinessAndClearProofWithoutStartCapture()
        {
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(),
                AchievementCompletionState.CompletedClaimed);
            harness.Prepare();
            harness.Navigator.SearchResult = NavigationResult(
                "search", true, false,
                AchievementScreenState.Idle,
                AchievementScreenState.SearchActive,
                null,
                resultScreenshot: new Bitmap(8, 8));

            AchievementSingleScanResult first = harness.Scan(UniqueId);
            harness.Navigator.SearchResult = NavigationResult(
                "search", true, false,
                AchievementScreenState.Idle,
                AchievementScreenState.SearchActive,
                null,
                resultScreenshot: new Bitmap(8, 8));
            AchievementSingleScanResult second = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Completed, first.State);
            Assert.Equal(AchievementSingleScanState.Completed, second.State);
            Assert.Equal(0, harness.Capture.CaptureCount);
            Assert.Equal(1, harness.Navigator.PrepareCount);
        }

        [Theory]
        [InlineData((int)AchievementCompletionState.CompletedClaimed, (int)AchievementSingleScanState.Completed)]
        [InlineData((int)AchievementCompletionState.CompletedUnclaimed, (int)AchievementSingleScanState.Completed)]
        [InlineData((int)AchievementCompletionState.Incomplete, (int)AchievementSingleScanState.Incomplete)]
        public void CompletionRecovery_UsesOneFreshFrameAndAcceptsOnlyResolvedEvidence(
            int recoveredCompletionValue,
            int expectedStateValue)
        {
            AchievementIdentityResult identity = AchievementCompletionClassifierTests.Identity();
            Harness harness = Harness.Create(identity, AchievementCompletionState.Unresolved);
            AchievementCompletionState recovered = (AchievementCompletionState)recoveredCompletionValue;
            harness.Classifier.Enqueue(Completion(identity, recovered));

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal((AchievementSingleScanState)expectedStateValue, result.State);
            Assert.Equal(2, harness.Classifier.CallCount);
            Assert.True(result.Timing.CompletionRecoveryUsed);
            Assert.False(result.Timing.IdentityRecoveryUsed);
            Assert.Equal(1, result.Timing.ExtraCaptureCount);
            Assert.Equal(AchievementSingleScanner.RecognitionRecoveryDelayMs,
                result.Timing.RecoveryDelayMilliseconds);
        }

        [Fact]
        public void CompletionRecovery_BothFramesUncertain_RemainsUnresolved()
        {
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(),
                AchievementCompletionState.Unresolved);

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Unresolved, result.State);
            Assert.Equal(2, harness.Classifier.CallCount);
            Assert.Equal(1, result.Timing.ExtraCaptureCount);
            Assert.Contains("completion:Unresolved", result.Timing.RecoveryResult);
        }

        [Fact]
        public void CompletionRecovery_ConflictingSecondFrame_RemainsUnresolved()
        {
            AchievementIdentityResult identity = AchievementCompletionClassifierTests.Identity();
            Harness harness = Harness.Create(identity, AchievementCompletionState.Unresolved);
            harness.Classifier.Enqueue(Completion(
                identity,
                AchievementCompletionState.Unresolved,
                "Claim evidence conflicts with progress evidence."));

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Unresolved, result.State);
            Assert.Contains("conflicts", result.Reason);
            Assert.Equal(1, result.Timing.ExtraCaptureCount);
        }

        [Fact]
        public void IdentityRecovery_SecondExactFrameContinuesToCompletionWithoutSecondRecoveryCapture()
        {
            AchievementIdentityResult unresolved = AchievementCompletionClassifierTests.Identity(
                AchievementIdentityState.Unresolved);
            AchievementIdentityResult identified = AchievementCompletionClassifierTests.Identity();
            Harness harness = Harness.Create(unresolved, AchievementCompletionState.CompletedClaimed);
            harness.IdentityVerifier.Enqueue(identified);

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Completed, result.State);
            Assert.Equal(2, harness.IdentityVerifier.CallCount);
            Assert.Equal(1, harness.Classifier.CallCount);
            Assert.True(result.Timing.IdentityRecoveryUsed);
            Assert.False(result.Timing.CompletionRecoveryUsed);
            Assert.Equal(1, result.Timing.ExtraCaptureCount);
        }

        [Fact]
        public void CancellationBeforeRecognitionRecovery_StopsPromptlyWithoutCaptureOrExit()
        {
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(),
                AchievementCompletionState.Unresolved);
            using var cancellation = new CancellationTokenSource();
            harness.Classifier.AfterClassify = cancellation.Cancel;

            AchievementSingleScanResult result = harness.Scan(UniqueId, cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.Equal(0, result.Timing.ExtraCaptureCount);
            Assert.Equal(2, harness.Capture.CaptureCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
        }

        [Theory]
        [InlineData((int)AchievementCompletionState.CompletedClaimed, (int)AchievementSingleScanState.Completed)]
        [InlineData((int)AchievementCompletionState.CompletedUnclaimed, (int)AchievementSingleScanState.Completed)]
        [InlineData((int)AchievementCompletionState.Incomplete, (int)AchievementSingleScanState.Incomplete)]
        [InlineData((int)AchievementCompletionState.Unresolved, (int)AchievementSingleScanState.Unresolved)]
        public void UniqueAchievement_MapsEvidenceAndReturnsToIdle(
            int completionStateValue,
            int expectedStateValue)
        {
            AchievementCompletionState completionState =
                (AchievementCompletionState)completionStateValue;
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(),
                completionState);

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal((AchievementSingleScanState)expectedStateValue, result.State);
            Assert.Equal("You There! Fly!", result.CanonicalTitle);
            Assert.Equal("You There! Fly!", harness.Navigator.LastQuery);
            Assert.Equal(completionState, result.CompletionResult.State);
            Assert.True(result.ExitAttempted);
            Assert.True(result.ExitSucceeded);
            Assert.True(result.ReadyForNextAchievement);
            Assert.Equal(AchievementScreenState.Idle, result.FinalScreenState);
            Assert.Equal(
                completionState == AchievementCompletionState.Unresolved ? 3 : 2,
                harness.Capture.CaptureCount);
            Assert.Equal(0, harness.RewardReader.CallCount);
        }

        [Theory]
        [InlineData(
            (int)AchievementCompletionState.CompletedUnclaimed,
            (int)AchievementSingleScanState.PendingClaimRequiresUserAction,
            true)]
        [InlineData(
            (int)AchievementCompletionState.CompletedClaimed,
            (int)AchievementSingleScanState.Completed,
            false)]
        [InlineData(
            (int)AchievementCompletionState.Incomplete,
            (int)AchievementSingleScanState.Incomplete,
            false)]
        public void StageFamily_PreservesEvidenceWithoutSelectingOrGuessingStage(
            int completionStateValue,
            int expectedStateValue,
            bool pendingClaim)
        {
            AchievementIdentityResult identity =
                AchievementCompletionClassifierTests.Identity(
                    AchievementIdentityState.StageGroupIdentified);
            Harness harness = Harness.Create(
                identity,
                (AchievementCompletionState)completionStateValue);

            AchievementSingleScanResult result = harness.Scan(StageGroupId);

            Assert.Equal((AchievementSingleScanState)expectedStateValue, result.State);
            Assert.Equal(pendingClaim, result.PendingClaim);
            Assert.Equal(AchievementIdentityState.StageGroupIdentified, result.IdentityResult.State);
            Assert.Equal(StageGroupId, result.IdentityResult.StageGroupId);
            Assert.Null(result.IdentityResult.IdentifiedId);
            Assert.Equal(new[] { 80127, 80128, 80129 }, result.IdentityResult.CandidateIds);
            Assert.Equal(
                (AchievementCompletionState)completionStateValue,
                result.CompletionResult.State);
            Assert.NotNull(result.StageProgressResult);
            Assert.Equal(
                pendingClaim
                    ? AchievementStageProgressState.PendingClaim
                    : AchievementStageProgressState.Resolved,
                result.StageProgressResult.State);
            Assert.Equal(pendingClaim ? 0 : 1, harness.RewardReader.CallCount);
            Assert.Equal(
                completionStateValue == (int)AchievementCompletionState.CompletedClaimed
                    ? new[] { 80127 }
                    : Array.Empty<int>(),
                result.StageProgressResult.CompletedStageIds);
            Assert.DoesNotContain("starCount=", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("inferredStageId=", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("descriptionEvidence=", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.True(result.ReadyForNextAchievement);
        }

        [Fact]
        public void StageRewardOcrFailureRemainsStructuredAndStillExitsSearch()
        {
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(
                    AchievementIdentityState.StageGroupIdentified),
                AchievementCompletionState.Incomplete);
            harness.RewardReader.Exception = new InvalidOperationException("reward OCR boom");

            AchievementSingleScanResult result = harness.Scan(StageGroupId);

            Assert.Equal(AchievementSingleScanState.Incomplete, result.State);
            Assert.Equal(AchievementStageProgressState.UnresolvedReward, result.StageProgressResult.State);
            Assert.Empty(result.StageProgressResult.CompletedStageIds);
            Assert.Contains("Reward OCR failed", result.StageProgressResult.Reason);
            Assert.Equal(1, harness.RewardReader.CallCount);
            Assert.True(result.ExitSucceeded);
            Assert.True(result.ReadyForNextAchievement);
        }

        [Theory]
        [InlineData((int)AchievementCompletionState.Incomplete, new int[] { 80127, 80128 })]
        [InlineData((int)AchievementCompletionState.CompletedClaimed, new int[] { 80127, 80128, 80129 })]
        public void StageFamilyPreservesResolvedStageThreeProgress(
            int completionStateValue,
            int[] expectedCompletedIds)
        {
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(
                    AchievementIdentityState.StageGroupIdentified),
                (AchievementCompletionState)completionStateValue);
            harness.RewardReader.Result = Reward(20);

            AchievementSingleScanResult result = harness.Scan(StageGroupId);

            Assert.Equal(AchievementStageProgressState.Resolved, result.StageProgressResult.State);
            Assert.Equal(3, result.StageProgressResult.CurrentStageIndex);
            Assert.Equal(80129, result.StageProgressResult.CurrentStageId);
            Assert.Equal(expectedCompletedIds, result.StageProgressResult.CompletedStageIds);
            Assert.True(result.ExitSucceeded);
        }

        [Theory]
        [InlineData((int)AchievementIdentityState.Ambiguous)]
        [InlineData((int)AchievementIdentityState.Mismatch)]
        [InlineData((int)AchievementIdentityState.Unresolved)]
        public void IdentityFailure_SkipsCompletionAndAttemptsVerifiedExit(int identityStateValue)
        {
            Harness harness = Harness.Create(
                AchievementCompletionClassifierTests.Identity(
                    (AchievementIdentityState)identityStateValue),
                AchievementCompletionState.CompletedClaimed);

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Unresolved, result.State);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(1, harness.Navigator.ExitCount);
            Assert.True(result.ExitSucceeded);
            Assert.True(result.ReadyForNextAchievement);
            bool expectedRecovery =
                (AchievementIdentityState)identityStateValue == AchievementIdentityState.Unresolved;
            Assert.Equal(expectedRecovery ? 2 : 1, harness.IdentityVerifier.CallCount);
            Assert.Equal(expectedRecovery, result.Timing.IdentityRecoveryUsed);
            Assert.Equal(expectedRecovery ? 1 : 0, result.Timing.ExtraCaptureCount);
        }

        [Theory]
        [InlineData(CollisionIdA)]
        [InlineData(CollisionIdB)]
        public void UnrelatedCollision_NeverUsesRequestedIdAsIdentityProof(int requestedId)
        {
            Harness harness = Harness.Create();

            AchievementSingleScanResult result = harness.Scan(requestedId);

            Assert.Equal(AchievementSingleScanState.AmbiguousCollision, result.State);
            Assert.Equal(AchievementTitleGroupKind.UnrelatedCollision, result.TitleGroupKind);
            Assert.Equal(new[] { CollisionIdA, CollisionIdB }, result.CandidateIds);
            Assert.Equal(1, result.MatchCount);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(1, harness.IdentityVerifier.CallCount);
            Assert.False(result.Timing.IdentityRecoveryUsed);
            Assert.Equal(1, harness.Navigator.ExitCount);
            Assert.True(result.ReadyForNextAchievement);
            Assert.Contains("individual ID", result.Reason);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        public void MultiMatch_NeverInfersFirstResultIdentityOrCompletion(int matchCount)
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NavigationResult(
                "search",
                succeeded: true,
                cancelled: false,
                AchievementScreenState.Idle,
                AchievementScreenState.SearchActive,
                null,
                afterMatchCount: matchCount);

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.AmbiguousMultiMatch, result.State);
            Assert.Equal(AchievementTitleGroupKind.Unique, result.TitleGroupKind);
            Assert.Equal(matchCount, result.MatchCount);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(1, harness.IdentityVerifier.CallCount);
            Assert.False(result.Timing.IdentityRecoveryUsed);
            Assert.Equal(1, harness.Navigator.ExitCount);
            Assert.True(result.ReadyForNextAchievement);
        }

        [Fact]
        public void ExitFailureAfterMultiMatch_ReportsNotReadyWithoutCompletionInference()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NavigationResult(
                "search",
                succeeded: true,
                cancelled: false,
                AchievementScreenState.Idle,
                AchievementScreenState.SearchActive,
                null,
                afterMatchCount: 5);
            harness.Navigator.ExitResult = NavigationResult(
                "exit",
                succeeded: false,
                cancelled: false,
                AchievementScreenState.SearchActive,
                AchievementScreenState.Unknown,
                "idle was not verified");

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.True(result.ExitAttempted);
            Assert.False(result.ExitSucceeded);
            Assert.False(result.ReadyForNextAchievement);
            Assert.Contains("multiple matches", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData((int)AchievementScreenState.SearchActive, 1)]
        [InlineData((int)AchievementScreenState.Unknown, 0)]
        [InlineData((int)AchievementScreenState.NotAchievementScreen, 0)]
        public void InvalidStartState_RefusesBeforeSearch(int stateValue, int matchCount)
        {
            Harness harness = Harness.Create();
            harness.StartDetector.Result = Detection(
                (AchievementScreenState)stateValue,
                matchCount);

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Equal(0, harness.Navigator.SearchCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Classifier.CallCount);
        }

        [Fact]
        public void UnknownCanonicalId_FailsBeforeCaptureOrInput()
        {
            Harness harness = Harness.Create();

            AchievementSingleScanResult result = harness.Scan(999999);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Contains("not found", result.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, harness.Capture.CaptureCount);
            Assert.Equal(0, harness.Navigator.SearchCount);
        }

        [Fact]
        public void SearchFailure_StopsBeforeIdentityAndDoesNotBlindlyExitUnknownState()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NavigationResult(
                "search",
                succeeded: false,
                cancelled: false,
                AchievementScreenState.Idle,
                AchievementScreenState.Unknown,
                "submitted results were not verified");

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
            Assert.Equal(AchievementScreenState.Unknown, result.FinalScreenState);
        }

        [Fact]
        public void InconsistentSuccessfulSearchWithoutSearchActive_FailsClosed()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NavigationResult(
                "search",
                succeeded: true,
                cancelled: false,
                AchievementScreenState.Idle,
                AchievementScreenState.Unknown,
                null);

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
        }

        [Fact]
        public void ExitFailure_PreservesEvidenceAndReportsNotReady()
        {
            Harness harness = Harness.Create();
            harness.Navigator.ExitResult = NavigationResult(
                "exit",
                succeeded: false,
                cancelled: false,
                AchievementScreenState.SearchActive,
                AchievementScreenState.Unknown,
                "idle was not verified");

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Equal(AchievementCompletionState.CompletedClaimed, result.CompletionResult.State);
            Assert.True(result.ExitAttempted);
            Assert.False(result.ExitSucceeded);
            Assert.False(result.ReadyForNextAchievement);
            Assert.Equal(AchievementScreenState.Unknown, result.FinalScreenState);
        }

        [Fact]
        public void CancelledBeforeStart_PerformsNoCaptureOrInput()
        {
            Harness harness = Harness.Create();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            AchievementSingleScanResult result = harness.Scan(UniqueId, cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.Equal(0, harness.Capture.CaptureCount);
            Assert.Equal(0, harness.Navigator.SearchCount);
        }

        [Fact]
        public void CancelledBeforeSearch_SendsNoSearchInput()
        {
            Harness harness = Harness.Create();
            using var cancellation = new CancellationTokenSource();
            harness.StartDetector.AfterDetect = cancellation.Cancel;

            AchievementSingleScanResult result = harness.Scan(UniqueId, cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.Equal(0, harness.Navigator.SearchCount);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
        }

        [Fact]
        public void CancelledAfterSearch_StopsBeforeIdentityAndExit()
        {
            Harness harness = Harness.Create();
            using var cancellation = new CancellationTokenSource();
            harness.Navigator.AfterSearch = cancellation.Cancel;

            AchievementSingleScanResult result = harness.Scan(UniqueId, cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
        }

        [Fact]
        public void CancelledAfterIdentity_StopsBeforeCompletionAndExit()
        {
            Harness harness = Harness.Create();
            using var cancellation = new CancellationTokenSource();
            harness.IdentityVerifier.AfterVerify = cancellation.Cancel;

            AchievementSingleScanResult result = harness.Scan(UniqueId, cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.NotNull(result.IdentityResult);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
        }

        [Fact]
        public void CancelledBeforeExit_PreservesCompletionAndSendsNoExitInput()
        {
            Harness harness = Harness.Create();
            using var cancellation = new CancellationTokenSource();
            harness.Classifier.AfterClassify = cancellation.Cancel;

            AchievementSingleScanResult result = harness.Scan(UniqueId, cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.Equal(AchievementCompletionState.CompletedClaimed, result.CompletionResult.State);
            Assert.Equal(0, harness.Navigator.ExitCount);
            Assert.False(result.ExitAttempted);
        }

        [Fact]
        public void CancellationDuringExit_IsStructuredAndBounded()
        {
            Harness harness = Harness.Create();
            using var cancellation = new CancellationTokenSource();
            harness.Navigator.AfterExit = cancellation.Cancel;
            harness.Navigator.ExitResult = NavigationResult(
                "exit",
                succeeded: false,
                cancelled: true,
                AchievementScreenState.SearchActive,
                AchievementScreenState.SearchActive,
                "exit cancelled");

            AchievementSingleScanResult result = harness.Scan(UniqueId, cancellation.Token);

            Assert.Equal(AchievementSingleScanState.Cancelled, result.State);
            Assert.Equal(1, harness.Navigator.ExitCount);
            Assert.True(result.ExitAttempted);
            Assert.False(result.ExitSucceeded);
        }

        [Fact]
        public void ResultCaptureException_IsStructuredAndStillAttemptsExit()
        {
            Harness harness = Harness.Create();
            harness.Capture.ThrowOnCapture = 2;

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Contains("result capture failed", result.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, harness.Navigator.ExitCount);
            Assert.True(result.ExitSucceeded);
        }

        [Fact]
        public void StartCaptureException_IsStructuredBeforeInput()
        {
            Harness harness = Harness.Create();
            harness.Capture.ThrowOnCapture = 1;

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Contains("start-state", result.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, harness.Navigator.SearchCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
        }

        [Fact]
        public void SearchException_IsStructuredBeforeDownstreamCalls()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchException = new InvalidOperationException("search boom");

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Contains("search boom", result.Reason);
            Assert.Equal(0, harness.IdentityVerifier.CallCount);
            Assert.Equal(0, harness.Navigator.ExitCount);
        }

        [Fact]
        public void IdentityException_IsStructuredAndStillAttemptsExit()
        {
            Harness harness = Harness.Create();
            harness.IdentityVerifier.Exception = new InvalidOperationException("identity boom");

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Contains("identity boom", result.Reason);
            Assert.Equal(0, harness.Classifier.CallCount);
            Assert.Equal(1, harness.Navigator.ExitCount);
        }

        [Fact]
        public void CompletionException_IsStructuredAndStillAttemptsExit()
        {
            Harness harness = Harness.Create();
            harness.Classifier.Exception = new InvalidOperationException("completion boom");

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.Contains("completion boom", result.Reason);
            Assert.Equal(1, harness.Navigator.ExitCount);
        }

        [Fact]
        public void ExitException_PreservesEvidenceAsStructuredFailure()
        {
            Harness harness = Harness.Create();
            harness.Navigator.ExitException = new InvalidOperationException("exit boom");

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Equal(AchievementSingleScanState.Failed, result.State);
            Assert.NotNull(result.IdentityResult);
            Assert.NotNull(result.CompletionResult);
            Assert.Contains("exit boom", result.Reason);
            Assert.True(result.ExitAttempted);
            Assert.False(result.ExitSucceeded);
        }

        [Fact]
        public void Diagnostics_AreBoundedAndContainTheOrchestrationContract()
        {
            Harness harness = Harness.Create();
            harness.Navigator.SearchResult = NavigationResult(
                "search",
                succeeded: true,
                cancelled: false,
                AchievementScreenState.Idle,
                AchievementScreenState.SearchActive,
                null,
                new string('X', 2000));

            AchievementSingleScanResult result = harness.Scan(UniqueId);

            Assert.Contains("operation=single-achievement-scan", result.Diagnostics);
            Assert.Contains("requestedId=81665", result.Diagnostics);
            Assert.Contains("titleLength=15", result.Diagnostics);
            Assert.Contains("titleGroupKind=Unique", result.Diagnostics);
            Assert.Contains("candidateCount=1", result.Diagnostics);
            Assert.Contains("candidateIds=81665", result.Diagnostics);
            Assert.Contains("matchCount=1", result.Diagnostics);
            Assert.Contains("collision=False", result.Diagnostics);
            Assert.Contains("multiMatch=False", result.Diagnostics);
            Assert.Contains("startState=Idle", result.Diagnostics);
            Assert.Contains("searchSucceeded=True", result.Diagnostics);
            Assert.Contains("identityState=Identified", result.Diagnostics);
            Assert.Contains("completionState=CompletedClaimed", result.Diagnostics);
            Assert.Contains("completionInvoked=True", result.Diagnostics);
            Assert.Contains("pendingClaim=False", result.Diagnostics);
            Assert.Contains("exitSucceeded=True", result.Diagnostics);
            Assert.Contains("finalState=Idle", result.Diagnostics);
            Assert.Contains("cancelled=False", result.Diagnostics);
            Assert.DoesNotContain(new string('X', 500), result.Diagnostics);
            Assert.True(result.Diagnostics.Length < 4096, result.Diagnostics.Length.ToString());
        }

        private static AchievementScreenDetection Detection(
            AchievementScreenState state,
            int matchCount) =>
            AchievementCompletionClassifierTests.Detection(state, matchCount);

        private static AchievementSearchNavigationResult NavigationResult(
            string operation,
            bool succeeded,
            bool cancelled,
            AchievementScreenState before,
            AchievementScreenState after,
            string failureReason,
            string diagnostics = "navigation diagnostics",
            int afterMatchCount = 1,
            Bitmap resultScreenshot = null) =>
            new(
                operation,
                succeeded,
                cancelled,
                failureReason,
                Detection(before, before == AchievementScreenState.SearchActive ? 1 : 0),
                null,
                null,
                null,
                Detection(after, after == AchievementScreenState.SearchActive ? afterMatchCount : 0),
                1,
                diagnostics,
                resultScreenshot);

        private static AchievementSearchNavigationResult NoResultsNavigationResult(
            string query,
            Bitmap resultScreenshot = null) =>
            new(
                "search",
                succeeded: true,
                cancelled: false,
                failureReason: null,
                Detection(AchievementScreenState.Idle, 0),
                null,
                null,
                null,
                NoResultsDetection(query),
                1,
                "explicit no-results navigation",
                resultScreenshot);

        private static AchievementScreenDetection NoResultsDetection(string query) =>
            AchievementScreenDetector.Classify(
                new Size(1920, 1080),
                AchievementTextEvidence.ForTest("Achievements", true),
                AchievementSearchFieldEvidence.ForTest(
                    activeControlsAccepted: true,
                    rawText: query + " Clear"),
                AchievementTextEvidence.ForTest("Achievements completed: 1", true),
                AchievementMatchCountParser.Parse(string.Empty),
                AchievementPageCounterParser.Parse(string.Empty),
                noResultsEvidence: AchievementTextEvidence.ForTest(
                    $"No achievements related to \"{query}\"",
                    true));

        private static AchievementCompletionResult Completion(
            AchievementIdentityResult identity,
            AchievementCompletionState state,
            string unresolvedReason = null)
        {
            bool claim = state == AchievementCompletionState.CompletedUnclaimed;
            bool completed = state == AchievementCompletionState.CompletedClaimed;
            bool incomplete = state == AchievementCompletionState.Incomplete;
            var attempts = new[]
            {
                new AchievementCompletionOcrAttempt(
                    AchievementCompletionOcrVariant.GrayscaleContrast,
                    claim ? "Claim" : completed ? "Completed" : incomplete ? "0/1" : "Reward",
                    0.90f,
                    "test evidence"),
            };
            return new AchievementCompletionResult(
                state,
                identity,
                Detection(AchievementScreenState.SearchActive, 1),
                claim,
                completed,
                claim ? "Claim" : string.Empty,
                completed ? "Completed" : string.Empty,
                incomplete ? "0/1" : string.Empty,
                incomplete ? 0 : null,
                incomplete ? 1 : null,
                string.Empty,
                attempts[0].RawText,
                0.90f,
                attempts,
                state == AchievementCompletionState.Unresolved
                    ? unresolvedReason ?? "test unresolved completion"
                    : "test positive completion evidence",
                "completion diagnostics");
        }

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

        private sealed class Harness
        {
            private readonly AchievementSingleScanner scanner;

            private Harness(
                AchievementIdentityResult identity,
                AchievementCompletionState completionState)
            {
                StartDetector = new RecordingScreenDetector(
                    Detection(AchievementScreenState.Idle, 0));
                Navigator = new RecordingSearchNavigator
                {
                    SearchResult = NavigationResult(
                        "search",
                        succeeded: true,
                        cancelled: false,
                        AchievementScreenState.Idle,
                        AchievementScreenState.SearchActive,
                        null),
                    ExitResult = NavigationResult(
                        "exit",
                        succeeded: true,
                        cancelled: false,
                        AchievementScreenState.SearchActive,
                        AchievementScreenState.Idle,
                        null),
                };
                IdentityVerifier = new RecordingIdentityVerifier(identity);
                Classifier = new RecordingCompletionClassifier(
                    Completion(identity, completionState));
                RewardReader = new RecordingRewardReader(Reward(5));
                Capture = new RecordingCapture();
                scanner = new AchievementSingleScanner(
                    new TestCatalogProvider(TestCatalog()),
                    StartDetector,
                    Navigator,
                    IdentityVerifier,
                    Classifier,
                    Capture,
                    RewardReader,
                    new AchievementStageProgressResolver(),
                    (milliseconds, token) => token.ThrowIfCancellationRequested());
            }

            internal RecordingScreenDetector StartDetector { get; }
            internal RecordingSearchNavigator Navigator { get; }
            internal RecordingIdentityVerifier IdentityVerifier { get; }
            internal RecordingCompletionClassifier Classifier { get; }
            internal RecordingRewardReader RewardReader { get; }
            internal RecordingCapture Capture { get; }

            internal static Harness Create(
                AchievementIdentityResult identity = null,
                AchievementCompletionState completionState = AchievementCompletionState.CompletedClaimed) =>
                new(
                    identity ?? AchievementCompletionClassifierTests.Identity(),
                    completionState);

            internal AchievementSingleScanResult Scan(
                int id,
                CancellationToken cancellationToken = default) =>
                scanner.Scan(id, cancellationToken);

            internal void Prepare() => scanner.PrepareForScan(CancellationToken.None);
        }

        private static AchievementCatalog TestCatalog() => new(new[]
        {
            new AchievementCatalogEntry(
                UniqueId, "You There! Fly!", "description", 1, false, "1.0", 1,
                null, null, null, 5),
            new AchievementCatalogEntry(
                80127, "Zoo Tycoon", "stage 1", 1, false, "1.0", 2,
                80127, 1, 3, 5),
            new AchievementCatalogEntry(
                80128, "Zoo Tycoon", "stage 2", 1, false, "1.0", 3,
                80127, 2, 3, 10),
            new AchievementCatalogEntry(
                80129, "Zoo Tycoon", "stage 3", 1, false, "1.0", 4,
                80127, 3, 3, 20),
            new AchievementCatalogEntry(
                CollisionIdB, "Let the Wind Lead", "collision B", 1, false, "1.0", 5,
                null, null, null, 5),
            new AchievementCatalogEntry(
                CollisionIdA, "Let the Wind Lead", "collision A", 1, false, "1.0", 6,
                null, null, null, 5),
            new AchievementCatalogEntry(
                NoAirForceId, "No Air Force", "Catch and defeat the Capybara King.", 0, true, "5.8", 7,
                null, null, null, 5),
            new AchievementCatalogEntry(
                HiddenStageGroupId, "Hidden Stage Family", "stage 1", 1, true, "1.0", 8,
                HiddenStageGroupId, 1, 2, 5),
            new AchievementCatalogEntry(
                HiddenStageGroupId + 1, "Hidden Stage Family", "stage 2", 1, true, "1.0", 9,
                HiddenStageGroupId, 2, 2, 10),
        });

        private sealed class TestCatalogProvider : IAchievementCatalogProvider
        {
            internal TestCatalogProvider(AchievementCatalog catalog) => Catalog = catalog;
            public AchievementCatalog Catalog { get; }
        }

        private sealed class RecordingScreenDetector : IAchievementScreenDetector
        {
            internal RecordingScreenDetector(AchievementScreenDetection result) => Result = result;
            internal AchievementScreenDetection Result { get; set; }
            internal Action AfterDetect { get; set; }
            public AchievementScreenDetection Detect(Bitmap screenshot)
            {
                AchievementScreenDetection result = Result;
                AfterDetect?.Invoke();
                return result;
            }
        }

        private sealed class RecordingSearchNavigator : IAchievementSearchNavigator
        {
            internal AchievementSearchNavigationResult SearchResult { get; set; }
            internal AchievementSearchNavigationResult ExitResult { get; set; }
            internal Exception SearchException { get; set; }
            internal Exception ExitException { get; set; }
            internal Action AfterSearch { get; set; }
            internal Action AfterExit { get; set; }
            internal int SearchCount { get; private set; }
            internal int ExitCount { get; private set; }
            internal int PrepareCount { get; private set; }
            internal string LastQuery { get; private set; }

            public void PreparePointer(CancellationToken cancellationToken, bool forceReposition = false)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PrepareCount++;
            }

            public AchievementSearchNavigationResult Search(
                string query,
                CancellationToken cancellationToken)
            {
                SearchCount++;
                LastQuery = query;
                if (SearchException != null) throw SearchException;
                AfterSearch?.Invoke();
                return SearchResult;
            }

            public AchievementSearchNavigationResult ClearSearch(CancellationToken cancellationToken)
            {
                ExitCount++;
                if (ExitException != null) throw ExitException;
                AfterExit?.Invoke();
                return ExitResult;
            }
        }

        private sealed class RecordingIdentityVerifier : IAchievementResultIdentityVerifier
        {
            private readonly Queue<AchievementIdentityResult> queued = new();
            internal RecordingIdentityVerifier(AchievementIdentityResult result) => Result = result;
            internal AchievementIdentityResult Result { get; set; }
            internal Exception Exception { get; set; }
            internal Action AfterVerify { get; set; }
            internal int CallCount { get; private set; }
            internal void Enqueue(AchievementIdentityResult result) => queued.Enqueue(result);

            public AchievementIdentityResult Verify(
                AchievementSearchNavigationResult searchResult,
                int expectedId,
                Bitmap screenshot)
            {
                CallCount++;
                if (Exception != null) throw Exception;
                AchievementIdentityResult result = CallCount > 1 && queued.Count > 0
                    ? queued.Dequeue()
                    : Result;
                AfterVerify?.Invoke();
                return result;
            }
        }

        private sealed class RecordingCompletionClassifier : IAchievementCompletionClassifier
        {
            private readonly Queue<AchievementCompletionResult> queued = new();
            internal RecordingCompletionClassifier(AchievementCompletionResult result) => Result = result;
            internal AchievementCompletionResult Result { get; set; }
            internal Exception Exception { get; set; }
            internal Action AfterClassify { get; set; }
            internal int CallCount { get; private set; }
            internal void Enqueue(AchievementCompletionResult result) => queued.Enqueue(result);

            public AchievementCompletionResult Classify(
                AchievementIdentityResult identity,
                Bitmap screenshot)
            {
                CallCount++;
                if (Exception != null) throw Exception;
                AchievementCompletionResult result = CallCount > 1 && queued.Count > 0
                    ? queued.Dequeue()
                    : Result;
                AfterClassify?.Invoke();
                return result;
            }
        }

        private sealed class RecordingRewardReader : IAchievementRewardReader
        {
            internal RecordingRewardReader(AchievementRewardResult result) => Result = result;
            internal AchievementRewardResult Result { get; set; }
            internal Exception Exception { get; set; }
            internal int CallCount { get; private set; }

            public AchievementRewardResult Read(Bitmap screenshot)
            {
                CallCount++;
                if (Exception != null) throw Exception;
                return Result;
            }
        }

        private sealed class RecordingCapture : IGameScreenCapture
        {
            internal int CaptureCount { get; private set; }
            internal int ThrowOnCapture { get; set; }
            internal Func<int, Bitmap> Capture { get; set; } = _ => new Bitmap(1, 1);

            public Bitmap CaptureWindow()
            {
                CaptureCount++;
                if (CaptureCount == ThrowOnCapture)
                    throw new InvalidOperationException("capture boom");
                return Capture(CaptureCount);
            }
        }
    }

    public sealed class AchievementSingleScannerFixtureIntegrationTests
    {
        private static readonly Size FixtureSize = new(1919, 1079);
        private readonly ITestOutputHelper output;

        public AchievementSingleScannerFixtureIntegrationTests(ITestOutputHelper output) =>
            this.output = output;

        [Fact]
        public void YouThereFly_RealIdentityAndClaimReaders_CompleteEndToEnd()
        {
            var catalogProvider = new AchievementCatalogProvider();
            var searchActive = new FixedScreenDetector(
                AchievementCompletionClassifierTests.Detection(
                    AchievementScreenState.SearchActive,
                    1));
            using var ocr = new OcrService(engineCount: 1);
            ocr.Restart();
            var processor = new ImageProcessor();
            var identityVerifier = new AchievementResultIdentityVerifier(
                catalogProvider,
                searchActive,
                new AchievementResultTitleReader(ocr, processor));
            var completionClassifier = new AchievementCompletionClassifier(
                searchActive,
                new AchievementCompletionReader(ocr, processor));
            var navigator = new FixedSearchNavigator();
            using Bitmap composite = BuildComposite();
            var capture = new FixtureSequenceCapture(composite);
            var scanner = new AchievementSingleScanner(
                catalogProvider,
                new FixedScreenDetector(
                    AchievementCompletionClassifierTests.Detection(
                        AchievementScreenState.Idle,
                        0)),
                navigator,
                identityVerifier,
                completionClassifier,
                capture,
                new AchievementRewardReader(ocr, processor),
                new AchievementStageProgressResolver());

            AchievementSingleScanResult result = scanner.Scan(81665, CancellationToken.None);

            output.WriteLine(result.Diagnostics);
            Assert.Equal(AchievementSingleScanState.Completed, result.State);
            Assert.Equal(AchievementIdentityState.Identified, result.IdentityResult.State);
            Assert.Equal(81665, result.IdentityResult.IdentifiedId);
            Assert.Equal(
                AchievementCompletionState.CompletedUnclaimed,
                result.CompletionResult.State);
            Assert.True(result.ExitSucceeded);
            Assert.True(result.ReadyForNextAchievement);
            Assert.Equal(2, capture.CaptureCount);
        }

        private static Bitmap BuildComposite()
        {
            var screenshot = new Bitmap(FixtureSize.Width, FixtureSize.Height);
            using Graphics graphics = Graphics.FromImage(screenshot);
            graphics.Clear(Color.FromArgb(34, 44, 77));
            AchievementScreenRegions regions = AchievementScreenRegions.For(FixtureSize);
            DrawFixture(graphics, "achievement_result_title_you_there_01.png", regions.FirstResultTitle);
            DrawFixture(
                graphics,
                "achievement_completion_claim_keyboard_01.png",
                regions.FirstResultCompletionState);
            return screenshot;
        }

        private static void DrawFixture(Graphics graphics, string fixtureName, Rectangle region)
        {
            using var crop = new Bitmap(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Achievements",
                fixtureName));
            Assert.Equal(region.Size, crop.Size);
            graphics.DrawImageUnscaled(crop, region.Location);
        }

        private sealed class FixedScreenDetector : IAchievementScreenDetector
        {
            private readonly AchievementScreenDetection result;
            internal FixedScreenDetector(AchievementScreenDetection result) => this.result = result;
            public AchievementScreenDetection Detect(Bitmap screenshot) => result;
        }

        private sealed class FixedSearchNavigator : IAchievementSearchNavigator
        {
            public AchievementSearchNavigationResult Search(
                string query,
                CancellationToken cancellationToken) =>
                Result(
                    "search",
                    AchievementScreenState.Idle,
                    AchievementScreenState.SearchActive);

            public AchievementSearchNavigationResult ClearSearch(CancellationToken cancellationToken) =>
                Result(
                    "exit",
                    AchievementScreenState.SearchActive,
                    AchievementScreenState.Idle);

            private static AchievementSearchNavigationResult Result(
                string operation,
                AchievementScreenState before,
                AchievementScreenState after) =>
                new(
                    operation,
                    succeeded: true,
                    cancelled: false,
                    failureReason: null,
                    AchievementCompletionClassifierTests.Detection(
                        before,
                        before == AchievementScreenState.SearchActive ? 1 : 0),
                    null,
                    null,
                    null,
                    AchievementCompletionClassifierTests.Detection(
                        after,
                        after == AchievementScreenState.SearchActive ? 1 : 0),
                    1,
                    "fixture navigation");
        }

        private sealed class FixtureSequenceCapture : IGameScreenCapture
        {
            private readonly Bitmap result;
            internal FixtureSequenceCapture(Bitmap result) => this.result = result;
            internal int CaptureCount { get; private set; }
            public Bitmap CaptureWindow()
            {
                CaptureCount++;
                return CaptureCount == 1
                    ? new Bitmap(FixtureSize.Width, FixtureSize.Height)
                    : new Bitmap(result);
            }
        }
    }
}
