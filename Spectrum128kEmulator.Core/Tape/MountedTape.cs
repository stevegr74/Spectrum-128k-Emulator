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
        private readonly IReadOnlyList<TapeBlock> blocks;
        private readonly bool skipCustomHeaderForEarPlayback;
        private readonly int initialBlockIndex;
        private readonly int initialPrePlaybackPauseTStates;
        private readonly int nonRomTimingDivisor;
        private readonly int loadableTimingDivisor;
        private readonly bool initialEarLevelHigh;
        private readonly bool allowRomLoadTraps;
        private int playbackClockHz = TapeTimingClockHz;
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
        private ulong pausedPulseElapsedTStates;
        private int stopMarkerResumeBlockIndex;
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
            bool initialEarLevelHigh = true,
            bool allowRomLoadTraps = true)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "unnamed.tap" : displayName;
            this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
            this.skipCustomHeaderForEarPlayback = skipCustomHeaderForEarPlayback;
            this.initialPrePlaybackPauseTStates = Math.Max(0, initialPrePlaybackPauseTStates);
            this.nonRomTimingDivisor = Math.Max(1, nonRomTimingDivisor);
            this.loadableTimingDivisor = Math.Max(1, loadableTimingDivisor);
            this.initialEarLevelHigh = initialEarLevelHigh;
            this.allowRomLoadTraps = allowRomLoadTraps;
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
            $"Paused={(playbackPaused ? 1 : 0)} Marker={(pausedByStopMarker ? 1 : 0)} Complete={(playbackCompleted ? 1 : 0)} " +
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
            pausedPulseElapsedTStates = 0;
            stopMarkerResumeBlockIndex = -1;
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

        internal void ConfigurePlaybackClock(int clockHz)
        {
            if (clockHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(clockHz));

            playbackClockHz = clockHz;
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
            stopMarkerResumeBlockIndex = -1;
            return true;
        }

        public bool ResumePlayback(ulong currentTStates)
        {
            if (!playbackPaused || playbackCompleted)
                return false;

            bool resumeAfterMarker = pausedByStopMarker;
            int resumeBlockIndex = stopMarkerResumeBlockIndex;
            playbackPaused = false;
            pausedByStopMarker = false;
            stopMarkerResumeBlockIndex = -1;

            if (resumeAfterMarker)
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
            pausedPulseElapsedTStates = 0;
            stopMarkerResumeBlockIndex = resumeBlockIndex;
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
