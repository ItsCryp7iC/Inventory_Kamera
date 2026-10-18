using InventoryKamera.game;
using System;
using System.Collections.Generic;
using System.Threading;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class GameTextInputTests
    {
        [Fact]
        public void NativeSendInputPacketUsesPlatformInputSize()
        {
            Assert.Equal(IntPtr.Size == 8 ? 40 : 28, SendInputGameKeyboardTextBackend.NativeInputSize);
        }

        [Fact]
        public void ReplaceAll_SelectsDeletesAndTypesExactUnicodeWithoutSubmit()
        {
            var backend = new RecordingBackend();
            var input = new GameTextInput(backend);

            input.ReplaceAll("À la recherche!", CancellationToken.None);

            Assert.Equal(new[]
            {
                "SelectAll",
                "DeleteSelection",
                "TypeText:À la recherche!"
            }, backend.Events);
            Assert.DoesNotContain(backend.Events, value =>
                value.Contains("Enter", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Controller", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void ReplaceAll_RejectsNullWithoutBackendInput()
        {
            var backend = new RecordingBackend();
            var input = new GameTextInput(backend);

            Assert.Throws<ArgumentNullException>(() =>
                input.ReplaceAll(null, CancellationToken.None));

            Assert.Empty(backend.Events);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\t")]
        public void ReplaceAll_RejectsEmptyOrWhitespaceWithoutBackendInput(string value)
        {
            var backend = new RecordingBackend();
            var input = new GameTextInput(backend);

            Assert.Throws<ArgumentException>(() =>
                input.ReplaceAll(value, CancellationToken.None));

            Assert.Empty(backend.Events);
        }

        [Fact]
        public void ReplaceAll_CancellationBetweenClearAndTypeStopsFurtherInput()
        {
            using var cancellation = new CancellationTokenSource();
            var backend = new RecordingBackend { AfterSelectAll = cancellation.Cancel };
            var input = new GameTextInput(backend);

            Assert.Throws<OperationCanceledException>(() =>
                input.ReplaceAll("Pur", cancellation.Token));

            Assert.Equal(new[] { "SelectAll" }, backend.Events);
        }

        private sealed class RecordingBackend : IGameKeyboardTextBackend
        {
            internal List<string> Events { get; } = new();
            internal Action AfterSelectAll { get; set; }
            public void SelectAll()
            {
                Events.Add("SelectAll");
                AfterSelectAll?.Invoke();
            }
            public void DeleteSelection() => Events.Add("DeleteSelection");
            public void TypeText(string text) => Events.Add("TypeText:" + text);
        }
    }
}
