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
        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }

        [Theory]
        [InlineData(0x00, 8063)]
        [InlineData(0x07, 8063)]
        [InlineData(0x7F, 8063)]
        [InlineData(0x80, 3223)]
        [InlineData(0xFF, 3223)]
        public void ParseBlocks_SelectsPilotLengthFromFlagRange(byte flag, ushort expectedPilotPulses)
        {
            byte[] tap = BuildTap(BuildDataBlock(new byte[] { 0x42 }, flag));

            TapeBlock block = Assert.Single(TapLoader.ParseBlocks(tap));

            Assert.Equal(expectedPilotPulses, block.PilotPulseCount);
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
                machine.Reset(SpectrumMachineModel.Spectrum48K);
                TapLoadResult result = TapLoader.Load(machine, tapePath);

                Assert.Equal(2, result.TotalBlockCount);
                Assert.Equal(1, result.LoadedBlockCount);
                Assert.Equal((byte)0x3E, machine.PeekMemory(0x8000));
                Assert.Equal((byte)0x2A, machine.PeekMemory(0x8001));
                Assert.Equal((byte)0x32, machine.PeekMemory(0x8002));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8003));
                Assert.Equal((byte)0x80, machine.PeekMemory(0x8004));
                Assert.Equal((ushort)0x12A2, machine.Cpu.Regs.PC);
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
                Assert.Equal((ushort)(23755 + basicAndVariables.Length + 1), ReadWord(machine, 23641));
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.Equal((byte)0, machine.PeekMemory(23620));
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

        private static byte[] BuildDataBlock(byte[] data, byte flag = 0xFF)
        {
            byte[] block = new byte[data.Length + 2];
            block[0] = flag;
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

        [Fact]
        public void MountedLoadUsrContinuationResolver_Evaluates_Cached_Preamble_Usr_FollowOn_Directly_When_Live_Line_Is_Gone()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)),
                    BuildBasicLine(20,
                        Token(241), Ascii(" "), Ascii("a"), Equals(),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Colon(),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATMAN",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);

                ushort programStart = 23755;
                ushort line20Start = (ushort)(programStart + BuildBasicLine(10,
                    Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)).Length);
                for (int offset = 0; offset < 8; offset++)
                    machine.PokeMemory((ushort)(line20Start + offset), 0xFF);

                var resolver = (Func<Spectrum128Machine, ushort?>)resolverObject!;
                ushort? entryPoint = resolver(machine);
                Assert.Equal((ushort)0x1234, entryPoint);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedLoadUsrContinuationResolver_Evaluates_FollowOn_Usr_From_Live_SystemVariables_For_MultiStep_Sequences()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)),
                    BuildBasicLine(20,
                        Token(241), Ascii(" "), Ascii("a"), Equals(),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Colon(),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATMAN",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);

                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);

                var resolver = (Func<Spectrum128Machine, ushort?>)resolverObject!;

                ushort basicProgramStart = 23755;
                for (int offset = 0; offset < basicLoader.Length; offset++)
                    machine.PokeMemory((ushort)(basicProgramStart + offset), 0x00);

                ushort? firstEntryPoint = resolver(machine);
                Assert.Equal((ushort)0x1234, firstEntryPoint);
                Assert.NotNull(machine.PendingMountedLoadResolver);

                machine.PokeMemory(23633, 0x78);
                machine.PokeMemory(23634, 0x56);
                var secondResolver = machine.PendingMountedLoadResolver!;
                ushort? secondEntryPoint = secondResolver(machine);
                Assert.Equal((ushort)0x5678, secondEntryPoint);
                Assert.Null(machine.PendingMountedLoadResolver);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Evaluates_Live_Complex_FollowOn_Usr_With_Preamble_Directly()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)),
                    BuildBasicLine(20,
                        Token(250), Ascii(" "), Ascii("a"), Equals(),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Colon(),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "LIVEUSR",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("idle-mounted", Array.Empty<TapeBlock>()));
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!);
                machine.PokeMemory(23631, 0x00);
                machine.PokeMemory(23632, 0x00);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumed);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.BC);
                Assert.Null(machine.PendingMountedLoadResolver);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_DirectUsr_Repositions_Cursor_To_Usr_Expression()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)),
                    BuildBasicLine(20,
                        Token(250), Ascii(" "), Ascii("a"), Equals(),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "ROMCHADD",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("idle-mounted", Array.Empty<TapeBlock>()));
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!);
                machine.PokeMemory(23631, 0x00);
                machine.PokeMemory(23632, 0x00);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumed);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);

                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Uses_Usr0_Mode_For_FollowOn_Usr_Zero_After_Direct_IfThen_Usr()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)),
                    BuildBasicLine(20,
                        Token(250), Ascii(" "), Ascii("a"), Equals(),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "CHAINUSR",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("idle-mounted", Array.Empty<TapeBlock>()));
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!);
                machine.PokeMemory(23631, 0x00);
                machine.PokeMemory(23632, 0x00);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                bool resumedOnUsrReturn = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumedOnUsrReturn);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
                Assert.NotNull(machine.PendingMountedLoadResolver);

                machine.Cpu.Regs.PC = 0x1555;
                bool resumedTooEarly = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.False(resumedTooEarly);
                Assert.NotNull(machine.PendingMountedLoadResolver);

                machine.Cpu.Regs.PC = 0x2D2B;
                bool resumedFollowOn = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumedFollowOn);
                Assert.Equal((ushort)0x0000, machine.Cpu.Regs.PC);
                Assert.Equal(1, machine.CurrentRomBank);
                Assert.False(machine.PagingLocked);
                Assert.Equal(Spectrum128Machine.FrameTStates128, machine.FrameTStates);
                Assert.Equal((ushort)0x5CBB, (ushort)(machine.PeekMemory(23633) | (machine.PeekMemory(23634) << 8)));
                Assert.Equal((ushort)0x0000, (ushort)(machine.PeekMemory(23647) | (machine.PeekMemory(23648) << 8)));
                Assert.Equal((ushort)0, (ushort)(machine.PeekMemory(23618) | (machine.PeekMemory(23619) << 8)));
                Assert.Equal((byte)0, machine.PeekMemory(23620));
                Assert.Equal((ushort)0, (ushort)(machine.PeekMemory(23621) | (machine.PeekMemory(23622) << 8)));
                Assert.Equal((byte)0, machine.PeekMemory(23623));
                machine.DebugWritePort(0x7FFD, 0x03);
                Assert.Equal(3, machine.PagedRamBank);
                Assert.Null(machine.PendingMountedLoadResolver);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Evaluates_Safe_Live_IfThen_Usr_Clause_Directly()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)),
                    BuildBasicLine(20,
                        Token(250), Ascii(" "), Ascii("a"), Equals(),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "IFTHEN",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("idle-mounted", Array.Empty<TapeBlock>()));
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });
                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory(23641, (byte)((varsAddress + 6) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 6) >> 8));

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!);
                machine.PokeMemory(23631, 0x00);
                machine.PokeMemory(23632, 0x00);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumed);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.BC);
                Assert.NotNull(machine.PendingMountedLoadResolver);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Skips_Safe_IfThen_Usr_Clause_And_Falls_Through_To_Usr0()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050)),
                    BuildBasicLine(20,
                        Token(250), Ascii(" "), Ascii("a"), Equals(),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATLIKE",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("idle-mounted", Array.Empty<TapeBlock>()));
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });
                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0x5B);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory(23641, (byte)((varsAddress + 6) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 6) >> 8));
                machine.RefreshPendingMountedLoadInterpreterContext();

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!);
                machine.PokeMemory(23631, 0xB6);
                machine.PokeMemory(23632, 0x5C);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumed);
                Assert.Equal((ushort)0x0000, (ushort)(machine.PeekMemory(23618) | (machine.PeekMemory(23619) << 8)));
                Assert.Equal((ushort)0x0000, (ushort)(machine.PeekMemory(23621) | (machine.PeekMemory(23622) << 8)));
                Assert.Equal((ushort)0x5CBB, (ushort)(machine.PeekMemory(23633) | (machine.PeekMemory(23634) << 8)));
                Assert.Null(machine.PendingMountedLoadResolver);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Evaluates_Live_Basic_Numeric_Variable_In_If_Then_Usr_Clause()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(245), Ascii(" "), Ascii("\""), Ascii("H"), Ascii("I"), Ascii("\"")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BASIC",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });
                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0xB6);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x5C);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)((varsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 7) >> 8));

                Type parserType = typeof(TapLoader).GetNestedType("ExpressionParser", BindingFlags.NonPublic)!;
                ConstructorInfo parserCtor = parserType
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(ctor => ctor.GetParameters().Length == 6);
                object parser = parserCtor.Invoke(new object[]
                {
                    machine,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    new List<string> { "a" },
                    0,
                    0,
                    true
                });
                int value = (int)parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(parser, Array.Empty<object>())!;
                Assert.Equal(0x5CB6, value);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Evaluates_Live_FloatingPoint_Basic_Numeric_Variable_In_If_Then_Usr_Clause()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(245), Ascii(" "), Ascii("\""), Ascii("H"), Ascii("I"), Ascii("\"")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BASIC",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });
                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(varsAddress, 0x61);
                WriteSpectrumPositiveIntegerFloat(machine, (ushort)(varsAddress + 1), 0x5CB6);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)((varsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 7) >> 8));

                Type parserType = typeof(TapLoader).GetNestedType("ExpressionParser", BindingFlags.NonPublic)!;
                ConstructorInfo parserCtor = parserType
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(ctor => ctor.GetParameters().Length == 6);
                object parser = parserCtor.Invoke(new object[]
                {
                    machine,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    new List<string> { "a" },
                    0,
                    0,
                    true
                });
                int value = (int)parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(parser, Array.Empty<object>())!;
                Assert.Equal(0x5CB6, value);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Evaluates_Basic_Numeric_Variable_From_Preserved_Context_When_Live_Vars_Move()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(245), Ascii(" "), Ascii("\""), Ascii("H"), Ascii("I"), Ascii("\"")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BASIC",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                ushort originalVarsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(originalVarsAddress, 0x61);
                machine.PokeMemory((ushort)(originalVarsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(originalVarsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(originalVarsAddress + 3), 0xB6);
                machine.PokeMemory((ushort)(originalVarsAddress + 4), 0x5C);
                machine.PokeMemory((ushort)(originalVarsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(originalVarsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)((originalVarsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((originalVarsAddress + 7) >> 8));
                machine.SetPendingMountedLoadUsrContinuationResolver(_ => 0);

                machine.PokeMemory(23627, 0xCB);
                machine.PokeMemory(23628, 0x5C);
                machine.PokeMemory(23641, 0xCC);
                machine.PokeMemory(23642, 0x5C);

                Type parserType = typeof(TapLoader).GetNestedType("ExpressionParser", BindingFlags.NonPublic)!;
                ConstructorInfo parserCtor = parserType
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(ctor => ctor.GetParameters().Length == 6);
                object parser = parserCtor.Invoke(new object[]
                {
                    machine,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    new List<string> { "a" },
                    0,
                    0,
                    true
                });
                int value = (int)parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(parser, Array.Empty<object>())!;
                Assert.Equal(0x5CB6, value);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Evaluates_Preserved_FloatingPoint_Basic_Numeric_Variable_When_Live_Vars_Move()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(245), Ascii(" "), Ascii("\""), Ascii("H"), Ascii("I"), Ascii("\"")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BASIC",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                ushort originalVarsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(originalVarsAddress, 0x61);
                WriteSpectrumPositiveIntegerFloat(machine, (ushort)(originalVarsAddress + 1), 0x5CB6);
                machine.PokeMemory((ushort)(originalVarsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)((originalVarsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((originalVarsAddress + 7) >> 8));
                machine.SetPendingMountedLoadUsrContinuationResolver(_ => 0);

                machine.PokeMemory(23627, 0xCB);
                machine.PokeMemory(23628, 0x5C);
                machine.PokeMemory(23641, 0xCC);
                machine.PokeMemory(23642, 0x5C);

                Type parserType = typeof(TapLoader).GetNestedType("ExpressionParser", BindingFlags.NonPublic)!;
                ConstructorInfo parserCtor = parserType
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(ctor => ctor.GetParameters().Length == 6);
                object parser = parserCtor.Invoke(new object[]
                {
                    machine,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    new List<string> { "a" },
                    0,
                    0,
                    true
                });
                int value = (int)parserType.GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(parser, Array.Empty<object>())!;
                Assert.Equal(0x5CB6, value);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Refreshes_Preserved_BasicVariableSnapshot_On_UsrReturn()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("24244"), NumberMarker(24244)),
                    BuildBasicLine(40,
                        Token(250), Ascii(" "), Ascii("a"), Ascii("="),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(50,
                        Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATLIKE",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 30,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("idle-mounted", Array.Empty<TapeBlock>()));
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x5B);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x00);
                machine.PokeMemory(23641, (byte)((varsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 7) >> 8));

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)30 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!, requireUsrReturnAddress: true);

                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0xB6);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x5C);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)((varsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 7) >> 8));

                machine.PokeMemory(23631, 0xB6);
                machine.PokeMemory(23632, 0x5C);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumed);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Refreshes_Preserved_BasicVariableSnapshot_On_UsrReturn_Even_When_RawStandardPath_Does_Not_Require_UsrReturn()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("24244"), NumberMarker(24244)),
                    BuildBasicLine(40,
                        Token(250), Ascii(" "), Ascii("a"), Ascii("="),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(50,
                        Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATLIKE",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 30,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                var machine = new Spectrum128Machine(tempFolder);
                machine.MountTape(new MountedTape("idle-mounted", Array.Empty<TapeBlock>()));
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x5B);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x00);
                machine.PokeMemory(23641, (byte)((varsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 7) >> 8));

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)30 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!, requireUsrReturnAddress: false);

                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0xB6);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x5C);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)((varsAddress + 7) & 0xFF));
                machine.PokeMemory(23642, (byte)((varsAddress + 7) >> 8));

                machine.PokeMemory(23631, 0xB6);
                machine.PokeMemory(23632, 0x5C);
                machine.PokeMemory(23633, 0x34);
                machine.PokeMemory(23634, 0x12);
                machine.Cpu.Regs.PC = 0x2D2B;
                machine.Cpu.Regs.SP = 0xFF00;

                bool resumed = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Assert.True(resumed);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0x1234, machine.Cpu.Regs.BC);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Refreshes_Preserved_BasicVariableSnapshot_After_Mounted_RomData_Block_Load()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("24244"), NumberMarker(24244)),
                    BuildBasicLine(40,
                        Token(250), Ascii(" "), Ascii("a"), Ascii("="),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(50,
                        Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATLIKE",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 30,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo variableReader = typeof(TapLoader).GetMethod(
                    "TryReadMountedContinuationNumericVariable",
                    BindingFlags.Static | BindingFlags.NonPublic)!;

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                ushort eLineAddress = (ushort)(varsAddress + 7);
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0x5B);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)(eLineAddress & 0xFF));
                machine.PokeMemory(23642, (byte)(eLineAddress >> 8));

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)30 });
                Assert.NotNull(resolverObject);
                machine.SetPendingMountedLoadUsrContinuationResolver((Func<Spectrum128Machine, ushort?>)resolverObject!, requireUsrReturnAddress: false);

                byte[] updatedVariableArea = new byte[]
                {
                    0x61,
                    0x00,
                    0x00,
                    0xB6,
                    0x5C,
                    0x00,
                    0x80
                };

                machine.PokeMemory(23627, (byte)(varsAddress & 0xFF));
                machine.PokeMemory(23628, (byte)(varsAddress >> 8));
                machine.PokeMemory(23641, (byte)(eLineAddress & 0xFF));
                machine.PokeMemory(23642, (byte)(eLineAddress >> 8));

                var tape = new MountedTape(
                    "raw-standard",
                    new[]
                    {
                        TapeBlock.CreateData(BuildDataBlock(updatedVariableArea), 2168, 3223, 667, 735, 855, 1710, 8, 1000)
                    },
                    initialBlockIndex: 0,
                    skipCustomHeaderForEarPlayback: false);
                machine.MountTape(tape);

                machine.Cpu.Regs.PC = 0x056B;
                machine.Cpu.Regs.SP = 0x9000;
                machine.PokeMemory(0x9000, 0x3F);
                machine.PokeMemory(0x9001, 0x05);
                machine.Cpu.Regs.IX = varsAddress;
                machine.Cpu.Regs.DE = (ushort)updatedVariableArea.Length;
                machine.Cpu.Regs.A = 0xFF;
                machine.Cpu.Regs.F = 0x01;
                machine.Cpu.Regs.A_ = 0xFF;
                machine.Cpu.Regs.F_ = 0x01;

                Assert.True(tape.TryHandleRomLoadTrap(machine, machine.Cpu));

                machine.PokeMemory(23627, 0x00);
                machine.PokeMemory(23628, 0x80);
                machine.PokeMemory(23641, 0x00);
                machine.PokeMemory(23642, 0x80);

                object[] readerArgs = new object[] { machine, "a", 0 };
                Assert.True((bool)variableReader.Invoke(null, readerArgs)!);
                Assert.Equal(0x5CB6, (int)readerArgs[2]);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadBasicProgram_Initializes_Current_Channel_To_K_And_Clears_XPtr()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(245), Ascii(" "), Ascii("\""), Ascii("H"), Ascii("I"), Ascii("\"")));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BASIC",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                Assert.Equal((ushort)0x5CB6, (ushort)(machine.PeekMemory(23633) | (machine.PeekMemory(23634) << 8)));
                Assert.Equal((ushort)0x0000, (ushort)(machine.PeekMemory(23647) | (machine.PeekMemory(23648) << 8)));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadBasicProgram_Appends_Variable_Terminator_And_Empty_Edit_Line()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10, Token(226)));
                byte[] savedVariables =
                {
                    0x61, 0x00, 0x00, 0x01, 0x00, 0x00
                };
                byte[] payload = program.Concat(savedVariables).ToArray();

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "VARS",
                    dataLength: (ushort)payload.Length,
                    parameter1: 10,
                    parameter2: (ushort)program.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method =>
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        return method.Name == "LoadBasicProgram" &&
                               parameters.Length == 3 &&
                               parameters[2].ParameterType == typeof(byte[]);
                    });

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, payload });

                ushort variablesEnd = (ushort)(23755 + payload.Length);
                ushort editLine = (ushort)(variablesEnd + 1);
                ushort workspace = (ushort)(editLine + 1);

                Assert.Equal((byte)0x80, machine.PeekMemory(variablesEnd));
                Assert.Equal((byte)0x0D, machine.PeekMemory(editLine));
                Assert.Equal(editLine, ReadWord(machine, 23641));
                Assert.Equal(workspace, ReadWord(machine, 23649));
                Assert.Equal(workspace, ReadWord(machine, 23651));
                Assert.Equal(workspace, ReadWord(machine, 23653));
                Assert.Equal(editLine, ReadWord(machine, 23643));
                Assert.Equal(editLine, ReadWord(machine, 23645));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Preserves_Usable_BasicVariableSnapshot_When_Live_Variable_Area_Collapses_To_EndMarker()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("24244"), NumberMarker(24244)),
                    BuildBasicLine(40,
                        Token(250), Ascii(" "), Ascii("a"), Ascii("="),
                        Token(190), Ascii("23631"), NumberMarker(23631),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23632"), NumberMarker(23632),
                        Token(203),
                        Token(249), Ascii(" "), Token(192),
                        Ascii("("),
                        Token(190), Ascii("23633"), NumberMarker(23633),
                        Ascii("+"),
                        Ascii("256"), NumberMarker(256),
                        Ascii("*"),
                        Token(190), Ascii("23634"), NumberMarker(23634),
                        Ascii(")")),
                    BuildBasicLine(50,
                        Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATLIKE",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 30,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                MethodInfo variableReader = typeof(TapLoader).GetMethod(
                    "TryReadMountedContinuationNumericVariable",
                    BindingFlags.Static | BindingFlags.NonPublic)!;

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                ushort richELineAddress = (ushort)(varsAddress + 7);
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0xB6);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x5C);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)(richELineAddress & 0xFF));
                machine.PokeMemory(23642, (byte)(richELineAddress >> 8));
                machine.RefreshPendingMountedLoadInterpreterContext(forceVariableAreaRefresh: true);

                ushort collapsedVarsAddress = 0x5CCB;
                ushort collapsedELineAddress = 0x5CCC;
                machine.PokeMemory(23627, (byte)(collapsedVarsAddress & 0xFF));
                machine.PokeMemory(23628, (byte)(collapsedVarsAddress >> 8));
                machine.PokeMemory(collapsedVarsAddress, 0x80);
                machine.PokeMemory(23641, (byte)(collapsedELineAddress & 0xFF));
                machine.PokeMemory(23642, (byte)(collapsedELineAddress >> 8));
                machine.RefreshPendingMountedLoadInterpreterContext(forceVariableAreaRefresh: true);

                object[] readerArgs = new object[] { machine, "a", 0 };
                Assert.True((bool)variableReader.Invoke(null, readerArgs)!);
                Assert.Equal(0x5CB6, (int)readerArgs[2]);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void PendingMountedLoadUsrContinuation_Preserves_Usable_BasicVariableSnapshot_When_Live_Variable_Area_Falls_Outside_BasicRam()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(30,
                        Token(249), Ascii(" "), Token(192), Ascii("24244"), NumberMarker(24244)));

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "BATLIKE",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 30,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                MethodInfo variableReader = typeof(TapLoader).GetMethod(
                    "TryReadMountedContinuationNumericVariable",
                    BindingFlags.Static | BindingFlags.NonPublic)!;

                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                ushort richELineAddress = (ushort)(varsAddress + 7);
                machine.PokeMemory(varsAddress, 0x61);
                machine.PokeMemory((ushort)(varsAddress + 1), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 2), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 3), 0xB6);
                machine.PokeMemory((ushort)(varsAddress + 4), 0x5C);
                machine.PokeMemory((ushort)(varsAddress + 5), 0x00);
                machine.PokeMemory((ushort)(varsAddress + 6), 0x80);
                machine.PokeMemory(23641, (byte)(richELineAddress & 0xFF));
                machine.PokeMemory(23642, (byte)(richELineAddress >> 8));
                machine.RefreshPendingMountedLoadInterpreterContext(forceVariableAreaRefresh: true);

                machine.PokeMemory(23627, 0x00);
                machine.PokeMemory(23628, 0x00);
                machine.PokeMemory(23641, 0x02);
                machine.PokeMemory(23642, 0x02);
                machine.RefreshPendingMountedLoadInterpreterContext(forceVariableAreaRefresh: true);

                object[] readerArgs = new object[] { machine, "a", 0 };
                Assert.True((bool)variableReader.Invoke(null, readerArgs)!);
                Assert.Equal(0x5CB6, (int)readerArgs[2]);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedTape_UsrContinuationBoundary_Remains_False_During_Pause_Before_Pending_RomLoadable_Block()
        {
            TapeBlock firstDataBlock = TapeBlock.CreateData(
                BuildDataBlock(new byte[] { 0x01, 0x02, 0x03 }),
                2168,
                3223,
                667,
                735,
                855,
                1710,
                8,
                1000);
            TapeBlock secondDataBlock = TapeBlock.CreateData(
                BuildDataBlock(new byte[] { 0x04, 0x05 }),
                2168,
                3223,
                667,
                735,
                855,
                1710,
                8,
                1000);

            var tape = new MountedTape(
                "boundary.tap",
                new[] { firstDataBlock, secondDataBlock },
                initialBlockIndex: 0,
                skipCustomHeaderForEarPlayback: false);

            static void SetField<T>(object target, string name, T value)
            {
                typeof(MountedTape)
                    .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(target, value);
            }

            FieldInfo stateField = typeof(MountedTape).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)!;
            FieldInfo earStateField = typeof(MountedTape).GetField("earPlaybackState", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object expectData = Enum.Parse(stateField.FieldType, "ExpectData");
            object pauseState = Enum.Parse(earStateField.FieldType, "Pause");

            SetField(tape, "nextBlockIndex", 1);
            SetField(tape, "earPlaybackBlockIndex", 0);
            SetField(tape, "pendingPrePlaybackPauseTStates", 0);
            SetField(tape, "retainedByteStreamTrapAvailable", false);
            stateField.SetValue(tape, expectData);
            earStateField.SetValue(tape, pauseState);

            Assert.False(tape.IsAtMountedLoadUsrContinuationBoundary);

            SetField(tape, "nextBlockIndex", 2);
            Assert.True(tape.IsAtMountedLoadUsrContinuationBoundary);
        }

        [Fact]
        public void MountedLoadUsrContinuationResolver_Refreshes_FollowOn_Usr_From_Live_TargetLine_When_Earlier_Basic_Is_Corrupted()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] line10 = BuildBasicLine(10,
                    Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050));
                byte[] line20Original = BuildBasicLine(20,
                    Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0));
                byte[] line20Patched = BuildBasicLine(20,
                    Token(249), Ascii(" "), Token(192), Ascii("4660"), NumberMarker(4660));
                byte[] basicLoader = BuildBasicProgram(line10, line20Original);

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "LIVEUSR",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);

                var resolver = (Func<Spectrum128Machine, ushort?>)resolverObject!;
                ushort programStart = 23755;
                machine.PokeMemory((ushort)(programStart + 2), 0xFF);
                machine.PokeMemory((ushort)(programStart + 3), 0x7F);

                ushort secondLineStart = (ushort)(programStart + line10.Length);
                for (int offset = 0; offset < line20Patched.Length; offset++)
                    machine.PokeMemory((ushort)(secondLineStart + offset), line20Patched[offset]);

                ushort? refreshedEntryPoint = resolver(machine);
                Assert.Equal((ushort)4660, refreshedEntryPoint);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedLoadUsrContinuationResolver_Uses_Hidden_NumberMarker_From_Live_TargetLine()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] line10 = BuildBasicLine(10,
                    Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050));
                byte[] line20 = BuildBasicLine(20,
                    Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0));
                byte[] basicLoader = BuildBasicProgram(line10, line20);

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "HIDDENUSR",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);

                var resolver = (Func<Spectrum128Machine, ushort?>)resolverObject!;
                ushort programStart = 23755;
                ushort secondLineStart = (ushort)(programStart + line10.Length);
                int markerOffsetInLine = Array.IndexOf(line20, (byte)0x0E);
                Assert.True(markerOffsetInLine >= 0);
                ushort markerAddress = (ushort)(secondLineStart + markerOffsetInLine);

                // Keep the visible BASIC text as "0" but patch the hidden numeric value
                // the way protected loaders commonly do before a follow-on USR.
                machine.PokeMemory(markerAddress, 0x0E);
                machine.PokeMemory((ushort)(markerAddress + 1), 0x00);
                machine.PokeMemory((ushort)(markerAddress + 2), 0x00);
                machine.PokeMemory((ushort)(markerAddress + 3), 0x34);
                machine.PokeMemory((ushort)(markerAddress + 4), 0x12);
                machine.PokeMemory((ushort)(markerAddress + 5), 0x00);

                ushort? refreshedEntryPoint = resolver(machine);
                Assert.Equal((ushort)0x1234, refreshedEntryPoint);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedLoadUsrContinuationPreamble_Let_Updates_Existing_SimpleNumeric_BasicVariable()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                ushort varsAddress = 0x8000;
                ushort simpleVariableAddress = (ushort)(varsAddress + 19);
                ushort eLineAddress = (ushort)(simpleVariableAddress + 7);

                machine.PokeMemory(varsAddress, 0xE9);
                for (int offset = 1; offset < 19; offset++)
                    machine.PokeMemory((ushort)(varsAddress + offset), 0x00);

                machine.PokeMemory(simpleVariableAddress, 0x61);
                machine.PokeMemory((ushort)(simpleVariableAddress + 1), 0x00);
                machine.PokeMemory((ushort)(simpleVariableAddress + 2), 0x00);
                machine.PokeMemory((ushort)(simpleVariableAddress + 3), 0x5B);
                machine.PokeMemory((ushort)(simpleVariableAddress + 4), 0x00);
                machine.PokeMemory((ushort)(simpleVariableAddress + 5), 0x00);
                machine.PokeMemory((ushort)(simpleVariableAddress + 6), 0x80);

                machine.PokeMemory(23627, (byte)(varsAddress & 0xFF));
                machine.PokeMemory(23628, (byte)(varsAddress >> 8));
                machine.PokeMemory(23641, (byte)(eLineAddress & 0xFF));
                machine.PokeMemory(23642, (byte)(eLineAddress >> 8));

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo preambleMethod = executorType.GetMethod(
                    "ExecuteMountedUsrContinuationPreamble",
                    BindingFlags.NonPublic | BindingFlags.Static)!;

                var variables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                string[][] preamble = new[]
                {
                    new[] { "LET", "a", "=", "23734" }
                };

                preambleMethod.Invoke(null, new object[] { machine, variables, preamble });

                Assert.Equal(23734, variables["a"]);
                Assert.Equal((byte)0x61, machine.PeekMemory(simpleVariableAddress));
                Assert.Equal((byte)0x00, machine.PeekMemory((ushort)(simpleVariableAddress + 1)));
                Assert.Equal((byte)0x00, machine.PeekMemory((ushort)(simpleVariableAddress + 2)));
                Assert.Equal((byte)0xB6, machine.PeekMemory((ushort)(simpleVariableAddress + 3)));
                Assert.Equal((byte)0x5C, machine.PeekMemory((ushort)(simpleVariableAddress + 4)));
                Assert.Equal((byte)0x00, machine.PeekMemory((ushort)(simpleVariableAddress + 5)));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void MountedLoadUsrContinuationResolver_Drops_Stale_FollowOn_Usr_Zero_When_Live_TargetLine_Is_Gone()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] line10 = BuildBasicLine(10,
                    Token(249), Ascii(" "), Token(192), Ascii("24050"), NumberMarker(24050));
                byte[] line20 = BuildBasicLine(20,
                    Token(249), Ascii(" "), Token(192), Ascii("24576"), NumberMarker(24576));
                byte[] line30 = BuildBasicLine(30,
                    Token(249), Ascii(" "), Token(192), Ascii("0"), NumberMarker(0));
                byte[] basicLoader = BuildBasicProgram(line10, line20, line30);

                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "USRZERO",
                    dataLength: (ushort)basicLoader.Length,
                    parameter1: 10,
                    parameter2: (ushort)basicLoader.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);

                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

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

                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo createResolver = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, basicLoader });

                object? resolverObject = createResolver.Invoke(null, new object[] { machine, (ushort)23755, (ushort)basicLoader.Length, (ushort)10 });
                Assert.NotNull(resolverObject);

                var resolver = (Func<Spectrum128Machine, ushort?>)resolverObject!;
                ushort? firstEntryPoint = resolver(machine);
                Assert.Equal((ushort)24576, firstEntryPoint);

                object? followOnResolverObject = machine.PendingMountedLoadResolver;
                Assert.NotNull(followOnResolverObject);
                var followOnResolver = (Func<Spectrum128Machine, ushort?>)followOnResolverObject!;

                // Simulate the loaded code reclaiming/overwriting the follow-on BASIC
                // text before the cached RANDOMIZE USR 0 can be validated.
                ushort programStart = 23755;
                ushort line30Start = (ushort)(programStart + line10.Length + line20.Length);
                for (int offset = 0; offset < line30.Length; offset++)
                    machine.PokeMemory((ushort)(line30Start + offset), 0xFF);

                ushort? secondEntryPoint = followOnResolver(machine);
                Assert.Null(secondEntryPoint);
                Assert.Null(machine.PendingMountedLoadResolver);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        private static void WriteSpectrumPositiveIntegerFloat(Spectrum128Machine machine, ushort address, int value)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            int bitLength = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)value);
            int exponent = 128 + bitLength;
            uint mantissa = (uint)value << (32 - bitLength);

            machine.PokeMemory(address, (byte)exponent);
            machine.PokeMemory((ushort)(address + 1), (byte)((mantissa >> 24) & 0x7F));
            machine.PokeMemory((ushort)(address + 2), (byte)(mantissa >> 16));
            machine.PokeMemory((ushort)(address + 3), (byte)(mantissa >> 8));
            machine.PokeMemory((ushort)(address + 4), (byte)mantissa);
        }
    }
}
