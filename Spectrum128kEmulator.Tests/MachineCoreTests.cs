using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class MachineCoreTests
    {
        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }

        [Fact]
        public void Keyboard_Is_Active_Low()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.SetKey(0, 0, true);

                byte portValue = machine.DebugReadPort(0xFEFE);
                Assert.Equal(0xFE, portValue & 0xFF);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Paging_Port_Changes_Rom_And_Screen_Bank()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.DebugWritePort(0x7FFD, 0x18);

                Assert.Equal(0, machine.PagedRamBank);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.Equal(7, machine.ScreenBank);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Paging_Lock_Prevents_Further_Changes()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.DebugWritePort(0x7FFD, 0x20 | 0x03);
                machine.DebugWritePort(0x7FFD, 0x10 | 0x08 | 0x07);

                Assert.True(machine.PagingLocked);
                Assert.Equal(3, machine.PagedRamBank);
                Assert.Equal(0, machine.CurrentRomBank);
                Assert.Equal(5, machine.ScreenBank);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }
    }
}
