using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementScreenReadinessWaiterTests
    {
        [Fact]
        public void WaitUntilIdle_PollsPassivelyUntilIdle()
        {
            var detector = new QueueDetector(
                Detection(AchievementScreenState.Unknown),
                ControllerIdle(),
                Detection(AchievementScreenState.Idle));
            var capture = new RecordingCapture();
            int waits = 0;
            var waiter = new AchievementScreenReadinessWaiter(
                capture,
                detector,
                (_, token) => { waits++; token.ThrowIfCancellationRequested(); },
                pollMilliseconds: 1);

            bool ready = waiter.WaitUntilIdle(CancellationToken.None);

            Assert.True(ready);
            Assert.Equal(3, capture.Count);
            Assert.Equal(3, detector.Count);
            Assert.Equal(2, waits);
        }

        [Fact]
        public void WaitUntilIdle_CancellationStopsWithoutAdditionalCapture()
        {
            using var cancellation = new CancellationTokenSource();
            var detector = new QueueDetector(Detection(AchievementScreenState.Unknown));
            var capture = new RecordingCapture();
            var waiter = new AchievementScreenReadinessWaiter(
                capture,
                detector,
                (_, _) => cancellation.Cancel(),
                pollMilliseconds: 1);

            Assert.False(waiter.WaitUntilIdle(cancellation.Token));
            Assert.Equal(1, capture.Count);
        }

        private static AchievementScreenDetection Detection(AchievementScreenState state) =>
            AchievementScreenDetector.Classify(
                new Size(1920, 1080),
                AchievementTextEvidence.ForTest("Achievements", true),
                state == AchievementScreenState.Idle
                    ? AchievementSearchFieldEvidence.ForTest(
                        idlePlaceholderAccepted: true,
                        rawText: "Search Achievements")
                    : AchievementSearchFieldEvidence.ForTest(),
                AchievementTextEvidence.ForTest("Completed 1", true),
                AchievementMatchCountParser.Parse(string.Empty),
                AchievementPageCounterParser.Parse(string.Empty));

        private static AchievementScreenDetection ControllerIdle() =>
            AchievementScreenDetector.Classify(
                new Size(1920, 1080),
                AchievementTextEvidence.ForTest("Achievements", true),
                AchievementSearchFieldEvidence.ForTest(
                    idleControllerPromptAccepted: true,
                    rawText: "Search"),
                AchievementTextEvidence.ForTest("Completed 1", true),
                AchievementMatchCountParser.Parse(string.Empty),
                AchievementPageCounterParser.Parse(string.Empty));

        private sealed class RecordingCapture : IGameScreenCapture
        {
            internal int Count { get; private set; }
            public Bitmap CaptureWindow()
            {
                Count++;
                return new Bitmap(2, 2);
            }
        }

        private sealed class QueueDetector : IAchievementScreenDetector
        {
            private readonly Queue<AchievementScreenDetection> values;
            internal QueueDetector(params AchievementScreenDetection[] values) =>
                this.values = new Queue<AchievementScreenDetection>(values);
            internal int Count { get; private set; }
            public AchievementScreenDetection Detect(Bitmap screenshot)
            {
                Count++;
                return values.Dequeue();
            }
        }
    }
}
