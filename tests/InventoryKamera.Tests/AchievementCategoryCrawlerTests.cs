using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementCategoryCrawlerTests
    {
        [Fact]
        public void OverlappingFrames_DeduplicateCanonicalRows_AndRequirePositiveStableBottom()
        {
            AchievementBulkFrameResult first = Frame(Row(1), Row(2), Row(3), Row(4));
            AchievementBulkFrameResult second = Frame(Row(3), Row(4), Row(5), Row(6));
            AchievementBulkFrameResult stableBottom = Frame(Row(3), Row(4), Row(5), Row(6));
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            using Bitmap stable = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { first, second, stableBottom },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Complete, result.State);
            Assert.Equal(AchievementScrollProgressState.ReachedBottom, result.FinalScrollState);
            Assert.Equal(6, result.IdentifiedRows.Count);
            Assert.Equal(6, result.RepeatedObservations);
            Assert.Equal(3, result.ProcessedFrames);
            Assert.Equal(2, result.ScrollActions);
            Assert.Contains("stable viewport", result.Reason);
        }

        [Fact]
        public void PartialRowThatBecomesComplete_IsRecoveredWithoutLosingProvenance()
        {
            AchievementVisibleRow partial = Row(10, AchievementVisibleRowVisibility.Partial);
            AchievementVisibleRow complete = Row(10);
            using Bitmap start = Image(16);
            using Bitmap moved = Image(80);
            using Bitmap stable = Image(80);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(partial), Frame(complete), Frame(complete) },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            AchievementObservedRow observed = Assert.Single(result.IdentifiedRows);
            Assert.Equal(3, observed.ObservationCount);
            Assert.True(observed.EverFullyVisible);
            Assert.Equal(AchievementVisibleRowVisibility.Complete, observed.SelectedObservation.Visibility);
            Assert.Equal(1, result.PartialRowsRecovered);
        }

        [Fact]
        public void LaterUnreadableObservation_DoesNotReplaceStrongerCanonicalEvidence()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(80);
            using Bitmap stable = Image(80);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(10)), Frame(Unresolved("damage"), Row(10)), Frame(Unresolved("damage"), Row(10)) },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Single(result.IdentifiedRows);
            Assert.Equal(2, result.UnresolvedRows.Count);
            Assert.Equal(3, result.IdentifiedRows[0].ObservationCount);
        }

        [Fact]
        public void EquallyStrongConflictingCanonicalObservations_AreMarkedUnresolved()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(80);
            using Bitmap stable = Image(80);
            AchievementVisibleRow completed = Row(10, completion: AchievementCompletionState.CompletedClaimed);
            AchievementVisibleRow incomplete = Row(10, completion: AchievementCompletionState.Incomplete);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(completed), Frame(incomplete), Frame(incomplete) },
                new[] { moved, stable },
                new[] { Evidence(top: true), Evidence(bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Empty(result.IdentifiedRows);
            Assert.Single(result.ConflictingRows);
            Assert.False(result.IsComplete && result.ConflictingRows.Count == 0);
        }

        [Fact]
        public void MixedUniqueStageCollisionAndUnresolved_UsesOnlyTrustedCanonicalFingerprints()
        {
            AchievementVisibleRow unique = Row(10);
            AchievementVisibleRow stage = Row(20, candidateIds: new[] { 20, 21 },
                identity: AchievementIdentityState.StageGroupIdentified, stageGroupId: 20,
                fingerprint: "canonical:20,21");
            AchievementVisibleRow collision = Row(30, candidateIds: new[] { 30, 31 },
                identity: AchievementIdentityState.Ambiguous, canonical: false,
                fingerprint: "diagnostic:let the wind lead");
            using Bitmap start = Image(16);
            using Bitmap stable = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(unique, stage, collision, Unresolved("noise")), Frame(unique, stage, collision, Unresolved("noise")) },
                new[] { stable },
                new[] { Evidence(top: true, bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(2, result.IdentifiedRows.Count);
            Assert.Equal(4, result.UnresolvedRows.Count);
            Assert.DoesNotContain(result.IdentifiedRows, row => row.Key.Contains("let the wind lead"));
        }

        [Fact]
        public void SummaryCardNeverEntersCrawlerObservations()
        {
            using Bitmap start = Image(16);
            using Bitmap stable = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(1)) },
                new[] { stable },
                new[] { Evidence(top: true, bottom: true), Evidence(bottom: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Single(result.ObservedRows);
            Assert.DoesNotContain(result.ObservedRows, row =>
                row.SelectedObservation?.RawTitle == "Achievement Progress");
        }

        [Fact]
        public void UnexpectedCategoryChange_StopsWithoutMergingLaterRows()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(2)) },
                new[] { moved },
                new[] { Evidence(top: true, signature: "A"), Evidence(signature: "B") });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Category", "A"), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(AchievementScrollProgressState.LostScreen, result.FinalScrollState);
            Assert.Contains("signature", result.Reason);
        }

        [Theory]
        [InlineData((int)AchievementScreenState.SearchActive)]
        [InlineData((int)AchievementScreenState.SearchNoResults)]
        [InlineData((int)AchievementScreenState.Unknown)]
        [InlineData((int)AchievementScreenState.NotAchievementScreen)]
        public void UnsupportedStartingScreen_IsRejected(int stateValue)
        {
            using Bitmap start = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame((AchievementScreenState)stateValue, Row(1)) },
                Array.Empty<Bitmap>(),
                new[] { Evidence(top: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(0, result.ScrollActions);
        }

        [Fact]
        public void SearchBecomesActiveAfterScroll_IsReportedAsLostScreen()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(AchievementScreenState.SearchActive, Row(2)) },
                new[] { moved }, new[] { Evidence(top: true), Evidence() });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Equal(AchievementScrollProgressState.LostScreen, result.FinalScrollState);
        }

        [Fact]
        public void UnsupportedStartPosition_ReturnsIncompleteWithoutScrolling()
        {
            using Bitmap start = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)) }, Array.Empty<Bitmap>(), new[] { Evidence() });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                new AchievementCategoryCrawlContext("Category"), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Equal(0, result.ScrollActions);
            Assert.Contains("top", result.Reason);
        }

        [Fact]
        public void ControllerIdleLayout_IsNotAcceptedForMouseWheelTraversal()
        {
            using Bitmap start = Image(16);
            AchievementBulkFrameResult frame = new(
                AchievementScreenState.Idle, new[] { Row(1) }, 17,
                new AchievementBulkFrameTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 1, 1),
                "Controller idle.", isKeyboardMouseIdle: false);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { frame }, Array.Empty<Bitmap>(), new[] { Evidence(top: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Contains("Keyboard/Mouse", result.Reason);
        }

        [Fact]
        public void NoMovementWithoutBottomProof_UsesBoundedRecoveryThenReturnsIncomplete()
        {
            using Bitmap start = Image(16);
            using Bitmap duplicate1 = Image(16);
            using Bitmap duplicate2 = Image(16);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(1)), Frame(Row(1)) },
                new[] { duplicate1, duplicate2 },
                new[] { Evidence(top: true), Evidence(), Evidence() },
                new AchievementCategoryCrawlOptions(maximumNoMovementRecoveryAttempts: 1,
                    scrollSettleMilliseconds: 0));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Equal(2, result.ScrollActions);
            Assert.Equal(AchievementScrollProgressState.NoMovementUnknown, result.FinalScrollState);
            Assert.Contains("bottom proof", result.Reason);
        }

        [Fact]
        public void NoNewCanonicalIdsAfterMovement_DoesNotProveBottom()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(1)) },
                new[] { moved },
                new[] { Evidence(top: true), Evidence() },
                new AchievementCategoryCrawlOptions(maximumCapturedFrames: 2,
                    scrollSettleMilliseconds: 0));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Equal(AchievementScrollProgressState.ScrollAdvanced, result.FinalScrollState);
        }

        [Fact]
        public void MaximumFrameLimit_IsIncomplete()
        {
            using Bitmap start = Image(16);
            using Bitmap moved = Image(64);
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)), Frame(Row(2)) }, new[] { moved },
                new[] { Evidence(top: true), Evidence() },
                new AchievementCategoryCrawlOptions(maximumCapturedFrames: 2,
                    scrollSettleMilliseconds: 0));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Incomplete, result.State);
            Assert.Contains("limit", result.Reason);
        }

        [Fact]
        public void CancellationBetweenFrames_PreservesObservedRows()
        {
            using Bitmap start = Image(16);
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress<AchievementCategoryCrawlProgress>(_ => cancellation.Cancel());
            AchievementCategoryCrawler crawler = Crawler(
                new[] { Frame(Row(1)) }, Array.Empty<Bitmap>(), new[] { Evidence(top: true) });

            AchievementCategoryCrawlResult result = crawler.Crawl(
                Context(), start, cancellation.Token, progress);

            Assert.Equal(AchievementCategoryCrawlState.Cancelled, result.State);
            Assert.Single(result.IdentifiedRows);
            Assert.Equal(AchievementScrollProgressState.Cancelled, result.FinalScrollState);
        }

        [Fact]
        public void CancellationDuringSettling_StopsBeforeCapture()
        {
            using Bitmap start = Image(16);
            using var cancellation = new CancellationTokenSource();
            var capture = new SequenceCapture(Array.Empty<Bitmap>());
            var crawler = new AchievementCategoryCrawler(
                new SequenceFrameReader(new[] { Frame(Row(1)) }), capture,
                new RecordingScrollInput(),
                new SequenceEvidenceDetector(new[] { Evidence(top: true) }),
                new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 1),
                (_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); });

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, cancellation.Token);

            Assert.Equal(AchievementCategoryCrawlState.Cancelled, result.State);
            Assert.Equal(0, capture.Captures);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ForegroundOrCaptureFailure_ReturnsStructuredFailure(bool scrollFailure)
        {
            using Bitmap start = Image(16);
            IAchievementListScrollInput scroll = scrollFailure
                ? new RecordingScrollInput(fail: true)
                : new RecordingScrollInput();
            IGameScreenCapture capture = scrollFailure
                ? new SequenceCapture(Array.Empty<Bitmap>())
                : new ThrowingCapture();
            var crawler = new AchievementCategoryCrawler(
                new SequenceFrameReader(new[] { Frame(Row(1)) }), capture, scroll,
                new SequenceEvidenceDetector(new[] { Evidence(top: true) }),
                new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 0));

            AchievementCategoryCrawlResult result = crawler.Crawl(Context(), start, CancellationToken.None);

            Assert.Equal(AchievementCategoryCrawlState.Failed, result.State);
            Assert.Single(result.IdentifiedRows);
        }

        [Fact]
        public void WindowsScrollInput_RejectsLeftPanelTarget_AndUsesNormalizedRightPanelTarget()
        {
            var pointer = new RecordingPointer();
            var foreground = new ForegroundActivator { IsForegroundValue = true };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AchievementWindowsListScrollInput(pointer, foreground, targetX: 0.20));
            var input = new AchievementWindowsListScrollInput(pointer, foreground);

            AchievementListScrollCommandResult result = input.Scroll(-360, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal((AchievementWindowsListScrollInput.ScrollTargetX,
                AchievementWindowsListScrollInput.ScrollTargetY), pointer.Position);
            Assert.Equal(-360, pointer.WheelDelta);
        }

        [Fact]
        public void WindowsScrollInput_DialogGatePreventsWheelInput()
        {
            var pointer = new RecordingPointer();
            var foreground = new ForegroundActivator { IsForegroundValue = true };
            var input = new AchievementWindowsListScrollInput(
                pointer, foreground, inputAllowed: () => false);

            AchievementListScrollCommandResult result = input.Scroll(-360, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(0, pointer.WheelDelta);
            Assert.Contains("dialog", result.FailureReason);
        }

        [Fact]
        public void ViewportFingerprint_ExcludesPointerTargetButDetectsListChange()
        {
            using Bitmap original = Image(32);
            using Bitmap cursorOnly = Image(32);
            using Bitmap changed = Image(96);
            int x = (int)Math.Round((cursorOnly.Width - 1) * AchievementWindowsListScrollInput.ScrollTargetX);
            int y = (int)Math.Round((cursorOnly.Height - 1) * AchievementWindowsListScrollInput.ScrollTargetY);
            using (Graphics graphics = Graphics.FromImage(cursorOnly))
                graphics.FillRectangle(Brushes.White, x - 20, y - 20, 40, 40);

            AchievementViewportFingerprint first = AchievementViewportFingerprint.Create(original);
            AchievementViewportFingerprint cursor = AchievementViewportFingerprint.Create(cursorOnly);
            AchievementViewportFingerprint different = AchievementViewportFingerprint.Create(changed);

            Assert.Equal(first, cursor);
            Assert.NotEqual(first, different);
        }

        [Fact]
        public void UncalibratedProductionEvidence_NeverClaimsTopOrBottom()
        {
            using Bitmap screenshot = Image(32);
            AchievementCategoryFrameEvidence evidence =
                new UncalibratedAchievementCategoryFrameEvidenceDetector().Detect(screenshot);

            Assert.False(evidence.TopAtExtent);
            Assert.False(evidence.BottomAtExtent);
            Assert.False(evidence.CategorySignatureVerified);
        }

        private static AchievementCategoryCrawler Crawler(
            IEnumerable<AchievementBulkFrameResult> frames,
            IEnumerable<Bitmap> captures,
            IEnumerable<AchievementCategoryFrameEvidence> evidence,
            AchievementCategoryCrawlOptions options = null) => new(
                new SequenceFrameReader(frames),
                new SequenceCapture(captures),
                new RecordingScrollInput(),
                new SequenceEvidenceDetector(evidence),
                options ?? new AchievementCategoryCrawlOptions(scrollSettleMilliseconds: 0),
                (_, token) => token.ThrowIfCancellationRequested());

        private static AchievementCategoryCrawlContext Context() =>
            new("Category", knownTraversalOrigin: true);

        private static AchievementCategoryFrameEvidence Evidence(
            bool top = false, bool bottom = false, string signature = null) => new(
                top, bottom, signature ?? string.Empty, signature != null, "Synthetic evidence.");

        private static AchievementBulkFrameResult Frame(params AchievementVisibleRow[] rows) =>
            Frame(AchievementScreenState.Idle, rows);

        private static AchievementBulkFrameResult Frame(
            AchievementScreenState state, params AchievementVisibleRow[] rows) => new(
                state, rows, 17,
                new AchievementBulkFrameTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, rows.Length, rows.Length),
                "Synthetic frame.");

        private static AchievementVisibleRow Row(
            int id,
            AchievementVisibleRowVisibility visibility = AchievementVisibleRowVisibility.Complete,
            AchievementCompletionState completion = AchievementCompletionState.CompletedClaimed,
            IEnumerable<int> candidateIds = null,
            AchievementIdentityState identity = AchievementIdentityState.Identified,
            int? stageGroupId = null,
            bool canonical = true,
            string fingerprint = null)
        {
            int[] ids = (candidateIds ?? new[] { id }).ToArray();
            return new AchievementVisibleRow(
                id, new NormalizedRectangle(0.38, Math.Min(0.8, id / 100.0), 0.56, 0.1),
                visibility, "Title " + id, "title " + id, identity, ids, stageGroupId,
                completion, completion == AchievementCompletionState.Incomplete ? 0 : null,
                completion == AchievementCompletionState.Incomplete ? 1 : null,
                null, 100, 0, 0.99f, false,
                new AchievementRowFingerprint(fingerprint ??
                    (canonical ? "canonical:" + string.Join(",", ids) : "diagnostic:" + id), canonical),
                null, null, null, "Synthetic row.");
        }

        private static AchievementVisibleRow Unresolved(string value) => new(
            0, new NormalizedRectangle(0.38, 0.2, 0.56, 0.1),
            AchievementVisibleRowVisibility.Uncertain, value, value,
            AchievementIdentityState.Unresolved, Array.Empty<int>(), null,
            AchievementCompletionState.Unresolved, null, null, null, 0, 0, 0,
            false, new AchievementRowFingerprint("diagnostic:" + value, false),
            null, null, null, "Synthetic unresolved row.");

        private static Bitmap Image(byte value)
        {
            var bitmap = new Bitmap(1919, 1079);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.FromArgb(value, value, value));
            return bitmap;
        }

        private sealed class SequenceFrameReader : IAchievementListFrameReader
        {
            private readonly Queue<AchievementBulkFrameResult> frames;
            internal SequenceFrameReader(IEnumerable<AchievementBulkFrameResult> frames) =>
                this.frames = new Queue<AchievementBulkFrameResult>(frames);
            public AchievementBulkFrameResult Read(Bitmap screenshot, TimeSpan? captureDuration = null) => frames.Dequeue();
        }

        private sealed class SequenceCapture : IGameScreenCapture
        {
            private readonly Queue<Bitmap> frames;
            internal SequenceCapture(IEnumerable<Bitmap> frames) =>
                this.frames = new Queue<Bitmap>(frames.Select(frame => new Bitmap(frame)));
            internal int Captures { get; private set; }
            public Bitmap CaptureWindow() { Captures++; return frames.Dequeue(); }
        }

        private sealed class ThrowingCapture : IGameScreenCapture
        {
            public Bitmap CaptureWindow() => throw new InvalidOperationException("Capture failed.");
        }

        private sealed class SequenceEvidenceDetector : IAchievementCategoryFrameEvidenceDetector
        {
            private readonly Queue<AchievementCategoryFrameEvidence> evidence;
            internal SequenceEvidenceDetector(IEnumerable<AchievementCategoryFrameEvidence> evidence) =>
                this.evidence = new Queue<AchievementCategoryFrameEvidence>(evidence);
            public AchievementCategoryFrameEvidence Detect(Bitmap screenshot) => evidence.Dequeue();
        }

        private sealed class RecordingScrollInput : IAchievementListScrollInput
        {
            private readonly bool fail;
            internal RecordingScrollInput(bool fail = false) => this.fail = fail;
            public AchievementListScrollCommandResult Scroll(int wheelDelta, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new AchievementListScrollCommandResult(!fail, !fail,
                    fail ? "Foreground verification failed." : null, "Synthetic scroll.");
            }
        }

        private sealed class RecordingPointer : IAchievementPointerInput
        {
            internal (double X, double Y) Position { get; private set; }
            internal int WheelDelta { get; private set; }
            public void MoveNormalized(double x, double y, CancellationToken cancellationToken) => Position = (x, y);
            public bool IsAtNormalized(double x, double y) => Position == (x, y);
            public void Click(CancellationToken cancellationToken) { }
            public void ScrollWheel(int delta, CancellationToken cancellationToken) => WheelDelta = delta;
        }

        private sealed class ForegroundActivator : IGameForegroundActivator
        {
            internal bool IsForegroundValue { get; set; }
            public bool IsForeground => IsForegroundValue;
            public void Activate() => IsForegroundValue = true;
        }

        private sealed class ImmediateProgress<T> : IProgress<T>
        {
            private readonly Action<T> report;
            internal ImmediateProgress(Action<T> report) => this.report = report;
            public void Report(T value) => report(value);
        }
    }
}
