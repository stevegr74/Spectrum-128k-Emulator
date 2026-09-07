using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class UlaTimingTests
    {
        [Fact]
        public void Contended48kMemoryAccess_Uses_The_48k_Ula_Window()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.PokeMemory(0x4000, 0x00); // NOP
                machine.Cpu.Regs.PC = 0x4000;
                machine.SetSnapshotResumeFramePhase(14335);

                ulong before = machine.Cpu.TStates;
                machine.ExecuteTimeSlice(1, out _);

                Assert.Equal(10UL, machine.Cpu.TStates - before);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(0x00, 4UL)]
        [InlineData(0x01, 10UL)]
        [InlineData(0x02, 4UL)]
        [InlineData(0x03, 10UL)]
        [InlineData(0x04, 4UL)]
        [InlineData(0x05, 10UL)]
        [InlineData(0x06, 4UL)]
        [InlineData(0x07, 10UL)]
        public void Paged128kMemoryAccess_UsesTheContentionProfileForEachRamBank(byte pagedBank, ulong expectedTStates)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor128kSnapshot(last7ffdValue: pagedBank, borderColor: 0);
                machine.PokeMemory(0xC000, 0x00); // NOP in the active paged bank
                machine.Cpu.Regs.PC = 0xC000;
                machine.SetSnapshotResumeFramePhase(14361);

                ulong before = machine.Cpu.TStates;
                machine.ExecuteTimeSlice(1, out _);

                Assert.Equal(expectedTStates, machine.Cpu.TStates - before);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void CompletedFrame_PreservesTimestampedBorderWrites()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ExecuteTimeSlice(1);
                machine.DebugWritePort(0x00FE, 0x02);
                machine.ExecuteFrame();

                BorderFrame borderFrame = machine.LastCompletedBorderFrame;
                Assert.Equal(1, borderFrame.InitialColor);
                Assert.Single(borderFrame.Events);
                Assert.Equal(2, borderFrame.Events[0].Color);
                Assert.InRange(borderFrame.Events[0].TStateOffset, 1, machine.FrameTStates);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(0x4001, 24UL)] // Contended address byte, odd port: four ULA-bus cycles.
        [InlineData(0x4000, 18UL)] // Contended address byte and even port: two ULA-bus cycles.
        [InlineData(0x0000, 17UL)] // Even ULA port: final three cycles are contended.
        public void Contended48kIo_UsesTheDocumentedUlaBusPattern(ushort port, ulong expectedTStates)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.PokeMemory(0x8000, 0xED);
                machine.PokeMemory(0x8001, 0x78); // IN A,(C)
                machine.Cpu.Regs.PC = 0x8000;
                machine.Cpu.Regs.BC = port;
                machine.SetSnapshotResumeFramePhase(14335);

                ulong before = machine.Cpu.TStates;
                machine.ExecuteTimeSlice(1, out _);

                Assert.Equal(expectedTStates, machine.Cpu.TStates - before);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ContendedUlaPort_ReadsEarAtTheDelayedThirdIoTState()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                machine.SetDebugEventCaptureEnabled(true);
                machine.PokeMemory(0x8000, 0xED);
                machine.PokeMemory(0x8001, 0x78); // IN A,(C)
                machine.Cpu.Regs.PC = 0x8000;
                machine.Cpu.Regs.BC = 0x00FE;
                machine.SetSnapshotResumeFramePhase(14335);

                machine.ExecuteTimeSlice(1, out _);

                // This phase has an active-display ULA delay. The event must use
                // the delayed third I/O T-state, rather than the instruction end.
                string dump = machine.BuildDebugDump();
                int eventOffset = dump.IndexOf("IN  00FE", StringComparison.Ordinal);
                Assert.True(eventOffset >= 0, dump);
                Assert.Contains("T=     14351 IN  00FE", dump);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(0x00, 12UL)]
        [InlineData(0x01, 24UL)]
        [InlineData(0x02, 12UL)]
        [InlineData(0x03, 24UL)]
        [InlineData(0x04, 12UL)]
        [InlineData(0x05, 24UL)]
        [InlineData(0x06, 12UL)]
        [InlineData(0x07, 24UL)]
        public void Paged128kIo_RecognisesTheActiveContentionBank(byte pagedBank, ulong expectedTStates)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor128kSnapshot(last7ffdValue: pagedBank, borderColor: 0);
                machine.PokeMemory(0x8000, 0xED);
                machine.PokeMemory(0x8001, 0x78); // IN A,(C)
                machine.Cpu.Regs.PC = 0x8000;
                machine.Cpu.Regs.BC = 0xC001;
                machine.SetSnapshotResumeFramePhase(14361);

                ulong before = machine.Cpu.TStates;
                machine.ExecuteTimeSlice(1, out _);

                Assert.Equal(expectedTStates, machine.Cpu.TStates - before);
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
