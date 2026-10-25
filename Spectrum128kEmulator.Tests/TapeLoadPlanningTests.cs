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
        public void CreateExecutionPlan_Classifies_Standard_CustomAndMounted_Tapes()
        {
            string tempFolder = CreateTempRoms();
            Type tapLoaderType = typeof(TapLoader);
            MethodInfo parseBlocks = tapLoaderType.GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;
            MethodInfo createPlan = tapLoaderType.GetMethod("CreateExecutionPlan", BindingFlags.NonPublic | BindingFlags.Static)!;
            Type planType = tapLoaderType.Assembly.GetType("Spectrum128kEmulator.Tap.TapeLoadPlan")!;
            var strategyProperty = planType.GetProperty("Strategy")!;
            var reasonCodeProperty = planType.GetProperty("ReasonCode")!;
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
                Assert.Equal("StandardTapeFullyAccelerated", reasonCodeProperty.GetValue(standardPlan)!.ToString());

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
                Assert.Equal("StandardBasicChainRequiresRom", reasonCodeProperty.GetValue(romDrivenPlan)!.ToString());

                byte[] nonContiguousBasicTape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)romDrivenProgram.Length, parameter1: 10, parameter2: (ushort)romDrivenProgram.Length),
                    BuildDataBlock(romDrivenProgram),
                    BuildHeaderBlock(type: 3, fileName: "SCREEN", dataLength: 1, parameter1: 0x4000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0xAA }),
                    BuildHeaderBlock(type: 0, fileName: "GAME", dataLength: (ushort)opaqueBasicStage.Length, parameter1: 0, parameter2: (ushort)opaqueBasicStage.Length),
                    BuildDataBlock(opaqueBasicStage));
                var nonContiguousBasicBlocks = parseBlocks.Invoke(null, new object[] { nonContiguousBasicTape })!;
                var nonContiguousBasicMachine = new Spectrum128Machine(tempFolder);
                object nonContiguousBasicPlan = createPlan.Invoke(null, new[] { nonContiguousBasicMachine, nonContiguousBasicBlocks })!;
                Assert.Equal("RomBootstrapMounted", strategyProperty.GetValue(nonContiguousBasicPlan)!.ToString());
                Assert.Equal("StandardBasicChainRequiresRom", reasonCodeProperty.GetValue(nonContiguousBasicPlan)!.ToString());

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
                Assert.Equal("StandardTapeRequiresMountedSemantics", reasonCodeProperty.GetValue(hybridPlan)!.ToString());

                byte[] protectedHybridTape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOT", dataLength: (ushort)romDrivenProgram.Length, parameter1: 10, parameter2: (ushort)romDrivenProgram.Length),
                    BuildDataBlock(romDrivenProgram),
                    BuildHeaderBlock(type: 0, fileName: "STAGE2", dataLength: (ushort)protectedBasicStage.Length, parameter1: 0, parameter2: (ushort)protectedBasicStage.Length),
                    BuildDataBlock(protectedBasicStage));
                var protectedHybridBlocks = parseBlocks.Invoke(null, new object[] { protectedHybridTape })!;
                var protectedHybridMachine = new Spectrum128Machine(tempFolder);
                object protectedHybridPlan = createPlan.Invoke(null, new[] { protectedHybridMachine, protectedHybridBlocks })!;
                Assert.Equal("BootstrapHybrid", strategyProperty.GetValue(protectedHybridPlan)!.ToString());
                Assert.Equal("StandardTapeRequiresMountedSemantics", reasonCodeProperty.GetValue(protectedHybridPlan)!.ToString());

                byte[] mountedOnlyTape = BuildTap(
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 2, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0xAA, 0xBB }));
                var mountedBlocks = parseBlocks.Invoke(null, new object[] { mountedOnlyTape })!;
                var mountedMachine = new Spectrum128Machine(tempFolder);
                object mountedPlan = createPlan.Invoke(null, new[] { mountedMachine, mountedBlocks })!;
                Assert.Equal("MountedRealtime", strategyProperty.GetValue(mountedPlan)!.ToString());
                Assert.Equal("NoSafeAccelerationPath", reasonCodeProperty.GetValue(mountedPlan)!.ToString());
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void CreateExecutionPlan_UsesRomBootstrap_WhenMixedTapeBasicIsNotBootstrapSafe()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] protectedLoader = BuildBasicProgram(
                    BuildBasicLine(0, Token(234), Ascii("PROTECTED LOADER")),
                    BuildBasicLine(0,
                        Token(241), Ascii("entry"), Equals(),
                        Token(190), Ascii("23627"), NumberMarker(23627)),
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                byte[] tape = BuildTap(
                    BuildHeaderBlock(
                        type: 0,
                        fileName: "LOADER",
                        dataLength: (ushort)protectedLoader.Length,
                        parameter1: 0,
                        parameter2: (ushort)protectedLoader.Length),
                    BuildDataBlock(protectedLoader),
                    BuildDataBlock(new byte[] { 0x10, 0x20, 0x30 }, flag: 0x98));

                MethodInfo parseBlocks = typeof(TapLoader).GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;
                var blocks = (IReadOnlyList<TapeBlock>)parseBlocks.Invoke(null, new object[] { tape })!;
                var machine = new Spectrum128Machine(tempFolder);

                TapeLoadPlan plan = TapLoader.CreateExecutionPlan(machine, blocks);

                Assert.Equal(TapeLoadStrategy.RomBootstrapMounted, plan.Strategy);
                Assert.Equal(TapeLoadPlanReason.UnsupportedMixedBootstrapControlFlow, plan.ReasonCode);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void CreateExecutionPlan_UsesMountedRealtime_ForInconsistentBasicHeaderDataPair()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] payload = new byte[637];
                byte[] tape = BuildTap(
                    BuildHeaderBlock(
                        type: 0,
                        fileName: "LOADER",
                        dataLength: (ushort)payload.Length,
                        parameter1: 10,
                        parameter2: 42020),
                    BuildDataBlock(payload));
                MethodInfo parseBlocks = typeof(TapLoader).GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;
                var blocks = (IReadOnlyList<TapeBlock>)parseBlocks.Invoke(null, new object[] { tape })!;
                var machine = new Spectrum128Machine(tempFolder);

                TapeLoadPlan plan = TapLoader.CreateExecutionPlan(machine, blocks);

                Assert.Equal(TapeLoadStrategy.MountedRealtime, plan.Strategy);
                Assert.Equal(TapeLoadPlanReason.InconsistentStandardBasicPair, plan.ReasonCode);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void TryExecuteLoadedMountedBasicProgram_RejectsMalformedExpressionBeforeExecution()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] malformedProgram = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(235), Ascii("i"), Equals(), Ascii("("),
                        Ascii("1"), NumberMarker(1), Token(204),
                        Ascii("2"), NumberMarker(2)));
                TapeBlock headerBlock = TapeBlock.CreateData(
                    BuildHeaderBlock(
                        type: 0,
                        fileName: "MALFORMED",
                        dataLength: (ushort)malformedProgram.Length,
                        parameter1: 10,
                        parameter2: (ushort)malformedProgram.Length),
                    2168,
                    8063,
                    667,
                    735,
                    855,
                    1710,
                    8,
                    1000);
                MethodInfo parseHeaderInfo = typeof(TapLoader).GetMethod("ParseHeaderInfo", BindingFlags.NonPublic | BindingFlags.Static)!;
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                MethodInfo loadBasicProgram = typeof(TapLoader)
                    .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                    .Single(method => method.Name == "LoadBasicProgram" && method.GetParameters().Length == 3);
                MethodInfo tryExecute = typeof(TapLoader).GetMethod(
                    "TryExecuteLoadedMountedBasicProgram",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
                var machine = new Spectrum128Machine(tempFolder);
                loadBasicProgram.Invoke(null, new object[] { machine, header, malformedProgram });

                bool executed = (bool)tryExecute.Invoke(
                    null,
                    new object[]
                    {
                        machine,
                        (ushort)malformedProgram.Length,
                        (ushort)malformedProgram.Length,
                        (ushort)10
                    })!;

                Assert.False(executed);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void CreateExecutionPlan_UsesRomBootstrap_ForInteractivePauseBeforeStandardCodeLoad()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10, Token(245), Ascii(" "), Ascii("\"READY\"")),
                    BuildBasicLine(20, Token(242), Ascii("0"), NumberMarker(0)),
                    BuildBasicLine(30, Token(239), QuoteQuote(), Ascii(" "), Token(175)),
                    BuildBasicLine(40,
                        Token(245), Ascii(" "), Token(192), Ascii("25000"), NumberMarker(25000)));
                byte[] tape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "INTERACT", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 2, parameter1: 25000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0xF3, 0xC9 }));

                MethodInfo parseBlocks = typeof(TapLoader).GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;
                var blocks = (IReadOnlyList<TapeBlock>)parseBlocks.Invoke(null, new object[] { tape })!;
                var machine = new Spectrum128Machine(tempFolder);

                TapeLoadPlan plan = TapLoader.CreateExecutionPlan(machine, blocks);

                Assert.Equal(TapeLoadStrategy.RomBootstrapMounted, plan.Strategy);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadWithPolicy_Uses_Rom_Bootstrap_When_Mounted_Loader_Has_Unsupported_Control_Flow()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "control-flow-loader.tap");

            try
            {
                byte[] rom = new byte[16384];
                byte[] bootstrap =
                {
                    0x31, 0x58, 0xFF,       // LD SP,FF58
                    0x21, 0x50, 0xFF,       // LD HL,FF50
                    0x22, 0x3D, 0x5C,       // LD (ERR_SP),HL
                    0xC3, 0xE7, 0x15        // JP 15E7
                };
                Buffer.BlockCopy(bootstrap, 0, rom, 0, bootstrap.Length);
                File.WriteAllBytes(Path.Combine(tempFolder, "128-1.rom"), rom);

                byte[] loader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(253), Ascii("24575"), NumberMarker(24575),
                        Colon(),
                        Token(239), Ascii("\"\""), Token(175),
                        Colon(),
                        Token(239), Ascii("\"\""), Token(175),
                        Colon(),
                        Token(239), Ascii("\"\""), Token(175)),
                    BuildBasicLine(20,
                        Token(250), Token(192), Ascii("60895"), NumberMarker(60895),
                        Token(203), Token(248), Ascii("\"SCORE\""), Token(175),
                        Colon(),
                        Token(236), Ascii("20"), NumberMarker(20)),
                    BuildBasicLine(30,
                        Token(239), Ascii("\"SCORE\""), Token(175),
                        Colon(),
                        Token(236), Ascii("20"), NumberMarker(20)));
                byte[] tape = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "LOADER", dataLength: (ushort)loader.Length, parameter1: 10, parameter2: (ushort)loader.Length),
                    BuildDataBlock(loader),
                    BuildHeaderBlock(type: 3, fileName: "PART1", dataLength: 1, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0x11 }),
                    BuildHeaderBlock(type: 3, fileName: "PART2", dataLength: 1, parameter1: 0x9000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0x22 }),
                    BuildHeaderBlock(type: 3, fileName: "PART3", dataLength: 1, parameter1: 0xA000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0x33 }));
                File.WriteAllBytes(tapePath, tape);

                var machine = new Spectrum128Machine(tempFolder, SpectrumRomProfile.SyntheticTest);
                TapeExecutionResult result = TapLoader.LoadWithPolicy(machine, tapePath);

                Assert.Equal(TapeLoadStrategy.RomBootstrapMounted, result.Strategy);
                Assert.Equal(TapeLoadPlanReason.UnsupportedStandardBootstrapControlFlow, result.ReasonCode);
                Assert.Contains("ROM control", result.Reason, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                Assert.Equal((ushort)0x1B9E, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)10, machine.Cpu.Regs.HL);
                Assert.Equal(ReadWord(machine, 23613), machine.Cpu.Regs.SP);
                Assert.Equal((ushort)0x1303, ReadWord(machine, machine.Cpu.Regs.SP));
                Assert.NotEqual(0, machine.PeekMemory(23611) & 0x80);
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
                byte[] rom = new byte[16384];
                byte[] bootstrap =
                {
                    0x31, 0x58, 0xFF,       // LD SP,FF58
                    0x21, 0x50, 0xFF,       // LD HL,FF50
                    0x22, 0x3D, 0x5C,       // LD (ERR_SP),HL
                    0xC3, 0xE7, 0x15        // JP 15E7
                };
                Buffer.BlockCopy(bootstrap, 0, rom, 0, bootstrap.Length);
                File.WriteAllBytes(Path.Combine(tempFolder, "128-1.rom"), rom);

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

                var machine = new Spectrum128Machine(tempFolder, SpectrumRomProfile.SyntheticTest);
                MethodInfo parseBlocks = typeof(TapLoader).GetMethod("ParseBlocks", BindingFlags.NonPublic | BindingFlags.Static)!;
                MethodInfo loadRomBootstrap = typeof(TapLoader).GetMethod("LoadLeadingBasicProgramAndMountRemainingForRomAutoStart", BindingFlags.NonPublic | BindingFlags.Static)!;
                IReadOnlyList<TapeBlock> blocks = (IReadOnlyList<TapeBlock>)parseBlocks.Invoke(null, new object[] { tap })!;
                TapBootstrapResult result = (TapBootstrapResult)loadRomBootstrap.Invoke(null, new object[] { machine, "rom-driven.tap", blocks, true, 1, 1, true })!;

                Assert.Equal(4, result.TotalBlockCount);
                Assert.Equal(2, result.ConsumedBlockCount);
                Assert.True(machine.HasMountedTape);
                Assert.Equal("rom-driven.tap", machine.MountedTapeName);
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.Equal((byte)0, machine.PeekMemory(23620));
                MountedTape mountedTape = Assert.IsType<MountedTape>(machine.MountedTape);
                Assert.Equal("Pause", GetPrivateField(mountedTape, "earPlaybackState").ToString());
                Assert.Equal(3546900, (int)GetPrivateField(mountedTape, "earPulseLengthTStates"));
                Assert.Equal(2, (int)GetPrivateField(mountedTape, "earPlaybackBlockIndex"));
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
        public void ExecuteBootstrapBasicAutoStart_Evaluates_Int_Division_In_Poke_Value()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(228), Ascii("513"), NumberMarker(513),
                        Colon(),
                        Token(227), Ascii("z"),
                        Colon(),
                        Token(244), Ascii("32000"), NumberMarker(32000), Comma(),
                        Ascii("z-256"), NumberMarker(256), Ascii("*"), Token(186),
                        Ascii("(z/256"), NumberMarker(256), Ascii(")")));

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
                    fileName: "INTDIV",
                    dataLength: (ushort)program.Length,
                    parameter1: 10,
                    parameter2: (ushort)program.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, header, program });

                executeBootstrap.Invoke(null, new object[] { machine, (ushort)23755, (ushort)program.Length, (ushort)10, false });

                Assert.Equal((byte)1, machine.PeekMemory(32000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void BasicBootstrapExecutor_Tokenizes_Int_And_Division()
        {
            Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
            MethodInfo tokenize = executorType.GetMethod("Tokenize", BindingFlags.NonPublic | BindingFlags.Static)!;
            object tokenSpans = tokenize.Invoke(null, new object[]
            {
                Token(186).Concat(Ascii("(-513/256)\r")).ToArray()
            })!;

            string[] tokens = ((System.Collections.IEnumerable)tokenSpans)
                .Cast<object>()
                .Select(token => token.GetType().GetProperty("Text")!.GetValue(token)!.ToString()!)
                .ToArray();

            Assert.Equal(new[] { "INT", "(", "-", "513", "/", "256", ")" }, tokens);
        }

        [Fact]
        public void BasicBootstrapExpression_IntDivisionRoundsNegativeValuesDown()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                Type parserType = typeof(TapLoader).GetNestedType("ExpressionParser", BindingFlags.NonPublic)!;
                ConstructorInfo parserCtor = parserType
                    .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Single(ctor => ctor.GetParameters().Length == 6);
                var tokens = new List<string> { "INT", "(", "-", "513", "/", "256", ")" };
                object parser = parserCtor.Invoke(new object[]
                {
                    machine,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    tokens,
                    0,
                    tokens.Count - 1,
                    false
                });

                int value = (int)parserType
                    .GetMethod("Parse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .Invoke(parser, Array.Empty<object>())!;

                Assert.Equal(-3, value);
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
                ushort expectedVariablesEnd = (ushort)(23755 + protectedStage.Length);
                ushort expectedEditLine = (ushort)(expectedVariablesEnd + 1);
                Assert.Equal(expectedVars, ReadWord(machine, 23627));
                Assert.Equal(expectedEditLine, ReadWord(machine, 23641));
                Assert.Equal(expectedEditLine, ReadWord(machine, 23649));
                Assert.Equal(expectedEditLine, ReadWord(machine, 23651));
                Assert.Equal(expectedEditLine, ReadWord(machine, 23653));
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
                        Token(244), Ascii("23664"), Comma(), Token(190), Ascii("23620"), NumberMarker(23620),
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
                        Token(244), Ascii("23664"), Comma(), Token(190), Ascii("23620"), NumberMarker(23620),
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

                ushort expectedVariablesEnd = (ushort)(23755 + protectedStage.Length);
                ushort expectedEditLine = (ushort)(expectedVariablesEnd + 1);
                ushort expectedWorkspace = (ushort)(expectedEditLine + 1);
                Assert.Equal((byte)0x00, machine.PeekMemory(23624));
                Assert.Equal((byte)0, machine.PeekMemory(23662));
                Assert.Equal((byte)0x00, machine.PeekMemory(23663));
                Assert.Equal((byte)3, machine.PeekMemory(23664));
                Assert.Equal(expectedEditLine, ReadWord(machine, 23641));
                Assert.Equal(expectedWorkspace, ReadWord(machine, 23649));
                Assert.Equal((byte)(expectedWorkspace & 0xFF), machine.PeekMemory(expectedEditLine));
                Assert.Equal((byte)(expectedWorkspace >> 8), machine.PeekMemory((ushort)(expectedEditLine + 1)));
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
        public void BasicBootstrapExecutor_ApplyClear_Relocates_Low_Machine_Stack()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                var machine = new Spectrum128Machine(tempFolder);
                MethodInfo initializeMachine = typeof(TapLoader).GetMethod("InitializeMachineForFakeTapeLoad", BindingFlags.NonPublic | BindingFlags.Static)!;
                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                ConstructorInfo constructor = executorType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    new[] { typeof(Spectrum128Machine), typeof(ushort), typeof(ushort), typeof(bool) },
                    modifiers: null)!;
                MethodInfo applyClear = executorType.GetMethod("ApplyClear", BindingFlags.Instance | BindingFlags.NonPublic)!;

                initializeMachine.Invoke(null, new object[] { machine, false });
                machine.Cpu.Regs.SP = 0x5CFE;
                object executor = constructor.Invoke(new object[] { machine, (ushort)23755, (ushort)0, false });

                applyClear.Invoke(executor, new object[] { 65535 });

                Assert.Equal((ushort)0xFF58, machine.Cpu.Regs.SP);
                Assert.Equal((ushort)0xFFFE, ReadWord(machine, 23730));
                Assert.Equal((ushort)0xFFFF, ReadWord(machine, 23649));
                Assert.Equal((ushort)0xFFFF, ReadWord(machine, 23651));
                Assert.Equal((ushort)0xFFFF, ReadWord(machine, 23653));

                // BASIC loaders conventionally use CLEAR -1 to retain all RAM.
                applyClear.Invoke(executor, new object[] { -1 });

                Assert.Equal((ushort)0xFFFE, ReadWord(machine, 23730));
                Assert.Equal((ushort)0xFFFF, ReadWord(machine, 23649));
                Assert.Equal((ushort)0xFFFF, ReadWord(machine, 23651));
                Assert.Equal((ushort)0xFFFF, ReadWord(machine, 23653));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void TryExecuteImmediateSideEffectProgram_Preserves_Live_XPtr_For_Protected_Handoff()
        {
            string tempFolder = CreateTempRoms();

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(0,
                        Token(244), Ascii("40000"), NumberMarker(40000), Comma(),
                        Token(190), Ascii("23647"), NumberMarker(23647),
                        Colon(),
                        Token(244), Ascii("40001"), NumberMarker(40001), Comma(),
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
                Type executorType = typeof(TapLoader).GetNestedType("BasicBootstrapExecutor", BindingFlags.NonPublic)!;
                ConstructorInfo constructor = executorType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    new[] { typeof(Spectrum128Machine), typeof(ushort), typeof(ushort), typeof(bool) },
                    modifiers: null)!;
                MethodInfo executeImmediate = executorType.GetMethod(
                    "TryExecuteImmediateSideEffectProgram",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

                initializeMachine.Invoke(null, new object[] { machine, false });
                TapeBlock headerBlock = TapeBlock.CreateData(BuildHeaderBlock(
                    type: 0,
                    fileName: "XPTRCHK",
                    dataLength: (ushort)program.Length,
                    parameter1: 0,
                    parameter2: (ushort)program.Length), 2168, 8063, 667, 735, 855, 1710, 8, 1000);
                object header = parseHeaderInfo.Invoke(null, new object[] { headerBlock })!;
                loadBasicProgram.Invoke(null, new object[] { machine, header, program });
                machine.PokeMemory(23647, 0x34);
                machine.PokeMemory(23648, 0x12);

                object executor = constructor.Invoke(new object[] { machine, (ushort)23755, (ushort)program.Length, false });
                bool executed = (bool)executeImmediate.Invoke(
                    executor,
                    new object[] { (ushort)program.Length, (ushort)program.Length, (ushort)0, true })!;

                Assert.True(executed);
                Assert.Equal((byte)0x34, machine.PeekMemory(40000));
                Assert.Equal((byte)0x12, machine.PeekMemory(40001));
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
        public void LoadAllStandardBlocksAndAutoStart_Enters_PrintUsr_After_Satisfied_Code_Load()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "print-usr.tap");

            try
            {
                const ushort entryPoint = 25000;
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10, Token(239), QuoteQuote(), Ascii(" "), Token(175)),
                    BuildBasicLine(20,
                        Token(245), Ascii(" "), Token(192), Ascii("25000"), NumberMarker(entryPoint)));
                byte[] code = new byte[] { 0xF3, 0xC9 };

                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "PRINTUSR", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: (ushort)code.Length, parameter1: entryPoint, parameter2: 0),
                    BuildDataBlock(code));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.Equal(4, result.ConsumedBlockCount);
                Assert.Equal("PRINTUSR", result.AutoStartFileName);
                Assert.Equal(entryPoint, machine.Cpu.Regs.PC);
                Assert.False(machine.HasMountedTape);
                Assert.Equal((byte)0xF3, machine.PeekMemory(entryPoint));
                Assert.Equal((byte)0xC9, machine.PeekMemory((ushort)(entryPoint + 1)));
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
        public void LoadAllStandardBlocksAndAutoStart_Does_Not_Treat_Unchanged_Wait_Loop_As_A_Handoff()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "unchanged-wait-loop.tap");

            try
            {
                const ushort entryPoint = 0xFC00;
                byte[] basicLoader = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("64512"), NumberMarker(entryPoint)));
                byte[] codeLoader = { 0x10, 0xFE, 0xC9 };
                byte[] customData = { 0xAA };
                byte[] tap = BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "LOADER", dataLength: (ushort)basicLoader.Length, parameter1: 10, parameter2: (ushort)basicLoader.Length),
                    BuildDataBlock(basicLoader),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: (ushort)codeLoader.Length, parameter1: entryPoint, parameter2: 0),
                    BuildDataBlock(codeLoader),
                    BuildHeaderBlock(type: 42, fileName: "CUSTOM", dataLength: (ushort)customData.Length, parameter1: 0x9000, parameter2: 0),
                    BuildDataBlock(customData));

                File.WriteAllBytes(tapePath, tap);

                var machine = new Spectrum128Machine(tempFolder);
                TapBootstrapResult result = TapLoader.LoadAllStandardBlocksAndAutoStart(machine, tapePath);

                Assert.Equal(6, result.ConsumedBlockCount);
                Assert.Equal(entryPoint, machine.Cpu.Regs.PC);
                Assert.True(machine.HasMountedTape);
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadAllStandardBlocksAndAutoStart_Resumes_At_Unique_BasicPatched_Loader_Wait_Loop()
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

        [Fact]
        public void LoadWithPolicy_PreloadedAutorunWithoutLoad_EntersRealRomBasicInterpreter()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "rom-autorun.tap");

            try
            {
                byte[] rom = new byte[16384];
                byte[] bootstrap =
                {
                    0x31, 0x58, 0xFF,
                    0x21, 0x50, 0xFF,
                    0x22, 0x3D, 0x5C,
                    0xC3, 0xE7, 0x15
                };
                Buffer.BlockCopy(bootstrap, 0, rom, 0, bootstrap.Length);
                File.WriteAllBytes(Path.Combine(tempFolder, "128-1.rom"), rom);

                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                File.WriteAllBytes(tapePath, BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "ROMRUN", dataLength: (ushort)program.Length, parameter1: 10, parameter2: (ushort)program.Length),
                    BuildDataBlock(program),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 1, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0xC9 })));

                var machine = new Spectrum128Machine(tempFolder, SpectrumRomProfile.SyntheticTest);
                TapeExecutionResult result = TapLoader.LoadWithPolicy(machine, tapePath);

                Assert.Equal(TapeLoadStrategy.FullFakeLoad, result.Strategy);
                Assert.Equal((ushort)0x1B9E, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)10, ReadWord(machine, 23618));
                Assert.Equal((byte)0xC9, machine.PeekMemory(0x8000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

        [Fact]
        public void LoadWithPolicy_PreloadedAutorunWithLoad_KeepsBootstrapLoadSkipping()
        {
            string tempFolder = CreateTempRoms();
            string tapePath = Path.Combine(tempFolder, "bootstrap-autorun.tap");

            try
            {
                byte[] program = BuildBasicProgram(
                    BuildBasicLine(10,
                        Token(239), QuoteQuote(), Ascii(" "), Token(175),
                        Colon(),
                        Token(249), Ascii(" "), Token(192), Ascii("32768"), NumberMarker(32768)));
                File.WriteAllBytes(tapePath, BuildTap(
                    BuildHeaderBlock(type: 0, fileName: "BOOTRUN", dataLength: (ushort)program.Length, parameter1: 10, parameter2: (ushort)program.Length),
                    BuildDataBlock(program),
                    BuildHeaderBlock(type: 3, fileName: "CODE", dataLength: 1, parameter1: 0x8000, parameter2: 0),
                    BuildDataBlock(new byte[] { 0xC9 })));

                var machine = new Spectrum128Machine(tempFolder);
                TapeExecutionResult result = TapLoader.LoadWithPolicy(machine, tapePath);

                Assert.Equal(TapeLoadStrategy.FullFakeLoad, result.Strategy);
                Assert.Equal((ushort)0x8000, machine.Cpu.Regs.PC);
                Assert.Equal((byte)0xC9, machine.PeekMemory(0x8000));
            }
            finally
            {
                Directory.Delete(tempFolder, true);
            }
        }

    }
}
