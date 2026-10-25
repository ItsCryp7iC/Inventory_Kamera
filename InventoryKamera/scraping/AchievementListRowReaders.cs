using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using Tesseract;

namespace InventoryKamera
{
    internal interface IAchievementListRowTitleReader
    {
        AchievementResultTitleRead Read(
            Bitmap screenshot,
            AchievementListRowGeometry row,
            Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt = null);
    }

    internal interface IAchievementListRowCompletionReader
    {
        AchievementCompletionRead Read(
            Bitmap screenshot,
            AchievementListRowGeometry row,
            Func<IReadOnlyList<AchievementCompletionOcrAttempt>, bool> stopAfterVariant = null);
    }

    internal interface IAchievementListRowRewardReader
    {
        AchievementRewardResult Read(Bitmap screenshot, AchievementListRowGeometry row);
    }

    internal sealed class AchievementListRowTitleReader : IAchievementListRowTitleReader
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementListRowTitleReader(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementResultTitleRead Read(
            Bitmap screenshot,
            AchievementListRowGeometry row,
            Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt = null)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            if (row == null) throw new ArgumentNullException(nameof(row));

            Stopwatch part = Stopwatch.StartNew();
            using Bitmap crop = screenshot.Clone(row.Title, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 3),
                Math.Max(1, crop.Height * 3));
            part.Stop();
            TimeSpan cropResize = part.Elapsed;
            TimeSpan preprocessing = TimeSpan.Zero;
            TimeSpan ocr = TimeSpan.Zero;
            var attempts = new List<AchievementTitleOcrAttempt>(3);
            ReadTitleVariant(resized, AchievementTitleOcrVariant.GrayscaleThreshold, attempts,
                ref preprocessing, ref ocr);
            if (stopAfterAttempt?.Invoke(attempts[^1]) == true)
                return Result();
            ReadTitleVariant(resized, AchievementTitleOcrVariant.GrayscaleContrast, attempts,
                ref preprocessing, ref ocr);
            if (stopAfterAttempt?.Invoke(attempts[^1]) == true)
                return Result();
            ReadTitleVariant(resized, AchievementTitleOcrVariant.GrayscaleContrastInverted, attempts,
                ref preprocessing, ref ocr);
            return Result();

