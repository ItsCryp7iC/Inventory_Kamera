using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class PaimonMenuDetectorFixture : IDisposable
    {
        private readonly OcrService ocr = new OcrService(engineCount: 1);
        internal PaimonMenuDetector Detector { get; }
        internal InventoryScreenDetector InventoryScreenDetector { get; }
        internal CharacterScreenDetector CharacterScreenDetector { get; }

        public PaimonMenuDetectorFixture()
        {
            ocr.Restart();
            var images = new ImageProcessor();
            Detector = new PaimonMenuDetector(ocr, images);
            InventoryScreenDetector = new InventoryScreenDetector(ocr, images);
            CharacterScreenDetector = new CharacterScreenDetector(ocr, images);
        }

        public static Bitmap Load(string name) => new Bitmap(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "PaimonMenu", name));

        public void Dispose() => ocr.Dispose();
    }

    public class PaimonMenuDetectorTests : IClassFixture<PaimonMenuDetectorFixture>
    {
        private readonly PaimonMenuDetectorFixture fixture;

        public PaimonMenuDetectorTests(PaimonMenuDetectorFixture fixture) => this.fixture = fixture;

        [Fact]
        public void Detect_FindsInventoryAndCharacterTargets()
        {
            using var image = PaimonMenuDetectorFixture.Load("menu_initial.png");
            PaimonMenuDetection result = fixture.Detector.Detect(image);

            Assert.NotNull(result.InventoryTile);
            Assert.Contains("inventory", PaimonMenuDetector.Normalize(result.InventoryTile.Label));
            Assert.NotNull(result.CharacterTile);
            Assert.Contains("character", PaimonMenuDetector.Normalize(result.CharacterTile.Label));
        }

        [Theory]
        [InlineData("menu_inventory_selected.png", "inventory")]
        [InlineData("menu_character_selected.png", "character")]
        [InlineData("menu_adventure_handbook_selected.png", "adventurerhandbook")]
        public void Detect_IdentifiesSelectedUpperViewportTile(string fileName, string expectedLabel)
        {
            using var image = PaimonMenuDetectorFixture.Load(fileName);
            PaimonMenuDetection result = fixture.Detector.Detect(image);

            Assert.True(result.HasUsableSelection, result.FailureReason + " " + result.Describe());
            Assert.Equal(expectedLabel, PaimonMenuDetector.Normalize(result.SelectedTile.Label));
        }

        [Fact]
        public void Detect_IdentifiesSelectedMiliastraShopInScrolledViewport()
        {
            using var image = PaimonMenuDetectorFixture.Load("menu_miliastra_shop_selected.png");
            PaimonMenuDetection result = fixture.Detector.Detect(image);

            Assert.True(result.HasUsableSelection, result.FailureReason + " " + result.Describe());
            Assert.Equal("miliastrashop", PaimonMenuDetector.Normalize(result.SelectedTile.Label));
            Assert.Null(result.InventoryTile);
        }

        [Fact]
        public void Detect_MissingMenuAndTargetFailsSafely()
        {
            using var image = new Bitmap(1919, 1079);
            using (Graphics graphics = Graphics.FromImage(image)) graphics.Clear(Color.Black);

            PaimonMenuDetection result = fixture.Detector.Detect(image);

            Assert.False(result.HasUsableSelection);
            Assert.Null(result.InventoryTile);
            Assert.NotNull(result.FailureReason);
        }

        [Fact]
        public void Detect_LowConfidenceInventoryLabelIsNotAcceptedAsTarget()
        {
            using var image = PaimonMenuDetectorFixture.Load("menu_initial.png");
            PaimonMenuDetection baseline = fixture.Detector.Detect(image);
            Assert.NotNull(baseline.InventoryTile);

            Rectangle inventoryLabel = baseline.InventoryTile.LabelBounds;
            inventoryLabel.Offset(
                -(int)(image.Width * 0.075),
                -(int)(image.Height * 0.28));
            var lowConfidenceOcr = new StubPositionalOcrService(
                new PositionalOcrResult("Inventory", inventoryLabel, 0.10f));
            var detector = new PaimonMenuDetector(lowConfidenceOcr, new ImageProcessor());

            PaimonMenuDetection result = detector.Detect(image);

            Assert.Null(result.InventoryTile);
        }

        [Fact]
        public void InventoryDestinationVerifier_AcceptsInventoryAndRejectsCharacter()
        {
            using var inventory = PaimonMenuDetectorFixture.Load("inventory_open.png");
            using var character = PaimonMenuDetectorFixture.Load("character_open.png");

            InventoryScreenDetection inventoryResult = fixture.InventoryScreenDetector.Detect(inventory);
            InventoryScreenDetection characterResult = fixture.InventoryScreenDetector.Detect(character);

            Assert.True(inventoryResult.IsInventoryOpen, $"OCR: {inventoryResult.RawText}");
            Assert.Equal("Weapons", inventoryResult.TabName);
            Assert.False(characterResult.IsInventoryOpen, $"OCR: {characterResult.RawText}");
        }

        [Fact]
        public void CharacterDestinationVerifier_AcceptsCharacterAndRejectsInventory()
        {
            using var character = PaimonMenuDetectorFixture.Load("character_open.png");
            using var inventory = PaimonMenuDetectorFixture.Load("inventory_open.png");

            CharacterScreenDetection characterResult = fixture.CharacterScreenDetector.Detect(character);
            CharacterScreenDetection inventoryResult = fixture.CharacterScreenDetector.Detect(inventory);

            Assert.True(characterResult.IsCharacterOpen,
                $"OCR: {characterResult.RawText}; confidence={characterResult.Confidence:P0}");
            Assert.False(inventoryResult.IsCharacterOpen,
                $"OCR: {inventoryResult.RawText}; confidence={inventoryResult.Confidence:P0}");
        }

        [Fact]
        public void CharacterDestinationVerifier_AcceptsRedactedLiveFailureCapture()
        {
            using var character = PaimonMenuDetectorFixture.Load("character_open_live_failure.png");

            CharacterScreenDetection result = fixture.CharacterScreenDetector.Detect(character);

            Assert.True(result.IsCharacterOpen,
                $"OCR: {result.RawText}; confidence={result.Confidence:P0}");
        }

        [Theory]
        [InlineData("Attributes", 0.10f)]
        [InlineData("", 0.95f)]
        public void CharacterDestinationVerifier_LowConfidenceOrMissingSignalFailsSafely(
            string text,
            float confidence)
        {
            using var screenshot = new Bitmap(1919, 1079);
            var detector = new CharacterScreenDetector(
                new StubOcrService(text, confidence),
                new ImageProcessor());

            CharacterScreenDetection result = detector.Detect(screenshot);

            Assert.False(result.IsCharacterOpen);
        }

        private sealed class StubPositionalOcrService : IPositionalOcrService
        {
            private readonly IReadOnlyList<PositionalOcrResult> results;

            public StubPositionalOcrService(params PositionalOcrResult[] results) => this.results = results;

            public IReadOnlyList<PositionalOcrResult> AnalyzeTextRegions(Bitmap bitmap) => results;
        }

        private sealed class StubOcrService : IOcrService
        {
            private readonly string text;
            private readonly float confidence;

            public StubOcrService(string text, float confidence)
            {
                this.text = text;
                this.confidence = confidence;
            }

            public void Restart() { }

            public string AnalyzeText(
                Bitmap bitmap,
                Tesseract.PageSegMode pageMode = Tesseract.PageSegMode.SingleLine,
                bool numbersOnly = false) => text;

            public (string Text, float Confidence) AnalyzeTextWithConfidence(
                Bitmap bitmap,
                Tesseract.PageSegMode pageMode = Tesseract.PageSegMode.SingleLine,
                bool numbersOnly = false) => (text, confidence);
        }
    }
}
