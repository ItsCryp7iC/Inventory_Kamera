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
    public sealed class AchievementSearchNavigatorTests
    {
        [Fact]
        public void VerifiedIdle_OpensFocusesTypesThenSubmitsInOrder()
        {
            var harness = Harness.ForSearch(1);

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "You There! Fly!",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.False(result.Cancelled);
            Assert.Equal(AchievementScreenState.Idle, result.BeforeDetection.ScreenState);
            Assert.Equal(AchievementSearchEditorState.Focused, result.AfterOpenFocusDetection.State);
            Assert.Equal(AchievementScreenState.SearchActive, result.AfterDetection.ScreenState);
            Assert.Equal(1, result.AfterDetection.MatchCountValue);
            Assert.Contains("postTextFocusAttemptCount=1", result.Diagnostics);
            Assert.Equal(1, CountOccurrences(result.Diagnostics, "transition=typed;"));
            AssertOrdered(
                harness.Events,
                "Button:Search:True",
                "Focus:Focused",
                "Text:You There! Fly!",
                "Button:Search:True",
                "Screen:SearchActive");
        }

        [Fact]
        public void PostTextUnknownThenFocused_RetriesAndSubmitsOnce()
        {
            var harness = Harness.ForPostTextFocus(
                UnknownFocus(),
                Focused());

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Megastar in Mondstadt",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(2, Count(harness.Events, "Button:Search:True"));
            Assert.Equal(2, CountOccurrences(result.Diagnostics, "transition=typed;"));
            Assert.Contains("postTextFocusAttemptCount=2", result.Diagnostics);
            Assert.Contains("transition=typed; attempt=1;", result.Diagnostics);
            Assert.Contains("state=Unknown; brightestBorderRowRatio=0.250", result.Diagnostics);
            Assert.Contains("transition=typed; attempt=2;", result.Diagnostics);
            Assert.Contains("state=Focused; brightestBorderRowRatio=0.950", result.Diagnostics);
        }

        [Fact]
        public void PostTextNotFocusedThenUnknownThenFocused_RetriesAndSubmitsOnce()
        {
            var harness = Harness.ForPostTextFocus(
                NotFocused(),
                UnknownFocus(),
                Focused());

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Megastar in Mondstadt",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(2, Count(harness.Events, "Button:Search:True"));
            Assert.Equal(3, CountOccurrences(result.Diagnostics, "transition=typed;"));
            Assert.Contains("postTextFocusAttemptCount=3", result.Diagnostics);
            Assert.Contains("state=NotFocused; brightestBorderRowRatio=0.050", result.Diagnostics);
        }

        [Fact]
        public void AllPostTextChecksUnknown_FailsClosedWithoutSubmission()
        {
            var harness = Harness.ForPostTextFocus(
                UnknownFocus(),
                UnknownFocus(),
                UnknownFocus());

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Megastar in Mondstadt",
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.False(result.Cancelled);
            Assert.Equal(1, Count(harness.Events, "Button:Search:True"));
            Assert.Equal(3, CountOccurrences(result.Diagnostics, "transition=typed;"));
            Assert.Contains("across 3 bounded capture attempts", result.FailureReason);
            Assert.Contains("postTextFocusAttemptCount=3", result.Diagnostics);
        }

        [Fact]
        public void AllPostTextChecksNotFocused_FailsClosedWithoutSubmission()
        {
            var harness = Harness.ForPostTextFocus(
                NotFocused(),
                NotFocused(),
                NotFocused());

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Megastar in Mondstadt",
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.False(result.Cancelled);
            Assert.Equal(1, Count(harness.Events, "Button:Search:True"));
            Assert.Equal(3, CountOccurrences(result.Diagnostics, "transition=typed;"));
            Assert.Contains("state=NotFocused; brightestBorderRowRatio=0.050", result.Diagnostics);
        }

        [Fact]
        public void CancellationDuringPostTextRetryWait_DoesNotSubmit()
        {
            using var cancellation = new CancellationTokenSource();
            var harness = Harness.ForPostTextFocus(
                new[] { UnknownFocus(), Focused() },
                retrySettleMs: 7,
                navigationWait: (milliseconds, token) =>
                {
                    if (milliseconds == 7) cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                });

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Megastar in Mondstadt",
                cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Equal(1, Count(harness.Events, "Button:Search:True"));
            Assert.Equal(2, harness.FocusDetector.DetectCount);
            Assert.Contains("postTextFocusAttemptCount=1", result.Diagnostics);
        }

        [Fact]
        public void CancellationImmediatelyAfterRecoveredFocus_DoesNotSubmit()
        {
            using var cancellation = new CancellationTokenSource();
            var harness = Harness.ForPostTextFocus(
                UnknownFocus(),
                Focused());
            harness.FocusDetector.AfterDetect = count =>
            {
                if (count == 3) cancellation.Cancel();
            };

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Megastar in Mondstadt",
                cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Equal(1, Count(harness.Events, "Button:Search:True"));
            Assert.Contains("postTextFocusAttemptCount=2", result.Diagnostics);
        }

        [Theory]
        [InlineData((int)AchievementScreenState.NotAchievementScreen)]
        [InlineData((int)AchievementScreenState.Unknown)]
        [InlineData((int)AchievementScreenState.SearchActive)]
        public void NonIdleStart_RefusesControllerAndTextInput(int stateValue)
        {
            var state = (AchievementScreenState)stateValue;
            var harness = Harness.ForStartState(state);

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Pur",
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.False(result.Cancelled);
            Assert.Contains("Idle start", result.FailureReason);
            Assert.DoesNotContain(harness.Events, value => value.StartsWith("Button:", StringComparison.Ordinal));
            Assert.DoesNotContain(harness.Events, value => value.StartsWith("Text:", StringComparison.Ordinal));
        }

        [Fact]
        public void FocusCannotBeProven_DoesNotTypeOrSubmitAndIsBounded()
        {
            var harness = Harness.ForFocusFailure(focusAttempts: 3);

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Pur",
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("no text was entered", result.FailureReason);
            Assert.Equal(3, harness.FocusDetector.DetectCount);
            Assert.Equal(1, Count(harness.Events, "Button:Search:True"));
            Assert.DoesNotContain(harness.Events, value => value.StartsWith("Text:", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        public void SearchActive_ObservedPositiveMatchCountsAreNavigationSuccess(int matchCount)
        {
            var harness = Harness.ForSearch(matchCount);

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "exact requested text",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(matchCount, result.AfterDetection.MatchCountValue);
            Assert.Equal("exact requested text", harness.TextInput.LastText);
        }

        [Fact]
        public void LiveZeroResultContract_UnknownAfterSubmissionFailsClosedWithoutMoreInput()
        {
            var harness = Harness.ForUnknownResult(resultAttempts: 3);
            const string query = "ZZZ Inventory Kamera Navigation Test ZZZ";

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                query,
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.False(result.Cancelled);
            Assert.Equal(AchievementScreenState.Unknown, result.AfterDetection.ScreenState);
            Assert.Null(result.AfterDetection.MatchCountValue);
            Assert.Null(result.AfterDetection.ResultPageCurrent);
            Assert.Null(result.AfterDetection.ResultPageTotal);
            Assert.Equal(5, Count(harness.Events, "Screen:Unknown"));
            Assert.Equal(5, result.Attempts);
            Assert.Equal(2, Count(harness.Events, "Button:Search:True"));
            Assert.Equal(2, Count(harness.Events, "Button:Search:False"));
            Assert.Equal(1, Count(harness.Events, "Text:" + query));
            Assert.DoesNotContain("Button:Back:True", harness.Events);
            Assert.Equal(3, CountOccurrences(result.Diagnostics, "transition=submit;"));
            Assert.Contains(
                "could not be positively verified after 3 bounded capture attempts; observed Unknown",
                result.FailureReason);
            Assert.Contains("afterState=Unknown", result.Diagnostics);
            Assert.Contains("matchCount=(none)", result.Diagnostics);
            Assert.DoesNotContain("identity", result.Diagnostics.ToLowerInvariant());
            Assert.DoesNotContain("completion", result.Diagnostics.ToLowerInvariant());
        }

        [Fact]
        public void ExitSearch_OneBackThenVerifiedIdleSucceeds()
        {
            var harness = Harness.ForExit(Idle());

            AchievementSearchNavigationResult result = harness.Navigator.ExitSearch(
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(AchievementScreenState.SearchActive, result.BeforeDetection.ScreenState);
            Assert.Equal(AchievementScreenState.Idle, result.AfterDetection.ScreenState);
            Assert.Equal(1, Count(harness.Events, "Button:Back:True"));
        }

        [Fact]
        public void ExitSearch_RemainsActiveFailsAfterBoundedCaptureRetriesWithoutMoreBackInputs()
        {
            var harness = Harness.ForExit(
                Active(1),
                Active(1),
                Active(1));

            AchievementSearchNavigationResult result = harness.Navigator.ExitSearch(
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("within 3 capture attempts", result.FailureReason);
            Assert.Equal(3, result.Attempts);
            Assert.Equal(1, Count(harness.Events, "Button:Back:True"));
        }

        [Fact]
        public void CancellationBeforeInputSendsNothingAndReturnsCancelledResult()
        {
            var harness = Harness.ForSearch(1);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Pur",
                cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.False(result.Succeeded);
            Assert.Empty(harness.Events);
        }

        [Fact]
        public void CancellationDuringFocusRetryStopsAdditionalCaptureAndInput()
        {
            using var cancellation = new CancellationTokenSource();
            var harness = Harness.ForFocusFailure(
                focusAttempts: 3,
                navigationWait: (milliseconds, token) =>
                {
                    if (milliseconds == 2) cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                },
                openSettleMs: 1,
                retrySettleMs: 2);

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Pur",
                cancellation.Token);

            Assert.True(result.Cancelled);
            Assert.Equal(1, harness.FocusDetector.DetectCount);
            Assert.Equal(1, Count(harness.Events, "Button:Search:True"));
            Assert.DoesNotContain(harness.Events, value => value.StartsWith("Text:", StringComparison.Ordinal));
        }

        [Fact]
        public void ForegroundFailureSendsNoControllerOrTextInput()
        {
            var harness = Harness.ForSearch(1);
            harness.Foreground.ThrowOnActivate = true;
            harness.Foreground.IsForeground = false;

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Pur",
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("foreground", result.FailureReason, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(harness.Events, value => value.StartsWith("Button:", StringComparison.Ordinal));
            Assert.DoesNotContain(harness.Events, value => value.StartsWith("Text:", StringComparison.Ordinal));
        }

        [Fact]
        public void ForegroundIsReestablishedBeforeTextAndBeforeSubmit()
        {
            Harness harness = null;
            harness = Harness.ForSearch(
                1,
                openSettleMs: 11,
                afterTypingSettleMs: 22,
                navigationWait: (milliseconds, token) =>
                {
                    if (milliseconds == 11 || milliseconds == 22)
                        harness.Foreground.IsForeground = false;
                    token.ThrowIfCancellationRequested();
                });

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Pur",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(2, Count(harness.Events, "ActivateForeground"));
            AssertOrdered(
                harness.Events,
                "ActivateForeground",
                "Focus:Focused",
                "Text:Pur",
                "ActivateForeground",
                "Focus:Focused",
                "Button:Search:True");
        }

        [Fact]
        public void EveryVerificationCaptureUsesTheSharedOverlayGuardBoundary()
        {
            var events = new List<string>();
            var guard = new RecordingGuard(events);
            var capture = new GuardedCapture(events, guard);
            var harness = Harness.ForSearch(1, events, capture);

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                "Pur",
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Diagnostics);
            Assert.Equal(4, capture.CaptureCount);
            Assert.Equal(4, Count(events, "OverlayHidden"));
            Assert.Equal(4, Count(events, "OverlayRestored"));
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] != "Capture") continue;
                Assert.Equal("OverlayHidden", events[i - 1]);
                Assert.Equal("OverlayRestored", events[i + 1]);
            }
        }

        [Fact]
        public void DiagnosticsAreBoundedAndContainRequiredNavigationFieldsWithoutQueryText()
        {
            const string query = "A private query that diagnostics should not retain";
            var harness = Harness.ForSearch(5);

            AchievementSearchNavigationResult result = harness.Navigator.Search(
                query,
                CancellationToken.None);

            Assert.Contains("operation=search", result.Diagnostics);
            Assert.Contains($"queryLength={query.Length}", result.Diagnostics);
            Assert.Contains("attempt=", result.Diagnostics);
            Assert.Contains("beforeState=Idle", result.Diagnostics);
            Assert.Contains("afterState=SearchActive", result.Diagnostics);
            Assert.Contains("matchCount=5", result.Diagnostics);
            Assert.Contains("page=1/1", result.Diagnostics);
            Assert.Contains("focusEvidence=", result.Diagnostics);
            Assert.Contains("cancelled=False", result.Diagnostics);
            Assert.Contains("reason=success", result.Diagnostics);
            Assert.DoesNotContain(query, result.Diagnostics);
            Assert.True(result.Diagnostics.Length < 4096);
        }

        private static int Count(IEnumerable<string> events, string expected)
        {
            int count = 0;
            foreach (string value in events)
                if (value == expected) count++;
            return count;
        }

        private static int CountOccurrences(string value, string expected)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(expected, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += expected.Length;
            }
            return count;
        }

        private static void AssertOrdered(IReadOnlyList<string> events, params string[] expected)
        {
            int nextIndex = 0;
            foreach (string value in events)
            {
                if (nextIndex < expected.Length && value == expected[nextIndex]) nextIndex++;
            }
            Assert.Equal(expected.Length, nextIndex);
        }

        private static AchievementScreenDetection Idle() => AchievementScreenDetector.Classify(
            new Size(1920, 1080),
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(
                idlePlaceholderAccepted: true,
                rawText: "Search for Achievements..."),
            AchievementTextEvidence.ForTest("Achievements completed: 1", true),
            AchievementMatchCountParser.Parse(string.Empty),
            AchievementPageCounterParser.Parse(string.Empty));

        private static AchievementScreenDetection Active(int count) => AchievementScreenDetector.Classify(
            new Size(1920, 1080),
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(
                activeControlsAccepted: true,
                rawText: "Clear Search"),
            AchievementTextEvidence.ForTest("Achievements completed: 1", true),
            AchievementMatchCountParser.Parse($"Match(es): {count}"),
            AchievementPageCounterParser.Parse(count == 0 ? string.Empty : "1/1"));

        private static AchievementScreenDetection Unknown() => AchievementScreenDetector.Classify(
            new Size(1920, 1080),
            AchievementTextEvidence.ForTest("Achievements", true),
            AchievementSearchFieldEvidence.ForTest(),
            AchievementTextEvidence.ForTest(string.Empty, false),
            AchievementMatchCountParser.Parse(string.Empty),
            AchievementPageCounterParser.Parse(string.Empty));

        private static AchievementScreenDetection NotAchievement() => AchievementScreenDetector.Classify(
            new Size(1920, 1080),
            AchievementTextEvidence.ForTest(string.Empty, false),
            AchievementSearchFieldEvidence.ForTest(),
            AchievementTextEvidence.ForTest(string.Empty, false),
            AchievementMatchCountParser.Parse(string.Empty),
            AchievementPageCounterParser.Parse(string.Empty));

        private sealed class Harness
        {
            private Harness(
                List<string> events,
                RecordingGameInput input,
                RecordingTextInput textInput,
                QueueScreenDetector screenDetector,
                QueueFocusDetector focusDetector,
                RecordingForeground foreground,
                AchievementSearchNavigator navigator)
            {
                Events = events;
                Input = input;
                TextInput = textInput;
                ScreenDetector = screenDetector;
                FocusDetector = focusDetector;
                Foreground = foreground;
                Navigator = navigator;
            }

            internal List<string> Events { get; }
            internal RecordingGameInput Input { get; }
            internal RecordingTextInput TextInput { get; }
            internal QueueScreenDetector ScreenDetector { get; }
            internal QueueFocusDetector FocusDetector { get; }
            internal RecordingForeground Foreground { get; }
            internal AchievementSearchNavigator Navigator { get; }

            internal static Harness ForSearch(
                int matchCount,
                List<string> events = null,
                IGameScreenCapture capture = null,
                int openSettleMs = 0,
                int afterTypingSettleMs = 0,
                Action<int, CancellationToken> navigationWait = null) => Create(
                    new[] { Idle(), Unknown(), Unknown(), Active(matchCount) },
                    new[] { Focused(), Focused() },
                    events,
                    capture,
                    Timing(
                        openSettleMs: openSettleMs,
                        afterTypingSettleMs: afterTypingSettleMs),
                    navigationWait);

            internal static Harness ForStartState(AchievementScreenState state)
            {
                AchievementScreenDetection detection = state switch
                {
                    AchievementScreenState.Idle => Idle(),
                    AchievementScreenState.SearchActive => Active(1),
                    AchievementScreenState.Unknown => Unknown(),
                    _ => NotAchievement(),
                };
                return Create(new[] { detection }, Array.Empty<AchievementSearchEditorDetection>());
            }

            internal static Harness ForFocusFailure(
                int focusAttempts,
                Action<int, CancellationToken> navigationWait = null,
                int openSettleMs = 0,
                int retrySettleMs = 0) => Create(
                    BuildDetections(Idle(), Unknown(), focusAttempts),
                    BuildFocusDetections(focusAttempts, UnknownFocus()),
                    timing: Timing(
                        focusAttempts: focusAttempts,
                        openSettleMs: openSettleMs,
                        retrySettleMs: retrySettleMs),
                    navigationWait: navigationWait);

            internal static Harness ForUnknownResult(int resultAttempts) => Create(
                BuildSearchWithResults(resultAttempts, Unknown()),
                new[] { Focused(), Focused() },
                timing: Timing(resultAttempts: resultAttempts));

            internal static Harness ForPostTextFocus(
                params AchievementSearchEditorDetection[] postTextDetections) =>
                ForPostTextFocus(postTextDetections, retrySettleMs: 0, navigationWait: null);

            internal static Harness ForPostTextFocus(
                AchievementSearchEditorDetection[] postTextDetections,
                int retrySettleMs,
                Action<int, CancellationToken> navigationWait)
            {
                var screens = new List<AchievementScreenDetection>
                {
                    Idle(),
                    Unknown(),
                };
                for (int i = 0; i < postTextDetections.Length; i++) screens.Add(Unknown());
                if (postTextDetections.Any(detection =>
                    detection.State == AchievementSearchEditorState.Focused))
                {
                    screens.Add(Active(1));
                }

                var focus = new List<AchievementSearchEditorDetection> { Focused() };
                focus.AddRange(postTextDetections);
                return Create(
                    screens,
                    focus,
                    timing: Timing(
                        postTextFocusAttempts: postTextDetections.Length,
                        retrySettleMs: retrySettleMs),
                    navigationWait: navigationWait);
            }

            internal static Harness ForExit(params AchievementScreenDetection[] after)
            {
                var detections = new List<AchievementScreenDetection> { Active(1) };
                detections.AddRange(after);
                return Create(
                    detections,
                    Array.Empty<AchievementSearchEditorDetection>(),
                    timing: Timing(exitAttempts: after.Length));
            }

            private static Harness Create(
                IEnumerable<AchievementScreenDetection> detections,
                IEnumerable<AchievementSearchEditorDetection> focusDetections,
                List<string> events = null,
                IGameScreenCapture capture = null,
                AchievementSearchNavigationTiming timing = null,
                Action<int, CancellationToken> navigationWait = null)
            {
                events ??= new List<string>();
                var input = new RecordingGameInput(events);
                var textInput = new RecordingTextInput(events);
                var screenDetector = new QueueScreenDetector(events, detections);
                var focusDetector = new QueueFocusDetector(events, focusDetections);
                var foreground = new RecordingForeground(events) { IsForeground = true };
                capture ??= new RecordingCapture(events);
                var gameNavigator = new GameNavigator(input, milliseconds =>
                    events.Add("ButtonWait:" + milliseconds.ToString(CultureInfo.InvariantCulture)));
                var navigator = new AchievementSearchNavigator(
                    gameNavigator,
                    textInput,
                    screenDetector,
                    focusDetector,
                    capture,
                    foreground,
                    timing ?? Timing(),
                    navigationWait ?? ((_, token) => token.ThrowIfCancellationRequested()));
                return new Harness(
                    events,
                    input,
                    textInput,
                    screenDetector,
                    focusDetector,
                    foreground,
                    navigator);
            }

            private static AchievementSearchNavigationTiming Timing(
                int focusAttempts = 1,
                int postTextFocusAttempts = 1,
                int resultAttempts = 1,
                int exitAttempts = 1,
                int openSettleMs = 0,
                int afterTypingSettleMs = 0,
                int retrySettleMs = 0) => new(
                    searchHoldMs: 90,
                    backHoldMs: 90,
                    foregroundSettleMs: 0,
                    openSettleMs: openSettleMs,
                    afterTypingSettleMs: afterTypingSettleMs,
                    submitSettleMs: 0,
                    exitSettleMs: 0,
                    retrySettleMs: retrySettleMs,
                    focusAttempts: focusAttempts,
                    postTextFocusAttempts: postTextFocusAttempts,
                    resultAttempts: resultAttempts,
                    exitAttempts: exitAttempts);

            private static IEnumerable<AchievementScreenDetection> BuildDetections(
                AchievementScreenDetection first,
                AchievementScreenDetection repeated,
                int repeatCount)
            {
                yield return first;
                for (int i = 0; i < repeatCount; i++) yield return repeated;
            }

            private static IEnumerable<AchievementSearchEditorDetection> BuildFocusDetections(
                int count,
                AchievementSearchEditorDetection detection)
            {
                for (int i = 0; i < count; i++) yield return detection;
            }

            private static IEnumerable<AchievementScreenDetection> BuildSearchWithResults(
                int resultAttempts,
                AchievementScreenDetection result)
            {
                yield return Idle();
                yield return Unknown();
                yield return Unknown();
                for (int i = 0; i < resultAttempts; i++) yield return result;
            }
        }

        private static AchievementSearchEditorDetection Focused() => new(
            AchievementSearchEditorState.Focused,
            0.95,
            "test focused border");

        private static AchievementSearchEditorDetection UnknownFocus() => new(
            AchievementSearchEditorState.Unknown,
            0.25,
            "test inconclusive border");

        private static AchievementSearchEditorDetection NotFocused() => new(
            AchievementSearchEditorState.NotFocused,
            0.05,
            "test unfocused border");

        private sealed class RecordingGameInput : IGameInput
        {
            private readonly List<string> events;
            internal RecordingGameInput(List<string> events) => this.events = events;
            public bool IsAvailable => true;
            public string FailureReason => null;
            public void SetLeftStickHorizontal(float value) => events.Add("Horizontal:" + value);
            public void SetLeftStickVertical(float value) => events.Add("Vertical:" + value);
            public void SetButtonState(GameInputButton button, bool isPressed) =>
                events.Add($"Button:{button}:{isPressed}");
            public void MoveMouseBy(int x, int y) => events.Add($"Mouse:{x}:{y}");
            public void Dispose() => events.Add("Dispose");
        }

        private sealed class RecordingTextInput : IGameTextInput
        {
            private readonly List<string> events;
            internal RecordingTextInput(List<string> events) => this.events = events;
            internal string LastText { get; private set; }
            internal Action AfterText { get; set; }
            public void ReplaceAll(string text, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastText = text;
                events.Add("Text:" + text);
                AfterText?.Invoke();
            }
        }

        private sealed class QueueScreenDetector : IAchievementScreenDetector
        {
            private readonly List<string> events;
            private readonly Queue<AchievementScreenDetection> detections;
            internal QueueScreenDetector(
                List<string> events,
                IEnumerable<AchievementScreenDetection> detections)
            {
                this.events = events;
                this.detections = new Queue<AchievementScreenDetection>(detections);
            }
            public AchievementScreenDetection Detect(Bitmap screenshot)
            {
                if (detections.Count == 0)
                    throw new InvalidOperationException("Unexpected screen detection.");
                AchievementScreenDetection detection = detections.Dequeue();
                events.Add("Screen:" + detection.ScreenState);
                return detection;
            }
        }

        private sealed class QueueFocusDetector : IAchievementSearchEditorDetector
        {
            private readonly List<string> events;
            private readonly Queue<AchievementSearchEditorDetection> detections;
            internal QueueFocusDetector(
                List<string> events,
                IEnumerable<AchievementSearchEditorDetection> detections)
            {
                this.events = events;
                this.detections = new Queue<AchievementSearchEditorDetection>(detections);
            }
            internal int DetectCount { get; private set; }
            internal Action<int> AfterDetect { get; set; }
            public AchievementSearchEditorDetection Detect(
                Bitmap screenshot,
                AchievementScreenDetection screenDetection)
            {
                DetectCount++;
                if (detections.Count == 0)
                    throw new InvalidOperationException("Unexpected focus detection.");
                AchievementSearchEditorDetection detection = detections.Dequeue();
                events.Add("Focus:" + detection.State);
                AfterDetect?.Invoke(DetectCount);
                return detection;
            }
        }

        private class RecordingCapture : IGameScreenCapture
        {
            protected readonly List<string> Events;
            internal RecordingCapture(List<string> events) => Events = events;
            internal int CaptureCount { get; private set; }
            public virtual Bitmap CaptureWindow()
            {
                CaptureCount++;
                Events.Add("Capture");
                return new Bitmap(1, 1);
            }
        }

        private sealed class GuardedCapture : RecordingCapture
        {
            private readonly ICaptureOverlayGuard guard;
            internal GuardedCapture(List<string> events, ICaptureOverlayGuard guard)
                : base(events) => this.guard = guard;
            public override Bitmap CaptureWindow() => Navigation.CaptureWithoutOverlay(
                base.CaptureWindow,
                guard);
        }

        private sealed class RecordingForeground : IGameForegroundActivator
        {
            private readonly List<string> events;
            internal RecordingForeground(List<string> events) => this.events = events;
            public bool IsForeground { get; set; }
            internal bool ThrowOnActivate { get; set; }
            public void Activate()
            {
                events.Add("ActivateForeground");
                if (ThrowOnActivate) throw new InvalidOperationException("activation failed");
                IsForeground = true;
            }
        }

        private sealed class RecordingGuard : ICaptureOverlayGuard
        {
            private readonly List<string> events;
            internal RecordingGuard(List<string> events) => this.events = events;
            public IDisposable SuspendForCapture()
            {
                events.Add("OverlayHidden");
                return new Restore(events);
            }

            private sealed class Restore : IDisposable
            {
                private readonly List<string> events;
                internal Restore(List<string> events) => this.events = events;
                public void Dispose() => events.Add("OverlayRestored");
            }
        }
    }

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
