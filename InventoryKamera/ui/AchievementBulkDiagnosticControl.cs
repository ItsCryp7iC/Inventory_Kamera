using System;
using System.Drawing;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    /// <summary>
    /// Places the Phase 7B.2 diagnostic in MainForm's scan-action column. Measurements are derived
    /// from the already auto-scaled Scan Genshin button, so runtime-created controls use the same
    /// DPI coordinate system as their designer-created siblings.
    /// </summary>
    internal sealed class AchievementBulkDiagnosticControl
    {
        private const int DesignStartButtonHeight = 68;
        private const int DesignDiagnosticButtonHeight = 29;
        private const int DesignVerticalGap = 7;

        private readonly Control[] following;
        private readonly Point[] originalLocations;
        private readonly Point[] achievementLocations;
        private readonly Label programStatus;
        private readonly bool originalProgramStatusAutoSize;
        private readonly bool originalProgramStatusAutoEllipsis;
        private readonly Size originalProgramStatusSize;
        private readonly int achievementColumnWidth;

        private AchievementBulkDiagnosticControl(Button button, Control[] following,
            Point[] originalLocations, Point[] achievementLocations,
            Label programStatus, int achievementColumnWidth)
        {
            Button = button;
            this.following = following;
            this.originalLocations = originalLocations;
            this.achievementLocations = achievementLocations;
            this.programStatus = programStatus;
            originalProgramStatusAutoSize = programStatus.AutoSize;
            originalProgramStatusAutoEllipsis = programStatus.AutoEllipsis;
            originalProgramStatusSize = programStatus.Size;
            this.achievementColumnWidth = achievementColumnWidth;
        }

        internal Button Button { get; }

        internal static AchievementBulkDiagnosticControl Create(
            Control parent,
            Button startScanButton,
            Control cancellationInstructions,
            Control programStatus,
            Control estimatedTimeRemaining,
            Control navigationLabel,
            Control navigationImage,
            EventHandler clickHandler)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            if (startScanButton == null) throw new ArgumentNullException(nameof(startScanButton));
            if (!ReferenceEquals(startScanButton.Parent, parent))
                throw new ArgumentException(
                    "The diagnostic and Scan Genshin buttons must share the same parent.",
                    nameof(startScanButton));
            if (programStatus is not Label programStatusLabel)
                throw new ArgumentException(
                    "The program status control must be a label.", nameof(programStatus));

            Control[] following =
            {
                cancellationInstructions ?? throw new ArgumentNullException(nameof(cancellationInstructions)),
                programStatus ?? throw new ArgumentNullException(nameof(programStatus)),
                estimatedTimeRemaining ?? throw new ArgumentNullException(nameof(estimatedTimeRemaining)),
                navigationLabel ?? throw new ArgumentNullException(nameof(navigationLabel)),
                navigationImage ?? throw new ArgumentNullException(nameof(navigationImage)),
            };
            foreach (Control control in following)
            {
                if (!ReferenceEquals(control.Parent, parent))
                    throw new ArgumentException(
                        "The scan-action column controls must share the MainForm parent.");
            }

            double scale = startScanButton.Height / (double)DesignStartButtonHeight;
            int gap = Math.Max(1, (int)Math.Round(DesignVerticalGap * scale));
            int buttonHeight = Math.Max(1,
                (int)Math.Round(DesignDiagnosticButtonHeight * scale));
            var button = new Button
            {
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7F),
                Location = new Point(startScanButton.Left, startScanButton.Bottom + gap),
                Name = "TestBulkScrolling_Button",
                Size = new Size(startScanButton.Width, buttonHeight),
                TabIndex = 121,
                Text = "Test Bulk Scrolling",
                UseVisualStyleBackColor = true,
            };
            if (clickHandler != null) button.Click += clickHandler;
            parent.Controls.Add(button);

            var originalLocations = new Point[following.Length];
            var achievementLocations = new Point[following.Length];
            int nextTop = button.Bottom + gap;
            for (int index = 0; index < following.Length; index++)
            {
                Control control = following[index];
                originalLocations[index] = control.Location;
                achievementLocations[index] = new Point(control.Left, nextTop);
                nextTop += control.Height + gap;
            }

            return new AchievementBulkDiagnosticControl(
                button, following, originalLocations, achievementLocations,
                programStatusLabel, startScanButton.Width);
        }

        internal void ApplyState(bool achievementModeSelected, bool activeRun)
        {
            Point[] locations = achievementModeSelected
                ? achievementLocations
                : originalLocations;
            for (int index = 0; index < following.Length; index++)
                following[index].Location = locations[index];

            if (achievementModeSelected)
            {
                programStatus.AutoSize = false;
                programStatus.AutoEllipsis = true;
                programStatus.Size = new Size(achievementColumnWidth,
                    originalProgramStatusSize.Height);
            }
            else
            {
                programStatus.AutoEllipsis = originalProgramStatusAutoEllipsis;
                programStatus.AutoSize = originalProgramStatusAutoSize;
                if (!originalProgramStatusAutoSize)
                    programStatus.Size = originalProgramStatusSize;
            }

            Button.Visible = AchievementBulkDiagnosticUiPolicy.IsControlVisible(
                achievementModeSelected);
            Button.Enabled = AchievementBulkDiagnosticUiPolicy.IsControlEnabled(
                achievementModeSelected, activeRun);
        }
    }
}
