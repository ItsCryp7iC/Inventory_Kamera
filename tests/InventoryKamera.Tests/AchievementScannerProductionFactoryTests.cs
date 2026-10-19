using InventoryKamera.game;
using System;
using System.Drawing;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementScannerProductionFactoryTests
    {
        [Fact]
        public void FactoryWiresCompleteBackendAroundBorrowedNavigatorAndSessionToken()
        {
            using var ocr = new OcrService(engineCount: 4);
            using var navigator = new GameNavigator(new AvailableGameInput(), _ => { });
            using var cancellation = new CancellationTokenSource();
            var runtime = new AchievementScanRuntimeContext(
                navigator,
                ocr,
                new ImageProcessor(),
                new UnusedCapture(),
                new ForegroundActivator(),
                cancellation.Token);
            var provider = new AchievementCatalogProvider();

            AchievementScannerProductionComponents components =
                AchievementScannerProductionFactory.Create(provider, runtime);

            Assert.NotNull(components.SingleScanner);
            Assert.NotNull(components.CatalogScanner);
            Assert.NotNull(components.UpdateScanner);
            Assert.Equal(1559, components.CatalogScanner.Targets.Count);
            Assert.Equal(cancellation.Token, runtime.CancellationToken);
            Assert.Same(navigator, runtime.Navigator);
        }

        private sealed class AvailableGameInput : IGameInput
        {
            public bool IsAvailable => true;
            public string FailureReason => null;
            public void SetLeftStickHorizontal(float value) { }
            public void SetLeftStickVertical(float value) { }
            public void SetButtonState(GameInputButton button, bool isPressed) { }
            public void MoveMouseBy(int x, int y) { }
            public void Dispose() { }
        }

        private sealed class UnusedCapture : IGameScreenCapture
        {
            public Bitmap CaptureWindow() => throw new InvalidOperationException("No live capture in automated tests.");
        }

        private sealed class ForegroundActivator : IGameForegroundActivator
        {
            public bool IsForeground => true;
            public void Activate() { }
        }
    }
}
