using InventoryKamera.game;
using System;
using System.Drawing;
using System.Threading;

namespace InventoryKamera
{
    /// <summary>
    /// Passive readiness gate for the standalone Achievement scanner. It sends no input and waits
    /// until a screenshot positively identifies the idle Keyboard/Mouse Achievement screen.
    /// </summary>
    internal sealed class AchievementScreenReadinessWaiter
    {
        private readonly IGameScreenCapture screenCapture;
        private readonly IAchievementScreenDetector screenDetector;
        private readonly Action<int, CancellationToken> wait;
        private readonly int pollMilliseconds;

        internal AchievementScreenReadinessWaiter(
            IGameScreenCapture screenCapture,
            IAchievementScreenDetector screenDetector,
            Action<int, CancellationToken> wait = null,
            int pollMilliseconds = 500)
        {
            this.screenCapture = screenCapture ?? throw new ArgumentNullException(nameof(screenCapture));
            this.screenDetector = screenDetector ?? throw new ArgumentNullException(nameof(screenDetector));
            this.wait = wait ?? WaitWithCancellation;
            this.pollMilliseconds = pollMilliseconds > 0
                ? pollMilliseconds
                : throw new ArgumentOutOfRangeException(nameof(pollMilliseconds));
        }

        internal bool WaitUntilIdle(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using Bitmap screenshot = screenCapture.CaptureWindow();
                cancellationToken.ThrowIfCancellationRequested();
                if (screenDetector.Detect(screenshot).IsKeyboardMouseIdle)
                    return true;
                wait(pollMilliseconds, cancellationToken);
            }
            return false;
        }

        private static void WaitWithCancellation(int milliseconds, CancellationToken cancellationToken)
        {
            if (cancellationToken.WaitHandle.WaitOne(milliseconds))
                cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
