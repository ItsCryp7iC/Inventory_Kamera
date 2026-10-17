using System.Collections.Generic;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// OCR result with the location of the recognized text in the supplied bitmap. Confidence is
    /// normalized to the same 0.0-1.0 range exposed by <see cref="IOcrService"/>.
    /// </summary>
    internal sealed class PositionalOcrResult
    {
        public string Text { get; }
        public Rectangle Bounds { get; }
        public float Confidence { get; }

        public PositionalOcrResult(string text, Rectangle bounds, float confidence)
        {
            Text = text;
            Bounds = bounds;
            Confidence = confidence;
        }
    }

    /// <summary>
    /// Dedicated positional-text seam for UI detection. Kept separate from <see cref="IOcrService"/>
    /// so existing scanner OCR remains text-oriented and unchanged.
    /// </summary>
    internal interface IPositionalOcrService
    {
        IReadOnlyList<PositionalOcrResult> AnalyzeTextRegions(Bitmap bitmap);
    }
}
