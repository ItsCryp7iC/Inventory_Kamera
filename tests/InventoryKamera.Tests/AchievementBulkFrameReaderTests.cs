using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Tesseract;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    public sealed class AchievementBulkFrameReaderTests
    {
        private readonly ITestOutputHelper output;

        public AchievementBulkFrameReaderTests(ITestOutputHelper output) => this.output = output;

        [Theory]
        [InlineData(1920, 1080)]
        [InlineData(1600, 1000)]
        public void ListGeometry_ScalesAcrossSupportedAspectRatios_WithoutRowLeakage(int width, int height)
        {
            AchievementListRegions regions = AchievementListRegions.For(new Size(width, height));

            Assert.Equal(AchievementListRegions.SlotCount, regions.Rows.Count);
            for (int index = 0; index < regions.Rows.Count; index++)
            {
                AchievementListRowGeometry row = regions.Rows[index];
                Assert.True(row.Bounds.Contains(row.Title));
                Assert.True(row.Bounds.Contains(row.Description));
                Assert.True(row.Bounds.Contains(row.CompletionState));
                Assert.True(row.Bounds.Contains(row.RewardValue));
                Assert.False(row.Title.IntersectsWith(row.Description));
                Assert.False(row.Title.IntersectsWith(row.CompletionState));
                if (index > 0)
                    Assert.True(regions.Rows[index - 1].Bounds.Bottom < row.Bounds.Top);
            }
        }

        [Fact]
        public void RowDetector_UsesPositiveGeometryEvidence_AndIgnoresEmptySlots()
        {
            using var frame = new Bitmap(1920, 1080);
            using Graphics graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.FromArgb(238, 235, 225));
            AchievementListRegions regions = AchievementListRegions.For(frame.Size);
            foreach (int index in new[] { 0, 2, 5 })
                DrawSyntheticRow(graphics, regions.Rows[index]);

            AchievementVisibleRowDetection result = new AchievementVisibleRowDetector().Detect(frame);

            Assert.Equal(3, result.VisibleRows.Count);
            Assert.All(result.VisibleRows, row => Assert.Equal(
                AchievementVisibleRowVisibility.Complete, row.Visibility));
            Assert.Equal(new[] { 145, 395, 771 }, result.VisibleRows.Select(row => row.Bounds.Top));
        }

        [Theory]
        [InlineData("real_list_standard_top.png", 7, 1, 0)]
        [InlineData("real_list_standard_scrolled.png", 7, 1, 0)]
        [InlineData("real_list_summary.png", 6, 2, 1)]
        [InlineData("real_list_summary_scrolled.png", 6, 1, 1)]
        public void RealSanitizedPanels_DetectRowsAndExcludeSummaryHeader(
            string fixtureName, int expectedRows, int expectedPartial, int expectedHeaders)
        {
            using Bitmap frame = BuildPanelComposite(fixtureName);

            AchievementVisibleRowDetection result = new AchievementVisibleRowDetector().Detect(frame);

            output.WriteLine(result.Diagnostics);
            Assert.Equal(expectedRows, result.VisibleRows.Count);
            Assert.Equal(expectedPartial, result.VisibleRows.Count(row =>
                row.Visibility != AchievementVisibleRowVisibility.Complete));
            Assert.Equal(expectedHeaders, result.Slots.Count(slot => slot.SummaryHeader));
        }

        [Fact]
        public void RealSanitizedPanel_TitleAndCompletionUseProductionOcr()
        {
            using Bitmap frame = BuildPanelComposite("real_list_standard_top.png");
            AchievementVisibleRowGeometryAssert(frame, out AchievementListRowGeometry first,
                out AchievementListRowGeometry completed);
            using var ocr = new OcrService(engineCount: 1);
            ocr.Restart();
            var preprocessor = new ImageProcessor();
            var titleReader = new AchievementListRowTitleReader(ocr, preprocessor);
            var completionReader = new AchievementListRowCompletionReader(ocr, preprocessor);

            AchievementResultTitleRead title = titleReader.Read(frame, first);
            AchievementCompletionRead incomplete = completionReader.Read(frame, first);
            AchievementCompletionRead claimed = completionReader.Read(frame, completed);
            var identityResolver = new AchievementVisibleRowIdentityResolver(new AchievementCatalogProvider());
            AchievementIdentityResult identity = identityResolver.Resolve(title);
            AchievementCompletionResult incompleteState =
                AchievementVisibleRowCompletionResolver.Resolve(identity, incomplete.Attempts);

            foreach (AchievementTitleOcrAttempt attempt in title.Attempts)
                output.WriteLine($"title raw={attempt.RawText}; confidence={attempt.Confidence:0.000}");
            Assert.Contains(title.Attempts, attempt =>
                attempt.Confidence >= AchievementTitleMatcher.MinimumOcrConfidence &&
                AchievementTitleMatcher.Similarity(attempt.NormalizedText, "Magnum Opus") >=
                    AchievementTitleMatcher.MinimumScore);
            Assert.Contains(incomplete.Attempts, attempt => attempt.Progress.Accepted &&
                attempt.Progress.Current == 0 && attempt.Progress.Target == 1);
            Assert.Contains(claimed.Attempts, attempt => attempt.CompletedTokenDetected &&
                attempt.Confidence >= AchievementCompletionClassifier.MinimumEvidenceConfidence);
            Assert.Equal(AchievementIdentityState.StageGroupIdentified, identity.State);
            Assert.Equal("Magnum Opus", identity.ExpectedTitle);
            Assert.Equal(AchievementCompletionState.Incomplete, incompleteState.State);
            Assert.Equal((0, 1), (incompleteState.ProgressCurrent, incompleteState.ProgressTarget));
        }

        [Theory]
        [InlineData((int)AchievementVisibleRowVisibility.Partial)]
        [InlineData((int)AchievementVisibleRowVisibility.Uncertain)]
        public void ClippedOrUncertainRow_IsRetainedUnresolvedWithoutOcr(int visibilityValue)
        {
            AchievementVisibleRowVisibility visibility = (AchievementVisibleRowVisibility)visibilityValue;
            AchievementListRowGeometry row = AchievementListRegions.CreateDetectedRow(
                new Size(1920, 1080), 0, 895, 895, 991, visibility);
            var titleReader = new FakeTitleReader(new Dictionary<int, string>());
            var completionReader = new FakeCompletionReader(new Dictionary<int, string>());
            AchievementListFrameReader reader = CreateReader(new[] { row }, titleReader, completionReader);
            using var screenshot = new Bitmap(1920, 1080);

            AchievementVisibleRow result = Assert.Single(reader.Read(screenshot).VisibleRows);

            Assert.Equal(visibility, result.Visibility);
            Assert.Equal(AchievementIdentityState.Unresolved, result.IdentityState);
            Assert.Equal(AchievementCompletionState.Unresolved, result.CompletionState);
            Assert.Empty(titleReader.AttemptsPerRow);
            Assert.Empty(completionReader.ReadRows);
            Assert.False(result.SuitableForTrustedCompletionExtraction);
        }

        [Fact]
        public void RealSanitizedCompletedTotal_UsesProductionOcr()
        {
            using var frame = new Bitmap(1919, 1079);
            using (Graphics graphics = Graphics.FromImage(frame)) graphics.Clear(Color.White);
            using Bitmap fixture = LoadBulkFixture("real_completed_total_1769.png");
            using (Graphics graphics = Graphics.FromImage(frame))
                graphics.DrawImageUnscaled(fixture, 1370, 12);
            using var ocr = new OcrService(engineCount: 1);
            ocr.Restart();

            AchievementCompletedTotalResult result = new AchievementCompletedTotalReader(
                ocr, new ImageProcessor()).Read(frame);

            output.WriteLine(string.Join(Environment.NewLine, result.Attempts.Select(attempt =>
                $"raw={attempt.RawText}; confidence={attempt.Confidence:0.000}; value={attempt.Value}")));
            Assert.Equal(1769, result.CompletedTotal);
        }

        [Theory]
        [InlineData("Achievements completed: 17", 17)]
        [InlineData("Achievements completed: 1769", 1769)]
        [InlineData("Achievements complete: 17", null)]
        [InlineData("Achievements completed:", null)]
        [InlineData("", null)]
        public void CompletedTotalParser_IsPositiveAndMalformedNeverMeansZero(string raw, int? expected)
        {
            Assert.Equal(expected, AchievementCompletedTotalParser.Parse(raw));
        }

        [Fact]
        public void MultiRowFrame_ClassifiesEveryRowIndependently_FromOneScreenshot()
        {
            AchievementListRowGeometry[] rows = Rows(4);
            var titleReader = new FakeTitleReader(new Dictionary<int, string>
            {
                [0] = "Alpha",
                [1] = "Delta",
                [2] = "Epsilon",
                [3] = "Zeta",
            });
            var completionReader = new FakeCompletionReader(new Dictionary<int, string>
            {
                [0] = "Completed",
                [1] = "Claim",
                [2] = "0/1",
                [3] = "0/10",
            });
            AchievementListFrameReader reader = CreateReader(rows, titleReader, completionReader);
            using var screenshot = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(screenshot, TimeSpan.FromMilliseconds(4));

            Assert.Equal(AchievementScreenState.Idle, result.ScreenState);
            Assert.Equal(4, result.VisibleRows.Count);
            Assert.Equal(
                new[]
                {
                    AchievementCompletionState.CompletedClaimed,
                    AchievementCompletionState.CompletedUnclaimed,
                    AchievementCompletionState.Incomplete,
                    AchievementCompletionState.Incomplete,
                },
                result.VisibleRows.Select(row => row.CompletionState));
            Assert.Equal((0, 1), (result.VisibleRows[2].ProgressCurrent, result.VisibleRows[2].ProgressTarget));
            Assert.Equal((0, 10), (result.VisibleRows[3].ProgressCurrent, result.VisibleRows[3].ProgressTarget));
            Assert.Equal(4, result.Timing.RowsProcessed);
            Assert.Equal(TimeSpan.FromMilliseconds(4), result.Timing.Capture);
            Assert.Equal(1, titleReader.AttemptsPerRow.Values.Max());
        }

        [Fact]
        public void MixedIdentityFrame_PreservesUniqueStageUnresolvedAndCollisionStates()
        {
            AchievementListRowGeometry[] rows = Rows(4);
            var titleReader = new FakeTitleReader(new Dictionary<int, string>
            {
                [0] = "Alpha",
                [1] = "Beta",
                [2] = "Unreadable damage",
                [3] = "Gamma",
            });
            var completionReader = new FakeCompletionReader(new Dictionary<int, string>
            {
                [0] = "Completed",
                [1] = "Completed",
                [2] = "Completed",
                [3] = "Completed",
            });
            var rewardReader = new FakeRewardReader(5);
            AchievementListFrameReader reader = CreateReader(
                rows,
                titleReader,
                completionReader,
                rewardReader);
            using var screenshot = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(screenshot);

            Assert.Equal(
                new[]
                {
                    AchievementIdentityState.Identified,
                    AchievementIdentityState.StageGroupIdentified,
                    AchievementIdentityState.Unresolved,
                    AchievementIdentityState.Ambiguous,
                },
                result.VisibleRows.Select(row => row.IdentityState));
            Assert.Equal(2, result.IdentifiedRows.Count);
            Assert.Equal(2, result.UnresolvedRows.Count);
            Assert.Equal(2, completionReader.ReadRows.Count);
            Assert.Equal(1, rewardReader.ReadCount);
            Assert.True(result.VisibleRows[0].Fingerprint.SuitableForDeduplication);
            Assert.True(result.VisibleRows[1].Fingerprint.SuitableForDeduplication);
            Assert.False(result.VisibleRows[2].Fingerprint.SuitableForDeduplication);
            Assert.False(result.VisibleRows[3].Fingerprint.SuitableForDeduplication);
            Assert.Equal(new[] { 100, 200 }, AchievementTrustedCompletionExtractor.Extract(result.VisibleRows));
        }

        [Fact]
        public void OneRowOcrFailure_DoesNotInvalidateOtherVisibleRows()
        {
            AchievementListRowGeometry[] rows = Rows(3);
            var titleReader = new FakeTitleReader(new Dictionary<int, string>
            {
                [0] = "Alpha",
                [1] = "Delta",
                [2] = "Epsilon",
            });
            titleReader.ThrowRows.Add(1);
            AchievementListFrameReader reader = CreateReader(
                rows,
                titleReader,
                new FakeCompletionReader(new Dictionary<int, string>
                {
                    [0] = "Completed",
                    [1] = "Completed",
                    [2] = "0/1",
                }));
            using var screenshot = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(screenshot);

            Assert.Equal(3, result.VisibleRows.Count);
            Assert.Equal(AchievementIdentityState.Identified, result.VisibleRows[0].IdentityState);
            Assert.Equal(AchievementIdentityState.Unresolved, result.VisibleRows[1].IdentityState);
            Assert.Equal(AchievementIdentityState.Identified, result.VisibleRows[2].IdentityState);
            Assert.Contains("failed independently", result.VisibleRows[1].Diagnostics);
        }

        [Fact]
        public void PendingClaimStage_BlocksRewardAndTrustedStageInference()
        {
            AchievementListRowGeometry[] rows = Rows(1);
            var rewardReader = new FakeRewardReader(5);
            AchievementListFrameReader reader = CreateReader(
                rows,
                new FakeTitleReader(new Dictionary<int, string> { [0] = "Beta" }),
                new FakeCompletionReader(new Dictionary<int, string> { [0] = "Claim" }),
                rewardReader);
            using var screenshot = new Bitmap(1920, 1080);

            AchievementVisibleRow row = Assert.Single(reader.Read(screenshot).VisibleRows);

            Assert.Equal(AchievementStageProgressState.PendingClaim, row.StageProgressResult.State);
            Assert.Equal(0, rewardReader.ReadCount);
            Assert.False(row.SuitableForTrustedCompletionExtraction);
            Assert.Empty(AchievementTrustedCompletionExtractor.Extract(new[] { row }));
        }

        [Fact]
        public void RowTitleReader_CropsFirstMiddleAndBottomTitlesWithoutNeighborBands()
        {
            using var screenshot = new Bitmap(1920, 1080);
            using Graphics graphics = Graphics.FromImage(screenshot);
            graphics.Clear(Color.White);
            AchievementListRegions regions = AchievementListRegions.For(screenshot.Size);
            Fill(graphics, regions.Rows[0].Title, 40);
            Fill(graphics, regions.Rows[2].Title, 120);
            Fill(graphics, regions.Rows[5].Title, 220);
            foreach (AchievementListRowGeometry row in regions.Rows)
                graphics.FillRectangle(Brushes.Black, row.Description);
            var reader = new AchievementListRowTitleReader(
                new RegionColorOcrService(),
                new PassThroughPreprocessor());

            string[] reads = new[] { 0, 2, 5 }
                .Select(index => reader.Read(
                    screenshot,
                    regions.Rows[index],
                    _ => true).Attempts.Single().RawText)
                .ToArray();

            Assert.Equal(new[] { "First title", "Middle title", "Bottom title" }, reads);
        }

        private static AchievementListFrameReader CreateReader(
            AchievementListRowGeometry[] rows,
            FakeTitleReader titleReader,
            FakeCompletionReader completionReader,
            FakeRewardReader rewardReader = null) => new(
                CreateCatalogProvider(),
                new IdleScreenDetector(),
                new FixedRowDetector(rows),
                titleReader,
                completionReader,
                rewardReader ?? new FakeRewardReader(5),
                new FixedCompletedTotalReader(17),
                new AchievementStageProgressResolver());

        private static Bitmap LoadBulkFixture(string fixtureName) => new(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "AchievementsBulk", fixtureName));

        private static Bitmap BuildPanelComposite(string fixtureName)
        {
            var frame = new Bitmap(1919, 1079);
            using (Graphics graphics = Graphics.FromImage(frame)) graphics.Clear(Color.Black);
            using Bitmap fixture = LoadBulkFixture(fixtureName);
            using (Graphics graphics = Graphics.FromImage(frame))
                graphics.DrawImageUnscaled(fixture, 727, 125);
            return frame;
        }

        private static void AchievementVisibleRowGeometryAssert(
            Bitmap frame,
            out AchievementListRowGeometry first,
            out AchievementListRowGeometry completed)
        {
            AchievementVisibleRowDetection detection = new AchievementVisibleRowDetector().Detect(frame);
            Assert.True(detection.VisibleRows.Count >= 3, detection.Diagnostics);
            first = detection.VisibleRows[0];
            completed = detection.VisibleRows[2];
            Assert.Equal(AchievementVisibleRowVisibility.Complete, first.Visibility);
            Assert.Equal(AchievementVisibleRowVisibility.Complete, completed.Visibility);
        }

        private static AchievementCatalogProvider CreateCatalogProvider()
        {
            var entries = new[]
            {
                // A completed hidden achievement is visible and follows the ordinary row path.
                Entry(100, "Alpha", hidden: true),
                Entry(101, "Delta"),
                Entry(102, "Epsilon"),
                Entry(103, "Zeta"),
                Entry(200, "Beta", stageGroupId: 200, stageIndex: 1, stageCount: 2, primogems: 5),
                Entry(201, "Beta", stageGroupId: 200, stageIndex: 2, stageCount: 2, primogems: 10),
                Entry(300, "Gamma"),
                Entry(301, "Gamma"),
            };
            return new AchievementCatalogProvider(new AchievementCatalog(entries));
        }

        private static AchievementCatalogEntry Entry(
            int id,
            string name,
            int? stageGroupId = null,
            int? stageIndex = null,
            int? stageCount = null,
            int primogems = 0,
            bool hidden = false) => new(
                id,
                name,
                "Description",
                categoryId: 1,
                hidden,
                version: "1.0",
                order: id,
                stageGroupId,
                stageIndex,
                stageCount,
                primogems);

        private static AchievementListRowGeometry[] Rows(int count) =>
            AchievementListRegions.For(new Size(1920, 1080)).Rows.Take(count).ToArray();

        private static void DrawSyntheticRow(Graphics graphics, AchievementListRowGeometry row)
        {
            graphics.FillRectangle(Brushes.LightGray, row.Title);
            graphics.DrawLine(Pens.Black, row.Title.Left + 4, row.Title.Top + 8, row.Title.Right - 4, row.Title.Bottom - 8);
            using var borderPen = new Pen(Color.FromArgb(218, 207, 190), 2);
            graphics.DrawLine(borderPen, row.Bounds.Left, row.Bounds.Top, row.Bounds.Right - 1, row.Bounds.Top);
            graphics.DrawLine(borderPen, row.Bounds.Left, row.Bounds.Bottom - 1, row.Bounds.Right - 1, row.Bounds.Bottom - 1);
            graphics.FillEllipse(Brushes.Goldenrod, row.Emblem);
            graphics.DrawEllipse(Pens.White, row.Emblem);
            graphics.FillRectangle(Brushes.IndianRed, row.CompletionLabel);
            graphics.DrawLine(Pens.White, row.CompletionLabel.Left, row.CompletionLabel.Top, row.CompletionLabel.Right, row.CompletionLabel.Bottom);
        }

        private static void Fill(Graphics graphics, Rectangle region, int value)
        {
            using var brush = new SolidBrush(Color.FromArgb(value, value, value));
            graphics.FillRectangle(brush, region);
        }

        private sealed class IdleScreenDetector : IAchievementScreenDetector
        {
            public AchievementScreenDetection Detect(Bitmap screenshot) => AchievementScreenDetector.Classify(
                screenshot.Size,
                AchievementTextEvidence.ForTest("Achievements", accepted: true),
                AchievementSearchFieldEvidence.ForTest(
                    idlePlaceholderAccepted: true,
                    rawText: "Search for Achievements..."),
                AchievementTextEvidence.ForTest("Achievements completed: 17", accepted: true),
                AchievementMatchCountParser.Parse(string.Empty),
                AchievementPageCounterParser.Parse(string.Empty));
        }

        private sealed class FixedRowDetector : IAchievementVisibleRowDetector
        {
            private readonly AchievementListRowGeometry[] rows;
            internal FixedRowDetector(AchievementListRowGeometry[] rows) => this.rows = rows;

            public AchievementVisibleRowDetection Detect(Bitmap screenshot) => new(rows.Select(row =>
                new AchievementVisibleRowEvidence(row, true, true, true, $"rowSlot={row.Index}; accepted=True")));
        }

        private sealed class FakeTitleReader : IAchievementListRowTitleReader
        {
            private readonly IReadOnlyDictionary<int, string> titles;
            internal FakeTitleReader(IReadOnlyDictionary<int, string> titles) => this.titles = titles;
            internal Dictionary<int, int> AttemptsPerRow { get; } = new();
            internal HashSet<int> ThrowRows { get; } = new();

            public AchievementResultTitleRead Read(
                Bitmap screenshot,
                AchievementListRowGeometry row,
                Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt = null)
            {
                if (ThrowRows.Contains(row.Index))
                    throw new InvalidOperationException("Synthetic row OCR failure.");

                var attempts = new List<AchievementTitleOcrAttempt>();
                foreach (string text in new[] { titles[row.Index], "fallback should not run" })
                {
                    var attempt = new AchievementTitleOcrAttempt(
                        AchievementTitleOcrVariant.GrayscaleThreshold,
                        text,
                        0.99f,
                        "Synthetic title.");
                    attempts.Add(attempt);
                    if (stopAfterAttempt?.Invoke(attempt) == true) break;
                }
                AttemptsPerRow[row.Index] = attempts.Count;
                return new AchievementResultTitleRead(attempts);
            }
        }

        private sealed class FakeCompletionReader : IAchievementListRowCompletionReader
        {
            private readonly IReadOnlyDictionary<int, string> states;
            internal FakeCompletionReader(IReadOnlyDictionary<int, string> states) => this.states = states;
            internal List<int> ReadRows { get; } = new();

            public AchievementCompletionRead Read(
                Bitmap screenshot,
                AchievementListRowGeometry row,
                Func<IReadOnlyList<AchievementCompletionOcrAttempt>, bool> stopAfterVariant = null)
            {
                ReadRows.Add(row.Index);
                return new AchievementCompletionRead(new[]
                {
                    new AchievementCompletionOcrAttempt(
                        AchievementCompletionOcrVariant.GrayscaleThreshold,
                        states[row.Index],
                        0.99f,
                        "Synthetic completion.",
                        AchievementCompletionOcrScope.StateLabel),
                });
            }
        }

        private sealed class FakeRewardReader : IAchievementListRowRewardReader
        {
            private readonly int value;
            internal FakeRewardReader(int value) => this.value = value;
            internal int ReadCount { get; private set; }

            public AchievementRewardResult Read(Bitmap screenshot, AchievementListRowGeometry row)
            {
                ReadCount++;
                var attempt = new AchievementRewardOcrAttempt(
                    AchievementRewardOcrVariant.GrayscaleThreshold,
                    value.ToString(),
                    0.99f);
                return new AchievementRewardResult(
                    new[] { attempt },
                    attempt,
                    value,
                    "Synthetic reward.",
                    "rewardValue=" + value);
            }
        }

        private sealed class FixedCompletedTotalReader : IAchievementCompletedTotalReader
        {
            private readonly int value;
            internal FixedCompletedTotalReader(int value) => this.value = value;
            public AchievementCompletedTotalResult Read(Bitmap screenshot) => new(new[]
            {
                new AchievementCompletedTotalAttempt($"Achievements completed: {value}", 0.99f, value, "Synthetic total."),
            });
        }

        private sealed class RegionColorOcrService : IOcrService
        {
            public void Restart() { }
            public string AnalyzeText(Bitmap bitmap, PageSegMode pageMode = PageSegMode.SingleLine, bool numbersOnly = false) =>
                AnalyzeTextWithConfidence(bitmap, pageMode, numbersOnly).Text;

            public (string Text, float Confidence) AnalyzeTextWithConfidence(
                Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false)
            {
                int value = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).R;
                return (value < 80 ? "First title" : value < 180 ? "Middle title" : "Bottom title", 0.99f);
            }
        }

        private sealed class PassThroughPreprocessor : IImagePreprocessor
        {
            public Bitmap ConvertToGrayscale(Bitmap bitmap) => new(bitmap);
            public void SetInvert(ref Bitmap bitmap) { }
            public void SetThreshold(int threshold, ref Bitmap bitmap) { }
            public void SetContrast(double contrast, ref Bitmap bitmap) { }
            public void FilterColors(ref Bitmap bitmap, IntRange red, IntRange green, IntRange blue) { }
            public (double R, double G, double B) AverageColor(Bitmap bitmap) => (0, 0, 0);
            public Bitmap EdgeDetectKirsch(Bitmap bitmap) => new(bitmap);
            public List<Rectangle> FindBlobRectangles(
                Bitmap binary,
                int minWidth,
                int maxWidth,
                int minHeight,
                int maxHeight) => new();
        }
    }
}
