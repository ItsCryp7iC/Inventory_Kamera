using System;
using System.Drawing;

namespace InventoryKamera
{
    internal static class BitmapOwnership
    {
        /// <summary>Replaces an owned bitmap and releases the prior instance.</summary>
        internal static void Replace(ref Bitmap owned, Bitmap replacement)
        {
            Replace(ref owned, replacement, bitmap => bitmap.Dispose());
        }

        internal static void Replace(
            ref Bitmap owned,
            Bitmap replacement,
            Action<Bitmap> disposeBitmap)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (disposeBitmap == null) throw new ArgumentNullException(nameof(disposeBitmap));

            Bitmap previous = owned;
            owned = replacement;
            if (previous != null && !ReferenceEquals(previous, replacement)) disposeBitmap(previous);
        }
    }
}
