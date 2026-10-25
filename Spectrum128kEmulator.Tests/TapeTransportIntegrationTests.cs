using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public partial class TapLoaderTests
    {
        [Fact]
        public void MountedTape_ManualPauseAndResume_PreservesPulsePositionAndEarLevel()
        {
            var tape = new MountedTape(
                "pause.tap",
                new[] { TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 4) });

            Assert.True(tape.ReadEarBit(0));
            Assert.True(tape.ReadEarBit(40));
            Assert.True(tape.PausePlayback(40));
            Assert.True(tape.IsPlaybackPaused);
            Assert.True(tape.ReadEarBit(1040));

            Assert.True(tape.ResumePlayback(1040));
            Assert.True(tape.ReadEarBit(1099));
            Assert.False(tape.ReadEarBit(1100));
        }

        [Theory]
        [InlineData(SpectrumMachineModel.Spectrum48K, 2168)]
        [InlineData(SpectrumMachineModel.Spectrum128K, 2197)]
        public void Machine_MountedTape_Scales35MHzTapePulsesToModelClock(
            SpectrumMachineModel model,
            int expectedPulseTStates)
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                machine.Reset(model);
                var tape = new MountedTape(
                    "clock-scaled.tzx",
                    new[] { TapeBlock.CreatePureTone(pulseLength: 2168, pulseCount: 2) });

                machine.MountTape(tape);

                Assert.Equal(expectedPulseTStates, (int)GetPrivateField(tape, "earPulseLengthTStates"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_StopMarker_PausesUntilResumedAndKeepsFollowingBlocks()
        {
            var tape = new MountedTape(
                "marker.tzx",
                new[]
                {
                    TapeBlock.CreatePureTone(pulseLength: 10, pulseCount: 1),
                    TapeBlock.CreateStopTape(),
                    TapeBlock.CreatePureTone(pulseLength: 20, pulseCount: 1)
                });

            Assert.True(tape.ReadEarBit(0));
            Assert.False(tape.ReadEarBit(10));
            Assert.True(tape.IsPlaybackPaused);
            Assert.True(tape.IsPausedByStopMarker);
            Assert.False(tape.ReadEarBit(1000));

            Assert.True(tape.ResumePlayback(1000));
            Assert.False(tape.IsPlaybackPaused);
            Assert.False(tape.ReadEarBit(1019));
            Assert.True(tape.ReadEarBit(1020));
            Assert.True(tape.HasCompletedPlayback);
        }

        [Fact]
        public void Machine_ToggleTapeTransport_StopsAndResumesMountedTape()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape(
                    "transport.tap",
                    new[] { TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 4) }));

                Assert.Equal(TapeTransportState.Playing, machine.TapeTransportState);
                Assert.Equal(TapeTransportState.Stopped, machine.ToggleTapeTransport());
                Assert.True(machine.MountedTape!.IsPlaybackPaused);
                Assert.Equal(TapeTransportState.Playing, machine.ToggleTapeTransport());
                Assert.False(machine.MountedTape!.IsPlaybackPaused);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_ExhaustedIdleTape_AutoEjectsWithoutWaveformCompletion()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "fast-loaded.tap",
                    new[] { TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 4) },
                    initialBlockIndex: 1);
                machine.MountTape(tape);

                Assert.False(tape.HasCompletedPlayback);
                Assert.True(tape.IsExhaustedAndIdle);

                machine.ExecuteTimeSlice(1);

                Assert.False(machine.HasMountedTape);
                Assert.Equal(TapeTransportState.Ended, machine.TapeTransportState);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_ReachingStopMarker_ReportsPersistentStoppedTransport()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape(
                    "marker.tzx",
                    new[]
                    {
                        TapeBlock.CreatePureTone(pulseLength: 1, pulseCount: 1),
                        TapeBlock.CreateStopTape(),
                        TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 1)
                    }));

                machine.ExecuteTimeSlice(32);
                machine.ExecuteTimeSlice(32);

                Assert.True(machine.HasMountedTape);
                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);
                Assert.True(machine.IsTapeStoppedAtMarker);

                Assert.Equal(TapeTransportState.Playing, machine.ToggleTapeTransport());
                Assert.False(machine.IsTapeStoppedAtMarker);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_AutoStopsTape_WhenLoaderHandsOffToStableHaltLoop()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                    2168, 60000, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape(
                    "loader-handoff.tap",
                    new[] { dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false));

                ushort pc = 0x8000;
                for (int read = 0; read < 96; read++)
                {
                    machine.PokeMemory(pc++, 0xDB); // IN A,(FE)
                    machine.PokeMemory(pc++, 0xFE);
                }
                machine.PokeMemory(pc, 0x76); // HALT
                machine.Cpu.Regs.PC = 0x8000;

                machine.ExecuteFrame();
                Assert.Equal(TapeTransportState.Playing, machine.TapeTransportState);

                machine.ExecuteFrame();
                machine.ExecuteFrame();
                machine.ExecuteFrame();

                Assert.True(machine.HasMountedTape);
                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);
                Assert.Equal(TapeTransportTransition.AutomaticHandoffStop, machine.LastTapeTransportTransition);
                Assert.True(machine.MountedTape!.IsPlaybackPaused);

                Assert.Equal(TapeTransportState.Playing, machine.ToggleTapeTransport());
                machine.ExecuteFrame();
                machine.ExecuteFrame();
                machine.ExecuteFrame();
                Assert.Equal(TapeTransportState.Playing, machine.TapeTransportState);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_AutoStopsTape_WhenLoaderHandsOffToKeyboardOnlyWaitLoop()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                    2168, 60000, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape(
                    "keyboard-handoff.tap",
                    new[] { dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false));

                ushort pc = 0x8000;
                for (int read = 0; read < 96; read++)
                {
                    machine.PokeMemory(pc++, 0xDB); // IN A,(FE)
                    machine.PokeMemory(pc++, 0xFE);
                }
                machine.PokeMemory(pc++, 0xC3); // JP 8100
                machine.PokeMemory(pc++, 0x00);
                machine.PokeMemory(pc, 0x81);

                machine.PokeMemory(0x8100, 0xAF); // XOR A
                machine.PokeMemory(0x8101, 0xDB); // IN A,(FE)
                machine.PokeMemory(0x8102, 0xFE);
                machine.PokeMemory(0x8103, 0x2F); // CPL
                machine.PokeMemory(0x8104, 0xE6); // AND 1F discards EAR bit 6.
                machine.PokeMemory(0x8105, 0x1F);
                machine.PokeMemory(0x8106, 0x28); // JR Z,8100
                machine.PokeMemory(0x8107, 0xF8);
                machine.Cpu.Regs.PC = 0x8000;

                machine.ExecuteFrame();
                Assert.Equal(TapeTransportState.Playing, machine.TapeTransportState);

                machine.ExecuteFrame();
                machine.ExecuteFrame();
                machine.ExecuteFrame();

                Assert.False(machine.Cpu.IsHalted);
                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);
                Assert.True(machine.MountedTape!.IsPlaybackPaused);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_AutoStopsTape_WhenKeyboardLoopForcesEarBitHighWithOr()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                    2168, 60000, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape(
                    "keyboard-or-handoff.tap",
                    new[] { dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false));

                ushort pc = 0x8000;
                for (int read = 0; read < 96; read++)
                {
                    machine.PokeMemory(pc++, 0xDB); // IN A,(FE)
                    machine.PokeMemory(pc++, 0xFE);
                }
                machine.PokeMemory(pc++, 0xC3); // JP 8100
                machine.PokeMemory(pc++, 0x00);
                machine.PokeMemory(pc, 0x81);

                machine.PokeMemory(0x8100, 0x3E); // LD A,00
                machine.PokeMemory(0x8101, 0x00);
                machine.PokeMemory(0x8102, 0xDB); // IN A,(FE)
                machine.PokeMemory(0x8103, 0xFE);
                machine.PokeMemory(0x8104, 0xF6); // OR E0 forces EAR bit 6 high.
                machine.PokeMemory(0x8105, 0xE0);
                machine.PokeMemory(0x8106, 0x3C); // INC A
                machine.PokeMemory(0x8107, 0x28); // JR Z,8100
                machine.PokeMemory(0x8108, 0xF7);
                machine.Cpu.Regs.PC = 0x8000;

                machine.ExecuteFrame();
                Assert.Equal(TapeTransportState.Playing, machine.TapeTransportState);

                machine.ExecuteFrame();
                machine.ExecuteFrame();
                machine.ExecuteFrame();

                Assert.False(machine.Cpu.IsHalted);
                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);
                Assert.Equal(TapeTransportTransition.AutomaticHandoffStop, machine.LastTapeTransportTransition);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_AutoResumesHandoffPause_AfterStableRecognizedRomLoaderReads()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                    2168, 60000, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape(
                    "automatic-resume.tap",
                    new[] { dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false));

                ushort pc = 0x8000;
                for (int read = 0; read < 96; read++)
                {
                    machine.PokeMemory(pc++, 0xDB); // IN A,(FE)
                    machine.PokeMemory(pc++, 0xFE);
                }
                machine.PokeMemory(pc++, 0xC3);
                machine.PokeMemory(pc++, 0x00);
                machine.PokeMemory(pc, 0x81);
                machine.PokeMemory(0x8100, 0xAF); // XOR A
                machine.PokeMemory(0x8101, 0xDB); // IN A,(FE)
                machine.PokeMemory(0x8102, 0xFE);
                machine.PokeMemory(0x8103, 0x2F); // CPL
                machine.PokeMemory(0x8104, 0xE6); // AND 1F ignores EAR.
                machine.PokeMemory(0x8105, 0x1F);
                machine.PokeMemory(0x8106, 0x28);
                machine.PokeMemory(0x8107, 0xF8);
                machine.Cpu.Regs.PC = 0x8000;

                machine.ExecuteFrame();
                machine.ExecuteFrame();
                machine.ExecuteFrame();
                machine.ExecuteFrame();
                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);

                // The CPU callback observes PC after the ROM's IN A,(FE)
                // operand has been fetched.
                machine.Cpu.Regs.PC = 0x0564;
                for (int read = 0; read < 63; read++)
                    machine.DebugReadPort(0x00FE);

                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);
                Assert.True(machine.MountedTape!.IsPlaybackPaused);

                machine.DebugReadPort(0x00FE);

                Assert.Equal(TapeTransportState.Playing, machine.TapeTransportState);
                Assert.Equal(TapeTransportTransition.AutomaticRomResume, machine.LastTapeTransportTransition);
                Assert.False(machine.MountedTape!.IsPlaybackPaused);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_DoesNotAutoResumeHandoffPause_ForGameCodeEarReads()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                    2168, 60000, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape(
                    "automatic-resume.tap",
                    new[] { dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false));

                ushort pc = 0x8000;
                for (int read = 0; read < 96; read++)
                {
                    machine.PokeMemory(pc++, 0xDB);
                    machine.PokeMemory(pc++, 0xFE);
                }
                machine.PokeMemory(pc++, 0xC3);
                machine.PokeMemory(pc++, 0x00);
                machine.PokeMemory(pc, 0x81);
                machine.PokeMemory(0x8100, 0xAF);
                machine.PokeMemory(0x8101, 0xDB);
                machine.PokeMemory(0x8102, 0xFE);
                machine.PokeMemory(0x8103, 0x2F);
                machine.PokeMemory(0x8104, 0xE6);
                machine.PokeMemory(0x8105, 0x1F);
                machine.PokeMemory(0x8106, 0x28);
                machine.PokeMemory(0x8107, 0xF8);
                machine.Cpu.Regs.PC = 0x8000;

                for (int frame = 0; frame < 4; frame++)
                    machine.ExecuteFrame();
                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);

                machine.Cpu.Regs.PC = 0x8002;
                for (int read = 0; read < 128; read++)
                    machine.DebugReadPort(0x00FE);

                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);
                Assert.True(machine.MountedTape!.IsPlaybackPaused);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void Machine_DoesNotAutoResumeUserPause_WhenCodeReadsEar()
        {
            string tempFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape(
                    "manual-pause.tap",
                    new[] { TapeBlock.CreatePureTone(pulseLength: 2168, pulseCount: 60000) }));
                Assert.Equal(TapeTransportState.Stopped, machine.ToggleTapeTransport());

                machine.PokeMemory(0x8000, 0xDB); // IN A,(FE)
                machine.PokeMemory(0x8001, 0xFE);
                machine.PokeMemory(0x8002, 0xE6); // AND 40 observes EAR.
                machine.PokeMemory(0x8003, 0x40);
                machine.PokeMemory(0x8004, 0xC3);
                machine.PokeMemory(0x8005, 0x00);
                machine.PokeMemory(0x8006, 0x80);
                machine.Cpu.Regs.PC = 0x8000;

                machine.ExecuteFrame();

                Assert.Equal(TapeTransportState.Stopped, machine.TapeTransportState);
                Assert.True(machine.MountedTape!.IsPlaybackPaused);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

    }
}
