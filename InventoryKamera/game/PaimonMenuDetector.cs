using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text.RegularExpressions;

namespace InventoryKamera.game
{
    internal sealed class PaimonMenuTile
    {
        public string Label { get; }
        public Rectangle LabelBounds { get; }
        public Rectangle TileBounds { get; }
        public float OcrConfidence { get; }
        public int FocusMarkerScore { get; }
        public PointF Center => new PointF(
            TileBounds.Left + TileBounds.Width / 2f,
            TileBounds.Top + TileBounds.Height / 2f);

        public PaimonMenuTile(
            string label,
            Rectangle labelBounds,
            Rectangle tileBounds,
            float ocrConfidence,
            int focusMarkerScore = 0)
        {
            Label = label ?? string.Empty;
            LabelBounds = labelBounds;
            TileBounds = tileBounds;
            OcrConfidence = ocrConfidence;
            FocusMarkerScore = focusMarkerScore;
        }

        public override string ToString() => string.IsNullOrWhiteSpace(Label)
            ? $"(unreadable tile at {Center.X:0},{Center.Y:0})"
            : Label;
    }

    internal sealed class PaimonMenuDetection
    {
        public IReadOnlyList<PaimonMenuTile> Tiles { get; }
        public PaimonMenuTile SelectedTile { get; }
        public PaimonMenuTile InventoryTile { get; }
        public PaimonMenuTile CharacterTile { get; }
        public string FailureReason { get; }
        public bool HasUsableSelection => SelectedTile != null && Tiles.Count > 0;

        public PaimonMenuDetection(
            IEnumerable<PaimonMenuTile> tiles,
            PaimonMenuTile selectedTile,
            PaimonMenuTile inventoryTile,
            PaimonMenuTile characterTile,
            string failureReason = null)
        {
            Tiles = new ReadOnlyCollection<PaimonMenuTile>((tiles ?? Array.Empty<PaimonMenuTile>()).ToList());
            SelectedTile = selectedTile;
            InventoryTile = inventoryTile;
            CharacterTile = characterTile;
            FailureReason = failureReason;
        }

        public string Describe()
        {
            string labels = string.Join(", ", Tiles.Select(t => t.ToString()));
            return $"tiles={Tiles.Count}; selected={SelectedTile?.ToString() ?? "(none)"}; " +
                $"inventory={InventoryTile?.ToString() ?? "(none)"}; labels=[{labels}]";
        }
    }

    internal interface IPaimonMenuDetector
    {
        PaimonMenuDetection Detect(Bitmap screenshot);
    }

    /// <summary>
    /// Detects Paimon-menu tiles from their blue-grey card backgrounds, reads their labels with
    /// positional OCR, and identifies focus from the white top chevron plus the enlarged cream
    /// selection border. All geometry is relative to the supplied bitmap; no menu row/column or
    /// target coordinate is encoded here.
    /// </summary>
    internal sealed class PaimonMenuDetector : IPaimonMenuDetector
    {
        // Broad scroll viewport only. The target's Y is deliberately not constrained inside it.
        private const double MenuLeft = 0.075;
        private const double MenuTop = 0.28;
        private const double MenuRight = 0.37;
        private const double MenuBottom = 0.93;

        // Tile-card background range measured from all supplied selected/unselected and scrolled
        // fixtures. It excludes the cream panel/border while retaining the subtly lighter selected
        // card. Values stay isolated here so future theme changes have one calibration point.
        private const byte TileRedMin = 40;
        private const byte TileRedMax = 145;
        private const byte TileGreenMin = 45;
        private const byte TileGreenMax = 150;
        private const byte TileBlueMin = 60;
        private const byte TileBlueMax = 165;

        private const double MinTileWidthByHeight = 0.075;
        private const double MaxTileWidthByHeight = 0.16;
        private const double MinTileHeightByHeight = 0.065;
        private const double MaxTileHeightByHeight = 0.155;
        private const int MinimumTileCount = 4;

