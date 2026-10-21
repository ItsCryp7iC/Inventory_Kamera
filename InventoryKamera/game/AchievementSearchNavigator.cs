using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;

namespace InventoryKamera.game
{
    internal interface IAchievementSearchNavigator
    {
        void PreparePointer(CancellationToken cancellationToken, bool forceReposition = false) { }
        AchievementSearchNavigationResult Search(string query, CancellationToken cancellationToken);
        AchievementSearchNavigationResult ClearSearch(CancellationToken cancellationToken) =>
            throw new NotSupportedException("This navigator cannot clear Achievement search results.");
    }

    internal interface IAchievementPointerInput
    {
        void MoveNormalized(double x, double y, CancellationToken cancellationToken);
        bool IsAtNormalized(double x, double y);
        void Click(CancellationToken cancellationToken);
    }

    internal sealed class AchievementSearchNavigationTiming
    {
        internal static AchievementSearchNavigationTiming Default { get; } = new(
            focusSettleMs: 100,
            afterTypingSettleMs: 100,
            resultSettleMs: 300,
            clearSettleMs: 250,
            foregroundSettleMs: 150,
            pointerSettleMs: 40,
            searchAttempts: 2,
            clearAttempts: 2);

        internal AchievementSearchNavigationTiming(
            int focusSettleMs,
            int afterTypingSettleMs,
            int resultSettleMs,
            int clearSettleMs,
            int foregroundSettleMs,
            int pointerSettleMs,
            int searchAttempts,
            int clearAttempts)
        {
            FocusSettleMs = NonNegative(focusSettleMs, nameof(focusSettleMs));
            AfterTypingSettleMs = NonNegative(afterTypingSettleMs, nameof(afterTypingSettleMs));
            ResultSettleMs = NonNegative(resultSettleMs, nameof(resultSettleMs));
            ClearSettleMs = NonNegative(clearSettleMs, nameof(clearSettleMs));
            ForegroundSettleMs = NonNegative(foregroundSettleMs, nameof(foregroundSettleMs));
            PointerSettleMs = NonNegative(pointerSettleMs, nameof(pointerSettleMs));
            SearchAttempts = searchAttempts is >= 1 and <= 2
                ? searchAttempts
                : throw new ArgumentOutOfRangeException(nameof(searchAttempts));
            ClearAttempts = clearAttempts is >= 1 and <= 2
                ? clearAttempts
                : throw new ArgumentOutOfRangeException(nameof(clearAttempts));
        }

        internal int FocusSettleMs { get; }
        internal int AfterTypingSettleMs { get; }
        internal int ResultSettleMs { get; }
        internal int ClearSettleMs { get; }
        internal int ForegroundSettleMs { get; }
        internal int PointerSettleMs { get; }
        internal int SearchAttempts { get; }
        internal int ClearAttempts { get; }

        private static int NonNegative(int value, string name) =>
            value >= 0 ? value : throw new ArgumentOutOfRangeException(name);
    }

    internal sealed class AchievementTargetTiming
    {
        internal long FocusMilliseconds { get; set; }
        internal long TypeMilliseconds { get; set; }
        internal long SubmitMilliseconds { get; set; }
        internal long ResultWaitMilliseconds { get; set; }
        internal long CaptureMilliseconds { get; set; }
        internal long ScreenDetectionMilliseconds { get; set; }
        internal long IdentityOcrMilliseconds { get; set; }
        internal long CompletionOcrMilliseconds { get; set; }
        internal long RewardOcrMilliseconds { get; set; }
        internal long ClearMilliseconds { get; set; }
        internal long TotalMilliseconds { get; set; }
        internal bool IdentityRecoveryUsed { get; set; }
        internal bool CompletionRecoveryUsed { get; set; }
        internal int ExtraCaptureCount { get; set; }
        internal int RecoveryDelayMilliseconds { get; set; }
        internal string RecoveryResult { get; set; } = string.Empty;
        internal bool RecognitionRecoveryUsed => IdentityRecoveryUsed || CompletionRecoveryUsed;

