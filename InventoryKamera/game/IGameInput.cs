using System;

namespace InventoryKamera.game
{
    internal enum GameInputButton
    {
        Back,
        Confirm,
        Menu,
        DPadDown,
        DPadLeft,
        LeftStick,
        PreviousTab,
        NextTab
    }

    /// <summary>
    /// Backend-neutral input operations required by Inventory Kamera's controller navigation.
    /// </summary>
    internal interface IGameInput : IDisposable
    {
        bool IsAvailable { get; }
        string FailureReason { get; }

        void SetLeftStickHorizontal(float value);
        void SetLeftStickVertical(float value);
        void SetButtonState(GameInputButton button, bool isPressed);
        void MoveMouseBy(int x, int y);
    }
}
