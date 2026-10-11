using System;
using System.Drawing;
using System.Threading;

namespace InventoryKamera.game
{
    internal enum InventoryTabDirection
    {
        Previous,
        Next,
    }

    internal readonly struct InventoryTabPlan
    {
        public bool Success { get; }
        public InventoryTabDirection Direction { get; }
        public int Steps { get; }
        public string FailureReason { get; }

        private InventoryTabPlan(bool success, InventoryTabDirection direction, int steps, string failureReason)
        {
            Success = success;
            Direction = direction;
            Steps = steps;
            FailureReason = failureReason;
        }

        public static InventoryTabPlan Succeeded(InventoryTabDirection direction, int steps) =>
            new InventoryTabPlan(true, direction, steps, null);

        public static InventoryTabPlan Failed(string reason) =>
            new InventoryTabPlan(false, InventoryTabDirection.Next, 0, reason);
    }

    /// <summary>Plans the shortest bounded route around Genshin's circular Inventory tab row.</summary>
    internal sealed class InventoryTabPathPlanner
    {
        public InventoryTabPlan Plan(int currentIndex, int targetIndex, int tabCount)
        {
            if (tabCount <= 0) return InventoryTabPlan.Failed("No Inventory tabs are configured.");
            if (currentIndex < 0 || currentIndex >= tabCount)
                return InventoryTabPlan.Failed($"Current Inventory tab index {currentIndex} is invalid.");
            if (targetIndex < 0 || targetIndex >= tabCount)
                return InventoryTabPlan.Failed($"Target Inventory tab index {targetIndex} is invalid.");

            int nextSteps = ((targetIndex - currentIndex) % tabCount + tabCount) % tabCount;
            int previousSteps = tabCount - nextSteps;
            return nextSteps <= previousSteps
                ? InventoryTabPlan.Succeeded(InventoryTabDirection.Next, nextSteps)
                : InventoryTabPlan.Succeeded(InventoryTabDirection.Previous, previousSteps);
        }
    }

    internal sealed class InventoryTabNavigationTiming
    {
        public int InitialSettleMs { get; }
        public int ButtonHoldMs { get; }
        public int StepSettleMs { get; }
        public int FinalSettleMs { get; }
        public int DetectionRetryMs { get; }

        public InventoryTabNavigationTiming(
            int initialSettleMs,
            int buttonHoldMs,
            int stepSettleMs,
            int finalSettleMs,
            int detectionRetryMs)
        {
            InitialSettleMs = initialSettleMs;
            ButtonHoldMs = buttonHoldMs;
            StepSettleMs = stepSettleMs;
            FinalSettleMs = finalSettleMs;
            DetectionRetryMs = detectionRetryMs;
        }
    }

    internal enum InventoryTabNavigationStatus
    {
        Succeeded,
        AlreadyAtTarget,
        FailedToDetectCurrent,
        FailedToReachTarget,
        Cancelled,
    }

    internal sealed class InventoryTabNavigationResult : IDisposable
    {
        public InventoryTabNavigationStatus Status { get; }
        public bool Success =>
            Status == InventoryTabNavigationStatus.Succeeded ||
            Status == InventoryTabNavigationStatus.AlreadyAtTarget;
        public string CurrentTab { get; }
        public string TargetTab { get; }
        public string Message { get; }
        public string RawText { get; }
        public int InputCount { get; }
        public Bitmap InitialDiagnosticScreenshot { get; }
        public Bitmap DiagnosticScreenshot { get; }

        private InventoryTabNavigationResult(
            InventoryTabNavigationStatus status,
            string currentTab,
            string targetTab,
            string message,
            string rawText,
            int inputCount,
            Bitmap initialDiagnosticScreenshot,
            Bitmap diagnosticScreenshot)
        {
            Status = status;
            CurrentTab = currentTab;
            TargetTab = targetTab;
            Message = message;
            RawText = rawText ?? string.Empty;
            InputCount = inputCount;
            InitialDiagnosticScreenshot = initialDiagnosticScreenshot;
            DiagnosticScreenshot = diagnosticScreenshot;
        }

        public static InventoryTabNavigationResult Succeeded(
            InventoryTabNavigationStatus status,
            string targetTab,
            string rawText,
            int inputCount) =>
            new InventoryTabNavigationResult(
                status, targetTab, targetTab, null, rawText, inputCount, null, null);

        public static InventoryTabNavigationResult Failed(
            InventoryTabNavigationStatus status,
            string message,
            string currentTab,
            string targetTab,
            string rawText,
            int inputCount,
            Bitmap initialScreenshot,
            Bitmap finalScreenshot) =>
            new InventoryTabNavigationResult(
                status,
                currentTab,
                targetTab,
                message,
                rawText,
                inputCount,
                initialScreenshot,
                finalScreenshot);