            AchievementResultTitleRead Result() => new(attempts,
                new AchievementOcrReadTiming(cropResize, preprocessing, ocr));
        }

        private void ReadTitleVariant(
            Bitmap resized,
            AchievementTitleOcrVariant variant,
            ICollection<AchievementTitleOcrAttempt> attempts,
            ref TimeSpan preprocessingTime,
            ref TimeSpan ocrTime)
        {
            Stopwatch part = Stopwatch.StartNew();
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                if (variant == AchievementTitleOcrVariant.GrayscaleThreshold)
                    imagePreprocessor.SetThreshold(220, ref processed);
                else
                {
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    if (variant == AchievementTitleOcrVariant.GrayscaleContrastInverted)
                        imagePreprocessor.SetInvert(ref processed);
                }
                part.Stop();
                preprocessingTime += part.Elapsed;

                part.Restart();
                (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                    processed,
                    PageSegMode.SingleLine);
                part.Stop();
                ocrTime += part.Elapsed;
                string raw = (text ?? string.Empty).Trim();
                attempts.Add(new AchievementTitleOcrAttempt(
                    variant,
                    raw,
                    confidence,
                    string.IsNullOrWhiteSpace(raw)
                        ? "OCR returned no visible-row title text."
                        : "OCR returned visible-row title text for canonical scoring."));
            }
            finally
            {
                processed.Dispose();
            }
        }
    }

    internal sealed class AchievementListRowCompletionReader : IAchievementListRowCompletionReader
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementListRowCompletionReader(
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementCompletionRead Read(
            Bitmap screenshot,
            AchievementListRowGeometry row,
            Func<IReadOnlyList<AchievementCompletionOcrAttempt>, bool> stopAfterVariant = null)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            if (row == null) throw new ArgumentNullException(nameof(row));

            var attempts = new List<AchievementCompletionOcrAttempt>(6);
            TimeSpan cropResize = TimeSpan.Zero;
            TimeSpan preprocessing = TimeSpan.Zero;
            TimeSpan ocr = TimeSpan.Zero;
            AchievementCompletionOcrVariant[] variants =
            {
                AchievementCompletionOcrVariant.GrayscaleThreshold,
                AchievementCompletionOcrVariant.GrayscaleContrast,
                AchievementCompletionOcrVariant.GrayscaleContrastInverted,
            };
            foreach (AchievementCompletionOcrVariant variant in variants)
            {
                ReadCompletionVariant(
                    screenshot,
                    row.CompletionLabel,
                    AchievementCompletionOcrScope.StateLabel,
                    PageSegMode.SingleLine,
                    variant,
                    attempts,
                    ref cropResize, ref preprocessing, ref ocr);
                ReadCompletionVariant(
                    screenshot,
                    row.CompletionState,
                    AchievementCompletionOcrScope.FullCompletionRegion,
                    PageSegMode.SparseText,
                    variant,
                    attempts,
                    ref cropResize, ref preprocessing, ref ocr);
                if (stopAfterVariant?.Invoke(attempts) == true) break;
            }

            return new AchievementCompletionRead(attempts,
                new AchievementOcrReadTiming(cropResize, preprocessing, ocr));
        }

        private void ReadCompletionVariant(
            Bitmap screenshot,
            Rectangle region,
            AchievementCompletionOcrScope scope,
            PageSegMode pageMode,
            AchievementCompletionOcrVariant variant,
            ICollection<AchievementCompletionOcrAttempt> attempts,
            ref TimeSpan cropResizeTime,
            ref TimeSpan preprocessingTime,
            ref TimeSpan ocrTime)
        {
            Stopwatch part = Stopwatch.StartNew();
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 3),
                Math.Max(1, crop.Height * 3));
            part.Stop();
            cropResizeTime += part.Elapsed;
            part.Restart();
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                if (variant == AchievementCompletionOcrVariant.GrayscaleThreshold)
                    imagePreprocessor.SetThreshold(200, ref processed);
                else
                {
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    if (variant == AchievementCompletionOcrVariant.GrayscaleContrastInverted)
                        imagePreprocessor.SetInvert(ref processed);
                }
                part.Stop();
                preprocessingTime += part.Elapsed;

                part.Restart();
                (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(processed, pageMode);
                part.Stop();
                ocrTime += part.Elapsed;
                string raw = (text ?? string.Empty).Trim();
                attempts.Add(new AchievementCompletionOcrAttempt(
                    variant,
                    raw,
                    confidence,
                    "OCR returned bounded visible-row state text.",
                    scope));
            }
            finally
            {
                processed.Dispose();
            }
        }
    }

    internal sealed class AchievementListRowRewardReader : IAchievementListRowRewardReader
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementListRowRewardReader(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementRewardResult Read(Bitmap screenshot, AchievementListRowGeometry row)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            if (row == null) throw new ArgumentNullException(nameof(row));

            Stopwatch part = Stopwatch.StartNew();
            using Bitmap crop = screenshot.Clone(row.RewardValue, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 5),
                Math.Max(1, crop.Height * 5));
            part.Stop();
            TimeSpan cropResize = part.Elapsed;
            TimeSpan preprocessing = TimeSpan.Zero;
            TimeSpan ocr = TimeSpan.Zero;
            var attempts = new List<AchievementRewardOcrAttempt>(3);
            ReadRewardVariant(resized, AchievementRewardOcrVariant.GrayscaleThreshold, attempts,
                ref preprocessing, ref ocr);
            if (!HasOneStrongValue(attempts))
                ReadRewardVariant(resized, AchievementRewardOcrVariant.GrayscaleContrast, attempts,
                    ref preprocessing, ref ocr);
            if (!HasOneStrongValue(attempts))
                ReadRewardVariant(resized, AchievementRewardOcrVariant.GrayscaleContrastInverted, attempts,
                    ref preprocessing, ref ocr);
            return SelectReward(attempts,
                new AchievementOcrReadTiming(cropResize, preprocessing, ocr));
        }

        private void ReadRewardVariant(
            Bitmap resized,
            AchievementRewardOcrVariant variant,
            ICollection<AchievementRewardOcrAttempt> attempts,
            ref TimeSpan preprocessingTime,
            ref TimeSpan ocrTime)
        {
            Stopwatch part = Stopwatch.StartNew();
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                if (variant == AchievementRewardOcrVariant.GrayscaleThreshold)
                    imagePreprocessor.SetThreshold(200, ref processed);
                else
                {
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    if (variant == AchievementRewardOcrVariant.GrayscaleContrastInverted)
                        imagePreprocessor.SetInvert(ref processed);
                }
                part.Stop();
                preprocessingTime += part.Elapsed;
                part.Restart();
                (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                    processed,
                    PageSegMode.SingleLine,
                    numbersOnly: true);
                part.Stop();
                ocrTime += part.Elapsed;
                attempts.Add(new AchievementRewardOcrAttempt(variant, (text ?? string.Empty).Trim(), confidence));
            }
            finally
            {
                processed.Dispose();
            }
        }

        private static bool HasOneStrongValue(IEnumerable<AchievementRewardOcrAttempt> attempts) =>
            attempts.Count(attempt => attempt.Confidence >= AchievementRewardReader.MinimumEvidenceConfidence &&
                attempt.ParseResult.Accepted) == 1;

        private static AchievementRewardResult SelectReward(
            IReadOnlyList<AchievementRewardOcrAttempt> attempts,
            AchievementOcrReadTiming timing)
        {
            AchievementRewardOcrAttempt[] strong = attempts
                .Where(attempt => attempt.Confidence >= AchievementRewardReader.MinimumEvidenceConfidence &&
                    attempt.ParseResult.Accepted)
                .ToArray();
            int[] values = strong.Select(attempt => attempt.ParseResult.Value.Value).Distinct().ToArray();
            AchievementRewardOcrAttempt selected = values.Length == 1
                ? strong.OrderByDescending(attempt => attempt.Confidence).ThenBy(attempt => attempt.Variant).First()
                : null;
            int? value = values.Length == 1 ? values[0] : null;
            string reason = values.Length == 0
                ? "No reward OCR attempt produced a valid value at the confidence floor."
                : values.Length > 1
                    ? "Strong reward OCR attempts disagreed; the reward remains unresolved."
                    : "Strong bounded reward OCR evidence agreed on one value.";
            var diagnostics = new StringBuilder();
            diagnostics.Append("rewardValue=").Append(value?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            diagnostics.Append("; attempts=").Append(attempts.Count.ToString(CultureInfo.InvariantCulture));
            diagnostics.Append("; reason=").Append(reason);
            return new AchievementRewardResult(attempts, selected, value, reason,
                diagnostics.ToString(), timing);
        }
    }
}
