using InventoryKamera.game;
using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

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

        /// <summary>
        /// Shared with every subclass (previously duplicated as a private method on
        /// <c>ArtifactScraper</c> only) so every controller-mode capture region -- including this
        /// base class's own <see cref="DetectCurrentTabIndex"/> -- can leave a visual
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

        public InventoryScraper(
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor,
            IScanSettings scanSettings,
            IScanProgressReporter progressReporter,
            ScanSession scanSession)
        {
            this.ocrService = ocrService;
            this.imagePreprocessor = imagePreprocessor;
            this.scanSettings = scanSettings;
            this.progressReporter = progressReporter;
            this.scanSession = scanSession ?? throw new ArgumentNullException(nameof(scanSession));

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
            GenshinProcesor.SetGamma(0.2, 0.2, 0.2, ref nameplate);
            Bitmap n = imagePreprocessor.ConvertToGrayscale(nameplate);
            imagePreprocessor.SetInvert(ref n);

            // Analyze
            var (rawText, confidence) = ocrService.AnalyzeTextWithConfidence(n, Tesseract.PageSegMode.SingleBlock);
            string text = Regex.Replace(rawText.ToLower(), @"[\W]", string.Empty);

            n.Dispose();

            return (text, confidence * 100);
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
            using (Bitmap countBitmap = Navigation.CaptureRegion(region))
            {
                progressReporter.SetNavigation_Image(countBitmap);

                Bitmap n = imagePreprocessor.ConvertToGrayscale(countBitmap);
                imagePreprocessor.SetContrast(60.0, ref n);
                imagePreprocessor.SetInvert(ref n);

                var (rawText, confidence) = ocrService.AnalyzeTextWithConfidence(n);
                string text = rawText.Trim();
                n.Dispose();

                // Remove any non-numeric and '/' characters
                text = Regex.Replace(text, @"[^0-9/]", string.Empty);

                float confidencePercent = confidence * 100;
                Logger.Debug("{0} item count OCR: rawText=\"{1}\" confidence={2:0.0}% threshold={3}%", inventoryPage, text, confidencePercent, scanSettings.OcrConfidenceThreshold);
                if (string.IsNullOrWhiteSpace(text) || confidencePercent < scanSettings.OcrConfidenceThreshold)
                {
                    Logger.Debug("{0} item count below confidence threshold -- requesting inline correction", inventoryPage);
                    string corrected = progressReporter.RequestCorrection(
                        countBitmap,
                        text,
                        confidencePercent,
                        $"{inventoryPage} item count",
                        scanSession.CancellationToken);
                    text = Regex.Replace(corrected ?? string.Empty, @"[^0-9/]", string.Empty);
                }

                if (string.IsNullOrWhiteSpace(text) || scanSettings.LogScreenshots)
                {
                    SaveInventoryBitmap(countBitmap, "ItemCount.png");
                    SaveInventoryBitmap(Navigation.CaptureWindow(), $"InventoryWindow_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png");
                    if (string.IsNullOrWhiteSpace(text)) throw new FormatException($"Unable to locate {inventoryPage} item count.");
                }

                int count;
                string pageCapacity;
                switch (inventoryPage)
                {
                    case InventoryPage.Artifacts:
                        pageCapacity = "1800";
                        break;
                    default:
                        pageCapacity = "2000";
                        break;
                }

                if (Regex.IsMatch(text, "/")) // Check for slash
                {
                    count = int.Parse(text.Split('/')[0]);
                }
                else if (Regex.Matches(text, pageCapacity).Count == 1) // Remove the inventory capacity from number
                {
                    text = text.Replace(pageCapacity, string.Empty);
                    count = int.Parse(text);
                }
                else // Extreme worst case
                {
                    count = 2000;
                    Logger.Debug("Defaulted to 2000 for inventory page capacity");
                }

                return count;
            }
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

        // Genshin's inventory remembers whichever tab was last open, so tab-switching can't assume a
        // known starting tab -- the tab name label gets OCR'd and fuzzy-matched against this list.
        // Shared across Weapons/Artifacts/Character Development Items (Phase 3 §6c).
        internal static readonly string[] ControllerInventoryTabNames = InventoryTabRecognition.Names;

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

                    var tabTiming = new InventoryTabNavigationTiming(
                        initialSettleMs: ScaledControllerDelay(500),
                        buttonHoldMs: ScaledControllerDelay(200),
                        stepSettleMs: ScaledControllerDelay(800),
                        finalSettleMs: ScaledControllerDelay(1000),
                        detectionRetryMs: ScaledControllerDelay(300));
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
                            SaveDebugScreenshot(
                                normalization.DiagnosticScreenshot,
                                "inventory/tab_normalization_failure",
                                force: true);
                        return false;
                    }
                }

                string error = result.Message + " Inventory scan phases were skipped. " + result.DetectionDetails;
                Logger.Error(error);
                progressReporter.AddError(error);
                if (result.DiagnosticScreenshot != null)
                    SaveDebugScreenshot(result.DiagnosticScreenshot, "paimonmenu/inventory_entry_failure", force: true);
                return false;
            }
        }

        /// <summary>
        /// Captures the tab-name label (top-left of the inventory screen), OCRs it, and fuzzy-matches
        /// it against <see cref="ControllerInventoryTabNames"/>. Returns the matched index (-1 if no
        /// confident match). Must be called while already inside Inventory (see
        /// <see cref="EnterInventory"/>) and before the owning <c>GameNavigator</c> is
        /// disposed -- disposal mashes back out of the menu as a safety net.
        /// </summary>
        internal int DetectCurrentTabIndex(out string rawText)
        {
            // Height tightened 0.08 -> 0.05 (2026-07-07): the debug screenshot showed the sub-tab
            // icon row bleeding into the bottom of the crop, which likely confused Tesseract's
            // SingleLine page-seg mode into garbling otherwise-clean "Weapons" text into "Mn NT".
            // Needs live confirmation that the tab-name line itself is still fully inside the crop.
            using (var region = Navigation.CaptureRegion(
                x: (int)(0.09 * Navigation.GetWidth()),
                y: (int)(0.035 * Navigation.GetHeight()),
                width: (int)(0.20 * Navigation.GetWidth()),
                height: (int)(0.05 * Navigation.GetHeight())))
            {
                SaveDebugScreenshot(region, "tabdetection/region");

                if (scanSettings.LogScreenshots)
                {
                    Bitmap processed = imagePreprocessor.ConvertToGrayscale(region);
                    imagePreprocessor.SetContrast(60.0, ref processed);
                    imagePreprocessor.SetInvert(ref processed);
                    using (processed) SaveDebugScreenshot(processed, "tabdetection/processed");
                }

                var detector = new InventoryScreenDetector(ocrService, imagePreprocessor);
                InventoryScreenDetection detection = detector.DetectTabRegion(region);
                rawText = detection.RawText;
            }
            return InventoryTabRecognition.Match(rawText);
        }

        /// <summary>
        /// Switches to <paramref name="targetTab"/> from wherever the cursor currently is, cycling
        /// LB/RB (the inventory sub-tab row's own input, distinct from the pause menu's stick-driven
        /// grid). Tabs wrap around (circular) -- takes whichever direction is fewer presses. Must run
        /// inside Inventory already (see <see cref="EnterInventory"/>).
        /// </summary>
        /// <param name="knownCurrentTab">
        /// If the caller already knows which tab is active (e.g. the previous scan phase just
        /// finished switching to and scanning that exact tab, and nothing changes tabs mid-scan), pass
        /// it here to skip OCR detection entirely. Per user (2026-07-05): re-detecting via OCR for
        /// every phase transition is both slower and less reliable than just remembering where the
        /// last phase left off -- a live miss ("Weapons" misread as "eepons J"/"eepons |") persisted
        /// even after a settle-wait and 3 retry attempts, pointing at an OCR/rendering issue with this
        /// specific capture rather than a timing one; skipping the read avoids it outright.
        /// </param>
        /// <returns>The tab actually active after this call (<paramref name="targetTab"/> on success,
        /// or the original tab if detection failed and the switch was skipped) -- pass this into the
        /// next phase's <paramref name="knownCurrentTab"/> to keep the chain going without OCR.</returns>
        internal string SwitchToTab(GameNavigator navigator, string targetTab, string knownCurrentTab = null)
        {
            int currentIndex;
            string rawText = null;

            if (knownCurrentTab != null)
            {
                currentIndex = Array.IndexOf(ControllerInventoryTabNames, knownCurrentTab);
                Logger.Info("Tab switch: using known current tab \"{0}\" (skipping OCR detection).", knownCurrentTab);
            }
            else
            {
                // Per user (2026-07-05): no settle wait existed between one scan phase's last
                // per-item advance and the next phase's tab-detection capture -- contributed to the
                // "Weapons" -> "eepons |" misread below, since detection fired immediately with zero
                // margin for whatever the previous action's animation/UI was still doing.
                Thread.Sleep(ScaledControllerDelay(500));

                // Detection can also flake even with the settle above -- retry a few times before
                // giving up, rather than treating a -1 "undetected" index as a valid array index in
                // the step-count math below. That silently produced a plausible-but-wrong step count
                // once (2 presses instead of 1), which would land on the wrong tab entirely and scan
                // garbage data with no visible error.
                currentIndex = -1;
                const int maxDetectAttempts = 3;
                for (int attempt = 1; attempt <= maxDetectAttempts && currentIndex < 0; attempt++)
                {
                    currentIndex = DetectCurrentTabIndex(out rawText);
                    if (currentIndex < 0 && attempt < maxDetectAttempts)
                    {
                        Logger.Warn("Tab detection attempt {0} failed (raw=\"{1}\") -- retrying.", attempt, rawText);
                        Thread.Sleep(ScaledControllerDelay(300));
                    }
                }
            }

            int targetIndex = Array.IndexOf(ControllerInventoryTabNames, targetTab);

            if (targetIndex < 0)
            {
                string warning = $"\"{targetTab}\" is not a recognized inventory tab name -- skipped tab switch.";
                Logger.Warn(warning);
                progressReporter.AddError(warning);
                return knownCurrentTab;
            }

            if (currentIndex < 0)
            {
                // Per user (2026-07-07): don't let the scan continue against a tab it can't confirm
                // -- that risks silently cataloguing items under the wrong item type. Ending this
                // phase's scan (StopScanning) is safe: WeaponScraper/ArtifactScraper/MaterialScraper's
                // own scan loops all check StopScanning before every iteration, so this stops before a
                // single item is read rather than partway through a wrong-tab grid.
                string warning = $"Could not confidently detect the current inventory tab (last OCR: \"{rawText}\") -- " +
                    $"stopping this scan phase instead of switching to {targetTab} blind.";
                Logger.Warn(warning);
                progressReporter.AddError(warning);
                StopScanning = true;
                return knownCurrentTab; // unknown tab either way; nothing better to report back
            }

            string currentTabName = ControllerInventoryTabNames[currentIndex];

            if (currentIndex == targetIndex)
            {
                Logger.Info("Tab switch: scanned in on \"{0}\" (raw=\"{1}\"), already the target -- 0 shoulder presses needed.", currentTabName, rawText);
                return targetTab;
            }

            int tabCount = ControllerInventoryTabNames.Length;
            int forwardSteps = ((targetIndex - currentIndex) % tabCount + tabCount) % tabCount;
            int backwardSteps = tabCount - forwardSteps;
            bool goForward = forwardSteps <= backwardSteps;
            int steps = Math.Min(forwardSteps, backwardSteps);
            string shoulderButton = goForward ? "RightShoulder" : "LeftShoulder";

            Logger.Info("Tab switch: scanned in on \"{0}\" (raw=\"{1}\"), target \"{2}\" -- {3} {4} ({5}) presses.",
                currentTabName, rawText, targetTab, steps, shoulderButton, goForward ? "forward/right" : "backward/left");

            // Per user (2026-07-04): base tab-switch timing doubled to 200/800/1000 (through several
            // rounds: 80/100/300 -> 120/150/400 -> 240/300/800 -> 100/400/500 -> this) -- still
            // scaled by ScaledControllerDelay, just a higher starting point.
            for (int i = 0; i < steps; i++)
            {
                if (goForward)
                    navigator.TapNextTab(holdMs: ScaledControllerDelay(200));
                else
                    navigator.TapPreviousTab(holdMs: ScaledControllerDelay(200));
                Thread.Sleep(ScaledControllerDelay(800));
            }
            Thread.Sleep(ScaledControllerDelay(1000));

            return targetTab;
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

    }
}
