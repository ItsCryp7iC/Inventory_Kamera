using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public class GameInputModeNavigatorTests
    {
        private static readonly GameInputModeTransitionTiming ImmediateTiming =
            new GameInputModeTransitionTiming(0, 0, 0, 0, 80, 300, 0);

        [Fact]
        public void ControllerRestore_NormalizesShopThenUsesSemanticControllerInputs()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.FreeRoam),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Controls),
                },
                dropdownOpen: true);
            var shop = new PaimonMenuTile(
                "Shop", Rectangle.Empty, new Rectangle(100, 100, 100, 100), 1f, 100);
            var paimonDetector = new FixedPaimonDetector(
                new PaimonMenuDetection(new[] { shop }, shop, null, null));
            var transition = CreateTransition(detector, new RecordingSettingsInput(events), paimonDetector, events);
            var gameInput = new RecordingGameInput(events);
            var gameNavigator = new GameNavigator(gameInput, milliseconds => events.Add("Wait:" + milliseconds));

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            Assert.Contains("Button:Menu:True", events);
            Assert.Contains("Horizontal:-1", events);
            Assert.Equal(4, events.FindAll(entry => entry == "Vertical:-1").Count);
            Assert.Equal(3, events.FindAll(entry => entry == "Button:Confirm:True").Count);
            Assert.Contains("Vertical:1", events);
            Assert.DoesNotContain(events, entry => entry.StartsWith("Click:", StringComparison.Ordinal));
        }

        [Fact]
        public void ControllerRestore_DoesNotChangeModeWhenPaimonFocusIsUnresolved()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.FreeRoam),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu),
                },
                dropdownOpen: true);
            var unresolved = new PaimonMenuDetection(
                Array.Empty<PaimonMenuTile>(), null, null, null, "unresolved");
            var transition = CreateTransition(
                detector,
                new RecordingSettingsInput(events),
                new FixedPaimonDetector(unresolved),
                events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("could not be detected", failure);
            Assert.DoesNotContain("Button:Confirm:True", events);
        }

        [Fact]
        public void ControllerRestore_RecoversSideRailFocusBeforePlanningToShop()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.FreeRoam),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Controls),
                },
                dropdownOpen: true);
            var shop = new PaimonMenuTile(
                "Shop", Rectangle.Empty, new Rectangle(100, 100, 100, 100), 1f, 100);
            var paimonDetector = new QueuePaimonDetector(
                new PaimonMenuDetection(new[] { shop }, null, null, null, "side rail selected"),
                new PaimonMenuDetection(new[] { shop }, shop, null, null));
            var transition = CreateTransition(
                detector, new RecordingSettingsInput(events), paimonDetector, events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            Assert.Contains("Horizontal:1", events);
            Assert.Contains("Horizontal:-1", events);
        }

        [Theory]
        [InlineData("Inventory")]
        [InlineData("Character")]
        public void ControllerRestore_KnownScanScreenBacksOutOneVerifiedStep(
            string startingScreenName)
        {
            var startingScreen = Enum.Parse<GenshinInputScreen>(startingScreenName);
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, startingScreen),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Controls),
                },
                dropdownOpen: true);
            var shop = new PaimonMenuTile(
                "Shop", Rectangle.Empty, new Rectangle(100, 100, 100, 100), 1f, 100);
            var transition = CreateTransition(
                detector,
                new RecordingSettingsInput(events),
                new FixedPaimonDetector(new PaimonMenuDetection(new[] { shop }, shop, null, null)),
                events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            int firstBack = events.IndexOf("Button:Back:True");
            int paimon = events.IndexOf("Detect:Controller:PaimonMenu");
            Assert.True(firstBack >= 0 && firstBack < paimon);
            Assert.Single(events.FindAll(entry => entry == "Button:Back:True"));
        }

        [Fact]
        public void ControllerRestore_InventoryCanBackOutThroughFreeRoamToPaimon()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.FreeRoam),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Controls),
                },
                dropdownOpen: true);
            var shop = new PaimonMenuTile(
                "Shop", Rectangle.Empty, new Rectangle(100, 100, 100, 100), 1f, 100);
            var transition = CreateTransition(
                detector,
                new RecordingSettingsInput(events),
                new FixedPaimonDetector(new PaimonMenuDetection(new[] { shop }, shop, null, null)),
                events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            int back = events.IndexOf("Button:Back:True");
            int freeRoam = events.IndexOf("Detect:Controller:FreeRoam");
            int openMenu = events.IndexOf("Button:Menu:True");
            int paimon = events.IndexOf("Detect:Controller:PaimonMenu");
            Assert.True(back >= 0 && back < freeRoam);
            Assert.True(freeRoam < openMenu && openMenu < paimon);
            Assert.Single(events.FindAll(entry => entry == "Button:Back:True"));
            Assert.Single(events.FindAll(entry => entry == "Button:Menu:True"));
        }

        [Fact]
        public void ControllerRestore_InventoryBecomingUnknownStopsAfterOneBack()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Unknown),
                },
                dropdownOpen: true);
            var transition = CreateTransition(
                detector, new RecordingSettingsInput(events), new UnusedPaimonDetector(), events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("not a known safe restoration state", failure);
            Assert.Single(events.FindAll(entry => entry == "Button:Back:True"));
            Assert.DoesNotContain("Button:Menu:True", events);
        }

        [Fact]
        public void ControllerRestore_FocusedControlTypeOpensDropdownOnFirstSemanticConfirm()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.DropdownOpen));
            var transition = CreateShopRestoreTransition(detector, events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            Assert.Equal(3, events.FindAll(entry => entry == "Button:Confirm:True").Count);
            Assert.Contains("ControlType:ControlTypeFocused", events);
            Assert.Contains("ControlType:DropdownOpen", events);
            Assert.DoesNotContain("Horizontal:1", events);
        }

        [Fact]
        public void ControllerRestore_SidebarFocusMovesRightOnceThenRequiresControlTypeFocus()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlsSidebarFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.DropdownOpen));
            var transition = CreateShopRestoreTransition(detector, events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            Assert.Single(events.FindAll(entry => entry == "Horizontal:1"));
            int sidebar = events.IndexOf("ControlType:ControlsSidebarFocused");
            int right = events.IndexOf("Horizontal:1");
            int focused = events.IndexOf("ControlType:ControlTypeFocused");
            int firstConfirm = events.IndexOf("Button:Confirm:True");
            int dropdownConfirm = events.FindIndex(firstConfirm + 1, entry => entry == "Button:Confirm:True");
            Assert.True(sidebar < right && right < focused && focused < dropdownConfirm);
            Assert.Contains("ControlType:DropdownOpen", events);
            Assert.Contains("Vertical:1", events);
        }

        [Fact]
        public void ControllerRestore_MissedDropdownInputRetriesOnceThenSucceeds()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.DropdownOpen));
            var transition = CreateShopRestoreTransition(detector, events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            // Open Settings, two semantic Confirm/Switch attempts, then select Keyboard.
            Assert.Equal(4, events.FindAll(entry => entry == "Button:Confirm:True").Count);
            // Initial Controls verification, the missed dropdown attempt, and the
            // post-MoveUp diagnostic observation are all fresh same-frame reads.
            Assert.Equal(
                3,
                events.FindAll(entry => entry == "ControlType:ControlTypeFocused").Count);
            Assert.Contains("Vertical:1", events);
        }

        [Fact]
        public void ControllerRestore_DropdownInputMissedTwiceFailsWithoutSelectingKeyboard()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused));
            var transition = CreateShopRestoreTransition(detector, events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("dropdown_open_input_missed", failure);
            Assert.Equal(3, events.FindAll(entry => entry == "Button:Confirm:True").Count);
            Assert.DoesNotContain("Vertical:1", events);
        }

        [Fact]
        public void ControllerRestore_FocusedControlTypeBecomingUnknownFailsAfterOneConfirm()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Unknown, ControlTypeInteractionState.Unknown));
            var transition = CreateShopRestoreTransition(detector, events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("dropdown_not_verified", failure);
            Assert.Equal(2, events.FindAll(entry => entry == "Button:Confirm:True").Count);
            Assert.DoesNotContain("Vertical:1", events);
        }

        [Fact]
        public void ControllerRestore_RightLeavingSidebarFocusUnchangedFailsBeforeDropdownConfirm()
        {
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "InventoryKamera-restore-diagnostics-" + Guid.NewGuid().ToString("N"));
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlsSidebarFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlsSidebarFocused));
            try
            {
                var store = new FailureDiagnosticStore(
                    Path.Combine(tempRoot, "failures"),
                    "20261016_000000_000_1234abcd");
                var diagnostics = new GameInputRestorationDiagnostics(
                    store,
                    new FixedForegroundProbe(matches: true));
                var transition = CreateShopRestoreTransition(detector, events, diagnostics);
                var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

                Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

                Assert.Contains("control_type_focus_unchanged", failure);
                Assert.Single(events.FindAll(entry => entry == "Button:Confirm:True"));
                Assert.Single(events.FindAll(entry => entry == "Horizontal:1"));
                Assert.DoesNotContain("Vertical:1", events);

                string trace = File.ReadAllText(
                    Directory.EnumerateFiles(store.RunDirectory, "trace.txt", SearchOption.AllDirectories).Single());
                Assert.Contains("RestoreStep=FocusControlType", trace);
                Assert.Contains("before=ControlsSidebarFocused; action=Right", trace);
                Assert.Contains("after=ControlsSidebarFocused", trace);
                Assert.Contains("foregroundMatchesGame=True", trace);
                Assert.DoesNotContain("RestoreStep=OpenControlTypeDropdown", trace);

                string[] images = Directory
                    .EnumerateFiles(store.RunDirectory, "*.png", SearchOption.AllDirectories)
                    .Select(Path.GetFileName)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(
                    new[]
                    {
                        "01_controls_open_control_type_row.png",
                        "01_controls_open_controls_top.png",
                        "01_controls_open_dropdown.png",
                        "02_after_focus_right_control_type_row.png",
                        "02_after_focus_right_controls_top.png",
                        "02_after_focus_right_dropdown.png",
                    },
                    images);
                Assert.DoesNotContain(images, name =>
                    string.Equals(name, "screen.png", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
        }

        [Fact]
        public void ControllerRestore_RightMakingFocusUnknownFailsBeforeDropdownConfirm()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlsSidebarFocused),
                Interaction(GenshinInputMode.Unknown, ControlTypeInteractionState.Unknown));
            var transition = CreateShopRestoreTransition(detector, events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("control_type_focus_unresolved", failure);
            Assert.Single(events.FindAll(entry => entry == "Horizontal:1"));
            Assert.Single(events.FindAll(entry => entry == "Button:Confirm:True"));
            Assert.DoesNotContain("ControlType:DropdownOpen", events);
            Assert.DoesNotContain("Vertical:1", events);
        }

        [Theory]
        [InlineData((int)ControllerConfirmLayout.BConfirm_ACancel, "B")]
        [InlineData((int)ControllerConfirmLayout.AConfirm_BCancel, "A")]
        public void ControllerRestore_SuccessfulKeyboardSelectionUsesConfiguredSemanticConfirm(
            int layoutValue,
            string expectedXboxButton)
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.PaimonMenu) },
                dropdownOpen: true,
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlsSidebarFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.ControlTypeFocused),
                Interaction(GenshinInputMode.Controller, ControlTypeInteractionState.DropdownOpen));
            var transition = CreateShopRestoreTransition(detector, events);
            var gameNavigator = new GameNavigator(
                new MappingRecordingGameInput(events, (ControllerConfirmLayout)layoutValue),
                _ => { });

            Assert.True(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure), failure);

            Assert.Equal(
                3,
                events.FindAll(entry => entry == $"Xbox:{expectedXboxButton}:True").Count);
            Assert.Contains("Horizontal:1", events);
            Assert.Contains("Vertical:1", events);
        }

        [Fact]
        public void ControllerRestore_UnchangedKnownScreenStopsAfterOneBack()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                },
                dropdownOpen: true);
            var transition = CreateTransition(
                detector, new RecordingSettingsInput(events), new UnusedPaimonDetector(), events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("did not make verified progress", failure);
            Assert.Single(events.FindAll(entry => entry == "Button:Back:True"));
        }

        [Fact]
        public void ControllerRestore_CyclicKnownScreensStopWithoutBlindInputs()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Character),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                },
                dropdownOpen: true);
            var transition = CreateTransition(
                detector, new RecordingSettingsInput(events), new UnusedPaimonDetector(), events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("detected again", failure);
            Assert.Equal(2, events.FindAll(entry => entry == "Button:Back:True").Count);
        }

        [Fact]
        public void ControllerRestore_UnknownScreenSendsNoInput()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Controller, GenshinInputScreen.Unknown) },
                dropdownOpen: true);
            var transition = CreateTransition(
                detector, new RecordingSettingsInput(events), new UnusedPaimonDetector(), events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("not a known safe restoration state", failure);
            Assert.DoesNotContain(events, entry => entry.StartsWith("Button:", StringComparison.Ordinal));
        }

        [Fact]
        public void ControllerRestore_CharacterWithUnknownModeDefersAllInputToRecovery()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[] { Detection(GenshinInputMode.Unknown, GenshinInputScreen.Character) },
                dropdownOpen: true);
            var transition = CreateTransition(
                detector, new RecordingSettingsInput(events), new UnusedPaimonDetector(), events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("manual Recovery is required", failure);
            Assert.DoesNotContain(events, entry => entry.StartsWith("Button:", StringComparison.Ordinal));
        }

        [Fact]
        public void ControllerRestore_NormalizationActionLimitIsBounded()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(
                events,
                new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Character),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Controls),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.FreeRoam),
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Inventory),
                },
                dropdownOpen: true);
            var transition = CreateTransition(
                detector, new RecordingSettingsInput(events), new UnusedPaimonDetector(), events);
            var gameNavigator = new GameNavigator(new RecordingGameInput(events), _ => { });

            Assert.False(transition.TryRestoreKeyboardMouse(gameNavigator, out string failure));

            Assert.Contains("exceeded", failure);
            Assert.Equal(
                GameInputModeNavigator.MaximumNormalizationActions,
                events.FindAll(entry => entry.EndsWith(":True", StringComparison.Ordinal)).Count);
        }

        [Fact]
        public void ExitKeyboardSettings_UsesKeyboardOnlyAfterRestoration()
        {
            var events = new List<string>();
            var detector = new FakeModeDetector(events, Array.Empty<GenshinInputModeDetection>(), true);
            var transition = CreateTransition(
                detector,
                new RecordingSettingsInput(events),
                new UnusedPaimonDetector(),
                events);

            transition.ExitKeyboardSettings();

            Assert.Equal(new[] { "Escape", "Wait:0", "Escape", "Wait:0" }, events);
        }

        private static GameInputModeNavigator CreateTransition(
            IGenshinInputModeDetector detector,
            IGameSettingsInput settingsInput,
            IPaimonMenuDetector paimonDetector,
            List<string> events,
            GameInputRestorationDiagnostics diagnostics = null) =>
            new GameInputModeNavigator(
                detector,
                paimonDetector,
                new BlankCapture(),
                settingsInput,
                ImmediateTiming,
                wait: milliseconds => events.Add("Wait:" + milliseconds),
                diagnostics: diagnostics);

        private static GameInputModeNavigator CreateShopRestoreTransition(
            IGenshinInputModeDetector detector,
            List<string> events,
            GameInputRestorationDiagnostics diagnostics = null)
        {
            var shop = new PaimonMenuTile(
                "Shop", Rectangle.Empty, new Rectangle(100, 100, 100, 100), 1f, 100);
            return CreateTransition(
                detector,
                new RecordingSettingsInput(events),
                new FixedPaimonDetector(new PaimonMenuDetection(new[] { shop }, shop, null, null)),
                events,
                diagnostics);
        }

        private static GenshinInputModeDetection Detection(
            GenshinInputMode mode,
            GenshinInputScreen screen) =>
            new GenshinInputModeDetection(
                mode,
                screen,
                mode == GenshinInputMode.KeyboardMouse ? new[] { "keyboard" } : Array.Empty<string>(),
                mode == GenshinInputMode.Controller ? new[] { "controller" } : Array.Empty<string>(),
                "test");

        private static ControlTypeInteractionDetection Interaction(
            GenshinInputMode mode,
            ControlTypeInteractionState state,
            bool controllerSelected = true)
        {
            bool open = state == ControlTypeInteractionState.DropdownOpen;
            bool focused = state == ControlTypeInteractionState.ControlTypeFocused || open;
            GenshinInputScreen screen = state == ControlTypeInteractionState.Unknown
                ? GenshinInputScreen.Unknown
                : GenshinInputScreen.Controls;
            return new ControlTypeInteractionDetection(
                Detection(mode, screen),
                new ControlTypeDropdownDetection(
                    open,
                    open,
                    open && controllerSelected,
                    open ? "Keyboard Controller" : string.Empty),
                focused ? 0.05 : 0,
                focused);
        }

        private sealed class FakeModeDetector : IGenshinInputModeDetector
        {
            private readonly Queue<GenshinInputModeDetection> detections;
            private readonly Queue<ControlTypeInteractionDetection> interactions;
            private readonly bool dropdownOpen;
            private readonly List<string> events;
            private GenshinInputModeDetection lastDetection;
            private int generatedControllerInteractions;

            internal FakeModeDetector(
                List<string> events,
                IEnumerable<GenshinInputModeDetection> detections,
                bool dropdownOpen,
                params ControlTypeInteractionDetection[] interactions)
            {
                this.events = events;
                this.detections = new Queue<GenshinInputModeDetection>(detections);
                this.interactions = new Queue<ControlTypeInteractionDetection>(
                    interactions ?? Array.Empty<ControlTypeInteractionDetection>());
                this.dropdownOpen = dropdownOpen;
            }

            public GenshinInputModeDetection Detect()
            {
                GenshinInputModeDetection result = detections.Dequeue();
                lastDetection = result;
                events.Add("Detect:" + result.Mode + ":" + result.Screen);
                return result;
            }

            public ControlTypeInteractionDetection DetectControlTypeInteraction()
            {
                ControlTypeInteractionDetection result;
                if (interactions.Count > 0)
                {
                    result = interactions.Dequeue();
                }
                else if (lastDetection?.Mode == GenshinInputMode.KeyboardMouse)
                {
                    result = Interaction(
                        GenshinInputMode.KeyboardMouse,
                        dropdownOpen
                            ? ControlTypeInteractionState.DropdownOpen
                            : ControlTypeInteractionState.ControlsSidebarFocused,
                        controllerSelected: false);
                }
                else
                {
                    generatedControllerInteractions++;
                    result = Interaction(
                        GenshinInputMode.Controller,
                        generatedControllerInteractions == 1 || !dropdownOpen
                            ? ControlTypeInteractionState.ControlTypeFocused
                            : ControlTypeInteractionState.DropdownOpen);
                }

                events.Add("ControlType:" + result.State);
                return result;
            }

            public ControlTypeInteractionDetection DetectControlTypeInteraction(Bitmap screenshot) =>
                DetectControlTypeInteraction();
        }

        private sealed class RecordingSettingsInput : IGameSettingsInput
        {
            private readonly List<string> events;

            internal RecordingSettingsInput(List<string> events)
            {
                this.events = events;
            }

            public void PressEscape() => events.Add("Escape");
        }

        private sealed class RecordingGameInput : IGameInput
        {
            private readonly List<string> events;

            internal RecordingGameInput(List<string> events)
            {
                this.events = events;
            }

            public bool IsAvailable => true;
            public string FailureReason => null;
            public void SetLeftStickHorizontal(float value) => events.Add("Horizontal:" + value);
            public void SetLeftStickVertical(float value) => events.Add("Vertical:" + value);
            public void SetButtonState(GameInputButton button, bool isPressed) =>
                events.Add($"Button:{button}:{isPressed}");
            public void MoveMouseBy(int x, int y) => events.Add($"Mouse:{x}:{y}");
            public void Dispose() => events.Add("Dispose");
        }

        private sealed class MappingRecordingGameInput : IGameInput
        {
            private readonly List<string> events;
            private readonly ControllerButtonMapping mapping;

            internal MappingRecordingGameInput(
                List<string> events,
                ControllerConfirmLayout layout)
            {
                this.events = events;
                mapping = new ControllerButtonMapping(layout);
            }

            public bool IsAvailable => true;
            public string FailureReason => null;
            public void SetLeftStickHorizontal(float value) => events.Add("Horizontal:" + value);
            public void SetLeftStickVertical(float value) => events.Add("Vertical:" + value);
            public void SetButtonState(GameInputButton button, bool isPressed) =>
                events.Add($"Xbox:{mapping.ToXbox360Button(button)}:{isPressed}");
            public void MoveMouseBy(int x, int y) => events.Add($"Mouse:{x}:{y}");
            public void Dispose() => events.Add("Dispose");
        }

        private sealed class FixedPaimonDetector : IPaimonMenuDetector
        {
            private readonly PaimonMenuDetection detection;

            internal FixedPaimonDetector(PaimonMenuDetection detection)
            {
                this.detection = detection;
            }

            public PaimonMenuDetection Detect(Bitmap screenshot) => detection;
        }

        private sealed class QueuePaimonDetector : IPaimonMenuDetector
        {
            private readonly Queue<PaimonMenuDetection> detections;

            internal QueuePaimonDetector(params PaimonMenuDetection[] detections)
            {
                this.detections = new Queue<PaimonMenuDetection>(detections);
            }

            public PaimonMenuDetection Detect(Bitmap screenshot) => detections.Dequeue();
        }

        private sealed class UnusedPaimonDetector : IPaimonMenuDetector
        {
            public PaimonMenuDetection Detect(Bitmap screenshot) => throw new NotSupportedException();
        }

        private sealed class BlankCapture : IGameScreenCapture
        {
            public Bitmap CaptureWindow() => new Bitmap(32, 32);
        }

        private sealed class FixedForegroundProbe : IGameForegroundProbe
        {
            private readonly bool matches;

            internal FixedForegroundProbe(bool matches)
            {
                this.matches = matches;
            }

            public GameForegroundStatus Capture() =>
                new GameForegroundStatus(
                    new IntPtr(0x1234),
                    100,
                    "GenshinImpact",
                    new IntPtr(0x1234),
                    100,
                    "GenshinImpact",
                    matches);
        }
    }
}
