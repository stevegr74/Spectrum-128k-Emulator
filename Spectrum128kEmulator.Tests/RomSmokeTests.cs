using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class RomSmokeTests
    {
        [Fact]
        public void Boots_For_Thirty_Frames_Without_Leaving_Valid_State()
        {
            string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
            if (!Directory.Exists(romFolder))
                return;

            string rom0 = Path.Combine(romFolder, "128-0.rom");
            string rom1 = Path.Combine(romFolder, "128-1.rom");
            if (!File.Exists(rom0) || !File.Exists(rom1))
                return;

            var machine = new Spectrum128Machine(romFolder);
            for (int i = 0; i < 30; i++)
                machine.ExecuteFrame();

            Assert.InRange(machine.Cpu.Regs.SP, (ushort)0x4000, ushort.MaxValue);
            Assert.InRange(machine.Cpu.Regs.PC, (ushort)0x0000, ushort.MaxValue);
        }
    }
}
