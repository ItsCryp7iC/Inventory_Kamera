using System;
using System.Drawing;
using System.Windows.Forms;
using InventoryKamera.game;
using InventoryKamera.ui;

namespace InventoryKamera
{
    /// <summary>
    /// Transactional, owned pre-scan configuration dialog. Settings are copied into a draft on
    /// construction and written to the shared application settings only when Start Scan is chosen.
    /// </summary>
    internal sealed class ScanSetupForm : Form
    {
        private readonly ScanSetupController controller;
        private readonly GameDataSnapshot gameData;
        private readonly Label viGEmStatusHeading;
        private readonly Label viGEmStatusDescription;
        private readonly Button downloadButton;
        private readonly Button checkAgainButton;
        private readonly RadioButton defaultLayoutRadio;
        private readonly RadioButton customLayoutRadio;
        private readonly ComboBox layoutComboBox;
        private readonly RadioButton keyboardMouseModeRadio;
        private readonly RadioButton controllerModeRadio;
        private readonly TextBox travelerTextBox;
        private readonly TextBox wandererTextBox;
        private readonly TextBox manequin1TextBox;
        private readonly TextBox manequin2TextBox;
        private readonly Button startButton;

        internal ScanSetupForm(
            GameDataSnapshot gameData,
            IViGEmBusDetector detector = null,
            IExternalUrlLauncher urlLauncher = null)
        {
            this.gameData = gameData;
            controller = new ScanSetupController(
                ScanSetupDraft.Load(Properties.Settings.Default),
                detector ?? new ViGEmBusDetector(),
                urlLauncher ?? new ShellExternalUrlLauncher());

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 692);
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Scan Genshin";

            var virtualControllerGroup = new GroupBox
            {
                Location = new Point(12, 12),
                Size = new Size(576, 134),
                Text = "Virtual Controller"
            };
            viGEmStatusHeading = new Label
            {
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Location = new Point(16, 26)
            };
            viGEmStatusDescription = new Label
            {
                AutoSize = false,
                Location = new Point(16, 51),
                Size = new Size(540, 36)
            };
            downloadButton = new Button
            {
                AutoSize = true,
                Location = new Point(16, 93),
                TabIndex = 0,
                Text = "Download ViGEmBus"
            };
            downloadButton.Click += DownloadButton_Click;
            checkAgainButton = new Button
            {
                AutoSize = true,
                Location = new Point(165, 93),
                TabIndex = 1,
                Text = "Check Again"
            };
            checkAgainButton.Click += CheckAgainButton_Click;
            virtualControllerGroup.Controls.Add(viGEmStatusHeading);
            virtualControllerGroup.Controls.Add(viGEmStatusDescription);
            virtualControllerGroup.Controls.Add(downloadButton);
            virtualControllerGroup.Controls.Add(checkAgainButton);

