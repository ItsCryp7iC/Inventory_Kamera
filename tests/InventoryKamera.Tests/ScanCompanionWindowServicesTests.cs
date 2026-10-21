using InventoryKamera.game;
using InventoryKamera.ui;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class ScanCompanionWindowServicesTests
    {
        [Fact]
        public void Placement_UsesGenshinBoundsInsteadOfMainFormMonitor()
        {
            var mainFormOnMonitorTwo = new Rectangle(2200, 100, 900, 700);
            var genshinOnMonitorOne = new Rectangle(0, 0, 1920, 1080);

            Point result = OverlayPlacement.Calculate(genshinOnMonitorOne, new Size(352, 242), null);

            Assert.True(genshinOnMonitorOne.Contains(result));
            Assert.False(mainFormOnMonitorTwo.Contains(result));
            Assert.Equal(new Point(1552, 16), result);
        }

        [Fact]
        public void Placement_FollowsGameToAnotherMonitorAtSameRelativePosition()
        {
            var relative = new OverlayRelativePosition(0.25, 0.40);

            Point result = OverlayPlacement.Calculate(
                new Rectangle(1920, 50, 2560, 1440), new Size(352, 242), relative);

            Assert.Equal(new Point(2560, 626), result);
        }

        [Fact]
        public void Placement_ResolutionChangeMaintainsNormalizedPosition()
        {
            var firstBounds = new Rectangle(100, 50, 1920, 1080);
            var saved = OverlayPlacement.ToRelative(firstBounds, new Point(1060, 320));

            Point result = OverlayPlacement.Calculate(
                new Rectangle(100, 50, 1280, 720), new Size(300, 200), saved);

            Assert.Equal(new Point(740, 230), result);
        }

        [Fact]
        public void Placement_DraggedLocationIsClampedInsideClient()
        {
            Rectangle game = new Rectangle(50, 75, 1280, 720);
            var outside = new OverlayRelativePosition(1, 1);

            Point result = OverlayPlacement.Calculate(game, new Size(352, 366), outside);

            Assert.Equal(new Point(978, 429), result);
        }

        [Theory]
        [InlineData(double.NaN, 0.5)]
        [InlineData(-0.1, 0.5)]
        [InlineData(0.5, 1.1)]
        public void Placement_InvalidSavedPositionUsesSafeTopRightDefault(double x, double y)
        {
            Point result = OverlayPlacement.Calculate(
                new Rectangle(300, 200, 1600, 900),
                new Size(352, 242),
                new OverlayRelativePosition(x, y));

            Assert.Equal(new Point(1532, 216), result);
        }

        [Fact]
        public void NonActivatingPresenter_ShowAndRestoreAlwaysRequestNoActivation()
        {
            var native = new RecordingOverlayNativeApi();
            var presenter = new NonActivatingOverlayPresenter(native);
            var bounds = new Rectangle(10, 20, 300, 200);

            presenter.Show(new IntPtr(7), bounds);
            presenter.Show(new IntPtr(7), bounds);

            Assert.Equal(
                new[] { "ShowNoActivate", "PositionNoActivate", "ShowNoActivate", "PositionNoActivate" },
                native.Events);
            Assert.Equal(0, native.ForegroundRequestCount);
        }

        [Fact]
        public void InteractionMode_PassiveIsClickThroughAndWaitingRecoveryAreInteractive()
        {
            Assert.Equal(OverlayInteractionMode.Passive,
                OverlayInteractionPolicy.ForState(ScanCompanionState.Scanning));
            Assert.Equal(OverlayInteractionMode.Passive,
                OverlayInteractionPolicy.ForState(ScanCompanionState.Cancelling));
            Assert.Equal(OverlayInteractionMode.Passive,
                OverlayInteractionPolicy.ForState(ScanCompanionState.RestoringKeyboard));
            Assert.Equal(OverlayInteractionMode.Passive,
                OverlayInteractionPolicy.ForState(ScanCompanionState.Completed));
            Assert.Equal(OverlayInteractionMode.Passive,
                OverlayInteractionPolicy.ForState(ScanCompanionState.WaitingForAchievementScreen));
            Assert.Equal(OverlayInteractionMode.Interactive,
                OverlayInteractionPolicy.ForState(ScanCompanionState.WaitingForController));
            Assert.Equal(OverlayInteractionMode.Interactive,
                OverlayInteractionPolicy.ForState(ScanCompanionState.Recovery));
        }

        [Fact]
        public void NonActivatingPresenter_StateTransitionTogglesClickThrough()
        {
            var native = new RecordingOverlayNativeApi();
            var presenter = new NonActivatingOverlayPresenter(native);

            presenter.SetInteractionMode(new IntPtr(7), OverlayInteractionMode.Passive);
            presenter.SetInteractionMode(new IntPtr(7), OverlayInteractionMode.Interactive);

            Assert.Equal(new[] { true, false }, native.ClickThroughValues);
        }

        [Fact]
        public void CaptureRestore_ShowNoActivatePreservesInteractiveRecoveryStyle()
        {
            var native = new RecordingOverlayNativeApi();
            var presenter = new NonActivatingOverlayPresenter(native);

            presenter.SetInteractionMode(new IntPtr(7), OverlayInteractionMode.Interactive);
            presenter.Show(new IntPtr(7), new Rectangle(10, 20, 352, 414));

            Assert.False(OverlayWindowBehavior.IsClickThrough(
                presenter.GetExtendedStyles(new IntPtr(7))));
            Assert.Equal(new[] { "ShowNoActivate", "PositionNoActivate" }, native.Events);
            Assert.Equal(0, native.ForegroundRequestCount);
        }

        [Fact]
        public void MouseActivate_NoActivatePermitsClickDeliveryButNoActivateAndEatDoesNot()
        {
            Assert.True(OverlayWindowBehavior.MouseActivatePermitsDelivery(
                OverlayWindowBehavior.MaNoActivate));
            Assert.False(OverlayWindowBehavior.MouseActivatePermitsDelivery(
                OverlayWindowBehavior.MaNoActivateAndEat));
        }

        [Theory]
        [InlineData((int)ScanCompanionState.Scanning, true)]
        [InlineData((int)ScanCompanionState.WaitingForController, false)]
        [InlineData((int)ScanCompanionState.Recovery, false)]
        [InlineData((int)ScanCompanionState.Completed, true)]
        public void StatePolicy_AppliesExpectedTransparentExtendedStyle(
            int stateValue,
            bool expectedTransparent)
        {
            var state = (ScanCompanionState)stateValue;
            OverlayInteractionMode mode = OverlayInteractionPolicy.ForState(state);
            long styles = OverlayWindowBehavior.ApplyInteractionStyle(0x08000080, mode);

            Assert.Equal(expectedTransparent, OverlayWindowBehavior.IsClickThrough(styles));
            Assert.Equal(expectedTransparent, OverlayWindowBehavior.HitTestIsTransparent(mode));
        }

        [Fact]
        public void ExplicitConfirmation_RemainsInteractiveRecoverySubstate()
        {
            OverlayInteractionMode mode = OverlayInteractionPolicy.ForState(ScanCompanionState.Recovery);
            long styles = OverlayWindowBehavior.ApplyInteractionStyle(
                OverlayWindowBehavior.WsExTransparent,
                mode);

            Assert.False(OverlayWindowBehavior.IsClickThrough(styles));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        public void RecoveryHeight_UsesActualScaledContentBottom(double dpiScale)
        {
            int manualConfirmBottom = (int)Math.Round(342 * dpiScale);
            int shortcutBottom = (int)Math.Round(414 * dpiScale);
            int padding = (int)Math.Round(14 * dpiScale);

            int height = OverlayContentSizing.RequiredClientHeight(
                padding,
                manualConfirmBottom,
                shortcutBottom);

            Assert.Equal(shortcutBottom + padding, height);
            Assert.True(height > manualConfirmBottom);
        }

        [Fact]
        public void RecoveryLayout_ActualControlsFitAndManualConfirmationPrecedesShortcuts()
        {
            Exception failure = null;
            bool contentFits = false;
            bool correctOrder = false;
            Size clientSize = Size.Empty;
            Rectangle manualBounds = Rectangle.Empty;
            Rectangle shortcutBounds = Rectangle.Empty;
            var thread = new Thread(() =>
            {
                try
                {
                    var native = new RecordingOverlayNativeApi();
                    var hotkeys = new RecordingHotkeyRegistrar();
                    using var overlay = new ScanCompanionOverlay(
                        new ScanViewModel(),
                        new RecordingGameWindow(new List<string>()),
                        native,
                        hotkeys);
                    overlay.ShowRecovery(new RecordingRecoveryController(), "test restoration failure");
                    contentFits = overlay.RecoveryContentFits;
                    correctOrder = overlay.ManualConfirmationPrecedesShortcuts;
                    clientSize = overlay.ClientSize;
                    manualBounds = overlay.ManualConfirmationBounds;
                    shortcutBounds = overlay.ShortcutLegendBounds;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

            Assert.Null(failure);
            Assert.True(contentFits,
                $"client={clientSize}; manual={manualBounds}; shortcuts={shortcutBounds}");
            Assert.True(correctOrder,
                $"client={clientSize}; manual={manualBounds}; shortcuts={shortcutBounds}");
        }

        [Theory]
        [InlineData("Up", "Up")]
        [InlineData("Left", "Left")]
        [InlineData("Confirm", "Confirm")]
        [InlineData("Back", "Back")]
        [InlineData("Paimon", "OpenMenu")]
        public void RecoveryMouseButtons_InvokeExactlyOneSemanticCallback(
            string buttonSuffix,
            string expectedAction)
        {
            Exception failure = null;
            string[] actions = Array.Empty<string>();
            var thread = new Thread(() =>
            {
                try
                {
                    var controller = new RecordingRecoveryController();
                    using var overlay = new ScanCompanionOverlay(
                        new ScanViewModel(),
                        new RecordingGameWindow(new List<string>()),
                        new RecordingOverlayNativeApi(),
                        new RecordingHotkeyRegistrar());
                    overlay.ShowRecovery(controller, "test restoration failure");
                    OverlayActionButton button = overlay.Controls
                        .OfType<FlowLayoutPanel>()
                        .SelectMany(panel => panel.Controls.OfType<OverlayActionButton>())
                        .Single(candidate => candidate.Text.EndsWith(
                            buttonSuffix,
                            StringComparison.Ordinal));
                    MethodInfo onClick = typeof(Button).GetMethod(
                        "OnClick",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                    onClick.Invoke(button, new object[] { EventArgs.Empty });
                    DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                    while (!controller.ActionObserved.Wait(10) && DateTime.UtcNow < deadline)
                        Application.DoEvents();
                    while (!button.Enabled && DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    actions = controller.Actions.ToArray();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

            Assert.Null(failure);
            Assert.Equal(new[] { expectedAction }, actions);
        }

        [Theory]
        [InlineData("GetForegroundWindowNative", "GetForegroundWindow")]
        [InlineData("IsIconicNative", "IsIconic")]
        [InlineData("SetForegroundWindowNative", "SetForegroundWindow")]
        public void ForegroundNativeImports_TargetRealUser32Exports(string methodName, string entryPoint)
        {
            MethodInfo method = typeof(WindowsGenshinWindowNativeApi).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            DllImportAttribute import = method.GetCustomAttribute<DllImportAttribute>();

            Assert.Equal(entryPoint, import.EntryPoint);
        }

        [Fact]
        public void ReadyGate_AcquiresGenshinBeforeReleasingScannerWait()
        {
            var events = new List<string>();
            var window = new RecordingGameWindow(events) { BringToForegroundResult = true };
            var gate = new ScanReadyFocusGate(window, wait: _ => { }, settleMilliseconds: 0);

            Assert.True(gate.TryRelease(() => events.Add("ReleaseScan")));

            Assert.Equal(new[] { "Foreground", "ReleaseScan" }, events);
        }

        [Fact]
        public void ReadyGate_DoesNotReleaseWhenForegroundCannotBeAcquired()
        {
            var events = new List<string>();
            var window = new RecordingGameWindow(events) { BringToForegroundResult = false };
            var gate = new ScanReadyFocusGate(window, wait: _ => { }, settleMilliseconds: 0);

            Assert.False(gate.TryRelease(() => events.Add("ReleaseScan")));

            Assert.Equal(new[] { "Foreground" }, events);
        }

        [Fact]
        public void ForegroundController_DoesNotCallSetForegroundWhenGameIsAlreadyForeground()
        {
            var native = new RecordingGenshinNativeApi { Foreground = new IntPtr(42) };
            var controller = new WindowsGenshinWindowController(() => new IntPtr(42), native, _ => { });

            Assert.True(controller.TryBringToForeground(TimeSpan.FromMilliseconds(100)));
            Assert.Equal(0, native.SetForegroundCount);
        }

        [Fact]
        public void ForegroundController_BoundsFocusAttemptAndVerifiesResult()
        {
            var native = new RecordingGenshinNativeApi
            {
                Foreground = new IntPtr(1),
                ForegroundAfterSet = new IntPtr(42),
            };
            var controller = new WindowsGenshinWindowController(() => new IntPtr(42), native, _ => { });

            Assert.True(controller.TryBringToForeground(TimeSpan.FromMilliseconds(100)));
            Assert.Equal(1, native.SetForegroundCount);
            Assert.Equal(new IntPtr(42), native.Foreground);
        }

        [Fact]
        public void RecoveryHotkeys_RegisterOnlyDuringRecoveryAndUnregisterIdempotently()
        {
            var registrar = new RecordingHotkeyRegistrar();
            using var coordinator = new RecoveryHotkeyCoordinator(registrar);

            Assert.False(coordinator.IsActive);
            Assert.Empty(registrar.Registered);

            coordinator.EnterRecovery(new IntPtr(9), _ => { });
            Assert.True(coordinator.IsActive);
            Assert.Equal(8, registrar.Registered.Count);

            coordinator.ExitRecovery();
            coordinator.ExitRecovery();
            Assert.False(coordinator.IsActive);
            Assert.Equal(8, registrar.Unregistered.Count);
        }

        [Fact]
        public void WaitingHotkey_RegistersOnlySpaceAndDispatchesReady()
        {
            var registrar = new RecordingHotkeyRegistrar();
            using var coordinator = new WaitingReadyHotkeyCoordinator(registrar);
            int readyCount = 0;

            coordinator.EnterWaiting(new IntPtr(9), () => readyCount++);

            Assert.True(coordinator.RegistrationAttempted);
            Assert.True(coordinator.RegistrationSucceeded);
            Assert.True(coordinator.IsActive);
            Assert.Equal(Keys.Space, registrar.Registered[WaitingReadyHotkeyCoordinator.HotkeyId]);
            Assert.Single(registrar.Registered);
            Assert.True(coordinator.TryDispatch(WaitingReadyHotkeyCoordinator.HotkeyId));
            Assert.Equal(1, readyCount);
        }

        [Fact]
        public void WaitingHotkey_UnregistersOnExitAndDisposeIdempotently()
        {
            var registrar = new RecordingHotkeyRegistrar();
            var coordinator = new WaitingReadyHotkeyCoordinator(registrar);
            coordinator.EnterWaiting(new IntPtr(9), () => { });

            coordinator.ExitWaiting();
            coordinator.ExitWaiting();
            coordinator.Dispose();

            Assert.False(coordinator.IsActive);
            Assert.Equal(new[] { WaitingReadyHotkeyCoordinator.HotkeyId }, registrar.Unregistered);
        }

        [Fact]
        public void WaitingHotkey_FailedSpaceRegistrationDoesNotThrowOrActivate()
        {
            var registrar = new RecordingHotkeyRegistrar { FailingKey = Keys.Space };
            using var coordinator = new WaitingReadyHotkeyCoordinator(registrar);

            coordinator.EnterWaiting(new IntPtr(9), () => throw new InvalidOperationException());

            Assert.True(coordinator.RegistrationAttempted);
            Assert.False(coordinator.RegistrationSucceeded);
            Assert.False(coordinator.IsActive);
            Assert.Equal(1409, coordinator.RegistrationErrorCode);
            Assert.False(coordinator.TryDispatch(WaitingReadyHotkeyCoordinator.HotkeyId));
        }

        [Fact]
        public void WaitingAndRecoveryHotkeySetsDoNotOverlap()
        {
            var registrar = new RecordingHotkeyRegistrar();
            using var waiting = new WaitingReadyHotkeyCoordinator(registrar);
            using var recovery = new RecoveryHotkeyCoordinator(registrar);
            waiting.EnterWaiting(new IntPtr(9), () => { });

            waiting.ExitWaiting();
            recovery.EnterRecovery(new IntPtr(9), _ => { });

            Assert.Contains(WaitingReadyHotkeyCoordinator.HotkeyId, registrar.Unregistered);
            Assert.DoesNotContain(Keys.Space, recovery.RegistrationResults.Select(result => result.Key));
            Assert.Contains(Keys.Enter, recovery.RegistrationResults.Select(result => result.Key));
        }

        [Fact]
        public void WaitingSpace_UsesReadyFocusPathExactlyOnceAndIsRemovedBeforeScanning()
        {
            ReadyPathResult result = RunOverlayReadyPath(useSpace: true, failSpaceRegistration: false);

            Assert.True(result.WaitResult);
            Assert.Equal(new[] { "Foreground" }, result.ForegroundEvents);
            Assert.Equal(new[] { Keys.Space }, result.KeysWhileWaiting);
            Assert.Contains(WaitingReadyHotkeyCoordinator.HotkeyId, result.Unregistered);
            Assert.Equal(0, result.ScanningCallbackCountWhileWaiting);
            Assert.Equal(1, result.ScanningCallbackCount);
            Assert.Equal("Ready to Scan (Space)", result.ReadyButtonText);
            Assert.Contains("Press SPACE when ready", result.WaitingInstructionText);
            Assert.True(result.WaitingContentFits);
            Assert.True(result.ReadyButtonVisible);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        public void WaitingContent_DpiScaledHierarchyFitsWithin1080p(double scale)
        {
            int stateBottom = (int)Math.Round(75 * scale);
            int messageTop = stateBottom + (int)Math.Round(5 * scale);
            int messageBottom = messageTop + (int)Math.Round(112 * scale);
            int buttonTop = messageBottom + (int)Math.Round(12 * scale);
            int buttonBottom = buttonTop + (int)Math.Round(36 * scale);
            int advisoryTop = buttonBottom + (int)Math.Round(7 * scale);
            int advisoryBottom = advisoryTop + (int)Math.Round(34 * scale);
            int clientHeight = OverlayContentSizing.RequiredClientHeight(
                (int)Math.Round(14 * scale),
                advisoryBottom);

            Assert.True(messageBottom < buttonTop);
            Assert.True(buttonBottom < advisoryTop);
            Assert.True(advisoryBottom < clientHeight);
            Assert.True(clientHeight <= 1080);
        }

        [Fact]
        public void WaitingSpaceRegistrationFailure_LeavesMouseReadyFunctionalAndShowsAdvisory()
        {
            ReadyPathResult result = RunOverlayReadyPath(useSpace: false, failSpaceRegistration: true);

            Assert.True(result.WaitResult);
            Assert.Equal(new[] { "Foreground" }, result.ForegroundEvents);
            Assert.True(result.ShortcutUnavailableAdvisory);
            Assert.Empty(result.KeysWhileWaiting);
            Assert.Equal(0, result.ScanningCallbackCountWhileWaiting);
            Assert.Equal(1, result.ScanningCallbackCount);
        }

        [Fact]
        public void WaitingCancellation_UnregistersSpaceWithoutStartingScanning()
        {
            Exception failure = null;
            bool waitResult = true;
            int scanningCallbacks = 0;
            int[] unregistered = Array.Empty<int>();
            var thread = new Thread(() =>
            {
                try
                {
                    var registrar = new RecordingHotkeyRegistrar();
                    using var cancellation = new CancellationTokenSource();
                    using var overlay = new ScanCompanionOverlay(
                        new ScanViewModel(),
                        new RecordingGameWindow(new List<string>()),
                        new RecordingOverlayNativeApi(),
                        registrar,
                        onScanning: () => scanningCallbacks++);
                    overlay.ShowAttachedToGame();
                    Task<bool> waiting = Task.Run(() =>
                        overlay.WaitForControllerReady(cancellation.Token));
                    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                    while (overlay.State != ScanCompanionState.WaitingForController &&
                        DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }

                    cancellation.Cancel();
                    while (!waiting.IsCompleted && DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    waitResult = waiting.GetAwaiter().GetResult();
                    unregistered = registrar.Unregistered.ToArray();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

            Assert.Null(failure);
            Assert.False(waitResult);
            Assert.Equal(0, scanningCallbacks);
            Assert.Contains(WaitingReadyHotkeyCoordinator.HotkeyId, unregistered);
        }

        [Fact]
        public void RecoveryHotkeys_ReenterRegistersOneFreshSet()
        {
            var registrar = new RecordingHotkeyRegistrar();
            using var coordinator = new RecoveryHotkeyCoordinator(registrar);

            coordinator.EnterRecovery(new IntPtr(9), _ => { });
            coordinator.EnterRecovery(new IntPtr(9), _ => { });

            Assert.Equal(16, registrar.RegistrationAttempts);
            Assert.Equal(8, coordinator.RegisteredCount);
            Assert.Equal(8, registrar.Unregistered.Count);
        }

        [Theory]
        [InlineData(Keys.Up, (int)RecoveryHotkeyCommand.Up)]
        [InlineData(Keys.Down, (int)RecoveryHotkeyCommand.Down)]
        [InlineData(Keys.Left, (int)RecoveryHotkeyCommand.Left)]
        [InlineData(Keys.Right, (int)RecoveryHotkeyCommand.Right)]
        [InlineData(Keys.Enter, (int)RecoveryHotkeyCommand.Confirm)]
        [InlineData(Keys.Back, (int)RecoveryHotkeyCommand.Back)]
        [InlineData(Keys.P, (int)RecoveryHotkeyCommand.OpenMenu)]
        [InlineData(Keys.R, (int)RecoveryHotkeyCommand.Retry)]
        public void RecoveryHotkeys_MapToSemanticCommands(Keys key, int expected)
        {
            var registrar = new RecordingHotkeyRegistrar();
            using var coordinator = new RecoveryHotkeyCoordinator(registrar);
            RecoveryHotkeyCommand? dispatched = null;
            coordinator.EnterRecovery(new IntPtr(9), command => dispatched = command);
            int id = registrar.Registered.Single(pair => pair.Value == key).Key;

            Assert.True(coordinator.TryDispatch(id));
            Assert.Equal((RecoveryHotkeyCommand)expected, dispatched);
        }

        [Fact]
        public void RecoveryEnterHotkeyMapsToConfirmNotScanCancellation()
        {
            var registrar = new RecordingHotkeyRegistrar();
            using var coordinator = new RecoveryHotkeyCoordinator(registrar);
            var commands = new List<RecoveryHotkeyCommand>();
            coordinator.EnterRecovery(new IntPtr(9), commands.Add);

            int enterId = registrar.Registered.Single(pair => pair.Value == Keys.Enter).Key;
            coordinator.TryDispatch(enterId);

            Assert.Equal(new[] { RecoveryHotkeyCommand.Confirm }, commands);
        }

        [Fact]
        public void FailedHotkeyRegistrationLeavesRemainingRecoveryHotkeysAvailable()
        {
            var registrar = new RecordingHotkeyRegistrar { FailingKey = Keys.Enter };
            using var coordinator = new RecoveryHotkeyCoordinator(registrar);

            coordinator.EnterRecovery(new IntPtr(9), _ => { });

            Assert.True(coordinator.IsActive);
            Assert.Equal(7, coordinator.RegisteredCount);
            Assert.DoesNotContain(Keys.Enter, registrar.Registered.Values);
            RecoveryHotkeyRegistration failure = Assert.Single(
                coordinator.RegistrationResults.Where(result => !result.Succeeded));
            Assert.Equal(1409, failure.ErrorCode);
        }

        [Fact]
        public void RecoveryActionGate_SerializesAndDebouncesMouseAndKeyboardActions()
        {
            var gate = new RecoveryActionGate(250);

            Assert.True(gate.IsAvailable);
            Assert.True(gate.TryEnter(1000));
            Assert.False(gate.IsAvailable);
            Assert.False(gate.TryEnter(1000));
            gate.Exit(1100);
            Assert.True(gate.IsAvailable);
            Assert.False(gate.TryEnter(1200));
            Assert.True(gate.TryEnter(1350));
            gate.Exit(1400);
            Assert.True(gate.IsAvailable);
        }

        [Fact]
        public void RecoveryActionGate_ReleasesAfterExceptionAndAllowsNextSequentialAction()
        {
            var gate = new RecoveryActionGate(0);
            Assert.True(gate.TryEnter(100));
            try
            {
                throw new InvalidOperationException("test");
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                gate.Exit(101);
            }

            Assert.True(gate.IsAvailable);
            Assert.True(gate.TryEnter(102));
            gate.Exit(103);
        }

        private sealed class RecordingOverlayNativeApi : IOverlayWindowNativeApi
        {
            internal List<string> Events { get; } = new();
            internal List<bool> ClickThroughValues { get; } = new();
            internal int ForegroundRequestCount { get; private set; }
            public void ShowNoActivate(IntPtr window) => Events.Add("ShowNoActivate");
            public void PositionNoActivate(IntPtr window, Rectangle bounds) => Events.Add("PositionNoActivate");
            private long extendedStyles;
            public void SetClickThrough(IntPtr window, bool enabled)
            {
                ClickThroughValues.Add(enabled);
                extendedStyles = OverlayWindowBehavior.ApplyInteractionStyle(
                    extendedStyles,
                    enabled ? OverlayInteractionMode.Passive : OverlayInteractionMode.Interactive);
            }
            public long GetExtendedStyles(IntPtr window) => extendedStyles;
        }

        private static ReadyPathResult RunOverlayReadyPath(bool useSpace, bool failSpaceRegistration)
        {
            ReadyPathResult result = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var foregroundEvents = new List<string>();
                    var gameWindow = new RecordingGameWindow(foregroundEvents)
                    {
                        BringToForegroundResult = true,
                    };
                    var registrar = new RecordingHotkeyRegistrar
                    {
                        FailingKey = failSpaceRegistration ? Keys.Space : null,
                    };
                    int scanningCallbacks = 0;
                    using var overlay = new ScanCompanionOverlay(
                        new ScanViewModel(),
                        gameWindow,
                        new RecordingOverlayNativeApi(),
                        registrar,
                        onScanning: () => scanningCallbacks++);
                    overlay.ShowAttachedToGame();
                    Task<bool> waiting = Task.Run(() =>
                        overlay.WaitForControllerReady(CancellationToken.None));
                    DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                    while (overlay.State != ScanCompanionState.WaitingForController &&
                        DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }

                    Keys[] keysWhileWaiting = registrar.Registered.Values.ToArray();
                    int scanningCallbacksWhileWaiting = scanningCallbacks;
                    string readyButtonText = overlay.ReadyButtonText;
                    string waitingInstructionText = overlay.WaitingInstructionText;
                    bool waitingContentFits = overlay.WaitingContentFits;
                    bool readyButtonVisible = overlay.Controls
                        .OfType<OverlayActionButton>()
                        .Single(button => button.Text == "Ready to Scan (Space)")
                        .Visible;
                    bool advisory = overlay.Controls.OfType<Label>().Any(label =>
                        label.Text.StartsWith("Space shortcut unavailable", StringComparison.Ordinal));
                    if (useSpace)
                    {
                        MethodInfo wndProc = typeof(ScanCompanionOverlay).GetMethod(
                            "WndProc",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        var message = Message.Create(
                            overlay.Handle,
                            0x0312,
                            new IntPtr(WaitingReadyHotkeyCoordinator.HotkeyId),
                            IntPtr.Zero);
                        wndProc.Invoke(overlay, new object[] { message });
                        wndProc.Invoke(overlay, new object[] { message });
                    }
                    else
                    {
                        OverlayActionButton readyButton = overlay.Controls
                            .OfType<OverlayActionButton>()
                            .Single(button => button.Text == "Ready to Scan (Space)");
                        MethodInfo onClick = typeof(Button).GetMethod(
                            "OnClick",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                        onClick.Invoke(readyButton, new object[] { EventArgs.Empty });
                    }

                    while (!waiting.IsCompleted && DateTime.UtcNow < deadline)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    bool waitResult = waiting.GetAwaiter().GetResult();
                    overlay.ShowScanning();
                    result = new ReadyPathResult
                    {
                        WaitResult = waitResult,
                        ForegroundEvents = foregroundEvents.ToArray(),
                        KeysWhileWaiting = keysWhileWaiting,
                        Unregistered = registrar.Unregistered.ToArray(),
                        ShortcutUnavailableAdvisory = advisory,
                        ScanningCallbackCountWhileWaiting = scanningCallbacksWhileWaiting,
                        ScanningCallbackCount = scanningCallbacks,
                        ReadyButtonText = readyButtonText,
                        WaitingInstructionText = waitingInstructionText,
                        WaitingContentFits = waitingContentFits,
                        ReadyButtonVisible = readyButtonVisible,
                    };
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.Null(failure);
            return result;
        }

        private sealed class ReadyPathResult
        {
            internal bool WaitResult { get; set; }
            internal string[] ForegroundEvents { get; set; }
            internal Keys[] KeysWhileWaiting { get; set; }
            internal int[] Unregistered { get; set; }
            internal bool ShortcutUnavailableAdvisory { get; set; }
            internal int ScanningCallbackCountWhileWaiting { get; set; }
            internal int ScanningCallbackCount { get; set; }
            internal string ReadyButtonText { get; set; }
            internal string WaitingInstructionText { get; set; }
            internal bool WaitingContentFits { get; set; }
            internal bool ReadyButtonVisible { get; set; }
        }

        private sealed class RecordingGameWindow : IGenshinWindowController
        {
            private readonly List<string> events;
            internal RecordingGameWindow(List<string> events) => this.events = events;
            internal bool BringToForegroundResult { get; set; }
            public bool IsForeground => false;
            public bool TryGetClientBounds(out Rectangle clientBounds)
            {
                clientBounds = new Rectangle(0, 0, 1920, 1080);
                return true;
            }
            public bool TryBringToForeground(TimeSpan timeout)
            {
                events.Add("Foreground");
                return BringToForegroundResult;
            }
        }

        private sealed class RecordingRecoveryController : IRecoveryInputController
        {
            internal List<string> Actions { get; } = new();
            internal ManualResetEventSlim ActionObserved { get; } = new(false);
            public bool ControllerAvailable => true;
            public bool SessionRecoveryRequired => true;
            public bool ActionGateAvailable => true;
            public bool TryMove(GameNavigator.MenuDirection direction, out string failureReason)
            {
                Record(direction.ToString());
                failureReason = null;
                return true;
            }
            public bool TryConfirm(out string failureReason)
            {
                Record("Confirm");
                failureReason = null;
                return true;
            }
            public bool TryBack(out string failureReason)
            {
                Record("Back");
                failureReason = null;
                return true;
            }
            public bool TryOpenMenu(out string failureReason)
            {
                Record("OpenMenu");
                failureReason = null;
                return true;
            }
            public bool TryRetryAutomaticRestoration(out string failureReason) { failureReason = null; return true; }
            public bool TryConfirmKeyboardMouse(out bool requiresExplicitDisconnect, out string failureReason)
            {
                requiresExplicitDisconnect = false;
                failureReason = null;
                return true;
            }
            public bool TryDisconnectAfterUserConfirmation(out string failureReason)
            {
                failureReason = null;
                return true;
            }

            private void Record(string action)
            {
                Actions.Add(action);
                ActionObserved.Set();
            }
        }

        private sealed class RecordingGenshinNativeApi : IGenshinWindowNativeApi
        {
            internal IntPtr Foreground { get; set; }
            internal IntPtr ForegroundAfterSet { get; set; }
            internal int SetForegroundCount { get; private set; }
            public bool TryGetClientBounds(IntPtr window, out Rectangle bounds)
            {
                bounds = new Rectangle(10, 20, 1920, 1080);
                return true;
            }
            public IntPtr GetForegroundWindow() => Foreground;
            public bool IsIconic(IntPtr window) => false;
            public bool RestoreWindow(IntPtr window) => true;
            public bool SetForegroundWindow(IntPtr window)
            {
                SetForegroundCount++;
                if (ForegroundAfterSet != IntPtr.Zero) Foreground = ForegroundAfterSet;
                return true;
            }
        }

        private sealed class RecordingHotkeyRegistrar : IGlobalHotkeyRegistrar
        {
            internal Dictionary<int, Keys> Registered { get; } = new();
            internal List<int> Unregistered { get; } = new();
            internal Keys? FailingKey { get; set; }
            internal int RegistrationAttempts { get; private set; }
            public bool TryRegister(IntPtr window, int id, Keys key, out int errorCode)
            {
                RegistrationAttempts++;
                if (FailingKey == key)
                {
                    errorCode = 1409;
                    return false;
                }
                errorCode = 0;
                Registered[id] = key;
                return true;
            }
            public void Unregister(IntPtr window, int id) => Unregistered.Add(id);
        }
    }
}
