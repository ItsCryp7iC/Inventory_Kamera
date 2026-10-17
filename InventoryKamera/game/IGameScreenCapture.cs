using System.Drawing;

namespace InventoryKamera.game
{
    /// <summary>Captures the game UI without exposing the static capture implementation to detectors.</summary>
    internal interface IGameScreenCapture
    {
        Bitmap CaptureWindow();
    }

    internal sealed class NavigationGameScreenCapture : IGameScreenCapture
    {
        public Bitmap CaptureWindow() => Navigation.CaptureWindow();
    }
}
