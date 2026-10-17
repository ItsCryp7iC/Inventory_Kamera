using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using Tesseract;
using Xunit;

namespace InventoryKamera.Tests
{
    public class ConstellationScanReliabilityTests
    {
        [Theory]
        [InlineData("Activated")]
        [InlineData("ACTIVATED")]
        [InlineData("Activated!")]
        [InlineData("B  Activated")]
        public void ClearActivatedLabelResolvesActivated(string text)
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("legacy", text, 0.72f) },
                "");

            Assert.Equal(ConstellationNodeState.Activated, observation.State);
        }

        [Fact]
        public void AlternateActivatedVariantCanRecoverWhenPrimaryIsCorrupted()
        {
            ConstellationNodeObservation observation = Observe(
                new[]
                {
                    ("legacy", "Acligpted", 0.0f),
                    ("gold_text_mask_2x", "Activated", 0.21f),
                    ("threshold", "", 0.0f),
                },
                "");

            Assert.True(observation.ActivatedEvidence);
            Assert.Equal(ConstellationNodeState.Activated, observation.State);
        }

        [Theory]
        [InlineData("Activate required")]
        [InlineData("ACTIVATE REQUIRED")]
        [InlineData("Activate\nrequired")]
        [InlineData("Activate: required!")]
        [InlineData("Description text\nActivate required\n0 / 1")]
        public void ExactActivateRequiredPhraseResolvesLocked(string text)
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("legacy", "", 0.0f) },
                text);

            Assert.True(observation.LockedEvidence);
            Assert.Equal(ConstellationNodeState.Locked, observation.State);
        }

        [Fact]
        public void PositiveLockIconAloneResolvesLocked()
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("legacy", "", 0.0f) },
                "",
                lockIconEvidence: true);

            Assert.True(observation.LockIconEvidence);
            Assert.False(observation.LockedTextEvidence);
            Assert.True(observation.LockedEvidence);
            Assert.Equal(ConstellationNodeState.Locked, observation.State);
        }

        [Fact]
        public void MissingLockIconDoesNotBecomeActivatedEvidence()
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("legacy", "", 0.0f) },
                "",
                lockIconEvidence: false);

            Assert.False(observation.ActivatedEvidence);
            Assert.False(observation.LockIconEvidence);
            Assert.Equal(ConstellationNodeState.Unresolved, observation.State);
        }

        [Theory]
        [InlineData("Activate")]
        [InlineData("B Activate")]
        [InlineData("Must activate Frostbiting Embrace first")]
        [InlineData("Activation required")]
        public void NonSpecificActivateTextDoesNotResolveLocked(string text)
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("legacy", "", 0.0f) },
                text);

            Assert.False(observation.LockedEvidence);
            Assert.Equal(ConstellationNodeState.Unresolved, observation.State);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyOcrIsUnresolved(string text)
        {
            Assert.Equal(
                ConstellationNodeState.Unresolved,
                Observe(new[] { ("legacy", text, 0.0f) }, "").State);
        }

        [Theory]
        [InlineData("Constellation")]
        [InlineData("Unlock requirement")]
        [InlineData("Adventure Rank 25")]
        public void UnrelatedTextIsUnresolved(string text)
        {
            Assert.Equal(
                ConstellationNodeState.Unresolved,
                Observe(new[] { ("legacy", text, 0.99f) }, text).State);
        }

        [Theory]
        [InlineData("Activatcd")]
        [InlineData("Activat")]
        [InlineData("Acti vated")]
        [InlineData("Activate d")]
        [InlineData("Activatedness")]
        public void MalformedActivatedLikeTextIsUnresolved(string text)
        {
            Assert.Equal(
                ConstellationNodeState.Unresolved,
                Observe(new[] { ("legacy", text, 0.99f) }, "").State);
        }

        [Fact]
        public void ExactSemanticsNotAggregateConfidenceDetermineState()
        {
            ConstellationNodeObservation lowConfidenceExact =
                Observe(new[] { ("legacy", "Activated", 0.01f) }, "");
            ConstellationNodeObservation highConfidenceUnrelated =
                Observe(new[] { ("legacy", "Constellation", 0.99f) }, "");

            Assert.Equal(ConstellationNodeState.Activated, lowConfidenceExact.State);
            Assert.Equal(0.01f, lowConfidenceExact.ActivatedReads[0].Confidence);
            Assert.Equal(ConstellationNodeState.Unresolved, highConfidenceUnrelated.State);
            Assert.Equal(0.99f, highConfidenceUnrelated.ActivatedReads[0].Confidence);
        }

        [Fact]
        public void ConflictingPositiveSignalsRemainUnresolved()
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("alternate", "Activated", 0.90f) },
                "Activate required");

            Assert.True(observation.ActivatedEvidence);
            Assert.True(observation.LockedEvidence);
            Assert.Equal(ConstellationNodeState.Unresolved, observation.State);
            Assert.Contains("conflicting", observation.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ActivatedTextAndLockIconConflictRemainsUnresolved()
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("alternate", "Activated", 0.90f) },
                "",
                lockIconEvidence: true);

            Assert.True(observation.ActivatedEvidence);
            Assert.True(observation.LockIconEvidence);
            Assert.Equal(ConstellationNodeState.Unresolved, observation.State);
        }

        [Fact]
        public void LockedPhraseInActivatedRegionDoesNotCountAsActivated()
        {
            ConstellationNodeObservation observation = Observe(
                new[] { ("gold_text_mask_2x", "Activate required", 0.99f) },
                "");

            Assert.False(observation.ActivatedEvidence);
            Assert.Equal(ConstellationNodeState.Unresolved, observation.State);
        }

        [Fact]
        public void GoldTextMaskIsolatesLabelColorFromWhiteCyanAndBlueAnimation()
        {
            using var source = new Bitmap(4, 1);
            source.SetPixel(0, 0, Color.FromArgb(255, 204, 50));
            source.SetPixel(1, 0, Color.White);
            source.SetPixel(2, 0, Color.FromArgb(80, 230, 255));
            source.SetPixel(3, 0, Color.FromArgb(20, 80, 180));

            using Bitmap mask = ConstellationActivatedTextPreprocessor.CreateGoldTextMask(source);

            Assert.Equal(8, mask.Width);
            Assert.Equal(2, mask.Height);
            Assert.Equal(Color.Black.ToArgb(), mask.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.Black.ToArgb(), mask.GetPixel(1, 1).ToArgb());
            Assert.Equal(Color.White.ToArgb(), mask.GetPixel(2, 0).ToArgb());
            Assert.Equal(Color.White.ToArgb(), mask.GetPixel(4, 0).ToArgb());
            Assert.Equal(Color.White.ToArgb(), mask.GetPixel(6, 0).ToArgb());
        }

        [Fact]
        public void GoldTextMaskKeepsActivatedReadableAcrossSyntheticCyanAnimation()
        {
            using var source = new Bitmap(128, 49);
            using (Graphics graphics = Graphics.FromImage(source))
            using (var cyanPen = new Pen(Color.FromArgb(180, 245, 255), 5f))
            using (var whiteBrush = new SolidBrush(Color.White))
            using (var goldBrush = new SolidBrush(Color.FromArgb(255, 204, 50)))
            using (var font = new Font(FontFamily.GenericSansSerif, 15f, FontStyle.Regular))
            {
                graphics.Clear(Color.FromArgb(25, 105, 155));
                graphics.DrawLine(cyanPen, 0, 42, 118, 0);
                graphics.FillEllipse(whiteBrush, 42, 22, 28, 28);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("Activated", font, goldBrush, 9, 10);
            }

            using Bitmap mask = ConstellationActivatedTextPreprocessor.CreateGoldTextMask(source);
            using var ocr = new OcrService(engineCount: 1);
            string text = ocr.AnalyzeText(mask, PageSegMode.SingleBlock);

            Assert.Equal("activated", ConstellationNodeStateResolver.Normalize(text));
        }

        [Theory]
        [InlineData(255, 204, 50, true)]
        [InlineData(230, 180, 70, true)]
        [InlineData(255, 255, 255, false)]
        [InlineData(140, 240, 255, false)]
        [InlineData(30, 100, 220, false)]
        [InlineData(205, 205, 170, false)]
        public void GoldClassifierSeparatesLabelFromAnimation(
            int red,
            int green,
            int blue,
            bool expected)
        {
            Assert.Equal(
                expected,
                ConstellationActivatedTextPreprocessor.IsLikelyActivatedGold(
                    Color.FromArgb(red, green, blue)));
        }

        [Fact]
        public void CaptureRegionsMatchLive1920By1080Measurements()
        {
            Assert.Equal(
                new Rectangle(320, 70, 96, 96),
                ConstellationCaptureRegions.LockStatusIcon(new Size(1920, 1080)));
            Assert.Equal(
                new Rectangle(213, 46, 64, 64),
                ConstellationCaptureRegions.LockStatusIcon(new Size(1280, 720)));
            Assert.Equal(
                new Rectangle(426, 93, 128, 128),
                ConstellationCaptureRegions.LockStatusIcon(new Size(2560, 1440)));
            Assert.Equal(
                new Rectangle(302, 898, 128, 49),
                ConstellationCaptureRegions.Activated(new Size(1920, 1080)));
            Assert.Equal(
                new Rectangle(312, 906, 110, 30),
                ConstellationCaptureRegions.ActivatedText(new Size(1920, 1080)));
            Assert.Equal(
                new Rectangle(86, 367, 547, 216),
                ConstellationCaptureRegions.LockedDescription(new Size(1920, 1080)));
        }

        [Theory]
        [MemberData(nameof(ActivatedStatusFixtures))]
        public void ActivatedStatusFixturesStayBelowLockThreshold(string fixtureName)
        {
            ConstellationLockIconDetection detection = DetectFixture(fixtureName);

            Assert.False(detection.Accepted);
            Assert.True(
                detection.Score <= 0.525,
                $"{fixtureName} lock score {detection.Score:0.000} lost the benchmark margin.");
        }

        [Theory]
        [MemberData(nameof(LockedStatusFixtures))]
        public void LockedStatusFixturesExceedLockThresholdWithMargin(string fixtureName)
        {
            ConstellationLockIconDetection detection = DetectFixture(fixtureName);

            Assert.True(detection.Accepted);
            Assert.True(
                detection.Score >= 0.990,
                $"{fixtureName} lock score {detection.Score:0.000} lost the benchmark margin.");
        }

        [Theory]
        [MemberData(nameof(AllStatusFixtures))]
        public void StatusFixturesKeepClassificationWhenScaledTo720p(
            string fixtureName,
            bool expectedLocked)
        {
            ConstellationLockIconDetection detection = DetectFixture(
                fixtureName,
                new Size(64, 64));

            Assert.Equal(expectedLocked, detection.Accepted);
        }

        [Theory]
        [InlineData("constellation_status_locked_01.png", 1)]
        [InlineData("constellation_status_locked_02.png", 1)]
        [InlineData("constellation_status_locked_03.png", 2)]
        [InlineData("constellation_status_locked_04.png", 1)]
        [InlineData("constellation_status_locked_05.png", 1)]
        [InlineData("constellation_status_locked_06.png", 1)]
        [InlineData("constellation_status_locked_07.png", 1)]
        [InlineData("constellation_status_locked_08.png", 1)]
        [InlineData("constellation_status_locked_09.png", 1)]
        [InlineData("constellation_status_locked_10.png", 1)]
        [InlineData("constellation_status_locked_11.png", 6)]
        [InlineData("constellation_status_locked_12.png", 6)]
        [InlineData("constellation_status_locked_13.png", 4)]
        [InlineData("constellation_status_locked_14.png", 6)]
        public void KnownFailureNodesResolveLockedFromPositiveIconEvidence(
            string fixtureName,
            int node)
        {
            ConstellationLockIconDetection detection = DetectFixture(fixtureName);
            ConstellationNodeObservation observation = ConstellationNodeStateResolver.Resolve(
                new[] { new ConstellationOcrRead("fixture", string.Empty) },
                new ConstellationOcrRead("locked_description", string.Empty),
                detection);

            Assert.InRange(node, 1, 6);
            Assert.Equal(ConstellationNodeState.Locked, observation.State);
        }

        [Fact]
        public void EmbeddedProductionTemplateClassifiesLockedFixture()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Constellation",
                "constellation_status_locked_03.png");
            using var fixture = new Bitmap(path);
            ConstellationLockIconDetector detector = ConstellationLockIconDetector.CreateDefault();
            ConstellationLockIconDetection detection = detector.Detect(
                fixture,
                new Rectangle(320, 70, 96, 96),
                out Bitmap processed);
            processed.Dispose();

            Assert.True(detection.Accepted);
            Assert.True(detection.Score >= 0.990);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void ForwardSequenceResolvesEveryConstellation(int expected)
        {
            ConstellationScanResult result =
                ConstellationSequenceEvaluator.EvaluateForward(ForwardStates(expected));

            Assert.True(result.Success);
            Assert.Equal(expected, result.Constellation);
        }

        [Fact]
        public void ForwardUnresolvedFirstNodeFailsInsteadOfGuessingC0()
        {
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                new[] { ConstellationNodeState.Unresolved });

            Assert.False(result.Success);
            Assert.Equal(-1, result.Constellation);
            Assert.Equal(1, result.UnresolvedNode);
        }

        [Fact]
        public void ForwardUnresolvedMiddleNodeFails()
        {
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                new[]
                {
                    ConstellationNodeState.Activated,
                    ConstellationNodeState.Activated,
                    ConstellationNodeState.Unresolved,
                });

            Assert.False(result.Success);
            Assert.Equal(3, result.UnresolvedNode);
        }

        [Fact]
        public void ForwardUnresolvedAfterActivatedNodesDoesNotReturnLowerCount()
        {
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                new[]
                {
                    ConstellationNodeState.Activated,
                    ConstellationNodeState.Activated,
                    ConstellationNodeState.Activated,
                    ConstellationNodeState.Unresolved,
                });

            Assert.False(result.Success);
            Assert.NotEqual(3, result.Constellation);
        }

        [Fact]
        public void IncompleteForwardSequenceCannotBeGuessed()
        {
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                new[] { ConstellationNodeState.Activated });

            Assert.False(result.Success);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void GreedySequenceResolvesEveryConstellation(int expected)
        {
            ConstellationScanResult result =
                ConstellationSequenceEvaluator.EvaluateGreedy(GreedyStates(expected));

            Assert.True(result.Success);
            Assert.Equal(expected, result.Constellation);
        }

        [Fact]
        public void GreedyUnresolvedHighNodeFailsWithoutDescendingToFalseLowerCount()
        {
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateGreedy(
                new[] { ConstellationNodeState.Unresolved });

            Assert.False(result.Success);
            Assert.Equal(6, result.UnresolvedNode);
            Assert.Equal(-1, result.Constellation);
        }

        [Fact]
        public void GreedyUnresolvedAfterLockedNodesStillFails()
        {
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateGreedy(
                new[]
                {
                    ConstellationNodeState.Locked,
                    ConstellationNodeState.Locked,
                    ConstellationNodeState.Unresolved,
                });

            Assert.False(result.Success);
            Assert.Equal(4, result.UnresolvedNode);
        }

        [Fact]
        public void UnreadableTextCannotSilentlyActAsGreedyLock()
        {
            ConstellationNodeState unreadable =
                Observe(new[] { ("legacy", "visual noise", 0.0f) }, "visual noise").State;

            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateGreedy(
                new[] { unreadable });

            Assert.Equal(ConstellationNodeState.Unresolved, unreadable);
            Assert.False(result.Success);
        }

        [Fact]
        public void UnreadableThenActivatedRecoversOnFreshRetry()
        {
            bool resolved = ReadNode(
                new[]
                {
                    Observe(new[] { ("legacy", "unreadable", 0.0f) }, ""),
                    Observe(new[] { ("legacy", "Activated", 0.8f) }, ""),
                },
                out ConstellationNodeObservation observation,
                out int reads,
                out int waits);

            Assert.True(resolved);
            Assert.Equal(ConstellationNodeState.Activated, observation.State);
            Assert.Equal(2, reads);
            Assert.Equal(1, waits);
        }

        [Fact]
        public void UnreadableThenLockedRecoversOnFreshRetry()
        {
            bool resolved = ReadNode(
                new[]
                {
                    Observe(new[] { ("legacy", "unreadable", 0.0f) }, ""),
                    Observe(new[] { ("legacy", "", 0.0f) }, "Activate required"),
                },
                out ConstellationNodeObservation observation,
                out int reads,
                out int waits);

            Assert.True(resolved);
            Assert.Equal(ConstellationNodeState.Locked, observation.State);
            Assert.Equal(2, reads);
            Assert.Equal(1, waits);
        }

        [Fact]
        public void RepeatedUnresolvedStopsAtBound()
        {
            int reads = 0;
            int waits = 0;

            bool resolved = ConstellationNodeRetry.TryResolve(
                ConstellationNodeRetry.MaximumAttempts,
                attempt =>
                {
                    reads++;
                    if (attempt > ConstellationNodeRetry.MaximumAttempts)
                        throw new InvalidOperationException("Retry bound exceeded.");
                    return Observe(new[] { ("legacy", "noise", 0.0f) }, "noise");
                },
                null,
                () => waits++,
                out ConstellationNodeObservation observation);

            Assert.False(resolved);
            Assert.Equal(ConstellationNodeState.Unresolved, observation.State);
            Assert.Equal(3, ConstellationNodeRetry.MaximumAttempts);
            Assert.Equal(ConstellationNodeRetry.MaximumAttempts, reads);
            Assert.Equal(ConstellationNodeRetry.MaximumAttempts - 1, waits);
        }

        [Fact]
        public void ResolvedFirstCaptureStopsWithoutRetryWaitOrAdditionalRead()
        {
            int reads = 0;
            int waits = 0;

            bool resolved = ConstellationNodeRetry.TryResolve(
                ConstellationNodeRetry.MaximumAttempts,
                _ =>
                {
                    reads++;
                    if (reads > 1)
                        throw new InvalidOperationException("Reader was called after state resolved.");
                    return Observe(new[] { ("alternate", "Activated", 0.5f) }, "");
                },
                null,
                () => waits++,
                out ConstellationNodeObservation observation);

            Assert.True(resolved);
            Assert.Equal(ConstellationNodeState.Activated, observation.State);
            Assert.Equal(1, reads);
            Assert.Equal(0, waits);
        }

        [Fact]
        public void QiqiLikeAlternateActivatedReadPreservesForwardSequence()
        {
            ConstellationNodeObservation nodeOne = Observe(
                new[] { ("legacy", "Activated", 0.96f) },
                "");
            ConstellationNodeObservation nodeTwo = Observe(
                new[]
                {
                    ("legacy", "clipe", 0.0f),
                    ("grayscale_inverted", "Activated", 0.25f),
                },
                "");
            ConstellationNodeObservation nodeThree = Observe(
                new[] { ("legacy", "", 0.0f) },
                "Activate required");

            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                new[] { nodeOne.State, nodeTwo.State, nodeThree.State });

            Assert.True(result.Success);
            Assert.Equal(2, result.Constellation);
        }

        [Fact]
        public void KaeyaLikeActivateRequiredAtC6LetsGreedySequenceContinueSafely()
        {
            ConstellationNodeObservation c6 = Observe(
                new[] { ("legacy", "rostbiting Ei", 0.55f) },
                "Glacial Whirlwind\nActivate required\n0 / 1");

            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateGreedy(
                new[]
                {
                    c6.State,
                    ConstellationNodeState.Locked,
                    ConstellationNodeState.Locked,
                    ConstellationNodeState.Activated,
                });

            Assert.Equal(ConstellationNodeState.Locked, c6.State);
            Assert.True(result.Success);
            Assert.Equal(3, result.Constellation);
        }

        [Fact]
        public void SuccessfulSequenceMarksConstellationSucceeded()
        {
            var character = new Character();
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                ForwardStates(4));

            bool succeeded = CharacterScraper.TrySetScannedConstellation(character, result);

            Assert.True(succeeded);
            Assert.Equal(4, character.Constellation);
            Assert.Equal(CharacterScanPhaseStatus.Succeeded, character.ConstellationScanStatus);
        }

        [Fact]
        public void FailedSequenceMarksConstellationFailedWithoutChangingTalentPhase()
        {
            var character = new Character();
            CharacterScraper.TrySetScannedTalents(
                character,
                new Dictionary<string, int>
                {
                    ["auto"] = 6,
                    ["skill"] = 9,
                    ["burst"] = 10,
                });
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                new[] { ConstellationNodeState.Unresolved });

            bool succeeded = CharacterScraper.TrySetScannedConstellation(character, result);

            Assert.False(succeeded);
            Assert.Equal(-1, character.Constellation);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.ConstellationScanStatus);
            Assert.Equal(CharacterScanPhaseStatus.Succeeded, character.TalentScanStatus);
            Assert.Equal(new[] { 6, 9, 10 }, character.Talents.Values);
        }

        [Theory]
        [InlineData("Cryo")]
        [InlineData("Pyro")]
        public void TravelerKeepsElementSpecificIdentityAndRawConstellation(string element)
        {
            var traveler = new Character
            {
                NameGOOD = "Traveler",
                Element = element,
            };
            ConstellationScanResult result = ConstellationSequenceEvaluator.EvaluateForward(
                ForwardStates(6));

            bool succeeded = CharacterScraper.TrySetScannedConstellation(traveler, result);

            Assert.True(succeeded);
            Assert.Equal("Traveler", traveler.CanonicalName);
            Assert.Equal("Traveler" + element, traveler.NameGOOD);
            Assert.Equal(6, traveler.Constellation);
        }

        private static bool ReadNode(
            IReadOnlyList<ConstellationNodeObservation> freshCaptures,
            out ConstellationNodeObservation observation,
            out int reads,
            out int waits)
        {
            int readCount = 0;
            int waitCount = 0;
            bool resolved = ConstellationNodeRetry.TryResolve(
                freshCaptures.Count,
                _ => freshCaptures[readCount++],
                null,
                () => waitCount++,
                out observation);
            reads = readCount;
            waits = waitCount;
            return resolved;
        }

        private static ConstellationNodeObservation Observe(
            IReadOnlyList<(string Source, string Text, float Confidence)> activated,
            string lockedText,
            float lockedConfidence = 0f,
            bool lockIconEvidence = false)
        {
            IReadOnlyList<ConstellationOcrRead> activatedReads = activated
                .Select(read => new ConstellationOcrRead(read.Source, read.Text, read.Confidence))
                .ToList();
            return ConstellationNodeStateResolver.Resolve(
                activatedReads,
                new ConstellationOcrRead("locked_description", lockedText, lockedConfidence),
                new ConstellationLockIconDetection(
                    lockIconEvidence ? 1.0 : 0.0,
                    ConstellationLockIconDetector.AcceptanceThreshold,
                    Rectangle.Empty));
        }

        public static IEnumerable<object[]> ActivatedStatusFixtures() =>
            Enumerable.Range(1, 5)
                .Select(index => new object[] { $"constellation_status_activated_{index:00}.png" });

        public static IEnumerable<object[]> LockedStatusFixtures() =>
            Enumerable.Range(1, 19)
                .Select(index => new object[] { $"constellation_status_locked_{index:00}.png" });

        public static IEnumerable<object[]> AllStatusFixtures() =>
            ActivatedStatusFixtures()
                .Select(values => new object[] { values[0], false })
                .Concat(LockedStatusFixtures().Select(values => new object[] { values[0], true }));

        private static ConstellationLockIconDetection DetectFixture(
            string fixtureName,
            Size? scaledSize = null)
        {
            string directory = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Constellation");
            using var template = new Bitmap(Path.Combine(
                directory,
                "constellation_status_locked_02.png"));
            using var fixture = new Bitmap(Path.Combine(directory, fixtureName));
            var detector = new ConstellationLockIconDetector(template);
            using Bitmap scaled = scaledSize.HasValue
                ? new Bitmap(fixture, scaledSize.Value)
                : null;
            ConstellationLockIconDetection detection = detector.Detect(
                scaled ?? fixture,
                new Rectangle(320, 70, 96, 96),
                out Bitmap processed);
            processed.Dispose();
            return detection;
        }

        private static IReadOnlyList<ConstellationNodeState> ForwardStates(int constellation)
        {
            var states = Enumerable.Repeat(
                ConstellationNodeState.Activated,
                constellation).ToList();
            if (constellation < ConstellationSequenceEvaluator.NodeCount)
                states.Add(ConstellationNodeState.Locked);
            return states;
        }

        private static IReadOnlyList<ConstellationNodeState> GreedyStates(int constellation)
        {
            var states = Enumerable.Repeat(
                ConstellationNodeState.Locked,
                ConstellationSequenceEvaluator.NodeCount - constellation).ToList();
            if (constellation > 0)
                states.Add(ConstellationNodeState.Activated);
            return states;
        }
    }
}
