using System;

namespace InventoryKamera.game
{
    /// <summary>
    /// Hides an Inventory Kamera surface while desktop pixels are copied. The existing capture
    /// backend reads composited screen pixels, so this guard must cover both whole-window and region
    /// captures rather than relying on overlay placement.
    /// </summary>
    internal interface ICaptureOverlayGuard
    {
        IDisposable SuspendForCapture();
    }
}
