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
            Assert.False(blocks[2].IsLoadableRomBlock);
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
        public void Mount_Starts_Tzx_Playback_With_Low_Ear_Level()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "low-start.tzx");

            try
            {
                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildPureToneBlock(2168, 4)));

                var machine = new Spectrum128Machine(romFolder);

                Tap.TzxLoader.Mount(machine, tapePath);

                Assert.True(machine.HasMountedTape);
                FieldInfo earLevelField = typeof(Tap.MountedTape).GetField("earLevel", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.False((bool)earLevelField.GetValue(machine.MountedTape!)!);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadPolicy_Uses_High_Start_For_Standard_Rom_Tzx_And_Low_Start_For_Mixed_Tzx()
        {
            MethodInfo usesStandardRomSignalStart = typeof(Tap.TzxLoader).GetMethod(
                "UsesStandardRomSignalStart",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            Tap.TapeBlock standardBlock = Tap.TapeBlock.CreateData(
                new byte[] { 0x00, 0x00 },
                2168,
                8063,
                667,
                735,
                855,
                1710,
                8,
                1000);
            Tap.TapeBlock protectedBlock = Tap.TapeBlock.CreatePureTone(2168, 32);

            bool standardStartHigh = (bool)usesStandardRomSignalStart.Invoke(
                null,
                new object[] { new[] { standardBlock } })!;
            bool mixedStartHigh = (bool)usesStandardRomSignalStart.Invoke(
                null,
                new object[] { new[] { standardBlock, protectedBlock } })!;

            Assert.True(standardStartHigh);
            Assert.False(mixedStartHigh);
        }

        [Fact]
        public void MountedTape_ProtectedTail_EndOfStream_Returns_Ear_High()
        {
            var tape = new Tap.MountedTape(
                "protected-tail",
                new[]
                {
                    Tap.TapeBlock.CreateByteStreamData(
                        new byte[] { 0xA5 },
                        zeroBitPulseLength: 855,
                        oneBitPulseLength: 1710,
                        usedBitsInLastByte: 8,
                        pauseAfterBlockMs: 1)
                },
                initialEarLevelHigh: false);

            bool sawEndTransition = false;
            for (ulong tStates = 0; tStates < 200000; tStates += 128)
            {
                tape.ReadEarBit(tStates);
                if (tape.DebugPlaybackState.Contains("EarState=EndOfStreamTransition", StringComparison.Ordinal))
                {
                    sawEndTransition = true;
                }

                if (!tape.IsActivelyDrivingEarLine)
                    break;
            }

            Assert.True(sawEndTransition);
            Assert.Contains("EarState=Idle", tape.DebugPlaybackState, StringComparison.Ordinal);
            Assert.Contains("EarLevel=1", tape.DebugPlaybackState, StringComparison.Ordinal);
        }

        [Fact]
        public void LoadWithPolicy_Uses_Explicit_TapePlan_Layer()
        {
            string romFolder = CreateTempRoms();
            string standardPath = Path.Combine(romFolder, "standard.tzx");
            string romDrivenPath = Path.Combine(romFolder, "rom-driven.tzx");
            string hybridPath = Path.Combine(romFolder, "hybrid.tzx");
            string chainedBasicPrefixPath = Path.Combine(romFolder, "chained-basic-prefix.tzx");

            try
            {
                byte[] fullLoadProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                byte[] mountedLoadProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(239)));
                byte[] protectedBasicProgram = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("5"), NumberMarker(5)));
                byte[] opaqueBasicProgram = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(242), Ascii("0"), NumberMarker(0)));

                File.WriteAllBytes(
                    standardPath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "AUTO", dataLength: (ushort)fullLoadProgram.Length, parameter1: 10, parameter2: (ushort)fullLoadProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(fullLoadProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 3, fileName: "CODE", dataLength: 1, parameter1: 0x8000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(new byte[] { 0xAA }), pauseMs: 1000)));

                File.WriteAllBytes(
                    romDrivenPath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)mountedLoadProgram.Length, parameter1: 10, parameter2: (ushort)mountedLoadProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(mountedLoadProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)opaqueBasicProgram.Length, parameter1: 0, parameter2: (ushort)opaqueBasicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(opaqueBasicProgram), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                File.WriteAllBytes(
                    hybridPath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)mountedLoadProgram.Length, parameter1: 10, parameter2: (ushort)mountedLoadProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(mountedLoadProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 3, fileName: "FAST", dataLength: 1, parameter1: 0x8000, parameter2: 0), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(new byte[] { 0x99 }), pauseMs: 1000)));

                byte[] firstStagePatch = BuildBasicProgram(
                    BuildBasicLine(10, Token(239)));
                byte[] secondBasicStage = BuildBasicProgram(
                    BuildBasicLine(0, Token(239)));
                byte[] thirdProtectedStage = BuildBasicProgram(
                    BuildBasicLine(0, Ascii("Protected by SPEEDLOCK")),
                    BuildBasicLine(0,
                        Token(217), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(216), Ascii("7"), NumberMarker(7),
                        Ascii(":"), Token(251),
                        Ascii(":"), Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("0"), NumberMarker(0)));

                File.WriteAllBytes(
                    chainedBasicPrefixPath,
                    BuildTzx(
                        BuildArchiveInfoBlock(),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "PATCH", dataLength: (ushort)firstStagePatch.Length, parameter1: 9000, parameter2: (ushort)firstStagePatch.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(firstStagePatch), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IMPOSS", dataLength: (ushort)secondBasicStage.Length, parameter1: 0, parameter2: (ushort)secondBasicStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(secondBasicStage), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)thirdProtectedStage.Length, parameter1: 0, parameter2: (ushort)thirdProtectedStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(thirdProtectedStage), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                var standardMachine = new Spectrum128Machine(romFolder);
                var standardResult = Tap.TzxLoader.LoadWithPolicy(standardMachine, standardPath);
                Assert.Equal("FullFakeLoad", standardResult.Strategy.ToString());

                var romDrivenMachine = new Spectrum128Machine(romFolder);
                var romDrivenResult = Tap.TzxLoader.LoadWithPolicy(romDrivenMachine, romDrivenPath);
                Assert.Equal("RomBootstrapMounted", romDrivenResult.Strategy.ToString());

                var hybridMachine = new Spectrum128Machine(romFolder);
                var hybridResult = Tap.TzxLoader.LoadWithPolicy(hybridMachine, hybridPath);
                Assert.Equal("BootstrapHybrid", hybridResult.Strategy.ToString());

                var chainedBasicPrefixMachine = new Spectrum128Machine(romFolder);
                var chainedBasicPrefixResult = Tap.TzxLoader.LoadWithPolicy(chainedBasicPrefixMachine, chainedBasicPrefixPath);
                Assert.Equal("BootstrapHybrid", chainedBasicPrefixResult.Strategy.ToString());

                string mountedMixedPath = Path.Combine(romFolder, "mounted-mixed.tzx");
                byte[] mountedBootstrapProgram = BuildBasicProgram(
                    BuildBasicLine(10, Token(249), Ascii("24050"), NumberMarker(24050)));
                File.WriteAllBytes(
                    mountedMixedPath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BATMAN", dataLength: (ushort)mountedBootstrapProgram.Length, parameter1: 10, parameter2: (ushort)mountedBootstrapProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(mountedBootstrapProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(new byte[] { 0x00, 0x10, 0x20, 0x30 }, pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(new byte[] { 0x05, 0x06, 0x07, 0x08 }), pauseMs: 1000)));

                var mountedMixedMachine = new Spectrum128Machine(romFolder);
                var mountedMixedResult = Tap.TzxLoader.LoadWithPolicy(mountedMixedMachine, mountedMixedPath);
                Assert.Equal("BootstrapHybrid", mountedMixedResult.Strategy.ToString());

                MethodInfo prepareBlocksForExecution = typeof(Tap.TzxLoader).GetMethod(
                    "PrepareBlocksForExecution",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                IReadOnlyList<Tap.TapeBlock> mountedMixedBlocks = (IReadOnlyList<Tap.TapeBlock>)prepareBlocksForExecution.Invoke(
                    null,
                    new object[] { Tap.TzxLoader.ParseBlocks(File.ReadAllBytes(mountedMixedPath)) })!;
                Assert.True(mountedMixedBlocks[2].IsLoadableRomBlock);
                Assert.True(mountedMixedBlocks[3].IsLoadableRomBlock);
                Assert.True(mountedMixedBlocks[2].CanUseRomLoadTrap);
                Assert.True(mountedMixedBlocks[3].CanUseRomLoadTrap);

                Exception? mountedMixedPlaybackException = Record.Exception(() =>
                {
                    for (int frame = 0; frame < 50; frame++)
                        mountedMixedMachine.ExecuteFrame();
                });
                Assert.Null(mountedMixedPlaybackException);

                string protectedHybridPath = Path.Combine(romFolder, "protected-hybrid.tzx");
                File.WriteAllBytes(
                    protectedHybridPath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)mountedLoadProgram.Length, parameter1: 10, parameter2: (ushort)mountedLoadProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(mountedLoadProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)protectedBasicProgram.Length, parameter1: 0, parameter2: (ushort)protectedBasicProgram.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(protectedBasicProgram), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                var protectedHybridMachine = new Spectrum128Machine(romFolder);
                var protectedHybridResult = Tap.TzxLoader.LoadWithPolicy(protectedHybridMachine, protectedHybridPath);
                Assert.Equal("LeadingStandardChainFakeLoad", protectedHybridResult.Strategy.ToString());
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void RawStandardMountedRemainder_Uses_BootstrapHybrid_Path()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "rom-bootstrap-initial-usr.tzx");

            try
            {
                byte[] basicProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(
                            BuildSpectrumHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)basicProgram.Length, parameter1: 10, parameter2: (ushort)basicProgram.Length),
                            pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(basicProgram), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(new byte[] { 0x00, 0x10, 0x20, 0x30 }, pauseMs: 1000)));

                var machine = new Spectrum128Machine(romFolder);
                Tap.TapeExecutionResult result = Tap.TzxLoader.LoadWithPolicy(machine, tapePath);

                Assert.Equal("BootstrapHybrid", result.Strategy.ToString());
                Assert.True(machine.HasMountedTape);
                Assert.True(machine.HasPendingMountedLoadUsrContinuation);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void TryExecuteLoadedMountedBasicProgram_ImmediateProtectedHandoffStage_DoesNotArmMountedContinuation()
        {
            string romFolder = CreateTempRoms();

            try
            {
                byte[] protectedHandoffProgram = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(231), Ascii("1"), NumberMarker(1),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(217), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(251),
                        Ascii(":"), Token(244), Ascii("23662"), NumberMarker(23662),
                        Ascii(","), Token(190), Ascii("23618"), NumberMarker(23618)));

                Tap.TapeBlock headerBlock = Tap.TapeBlock.CreateData(
                    BuildSpectrumHeaderBlock(
                        type: 0,
                        fileName: "IM1",
                        dataLength: (ushort)protectedHandoffProgram.Length,
                        parameter1: 0,
                        parameter2: (ushort)protectedHandoffProgram.Length),
                    2168,
                    8063,
                    667,
                    735,
                    855,
                    1710,
                    8,
                    1000);

                MethodInfo parseHeaderInfo = typeof(Tap.TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;

                MethodInfo loadBasicProgram = typeof(Tap.TapLoader)
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

                var machine = new Spectrum128Machine(romFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, protectedHandoffProgram });

                MethodInfo tryExecuteLoadedMountedBasicProgram = typeof(Tap.TapLoader).GetMethod(
                    "TryExecuteLoadedMountedBasicProgram",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                bool executed = (bool)tryExecuteLoadedMountedBasicProgram.Invoke(
                    null,
                    new object[]
                    {
                        machine,
                        (ushort)protectedHandoffProgram.Length,
                        (ushort)protectedHandoffProgram.Length,
                        (ushort)0
                    })!;

                Assert.True(executed);
                Assert.False(machine.HasPendingMountedLoadUsrContinuation);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadWithPolicy_DoesNotDependOnCurrentMachineMode_For_StopIf48k_Blocks()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "stop-if-48k.tzx");

            try
            {
                byte[] autorunProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(
                            BuildSpectrumHeaderBlock(type: 0, fileName: "AUTO", dataLength: (ushort)autorunProgram.Length, parameter1: 10, parameter2: (ushort)autorunProgram.Length),
                            pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(autorunProgram), pauseMs: 1000),
                        BuildStopIf48kBlock(),
                        BuildStandardSpeedDataBlock(
                            BuildSpectrumHeaderBlock(type: 3, fileName: "CODE", dataLength: 1, parameter1: 0x8000, parameter2: 0),
                            pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(new byte[] { 0xAA }), pauseMs: 1000)));

                var freshMachine = new Spectrum128Machine(romFolder);
                Tap.TapeExecutionResult freshResult = Tap.TzxLoader.LoadWithPolicy(freshMachine, tapePath);

                var stale48kMachine = new Spectrum128Machine(romFolder);
                stale48kMachine.Reset();
                stale48kMachine.ConfigureFor48kTapeLoad(borderColor: 0);
                Tap.TapeExecutionResult staleResult = Tap.TzxLoader.LoadWithPolicy(stale48kMachine, tapePath);

                Assert.Equal(freshResult.Strategy, staleResult.Strategy);
                Assert.Equal(freshResult.TotalBlockCount, staleResult.TotalBlockCount);
                Assert.Equal(freshResult.ConsumedBlockCount, staleResult.ConsumedBlockCount);
                Assert.True(freshMachine.HasMountedTape == stale48kMachine.HasMountedTape);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadWithPolicy_LeadingStandardBasicChain_Remounts_ProtectedRemainder_After_FakeLoaded_BasicPrefix()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "bugfix-shape.tzx");

            try
            {
                byte[] firstStagePatch = BuildBasicProgram(
                    BuildBasicLine(10, Token(239)));
                byte[] secondBasicStage = BuildBasicProgram(
                    BuildBasicLine(0, Token(239)));
                byte[] thirdProtectedStage = BuildBasicProgram(
                    BuildBasicLine(0, Ascii("Protected by SPEEDLOCK")),
                    BuildBasicLine(0,
                        Token(217), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(216), Ascii("7"), NumberMarker(7),
                        Ascii(":"), Token(251),
                        Ascii(":"), Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("0"), NumberMarker(0)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildArchiveInfoBlock(),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "PATCH", dataLength: (ushort)firstStagePatch.Length, parameter1: 9000, parameter2: (ushort)firstStagePatch.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(firstStagePatch), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IMPOSS", dataLength: (ushort)secondBasicStage.Length, parameter1: 0, parameter2: (ushort)secondBasicStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(secondBasicStage), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)thirdProtectedStage.Length, parameter1: 0, parameter2: (ushort)thirdProtectedStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(thirdProtectedStage), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                var machine = new Spectrum128Machine(romFolder);
                Tap.TapeExecutionResult result = Tap.TzxLoader.LoadWithPolicy(machine, tapePath);

                Assert.Equal("BootstrapHybrid", result.Strategy.ToString());
                Assert.Equal(3, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                object mountedTape = GetPrivateField(machine, "mountedTape");
                int nextBlockIndex = (int)GetPrivateField(mountedTape, "nextBlockIndex");
                int playbackBlockIndex = (int)GetPrivateField(mountedTape, "earPlaybackBlockIndex");
                Assert.Equal(3, nextBlockIndex);
                Assert.Equal(3, playbackBlockIndex);
                Assert.Equal((byte)0x00, machine.PeekMemory(23624));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadWithPolicy_SafeStandardBasicPrefixBeforeProtectedRemainder_Uses_LeadingStandardChainFakeLoad()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "im-bugfix-shape.tzx");

            try
            {
                byte[] firstStagePatch = BuildBasicProgram(
                    BuildBasicLine(9000, Token(239)));
                byte[] secondBasicStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(231), Ascii("1"), NumberMarker(1),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(253), Ascii("65535"), NumberMarker(65535),
                        Ascii(":"), Token(239)));
                byte[] thirdProtectedStage = BuildBasicProgram(
                    BuildBasicLine(0, Ascii("Protected by SPEEDLOCK")),
                    BuildBasicLine(0,
                        Token(217), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(244), Ascii("23662"), NumberMarker(23662), Ascii(","), Ascii("77"), NumberMarker(77),
                        Ascii(":"), Token(244), Ascii("23663"), NumberMarker(23663), Ascii(","), Ascii("88"), NumberMarker(88),
                        Ascii(":"), Token(244), Ascii("23664"), NumberMarker(23664), Ascii(","), Ascii("99"), NumberMarker(99)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildArchiveInfoBlock(),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "PATCH", dataLength: (ushort)firstStagePatch.Length, parameter1: 9000, parameter2: (ushort)firstStagePatch.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(firstStagePatch), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IMPOSS", dataLength: (ushort)secondBasicStage.Length, parameter1: 0, parameter2: (ushort)secondBasicStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(secondBasicStage), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)thirdProtectedStage.Length, parameter1: 0, parameter2: (ushort)thirdProtectedStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(thirdProtectedStage), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                var machine = new Spectrum128Machine(romFolder);
                Tap.TapeExecutionResult result = Tap.TzxLoader.LoadWithPolicy(machine, tapePath);

                Assert.Equal("LeadingStandardChainFakeLoad", result.Strategy.ToString());
                Assert.Equal(7, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                object mountedTape = GetPrivateField(machine, "mountedTape");
                int nextBlockIndex = (int)GetPrivateField(mountedTape, "nextBlockIndex");
                int playbackBlockIndex = (int)GetPrivateField(mountedTape, "earPlaybackBlockIndex");
                Assert.Equal(7, nextBlockIndex);
                Assert.Equal(7, playbackBlockIndex);
                Assert.Equal((byte)77, machine.PeekMemory(23662));
                Assert.Equal((byte)88, machine.PeekMemory(23663));
                Assert.Equal((byte)99, machine.PeekMemory(23664));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadWithPolicy_ThreeStageStandardBasicPrefixWithOpaqueFinalStage_DoesNotUse_LeadingStandardChainFakeLoad()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "three-stage-opaque-prefix.tzx");

            try
            {
                byte[] firstStagePatch = BuildBasicProgram(
                    BuildBasicLine(9000, Token(239)));
                byte[] secondBasicStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(231), Ascii("1"), NumberMarker(1),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(253), Ascii("65535"), NumberMarker(65535),
                        Ascii(":"), Token(239)));
                byte[] opaqueFinalStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("23662"), NumberMarker(23662), Ascii(","), Token(190), Ascii("23641"), NumberMarker(23641),
                        Ascii(":"), Token(239)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildArchiveInfoBlock(),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "PATCH", dataLength: (ushort)firstStagePatch.Length, parameter1: 9000, parameter2: (ushort)firstStagePatch.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(firstStagePatch), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IMPOSS", dataLength: (ushort)secondBasicStage.Length, parameter1: 0, parameter2: (ushort)secondBasicStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(secondBasicStage), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)opaqueFinalStage.Length, parameter1: 0, parameter2: (ushort)opaqueFinalStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(opaqueFinalStage), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                var machine = new Spectrum128Machine(romFolder);
                MethodInfo createExecutionPlan = typeof(Tap.TapLoader).GetMethod(
                    "CreateExecutionPlan",
                    BindingFlags.Static | BindingFlags.NonPublic)!;
                IReadOnlyList<Tap.TapeBlock> blocks = Tap.TzxLoader.ParseBlocks(File.ReadAllBytes(tapePath));
                object plan = createExecutionPlan.Invoke(null, new object[] { machine, blocks })!;
                string strategy = plan.GetType().GetProperty("Strategy")!.GetValue(plan)!.ToString()!;

                Assert.NotEqual("LeadingStandardChainFakeLoad", strategy);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void LoadWithPolicy_ThreeStageMixedPrefix_With_ProtectedImmediateFinalStage_Uses_BootstrapHybrid()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "three-stage-protected-rom-prefix.tzx");

            try
            {
                byte[] firstStagePatch = BuildBasicProgram(
                    BuildBasicLine(9000, Token(239)));
                byte[] secondBasicStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(231), Ascii("1"), NumberMarker(1),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(253), Ascii("65535"), NumberMarker(65535),
                        Ascii(":"), Token(239)));
                byte[] protectedImmediateStage = BuildBasicProgram(
                    BuildBasicLine(0, Ascii("Protected by SPEEDLOCK")),
                    BuildBasicLine(0,
                        Token(231), Ascii("1"), NumberMarker(1),
                        Ascii(":"), Token(218), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(217), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(251),
                        Ascii(":"), Token(244), Ascii("23662"), NumberMarker(23662), Ascii(","), Token(190), Ascii("23618"), NumberMarker(23618),
                        Ascii(":"), Token(244), Ascii("23663"), NumberMarker(23663), Ascii(","), Token(190), Ascii("23619"), NumberMarker(23619),
                        Ascii(":"), Token(244), Ascii("23664"), NumberMarker(23664), Ascii(","), Token(190), Ascii("23621"), NumberMarker(23621)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildArchiveInfoBlock(),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "PATCH", dataLength: (ushort)firstStagePatch.Length, parameter1: 9000, parameter2: (ushort)firstStagePatch.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(firstStagePatch), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IMPOSS", dataLength: (ushort)secondBasicStage.Length, parameter1: 0, parameter2: (ushort)secondBasicStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(secondBasicStage), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)protectedImmediateStage.Length, parameter1: 0, parameter2: (ushort)protectedImmediateStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(protectedImmediateStage), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                var machine = new Spectrum128Machine(romFolder);
                Tap.TapeExecutionResult result = Tap.TzxLoader.LoadWithPolicy(machine, tapePath);

                Assert.Equal("BootstrapHybrid", result.Strategy.ToString());
                Assert.Equal(3, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void ProtectedLiveByteStreamRemainder_DoesNotElectricallyAccelerate()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "protected-live-remainder.tzx");

            try
            {
                byte[] bootstrap = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("0"), NumberMarker(0),
                        Ascii(":"), Token(239)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)bootstrap.Length, parameter1: 10, parameter2: (ushort)bootstrap.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(bootstrap), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xE8 }, usedBitsInLastByte: 6, pauseMs: 0),
                        BuildPureDataBlock(new byte[] { 0x40, 0xAA, 0x55 }, usedBitsInLastByte: 8, pauseMs: 1)));

                var machine = new Spectrum128Machine(romFolder);
                var blocks = Tap.TzxLoader.ParseBlocks(File.ReadAllBytes(tapePath));
                object plan = typeof(Tap.TapLoader)
                    .GetMethod("CreateExecutionPlan", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { machine, blocks })!;
                Assert.Equal("BootstrapHybrid", plan.GetType().GetProperty("Strategy")!.GetValue(plan)!.ToString());

                int divisor = (int)typeof(Tap.TapLoader)
                    .GetMethod("GetProtectedLiveTapeTimingDivisor", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, new object[] { plan.GetType().GetProperty("Strategy")!.GetValue(plan)!, blocks })!;
                Assert.Equal(1, divisor);
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
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void BootstrapBasicProgramAndMountRemaining_Remounts_Protected_Remainder_After_Chained_Standard_Basic_Load()
        {
            string romFolder = CreateTempRoms();
            string tapePath = Path.Combine(romFolder, "chained-protected.tzx");

            try
            {
                byte[] firstStage = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(253), Ascii("25999"), NumberMarker(25999),
                        Ascii(":"), Token(239)));
                byte[] secondStage = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("23624"), NumberMarker(23624), Ascii(","), Ascii("5"), NumberMarker(5)));

                File.WriteAllBytes(
                    tapePath,
                    BuildTzx(
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)firstStage.Length, parameter1: 10, parameter2: (ushort)firstStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(firstStage), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumHeaderBlock(type: 0, fileName: "IM1", dataLength: (ushort)secondStage.Length, parameter1: 0, parameter2: (ushort)secondStage.Length), pauseMs: 1000),
                        BuildStandardSpeedDataBlock(BuildSpectrumDataBlock(secondStage), pauseMs: 1000),
                        BuildPureToneBlock(2168, 32),
                        BuildPulseSequenceBlock(855, 1710),
                        BuildPureDataBlock(new byte[] { 0xAA, 0x55, 0xF0 }, usedBitsInLastByte: 8, pauseMs: 250)));

                var machine = new Spectrum128Machine(romFolder);
                Tap.TapBootstrapResult result = Tap.TzxLoader.BootstrapBasicProgramAndMountRemaining(machine, tapePath);

                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                Assert.Equal((byte)5, machine.PeekMemory(23624));

                object mountedTape = GetPrivateField(machine, "mountedTape");
                int nextBlockIndex = (int)GetPrivateField(mountedTape, "nextBlockIndex");
                int playbackBlockIndex = (int)GetPrivateField(mountedTape, "earPlaybackBlockIndex");

                Assert.Equal(4, nextBlockIndex);
                Assert.Equal(4, playbackBlockIndex);
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

        [Fact(Skip = "Local debug helper")]
        public void Debug_ImpossibleMission_ChainedBasicShape()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                string tzxPath = @"C:\Users\steve\Desktop\Snapshots\Impossible Mission.tzx";
                Tap.TapBootstrapResult result = Tap.TzxLoader.BootstrapBasicProgramAndMountRemaining(machine, tzxPath);
                Console.WriteLine($"Bootstrap consumed={result.ConsumedBlockCount} auto={result.AutoStartFileName} PC={machine.Cpu.Regs.PC:X4}");
                DumpSysVars("Bootstrap result immediate", machine);
                for (int frame = 0; frame < 200; frame++)
                    machine.ExecuteFrame();
                DumpSysVars("Bootstrap result after 200 frames", machine);
                Console.WriteLine($"Tape after 200 frames: {machine.GetMountedTapeDebugState()}");
                for (int frame = 200; frame < 1200; frame++)
                    machine.ExecuteFrame();
                DumpSysVars("Bootstrap result after 1200 frames", machine);
                Console.WriteLine($"Tape after 1200 frames: {machine.GetMountedTapeDebugState()}");

                Type tapLoaderType = typeof(Tap.TapLoader);
                MethodInfo parseHeaderInfoMethod = tapLoaderType.GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo initMachineMethod = tapLoaderType.GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgramMethod = tapLoaderType
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
                MethodInfo romBootstrapMethod = tapLoaderType.GetMethod("LoadLeadingBasicProgramAndMountRemainingForRomAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;
                Type executorType = tapLoaderType.GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo parseLinesMethod = executorType.GetMethod("ParseLines", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo canExecuteMethod = executorType.GetMethod("CanExecuteProgram", BindingFlags.NonPublic | BindingFlags.Static)!;
                var blocks = Tap.TzxLoader.ParseBlocks(File.ReadAllBytes(tzxPath));
                object impossHeader = parseHeaderInfoMethod.Invoke(null, new object[] { blocks[1] })!;
                Type impossHeaderType = impossHeader.GetType();
                ushort impossProgramLength = (ushort)impossHeaderType.GetProperty("ProgramLength")!.GetValue(impossHeader)!;
                ushort impossAutoStartLine = (ushort)impossHeaderType.GetProperty("AutoStartLine")!.GetValue(impossHeader)!;
                Console.WriteLine($"Raw IMPOSS header len={impossProgramLength} auto={impossAutoStartLine}");

                var impossMachine = new Spectrum128Machine(romFolder);
                initMachineMethod.Invoke(null, new object[] { impossMachine, false });
                loadBasicProgramMethod.Invoke(null, new object[] { impossMachine, impossHeader, blocks[2].Payload! });
                object impossLines = parseLinesMethod.Invoke(null, new object[] { impossMachine, (ushort)23755, impossProgramLength })!;
                bool impossCanExecute = (bool)canExecuteMethod.Invoke(null, new object[] { impossLines, impossAutoStartLine })!;
                Console.WriteLine($"Raw IMPOSS canExecute={impossCanExecute}");
                int impossLineCounter = 0;
                foreach (object line in (System.Collections.IEnumerable)impossLines)
                {
                    Type lineType = line.GetType();
                    ushort number = (ushort)lineType.GetProperty("Number")!.GetValue(line)!;
                    var statements = (System.Collections.IEnumerable)lineType.GetProperty("Statements")!.GetValue(line)!;
                    var renderedStatements = new List<string>();
                    foreach (object stmtObj in statements)
                    {
                        var stmtTokens = new List<string>();
                        foreach (object? token in (System.Collections.IEnumerable)stmtObj)
                            stmtTokens.Add(token?.ToString() ?? string.Empty);
                        renderedStatements.Add(string.Join(" ", stmtTokens));
                    }
                    Console.WriteLine($"IMP {impossLineCounter++}: {number}: {string.Join(" : ", renderedStatements)}");
                }

                object im1Header = parseHeaderInfoMethod.Invoke(null, new object[] { blocks[3] })!;
                Type headerType = im1Header.GetType();
                ushort programLength = (ushort)headerType.GetProperty("ProgramLength")!.GetValue(im1Header)!;
                ushort autoStartLine = (ushort)headerType.GetProperty("AutoStartLine")!.GetValue(im1Header)!;
                Console.WriteLine($"Raw IM1 header len={programLength} auto={autoStartLine}");

                var rawMachine = new Spectrum128Machine(romFolder);
                initMachineMethod.Invoke(null, new object[] { rawMachine, false });
                loadBasicProgramMethod.Invoke(null, new object[] { rawMachine, im1Header, blocks[4].Payload! });
                object lines = parseLinesMethod.Invoke(null, new object[] { rawMachine, (ushort)23755, programLength })!;
                bool canExecute = (bool)canExecuteMethod.Invoke(null, new object[] { lines, autoStartLine })!;

                Console.WriteLine($"Raw canExecute={canExecute}");
                DumpSysVars("Raw before execute", rawMachine);
                var bootMachine = new Spectrum128Machine(romFolder);
                bootMachine.Reset();
                bootMachine.ConfigureFor48kTapeLoad(borderColor: 0);
                bootMachine.Cpu.Regs.PC = 0;
                for (int i = 0; i < 20; i++)
                    bootMachine.ExecuteFrame();
                DumpSysVars("Booted prompt state", bootMachine);
                foreach (int index in new[] { 70, 71, 138, 139, 206, 207, 274, 275, 276, 277, 278 })
                {
                    var block = blocks[index];
                    string firstBytes = block.StreamData == null
                        ? "(none)"
                        : string.Join(" ", block.StreamData.Take(Math.Min(16, block.StreamData.Length)).Select(b => b.ToString("X2")));
                    Console.WriteLine($"Block {index}: Rom={block.IsLoadableRomBlock} Count={block.StreamByteCount} UsedBits={block.UsedBitsInLastByte} Pause={block.PauseAfterBlockMs} First={firstBytes}");
                }
                int lineCounter = 0;
                var supportedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "?", "REM", "BORDER", "PAPER", "INK", "CLS", "LOAD", "CLEAR", "POKE", "DATA", "READ", "FOR", "NEXT", "RANDOMIZE"
                };
                foreach (object line in (System.Collections.IEnumerable)lines)
                {
                    Type lineType = line.GetType();
                    ushort number = (ushort)lineType.GetProperty("Number")!.GetValue(line)!;
                    var statements = (System.Collections.IEnumerable)lineType.GetProperty("Statements")!.GetValue(line)!;
                    var renderedStatements = new List<string>();
                    foreach (object stmtObj in statements)
                    {
                        var stmtTokens = new List<string>();
                        foreach (object? token in (System.Collections.IEnumerable)stmtObj)
                            stmtTokens.Add(token?.ToString() ?? string.Empty);
                        if (stmtTokens.Count > 0 && !supportedKeywords.Contains(stmtTokens[0]))
                            Console.WriteLine($"UNSUPPORTED line={number} keyword={stmtTokens[0]} tokens={string.Join(" ", stmtTokens)}");
                        renderedStatements.Add(string.Join(" ", stmtTokens));
                    }
                    Console.WriteLine($"{lineCounter++}: {number}: {string.Join(" : ", renderedStatements)}");
                }

                MethodInfo executeBootstrapMethod = tapLoaderType.GetMethod(
                    "ExecuteBootstrapBasicAutoStart",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                executeBootstrapMethod.Invoke(null, new object[] { rawMachine, (ushort)23755, programLength, autoStartLine, false });
                DumpSysVars("Raw after execute", rawMachine);

                var romDrivenMachine = new Spectrum128Machine(romFolder);
                romBootstrapMethod.Invoke(null, new object[] { romDrivenMachine, "Impossible Mission.tzx", blocks, false, 1, 1 });
                for (int frame = 0; frame < 400; frame++)
                {
                    romDrivenMachine.ExecuteFrame();
                    if (frame == 0 || frame == 50 || frame == 100 || frame == 200 || frame == 399)
                    {
                        Console.WriteLine($"ROM-driven frame {frame + 1}: PC={romDrivenMachine.Cpu.Regs.PC:X4} Tape={romDrivenMachine.GetMountedTapeDebugState()}");
                        DumpSysVars($"ROM-driven frame {frame + 1}", romDrivenMachine);
                    }
                }
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Debug_Batman_ChainedBasicShape()
        {
            string romFolder = CreateTempRoms();
            try
            {
                string tzxPath = @"C:\Users\steve\Desktop\Snapshots\Batman - Release 1.tzx";
                Type tapLoaderType = typeof(Tap.TapLoader);
                MethodInfo parseHeaderInfoMethod = tapLoaderType.GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo initMachineMethod = tapLoaderType.GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgramMethod = tapLoaderType
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
                Type executorType = tapLoaderType.GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo parseLinesMethod = executorType.GetMethod("ParseLines", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo createResolverMethod = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);

                var blocks = Tap.TzxLoader.ParseBlocks(File.ReadAllBytes(tzxPath));
                for (int i = 0; i < blocks.Count; i++)
                {
                    Console.WriteLine(
                        $"BAT block {i}: kind={blocks[i].Kind} flag=0x{blocks[i].Flag:X2} loadable={blocks[i].IsLoadableRomBlock} pause={blocks[i].PauseAfterBlockMs} payload={(blocks[i].Payload?.Length ?? -1)}");
                }
                object batmanHeader = parseHeaderInfoMethod.Invoke(null, new object[] { blocks[0] })!;
                Type batmanHeaderType = batmanHeader.GetType();
                ushort programLength = (ushort)batmanHeaderType.GetProperty("ProgramLength")!.GetValue(batmanHeader)!;
                ushort autoStartLine = (ushort)batmanHeaderType.GetProperty("AutoStartLine")!.GetValue(batmanHeader)!;
                Console.WriteLine($"BATMAN header len={programLength} auto={autoStartLine}");

                var machine = new Spectrum128Machine(romFolder);
                initMachineMethod.Invoke(null, new object[] { machine, false });
                loadBasicProgramMethod.Invoke(null, new object[] { machine, batmanHeader, blocks[1].Payload! });
                object lines = parseLinesMethod.Invoke(null, new object[] { machine, (ushort)23755, programLength })!;
                int lineCounter = 0;
                foreach (object line in (System.Collections.IEnumerable)lines)
                {
                    Type lineType = line.GetType();
                    ushort number = (ushort)lineType.GetProperty("Number")!.GetValue(line)!;
                    var statements = (System.Collections.IEnumerable)lineType.GetProperty("Statements")!.GetValue(line)!;
                    var renderedStatements = new List<string>();
                    foreach (object stmtObj in statements)
                    {
                        var stmtTokens = new List<string>();
                        foreach (object? token in (System.Collections.IEnumerable)stmtObj)
                            stmtTokens.Add(token?.ToString() ?? string.Empty);
                        renderedStatements.Add(string.Join(" ", stmtTokens));
                    }
                    Console.WriteLine($"BAT {lineCounter++}: {number}: {string.Join(" : ", renderedStatements)}");
                }

                object? resolver = createResolverMethod.Invoke(null, new object[] { machine, (ushort)23755, programLength, autoStartLine });
                Console.WriteLine($"BATMAN resolver? {(resolver != null)}");

                Tap.TapeExecutionResult result = Tap.TzxLoader.LoadWithPolicy(machine, tzxPath);
                Console.WriteLine($"BATMAN strategy={result.Strategy} consumed={result.ConsumedBlockCount}/{result.TotalBlockCount}");
                FieldInfo pendingResolverField = typeof(Spectrum128Machine).GetField(
                    "pendingMountedLoadUsrContinuationResolver",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                object? initialPendingResolverObject = pendingResolverField.GetValue(machine);
                Console.WriteLine(
                    $"BAT POST-LOAD pending={(initialPendingResolverObject != null ? 1 : 0)} " +
                    $"pc=0x{machine.Cpu.Regs.PC:X4} sp=0x{machine.Cpu.Regs.SP:X4} tape={machine.GetMountedTapeDebugState()}");
                object? wrappedResolverSource = null;

                bool? slicePending = null;
                ushort lastSlicePc = 0xFFFF;
                for (int slice = 0; slice < 4000 && machine.FrameCount == 0; slice++)
                {
                    object? currentResolverObject = pendingResolverField.GetValue(machine);
                    if (currentResolverObject != null &&
                        !ReferenceEquals(currentResolverObject, wrappedResolverSource))
                    {
                        wrappedResolverSource = currentResolverObject;
                        Func<Spectrum128Machine, ushort?> currentPendingResolver = (Func<Spectrum128Machine, ushort?>)currentResolverObject;
                        machine.SetPendingMountedLoadUsrContinuationResolver(currentMachine =>
                        {
                            ushort? value = currentPendingResolver(currentMachine);
                            Console.WriteLine(
                                $"BAT RESOLVER fired frame={currentMachine.FrameCount} pc=0x{currentMachine.Cpu.Regs.PC:X4} " +
                                $"sp=0x{currentMachine.Cpu.Regs.SP:X4} result=0x{(value.HasValue ? value.Value.ToString("X4") : "null")} " +
                                $"tape={currentMachine.GetMountedTapeDebugState()}");
                            return value;
                        });
                    }

                    bool slicePendingNow = pendingResolverField.GetValue(machine) != null;
                    if (slicePending != slicePendingNow || machine.Cpu.Regs.PC != lastSlicePc)
                    {
                        Console.WriteLine(
                            $"BAT SLICE {slice} frame={machine.FrameCount} pending={(slicePendingNow ? 1 : 0)} " +
                            $"pc=0x{machine.Cpu.Regs.PC:X4} sp=0x{machine.Cpu.Regs.SP:X4} " +
                            $"newppc=0x{(machine.PeekMemory(23618) | (machine.PeekMemory(23619) << 8)):X4} " +
                            $"nsppc={machine.PeekMemory(23620)} ppc=0x{(machine.PeekMemory(23621) | (machine.PeekMemory(23622) << 8)):X4} " +
                            $"subppc={machine.PeekMemory(23623)} tape={machine.GetMountedTapeDebugState()}");
                        slicePending = slicePendingNow;
                        lastSlicePc = machine.Cpu.Regs.PC;
                    }

                    machine.ExecuteTimeSlice(Math.Max(1, machine.CurrentCpuClockHz / 1000), out _);
                }

                bool? lastPending = null;
                for (int frame = 0; frame < 15000; frame++)
                {
                    machine.ExecuteFrame();
                    object? currentPending = pendingResolverField.GetValue(machine);
                    bool pending = currentPending != null;
                    if (pending != lastPending)
                    {
                        Console.WriteLine(
                            $"BAT PENDING frame={machine.FrameCount} pending={pending} pc=0x{machine.Cpu.Regs.PC:X4} " +
                            $"sp=0x{machine.Cpu.Regs.SP:X4} tape={machine.GetMountedTapeDebugState()}");
                        lastPending = pending;
                    }

                    if (machine.FrameCount == 14110 && currentPending is Func<Spectrum128Machine, ushort?> pendingResolver)
                    {
                        Console.WriteLine(
                            $"BAT PRE-RESUME frame={machine.FrameCount} pc=0x{machine.Cpu.Regs.PC:X4} " +
                            $"sp=0x{machine.Cpu.Regs.SP:X4} tape={machine.GetMountedTapeDebugState()}");
                        Console.WriteLine(
                            $"BAT PRE-RESUME sysvars CHANS=0x{(machine.PeekMemory(23631) | (machine.PeekMemory(23632) << 8)):X4} " +
                            $"CURCHL=0x{(machine.PeekMemory(23633) | (machine.PeekMemory(23634) << 8)):X4} " +
                            $"PROG=0x{(machine.PeekMemory(23635) | (machine.PeekMemory(23636) << 8)):X4} " +
                            $"VARS=0x{(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8)):X4} " +
                            $"ELINE=0x{(machine.PeekMemory(23641) | (machine.PeekMemory(23642) << 8)):X4}");
                        ushort? preview = pendingResolver(machine);
                        Console.WriteLine($"BAT PREVIEW resolver=0x{(preview.HasValue ? preview.Value.ToString("X4") : "null")}");
                        break;
                    }
                }

                object? pendingResolverObject = pendingResolverField.GetValue(machine);
                Console.WriteLine($"BATMAN pending after run? {pendingResolverObject != null}");
                Console.WriteLine($"BATMAN PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4}");
                Console.WriteLine($"BATMAN tape={machine.GetMountedTapeDebugState()}");
                Console.WriteLine(
                    $"BATMAN sysvars CHANS=0x{(machine.PeekMemory(23631) | (machine.PeekMemory(23632) << 8)):X4} " +
                    $"CURCHL=0x{(machine.PeekMemory(23633) | (machine.PeekMemory(23634) << 8)):X4} " +
                    $"WORKSP=0x{(machine.PeekMemory(23649) | (machine.PeekMemory(23650) << 8)):X4} " +
                    $"NEWPPC=0x{(machine.PeekMemory(23618) | (machine.PeekMemory(23619) << 8)):X4} " +
                    $"NSPPC={machine.PeekMemory(23621)}");

                object liveLines = parseLinesMethod.Invoke(null, new object[] { machine, (ushort)23755, programLength })!;
                int liveLineCounter = 0;
                foreach (object line in (System.Collections.IEnumerable)liveLines)
                {
                    Type lineType = line.GetType();
                    ushort number = (ushort)lineType.GetProperty("Number")!.GetValue(line)!;
                    var statements = (System.Collections.IEnumerable)lineType.GetProperty("Statements")!.GetValue(line)!;
                    var renderedStatements = new List<string>();
                    foreach (object stmtObj in statements)
                    {
                        var stmtTokens = new List<string>();
                        foreach (object? token in (System.Collections.IEnumerable)stmtObj)
                            stmtTokens.Add(token?.ToString() ?? string.Empty);
                        renderedStatements.Add(string.Join(" ", stmtTokens));
                    }
                    Console.WriteLine($"BAT LIVE {liveLineCounter++}: {number}: {string.Join(" : ", renderedStatements)}");
                }
                Console.WriteLine(
                    $"BATMAN late sysvars CHANS=0x{(machine.PeekMemory(23631) | (machine.PeekMemory(23632) << 8)):X4} " +
                    $"CURCHL=0x{(machine.PeekMemory(23633) | (machine.PeekMemory(23634) << 8)):X4} " +
                    $"WORKSP=0x{(machine.PeekMemory(23649) | (machine.PeekMemory(23650) << 8)):X4} " +
                    $"NEWPPC=0x{(machine.PeekMemory(23618) | (machine.PeekMemory(23619) << 8)):X4} " +
                    $"NSPPC={machine.PeekMemory(23621)} PPC=0x{(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8)):X4} SUBPPC={machine.PeekMemory(23629)}");
                Console.Write("BAT MEM 01A0:");
                for (int i = 0x01A0; i < 0x01B0; i++)
                    Console.Write($" {machine.PeekMemory((ushort)i):X2}");
                Console.WriteLine();
                Console.Write("BAT MEM 5C40:");
                for (int i = 0x5C40; i < 0x5C60; i++)
                    Console.Write($" {machine.PeekMemory((ushort)i):X2}");
                Console.WriteLine();
                ushort progAddress = (ushort)(machine.PeekMemory(23635) | (machine.PeekMemory(23636) << 8));
                ushort varsAddress = (ushort)(machine.PeekMemory(23627) | (machine.PeekMemory(23628) << 8));
                ushort eLineAddress = (ushort)(machine.PeekMemory(23641) | (machine.PeekMemory(23642) << 8));
                Console.WriteLine($"BAT POINTERS PROG=0x{progAddress:X4} VARS=0x{varsAddress:X4} ELINE=0x{eLineAddress:X4}");
                Console.Write("BAT VARS:");
                for (int i = varsAddress; i < Math.Min(eLineAddress, varsAddress + 48); i++)
                    Console.Write($" {machine.PeekMemory((ushort)i):X2}");
                Console.WriteLine();

                MethodInfo resumeMethod = typeof(Spectrum128Machine).GetMethod(
                    "TryResumePendingMountedLoadUsrContinuation",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                machine.Cpu.Regs.PC = 0x2D2B;
                bool forcedResume = (bool)resumeMethod.Invoke(machine, new object[] { machine.Cpu })!;
                Console.WriteLine($"BATMAN forced resume? {forcedResume} pc=0x{machine.Cpu.Regs.PC:X4} bc=0x{machine.Cpu.Regs.BC:X4}");
                for (int frame = 0; frame < 2000; frame++)
                    machine.ExecuteFrame();
                Console.WriteLine($"BATMAN after forced resume PC=0x{machine.Cpu.Regs.PC:X4} tape={machine.GetMountedTapeDebugState()}");
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Local debug helper")]
        public void Debug_Batman_TwoConsecutiveLoads_SameMachine()
        {
            string romFolder = CreateTempRoms();
            try
            {
                string tzxPath = @"C:\Users\steve\Desktop\Snapshots\Batman - Release 1.tzx";
                var machine = new Spectrum128Machine(romFolder);

                Tap.TapeExecutionResult first = Tap.TzxLoader.LoadWithPolicy(machine, tzxPath);
                string firstLoadDump = machine.BuildDebugDump("batman-first-load");
                Console.WriteLine(
                    $"BAT FIRST-LOAD strategy={first.Strategy} consumed={first.ConsumedBlockCount}/{first.TotalBlockCount} " +
                    $"pc=0x{machine.Cpu.Regs.PC:X4} frameTStates={machine.FrameTStates} tape={machine.GetMountedTapeDebugState()}");
                for (int frame = 0; frame < 17000; frame++)
                    machine.ExecuteFrame();

                FieldInfo pendingResolverField = typeof(Spectrum128Machine).GetField(
                    "pendingMountedLoadUsrContinuationResolver",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;

                Console.WriteLine(
                    $"BAT FIRST strategy={first.Strategy} consumed={first.ConsumedBlockCount}/{first.TotalBlockCount} " +
                    $"pc=0x{machine.Cpu.Regs.PC:X4} frameTStates={machine.FrameTStates} pending={(pendingResolverField.GetValue(machine) != null ? 1 : 0)} " +
                    $"tape={machine.GetMountedTapeDebugState()}");

                Tap.TapeExecutionResult second = Tap.TzxLoader.LoadWithPolicy(machine, tzxPath);
                string secondLoadDump = machine.BuildDebugDump("batman-second-load");
                Console.WriteLine(
                    $"BAT SECOND-LOAD strategy={second.Strategy} consumed={second.ConsumedBlockCount}/{second.TotalBlockCount} " +
                    $"pc=0x{machine.Cpu.Regs.PC:X4} frameTStates={machine.FrameTStates} pending={(pendingResolverField.GetValue(machine) != null ? 1 : 0)} " +
                    $"tape={machine.GetMountedTapeDebugState()}");
                bool dumpsEqual = string.Equals(firstLoadDump, secondLoadDump, StringComparison.Ordinal);
                Console.WriteLine($"BAT LOAD DUMPS EQUAL={(dumpsEqual ? 1 : 0)}");
                if (!dumpsEqual)
                {
                    string[] firstLines = firstLoadDump.Split(Environment.NewLine);
                    string[] secondLines = secondLoadDump.Split(Environment.NewLine);
                    int compareLength = Math.Min(firstLines.Length, secondLines.Length);
                    for (int i = 0; i < compareLength; i++)
                    {
                        if (string.Equals(firstLines[i], secondLines[i], StringComparison.Ordinal))
                            continue;

                        Console.WriteLine($"BAT LOAD DIFF line={i}");
                        Console.WriteLine($"  FIRST : {firstLines[i]}");
                        Console.WriteLine($"  SECOND: {secondLines[i]}");
                        break;
                    }
                }

                for (int frame = 0; frame < 17000; frame++)
                    machine.ExecuteFrame();

                Console.WriteLine(
                    $"BAT SECOND-RUN pc=0x{machine.Cpu.Regs.PC:X4} frameTStates={machine.FrameTStates} pending={(pendingResolverField.GetValue(machine) != null ? 1 : 0)} " +
                    $"tape={machine.GetMountedTapeDebugState()}");
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Local debug helper")]
        public void Debug_Batman_ExecuteFrame_Vs_ExecuteTimeSlice()
        {
            string romFolder = CreateTempRoms();
            try
            {
                string tzxPath = @"C:\Users\steve\Desktop\Snapshots\Batman - Release 1.tzx";
                if (!File.Exists(tzxPath))
                    return;

                static string SnapshotState(Spectrum128Machine machine) =>
                    $"PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} Frame={machine.FrameCount} " +
                    $"Pending={(typeof(Spectrum128Machine).GetField("pendingMountedLoadUsrContinuationResolver", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(machine) != null ? 1 : 0)} " +
                    $"Tape={machine.GetMountedTapeDebugState()}";

                static void ExecuteFrames(Spectrum128Machine machine, int targetFrame)
                {
                    while (machine.FrameCount < targetFrame)
                        machine.ExecuteFrame();
                }

                static void ExecuteSlices(Spectrum128Machine machine, int sliceBudget, int targetFrame)
                {
                    while (machine.FrameCount < targetFrame)
                        machine.ExecuteTimeSlice(sliceBudget);
                }

                var frameMachine = new Spectrum128Machine(romFolder);
                Tap.TzxLoader.LoadWithPolicy(frameMachine, tzxPath);
                ExecuteFrames(frameMachine, 2500);
                Console.WriteLine("BAT FRAME2500 " + SnapshotState(frameMachine));
                ExecuteFrames(frameMachine, 10000);
                Console.WriteLine("BAT FRAME10000 " + SnapshotState(frameMachine));

                var sliceMachine1 = new Spectrum128Machine(romFolder);
                Tap.TzxLoader.LoadWithPolicy(sliceMachine1, tzxPath);
                int sliceBudget1 = sliceMachine1.CurrentCpuClockHz / 1000;
                ExecuteSlices(sliceMachine1, sliceBudget1, 2500);
                Console.WriteLine("BAT SLICE1_2500 " + SnapshotState(sliceMachine1));
                ExecuteSlices(sliceMachine1, sliceBudget1, 10000);
                Console.WriteLine("BAT SLICE1_10000 " + SnapshotState(sliceMachine1));

                var sliceMachine8 = new Spectrum128Machine(romFolder);
                Tap.TzxLoader.LoadWithPolicy(sliceMachine8, tzxPath);
                int sliceBudget8 = (sliceMachine8.CurrentCpuClockHz / 1000) * 8;
                ExecuteSlices(sliceMachine8, sliceBudget8, 2500);
                Console.WriteLine("BAT SLICE8_2500 " + SnapshotState(sliceMachine8));
                ExecuteSlices(sliceMachine8, sliceBudget8, 10000);
                Console.WriteLine("BAT SLICE8_10000 " + SnapshotState(sliceMachine8));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Local debug helper")]
        public void Debug_ImpossibleMissionBugfix_ChainedBasicShape()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                string tzxPath = @"C:\Users\steve\Desktop\Snapshots\Impossible Mission - Bugfix.tzx";
                var parseBlocks = typeof(Tap.TzxLoader).GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(byte[]), typeof(bool) }, null)!;
                var blocks = (IReadOnlyList<Tap.TapeBlock>)parseBlocks.Invoke(null, new object[] { File.ReadAllBytes(tzxPath), true })!;

                Console.WriteLine($"Bugfix blocks={blocks.Count}");
                for (int i = 0; i < blocks.Count; i++)
                {
                    Tap.TapeBlock block = blocks[i];
                    if (block.Kind == Tap.TapeBlockKind.Metadata)
                    {
                        Console.WriteLine($"[{i}] Metadata");
                        continue;
                    }

                    if (block.CanUseRomLoadTrap && block.Flag == 0x00 && block.Payload?.Length == 17)
                    {
                        object header = typeof(Tap.TapLoader)
                            .GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!
                            .Invoke(null, new object[] { block })!;
                        byte type = (byte)header.GetType().GetProperty("Type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                        string fileName = (string)header.GetType().GetProperty("FileName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                        ushort dataLength = (ushort)header.GetType().GetProperty("DataLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                        ushort autoStart = (ushort)header.GetType().GetProperty("AutoStartLine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                        Console.WriteLine($"[{i}] HEADER type={type} name='{fileName}' len={dataLength} auto={autoStart}");
                        continue;
                    }

                    if (block.Kind == Tap.TapeBlockKind.Data)
                    {
                        Console.WriteLine($"[{i}] DATA flag=0x{block.Flag:X2} rom={block.CanUseRomLoadTrap} len={block.Payload?.Length ?? block.StreamData?.Length ?? 0} pause={block.PauseAfterBlockMs}");
                        continue;
                    }

                    Console.WriteLine($"[{i}] {block.Kind}");
                }

                var canBootstrapLoaded = typeof(Tap.TapLoader).GetMethod("CanBootstrapLoadedBasicProgram", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo tryExecuteLoadedMountedBasicProgram = typeof(Tap.TapLoader).GetMethod(
                    "TryExecuteLoadedMountedBasicProgram",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                Type executorType = typeof(Tap.TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                MethodInfo parseLinesMethod = executorType.GetMethod("ParseLines", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo canHandleLoadedProgramMethod = executorType.GetMethod("CanHandleLoadedProgram", BindingFlags.Public | BindingFlags.Static)!;
                MethodInfo canExecuteImmediateSideEffectProgramMethod = executorType.GetMethod("CanExecuteImmediateSideEffectProgram", BindingFlags.Public | BindingFlags.Static)!;
                MethodInfo requiresMountedLoadSemanticsMethod = executorType.GetMethod("RequiresMountedLoadSemantics", BindingFlags.Public | BindingFlags.Static)!;
                MethodInfo requiresRomDrivenMountedLoadedProgramMethod = executorType.GetMethod("RequiresRomDrivenMountedLoadedProgram", BindingFlags.Public | BindingFlags.Static)!;
                MethodInfo createResolverMethod = executorType
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "CreateMountedLoadUsrContinuationResolver" && method.GetParameters().Length == 4);
                MethodInfo initializeMachineForFakeTapeLoadMethod = typeof(Tap.TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadBasicProgramMethod = typeof(Tap.TapLoader)
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
                for (int i = 0; i + 1 < blocks.Count; i++)
                {
                    if (!blocks[i].CanUseRomLoadTrap || blocks[i].Flag != 0x00 || blocks[i].Payload?.Length != 17 || blocks[i + 1].Flag != 0xFF)
                        continue;

                    object header = typeof(Tap.TapLoader)
                        .GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!
                        .Invoke(null, new object[] { blocks[i] })!;
                    byte type = (byte)header.GetType().GetProperty("Type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                    if (type != 0)
                        continue;

                    bool use128kMode = (bool)typeof(Tap.TapLoader)
                        .GetMethod("Requires128kTapeLoadModeForStandardTape", BindingFlags.NonPublic | BindingFlags.Static)!
                        .Invoke(null, new object[] { machine, blocks })!;
                    bool canBootstrap = (bool)canBootstrapLoaded.Invoke(null, new object[] { machine, header, blocks[i + 1].Payload!, use128kMode })!;
                    string fileName = (string)header.GetType().GetProperty("FileName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                    ushort autoStart = (ushort)header.GetType().GetProperty("AutoStartLine", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                    ushort programLength = (ushort)header.GetType().GetProperty("ProgramLength", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(header)!;
                    var programMachine = new Spectrum128Machine(romFolder);
                    initializeMachineForFakeTapeLoadMethod.Invoke(null, new object[] { programMachine, use128kMode });
                    loadBasicProgramMethod.Invoke(null, new object[] { programMachine, header, blocks[i + 1].Payload! });
                    object lines = parseLinesMethod.Invoke(null, new object[] { programMachine, (ushort)23755, programLength })!;
                    bool canHandleLoaded = (bool)canHandleLoadedProgramMethod.Invoke(null, new object[] { programMachine, (ushort)23755, programLength, autoStart })!;
                    bool canExecuteImmediate = (bool)canExecuteImmediateSideEffectProgramMethod.Invoke(null, new object[] { programMachine, (ushort)23755, programLength, autoStart })!;
                    bool requiresMountedLoadSemantics = (bool)requiresMountedLoadSemanticsMethod.Invoke(null, new object[] { programMachine, (ushort)23755, programLength, autoStart })!;
                    bool requiresRomDrivenMountedLoadedProgram = (bool)requiresRomDrivenMountedLoadedProgramMethod.Invoke(null, new object[] { programMachine, (ushort)23755, programLength, autoStart })!;
                    object? resolver = createResolverMethod.Invoke(null, new object[] { programMachine, (ushort)23755, programLength, autoStart });
                    Console.WriteLine(
                        $"Program '{fileName}' auto={autoStart} len={programLength} canBootstrap={canBootstrap} " +
                        $"canHandle={canHandleLoaded} immediate={canExecuteImmediate} mountedSemantics={requiresMountedLoadSemantics} " +
                        $"romDriven={requiresRomDrivenMountedLoadedProgram} resolver={(resolver != null ? 1 : 0)}");

                    if (string.Equals(fileName, "IM1", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (ushort executeLength in new[] { programLength, (ushort)blocks[i + 1].Payload!.Length })
                        {
                            var executeMachine = new Spectrum128Machine(romFolder);
                            initializeMachineForFakeTapeLoadMethod.Invoke(null, new object[] { executeMachine, use128kMode });
                            loadBasicProgramMethod.Invoke(null, new object[] { executeMachine, header, blocks[i + 1].Payload! });
                            bool executedMounted = (bool)tryExecuteLoadedMountedBasicProgram.Invoke(
                                null,
                                new object[]
                                {
                                    executeMachine,
                                    programLength,
                                    executeLength,
                                    autoStart
                                })!;
                            Console.WriteLine(
                                $"IM1 direct execute len={executeLength}: executed={executedMounted} pendingUsr={executeMachine.HasPendingMountedLoadUsrContinuation} " +
                                $"PC=0x{executeMachine.Cpu.Regs.PC:X4} SP=0x{executeMachine.Cpu.Regs.SP:X4}");
                            DumpSysVars($"IM1 direct execute len={executeLength}", executeMachine);
                        }
                    }

                    int lineCounter = 0;
                    foreach (object line in (System.Collections.IEnumerable)lines)
                    {
                        Type lineType = line.GetType();
                        ushort number = (ushort)lineType.GetProperty("Number")!.GetValue(line)!;
                        ushort dataAddress = (ushort)lineType.GetProperty("DataAddress", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(line)!;
                        var statements = (System.Collections.IEnumerable)lineType.GetProperty("Statements")!.GetValue(line)!;
                        var renderedStatements = new List<string>();
                        foreach (object stmtObj in statements)
                        {
                            var stmtTokens = new List<string>();
                            foreach (object? token in (System.Collections.IEnumerable)stmtObj)
                                stmtTokens.Add(token?.ToString() ?? string.Empty);

                            renderedStatements.Add(string.Join(" ", stmtTokens));
                        }

                        var rawBytes = new List<string>();
                        for (ushort address = dataAddress; address < dataAddress + 64; address++)
                        {
                            byte value = programMachine.PeekMemory(address);
                            rawBytes.Add(value.ToString("X2"));
                            if (value == 0x0D)
                                break;
                        }

                        Console.WriteLine(
                            $"  {fileName} {lineCounter++}: {number}: {string.Join(" : ", renderedStatements)} raw={string.Join(" ", rawBytes)}");
                    }
                }

                Tap.TapeExecutionResult result = Tap.TzxLoader.LoadWithPolicy(machine, tzxPath);
                Console.WriteLine($"Strategy={result.Strategy} consumed={result.ConsumedBlockCount}");
                Console.WriteLine(machine.GetMountedTapeDebugState());
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Local debug helper")]
        public void Debug_ImpossibleMissionBugfix_FirstStagePatchPrefix()
        {
            string romFolder = CreateTempRoms();

            try
            {
                string tzxPath = @"C:\Users\steve\Desktop\Snapshots\Impossible Mission - Bugfix.tzx";
                if (!File.Exists(tzxPath))
                    return;

                var machine = new Spectrum128Machine(romFolder);
                Tap.TzxLoader.LoadWithPolicy(machine, tzxPath);

                byte[] actualPrefix = new byte[32];
                for (int i = 0; i < actualPrefix.Length; i++)
                    actualPrefix[i] = machine.PeekMemory((ushort)(0x8000 + i));

                byte[] expectedPrefix =
                {
                    0xDD, 0x21, 0xCB, 0x5C, // ld ix,$5ccb
                    0x11, 0x1A, 0x06,       // ld de,1562
                    0x3E, 0xFF,             // ld a,$ff
                    0x37,                   // scf
                    0xCD, 0x56, 0x05,       // call $0556
                    0x30, 0xF1,             // jr nc,main
                    0xF3,                   // di
                    0x21, 0xFD, 0x5E,       // ld hl,$5efd
                    0xE5,                   // push hl
                    0x11, 0x83, 0xFC,       // ld de,$fc83
                    0x01, 0x8B, 0x02,       // ld bc,$028b
                    0x3E, 0xC2              // ld a,$c2
                };

                Console.WriteLine("Actual 8000 prefix: " + BitConverter.ToString(actualPrefix));
                Assert.Equal(expectedPrefix, actualPrefix[..expectedPrefix.Length]);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact(Skip = "Local debug helper")]
        public void Debug_ImpossibleMissionBugfix_ProtectedTailDuration()
        {
            string tzxPath = @"C:\Users\steve\Desktop\Snapshots\Impossible Mission - Bugfix.tzx";
            if (!File.Exists(tzxPath))
                return;

            var blocks = Tap.TzxLoader.ParseBlocks(File.ReadAllBytes(tzxPath));
            MethodInfo estimateMethod = typeof(Tap.TapLoader).GetMethod(
                "EstimateTapeBlockDurationTStates",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            ulong tailTStates = 0;
            for (int i = 8; i < blocks.Count; i++)
                tailTStates += (ulong)estimateMethod.Invoke(null, new object[] { blocks[i] })!;

            Console.WriteLine($"Impossible Mission bugfix protected tail tstates={tailTStates}");
            Console.WriteLine($"Impossible Mission bugfix protected tail seconds={(tailTStates / 3500000.0):F2}");
        }


        private static byte[] BuildJumpBlock(short relativeOffset)
        {
            return new byte[] { 0x23, (byte)(relativeOffset & 0xFF), (byte)((relativeOffset >> 8) & 0xFF) };
        }

        private static void DumpSysVars(string label, Spectrum128Machine machine)
        {
            static ushort ReadWord(Spectrum128Machine m, ushort addr)
                => (ushort)(m.PeekMemory(addr) | (m.PeekMemory((ushort)(addr + 1)) << 8));

            static string DumpBytes(Spectrum128Machine m, ushort start, int length)
            {
                var bytes = new byte[length];
                for (int i = 0; i < length; i++)
                    bytes[i] = m.PeekMemory((ushort)(start + i));
                return string.Join(" ", bytes.Select(b => b.ToString("X2")));
            }

                ushort[] addrs =
                {
                    23611, 23612, 23613, 23618, 23624, 23627, 23633, 23635, 23637, 23639, 23641, 23647, 23649, 23651, 23653, 23662
                };

            Console.WriteLine(label);
            foreach (ushort addr in addrs)
                Console.WriteLine($"  {addr}: {ReadWord(machine, addr):X4}");
            ushort eLine = ReadWord(machine, 23641);
            ushort ptr = ReadWord(machine, 23633);
            Console.WriteLine($"  [E_LINE]={machine.PeekMemory(eLine):X2} {machine.PeekMemory((ushort)(eLine + 1)):X2}");
            Console.WriteLine($"  [PTR23633]={machine.PeekMemory(ptr):X2} {machine.PeekMemory((ushort)(ptr + 1)):X2}");
            Console.WriteLine($"  [8000]={DumpBytes(machine, 0x8000, 32)}");
            Console.WriteLine($"  [FC80]={DumpBytes(machine, 0xFC80, 32)}");
            Console.WriteLine($"  [FD00]={DumpBytes(machine, 0xFD00, 32)}");
            Console.WriteLine($"  PC={machine.Cpu.Regs.PC:X4} SP={machine.Cpu.Regs.SP:X4}");
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

        private static byte[] BuildStopIf48kBlock()
        {
            return new byte[] { 0x2A, 0x00, 0x00, 0x00, 0x00 };
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
