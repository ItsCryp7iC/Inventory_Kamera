using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Tesseract;
using Xunit;

namespace InventoryKamera.Tests
{
    public class InventoryScreenDetectorCropTests
    {
        [Fact]
        public void Detect_LongPersistedTabLabelUsesFullInventoryTitleRegion()
        {
            var ocr = new RecordingOcrService("Character Development Items");
            var detector = new InventoryScreenDetector(ocr, new PassThroughImagePreprocessor());
            using var screenshot = new Bitmap(1920, 1080);

            InventoryScreenDetection result = detector.Detect(screenshot);

            Assert.True(result.IsInventoryOpen, result.RawText);
            Assert.Equal("Character Development Items", result.TabName);
            Assert.Equal(1152, ocr.LastAnalyzedWidth); // 20% of 1920, enlarged 3x for OCR.
        }

        private sealed class RecordingOcrService : IOcrService
        {
            private readonly string result;

            public RecordingOcrService(string result) => this.result = result;

            public int LastAnalyzedWidth { get; private set; }

            public void Restart() { }

            public string AnalyzeText(
                Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false)
            {
                LastAnalyzedWidth = bitmap.Width;
                return result;
            }

            public (string Text, float Confidence) AnalyzeTextWithConfidence(
                Bitmap bitmap,
                PageSegMode pageMode = PageSegMode.SingleLine,
                bool numbersOnly = false) => (AnalyzeText(bitmap, pageMode, numbersOnly), 1f);
        }

        private sealed class PassThroughImagePreprocessor : IImagePreprocessor
        {
            public Bitmap ConvertToGrayscale(Bitmap bitmap) => new Bitmap(bitmap);
            public void SetInvert(ref Bitmap bitmap) { }
            public void SetThreshold(int threshold, ref Bitmap bitmap) { }
            public void SetContrast(double contrast, ref Bitmap bitmap) { }
            public void FilterColors(ref Bitmap bitmap, IntRange red, IntRange green, IntRange blue) { }
            public (double R, double G, double B) AverageColor(Bitmap bitmap) => (0, 0, 0);
            public Bitmap EdgeDetectKirsch(Bitmap bitmap) => new Bitmap(bitmap);
            public List<Rectangle> FindBlobRectangles(
                Bitmap binary,
                int minWidth,
                int maxWidth,
                int minHeight,
                int maxHeight) => new List<Rectangle>();
        }
    }

    public class InventoryTabPathPlannerTests
    {
        private readonly InventoryTabPathPlanner planner = new InventoryTabPathPlanner();

        [Fact]
        public void Plan_AlreadyAtTargetRequiresNoMovement()
        {
            InventoryTabPlan plan = planner.Plan(0, 0, InventoryTabRecognition.Names.Length);

            Assert.True(plan.Success);
            Assert.Equal(0, plan.Steps);
        }

        [Fact]
        public void Plan_CharacterDevelopmentItemsToWeaponsUsesTwoPreviousSteps()
        {
            InventoryTabPlan plan = planner.Plan(2, 0, InventoryTabRecognition.Names.Length);

            Assert.True(plan.Success);
            Assert.Equal(InventoryTabDirection.Previous, plan.Direction);
            Assert.Equal(2, plan.Steps);
        }

        [Fact]
        public void Plan_WrapsFromFurnishingsToWeaponsInOneNextStep()
        {
            InventoryTabPlan plan = planner.Plan(8, 0, InventoryTabRecognition.Names.Length);

            Assert.True(plan.Success);
            Assert.Equal(InventoryTabDirection.Next, plan.Direction);
            Assert.Equal(1, plan.Steps);
        }

        [Theory]
        [InlineData(-1, 0, 9)]
        [InlineData(0, -1, 9)]
        [InlineData(0, 0, 0)]
        public void Plan_InvalidStateFailsSafely(int current, int target, int count)
        {
            InventoryTabPlan plan = planner.Plan(current, target, count);

            Assert.False(plan.Success);
            Assert.Equal(0, plan.Steps);
        }
    }

    public class InventoryTabNavigatorTests
    {
        [Fact]
        public void NormalizeToWeapons_AlreadyOnWeaponsDoesNotSendTabInput()
        {
            var events = new List<string>();
            InventoryTabNavigator navigator = CreateNavigator(events, Detection("Weapons"));

            using InventoryTabNavigationResult result = navigator.NormalizeToWeapons(NoWaitTiming());

            Assert.True(result.Success, result.Message);
            Assert.Equal("Weapons", result.CurrentTab);
            Assert.DoesNotContain(events, IsTabPress);
            Assert.Equal(1, events.Count(e => e == "Capture"));
        }

        [Fact]
        public void NormalizeToWeapons_FromCharacterDevelopmentItemsUsesPreviousTabsAndVerifiesWeapons()
        {
            var events = new List<string>();
            InventoryTabNavigator navigator = CreateNavigator(
                events,
                Detection("Character Development Items"),
                Detection("Weapons"));

            using InventoryTabNavigationResult result = navigator.NormalizeToWeapons(NoWaitTiming());

            Assert.True(result.Success, result.Message);
            Assert.Equal(2, events.Count(e => e == "Button:PreviousTab:True"));
            Assert.DoesNotContain("Button:NextTab:True", events);
            Assert.Equal(2, events.Count(e => e == "Capture"));
            Assert.True(events.LastIndexOf("Detect:Weapons") > events.LastIndexOf("Button:PreviousTab:True"));
        }

