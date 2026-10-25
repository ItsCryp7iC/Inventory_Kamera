using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using Tesseract;

namespace InventoryKamera
{
    /// <summary>
    /// Phase 7B.1 evidence calibrated from the 1919x1079 Memories of the Heart scroll sequence.
    /// Category identity is OCR-backed; scrollbar extent proof is restricted to the calibrated
    /// fixture size and the exact 1920x1080 Navigation capture size confirmed by live diagnostics.
    /// </summary>
    internal sealed class AchievementCategoryFrameEvidenceDetector :
        IAchievementCategoryFrameEvidenceDetector
    {
        internal const string MemoriesOfTheHeartSignature = "memories of the heart";
        private const int ReferenceWidth = 1919;
        private const int ReferenceHeight = 1079;
        private const int ReferenceScrollbarX = 1838;
        private const int ReferenceSearchTop = 145;
        private const int ReferenceSearchBottom = 970;
        private const int ReferenceTrackTop = 148;
        private const int ReferenceTrackBottom = 967;
        private const int ReferenceTopMaximum = 150;
        private const int ReferenceTopBottomMaximum = 265;
        private const int ReferenceBottomThumbTop = 853;
        private const int ReferenceBottomThumbBottom = 967;
        private const int BottomEndpointTolerance = 2;
        private const int ReferenceThumbHeight = 115;
        private const int ReferenceMinimumThumbHeight = 105;
        private const int ReferenceMaximumThumbHeight = 125;
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
            AchievementScrollbarEvidence scrollbar = AnalyzeScrollbar(screenshot);

            return new AchievementCategoryFrameEvidence(
                scrollbar.TopAtExtent,
                scrollbar.BottomAtExtent,
                categorySignature,
                categoryVerified,
                $"{categoryDiagnostics}; {scrollbar.Diagnostics}");
        }