        public void Dispose()
        {
            InitialDiagnosticScreenshot?.Dispose();
            if (!ReferenceEquals(DiagnosticScreenshot, InitialDiagnosticScreenshot))
                DiagnosticScreenshot?.Dispose();
        }
    }

    /// <summary>
    /// Navigates between Inventory tabs from visual state. Semantic OCR determines the active tab;
    /// controller shoulder input follows a bounded circular plan; a fresh capture must verify the
    /// requested target before its scan phase may begin.
    /// </summary>
    internal sealed class InventoryTabNavigator
    {
        internal const string CanonicalTabName = "Weapons";
        internal const int DefaultDetectionAttempts = 3;
        internal const int DefaultRecoveryAttempts = 2;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly GameNavigator navigator;
        private readonly IInventoryScreenDetector detector;
        private readonly IGameScreenCapture screenCapture;
        private readonly InventoryTabPathPlanner pathPlanner;
        private readonly Action<int> wait;
        private readonly CancellationToken cancellationToken;
        private readonly int detectionAttempts;
        private readonly int recoveryAttempts;

        public InventoryTabNavigator(
            GameNavigator navigator,
            IInventoryScreenDetector detector,
            IGameScreenCapture screenCapture,
            InventoryTabPathPlanner pathPlanner = null,
            Action<int> wait = null,
            CancellationToken cancellationToken = default,
            int detectionAttempts = DefaultDetectionAttempts,
            int recoveryAttempts = DefaultRecoveryAttempts)
        {
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.pathPlanner = pathPlanner ?? new InventoryTabPathPlanner();
            this.wait = wait;
            this.cancellationToken = cancellationToken;
            this.detectionAttempts = Math.Max(1, detectionAttempts);
            this.recoveryAttempts = Math.Max(0, recoveryAttempts);
        }

        public InventoryTabNavigationResult NormalizeToWeapons(InventoryTabNavigationTiming timing) =>
            NavigateTo(CanonicalTabName, timing);

        public InventoryTabNavigationResult NavigateTo(string targetTab, InventoryTabNavigationTiming timing)
        {
            if (timing == null) throw new ArgumentNullException(nameof(timing));

            int targetIndex = Array.IndexOf(InventoryTabRecognition.Names, targetTab);
            if (targetIndex < 0)
                throw new ArgumentException($"Unknown Inventory tab '{targetTab}'.", nameof(targetTab));

            Bitmap latestScreenshot = null;
            Bitmap initialScreenshot = null;
            InventoryScreenDetection latestDetection = null;
            int inputCount = 0;

            try
            {
                Wait(timing.InitialSettleMs);
                if (!TryDetect(timing, ref latestScreenshot, out latestDetection))
                {
                    if (latestScreenshot != null) initialScreenshot = new Bitmap(latestScreenshot);
                    return Fail(
                        InventoryTabNavigationStatus.FailedToDetectCurrent,
                        $"Inventory is open, but the active tab could not be detected before navigating to {targetTab} " +
                            $"(OCR: \"{latestDetection?.RawText}\").",
                        targetTab,
                        latestDetection,
                        inputCount,
                        Transfer(ref initialScreenshot),
                        Transfer(ref latestScreenshot));
                }

                initialScreenshot = new Bitmap(latestScreenshot);
                if (latestDetection.TabIndex == targetIndex)
                {
                    Logger.Info("Inventory tab transition verified {0} without input (OCR: \"{1}\").",
                        targetTab, latestDetection.RawText);
                    return InventoryTabNavigationResult.Succeeded(
                        InventoryTabNavigationStatus.AlreadyAtTarget,
                        targetTab,
                        latestDetection.RawText,
                        inputCount);
                }

                int maximumRoutes = recoveryAttempts + 1;
                for (int routeAttempt = 1; routeAttempt <= maximumRoutes; routeAttempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    InventoryTabPlan plan = pathPlanner.Plan(
                        latestDetection.TabIndex,
                        targetIndex,
                        InventoryTabRecognition.Names.Length);
                    if (!plan.Success)
                    {
                        return Fail(
                            InventoryTabNavigationStatus.FailedToReachTarget,
                            plan.FailureReason,
                            targetTab,
                            latestDetection,
                            inputCount,
                            Transfer(ref initialScreenshot),
                            Transfer(ref latestScreenshot));
                    }

                    Logger.Info(
                        "Inventory tab transition {0}/{1}: current={2} (OCR: \"{3}\"), target={4}, direction={5}, steps={6}.",
                        routeAttempt,
                        maximumRoutes,
                        latestDetection.TabName,
                        latestDetection.RawText,
                        targetTab,
                        plan.Direction,
                        plan.Steps);

                    for (int step = 0; step < plan.Steps; step++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (plan.Direction == InventoryTabDirection.Next)
                            navigator.TapNextTab(timing.ButtonHoldMs);
                        else
                            navigator.TapPreviousTab(timing.ButtonHoldMs);
                        inputCount++;
                        Wait(timing.StepSettleMs);
                    }

                    Wait(timing.FinalSettleMs);
                    bool detected = TryDetect(timing, ref latestScreenshot, out latestDetection);
                    if (detected && latestDetection.TabIndex == targetIndex)
                    {
                        Logger.Info(
                            "Inventory tab transition verified {0} after {1} shoulder input(s) (OCR: \"{2}\").",
                            targetTab,
                            inputCount,
                            latestDetection.RawText);
                        return InventoryTabNavigationResult.Succeeded(
                            InventoryTabNavigationStatus.Succeeded,
                            targetTab,
                            latestDetection.RawText,
                            inputCount);
                    }

                    if (!detected || routeAttempt == maximumRoutes)
                    {
                        string detectedTab = latestDetection?.TabName ?? "(unknown)";
                        return Fail(
                            InventoryTabNavigationStatus.FailedToReachTarget,
                            $"Inventory tab transition did not reach {targetTab} after {routeAttempt} bounded route attempt(s); " +
                                $"detected {detectedTab} (OCR: \"{latestDetection?.RawText}\").",
                            targetTab,
                            latestDetection,
                            inputCount,
                            Transfer(ref initialScreenshot),
                            Transfer(ref latestScreenshot));
                    }

                    Logger.Warn(
                        "Inventory tab transition reached {0} instead of {1}; replanning from the verified state.",
                        latestDetection.TabName,
                        targetTab);
                }

                throw new InvalidOperationException("Inventory tab transition exhausted without a result.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Cancelled(targetTab, latestDetection, inputCount,
                    Transfer(ref initialScreenshot), Transfer(ref latestScreenshot));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Inventory tab transition to {0} failed unexpectedly.", targetTab);
                return Fail(
                    InventoryTabNavigationStatus.FailedToReachTarget,
                    $"Inventory tab transition to {targetTab} failed: {ex.Message}",
                    targetTab,
                    latestDetection,
                    inputCount,
                    Transfer(ref initialScreenshot),
                    Transfer(ref latestScreenshot));
            }
            finally
            {
                initialScreenshot?.Dispose();
                latestScreenshot?.Dispose();
            }
        }

