using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;

namespace InventoryKamera
{
    internal sealed class InventoryScreenDetection
    {
        public bool IsInventoryOpen => TabIndex >= 0;
        public int TabIndex { get; }
        public string TabName { get; }
        public string RawText { get; }

        public InventoryScreenDetection(int tabIndex, string tabName, string rawText)
        {
            TabIndex = tabIndex;
            TabName = tabName;
            RawText = rawText ?? string.Empty;
        }
    }

    internal interface IInventoryScreenDetector
    {
        InventoryScreenDetection Detect(Bitmap screenshot);
    }

    /// <summary>Recognizes the active Inventory sub-tab as a postcondition for menu entry.</summary>
    internal sealed class InventoryScreenDetector : IInventoryScreenDetector
    {
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        public InventoryScreenDetector(IOcrService ocrService, IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public InventoryScreenDetection Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            // The active sub-tab label is anchored to the top-left independently of inventory-grid
            // geometry. Character Development Items needs the same 20%-wide title crop already used
            // by InventoryScraper's phase transitions; the former 7.5% crop truncated persisted long
            // tab names and incorrectly reported that Inventory had not opened.
            var region = new Rectangle(
                x: (int)(0.085 * screenshot.Width),
                y: (int)(0.035 * screenshot.Height),
                width: (int)(0.20 * screenshot.Width),
                height: (int)(0.050 * screenshot.Height));
            using (Bitmap crop = screenshot.Clone(region, PixelFormat.Format24bppRgb))
                return DetectTabRegion(crop);
        }

        internal InventoryScreenDetection DetectTabRegion(Bitmap region)
        {
            using Bitmap resized = GenshinProcesor.ResizeImage(region, region.Width * 3, region.Height * 3);
            var attempts = new List<string>();

            // The title can be light-on-dark or dark-on-light as the active tab styling changes.
            // Both variants use the existing OCR contrast value; this does not affect scanner OCR.
            foreach (bool invert in new[] { true, false })
            {
                Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
                try
                {
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    if (invert) imagePreprocessor.SetInvert(ref processed);

                    string rawText = ocrService.AnalyzeText(processed, PageSegMode.SingleLine).Trim();
                    attempts.Add(rawText);
                    int index = InventoryTabRecognition.Match(rawText);
                    if (index >= 0)
                        return new InventoryScreenDetection(index, InventoryTabRecognition.Names[index], rawText);
                }
                finally
                {
                    processed.Dispose();
                }
            }

            return new InventoryScreenDetection(-1, null, string.Join(" | ", attempts));
        }
    }

    internal static class InventoryTabRecognition
    {
        internal static readonly string[] Names =
        {
            "Weapons", "Artifacts", "Character Development Items", "Food", "Materials",
            "Gadget", "Quest", "Precious Items", "Furnishings",
        };

        internal static int Match(string rawText)
        {
            string[] normalizedTabs = Names.Select(t => Normalize(t)).ToArray();
            string normalizedText = Normalize(rawText);
            string matched = TextNormalizer.FindClosestInList(normalizedText, new HashSet<string>(normalizedTabs));
            return Array.IndexOf(normalizedTabs, matched);
        }

        private static string Normalize(string text) =>
            Regex.Replace((text ?? string.Empty).ToLowerInvariant(), @"[\W]", string.Empty);
    }
}
