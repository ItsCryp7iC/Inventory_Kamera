using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    public sealed class AchievementTitleNormalizerTests
    {
        [Theory]
        [InlineData("  YOU   there!  Fly! ", "you there! fly!")]
        [InlineData("That’s What They Call a Getaway!", "that's what they call a getaway!")]
        [InlineData("That‘s What They Call a Getaway!", "that's what they call a getaway!")]
        [InlineData("“Seeds of Stories, Brought by the Wind…”", "\"seeds of stories, brought by the wind...\"")]
        [InlineData("\" ... Anyone can be a gourmet . \"", "\"... anyone can be a gourmet.\"")]
        [InlineData("If you put your heart into it . . .", "if you put your heart into it...")]
        public void Normalize_CollapsesOnlyApprovedVisualEquivalents(string input, string expected)
        {
            Assert.Equal(expected, AchievementTitleNormalizer.Normalize(input));
        }

        [Theory]
        [InlineData("A-B", "A B")]
        [InlineData("What?", "What!")]
        [InlineData("Seeds of Stories", "Seed of Stories")]
        public void Normalize_PreservesMeaningfulPunctuationAndWords(string left, string right)
        {
            Assert.NotEqual(
                AchievementTitleNormalizer.Normalize(left),
                AchievementTitleNormalizer.Normalize(right));
        }

        [Theory]
        [InlineData("It's the Same As Having Wings", "Its the Same As Having Wings")]
        [InlineData("Prodigal Son's Return", "Prodigal Sons Return")]
        [InlineData("Snow-Stored History", "Snow Stored History")]
        [InlineData("Snow—Stored History", "Snow Stored History")]
        [InlineData("Déjà Vu!", "Deja Vu!")]
        [InlineData("Déjà Vu!", "Déja Vu!")]
        [InlineData("Déjà Vu!", "Dejà Vu!")]
        public void ComparisonNormalization_FoldsOnlyApprovedOcrUnstableForms(
            string left,
            string right)
        {
            Assert.Equal(
                AchievementTitleNormalizer.NormalizeForComparison(left),
                AchievementTitleNormalizer.NormalizeForComparison(right));
        }

        [Fact]
        public void ComparisonNormalization_DoesNotAlterCanonicalNormalization()
        {
            Assert.Equal("déjà vu!", AchievementTitleNormalizer.Normalize("Déjà Vu!"));
            Assert.Equal("deja vu!", AchievementTitleNormalizer.NormalizeForComparison("Déjà Vu!"));
            Assert.Equal("snow-stored history", AchievementTitleNormalizer.Normalize("Snow-Stored History"));
            Assert.Equal("snow stored history", AchievementTitleNormalizer.NormalizeForComparison("Snow-Stored History"));
        }

        [Fact]
        public void Match_OneCharacterOcrSubstitutionExceedsCalibratedThreshold()
        {
            AchievementTitleMatch match = AchievementTitleMatcher.Match(
                "You There! FIy!",
                "You There! Fly!",
                Array.Empty<string>());

            Assert.True(match.Score >= AchievementTitleMatcher.MinimumScore, match.Score.ToString());
            Assert.Equal(0, match.RunnerUpScore);
        }

        [Fact]
        public void Match_CloseRunnerUpDoesNotMeetRequiredMargin()
        {
            AchievementTitleMatch match = AchievementTitleMatcher.Match(
                "The Wind Glows",
                "The Wind Blows",
                new[] { "The Wind Flows" });

            Assert.True(match.Score >= AchievementTitleMatcher.MinimumScore, match.Score.ToString());
            Assert.True(match.RunnerUpScore >= AchievementTitleMatcher.MinimumScore, match.RunnerUpScore.ToString());
            Assert.True(match.Margin < AchievementTitleMatcher.MinimumRunnerUpMargin, match.Margin.ToString());
        }

        [Theory]
        [InlineData("Narzissenkreuz Notes: The Labyrinth", "Narzissenkreuz Notes")]
        [InlineData("The Finishing Touch?", "The Finishing Touch")]
        public void Match_CanonicalTitleExtensionIsNotTreatedAsNoisySuffix(
            string expectedTitle,
            string shorterCanonicalTitle)
        {
            AchievementTitleMatch match = AchievementTitleMatcher.Match(
                expectedTitle,
                expectedTitle,
                new[] { shorterCanonicalTitle });

            Assert.Equal(100, match.Score);
            Assert.True(match.RunnerUpScore < 100, match.RunnerUpScore.ToString());
            Assert.True(match.Margin >= AchievementTitleMatcher.MinimumRunnerUpMargin, match.Margin.ToString());
        }

        [Theory]
        [InlineData("Cecilia Garden", "Cecilla Garden")]
        [InlineData("Hidden Palace of Zhou Formula", "Hidden Palace of Zhou FormuIa")]
        [InlineData("It's the Same As Having Wings", "Its the Same As Having Wings")]
        [InlineData("Prodigal Son's Return", "Prodigal Sons Return")]
        [InlineData("Snow-Stored History", "Snow Stored History")]
        [InlineData("Déjà Vu!", "Deja Vu!")]
        [InlineData("As You Wish", "As You Wlsh")]
        [InlineData("Cat in the Clouds", "Cat in the CIouds")]
        public void Match_LiveFailureTitlePlausibleOcrFormsMeetUnchangedThreshold(
            string expectedTitle,
            string observedTitle)
        {
            AchievementTitleMatch match = AchievementTitleMatcher.Match(
                observedTitle,
                expectedTitle,
                Array.Empty<string>());

            Assert.True(match.Score >= AchievementTitleMatcher.MinimumScore, match.Score.ToString());
        }

        [Fact]
        public void PackagedPunctuationHeavyTitlesRemainCanonicalAndDistinct()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            string[] titles =
            {
                "\"Seeds of Stories, Brought by the Wind...\"",
                "\"If you put your heart into it...\"",
                "\"...Anyone can be a gourmet.\"",
                "\"That's What They Call a Getaway!\"",
                "Initiating Warp Drive!",
                "Déjà Vu!",
            };

            Assert.All(titles, title => Assert.Single(catalog.FindByExactName(title)));
            Assert.Equal(titles.Length, titles
                .Select(AchievementTitleNormalizer.Normalize)
                .Distinct(StringComparer.Ordinal)
                .Count());
        }

        [Fact]
        public void PackagedCatalog_ComparisonKeysIntroduceNoNewUnrelatedCollision()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            var comparisonGroups = catalog.All
                .GroupBy(entry => AchievementTitleNormalizer.NormalizeForComparison(entry.Name))
                .ToArray();
            var newlyCollapsedTitles = comparisonGroups
                .Where(group => group
                    .Select(entry => AchievementTitleNormalizer.Normalize(entry.Name))
                    .Distinct(StringComparer.Ordinal)
                    .Count() > 1)
                .ToArray();

            Assert.Equal(1854, catalog.All.Count);
            Assert.Equal(1559, comparisonGroups.Length);
            Assert.Empty(newlyCollapsedTitles);
            Assert.Single(catalog.TitleGroups.Where(group => group.IsUnrelatedCollision));
            Assert.Equal(
                "Let the Wind Lead",
                catalog.TitleGroups.Single(group => group.IsUnrelatedCollision).CanonicalTitle);
            Assert.All(
                catalog.TitleGroups.Where(group => group.Entries.Count > 1 && !group.IsUnrelatedCollision),
                group => Assert.True(group.IsStageFamily, group.CanonicalTitle));
        }
    }

    public sealed class AchievementResultTitleReaderTests
    {
        private static readonly Size FixtureSize = new(1919, 1079);
        private readonly ITestOutputHelper output;

        public AchievementResultTitleReaderTests(ITestOutputHelper output) => this.output = output;

        [Theory]
        [InlineData("achievement_result_title_you_there_01.png", "You There! Fly!")]
        [InlineData("achievement_result_title_moonmist_01.png", "Moonmist Treasure Hunter")]
        [InlineData("achievement_result_title_pur_01.png", "The Purest Heat")]
        public void ExistingSanitizedHighlightedTitleFixtures_HavePositiveBoundedOcrEvidence(
            string fixtureName,
            string expectedTitle)
        {
            using Bitmap screenshot = BuildTitleComposite(fixtureName);
            using var ocr = new OcrService(engineCount: 1);
            ocr.Restart();
            var reader = new AchievementResultTitleReader(ocr, new ImageProcessor());

            AchievementResultTitleRead result = reader.Read(screenshot);

            Assert.Equal(6, result.Attempts.Count);
            Assert.Equal(3, result.Attempts.Count(attempt =>
                attempt.Scope == AchievementTitleOcrScope.Primary));
            Assert.Equal(3, result.Attempts.Count(attempt =>
                attempt.Scope == AchievementTitleOcrScope.WideFallback));
            foreach (AchievementTitleOcrAttempt attempt in result.Attempts)
            {
                double score = AchievementTitleMatcher.Similarity(attempt.NormalizedText, expectedTitle);
                output.WriteLine(
                    "{0}: raw=\"{1}\"; normalized=\"{2}\"; confidence={3:0.000}; score={4:0.0}",
                    attempt.Variant,
                    attempt.RawText.Replace("\r", " ").Replace("\n", " "),
                    attempt.NormalizedText,
                    attempt.Confidence,
                    score);
            }
            Assert.Contains(result.Attempts, attempt =>
                attempt.Confidence >= AchievementTitleMatcher.MinimumOcrConfidence &&
                AchievementTitleMatcher.Similarity(attempt.NormalizedText, expectedTitle) >=
                    AchievementTitleMatcher.MinimumScore);
        }

        private static Bitmap BuildTitleComposite(string fixtureName)
        {
            var screenshot = new Bitmap(FixtureSize.Width, FixtureSize.Height);
            using Graphics graphics = Graphics.FromImage(screenshot);
            graphics.Clear(Color.FromArgb(34, 44, 77));
            Rectangle region = AchievementScreenRegions.For(FixtureSize).FirstResultTitle;
            using var crop = new Bitmap(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Achievements",
                fixtureName));
            Assert.Equal(region.Size, crop.Size);
            graphics.DrawImageUnscaled(crop, region.Location);
            return screenshot;
        }
    }

    public sealed class AchievementResultIdentityVerifierTests
    {
        private const int YouThereId = 81665;
        private const int ZooTycoonId = 80127;

        [Fact]
        public void ExactUniqueVisibleTitle_IsIdentifiedFromPackagedCatalog()
        {
            Harness harness = Harness.Create(Active(1), "You There! Fly!", 0.96f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.Equal(YouThereId, result.IdentifiedId);
            Assert.Null(result.StageGroupId);
            Assert.Equal(new[] { YouThereId }, result.CandidateIds);
            Assert.Equal(100, result.Score);
        }

        [Fact]
        public void OcrTolerantUniqueVisibleTitle_IsIdentified()
        {
            Harness harness = Harness.Create(Active(1), "You There! FIy!", 0.90f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.True(result.Score >= AchievementTitleMatcher.MinimumScore, result.Diagnostics);
        }

        [Theory]
        [InlineData(81015, "Cecilia Garden", "Cecilia Garden al =. __* _tL 24-1")]
        [InlineData(81016, "Hidden Palace of Zhou Formula", "Hidden Palace of Zhou Formula cereal = —=T1o Al = +")]
        [InlineData(81021, "It's the Same As Having Wings", "Its the Same As Having Wings")]
        [InlineData(81035, "Prodigal Son's Return", "Prodigal Son's Return —c = == 1 = == Jd")]
        [InlineData(81036, "Snow-Stored History", "Snow-Stored History 1 = a 2---_- - “@&°")]
        [InlineData(81078, "Déjà Vu!", "Déja Vu!")]
        [InlineData(81150, "As You Wish", "As You Wish a =1 1 Ff 2°... - - BL")]
        [InlineData(81158, "Cat in the Clouds", "Cat in the Clouds meee = 1 “8 -2T oo =")]
        public void LiveFailureTitles_HaveSafePositiveComparisonEvidence(
            int expectedId,
            string expectedTitle,
            string observedTitle)
        {
            Harness harness = Harness.Create(Active(1), observedTitle, 0.90f);

            AchievementIdentityResult result = harness.Verify(expectedId);

            Assert.Equal(expectedTitle, result.ExpectedTitle);
            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.True(result.Score >= AchievementTitleMatcher.MinimumScore, result.Diagnostics);
            Assert.True(result.Margin >= AchievementTitleMatcher.MinimumRunnerUpMargin, result.Diagnostics);
        }

        [Fact]
        public void AcceptedVariant_IsNotMaskedByHigherScoreLowConfidenceVariant()
        {
            Harness harness = Harness.Create(
                Active(1),
                new[]
                {
                    Attempt("You There! Fly!", 0.20f),
                    Attempt("You There! FIy!", 0.90f),
                });

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.Equal("You There! FIy!", result.RawTitle);
            Assert.Equal(0.90f, result.OcrConfidence);
        }

        [Fact]
        public void BelowThresholdNearRead_IsUnresolved()
        {
            Harness harness = Harness.Create(Active(1), "You There Fly", 0.90f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.Contains("below the positive identity threshold", result.Reason);
        }

        [Fact]
        public void RunnerUpTooClose_IsAmbiguous()
        {
            AchievementCatalog catalog = BuildCatalog(
                Entry(1, "The Wind Blows"),
                Entry(2, "The Wind Flows"));
            Harness harness = Harness.Create(
                Active(1),
                "The Wind Glows",
                0.90f,
                new AchievementCatalogProvider(catalog));

            AchievementIdentityResult result = harness.Verify(1);

            Assert.Equal(AchievementIdentityState.Ambiguous, result.State);
            Assert.True(result.Margin < AchievementTitleMatcher.MinimumRunnerUpMargin, result.Diagnostics);
            Assert.Null(result.IdentifiedId);
        }

        [Fact]
        public void SingleMatchWithUnrelatedVisibleTitle_IsMismatch()
        {
            Harness harness = Harness.Create(Active(1), "Completely Different", 0.95f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Mismatch, result.State);
            Assert.Null(result.IdentifiedId);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(5)]
        public void MultipleMatches_AreAmbiguousWithoutReadingFirstTitle(int matchCount)
        {
            Harness harness = Harness.Create(Active(matchCount), "The Purest Heat", 0.95f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Ambiguous, result.State);
            Assert.Equal(0, harness.TitleReader.ReadCount);
            Assert.Null(result.IdentifiedId);
        }

        [Fact]
        public void InvalidMatchCount_IsUnresolved()
        {
            Harness harness = Harness.Create(Detection(AchievementScreenState.SearchActive), "You There! Fly!", 0.95f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.Contains("valid Match(es) count", result.Reason);
            Assert.Equal(0, harness.TitleReader.ReadCount);
        }

        [Theory]
        [InlineData((int)AchievementScreenState.Idle)]
        [InlineData((int)AchievementScreenState.Unknown)]
        [InlineData((int)AchievementScreenState.NotAchievementScreen)]
        public void NonSearchActiveCurrentScreen_IsUnresolved(int stateValue)
        {
            Harness harness = Harness.Create(Detection((AchievementScreenState)stateValue), "You There! Fly!", 0.95f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.Equal(0, harness.TitleReader.ReadCount);
        }

        [Fact]
        public void PriorSearchActiveAndFreshSinglePageMarkers_AllowTitleProofAfterTransientUnknown()
        {
            Harness harness = Harness.Create(
                Detection(AchievementScreenState.Unknown, "Match(es): 1", "1/1"),
                "It's the Same As Having Wings",
                0.96f);

            AchievementIdentityResult result = harness.Verify(81021);

            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.True(result.UsedCorroboratedSearchResult);
            Assert.Equal(1, harness.TitleReader.ReadCount);
        }

        [Fact]
        public void EmptyTitleOcr_IsUnresolved()
        {
            Harness harness = Harness.Create(Active(1), string.Empty, 0.95f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.Contains("no usable text", result.Reason);
        }

        [Fact]
        public void LowConfidenceExactTitle_IsUnresolved()
        {
            Harness harness = Harness.Create(Active(1), "You There! Fly!", 0.20f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.Contains("confidence", result.Reason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ZooTycoon_IdentifiesStageFamilyWithoutSelectingStage()
        {
            Harness harness = Harness.Create(Active(1), "Zoo Tycoon", 0.95f);

            AchievementIdentityResult result = harness.Verify(ZooTycoonId);

            Assert.Equal(AchievementIdentityState.StageGroupIdentified, result.State);
            Assert.Equal(80127, result.StageGroupId);
            Assert.Null(result.IdentifiedId);
            Assert.Equal(new[] { 80127, 80128, 80129 }, result.CandidateIds);
        }

        [Fact]
        public void MoonmistCanonicalTarget_IdentifiesItsCatalogStageFamilyNotQueryTextOrStage()
        {
            Harness harness = Harness.Create(Active(1), "Moonmist Treasure Hunter", 0.95f);

            AchievementIdentityResult result = harness.Verify(80490);

            Assert.Equal(AchievementIdentityState.StageGroupIdentified, result.State);
            Assert.Equal(80490, result.StageGroupId);
            Assert.Null(result.IdentifiedId);
            Assert.Equal(new[] { 80490, 80491, 80492 }, result.CandidateIds);
        }

        [Fact]
        public void LongCanonicalTitle_StrongLeftPrefixAndSingleResultIsIdentified()
        {
            Harness harness = Harness.Create(
                Active(1),
                "Just Me and You, the Sky So Blue",
                0.95f);

            AchievementIdentityResult result = harness.Verify(86010);

            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.True(result.UsedClippedPrefixEvidence);
            Assert.Equal(86010, result.IdentifiedId);
        }

        [Fact]
        public void LongCanonicalTitle_LiveClippedPrefixWithBoundedTailErrorIsIdentified()
        {
            Harness harness = Harness.Create(
                Active(1),
                "Just A Me and You, the Sky So Blue, and Almost Gett",
                0.91f);

            AchievementIdentityResult result = harness.Verify(86010);

            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.True(result.UsedClippedPrefixEvidence);
            Assert.Equal(86010, result.IdentifiedId);
        }

        [Theory]
        [InlineData(81048, "...You could hear Paimon all along, couldn!")]
        [InlineData(81154, "Today, This Seal — Tomorrow, Watatstimi")]
        [InlineData(81168, "Nihil Sul Caligine Novi")]
        [InlineData(81220, "\"All We Need Is Some Firewood and Some \\")]
        [InlineData(81252, "The Jasmines Whisper, the Pomegranates /")]
        [InlineData(81309, "From Soil You Are, and to the Sand You Sh:")]
        [InlineData(81315, "Like a Morning Sun Coming Out of Gloomy")]
        [InlineData(81335, "When the Red Scarf Transforms Into a Bir")]
        [InlineData(81337, "Whose Descendant Are You, and What's Yo")]
        [InlineData(81366, "Fontaine Expects That Everyone Will Do TI")]
        [InlineData(81384, "The Calendar of the Future Is Longer Than")]
        [InlineData(81388, "The Worst! Fontaine's Eight Evil Clockwor")]
        [InlineData(81389, "Automated Supercomputing Field Genera!")]
        [InlineData(81390, "Slam-Bang No-Holds Barred Meropide-Style")]
        public void LiveClippedTitleForms_ResolveOnlyThroughCorroboratedPrefix(
            int expectedId,
            string observed)
        {
            Harness harness = Harness.Create(Active(1), observed, 0.90f);

            AchievementIdentityResult result = harness.Verify(expectedId);

            Assert.True(
                result.State == AchievementIdentityState.Identified ||
                result.State == AchievementIdentityState.StageGroupIdentified,
                result.Diagnostics);
            Assert.True(result.UsedClippedPrefixEvidence, result.Diagnostics);
        }

        [Fact]
        public void ClippedPrefixSharedByUnrelatedCatalogTitle_RemainsUnresolved()
        {
            const string observed = "A Conservatively Long Shared Prefix";
            AchievementCatalog catalog = BuildCatalog(
                Entry(1, observed + " First Ending"),
                Entry(2, observed + " Second Ending"));
            Harness harness = Harness.Create(
                Active(1),
                observed,
                0.95f,
                new AchievementCatalogProvider(catalog));

            AchievementIdentityResult result = harness.Verify(1);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.False(result.UsedClippedPrefixEvidence);
        }

        [Fact]
        public void ShortCanonicalPrefix_RemainsUnresolvedEvenWithSingleResult()
        {
            Harness harness = Harness.Create(
                Active(1),
                "Just Me and You",
                0.95f);

            AchievementIdentityResult result = harness.Verify(86010);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.False(result.UsedClippedPrefixEvidence);
        }

        [Theory]
        [InlineData(84004)]
        [InlineData(80032)]
        public void LetTheWindLead_RemainsAmbiguousWithoutRequestedIdOrCatalogOrderTieBreak(
            int requestedId)
        {
            Harness harness = Harness.Create(Active(1), "Let the Wind Lead", 0.95f);

            AchievementIdentityResult result = harness.Verify(requestedId);

            Assert.Equal(AchievementIdentityState.Ambiguous, result.State);
            Assert.Null(result.IdentifiedId);
            Assert.Null(result.StageGroupId);
            Assert.Equal(new[] { 80032, 84004 }, result.CandidateIds);
            Assert.Equal(100, result.Score);
            Assert.Equal(1, result.ScreenDetection.MatchCountValue);
        }

        [Fact]
        public void UnknownExpectedId_FailsSafelyWithoutOcr()
        {
            Harness harness = Harness.Create(Active(1), "You There! Fly!", 0.95f);

            AchievementIdentityResult result = harness.Verify(999999);

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.Equal(0, harness.ScreenDetector.DetectCount);
            Assert.Equal(0, harness.TitleReader.ReadCount);
        }

        [Fact]
        public void FailedPhase6CResult_FailsBeforeCurrentScreenOrTitleRead()
        {
            Harness harness = Harness.Create(Active(1), "You There! Fly!", 0.95f);

            AchievementIdentityResult result = harness.Verifier.Verify(
                SearchResult(succeeded: false),
                YouThereId,
                new Bitmap(1, 1));

            Assert.Equal(AchievementIdentityState.Unresolved, result.State);
            Assert.Equal(0, harness.ScreenDetector.DetectCount);
            Assert.Equal(0, harness.TitleReader.ReadCount);
        }

        [Fact]
        public void Diagnostics_AreBoundedAndContainRequiredIdentityFields()
        {
            string longRead = new string('X', 1000);
            Harness harness = Harness.Create(Active(1), longRead, 0.95f);

            AchievementIdentityResult result = harness.Verify(YouThereId);

            Assert.Contains("operation=identity-verify", result.Diagnostics);
            Assert.Contains("expectedId=81665", result.Diagnostics);
            Assert.Contains("expectedTitle=You There! Fly!", result.Diagnostics);
            Assert.Contains("expectedTitleLength=15", result.Diagnostics);
            Assert.Contains("screenState=SearchActive", result.Diagnostics);
            Assert.Contains("matchCountRaw=Match(es): 1", result.Diagnostics);
            Assert.Contains("matchCount=1", result.Diagnostics);
            Assert.Contains("page=1/1", result.Diagnostics);
            Assert.Contains("rawTitle=", result.Diagnostics);
            Assert.Contains("normalizedTitle=", result.Diagnostics);
            Assert.Contains("comparisonTitle=", result.Diagnostics);
            Assert.Contains("ocrConfidence=", result.Diagnostics);
            Assert.Contains("score=", result.Diagnostics);
            Assert.Contains("bestAlternateTitle=", result.Diagnostics);
            Assert.Contains("runnerUpScore=", result.Diagnostics);
            Assert.Contains("margin=", result.Diagnostics);
            Assert.Contains("titleGroupKind=Unique", result.Diagnostics);
            Assert.Contains("candidateCount=1", result.Diagnostics);
            Assert.Contains("candidateIds=81665", result.Diagnostics);
            Assert.Contains("collision=False", result.Diagnostics);
            Assert.Contains("multiMatch=False", result.Diagnostics);
            Assert.Contains("usedCorroboratedSearchResult=False", result.Diagnostics);
            Assert.Contains("state=", result.Diagnostics);
            Assert.Contains("finalIdentityState=", result.Diagnostics);
            Assert.Contains("reason=", result.Diagnostics);
            Assert.Contains("finalRejectionReason=", result.Diagnostics);
            Assert.DoesNotContain(new string('X', 200), result.Diagnostics);
            Assert.DoesNotContain("completion", result.Diagnostics, StringComparison.OrdinalIgnoreCase);
            Assert.True(result.Diagnostics.Length < 4096, result.Diagnostics.Length.ToString());
        }

        private static AchievementCatalog BuildCatalog(params AchievementCatalogEntry[] entries) =>
            new(entries);

        private static AchievementCatalogEntry Entry(int id, string name) => new(
            id,
            name,
            string.Empty,
            categoryId: 1,
            hidden: false,
            version: "1.0",
            order: id,
            stageGroupId: null,
            stageIndex: null,
            stageCount: null);

        private static AchievementTitleOcrAttempt Attempt(string text, float confidence) => new(
            AchievementTitleOcrVariant.GrayscaleContrast,
            text,
            confidence,
            string.IsNullOrWhiteSpace(text) ? "empty test read" : "test read");

        private static AchievementScreenDetection Active(int count) => Detection(
            AchievementScreenState.SearchActive,
            $"Match(es): {count}",
            "1/1");

        private static AchievementScreenDetection Detection(
            AchievementScreenState state,
            string matchText = "",
            string pageText = "")
        {
            AchievementMatchCountParseResult match = AchievementMatchCountParser.Parse(matchText);
            AchievementPageCounterParseResult page = AchievementPageCounterParser.Parse(pageText);
            return new AchievementScreenDetection(
                state,
                AchievementTextEvidence.ForTest("Achievements", state != AchievementScreenState.NotAchievementScreen),
                AchievementSearchFieldEvidence.ForTest(activeControlsAccepted: state == AchievementScreenState.SearchActive),
                AchievementTextEvidence.ForTest("Achievements completed: 1", state != AchievementScreenState.NotAchievementScreen),
                AchievementTextEvidence.ForTest(matchText, match.Accepted),
                match,
                AchievementTextEvidence.ForTest(pageText, page.Accepted),
                page,
                AchievementScreenRegions.For(new Size(1920, 1080)),
                "test detection");
        }

        private static AchievementSearchNavigationResult SearchResult(bool succeeded = true) => new(
            "search",
            succeeded,
            cancelled: false,
            failureReason: succeeded ? null : "test failure",
            beforeDetection: Detection(AchievementScreenState.Idle),
            afterOpenScreenDetection: null,
            afterOpenFocusDetection: null,
            afterTypingFocusDetection: null,
            afterDetection: succeeded ? Active(1) : null,
            attempts: 1,
            diagnostics: string.Empty);

        private sealed class Harness
        {
            private Harness(
                RecordingScreenDetector screenDetector,
                RecordingTitleReader titleReader,
                AchievementResultIdentityVerifier verifier)
            {
                ScreenDetector = screenDetector;
                TitleReader = titleReader;
                Verifier = verifier;
            }

            internal RecordingScreenDetector ScreenDetector { get; }
            internal RecordingTitleReader TitleReader { get; }
            internal AchievementResultIdentityVerifier Verifier { get; }

            internal static Harness Create(
                AchievementScreenDetection detection,
                string title,
                float confidence,
                IAchievementCatalogProvider provider = null)
            {
                return Create(detection, new[] { Attempt(title, confidence) }, provider);
            }

            internal static Harness Create(
                AchievementScreenDetection detection,
                IEnumerable<AchievementTitleOcrAttempt> attempts,
                IAchievementCatalogProvider provider = null)
            {
                var screenDetector = new RecordingScreenDetector(detection);
                var titleReader = new RecordingTitleReader(attempts);
                var verifier = new AchievementResultIdentityVerifier(
                    provider ?? new AchievementCatalogProvider(),
                    screenDetector,
                    titleReader);
                return new Harness(screenDetector, titleReader, verifier);
            }

            internal AchievementIdentityResult Verify(int expectedId)
            {
                using var screenshot = new Bitmap(1, 1);
                return Verifier.Verify(SearchResult(), expectedId, screenshot);
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

        private sealed class RecordingTitleReader : IAchievementResultTitleReader
        {
            private readonly AchievementResultTitleRead read;
            internal RecordingTitleReader(IEnumerable<AchievementTitleOcrAttempt> attempts)
            {
                read = new AchievementResultTitleRead(attempts);
            }
            internal int ReadCount { get; private set; }
            public AchievementResultTitleRead Read(Bitmap screenshot)
            {
                ReadCount++;
                return read;
            }
        }
    }

    public sealed class AchievementResultIdentityFixtureTests
    {
        private static readonly Size FixtureSize = new(1919, 1079);
        private readonly ITestOutputHelper output;

        public AchievementResultIdentityFixtureTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void SanitizedYouThereComposite_IdentifiesUniqueCatalogEntry()
        {
            using Bitmap screenshot = BuildComposite(
                "achievement_heading_search_keyboard_01.png",
                "achievement_search_active_you_there_01.png",
                "achievement_completed_search_keyboard_01.png",
                "achievement_match_count_1_keyboard_01.png",
                "achievement_page_1_1_keyboard_01.png",
                "achievement_result_title_you_there_01.png");

            AchievementIdentityResult result = Verify(screenshot, 81665);

            output.WriteLine(result.Diagnostics);
            Assert.Equal(AchievementIdentityState.Identified, result.State);
            Assert.Equal(81665, result.IdentifiedId);
        }

        [Fact]
        public void SanitizedMoonmistComposite_IdentifiesCatalogStageFamily()
        {
            using Bitmap screenshot = BuildComposite(
                "achievement_heading_search_keyboard_01.png",
                "achievement_search_active_moonmist_01.png",
                "achievement_completed_search_keyboard_01.png",
                "achievement_match_count_1_keyboard_01.png",
                "achievement_page_1_1_keyboard_01.png",
                "achievement_result_title_moonmist_01.png");

            AchievementIdentityResult result = Verify(screenshot, 80490);

            output.WriteLine(result.Diagnostics);
            Assert.Equal(AchievementIdentityState.StageGroupIdentified, result.State);
            Assert.Equal(new[] { 80490, 80491, 80492 }, result.CandidateIds);
            Assert.Null(result.IdentifiedId);
        }

        [Fact]
        public void SanitizedPurComposite_RemainsAmbiguousBeforeFirstResultInference()
        {
            using Bitmap screenshot = BuildComposite(
                "achievement_heading_search_controller_01.png",
                "achievement_search_active_pur_controller_01.png",
                "achievement_completed_search_controller_01.png",
                "achievement_match_count_5_controller_01.png",
                "achievement_page_1_1_controller_01.png",
                "achievement_result_title_pur_01.png");

            AchievementIdentityResult result = Verify(screenshot, 81572);

            output.WriteLine(result.Diagnostics);
            Assert.Equal(AchievementIdentityState.Ambiguous, result.State);
            Assert.Empty(result.Attempts);
            Assert.Null(result.IdentifiedId);
        }

        private static AchievementIdentityResult Verify(Bitmap screenshot, int expectedId)
        {
            using var ocr = new OcrService(engineCount: 1);
            ocr.Restart();
            var processor = new ImageProcessor();
            var screenDetector = new AchievementScreenDetector(ocr, processor);
            var verifier = new AchievementResultIdentityVerifier(
                new AchievementCatalogProvider(),
                screenDetector,
                new AchievementResultTitleReader(ocr, processor));
            AchievementScreenDetection prior = Active(1);
            var navigation = new AchievementSearchNavigationResult(
                "search",
                succeeded: true,
                cancelled: false,
                failureReason: null,
                beforeDetection: null,
                afterOpenScreenDetection: null,
                afterOpenFocusDetection: null,
                afterTypingFocusDetection: null,
                afterDetection: prior,
                attempts: 1,
                diagnostics: string.Empty);
            return verifier.Verify(navigation, expectedId, screenshot);
        }

        private static AchievementScreenDetection Active(int count) =>
            AchievementScreenDetector.Classify(
                FixtureSize,
                AchievementTextEvidence.ForTest("Achievements", true),
                AchievementSearchFieldEvidence.ForTest(activeControlsAccepted: true),
                AchievementTextEvidence.ForTest("Achievements completed: 1", true),
                AchievementMatchCountParser.Parse($"Match(es): {count}"),
                AchievementPageCounterParser.Parse("1/1"));

        private static Bitmap BuildComposite(
            string headingFixture,
            string searchFixture,
            string completedFixture,
            string matchFixture,
            string pageFixture,
            string titleFixture)
        {
            var screenshot = new Bitmap(FixtureSize.Width, FixtureSize.Height);
            using Graphics graphics = Graphics.FromImage(screenshot);
            graphics.Clear(Color.FromArgb(34, 44, 77));
            AchievementScreenRegions regions = AchievementScreenRegions.For(FixtureSize);
            Draw(graphics, headingFixture, regions.Heading);
            Draw(graphics, searchFixture, regions.SearchField);
            Draw(graphics, completedFixture, regions.CompletedCount);
            Draw(graphics, matchFixture, regions.MatchCount);
            Draw(graphics, pageFixture, regions.PageCounter);
            Draw(graphics, titleFixture, regions.FirstResultTitle);
            return screenshot;
        }

        private static void Draw(Graphics graphics, string fixtureName, Rectangle region)
        {
            using var crop = new Bitmap(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Achievements",
                fixtureName));
            Assert.Equal(region.Size, crop.Size);
            graphics.DrawImageUnscaled(crop, region.Location);
        }
    }
}
