using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public class InventoryItemCountParserTests
    {
        [Theory]
        [InlineData("2450/2700", 2450, 2700)]
        [InlineData("0/2700", 0, 2700)]
        [InlineData("2700/2700", 2700, 2700)]
        [InlineData("22/1898", 22, 1898)]
        [InlineData(" 123 / 2000 ", 123, 2000)]
        [InlineData("1,234 / 2,700", 1234, 2700)]
        public void Parse_ValidInventoryUiForms_ReturnsCount(
            string text,
            int expectedCount,
            int expectedCapacity)
        {
            InventoryItemCountParseResult result = InventoryItemCountParser.Parse(text);

            Assert.True(result.Success, result.Reason);
            Assert.Equal(expectedCount, result.Count);
            Assert.Equal(expectedCapacity, result.Capacity);
        }

        [Theory]
        [InlineData("24502700")]
        [InlineData("2450 2700")]
        [InlineData("")]
        [InlineData("123/")]
        [InlineData("/2000")]
        [InlineData("count 123 / 2000")]
        [InlineData("123-2000")]
        [InlineData("2000")]
        [InlineData("Quality IL")]
        [InlineData("-1/2000")]
        public void Parse_AmbiguousMalformedOrPartialText_Fails(string text)
        {
            InventoryItemCountParseResult result = InventoryItemCountParser.Parse(text);

            Assert.False(result.Success);
            Assert.NotEqual(2000, result.Count);
        }

        [Fact]
        public void Parse_OverflowFails()
        {
            InventoryItemCountParseResult result =
                InventoryItemCountParser.Parse("99999999999999999999/2000");

            Assert.False(result.Success);
            Assert.Equal(InventoryItemCountParseStatus.Overflow, result.Status);
        }

        [Theory]
        [InlineData("2701/2700", "OutOfRange")]
        [InlineData("2450/0", "OutOfRange")]
        public void Parse_ImpossibleValuesFail(string text, string expectedStatus)
        {
            InventoryItemCountParseResult result = InventoryItemCountParser.Parse(text);

            Assert.False(result.Success);
            Assert.Equal(expectedStatus, result.Status.ToString());
        }

        [Fact]
        public void Reader_MalformedHighConfidenceReadDoesNotBecomeCapacity()
        {
            InventoryItemCountReadResult result = InventoryItemCountReader.Read(
                confidenceThreshold: 40,
                _ => new InventoryItemCountObservation("not a count", 99),
                CancellationToken.None,
                maxAttempts: 1);

            Assert.False(result.Success);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public void Reader_LiveArtifactPairSucceedsImmediatelyWithoutRetry()
        {
            int captures = 0;
            int waits = 0;

            InventoryItemCountReadResult result = InventoryItemCountReader.Read(
                confidenceThreshold: 40,
                _ =>
                {
                    captures++;
                    return new InventoryItemCountObservation("2450/2700", 96);
                },
                CancellationToken.None,
                _ => waits++,
                maxAttempts: 3,
                retryDelayMs: 1);

            Assert.True(result.Success, result.LastParse.Reason);
            Assert.Equal(2450, result.Count);
            Assert.Equal(2700, result.Capacity);
            Assert.Equal(1, captures);
            Assert.Equal(0, waits);
        }

        [Fact]
        public void Reader_FirstMalformedThenFreshValidReadSucceeds()
        {
            var observations = new Queue<InventoryItemCountObservation>(new[]
            {
                new InventoryItemCountObservation("garbled", 95),
                new InventoryItemCountObservation("321/2000", 90),
            });
            int captures = 0;
            int waits = 0;

            InventoryItemCountReadResult result = InventoryItemCountReader.Read(
                confidenceThreshold: 40,
                _ =>
                {
                    captures++;
                    return observations.Dequeue();
                },
                CancellationToken.None,
                _ => waits++,
                maxAttempts: 3,
                retryDelayMs: 1);

            Assert.True(result.Success, result.LastParse.Reason);
            Assert.Equal(321, result.Count);
            Assert.Equal(2, captures);
            Assert.Equal(1, waits);
        }

        [Fact]
        public void Reader_ExhaustionStopsAtBoundAndFailsSafely()
        {
            int captures = 0;
            int waits = 0;

            InventoryItemCountReadResult result = InventoryItemCountReader.Read(
                confidenceThreshold: 40,
                _ =>
                {
                    captures++;
                    return new InventoryItemCountObservation("123/", 99);
                },
                CancellationToken.None,
                _ => waits++,
                maxAttempts: 3,
                retryDelayMs: 1);

            Assert.False(result.Success);
            Assert.Equal(3, captures);
            Assert.Equal(2, waits);
            Assert.Equal(0, result.Count);
        }

        [Fact]
        public void Reader_LowConfidenceStructurallyValidReadStillRetries()
        {
            int captures = 0;
            InventoryItemCountReadResult result = InventoryItemCountReader.Read(
                confidenceThreshold: 40,
                _ =>
                {
                    captures++;
                    return captures == 1
                        ? new InventoryItemCountObservation("123/2000", 20)
                        : new InventoryItemCountObservation("123/2000", 90);
                },
                CancellationToken.None,
                _ => { },
                maxAttempts: 3);

            Assert.True(result.Success);
            Assert.Equal(2, captures);
        }

        [Fact]
        public void Reader_ConflictingCapacitiesRemainUnresolved()
        {
            var observations = new Queue<InventoryItemCountObservation>(new[]
            {
                new InventoryItemCountObservation("100/2000", 20),
                new InventoryItemCountObservation("2450/2700", 96),
                new InventoryItemCountObservation("2450/2700", 96),
            });
            int captures = 0;

            InventoryItemCountReadResult result = InventoryItemCountReader.Read(
                confidenceThreshold: 40,
                _ =>
                {
                    captures++;
                    return observations.Dequeue();
                },
                CancellationToken.None,
                _ => { },
                maxAttempts: 3);

            Assert.False(result.Success);
            Assert.Equal(3, captures);
            Assert.Equal(InventoryItemCountParseStatus.CapacityConflict, result.LastParse.Status);
        }

        [Fact]
        public void Reader_CancellationDuringRetryWaitPreventsAnotherCapture()
        {
            using var cancellation = new CancellationTokenSource();
            int captures = 0;

            Assert.Throws<OperationCanceledException>(() => InventoryItemCountReader.Read(
                confidenceThreshold: 40,
                _ =>
                {
                    captures++;
                    return new InventoryItemCountObservation("garbled", 99);
                },
                cancellation.Token,
                _ => cancellation.Cancel(),
                maxAttempts: 3,
                retryDelayMs: 1));

            Assert.Equal(1, captures);
        }
    }
}
