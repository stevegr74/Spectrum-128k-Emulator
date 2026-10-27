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
        public void MountTap_Exposes_Changing_Ear_Bit_On_Port_Fe()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "mounted.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "EARTEST", dataLength: 4, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0x80, 0x00, 0xFF, 0x55 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapMountResult result = TapLoader.Mount(machine, tapePath);

                Assert.Equal(2, result.TotalBlockCount);
                Assert.True(machine.HasMountedTape);

                bool sawHigh = false;
                bool sawLow = false;
                for (int i = 0; i < 64; i++)
                {
                    bool earHigh = (machine.DebugReadPort(0x00FE) & 0x40) != 0;
                    sawHigh |= earHigh;
                    sawLow |= !earHigh;
                    machine.Cpu.AddTStates(2168);
                }

                Assert.True(sawHigh);
                Assert.True(sawLow);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_EndOfTape_Preserves_FinalEarLevel()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "final-ear",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateSetSignalLevel(false),
                        TapeBlock.CreatePause(1)
                    });
                machine.MountTape(tape);

                object mountedTape = machine.MountedTape!;
                FieldInfo stateField = mountedTape.GetType().GetField("earPlaybackState", BindingFlags.Instance | BindingFlags.NonPublic)!;
                FieldInfo levelField = mountedTape.GetType().GetField("earLevel", BindingFlags.Instance | BindingFlags.NonPublic)!;

                for (int i = 0; i < 10 && !string.Equals(stateField.GetValue(mountedTape)?.ToString(), "Idle", StringComparison.Ordinal); i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal("Idle", stateField.GetValue(mountedTape)?.ToString());

                bool finalLevel = (bool)levelField.GetValue(mountedTape)!;
                bool sampleAtIdle = (machine.DebugReadPort(0x00FE) & 0x40) != 0;
                machine.Cpu.AddTStates(20000);
                bool sampleLater = (machine.DebugReadPort(0x00FE) & 0x40) != 0;

                Assert.Equal(finalLevel, sampleAtIdle);
                Assert.Equal(sampleAtIdle, sampleLater);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_PauseBoundary_Holds_FinalHighLevel_Before_SettlingLow()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "pause-boundary",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0x80 }, 855, 1710, 1, 2),
                        TapeBlock.CreatePureTone(100, 1)
                    },
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                for (int i = 0; i < 64 && GetPrivateField(tape, "earPlaybackState").ToString() != "Pause"; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal("Pause", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.True((bool)GetPrivateField(tape, "earLevel"));
                Assert.Equal(3547, (int)GetPrivateField(tape, "earPulseLengthTStates"));

                MethodInfo advanceEarPulse = typeof(MountedTape).GetMethod(
                    "AdvanceEarPulse",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                advanceEarPulse.Invoke(tape, Array.Empty<object>());

                Assert.Equal("Pause", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.False((bool)GetPrivateField(tape, "earLevel"));
                Assert.Equal(3547, (int)GetPrivateField(tape, "earPulseLengthTStates"));

                advanceEarPulse.Invoke(tape, Array.Empty<object>());

                Assert.Equal("PureTone", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.False((bool)GetPrivateField(tape, "earLevel"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_PauseBoundary_RemainsLow_When_PreviousLevelIsLow()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "pause-termination",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateSetSignalLevel(false),
                        TapeBlock.CreatePause(2),
                        TapeBlock.CreatePureTone(100, 1)
                    },
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                Assert.Equal("Pause", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.False((bool)GetPrivateField(tape, "earLevel"));
                Assert.Equal(7094, (int)GetPrivateField(tape, "earPulseLengthTStates"));

                MethodInfo advanceEarPulse = typeof(MountedTape).GetMethod(
                    "AdvanceEarPulse",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                advanceEarPulse.Invoke(tape, Array.Empty<object>());

                Assert.Equal("PureTone", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.False((bool)GetPrivateField(tape, "earLevel"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapTapeBlocksAndMountRemaining_Preserves_LeadingDataPause_Before_LivePlayback()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10, Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                var blocks = new[]
                {
                    TapeBlock.CreateData(BuildHeaderBlock(0, "BOOT", (ushort)basicProgram.Length, 10, (ushort)basicProgram.Length),
                        2168, 8063, 667, 735, 855, 1710, 8, 0),
                    TapeBlock.CreateData(BuildDataBlock(basicProgram),
                        2168, 3223, 667, 735, 855, 1710, 8, 2000),
                    TapeBlock.CreatePureTone(100, 1)
                };
                var machine = new Spectrum128Machine(tempFolder);

                MethodInfo bootstrap = typeof(TapLoader).GetMethod(
                    "BootstrapTapeBlocksAndMountRemaining",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                bootstrap.Invoke(null, new object[]
                {
                    machine,
                    "bootstrap-pause",
                    blocks,
                    false,
                    1,
                    1,
                    true
                });

                MountedTape tape = Assert.IsType<MountedTape>(machine.MountedTape);
                Assert.Equal("Pause", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.Equal(7093800, (int)GetPrivateField(tape, "earPulseLengthTStates"));
                Assert.False((bool)GetPrivateField(tape, "earLevel"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_NaturalByteStreamCompletion_DoesNotRetainIdleProtectedByteStreamForRomTrap()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                byte[] markerHeader = BuildHeaderBlock(
                    type: 42,
                    fileName: "TYPE42",
                    dataLength: 2,
                    parameter1: 0x8000,
                    parameter2: 0);
                byte[] headerPayload = new byte[17];
                Buffer.BlockCopy(markerHeader, 1, headerPayload, 0, headerPayload.Length);

                var tape = new MountedTape(
                    "protected-byte-stream",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0xE8 }, 855, 1710, 8, 0),
                        TapeBlock.CreateByteStreamData(new byte[] { 0x55, 0xAA }, 855, 1710, 8, 0)
                    },
                    initialBlockIndex: 1,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                MethodInfo applyByteStreamRecordSideEffects = typeof(MountedTape).GetMethod(
                    "ApplyByteStreamRecordSideEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                applyByteStreamRecordSideEffects.Invoke(tape, new object[] { machine, (byte)0x00, headerPayload, false });

                Assert.Equal("Idle", GetPrivateField(tape, "state").ToString());
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));

                for (int i = 0; i < 64 && GetPrivateField(tape, "earPlaybackState").ToString() != "Idle"; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal("Idle", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal("Idle", GetPrivateField(tape, "state").ToString());
                Assert.False((bool)GetPrivateField(tape, "retainedByteStreamTrapAvailable"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_Identifies_ProtectedLiveByteStream_Only_For_NonRom_StreamPlayback()
        {
            var protectedTape = new MountedTape(
                "protected",
                new TapeBlock[]
                {
                    TapeBlock.CreateByteStreamData(new byte[] { 0x12, 0x34, 0x56 }, 855, 1710, 8, 0)
                },
                initialBlockIndex: 0,
                skipCustomHeaderForEarPlayback: false);

            Assert.True(protectedTape.IsActivelyStreamingEarSignal);
            Assert.True(protectedTape.IsStreamingProtectedByteStream);

            var romTape = new MountedTape(
                "rom",
                new TapeBlock[]
                {
                    TapeBlock.CreateData(
                        new byte[] { 0x00, 0xAA, 0x55 },
                        2168,
                        10,
                        667,
                        735,
                        855,
                        1710,
                        8,
                        1000)
                },
                initialBlockIndex: 0,
                skipCustomHeaderForEarPlayback: false);

            Assert.True(romTape.IsActivelyStreamingEarSignal);
            Assert.False(romTape.IsStreamingProtectedByteStream);
        }

        [Fact]
        public void Requires128kTapeLoadMode_Selects128kForGenericBasicHandoffToProtectedStream()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                var blocks = new TapeBlock[]
                {
                    TapeBlock.CreateData(
                        BuildHeaderBlock(0, "BOOT", (ushort)basicProgram.Length, 10, (ushort)basicProgram.Length),
                        2168, 8063, 667, 735, 855, 1710, 8, 1000),
                    TapeBlock.CreateData(
                        BuildDataBlock(basicProgram),
                        2168, 3223, 667, 735, 855, 1710, 8, 1000),
                    TapeBlock.CreateByteStreamData(new byte[] { 0xA5 }, 753, 1506, 8, 0)
                };
                MethodInfo requires128k = typeof(TapLoader).GetMethod(
                    "Requires128kTapeLoadModeForStandardTape",
                    BindingFlags.NonPublic | BindingFlags.Static)!;

                bool use128kMode = (bool)requires128k.Invoke(
                    null,
                    new object[] { new Spectrum128Machine(tempFolder), blocks })!;

                Assert.True(use128kMode);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_ProtectedByteStreamWithTrailingPause_EndsWithoutSyntheticEofTransition()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "protected-eof",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0x55 }, 855, 1710, 8, 1)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                for (int i = 0; i < 64 && tape.IsActivelyDrivingEarLine; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal("Idle", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.False((bool)GetPrivateField(tape, "retainedByteStreamTrapAvailable"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_LongTerminalPause_IsCappedBeforeAutoEject()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "terminal-silence",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0x00 }, 1, 1, 1, 60000)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                _ = tape.ReadEarBit(0);
                _ = tape.ReadEarBit(2);

                Assert.Equal("Pause", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.Equal(3547, (int)GetPrivateField(tape, "earPulseLengthTStates"));
                Assert.Equal(3546900 - 3547, (int)GetPrivateField(tape, "earPauseLowTailTStates"));

                _ = tape.ReadEarBit(3546902);
                Assert.True(tape.HasCompletedPlayback);

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
        public void MountedTape_LongIntermediatePause_PreservesRecordedDuration()
        {
            var tape = new MountedTape(
                "intermediate-silence",
                new TapeBlock[]
                {
                    TapeBlock.CreateByteStreamData(new byte[] { 0x00 }, 1, 1, 1, 60000),
                    TapeBlock.CreatePureTone(100, 1)
                },
                initialBlockIndex: 0,
                skipCustomHeaderForEarPlayback: false);

            _ = tape.ReadEarBit(0);
            _ = tape.ReadEarBit(2);

            Assert.Equal("Pause", GetPrivateField(tape, "earPlaybackState").ToString());
            Assert.Equal(3500, (int)GetPrivateField(tape, "earPulseLengthTStates"));
            Assert.Equal(59999 * 3500, (int)GetPrivateField(tape, "earPauseLowTailTStates"));
        }

        [Fact]
        public void MountedTape_CompletedProtectedEndOfStream_AutoEjectsAfterFinalTransition()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "completed-protected-stream",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0xA5 }, 1, 1, 8, 0)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                for (int i = 0; i < 32 && !tape.HasCompletedPlayback; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.True(tape.HasCompletedPlayback);
                Assert.True(machine.HasMountedTape);

                machine.ExecuteTimeSlice(1);

                Assert.False(machine.HasMountedTape);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_NaturalByteStreamCompletion_RetainsProtectedByteStreamWhenLogicalLoadStateIsActive()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                byte[] markerHeader = BuildHeaderBlock(
                    type: 42,
                    fileName: "TYPE42",
                    dataLength: 2,
                    parameter1: 0x8000,
                    parameter2: 0);
                byte[] headerPayload = new byte[17];
                Buffer.BlockCopy(markerHeader, 1, headerPayload, 0, headerPayload.Length);

                var tape = new MountedTape(
                    "protected-byte-stream",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0xE8 }, 855, 1710, 8, 0),
                        TapeBlock.CreateByteStreamData(new byte[] { 0x55, 0xAA }, 855, 1710, 8, 0)
                    },
                    initialBlockIndex: 1,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                MethodInfo applyByteStreamRecordSideEffects = typeof(MountedTape).GetMethod(
                    "ApplyByteStreamRecordSideEffects",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                applyByteStreamRecordSideEffects.Invoke(tape, new object[] { machine, (byte)0x00, headerPayload, false });

                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));

                for (int i = 0; i < 64 && GetPrivateField(tape, "earPlaybackState").ToString() != "Idle"; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal("Idle", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.True((bool)GetPrivateField(tape, "retainedByteStreamTrapAvailable"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_NaturalByteStreamCompletion_ClearsStaleProtectedByteStreamWhenLivePlaybackHasPassedIt()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "protected-byte-stream",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0x55, 0xAA }, 855, 1710, 8, 0)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "romStreamTrapBlockIndex", 0);
                SetPrivateField(tape, "romStreamTrapByteIndex", 1);
                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "Idle"));

                for (int i = 0; i < 64 && GetPrivateField(tape, "earPlaybackState").ToString() != "Idle"; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal("Idle", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.False((bool)GetPrivateField(tape, "retainedByteStreamTrapAvailable"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomByteStreamTrap_CanConsume_RawByteStreamChunks()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "raw-byte-stream",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0x11, 0x22, 0x33, 0x44 }, 855, 1710, 8, 0)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 2;
                machine.Cpu.Regs.A = 0x02;
                machine.Cpu.Regs.F = 0x01;
                machine.Cpu.Regs.A_ = 0x02;
                machine.Cpu.Regs.F_ = 0x01;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.Equal((byte)0x11, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x22, machine.PeekMemory(0x8001));
                Assert.Equal((ushort)0, machine.Cpu.Regs.DE);
                Assert.Equal((byte)0x01, (byte)(machine.Cpu.Regs.F & 0x01));
                Assert.Equal(2, (int)GetPrivateField(tape, "romStreamTrapByteIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_Uses_AlternateAf_For_ExpectedFlag_And_LoadMode()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "alt-af-raw-byte-stream",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0x11, 0x22 }, 855, 1710, 8, 0)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 2;
                machine.Cpu.Regs.A = 0x20;
                machine.Cpu.Regs.F = 0x00;
                machine.Cpu.Regs.A_ = 0xFF;
                machine.Cpu.Regs.F_ = 0x01;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.Equal((byte)0x11, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x22, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x01, (byte)(machine.Cpu.Regs.F & 0x01));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomSyncLoopTrap_CanConsume_UnstructuredStandardRomBlock_With_PendingContinuation()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "unstructured-standard-sync",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateData(
                            BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                            2168, 3223, 667, 735, 855, 1710, 8, 1000)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));
                machine.SetPendingMountedLoadUsrContinuationResolver(_ => 0x8000, requireUsrReturnAddress: false);

                machine.Cpu.Regs.PC = 0x0574;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0x7F;
                machine.Cpu.Regs.F = 0x00;
                machine.Cpu.Regs.A_ = 0x7F;
                machine.Cpu.Regs.F_ = 0x00;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.Equal((byte)0x11, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x22, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x33, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0x44, machine.PeekMemory(0x8003));
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_UnstructuredStandardBlock_ReturnsToDirectCaller()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "direct-rom-loader",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateData(
                            BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                            2168, 3223, 667, 735, 855, 1710, 8, 1000)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0xF7);
                machine.PokeMemory(0x9001, 0x5D);
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.Equal((ushort)0x5DF7, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x11, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x44, machine.PeekMemory(0x8003));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomSyncLoopTrap_Reclaims_Matching_Active_StandardBlock()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] expectedPayload = { 0x11, 0x22, 0x33, 0x44 };
                TapeBlock activeBlock = TapeBlock.CreateData(
                    BuildDataBlock(expectedPayload),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);
                TapeBlock followingBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x99, 0x88, 0x77, 0x66, 0x55 }),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "active-standard-block",
                    new[] { activeBlock, followingBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "nextBlockIndex", 1);
                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));
                SetPrivateField(tape, "earPlaybackBlockIndex", 0);
                SetPrivateField(tape, "earPlaybackState", Enum.Parse(GetPrivateField(tape, "earPlaybackState").GetType(), "Data"));
                SetPrivateField(tape, "earPlaybackStarted", true);
                SetPrivateField(tape, "earStreamByteIndex", 2);

                machine.Cpu.Regs.PC = 0x0574;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x00);
                machine.PokeMemory(0x9001, 0x80);
                machine.Cpu.Regs.IX = 0x7000;
                machine.Cpu.Regs.DE = (ushort)expectedPayload.Length;
                machine.Cpu.Regs.A = 0x7F;
                machine.Cpu.Regs.F = 0x00;
                machine.Cpu.Regs.A_ = 0xFF;
                machine.Cpu.Regs.F_ = 0x01;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.NotEqual(0, machine.Cpu.Regs.F & 0x01);
                Assert.Equal(expectedPayload, expectedPayload.Select((_, index) =>
                    machine.PeekMemory((ushort)(0x7000 + index))).ToArray());
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal(1, (int)GetPrivateField(tape, "earPlaybackBlockIndex"));
                Assert.False(tape.IsPlaybackPaused);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_Reclaims_ImmediatelyCompleted_StandardBlock()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] expectedPayload = { 0x11, 0x22, 0x33, 0x44 };
                TapeBlock completedBlock = TapeBlock.CreateData(
                    BuildDataBlock(expectedPayload, flag: 0x81),
                    0, 0, 0, 0, 1, 1, 8, 0);
                TapeBlock followingBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x99 }, flag: 0xAA),
                    2168, 60000, 667, 735, 855, 1710, 8, 1000);
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "completed-standard-block",
                    new[] { completedBlock, followingBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                _ = tape.ReadEarBit(0);
                _ = tape.ReadEarBit(200);
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal(1, (int)GetPrivateField(tape, "earPlaybackBlockIndex"));
                MountedTape.QuickState savedState = tape.CaptureQuickState();
                Assert.Equal(0, savedState.CompletedRomLoadableBlockIndex);
                tape.RestoreQuickState(savedState);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x00);
                machine.PokeMemory(0x9001, 0x80);
                machine.Cpu.Regs.IX = 0x7000;
                machine.Cpu.Regs.DE = (ushort)expectedPayload.Length;
                machine.Cpu.Regs.A = 0x02;
                machine.Cpu.Regs.F = 0x00;
                machine.Cpu.Regs.A_ = 0x81;
                machine.Cpu.Regs.F_ = 0x01;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.NotEqual(0, machine.Cpu.Regs.F & 0x01);
                Assert.Equal(expectedPayload, expectedPayload.Select((_, index) =>
                    machine.PeekMemory((ushort)(0x7000 + index))).ToArray());
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal(-1, (int)GetPrivateField(tape, "completedRomLoadableBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_Reclaims_ActiveBlock_BeforeReportingFlagMismatch()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] skippedPayload = { 0x11, 0x22, 0x33, 0x44 };
                TapeBlock activeBlock = TapeBlock.CreateData(
                    BuildDataBlock(skippedPayload, flag: 0x0A),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);
                TapeBlock followingBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x99, 0x88, 0x77 }, flag: 0x03),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "active-flag-mismatch",
                    new[] { activeBlock, followingBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "nextBlockIndex", 1);
                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));
                SetPrivateField(tape, "earPlaybackBlockIndex", 0);
                SetPrivateField(tape, "earPlaybackState", Enum.Parse(GetPrivateField(tape, "earPlaybackState").GetType(), "Data"));
                SetPrivateField(tape, "earPlaybackStarted", true);
                SetPrivateField(tape, "earStreamByteIndex", 2);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x00);
                machine.PokeMemory(0x9001, 0x80);
                machine.Cpu.Regs.IX = 0x7000;
                machine.Cpu.Regs.DE = (ushort)skippedPayload.Length;
                machine.Cpu.Regs.A = 0x22;
                machine.Cpu.Regs.F = 0x00;
                machine.Cpu.Regs.A_ = 0x02;
                machine.Cpu.Regs.F_ = 0x00;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.Equal(0, machine.Cpu.Regs.F & 0x01);
                Assert.All(skippedPayload.Select((_, index) =>
                    machine.PeekMemory((ushort)(0x7000 + index))), value => Assert.Equal(0, value));
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal(1, (int)GetPrivateField(tape, "earPlaybackBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_Uses_Nonstandard_Flag_From_AlternateAccumulator()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] expectedPayload = { 0x11, 0x22, 0x33, 0x44 };
                TapeBlock block = TapeBlock.CreateData(
                    BuildDataBlock(expectedPayload, flag: 0x01),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "nonstandard-flag",
                    new[] { block },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x00);
                machine.PokeMemory(0x9001, 0x80);
                machine.Cpu.Regs.IX = 0x7000;
                machine.Cpu.Regs.DE = (ushort)expectedPayload.Length;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x00;
                machine.Cpu.Regs.A_ = 0x01;
                machine.Cpu.Regs.F_ = 0x01;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.NotEqual(0, machine.Cpu.Regs.F & 0x01);
                Assert.Equal(expectedPayload, expectedPayload.Select((_, index) =>
                    machine.PeekMemory((ushort)(0x7000 + index))).ToArray());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_Prefers_AlternateCarry_WhenBothAccumulatorsMatchFlag()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] expectedPayload = { 0x11, 0x22, 0x33, 0x44 };
                TapeBlock block = TapeBlock.CreateData(
                    BuildDataBlock(expectedPayload, flag: 0x02),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "alternate-carry",
                    new[] { block },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x00);
                machine.PokeMemory(0x9001, 0x80);
                machine.Cpu.Regs.IX = 0x7000;
                machine.Cpu.Regs.DE = (ushort)expectedPayload.Length;
                machine.Cpu.Regs.A = 0x02;
                machine.Cpu.Regs.F = 0x00;
                machine.Cpu.Regs.A_ = 0x02;
                machine.Cpu.Regs.F_ = 0x01;

                bool handled = tape.TryHandleRomLoadTrap(machine, machine.Cpu);

                Assert.True(handled);
                Assert.NotEqual(0, machine.Cpu.Regs.F & 0x01);
                Assert.Equal(expectedPayload, expectedPayload.Select((_, index) =>
                    machine.PeekMemory((ushort)(0x7000 + index))).ToArray());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_StructuredBasicSideEffects_Resume_Original_Caller()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(244), Ascii("23624"), Comma(), Ascii("5"), NumberMarker(5)));

                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(
                        type: 0,
                        fileName: "BASRET",
                        dataLength: (ushort)basicProgram.Length,
                        parameter1: 10,
                        parameter2: (ushort)basicProgram.Length),
                    2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(basicProgram),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod(
                    "ParseHeaderInfo",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "structured-basic-return",
                    new[] { headerBlock, dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "nextBlockIndex", 1);
                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));
                SetPrivateField(tape, "expectedDataLength", (int?)basicProgram.Length);
                SetPrivateField(tape, "pendingHeaderName", "BASRET");
                SetPrivateField(tape, "pendingHeaderInfo", header);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x34);
                machine.PokeMemory(0x9001, 0x12);
                machine.Cpu.Regs.IX = 23755;
                machine.Cpu.Regs.DE = (ushort)basicProgram.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                machine.Cpu.Regs.A_ = 0xFF;
                machine.Cpu.Regs.F_ = 0x01;

                Assert.True(tape.TryHandleRomLoadTrap(machine, machine.Cpu));
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
                Assert.Equal((byte)5, machine.PeekMemory(23624));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_ProtectedStructuredBasic_RemainsOnExactEarPath()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] protectedProgram = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("23662"), Comma(),
                        Token(190), Ascii("23618"), NumberMarker(23618)));

                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(
                        type: 0,
                        fileName: "PROTECTED",
                        dataLength: (ushort)protectedProgram.Length,
                        parameter1: 0,
                        parameter2: (ushort)protectedProgram.Length),
                    2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(protectedProgram),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod(
                    "ParseHeaderInfo",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "protected-structured-basic",
                    new[] { headerBlock, dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "nextBlockIndex", 1);
                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));
                SetPrivateField(tape, "expectedDataLength", (int?)protectedProgram.Length);
                SetPrivateField(tape, "pendingHeaderName", "PROTECTED");
                SetPrivateField(tape, "pendingHeaderInfo", header);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.IX = 23755;
                machine.Cpu.Regs.DE = (ushort)protectedProgram.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                Assert.False(tape.TryHandleRomLoadTrap(machine, machine.Cpu));
                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal((byte)0, machine.PeekMemory(23755));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_RomTrap_UnsupportedAutoStartBasic_Uses_Clean_Rom_Handoff()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(241), Ascii("a=1"), NumberMarker(1)));

                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(
                        type: 0,
                        fileName: "ROMSTART",
                        dataLength: (ushort)basicProgram.Length,
                        parameter1: 0,
                        parameter2: (ushort)basicProgram.Length),
                    2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(basicProgram),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod(
                    "ParseHeaderInfo",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "unsupported-autostart-basic",
                    new[] { headerBlock, dataBlock },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "nextBlockIndex", 1);
                SetPrivateField(tape, "state", Enum.Parse(GetPrivateField(tape, "state").GetType(), "ExpectData"));
                SetPrivateField(tape, "expectedDataLength", (int?)basicProgram.Length);
                SetPrivateField(tape, "pendingHeaderName", "ROMSTART");
                SetPrivateField(tape, "pendingHeaderInfo", header);

                machine.PokeMemory(23613, 0x00);
                machine.PokeMemory(23614, 0x90);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x8F00;
                machine.PokeMemory(0x8F00, 0x34);
                machine.PokeMemory(0x8F01, 0x12);
                machine.Cpu.Regs.IX = 23755;
                machine.Cpu.Regs.DE = (ushort)basicProgram.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                Assert.True(tape.TryHandleRomLoadTrap(machine, machine.Cpu));
                Assert.Equal((ushort)0x1B9E, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x9000, machine.Cpu.Regs.SP);
                Assert.Equal((byte)0x03, machine.PeekMemory(0x9000));
                Assert.Equal((byte)0x13, machine.PeekMemory(0x9001));
                Assert.Equal((ushort)0, ReadWord(machine, 23618));
                Assert.Equal((ushort)23755, ReadWord(machine, 23635));
                Assert.Equal((ushort)(23755 + basicProgram.Length), ReadWord(machine, 23627));
                Assert.Equal((byte)0xFF, machine.PeekMemory(23610));
                Assert.True(machine.Cpu.IFF1);
                Assert.True(machine.Cpu.IFF2);
                Assert.Equal(2, (int)GetPrivateField(tape, "nextBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_LiveByteStreamProgress_Clears_StaleRomTrapCursor_WhenPlaybackHasPassedIt()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "stale-rom-trap-cursor",
                    new TapeBlock[]
                    {
                        TapeBlock.CreateByteStreamData(new byte[] { 0x11, 0x22, 0x33, 0x44 }, 855, 1710, 8, 0)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                SetPrivateField(tape, "earPlaybackBlockIndex", 0);
                SetPrivateField(tape, "earStreamByteIndex", 4);
                SetPrivateField(tape, "romStreamTrapBlockIndex", 0);
                SetPrivateField(tape, "romStreamTrapByteIndex", 1);
                SetPrivateField(tape, "retainedByteStreamTrapAvailable", true);

                MethodInfo syncMethod = typeof(MountedTape).GetMethod(
                    "SyncRomByteStreamTrapToEarProgress",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                syncMethod.Invoke(tape, Array.Empty<object>());

                Assert.Equal(-1, (int)GetPrivateField(tape, "romStreamTrapBlockIndex"));
                Assert.Equal(0, (int)GetPrivateField(tape, "romStreamTrapByteIndex"));
                Assert.False((bool)GetPrivateField(tape, "retainedByteStreamTrapAvailable"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Loads_Header_Block_And_Returns_To_Rom()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "romload.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "ROMTEST", dataLength: 5, parameter1: 0x8000, parameter2: 0x2222),
                    BuildDataBlock(new byte[] { 1, 2, 3, 4, 5 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                machine.Cpu.Regs.A_ = 0x00;
                machine.Cpu.Regs.F_ = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x9002, machine.Cpu.Regs.SP);
                Assert.Equal((ushort)0, machine.Cpu.Regs.DE);
                Assert.Equal((ushort)(0x8000 + 17), machine.Cpu.Regs.IX);
                Assert.Equal((byte)'R', machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x01, (byte)(machine.Cpu.Regs.F & 0x01));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Loads_BasicProgram_Data_With_BasicSideEffects()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "basic-romload.tap");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10, Token(244), Ascii("23624"), Comma(), Ascii("7"), NumberMarker(7)));
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BASIC2", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length),
                    BuildDataBlock(basicProgram));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;

                Assert.True(machine.TryServiceTapeTrap());
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);

                machine.PokeMemory(0x9002, 0x3F);
                machine.PokeMemory(0x9003, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9002;
                machine.Cpu.Regs.IX = 23755;
                machine.Cpu.Regs.DE = (ushort)basicProgram.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                Assert.True(machine.TryServiceTapeTrap());
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)23755, ReadWord(machine, 23635));
                Assert.Equal((ushort)(23755 + basicProgram.Length), ReadWord(machine, 23627));
                Assert.Equal((ushort)(23755 + basicProgram.Length + 1), ReadWord(machine, 23641));
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.Equal(basicProgram[0], machine.PeekMemory(23755));
                Assert.Equal((byte)7, machine.PeekMemory(23624));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Consumes_Raw_Standard_Stream_Without_Header_Context()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] rawStreamPayload = Enumerable.Repeat((byte)0xAA, 32).ToArray();
                TapeBlock rawStream = TapeBlock.CreateData(
                    BuildDataBlock(rawStreamPayload),
                    2168,
                    3223,
                    667,
                    735,
                    855,
                    1710,
                    8,
                    1000);

                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("raw-standard.tap", new[] { rawStream }));
                machine.SetPendingMountedLoadUsrContinuationResolver(_ => 0x8000, requireUsrReturnAddress: true);

                machine.PokeMemory(0x9000, 0x34);
                machine.PokeMemory(0x9001, 0x12);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)rawStreamPayload.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                for (int i = 0; i < rawStreamPayload.Length; i++)
                    Assert.Equal(rawStreamPayload[i], machine.PeekMemory((ushort)(0x8000 + i)));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_CanBeDisabled_ForNativeRomPlayback()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] payload = Enumerable.Repeat((byte)0xAA, 32).ToArray();
                TapeBlock rawStream = TapeBlock.CreateData(
                    BuildDataBlock(payload),
                    2168,
                    3223,
                    667,
                    735,
                    855,
                    1710,
                    8,
                    1000);

                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape(
                    "native-rom.tap",
                    new[] { rawStream },
                    allowRomLoadTraps: false));

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)payload.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                Assert.False(machine.TryServiceTapeTrap());
                Assert.Equal((ushort)0x056B, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_BootstrapLoad_Preserves_DataPause_For_RuntimeHandoff()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10, Token(244), Ascii("23624"), Comma(), Ascii("7"), NumberMarker(7)));
                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(type: 0, fileName: "STAGE2", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length),
                    2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(basicProgram),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);
                TapeBlock remainder = TapeBlock.CreatePureTone(2168, 32);

                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape("bootstrap.tap", new[] { headerBlock, dataBlock, remainder });

                BootstrapTapeLoadResult result = tape.TryConsumeBootstrapLoad(machine);

                Assert.True(result.Success);
                ulong expectedTStates =
                    EstimateTapeBlockDurationTStatesForTest(headerBlock) +
                    EstimateTapeBlockDurationTStatesForTest(dataBlock) -
                    ((ulong)dataBlock.PauseAfterBlockMs * 3500UL);
                Assert.Equal(expectedTStates, machine.Cpu.TStates);
                Assert.Equal("Pause", GetPrivateField(tape, "earPlaybackState").ToString());
                Assert.Equal(1000 * 3500, (int)GetPrivateField(tape, "earPulseLengthTStates"));
                Assert.Equal(2, (int)GetPrivateField(tape, "earPlaybackBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_FinalBootstrapLoad_MarksPlaybackCompleteAndAutoEjects()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10, Token(244), Ascii("23624"), Comma(), Ascii("7"), NumberMarker(7)));
                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(type: 0, fileName: "FINAL", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length),
                    2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(basicProgram),
                    2168, 3223, 667, 735, 855, 1710, 8, 1000);

                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape("final-bootstrap.tap", new[] { headerBlock, dataBlock });
                machine.MountTape(tape);

                BootstrapTapeLoadResult result = tape.TryConsumeBootstrapLoad(machine);

                Assert.True(result.Success);
                Assert.False(tape.HasMoreBlocks);
                Assert.True(tape.HasCompletedPlayback);

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
        public void LoadTap_Rejects_Data_Block_Without_Header()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "bad.tap");

            try
            {
                File.WriteAllBytes(tapePath, BuildTap(BuildDataBlock(new byte[] { 1, 2, 3, 4 })));

                var machine = new Spectrum128Machine(tempFolder);

                Assert.Throws<InvalidOperationException>(() => TapLoader.Load(machine, tapePath));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapTap_Mounts_Nonstandard_Remainder_Playback_From_Data_Block()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "bootstrap-nonstandard.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: 4, parameter1: 32768, parameter2: 4),
                    BuildDataBlock(new byte[] { 0, 0, 0, 0 }),
                    BuildHeaderBlock(type: 42, fileName: "FAST", dataLength: 1, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0x99 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                MountedTape mountedTape = Assert.IsType<MountedTape>(machine.MountedTape);
                int playbackBlockIndex = (int)GetPrivateField(mountedTape, "earPlaybackBlockIndex");
                int nextBlockIndex = (int)GetPrivateField(mountedTape, "nextBlockIndex");

                Assert.Equal(2, nextBlockIndex);
                Assert.Equal(3, playbackBlockIndex);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }


        [Fact]
        public void MountedTap_RomTrap_Loads_Header_Then_Data_Sequentially()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "sequence.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "ONE", dataLength: 3, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 1, 2, 3 }),
                    BuildHeaderBlock(type: 3, fileName: "TWO", dataLength: 2, parameter1: 0x8100, parameter2: 0),
                    BuildDataBlock(new byte[] { 4, 5 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8100;
                machine.Cpu.Regs.DE = 3;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8200;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8300;
                machine.Cpu.Regs.DE = 2;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                Assert.Equal((byte)1, machine.PeekMemory(0x8100));
                Assert.Equal((byte)3, machine.PeekMemory(0x8102));
                Assert.Equal((byte)4, machine.PeekMemory(0x8300));
                Assert.Equal((byte)5, machine.PeekMemory(0x8301));
                Assert.False(machine.HasMountedTape && machine.MountedTape!.HasRemainingBlocks);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_Header_With_Mismatched_Data_Length_Throws()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "badsequence.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "BAD", dataLength: 5, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 1, 2, 3, 4 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8100;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                Assert.Throws<InvalidOperationException>(() => machine.TryServiceTapeTrap());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }



        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Loads_Leading_Basic_And_Starts_Tape_After_Consumed_Blocks()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "bootstrap.tap");

            try
            {
                byte[] basicLoader = new byte[] { 0x0A, 0x00, 0x02, 0x00, 0xF7, 0x0D };
                byte[] codeLoader = new byte[] { 0x3E, 0x42, 0x32, 0x00, 0x90 };
                byte[] trailingData = new byte[] { 0x44, 0x55, 0x66 };
                byte[] header = BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: 12, parameter1: 10, parameter2: 12);
                byte[] tap = BuildTap(
                    header,
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: (ushort)codeLoader.Length, parameter1: 0x9000, parameter2: 0),
                    BuildDataBlock(codeLoader),
                    BuildHeaderBlock(type: 3, fileName: "TRAIL", dataLength: (ushort)trailingData.Length, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(trailingData));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(6, result.TotalBlockCount);
                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal("BOOT", result.AutoStartFileName);
                Assert.True(machine.HasMountedTape);
                Assert.True(machine.MountedTape!.HasRemainingBlocks);
                Assert.Equal((byte)0x0A, machine.PeekMemory(23755));
                Assert.Equal((byte)0xF7, machine.PeekMemory(23759));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x9000));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x9001));
                Assert.Equal((ushort)10, ReadWord(machine, 23618));

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0xA000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x9000;
                machine.Cpu.Regs.DE = (ushort)codeLoader.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)trailingData.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                Assert.True(machine.TryServiceTapeTrap());
                Assert.Equal((byte)0x44, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x55, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x66, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0x3E, machine.PeekMemory(0x9000));
                Assert.Equal((byte)0x42, machine.PeekMemory(0x9001));
                Assert.False(machine.MountedTape!.HasRemainingBlocks);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_WithPendingUsrContinuation_Returns_To_Caller_Path()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "pendingusr.tap");

            try
            {
                byte[] code = new byte[] { 0x11, 0x22, 0x33, 0x44 };
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: (ushort)code.Length, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(code));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);
                machine.SetPendingMountedLoadUsrContinuation(0x9000);

                machine.PokeMemory(0x9000, 0x34);
                machine.PokeMemory(0x9001, 0x12);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0xA000;
                machine.Cpu.Regs.DE = 17;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;

                Assert.True(machine.TryServiceTapeTrap());
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Consumes_Record_From_ByteStream_Remainder()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "protected-remainder",
                    new[]
                    {
                        TapeBlock.CreateMetadata(),
                        TapeBlock.CreatePureTone(2168, 32),
                        TapeBlock.CreatePulseSequence(new[] { 855, 1710 }),
                        TapeBlock.CreateByteStreamData(new byte[] { 0xFF, 0x55, 0xAA }, 855, 1710, 8, 250)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 1;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                MountedTape mountedTape = Assert.IsType<MountedTape>(machine.MountedTape);
                SetPrivateField(mountedTape, "earPlaybackBlockIndex", 3);
                SetPrivateField(mountedTape, "earPlaybackStarted", true);
                SetPrivateField(
                    mountedTape,
                    "earPlaybackState",
                    Enum.Parse(GetPrivateField(mountedTape, "earPlaybackState").GetType(), "Data"));
                SetPrivateField(
                    mountedTape,
                    "state",
                    Enum.Parse(GetPrivateField(mountedTape, "state").GetType(), "Idle"));

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((byte)0x55, machine.PeekMemory(0x8000));
                Assert.Equal(4, (int)GetPrivateField(mountedTape, "nextBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }


        [Fact]
        public void MountedTap_RomTrap_Aligns_To_First_Matching_Record_In_ByteStream_Remainder()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "protected-remainder",
                    new[]
                    {
                        TapeBlock.CreateMetadata(),
                        TapeBlock.CreatePureTone(2168, 32),
                        TapeBlock.CreatePulseSequence(new[] { 855, 1710 }),
                        TapeBlock.CreateByteStreamData(new byte[] { 0x40, 0x40, 0x76, 0xFF, 0x55, 0xAA }, 855, 1710, 8, 250)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 1;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                MountedTape mountedTape = Assert.IsType<MountedTape>(machine.MountedTape);
                SetPrivateField(mountedTape, "earPlaybackBlockIndex", 3);
                SetPrivateField(mountedTape, "earPlaybackStarted", true);
                SetPrivateField(
                    mountedTape,
                    "earPlaybackState",
                    Enum.Parse(GetPrivateField(mountedTape, "earPlaybackState").GetType(), "Data"));
                SetPrivateField(
                    mountedTape,
                    "state",
                    Enum.Parse(GetPrivateField(mountedTape, "state").GetType(), "Idle"));

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((byte)0x55, machine.PeekMemory(0x8000));
                Assert.Equal(4, (int)GetPrivateField(mountedTape, "nextBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Does_Not_Consume_SingleByte_Stream_Remainder()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "protected-remainder",
                    new[]
                    {
                        TapeBlock.CreateMetadata(),
                        TapeBlock.CreatePureTone(2168, 32),
                        TapeBlock.CreatePulseSequence(new[] { 855, 1710 }),
                        TapeBlock.CreateByteStreamData(new byte[] { 0xAA }, 855, 1710, 8, 250)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 1;
                machine.Cpu.Regs.A = 0xAA;
                machine.Cpu.Regs.F = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.False(handled);
                MountedTape mountedTape = Assert.IsType<MountedTape>(machine.MountedTape);
                Assert.Equal(1, (int)GetPrivateField(mountedTape, "nextBlockIndex"));
                Assert.Equal(1, (int)GetPrivateField(mountedTape, "earPlaybackBlockIndex"));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_FromRamCaller_LoadsBasicBlockAsRawBytesWithoutBasicSideEffects()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] rawBasicImage = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "ram-basic-load",
                    new[]
                    {
                        TapeBlock.CreateData(BuildHeaderBlock(type: 0, fileName: "BASIC", dataLength: (ushort)rawBasicImage.Length, parameter1: 10, parameter2: (ushort)rawBasicImage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000),
                        TapeBlock.CreateData(BuildDataBlock(rawBasicImage), 2168, 3223, 667, 735, 855, 1710, 8, 1000)
                    },
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                ushort beforeProg = ReadWord(machine, 23635);
                ushort beforeVars = ReadWord(machine, 23627);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x00);
                machine.PokeMemory(0x9001, 0x80);
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)rawBasicImage.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)rawBasicImage.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                Assert.Equal((byte)0xDE, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0xAD, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0xBE, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0xEF, machine.PeekMemory(0x8003));
                Assert.Equal(beforeProg, ReadWord(machine, 23635));
                Assert.Equal(beforeVars, ReadWord(machine, 23627));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_FromRomCaller_LoadsBasicBlockWithStructuredSideEffects()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicProgram = new byte[] { 0x0A, 0x00, 0x02, 0x00, 0xF7, 0x0D };
                var machine = new Spectrum128Machine(tempFolder);
                var tape = new MountedTape(
                    "rom-basic-load",
                    new[]
                    {
                        TapeBlock.CreateData(BuildHeaderBlock(type: 0, fileName: "BASIC", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000),
                        TapeBlock.CreateData(BuildDataBlock(basicProgram), 2168, 3223, 667, 735, 855, 1710, 8, 1000)
                    },
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)basicProgram.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = (ushort)basicProgram.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                Assert.Equal((byte)0x0A, machine.PeekMemory(23755));
                Assert.Equal((ushort)23755, ReadWord(machine, 23635));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

    }
}
