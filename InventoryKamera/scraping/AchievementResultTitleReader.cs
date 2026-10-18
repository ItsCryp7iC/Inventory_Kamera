using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;

namespace InventoryKamera
{
    internal enum AchievementTitleOcrVariant
    {
        GrayscaleContrast,
        GrayscaleContrastInverted,
        GrayscaleThreshold,
    }

    internal sealed class AchievementTitleOcrAttempt
    {
        internal AchievementTitleOcrAttempt(
            AchievementTitleOcrVariant variant,
            string rawText,
            float confidence,
            string reason)
        {
            Variant = variant;
            RawText = rawText ?? string.Empty;
            NormalizedText = AchievementTitleNormalizer.Normalize(RawText);
            Confidence = confidence;
            Reason = reason ?? string.Empty;
        }

        internal AchievementTitleOcrVariant Variant { get; }
        internal string RawText { get; }
        internal string NormalizedText { get; }
        internal float Confidence { get; }
        internal string Reason { get; }
    }

    internal sealed class AchievementResultTitleRead
    {
        internal AchievementResultTitleRead(IEnumerable<AchievementTitleOcrAttempt> attempts)
        {
            if (attempts == null) throw new ArgumentNullException(nameof(attempts));
            Attempts = new ReadOnlyCollection<AchievementTitleOcrAttempt>(
                new List<AchievementTitleOcrAttempt>(attempts));
        }

        internal IReadOnlyList<AchievementTitleOcrAttempt> Attempts { get; }
    }

    internal interface IAchievementResultTitleReader
    {
        AchievementResultTitleRead Read(Bitmap screenshot);
    }

    /// <summary>
    /// Reads only Phase 6A's normalized first-result title crop. Three bounded variants extend the
    /// grayscale/contrast strategy already proven by the Achievement screen detector and make no
    /// assumption that highlighted and non-highlighted title text share one color.
    /// </summary>
    internal sealed class AchievementResultTitleReader : IAchievementResultTitleReader
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementResultTitleReader(
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementResultTitleRead Read(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            Rectangle region = AchievementScreenRegions.For(screenshot.Size).FirstResultTitle;
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 3),
                Math.Max(1, crop.Height * 3));
            var attempts = new List<AchievementTitleOcrAttempt>(3);

            ReadVariant(resized, inverted: false, attempts);
            ReadVariant(resized, inverted: true, attempts);
            ReadThresholdVariant(resized, attempts);
            return new AchievementResultTitleRead(attempts);
        }

        private void ReadThresholdVariant(
            Bitmap resized,
            ICollection<AchievementTitleOcrAttempt> attempts)
        {
            // The Phase 6A region deliberately leaves vertical safety margin, which can include
            // the pale description line on compact multi-result rows. Restrict this high-threshold
            // variant to the measured top title band; do not interpret wrapped/long titles from a
            // partial read as identity; live validation established that long titles can be clipped.
            using Bitmap titleBand = resized.Clone(
                new Rectangle(0, 0, resized.Width, Math.Max(1, resized.Height * 2 / 3)),
                PixelFormat.Format24bppRgb);
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(titleBand);
            try
            {
                // The highlighted substring is orange while the rest of the title is dark gray.
                // A high grayscale threshold retains both against the pale result-card background;
                // the existing Pur fixture specifically proves why the contrast-only variants are
                // insufficient for mixed-color title text.
                imagePreprocessor.SetThreshold(220, ref processed);
                (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                    processed,
                    PageSegMode.SingleLine);
                string raw = (text ?? string.Empty).Trim();
                attempts.Add(new AchievementTitleOcrAttempt(
                    AchievementTitleOcrVariant.GrayscaleThreshold,
                    raw,
                    confidence,
                    string.IsNullOrWhiteSpace(raw)
                        ? "OCR returned no title text."
                        : "OCR returned mixed-color title text for identity scoring."));
            }
            finally
            {
                processed.Dispose();
            }
        }

        private void ReadVariant(
            Bitmap resized,
            bool inverted,
            ICollection<AchievementTitleOcrAttempt> attempts)
        {
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                imagePreprocessor.SetContrast(60.0, ref processed);
                if (inverted) imagePreprocessor.SetInvert(ref processed);
                (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                    processed,
                    PageSegMode.SingleLine);
                string raw = (text ?? string.Empty).Trim();
                attempts.Add(new AchievementTitleOcrAttempt(
                    inverted
                        ? AchievementTitleOcrVariant.GrayscaleContrastInverted
                        : AchievementTitleOcrVariant.GrayscaleContrast,
                    raw,
                    confidence,
                    string.IsNullOrWhiteSpace(raw)
                        ? "OCR returned no title text."
                        : "OCR returned title text for identity scoring."));
            }
            finally
            {
                processed.Dispose();
            }
        }
    }

    internal static class AchievementTitleNormalizer
    {
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.;:!?])", RegexOptions.Compiled);
        private static readonly Regex SpacedEllipsis = new(@"\.\s*\.\s*\.", RegexOptions.Compiled);
        private static readonly Regex ApostropheSpacing = new(@"\s*'\s*", RegexOptions.Compiled);
        private static readonly Regex SpaceAfterOpeningQuote = new("\"\\s+", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforeClosingQuote = new("\\s+\"", RegexOptions.Compiled);

        internal static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string normalized = text.Normalize(NormalizationForm.FormC)
                .Replace('\u00A0', ' ')
                .Replace('\u2018', '\'')
                .Replace('\u2019', '\'')
                .Replace('\u02BC', '\'')
                .Replace('\uFF07', '\'')
                .Replace('\u201C', '"')
                .Replace('\u201D', '"')
                .Replace('\u201E', '"')
                .Replace("\u2026", "...", StringComparison.Ordinal);

            normalized = SpacedEllipsis.Replace(normalized, "...");
            normalized = Whitespace.Replace(normalized, " ").Trim();
            normalized = SpaceBeforePunctuation.Replace(normalized, "$1");
            normalized = ApostropheSpacing.Replace(normalized, "'");
            normalized = SpaceAfterOpeningQuote.Replace(normalized, "\"");
            normalized = SpaceBeforeClosingQuote.Replace(normalized, "\"");
            return normalized.ToLowerInvariant();
        }
    }
}
