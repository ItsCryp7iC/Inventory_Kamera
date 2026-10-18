using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using WindowsInput.Native;

namespace InventoryKamera.game
{
    /// <summary>
    /// Backend-neutral text replacement used by game controls that have already been positively
    /// verified as editable. Controller submission is intentionally outside this abstraction.
    /// </summary>
    internal interface IGameTextInput
    {
        void ReplaceAll(string text, CancellationToken cancellationToken);
    }

    internal interface IGameKeyboardTextBackend
    {
        void SelectAll();
        void DeleteSelection();
        void TypeText(string text);
    }

    internal sealed class SendInputGameKeyboardTextBackend : IGameKeyboardTextBackend
    {
        private const uint InputKeyboard = 1;
        private const uint KeyEventKeyUp = 0x0002;
        private const uint KeyEventUnicode = 0x0004;
        private const uint KeyEventScanCode = 0x0008;
        private const uint MapVkToScanCode = 0;
        private const int VkCapital = 0x14;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern short VkKeyScan(char character);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

        internal static int NativeInputSize => Marshal.SizeOf<NativeInput>();

        public void SelectAll() => SendModifiedKey(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_A);

        public void DeleteSelection() => SendKey(VirtualKeyCode.BACK);

        public void TypeText(string text)
        {
            foreach (char character in text)
            {
                short scan = VkKeyScan(character);
                if (scan == -1)
                {
                    SendUnicode(character);
                    continue;
                }

                var key = (VirtualKeyCode)(scan & 0xff);
                int modifiers = (scan >> 8) & 0xff;
                bool requiresControlOrAlt = (modifiers & 0x06) != 0;
                if (requiresControlOrAlt)
                {
                    // Unicode packets preserve exact text when the active keyboard layout would
                    // otherwise require AltGr or cannot safely express the character.
                    SendUnicode(character);
                    continue;
                }

                bool shift = (modifiers & 0x01) != 0;
                bool isLetter = char.IsLetter(character) && character <= 0x7f;
                bool capsLock = (GetKeyState(VkCapital) & 0x0001) != 0;
                if (isLetter && capsLock) shift = !shift;

                if (shift)
                    SendModifiedKey(VirtualKeyCode.SHIFT, key);
                else
                    SendKey(key);
            }
        }

        private static void SendKey(VirtualKeyCode key) => SendScanCodeChord(null, key);

        private static void SendModifiedKey(VirtualKeyCode modifier, VirtualKeyCode key) =>
            SendScanCodeChord(modifier, key);

        private static void SendScanCodeChord(VirtualKeyCode? modifier, VirtualKeyCode key)
        {
            ushort keyScanCode = GetScanCode(key);
            if (modifier.HasValue)
            {
                ushort modifierScanCode = GetScanCode(modifier.Value);
                Send(
                    ScanCodeInput(modifierScanCode, keyUp: false),
                    ScanCodeInput(keyScanCode, keyUp: false),
                    ScanCodeInput(keyScanCode, keyUp: true),
                    ScanCodeInput(modifierScanCode, keyUp: true));
                return;
            }

            Send(
                ScanCodeInput(keyScanCode, keyUp: false),
                ScanCodeInput(keyScanCode, keyUp: true));
        }

        private static ushort GetScanCode(VirtualKeyCode key)
        {
            uint scanCode = MapVirtualKey((uint)key, MapVkToScanCode);
            if (scanCode == 0)
                throw new InvalidOperationException($"No keyboard scan code is available for virtual key {key}.");
            return checked((ushort)scanCode);
        }

        private static NativeInput ScanCodeInput(ushort scanCode, bool keyUp) => new()
        {
            Type = InputKeyboard,
            Union = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    ScanCode = scanCode,
                    Flags = KeyEventScanCode | (keyUp ? KeyEventKeyUp : 0)
                }
            }
        };

        private static void SendUnicode(char character)
        {
            Send(
                UnicodeInput(character, keyUp: false),
                UnicodeInput(character, keyUp: true));
        }

        private static NativeInput UnicodeInput(char character, bool keyUp) => new()
        {
            Type = InputKeyboard,
            Union = new NativeInputUnion
            {
                Keyboard = new NativeKeyboardInput
                {
                    ScanCode = character,
                    Flags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0)
                }
            }
        };

        private static void Send(params NativeInput[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
            if (sent != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not deliver the requested keyboard input.");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeInput
        {
            internal uint Type;
            internal NativeInputUnion Union;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct NativeInputUnion
        {
            [FieldOffset(0)]
            internal NativeKeyboardInput Keyboard;

            // INPUT is a tagged union. Including its largest member is required so cbSize is
            // 28 bytes on x86 and 40 bytes on x64 even though this backend sends only keyboard
            // packets. An undersized keyboard-only union is rejected by SendInput.
            [FieldOffset(0)]
            internal NativeMouseInput Mouse;
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
    }

    internal sealed class GameTextInput : IGameTextInput
    {
        private readonly IGameKeyboardTextBackend backend;

        internal GameTextInput()
            : this(new SendInputGameKeyboardTextBackend())
        {
        }

        internal GameTextInput(IGameKeyboardTextBackend backend)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public void ReplaceAll(string text, CancellationToken cancellationToken)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Achievement search text cannot be empty or whitespace-only.", nameof(text));

            cancellationToken.ThrowIfCancellationRequested();
            backend.SelectAll();
            cancellationToken.ThrowIfCancellationRequested();
            backend.DeleteSelection();
            cancellationToken.ThrowIfCancellationRequested();
            backend.TypeText(text);
        }
    }
}
