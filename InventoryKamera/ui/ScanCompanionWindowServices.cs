using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    internal readonly struct OverlayRelativePosition
    {
        internal OverlayRelativePosition(double x, double y)
        {
            X = x;
            Y = y;
        }

        internal double X { get; }
        internal double Y { get; }
        internal bool IsValid =>
            !double.IsNaN(X) && !double.IsInfinity(X) && X >= 0 && X <= 1 &&
            !double.IsNaN(Y) && !double.IsInfinity(Y) && Y >= 0 && Y <= 1;
    }

    internal static class OverlayPlacement
    {
        internal const int DefaultMargin = 16;

        internal static Point Calculate(
            Rectangle gameClientBounds,
            Size overlaySize,
            OverlayRelativePosition? relativePosition,
            int margin = DefaultMargin)
        {
            if (gameClientBounds.Width <= 0 || gameClientBounds.Height <= 0)
                return Point.Empty;

            int maxX = Math.Max(gameClientBounds.Left, gameClientBounds.Right - overlaySize.Width);
            int maxY = Math.Max(gameClientBounds.Top, gameClientBounds.Bottom - overlaySize.Height);
            Point candidate;

            if (relativePosition.HasValue && relativePosition.Value.IsValid)
            {
                candidate = new Point(
                    gameClientBounds.Left + (int)Math.Round(relativePosition.Value.X * gameClientBounds.Width),
                    gameClientBounds.Top + (int)Math.Round(relativePosition.Value.Y * gameClientBounds.Height));
            }
            else
            {
                candidate = new Point(
                    gameClientBounds.Right - overlaySize.Width - Math.Max(0, margin),
                    gameClientBounds.Top + Math.Max(0, margin));
            }

            return new Point(
                Math.Clamp(candidate.X, gameClientBounds.Left, maxX),
                Math.Clamp(candidate.Y, gameClientBounds.Top, maxY));
        }

        internal static OverlayRelativePosition ToRelative(Rectangle gameClientBounds, Point overlayLocation)
        {
            if (gameClientBounds.Width <= 0 || gameClientBounds.Height <= 0)
                return new OverlayRelativePosition(double.NaN, double.NaN);

            return new OverlayRelativePosition(
                Math.Clamp((overlayLocation.X - gameClientBounds.Left) / (double)gameClientBounds.Width, 0, 1),
                Math.Clamp((overlayLocation.Y - gameClientBounds.Top) / (double)gameClientBounds.Height, 0, 1));
        }
    }

    internal enum OverlayInteractionMode
    {
        Passive,
        Interactive,
    }

    internal static class OverlayInteractionPolicy
    {
        internal static OverlayInteractionMode ForState(ScanCompanionState state) => state switch
        {
            ScanCompanionState.WaitingForController => OverlayInteractionMode.Interactive,
            ScanCompanionState.Recovery => OverlayInteractionMode.Interactive,
            ScanCompanionState.Error => OverlayInteractionMode.Interactive,
            _ => OverlayInteractionMode.Passive,
        };
    }

    internal static class OverlayWindowBehavior
    {
        internal const long WsExTransparent = 0x00000020L;
        internal const int MaNoActivate = 3;
        internal const int MaNoActivateAndEat = 4;

        internal static long ApplyInteractionStyle(long extendedStyles, OverlayInteractionMode mode) =>
            mode == OverlayInteractionMode.Passive
                ? extendedStyles | WsExTransparent
                : extendedStyles & ~WsExTransparent;

        internal static bool IsClickThrough(long extendedStyles) =>
            (extendedStyles & WsExTransparent) != 0;

        internal static bool HitTestIsTransparent(OverlayInteractionMode mode) =>
            mode == OverlayInteractionMode.Passive;

        internal static bool MouseActivatePermitsDelivery(int result) =>
            result != MaNoActivateAndEat && result != 2; // MA_ACTIVATEANDEAT
    }

    internal static class OverlayContentSizing
    {
        internal static int RequiredClientHeight(int bottomPadding, params int[] controlBottoms) =>
            (controlBottoms == null || controlBottoms.Length == 0 ? 0 : controlBottoms.Max()) +
            Math.Max(0, bottomPadding);
    }

    internal interface IGenshinWindowController
    {
        bool TryGetClientBounds(out Rectangle clientBounds);
        bool IsForeground { get; }
        bool TryBringToForeground(TimeSpan timeout);
    }

    internal interface IGenshinWindowNativeApi
    {
        bool TryGetClientBounds(IntPtr window, out Rectangle bounds);
        IntPtr GetForegroundWindow();
        bool IsIconic(IntPtr window);
        bool RestoreWindow(IntPtr window);
        bool SetForegroundWindow(IntPtr window);
    }

    internal sealed class WindowsGenshinWindowController : IGenshinWindowController
    {
        private const int FocusPollMilliseconds = 25;
        private readonly Func<IntPtr> findWindow;
        private readonly IGenshinWindowNativeApi nativeApi;
        private readonly Action<int> wait;

        internal WindowsGenshinWindowController(
            Func<IntPtr> findWindow = null,
            IGenshinWindowNativeApi nativeApi = null,
            Action<int> wait = null)
        {
            this.findWindow = findWindow ?? FindGenshinWindow;
            this.nativeApi = nativeApi ?? new WindowsGenshinWindowNativeApi();
            this.wait = wait ?? Thread.Sleep;
        }

        public bool TryGetClientBounds(out Rectangle clientBounds)
        {
            clientBounds = Rectangle.Empty;
            IntPtr window = findWindow();
            return window != IntPtr.Zero && nativeApi.TryGetClientBounds(window, out clientBounds);
        }

        public bool IsForeground
        {
            get
            {
                IntPtr window = findWindow();
                return window != IntPtr.Zero && nativeApi.GetForegroundWindow() == window;
            }
        }

        public bool TryBringToForeground(TimeSpan timeout)
        {
            IntPtr window = findWindow();
            if (window == IntPtr.Zero) return false;
            if (nativeApi.GetForegroundWindow() == window) return true;

            if (nativeApi.IsIconic(window)) nativeApi.RestoreWindow(window);
            nativeApi.SetForegroundWindow(window);

            int attempts = Math.Max(1, (int)Math.Ceiling(
                Math.Max(0, timeout.TotalMilliseconds) / FocusPollMilliseconds));
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (nativeApi.GetForegroundWindow() == window) return true;
                if (attempt + 1 < attempts) wait(FocusPollMilliseconds);
            }
            return false;
        }

        private static IntPtr FindGenshinWindow()
        {
            foreach (string processName in Properties.Settings.Default.Executables.Cast<string>())
            {
                Process[] processes = Array.Empty<Process>();
                try
                {
                    processes = Process.GetProcessesByName(processName);
                    foreach (Process process in processes)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                    }
                }
                finally
                {
                    foreach (Process process in processes) process.Dispose();
                }
            }
            return IntPtr.Zero;
        }
    }

    internal sealed class WindowsGenshinWindowNativeApi : IGenshinWindowNativeApi
    {
        private const int SwRestore = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern IntPtr GetForegroundWindowNative();

        [DllImport("user32.dll", EntryPoint = "IsIconic")]
        private static extern bool IsIconicNative(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        private static extern bool SetForegroundWindowNative(IntPtr window);

        public bool TryGetClientBounds(IntPtr window, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!GetClientRect(window, out NativeRect rect)) return false;
            var origin = new NativePoint();
            if (!ClientToScreen(window, ref origin)) return false;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return false;
            bounds = new Rectangle(origin.X, origin.Y, width, height);
            return true;
        }

        public IntPtr GetForegroundWindow() => GetForegroundWindowNative();
        public bool IsIconic(IntPtr window) => IsIconicNative(window);
        public bool RestoreWindow(IntPtr window) => ShowWindow(window, SwRestore);
        public bool SetForegroundWindow(IntPtr window) => SetForegroundWindowNative(window);
    }

    internal interface IOverlayWindowNativeApi
    {
        void ShowNoActivate(IntPtr window);
        void PositionNoActivate(IntPtr window, Rectangle bounds);
        void SetClickThrough(IntPtr window, bool enabled);
        long GetExtendedStyles(IntPtr window);
    }

    internal sealed class NonActivatingOverlayPresenter
    {
        private readonly IOverlayWindowNativeApi nativeApi;

        internal NonActivatingOverlayPresenter(IOverlayWindowNativeApi nativeApi = null) =>
            this.nativeApi = nativeApi ?? new WindowsOverlayWindowNativeApi();

        internal void Show(IntPtr window, Rectangle bounds)
        {
            nativeApi.ShowNoActivate(window);
            nativeApi.PositionNoActivate(window, bounds);
        }

        internal void Position(IntPtr window, Rectangle bounds) =>
            nativeApi.PositionNoActivate(window, bounds);

        internal void SetInteractionMode(IntPtr window, OverlayInteractionMode mode) =>
            nativeApi.SetClickThrough(window, mode == OverlayInteractionMode.Passive);

        internal long GetExtendedStyles(IntPtr window) => nativeApi.GetExtendedStyles(window);
    }

    internal sealed class WindowsOverlayWindowNativeApi : IOverlayWindowNativeApi
    {
        private const int GwlExStyle = -20;
        private const int SwShowNoActivate = 4;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpNoOwnerZOrder = 0x0200;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr window, int index, IntPtr value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLongPtr32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLongPtr32(IntPtr window, int index, IntPtr value);

        public void ShowNoActivate(IntPtr window)
        {
            ShowWindow(window, SwShowNoActivate);
        }

        public void PositionNoActivate(IntPtr window, Rectangle bounds)
        {
            SetWindowPos(window, HwndTopmost, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SwpNoActivate | SwpNoOwnerZOrder | SwpShowWindow);
        }

        public void SetClickThrough(IntPtr window, bool enabled)
        {
            long current = GetExtendedStyles(window);
            long value = OverlayWindowBehavior.ApplyInteractionStyle(
                current,
                enabled ? OverlayInteractionMode.Passive : OverlayInteractionMode.Interactive);
            if (IntPtr.Size == 8)
                SetWindowLongPtr64(window, GwlExStyle, new IntPtr(value));
            else
                SetWindowLongPtr32(window, GwlExStyle, new IntPtr(value));

            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder | SwpFrameChanged);
        }

        public long GetExtendedStyles(IntPtr window) => (IntPtr.Size == 8
            ? GetWindowLongPtr64(window, GwlExStyle)
            : GetWindowLongPtr32(window, GwlExStyle)).ToInt64();
    }

    internal sealed class ScanReadyFocusGate
    {
        private readonly IGenshinWindowController gameWindow;
        private readonly TimeSpan timeout;
        private readonly Action<int> wait;
        private readonly int settleMilliseconds;

        internal ScanReadyFocusGate(
            IGenshinWindowController gameWindow,
            TimeSpan? timeout = null,
            Action<int> wait = null,
            int settleMilliseconds = 150)
        {
            this.gameWindow = gameWindow ?? throw new ArgumentNullException(nameof(gameWindow));
            this.timeout = timeout ?? TimeSpan.FromMilliseconds(750);
            this.wait = wait ?? Thread.Sleep;
            this.settleMilliseconds = Math.Max(0, settleMilliseconds);
        }

        internal bool TryRelease(Action release)
        {
            if (!gameWindow.TryBringToForeground(timeout)) return false;
            if (settleMilliseconds > 0) wait(settleMilliseconds);
            release();
            return true;
        }
    }

    internal enum RecoveryHotkeyCommand
    {
        Up,
        Down,
        Left,
        Right,
        Confirm,
        Back,
        OpenMenu,
        Retry,
    }

    internal sealed class WaitingReadyHotkeyCoordinator : IDisposable
    {
        internal const int HotkeyId = 0x5A10;
        private readonly IGlobalHotkeyRegistrar registrar;
        private IntPtr window;
        private Action dispatch;
        private bool registered;

        internal WaitingReadyHotkeyCoordinator(IGlobalHotkeyRegistrar registrar = null) =>
            this.registrar = registrar ?? new WindowsGlobalHotkeyRegistrar();

        internal bool RegistrationAttempted { get; private set; }
        internal bool RegistrationSucceeded => registered;
        internal int RegistrationErrorCode { get; private set; }
        internal bool IsActive => registered;

        internal void EnterWaiting(IntPtr window, Action dispatch)
        {
            ExitWaiting();
            if (window == IntPtr.Zero || dispatch == null) return;

            this.window = window;
            this.dispatch = dispatch;
            RegistrationAttempted = true;
            registered = registrar.TryRegister(window, HotkeyId, Keys.Space, out int errorCode);
            RegistrationErrorCode = errorCode;
        }

        internal bool TryDispatch(int id)
        {
            if (!registered || id != HotkeyId) return false;
            dispatch?.Invoke();
            return true;
        }

        internal void ExitWaiting()
        {
            if (registered && window != IntPtr.Zero) registrar.Unregister(window, HotkeyId);
            registered = false;
            dispatch = null;
            window = IntPtr.Zero;
            RegistrationAttempted = false;
            RegistrationErrorCode = 0;
        }

        public void Dispose() => ExitWaiting();
    }

    internal sealed class RecoveryActionGate
    {
        private readonly long debounceMilliseconds;
        private int executing;
        private long lastCompletionTicks = long.MinValue;

        internal RecoveryActionGate(long debounceMilliseconds = 250) =>
            this.debounceMilliseconds = Math.Max(0, debounceMilliseconds);

        internal bool IsAvailable => Volatile.Read(ref executing) == 0;

        internal bool TryEnter(long now)
        {
            if (Interlocked.CompareExchange(ref executing, 1, 0) != 0) return false;
            long last = Interlocked.Read(ref lastCompletionTicks);
            if (last != long.MinValue && now - last < debounceMilliseconds)
            {
                Interlocked.Exchange(ref executing, 0);
                return false;
            }
            return true;
        }

        internal void Exit(long now)
        {
            Interlocked.Exchange(ref lastCompletionTicks, now);
            Interlocked.Exchange(ref executing, 0);
        }
    }

    internal interface IGlobalHotkeyRegistrar
    {
        bool TryRegister(IntPtr window, int id, Keys key, out int errorCode);
        void Unregister(IntPtr window, int id);
    }

    internal sealed class WindowsGlobalHotkeyRegistrar : IGlobalHotkeyRegistrar
    {
        private const uint ModNoRepeat = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        public bool TryRegister(IntPtr window, int id, Keys key, out int errorCode)
        {
            bool registered = RegisterHotKey(window, id, ModNoRepeat, (uint)key);
            errorCode = registered ? 0 : Marshal.GetLastWin32Error();
            return registered;
        }

        public void Unregister(IntPtr window, int id) => UnregisterHotKey(window, id);
    }

    internal sealed class RecoveryHotkeyCoordinator : IDisposable
    {
        private static readonly (int Id, Keys Key, RecoveryHotkeyCommand Command)[] Bindings =
        {
            (0x5A20, Keys.Up, RecoveryHotkeyCommand.Up),
            (0x5A21, Keys.Down, RecoveryHotkeyCommand.Down),
            (0x5A22, Keys.Left, RecoveryHotkeyCommand.Left),
            (0x5A23, Keys.Right, RecoveryHotkeyCommand.Right),
            (0x5A24, Keys.Enter, RecoveryHotkeyCommand.Confirm),
            (0x5A25, Keys.Back, RecoveryHotkeyCommand.Back),
            (0x5A26, Keys.P, RecoveryHotkeyCommand.OpenMenu),
            (0x5A27, Keys.R, RecoveryHotkeyCommand.Retry),
        };

        private readonly IGlobalHotkeyRegistrar registrar;
        private readonly Dictionary<int, RecoveryHotkeyCommand> registered = new();
        private readonly List<RecoveryHotkeyRegistration> registrationResults = new();
        private IntPtr window;
        private Action<RecoveryHotkeyCommand> dispatch;

        internal RecoveryHotkeyCoordinator(IGlobalHotkeyRegistrar registrar = null) =>
            this.registrar = registrar ?? new WindowsGlobalHotkeyRegistrar();

        internal bool IsActive => window != IntPtr.Zero;
        internal int RegisteredCount => registered.Count;
        internal bool RegistrationAttempted { get; private set; }
        internal IReadOnlyList<RecoveryHotkeyRegistration> RegistrationResults => registrationResults;

        internal void EnterRecovery(IntPtr window, Action<RecoveryHotkeyCommand> dispatch)
        {
            ExitRecovery();
            if (window == IntPtr.Zero || dispatch == null) return;
            this.window = window;
            this.dispatch = dispatch;
            RegistrationAttempted = true;
            foreach ((int id, Keys key, RecoveryHotkeyCommand command) in Bindings)
            {
                bool success = registrar.TryRegister(window, id, key, out int errorCode);
                registrationResults.Add(new RecoveryHotkeyRegistration(id, key, command, success, errorCode));
                if (success) registered.Add(id, command);
            }
        }

        internal bool TryGetCommand(int id, out RecoveryHotkeyCommand command) =>
            registered.TryGetValue(id, out command);

        internal bool TryDispatch(int id)
        {
            if (!registered.TryGetValue(id, out RecoveryHotkeyCommand command)) return false;
            dispatch?.Invoke(command);
            return true;
        }

        internal void ExitRecovery()
        {
            if (window != IntPtr.Zero)
            {
                foreach (int id in registered.Keys.ToArray()) registrar.Unregister(window, id);
            }
            registered.Clear();
            registrationResults.Clear();
            dispatch = null;
            window = IntPtr.Zero;
            RegistrationAttempted = false;
        }

        public void Dispose() => ExitRecovery();
    }

    internal readonly struct RecoveryHotkeyRegistration
    {
        internal RecoveryHotkeyRegistration(
            int id,
            Keys key,
            RecoveryHotkeyCommand command,
            bool succeeded,
            int errorCode)
        {
            Id = id;
            Key = key;
            Command = command;
            Succeeded = succeeded;
            ErrorCode = errorCode;
        }

        internal int Id { get; }
        internal Keys Key { get; }
        internal RecoveryHotkeyCommand Command { get; }
        internal bool Succeeded { get; }
        internal int ErrorCode { get; }
    }
}
