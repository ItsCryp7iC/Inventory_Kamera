using System;
using System.Drawing;
using System.Linq;
using System.Threading;

namespace InventoryKamera.game
{
    internal interface IGameSettingsInput
    {
        void PressEscape();
    }

    internal sealed class NavigationGameSettingsInput : IGameSettingsInput
    {
        public void PressEscape() => Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
    }

    internal sealed class GameInputModeTransitionTiming
    {
        internal GameInputModeTransitionTiming(
            int menuSettleMs,
            int settingsSettleMs,
            int dropdownSettleMs,
            int modeSwitchSettleMs,
            int moveHoldMs,
            int moveSettleMs,
            int detectionRetryMs)
        {
            MenuSettleMs = menuSettleMs;
            SettingsSettleMs = settingsSettleMs;
            DropdownSettleMs = dropdownSettleMs;
            ModeSwitchSettleMs = modeSwitchSettleMs;
            MoveHoldMs = moveHoldMs;
            MoveSettleMs = moveSettleMs;
            DetectionRetryMs = detectionRetryMs;
        }

        internal int MenuSettleMs { get; }
        internal int SettingsSettleMs { get; }
        internal int DropdownSettleMs { get; }
        internal int ModeSwitchSettleMs { get; }
        internal int MoveHoldMs { get; }
        internal int MoveSettleMs { get; }
        internal int DetectionRetryMs { get; }

        internal static GameInputModeTransitionTiming CreateScaled() =>
            new GameInputModeTransitionTiming(
                InventoryScraper.ScaledControllerDelay(2000),
                InventoryScraper.ScaledControllerDelay(2000),
                InventoryScraper.ScaledControllerDelay(500),
                Math.Max(1500, InventoryScraper.ScaledControllerDelay(1000)),
                InventoryScraper.ScaledControllerDelay(PaimonMenuNavigationTiming.SingleStepHoldMs),
                InventoryScraper.ScaledControllerDelay(PaimonMenuNavigationTiming.SingleStepSettleMs),
                InventoryScraper.ScaledControllerDelay(300));
    }

    internal interface IGameInputModeTransition
    {
        bool TryRestoreKeyboardMouse(GameNavigator navigator, out string failureReason);
        void ExitKeyboardSettings();
    }

    /// <summary>
    /// Performs the bounded, best-effort Controller-to-Keyboard restoration. It reuses the
    /// state-aware Paimon tile planner to normalize focus to Shop, then enters the fixed five-item
    /// side rail shown by the supplied fixture and verifies the Controls destination before changing
    /// mode. Scan startup no longer uses this type to switch Keyboard to Controller.
    /// </summary>
    internal sealed class GameInputModeNavigator : IGameInputModeTransition
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private const int MaximumShopMoves = 12;
        internal const int MaximumNormalizationActions = 4;
        internal const int MaximumDropdownOpenAttempts = 2;
        private const int DetectionAttempts = 3;
        private const int SettingsGearDownSteps = 4;

        private readonly IGenshinInputModeDetector inputModeDetector;
        private readonly IPaimonMenuDetector paimonMenuDetector;
        private readonly IGameScreenCapture screenCapture;
        private readonly IGameSettingsInput settingsInput;
        private readonly PaimonMenuPathPlanner pathPlanner;
        private readonly GameInputModeTransitionTiming timing;
        private readonly Action<int> wait;
        private readonly GameInputRestorationDiagnostics diagnostics;

        internal GameInputModeNavigator(
            IGenshinInputModeDetector inputModeDetector,
            IPaimonMenuDetector paimonMenuDetector,
            IGameScreenCapture screenCapture,
            IGameSettingsInput settingsInput,
            GameInputModeTransitionTiming timing,
            PaimonMenuPathPlanner pathPlanner = null,
            Action<int> wait = null,
            GameInputRestorationDiagnostics diagnostics = null)
        {
            this.inputModeDetector = inputModeDetector ?? throw new ArgumentNullException(nameof(inputModeDetector));
            this.paimonMenuDetector = paimonMenuDetector ?? throw new ArgumentNullException(nameof(paimonMenuDetector));
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.settingsInput = settingsInput ?? throw new ArgumentNullException(nameof(settingsInput));
            this.timing = timing ?? throw new ArgumentNullException(nameof(timing));
            this.pathPlanner = pathPlanner ?? new PaimonMenuPathPlanner();
            this.wait = wait ?? Thread.Sleep;
            this.diagnostics = diagnostics ?? new GameInputRestorationDiagnostics();
        }

