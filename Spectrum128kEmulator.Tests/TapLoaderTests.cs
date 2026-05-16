using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class TapLoaderTests
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
        public void LoadTap_CodeBlock_Writes_Bytes_To_Target_Address()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "code.tap");

            try
            {
                byte[] code = new byte[] { 0x3E, 0x2A, 0x32, 0x00, 0x80 };
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "CODEDEMO", dataLength: (ushort)code.Length, parameter1: 0x8000, parameter2: 32768),
                    BuildDataBlock(code));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoadResult result = TapLoader.Load(machine, tapePath);

                Assert.Equal(2, result.TotalBlockCount);
                Assert.Equal(1, result.LoadedBlockCount);
                Assert.Equal((byte)0x3E, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x2A, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x32, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8003));
                Assert.Equal((byte)0x80, machine.PeekMemory(0x8004));
                Assert.Equal((ushort)0x1555, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0xFF58, machine.Cpu.Regs.SP);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.True(machine.PagingLocked);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadTap_BasicProgram_Loads_Program_And_Updates_System_Variables()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "basic.tap");

            try
            {
                byte[] basicAndVariables = new byte[]
                {
                    0x0A, 0x00, 0x04, 0x00, 0xF5, 0x0D,
                    0x14, 0x00, 0x04, 0x00, 0xF7, 0x0D,
                    0x80, 0xAA
                };

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BASICDEMO", dataLength: (ushort)basicAndVariables.Length, parameter1: 10, parameter2: 12),
                    BuildDataBlock(basicAndVariables));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoadResult result = TapLoader.Load(machine, tapePath);

                Assert.Equal("BASICDEMO", result.AutoStartFileName);
                Assert.Equal((byte)0x0A, machine.PeekMemory(23755));
                Assert.Equal((byte)0xF7, machine.PeekMemory(23765));
                Assert.Equal((byte)0xAA, machine.PeekMemory(23768));
                Assert.Equal((ushort)23755, ReadWord(machine, 23635));
                Assert.Equal((ushort)(23755 + 12), ReadWord(machine, 23627));
                Assert.Equal((ushort)(23755 + basicAndVariables.Length), ReadWord(machine, 23641));
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.Equal((byte)0, machine.PeekMemory(23620));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

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
                Assert.Equal((ushort)(23755 + basicProgram.Length), ReadWord(machine, 23641));
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
        public void MountedTap_RomTrap_Mismatch_Resets_Carry()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "rommismatch.tap");

            try
            {
                byte[] tap = BuildTap(BuildDataBlock(new byte[] { 1, 2, 3, 4 }));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 99;
                machine.Cpu.Regs.A = 0x00;
                machine.Cpu.Regs.F = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x00, (byte)(machine.Cpu.Regs.F & 0x01));
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

                object mountedTape = GetPrivateField(machine, "mountedTape");
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
        public void MountedTap_Reset_Rewinds_Block_Sequence()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "reset.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildDataBlock(new byte[] { 9, 8, 7 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 3;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());
                Assert.False(machine.MountedTape!.HasRemainingBlocks);

                machine.MountedTape!.Reset();
                Assert.True(machine.MountedTape!.HasRemainingBlocks);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8100;
                machine.Cpu.Regs.DE = 3;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                Assert.True(machine.TryServiceTapeTrap());

                Assert.Equal((byte)9, machine.PeekMemory(0x8100));
                Assert.Equal((byte)7, machine.PeekMemory(0x8102));
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
        public void MountedTap_RomTrap_Verify_Match_Sets_Carry_Without_Writing()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "verifymatch.tap");

            try
            {
                byte[] tap = BuildTap(BuildDataBlock(new byte[] { 0x10, 0x20, 0x30, 0x40 }));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.PokeMemory(0x8000, 0x10);
                machine.PokeMemory(0x8001, 0x20);
                machine.PokeMemory(0x8002, 0x30);
                machine.PokeMemory(0x8003, 0x40);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x00;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x01, (byte)(machine.Cpu.Regs.F & 0x01));
                Assert.Equal((byte)0x10, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x40, machine.PeekMemory(0x8003));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTap_RomTrap_Verify_Mismatch_Resets_Carry()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "verifymismatch.tap");

            try
            {
                byte[] tap = BuildTap(BuildDataBlock(new byte[] { 0x10, 0x20, 0x30, 0x40 }));
                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapLoader.Mount(machine, tapePath);

                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.PokeMemory(0x8000, 0x10);
                machine.PokeMemory(0x8001, 0x20);
                machine.PokeMemory(0x8002, 0x31);
                machine.PokeMemory(0x8003, 0x40);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.Cpu.Regs.IX = 0x8000;
                machine.Cpu.Regs.DE = 4;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x00;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
                Assert.Equal((ushort)0x053F, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x00, (byte)(machine.Cpu.Regs.F & 0x01));
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
                    BuildDataBlock(trailingData));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(5, result.TotalBlockCount);
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
                machine.Cpu.Regs.DE = (ushort)trailingData.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;

                bool handled = machine.TryServiceTapeTrap();

                Assert.True(handled);
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
                object mountedTape = GetPrivateField(machine, "mountedTape");
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
                object mountedTape = GetPrivateField(machine, "mountedTape");
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
                object mountedTape = GetPrivateField(machine, "mountedTape");
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

        [Fact]
        public void CreateExecutionPlan_Classifies_Standard_CustomAndMounted_Tapes()
        {
            string tempFolder = CreateTempRoms();
            Type tapLoaderType = typeof(TapLoader);
            MethodInfo parseBlocks = tapLoaderType.GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo createPlan = tapLoaderType.GetMethod("CreateExecutionPlan", BindingFlags.NonPublic | BindingFlags.Static)!;
            Type planType = tapLoaderType.Assembly.GetType("Spectrum128kEmulator.Tap.TapeLoadPlan")!;
            var strategyProperty = planType.GetProperty("Strategy")!;
            try
            {
                byte[] standardTape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "STD", dataLength: 4, parameter1: 10, parameter2: 4),
                    BuildDataBlock(new byte[] { 1, 2, 3, 4 }),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 2, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0xAA, 0xBB }));
                var standardBlocks = parseBlocks.Invoke(null, new object[] { standardTape })!;
                var standardMachine = new Spectrum128Machine(tempFolder);
                object standardPlan = createPlan.Invoke(null, new[] { standardMachine, standardBlocks })!;
                Assert.Equal("FullFakeLoad", strategyProperty.GetValue(standardPlan)!.ToString());

                byte[] romDrivenProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("0"), NumberMarker(0),
                        Colon(),
                        Token(239)));
                byte[] protectedBasicStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("5"), NumberMarker(5)));
                byte[] opaqueBasicStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(242), Ascii("0"), NumberMarker(0)));
                byte[] romDrivenTape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)romDrivenProgram.Length, parameter1: 10, parameter2: (ushort)romDrivenProgram.Length),
                    BuildDataBlock(romDrivenProgram),
                    BuildHeaderBlock(type: 0, fileName: "STAGE2", dataLength: (ushort)opaqueBasicStage.Length, parameter1: 0, parameter2: (ushort)opaqueBasicStage.Length),
                    BuildDataBlock(opaqueBasicStage));
                var romDrivenBlocks = parseBlocks.Invoke(null, new object[] { romDrivenTape })!;
                var romDrivenMachine = new Spectrum128Machine(tempFolder);
                object romDrivenPlan = createPlan.Invoke(null, new[] { romDrivenMachine, romDrivenBlocks })!;
                Assert.Equal("RomBootstrapMounted", strategyProperty.GetValue(romDrivenPlan)!.ToString());

                byte[] hybridProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("0"), NumberMarker(0),
                        Colon(),
                        Token(239)));
                byte[] hybridTape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)hybridProgram.Length, parameter1: 10, parameter2: (ushort)hybridProgram.Length),
                    BuildDataBlock(hybridProgram),
                    BuildHeaderBlock(type: 3, fileName: "FAST", dataLength: 1, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0x99 }));
                var hybridBlocks = parseBlocks.Invoke(null, new object[] { hybridTape })!;
                var hybridMachine = new Spectrum128Machine(tempFolder);
                object hybridPlan = createPlan.Invoke(null, new[] { hybridMachine, hybridBlocks })!;
                Assert.Equal("BootstrapHybrid", strategyProperty.GetValue(hybridPlan)!.ToString());

                byte[] protectedHybridTape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)romDrivenProgram.Length, parameter1: 10, parameter2: (ushort)romDrivenProgram.Length),
                    BuildDataBlock(romDrivenProgram),
                    BuildHeaderBlock(type: 0, fileName: "STAGE2", dataLength: (ushort)protectedBasicStage.Length, parameter1: 0, parameter2: (ushort)protectedBasicStage.Length),
                    BuildDataBlock(protectedBasicStage));
                var protectedHybridBlocks = parseBlocks.Invoke(null, new object[] { protectedHybridTape })!;
                var protectedHybridMachine = new Spectrum128Machine(tempFolder);
                object protectedHybridPlan = createPlan.Invoke(null, new[] { protectedHybridMachine, protectedHybridBlocks })!;
                Assert.Equal("BootstrapHybrid", strategyProperty.GetValue(protectedHybridPlan)!.ToString());

                byte[] mountedOnlyTape = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 2, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0xAA, 0xBB }));
                var mountedBlocks = parseBlocks.Invoke(null, new object[] { mountedOnlyTape })!;
                var mountedMachine = new Spectrum128Machine(tempFolder);
                object mountedPlan = createPlan.Invoke(null, new[] { mountedMachine, mountedBlocks })!;
                Assert.Equal("MountedRealtime", strategyProperty.GetValue(mountedPlan)!.ToString());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadLeadingBasicProgramAndMountRemainingForRomAutoStart_Mounts_Second_Basic_Stage_For_Rom_Driven_Autostart()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] firstStage = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("0"), NumberMarker(0),
                        Colon(),
                        Token(239)));
                byte[] secondStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("5"), NumberMarker(5)));

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)firstStage.Length, parameter1: 10, parameter2: (ushort)firstStage.Length),
                    BuildDataBlock(firstStage),
                    BuildHeaderBlock(type: 0, fileName: "STAGE2", dataLength: (ushort)secondStage.Length, parameter1: 0, parameter2: (ushort)secondStage.Length),
                    BuildDataBlock(secondStage));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo parseBlocks = typeof(TapLoader).GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadRomBootstrap = typeof(TapLoader).GetMethod("LoadLeadingBasicProgramAndMountRemainingForRomAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;
                IReadOnlyList<TapeBlock> blocks = (IReadOnlyList<TapeBlock>)parseBlocks.Invoke(null, new object[] { tap })!;
                TapBootstrapResult result = (TapBootstrapResult)loadRomBootstrap.Invoke(null, new object[] { machine, "rom-driven.tap", blocks, true, 1, 1 })!;

                Assert.Equal(4, result.TotalBlockCount);
                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                Assert.Equal("rom-driven.tap", machine.MountedTapeName);
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.Equal((byte)0, machine.PeekMemory(23620));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Rejects_NonBasic_Leader()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "nonbasicbootstrap.tap");

            try
            {
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 4, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 1, 2, 3, 4 }));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);

                Assert.Throws<InvalidOperationException>(() =>
                    TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Executes_AutoStart_Statements()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "autorun.tap");

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(253), Ascii("25999"), NumberMarker(25999),
                        Colon(),
                        Token(239), QuoteQuote(), Ascii(" "), Token(175),
                        Colon(),
                        Token(244), Ascii("40000"), NumberMarker(40000), Comma(), Ascii("1"), NumberMarker(1),
                        Colon(),
                        Token(235), Ascii("f"), Equals(), Ascii("40001"), NumberMarker(40001), Ascii(" "), Token(204), Ascii("40003"), NumberMarker(40003),
                        Colon(),
                        Token(227), Ascii("a"),
                        Colon(),
                        Token(244), Ascii("f"), Comma(), Ascii("a"),
                        Colon(),
                        Token(243), Ascii("f"),
                        Colon(),
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)),
                    BuildBasicLine(20,
                        Token(228), Ascii("2"), NumberMarker(2), Comma(), Ascii("3"), NumberMarker(3), Comma(), Ascii("4"), NumberMarker(4)));

                byte[] codeLoader = new byte[] { 0x00, 0x01, 0x02 };

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "AUTO", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: (ushort)codeLoader.Length, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(codeLoader));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.True(machine.HasMountedTape);
                Assert.False(machine.MountedTape!.HasRemainingBlocks);
                Assert.Equal((byte)1, machine.PeekMemory(0x9C40));
                Assert.Equal((byte)2, machine.PeekMemory(0x9C41));
                Assert.Equal((byte)3, machine.PeekMemory(0x9C42));
                Assert.Equal((byte)4, machine.PeekMemory(0x9C43));
                Assert.Equal((ushort)25999, ReadWord(machine, 23730));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x01, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x02, machine.PeekMemory(0x8002));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void ShouldDeferToRomForMountedLoadProgram_Defers_Simple_ClearLoad_Programs()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(253), Ascii("25999"), NumberMarker(25999),
                        Colon(),
                        Token(239)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo shouldDefer = executorType.GetMethod("ShouldDeferToRomForMountedLoadProgram", BindingFlags.Public | BindingFlags.Static)!;
                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE1",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                bool defer = (bool)shouldDefer.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 })!;
                Assert.True(defer);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BasicBootstrapExecutor_CanExecuteProgram_Rejects_Duplicate_Line_Numbers()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] chainedLoader = BuildBasicProgram(
                    BuildBasicLine(0, Token(244), Ascii("23624"), Comma(), Ascii("0"), NumberMarker(0)),
                    BuildBasicLine(0, Token(244), Ascii("23662"), Comma(), Ascii("0"), NumberMarker(0)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo parseLines = executorType.GetMethod("ParseLines", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo canExecute = executorType.GetMethod("CanExecuteProgram", BindingFlags.NonPublic | BindingFlags.Static)!;
                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "CHAINED",
                    dataLength: (ushort)chainedLoader.Length,
                    parameter1: 0,
                    parameter2: (ushort)chainedLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

                loadBasicProgram.Invoke(null, new object[] { machine, header, chainedLoader });

                object parsedLines = parseLines.Invoke(null, new object[] { machine, (ushort)23755, (ushort)chainedLoader.Length })!;
                bool result = (bool)canExecute.Invoke(null, new object[] { parsedLines, (ushort)0 })!;

                Assert.False(result);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BasicBootstrapExecutor_ParseLines_Preserves_To_Keyword_Between_Variables()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(235), Ascii("D"), Equals(), Ascii("B"), Token(204), Ascii("B"), Ascii("+"), Ascii("C"), Ascii("-"), Ascii("1"), NumberMarker(1),
                        Colon(),
                        Token(243), Ascii("D")));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        return method.Name == "LoadBasicProgram" &&
                               parameters.Length == 4 &&
                               parameters[1].ParameterType.Name == "TapHeaderInfo";
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo parseLines = executorType.GetMethod("ParseLines", BindingFlags.NonPublic | BindingFlags.Static)!;

                initializeMachine.Invoke(null, new object[] { machine, false });

                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(
                        type: 0,
                        fileName: "FORTEST",
                        dataLength: (ushort)program.Length,
                        parameter1: 10,
                        parameter2: (ushort)program.Length),
                    2168,
                    8063,
                    667,
                    735,
                    855,
                    1710,
                    8,
                    1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, header, program, false });

                object parsedLines = parseLines.Invoke(null, new object[] { machine, (ushort)23755, (ushort)program.Length })!;
                object firstLine = ((System.Collections.IEnumerable)parsedLines).Cast<object>().First();
                var statements = (System.Collections.IEnumerable)firstLine.GetType().GetProperty("Statements")!.GetValue(firstLine)!;
                List<string> forStatement = ((System.Collections.IEnumerable)statements).Cast<object>()
                    .Select(statement => ((System.Collections.IEnumerable)statement).Cast<object>().Select(token => token.ToString()!).ToList())
                    .First();

                Assert.Equal(new[] { "FOR", "D", "=", "B", "TO", "B", "+", "C", "-", "1" }, forStatement);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BasicBootstrapExecutor_ImmediateProtectedProgram_DoesNotRequireRomDrivenMountedPath()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] protectedStage = BuildBasicProgram(
                    BuildBasicLine(0, Token(244), Ascii("23624"), Comma(), Ascii("0"), NumberMarker(0)),
                    BuildBasicLine(0, Token(244), Ascii("23662"), Comma(), Ascii("17"), NumberMarker(17)),
                    BuildBasicLine(0, Token(244), Ascii("23663"), Comma(), Ascii("34"), NumberMarker(34)),
                    BuildBasicLine(0, Token(244), Ascii("23664"), Comma(), Ascii("51"), NumberMarker(51)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo requiresRomDriven = executorType.GetMethod("RequiresRomDrivenMountedLoadedProgram", BindingFlags.Public | BindingFlags.Static)!;
                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE2",
                    dataLength: (ushort)protectedStage.Length,
                    parameter1: 0,
                    parameter2: (ushort)protectedStage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

                loadBasicProgram.Invoke(null, new object[] { machine, header, protectedStage });

                bool result = (bool)requiresRomDriven.Invoke(null, new object[] { machine, (ushort)23755, (ushort)protectedStage.Length, (ushort)0 })!;
                Assert.False(result);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void ExecuteBootstrapBasicAutoStart_Applies_SideEffectOnly_DuplicateLine_Loaded_Programs()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] firstStage = BuildBasicProgram(
                    BuildBasicLine(10, Token(239)));

                byte[] protectedStage = BuildBasicProgram(
                    BuildBasicLine(0, Token(244), Ascii("23624"), Comma(), Ascii("0"), NumberMarker(0)),
                    BuildBasicLine(0, Token(244), Ascii("23662"), Comma(), Ascii("17"), NumberMarker(17)),
                    BuildBasicLine(0, Token(244), Ascii("23663"), Comma(), Ascii("34"), NumberMarker(34)),
                    BuildBasicLine(0, Token(244), Ascii("23664"), Comma(), Ascii("51"), NumberMarker(51)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo executeBootstrap = typeof(TapLoader).GetMethod("ExecuteBootstrapBasicAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;

                initializeMachine.Invoke(null, new object[] { machine, false });

                TapeBlock firstHeaderBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE1",
                    dataLength: (ushort)firstStage.Length,
                    parameter1: 10,
                    parameter2: (ushort)firstStage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object firstHeader = parseHeaderInfo.Invoke(null, new object[] { firstHeaderBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, firstHeader, firstStage });

                TapeBlock secondHeader = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE2",
                    dataLength: (ushort)protectedStage.Length,
                    parameter1: 0,
                    parameter2: (ushort)protectedStage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock secondData = TapeBlock.CreateData(BuildDataBlock(protectedStage), 2168, 3223, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape("protected.tap", new[] { secondHeader, secondData }));

                executeBootstrap.Invoke(null, new object[] { machine, (ushort)23755, (ushort)firstStage.Length, (ushort)10, false });

                ushort expectedVars = (ushort)(23755 + protectedStage.Length);
                ushort expectedEnd = (ushort)(23755 + protectedStage.Length);
                Assert.Equal(expectedVars, ReadWord(machine, 23627));
                Assert.Equal(expectedEnd, ReadWord(machine, 23641));
                Assert.Equal(expectedEnd, ReadWord(machine, 23649));
                Assert.Equal(expectedEnd, ReadWord(machine, 23651));
                Assert.Equal(expectedEnd, ReadWord(machine, 23653));
                Assert.Equal((byte)0x00, machine.PeekMemory(23624));
                Assert.Equal((byte)17, machine.PeekMemory(23662));
                Assert.Equal((byte)34, machine.PeekMemory(23663));
                Assert.Equal((byte)51, machine.PeekMemory(23664));
                Assert.Equal(ReadWord(machine, 23641), ReadWord(machine, 23649));
                Assert.Equal(ReadWord(machine, 23641), ReadWord(machine, 23651));
                Assert.Equal(ReadWord(machine, 23641), ReadWord(machine, 23653));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void ExecuteBootstrapBasicAutoStart_Seeds_CurrentExecutionContext_For_ImmediateLoadedProgram()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] firstStage = BuildBasicProgram(
                    BuildBasicLine(10, Token(239)));

                byte[] protectedStage = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(244), Ascii("23662"), Comma(), Token(190), Ascii("23618"), NumberMarker(23618),
                        Colon(),
                        Token(244), Ascii("23663"), Comma(), Token(190), Ascii("23619"), NumberMarker(23619),
                        Colon(),
                        Token(244), Ascii("23664"), Comma(), Token(190), Ascii("23621"), NumberMarker(23621),
                        Colon(),
                        Token(244), Ascii("23624"), Comma(), Ascii("0"), NumberMarker(0)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo executeBootstrap = typeof(TapLoader).GetMethod("ExecuteBootstrapBasicAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;

                initializeMachine.Invoke(null, new object[] { machine, false });

                TapeBlock firstHeaderBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE1",
                    dataLength: (ushort)firstStage.Length,
                    parameter1: 10,
                    parameter2: (ushort)firstStage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object firstHeader = parseHeaderInfo.Invoke(null, new object[] { firstHeaderBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, firstHeader, firstStage });

                TapeBlock secondHeader = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE2",
                    dataLength: (ushort)protectedStage.Length,
                    parameter1: 10,
                    parameter2: (ushort)protectedStage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock secondData = TapeBlock.CreateData(BuildDataBlock(protectedStage), 2168, 3223, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape("protected.tap", new[] { secondHeader, secondData }));

                executeBootstrap.Invoke(null, new object[] { machine, (ushort)23755, (ushort)firstStage.Length, (ushort)10, false });

                Assert.Equal((byte)10, machine.PeekMemory(23662));
                Assert.Equal((byte)0, machine.PeekMemory(23663));
                Assert.Equal((byte)3, machine.PeekMemory(23664));
                Assert.Equal((byte)0, machine.PeekMemory(23624));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void ExecuteBootstrapBasicAutoStart_Allows_Decorated_ImmediateLoadedProgram_To_Preserve_Protected_Handoff_State()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] firstStage = BuildBasicProgram(
                    BuildBasicLine(10, Token(239)));

                byte[] protectedStage = BuildBasicProgram(
                    BuildBasicLine(0, Ascii("Protected by SPEEDLOCK")),
                    BuildBasicLine(0,
                        Ascii("0"),
                        Colon(),
                        Token(251),
                        Colon(),
                        Token(244), Ascii("23624"), Comma(), Ascii("0"), NumberMarker(0)),
                    BuildBasicLine(0,
                        Token(244), Ascii("23662"), Comma(), Token(190), Ascii("23618"), NumberMarker(23618),
                        Colon(),
                        Token(244), Ascii("23663"), Comma(), Token(190), Ascii("23619"), NumberMarker(23619),
                        Colon(),
                        Token(244), Ascii("23664"), Comma(), Token(190), Ascii("23621"), NumberMarker(23621),
                        Colon(),
                        Token(244), Ascii("("), Token(190), Ascii("23641"), NumberMarker(23641), Ascii("+"), Ascii("256"), NumberMarker(256), Ascii("*"), Token(190), Ascii("23642"), NumberMarker(23642), Ascii(")"),
                        Comma(),
                        Token(190), Ascii("23649"), NumberMarker(23649),
                        Colon(),
                        Token(244), Ascii("("), Token(190), Ascii("23641"), NumberMarker(23641), Ascii("+"), Ascii("256"), NumberMarker(256), Ascii("*"), Token(190), Ascii("23642"), NumberMarker(23642), Ascii(")"), Ascii("+"), Ascii("1"), NumberMarker(1),
                        Comma(),
                        Token(190), Ascii("23650"), NumberMarker(23650),
                        Colon(),
                        Token(244), Ascii("("), Token(190), Ascii("23633"), NumberMarker(23633), Ascii("+"), Ascii("256"), NumberMarker(256), Ascii("*"), Token(190), Ascii("23634"), NumberMarker(23634), Ascii(")"),
                        Comma(),
                        Token(190), Ascii("23647"), NumberMarker(23647),
                        Colon(),
                        Token(244), Ascii("("), Token(190), Ascii("23633"), NumberMarker(23633), Ascii("+"), Ascii("256"), NumberMarker(256), Ascii("*"), Token(190), Ascii("23634"), NumberMarker(23634), Ascii(")"), Ascii("+"), Ascii("1"), NumberMarker(1),
                        Comma(),
                        Token(190), Ascii("23648"), NumberMarker(23648)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo executeBootstrap = typeof(TapLoader).GetMethod("ExecuteBootstrapBasicAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;

                initializeMachine.Invoke(null, new object[] { machine, false });

                TapeBlock firstHeaderBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE1",
                    dataLength: (ushort)firstStage.Length,
                    parameter1: 10,
                    parameter2: (ushort)firstStage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object firstHeader = parseHeaderInfo.Invoke(null, new object[] { firstHeaderBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, firstHeader, firstStage });

                TapeBlock secondHeader = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "STAGE2",
                    dataLength: (ushort)protectedStage.Length,
                    parameter1: 0,
                    parameter2: (ushort)protectedStage.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                TapeBlock secondData = TapeBlock.CreateData(BuildDataBlock(protectedStage), 2168, 3223, 667, 735, 855, 1710, 8, 1000);
                machine.MountTape(new MountedTape("protected.tap", new[] { secondHeader, secondData }));

                executeBootstrap.Invoke(null, new object[] { machine, (ushort)23755, (ushort)firstStage.Length, (ushort)10, false });

                ushort expectedEnd = (ushort)(23755 + protectedStage.Length);
                Assert.Equal((byte)0x00, machine.PeekMemory(23624));
                Assert.Equal((byte)0, machine.PeekMemory(23662));
                Assert.Equal((byte)0x00, machine.PeekMemory(23663));
                Assert.Equal((byte)3, machine.PeekMemory(23664));
                Assert.Equal(expectedEnd, ReadWord(machine, 23641));
                Assert.Equal(expectedEnd, ReadWord(machine, 23649));
                Assert.Equal((byte)(expectedEnd & 0xFF), machine.PeekMemory(expectedEnd));
                Assert.Equal((byte)(expectedEnd >> 8), machine.PeekMemory((ushort)(expectedEnd + 1)));
                Assert.Equal(machine.PeekMemory(23647), machine.PeekMemory(ReadWord(machine, 23633)));
                Assert.Equal(machine.PeekMemory(23648), machine.PeekMemory((ushort)(ReadWord(machine, 23633) + 1)));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void ExecuteBootstrapBasicAutoStart_Read_Assigns_Multiple_Variables_From_One_Statement()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(227), Ascii("A"), Comma(), Ascii("B"),
                        Colon(),
                        Token(244), Ascii("23624"), Comma(), Ascii("A"),
                        Colon(),
                        Token(244), Ascii("23625"), Comma(), Ascii("B")),
                    BuildBasicLine(20,
                        Token(228),
                        Ascii("17"), NumberMarker(17), Comma(),
                        Ascii("34"), NumberMarker(34)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo executeBootstrap = typeof(TapLoader).GetMethod("ExecuteBootstrapBasicAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;

                initializeMachine.Invoke(null, new object[] { machine, false });
                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "READ2",
                    dataLength: (ushort)program.Length,
                    parameter1: 10,
                    parameter2: (ushort)program.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, header, program });

                executeBootstrap.Invoke(null, new object[] { machine, (ushort)23755, (ushort)program.Length, (ushort)10, false });

                Assert.Equal((byte)17, machine.PeekMemory(23624));
                Assert.Equal((byte)34, machine.PeekMemory(23625));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void ExecuteBootstrapBasicAutoStart_Restore_Rewinds_Data_Stream_To_Target_Line()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(227), Ascii("A"),
                        Colon(),
                        Token(244), Ascii("23624"), Comma(), Ascii("A"),
                        Colon(),
                        Token(229), Ascii("30"), NumberMarker(30),
                        Colon(),
                        Token(227), Ascii("B"),
                        Colon(),
                        Token(244), Ascii("23625"), Comma(), Ascii("B")),
                    BuildBasicLine(20,
                        Token(228),
                        Ascii("1"), NumberMarker(1)),
                    BuildBasicLine(30,
                        Token(228),
                        Ascii("2"), NumberMarker(2)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo executeBootstrap = typeof(TapLoader).GetMethod("ExecuteBootstrapBasicAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;

                initializeMachine.Invoke(null, new object[] { machine, false });
                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "RESTORE",
                    dataLength: (ushort)program.Length,
                    parameter1: 10,
                    parameter2: (ushort)program.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, header, program });

                executeBootstrap.Invoke(null, new object[] { machine, (ushort)23755, (ushort)program.Length, (ushort)10, false });

                Assert.Equal((byte)1, machine.PeekMemory(23624));
                Assert.Equal((byte)2, machine.PeekMemory(23625));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void ExecuteBootstrapBasicAutoStart_For_With_Start_Greater_Than_End_Skips_Loop_Body()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(229), Ascii("20"), NumberMarker(20),
                        Colon(),
                        Token(235), Ascii("A"), Equals(), Ascii("1"), NumberMarker(1), Ascii(" "), Token(204), Ascii("1"), NumberMarker(1),
                        Colon(),
                        Token(227), Ascii("B"), Comma(), Ascii("C"),
                        Colon(),
                        Token(235), Ascii("D"), Equals(), Ascii("B"), Token(204), Ascii("B"), Ascii("+"), Ascii("C"), Ascii("-"), Ascii("1"), NumberMarker(1),
                        Colon(),
                        Token(227), Ascii("C"),
                        Colon(),
                        Token(244), Ascii("D"), Comma(), Ascii("C"),
                        Colon(),
                        Token(243), Ascii("D"),
                        Colon(),
                        Token(243), Ascii("A")),
                    BuildBasicLine(20,
                        Token(228),
                        Ascii("40000"), NumberMarker(40000), Comma(),
                        Ascii("0"), NumberMarker(0), Comma(),
                        Ascii("50000"), NumberMarker(50000), Comma(),
                        Ascii("2"), NumberMarker(2), Comma(),
                        Ascii("11"), NumberMarker(11), Comma(),
                        Ascii("22"), NumberMarker(22)));

                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        if (method.Name != "LoadBasicProgram")
                            return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 &&
                               parameters[0].ParameterType == typeof(Spectrum128Machine) &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo executeBootstrap = typeof(TapLoader).GetMethod("ExecuteBootstrapBasicAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;

                initializeMachine.Invoke(null, new object[] { machine, false });
                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "FORSKIP",
                    dataLength: (ushort)program.Length,
                    parameter1: 10,
                    parameter2: (ushort)program.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, header, program });

                executeBootstrap.Invoke(null, new object[] { machine, (ushort)23755, (ushort)program.Length, (ushort)10, false });

                Assert.Equal((byte)0, machine.PeekMemory(50000));
                Assert.Equal((byte)0, machine.PeekMemory(50001));
                Assert.Equal((byte)0, machine.PeekMemory(40000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Executes_ExolonStyle_Basic_Loader()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "exolon-style.tap");

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(231), Ascii("0"), NumberMarker(0),
                        Colon(),
                        Token(218), Ascii("0"), NumberMarker(0),
                        Colon(),
                        Token(217), Ascii("7"), NumberMarker(7),
                        Colon(),
                        Token(253), Ascii("25999"), NumberMarker(25999),
                        Colon(),
                        Token(239), QuoteQuote(), Ascii(" "), Token(175),
                        Colon(),
                        Token(244), Ascii("64659"), NumberMarker(64659), Comma(), Ascii("0"), NumberMarker(0),
                        Colon(),
                        Token(244), Ascii("65105"), NumberMarker(65105), Comma(), Ascii("195"), NumberMarker(195),
                        Colon(),
                        Token(244), Ascii("65106"), NumberMarker(65106), Comma(), Ascii("153"), NumberMarker(153),
                        Colon(),
                        Token(244), Ascii("65107"), NumberMarker(65107), Comma(), Ascii("252"), NumberMarker(252),
                        Colon(),
                        Token(235), Ascii("f"), Equals(), Ascii("64662"), NumberMarker(64662), Ascii(" "), Token(204), Ascii("64689"), NumberMarker(64689),
                        Colon(),
                        Token(227), Ascii("a"),
                        Colon(),
                        Token(244), Ascii("f"), Comma(), Ascii("a"),
                        Colon(),
                        Token(243), Ascii("f"),
                        Colon(),
                        Token(249), Ascii(" "), Token(192), Ascii("65082"), NumberMarker(65082)),
                    BuildBasicLine(20,
                        Token(228),
                        Ascii("195"), NumberMarker(195), Comma(),
                        Ascii("98"), NumberMarker(98), Comma(),
                        Ascii("5"), NumberMarker(5), Comma(),
                        Ascii("243"), NumberMarker(243), Comma(),
                        Ascii("205"), NumberMarker(205), Comma(),
                        Ascii("142"), NumberMarker(142), Comma(),
                        Ascii("2"), NumberMarker(2), Comma(),
                        Ascii("28"), NumberMarker(28), Comma(),
                        Ascii("40"), NumberMarker(40), Comma(),
                        Ascii("250"), NumberMarker(250), Comma(),
                        Ascii("62"), NumberMarker(62), Comma(),
                        Ascii("33"), NumberMarker(33), Comma(),
                        Ascii("50"), NumberMarker(50), Comma(),
                        Ascii("81"), NumberMarker(81), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("62"), NumberMarker(62), Comma(),
                        Ascii("95"), NumberMarker(95), Comma(),
                        Ascii("50"), NumberMarker(50), Comma(),
                        Ascii("82"), NumberMarker(82), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("62"), NumberMarker(62), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("50"), NumberMarker(50), Comma(),
                        Ascii("83"), NumberMarker(83), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("195"), NumberMarker(195), Comma(),
                        Ascii("81"), NumberMarker(81), Comma(),
                        Ascii("254"), NumberMarker(254)));

                byte[] codeLoader = new byte[768];

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "EXOLON", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "EXOLON", dataLength: (ushort)codeLoader.Length, parameter1: 0xFC00, parameter2: 0),
                    BuildDataBlock(codeLoader));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal((ushort)0xFE3A, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x00, machine.PeekMemory(64659));
                Assert.Equal((byte)0xC3, machine.PeekMemory(65105));
                Assert.Equal((byte)0x99, machine.PeekMemory(65106));
                Assert.Equal((byte)0xFC, machine.PeekMemory(65107));

                byte[] expectedPatch =
                {
                    195,98,5,243,205,142,2,28,40,250,62,33,50,81,254,62,95,50,82,254,62,254,50,83,254,195,81,254
                };

                for (int i = 0; i < expectedPatch.Length; i++)
                    Assert.Equal(expectedPatch[i], machine.PeekMemory((ushort)(64662 + i)));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Executes_Simple_Mounted_Load_Stages()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "mounted-stage.tap");

            try
            {
                byte[] firstStage = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(253), Ascii("25999"), NumberMarker(25999),
                        Colon(),
                        Token(239)));
                byte[] secondStage = BuildBasicProgram(
                    BuildBasicLine(20,
                        Token(244), Ascii("23624"), NumberMarker(23624), Comma(), Ascii("5"), NumberMarker(5)));

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "STAGE1", dataLength: (ushort)firstStage.Length, parameter1: 10, parameter2: (ushort)firstStage.Length),
                    BuildDataBlock(firstStage),
                    BuildHeaderBlock(type: 0, fileName: "STAGE2", dataLength: (ushort)secondStage.Length, parameter1: 20, parameter2: (ushort)secondStage.Length),
                    BuildDataBlock(secondStage));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal((byte)5, machine.PeekMemory(23624));
                Assert.False(machine.HasMountedTape && machine.MountedTape!.HasRemainingBlocks);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Continues_After_Immediate_SideEffect_Loaded_Program()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "mounted-sidefx.tap");

            try
            {
                byte[] firstStage = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(239),
                        Colon(),
                        Token(244), Ascii("32768"), NumberMarker(32768), Comma(), Ascii("1"), NumberMarker(1),
                        Colon(),
                        Token(244), Ascii("32769"), NumberMarker(32769), Comma(), Ascii("2"), NumberMarker(2)));
                byte[] secondStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("23624"), NumberMarker(23624), Comma(), Ascii("5"), NumberMarker(5)));

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "STAGE1", dataLength: (ushort)firstStage.Length, parameter1: 10, parameter2: (ushort)firstStage.Length),
                    BuildDataBlock(firstStage),
                    BuildHeaderBlock(type: 0, fileName: "STAGE2", dataLength: (ushort)secondStage.Length, parameter1: 0, parameter2: (ushort)secondStage.Length),
                    BuildDataBlock(secondStage));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal((byte)5, machine.PeekMemory(23624));
                Assert.Equal((byte)1, machine.PeekMemory(32768));
                Assert.Equal((byte)2, machine.PeekMemory(32769));
                Assert.False(machine.HasMountedTape && machine.MountedTape!.HasRemainingBlocks);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Executes_MultiLoad_Basic_Without_Mounted_Tape()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "fullload.tap");

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(235), Ascii("i"), Equals(), Ascii("1"), NumberMarker(1), Ascii(" "), Token(204), Ascii("2"), NumberMarker(2),
                        Colon(),
                        Token(239), QuoteQuote(), Ascii(" "), Token(175),
                        Colon(),
                        Token(243), Ascii("i"),
                        Colon(),
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));

                byte[] codeOne = new byte[] { 0xAA };
                byte[] codeTwo = new byte[] { 0x00, 0x01, 0x02 };

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "FULL", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "ONE", dataLength: (ushort)codeOne.Length, parameter1: 0x9000, parameter2: 0),
                    BuildDataBlock(codeOne),
                    BuildHeaderBlock(type: 3, fileName: "TWO", dataLength: (ushort)codeTwo.Length, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(codeTwo));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.Equal(6, result.ConsumedBlockCount);
                Assert.Equal("FULL", result.AutoStartFileName);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.False(machine.HasMountedTape);
                Assert.Equal((byte)0xAA, machine.PeekMemory(0x9000));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x01, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x02, machine.PeekMemory(0x8002));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Loads_Custom_Header_Data_To_Parameter1_Address_And_Mounts_Custom_Remainder()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "custom-full.tap");

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10, Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                byte[] customData = new byte[] { 0xC3, 0x34, 0x12, 0x99 };

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "FULL", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 42, fileName: "FAST", dataLength: (ushort)customData.Length, parameter1: 0x9000, parameter2: 0),
                    BuildDataBlock(customData));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.Equal(4, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                Assert.Equal("custom-full.tap", machine.MountedTapeName);
                Assert.Equal((byte)0xC3, machine.PeekMemory(0x9000));
                Assert.Equal((byte)0x34, machine.PeekMemory(0x9001));
                Assert.Equal((byte)0x12, machine.PeekMemory(0x9002));
                Assert.Equal((byte)0x99, machine.PeekMemory(0x9003));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Uses_Code_Payload_For_Custom_Resume_Search_After_Basic_Patches_Ram()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "custom-resume.tap");

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(239), QuoteQuote(), Ascii(" "), Token(175),
                        Colon(),
                        Token(244), Ascii("64659"), NumberMarker(64659), Comma(), Ascii("0"), NumberMarker(0),
                        Colon(),
                        Token(244), Ascii("65105"), NumberMarker(65105), Comma(), Ascii("195"), NumberMarker(195),
                        Colon(),
                        Token(244), Ascii("65106"), NumberMarker(65106), Comma(), Ascii("153"), NumberMarker(153),
                        Colon(),
                        Token(244), Ascii("65107"), NumberMarker(65107), Comma(), Ascii("252"), NumberMarker(252),
                        Colon(),
                        Token(235), Ascii("f"), Equals(), Ascii("64662"), NumberMarker(64662), Ascii(" "), Token(204), Ascii("64689"), NumberMarker(64689),
                        Colon(),
                        Token(227), Ascii("a"),
                        Colon(),
                        Token(244), Ascii("f"), Comma(), Ascii("a"),
                        Colon(),
                        Token(243), Ascii("f"),
                        Colon(),
                        Token(249), Ascii(" "), Token(192), Ascii("65082"), NumberMarker(65082)),
                    BuildBasicLine(20,
                        Token(228),
                        Ascii("195"), NumberMarker(195), Comma(),
                        Ascii("98"), NumberMarker(98), Comma(),
                        Ascii("5"), NumberMarker(5), Comma(),
                        Ascii("243"), NumberMarker(243), Comma(),
                        Ascii("205"), NumberMarker(205), Comma(),
                        Ascii("142"), NumberMarker(142), Comma(),
                        Ascii("2"), NumberMarker(2), Comma(),
                        Ascii("28"), NumberMarker(28), Comma(),
                        Ascii("40"), NumberMarker(40), Comma(),
                        Ascii("250"), NumberMarker(250), Comma(),
                        Ascii("62"), NumberMarker(62), Comma(),
                        Ascii("33"), NumberMarker(33), Comma(),
                        Ascii("50"), NumberMarker(50), Comma(),
                        Ascii("81"), NumberMarker(81), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("62"), NumberMarker(62), Comma(),
                        Ascii("95"), NumberMarker(95), Comma(),
                        Ascii("50"), NumberMarker(50), Comma(),
                        Ascii("82"), NumberMarker(82), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("62"), NumberMarker(62), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("50"), NumberMarker(50), Comma(),
                        Ascii("83"), NumberMarker(83), Comma(),
                        Ascii("254"), NumberMarker(254), Comma(),
                        Ascii("195"), NumberMarker(195), Comma(),
                        Ascii("81"), NumberMarker(81), Comma(),
                        Ascii("254"), NumberMarker(254)));

                byte[] codeLoader = new byte[768];
                codeLoader[0xA8] = 0x10;
                codeLoader[0xA9] = 0xFE;
                codeLoader[0x182] = 0x10;
                codeLoader[0x183] = 0xFE;

                byte[] customData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "EXOLON", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "EXOLON", dataLength: (ushort)codeLoader.Length, parameter1: 0xFC00, parameter2: 0),
                    BuildDataBlock(codeLoader),
                    BuildHeaderBlock(type: 42, fileName: "FAST", dataLength: (ushort)customData.Length, parameter1: 0x9000, parameter2: 0),
                    BuildDataBlock(customData));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.Equal(6, result.ConsumedBlockCount);
                Assert.Equal((ushort)0xFCA8, machine.Cpu.Regs.PC);
                Assert.NotEqual((byte)0x10, machine.PeekMemory(0xFCA8));
                Assert.True(machine.HasMountedTape);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_LiveLoadableHeaderAndDataPlayback_AdvancesLogicalPosition_WithoutRomTrap()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(type: 42, fileName: "FAST", dataLength: 4, parameter1: 0x9000, parameter2: 0),
                    2168, 64, 667, 735, 855, 1710, 8, 10);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD }),
                    2168, 32, 667, 735, 855, 1710, 8, 0);
                var tape = new MountedTape("live-loadable-custom", new[] { headerBlock, dataBlock }, initialBlockIndex: 0, skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                for (int i = 0; i < 256 && GetPrivateField(tape, "earPlaybackState").ToString() != "Idle"; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal(2, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal("Idle", GetPrivateField(tape, "state").ToString());
                Assert.Equal("Idle", GetPrivateField(tape, "earPlaybackState").ToString());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_LiveLoadableDataPlayback_AdvancesLogicalPosition_WithoutRomTrap()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                TapeBlock dataBlock = TapeBlock.CreateData(
                    BuildDataBlock(new byte[] { 0x11, 0x22, 0x33, 0x44 }),
                    2168, 32, 667, 735, 855, 1710, 8, 0);
                var tape = new MountedTape("live-loadable-data", new[] { dataBlock }, initialBlockIndex: 0, skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                for (int i = 0; i < 256 && GetPrivateField(tape, "earPlaybackState").ToString() != "Idle"; i++)
                {
                    machine.Cpu.AddTStates(5000);
                    _ = machine.DebugReadPort(0x00FE);
                }

                Assert.Equal(1, (int)GetPrivateField(tape, "nextBlockIndex"));
                Assert.Equal("Idle", GetPrivateField(tape, "state").ToString());
                Assert.Equal("Idle", GetPrivateField(tape, "earPlaybackState").ToString());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        private static ushort ReadWord(Spectrum128Machine machine, ushort address)
        {
            return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
        }

        private static ulong EstimateTapeBlockDurationTStatesForTest(TapeBlock block)
        {
            return block.Kind switch
            {
                TapeBlockKind.Data => EstimateDataBlockDurationTStatesForTest(block),
                TapeBlockKind.PureTone => (ulong)block.PureTonePulseLength * (ulong)block.PureTonePulseCount,
                TapeBlockKind.PulseSequence => block.PulseSequence == null
                    ? 0UL
                    : block.PulseSequence.Aggregate(0UL, (total, pulse) => total + (ulong)pulse),
                TapeBlockKind.DirectRecording => EstimateDirectRecordingDurationTStatesForTest(block),
                TapeBlockKind.Pause => (ulong)block.PauseAfterBlockMs * 3500UL,
                _ => 0UL
            };
        }

        private static ulong EstimateDataBlockDurationTStatesForTest(TapeBlock block)
        {
            ulong total = (ulong)block.PilotPulseLength * (ulong)block.PilotPulseCount;
            total += block.SyncFirstPulseLength;
            total += block.SyncSecondPulseLength;

            for (int i = 0; i < block.StreamByteCount; i++)
            {
                byte value = block.GetStreamByte(i);
                int bitCount = block.GetStreamByteBitCount(i);
                for (int bit = 0; bit < bitCount; bit++)
                {
                    bool oneBit = (value & 0x80) != 0;
                    ushort pulseLength = oneBit ? block.OneBitPulseLength : block.ZeroBitPulseLength;
                    total += (ulong)pulseLength * 2UL;
                    value <<= 1;
                }
            }

            total += (ulong)block.PauseAfterBlockMs * 3500UL;
            return total;
        }

        private static ulong EstimateDirectRecordingDurationTStatesForTest(TapeBlock block)
        {
            int totalBits = Math.Max(0, ((block.StreamByteCount - 1) * 8) + block.UsedBitsInLastByte);
            return ((ulong)block.DirectRecordingSampleTStates * (ulong)totalBits) +
                   ((ulong)block.PauseAfterBlockMs * 3500UL);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            object? value = field!.GetValue(target);
            Assert.NotNull(value);
            return value!;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        private static byte[] BuildBasicProgram(params byte[][] lines)
        {
            var bytes = new List<byte>();
            foreach (byte[] line in lines)
                bytes.AddRange(line);
            return bytes.ToArray();
        }

        private static byte[] BuildBasicLine(ushort lineNumber, params byte[][] bodyParts)
        {
            var body = new List<byte>();
            foreach (byte[] part in bodyParts)
                body.AddRange(part);
            body.Add(0x0D);

            var bytes = new List<byte>
            {
                (byte)(lineNumber >> 8),
                (byte)(lineNumber & 0xFF),
                (byte)(body.Count & 0xFF),
                (byte)(body.Count >> 8)
            };
            bytes.AddRange(body);
            return bytes.ToArray();
        }

        private static byte[] Token(byte value) => new[] { value };
        private static byte[] Ascii(string text) => System.Text.Encoding.ASCII.GetBytes(text);
        private static byte[] Colon() => new byte[] { (byte)':' };
        private static byte[] Comma() => new byte[] { (byte)',' };
        private static byte[] Equals() => new byte[] { (byte)'=' };
        private static byte[] QuoteQuote() => new byte[] { (byte)'"', (byte)'"' };
        private static byte[] NumberMarker(int value)
        {
            return new byte[]
            {
                0x0E,
                0x00,
                0x00,
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                0x00
            };
        }

        private static byte[] BuildTap(params byte[][] blocks)
        {
            var bytes = new List<byte>();
            foreach (byte[] block in blocks)
            {
                bytes.Add((byte)(block.Length & 0xFF));
                bytes.Add((byte)(block.Length >> 8));
                bytes.AddRange(block);
            }

            return bytes.ToArray();
        }

        private static byte[] BuildHeaderBlock(byte type, string fileName, ushort dataLength, ushort parameter1, ushort parameter2)
        {
            byte[] payload = new byte[19];
            payload[0] = 0x00;
            payload[1] = type;

            string paddedName = (fileName ?? string.Empty).PadRight(10).Substring(0, 10);
            for (int i = 0; i < 10; i++)
                payload[2 + i] = (byte)paddedName[i];

            payload[12] = (byte)(dataLength & 0xFF);
            payload[13] = (byte)(dataLength >> 8);
            payload[14] = (byte)(parameter1 & 0xFF);
            payload[15] = (byte)(parameter1 >> 8);
            payload[16] = (byte)(parameter2 & 0xFF);
            payload[17] = (byte)(parameter2 >> 8);
            payload[18] = ComputeChecksum(payload, 0, payload.Length - 1);
            return payload;
        }

        private static byte[] BuildDataBlock(byte[] data)
        {
            byte[] block = new byte[data.Length + 2];
            block[0] = 0xFF;
            Buffer.BlockCopy(data, 0, block, 1, data.Length);
            block[block.Length - 1] = ComputeChecksum(block, 0, block.Length - 1);
            return block;
        }

        private static byte ComputeChecksum(byte[] data, int offset, int count)
        {
            byte checksum = 0;
            for (int i = 0; i < count; i++)
                checksum ^= data[offset + i];

            return checksum;
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Uses_Mounted_Path_For_Poke_Selected_128k_Loads()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "banked-standard.tap");

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(227), Ascii("a"),
                        Colon(),
                        Token(244), Ascii("23388"), NumberMarker(23388), Comma(), Ascii("16"), NumberMarker(16), Ascii("+"), Ascii("a"),
                        Colon(),
                        Token(239), QuoteQuote(),
                        Colon(),
                        Token(227), Ascii("a"),
                        Colon(),
                        Token(244), Ascii("23388"), NumberMarker(23388), Comma(), Ascii("16"), NumberMarker(16), Ascii("+"), Ascii("a"),
                        Colon(),
                        Token(239), QuoteQuote(),
                        Colon(),
                        Token(249), Ascii(" "), Token(192), Ascii("49152"), NumberMarker(49152)),
                    BuildBasicLine(20,
                        Token(228),
                        Ascii("3"), NumberMarker(3), Comma(),
                        Ascii("4"), NumberMarker(4)));

                byte[] firstCode = new byte[] { 0xAA, 0xAB };
                byte[] secondCode = new byte[] { 0xBB, 0xBC };
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BANKED", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "ONE", dataLength: (ushort)firstCode.Length, parameter1: 0xC000, parameter2: 0),
                    BuildDataBlock(firstCode),
                    BuildHeaderBlock(type: 3, fileName: "TWO", dataLength: (ushort)secondCode.Length, parameter1: 0xC000, parameter2: 0),
                    BuildDataBlock(secondCode));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal((ushort)0xC000, machine.Cpu.Regs.PC);
                Assert.False(machine.PagingLocked);
                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.Equal((byte)0xAA, machine.GetRamBankCopy(3)[0]);
                Assert.Equal((byte)0xAB, machine.GetRamBankCopy(3)[1]);
                Assert.Equal((byte)0xBB, machine.GetRamBankCopy(4)[0]);
                Assert.Equal((byte)0xBC, machine.GetRamBankCopy(4)[1]);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }
}
