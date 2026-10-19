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

    internal sealed class AchievementStartHandoffForm : Form
    {
        internal AchievementStartHandoffForm()
        {
            Text = "Achievement Scanner";
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 205);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;

            var message = new Label
            {
                AutoSize = false,
                Location = new Point(18, 18),
                Size = new Size(464, 112),
                Text = "Open Genshin's Achievements screen and make sure no search is active.\n\n" +
                    "You may interact with Genshin while this window is open. Return here and click Continue when the Achievement screen is ready.",
            };
            var continueButton = new Button
            {
                DialogResult = DialogResult.OK,
                Location = new Point(222, 150),
                Size = new Size(110, 35),
                Text = "Continue",
            };
            var cancelButton = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(342, 150),
                Size = new Size(140, 35),
                Text = "Cancel Achievement Scan",
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
}
