using System;
using System.Diagnostics;
using InventoryKamera.game;

namespace InventoryKamera
{
    internal interface IExternalUrlLauncher
    {
        void Open(string url);
    }

    internal sealed class ShellExternalUrlLauncher : IExternalUrlLauncher
    {
        public void Open(string url)
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    internal sealed class ScanSetupDraft
    {
        internal string TravelerName { get; set; }
        internal string WandererName { get; set; }
        internal string Manequin1Name { get; set; }
        internal string Manequin2Name { get; set; }
        internal bool UseDefaultControllerLayout { get; set; }
        internal ControllerConfirmLayout CustomControllerLayout { get; set; }
        internal DeclaredInputMode DeclaredOriginalInputMode { get; set; }

        internal ControllerConfirmLayout EffectiveControllerLayout =>
            UseDefaultControllerLayout
                ? ControllerConfirmLayoutOptions.Default
                : CustomControllerLayout;

        internal static ScanSetupDraft Load(Properties.Settings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            return new ScanSetupDraft
            {
                TravelerName = settings.TravelerName,
                WandererName = settings.WandererName,
                Manequin1Name = settings.Manequin1Name,
                Manequin2Name = settings.Manequin2Name,
                UseDefaultControllerLayout = settings.UseDefaultControllerConfirmLayout,
                CustomControllerLayout = ControllerConfirmLayoutOptions.FromPersistedValue(
                    settings.ControllerConfirmLayout),
                DeclaredOriginalInputMode = DeclaredInputModeOptions.FromPersistedValue(
                    settings.DeclaredOriginalInputMode)
            };
        }

        internal void Save(Properties.Settings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings.TravelerName = TravelerName ?? string.Empty;
            settings.WandererName = WandererName ?? string.Empty;
            settings.Manequin1Name = Manequin1Name ?? string.Empty;
            settings.Manequin2Name = Manequin2Name ?? string.Empty;
            settings.UseDefaultControllerConfirmLayout = UseDefaultControllerLayout;
            settings.ControllerConfirmLayout = (int)EffectiveControllerLayout;
            settings.DeclaredOriginalInputMode = (int)DeclaredOriginalInputMode;
            settings.Save();
        }
    }

    /// <summary>
    /// Testable state and actions for the Scan Setup dialog. It does not activate Genshin, create a
    /// scan run, or instantiate ViGEm; those remain downstream of an accepted dialog result.
    /// </summary>
    internal sealed class ScanSetupController
    {
        internal const string ViGEmBusReleasesUrl = "https://github.com/nefarius/ViGEmBus/releases";
        private readonly IViGEmBusDetector detector;
        private readonly IExternalUrlLauncher urlLauncher;

        internal ScanSetupController(
            ScanSetupDraft draft,
            IViGEmBusDetector detector,
            IExternalUrlLauncher urlLauncher)
        {
            Draft = draft ?? throw new ArgumentNullException(nameof(draft));
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.urlLauncher = urlLauncher ?? throw new ArgumentNullException(nameof(urlLauncher));
            RefreshViGEmBusStatus();
        }

        internal ScanSetupDraft Draft { get; }
        internal ViGEmBusInstallationStatus ViGEmBusStatus { get; private set; }
        internal bool CanStartScan => ViGEmBusStatus == ViGEmBusInstallationStatus.Installed;
        internal bool CanDownloadViGEmBus => ViGEmBusStatus == ViGEmBusInstallationStatus.NotInstalled;
        internal bool IsLayoutSelectorEnabled => !Draft.UseDefaultControllerLayout;

        internal string ViGEmBusStatusHeading => ViGEmBusStatus switch
        {
            ViGEmBusInstallationStatus.Installed => "✓ ViGEmBus installed",
            ViGEmBusInstallationStatus.NotInstalled => "✕ ViGEmBus not installed",
            _ => "⚠ Could not determine ViGEmBus status"
        };

        internal string ViGEmBusStatusDescription => ViGEmBusStatus switch
        {
            ViGEmBusInstallationStatus.Installed =>
                "Ready for controller automation. A physical controller is not required.",
            ViGEmBusInstallationStatus.NotInstalled =>
                "Inventory Kamera requires ViGEmBus for automated scanning; a physical controller " +
                "does not replace it.",
            _ => "Check Again before starting the scan."
        };

        internal void SetUseDefaultControllerLayout(bool useDefault)
        {
            Draft.UseDefaultControllerLayout = useDefault;
            if (useDefault)
                Draft.CustomControllerLayout = ControllerConfirmLayoutOptions.Default;
        }

        internal void RefreshViGEmBusStatus()
        {
            ViGEmBusStatus = detector.Detect();
        }

        internal bool TryOpenViGEmBusDownload(out string error)
        {
            try
            {
                urlLauncher.Open(ViGEmBusReleasesUrl);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
