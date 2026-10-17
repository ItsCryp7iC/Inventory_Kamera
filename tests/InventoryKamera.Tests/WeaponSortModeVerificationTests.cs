using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Xunit;

namespace InventoryKamera.Tests
{
    public class WeaponSortModeRecognizerTests
    {
        [Theory]
        [InlineData("Quality", "Quality", "Exact")]
        [InlineData("Quality [", "Quality", "Exact")]
        [InlineData("Quality IL", "Quality", "SafePrefix")]
        [InlineData("Level |", "Level", "Exact")]
        [InlineData("Type I", "Type", "SafePrefix")]
        public void Recognize_KnownLabelsAndConservativeTrailingNoise(
            string raw,
            string expectedMode,
            string expectedReason)
        {
            WeaponSortRecognition result = WeaponSortModeRecognizer.Recognize(raw);

            Assert.Equal(expectedMode, result.RecognizedMode);
            Assert.Equal(expectedReason, result.MatchReason.ToString());
        }

        [Theory]
        [InlineData("qual")]
        [InlineData("q")]
        [InlineData("unrelated")]
        [InlineData("levelquality")]
        [InlineData("qualitylevel")]
        [InlineData("typefoo")]
        public void Recognize_AmbiguousShortOrUnrelatedTextIsRejected(string raw)
        {
            WeaponSortRecognition result = WeaponSortModeRecognizer.Recognize(raw);

            Assert.Null(result.RecognizedMode);
            Assert.Equal(WeaponSortMatchReason.None, result.MatchReason);
        }

        [Fact]
        public void Recognize_ExistingFuzzyPolicyStillAcceptsUnambiguousHighSimilarity()
        {
            WeaponSortRecognition result = WeaponSortModeRecognizer.Recognize("Qualiti");

            Assert.Equal("Quality", result.RecognizedMode);
            Assert.Equal(WeaponSortMatchReason.Fuzzy, result.MatchReason);
            Assert.True(result.Similarity > WeaponSortModeRecognizer.FuzzyThreshold);
        }
    }

    public class WeaponSortModeVerifierTests
    {
        [Fact]
        public void ConfirmSortMode_FirstCaptureBadSecondGoodUsesFreshCaptures()
        {
            var capture = new RecordingCapture();
            var detector = new QueueDetector(
                WeaponSortModeDetection.Unrecognized(),
                WeaponSortModeDetection.ForMode("Quality"));
            var verifier = CreateVerifier(capture, detector);

            bool confirmed = verifier.ConfirmSortMode(CreateNavigator(), "Quality", NoWaitTiming());

            Assert.True(confirmed);
            Assert.Equal(2, capture.Captures.Count);
            Assert.NotSame(capture.Captures[0], capture.Captures[1]);
            Assert.Equal(2, detector.SeenBitmaps.Count);
            Assert.NotSame(detector.SeenBitmaps[0], detector.SeenBitmaps[1]);
        }

        [Fact]
        public void ConfirmSortMode_ThirdFreshCaptureCanSucceed()
        {
            var capture = new RecordingCapture();
            var detector = new QueueDetector(
                WeaponSortModeDetection.Unrecognized(),
                WeaponSortModeDetection.Unrecognized(),
                WeaponSortModeDetection.ForMode("Quality"));
            var verifier = CreateVerifier(capture, detector);

            bool confirmed = verifier.ConfirmSortMode(CreateNavigator(), "Quality", NoWaitTiming());

            Assert.True(confirmed);
            Assert.Equal(3, capture.Captures.Count);
            Assert.Equal(3, capture.Captures.DistinctReferenceCount());
        }