        private const double MarkerHalfWidthByHeight = 0.015;
        private const double MarkerTopPaddingByHeight = 0.008;
        private const double MarkerSearchHeightByHeight = 0.028;
        private const double SelectedBorderGrowthByHeight = 0.005;
        private const int MarkerWhiteFloor = 235;
        private const int MarkerPixelsAt1080 = 20;
        private const float MinimumWordConfidence = 0.15f;
        private const float MinimumSemanticConfidence = 0.25f;
        private const double MinimumSemanticSimilarity = 72;

        private readonly IPositionalOcrService positionalOcr;
        private readonly IImagePreprocessor imagePreprocessor;

        public PaimonMenuDetector(IPositionalOcrService positionalOcr, IImagePreprocessor imagePreprocessor)
        {
            this.positionalOcr = positionalOcr ?? throw new ArgumentNullException(nameof(positionalOcr));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
        }

        public PaimonMenuDetection Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            Rectangle viewport = GetMenuViewport(screenshot.Size);
            using (Bitmap tileMask = CreateTileMask(screenshot, viewport))
            {
                int minWidth = Math.Max(1, (int)(screenshot.Height * MinTileWidthByHeight));
                int maxWidth = Math.Max(minWidth, (int)(screenshot.Height * MaxTileWidthByHeight));
                int minHeight = Math.Max(1, (int)(screenshot.Height * MinTileHeightByHeight));
                int maxHeight = Math.Max(minHeight, (int)(screenshot.Height * MaxTileHeightByHeight));

                List<Rectangle> relativeTiles = imagePreprocessor.FindBlobRectangles(
                    tileMask, minWidth, maxWidth, minHeight, maxHeight);
                var tileBounds = relativeTiles
                    .Select(r => new Rectangle(r.X + viewport.X, r.Y + viewport.Y, r.Width, r.Height))
                    .OrderBy(r => r.Top)
                    .ThenBy(r => r.Left)
                    .ToList();

                if (tileBounds.Count < MinimumTileCount)
                {
                    return new PaimonMenuDetection(
                        Array.Empty<PaimonMenuTile>(), null, null, null,
                        $"Paimon menu card detection found only {tileBounds.Count} tile(s)." );
                }

                IReadOnlyList<PositionalOcrResult> words;
                using (Bitmap ocrCanvas = CreateOcrCanvas(screenshot, viewport, tileBounds))
                {
                    words = positionalOcr.AnalyzeTextRegions(ocrCanvas);
                }

                int medianWidth = Median(tileBounds.Select(r => r.Width));
                int medianHeight = Median(tileBounds.Select(r => r.Height));
                int requiredGrowth = Math.Max(3, (int)(screenshot.Height * SelectedBorderGrowthByHeight));
                double scale = screenshot.Height / 1080.0;
                int markerThreshold = Math.Max(6, (int)(MarkerPixelsAt1080 * scale * scale));

                var tiles = new List<PaimonMenuTile>(tileBounds.Count);
                foreach (Rectangle bounds in tileBounds)
                {
                    var tileWords = words
                        .Select(w => Offset(w, viewport.Location))
                        .Where(w => w.Confidence >= MinimumWordConfidence)
                        .Where(w => bounds.Contains(CenterOf(w.Bounds)))
                        .Where(w => CenterOf(w.Bounds).Y >= bounds.Top + bounds.Height * 0.54)
                        .Where(w => Normalize(w.Text).Length > 0)
                        .OrderBy(w => w.Bounds.Top)
                        .ThenBy(w => w.Bounds.Left)
                        .ToList();

                    string label = string.Join(" ", tileWords.Select(w => w.Text.Trim()));
                    Rectangle labelBounds = tileWords.Count == 0
                        ? Rectangle.Empty
                        : tileWords.Select(w => w.Bounds).Aggregate(Rectangle.Union);
                    float confidence = tileWords.Count == 0 ? 0 : tileWords.Average(w => w.Confidence);
                    int markerScore = CountFocusMarkerPixels(screenshot, bounds);

                    tiles.Add(new PaimonMenuTile(label, labelBounds, bounds, confidence, markerScore));
                }

                var selectedCandidates = tiles.Where(t =>
                    t.FocusMarkerScore >= markerThreshold &&
                    t.TileBounds.Width >= medianWidth + requiredGrowth &&
                    t.TileBounds.Height >= medianHeight + requiredGrowth).ToList();

                PaimonMenuTile selected = selectedCandidates.Count == 1 ? selectedCandidates[0] : null;
                PaimonMenuTile inventory = FindSemanticTile(tiles, "Inventory");
                PaimonMenuTile character = FindSemanticTile(tiles, "Character");

                string failure = selectedCandidates.Count switch
                {
                    0 => "No selected Paimon menu tile passed the chevron and border checks.",
                    > 1 => $"Selection detection was ambiguous ({selectedCandidates.Count} candidates).",
                    _ => null,
                };

                return new PaimonMenuDetection(tiles, selected, inventory, character, failure);
            }
        }