        public bool TryRestoreKeyboardMouse(GameNavigator navigator, out string failureReason)
        {
            if (navigator == null) throw new ArgumentNullException(nameof(navigator));

            diagnostics.BeginAttempt();
            diagnostics.Record(
                "NormalizeToPaimon",
                $"expectedMode=Controller; expectedScreen=PaimonMenu; result=started; " +
                $"menuSettleMs={timing.MenuSettleMs}; settingsSettleMs={timing.SettingsSettleMs}; " +
                $"dropdownSettleMs={timing.DropdownSettleMs}; modeSwitchSettleMs={timing.ModeSwitchSettleMs}; " +
                $"moveHoldMs={timing.MoveHoldMs}; moveSettleMs={timing.MoveSettleMs}");

            if (!TryNormalizeToPaimon(navigator, out bool alreadyKeyboardMouse, out failureReason))
            {
                diagnostics.Record(
                    "NormalizeToPaimon",
                    $"result=failure; timeoutReason=\"{failureReason}\"");
                return false;
            }
            if (alreadyKeyboardMouse)
            {
                diagnostics.Record(
                    "NormalizeToPaimon",
                    "result=success; observedMode=KeyboardMouse; noControllerTransitionRequired=true");
                return true;
            }
            diagnostics.Record(
                "NormalizeToPaimon",
                "result=success; observedMode=Controller; observedScreen=PaimonMenu");

            if (!TrySelectShop(navigator, out failureReason))
            {
                diagnostics.Record(
                    "SelectShop",
                    $"result=failure; timeoutReason=\"{failureReason}\"");
                return false;
            }
            diagnostics.Record("SelectShop", "result=success; expectedTile=Shop");

            // Shop is the first tile beside the first side-rail item. One Left enters the rail;
            // four Down steps reach the fifth (Settings) item visible in the supplied fixture.
            SendControllerAction(
                navigator,
                "EnterSettingsSideRail",
                "MoveLeft",
                1,
                () => navigator.MoveStep(
                    GameNavigator.MenuDirection.Left,
                    timing.MoveHoldMs,
                    timing.MoveSettleMs));
            SendControllerAction(
                navigator,
                "FocusSettings",
                $"MoveDownx{SettingsGearDownSteps}",
                1,
                () => navigator.Move(
                    GameNavigator.MenuDirection.Down,
                    SettingsGearDownSteps,
                    timing.MoveHoldMs,
                    timing.MoveSettleMs));
            SendControllerAction(
                navigator,
                "OpenSettings",
                "SemanticConfirm",
                1,
                () => navigator.TapConfirm(timing.MoveHoldMs));
            long settingsActionCompletedAt = diagnostics.ElapsedMilliseconds;
            wait(timing.SettingsSettleMs);

            ControlTypeInteractionDetection controls = CaptureControlTypeInteraction(
                "01_controls_open",
                "VerifyControls",
                "mode=Controller; screen=Controls; state=ControlTypeFocused",
                attempt: 1,
                actionCompletedAtMs: settingsActionCompletedAt);
            if (controls.Page.Mode != GenshinInputMode.Controller ||
                controls.Page.Screen != GenshinInputScreen.Controls)
            {
                failureReason = $"Controller Controls page was not verified after opening Settings ({controls}).";
                diagnostics.Record(
                    "VerifyControls",
                    $"result=failure; timeoutReason=\"{failureReason}\"");
                return false;
            }
            if (!TryFocusControlType(navigator, controls, out failureReason)) return false;
            if (!TryOpenControllerDropdown(navigator, out failureReason)) return false;

            SendControllerAction(
                navigator,
                "FocusKeyboardOption",
                "MoveUp",
                1,
                () => navigator.MoveStep(
                    GameNavigator.MenuDirection.Up,
                    timing.MoveHoldMs,
                    timing.MoveSettleMs));
            CaptureControlTypeInteraction(
                "04_keyboard_option_focused",
                "FocusKeyboardOption",
                "dropdownOpen=true; focusedOption=Keyboard",
                attempt: 1,
                actionCompletedAtMs: diagnostics.ElapsedMilliseconds);
            SendControllerAction(
                navigator,
                "SelectKeyboardOption",
                "SemanticConfirm",
                1,
                () => navigator.TapConfirm(timing.MoveHoldMs));
            long selectionActionCompletedAt = diagnostics.ElapsedMilliseconds;
            wait(timing.ModeSwitchSettleMs);
            CaptureControlTypeInteraction(
                "05_after_keyboard_selection",
                "VerifyKeyboardSelection",
                "mode=KeyboardMouse; screen=Controls; controlType=Keyboard",
                attempt: 1,
                actionCompletedAtMs: selectionActionCompletedAt);
            Logger.Info("Control Type restoration state: KeyboardSelected; awaiting positive KeyboardMouse verification.");
            failureReason = null;
            return true;
        }

