using System;
using System.Collections.Generic;
using InventoryKamera.game;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ScanSetupTests
    {
        [Fact]
        public void DraftLoadsExistingNamesAndControllerConfiguration()
        {
            var settings = Properties.Settings.Default;
            var original = CaptureSettings(settings);
            try
            {
                settings.TravelerName = "Traveler Custom";
                settings.WandererName = "Wanderer Custom";
                settings.Manequin1Name = "Manequin F";
                settings.Manequin2Name = "Manequin M";
                settings.UseDefaultControllerConfirmLayout = false;
                settings.ControllerConfirmLayout = (int)ControllerConfirmLayout.AConfirm_BCancel;

                ScanSetupDraft draft = ScanSetupDraft.Load(settings);

                Assert.Equal("Traveler Custom", draft.TravelerName);
                Assert.Equal("Wanderer Custom", draft.WandererName);
                Assert.Equal("Manequin F", draft.Manequin1Name);
                Assert.Equal("Manequin M", draft.Manequin2Name);
                Assert.False(draft.UseDefaultControllerLayout);
                Assert.Equal(ControllerConfirmLayout.AConfirm_BCancel, draft.CustomControllerLayout);
            }
            finally
            {
                RestoreSettings(settings, original);
            }
        }

        [Fact]
        public void DefaultSelectionDisablesSelectorAndUsesBConfirmACancel()
        {
            ScanSetupController controller = CreateController(
                ViGEmBusInstallationStatus.Installed,
                useDefault: false,
                layout: ControllerConfirmLayout.AConfirm_BCancel);

            controller.SetUseDefaultControllerLayout(true);

            Assert.False(controller.IsLayoutSelectorEnabled);
            Assert.Equal(ControllerConfirmLayout.BConfirm_ACancel,
                controller.Draft.EffectiveControllerLayout);
        }

        [Fact]
        public void CustomSelectionEnablesSelectorAndUsesAlternateLayout()
        {
            ScanSetupController controller = CreateController(
                ViGEmBusInstallationStatus.Installed,
                useDefault: true,
                layout: ControllerConfirmLayout.BConfirm_ACancel);

            controller.SetUseDefaultControllerLayout(false);
            controller.Draft.CustomControllerLayout = ControllerConfirmLayout.AConfirm_BCancel;

            Assert.True(controller.IsLayoutSelectorEnabled);
            Assert.Equal(ControllerConfirmLayout.AConfirm_BCancel,
                controller.Draft.EffectiveControllerLayout);
        }

        [Fact]
        public void StartSavePersistsNamesAndLayoutToSharedApplicationSettings()
        {
            var settings = Properties.Settings.Default;
            var original = CaptureSettings(settings);
            try
            {
                var draft = new ScanSetupDraft
                {
                    TravelerName = "Saved Traveler",
                    WandererName = "Saved Wanderer",
                    Manequin1Name = "Saved F",
                    Manequin2Name = "Saved M",
                    UseDefaultControllerLayout = false,
                    CustomControllerLayout = ControllerConfirmLayout.AConfirm_BCancel
                };

                draft.Save(settings);

                Assert.Equal("Saved Traveler", settings.TravelerName);
                Assert.Equal("Saved Wanderer", settings.WandererName);
                Assert.Equal("Saved F", settings.Manequin1Name);
                Assert.Equal("Saved M", settings.Manequin2Name);
                Assert.False(settings.UseDefaultControllerConfirmLayout);
                Assert.Equal((int)ControllerConfirmLayout.AConfirm_BCancel,
                    settings.ControllerConfirmLayout);
            }
            finally
            {
                RestoreSettings(settings, original);
            }
        }

        [Fact]
        public void CancelStyleDraftEditsDoNotMutateApplicationSettings()
        {
            var settings = Properties.Settings.Default;
            SettingsValues original = CaptureSettings(settings);
            ScanSetupDraft draft = ScanSetupDraft.Load(settings);

            draft.TravelerName = "Unsaved Traveler";
            draft.WandererName = "Unsaved Wanderer";
            draft.UseDefaultControllerLayout = !draft.UseDefaultControllerLayout;
            draft.CustomControllerLayout = ControllerConfirmLayout.AConfirm_BCancel;

            AssertSettingsEqual(original, settings);
        }

        [Theory]
        [InlineData(true, (int)ViGEmBusInstallationStatus.Installed)]
        [InlineData(false, (int)ViGEmBusInstallationStatus.NotInstalled)]
        public void DriverServiceRegistrationMapsToExpectedStatus(
            bool serviceExists,
            int expected)
        {
            var detector = new ViGEmBusDetector(() => serviceExists);

            Assert.Equal((ViGEmBusInstallationStatus)expected, detector.Detect());
        }

        [Fact]
        public void DriverDetectionFailureReturnsDetectionFailed()
        {
            var detector = new ViGEmBusDetector(
                () => throw new InvalidOperationException("registry unavailable"));

            Assert.Equal(ViGEmBusInstallationStatus.DetectionFailed, detector.Detect());
        }

        [Theory]
        [InlineData((int)ViGEmBusInstallationStatus.Installed, true, false)]
        [InlineData((int)ViGEmBusInstallationStatus.NotInstalled, false, true)]
        [InlineData((int)ViGEmBusInstallationStatus.DetectionFailed, false, false)]
        public void StatusControlsStartAndDownloadPolicy(
            int statusValue,
            bool canStart,
            bool canDownload)
        {
            var status = (ViGEmBusInstallationStatus)statusValue;
            ScanSetupController controller = CreateController(status);

            Assert.Equal(status, controller.ViGEmBusStatus);
            Assert.Equal(canStart, controller.CanStartScan);
            Assert.Equal(canDownload, controller.CanDownloadViGEmBus);
            Assert.False(string.IsNullOrWhiteSpace(controller.ViGEmBusStatusHeading));
            Assert.False(string.IsNullOrWhiteSpace(controller.ViGEmBusStatusDescription));
        }

        [Fact]
        public void CheckAgainUpdatesNotInstalledToInstalled()
        {
            var detector = new SequenceDetector(
                ViGEmBusInstallationStatus.NotInstalled,
                ViGEmBusInstallationStatus.Installed);
            var controller = new ScanSetupController(
                CreateDraft(), detector, new RecordingUrlLauncher());

            Assert.False(controller.CanStartScan);
            Assert.True(controller.CanDownloadViGEmBus);

            controller.RefreshViGEmBusStatus();

            Assert.True(controller.CanStartScan);
            Assert.False(controller.CanDownloadViGEmBus);
            Assert.Equal(2, detector.CallCount);
        }

        [Fact]
        public void DownloadUsesOfficialViGEmBusReleasesUrlWithoutStartingScan()
        {
            var launcher = new RecordingUrlLauncher();
            var controller = new ScanSetupController(
                CreateDraft(),
                new SequenceDetector(ViGEmBusInstallationStatus.NotInstalled),
                launcher);

            bool opened = controller.TryOpenViGEmBusDownload(out string error);

            Assert.True(opened);
            Assert.Null(error);
            Assert.Equal(ScanSetupController.ViGEmBusReleasesUrl, launcher.LastUrl);
            Assert.False(controller.CanStartScan);
        }

        [Fact]
        public void DownloadLaunchFailureIsReturnedForUserFacingHandling()
        {
            var launcher = new RecordingUrlLauncher { Exception = new InvalidOperationException("no browser") };
            var controller = new ScanSetupController(
                CreateDraft(),
                new SequenceDetector(ViGEmBusInstallationStatus.NotInstalled),
                launcher);

            bool opened = controller.TryOpenViGEmBusDownload(out string error);

            Assert.False(opened);
            Assert.Equal("no browser", error);
            Assert.False(controller.CanStartScan);
        }

        private static ScanSetupController CreateController(
            ViGEmBusInstallationStatus status,
            bool useDefault = true,
            ControllerConfirmLayout layout = ControllerConfirmLayout.BConfirm_ACancel) =>
            new ScanSetupController(
                CreateDraft(useDefault, layout),
                new SequenceDetector(status),
                new RecordingUrlLauncher());

        private static ScanSetupDraft CreateDraft(
            bool useDefault = true,
            ControllerConfirmLayout layout = ControllerConfirmLayout.BConfirm_ACancel) =>
            new ScanSetupDraft
            {
                TravelerName = string.Empty,
                WandererName = "Wanderer",
                Manequin1Name = "Manequin1",
                Manequin2Name = "Manequin2",
                UseDefaultControllerLayout = useDefault,
                CustomControllerLayout = layout
            };

        private static SettingsValues CaptureSettings(Properties.Settings settings) => new SettingsValues
        {
            TravelerName = settings.TravelerName,
            WandererName = settings.WandererName,
            Manequin1Name = settings.Manequin1Name,
            Manequin2Name = settings.Manequin2Name,
            UseDefaultControllerLayout = settings.UseDefaultControllerConfirmLayout,
            ControllerLayout = settings.ControllerConfirmLayout
        };

        private static void RestoreSettings(Properties.Settings settings, SettingsValues values)
        {
            settings.TravelerName = values.TravelerName;
            settings.WandererName = values.WandererName;
            settings.Manequin1Name = values.Manequin1Name;
            settings.Manequin2Name = values.Manequin2Name;
            settings.UseDefaultControllerConfirmLayout = values.UseDefaultControllerLayout;
            settings.ControllerConfirmLayout = values.ControllerLayout;
            settings.Save();
        }

        private static void AssertSettingsEqual(SettingsValues expected, Properties.Settings actual)
        {
            Assert.Equal(expected.TravelerName, actual.TravelerName);
            Assert.Equal(expected.WandererName, actual.WandererName);
            Assert.Equal(expected.Manequin1Name, actual.Manequin1Name);
            Assert.Equal(expected.Manequin2Name, actual.Manequin2Name);
            Assert.Equal(expected.UseDefaultControllerLayout, actual.UseDefaultControllerConfirmLayout);
            Assert.Equal(expected.ControllerLayout, actual.ControllerConfirmLayout);
        }

        private sealed class SettingsValues
        {
            internal string TravelerName { get; init; }
            internal string WandererName { get; init; }
            internal string Manequin1Name { get; init; }
            internal string Manequin2Name { get; init; }
            internal bool UseDefaultControllerLayout { get; init; }
            internal int ControllerLayout { get; init; }
        }

        private sealed class SequenceDetector : IViGEmBusDetector
        {
            private readonly Queue<ViGEmBusInstallationStatus> statuses;
            private ViGEmBusInstallationStatus lastStatus;

            internal SequenceDetector(params ViGEmBusInstallationStatus[] statuses)
            {
                this.statuses = new Queue<ViGEmBusInstallationStatus>(statuses);
                if (this.statuses.Count == 0)
                    throw new ArgumentException("At least one status is required.", nameof(statuses));
            }

            internal int CallCount { get; private set; }

            public ViGEmBusInstallationStatus Detect()
            {
                CallCount++;
                if (statuses.Count > 0) lastStatus = statuses.Dequeue();
                return lastStatus;
            }
        }

        private sealed class RecordingUrlLauncher : IExternalUrlLauncher
        {
            internal string LastUrl { get; private set; }
            internal Exception Exception { get; set; }

            public void Open(string url)
            {
                LastUrl = url;
                if (Exception != null) throw Exception;
            }
        }
    }
}
