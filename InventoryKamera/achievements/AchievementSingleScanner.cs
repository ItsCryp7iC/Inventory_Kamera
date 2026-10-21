using InventoryKamera.game;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    internal interface IAchievementSingleScanner
    {
        void PrepareForScan(CancellationToken cancellationToken) { }
        void ReacquireAfterUserInteraction(CancellationToken cancellationToken) { }
        AchievementSingleScanResult Scan(
            int canonicalAchievementId,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Completed and Incomplete describe the verified visible row. Stage-family canonical IDs are
    /// exposed only through a separately resolved StageProgressResult.
    /// </summary>
    internal enum AchievementSingleScanState
    {
        Completed,
        Incomplete,
        Unresolved,
        AmbiguousCollision,
        AmbiguousMultiMatch,
        PendingClaimRequiresUserAction,
        Failed,
        Cancelled,
    }

    internal sealed class AchievementSingleScanResult
    {
        internal AchievementSingleScanResult(
            AchievementSingleScanState state,
            int requestedId,
            string canonicalTitle,
            AchievementTitleGroup titleGroup,
            AchievementScreenDetection startDetection,
            AchievementSearchNavigationResult searchResult,
            AchievementIdentityResult identityResult,
            AchievementCompletionResult completionResult,
            AchievementSearchNavigationResult exitResult,
            bool exitAttempted,
            bool exitSucceeded,
            AchievementScreenState? finalScreenState,
            bool pendingClaim,
            string reason,
            string diagnostics,
            AchievementStageProgressResult stageProgressResult = null)
        {
            State = state;
            RequestedId = requestedId;
            CanonicalTitle = canonicalTitle ?? string.Empty;
            TitleGroup = titleGroup;
            CandidateIds = new ReadOnlyCollection<int>(
                (titleGroup?.CandidateIds ?? Array.Empty<int>()).ToArray());
            StartDetection = startDetection;
            SearchResult = searchResult;
            IdentityResult = identityResult;
            CompletionResult = completionResult;
            ExitResult = exitResult;
            ExitAttempted = exitAttempted;
            ExitSucceeded = exitSucceeded;
            FinalScreenState = finalScreenState;
            PendingClaim = pendingClaim;
            Reason = reason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
            StageProgressResult = stageProgressResult;
        }

        internal AchievementSingleScanState State { get; }
        internal int RequestedId { get; }
        internal string CanonicalTitle { get; }
        internal AchievementTitleGroup TitleGroup { get; }
        internal AchievementTitleGroupKind? TitleGroupKind => TitleGroup?.Kind;
        internal IReadOnlyList<int> CandidateIds { get; }
        internal int? MatchCount
        {
            get
            {
                int? identityCount = IdentityResult?.ScreenDetection?.MatchCountValue;
                int? searchCount = SearchResult?.AfterDetection?.MatchCountValue;
                if (identityCount.HasValue && searchCount.HasValue)
                    return Math.Max(identityCount.Value, searchCount.Value);
                return identityCount ?? searchCount;
            }
        }
        internal AchievementScreenDetection StartDetection { get; }
        internal AchievementSearchNavigationResult SearchResult { get; }
        internal AchievementIdentityResult IdentityResult { get; }
        internal AchievementCompletionResult CompletionResult { get; }
        internal AchievementSearchNavigationResult ExitResult { get; }
        internal bool ExitAttempted { get; }
        internal bool ExitSucceeded { get; }
        internal AchievementScreenState? FinalScreenState { get; }
        internal bool PendingClaim { get; }
        internal bool Cancelled => State == AchievementSingleScanState.Cancelled;
        internal bool ReadyForNextAchievement =>
            ExitSucceeded && FinalScreenState == AchievementScreenState.Idle;
        internal string Reason { get; }
        internal string Diagnostics { get; }
        internal AchievementStageProgressResult StageProgressResult { get; }
        internal AchievementTargetTiming Timing => SearchResult?.Timing;
    }

    /// <summary>
    /// Coordinates one canonical achievement through the already-verified Phase 6C, 6D, and 6E
    /// components. Input, OCR, identity, completion semantics, and global input-session ownership
    /// remain with those components; this type only sequences them and preserves their evidence.
    /// </summary>
    internal sealed class AchievementSingleScanner : IAchievementSingleScanner
    {
        internal const int RecognitionRecoveryDelayMs = 150;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly AchievementCatalog catalog;
        private readonly IAchievementScreenDetector screenDetector;
        private readonly IAchievementSearchNavigator searchNavigator;
        private readonly IAchievementResultIdentityVerifier identityVerifier;
        private readonly IAchievementCompletionClassifier completionClassifier;
        private readonly IGameScreenCapture screenCapture;
        private readonly IAchievementRewardReader rewardReader;
        private readonly AchievementStageProgressResolver stageProgressResolver;
        private readonly Action<int, CancellationToken> recoveryWait;
        private bool idleStateVerifiedForNextTarget;

        internal AchievementSingleScanner(
            IAchievementCatalogProvider catalogProvider,
            IAchievementScreenDetector screenDetector,
            IAchievementSearchNavigator searchNavigator,
            IAchievementResultIdentityVerifier identityVerifier,
            IAchievementCompletionClassifier completionClassifier,
            IGameScreenCapture screenCapture,
            IAchievementRewardReader rewardReader,
            AchievementStageProgressResolver stageProgressResolver,
            Action<int, CancellationToken> recoveryWait = null)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.searchNavigator = searchNavigator ?? throw new ArgumentNullException(nameof(searchNavigator));
            this.identityVerifier = identityVerifier ?? throw new ArgumentNullException(nameof(identityVerifier));
            this.completionClassifier = completionClassifier ?? throw new ArgumentNullException(nameof(completionClassifier));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.rewardReader = rewardReader ?? throw new ArgumentNullException(nameof(rewardReader));
            this.stageProgressResolver = stageProgressResolver ?? throw new ArgumentNullException(nameof(stageProgressResolver));
            this.recoveryWait = recoveryWait ?? WaitWithCancellation;
        }

        public void PrepareForScan(CancellationToken cancellationToken)
        {
            searchNavigator.PreparePointer(cancellationToken, forceReposition: true);
            // MainForm calls this only after the passive readiness waiter positively verified
            // Keyboard/Mouse Idle, so the first target can reuse that proof.
            idleStateVerifiedForNextTarget = true;
        }

        public void ReacquireAfterUserInteraction(CancellationToken cancellationToken)
        {
            searchNavigator.PreparePointer(cancellationToken, forceReposition: true);
            // The user may have changed the screen while claiming a reward. The resumed target must
            // take one fresh start-state proof before issuing input.
            idleStateVerifiedForNextTarget = false;
        }

        public AchievementSingleScanResult Scan(
            int canonicalAchievementId,
            CancellationToken cancellationToken)
        {
            AchievementCatalogEntry target = catalog.GetById(canonicalAchievementId);
            var context = new ScanContext(
                canonicalAchievementId,
                target?.Name,
                catalog.GetTitleGroupForId(canonicalAchievementId));

            if (target == null)
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    "The requested canonical achievement ID was not found in the packaged catalog.");
            context.TargetHidden = target.Hidden;

            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled before the start capture.");

            bool reuseVerifiedIdle = idleStateVerifiedForNextTarget;
            idleStateVerifiedForNextTarget = false;
            if (reuseVerifiedIdle)
            {
                context.StartStateReused = true;
                context.FinalScreenState = AchievementScreenState.Idle;
            }
            else try
            {
                using Bitmap startScreenshot = screenCapture.CaptureWindow();
                if (cancellationToken.IsCancellationRequested)
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled before start-state verification.");
                context.StartDetection = screenDetector.Detect(startScreenshot);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled during start-state verification.");
            }
            catch (Exception ex)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    $"Start-state capture or detection failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (!reuseVerifiedIdle)
            {
                context.FinalScreenState = context.StartDetection?.ScreenState;
                if (context.StartDetection?.ScreenState != AchievementScreenState.Idle)
                {
                    return Finish(
                        context,
                        AchievementSingleScanState.Failed,
                        $"Single-achievement scanning requires a positively verified Idle start; observed " +
                        $"{context.StartDetection?.ScreenState.ToString() ?? "no state"}.");
                }
            }

            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled before search.");

            try
            {
                context.SearchResult = searchNavigator.Search(target.Name, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled during search.");
            }
            catch (Exception ex)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    $"Achievement search failed: {ex.GetType().Name}: {ex.Message}");
            }

            context.FinalScreenState = MostRecentScreenState(context.SearchResult) ?? context.FinalScreenState;
            if (context.SearchResult == null)
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    "Achievement search returned no structured result.");
            if (context.SearchResult.Cancelled || cancellationToken.IsCancellationRequested)
            {
                context.SearchResult.ResultScreenshot?.Dispose();
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    context.SearchResult.FailureReason ?? "Single-achievement scanning was cancelled during search.");
            }
            if (!context.SearchResult.Succeeded || !HasUsableResultState(context.SearchResult.AfterDetection))
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    context.SearchResult.FailureReason ??
                    "Achievement search did not return a positively verified result or explicit no-results state.");

            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled after search and before identity verification.");

            if (context.SearchResult.AfterDetection.ScreenState == AchievementScreenState.SearchNoResults)
            {
                context.SearchResult.ResultScreenshot?.Dispose();
                context.ExplicitNoResultsHandled = true;
                AchievementSingleScanState zeroResultState =
                    ClassifyExplicitNoResults(context, target, out string zeroResultReason);
                return FinishWithExit(
                    context,
                    zeroResultState,
                    zeroResultReason,
                    cancellationToken);
            }

            Bitmap resultScreenshot = context.SearchResult.ResultScreenshot;
            if (resultScreenshot == null)
            {
                // Test doubles and older callers may not transfer the navigator-owned frame. The
                // production keyboard/mouse navigator always supplies it, so the live path has one
                // and only one fresh result capture per target.
                try
                {
                    resultScreenshot = screenCapture.CaptureWindow();
                }
                catch (Exception ex)
                {
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.Failed,
                        $"Fresh result capture failed: {ex.GetType().Name}: {ex.Message}",
                        cancellationToken);
                }
            }

            using (resultScreenshot)
            {
                Bitmap recoveryScreenshot = null;
                if (cancellationToken.IsCancellationRequested)
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled after search and before identity verification.");

                try
                {
                    context.IdentityResult = VerifyIdentity(
                        context,
                        target.Id,
                        resultScreenshot,
                        context.SearchResult.AfterDetection);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled during identity verification.");
                }
                catch (Exception ex)
                {
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.Failed,
                        $"Achievement identity verification failed: {ex.GetType().Name}: {ex.Message}",
                        cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled after identity and before completion classification.");

                bool multiMatch = CurrentMatchCount(context) > 1;
                if (context.TitleGroup?.Kind == AchievementTitleGroupKind.UnrelatedCollision)
                {
                    string collisionReason = multiMatch
                        ? "The canonical title is an unrelated collision and the search also returned multiple matches; no individual ID can be selected."
                        : "The canonical title maps to unrelated achievements; visible title evidence cannot select an individual ID.";
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.AmbiguousCollision,
                        collisionReason,
                        cancellationToken);
                }

                if (multiMatch)
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.AmbiguousMultiMatch,
                        "The search returned multiple matches; first-result identity and completion were not inferred.",
                        cancellationToken);

                Bitmap evidenceScreenshot = resultScreenshot;
                AchievementScreenDetection evidenceDetection = context.SearchResult.AfterDetection;
                if (context.IdentityResult?.State == AchievementIdentityState.Unresolved)
                {
                    context.SearchResult.Timing.IdentityRecoveryUsed = true;
                    try
                    {
                        recoveryScreenshot = CaptureRecoveryFrame(context, cancellationToken, out evidenceDetection);
                        evidenceScreenshot = recoveryScreenshot;
                        context.IdentityResult = VerifyIdentity(
                            context,
                            target.Id,
                            evidenceScreenshot,
                            evidenceDetection);
                        context.SearchResult.Timing.RecoveryResult =
                            $"identity:{context.IdentityResult?.State.ToString() ?? "(none)"}";
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        recoveryScreenshot?.Dispose();
                        return Finish(
                            context,
                            AchievementSingleScanState.Cancelled,
                            "Single-achievement scanning was cancelled before identity recovery completed.");
                    }
                    catch (Exception ex)
                    {
                        recoveryScreenshot?.Dispose();
                        return FinishWithExit(
                            context,
                            AchievementSingleScanState.Failed,
                            $"Achievement identity recovery failed: {ex.GetType().Name}: {ex.Message}",
                            cancellationToken);
                    }
                }

                if (!HasPositiveIdentity(context.IdentityResult))
                {
                    recoveryScreenshot?.Dispose();
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.Unresolved,
                        context.IdentityResult?.Reason ?? "Achievement identity was not positively verified.",
                        cancellationToken);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    recoveryScreenshot?.Dispose();
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled after identity recovery and before completion classification.");
                }

                try
                {
                    context.CompletionInvoked = true;
                    context.CompletionResult = ClassifyCompletion(
                        context,
                        evidenceScreenshot,
                        evidenceDetection);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    recoveryScreenshot?.Dispose();
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled during completion classification.");
                }
                catch (Exception ex)
                {
                    recoveryScreenshot?.Dispose();
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.Failed,
                        $"Achievement completion classification failed: {ex.GetType().Name}: {ex.Message}",
                        cancellationToken);
                }

                if (context.CompletionResult?.State == AchievementCompletionState.Unresolved &&
                    recoveryScreenshot == null)
                {
                    context.SearchResult.Timing.CompletionRecoveryUsed = true;
                    try
                    {
                        recoveryScreenshot = CaptureRecoveryFrame(
                            context,
                            cancellationToken,
                            out evidenceDetection);
                        evidenceScreenshot = recoveryScreenshot;
                        context.CompletionResult = ClassifyCompletion(
                            context,
                            evidenceScreenshot,
                            evidenceDetection);
                        context.SearchResult.Timing.RecoveryResult =
                            $"completion:{context.CompletionResult?.State.ToString() ?? "(none)"}";
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        recoveryScreenshot?.Dispose();
                        return Finish(
                            context,
                            AchievementSingleScanState.Cancelled,
                            "Single-achievement scanning was cancelled before completion recovery completed.");
                    }
                    catch (Exception ex)
                    {
                        recoveryScreenshot?.Dispose();
                        return FinishWithExit(
                            context,
                            AchievementSingleScanState.Failed,
                            $"Achievement completion recovery failed: {ex.GetType().Name}: {ex.Message}",
                            cancellationToken);
                    }
                }

                try
                {
                    ResolveStageProgress(context, evidenceScreenshot);
                }
                finally
                {
                    recoveryScreenshot?.Dispose();
                }
            }

            AchievementSingleScanState evidenceState = MapEvidenceState(context);
            string evidenceReason = EvidenceReason(context, evidenceState);
            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    $"Single-achievement scanning was cancelled before search exit. {evidenceReason}");

            return FinishWithExit(context, evidenceState, evidenceReason, cancellationToken);
        }

        private AchievementIdentityResult VerifyIdentity(
            ScanContext context,
            int targetId,
            Bitmap screenshot,
            AchievementScreenDetection detection)
        {
            Stopwatch timer = Stopwatch.StartNew();
            AchievementIdentityResult result = identityVerifier is IAchievementSharedScreenIdentityVerifier shared
                ? shared.Verify(context.SearchResult, targetId, screenshot, detection)
                : identityVerifier.Verify(context.SearchResult, targetId, screenshot);
            timer.Stop();
            context.SearchResult.Timing.IdentityOcrMilliseconds += timer.ElapsedMilliseconds;
            return result;
        }

        private AchievementCompletionResult ClassifyCompletion(
            ScanContext context,
            Bitmap screenshot,
            AchievementScreenDetection detection)
        {
            Stopwatch timer = Stopwatch.StartNew();
            AchievementCompletionResult result =
                completionClassifier is IAchievementSharedScreenCompletionClassifier shared
                    ? shared.Classify(context.IdentityResult, screenshot, detection)
                    : completionClassifier.Classify(context.IdentityResult, screenshot);
            timer.Stop();
            context.SearchResult.Timing.CompletionOcrMilliseconds += timer.ElapsedMilliseconds;
            return result;
        }

        private Bitmap CaptureRecoveryFrame(
            ScanContext context,
            CancellationToken cancellationToken,
            out AchievementScreenDetection detection)
        {
            recoveryWait(RecognitionRecoveryDelayMs, cancellationToken);
            context.SearchResult.Timing.RecoveryDelayMilliseconds += RecognitionRecoveryDelayMs;

            Stopwatch captureTimer = Stopwatch.StartNew();
            Bitmap screenshot = screenCapture.CaptureWindow();
            captureTimer.Stop();
            context.SearchResult.Timing.CaptureMilliseconds += captureTimer.ElapsedMilliseconds;
            context.SearchResult.Timing.ExtraCaptureCount++;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Stopwatch detectionTimer = Stopwatch.StartNew();
                detection = screenDetector.Detect(screenshot);
                detectionTimer.Stop();
                context.SearchResult.Timing.ScreenDetectionMilliseconds += detectionTimer.ElapsedMilliseconds;
                return screenshot;
            }
            catch
            {
                screenshot.Dispose();
                throw;
            }
        }

        private static bool HasPositiveIdentity(AchievementIdentityResult identity) =>
            identity?.State == AchievementIdentityState.Identified ||
            identity?.State == AchievementIdentityState.StageGroupIdentified;

        private static void WaitWithCancellation(int milliseconds, CancellationToken cancellationToken)
        {
            if (milliseconds <= 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            if (cancellationToken.WaitHandle.WaitOne(milliseconds))
                cancellationToken.ThrowIfCancellationRequested();
        }

        private void ResolveStageProgress(ScanContext context, Bitmap resultScreenshot)
        {
            if (context.IdentityResult?.State != AchievementIdentityState.StageGroupIdentified ||
                context.TitleGroup?.Kind != AchievementTitleGroupKind.StageFamily ||
                context.CompletionResult == null)
                return;

            AchievementRewardResult reward = null;
            if (context.CompletionResult.State == AchievementCompletionState.Incomplete ||
                context.CompletionResult.State == AchievementCompletionState.CompletedClaimed)
            {
                try
                {
                    Stopwatch rewardTimer = Stopwatch.StartNew();
                    reward = rewardReader.Read(resultScreenshot) ??
                        AchievementRewardResult.Unresolved(
                            "The reward reader returned no structured result.");
                    rewardTimer.Stop();
                    context.SearchResult.Timing.RewardOcrMilliseconds = rewardTimer.ElapsedMilliseconds;
                }
                catch (Exception ex)
                {
                    reward = AchievementRewardResult.Unresolved(
                        $"Reward OCR failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            context.StageProgressResult = stageProgressResolver.Resolve(
                context.IdentityResult,
                context.CompletionResult,
                reward,
                context.TitleGroup);
        }

        private AchievementSingleScanState ClassifyExplicitNoResults(
            ScanContext context,
            AchievementCatalogEntry target,
            out string reason)
        {
            AchievementTitleGroup group = context.TitleGroup;
            if (group?.Kind == AchievementTitleGroupKind.Unique && target.Hidden)
            {
                context.ZeroResultDecision = "hidden-unique-incomplete";
                reason =
                    "The hidden unique achievement is absent from a positively verified exact-title no-results search.";
                return AchievementSingleScanState.Incomplete;
            }

            if (group?.Kind == AchievementTitleGroupKind.StageFamily &&
                group.Entries.All(entry => entry.Hidden))
            {
                context.StageProgressResult =
                    stageProgressResolver.ResolveExplicitNoResults(group);
                context.ZeroResultDecision = "all-hidden-stage-family-zero-completed";
                reason =
                    "The all-hidden staged title-family is absent from a positively verified exact-title no-results search; stage 1 is incomplete and zero stages are complete.";
                return AchievementSingleScanState.Incomplete;
            }

            context.ZeroResultDecision = group?.Kind == AchievementTitleGroupKind.UnrelatedCollision
                ? "collision-unresolved"
                : target.Hidden
                    ? "mixed-or-unsupported-hidden-group-unresolved"
                    : "non-hidden-catalog-mismatch-unresolved";
            reason = target.Hidden
                ? "The explicit no-results search maps to a collision or mixed/unsupported hidden title group; no progress was inferred."
                : "A non-hidden canonical achievement returned explicit no results; possible catalog, localization, version, or search mismatch remains unresolved.";
            return AchievementSingleScanState.Unresolved;
        }

        private AchievementSingleScanResult FinishWithExit(
            ScanContext context,
            AchievementSingleScanState evidenceState,
            string evidenceReason,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled before search exit.");

            context.ExitAttempted = true;
            try
            {
                context.ExitResult = searchNavigator.ClearSearch(cancellationToken);
                if (context.SearchResult?.Timing != null)
                    context.SearchResult.Timing.ClearMilliseconds =
                        context.ExitResult?.Timing?.ClearMilliseconds ?? 0;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled during search exit.");
            }
            catch (Exception ex)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    $"{evidenceReason} Search exit failed: {ex.GetType().Name}: {ex.Message}");
            }

            context.FinalScreenState = MostRecentScreenState(context.ExitResult) ?? context.FinalScreenState;
            if (context.ExitResult?.Cancelled == true || cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    context.ExitResult?.FailureReason ?? "Single-achievement scanning was cancelled during search exit.");

            context.ExitSucceeded = context.ExitResult?.Succeeded == true &&
                context.FinalScreenState == AchievementScreenState.Idle;
            if (!context.ExitSucceeded)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    $"{evidenceReason} Search exit did not return to positively verified Idle: " +
                    $"{context.ExitResult?.FailureReason ?? "no verified exit result"}");
            }

            idleStateVerifiedForNextTarget = true;
            return Finish(context, evidenceState, evidenceReason);
        }

        private static AchievementSingleScanState MapEvidenceState(ScanContext context)
        {
            if (context.CompletionResult == null)
                return AchievementSingleScanState.Unresolved;

            if (context.IdentityResult.State == AchievementIdentityState.StageGroupIdentified &&
                context.CompletionResult.State == AchievementCompletionState.CompletedUnclaimed)
            {
                context.PendingClaim = true;
                return AchievementSingleScanState.PendingClaimRequiresUserAction;
            }

            return context.CompletionResult.State switch
            {
                AchievementCompletionState.CompletedClaimed => AchievementSingleScanState.Completed,
                AchievementCompletionState.CompletedUnclaimed => AchievementSingleScanState.Completed,
                AchievementCompletionState.Incomplete => AchievementSingleScanState.Incomplete,
                _ => AchievementSingleScanState.Unresolved,
            };
        }

        private static string EvidenceReason(
            ScanContext context,
            AchievementSingleScanState state) => state switch
            {
                AchievementSingleScanState.Completed =>
                    context.IdentityResult.State == AchievementIdentityState.StageGroupIdentified
                        ? StageEvidenceReason(context, "The staged title-family has visible completed-and-claimed evidence.")
                        : "The unique achievement identity and visible completed state were positively verified.",
                AchievementSingleScanState.Incomplete =>
                    context.IdentityResult.State == AchievementIdentityState.StageGroupIdentified
                        ? StageEvidenceReason(context, "The staged title-family has visible incomplete evidence.")
                        : "The unique achievement identity and visible incomplete state were positively verified.",
                AchievementSingleScanState.PendingClaimRequiresUserAction =>
                    "The staged title-family shows Claim; pending rewards prevent safe stage resolution and require user action.",
                _ => context.CompletionResult?.Reason ?? "Completion state was unresolved.",
            };

        private static string StageEvidenceReason(ScanContext context, string prefix) =>
            context.StageProgressResult == null
                ? $"{prefix} Staged progress was not resolved."
                : $"{prefix} {context.StageProgressResult.Reason}";

        private static AchievementScreenState? MostRecentScreenState(
            AchievementSearchNavigationResult navigation) =>
            navigation?.AfterDetection?.ScreenState ?? navigation?.BeforeDetection?.ScreenState;

        private static AchievementSingleScanResult Finish(
            ScanContext context,
            AchievementSingleScanState state,
            string reason)
        {
            if (context.SearchResult?.Timing != null)
                context.SearchResult.Timing.TotalMilliseconds = context.TotalStopwatch.ElapsedMilliseconds;
            string diagnostics = BuildDiagnostics(context, state, reason);
            if (state == AchievementSingleScanState.Completed ||
                state == AchievementSingleScanState.Incomplete ||
                state == AchievementSingleScanState.PendingClaimRequiresUserAction)
                Logger.Info(diagnostics);
            else
                Logger.Warn(diagnostics);

            return new AchievementSingleScanResult(
                state,
                context.RequestedId,
                context.CanonicalTitle,
                context.TitleGroup,
                context.StartDetection,
                context.SearchResult,
                context.IdentityResult,
                context.CompletionResult,
                context.ExitResult,
                context.ExitAttempted,
                context.ExitSucceeded,
                context.FinalScreenState,
                context.PendingClaim,
                reason,
                diagnostics,
                context.StageProgressResult);
        }

        private static string BuildDiagnostics(
            ScanContext context,
            AchievementSingleScanState state,
            string reason)
        {
            var builder = new StringBuilder(2600);
            builder.AppendLine("operation=single-achievement-scan");
            builder.AppendLine($"requestedId={context.RequestedId.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"titleLength={context.CanonicalTitle.Length.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"titleGroupKind={Format(context.TitleGroup?.Kind)}");
            builder.AppendLine($"candidateCount={(context.TitleGroup?.CandidateIds.Count ?? 0).ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"candidateIds={string.Join(",", context.TitleGroup?.CandidateIds ?? Array.Empty<int>())}");
            builder.AppendLine($"matchCount={Format(CurrentMatchCount(context))}");
            builder.AppendLine($"collision={context.TitleGroup?.IsUnrelatedCollision == true}");
            builder.AppendLine($"multiMatch={CurrentMatchCount(context) > 1}");
            builder.AppendLine($"startState={Format(context.StartDetection?.ScreenState)}");
            builder.AppendLine($"startStateReused={context.StartStateReused}");
            builder.AppendLine($"searchSucceeded={context.SearchResult?.Succeeded == true}");
            builder.AppendLine($"searchNoResults={context.SearchResult?.AfterDetection?.ScreenState == AchievementScreenState.SearchNoResults}");
            builder.AppendLine($"targetHidden={context.TargetHidden}");
            builder.AppendLine($"zeroResultHandled={context.ExplicitNoResultsHandled}");
            builder.AppendLine($"zeroResultDecision={context.ZeroResultDecision}");
            builder.AppendLine($"identityState={Format(context.IdentityResult?.State)}");
            builder.AppendLine($"completionState={Format(context.CompletionResult?.State)}");
            builder.AppendLine($"completionInvoked={context.CompletionInvoked}");
            builder.AppendLine($"pendingClaim={context.PendingClaim}");
            builder.AppendLine($"stageProgressState={Format(context.StageProgressResult?.State)}");
            builder.AppendLine($"completedStageIds={string.Join(",", context.StageProgressResult?.CompletedStageIds ?? Array.Empty<int>())}");
            builder.AppendLine($"exitAttempted={context.ExitAttempted}");
            builder.AppendLine($"exitSucceeded={context.ExitSucceeded}");
            builder.AppendLine($"finalState={Format(context.FinalScreenState)}");
            builder.AppendLine($"cancelled={state == AchievementSingleScanState.Cancelled}");
            builder.AppendLine($"state={state}");
            builder.AppendLine($"reason={Bound(reason, 240)}");
            AppendNested(builder, "search", context.SearchResult?.Diagnostics);
            AppendNested(builder, "identity", context.IdentityResult?.Diagnostics);
            AppendNested(builder, "completion", context.CompletionResult?.Diagnostics);
            AppendNested(builder, "stageProgress", context.StageProgressResult?.Diagnostics);
            AppendNested(builder, "exit", context.ExitResult?.Diagnostics);
            if (context.SearchResult?.Timing != null)
                AppendNested(builder, "timing", context.SearchResult.Timing.ToDiagnostics());
            return builder.ToString().TrimEnd();
        }

        private static void AppendNested(StringBuilder builder, string name, string diagnostics)
        {
            if (string.IsNullOrWhiteSpace(diagnostics)) return;
            builder.AppendLine($"{name}Diagnostics={Bound(diagnostics, 360)}");
        }

        private static string Bound(string value, int maximumLength)
        {
            string cleaned = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return cleaned.Length <= maximumLength
                ? cleaned
                : cleaned.Substring(0, maximumLength) + "...";
        }

        private static string Format<T>(T? value) where T : struct =>
            value?.ToString() ?? "(none)";

        private static int? CurrentMatchCount(ScanContext context)
        {
            int? identityCount = context.IdentityResult?.ScreenDetection?.MatchCountValue;
            int? searchCount = context.SearchResult?.AfterDetection?.MatchCountValue;
            if (identityCount.HasValue && searchCount.HasValue)
                return Math.Max(identityCount.Value, searchCount.Value);
            return identityCount ?? searchCount;
        }

        private static bool HasUsableResultState(AchievementScreenDetection detection) =>
            detection?.ScreenState == AchievementScreenState.SearchActive ||
            detection?.ScreenState == AchievementScreenState.SearchNoResults ||
            (detection?.ScreenState == AchievementScreenState.Unknown &&
             detection.MatchCountAccepted && detection.MatchCountValue == 1 &&
             detection.ResultPageAccepted && detection.ResultPageCurrent == 1 &&
             detection.ResultPageTotal == 1);

        private sealed class ScanContext
        {
            internal ScanContext(
                int requestedId,
                string canonicalTitle,
                AchievementTitleGroup titleGroup)
            {
                RequestedId = requestedId;
                CanonicalTitle = canonicalTitle ?? string.Empty;
                TitleGroup = titleGroup;
                TotalStopwatch = Stopwatch.StartNew();
            }

            internal int RequestedId { get; }
            internal string CanonicalTitle { get; }
            internal AchievementTitleGroup TitleGroup { get; }
            internal AchievementScreenDetection StartDetection { get; set; }
            internal bool StartStateReused { get; set; }
            internal AchievementSearchNavigationResult SearchResult { get; set; }
            internal AchievementIdentityResult IdentityResult { get; set; }
            internal AchievementCompletionResult CompletionResult { get; set; }
            internal AchievementStageProgressResult StageProgressResult { get; set; }
            internal AchievementSearchNavigationResult ExitResult { get; set; }
            internal bool ExitAttempted { get; set; }
            internal bool ExitSucceeded { get; set; }
            internal bool PendingClaim { get; set; }
            internal bool CompletionInvoked { get; set; }
            internal bool TargetHidden { get; set; }
            internal bool ExplicitNoResultsHandled { get; set; }
            internal string ZeroResultDecision { get; set; } = string.Empty;
            internal AchievementScreenState? FinalScreenState { get; set; }
            internal Stopwatch TotalStopwatch { get; }
        }
    }
}
