using System;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ScanRunTests
    {
        [Fact]
        public void NewRunOwnsItsSessionAndScanner()
        {
            var owner = new ScanRunOwner();
            var reporter = new ScanViewModel();

            Assert.True(owner.TryCreate(reporter, out ScanRun run));

            Assert.NotNull(run.Session);
            Assert.Null(run.Scanner);
            Assert.NotNull(run.InitializeScanner());
            Assert.Same(run.Scanner, run.InitializeScanner());
            Assert.False(run.Session.IsCancellationRequested);
            owner.Complete(run);
        }

        [Fact]
        public void SequentialRunsDoNotShareCancellation()
        {
            var owner = new ScanRunOwner();
            var reporter = new ScanViewModel();
            owner.TryCreate(reporter, out ScanRun first);
            first.RequestCancellation();
            owner.Complete(first);

            Assert.True(owner.TryCreate(reporter, out ScanRun second));

            Assert.NotSame(first.Session, second.Session);
            Assert.False(second.Session.IsCancellationRequested);
            owner.Complete(second);
        }

        [Fact]
        public void CancellationTargetsOnlyTheOwnersCurrentRun()
        {
            var firstOwner = new ScanRunOwner();
            var secondOwner = new ScanRunOwner();
            var reporter = new ScanViewModel();
            firstOwner.TryCreate(reporter, out ScanRun first);
            secondOwner.TryCreate(reporter, out ScanRun second);

            Assert.True(firstOwner.RequestCancellation());

            Assert.True(first.Session.IsCancellationRequested);
            Assert.False(second.Session.IsCancellationRequested);
            firstOwner.Complete(first);
            secondOwner.Complete(second);
        }

        [Fact]
        public void CompletionClearsAndDisposesRun()
        {
            var owner = new ScanRunOwner();
            owner.TryCreate(new ScanViewModel(), out ScanRun run);

            Assert.True(owner.Complete(run));

            Assert.True(run.IsDisposed);
            Assert.False(owner.HasActiveRun);
            Assert.False(owner.RequestCancellation());
        }

        [Fact]
        public void FailureCleanupAllowsFreshRun()
        {
            var owner = new ScanRunOwner();
            var reporter = new ScanViewModel();
            owner.TryCreate(reporter, out ScanRun failedRun);

            try
            {
                throw new InvalidOperationException("simulated scan failure");
            }
            catch (InvalidOperationException)
            {
                // MainForm's worker-thread finally follows this same cleanup path.
            }
            finally
            {
                owner.Complete(failedRun);
            }

            Assert.False(owner.HasActiveRun);
            Assert.True(failedRun.IsDisposed);
            Assert.True(owner.TryCreate(reporter, out ScanRun nextRun));
            owner.Complete(nextRun);
        }

        [Fact]
        public void RepeatedCancellationIsSafeAndIdempotent()
        {
            var owner = new ScanRunOwner();
            owner.TryCreate(new ScanViewModel(), out ScanRun run);

            Assert.True(owner.RequestCancellation());
            Assert.True(owner.RequestCancellation());
            Assert.True(run.Session.IsCancellationRequested);

            owner.Complete(run);
        }
    }
}
