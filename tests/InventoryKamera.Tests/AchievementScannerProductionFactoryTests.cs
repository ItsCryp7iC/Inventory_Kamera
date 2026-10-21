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
        public void FactoryWiresStandaloneKeyboardMouseBackendAndSessionToken()
        {
            using var ocr = new OcrService(engineCount: 4);
            using var cancellation = new CancellationTokenSource();
            var runtime = new AchievementScanRuntimeContext(
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
