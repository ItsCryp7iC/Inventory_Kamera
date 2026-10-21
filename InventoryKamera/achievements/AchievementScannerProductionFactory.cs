using InventoryKamera.game;
using System;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// Per-run services for the standalone keyboard/mouse Achievement scanner. This context has no
    /// controller, GameNavigator, or GameInputSession dependency.
    /// </summary>
    internal sealed class AchievementScanRuntimeContext
    {
        internal AchievementScanRuntimeContext(
            IOcrService ocrService,
            IImagePreprocessor imagePreprocessor,
            IGameScreenCapture screenCapture,
            IGameForegroundActivator foregroundActivator,
            CancellationToken cancellationToken)
        {
            OcrService = ocrService ?? throw new ArgumentNullException(nameof(ocrService));
            ImagePreprocessor = imagePreprocessor ?? throw new ArgumentNullException(nameof(imagePreprocessor));
            ScreenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            ForegroundActivator = foregroundActivator ?? throw new ArgumentNullException(nameof(foregroundActivator));
            CancellationToken = cancellationToken;
        }

        internal IOcrService OcrService { get; }
        internal IImagePreprocessor ImagePreprocessor { get; }
        internal IGameScreenCapture ScreenCapture { get; }
        internal IGameForegroundActivator ForegroundActivator { get; }
        internal CancellationToken CancellationToken { get; }
    }

    internal sealed class AchievementScannerProductionComponents
    {
        internal AchievementScannerProductionComponents(
            AchievementSingleScanner singleScanner,
            AchievementCatalogScanner catalogScanner,
            AchievementUpdateScanner updateScanner)
        {
            SingleScanner = singleScanner ?? throw new ArgumentNullException(nameof(singleScanner));
            CatalogScanner = catalogScanner ?? throw new ArgumentNullException(nameof(catalogScanner));
            UpdateScanner = updateScanner ?? throw new ArgumentNullException(nameof(updateScanner));
        }

        internal AchievementSingleScanner SingleScanner { get; }
        internal AchievementCatalogScanner CatalogScanner { get; }
        internal AchievementUpdateScanner UpdateScanner { get; }
    }

    internal static class AchievementScannerProductionFactory
    {
        internal static AchievementScannerProductionComponents Create(
            IAchievementCatalogProvider catalogProvider,
            AchievementScanRuntimeContext runtime)
        {
            if (catalogProvider == null) throw new ArgumentNullException(nameof(catalogProvider));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));

            var screenDetector = new AchievementScreenDetector(
                runtime.OcrService,
                runtime.ImagePreprocessor);
            var searchNavigator = new AchievementKeyboardMouseNavigator(
                new AchievementWindowsPointerInput(),
                new GameTextInput(),
                screenDetector,
                runtime.ScreenCapture,
                runtime.ForegroundActivator);
            var titleReader = new AchievementResultTitleReader(
                runtime.OcrService,
                runtime.ImagePreprocessor);
            var identityVerifier = new AchievementResultIdentityVerifier(
                catalogProvider,
                screenDetector,
                titleReader);
            var completionReader = new AchievementCompletionReader(
                runtime.OcrService,
                runtime.ImagePreprocessor);
            var completionClassifier = new AchievementCompletionClassifier(
                screenDetector,
                completionReader);
            var rewardReader = new AchievementRewardReader(
                runtime.OcrService,
                runtime.ImagePreprocessor);
            var stageResolver = new AchievementStageProgressResolver();
            var singleScanner = new AchievementSingleScanner(
                catalogProvider,
                screenDetector,
                searchNavigator,
                identityVerifier,
                completionClassifier,
                runtime.ScreenCapture,
                rewardReader,
                stageResolver);
            var catalogScanner = new AchievementCatalogScanner(catalogProvider, singleScanner);
            var updateScanner = new AchievementUpdateScanner(catalogProvider, singleScanner);
            return new AchievementScannerProductionComponents(
                singleScanner,
                catalogScanner,
                updateScanner);
        }
    }
}
