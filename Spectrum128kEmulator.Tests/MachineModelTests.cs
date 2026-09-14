using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class MachineModelTests
    {
        [Fact]
        public void Reset_CanSelectExplicitMachineModel()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                Assert.Equal(SpectrumMachineModel.Spectrum128K, machine.MachineModel);
                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);

                machine.Reset(SpectrumMachineModel.Spectrum48K);

                Assert.Equal(SpectrumMachineModel.Spectrum48K, machine.MachineModel);
                Assert.Equal(Spectrum128Machine.FrameTStates48, machine.FrameTStates);

                machine.Reset();

                Assert.Equal(SpectrumMachineModel.Spectrum128K, machine.MachineModel);
                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static string CreateTempRoms()
        {
            string root = Path.Combine(Path.GetTempPath(), "SpectrumMachineModelTests_" + Guid.NewGuid().ToString("N"));
            string roms = Path.Combine(root, "ROMs");
            Directory.CreateDirectory(roms);
            File.WriteAllBytes(Path.Combine(roms, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(roms, "128-1.rom"), new byte[16384]);
            return roms;
        }
    }
}