        private bool TryDetect(
            InventoryTabNavigationTiming timing,
            ref Bitmap latestScreenshot,
            out InventoryScreenDetection detection)
        {
            detection = null;
            for (int attempt = 1; attempt <= detectionAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                latestScreenshot?.Dispose();
                latestScreenshot = screenCapture.CaptureWindow();
                cancellationToken.ThrowIfCancellationRequested();
                detection = detector.Detect(latestScreenshot);
                cancellationToken.ThrowIfCancellationRequested();
                Logger.Debug(
                    "Inventory tab detection attempt {0}/{1}: tab={2}; index={3}; OCR=\"{4}\".",
                    attempt,
                    detectionAttempts,
                    detection.TabName ?? "(unknown)",
                    detection.TabIndex,
                    detection.RawText);
                if (detection.IsInventoryOpen) return true;
                if (attempt < detectionAttempts) Wait(timing.DetectionRetryMs);
            }
            return false;
        }

        private InventoryTabNavigationResult Cancelled(
            string targetTab,
            InventoryScreenDetection detection,
            int inputCount,
            Bitmap initialScreenshot,
            Bitmap finalScreenshot)
        {
            const string message = "Inventory tab transition was cancelled.";
            Logger.Info(message);
            return InventoryTabNavigationResult.Failed(
                InventoryTabNavigationStatus.Cancelled,
                message,
                detection?.TabName,
                targetTab,
                detection?.RawText,
                inputCount,
                initialScreenshot,
                finalScreenshot);
        }

        private InventoryTabNavigationResult Fail(
            InventoryTabNavigationStatus status,
            string message,
            string targetTab,
            InventoryScreenDetection detection,
            int inputCount,
            Bitmap initialScreenshot,
            Bitmap finalScreenshot)
        {
            Logger.Warn(message);
            try
            {
                navigator.MashBack();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not complete the safe back-out after Inventory tab transition failure.");
            }

            return InventoryTabNavigationResult.Failed(
                status,
                message,
                detection?.TabName,
                targetTab,
                detection?.RawText,
                inputCount,
                initialScreenshot,
                finalScreenshot);
        }

        private void Wait(int milliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (milliseconds <= 0) return;

            if (wait != null)
            {
                wait(milliseconds);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }

            if (cancellationToken.WaitHandle.WaitOne(milliseconds))
                cancellationToken.ThrowIfCancellationRequested();
        }

        private static Bitmap Transfer(ref Bitmap bitmap)
        {
            Bitmap transferred = bitmap;
            bitmap = null;
            return transferred;
        }
    }
}
