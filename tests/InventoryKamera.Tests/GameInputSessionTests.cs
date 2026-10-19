using InventoryKamera.game;
using InventoryKamera.ui;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class GameInputSessionTests
    {
        [Fact]
        public void KeyboardDeclaration_BypassesInitialDetectorAndAutomaticControllerNavigation()
        {
            var harness = Harness.Create(DeclaredInputMode.KeyboardMouse);

            Assert.True(harness.Session.Prepare(CancellationToken.None, out string failure), failure);

            Assert.Equal(0, harness.Detector.DetectCount);
            Assert.Equal(1, harness.Overlay.WaitCount);
            Assert.True(harness.Session.ScanActive);
            Assert.Equal(DeclaredInputMode.KeyboardMouse, harness.Session.DeclaredOriginalInputMode);
        }

        [Fact]
        public async Task KeyboardDeclaration_DoesNotStartBeforeReady()
        {
            var overlay = new RecordingOverlay(waitForSignal: true);
            var harness = Harness.Create(DeclaredInputMode.KeyboardMouse, overlay: overlay);

            Task<bool> preparing = Task.Run(() =>
                harness.Session.Prepare(CancellationToken.None, out _));

            Assert.True(overlay.Waiting.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(preparing.IsCompleted);
            Assert.False(harness.Session.ScanActive);

            overlay.SignalReady();

            Assert.True(await preparing);
            Assert.True(harness.Session.ScanActive);
            Assert.Equal(ScanCompanionState.Scanning, overlay.State);
        }

        [Fact]
        public void DetectorUnknown_DoesNotAbortUserConfirmedKeyboardStart()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                detections: new[] { Detection(GenshinInputMode.Unknown) });

            Assert.True(harness.Session.Prepare(CancellationToken.None, out string failure), failure);
            Assert.Equal(0, harness.Detector.DetectCount);
        }

        [Fact]
        public void ControllerDeclaration_BypassesKeyboardPreparationAndDetection()
        {
            var harness = Harness.Create(DeclaredInputMode.Controller);

            Assert.True(harness.Session.Prepare(CancellationToken.None, out string failure), failure);

            Assert.Equal(0, harness.Overlay.WaitCount);
            Assert.Equal(0, harness.Detector.DetectCount);
            Assert.Equal(ScanCompanionState.Scanning, harness.Overlay.State);
        }

        [Fact]
        public void DeclaredMode_RemainsUnchangedAcrossRunAndDetectorDisagreement()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                detections: new[]
                {
                    Detection(GenshinInputMode.Controller),
                    Detection(GenshinInputMode.KeyboardMouse),
                });

            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));
            Assert.True(harness.Session.RestoreAndRelease(out string failure), failure);

            Assert.Equal(DeclaredInputMode.KeyboardMouse, harness.Session.DeclaredOriginalInputMode);
        }

        [Fact]
        public void ControllerOrigin_NeverRestoresKeyboardAndDisposesExactlyOnce()
        {
            var harness = Harness.Create(DeclaredInputMode.Controller);
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.True(harness.Session.RestoreAndRelease(out string failure), failure);
            Assert.True(harness.Session.RestoreAndRelease(out failure), failure);

            Assert.Equal(0, harness.Transition.RestoreCount);
            Assert.Equal(1, harness.Input.DisposeCount);
            Assert.False(harness.Session.RestorationRequired);
        }

        [Fact]
        public void KeyboardOrigin_AlwaysOwnsRestorationResponsibilityWhileConnected()
        {
            var harness = Harness.Create(DeclaredInputMode.KeyboardMouse);
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.True(harness.Session.RestorationRequired);
        }

        [Fact]
        public void KeyboardOrigin_AutomaticRestorationSuccess_ReleasesBackend()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                detections: new[]
                {
                    Detection(GenshinInputMode.Controller),
                    Detection(GenshinInputMode.KeyboardMouse),
                });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.True(harness.Session.RestoreAndRelease(out string failure), failure);

            Assert.Equal(1, harness.Transition.RestoreCount);
            Assert.Equal(1, harness.Input.DisposeCount);
            Assert.False(harness.Session.RecoveryRequired);
        }

        [Fact]
        public void AutomaticRestorationFailure_RetainsViGEmAndShowsRecoveryOverlay()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                restoreSucceeds: false,
                detections: new[] { Detection(GenshinInputMode.Controller) });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.False(harness.Session.RestoreAndRelease(out _));

            Assert.True(harness.Session.RecoveryRequired);
            Assert.True(harness.Session.ViGEmConnected);
            Assert.True(harness.Session.ControllerAvailable);
            Assert.True(harness.Session.SessionRecoveryRequired);
            Assert.True(harness.Session.ActionGateAvailable);
            Assert.Equal(0, harness.Input.DisposeCount);
            Assert.Equal(ScanCompanionState.Recovery, harness.Overlay.State);
            Assert.Same(harness.Session, harness.Overlay.RecoveryController);
        }

        [Fact]
        public void UnknownModeOnRecognizedCharacterScreen_StopsSafelyInRecovery()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                restoreSucceeds: false,
                detections: new[]
                {
                    Detection(GenshinInputMode.Unknown, GenshinInputScreen.Character),
                });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.False(harness.Session.RestoreAndRelease(out _));

            Assert.True(harness.Session.RecoveryRequired);
            Assert.True(harness.Session.ViGEmConnected);
            Assert.Equal(ScanCompanionState.Recovery, harness.Overlay.State);
            Assert.Equal(0, harness.Input.DisposeCount);
        }

        [Fact]
        public void UnknownScreen_StopsSafelyInRecovery()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                restoreSucceeds: false,
                detections: new[]
                {
                    Detection(GenshinInputMode.Controller, GenshinInputScreen.Unknown),
                });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.False(harness.Session.RestoreAndRelease(out _));

            Assert.True(harness.Session.RecoveryRequired);
            Assert.True(harness.Session.ViGEmConnected);
            Assert.Equal(ScanCompanionState.Recovery, harness.Overlay.State);
            Assert.Equal(0, harness.Input.DisposeCount);
        }

        [Theory]
        [InlineData((int)GameNavigator.MenuDirection.Up, "Vertical:1")]
        [InlineData((int)GameNavigator.MenuDirection.Down, "Vertical:-1")]
        [InlineData((int)GameNavigator.MenuDirection.Left, "Horizontal:-1")]
        [InlineData((int)GameNavigator.MenuDirection.Right, "Horizontal:1")]
        public void RecoveryDirections_UseSemanticNavigatorActions(int directionValue, string expected)
        {
            var harness = Harness.InRecovery();

            Assert.True(harness.Session.TryMove((GameNavigator.MenuDirection)directionValue, out string failure), failure);

            Assert.Contains(expected, harness.Input.Events);
            Assert.Contains("Horizontal:0", harness.Input.Events);
            Assert.Contains("Vertical:0", harness.Input.Events);
        }

        [Fact]
        public void RecoveryConfirmBackAndPaimon_UseSemanticActions()
        {
            var harness = Harness.InRecovery();

            Assert.True(harness.Session.TryConfirm(out string confirmFailure), confirmFailure);
            Assert.True(harness.Session.TryBack(out string backFailure), backFailure);
            Assert.True(harness.Session.TryOpenMenu(out string menuFailure), menuFailure);

            Assert.Contains("Button:Confirm:True", harness.Input.Events);
            Assert.Contains("Button:Confirm:False", harness.Input.Events);
            Assert.Contains("Button:Back:True", harness.Input.Events);
            Assert.Contains("Button:Back:False", harness.Input.Events);
            Assert.Contains("Button:Menu:True", harness.Input.Events);
            Assert.Contains("Button:Menu:False", harness.Input.Events);
        }

        [Fact]
        public async Task RecoveryActions_CannotRunConcurrently()
        {
            var input = new RecordingInput { BlockButtonPress = true };
            var harness = Harness.InRecovery(input);

            Task<bool> first = Task.Run(() => harness.Session.TryConfirm(out _));
            Assert.True(input.ButtonPressStarted.Wait(TimeSpan.FromSeconds(2)));

            Assert.False(harness.Session.TryBack(out string failure));
            Assert.Contains("still running", failure);

            input.AllowButtonPress.Set();
            Assert.True(await first);
        }

        [Fact]
        public void Retry_ReusesSameBackendAndDoesNotCreateOrDisposeAnotherController()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                restoreSucceeds: false,
                detections: new[]
                {
                    Detection(GenshinInputMode.Controller),
                    Detection(GenshinInputMode.KeyboardMouse),
                });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));
            Assert.False(harness.Session.RestoreAndRelease(out _));
            harness.Transition.RestoreSucceeds = true;

            Assert.True(harness.Session.TryRetryAutomaticRestoration(out string failure), failure);

            Assert.Equal(1, harness.Input.DisposeCount);
            Assert.False(harness.Session.RecoveryRequired);
        }

        [Fact]
        public void UserConfirmation_PositiveKeyboardDetectionDisconnectsImmediately()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                restoreSucceeds: false,
                detections: new[]
                {
                    Detection(GenshinInputMode.Controller),
                    Detection(GenshinInputMode.KeyboardMouse),
                });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));
            Assert.False(harness.Session.RestoreAndRelease(out _));

            Assert.True(harness.Session.TryConfirmKeyboardMouse(
                out bool requiresExplicitDisconnect, out string failure), failure);

            Assert.False(requiresExplicitDisconnect);
            Assert.Equal(1, harness.Input.DisposeCount);
            Assert.False(harness.Session.RecoveryRequired);
        }

        [Fact]
        public void UserConfirmation_UnknownOffersExplicitDisconnectAndClearsRecovery()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                restoreSucceeds: false,
                detections: new[]
                {
                    Detection(GenshinInputMode.Controller),
                    Detection(GenshinInputMode.Unknown),
                });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));
            Assert.False(harness.Session.RestoreAndRelease(out _));

            Assert.False(harness.Session.TryConfirmKeyboardMouse(
                out bool requiresExplicitDisconnect, out _));
            Assert.True(requiresExplicitDisconnect);
            Assert.True(harness.Session.RecoveryRequired);

            Assert.True(harness.Session.TryDisconnectAfterUserConfirmation(out string failure), failure);
            Assert.Equal(1, harness.Input.DisposeCount);
            Assert.False(harness.Session.RecoveryRequired);
        }

        [Fact]
        public void KeyboardCancellation_UsesRestorationLifecycleWithoutCancelledToken()
        {
            var overlay = new RecordingOverlay(waitForSignal: true);
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                overlay: overlay,
                detections: new[] { Detection(GenshinInputMode.KeyboardMouse) });
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.False(harness.Session.Prepare(cancellation.Token, out _));
            Assert.True(harness.Session.RestoreAndRelease(out string failure), failure);
            Assert.Equal(1, harness.Input.DisposeCount);
        }

        [Fact]
        public void ControllerCancellation_DoesNotRestoreKeyboard()
        {
            var harness = Harness.Create(DeclaredInputMode.Controller);
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.True(harness.Session.RestoreAndRelease(out string failure), failure);

            Assert.Equal(0, harness.Transition.RestoreCount);
            Assert.Equal(1, harness.Input.DisposeCount);
        }

        [Fact]
        public void KeyboardOrigin_ExceptionCleanup_AttemptsRestoreThenUsesRecoveryFallback()
        {
            var harness = Harness.Create(
                DeclaredInputMode.KeyboardMouse,
                restoreSucceeds: false,
                detections: new[] { Detection(GenshinInputMode.Controller) });
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.False(harness.Session.RestoreAndRelease(out _));

            Assert.Equal(1, harness.Transition.RestoreCount);
            Assert.True(harness.Session.RecoveryRequired);
            Assert.Equal(0, harness.Input.DisposeCount);
        }

        [Fact]
        public void ControllerOrigin_ExceptionCleanup_DoesNotRestoreKeyboardOrLeakBackend()
        {
            var harness = Harness.Create(DeclaredInputMode.Controller);
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));

            Assert.True(harness.Session.RestoreAndRelease(out string failure), failure);

            Assert.Equal(0, harness.Transition.RestoreCount);
            Assert.Equal(1, harness.Input.DisposeCount);
            Assert.False(harness.Session.ViGEmConnected);
        }

        [Fact]
        public void CorrectionResume_RefocusesWithoutModeDetection()
        {
            var harness = Harness.Create(DeclaredInputMode.Controller);
            Assert.True(harness.Session.Prepare(CancellationToken.None, out _));
            int detectionsBefore = harness.Detector.DetectCount;
            harness.Activator.LoseForeground();

            Assert.True(harness.Session.TryResumeAfterInteractiveDialog(out string failure), failure);

            Assert.Equal(detectionsBefore, harness.Detector.DetectCount);
            Assert.True(harness.Activator.ActivateCount >= 2);
        }

        [Fact]
        public void RecoveryAction_WhenGenshinAlreadyForeground_DoesNotRequestForegroundAgain()
        {
            var harness = Harness.InRecovery();
            int activationsBefore = harness.Activator.ActivateCount;

            Assert.True(harness.Session.TryMove(GameNavigator.MenuDirection.Up, out string failure), failure);

            Assert.Equal(activationsBefore, harness.Activator.ActivateCount);
        }

        [Fact]
        public void RecoveryAction_WhenGenshinIsNotForeground_ActivatesBeforeSendingInput()
        {
            var harness = Harness.InRecovery();
            harness.Activator.Events.Clear();
            harness.Input.Events.Clear();
            harness.Activator.LoseForeground();
            harness.Activator.OnActivate = () => harness.Input.Events.Add("Foreground");

            Assert.True(harness.Session.TryConfirm(out string failure), failure);

            Assert.Equal("Foreground", harness.Input.Events[0]);
            Assert.Equal("Button:Confirm:True", harness.Input.Events[1]);
            Assert.Single(harness.Activator.Events);
        }

        private static GenshinInputModeDetection Detection(GenshinInputMode mode) =>
            Detection(
                mode,
                mode == GenshinInputMode.Unknown
                    ? GenshinInputScreen.Unknown
                    : GenshinInputScreen.PaimonMenu);

        private static GenshinInputModeDetection Detection(
            GenshinInputMode mode,
            GenshinInputScreen screen) =>
            new GenshinInputModeDetection(
                mode,
                screen,
                Array.Empty<string>(),
                Array.Empty<string>(),
                "test");

        private sealed class Harness
        {
            private Harness(
                RecordingInput input,
                QueueDetector detector,
                FakeTransition transition,
                RecordingOverlay overlay,
                RecordingActivator activator,
                GameInputSession session)
            {
                Input = input;
                Detector = detector;
                Transition = transition;
                Overlay = overlay;
                Activator = activator;
                Session = session;
            }

            internal RecordingInput Input { get; }
            internal QueueDetector Detector { get; }
            internal FakeTransition Transition { get; }
            internal RecordingOverlay Overlay { get; }
            internal RecordingActivator Activator { get; }
            internal GameInputSession Session { get; }

            internal static Harness Create(
                DeclaredInputMode mode,
                RecordingInput input = null,
                RecordingOverlay overlay = null,
                bool restoreSucceeds = true,
                GenshinInputModeDetection[] detections = null)
            {
                input ??= new RecordingInput();
                overlay ??= new RecordingOverlay();
                var detector = new QueueDetector(detections ?? Array.Empty<GenshinInputModeDetection>());
                var transition = new FakeTransition { RestoreSucceeds = restoreSucceeds };
                var activator = new RecordingActivator();
                var session = new GameInputSession(
                    new GameNavigator(input, _ => { }),
                    mode,
                    detector,
                    transition,
                    new ScanViewModel(),
                    overlay,
                    foregroundActivator: activator,
                    wait: _ => { },
                    foregroundSettleMs: 0);
                return new Harness(input, detector, transition, overlay, activator, session);
            }

            internal static Harness InRecovery(RecordingInput input = null)
            {
                var harness = Create(
                    DeclaredInputMode.KeyboardMouse,
                    input,
                    restoreSucceeds: false,
                    detections: new[] { Detection(GenshinInputMode.Controller) });
                Assert.True(harness.Session.Prepare(CancellationToken.None, out _));
                Assert.False(harness.Session.RestoreAndRelease(out _));
                return harness;
            }
        }

        private sealed class QueueDetector : IGenshinInputModeDetector
        {
            private readonly Queue<GenshinInputModeDetection> detections;
            internal QueueDetector(IEnumerable<GenshinInputModeDetection> detections) =>
                this.detections = new Queue<GenshinInputModeDetection>(detections);
            internal int DetectCount { get; private set; }
            public GenshinInputModeDetection Detect()
            {
                DetectCount++;
                return detections.Count == 0 ? Detection(GenshinInputMode.Unknown) : detections.Dequeue();
            }
            public ControlTypeInteractionDetection DetectControlTypeInteraction() => throw new NotSupportedException();
            public ControlTypeInteractionDetection DetectControlTypeInteraction(Bitmap screenshot) => throw new NotSupportedException();
        }

        private sealed class FakeTransition : IGameInputModeTransition
        {
            internal bool RestoreSucceeds { get; set; } = true;
            internal int RestoreCount { get; private set; }
            public bool TryRestoreKeyboardMouse(GameNavigator navigator, out string failureReason)
            {
                RestoreCount++;
                failureReason = RestoreSucceeds ? null : "bounded restoration failed";
                return RestoreSucceeds;
            }
            public void ExitKeyboardSettings() { }
        }

        private sealed class RecordingInput : IGameInput
        {
            internal readonly List<string> Events = new List<string>();
            internal readonly ManualResetEventSlim ButtonPressStarted = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim AllowButtonPress = new ManualResetEventSlim(false);
            internal bool BlockButtonPress { get; set; }
            internal int DisposeCount { get; private set; }
            public bool IsAvailable => DisposeCount == 0;
            public string FailureReason => null;
            public void SetLeftStickHorizontal(float value) => Events.Add("Horizontal:" + value);
            public void SetLeftStickVertical(float value) => Events.Add("Vertical:" + value);
            public void SetButtonState(GameInputButton button, bool isPressed)
            {
                Events.Add($"Button:{button}:{isPressed}");
                if (BlockButtonPress && isPressed)
                {
                    ButtonPressStarted.Set();
                    AllowButtonPress.Wait(TimeSpan.FromSeconds(5));
                }
            }
            public void MoveMouseBy(int x, int y) => Events.Add($"Mouse:{x}:{y}");
            public void Dispose() => DisposeCount++;
        }

        private sealed class RecordingActivator : IGameForegroundActivator
        {
            internal int ActivateCount { get; private set; }
            internal List<string> Events { get; } = new List<string>();
            internal Action OnActivate { get; set; }
            public bool IsForeground { get; private set; }
            internal void LoseForeground() => IsForeground = false;
            public void Activate()
            {
                ActivateCount++;
                Events.Add("Activate");
                IsForeground = true;
                OnActivate?.Invoke();
            }
        }

        private sealed class RecordingOverlay : IScanCompanionOverlay
        {
            private readonly bool waitForSignal;
            private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
            internal RecordingOverlay(bool waitForSignal = false) => this.waitForSignal = waitForSignal;
            internal ManualResetEventSlim Waiting { get; } = new ManualResetEventSlim(false);
            internal int WaitCount { get; private set; }
            internal IRecoveryInputController RecoveryController { get; private set; }
            public ScanCompanionState State { get; private set; }
            internal void SignalReady() => ready.Set();
            public void ShowPreparing(DeclaredInputMode declaredMode) => State = ScanCompanionState.Preparing;
            public bool WaitForControllerReady(CancellationToken cancellationToken)
            {
                WaitCount++;
                State = ScanCompanionState.WaitingForController;
                Waiting.Set();
                if (waitForSignal) WaitHandle.WaitAny(new[] { ready.WaitHandle, cancellationToken.WaitHandle });
                return !cancellationToken.IsCancellationRequested;
            }
            public void ShowScanning() => State = ScanCompanionState.Scanning;
            public void ShowAchievementProgress(string progress, string details) =>
                State = ScanCompanionState.Scanning;
            public void ShowCancelling() => State = ScanCompanionState.Cancelling;
            public void ShowRestoringKeyboard() => State = ScanCompanionState.RestoringKeyboard;
            public void ShowInputRequired() { }
            public void ShowRecovery(IRecoveryInputController controller, string failureReason)
            {
                RecoveryController = controller;
                State = ScanCompanionState.Recovery;
            }
            public void ShowCompleted(string message) => State = ScanCompanionState.Completed;
            public void ShowError(string message) => State = ScanCompanionState.Error;
            public IDisposable SuspendForCapture() => new NoopDisposable();
            public IDisposable SuspendForInteraction() => new NoopDisposable();
            private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
        }
    }
}
