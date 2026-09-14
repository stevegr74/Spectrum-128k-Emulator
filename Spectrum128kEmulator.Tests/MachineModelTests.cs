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

        [Fact]
        public void Spectrum48kMode_IgnoresAyPortWrites()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Reset(SpectrumMachineModel.Spectrum48K);

                machine.DebugWritePort(0xFFFD, 0x07);
                machine.DebugWritePort(0xBFFD, 0xAB);

                Assert.Equal((byte)0x00, machine.Ay.CurrentRegister);
                Assert.Equal((byte)0x00, machine.Ay.ReadRegister(7));
                Assert.False(machine.HasAudibleOutput);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Spectrum48kMode_EmitsAudioFramesWithoutAyState()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Reset(SpectrumMachineModel.Spectrum48K);
                machine.Ay.SelectRegister(7);
                machine.Ay.WriteRegister(0b0011_1110);
                machine.Ay.SelectRegister(8);
                machine.Ay.WriteRegister(0x0F);

                machine.ExecuteTimeSlice(machine.FrameTStates);

                Assert.True(machine.TryDequeueCompletedAudioFrame(out var frame));
                Assert.Null(frame.AyState);
                Assert.Null(frame.InitialAyState);
                Assert.Empty(frame.AyWrites);
                Assert.False(machine.HasAudibleOutput);
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
