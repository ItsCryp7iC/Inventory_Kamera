using InventoryKamera.game;
using System;
using System.Drawing;
using System.IO;

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
            PaimonMenuNavigationResult result,
            string targetName,
            Func<Bitmap, Bitmap> copyDetectorCrop)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (copyDetectorCrop == null) throw new ArgumentNullException(nameof(copyDetectorCrop));
            if (result.DiagnosticScreenshots.Count == 0) return;

            try
            {
                string runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 8);
                string directory = Path.Combine(
                    "./logging",
                    "paimonmenu",
                    $"{targetName}_entry_failure_{runId}");
                Directory.CreateDirectory(directory);

                for (int index = 0; index < result.DiagnosticScreenshots.Count; index++)
                {
                    Bitmap screenshot = result.DiagnosticScreenshots[index];
                    int attempt = index + 1;
                    screenshot.Save(Path.Combine(directory, $"attempt_{attempt:00}_full.png"));
                    using Bitmap crop = copyDetectorCrop(screenshot);
                    crop.Save(Path.Combine(directory, $"attempt_{attempt:00}_crop.png"));
                }

                Logger.Info(
                    "Saved {0} {1} destination-verification diagnostic attempt(s) to {2}.",
                    result.DiagnosticScreenshots.Count,
                    targetName,
                    directory);
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
