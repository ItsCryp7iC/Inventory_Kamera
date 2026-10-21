using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementSearchEditorDetectorTests
    {
        private static readonly Size FixtureSize = new(1919, 1079);

        [Theory]
        [InlineData("achievement_search_active_you_there_01.png")]
        [InlineData("achievement_search_active_pur_controller_01.png")]
        public void BrightFocusedSearchBorder_IsPositiveEvidence(string fixtureName)
        {
            using Bitmap screenshot = BuildComposite(fixtureName);
            var detector = new AchievementSearchEditorDetector();

            AchievementSearchEditorDetection result = detector.Detect(screenshot, Active(1));

            Assert.Equal(AchievementSearchEditorState.Focused, result.State);
            Assert.True(result.BrightestBorderRowRatio >= 0.70, result.Evidence);
        }

        [Theory]
        [InlineData("achievement_search_idle_keyboard_01.png")]
        [InlineData("achievement_search_idle_controller_01.png")]
        public void IdleSearchBorder_IsNotFocused(string fixtureName)
        {
            using Bitmap screenshot = BuildComposite(fixtureName);
            var detector = new AchievementSearchEditorDetector();

            AchievementSearchEditorDetection result = detector.Detect(screenshot, Idle());

            Assert.Equal(AchievementSearchEditorState.NotFocused, result.State);
            Assert.True(result.BrightestBorderRowRatio <= 0.10, result.Evidence);
        }

        [Fact]
        public void BrightBorderWithoutAchievementContext_IsUnknown()
        {
            using Bitmap screenshot = BuildComposite("achievement_search_active_you_there_01.png");
            var detector = new AchievementSearchEditorDetector();

            AchievementSearchEditorDetection result = detector.Detect(screenshot, NotAchievement());

            Assert.Equal(AchievementSearchEditorState.Unknown, result.State);
        }

        private static Bitmap BuildComposite(string fixtureName)
        {
            var screenshot = new Bitmap(FixtureSize.Width, FixtureSize.Height);
            using Graphics graphics = Graphics.FromImage(screenshot);
            graphics.Clear(Color.FromArgb(34, 44, 77));
            AchievementScreenRegions regions = AchievementScreenRegions.For(FixtureSize);
            using var crop = new Bitmap(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Achievements",
                fixtureName));
            graphics.DrawImageUnscaled(crop, regions.SearchField.Location);
            return screenshot;
        }

        private static AchievementScreenDetection Idle() => AchievementScreenDetector.Classify(
            FixtureSize,
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(idlePlaceholderAccepted: true),
            AchievementTextEvidence.ForTest("Achievements completed: 1", true),
            AchievementMatchCountParser.Parse(string.Empty),
            AchievementPageCounterParser.Parse(string.Empty));

        private static AchievementScreenDetection Active(int count) => AchievementScreenDetector.Classify(
            FixtureSize,
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(activeControlsAccepted: true),
            AchievementTextEvidence.ForTest("Achievements completed: 1", true),
            AchievementMatchCountParser.Parse($"Match(es): {count}"),
            AchievementPageCounterParser.Parse("1/1"));

        private static AchievementScreenDetection NotAchievement() => AchievementScreenDetector.Classify(
            FixtureSize,
            AchievementTextEvidence.ForTest(string.Empty, false),
            AchievementSearchFieldEvidence.ForTest(),
            AchievementTextEvidence.ForTest(string.Empty, false),
            AchievementMatchCountParser.Parse(string.Empty),
            AchievementPageCounterParser.Parse(string.Empty));
    }
}
