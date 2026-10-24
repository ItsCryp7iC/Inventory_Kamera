using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InventoryKamera
{
    internal sealed class AchievementVisibleRowEvidence
    {
        internal AchievementVisibleRowEvidence(
            AchievementListRowGeometry geometry,
            bool emblemEvidence,
            bool titleBandEvidence,
            bool rightSideEvidence,
            string diagnostics)
        {
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            EmblemEvidence = emblemEvidence;
            TitleBandEvidence = titleBandEvidence;
            RightSideEvidence = rightSideEvidence;
            Diagnostics = diagnostics ?? string.Empty;
        }

        internal AchievementListRowGeometry Geometry { get; }
        internal bool EmblemEvidence { get; }
        internal bool TitleBandEvidence { get; }
        internal bool RightSideEvidence { get; }
        internal bool Accepted => TitleBandEvidence && (EmblemEvidence || RightSideEvidence);
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

    /// <summary>
    /// Finds ordinary-list rows from fixed row slots plus positive visual structure. OCR is not
    /// used for discovery: a title-like ink band and either emblem or right-side control/reward
    /// structure must be present.
    /// </summary>
    internal sealed class AchievementVisibleRowDetector : IAchievementVisibleRowDetector
    {
        internal const double MinimumTitleStandardDeviation = 8.0;
        internal const double MinimumSupportingStandardDeviation = 6.0;

        public AchievementVisibleRowDetection Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            AchievementListRegions regions = AchievementListRegions.For(screenshot.Size);
            var results = new List<AchievementVisibleRowEvidence>(regions.Rows.Count);
            foreach (AchievementListRowGeometry row in regions.Rows)
            {
                double emblem = StandardDeviation(screenshot, row.Emblem);
                double title = StandardDeviation(screenshot, row.Title);
                double right = StandardDeviation(screenshot, row.CompletionState);
                bool emblemEvidence = emblem >= MinimumSupportingStandardDeviation;
                bool titleEvidence = title >= MinimumTitleStandardDeviation;
                bool rightEvidence = right >= MinimumSupportingStandardDeviation;
                string diagnostics = string.Format(
                    CultureInfo.InvariantCulture,
                    "rowSlot={0}; accepted={1}; emblemStdDev={2:0.00}; titleStdDev={3:0.00}; rightStdDev={4:0.00}",
                    row.Index,
                    titleEvidence && (emblemEvidence || rightEvidence),
                    emblem,
                    title,
                    right);
                results.Add(new AchievementVisibleRowEvidence(
                    row,
                    emblemEvidence,
                    titleEvidence,
                    rightEvidence,
                    diagnostics));
            }

            return new AchievementVisibleRowDetection(results);
        }

        private static double StandardDeviation(Bitmap bitmap, Rectangle region)
        {
            const int stride = 4;
            double sum = 0;
            double sumSquares = 0;
            int count = 0;
            for (int y = region.Top; y < region.Bottom; y += stride)
            {
                for (int x = region.Left; x < region.Right; x += stride)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    double luminance = (0.2126 * pixel.R) + (0.7152 * pixel.G) + (0.0722 * pixel.B);
                    sum += luminance;
                    sumSquares += luminance * luminance;
                    count++;
                }
            }

            if (count == 0) return 0;
            double mean = sum / count;
            return Math.Sqrt(Math.Max(0, (sumSquares / count) - (mean * mean)));
        }
    }
}
