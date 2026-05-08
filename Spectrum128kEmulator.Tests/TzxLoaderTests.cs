using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class TzxLoaderTests
    {
        [Fact]
        public void ParseBlocks_Parses_Standard_Data_And_Metadata_Blocks()
        {
            byte[] tzx = BuildTzx(
                BuildTextDescriptionBlock("test"),
                BuildStandardSpeedDataBlock(new byte[] { 0x00, 0x01, 0x02, 0x03 }, pauseMs: 1000));

            var blocks = Tap.TzxLoader.ParseBlocks(tzx);

            Assert.Equal(2, blocks.Count);
            Assert.Equal(Tap.TapeBlockKind.Metadata, blocks[0].Kind);
            Assert.Equal(Tap.TapeBlockKind.Data, blocks[1].Kind);
            Assert.Equal((byte)0x00, blocks[1].Flag);
            Assert.Equal(1000, blocks[1].PauseAfterBlockMs);
            Assert.Equal(new byte[] { 0x01, 0x02 }, blocks[1].Payload);
        }

        [Fact]
        public void ParseBlocks_Parses_PureTone_PulseSequence_And_PureData()
        {
            byte[] tzx = BuildTzx(
                BuildPureToneBlock(2168, 32),
                BuildPulseSequenceBlock(855, 1710),
                BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 4, pauseMs: 250));

            var blocks = Tap.TzxLoader.ParseBlocks(tzx);

            Assert.Equal(3, blocks.Count);
            Assert.Equal(Tap.TapeBlockKind.PureTone, blocks[0].Kind);
            Assert.Equal((ushort)2168, blocks[0].PureTonePulseLength);
            Assert.Equal((ushort)32, blocks[0].PureTonePulseCount);
            Assert.Equal(Tap.TapeBlockKind.PulseSequence, blocks[1].Kind);
            Assert.Equal(new int[] { 855, 1710 }, blocks[1].PulseSequence);
            Assert.Equal(Tap.TapeBlockKind.Data, blocks[2].Kind);
            Assert.Equal((byte)4, blocks[2].UsedBitsInLastByte);
            Assert.Equal(250, blocks[2].PauseAfterBlockMs);
        }

        [Fact]
        public void ParseBlocks_Parses_DirectRecording_Csw_And_SetSignalLevel()
        {
            byte[] tzx = BuildTzx(
                BuildSetSignalLevelBlock(high: true),
                BuildDirectRecordingBlock(new byte[] { 0b1010_0000 }, tStatesPerSample: 79, usedBitsInLastByte: 4, pauseMs: 500),
                BuildCswRleBlock(new byte[] { 10, 20, 30 }, sampleRate: 44100, pauseMs: 250));

            var blocks = Tap.TzxLoader.ParseBlocks(tzx);

            Assert.Equal(3, blocks.Count);
            Assert.Equal(Tap.TapeBlockKind.SetSignalLevel, blocks[0].Kind);
            Assert.True(blocks[0].SignalLevel);
            Assert.Equal(Tap.TapeBlockKind.DirectRecording, blocks[1].Kind);
            Assert.Equal((ushort)79, blocks[1].DirectRecordingSampleTStates);
            Assert.Equal((byte)4, blocks[1].UsedBitsInLastByte);
            Assert.Equal(Tap.TapeBlockKind.PulseSequence, blocks[2].Kind);
            Assert.NotNull(blocks[2].PulseSequence);
            Assert.Equal(3, blocks[2].PulseSequence!.Length);
            Assert.Equal(250, blocks[2].PauseAfterBlockMs);
        }

        [Fact]
        public void ParseBlocks_Resolves_Jump_Loop_And_Call_Control_Flow()
        {
            byte[] dataA = BuildStandardSpeedDataBlock(new byte[] { 0x00, 0xAA, 0xAA }, pauseMs: 0);
            byte[] dataB = BuildStandardSpeedDataBlock(new byte[] { 0x00, 0xBB, 0xBB }, pauseMs: 0);
            byte[] dataC = BuildStandardSpeedDataBlock(new byte[] { 0x00, 0xCC, 0xCC }, pauseMs: 0);

            byte[] tzx = BuildTzx(
                BuildJumpBlock(2),
                dataA,
                dataB,
                BuildLoopStartBlock(2),
                dataC,
                BuildLoopEndBlock(),
                BuildCallSequenceBlock(3),
                BuildTextDescriptionBlock("tail"),
                BuildJumpBlock(3),
                BuildTextDescriptionBlock("subroutine"),
                BuildReturnBlock());

            var blocks = Tap.TzxLoader.ParseBlocks(tzx);

            Assert.Equal(5, blocks.Count);
            Assert.Equal((byte)0x00, blocks[0].Flag);
            Assert.Equal((byte)0xCC, blocks[1].Payload![0]);
            Assert.Equal((byte)0xCC, blocks[2].Payload![0]);
            Assert.Equal(Tap.TapeBlockKind.Metadata, blocks[3].Kind);
            Assert.Equal(Tap.TapeBlockKind.Metadata, blocks[4].Kind);
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Loads_Standard_Leading_Blocks()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "test.tzx");

            try
            {
                byte[] basicHeader = BuildSpectrumHeaderBlock(
                    type: 0,
                    fileName: "loader",
                    dataLength: 4,
                    parameter1: 10,
                    parameter2: 4);
                byte[] basicData = BuildSpectrumDataBlock(new byte[] { 0x01, 0x02, 0x03, 0x04 });
                byte[] codeHeader = BuildSpectrumHeaderBlock(
                    type: 3,
                    fileName: "code",
                    dataLength: 4,
                    parameter1: 0x8000,
                    parameter2: 0);
                byte[] codeData = BuildSpectrumDataBlock(new byte[] { 0xAA, 0xBB, 0xCC, 0xDD });

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(basicHeader, pauseMs: 1000),
                        BuildStandardSpeedDataBlock(basicData, pauseMs: 1000),
                        BuildStandardSpeedDataBlock(codeHeader, pauseMs: 1000),
                        BuildStandardSpeedDataBlock(codeData, pauseMs: 1000),
                        BuildTextDescriptionBlock("tail")));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TapBootstrapResult result = Tap.TzxLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(5, result.TotalBlockCount);
                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal("loader", result.AutoStartFileName);
                Assert.True(machine.HasMountedTape);
                Assert.Equal((byte)0x01, machine.PeekMemory(23755));
                Assert.Equal((byte)0x00, machine.PeekMemory(0x8000));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Skips_Leading_Metadata()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "meta-first.tzx");

            try
            {
                byte[] basicHeader = BuildSpectrumHeaderBlock(
                    type: 0,
                    fileName: "loader",
                    dataLength: 4,
                    parameter1: 10,
                    parameter2: 4);
                byte[] basicData = BuildSpectrumDataBlock(new byte[] { 0x01, 0x02, 0x03, 0x04 });

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildArchiveInfoBlock(),
                        BuildStandardSpeedDataBlock(basicHeader, pauseMs: 1000),
                        BuildStandardSpeedDataBlock(basicData, pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TapBootstrapResult result = Tap.TzxLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(3, result.TotalBlockCount);
                Assert.Equal(3, result.ConsumedBlockCount);
                Assert.Equal("loader", result.AutoStartFileName);
                Assert.Equal((byte)0x01, machine.PeekMemory(23755));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Executes_Peek_Based_AutoStart()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "peek-autorun.tzx");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(249), Ascii(" "), Token(192), Ascii("(("),
                        Token(190), Ascii("23635"), NumberMarker(23635),
                        Ascii("+256"), NumberMarker(256), Ascii("*"),
                        Token(190), Ascii("23636"), NumberMarker(23636),
                        Ascii(")+10"), NumberMarker(10), Ascii(")")));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "TR", dataLength: (ushort)basicProgram.Length, parameter1: 0, parameter2: (ushort)basicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(basicProgram), pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TapBootstrapResult result = Tap.TzxLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal((ushort)(23755 + 10), machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0, ReadWord(machine, 23618));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Consumes_Custom_Remainder_Through_Load_Statement()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "custom-remainder.tzx");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10, Token(239), Ascii(" \"\""), Ascii(":"),
                        Token(249), Ascii(" "), Token(192), Ascii(" 64512"), NumberMarker(64512)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(basicProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 42, fileName: "FAST", dataLength: 1, parameter1: 0x8000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(new byte[] { 0x99 }), pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);
                Tap.TzxLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.False(machine.HasMountedTape);
                Assert.Equal((ushort)0xFC00, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0x99, machine.PeekMemory(0x8000));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Treats_AutoStart_Line_Zero_As_First_Line()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "line10-autorun.tzx");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "AUTO", dataLength: (ushort)basicProgram.Length, parameter1: 0, parameter2: (ushort)basicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(basicProgram), pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TapBootstrapResult result = Tap.TzxLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0, ReadWord(machine, 23618));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Executes_MultiLoad_Basic_Without_Mounted_Tape()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "full-standard.tzx");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(235), Ascii("i"), Ascii("="), Ascii("1"), NumberMarker(1), Ascii(" "),
                        Token(204), Ascii("2"), NumberMarker(2),
                        Ascii(":"), Token(239), Ascii("\"\" "), Token(175),
                        Ascii(":"), Token(243), Ascii("i"),
                        Ascii(":"), Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "FULL", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(basicProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 3, fileName: "ONE", dataLength: 1, parameter1: 0x9000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(new byte[] { 0xAA }), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 3, fileName: "TWO", dataLength: 3, parameter1: 0x8000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(new byte[] { 0x00, 0x01, 0x02 }), pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TapBootstrapResult result = Tap.TzxLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

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
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Loads_Custom_Header_Data_To_Parameter1_Address()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "custom-full.tzx");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                byte[] customData = new byte[] { 0xC3, 0x78, 0x56, 0x42 };

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "AUTO", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(basicProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 42, fileName: "FAST", dataLength: (ushort)customData.Length, parameter1: 0x9000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(customData), pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TapBootstrapResult result = Tap.TzxLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.Equal(4, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                Assert.Equal((byte)0xC3, machine.PeekMemory(0x9000));
                Assert.Equal((byte)0x78, machine.PeekMemory(0x9001));
                Assert.Equal((byte)0x56, machine.PeekMemory(0x9002));
                Assert.Equal((byte)0x42, machine.PeekMemory(0x9003));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Uses_Mounted_Path_For_Poke_Selected_128k_Loads()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "banked-standard.tzx");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(227), Ascii("a"),
                        Ascii(":"), Token(244), Ascii("23388"), NumberMarker(23388), Ascii(","), Ascii("16"), NumberMarker(16), Ascii("+"), Ascii("a"),
                        Ascii(":"), Token(239), Ascii("\"\""),
                        Ascii(":"), Token(227), Ascii("a"),
                        Ascii(":"), Token(244), Ascii("23388"), NumberMarker(23388), Ascii(","), Ascii("16"), NumberMarker(16), Ascii("+"), Ascii("a"),
                        Ascii(":"), Token(239), Ascii("\"\""),
                        Ascii(":"), Token(249), Ascii(" "), Token(192), Ascii("49152"), NumberMarker(49152)),
                    BuildBasicLine(20,
                        Token(228),
                        Ascii("3"), NumberMarker(3), Ascii(","),
                        Ascii("4"), NumberMarker(4)));

                byte[] firstCode = new byte[] { 0xAA, 0xAB };
                byte[] secondCode = new byte[] { 0xBB, 0xBC };

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BANKED", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(basicProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 3, fileName: "ONE", dataLength: (ushort)firstCode.Length, parameter1: 0xC000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(firstCode), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 3, fileName: "TWO", dataLength: (ushort)secondCode.Length, parameter1: 0xC000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(secondCode), pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TapBootstrapResult result = Tap.TzxLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

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

        private static ushort ReadWord(Spectrum128Machine machine, ushort address)
        {
            return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
        }

        private static byte[] BuildTzx(params byte[][] blocks)
        {
            using var ms = new MemoryStream();
            ms.Write(System.Text.Encoding.ASCII.GetBytes("ZXTape!"));
            ms.WriteByte(0x1A);
            ms.WriteByte(1);
            ms.WriteByte(20);
            foreach (byte[] block in blocks)
                ms.Write(block, 0, block.Length);
            return ms.ToArray();
        }

        private static object GetPrivateField(object target, string name)
        {
            FieldInfo? field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return field!.GetValue(target)!;
        }

        private static byte[] BuildTextDescriptionBlock(string text)
        {
            byte[] bytes = System.Text.Encoding.ASCII.GetBytes(text);
            return new byte[] { 0x30, (byte)bytes.Length }.Concat(bytes).ToArray();
        }

        private static byte[] BuildArchiveInfoBlock()
        {
            return new byte[] { 0x32, 0x02, 0x00, 0x00, 0x00 };
        }

        private static byte[] BuildStandardSpeedDataBlock(byte[] streamData, ushort pauseMs)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x10);
            ms.WriteByte((byte)(pauseMs & 0xFF));
            ms.WriteByte((byte)(pauseMs >> 8));
            ms.WriteByte((byte)(streamData.Length & 0xFF));
            ms.WriteByte((byte)(streamData.Length >> 8));
            ms.Write(streamData, 0, streamData.Length);
            return ms.ToArray();
        }

        private static byte[] BuildSpectrumHeaderBlock(byte type, string fileName, ushort dataLength, ushort parameter1, ushort parameter2)
        {
            byte[] payload = new byte[17];
            payload[0] = type;
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(fileName.PadRight(10).Substring(0, 10));
            Array.Copy(nameBytes, 0, payload, 1, 10);
            payload[11] = (byte)(dataLength & 0xFF);
            payload[12] = (byte)(dataLength >> 8);
            payload[13] = (byte)(parameter1 & 0xFF);
            payload[14] = (byte)(parameter1 >> 8);
            payload[15] = (byte)(parameter2 & 0xFF);
            payload[16] = (byte)(parameter2 >> 8);
            return BuildSpectrumStream(0x00, payload);
        }

        private static byte[] BuildSpectrumDataBlock(byte[] payload)
        {
            return BuildSpectrumStream(0xFF, payload);
        }

        private static byte[] BuildSpectrumStream(byte flag, byte[] payload)
        {
            byte[] stream = new byte[payload.Length + 2];
            stream[0] = flag;
            Array.Copy(payload, 0, stream, 1, payload.Length);
            byte checksum = 0;
            for (int i = 0; i < stream.Length - 1; i++)
                checksum ^= stream[i];
            stream[^1] = checksum;
            return stream;
        }

        private static byte[] BuildPureToneBlock(ushort pulseLength, ushort pulseCount)
        {
            return new byte[]
            {
                0x12,
                (byte)(pulseLength & 0xFF), (byte)(pulseLength >> 8),
                (byte)(pulseCount & 0xFF), (byte)(pulseCount >> 8)
            };
        }

        private static byte[] BuildPulseSequenceBlock(params ushort[] pulses)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x13);
            ms.WriteByte((byte)pulses.Length);
            foreach (ushort pulse in pulses)
            {
                ms.WriteByte((byte)(pulse & 0xFF));
                ms.WriteByte((byte)(pulse >> 8));
            }
            return ms.ToArray();
        }

        private static byte[] BuildPureDataBlock(byte[] streamData, byte usedBitsInLastByte, ushort pauseMs)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x14);
            ms.WriteByte(0x57);
            ms.WriteByte(0x03);
            ms.WriteByte(0xAE);
            ms.WriteByte(0x06);
            ms.WriteByte(usedBitsInLastByte);
            ms.WriteByte((byte)(pauseMs & 0xFF));
            ms.WriteByte((byte)(pauseMs >> 8));
            ms.WriteByte((byte)(streamData.Length & 0xFF));
            ms.WriteByte((byte)((streamData.Length >> 8) & 0xFF));
            ms.WriteByte((byte)((streamData.Length >> 16) & 0xFF));
            ms.Write(streamData, 0, streamData.Length);
            return ms.ToArray();
        }

        private static byte[] BuildSetSignalLevelBlock(bool high)
        {
            return new byte[] { 0x2B, 0x01, 0x00, 0x00, 0x00, high ? (byte)0x01 : (byte)0x00 };
        }

        private static byte[] BuildDirectRecordingBlock(byte[] sampleData, ushort tStatesPerSample, byte usedBitsInLastByte, ushort pauseMs)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x15);
            ms.WriteByte((byte)(tStatesPerSample & 0xFF));
            ms.WriteByte((byte)(tStatesPerSample >> 8));
            ms.WriteByte((byte)(pauseMs & 0xFF));
            ms.WriteByte((byte)(pauseMs >> 8));
            ms.WriteByte(usedBitsInLastByte);
            ms.WriteByte((byte)(sampleData.Length & 0xFF));
            ms.WriteByte((byte)((sampleData.Length >> 8) & 0xFF));
            ms.WriteByte((byte)((sampleData.Length >> 16) & 0xFF));
            ms.Write(sampleData, 0, sampleData.Length);
            return ms.ToArray();
        }

        private static byte[] BuildCswRleBlock(byte[] pulses, int sampleRate, ushort pauseMs)
        {
            using var ms = new MemoryStream();
            uint blockLength = (uint)(10 + pulses.Length);
            ms.WriteByte(0x18);
            ms.WriteByte((byte)(blockLength & 0xFF));
            ms.WriteByte((byte)((blockLength >> 8) & 0xFF));
            ms.WriteByte((byte)((blockLength >> 16) & 0xFF));
            ms.WriteByte((byte)((blockLength >> 24) & 0xFF));
            ms.WriteByte((byte)(pauseMs & 0xFF));
            ms.WriteByte((byte)(pauseMs >> 8));
            ms.WriteByte((byte)(sampleRate & 0xFF));
            ms.WriteByte((byte)((sampleRate >> 8) & 0xFF));
            ms.WriteByte((byte)((sampleRate >> 16) & 0xFF));
            ms.WriteByte(0x01);
            ms.WriteByte((byte)(pulses.Length & 0xFF));
            ms.WriteByte((byte)((pulses.Length >> 8) & 0xFF));
            ms.WriteByte((byte)((pulses.Length >> 16) & 0xFF));
            ms.WriteByte((byte)((pulses.Length >> 24) & 0xFF));
            ms.Write(pulses, 0, pulses.Length);
            return ms.ToArray();
        }

        private static byte[] BuildJumpBlock(short relativeOffset)
        {
            return new byte[] { 0x23, (byte)(relativeOffset & 0xFF), (byte)((relativeOffset >> 8) & 0xFF) };
        }

        private static byte[] BuildLoopStartBlock(ushort repetitions)
        {
            return new byte[] { 0x24, (byte)(repetitions & 0xFF), (byte)(repetitions >> 8) };
        }

        private static byte[] BuildLoopEndBlock()
        {
            return new byte[] { 0x25 };
        }

        private static byte[] BuildCallSequenceBlock(params short[] offsets)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x26);
            ms.WriteByte((byte)(offsets.Length & 0xFF));
            ms.WriteByte((byte)(offsets.Length >> 8));
            foreach (short offset in offsets)
            {
                ms.WriteByte((byte)(offset & 0xFF));
                ms.WriteByte((byte)((offset >> 8) & 0xFF));
            }

            return ms.ToArray();
        }

        private static byte[] BuildReturnBlock()
        {
            return new byte[] { 0x27 };
        }

        private static byte[] BuildBasicProgram(params byte[][] lines)
        {
            using var ms = new MemoryStream();
            foreach (byte[] line in lines)
                ms.Write(line, 0, line.Length);
            return ms.ToArray();
        }

        private static byte[] BuildBasicLine(ushort lineNumber, params byte[][] bodyParts)
        {
            using var ms = new MemoryStream();
            foreach (byte[] part in bodyParts)
                ms.Write(part, 0, part.Length);
            ms.WriteByte(0x0D);
            byte[] body = ms.ToArray();
            return new byte[]
            {
                (byte)(lineNumber >> 8),
                (byte)(lineNumber & 0xFF),
                (byte)(body.Length & 0xFF),
                (byte)(body.Length >> 8)
            }.Concat(body).ToArray();
        }

        private static byte[] Token(byte value) => new[] { value };
        private static byte[] Ascii(string text) => System.Text.Encoding.ASCII.GetBytes(text);

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
    }
}
