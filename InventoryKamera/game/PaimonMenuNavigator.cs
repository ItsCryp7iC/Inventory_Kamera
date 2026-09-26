using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace InventoryKamera.game
{
    internal enum PaimonMenuTarget
    {
        Inventory,
        Character,
    }

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
    /// State-aware Paimon-menu entry: observe the current menu, make one move, and observe again.
    /// It never confirms until the requested semantic target is the visually selected tile.
    /// </summary>
    internal sealed class PaimonMenuNavigator
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        internal const int DefaultMaximumMoves = 12;
        internal const int DefaultDetectionAttempts = 3;
        internal const int DefaultUnchangedSelectionRetries = 2;
        internal const int CharacterDestinationVerificationAttempts = 3;

        private readonly GameNavigator navigator;
        private readonly IPaimonMenuDetector detector;
        private readonly IGameScreenCapture screenCapture;
        private readonly IInventoryScreenDetector inventoryScreenDetector;
        private readonly ICharacterScreenDetector characterScreenDetector;
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
            ICharacterScreenDetector characterScreenDetector = null,
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
            this.characterScreenDetector = characterScreenDetector;
            this.pathPlanner = pathPlanner ?? new PaimonMenuPathPlanner();
            this.wait = wait ?? Thread.Sleep;
            this.cancellationRequested = cancellationRequested ?? (() => false);
            this.maximumMoves = Math.Max(0, maximumMoves);
            this.detectionAttempts = Math.Max(1, detectionAttempts);
            this.unchangedSelectionRetries = Math.Max(0, unchangedSelectionRetries);
        }

        public PaimonMenuNavigationResult OpenInventory(PaimonMenuNavigationTiming timing)
        {
            return OpenTarget(PaimonMenuTarget.Inventory, timing);
        }

        public PaimonMenuNavigationResult OpenCharacter(PaimonMenuNavigationTiming timing)
        {
            return OpenTarget(PaimonMenuTarget.Character, timing);
        }

        private PaimonMenuNavigationResult OpenTarget(PaimonMenuTarget target, PaimonMenuNavigationTiming timing)
        {
            if (timing == null) throw new ArgumentNullException(nameof(timing));
            if (target == PaimonMenuTarget.Character && characterScreenDetector == null)
                return Fail("Character destination detection is not configured.", null, null);

            string targetName = target.ToString();

            Bitmap latestScreenshot = null;
            PaimonMenuDetection latestDetection = null;
            PaimonMenuTile latestTargetTile = null;
            try
            {
                navigator.EnterControllerMode();
                wait(timing.ControllerModeSettleMs);
                navigator.OpenMenu();
                wait(timing.MenuOpenSettleMs);

                var visitedSelections = new List<PaimonMenuTilePhysicalIdentity>();
                PaimonMenuTilePhysicalIdentity? selectionExpectedToChange = null;
                GameNavigator.MenuDirection? lastPlannedDirection = null;
                PaimonMenuTile previousSelectionForLastMove = null;
                int unchangedRetries = 0;
                int moves = 0;

                while (true)
                {
                    if (cancellationRequested())
                        return Fail($"{targetName} menu navigation was cancelled.", latestDetection, Transfer(ref latestScreenshot));

                    bool usable = false;
                    for (int attempt = 1; attempt <= detectionAttempts; attempt++)
                    {
                        latestScreenshot?.Dispose();
                        latestScreenshot = screenCapture.CaptureWindow();
                        latestDetection = detector.Detect(latestScreenshot);
                        latestTargetTile = GetTargetTile(latestDetection, target);
                        usable = latestDetection.HasUsableSelection && latestTargetTile != null;
                        Logger.Debug("Paimon menu detection attempt {0}/{1}: {2}; failure={3}",
                            attempt, detectionAttempts, latestDetection.Describe(), latestDetection.FailureReason ?? "(none)");
                        if (usable) break;
                        if (attempt < detectionAttempts) wait(timing.DetectionRetryMs);
                    }

                    if (lastPlannedDirection.HasValue)
                    {
                        PaimonMenuTile newlySelected = latestDetection?.SelectedTile;
                        bool semanticLabelEqual = SemanticLabelsEqual(previousSelectionForLastMove, newlySelected);
                        bool physicalIdentityEqual = PaimonMenuTilePhysicalIdentity.AreSame(
                            previousSelectionForLastMove,
                            newlySelected);
                        Logger.Info(
                            "Paimon menu movement result: direction={0}; previous={1}; detected={2}; " +
                            "semanticLabelEqual={3}; physicalIdentityEqual={4}",
                            lastPlannedDirection.Value,
                            DescribeTile(previousSelectionForLastMove),
                            DescribeTile(newlySelected),
                            semanticLabelEqual,
                            physicalIdentityEqual);
                        lastPlannedDirection = null;
                        previousSelectionForLastMove = null;
                    }

                    if (!usable)
                    {
                        string reason = latestTargetTile == null
                            ? $"{targetName} was not confidently detected in the visible Paimon menu."
                            : latestDetection?.FailureReason ?? "The selected Paimon menu tile could not be detected.";
                        return Fail(reason, latestDetection, Transfer(ref latestScreenshot));
                    }

                    PaimonMenuTile currentSelection = latestDetection.SelectedTile;
                    if (selectionExpectedToChange.HasValue && selectionExpectedToChange.Value.Matches(currentSelection))
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
                    if (visitedSelections.Exists(identity => identity.Matches(currentSelection)))
                    {
                        return Fail(
                            $"Paimon menu navigation entered a selection cycle at {latestDetection.SelectedTile}.",
                            latestDetection,
                            Transfer(ref latestScreenshot));
                    }
                    visitedSelections.Add(new PaimonMenuTilePhysicalIdentity(currentSelection));

                    if (PaimonMenuTilePhysicalIdentity.AreUniqueCorrespondingTiles(
                        latestDetection.SelectedTile,
                        latestTargetTile,
                        latestDetection.Tiles))
                    {
                        Logger.Info("{0} tile is visibly selected; confirming.", targetName);
                        wait(timing.PreConfirmSettleMs);
                        navigator.TapConfirm(timing.ConfirmHoldMs);
                        wait(timing.DestinationSettleMs);

                        int destinationAttempts = target == PaimonMenuTarget.Character
                            ? CharacterDestinationVerificationAttempts
                            : 1;
                        DestinationVerification destination = default;
                        for (int attempt = 1; attempt <= destinationAttempts; attempt++)
                        {
                            latestScreenshot?.Dispose();
                            latestScreenshot = screenCapture.CaptureWindow();
                            destination = VerifyDestination(target, latestScreenshot);
                            Logger.Info(
                                "{0} destination verification attempt {1}/{2}: success={3}; {4}",
                                targetName,
                                attempt,
                                destinationAttempts,
                                destination.Success,
                                destination.Success ? destination.SuccessDetails : destination.FailureMessage);
                            if (destination.Success) break;
                            if (attempt < destinationAttempts) wait(timing.DetectionRetryMs);
                        }

                        if (!destination.Success)
                        {
                            return Fail(
                                destination.FailureMessage,
                                latestDetection,
                                Transfer(ref latestScreenshot));
                        }

                        latestScreenshot.Dispose();
                        latestScreenshot = null;
                        return PaimonMenuNavigationResult.Succeeded(destination.SuccessDetails);
                    }

                    if (moves >= maximumMoves)
                    {
                        return Fail(
                            $"{targetName} was not reached within the {maximumMoves}-move safety limit.",
                            latestDetection,
                            Transfer(ref latestScreenshot));
                    }

                    PaimonMenuPathPlan plan = pathPlanner.Plan(
                        latestDetection.Tiles,
                        latestDetection.SelectedTile,
                        latestTargetTile);
                    if (!plan.Success || plan.Steps.Count == 0)
                    {
                        return Fail(
                            plan.FailureReason ?? $"No safe next step toward {targetName} was available.",
                            latestDetection,
                            Transfer(ref latestScreenshot));
                    }

                    GameNavigator.MenuDirection next = plan.Steps[0];
                    Logger.Info("Paimon menu: selected={0}, target={1}, next={2}, plannedSteps={3}",
                        latestDetection.SelectedTile, latestTargetTile, next, plan.Steps.Count);
                    selectionExpectedToChange = new PaimonMenuTilePhysicalIdentity(currentSelection);
                    lastPlannedDirection = next;
                    previousSelectionForLastMove = latestDetection.SelectedTile;
                    latestScreenshot.Dispose();
                    latestScreenshot = null;
                    navigator.MoveStep(next, timing.MoveHoldMs, timing.MoveSettleMs);
                    moves++;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "State-aware Paimon menu navigation failed unexpectedly.");
                return Fail($"State-aware {targetName} navigation failed: {ex.Message}", latestDetection, Transfer(ref latestScreenshot));
            }
            finally
            {
                latestScreenshot?.Dispose();
            }
        }

        private static PaimonMenuTile GetTargetTile(PaimonMenuDetection detection, PaimonMenuTarget target)
        {
            if (detection == null) return null;
            return target switch
            {
                PaimonMenuTarget.Inventory => detection.InventoryTile,
                PaimonMenuTarget.Character => detection.CharacterTile,
                _ => null,
            };
        }

        private DestinationVerification VerifyDestination(PaimonMenuTarget target, Bitmap screenshot)
        {
            if (target == PaimonMenuTarget.Inventory)
            {
                InventoryScreenDetection detection = inventoryScreenDetector.Detect(screenshot);
                return new DestinationVerification(
                    detection.IsInventoryOpen,
                    $"Selected Inventory and verified destination tab {detection.TabName} " +
                        $"(OCR: \"{detection.RawText}\").",
                    $"Inventory confirmation was sent, but the Inventory screen could not be verified " +
                        $"(OCR: \"{detection.RawText}\").");
            }

            CharacterScreenDetection character = characterScreenDetector.Detect(screenshot);
            return new DestinationVerification(
                character.IsCharacterOpen,
                $"Selected Character and verified destination label Attributes " +
                    $"(OCR: \"{character.RawText}\"; confidence={character.Confidence:P0}).",
                $"Character confirmation was sent, but the Character screen could not be verified " +
                    $"(OCR: \"{character.RawText}\"; confidence={character.Confidence:P0}).");
        }

        private readonly struct DestinationVerification
        {
            public bool Success { get; }
            public string SuccessDetails { get; }
            public string FailureMessage { get; }

            public DestinationVerification(bool success, string successDetails, string failureMessage)
            {
                Success = success;
                SuccessDetails = successDetails;
                FailureMessage = failureMessage;
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

        // Diagnostic only: semantic OCR equality never determines movement, cycles, or confirmation.
        private static bool SemanticLabelsEqual(PaimonMenuTile first, PaimonMenuTile second)
        {
            if (first == null || second == null) return false;
            return PaimonMenuDetector.Normalize(first.Label) == PaimonMenuDetector.Normalize(second.Label);
        }

        private static string DescribeTile(PaimonMenuTile tile) => tile == null
            ? "(none)"
            : $"label=\"{tile.Label}\", center=({tile.Center.X:0.0},{tile.Center.Y:0.0}), " +
                $"bounds={tile.TileBounds}";
    }
}
