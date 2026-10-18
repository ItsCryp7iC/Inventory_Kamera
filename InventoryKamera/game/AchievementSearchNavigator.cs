using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;

namespace InventoryKamera.game
{
    internal sealed class AchievementSearchNavigationTiming
    {
        internal static AchievementSearchNavigationTiming Default { get; } = new(
            searchHoldMs: 90,
            backHoldMs: 150,
            foregroundSettleMs: 300,
            openSettleMs: 500,
            afterTypingSettleMs: 200,
            submitSettleMs: 700,
            exitSettleMs: 500,
            retrySettleMs: 250,
            focusAttempts: 3,
            resultAttempts: 3,
            exitAttempts: 3);

        internal AchievementSearchNavigationTiming(
            int searchHoldMs,
            int backHoldMs,
            int foregroundSettleMs,
            int openSettleMs,
            int afterTypingSettleMs,
            int submitSettleMs,
            int exitSettleMs,
            int retrySettleMs,
            int focusAttempts,
            int resultAttempts,
            int exitAttempts)
        {
            SearchHoldMs = NonNegative(searchHoldMs, nameof(searchHoldMs));
            BackHoldMs = NonNegative(backHoldMs, nameof(backHoldMs));
            ForegroundSettleMs = NonNegative(foregroundSettleMs, nameof(foregroundSettleMs));
            OpenSettleMs = NonNegative(openSettleMs, nameof(openSettleMs));
            AfterTypingSettleMs = NonNegative(afterTypingSettleMs, nameof(afterTypingSettleMs));
            SubmitSettleMs = NonNegative(submitSettleMs, nameof(submitSettleMs));
            ExitSettleMs = NonNegative(exitSettleMs, nameof(exitSettleMs));
            RetrySettleMs = NonNegative(retrySettleMs, nameof(retrySettleMs));
            FocusAttempts = Positive(focusAttempts, nameof(focusAttempts));
            ResultAttempts = Positive(resultAttempts, nameof(resultAttempts));
            ExitAttempts = Positive(exitAttempts, nameof(exitAttempts));
        }

        internal int SearchHoldMs { get; }
        internal int BackHoldMs { get; }
        internal int ForegroundSettleMs { get; }
        internal int OpenSettleMs { get; }
        internal int AfterTypingSettleMs { get; }
        internal int SubmitSettleMs { get; }
        internal int ExitSettleMs { get; }
        internal int RetrySettleMs { get; }
        internal int FocusAttempts { get; }
        internal int ResultAttempts { get; }
        internal int ExitAttempts { get; }

        private static int NonNegative(int value, string name) =>
            value >= 0 ? value : throw new ArgumentOutOfRangeException(name);

        private static int Positive(int value, string name) =>
            value > 0 ? value : throw new ArgumentOutOfRangeException(name);
    }

    internal sealed class AchievementSearchNavigationResult
    {
        internal AchievementSearchNavigationResult(
            string operation,
            bool succeeded,
            bool cancelled,
            string failureReason,
            AchievementScreenDetection beforeDetection,
            AchievementScreenDetection afterOpenScreenDetection,
            AchievementSearchEditorDetection afterOpenFocusDetection,
            AchievementSearchEditorDetection afterTypingFocusDetection,
            AchievementScreenDetection afterDetection,
            int attempts,
            string diagnostics)
        {
            Operation = operation ?? string.Empty;
            Succeeded = succeeded;
            Cancelled = cancelled;
            FailureReason = failureReason;
            BeforeDetection = beforeDetection;
            AfterOpenScreenDetection = afterOpenScreenDetection;
            AfterOpenFocusDetection = afterOpenFocusDetection;
            AfterTypingFocusDetection = afterTypingFocusDetection;
            AfterDetection = afterDetection;
            Attempts = attempts;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal string Operation { get; }
        internal bool Succeeded { get; }
        internal bool Cancelled { get; }
        internal string FailureReason { get; }
        internal AchievementScreenDetection BeforeDetection { get; }
        internal AchievementScreenDetection AfterOpenScreenDetection { get; }
        internal AchievementSearchEditorDetection AfterOpenFocusDetection { get; }
        internal AchievementSearchEditorDetection AfterTypingFocusDetection { get; }
        internal AchievementScreenDetection AfterDetection { get; }
        internal int Attempts { get; }
        internal string Diagnostics { get; }
    }