        private bool TryFocusControlType(
            GameNavigator navigator,
            ControlTypeInteractionDetection controls,
            out string failureReason)
        {
            if (controls.State == ControlTypeInteractionState.ControlTypeFocused)
            {
                diagnostics.Record(
                    "FocusControlType",
                    $"before={controls.State}; action=None; after={controls.State}; result=success; " +
                    $"focusLightRatio={controls.FocusMarkerLightRatio:0.000}");
                failureReason = null;
                return true;
            }

            if (controls.State != ControlTypeInteractionState.ControlsSidebarFocused)
            {
                failureReason =
                    $"control_type_focus_unresolved: Controls opened in an unsupported focus state ({controls}).";
                diagnostics.Record(
                    "FocusControlType",
                    $"before={controls.State}; action=None; result=failure; timeoutReason=\"{failureReason}\"");
                return false;
            }

            diagnostics.Record(
                "FocusControlType",
                "before=ControlsSidebarFocused; action=Right; expectedAfter=ControlTypeFocused");
            SendControllerAction(
                navigator,
                "FocusControlType",
                "MoveRight",
                1,
                () => navigator.MoveStep(
                    GameNavigator.MenuDirection.Right,
                    timing.MoveHoldMs,
                    timing.MoveSettleMs));
            long actionCompletedAt = diagnostics.ElapsedMilliseconds;
            ControlTypeInteractionDetection after = CaptureControlTypeInteraction(
                "02_after_focus_right",
                "FocusControlType",
                "mode=Controller; screen=Controls; state=ControlTypeFocused",
                attempt: 1,
                actionCompletedAtMs: actionCompletedAt);

            if (after.Page.Mode != GenshinInputMode.Controller ||
                after.Page.Screen != GenshinInputScreen.Controls ||
                after.State == ControlTypeInteractionState.Unknown)
            {
                failureReason =
                    $"control_type_focus_unresolved: Control Type focus became unresolved after one Right ({after}).";
                diagnostics.Record(
                    "FocusControlType",
                    $"before=ControlsSidebarFocused; action=Right; after={after.State}; result=failure; " +
                    $"timeoutReason=\"{failureReason}\"");
                return false;
            }

            if (after.State != ControlTypeInteractionState.ControlTypeFocused)
            {
                failureReason = after.State == ControlTypeInteractionState.ControlsSidebarFocused
                    ? $"control_type_focus_unchanged: focus remained on the Controls sidebar after one Right ({after})."
                    : $"control_type_focus_unresolved: unexpected focus state after one Right ({after}).";
                diagnostics.Record(
                    "FocusControlType",
                    $"before=ControlsSidebarFocused; action=Right; after={after.State}; result=failure; " +
                    $"timeoutReason=\"{failureReason}\"");
                return false;
            }

            diagnostics.Record(
                "FocusControlType",
                $"before=ControlsSidebarFocused; action=Right; after=ControlTypeFocused; result=success; " +
                $"focusLightRatio={after.FocusMarkerLightRatio:0.000}");
            failureReason = null;
            return true;
        }

