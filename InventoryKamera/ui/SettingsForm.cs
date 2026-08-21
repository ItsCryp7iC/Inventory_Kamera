using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;
using WindowsInput.Native;

namespace InventoryKamera.ui
{
    public partial class SettingsForm : Form
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public SettingsForm()
        {
            InitializeComponent();

            UiTheme.RoundCorners(FileSelectButton, 4);
            UiTheme.RoundCorners(CloseButton, 6);

            LoadKeyBindingDisplays();
            DarkModeCheckBox.Checked = Properties.Settings.Default.DarkMode;

            UiTheme.ApplyTheme(this);
        }

        // Reflects the current key bindings (kept in Navigation, synced from settings at app start)
        // into the three capture textboxes, converting OEM keys to their printed glyph.
        private void LoadKeyBindingDisplays()
        {
            inventoryKeyTextBox.Text = KeyDisplayText((Keys)Navigation.inventoryKey);
            characterKeyTextBox.Text = KeyDisplayText((Keys)Navigation.characterKey);
            slot1KeyTextBox.Text = KeyDisplayText((Keys)Navigation.slotOneKey);
        }

        private string KeyDisplayText(Keys key)
        {
            var text = new KeysConverter().ConvertToString(key);
            return text != null && text.ToUpper().Contains("OEM") ? KeyCodeToUnicode(key) : text;
        }

        // Press-a-key capture for the three Genshin key bindings. Ported from the old Options-menu
        // textboxes; writes both the live Navigation key and the persisted setting so a rebind takes
        // effect immediately and survives restarts. Dialog-close re-syncs MainForm (see
        // MainForm.AdvancedSettingsMenuItem_Click).
        private void KeyBinding_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            // Virtual keys for 0-9, A-Z
            bool vk = e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.Z;
            // Numpad keys and function keys (internally accepts up to F24)
            bool np = e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.F24;
            // OEM keys (Keys that vary depending on keyboard layout)
            bool oem = e.KeyCode >= Keys.Oem1 && e.KeyCode <= Keys.Oem7;
            // Arrow keys, spacebar, INS, DEL, HOME, END, PAGEUP, PAGEDOWN
            bool misc = e.KeyCode == Keys.Space || (e.KeyCode >= Keys.Left && e.KeyCode <= Keys.Down) || (e.KeyCode >= Keys.Prior && e.KeyCode <= Keys.Home) || e.KeyCode == Keys.Insert || e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back;

            // Validate that key is an acceptable Genshin keybind.
            if (!vk && !np && !oem && !misc)
            {
                Logger.Debug("Invalid {key} key pressed", e.KeyCode);
                return;
            }
            TextBox s = (TextBox)sender;

            // Needed to differentiate between NUMPAD numbers and numbers at top of keyboard
            s.Text = np || e.KeyCode == Keys.Back ? new KeysConverter().ConvertToString(e.KeyCode) : KeyCodeToUnicode(e.KeyData);

            // Spacebar or upper navigation keys (INSERT-PAGEDOWN keys) make textbox empty
            if (string.IsNullOrWhiteSpace(s.Text) || string.IsNullOrEmpty(s.Text))
            {
                s.Text = new KeysConverter().ConvertToString(e.KeyCode);
            }

            switch (s.Tag)
            {
                case "InventoryKey":
                    Navigation.inventoryKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Inv key set to: {key}", Navigation.inventoryKey);
                    Properties.Settings.Default.InventoryKey = e.KeyValue;
                    break;

                case "CharacterKey":
                    Navigation.characterKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Char key set to: {key}", Navigation.characterKey);
                    Properties.Settings.Default.CharacterKey = e.KeyValue;
                    break;

                case "slot1Key":
                    Navigation.slotOneKey = (VirtualKeyCode)e.KeyCode;
                    Logger.Debug("Slot 1 key set to: {key}", Navigation.slotOneKey);
                    Properties.Settings.Default.Slot1Key = e.KeyValue;
                    break;

                default:
                    break;
            }
        }

        private void DarkModeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.DarkMode = DarkModeCheckBox.Checked;
            Properties.Settings.Default.Save();
            // Live preview on this dialog; MainForm re-themes itself when the dialog closes.
            UiTheme.ApplyTheme(this);
        }

        #region Unicode Helper Functions

        // Needed to display OEM keys as glyphs from keyboard. Should work for other languages
        // and keyboard layouts but only tested with QWERTY layout.
        private string KeyCodeToUnicode(Keys key)
        {
            byte[] keyboardState = new byte[255];
            bool keyboardStateStatus = GetKeyboardState(keyboardState);

            if (!keyboardStateStatus)
            {
                return "";
            }
            uint virtualKeyCode = (uint)key;
            uint scanCode = MapVirtualKey(virtualKeyCode, 0);
            IntPtr inputLocaleIdentifier = GetKeyboardLayout(0);

            StringBuilder result = new StringBuilder();
            ToUnicodeEx(virtualKeyCode, scanCode, keyboardState, result, 5, 0, inputLocaleIdentifier);

            return result.ToString();
        }

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState, [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);

        #endregion Unicode Helper Functions

        private void ValidateCustomName(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            var name = textbox.Text;

            if (!string.IsNullOrWhiteSpace(name))
            {
                textbox.BackColor = GenshinProcesor.Characters.ContainsKey(name.ConvertToGood().ToLower())
                    ? Color.Yellow
                    : Color.White;
            }
        }

        private void ValidateCustomName1(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            var name = textbox.Text;

            if (!string.IsNullOrWhiteSpace(name))
            {
                textbox.BackColor = GenshinProcesor.Characters.ContainsKey(name.ConvertToGood().ToLower())
                    ? Color.Yellow
                    : Color.White;
            }
        }

        private void ValidateCustomName2(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            var name = textbox.Text;

            if (!string.IsNullOrWhiteSpace(name))
            {
                textbox.BackColor = GenshinProcesor.Characters.ContainsKey(name.ConvertToGood().ToLower())
                    ? Color.Yellow
                    : Color.White;
            }
        }

        private void DisplayCustomNameTooltip(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;

            if (textbox.BackColor == Color.Yellow)
            {
                var tooltip = new ToolTip();
                tooltip.Show($"{textbox.Text} already exists as a character's name.\n" +
                    $"This may affect equipping items to characters and is not fully supported yet.", textbox);
            }
        }

        private void FileSelectButton_Click(object sender, EventArgs e)
        {
            // A nicer file browser
            CommonOpenFileDialog d = new CommonOpenFileDialog
            {
                InitialDirectory = !System.IO.Directory.Exists(OutputPath_TextBox.Text) ? System.IO.Directory.GetCurrentDirectory() : OutputPath_TextBox.Text,
                IsFolderPicker = true
            };

            if (d.ShowDialog() == CommonFileDialogResult.Ok)
            {
                OutputPath_TextBox.Text = d.FileName;
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
