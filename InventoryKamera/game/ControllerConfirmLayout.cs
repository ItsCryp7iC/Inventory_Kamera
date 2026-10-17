namespace InventoryKamera.game
{
    internal enum ControllerConfirmLayout
    {
        BConfirm_ACancel = 0,
        AConfirm_BCancel = 1
    }

    internal static class ControllerConfirmLayoutOptions
    {
        internal const ControllerConfirmLayout Default = ControllerConfirmLayout.BConfirm_ACancel;

        internal static ControllerConfirmLayout FromPersistedValue(int value) =>
            value == (int)ControllerConfirmLayout.AConfirm_BCancel
                ? ControllerConfirmLayout.AConfirm_BCancel
                : Default;

        internal static ControllerConfirmLayout Resolve(bool useDefault, int persistedValue) =>
            useDefault ? Default : FromPersistedValue(persistedValue);

        internal static string DisplayText(ControllerConfirmLayout layout) =>
            layout == ControllerConfirmLayout.AConfirm_BCancel
                ? "A = Confirm, B = Cancel"
                : "B = Confirm, A = Cancel";
    }

}
