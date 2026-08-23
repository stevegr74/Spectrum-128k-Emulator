using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DiagnosticsPerformanceTests
    {
        [Fact]
        public void ScreenWriteDiagnostics_Are_OptIn()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                machine.PokeMemory(0x4000, 0x01);
                machine.PokeMemory(0x5B00, 0x02);
                Assert.Empty(machine.ScreenWriteLog);
                Assert.Empty(machine.AboveScreenWriteLog);

                machine.SetScreenWriteDiagnosticsEnabled(true);
                machine.PokeMemory(0x4000, 0x03);
                machine.PokeMemory(0x5B00, 0x04);
                Assert.Equal(1, machine.ScreenWriteLog[0x4000]);
                Assert.Equal(1, machine.AboveScreenWriteLog[0x5B00]);

                machine.SetScreenWriteDiagnosticsEnabled(false);
                Assert.Empty(machine.ScreenWriteLog);
                Assert.Empty(machine.AboveScreenWriteLog);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
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
