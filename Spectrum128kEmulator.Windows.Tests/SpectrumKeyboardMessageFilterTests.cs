using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumKeyboardMessageFilterTests
    {
        [Theory]
        [InlineData(0x0100, true)]
        [InlineData(0x0101, false)]
        [InlineData(0x0104, true)]
        [InlineData(0x0105, false)]
        public void MappedPhysicalMessages_AreCapturedBeforeWinFormsPreprocessing(int messageId, bool expectedDown)
        {
            Keys? capturedKey = null;
            bool? capturedDown = null;
            var filter = new SpectrumKeyboardMessageFilter(
                () => true,
                (key, isDown) =>
                {
                    capturedKey = key;
                    capturedDown = isDown;
                });
            Message message = Message.Create(IntPtr.Zero, messageId, (IntPtr)(int)Keys.Menu, IntPtr.Zero);

            Assert.True(filter.PreFilterMessage(ref message));
            Assert.Equal(Keys.Menu, capturedKey);
            Assert.Equal(expectedDown, capturedDown);
        }

        [Fact]
        public void MessagesAreNotCapturedWhenEmulatorDoesNotHaveFocus()
        {
            bool called = false;
            var filter = new SpectrumKeyboardMessageFilter(() => false, (_, _) => called = true);
            Message message = Message.Create(
                IntPtr.Zero,
                0x0100,
                (IntPtr)(int)Keys.A,
                IntPtr.Zero);

            Assert.False(filter.PreFilterMessage(ref message));
            Assert.False(called);
        }

        [Fact]
        public void EmulatorShortcutAndSystemKeysRemainAvailableToTheFormAndWindows()
        {
            bool called = false;
            var filter = new SpectrumKeyboardMessageFilter(() => true, (_, _) => called = true);
            Message message = Message.Create(
                IntPtr.Zero,
                0x0104,
                (IntPtr)(int)Keys.F4,
                IntPtr.Zero);

            Assert.False(filter.PreFilterMessage(ref message));
            Assert.False(called);
        }
    }
}
