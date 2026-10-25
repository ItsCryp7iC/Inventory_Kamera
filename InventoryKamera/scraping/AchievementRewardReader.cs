using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Tesseract;

namespace InventoryKamera
{
    internal enum AchievementRewardOcrVariant
    {
        GrayscaleContrast,
        GrayscaleContrastInverted,
        GrayscaleThreshold,
    }

    internal sealed class AchievementRewardParseResult
    {
        internal AchievementRewardParseResult(
            string rawText,
            string normalizedText,
            bool accepted,
            int? value,
            string reason)
        {
            RawText = rawText ?? string.Empty;
            NormalizedText = normalizedText ?? string.Empty;
            Accepted = accepted;
            Value = value;
            Reason = reason ?? string.Empty;
        }

        internal string RawText { get; }
        internal string NormalizedText { get; }
        internal bool Accepted { get; }
        internal int? Value { get; }
        internal string Reason { get; }
    }

    internal static class AchievementRewardParser
    {
        internal const int MaximumReward = 999;
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex IntegerRuns = new(@"\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ExactPositiveInteger = new(@"^\d{1,3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static AchievementRewardParseResult Parse(string rawText)
        {
            string raw = rawText ?? string.Empty;
            string normalized = Whitespace.Replace(
                    raw.Normalize(NormalizationForm.FormC).Replace('\u00A0', ' '),
                    " ")
                .Trim();

            if (normalized.Length == 0)
                return Reject(raw, normalized, "OCR returned no reward value.");

            int integerCount = IntegerRuns.Matches(normalized).Count;
            if (integerCount > 1)
                return Reject(raw, normalized, "Multiple integer candidates were found in the reward crop.");
            if (!ExactPositiveInteger.IsMatch(normalized))
                return Reject(raw, normalized, "The reward crop was not exactly one unsigned bounded integer.");
            if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value <= 0 || value > MaximumReward)
                return Reject(raw, normalized, "The reward value was outside the supported positive bounds.");

            return new AchievementRewardParseResult(
                raw,
                normalized,
                accepted: true,
                value,
                "Exactly one bounded positive reward integer was recognized.");
        }

        private static AchievementRewardParseResult Reject(
            string raw,
            string normalized,
            string reason) => new(raw, normalized, false, null, reason);
    }

    internal sealed class AchievementRewardOcrAttempt
    {
        internal AchievementRewardOcrAttempt(
            AchievementRewardOcrVariant variant,
            string rawText,
            float confidence)
        {
            Variant = variant;
            RawText = rawText ?? string.Empty;
            Confidence = confidence;
            ParseResult = AchievementRewardParser.Parse(RawText);
        }

        internal AchievementRewardOcrVariant Variant { get; }
        internal string RawText { get; }
        internal string NormalizedText => ParseResult.NormalizedText;
        internal float Confidence { get; }
        internal AchievementRewardParseResult ParseResult { get; }
    }

    internal sealed class AchievementRewardResult
    {
        internal AchievementRewardResult(
            IEnumerable<AchievementRewardOcrAttempt> attempts,
            AchievementRewardOcrAttempt selectedAttempt,
            int? rewardValue,
            string reason,
            string diagnostics,
            AchievementOcrReadTiming timing = null)
        {
            Attempts = new ReadOnlyCollection<AchievementRewardOcrAttempt>(
                (attempts ?? Array.Empty<AchievementRewardOcrAttempt>()).ToArray());
            SelectedAttempt = selectedAttempt;
            RewardValue = rewardValue;
            Reason = reason ?? string.Empty;
            Diagnostics = diagnostics ?? string.Empty;
            Timing = timing ?? AchievementOcrReadTiming.Empty;
        }

        internal IReadOnlyList<AchievementRewardOcrAttempt> Attempts { get; }
        internal AchievementRewardOcrAttempt SelectedAttempt { get; }
        internal int? RewardValue { get; }
        internal bool Succeeded => RewardValue.HasValue;
        internal string Reason { get; }
        internal string Diagnostics { get; }
        internal AchievementOcrReadTiming Timing { get; }

        internal static AchievementRewardResult Unresolved(string reason) =>
            CreateUnresolved(reason);

        private static AchievementRewardResult CreateUnresolved(string reason)
        {
            string bounded = Regex.Replace(reason ?? string.Empty, @"\s+", " ").Trim();
            if (bounded.Length > 240) bounded = bounded.Substring(0, 240) + "...";
            return new AchievementRewardResult(
                Array.Empty<AchievementRewardOcrAttempt>(),
                null,
                null,
                bounded,
                $"rewardValue=(none); attempts=0; reason={bounded}");
        }
    }

    internal interface IAchievementRewardReader
    {
        AchievementRewardResult Read(Bitmap screenshot);
    }

