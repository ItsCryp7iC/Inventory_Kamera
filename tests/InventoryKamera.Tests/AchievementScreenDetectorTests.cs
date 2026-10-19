using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    public sealed class AchievementScreenDetectorFixture : IDisposable
    {
        private readonly OcrService ocr = new OcrService(engineCount: 1);

        public AchievementScreenDetectorFixture()
        {
            ocr.Restart();
            Detector = new AchievementScreenDetector(ocr, new ImageProcessor());
        }

        internal AchievementScreenDetector Detector { get; }

        internal static Bitmap LoadPaimonFixture(string name) => new Bitmap(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "PaimonMenu",
            name));

        internal static Bitmap LoadAchievementFixture(string name) => new Bitmap(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Achievements",
            name));

        public void Dispose() => ocr.Dispose();
    }

    public sealed class AchievementScreenDetectorTests : IClassFixture<AchievementScreenDetectorFixture>
    {
        private static readonly Size FullHd = new Size(1920, 1080);
        private static readonly Size FixtureSourceSize = new Size(1919, 1079);
        private readonly AchievementScreenDetectorFixture fixture;
        private readonly ITestOutputHelper output;

        public AchievementScreenDetectorTests(
            AchievementScreenDetectorFixture fixture,
            ITestOutputHelper output)
        {
            this.fixture = fixture;
            this.output = output;
        }

        public static IEnumerable<object[]> LiveAchievementStates()
        {
            yield return new object[]
            {
                "Idle keyboard",
                "achievement_heading_idle_keyboard_01.png",
                "achievement_search_idle_keyboard_01.png",
                "achievement_completed_idle_keyboard_01.png",
                null,
                null,
                "Idle",
                null,
                null,
            };
            yield return new object[]
            {
                "Exact You There! Fly!",
                "achievement_heading_search_keyboard_01.png",
                "achievement_search_active_you_there_01.png",
                "achievement_completed_search_keyboard_01.png",
                "achievement_match_count_1_keyboard_01.png",
                "achievement_page_1_1_keyboard_01.png",
                "SearchActive",
                1,
                1,
            };
            yield return new object[]
            {
                "Exact Moonmist Treasure",
                "achievement_heading_search_keyboard_01.png",
                "achievement_search_active_moonmist_01.png",
                "achievement_completed_search_keyboard_01.png",
                "achievement_match_count_1_keyboard_01.png",
                "achievement_page_1_1_keyboard_01.png",
                "SearchActive",
                1,
                1,
            };
            yield return new object[]
            {
                "Idle controller",
                "achievement_heading_idle_controller_01.png",
                "achievement_search_idle_controller_01.png",
                "achievement_completed_idle_controller_01.png",
                null,
                null,
                "Idle",
                null,
                null,
            };
            yield return new object[]
            {
                "Controller partial Pur",
                "achievement_heading_search_controller_01.png",
                "achievement_search_active_pur_controller_01.png",
                "achievement_completed_search_controller_01.png",
                "achievement_match_count_5_controller_01.png",
                "achievement_page_1_1_controller_01.png",
                "SearchActive",
                5,
                1,
            };
        }

        [Fact]
        public void IdleAchievementScreen_RequiresMultiplePositiveSignalsAndIdleEvidence()
        {
            AchievementScreenDetection result = Classify(
                heading: true,
                completed: true,
                search: AchievementSearchFieldEvidence.ForTest(
                    idlePlaceholderAccepted: true,
                    rawText: "Search for Achievements..."));

            Assert.Equal(AchievementScreenState.Idle, result.ScreenState);
            Assert.True(result.AchievementHeadingAccepted);
            Assert.True(result.SearchFieldAccepted);
            Assert.True(result.CompletedCountAccepted);
            Assert.Equal(AchievementResultCountState.NoValidCount, result.ResultCountState);
        }

        [Theory]
        [InlineData(1, "SingleResult")]
        [InlineData(5, "MultipleResults")]
        [InlineData(0, "NoResults")]
        public void ActiveAchievementSearch_UsesValidMatchCount(
            int count,
            string expectedCountState)
        {
            AchievementScreenDetection result = Classify(
                heading: true,
                completed: true,
                search: AchievementSearchFieldEvidence.ForTest(
                    activeControlsAccepted: true,
                    rawText: "Clear Search"),
                matchText: $"Match(es): {count}",
                pageText: count == 0 ? string.Empty : "1/1");

            Assert.Equal(AchievementScreenState.SearchActive, result.ScreenState);
            Assert.Equal(count, result.MatchCountValue);
            Assert.Equal(
                Enum.Parse<AchievementResultCountState>(expectedCountState),
                result.ResultCountState);
        }

        [Fact]
        public void ControllerIdleScreen_UsesSemanticSearchPromptWithoutButtonLetter()
        {
            AchievementScreenDetection result = Classify(
                heading: true,
                completed: true,
                search: AchievementSearchFieldEvidence.ForTest(
                    idleControllerPromptAccepted: true,
                    rawText: "Search"));

            Assert.Equal(AchievementScreenState.Idle, result.ScreenState);
        }

        [Fact]
        public void ControllerActiveSearch_MatchCountWinsOverWeakSearchPrompt()
        {
            AchievementScreenDetection result = Classify(
                heading: true,
                completed: true,
                search: AchievementSearchFieldEvidence.ForTest(
                    idleControllerPromptAccepted: true,
                    rawText: "Search"),
                matchText: "Match(es): 5",
                pageText: "1/1");

            Assert.Equal(AchievementScreenState.SearchActive, result.ScreenState);
            Assert.Equal(5, result.MatchCountValue);
        }

        [Fact]
        public void StrongIdleAndActiveEvidenceConflict_ReturnsUnknown()
        {
            AchievementScreenDetection result = Classify(
                heading: true,
                completed: true,
                search: AchievementSearchFieldEvidence.ForTest(
                    idlePlaceholderAccepted: true,
                    rawText: "Search for Achievements..."),
                matchText: "Match(es): 1",
                pageText: "1/1");

            Assert.Equal(AchievementScreenState.Unknown, result.ScreenState);
            Assert.Contains("conflict", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void HeadingAlone_DoesNotClassifyAchievementScreen()
        {
            AchievementScreenDetection result = Classify(heading: true);

            Assert.Equal(AchievementScreenState.Unknown, result.ScreenState);
        }

        [Fact]
        public void NoEvidence_ReturnsNotAchievementScreen()
        {
            AchievementScreenDetection result = Classify();

            Assert.Equal(AchievementScreenState.NotAchievementScreen, result.ScreenState);
        }

        [Fact]
        public void DiagnosticsContainBoundedStructuredEvidence()
        {
            AchievementScreenDetection result = Classify(
                heading: true,
                completed: true,
                search: AchievementSearchFieldEvidence.ForTest(
                    activeControlsAccepted: true,
                    rawText: "Clear Search"),
                matchText: "Match(es): 1",
                pageText: "1/1");

            Assert.Contains("screenState=SearchActive", result.Diagnostics);
            Assert.Contains("achievementHeading:", result.Diagnostics);
            Assert.Contains("searchField:", result.Diagnostics);
            Assert.Contains("completedCount:", result.Diagnostics);
            Assert.Contains("matchCount:", result.Diagnostics);
            Assert.Contains("pageCounter:", result.Diagnostics);
            Assert.Contains("finalState=SearchActive", result.Diagnostics);
            Assert.True(result.Diagnostics.Length < 4096);
        }

        [Theory]
        [MemberData(nameof(LiveAchievementStates))]
        public void RealSanitizedCrops_DriveCompleteDetectorForLiveStates(
            string stateName,
            string headingFixture,
            string searchFixture,
            string completedFixture,
            string matchFixture,
            string pageFixture,
            string expectedState,
            int? expectedMatchCount,
            int? expectedPage)
        {
            using Bitmap screenshot = BuildAchievementComposite(
                headingFixture,
                searchFixture,
                completedFixture,
                matchFixture,
                pageFixture);

            AchievementScreenDetection result = fixture.Detector.Detect(screenshot);

            WriteRealOcrEvidence(stateName, result);
            Assert.Equal(Enum.Parse<AchievementScreenState>(expectedState), result.ScreenState);
            Assert.Equal(expectedMatchCount, result.MatchCountValue);
            Assert.Equal(expectedPage, result.ResultPageCurrent);
            Assert.Equal(expectedPage, result.ResultPageTotal);
            Assert.True(result.AchievementHeadingAccepted, result.Diagnostics);
            Assert.True(result.SearchFieldAccepted, result.Diagnostics);
            Assert.True(result.CompletedCountAccepted, result.Diagnostics);
        }

        [Fact]
        public void GeometryOnlyFixtures_MatchCalibratedTitleAndCompletionRegions()
        {
            AchievementScreenRegions regions = AchievementScreenRegions.For(FixtureSourceSize);
            string[] titleFixtures =
            {
                "achievement_result_title_you_there_01.png",
                "achievement_result_title_moonmist_01.png",
                "achievement_result_title_pur_01.png",
            };
            string[] completionFixtures =
            {
                "achievement_completion_claim_keyboard_01.png",
                "achievement_completion_completed_controller_01.png",
            };

            foreach (string fixtureName in titleFixtures)
            {
                using Bitmap crop = AchievementScreenDetectorFixture.LoadAchievementFixture(fixtureName);
                Assert.Equal(regions.FirstResultTitle.Size, crop.Size);
            }
            foreach (string fixtureName in completionFixtures)
            {
                using Bitmap crop = AchievementScreenDetectorFixture.LoadAchievementFixture(fixtureName);
                Assert.Equal(regions.FirstResultCompletionState.Size, crop.Size);
            }
        }

        [Fact]
        public void ScreenshotPipeline_CollectsStructuredIdleEvidence()
        {
            using var screenshot = new Bitmap(1920, 1080);
            var detector = new AchievementScreenDetector(
                new SequenceOcrService(
                    "Achievements",
                    "Search for Achievements...",
                    "Achievements completed: 123",
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty),
                new ImageProcessor());

            AchievementScreenDetection result = detector.Detect(screenshot);

            Assert.Equal(AchievementScreenState.Idle, result.ScreenState);
            Assert.Single(result.AchievementHeading.Attempts);
            Assert.Single(result.SearchField.Text.Attempts);
            Assert.Single(result.CompletedCount.Attempts);
            Assert.False(result.MatchCountAccepted);
            Assert.False(result.ResultPageAccepted);
            Assert.Equal(2, result.MatchCountEvidence.Attempts.Count);
            Assert.Equal(2, result.ResultPageEvidence.Attempts.Count);
        }

        [Fact]
        public void ScreenshotPipeline_CollectsStructuredActiveEvidence()
        {
            using var screenshot = new Bitmap(1920, 1080);
            var detector = new AchievementScreenDetector(
                new SequenceOcrService(
                    "Achievements",
                    "Clear Search",
                    "Achievements completed: 123",
                    "Match(es): 5",
                    "1/1"),
                new ImageProcessor());

            AchievementScreenDetection result = detector.Detect(screenshot);

            Assert.Equal(AchievementScreenState.SearchActive, result.ScreenState);
            Assert.Equal(5, result.MatchCountValue);
            Assert.Equal(1, result.ResultPageCurrent);
            Assert.Equal(1, result.ResultPageTotal);
        }

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(1280, 720)]
        [InlineData(2560, 1440)]
        [InlineData(1920, 1200)]
        public void CaptureRegionsScaleFromNormalizedGeometry(int width, int height)
        {
            var size = new Size(width, height);
            AchievementScreenRegions regions = AchievementScreenRegions.For(size);

            AssertNormalized(regions.Heading, size, 100.0 / 1919.0, 15.0 / 1079.0, 265.0 / 1919.0, 77.0 / 1079.0);
            AssertNormalized(regions.SearchField, size, 55.0 / 1919.0, 105.0 / 1079.0, 625.0 / 1919.0, 65.0 / 1079.0);
            AssertNormalized(regions.CompletedCount, size, 1380.0 / 1919.0, 15.0 / 1079.0, 430.0 / 1919.0, 75.0 / 1079.0);
            AssertNormalized(regions.MatchCount, size, 730.0 / 1919.0, 130.0 / 1079.0, 305.0 / 1919.0, 65.0 / 1079.0);
            AssertNormalized(regions.PageCounter, size, 1670.0 / 1919.0, 140.0 / 1079.0, 115.0 / 1919.0, 55.0 / 1079.0);
            AssertNormalized(regions.FirstResultTitle, size, 840.0 / 1919.0, 245.0 / 1079.0, 590.0 / 1919.0, 70.0 / 1079.0);
            AssertNormalized(regions.FirstResultCompletionState, size, 1545.0 / 1919.0, 235.0 / 1079.0, 290.0 / 1919.0, 120.0 / 1079.0);
            AssertNormalized(regions.FirstResultCompletionLabel, size, 1670.0 / 1919.0, 280.0 / 1079.0, 150.0 / 1919.0, 50.0 / 1079.0);
            AssertNormalized(regions.FirstResultRewardValue, size, 1585.0 / 1919.0, 319.0 / 1079.0, 65.0 / 1919.0, 35.0 / 1079.0);
        }

        [Fact]
        public void SourceResolutionRegions_MatchMeasuredFixtureBounds()
        {
            AchievementScreenRegions regions = AchievementScreenRegions.For(FixtureSourceSize);

            Assert.Equal(Rectangle.FromLTRB(100, 15, 365, 92), regions.Heading);
            Assert.Equal(Rectangle.FromLTRB(55, 105, 680, 170), regions.SearchField);
            Assert.Equal(Rectangle.FromLTRB(1380, 15, 1810, 90), regions.CompletedCount);
            Assert.Equal(Rectangle.FromLTRB(730, 130, 1035, 195), regions.MatchCount);
            Assert.Equal(Rectangle.FromLTRB(1670, 140, 1785, 195), regions.PageCounter);
            Assert.Equal(Rectangle.FromLTRB(840, 245, 1430, 315), regions.FirstResultTitle);
            Assert.Equal(Rectangle.FromLTRB(1545, 235, 1835, 355), regions.FirstResultCompletionState);
            Assert.Equal(Rectangle.FromLTRB(1670, 280, 1820, 330), regions.FirstResultCompletionLabel);
            Assert.Equal(Rectangle.FromLTRB(1585, 319, 1650, 354), regions.FirstResultRewardValue);
            Assert.True(regions.FirstResultCompletionState.Contains(regions.FirstResultRewardValue));
        }

        [Theory]
        [InlineData("inventory_open.png")]
        [InlineData("character_open.png")]
        [InlineData("menu_initial.png")]
        public void KnownNonAchievementFixtures_AreNotPositivelyClassified(string fixtureName)
        {
            using Bitmap screenshot = AchievementScreenDetectorFixture.LoadPaimonFixture(fixtureName);

            AchievementScreenDetection result = fixture.Detector.Detect(screenshot);

            Assert.True(
                result.ScreenState == AchievementScreenState.NotAchievementScreen ||
                result.ScreenState == AchievementScreenState.Unknown,
                result.Diagnostics);
        }

        [Theory]
        [InlineData("inventory_open.png")]
        [InlineData("character_open.png")]
        [InlineData("menu_initial.png")]
        public void ExistingSanitizedNegativeFixture_LoadsAndRegionsRemainNarrow(string fixtureName)
        {
            using Bitmap screenshot = AchievementScreenDetectorFixture.LoadPaimonFixture(fixtureName);

            Assert.True(screenshot.Width > 0);
            Assert.True(screenshot.Height > 0);
            AchievementScreenRegions regions = AchievementScreenRegions.For(screenshot.Size);
            Assert.True(regions.Heading.Width < screenshot.Width / 2);
            Assert.True(regions.MatchCount.Height < screenshot.Height / 4);
        }

        private static Bitmap BuildAchievementComposite(
            string headingFixture,
            string searchFixture,
            string completedFixture,
            string matchFixture,
            string pageFixture)
        {
            var screenshot = new Bitmap(
                FixtureSourceSize.Width,
                FixtureSourceSize.Height,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(screenshot))
            {
                graphics.Clear(Color.FromArgb(34, 44, 77));
                AchievementScreenRegions regions = AchievementScreenRegions.For(FixtureSourceSize);
                DrawFixture(graphics, headingFixture, regions.Heading);
                DrawFixture(graphics, searchFixture, regions.SearchField);
                DrawFixture(graphics, completedFixture, regions.CompletedCount);
                if (matchFixture != null) DrawFixture(graphics, matchFixture, regions.MatchCount);
                if (pageFixture != null) DrawFixture(graphics, pageFixture, regions.PageCounter);
            }
            return screenshot;
        }

        private static void DrawFixture(Graphics graphics, string fixtureName, Rectangle region)
        {
            using Bitmap crop = AchievementScreenDetectorFixture.LoadAchievementFixture(fixtureName);
            Assert.Equal(region.Size, crop.Size);
            graphics.DrawImageUnscaled(crop, region.Location);
        }

        private void WriteRealOcrEvidence(string stateName, AchievementScreenDetection result)
        {
            output.WriteLine("State fixture: {0}", stateName);
            WriteAttempts("heading", result.AchievementHeading);
            WriteAttempts("search", result.SearchField.Text);
            WriteAttempts("completed", result.CompletedCount);
            WriteAttempts("match", result.MatchCountEvidence);
            WriteAttempts("page", result.ResultPageEvidence);
            output.WriteLine(
                "Detector: state={0}; match={1}; page={2}/{3}",
                result.ScreenState,
                result.MatchCountValue?.ToString() ?? "(none)",
                result.ResultPageCurrent?.ToString() ?? "(none)",
                result.ResultPageTotal?.ToString() ?? "(none)");
        }

        private void WriteAttempts(string regionName, AchievementTextEvidence evidence)
        {
            foreach (AchievementOcrAttempt attempt in evidence.Attempts)
            {
                output.WriteLine(
                    "{0}: variant={1}; raw=\"{2}\"; normalized=\"{3}\"; confidence={4:0.000}; accepted={5}; reason={6}",
                    regionName,
                    attempt.Inverted ? "grayscale-contrast-inverted" : "grayscale-contrast",
                    attempt.RawText.Replace("\r", " ").Replace("\n", " "),
                    attempt.NormalizedText,
                    attempt.Confidence,
                    attempt.Accepted,
                    attempt.Reason);
            }
        }

        private static AchievementScreenDetection Classify(
            bool heading = false,
            bool completed = false,
            AchievementSearchFieldEvidence search = null,
            string matchText = "",
            string pageText = "") =>
            AchievementScreenDetector.Classify(
                FullHd,
                AchievementTextEvidence.ForTest("Achievements", heading),
                search ?? AchievementSearchFieldEvidence.ForTest(),
                AchievementTextEvidence.ForTest("Achievements completed: 123", completed),
                AchievementMatchCountParser.Parse(matchText),
                AchievementPageCounterParser.Parse(pageText));

        private static void AssertNormalized(
            Rectangle actual,
            Size size,
            double x,
            double y,
            double width,
            double height)
        {
            Assert.InRange(actual.Left, (int)Math.Round(size.Width * x) - 1, (int)Math.Round(size.Width * x) + 1);
            Assert.InRange(actual.Top, (int)Math.Round(size.Height * y) - 1, (int)Math.Round(size.Height * y) + 1);
            Assert.InRange(actual.Right, (int)Math.Round(size.Width * (x + width)) - 1, (int)Math.Round(size.Width * (x + width)) + 1);
            Assert.InRange(actual.Bottom, (int)Math.Round(size.Height * (y + height)) - 1, (int)Math.Round(size.Height * (y + height)) + 1);
            Assert.True(actual.Left >= 0 && actual.Top >= 0);
            Assert.True(actual.Right <= size.Width && actual.Bottom <= size.Height);
        }

        private sealed class SequenceOcrService : IOcrService
        {
            private readonly Queue<string> reads;

            internal SequenceOcrService(params string[] reads) =>
                this.reads = new Queue<string>(reads ?? Array.Empty<string>());

            public void Restart() { }

            public string AnalyzeText(
                Bitmap bitmap,
                Tesseract.PageSegMode pageMode = Tesseract.PageSegMode.SingleLine,
                bool numbersOnly = false) => AnalyzeTextWithConfidence(bitmap, pageMode, numbersOnly).Text;

            public (string Text, float Confidence) AnalyzeTextWithConfidence(
                Bitmap bitmap,
                Tesseract.PageSegMode pageMode = Tesseract.PageSegMode.SingleLine,
                bool numbersOnly = false)
            {
                if (reads.Count == 0)
                    throw new InvalidOperationException("The detector performed an unexpected OCR read.");
                return (reads.Dequeue(), 0.95f);
            }
        }
    }

    public sealed class AchievementSearchParserTests
    {
        [Theory]
        [InlineData("Match(es): 1", 1)]
        [InlineData("Match(es) : 5", 5)]
        [InlineData("Matches; 0", 0)]
        [InlineData("Match es 25", 25)]
        [InlineData("Match(es): 5 Match(es): 5", 5)]
        public void MatchCount_AcceptsOnlyLabeledBoundedCounts(string raw, int expected)
        {
            AchievementMatchCountParseResult result = AchievementMatchCountParser.Parse(raw);

            Assert.True(result.Accepted, result.Reason);
            Assert.Equal(expected, result.Value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("Match(es):")]
        [InlineData("Results: 5")]
        [InlineData("5 unrelated")]
        [InlineData("Match(es): -1")]
        [InlineData("Match(es): 10000")]
        [InlineData("Match(es): 1 Match(es): 5")]
        public void MatchCount_RejectsMalformedUnboundedOrConflictingText(string raw)
        {
            AchievementMatchCountParseResult result = AchievementMatchCountParser.Parse(raw);

            Assert.False(result.Accepted);
            Assert.Null(result.Value);
        }

        [Theory]
        [InlineData("1/1", 1, 1)]
        [InlineData(" 2 / 5 ", 2, 5)]
        [InlineData("1|3", 1, 3)]
        [InlineData("Page 1/1 Page 1/1", 1, 1)]
        public void PageCounter_AcceptsBoundedUnambiguousPages(
            string raw,
            int expectedCurrent,
            int expectedTotal)
        {
            AchievementPageCounterParseResult result = AchievementPageCounterParser.Parse(raw);

            Assert.True(result.Accepted, result.Reason);
            Assert.Equal(expectedCurrent, result.Current);
            Assert.Equal(expectedTotal, result.Total);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("0/1")]
        [InlineData("2/1")]
        [InlineData("1/10000")]
        [InlineData("1/1 1/2")]
        public void PageCounter_RejectsMalformedOutOfRangeOrConflictingText(string raw)
        {
            AchievementPageCounterParseResult result = AchievementPageCounterParser.Parse(raw);

            Assert.False(result.Accepted);
            Assert.Null(result.Current);
            Assert.Null(result.Total);
        }
    }
}
