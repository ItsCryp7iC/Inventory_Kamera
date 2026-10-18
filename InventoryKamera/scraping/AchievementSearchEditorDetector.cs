using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;

namespace InventoryKamera
{
    internal enum AchievementSearchEditorState
    {
        NotFocused,
        Focused,
        Unknown,
    }

    internal sealed class AchievementSearchEditorDetection
    {
        internal AchievementSearchEditorDetection(
            AchievementSearchEditorState state,
            double brightestBorderRowRatio,
            string reason)
        {
            State = state;
            BrightestBorderRowRatio = brightestBorderRowRatio;
            Reason = reason ?? string.Empty;
        }

        internal AchievementSearchEditorState State { get; }
        internal double BrightestBorderRowRatio { get; }
        internal string Reason { get; }
        internal string Evidence => string.Format(
            CultureInfo.InvariantCulture,
            "state={0}; brightestBorderRowRatio={1:0.000}; reason={2}",
            State,
            BrightestBorderRowRatio,
            Reason);
    }

    internal interface IAchievementSearchEditorDetector
    {
        AchievementSearchEditorDetection Detect(
            Bitmap screenshot,
            AchievementScreenDetection screenDetection);
    }

    /// <summary>
    /// Separately proves that the Achievement search editor has the current bright focus border.
    /// This deliberately does not change Phase 6A's SearchActive result-state contract.
    /// </summary>
    internal sealed class AchievementSearchEditorDetector : IAchievementSearchEditorDetector
    {
        private const int BrightLuminanceThreshold = 210;
        private const double FocusedRowRatio = 0.70;
        private const double UnfocusedRowRatio = 0.10;

        public AchievementSearchEditorDetection Detect(
            Bitmap screenshot,
            AchievementScreenDetection screenDetection)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            if (screenDetection == null) throw new ArgumentNullException(nameof(screenDetection));

            bool achievementContext =
                screenDetection.AchievementHeadingAccepted &&
                screenDetection.CompletedCountAccepted;
            Rectangle searchRegion = AchievementScreenRegions.For(screenshot.Size).SearchField;
            using Bitmap searchCrop = screenshot.Clone(searchRegion, PixelFormat.Format24bppRgb);
            double brightestRowRatio = FindBrightestCentralRowRatio(searchCrop);

            if (achievementContext && brightestRowRatio >= FocusedRowRatio)
            {
                return new AchievementSearchEditorDetection(
                    AchievementSearchEditorState.Focused,
                    brightestRowRatio,
                    "The Achievement screen is positively identified and the search control has its bright focus border.");
            }

            if (screenDetection.ScreenState == AchievementScreenState.Idle &&
                brightestRowRatio <= UnfocusedRowRatio)
            {
                return new AchievementSearchEditorDetection(
                    AchievementSearchEditorState.NotFocused,
                    brightestRowRatio,
                    "The idle Achievement search control does not have the bright focus border.");
            }

            return new AchievementSearchEditorDetection(
                AchievementSearchEditorState.Unknown,
                brightestRowRatio,
                achievementContext
                    ? "Achievement context was found, but the focus-border evidence was inconclusive."
                    : "The search border cannot prove focus without two positive Achievement-screen signals.");
        }

        private static double FindBrightestCentralRowRatio(Bitmap crop)
        {
            int left = Math.Max(0, (int)Math.Round(crop.Width * 0.08));
            int right = Math.Min(crop.Width, (int)Math.Round(crop.Width * 0.92));
            int sampledWidth = Math.Max(1, right - left);
            double maximum = 0;

            for (int y = 0; y < crop.Height; y++)
            {
                int bright = 0;
                for (int x = left; x < right; x++)
                {
                    Color pixel = crop.GetPixel(x, y);
                    double luminance =
                        0.2126 * pixel.R +
                        0.7152 * pixel.G +
                        0.0722 * pixel.B;
                    if (luminance >= BrightLuminanceThreshold) bright++;
                }

                maximum = Math.Max(maximum, bright / (double)sampledWidth);
            }

            return maximum;
        }
    }
}
