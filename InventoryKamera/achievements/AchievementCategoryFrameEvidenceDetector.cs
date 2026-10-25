using System;
using System.Drawing;
using System.Drawing.Imaging;
using Tesseract;

namespace InventoryKamera
{
    /// <summary>
    /// Phase 7B.1 evidence calibrated from the 1919x1079 Memories of the Heart scroll sequence.
    /// Category identity is OCR-backed; scrollbar extent proof is deliberately restricted to the
    /// only resolution for which real top and bottom pixels are currently available.
    /// </summary>
    internal sealed class AchievementCategoryFrameEvidenceDetector :
        IAchievementCategoryFrameEvidenceDetector
    {
        internal const string MemoriesOfTheHeartSignature = "memories of the heart";
        private const int ReferenceWidth = 1919;
        private const int ReferenceHeight = 1079;
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;

        internal AchievementCategoryFrameEvidenceDetector(
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor)
        {
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public AchievementCategoryFrameEvidence Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            bool categoryVerified = TryReadSelectedCategory(screenshot,
                out string categorySignature, out string categoryDiagnostics);
            bool top = false;
            bool bottom = false;
            string scrollbarDiagnostics;
            if (screenshot.Width == ReferenceWidth && screenshot.Height == ReferenceHeight &&
                TryFindScrollbarThumb(screenshot, out int thumbTop, out int thumbBottom))
            {
                top = thumbTop <= 149 && thumbBottom <= 265;
                bottom = thumbTop >= 848 && thumbBottom >= 966;
                scrollbarDiagnostics = $"scrollbarThumb={thumbTop}-{thumbBottom}; calibratedResolution=true";
            }
            else
            {
                scrollbarDiagnostics = screenshot.Width == ReferenceWidth && screenshot.Height == ReferenceHeight
                    ? "scrollbarThumb=unresolved; calibratedResolution=true"
                    : $"scrollbarThumb=unsupported-resolution; size={screenshot.Width}x{screenshot.Height}";
            }

            return new AchievementCategoryFrameEvidence(
                top,
                bottom,
                categorySignature,
                categoryVerified,
                $"{categoryDiagnostics}; {scrollbarDiagnostics}");
        }

        private bool TryReadSelectedCategory(Bitmap screenshot,
            out string signature, out string diagnostics)
        {
            signature = string.Empty;
            if (!TryFindSelectedCategoryBand(screenshot, out Rectangle band))
            {
                diagnostics = "selectedCategoryBand=unresolved";
                return false;
            }

            Rectangle title = new(
                ScaleX(112, screenshot.Width),
                band.Top + ScaleY(7, screenshot.Height),
                ScaleX(535, screenshot.Width),
                Math.Min(ScaleY(54, screenshot.Height), band.Height - ScaleY(7, screenshot.Height)));
            title.Intersect(new Rectangle(Point.Empty, screenshot.Size));
            if (title.Width <= 0 || title.Height <= 0)
            {
                diagnostics = $"selectedCategoryBand={band}; titleRegion=invalid";
                return false;
            }

            using Bitmap crop = screenshot.Clone(title, PixelFormat.Format24bppRgb);
            using Bitmap resized = GenshinProcesor.ResizeImage(crop,
                Math.Max(1, crop.Width * 2), Math.Max(1, crop.Height * 2));
            Bitmap processed = imagePreprocessor.ConvertToGrayscale(resized);
            try
            {
                imagePreprocessor.SetThreshold(205, ref processed);
                (string text, float confidence) = ocrService.AnalyzeTextWithConfidence(
                    processed, PageSegMode.SingleLine);
                signature = AchievementTitleNormalizer.Normalize(text);
                bool verified = signature.Length > 0 && confidence >= 0.60f;
                diagnostics = $"selectedCategoryBand={band.Top}-{band.Bottom}; categoryOcr='{signature}'; confidence={confidence:0.000}";
                if (!verified) signature = string.Empty;
                return verified;
            }
            finally
            {
                processed.Dispose();
            }
        }

        private static bool TryFindSelectedCategoryBand(Bitmap screenshot, out Rectangle band)
        {
            int left = ScaleX(52, screenshot.Width);
            int right = ScaleX(668, screenshot.Width);
            int top = ScaleY(170, screenshot.Height);
            int bottom = Math.Min(screenshot.Height - 1, ScaleY(1000, screenshot.Height));
            int xStep = Math.Max(1, ScaleX(16, screenshot.Width));
            int minimumHeight = ScaleY(70, screenshot.Height);
            int bestTop = -1;
            int bestBottom = -1;
            int runTop = -1;

            for (int y = top; y <= bottom; y++)
            {
                int bright = 0;
                int samples = 0;
                for (int x = left; x <= right; x += xStep)
                {
                    Color pixel = screenshot.GetPixel(x, y);
                    int luminance = Luminance(pixel);
                    if (luminance >= 145) bright++;
                    samples++;
                }
                bool selected = samples > 0 && bright * 100 >= samples * 60;
                if (selected && runTop < 0) runTop = y;
                if ((!selected || y == bottom) && runTop >= 0)
                {
                    int runBottom = selected && y == bottom ? y + 1 : y;
                    if (runBottom - runTop >= minimumHeight &&
                        runBottom - runTop > bestBottom - bestTop)
                    {
                        bestTop = runTop;
                        bestBottom = runBottom;
                    }
                    runTop = -1;
                }
            }

            band = bestTop >= 0
                ? Rectangle.FromLTRB(left, bestTop, right + 1, bestBottom)
                : Rectangle.Empty;
            return bestTop >= 0;
        }

        private static bool TryFindScrollbarThumb(Bitmap screenshot,
            out int thumbTop, out int thumbBottom)
        {
            const int x = 1838;
            const int scanTop = 145;
            const int scanBottom = 970;
            int bestTop = -1;
            int bestBottom = -1;
            int runTop = -1;
            for (int y = scanTop; y <= scanBottom; y++)
            {
                Color pixel = screenshot.GetPixel(x, y);
                bool bright = pixel.R >= 235 && pixel.G >= 232 && pixel.B >= 228;
                if (bright && runTop < 0) runTop = y;
                if ((!bright || y == scanBottom) && runTop >= 0)
                {
                    int runBottom = bright && y == scanBottom ? y : y - 1;
                    if (runBottom - runTop > bestBottom - bestTop)
                    {
                        bestTop = runTop;
                        bestBottom = runBottom;
                    }
                    runTop = -1;
                }
            }

            thumbTop = bestTop;
            thumbBottom = bestBottom;
            int height = bestBottom - bestTop + 1;
            return bestTop >= 0 && height >= 105 && height <= 125;
        }

        private static int ScaleX(int value, int width) =>
            Math.Max(1, (int)Math.Round(value * width / (double)ReferenceWidth));

        private static int ScaleY(int value, int height) =>
            Math.Max(1, (int)Math.Round(value * height / (double)ReferenceHeight));

        private static int Luminance(Color pixel) =>
            ((54 * pixel.R) + (183 * pixel.G) + (19 * pixel.B)) >> 8;
    }
}
