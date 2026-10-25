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
    internal enum AchievementCompletionOcrVariant
    {
        GrayscaleContrast,
        GrayscaleContrastInverted,
        GrayscaleThreshold,
    }

    internal enum AchievementCompletionOcrScope
    {
        StateLabel,
        FullCompletionRegion,
    }

    internal sealed class AchievementProgressParseResult
    {
        internal AchievementProgressParseResult(
            string rawText,
            bool accepted,
            int? current,
            int? target,
            bool multipleFractions,
            string reason)
        {
            RawText = rawText ?? string.Empty;
            Accepted = accepted;
            Current = current;
            Target = target;
            MultipleFractions = multipleFractions;
            Reason = reason ?? string.Empty;
        }

        internal string RawText { get; }
        internal bool Accepted { get; }
        internal int? Current { get; }
        internal int? Target { get; }
        internal bool MultipleFractions { get; }
        internal string Reason { get; }
    }

    internal static class AchievementProgressParser
    {
        internal const int MaximumComponent = 9999;

        private static readonly Regex FractionPattern = new(
            @"(?<![\d/-])(?<current>\d{1,4})\s*/\s*(?<target>\d{1,4})(?![\d/])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static AchievementProgressParseResult Parse(string rawText)
        {
            string raw = rawText ?? string.Empty;
            MatchCollection matches = FractionPattern.Matches(raw);
            if (matches.Count == 0)
                return Reject(raw, "Exactly one bounded progress fraction was not found.");
            if (matches.Count != 1)
                return new AchievementProgressParseResult(
                    raw,
                    accepted: false,
                    current: null,
                    target: null,
                    multipleFractions: true,
                    "Multiple progress fractions were found.");

            Match match = matches[0];
            if (!int.TryParse(
                    match.Groups["current"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int current) ||
                !int.TryParse(
                    match.Groups["target"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int target))
            {
                return Reject(raw, "The progress components were not valid invariant integers.");
            }

            if (current < 0 || current > MaximumComponent ||
                target <= 0 || target > MaximumComponent)
                return Reject(raw, "The progress fraction was outside the supported bounds.");
            if (current > target)
                return Reject(raw, "Progress current cannot exceed its target.");

            return new AchievementProgressParseResult(
                match.Value,
                accepted: true,
                current,
                target,
                multipleFractions: false,
                current < target
                    ? "A valid incomplete progress fraction was found."
                    : "A valid equal progress fraction was found; it does not prove completion.");
        }

        private static AchievementProgressParseResult Reject(string rawText, string reason) =>
            new(rawText, false, null, null, false, reason);
    }

    internal sealed class AchievementCompletionOcrAttempt
    {
        internal AchievementCompletionOcrAttempt(
            AchievementCompletionOcrVariant variant,
            string rawText,
            float confidence,
            string reason,
            AchievementCompletionOcrScope scope = AchievementCompletionOcrScope.StateLabel)
        {
            Variant = variant;
            Scope = scope;
            RawText = rawText ?? string.Empty;
            NormalizedText = AchievementCompletionText.Normalize(RawText);
            Confidence = confidence;
            ClaimTokenDetected = AchievementCompletionText.HasToken(NormalizedText, "claim");
            CompletedTokenDetected = AchievementCompletionText.HasToken(NormalizedText, "completed");
            Progress = AchievementProgressParser.Parse(RawText);
            DateRaw = AchievementCompletionText.FindDate(RawText);
            Reason = BuildReason(reason);
        }

        internal AchievementCompletionOcrVariant Variant { get; }
        internal AchievementCompletionOcrScope Scope { get; }
        internal string RawText { get; }
        internal string NormalizedText { get; }
        internal float Confidence { get; }
        internal bool ClaimTokenDetected { get; }
        internal bool CompletedTokenDetected { get; }
        internal AchievementProgressParseResult Progress { get; }
        internal string DateRaw { get; }
        internal string Reason { get; }

        private string BuildReason(string fallback)
        {
            if (string.IsNullOrWhiteSpace(RawText))
                return "OCR returned no completion-state text.";
            if (ClaimTokenDetected && CompletedTokenDetected)
                return "Conflicting Claim and Completed token candidates were recognized.";
            if (ClaimTokenDetected)
                return "A whole-word Claim token candidate was recognized; confidence and conflicts remain to be checked.";
            if (CompletedTokenDetected)
                return "A whole-word Completed token candidate was recognized; confidence and conflicts remain to be checked.";
            if (Progress.Accepted)
                return Progress.Reason;
            if (Progress.MultipleFractions)
                return Progress.Reason;
            if (!string.IsNullOrEmpty(DateRaw))
                return "A valid game-format date was recognized as supporting evidence only.";

            return string.IsNullOrWhiteSpace(fallback)
                ? "No supported row-level state evidence was recognized."
                : $"{fallback} No supported row-level state evidence was recognized.";
        }
    }

    internal sealed class AchievementCompletionRead
    {
        internal AchievementCompletionRead(IEnumerable<AchievementCompletionOcrAttempt> attempts,
            AchievementOcrReadTiming timing = null)
        {
            if (attempts == null) throw new ArgumentNullException(nameof(attempts));
            Attempts = new ReadOnlyCollection<AchievementCompletionOcrAttempt>(attempts.ToArray());
            Timing = timing ?? AchievementOcrReadTiming.Empty;
        }

        internal IReadOnlyList<AchievementCompletionOcrAttempt> Attempts { get; }
        internal AchievementOcrReadTiming Timing { get; }
    }

    internal interface IAchievementCompletionReader
    {
        AchievementCompletionRead Read(Bitmap screenshot);
    }

    internal interface IAchievementLazyCompletionReader
    {
        AchievementCompletionRead Read(
            Bitmap screenshot,
            Func<IReadOnlyList<AchievementCompletionOcrAttempt>, bool> stopAfterVariant);
    }

    /// <summary>
    /// Reads only Phase 6A's normalized first-result completion-state crop. The variants are
    /// deliberately bounded and semantic tokens remain the primary evidence; color is not used to
    /// decide completion.
    /// </summary>
    internal sealed class AchievementCompletionReader :
        IAchievementCompletionReader,
        IAchievementLazyCompletionReader
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementCompletionReader(
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementCompletionRead Read(Bitmap screenshot) => Read(screenshot, null);

        public AchievementCompletionRead Read(
            Bitmap screenshot,
            Func<IReadOnlyList<AchievementCompletionOcrAttempt>, bool> stopAfterVariant)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            AchievementScreenRegions regions = AchievementScreenRegions.For(screenshot.Size);
            var attempts = new List<AchievementCompletionOcrAttempt>(6);
            AchievementCompletionOcrVariant[] variants =
            {
                AchievementCompletionOcrVariant.GrayscaleThreshold,
                AchievementCompletionOcrVariant.GrayscaleContrast,
                AchievementCompletionOcrVariant.GrayscaleContrastInverted,
            };
            foreach (AchievementCompletionOcrVariant variant in variants)
            {
                ReadVariantForRegion(
                    screenshot,
                    regions.FirstResultCompletionLabel,
                    AchievementCompletionOcrScope.StateLabel,
                    PageSegMode.SingleLine,
                    variant,
                    attempts);
                ReadVariantForRegion(
                    screenshot,
                    regions.FirstResultCompletionState,
                    AchievementCompletionOcrScope.FullCompletionRegion,
                    PageSegMode.SparseText,
                    variant,
                    attempts);
                if (stopAfterVariant != null && stopAfterVariant(attempts)) break;
            }
            return new AchievementCompletionRead(attempts);
        }

        private void ReadVariantForRegion(
            Bitmap screenshot,
            Rectangle region,
            AchievementCompletionOcrScope scope,
            PageSegMode pageMode,
            AchievementCompletionOcrVariant variant,
            ICollection<AchievementCompletionOcrAttempt> attempts)
        {
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 3),
                Math.Max(1, crop.Height * 3));
            if (variant == AchievementCompletionOcrVariant.GrayscaleThreshold)
                ReadThresholdVariant(resized, scope, pageMode, attempts);
            else
                ReadContrastVariant(
                    resized,
                    inverted: variant == AchievementCompletionOcrVariant.GrayscaleContrastInverted,
                    scope,
                    pageMode,
                    attempts);
        }

        private void ReadRegion(
            Bitmap screenshot,
            Rectangle region,
            AchievementCompletionOcrScope scope,
            PageSegMode pageMode,
            ICollection<AchievementCompletionOcrAttempt> attempts)
        {
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(
                crop,
                Math.Max(1, crop.Width * 3),
                Math.Max(1, crop.Height * 3));
            ReadContrastVariant(resized, inverted: false, scope, pageMode, attempts);
            ReadContrastVariant(resized, inverted: true, scope, pageMode, attempts);
            ReadThresholdVariant(resized, scope, pageMode, attempts);
        }

        private void ReadContrastVariant(
            Bitmap resized,
            bool inverted,
            AchievementCompletionOcrScope scope,
            PageSegMode pageMode,
            ICollection<AchievementCompletionOcrAttempt> attempts)
        {
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                imagePreprocessor.SetContrast(60.0, ref processed);
                if (inverted) imagePreprocessor.SetInvert(ref processed);
                AddAttempt(
                    processed,
                    inverted
                        ? AchievementCompletionOcrVariant.GrayscaleContrastInverted
                        : AchievementCompletionOcrVariant.GrayscaleContrast,
                    scope,
                    pageMode,
                    attempts);
            }
            finally
            {
                processed.Dispose();
            }
        }

        private void ReadThresholdVariant(
            Bitmap resized,
            AchievementCompletionOcrScope scope,
            PageSegMode pageMode,
            ICollection<AchievementCompletionOcrAttempt> attempts)
        {
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                imagePreprocessor.SetThreshold(200, ref processed);
                AddAttempt(
                    processed,
                    AchievementCompletionOcrVariant.GrayscaleThreshold,
                    scope,
                    pageMode,
                    attempts);
            }
            finally
            {
                processed.Dispose();
            }
        }

        private void AddAttempt(
            Bitmap processed,
            AchievementCompletionOcrVariant variant,
            AchievementCompletionOcrScope scope,
            PageSegMode pageMode,
            ICollection<AchievementCompletionOcrAttempt> attempts)
        {
            (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                processed,
                pageMode);
            string raw = (text ?? string.Empty).Trim();
            attempts.Add(new AchievementCompletionOcrAttempt(
                variant,
                raw,
                confidence,
                string.IsNullOrWhiteSpace(raw)
                    ? "OCR returned no completion-state text."
                    : "OCR returned bounded row-state text for semantic classification.",
                scope));
        }
    }

    internal static class AchievementCompletionText
    {
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex DatePattern = new(
            @"(?<!\d)(?<year>20\d{2})\s*/\s*(?<month>0?[1-9]|1[0-2])\s*/\s*(?<day>0?[1-9]|[12]\d|3[01])(?![\d/])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return Whitespace.Replace(
                    text.Normalize(NormalizationForm.FormC).Replace('\u00A0', ' '),
                    " ")
                .Trim()
                .ToLowerInvariant();
        }

        internal static bool HasToken(string normalizedText, string token) =>
            Regex.IsMatch(
                normalizedText ?? string.Empty,
                $@"(?<![a-z]){Regex.Escape(token)}(?![a-z])",
                RegexOptions.CultureInvariant);

        internal static string FindDate(string rawText)
        {
            MatchCollection matches = DatePattern.Matches(rawText ?? string.Empty);
            if (matches.Count != 1) return string.Empty;

            string normalized = Regex.Replace(matches[0].Value, @"\s+", string.Empty);
            string[] formats = { "yyyy/M/d", "yyyy/MM/dd", "yyyy/M/dd", "yyyy/MM/d" };
            return DateTime.TryParseExact(
                normalized,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _)
                    ? normalized
                    : string.Empty;
        }
    }
}