        [Fact]
        public void ConfirmSortMode_AllAttemptsFailRemainsUnconfirmedAndSavesAllCrops()
        {
            var capture = new RecordingCapture();
            var detector = new QueueDetector(WeaponSortModeDetection.Unrecognized());
            var diagnostics = new RecordingDiagnostics();
            var verifier = CreateVerifier(capture, detector, diagnostics);

            bool confirmed = verifier.ConfirmSortMode(CreateNavigator(), "Quality", NoWaitTiming());

            Assert.False(confirmed);
            Assert.Equal(3, capture.Captures.Count);
            Assert.Equal("pre-change", diagnostics.Phase);
            Assert.Equal(3, diagnostics.CropCount);
        }

        [Fact]
        public void ConfirmSortMode_WrongModeSendsOneChangeAndPostChangeVerificationSucceeds()
        {
            var events = new List<string>();
            var capture = new RecordingCapture();
            var detector = new QueueDetector(
                WeaponSortModeDetection.ForMode("Level"),
                WeaponSortModeDetection.ForMode("Quality"));
            var verifier = CreateVerifier(capture, detector, wait: milliseconds => events.Add($"VerifyWait:{milliseconds}"));

            bool confirmed = verifier.ConfirmSortMode(
                CreateNavigator(events),
                "Quality",
                new WeaponSortModeTiming(80, 300, 100, 80, 300, 250));

            Assert.True(confirmed);
            Assert.Equal(2, capture.Captures.Count);
            Assert.Single(events.FindAll(entry => entry == "Button:DPadDown:True"));
            Assert.Single(events.FindAll(entry => entry == "Button:Confirm:True"));
            Assert.Contains("VerifyWait:300", events);
            Assert.Contains("VerifyWait:100", events);
            Assert.Single(events.FindAll(entry => entry == "Vertical:-1"));
        }

        [Fact]
        public void ConfirmSortMode_PostChangeVerificationFailsWithoutRepeatingSortInput()
        {
            var events = new List<string>();
            var capture = new RecordingCapture();
            var detector = new QueueDetector(
                WeaponSortModeDetection.ForMode("Level"),
                WeaponSortModeDetection.Unrecognized());
            var diagnostics = new RecordingDiagnostics();
            var verifier = CreateVerifier(capture, detector, diagnostics);

            bool confirmed = verifier.ConfirmSortMode(
                CreateNavigator(events),
                "Quality",
                NoWaitTiming());

            Assert.False(confirmed);
            Assert.Equal(4, capture.Captures.Count);
            Assert.Single(events.FindAll(entry => entry == "Button:DPadDown:True"));
            Assert.Single(events.FindAll(entry => entry == "Button:Confirm:True"));
            Assert.Equal("post-change", diagnostics.Phase);
            Assert.Equal(3, diagnostics.CropCount);
        }

        [Fact]
        public void ConfirmSortMode_AlreadyCorrectDoesNotSendSortInput()
        {
            var events = new List<string>();
            var verifier = CreateVerifier(
                new RecordingCapture(),
                new QueueDetector(WeaponSortModeDetection.ForMode("Quality")));

            bool confirmed = verifier.ConfirmSortMode(
                CreateNavigator(events),
                "Quality",
                NoWaitTiming());

            Assert.True(confirmed);
            Assert.DoesNotContain(events, entry => entry.StartsWith("Button:", StringComparison.Ordinal));
            Assert.DoesNotContain(events, entry => entry.StartsWith("Vertical:", StringComparison.Ordinal));
        }

        private static WeaponSortModeVerifier CreateVerifier(
            RecordingCapture capture,
            QueueDetector detector,
            RecordingDiagnostics diagnostics = null,
            Action<int> wait = null) =>
            new WeaponSortModeVerifier(
                capture,
                detector,
                diagnostics ?? new RecordingDiagnostics(),
                wait ?? (_ => { }));

        private static WeaponSortModeTiming NoWaitTiming() =>
            new WeaponSortModeTiming(0, 0, 0, 0, 0, 0);

        private static GameNavigator CreateNavigator(List<string> events = null)
        {
            events ??= new List<string>();
            return new GameNavigator(
                new FakeGameInput(events),
                milliseconds => events.Add($"InputWait:{milliseconds}"));
        }

