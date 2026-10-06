using System;
using System.IO;
using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumRomProfileTests
    {
        [Fact]
        public void BundledRomPair_Enables_AddressBasedTapeServices()
        {
            string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
            var machine = new Spectrum128Machine(romFolder);

            Assert.Equal("Sinclair ZX Spectrum 128K", machine.RomProfile.Name);
            Assert.True(machine.SupportsRomTapeAcceleration);
        }

        [Fact]
        public void UnknownRomPair_Disables_AddressBasedTapeServices_And_Uses_RawPlayback()
        {
            string romFolder = CreateUnknownRoms();

            try
            {
                var machine = new Spectrum128Machine(romFolder);
                var tape = new MountedTape(
                    "unknown-rom.tap",
                    new[]
                    {
                        TapeBlock.CreateData(
                            new byte[] { 0xFF, 0x42, 0xBD },
                            pilotPulseLength: 2168,
                            pilotPulseCount: 3223,
                            syncFirstPulseLength: 667,
                            syncSecondPulseLength: 735,
                            zeroBitPulseLength: 855,
                            oneBitPulseLength: 1710,
                            usedBitsInLastByte: 8,
                            pauseAfterBlockMs: 1000)
                    });
                machine.MountTape(tape);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 1;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                machine.SetPendingMountedLoadUsrContinuation(0x9000);
                machine.SetPendingMountedLoadBasicResume(10, 0);
                TapeLoadPlan plan = TapLoader.CreateExecutionPlan(
                    machine,
                    new[] { TapeBlock.CreatePureTone(pulseLength: 2168, pulseCount: 32) });

                Assert.Equal("Unknown 128K ROM pair", machine.RomProfile.Name);
                Assert.False(machine.SupportsRomTapeAcceleration);
                Assert.False(machine.HasPendingMountedLoadUsrContinuation);
                Assert.False(machine.TryServiceTapeTrap());
                Assert.Equal((byte)0, machine.PeekMemory(0x8000));
                Assert.Equal(TapeLoadStrategy.MountedRealtime, plan.Strategy);
                Assert.Contains("not compatible", plan.Reason, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static string CreateUnknownRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            byte[] rom0 = new byte[16384];
            byte[] rom1 = new byte[16384];
            rom0[0] = 0xF3;
            rom1[0] = 0xC3;
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), rom0);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), rom1);
            return folder;
        }
    }
}
