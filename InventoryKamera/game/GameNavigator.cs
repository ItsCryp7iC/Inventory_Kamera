using System;
using System.Threading;

namespace InventoryKamera.game
{
    /// <summary>
    /// Expresses Inventory Kamera's game-navigation operations independently of the input backend.
    /// </summary>
    internal sealed class GameNavigator : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
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
            Exception operationFailure = null;
            bool movementStarted = false;
            try
            {
                movementStarted = true;
                input.SetLeftStickHorizontal(0.5f);
                wait(150);
            }
            catch (Exception ex)
            {
                operationFailure = ex;
                throw;
            }
            finally
            {
                if (movementStarted)
                    ReleasePreservingOriginal(() => input.SetLeftStickHorizontal(0f), operationFailure, "centering the left stick");
            }
            wait(150);
            TapBack();
        }

        public void OpenMenu() => TapButton(GameInputButton.Menu);

        public void MoveStep(MenuDirection direction, int holdMs = 150, int settleMs = 150)
        {
            RequireAvailable();
            Exception operationFailure = null;
            bool movementStarted = false;
            try
            {
                movementStarted = true;
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
            }
            catch (Exception ex)
            {
                operationFailure = ex;
                throw;
            }
            finally
            {
                if (movementStarted) CenterStickPreservingOriginal(operationFailure);
            }
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
            Dispose(exitControllerMode: true);
        }

        /// <summary>
        /// Releases the owned input backend without sending the legacy controller-mode exit
        /// sequence. Used only after Genshin has positively switched back to Keyboard/Mouse mode;
        /// disconnecting the virtual controller before that verification can leave the game unusable.
        /// </summary>
        internal void DisposeInputOnly()
        {
            Dispose(exitControllerMode: false);
        }

        private void Dispose(bool exitControllerMode)
        {
            Exception operationFailure = null;
            try
            {
                if (exitControllerMode && IsAvailable)
                {
                    ExitControllerMode();
                    wait(100);
                }
            }
            catch (Exception ex)
            {
                operationFailure = ex;
                throw;
            }
            finally
            {
                ReleasePreservingOriginal(input.Dispose, operationFailure, "disposing game input");
            }
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
            Exception operationFailure = null;
            bool pressed = false;
            try
            {
                input.SetButtonState(button, true);
                pressed = true;
                wait(holdMs);
            }
            catch (Exception ex)
            {
                operationFailure = ex;
                throw;
            }
            finally
            {
                if (pressed)
                    ReleasePreservingOriginal(
                        () => input.SetButtonState(button, false),
                        operationFailure,
                        $"releasing {button}");
            }
        }

        private void CenterStickPreservingOriginal(Exception operationFailure)
        {
            Exception horizontalFailure = null;
            try
            {
                input.SetLeftStickHorizontal(0f);
            }
            catch (Exception ex)
            {
                horizontalFailure = ex;
                if (operationFailure != null)
                    Logger.Warn(ex, "Controller cleanup failed while centering the horizontal axis; preserving the original failure.");
            }

            try
            {
                input.SetLeftStickVertical(0f);
            }
            catch (Exception ex)
            {
                if (operationFailure != null || horizontalFailure != null)
                {
                    Logger.Warn(ex, "Controller cleanup failed while centering the vertical axis; preserving the earlier failure.");
                }
                else
                {
                    throw;
                }
            }

            if (operationFailure == null && horizontalFailure != null) throw horizontalFailure;
        }

        private static void ReleasePreservingOriginal(Action cleanup, Exception operationFailure, string operation)
        {
            try
            {
                cleanup();
            }
            catch (Exception ex) when (operationFailure != null)
            {
                Logger.Warn(ex, "Controller cleanup failed while {0}; preserving the original failure.", operation);
            }
        }

        private void RequireAvailable()
        {
            if (!IsAvailable) throw new InvalidOperationException($"Controller is not available: {FailureReason}");
        }
    }
}
