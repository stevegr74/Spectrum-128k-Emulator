using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator.Tap
{
    public static partial class TapLoader
    {
        private const int TapHeaderPayloadLength = 17;
        private const byte HeaderFlag = 0x00;
        private const byte DataFlag = 0xFF;
        private const ushort StandardTapPauseAfterBlockMs = 1000;

        private const byte ProgramType = 0;
        private const byte NumberArrayType = 1;
        private const byte CharacterArrayType = 2;
        private const byte CodeType = 3;
        private const ushort HeaderPilotPulseCount = 8063;
        private const ushort DataPilotPulseCount = 3223;
        private const ushort PilotPulseLengthTStates = 2168;
        private const ushort SyncFirstPulseLengthTStates = 667;
        private const ushort SyncSecondPulseLengthTStates = 735;
        private const ushort ZeroBitPulseLengthTStates = 855;
        private const ushort OneBitPulseLengthTStates = 1710;
        private const int TStatesPerMillisecond48k = 3500;
        private const int DefaultTapeAutoStartInitialInterruptDelay = 32;
        private const int ProtectedLiveTapeTimingDivisor = 8;
        private const int ProtectedLiveChainTapeTimingDivisor = 64;
        private const int PreloadedLoadableReplayTimingDivisor = 1;
        private const int RomInitializationFrameLimit = 150;

        private const ushort BasicProgramStart = 23755;
        private const ushort DefaultStackPointer = 0xFF58;
        private const ushort RomSystemVariablesBase = 0x5C3A;
        private const ushort ErrorNumberSystemVariableAddress = RomSystemVariablesBase;

        private const ushort NewPpcAddress = 23618;
        private const ushort NspPcAddress = 23620;
        private const ushort PpcAddress = 23621;
        private const ushort SubPpcAddress = 23623;
        private const ushort ErrorStackPointerAddress = 23613;
        private const ushort FlagsSystemVariableAddress = 23611;
        private const ushort TvFlagSystemVariableAddress = 23612;
        private const ushort BorderSystemVariableAddress = 23624;
        private const ushort StreamsAddress = 23568;
        private const ushort VarsAddress = 23627;
        private const ushort ChansAddress = 23631;
        private const ushort CurChlAddress = 23633;
        private const ushort ProgAddress = 23635;
        private const ushort NextLineAddress = 23637;
        private const ushort DataAddress = 23639;
        private const ushort EditLineAddress = 23641;
        private const ushort KCurAddress = 23643;
        private const ushort ChAddAddress = 23645;
        private const ushort XPtrAddress = 23647;
        private const ushort WorkspaceAddress = 23649;
        private const ushort StackBottomAddress = 23651;
        private const ushort StackEndAddress = 23653;
        private const ushort RamTopAddress = 23730;
        private const ushort PhysicalRamTopAddress = 23732;
        private const ushort Spectrum128TapeLoadBankSelectAddress = 23388;
        private const ushort Spectrum128RamRstAddress = 23389;
        private const byte Spectrum128RamRstOpcode = 0xCF;
        private const ushort InitialChannelsAreaAddress = BasicProgramStart - 21;
        private const ushort KeyboardChannelDescriptorAddress = InitialChannelsAreaAddress;
        private const ushort ScreenChannelDescriptorAddress = InitialChannelsAreaAddress + 5;

        public static TapLoadResult Load(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            IReadOnlyList<TapeBlock> blocks = ParseBlocks(fileData);
            if (blocks.Count == 0)
                throw new InvalidOperationException("The .tap file does not contain any blocks.");

            bool use128kMode = Use128kTapeLoadMode(machine);
            InitializeMachineForFakeTapeLoad(machine, use128kMode);

            TapHeaderInfo? pendingHeader = null;
            int loadedBlockCount = 0;
            string? autoStartFileName = null;

            foreach (TapeBlock block in blocks)
            {
                if (block.Flag == HeaderFlag)
                {
                    pendingHeader = ParseHeaderInfo(block);
                    continue;
                }

                if (block.Flag != DataFlag)
                    throw new InvalidOperationException($"Unsupported tape block flag 0x{block.Flag:X2}.");

                if (pendingHeader == null)
                    throw new InvalidOperationException("Encountered a tape data block without a preceding header block.");

                if (block.Payload == null || block.Payload.Length != pendingHeader.DataLength)
                {
                    throw new InvalidOperationException(
                        $"Tape data block length mismatch for '{pendingHeader.FileName}'. Expected {pendingHeader.DataLength} bytes, got {block.Payload?.Length ?? 0}.");
                }

                LoadDataBlock(machine, pendingHeader, block.Payload);
                loadedBlockCount++;

                if (pendingHeader.Type == ProgramType && pendingHeader.AutoStartLine < 32768)
                    autoStartFileName = pendingHeader.FileName;

                pendingHeader = null;
            }

            if (pendingHeader != null)
                throw new InvalidOperationException($"Tape ended after header '{pendingHeader.FileName}' without a matching data block.");

            return new TapLoadResult(blocks.Count, loadedBlockCount, autoStartFileName);
        }

        public static TapMountResult Mount(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            IReadOnlyList<TapeBlock> blocks = ParseBlocks(fileData);
            if (blocks.Count == 0)
                throw new InvalidOperationException("The .tap file does not contain any blocks.");

            var tape = new MountedTape(Path.GetFileName(path), blocks);
            machine.MountTape(tape);
            LogMountedTape(tape, blocks);
            return new TapMountResult(blocks.Count, Path.GetFileName(path));
        }

        public static TapeExecutionResult LoadWithPolicy(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            IReadOnlyList<TapeBlock> blocks = ParseBlocks(fileData);
            string displayName = Path.GetFileName(path);
            TapeLoadPlan plan = CreateExecutionPlan(machine, blocks);

            return ExecutePlan(machine, displayName, blocks, plan);
        }

        public static TapBootstrapResult LoadAllStandardBlocksAndAutoStart(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            IReadOnlyList<TapeBlock> blocks = ParseBlocks(fileData);
            return LoadAllStandardTapeBlocksAndAutoStart(
                machine,
                Path.GetFileName(path),
                blocks,
                skipCustomHeaderForEarPlayback: false,
                remountPlaybackRemainder: FindFirstCustomHeaderBlockIndex(blocks) >= 0,
                stopBeforeFirstCustomHeader: false);
        }

        public static TapBootstrapResult BootstrapBasicProgramAndMountRemaining(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            IReadOnlyList<TapeBlock> blocks = ParseBlocks(fileData);
            return BootstrapTapeBlocksAndMountRemaining(machine, Path.GetFileName(path), blocks);
        }

        internal static TapBootstrapResult BootstrapTapeBlocksAndMountRemaining(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            bool skipCustomHeaderForEarPlayback = true,
            int nonRomTimingDivisor = 1,
            int loadableTimingDivisor = 1,
            bool initialEarLevelHigh = true)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (blocks == null)
                throw new ArgumentNullException(nameof(blocks));
            if (blocks.Count < 2)
                throw new InvalidOperationException("The tape image does not contain enough blocks to bootstrap a BASIC loader.");

            bool use128kMode = Use128kTapeLoadMode(machine);
            InitializeMachineForFakeTapeLoad(machine, use128kMode);
            if (use128kMode)
            {
                // Hybrid execution can hand paging-aware machine code directly
                // into live execution. Keep the ROM's BANK_M mirror aligned with
                // the ROM-1 paging latch established by ConfigureFor128kTapeLoad.
                machine.PokeMemory(Spectrum128TapeLoadBankSelectAddress, 0x10);
            }
            int consumedBlockCount = 0;
            string? autoStartFileName = null;

            while (consumedBlockCount < blocks.Count && blocks[consumedBlockCount].Kind == TapeBlockKind.Metadata)
                consumedBlockCount++;

            if (consumedBlockCount + 1 >= blocks.Count)
                throw new InvalidOperationException("The tape image does not contain a complete leading BASIC header/data pair.");

            TapeBlock headerBlock = blocks[consumedBlockCount];
            TapeBlock dataBlock = blocks[consumedBlockCount + 1];

            if (!IsStandardHeaderBlock(headerBlock) || dataBlock.Flag != DataFlag)
                throw new InvalidOperationException("The tape image does not begin with a standard BASIC header/data pair.");

            TapHeaderInfo header = ParseHeaderInfo(headerBlock);
            if (header.Type != ProgramType)
                throw new InvalidOperationException($"The leading tape header must be BASIC, but was type {header.Type}.");

            ushort effectiveProgramLength = (ushort)Math.Min(header.ProgramLength, dataBlock.Payload!.Length);
            var effectiveHeader = new TapHeaderInfo(
                header.Type,
                header.FileName,
                (ushort)dataBlock.Payload!.Length,
                header.AutoStartLine,
                effectiveProgramLength);

            LoadBasicProgram(machine, effectiveHeader, dataBlock.Payload!);
            if (effectiveHeader.AutoStartLine < 32768)
                autoStartFileName = effectiveHeader.FileName;

            AdvanceBootstrapTapeTime(
                machine,
                EstimateTapeBlockDurationTStates(headerBlock) + EstimateTapeBlockDurationTStates(dataBlock));

            consumedBlockCount += 2;

            int playbackStartBlockIndex = consumedBlockCount;

            var tape = new MountedTape(
                displayName,
                blocks,
                initialBlockIndex: playbackStartBlockIndex,
                skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback,
                initialPrePlaybackPauseTStates: dataBlock.PauseAfterBlockMs * TStatesPerMillisecond48k,
                nonRomTimingDivisor: nonRomTimingDivisor,
                loadableTimingDivisor: loadableTimingDivisor,
                initialEarLevelHigh: initialEarLevelHigh);
            machine.MountTape(tape);

            // A BASIC bootstrap followed by raw standard records must retain the
            // interpreter continuation that consumes those records before USR runs.
            if (RequiresMountedRealtimeForMixedTape(machine, blocks))
            {
                Func<Spectrum128Machine, ushort?>? continuationResolver =
                    BasicBootstrapExecutor.CreateMountedLoadUsrContinuationResolver(
                        machine,
                        BasicProgramStart,
                        effectiveProgramLength,
                        effectiveHeader.AutoStartLine,
                        requireUsrReturnAddressBetweenSteps: false);
                if (continuationResolver != null)
                    machine.SetPendingMountedLoadUsrContinuationResolver(continuationResolver);
            }

            BootstrapExecutionResult bootstrapExecutionResult = BootstrapExecutionResult.None;
            if (effectiveHeader.AutoStartLine < 32768)
            {
                bootstrapExecutionResult = ExecuteBootstrapBasicAutoStart(
                    machine,
                    BasicProgramStart,
                    effectiveProgramLength,
                    effectiveHeader.AutoStartLine);

                if (bootstrapExecutionResult.ConsumedMountedLoadCount > 0 && machine.HasMountedTape)
                {
                    int adjustedPlaybackStartBlockIndex = SkipSatisfiedStandardLoads(
                        blocks,
                        playbackStartBlockIndex,
                        bootstrapExecutionResult.ConsumedMountedLoadCount);
                    if (adjustedPlaybackStartBlockIndex != playbackStartBlockIndex)
                    {
                        tape = new MountedTape(
                            displayName,
                            blocks,
                            initialBlockIndex: adjustedPlaybackStartBlockIndex,
                            skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback,
                            nonRomTimingDivisor: nonRomTimingDivisor,
                            loadableTimingDivisor: loadableTimingDivisor,
                            initialEarLevelHigh: initialEarLevelHigh);
                        machine.MountTape(tape);
                    }
                }
            }

            int currentPhase = (int)(machine.Cpu.TStates % (ulong)machine.FrameTStates);
            machine.SetSnapshotResumeFramePhase(currentPhase);
            if (currentPhase == 0)
                machine.SetInitialInterruptDelay(DefaultTapeAutoStartInitialInterruptDelay);
            LogMountedTape(tape, blocks);
            return new TapBootstrapResult(blocks.Count, consumedBlockCount, displayName, autoStartFileName);
        }


        internal static bool CanLoadAllStandardTapeBlocks(IReadOnlyList<TapeBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0)
                return false;

            int index = 0;
            bool sawProgram = false;

            while (index < blocks.Count)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index >= blocks.Count)
                    break;

                if (index + 1 >= blocks.Count)
                    return false;

                TapeBlock headerBlock = blocks[index];
                TapeBlock dataBlock = blocks[index + 1];
                if (!IsStandardHeaderBlock(headerBlock) || dataBlock.Flag != DataFlag)
                    return false;

                TapHeaderInfo header = ParseHeaderInfo(headerBlock);
                if (header.Type == NumberArrayType || header.Type == CharacterArrayType)
                    return false;

                if (header.Type == ProgramType)
                    sawProgram = true;

                index += 2;
            }

            return sawProgram;
        }

        internal static bool CanBootstrapBasicProgramAndMountRemaining(IReadOnlyList<TapeBlock> blocks)
        {
            if (blocks == null || blocks.Count < 2)
                return false;

            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count)
                return false;

            TapeBlock headerBlock = blocks[index];
            TapeBlock dataBlock = blocks[index + 1];
            if (!IsStandardHeaderBlock(headerBlock) || dataBlock.Flag != DataFlag)
                return false;

            TapHeaderInfo header = ParseHeaderInfo(headerBlock);
            return header.Type == ProgramType;
        }

        internal static TapeLoadPlan CreateExecutionPlan(Spectrum128Machine machine, IReadOnlyList<TapeBlock> blocks)
        {
            return TapeLoadPlanner.Create(machine, blocks);
        }

        internal static bool HasTwoStageBasicPrefixBeforeElectricallyDecodedProtectedStream(
            IReadOnlyList<TapeBlock> blocks)
        {
            int index = 0;
            int programCount = 0;
            while (index < blocks.Count)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index + 1 >= blocks.Count ||
                    !IsStandardHeaderBlock(blocks[index]) ||
                    blocks[index + 1].Flag != DataFlag)
                {
                    break;
                }

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                if (header.Type != ProgramType)
                    break;

                programCount++;
                index += 2;
            }

            if (programCount != 2)
                return false;

            for (; index < blocks.Count; index++)
            {
                TapeBlock block = blocks[index];
                if (!block.CanUseRomLoadTrap &&
                    block.Kind is TapeBlockKind.Data or TapeBlockKind.DirectRecording)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool StartsProtectedRemainderImmediatelyAfterLeadingBasic(IReadOnlyList<TapeBlock> blocks)
        {
            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            index += 2;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            return index < blocks.Count && !blocks[index].IsLoadableRomBlock;
        }

        internal static TapeExecutionResult ExecutePlan(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            TapeLoadPlan plan,
            bool initialEarLevelHigh = true)
        {
            int nonRomTimingDivisor = GetProtectedLiveTapeTimingDivisor(plan.Strategy, blocks);
            int loadableTimingDivisor = GetPreloadedLoadableReplayTimingDivisor(plan.Strategy);
            switch (plan.Strategy)
            {
                case TapeLoadStrategy.FullFakeLoad:
                {
                    TapBootstrapResult result = LoadAllStandardTapeBlocksAndAutoStart(
                        machine,
                        displayName,
                        blocks,
                        skipCustomHeaderForEarPlayback: false,
                        remountPlaybackRemainder: FindFirstCustomHeaderBlockIndex(blocks) >= 0,
                        stopBeforeFirstCustomHeader: false,
                        loadableTimingDivisor: loadableTimingDivisor,
                        initialEarLevelHigh: initialEarLevelHigh);
                    return new TapeExecutionResult(
                        TapeLoadStrategy.FullFakeLoad,
                        plan.ReasonCode,
                        plan.Reason,
                        result.TotalBlockCount,
                        result.ConsumedBlockCount,
                        result.DisplayName,
                        result.AutoStartFileName);
                }

                case TapeLoadStrategy.LeadingStandardChainFakeLoad:
                {
                    TapBootstrapResult result = LoadLeadingStandardBasicChainAndMountRemaining(
                        machine,
                        displayName,
                        blocks,
                        skipCustomHeaderForEarPlayback: false,
                        nonRomTimingDivisor: nonRomTimingDivisor,
                        loadableTimingDivisor: loadableTimingDivisor,
                        initialEarLevelHigh: initialEarLevelHigh);
                    return new TapeExecutionResult(
                        TapeLoadStrategy.LeadingStandardChainFakeLoad,
                        plan.ReasonCode,
                        plan.Reason,
                        result.TotalBlockCount,
                        result.ConsumedBlockCount,
                        result.DisplayName,
                        result.AutoStartFileName);
                }

                case TapeLoadStrategy.RomBootstrapMounted:
                {
                    bool requiresNative128Basic =
                        machine.MachineModel == SpectrumMachineModel.Spectrum128K &&
                        !ReferenceEquals(machine.RomProfile, SpectrumRomProfile.SyntheticTest) &&
                        (plan.ReasonCode == TapeLoadPlanReason.UnsupportedStandardBootstrapControlFlow ||
                         (plan.ReasonCode == TapeLoadPlanReason.UnsupportedMixedBootstrapControlFlow &&
                          Requires128kTapeLoadModeForStandardTape(machine, blocks)));
                    TapBootstrapResult result = requiresNative128Basic
                        ? StartNative128kRomTapeLoader(
                            machine,
                            displayName,
                            blocks,
                            nonRomTimingDivisor,
                            loadableTimingDivisor,
                            initialEarLevelHigh)
                        : LoadLeadingBasicProgramAndMountRemainingForRomAutoStart(
                            machine,
                            displayName,
                            blocks,
                            skipCustomHeaderForEarPlayback: false,
                            nonRomTimingDivisor: nonRomTimingDivisor,
                            loadableTimingDivisor: loadableTimingDivisor,
                            initialEarLevelHigh: initialEarLevelHigh,
                            allowRomLoadTraps: plan.ReasonCode != TapeLoadPlanReason.UnrepresentableMountedContinuation);
                    return new TapeExecutionResult(
                        TapeLoadStrategy.RomBootstrapMounted,
                        plan.ReasonCode,
                        plan.Reason,
                        result.TotalBlockCount,
                        result.ConsumedBlockCount,
                        result.DisplayName,
                        result.AutoStartFileName);
                }

                case TapeLoadStrategy.BootstrapHybrid:
                {
                    TapBootstrapResult result = BootstrapTapeBlocksAndMountRemaining(
                        machine,
                        displayName,
                        blocks,
                        skipCustomHeaderForEarPlayback: false,
                        nonRomTimingDivisor: nonRomTimingDivisor,
                        loadableTimingDivisor: loadableTimingDivisor,
                        initialEarLevelHigh: initialEarLevelHigh);
                    return new TapeExecutionResult(
                        TapeLoadStrategy.BootstrapHybrid,
                        plan.ReasonCode,
                        plan.Reason,
                        result.TotalBlockCount,
                        result.ConsumedBlockCount,
                        result.DisplayName,
                        result.AutoStartFileName);
                }

                default:
                {
                    bool use128kMode = Use128kTapeLoadMode(machine);
                    InitializeMachineForFakeTapeLoad(machine, use128kMode);
                    var tape = new MountedTape(
                        displayName,
                        blocks,
                        nonRomTimingDivisor: nonRomTimingDivisor,
                        loadableTimingDivisor: loadableTimingDivisor,
                        initialEarLevelHigh: initialEarLevelHigh);
                    machine.MountTape(tape);
                    LogMountedTape(tape, blocks);
                    return new TapeExecutionResult(
                        TapeLoadStrategy.MountedRealtime,
                        plan.ReasonCode,
                        plan.Reason,
                        blocks.Count,
                        0,
                        displayName,
                        null);
                }
            }
        }

        private static int GetProtectedLiveTapeTimingDivisor(TapeLoadStrategy strategy, IReadOnlyList<TapeBlock> blocks)
        {
            if (strategy == TapeLoadStrategy.FullFakeLoad || blocks.Count == 0)
                return 1;

            if (ContainsElectricallyDecodedProtectedStream(blocks))
                return 1;

            int protectedTimingDivisor = strategy == TapeLoadStrategy.LeadingStandardChainFakeLoad
                ? ProtectedLiveChainTapeTimingDivisor
                : ProtectedLiveTapeTimingDivisor;

            foreach (TapeBlock block in blocks)
            {
                if (!block.CanUseRomLoadTrap && block.Kind != TapeBlockKind.Metadata)
                    return protectedTimingDivisor;
            }

            return 1;
        }

        private static int GetPreloadedLoadableReplayTimingDivisor(TapeLoadStrategy strategy)
        {
            return strategy switch
            {
                TapeLoadStrategy.FullFakeLoad => PreloadedLoadableReplayTimingDivisor,
                TapeLoadStrategy.LeadingStandardChainFakeLoad => PreloadedLoadableReplayTimingDivisor,
                _ => 1
            };
        }

        private static bool ContainsElectricallyDecodedProtectedStream(IReadOnlyList<TapeBlock> blocks)
        {
            foreach (TapeBlock block in blocks)
            {
                if (block.CanUseRomLoadTrap)
                    continue;

                if (block.Kind == TapeBlockKind.Data || block.Kind == TapeBlockKind.DirectRecording)
                    return true;
            }

            return false;
        }

        internal static TapBootstrapResult LoadAllStandardTapeBlocksAndAutoStart(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            bool skipCustomHeaderForEarPlayback = true,
            bool remountPlaybackRemainder = true,
            bool stopBeforeFirstCustomHeader = false,
            int nonRomTimingDivisor = 1,
            int loadableTimingDivisor = 1,
            bool initialEarLevelHigh = true)
        {
            if (!stopBeforeFirstCustomHeader && !CanLoadAllStandardTapeBlocks(blocks))
                throw new InvalidOperationException("The tape image contains nonstandard blocks and cannot be fully fake-loaded.");

            bool use128kMode = Use128kTapeLoadMode(machine);
            if (RequiresMountedLoadSemanticsForStandardTape(machine, blocks, use128kMode))
            {
                return BootstrapTapeBlocksAndMountRemaining(
                    machine,
                    displayName,
                    blocks,
                    skipCustomHeaderForEarPlayback,
                    initialEarLevelHigh: initialEarLevelHigh);
            }

            InitializeMachineForFakeTapeLoad(machine, use128kMode);

            string? autoStartFileName = null;
            ushort autoStartProgramStart = 0;
            ushort autoStartProgramLength = 0;
            ushort autoStartLine = 0xFFFF;
            int? playbackStartBlockIndex = null;
            int consumedBlockCount = 0;
            int index = 0;
            ulong consumedTStates = 0;

            while (index < blocks.Count)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                {
                    consumedTStates += EstimateTapeBlockDurationTStates(blocks[index]);
                    index++;
                    consumedBlockCount++;
                }

                if (index >= blocks.Count)
                    break;

                if (stopBeforeFirstCustomHeader &&
                    (index + 1 >= blocks.Count ||
                     !IsStandardHeaderBlock(blocks[index]) ||
                     blocks[index + 1].Flag != DataFlag))
                {
                    break;
                }

                TapeBlock headerBlock = blocks[index];
                TapeBlock dataBlock = blocks[index + 1];
                TapHeaderInfo header = ParseHeaderInfo(headerBlock);

                if (stopBeforeFirstCustomHeader && header.Type > CodeType)
                {
                    break;
                }

                LoadDataBlock(machine, header, dataBlock.Payload!);
                consumedTStates += EstimateTapeBlockDurationTStates(headerBlock);
                consumedTStates += EstimateTapeBlockDurationTStates(dataBlock);

                if (header.Type == ProgramType && autoStartFileName == null && header.AutoStartLine < 32768)
                {
                    autoStartFileName = header.FileName;
                    autoStartProgramStart = BasicProgramStart;
                    autoStartProgramLength = header.ProgramLength;
                    autoStartLine = header.AutoStartLine;
                    playbackStartBlockIndex = index + 2;
                }

                index += 2;
                consumedBlockCount += 2;
            }

            IReadOnlyList<CustomLoaderWaitLoop> customLoaderWaitLoops =
                CaptureCustomLoaderWaitLoops(machine, blocks);
            int ignoredLoadCount = 0;
            if (autoStartFileName != null)
            {
                if (BasicBootstrapExecutor.CanRunPreloadedProgramUnderRom(
                        machine,
                        autoStartProgramStart,
                        autoStartProgramLength,
                        autoStartLine))
                {
                    InitializeMachineForRomDrivenTapeLoad(machine, use128kMode);
                    if (CanStartRomDrivenMountedBasicAutoStart(machine))
                    {
                        ReloadConsumedStandardBlocks(machine, blocks, consumedBlockCount);
                        AdvanceBootstrapTapeTime(machine, consumedTStates);
                        StartRomDrivenMountedBasicAutoStart(machine, autoStartLine);
                    }
                    else
                    {
                        InitializeMachineForFakeTapeLoad(machine, use128kMode);
                        ReloadConsumedStandardBlocks(machine, blocks, consumedBlockCount);
                        AdvanceBootstrapTapeTime(machine, consumedTStates);
                        ignoredLoadCount = ExecuteBootstrapBasicAutoStart(
                            machine,
                            autoStartProgramStart,
                            autoStartProgramLength,
                            autoStartLine,
                            ignoreLoadStatements: true).IgnoredLoadCount;
                    }
                }
                else
                {
                    AdvanceBootstrapTapeTime(machine, consumedTStates);
                    ignoredLoadCount = ExecuteBootstrapBasicAutoStart(
                        machine,
                        autoStartProgramStart,
                        autoStartProgramLength,
                        autoStartLine,
                        ignoreLoadStatements: true).IgnoredLoadCount;
                }
            }
            else
            {
                AdvanceBootstrapTapeTime(machine, consumedTStates);
            }

            if (TryResolvePatchedCustomLoaderWaitLoop(machine, customLoaderWaitLoops, out ushort resumePc))
                machine.Cpu.Regs.PC = resumePc;

            if (remountPlaybackRemainder)
            {
                int remountStartIndex = stopBeforeFirstCustomHeader
                    ? consumedBlockCount
                    : playbackStartBlockIndex ?? consumedBlockCount;
                int adjustedPlaybackStartBlockIndex = stopBeforeFirstCustomHeader
                    ? remountStartIndex
                    : SkipSatisfiedStandardLoads(blocks, remountStartIndex, ignoredLoadCount);
                if (adjustedPlaybackStartBlockIndex < blocks.Count)
                {
                    var playbackTape = new MountedTape(
                        displayName,
                        blocks,
                        adjustedPlaybackStartBlockIndex,
                        skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback,
                        nonRomTimingDivisor: nonRomTimingDivisor,
                        loadableTimingDivisor: loadableTimingDivisor,
                        initialEarLevelHigh: initialEarLevelHigh);
                    machine.MountTape(playbackTape);
                }
            }

            if (machine.HasMountedTape && !machine.MountedTape!.HasRemainingBlocks)
            {
                machine.EjectTape();
            }

            int currentPhase = (int)(machine.Cpu.TStates % (ulong)machine.FrameTStates);
            machine.SetSnapshotResumeFramePhase(currentPhase);
            if (currentPhase == 0)
                machine.SetInitialInterruptDelay(DefaultTapeAutoStartInitialInterruptDelay);

            return new TapBootstrapResult(blocks.Count, consumedBlockCount, displayName, autoStartFileName);
        }

        private static void ReloadConsumedStandardBlocks(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks,
            int consumedBlockCount)
        {
            int index = 0;
            while (index < consumedBlockCount)
            {
                if (blocks[index].Kind == TapeBlockKind.Metadata)
                {
                    index++;
                    continue;
                }

                if (index + 1 >= consumedBlockCount ||
                    !IsStandardHeaderBlock(blocks[index]) ||
                    blocks[index + 1].Flag != DataFlag)
                {
                    throw new InvalidOperationException("The consumed tape prefix is not a complete standard header/data chain.");
                }

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                LoadDataBlock(machine, header, blocks[index + 1].Payload!);
                index += 2;
            }
        }

        private static IReadOnlyList<CustomLoaderWaitLoop> CaptureCustomLoaderWaitLoops(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks)
        {
            if (FindFirstCustomHeaderBlockIndex(blocks) < 0)
                return Array.Empty<CustomLoaderWaitLoop>();

            var waitLoops = new List<CustomLoaderWaitLoop>();
            for (int blockIndex = 0; blockIndex + 1 < blocks.Count; blockIndex++)
            {
                if (!IsStandardHeaderBlock(blocks[blockIndex]) ||
                    blocks[blockIndex + 1].Flag != DataFlag ||
                    blocks[blockIndex + 1].Payload == null)
                {
                    continue;
                }

                TapHeaderInfo header = ParseHeaderInfo(blocks[blockIndex]);
                if (header.Type != CodeType)
                    continue;

                byte[] originalCode = blocks[blockIndex + 1].Payload!;
                int searchLength = Math.Min(header.DataLength, originalCode.Length);
                for (int offset = 0; offset + 1 < searchLength; offset++)
                {
                    if (originalCode[offset] != 0x10 || originalCode[offset + 1] != 0xFE)
                        continue;

                    ushort address = (ushort)(header.StartAddress + offset);
                    if (machine.PeekMemory(address) == 0x10 &&
                        machine.PeekMemory((ushort)(address + 1)) == 0xFE)
                    {
                        waitLoops.Add(new CustomLoaderWaitLoop(
                            address,
                            header.StartAddress,
                            header.DataLength));
                    }
                }
            }

            return waitLoops;
        }

        private static bool TryResolvePatchedCustomLoaderWaitLoop(
            Spectrum128Machine machine,
            IReadOnlyList<CustomLoaderWaitLoop> waitLoops,
            out ushort resumePc)
        {
            resumePc = 0;
            ushort currentPc = machine.Cpu.Regs.PC;
            ushort? patchedWaitLoop = null;
            foreach (CustomLoaderWaitLoop waitLoop in waitLoops)
            {
                if (currentPc < waitLoop.CodeStart ||
                    currentPc - waitLoop.CodeStart >= waitLoop.CodeLength ||
                    (machine.PeekMemory(waitLoop.Address) == 0x10 &&
                     machine.PeekMemory((ushort)(waitLoop.Address + 1)) == 0xFE))
                {
                    continue;
                }

                if (patchedWaitLoop.HasValue)
                    return false;

                patchedWaitLoop = waitLoop.Address;
            }

            if (!patchedWaitLoop.HasValue)
                return false;

            resumePc = patchedWaitLoop.Value;
            return true;
        }

        private readonly record struct CustomLoaderWaitLoop(
            ushort Address,
            ushort CodeStart,
            ushort CodeLength);

        internal static bool RequiresRomDrivenBootstrapForStandardTape(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks,
            bool use128kMode)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                return false;

            TapHeaderInfo firstHeader = ParseHeaderInfo(blocks[index]);
            if (firstHeader.Type != ProgramType || firstHeader.AutoStartLine >= 32768)
                return false;

            if (HasNonContiguousBasicProgramStage(blocks, index + 2))
                return true;

            InitializeMachineForFakeTapeLoad(machine, use128kMode);
            LoadBasicProgram(machine, firstHeader, blocks[index + 1].Payload!);
            if (!BasicBootstrapExecutor.RequiresMountedLoadSemantics(
                    machine,
                    BasicProgramStart,
                    firstHeader.ProgramLength,
                    firstHeader.AutoStartLine))
            {
                return false;
            }

            index += 2;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                return false;

            TapHeaderInfo nextHeader = ParseHeaderInfo(blocks[index]);
            if (nextHeader.Type != ProgramType)
                return false;

            return !CanBootstrapLoadedBasicProgram(machine, nextHeader, blocks[index + 1].Payload!, use128kMode);
        }

        private static bool HasNonContiguousBasicProgramStage(
            IReadOnlyList<TapeBlock> blocks,
            int index)
        {
            bool sawNonBasicPair = false;
            while (index < blocks.Count)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index + 1 >= blocks.Count ||
                    !IsStandardHeaderBlock(blocks[index]) ||
                    blocks[index + 1].Flag != DataFlag)
                {
                    return false;
                }

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                if (header.Type == ProgramType)
                {
                    if (sawNonBasicPair)
                        return true;
                }
                else
                {
                    sawNonBasicPair = true;
                }

                index += 2;
            }

            return false;
        }

        internal static bool RequiresRomDrivenBootstrapForMixedTape(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (blocks == null || blocks.Count == 0)
                return false;

            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                return false;

            TapHeaderInfo firstHeader = ParseHeaderInfo(blocks[index]);
            if (firstHeader.Type != ProgramType || firstHeader.AutoStartLine >= 32768)
                return false;

            index += 2;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                return false;

            TapHeaderInfo nextHeader = ParseHeaderInfo(blocks[index]);
            if (nextHeader.Type != ProgramType)
                return false;

            bool use128kMode = Use128kTapeLoadMode(machine);
            return !CanBootstrapLoadedBasicProgram(machine, nextHeader, blocks[index + 1].Payload!, use128kMode);
        }

        internal static bool RequiresLeadingStandardBasicChainFakeLoad(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (blocks == null || blocks.Count == 0)
                return false;

            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                return false;

            TapHeaderInfo firstHeader = ParseHeaderInfo(blocks[index]);
            if (firstHeader.Type != ProgramType || firstHeader.AutoStartLine >= 32768)
                return false;

            bool use128kMode = Use128kTapeLoadMode(machine);
            InitializeMachineForFakeTapeLoad(machine, use128kMode);
            LoadBasicProgram(machine, firstHeader, blocks[index + 1].Payload!);
            bool firstStageBootstrapSafe = CanBootstrapLoadedBasicProgram(
                machine,
                firstHeader,
                blocks[index + 1].Payload!,
                use128kMode);
            if (!firstStageBootstrapSafe &&
                !BasicBootstrapExecutor.RequiresMountedLoadSemantics(
                    machine,
                    BasicProgramStart,
                    firstHeader.ProgramLength,
                    firstHeader.AutoStartLine))
            {
                return false;
            }

            index += 2;
            int programCount = 1;
            while (index + 1 < blocks.Count)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                    break;

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                if (header.Type != ProgramType)
                    break;

                if (!CanBootstrapLoadedBasicProgram(machine, header, blocks[index + 1].Payload!, use128kMode))
                    return false;

                programCount++;
                index += 2;
            }

            return programCount > 1;
        }

        internal static bool RequiresMountedRealtimeForMixedTape(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (blocks == null || blocks.Count == 0)
                return false;

            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                return false;

            TapHeaderInfo firstHeader = ParseHeaderInfo(blocks[index]);
            if (firstHeader.Type != ProgramType || firstHeader.AutoStartLine >= 32768)
                return false;

            index += 2;
            bool sawRawStandardData = false;

            while (index < blocks.Count)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index >= blocks.Count)
                    break;

                TapeBlock block = blocks[index];
                if (IsStandardHeaderBlock(block))
                {
                    if (index + 1 >= blocks.Count ||
                        blocks[index + 1].Kind != TapeBlockKind.Data ||
                        blocks[index + 1].Flag != DataFlag)
                    {
                        return false;
                    }

                    index += 2;
                    continue;
                }

                if (block.Kind != TapeBlockKind.Data)
                    return false;

                sawRawStandardData = true;
                index++;
            }

            return sawRawStandardData;
        }

        internal static bool HasStandardHeaderDataPairAfterLeadingBasic(IReadOnlyList<TapeBlock> blocks)
        {
            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count)
                return false;

            index += 2;
            while (index < blocks.Count)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index >= blocks.Count)
                    break;

                if (!IsStandardHeaderBlock(blocks[index]))
                {
                    index++;
                    continue;
                }

                return index + 1 < blocks.Count &&
                       blocks[index + 1].Kind == TapeBlockKind.Data &&
                       blocks[index + 1].Flag == DataFlag;
            }

            return false;
        }

        internal static bool CanResolveMountedLoadUsrContinuation(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks)
        {
            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count ||
                !IsStandardHeaderBlock(blocks[index]) ||
                blocks[index + 1].Flag != DataFlag)
            {
                return false;
            }

            TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
            if (header.Type != ProgramType || header.AutoStartLine >= 32768)
                return false;

            bool use128kMode = Use128kTapeLoadMode(machine);
            InitializeMachineForFakeTapeLoad(machine, use128kMode);
            LoadBasicProgram(machine, header, blocks[index + 1].Payload!);
            return BasicBootstrapExecutor.CreateMountedLoadUsrContinuationResolver(
                machine,
                BasicProgramStart,
                header.ProgramLength,
                header.AutoStartLine,
                requireUsrReturnAddressBetweenSteps: false) != null;
        }

        private static TapBootstrapResult LoadLeadingStandardBasicChainAndMountRemaining(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            bool skipCustomHeaderForEarPlayback,
            int nonRomTimingDivisor = 1,
            int loadableTimingDivisor = 1,
            bool initialEarLevelHigh = true)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (blocks == null)
                throw new ArgumentNullException(nameof(blocks));

            int prefixEndIndex = FindLeadingStandardBasicChainEndIndex(blocks);
            if (prefixEndIndex <= 0)
                throw new InvalidOperationException("The tape does not contain a safe leading standard BASIC chain.");

            bool use128kMode = Use128kTapeLoadMode(machine);
            InitializeMachineForFakeTapeLoad(machine, use128kMode);

            int consumedBlockCount = 0;
            ulong consumedTStates = 0;
            while (consumedBlockCount < blocks.Count && blocks[consumedBlockCount].Kind == TapeBlockKind.Metadata)
            {
                consumedTStates += EstimateTapeBlockDurationTStates(blocks[consumedBlockCount]);
                consumedBlockCount++;
            }

            if (consumedBlockCount + 1 >= blocks.Count)
                throw new InvalidOperationException("The tape image does not contain a complete leading BASIC header/data pair.");

            TapeBlock headerBlock = blocks[consumedBlockCount];
            TapeBlock dataBlock = blocks[consumedBlockCount + 1];
            if (!IsStandardHeaderBlock(headerBlock) || dataBlock.Flag != DataFlag)
                throw new InvalidOperationException("The tape image does not begin with a standard BASIC header/data pair.");

            TapHeaderInfo header = ParseHeaderInfo(headerBlock);
            if (header.Type != ProgramType)
                throw new InvalidOperationException($"The leading tape header must be BASIC, but was type {header.Type}.");

            ushort effectiveProgramLength = (ushort)Math.Min(header.ProgramLength, dataBlock.Payload!.Length);
            var effectiveHeader = new TapHeaderInfo(
                header.Type,
                header.FileName,
                (ushort)dataBlock.Payload!.Length,
                header.AutoStartLine,
                effectiveProgramLength);

            LoadBasicProgram(machine, effectiveHeader, dataBlock.Payload!);
            consumedTStates += EstimateTapeBlockDurationTStates(headerBlock);
            consumedTStates += EstimateTapeBlockDurationTStates(dataBlock);
            consumedBlockCount += 2;
            AdvanceBootstrapTapeTime(machine, consumedTStates);

            if (consumedBlockCount < prefixEndIndex)
            {
                var prefixTape = new MountedTape(
                    displayName,
                    blocks,
                    initialBlockIndex: consumedBlockCount,
                    skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback,
                    nonRomTimingDivisor: 1,
                    loadableTimingDivisor: loadableTimingDivisor,
                    initialEarLevelHigh: initialEarLevelHigh);
                machine.MountTape(prefixTape);
            }

            if (effectiveHeader.AutoStartLine < 32768)
            {
                ExecuteBootstrapBasicAutoStart(
                    machine,
                    BasicProgramStart,
                    effectiveProgramLength,
                    effectiveHeader.AutoStartLine,
                    ignoreLoadStatements: false);
            }

            if (prefixEndIndex < blocks.Count)
            {
                var playbackTape = new MountedTape(
                    displayName,
                    blocks,
                    initialBlockIndex: prefixEndIndex,
                    skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback,
                    nonRomTimingDivisor: nonRomTimingDivisor,
                    loadableTimingDivisor: loadableTimingDivisor,
                    initialEarLevelHigh: initialEarLevelHigh);
                machine.MountTape(playbackTape);
            }
            else
            {
                machine.EjectTape();
            }

            int currentPhase = (int)(machine.Cpu.TStates % (ulong)machine.FrameTStates);
            machine.SetSnapshotResumeFramePhase(currentPhase);
            if (currentPhase == 0)
                machine.SetInitialInterruptDelay(DefaultTapeAutoStartInitialInterruptDelay);

            return new TapBootstrapResult(blocks.Count, prefixEndIndex, displayName, effectiveHeader.FileName);
        }

        internal static int FindLeadingStandardBasicChainEndIndex(IReadOnlyList<TapeBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0)
                return 0;

            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                return 0;

            TapHeaderInfo firstHeader = ParseHeaderInfo(blocks[index]);
            if (firstHeader.Type != ProgramType || firstHeader.AutoStartLine >= 32768)
                return 0;

            index += 2;
            bool sawAdditionalProgram = false;
            while (true)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                    break;

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                if (header.Type != ProgramType)
                    break;

                sawAdditionalProgram = true;
                index += 2;
            }

            return sawAdditionalProgram ? index : 0;
        }

        internal static bool RequiresMountedLoadSemanticsForStandardTape(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks,
            bool use128kMode)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            for (int index = 0; index + 1 < blocks.Count; index++)
            {
                if (!IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                    continue;

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                if (header.Type != ProgramType || header.AutoStartLine >= 32768)
                    continue;

                InitializeMachineForFakeTapeLoad(machine, use128kMode);
                LoadBasicProgram(machine, header, blocks[index + 1].Payload!);
                return BasicBootstrapExecutor.RequiresMountedLoadSemantics(
                    machine,
                    BasicProgramStart,
                    header.ProgramLength,
                    header.AutoStartLine);
            }

            return false;
        }

        internal static bool CanExecuteLeadingBasicProgramWithBootstrap(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks,
            bool use128kMode)
        {
            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count ||
                !IsStandardHeaderBlock(blocks[index]) ||
                blocks[index + 1].Flag != DataFlag ||
                blocks[index + 1].Payload == null)
            {
                return false;
            }

            TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
            if (header.Type != ProgramType || header.AutoStartLine >= 32768)
                return false;

            return CanBootstrapLoadedBasicProgram(
                machine,
                header,
                blocks[index + 1].Payload!,
                use128kMode);
        }

        internal static bool CanSatisfyLeadingBasicLoadRequestsBeforeProtectedRemainder(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks,
            bool use128kMode)
        {
            int index = 0;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (index + 1 >= blocks.Count ||
                !IsStandardHeaderBlock(blocks[index]) ||
                blocks[index + 1].Flag != DataFlag ||
                blocks[index + 1].Payload == null)
            {
                return false;
            }

            TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
            if (header.Type != ProgramType || header.AutoStartLine >= 32768)
                return false;

            // Chained BASIC stages are adopted dynamically by the bootstrap executor;
            // their LOAD requirements cannot be inferred from the first program alone.
            if (FindLeadingStandardBasicChainEndIndex(blocks) > 0)
                return true;

            InitializeMachineForFakeTapeLoad(machine, use128kMode);
            LoadBasicProgram(machine, header, blocks[index + 1].Payload!);

            int requiredLoadCount = ExecuteBootstrapBasicAutoStart(
                machine,
                BasicProgramStart,
                header.ProgramLength,
                header.AutoStartLine,
                ignoreLoadStatements: true).IgnoredLoadCount;
            int availableLoadCount = CountBootstrapConsumableDataBlocks(blocks, index + 2);
            return requiredLoadCount <= availableLoadCount;
        }

        private static int CountBootstrapConsumableDataBlocks(IReadOnlyList<TapeBlock> blocks, int startIndex)
        {
            int count = 0;
            for (int index = startIndex; index < blocks.Count; index++)
            {
                TapeBlock block = blocks[index];
                if (block.Kind == TapeBlockKind.StopTape)
                    break;

                if (block.CanUseRomLoadTrap && block.Flag == DataFlag)
                    count++;
            }

            return count;
        }

        private static bool CanBootstrapLoadedBasicProgram(
            Spectrum128Machine machine,
            TapHeaderInfo header,
            byte[] payload,
            bool use128kMode)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            InitializeMachineForFakeTapeLoad(machine, use128kMode);
            LoadBasicProgram(machine, header, payload);
            bool canHandle = BasicBootstrapExecutor.CanHandleLoadedProgram(
                machine,
                BasicProgramStart,
                header.ProgramLength,
                header.AutoStartLine);
            if (!canHandle)
                return false;

            bool requiresProtectedInterpreterHandoff = BasicBootstrapExecutor.RequiresRomDrivenMountedLoadedProgram(
                machine,
                BasicProgramStart,
                header.ProgramLength,
                header.AutoStartLine);
            if (requiresProtectedInterpreterHandoff)
                return false;

            return true;
        }

        internal static TapBootstrapResult LoadLeadingBasicProgramAndMountRemainingForRomAutoStart(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            bool skipCustomHeaderForEarPlayback = true,
            int nonRomTimingDivisor = 1,
            int loadableTimingDivisor = 1,
            bool initialEarLevelHigh = true,
            bool allowRomLoadTraps = true)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (blocks == null)
                throw new ArgumentNullException(nameof(blocks));
            if (blocks.Count == 0)
                throw new InvalidOperationException("The tape image does not contain any blocks.");

            bool use128kMode = Use128kTapeLoadMode(machine);
            InitializeMachineForRomDrivenTapeLoad(machine, use128kMode);

            int consumedBlockCount = 0;
            while (consumedBlockCount < blocks.Count && blocks[consumedBlockCount].Kind == TapeBlockKind.Metadata)
                consumedBlockCount++;

            if (consumedBlockCount + 1 >= blocks.Count)
                throw new InvalidOperationException("The tape image does not contain a complete leading BASIC header/data pair.");

            TapeBlock headerBlock = blocks[consumedBlockCount];
            TapeBlock dataBlock = blocks[consumedBlockCount + 1];

            if (!IsStandardHeaderBlock(headerBlock) || dataBlock.Flag != DataFlag)
                throw new InvalidOperationException("The tape image does not begin with a standard BASIC header/data pair.");

            TapHeaderInfo header = ParseHeaderInfo(headerBlock);
            if (header.Type != ProgramType)
                throw new InvalidOperationException($"The leading tape header must be BASIC, but was type {header.Type}.");

            LoadBasicProgram(machine, header, dataBlock.Payload!);
            consumedBlockCount += 2;

            bool rawStandardMixedRemainder = RequiresMountedRealtimeForMixedTape(machine, blocks);
            if (rawStandardMixedRemainder)
            {
                Func<Spectrum128Machine, ushort?>? mountedLoadUsrContinuationResolver =
                    BasicBootstrapExecutor.CreateMountedLoadUsrContinuationResolver(
                        machine,
                        BasicProgramStart,
                        header.ProgramLength,
                        header.AutoStartLine,
                        requireUsrReturnAddressBetweenSteps: false);
                if (mountedLoadUsrContinuationResolver != null)
                {
                    machine.SetPendingMountedLoadUsrContinuationResolver(
                        mountedLoadUsrContinuationResolver);
                }
                else
                {
                    machine.ClearPendingMountedLoadUsrContinuation();
                }
            }
            else
            {
                Func<Spectrum128Machine, ushort?>? mountedLoadUsrContinuationResolver =
                    BasicBootstrapExecutor.CreateMountedLoadUsrContinuationResolver(
                        machine,
                        BasicProgramStart,
                        header.ProgramLength,
                        header.AutoStartLine);
                if (mountedLoadUsrContinuationResolver != null)
                    machine.SetPendingMountedLoadUsrContinuationResolver(mountedLoadUsrContinuationResolver);
                else
                {
                    machine.ClearPendingMountedLoadUsrContinuation();
                }
            }

            if (consumedBlockCount < blocks.Count)
            {
                var tape = new MountedTape(
                    displayName,
                    blocks,
                    initialBlockIndex: consumedBlockCount,
                    skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback,
                    initialPrePlaybackPauseTStates: dataBlock.PauseAfterBlockMs * TStatesPerMillisecond48k,
                    nonRomTimingDivisor: nonRomTimingDivisor,
                    loadableTimingDivisor: loadableTimingDivisor,
                    initialEarLevelHigh: initialEarLevelHigh,
                    allowRomLoadTraps: allowRomLoadTraps);
                machine.MountTape(tape);
            }

            if (header.AutoStartLine < 32768)
                StartRomDrivenMountedBasicAutoStart(machine, header.AutoStartLine);

            int currentPhase = (int)(machine.Cpu.TStates % (ulong)machine.FrameTStates);
            machine.SetSnapshotResumeFramePhase(currentPhase);
            if (currentPhase == 0)
                machine.SetInitialInterruptDelay(DefaultTapeAutoStartInitialInterruptDelay);

            return new TapBootstrapResult(blocks.Count, consumedBlockCount, displayName, header.FileName);
        }

        private static bool Requires128kTapeLoadModeForStandardTape(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            for (int index = 0; index + 1 < blocks.Count; index++)
            {
                if (!IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                    continue;

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                if (header.Type != ProgramType || header.AutoStartLine >= 32768)
                    continue;

                try
                {
                    InitializeMachineForFakeTapeLoad(machine, use128kMode: false);
                    LoadBasicProgram(machine, header, blocks[index + 1].Payload!);
                    if (BasicBootstrapExecutor.Requires128kTapeLoadMode(
                        machine,
                        BasicProgramStart,
                        header.ProgramLength,
                        header.AutoStartLine))
                    {
                        return true;
                    }
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
            }

            // Protected streams can contain their own machine-mode probe after a
            // generic BASIC USR handoff, so keep the native 128K configuration.
            return ContainsElectricallyDecodedProtectedStream(blocks);
        }

        internal static bool Use128kTapeLoadMode(Spectrum128Machine machine)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            return machine.MachineModel == SpectrumMachineModel.Spectrum128K;
        }

        internal static void StartRomDrivenMountedBasicAutoStart(
            Spectrum128Machine machine,
            ushort autoStartLine)
        {
            WriteWord(machine, NewPpcAddress, autoStartLine);
            machine.PokeMemory((ushort)(NewPpcAddress + 2), 0);
            WriteWord(machine, PpcAddress, autoStartLine);
            machine.PokeMemory(SubPpcAddress, 0);

            // Enter the same interpreter path used after LOAD has selected an
            // autostart line, with an error return frame for MAIN-4.
            ushort errorStackPointer = ReadWord(machine, ErrorStackPointerAddress);
            if (errorStackPointer < 0x4000 || errorStackPointer == 0xFFFF)
                throw new InvalidOperationException("The Spectrum ROM did not initialize a valid BASIC error stack.");

            WriteWord(machine, errorStackPointer, machine.RomProfile.MainExecutionReportAddress);
            machine.Cpu.Regs.SP = errorStackPointer;
            machine.Cpu.Regs.HL = autoStartLine;
            machine.Cpu.Regs.PC = machine.RomProfile.BasicLineNewAddress;
            // The normal ROM tape return executes EI before returning to BASIC.
            // This direct handoff bypasses that path, so preserve its interrupt state.
            machine.Cpu.RestoreInterruptState(true, true, machine.Cpu.InterruptMode);
            machine.PokeMemory(ErrorNumberSystemVariableAddress, 0xFF);
            machine.PokeMemory(
                FlagsSystemVariableAddress,
                (byte)(machine.PeekMemory(FlagsSystemVariableAddress) | 0x80));
        }

        internal static bool CanStartRomDrivenMountedBasicAutoStart(Spectrum128Machine machine)
        {
            ushort errorStackPointer = ReadWord(machine, ErrorStackPointerAddress);
            return errorStackPointer >= 0x4000 && errorStackPointer != 0xFFFF;
        }

        private static void InitializeMachineForRomDrivenTapeLoad(
            Spectrum128Machine machine,
            bool use128kMode)
        {
            machine.Reset();
            if (use128kMode)
                machine.ConfigureFor128kTapeLoad(borderColor: 0);
            else
                machine.ConfigureFor48kTapeLoad(borderColor: 0);

            machine.Cpu.StopBeforeInstruction = cpu =>
                cpu.Regs.PC == machine.RomProfile.KeyboardInputLoopAddress;
            try
            {
                for (int frame = 0; frame < RomInitializationFrameLimit && !machine.Cpu.ExecutionStopped; frame++)
                    machine.ExecuteTimeSlice(machine.FrameTStates);
            }
            finally
            {
                machine.Cpu.StopBeforeInstruction = null;
            }

            if (!machine.Cpu.ExecutionStopped)
                throw new InvalidOperationException("The Spectrum ROM did not reach the BASIC input loop during tape initialization.");

            while (machine.TryDequeueCompletedAudioFrame(out _))
            {
            }

            machine.ClearLogs();
            machine.ClearKeyboard();
        }

        private static TapBootstrapResult StartNative128kRomTapeLoader(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            int nonRomTimingDivisor,
            int loadableTimingDivisor,
            bool initialEarLevelHigh)
        {
            machine.Reset(SpectrumMachineModel.Spectrum128K);

            for (int frame = 0;
                 frame < RomInitializationFrameLimit &&
                 machine.PeekMemory(Spectrum128RamRstAddress) != Spectrum128RamRstOpcode;
                 frame++)
            {
                machine.ExecuteTimeSlice(machine.FrameTStates);
            }

            if (machine.PeekMemory(Spectrum128RamRstAddress) != Spectrum128RamRstOpcode)
            {
                throw new InvalidOperationException(
                    "The Spectrum 128 ROM did not initialize its paging workspace.");
            }

            // Allow the menu to settle on its default Tape Loader entry before
            // mounting. Starting playback here preserves the complete tape while
            // avoiding consumption during ROM startup.
            for (int frame = 0; frame < 30; frame++)
                machine.ExecuteTimeSlice(machine.FrameTStates);

            var tape = new MountedTape(
                displayName,
                blocks,
                initialBlockIndex: 0,
                skipCustomHeaderForEarPlayback: false,
                nonRomTimingDivisor: nonRomTimingDivisor,
                loadableTimingDivisor: loadableTimingDivisor,
                initialEarLevelHigh: initialEarLevelHigh,
                allowRomLoadTraps: false);
            machine.MountTape(tape);

            machine.SetKey(6, 0, true);
            for (int frame = 0; frame < 10; frame++)
                machine.ExecuteTimeSlice(machine.FrameTStates);
            machine.SetKey(6, 0, false);

            bool enteredTapeLoader = false;
            for (int frame = 0; frame < 100; frame++)
            {
                machine.ExecuteTimeSlice(machine.FrameTStates);
                if (machine.CurrentRomBank == 1 &&
                    machine.PagedRamBank == 0 &&
                    (machine.PeekMemory(FlagsSystemVariableAddress) & 0x10) != 0)
                {
                    enteredTapeLoader = true;
                    break;
                }
            }

            machine.ClearKeyboard();
            if (!enteredTapeLoader)
                throw new InvalidOperationException("The Spectrum 128 ROM did not enter its Tape Loader.");

            string? autoStartFileName = null;
            foreach (TapeBlock block in blocks)
            {
                if (!IsStandardHeaderBlock(block))
                    continue;

                TapHeaderInfo header = ParseHeaderInfo(block);
                if (header.Type == ProgramType)
                    autoStartFileName = header.FileName;
                break;
            }

            return new TapBootstrapResult(
                blocks.Count,
                0,
                displayName,
                autoStartFileName);
        }

        private static int FindFirstCustomHeaderBlockIndex(IReadOnlyList<TapeBlock> blocks)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                TapeBlock block = blocks[i];
                if (!IsStandardHeaderBlock(block))
                    continue;

                TapHeaderInfo header = ParseHeaderInfo(block);
                if (header.Type > CodeType)
                    return i;
            }

            return -1;
        }

        private static int SkipSatisfiedStandardLoads(IReadOnlyList<TapeBlock> blocks, int startIndex, int satisfiedLoadCount)
        {
            int index = startIndex;
            for (int load = 0; load < satisfiedLoadCount; load++)
            {
                while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                    index++;

                if (index + 1 >= blocks.Count || !IsStandardHeaderBlock(blocks[index]) || blocks[index + 1].Flag != DataFlag)
                    break;

                index += 2;
            }

            return index;
        }

        internal static void AdvanceBootstrapTapeTime(Spectrum128Machine machine, ulong consumedTStates)
        {
            if (consumedTStates == 0)
                return;

            while (consumedTStates > 0)
            {
                uint step = consumedTStates > uint.MaxValue ? uint.MaxValue : (uint)consumedTStates;
                machine.Cpu.AdvanceTStates(step);
                consumedTStates -= step;
            }

            int currentPhase = (int)(machine.Cpu.TStates % (ulong)machine.FrameTStates);
            machine.SetSnapshotResumeFramePhase(currentPhase);
        }

        private static void LogMountedTape(MountedTape tape, IReadOnlyList<TapeBlock> blocks)
        {
            Console.WriteLine($"[TAP] Mounted '{tape.DisplayName}' with {blocks.Count} blocks.");
            for (int i = 0; i < blocks.Count; i++)
            {
                TapeBlock block = blocks[i];
                if (block.Kind != TapeBlockKind.Data)
                {
                    Console.WriteLine($"[TAP] Block {i}: {block.Kind}");
                }
                else if (IsStandardHeaderBlock(block))
                {
                    TapHeaderInfo header = ParseHeaderInfo(block);
                    Console.WriteLine($"[TAP] Block {i}: HEADER {GetHeaderTypeName(header.Type)} '{header.FileName}' len={header.DataLength}");
                }
                else
                {
                    Console.WriteLine(
                        $"[TAP] Block {i}: DATA flag=0x{block.Flag:X2} " +
                        $"payloadLen={block.Payload?.Length ?? -1} streamLen={block.StreamByteCount} " +
                        $"usedBits={block.UsedBitsInLastByte} pause={block.PauseAfterBlockMs}");
                }
            }
        }

        private static string GetHeaderTypeName(byte type)
        {
            return type switch
            {
                ProgramType => "BASIC",
                NumberArrayType => "NUMARRAY",
                CharacterArrayType => "CHARARRAY",
                CodeType => "CODE",
                _ => $"TYPE{type}"
            };
        }

        internal static void InitializeMachineForFakeTapeLoad(Spectrum128Machine machine, bool use128kMode)
        {
            machine.Reset();
            if (use128kMode)
                machine.ConfigureFor128kTapeLoad(borderColor: 0);
            else
                machine.ConfigureFor48kTapeLoad(borderColor: 0);

            machine.Cpu.Regs.PC = machine.RomProfile.TapeAutoStartExecutionLoopAddress;
            machine.Cpu.Regs.SP = DefaultStackPointer;
            machine.Cpu.Regs.IY = RomSystemVariablesBase;
            machine.Cpu.Regs.IX = RomSystemVariablesBase;
            machine.Cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
            machine.Cpu.ClearSnapshotExecutionState();
            machine.ClearLogs();
            machine.ClearKeyboard();
            InitializeRomChannelsAndStreams(machine);

            WriteWord(machine, ProgAddress, BasicProgramStart);
            WriteWord(machine, VarsAddress, BasicProgramStart);
            WriteWord(machine, NextLineAddress, BasicProgramStart);
            WriteWord(machine, DataAddress, BasicProgramStart);
            WriteWord(machine, EditLineAddress, BasicProgramStart);
            WriteWord(machine, WorkspaceAddress, BasicProgramStart);
            WriteWord(machine, StackBottomAddress, BasicProgramStart);
            WriteWord(machine, StackEndAddress, BasicProgramStart);
            WriteWord(machine, RamTopAddress, (ushort)(BasicProgramStart - 1));
            WriteWord(machine, PhysicalRamTopAddress, 0xFFFF);
            WriteWord(machine, NewPpcAddress, 0);
            machine.PokeMemory((ushort)(NewPpcAddress + 2), 0);
            machine.PokeMemory(BorderSystemVariableAddress, 0);
            machine.PokeMemory(BasicProgramStart, 0x0D);
        }

        private static void InitializeRomChannelsAndStreams(Spectrum128Machine machine)
        {
            WriteWord(machine, ChansAddress, InitialChannelsAreaAddress);
            WriteWord(machine, CurChlAddress, KeyboardChannelDescriptorAddress);

            byte[] streamData =
            {
                0x01, 0x00, // stream -3 -> K
                0x06, 0x00, // stream -2 -> S
                0x0B, 0x00, // stream -1 -> R
                0x01, 0x00, // stream  0 -> K
                0x01, 0x00, // stream  1 -> K
                0x06, 0x00, // stream  2 -> S
                0x10, 0x00  // stream  3 -> P
            };

            for (int i = 0; i < streamData.Length; i++)
                machine.PokeMemory((ushort)(StreamsAddress + i), streamData[i]);

            byte[] channelData =
            {
                0xF4, 0x09, 0xA8, 0x10, 0x4B, // K
                0xF4, 0x09, 0xC4, 0x15, 0x53, // S
                0x81, 0x0F, 0xC4, 0x15, 0x52, // R
                0xF4, 0x09, 0xC4, 0x15, 0x50, // P
                0x80
            };

            for (int i = 0; i < channelData.Length; i++)
                machine.PokeMemory((ushort)(InitialChannelsAreaAddress + i), channelData[i]);
        }

        internal static void LoadDataBlock(Spectrum128Machine machine, TapHeaderInfo header, byte[] payload)
        {
            switch (header.Type)
            {
                case ProgramType:
                    LoadBasicProgram(machine, header, payload);
                    break;

                case CodeType:
                    LoadBytes(machine, header.StartAddress, payload);
                    break;

                case NumberArrayType:
                case CharacterArrayType:
                    throw new NotSupportedException("Standard ROM-saved array blocks are not wired into the fake tape loader yet.");

                default:
                    LoadBytes(machine, header.StartAddress, payload);
                    break;
            }
        }

        internal static void LoadBasicProgram(
            Spectrum128Machine machine,
            TapHeaderInfo header,
            byte[] payload,
            bool preserveInterpreterWorkspace = false)
        {
            ushort savedVars = 0;
            ushort savedCurChl = 0;
            ushort savedKCur = 0;
            ushort savedChAdd = 0;
            ushort savedXPtr = 0;
            ushort savedEditLine = 0;
            ushort savedWorkspace = 0;
            ushort savedStackBottom = 0;
            ushort savedStackEnd = 0;
            if (preserveInterpreterWorkspace)
            {
                savedVars = ReadWord(machine, VarsAddress);
                savedCurChl = ReadWord(machine, CurChlAddress);
                savedKCur = ReadWord(machine, KCurAddress);
                savedChAdd = ReadWord(machine, ChAddAddress);
                savedXPtr = ReadWord(machine, XPtrAddress);
                savedEditLine = ReadWord(machine, EditLineAddress);
                savedWorkspace = ReadWord(machine, WorkspaceAddress);
                savedStackBottom = ReadWord(machine, StackBottomAddress);
                savedStackEnd = ReadWord(machine, StackEndAddress);
            }

            ushort programStart = BasicProgramStart;
            ushort programLength = header.ProgramLength;
            if (programLength > payload.Length)
            {
                throw new InvalidOperationException(
                    $"BASIC header for '{header.FileName}' declares a program length of {programLength} bytes, but the data block only contains {payload.Length} bytes.");
            }

            if (((int)programStart + payload.Length + 2) > 0x10000)
                throw new InvalidOperationException("The BASIC program, variables, and interpreter terminators do not fit in 48K RAM.");

            LoadBytes(machine, programStart, payload);

            ushort varsAddress = (ushort)(programStart + programLength);
            ushort variablesEndAddress = (ushort)(programStart + payload.Length);
            ushort editLineAddress = (ushort)(variablesEndAddress + 1);
            ushort workspaceAddress = (ushort)(editLineAddress + 1);

            // Tape files omit the in-memory variables terminator and edit line.
            machine.PokeMemory(variablesEndAddress, 0x80);
            machine.PokeMemory(editLineAddress, 0x0D);

            WriteWord(machine, ProgAddress, programStart);
            WriteWord(machine, VarsAddress, varsAddress);
            WriteWord(machine, NextLineAddress, programStart);
            WriteWord(machine, DataAddress, programStart);
            if (preserveInterpreterWorkspace)
            {
                WriteWord(machine, VarsAddress, savedVars);
                WriteWord(machine, CurChlAddress, savedCurChl);
                WriteWord(machine, KCurAddress, savedKCur);
                WriteWord(machine, ChAddAddress, savedChAdd);
                WriteWord(machine, XPtrAddress, savedXPtr);
                WriteWord(machine, EditLineAddress, savedEditLine);
                WriteWord(machine, WorkspaceAddress, savedWorkspace);
                WriteWord(machine, StackBottomAddress, savedStackBottom);
                WriteWord(machine, StackEndAddress, savedStackEnd);
            }
            else
            {
                WriteWord(machine, EditLineAddress, editLineAddress);
                WriteWord(machine, WorkspaceAddress, workspaceAddress);
                WriteWord(machine, StackBottomAddress, workspaceAddress);
                WriteWord(machine, StackEndAddress, workspaceAddress);
                InitializeInterpreterPointersForLoadedProgram(machine, editLineAddress);
            }

            if (header.AutoStartLine < 32768)
            {
                WriteWord(machine, NewPpcAddress, header.AutoStartLine);
                machine.PokeMemory((ushort)(NewPpcAddress + 2), 0);
            }
        }

        internal static void LoadBasicProgram(Spectrum128Machine machine, TapHeaderInfo header, byte[] payload)
        {
            LoadBasicProgram(machine, header, payload, preserveInterpreterWorkspace: false);
        }

        private static void InitializeInterpreterPointersForLoadedProgram(Spectrum128Machine machine, ushort editLineAddress)
        {
            WriteWord(machine, CurChlAddress, KeyboardChannelDescriptorAddress);
            WriteWord(machine, KCurAddress, editLineAddress);
            WriteWord(machine, ChAddAddress, editLineAddress);
            WriteWord(machine, XPtrAddress, 0);
        }

        internal static void LoadBytes(Spectrum128Machine machine, ushort startAddress, byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));
            if (startAddress < 0x4000)
                throw new InvalidOperationException($"Cannot fake-load tape data into ROM at 0x{startAddress:X4}.");
            if (((int)startAddress + payload.Length) > 0x10000)
            {
                throw new InvalidOperationException(
                    $"Tape block at 0x{startAddress:X4} with length {payload.Length} extends past 0xFFFF.");
            }

            ushort address = startAddress;
            for (int i = 0; i < payload.Length; i++, address++)
                machine.PokeMemory(address, payload[i]);
        }

        internal static TapHeaderInfo ParseHeaderInfo(TapeBlock block)
        {
            if (block.Payload == null || block.Payload.Length != TapHeaderPayloadLength)
            {
                throw new InvalidOperationException(
                    $"Tape header blocks must contain exactly {TapHeaderPayloadLength} payload bytes, but got {block.Payload?.Length ?? 0}.");
            }

            byte type = block.Payload[0];
            string fileName = Encoding.ASCII.GetString(block.Payload, 1, 10).TrimEnd();
            ushort dataLength = ReadWord(block.Payload, 11);
            ushort parameter1 = ReadWord(block.Payload, 13);
            ushort parameter2 = ReadWord(block.Payload, 15);

            return new TapHeaderInfo(type, fileName, dataLength, parameter1, parameter2);
        }

        private static void ValidateChecksum(byte[] data, int offset, int length)
        {
            byte xor = 0;
            for (int i = 0; i < length; i++)
                xor ^= data[offset + i];

            if (xor != 0)
                throw new InvalidOperationException("Tape block checksum mismatch.");
        }

        private static ushort ReadWord(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        internal static bool IsSupportedRomHeaderType(byte type)
        {
            return type == ProgramType || type == NumberArrayType || type == CharacterArrayType || type == CodeType;
        }

        internal static bool IsStandardHeaderBlock(TapeBlock block)
        {
            return block.Flag == HeaderFlag && block.Payload != null && block.Payload.Length == TapHeaderPayloadLength;
        }

        internal static bool HasInconsistentStandardBasicHeaderDataPair(IReadOnlyList<TapeBlock> blocks)
        {
            for (int index = 0; index + 1 < blocks.Count; index++)
            {
                if (!IsStandardHeaderBlock(blocks[index]))
                    continue;

                TapeBlock dataBlock = blocks[index + 1];
                if (dataBlock.Flag != DataFlag || dataBlock.Payload == null)
                    continue;

                TapHeaderInfo header = ParseHeaderInfo(blocks[index]);
                if (header.Type == ProgramType && header.ProgramLength > dataBlock.Payload.Length)
                    return true;
            }

            return false;
        }

        private static BootstrapExecutionResult ExecuteBootstrapBasicAutoStart(
            Spectrum128Machine machine,
            ushort programStart,
            ushort programLength,
            ushort autoStartLine,
            bool ignoreLoadStatements = false)
        {
            if (programLength == 0)
                return BootstrapExecutionResult.None;

            var executor = new BasicBootstrapExecutor(machine, programStart, programLength, ignoreLoadStatements);
            executor.Execute(autoStartLine);
            return new BootstrapExecutionResult(executor.IgnoredLoadCount, executor.ConsumedMountedLoadCount);
        }

        internal static bool TryExecuteLoadedMountedBasicProgram(
            Spectrum128Machine machine,
            ushort programLength,
            ushort dataLength,
            ushort autoStartLine)
        {
            if (!BasicBootstrapExecutor.CanHandleLoadedProgram(machine, BasicProgramStart, programLength, autoStartLine))
            {
                machine.Trace?.Invoke(
                    $"[MountedLoad] Loaded BASIC program rejected len={programLength} data={dataLength} auto={autoStartLine}");
                return false;
            }

            var executor = new BasicBootstrapExecutor(machine, BasicProgramStart, programLength, ignoreLoadStatements: false);
            bool requiresRomDrivenMountedLoadedProgram =
                BasicBootstrapExecutor.RequiresRomDrivenMountedLoadedProgram(
                    machine,
                    BasicProgramStart,
                    programLength,
                    autoStartLine);
            machine.Trace?.Invoke(
                $"[MountedLoad] Loaded BASIC decision len={programLength} data={dataLength} auto={autoStartLine} requiresRomDriven={(requiresRomDrivenMountedLoadedProgram ? 1 : 0)}");
            if (executor.TryExecuteImmediateSideEffectProgram(programLength, dataLength, autoStartLine))
            {
                machine.Trace?.Invoke(
                    $"[MountedLoad] Immediate side-effect execution completed len={programLength} auto={autoStartLine}");
                return true;
            }

            if (BasicBootstrapExecutor.RequiresMountedLoadSemantics(machine, BasicProgramStart, programLength, autoStartLine))
            {
                machine.Trace?.Invoke(
                    $"[MountedLoad] Loaded BASIC requires mounted-load semantics len={programLength} auto={autoStartLine}");
                Func<Spectrum128Machine, ushort?>? mountedLoadUsrContinuationResolver =
                    BasicBootstrapExecutor.CreateMountedLoadUsrContinuationResolver(
                        machine,
                        BasicProgramStart,
                        programLength,
                        autoStartLine);
                if (mountedLoadUsrContinuationResolver != null)
                {
                    machine.Trace?.Invoke(
                        $"[MountedLoad] Armed mounted-load continuation len={programLength} auto={autoStartLine}");
                    machine.SetPendingMountedLoadUsrContinuationResolver(mountedLoadUsrContinuationResolver);
                }
                else
                {
                    machine.Trace?.Invoke(
                        $"[MountedLoad] Mounted-load continuation resolver was null len={programLength} auto={autoStartLine}");
                }

                return false;
            }

            if (requiresRomDrivenMountedLoadedProgram)
            {
                machine.Trace?.Invoke(
                    $"[MountedLoad] Loaded BASIC requires deferred ROM BASIC continuation len={programLength} auto={autoStartLine}");
                Func<Spectrum128Machine, ushort?>? romBasicContinuationResolver =
                    BasicBootstrapExecutor.CreateMountedLoadRomBasicContinuationResolver(
                        machine,
                        BasicProgramStart,
                        programLength,
                        autoStartLine);
                if (romBasicContinuationResolver != null)
                {
                    machine.Trace?.Invoke(
                        $"[MountedLoad] Armed deferred ROM BASIC continuation len={programLength} auto={autoStartLine}");
                    machine.SetPendingMountedLoadUsrContinuationResolver(romBasicContinuationResolver);
                }
                else
                {
                    machine.Trace?.Invoke(
                        $"[MountedLoad] Deferred ROM BASIC continuation resolver was null len={programLength} auto={autoStartLine}");
                }

                return false;
            }

            ExecuteBootstrapBasicAutoStart(
                machine,
                BasicProgramStart,
                programLength,
                autoStartLine,
                ignoreLoadStatements: false);
            return true;
        }

        internal static bool ShouldReloadStructuredMountedBasicProgramWithoutPreservingInterpreterWorkspace(
            Spectrum128Machine machine,
            ushort programLength,
            ushort autoStartLine)
        {
            if (programLength == 0)
                return false;

            if (BasicBootstrapExecutor.RequiresMountedLoadSemantics(machine, BasicProgramStart, programLength, autoStartLine))
                return true;

            return BasicBootstrapExecutor.RequiresRomDrivenMountedLoadedProgram(
                machine,
                BasicProgramStart,
                programLength,
                autoStartLine);
        }

        internal static bool RequiresRomDrivenStructuredMountedProgram(
            Spectrum128Machine machine,
            TapHeaderInfo header,
            ushort loadedProgramLength)
        {
            return header.Type == ProgramType &&
                   loadedProgramLength != 0 &&
                   BasicBootstrapExecutor.RequiresRomDrivenMountedLoadedProgram(
                       machine,
                       BasicProgramStart,
                       loadedProgramLength,
                       header.AutoStartLine);
        }

        internal static bool RequiresExactRomBasicLoad(TapHeaderInfo header, byte[] payload)
        {
            if (header.Type != ProgramType || header.ProgramLength == 0)
                return false;

            return BasicBootstrapExecutor.RequiresRomDrivenMountedLoadedProgramData(
                payload,
                header.ProgramLength,
                header.AutoStartLine);
        }

        private readonly record struct BootstrapExecutionResult(int IgnoredLoadCount, int ConsumedMountedLoadCount)
        {
            public static readonly BootstrapExecutionResult None = new(0, 0);
        }
    }
}
