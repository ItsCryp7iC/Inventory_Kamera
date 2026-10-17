using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;

namespace InventoryKamera.game
{
    internal enum GenshinInputMode
    {
        Unknown,
        KeyboardMouse,
        Controller,
    }

    internal enum GenshinInputScreen
    {
        Unknown,
        FreeRoam,
        PaimonMenu,
        Controls,
        Inventory,
        Character,
    }

    internal sealed class GenshinInputModeDetection
    {
        internal GenshinInputModeDetection(
            GenshinInputMode mode,
            GenshinInputScreen screen,
            IEnumerable<string> keyboardEvidence,
            IEnumerable<string> controllerEvidence,
            string details)
        {
            Mode = mode;
            Screen = screen;
            KeyboardEvidence = (keyboardEvidence ?? Array.Empty<string>()).ToArray();
            ControllerEvidence = (controllerEvidence ?? Array.Empty<string>()).ToArray();
            Details = details ?? string.Empty;
        }

        internal GenshinInputMode Mode { get; }
        internal GenshinInputScreen Screen { get; }
        internal IReadOnlyList<string> KeyboardEvidence { get; }
        internal IReadOnlyList<string> ControllerEvidence { get; }
        internal string Details { get; }

        public override string ToString() =>
            $"mode={Mode}; screen={Screen}; keyboard=[{string.Join(", ", KeyboardEvidence)}]; " +
            $"controller=[{string.Join(", ", ControllerEvidence)}]; {Details}";
    }

    internal sealed class ControlTypeDropdownDetection
    {
        internal ControlTypeDropdownDetection(
            bool keyboardVisible,
            bool controllerVisible,
            bool controllerSelected,
            string rawText)
        {
            KeyboardVisible = keyboardVisible;
            ControllerVisible = controllerVisible;
            ControllerSelected = controllerSelected;
            RawText = rawText ?? string.Empty;
        }

        internal bool KeyboardVisible { get; }
        internal bool ControllerVisible { get; }
        internal bool ControllerSelected { get; }
        internal bool IsOpen => KeyboardVisible && ControllerVisible;
        internal string RawText { get; }
    }

    internal enum ControlTypeInteractionState
    {
        Unknown,
        ControlsSidebarFocused,
        ControlTypeFocused,
        DropdownOpen,
    }

    internal sealed class ControlTypeInteractionDetection
    {
        internal ControlTypeInteractionDetection(
            GenshinInputModeDetection page,
            ControlTypeDropdownDetection dropdown,
            double focusMarkerLightRatio,
            bool isControlTypeFocused)
        {
            Page = page ?? throw new ArgumentNullException(nameof(page));
            Dropdown = dropdown ?? throw new ArgumentNullException(nameof(dropdown));
            FocusMarkerLightRatio = focusMarkerLightRatio;
            IsControlTypeFocused = isControlTypeFocused;
            State = page.Screen != GenshinInputScreen.Controls
                ? ControlTypeInteractionState.Unknown
                : dropdown.IsOpen
                    ? ControlTypeInteractionState.DropdownOpen
                    : isControlTypeFocused
                        ? ControlTypeInteractionState.ControlTypeFocused
                        : ControlTypeInteractionState.ControlsSidebarFocused;
        }

        internal GenshinInputModeDetection Page { get; }
        internal ControlTypeDropdownDetection Dropdown { get; }
        internal double FocusMarkerLightRatio { get; }
        internal bool IsControlTypeFocused { get; }
        internal ControlTypeInteractionState State { get; }

        public override string ToString() =>
            $"state={State}; focusLightRatio={FocusMarkerLightRatio:0.000}; " +
            $"dropdownOpen={Dropdown.IsOpen}; controllerSelected={Dropdown.ControllerSelected}; " +
            $"dropdownOcr=\"{Dropdown.RawText}\"; {Page}";
    }

