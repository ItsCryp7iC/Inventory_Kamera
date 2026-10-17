namespace InventoryKamera.game
{
    /// <summary>
    /// The user's authoritative declaration for one scan. Visual input-mode observations must not
    /// overwrite this value or prevent a user-confirmed scan from starting.
    /// </summary>
    internal enum DeclaredInputMode
    {
        KeyboardMouse = 0,
        Controller = 1,
    }

    internal static class DeclaredInputModeOptions
    {
        internal static DeclaredInputMode FromPersistedValue(int value) =>
            value == (int)DeclaredInputMode.Controller
                ? DeclaredInputMode.Controller
                : DeclaredInputMode.KeyboardMouse;
    }
}
