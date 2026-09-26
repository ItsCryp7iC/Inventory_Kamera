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

        private InventoryTabPlan(
            bool success,
            InventoryTabDirection direction,
            int steps,
            string failureReason)
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

    internal sealed class InventoryTabNavigationResult : IDisposable
    {
        public bool Success { get; }
        public string CurrentTab { get; }
        public string Message { get; }
        public string RawText { get; }
        public Bitmap DiagnosticScreenshot { get; }

        private InventoryTabNavigationResult(
            bool success,
            string currentTab,
            string message,
            string rawText,
            Bitmap diagnosticScreenshot)
        {
            Success = success;
            CurrentTab = currentTab;
            Message = message;
            RawText = rawText ?? string.Empty;
            DiagnosticScreenshot = diagnosticScreenshot;
        }

        public static InventoryTabNavigationResult Succeeded(string currentTab, string rawText) =>
            new InventoryTabNavigationResult(true, currentTab, null, rawText, null);

        public static InventoryTabNavigationResult Failed(
            string message,
            string currentTab,
            string rawText,
            Bitmap screenshot) =>
            new InventoryTabNavigationResult(false, currentTab, message, rawText, screenshot);

        public void Dispose() => DiagnosticScreenshot?.Dispose();
    }

    /// <summary>
    /// Normalizes a verified-open Inventory screen to the canonical Weapons tab. Semantic OCR
    /// determines the active tab; controller shoulder input follows a bounded circular plan; a fresh
    /// capture must verify Weapons before scanning can begin.
    /// </summary>
    internal sealed class InventoryTabNavigator
    {
        internal const string CanonicalTabName = "Weapons";
        internal const int DefaultDetectionAttempts = 3;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly GameNavigator navigator;
        private readonly IInventoryScreenDetector detector;
        private readonly IGameScreenCapture screenCapture;
        private readonly InventoryTabPathPlanner pathPlanner;
        private readonly Action<int> wait;
        private readonly Func<bool> cancellationRequested;
        private readonly int detectionAttempts;

        public InventoryTabNavigator(
            GameNavigator navigator,
            IInventoryScreenDetector detector,
            IGameScreenCapture screenCapture,
            InventoryTabPathPlanner pathPlanner = null,
            Action<int> wait = null,
            Func<bool> cancellationRequested = null,
            int detectionAttempts = DefaultDetectionAttempts)
        {
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.pathPlanner = pathPlanner ?? new InventoryTabPathPlanner();
            this.wait = wait ?? Thread.Sleep;
            this.cancellationRequested = cancellationRequested ?? (() => false);
            this.detectionAttempts = Math.Max(1, detectionAttempts);
        }

        public InventoryTabNavigationResult NormalizeToWeapons(InventoryTabNavigationTiming timing)
        {
            if (timing == null) throw new ArgumentNullException(nameof(timing));

            Bitmap latestScreenshot = null;
            InventoryScreenDetection latestDetection = null;
            try
            {
                wait(timing.InitialSettleMs);
                if (!TryDetect(timing, ref latestScreenshot, out latestDetection))
                {
                    return Fail(
                        $"Inventory opened, but the active tab could not be detected (OCR: \"{latestDetection?.RawText}\").",
                        latestDetection,
                        Transfer(ref latestScreenshot));
                }

                int targetIndex = Array.IndexOf(InventoryTabRecognition.Names, CanonicalTabName);
                InventoryTabPlan plan = pathPlanner.Plan(
                    latestDetection.TabIndex,
                    targetIndex,
                    InventoryTabRecognition.Names.Length);
                if (!plan.Success)
                    return Fail(plan.FailureReason, latestDetection, Transfer(ref latestScreenshot));

                Logger.Info(
                    "Inventory tab normalization: current={0} (OCR: \"{1}\"), target={2}, direction={3}, steps={4}.",
                    latestDetection.TabName,
                    latestDetection.RawText,
                    CanonicalTabName,
                    plan.Direction,
                    plan.Steps);

                if (plan.Steps == 0)
                {
                    latestScreenshot.Dispose();
                    latestScreenshot = null;
                    return InventoryTabNavigationResult.Succeeded(
                        CanonicalTabName,
                        latestDetection.RawText);
                }

                for (int step = 0; step < plan.Steps; step++)
                {
                    if (cancellationRequested())
                        return Fail(
                            "Inventory tab normalization was cancelled.",
                            latestDetection,
                            Transfer(ref latestScreenshot));

                    if (plan.Direction == InventoryTabDirection.Next)
                        navigator.TapNextTab(timing.ButtonHoldMs);
                    else
                        navigator.TapPreviousTab(timing.ButtonHoldMs);
                    wait(timing.StepSettleMs);
                }

                wait(timing.FinalSettleMs);
                if (!TryDetect(timing, ref latestScreenshot, out latestDetection) ||
                    latestDetection.TabIndex != targetIndex)
                {
                    return Fail(
                        $"Inventory tab normalization did not reach {CanonicalTabName}; " +
                            $"detected {latestDetection?.TabName ?? "(unknown)"} " +
                            $"(OCR: \"{latestDetection?.RawText}\").",
                        latestDetection,
                        Transfer(ref latestScreenshot));
                }

                Logger.Info("Inventory tab normalization verified {0} (OCR: \"{1}\").",
                    latestDetection.TabName, latestDetection.RawText);
                latestScreenshot.Dispose();
                latestScreenshot = null;
                return InventoryTabNavigationResult.Succeeded(
                    CanonicalTabName,
                    latestDetection.RawText);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Inventory tab normalization failed unexpectedly.");
                return Fail(
                    $"Inventory tab normalization failed: {ex.Message}",
                    latestDetection,
                    Transfer(ref latestScreenshot));
            }
            finally
            {
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
                if (cancellationRequested()) return false;
                latestScreenshot?.Dispose();
                latestScreenshot = screenCapture.CaptureWindow();
                detection = detector.Detect(latestScreenshot);
                Logger.Debug(
                    "Inventory tab detection attempt {0}/{1}: tab={2}; index={3}; OCR=\"{4}\".",
                    attempt,
                    detectionAttempts,
                    detection.TabName ?? "(unknown)",
                    detection.TabIndex,
                    detection.RawText);
                if (detection.IsInventoryOpen) return true;
                if (attempt < detectionAttempts) wait(timing.DetectionRetryMs);
            }
            return false;
        }

        private InventoryTabNavigationResult Fail(
            string message,
            InventoryScreenDetection detection,
            Bitmap diagnosticScreenshot)
        {
            Logger.Warn(message);
            try
            {
                navigator.MashBack();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not complete the safe back-out after Inventory tab normalization failure.");
            }

            return InventoryTabNavigationResult.Failed(
                message,
                detection?.TabName,
                detection?.RawText,
                diagnosticScreenshot);
        }

        private static Bitmap Transfer(ref Bitmap bitmap)
        {
            Bitmap transferred = bitmap;
            bitmap = null;
            return transferred;
        }
    }
}