        internal string ToDiagnostics()
        {
            var builder = new StringBuilder(320);
            builder.AppendLine($"timing.focusMs={FocusMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.typeMs={TypeMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.submitMs={SubmitMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.resultWaitMs={ResultWaitMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.captureMs={CaptureMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.screenDetectMs={ScreenDetectionMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.identityOcrMs={IdentityOcrMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.completionOcrMs={CompletionOcrMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.rewardOcrMs={RewardOcrMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"timing.clearMs={ClearMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"identityRecoveryUsed={IdentityRecoveryUsed}");
            builder.AppendLine($"completionRecoveryUsed={CompletionRecoveryUsed}");
            builder.AppendLine($"extraCaptureCount={ExtraCaptureCount.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"recoveryDelayMs={RecoveryDelayMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            builder.AppendLine($"recoveryResult={RecoveryResult}");
            builder.Append($"timing.totalMs={TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}");
            return builder.ToString();
        }
    }

    internal sealed class AchievementSearchNavigationResult : IDisposable
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
            string diagnostics,
            Bitmap resultScreenshot = null,
            AchievementTargetTiming timing = null)
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
            ResultScreenshot = resultScreenshot;
            Timing = timing ?? new AchievementTargetTiming();
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
        internal Bitmap ResultScreenshot { get; }
        internal AchievementTargetTiming Timing { get; }

        public void Dispose() => ResultScreenshot?.Dispose();
    }

    /// <summary>
    /// Achievement-only keyboard/mouse navigation. It does not own or reference the legacy
    /// controller stack. The healthy path performs no focus OCR.
    /// </summary>
    internal sealed class AchievementKeyboardMouseNavigator : IAchievementSearchNavigator
    {
        // Akasha's historical 300x90 point on a 1280x720 reference maps into the measured current
        // SearchField region. In Keyboard/Mouse Idle it lies inside the empty text field; with a
        // query active the Clear control occupies the same semantic point.
        internal const double SearchToggleX = 300.0 / 1280.0;
        internal const double SearchToggleY = 90.0 / 720.0;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly IAchievementPointerInput pointerInput;
        private readonly IGameTextInput textInput;
        private readonly IAchievementScreenDetector screenDetector;
        private readonly IGameScreenCapture screenCapture;
        private readonly IGameForegroundActivator foregroundActivator;
        private readonly AchievementSearchNavigationTiming timing;
        private readonly Action<int, CancellationToken> wait;
        private bool pointerPrepared;
        private bool fieldKnownEmpty;

        internal AchievementKeyboardMouseNavigator(
            IAchievementPointerInput pointerInput,
            IGameTextInput textInput,
            IAchievementScreenDetector screenDetector,
            IGameScreenCapture screenCapture,
            IGameForegroundActivator foregroundActivator,
            AchievementSearchNavigationTiming timing = null,
            Action<int, CancellationToken> wait = null)
        {
            this.pointerInput = pointerInput ?? throw new ArgumentNullException(nameof(pointerInput));
            this.textInput = textInput ?? throw new ArgumentNullException(nameof(textInput));
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.foregroundActivator = foregroundActivator ?? throw new ArgumentNullException(nameof(foregroundActivator));
            this.timing = timing ?? AchievementSearchNavigationTiming.Default;
            this.wait = wait ?? WaitWithCancellation;
        }

        public void PreparePointer(
            CancellationToken cancellationToken,
            bool forceReposition = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryEnsureForeground(cancellationToken, out string failure))
                throw new InvalidOperationException(failure);

            bool atTogglePoint = pointerInput.IsAtNormalized(SearchToggleX, SearchToggleY);
            if (forceReposition || !pointerPrepared || !atTogglePoint)
            {
                pointerInput.MoveNormalized(SearchToggleX, SearchToggleY, cancellationToken);
                Wait(timing.PointerSettleMs, cancellationToken);
            }
            pointerPrepared = true;
            fieldKnownEmpty = true;
        }

        public AchievementSearchNavigationResult Search(string query, CancellationToken cancellationToken)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Achievement search text cannot be empty or whitespace-only.", nameof(query));

            var targetTiming = new AchievementTargetTiming();
            var trace = new List<string>();
            Bitmap resultScreenshot = null;
            AchievementScreenDetection resultDetection = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryEnsureForeground(cancellationToken, out string failure))
                    return Finish("search", false, false, failure, null, 0, trace, null, targetTiming);
                EnsurePointerAtTogglePoint(cancellationToken, trace);

