using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumKeyboardPipelineTests
    {
        [Fact]
        public void QuickWindowsTap_IsVisibleOnTheRealMachinePortForOneScan()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                var controller = new SpectrumKeyboardInputController();

                controller.HandleHostKeyEvent(
                    Keys.Enter,
                    true,
                    machine.GetKeyboardRowScanCount,
                    0,
                    (ulong)machine.FrameTStates,
                    machine.SetKey);
                controller.HandleHostKeyEvent(
                    Keys.Enter,
                    false,
                    machine.GetKeyboardRowScanCount,
                    0,
                    (ulong)machine.FrameTStates,
                    machine.SetKey);

                Assert.Equal(0, machine.DebugReadPort(0xBFFE) & 0x01);

                controller.Advance(machine.GetKeyboardRowScanCount, 1000, machine.SetKey);
                Assert.Equal(1, machine.DebugReadPort(0xBFFE) & 0x01);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void SharedCursorShift_RemainsAssertedOnTheRealMachinePort()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                var controller = new SpectrumKeyboardInputController();

                SetHostKey(controller, machine, Keys.ShiftKey, true, 0);
                SetHostKey(controller, machine, Keys.Left, true, 0);
                machine.DebugReadPort(0xFEFE);
                machine.DebugReadPort(0xF7FE);
                SetHostKey(controller, machine, Keys.Left, false, 1000);

                Assert.Equal(0, machine.DebugReadPort(0xFEFE) & 0x01);
                Assert.Equal(0x10, machine.DebugReadPort(0xF7FE) & 0x10);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static void SetHostKey(
            SpectrumKeyboardInputController controller,
            Spectrum128Machine machine,
            Keys key,
            bool isDown,
            ulong tStates)
        {
            controller.HandleHostKeyEvent(
                key,
                isDown,
                machine.GetKeyboardRowScanCount,
                tStates,
                (ulong)machine.FrameTStates,
                machine.SetKey);
        }

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }
    }
}
