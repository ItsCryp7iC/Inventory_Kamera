using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ResourceOwnershipTests
    {
        [Fact]
        public void CharacterPreprocessingReplacement_ReleasesTheReplacedBitmap()
        {
            using var original = new Bitmap(1, 1);
            using var replacement = new Bitmap(2, 2);
            Bitmap owned = original;
            int disposals = 0;

            BitmapOwnership.Replace(ref owned, replacement, bitmap =>
            {
                Assert.Same(original, bitmap);
                disposals++;
            });

            Assert.Same(replacement, owned);
            Assert.Equal(1, disposals);
        }

        [Fact]
        public void CharacterPreprocessingReplacement_DoesNotReleaseTheSameInstance()
        {
            using var bitmap = new Bitmap(1, 1);
            Bitmap owned = bitmap;
            int disposals = 0;

            BitmapOwnership.Replace(ref owned, bitmap, _ => disposals++);

            Assert.Same(bitmap, owned);
            Assert.Equal(0, disposals);
        }

        [Fact]
        public void SetGamma_PreservesLegacyPixelOutputAndBorrowedSource()
        {
            using var source = new Bitmap(2, 1);
            source.SetPixel(0, 0, Color.FromArgb(12, 64, 128));
            source.SetPixel(1, 0, Color.FromArgb(200, 220, 240));
            Bitmap result = source;

            GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref result);

            try
            {
                Assert.NotSame(source, result);
                Assert.Equal(Color.FromArgb(12, 64, 128).ToArgb(), source.GetPixel(0, 0).ToArgb());
                Assert.Equal(ExpectedGamma(Color.FromArgb(12, 64, 128), 0.2).ToArgb(), result.GetPixel(0, 0).ToArgb());
                Assert.Equal(ExpectedGamma(Color.FromArgb(200, 220, 240), 0.2).ToArgb(), result.GetPixel(1, 0).ToArgb());
            }
            finally
            {
                if (!ReferenceEquals(source, result)) result.Dispose();
            }
        }

        [Fact]
        public void SetBrightness_PreservesLegacyPixelOutputAndBorrowedSource()
        {
            using var source = new Bitmap(1, 1);
            source.SetPixel(0, 0, Color.FromArgb(12, 64, 240));
            Bitmap result = source;

            GenshinProcesor.SetBrightness(-30, ref result);

            try
            {
                Assert.NotSame(source, result);
                Assert.Equal(Color.FromArgb(12, 64, 240).ToArgb(), source.GetPixel(0, 0).ToArgb());
                Assert.Equal(Color.FromArgb(1, 34, 210).ToArgb(), result.GetPixel(0, 0).ToArgb());
            }
            finally
            {
                if (!ReferenceEquals(source, result)) result.Dispose();
            }
        }

        [Fact]
        public void WorkItemDispose_ReleasesEachDistinctBitmapExactlyOnce()
        {
            using var first = new Bitmap(1, 1);
            using var second = new Bitmap(1, 1);
            var releases = new Dictionary<Bitmap, int>(ReferenceEqualityComparer.Instance);
            var work = new OCRImageCollection(
                new List<Bitmap> { first, second, first },
                "weapon",
                1,
                bitmap => releases[bitmap] = releases.TryGetValue(bitmap, out int count) ? count + 1 : 1);

            work.Dispose();
            work.Dispose();

            Assert.True(work.IsDisposed);
            Assert.Equal(2, releases.Count);
            Assert.Equal(1, releases[first]);
            Assert.Equal(1, releases[second]);
        }

        [Fact]
        public async Task NormalWorkItemProcessing_KeepsImagesAliveThenDisposesThem()
        {
            using var bitmap = new Bitmap(1, 1);
            int releases = 0;
            var work = TrackedWork(bitmap, () => releases++);

            await OCRImageCollectionProcessor.ProcessAsync(work, current =>
            {
                Assert.False(current.IsDisposed);
                Assert.Equal(1, current.Bitmaps[0].Width);
                return Task.CompletedTask;
            });

            Assert.True(work.IsDisposed);
            Assert.Equal(1, releases);
        }

        [Fact]
        public async Task EnhancementMaterialEarlyExit_DisposesAllOwnedImages()
        {
            using var first = new Bitmap(1, 1);
            using var second = new Bitmap(1, 1);
            int releases = 0;
            var work = new OCRImageCollection(
                new List<Bitmap> { first, second },
                "weapon",
                2,
                _ => releases++);

            await OCRImageCollectionProcessor.ProcessAsync(work, _ => Task.CompletedTask);

            Assert.True(work.IsDisposed);
            Assert.Equal(2, releases);
        }

        [Fact]
        public async Task ProcessingException_DisposesOwnedImagesAndPropagates()
        {
            using var bitmap = new Bitmap(1, 1);
            int releases = 0;
            var work = TrackedWork(bitmap, () => releases++);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                OCRImageCollectionProcessor.ProcessAsync(
                    work,
                    _ => throw new InvalidOperationException("test")));

            Assert.True(work.IsDisposed);
            Assert.Equal(1, releases);
        }

        [Fact]
        public async Task ProcessingCancellation_DisposesOwnedImagesAndPropagates()
        {
            using var bitmap = new Bitmap(1, 1);
            int releases = 0;
            var work = TrackedWork(bitmap, () => releases++);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                OCRImageCollectionProcessor.ProcessAsync(
                    work,
                    _ => Task.FromCanceled(cancellation.Token)));

            Assert.True(work.IsDisposed);
            Assert.Equal(1, releases);
        }

        [Fact]
        public void SessionShutdown_DisposesUnconsumedQueuedWork()
        {
            using var bitmap = new Bitmap(1, 1);
            int releases = 0;
            var work = TrackedWork(bitmap, () => releases++);
            var session = new ScanSession();

            Assert.True(session.TryQueueWork(work));
            session.Dispose();

            Assert.True(work.IsDisposed);
            Assert.Equal(1, releases);
        }

        [Fact]
        public void RejectedQueueTransfer_DisposesWorkImmediately()
        {
            using var bitmap = new Bitmap(1, 1);
            int releases = 0;
            var work = TrackedWork(bitmap, () => releases++);
            var session = new ScanSession();
            session.Dispose();

            Assert.False(session.TryQueueWork(work));

            Assert.True(work.IsDisposed);
            Assert.Equal(1, releases);
        }

        private static OCRImageCollection TrackedWork(Bitmap bitmap, Action released)
        {
            return new OCRImageCollection(
                new List<Bitmap> { bitmap },
                "artifact",
                1,
                _ => released());
        }

        private static Color ExpectedGamma(Color color, double gamma)
        {
            byte Apply(byte value) => (byte)Math.Min(
                255,
                (int)((255.0 * Math.Pow(value / 255.0, 1.0 / gamma)) + 0.5));

            return Color.FromArgb(Apply(color.R), Apply(color.G), Apply(color.B));
        }
    }
}
