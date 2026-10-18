using System;
using System.IO;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class UlaTimingTests
    {
        [Fact]
        public void TimingProfiles_DefineTheSupportedMachineBaselines()
        {
            AssertTimingProfile(
                UlaTimingProfile.Spectrum48K,
                SpectrumMachineModel.Spectrum48K,
                frameTStates: 69888,
                cpuClockHz: 3500000,
                lineTStates: 224,
                contentionStartTState: 14335,
                displayFetchStartTState: 14338);
            AssertTimingProfile(
                UlaTimingProfile.Spectrum128K,
                SpectrumMachineModel.Spectrum128K,
                frameTStates: 70908,
                cpuClockHz: 3546900,
                lineTStates: 228,
                contentionStartTState: 14361,
                displayFetchStartTState: 14364);
        }

        [Theory]
        [InlineData(SpectrumMachineModel.Spectrum48K, 14335)]
        [InlineData(SpectrumMachineModel.Spectrum128K, 14361)]
        public void ContendedMemoryAccess_UsesTheCompleteEightTStateDelayPattern(
            SpectrumMachineModel model,
            int contentionStartTState)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Reset(model);
                ulong[] expectedDelays = { 6, 5, 4, 3, 2, 1, 0, 0 };

                for (int phase = 0; phase < expectedDelays.Length; phase++)
                {
                    machine.PokeMemory(0x4000, 0x00); // NOP
                    machine.Cpu.Regs.PC = 0x4000;
                    machine.SetSnapshotResumeFramePhase(contentionStartTState + phase);

                    ulong before = machine.Cpu.TStates;
                    machine.ExecuteTimeSlice(1, out _);

                    Assert.Equal(4UL + expectedDelays[phase], machine.Cpu.TStates - before);
                }
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(SpectrumMachineModel.Spectrum48K, 14335, 224)]
        [InlineData(SpectrumMachineModel.Spectrum128K, 14361, 228)]
        public void ContentionWindow_StopsAfterTheDisplayFetchesAndRestartsOnTheNextLine(
            SpectrumMachineModel model,
            int contentionStartTState,
            int lineTStates)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Reset(model);

                AssertMemoryFetchDuration(machine, contentionStartTState - 1, 4UL);
                AssertMemoryFetchDuration(machine, contentionStartTState + 127, 4UL);
                AssertMemoryFetchDuration(machine, contentionStartTState + 128, 4UL);
                AssertMemoryFetchDuration(machine, contentionStartTState + lineTStates, 10UL);
                AssertMemoryFetchDuration(
                    machine,
                    contentionStartTState + (lineTStates * UlaTimingProfile.ForModel(model).DisplayLineCount),
                    4UL);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

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
        [InlineData(0x0001, 12UL)] // Uncontended address byte, odd port: no ULA contention.
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

        [Theory]
        [InlineData(0x0001, 12UL)]
        [InlineData(0x4001, 24UL)]
        [InlineData(0x4000, 18UL)]
        [InlineData(0x0000, 17UL)]
        public void Contended128kIo_UsesTheDocumentedUlaBusPattern(ushort port, ulong expectedTStates)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor128kSnapshot(last7ffdValue: 0, borderColor: 0);
                machine.PokeMemory(0x8000, 0xED);
                machine.PokeMemory(0x8001, 0x78); // IN A,(C)
                machine.Cpu.Regs.PC = 0x8000;
                machine.Cpu.Regs.BC = port;
                machine.SetSnapshotResumeFramePhase(UlaTimingProfile.Spectrum128K.ContentionStartTState);

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

        [Theory]
        [InlineData(0, 0xA0)]
        [InlineData(1, 0xB0)]
        [InlineData(2, 0xA1)]
        [InlineData(3, 0xB1)]
        [InlineData(4, 0xFF)]
        [InlineData(5, 0xFF)]
        [InlineData(6, 0xFF)]
        [InlineData(7, 0xFF)]
        public void FloatingBus48_ReadsTheTwoDisplayPairsThenReturnsIdle(int phase, byte expected)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                byte[] ram = new byte[48 * 1024];
                ram[0x0000] = 0xA0;
                ram[0x0001] = 0xA1;
                ram[0x1800] = 0xB0;
                ram[0x1801] = 0xB1;
                machine.Load48kSnapshotRam(ram);
                machine.SetSnapshotResumeFramePhase(UlaTimingProfile.Spectrum48K.DisplayFetchStartTState + phase);

                Assert.Equal(expected, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(0, 0xC0)]
        [InlineData(1, 0xD0)]
        [InlineData(2, 0xC1)]
        [InlineData(3, 0xD1)]
        [InlineData(4, 0xFF)]
        [InlineData(5, 0xFF)]
        [InlineData(6, 0xFF)]
        [InlineData(7, 0xFF)]
        public void FloatingBus128_ReadsTheTwoDisplayPairsThenReturnsIdle(int phase, byte expected)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                byte[] bank = new byte[16 * 1024];
                bank[0x0000] = 0xC0;
                bank[0x0001] = 0xC1;
                bank[0x1800] = 0xD0;
                bank[0x1801] = 0xD1;
                machine.LoadRamBank(5, bank);
                machine.ConfigureFor128kSnapshot(last7ffdValue: 0, borderColor: 0);
                machine.SetSnapshotResumeFramePhase(UlaTimingProfile.Spectrum128K.DisplayFetchStartTState + phase);

                Assert.Equal(expected, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(SpectrumMachineModel.Spectrum48K)]
        [InlineData(SpectrumMachineModel.Spectrum128K)]
        public void FloatingBus_ReturnsHighBeforeAndAfterTheDisplayFetchWindow(SpectrumMachineModel model)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.Reset(model);
                UlaTimingProfile timing = UlaTimingProfile.ForModel(model);

                machine.SetSnapshotResumeFramePhase(timing.DisplayFetchStartTState - 1);
                Assert.Equal((byte)0xFF, machine.DebugReadPort(0xFFFF));

                int afterDisplay = timing.DisplayFetchStartTState + (timing.LineTStates * timing.DisplayLineCount);
                machine.SetSnapshotResumeFramePhase(afterDisplay);
                Assert.Equal((byte)0xFF, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void FloatingBus128_UsesSpectrumBitmapAndAttributeLineAddressing()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                byte[] bank = new byte[16 * 1024];
                bank[0x0100] = 0x11; // Bitmap line 1, column 0.
                bank[0x0020] = 0x88; // Bitmap line 8, column 0.
                bank[0x1820] = 0xA8; // Attribute row 1, column 0.
                machine.LoadRamBank(5, bank);
                machine.ConfigureFor128kSnapshot(last7ffdValue: 0, borderColor: 0);
                UlaTimingProfile timing = UlaTimingProfile.Spectrum128K;

                machine.SetSnapshotResumeFramePhase(timing.DisplayFetchStartTState + timing.LineTStates);
                Assert.Equal((byte)0x11, machine.DebugReadPort(0xFFFF));

                machine.SetSnapshotResumeFramePhase(timing.DisplayFetchStartTState + (8 * timing.LineTStates));
                Assert.Equal((byte)0x88, machine.DebugReadPort(0xFFFF));
                machine.SetSnapshotResumeFramePhase(timing.DisplayFetchStartTState + (8 * timing.LineTStates) + 1);
                Assert.Equal((byte)0xA8, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(0x00, 5, 0x51)]
        [InlineData(0x08, 7, 0x71)]
        public void FloatingBus128_ReadsFromTheSelectedScreenBank(byte paging, int screenBank, byte expected)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                byte[] bank = new byte[16 * 1024];
                bank[0] = expected;
                machine.LoadRamBank(screenBank, bank);
                machine.ConfigureFor128kSnapshot(paging, borderColor: 0);
                machine.SetSnapshotResumeFramePhase(UlaTimingProfile.Spectrum128K.DisplayFetchStartTState);

                Assert.Equal(expected, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void AyRegisterRead_TakesPriorityOverThe128kFloatingBus()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                byte[] screenBank = new byte[16 * 1024];
                screenBank[0] = 0x55;
                machine.LoadRamBank(5, screenBank);
                machine.ConfigureFor128kSnapshot(last7ffdValue: 0, borderColor: 0);
                machine.DebugWritePort(0xFFFD, 0x07);
                machine.DebugWritePort(0xBFFD, 0xA6);
                machine.SetSnapshotResumeFramePhase(UlaTimingProfile.Spectrum128K.DisplayFetchStartTState);

                Assert.Equal((byte)0xA6, machine.DebugReadPort(0xFFFD));
                Assert.Equal((byte)0x55, machine.DebugReadPort(0xFFFF));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void TimedCpuPortRead_ReturnsTheSelectedAyRegister()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor128kSnapshot(last7ffdValue: 0, borderColor: 0);
                machine.DebugWritePort(0xFFFD, 0x0A);
                machine.DebugWritePort(0xBFFD, 0x6C);
                machine.PokeMemory(0x8000, 0xED);
                machine.PokeMemory(0x8001, 0x78); // IN A,(C)
                machine.Cpu.Regs.PC = 0x8000;
                machine.Cpu.Regs.BC = 0xFFFD;

                machine.ExecuteTimeSlice(1, out _);

                Assert.Equal((byte)0x6C, machine.Cpu.Regs.A);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void AyRegisterRead_IsNotAttachedIn48kMode()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.ConfigureFor48kSnapshot(borderColor: 0);
                byte[] ram = new byte[48 * 1024];
                ram[0] = 0x48;
                machine.Load48kSnapshotRam(ram);
                machine.SetSnapshotResumeFramePhase(UlaTimingProfile.Spectrum48K.DisplayFetchStartTState);

                Assert.Equal((byte)0x48, machine.DebugReadPort(0xFFFD));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static void AssertTimingProfile(
            UlaTimingProfile profile,
            SpectrumMachineModel model,
            int frameTStates,
            int cpuClockHz,
            int lineTStates,
            int contentionStartTState,
            int displayFetchStartTState)
        {
            Assert.Equal(model, profile.Model);
            Assert.Equal(frameTStates, profile.FrameTStates);
            Assert.Equal(cpuClockHz, profile.CpuClockHz);
            Assert.Equal(lineTStates, profile.LineTStates);
            Assert.Equal(contentionStartTState, profile.ContentionStartTState);
            Assert.Equal(displayFetchStartTState, profile.DisplayFetchStartTState);
            Assert.Equal(192, profile.DisplayLineCount);
            Assert.Equal(128, profile.DisplayFetchTStatesPerLine);
        }

        private static void AssertMemoryFetchDuration(
            Spectrum128Machine machine,
            int framePhase,
            ulong expectedDuration)
        {
            machine.PokeMemory(0x4000, 0x00); // NOP
            machine.Cpu.Regs.PC = 0x4000;
            machine.SetSnapshotResumeFramePhase(framePhase);

            ulong before = machine.Cpu.TStates;
            machine.ExecuteTimeSlice(1, out _);

            Assert.Equal(expectedDuration, machine.Cpu.TStates - before);
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
