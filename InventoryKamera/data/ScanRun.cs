using System;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// Owns the objects whose lifetime is exactly one active scan. The scan still runs on the same
    /// dedicated background <see cref="Thread"/> used by MainForm; this type only makes that ownership
    /// explicit and keeps cancellation routed through the matching <see cref="ScanSession"/>.
    /// </summary>
    internal sealed class ScanRun : IDisposable
    {
        private readonly IScanProgressReporter progressReporter;
        private readonly object scannerGate = new object();
        private int started;
        private int disposed;

        internal ScanSession Session { get; }
        internal GameScanner Scanner { get; private set; }
        internal Thread WorkerThread { get; private set; }
        internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

        internal ScanRun(IScanProgressReporter progressReporter)
        {
            this.progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
            Session = new ScanSession();
        }

        internal GameScanner InitializeScanner()
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(ScanRun));
            lock (scannerGate)
            {
                if (Scanner == null) Scanner = new GameScanner(progressReporter, Session);
                return Scanner;
            }
        }

        internal void Start(ThreadStart work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(ScanRun));
            if (Interlocked.Exchange(ref started, 1) != 0)
                throw new InvalidOperationException("This scan run has already been started.");

            WorkerThread = new Thread(work)
            {
                IsBackground = true
            };
            WorkerThread.Start();
        }

        internal void RequestCancellation() => Session.RequestCancellation();

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Session.Dispose();
        }
    }

    /// <summary>
    /// Thread-safe single slot for MainForm's current scan. Clearing is identity-checked so a late
    /// completion callback from an older run can never dispose or clear a newer run.
    /// </summary>
    internal sealed class ScanRunOwner
    {
        private readonly object gate = new object();
        private ScanRun activeRun;

        internal bool HasActiveRun
        {
            get
            {
                lock (gate) return activeRun != null;
            }
        }

        internal bool TryCreate(IScanProgressReporter progressReporter, out ScanRun run)
        {
            lock (gate)
            {
                if (activeRun != null)
                {
                    run = null;
                    return false;
                }

                run = new ScanRun(progressReporter);
                activeRun = run;
                return true;
            }
        }

        internal bool RequestCancellation()
        {
            ScanRun run;
            lock (gate) run = activeRun;
            if (run == null) return false;

            run.RequestCancellation();
            return true;
        }

        internal bool Complete(ScanRun run)
        {
            if (run == null) return false;

            lock (gate)
            {
                if (!ReferenceEquals(activeRun, run)) return false;
                activeRun = null;
            }

            run.Dispose();
            return true;
        }
    }
}
