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
    /// ViGEmBus-backed implementation of <see cref="IGameInput"/>. All Nefarius-specific types and
    /// mappings are contained here so navigation code remains independent of the controller backend.
    /// </summary>
    internal sealed class ViGEmGameInput : IGameInput
    {
        private readonly ViGEmClient client;
        private readonly IXbox360Controller controller;
        private readonly InputSimulator inputSimulator = new InputSimulator();

        public string FailureReason { get; }

        public bool IsAvailable => FailureReason == null;

        public ViGEmGameInput()
        {
            try
            {
                client = new ViGEmClient();
                controller = client.CreateXbox360Controller();
                controller.Connect();
                // Windows/the game need a moment to enumerate the virtual device after Connect().
                Thread.Sleep(500);
            }
            catch (VigemBusNotFoundException)
            {
                FailureReason = "ViGEmBus driver is not installed. Install it from " +
                                 "https://github.com/ViGEm/ViGEmBus/releases and try again.";
            }
            catch (Exception ex)
            {
                FailureReason = $"{ex.GetType().Name}: {ex.Message}";
            }
        }

        public void SetLeftStickHorizontal(float value) =>
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, ToAxisValue(value));

        public void SetLeftStickVertical(float value) =>
            controller.SetAxisValue(Xbox360Axis.LeftThumbY, ToAxisValue(value));

        public void SetButtonState(GameInputButton button, bool isPressed) =>
            controller.SetButtonState(ToXbox360Button(button), isPressed);

        public void MoveMouseBy(int x, int y) => inputSimulator.Mouse.MoveMouseBy(x, y);

        public void Dispose()
        {
            if (!IsAvailable) return;
            controller.Disconnect();
            client.Dispose();
        }

        private static short ToAxisValue(float value)
        {
            if (value <= -1f) return short.MinValue;
            if (value >= 1f) return short.MaxValue;
            return (short)(short.MaxValue * value);
        }

        private static Xbox360Button ToXbox360Button(GameInputButton button)
        {
            switch (button)
            {
                case GameInputButton.Back:
                    return Xbox360Button.A;
                case GameInputButton.Confirm:
                    return Xbox360Button.B;
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
}
