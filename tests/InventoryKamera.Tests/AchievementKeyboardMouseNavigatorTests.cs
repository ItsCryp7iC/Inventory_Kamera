using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementKeyboardMouseNavigatorTests
    {
        [Fact]
        public void Search_UsesMouseTextAndEnterThenCapturesExactlyOnce()
        {
            var events = new List<string>();
            var capture = new RecordingCapture(events);
            var detector = new QueueDetector(events, Active(1));
            var navigator = Create(events, capture, detector);

            using AchievementSearchNavigationResult result = navigator.Search(
                "You There! Fly!",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(1, result.Attempts);
            Assert.Equal(1, capture.Count);
            Assert.Equal(1, detector.Count);
            Assert.NotNull(result.ResultScreenshot);
            Assert.Equal(new[]
            {
                "Move:toggle",
                "Click",
                "Type:You There! Fly!",
                "Enter",
                "Capture",
                "Detect:SearchActive",
            }, events);
            Assert.Contains("inputBackend=keyboard-mouse-sendinput", result.Diagnostics);
        }

        [Fact]
        public void Search_CorroboratedUnknownSingleResultIsAccepted()
        {
            var events = new List<string>();
            var navigator = Create(
                events,
                new RecordingCapture(events),
                new QueueDetector(events, UnknownWithSingleResult()));

            using AchievementSearchNavigationResult result = navigator.Search("Pur", CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(AchievementScreenState.Unknown, result.AfterDetection.ScreenState);
        }

        [Fact]
        public void Search_ExplicitNoResultsForExactVisibleQueryIsSuccessfulSubmission()
        {
            var events = new List<string>();
            var capture = new RecordingCapture(events);
            var navigator = Create(
                events,
                capture,
                new QueueDetector(events, NoResults("No Air Force")));

            using AchievementSearchNavigationResult result = navigator.Search(
                "No Air Force",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(1, result.Attempts);
            Assert.Equal(AchievementScreenState.SearchNoResults, result.AfterDetection.ScreenState);
            Assert.Equal(AchievementResultCountState.NoResults, result.AfterDetection.ResultCountState);
            Assert.Equal(1, capture.Count);
            Assert.Contains("noResultsForSubmittedQuery=True", result.Diagnostics);
        }

        [Fact]
        public void Search_StructuralNoResultsDoesNotRequireQueryOcrReadback()
        {
            var events = new List<string>();
            var navigator = Create(
                events,
                new RecordingCapture(events),
                new QueueDetector(
                    events,
                    NoResults("Some Other Title"),
                    NoResults("Some Other Title")));

            using AchievementSearchNavigationResult result = navigator.Search(
                "No Air Force",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(1, result.Attempts);
            Assert.Equal("No Air Force", result.SubmittedQuery);
            Assert.Contains("noResultsForSubmittedQuery=True", result.Diagnostics);
        }

        [Fact]
        public void Search_UsesOneBoundedRetryWhenFirstSubmissionIsUnrecognized()
        {
            var events = new List<string>();
            var capture = new RecordingCapture(events);
            var detector = new QueueDetector(events, Unknown(), Active(1));
            var navigator = Create(events, capture, detector);

            using AchievementSearchNavigationResult result = navigator.Search("Pur", CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(2, result.Attempts);
            Assert.Equal(2, capture.Count);
            Assert.Equal(2, detector.Count);
            Assert.Equal(3, events.FindAll(value => value == "Click").Count);
            Assert.Single(events.FindAll(value => value == "Type:Pur"));
            Assert.Single(events.FindAll(value => value == "Replace:Pur"));
            Assert.Equal(2, events.FindAll(value => value == "Enter").Count);
        }

        [Fact]
        public void Search_StopsAfterTwoUnrecognizedSubmissions()
        {
            var events = new List<string>();
            var capture = new RecordingCapture(events);
            var navigator = Create(events, capture, new QueueDetector(events, Unknown(), Unknown()));

            using AchievementSearchNavigationResult result = navigator.Search("Pur", CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(2, result.Attempts);
            Assert.Equal(2, capture.Count);
            Assert.Null(result.ResultScreenshot);
        }

        [Fact]
        public void Search_CancelledBeforeStartSendsNoInput()
        {
            var events = new List<string>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var navigator = Create(
                events,
                new RecordingCapture(events),
                new QueueDetector(events, Active(1)));

            using AchievementSearchNavigationResult result = navigator.Search("Pur", cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Empty(events);
        }

        [Fact]
        public void Search_CancellationAfterFocusStopsBeforeTextInput()
        {
            var events = new List<string>();
            using var cancellation = new CancellationTokenSource();
            var navigator = Create(
                new CancellingPointer(events, cancellation),
                new RecordingText(events),
                new RecordingCapture(events),
                new QueueDetector(events, Active(1)));

            using AchievementSearchNavigationResult result = navigator.Search("Pur", cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Equal(new[] { "Move:toggle", "Click" }, events);
        }

        [Fact]
        public void Search_CancellationAfterTextStopsBeforeEnter()
        {
            var events = new List<string>();
            using var cancellation = new CancellationTokenSource();
            var navigator = Create(
                new RecordingPointer(events),
                new CancellingText(events, cancellation, cancelOnEnter: false),
                new RecordingCapture(events),
                new QueueDetector(events, Active(1)));

            using AchievementSearchNavigationResult result = navigator.Search("Pur", cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Equal(new[] { "Move:toggle", "Click", "Type:Pur" }, events);
        }

        [Fact]
        public void Search_CancellationAfterEnterStopsBeforeCapture()
        {
            var events = new List<string>();
            using var cancellation = new CancellationTokenSource();
            var navigator = Create(
                new RecordingPointer(events),
                new CancellingText(events, cancellation, cancelOnEnter: true),
                new RecordingCapture(events),
                new QueueDetector(events, Active(1)));

            using AchievementSearchNavigationResult result = navigator.Search("Pur", cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Equal(new[] { "Move:toggle", "Click", "Type:Pur", "Enter" }, events);
        }

        [Fact]
        public void ClearSearch_UsesOneVisibleClearClickWhenIdleIsVerified()
        {
            var events = new List<string>();
            var navigator = Create(
                events,
                new RecordingCapture(events),
                new QueueDetector(events, Idle()));

            using AchievementSearchNavigationResult result = navigator.ClearSearch(CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(1, result.Attempts);
            Assert.Single(events.FindAll(value => value == "Click"));
        }

        [Fact]
        public void ClearSearch_CancelledBeforeStartSendsNoInput()
        {
            var events = new List<string>();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var navigator = Create(
                events,
                new RecordingCapture(events),
                new QueueDetector(events, Idle()));

            using AchievementSearchNavigationResult result = navigator.ClearSearch(cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Empty(events);
        }

        [Fact]
        public void ClearSearch_UsesAtMostOneBoundedSecondClick()
        {
            var events = new List<string>();
            var navigator = Create(
                events,
                new RecordingCapture(events),
                new QueueDetector(events, Unknown(), Idle()));

            using AchievementSearchNavigationResult result = navigator.ClearSearch(CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(2, result.Attempts);
            Assert.Equal(2, events.FindAll(value => value == "Click").Count);
        }

        [Fact]
        public void PreparedSteadyState_MovesOnceAndUsesSamePointForTwoTargets()
        {
            var events = new List<string>();
            var pointer = new RecordingPointer(events);
            var navigator = Create(
                pointer,
                new RecordingText(events),
                new RecordingCapture(events),
                new QueueDetector(events, Active(1), Idle(), Active(1), Idle()));

            navigator.PreparePointer(CancellationToken.None, forceReposition: true);
            using (navigator.Search("A", CancellationToken.None)) { }
            using (navigator.ClearSearch(CancellationToken.None)) { }
            using (navigator.Search("B", CancellationToken.None)) { }
            using (navigator.ClearSearch(CancellationToken.None)) { }

            Assert.Single(events.FindAll(value => value == "Move:toggle"));
            Assert.Equal(4, events.FindAll(value => value == "Click").Count);
            Assert.Equal(new[] { "Type:A", "Type:B" },
                events.FindAll(value => value.StartsWith("Type:", StringComparison.Ordinal)));
            Assert.Empty(events.FindAll(value => value.StartsWith("Replace:", StringComparison.Ordinal)));
        }

        [Fact]
        public void ExternallyMovedCursor_IsRepositionedAsBoundedFallback()
        {
            var events = new List<string>();
            var pointer = new RecordingPointer(events);
            var navigator = Create(
                pointer,
                new RecordingText(events),
                new RecordingCapture(events),
                new QueueDetector(events, Active(1), Idle(), Active(1)));

            navigator.PreparePointer(CancellationToken.None, forceReposition: true);
            using (navigator.Search("A", CancellationToken.None)) { }
            using (navigator.ClearSearch(CancellationToken.None)) { }
            pointer.SimulateExternalMove();
            using (navigator.Search("B", CancellationToken.None)) { }

            Assert.Equal(2, events.FindAll(value => value == "Move:toggle").Count);
        }

        [Fact]
        public void DefaultTimingMatchesConservativeFastPath()
        {
            AchievementSearchNavigationTiming timing = AchievementSearchNavigationTiming.Default;

            Assert.Equal(100, timing.FocusSettleMs);
            Assert.Equal(100, timing.AfterTypingSettleMs);
            Assert.Equal(300, timing.ResultSettleMs);
            Assert.Equal(250, timing.ClearSettleMs);
            Assert.Equal(40, timing.PointerSettleMs);
            Assert.Equal(2, timing.SearchAttempts);
            Assert.Equal(2, timing.ClearAttempts);
        }

        private static AchievementKeyboardMouseNavigator Create(
            List<string> events,
            IGameScreenCapture capture,
            IAchievementScreenDetector detector) => Create(
                new RecordingPointer(events),
                new RecordingText(events),
                capture,
                detector);

        private static AchievementKeyboardMouseNavigator Create(
            IAchievementPointerInput pointer,
            IGameTextInput text,
            IGameScreenCapture capture,
            IAchievementScreenDetector detector) => new(
                pointer,
                text,
                detector,
                capture,
                new Foreground(),
                new AchievementSearchNavigationTiming(0, 0, 0, 0, 0, 0, 2, 2),
                (_, token) => token.ThrowIfCancellationRequested());

        private static AchievementScreenDetection Idle() => AchievementScreenDetector.Classify(
            new Size(1920, 1080),
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(idlePlaceholderAccepted: true, rawText: "Search Achievements"),
            AchievementTextEvidence.ForTest("Completed 1", true),
            AchievementMatchCountParser.Parse(string.Empty),
            AchievementPageCounterParser.Parse(string.Empty));

        private static AchievementScreenDetection Active(int count) => AchievementScreenDetector.Classify(
            new Size(1920, 1080),
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(activeControlsAccepted: true, rawText: "Clear"),
            AchievementTextEvidence.ForTest("Completed 1", true),
            AchievementMatchCountParser.Parse($"Match(es): {count}"),
            AchievementPageCounterParser.Parse("1/1"));

        private static AchievementScreenDetection NoResults(string visibleQuery) =>
            AchievementScreenDetector.Classify(
                new Size(1920, 1080),
                AchievementTextEvidence.ForTest("Achievements", true),
                AchievementSearchFieldEvidence.ForTest(
                    activeControlsAccepted: true,
                    rawText: visibleQuery + " Clear"),
                AchievementTextEvidence.ForTest("Completed 1", true),
                AchievementMatchCountParser.Parse(string.Empty),
                AchievementPageCounterParser.Parse(string.Empty),
                noResultsEvidence: AchievementTextEvidence.ForTest(
                    string.Empty,
                    false),
                resultRowEvidence: AchievementResultRowEvidence.ForTest(
                    AchievementResultRowPresence.Absent));

        private static AchievementScreenDetection UnknownWithSingleResult() => new(
            AchievementScreenState.Unknown,
            AchievementTextEvidence.ForTest("", false),
            AchievementSearchFieldEvidence.ForTest(),
            AchievementTextEvidence.ForTest("", false),
            AchievementTextEvidence.ForTest("Match(es): 1", true),
            AchievementMatchCountParser.Parse("Match(es): 1"),
            AchievementTextEvidence.ForTest("1/1", true),
            AchievementPageCounterParser.Parse("1/1"),
            AchievementScreenRegions.For(new Size(1920, 1080)),
            "test transient unknown");

        private static AchievementScreenDetection Unknown() => AchievementScreenDetector.Classify(
            new Size(1920, 1080),
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(),
            AchievementTextEvidence.ForTest("Completed 1", true),
            AchievementMatchCountParser.Parse(string.Empty),
            AchievementPageCounterParser.Parse(string.Empty));

        private sealed class RecordingPointer : IAchievementPointerInput
        {
            private readonly List<string> events;
            internal RecordingPointer(List<string> events) => this.events = events;
            private bool atToggle;
            internal void SimulateExternalMove() => atToggle = false;
            public void MoveNormalized(double x, double y, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.Equal(AchievementKeyboardMouseNavigator.SearchToggleX, x, 6);
                Assert.Equal(AchievementKeyboardMouseNavigator.SearchToggleY, y, 6);
                atToggle = true;
                events.Add("Move:toggle");
            }
            public bool IsAtNormalized(double x, double y) => atToggle;
            public void Click(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                events.Add("Click");
            }
        }

        private sealed class RecordingText : IGameTextInput
        {
            private readonly List<string> events;
            internal RecordingText(List<string> events) => this.events = events;
            public void TypeExact(string text, CancellationToken cancellationToken) => events.Add("Type:" + text);
            public void ReplaceAll(string text, CancellationToken cancellationToken) => events.Add("Replace:" + text);
            public void PressEnter(CancellationToken cancellationToken) => events.Add("Enter");
        }

        private sealed class CancellingPointer : IAchievementPointerInput
        {
            private readonly List<string> events;
            private readonly CancellationTokenSource cancellation;
            internal CancellingPointer(List<string> events, CancellationTokenSource cancellation)
            {
                this.events = events;
                this.cancellation = cancellation;
            }
            public void MoveNormalized(double x, double y, CancellationToken cancellationToken) =>
                events.Add("Move:toggle");
            public bool IsAtNormalized(double x, double y) => true;
            public void Click(CancellationToken cancellationToken)
            {
                events.Add("Click");
                cancellation.Cancel();
            }
        }

        private sealed class CancellingText : IGameTextInput
        {
            private readonly List<string> events;
            private readonly CancellationTokenSource cancellation;
            private readonly bool cancelOnEnter;
            internal CancellingText(
                List<string> events,
                CancellationTokenSource cancellation,
                bool cancelOnEnter)
            {
                this.events = events;
                this.cancellation = cancellation;
                this.cancelOnEnter = cancelOnEnter;
            }
            public void TypeExact(string text, CancellationToken cancellationToken)
            {
                events.Add("Type:" + text);
                if (!cancelOnEnter) cancellation.Cancel();
            }
            public void ReplaceAll(string text, CancellationToken cancellationToken)
            {
                events.Add("Replace:" + text);
                if (!cancelOnEnter) cancellation.Cancel();
            }
            public void PressEnter(CancellationToken cancellationToken)
            {
                events.Add("Enter");
                if (cancelOnEnter) cancellation.Cancel();
            }
        }

        private sealed class RecordingCapture : IGameScreenCapture
        {
            private readonly List<string> events;
            internal RecordingCapture(List<string> events) => this.events = events;
            internal int Count { get; private set; }
            public Bitmap CaptureWindow()
            {
                Count++;
                events.Add("Capture");
                return new Bitmap(2, 2);
            }
        }

        private sealed class QueueDetector : IAchievementScreenDetector
        {
            private readonly List<string> events;
            private readonly Queue<AchievementScreenDetection> values;
            internal QueueDetector(List<string> events, params AchievementScreenDetection[] values)
            {
                this.events = events;
                this.values = new Queue<AchievementScreenDetection>(values);
            }
            internal int Count { get; private set; }
            public AchievementScreenDetection Detect(Bitmap screenshot)
            {
                Count++;
                AchievementScreenDetection value = values.Dequeue();
                events.Add("Detect:" + value.ScreenState);
                return value;
            }
        }

        private sealed class Foreground : IGameForegroundActivator
        {
            public bool IsForeground => true;
            public void Activate() { }
        }
    }
}
