using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace InventoryKamera.game
{
    internal sealed class AchievementWindowsPointerInput : IAchievementPointerInput
    {
        private const uint InputMouse = 0;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

        public void MoveNormalized(double x, double y, CancellationToken cancellationToken)
        {
            if (x is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(x));
            if (y is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(y));
            cancellationToken.ThrowIfCancellationRequested();

            RECT bounds = Navigation.GetPosition();
            var size = Navigation.GetSize();
            int screenX = bounds.Left + (int)Math.Round((size.Width - 1) * x);
            int screenY = bounds.Top + (int)Math.Round((size.Height - 1) * y);
            if (!SetCursorPos(screenX, screenY))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not position the mouse cursor.");
        }

        public bool IsAtNormalized(double x, double y)
        {
            RECT bounds = Navigation.GetPosition();
            var size = Navigation.GetSize();
            var expected = new Point(
                bounds.Left + (int)Math.Round((size.Width - 1) * x),
                bounds.Top + (int)Math.Round((size.Height - 1) * y));
            if (!GetCursorPos(out Point actual)) return false;
            return Math.Abs(actual.X - expected.X) <= 2 && Math.Abs(actual.Y - expected.Y) <= 2;
        }

        public void Click(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputs = new[] { MouseInput(MouseLeftDown), MouseInput(MouseLeftUp) };
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
            if (sent != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not deliver the requested mouse click.");
        }

        private static NativeInput MouseInput(uint flags) => new()
        {
            Type = InputMouse,
            Union = new NativeInputUnion { Mouse = new NativeMouseInput { Flags = flags } }
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            internal uint Type;
            internal NativeInputUnion Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct NativeInputUnion
        {
            [FieldOffset(0)] internal NativeMouseInput Mouse;
            [FieldOffset(0)] internal NativeKeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMouseInput
        {
            internal int X;
            internal int Y;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeKeyboardInput
        {
            internal ushort VirtualKey;
            internal ushort ScanCode;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }
    }
}