        private bool TryOpenControllerDropdown(GameNavigator navigator, out string failureReason)
        {
            for (int attempt = 1; attempt <= MaximumDropdownOpenAttempts; attempt++)
            {
                Logger.Info(
                    "Control Type restoration: semantic Confirm/Switch attempt {0}/{1}.",
                    attempt,
                    MaximumDropdownOpenAttempts);
                SendControllerAction(
                    navigator,
                    "OpenControlTypeDropdown",
                    "SemanticConfirm",
                    attempt,
                    () => navigator.TapConfirm(timing.MoveHoldMs));
                long actionCompletedAt = diagnostics.ElapsedMilliseconds;
                wait(timing.DropdownSettleMs);

                ControlTypeInteractionDetection current = CaptureControlTypeInteraction(
                    $"03_dropdown_attempt_{attempt:00}",
                    "OpenControlTypeDropdown",
                    "state=DropdownOpen; currentOption=Controller",
                    attempt,
                    actionCompletedAt);
                Logger.Info(
                    "Control Type restoration observation after Confirm/Switch {0}/{1}: {2}",
                    attempt,
                    MaximumDropdownOpenAttempts,
                    current);

                if (current.Page.Mode != GenshinInputMode.Controller ||
                    current.Page.Screen != GenshinInputScreen.Controls)
                {
                    failureReason =
                        $"dropdown_not_verified: Controls became unresolved after Confirm/Switch attempt {attempt} ({current}).";
                    diagnostics.Record(
                        "OpenControlTypeDropdown",
                        $"result=failure; attempt={attempt}; timeoutReason=\"{failureReason}\"");
                    return false;
                }
                if (current.State == ControlTypeInteractionState.DropdownOpen)
                {
                    if (!current.Dropdown.ControllerSelected)
                    {
                        failureReason =
                            $"dropdown_not_verified: the expanded Control Type options were visible, " +
                            $"but the current Controller option was not positively selected ({current}).";
                        diagnostics.Record(
                            "OpenControlTypeDropdown",
                            $"result=failure; attempt={attempt}; timeoutReason=\"{failureReason}\"");
                        return false;
                    }

                    diagnostics.Record(
                        "OpenControlTypeDropdown",
                        $"result=success; attempt={attempt}; observedState={current.State}; " +
                        $"controllerSelected={current.Dropdown.ControllerSelected}; optionsOcr=\"{current.Dropdown.RawText}\"");
                    failureReason = null;
                    return true;
                }
                if (current.State == ControlTypeInteractionState.ControlTypeFocused)
                {
                    if (attempt < MaximumDropdownOpenAttempts) continue;

                    failureReason =
                        $"dropdown_open_input_missed: Control Type remained focused after " +
                        $"{MaximumDropdownOpenAttempts} semantic Confirm/Switch attempts ({current}).";
                    diagnostics.Record(
                        "OpenControlTypeDropdown",
                        $"result=failure; attempt={attempt}; timeoutReason=\"{failureReason}\"");
                    return false;
                }

                failureReason = current.State == ControlTypeInteractionState.ControlsSidebarFocused
                    ? $"control_type_not_focused: focus left Control Type after Confirm/Switch attempt {attempt} ({current})."
                    : $"dropdown_not_verified: the Control Type interaction state became unresolved ({current}).";
                diagnostics.Record(
                    "OpenControlTypeDropdown",
                    $"result=failure; attempt={attempt}; timeoutReason=\"{failureReason}\"");
                return false;
            }

            failureReason = "dropdown_not_verified: bounded Control Type dropdown attempts were exhausted.";
            diagnostics.Record(
                "OpenControlTypeDropdown",
                $"result=failure; timeoutReason=\"{failureReason}\"");
            return false;
        }

