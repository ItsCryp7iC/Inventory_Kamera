using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace InventoryKamera
{
    internal sealed class AchievementVisibleRowEvidence
    {
        internal AchievementVisibleRowEvidence(AchievementListRowGeometry geometry,
            bool emblemEvidence, bool titleBandEvidence, bool rightSideEvidence,
            string diagnostics, bool summaryHeader = false)
        {
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            EmblemEvidence = emblemEvidence;
            TitleBandEvidence = titleBandEvidence;
            RightSideEvidence = rightSideEvidence;
            Diagnostics = diagnostics ?? string.Empty;
            SummaryHeader = summaryHeader;
        }

        internal AchievementListRowGeometry Geometry { get; }
        internal bool EmblemEvidence { get; }
        internal bool TitleBandEvidence { get; }
        internal bool RightSideEvidence { get; }
        internal bool SummaryHeader { get; }
        internal bool Accepted => !SummaryHeader && EmblemEvidence &&
            (TitleBandEvidence || RightSideEvidence);
        internal string Diagnostics { get; }
    }

    internal sealed class AchievementVisibleRowDetection
    {
        internal AchievementVisibleRowDetection(IEnumerable<AchievementVisibleRowEvidence> slots)
        {
            Slots = new ReadOnlyCollection<AchievementVisibleRowEvidence>(
                (slots ?? Array.Empty<AchievementVisibleRowEvidence>()).ToArray());
            VisibleRows = new ReadOnlyCollection<AchievementListRowGeometry>(
                Slots.Where(slot => slot.Accepted).Select(slot => slot.Geometry).ToArray());
            Diagnostics = string.Join(Environment.NewLine, Slots.Select(slot => slot.Diagnostics));
        }

        internal IReadOnlyList<AchievementVisibleRowEvidence> Slots { get; }
        internal IReadOnlyList<AchievementListRowGeometry> VisibleRows { get; }
        internal string Diagnostics { get; }
    }

    internal interface IAchievementVisibleRowDetector
    {
        AchievementVisibleRowDetection Detect(Bitmap screenshot);
    }

    internal sealed class AchievementVisibleRowDetector : IAchievementVisibleRowDetector
    {
        internal const double MinimumBorderRatio = 0.82;
        internal const double MinimumGoldEmblemRatio = 0.02;
        internal const double MinimumTitleStandardDeviation = 8.0;
        internal const double MinimumSupportingStandardDeviation = 6.0;
        internal const double MinimumSummaryProgressBarRatio = 0.80;

        public AchievementVisibleRowDetection Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            AchievementListRegions regions = AchievementListRegions.For(screenshot.Size);
            int nominalHeight = AchievementListRegions.ScaleReferenceY(AchievementListRegions.RowHeight, screenshot.Size);
            int minHeight = AchievementListRegions.ScaleReferenceY(112, screenshot.Size);
            int maxHeight = AchievementListRegions.ScaleReferenceY(126, screenshot.Size);
            int[] borders = FindBorderCenters(screenshot, regions.BorderScanBand);
            var segments = new List<(int Top, int Bottom, AchievementVisibleRowVisibility Visibility)>();
            var used = new HashSet<int>();

            for (int i = 0; i + 1 < borders.Length; i++)
            {
                int height = borders[i + 1] - borders[i] + 1;
                if (height < minHeight || height > maxHeight) continue;
                segments.Add((borders[i], borders[i + 1] + 1, AchievementVisibleRowVisibility.Complete));
                used.Add(i);
                used.Add(i + 1);
            }
            for (int i = 0; i < borders.Length; i++)
            {
                if (used.Contains(i)) continue;
                int border = borders[i];
                int expectedTop = border - nominalHeight + 1;
                if (expectedTop < regions.RowViewport.Top && border > regions.RowViewport.Top)
                    segments.Add((expectedTop, border + 1, AchievementVisibleRowVisibility.Partial));
                else if (border < regions.RowViewport.Bottom && border + nominalHeight > regions.RowViewport.Bottom)
                    segments.Add((border, border + nominalHeight, AchievementVisibleRowVisibility.Partial));
            }

            // A summary card can visually occlude the long borders of the first achievement row.
            // Preserve the structurally populated one-row gap as Uncertain; it must not be trusted.
            var orderedSegments = segments.OrderBy(value => value.Top).ToArray();
            for (int i = 0; i + 1 < orderedSegments.Length; i++)
            {
                int gap = orderedSegments[i + 1].Top - orderedSegments[i].Bottom;
                if (gap >= AchievementListRegions.ScaleReferenceY(70, screenshot.Size) &&
                    gap <= AchievementListRegions.ScaleReferenceY(140, screenshot.Size))
                {
                    segments.Add((orderedSegments[i].Bottom + AchievementListRegions.ScaleReferenceY(4, screenshot.Size),
                        orderedSegments[i + 1].Top - AchievementListRegions.ScaleReferenceY(5, screenshot.Size),
                        AchievementVisibleRowVisibility.Uncertain));
                }
            }

            var results = new List<AchievementVisibleRowEvidence>();
            int index = 0;
            foreach (var segment in segments.OrderBy(value => value.Top))
            {
                int visibleTop = segment.Visibility == AchievementVisibleRowVisibility.Complete
                    ? segment.Top
                    : Math.Max(segment.Top, regions.RowViewport.Top);
                int visibleBottom = segment.Visibility == AchievementVisibleRowVisibility.Complete
                    ? segment.Top + nominalHeight
                    : Math.Min(segment.Bottom, regions.RowViewport.Bottom);
                AchievementListRowGeometry row = AchievementListRegions.CreateDetectedRow(
                    screenshot.Size, index++, segment.Top, visibleTop, visibleBottom, segment.Visibility);
                Rectangle emblemRegion = Rectangle.Intersect(row.Emblem, row.VisibleBounds);
                double emblemStdDev = StandardDeviation(screenshot, emblemRegion);
                double goldRatio = GoldRatio(screenshot, emblemRegion);
                double titleStdDev = StandardDeviation(screenshot, Rectangle.Intersect(row.Title, row.VisibleBounds));
                double rightStdDev = StandardDeviation(screenshot, Rectangle.Intersect(row.CompletionState, row.VisibleBounds));
                double progressBarRatio = ProgressBarRatio(screenshot, row);
                bool emblemEvidence = goldRatio >= MinimumGoldEmblemRatio && emblemStdDev >= MinimumSupportingStandardDeviation;
                bool titleEvidence = titleStdDev >= MinimumTitleStandardDeviation;
                bool rightEvidence = rightStdDev >= MinimumSupportingStandardDeviation;
                bool summaryHeader = row.Bounds.Top <= AchievementListRegions.ScaleReferenceY(170, screenshot.Size) &&
                    goldRatio < MinimumGoldEmblemRatio && progressBarRatio >= MinimumSummaryProgressBarRatio;
                bool accepted = !summaryHeader && emblemEvidence && (titleEvidence || rightEvidence);
                string diagnostics = string.Format(CultureInfo.InvariantCulture,
                    "rowSegment={0}; expectedBounds={1}; visibleBounds={2}; visibility={3}; accepted={4}; summaryHeader={5}; emblemGoldRatio={6:0.000}; emblemStdDev={7:0.00}; titleStdDev={8:0.00}; rightStdDev={9:0.00}; progressBarRatio={10:0.000}",
                    row.Index, row.Bounds, row.VisibleBounds, row.Visibility, accepted, summaryHeader,
                    goldRatio, emblemStdDev, titleStdDev, rightStdDev, progressBarRatio);
                results.Add(new AchievementVisibleRowEvidence(row, emblemEvidence, titleEvidence,
                    rightEvidence, diagnostics, summaryHeader));
            }
            return new AchievementVisibleRowDetection(results);
        }

        private static int[] FindBorderCenters(Bitmap bitmap, Rectangle band)
        {
            var runs = new List<(int Start, int End)>();
            int start = -1;
            for (int y = band.Top; y < band.Bottom; y++)
            {
                bool border = BorderRatio(bitmap, band.Left, band.Right, y) >= MinimumBorderRatio;
                if (border && start < 0) start = y;
                else if (!border && start >= 0) { runs.Add((start, y - 1)); start = -1; }
            }
            if (start >= 0) runs.Add((start, band.Bottom - 1));
            return runs.Select(run => (run.Start + run.End) / 2).ToArray();
        }

        private static double BorderRatio(Bitmap bitmap, int left, int right, int y)
        {
            int hits = 0, count = 0;
            for (int x = left; x < right; x += 4)
            {
                Color p = bitmap.GetPixel(x, y);
                if (p.R >= 195 && p.R <= 235 && p.G >= 185 && p.G <= 225 && p.B >= 170 && p.B <= 212) hits++;
                count++;
            }
            return count == 0 ? 0 : (double)hits / count;
        }

        private static double GoldRatio(Bitmap bitmap, Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0) return 0;
            int hits = 0, count = 0;
            for (int y = region.Top; y < region.Bottom; y += 3)
                for (int x = region.Left; x < region.Right; x += 3)
                {
                    Color p = bitmap.GetPixel(x, y);
                    if (p.R >= 135 && p.G >= 92 && p.G <= 190 && p.B <= 105 && p.R >= p.G + 20) hits++;
                    count++;
                }
            return count == 0 ? 0 : (double)hits / count;
        }

        private static double ProgressBarRatio(Bitmap bitmap, AchievementListRowGeometry row)
        {
            Rectangle region = new(row.Title.Left,
                row.Bounds.Top + AchievementListRegions.ScaleReferenceY(45, bitmap.Size),
                AchievementListRegions.ScaleReferenceX(794, bitmap.Size),
                AchievementListRegions.ScaleReferenceY(38, bitmap.Size));
            region.Width = Math.Min(region.Width, row.CompletionState.Left - region.Left - 8);
            region = Rectangle.Intersect(region, row.VisibleBounds);
            double best = 0;
            for (int y = region.Top; y < region.Bottom; y++)
            {
                int hits = 0, count = 0;
                for (int x = region.Left; x < region.Right; x += 2)
                {
                    Color p = bitmap.GetPixel(x, y);
                    bool yellow = p.R > 185 && p.G > 110 && p.B < 115 && p.R > p.G;
                    bool gray = Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) < 15 && p.R < 175;
                    if (yellow || gray) hits++;
                    count++;
                }
                if (count > 0) best = Math.Max(best, (double)hits / count);
            }
            return best;
        }

        private static double StandardDeviation(Bitmap bitmap, Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0) return 0;
            double sum = 0, squares = 0;
            int count = 0;
            for (int y = region.Top; y < region.Bottom; y += 4)
                for (int x = region.Left; x < region.Right; x += 4)
                {
                    Color p = bitmap.GetPixel(x, y);
                    double value = (0.2126 * p.R) + (0.7152 * p.G) + (0.0722 * p.B);
                    sum += value;
                    squares += value * value;
                    count++;
                }
            if (count == 0) return 0;
            double mean = sum / count;
            return Math.Sqrt(Math.Max(0, (squares / count) - (mean * mean)));
        }
    }
}
