using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementScrollCalibrationTests
    {
        [Fact]
        public void RealSequence_MeasuresEighteenPixelNotchesAndStationaryBottom()
        {
            using Bitmap top = FullFrame("01_top.png");
            using Bitmap first = FullFrame("02_scroll_1.png");
            using Bitmap second = FullFrame("03_scroll_2.png");
            using Bitmap bottom = FullFrame("04_bottom.png");
            using Bitmap beyondBottom = FullFrame("05_beyond_bottom.png");

            AchievementViewportComparison firstNotch = Fingerprint(top).CompareTo(Fingerprint(first));
            AchievementViewportComparison secondNotch = Fingerprint(first).CompareTo(Fingerprint(second));
            AchievementViewportComparison twoNotches = Fingerprint(top).CompareTo(Fingerprint(second));
            AchievementViewportComparison stopped = Fingerprint(bottom).CompareTo(Fingerprint(beyondBottom));

            Assert.Equal(-18, firstNotch.DisplacementPixels);
            Assert.Equal(-18, secondNotch.DisplacementPixels);
            Assert.Equal(-36, twoNotches.DisplacementPixels);
            Assert.True(firstNotch.ReliableMovement);
            Assert.True(secondNotch.ReliableMovement);
            Assert.True(stopped.Stable);
            Assert.Equal(0, stopped.DisplacementPixels);
        }

        [Fact]
        public void DefaultBatch_RequestsTwentyOneIndependentNotchesWithoutAssumingDisplacement()
        {
            AchievementCategoryCrawlOptions options = AchievementCategoryCrawlOptions.Default;
            var pointer = new RecordingPointer();
            var input = new AchievementWindowsListScrollInput(pointer, new ForegroundActivator());

            AchievementListScrollCommandResult result = input.Scroll(
                options.ScrollRequest, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(21, pointer.WheelDeltas.Count);
            Assert.All(pointer.WheelDeltas, delta => Assert.Equal(-120, delta));
            Assert.Equal(-2520, options.ScrollRequest.LogicalWheelDelta);
            Assert.Equal(378, options.ScrollRequest.ProvisionalTargetDisplacementPixels);
            Assert.Contains("actualDisplacement=measured", result.Diagnostics);
        }

        [Fact]
        public void BatchCancellation_StopsBetweenIndividualNotches()
        {
            using var cancellation = new CancellationTokenSource();
            var pointer = new RecordingPointer(afterWheel: count =>
            {
                if (count == 2) cancellation.Cancel();
            });
            var input = new AchievementWindowsListScrollInput(pointer, new ForegroundActivator());

            Assert.Throws<OperationCanceledException>(() => input.Scroll(
                AchievementCategoryCrawlOptions.Default.ScrollRequest, cancellation.Token));
            Assert.Equal(2, pointer.WheelDeltas.Count);
        }

        [Fact]
        public void RealEvidence_VerifiesMemoriesCategoryAndMeasuredScrollbarEndpoints()
        {
            using var ocr = new OcrService(engineCount: 1);
            var detector = new AchievementCategoryFrameEvidenceDetector(ocr, new ImageProcessor());
            using Bitmap top = FullFrame("01_top.png", includeCategory: true);
            using Bitmap first = FullFrame("02_scroll_1.png", includeCategory: true);
            using Bitmap second = FullFrame("03_scroll_2.png", includeCategory: true);
            using Bitmap bottom = FullFrame("04_bottom.png", includeCategory: true);
            using Bitmap beyondBottom = FullFrame("05_beyond_bottom.png", includeCategory: true);

            AchievementCategoryFrameEvidence topEvidence = detector.Detect(top);
            AchievementCategoryFrameEvidence firstEvidence = detector.Detect(first);
            AchievementCategoryFrameEvidence secondEvidence = detector.Detect(second);
            AchievementCategoryFrameEvidence bottomEvidence = detector.Detect(bottom);
            AchievementCategoryFrameEvidence beyondEvidence = detector.Detect(beyondBottom);

            Assert.True(topEvidence.CategorySignatureVerified, topEvidence.Diagnostics);
            Assert.Equal(AchievementCategoryFrameEvidenceDetector.MemoriesOfTheHeartSignature,
                topEvidence.CategorySignature);
            Assert.DoesNotContain("wonders of the world", topEvidence.CategorySignature);
            Assert.True(topEvidence.TopAtExtent);
            Assert.False(firstEvidence.TopAtExtent);
            Assert.False(secondEvidence.TopAtExtent);
            Assert.False(secondEvidence.BottomAtExtent);
            Assert.True(bottomEvidence.BottomAtExtent);
            Assert.True(beyondEvidence.BottomAtExtent);
            Assert.Contains("148-262", topEvidence.Diagnostics);
            Assert.Contains("853-967", bottomEvidence.Diagnostics);
        }

        [Fact]
        public void ScrollbarEvidence_MapsConfirmedLiveCaptureSizeToCalibratedTop()
        {
            using Bitmap frame = ScrollbarFrame(1920, 1080, (148, 262));

            AchievementScrollbarEvidence evidence =
                AchievementCategoryFrameEvidenceDetector.AnalyzeScrollbar(frame);

            Assert.True(evidence.Resolved, evidence.Diagnostics);
            Assert.True(evidence.TopAtExtent, evidence.Diagnostics);
            Assert.False(evidence.BottomAtExtent);
            Assert.Equal(1839, evidence.X);
            Assert.Equal(148, evidence.TrackTop);
            Assert.Equal(968, evidence.TrackBottom);
            Assert.Equal(0.0, evidence.NormalizedPosition);
            Assert.Contains("mapped-live-1920x1080", evidence.Diagnostics);
            Assert.Contains("rejectionReason=none", evidence.Diagnostics);
        }

        [Theory]
        [InlineData(148, 262, true, false)]
        [InlineData(150, 264, true, false)]
        [InlineData(500, 614, false, false)]
        [InlineData(853, 967, false, true)]
        public void ScrollbarEvidence_ClassifiesCalibratedPositionsWithoutPromotingMiddle(
            int thumbTop, int thumbBottom, bool expectedTop, bool expectedBottom)
        {
            using Bitmap frame = ScrollbarFrame(1919, 1079, (thumbTop, thumbBottom));

            AchievementScrollbarEvidence evidence =
                AchievementCategoryFrameEvidenceDetector.AnalyzeScrollbar(frame);

            Assert.True(evidence.Resolved, evidence.Diagnostics);
            Assert.Equal(expectedTop, evidence.TopAtExtent);
            Assert.Equal(expectedBottom, evidence.BottomAtExtent);
            Assert.InRange(evidence.Confidence, 0.75, 1.0);
            Assert.NotNull(evidence.NormalizedPosition);
            Assert.Contains("scrollbarTrack=x1838,y148-967", evidence.Diagnostics);
            Assert.Contains($"scrollbarThumb={thumbTop}-{thumbBottom}", evidence.Diagnostics);
        }

        [Fact]
        public void ScrollbarEvidence_MissingThumbRemainsUnknown()
        {
            using Bitmap frame = ScrollbarFrame(1919, 1079);

            AchievementScrollbarEvidence evidence =
                AchievementCategoryFrameEvidenceDetector.AnalyzeScrollbar(frame);

            Assert.False(evidence.Resolved);
            Assert.False(evidence.TopAtExtent);
            Assert.False(evidence.BottomAtExtent);
            Assert.Equal("no-bright-scrollbar-run", evidence.RejectionReason);
            Assert.Contains("normalizedPosition=unavailable", evidence.Diagnostics);
        }

        [Fact]
        public void ScrollbarEvidence_MultiplePlausibleThumbsRemainAmbiguous()
        {
            using Bitmap frame = ScrollbarFrame(1919, 1079, (148, 262), (500, 614));

            AchievementScrollbarEvidence evidence =
                AchievementCategoryFrameEvidenceDetector.AnalyzeScrollbar(frame);

            Assert.False(evidence.Resolved);
            Assert.False(evidence.TopAtExtent);
            Assert.False(evidence.BottomAtExtent);
            Assert.Equal(2, evidence.CandidateCount);
            Assert.Equal("multiple-valid-thumb-candidates", evidence.RejectionReason);
            Assert.Contains("observedRuns=148-262,500-614", evidence.Diagnostics);
        }

        [Fact]
        public void StableViewportWithoutBottomEvidence_RemainsIncomplete()
        {
            using Bitmap start = SolidFrame(20);
            using Bitmap stable = SolidFrame(20);
            var crawler = new AchievementCategoryCrawler(
                new CountingFrameReader(Frame(Row(1)), Frame(Row(1))),
                new SequenceCapture(stable),
                new SuccessfulScrollInput(),
                new SequenceEvidenceDetector(Evidence(top: true), Evidence()),
                new AchievementCategoryCrawlOptions(maximumCapturedFrames: 2,
                    maximumNoMovementRecoveryAttempts: 0,
                    scrollSettleMilliseconds: 0,
                    maximumStabilizationCaptures: 1),
                (_, token) => token.ThrowIfCancellationRequested());

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Memories of the Heart",
                    knownTraversalOrigin: true), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Contains("bottom proof", result.Reason);
            AchievementScrollObservation observation = Assert.Single(result.ScrollObservations);
            Assert.Equal(21, observation.RequestedNotches);
            Assert.Equal(-2520, observation.RequestedLogicalWheelDelta);
            Assert.True(observation.ViewportStable);
            Assert.False(observation.MovementVerified);
        }

        [Fact]
        public void Stabilization_ProcessesOnlyTheFinalStableCapture()
        {
            using Bitmap start = SolidFrame(20);
            using Bitmap movingOne = SolidFrame(40);
            using Bitmap movingTwo = SolidFrame(80);
            using Bitmap stable = SolidFrame(80);
            var reader = new CountingFrameReader(Frame(Row(1)), Frame(Row(2)));
            var capture = new SequenceCapture(movingOne, movingTwo, stable);
            var crawler = new AchievementCategoryCrawler(
                reader,
                capture,
                new SuccessfulScrollInput(),
                new SequenceEvidenceDetector(Evidence(top: true), Evidence()),
                new AchievementCategoryCrawlOptions(maximumCapturedFrames: 2,
                    scrollSettleMilliseconds: 0,
                    stabilizationPollMilliseconds: 0,
                    maximumStabilizationCaptures: 3),
                (_, token) => token.ThrowIfCancellationRequested());

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Memories of the Heart",
                    knownTraversalOrigin: true), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Equal(3, capture.Captures);
            Assert.Equal(2, reader.Reads);
            Assert.Contains("stabilizationCaptures=3; stable=true", result.Diagnostics);
        }

        private static AchievementViewportFingerprint Fingerprint(Bitmap frame) =>
            AchievementViewportFingerprint.Create(frame);

        private static Bitmap FullFrame(string fixture, bool includeCategory = false)
        {
            var frame = new Bitmap(1919, 1079);
            using Graphics graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Black);
            using Bitmap panel = new(FixturePath(fixture));
            graphics.DrawImageUnscaled(panel, 727, 136);
            if (includeCategory)
            {
                using Bitmap category = new(FixturePath("category_memories_selected.png"));
                graphics.DrawImageUnscaled(category, 50, 175);
            }
            return frame;
        }

        private static string FixturePath(string name) => Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "AchievementsScroll", name);

        private static Bitmap SolidFrame(byte value)
        {
            var bitmap = new Bitmap(1919, 1079);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(value, value, value));
            return bitmap;
        }

        private static Bitmap ScrollbarFrame(int width, int height,
            params (int Top, int Bottom)[] thumbs)
        {
            var bitmap = new Bitmap(width, height);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Black);
            int x = (int)Math.Round(1838 * width / 1919.0);
            using var brush = new SolidBrush(Color.FromArgb(245, 242, 238));
            foreach ((int top, int bottom) in thumbs)
                graphics.FillRectangle(brush, x - 2, top, 5, bottom - top + 1);
            return bitmap;
        }

        private static AchievementCategoryFrameEvidence Evidence(bool top = false, bool bottom = false) =>
            new(top, bottom, string.Empty, false, "Synthetic evidence.");

        private static AchievementBulkFrameResult Frame(params AchievementVisibleRow[] rows) => new(
            AchievementScreenState.Idle,
            rows,
            17,
            new AchievementBulkFrameTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, rows.Length, rows.Length),
            "Synthetic frame.");

        private static AchievementVisibleRow Row(int id) => new(
            id,
            new NormalizedRectangle(0.38, id / 100.0, 0.56, 0.1),
            AchievementVisibleRowVisibility.Complete,
            "Title " + id,
            "title " + id,
            AchievementIdentityState.Identified,
            new[] { id },
            null,
            AchievementCompletionState.CompletedClaimed,
            null, null, null,
            100, 0, 0.99f,
            true,
            new AchievementRowFingerprint("canonical:" + id, true),
            null, null, null,
            "Synthetic row.");

        private sealed class RecordingPointer : IAchievementPointerInput
        {
            private readonly Action<int> afterWheel;
            internal RecordingPointer(Action<int> afterWheel = null) => this.afterWheel = afterWheel;
            internal List<int> WheelDeltas { get; } = new();
            public void MoveNormalized(double x, double y, CancellationToken cancellationToken) { }
            public bool IsAtNormalized(double x, double y) => true;
            public void Click(CancellationToken cancellationToken) { }
            public void ScrollWheel(int delta, CancellationToken cancellationToken)
            {
                WheelDeltas.Add(delta);
                afterWheel?.Invoke(WheelDeltas.Count);
            }
        }

        private sealed class ForegroundActivator : IGameForegroundActivator
        {
            public bool IsForeground => true;
            public void Activate() { }
        }

        private sealed class SuccessfulScrollInput : IAchievementListScrollInput
        {
            public AchievementListScrollCommandResult Scroll(
                AchievementListScrollRequest request, CancellationToken cancellationToken) =>
                new(true, true, null, "Synthetic batch.");
        }

        private sealed class SequenceCapture : IGameScreenCapture
        {
            private readonly Queue<Bitmap> frames;
            internal SequenceCapture(params Bitmap[] frames) =>
                this.frames = new Queue<Bitmap>(Array.ConvertAll(frames, frame => new Bitmap(frame)));
            internal int Captures { get; private set; }
            public Bitmap CaptureWindow()
            {
                Captures++;
                return frames.Dequeue();
            }
        }

        private sealed class CountingFrameReader : IAchievementListFrameReader
        {
            private readonly Queue<AchievementBulkFrameResult> frames;
            internal CountingFrameReader(params AchievementBulkFrameResult[] frames) =>
                this.frames = new Queue<AchievementBulkFrameResult>(frames);
            internal int Reads { get; private set; }
            public AchievementBulkFrameResult Read(Bitmap screenshot, TimeSpan? captureDuration = null)
            {
                Reads++;
                return frames.Dequeue();
            }
        }

        private sealed class SequenceEvidenceDetector : IAchievementCategoryFrameEvidenceDetector
        {
            private readonly Queue<AchievementCategoryFrameEvidence> evidence;
            internal SequenceEvidenceDetector(params AchievementCategoryFrameEvidence[] evidence) =>
                this.evidence = new Queue<AchievementCategoryFrameEvidence>(evidence);
            public AchievementCategoryFrameEvidence Detect(Bitmap screenshot) => evidence.Dequeue();
        }
    }
}