        /// <summary>
        /// Leaves only positively recognized controller UI one semantic step at a time. Each input
        /// is followed by a fresh classification; Unknown, unchanged, cyclic, or over-bound state
        /// fails without another blind Back press.
        /// </summary>
        private bool TryNormalizeToPaimon(
            GameNavigator navigator,
            out bool alreadyKeyboardMouse,
            out string failureReason)
        {
            alreadyKeyboardMouse = false;
            var visited = new System.Collections.Generic.HashSet<GenshinInputScreen>();

            for (int action = 0; action <= MaximumNormalizationActions; action++)
            {
                GenshinInputModeDetection current = inputModeDetector.Detect();
                Logger.Info(
                    "Controller UI normalization observation {0}/{1}: {2}",
                    action + 1,
                    MaximumNormalizationActions + 1,
                    current);
                diagnostics.Record(
                    "NormalizeToPaimon",
                    $"attempt={action + 1}; expectedMode=Controller; expectedScreen=PaimonMenu; " +
                    $"nextObserved=({current})");
                if (current.Mode == GenshinInputMode.KeyboardMouse)
                {
                    alreadyKeyboardMouse = true;
                    failureReason = null;
                    return true;
                }
                if (current.Mode != GenshinInputMode.Controller)
                {
                    failureReason =
                        $"Input mode became unresolved while normalizing restoration UI ({current}).";
                    return false;
                }
                if (current.Screen == GenshinInputScreen.PaimonMenu)
                {
                    failureReason = null;
                    return true;
                }
                if (current.Screen == GenshinInputScreen.Unknown)
                {
                    failureReason =
                        $"Controller mode was visible, but the current UI was not a known safe restoration state ({current}).";
                    return false;
                }
                if (action == MaximumNormalizationActions)
                {
                    failureReason =
                        $"Controller UI normalization exceeded {MaximumNormalizationActions} verified actions.";
                    return false;
                }
                if (!visited.Add(current.Screen))
                {
                    failureReason =
                        $"Controller UI normalization did not make verified progress; {current.Screen} was detected again.";
                    return false;
                }

                switch (current.Screen)
                {
                    case GenshinInputScreen.FreeRoam:
                        Logger.Info("Controller UI normalization: FreeRoam -> OpenMenu.");
                        SendControllerAction(
                            navigator,
                            "NormalizeToPaimon",
                            "OpenMenu",
                            action + 1,
                            navigator.OpenMenu);
                        break;
                    case GenshinInputScreen.Inventory:
                    case GenshinInputScreen.Character:
                    case GenshinInputScreen.Controls:
                        Logger.Info(
                            "Controller UI normalization: {0} -> one semantic Back.",
                            current.Screen);
                        SendControllerAction(
                            navigator,
                            "NormalizeToPaimon",
                            "SemanticBack",
                            action + 1,
                            () => navigator.TapBack(timing.MoveHoldMs));
                        break;
                    default:
                        failureReason =
                            $"Controller UI {current.Screen} is not a supported restoration starting state.";
                        return false;
                }

                wait(timing.MenuSettleMs);
            }

            failureReason =
                $"Controller UI normalization exceeded {MaximumNormalizationActions} verified actions.";
            return false;
        }

        public void ExitKeyboardSettings()
        {
            settingsInput.PressEscape();
            wait(timing.MenuSettleMs);
            settingsInput.PressEscape();
            wait(timing.MenuSettleMs);
        }

