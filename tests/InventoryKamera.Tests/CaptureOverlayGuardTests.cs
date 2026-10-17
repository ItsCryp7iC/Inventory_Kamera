using InventoryKamera.game;
using System;
using System.Collections.Generic;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class CaptureOverlayGuardTests
    {
        [Theory]
        [InlineData("Weapon")]
        [InlineData("Artifact")]
        [InlineData("Character")]
        [InlineData("Material")]
        public void SharedCaptureBoundary_HidesOverlayForEveryOcrFamily(string captureFamily)
        {
            var events = new List<string>();
            var guard = new RecordingGuard(events);
            string result = Navigation.CaptureWithoutOverlay(() =>
                {
                    events.Add("Capture:" + captureFamily);
                    return captureFamily;
                }, guard);

            Assert.Equal(captureFamily, result);
            Assert.Equal(
                new[] { "OverlayHidden", "Capture:" + captureFamily, "OverlayRestored" },
                events);
        }

        [Fact]
        public void SharedCaptureBoundary_RestoresOverlayWhenCaptureThrows()
        {
            var events = new List<string>();
            var guard = new RecordingGuard(events);
            Assert.Throws<InvalidOperationException>(() =>
                Navigation.CaptureWithoutOverlay<object>(() =>
                    {
                        events.Add("CaptureFailed");
                        throw new InvalidOperationException("capture failed");
                    }, guard));

            Assert.Equal(
                new[] { "OverlayHidden", "CaptureFailed", "OverlayRestored" },
                events);
        }

        private sealed class RecordingGuard : ICaptureOverlayGuard
        {
            private readonly List<string> events;
            internal RecordingGuard(List<string> events) => this.events = events;
            public IDisposable SuspendForCapture()
            {
                events.Add("OverlayHidden");
                return new Restore(events);
            }

            private sealed class Restore : IDisposable
            {
                private readonly List<string> events;
                private bool disposed;
                internal Restore(List<string> events) => this.events = events;
                public void Dispose()
                {
                    if (disposed) return;
                    disposed = true;
                    events.Add("OverlayRestored");
                }
            }
        }
    }
}
