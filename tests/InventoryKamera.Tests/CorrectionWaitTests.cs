using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace InventoryKamera.Tests
{
    public class CorrectionWaitTests
    {
        [Fact]
        public async Task PendingCorrectionBlocksUntilResolved()
        {
            using var pending = new PendingCorrection();
            using var session = new ScanSession();
            pending.Start();

            Task<bool> wait = Task.Run(() =>
                pending.ViewModel.WaitIfCorrectionPending(session.CancellationToken));

            await Task.Delay(100);
            Assert.False(wait.IsCompleted);
            pending.Resolve();

            Assert.True(await wait.WaitAsync(TimeSpan.FromSeconds(2)));
            await pending.CorrectionTask.WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task CancellationReleasesWaitWithoutResolvingCorrection()
        {
            using var pending = new PendingCorrection();
            using var session = new ScanSession();
            pending.Start();
            Task<bool> wait = Task.Run(() =>
                pending.ViewModel.WaitIfCorrectionPending(session.CancellationToken));

            session.RequestCancellation();

            Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(pending.CorrectionTask.IsCompleted);
            pending.Resolve();
            await pending.CorrectionTask.WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task CancellingOneSessionDoesNotReleaseAnotherSessionsWait()
        {
            using var pending = new PendingCorrection();
            using var first = new ScanSession();
            using var second = new ScanSession();
            pending.Start();
            Task<bool> firstWait = Task.Run(() =>
                pending.ViewModel.WaitIfCorrectionPending(first.CancellationToken));
            Task<bool> secondWait = Task.Run(() =>
                pending.ViewModel.WaitIfCorrectionPending(second.CancellationToken));

            first.RequestCancellation();

            Assert.False(await firstWait.WaitAsync(TimeSpan.FromSeconds(2)));
            await Task.Delay(100);
            Assert.False(secondWait.IsCompleted);
            pending.Resolve();
            Assert.True(await secondWait.WaitAsync(TimeSpan.FromSeconds(2)));
            await pending.CorrectionTask.WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task LateCorrectionResolutionAfterCancellationIsSafe()
        {
            using var pending = new PendingCorrection();
            using var cancelledSession = new ScanSession();
            pending.Start();
            Task<bool> cancelledWait = Task.Run(() =>
                pending.ViewModel.WaitIfCorrectionPending(cancelledSession.CancellationToken));
            cancelledSession.RequestCancellation();
            Assert.False(await cancelledWait.WaitAsync(TimeSpan.FromSeconds(2)));

            pending.Resolve();
            Assert.Equal("corrected", await pending.CorrectionTask.WaitAsync(TimeSpan.FromSeconds(2)));

            using var nextSession = new ScanSession();
            Assert.True(pending.ViewModel.WaitIfCorrectionPending(nextSession.CancellationToken));
        }

        [Fact]
        public async Task InlineCorrectionReturnsOnCancellationWithoutClosingInteraction()
        {
            using var pending = new PendingCorrection();
            using var session = new ScanSession();
            pending.Start(session.CancellationToken);

            session.RequestCancellation();

            Assert.Equal("uncorrected", await pending.CorrectionTask.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(pending.InteractionCompleted);

            pending.Resolve();
            await pending.InteractionTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(pending.InteractionCompleted);
        }

        [Fact]
        public async Task DeferredCorrectionFlushReturnsOnCancellationWithoutApplyingLateAnswer()
        {
            var viewModel = new ScanViewModel();
            using var session = new ScanSession();
            using var image = new Bitmap(2, 2);
            using var interactionStarted = new ManualResetEventSlim(false);
            using var resolve = new ManualResetEventSlim(false);
            var interactionCompleted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            bool applied = false;
            viewModel.CorrectionRequested += args =>
            {
                interactionStarted.Set();
                resolve.Wait();
                args.ResolvedText = "corrected";
                interactionCompleted.TrySetResult(true);
            };
            viewModel.EnqueueCorrection(image, "uncorrected", 10, "test field", _ => applied = true);
            Task flush = Task.Run(() =>
                viewModel.FlushDeferredCorrections(session.CancellationToken));
            Assert.True(interactionStarted.Wait(TimeSpan.FromSeconds(2)));

            session.RequestCancellation();

            await flush.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(applied);
            Assert.False(interactionCompleted.Task.IsCompleted);

            resolve.Set();
            await interactionCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(applied);
        }

        private sealed class PendingCorrection : IDisposable
        {
            private readonly ManualResetEventSlim handlerEntered = new ManualResetEventSlim(false);
            private readonly ManualResetEventSlim resolve = new ManualResetEventSlim(false);
            private readonly Bitmap image = new Bitmap(2, 2);
            private readonly TaskCompletionSource<bool> interactionCompleted =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal ScanViewModel ViewModel { get; } = new ScanViewModel();
            internal Task<string> CorrectionTask { get; private set; }
            internal Task InteractionTask => interactionCompleted.Task;
            internal bool InteractionCompleted => interactionCompleted.Task.IsCompleted;

            internal PendingCorrection()
            {
                ViewModel.CorrectionRequested += args =>
                {
                    handlerEntered.Set();
                    resolve.Wait();
                    args.ResolvedText = "corrected";
                    interactionCompleted.TrySetResult(true);
                };
            }

            internal void Start(CancellationToken cancellationToken = default)
            {
                CorrectionTask = Task.Run(() =>
                    ViewModel.RequestCorrection(
                        image,
                        "uncorrected",
                        10,
                        "test field",
                        cancellationToken));
                Assert.True(handlerEntered.Wait(TimeSpan.FromSeconds(2)));
            }

            internal void Resolve() => resolve.Set();

            public void Dispose()
            {
                resolve.Set();
                CorrectionTask?.Wait(TimeSpan.FromSeconds(2));
                image.Dispose();
                handlerEntered.Dispose();
                resolve.Dispose();
            }
        }
    }
}