        internal static AchievementScrollbarEvidence AnalyzeScrollbar(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            bool reference = screenshot.Width == ReferenceWidth && screenshot.Height == ReferenceHeight;
            bool liveCapture = screenshot.Width == 1920 && screenshot.Height == 1080;
            int x = ScaleX(ReferenceScrollbarX, screenshot.Width);
            int searchTop = ScaleY(ReferenceSearchTop, screenshot.Height);
            int searchBottom = ScaleY(ReferenceSearchBottom, screenshot.Height);
            int trackTop = ScaleY(ReferenceTrackTop, screenshot.Height);
            int trackBottom = ScaleY(ReferenceTrackBottom, screenshot.Height);
            string calibration = reference ? "reference-1919x1079" :
                liveCapture ? "mapped-live-1920x1080" : "unsupported";

            if (!reference && !liveCapture)
            {
                return AchievementScrollbarEvidence.CreateUnresolved(
                    x, searchTop, searchBottom, trackTop, trackBottom,
                    calibration,
                    screenshot.Size,
                    "unsupported-capture-size",
                    candidateCount: 0);
            }

            int minimumHeight = ScaleLengthY(ReferenceMinimumThumbHeight, screenshot.Height);
            int maximumHeight = ScaleLengthY(ReferenceMaximumThumbHeight, screenshot.Height);
            List<(int Top, int Bottom)> runs = null;
            List<(int Top, int Bottom)> candidates = null;
            int selectedX = x;
            int[] xOffsets = { 0, -1, 1, -2, 2 };
            foreach (int xOffset in xOffsets)
            {
                int candidateX = Math.Clamp(x + xOffset, 0, screenshot.Width - 1);
                List<(int Top, int Bottom)> candidateRuns = FindBrightRuns(
                    screenshot, candidateX, searchTop, searchBottom);
                var candidateThumbs = new List<(int Top, int Bottom)>();
                foreach ((int runStart, int runEnd) in candidateRuns)
                {
                    int height = runEnd - runStart + 1;
                    if (height >= minimumHeight && height <= maximumHeight)
                        candidateThumbs.Add((runStart, runEnd));
                }

                if (runs == null)
                {
                    runs = candidateRuns;
                    candidates = candidateThumbs;
                }
                if (candidateThumbs.Count > 0)
                {
                    selectedX = candidateX;
                    runs = candidateRuns;
                    candidates = candidateThumbs;
                    break;
                }
            }

            if (candidates.Count != 1)
            {
                string reason = candidates.Count > 1
                    ? "multiple-valid-thumb-candidates"
                    : runs.Count == 0
                        ? "no-bright-scrollbar-run"
                        : "bright-runs-outside-thumb-height-range";
                return AchievementScrollbarEvidence.CreateUnresolved(
                    selectedX, searchTop, searchBottom, trackTop, trackBottom,
                    calibration,
                    screenshot.Size,
                    reason,
                    candidates.Count,
                    DescribeRuns(runs));
            }

            (int thumbTop, int thumbBottom) = candidates[0];
            int heightPixels = thumbBottom - thumbTop + 1;
            int expectedHeight = ScaleLengthY(ReferenceThumbHeight, screenshot.Height);
            double heightConfidence = Math.Max(0.0,
                1.0 - Math.Abs(heightPixels - expectedHeight) /
                (double)Math.Max(1, maximumHeight - minimumHeight));
            double crossSectionSupport = MeasureCrossSectionSupport(
                screenshot, selectedX, thumbTop, thumbBottom);
            double confidence = 0.65 * heightConfidence + 0.35 * crossSectionSupport;
            if (confidence < 0.75)
            {
                return AchievementScrollbarEvidence.CreateUnresolved(
                    selectedX, searchTop, searchBottom, trackTop, trackBottom,
                    calibration,
                    screenshot.Size,
                    "thumb-candidate-low-confidence",
                    candidates.Count,
                    DescribeRuns(runs),
                    thumbTop,
                    thumbBottom,
                    confidence);
            }

            int maximumThumbTop = Math.Max(trackTop + 1, trackBottom - heightPixels + 1);
            double normalizedPosition = Math.Clamp(
                (thumbTop - trackTop) / (double)(maximumThumbTop - trackTop), 0.0, 1.0);
            bool top = thumbTop <= ScaleY(ReferenceTopMaximum, screenshot.Height) &&
                thumbBottom <= ScaleY(ReferenceTopBottomMaximum, screenshot.Height);
            int expectedBottomTop = ScaleY(ReferenceBottomThumbTop, screenshot.Height);
            int expectedBottomBottom = ScaleY(ReferenceBottomThumbBottom, screenshot.Height);
            int bottomTolerance = ScaleLengthY(BottomEndpointTolerance, screenshot.Height);
            int bottomTopDelta = thumbTop - expectedBottomTop;
            int bottomBottomDelta = thumbBottom - expectedBottomBottom;
            bool bottom = Math.Abs(bottomTopDelta) <= bottomTolerance &&
                Math.Abs(bottomBottomDelta) <= bottomTolerance;
            string topRejection = top ? "none" :
                $"thumb-not-at-top(top>{ScaleY(ReferenceTopMaximum, screenshot.Height)}" +
                $" or bottom>{ScaleY(ReferenceTopBottomMaximum, screenshot.Height)})";
            string bottomRejection = bottom ? "none" :
                $"thumb-not-at-calibrated-bottom(topDelta={bottomTopDelta}," +
                $" bottomDelta={bottomBottomDelta}, tolerance={bottomTolerance})";
            return AchievementScrollbarEvidence.CreateResolved(
                selectedX, searchTop, searchBottom, trackTop, trackBottom,
                calibration, screenshot.Size, thumbTop, thumbBottom,
                normalizedPosition, confidence, top, bottom,
                topRejection, bottomRejection, candidates.Count,
                expectedBottomTop, expectedBottomBottom, bottomTolerance);
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

        private static List<(int Top, int Bottom)> FindBrightRuns(Bitmap screenshot,
            int x, int scanTop, int scanBottom)
        {
            var runs = new List<(int Top, int Bottom)>();
            int runTop = -1;
            for (int y = scanTop; y <= scanBottom; y++)
            {
                Color pixel = screenshot.GetPixel(x, y);
                bool bright = pixel.R >= 235 && pixel.G >= 232 && pixel.B >= 228;
                if (bright && runTop < 0) runTop = y;
                if ((!bright || y == scanBottom) && runTop >= 0)
                {
                    int runBottom = bright && y == scanBottom ? y : y - 1;
                    runs.Add((runTop, runBottom));
                    runTop = -1;
                }
            }
            return runs;
        }

        private static double MeasureCrossSectionSupport(Bitmap screenshot,
            int centerX, int top, int bottom)
        {
            int supported = 0;
            int samples = 0;
            int[] ys = { top, top + (bottom - top) / 2, bottom };
            foreach (int y in ys)
            {
                for (int x = Math.Max(0, centerX - 2);
                    x <= Math.Min(screenshot.Width - 1, centerX + 2); x++)
                {
                    Color pixel = screenshot.GetPixel(x, y);
                    if (IsScrollbarBright(pixel)) supported++;
                    samples++;
                }
            }
            return samples == 0 ? 0.0 : supported / (double)samples;
        }

        private static string DescribeRuns(IEnumerable<(int Top, int Bottom)> runs)
        {
            var descriptions = new List<string>();
            foreach ((int top, int bottom) in runs)
            {
                if (descriptions.Count == 4)
                {
                    descriptions.Add("...");
                    break;
                }
                descriptions.Add($"{top}-{bottom}");
            }
            return descriptions.Count == 0 ? "none" : string.Join(",", descriptions);
        }

        private static bool IsScrollbarBright(Color pixel) =>
            pixel.R >= 235 && pixel.G >= 232 && pixel.B >= 228;

        private static int ScaleX(int value, int width) =>
            Math.Max(1, (int)Math.Round(value * width / (double)ReferenceWidth));

        private static int ScaleY(int value, int height) =>
            Math.Max(1, (int)Math.Round(value * height / (double)ReferenceHeight));

        private static int ScaleLengthY(int value, int height) =>
            Math.Max(1, (int)Math.Round(value * height / (double)ReferenceHeight));

        private static int Luminance(Color pixel) =>
            ((54 * pixel.R) + (183 * pixel.G) + (19 * pixel.B)) >> 8;
    }

