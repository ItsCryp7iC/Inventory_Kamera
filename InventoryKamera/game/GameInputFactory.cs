namespace InventoryKamera.game
{
    internal static class GameInputFactory
    {
        public static GameNavigator CreateNavigator()
        {
            var settings = Properties.Settings.Default;
            ControllerConfirmLayout layout = ControllerConfirmLayoutOptions.Resolve(
                settings.UseDefaultControllerConfirmLayout,
                settings.ControllerConfirmLayout);
            return new GameNavigator(new ViGEmGameInput(layout));
        }
    }
}
