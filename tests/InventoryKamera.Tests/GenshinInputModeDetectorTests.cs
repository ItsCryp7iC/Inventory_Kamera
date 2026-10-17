using InventoryKamera.game;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Tesseract;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class GenshinInputModeDetectorTests : IDisposable
    {
        private const int FixtureWidth = 1919;
        private const int FixtureHeight = 1079;

        private readonly OcrService ocr = new OcrService(engineCount: 1);
        private readonly GenshinInputModeDetector detector;

        public GenshinInputModeDetectorTests()
        {
            ocr.Restart();
            detector = new GenshinInputModeDetector(
                new UnusedCapture(), ocr, new ImageProcessor(), new NoPaimonDetector());
        }

        [Fact]
        public void KeyboardMouseFreeRoam_UsesPositiveEnterPrompt()
        {
            using Bitmap screen = Compose("keyboard_free_roam_prompt.png", 0.015, 0.925, 0.11, 0.065);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.KeyboardMouse, result.Mode);
            Assert.Equal(GenshinInputScreen.FreeRoam, result.Screen);
        }

        [Fact]
        public void ControllerFreeRoam_RequiresControllerGlyphsAndChatPrompt()
        {
            using Bitmap screen = Compose("controller_free_roam_prompts.png", 0.84, 0.82, 0.14, 0.15);
            Paste(screen, "controller_free_roam_chat.png", 0.015, 0.925, 0.11, 0.065);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.True(result.Mode == GenshinInputMode.Controller, result.ToString());
            Assert.Equal(GenshinInputScreen.FreeRoam, result.Screen);
        }

        [Fact]
        public void ControllerGlyphsAlone_ProveModeButNotFreeRoamState()
        {
            using Bitmap screen = Compose("controller_free_roam_prompts.png", 0.84, 0.82, 0.14, 0.15);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.Controller, result.Mode);
            Assert.Equal(GenshinInputScreen.Unknown, result.Screen);
        }

        [Fact]
        public void KeyboardMousePaimon_UsesPositiveMouseBackControl()
        {
            using Bitmap screen = Compose("keyboard_paimon_back.png", 0.005, 0.01, 0.04, 0.075);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.KeyboardMouse, result.Mode);
            Assert.Equal(GenshinInputScreen.PaimonMenu, result.Screen);
        }

        [Fact]
        public void ControllerPaimon_UsesPositiveQuitAndLeavePrompts()
        {
            using Bitmap screen = Compose("controller_paimon_prompts.png", 0.18, 0.90, 0.22, 0.075);
            var paimonDetector = CreateDetector(new PaimonLayoutDetector());

            GenshinInputModeDetection result = paimonDetector.Detect(screen);

            Assert.True(result.Mode == GenshinInputMode.Controller, result.ToString());
            Assert.Equal(GenshinInputScreen.PaimonMenu, result.Screen);
        }

        [Fact]
        public void ControllerPaimon_LiveGlyphEvidenceSurvivesCorruptFooterOcr()
        {
            using Bitmap screen = Compose("controller_paimon_live_footer.png", 0.195, 0.905, 0.175, 0.055);
            var paimonDetector = new GenshinInputModeDetector(
                new UnusedCapture(),
                new FixedTextOcr("Â© autcame @ Leave"),
                new ImageProcessor(),
                new PaimonLayoutDetector());

            GenshinInputModeDetection result = paimonDetector.Detect(screen);

            Assert.Equal(GenshinInputMode.Controller, result.Mode);
            Assert.Equal(GenshinInputScreen.PaimonMenu, result.Screen);
            Assert.Contains(
                result.ControllerEvidence,
                evidence => evidence.Contains("blue X and green A", StringComparison.Ordinal));
            Assert.Contains("autcame", result.Details);
        }

        [Fact]
        public void ControllerPaimon_LiveFooterAndRealTileDetectorClassifyControllerPaimon()
        {
            string menuPath = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PaimonMenu",
                "menu_inventory_selected.png");
            using var screen = new Bitmap(menuPath);
            Paste(screen, "controller_paimon_live_footer.png", 0.195, 0.905, 0.175, 0.055);
            var images = new ImageProcessor();
            var modeDetector = new GenshinInputModeDetector(
                new UnusedCapture(),
                ocr,
                images,
                new PaimonMenuDetector(ocr, images));

            GenshinInputModeDetection result = modeDetector.Detect(screen);

            Assert.Equal(GenshinInputMode.Controller, result.Mode);
            Assert.Equal(GenshinInputScreen.PaimonMenu, result.Screen);
            Assert.Contains("paimonLayout=True", result.Details);
        }

        [Fact]
        public void PaimonLayoutWithoutControllerFooter_DoesNotInferControllerMode()
        {
            using var screen = new Bitmap(FixtureWidth, FixtureHeight);
            var paimonDetector = CreateDetector(new PaimonLayoutDetector());

            GenshinInputModeDetection result = paimonDetector.Detect(screen);

            Assert.Equal(GenshinInputMode.Unknown, result.Mode);
            Assert.Equal(GenshinInputScreen.PaimonMenu, result.Screen);
        }

        [Fact]
        public void ControllerColoredCirclesOutsidePaimonFooter_DoNotInferControllerMode()
        {
            using var screen = new Bitmap(FixtureWidth, FixtureHeight);
            using (Graphics graphics = Graphics.FromImage(screen))
            {
                graphics.FillEllipse(Brushes.DeepSkyBlue, 1000, 400, 30, 30);
                graphics.FillEllipse(Brushes.LimeGreen, 1050, 400, 30, 30);
            }
            var paimonDetector = CreateDetector(new PaimonLayoutDetector());

            GenshinInputModeDetection result = paimonDetector.Detect(screen);

            Assert.Equal(GenshinInputMode.Unknown, result.Mode);
            Assert.Equal(GenshinInputScreen.PaimonMenu, result.Screen);
        }

        [Fact]
        public void ConflictingKeyboardAndControllerPaimonEvidence_ReturnsUnknownPaimon()
        {
            using Bitmap screen = Compose("keyboard_paimon_back.png", 0.005, 0.01, 0.04, 0.075);
            Paste(screen, "controller_paimon_live_footer.png", 0.195, 0.905, 0.175, 0.055);
            var paimonDetector = CreateDetector(new PaimonLayoutDetector());

            GenshinInputModeDetection result = paimonDetector.Detect(screen);

            Assert.Equal(GenshinInputMode.Unknown, result.Mode);
            Assert.Equal(GenshinInputScreen.PaimonMenu, result.Screen);
            Assert.NotEmpty(result.KeyboardEvidence);
            Assert.NotEmpty(result.ControllerEvidence);
        }

        [Fact]
        public void ControllerInventory_UsesRecognizedTabAndControllerActionPrompts()
        {
            using Bitmap screen = Compose("inventory_controller_header.png", 0.085, 0.035, 0.20, 0.05);
            Paste(screen, "inventory_controller_prompts.png", 0.70, 0.89, 0.24, 0.08);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.True(result.Mode == GenshinInputMode.Controller, result.ToString());
            Assert.Equal(GenshinInputScreen.Inventory, result.Screen);
            Assert.Contains(result.ControllerEvidence, evidence => evidence.Contains("Inventory"));
        }

        [Fact]
        public void ControllerInventory_WinsOverOverlappingGenericControllerGlyphEvidence()
        {
            using Bitmap screen = Compose("inventory_controller_header.png", 0.085, 0.035, 0.20, 0.05);
            Paste(screen, "inventory_controller_prompts.png", 0.70, 0.89, 0.24, 0.08);
            Paste(screen, "inventory_controller_freeroam_overlap.png", 0.84, 0.82, 0.14, 0.15);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.Controller, result.Mode);
            Assert.Equal(GenshinInputScreen.Inventory, result.Screen);
            Assert.NotEqual(GenshinInputScreen.FreeRoam, result.Screen);
        }

        [Fact]
        public void ControllerInventory_SpecificStateWinsEvenWhenFreeRoamTextAlsoMatches()
        {
            using Bitmap screen = Compose("inventory_controller_header.png", 0.085, 0.035, 0.20, 0.05);
            Paste(screen, "inventory_controller_prompts.png", 0.70, 0.89, 0.24, 0.08);
            Paste(screen, "inventory_controller_freeroam_overlap.png", 0.84, 0.82, 0.14, 0.15);
            Paste(screen, "controller_free_roam_chat.png", 0.015, 0.925, 0.11, 0.065);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.Controller, result.Mode);
            Assert.Equal(GenshinInputScreen.Inventory, result.Screen);
        }

        [Fact]
        public void ControllerCharacter_UsesAttributesAndControllerActionPrompts()
        {
            using Bitmap screen = Compose("character_controller_header.png", 0.080, 0.115, 0.150, 0.070);
            Paste(screen, "character_controller_prompts.png", 0.70, 0.89, 0.24, 0.08);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.True(result.Mode == GenshinInputMode.Controller, result.ToString());
            Assert.Equal(GenshinInputScreen.Character, result.Screen);
            Assert.Contains(result.ControllerEvidence, evidence => evidence.Contains("Character"));
        }

        [Fact]
        public void ControllerActionColorsWithoutKnownScanScreenRemainUnknown()
        {
            using Bitmap screen = Compose("inventory_controller_prompts.png", 0.70, 0.89, 0.24, 0.08);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.Unknown, result.Mode);
            Assert.Equal(GenshinInputScreen.Unknown, result.Screen);
        }

        [Fact]
        public void ControllerInventoryEvidence_ScalesToDownsampledCapture()
        {
            using Bitmap screen = Compose(
                "inventory_controller_header.png",
                0.085,
                0.035,
                0.20,
                0.05,
                screenWidth: 1280,
                screenHeight: 720);
            Paste(screen, "inventory_controller_prompts.png", 0.70, 0.89, 0.24, 0.08);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.True(result.Mode == GenshinInputMode.Controller, result.ToString());
            Assert.Equal(GenshinInputScreen.Inventory, result.Screen);
        }

        [Theory]
        [InlineData("control_type_keyboard.png", 1)]
        [InlineData("control_type_controller.png", 2)]
        public void ControlsValue_IsIndependentPositiveModeEvidence(string fixture, int expectedValue)
        {
            using Bitmap screen = Compose(fixture, 0.245, 0.16, 0.695, 0.07);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal((GenshinInputMode)expectedValue, result.Mode);
            Assert.Equal(GenshinInputScreen.Controls, result.Screen);
        }

        [Theory]
        [InlineData("dropdown_keyboard_selected.png", false)]
        [InlineData("dropdown_controller_selected.png", true)]
        public void ControlTypeDropdown_RequiresBothKnownOptionsAndTracksControllerSelection(
            string fixture,
            bool controllerSelected)
        {
            using Bitmap screen = Compose(fixture, 0.70, 0.205, 0.245, 0.12);

            ControlTypeDropdownDetection result = detector.DetectControlTypeDropdown(screen);

            Assert.True(result.KeyboardVisible, result.RawText);
            Assert.True(result.ControllerVisible, result.RawText);
            Assert.True(result.IsOpen);
            Assert.Equal(controllerSelected, result.ControllerSelected);
        }

        [Fact]
        public void ControllerControlsSidebarWithoutControlTypeMarker_IsExplicitlyClassified()
        {
            using Bitmap screen = Compose("control_type_controller.png", 0.245, 0.16, 0.695, 0.07);

            ControlTypeInteractionDetection result = detector.DetectControlTypeInteraction(screen);

            Assert.Equal(ControlTypeInteractionState.ControlsSidebarFocused, result.State);
            Assert.False(result.IsControlTypeFocused);
        }

        [Fact]
        public void ControllerControlTypeFocus_RequiresPositiveChevronEvidence()
        {
            using Bitmap screen = Compose("control_type_controller.png", 0.245, 0.16, 0.695, 0.07);
            Paste(screen, "control_type_focused_marker.png", 0.232, 0.165, 0.012, 0.055);

            ControlTypeInteractionDetection result = detector.DetectControlTypeInteraction(screen);

            Assert.Equal(ControlTypeInteractionState.ControlTypeFocused, result.State);
            Assert.True(result.IsControlTypeFocused);
            Assert.False(result.Dropdown.IsOpen);
        }

        [Fact]
        public void ControllerDropdownOpen_RequiresOptionsAndCurrentControllerCheckmark()
        {
            using Bitmap screen = Compose("control_type_controller.png", 0.245, 0.16, 0.695, 0.07);
            Paste(screen, "control_type_focused_marker.png", 0.232, 0.165, 0.012, 0.055);
            Paste(screen, "dropdown_controller_selected.png", 0.70, 0.205, 0.245, 0.12);

            ControlTypeInteractionDetection result = detector.DetectControlTypeInteraction(screen);

            Assert.Equal(ControlTypeInteractionState.DropdownOpen, result.State);
            Assert.True(result.Dropdown.IsOpen);
            Assert.True(result.Dropdown.ControllerSelected);
        }

        [Fact]
        public void MissingEvidence_ReturnsUnknown()
        {
            using var screen = new Bitmap(FixtureWidth, FixtureHeight);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.True(result.Mode == GenshinInputMode.Unknown, result.ToString());
            Assert.Equal(GenshinInputScreen.Unknown, result.Screen);
        }

        [Fact]
        public void ConflictingPositiveEvidence_ReturnsUnknown()
        {
            using Bitmap screen = Compose("keyboard_free_roam_prompt.png", 0.015, 0.925, 0.11, 0.065);
            Paste(screen, "controller_free_roam_prompts.png", 0.84, 0.82, 0.14, 0.15);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.True(result.Mode == GenshinInputMode.Unknown, result.ToString());
            Assert.NotEmpty(result.KeyboardEvidence);
            Assert.NotEmpty(result.ControllerEvidence);
        }

        [Fact]
        public void RelativeRegions_WorkAtDownscaledResolution()
        {
            using Bitmap screen = Compose(
                "control_type_keyboard.png", 0.245, 0.16, 0.695, 0.07,
                screenWidth: 1280, screenHeight: 720);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.KeyboardMouse, result.Mode);
            Assert.Equal(GenshinInputScreen.Controls, result.Screen);
        }

        [Fact]
        public void RelativeRegions_WorkOnSixteenByTenCanvas()
        {
            using Bitmap screen = Compose(
                "control_type_controller.png", 0.245, 0.16, 0.695, 0.07,
                screenWidth: 1920, screenHeight: 1200);

            GenshinInputModeDetection result = detector.Detect(screen);

            Assert.Equal(GenshinInputMode.Controller, result.Mode);
            Assert.Equal(GenshinInputScreen.Controls, result.Screen);
        }

        public void Dispose() => ocr.Dispose();

        private GenshinInputModeDetector CreateDetector(IPaimonMenuDetector paimonMenuDetector) =>
            new GenshinInputModeDetector(
                new UnusedCapture(), ocr, new ImageProcessor(), paimonMenuDetector);

        private static Bitmap Compose(
            string fixture,
            double x,
            double y,
            double width,
            double height,
            int screenWidth = FixtureWidth,
            int screenHeight = FixtureHeight)
        {
            var screen = new Bitmap(screenWidth, screenHeight);
            Paste(screen, fixture, x, y, width, height);
            return screen;
        }

        private static void Paste(
            Bitmap screen,
            string fixture,
            double x,
            double y,
            double width,
            double height)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "InputMode", fixture);
            using var crop = new Bitmap(path);
            Rectangle target = new NormalizedRectangle(x, y, width, height).ToRectangle(screen.Size);
            using Graphics graphics = Graphics.FromImage(screen);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(crop, target);
        }

        private sealed class UnusedCapture : IGameScreenCapture
        {
            public Bitmap CaptureWindow() => throw new NotSupportedException();
        }

        private sealed class NoPaimonDetector : IPaimonMenuDetector
        {
            public PaimonMenuDetection Detect(Bitmap screenshot) =>
                new PaimonMenuDetection(Array.Empty<PaimonMenuTile>(), null, null, null, "not Paimon");
        }

        private sealed class PaimonLayoutDetector : IPaimonMenuDetector
        {
            public PaimonMenuDetection Detect(Bitmap screenshot)
            {
                var tiles = new[]
                {
                    Tile(0),
                    Tile(1),
                    Tile(2),
                    Tile(3),
                };
                return new PaimonMenuDetection(tiles, tiles[0], null, null);
            }

            private static PaimonMenuTile Tile(int index) =>
                new PaimonMenuTile(
                    $"Tile {index}",
                    Rectangle.Empty,
                    new Rectangle(100 + index * 110, 300, 100, 100),
                    1f,
                    index == 0 ? 100 : 0);
        }

        private sealed class FixedTextOcr : IOcrService
        {
            private readonly string text;

            internal FixedTextOcr(string text)
            {
                this.text = text;
            }

            public void Restart()
            {
            }

            public string AnalyzeText(
                Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false) => text;

            public (string Text, float Confidence) AnalyzeTextWithConfidence(
                Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false) => (text, 1f);
        }
    }
}
