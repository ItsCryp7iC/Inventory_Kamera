namespace InventoryKamera.game
{
    internal static class GameInputFactory
    {
        public static GameNavigator CreateNavigator() => new GameNavigator(new ViGEmGameInput());
    }
}
