using InventoryKamera.game;
using System;
using System.Drawing;

namespace InventoryKamera
{
    /// <summary>
    /// Persists the fresh frames used by a failed destination verification. Paths are unique per
    /// failure so a later scan does not overwrite the evidence.
    /// </summary>
    internal static class DestinationVerificationDiagnostics
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        internal static void Save(
            FailureDiagnosticStore diagnosticStore,
            PaimonMenuNavigationResult result,
            string targetName,
            Func<Bitmap, Bitmap> copyDetectorCrop)
        {
            if (diagnosticStore == null) throw new ArgumentNullException(nameof(diagnosticStore));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (copyDetectorCrop == null) throw new ArgumentNullException(nameof(copyDetectorCrop));
            if (result.DiagnosticScreenshots.Count == 0) return;

            try
            {
                FailureDiagnosticScope failure = diagnosticStore.CreateFailure(
                    "destination_verification",
                    targetName + "_entry");

                for (int index = 0; index < result.DiagnosticScreenshots.Count; index++)
                {
                    Bitmap screenshot = result.DiagnosticScreenshots[index];
                    int attempt = index + 1;
                    using Bitmap crop = copyDetectorCrop(screenshot);
                    failure.SaveBitmap($"attempt_{attempt:00}_crop.png", crop);
                }

                // One final context frame is sufficient; retaining every full-window retry duplicated
                // private screen content without adding evidence beyond the attempt crops.
                failure.SaveBitmap("failure_full.png", result.DiagnosticScreenshots[result.DiagnosticScreenshots.Count - 1]);

                Logger.Info(
                    "Saved {0} {1} destination-verification diagnostic attempt(s) to {2}.",
                    result.DiagnosticScreenshots.Count,
                    targetName,
                    failure.DirectoryPath);
            }
            catch (Exception ex)
            {
                // Diagnostics must never turn an already-safe verification failure into a scanner
                // orchestration failure. The original phase still backs out and remains skipped.
                Logger.Warn(ex, "Could not save {0} destination-verification diagnostics.", targetName);
            }
        }
    }
}
