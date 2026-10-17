using InventoryKamera.ui;
using System;
using System.Threading;

namespace InventoryKamera.game
{
    internal interface IGameForegroundActivator
    {
        bool IsForeground { get; }
        void Activate();
    }

    internal sealed class NavigationGameForegroundActivator : IGameForegroundActivator
    {
        private readonly IGenshinWindowController gameWindow = new WindowsGenshinWindowController();

        public bool IsForeground => gameWindow.IsForeground;

        public void Activate()
        {
            if (!gameWindow.TryBringToForeground(TimeSpan.FromMilliseconds(750)))
                throw new InvalidOperationException("Genshin could not be brought to the foreground.");
        }
    }

    /// <summary>
    /// Owns one scan's virtual controller. Restoration responsibility comes only from the user's
    /// immutable declaration; visual recognition is advisory and best-effort restoration evidence.
    /// </summary>
    internal sealed class GameInputSession : IRecoveryInputController
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly GameNavigator navigator;
        private readonly IGenshinInputModeDetector detector;
        private readonly IGameInputModeTransition transition;
        private readonly IScanProgressReporter progressReporter;
        private readonly FailureDiagnosticStore diagnosticStore;
        private readonly IGameForegroundActivator foregroundActivator;
        private readonly IScanCompanionOverlay overlay;
        private readonly Action<int> wait;
        private readonly int foregroundSettleMs;
        private int backendReleased;
        private int recoveryActionExecuting;

        internal GameInputSession(
            GameNavigator navigator,
            DeclaredInputMode declaredOriginalInputMode,
            IGenshinInputModeDetector detector,
            IGameInputModeTransition transition,
            IScanProgressReporter progressReporter,
            IScanCompanionOverlay overlay = null,
            FailureDiagnosticStore diagnosticStore = null,
            IGameForegroundActivator foregroundActivator = null,
            Action<int> wait = null,
            int foregroundSettleMs = 300)
        {
            this.navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
            DeclaredOriginalInputMode = declaredOriginalInputMode;
            this.detector = detector ?? throw new ArgumentNullException(nameof(detector));
            this.transition = transition ?? throw new ArgumentNullException(nameof(transition));
            this.progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
            this.overlay = overlay ?? NullScanCompanionOverlay.Instance;
            this.diagnosticStore = diagnosticStore;
            this.foregroundActivator = foregroundActivator;
            this.wait = wait ?? Thread.Sleep;
            this.foregroundSettleMs = Math.Max(0, foregroundSettleMs);
        }

        internal GameNavigator Navigator => navigator;
        internal DeclaredInputMode DeclaredOriginalInputMode { get; }
        internal GenshinInputMode LastObservedInputMode { get; private set; } = GenshinInputMode.Unknown;
        internal bool ViGEmConnected => Volatile.Read(ref backendReleased) == 0 && navigator.IsAvailable;
        internal bool ScanActive { get; private set; }
        internal bool RestorationRequired =>
            DeclaredOriginalInputMode == DeclaredInputMode.KeyboardMouse && ViGEmConnected;
        internal bool RecoveryRequired { get; private set; }
        internal bool HasPendingRecovery => RecoveryRequired;
        public bool ControllerAvailable => ViGEmConnected;
        public bool SessionRecoveryRequired => RecoveryRequired;
        public bool ActionGateAvailable => Volatile.Read(ref recoveryActionExecuting) == 0;

        internal bool Prepare(CancellationToken cancellationToken, out string failureReason)
        {
            overlay.ShowPreparing(DeclaredOriginalInputMode);
            if (!navigator.IsAvailable)
            {
                failureReason = "Inventory Kamera could not create the virtual controller: " + navigator.FailureReason;
                overlay.ShowError(failureReason);
                ReleaseBackend();
                return false;
            }

            ActivateGameAndSettle();
            if (DeclaredOriginalInputMode == DeclaredInputMode.KeyboardMouse)
            {
                progressReporter.SetProgramStatus("Waiting for Controller mode...");
                if (!overlay.WaitForControllerReady(cancellationToken))
                {
                    overlay.ShowCancelling();
                    failureReason = "Scan cancelled before Controller mode was confirmed ready.";
                    return false;
                }

                Logger.Info("User confirmed that Controller mode is ready; starting without visual mode gating.");
            }

            ScanActive = true;
            progressReporter.SetProgramStatus("Scanning...");
            overlay.ShowScanning();
            failureReason = null;
            return true;
        }

