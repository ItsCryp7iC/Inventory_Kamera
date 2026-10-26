using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Tesseract;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    public sealed class AchievementBulkFrameOptimizationTests
    {
        private readonly ITestOutputHelper output;

        public AchievementBulkFrameOptimizationTests(ITestOutputHelper output) =>
            this.output = output;

        [Fact]
        public void StandaloneRead_PreservesFullValidationAndDedicatedCompletedTotalOcr()
        {
            var screen = new FixedScreenDetector(AchievementScreenState.Idle, "Achievements completed");
            var total = new FixedTotalReader(17);
            AchievementListFrameReader reader = CreateReader(screen, total,
                new FixedContinuationValidator(true));
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(bitmap);

            Assert.Equal(AchievementBulkScreenValidationPath.Full, result.ScreenValidationPath);
            Assert.Equal(1, screen.Calls);
            Assert.Equal(1, total.Calls);
            Assert.Equal(AchievementCompletedTotalEvidenceSource.DedicatedOcr,
                result.CompletedTotalEvidence.Source);
            Assert.Equal(17, result.CompletedTotal);
        }

        [Fact]
        public void InitialCrawlerRead_UsesFullScreenCompletedTotalWithoutDuplicateOcr()
        {
            var screen = new FixedScreenDetector(AchievementScreenState.Idle,
                "Achievements completed: 1769");
            var total = new FixedTotalReader(1769);
            AchievementListFrameReader reader = CreateReader(screen, total,
                new FixedContinuationValidator(true));
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(bitmap,
                Context(0, forceFull: true, observeTotal: true));

            Assert.Equal(AchievementBulkScreenValidationPath.Full, result.ScreenValidationPath);
            Assert.Equal(1, screen.Calls);
            Assert.Equal(0, total.Calls);
            Assert.True(result.CompletedTotalEvidence.PositivelyObserved);
            Assert.Equal(AchievementCompletedTotalEvidenceSource.FullScreenDetection,
                result.CompletedTotalEvidence.Source);
            Assert.Equal(1769, result.CompletedTotal);
        }

        [Fact]
        public void ContinuationRead_UsesFastValidationAndContextualTotal()
        {
            var screen = new FixedScreenDetector(AchievementScreenState.Idle,
                "Achievements completed: 17");
            var total = new FixedTotalReader(17);
            var fast = new FixedContinuationValidator(true);
            AchievementListFrameReader reader = CreateReader(screen, total, fast);
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(bitmap,
                Context(1, forceFull: false, observeTotal: false, cached: 17, cachedFrame: 0));

            Assert.Equal(AchievementBulkScreenValidationPath.Fast, result.ScreenValidationPath);
            Assert.Equal(1, fast.Calls);
            Assert.Equal(0, screen.Calls);
            Assert.Equal(0, total.Calls);
            Assert.True(result.CompletedTotalEvidence.Reused);
            Assert.False(result.CompletedTotalEvidence.PositivelyObserved);
            Assert.Equal(0, result.CompletedTotalEvidence.ObservationFrameIndex);
            Assert.Equal(1, result.Timing.FastScreenValidationCount);
            Assert.Equal(1, result.Timing.CompletedTotalReuseCount);
        }

        [Fact]
        public void CheckpointRead_ExecutesDedicatedTotalOcr()
        {
            var total = new FixedTotalReader(18);
            AchievementListFrameReader reader = CreateReader(
                new FixedScreenDetector(AchievementScreenState.Idle, "Achievements completed"),
                total,
                new FixedContinuationValidator(true));
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(bitmap,
                Context(5, forceFull: false, observeTotal: true, cached: 17, cachedFrame: 0));

            Assert.Equal(1, total.Calls);
            Assert.Equal(18, result.CompletedTotal);
            Assert.True(result.CompletedTotalEvidence.PositivelyObserved);
            Assert.Equal(5, result.CompletedTotalEvidence.ObservationFrameIndex);
            Assert.Equal(1, result.Timing.CompletedTotalOcrExecutions);
        }

        [Fact]
        public void UnreadableCheckpoint_NeverBecomesZero_AndRetainsLastPositiveValue()
        {
            var total = new FixedTotalReader(null);
            AchievementListFrameReader reader = CreateReader(
                new FixedScreenDetector(AchievementScreenState.Idle, "Achievements completed"),
                total,
                new FixedContinuationValidator(true));
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(bitmap,
                Context(5, forceFull: false, observeTotal: true, cached: 17, cachedFrame: 0));

            Assert.Equal(17, result.CompletedTotal);
            Assert.False(result.CompletedTotalEvidence.PositivelyObserved);
            Assert.True(result.CompletedTotalEvidence.Reused);
            Assert.Equal(0, result.CompletedTotalEvidence.ObservationFrameIndex);
        }

        [Theory]
        [InlineData((int)AchievementScreenState.Idle, true)]
        [InlineData((int)AchievementScreenState.SearchActive, false)]
        [InlineData((int)AchievementScreenState.Unknown, false)]
        [InlineData((int)AchievementScreenState.NotAchievementScreen, false)]
        public void FastValidationFailure_AlwaysFallsBackAndPreservesFullDecision(
            int fallbackStateValue, bool expectedRowsAllowed)
        {
            AchievementScreenState fallbackState = (AchievementScreenState)fallbackStateValue;
            var screen = new FixedScreenDetector(fallbackState, "Achievements completed: 17");
            var fast = new FixedContinuationValidator(false, AchievementScreenState.Unknown,
                "Synthetic current-frame ambiguity.");
            AchievementListFrameReader reader = CreateReader(screen, new FixedTotalReader(17), fast);
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(bitmap,
                Context(1, forceFull: false, observeTotal: false, cached: 17, cachedFrame: 0));

            Assert.Equal(AchievementBulkScreenValidationPath.FullFallback,
                result.ScreenValidationPath);
            Assert.Equal(fallbackState, result.ScreenState);
            Assert.Equal(1, screen.Calls);
            Assert.Equal(1, result.Timing.FullScreenFallbackCount);
            Assert.Equal(expectedRowsAllowed, result.ScreenState == AchievementScreenState.Idle);
            Assert.Contains("ambiguity", result.ScreenValidationFallbackReason,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FastValidationException_FallsBackWithoutLeakingExceptionMessage()
        {
            var screen = new FixedScreenDetector(AchievementScreenState.Idle,
                "Achievements completed: 17");
            AchievementListFrameReader reader = CreateReader(screen,
                new FixedTotalReader(17), new ThrowingContinuationValidator());
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkFrameResult result = reader.Read(bitmap,
                Context(1, false, false, cached: 17, cachedFrame: 0));

            Assert.Equal(AchievementBulkScreenValidationPath.FullFallback,
                result.ScreenValidationPath);
            Assert.Equal(AchievementScreenState.Idle, result.ScreenState);
            Assert.Contains(nameof(InvalidOperationException),
                result.ScreenValidationFallbackReason);
            Assert.DoesNotContain("sensitive", result.ScreenValidationFallbackReason,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FastValidator_RequiresCurrentSizeCategoryScrollbarHeadingAndKeyboardMouseIdle()
        {
            var ocr = new SequenceOcrService(("Achievements", .99f),
                ("Search for Achievements...", .99f));
            var detector = new AchievementScreenDetector(ocr, new PassThroughPreprocessor());
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkContinuationScreenValidation accepted = detector.Validate(bitmap,
                Context(1, false, false));
            AchievementBulkContinuationScreenValidation wrongSize = detector.Validate(bitmap,
                Context(1, false, false, traversalSize: new Size(1919, 1079)));
            AchievementBulkContinuationScreenValidation wrongCategory = detector.Validate(bitmap,
                Context(1, false, false, category: "wonders of the world"));
            AchievementBulkContinuationScreenValidation unresolvedScrollbar = detector.Validate(bitmap,
                Context(1, false, false, scrollbarResolved: false));

            Assert.True(accepted.Accepted);
            Assert.True(accepted.IsKeyboardMouseIdle);
            Assert.False(wrongSize.Accepted);
            Assert.False(wrongCategory.Accepted);
            Assert.False(unresolvedScrollbar.Accepted);
        }

        [Theory]
        [InlineData("Clear", (int)AchievementScreenState.SearchActive)]
        [InlineData("unreadable", (int)AchievementScreenState.Unknown)]
        public void FastValidator_SearchActiveOrAmbiguousNeverPasses(
            string searchText, int expectedStateValue)
        {
            AchievementScreenState expectedState = (AchievementScreenState)expectedStateValue;
            var detector = new AchievementScreenDetector(
                new SequenceOcrService(("Achievements", .99f), (searchText, .99f)),
                new PassThroughPreprocessor());
            using var bitmap = new Bitmap(1920, 1080);

            AchievementBulkContinuationScreenValidation result = detector.Validate(bitmap,
                Context(1, false, false));

            Assert.False(result.Accepted);
            Assert.Equal(expectedState, result.ScreenState);
        }

        [Fact]
        public void Reader_DoesNotDisposeCallerOwnedBitmap()
        {
            AchievementListFrameReader reader = CreateReader(
                new FixedScreenDetector(AchievementScreenState.Idle, "Achievements completed: 17"),
                new FixedTotalReader(17), new FixedContinuationValidator(true));
            using var bitmap = new Bitmap(1920, 1080);

            reader.Read(bitmap, Context(1, false, false, cached: 17, cachedFrame: 0));

            bitmap.SetPixel(0, 0, Color.Red);
            Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        }

        [Fact]
        public void RepresentativeFixture_BeforeAfterBenchmarkReportsWorkAndPreservesRows()
        {
            using Bitmap bitmap = BuildPanelComposite("real_list_standard_top.png");
            var baselineScreen = new FixedScreenDetector(AchievementScreenState.Idle,
                "Achievements completed");
            var baselineTotal = new FixedTotalReader(17);
            AchievementListFrameReader baseline = CreateReader(baselineScreen, baselineTotal,
                new FixedContinuationValidator(true), new AchievementVisibleRowDetector());
            var optimizedScreen = new FixedScreenDetector(AchievementScreenState.Idle,
                "Achievements completed: 17");
            var optimizedTotal = new FixedTotalReader(17);
            var optimizedFast = new FixedContinuationValidator(true);
            AchievementListFrameReader optimized = CreateReader(optimizedScreen, optimizedTotal,
                optimizedFast, new AchievementVisibleRowDetector());

            var beforeRows = new List<string>();
            Stopwatch before = Stopwatch.StartNew();
            for (int frame = 0; frame < 20; frame++)
                beforeRows.Add(RowSignature(baseline.Read(bitmap)));
            before.Stop();

            var afterRows = new List<string>();
            Stopwatch after = Stopwatch.StartNew();
            int? cached = null;
            int? cachedFrame = null;
            for (int frame = 0; frame < 20; frame++)
            {
                bool checkpoint = frame == 0 || frame % 5 == 0 || frame == 19;
                AchievementBulkFrameResult result = optimized.Read(bitmap,
                    Context(frame, frame == 0, checkpoint, cached, cachedFrame));
                if (result.CompletedTotalEvidence.PositivelyObserved)
                {
                    cached = result.CompletedTotal;
                    cachedFrame = frame;
                }
                afterRows.Add(RowSignature(result));
            }
            after.Stop();

            output.WriteLine(
                "Representative sanitized-fixture benchmark (instrumentation only): " +
                $"beforeMs={before.Elapsed.TotalMilliseconds:0.000}; afterMs={after.Elapsed.TotalMilliseconds:0.000}; " +
                $"beforeFull={baselineScreen.Calls}; beforeCompletedTotalOcr={baselineTotal.Calls}; " +
                $"afterFull={optimizedScreen.Calls}; afterFast={optimizedFast.Calls}; " +
                $"afterCompletedTotalOcr={optimizedTotal.Calls}; frames=20");
            Assert.Equal(beforeRows, afterRows);
            Assert.Equal(20, baselineScreen.Calls);
            Assert.Equal(20, baselineTotal.Calls);
            Assert.Equal(1, optimizedScreen.Calls);
            Assert.Equal(19, optimizedFast.Calls);
            Assert.Equal(4, optimizedTotal.Calls);
        }

        private static string RowSignature(AchievementBulkFrameResult result) => string.Join("|",
            result.VisibleRows.Select(row =>
                $"{row.Visibility}:{row.NormalizedVerticalPosition:0.000000}:{row.IdentityState}:{row.CompletionState}"));

        private static AchievementListFrameReader CreateReader(
            IAchievementScreenDetector screen,
            IAchievementCompletedTotalReader total,
            IAchievementBulkContinuationScreenValidator fast,
            IAchievementVisibleRowDetector rowDetector = null) => new(
                new AchievementCatalogProvider(new AchievementCatalog(Array.Empty<AchievementCatalogEntry>())),
                screen,
                rowDetector ?? new EmptyRowDetector(),
                new ThrowingTitleReader(),
                new ThrowingCompletionReader(),
                new ThrowingRewardReader(),
                total,
                new AchievementStageProgressResolver(),
                fast);

        private static Bitmap BuildPanelComposite(string fixtureName)
        {
            var frame = new Bitmap(1919, 1079);
            using (Graphics graphics = Graphics.FromImage(frame)) graphics.Clear(Color.Black);
            using var fixture = new Bitmap(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "AchievementsBulk", fixtureName));
            using (Graphics graphics = Graphics.FromImage(frame))
                graphics.DrawImageUnscaled(fixture, 727, 125);
            return frame;
        }

        private static AchievementBulkFrameReadContext Context(int frame,
            bool forceFull, bool observeTotal, int? cached = null, int? cachedFrame = null,
            Size? traversalSize = null, string category = "memories of the heart",
            bool scrollbarResolved = true)
        {
            Size size = traversalSize ?? new Size(1920, 1080);
            var evidence = new AchievementCategoryFrameEvidence(false, false,
                "memories of the heart", true, "Synthetic evidence.",
                new Size(1920, 1080), scrollbarResolved);
            return new AchievementBulkFrameReadContext(frame, size, evidence, category,
                forceFull, observeTotal, cached, cachedFrame);
        }

        private sealed class FixedContinuationValidator : IAchievementBulkContinuationScreenValidator
        {
            private readonly bool accepted;
            private readonly AchievementScreenState state;
            private readonly string reason;
            internal FixedContinuationValidator(bool accepted,
                AchievementScreenState state = AchievementScreenState.Idle,
                string reason = "Synthetic validation.")
            {
                this.accepted = accepted;
                this.state = state;
                this.reason = reason;
            }
            internal int Calls { get; private set; }
            public AchievementBulkContinuationScreenValidation Validate(Bitmap screenshot,
                AchievementBulkFrameReadContext context)
            {
                Calls++;
                return new AchievementBulkContinuationScreenValidation(
                    accepted, state, accepted, reason);
            }
        }

        private sealed class ThrowingContinuationValidator : IAchievementBulkContinuationScreenValidator
        {
            public AchievementBulkContinuationScreenValidation Validate(Bitmap screenshot,
                AchievementBulkFrameReadContext context) =>
                throw new InvalidOperationException("Sensitive synthetic details.");
        }

        private sealed class FixedScreenDetector : IAchievementScreenDetector
        {
            private readonly AchievementScreenState state;
            private readonly string completed;
            internal FixedScreenDetector(AchievementScreenState state, string completed)
            {
                this.state = state;
                this.completed = completed;
            }
            internal int Calls { get; private set; }
            public AchievementScreenDetection Detect(Bitmap screenshot)
            {
                Calls++;
                bool idle = state == AchievementScreenState.Idle;
                return new AchievementScreenDetection(state,
                    AchievementTextEvidence.ForTest("Achievements", true),
                    AchievementSearchFieldEvidence.ForTest(
                        idlePlaceholderAccepted: idle,
                        activeControlsAccepted: state == AchievementScreenState.SearchActive,
                        rawText: idle ? "Search for Achievements..." : "Clear"),
                    AchievementTextEvidence.ForTest(completed, true, .99f),
                    AchievementTextEvidence.ForTest(string.Empty, false),
                    AchievementMatchCountParser.Parse(string.Empty),
                    AchievementTextEvidence.ForTest(string.Empty, false),
                    AchievementPageCounterParser.Parse(string.Empty),
                    AchievementScreenRegions.For(screenshot.Size),
                    "Synthetic full-screen decision.");
            }
        }

        private sealed class FixedTotalReader : IAchievementCompletedTotalReader
        {
            private readonly int? value;
            internal FixedTotalReader(int? value) => this.value = value;
            internal int Calls { get; private set; }
            public AchievementCompletedTotalResult Read(Bitmap screenshot)
            {
                Calls++;
                return value.HasValue
                    ? new AchievementCompletedTotalResult(new[]
                    {
                        new AchievementCompletedTotalAttempt(
                            $"Achievements completed: {value}", .99f, value, "Synthetic total."),
                    })
                    : new AchievementCompletedTotalResult(Array.Empty<AchievementCompletedTotalAttempt>());
            }
        }

        private sealed class EmptyRowDetector : IAchievementVisibleRowDetector
        {
            public AchievementVisibleRowDetection Detect(Bitmap screenshot) =>
                new(Array.Empty<AchievementVisibleRowEvidence>());
        }

        private sealed class ThrowingTitleReader : IAchievementListRowTitleReader
        {
            public AchievementResultTitleRead Read(Bitmap screenshot, AchievementListRowGeometry row,
                Func<AchievementTitleOcrAttempt, bool> stopAfterAttempt = null) =>
                throw new InvalidOperationException();
        }

        private sealed class ThrowingCompletionReader : IAchievementListRowCompletionReader
        {
            public AchievementCompletionRead Read(Bitmap screenshot, AchievementListRowGeometry row,
                Func<IReadOnlyList<AchievementCompletionOcrAttempt>, bool> stopAfterVariant = null) =>
                throw new InvalidOperationException();
        }

        private sealed class ThrowingRewardReader : IAchievementListRowRewardReader
        {
            public AchievementRewardResult Read(Bitmap screenshot, AchievementListRowGeometry row) =>
                throw new InvalidOperationException();
        }

        private sealed class SequenceOcrService : IOcrService
        {
            private readonly Queue<(string Text, float Confidence)> values;
            internal SequenceOcrService(params (string Text, float Confidence)[] values) =>
                this.values = new Queue<(string Text, float Confidence)>(values);
            public void Restart() { }
            public string AnalyzeText(Bitmap bitmap, PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false) =>
                AnalyzeTextWithConfidence(bitmap, pageMode, numbersOnly).Text;
            public (string Text, float Confidence) AnalyzeTextWithConfidence(Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine, bool numbersOnly = false) =>
                values.Count > 0 ? values.Dequeue() : (string.Empty, 0f);
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
            public List<Rectangle> FindBlobRectangles(Bitmap binary, int minWidth, int maxWidth,
                int minHeight, int maxHeight) => new();
        }
    }
}
