using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;

namespace InventoryKamera
{
    internal sealed class AchievementCompletedTotalAttempt
    {
        internal AchievementCompletedTotalAttempt(string rawText, float confidence, int? value, string reason)
        {
            RawText = rawText ?? string.Empty;
            Confidence = confidence;
            Value = value;
            Reason = reason ?? string.Empty;
        }

        internal string RawText { get; }
        internal float Confidence { get; }
        internal int? Value { get; }
        internal string Reason { get; }
    }

    internal sealed class AchievementCompletedTotalResult
    {
        internal AchievementCompletedTotalResult(IEnumerable<AchievementCompletedTotalAttempt> attempts)
        {
            Attempts = new ReadOnlyCollection<AchievementCompletedTotalAttempt>(
                (attempts ?? Array.Empty<AchievementCompletedTotalAttempt>()).ToArray());
            AchievementCompletedTotalAttempt[] accepted = Attempts
                .Where(attempt => attempt.Value.HasValue &&
                    attempt.Confidence >= AchievementTitleMatcher.MinimumOcrConfidence)
                .ToArray();
            int[] values = accepted.Select(attempt => attempt.Value.Value).Distinct().ToArray();
            CompletedTotal = values.Length == 1 ? values[0] : null;
            Reason = values.Length == 1
                ? "One nonnegative completed total was positively recognized."
                : values.Length > 1
                    ? "Completed-total OCR variants disagreed."
                    : "No completed total was positively recognized.";
        }

        internal IReadOnlyList<AchievementCompletedTotalAttempt> Attempts { get; }
        internal int? CompletedTotal { get; }
        internal string Reason { get; }
    }

    internal interface IAchievementCompletedTotalReader
    {
        AchievementCompletedTotalResult Read(Bitmap screenshot);
    }

    internal static class AchievementCompletedTotalParser
    {
        private static readonly Regex Pattern = new(
            @"^\s*achievements?\s+completed\s*:\s*(?<value>\d{1,5})\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static int? Parse(string rawText)
        {
            Match match = Pattern.Match(rawText ?? string.Empty);
            return match.Success && int.TryParse(
                match.Groups["value"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int value)
                    ? value
                    : null;
        }
    }

    internal sealed class AchievementCompletedTotalReader : IAchievementCompletedTotalReader
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementCompletedTotalReader(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementCompletedTotalResult Read(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            Rectangle region = AchievementScreenRegions.For(screenshot.Size).CompletedCount;
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(crop, crop.Width * 3, crop.Height * 3);
            var attempts = new List<AchievementCompletedTotalAttempt>(3);
            ReadVariant(resized, threshold: true, inverted: false, attempts);
            if (!Accepted(attempts)) ReadVariant(resized, threshold: false, inverted: false, attempts);
            if (!Accepted(attempts)) ReadVariant(resized, threshold: false, inverted: true, attempts);
            return new AchievementCompletedTotalResult(attempts);
        }

        private static bool Accepted(IEnumerable<AchievementCompletedTotalAttempt> attempts) =>
            attempts.Any(attempt => attempt.Value.HasValue &&
                attempt.Confidence >= AchievementTitleMatcher.MinimumOcrConfidence);

        private void ReadVariant(
            Bitmap resized,
            bool threshold,
            bool inverted,
            ICollection<AchievementCompletedTotalAttempt> attempts)
        {
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                if (threshold) imagePreprocessor.SetThreshold(200, ref processed);
                else imagePreprocessor.SetContrast(60.0, ref processed);
                if (inverted) imagePreprocessor.SetInvert(ref processed);
                (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                    processed,
                    PageSegMode.SingleLine);
                string raw = (text ?? string.Empty).Trim();
                int? value = AchievementCompletedTotalParser.Parse(raw);
                attempts.Add(new AchievementCompletedTotalAttempt(
                    raw,
                    confidence,
                    value,
                    value.HasValue ? "Completed total parsed." : "Completed total was malformed."));
            }
            finally
            {
                processed.Dispose();
            }
        }
    }
}