    /// <summary>
    /// Reads only the narrow first-row Primogem number region. Three preprocessing variants are
    /// bounded, and a value is returned only when all strong parsed evidence agrees.
    /// </summary>
    internal sealed class AchievementRewardReader : IAchievementRewardReader
    {
        internal const float MinimumEvidenceConfidence = 0.35f;

        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementRewardReader(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementRewardResult Read(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            Rectangle region = AchievementScreenRegions.For(screenshot.Size).FirstResultRewardValue;
            var attempts = new List<AchievementRewardOcrAttempt>(3);
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 5),
                Math.Max(1, crop.Height * 5));
            ReadContrast(resized, inverted: false, attempts);
            ReadContrast(resized, inverted: true, attempts);
            ReadThreshold(resized, attempts);
            return Select(attempts);
        }

        private void ReadContrast(
            Bitmap resized,
            bool inverted,
            ICollection<AchievementRewardOcrAttempt> attempts)
        {
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                imagePreprocessor.SetContrast(60.0, ref processed);
                if (inverted) imagePreprocessor.SetInvert(ref processed);
                AddAttempt(
                    processed,
                    inverted
                        ? AchievementRewardOcrVariant.GrayscaleContrastInverted
                        : AchievementRewardOcrVariant.GrayscaleContrast,
                    attempts);
            }
            finally
            {
                processed.Dispose();
            }
        }

        private void ReadThreshold(
            Bitmap resized,
            ICollection<AchievementRewardOcrAttempt> attempts)
        {
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                imagePreprocessor.SetThreshold(200, ref processed);
                AddAttempt(processed, AchievementRewardOcrVariant.GrayscaleThreshold, attempts);
            }
            finally
            {
                processed.Dispose();
            }
        }

        private void AddAttempt(
            Bitmap processed,
            AchievementRewardOcrVariant variant,
            ICollection<AchievementRewardOcrAttempt> attempts)
        {
            (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                processed,
                PageSegMode.SingleLine,
                numbersOnly: true);
            attempts.Add(new AchievementRewardOcrAttempt(variant, (text ?? string.Empty).Trim(), confidence));
        }

        private static AchievementRewardResult Select(
            IReadOnlyList<AchievementRewardOcrAttempt> attempts)
        {
            AchievementRewardOcrAttempt[] strong = attempts
                .Where(attempt => attempt.Confidence >= MinimumEvidenceConfidence && attempt.ParseResult.Accepted)
                .ToArray();
            int[] values = strong.Select(attempt => attempt.ParseResult.Value.Value).Distinct().ToArray();

            AchievementRewardOcrAttempt selected = null;
            int? value = null;
            string reason;
            if (values.Length == 0)
            {
                reason = "No reward OCR attempt produced a valid value at the confidence floor.";
            }
            else if (values.Length > 1)
            {
                reason = "Strong reward OCR attempts disagreed; the reward remains unresolved.";
            }
            else
            {
                value = values[0];
                selected = strong
                    .OrderByDescending(attempt => attempt.Confidence)
                    .ThenBy(attempt => attempt.Variant)
                    .First();
                reason = "Strong bounded reward OCR evidence agreed on one value.";
            }

            var diagnostics = new StringBuilder(640);
            diagnostics.Append("rewardValue=").Append(value?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            diagnostics.Append("; attempts=").Append(attempts.Count.ToString(CultureInfo.InvariantCulture));
            diagnostics.Append("; selectedVariant=").Append(selected?.Variant.ToString() ?? "(none)");
            diagnostics.Append("; rewardRaw=").Append(Bound(selected?.RawText));
            diagnostics.Append("; rewardNormalized=").Append(Bound(selected?.NormalizedText));
            diagnostics.Append("; rewardConfidence=").Append(
                selected?.Confidence.ToString("0.000", CultureInfo.InvariantCulture) ?? "(none)");
            diagnostics.Append("; reason=").Append(reason);
            for (int index = 0; index < attempts.Count; index++)
            {
                AchievementRewardOcrAttempt attempt = attempts[index];
                diagnostics.Append("; attempt").Append(index + 1).Append('=')
                    .Append(attempt.Variant).Append('|')
                    .Append(Bound(attempt.RawText)).Append('|')
                    .Append(Bound(attempt.NormalizedText)).Append('|')
                    .Append(attempt.Confidence.ToString("0.000", CultureInfo.InvariantCulture)).Append('|')
                    .Append(attempt.ParseResult.Value?.ToString(CultureInfo.InvariantCulture) ?? "(none)");
            }

            return new AchievementRewardResult(attempts, selected, value, reason, diagnostics.ToString());
        }

        private static string Bound(string text)
        {
            string clean = Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
            return clean.Length <= 48 ? clean : clean.Substring(0, 48) + "...";
        }
    }
}