        internal bool TryResumeAfterInteractiveDialog(out string failureReason)
        {
            if (!ScanActive || RecoveryRequired || !ViGEmConnected)
            {
                failureReason = null;
                return true;
            }

            try
            {
                ActivateGameAndSettle();
                overlay.ShowScanning();
                failureReason = null;
                return true;
            }
            catch (Exception ex)
            {
                failureReason = $"Genshin could not be re-foregrounded after the correction dialog: {ex.Message}";
                progressReporter.AddError(failureReason);
                return false;
            }
        }

        internal bool RestoreAndRelease(out string failureReason)
        {
            ScanActive = false;
            if (Volatile.Read(ref backendReleased) != 0)
            {
                failureReason = null;
                return true;
            }

            if (DeclaredOriginalInputMode == DeclaredInputMode.Controller)
            {
                ReleaseBackend();
                failureReason = null;
                return true;
            }

            overlay.ShowRestoringKeyboard();
            progressReporter.SetProgramStatus("Restoring Keyboard/Mouse...");
            if (TryAutomaticKeyboardRestoration(out failureReason)) return true;

            string automaticFailure = failureReason ??
                "Keyboard/Mouse was not confirmed after automatic restoration.";
            SaveFailure("keyboard_restore_failed", automaticFailure);
            Logger.Error(automaticFailure);
            progressReporter.AddError(automaticFailure);
            RecoveryRequired = true;
            overlay.ShowRecovery(this, automaticFailure);
            return false;
        }

        public bool TryRetryAutomaticRestoration(out string failureReason)
        {
            if (!RecoveryRequired)
            {
                failureReason = null;
                return true;
            }
            if (!TryBeginRecoveryAction(out failureReason)) return false;
            try
            {
                progressReporter.SetProgramStatus("Retrying Keyboard/Mouse restoration...");
                bool restored = TryAutomaticKeyboardRestoration(out failureReason);
                if (!restored)
                {
                    RecoveryRequired = true;
                    SaveFailure("keyboard_restore_failed", failureReason);
                    progressReporter.AddError(failureReason);
                }
                return restored;
            }
            finally
            {
                EndRecoveryAction();
            }
        }

        public bool TryMove(GameNavigator.MenuDirection direction, out string failureReason) =>
            TryManualRecoveryAction(direction.ToString(), () => navigator.MoveStep(direction), out failureReason);

        public bool TryConfirm(out string failureReason) =>
            TryManualRecoveryAction("Confirm", () => navigator.TapConfirm(), out failureReason);

        public bool TryBack(out string failureReason) =>
            TryManualRecoveryAction("Back", () => navigator.TapBack(), out failureReason);

        public bool TryOpenMenu(out string failureReason) =>
            TryManualRecoveryAction("PaimonMenu", navigator.OpenMenu, out failureReason);

        public bool TryConfirmKeyboardMouse(
            out bool requiresExplicitDisconnect,
            out string failureReason)
        {
            requiresExplicitDisconnect = false;
            if (!RecoveryRequired)
            {
                failureReason = null;
                return true;
            }
            if (!TryBeginRecoveryAction(out failureReason)) return false;
            try
            {
                GenshinInputModeDetection current = TryDetect("manual recovery confirmation", out string detectionFailure);
                if (current?.Mode == GenshinInputMode.KeyboardMouse)
                {
                    FinishKeyboardRestoration();
                    failureReason = null;
                    return true;
                }

                requiresExplicitDisconnect = true;
                failureReason = detectionFailure ??
                    "Inventory Kamera could not verify Keyboard/Mouse automatically.";
                return false;
            }
            finally
            {
                EndRecoveryAction();
            }
        }

        public bool TryDisconnectAfterUserConfirmation(out string failureReason)
        {
            if (!TryBeginRecoveryAction(out failureReason)) return false;
            try
            {
                ReleaseBackend();
                RecoveryRequired = false;
                LastObservedInputMode = GenshinInputMode.KeyboardMouse;
                failureReason = null;
                Logger.Info("User explicitly confirmed Keyboard/Mouse and authorized virtual-controller disconnect.");
                return true;
            }
            catch (Exception ex)
            {
                failureReason = $"The temporary controller could not be disconnected: {ex.Message}";
                return false;
            }
            finally
            {
                EndRecoveryAction();
            }
        }

