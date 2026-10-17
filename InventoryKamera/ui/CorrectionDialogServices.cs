using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    internal static class CorrectionDialogPlacement
    {
        internal static Point Calculate(
            Rectangle gameClientBounds,
            Size dialogSize,
            Rectangle monitorWorkArea)
        {
            Rectangle clampBounds =
                gameClientBounds.Width >= dialogSize.Width &&
                gameClientBounds.Height >= dialogSize.Height
                    ? gameClientBounds
                    : monitorWorkArea;

            int centeredX = gameClientBounds.Left + (gameClientBounds.Width - dialogSize.Width) / 2;
            int centeredY = gameClientBounds.Top + (gameClientBounds.Height - dialogSize.Height) / 2;
            int maxX = Math.Max(clampBounds.Left, clampBounds.Right - dialogSize.Width);
            int maxY = Math.Max(clampBounds.Top, clampBounds.Bottom - dialogSize.Height);
            return new Point(
                Math.Clamp(centeredX, clampBounds.Left, maxX),
                Math.Clamp(centeredY, clampBounds.Top, maxY));
        }
    }

    internal sealed class CorrectionDialogPositioner
    {
        private readonly IGenshinWindowController gameWindow;
        private readonly Func<Rectangle, Rectangle> workAreaForBounds;

        internal CorrectionDialogPositioner(
            IGenshinWindowController gameWindow,
            Func<Rectangle, Rectangle> workAreaForBounds = null)
        {
            this.gameWindow = gameWindow ?? throw new ArgumentNullException(nameof(gameWindow));
            this.workAreaForBounds = workAreaForBounds ??
                (bounds => Screen.FromRectangle(bounds).WorkingArea);
        }

        internal bool TryPosition(Form dialog)
        {
            if (dialog == null) throw new ArgumentNullException(nameof(dialog));
            if (!TryCalculate(dialog.Size, out Point location)) return false;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = location;
            return true;
        }

        internal bool TryCalculate(Size dialogSize, out Point location)
        {
            location = Point.Empty;
            if (!gameWindow.TryGetClientBounds(out Rectangle gameBounds)) return false;
            location = CorrectionDialogPlacement.Calculate(
                gameBounds,
                dialogSize,
                workAreaForBounds(gameBounds));
            return true;
        }
    }

    internal interface ICorrectionWindowNativeApi
    {
        bool SetForegroundWindow(IntPtr window);
    }

    internal sealed class WindowsCorrectionWindowNativeApi : ICorrectionWindowNativeApi
    {
        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        private static extern bool SetForegroundWindowNative(IntPtr window);

        public bool SetForegroundWindow(IntPtr window) => SetForegroundWindowNative(window);
    }

    internal sealed class CorrectionDialogForegroundService
    {
        private readonly ICorrectionWindowNativeApi nativeApi;

        internal CorrectionDialogForegroundService(ICorrectionWindowNativeApi nativeApi = null) =>
            this.nativeApi = nativeApi ?? new WindowsCorrectionWindowNativeApi();

        internal void PrepareForShow(Form dialog)
        {
            if (dialog == null) throw new ArgumentNullException(nameof(dialog));
            dialog.TopMost = true;
        }

        internal bool RequestForeground(Form dialog)
        {
            if (dialog == null) throw new ArgumentNullException(nameof(dialog));
            dialog.BringToFront();
            dialog.Activate();
            bool requested = nativeApi.SetForegroundWindow(dialog.Handle);
            dialog.Focus();
            return requested;
        }

        internal void Release(Form dialog)
        {
            if (dialog != null && !dialog.IsDisposed) dialog.TopMost = false;
        }
    }

    internal static class CorrectionDialogLifecycle
    {
        internal static void Run(IScanCompanionOverlay overlay, Action interaction)
        {
            if (interaction == null) throw new ArgumentNullException(nameof(interaction));
            overlay?.ShowInputRequired();
            using IDisposable suspension = overlay?.SuspendForInteraction();
            interaction();
        }
    }
}
