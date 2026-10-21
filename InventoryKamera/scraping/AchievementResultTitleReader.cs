using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
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

    internal enum AchievementTitleOcrScope
    {
        Primary,
        WideFallback,
    }

    internal sealed class AchievementTitleOcrAttempt
    {
        internal AchievementTitleOcrAttempt(
            AchievementTitleOcrVariant variant,
            string rawText,
            float confidence,
            string reason,
            AchievementTitleOcrScope scope = AchievementTitleOcrScope.Primary)
        {
            Variant = variant;
            RawText = rawText ?? string.Empty;
            NormalizedText = AchievementTitleNormalizer.Normalize(RawText);
            ComparisonText = AchievementTitleNormalizer.NormalizeForComparison(RawText);
            Confidence = confidence;
            Reason = reason ?? string.Empty;
            Scope = scope;
        }

        internal AchievementTitleOcrVariant Variant { get; }
        internal string RawText { get; }
        internal string NormalizedText { get; }
        internal string ComparisonText { get; }
        internal float Confidence { get; }
        internal string Reason { get; }
        internal AchievementTitleOcrScope Scope { get; }
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

    internal interface IAchievementLazyResultTitleReader
    {
        AchievementResultTitleRead Read(
            Bitmap screenshot,
            Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt);
    }

    /// <summary>
    /// Reads only Phase 6A's normalized first-result title crop. Three bounded variants extend the
    /// grayscale/contrast strategy already proven by the Achievement screen detector and make no
    /// assumption that highlighted and non-highlighted title text share one color.
    /// </summary>
    internal sealed class AchievementResultTitleReader :
        IAchievementResultTitleReader,
        IAchievementLazyResultTitleReader
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

        public AchievementResultTitleRead Read(Bitmap screenshot) => Read(screenshot, null);

        public AchievementResultTitleRead Read(
            Bitmap screenshot,
            Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            AchievementScreenRegions regions = AchievementScreenRegions.For(screenshot.Size);
            var attempts = new List<AchievementTitleOcrAttempt>(6);
            ReadRegion(
                screenshot,
                regions.FirstResultTitle,
                AchievementTitleOcrScope.Primary,
                attempts,
                stopAfterAttempt);
            if (attempts.Count > 0 && stopAfterAttempt?.Invoke(attempts[^1]) == true)
                return new AchievementResultTitleRead(attempts);

            ReadRegion(
                screenshot,
                regions.FirstResultTitleWide,
                AchievementTitleOcrScope.WideFallback,
                attempts,
                stopAfterAttempt);
            return new AchievementResultTitleRead(attempts);
        }

        private void ReadRegion(
            Bitmap screenshot,
            Rectangle region,
            AchievementTitleOcrScope scope,
            List<AchievementTitleOcrAttempt> attempts,
            Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt)
        {
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 3),
                Math.Max(1, crop.Height * 3));
            // Threshold is the calibrated primary for mixed orange/dark title text. Contrast
            // variants are fallbacks and are only paid for when the caller still lacks a safe match.
            ReadThresholdVariant(resized, scope, attempts);
            if (ShouldStop(attempts, stopAfterAttempt)) return;
            ReadVariant(resized, inverted: false, scope, attempts);
            if (ShouldStop(attempts, stopAfterAttempt)) return;
            ReadVariant(resized, inverted: true, scope, attempts);
        }

        private static bool ShouldStop(
            IReadOnlyList<AchievementTitleOcrAttempt> attempts,
            Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt) =>
            stopAfterAttempt != null && attempts.Count > 0 && stopAfterAttempt(attempts[^1]);

        private void ReadThresholdVariant(
            Bitmap resized,
            AchievementTitleOcrScope scope,
            List<AchievementTitleOcrAttempt> attempts)
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
                        : "OCR returned mixed-color title text for identity scoring.",
                    scope));
            }
            finally
            {
                processed.Dispose();
            }
        }

        private void ReadVariant(
            Bitmap resized,
            bool inverted,
            AchievementTitleOcrScope scope,
            List<AchievementTitleOcrAttempt> attempts)
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
                        : "OCR returned title text for identity scoring.",
                    scope));
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

        /// <summary>
        /// Produces a comparison-only key for OCR matching. Canonical display text and catalog
        /// grouping continue to use <see cref="Normalize"/>. These transformations are limited to
        /// characters observed to be unstable in title OCR: accents, apostrophes, and dash glyphs.
        /// </summary>
        internal static string NormalizeForComparison(string text)
        {
            string normalized = Normalize(text);
            if (normalized.Length == 0) return string.Empty;

            string decomposed = normalized.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                switch (character)
                {
                    case '\'':
                        break;
                    case '-':
                    case '\u2010':
                    case '\u2011':
                    case '\u2012':
                    case '\u2013':
                    case '\u2014':
                    case '\u2015':
                    case '\u2212':
                        builder.Append(' ');
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return Whitespace.Replace(
                builder.ToString().Normalize(NormalizationForm.FormC),
                " ").Trim();
        }
    }
}
