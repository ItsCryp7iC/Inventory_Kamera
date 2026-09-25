using InventoryKamera.game;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public class PaimonMenuNavigatorTests
    {
        [Fact]
        public void OpenInventory_CapturesAfterMoveAndVerifiesDestinationBeforeSuccess()
        {
            var events = new List<string>();
            PaimonMenuTile start = Tile("Shop", 0, 0);
            PaimonMenuTile inventory1 = Tile("Inventory", 130, 0);
            PaimonMenuTile inventory2 = Tile("Inventory", 130, 0);
            var detector = new FakeDetector(events,
                Detection(start, start, inventory1),
                Detection(inventory2, inventory2));
            var capture = new FakeCapture(events);
            var destination = new FakeInventoryScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(events, detector, capture, destination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenInventory(NoWaitTiming());

            Assert.True(result.Success, result.Message);
            Assert.Equal(3, capture.Count); // initial menu, after one move, post-confirm destination
            Assert.Equal(2, detector.Calls);
            Assert.Equal(1, destination.Calls);
            Assert.Equal(1, events.Count(e => e == "Button:Confirm:True"));
            Assert.True(events.IndexOf("Detect:Inventory") < events.IndexOf("Button:Confirm:True"));
            Assert.True(events.IndexOf("Button:Confirm:True") < events.IndexOf("VerifyDestination"));
        }

        [Fact]
        public void OpenInventory_PassesPaimonSpecificSingleStepTimingToGameNavigator()
        {
            var events = new List<string>();
            PaimonMenuTile shop = Tile("Shop", 0, 0);
            PaimonMenuTile inventoryBeforeMove = Tile("Inventory", 130, 0);
            PaimonMenuTile inventoryAfterMove = Tile("Inventory", 130, 0);
            var detector = new FakeDetector(events,
                Detection(shop, shop, inventoryBeforeMove),
                Detection(inventoryAfterMove, inventoryAfterMove));
            var capture = new FakeCapture(events);
            var destination = new FakeInventoryScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                destination,
                navigatorWait: milliseconds => events.Add($"NavigatorWait:{milliseconds}"));
            var timing = new PaimonMenuNavigationTiming(
                0,
                0,
                PaimonMenuNavigationTiming.SingleStepHoldMs,
                PaimonMenuNavigationTiming.SingleStepSettleMs,
                0,
                0,
                0,
                0);

            using PaimonMenuNavigationResult result = menuNavigator.OpenInventory(timing);

            Assert.True(result.Success, result.Message);
            Assert.Equal(80, PaimonMenuNavigationTiming.SingleStepHoldMs);
            Assert.Equal(300, PaimonMenuNavigationTiming.SingleStepSettleMs);
            int movement = events.IndexOf("Horizontal:1");
            Assert.True(movement >= 0);
            Assert.Equal(new[]
            {
                "Horizontal:1",
                "NavigatorWait:80",
                "Horizontal:0",
                "Vertical:0",
                "NavigatorWait:300",
            }, events.Skip(movement).Take(5));
        }

        [Fact]
        public void OpenInventory_ReplansAfterUnexpectedButValidMovement()
        {
            var events = new List<string>();
            PaimonMenuTile start = Tile("Shop", 0, 0);
            PaimonMenuTile expectedRight = Tile("Expected", 130, 0);
            PaimonMenuTile initialTarget = Tile("Inventory", 130, 118);
            PaimonMenuTile unexpected = Tile("Unexpected", 0, 118);
            PaimonMenuTile replannedTarget = Tile("Inventory", 0, 236);
            PaimonMenuTile finalTarget = Tile("Inventory", 0, 236);
            var detector = new FakeDetector(events,
                Detection(start, start, expectedRight, initialTarget),
                Detection(unexpected, unexpected, replannedTarget),
                Detection(finalTarget, finalTarget));
            var capture = new FakeCapture(events);
            var destination = new FakeInventoryScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(events, detector, capture, destination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenInventory(NoWaitTiming());

            Assert.True(result.Success, result.Message);
            Assert.Contains("Horizontal:1", events);
            Assert.Contains("Vertical:-1", events);
            Assert.Equal(3, detector.Calls);
        }

        [Fact]
        public void OpenInventory_UnchangedSelectionFailsAfterBoundedRecaptureWithoutConfirming()
        {
            var events = new List<string>();
            PaimonMenuTile start1 = Tile("Shop", 0, 0);
            PaimonMenuTile target1 = Tile("Inventory", 130, 0);
            PaimonMenuTile start2 = Tile("Shop", 0, 0);
            PaimonMenuTile target2 = Tile("Inventory", 130, 0);
            PaimonMenuTile start3 = Tile("Shop", 0, 0);
            PaimonMenuTile target3 = Tile("Inventory", 130, 0);
            var detector = new FakeDetector(events,
                Detection(start1, start1, target1),
                Detection(start2, start2, target2),
                Detection(start3, start3, target3));
            var capture = new FakeCapture(events);
            var destination = new FakeInventoryScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events, detector, capture, destination, unchangedSelectionRetries: 1);

            using PaimonMenuNavigationResult result = menuNavigator.OpenInventory(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("did not change", result.Message);
            Assert.Equal(3, capture.Count);
            Assert.DoesNotContain("Button:Confirm:True", events);
            Assert.Equal(1, events.Count(e => e == "Horizontal:1"));
        }

        [Fact]
        public void OpenInventory_SelectionCycleFailsSafelyWithoutConfirming()
        {
            var events = new List<string>();
            PaimonMenuTile a1 = Tile("A", 0, 0);
            PaimonMenuTile b1 = Tile("B", 130, 0);
            PaimonMenuTile target1 = Tile("Inventory", 260, 0);
            PaimonMenuTile a2 = Tile("A", 0, 0);
            PaimonMenuTile b2 = Tile("B", 130, 0);
            PaimonMenuTile target2 = Tile("Inventory", 260, 0);
            PaimonMenuTile a3 = Tile("A", 0, 0);
            PaimonMenuTile b3 = Tile("B", 130, 0);
            PaimonMenuTile target3 = Tile("Inventory", 260, 0);
            var detector = new FakeDetector(events,
                Detection(a1, a1, b1, target1),
                Detection(b2, a2, b2, target2),
                Detection(a3, a3, b3, target3));
            var capture = new FakeCapture(events);
            var destination = new FakeInventoryScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(events, detector, capture, destination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenInventory(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("cycle", result.Message);
            Assert.DoesNotContain("Button:Confirm:True", events);
        }

        [Fact]
        public void OpenInventory_FailedDetectionNeverMovesOrConfirms()
        {
            var events = new List<string>();
            var invalid = new PaimonMenuDetection(
                new PaimonMenuTile[0], null, null, null, "Nothing detected.");
            var detector = new FakeDetector(events, invalid, invalid);
            var capture = new FakeCapture(events);
            var destination = new FakeInventoryScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events, detector, capture, destination, detectionAttempts: 2);

            using PaimonMenuNavigationResult result = menuNavigator.OpenInventory(NoWaitTiming());

            Assert.False(result.Success);
            Assert.DoesNotContain("Button:Confirm:True", events);
            Assert.DoesNotContain("Horizontal:1", events);
            Assert.DoesNotContain("Vertical:-1", events);
            Assert.Equal(0, destination.Calls);
        }

        [Fact]
        public void OpenInventory_FailedDestinationVerificationBacksOutAndReportsFailure()
        {
            var events = new List<string>();
            PaimonMenuTile inventory = Tile("Inventory", 0, 0);
            var detector = new FakeDetector(events, Detection(inventory, inventory));
            var capture = new FakeCapture(events);
            var destination = new FakeInventoryScreenDetector(events, succeeds: false);
            PaimonMenuNavigator menuNavigator = CreateNavigator(events, detector, capture, destination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenInventory(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("could not be verified", result.Message);
            Assert.Contains("Button:Confirm:True", events);
            Assert.Contains("Button:Back:True", events); // safe MashBack
            Assert.Equal(1, destination.Calls);
        }

        [Fact]
        public void OpenCharacter_RetriesDestinationVerificationAndAcceptsLaterSuccess()
        {
            var events = new List<string>();
            PaimonMenuTile character = Tile("Character", 0, 0);
            var detector = new FakeDetector(events, Detection(character, character));
            var capture = new FakeCapture(events);
            var destination = new FakeCharacterScreenDetector(events, false, true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                new FakeInventoryScreenDetector(events, succeeds: true),
                destination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(NoWaitTiming());

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, destination.Calls);
            Assert.Equal(3, capture.Count); // menu detection plus two destination attempts
            Assert.Equal(1, events.Count(e => e == "Button:Confirm:True"));
        }

        [Fact]
        public void OpenCharacter_SelectedTargetAllowsConfirmAndRequiresDestinationVerification()
        {
            var events = new List<string>();
            PaimonMenuTile character = Tile("Character", 0, 0);
            var detector = new FakeDetector(events, Detection(character, character));
            var capture = new FakeCapture(events);
            var inventoryDestination = new FakeInventoryScreenDetector(events, succeeds: true);
            var characterDestination = new FakeCharacterScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events, detector, capture, inventoryDestination, characterDestination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(NoWaitTiming());

            Assert.True(result.Success, result.Message);
            Assert.Contains("Button:Confirm:True", events);
            Assert.Equal(1, characterDestination.Calls);
            Assert.Equal(0, inventoryDestination.Calls);
            Assert.True(events.IndexOf("Detect:Character") < events.IndexOf("Button:Confirm:True"));
            Assert.True(events.IndexOf("Button:Confirm:True") < events.IndexOf("VerifyCharacterDestination"));
        }

        [Fact]
        public void OpenCharacter_TargetNotSelectedNeverConfirmsWithoutSafePath()
        {
            var events = new List<string>();
            PaimonMenuTile shop = Tile("Shop", 0, 0);
            PaimonMenuTile character = Tile("Character", 130, 118);
            var detector = new FakeDetector(events, Detection(shop, shop, character));
            var capture = new FakeCapture(events);
            var destination = new FakeCharacterScreenDetector(events, succeeds: true);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                new FakeInventoryScreenDetector(events, succeeds: true),
                destination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(NoWaitTiming());

            Assert.False(result.Success);
            Assert.DoesNotContain("Button:Confirm:True", events);
            Assert.Equal(0, destination.Calls);
        }

        [Fact]
        public void OpenCharacter_MissingTargetFailsSafelyWithoutMovementOrConfirm()
        {
            var events = new List<string>();
            PaimonMenuTile shop = Tile("Shop", 0, 0);
            var detector = new FakeDetector(events, Detection(shop, shop));
            var capture = new FakeCapture(events);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                new FakeInventoryScreenDetector(events, succeeds: true),
                new FakeCharacterScreenDetector(events, succeeds: true));

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("Character was not confidently detected", result.Message);
            Assert.DoesNotContain("Horizontal:1", events);
            Assert.DoesNotContain("Vertical:-1", events);
            Assert.DoesNotContain("Button:Confirm:True", events);
        }

        [Fact]
        public void OpenCharacter_UnchangedSelectionFailsAfterBoundedRecapture()
        {
            var events = new List<string>();
            PaimonMenuTile shop1 = Tile("Shop", 0, 0);
            PaimonMenuTile character1 = Tile("Character", 130, 0);
            PaimonMenuTile shop2 = Tile("Shop", 0, 0);
            PaimonMenuTile character2 = Tile("Character", 130, 0);
            PaimonMenuTile shop3 = Tile("Shop", 0, 0);
            PaimonMenuTile character3 = Tile("Character", 130, 0);
            var detector = new FakeDetector(events,
                Detection(shop1, shop1, character1),
                Detection(shop2, shop2, character2),
                Detection(shop3, shop3, character3));
            var capture = new FakeCapture(events);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                new FakeInventoryScreenDetector(events, succeeds: true),
                new FakeCharacterScreenDetector(events, succeeds: true),
                unchangedSelectionRetries: 1);

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("did not change", result.Message);
            Assert.Equal(3, capture.Count);
            Assert.Equal(1, events.Count(e => e == "Horizontal:1"));
            Assert.DoesNotContain("Button:Confirm:True", events);
        }

        [Fact]
        public void OpenCharacter_SelectionCycleFailsSafely()
        {
            var events = new List<string>();
            PaimonMenuTile a1 = Tile("A", 0, 0);
            PaimonMenuTile b1 = Tile("B", 130, 0);
            PaimonMenuTile character1 = Tile("Character", 260, 0);
            PaimonMenuTile a2 = Tile("A", 0, 0);
            PaimonMenuTile b2 = Tile("B", 130, 0);
            PaimonMenuTile character2 = Tile("Character", 260, 0);
            PaimonMenuTile a3 = Tile("A", 0, 0);
            PaimonMenuTile b3 = Tile("B", 130, 0);
            PaimonMenuTile character3 = Tile("Character", 260, 0);
            var detector = new FakeDetector(events,
                Detection(a1, a1, b1, character1),
                Detection(b2, a2, b2, character2),
                Detection(a3, a3, b3, character3));
            var capture = new FakeCapture(events);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                new FakeInventoryScreenDetector(events, succeeds: true),
                new FakeCharacterScreenDetector(events, succeeds: true));

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("cycle", result.Message);
            Assert.DoesNotContain("Button:Confirm:True", events);
        }

        [Fact]
        public void OpenCharacter_RecapturesAndReplansAfterUnexpectedMovement()
        {
            var events = new List<string>();
            PaimonMenuTile start = Tile("Shop", 0, 0);
            PaimonMenuTile expectedRight = Tile("Expected", 130, 0);
            PaimonMenuTile initialTarget = Tile("Character", 130, 118);
            PaimonMenuTile unexpected = Tile("Unexpected", 0, 118);
            PaimonMenuTile replannedTarget = Tile("Character", 0, 236);
            PaimonMenuTile finalTarget = Tile("Character", 0, 236);
            var detector = new FakeDetector(events,
                Detection(start, start, expectedRight, initialTarget),
                Detection(unexpected, unexpected, replannedTarget),
                Detection(finalTarget, finalTarget));
            var capture = new FakeCapture(events);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                new FakeInventoryScreenDetector(events, succeeds: true),
                new FakeCharacterScreenDetector(events, succeeds: true),
                navigatorWait: milliseconds => events.Add($"NavigatorWait:{milliseconds}"));

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(PaimonStepTiming());

            Assert.True(result.Success, result.Message);
            Assert.Contains("Horizontal:1", events);
            Assert.Contains("Vertical:-1", events);
            Assert.Equal(4, capture.Count); // initial, after each of two moves, destination
            Assert.Equal(3, detector.Calls);
            int firstMove = events.IndexOf("Horizontal:1");
            Assert.Equal(new[]
            {
                "Horizontal:1",
                "NavigatorWait:80",
                "Horizontal:0",
                "Vertical:0",
                "NavigatorWait:300",
            }, events.Skip(firstMove).Take(5));
        }

        [Fact]
        public void OpenCharacter_FailedDestinationVerificationReturnsFailureAndBacksOut()
        {
            var events = new List<string>();
            PaimonMenuTile character = Tile("Character", 0, 0);
            var detector = new FakeDetector(events, Detection(character, character));
            var capture = new FakeCapture(events);
            var destination = new FakeCharacterScreenDetector(events, succeeds: false);
            PaimonMenuNavigator menuNavigator = CreateNavigator(
                events,
                detector,
                capture,
                new FakeInventoryScreenDetector(events, succeeds: true),
                destination);

            using PaimonMenuNavigationResult result = menuNavigator.OpenCharacter(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("could not be verified", result.Message);
            Assert.Contains("Button:Confirm:True", events);
            Assert.Contains("Button:Back:True", events);
            Assert.Equal(PaimonMenuNavigator.CharacterDestinationVerificationAttempts, destination.Calls);
        }

        private static PaimonMenuNavigator CreateNavigator(
            List<string> events,
            IPaimonMenuDetector detector,
            IGameScreenCapture capture,
            IInventoryScreenDetector destination,
            ICharacterScreenDetector characterDestination = null,
            int detectionAttempts = 1,
            int unchangedSelectionRetries = 2,
            System.Action<int> navigatorWait = null)
        {
            var input = new FakeGameInput(events);
            var gameNavigator = new GameNavigator(input, navigatorWait ?? (_ => { }));
            return new PaimonMenuNavigator(
                gameNavigator,
                detector,
                capture,
                destination,
                characterDestination,
                wait: _ => { },
                maximumMoves: 8,
                detectionAttempts: detectionAttempts,
                unchangedSelectionRetries: unchangedSelectionRetries);
        }

        private static PaimonMenuNavigationTiming NoWaitTiming() =>
            new PaimonMenuNavigationTiming(0, 0, 0, 0, 0, 0, 0, 0);

        private static PaimonMenuNavigationTiming PaimonStepTiming() =>
            new PaimonMenuNavigationTiming(
                0,
                0,
                PaimonMenuNavigationTiming.SingleStepHoldMs,
                PaimonMenuNavigationTiming.SingleStepSettleMs,
                0,
                0,
                0,
                0);

        private static PaimonMenuTile Tile(string label, int x, int y) =>
            new PaimonMenuTile(label, Rectangle.Empty, new Rectangle(x, y, 120, 108), 1f);

        private static PaimonMenuDetection Detection(
            PaimonMenuTile selected,
            params PaimonMenuTile[] tiles)
        {
            PaimonMenuTile inventory = tiles.FirstOrDefault(t =>
                PaimonMenuDetector.Normalize(t.Label) == "inventory");
            PaimonMenuTile character = tiles.FirstOrDefault(t =>
                PaimonMenuDetector.Normalize(t.Label) == "character");
            return new PaimonMenuDetection(tiles, selected, inventory, character);
        }

        private sealed class FakeDetector : IPaimonMenuDetector
        {
            private readonly Queue<PaimonMenuDetection> detections;
            private readonly List<string> events;
            public int Calls { get; private set; }

            public FakeDetector(List<string> events, params PaimonMenuDetection[] detections)
            {
                this.events = events;
                this.detections = new Queue<PaimonMenuDetection>(detections);
            }

            public PaimonMenuDetection Detect(Bitmap screenshot)
            {
                Calls++;
                PaimonMenuDetection detection = detections.Count > 1 ? detections.Dequeue() : detections.Peek();
                events.Add("Detect:" + (detection.SelectedTile?.Label ?? "None"));
                return detection;
            }
        }

        private sealed class FakeCapture : IGameScreenCapture
        {
            private readonly List<string> events;
            public int Count { get; private set; }

            public FakeCapture(List<string> events) => this.events = events;

            public Bitmap CaptureWindow()
            {
                Count++;
                events.Add("Capture");
                return new Bitmap(10, 10);
            }
        }

        private sealed class FakeInventoryScreenDetector : IInventoryScreenDetector
        {
            private readonly List<string> events;
            private readonly bool succeeds;
            public int Calls { get; private set; }

            public FakeInventoryScreenDetector(List<string> events, bool succeeds)
            {
                this.events = events;
                this.succeeds = succeeds;
            }

            public InventoryScreenDetection Detect(Bitmap screenshot)
            {
                Calls++;
                events.Add("VerifyDestination");
                return succeeds
                    ? new InventoryScreenDetection(0, "Weapons", "Weapons")
                    : new InventoryScreenDetection(-1, null, "unknown");
            }
        }

        private sealed class FakeCharacterScreenDetector : ICharacterScreenDetector
        {
            private readonly List<string> events;
            private readonly Queue<bool> results;
            public int Calls { get; private set; }

            public FakeCharacterScreenDetector(List<string> events, params bool[] succeeds)
            {
                this.events = events;
                results = new Queue<bool>(succeeds);
            }

            public CharacterScreenDetection Detect(Bitmap screenshot)
            {
                Calls++;
                events.Add("VerifyCharacterDestination");
                bool succeeds = results.Count > 1 ? results.Dequeue() : results.Peek();
                return succeeds
                    ? new CharacterScreenDetection(true, "Attributes", 0.99f)
                    : new CharacterScreenDetection(false, "unknown", 0.10f);
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
}
