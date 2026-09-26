using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using Tesseract;

namespace InventoryKamera
{
    internal sealed class CharacterScreenDetection
    {
        public bool IsCharacterOpen { get; }
        public string RawText { get; }
        public float Confidence { get; }
        public IReadOnlyList<CharacterScreenOcrAttempt> OcrAttempts { get; }

        public CharacterScreenDetection(
            bool isCharacterOpen,
            string rawText,
            float confidence,
            IReadOnlyList<CharacterScreenOcrAttempt> ocrAttempts = null)
        {
            IsCharacterOpen = isCharacterOpen;
            RawText = rawText ?? string.Empty;
            Confidence = confidence;
            OcrAttempts = ocrAttempts == null
                ? Array.Empty<CharacterScreenOcrAttempt>()
                : new List<CharacterScreenOcrAttempt>(ocrAttempts).AsReadOnly();
        }
    }

    internal sealed class CharacterScreenOcrAttempt
    {
        public bool Inverted { get; }
        public string RawText { get; }
        public float Confidence { get; }
        public bool ExactSemanticMatch { get; }
        public bool FuzzySemanticMatch { get; }
        public bool Accepted { get; }

        public CharacterScreenOcrAttempt(
            bool inverted,
            string rawText,
            float confidence,
            bool exactSemanticMatch,
            bool fuzzySemanticMatch,
            bool accepted)
        {
            Inverted = inverted;
            RawText = rawText ?? string.Empty;
            Confidence = confidence;
            ExactSemanticMatch = exactSemanticMatch;
            FuzzySemanticMatch = fuzzySemanticMatch;
            Accepted = accepted;
        }
    }

    internal interface ICharacterScreenDetector
    {
        CharacterScreenDetection Detect(Bitmap screenshot);
    }

    /// <summary>
    /// Verifies the Character destination from the stable Attributes sub-tab label. The crop avoids
    /// character names and the element-colored background, both of which vary between accounts.
    /// </summary>
    internal sealed class CharacterScreenDetector : ICharacterScreenDetector
    {
        // Destination verification is deliberately isolated from the scanner's OCR thresholds.
        // This remains the floor for fuzzy/partial matches. Exact normalized "attributes" is accepted
        // independently because animated particles can lower aggregate confidence on otherwise exact text.
        internal const float MinimumConfidence = 0.40f;

        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        public CharacterScreenDetector(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public CharacterScreenDetection Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            using Bitmap crop = CopyDetectionRegion(screenshot);
            return DetectLabelRegion(crop);
        }

        internal static Bitmap CopyDetectionRegion(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            var region = new Rectangle(
                x: (int)(0.080 * screenshot.Width),
                y: (int)(0.115 * screenshot.Height),
                width: (int)(0.150 * screenshot.Width),
                height: (int)(0.070 * screenshot.Height));
            return screenshot.Clone(region, PixelFormat.Format24bppRgb);
        }

        internal CharacterScreenDetection DetectLabelRegion(Bitmap region)
        {
            using Bitmap resized = GenshinProcesor.ResizeImage(region, region.Width * 3, region.Height * 3);
            var attempts = new List<CharacterScreenOcrAttempt>();
            float bestConfidence = 0;

            bool[] inversionAttempts = { true, false };
            for (int attempt = 0; attempt < inversionAttempts.Length; attempt++)
            {
                bool invert = inversionAttempts[attempt];
                Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
                try
                {
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    if (invert) imagePreprocessor.SetInvert(ref processed);

                    (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                        processed, PageSegMode.SingleLine);
                    string rawText = text?.Trim() ?? string.Empty;
                    bestConfidence = Math.Max(bestConfidence, confidence);

                    (bool exactMatch, bool fuzzyMatch) = MatchAttributesLabel(rawText);
                    // An exact normalized label is stronger evidence than Tesseract's aggregate
                    // confidence on the animated Character background. Noisy/fuzzy text retains the
                    // existing 40% floor; this policy is local to destination verification.
                    bool accepted = exactMatch || (fuzzyMatch && confidence >= MinimumConfidence);
                    attempts.Add(new CharacterScreenOcrAttempt(
                        invert,
                        rawText,
                        confidence,
                        exactMatch,
                        fuzzyMatch,
                        accepted));

                    if (accepted)
                        return new CharacterScreenDetection(true, rawText, confidence, attempts.ToArray());
                }
                finally
                {
                    processed.Dispose();
                }
            }

            return new CharacterScreenDetection(
                false,
                string.Join(" | ", attempts.ConvertAll(attempt => attempt.RawText)),
                bestConfidence,
                attempts.ToArray());
        }

        private static (bool ExactMatch, bool FuzzyMatch) MatchAttributesLabel(string rawText)
        {
            string normalized = Regex.Replace((rawText ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]", string.Empty);
            bool exactMatch = normalized == "attributes";
            string match = TextNormalizer.FindClosestInList(
                normalized,
                new HashSet<string>(new[] { "attributes" }));
            bool fuzzyMatch = !exactMatch &&
                (normalized.Contains("attributes") || match == "attributes");
            return (exactMatch, fuzzyMatch);
        }
    }
}