            var inputModeGroup = new GroupBox
            {
                Location = new Point(12, 154),
                Size = new Size(576, 74),
                Text = "Current Genshin Input Mode"
            };
            keyboardMouseModeRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(19, 31),
                TabIndex = 2,
                Text = "Keyboard / Mouse"
            };
            controllerModeRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(190, 31),
                TabIndex = 3,
                Text = "Controller"
            };
            inputModeGroup.Controls.Add(keyboardMouseModeRadio);
            inputModeGroup.Controls.Add(controllerModeRadio);

            var controllerSettingsGroup = new GroupBox
            {
                Location = new Point(12, 236),
                Size = new Size(576, 177),
                Text = "Controller Settings"
            };
            controllerSettingsGroup.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(16, 26),
                Text = "Have you changed Genshin's Controller Confirm/Cancel setting?"
            });
            defaultLayoutRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(19, 54),
                TabIndex = 4,
                Text = "No / I don't know — use Genshin's default layout"
            };
            customLayoutRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(19, 80),
                TabIndex = 5,
                Text = "Yes, I changed it"
            };
            var layoutLabel = new Label
            {
                AutoSize = true,
                Location = new Point(38, 111),
                Text = "Controller Confirm / Cancel Layout:"
            };
            layoutComboBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(247, 107),
                Size = new Size(285, 23),
                TabIndex = 6
            };
            layoutComboBox.Items.Add(ControllerConfirmLayoutOptions.DisplayText(
                ControllerConfirmLayout.BConfirm_ACancel));
            layoutComboBox.Items.Add(ControllerConfirmLayoutOptions.DisplayText(
                ControllerConfirmLayout.AConfirm_BCancel));
            controllerSettingsGroup.Controls.Add(defaultLayoutRadio);
            controllerSettingsGroup.Controls.Add(customLayoutRadio);
            controllerSettingsGroup.Controls.Add(layoutLabel);
            controllerSettingsGroup.Controls.Add(layoutComboBox);
            controllerSettingsGroup.Controls.Add(new Label
            {
                AutoSize = false,
                ForeColor = Color.DimGray,
                Location = new Point(19, 140),
                Size = new Size(535, 30),
                Text = "Inventory Kamera uses a temporary virtual Xbox controller. " +
                       "Keyboard/Mouse mode is restored after the scan when it was active before starting."
            });

            var characterNamesGroup = new GroupBox
            {
                Location = new Point(12, 421),
                Size = new Size(576, 214),
                Text = "Character Names"
            };
            characterNamesGroup.Controls.Add(new Label
            {
                AutoSize = false,
                Location = new Point(16, 24),
                Size = new Size(540, 34),
                Text = "These names recognize characters whose in-game display name can differ " +
                       "from their canonical name. Blank values remain allowed."
            });

            travelerTextBox = AddNameField(characterNamesGroup, "Traveler's Name:", 62, 7);
            wandererTextBox = AddNameField(characterNamesGroup, "Wanderer's Name:", 94, 8);
            manequin1TextBox = AddNameField(characterNamesGroup, "Manequin's (f) Name:", 126, 9);
            manequin2TextBox = AddNameField(characterNamesGroup, "Manequin's (m) Name:", 158, 10);
            characterNamesGroup.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(190, 187),
                Text = "Yellow indicates a name that already belongs to another character."
            });

            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(421, 653),
                Size = new Size(80, 29),
                TabIndex = 12,
                Text = "Cancel"
            };
            startButton = new Button
            {
                Location = new Point(507, 653),
                Size = new Size(81, 29),
                TabIndex = 11,
                Text = "Start Scan"
            };
            startButton.Click += StartButton_Click;

            Controls.Add(virtualControllerGroup);
            Controls.Add(inputModeGroup);
            Controls.Add(controllerSettingsGroup);
            Controls.Add(characterNamesGroup);
            Controls.Add(cancelButton);
            Controls.Add(startButton);
            AcceptButton = startButton;
            CancelButton = cancelButton;

            LoadDraftIntoControls();
            defaultLayoutRadio.CheckedChanged += LayoutModeChanged;
            customLayoutRadio.CheckedChanged += LayoutModeChanged;
            layoutComboBox.SelectedIndexChanged += LayoutSelectionChanged;
            keyboardMouseModeRadio.CheckedChanged += InputModeChanged;
            controllerModeRadio.CheckedChanged += InputModeChanged;

            UiTheme.ApplyTheme(this);
            RefreshNameValidation();
            RefreshViGEmBusStatus();
        }

        private TextBox AddNameField(Control parent, string labelText, int top, int tabIndex)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(16, top + 4),
                Text = labelText
            });
            var textBox = new TextBox
            {
                Location = new Point(190, top),
                Size = new Size(342, 23),
                TabIndex = tabIndex
            };
            textBox.TextChanged += (_, _) => RefreshNameValidation(textBox);
            parent.Controls.Add(textBox);
            return textBox;
        }

        private void LoadDraftIntoControls()
        {
            ScanSetupDraft draft = controller.Draft;
            travelerTextBox.Text = draft.TravelerName ?? string.Empty;
            wandererTextBox.Text = draft.WandererName ?? string.Empty;
            manequin1TextBox.Text = draft.Manequin1Name ?? string.Empty;
            manequin2TextBox.Text = draft.Manequin2Name ?? string.Empty;
            layoutComboBox.SelectedIndex = (int)draft.CustomControllerLayout;
            defaultLayoutRadio.Checked = draft.UseDefaultControllerLayout;
            customLayoutRadio.Checked = !draft.UseDefaultControllerLayout;
            keyboardMouseModeRadio.Checked =
                draft.DeclaredOriginalInputMode == DeclaredInputMode.KeyboardMouse;
            controllerModeRadio.Checked =
                draft.DeclaredOriginalInputMode == DeclaredInputMode.Controller;
            RefreshLayoutControls();
        }

        private void InputModeChanged(object sender, EventArgs e)
        {
            if (!keyboardMouseModeRadio.Checked && !controllerModeRadio.Checked) return;
            controller.Draft.DeclaredOriginalInputMode = controllerModeRadio.Checked
                ? DeclaredInputMode.Controller
                : DeclaredInputMode.KeyboardMouse;
        }

        private void LayoutModeChanged(object sender, EventArgs e)
        {
            if (!defaultLayoutRadio.Checked && !customLayoutRadio.Checked) return;
            controller.SetUseDefaultControllerLayout(defaultLayoutRadio.Checked);
            if (controller.Draft.UseDefaultControllerLayout)
                layoutComboBox.SelectedIndex = (int)ControllerConfirmLayoutOptions.Default;
            RefreshLayoutControls();
        }

        private void LayoutSelectionChanged(object sender, EventArgs e)
        {
            if (layoutComboBox.SelectedIndex >= 0 && controller.IsLayoutSelectorEnabled)
            {
                controller.Draft.CustomControllerLayout =
                    ControllerConfirmLayoutOptions.FromPersistedValue(layoutComboBox.SelectedIndex);
            }
        }

        private void RefreshLayoutControls()
        {
            layoutComboBox.Enabled = controller.IsLayoutSelectorEnabled;
        }

        private void CheckAgainButton_Click(object sender, EventArgs e)
        {
            controller.RefreshViGEmBusStatus();
            RefreshViGEmBusStatus();
        }

        private void DownloadButton_Click(object sender, EventArgs e)
        {
            if (!controller.TryOpenViGEmBusDownload(out string error))
            {
                MessageBox.Show(
                    this,
                    "Inventory Kamera could not open the ViGEmBus download page.\n\n" + error,
                    "Could Not Open Browser",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RefreshViGEmBusStatus()
        {
            viGEmStatusHeading.Text = controller.ViGEmBusStatusHeading;
            viGEmStatusDescription.Text = controller.ViGEmBusStatusDescription;
            viGEmStatusHeading.ForeColor = controller.ViGEmBusStatus switch
            {
                ViGEmBusInstallationStatus.Installed => Color.DarkGreen,
                ViGEmBusInstallationStatus.NotInstalled => Color.DarkRed,
                _ => Color.DarkOrange
            };
            downloadButton.Visible = controller.CanDownloadViGEmBus;
            downloadButton.Enabled = controller.CanDownloadViGEmBus;
            startButton.Enabled = controller.CanStartScan;
        }

        private void RefreshNameValidation()
        {
            RefreshNameValidation(travelerTextBox);
            RefreshNameValidation(wandererTextBox);
            RefreshNameValidation(manequin1TextBox);
            RefreshNameValidation(manequin2TextBox);
        }

        private void RefreshNameValidation(TextBox textBox)
        {
            if (textBox == null) return;
            textBox.BackColor = !string.IsNullOrWhiteSpace(textBox.Text) &&
                SettingsForm.IsExistingCharacterName(textBox.Text, gameData)
                    ? Color.Yellow
                    : Color.White;
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            controller.Draft.TravelerName = travelerTextBox.Text;
            controller.Draft.WandererName = wandererTextBox.Text;
            controller.Draft.Manequin1Name = manequin1TextBox.Text;
            controller.Draft.Manequin2Name = manequin2TextBox.Text;
            controller.Draft.Save(Properties.Settings.Default);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