    internal interface IGenshinInputModeDetector
    {
        GenshinInputModeDetection Detect();
        ControlTypeInteractionDetection DetectControlTypeInteraction();
        ControlTypeInteractionDetection DetectControlTypeInteraction(Bitmap screenshot);
    }

    /// <summary>
    /// Reads only small, resolution-relative UI regions demonstrated by the Phase 5G.1B fixtures.
    /// Both modes require positive evidence; missing or conflicting evidence remains Unknown.
    /// </summary>
    internal sealed class GenshinInputModeDetector : IGenshinInputModeDetector
    {
        private const double KeyboardPaimonBackMinimumLightRatio = 0.12;
        private const double ControlTypeFocusMinimumLightRatio = 0.03;
        private const double ControllerSelectedMinimumLightRatio = 0.03;

        private static readonly NormalizedRectangle KeyboardFreeRoamPrompt =
            new NormalizedRectangle(0.015, 0.925, 0.11, 0.065);
        // Colored Xbox action glyphs prove controller input mode, but they are shared by free roam,
        // Inventory, and other controller screens. UI-state classification needs separate evidence.
        private static readonly NormalizedRectangle ControllerModeGlyphs =
            new NormalizedRectangle(0.84, 0.82, 0.14, 0.15);
        private static readonly NormalizedRectangle ControllerFreeRoamChat =
            new NormalizedRectangle(0.015, 0.925, 0.11, 0.065);
        private static readonly NormalizedRectangle ControllerPaimonPrompts =
            new NormalizedRectangle(0.18, 0.90, 0.22, 0.075);
        // The controller letters are stylized glyphs and are unreliable OCR input. Their blue X
        // and green A strokes are instead read from narrow, fixed footer locations. These are mode
        // evidence only when the established Paimon tile detector independently proves the screen.
        private static readonly NormalizedRectangle ControllerPaimonXGlyph =
            new NormalizedRectangle(0.198, 0.912, 0.022, 0.040);
        private static readonly NormalizedRectangle ControllerPaimonAGlyph =
            new NormalizedRectangle(0.297, 0.912, 0.022, 0.040);
        private static readonly NormalizedRectangle ControllerPaimonQuitText =
            new NormalizedRectangle(0.220, 0.912, 0.072, 0.040);
        private static readonly NormalizedRectangle ControllerPaimonLeaveText =
            new NormalizedRectangle(0.318, 0.912, 0.052, 0.040);
        private static readonly NormalizedRectangle KeyboardPaimonBack =
            new NormalizedRectangle(0.005, 0.01, 0.04, 0.075);
        private static readonly NormalizedRectangle ControlTypeRow =
            new NormalizedRectangle(0.245, 0.16, 0.695, 0.07);
        private static readonly NormalizedRectangle ControlTypeDropdown =
            new NormalizedRectangle(0.70, 0.205, 0.245, 0.12);
        // The controller focus chevron sits immediately left of the selected Control Type row.
        // The narrow region excludes the row itself so an unfocused cream row is not evidence.
        private static readonly NormalizedRectangle ControlTypeFocusMarker =
            new NormalizedRectangle(0.232, 0.165, 0.012, 0.055);
        // In the verified controller dropdown, the current Controller option has a white checkmark
        // at this fixed relative location. Keyboard-mode dropdown geometry differs and is not used
        // for controller restoration.
        private static readonly NormalizedRectangle ControllerSelectedMarker =
            new NormalizedRectangle(0.895, 0.255, 0.015, 0.035);
        // Controller inventory/character screens expose colored semantic action glyphs along the
        // lower-right edge. This region is separate from the free-roam prompt cluster and contains
        // no account-identifying UI in the supplied/live-validated captures.
        private static readonly NormalizedRectangle ControllerScanActionPrompts =
            new NormalizedRectangle(0.70, 0.89, 0.24, 0.08);

