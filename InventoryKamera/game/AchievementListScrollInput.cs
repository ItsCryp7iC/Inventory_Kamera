using System;
using System.Threading;

namespace InventoryKamera.game
{
    /// <summary>
    /// Sends one bounded batch of standard wheel notches over the right-side Achievement list.
    /// Coordinates are relative to the active Genshin client and deliberately avoid the left
    /// category scrollbar.
    /// </summary>
    internal sealed class AchievementWindowsListScrollInput : IAchievementListScrollInput
    {
        internal const double ScrollTargetX = 0.75;
        internal const double ScrollTargetY = 0.50;
        internal const double MinimumRightPanelX = 0.42;
        internal const double MaximumRightPanelX = 0.92;
        internal const double MinimumRightPanelY = 0.20;
        internal const double MaximumRightPanelY = 0.85;

        private readonly IAchievementPointerInput pointerInput;
        private readonly IGameForegroundActivator foregroundActivator;
        private readonly double targetX;
        private readonly double targetY;
        private readonly Func<bool> inputAllowed;

        internal AchievementWindowsListScrollInput(
            IAchievementPointerInput pointerInput,
            IGameForegroundActivator foregroundActivator,
            double targetX = ScrollTargetX,
            double targetY = ScrollTargetY,
            Func<bool> inputAllowed = null)
        {
            this.pointerInput = pointerInput ?? throw new ArgumentNullException(nameof(pointerInput));
            this.foregroundActivator = foregroundActivator ?? throw new ArgumentNullException(nameof(foregroundActivator));
            if (targetX < MinimumRightPanelX || targetX > MaximumRightPanelX)
                throw new ArgumentOutOfRangeException(nameof(targetX),
                    "The scroll target must remain inside the right-side Achievement panel.");
            if (targetY < MinimumRightPanelY || targetY > MaximumRightPanelY)
                throw new ArgumentOutOfRangeException(nameof(targetY),
                    "The scroll target must remain inside the Achievement-list viewport.");
            this.targetX = targetX;
            this.targetY = targetY;
            this.inputAllowed = inputAllowed ?? (() => true);
        }

        public AchievementListScrollCommandResult Scroll(
            AchievementListScrollRequest request,
            CancellationToken cancellationToken)
        {
            int wheelEventsSent = 0;
            try
            {
                if (request == null) throw new ArgumentNullException(nameof(request));
                cancellationToken.ThrowIfCancellationRequested();
                if (!inputAllowed())
                    return Failure("Achievement-list input is blocked while an interactive dialog is open.");
                if (!foregroundActivator.IsForeground)
                {
                    foregroundActivator.Activate();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (!foregroundActivator.IsForeground)
                    return Failure("Genshin was not the verified foreground target; no wheel input was sent.");

                pointerInput.MoveNormalized(targetX, targetY, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                for (int notch = 0; notch < request.NotchCount; notch++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pointerInput.ScrollWheel(request.WheelDeltaPerNotch, cancellationToken);
                    wheelEventsSent++;
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new AchievementListScrollCommandResult(
                    succeeded: true,
                    foregroundVerified: true,
                    failureReason: null,
                    diagnostics: $"target=({targetX:0.000},{targetY:0.000}); " +
                        $"logicalNotches={request.NotchCount}; deltaPerNotch={request.WheelDeltaPerNotch}; " +
                        $"logicalDelta={request.LogicalWheelDelta}; provisionalTargetPixels={request.ProvisionalTargetDisplacementPixels}; " +
                        "actualDisplacement=measured-from-post-scroll-frame",
                    wheelEventsSent: wheelEventsSent);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var cancelled = new OperationCanceledException(
                    "Achievement-list scrolling was cancelled.", cancellationToken);
                cancelled.Data["AchievementWheelEventsSent"] = wheelEventsSent;
                throw cancelled;
            }
            catch (Exception ex)
            {
                string reason = $"Achievement-list scrolling failed: {ex.GetType().Name}: {ex.Message}";
                return new AchievementListScrollCommandResult(
                    succeeded: false,
                    foregroundVerified: foregroundActivator.IsForeground,
                    failureReason: reason,
                    diagnostics: $"{reason}; wheelEventsSent={wheelEventsSent}",
                    wheelEventsSent: wheelEventsSent);
            }
        }

        private static AchievementListScrollCommandResult Failure(string reason) => new(
            succeeded: false,
            foregroundVerified: false,
            failureReason: reason,
            diagnostics: reason);
    }
}
