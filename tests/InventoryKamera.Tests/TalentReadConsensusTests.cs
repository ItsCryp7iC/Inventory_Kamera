using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public class TalentReadConsensusTests
    {
        [Fact]
        public void SameCompleteTripletTwiceIsAccepted()
        {
            bool accepted = Read(
                new[] { Complete(1, 10, 9), Complete(1, 10, 9) },
                out TalentLevelTriplet triplet,
                out int reads,
                out _);

            Assert.True(accepted);
            Assert.Equal(new TalentLevelTriplet(1, 10, 9), triplet);
            Assert.Equal(2, reads);
        }

        [Fact]
        public void PartialReadBetweenMatchingCompleteReadsIsIgnored()
        {
            bool accepted = Read(
                new[] { Complete(1, 10, 9), "Lv. 1\nLv. 10", Complete(1, 10, 9) },
                out TalentLevelTriplet triplet,
                out int reads,
                out _);

            Assert.True(accepted);
            Assert.Equal(new TalentLevelTriplet(1, 10, 9), triplet);
            Assert.Equal(3, reads);
        }

        [Fact]
        public void FirstCompleteWrongThenRepeatedCorrectAcceptsCorrectTriplet()
        {
            bool accepted = Read(
                new[] { Complete(1, 10, 1), Complete(1, 10, 9), Complete(1, 10, 9) },
                out TalentLevelTriplet triplet,
                out _,
                out _);

            Assert.True(accepted);
            Assert.Equal(new TalentLevelTriplet(1, 10, 9), triplet);
        }

        [Fact]
        public void MalformedReadBetweenMatchingCompleteReadsIsIgnored()
        {
            bool accepted = Read(
                new[] { Complete(6, 10, 10), "unreadable particles", Complete(6, 10, 10) },
                out TalentLevelTriplet triplet,
                out _,
                out _);

            Assert.True(accepted);
            Assert.Equal(new TalentLevelTriplet(6, 10, 10), triplet);
        }

        [Fact]
        public void ReadingStopsImmediatelyWhenConsensusIsReached()
        {
            bool accepted = Read(
                new[] { Complete(1, 2, 3), Complete(1, 2, 3), Complete(9, 9, 9) },
                out _,
                out int reads,
                out int waits);

            Assert.True(accepted);
            Assert.Equal(2, reads);
            Assert.Equal(1, waits);
        }

        [Fact]
        public void OneCompleteTripletOnlyDoesNotReachConsensus()
        {
            bool accepted = Read(
                new[] { Complete(1, 10, 9), "Lv. 1\nLv. 10" },
                out _,
                out _,
                out _);

            Assert.False(accepted);
        }

        [Fact]
        public void AllPartialReadsFail()
        {
            bool accepted = Read(
                new[] { "Lv. 1", "Lv. 10\nLv. 9", "Lv. 1\nLv. 10" },
                out _,
                out _,
                out _);

            Assert.False(accepted);
        }

        [Fact]
        public void EveryCompleteTripletDifferentFails()
        {
            bool accepted = Read(
                new[] { Complete(1, 2, 3), Complete(1, 2, 4), Complete(1, 3, 4) },
                out _,
                out _,
                out _);

            Assert.False(accepted);
        }

        [Fact]
        public void NoCandidateReachingTwoFails()
        {
            bool accepted = Read(
                new[] { Complete(1, 1, 1), "noise", Complete(2, 2, 2), "Lv. 3" },
                out _,
                out _,
                out _);

            Assert.False(accepted);
        }

        [Fact]
        public void MalformedOcrFailsSafely()
        {
            bool accepted = Read(
                new[] { null, string.Empty, "Level ten", "Lv. 0\nLv. 16\nLv. nope" },
                out _,
                out _,
                out _);

            Assert.False(accepted);
        }

        [Fact]
        public void NeverReadsMoreThanTwentyAttempts()
        {
            int reads = 0;

            bool accepted = TalentReadConsensus.TryRead(
                TalentReadConsensus.MaximumAttempts,
                attempt =>
                {
                    reads++;
                    if (attempt > 20) throw new InvalidOperationException("Attempt limit exceeded.");
                    return Complete(((attempt - 1) % 15) + 1, ((attempt - 1) / 15) + 1, 1);
                },
                null,
                null,
                out _);

            Assert.False(accepted);
            Assert.Equal(20, TalentReadConsensus.MaximumAttempts);
            Assert.Equal(TalentReadConsensus.MaximumAttempts, reads);
        }

        [Fact]
        public void LevelOneTripletRemainsValid()
        {
            Assert.Equal(
                new[] { 1, 1, 1 },
                TalentReadConsensus.ParseRows("Lv. 1\nLv. 1\nLv. 1"));
        }

        [Fact]
        public void OptionalPeriodSyntaxRemainsSupported()
        {
            Assert.Equal(
                new[] { 10, 11, 12 },
                TalentReadConsensus.ParseRows("Lv 10\nlV. 11\nLV 12"));
        }

        [Fact]
        public void DisplayedRangeOneThroughFifteenIsValid()
        {
            Assert.Equal(
                Enumerable.Range(1, 15),
                TalentReadConsensus.ParseRows(string.Join("\n", Enumerable.Range(1, 15).Select(level => $"Lv. {level}"))));
        }

        [Fact]
        public void ZeroAndValuesAboveFifteenAreRejected()
        {
            Assert.Equal(
                new[] { 1, 15 },
                TalentReadConsensus.ParseRows("Lv. 0\nLv. 1\nLv. 15\nLv. 16\nLv. 204"));
        }

        [Fact]
        public void FirstThreeValidRowsPreserveTopToBottomOrder()
        {
            TalentReadObservation observation = new TalentReadConsensus().Observe(
                "Normal Attack\nLv. 4\nSkill\nLv. 12\nBurst\nLv. 8\nPassive\nLv. 1");

            Assert.Equal(new[] { 4, 12, 8, 1 }, observation.ParsedRows);
            Assert.Equal(new TalentLevelTriplet(4, 12, 8), observation.Triplet);
        }

        [Fact]
        public void DescriptiveTextIsIgnored()
        {
            Assert.Equal(
                new[] { 6, 9, 10 },
                TalentReadConsensus.ParseRows(
                    "Normal Attack: Performs strikes\nLv. 6\nElemental Skill text\nLv. 9\nEnergy Cost 60\nLv. 10"));
        }

        [Fact]
        public void PartialRowsAreNeverCombinedAcrossAttempts()
        {
            bool accepted = Read(
                new[] { "Lv. 1", "Lv. 10", "Lv. 9", "Lv. 1\nLv. 10" },
                out _,
                out _,
                out _);

            Assert.False(accepted);
        }

        [Fact]
        public void FreshReadDelegateIsInvokedOncePerAttempt()
        {
            var invokedAttempts = new List<int>();

            bool accepted = TalentReadConsensus.TryRead(
                5,
                attempt =>
                {
                    invokedAttempts.Add(attempt);
                    return attempt < 4 ? "unreadable" : Complete(2, 8, 9);
                },
                null,
                null,
                out _);

            Assert.True(accepted);
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, invokedAttempts);
        }

        [Fact]
        public void AcceptedValuesFlowIntoSuccessfulTalentStatus()
        {
            Read(
                new[] { Complete(6, 9, 10), Complete(6, 9, 10) },
                out TalentLevelTriplet triplet,
                out _,
                out _);
            var character = new Character();

            bool succeeded = CharacterScraper.TrySetScannedTalents(character, triplet.ToDictionary());

            Assert.True(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Succeeded, character.TalentScanStatus);
            Assert.Equal(6, character.Talents["auto"]);
            Assert.Equal(9, character.Talents["skill"]);
            Assert.Equal(10, character.Talents["burst"]);
        }

        [Fact]
        public void FailedConsensusFlowsIntoExistingUnavailableStatus()
        {
            bool accepted = Read(
                new[] { Complete(6, 9, 10), "partial Lv. 6" },
                out _,
                out _,
                out _);
            var character = new Character();

            bool succeeded = CharacterScraper.TrySetScannedTalents(
                character,
                accepted ? throw new InvalidOperationException() : CharacterScraper.UnavailableTalents());

            Assert.False(succeeded);
            Assert.Equal(CharacterScanPhaseStatus.Failed, character.TalentScanStatus);
            Assert.All(character.Talents.Values, value => Assert.Equal(-1, value));
        }

        [Fact]
        public void TravelerAdjustmentOccursAfterRawConsensus()
        {
            Read(
                new[] { Complete(6, 8, 10), Complete(6, 8, 10) },
                out TalentLevelTriplet rawTriplet,
                out _,
                out _);
            var traveler = new Character
            {
                NameGOOD = "Traveler",
                Element = "Cryo",
                Constellation = 3,
            };

            Assert.Equal(new TalentLevelTriplet(6, 8, 10), rawTriplet);
            Assert.True(CharacterScraper.TrySetScannedTalents(traveler, rawTriplet.ToDictionary()));
            CharacterScraper.ApplyConstellationTalentAdjustments(traveler, "burst", "skill");

            Assert.Equal(6, traveler.Talents["auto"]);
            Assert.Equal(8, traveler.Talents["skill"]);
            Assert.Equal(7, traveler.Talents["burst"]);
        }

        [Fact]
        public void ExistingSpecialCharacterPostProcessingStillRequiresSuccessfulConsensus()
        {
            Read(
                new[] { Complete(6, 10, 10), Complete(6, 10, 10) },
                out TalentLevelTriplet triplet,
                out _,
                out _);
            var character = new Character();
            CharacterScraper.TrySetScannedTalents(character, triplet.ToDictionary());

            bool tartagliaStyleAdjustment = CharacterScraper.TryApplyTalentAdjustment(character, "auto", -1);
            bool skirkStyleAdjustment = CharacterScraper.TryApplyTalentAdjustment(character, "skill", -1);

            Assert.True(tartagliaStyleAdjustment);
            Assert.True(skirkStyleAdjustment);
            Assert.Equal(5, character.Talents["auto"]);
            Assert.Equal(9, character.Talents["skill"]);
            Assert.Equal(10, character.Talents["burst"]);
        }

        private static bool Read(
            IReadOnlyList<string> captures,
            out TalentLevelTriplet triplet,
            out int reads,
            out int waits)
        {
            int readCount = 0;
            int waitCount = 0;
            bool accepted = TalentReadConsensus.TryRead(
                captures.Count,
                _ => captures[readCount++],
                null,
                () => waitCount++,
                out triplet);
            reads = readCount;
            waits = waitCount;
            return accepted;
        }

        private static string Complete(int auto, int skill, int burst) =>
            $"Lv. {auto}\nLv. {skill}\nLv. {burst}";
    }
}