                for (int attempt = 1; attempt <= timing.SearchAttempts; attempt++)
                {
                    targetTiming.FocusMilliseconds += Measure(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (attempt > 1)
                        {
                            // Bounded recovery: the same stationary point clears an active query,
                            // then focuses the now-empty editor. No cursor movement is needed.
                            pointerInput.Click(cancellationToken);
                            Wait(timing.ClearSettleMs, cancellationToken);
                        }
                        pointerInput.Click(cancellationToken);
                        Wait(timing.FocusSettleMs, cancellationToken);
                    });
                    targetTiming.TypeMilliseconds += Measure(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (fieldKnownEmpty && attempt == 1)
                            textInput.TypeExact(query, cancellationToken);
                        else
                            textInput.ReplaceAll(query, cancellationToken);
                        fieldKnownEmpty = false;
                        Wait(timing.AfterTypingSettleMs, cancellationToken);
                    });
                    targetTiming.SubmitMilliseconds += Measure(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        textInput.PressEnter(cancellationToken);
                    });
                    targetTiming.ResultWaitMilliseconds += Measure(() => Wait(timing.ResultSettleMs, cancellationToken));
                    targetTiming.CaptureMilliseconds += Measure(() => resultScreenshot = screenCapture.CaptureWindow());
                    cancellationToken.ThrowIfCancellationRequested();
                    targetTiming.ScreenDetectionMilliseconds += Measure(() =>
                        resultDetection = screenDetector.Detect(resultScreenshot));
                    bool verifiedNoResults = HasVerifiedNoResultsForQuery(resultDetection, query);
                    trace.Add(
                        $"searchAttempt={attempt}; result={resultDetection?.ScreenState}; " +
                        $"match={resultDetection?.MatchCountValue?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}; " +
                        $"noResultsForQuery={verifiedNoResults}");

                    bool accepted = resultDetection?.ScreenState == AchievementScreenState.SearchActive ||
                        HasCorroboratedSingleResultMarkers(resultDetection) ||
                        verifiedNoResults;
                    if (accepted)
                        return Finish("search", true, false, null, resultDetection, attempt, trace,
                            resultScreenshot, targetTiming);

                    resultScreenshot.Dispose();
                    resultScreenshot = null;
                }

                string reason = $"Submitted Achievement result was not safely recognized after {timing.SearchAttempts} bounded attempts; observed {resultDetection?.ScreenState.ToString() ?? "no state"}.";
                return Finish("search", false, false, reason, resultDetection,
                    timing.SearchAttempts, trace, null, targetTiming);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                resultScreenshot?.Dispose();
                return Finish("search", false, true, "Achievement search navigation was cancelled.",
                    resultDetection, resultDetection == null ? 0 : 1, trace, null, targetTiming);
            }
            catch (Exception ex)
            {
                resultScreenshot?.Dispose();
                return Finish("search", false, false,
                    $"Achievement search navigation failed: {ex.GetType().Name}: {ex.Message}",
                    resultDetection, resultDetection == null ? 0 : 1, trace, null, targetTiming);
            }
        }

        public AchievementSearchNavigationResult ClearSearch(CancellationToken cancellationToken)
        {
            var targetTiming = new AchievementTargetTiming();
            var trace = new List<string>();
            AchievementScreenDetection after = null;
            Stopwatch total = Stopwatch.StartNew();
            try
            {
                if (!TryEnsureForeground(cancellationToken, out string failure))
                    return Finish("clear", false, false, failure, null, 0, trace, null, targetTiming);
                EnsurePointerAtTogglePoint(cancellationToken, trace);

                for (int attempt = 1; attempt <= timing.ClearAttempts; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pointerInput.Click(cancellationToken);
                    Wait(timing.ClearSettleMs, cancellationToken);
                    using Bitmap screenshot = screenCapture.CaptureWindow();
                    after = screenDetector.Detect(screenshot);
                    trace.Add($"clearAttempt={attempt}; state={after.ScreenState}");
                    if (after.IsKeyboardMouseIdle)
                    {
                        fieldKnownEmpty = true;
                        total.Stop();
                        targetTiming.ClearMilliseconds = total.ElapsedMilliseconds;
                        return Finish("clear", true, false, null, after, attempt, trace, null, targetTiming);
                    }
                }

                total.Stop();
                targetTiming.ClearMilliseconds = total.ElapsedMilliseconds;
                return Finish("clear", false, false,
                    "The visible Clear control did not return the Achievement screen to verified Idle after two bounded clicks.",
                    after, timing.ClearAttempts, trace, null, targetTiming);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                total.Stop();
                targetTiming.ClearMilliseconds = total.ElapsedMilliseconds;
                return Finish("clear", false, true, "Achievement search clearing was cancelled.",
                    after, trace.Count, trace, null, targetTiming);
            }
            catch (Exception ex)
            {
                total.Stop();
                targetTiming.ClearMilliseconds = total.ElapsedMilliseconds;
                return Finish("clear", false, false,
                    $"Achievement search clearing failed: {ex.GetType().Name}: {ex.Message}",
                    after, trace.Count, trace, null, targetTiming);
            }
        }

        private bool TryEnsureForeground(CancellationToken cancellationToken, out string failureReason)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!foregroundActivator.IsForeground)
            {
                foregroundActivator.Activate();
                Wait(timing.ForegroundSettleMs, cancellationToken);
            }
            if (!foregroundActivator.IsForeground)
            {
                failureReason = "Genshin foreground could not be positively established; no input was sent.";
                return false;
            }
            failureReason = null;
            return true;
        }

        private void EnsurePointerAtTogglePoint(
            CancellationToken cancellationToken,
            ICollection<string> trace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pointerPrepared && pointerInput.IsAtNormalized(SearchToggleX, SearchToggleY)) return;

            bool firstPreparation = !pointerPrepared;
            pointerInput.MoveNormalized(SearchToggleX, SearchToggleY, cancellationToken);
            Wait(timing.PointerSettleMs, cancellationToken);
            pointerPrepared = true;
            if (firstPreparation) fieldKnownEmpty = true;
            trace?.Add("pointer=repositioned-to-search-toggle");
        }

        private static bool HasCorroboratedSingleResultMarkers(AchievementScreenDetection detection) =>
            detection?.ScreenState == AchievementScreenState.Unknown &&
            detection.MatchCountAccepted && detection.MatchCountValue == 1 &&
            detection.ResultPageAccepted && detection.ResultPageCurrent == 1 && detection.ResultPageTotal == 1;

        private static bool HasVerifiedNoResultsForQuery(
            AchievementScreenDetection detection,
            string query)
        {
            if (detection?.ScreenState != AchievementScreenState.SearchNoResults ||
                !detection.SearchField.ActiveControlsAccepted ||
                !detection.NoResultsEvidence.Accepted ||
                (detection.MatchCountAccepted && detection.MatchCountValue.GetValueOrDefault() > 0))
                return false;

            string normalizedQuery = AchievementScreenDetector.Normalize(query);
            if (normalizedQuery.Length == 0) return false;

            return ContainsNormalizedPhrase(
                    detection.SearchField.Text.NormalizedText,
                    normalizedQuery) ||
                ContainsNormalizedPhrase(
                    detection.NoResultsEvidence.NormalizedText,
                    normalizedQuery);
        }

        private static bool ContainsNormalizedPhrase(string text, string phrase) =>
            (" " + (text ?? string.Empty) + " ").Contains(
                " " + phrase + " ",
                StringComparison.Ordinal);

        private AchievementSearchNavigationResult Finish(
            string operation,
            bool succeeded,
            bool cancelled,
            string reason,
            AchievementScreenDetection after,
            int attempts,
            IReadOnlyList<string> trace,
            Bitmap screenshot,
            AchievementTargetTiming targetTiming)
        {
            var diagnostics = new StringBuilder(640);
            diagnostics.AppendLine($"operation={operation}");
            diagnostics.AppendLine("inputBackend=keyboard-mouse-sendinput");
            diagnostics.AppendLine($"attempt={attempts.ToString(CultureInfo.InvariantCulture)}");
            diagnostics.AppendLine($"afterState={after?.ScreenState.ToString() ?? "(none)"}");
            diagnostics.AppendLine($"cancelled={cancelled}");
            diagnostics.AppendLine($"reason={reason ?? "success"}");
            for (int i = 0; i < trace.Count; i++) diagnostics.AppendLine($"trace[{i}]={trace[i]}");
            diagnostics.Append(targetTiming.ToDiagnostics());
            string value = diagnostics.ToString().TrimEnd();
            if (succeeded) Logger.Info(value); else Logger.Warn(value);
            return new AchievementSearchNavigationResult(
                operation, succeeded, cancelled, reason,
                null, null, null, null, after, attempts, value, screenshot, targetTiming);
        }

        private static long Measure(Action action)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
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
    }
}