        private readonly IGameScreenCapture screenCapture;
        private readonly IOcrService ocrService;
        private readonly IImagePreprocessor imagePreprocessor;
        private readonly IPaimonMenuDetector paimonMenuDetector;

        internal GenshinInputModeDetector(
            IGameScreenCapture screenCapture,
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor,
            IPaimonMenuDetector paimonMenuDetector)
        {
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.ocrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            this.imagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
            this.paimonMenuDetector = paimonMenuDetector ?? throw new ArgumentNullException(nameof(paimonMenuDetector));
        }

        public GenshinInputModeDetection Detect()
        {
            using Bitmap screenshot = screenCapture.CaptureWindow();
            return Detect(screenshot);
        }

        public ControlTypeInteractionDetection DetectControlTypeInteraction()
        {
            using Bitmap screenshot = screenCapture.CaptureWindow();
            return DetectControlTypeInteraction(screenshot);
        }

        public ControlTypeInteractionDetection DetectControlTypeInteraction(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            GenshinInputModeDetection page = Detect(screenshot);
            ControlTypeDropdownDetection dropdown = DetectControlTypeDropdown(screenshot);
            using Bitmap focusCrop = CopyRegion(screenshot, ControlTypeFocusMarker);
            double focusLightRatio = GetLightPixelRatio(focusCrop);
            return new ControlTypeInteractionDetection(
                page,
                dropdown,
                focusLightRatio,
                focusLightRatio >= ControlTypeFocusMinimumLightRatio);
        }

        internal GenshinInputModeDetection Detect(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));

            var keyboard = new List<string>();
            var controller = new List<string>();
            var details = new List<string>();

            string controlText = ReadRegion(screenshot, ControlTypeRow, PageSegMode.SingleLine);
            string normalizedControl = Normalize(controlText);
            bool controlKeyboard = ContainsWholeToken(normalizedControl, "keyboard");
            bool controlController = ContainsWholeToken(normalizedControl, "controller");
            if (controlKeyboard) keyboard.Add("Controls value=Keyboard");
            if (controlController) controller.Add("Controls value=Controller");
            details.Add($"control=\"{CleanForLog(controlText)}\"");

            string keyboardFreeRoamText = ReadRegion(screenshot, KeyboardFreeRoamPrompt, PageSegMode.SparseText);
            string normalizedKeyboardFreeRoam = Normalize(keyboardFreeRoamText);
            bool keyboardFreeRoam = ContainsWholeToken(normalizedKeyboardFreeRoam, "enter");
            if (keyboardFreeRoam) keyboard.Add("free-roam Enter key");
            details.Add($"keyboardFreeRoam=\"{CleanForLog(keyboardFreeRoamText)}\"");

            using Bitmap controllerModeCrop = CopyRegion(screenshot, ControllerModeGlyphs);
            bool controllerModeGlyphs = HasControllerButtonColorCluster(
                controllerModeCrop, out int redPixels, out int greenPixels, out int yellowPixels);
            if (controllerModeGlyphs) controller.Add("colored Xbox button cluster");
            details.Add($"controllerModeColors=red:{redPixels},green:{greenPixels},yellow:{yellowPixels}");

            string controllerFreeRoamText = ReadRegion(
                screenshot, ControllerFreeRoamChat, PageSegMode.SparseText);
            string normalizedControllerFreeRoam = Normalize(controllerFreeRoamText);
            bool controllerFreeRoam = controllerModeGlyphs &&
                ContainsWholeToken(normalizedControllerFreeRoam, "chat");
            details.Add($"controllerFreeRoam=\"{CleanForLog(controllerFreeRoamText)}\"");

