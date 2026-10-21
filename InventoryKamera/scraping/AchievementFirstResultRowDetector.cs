using System;
using System.Drawing;
using System.Globalization;

namespace InventoryKamera
{
    internal enum AchievementResultRowPresence
    {
        Unknown,
        Absent,
        Present,
    }

    internal sealed class AchievementResultRowEvidence
    {
        internal AchievementResultRowEvidence(
            AchievementResultRowPresence presence,
            int highlightedPixelCount,
            int highlightedColumnCount,
            int highlightedRowCount,
            string reason)
        {
            Presence = presence;
            HighlightedPixelCount = highlightedPixelCount;
            HighlightedColumnCount = highlightedColumnCount;
            HighlightedRowCount = highlightedRowCount;
            Reason = reason ?? string.Empty;
        }

        internal AchievementResultRowPresence Presence { get; }
        internal int HighlightedPixelCount { get; }
        internal int HighlightedColumnCount { get; }
        internal int HighlightedRowCount { get; }
        internal string Reason { get; }
        internal bool Present => Presence == AchievementResultRowPresence.Present;
        internal bool AbsenceAccepted => Presence == AchievementResultRowPresence.Absent;

        internal string Diagnostics =>
            $"presence={Presence}; highlightedPixels={HighlightedPixelCount.ToString(CultureInfo.InvariantCulture)}; " +
            $"highlightedColumns={HighlightedColumnCount.ToString(CultureInfo.InvariantCulture)}; " +
            $"highlightedRows={HighlightedRowCount.ToString(CultureInfo.InvariantCulture)}; reason={Reason}";

        internal static AchievementResultRowEvidence ForTest(AchievementResultRowPresence presence) =>
            new(
                presence,
                presence == AchievementResultRowPresence.Present ? 100 : 0,
                presence == AchievementResultRowPresence.Present ? 20 : 0,
                presence == AchievementResultRowPresence.Present ? 8 : 0,
                "Test row-presence evidence.");
    }

    /// <summary>
    /// Detects the gold/orange exact-query highlight in the first result title band without OCR.
    /// The first 20 reference pixels are ignored because the row ornament can touch the crop edge.
    /// A clearly absent highlight is meaningful only alongside the detector's positive Achievement
    /// and active-search evidence.
    /// </summary>
    internal static class AchievementFirstResultRowDetector
    {
        private const int ReferenceLeftInset = 20;
        private const int MinimumPixels = 24;
        private const int MinimumColumns = 6;
        private const int MinimumRows = 3;
        private const int MaximumAbsentPixels = 8;

        internal static AchievementResultRowEvidence Detect(
            Bitmap screenshot,
            Rectangle titleRegion)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            if (titleRegion.Width <= 0 || titleRegion.Height <= 0 ||
                titleRegion.Left < 0 || titleRegion.Top < 0 ||
                titleRegion.Right > screenshot.Width || titleRegion.Bottom > screenshot.Height)
                throw new ArgumentOutOfRangeException(nameof(titleRegion));

            int inset = Math.Min(
                titleRegion.Width,
                Math.Max(1, (int)Math.Round(
                    titleRegion.Width * (ReferenceLeftInset / 590.0),
                    MidpointRounding.AwayFromZero)));
            int pixels = 0;
            var columns = new bool[titleRegion.Width];
            var rows = new bool[titleRegion.Height];
            for (int y = 0; y < titleRegion.Height; y++)
            {
                for (int x = inset; x < titleRegion.Width; x++)
                {
                    Color color = screenshot.GetPixel(titleRegion.Left + x, titleRegion.Top + y);
                    if (!IsHighlightedTitlePixel(color)) continue;

                    pixels++;
                    columns[x] = true;
                    rows[y] = true;
                }
            }

            int columnCount = CountTrue(columns);
            int rowCount = CountTrue(rows);
            if (pixels >= MinimumPixels &&
                columnCount >= MinimumColumns &&
                rowCount >= MinimumRows)
            {
                return new AchievementResultRowEvidence(
                    AchievementResultRowPresence.Present,
                    pixels,
                    columnCount,
                    rowCount,
                    "The first result title contains the expected highlighted-title color structure.");
            }

            if (pixels <= MaximumAbsentPixels)
            {
                return new AchievementResultRowEvidence(
                    AchievementResultRowPresence.Absent,
                    pixels,
                    columnCount,
                    rowCount,
                    "No highlighted first-result title structure was found.");
            }

            return new AchievementResultRowEvidence(
                AchievementResultRowPresence.Unknown,
                pixels,
                columnCount,
                rowCount,
                "Sparse highlighted pixels were present but did not prove either a result row or a blank title band.");
        }

        private static bool IsHighlightedTitlePixel(Color color) =>
            color.R >= 150 &&
            color.G >= 75 &&
            color.B <= 145 &&
            color.R - color.G >= 25 &&
            color.G - color.B >= 10;

        private static int CountTrue(bool[] values)
        {
            int count = 0;
            foreach (bool value in values)
            {
                if (value) count++;
            }
            return count;
        }
    }
}
