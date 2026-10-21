using System.Linq;
using System.Collections.Generic;
using Xunit;
using System.Windows.Forms;

namespace InventoryKamera.Tests
{
    public sealed class AchievementScanUiPolicyTests
    {
        [Fact]
        public void CancellationHotkeys_KeepLegacyEnterAndReserveF8ForAchievements()
        {
            Assert.Equal(Keys.Enter, ScanCancellationHotkeyPolicy.KeyFor(
                ScanCancellationHotkeyMode.LegacyInventory));
            Assert.Equal(Keys.F8, ScanCancellationHotkeyPolicy.KeyFor(
                ScanCancellationHotkeyMode.Achievement));
            Assert.NotEqual(
                Keys.Enter,
                ScanCancellationHotkeyPolicy.KeyFor(ScanCancellationHotkeyMode.Achievement));
            Assert.True(ScanCancellationHotkeyPolicy.IsCancellationKey(
                ScanCancellationHotkeyMode.LegacyInventory, Keys.Enter));
            Assert.False(ScanCancellationHotkeyPolicy.IsCancellationKey(
                ScanCancellationHotkeyMode.Achievement, Keys.Enter));
            Assert.True(ScanCancellationHotkeyPolicy.IsCancellationKey(
                ScanCancellationHotkeyMode.Achievement, Keys.F8));
            Assert.Equal("Stop", ScanCancellationHotkeyPolicy.RegistrationName);
        }

        [Fact]
        public void SelectingAchievementsClearsEveryLegacyCategory()
        {
            AchievementScanSelection selection =
                AchievementScanSelectionPolicy.SelectAchievements(true);

            Assert.True(selection.Achievements);
            Assert.False(selection.HasLegacySelection);
            Assert.False(selection.LegacyChoicesEnabled);
            Assert.True(selection.AchievementChoiceEnabled);
            Assert.True(selection.AchievementImportEnabled);
            Assert.True(AchievementScanSelectionPolicy.IsValid(selection));
        }

        [Fact]
        public void SelectingLegacyCategoriesClearsAchievementsAndPreservesLegacyCombination()
        {
            AchievementScanSelection selection = AchievementScanSelectionPolicy.SelectLegacy(
                weapons: true,
                artifacts: true,
                characters: false,
                characterDevelopmentItems: true,
                materials: false);

            Assert.False(selection.Achievements);
            Assert.True(selection.Weapons);
            Assert.True(selection.Artifacts);
            Assert.True(selection.CharacterDevelopmentItems);
            Assert.True(selection.LegacyChoicesEnabled);
            Assert.False(selection.AchievementChoiceEnabled);
            Assert.False(selection.AchievementImportEnabled);
            Assert.True(AchievementScanSelectionPolicy.IsValid(selection));
        }

        [Fact]
        public void ClearingLastLegacyCategoryRestoresBothFamilies()
        {
            AchievementScanSelection selection = AchievementScanSelectionPolicy.SelectLegacy(
                weapons: false,
                artifacts: false,
                characters: false,
                characterDevelopmentItems: false,
                materials: false);

            Assert.False(selection.Achievements);
            Assert.False(selection.HasLegacySelection);
            Assert.True(selection.LegacyChoicesEnabled);
            Assert.True(selection.AchievementChoiceEnabled);
            Assert.False(selection.AchievementImportEnabled);
        }

        [Fact]
        public void MixedAchievementAndLegacySelectionIsRejectedByPurePolicy()
        {
            var mixed = new AchievementScanSelection(
                achievements: true,
                weapons: true,
                artifacts: false,
                characters: false,
                characterDevelopmentItems: false,
                materials: false);

            Assert.False(AchievementScanSelectionPolicy.IsValid(mixed));
        }

        [Fact]
        public void Warning_IsShownOnlyWhenAchievementsAreSelected()
        {
            Assert.True(AchievementScanStartPolicy.ShouldShowAchievementWarning(true));
            Assert.False(AchievementScanStartPolicy.ShouldShowAchievementWarning(false));
        }

        [Fact]
        public void AchievementOnlySelection_IsAllowedAndDetected()
        {
            Assert.True(AchievementScanStartPolicy.IsAchievementOnlySelection(
                achievements: true,
                weapons: false,
                artifacts: false,
                characters: false,
                characterDevelopmentItems: false,
                materials: false));
            Assert.False(AchievementScanStartPolicy.IsAchievementOnlySelection(
                achievements: false,
                weapons: false,
                artifacts: false,
                characters: false,
                characterDevelopmentItems: false,
                materials: false));
        }

        [Fact]
        public void CancelledAchievementWarning_PreventsActivationAndScanStart()
        {
            var events = new List<string>();

            bool proceed = ControllerScanPreflight.TryProceed(
                controllerScanSelected: true,
                confirmRequirements: () =>
                {
                    events.Add("warning");
                    return false;
                },
                validateAndActivateGame: () =>
                {
                    events.Add("activate");
                    return true;
                });

            Assert.False(proceed);
            Assert.Equal(new[] { "warning" }, events);
        }

        [Fact]
        public void ValidGoodAndNativeImports_CreateResumeSeed()
        {
            var provider = new AchievementCatalogProvider();
            int[] ids = provider.Catalog.All.Take(2).Select(entry => entry.Id).ToArray();
            var exporter = new AchievementToolkitExporter(provider);
            var state = new AchievementResumeDataState(new AchievementToolkitImporter(provider));

            AchievementImportResult good = state.Import(exporter.ExportGood(ids).Json, "good.json");
            Assert.True(good.Success);
            Assert.Equal(ids.OrderBy(id => id), state.ResumeSeed.TrustedCompletedIds);

            AchievementImportResult native = state.Import(exporter.ExportNative(ids).Json, "native.json");
            Assert.True(native.Success);
            Assert.Equal(ids.OrderBy(id => id), state.ResumeSeed.TrustedCompletedIds);
        }

        [Fact]
        public void MalformedOrUnknownImport_BlocksStartUntilCleared()
        {
            var provider = new AchievementCatalogProvider();
            var state = new AchievementResumeDataState(new AchievementToolkitImporter(provider));

            AchievementImportResult malformed = state.Import("{", "bad.json");
            Assert.False(malformed.Success);
            Assert.True(state.HasBlockingError);
            Assert.Null(state.ResumeSeed);

            AchievementImportResult unknown = state.Import(
                "{\"achievements\":[2147483647]}",
                "unknown.json");
            Assert.False(unknown.Success);
            Assert.True(state.HasBlockingError);
            Assert.Null(state.ResumeSeed);

            state.Clear();
            Assert.False(state.HasBlockingError);
            Assert.False(state.HasResumeSeed);
        }
    }
}
