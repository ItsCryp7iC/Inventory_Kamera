using InventoryKamera.game;
using InventoryKamera.ui;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class CorrectionDialogServicesTests
    {
        [Fact]
        public void Placement_MainFormOnSecondMonitorStillCentersOnGenshinClient()
        {
            var mainFormBounds = new Rectangle(2200, 100, 900, 700);
            var gameBounds = new Rectangle(100, 50, 1600, 900);

            Point location = CorrectionDialogPlacement.Calculate(
                gameBounds,
                new Size(360, 300),
                new Rectangle(0, 0, 1920, 1040));

            Assert.Equal(new Point(720, 350), location);
            Assert.False(mainFormBounds.Contains(location));
        }

        [Fact]
        public void Positioner_QueriesCurrentGameBoundsForEveryCorrection()
        {
            var game = new MutableGameWindow
            {
                Bounds = new Rectangle(0, 0, 1920, 1080),
            };
            var positioner = new CorrectionDialogPositioner(
                game,
                bounds => new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height));

            Assert.True(positioner.TryCalculate(new Size(400, 300), out Point first));
            game.Bounds = new Rectangle(1920, 100, 2560, 1440);
            Assert.True(positioner.TryCalculate(new Size(400, 300), out Point second));

            Assert.Equal(new Point(760, 390), first);
            Assert.Equal(new Point(3000, 670), second);
        }

        [Fact]
        public void Placement_HandlesNonZeroClientOriginAndAlternateResolution()
        {
            Point location = CorrectionDialogPlacement.Calculate(
                new Rectangle(-1500, 120, 1280, 720),
                new Size(360, 300),
                new Rectangle(-1600, 40, 1600, 860));

            Assert.Equal(new Point(-1040, 330), location);
        }

        [Fact]
        public void Placement_UsesDpiScaledDialogSize()
        {
            Point location = CorrectionDialogPlacement.Calculate(
                new Rectangle(0, 0, 1920, 1080),
                new Size(540, 390),
                new Rectangle(0, 0, 1920, 1040));

            Assert.Equal(new Point(690, 345), location);
        }

        [Fact]
        public void CorrectionForm_UsesManualDpiAwarePositioning()
        {
            Exception failure = null;
            FormStartPosition startPosition = default;
            AutoScaleMode autoScaleMode = default;
            var thread = new Thread(() =>
            {
                try
                {
                    using var image = new Bitmap(2, 2);
                    using var dialog = new OcrCorrectionForm(image, "test", 10, "weapon name");
                    startPosition = dialog.StartPosition;
                    autoScaleMode = dialog.AutoScaleMode;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

            Assert.Null(failure);
            Assert.Equal(FormStartPosition.Manual, startPosition);
            Assert.Equal(AutoScaleMode.Dpi, autoScaleMode);
        }

        [Fact]
        public void CorrectionForeground_DualMonitorTargetsCorrectionHwndAndUsesLifetimeBoundTopMost()
        {
            var mainFormBounds = new Rectangle(2200, 100, 900, 700);
            var gameBounds = new Rectangle(100, 50, 1600, 900);
            Exception failure = null;
            IntPtr correctionHandle = IntPtr.Zero;
            IntPtr foregroundTarget = IntPtr.Zero;
            Point location = Point.Empty;
            Size dialogSize = Size.Empty;
            bool topMostWhileOpen = false;
            bool topMostAfterRelease = true;
            bool usesNoActivate = true;
            var thread = new Thread(() =>
            {
                try
                {
                    var native = new RecordingCorrectionNativeApi();
                    var foreground = new CorrectionDialogForegroundService(native);
                    var game = new MutableGameWindow
                    {
                        Bounds = gameBounds,
                    };
                    var positioner = new CorrectionDialogPositioner(
                        game,
                        _ => new Rectangle(0, 0, 1920, 1040));
                    using var image = new Bitmap(2, 2);
                    using var dialog = new OcrCorrectionForm(image, "test", 10, "artifact set name");

                    Assert.True(positioner.TryPosition(dialog));
                    foreground.PrepareForShow(dialog);
                    correctionHandle = dialog.Handle;
                    Assert.True(foreground.RequestForeground(dialog));
                    location = dialog.Location;
                    dialogSize = dialog.Size;
                    topMostWhileOpen = dialog.TopMost;
                    foregroundTarget = native.LastForegroundTarget;
                    PropertyInfo createParamsProperty = typeof(Control).GetProperty(
                        "CreateParams",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    var createParams = (CreateParams)createParamsProperty.GetValue(dialog);
                    usesNoActivate = (createParams.ExStyle & 0x08000000) != 0;
                    foreground.Release(dialog);
                    topMostAfterRelease = dialog.TopMost;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));

            Assert.Null(failure);
            Assert.Equal(
                CorrectionDialogPlacement.Calculate(
                    gameBounds,
                    dialogSize,
                    new Rectangle(0, 0, 1920, 1040)),
                location);
            var correctionBounds = new Rectangle(location, dialogSize);
            Assert.True(gameBounds.Contains(correctionBounds));
            Assert.False(mainFormBounds.IntersectsWith(correctionBounds));
            Assert.NotEqual(IntPtr.Zero, correctionHandle);
            Assert.Equal(correctionHandle, foregroundTarget);
            Assert.True(topMostWhileOpen);
            Assert.False(topMostAfterRelease);
            Assert.False(usesNoActivate);
        }

        [Fact]
        public void Placement_WhenGameIsSmallerClampsDialogToMonitorWorkArea()
        {
            var workArea = new Rectangle(0, 0, 800, 600);
            var dialogSize = new Size(500, 400);

            Point location = CorrectionDialogPlacement.Calculate(
                new Rectangle(700, 500, 200, 100),
                dialogSize,
                workArea);

            Assert.Equal(new Point(300, 200), location);
            Assert.True(workArea.Contains(new Rectangle(location, dialogSize)));
        }

        [Fact]
        public void Placement_WhenDialogExceedsWorkAreaUsesSafeWorkAreaOrigin()
        {
            Point location = CorrectionDialogPlacement.Calculate(
                new Rectangle(500, 300, 200, 100),
                new Size(1000, 800),
                new Rectangle(20, 30, 800, 600));

            Assert.Equal(new Point(20, 30), location);
        }

        [Fact]
        public void SharedCorrectionLifecycle_HidesRestoresAndPreservesAnswerBeforeHandoff()
        {
            var events = new List<string>();
            var overlay = new RecordingOverlay(events);
            var viewModel = new ScanViewModel();
            using var image = new Bitmap(2, 2);
            viewModel.CorrectionRequested += args =>
                CorrectionDialogLifecycle.Run(overlay, () =>
                {
                    events.Add("PositionedOverGenshin");
                    events.Add("CorrectionForegroundRequested");
                    events.Add("Dialog");
                    args.ResolvedText = "chosen";
                    events.Add("CorrectionClosed");
                });
            viewModel.CorrectionInteractionCompleted += () =>
            {
                events.Add("GenshinForegroundRequested");
                events.Add("ScannerResumed");
            };

            string answer = viewModel.RequestCorrection(image, "original", 10, "item count");

            Assert.Equal("chosen", answer);
            Assert.Equal(
                new[]
                {
                    "InputRequired",
                    "OverlayHidden",
                    "PositionedOverGenshin",
                    "CorrectionForegroundRequested",
                    "Dialog",
                    "CorrectionClosed",
                    "OverlayRestored",
                    "GenshinForegroundRequested",
                    "ScannerResumed",
                },
                events);
        }

        private sealed class MutableGameWindow : IGenshinWindowController
        {
            internal Rectangle Bounds { get; set; }
            public bool IsForeground => true;
            public bool TryGetClientBounds(out Rectangle clientBounds)
            {
                clientBounds = Bounds;
                return !Bounds.IsEmpty;
            }
            public bool TryBringToForeground(TimeSpan timeout) => true;
        }

        private sealed class RecordingCorrectionNativeApi : ICorrectionWindowNativeApi
        {
            internal IntPtr LastForegroundTarget { get; private set; }
            public bool SetForegroundWindow(IntPtr window)
            {
                LastForegroundTarget = window;
                return true;
            }
        }

        private sealed class RecordingOverlay : IScanCompanionOverlay
        {
            private readonly List<string> events;
            internal RecordingOverlay(List<string> events) => this.events = events;
            public ScanCompanionState State { get; private set; }
            public void ShowPreparing(DeclaredInputMode declaredMode) => State = ScanCompanionState.Preparing;
            public bool WaitForControllerReady(CancellationToken cancellationToken) => true;
            public void ShowScanning() => State = ScanCompanionState.Scanning;
            public void ShowAchievementProgress(string progress, string details) =>
                State = ScanCompanionState.Scanning;
            public void ShowCancelling() => State = ScanCompanionState.Cancelling;
            public void ShowRestoringKeyboard() => State = ScanCompanionState.RestoringKeyboard;
            public void ShowInputRequired() => events.Add("InputRequired");
            public void ShowRecovery(IRecoveryInputController controller, string failureReason) =>
                State = ScanCompanionState.Recovery;
            public void ShowCompleted(string message) => State = ScanCompanionState.Completed;
            public void ShowError(string message) => State = ScanCompanionState.Error;
            public IDisposable SuspendForCapture() => new Restore(events);
            public IDisposable SuspendForInteraction()
            {
                events.Add("OverlayHidden");
                return new Restore(events);
            }

            private sealed class Restore : IDisposable
            {
                private readonly List<string> events;
                private int disposed;
                internal Restore(List<string> events) => this.events = events;
                public void Dispose()
                {
                    if (Interlocked.Exchange(ref disposed, 1) == 0)
                        events.Add("OverlayRestored");
                }
            }
        }
    }
}
