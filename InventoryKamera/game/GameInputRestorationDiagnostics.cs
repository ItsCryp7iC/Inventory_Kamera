using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace InventoryKamera.game
{
    internal sealed class GameForegroundStatus
    {
        internal GameForegroundStatus(
            IntPtr foregroundHandle,
            int foregroundProcessId,
            string foregroundProcessName,
            IntPtr expectedGameHandle,
            int expectedGameProcessId,
            string expectedGameProcessName,
            bool matches)
        {
            ForegroundHandle = foregroundHandle;
            ForegroundProcessId = foregroundProcessId;
            ForegroundProcessName = foregroundProcessName ?? "unknown";
            ExpectedGameHandle = expectedGameHandle;
            ExpectedGameProcessId = expectedGameProcessId;
            ExpectedGameProcessName = expectedGameProcessName ?? "not_found";
            Matches = matches;
        }

        internal IntPtr ForegroundHandle { get; }
        internal int ForegroundProcessId { get; }
        internal string ForegroundProcessName { get; }
        internal IntPtr ExpectedGameHandle { get; }
        internal int ExpectedGameProcessId { get; }
        internal string ExpectedGameProcessName { get; }
        internal bool Matches { get; }

        public override string ToString() =>
            $"foregroundProcess={ForegroundProcessName}; foregroundPid={ForegroundProcessId}; " +
            $"foregroundHwnd=0x{ForegroundHandle.ToInt64():X}; " +
            $"expectedProcess={ExpectedGameProcessName}; expectedPid={ExpectedGameProcessId}; " +
            $"expectedHwnd=0x{ExpectedGameHandle.ToInt64():X}; foregroundMatchesGame={Matches}";
    }

    internal interface IGameForegroundProbe
    {
        GameForegroundStatus Capture();
    }

    /// <summary>
    /// Reports process IDs and window handles only. Window titles are deliberately excluded because
    /// they can contain unrelated private text.
    /// </summary>
    internal sealed class WindowsGameForegroundProbe : IGameForegroundProbe
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public GameForegroundStatus Capture()
        {
            IntPtr foregroundHandle = GetForegroundWindow();
            GetWindowThreadProcessId(foregroundHandle, out uint foregroundProcessId);
            string foregroundName = ProcessName((int)foregroundProcessId);

            var candidates = new List<(IntPtr Handle, int ProcessId, string Name)>();
            foreach (string executable in Properties.Settings.Default.Executables.Cast<string>())
            {
                foreach (Process process in Process.GetProcessesByName(executable))
                {
                    using (process)
                    {
                        IntPtr handle = process.MainWindowHandle;
                        if (handle != IntPtr.Zero)
                            candidates.Add((handle, process.Id, process.ProcessName));
                    }
                }
            }

            var expected = candidates.FirstOrDefault(candidate => candidate.Handle == foregroundHandle);
            bool matches = expected.Handle != IntPtr.Zero;
            if (!matches && candidates.Count > 0) expected = candidates[0];

            return new GameForegroundStatus(
                foregroundHandle,
                (int)foregroundProcessId,
                foregroundName,
                expected.Handle,
                expected.ProcessId,
                expected.Name,
                matches);
        }

        private static string ProcessName(int processId)
        {
            if (processId <= 0) return "unknown";
            try
            {
                using Process process = Process.GetProcessById(processId);
                return process.ProcessName;
            }
            catch
            {
                return "unknown";
            }
        }
    }

    /// <summary>
    /// Writes a bounded, narrow Controls-page trace for one restoration attempt. It never stores a
    /// full-window screenshot; supplied screenshots remain owned by the caller.
    /// </summary>
    internal sealed class GameInputRestorationDiagnostics
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly NormalizedRectangle ControlsTopRegion =
            new NormalizedRectangle(0.235, 0.115, 0.68, 0.26);
        private static readonly NormalizedRectangle ControlTypeRowRegion =
            new NormalizedRectangle(0.235, 0.155, 0.68, 0.08);
        private static readonly NormalizedRectangle DropdownRegion =
            new NormalizedRectangle(0.695, 0.195, 0.255, 0.15);

        private readonly FailureDiagnosticStore store;
        private readonly IGameForegroundProbe foregroundProbe;
        private readonly List<string> traceLines = new List<string>();
        private FailureDiagnosticScope scope;
        private Stopwatch stopwatch;

        internal GameInputRestorationDiagnostics(
            FailureDiagnosticStore store = null,
            IGameForegroundProbe foregroundProbe = null)
        {
            this.store = store;
            this.foregroundProbe = foregroundProbe;
        }

        internal long ElapsedMilliseconds => stopwatch?.ElapsedMilliseconds ?? 0;

        internal void BeginAttempt()
        {
            traceLines.Clear();
            stopwatch = Stopwatch.StartNew();
            scope = null;
            if (store != null)
            {
                try
                {
                    scope = store.CreateFailure("controller_restoration_trace", "control_type");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Could not create controller-restoration diagnostic scope.");
                }
            }

            Record("Begin", "restorationAttemptStarted=true");
        }

        internal void Record(string step, string details)
        {
            string line =
                $"{DateTimeOffset.Now:O} elapsedMs={ElapsedMilliseconds}; RestoreStep={step}; {details}";
            Logger.Info(line);
            traceLines.Add(line);
            PersistTrace();
        }

        internal void RecordActionBefore(
            string step,
            string action,
            int attempt,
            bool controllerAvailable)
        {
            Record(
                step,
                $"actionAboutToSend={action}; attempt={attempt}; " +
                $"controllerAvailable={controllerAvailable}; {CaptureForeground()}");
        }

        internal void RecordActionAfter(
            string step,
            string action,
            int attempt,
            bool controllerAvailable,
            long actionStartedAtMs)
        {
            Record(
                step,
                $"actionActuallySent={action}; attempt={attempt}; actionCompletedWithoutException=true; " +
                $"buttonOrStickReleaseCompleted=true; actionElapsedMs={ElapsedMilliseconds - actionStartedAtMs}; " +
                $"controllerAvailable={controllerAvailable}; {CaptureForeground()}");
        }

        internal void RecordActionFailure(
            string step,
            string action,
            int attempt,
            Exception exception,
            long actionStartedAtMs)
        {
            Record(
                step,
                $"actionFailed={action}; attempt={attempt}; actionElapsedMs={ElapsedMilliseconds - actionStartedAtMs}; " +
                $"exception={exception.GetType().Name}: {exception.Message}; {CaptureForeground()}");
        }

        internal void SaveControlTypeFrame(
            string stage,
            Bitmap screenshot,
            ControlTypeInteractionDetection detection)
        {
            if (scope == null || screenshot == null ||
                detection?.Page.Screen != GenshinInputScreen.Controls)
            {
                return;
            }

            string safeStage = Regex.Replace(stage ?? "state", @"[^a-zA-Z0-9_-]", "_");
            try
            {
                using Bitmap controlsTop = GenshinInputModeDetector.CopyRegion(screenshot, ControlsTopRegion);
                using Bitmap controlTypeRow = GenshinInputModeDetector.CopyRegion(screenshot, ControlTypeRowRegion);
                using Bitmap dropdown = GenshinInputModeDetector.CopyRegion(screenshot, DropdownRegion);
                scope.SaveBitmap($"{safeStage}_controls_top.png", controlsTop);
                scope.SaveBitmap($"{safeStage}_control_type_row.png", controlTypeRow);
                scope.SaveBitmap($"{safeStage}_dropdown.png", dropdown);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not save sanitized Control Type diagnostics for {0}.", stage);
            }
        }

        private string CaptureForeground()
        {
            if (foregroundProbe == null) return "foregroundProbe=unavailable";
            try
            {
                return foregroundProbe.Capture().ToString();
            }
            catch (Exception ex)
            {
                return $"foregroundProbeError={ex.GetType().Name}: {ex.Message}";
            }
        }

        private void PersistTrace()
        {
            if (scope == null) return;
            try
            {
                scope.SaveText("trace.txt", string.Join(Environment.NewLine, traceLines));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not persist controller-restoration trace.");
            }
        }
    }
}
