using System;
using System.Collections.Generic;
using System.Drawing;

namespace InventoryKamera.game
{
    /// <summary>
    /// Identifies a focused Paimon-menu tile by its detected geometry, independently of OCR text.
    /// Blob detection can move or resize a card by a few pixels between captures, so centers and
    /// dimensions are compared with a tolerance relative to the smaller detected tile.
    /// </summary>
    internal readonly struct PaimonMenuTilePhysicalIdentity
    {
        internal const double GeometryToleranceRatio = 0.20;
        internal const float MinimumTolerancePixels = 4f;

        public Rectangle Bounds { get; }

        public PaimonMenuTilePhysicalIdentity(PaimonMenuTile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            Bounds = tile.TileBounds;
        }

        public bool Matches(PaimonMenuTile tile) =>
            tile != null && BoundsMatch(Bounds, tile.TileBounds);

        public bool Matches(PaimonMenuTilePhysicalIdentity other) =>
            BoundsMatch(Bounds, other.Bounds);

        public static bool AreSame(PaimonMenuTile first, PaimonMenuTile second) =>
            first != null && second != null && BoundsMatch(first.TileBounds, second.TileBounds);

        /// <summary>
        /// Confirms that selected and semantic target geometry correspond to exactly one detected
        /// tile. This prevents confirmation if overlapping/duplicate detections make proximity
        /// ambiguous.
        /// </summary>
        public static bool AreUniqueCorrespondingTiles(
            PaimonMenuTile selected,
            PaimonMenuTile semanticTarget,
            IReadOnlyList<PaimonMenuTile> detectedTiles)
        {
            if (!AreSame(selected, semanticTarget) || detectedTiles == null) return false;

            int selectedMatches = 0;
            int targetMatches = 0;
            foreach (PaimonMenuTile tile in detectedTiles)
            {
                if (AreSame(selected, tile)) selectedMatches++;
                if (AreSame(semanticTarget, tile)) targetMatches++;
            }

            return selectedMatches == 1 && targetMatches == 1;
        }

        private static bool BoundsMatch(Rectangle first, Rectangle second)
        {
            float widthTolerance = Tolerance(first.Width, second.Width);
            float heightTolerance = Tolerance(first.Height, second.Height);
            PointF firstCenter = Center(first);
            PointF secondCenter = Center(second);

            return Math.Abs(firstCenter.X - secondCenter.X) <= widthTolerance &&
                Math.Abs(firstCenter.Y - secondCenter.Y) <= heightTolerance &&
                Math.Abs(first.Width - second.Width) <= widthTolerance &&
                Math.Abs(first.Height - second.Height) <= heightTolerance;
        }

        private static float Tolerance(int firstDimension, int secondDimension) =>
            Math.Max(
                MinimumTolerancePixels,
                (float)(Math.Min(firstDimension, secondDimension) * GeometryToleranceRatio));

        private static PointF Center(Rectangle bounds) => new PointF(
            bounds.Left + bounds.Width / 2f,
            bounds.Top + bounds.Height / 2f);
    }
}
