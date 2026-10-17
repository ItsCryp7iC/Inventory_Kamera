namespace InventoryKamera
{
    /// <summary>
    /// Separates completed scan-data handling from input-mode cleanup. A pending Recovery owns the
    /// overlay and temporary controller, but does not invalidate or suppress successfully collected
    /// data.
    /// </summary>
    internal sealed class ScanCompletionPlan
    {
        internal ScanCompletionPlan(
            bool exportGood,
            bool showCompletedOverlay,
            bool openOptimizer,
            string status,
            bool statusOk,
            string completionMessage)
        {
            ExportGood = exportGood;
            ShowCompletedOverlay = showCompletedOverlay;
            OpenOptimizer = openOptimizer;
            Status = status;
            StatusOk = statusOk;
            CompletionMessage = completionMessage;
        }

        internal bool ExportGood { get; }
        internal bool ShowCompletedOverlay { get; }
        internal bool OpenOptimizer { get; }
        internal string Status { get; }
        internal bool StatusOk { get; }
        internal string CompletionMessage { get; }
    }

    internal static class ScanCompletionPolicy
    {
        internal static ScanCompletionPlan Evaluate(bool cancelled, bool recoveryPending)
        {
            if (recoveryPending)
            {
                return new ScanCompletionPlan(
                    exportGood: !cancelled,
                    showCompletedOverlay: false,
                    openOptimizer: false,
                    status: "Keyboard/Mouse restoration required",
                    statusOk: false,
                    completionMessage: null);
            }

            if (cancelled)
            {
                return new ScanCompletionPlan(
                    exportGood: false,
                    showCompletedOverlay: true,
                    openOptimizer: false,
                    status: "Scan stopped",
                    statusOk: true,
                    completionMessage: "Scan cancelled. Input cleanup is complete.");
            }

            return new ScanCompletionPlan(
                exportGood: true,
                showCompletedOverlay: true,
                openOptimizer: true,
                status: "Finished",
                statusOk: true,
                completionMessage: "Scan finished. Input cleanup is complete.");
        }
    }
}
