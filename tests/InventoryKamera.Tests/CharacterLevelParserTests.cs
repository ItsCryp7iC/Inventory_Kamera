using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public class CharacterLevelParserTests
    {
        [Theory]
        [InlineData("Level 1 / 20", 1, 20, false)]
        [InlineData("20/20", 20, 20, false)]
        [InlineData("Level 20 / 40", 20, 40, true)]
        [InlineData("40/40", 40, 40, false)]
        [InlineData("Level 40 / 50", 40, 50, true)]
        [InlineData("50/50", 50, 50, false)]
        [InlineData("50/60", 50, 60, true)]
        [InlineData("60/60", 60, 60, false)]
        [InlineData("60/70", 60, 70, true)]
        [InlineData("70/70", 70, 70, false)]
        [InlineData("70/80", 70, 80, true)]
        [InlineData("80/80", 80, 80, false)]
        [InlineData("80/90", 80, 90, true)]
        [InlineData("90/90", 90, 90, false)]
        [InlineData("90/100", 90, 100, true)]
        [InlineData("100/100", 100, 100, false)]
        public void Parse_AcceptsSupportedPairsAndPreservesAscensionSemantics(
            string raw,
            int expectedLevel,
            int expectedMaxLevel,
            bool expectedAscended)
        {
            CharacterLevelParseResult result = CharacterLevelParser.Parse(raw);

            Assert.True(result.Success);
            Assert.Equal(expectedLevel, result.Level);
            Assert.Equal(expectedMaxLevel, result.MaxLevel);
            Assert.Equal(expectedAscended, result.Ascended);
        }

        [Theory]
        [InlineData("Level 20 4/20", "204/20")]
        [InlineData("90/80", "90/80")]
        [InlineData("0/20", "0/20")]
        [InlineData("Level -1 / 20", "1/20")]
        [InlineData("garbled", "")]
        [InlineData("20/30", "20/30")]
        [InlineData("20", "20")]
        [InlineData("20//40", "20//40")]
        [InlineData("80/89", "80/89")]
        public void Parse_RejectsImplausibleOrMalformedPairs(string raw, string expectedFiltered)
        {
            CharacterLevelParseResult result = CharacterLevelParser.Parse(raw);

            Assert.False(result.Success);
            Assert.Equal(expectedFiltered, result.FilteredText);
            Assert.False(string.IsNullOrWhiteSpace(result.RejectionReason));
        }

        [Fact]
        public void ReadFirstPlausible_RetriesInvalidAttemptThenAcceptsFreshValidAttempt()
        {
            var reads = new Queue<string>(new[] { "Level 20 4/20", "Level 20 / 20" });
            var rejectedAttempts = new List<int>();
            int readCount = 0;

            CharacterLevelParseResult result = CharacterLevelParser.ReadFirstPlausible(
                maxAttempts: 20,
                attempt =>
                {
                    readCount++;
                    return reads.Dequeue();
                },
                (attempt, _) => rejectedAttempts.Add(attempt));

            Assert.True(result.Success);
            Assert.Equal(20, result.Level);
            Assert.Equal(2, readCount);
            Assert.Equal(new[] { 1 }, rejectedAttempts);
        }

        [Fact]
        public void ReadFirstPlausible_RetriesMultipleInvalidAttemptsThenAcceptsValidAttempt()
        {
            var reads = new Queue<string>(new[] { "garbled", "90/80", "80/90" });
            int readCount = 0;

            CharacterLevelParseResult result = CharacterLevelParser.ReadFirstPlausible(
                maxAttempts: 20,
                _ =>
                {
                    readCount++;
                    return reads.Dequeue();
                });

            Assert.True(result.Success);
            Assert.Equal(80, result.Level);
            Assert.True(result.Ascended);
            Assert.Equal(3, readCount);
        }

        [Fact]
        public void ReadFirstPlausible_AllAttemptsInvalid_ReturnsExplicitFailure()
        {
            int readCount = 0;
            int rejectedCount = 0;

            CharacterLevelParseResult result = CharacterLevelParser.ReadFirstPlausible(
                maxAttempts: 3,
                _ =>
                {
                    readCount++;
                    return "204/20";
                },
                (_, _) => rejectedCount++);

            Assert.False(result.Success);
            Assert.Equal(3, readCount);
            Assert.Equal(3, rejectedCount);
            Assert.Equal(204, result.Level);
            Assert.Equal(20, result.MaxLevel);
        }

        [Fact]
        public void ReadFirstPlausible_StopsReadingAfterFirstValidAttempt()
        {
            int readCount = 0;

            CharacterLevelParseResult result = CharacterLevelParser.ReadFirstPlausible(
                maxAttempts: 3,
                _ =>
                {
                    readCount++;
                    return "20/20";
                });

            Assert.True(result.Success);
            Assert.Equal(1, readCount);
        }

        [Fact]
        public void ReadFirstPlausible_RequiresPositiveAttemptCount()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CharacterLevelParser.ReadFirstPlausible(0, _ => "20/20"));
        }
    }
}
