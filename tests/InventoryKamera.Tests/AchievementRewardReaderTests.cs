using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Tesseract;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    public sealed class AchievementRewardReaderTests
    {
        private static readonly Size FixtureSize = new(1919, 1079);
        private readonly ITestOutputHelper output;

        public AchievementRewardReaderTests(ITestOutputHelper output) => this.output = output;

        [Theory]
        [InlineData("5", 5)]
        [InlineData(" 10\r\n", 10)]
        [InlineData("20", 20)]
        [InlineData("999", 999)]
        public void StrictParserAcceptsOneBoundedPositiveInteger(string text, int expected)
        {
            AchievementRewardParseResult result = AchievementRewardParser.Parse(text);

            Assert.True(result.Accepted);
            Assert.Equal(expected, result.Value);
            Assert.Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), result.NormalizedText);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("5 10")]
        [InlineData("-5")]
        [InlineData("5.0")]
        [InlineData("0")]
        [InlineData("1000")]
        [InlineData("999999999999999999999999")]
        [InlineData("reward 5")]
        [InlineData("garbage")]
        public void StrictParserRejectsUnsupportedOrAmbiguousText(string text)
        {
            AchievementRewardParseResult result = AchievementRewardParser.Parse(text);

            Assert.False(result.Accepted);
            Assert.Null(result.Value);
        }

        [Fact]
        public void ReaderUsesThreeBoundedVariantsAndSelectsHighestConfidenceAgreement()
        {
            var ocr = new SequenceOcrService(
                ("20", 0.72f),
                ("20", 0.91f),
                ("", 0.0f));
            var reader = new AchievementRewardReader(ocr, new ImageProcessor());
            using var screenshot = new Bitmap(FixtureSize.Width, FixtureSize.Height);

            AchievementRewardResult result = reader.Read(screenshot);

            Assert.Equal(20, result.RewardValue);
            Assert.Equal(3, result.Attempts.Count);
            Assert.Equal(AchievementRewardOcrVariant.GrayscaleContrastInverted, result.SelectedAttempt.Variant);
            Assert.All(ocr.NumbersOnlyValues, Assert.True);
            Assert.Contains("rewardValue=20", result.Diagnostics);
        }

        [Fact]
        public void ReaderRejectsConflictingStrongValuesAndLowConfidenceValues()
        {
            using var screenshot = new Bitmap(FixtureSize.Width, FixtureSize.Height);
            var conflict = new AchievementRewardReader(
                new SequenceOcrService(("5", 0.90f), ("10", 0.90f), ("", 0.0f)),
                new ImageProcessor());
            var weak = new AchievementRewardReader(
                new SequenceOcrService(("20", 0.34f), ("", 0.0f), ("", 0.0f)),
                new ImageProcessor());

            Assert.Null(conflict.Read(screenshot).RewardValue);
            Assert.Null(weak.Read(screenshot).RewardValue);
        }

        [Theory]
        [InlineData("achievement_completion_progress_0_1_01.png", 20)]
        [InlineData("achievement_completion_claim_keyboard_01.png", 5)]
        public void RealSanitizedCompletionFixtures_ReadVisibleReward(string fixtureName, int expected)
        {
            using var ocr = new OcrService(engineCount: 1);
            ocr.Restart();
            var reader = new AchievementRewardReader(ocr, new ImageProcessor());
            using Bitmap screenshot = BuildComposite(fixtureName);

            AchievementRewardResult result = reader.Read(screenshot);

            output.WriteLine(result.Diagnostics);
            Assert.Equal(expected, result.RewardValue);
            Assert.NotNull(result.SelectedAttempt);
            Assert.True(result.SelectedAttempt.Confidence >= AchievementRewardReader.MinimumEvidenceConfidence);
        }

        private static Bitmap BuildComposite(string fixtureName)
        {
            var screenshot = new Bitmap(FixtureSize.Width, FixtureSize.Height);
            using Graphics graphics = Graphics.FromImage(screenshot);
            graphics.Clear(Color.FromArgb(34, 44, 77));
            Rectangle region = AchievementScreenRegions.For(FixtureSize).FirstResultCompletionState;
            using var crop = new Bitmap(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Achievements",
                fixtureName));
            Assert.Equal(region.Size, crop.Size);
            graphics.DrawImageUnscaled(crop, region.Location);
            return screenshot;
        }

        private sealed class SequenceOcrService : IOcrService
        {
            private readonly Queue<(string Text, float Confidence)> results;

            internal SequenceOcrService(params (string Text, float Confidence)[] results) =>
                this.results = new Queue<(string Text, float Confidence)>(results);

            internal List<bool> NumbersOnlyValues { get; } = new();

            public void Restart() { }

            public string AnalyzeText(
                Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false) =>
                AnalyzeTextWithConfidence(bitmap, pageMode, numbersOnly).Text;

            public (string Text, float Confidence) AnalyzeTextWithConfidence(
                Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false)
            {
                NumbersOnlyValues.Add(numbersOnly);
                return results.Dequeue();
            }
        }
    }
}
