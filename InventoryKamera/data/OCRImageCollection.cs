using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// Owns the captured images for one queued weapon/artifact OCR item. Ownership transfers to
    /// <see cref="ScanSession.TryQueueWork"/> when it is called; disposing the work item releases
    /// every distinct bitmap exactly once.
    /// </summary>
    public sealed class OCRImageCollection : IDisposable
    {
        private readonly Action<Bitmap> disposeBitmap;
        private int disposed;

        public List<Bitmap> Bitmaps { get; }
        public string Type { get; }
        public int Id { get; }

        public OCRImageCollection(List<Bitmap> bitmaps, string type, int id)
            : this(bitmaps, type, id, bitmap => bitmap.Dispose())
        {
        }

        internal OCRImageCollection(
            List<Bitmap> bitmaps,
            string type,
            int id,
            Action<Bitmap> bitmapDisposer)
        {
            Bitmaps = bitmaps ?? throw new ArgumentNullException(nameof(bitmaps));
            Type = type;
            Id = id;
            disposeBitmap = bitmapDisposer ?? throw new ArgumentNullException(nameof(bitmapDisposer));
        }

        internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;

            var released = new HashSet<Bitmap>(ReferenceEqualityComparer.Instance);
            foreach (Bitmap bitmap in Bitmaps)
            {
                if (bitmap != null && released.Add(bitmap)) disposeBitmap(bitmap);
            }
        }
    }
}