        private bool TrySelectShop(GameNavigator navigator, out string failureReason)
        {
            bool sidebarRecoveryAttempted = false;
            for (int move = 0; move <= MaximumShopMoves; move++)
            {
                PaimonMenuDetection detection = DetectPaimonMenu();
                if (detection == null)
                {
                    failureReason = "Paimon tile focus could not be detected while restoring Keyboard/Mouse mode.";
                    return false;
                }
                diagnostics.Record(
                    "SelectShop",
                    $"attempt={move + 1}; expectedTile=Shop; nextObserved=({detection.Describe()})");

                // Opening Paimon immediately after leaving Settings can retain focus in the side
                // rail. The tile detector still sees the tile layout but deliberately reports no
                // selected tile. One Right step returns focus to the tile grid; subsequent movement
                // remains visually planned and verified.
                if (!detection.HasUsableSelection && detection.Tiles.Count > 0 && !sidebarRecoveryAttempted)
                {
                    sidebarRecoveryAttempted = true;
                    SendControllerAction(
                        navigator,
                        "SelectShop",
                        "MoveRightToTileGrid",
                        move + 1,
                        () => navigator.MoveStep(
                            GameNavigator.MenuDirection.Right,
                            timing.MoveHoldMs,
                            timing.MoveSettleMs));
                    continue;
                }
                if (!detection.HasUsableSelection)
                {
                    failureReason = "Paimon tile focus could not be detected while restoring Keyboard/Mouse mode.";
                    return false;
                }

                PaimonMenuTile shop = detection.Tiles.FirstOrDefault(tile =>
                    string.Equals(PaimonMenuDetector.Normalize(tile.Label), "shop", StringComparison.Ordinal));
                if (shop == null)
                {
                    failureReason = $"Shop tile was not detected while normalizing Paimon focus ({detection.Describe()}).";
                    return false;
                }

                PaimonMenuPathPlan plan = pathPlanner.Plan(detection.Tiles, detection.SelectedTile, shop);
                if (!plan.Success)
                {
                    failureReason = plan.FailureReason;
                    return false;
                }
                if (plan.AlreadyAtTarget)
                {
                    diagnostics.Record(
                        "SelectShop",
                        $"attempt={move + 1}; result=success; selectedTile={detection.SelectedTile?.Label}");
                    failureReason = null;
                    return true;
                }
                if (move == MaximumShopMoves) break;

                SendControllerAction(
                    navigator,
                    "SelectShop",
                    "Move" + plan.Steps[0],
                    move + 1,
                    () => navigator.MoveStep(
                        plan.Steps[0], timing.MoveHoldMs, timing.MoveSettleMs));
            }

            failureReason = $"Shop focus was not reached within {MaximumShopMoves} verified moves.";
            return false;
        }

        private PaimonMenuDetection DetectPaimonMenu()
        {
            PaimonMenuDetection latest = null;
            for (int attempt = 1; attempt <= DetectionAttempts; attempt++)
            {
                using Bitmap screenshot = screenCapture.CaptureWindow();
                latest = paimonMenuDetector.Detect(screenshot);
                if (latest.HasUsableSelection || latest.Tiles.Count > 0) return latest;
                if (attempt < DetectionAttempts) wait(timing.DetectionRetryMs);
            }
            return latest;
        }

        private ControlTypeInteractionDetection CaptureControlTypeInteraction(
            string diagnosticStage,
            string restoreStep,
            string expected,
            int attempt,
            long actionCompletedAtMs)
        {
            using Bitmap screenshot = screenCapture.CaptureWindow();
            ControlTypeInteractionDetection detection =
                inputModeDetector.DetectControlTypeInteraction(screenshot);
            diagnostics.SaveControlTypeFrame(diagnosticStage, screenshot, detection);
            diagnostics.Record(
                restoreStep,
                $"attempt={attempt}; expected={expected}; nextObserved=({detection}); " +
                $"elapsedWaitSinceActionMs={diagnostics.ElapsedMilliseconds - actionCompletedAtMs}");
            return detection;
        }

        private void SendControllerAction(
            GameNavigator navigator,
            string restoreStep,
            string action,
            int attempt,
            Action send)
        {
            diagnostics.RecordActionBefore(restoreStep, action, attempt, navigator.IsAvailable);
            long startedAt = diagnostics.ElapsedMilliseconds;
            try
            {
                send();
                diagnostics.RecordActionAfter(
                    restoreStep,
                    action,
                    attempt,
                    navigator.IsAvailable,
                    startedAt);
            }
            catch (Exception ex)
            {
                diagnostics.RecordActionFailure(restoreStep, action, attempt, ex, startedAt);
                throw;
            }
        }
    }
}