    /// <summary>
    /// Performs only the Phase 6C search-control transitions. It proves screen state before each
    /// semantic action and never interprets result identity or completion state. The current live
    /// zero-results layout remains Unknown, so it produces a bounded unverified-result failure;
    /// a future detector can enable that layout without changing this navigator.
    /// </summary>
    internal sealed class AchievementSearchNavigator
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly GameNavigator navigator;
        private readonly IGameTextInput textInput;
        private readonly IAchievementScreenDetector screenDetector;
        private readonly IAchievementSearchEditorDetector editorDetector;
        private readonly IGameScreenCapture screenCapture;
        private readonly IGameForegroundActivator foregroundActivator;
        private readonly AchievementSearchNavigationTiming timing;
        private readonly Action<int, CancellationToken> wait;

        internal AchievementSearchNavigator(
            GameNavigator navigator,
            IGameTextInput textInput,
            IAchievementScreenDetector screenDetector,
            IAchievementSearchEditorDetector editorDetector,
            IGameScreenCapture screenCapture,
            IGameForegroundActivator foregroundActivator,
            AchievementSearchNavigationTiming timing = null,
            Action<int, CancellationToken> wait = null)
        {
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            this.textInput = textInput ?? throw new ArgumentNullException(nameof(textInput));
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.editorDetector = editorDetector ?? throw new ArgumentNullException(nameof(editorDetector));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.foregroundActivator = foregroundActivator ?? throw new ArgumentNullException(nameof(foregroundActivator));
            this.timing = timing ?? AchievementSearchNavigationTiming.Default;
            this.wait = wait ?? WaitWithCancellation;
        }

