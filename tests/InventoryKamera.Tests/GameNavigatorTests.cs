using InventoryKamera.game;
using System.Collections.Generic;
using System.Globalization;
using Xunit;

namespace InventoryKamera.Tests
{
    public class GameNavigatorTests
    {
        [Fact]
        public void EnterControllerMode_SendsExistingInputSequenceInOrder()
        {
            var calls = new List<string>();
            var navigator = CreateNavigator(calls);

            navigator.EnterControllerMode();

            Assert.Equal(new[]
            {
                "Horizontal:0.5",
                "Wait:150",
                "Horizontal:0",
                "Wait:150",
                "Button:Back:True",
                "Wait:150",
                "Button:Back:False"
            }, calls);
        }

        [Fact]
        public void Move_RepeatsStickStepWithRequestedTiming()
        {
            var calls = new List<string>();
            var navigator = CreateNavigator(calls);

            navigator.Move(GameNavigator.MenuDirection.Right, 2, holdMs: 80, settleMs: 100);

            Assert.Equal(new[]
            {
                "Horizontal:1", "Wait:80", "Horizontal:0", "Vertical:0", "Wait:100",
                "Horizontal:1", "Wait:80", "Horizontal:0", "Vertical:0", "Wait:100"
            }, calls);
        }

        [Fact]
        public void Dispose_ExitsControllerModeBeforeDisconnectingInput()
        {
            var calls = new List<string>();
            var navigator = CreateNavigator(calls);
            var expected = new List<string>();
            for (var i = 0; i < 6; i++)
            {
                expected.Add("Button:Back:True");
                expected.Add("Wait:150");
                expected.Add("Button:Back:False");
                expected.Add("Wait:300");
            }
            expected.Add("Mouse:1:0");
            expected.Add("Wait:50");
            expected.Add("Mouse:-1:0");
            expected.Add("Wait:100");
            expected.Add("Dispose");

            navigator.Dispose();

            Assert.Equal(expected, calls);
        }

        private static GameNavigator CreateNavigator(List<string> calls)
        {
            var input = new FakeGameInput(calls);
            return new GameNavigator(input, milliseconds => calls.Add($"Wait:{milliseconds}"));
        }

        private sealed class FakeGameInput : IGameInput
        {
            private readonly List<string> calls;

            public FakeGameInput(List<string> calls)
            {
                this.calls = calls;
            }

            public bool IsAvailable => true;
            public string FailureReason => null;

            public void SetLeftStickHorizontal(float value) =>
                calls.Add("Horizontal:" + value.ToString("0.###", CultureInfo.InvariantCulture));

            public void SetLeftStickVertical(float value) =>
                calls.Add("Vertical:" + value.ToString("0.###", CultureInfo.InvariantCulture));

            public void SetButtonState(GameInputButton button, bool isPressed) =>
                calls.Add($"Button:{button}:{isPressed}");

            public void MoveMouseBy(int x, int y) => calls.Add($"Mouse:{x}:{y}");

            public void Dispose() => calls.Add("Dispose");
        }
    }
}
