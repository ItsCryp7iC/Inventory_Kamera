using System;
using System.Collections.Generic;
using InventoryKamera.game;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ControllerLifecycleTests
    {
        [Fact]
        public void PreflightCancel_DoesNotActivateGameOrReachControllerAndRunCreation()
        {
            int activations = 0;
            int controllerCreations = 0;
            int scanRunCreations = 0;

            bool proceed = ControllerScanPreflight.TryProceed(
                controllerScanSelected: true,
                confirmRequirements: () => false,
                validateAndActivateGame: () =>
                {
                    activations++;
                    return true;
                });

            if (proceed)
            {
                controllerCreations++;
                scanRunCreations++;
            }

            Assert.False(proceed);
            Assert.Equal(0, activations);
            Assert.Equal(0, controllerCreations);
            Assert.Equal(0, scanRunCreations);
        }

        [Fact]
        public void PreflightAccept_ActivatesGameOnlyAfterDialogReturns()
        {
            var events = new List<string>();

            bool proceed = ControllerScanPreflight.TryProceed(
                controllerScanSelected: true,
                confirmRequirements: () =>
                {
                    events.Add("dialog");
                    return true;
                },
                validateAndActivateGame: () =>
                {
                    events.Add("activate");
                    return true;
                });

            if (proceed)
            {
                events.Add("controller");
                events.Add("scan-run");
            }

            Assert.True(proceed);
            Assert.Equal(new[] { "dialog", "activate", "controller", "scan-run" }, events);
        }

        [Fact]
        public void ConfirmCancelMapping_UsesConfiguredLayouts()
        {
            var defaultMapping = new ControllerButtonMapping(ControllerConfirmLayout.BConfirm_ACancel);
            var alternateMapping = new ControllerButtonMapping(ControllerConfirmLayout.AConfirm_BCancel);

            Assert.Equal(Xbox360Button.B, defaultMapping.ToXbox360Button(GameInputButton.Confirm));
            Assert.Equal(Xbox360Button.A, defaultMapping.ToXbox360Button(GameInputButton.Back));
            Assert.Equal(Xbox360Button.A, alternateMapping.ToXbox360Button(GameInputButton.Confirm));
            Assert.Equal(Xbox360Button.B, alternateMapping.ToXbox360Button(GameInputButton.Back));
        }

        [Fact]
        public void UnknownPersistedLayout_FallsBackToDefaultBConfirmLayout()
        {
            ControllerConfirmLayout layout = ControllerConfirmLayoutOptions.FromPersistedValue(999);
            var mapping = new ControllerButtonMapping(layout);

            Assert.Equal(ControllerConfirmLayout.BConfirm_ACancel, layout);
            Assert.Equal(Xbox360Button.B, mapping.ToXbox360Button(GameInputButton.Confirm));
            Assert.Equal(Xbox360Button.A, mapping.ToXbox360Button(GameInputButton.Back));
        }

        [Fact]
        public void DefaultConfigurationModeForcesBConfirmEvenWhenAlternateValueIsPersisted()
        {
            ControllerConfirmLayout layout = ControllerConfirmLayoutOptions.Resolve(
                useDefault: true,
                persistedValue: (int)ControllerConfirmLayout.AConfirm_BCancel);

            Assert.Equal(ControllerConfirmLayout.BConfirm_ACancel, layout);
        }

        [Fact]
        public void CustomConfigurationModeUsesPersistedAlternateLayout()
        {
            ControllerConfirmLayout layout = ControllerConfirmLayoutOptions.Resolve(
                useDefault: false,
                persistedValue: (int)ControllerConfirmLayout.AConfirm_BCancel);

            Assert.Equal(ControllerConfirmLayout.AConfirm_BCancel, layout);
        }

        [Fact]
        public void NonConfirmMappings_AreUnchanged()
        {
            var expectedMappings = new[]
            {
                (GameInputButton.Menu, Xbox360Button.Start),
                (GameInputButton.DPadDown, Xbox360Button.Down),
                (GameInputButton.DPadLeft, Xbox360Button.Left),
                (GameInputButton.LeftStick, Xbox360Button.LeftThumb),
                (GameInputButton.PreviousTab, Xbox360Button.LeftShoulder),
                (GameInputButton.NextTab, Xbox360Button.RightShoulder)
            };

            foreach (ControllerConfirmLayout layout in Enum.GetValues<ControllerConfirmLayout>())
            {
                var mapping = new ControllerButtonMapping(layout);
                foreach (var expected in expectedMappings)
                    Assert.Equal(expected.Item2, mapping.ToXbox360Button(expected.Item1));
            }
        }

        [Fact]
        public void ViGEmBackend_AppliesSemanticButtonMapping()
        {
            AssertViGEmButton(
                ControllerConfirmLayout.BConfirm_ACancel, GameInputButton.Confirm, Xbox360Button.B);
            AssertViGEmButton(
                ControllerConfirmLayout.BConfirm_ACancel, GameInputButton.Back, Xbox360Button.A);
            AssertViGEmButton(
                ControllerConfirmLayout.AConfirm_BCancel, GameInputButton.Confirm, Xbox360Button.A);
            AssertViGEmButton(
                ControllerConfirmLayout.AConfirm_BCancel, GameInputButton.Back, Xbox360Button.B);
            AssertViGEmButton(
                ControllerConfirmLayout.BConfirm_ACancel, GameInputButton.Search, Xbox360Button.X);
            AssertViGEmButton(
                ControllerConfirmLayout.AConfirm_BCancel, GameInputButton.Search, Xbox360Button.X);
        }

        private static void AssertViGEmButton(
            ControllerConfirmLayout layout,
            GameInputButton action,
            Xbox360Button expectedButton)
        {
            var controller = new FakeViGEmController();
            using var input = new ViGEmGameInput(
                new FakeViGEmClientFactory(new FakeViGEmClient(controller)), layout, _ => { });

            input.SetButtonState(action, true);

            Assert.Equal(expectedButton, controller.LastButton);
        }

        [Fact]
        public void TapButton_WhenWaitThrows_AttemptsReleaseAndPreservesOriginalFailure()
        {
            var input = new RecordingGameInput();
            var navigator = new GameNavigator(input, _ => throw new InvalidOperationException("wait failed"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => navigator.TapConfirm());

            Assert.Equal("wait failed", error.Message);
            Assert.Equal(new[] { "Button:Confirm:True", "Button:Confirm:False" }, input.Calls);
        }

        [Fact]
        public void TapButton_WhenWaitAndReleaseThrow_PreservesWaitFailure()
        {
            var input = new RecordingGameInput { ThrowOnButtonRelease = true };
            var navigator = new GameNavigator(input, _ => throw new InvalidOperationException("wait failed"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => navigator.TapConfirm());

            Assert.Equal("wait failed", error.Message);
            Assert.Equal(new[] { "Button:Confirm:True", "Button:Confirm:False" }, input.Calls);
        }

        [Fact]
        public void TapSearch_WhenWaitThrows_ReleasesSearchAndPreservesOriginalFailure()
        {
            var input = new RecordingGameInput { ThrowOnButtonRelease = true };
            var navigator = new GameNavigator(input, _ => throw new InvalidOperationException("wait failed"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                navigator.TapSearch(90));

            Assert.Equal("wait failed", error.Message);
            Assert.Equal(new[] { "Button:Search:True", "Button:Search:False" }, input.Calls);
        }

        [Fact]
        public void MoveStep_WhenWaitThrows_AttemptsBothAxisCenters()
        {
            var input = new RecordingGameInput();
            var navigator = new GameNavigator(input, _ => throw new InvalidOperationException("wait failed"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                navigator.MoveStep(GameNavigator.MenuDirection.Right));

            Assert.Equal("wait failed", error.Message);
            Assert.Equal(new[] { "Horizontal:1", "Horizontal:0", "Vertical:0" }, input.Calls);
        }

        [Fact]
        public void EnterControllerMode_WhenWaitThrows_AttemptsHorizontalCenter()
        {
            var input = new RecordingGameInput();
            var navigator = new GameNavigator(input, _ => throw new InvalidOperationException("wait failed"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                navigator.EnterControllerMode());

            Assert.Equal("wait failed", error.Message);
            Assert.Equal(new[] { "Horizontal:0.5", "Horizontal:0" }, input.Calls);
        }

        [Fact]
        public void MoveStep_WhenWaitAndFirstCenterThrow_AttemptsSecondCenterAndPreservesWaitFailure()
        {
            var input = new RecordingGameInput { ThrowWhenCenteringHorizontal = true };
            var navigator = new GameNavigator(input, _ => throw new InvalidOperationException("wait failed"));

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                navigator.MoveStep(GameNavigator.MenuDirection.Right));

            Assert.Equal("wait failed", error.Message);
            Assert.Equal(new[] { "Horizontal:1", "Horizontal:0", "Vertical:0" }, input.Calls);
        }

        [Fact]
        public void ViGEmInitializationFailure_CleansPartiallyInitializedResources()
        {
            var controller = new FakeViGEmController();
            var client = new FakeViGEmClient(controller);
            var input = new ViGEmGameInput(
                new FakeViGEmClientFactory(client),
                ControllerConfirmLayout.BConfirm_ACancel,
                _ => throw new InvalidOperationException("enumeration failed"));

            Assert.False(input.IsAvailable);
            Assert.Contains("enumeration failed", input.FailureReason);
            Assert.Equal(1, controller.ConnectCount);
            Assert.Equal(1, controller.DisconnectCount);
            Assert.Equal(1, client.DisposeCount);

            input.Dispose();
            Assert.Equal(1, controller.DisconnectCount);
            Assert.Equal(1, client.DisposeCount);
        }

        [Fact]
        public void ViGEmControllerCreationFailure_DisposesCreatedClient()
        {
            var client = new FakeViGEmClient(null) { ThrowOnCreateController = true };
            var input = new ViGEmGameInput(
                new FakeViGEmClientFactory(client),
                ControllerConfirmLayout.BConfirm_ACancel,
                _ => { });

            Assert.False(input.IsAvailable);
            Assert.Equal(1, client.DisposeCount);

            input.Dispose();
            Assert.Equal(1, client.DisposeCount);
        }

        [Fact]
        public void ViGEmDispose_IsIdempotent()
        {
            var controller = new FakeViGEmController();
            var client = new FakeViGEmClient(controller);
            var input = new ViGEmGameInput(
                new FakeViGEmClientFactory(client),
                ControllerConfirmLayout.BConfirm_ACancel,
                _ => { });

            input.Dispose();
            input.Dispose();

            Assert.Equal(1, controller.ConnectCount);
            Assert.Equal(1, controller.DisconnectCount);
            Assert.Equal(1, client.DisposeCount);
        }

        [Fact]
        public void GameNavigatorDispose_UnavailableInputStillDisposesOwnedBackend()
        {
            var input = new RecordingGameInput { Available = false };
            var navigator = new GameNavigator(input, _ => { });

            navigator.Dispose();

            Assert.Equal(new[] { "Dispose" }, input.Calls);
        }

        private sealed class RecordingGameInput : IGameInput
        {
            internal readonly List<string> Calls = new List<string>();
            internal bool ThrowOnButtonRelease { get; set; }
            internal bool ThrowWhenCenteringHorizontal { get; set; }

            internal bool Available { get; set; } = true;
            public bool IsAvailable => Available;
            public string FailureReason => null;

            public void SetLeftStickHorizontal(float value)
            {
                Calls.Add($"Horizontal:{value:0.###}");
                if (value == 0f && ThrowWhenCenteringHorizontal)
                    throw new InvalidOperationException("horizontal center failed");
            }

            public void SetLeftStickVertical(float value) => Calls.Add($"Vertical:{value:0.###}");

            public void SetButtonState(GameInputButton button, bool isPressed)
            {
                Calls.Add($"Button:{button}:{isPressed}");
                if (!isPressed && ThrowOnButtonRelease)
                    throw new InvalidOperationException("release failed");
            }

            public void MoveMouseBy(int x, int y) => Calls.Add($"Mouse:{x}:{y}");
            public void Dispose() => Calls.Add("Dispose");
        }

        private sealed class FakeViGEmClientFactory : IViGEmClientFactory
        {
            private readonly IViGEmClientResource client;

            internal FakeViGEmClientFactory(IViGEmClientResource client)
            {
                this.client = client;
            }

            public IViGEmClientResource CreateClient() => client;
        }

        private sealed class FakeViGEmClient : IViGEmClientResource
        {
            private readonly IViGEmControllerResource controller;
            internal int DisposeCount { get; private set; }
            internal bool ThrowOnCreateController { get; set; }

            internal FakeViGEmClient(IViGEmControllerResource controller)
            {
                this.controller = controller;
            }

            public IViGEmControllerResource CreateXbox360Controller()
            {
                if (ThrowOnCreateController) throw new InvalidOperationException("controller creation failed");
                return controller;
            }
            public void Dispose() => DisposeCount++;
        }

        private sealed class FakeViGEmController : IViGEmControllerResource
        {
            internal int ConnectCount { get; private set; }
            internal int DisconnectCount { get; private set; }
            internal Xbox360Button LastButton { get; private set; }

            public void Connect() => ConnectCount++;
            public void Disconnect() => DisconnectCount++;
            public void SetAxisValue(Xbox360Axis axis, short value) { }
            public void SetButtonState(Xbox360Button button, bool isPressed) => LastButton = button;
        }
    }
}