        private static Rectangle GetMenuViewport(Size size)
        {
            int left = (int)(size.Width * MenuLeft);
            int top = (int)(size.Height * MenuTop);
            int right = (int)(size.Width * MenuRight);
            int bottom = (int)(size.Height * MenuBottom);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private static PositionalOcrResult Offset(PositionalOcrResult result, Point offset)
        {
            var bounds = result.Bounds;
            bounds.Offset(offset);
            return new PositionalOcrResult(result.Text, bounds, result.Confidence);
        }

        private static Point CenterOf(Rectangle bounds) =>
            new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

        private static int Median(IEnumerable<int> values)
        {
            int[] ordered = values.OrderBy(v => v).ToArray();
            return ordered[ordered.Length / 2];
        }

        private static PaimonMenuTile FindSemanticTile(IEnumerable<PaimonMenuTile> tiles, string target)
        {
            string normalizedTarget = Normalize(target);
            return tiles
                .Where(t => t.OcrConfidence >= MinimumSemanticConfidence)
                .Select(t => new { Tile = t, Similarity = Similarity(Normalize(t.Label), normalizedTarget) })
                .Where(x => x.Similarity >= MinimumSemanticSimilarity)
                .OrderByDescending(x => x.Similarity)
                .ThenByDescending(x => x.Tile.OcrConfidence)
                .Select(x => x.Tile)
                .FirstOrDefault();
        }

        internal static string Normalize(string text) =>
            Regex.Replace((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]", string.Empty);

        private static double Similarity(string source, string target)
        {
            if (source.Length == 0 || target.Length == 0) return 0;
            if (source == target) return 100;

            var distance = new int[source.Length + 1, target.Length + 1];
            for (int i = 0; i <= source.Length; i++) distance[i, 0] = i;
            for (int j = 0; j <= target.Length; j++) distance[0, j] = j;
            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            int maxLength = Math.Max(source.Length, target.Length);
            return (1.0 - distance[source.Length, target.Length] / (double)maxLength) * 100.0;
        }

        private static int CountFocusMarkerPixels(Bitmap screenshot, Rectangle tileBounds)
        {
            int halfWidth = Math.Max(5, (int)(screenshot.Height * MarkerHalfWidthByHeight));
            int topPadding = Math.Max(2, (int)(screenshot.Height * MarkerTopPaddingByHeight));
            int searchHeight = Math.Max(8, (int)(screenshot.Height * MarkerSearchHeightByHeight));
            int centerX = tileBounds.Left + tileBounds.Width / 2;
            Rectangle search = Rectangle.Intersect(
                new Rectangle(centerX - halfWidth, tileBounds.Top - topPadding, halfWidth * 2 + 1, searchHeight),
                new Rectangle(Point.Empty, screenshot.Size));

            int count = 0;
            for (int y = search.Top; y < search.Bottom; y++)
            {
                for (int x = search.Left; x < search.Right; x++)
                {
                    Color pixel = screenshot.GetPixel(x, y);
                    if (pixel.R >= MarkerWhiteFloor && pixel.G >= MarkerWhiteFloor && pixel.B >= MarkerWhiteFloor)
                        count++;
                }
            }
            return count;
        }

        private static unsafe Bitmap CreateTileMask(Bitmap screenshot, Rectangle viewport)
        {
            var mask = new Bitmap(viewport.Width, viewport.Height, PixelFormat.Format8bppIndexed);
            ColorPalette palette = mask.Palette;
            for (int i = 0; i < palette.Entries.Length; i++) palette.Entries[i] = Color.FromArgb(i, i, i);
            mask.Palette = palette;

            BitmapData sourceData = screenshot.LockBits(viewport, ImageLockMode.ReadOnly, screenshot.PixelFormat);
            BitmapData maskData = mask.LockBits(
                new Rectangle(0, 0, mask.Width, mask.Height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                int bytesPerPixel = Image.GetPixelFormatSize(screenshot.PixelFormat) / 8;
                if (bytesPerPixel < 3)
                    throw new NotSupportedException($"Paimon menu detection expects a colour image, got {screenshot.PixelFormat}.");

                byte* sourceBase = (byte*)sourceData.Scan0;
                byte* maskBase = (byte*)maskData.Scan0;
                for (int y = 0; y < viewport.Height; y++)
                {
                    byte* sourceRow = sourceBase + y * sourceData.Stride;
                    byte* maskRow = maskBase + y * maskData.Stride;
                    for (int x = 0; x < viewport.Width; x++)
                    {
                        byte* pixel = sourceRow + x * bytesPerPixel;
                        maskRow[x] = IsTileBackground(pixel[2], pixel[1], pixel[0]) ? (byte)255 : (byte)0;
                    }
                }
            }
            finally
            {
                screenshot.UnlockBits(sourceData);
                mask.UnlockBits(maskData);
            }
            return mask;
        }

        private static unsafe Bitmap CreateOcrCanvas(
            Bitmap screenshot,
            Rectangle viewport,
            IEnumerable<Rectangle> globalTileBounds)
        {
            var canvas = new Bitmap(viewport.Width, viewport.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(canvas)) graphics.Clear(Color.White);

            BitmapData sourceData = screenshot.LockBits(viewport, ImageLockMode.ReadOnly, screenshot.PixelFormat);
            BitmapData canvasData = canvas.LockBits(
                new Rectangle(0, 0, canvas.Width, canvas.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                int sourceBytesPerPixel = Image.GetPixelFormatSize(screenshot.PixelFormat) / 8;
                byte* sourceBase = (byte*)sourceData.Scan0;
                byte* canvasBase = (byte*)canvasData.Scan0;

                foreach (Rectangle globalBounds in globalTileBounds)
                {
                    Rectangle bounds = globalBounds;
                    bounds.Offset(-viewport.X, -viewport.Y);
                    bounds.Intersect(new Rectangle(0, 0, viewport.Width, viewport.Height));
                    for (int y = bounds.Top; y < bounds.Bottom; y++)
                    {
                        byte* sourceRow = sourceBase + y * sourceData.Stride;
                        byte* canvasRow = canvasBase + y * canvasData.Stride;
                        for (int x = bounds.Left; x < bounds.Right; x++)
                        {
                            byte* sourcePixel = sourceRow + x * sourceBytesPerPixel;
                            if (IsTileBackground(sourcePixel[2], sourcePixel[1], sourcePixel[0])) continue;
                            byte* canvasPixel = canvasRow + x * 3;
                            canvasPixel[0] = canvasPixel[1] = canvasPixel[2] = 0;
                        }
                    }
                }
            }
            finally
            {
                screenshot.UnlockBits(sourceData);
                canvas.UnlockBits(canvasData);
            }
            return canvas;
        }

        private static bool IsTileBackground(byte red, byte green, byte blue) =>
            red >= TileRedMin && red <= TileRedMax &&
            green >= TileGreenMin && green <= TileGreenMax &&
            blue >= TileBlueMin && blue <= TileBlueMax &&
            blue >= red;
    }
}