        private sealed class RecordingCapture : IWeaponSortModeCapture
        {
            public List<Bitmap> Captures { get; } = new List<Bitmap>();

            public Bitmap CaptureSortLabel()
            {
                var bitmap = new Bitmap(10, 10);
                Captures.Add(bitmap);
                return bitmap;
            }
        }

        private sealed class QueueDetector : IWeaponSortModeDetector
        {
            private readonly Queue<WeaponSortModeDetection> detections;
            private WeaponSortModeDetection lastDetection;

            public QueueDetector(params WeaponSortModeDetection[] detections)
            {
                this.detections = new Queue<WeaponSortModeDetection>(detections);
            }

            public List<Bitmap> SeenBitmaps { get; } = new List<Bitmap>();

            public WeaponSortModeDetection Detect(Bitmap sortLabelRegion)
            {
                SeenBitmaps.Add(sortLabelRegion);
                if (detections.Count > 0) lastDetection = detections.Dequeue();
                return lastDetection ?? WeaponSortModeDetection.Unrecognized();
            }
        }

        private sealed class RecordingDiagnostics : IWeaponSortModeDiagnostics
        {
            public string Phase { get; private set; }
            public int CropCount { get; private set; }

            public void SaveFailure(string phase, IReadOnlyList<Bitmap> attemptCrops)
            {
                Phase = phase;
                CropCount = attemptCrops.Count;
            }
        }

        private sealed class FakeGameInput : IGameInput
        {
            private readonly List<string> events;

            public FakeGameInput(List<string> events) => this.events = events;

            public bool IsAvailable => true;
            public string FailureReason => null;
            public void SetLeftStickHorizontal(float value) =>
                events.Add("Horizontal:" + value.ToString("0.###", CultureInfo.InvariantCulture));
            public void SetLeftStickVertical(float value) =>
                events.Add("Vertical:" + value.ToString("0.###", CultureInfo.InvariantCulture));
            public void SetButtonState(GameInputButton button, bool isPressed) =>
                events.Add($"Button:{button}:{isPressed}");
            public void MoveMouseBy(int x, int y) => events.Add($"Mouse:{x}:{y}");
            public void Dispose() => events.Add("Dispose");
        }
    }

    public class WeaponFilterPolicyTests
    {
        [Fact]
        public void ConfirmedQualitySortPermitsRarityEarlyStop()
        {
            WeaponFilterDecision decision = WeaponFilterPolicy.Evaluate(
                sortModeConfirmed: true,
                sortByLevel: false,
                belowRarity: true,
                belowLevel: false);

            Assert.True(decision.ShouldStop);
            Assert.True(decision.ShouldDiscard);
        }

        [Fact]
        public void UnconfirmedSortDisablesOnlyEarlyStopAndStillDiscardsBelowThresholdItem()
        {
            WeaponFilterDecision decision = WeaponFilterPolicy.Evaluate(
                sortModeConfirmed: false,
                sortByLevel: false,
                belowRarity: true,
                belowLevel: false);

            Assert.False(decision.ShouldStop);
            Assert.True(decision.ShouldDiscard);
        }

        [Fact]
        public void UnconfirmedSortKeepsEligibleItemsForExistingCataloguePipeline()
        {
            WeaponFilterDecision decision = WeaponFilterPolicy.Evaluate(
                sortModeConfirmed: false,
                sortByLevel: false,
                belowRarity: false,
                belowLevel: false);

            Assert.False(decision.ShouldStop);
            Assert.False(decision.ShouldDiscard);
        }
    }

    internal static class BitmapReferenceAssertions
    {
        public static int DistinctReferenceCount(this IReadOnlyList<Bitmap> bitmaps)
        {
            var distinct = new HashSet<Bitmap>(ReferenceEqualityComparer.Instance);
            foreach (Bitmap bitmap in bitmaps) distinct.Add(bitmap);
            return distinct.Count;
        }
    }
}