        [Fact]
        public void NormalizeToWeapons_FromAnotherSupportedTabUsesBoundedCircularRoute()
        {
            var events = new List<string>();
            InventoryTabNavigator navigator = CreateNavigator(
                events,
                Detection("Furnishings"),
                Detection("Weapons"));

            using InventoryTabNavigationResult result = navigator.NormalizeToWeapons(NoWaitTiming());

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, events.Count(e => e == "Button:NextTab:True"));
            Assert.DoesNotContain("Button:PreviousTab:True", events);
        }

        [Fact]
        public void NormalizeToWeapons_CanonicalVerificationFailureBacksOutAndFails()
        {
            var events = new List<string>();
            InventoryTabNavigator navigator = CreateNavigator(
                events,
                Detection("Character Development Items"),
                Detection("Artifacts"));

            using InventoryTabNavigationResult result = navigator.NormalizeToWeapons(NoWaitTiming());

            Assert.False(result.Success);
            Assert.Contains("did not reach Weapons", result.Message);
            Assert.NotNull(result.DiagnosticScreenshot);
            Assert.Contains("Button:Back:True", events);
        }

        [Fact]
        public void NormalizeToWeapons_RepeatedWeaponsOnlyEntriesRemainDeterministic()
        {
            var events = new List<string>();
            InventoryTabNavigator navigator = CreateNavigator(
                events,
                Detection("Weapons"),
                Detection("Weapons"));

            using InventoryTabNavigationResult first = navigator.NormalizeToWeapons(NoWaitTiming());
            using InventoryTabNavigationResult second = navigator.NormalizeToWeapons(NoWaitTiming());

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.DoesNotContain(events, IsTabPress);
            Assert.Equal(2, events.Count(e => e == "Capture"));
        }

        [Fact]
        public void NormalizeToWeapons_RepeatedWeaponsAndDevelopmentItemEntriesResetPersistedTabEachTime()
        {
            var events = new List<string>();
            InventoryTabNavigator navigator = CreateNavigator(
                events,
                Detection("Character Development Items"),
                Detection("Weapons"),
                Detection("Character Development Items"),
                Detection("Weapons"));

            using InventoryTabNavigationResult first = navigator.NormalizeToWeapons(NoWaitTiming());
            using InventoryTabNavigationResult second = navigator.NormalizeToWeapons(NoWaitTiming());

            Assert.True(first.Success);
            Assert.True(second.Success);
            Assert.Equal(4, events.Count(e => e == "Button:PreviousTab:True"));
            Assert.Equal(4, events.Count(e => e == "Capture"));
        }

        [Fact]
        public void PlannerRoutesDoNotDependOnEnabledIntermediateScanCategories()
        {
            var planner = new InventoryTabPathPlanner();
            int tabCount = InventoryTabRecognition.Names.Length;

            InventoryTabPlan developmentItems = planner.Plan(0, 2, tabCount);
            InventoryTabPlan materials = planner.Plan(0, 4, tabCount);

            Assert.Equal(InventoryTabDirection.Next, developmentItems.Direction);
            Assert.Equal(2, developmentItems.Steps);
            Assert.Equal(InventoryTabDirection.Next, materials.Direction);
            Assert.Equal(4, materials.Steps);
        }

        private static InventoryTabNavigator CreateNavigator(
            List<string> events,
            params InventoryScreenDetection[] detections)
        {
            var gameNavigator = new GameNavigator(new FakeGameInput(events), _ => { });
            return new InventoryTabNavigator(
                gameNavigator,
                new FakeInventoryScreenDetector(events, detections),
                new FakeCapture(events),
                wait: _ => { },
                detectionAttempts: 1);
        }

        private static InventoryTabNavigationTiming NoWaitTiming() =>
            new InventoryTabNavigationTiming(0, 0, 0, 0, 0);

        private static InventoryScreenDetection Detection(string tabName)
        {
            int index = Array.IndexOf(InventoryTabRecognition.Names, tabName);
            return new InventoryScreenDetection(index, index >= 0 ? tabName : null, tabName);
        }

        private static bool IsTabPress(string value) =>
            value == "Button:PreviousTab:True" || value == "Button:NextTab:True";

        private sealed class FakeInventoryScreenDetector : IInventoryScreenDetector
        {
            private readonly List<string> events;
            private readonly Queue<InventoryScreenDetection> detections;

            public FakeInventoryScreenDetector(
                List<string> events,
                IEnumerable<InventoryScreenDetection> detections)
            {
                this.events = events;
                this.detections = new Queue<InventoryScreenDetection>(detections);
            }

            public InventoryScreenDetection Detect(Bitmap screenshot)
            {
                InventoryScreenDetection detection =
                    detections.Count > 1 ? detections.Dequeue() : detections.Peek();
                events.Add("Detect:" + (detection.TabName ?? "Unknown"));
                return detection;
            }
        }

        private sealed class FakeCapture : IGameScreenCapture
        {
            private readonly List<string> events;

            public FakeCapture(List<string> events) => this.events = events;

            public Bitmap CaptureWindow()
            {
                events.Add("Capture");
                return new Bitmap(10, 10);
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
