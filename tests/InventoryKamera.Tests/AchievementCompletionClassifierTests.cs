using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    public sealed class AchievementProgressParserTests
    {
        [Theory]
        [InlineData("0/1", 0, 1)]
        [InlineData("3/20", 3, 20)]
        [InlineData("19/20", 19, 20)]
        [InlineData("20/20", 20, 20)]
        [InlineData("3 / 20", 3, 20)]
        [InlineData("Progress\r\n3  /  20", 3, 20)]
        public void Parse_AcceptsOneBoundedFraction(string raw, int current, int target)
        {
            AchievementProgressParseResult result = AchievementProgressParser.Parse(raw);

            Assert.True(result.Accepted, result.Reason);
            Assert.Equal(current, result.Current);
            Assert.Equal(target, result.Target);
            Assert.False(result.MultipleFractions);
        }

        [Theory]
        [InlineData("")]
        [InlineData("3")]
        [InlineData("/20")]
        [InlineData("3/")]
        [InlineData("20/3")]
        [InlineData("-1/20")]
        [InlineData("3/0")]
        [InlineData("10000/20000")]
        [InlineData("2026/09/14")]
        public void Parse_RejectsMalformedOrUnsupportedText(string raw)
        {
            AchievementProgressParseResult result = AchievementProgressParser.Parse(raw);

            Assert.False(result.Accepted);
            Assert.Null(result.Current);
            Assert.Null(result.Target);
        }

        [Fact]
        public void Parse_RejectsMultipleFractions()
        {
            AchievementProgressParseResult result = AchievementProgressParser.Parse("3/20 4/20");

            Assert.False(result.Accepted);
            Assert.True(result.MultipleFractions);
        }
    }

    public sealed class AchievementCompletionTextTests
    {
        [Theory]
        [InlineData("Claim", true, false)]
        [InlineData("Claim!", true, false)]
        [InlineData("Completed", false, true)]
        [InlineData("reclaim", false, false)]
        [InlineData("unclaimed", false, false)]
        [InlineData("completedness", false, false)]
        public void SemanticTokens_AreWholeWordsOnly(
            string raw,
            bool claimExpected,
            bool completedExpected)
        {
            var attempt = Attempt(raw);

            Assert.Equal(claimExpected, attempt.ClaimTokenDetected);
            Assert.Equal(completedExpected, attempt.CompletedTokenDetected);
        }

        [Theory]
        [InlineData("2026/09/14", "2026/09/14")]
        [InlineData("Completed 2025 / 5 / 20", "2025/5/20")]
        [InlineData("2025/02/30", "")]
        [InlineData("3/20", "")]
        public void DateEvidence_IsDateOnlyAndStrict(string raw, string expected)
        {
            Assert.Equal(expected, AchievementCompletionText.FindDate(raw));
        }

        private static AchievementCompletionOcrAttempt Attempt(string raw) => new(
            AchievementCompletionOcrVariant.GrayscaleContrast,
            raw,
            0.90f,
            "test read");
    }

    public sealed class AchievementCompletionClassifierTests
    {
        [Fact]
        public void ClaimOnly_IsCompletedUnclaimed()
        {
            Harness harness = Harness.Create("Claim", 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.CompletedUnclaimed, result.State);
            Assert.True(result.ClaimDetected);
            Assert.False(result.CompletedDetected);
        }

        [Fact]
        public void CompletedOnly_IsCompletedClaimed()
        {
            Harness harness = Harness.Create("Completed", 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.CompletedClaimed, result.State);
            Assert.False(result.ClaimDetected);
            Assert.True(result.CompletedDetected);
        }

        [Theory]
        [InlineData("3/20", 3, 20)]
        [InlineData("0/1", 0, 1)]
        public void ExplicitIncompleteProgress_IsIncomplete(string raw, int current, int target)
        {
            Harness harness = Harness.Create(raw, 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Incomplete, result.State);
            Assert.Equal(current, result.ProgressCurrent);
            Assert.Equal(target, result.ProgressTarget);
        }

        [Fact]
        public void EqualProgress_DoesNotInferCompletion()
        {
            Harness harness = Harness.Create("20/20", 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("does not independently prove completion", result.Reason);
        }

        [Fact]
        public void NoStateEvidence_DoesNotInferIncomplete()
        {
            Harness harness = Harness.Create("Reward 5", 0.95f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.False(result.ClaimDetected);
            Assert.False(result.CompletedDetected);
            Assert.Null(result.ProgressCurrent);
        }

        [Fact]
        public void EmptyOcr_IsUnresolvedWithoutAbsenceInference()
        {
            Harness harness = Harness.Create(string.Empty, 0.0f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("no usable text", result.Reason, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.ClaimDetected);
            Assert.False(result.CompletedDetected);
            Assert.Null(result.ProgressCurrent);
        }

        [Theory]
        [InlineData("Claim Completed")]
        [InlineData("Claim 3/20")]
        [InlineData("Completed 3/20")]
        public void ConflictingStateEvidence_FailsClosed(string raw)
        {
            Harness harness = Harness.Create(raw, 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("conflict", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MultipleProgressFractions_FailClosed()
        {
            Harness harness = Harness.Create("3/20 4/20", 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("Multiple", result.Reason);
        }

        [Fact]
        public void DifferentFractionsAcrossAttempts_FailClosed()
        {
            Harness harness = Harness.Create(
                Attempt("3/20", 0.90f),
                Attempt("4/20", 0.91f, AchievementCompletionOcrVariant.GrayscaleContrastInverted));

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("Multiple", result.Reason);
        }

        [Fact]
        public void MalformedProgress_DoesNotOverrideIndependentClaimEvidence()
        {
            Harness harness = Harness.Create("Claim 3/", 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.CompletedUnclaimed, result.State);
        }

        [Fact]
        public void DateOnly_IsUnresolved()
        {
            Harness harness = Harness.Create("2026/09/14", 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Equal("2026/09/14", result.DateRaw);
        }

        [Theory]
        [InlineData("Claim")]
        [InlineData("Completed")]
        public void LowConfidenceCompletionToken_IsUnresolved(string raw)
        {
            Harness harness = Harness.Create(raw, 0.20f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("confidence", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("Claim 2026/09/14", (int)AchievementCompletionState.CompletedUnclaimed)]
        [InlineData("Completed 2025/05/20", (int)AchievementCompletionState.CompletedClaimed)]
        public void CompletionToken_WithSupportingDate_UsesTokenOnly(
            string raw,
            int expectedState)
        {
            Harness harness = Harness.Create(raw, 0.90f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal((AchievementCompletionState)expectedState, result.State);
            Assert.NotEmpty(result.DateRaw);
        }

        [Fact]
        public void LowConfidenceClaim_CompetingWithStrongProgress_FailsClosed()
        {
            Harness harness = Harness.Create(
                Attempt("Claim", 0.20f),
                Attempt("3/20", 0.90f, AchievementCompletionOcrVariant.GrayscaleContrastInverted));

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("conflict", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void StageGroupIdentity_ClassifiesVisibleRowWithoutSelectingStage()
        {
            AchievementIdentityResult identity = Identity(AchievementIdentityState.StageGroupIdentified);
            Harness harness = Harness.Create("Claim", 0.90f);

            AchievementCompletionResult result = harness.Classify(identity);

            Assert.Equal(AchievementCompletionState.CompletedUnclaimed, result.State);
            Assert.Same(identity, result.Identity);
            Assert.Null(identity.IdentifiedId);
            Assert.Equal(80127, identity.StageGroupId);
            Assert.Equal(new[] { 80127, 80128, 80129 }, identity.CandidateIds);
        }

        [Theory]
        [InlineData((int)AchievementIdentityState.Ambiguous)]
        [InlineData((int)AchievementIdentityState.Mismatch)]
        [InlineData((int)AchievementIdentityState.Unresolved)]
        public void NonPositiveIdentity_StopsBeforeScreenOrCompletionOcr(int stateValue)
        {
            Harness harness = Harness.Create("Claim", 0.90f);

            AchievementCompletionResult result = harness.Classify(
                Identity((AchievementIdentityState)stateValue));

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Equal(0, harness.ScreenDetector.DetectCount);
            Assert.Equal(0, harness.Reader.ReadCount);
        }

        [Fact]
        public void NullIdentity_StopsBeforeScreenOrCompletionOcr()
        {
            Harness harness = Harness.Create("Claim", 0.90f);

            AchievementCompletionResult result = harness.Classify(null);

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Equal(0, harness.ScreenDetector.DetectCount);
            Assert.Equal(0, harness.Reader.ReadCount);
        }

        [Theory]
        [InlineData((int)AchievementScreenState.Idle, 0)]
        [InlineData((int)AchievementScreenState.Unknown, 0)]
        [InlineData((int)AchievementScreenState.NotAchievementScreen, 0)]
        [InlineData((int)AchievementScreenState.SearchActive, 5)]
        public void InvalidCurrentScreen_StopsBeforeCompletionOcr(int stateValue, int matchCount)
        {
            AchievementScreenState state = (AchievementScreenState)stateValue;
            Harness harness = Harness.Create(
                Detection(state, matchCount),
                Attempt("Claim", 0.90f));

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Equal(1, harness.ScreenDetector.DetectCount);
            Assert.Equal(0, harness.Reader.ReadCount);
        }

        [Fact]
        public void NullScreenshot_StopsBeforeScreenOrCompletionOcr()
        {
            Harness harness = Harness.Create("Claim", 0.90f);

            AchievementCompletionResult result = harness.Classifier.Classify(Identity(), null);

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Equal(0, harness.ScreenDetector.DetectCount);
            Assert.Equal(0, harness.Reader.ReadCount);
        }

        [Fact]
        public void CompletionCropFailure_IsReturnedAsUnresolved()
        {
            var detector = new RecordingScreenDetector(
                Detection(AchievementScreenState.SearchActive, 1));
            var classifier = new AchievementCompletionClassifier(
                detector,
                new ThrowingCompletionReader());
            using var screenshot = new Bitmap(1, 1);

            AchievementCompletionResult result = classifier.Classify(Identity(), screenshot);

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.Contains("OCR failed", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ScreenLevelCompletedCount_CannotClassifyTheVisibleRow()
        {
            AchievementScreenDetection detection = Detection(
                AchievementScreenState.SearchActive,
                1,
                completedCountRaw: "Achievements completed: Claim Completed 3/20");
            Harness harness = Harness.Create(detection, Attempt("Reward 5", 0.95f));

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Equal(AchievementCompletionState.Unresolved, result.State);
            Assert.False(result.ClaimDetected);
            Assert.False(result.CompletedDetected);
            Assert.Null(result.ProgressCurrent);
        }

        [Fact]
        public void Diagnostics_AreBoundedAndContainRequiredFields()
        {
            Harness harness = Harness.Create(new string('X', 1000), 0.95f);

            AchievementCompletionResult result = harness.Classify(Identity());

            Assert.Contains("operation=completion-classify", result.Diagnostics);
            Assert.Contains("identityState=Identified", result.Diagnostics);
            Assert.Contains("expectedId=81665", result.Diagnostics);
            Assert.Contains("stageGroupId=(none)", result.Diagnostics);
            Assert.Contains("screenState=SearchActive", result.Diagnostics);
            Assert.Contains("matchCount=1", result.Diagnostics);
            Assert.Contains("claimDetected=False", result.Diagnostics);
            Assert.Contains("completedDetected=False", result.Diagnostics);
            Assert.Contains("progressRaw=", result.Diagnostics);
            Assert.Contains("progressCurrent=(none)", result.Diagnostics);
            Assert.Contains("progressTarget=(none)", result.Diagnostics);
            Assert.Contains("dateRaw=", result.Diagnostics);
            Assert.Contains("selectedState=Unresolved", result.Diagnostics);
            Assert.Contains("attemptCount=1", result.Diagnostics);
            Assert.Contains("reason=", result.Diagnostics);
            Assert.DoesNotContain(new string('X', 200), result.Diagnostics);
            Assert.DoesNotContain("completedAt", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.True(result.Diagnostics.Length < 4096, result.Diagnostics.Length.ToString());
        }

        private static AchievementCompletionOcrAttempt Attempt(
            string raw,
            float confidence,
            AchievementCompletionOcrVariant variant = AchievementCompletionOcrVariant.GrayscaleContrast) =>
            new(variant, raw, confidence, "test read");

        internal static AchievementIdentityResult Identity(
            AchievementIdentityState state = AchievementIdentityState.Identified)
        {
            bool staged = state == AchievementIdentityState.StageGroupIdentified;
            int expectedId = staged ? 80127 : 81665;
            return new AchievementIdentityResult(
                state,
                expectedId,
                staged ? "Zoo Tycoon" : "You There! Fly!",
                state == AchievementIdentityState.Identified ? expectedId : null,
                staged ? 80127 : null,
                staged ? new[] { 80127, 80128, 80129 } : new[] { expectedId },
                staged ? "Zoo Tycoon" : "You There! Fly!",
                staged ? "zoo tycoon" : "you there! fly!",
                100,
                0,
                100,
                0.95f,
                Array.Empty<AchievementIdentityOcrAttempt>(),
                Detection(AchievementScreenState.SearchActive, 1),
                "test identity",
                "test diagnostics");
        }

        internal static AchievementScreenDetection Detection(
            AchievementScreenState state,
            int matchCount,
            string completedCountRaw = "Achievements completed: 1")
        {
            string matchRaw = state == AchievementScreenState.SearchActive
                ? $"Match(es): {matchCount}"
                : string.Empty;
            AchievementMatchCountParseResult match = AchievementMatchCountParser.Parse(matchRaw);
            AchievementPageCounterParseResult page = AchievementPageCounterParser.Parse(
                state == AchievementScreenState.SearchActive ? "1/1" : string.Empty);
            return new AchievementScreenDetection(
                state,
                AchievementTextEvidence.ForTest("Achievements", state != AchievementScreenState.NotAchievementScreen),
                AchievementSearchFieldEvidence.ForTest(activeControlsAccepted: state == AchievementScreenState.SearchActive),
                AchievementTextEvidence.ForTest(completedCountRaw, state != AchievementScreenState.NotAchievementScreen),
                AchievementTextEvidence.ForTest(matchRaw, match.Accepted),
                match,
                AchievementTextEvidence.ForTest(page.RawText, page.Accepted),
                page,
                AchievementScreenRegions.For(new Size(1920, 1080)),
                "test detection");
        }

        private sealed class Harness
        {
            private Harness(
                RecordingScreenDetector screenDetector,
                RecordingCompletionReader reader)
            {
                ScreenDetector = screenDetector;
                Reader = reader;
                Classifier = new AchievementCompletionClassifier(screenDetector, reader);
            }

            internal RecordingScreenDetector ScreenDetector { get; }
            internal RecordingCompletionReader Reader { get; }
            internal AchievementCompletionClassifier Classifier { get; }

            internal static Harness Create(string raw, float confidence) =>
                Create(Attempt(raw, confidence));

            internal static Harness Create(params AchievementCompletionOcrAttempt[] attempts) =>
                Create(Detection(AchievementScreenState.SearchActive, 1), attempts);

            internal static Harness Create(
                AchievementScreenDetection detection,
                params AchievementCompletionOcrAttempt[] attempts) =>
                new(new RecordingScreenDetector(detection), new RecordingCompletionReader(attempts));

            internal AchievementCompletionResult Classify(AchievementIdentityResult identity)
            {
                using var screenshot = new Bitmap(1, 1);
                return Classifier.Classify(identity, screenshot);
            }
        }

        private sealed class RecordingScreenDetector : IAchievementScreenDetector
        {
            private readonly AchievementScreenDetection detection;
            internal RecordingScreenDetector(AchievementScreenDetection detection) =>
                this.detection = detection;
            internal int DetectCount { get; private set; }
            public AchievementScreenDetection Detect(Bitmap screenshot)
            {
                DetectCount++;
                return detection;
            }
        }

        private sealed class RecordingCompletionReader : IAchievementCompletionReader
        {
            private readonly AchievementCompletionRead read;
            internal RecordingCompletionReader(IEnumerable<AchievementCompletionOcrAttempt> attempts) =>
                read = new AchievementCompletionRead(attempts);
            internal int ReadCount { get; private set; }
            public AchievementCompletionRead Read(Bitmap screenshot)
            {
                ReadCount++;
                return read;
            }
        }

        private sealed class ThrowingCompletionReader : IAchievementCompletionReader
        {
            public AchievementCompletionRead Read(Bitmap screenshot) =>
                throw new ArgumentException("Completion crop is outside the capture.");
        }
    }

    public sealed class AchievementCompletionFixtureTests
    {
        private static readonly Size FixtureSize = new(1919, 1079);
        private readonly ITestOutputHelper output;

        public AchievementCompletionFixtureTests(ITestOutputHelper output) => this.output = output;

        [Theory]
        [InlineData(
            "achievement_completion_claim_keyboard_01.png",
            (int)AchievementCompletionState.CompletedUnclaimed,
            null,
            null)]
        [InlineData(
            "achievement_completion_completed_controller_01.png",
            (int)AchievementCompletionState.CompletedClaimed,
            null,
            null)]
        [InlineData(
            "achievement_completion_progress_0_1_01.png",
            (int)AchievementCompletionState.Incomplete,
            0,
            1)]
        public void ExistingSanitizedCompletionFixture_UsesRealOcrPipeline(
            string fixtureName,
            int expectedStateValue,
            int? expectedCurrent,
            int? expectedTarget)
        {
            using Bitmap screenshot = BuildComposite(fixtureName);
            using var ocr = new OcrService(engineCount: 1);
            ocr.Restart();
            var reader = new AchievementCompletionReader(ocr, new ImageProcessor());
            var detector = new FixedScreenDetector(
                AchievementCompletionClassifierTests.Detection(AchievementScreenState.SearchActive, 1));
            var classifier = new AchievementCompletionClassifier(detector, reader);

            AchievementCompletionResult result = classifier.Classify(
                AchievementCompletionClassifierTests.Identity(),
                screenshot);

            output.WriteLine(result.Diagnostics);
            Assert.Equal((AchievementCompletionState)expectedStateValue, result.State);
            Assert.Equal(6, result.Attempts.Count);
            Assert.Equal(expectedCurrent, result.ProgressCurrent);
            Assert.Equal(expectedTarget, result.ProgressTarget);
            Assert.Contains(result.Attempts, attempt =>
                attempt.Confidence >= AchievementCompletionClassifier.MinimumEvidenceConfidence &&
                (expectedStateValue == (int)AchievementCompletionState.CompletedUnclaimed
                    ? attempt.ClaimTokenDetected
                    : expectedStateValue == (int)AchievementCompletionState.CompletedClaimed
                        ? attempt.CompletedTokenDetected
                        : attempt.Progress.Accepted &&
                          attempt.Progress.Current == expectedCurrent &&
                          attempt.Progress.Target == expectedTarget));

            if (expectedStateValue == (int)AchievementCompletionState.Incomplete)
            {
                AchievementCompletionOcrAttempt accepted = result.Attempts
                    .Where(attempt =>
                        attempt.Confidence >= AchievementCompletionClassifier.MinimumEvidenceConfidence &&
                        attempt.Progress.Accepted &&
                        attempt.Progress.Current == expectedCurrent &&
                        attempt.Progress.Target == expectedTarget)
                    .OrderByDescending(attempt => attempt.Confidence)
                    .First();
                output.WriteLine(
                    $"acceptedVariant={accepted.Variant}; raw={accepted.RawText}; " +
                    $"normalized={accepted.NormalizedText}; confidence={accepted.Confidence:0.000}; " +
                    $"current={accepted.Progress.Current}; target={accepted.Progress.Target}; " +
                    $"state={result.State}");
                Assert.False(result.ClaimDetected);
                Assert.False(result.CompletedDetected);
            }
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

        private sealed class FixedScreenDetector : IAchievementScreenDetector
        {
            private readonly AchievementScreenDetection detection;
            internal FixedScreenDetector(AchievementScreenDetection detection) =>
                this.detection = detection;
            public AchievementScreenDetection Detect(Bitmap screenshot) => detection;
        }
    }
}
