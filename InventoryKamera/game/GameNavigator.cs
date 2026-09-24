using System;
using System.Threading;

namespace InventoryKamera.game
{
    /// <summary>
    /// Expresses Inventory Kamera's game-navigation operations independently of the input backend.
    /// </summary>
    internal sealed class GameNavigator : IDisposable
    {
        private readonly IGameInput input;
        private readonly Action<int> wait;

        public string FailureReason => input.FailureReason;

        public bool IsAvailable => input.IsAvailable;

        public GameNavigator(IGameInput input, Action<int> wait = null)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.wait = wait ?? Thread.Sleep;
        }

        /// <summary>
        /// Switches Genshin to controller mode using the existing stick-nudge and A-button sequence.
        /// </summary>
        public void EnterControllerMode()
        {
            RequireAvailable();
            input.SetLeftStickHorizontal(0.5f);
            wait(150);
            input.SetLeftStickHorizontal(0f);
            wait(150);
            TapBack();
        }

        public void OpenMenu() => TapButton(GameInputButton.Menu);

        public void MoveStep(MenuDirection direction, int holdMs = 150, int settleMs = 150)
        {
            RequireAvailable();
            switch (direction)
            {
                case MenuDirection.Up:
                    input.SetLeftStickVertical(1f);
                    break;
                case MenuDirection.Down:
                    input.SetLeftStickVertical(-1f);
                    break;
                case MenuDirection.Left:
                    input.SetLeftStickHorizontal(-1f);
                    break;
                case MenuDirection.Right:
                    input.SetLeftStickHorizontal(1f);
                    break;
            }
            wait(holdMs);
            input.SetLeftStickHorizontal(0f);
            input.SetLeftStickVertical(0f);
            wait(settleMs);
        }

        public void Move(MenuDirection direction, int steps, int holdMs = 150, int settleMs = 150)
        {
            for (var i = 0; i < steps; i++) MoveStep(direction, holdMs, settleMs);
        }

        public void TapBack(int holdMs = 150) => TapButton(GameInputButton.Back, holdMs);

        public void TapConfirm(int holdMs = 150) => TapButton(GameInputButton.Confirm, holdMs);

        public void TapDPadDown(int holdMs = 150) => TapButton(GameInputButton.DPadDown, holdMs);

        public void TapDPadLeft(int holdMs = 150) => TapButton(GameInputButton.DPadLeft, holdMs);

        public void TapLeftStick(int holdMs = 150) => TapButton(GameInputButton.LeftStick, holdMs);

        public void TapPreviousTab(int holdMs = 150) => TapButton(GameInputButton.PreviousTab, holdMs);

        public void TapNextTab(int holdMs = 150) => TapButton(GameInputButton.NextTab, holdMs);

        public void MashBack(int times = 6, int delayMs = 300)
        {
            RequireAvailable();
            for (var i = 0; i < times; i++)
            {
                TapBack();
                wait(delayMs);
            }
        }

        public void ExitControllerMode()
        {
            if (!IsAvailable) return;
            MashBack();
            input.MoveMouseBy(1, 0);
            wait(50);
            input.MoveMouseBy(-1, 0);
        }

        public void Dispose()
        {
            if (!IsAvailable) return;
            ExitControllerMode();
            wait(100);
            input.Dispose();
        }

        public enum MenuDirection
        {
            Up,
            Down,
            Left,
            Right
        }

        private void TapButton(GameInputButton button, int holdMs = 150)
        {
            RequireAvailable();
            input.SetButtonState(button, true);
            wait(holdMs);
            input.SetButtonState(button, false);
        }

        private void RequireAvailable()
        {
            if (!IsAvailable) throw new InvalidOperationException($"Controller is not available: {FailureReason}");
        }
    }
}
