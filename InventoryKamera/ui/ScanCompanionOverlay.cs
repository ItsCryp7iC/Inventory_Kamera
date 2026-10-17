using InventoryKamera.game;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace InventoryKamera.ui
{
    internal enum ScanCompanionState
    {
        Preparing,
        WaitingForController,
        Scanning,
        Cancelling,
        RestoringKeyboard,
        Recovery,
        Completed,
        Error,
    }

    internal interface IRecoveryInputController
    {
        bool ControllerAvailable { get; }
        bool SessionRecoveryRequired { get; }
        bool ActionGateAvailable { get; }
        bool TryMove(GameNavigator.MenuDirection direction, out string failureReason);
        bool TryConfirm(out string failureReason);
        bool TryBack(out string failureReason);
        bool TryOpenMenu(out string failureReason);
        bool TryRetryAutomaticRestoration(out string failureReason);
        bool TryConfirmKeyboardMouse(out bool requiresExplicitDisconnect, out string failureReason);
        bool TryDisconnectAfterUserConfirmation(out string failureReason);
    }

    internal interface IScanCompanionOverlay : ICaptureOverlayGuard
    {
        ScanCompanionState State { get; }
        void ShowPreparing(DeclaredInputMode declaredMode);
        bool WaitForControllerReady(CancellationToken cancellationToken);
        void ShowScanning();
        void ShowCancelling();
        void ShowRestoringKeyboard();
        void ShowInputRequired();
        void ShowRecovery(IRecoveryInputController controller, string failureReason);
        void ShowCompleted(string message);
        void ShowError(string message);
        IDisposable SuspendForInteraction();
    }

    internal sealed class NullScanCompanionOverlay : IScanCompanionOverlay
    {
        internal static readonly NullScanCompanionOverlay Instance = new();
        private NullScanCompanionOverlay() { }
        public ScanCompanionState State { get; private set; }
        public void ShowPreparing(DeclaredInputMode declaredMode) => State = ScanCompanionState.Preparing;
        public bool WaitForControllerReady(CancellationToken cancellationToken) => !cancellationToken.IsCancellationRequested;
        public void ShowScanning() => State = ScanCompanionState.Scanning;
        public void ShowCancelling() => State = ScanCompanionState.Cancelling;
        public void ShowRestoringKeyboard() => State = ScanCompanionState.RestoringKeyboard;
        public void ShowInputRequired() { }
        public void ShowRecovery(IRecoveryInputController controller, string failureReason) => State = ScanCompanionState.Recovery;
        public void ShowCompleted(string message) => State = ScanCompanionState.Completed;
        public void ShowError(string message) => State = ScanCompanionState.Error;
        public IDisposable SuspendForCapture() => EmptyDisposable.Instance;
        public IDisposable SuspendForInteraction() => EmptyDisposable.Instance;

        private sealed class EmptyDisposable : IDisposable
        {
            internal static readonly EmptyDisposable Instance = new();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// A compact companion card attached to the Genshin client area. Passive states are click-through;
    /// interactive controls receive mouse input without activating this process.
    /// </summary>
    internal sealed class ScanCompanionOverlay : Form, IScanCompanionOverlay
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const int CsDropShadow = 0x00020000;
        private const int WmMouseActivate = 0x0021;
        private const int WmNcHitTest = 0x0084;
        private const int WmHotkey = 0x0312;
        private const int HtTransparent = -1;
        private const int CardWidth = 352;
        private static readonly Color CardColor = Color.FromArgb(27, 30, 36);
        private static readonly Color MutedColor = Color.FromArgb(184, 191, 203);
        private static readonly Color WaitingAccentColor = Color.FromArgb(112, 196, 255);
        private static readonly Color WaitingButtonColor = Color.FromArgb(38, 102, 153);

        private readonly ScanViewModel viewModel;
        private readonly IGenshinWindowController gameWindow;
        private readonly NonActivatingOverlayPresenter presenter;
        private readonly ScanReadyFocusGate readyFocusGate;
        private readonly WaitingReadyHotkeyCoordinator waitingHotkeys;
        private readonly RecoveryHotkeyCoordinator recoveryHotkeys;
        private readonly RecoveryActionGate recoveryActionGate = new();
        private readonly Action beforeRecoveryHotkeys;
        private readonly Action onScanning;
        private readonly Font standardStateFont;
        private readonly Font waitingStateFont;
        private readonly Font standardMessageFont;
        private readonly Font waitingInstructionFont;
        private readonly Font standardButtonFont;
        private readonly Font waitingButtonFont;
        private readonly ManualResetEventSlim ready = new(false);
        private readonly Panel headerPanel;
        private readonly Label stateLabel;
        private readonly Label messageLabel;
        private readonly Label phaseLabel;
        private readonly Label progressLabel;
        private readonly Label errorsLabel;
        private readonly Label shortcutsLabel;
        private readonly OverlayActionButton primaryButton;
        private readonly FlowLayoutPanel recoveryDirections;
        private readonly FlowLayoutPanel recoveryActions;
        private readonly FlowLayoutPanel confirmationActions;
        private readonly System.Collections.Generic.List<(string Name, OverlayActionButton Button)> recoveryButtons = new();
        private readonly OverlayActionButton retryButton;
        private readonly OverlayActionButton manualConfirmButton;
        private readonly System.Windows.Forms.Timer trackingTimer;
        private readonly System.Windows.Forms.Timer completionTimer;
        private IRecoveryInputController recoveryController;
        private OverlayRelativePosition? relativePosition;
        private Rectangle lastGameBounds;
        private OverlayInteractionMode interactionMode = OverlayInteractionMode.Passive;
        private Point dragCursorOrigin;
        private Point dragWindowOrigin;
        private bool dragging;
        private bool desiredVisible;
        private int suspendCount;
        private bool restoreAfterSuspension;
        private int readyCommandExecuting;

        internal ScanCompanionOverlay(
            ScanViewModel viewModel,
            IGenshinWindowController gameWindow = null,
            IOverlayWindowNativeApi overlayNativeApi = null,
            IGlobalHotkeyRegistrar hotkeyRegistrar = null,
            Action beforeRecoveryHotkeys = null,
            Action onScanning = null)
        {
            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            this.gameWindow = gameWindow ?? new WindowsGenshinWindowController();
            presenter = new NonActivatingOverlayPresenter(overlayNativeApi);
            readyFocusGate = new ScanReadyFocusGate(this.gameWindow);
            waitingHotkeys = new WaitingReadyHotkeyCoordinator(hotkeyRegistrar);
            recoveryHotkeys = new RecoveryHotkeyCoordinator(hotkeyRegistrar);
            this.beforeRecoveryHotkeys = beforeRecoveryHotkeys;
            this.onScanning = onScanning;

            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = CardColor;
            ClientSize = new Size(CardWidth, 140);
            ControlBox = false;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9F);
            standardStateFont = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            waitingStateFont = new Font(Font.FontFamily, 12F, FontStyle.Bold);
            standardMessageFont = new Font(Font.FontFamily, 9F);
            waitingInstructionFont = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            standardButtonFont = new Font(Font.FontFamily, 9F);
            waitingButtonFont = new Font(Font.FontFamily, 9.5F, FontStyle.Bold);
            ForeColor = Color.WhiteSmoke;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Opacity = 0.96;
            Padding = new Padding(1);
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;

            headerPanel = new Panel
            {
                BackColor = Color.FromArgb(35, 39, 47),
                Cursor = Cursors.SizeAll,
                Location = new Point(1, 1),
                Size = new Size(CardWidth - 2, 34),
            };
            var icon = new PictureBox
            {
                Image = LoadApplicationIcon(),
                Location = new Point(11, 8),
                Size = new Size(18, 18),
                SizeMode = PictureBoxSizeMode.Zoom,
            };
            var title = new Label
            {
                AutoSize = false,
                Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
                Location = new Point(36, 7),
                Size = new Size(270, 22),
                Text = "Inventory Kamera",
                TextAlign = ContentAlignment.MiddleLeft,
            };
            headerPanel.Controls.Add(icon);
            headerPanel.Controls.Add(title);

            stateLabel = new Label
            {
                AutoSize = false,
                Font = standardStateFont,
                Location = new Point(14, 45),
                Size = new Size(324, 26),
            };
            messageLabel = new Label
            {
                AutoSize = false,
                ForeColor = MutedColor,
                Font = standardMessageFont,
                Location = new Point(14, 75),
                Size = new Size(324, 58),
            };
            phaseLabel = new Label
            {
                AutoSize = false,
                ForeColor = MutedColor,
                Location = new Point(14, 100),
                Size = new Size(324, 22),
            };
            progressLabel = new Label
            {
                AutoSize = false,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                Location = new Point(14, 123),
                Size = new Size(160, 24),
            };
            errorsLabel = new Label
            {
                AutoSize = false,
                ForeColor = MutedColor,
                Location = new Point(190, 124),
                Size = new Size(148, 22),
                TextAlign = ContentAlignment.TopRight,
            };
            primaryButton = CreateActionButton("Ready to Scan", 14, 166, 324);
            primaryButton.Font = standardButtonFont;
            primaryButton.Click += PrimaryButton_Click;

            recoveryDirections = CreateFlowPanel(162, 68);
            AddRecoveryButton(recoveryDirections, "↑ Up", () => MoveRecovery(GameNavigator.MenuDirection.Up), 72);
            AddRecoveryButton(recoveryDirections, "↓ Down", () => MoveRecovery(GameNavigator.MenuDirection.Down), 72);
            AddRecoveryButton(recoveryDirections, "← Left", () => MoveRecovery(GameNavigator.MenuDirection.Left), 72);
            AddRecoveryButton(recoveryDirections, "→ Right", () => MoveRecovery(GameNavigator.MenuDirection.Right), 72);

            recoveryActions = CreateFlowPanel(232, 72);
            AddRecoveryButton(recoveryActions, "Confirm", Confirm, 92);
            AddRecoveryButton(recoveryActions, "Back", Back, 70);
            AddRecoveryButton(recoveryActions, "Paimon", OpenMenu, 92);
            retryButton = AddRecoveryButton(
                recoveryActions, "Retry automatic restoration", RetryAutomaticRestoration, 194);
            manualConfirmButton = AddRecoveryButton(
                recoveryActions, "I've switched to Keyboard/Mouse", ConfirmKeyboardMouse, 244);

            shortcutsLabel = new Label
            {
                AutoSize = false,
                ForeColor = MutedColor,
                Font = new Font(Font.FontFamily, 8F),
                Location = new Point(14, 309),
                Size = new Size(324, 43),
                Text = "────────────────────────\n" +
                    "↑ ↓ ← →  Navigate\n" +
                    "Enter       Confirm\n" +
                    "Backspace   Back\n" +
                    "P  Paimon Menu     R  Retry",
                Visible = false,
            };

            confirmationActions = CreateFlowPanel(272, 38);
            AddRecoveryButton(confirmationActions, "Yes, disconnect", DisconnectAfterConfirmation, 146);
            AddRecoveryButton(confirmationActions, "Go back", GoBackToRecovery, 90);

            Controls.Add(headerPanel);
            Controls.Add(stateLabel);
            Controls.Add(messageLabel);
            Controls.Add(phaseLabel);
            Controls.Add(progressLabel);
            Controls.Add(errorsLabel);
            Controls.Add(primaryButton);
            Controls.Add(recoveryDirections);
            Controls.Add(recoveryActions);
            Controls.Add(shortcutsLabel);
            Controls.Add(confirmationActions);

            foreach (Control dragSurface in new Control[] { headerPanel, icon, title })
            {
                dragSurface.MouseDown += HeaderMouseDown;
                dragSurface.MouseMove += HeaderMouseMove;
                dragSurface.MouseUp += HeaderMouseUp;
            }

            trackingTimer = new System.Windows.Forms.Timer { Interval = 500 };
            trackingTimer.Tick += (_, _) => TrackGameWindow();
            completionTimer = new System.Windows.Forms.Timer { Interval = 2500 };
            completionTimer.Tick += (_, _) =>
            {
                completionTimer.Stop();
                desiredVisible = false;
                Hide();
            };

            viewModel.CountersChanged += RefreshProgress;
            viewModel.ProgramStatusChanged += RefreshProgress;
            viewModel.ErrorAdded += OnErrorAdded;
            viewModel.ErrorsReset += RefreshProgress;
            UpdateRoundedRegion();
        }

        public ScanCompanionState State { get; private set; }
        internal OverlayInteractionMode InteractionMode => interactionMode;
        internal OverlayRelativePosition? RelativePosition => relativePosition;
        internal bool RecoveryContentFits =>
            BoundsInForm(manualConfirmButton).Bottom <= ClientSize.Height &&
            shortcutsLabel.Bottom <= ClientSize.Height;
        internal bool ManualConfirmationPrecedesShortcuts =>
            BoundsInForm(manualConfirmButton).Bottom < shortcutsLabel.Top;
        internal Rectangle ManualConfirmationBounds => BoundsInForm(manualConfirmButton);
        internal Rectangle ShortcutLegendBounds => shortcutsLabel.Bounds;
        internal string ReadyButtonText => primaryButton.Text;
        internal string WaitingInstructionText => messageLabel.Text;
        internal Rectangle ReadyButtonBounds => primaryButton.Bounds;
        internal bool WaitingContentFits =>
            messageLabel.Bottom < primaryButton.Top &&
            primaryButton.Bottom < phaseLabel.Top &&
            phaseLabel.Bottom <= ClientSize.Height;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= CsDropShadow;
                parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
                parameters.ExStyle = (int)OverlayWindowBehavior.ApplyInteractionStyle(
                    parameters.ExStyle,
                    interactionMode);
                return parameters;
            }
        }

        internal void ShowAttachedToGame()
        {
            desiredVisible = true;
            PositionFromGame(force: true);
            if (!Visible) Show();
            presenter.Show(Handle, Bounds);
            trackingTimer.Start();
        }

        public void ShowPreparing(DeclaredInputMode declaredMode) => Ui(() =>
        {
            SetState(ScanCompanionState.Preparing, "Preparing",
                declaredMode == DeclaredInputMode.KeyboardMouse
                    ? "Connecting the temporary controller for Controller-mode setup."
                    : "Connecting the temporary controller for this scan.");
        });

        public bool WaitForControllerReady(CancellationToken cancellationToken)
        {
            Ui(() =>
            {
                primaryButton.Text = "Ready to Scan (Space)";
                SetState(ScanCompanionState.WaitingForController, "Controller mode required",
                    "Switch Genshin to Controller mode\n\n" +
                    "Settings → Controls\nControl Type → Controller\n\n" +
                    "Press SPACE when ready");
                primaryButton.Enabled = true;
                BeginWaitingHotkey();
            });
            using CancellationTokenRegistration registration = cancellationToken.Register(ready.Set);
            try
            {
                ready.Wait();
                return !cancellationToken.IsCancellationRequested;
            }
            finally
            {
                Ui(() => waitingHotkeys.ExitWaiting());
            }
        }

        public void ShowScanning() => Ui(() =>
        {
            SetState(ScanCompanionState.Scanning, "Scanning", "Press Enter to cancel");
            onScanning?.Invoke();
        });

        public void ShowCancelling() => Ui(() => SetState(
            ScanCompanionState.Cancelling, "Cancelling", "Stopping scan work safely…"));

        public void ShowRestoringKeyboard() => Ui(() => SetState(
            ScanCompanionState.RestoringKeyboard,
            "Restoring Keyboard/Mouse",
            "Attempting one bounded automatic restoration."));

        public void ShowInputRequired() => Ui(() =>
        {
            stateLabel.Text = "Input required";
            messageLabel.Text = "Complete the OCR correction dialog to continue scanning.";
        });

        public void ShowRecovery(IRecoveryInputController controller, string failureReason) => Ui(() =>
        {
            recoveryController = controller ?? throw new ArgumentNullException(nameof(controller));
            SetState(ScanCompanionState.Recovery, "Recovery",
                "Scan stopped while Genshin is in Controller mode. Keep the temporary controller " +
                "connected while you switch back to Keyboard/Mouse.");
            phaseLabel.Text = string.IsNullOrWhiteSpace(failureReason)
                ? string.Empty
                : "Automatic restoration was not confirmed.";
            LayoutRecoveryContent();
            BeginRecoveryHotkeys();
            LogRecoveryState();
        });

        public void ShowCompleted(string message) => Ui(() =>
        {
            SetState(ScanCompanionState.Completed, "Completed", message);
            completionTimer.Stop();
            completionTimer.Start();
        });

        public void ShowError(string message) => Ui(() => SetState(
            ScanCompanionState.Error, "Error", message));

        public IDisposable SuspendForCapture() => SuspendOverlay();

        public IDisposable SuspendForInteraction() => SuspendOverlay();

        private IDisposable SuspendOverlay()
        {
            Ui(() =>
            {
                suspendCount++;
                if (Visible)
                {
                    restoreAfterSuspension = true;
                    Hide();
                }
            });
            return new OverlaySuspension(this);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmMouseActivate)
            {
                message.Result = new IntPtr(OverlayWindowBehavior.MaNoActivate);
                return;
            }
            if (message.Msg == WmHotkey)
            {
                int id = message.WParam.ToInt32();
                if (waitingHotkeys.TryDispatch(id))
                {
                    message.Result = IntPtr.Zero;
                    return;
                }
                bool mapped = recoveryHotkeys.TryGetCommand(id, out RecoveryHotkeyCommand command);
                Logger.Info(
                    "RecoveryHotkeyReceived id=0x{0:X}; mappedAction={1}; callbackEntered={2}",
                    id,
                    mapped ? command.ToString() : "none",
                    mapped);
                if (mapped && recoveryHotkeys.TryDispatch(id))
                {
                    message.Result = IntPtr.Zero;
                    return;
                }
            }

            base.WndProc(ref message);
            if (message.Msg == WmNcHitTest && OverlayWindowBehavior.HitTestIsTransparent(interactionMode))
                message.Result = new IntPtr(HtTransparent);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var outline = new Pen(Color.FromArgb(90, 126, 146, 173));
            using GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), 12);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(outline, path);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (headerPanel != null) headerPanel.Width = ClientSize.Width - 2;
            UpdateRoundedRegion();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (State == ScanCompanionState.Recovery && recoveryActions != null)
                LayoutRecoveryContent();
            else if (State == ScanCompanionState.WaitingForController && primaryButton != null)
                ApplyStateLayout(State);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                trackingTimer?.Stop();
                completionTimer?.Stop();
                waitingHotkeys?.Dispose();
                recoveryHotkeys?.Dispose();
                viewModel.CountersChanged -= RefreshProgress;
                viewModel.ProgramStatusChanged -= RefreshProgress;
                viewModel.ErrorAdded -= OnErrorAdded;
                viewModel.ErrorsReset -= RefreshProgress;
                ready.Dispose();
                trackingTimer?.Dispose();
                completionTimer?.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
            {
                standardStateFont?.Dispose();
                waitingStateFont?.Dispose();
                standardMessageFont?.Dispose();
                waitingInstructionFont?.Dispose();
                standardButtonFont?.Dispose();
                waitingButtonFont?.Dispose();
            }
        }

        private static Image LoadApplicationIcon()
        {
            try
            {
                using Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                return icon?.ToBitmap() ?? SystemIcons.Application.ToBitmap();
            }
            catch
            {
                return SystemIcons.Application.ToBitmap();
            }
        }

        private static OverlayActionButton CreateActionButton(string text, int left, int top, int width) =>
            new()
            {
                Location = new Point(left, top),
                Size = new Size(width, 32),
                Text = text,
                Visible = false,
            };

        private FlowLayoutPanel CreateFlowPanel(int top, int height) => new()
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            Location = new Point(14, top),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = new Size(324, height),
            Visible = false,
            WrapContents = true,
        };

        private OverlayActionButton AddRecoveryButton(
            FlowLayoutPanel panel,
            string text,
            Func<string> action,
            int width)
        {
            var button = new OverlayActionButton { Text = text, Width = width, Height = 29 };
            button.Click += async (_, _) =>
            {
                Logger.Info("RecoveryMouseActionRequested action={0}", text);
                await RunRecoveryAction(text, "Mouse", action);
            };
            panel.Controls.Add(button);
            recoveryButtons.Add((text, button));
            return button;
        }

        private async void PrimaryButton_Click(object sender, EventArgs e)
        {
            if (State == ScanCompanionState.WaitingForController)
            {
                await ExecuteReadyCommand();
                return;
            }

            desiredVisible = false;
            Hide();
        }

        private async Task ExecuteReadyCommand()
        {
            if (State != ScanCompanionState.WaitingForController ||
                Interlocked.CompareExchange(ref readyCommandExecuting, 1, 0) != 0)
                return;

            primaryButton.Enabled = false;
            phaseLabel.Visible = true;
            phaseLabel.Text = "Returning focus to Genshin…";
            try
            {
                bool released = await Task.Run(() => readyFocusGate.TryRelease(ready.Set));
                if (released)
                {
                    waitingHotkeys.ExitWaiting();
                    return;
                }

                phaseLabel.Text = "Could not foreground Genshin. Bring it forward and try Ready again.";
                primaryButton.Enabled = true;
            }
            finally
            {
                Interlocked.Exchange(ref readyCommandExecuting, 0);
            }
        }

        private string MoveRecovery(GameNavigator.MenuDirection direction) =>
            recoveryController.TryMove(direction, out string failure) ? null : failure;

        private string Confirm() => recoveryController.TryConfirm(out string failure) ? null : failure;
        private string Back() => recoveryController.TryBack(out string failure) ? null : failure;
        private string OpenMenu() => recoveryController.TryOpenMenu(out string failure) ? null : failure;

        private async Task RunRecoveryAction(string actionName, string source, Func<string> action)
        {
            bool callbackEntered = recoveryController != null;
            bool gateAcquired = callbackEntered && recoveryActionGate.TryEnter(Environment.TickCount64);
            Logger.Info(
                "RecoveryAction callbackEntered={0}; source={1}; action={2}; gateAcquired={3}",
                callbackEntered,
                source,
                actionName,
                gateAcquired);
            if (!gateAcquired) return;

            SetRecoveryControlsEnabled(false);
            try
            {
                string failure = await Task.Run(action);
                Logger.Info(
                    "RecoveryActionCompleted source={0}; action={1}; succeeded={2}; failure={3}",
                    source,
                    actionName,
                    string.IsNullOrWhiteSpace(failure),
                    string.IsNullOrWhiteSpace(failure) ? "none" : failure);
                if (!string.IsNullOrWhiteSpace(failure)) Ui(() => phaseLabel.Text = failure);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "RecoveryAction exception; source={0}; action={1}", source, actionName);
                Ui(() => phaseLabel.Text = "Recovery action failed. See the Inventory Kamera log.");
            }
            finally
            {
                SetRecoveryControlsEnabled(true);
                recoveryActionGate.Exit(Environment.TickCount64);
            }
        }

        private string RetryAutomaticRestoration()
        {
            if (recoveryController.TryRetryAutomaticRestoration(out string failure))
            {
                ShowCompleted("Keyboard/Mouse restored. Temporary controller disconnected.");
                return null;
            }
            return failure;
        }

        private string ConfirmKeyboardMouse()
        {
            if (recoveryController.TryConfirmKeyboardMouse(out bool requiresExplicitDisconnect, out string failure))
            {
                ShowCompleted("Keyboard/Mouse confirmed. Temporary controller disconnected.");
                return null;
            }
            if (!requiresExplicitDisconnect) return failure;

            Ui(() =>
            {
                recoveryHotkeys.ExitRecovery();
                messageLabel.Text =
                    "Inventory Kamera could not verify Keyboard/Mouse automatically.\n\n" +
                    "Make sure Control Type = Keyboard. Disconnect anyway?";
                messageLabel.Height = ScaleLogical(94);
                recoveryDirections.Visible = false;
                recoveryActions.Visible = false;
                shortcutsLabel.Visible = false;
                confirmationActions.Visible = true;
                confirmationActions.Top = messageLabel.Bottom + ScaleLogical(8);
                ResizeFlowPanelToContent(confirmationActions);
                SetCardHeightFromControls(confirmationActions);
            });
            return null;
        }

        private string DisconnectAfterConfirmation()
        {
            if (!recoveryController.TryDisconnectAfterUserConfirmation(out string failure)) return failure;
            ShowCompleted("Temporary controller disconnected by your confirmation.");
            return null;
        }

        private string GoBackToRecovery()
        {
            Ui(() =>
            {
                messageLabel.Text =
                    "Scan stopped while Genshin is in Controller mode. Keep the temporary controller " +
                    "connected while you switch back to Keyboard/Mouse.";
                messageLabel.Height = ScaleLogical(76);
                confirmationActions.Visible = false;
                recoveryDirections.Visible = true;
                recoveryActions.Visible = true;
                shortcutsLabel.Visible = true;
                LayoutRecoveryContent();
                BeginRecoveryHotkeys();
            });
            return null;
        }

        private void SetState(ScanCompanionState state, string heading, string message)
        {
            completionTimer.Stop();
            if (state != ScanCompanionState.WaitingForController) waitingHotkeys.ExitWaiting();
            if (state != ScanCompanionState.Recovery) recoveryHotkeys.ExitRecovery();
            State = state;
            stateLabel.Text = heading;
            messageLabel.Text = message;
            bool waiting = state == ScanCompanionState.WaitingForController;
            stateLabel.Font = waiting ? waitingStateFont : standardStateFont;
            stateLabel.ForeColor = waiting ? WaitingAccentColor : Color.WhiteSmoke;
            messageLabel.Font = waiting ? waitingInstructionFont : standardMessageFont;
            messageLabel.ForeColor = waiting ? Color.WhiteSmoke : MutedColor;
            primaryButton.Font = waiting ? waitingButtonFont : standardButtonFont;
            primaryButton.BackColor = waiting ? WaitingButtonColor : Color.FromArgb(48, 56, 68);
            messageLabel.Height = ScaleLogical(76);
            phaseLabel.Text = string.Empty;
            primaryButton.Visible = state == ScanCompanionState.WaitingForController || state == ScanCompanionState.Error;
            if (state == ScanCompanionState.Error)
            {
                primaryButton.Text = "Dismiss";
                primaryButton.Enabled = true;
            }

            recoveryDirections.Visible = state == ScanCompanionState.Recovery;
            recoveryActions.Visible = state == ScanCompanionState.Recovery;
            shortcutsLabel.Visible = state == ScanCompanionState.Recovery;
            confirmationActions.Visible = false;
            phaseLabel.Visible = state == ScanCompanionState.Scanning || state == ScanCompanionState.Recovery;
            progressLabel.Visible = state == ScanCompanionState.Scanning;
            errorsLabel.Visible = state == ScanCompanionState.Scanning;
            SetInteractionMode(OverlayInteractionPolicy.ForState(state));
            ApplyStateLayout(state);
            RefreshProgress();
        }

        private void ApplyStateLayout(ScanCompanionState state)
        {
            stateLabel.Top = ScaleLogical(45);
            stateLabel.Height = ScaleLogical(26);
            messageLabel.Top = ScaleLogical(75);
            primaryButton.Height = ScaleLogical(32);
            phaseLabel.Height = ScaleLogical(22);
            int height;
            switch (state)
            {
                case ScanCompanionState.WaitingForController:
                    stateLabel.Height = ScaleLogical(30);
                    messageLabel.Top = stateLabel.Bottom + ScaleLogical(5);
                    messageLabel.Height = TextRenderer.MeasureText(
                        messageLabel.Text,
                        messageLabel.Font,
                        new Size(messageLabel.Width, int.MaxValue),
                        TextFormatFlags.Left | TextFormatFlags.Top |
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height +
                        ScaleLogical(6);
                    primaryButton.Top = messageLabel.Bottom + ScaleLogical(12);
                    primaryButton.Height = ScaleLogical(36);
                    phaseLabel.Top = primaryButton.Bottom + ScaleLogical(7);
                    phaseLabel.Height = ScaleLogical(34);
                    SetCardHeightFromControls(phaseLabel);
                    return;
                case ScanCompanionState.Scanning:
                    messageLabel.Height = ScaleLogical(24);
                    phaseLabel.Top = ScaleLogical(101);
                    progressLabel.Top = ScaleLogical(125);
                    errorsLabel.Top = ScaleLogical(126);
                    height = 158;
                    break;
                case ScanCompanionState.Recovery:
                    LayoutRecoveryContent();
                    return;
                case ScanCompanionState.Error:
                    primaryButton.Top = ScaleLogical(137);
                    height = 181;
                    break;
                default:
                    height = 140;
                    break;
            }
            SetCardHeight(height);
        }

        private void SetCardHeight(int height)
        {
            ClientSize = new Size(ClientSize.Width, ScaleLogical(height));
            PositionFromGame(force: true);
        }

        private int ScaleLogical(int value) =>
            Math.Max(1, (int)Math.Round(value * DeviceDpi / 96d));

        private void LayoutRecoveryContent()
        {
            int gap = ScaleLogical(6);
            messageLabel.Height = ScaleLogical(76);
            phaseLabel.Top = messageLabel.Bottom + ScaleLogical(2);
            recoveryDirections.Top = phaseLabel.Bottom + ScaleLogical(3);
            ResizeFlowPanelToContent(recoveryDirections);
            recoveryActions.Top = recoveryDirections.Bottom + gap;
            ResizeFlowPanelToContent(recoveryActions);

            shortcutsLabel.Top = recoveryActions.Bottom + ScaleLogical(8);
            shortcutsLabel.Height = TextRenderer.MeasureText(
                shortcutsLabel.Text,
                shortcutsLabel.Font,
                new Size(shortcutsLabel.Width, int.MaxValue),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height +
                ScaleLogical(3);

            SetCardHeightFromControls(shortcutsLabel);
            LogRecoveryLayout();
        }

        private void ResizeFlowPanelToContent(FlowLayoutPanel panel)
        {
            panel.PerformLayout();
            int contentBottom = panel.Controls.Cast<Control>()
                .Select(control => control.Bottom + control.Margin.Bottom)
                .DefaultIfEmpty(0)
                .Max();
            panel.Height = Math.Max(ScaleLogical(1), contentBottom + panel.Padding.Bottom);
            panel.PerformLayout();
        }

        private void SetCardHeightFromControls(params Control[] bottomControls)
        {
            int[] bottoms = bottomControls
                .Where(control => control != null)
                .Select(ControlBottomInForm)
                .ToArray();
            int requiredHeight = OverlayContentSizing.RequiredClientHeight(ScaleLogical(14), bottoms);
            ClientSize = new Size(ClientSize.Width, requiredHeight);
            PositionFromGame(force: true);
        }

        private int ControlBottomInForm(Control control)
        {
            int bottom = control.Bottom;
            Control parent = control.Parent;
            while (parent != null && !ReferenceEquals(parent, this))
            {
                bottom += parent.Top;
                parent = parent.Parent;
            }
            return bottom;
        }

        private void SetInteractionMode(OverlayInteractionMode mode)
        {
            if (interactionMode == mode) return;
            interactionMode = mode;
            if (IsHandleCreated)
            {
                presenter.SetInteractionMode(Handle, mode);
                long styles = presenter.GetExtendedStyles(Handle);
                Logger.Info(
                    "OverlayInteractionChanged state={0}; interactionMode={1}; clickThrough={2}; extendedStyles=0x{3:X}",
                    State,
                    mode,
                    OverlayWindowBehavior.IsClickThrough(styles),
                    styles);
            }
        }

        private void BeginRecoveryHotkeys()
        {
            beforeRecoveryHotkeys?.Invoke();
            recoveryHotkeys.EnterRecovery(Handle, DispatchRecoveryHotkey);
            string registrations = string.Join(", ", recoveryHotkeys.RegistrationResults.Select(result =>
                $"{result.Command}(id=0x{result.Id:X},key={result.Key},ok={result.Succeeded},error={result.ErrorCode})"));
            Logger.Info(
                "RecoveryHotkeyRegistration attempted={0}; targetHwnd=0x{1:X}; registeredHotkeys=[{2}]",
                recoveryHotkeys.RegistrationAttempted,
                Handle.ToInt64(),
                registrations);
            if (recoveryHotkeys.RegisteredCount < 8)
                phaseLabel.Text = "Some keyboard shortcuts are unavailable; mouse controls still work.";
        }

        private void BeginWaitingHotkey()
        {
            waitingHotkeys.EnterWaiting(Handle, () => _ = ExecuteReadyCommand());
            Logger.Info(
                "WaitingHotkeyRegistration attempted={0}; targetHwnd=0x{1:X}; key=Space; ok={2}; error={3}",
                waitingHotkeys.RegistrationAttempted,
                Handle.ToInt64(),
                waitingHotkeys.RegistrationSucceeded,
                waitingHotkeys.RegistrationErrorCode);
            if (!waitingHotkeys.RegistrationSucceeded)
            {
                phaseLabel.Visible = true;
                phaseLabel.Text = "Space shortcut unavailable — use Ready to Scan.";
            }
        }

        private void LogRecoveryState()
        {
            long styles = presenter.GetExtendedStyles(Handle);
            Logger.Info(
                "RecoveryEntered overlayState={0}; interactionMode={1}; clickThrough={2}; extendedStyles=0x{3:X}; " +
                "controllerAvailable={4}; sessionRecoveryRequired={5}; actionGateAvailable={6}; " +
                "sessionActionGateAvailable={7}; hotkeyRegistrationAttempted={8}",
                State,
                interactionMode,
                OverlayWindowBehavior.IsClickThrough(styles),
                styles,
                recoveryController?.ControllerAvailable,
                recoveryController?.SessionRecoveryRequired,
                recoveryActionGate.IsAvailable,
                recoveryController?.ActionGateAvailable,
                recoveryHotkeys.RegistrationAttempted);
        }

        private void LogRecoveryLayout()
        {
            string buttons = string.Join(", ", recoveryButtons.Select(item =>
                $"{item.Name}={BoundsInForm(item.Button)}"));
            Logger.Info(
                "RecoveryLayout clientSize={0}; header={1}; title={2}; message={3}; directions={4}; " +
                "actions={5}; retry={6}; manualConfirm={7}; shortcuts={8}; buttons=[{9}]",
                ClientSize,
                headerPanel.Bounds,
                stateLabel.Bounds,
                messageLabel.Bounds,
                recoveryDirections.Bounds,
                recoveryActions.Bounds,
                BoundsInForm(retryButton),
                BoundsInForm(manualConfirmButton),
                shortcutsLabel.Bounds,
                buttons);
        }

        private Rectangle BoundsInForm(Control control)
        {
            Point location = control.Location;
            Control parent = control.Parent;
            while (parent != null && !ReferenceEquals(parent, this))
            {
                location.Offset(parent.Left, parent.Top);
                parent = parent.Parent;
            }
            return new Rectangle(location, control.Size);
        }

        private void DispatchRecoveryHotkey(RecoveryHotkeyCommand command)
        {
            Func<string> action = command switch
            {
                RecoveryHotkeyCommand.Up => () => MoveRecovery(GameNavigator.MenuDirection.Up),
                RecoveryHotkeyCommand.Down => () => MoveRecovery(GameNavigator.MenuDirection.Down),
                RecoveryHotkeyCommand.Left => () => MoveRecovery(GameNavigator.MenuDirection.Left),
                RecoveryHotkeyCommand.Right => () => MoveRecovery(GameNavigator.MenuDirection.Right),
                RecoveryHotkeyCommand.Confirm => Confirm,
                RecoveryHotkeyCommand.Back => Back,
                RecoveryHotkeyCommand.OpenMenu => OpenMenu,
                RecoveryHotkeyCommand.Retry => RetryAutomaticRestoration,
                _ => null,
            };
            if (action != null) _ = RunRecoveryAction(command.ToString(), "Hotkey", action);
        }

        private void RefreshProgress()
        {
            Ui(() =>
            {
                if (State != ScanCompanionState.Scanning) return;
                string status = viewModel.ProgramStatus ?? string.Empty;
                if (status.IndexOf("Weapon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    phaseLabel.Text = "Current phase: Weapons";
                    progressLabel.Text = $"{viewModel.WeaponCount} / {viewModel.WeaponMax?.ToString() ?? "?"}";
                }
                else if (status.IndexOf("Artifact", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    phaseLabel.Text = "Current phase: Artifacts";
                    progressLabel.Text = $"{viewModel.ArtifactCount} / {viewModel.ArtifactMax?.ToString() ?? "?"}";
                }
                else if (status.IndexOf("Character", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    status.IndexOf("Development", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    phaseLabel.Text = "Current phase: Characters";
                    progressLabel.Text = $"{viewModel.CharacterCount} / {viewModel.CharacterMax?.ToString() ?? "?"}";
                }
                else if (status.IndexOf("Development", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    phaseLabel.Text = "Current phase: Dev Items";
                    progressLabel.Text = $"{viewModel.MaterialCount} / ?";
                }
                else if (status.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    phaseLabel.Text = "Current phase: Materials";
                    progressLabel.Text = $"{viewModel.MaterialCount} / ?";
                }
                else
                {
                    phaseLabel.Text = "Current phase: Starting";
                    progressLabel.Text = "0 / ?";
                }
                errorsLabel.Text = $"Errors: {viewModel.ErrorCount}";
            });
        }

        private void OnErrorAdded(string error) => RefreshProgress();

        private void SetRecoveryControlsEnabled(bool enabled) => Ui(() =>
        {
            recoveryDirections.Enabled = enabled;
            recoveryActions.Enabled = enabled;
            confirmationActions.Enabled = enabled;
        });

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (interactionMode != OverlayInteractionMode.Interactive || e.Button != MouseButtons.Left) return;
            dragging = true;
            dragCursorOrigin = Cursor.Position;
            dragWindowOrigin = Location;
        }

        private void HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point cursor = Cursor.Position;
            Point requested = new(
                dragWindowOrigin.X + cursor.X - dragCursorOrigin.X,
                dragWindowOrigin.Y + cursor.Y - dragCursorOrigin.Y);
            Rectangle gameBounds = lastGameBounds;
            if (gameWindow.TryGetClientBounds(out Rectangle currentBounds)) gameBounds = currentBounds;
            Point clamped = OverlayPlacement.Calculate(
                gameBounds,
                Size,
                OverlayPlacement.ToRelative(gameBounds, requested),
                margin: 0);
            presenter.Position(Handle, new Rectangle(clamped, Size));
        }

        private void HeaderMouseUp(object sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (gameWindow.TryGetClientBounds(out Rectangle gameBounds))
            {
                lastGameBounds = gameBounds;
                relativePosition = OverlayPlacement.ToRelative(gameBounds, Location);
                PositionFromGame(force: true);
            }
        }

        private void TrackGameWindow()
        {
            if (!dragging && desiredVisible && suspendCount == 0) PositionFromGame(force: false);
        }

        private void PositionFromGame(bool force)
        {
            if (!gameWindow.TryGetClientBounds(out Rectangle gameBounds)) return;
            if (!force && gameBounds == lastGameBounds) return;
            lastGameBounds = gameBounds;
            Point position = OverlayPlacement.Calculate(gameBounds, Size, relativePosition);
            if (IsHandleCreated)
                presenter.Position(Handle, new Rectangle(position, Size));
            else
                Location = position;
        }

        private void ResumeAfterSuspension()
        {
            Ui(() =>
            {
                suspendCount = Math.Max(0, suspendCount - 1);
                if (suspendCount != 0) return;
                bool restore = restoreAfterSuspension;
                restoreAfterSuspension = false;
                if (!restore || !desiredVisible || IsDisposed) return;
                PositionFromGame(force: true);
                if (!Visible) Show();
                presenter.Show(Handle, Bounds);
            });
        }

        private void UpdateRoundedRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width, Height), 12);
            Region oldRegion = Region;
            Region = new Region(path);
            oldRegion?.Dispose();
            Invalidate();
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void Ui(Action action)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { Invoke((MethodInvoker)(() => action())); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            action();
        }

        private sealed class OverlaySuspension : IDisposable
        {
            private ScanCompanionOverlay owner;
            internal OverlaySuspension(ScanCompanionOverlay owner) => this.owner = owner;
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.ResumeAfterSuspension();
        }
    }

    internal sealed class OverlayActionButton : Button
    {
        internal OverlayActionButton()
        {
            BackColor = Color.FromArgb(48, 56, 68);
            Cursor = Cursors.Hand;
            FlatAppearance.BorderColor = Color.FromArgb(90, 120, 150);
            FlatAppearance.BorderSize = 1;
            FlatAppearance.MouseDownBackColor = Color.FromArgb(38, 102, 153);
            FlatAppearance.MouseOverBackColor = Color.FromArgb(61, 73, 89);
            FlatStyle = FlatStyle.Flat;
            ForeColor = Color.White;
            Margin = new Padding(0, 0, 6, 6);
            TabStop = false;
            UseVisualStyleBackColor = false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 0 || Height <= 0) return;
            Rectangle bounds = ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            using GraphicsPath path = RoundedPath(bounds, 6);
            Region oldRegion = Region;
            Region = new Region(path);
            oldRegion?.Dispose();
        }

        private static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
