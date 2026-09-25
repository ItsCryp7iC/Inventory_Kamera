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

        public CharacterScreenDetection(bool isCharacterOpen, string rawText, float confidence)
        {
            IsCharacterOpen = isCharacterOpen;
            RawText = rawText ?? string.Empty;
            Confidence = confidence;
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
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        // Destination verification is deliberately isolated from the scanner's OCR thresholds.
        // The live 1920x1080 failure capture reads "Attributes" correctly at 45% confidence, so
        // accept that stable semantic signal while still rejecting genuinely low-confidence text.
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

            var region = new Rectangle(
                x: (int)(0.080 * screenshot.Width),
                y: (int)(0.115 * screenshot.Height),
                width: (int)(0.150 * screenshot.Width),
                height: (int)(0.070 * screenshot.Height));
            using Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb);
            return DetectLabelRegion(crop);
        }

        internal CharacterScreenDetection DetectLabelRegion(Bitmap region)
        {
            using Bitmap resized = GenshinProcesor.ResizeImage(region, region.Width * 3, region.Height * 3);
            var attempts = new List<string>();
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
                    attempts.Add(rawText);
                    bestConfidence = Math.Max(bestConfidence, confidence);

                    (bool semanticMatch, bool fuzzyMatch) = MatchAttributesLabel(rawText);
                    bool accepted = confidence >= MinimumConfidence && semanticMatch;
                    Logger.Info(
                        "Character destination OCR attempt {0}/{1}: inverted={2}, text=\"{3}\", confidence={4:P0}, semanticMatch={5}, fuzzyMatch={6}, accepted={7}",
                        attempt + 1,
                        inversionAttempts.Length,
                        invert,
                        rawText.Replace("\r", " ").Replace("\n", " "),
                        confidence,
                        semanticMatch,
                        fuzzyMatch,
                        accepted);

                    if (accepted)
                        return new CharacterScreenDetection(true, rawText, confidence);
                }
                finally
                {
                    processed.Dispose();
                }
            }

            return new CharacterScreenDetection(false, string.Join(" | ", attempts), bestConfidence);
        }

        private static (bool SemanticMatch, bool FuzzyMatch) MatchAttributesLabel(string rawText)
        {
            string normalized = Regex.Replace((rawText ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]", string.Empty);
            string match = TextNormalizer.FindClosestInList(
                normalized,
                new HashSet<string>(new[] { "attributes" }));
            bool fuzzyMatch = match == "attributes";
            return (normalized.Contains("attributes") || fuzzyMatch, fuzzyMatch);
        }
    }
}
