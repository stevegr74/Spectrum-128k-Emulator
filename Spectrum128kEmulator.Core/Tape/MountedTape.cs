using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator.Tap
{
    public sealed partial class MountedTape
    {
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
        private const int PauseLevelSettlingTStates = 3500;
        private const int TapeTimingClockHz = 3500000;
        private const int MaximumTerminalPauseMs = 1000;
        private const int ProtectedStageHandoffPauseMs = 10000;
        private readonly IReadOnlyList<TapeBlock> blocks;
        private readonly bool skipCustomHeaderForEarPlayback;
        private readonly int initialBlockIndex;
        private readonly int initialPrePlaybackPauseTStates;
        private readonly int nonRomTimingDivisor;
        private readonly int loadableTimingDivisor;
        private readonly bool initialEarLevelHigh;
        private int playbackClockHz = TapeTimingClockHz;
        private bool stopAtProtectedStageHandoffs;
        private int nextBlockIndex;
        private int earPlaybackBlockIndex;
        private int earStreamByteIndex;
        private int earBitIndex;
        private int earPulseRepeatCount;
        private int earPilotPulsesRemaining;
        private int earPulseLengthTStates;
        private int earPulseSequenceIndex;
        private int earNextBlockIndexAfterPause;
        private int earPauseLowTailTStates;
        private int pendingPrePlaybackPauseTStates;
        private int romStreamTrapBlockIndex;
        private int romStreamTrapByteIndex;
        private int completedRomLoadableBlockIndex;
        private ulong lastEarSampleTStates;
        private bool earLevel;
        private bool earPlaybackStarted;
        private bool retainedByteStreamTrapAvailable;
        private bool playbackCompleted;
        private bool playbackPaused;
        private bool pausedByStopMarker;
        private bool pausedByProtectedStageHandoff;
        private ulong pausedPulseElapsedTStates;
        private int pauseBoundaryResumeBlockIndex;
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
            !playbackPaused &&
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
            $"Paused={(playbackPaused ? 1 : 0)} Marker={(pausedByStopMarker ? 1 : 0)} ProtectedHandoff={(pausedByProtectedStageHandoff ? 1 : 0)} Complete={(playbackCompleted ? 1 : 0)} " +
            $"RomTrapBlock={romStreamTrapBlockIndex} RomTrapByte={romStreamTrapByteIndex} " +
            $"CompletedRomBlock={completedRomLoadableBlockIndex}";
        public bool IsActivelyDrivingEarLine => !playbackPaused && earPlaybackState != EarPlaybackState.Idle;
        public bool HasCompletedPlayback => playbackCompleted;
        public bool IsExhaustedAndIdle =>
            blocks.Count > 0 &&
            !HasRemainingBlocks &&
            earPlaybackBlockIndex >= blocks.Count &&
            state == TapeState.Idle &&
            earPlaybackState == EarPlaybackState.Idle &&
            pendingPrePlaybackPauseTStates <= 0 &&
            !retainedByteStreamTrapAvailable &&
            !playbackPaused;
        public bool IsPlaybackPaused => playbackPaused;
        public bool IsPausedByStopMarker => pausedByStopMarker;
        public bool IsPausedByProtectedStageHandoff => pausedByProtectedStageHandoff;
        public bool IsActivelyStreamingEarSignal =>
            !playbackPaused &&
            earPlaybackState is not EarPlaybackState.Idle
            and not EarPlaybackState.Pause;
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
            earPauseLowTailTStates = 0;
            pendingPrePlaybackPauseTStates = ScaleToPlaybackClock(initialPrePlaybackPauseTStates);
            romStreamTrapBlockIndex = -1;
            romStreamTrapByteIndex = 0;
            completedRomLoadableBlockIndex = -1;
            lastEarSampleTStates = 0;
            earLevel = initialEarLevelHigh;
            earPlaybackStarted = false;
            retainedByteStreamTrapAvailable = false;
            playbackCompleted = false;
            playbackPaused = false;
            pausedByStopMarker = false;
            pausedByProtectedStageHandoff = false;
            pausedPulseElapsedTStates = 0;
            pauseBoundaryResumeBlockIndex = -1;
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

        internal void ConfigurePlaybackClock(int clockHz, bool stopAtProtectedStageHandoffs = false)
        {
            if (clockHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(clockHz));

            playbackClockHz = clockHz;
            this.stopAtProtectedStageHandoffs = stopAtProtectedStageHandoffs;
        }

        public bool ReadEarBit(ulong currentTStates)
        {
            if (playbackPaused)
                return earLevel;

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

        public bool PausePlayback(ulong currentTStates)
        {
            if (playbackPaused || playbackCompleted)
                return false;

            ReadEarBit(currentTStates);
            pausedPulseElapsedTStates = earPlaybackStarted && currentTStates >= lastEarSampleTStates
                ? currentTStates - lastEarSampleTStates
                : 0;
            playbackPaused = true;
            pausedByStopMarker = false;
            pausedByProtectedStageHandoff = false;
            pauseBoundaryResumeBlockIndex = -1;
            return true;
        }

        public bool ResumePlayback(ulong currentTStates)
        {
            if (!playbackPaused || playbackCompleted)
                return false;

            bool resumeAtBlockBoundary = pausedByStopMarker || pausedByProtectedStageHandoff;
            int resumeBlockIndex = pauseBoundaryResumeBlockIndex;
            playbackPaused = false;
            pausedByStopMarker = false;
            pausedByProtectedStageHandoff = false;
            pauseBoundaryResumeBlockIndex = -1;

            if (resumeAtBlockBoundary)
            {
                pausedPulseElapsedTStates = 0;
                StartEarPlaybackBlock(resumeBlockIndex, preserveSignalPhase: true);
                if (!playbackPaused && earPlaybackState != EarPlaybackState.Idle)
                {
                    earPlaybackStarted = true;
                    lastEarSampleTStates = currentTStates;
                }

                return true;
            }

            if (earPlaybackStarted)
            {
                lastEarSampleTStates = currentTStates >= pausedPulseElapsedTStates
                    ? currentTStates - pausedPulseElapsedTStates
                    : currentTStates;
            }

            pausedPulseElapsedTStates = 0;
            return true;
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
            if (playbackPaused || !machine.SupportsRomTapeAcceleration)
                return false;

            bool isSyncLoopTrap = cpu.Regs.PC == machine.RomProfile.LoadBytesSyncLoopAddress;
            if (cpu.Regs.PC != machine.RomProfile.LoadBytesTrapAddress && !isSyncLoopTrap)
                return false;

            bool success = false;
            ushort? romDrivenBasicAutoStartLine = null;
            byte expectedFlag = cpu.Regs.A;
            bool isLoad = (cpu.Regs.F & FlagCarry) != 0;

            ushort expectedLength = cpu.Regs.DE;
            ushort destination = cpu.Regs.IX;
            ushort callerReturnAddress = PeekWord(machine, cpu.Regs.SP);
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
                ResolveExpectedRomFlag(
                    cpu,
                    playbackTrapBlockIndex,
                    ref expectedFlag,
                    ref isLoad);
                machine.Trace?.Invoke(
                    $"[RomTrap] request pc=0x{cpu.Regs.PC:X4} next={nextBlockIndex} active={playbackTrapBlockIndex} " +
                    $"AF=0x{cpu.Regs.AF:X4} A'F'=0x{cpu.Regs.A_:X2}{cpu.Regs.F_:X2} selected=0x{expectedFlag:X2} " +
                    $"DE={expectedLength} IX=0x{destination:X4} load={(isLoad ? 1 : 0)} " +
                    $"return=0x{callerReturnAddress:X4}/0x{PeekWord(machine, (ushort)(cpu.Regs.SP + 2)):X4}");
                bool reclaimActivePlaybackBlock =
                    playbackTrapBlockIndex >= 0 &&
                    playbackTrapBlockIndex < nextBlockIndex &&
                    (RomBlockMatchesRequest(
                         blocks[playbackTrapBlockIndex],
                         expectedFlag,
                         expectedLength) ||
                     RomBlockMatchesLength(blocks[playbackTrapBlockIndex], expectedLength));
                bool usingPlaybackTrapBlock =
                    playbackTrapBlockIndex > nextBlockIndex || reclaimActivePlaybackBlock;
                if (usingPlaybackTrapBlock)
                {
                    if (reclaimActivePlaybackBlock)
                    {
                        nextBlockIndex = playbackTrapBlockIndex;
                        state = TapeState.ExpectData;
                    }
                    else
                    {
                        while (nextBlockIndex < playbackTrapBlockIndex)
                            AdvanceBlockState(blocks[nextBlockIndex]);
                        state = TapeState.Idle;
                    }

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

                int consumedBlockIndex = nextBlockIndex;
                TapeBlock block = blocks[consumedBlockIndex];

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

                            bool executedLoadedProgram = TapLoader.TryExecuteLoadedMountedBasicProgram(
                                machine,
                                pendingHeaderInfo.ProgramLength,
                                (ushort)block.Payload!.Length,
                                pendingHeaderInfo.AutoStartLine);
                            if (!executedLoadedProgram &&
                                !machine.HasPendingMountedLoadUsrContinuation &&
                                pendingHeaderInfo.AutoStartLine < 32768 &&
                                TapLoader.CanStartRomDrivenMountedBasicAutoStart(machine))
                            {
                                TapLoader.LoadBasicProgram(
                                    machine,
                                    pendingHeaderInfo,
                                    block.Payload!,
                                    preserveInterpreterWorkspace: false);
                                romDrivenBasicAutoStartLine = pendingHeaderInfo.AutoStartLine;
                            }

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
                if (completedRomLoadableBlockIndex == consumedBlockIndex)
                    completedRomLoadableBlockIndex = -1;
                SyncEarPlaybackToNextBlock(block.PauseAfterBlockMs);

                if (romDrivenBasicAutoStartLine.HasValue)
                {
                    TapLoader.StartRomDrivenMountedBasicAutoStart(
                        machine,
                        romDrivenBasicAutoStartLine.Value);
                    return true;
                }

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
                    return machine.RomProfile.TapeReturnAddress;
                }

                return PeekWord(machine, cpu.Regs.SP);
            }

            if (hasUnstructuredStandardRomLoadContext)
                return PeekWord(machine, cpu.Regs.SP);

            return machine.RomProfile.TapeReturnAddress;
        }

        public BootstrapTapeLoadResult TryConsumeBootstrapLoad(Spectrum128Machine machine)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (playbackPaused)
                return BootstrapTapeLoadResult.None;

            ulong consumedTStates = 0;
            while (nextBlockIndex < blocks.Count &&
                   !blocks[nextBlockIndex].IsLoadableRomBlock &&
                   blocks[nextBlockIndex].Kind != TapeBlockKind.StopTape)
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

                while (nextBlockIndex < blocks.Count &&
                       !blocks[nextBlockIndex].IsLoadableRomBlock &&
                       blocks[nextBlockIndex].Kind != TapeBlockKind.StopTape)
                {
                    consumedTStates += TapLoader.EstimateTapeBlockDurationTStates(blocks[nextBlockIndex]);
                    AdvanceBlockState(blocks[nextBlockIndex]);
                }
            }

            if (nextBlockIndex >= blocks.Count)
                return BootstrapTapeLoadResult.None;

            TapeBlock dataBlock = blocks[nextBlockIndex];
            if (dataBlock.Kind == TapeBlockKind.StopTape)
                return BootstrapTapeLoadResult.None;

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

        private static bool RomBlockMatchesRequest(
            TapeBlock block,
            byte expectedFlag,
            ushort expectedLength) =>
            CanUseRomLoadTrap(block) &&
            block.Flag == expectedFlag &&
            block.Payload!.Length == expectedLength;

        private static bool RomBlockMatchesLength(TapeBlock block, ushort expectedLength) =>
            CanUseRomLoadTrap(block) &&
            block.Payload!.Length == expectedLength;

        private void ResolveExpectedRomFlag(
            Z80Cpu cpu,
            int playbackTrapBlockIndex,
            ref byte expectedFlag,
            ref bool isLoad)
        {
            if (TryResolveExpectedRomFlagFromBlock(nextBlockIndex, cpu, ref expectedFlag, ref isLoad))
                return;

            if (playbackTrapBlockIndex != nextBlockIndex &&
                TryResolveExpectedRomFlagFromBlock(playbackTrapBlockIndex, cpu, ref expectedFlag, ref isLoad))
            {
                return;
            }

            if (expectedFlag != HeaderFlag &&
                expectedFlag != DataFlag &&
                (cpu.Regs.A_ == HeaderFlag || cpu.Regs.A_ == DataFlag))
            {
                expectedFlag = cpu.Regs.A_;
                isLoad = (cpu.Regs.F_ & FlagCarry) != 0;
            }
        }

        private bool TryResolveExpectedRomFlagFromBlock(
            int blockIndex,
            Z80Cpu cpu,
            ref byte expectedFlag,
            ref bool isLoad)
        {
            if (blockIndex < 0 ||
                blockIndex >= blocks.Count ||
                !CanUseRomLoadTrap(blocks[blockIndex]))
            {
                return false;
            }

            byte blockFlag = blocks[blockIndex].Flag;
            if (cpu.Regs.A_ == blockFlag)
            {
                expectedFlag = cpu.Regs.A_;
                isLoad = (cpu.Regs.F_ & FlagCarry) != 0;
                return true;
            }

            return cpu.Regs.A == blockFlag;
        }

        private static bool IsRomTrapByteStreamBlock(TapeBlock block) =>
            block.Kind == TapeBlockKind.Data &&
            block.CanUseRomLoadTrap &&
            !block.IsLoadableRomBlock &&
            block.StreamData != null &&
            block.Payload != null;

        private int GetActiveRomTrapPlaybackBlockIndex()
        {
            if (earPlaybackBlockIndex >= 0 &&
                earPlaybackBlockIndex < blocks.Count &&
                CanUseRomLoadTrap(blocks[earPlaybackBlockIndex]))
            {
                int activeBlockIndex = earPlaybackState switch
                {
                    EarPlaybackState.Data => earPlaybackBlockIndex,
                    EarPlaybackState.Pause => earPlaybackBlockIndex,
                    EarPlaybackState.Idle when earPlaybackStarted || retainedByteStreamTrapAvailable => earPlaybackBlockIndex,
                    _ => -1
                };
                if (activeBlockIndex >= 0)
                    return activeBlockIndex;
            }

            return completedRomLoadableBlockIndex >= 0 &&
                   nextBlockIndex == completedRomLoadableBlockIndex + 1
                ? completedRomLoadableBlockIndex
                : -1;
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
                pendingPrePlaybackPauseTStates = ScaleMillisecondsToPlaybackClock(block.PauseAfterBlockMs);
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
                {
                    AdvanceBlockState(block);
                    completedRomLoadableBlockIndex = blockIndex;
                }

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
                if (desiredBlockIndex >= blocks.Count &&
                    !HasRemainingBlocks &&
                    earPlaybackState == EarPlaybackState.Idle &&
                    !retainedByteStreamTrapAvailable)
                {
                    playbackCompleted = true;
                }
                return;
            }

            pendingPrePlaybackPauseTStates = ScaleMillisecondsToPlaybackClock(consumedBlockPauseMs);
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
                earPauseLowTailTStates = 0;
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
                    BeginPause(block, blockIndex + 1);
                    return;

                case TapeBlockKind.StopTape:
                    nextBlockIndex = Math.Max(nextBlockIndex, blockIndex + 1);
                    PauseAtStopMarker(blockIndex + 1);
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

        private void BeginPause(TapeBlock block, int nextBlockIndex)
        {
            if (ShouldStopAtProtectedStageHandoff(block, nextBlockIndex))
            {
                PauseAtProtectedStageHandoff(nextBlockIndex);
                return;
            }

            int pauseTStates = GetPauseLengthTStates(block, nextBlockIndex);
            earPlaybackState = EarPlaybackState.Pause;
            earNextBlockIndexAfterPause = nextBlockIndex;

            if (earLevel)
            {
                // Finish the last edge at its current level before TZX silence settles low.
                int terminatingPulseTStates = Math.Min(
                    ScaleToPlaybackClock(PauseLevelSettlingTStates),
                    pauseTStates);
                earPulseLengthTStates = terminatingPulseTStates;
                earPauseLowTailTStates = pauseTStates - terminatingPulseTStates;
                return;
            }

            earPulseLengthTStates = pauseTStates;
            earPauseLowTailTStates = 0;
        }

        private void PauseAtStopMarker(int resumeBlockIndex)
        {
            playbackPaused = true;
            pausedByStopMarker = true;
            pausedByProtectedStageHandoff = false;
            pausedPulseElapsedTStates = 0;
            pauseBoundaryResumeBlockIndex = resumeBlockIndex;
            earPlaybackState = EarPlaybackState.Idle;
            earPulseLengthTStates = 0;
        }

        private bool ShouldStopAtProtectedStageHandoff(TapeBlock block, int nextBlockIndex) =>
            stopAtProtectedStageHandoffs &&
            block.Kind is TapeBlockKind.Data or TapeBlockKind.DirectRecording &&
            !block.IsLoadableRomBlock &&
            block.PauseAfterBlockMs >= ProtectedStageHandoffPauseMs &&
            GetEarPlaybackStartBlockIndex(nextBlockIndex) < blocks.Count;

        private void PauseAtProtectedStageHandoff(int resumeBlockIndex)
        {
            playbackPaused = true;
            pausedByStopMarker = false;
            pausedByProtectedStageHandoff = true;
            pausedPulseElapsedTStates = 0;
            pauseBoundaryResumeBlockIndex = resumeBlockIndex;
            earPlaybackState = EarPlaybackState.Idle;
            earPulseLengthTStates = 0;
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
                        BeginPause(block, earPlaybackBlockIndex + 1);
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
                            BeginPause(block, earPlaybackBlockIndex + 1);
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
                        BeginPause(block, earPlaybackBlockIndex + 1);
                        return;
                    }

                    StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                    return;

                case EarPlaybackState.Pause:
                    if (earPauseLowTailTStates > 0)
                    {
                        earLevel = false;
                        earPulseLengthTStates = earPauseLowTailTStates;
                        earPauseLowTailTStates = 0;
                        return;
                    }

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
            int clamped = Math.Max(1, ScaleToPlaybackClock(tStates));
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

        private int GetPauseLengthTStates(TapeBlock block, int nextBlockIndex)
        {
            int pauseMs = block.PauseAfterBlockMs;
            if (GetEarPlaybackStartBlockIndex(nextBlockIndex) >= blocks.Count)
                pauseMs = Math.Min(pauseMs, MaximumTerminalPauseMs);

            int basePauseTStates = ScaleMillisecondsToPlaybackClock(pauseMs);
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

        private int ScaleMillisecondsToPlaybackClock(int milliseconds)
        {
            if (milliseconds <= 0)
                return 0;

            return checked((int)Math.Max(
                1L,
                ((long)milliseconds * playbackClockHz + 500L) / 1000L));
        }

        private int ScaleToPlaybackClock(int tapeTStates)
        {
            if (tapeTStates <= 0)
                return 0;

            return checked((int)Math.Max(
                1L,
                ((long)tapeTStates * playbackClockHz + (TapeTimingClockHz / 2L)) /
                TapeTimingClockHz));
        }

        private void BeginEndOfStreamIdleTransition(bool preserveSignalPhase)
        {
            retainedByteStreamTrapAvailable = false;

            if (earPlaybackBlockIndex < 0 || earPlaybackBlockIndex >= blocks.Count)
            {
                earPlaybackState = EarPlaybackState.Idle;
                earPulseLengthTStates = 0;
                earLevel = true;
                earPlaybackStarted = false;
                playbackCompleted = true;
                return;
            }

            // A TZX stream with no following block has no implicit tail pulses.
            // Preserve its final EAR level so protected loaders observe tape silence.
            earPlaybackState = EarPlaybackState.Idle;
            earPulseLengthTStates = 0;
            if (!preserveSignalPhase)
            {
                earLevel = true;
                earPlaybackStarted = false;
            }
            playbackCompleted = true;
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
}