            string controllerPaimonText = ReadRegion(screenshot, ControllerPaimonPrompts, PageSegMode.SparseText);
            string controllerPaimonQuitText = ReadRegion(
                screenshot, ControllerPaimonQuitText, PageSegMode.SingleLine);
            string controllerPaimonLeaveText = ReadRegion(
                screenshot, ControllerPaimonLeaveText, PageSegMode.SingleLine);
            using Bitmap controllerPaimonXCrop = CopyRegion(screenshot, ControllerPaimonXGlyph);
            using Bitmap controllerPaimonACrop = CopyRegion(screenshot, ControllerPaimonAGlyph);
            bool controllerPaimonX = HasBlueControllerGlyph(controllerPaimonXCrop, out int paimonBluePixels);
            bool controllerPaimonA = HasGreenControllerGlyph(controllerPaimonACrop, out int paimonGreenPixels);
            details.Add(
                $"controllerPaimon=\"{CleanForLog(controllerPaimonText)}\"; " +
                $"controllerPaimonText=quit:\"{CleanForLog(controllerPaimonQuitText)}\"," +
                $"leave:\"{CleanForLog(controllerPaimonLeaveText)}\"; " +
                $"controllerPaimonGlyphs=blueX:{paimonBluePixels}/{controllerPaimonX}," +
                $"greenA:{paimonGreenPixels}/{controllerPaimonA}");

            using Bitmap backCrop = CopyRegion(screenshot, KeyboardPaimonBack);
            double backLightRatio = GetLightPixelRatio(backCrop);
            bool keyboardPaimon = !keyboardFreeRoam && backLightRatio >= KeyboardPaimonBackMinimumLightRatio;
            if (keyboardPaimon) keyboard.Add("Paimon mouse back control");
            details.Add($"keyboardPaimonLightRatio={backLightRatio:0.000}");

            bool controllerInventory = false;
            bool controllerCharacter = false;
            if (!controlKeyboard && !controlController && !keyboardPaimon)
            {
                using Bitmap scanPromptCrop = CopyRegion(screenshot, ControllerScanActionPrompts);
                bool controllerScanPrompts = HasControllerScanActionCluster(
                    scanPromptCrop,
                    out int scanRedPixels,
                    out int scanGreenPixels,
                    out int scanYellowPixels);
                details.Add(
                    $"controllerScanColors=red:{scanRedPixels},green:{scanGreenPixels},yellow:{scanYellowPixels}");

                if (controllerScanPrompts)
                {
                    var inventoryDetector = new InventoryScreenDetector(ocrService, imagePreprocessor);
                    InventoryScreenDetection inventory = inventoryDetector.Detect(screenshot);
                    controllerInventory = inventory.IsInventoryOpen;
                    details.Add(
                        $"inventoryTab={inventory.TabName ?? "(none)"}; inventoryOcr=\"{CleanForLog(inventory.RawText)}\"");

                    if (!controllerInventory)
                    {
                        var characterDetector = new CharacterScreenDetector(ocrService, imagePreprocessor);
                        CharacterScreenDetection character = characterDetector.Detect(screenshot);
                        controllerCharacter = character.IsCharacterOpen;
                        details.Add(
                            $"characterOpen={character.IsCharacterOpen}; characterOcr=\"{CleanForLog(character.RawText)}\"");
                    }
                }
            }

            if (controllerInventory) controller.Add("Inventory controller action prompts");
            if (controllerCharacter) controller.Add("Character controller action prompts");

            bool paimonLayout = false;
            if (!controlKeyboard && !controlController &&
                !controllerInventory && !controllerCharacter &&
                !keyboardFreeRoam && !controllerFreeRoam)
            {
                PaimonMenuDetection paimon = paimonMenuDetector.Detect(screenshot);
                paimonLayout = paimon.Tiles.Count >= 4;
                details.Add(
                    $"paimonLayout={paimonLayout}; paimonTiles={paimon.Tiles.Count}; " +
                    $"paimonFailure=\"{CleanForLog(paimon.FailureReason)}\"");
            }

            bool controllerPaimon = paimonLayout && controllerPaimonX && controllerPaimonA;
            if (controllerPaimon)
                controller.Add("Paimon blue X and green A footer glyphs");

