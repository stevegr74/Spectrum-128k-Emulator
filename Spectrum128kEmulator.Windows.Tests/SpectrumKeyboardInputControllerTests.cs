using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumKeyboardInputControllerTests
    {
        private const ulong FrameTStates = 69888;
        private readonly SpectrumKeyboardInputController controller = new();
        private readonly ulong[] scans = new ulong[8];
        private readonly bool[,] matrix = new bool[8, 5];

        [Fact]
        public void NormalKeyDownAndUpQueuedTogether_IsVisibleUntilTheGameScansIt()
        {
            Host(Keys.Enter, true, 0);
            Host(Keys.Enter, false, 0);

            Assert.True(matrix[6, 0]);

            scans[6]++;
            Advance(1000);
            Assert.False(matrix[6, 0]);
        }

        [Fact]
        public void ReleasingCursorLeft_DoesNotReleaseASeparatelyHeldShift()
        {
            Host(Keys.ShiftKey, true, 0);
            Host(Keys.Left, true, 0);
            ObserveRows(0, 3);
            Host(Keys.Left, false, 1000);

            Assert.True(matrix[0, 0]);
            Assert.False(matrix[3, 4]);
        }

        [Fact]
        public void ReleasingOneCursor_DoesNotReleaseShiftRequiredByAnotherCursor()
        {
            Host(Keys.Left, true, 0);
            Host(Keys.Up, true, 0);
            ObserveRows(0, 3, 4);
            Host(Keys.Left, false, 1000);

            Assert.True(matrix[0, 0]);
            Assert.False(matrix[3, 4]);
            Assert.True(matrix[4, 3]);
        }

        [Fact]
        public void TopRowAndNumpadSourcesShareTheSameSpectrumKeySafely()
        {
            Host(Keys.D1, true, 0);
            Host(Keys.NumPad1, true, 0);
            ObserveRows(3);
            Host(Keys.D1, false, 1000);

            Assert.True(matrix[3, 0]);

            Host(Keys.NumPad1, false, 1001);
            Assert.False(matrix[3, 0]);
        }

        [Fact]
        public void ControlAndAltSourcesShareSymbolShiftSafely()
        {
            Host(Keys.ControlKey, true, 0);
            Host(Keys.Menu, true, 0);
            ObserveRows(7);
            Host(Keys.ControlKey, false, 1000);

            Assert.True(matrix[7, 1]);

            Host(Keys.Menu, false, 1001);
            Assert.False(matrix[7, 1]);
        }

        [Fact]
        public void ReleaseDeadlineUsesEmulatedTStatesRatherThanSchedulerIterations()
        {
            Host(Keys.A, true, 200);
            Host(Keys.A, false, 200);

            for (ulong tStates = 1000; tStates < FrameTStates; tStates += 1000)
                Advance(tStates);
            Assert.True(matrix[1, 0]);

            Advance(200 + FrameTStates);
            Assert.False(matrix[1, 0]);
        }

        [Fact]
        public void ResetClearsInternalOwnershipBeforeTheMachineMatrixIsCleared()
        {
            Host(Keys.ShiftKey, true, 0);
            Host(Keys.Left, true, 0);

            controller.Reset();
            Array.Clear(matrix);
            Host(Keys.Left, true, 1000);

            Assert.True(matrix[0, 0]);
            Assert.True(matrix[3, 4]);
        }

        [Theory]
        [InlineData(Keys.Left, 0, 0, 3, 4)]
        [InlineData(Keys.Back, 0, 0, 4, 0)]
        [InlineData(Keys.Delete, 0, 0, 4, 0)]
        [InlineData(Keys.Escape, 0, 0, 7, 0)]
        [InlineData(Keys.CapsLock, 0, 0, 3, 1)]
        [InlineData(Keys.Tab, 0, 0, 7, 1)]
        public void ConvenienceKey_MapsToExpectedSpectrumChord(Keys key, int row1, int bit1, int row2, int bit2)
        {
            Host(key, true, 0);

            Assert.True(matrix[row1, bit1]);
            Assert.True(matrix[row2, bit2]);
        }

        private void Host(Keys key, bool isDown, ulong tStates)
        {
            controller.HandleHostKeyEvent(
                key,
                isDown,
                row => scans[row],
                tStates,
                FrameTStates,
                (row, bit, pressed) => matrix[row, bit] = pressed);
        }

        private void Advance(ulong tStates)
        {
            controller.Advance(
                row => scans[row],
                tStates,
                (row, bit, pressed) => matrix[row, bit] = pressed);
        }

        private void ObserveRows(params int[] rows)
        {
            foreach (int row in rows)
                scans[row]++;
        }
    }
}
