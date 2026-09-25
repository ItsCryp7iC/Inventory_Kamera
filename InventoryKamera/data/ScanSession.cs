using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace InventoryKamera
{
    /// <summary>
    /// Owns cooperative cancellation and OCR-worker coordination for exactly one scan run.
    /// Scraper-local end conditions remain outside this type and are passed to
    /// <see cref="ShouldContinue"/> only when evaluating a particular loop.
    /// </summary>
    internal sealed class ScanSession : IDisposable
    {
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly CancellationTokenSource workerAbortSource = new CancellationTokenSource();
        private readonly Channel<OCRImageCollection> workChannel =
            Channel.CreateUnbounded<OCRImageCollection>();
        private readonly object workerGate = new object();
        private readonly List<Task> workerTasks = new List<Task>();

        private bool workersStarted;
        private int workCompleted;
        private int disposed;

        internal CancellationToken CancellationToken => cancellationSource.Token;
        internal bool IsCancellationRequested => cancellationSource.IsCancellationRequested;
        internal ChannelReader<OCRImageCollection> WorkReader => workChannel.Reader;

        internal void RequestCancellation()
        {
            if (Volatile.Read(ref disposed) != 0) return;
            try
            {
                cancellationSource.Cancel();
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref disposed) != 0)
            {
                // Disposal won a race with a late UI cancellation request.
            }
        }

        internal bool ShouldContinue(bool scraperStopRequested = false)
        {
            return !IsCancellationRequested && !scraperStopRequested;
        }

        internal bool TryQueueWork(OCRImageCollection work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));

            if (Volatile.Read(ref disposed) == 0 && workChannel.Writer.TryWrite(work))
                return true;

            DisposeWork(work);
            return false;
        }

        internal void CompleteWork()
        {
            if (Interlocked.Exchange(ref workCompleted, 1) == 0)
                workChannel.Writer.TryComplete();
        }

        internal void StartWorkers(
            int count,
            Func<CancellationToken, Task> worker)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (worker == null) throw new ArgumentNullException(nameof(worker));

            lock (workerGate)
            {
                if (workersStarted) throw new InvalidOperationException("Scan-session workers have already been started.");
                workersStarted = true;
                for (int i = 0; i < count; i++)
                {
                    CancellationToken abortToken = workerAbortSource.Token;
                    workerTasks.Add(Task.Run(() => worker(abortToken)));
                }
            }
        }

        internal void AwaitWorkers()
        {
            Task[] tasks;
            lock (workerGate)
            {
                tasks = workerTasks.ToArray();
            }

            try
            {
                Task.WaitAll(tasks);
            }
            catch (AggregateException ex) when (
                workerAbortSource.IsCancellationRequested && ContainsOnlyCancellation(ex))
            {
                // An explicit worker abort is a normal emergency-shutdown path.
            }
            finally
            {
                lock (workerGate)
                {
                    workerTasks.Clear();
                }
            }
        }

        internal void AbortWorkersAndWait()
        {
            workerAbortSource.Cancel();
            CompleteWork();
            AwaitWorkers();
            DisposeQueuedWork();
        }

        private void DisposeQueuedWork()
        {
            while (workChannel.Reader.TryRead(out OCRImageCollection work))
                DisposeWork(work);
        }

        private static void DisposeWork(OCRImageCollection work)
        {
            if (work?.Bitmaps == null) return;
            foreach (var bitmap in work.Bitmaps)
                bitmap?.Dispose();
        }

        private static bool ContainsOnlyCancellation(AggregateException exception)
        {
            foreach (Exception inner in exception.Flatten().InnerExceptions)
            {
                if (!(inner is OperationCanceledException)) return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;

            CompleteWork();
            workerAbortSource.Cancel();
            AwaitWorkers();
            DisposeQueuedWork();
            workerAbortSource.Dispose();
            cancellationSource.Dispose();
        }
    }
}