        private bool TryAutomaticKeyboardRestoration(out string failureReason)
        {
            GenshinInputModeDetection before = TryDetect(
                "pre-restoration input-mode observation", out string observationFailure);
            if (before?.Mode == GenshinInputMode.KeyboardMouse)
            {
                FinishKeyboardRestoration();
                failureReason = null;
                return true;
            }

            bool transitionSucceeded;
            try
            {
                transitionSucceeded = transition.TryRestoreKeyboardMouse(navigator, out failureReason);
            }
            catch (Exception ex)
            {
                transitionSucceeded = false;
                failureReason = $"Keyboard/Mouse restoration threw {ex.GetType().Name}: {ex.Message}";
            }
            if (!transitionSucceeded)
            {
                failureReason ??= observationFailure ?? "Automatic restoration did not complete.";
                return false;
            }

            GenshinInputModeDetection restored = TryDetect(
                "post-restoration input-mode observation", out string verificationFailure);
            if (restored?.Mode != GenshinInputMode.KeyboardMouse)
            {
                failureReason = verificationFailure ??
                    $"Keyboard/Mouse was not positively observed after automatic restoration ({restored}).";
                return false;
            }

            FinishKeyboardRestoration();
            failureReason = null;
            return true;
        }

        private GenshinInputModeDetection TryDetect(string operation, out string failureReason)
        {
            try
            {
                ActivateGameAndSettle();
                GenshinInputModeDetection detection = detector.Detect();
                LastObservedInputMode = detection.Mode;
                Logger.Info("{0}: {1}", operation, detection);
                failureReason = detection.Mode == GenshinInputMode.Unknown
                    ? $"{operation} was inconclusive ({detection})."
                    : null;
                return detection;
            }
            catch (Exception ex)
            {
                LastObservedInputMode = GenshinInputMode.Unknown;
                failureReason = $"{operation} failed: {ex.GetType().Name}: {ex.Message}";
                Logger.Warn(ex, failureReason);
                return null;
            }
        }

        private bool TryManualRecoveryAction(string actionName, Action action, out string failureReason)
        {
            if (!RecoveryRequired)
            {
                failureReason = "Recovery controls are no longer active.";
                return false;
            }
            if (!TryBeginRecoveryAction(out failureReason)) return false;
            try
            {
                bool foregroundBefore = foregroundActivator == null || foregroundActivator.IsForeground;
                bool foregroundRequest = foregroundActivator != null && !foregroundBefore;
                Logger.Info(
                    "RecoveryNavigatorAction action={0}; controllerAvailable={1}; recoveryRequired={2}; " +
                    "gateAcquired=true; genshinForegroundBefore={3}; foregroundRequest={4}",
                    actionName,
                    navigator.IsAvailable,
                    RecoveryRequired,
                    foregroundBefore,
                    foregroundRequest);
                if (foregroundRequest)
                {
                    foregroundActivator.Activate();
                    if (foregroundSettleMs > 0) wait(foregroundSettleMs);
                }
                Logger.Info("RecoveryNavigatorAction action={0}; navigatorActionStarted=true", actionName);
                action();
                Logger.Info("RecoveryNavigatorAction action={0}; navigatorActionCompleted=true", actionName);
                failureReason = null;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "RecoveryNavigatorAction action={0}; exception={1}", actionName, ex.Message);
                failureReason = $"Controller action failed: {ex.Message}";
                return false;
            }
            finally
            {
                EndRecoveryAction();
            }
        }

        private bool TryBeginRecoveryAction(out string failureReason)
        {
            if (Interlocked.CompareExchange(ref recoveryActionExecuting, 1, 0) != 0)
            {
                failureReason = "Another recovery action is still running.";
                return false;
            }
            failureReason = null;
            return true;
        }

        private void EndRecoveryAction() => Interlocked.Exchange(ref recoveryActionExecuting, 0);

        private void FinishKeyboardRestoration()
        {
            LastObservedInputMode = GenshinInputMode.KeyboardMouse;
            ReleaseBackend();
            RecoveryRequired = false;
            try
            {
                transition.ExitKeyboardSettings();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Keyboard/Mouse was restored, but closing Settings did not complete.");
            }
            Logger.Info("Keyboard/Mouse restoration completed before virtual-controller disconnect.");
        }

        private void ReleaseBackend()
        {
            if (Interlocked.Exchange(ref backendReleased, 1) != 0) return;
            navigator.DisposeInputOnly();
        }

        private void ActivateGameAndSettle()
        {
            if (foregroundActivator == null || foregroundActivator.IsForeground) return;
            foregroundActivator.Activate();
            if (foregroundSettleMs > 0) wait(foregroundSettleMs);
        }

        private void SaveFailure(string reason, string details)
        {
            if (diagnosticStore == null) return;
            try
            {
                FailureDiagnosticScope failure = diagnosticStore.CreateFailure("controller_preflight", reason);
                failure.SaveText("details.txt", details ?? string.Empty);
                Logger.Warn("Saved controller input-mode diagnostics to {0}.", failure.DirectoryPath);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not save controller input-mode diagnostics for {0}.", reason);
            }
        }
    }
}
