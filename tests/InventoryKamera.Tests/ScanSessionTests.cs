using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ScanSessionTests
    {
        [Fact]
        public void NewSession_StartsUncancelled()
        {
            using var session = new ScanSession();

            Assert.False(session.IsCancellationRequested);
            Assert.False(session.CancellationToken.IsCancellationRequested);
            Assert.True(session.ShouldContinue());
        }

        [Fact]
        public void RequestCancellation_CancelsSessionToken()
        {
            using var session = new ScanSession();

            session.RequestCancellation();

            Assert.True(session.IsCancellationRequested);
            Assert.True(session.CancellationToken.IsCancellationRequested);
            Assert.False(session.ShouldContinue());
        }

        [Fact]
        public void Sessions_DoNotShareCancellationState()
        {
            using var first = new ScanSession();
            using var second = new ScanSession();

            first.RequestCancellation();

            Assert.True(first.IsCancellationRequested);
            Assert.False(second.IsCancellationRequested);
            Assert.True(second.ShouldContinue());
        }

        [Fact]
        public async Task RepresentativeLoop_ObservesCancellation()
        {
            using var session = new ScanSession();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int iterations = 0;
            Task loop = Task.Run(() =>
            {
                started.SetResult(true);
                while (session.ShouldContinue())
                {
                    Interlocked.Increment(ref iterations);
                    Thread.Yield();
                }
            });

            await started.Task;
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref iterations) > 0, 1000));
            session.RequestCancellation();
            Task completed = await Task.WhenAny(loop, Task.Delay(2000));

            Assert.Same(loop, completed);
        }

        [Fact]
        public void ScraperLocalStop_DoesNotCancelSession()
        {
            using var session = new ScanSession();
            var scraper = new TestInventoryScraper(session);

            scraper.RequestLocalStop();

            Assert.False(scraper.ShouldContinue);
            Assert.False(session.IsCancellationRequested);
            Assert.True(session.ShouldContinue(scraperStopRequested: false));
        }

        [Fact]
        public void Sessions_DoNotShareWorkerChannels()
        {
            using var first = new ScanSession();
            using var second = new ScanSession();
            var work = new OCRImageCollection(new List<System.Drawing.Bitmap>(), "weapon", 7);

            Assert.True(first.TryQueueWork(work));
            Assert.True(first.WorkReader.TryRead(out OCRImageCollection received));
            Assert.Same(work, received);
            Assert.False(second.WorkReader.TryRead(out _));
        }

        [Fact]
        public async Task CompletingOneSession_DoesNotCompleteAnother()
        {
            using var first = new ScanSession();
            using var second = new ScanSession();

            first.CompleteWork();
            first.CompleteWork(); // idempotent

            Assert.False(await first.WorkReader.WaitToReadAsync());
            Assert.True(second.TryQueueWork(
                new OCRImageCollection(new List<System.Drawing.Bitmap>(), "artifact", 3)));
            Assert.True(await second.WorkReader.WaitToReadAsync());
        }

        [Fact]
        public async Task CompletingChannel_AllowsWorkerToDrainAndExit()
        {
            using var session = new ScanSession();
            int processed = 0;
            session.StartWorkers(1, async abortToken =>
            {
                await foreach (OCRImageCollection work in session.WorkReader.ReadAllAsync(abortToken))
                {
                    Interlocked.Increment(ref processed);
                    foreach (var bitmap in work.Bitmaps) bitmap.Dispose();
                }
            });
            session.TryQueueWork(new OCRImageCollection(
                new List<System.Drawing.Bitmap>(), "weapon", 1));

            session.CompleteWork();
            Task awaitWorkers = Task.Run(session.AwaitWorkers);
            Task completed = await Task.WhenAny(awaitWorkers, Task.Delay(2000));

            Assert.Same(awaitWorkers, completed);
            Assert.Equal(1, processed);
        }

        [Fact]
        public async Task AbortingOneSession_DoesNotAffectOtherSessionChannel()
        {
            using var first = new ScanSession();
            using var second = new ScanSession();
            first.StartWorkers(1, async abortToken =>
            {
                await foreach (OCRImageCollection _ in first.WorkReader.ReadAllAsync(abortToken)) { }
            });

            Task abort = Task.Run(first.AbortWorkersAndWait);
            Task completed = await Task.WhenAny(abort, Task.Delay(2000));

            Assert.Same(abort, completed);
            Assert.True(second.TryQueueWork(
                new OCRImageCollection(new List<System.Drawing.Bitmap>(), "weapon", 2)));
            Assert.True(second.WorkReader.TryRead(out _));
            Assert.False(second.IsCancellationRequested);
        }

        private sealed class TestInventoryScraper : InventoryScraper
        {
            public TestInventoryScraper(ScanSession session)
                : base(null, null, null, null, session)
            {
            }

            public bool ShouldContinue => scanSession.ShouldContinue(StopScanning);

            public void RequestLocalStop()
            {
                StopScanning = true;
            }
        }
    }
}
