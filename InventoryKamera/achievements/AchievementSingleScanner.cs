using InventoryKamera.game;
using NLog;
using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// Completed and Incomplete describe the verified visible row. Stage-family identity remains
    /// explicit in the nested identity result and never implies an individual canonical stage ID.
    /// </summary>
    internal enum AchievementSingleScanState
    {
        Completed,
        Incomplete,
        Unresolved,
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
            string diagnostics)
        {
            State = state;
            RequestedId = requestedId;
            CanonicalTitle = canonicalTitle ?? string.Empty;
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
        }

        internal AchievementSingleScanState State { get; }
        internal int RequestedId { get; }
        internal string CanonicalTitle { get; }
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
    }

    /// <summary>
    /// Coordinates one canonical achievement through the already-verified Phase 6C, 6D, and 6E
    /// components. Input, OCR, identity, completion semantics, and global input-session ownership
    /// remain with those components; this type only sequences them and preserves their evidence.
    /// </summary>
    internal sealed class AchievementSingleScanner
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly AchievementCatalog catalog;
        private readonly IAchievementScreenDetector screenDetector;
        private readonly IAchievementSearchNavigator searchNavigator;
        private readonly IAchievementResultIdentityVerifier identityVerifier;
        private readonly IAchievementCompletionClassifier completionClassifier;
        private readonly IGameScreenCapture screenCapture;

        internal AchievementSingleScanner(
            IAchievementCatalogProvider catalogProvider,
            IAchievementScreenDetector screenDetector,
            IAchievementSearchNavigator searchNavigator,
            IAchievementResultIdentityVerifier identityVerifier,
            IAchievementCompletionClassifier completionClassifier,
            IGameScreenCapture screenCapture)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            catalog = catalogProvider.Catalog ?? throw new ArgumentException(
                "The catalog provider returned no catalog.", nameof(catalogProvider));
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.searchNavigator = searchNavigator ?? throw new ArgumentNullException(nameof(searchNavigator));
            this.identityVerifier = identityVerifier ?? throw new ArgumentNullException(nameof(identityVerifier));
            this.completionClassifier = completionClassifier ?? throw new ArgumentNullException(nameof(completionClassifier));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
        }

        internal AchievementSingleScanResult Scan(
            int canonicalAchievementId,
            CancellationToken cancellationToken)
        {
            AchievementCatalogEntry target = catalog.GetById(canonicalAchievementId);
            var context = new ScanContext(canonicalAchievementId, target?.Name);

            if (target == null)
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    "The requested canonical achievement ID was not found in the packaged catalog.");

            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled before the start capture.");

            try
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

            context.FinalScreenState = context.StartDetection?.ScreenState;
            if (context.StartDetection?.ScreenState != AchievementScreenState.Idle)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    $"Single-achievement scanning requires a positively verified Idle start; observed " +
                    $"{context.StartDetection?.ScreenState.ToString() ?? "no state"}.");
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
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    context.SearchResult.FailureReason ?? "Single-achievement scanning was cancelled during search.");
            if (!context.SearchResult.Succeeded ||
                context.SearchResult.AfterDetection?.ScreenState != AchievementScreenState.SearchActive)
                return Finish(
                    context,
                    AchievementSingleScanState.Failed,
                    context.SearchResult.FailureReason ??
                    "Achievement search did not return a positively verified SearchActive result.");

            if (cancellationToken.IsCancellationRequested)
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled after search and before identity verification.");

            Bitmap resultScreenshot;
            try
            {
                resultScreenshot = screenCapture.CaptureWindow();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(
                    context,
                    AchievementSingleScanState.Cancelled,
                    "Single-achievement scanning was cancelled during the fresh result capture.");
            }
            catch (Exception ex)
            {
                return FinishWithExit(
                    context,
                    AchievementSingleScanState.Failed,
                    $"Fresh result capture failed: {ex.GetType().Name}: {ex.Message}",
                    cancellationToken);
            }

            using (resultScreenshot)
            {
                if (cancellationToken.IsCancellationRequested)
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled after search and before identity verification.");

                try
                {
                    context.IdentityResult = identityVerifier.Verify(
                        context.SearchResult,
                        target.Id,
                        resultScreenshot);
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

                if (context.IdentityResult == null ||
                    (context.IdentityResult.State != AchievementIdentityState.Identified &&
                     context.IdentityResult.State != AchievementIdentityState.StageGroupIdentified))
                {
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.Unresolved,
                        context.IdentityResult?.Reason ?? "Achievement identity was not positively verified.",
                        cancellationToken);
                }

                try
                {
                    context.CompletionResult = completionClassifier.Classify(
                        context.IdentityResult,
                        resultScreenshot);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return Finish(
                        context,
                        AchievementSingleScanState.Cancelled,
                        "Single-achievement scanning was cancelled during completion classification.");
                }
                catch (Exception ex)
                {
                    return FinishWithExit(
                        context,
                        AchievementSingleScanState.Failed,
                        $"Achievement completion classification failed: {ex.GetType().Name}: {ex.Message}",
                        cancellationToken);
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
                context.ExitResult = searchNavigator.ExitSearch(cancellationToken);
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
                        ? "The staged title-family and visible completed state were preserved without selecting an individual stage."
                        : "The unique achievement identity and visible completed state were positively verified.",
                AchievementSingleScanState.Incomplete =>
                    context.IdentityResult.State == AchievementIdentityState.StageGroupIdentified
                        ? "The staged title-family and visible incomplete state were preserved without inferring completed stages."
                        : "The unique achievement identity and visible incomplete state were positively verified.",
                AchievementSingleScanState.PendingClaimRequiresUserAction =>
                    "The staged title-family shows Claim; pending rewards prevent safe stage resolution and require user action.",
                _ => context.CompletionResult?.Reason ?? "Completion state was unresolved.",
            };

        private static AchievementScreenState? MostRecentScreenState(
            AchievementSearchNavigationResult navigation) =>
            navigation?.AfterDetection?.ScreenState ?? navigation?.BeforeDetection?.ScreenState;

        private static AchievementSingleScanResult Finish(
            ScanContext context,
            AchievementSingleScanState state,
            string reason)
        {
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
                diagnostics);
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
            builder.AppendLine($"startState={Format(context.StartDetection?.ScreenState)}");
            builder.AppendLine($"searchSucceeded={context.SearchResult?.Succeeded == true}");
            builder.AppendLine($"identityState={Format(context.IdentityResult?.State)}");
            builder.AppendLine($"completionState={Format(context.CompletionResult?.State)}");
            builder.AppendLine($"pendingClaim={context.PendingClaim}");
            builder.AppendLine($"exitAttempted={context.ExitAttempted}");
            builder.AppendLine($"exitSucceeded={context.ExitSucceeded}");
            builder.AppendLine($"finalState={Format(context.FinalScreenState)}");
            builder.AppendLine($"cancelled={state == AchievementSingleScanState.Cancelled}");
            builder.AppendLine($"state={state}");
            builder.AppendLine($"reason={Bound(reason, 240)}");
            AppendNested(builder, "search", context.SearchResult?.Diagnostics);
            AppendNested(builder, "identity", context.IdentityResult?.Diagnostics);
            AppendNested(builder, "completion", context.CompletionResult?.Diagnostics);
            AppendNested(builder, "exit", context.ExitResult?.Diagnostics);
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

        private sealed class ScanContext
        {
            internal ScanContext(int requestedId, string canonicalTitle)
            {
                RequestedId = requestedId;
                CanonicalTitle = canonicalTitle ?? string.Empty;
            }

            internal int RequestedId { get; }
            internal string CanonicalTitle { get; }
            internal AchievementScreenDetection StartDetection { get; set; }
            internal AchievementSearchNavigationResult SearchResult { get; set; }
            internal AchievementIdentityResult IdentityResult { get; set; }
            internal AchievementCompletionResult CompletionResult { get; set; }
            internal AchievementSearchNavigationResult ExitResult { get; set; }
            internal bool ExitAttempted { get; set; }
            internal bool ExitSucceeded { get; set; }
            internal bool PendingClaim { get; set; }
            internal AchievementScreenState? FinalScreenState { get; set; }
        }
    }
}