        internal AchievementSearchNavigationResult Search(
            string query,
            CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Achievement search text cannot be empty or whitespace-only.", nameof(query));

            const string operation = "search";
            var trace = new List<string>();
            AchievementScreenDetection before = null;
            AchievementScreenDetection afterOpenScreen = null;
            AchievementSearchEditorDetection afterOpenFocus = null;
            AchievementSearchEditorDetection afterTypingFocus = null;
            AchievementScreenDetection after = null;
            int attempts = 0;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                before = CaptureDetection(cancellationToken);
                if (before.ScreenState != AchievementScreenState.Idle)
                {
                    return Finish(
                        operation, query.Length, false, false,
                        $"Achievement search requires a positively verified Idle start; observed {before.ScreenState}.",
                        before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                }

                if (!TryEnsureForeground(cancellationToken, out string foregroundFailure))
                {
                    return Finish(
                        operation, query.Length, false, false, foregroundFailure,
                        before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                }

                cancellationToken.ThrowIfCancellationRequested();
                navigator.TapSearch(timing.SearchHoldMs);
                Wait(timing.OpenSettleMs, cancellationToken);

                for (int attempt = 1; attempt <= timing.FocusAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!TryEnsureForeground(cancellationToken, out foregroundFailure))
                    {
                        return Finish(
                            operation, query.Length, false, false, foregroundFailure,
                            before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                    }
                    attempts++;
                    using Bitmap screenshot = screenCapture.CaptureWindow();
                    cancellationToken.ThrowIfCancellationRequested();
                    afterOpenScreen = screenDetector.Detect(screenshot);
                    afterOpenFocus = editorDetector.Detect(screenshot, afterOpenScreen);
                    trace.Add(DescribeAttempt("open", attempt, afterOpenScreen, afterOpenFocus));
                    if (afterOpenFocus.State == AchievementSearchEditorState.Focused) break;
                    if (attempt < timing.FocusAttempts) Wait(timing.RetrySettleMs, cancellationToken);
                }

                if (afterOpenFocus?.State != AchievementSearchEditorState.Focused)
                {
                    return Finish(
                        operation, query.Length, false, false,
                        "Achievement search editor focus could not be positively verified; no text was entered.",
                        before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                }

                if (!foregroundActivator.IsForeground)
                {
                    return Finish(
                        operation, query.Length, false, false,
                        "Genshin lost foreground after editor-focus verification; no text was entered.",
                        before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                }

                cancellationToken.ThrowIfCancellationRequested();
                textInput.ReplaceAll(query, cancellationToken);
                Wait(timing.AfterTypingSettleMs, cancellationToken);

                if (!TryEnsureForeground(cancellationToken, out foregroundFailure))
                {
                    return Finish(
                        operation, query.Length, false, false, foregroundFailure,
                        before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                }

                attempts++;
                using (Bitmap screenshot = screenCapture.CaptureWindow())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AchievementScreenDetection typedScreen = screenDetector.Detect(screenshot);
                    afterTypingFocus = editorDetector.Detect(screenshot, typedScreen);
                    trace.Add(DescribeAttempt("typed", 1, typedScreen, afterTypingFocus));
                    afterOpenScreen = typedScreen;
                }

                if (afterTypingFocus.State != AchievementSearchEditorState.Focused)
                {
                    return Finish(
                        operation, query.Length, false, false,
                        "The editor was no longer positively focused after text entry; search was not submitted.",
                        before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                }

                if (!foregroundActivator.IsForeground)
                {
                    return Finish(
                        operation, query.Length, false, false,
                        "Genshin lost foreground after typed-editor verification; search was not submitted.",
                        before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                }

                cancellationToken.ThrowIfCancellationRequested();
                navigator.TapSearch(timing.SearchHoldMs);
                Wait(timing.SubmitSettleMs, cancellationToken);

                for (int attempt = 1; attempt <= timing.ResultAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    attempts++;
                    after = CaptureDetection(cancellationToken);
                    trace.Add(DescribeAttempt("submit", attempt, after, null));
                    if (after.ScreenState == AchievementScreenState.SearchActive)
                    {
                        return Finish(
                            operation, query.Length, true, false, null,
                            before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
                    }
                    if (attempt < timing.ResultAttempts) Wait(timing.RetrySettleMs, cancellationToken);
                }

                return Finish(
                    operation, query.Length, false, false,
                    $"Submitted Achievement results could not be positively verified after " +
                    $"{timing.ResultAttempts.ToString(CultureInfo.InvariantCulture)} bounded capture attempts; " +
                    $"observed {after?.ScreenState.ToString() ?? "no state"}.",
                    before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(
                    operation, query.Length, false, true, "Achievement search navigation was cancelled.",
                    before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
            }
            catch (Exception ex)
            {
                return Finish(
                    operation, query.Length, false, false,
                    $"Achievement search navigation failed: {ex.GetType().Name}: {ex.Message}",
                    before, afterOpenScreen, afterOpenFocus, afterTypingFocus, after, attempts, trace);
            }
        }

        internal AchievementSearchNavigationResult ExitSearch(CancellationToken cancellationToken)
        {
            const string operation = "exit";
            var trace = new List<string>();
            AchievementScreenDetection before = null;
            AchievementScreenDetection after = null;
            int attempts = 0;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                before = CaptureDetection(cancellationToken);
                if (before.ScreenState != AchievementScreenState.SearchActive)
                {
                    return Finish(
                        operation, 0, false, false,
                        $"Exit Search requires a positively verified SearchActive start; observed {before.ScreenState}.",
                        before, null, null, null, after, attempts, trace);
                }

                if (!TryEnsureForeground(cancellationToken, out string foregroundFailure))
                {
                    return Finish(
                        operation, 0, false, false, foregroundFailure,
                        before, null, null, null, after, attempts, trace);
                }

                cancellationToken.ThrowIfCancellationRequested();
                navigator.TapBack(timing.BackHoldMs);
                Wait(timing.ExitSettleMs, cancellationToken);

                for (int attempt = 1; attempt <= timing.ExitAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    attempts++;
                    after = CaptureDetection(cancellationToken);
                    trace.Add(DescribeAttempt("exit", attempt, after, null));
                    if (after.ScreenState == AchievementScreenState.Idle)
                    {
                        return Finish(
                            operation, 0, true, false, null,
                            before, null, null, null, after, attempts, trace);
                    }
                    if (attempt < timing.ExitAttempts) Wait(timing.RetrySettleMs, cancellationToken);
                }

                return Finish(
                    operation, 0, false, false,
                    $"Exit Search did not return to verified Idle within {timing.ExitAttempts} capture attempts.",
                    before, null, null, null, after, attempts, trace);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Finish(
                    operation, 0, false, true, "Achievement search exit was cancelled.",
                    before, null, null, null, after, attempts, trace);
            }
            catch (Exception ex)
            {
                return Finish(
                    operation, 0, false, false,
                    $"Achievement search exit failed: {ex.GetType().Name}: {ex.Message}",
                    before, null, null, null, after, attempts, trace);
            }
        }

        private AchievementScreenDetection CaptureDetection(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using Bitmap screenshot = screenCapture.CaptureWindow();
            cancellationToken.ThrowIfCancellationRequested();
            return screenDetector.Detect(screenshot);
        }

        private bool TryEnsureForeground(CancellationToken cancellationToken, out string failureReason)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!foregroundActivator.IsForeground)
                {
                    foregroundActivator.Activate();
                    Wait(timing.ForegroundSettleMs, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!foregroundActivator.IsForeground)
                {
                    failureReason = "Genshin foreground could not be positively established; no input was sent.";
                    return false;
                }

                failureReason = null;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failureReason = $"Genshin foreground could not be established: {ex.Message}; no input was sent.";
                return false;
            }
        }

        private void Wait(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            wait(milliseconds, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

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

        private AchievementSearchNavigationResult Finish(
            string operation,
            int queryLength,
            bool succeeded,
            bool cancelled,
            string failureReason,
            AchievementScreenDetection before,
            AchievementScreenDetection afterOpenScreen,
            AchievementSearchEditorDetection afterOpenFocus,
            AchievementSearchEditorDetection afterTypingFocus,
            AchievementScreenDetection after,
            int attempts,
            IReadOnlyList<string> trace)
        {
            string diagnostics = BuildDiagnostics(
                operation,
                queryLength,
                attempts,
                before,
                after,
                afterTypingFocus ?? afterOpenFocus,
                cancelled,
                failureReason ?? "success",
                trace);
            if (succeeded) Logger.Info(diagnostics); else Logger.Warn(diagnostics);
            return new AchievementSearchNavigationResult(
                operation,
                succeeded,
                cancelled,
                failureReason,
                before,
                afterOpenScreen,
                afterOpenFocus,
                afterTypingFocus,
                after,
                attempts,
                diagnostics);
        }

        private static string BuildDiagnostics(
            string operation,
            int queryLength,
            int attempts,
            AchievementScreenDetection before,
            AchievementScreenDetection after,
            AchievementSearchEditorDetection focus,
            bool cancelled,
            string reason,
            IReadOnlyList<string> trace)
        {
            var builder = new StringBuilder(1024);
            builder.AppendLine($"operation={operation}");
            builder.AppendLine($"queryLength={queryLength.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"attempt={attempts.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"beforeState={FormatState(before)}");
            builder.AppendLine($"afterState={FormatState(after)}");
            builder.AppendLine($"matchCount={FormatNumber(after?.MatchCountValue)}");
            builder.AppendLine($"page={FormatPage(after)}");
            builder.AppendLine($"focusEvidence={Bound(focus?.Evidence ?? "(none)")}");
            builder.AppendLine($"cancelled={cancelled}");
            builder.AppendLine($"reason={Bound(reason)}");
            for (int i = 0; i < trace.Count; i++)
                builder.AppendLine($"trace[{i.ToString(CultureInfo.InvariantCulture)}]={Bound(trace[i])}");
            return builder.ToString().TrimEnd();
        }

        private static string DescribeAttempt(
            string transition,
            int attempt,
            AchievementScreenDetection screen,
            AchievementSearchEditorDetection focus) =>
            $"transition={transition}; attempt={attempt.ToString(CultureInfo.InvariantCulture)}; " +
            $"screen={FormatState(screen)}; match={FormatNumber(screen?.MatchCountValue)}; " +
            $"page={FormatPage(screen)}; focus={focus?.Evidence ?? "(not evaluated)"}";

        private static string FormatState(AchievementScreenDetection detection) =>
            detection?.ScreenState.ToString() ?? "(none)";

        private static string FormatNumber(int? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? "(none)";

        private static string FormatPage(AchievementScreenDetection detection) => detection == null
            ? "(none)"
            : $"{FormatNumber(detection.ResultPageCurrent)}/{FormatNumber(detection.ResultPageTotal)}";

        private static string Bound(string value)
        {
            string singleLine = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return singleLine.Length <= 300 ? singleLine : singleLine.Substring(0, 300) + "...";
        }
    }
}
