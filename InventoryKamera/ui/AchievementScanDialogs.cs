using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    internal sealed class AchievementPreScanWarningForm : Form
    {
        internal AchievementPreScanWarningForm()
        {
            Text = "Before Scanning Achievements";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(530, 245);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var message = new Label
            {
                AutoSize = false,
                Location = new Point(18, 18),
                Size = new Size(494, 155),
                Text = "Achievement Scanner works best when all pending achievement rewards have been claimed.\n\n" +
                    "For staged achievements, an unclaimed reward can hide later stages that may already be completed.\n\n" +
                    "You can continue without claiming them, but those achievements may be paused or skipped during the scan.",
            };
            var continueButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(292, 190),
                Size = new Size(105, 35),
                Text = "Continue",
            };
            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(407, 190),
                Size = new Size(105, 35),
                Text = "Cancel",
            };
            Controls.Add(message);
            Controls.Add(continueButton);
            Controls.Add(cancelButton);
            AcceptButton = continueButton;
            CancelButton = cancelButton;
        }
    }

    internal sealed class AchievementPendingClaimForm : Form
    {
        internal AchievementPendingClaimForm(string achievementTitle)
        {
            Text = "Unclaimed Achievement Reward";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(570, 275);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var heading = new Label
            {
                AutoSize = false,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                Location = new Point(18, 18),
                Size = new Size(534, 28),
                Text = achievementTitle ?? string.Empty,
            };
            var message = new Label
            {
                AutoSize = false,
                Location = new Point(18, 51),
                Size = new Size(534, 145),
                Text = "This achievement has an unclaimed reward. For staged achievements, later completed stages may be hidden until pending rewards are claimed.\n\n" +
                    "You may claim rewards manually in Genshin. Return to an achievement search-ready screen, then choose Resume to scan this same achievement again.",
            };
            var resumeButton = new Button
            {
                DialogResult = DialogResult.Retry,
                Location = new Point(214, 218),
                Size = new Size(100, 35),
                Text = "Resume",
            };
            var skipButton = new Button
            {
                DialogResult = DialogResult.Ignore,
                Location = new Point(324, 218),
                Size = new Size(110, 35),
                Text = "Skip Achievement",
            };
            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(444, 218),
                Size = new Size(108, 35),
                Text = "Cancel Scan",
            };
            Controls.Add(heading);
            Controls.Add(message);
            Controls.Add(resumeButton);
            Controls.Add(skipButton);
            Controls.Add(cancelButton);
            AcceptButton = resumeButton;
            CancelButton = cancelButton;
        }
    }

    internal sealed class AchievementIssueReportForm : Form
    {
        internal AchievementIssueReportForm(string report)
        {
            Text = "Achievement Scan - Skipped and Unresolved";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(680, 440);
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            var reportBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Text = report ?? string.Empty,
            };
            var closeButton = new Button
            {
                DialogResult = DialogResult.OK,
                Dock = DockStyle.Bottom,
                Height = 38,
                Text = "Close",
            };
            Controls.Add(reportBox);
            Controls.Add(closeButton);
            AcceptButton = closeButton;
            CancelButton = closeButton;
        }
    }

    internal sealed class AchievementBulkDiagnosticStartForm : Form
    {
        private readonly RadioButton shortDiagnostic;
        private readonly RadioButton fullTraversal;
        private readonly Label modeDescription;

        internal AchievementBulkDiagnosticStartForm()
        {
            Text = "Achievement Bulk Diagnostics";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(570, 423);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var message = new Label
            {
                AutoSize = false,
                Location = new Point(18, 18),
                Size = new Size(534, 154),
                Text = "Both read-only diagnostics test one manually selected Achievement category. " +
                    "They collect recognition and traversal evidence without saving or exporting achievement progress.\n\n" +
                    "Before continuing:\n" +
                    "1. Use Keyboard/Mouse mode in Genshin.\n" +
                    "2. Open Achievements and select the category to test.\n" +
                    "3. Move that category's achievement list to the top.\n" +
                    "4. Leave the ordinary category list visible with no search or dialog open.",
            };
            var modeGroup = new GroupBox
            {
                Location = new Point(18, 178),
                Size = new Size(534, 103),
                Text = "Diagnostic mode",
            };
            shortDiagnostic = new RadioButton
            {
                AutoSize = true,
                Checked = true,
                Location = new Point(15, 25),
                Text = "Test Bulk Scrolling (5-frame diagnostic)",
            };
            fullTraversal = new RadioButton
            {
                AutoSize = true,
                Location = new Point(15, 60),
                Text = "Verify Full Category Traversal",
            };
            shortDiagnostic.CheckedChanged += (_, _) => UpdateModeDescription();
            fullTraversal.CheckedChanged += (_, _) => UpdateModeDescription();
            modeGroup.Controls.Add(shortDiagnostic);
            modeGroup.Controls.Add(fullTraversal);

            modeDescription = new Label
            {
                AutoSize = false,
                Location = new Point(18, 292),
                Size = new Size(534, 70),
            };
            var startButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(332, 370),
                Size = new Size(105, 35),
                Text = "Start Test",
            };
            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(447, 370),
                Size = new Size(105, 35),
                Text = "Cancel",
            };
            Controls.Add(message);
            Controls.Add(modeGroup);
            Controls.Add(modeDescription);
            Controls.Add(startButton);
            Controls.Add(cancelButton);
            AcceptButton = startButton;
            CancelButton = cancelButton;
            UpdateModeDescription();
        }

        internal AchievementBulkDiagnosticMode SelectedMode => fullTraversal.Checked
            ? AchievementBulkDiagnosticMode.FullTraversal
            : AchievementBulkDiagnosticMode.Short;

        internal void SelectMode(AchievementBulkDiagnosticMode mode)
        {
            if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            fullTraversal.Checked = mode == AchievementBulkDiagnosticMode.FullTraversal;
            shortDiagnostic.Checked = mode == AchievementBulkDiagnosticMode.Short;
            UpdateModeDescription();
        }

        internal string ModeDescription => modeDescription.Text;

        private void UpdateModeDescription()
        {
            modeDescription.Text = SelectedMode == AchievementBulkDiagnosticMode.FullTraversal
                ? "Starts only after positive top evidence, then scrolls through the selected category to verify its real bottom. " +
                  "This may take longer, is bounded to 80 frames / 79 batches, and can be cancelled with F8. No progress is saved or exported."
                : "Runs the existing short diagnostic: at most 5 frames and 4 scroll batches. " +
                  "It may stop before the category bottom. Press F8 to cancel; no progress is saved or exported.";
        }
    }
}
