using InventoryKamera.game;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace InventoryKamera
{

    public enum InventoryPage
    {
        Weapons,
        Artifacts,
        CharacterDevelopmentItems,
        Food,
        Materials,
        Gadget,
        Quest,
        PreciousItems,
        Furnishings,
    }

    public enum Quality
    {
        INVALID,
        ONESTAR,
        TWOSTAR,
        THREESTAR,
        FOURSTAR,
        FIVESTAR
    }

    internal static class InventoryPageExtension
    {
        public static string ToString(this InventoryPage page)
        {
            switch (page)
            {
                case InventoryPage.CharacterDevelopmentItems:
                    return "CharDevItems";
                default:
                    return page.ToString();
            }
        }
    }

    internal class InventoryScraper
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        protected InventoryPage inventoryPage;

        protected bool SortByLevel = false;

        protected int SortByObtained = 0;

        protected readonly List<InventoryPage> materialPages;

        protected readonly IOcrService ocrService;
        protected readonly IImagePreprocessor imagePreprocessor;
        protected readonly IScanSettings scanSettings;
        protected readonly IScanProgressReporter progressReporter;
        protected readonly ScanSession scanSession;
        protected readonly FailureDiagnosticStore diagnosticStore;

        /// <summary>
        /// Shared with every subclass (previously duplicated as a private method on
        /// <c>ArtifactScraper</c> only) so every controller-mode capture region can leave a visual
        /// trail when <see cref="IScanSettings.LogScreenshots"/> is on, not just artifacts.
        /// <paramref name="relativePath"/> may contain '/' to nest into subfolders (e.g.
        /// <c>"weapons/weapon0/name/name"</c>), matching the per-item folder layout
        /// <c>InventoryKamera.ProcessImageCollectionAsync</c> already uses for cataloguing failures --
        /// so scan attempts saved from here land in the same kind of organized tree instead of a flat
        /// dump of files directly under <c>./logging</c>.
        /// </summary>
        protected void SaveDebugScreenshot(Bitmap bitmap, string relativePath, bool force = false)
        {
            if (!force && !scanSettings.LogScreenshots) return;

            string fullPath = Path.Combine("./logging", relativePath + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            bitmap.Save(fullPath);
        }

        protected void SaveFailureScreenshot(Bitmap bitmap, string category, string context, string fileName)
        {
            try
            {
                FailureDiagnosticScope failure = diagnosticStore.CreateFailure(category, context);
                failure.SaveBitmap(fileName, bitmap);
                Logger.Warn("Saved failure diagnostics to {0}.", failure.DirectoryPath);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not save {0} failure diagnostics.", category);
            }
        }

        public InventoryScraper(
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor,
            IScanSettings scanSettings,
            IScanProgressReporter progressReporter,
            ScanSession scanSession,
            FailureDiagnosticStore diagnosticStore = null)
        {
            this.ocrService = ocrService;
            this.imagePreprocessor = imagePreprocessor;
            this.scanSettings = scanSettings;
            this.progressReporter = progressReporter;
            this.scanSession = scanSession ?? throw new ArgumentNullException(nameof(scanSession));
            this.diagnosticStore = diagnosticStore ?? FailureDiagnosticStore.CreateDefault();

            materialPages = new List<InventoryPage>();

            materialPages.AddRange(Enum.GetValues(typeof(InventoryPage)).Cast<InventoryPage>());
            materialPages.Remove(InventoryPage.Weapons);
            materialPages.Remove(InventoryPage.Artifacts);
        }

        /// <summary>
        /// Stops only this scraper after a filter/end-of-list condition. It is deliberately separate
        /// from <see cref="ScanSession"/> cancellation so later scan phases can still run.
        /// </summary>
        internal bool StopScanning { get; set; }

        /// <summary>
        /// Parses the item name from an item card's nameplate
        /// </summary>
        /// <param name="nameplate">The item nameplate to parse</param>
        /// <returns>String parsed from nameplate</returns>
        internal string ScanItemName(Bitmap nameplate)
        {
            return ScanItemNameWithConfidence(nameplate).Text;
        }

        /// <summary>
        /// Same recognition as <see cref="ScanItemName"/>, also returning Tesseract's confidence --
        /// used by callers that gate low-confidence item names on inline correction (Phase 3 §3.3).
        /// Not the default <see cref="ScanItemName"/> return shape because one caller
        /// (<c>WeaponScraper.ScanEnchancementOreName</c>) runs on every weapon used as upgrade fodder,
        /// often dozens per scan -- gating correction there would mean a popup for nearly every junk
        /// item fed to a bulk enhancement, so only callers that specifically opt in (the weapon's own
        /// headline name) should request the confidence and act on it.
        /// </summary>
        internal (string Text, float ConfidencePercent) ScanItemNameWithConfidence(Bitmap nameplate)
        {
            // The caller retains the source nameplate. SetGamma returns a separate image through
            // the ref local, which is owned and released here.
            Bitmap gamma = nameplate;
            Bitmap n = null;
            try
            {
                GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref gamma);
                n = imagePreprocessor.ConvertToGrayscale(gamma);
                imagePreprocessor.SetInvert(ref n);

                // Analyze
                var (rawText, confidence) = ocrService.AnalyzeTextWithConfidence(n, Tesseract.PageSegMode.SingleBlock);
                string text = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);
                return (text, confidence * 100);
            }
            finally
            {
                n?.Dispose();
                if (!ReferenceEquals(gamma, nameplate)) gamma?.Dispose();
            }
        }

        /// <summary>
        /// Parses the number of items from for the current inventory page.
        /// </summary>
        /// <remarks>Note: This is primarily for the Weapons, Artifacts, and Furnishing inventories since they have
        /// specific inventory item counts and capacities.
        /// All other inventory pages have a "shared" inventory item counts and capacities. The count
        /// label's position is measured with the coordinate-picker tool (2026-07-03).</remarks>
        /// <param name="inventoryPage">The current inventory page</param>
        /// <returns>The number of unique items for the current inventory</returns>
        /// <exception cref="FormatException">The inventory item count could not be found where it is expected</exception>
        internal int ScanItemCount()
        {
            Rectangle region = new Rectangle(
                x: (int)(0.8865 * Navigation.GetWidth()),
                y: (int)(0.0509 * Navigation.GetHeight()),
                width: (int)(0.0641 * Navigation.GetWidth()),
                height: (int)(0.0269 * Navigation.GetHeight()));

            return ScanItemCountFromRegion(region);
        }

        private int ScanItemCountFromRegion(Rectangle region)
        {
            Bitmap latestCountBitmap = null;
            InventoryItemCountReadResult readResult = null;
            try
            {
                readResult = InventoryItemCountReader.Read(
                    scanSettings.OcrConfidenceThreshold,
                    attempt =>
                    {
                        scanSession.CancellationToken.ThrowIfCancellationRequested();
                        latestCountBitmap?.Dispose();
                        latestCountBitmap = Navigation.CaptureRegion(region);
                        progressReporter.SetNavigation_Image(latestCountBitmap);

                        using Bitmap processed = imagePreprocessor.ConvertToGrayscale(latestCountBitmap);
                        Bitmap ocrImage = processed;
                        try
                        {
                            imagePreprocessor.SetContrast(60.0, ref ocrImage);
                            imagePreprocessor.SetInvert(ref ocrImage);
                            var (rawText, confidence) = ocrService.AnalyzeTextWithConfidence(ocrImage);
                            float confidencePercent = confidence * 100;
                            InventoryItemCountParseResult parsed =
                                InventoryItemCountParser.Parse(rawText);
                            Logger.Debug(
                                "{0} item count OCR attempt {1}/{2}: raw=\"{3}\" confidence={4:0.0}% " +
                                "parse={5} reason=\"{6}\".",
                                inventoryPage,
                                attempt,
                                InventoryItemCountReader.DefaultAttempts,
                                rawText?.Trim(),
                                confidencePercent,
                                parsed.Status,
                                parsed.Reason ?? "accepted");
                            return new InventoryItemCountObservation(rawText, confidencePercent);
                        }
                        finally
                        {
                            if (!ReferenceEquals(ocrImage, processed)) ocrImage?.Dispose();
                        }
                    },
                    scanSession.CancellationToken,
                    WaitForItemCountRetry,
                    retryDelayMs: ScaledControllerDelay(300));

                if (readResult.Success)
                {
                    SaveSuccessfulItemCountDiagnostics(latestCountBitmap);
                    return readResult.Count;
                }

                InventoryItemCountObservation lastObservation =
                    readResult.Observations[readResult.Observations.Count - 1];
                Logger.Warn(
                    "{0} item count remained unresolved after {1} fresh captures; requesting inline correction.",
                    inventoryPage,
                    readResult.Observations.Count);
                string corrected = progressReporter.RequestCorrection(
                    latestCountBitmap,
                    lastObservation.RawText,
                    lastObservation.ConfidencePercent,
                    $"{inventoryPage} item count",
                    scanSession.CancellationToken);
                scanSession.CancellationToken.ThrowIfCancellationRequested();

                InventoryItemCountParseResult correction =
                    InventoryItemCountParser.Parse(corrected);
                if (correction.Success)
                {
                    Logger.Info(
                        "Accepted corrected {0} item count {1}/{2}.",
                        inventoryPage,
                        correction.Count,
                        correction.Capacity);
                    SaveSuccessfulItemCountDiagnostics(latestCountBitmap);
                    return correction.Count;
                }

                SaveItemCountFailure(latestCountBitmap, readResult, corrected, correction);
                throw new FormatException(
                    $"Unable to determine a structurally valid {inventoryPage} item count; " +
                    $"last result was {correction.Status}: {correction.Reason}");
            }
            finally
            {
                latestCountBitmap?.Dispose();
            }
        }

        private void WaitForItemCountRetry(int milliseconds)
        {
            if (scanSession.CancellationToken.WaitHandle.WaitOne(milliseconds))
                scanSession.CancellationToken.ThrowIfCancellationRequested();
        }

        private void SaveSuccessfulItemCountDiagnostics(Bitmap countBitmap)
        {
            if (!scanSettings.LogScreenshots || countBitmap == null) return;

            SaveInventoryBitmap(countBitmap, "ItemCount.png");
            using Bitmap full = Navigation.CaptureWindow();
            SaveInventoryBitmap(full, $"InventoryWindow_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png");
        }

        /// <summary>
        /// <summary>
        /// Determines the quality of an item based on it's nameplate
        /// </summary>
        /// <param name="nameplate">The an item card's nameplate</param>
        /// <returns>An integer representing quality from 0 - 5 (invalid - 5 star)</returns>
        internal int GetQuality(Bitmap nameplate)
        {
            var avg = imagePreprocessor.AverageColor(nameplate);
            var averageColor = Color.FromArgb((int)avg.R, (int)avg.G, (int)avg.B);

            Color fiveStar = Color.FromArgb(255, 188, 105, 50);
            Color fourStar = Color.FromArgb(255, 161, 86, 224);
            Color threeStar = Color.FromArgb(255, 81, 127, 203);
            Color twoStar = Color.FromArgb(255, 42, 143, 114);
            Color oneStar = Color.FromArgb(255, 114, 119, 138);

            var colors = new List<Color> { Color.Black, oneStar, twoStar, threeStar, fourStar, fiveStar };

            var c = GenshinProcesor.ClosestColor(colors, averageColor);

            return colors.IndexOf(c);
        }

        /// <summary>
        /// Extracts a bitmap copy of an item card's nameplate. Measured with the coordinate-picker
        /// tool (2026-07-03).
        /// </summary>
        internal static Bitmap GetItemNameBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: 0,
                    y: 0,
                    width: card.Width,
                    height: (int)(card.Height * 0.0574)));
        }

        /// <summary>
        /// Extracts a bitmap copy of an item card's equipped character status. Measured with the
        /// coordinate-picker tool (2026-07-03) -- the left edge matters here: an earlier guess that
        /// started far enough left to include the character portrait icon made Tesseract hallucinate
        /// stray characters even though the text itself was clean.
        /// </summary>
        internal static Bitmap GetEquippedBitmap(Bitmap card)
        {
            return GenshinProcesor.CopyBitmap(card,
                new Rectangle(
                    x: (int)(card.Width * 0.1527),
                    y: (int)(card.Height * 0.9444),
                    width: (int)(card.Width * 0.8309),
                    height: (int)(card.Height * 0.0519)));
        }

        /// <summary>
        /// Captures the selected item's always-visible detail card -- confirmed by the user to be the
        /// same region for Weapons, Artifacts, and Character Development Items. Percentages measured
        /// with the coordinate-picker tool from a full-window capture (2026-07-03, 1920x1080).
        /// </summary>
        internal Bitmap GetItemCard()
        {
            return Navigation.CaptureRegion(
                x: (int)(0.7031 * Navigation.GetWidth()),
                y: (int)((Navigation.IsNormal ? 0.1231 : 0.1120) * Navigation.GetHeight()),
                width: (int)(0.2167 * Navigation.GetWidth()),
                height: (int)((Navigation.IsNormal ? 0.7556 : 0.7800) * Navigation.GetHeight()));
        }
        /// <summary>
        /// Opens the pause menu and enters Inventory through visually verified Paimon-menu
        /// navigation. The navigator re-detects focus after every individual move and confirms only
        /// while the Inventory label itself is selected.
        /// </summary>
        internal bool EnterInventory(
            PaimonMenuNavigator menuNavigator,
            InventoryTabNavigator tabNavigator)
        {
            if (tabNavigator == null) throw new ArgumentNullException(nameof(tabNavigator));
            // The mouse-based scan's Navigation.InventoryScreen() started every phase with a real
            // Escape press to guarantee a known baseline (unpaused, no menu open) before doing
            // anything else -- controller-mode ad-hoc tests never needed this because the user
            // manually alt-tabbed into that same baseline state before clicking a test. A real scan
            // can't assume that: Genshin might already be sitting in a menu (paused, or left open by
            // a previous phase) when this runs, and EnterControllerMode()'s stick-nudge+A sequence
            // only reliably flips the input scheme from the free-roam state, not from inside a menu.
            Navigation.sim.Keyboard.KeyPress(Navigation.escapeKey);
            Navigation.SystemWait(Navigation.Speed.UI);

            var timing = new PaimonMenuNavigationTiming(
                controllerModeSettleMs: ScaledControllerDelay(2000),
                menuOpenSettleMs: ScaledControllerDelay(2000),
                moveHoldMs: ScaledControllerDelay(PaimonMenuNavigationTiming.SingleStepHoldMs),
                moveSettleMs: ScaledControllerDelay(PaimonMenuNavigationTiming.SingleStepSettleMs),
                preConfirmSettleMs: ScaledControllerDelay(600),
                confirmHoldMs: ScaledControllerDelay(300),
                destinationSettleMs: Math.Max(3000, ScaledControllerDelay(2000)),
                detectionRetryMs: ScaledControllerDelay(300));

            using (PaimonMenuNavigationResult result = menuNavigator.OpenInventory(timing))
            {
                if (result.Success)
                {
                    Logger.Info("State-aware Inventory entry succeeded. {0}", result.DetectionDetails);

                    InventoryTabNavigationTiming tabTiming = CreateInventoryTabNavigationTiming();
                    using (InventoryTabNavigationResult normalization =
                        tabNavigator.NormalizeToWeapons(tabTiming))
                    {
                        if (normalization.Success)
                        {
                            Logger.Info("Inventory entry normalized to {0} (OCR: \"{1}\").",
                                normalization.CurrentTab, normalization.RawText);
                            return true;
                        }

                        string normalizationError = normalization.Message +
                            " Inventory scan phases were skipped.";
                        Logger.Error(normalizationError);
                        progressReporter.AddError(normalizationError);
                        if (normalization.DiagnosticScreenshot != null)
                            SaveFailureScreenshot(
                                normalization.DiagnosticScreenshot,
                                "inventory_tab_normalization",
                                "weapons",
                                "failure_full.png");
                        return false;
                    }
                }

                string error = result.Message + " Inventory scan phases were skipped. " + result.DetectionDetails;
                Logger.Error(error);
                progressReporter.AddError(error);
                DestinationVerificationDiagnostics.Save(
                    diagnosticStore,
                    result,
                    "inventory",
                    InventoryScreenDetector.CopyDetectionRegion);
                return false;
            }
        }

        internal static InventoryTabNavigationTiming CreateInventoryTabNavigationTiming() =>
            new InventoryTabNavigationTiming(
                initialSettleMs: ScaledControllerDelay(500),
                buttonHoldMs: ScaledControllerDelay(200),
                stepSettleMs: ScaledControllerDelay(800),
                finalSettleMs: ScaledControllerDelay(1000),
                detectionRetryMs: ScaledControllerDelay(300));

        /// <summary>
        /// Enforces the verified-tab precondition established by <see cref="InventoryTabNavigator"/>.
        /// This method deliberately sends no input; all tab movement and visual verification belongs
        /// to the shared navigator before a category scanner is invoked.
        /// </summary>
        internal string SwitchToTab(string targetTab, string verifiedCurrentTab)
        {
            if (string.Equals(targetTab, verifiedCurrentTab, StringComparison.Ordinal))
                return targetTab;

            string warning = $"The {targetTab} scan phase was not started because its Inventory tab " +
                $"was not visually verified (verified tab: {verifiedCurrentTab ?? "(none)"}).";
            Logger.Warn(warning);
            progressReporter.AddError(warning);
            StopScanning = true;
            return verifiedCurrentTab;
        }

        /// <summary>
        /// Scales a base controller-timing duration by the app's existing scan-speed setting
        /// (<see cref="Navigation.GetDelay"/> -- the same 0.5x/Fast, 1x/Normal, 1.5x/Slow multiplier
        /// already applied to every mouse-path scan timing via <see cref="Navigation.SystemWait(Speed)"/>),
        /// so per-item controller-scan timing speeds up/slows down consistently with the rest of the
        /// app instead of being hardcoded. Below 1x (Fast), the multiplier is squared rather than
        /// applied once -- per direct user feedback (2026-07-04), a plain 0.5x scale of an
        /// already-tuned-fast base wasn't fast enough. A 20ms floor guards against a degenerate
        /// near-zero hold if the underlying setting is ever extended below 0.5x. UNVERIFIED how low
        /// Fast's resulting values can go and still register reliably against the game -- this is new,
        /// live-testing-only territory, not something a build/compile check can confirm.
        /// </summary>
        internal static int ScaledControllerDelay(int baseMs)
        {
            double delay = Navigation.GetDelay();
            if (delay < 1) delay *= delay;
            return Math.Max(20, (int)(baseMs * delay));
        }

        internal void SaveInventoryBitmap(Bitmap image, string filename)
        {
            var path = "./logging/";

            if (materialPages.Contains(inventoryPage))
                path += "Materials/";
            else path += inventoryPage.ToString() + "/";

            // filename may itself contain '/' to nest into a subfolder (e.g. "quantity/Iron_Quantity.png")
            // -- GatherData only creates the top-level per-page folder (./logging/materials/) up front,
            // so any subfolder needs creating here rather than assuming it already exists.
            Directory.CreateDirectory(Path.GetDirectoryName(path + filename));
            image.Save(path + filename);
        }

		private void SaveItemCountFailure(
			Bitmap countBitmap,
			InventoryItemCountReadResult readResult,
			string correctedText,
			InventoryItemCountParseResult correction)
		{
			try
			{
				FailureDiagnosticScope failure = diagnosticStore.CreateFailure(
					"inventory_item_count",
					inventoryPage.ToString());
				if (countBitmap != null) failure.SaveBitmap("count_crop.png", countBitmap);
				var evidence = new System.Text.StringBuilder();
				for (int i = 0; i < readResult.Observations.Count; i++)
				{
					InventoryItemCountObservation observation = readResult.Observations[i];
					InventoryItemCountParseResult parsed =
						InventoryItemCountParser.Parse(observation.RawText);
					evidence.AppendLine(
						$"attempt={i + 1}; raw=\"{observation.RawText}\"; " +
						$"confidence={observation.ConfidencePercent:0.0}; status={parsed.Status}; reason={parsed.Reason}");
				}
				evidence.AppendLine(
					$"correction=\"{correctedText}\"; status={correction.Status}; reason={correction.Reason}");
				failure.SaveText("observations.txt", evidence.ToString());
				using (Bitmap full = Navigation.CaptureWindow())
					failure.SaveBitmap("failure_full.png", full);
				Logger.Warn("Saved inventory item-count diagnostics to {0}.", failure.DirectoryPath);
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "Could not save {0} item-count diagnostics.", inventoryPage);
			}
		}

    }
}
