using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace InventoryKamera.game
{
    internal sealed class PaimonMenuNavigationTiming
    {
        // Paimon menu focus must advance exactly once before the next capture. The 80ms hold is the
        // shortest single-step controller timing already established elsewhere in the scanner; the
        // existing 300ms settle is retained so the selection animation completes before detection.
        internal const int SingleStepHoldMs = 80;
        internal const int SingleStepSettleMs = 300;

        public int ControllerModeSettleMs { get; }
        public int MenuOpenSettleMs { get; }
        public int MoveHoldMs { get; }
        public int MoveSettleMs { get; }
        public int PreConfirmSettleMs { get; }
        public int ConfirmHoldMs { get; }
        public int DestinationSettleMs { get; }
        public int DetectionRetryMs { get; }

        public PaimonMenuNavigationTiming(
            int controllerModeSettleMs,
            int menuOpenSettleMs,
            int moveHoldMs,
            int moveSettleMs,
            int preConfirmSettleMs,
            int confirmHoldMs,
            int destinationSettleMs,
            int detectionRetryMs)
        {
            ControllerModeSettleMs = controllerModeSettleMs;
            MenuOpenSettleMs = menuOpenSettleMs;
            MoveHoldMs = moveHoldMs;
            MoveSettleMs = moveSettleMs;
            PreConfirmSettleMs = preConfirmSettleMs;
            ConfirmHoldMs = confirmHoldMs;
            DestinationSettleMs = destinationSettleMs;
            DetectionRetryMs = detectionRetryMs;
        }
    }

    internal sealed class PaimonMenuNavigationResult : IDisposable
    {
        public bool Success { get; }
        public string Message { get; }
        public string DetectionDetails { get; }
        public Bitmap DiagnosticScreenshot { get; }

        private PaimonMenuNavigationResult(
            bool success,
            string message,
            string detectionDetails,
            Bitmap diagnosticScreenshot)
        {
            Success = success;
            Message = message;
            DetectionDetails = detectionDetails;
            DiagnosticScreenshot = diagnosticScreenshot;
        }

        public static PaimonMenuNavigationResult Succeeded(string details) =>
            new PaimonMenuNavigationResult(true, null, details, null);

        public static PaimonMenuNavigationResult Failed(string message, string details, Bitmap screenshot) =>
            new PaimonMenuNavigationResult(false, message, details, screenshot);

        public void Dispose() => DiagnosticScreenshot?.Dispose();
    }

    /// <summary>
    /// State-aware Inventory entry: observe the current Paimon menu, make one move, and observe again.
    /// It never confirms until Inventory itself is the visually selected tile.
    /// </summary>
    internal sealed class PaimonMenuNavigator
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        internal const int DefaultMaximumMoves = 12;
        internal const int DefaultDetectionAttempts = 3;
        internal const int DefaultUnchangedSelectionRetries = 2;

        private readonly GameNavigator navigator;
        private readonly IPaimonMenuDetector detector;
        private readonly IGameScreenCapture screenCapture;
        private readonly IInventoryScreenDetector inventoryScreenDetector;
        private readonly PaimonMenuPathPlanner pathPlanner;
        private readonly Action<int> wait;
        private readonly Func<bool> cancellationRequested;
        private readonly int maximumMoves;
        private readonly int detectionAttempts;
        private readonly int unchangedSelectionRetries;

        public PaimonMenuNavigator(
            GameNavigator navigator,
            IPaimonMenuDetector detector,
            IGameScreenCapture screenCapture,
            IInventoryScreenDetector inventoryScreenDetector,
            PaimonMenuPathPlanner pathPlanner = null,
            Action<int> wait = null,
            Func<bool> cancellationRequested = null,
            int maximumMoves = DefaultMaximumMoves,
            int detectionAttempts = DefaultDetectionAttempts,
            int unchangedSelectionRetries = DefaultUnchangedSelectionRetries)
        {
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.inventoryScreenDetector = inventoryScreenDetector ?? throw new ArgumentNullException(nameof(inventoryScreenDetector));
            this.pathPlanner = pathPlanner ?? new PaimonMenuPathPlanner();
            this.wait = wait ?? Thread.Sleep;
            this.cancellationRequested = cancellationRequested ?? (() => false);
            this.maximumMoves = Math.Max(0, maximumMoves);
            this.detectionAttempts = Math.Max(1, detectionAttempts);
            this.unchangedSelectionRetries = Math.Max(0, unchangedSelectionRetries);
        }

        public PaimonMenuNavigationResult OpenInventory(PaimonMenuNavigationTiming timing)
        {
            if (timing == null) throw new ArgumentNullException(nameof(timing));

            Bitmap latestScreenshot = null;
            PaimonMenuDetection latestDetection = null;
            try
            {
                navigator.EnterControllerMode();
                wait(timing.ControllerModeSettleMs);
                navigator.OpenMenu();
                wait(timing.MenuOpenSettleMs);

                var visitedSelections = new HashSet<string>(StringComparer.Ordinal);
                string selectionExpectedToChange = null;
                GameNavigator.MenuDirection? lastPlannedDirection = null;
                string previousSelectionForLastMove = null;
                int unchangedRetries = 0;
                int moves = 0;

                while (true)
                {
                    if (cancellationRequested())
                        return Fail("Inventory menu navigation was cancelled.", latestDetection, Transfer(ref latestScreenshot));

                    bool usable = false;
                    for (int attempt = 1; attempt <= detectionAttempts; attempt++)
                    {
                        latestScreenshot?.Dispose();
                        latestScreenshot = screenCapture.CaptureWindow();
                        latestDetection = detector.Detect(latestScreenshot);
                        usable = latestDetection.HasUsableSelection && latestDetection.InventoryTile != null;
                        Logger.Debug("Paimon menu detection attempt {0}/{1}: {2}; failure={3}",
                            attempt, detectionAttempts, latestDetection.Describe(), latestDetection.FailureReason ?? "(none)");
                        if (usable) break;
                        if (attempt < detectionAttempts) wait(timing.DetectionRetryMs);
                    }

                    if (lastPlannedDirection.HasValue)
                    {
                        Logger.Info(
                            "Paimon menu movement result: direction={0}, previous={1}, detected={2}",
                            lastPlannedDirection.Value,
                            previousSelectionForLastMove ?? "(unknown)",
                            latestDetection?.SelectedTile?.ToString() ?? "(none)");
                        lastPlannedDirection = null;
                        previousSelectionForLastMove = null;
                    }

                    if (!usable)
                    {
                        string reason = latestDetection?.InventoryTile == null
                            ? "Inventory was not confidently detected in the visible Paimon menu."
                            : latestDetection?.FailureReason ?? "The selected Paimon menu tile could not be detected.";
                        return Fail(reason, latestDetection, Transfer(ref latestScreenshot));
                    }

                    string currentSelection = SelectionIdentity(latestDetection.SelectedTile);
                    if (selectionExpectedToChange != null && currentSelection == selectionExpectedToChange)
                    {
                        unchangedRetries++;
                        if (unchangedRetries > unchangedSelectionRetries)
                        {
                            return Fail(
                                $"Paimon menu selection did not change after {unchangedSelectionRetries + 1} verified capture(s).",
                                latestDetection,
                                Transfer(ref latestScreenshot));
                        }

                        Logger.Warn("Paimon menu focus remained on {0}; re-capturing before sending more input ({1}/{2}).",
                            latestDetection.SelectedTile, unchangedRetries, unchangedSelectionRetries);
                        wait(timing.DetectionRetryMs);
                        continue;
                    }

                    selectionExpectedToChange = null;
                    unchangedRetries = 0;
                    if (!visitedSelections.Add(currentSelection))
                    {
                        return Fail(
                            $"Paimon menu navigation entered a selection cycle at {latestDetection.SelectedTile}.",
                            latestDetection,
                            Transfer(ref latestScreenshot));
                    }

                    if (ReferenceEquals(latestDetection.SelectedTile, latestDetection.InventoryTile))
                    {
                        Logger.Info("Inventory tile is visibly selected; confirming.");
                        wait(timing.PreConfirmSettleMs);
                        navigator.TapConfirm(timing.ConfirmHoldMs);
                        wait(timing.DestinationSettleMs);

                        latestScreenshot.Dispose();
                        latestScreenshot = screenCapture.CaptureWindow();
                        InventoryScreenDetection destination = inventoryScreenDetector.Detect(latestScreenshot);
                        if (!destination.IsInventoryOpen)
                        {
                            return Fail(
                                $"Inventory confirmation was sent, but the Inventory screen could not be verified (OCR: \"{destination.RawText}\").",
                                latestDetection,
                                Transfer(ref latestScreenshot));
                        }

                        string details = $"Selected Inventory and verified destination tab {destination.TabName} " +
                            $"(OCR: \"{destination.RawText}\").";
                        latestScreenshot.Dispose();
                        latestScreenshot = null;
                        return PaimonMenuNavigationResult.Succeeded(details);
                    }

                    if (moves >= maximumMoves)
                    {
                        return Fail(
                            $"Inventory was not reached within the {maximumMoves}-move safety limit.",
                            latestDetection,
                            Transfer(ref latestScreenshot));
                    }

                    PaimonMenuPathPlan plan = pathPlanner.Plan(
                        latestDetection.Tiles,
                        latestDetection.SelectedTile,
                        latestDetection.InventoryTile);
                    if (!plan.Success || plan.Steps.Count == 0)
                    {
                        return Fail(
                            plan.FailureReason ?? "No safe next step toward Inventory was available.",
                            latestDetection,
                            Transfer(ref latestScreenshot));
                    }

                    GameNavigator.MenuDirection next = plan.Steps[0];
                    Logger.Info("Paimon menu: selected={0}, target={1}, next={2}, plannedSteps={3}",
                        latestDetection.SelectedTile, latestDetection.InventoryTile, next, plan.Steps.Count);
                    selectionExpectedToChange = currentSelection;
                    lastPlannedDirection = next;
                    previousSelectionForLastMove = latestDetection.SelectedTile.ToString();
                    latestScreenshot.Dispose();
                    latestScreenshot = null;
                    navigator.MoveStep(next, timing.MoveHoldMs, timing.MoveSettleMs);
                    moves++;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "State-aware Paimon menu navigation failed unexpectedly.");
                return Fail($"State-aware Inventory navigation failed: {ex.Message}", latestDetection, Transfer(ref latestScreenshot));
            }
            finally
            {
                latestScreenshot?.Dispose();
            }
        }

        private PaimonMenuNavigationResult Fail(
            string reason,
            PaimonMenuDetection detection,
            Bitmap diagnosticScreenshot)
        {
            string details = detection?.Describe() ?? "No menu detection was available.";
            Logger.Warn("{0} Detection: {1}", reason, details);
            try
            {
                navigator.MashBack();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not complete the safe menu back-out after navigation failure.");
            }
            return PaimonMenuNavigationResult.Failed(reason, details, diagnosticScreenshot);
        }

        private static Bitmap Transfer(ref Bitmap bitmap)
        {
            Bitmap transferred = bitmap;
            bitmap = null;
            return transferred;
        }

        private static string SelectionIdentity(PaimonMenuTile tile)
        {
            string label = PaimonMenuDetector.Normalize(tile.Label);
            if (label.Length > 0) return label;
            return $"position:{Math.Round(tile.Center.X / 10f)}:{Math.Round(tile.Center.Y / 10f)}";
        }
    }
}
