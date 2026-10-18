using System;
using System.Threading;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using WindowsInput;

namespace InventoryKamera.game
{
    /// <summary>
    /// ViGEm-specific translation from semantic menu actions to the configured Xbox buttons.
    /// Non-confirm/cancel controls intentionally retain their existing mappings.
    /// </summary>
    internal sealed class ControllerButtonMapping
    {
        private readonly ControllerConfirmLayout layout;

        internal ControllerButtonMapping(ControllerConfirmLayout layout)
        {
            this.layout = layout;
        }

        internal Xbox360Button ToXbox360Button(GameInputButton button)
        {
            switch (button)
            {
                case GameInputButton.Back:
                    return layout == ControllerConfirmLayout.AConfirm_BCancel
                        ? Xbox360Button.B
                        : Xbox360Button.A;
                case GameInputButton.Confirm:
                    return layout == ControllerConfirmLayout.AConfirm_BCancel
                        ? Xbox360Button.A
                        : Xbox360Button.B;
                case GameInputButton.Search:
                    return Xbox360Button.X;
                case GameInputButton.Menu:
                    return Xbox360Button.Start;
                case GameInputButton.DPadDown:
                    return Xbox360Button.Down;
                case GameInputButton.DPadLeft:
                    return Xbox360Button.Left;
                case GameInputButton.LeftStick:
                    return Xbox360Button.LeftThumb;
                case GameInputButton.PreviousTab:
                    return Xbox360Button.LeftShoulder;
                case GameInputButton.NextTab:
                    return Xbox360Button.RightShoulder;
                default:
                    throw new ArgumentOutOfRangeException(nameof(button), button, null);
            }
        }
    }

    internal interface IViGEmClientFactory
    {
        IViGEmClientResource CreateClient();
    }

    internal interface IViGEmClientResource : IDisposable
    {
        IViGEmControllerResource CreateXbox360Controller();
    }

    internal interface IViGEmControllerResource
    {
        void Connect();
        void Disconnect();
        void SetAxisValue(Xbox360Axis axis, short value);
        void SetButtonState(Xbox360Button button, bool isPressed);
    }

    internal sealed class NefariusViGEmClientFactory : IViGEmClientFactory
    {
        public IViGEmClientResource CreateClient() => new NefariusViGEmClientResource(new ViGEmClient());
    }

    internal sealed class NefariusViGEmClientResource : IViGEmClientResource
    {
        private readonly ViGEmClient client;

        internal NefariusViGEmClientResource(ViGEmClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public IViGEmControllerResource CreateXbox360Controller() =>
            new NefariusViGEmControllerResource(client.CreateXbox360Controller());

        public void Dispose() => client.Dispose();
    }

    internal sealed class NefariusViGEmControllerResource : IViGEmControllerResource
    {
        private readonly IXbox360Controller controller;

        internal NefariusViGEmControllerResource(IXbox360Controller controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public void Connect() => controller.Connect();
        public void Disconnect() => controller.Disconnect();
        public void SetAxisValue(Xbox360Axis axis, short value) => controller.SetAxisValue(axis, value);
        public void SetButtonState(Xbox360Button button, bool isPressed) =>
            controller.SetButtonState(button, isPressed);
    }

    /// <summary>
    /// ViGEmBus-backed implementation of <see cref="IGameInput"/>. All Nefarius-specific types and
    /// mappings are contained here so navigation code remains independent of the controller backend.
    /// </summary>
    internal sealed class ViGEmGameInput : IGameInput
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly ControllerButtonMapping buttonMapping;
        private readonly InputSimulator inputSimulator = new InputSimulator();
        private IViGEmClientResource client;
        private IViGEmControllerResource controller;
        private bool connectAttempted;
        private int disposed;

        public string FailureReason { get; private set; }

        public bool IsAvailable => FailureReason == null && Volatile.Read(ref disposed) == 0;

        public ViGEmGameInput(ControllerConfirmLayout layout)
            : this(new NefariusViGEmClientFactory(), layout, Thread.Sleep)
        {
        }

        internal ViGEmGameInput(
            IViGEmClientFactory clientFactory,
            ControllerConfirmLayout layout,
            Action<int> wait)
        {
            if (clientFactory == null) throw new ArgumentNullException(nameof(clientFactory));
            if (wait == null) throw new ArgumentNullException(nameof(wait));

            buttonMapping = new ControllerButtonMapping(layout);
            try
            {
                client = clientFactory.CreateClient();
                controller = client.CreateXbox360Controller();
                connectAttempted = true;
                controller.Connect();
                // Windows/the game need a moment to enumerate the virtual device after Connect().
                wait(500);
            }
            catch (VigemBusNotFoundException)
            {
                FailureReason = "ViGEmBus driver is not installed. Install it from " +
                                "https://github.com/ViGEm/ViGEmBus/releases and try again.";
                DisposeOwnedResourcesPreservingFailure();
            }
            catch (Exception ex)
            {
                FailureReason = $"{ex.GetType().Name}: {ex.Message}";
                DisposeOwnedResourcesPreservingFailure();
            }
        }

        public void SetLeftStickHorizontal(float value)
        {
            RequireController();
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxisValue(value));
        }

        public void SetLeftStickVertical(float value)
        {
            RequireController();
            controller.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxisValue(value));
        }

        public void SetButtonState(GameInputButton button, bool isPressed)
        {
            RequireController();
            controller.SetButtonState(buttonMapping.ToXbox360Button(button), isPressed);
        }

        public void MoveMouseBy(int x, int y) => inputSimulator.Mouse.MoveMouseBy(x, y);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            DisposeOwnedResources(throwOnFailure: true);
        }

        private void DisposeOwnedResourcesPreservingFailure()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            DisposeOwnedResources(throwOnFailure: false);
        }

        private void DisposeOwnedResources(bool throwOnFailure)
        {
            Exception firstFailure = null;
            if (controller != null && connectAttempted)
            {
                try
                {
                    controller.Disconnect();
                }
                catch (Exception ex)
                {
                    firstFailure = ex;
                    if (!throwOnFailure)
                        Logger.Warn(ex, "ViGEm cleanup failed while disconnecting a partially initialized controller.");
                }
            }

            if (client != null)
            {
                try
                {
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    if (firstFailure == null) firstFailure = ex;
                    if (!throwOnFailure)
                        Logger.Warn(ex, "ViGEm cleanup failed while disposing a partially initialized client.");
                }
            }

            controller = null;
            client = null;
            if (throwOnFailure && firstFailure != null) throw firstFailure;
        }

        private void RequireController()
        {
            if (!IsAvailable || controller == null)
                throw new InvalidOperationException($"Controller is not available: {FailureReason ?? "disposed"}");
        }

        private static short ToAxisValue(float value)
        {
            if (value <= -1f) return short.MinValue;
            if (value >= 1f) return short.MaxValue;
            return (short)(short.MaxValue * value);
        }
    }
}
