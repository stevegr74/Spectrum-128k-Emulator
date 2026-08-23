using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator.Tap
{
    public sealed class TapLoadResult
    {
        public TapLoadResult(int totalBlockCount, int loadedBlockCount, string? autoStartFileName)
        {
            TotalBlockCount = totalBlockCount;
            LoadedBlockCount = loadedBlockCount;
            AutoStartFileName = autoStartFileName;
        }

        public int TotalBlockCount { get; }
        public int LoadedBlockCount { get; }
        public string? AutoStartFileName { get; }
    }

    public sealed class TapMountResult
    {
        public TapMountResult(int totalBlockCount, string displayName)
        {
            TotalBlockCount = totalBlockCount;
            DisplayName = displayName;
        }

        public int TotalBlockCount { get; }
        public string DisplayName { get; }
    }

    public sealed class TapBootstrapResult
    {
        public TapBootstrapResult(int totalBlockCount, int consumedBlockCount, string displayName, string? autoStartFileName)
        {
            TotalBlockCount = totalBlockCount;
            ConsumedBlockCount = consumedBlockCount;
            DisplayName = displayName;
            AutoStartFileName = autoStartFileName;
        }

        public int TotalBlockCount { get; }
        public int ConsumedBlockCount { get; }
        public string DisplayName { get; }
        public string? AutoStartFileName { get; }
    }

        public enum TapeLoadStrategy
        {
            FullFakeLoad,
            LeadingStandardChainFakeLoad,
            RomBootstrapMounted,
            BootstrapHybrid,
            MountedRealtime
        }

    public sealed class TapeLoadPlan
    {
        public TapeLoadPlan(TapeLoadStrategy strategy, string reason)
        {
            Strategy = strategy;
            Reason = reason;
        }

        public TapeLoadStrategy Strategy { get; }
        public string Reason { get; }
    }

    public sealed class TapeExecutionResult
    {
        public TapeExecutionResult(
            TapeLoadStrategy strategy,
            int totalBlockCount,
            int consumedBlockCount,
            string displayName,
            string? autoStartFileName)
        {
            Strategy = strategy;
            TotalBlockCount = totalBlockCount;
            ConsumedBlockCount = consumedBlockCount;
            DisplayName = displayName;
            AutoStartFileName = autoStartFileName;
        }

        public TapeLoadStrategy Strategy { get; }
        public int TotalBlockCount { get; }
        public int ConsumedBlockCount { get; }
        public string DisplayName { get; }
        public string? AutoStartFileName { get; }
    }

    public sealed class BootstrapTapeLoadResult
    {
        public static readonly BootstrapTapeLoadResult None = new(false, false, 0, 0, 0, 0xFFFF);

        public BootstrapTapeLoadResult(
            bool success,
            bool loadedBasicProgram,
            byte loadedHeaderType,
            ushort loadedProgramLength,
            ushort loadedDataLength,
            ushort loadedAutoStartLine)
        {
            Success = success;
            LoadedBasicProgram = loadedBasicProgram;
            LoadedHeaderType = loadedHeaderType;
            LoadedProgramLength = loadedProgramLength;
            LoadedDataLength = loadedDataLength;
            LoadedAutoStartLine = loadedAutoStartLine;
        }

        public bool Success { get; }
        public bool LoadedBasicProgram { get; }
        public byte LoadedHeaderType { get; }
        public ushort LoadedProgramLength { get; }
        public ushort LoadedDataLength { get; }
        public ushort LoadedAutoStartLine { get; }
    }

    public sealed class MountedTape
    {
        private const ushort RomTapeReturnAddress = 0x053F;
        private const ushort RomLoadBytesTrapAddress = 0x056B;
        private const ushort RomLoadBytesSyncLoopAddress = 0x0574;
        private const byte FlagCarry = 0x01;
        private const byte HeaderFlag = 0x00;
        private const byte DataFlag = 0xFF;
        private const int HeaderPayloadLength = 17;
        private const int HeaderPilotPulseCount = 8063;
        private const int DataPilotPulseCount = 3223;
        private const int PilotPulseLengthTStates = 2168;
        private const int SyncFirstPulseLengthTStates = 667;
        private const int SyncSecondPulseLengthTStates = 735;
        private const int ZeroBitPulseLengthTStates = 855;
        private const int OneBitPulseLengthTStates = 1710;
        private readonly IReadOnlyList<TapeBlock> blocks;
        private readonly bool skipCustomHeaderForEarPlayback;
        private readonly int initialBlockIndex;
        private readonly int initialPrePlaybackPauseTStates;
        private readonly int nonRomTimingDivisor;
        private readonly int loadableTimingDivisor;
        private readonly bool initialEarLevelHigh;
        private int nextBlockIndex;
        private int earPlaybackBlockIndex;
        private int earStreamByteIndex;
        private int earBitIndex;
        private int earPulseRepeatCount;
        private int earPilotPulsesRemaining;
        private int earPulseLengthTStates;
        private int earPulseSequenceIndex;
        private int earNextBlockIndexAfterPause;
        private int pendingPrePlaybackPauseTStates;
        private int endOfStreamTransitionTStates;
        private int endOfStreamTransitionTailTStates;
        private int endOfStreamTransitionPhase;
        private int romStreamTrapBlockIndex;
        private int romStreamTrapByteIndex;
        private ulong lastEarSampleTStates;
        private bool earLevel;
        private bool earPlaybackStarted;
        private bool retainedByteStreamTrapAvailable;
        private EarPlaybackState earPlaybackState;
        private TapeState state;
        private int? expectedDataLength;
        private string? pendingHeaderName;
        private TapLoader.TapHeaderInfo? pendingHeaderInfo;

        private enum TapeState
        {
            Idle,
            ExpectHeader,
            ExpectData
        }

        private enum EarPlaybackState
        {
            Idle,
            Pilot,
            SyncFirst,
            SyncSecond,
            Data,
            DirectRecording,
            Pause,
            PulseSequence,
            PureTone,
            EndOfStreamTransition
        }

        public MountedTape(
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            int initialBlockIndex = 0,
            bool skipCustomHeaderForEarPlayback = true,
            int initialPrePlaybackPauseTStates = 0,
            int nonRomTimingDivisor = 1,
            int loadableTimingDivisor = 1,
            bool initialEarLevelHigh = true)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "unnamed.tap" : displayName;
            this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
            this.skipCustomHeaderForEarPlayback = skipCustomHeaderForEarPlayback;
            this.initialPrePlaybackPauseTStates = Math.Max(0, initialPrePlaybackPauseTStates);
            this.nonRomTimingDivisor = Math.Max(1, nonRomTimingDivisor);
            this.loadableTimingDivisor = Math.Max(1, loadableTimingDivisor);
            this.initialEarLevelHigh = initialEarLevelHigh;
            if (initialBlockIndex < 0 || initialBlockIndex > blocks.Count)
                throw new ArgumentOutOfRangeException(nameof(initialBlockIndex));

            this.initialBlockIndex = initialBlockIndex;
            Reset();
        }

        public string DisplayName { get; }
        public bool HasRemainingBlocks => nextBlockIndex < blocks.Count;
        public bool HasMoreBlocks => HasRemainingBlocks;
        public bool IsAtMountedLoadUsrContinuationBoundary =>
            !retainedByteStreamTrapAvailable &&
            pendingPrePlaybackPauseTStates <= 0 &&
            !IsActivelyStreamingEarSignal &&
            !HasPendingRomLoadableBlock() &&
            !HasPendingUnstructuredLoadableStandardDataBlock() &&
            !HasStructuredPendingDataBlock() &&
            (earPlaybackState == EarPlaybackState.Idle || earPlaybackState == EarPlaybackState.Pause);
        public string DebugPlaybackState =>
            $"NextBlock={nextBlockIndex}/{blocks.Count} EarBlock={earPlaybackBlockIndex}/{blocks.Count} " +
            $"State={state} EarState={earPlaybackState} Byte={earStreamByteIndex} Bit={earBitIndex} " +
            $"Pilot={earPilotPulsesRemaining} PulseLen={earPulseLengthTStates} PulseSeq={earPulseSequenceIndex} " +
            $"EarLevel={(earLevel ? 1 : 0)} Started={(earPlaybackStarted ? 1 : 0)} Retained={(retainedByteStreamTrapAvailable ? 1 : 0)} " +
            $"RomTrapBlock={romStreamTrapBlockIndex} RomTrapByte={romStreamTrapByteIndex}";
        public bool IsActivelyDrivingEarLine => earPlaybackState != EarPlaybackState.Idle;
        public bool IsActivelyStreamingEarSignal =>
            earPlaybackState is not EarPlaybackState.Idle
            and not EarPlaybackState.Pause
            and not EarPlaybackState.EndOfStreamTransition;
        public bool IsStreamingProtectedByteStream =>
            TryGetActivePlaybackBlock(out TapeBlock? block) && block != null &&
            (block.Kind == TapeBlockKind.DirectRecording ||
             (block.Kind == TapeBlockKind.Data && !block.CanUseRomLoadTrap));
        public bool IsStreamingRomLoadableStandardData =>
            TryGetActivePlaybackBlock(out TapeBlock? block) && block != null &&
            block.Kind == TapeBlockKind.Data &&
            block.CanUseRomLoadTrap;

        public void Reset()
        {
            nextBlockIndex = initialBlockIndex;
            earPlaybackBlockIndex = initialBlockIndex;
            earStreamByteIndex = 0;
            earBitIndex = 0;
            earPulseRepeatCount = 0;
            earPilotPulsesRemaining = 0;
            earPulseLengthTStates = 0;
            earPulseSequenceIndex = 0;
            earNextBlockIndexAfterPause = 0;
            pendingPrePlaybackPauseTStates = initialPrePlaybackPauseTStates;
            endOfStreamTransitionTStates = 0;
            endOfStreamTransitionTailTStates = 0;
            endOfStreamTransitionPhase = 0;
            romStreamTrapBlockIndex = -1;
            romStreamTrapByteIndex = 0;
            lastEarSampleTStates = 0;
            earLevel = initialEarLevelHigh;
            earPlaybackStarted = false;
            retainedByteStreamTrapAvailable = false;
            earPlaybackState = EarPlaybackState.Idle;
            expectedDataLength = null;
            pendingHeaderName = null;
            pendingHeaderInfo = null;

            if (blocks.Count == 0 || initialBlockIndex >= blocks.Count)
            {
                state = TapeState.Idle;
                return;
            }

            int firstLoadableBlockIndex = FindNextLoadableBlockIndex(initialBlockIndex);
            state = firstLoadableBlockIndex >= blocks.Count
                ? TapeState.Idle
                : IsHeaderBlock(blocks[firstLoadableBlockIndex]) ? TapeState.ExpectHeader : TapeState.ExpectData;
            int playbackStartBlockIndex = GetEarPlaybackStartBlockIndex(initialBlockIndex);
            StartEarPlaybackBlock(playbackStartBlockIndex, preserveSignalPhase: false);
        }

        public bool ReadEarBit(ulong currentTStates)
        {
            if (earPlaybackState == EarPlaybackState.Idle)
                return earLevel;

            if (!earPlaybackStarted)
            {
                earPlaybackStarted = true;
                lastEarSampleTStates = currentTStates;
                return earLevel;
            }

            if (currentTStates < lastEarSampleTStates)
            {
                lastEarSampleTStates = currentTStates;
                return earLevel;
            }

            ulong elapsed = currentTStates - lastEarSampleTStates;
            while (earPlaybackState != EarPlaybackState.Idle && elapsed >= (ulong)earPulseLengthTStates)
            {
                elapsed -= (ulong)earPulseLengthTStates;
                AdvanceEarPulse();
            }

            lastEarSampleTStates = currentTStates - elapsed;
            return earLevel;
        }

        public bool ReadEarBit()
        {
            return ReadEarBit(lastEarSampleTStates + 1024UL);
        }

        internal void AdvanceToTime(ulong currentTStates)
        {
            ReadEarBit(currentTStates);
        }

        private bool TryGetActivePlaybackBlock(out TapeBlock? block)
        {
            if (earPlaybackBlockIndex >= 0 && earPlaybackBlockIndex < blocks.Count)
            {
                block = blocks[earPlaybackBlockIndex];
                return true;
            }

            block = null;
            return false;
        }

        private bool HasPendingRomLoadableBlock()
        {
            int index = nextBlockIndex;
            while (index < blocks.Count &&
                   (blocks[index].Kind == TapeBlockKind.Metadata || blocks[index].Kind == TapeBlockKind.Pause))
                index++;

            return index < blocks.Count && CanUseRomLoadTrap(blocks[index]);
        }

        private bool HasPendingUnstructuredLoadableStandardDataBlock()
        {
            int index = nextBlockIndex;
            while (index < blocks.Count &&
                   (blocks[index].Kind == TapeBlockKind.Metadata || blocks[index].Kind == TapeBlockKind.Pause))
            {
                index++;
            }

            return index < blocks.Count &&
                   blocks[index].Kind == TapeBlockKind.Data &&
                   blocks[index].IsLoadableRomBlock &&
                   !CanUseRomLoadTrap(blocks[index]);
        }

        private bool HasPendingPlaybackBlock()
        {
            int index = nextBlockIndex;
            while (index < blocks.Count &&
                   (blocks[index].Kind == TapeBlockKind.Metadata || blocks[index].Kind == TapeBlockKind.Pause))
                index++;

            return index < blocks.Count;
        }

        private bool HasStructuredPendingDataBlock()
        {
            return state == TapeState.ExpectData &&
                   (expectedDataLength.HasValue || pendingHeaderInfo != null);
        }

        public bool TryHandleRomLoadTrap(Spectrum128Machine machine, Z80Cpu cpu)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (cpu == null)
                throw new ArgumentNullException(nameof(cpu));
            bool isSyncLoopTrap = cpu.Regs.PC == RomLoadBytesSyncLoopAddress;
            if (cpu.Regs.PC != RomLoadBytesTrapAddress && !isSyncLoopTrap)
                return false;

            bool success = false;
            ushort trapReturnAddress = GetRomTrapReturnAddress(
                machine,
                cpu,
                isSyncLoopTrap,
                hasUnstructuredStandardRomLoadContext: false);

            if (HasRemainingBlocks)
            {
                while (nextBlockIndex < blocks.Count && blocks[nextBlockIndex].Kind == TapeBlockKind.Metadata)
                {
                    AdvanceBlockState(blocks[nextBlockIndex]);
                }

                int playbackTrapBlockIndex = GetActiveRomTrapPlaybackBlockIndex();
                bool usingPlaybackTrapBlock = playbackTrapBlockIndex > nextBlockIndex;
                if (usingPlaybackTrapBlock)
                {
                    while (nextBlockIndex < playbackTrapBlockIndex)
                        AdvanceBlockState(blocks[nextBlockIndex]);

                    state = TapeState.Idle;
                    expectedDataLength = null;
                    pendingHeaderName = null;
                    pendingHeaderInfo = null;
                }

                if (!HasRemainingBlocks)
                {
                    if (TryHandleRomByteStreamTrap(machine, cpu, isSyncLoopTrap))
                        return true;

                    CompleteTrap(machine, cpu, success: false, returnAddress: trapReturnAddress);
                    return true;
                }

                if (!CanUseRomLoadTrap(blocks[nextBlockIndex]))
                {
                    if (TryHandleRomByteStreamTrap(machine, cpu, isSyncLoopTrap))
                        return true;

                    return false;
                }

                TapeBlock block = blocks[nextBlockIndex];
                byte expectedFlag = cpu.Regs.A;
                bool isLoad = (cpu.Regs.F & FlagCarry) != 0;
                if (expectedFlag != HeaderFlag &&
                    expectedFlag != DataFlag &&
                    (cpu.Regs.A_ == HeaderFlag || cpu.Regs.A_ == DataFlag))
                {
                    expectedFlag = cpu.Regs.A_;
                    isLoad = (cpu.Regs.F_ & FlagCarry) != 0;
                }

                ushort expectedLength = cpu.Regs.DE;
                ushort destination = cpu.Regs.IX;
                ushort callerReturnAddress = PeekWord(machine, cpu.Regs.SP);

                if (IsRomTrapByteStreamBlock(block))
                {
                    if (TryHandleRomByteStreamTrap(machine, cpu, isSyncLoopTrap))
                        return true;

                    return false;
                }

                if (state == TapeState.ExpectHeader && IsHeaderBlock(block))
                {
                    TapLoader.TapHeaderInfo header = TapLoader.ParseHeaderInfo(block);
                    if (!TapLoader.IsSupportedRomHeaderType(header.Type))
                        return false;
                }

                if (state == TapeState.ExpectHeader && !IsHeaderBlock(block))
                {
                    throw new InvalidOperationException(
                        $"Tape sequencing error: expected a header block, found flag 0x{block.Flag:X2}.");
                }

                bool hasStructuredDataContext =
                    state == TapeState.ExpectData &&
                    (expectedDataLength.HasValue || pendingHeaderInfo != null);

                if (hasStructuredDataContext)
                {
                    EnsureDataBlock(block);

                    if (expectedDataLength.HasValue && block.Payload!.Length != expectedDataLength.Value)
                    {
                        string displayName = pendingHeaderName ?? "unnamed";
                        throw new InvalidOperationException(
                            $"Tape data block length mismatch for '{displayName}'. Expected {expectedDataLength.Value} bytes, got {block.Payload.Length}.");
                    }
                }

                bool hasStructuredRomLoadContext =
                    state == TapeState.ExpectHeader ||
                    hasStructuredDataContext ||
                    IsHeaderBlock(block);
                bool hasUnstructuredStandardRomLoadContext =
                    state == TapeState.ExpectData &&
                    !hasStructuredDataContext &&
                    machine.HasPendingMountedLoadUsrContinuation &&
                    block.IsLoadableRomBlock &&
                    !IsHeaderBlock(block);
                trapReturnAddress = GetRomTrapReturnAddress(
                    machine,
                    cpu,
                    isSyncLoopTrap,
                    hasUnstructuredStandardRomLoadContext);

                bool lengthMatches = block.Payload!.Length == expectedLength;
                bool flagMatches = block.Flag == expectedFlag;
                bool canUseEarlySyncTrap =
                    isSyncLoopTrap &&
                    lengthMatches &&
                    (hasStructuredRomLoadContext || hasUnstructuredStandardRomLoadContext);
                if (!hasStructuredRomLoadContext && !hasUnstructuredStandardRomLoadContext)
                {
                    return false;
                }

                bool appliedStructuredBasicProgramSideEffects = false;
                if ((flagMatches && lengthMatches) || canUseEarlySyncTrap)
                {
                    success = true;

                    if (isLoad || canUseEarlySyncTrap)
                    {
                        if (pendingHeaderInfo != null && pendingHeaderInfo.Type == 0)
                        {
                            machine.Trace?.Invoke(
                                $"[RomTrap] BASIC stage load file={pendingHeaderInfo.FileName} auto={pendingHeaderInfo.AutoStartLine} len={pendingHeaderInfo.ProgramLength} caller=0x{callerReturnAddress:X4} preserve={(ShouldApplyStructuredBasicProgramLoadSideEffects(callerReturnAddress) ? 1 : 0)}");
                        }

                        if (state == TapeState.ExpectData &&
                            pendingHeaderInfo != null &&
                            pendingHeaderInfo.Type == 0 &&
                            ShouldApplyStructuredBasicProgramLoadSideEffects(callerReturnAddress))
                        {
                            appliedStructuredBasicProgramSideEffects = true;
                            RelocateActiveStackIfLoadWouldOverlap(machine, cpu, 23755, (ushort)block.Payload!.Length);
                            TapLoader.LoadBasicProgram(machine, pendingHeaderInfo, block.Payload!, preserveInterpreterWorkspace: true);
                            if (TapLoader.ShouldReloadStructuredMountedBasicProgramWithoutPreservingInterpreterWorkspace(
                                machine,
                                pendingHeaderInfo.ProgramLength,
                                pendingHeaderInfo.AutoStartLine))
                            {
                                TapLoader.LoadBasicProgram(machine, pendingHeaderInfo, block.Payload!, preserveInterpreterWorkspace: false);
                            }

                            TapLoader.TryExecuteLoadedMountedBasicProgram(
                                machine,
                                pendingHeaderInfo.ProgramLength,
                                (ushort)block.Payload!.Length,
                                pendingHeaderInfo.AutoStartLine);
                            machine.Trace?.Invoke(
                                $"[RomTrap] BASIC side-effects applied file={pendingHeaderInfo.FileName} pending={(machine.HasPendingMountedLoadUsrContinuation ? 1 : 0)} pc=0x{machine.Cpu.Regs.PC:X4}");
                        }
                        else
                        {
                            RelocateActiveStackIfLoadWouldOverlap(machine, cpu, destination, (ushort)block.Payload.Length);
                            for (int i = 0; i < block.Payload.Length; i++)
                                machine.PokeMemory((ushort)(destination + i), block.Payload[i]);
                        }

                        if (machine.HasPendingMountedLoadUsrContinuation)
                        {
                            machine.RefreshPendingMountedLoadInterpreterContext(forceVariableAreaRefresh: true);
                        }
                    }
                    else
                    {
                        for (int i = 0; i < block.Payload.Length; i++)
                        {
                            if (machine.PeekMemory((ushort)(destination + i)) != block.Payload[i])
                            {
                                success = false;
                                break;
                            }
                        }
                    }
                }

                AdvanceBlockState(block);
                SyncEarPlaybackToNextBlock(block.PauseAfterBlockMs);

                if (appliedStructuredBasicProgramSideEffects)
                {
                    ushort loadedProgramLength = pendingHeaderInfo != null && block.Payload != null
                        ? (ushort)Math.Min(pendingHeaderInfo.ProgramLength, block.Payload.Length)
                        : (ushort)0;
                    bool requiresRomDrivenMountedProgram =
                        pendingHeaderInfo != null &&
                        TapLoader.RequiresRomDrivenStructuredMountedProgram(
                            machine,
                            pendingHeaderInfo,
                            loadedProgramLength);
                    trapReturnAddress = (machine.HasPendingMountedLoadUsrContinuation || requiresRomDrivenMountedProgram)
                        ? GetRomTrapReturnAddress(
                            machine,
                            cpu,
                            isSyncLoopTrap,
                            hasUnstructuredStandardRomLoadContext)
                        : callerReturnAddress;
                    machine.Trace?.Invoke(
                        $"[RomTrap] BASIC side-effects return file={(pendingHeaderInfo?.FileName ?? "?")} pending={(machine.HasPendingMountedLoadUsrContinuation ? 1 : 0)} requiresRomDriven={(requiresRomDrivenMountedProgram ? 1 : 0)} return=0x{trapReturnAddress:X4}");
                }
            }

            CompleteTrap(
                machine,
                cpu,
                success,
                returnAddress: trapReturnAddress);
            return true;
        }

        private static void RelocateActiveStackIfLoadWouldOverlap(
            Spectrum128Machine machine,
            Z80Cpu cpu,
            ushort loadAddress,
            ushort loadLength)
        {
            if (loadLength == 0)
                return;

            const int stackWindowSize = 0x100;
            ushort stackStart = cpu.Regs.SP;
            uint loadStart = loadAddress;
            uint loadEndExclusive = (uint)loadAddress + loadLength;
            uint stackEndExclusive = (uint)stackStart + stackWindowSize;
            if (stackEndExclusive <= loadStart || stackStart >= loadEndExclusive)
                return;

            ushort relocatedStackStart = FindSafeStackRelocationAddress(loadAddress, loadLength, stackWindowSize);
            if (relocatedStackStart == stackStart)
                return;

            var stackBytes = new byte[stackWindowSize];
            for (int i = 0; i < stackWindowSize; i++)
                stackBytes[i] = machine.PeekMemory((ushort)(stackStart + i));

            for (int i = 0; i < stackWindowSize; i++)
                machine.PokeMemory((ushort)(relocatedStackStart + i), stackBytes[i]);

            cpu.Regs.SP = relocatedStackStart;
        }

        private static ushort FindSafeStackRelocationAddress(ushort loadAddress, ushort loadLength, int stackWindowSize)
        {
            uint loadStart = loadAddress;
            uint loadEndExclusive = (uint)loadAddress + loadLength;

            for (int candidateEndExclusive = 0x10000; candidateEndExclusive >= stackWindowSize; candidateEndExclusive -= 0x100)
            {
                ushort candidateStart = (ushort)(candidateEndExclusive - stackWindowSize);
                uint candidateEnd = (uint)candidateStart + (uint)stackWindowSize;
                if (candidateEnd <= loadStart || candidateStart >= loadEndExclusive)
                    return candidateStart;
            }

            return (ushort)(0x10000 - stackWindowSize);
        }

        private static ushort GetRomTrapReturnAddress(
            Spectrum128Machine machine,
            Z80Cpu cpu,
            bool isSyncLoopTrap,
            bool hasUnstructuredStandardRomLoadContext)
        {
            if (isSyncLoopTrap)
                return PeekWord(machine, cpu.Regs.SP);

            if (machine.HasPendingMountedLoadUsrContinuation)
            {
                if (hasUnstructuredStandardRomLoadContext &&
                    machine.PendingMountedLoadUsrContinuationRequiresUsrReturnAddress)
                {
                    return RomTapeReturnAddress;
                }

                return PeekWord(machine, cpu.Regs.SP);
            }

            return RomTapeReturnAddress;
        }

        public BootstrapTapeLoadResult TryConsumeBootstrapLoad(Spectrum128Machine machine)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            ulong consumedTStates = 0;
            while (nextBlockIndex < blocks.Count && !blocks[nextBlockIndex].IsLoadableRomBlock)
            {
                consumedTStates += TapLoader.EstimateTapeBlockDurationTStates(blocks[nextBlockIndex]);
                AdvanceBlockState(blocks[nextBlockIndex]);
            }

            if (nextBlockIndex >= blocks.Count)
                return BootstrapTapeLoadResult.None;

            TapLoader.TapHeaderInfo? header = null;
            if (IsHeaderBlock(blocks[nextBlockIndex]))
            {
                consumedTStates += TapLoader.EstimateTapeBlockDurationTStates(blocks[nextBlockIndex]);
                header = TapLoader.ParseHeaderInfo(blocks[nextBlockIndex]);
                AdvanceBlockState(blocks[nextBlockIndex]);
                SyncEarPlaybackToNextBlock(blocks[nextBlockIndex - 1].PauseAfterBlockMs);

                while (nextBlockIndex < blocks.Count && !blocks[nextBlockIndex].IsLoadableRomBlock)
                {
                    consumedTStates += TapLoader.EstimateTapeBlockDurationTStates(blocks[nextBlockIndex]);
                    AdvanceBlockState(blocks[nextBlockIndex]);
                }
            }

            if (nextBlockIndex >= blocks.Count)
                return BootstrapTapeLoadResult.None;

            TapeBlock dataBlock = blocks[nextBlockIndex];
            EnsureDataBlock(dataBlock);

            if (header != null)
            {
                switch (header.Type)
                {
                    case 0:
                        TapLoader.LoadBasicProgram(machine, header, dataBlock.Payload!, preserveInterpreterWorkspace: true);
                        if (TapLoader.ShouldReloadStructuredMountedBasicProgramWithoutPreservingInterpreterWorkspace(
                            machine,
                            header.ProgramLength,
                            header.AutoStartLine))
                        {
                            TapLoader.LoadBasicProgram(machine, header, dataBlock.Payload!, preserveInterpreterWorkspace: false);
                        }

                        break;

                    case 3:
                        TapLoader.LoadBytes(machine, header.StartAddress, dataBlock.Payload!);
                        break;

                    case 1:
                    case 2:
                        throw new NotSupportedException("Array loads are not supported by the bootstrap tape executor.");

                    default:
                        break;
                }
            }

                consumedTStates += TapLoader.EstimateTapeBlockDurationBeforeTrailingPauseTStates(dataBlock);
                AdvanceBlockState(dataBlock);
                SyncEarPlaybackToNextBlock(dataBlock.PauseAfterBlockMs);
                TapLoader.AdvanceBootstrapTapeTime(machine, consumedTStates);
            bool loadedBasicProgram = header != null && header.Type == 0;
            return new BootstrapTapeLoadResult(
                success: true,
                loadedBasicProgram: loadedBasicProgram,
                loadedHeaderType: header?.Type ?? 0xFF,
                loadedProgramLength: loadedBasicProgram ? header!.ProgramLength : (ushort)0,
                loadedDataLength: loadedBasicProgram ? (ushort)dataBlock.Payload!.Length : (ushort)0,
                loadedAutoStartLine: loadedBasicProgram ? header!.AutoStartLine : (ushort)0xFFFF);
        }

        private void AdvanceBlockState(TapeBlock block)
        {
            nextBlockIndex++;

            if (nextBlockIndex >= blocks.Count)
            {
                state = TapeState.Idle;
                expectedDataLength = null;
                pendingHeaderName = null;
                pendingHeaderInfo = null;
                return;
            }

            if (IsHeaderBlock(block))
            {
                TapLoader.TapHeaderInfo header = TapLoader.ParseHeaderInfo(block);
                expectedDataLength = header.DataLength;
                pendingHeaderName = header.FileName;
                pendingHeaderInfo = header;
                state = TapeState.ExpectData;
                return;
            }

            expectedDataLength = null;
            pendingHeaderName = null;
            pendingHeaderInfo = null;
            int nextLoadableBlockIndex = FindNextLoadableBlockIndex(nextBlockIndex);
            state = nextLoadableBlockIndex >= blocks.Count
                ? TapeState.Idle
                : IsHeaderBlock(blocks[nextLoadableBlockIndex]) ? TapeState.ExpectHeader : TapeState.ExpectData;
        }

        private static void EnsureDataBlock(TapeBlock block)
        {
            if (block.Flag != DataFlag)
            {
                throw new InvalidOperationException(
                    $"Tape sequencing error: expected a data block, found flag 0x{block.Flag:X2}.");
            }
        }

        private static bool IsHeaderBlock(TapeBlock block) =>
            block.CanUseRomLoadTrap &&
            block.Flag == HeaderFlag &&
            block.Payload != null &&
            block.Payload.Length == HeaderPayloadLength;

        private static bool CanUseRomLoadTrap(TapeBlock block) =>
            block.Kind == TapeBlockKind.Data &&
            block.CanUseRomLoadTrap &&
            block.Payload != null;

        private static bool IsRomTrapByteStreamBlock(TapeBlock block) =>
            block.Kind == TapeBlockKind.Data &&
            block.CanUseRomLoadTrap &&
            !block.IsLoadableRomBlock &&
            block.StreamData != null &&
            block.Payload != null;

        private int GetActiveRomTrapPlaybackBlockIndex()
        {
            if (earPlaybackBlockIndex < 0 || earPlaybackBlockIndex >= blocks.Count)
                return -1;

            if (!CanUseRomLoadTrap(blocks[earPlaybackBlockIndex]))
                return -1;

            return earPlaybackState switch
            {
                EarPlaybackState.Data => earPlaybackBlockIndex,
                EarPlaybackState.Pause => earPlaybackBlockIndex,
                EarPlaybackState.Idle when earPlaybackStarted || retainedByteStreamTrapAvailable => earPlaybackBlockIndex,
                _ => -1
            };
        }

        private int FindNextLoadableBlockIndex(int startIndex)
        {
            int index = startIndex;
            while (index < blocks.Count && !CanUseRomLoadTrap(blocks[index]))
                index++;

            return index;
        }

        private bool TryHandleRomByteStreamTrap(Spectrum128Machine machine, Z80Cpu cpu, bool isSyncLoopTrap)
        {
            int blockIndex = GetActiveByteStreamTrapBlockIndex();
            if (blockIndex < 0)
                return false;

            TapeBlock block = blocks[blockIndex];
            if (block.StreamData == null)
                return false;

            if (nextBlockIndex < blockIndex)
                SetLogicalPositionToCurrentByteStreamBlock(blockIndex);

            byte expectedFlag = cpu.Regs.A;
            bool isLoad = (cpu.Regs.F & FlagCarry) != 0;
            if (expectedFlag != HeaderFlag &&
                expectedFlag != DataFlag &&
                (cpu.Regs.A_ == HeaderFlag || cpu.Regs.A_ == DataFlag))
            {
                expectedFlag = cpu.Regs.A_;
                isLoad = (cpu.Regs.F_ & FlagCarry) != 0;
            }

            ushort expectedLength = cpu.Regs.DE;
            ushort destination = cpu.Regs.IX;
            ushort callerReturnAddress = PeekWord(machine, cpu.Regs.SP);

            if (romStreamTrapBlockIndex != blockIndex)
            {
                romStreamTrapBlockIndex = blockIndex;
                romStreamTrapByteIndex = FindInitialRomByteStreamTrapIndex(
                    block,
                    expectedFlag,
                    expectedLength);
            }

            if (romStreamTrapByteIndex >= block.StreamData.Length)
            {
                CompleteTrap(
                    machine,
                    cpu,
                    success: false,
                    returnAddress: GetRomTrapReturnAddress(machine, cpu, isSyncLoopTrap, hasUnstructuredStandardRomLoadContext: true));
                return true;
            }

            int framedRequiredLength = expectedLength + 2;
            int remaining = block.StreamData.Length - romStreamTrapByteIndex;
            byte recordFlag = block.StreamData[romStreamTrapByteIndex];
            bool useFramedRecord = recordFlag == expectedFlag && remaining >= framedRequiredLength;
            bool useRawChunk = !useFramedRecord && remaining >= expectedLength;
            if (!useFramedRecord && !useRawChunk)
            {
                CompleteTrap(
                    machine,
                    cpu,
                    success: false,
                    returnAddress: GetRomTrapReturnAddress(machine, cpu, isSyncLoopTrap, hasUnstructuredStandardRomLoadContext: true));
                return true;
            }

            int payloadStart = useFramedRecord ? romStreamTrapByteIndex + 1 : romStreamTrapByteIndex;
            byte[] payload = new byte[expectedLength];
            if (expectedLength > 0)
                Buffer.BlockCopy(block.StreamData, payloadStart, payload, 0, expectedLength);
            bool appliedStructuredBasicProgramSideEffects = false;
            if (isLoad || isSyncLoopTrap)
            {
                for (int i = 0; i < expectedLength; i++)
                    machine.PokeMemory((ushort)(destination + i), payload[i]);

                if (useFramedRecord)
                {
                    appliedStructuredBasicProgramSideEffects =
                        ShouldApplyStructuredBasicProgramLoadSideEffects(callerReturnAddress) &&
                        recordFlag == DataFlag &&
                        pendingHeaderInfo != null &&
                        pendingHeaderInfo.Type == 0;
                    ApplyByteStreamRecordSideEffects(
                        machine,
                        recordFlag,
                        payload,
                        ShouldApplyStructuredBasicProgramLoadSideEffects(callerReturnAddress));
                }
            }
            else
            {
                for (int i = 0; i < expectedLength; i++)
                {
                    if (machine.PeekMemory((ushort)(destination + i)) != payload[i])
                    {
                        CompleteTrap(
                            machine,
                            cpu,
                            success: false,
                            returnAddress: GetRomTrapReturnAddress(machine, cpu, isSyncLoopTrap, hasUnstructuredStandardRomLoadContext: true));
                        return true;
                    }
                }
            }

            romStreamTrapByteIndex += useFramedRecord ? framedRequiredLength : expectedLength;
            if (romStreamTrapByteIndex >= block.StreamData.Length)
            {
                retainedByteStreamTrapAvailable = false;
                romStreamTrapBlockIndex = -1;
                romStreamTrapByteIndex = 0;
                SetLogicalPositionAfterByteStreamBlock(blockIndex);
                pendingPrePlaybackPauseTStates = block.PauseAfterBlockMs * 3500;
                StartEarPlaybackBlock(blockIndex + 1, preserveSignalPhase: false);
            }

            ushort trapReturnAddress = appliedStructuredBasicProgramSideEffects
                ? (
                    machine.HasPendingMountedLoadUsrContinuation ||
                    (pendingHeaderInfo != null &&
                     TapLoader.RequiresRomDrivenStructuredMountedProgram(
                         machine,
                         pendingHeaderInfo,
                         (ushort)Math.Min(pendingHeaderInfo.ProgramLength, payload.Length))))
                    ? GetRomTrapReturnAddress(machine, cpu, isSyncLoopTrap, hasUnstructuredStandardRomLoadContext: true)
                    : callerReturnAddress
                : GetRomTrapReturnAddress(machine, cpu, isSyncLoopTrap, hasUnstructuredStandardRomLoadContext: true);
            CompleteTrap(
                machine,
                cpu,
                success: true,
                returnAddress: trapReturnAddress);
            return true;
        }

        private void ApplyByteStreamRecordSideEffects(Spectrum128Machine machine, byte recordFlag, byte[] payload, bool applyBasicProgramSideEffects)
        {
            if (recordFlag == HeaderFlag && payload.Length == HeaderPayloadLength)
            {
                byte[] headerStream = new byte[payload.Length + 2];
                headerStream[0] = HeaderFlag;
                Buffer.BlockCopy(payload, 0, headerStream, 1, payload.Length);
                headerStream[^1] = 0;

                TapeBlock syntheticHeader = TapeBlock.CreateData(
                    headerStream,
                    PilotPulseLengthTStates,
                    HeaderPilotPulseCount,
                    SyncFirstPulseLengthTStates,
                    SyncSecondPulseLengthTStates,
                    ZeroBitPulseLengthTStates,
                    OneBitPulseLengthTStates,
                    usedBitsInLastByte: 8,
                    pauseAfterBlockMs: 0);

                if (IsHeaderBlock(syntheticHeader))
                {
                    TapLoader.TapHeaderInfo header = TapLoader.ParseHeaderInfo(syntheticHeader);
                    if (TapLoader.IsSupportedRomHeaderType(header.Type))
                    {
                        expectedDataLength = header.DataLength;
                        pendingHeaderName = header.FileName;
                        pendingHeaderInfo = header;
                        state = TapeState.ExpectData;
                    }
                }

                return;
            }

            if (recordFlag == DataFlag && pendingHeaderInfo != null)
            {
                if (pendingHeaderInfo.Type == 0 && applyBasicProgramSideEffects)
                {
                    TapLoader.LoadBasicProgram(machine, pendingHeaderInfo, payload, preserveInterpreterWorkspace: true);
                    TapLoader.TryExecuteLoadedMountedBasicProgram(
                        machine,
                        pendingHeaderInfo.ProgramLength,
                        (ushort)payload.Length,
                        pendingHeaderInfo.AutoStartLine);
                }

                expectedDataLength = null;
                pendingHeaderName = null;
                pendingHeaderInfo = null;
                state = TapeState.ExpectHeader;
            }
        }

        private static bool ShouldApplyStructuredBasicProgramLoadSideEffects(ushort callerReturnAddress)
        {
            return callerReturnAddress < 0x4000;
        }

        private static int FindInitialRomByteStreamTrapIndex(TapeBlock block, byte expectedFlag, ushort expectedLength)
        {
            if (block.StreamData == null)
                return 0;

            int requiredLength = expectedLength + 2;
            if (requiredLength <= 0 || block.StreamData.Length < requiredLength)
                return 0;

            for (int index = 0; index <= block.StreamData.Length - requiredLength; index++)
            {
                if (block.StreamData[index] == expectedFlag)
                    return index;
            }

            return 0;
        }

        private int GetActiveByteStreamTrapBlockIndex()
        {
            if (earPlaybackBlockIndex < 0 || earPlaybackBlockIndex >= blocks.Count)
                return -1;

            TapeBlock block = blocks[earPlaybackBlockIndex];
            if (block.Kind != TapeBlockKind.Data ||
                block.IsLoadableRomBlock ||
                block.StreamData == null)
            {
                return -1;
            }

            return earPlaybackState switch
            {
                EarPlaybackState.Data => earPlaybackBlockIndex,
                EarPlaybackState.Pause => earPlaybackBlockIndex,
                EarPlaybackState.Idle when earPlaybackStarted || retainedByteStreamTrapAvailable => earPlaybackBlockIndex,
                _ => -1
            };
        }

        private void SetLogicalPositionToCurrentByteStreamBlock(int blockIndex)
        {
            nextBlockIndex = blockIndex;
            expectedDataLength = null;
            pendingHeaderName = null;
            pendingHeaderInfo = null;
            state = TapeState.ExpectData;
        }

        private void SetLogicalPositionAfterByteStreamBlock(int blockIndex)
        {
            nextBlockIndex = Math.Min(blockIndex + 1, blocks.Count);
            retainedByteStreamTrapAvailable = false;
            expectedDataLength = null;
            pendingHeaderName = null;
            pendingHeaderInfo = null;
            int nextLoadableBlockIndex = FindNextLoadableBlockIndex(nextBlockIndex);
            state = nextLoadableBlockIndex >= blocks.Count
                ? TapeState.Idle
                : IsHeaderBlock(blocks[nextLoadableBlockIndex]) ? TapeState.ExpectHeader : TapeState.ExpectData;
        }

        private void AdvanceLogicalPositionAfterLivePlaybackBlockIfNeeded(int blockIndex, TapeBlock block)
        {
            if (block.Kind != TapeBlockKind.Data)
                return;

            if (block.IsLoadableRomBlock)
            {
                if (nextBlockIndex <= blockIndex)
                    AdvanceBlockState(block);

                return;
            }

            if (block.StreamData == null)
                return;

            bool hasCompletedPastLogicalPosition = nextBlockIndex < blockIndex;
            bool hasExplicitTrailingPause = block.PauseAfterBlockMs != 0 && nextBlockIndex <= blockIndex;
            if (!hasCompletedPastLogicalPosition && !hasExplicitTrailingPause)
                return;

            SetLogicalPositionAfterByteStreamBlock(blockIndex);
        }

        private void SyncRomByteStreamTrapToEarProgress()
        {
            if (earPlaybackBlockIndex < 0 || earPlaybackBlockIndex >= blocks.Count)
                return;

            if (romStreamTrapBlockIndex != earPlaybackBlockIndex)
                return;

            if (earStreamByteIndex <= romStreamTrapByteIndex)
                return;

            TapeBlock block = blocks[earPlaybackBlockIndex];
            if (block.StreamData == null)
                return;

            romStreamTrapByteIndex = Math.Min(earStreamByteIndex, block.StreamData.Length);
            if (romStreamTrapByteIndex >= block.StreamData.Length)
            {
                retainedByteStreamTrapAvailable = false;
                romStreamTrapBlockIndex = -1;
                romStreamTrapByteIndex = 0;
            }
        }

        private bool ShouldRetainCompletedByteStreamForRomTrap(int blockIndex, TapeBlock block)
        {
            int nextPlaybackBlockIndex = GetEarPlaybackStartBlockIndex(blockIndex + 1);
            bool hasUnconsumedEmbeddedRomTrapData =
                block.Kind == TapeBlockKind.Data &&
                !block.IsLoadableRomBlock &&
                block.StreamData != null &&
                romStreamTrapBlockIndex == blockIndex &&
                romStreamTrapByteIndex > 0 &&
                romStreamTrapByteIndex < block.StreamData.Length;

            return block.Kind == TapeBlockKind.Data &&
                   !block.IsLoadableRomBlock &&
                   block.StreamData != null &&
                   nextPlaybackBlockIndex >= blocks.Count &&
                   ((block.PauseAfterBlockMs == 0 &&
                     state != TapeState.Idle &&
                     nextBlockIndex <= earPlaybackBlockIndex) ||
                    hasUnconsumedEmbeddedRomTrapData);
        }

        private void SyncEarPlaybackToNextBlock(ushort consumedBlockPauseMs = 0)
        {
            int desiredBlockIndex = GetEarPlaybackStartBlockIndex(nextBlockIndex);
            if (desiredBlockIndex == earPlaybackBlockIndex)
            {
                pendingPrePlaybackPauseTStates = 0;
                return;
            }

            pendingPrePlaybackPauseTStates = consumedBlockPauseMs * 3500;
            StartEarPlaybackBlock(desiredBlockIndex, preserveSignalPhase: false);
        }

        private int GetEarPlaybackStartBlockIndex(int startIndex)
        {
            int index = startIndex;
            while (index < blocks.Count && blocks[index].Kind == TapeBlockKind.Metadata)
                index++;

            if (skipCustomHeaderForEarPlayback && index >= 0 && index < blocks.Count && IsHeaderBlock(blocks[index]))
            {
                var header = TapLoader.ParseHeaderInfo(blocks[index]);
                if (header.Type > 3 && index + 1 < blocks.Count && blocks[index + 1].Kind == TapeBlockKind.Data)
                    index++;
            }

            return index;
        }

        private void StartEarPlaybackBlock(int blockIndex, bool preserveSignalPhase)
        {
            while (blockIndex >= 0 && blockIndex < blocks.Count && blocks[blockIndex].Kind == TapeBlockKind.Metadata)
                blockIndex++;

            if (blockIndex < 0 || blockIndex >= blocks.Count)
            {
                BeginEndOfStreamIdleTransition(preserveSignalPhase);
                return;
            }

            earPlaybackBlockIndex = blockIndex;
            earStreamByteIndex = 0;
            earBitIndex = 0;
            earPulseRepeatCount = 0;
            earPulseSequenceIndex = 0;
            earNextBlockIndexAfterPause = blockIndex + 1;
            retainedByteStreamTrapAvailable = false;
            if (!preserveSignalPhase)
            {
                earLevel = initialEarLevelHigh;
                earPlaybackStarted = false;
            }

            if (pendingPrePlaybackPauseTStates > 0)
            {
                earPlaybackState = EarPlaybackState.Pause;
                earPulseLengthTStates = pendingPrePlaybackPauseTStates;
                earNextBlockIndexAfterPause = blockIndex;
                pendingPrePlaybackPauseTStates = 0;
                earLevel = false;
                return;
            }

            TapeBlock block = blocks[blockIndex];
            switch (block.Kind)
            {
                case TapeBlockKind.Data:
                    earPilotPulsesRemaining = block.PilotPulseCount;
                    if (earPilotPulsesRemaining > 0)
                    {
                        earPlaybackState = EarPlaybackState.Pilot;
                        earPulseLengthTStates = ScaleBlockTiming(block.PilotPulseLength, block);
                    }
                    else if (block.SyncFirstPulseLength != 0)
                    {
                        earPlaybackState = EarPlaybackState.SyncFirst;
                        earPulseLengthTStates = ScaleBlockTiming(block.SyncFirstPulseLength, block);
                    }
                    else
                    {
                        earPlaybackState = EarPlaybackState.Data;
                        earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                    }
                    return;

                case TapeBlockKind.PureTone:
                    earPlaybackState = EarPlaybackState.PureTone;
                    earPilotPulsesRemaining = block.PureTonePulseCount;
                    earPulseLengthTStates = ScaleBlockTiming(block.PureTonePulseLength, block);
                    return;

                case TapeBlockKind.PulseSequence:
                    earPlaybackState = EarPlaybackState.PulseSequence;
                    earPulseLengthTStates = ScaleBlockTiming(block.PulseSequence![0], block);
                    return;

                case TapeBlockKind.DirectRecording:
                    earPlaybackState = EarPlaybackState.DirectRecording;
                    earPulseLengthTStates = ScaleBlockTiming(block.DirectRecordingSampleTStates, block);
                    earLevel = GetCurrentDirectRecordingLevel(block);
                    return;

                case TapeBlockKind.Pause:
                    earPlaybackState = EarPlaybackState.Pause;
                    earPulseLengthTStates = GetPauseLengthTStates(block);
                    return;

                case TapeBlockKind.SetSignalLevel:
                    earLevel = block.SignalLevel ?? true;
                    StartEarPlaybackBlock(blockIndex + 1, preserveSignalPhase: true);
                    return;

                default:
                    earPlaybackState = EarPlaybackState.Idle;
                    earPulseLengthTStates = 0;
                    return;
            }
        }

        private void AdvanceEarPulse()
        {
            TapeBlock block = blocks[earPlaybackBlockIndex];

            switch (earPlaybackState)
            {
                case EarPlaybackState.Pilot:
                    earLevel = !earLevel;
                    earPilotPulsesRemaining--;
                    if (earPilotPulsesRemaining > 0)
                    {
                        earPulseLengthTStates = ScaleBlockTiming(block.PilotPulseLength, block);
                        return;
                    }

                    if (block.SyncFirstPulseLength != 0)
                    {
                        earPlaybackState = EarPlaybackState.SyncFirst;
                        earPulseLengthTStates = ScaleBlockTiming(block.SyncFirstPulseLength, block);
                    }
                    else
                    {
                        earPlaybackState = EarPlaybackState.Data;
                        earPulseRepeatCount = 0;
                        earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                    }
                    return;

                case EarPlaybackState.SyncFirst:
                    earLevel = !earLevel;
                    earPlaybackState = EarPlaybackState.SyncSecond;
                    earPulseLengthTStates = ScaleBlockTiming(block.SyncSecondPulseLength, block);
                    return;

                case EarPlaybackState.SyncSecond:
                    earLevel = !earLevel;
                    earPlaybackState = EarPlaybackState.Data;
                    earPulseRepeatCount = 0;
                    earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                    return;

                case EarPlaybackState.Data:
                    earLevel = !earLevel;
                    earPulseRepeatCount++;
                    if (earPulseRepeatCount < 2)
                    {
                        earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                        return;
                    }

                    earPulseRepeatCount = 0;
                    earBitIndex++;
                    if (earBitIndex >= block.GetStreamByteBitCount(earStreamByteIndex))
                    {
                        earBitIndex = 0;
                        earStreamByteIndex++;
                        SyncRomByteStreamTrapToEarProgress();
                    }

                    if (earStreamByteIndex < blocks[earPlaybackBlockIndex].StreamByteCount)
                    {
                        earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                        return;
                    }

                    AdvanceLogicalPositionAfterLivePlaybackBlockIfNeeded(earPlaybackBlockIndex, block);

                    if (block.PauseAfterBlockMs != 0)
                    {
                        earPlaybackState = EarPlaybackState.Pause;
                        earPulseLengthTStates = GetPauseLengthTStates(block);
                        return;
                    }

                    if (ShouldRetainCompletedByteStreamForRomTrap(earPlaybackBlockIndex, block))
                    {
                        earPlaybackState = EarPlaybackState.Idle;
                        earPulseLengthTStates = 0;
                        retainedByteStreamTrapAvailable = true;
                        return;
                    }

                    StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                    return;

                case EarPlaybackState.DirectRecording:
                {
                    int currentBitCount = GetCurrentDirectRecordingBitCount(block);
                    earBitIndex++;
                    if (earBitIndex >= currentBitCount)
                    {
                        earBitIndex = 0;
                        earStreamByteIndex++;
                    }

                    if (block.DirectRecordingSamples == null || earStreamByteIndex >= block.DirectRecordingSamples.Length)
                    {
                        if (block.PauseAfterBlockMs != 0)
                        {
                            earPlaybackState = EarPlaybackState.Pause;
                            earPulseLengthTStates = GetPauseLengthTStates(block);
                            return;
                        }

                        StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                        return;
                    }

                    earLevel = GetCurrentDirectRecordingLevel(block);
                    earPulseLengthTStates = ScaleBlockTiming(block.DirectRecordingSampleTStates, block);
                    return;
                }

                case EarPlaybackState.PureTone:
                    earLevel = !earLevel;
                    earPilotPulsesRemaining--;
                    if (earPilotPulsesRemaining > 0)
                    {
                        earPulseLengthTStates = ScaleBlockTiming(block.PureTonePulseLength, block);
                        return;
                    }

                    StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                    return;

                case EarPlaybackState.PulseSequence:
                    earLevel = !earLevel;
                    earPulseSequenceIndex++;
                    if (block.PulseSequence != null && earPulseSequenceIndex < block.PulseSequence.Length)
                    {
                        earPulseLengthTStates = ScaleBlockTiming(block.PulseSequence[earPulseSequenceIndex], block);
                        return;
                    }

                    if (block.PauseAfterBlockMs != 0)
                    {
                        earPlaybackState = EarPlaybackState.Pause;
                        earPulseLengthTStates = GetPauseLengthTStates(block);
                        return;
                    }

                    StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                    return;

                case EarPlaybackState.Pause:
                    earLevel = false;
                    AdvanceLogicalPositionAfterLivePlaybackBlockIfNeeded(earPlaybackBlockIndex, block);

                    if (ShouldRetainCompletedByteStreamForRomTrap(earPlaybackBlockIndex, block))
                    {
                        earPlaybackState = EarPlaybackState.Idle;
                        earPulseLengthTStates = 0;
                        retainedByteStreamTrapAvailable = true;
                        return;
                    }

                    StartEarPlaybackBlock(earNextBlockIndexAfterPause, preserveSignalPhase: true);
                    return;

                case EarPlaybackState.EndOfStreamTransition:
                    if (endOfStreamTransitionPhase == 0)
                    {
                        endOfStreamTransitionPhase = 1;
                        earLevel = true;
                        earPulseLengthTStates = Math.Max(1, endOfStreamTransitionTailTStates);
                        return;
                    }

                    if (endOfStreamTransitionPhase == 1)
                    {
                        endOfStreamTransitionPhase = 2;
                        earLevel = false;
                        earPulseLengthTStates = Math.Max(1, endOfStreamTransitionTailTStates);
                        return;
                    }

                    earLevel = true;
                    earPlaybackState = EarPlaybackState.Idle;
                    earPulseLengthTStates = 0;
                    earPlaybackStarted = false;
                    return;

                default:
                    earPlaybackState = EarPlaybackState.Idle;
                    earPulseLengthTStates = 0;
                    return;
            }
        }

        private int GetCurrentBitPulseLengthTStates()
        {
            TapeBlock block = blocks[earPlaybackBlockIndex];
            byte streamByte = block.GetStreamByte(earStreamByteIndex);
            bool bitSet = ((streamByte >> (7 - earBitIndex)) & 0x01) != 0;
            return ScaleBlockTiming(bitSet ? block.OneBitPulseLength : block.ZeroBitPulseLength, block);
        }

        private bool GetCurrentDirectRecordingLevel(TapeBlock block)
        {
            byte streamByte = block.DirectRecordingSamples![earStreamByteIndex];
            return ((streamByte >> (7 - earBitIndex)) & 0x01) != 0;
        }

        private int GetCurrentDirectRecordingBitCount(TapeBlock block)
        {
            return earStreamByteIndex == block.DirectRecordingSamples!.Length - 1
                ? block.UsedBitsInLastByte
                : 8;
        }

        private int ScaleBlockTiming(int tStates, TapeBlock block)
        {
            int clamped = Math.Max(1, tStates);
            if (block.IsLoadableRomBlock)
            {
                if (loadableTimingDivisor <= 1)
                    return clamped;

                return Math.Max(1, clamped / loadableTimingDivisor);
            }

            if (nonRomTimingDivisor <= 1)
                return clamped;

            return Math.Max(1, clamped / nonRomTimingDivisor);
        }

        private int GetPauseLengthTStates(TapeBlock block)
        {
            int basePauseTStates = Math.Max(1, block.PauseAfterBlockMs * 3500);
            if (block.PauseAfterBlockMs <= 2)
                return basePauseTStates;

            if (block.IsLoadableRomBlock)
            {
                if (loadableTimingDivisor <= 1)
                    return basePauseTStates;

                return Math.Max(1, basePauseTStates / loadableTimingDivisor);
            }

            if (nonRomTimingDivisor <= 1)
                return basePauseTStates;

            return Math.Max(1, basePauseTStates / nonRomTimingDivisor);
        }

        private void BeginEndOfStreamIdleTransition(bool preserveSignalPhase)
        {
            retainedByteStreamTrapAvailable = false;
            endOfStreamTransitionTStates = 0;
            endOfStreamTransitionTailTStates = 0;
            endOfStreamTransitionPhase = 0;

            if (earPlaybackBlockIndex < 0 || earPlaybackBlockIndex >= blocks.Count)
            {
                earPlaybackState = EarPlaybackState.Idle;
                earPulseLengthTStates = 0;
                earLevel = true;
                earPlaybackStarted = false;
                return;
            }

            TapeBlock lastBlock = blocks[earPlaybackBlockIndex];
            bool endedOnProtectedByteStream =
                lastBlock.Kind == TapeBlockKind.Data &&
                !lastBlock.IsLoadableRomBlock &&
                lastBlock.StreamData != null;

            if (!endedOnProtectedByteStream)
            {
                earPlaybackState = EarPlaybackState.Idle;
                earPulseLengthTStates = 0;
                if (!preserveSignalPhase)
                {
                    earLevel = true;
                    earPlaybackStarted = false;
                }
                return;
            }

            earPlaybackState = EarPlaybackState.EndOfStreamTransition;
            int protectedEdgePulseTStates = ScaleBlockTiming(
                Math.Min(lastBlock.ZeroBitPulseLength, lastBlock.OneBitPulseLength),
                lastBlock);
            int protectedEndPauseTStates = GetPauseLengthTStates(lastBlock);
            endOfStreamTransitionTStates = Math.Max(
                protectedEdgePulseTStates,
                Math.Max(2048, protectedEndPauseTStates));
            endOfStreamTransitionTailTStates = Math.Max(1, protectedEdgePulseTStates);
            earPulseLengthTStates = Math.Max(1, endOfStreamTransitionTStates);
            earLevel = false;
            earPlaybackStarted = false;
        }

        private static ushort PeekWord(Spectrum128Machine machine, ushort address)
        {
            byte lo = machine.PeekMemory(address);
            byte hi = machine.PeekMemory((ushort)(address + 1));
            return (ushort)(lo | (hi << 8));
        }

        private static void CompleteTrap(Spectrum128Machine machine, Z80Cpu cpu, bool success, ushort returnAddress)
        {
            cpu.Regs.SP += 2;
            cpu.Regs.PC = returnAddress;
            cpu.Regs.IX += cpu.Regs.DE;
            cpu.Regs.DE = 0;

            if (success)
                cpu.Regs.F = (byte)(cpu.Regs.F | FlagCarry);
            else
                cpu.Regs.F = (byte)(cpu.Regs.F & ~FlagCarry);

            cpu.AdvanceTStates(32);
        }
    }

    public static class TapLoader
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

        private const ushort BasicProgramStart = 23755;
        private const ushort MainExecutionLoopAddress = 0x1555;
        private const ushort UsrReturnAddress = 0x2D2B;
        private const ushort EndCalcLiteralAddress = 0x2758;
        private const ushort DefaultStackPointer = 0xFF58;
        private const ushort RomSystemVariablesBase = 0x5C3A;

        private const ushort NewPpcAddress = 23618;
        private const ushort NspPcAddress = 23620;
        private const ushort PpcAddress = 23621;
        private const ushort SubPpcAddress = 23623;
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

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
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

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
            InitializeMachineForFakeTapeLoad(machine, use128kMode);
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
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            if (CanLoadAllStandardTapeBlocks(blocks))
            {
                bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
                if (RequiresRomDrivenBootstrapForStandardTape(machine, blocks, use128kMode))
                {
                    return new TapeLoadPlan(
                        TapeLoadStrategy.RomBootstrapMounted,
                        "Standard tape chains from an autorun BASIC stage into a second BASIC loader and should resume under ROM control.");
                }

                if (RequiresMountedLoadSemanticsForStandardTape(machine, blocks, use128kMode))
                {
                    return new TapeLoadPlan(
                        TapeLoadStrategy.BootstrapHybrid,
                        "Standard tape contains a protected or chained loader stage that requires mounted LOAD semantics.");
                }

                return new TapeLoadPlan(
                    TapeLoadStrategy.FullFakeLoad,
                    "All blocks are standard header/data pairs and can be fully fake-loaded.");
            }

            if (RequiresLeadingStandardBasicChainFakeLoad(machine, blocks))
            {
                return new TapeLoadPlan(
                    TapeLoadStrategy.LeadingStandardChainFakeLoad,
                    "Tape begins with a chain of standard BASIC stages before a protected remainder and can fake-load that safe prefix.");
            }

            if (RequiresRomDrivenBootstrapForMixedTape(machine, blocks))
            {
                return new TapeLoadPlan(
                    TapeLoadStrategy.RomBootstrapMounted,
                    "Tape begins with an autorun BASIC stage that chains into a mounted BASIC loader before protected continuation.");
            }

            if (RequiresMountedRealtimeForMixedTape(machine, blocks))
            {
                return new TapeLoadPlan(
                    TapeLoadStrategy.BootstrapHybrid,
                    "Tape begins with a BASIC stage followed by raw standard byte streams and should enter the mounted remainder through the bootstrap loader path.");
            }

            if (CanBootstrapBasicProgramAndMountRemaining(blocks))
                return new TapeLoadPlan(TapeLoadStrategy.BootstrapHybrid, "Tape begins with a standard BASIC loader and requires mounted continuation.");

            return new TapeLoadPlan(TapeLoadStrategy.MountedRealtime, "Tape does not have a safe fake-load bootstrap path.");
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
                        result.TotalBlockCount,
                        result.ConsumedBlockCount,
                        result.DisplayName,
                        result.AutoStartFileName);
                }

                case TapeLoadStrategy.RomBootstrapMounted:
                {
                    TapBootstrapResult result = LoadLeadingBasicProgramAndMountRemainingForRomAutoStart(
                        machine,
                        displayName,
                        blocks,
                        skipCustomHeaderForEarPlayback: false,
                        nonRomTimingDivisor: nonRomTimingDivisor,
                        loadableTimingDivisor: loadableTimingDivisor,
                        initialEarLevelHigh: initialEarLevelHigh);
                    return new TapeExecutionResult(
                        TapeLoadStrategy.RomBootstrapMounted,
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
                        result.TotalBlockCount,
                        result.ConsumedBlockCount,
                        result.DisplayName,
                        result.AutoStartFileName);
                }

                default:
                {
                    bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
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

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
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

            AdvanceBootstrapTapeTime(machine, consumedTStates);

            int ignoredLoadCount = 0;
            if (autoStartFileName != null)
            {
                ignoredLoadCount = ExecuteBootstrapBasicAutoStart(
                    machine,
                    autoStartProgramStart,
                    autoStartProgramLength,
                    autoStartLine,
                    ignoreLoadStatements: true).IgnoredLoadCount;
            }

            if (TryFindTapCustomLoaderResumePc(machine, blocks, out ushort resumePc))
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

        private static bool TryFindTapCustomLoaderResumePc(
            Spectrum128Machine machine,
            IReadOnlyList<TapeBlock> blocks,
            out ushort resumePc)
        {
            resumePc = 0;
            if (machine == null || blocks == null)
                return false;

            if (FindFirstCustomHeaderBlockIndex(blocks) < 0)
                return false;

            ushort currentPc = machine.Cpu.Regs.PC;
            int codeBlockStartIndex = -1;
            TapHeaderInfo? codeHeader = null;

            for (int i = 0; i + 1 < blocks.Count; i++)
            {
                if (!IsStandardHeaderBlock(blocks[i]) || blocks[i + 1].Flag != DataFlag)
                    continue;

                TapHeaderInfo header = ParseHeaderInfo(blocks[i]);
                if (header.Type != CodeType)
                    continue;

                ushort start = header.StartAddress;
                ushort endExclusive = (ushort)(start + header.DataLength);
                if (currentPc >= start && currentPc < endExclusive)
                {
                    codeBlockStartIndex = i;
                    codeHeader = header;
                    break;
                }
            }

            if (codeBlockStartIndex < 0 || codeHeader == null)
                return false;

            if (codeBlockStartIndex + 1 >= blocks.Count || blocks[codeBlockStartIndex + 1].Payload == null)
                return false;

            byte[] codePayload = blocks[codeBlockStartIndex + 1].Payload!;
            int searchEndExclusive = Math.Min(codeHeader.DataLength - 1, codePayload.Length - 1);
            for (int offset = 0; offset < searchEndExclusive; offset++)
            {
                if (codePayload[offset] == 0x10 &&
                    codePayload[offset + 1] == 0xFE)
                {
                    resumePc = (ushort)(codeHeader.StartAddress + offset);
                    return true;
                }
            }

            return false;
        }

        private static bool RequiresRomDrivenBootstrapForStandardTape(
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

        private static bool RequiresRomDrivenBootstrapForMixedTape(
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

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
            return !CanBootstrapLoadedBasicProgram(machine, nextHeader, blocks[index + 1].Payload!, use128kMode);
        }

        private static bool RequiresLeadingStandardBasicChainFakeLoad(
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

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
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

        private static bool RequiresMountedRealtimeForMixedTape(
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
                    return false;

                if (block.Kind != TapeBlockKind.Data)
                    return false;

                sawRawStandardData = true;
                index++;
            }

            return sawRawStandardData;
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

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
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

        private static int FindLeadingStandardBasicChainEndIndex(IReadOnlyList<TapeBlock> blocks)
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

        private static bool RequiresMountedLoadSemanticsForStandardTape(
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
            bool initialEarLevelHigh = true)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (blocks == null)
                throw new ArgumentNullException(nameof(blocks));
            if (blocks.Count == 0)
                throw new InvalidOperationException("The tape image does not contain any blocks.");

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
            InitializeMachineForFakeTapeLoad(machine, use128kMode);

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
                    nonRomTimingDivisor: nonRomTimingDivisor,
                    loadableTimingDivisor: loadableTimingDivisor,
                    initialEarLevelHigh: initialEarLevelHigh);
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
                    return BasicBootstrapExecutor.Requires128kTapeLoadMode(
                        machine,
                        BasicProgramStart,
                        header.ProgramLength,
                        header.AutoStartLine);
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            return false;
        }

        private static void StartRomDrivenMountedBasicAutoStart(
            Spectrum128Machine machine,
            ushort autoStartLine)
        {
            machine.Cpu.Regs.PC = MainExecutionLoopAddress;

            WriteWord(machine, NewPpcAddress, autoStartLine);
            machine.PokeMemory((ushort)(NewPpcAddress + 2), 0);
            WriteWord(machine, PpcAddress, autoStartLine);
            machine.PokeMemory(SubPpcAddress, 0);
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

        internal static ulong EstimateTapeBlockDurationTStates(TapeBlock block)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));

            return block.Kind switch
            {
                TapeBlockKind.Data => EstimateDataBlockDurationTStates(block),
                TapeBlockKind.PureTone => (ulong)block.PureTonePulseLength * block.PureTonePulseCount,
                TapeBlockKind.PulseSequence => EstimatePulseSequenceDurationTStates(block.PulseSequence),
                TapeBlockKind.DirectRecording => EstimateDirectRecordingDurationTStates(block),
                TapeBlockKind.Pause => (ulong)block.PauseAfterBlockMs * TStatesPerMillisecond48k,
                _ => 0UL
            };
        }

        internal static ulong EstimateTapeBlockDurationBeforeTrailingPauseTStates(TapeBlock block)
        {
            if (block == null)
                throw new ArgumentNullException(nameof(block));

            return block.Kind switch
            {
                TapeBlockKind.Data => EstimateDataBlockDurationTStates(block, includeTrailingPause: false),
                TapeBlockKind.Pause => 0UL,
                _ => EstimateTapeBlockDurationTStates(block)
            };
        }

        private static ulong EstimateDataBlockDurationTStates(TapeBlock block)
            => EstimateDataBlockDurationTStates(block, includeTrailingPause: true);

        private static ulong EstimateDataBlockDurationTStates(TapeBlock block, bool includeTrailingPause)
        {
            ulong total = (ulong)block.PilotPulseLength * block.PilotPulseCount;
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

            if (includeTrailingPause)
                total += (ulong)block.PauseAfterBlockMs * TStatesPerMillisecond48k;
            return total;
        }

        private static ulong EstimatePulseSequenceDurationTStates(int[]? pulseSequence)
        {
            if (pulseSequence == null)
                return 0;

            ulong total = 0;
            for (int i = 0; i < pulseSequence.Length; i++)
                total += (ulong)pulseSequence[i];

            return total;
        }

        private static ulong EstimateDirectRecordingDurationTStates(TapeBlock block)
        {
            if (block.DirectRecordingSamples == null)
                return 0;

            ulong totalBits = 0;
            for (int i = 0; i < block.DirectRecordingSamples.Length; i++)
                totalBits += i == block.DirectRecordingSamples.Length - 1 ? block.UsedBitsInLastByte : 8UL;

            return (totalBits * block.DirectRecordingSampleTStates)
                + ((ulong)block.PauseAfterBlockMs * TStatesPerMillisecond48k);
        }

        internal static IReadOnlyList<TapeBlock> ParseBlocks(byte[] fileData)
        {
            var blocks = new List<TapeBlock>();
            int offset = 0;

            while (offset < fileData.Length)
            {
                if (offset + 2 > fileData.Length)
                    throw new InvalidOperationException("The .tap file ends inside a block length field.");

                int blockLength = ReadWord(fileData, offset);
                offset += 2;

                if (blockLength < 2)
                {
                    throw new InvalidOperationException(
                        $"Invalid tape block length {blockLength}. Each block must contain at least a flag byte and checksum byte.");
                }

                if (offset + blockLength > fileData.Length)
                    throw new InvalidOperationException("The .tap file ends inside a tape block.");

                ValidateChecksum(fileData, offset, blockLength);
                byte[] streamData = new byte[blockLength];
                Buffer.BlockCopy(fileData, offset, streamData, 0, blockLength);
                blocks.Add(TapeBlock.CreateData(
                    streamData,
                    PilotPulseLengthTStates,
                    streamData[0] == HeaderFlag ? HeaderPilotPulseCount : DataPilotPulseCount,
                    SyncFirstPulseLengthTStates,
                    SyncSecondPulseLengthTStates,
                    ZeroBitPulseLengthTStates,
                    OneBitPulseLengthTStates,
                    usedBitsInLastByte: 8,
                    pauseAfterBlockMs: StandardTapPauseAfterBlockMs));
                offset += blockLength;
            }

            return blocks;
        }

        internal static void InitializeMachineForFakeTapeLoad(Spectrum128Machine machine, bool use128kMode)
        {
            machine.Reset();
            if (use128kMode)
                machine.ConfigureFor128kTapeLoad(borderColor: 0);
            else
                machine.ConfigureFor48kTapeLoad(borderColor: 0);

            machine.Cpu.Regs.PC = MainExecutionLoopAddress;
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

            if (((int)programStart + payload.Length) > 0x10000)
                throw new InvalidOperationException("The BASIC program and variables do not fit in 48K RAM.");

            LoadBytes(machine, programStart, payload);

            ushort varsAddress = (ushort)(programStart + programLength);
            ushort endAddress = (ushort)(programStart + payload.Length);

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
                WriteWord(machine, EditLineAddress, endAddress);
                WriteWord(machine, WorkspaceAddress, endAddress);
                WriteWord(machine, StackBottomAddress, endAddress);
                WriteWord(machine, StackEndAddress, endAddress);
                InitializeInterpreterPointersForLoadedProgram(machine, endAddress);
            }

            machine.PokeMemory(endAddress, 0x0D);

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

        private readonly record struct BootstrapExecutionResult(int IgnoredLoadCount, int ConsumedMountedLoadCount)
        {
            public static readonly BootstrapExecutionResult None = new(0, 0);
        }

        private static ushort ReadWord(Spectrum128Machine machine, ushort address)
        {
            return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
        }

        private static bool TryReadLiveBasicNumericVariable(
            Spectrum128Machine machine,
            string variableName,
            out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                return false;

            char variableChar = char.ToLowerInvariant(variableName[0]);
            if (variableChar < 'a' || variableChar > 'z')
                return false;

            byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));
            ushort varsAddress = ReadWord(machine, VarsAddress);
            ushort eLineAddress = ReadWord(machine, EditLineAddress);
            if (TryReadBasicNumericVariableFromRange(machine, targetHeader, varsAddress, eLineAddress, out value))
                return true;

            if (machine.TryGetPendingMountedLoadBasicVariableSnapshot(out ushort preservedVarsAddress, out byte[] preservedVariableData))
            {
                if (TryReadBasicNumericVariableFromSnapshot(
                    machine,
                    targetHeader,
                    preservedVarsAddress,
                    preservedVariableData,
                    out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadMountedContinuationNumericVariable(
            Spectrum128Machine machine,
            string variableName,
            out int value)
        {
            return TryReadLiveBasicNumericVariable(machine, variableName, out value);
        }

        private static bool TryReadBasicNumericVariableFromSnapshot(
            Spectrum128Machine machine,
            byte targetHeader,
            ushort varsAddress,
            byte[] variableData,
            out int value)
        {
            value = 0;
            if (variableData.Length == 0)
                return false;

            int currentOffset = 0;
            while (currentOffset < variableData.Length)
            {
                byte header = variableData[currentOffset];
                if (header == 0x80)
                    return false;

                int entryLength = GetBasicVariableEntryLength(variableData, currentOffset);
                if (entryLength <= 0 || currentOffset + entryLength > variableData.Length)
                    return false;

                if ((header & 0xE0) == 0x60 && header == targetHeader)
                {
                    ushort entryAddress = (ushort)(varsAddress + currentOffset + 1);
                    return TryReadSpectrumSmallIntegerFromSnapshot(
                        machine,
                        entryAddress,
                        variableData,
                        currentOffset + 1,
                        out value);
                }

                currentOffset += entryLength;
            }

            return false;
        }

        private static bool TryReadBasicNumericVariableFromRange(
            Spectrum128Machine machine,
            byte targetHeader,
            ushort varsAddress,
            ushort eLineAddress,
            out int value)
        {
            value = 0;
            if (varsAddress >= eLineAddress)
                return false;

            ushort current = varsAddress;
            while (current < eLineAddress)
            {
                byte header = machine.PeekMemory(current);
                if (header == 0x80)
                    return false;

                int entryLength = GetLiveBasicVariableEntryLength(machine, current, eLineAddress);
                if (entryLength <= 0 || current + entryLength > eLineAddress)
                    return false;

                if ((header & 0xE0) == 0x60 && header == targetHeader)
                    return TryReadSpectrumSmallInteger(machine, (ushort)(current + 1), out value);

                current = (ushort)(current + entryLength);
            }

            return false;
        }

        private static int GetBasicVariableEntryLength(byte[] variableData, int offset)
        {
            if ((uint)offset >= (uint)variableData.Length)
                return 0;

            byte header = variableData[offset];
            if (header == 0x80)
                return 1;

            switch (header & 0xE0)
            {
                case 0x40:
                {
                    if (offset + 2 >= variableData.Length)
                        return 0;
                    ushort length = (ushort)(variableData[offset + 1] | (variableData[offset + 2] << 8));
                    return 3 + length;
                }

                case 0x60:
                    return 6;

                case 0x80:
                case 0xC0:
                {
                    if (offset + 2 >= variableData.Length)
                        return 0;
                    ushort length = (ushort)(variableData[offset + 1] | (variableData[offset + 2] << 8));
                    return 3 + length;
                }

                case 0xA0:
                {
                    int current = offset + 1;
                    while (current < variableData.Length)
                    {
                        byte nameByte = variableData[current];
                        current++;
                        if ((nameByte & 0x80) != 0)
                        {
                            if (current + 5 > variableData.Length)
                                return 0;
                            return current - offset + 5;
                        }
                    }

                    return 0;
                }

                case 0xE0:
                    return 19;

                default:
                    return 0;
            }
        }

        private static bool TryReadSpectrumSmallIntegerFromSnapshot(
            Spectrum128Machine machine,
            ushort baseAddress,
            byte[] variableData,
            int offset,
            out int value)
        {
            value = 0;
            if (offset + 4 >= variableData.Length)
                return false;

            byte exponent = variableData[offset];
            byte sign = variableData[offset + 1];
            byte low = variableData[offset + 2];
            byte high = variableData[offset + 3];
            byte trailing = variableData[offset + 4];

            if (TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value))
                return true;

            return TryReadSpectrumSmallInteger(machine, baseAddress, out value);
        }

        private static int GetLiveBasicVariableEntryLength(
            Spectrum128Machine machine,
            ushort address,
            ushort eLineAddress)
        {
            if (address >= eLineAddress)
                return 0;

            byte header = machine.PeekMemory(address);
            switch (header & 0xE0)
            {
                case 0x20:
                    return 6;

                case 0x40:
                case 0x80:
                {
                    if (address + 2 >= eLineAddress)
                        return 0;
                    ushort length = ReadWord(machine, (ushort)(address + 1));
                    return 3 + length;
                }

                case 0x60:
                    return 6;

                case 0xA0:
                {
                    ushort current = (ushort)(address + 1);
                    while (current < eLineAddress)
                    {
                        byte nameByte = machine.PeekMemory(current);
                        current++;
                        if ((nameByte & 0x80) != 0)
                        {
                            if (current + 5 > eLineAddress)
                                return 0;
                            return current - address + 5;
                        }
                    }

                    return 0;
                }

                case 0xE0:
                    return 19;

                default:
                    return 0;
            }
        }

        private static bool TryReadSpectrumSmallInteger(
            Spectrum128Machine machine,
            ushort address,
            out int value)
        {
            value = 0;
            if (address + 4 >= 65536)
                return false;

            byte exponent = machine.PeekMemory(address);
            byte sign = machine.PeekMemory((ushort)(address + 1));
            byte low = machine.PeekMemory((ushort)(address + 2));
            byte high = machine.PeekMemory((ushort)(address + 3));
            byte trailing = machine.PeekMemory((ushort)(address + 4));
            return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
        }

        private static bool TryDecodeSpectrumNumericValue(
            byte exponent,
            byte signOrMantissa,
            byte third,
            byte fourth,
            byte fifth,
            out int value)
        {
            value = 0;

            if (exponent == 0x00)
            {
                if (fifth != 0x00)
                    return false;

                short signedValue = unchecked((short)(third | (fourth << 8)));
                value = signOrMantissa == 0xFF ? -Math.Abs(signedValue) : signedValue;
                return true;
            }

            uint mantissa =
                (uint)(((signOrMantissa & 0x7F) | 0x80) << 24) |
                (uint)(third << 16) |
                (uint)(fourth << 8) |
                fifth;
            int sign = (signOrMantissa & 0x80) != 0 ? -1 : 1;
            double numericValue = sign * mantissa * Math.Pow(2.0, exponent - 160);
            double roundedValue = Math.Round(numericValue);
            if (roundedValue < int.MinValue || roundedValue > int.MaxValue)
                return false;

            if (Math.Abs(numericValue - roundedValue) > 1e-6)
                return false;

            value = (int)roundedValue;
            return true;
        }

        private static void WriteWord(Spectrum128Machine machine, ushort address, ushort value)
        {
            machine.PokeMemory(address, (byte)(value & 0xFF));
            machine.PokeMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        private sealed class BasicBootstrapExecutor
        {
            private readonly Spectrum128Machine machine;
            private List<BasicLine> lines;
            private Queue<int> dataValues;
            private readonly Dictionary<string, int> variables = new(StringComparer.OrdinalIgnoreCase);
            private readonly bool ignoreLoadStatements;
            private bool restoreInterpreterWorkspaceOnExit = true;
            public int IgnoredLoadCount { get; private set; }
            public int ConsumedMountedLoadCount { get; private set; }

            public BasicBootstrapExecutor(Spectrum128Machine machine, ushort programStart, ushort programLength, bool ignoreLoadStatements)
            {
                this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
                this.ignoreLoadStatements = ignoreLoadStatements;
                lines = ParseLines(machine, programStart, programLength);
                dataValues = new Queue<int>(CollectDataValues());
            }

            public void Execute(ushort autoStartLine)
            {
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : lines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return;

                var forStack = new Stack<ForFrame>();
                int lineIndex = startLineIndex;
                int statementIndex = 0;
                int stepCount = 0;

                while (lineIndex >= 0 && lineIndex < lines.Count && stepCount++ < 10000)
                {
                    BasicLine line = lines[lineIndex];
                    if (statementIndex >= line.Statements.Count)
                    {
                        lineIndex++;
                        statementIndex = 0;
                        continue;
                    }

                    List<string> statement = line.Statements[statementIndex];
                    statementIndex++;
                    if (statement.Count == 0)
                        continue;

                    UpdateExecutionContext(line.Number, statementIndex);

                    string keyword = NormalizeStatementKeyword(statement[0]);
                    switch (keyword)
                    {
                        case "REM":
                            statementIndex = line.Statements.Count;
                            break;

                        case "BORDER":
                        case "PAPER":
                        case "INK":
                        case "CLS":
                            ApplyDisplaySideEffects();
                            break;

                        case "?":
                            ApplyPrintSideEffects(line, statementIndex - 1, statement);
                            break;

                        case "LOAD":
                            if (ignoreLoadStatements)
                            {
                                IgnoredLoadCount++;
                            }
                            else
                            {
                                BootstrapTapeLoadResult loadResult = machine.TryConsumeBootstrapTapeLoad();
                                if (!loadResult.Success)
                                    throw new InvalidOperationException("BASIC LOAD could not consume a mounted tape block during bootstrap.");
                                ConsumedMountedLoadCount++;

                                if (loadResult.LoadedBasicProgram &&
                                    TryExecuteImmediateSideEffectProgram(
                                        loadResult.LoadedProgramLength,
                                        loadResult.LoadedDataLength,
                                        loadResult.LoadedAutoStartLine))
                                {
                                    break;
                                }
                                else if (loadResult.LoadedBasicProgram &&
                                         TryAdoptLoadedProgram(
                                             loadResult.LoadedProgramLength,
                                             loadResult.LoadedDataLength,
                                             loadResult.LoadedAutoStartLine,
                                             out int adoptedLineIndex))
                                {
                                    forStack.Clear();
                                    variables.Clear();
                                    lineIndex = adoptedLineIndex;
                                    statementIndex = 0;
                                }
                                else if (loadResult.LoadedBasicProgram)
                                {
                                    return;
                                }
                            }
                            break;

                        case "CLEAR":
                        {
                            int clearAddress = EvaluateExpression(statement, 1, statement.Count - 1);
                            ApplyClear(clearAddress);
                            break;
                        }

                        case "POKE":
                        {
                            int commaIndex = statement.IndexOf(",");
                            if (commaIndex <= 1)
                                throw new InvalidOperationException("Malformed BASIC POKE statement in tape bootstrap.");

                            int address = EvaluateExpression(statement, 1, commaIndex - 1);
                            int value = EvaluateExpression(statement, commaIndex + 1, statement.Count - 1);
                            machine.PokeMemory((ushort)address, (byte)value);
                            if (address == Spectrum128TapeLoadBankSelectAddress && (value & 0xF8) == 0x10)
                                machine.ForceApply7ffdValue((byte)value);
                            break;
                        }

                        case "DATA":
                            break;

                        case "RESTORE":
                            RestoreDataValues(statement);
                            break;

                        case "READ":
                        {
                            if (statement.Count < 2)
                                throw new InvalidOperationException("Malformed BASIC READ statement in tape bootstrap.");

                            for (int tokenIndex = 1; tokenIndex < statement.Count; tokenIndex++)
                            {
                                string token = statement[tokenIndex];
                                if (token == ",")
                                    continue;

                                if (dataValues.Count == 0)
                                    throw new InvalidOperationException("BASIC READ exhausted DATA values during tape bootstrap.");

                                variables[token] = dataValues.Dequeue();
                            }
                            break;
                        }

                        case "FOR":
                        {
                            int equalsIndex = statement.IndexOf("=");
                            int toIndex = statement.IndexOf("TO");
                            if (statement.Count < 5 || equalsIndex != 2 || toIndex < 4)
                                throw new InvalidOperationException("Malformed BASIC FOR statement in tape bootstrap.");

                            string variableName = statement[1];
                            int startValue = EvaluateExpression(statement, 3, toIndex - 1);
                            int endValue = EvaluateExpression(statement, toIndex + 1, statement.Count - 1);
                            variables[variableName] = startValue;
                            if (startValue > endValue)
                            {
                                SkipLoopBody(ref lineIndex, ref statementIndex);
                                break;
                            }

                            forStack.Push(new ForFrame(variableName, endValue, lineIndex, statementIndex));
                            break;
                        }

                        case "NEXT":
                        {
                            if (forStack.Count == 0)
                                throw new InvalidOperationException("BASIC NEXT encountered without a matching FOR during tape bootstrap.");

                            string variableName = statement.Count > 1 ? statement[1] : forStack.Peek().VariableName;
                            ForFrame frame = forStack.Peek();
                            if (!string.Equals(variableName, frame.VariableName, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("Nested BASIC FOR/NEXT mismatch during tape bootstrap.");

                            int nextValue = variables.GetValueOrDefault(frame.VariableName) + 1;
                            variables[frame.VariableName] = nextValue;
                            if (nextValue <= frame.EndValue)
                            {
                                lineIndex = frame.LineIndex;
                                statementIndex = frame.StatementIndex;
                            }
                            else
                            {
                                forStack.Pop();
                            }
                            break;
                        }

                        case "RANDOMIZE":
                        {
                            int usrIndex = statement.IndexOf("USR");
                            if (usrIndex >= 0 && usrIndex + 1 < statement.Count)
                            {
                                int entryPoint = EvaluateExpression(statement, usrIndex + 1, statement.Count - 1);
                                EnterMachineCode((ushort)entryPoint);
                                return;
                            }
                            break;
                        }
                    }
                }

                if (restoreInterpreterWorkspaceOnExit)
                    RestoreInterpreterWorkspaceAfterImmediateProgram();
            }

            private void RestoreDataValues(List<string> statement)
            {
                int? restoreLineNumber = null;
                if (statement.Count > 1)
                    restoreLineNumber = EvaluateExpression(statement, 1, statement.Count - 1);

                dataValues = new Queue<int>(CollectDataValues(restoreLineNumber));
            }

            private void SkipLoopBody(ref int lineIndex, ref int statementIndex)
            {
                int nestedLoopDepth = 0;

                for (int scanLineIndex = lineIndex; scanLineIndex < lines.Count; scanLineIndex++)
                {
                    BasicLine scanLine = lines[scanLineIndex];
                    int scanStatementIndex = scanLineIndex == lineIndex ? statementIndex : 0;

                    for (; scanStatementIndex < scanLine.Statements.Count; scanStatementIndex++)
                    {
                        List<string> statement = scanLine.Statements[scanStatementIndex];
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        switch (keyword)
                        {
                            case "REM":
                                scanStatementIndex = scanLine.Statements.Count;
                                break;

                            case "FOR":
                                nestedLoopDepth++;
                                break;

                            case "NEXT":
                                if (nestedLoopDepth == 0)
                                {
                                    lineIndex = scanLineIndex;
                                    statementIndex = scanStatementIndex + 1;
                                    return;
                                }

                                nestedLoopDepth--;
                                break;
                        }
                    }
                }

                lineIndex = lines.Count;
                statementIndex = 0;
            }

            private void EnterMachineCode(ushort entryPoint)
            {
                // Match the ROM's USR entry shape closely: on entry the machine-code
                // routine sees STACK-BC as its return address, and successful returns
                // back to BASIC expect H'L' to still reference end-calc.
                machine.Cpu.Regs.SP -= 2;
                WriteWord(machine, machine.Cpu.Regs.SP, UsrReturnAddress);
                machine.Cpu.Regs.BC = entryPoint;
                machine.Cpu.Regs.H_ = (byte)(EndCalcLiteralAddress >> 8);
                machine.Cpu.Regs.L_ = (byte)(EndCalcLiteralAddress & 0xFF);
                machine.Cpu.Regs.PC = entryPoint;
            }

            private void UpdateExecutionContext(ushort lineNumber, int statementOrdinal)
            {
                WriteWord(machine, NewPpcAddress, lineNumber);
                machine.PokeMemory(NspPcAddress, (byte)Math.Clamp(statementOrdinal, 0, 255));
            }

            private void UpdateInterpreterPointersForPrint()
            {
                ushort eLine = ReadWord(machine, EditLineAddress);
                ushort channelDescriptor = ScreenChannelDescriptorAddress;
                WriteWord(machine, CurChlAddress, channelDescriptor);
                WriteWord(machine, KCurAddress, eLine);
                WriteWord(machine, ChAddAddress, eLine);
            }

            private void UpdateInterpreterPointersForDisplay()
            {
                ushort eLine = ReadWord(machine, EditLineAddress);
                WriteWord(machine, CurChlAddress, ScreenChannelDescriptorAddress);
                WriteWord(machine, KCurAddress, eLine);
                WriteWord(machine, ChAddAddress, eLine);
            }

            private void ApplyPrintSideEffects(BasicLine line, int statementIndex, List<string> statement)
            {
                UpdateInterpreterPointersForPrint();
                ApplyDisplaySideEffects();

                if (statementIndex >= 0 && statementIndex < line.StatementByteOffsets.Count)
                {
                    ushort xPtr = (ushort)(line.DataAddress + line.StatementByteOffsets[statementIndex] + 1);
                    WriteWord(machine, XPtrAddress, xPtr);
                }

                ushort eLine = ReadWord(machine, EditLineAddress);
                string printable = ExtractPrintableText(statement);
                int printableLength = Math.Clamp(printable.Length, 0, 255);

                for (int i = 0; i < printableLength; i++)
                    machine.PokeMemory((ushort)(eLine + i), EncodeSpectrumPrintChar(printable[i]));

                machine.PokeMemory((ushort)(eLine + printableLength), 0x0D);

                ushort workspace = (ushort)(eLine + printableLength + 1);
                WriteWord(machine, WorkspaceAddress, workspace);
                WriteWord(machine, StackBottomAddress, workspace);
                WriteWord(machine, StackEndAddress, workspace);
            }

            private static string ExtractPrintableText(List<string> statement)
            {
                if (statement.Count == 0)
                    return string.Empty;

                string first = statement[0];
                if (!string.IsNullOrEmpty(first) && first[0] == '?')
                {
                    string inline = first.Length > 1 ? first[1..] : string.Empty;
                    return inline + string.Concat(statement.Skip(1));
                }

                return string.Concat(statement);
            }

            private static byte EncodeSpectrumPrintChar(char ch)
            {
                if (ch >= 32 && ch <= 126)
                    return (byte)ch;

                return ch switch
                {
                    '\r' => 0x0D,
                    '\n' => 0x0D,
                    _ => (byte)'?'
                };
            }

            private void ApplyDisplaySideEffects()
            {
                machine.PokeMemory(FlagsSystemVariableAddress, (byte)(machine.PeekMemory(FlagsSystemVariableAddress) | 0x20));
                machine.PokeMemory(TvFlagSystemVariableAddress, (byte)(machine.PeekMemory(TvFlagSystemVariableAddress) | 0x20));
                UpdateInterpreterPointersForDisplay();
            }

            private static ushort ClampClearTopAddress(int clearAddress)
            {
                // Spectrum BASIC uses CLEAR -1 as the conventional spelling for
                // "all available RAM". Clamp it after applying that signed value.
                if (clearAddress < 0)
                    return 0xFFFE;

                return (ushort)Math.Clamp(clearAddress, 0x5D00, 0xFFFE);
            }

            private static ushort GetWorkspaceAddressForClear(ushort top)
            {
                return (ushort)(top + 1);
            }

            private static ushort GetMachineStackPointerForClear(ushort top)
            {
                return top >= DefaultStackPointer ? DefaultStackPointer : top;
            }

            private static bool ShouldRelocateMachineStackAfterClear(Spectrum128Machine machine, ushort top)
            {
                return machine.Cpu.Regs.SP > top || machine.Cpu.Regs.SP < 0x8000;
            }

            private bool TryAdoptLoadedProgram(
                ushort loadedProgramLength,
                ushort loadedDataLength,
                ushort loadedAutoStartLine,
                out int adoptedLineIndex)
            {
                adoptedLineIndex = -1;
                if (loadedProgramLength == 0)
                    return false;

                List<BasicLine> loadedLines = ParseLines(machine, BasicProgramStart, loadedProgramLength);
                if (loadedLines.Count == 0 ||
                    (!CanExecuteProgram(loadedLines, loadedAutoStartLine) &&
                     !CanExecuteProtectedProgram(loadedLines, loadedAutoStartLine)))
                    return false;

                int startLineIndex = loadedAutoStartLine == 0
                    ? 0
                    : loadedLines.FindIndex(line => line.Number == loadedAutoStartLine);
                if (startLineIndex < 0)
                    return false;

                PrepareLoadedProgramExecutionContext(loadedProgramLength, loadedDataLength);
                lines = loadedLines;
                dataValues = new Queue<int>(CollectDataValues());
                adoptedLineIndex = startLineIndex;
                return true;
            }

            internal bool TryExecuteImmediateSideEffectProgram(
                ushort loadedProgramLength,
                ushort loadedDataLength,
                ushort loadedAutoStartLine,
                bool preserveProtectedInterpreterHandoffCursor = true)
            {
                if (loadedProgramLength == 0)
                    return false;

                List<BasicLine> loadedLines = ParseLines(machine, BasicProgramStart, loadedProgramLength);
                int startLineIndex = loadedAutoStartLine == 0
                    ? 0
                    : loadedLines.FindIndex(line => line.Number == loadedAutoStartLine);
                if (startLineIndex < 0 || !CanExecuteImmediateSideEffectProgram(loadedLines, startLineIndex))
                    return false;

                bool preservesInterpreterHandoff =
                    preserveProtectedInterpreterHandoffCursor &&
                    TouchesProtectedInterpreterHandoff(loadedLines, startLineIndex);
                ushort liveXPtr = ReadWord(machine, XPtrAddress);
                PrepareLoadedProgramExecutionContext(loadedProgramLength, loadedDataLength);
                if (preservesInterpreterHandoff)
                    WriteWord(machine, XPtrAddress, liveXPtr);
                var savedVariables = new Dictionary<string, int>(variables, StringComparer.OrdinalIgnoreCase);
                variables.Clear();
                try
                {
                    ExecuteImmediateSideEffectStatements(loadedLines, startLineIndex);
                    restoreInterpreterWorkspaceOnExit = !preservesInterpreterHandoff;
                    if (!preservesInterpreterHandoff)
                        RestoreInterpreterWorkspaceAfterImmediateProgram();
                    return true;
                }
                finally
                {
                    variables.Clear();
                    foreach (var pair in savedVariables)
                        variables[pair.Key] = pair.Value;
                }
            }

            private void PrepareLoadedProgramExecutionContext(
                ushort loadedProgramLength,
                ushort loadedDataLength)
            {
                ushort programStart = BasicProgramStart;
                ushort varsAddress = (ushort)(programStart + loadedProgramLength);
                ushort endAddress = (ushort)(programStart + loadedDataLength);

                WriteWord(machine, ProgAddress, programStart);
                WriteWord(machine, VarsAddress, varsAddress);
                WriteWord(machine, NextLineAddress, programStart);
                WriteWord(machine, DataAddress, programStart);
                WriteWord(machine, EditLineAddress, endAddress);
                WriteWord(machine, WorkspaceAddress, endAddress);
                WriteWord(machine, StackBottomAddress, endAddress);
                WriteWord(machine, StackEndAddress, endAddress);
                InitializeInterpreterPointersForLoadedProgram(machine, endAddress);
            }

            private void RestoreInterpreterWorkspaceAfterImmediateProgram()
            {
                ushort eLine = ReadWord(machine, EditLineAddress);
                machine.PokeMemory(eLine, 0x0D);
                machine.PokeMemory((ushort)(eLine + 1), 0x00);
                WriteWord(machine, WorkspaceAddress, eLine);
                WriteWord(machine, StackBottomAddress, eLine);
                WriteWord(machine, StackEndAddress, eLine);
                InitializeInterpreterPointersForLoadedProgram(machine, eLine);
            }

            private static bool CanExecuteImmediateSideEffectProgram(List<BasicLine> parsedLines, int startLineIndex)
            {
                bool sawExecutableSideEffect = false;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        if (IsIgnorableProtectedDecorationStatement(statement))
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "?" || keyword == "REM")
                            continue;

                        switch (keyword)
                        {
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "CLEAR":
                            case "POKE":
                                sawExecutableSideEffect = true;
                                continue;
                        }

                        return false;
                    }
                }

                return sawExecutableSideEffect;
            }

            private static bool TouchesProtectedInterpreterHandoff(List<BasicLine> parsedLines, int startLineIndex)
            {
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0 || NormalizeStatementKeyword(statement[0]) != "POKE")
                            continue;

                        string rendered = string.Join(" ", statement);
                        if (rendered.Contains("PEEK 23641", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23642", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23633", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23634", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23618", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23619", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23621", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23647", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23648", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23649", StringComparison.OrdinalIgnoreCase) ||
                            rendered.Contains("PEEK 23650", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            private void ExecuteImmediateSideEffectStatements(List<BasicLine> parsedLines, int startLineIndex)
            {
                int stepCount = 0;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count && stepCount++ < 10000; lineIndex++)
                {
                    BasicLine line = parsedLines[lineIndex];
                    for (int statementIndex = 0; statementIndex < line.Statements.Count; statementIndex++)
                    {
                        List<string> statement = line.Statements[statementIndex];
                        if (statement.Count == 0)
                            continue;

                        UpdateExecutionContext(line.Number, statementIndex + 1);
                        if (IsIgnorableProtectedDecorationStatement(statement))
                        {
                            ApplyDisplaySideEffects();
                            continue;
                        }

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        switch (keyword)
                        {
                            case "REM":
                                goto NextLine;

                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                                ApplyDisplaySideEffects();
                                break;

                            case "?":
                                ApplyPrintSideEffects(line, statementIndex, statement);
                                break;

                            case "CLEAR":
                            {
                                int clearAddress = EvaluateExpression(statement, 1, statement.Count - 1);
                                ApplyClear(clearAddress);
                                break;
                            }

                            case "POKE":
                            {
                                int commaIndex = statement.IndexOf(",");
                                if (commaIndex <= 1)
                                    throw new InvalidOperationException("Malformed BASIC POKE statement in protected bootstrap stage.");

                                int address = EvaluateExpression(statement, 1, commaIndex - 1);
                                int value = EvaluateExpression(statement, commaIndex + 1, statement.Count - 1);
                                machine.PokeMemory((ushort)address, (byte)value);
                                if (address == Spectrum128TapeLoadBankSelectAddress && (value & 0xF8) == 0x10)
                                    machine.ForceApply7ffdValue((byte)value);
                                break;
                            }

                            default:
                                throw new InvalidOperationException($"Unsupported BASIC statement '{keyword}' in protected bootstrap stage.");
                        }
                    }

                NextLine:
                    continue;
                }
            }

            private static bool IsIgnorableProtectedDecorationStatement(List<string> statement)
            {
                if (statement.Count == 0)
                    return true;

                foreach (string token in statement)
                {
                    if (IsKnownBasicKeywordToken(token))
                        return false;

                    string keyword = NormalizeStatementKeyword(token);
                    if (keyword is "?" or "REM" or "BORDER" or "PAPER" or "INK" or "CLS" or "CLEAR" or "POKE")
                        return false;

                    if (token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                        return false;

                    for (int i = 0; i < token.Length; i++)
                    {
                        char c = token[i];
                        if (!IsAsciiLetterOrDigit(c))
                            return false;
                    }
                }

                return true;
            }

            private static bool IsKnownBasicKeywordToken(string token)
            {
                string keyword = NormalizeStatementKeyword(token);
                return keyword is
                    "?" or "REM" or "BORDER" or "PAPER" or "INK" or "CLS" or "CLEAR" or
                    "POKE" or "LOAD" or "DATA" or "READ" or "FOR" or "NEXT" or
                    "RANDOMIZE" or "PAUSE" or "RETURN" or "RESTORE" or "USR" or
                    "LET" or "THEN" or
                    "CODE" or "PEEK" or "TO" or "PRINT";
            }

            private static bool CanExecuteProgram(List<BasicLine> parsedLines, ushort autoStartLine)
            {
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawExecutableStatement = false;

                // Repeated or non-monotonic line numbers usually mean a protected or
                // interpreter-sensitive chained loader stage. Let the real ROM path
                // continue with those instead of trying to fake-execute them.
                for (int lineIndex = startLineIndex + 1; lineIndex < parsedLines.Count; lineIndex++)
                {
                    if (parsedLines[lineIndex].Number <= parsedLines[lineIndex - 1].Number)
                        return false;
                }

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "?" || keyword == "REM")
                            continue;

                        switch (keyword)
                        {
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "LOAD":
                            case "CLEAR":
                            case "POKE":
                            case "DATA":
                            case "READ":
                            case "RESTORE":
                            case "FOR":
                            case "NEXT":
                            case "RANDOMIZE":
                                sawExecutableStatement = true;
                                continue;
                        }

                        return false;
                    }
                }

                return sawExecutableStatement;
            }

            private static bool CanExecuteProtectedProgram(List<BasicLine> parsedLines, ushort autoStartLine)
            {
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "?" || keyword == "REM")
                            continue;

                        switch (keyword)
                        {
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "LOAD":
                            case "CLEAR":
                            case "POKE":
                            case "DATA":
                            case "READ":
                            case "FOR":
                            case "NEXT":
                            case "RANDOMIZE":
                                continue;
                        }

                        return false;
                    }
                }

                return true;
            }

            private void ApplyClear(int clearAddress)
            {
                ushort top = ClampClearTopAddress(clearAddress);
                ushort workspace = GetWorkspaceAddressForClear(top);
                WriteWord(machine, RamTopAddress, top);
                WriteWord(machine, PhysicalRamTopAddress, top);
                WriteWord(machine, WorkspaceAddress, workspace);
                WriteWord(machine, StackBottomAddress, workspace);
                WriteWord(machine, StackEndAddress, workspace);
                if (ShouldRelocateMachineStackAfterClear(machine, top))
                    machine.Cpu.Regs.SP = GetMachineStackPointerForClear(top);
            }

            private IEnumerable<int> CollectDataValues(int? restoreLineNumber = null)
            {
                bool collecting = restoreLineNumber == null;
                foreach (BasicLine line in lines)
                {
                    if (!collecting && restoreLineNumber.HasValue && line.Number == restoreLineNumber.Value)
                        collecting = true;

                    if (!collecting)
                        continue;

                    foreach (List<string> statement in line.Statements)
                    {
                        if (statement.Count == 0 || statement[0] != "DATA")
                            continue;

                        int itemStart = 1;
                        while (itemStart < statement.Count)
                        {
                            int itemEnd = itemStart;
                            while (itemEnd < statement.Count && statement[itemEnd] != ",")
                                itemEnd++;

                            if (itemEnd > itemStart)
                                yield return EvaluateExpression(statement, itemStart, itemEnd - 1);

                            itemStart = itemEnd + 1;
                        }
                    }
                }
            }

            private int EvaluateExpression(List<string> tokens, int startIndex, int endIndex)
            {
                var parser = new ExpressionParser(machine, variables, tokens, startIndex, endIndex);
                return parser.Parse();
            }

            private static List<BasicLine> ParseLines(Spectrum128Machine machine, ushort programStart, ushort programLength)
            {
                var parsedLines = new List<BasicLine>();
                ushort cursor = programStart;
                ushort endAddress = (ushort)(programStart + programLength);

                while (cursor < endAddress)
                {
                    ushort lineNumber = (ushort)((machine.PeekMemory(cursor) << 8) | machine.PeekMemory((ushort)(cursor + 1)));
                    ushort lineLength = ReadWord(machine, (ushort)(cursor + 2));
                    ushort lineDataAddress = (ushort)(cursor + 4);
                    ushort nextLineAddress = (ushort)(lineDataAddress + lineLength);
                    if (nextLineAddress > endAddress)
                        break;

                    byte[] lineBytes = new byte[lineLength];
                    for (int i = 0; i < lineLength; i++)
                        lineBytes[i] = machine.PeekMemory((ushort)(lineDataAddress + i));

                    (List<List<string>> statements, List<int> statementByteOffsets, List<List<TokenSpan>> statementTokenSpans) = SplitStatements(Tokenize(lineBytes));
                    parsedLines.Add(new BasicLine(lineNumber, statements, statementByteOffsets, statementTokenSpans, lineDataAddress));
                    cursor = nextLineAddress;
                }

                return parsedLines;
            }

            private static (List<List<string>> Statements, List<int> StatementByteOffsets, List<List<TokenSpan>> StatementTokenSpans) SplitStatements(List<TokenSpan> tokens)
            {
                var statements = new List<List<string>>();
                var statementByteOffsets = new List<int>();
                var statementTokenSpans = new List<List<TokenSpan>>();
                var current = new List<string>();
                var currentTokenSpans = new List<TokenSpan>();
                int currentByteOffset = 0;
                bool sawTokenInStatement = false;
                foreach (TokenSpan token in tokens)
                {
                    if (token.Text == ":")
                    {
                        statements.Add(current);
                        statementByteOffsets.Add(currentByteOffset);
                        statementTokenSpans.Add(currentTokenSpans);
                        current = new List<string>();
                        currentTokenSpans = new List<TokenSpan>();
                        currentByteOffset = 0;
                        sawTokenInStatement = false;
                    }
                    else
                    {
                        if (!sawTokenInStatement)
                        {
                            currentByteOffset = token.ByteOffset;
                            sawTokenInStatement = true;
                        }

                        current.Add(token.Text);
                        currentTokenSpans.Add(token);
                    }
                }

                statements.Add(current);
                statementByteOffsets.Add(currentByteOffset);
                statementTokenSpans.Add(currentTokenSpans);
                return (statements, statementByteOffsets, statementTokenSpans);
            }

            private static List<TokenSpan> Tokenize(byte[] lineBytes)
            {
                var tokens = new List<TokenSpan>();
                for (int i = 0; i < lineBytes.Length; i++)
                {
                    byte b = lineBytes[i];
                    if (b == 0x0D)
                        break;

                    if (b == 0x0E)
                    {
                        i += 5;
                        continue;
                    }

                    if (TryGetKeywordToken(b, out string? keyword))
                    {
                        tokens.Add(new TokenSpan(keyword!, i));
                        continue;
                    }

                    char c = (char)b;
                    if (IsAsciiDigit(c))
                    {
                        int start = i;
                        while (i + 1 < lineBytes.Length && IsAsciiDigit((char)lineBytes[i + 1]))
                            i++;

                        string numericToken = System.Text.Encoding.ASCII.GetString(lineBytes, start, i - start + 1);
                        if (TryDecodeSpectrumNumberMarker(lineBytes, i + 1, out int encodedValue))
                        {
                            numericToken = encodedValue.ToString();
                            i += SpectrumNumberMarkerLength;
                        }

                        tokens.Add(new TokenSpan(numericToken, start));
                        continue;
                    }

                    if (IsAsciiLetter(c))
                    {
                        int start = i;
                        while (i + 1 < lineBytes.Length && IsAsciiLetterOrDigit((char)lineBytes[i + 1]))
                            i++;
                        tokens.Add(new TokenSpan(System.Text.Encoding.ASCII.GetString(lineBytes, start, i - start + 1), start));
                        continue;
                    }

                    if (c == '"')
                    {
                        int start = ++i;
                        while (i < lineBytes.Length && (char)lineBytes[i] != '"')
                            i++;
                        tokens.Add(new TokenSpan(System.Text.Encoding.ASCII.GetString(lineBytes, start, Math.Max(0, i - start)), start));
                        continue;
                    }

                    if ("()+-*=,:".IndexOf(c) >= 0)
                        tokens.Add(new TokenSpan(c.ToString(), i));
                }

                return tokens;
            }

            private const int SpectrumNumberMarkerLength = 5;

            private static bool TryDecodeSpectrumNumberMarker(byte[] lineBytes, int markerOffset, out int value)
            {
                value = 0;
                if (markerOffset < 0 ||
                    markerOffset + SpectrumNumberMarkerLength >= lineBytes.Length ||
                    lineBytes[markerOffset] != 0x0E)
                {
                    return false;
                }

                byte exponent = lineBytes[markerOffset + 1];
                byte sign = lineBytes[markerOffset + 2];
                byte low = lineBytes[markerOffset + 3];
                byte high = lineBytes[markerOffset + 4];
                byte trailing = lineBytes[markerOffset + 5];

                // Small integers are encoded as an exponent byte of zero followed by a
                // sign byte, the 16-bit integer payload, and a zero terminator.
                if (exponent == 0x00 && trailing == 0x00)
                {
                    short integerValue = unchecked((short)(low | (high << 8)));
                    value = integerValue;
                    return true;
                }

                return false;
            }

            private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

            private static bool IsAsciiLetter(char c) =>
                (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z');

            private static bool IsAsciiLetterOrDigit(char c) => IsAsciiLetter(c) || IsAsciiDigit(c);

            public static bool RequiresMountedLoadSemantics(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawPreparatorySideEffects = false;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        if (keyword == "RANDOMIZE" && statement.IndexOf("USR") >= 0)
                            return false;

                        if (keyword == "LOAD")
                        {
                            if (sawPreparatorySideEffects || !IsSimpleAnonymousCodeLoad(statement))
                                return true;
                            continue;
                        }

                        if (keyword == "POKE" || keyword == "READ" || keyword == "DATA")
                            sawPreparatorySideEffects = true;
                    }
                }

                return false;
            }

            public static bool CanHandleLoadedProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                if (parsedLines.Count == 0)
                    return false;

                if (CanExecuteProgram(parsedLines, autoStartLine) || CanExecuteProtectedProgram(parsedLines, autoStartLine))
                    return true;

                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                return startLineIndex >= 0 && CanExecuteImmediateSideEffectProgram(parsedLines, startLineIndex);
            }

            public static bool CanExecuteImmediateSideEffectProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                return startLineIndex >= 0 && CanExecuteImmediateSideEffectProgram(parsedLines, startLineIndex);
            }

            public static bool ShouldDeferToRomForMountedLoadProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawLoad = false;
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        string keyword = NormalizeStatementKeyword(statement[0]);
                        switch (keyword)
                        {
                            case "REM":
                            case "?":
                            case "BORDER":
                            case "PAPER":
                            case "INK":
                            case "CLS":
                            case "CLEAR":
                                break;

                            case "LOAD":
                                sawLoad = true;
                                break;

                            default:
                                return false;
                        }
                    }
                }

                return sawLoad;
            }

            public static bool RequiresRomDrivenMountedLoadedProgram(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                return TouchesProtectedInterpreterHandoff(parsedLines, startLineIndex);
            }

            public static bool Requires128kTapeLoadMode(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                var variables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var dataValues = new Queue<int>(CollectDataValues(parsedLines, machine, variables));

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    foreach (List<string> statement in parsedLines[lineIndex].Statements)
                    {
                        if (statement.Count == 0)
                            continue;

                        switch (statement[0])
                        {
                            case "READ":
                                if (statement.Count >= 2 && dataValues.Count > 0)
                                    variables[statement[1]] = dataValues.Dequeue();
                                break;

                            case "POKE":
                            {
                                int commaIndex = statement.IndexOf(",");
                                if (commaIndex <= 1)
                                    break;

                                var parser = new ExpressionParser(machine, variables, statement, 1, commaIndex - 1);
                                if (parser.Parse() == Spectrum128TapeLoadBankSelectAddress)
                                    return true;
                                break;
                            }

                            case "RANDOMIZE":
                                if (statement.IndexOf("USR") >= 0)
                                    return false;
                                break;
                        }
                    }
                }

                return false;
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadUsrContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                return CreateMountedLoadUsrContinuationResolver(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    requireUsrReturnAddressBetweenSteps: true);
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadUsrContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                bool requireUsrReturnAddressBetweenSteps = true)
            {
                if (!TryBuildMountedUsrContinuationSteps(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    out List<MountedUsrContinuationStep> continuationSteps))
                {
                    return null;
                }

                if (continuationSteps.Count == 0)
                {
                    if (!TryBuildInitialMountedUsrContinuationStep(
                            machine,
                            programStart,
                            programLength,
                            autoStartLine,
                            out MountedUsrContinuationStep? initialStep) ||
                        initialStep is null)
                    {
                        return null;
                    }

                    continuationSteps.Add((MountedUsrContinuationStep)initialStep);
                }

                return CreateMountedUsrContinuationResolver(
                    continuationSteps,
                    0,
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    programStart,
                    programLength,
                    requireUsrReturnAddressBetweenSteps);
            }

            public static Func<Spectrum128Machine, ushort?>? CreateMountedLoadRomBasicContinuationResolver(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine)
            {
                if (!TryBuildMountedUsrContinuationSteps(
                    machine,
                    programStart,
                    programLength,
                    autoStartLine,
                    out List<MountedUsrContinuationStep> continuationSteps))
                {
                    return null;
                }

                MountedUsrContinuationStep firstStep;
                if (continuationSteps.Count == 0)
                {
                    if (!TryBuildInitialMountedUsrContinuationStep(
                            machine,
                            programStart,
                            programLength,
                            autoStartLine,
                            out MountedUsrContinuationStep? initialStep) ||
                        initialStep is null)
                    {
                        if (!TryBuildInitialRomBasicResumeStep(
                                machine,
                                programStart,
                                programLength,
                                autoStartLine,
                                out MountedUsrContinuationStep? romResumeStep) ||
                            romResumeStep is null)
                        {
                            return null;
                        }

                        firstStep = (MountedUsrContinuationStep)romResumeStep;
                    }
                    else
                    {
                        firstStep = (MountedUsrContinuationStep)initialStep;
                    }
                }
                else
                {
                    firstStep = continuationSteps[0];
                }

                return currentMachine =>
                {
                    UpdateMountedUsrContinuationRomResumeContext(currentMachine, firstStep);
                    currentMachine.SetPendingMountedLoadBasicResume(
                        firstStep.LineNumber,
                        (byte)Math.Clamp(firstStep.StatementIndex, 0, byte.MaxValue));
                    return ushort.MaxValue;
                };
            }

            private static bool TryBuildMountedUsrContinuationSteps(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out List<MountedUsrContinuationStep> continuationSteps)
            {
                continuationSteps = new List<MountedUsrContinuationStep>();
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                bool sawInitialUsr = false;
                List<List<string>> pendingPreamble = new();
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    for (int parsedStatementIndex = 0; parsedStatementIndex < parsedLines[lineIndex].Statements.Count; parsedStatementIndex++)
                    {
                        List<string> statement = parsedLines[lineIndex].Statements[parsedStatementIndex];
                        int usrClauseOrdinal = 0;
                        foreach (List<TokenSpan> clauseTokens in EnumerateUsrContinuationClauses(parsedLines[lineIndex].StatementTokenSpans[parsedStatementIndex]))
                        {
                            if (clauseTokens.Count == 0)
                                continue;

                            List<string> clause = clauseTokens.ConvertAll(token => token.Text);
                            if (!sawInitialUsr)
                            {
                                if (TryGetRandomizeUsrExpressionTokens(clause, out _))
                                {
                                    sawInitialUsr = true;
                                    continue;
                                }

                                if (IsSafeUsrContinuationPreambleStatement(clause))
                                    continue;

                                return false;
                            }

                            if (TryGetRandomizeUsrExpressionInfo(clauseTokens, out List<string> expressionTokens, out int expressionByteOffset))
                            {
                                continuationSteps.Add(new MountedUsrContinuationStep(
                                    parsedLines[lineIndex].Number,
                                    parsedStatementIndex + 1,
                                    usrClauseOrdinal,
                                    parsedLines[lineIndex].StatementByteOffsets[parsedStatementIndex],
                                    clauseTokens[0].ByteOffset,
                                    expressionByteOffset,
                                    parsedLines[lineIndex].DataAddress,
                                    pendingPreamble.Select(step => step.ToArray()).ToArray(),
                                    expressionTokens.ToArray()));
                                usrClauseOrdinal++;
                                pendingPreamble.Clear();
                                continue;
                            }

                            if (IsSafeUsrContinuationPreambleStatement(clause))
                            {
                                pendingPreamble.Add(new List<string>(clause));
                                continue;
                            }

                            return false;
                        }
                    }
                }

                return true;
            }

            private static bool TryBuildInitialMountedUsrContinuationStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out MountedUsrContinuationStep? continuationStep)
            {
                continuationStep = null;
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                var pendingPreamble = new List<List<string>>();
                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    for (int statementIndex = 0; statementIndex < parsedLines[lineIndex].StatementTokenSpans.Count; statementIndex++)
                    {
                        List<TokenSpan> statementTokens = parsedLines[lineIndex].StatementTokenSpans[statementIndex];
                        foreach (List<TokenSpan> clauseTokens in EnumerateUsrContinuationClauses(statementTokens))
                        {
                            List<string> clause = clauseTokens.ConvertAll(token => token.Text);
                            if (TryGetRandomizeUsrExpressionInfo(
                                clauseTokens,
                                out List<string> expressionTokens,
                                out int expressionByteOffset))
                            {
                                continuationStep = new MountedUsrContinuationStep(
                                    parsedLines[lineIndex].Number,
                                    statementIndex,
                                    0,
                                    parsedLines[lineIndex].StatementByteOffsets[statementIndex],
                                    clauseTokens[0].ByteOffset,
                                    expressionByteOffset,
                                    parsedLines[lineIndex].DataAddress,
                                    pendingPreamble.Select(step => step.ToArray()).ToArray(),
                                    expressionTokens.ToArray());
                                return true;
                            }

                            if (IsSafeUsrContinuationPreambleStatement(clause))
                            {
                                pendingPreamble.Add(new List<string>(clause));
                                continue;
                            }

                            return false;
                        }
                    }
                }

                return false;
            }

            private static bool TryBuildInitialRomBasicResumeStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                ushort autoStartLine,
                out MountedUsrContinuationStep? continuationStep)
            {
                continuationStep = null;
                if (programLength == 0)
                    return false;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int startLineIndex = autoStartLine == 0
                    ? 0
                    : parsedLines.FindIndex(line => line.Number == autoStartLine);
                if (startLineIndex < 0)
                    return false;

                for (int lineIndex = startLineIndex; lineIndex < parsedLines.Count; lineIndex++)
                {
                    if (parsedLines[lineIndex].StatementTokenSpans.Count == 0)
                        continue;

                    for (int statementIndex = 0; statementIndex < parsedLines[lineIndex].StatementTokenSpans.Count; statementIndex++)
                    {
                        List<TokenSpan> statementTokens = parsedLines[lineIndex].StatementTokenSpans[statementIndex];
                        if (statementTokens.Count == 0)
                            continue;

                        continuationStep = new MountedUsrContinuationStep(
                            parsedLines[lineIndex].Number,
                            statementIndex,
                            0,
                            parsedLines[lineIndex].StatementByteOffsets[statementIndex],
                            statementTokens[0].ByteOffset,
                            statementTokens[0].ByteOffset,
                            parsedLines[lineIndex].DataAddress,
                            Array.Empty<string[]>(),
                            Array.Empty<string>());
                        return true;
                    }
                }

                return false;
            }

            private static Func<Spectrum128Machine, ushort?> CreateMountedUsrBasicResumeResolver(
                MountedUsrContinuationStep step,
                Func<Spectrum128Machine, ushort?>? followOnResolver = null,
                Func<Spectrum128Machine, bool>? canResume = null)
            {
                return currentMachine =>
                {
                    if (canResume != null && !canResume(currentMachine))
                    {
                        currentMachine.ClearPendingMountedLoadUsrContinuation();
                        return null;
                    }

                    if (followOnResolver != null)
                    {
                        // A follow-on step after a ROM-driven BASIC resume should not fire
                        // from the intermediate ROM callback/service loop that the resumed
                        // USR routine may use internally. It should wait until the prior
                        // USR genuinely returns through STACK-BC.
                        currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                            followOnResolver);
                    }

                    currentMachine.SetPendingMountedLoadBasicResume(
                        step.LineNumber,
                        (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                    return ushort.MaxValue;
                };
            }

            private static Func<Spectrum128Machine, ushort?> CreateMountedUsrContinuationResolver(
                IReadOnlyList<MountedUsrContinuationStep> steps,
                int stepIndex,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                bool requireUsrReturnAddressBetweenSteps)
            {
                return currentMachine =>
                {
                    int currentStepIndex = stepIndex;
                    bool allowCachedLiteralUsrZeroFallback = false;
                    while ((uint)currentStepIndex < (uint)steps.Count)
                    {
                        MountedUsrContinuationStep step = steps[currentStepIndex];
                        try
                        {
                            bool hasConditionalPreamble = ContainsConditionalUsrContinuationPreamble(step.PreambleStatements);
                            if (ShouldForceRomBasicResumeForLiveConditionalMountedUsrStep(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step))
                            {
                                UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                if (currentStepIndex + 1 < steps.Count)
                                {
                                    currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                        requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                }
                                else
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                }

                                currentMachine.SetPendingMountedLoadBasicResume(
                                    step.LineNumber,
                                    (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                return ushort.MaxValue;
                            }

                            bool? romBasicResumeDecision = ShouldUseRomBasicResumeForMountedUsrStep(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step,
                                allowCachedLiteralUsrZeroFallback);

                            if (hasConditionalPreamble)
                            {
                                allowCachedLiteralUsrZeroFallback = false;

                                if (!romBasicResumeDecision.HasValue)
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                    return null;
                                }

                                if (romBasicResumeDecision.Value)
                                {
                                    UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                    if (currentStepIndex + 1 < steps.Count)
                                    {
                                        currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                            requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                    }
                                    else
                                    {
                                        currentMachine.ClearPendingMountedLoadUsrContinuation();
                                    }

                                    currentMachine.SetPendingMountedLoadBasicResume(
                                        step.LineNumber,
                                        (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                    return ushort.MaxValue;
                                }
                            }

                            MountedUsrContinuationPreambleResult preambleResult =
                                ExecuteMountedUsrContinuationPreamble(currentMachine, variables, step.PreambleStatements);
                            if (preambleResult == MountedUsrContinuationPreambleResult.SkipStep)
                            {
                                allowCachedLiteralUsrZeroFallback = true;
                                currentStepIndex++;
                                continue;
                            }

                            allowCachedLiteralUsrZeroFallback = false;

                            if (!romBasicResumeDecision.HasValue)
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                                return null;
                            }

                            if (romBasicResumeDecision.Value)
                            {
                                UpdateMountedUsrContinuationRomResumeContext(currentMachine, step);

                                if (currentStepIndex + 1 < steps.Count)
                                {
                                    currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                        CreateMountedUsrContinuationResolver(
                                            steps,
                                            currentStepIndex + 1,
                                            variables,
                                            programStart,
                                            programLength,
                                            requireUsrReturnAddressBetweenSteps),
                                        requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                                }
                                else
                                {
                                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                                }

                                currentMachine.SetPendingMountedLoadBasicResume(
                                    step.LineNumber,
                                    (byte)Math.Clamp(step.StatementIndex, 0, byte.MaxValue));
                                return ushort.MaxValue;
                            }

                            if (currentStepIndex + 1 < steps.Count)
                            {
                                currentMachine.SetPendingMountedLoadUsrContinuationResolver(
                                    CreateMountedUsrContinuationResolver(
                                        steps,
                                        currentStepIndex + 1,
                                        variables,
                                        programStart,
                                        programLength,
                                        requireUsrReturnAddressBetweenSteps),
                                    requireUsrReturnAddress: requireUsrReturnAddressBetweenSteps);
                            }
                            else
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                            }

                            bool preserveLiveInterpreterStateForDirectUsr =
                                ShouldPreserveLiveInterpreterStateForDirectMountedUsrStep(
                                    currentMachine,
                                    programStart,
                                    programLength,
                                    step) ||
                                ShouldPreserveLiveInterpreterStateForFollowOnMountedUsrSteps(
                                    steps,
                                    currentStepIndex);
                            ushort? entryPoint = EvaluateMountedUsrContinuationExpression(
                                currentMachine,
                                variables,
                                programStart,
                                programLength,
                                step);

                            if (!entryPoint.HasValue)
                            {
                                currentMachine.ClearPendingMountedLoadUsrContinuation();
                                return null;
                            }

                            if (entryPoint.Value == 0)
                                return 0;

                            UpdateMountedUsrContinuationDirectUsrContext(
                                currentMachine,
                                step,
                                preserveLiveInterpreterStateForDirectUsr);
                            return entryPoint.Value;
                        }
                        catch
                        {
                            currentMachine.ClearPendingMountedLoadUsrContinuation();
                            return null;
                        }
                    }

                    currentMachine.ClearPendingMountedLoadUsrContinuation();
                    return null;
                };
            }

            private static bool? ShouldUseRomBasicResumeForMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step,
                bool allowCachedLiteralUsrZeroFallback = false)
            {
                IReadOnlyList<string> expressionTokens = step.ExpressionTokens;
                bool hasLiveTokens = TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens);
                if (hasLiveTokens && refreshedTokens is { Length: > 0 })
                    expressionTokens = refreshedTokens;

                if (hasLiveTokens &&
                    ShouldPreferRomBasicResumeForLiveMountedUsrStep(machine, variables, step, expressionTokens))
                {
                    return true;
                }

                if (ContainsConditionalUsrContinuationPreamble(step.PreambleStatements) &&
                    !CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                {
                    return true;
                }

                if (expressionTokens.Count == 1 &&
                    string.Equals(expressionTokens[0], "0", StringComparison.Ordinal))
                {
                    if (!hasLiveTokens && !allowCachedLiteralUsrZeroFallback)
                        return null;

                    return false;
                }

                return ShouldPreferRomBasicResumeForLiveMountedUsrStep(machine, variables, step, expressionTokens);
            }

            private static bool ShouldForceRomBasicResumeForLiveConditionalMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                if (!ContainsConditionalUsrContinuationPreamble(step.PreambleStatements))
                    return false;

                if (CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                    return false;

                return TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out _);
            }

            private static bool ShouldPreferRomBasicResumeForLiveMountedUsrStep(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                MountedUsrContinuationStep step,
                IReadOnlyList<string> expressionTokens)
            {
                if (CanEvaluateLiveMountedUsrStepDirectly(machine, variables, step, expressionTokens))
                    return false;

                return !IsSimpleLiteralUsrExpression(expressionTokens);
            }

            private static bool CanEvaluateLiveMountedUsrStepDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                MountedUsrContinuationStep step,
                IReadOnlyList<string> expressionTokens)
            {
                if (step.PreambleStatements.Length != 0 &&
                    !CanEvaluateMountedUsrPreambleDirectly(machine, variables, step.PreambleStatements))
                {
                    return false;
                }

                return CanEvaluateMountedUsrExpressionDirectly(machine, variables, expressionTokens);
            }

            private static bool ShouldPreserveLiveInterpreterStateForDirectMountedUsrStep(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                IReadOnlyList<string> expressionTokens = step.ExpressionTokens;
                if (TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens) &&
                    refreshedTokens is { Length: > 0 })
                {
                    expressionTokens = refreshedTokens;
                }

                return !IsSimpleLiteralUsrExpression(expressionTokens);
            }

            private static bool ShouldPreserveLiveInterpreterStateForFollowOnMountedUsrSteps(
                IReadOnlyList<MountedUsrContinuationStep> steps,
                int currentStepIndex)
            {
                for (int i = currentStepIndex + 1; i < steps.Count; i++)
                {
                    MountedUsrContinuationStep followOnStep = steps[i];
                    if (ContainsConditionalUsrContinuationPreamble(followOnStep.PreambleStatements) ||
                        followOnStep.PreambleStatements.Length != 0 ||
                        !IsSimpleLiteralUsrExpression(followOnStep.ExpressionTokens))
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool ContainsConditionalUsrContinuationPreamble(IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length != 0 &&
                        NormalizeStatementKeyword(clause[0]) == "IF")
                    {
                        return true;
                    }
                }

                return false;
            }

            private static bool CanEvaluateMountedUsrPreambleDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length == 0)
                        continue;

                    string keyword = NormalizeStatementKeyword(clause[0]);
                    if (keyword == "IF")
                    {
                        if (!CanEvaluateMountedUsrContinuationIfConditionDirectly(machine, variables, clause))
                            return false;
                        continue;
                    }

                    if (keyword == "LET")
                    {
                        int equalsIndex = Array.IndexOf(clause, "=");
                        if (equalsIndex <= 1 || equalsIndex >= clause.Length - 1)
                            return false;

                        if (!CanEvaluateMountedUsrExpressionDirectly(
                            machine,
                            variables,
                            clause.Skip(equalsIndex + 1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (keyword == "CLEAR")
                    {
                        if (clause.Length > 1 &&
                            !CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (keyword == "POKE")
                    {
                        int commaIndex = Array.IndexOf(clause, ",");
                        if (commaIndex <= 1 || commaIndex >= clause.Length - 1)
                            return false;

                        if (!CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(1).Take(commaIndex - 1).ToArray()) ||
                            !CanEvaluateMountedUsrExpressionDirectly(
                                machine,
                                variables,
                                clause.Skip(commaIndex + 1).ToArray()))
                        {
                            return false;
                        }

                        continue;
                    }

                    if (!IsSafeUsrContinuationPreambleStatement(new List<string>(clause)))
                        return false;
                }

                return true;
            }

            private static bool CanEvaluateMountedUsrContinuationIfConditionDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> clause)
            {
                int equalsIndex = -1;
                for (int i = 1; i < clause.Count; i++)
                {
                    if (clause[i] == "=")
                    {
                        equalsIndex = i;
                        break;
                    }
                }

                if (equalsIndex <= 1 || equalsIndex >= clause.Count - 1)
                    return false;

                string[] left = clause.Skip(1).Take(equalsIndex - 1).ToArray();
                string[] right = clause.Skip(equalsIndex + 1).ToArray();
                return CanEvaluateMountedUsrExpressionDirectly(machine, variables, left) &&
                       CanEvaluateMountedUsrExpressionDirectly(machine, variables, right);
            }

            private static bool CanEvaluateMountedUsrExpressionDirectly(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> expressionTokens)
            {
                if (!IsSafeUsrContinuationExpression(expressionTokens))
                    return false;

                foreach (string token in expressionTokens)
                {
                    if (string.IsNullOrEmpty(token) ||
                        token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                    {
                        continue;
                    }

                    if (NormalizeStatementKeyword(token) == "PEEK")
                        continue;

                    if (int.TryParse(token, out _))
                        continue;

                    if (variables.ContainsKey(token))
                        continue;

                    if (TryReadMountedContinuationNumericVariable(machine, token, out _))
                        continue;

                    // Spectrum BASIC numeric variables read as zero until assigned.
                    // Treating an unresolved safe variable token as evaluable keeps
                    // direct mounted-continuation IF/THEN logic aligned with the ROM
                    // instead of needlessly forcing a ROM BASIC resume.
                    continue;
                }

                return true;
            }

            private static bool IsSimpleLiteralUsrExpression(IReadOnlyList<string> expressionTokens)
            {
                if (expressionTokens.Count != 1)
                    return false;

                string token = expressionTokens[0];
                if (string.IsNullOrEmpty(token))
                    return false;

                for (int i = 0; i < token.Length; i++)
                {
                    if (!char.IsAsciiDigit(token[i]))
                        return false;
                }

                return true;
            }

            private static bool IsSafeUsrContinuationExpression(IReadOnlyList<string> expressionTokens)
            {
                if (expressionTokens.Count == 0)
                    return false;

                foreach (string token in expressionTokens)
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    if (token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                        continue;

                    string normalized = NormalizeStatementKeyword(token);
                    if (normalized == "PEEK")
                        continue;

                    if (IsKnownBasicKeywordToken(token))
                        return false;

                    for (int i = 0; i < token.Length; i++)
                    {
                        if (!IsAsciiLetterOrDigit(token[i]))
                            return false;
                    }
                }

                return true;
            }

            private static ushort? EvaluateMountedUsrContinuationExpression(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step)
            {
                bool hasRefreshedTokens = TryRefreshMountedUsrExpressionTokens(
                    machine,
                    programStart,
                    programLength,
                    step,
                    out string[]? refreshedTokens);

                if (hasRefreshedTokens &&
                    TryEvaluateMountedUsrExpression(machine, variables, refreshedTokens!, out ushort? refreshedValue))
                {
                    return refreshedValue;
                }

                return TryEvaluateMountedUsrExpression(machine, variables, step.ExpressionTokens, out ushort? fallbackValue)
                    ? fallbackValue
                    : null;
            }

            private static bool TryEvaluateMountedUsrExpression(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> expressionTokens,
                out ushort? value)
            {
                value = null;
                if (expressionTokens.Count == 0)
                    return false;

                try
                {
                    var parser = new ExpressionParser(
                        machine,
                        variables,
                        expressionTokens.ToList(),
                        0,
                        expressionTokens.Count - 1,
                        requireVariableResolution: true);
                    int parsedValue = parser.Parse();
                    if ((uint)parsedValue > ushort.MaxValue)
                        return false;

                    value = (ushort)parsedValue;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static bool TryRefreshMountedUsrExpressionTokens(
                Spectrum128Machine machine,
                ushort programStart,
                ushort programLength,
                MountedUsrContinuationStep step,
                out string[]? expressionTokens)
            {
                expressionTokens = null;
                if (TryRefreshMountedUsrExpressionTokensFromLiveLine(machine, step, out expressionTokens))
                    return true;

                List<BasicLine> parsedLines = ParseLines(machine, programStart, programLength);
                int lineIndex = parsedLines.FindIndex(line => line.Number == step.LineNumber);
                if (lineIndex < 0)
                    return false;

                int statementIndex = step.StatementIndex - 1;
                if ((uint)statementIndex >= (uint)parsedLines[lineIndex].Statements.Count)
                    return false;

                int usrClauseOrdinal = 0;
                foreach (List<string> clause in EnumerateUsrContinuationClauses(parsedLines[lineIndex].Statements[statementIndex]))
                {
                    if (!TryGetRandomizeUsrExpressionTokens(clause, out List<string> liveExpressionTokens))
                        continue;

                    if (usrClauseOrdinal == step.UsrClauseOrdinal)
                    {
                        expressionTokens = liveExpressionTokens.ToArray();
                        return expressionTokens.Length > 0;
                    }

                    usrClauseOrdinal++;
                }

                return false;
            }

            private static bool TryRefreshMountedUsrExpressionTokensFromLiveLine(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                out string[]? expressionTokens)
            {
                expressionTokens = null;

                if (!TryParseLiveBasicLine(machine, step.LineDataAddress, step.LineNumber, out BasicLine? parsedLine) ||
                    parsedLine is null)
                    return false;
                BasicLine liveLine = (BasicLine)parsedLine;

                int statementIndex = step.StatementIndex - 1;
                if ((uint)statementIndex >= (uint)liveLine.Statements.Count)
                    return false;

                int usrClauseOrdinal = 0;
                foreach (List<string> clause in EnumerateUsrContinuationClauses(liveLine.Statements[statementIndex]))
                {
                    if (!TryGetRandomizeUsrExpressionTokens(clause, out List<string> liveExpressionTokens))
                        continue;

                    if (usrClauseOrdinal == step.UsrClauseOrdinal)
                    {
                        expressionTokens = liveExpressionTokens.ToArray();
                        return expressionTokens.Length > 0;
                    }

                    usrClauseOrdinal++;
                }

                return false;
            }

            private static bool TryParseLiveBasicLine(
                Spectrum128Machine machine,
                ushort lineDataAddress,
                ushort expectedLineNumber,
                out BasicLine? line)
            {
                line = null;
                if (lineDataAddress < 4)
                    return false;

                ushort lineHeaderAddress = (ushort)(lineDataAddress - 4);
                ushort liveLineNumber = (ushort)(
                    (machine.PeekMemory(lineHeaderAddress) << 8) |
                    machine.PeekMemory((ushort)(lineHeaderAddress + 1)));
                if (liveLineNumber != expectedLineNumber)
                    return false;

                ushort lineLength = ReadWord(machine, (ushort)(lineHeaderAddress + 2));
                if (lineLength == 0 || lineLength > 1024)
                    return false;

                byte[] lineBytes = new byte[lineLength];
                for (int i = 0; i < lineLength; i++)
                    lineBytes[i] = machine.PeekMemory((ushort)(lineDataAddress + i));

                (List<List<string>> statements, List<int> statementByteOffsets, List<List<TokenSpan>> statementTokenSpans) =
                    SplitStatements(Tokenize(lineBytes));
                line = new BasicLine(liveLineNumber, statements, statementByteOffsets, statementTokenSpans, lineDataAddress);
                return true;
            }

            private static void UpdateMountedUsrContinuationRomResumeContext(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                bool suppressCursorOverride = false)
            {
                WriteWord(machine, NewPpcAddress, step.LineNumber);
                machine.PokeMemory(NspPcAddress, (byte)Math.Clamp(step.StatementIndex, 0, 255));
                WriteWord(machine, PpcAddress, step.LineNumber);
                machine.PokeMemory(SubPpcAddress, (byte)Math.Clamp(step.StatementIndex, 0, 255));

                if (!suppressCursorOverride)
                {
                    ushort chAdd = (ushort)(step.LineDataAddress + step.ClauseStartByteOffset);
                    machine.SetPendingMountedLoadResumeCursorOverride(chAdd: chAdd);
                }
            }

            private static void UpdateMountedUsrContinuationDirectUsrContext(
                Spectrum128Machine machine,
                MountedUsrContinuationStep step,
                bool preserveLiveInterpreterState)
            {
                UpdateMountedUsrContinuationRomResumeContext(
                    machine,
                    step,
                    suppressCursorOverride: preserveLiveInterpreterState);
                machine.SetPendingMountedLoadDirectUsrContextPolicy(preserveLiveInterpreterState);
                if (preserveLiveInterpreterState)
                    return;

                ushort eLine = ReadWord(machine, EditLineAddress);
                ushort chAdd = (ushort)(step.LineDataAddress + step.ExpressionByteOffset + 1);
                machine.SetPendingMountedLoadResumeCursorOverride(kCur: eLine, chAdd: chAdd, xPtr: chAdd);
            }

            private enum MountedUsrContinuationPreambleResult
            {
                ContinueStep,
                SkipStep
            }

            private static MountedUsrContinuationPreambleResult ExecuteMountedUsrContinuationPreamble(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string[]> preambleStatements)
            {
                foreach (string[] clause in preambleStatements)
                {
                    if (clause.Length == 0)
                        continue;

                    string keyword = NormalizeStatementKeyword(clause[0]);
                    switch (keyword)
                    {
                        case "LET":
                        {
                            int equalsIndex = Array.IndexOf(clause, "=");
                            if (equalsIndex <= 1 || equalsIndex >= clause.Length - 1)
                                throw new InvalidOperationException("Malformed LET statement in mounted USR continuation.");

                            string variableName = clause[1];
                            var parser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                equalsIndex + 1,
                                clause.Length - 1,
                                requireVariableResolution: true);
                            int value = parser.Parse();
                            variables[variableName] = value;
                            TryUpdateMountedUsrContinuationBasicVariable(machine, variableName, value);
                            break;
                        }

                        case "IF":
                        {
                            if (!EvaluateMountedUsrContinuationIfCondition(machine, variables, clause))
                                return MountedUsrContinuationPreambleResult.SkipStep;
                            break;
                        }

                        case "REM":
                        case "?":
                        case "BORDER":
                        case "PAPER":
                        case "INK":
                        case "CLS":
                            ApplyMountedContinuationDisplaySideEffects(machine);
                            break;

                        case "CLEAR":
                        {
                            if (clause.Length > 1)
                            {
                                var parser = new ExpressionParser(
                                    machine,
                                    variables,
                                    new List<string>(clause),
                                    1,
                                    clause.Length - 1,
                                    requireVariableResolution: true);
                                ApplyMountedContinuationClear(machine, parser.Parse());
                            }
                            else
                            {
                                ApplyMountedContinuationDisplaySideEffects(machine);
                            }
                            break;
                        }

                        case "POKE":
                        {
                            int commaIndex = Array.IndexOf(clause, ",");
                            if (commaIndex <= 1 || commaIndex >= clause.Length - 1)
                                throw new InvalidOperationException("Malformed POKE statement in mounted USR continuation.");

                            var addressParser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                1,
                                commaIndex - 1,
                                requireVariableResolution: true);
                            var valueParser = new ExpressionParser(
                                machine,
                                variables,
                                new List<string>(clause),
                                commaIndex + 1,
                                clause.Length - 1,
                                requireVariableResolution: true);
                            int address = addressParser.Parse();
                            int value = valueParser.Parse();
                            machine.PokeMemory((ushort)address, (byte)value);
                            if (address == Spectrum128TapeLoadBankSelectAddress && (value & 0xF8) == 0x10)
                                machine.ForceApply7ffdValue((byte)value);
                            break;
                        }

                        default:
                            throw new InvalidOperationException($"Unsupported mounted USR continuation preamble '{keyword}'.");
                    }
                }

                return MountedUsrContinuationPreambleResult.ContinueStep;
            }

            private static bool EvaluateMountedUsrContinuationIfCondition(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                IReadOnlyList<string> clause)
            {
                int equalsIndex = -1;
                for (int i = 1; i < clause.Count; i++)
                {
                    if (clause[i] == "=")
                    {
                        equalsIndex = i;
                        break;
                    }
                }

                if (equalsIndex <= 1 || equalsIndex >= clause.Count - 1)
                    throw new InvalidOperationException("Malformed IF statement in mounted USR continuation.");

                var leftParser = new ExpressionParser(
                    machine,
                    variables,
                    new List<string>(clause),
                    1,
                    equalsIndex - 1,
                    requireVariableResolution: true);
                var rightParser = new ExpressionParser(
                    machine,
                    variables,
                    new List<string>(clause),
                    equalsIndex + 1,
                    clause.Count - 1,
                    requireVariableResolution: true);
                return leftParser.Parse() == rightParser.Parse();
            }

            private static void ApplyMountedContinuationDisplaySideEffects(Spectrum128Machine machine)
            {
                machine.PokeMemory(FlagsSystemVariableAddress, (byte)(machine.PeekMemory(FlagsSystemVariableAddress) | 0x20));
                machine.PokeMemory(TvFlagSystemVariableAddress, (byte)(machine.PeekMemory(TvFlagSystemVariableAddress) | 0x20));
            }

            private static void ApplyMountedContinuationClear(Spectrum128Machine machine, int clearAddress)
            {
                ushort top = ClampClearTopAddress(clearAddress);
                WriteWord(machine, RamTopAddress, top);
                WriteWord(machine, PhysicalRamTopAddress, top);
                ushort workspace = GetWorkspaceAddressForClear(top);
                WriteWord(machine, VarsAddress, workspace);
                WriteWord(machine, EditLineAddress, workspace);
                WriteWord(machine, WorkspaceAddress, workspace);
                WriteWord(machine, StackBottomAddress, workspace);
                WriteWord(machine, StackEndAddress, workspace);
                WriteWord(machine, KCurAddress, workspace);
                WriteWord(machine, ChAddAddress, workspace);
                if (ShouldRelocateMachineStackAfterClear(machine, top))
                    machine.Cpu.Regs.SP = GetMachineStackPointerForClear(top);
            }

            private static void TryUpdateMountedUsrContinuationBasicVariable(
                Spectrum128Machine machine,
                string variableName,
                int value)
            {
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return;

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return;

                if (value < short.MinValue || value > ushort.MaxValue)
                    return;

                ushort varsAddress = ReadWord(machine, VarsAddress);
                ushort eLineAddress = ReadWord(machine, EditLineAddress);
                ushort current = varsAddress;
                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));

                while (current < eLineAddress)
                {
                    byte header = machine.PeekMemory(current);
                    if (header == 0x80)
                        return;

                    int entryLength = GetBasicVariableEntryLength(machine, current, eLineAddress);
                    if (entryLength <= 0 || current + entryLength > eLineAddress)
                        return;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        WriteSpectrumSmallInteger(machine, (ushort)(current + 1), value);
                        return;
                    }

                    current = (ushort)(current + entryLength);
                }
            }

            private static int GetBasicVariableEntryLength(
                Spectrum128Machine machine,
                ushort address,
                ushort eLineAddress)
            {
                if (address >= eLineAddress)
                    return 0;

                byte header = machine.PeekMemory(address);
                if (header == 0x80)
                    return 1;

                switch (header & 0xE0)
                {
                    case 0x40:
                    {
                        if (address + 2 >= eLineAddress)
                            return 0;
                        ushort length = ReadWord(machine, (ushort)(address + 1));
                        return 3 + length;
                    }

                    case 0x60:
                        return 6;

                    case 0x80:
                    case 0xC0:
                    {
                        if (address + 2 >= eLineAddress)
                            return 0;
                        ushort length = ReadWord(machine, (ushort)(address + 1));
                        return 3 + length;
                    }

                    case 0xA0:
                    {
                        ushort current = (ushort)(address + 1);
                        while (current < eLineAddress)
                        {
                            byte nameByte = machine.PeekMemory(current);
                            current++;
                            if ((nameByte & 0x80) != 0)
                            {
                                if (current + 5 > eLineAddress)
                                    return 0;
                                return current - address + 5;
                            }
                        }

                        return 0;
                    }

                    case 0xE0:
                        return 19;

                    default:
                        return 0;
                }
            }

            private static void WriteSpectrumSmallInteger(
                Spectrum128Machine machine,
                ushort address,
                int value)
            {
                short signedValue = (short)value;
                ushort magnitude = (ushort)signedValue;
                byte sign = signedValue < 0 ? (byte)0xFF : (byte)0x00;

                machine.PokeMemory(address, 0x00);
                machine.PokeMemory((ushort)(address + 1), sign);
                machine.PokeMemory((ushort)(address + 2), (byte)(magnitude & 0xFF));
                machine.PokeMemory((ushort)(address + 3), (byte)(magnitude >> 8));
                machine.PokeMemory((ushort)(address + 4), 0x00);
            }

            private static bool TryReadMountedContinuationNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                if (TryReadLiveBasicNumericVariable(machine, variableName, out value))
                    return true;

                return TryReadPreservedMountedLoadBasicNumericVariable(machine, variableName, out value);
            }

            private static bool TryReadLiveBasicNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return false;

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return false;

                ushort varsAddress = ReadWord(machine, VarsAddress);
                ushort eLineAddress = ReadWord(machine, EditLineAddress);
                ushort current = varsAddress;
                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));

                while (current < eLineAddress)
                {
                    byte header = machine.PeekMemory(current);
                    if (header == 0x80)
                        return false;

                    int entryLength = GetBasicVariableEntryLength(machine, current, eLineAddress);
                    if (entryLength <= 0 || current + entryLength > eLineAddress)
                        return false;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        return TryReadSpectrumSmallInteger(machine, (ushort)(current + 1), out value);
                    }

                    current = (ushort)(current + entryLength);
                }

                return false;
            }

            private static bool TryReadPreservedMountedLoadBasicNumericVariable(
                Spectrum128Machine machine,
                string variableName,
                out int value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(variableName) || variableName.Length != 1)
                    return false;

                if (!machine.TryGetPendingMountedLoadBasicVariableSnapshot(out _, out byte[] data) ||
                    data.Length == 0)
                {
                    return false;
                }

                char variableChar = char.ToLowerInvariant(variableName[0]);
                if (variableChar < 'a' || variableChar > 'z')
                    return false;

                byte targetHeader = (byte)(0x60 | (variableChar - 'a' + 1));
                int index = 0;
                while (index < data.Length)
                {
                    byte header = data[index];
                    if (header == 0x80)
                        return false;

                    int entryLength = GetBasicVariableEntryLength(data, index);
                    if (entryLength <= 0 || index + entryLength > data.Length)
                        return false;

                    if ((header & 0xE0) == 0x60 && header == targetHeader)
                    {
                        return TryReadSpectrumSmallInteger(data, index + 1, out value);
                    }

                    index += entryLength;
                }

                return false;
            }

            private static bool TryReadSpectrumSmallInteger(
                Spectrum128Machine machine,
                ushort address,
                out int value)
            {
                value = 0;
                byte exponent = machine.PeekMemory(address);
                byte sign = machine.PeekMemory((ushort)(address + 1));
                byte low = machine.PeekMemory((ushort)(address + 2));
                byte high = machine.PeekMemory((ushort)(address + 3));
                byte trailing = machine.PeekMemory((ushort)(address + 4));
                return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
            }

            private static bool TryReadSpectrumSmallInteger(
                byte[] data,
                int index,
                out int value)
            {
                value = 0;
                if (index < 0 || index + 4 >= data.Length)
                    return false;

                byte exponent = data[index];
                byte sign = data[index + 1];
                byte low = data[index + 2];
                byte high = data[index + 3];
                byte trailing = data[index + 4];
                return TryDecodeSpectrumNumericValue(exponent, sign, low, high, trailing, out value);
            }

            private static bool TryDecodeSpectrumNumericValue(
                byte exponent,
                byte signOrMantissa,
                byte third,
                byte fourth,
                byte fifth,
                out int value)
            {
                value = 0;

                if (exponent == 0x00)
                {
                    if (fifth != 0x00)
                        return false;

                    short signedValue = unchecked((short)(third | (fourth << 8)));
                    value = signOrMantissa == 0xFF ? -Math.Abs(signedValue) : signedValue;
                    return true;
                }

                uint mantissa =
                    (uint)(((signOrMantissa & 0x7F) | 0x80) << 24) |
                    (uint)(third << 16) |
                    (uint)(fourth << 8) |
                    fifth;
                int sign = (signOrMantissa & 0x80) != 0 ? -1 : 1;
                double numericValue = sign * mantissa * Math.Pow(2.0, exponent - 160);
                double roundedValue = Math.Round(numericValue);
                if (roundedValue < int.MinValue || roundedValue > int.MaxValue)
                    return false;

                if (Math.Abs(numericValue - roundedValue) > 1e-6)
                    return false;

                value = (int)roundedValue;
                return true;
            }

            private static int GetBasicVariableEntryLength(byte[] data, int index)
            {
                if ((uint)index >= (uint)data.Length)
                    return 0;

                byte header = data[index];
                if (header == 0x80)
                    return 1;

                switch (header & 0xE0)
                {
                    case 0x40:
                    case 0x80:
                    case 0xC0:
                    {
                        if (index + 2 >= data.Length)
                            return 0;

                        ushort length = (ushort)(data[index + 1] | (data[index + 2] << 8));
                        return 3 + length;
                    }

                    case 0x60:
                        return 6;

                    case 0xA0:
                    {
                        int current = index + 1;
                        while (current < data.Length)
                        {
                            byte nameByte = data[current++];
                            if ((nameByte & 0x80) != 0)
                            {
                                if (current + 5 > data.Length)
                                    return 0;

                                return current - index + 5;
                            }
                        }

                        return 0;
                    }

                    case 0xE0:
                        return 19;

                    default:
                        return 0;
                }
            }

            private sealed record MountedUsrContinuationStep(
                ushort LineNumber,
                int StatementIndex,
                int UsrClauseOrdinal,
                int StatementStartByteOffset,
                int ClauseStartByteOffset,
                int ExpressionByteOffset,
                ushort LineDataAddress,
                string[][] PreambleStatements,
                string[] ExpressionTokens);

            private static bool TryGetRandomizeUsrExpressionTokens(List<string> statement, out List<string> expressionTokens)
            {
                expressionTokens = new List<string>();
                if (statement.Count == 0 || NormalizeStatementKeyword(statement[0]) != "RANDOMIZE")
                    return false;

                int usrIndex = statement.IndexOf("USR");
                if (usrIndex < 0 || usrIndex + 1 >= statement.Count)
                    return false;

                expressionTokens = statement.GetRange(usrIndex + 1, statement.Count - usrIndex - 1);
                return expressionTokens.Count > 0;
            }

            private static bool TryGetRandomizeUsrExpressionInfo(
                List<TokenSpan> statementTokens,
                out List<string> expressionTokens,
                out int expressionByteOffset)
            {
                expressionTokens = new List<string>();
                expressionByteOffset = 0;
                if (statementTokens.Count == 0 || NormalizeStatementKeyword(statementTokens[0].Text) != "RANDOMIZE")
                    return false;

                int usrIndex = statementTokens.FindIndex(token => token.Text == "USR");
                if (usrIndex < 0 || usrIndex + 1 >= statementTokens.Count)
                    return false;

                expressionByteOffset = statementTokens[usrIndex + 1].ByteOffset;
                expressionTokens = statementTokens
                    .GetRange(usrIndex + 1, statementTokens.Count - usrIndex - 1)
                    .ConvertAll(token => token.Text);
                return expressionTokens.Count > 0;
            }

            private static IEnumerable<List<string>> EnumerateUsrContinuationClauses(List<string> statement)
            {
                if (statement.Count == 0)
                    yield break;

                int clauseStart = 0;
                for (int tokenIndex = 0; tokenIndex < statement.Count; tokenIndex++)
                {
                    if (!string.Equals(statement[tokenIndex], "THEN", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (tokenIndex > clauseStart)
                        yield return statement.GetRange(clauseStart, tokenIndex - clauseStart);

                    clauseStart = tokenIndex + 1;
                }

                if (clauseStart < statement.Count)
                    yield return statement.GetRange(clauseStart, statement.Count - clauseStart);
            }

            private static IEnumerable<List<TokenSpan>> EnumerateUsrContinuationClauses(
                List<TokenSpan> statementTokens)
            {
                if (statementTokens.Count == 0)
                    yield break;

                int clauseStartIndex = 0;
                for (int tokenIndex = 0; tokenIndex < statementTokens.Count; tokenIndex++)
                {
                    if (!string.Equals(statementTokens[tokenIndex].Text, "THEN", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (tokenIndex > clauseStartIndex)
                    {
                        yield return statementTokens.GetRange(clauseStartIndex, tokenIndex - clauseStartIndex);
                    }

                    clauseStartIndex = tokenIndex + 1;
                }

                if (clauseStartIndex < statementTokens.Count)
                {
                    yield return statementTokens.GetRange(clauseStartIndex, statementTokens.Count - clauseStartIndex);
                }
            }

            private static bool IsSafeUsrContinuationPreambleStatement(List<string> statement)
            {
                if (statement.Count == 0)
                    return true;

                string keyword = NormalizeStatementKeyword(statement[0]);
                if (keyword is "REM" or "?" or "BORDER" or "PAPER" or "INK" or "CLS" or "CLEAR" or "POKE")
                    return true;

                if (keyword == "LET")
                    return statement.Contains("=");

                if (keyword == "IF")
                    return statement.Contains("=");

                foreach (string token in statement)
                {
                    if (string.IsNullOrEmpty(token))
                        continue;

                    if (token is "," or "(" or ")" or "=" or "+" or "-" or "*")
                    {
                        continue;
                    }

                    string normalized = NormalizeStatementKeyword(token);
                    if (normalized == "PEEK")
                        continue;

                    if (IsKnownBasicKeywordToken(token))
                        return false;

                    bool isAsciiWord = true;
                    for (int i = 0; i < token.Length; i++)
                    {
                        char c = token[i];
                        if (!IsAsciiLetterOrDigit(c))
                        {
                            isAsciiWord = false;
                            break;
                        }
                    }

                    if (!isAsciiWord)
                        return false;
                }

                return true;
            }

            private static bool IsSimpleAnonymousCodeLoad(List<string> statement)
            {
                if (statement.Count <= 1)
                    return true;

                for (int i = 1; i < statement.Count; i++)
                {
                    string token = statement[i];
                    if (string.IsNullOrEmpty(token) || token == "CODE")
                        continue;
                    return false;
                }

                return true;
            }

            private static string NormalizeStatementKeyword(string keyword)
            {
                if (string.Equals(keyword, "PRINT", StringComparison.OrdinalIgnoreCase))
                    return "?";

                if (!string.IsNullOrEmpty(keyword) && keyword[0] == '?')
                    return "?";

                return keyword;
            }

            private static IEnumerable<int> CollectDataValues(
                List<BasicLine> parsedLines,
                Spectrum128Machine machine,
                Dictionary<string, int> variables)
            {
                foreach (BasicLine line in parsedLines)
                {
                    foreach (List<string> statement in line.Statements)
                    {
                        if (statement.Count == 0 || statement[0] != "DATA")
                            continue;

                        int itemStart = 1;
                        while (itemStart < statement.Count)
                        {
                            int itemEnd = itemStart;
                            while (itemEnd < statement.Count && statement[itemEnd] != ",")
                                itemEnd++;

                            if (itemEnd > itemStart)
                            {
                                var parser = new ExpressionParser(machine, variables, statement, itemStart, itemEnd - 1);
                                yield return parser.Parse();
                            }

                            itemStart = itemEnd + 1;
                        }
                    }
                }
            }

            private static bool TryGetKeywordToken(byte value, out string? keyword)
            {
                keyword = value switch
                {
                    190 => "PEEK",
                    192 => "USR",
                    217 => "INK",
                    218 => "PAPER",
                    227 => "READ",
                    229 => "RESTORE",
                    231 => "BORDER",
                    234 => "REM",
                    239 => "LOAD",
                    244 => "POKE",
                    245 => "PRINT",
                    249 => "RANDOMIZE",
                    228 => "DATA",
                    235 => "FOR",
                    175 => "CODE",
                    242 => "PAUSE",
                    243 => "NEXT",
                    203 => "THEN",
                    241 => "LET",
                    250 => "IF",
                    251 => "CLS",
                    253 => "CLEAR",
                    254 => "RETURN",
                    204 => "TO",
                    _ => null
                };

                return keyword != null;
            }

            private readonly record struct TokenSpan(string Text, int ByteOffset);
            private readonly record struct BasicLine(
                ushort Number,
                List<List<string>> Statements,
                List<int> StatementByteOffsets,
                List<List<TokenSpan>> StatementTokenSpans,
                ushort DataAddress);
            private readonly record struct ForFrame(string VariableName, int EndValue, int LineIndex, int StatementIndex);
        }

        private sealed class ExpressionParser
        {
            private readonly Spectrum128Machine machine;
            private readonly Dictionary<string, int> variables;
            private readonly List<string> tokens;
            private readonly bool requireVariableResolution;
            private readonly int endIndex;
            private int index;

            public ExpressionParser(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                List<string> tokens,
                int startIndex,
                int endIndex,
                bool requireVariableResolution = false)
            {
                this.machine = machine;
                this.variables = variables;
                this.tokens = tokens;
                this.requireVariableResolution = requireVariableResolution;
                index = startIndex;
                this.endIndex = endIndex;
            }

            public int Parse()
            {
                int value = ParseAdditive();
                return value;
            }

            private int ParseAdditive()
            {
                int value = ParseMultiplicative();
                while (index <= endIndex)
                {
                    string token = tokens[index];
                    if (token == "+")
                    {
                        index++;
                        value += ParseMultiplicative();
                    }
                    else if (token == "-")
                    {
                        index++;
                        value -= ParseMultiplicative();
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private int ParseMultiplicative()
            {
                int value = ParseUnary();
                while (index <= endIndex)
                {
                    string token = tokens[index];
                    if (token == "*")
                    {
                        index++;
                        value *= ParseUnary();
                    }
                    else
                    {
                        break;
                    }
                }

                return value;
            }

            private int ParseUnary()
            {
                if (index <= endIndex && tokens[index] == "-")
                {
                    index++;
                    return -ParseUnary();
                }

                return ParsePrimary();
            }

            private int ParsePrimary()
            {
                if (index > endIndex)
                    throw new InvalidOperationException("Unexpected end of BASIC expression during tape bootstrap.");

                string token = tokens[index++];
                if (token == "(")
                {
                    int value = ParseAdditive();
                    if (index > endIndex || tokens[index] != ")")
                        throw new InvalidOperationException("Missing closing parenthesis in BASIC tape-bootstrap expression.");
                    index++;
                    return value;
                }

                if (token == "PEEK")
                {
                    int address = ParsePrimary();
                    return machine.PeekMemory((ushort)address);
                }

                if (int.TryParse(token, out int number))
                    return number;

                if (variables.TryGetValue(token, out int variableValue))
                    return variableValue;

                if (TryReadMountedContinuationNumericVariable(machine, token, out int liveVariableValue))
                    return liveVariableValue;

                // Spectrum BASIC treats an unread numeric variable as zero until it
                // is assigned. Mounted continuation evaluation should follow that
                // model instead of forcing a ROM BASIC resume just because a safe
                // direct IF/THEN or USR expression references an unset variable.
                if (requireVariableResolution)
                    return 0;

                return 0;
            }
        }

        internal sealed class TapHeaderInfo
        {
            public TapHeaderInfo(byte type, string fileName, ushort dataLength, ushort parameter1, ushort parameter2)
            {
                Type = type;
                FileName = string.IsNullOrWhiteSpace(fileName) ? "unnamed" : fileName;
                DataLength = dataLength;
                AutoStartLine = parameter1;
                ProgramLength = parameter2;
                StartAddress = parameter1;
            }

            public byte Type { get; }
            public string FileName { get; }
            public ushort DataLength { get; }
            public ushort AutoStartLine { get; }
            public ushort ProgramLength { get; }
            public ushort StartAddress { get; }
        }
    }
}