    internal sealed class AchievementScrollbarEvidence
    {
        private AchievementScrollbarEvidence(bool resolved, bool topAtExtent,
            bool bottomAtExtent, int x, int searchTop, int searchBottom,
            int trackTop, int trackBottom, int? thumbTop, int? thumbBottom,
            double? normalizedPosition, double confidence, int candidateCount,
            string calibration, Size captureSize, string rejectionReason,
            string topRejection, string bottomRejection, string observedRuns,
            int? expectedBottomTop, int? expectedBottomBottom, int bottomTolerance)
        {
            Resolved = resolved;
            TopAtExtent = topAtExtent;
            BottomAtExtent = bottomAtExtent;
            X = x;
            SearchTop = searchTop;
            SearchBottom = searchBottom;
            TrackTop = trackTop;
            TrackBottom = trackBottom;
            ThumbTop = thumbTop;
            ThumbBottom = thumbBottom;
            NormalizedPosition = normalizedPosition;
            Confidence = confidence;
            CandidateCount = candidateCount;
            Calibration = calibration ?? string.Empty;
            CaptureSize = captureSize;
            RejectionReason = rejectionReason ?? string.Empty;
            TopRejection = topRejection ?? string.Empty;
            BottomRejection = bottomRejection ?? string.Empty;
            ObservedRuns = observedRuns ?? string.Empty;
            ExpectedBottomTop = expectedBottomTop;
            ExpectedBottomBottom = expectedBottomBottom;
            BottomTolerance = bottomTolerance;
        }

        internal bool Resolved { get; }
        internal bool TopAtExtent { get; }
        internal bool BottomAtExtent { get; }
        internal int X { get; }
        internal int SearchTop { get; }
        internal int SearchBottom { get; }
        internal int TrackTop { get; }
        internal int TrackBottom { get; }
        internal int? ThumbTop { get; }
        internal int? ThumbBottom { get; }
        internal double? NormalizedPosition { get; }
        internal double Confidence { get; }
        internal int CandidateCount { get; }
        internal string Calibration { get; }
        internal Size CaptureSize { get; }
        internal string RejectionReason { get; }
        internal string TopRejection { get; }
        internal string BottomRejection { get; }
        internal string ObservedRuns { get; }
        internal int? ExpectedBottomTop { get; }
        internal int? ExpectedBottomBottom { get; }
        internal int BottomTolerance { get; }
        internal string Diagnostics =>
            $"scrollbarTrack=x{X},y{TrackTop}-{TrackBottom}; searchY={SearchTop}-{SearchBottom}; " +
            $"scrollbarThumb={(ThumbTop.HasValue ? $"{ThumbTop}-{ThumbBottom}" : "unresolved")}; " +
            $"normalizedPosition={(NormalizedPosition.HasValue ? NormalizedPosition.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "unavailable")}; " +
            $"confidence={Confidence.ToString("0.000", CultureInfo.InvariantCulture)}; candidates={CandidateCount}; " +
            $"top={TopAtExtent}; bottom={BottomAtExtent}; rejectionReason={RejectionReason}; " +
            $"topRejection={TopRejection}; bottomRejection={BottomRejection}; " +
            $"bottomEndpointExpected={(ExpectedBottomTop.HasValue ? $"{ExpectedBottomTop}-{ExpectedBottomBottom}" : "unavailable")}; " +
            $"bottomEndpointTolerance={BottomTolerance}px; " +
            $"observedRuns={ObservedRuns}; calibration={Calibration}; size={CaptureSize.Width}x{CaptureSize.Height}";

        internal static AchievementScrollbarEvidence CreateResolved(
            int x, int searchTop, int searchBottom, int trackTop, int trackBottom,
            string calibration, Size captureSize, int thumbTop, int thumbBottom,
            double normalizedPosition, double confidence, bool top, bool bottom,
            string topRejection, string bottomRejection, int candidateCount,
            int expectedBottomTop, int expectedBottomBottom, int bottomTolerance) =>
            new(true, top, bottom, x, searchTop, searchBottom, trackTop, trackBottom,
                thumbTop, thumbBottom, normalizedPosition, confidence, candidateCount,
                calibration, captureSize, "none", topRejection, bottomRejection,
                $"{thumbTop}-{thumbBottom}", expectedBottomTop, expectedBottomBottom,
                bottomTolerance);

        internal static AchievementScrollbarEvidence CreateUnresolved(
            int x, int searchTop, int searchBottom, int trackTop, int trackBottom,
            string calibration, Size captureSize, string rejectionReason,
            int candidateCount, string observedRuns = "none", int? thumbTop = null,
            int? thumbBottom = null, double confidence = 0.0) =>
            new(false, false, false, x, searchTop, searchBottom, trackTop, trackBottom,
                thumbTop, thumbBottom, null, confidence, candidateCount, calibration,
                captureSize, rejectionReason, rejectionReason, rejectionReason, observedRuns,
                null, null, 0);
    }
}