            GenshinInputMode mode = keyboard.Count > 0 && controller.Count == 0
                ? GenshinInputMode.KeyboardMouse
                : controller.Count > 0 && keyboard.Count == 0
                    ? GenshinInputMode.Controller
                    : GenshinInputMode.Unknown;

            GenshinInputScreen screen = GenshinInputScreen.Unknown;
            if (controlKeyboard || controlController) screen = GenshinInputScreen.Controls;
            else if (controllerInventory) screen = GenshinInputScreen.Inventory;
            else if (controllerCharacter) screen = GenshinInputScreen.Character;
            else if (paimonLayout || keyboardPaimon || controllerPaimon) screen = GenshinInputScreen.PaimonMenu;
            else if (keyboardFreeRoam || controllerFreeRoam) screen = GenshinInputScreen.FreeRoam;

            return new GenshinInputModeDetection(mode, screen, keyboard, controller, string.Join("; ", details));
        }

        internal ControlTypeDropdownDetection DetectControlTypeDropdown(Bitmap screenshot)
        {
            if (screenshot == null) throw new ArgumentNullException(nameof(screenshot));
            using Bitmap crop = CopyRegion(screenshot, ControlTypeDropdown);
            string raw = ReadText(crop, PageSegMode.SparseText);
            string normalized = Normalize(raw);
            using Bitmap controllerSelectedCrop = CopyRegion(screenshot, ControllerSelectedMarker);
            bool controllerSelected =
                GetLightPixelRatio(controllerSelectedCrop) >= ControllerSelectedMinimumLightRatio;
            return new ControlTypeDropdownDetection(
                normalized.Contains("keyboard"),
                normalized.Contains("controller"),
                controllerSelected,
                raw);
        }

        private string ReadRegion(Bitmap screenshot, NormalizedRectangle region, PageSegMode mode)
        {
            using Bitmap crop = CopyRegion(screenshot, region);
            return ReadText(crop, mode);
        }

        private string ReadText(Bitmap crop, PageSegMode mode)
        {
            using Bitmap enlarged = GenshinProcesor.ResizeImage(crop, crop.Width * 2, crop.Height * 2);
            using Bitmap grayscale = imagePreprocessor.ConvertToGrayscale(enlarged);
            return ocrService.AnalyzeText(grayscale, mode) ?? string.Empty;
        }

        internal static Bitmap CopyRegion(Bitmap source, NormalizedRectangle region)
        {
            Rectangle bounds = region.ToRectangle(source.Size);
            return source.Clone(bounds, source.PixelFormat);
        }

        private static double GetLightPixelRatio(Bitmap crop)
        {
            int light = 0;
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    Color pixel = crop.GetPixel(x, y);
                    if (pixel.R >= 190 && pixel.G >= 185 && pixel.B >= 165) light++;
                }
            }
            return light / (double)(crop.Width * crop.Height);
        }

        private static bool HasControllerButtonColorCluster(
            Bitmap crop,
            out int redPixels,
            out int greenPixels,
            out int yellowPixels)
        {
            redPixels = 0;
            greenPixels = 0;
            yellowPixels = 0;
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    Color pixel = crop.GetPixel(x, y);
                    if (pixel.R > 100 && pixel.R > pixel.G * 1.35 && pixel.R > pixel.B * 1.35)
                        redPixels++;
                    if (pixel.G > 90 && pixel.G > pixel.R * 1.2 && pixel.G > pixel.B * 1.2)
                        greenPixels++;
                    if (pixel.R > 120 && pixel.G > 90 && pixel.B < 100 &&
                        Math.Abs(pixel.R - pixel.G) < 100)
                    {
                        yellowPixels++;
                    }
                }
            }

            double scale = crop.Height / (1079.0 * ControllerModeGlyphs.Height);
            int minimum = Math.Max(6, (int)Math.Round(30 * scale * scale));
            return redPixels >= minimum && greenPixels >= minimum && yellowPixels >= minimum;
        }

        private static bool HasControllerScanActionCluster(
            Bitmap crop,
            out int redPixels,
            out int greenPixels,
            out int yellowPixels)
        {
            CountControllerButtonColors(crop, out redPixels, out greenPixels, out yellowPixels);

            double scale = crop.Height / (1079.0 * ControllerScanActionPrompts.Height);
            int minimum = Math.Max(6, (int)Math.Round(30 * scale * scale));
            // A/Confirm-or-Leave is consistently green. Requiring a second distinct Xbox action
            // color prevents a colorful game background from becoming Controller evidence by itself.
            return greenPixels >= minimum && (redPixels >= minimum || yellowPixels >= minimum);
        }

        private static bool HasBlueControllerGlyph(Bitmap crop, out int bluePixels)
        {
            bluePixels = 0;
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    Color pixel = crop.GetPixel(x, y);
                    if (pixel.B > 100 && pixel.B > pixel.R * 1.2 && pixel.B > pixel.G * 1.1)
                        bluePixels++;
                }
            }

            return bluePixels >= ScaledGlyphMinimum(crop, ControllerPaimonXGlyph);
        }

        private static bool HasGreenControllerGlyph(Bitmap crop, out int greenPixels)
        {
            greenPixels = 0;
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    Color pixel = crop.GetPixel(x, y);
                    if (pixel.G > 90 && pixel.G > pixel.R * 1.2 && pixel.G > pixel.B * 1.2)
                        greenPixels++;
                }
            }

            return greenPixels >= ScaledGlyphMinimum(crop, ControllerPaimonAGlyph);
        }

        private static int ScaledGlyphMinimum(Bitmap crop, NormalizedRectangle region)
        {
            double scale = crop.Height / (1079.0 * region.Height);
            return Math.Max(6, (int)Math.Round(30 * scale * scale));
        }

        private static void CountControllerButtonColors(
            Bitmap crop,
            out int redPixels,
            out int greenPixels,
            out int yellowPixels)
        {
            redPixels = 0;
            greenPixels = 0;
            yellowPixels = 0;
            for (int y = 0; y < crop.Height; y++)
            {
                for (int x = 0; x < crop.Width; x++)
                {
                    Color pixel = crop.GetPixel(x, y);
                    if (pixel.R > 100 && pixel.R > pixel.G * 1.35 && pixel.R > pixel.B * 1.35)
                        redPixels++;
                    if (pixel.G > 90 && pixel.G > pixel.R * 1.2 && pixel.G > pixel.B * 1.2)
                        greenPixels++;
                    if (pixel.R > 120 && pixel.G > 90 && pixel.B < 100 &&
                        Math.Abs(pixel.R - pixel.G) < 100)
                    {
                        yellowPixels++;
                    }
                }
            }
        }

        private static bool ContainsWholeToken(string normalized, string token) =>
            Regex.IsMatch(normalized ?? string.Empty, $@"(?:^|\s){Regex.Escape(token)}(?:\s|$)");

        private static string Normalize(string text) =>
            Regex.Replace((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

        private static string CleanForLog(string text) =>
            Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
    }

    internal readonly struct NormalizedRectangle
    {
        internal NormalizedRectangle(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        internal double X { get; }
        internal double Y { get; }
        internal double Width { get; }
        internal double Height { get; }

        internal Rectangle ToRectangle(Size size)
        {
            int left = Math.Clamp((int)Math.Round(size.Width * X), 0, size.Width - 1);
            int top = Math.Clamp((int)Math.Round(size.Height * Y), 0, size.Height - 1);
            int right = Math.Clamp((int)Math.Round(size.Width * (X + Width)), left + 1, size.Width);
            int bottom = Math.Clamp((int)Math.Round(size.Height * (Y + Height)), top + 1, size.Height);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }
}
