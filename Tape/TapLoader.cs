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

    public sealed class MountedTape
    {
        private const ushort RomTapeReturnAddress = 0x053F;
        private const ushort RomLoadBytesTrapAddress = 0x056B;
        private const ushort RomLoadBytesSyncLoopAddress = 0x0574;
        private const byte FlagCarry = 0x01;
        private const byte HeaderFlag = 0x00;
        private const byte DataFlag = 0xFF;
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
        private int nextBlockIndex;
        private int earPlaybackBlockIndex;
        private int earStreamByteIndex;
        private int earBitIndex;
        private int earPulseRepeatCount;
        private int earPilotPulsesRemaining;
        private int earPulseLengthTStates;
        private int earPulseSequenceIndex;
        private int earNextBlockIndexAfterPause;
        private ulong lastEarSampleTStates;
        private bool earLevel;
        private bool earPlaybackStarted;
        private EarPlaybackState earPlaybackState;
        private TapeState state;
        private int? expectedDataLength;
        private string? pendingHeaderName;

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
            PureTone
        }

        public MountedTape(
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            int initialBlockIndex = 0,
            bool skipCustomHeaderForEarPlayback = true)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "unnamed.tap" : displayName;
            this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
            this.skipCustomHeaderForEarPlayback = skipCustomHeaderForEarPlayback;
            if (initialBlockIndex < 0 || initialBlockIndex > blocks.Count)
                throw new ArgumentOutOfRangeException(nameof(initialBlockIndex));

            this.initialBlockIndex = initialBlockIndex;
            Reset();
        }

        public string DisplayName { get; }
        public bool HasRemainingBlocks => nextBlockIndex < blocks.Count;
        public bool HasMoreBlocks => HasRemainingBlocks;
        public string DebugPlaybackState =>
            $"NextBlock={nextBlockIndex}/{blocks.Count} EarBlock={earPlaybackBlockIndex}/{blocks.Count} " +
            $"State={state} EarState={earPlaybackState} Byte={earStreamByteIndex} Bit={earBitIndex} " +
            $"Pilot={earPilotPulsesRemaining} PulseLen={earPulseLengthTStates} PulseSeq={earPulseSequenceIndex} " +
            $"EarLevel={(earLevel ? 1 : 0)} Started={(earPlaybackStarted ? 1 : 0)}";

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
            lastEarSampleTStates = 0;
            earLevel = true;
            earPlaybackStarted = false;
            earPlaybackState = EarPlaybackState.Idle;
            expectedDataLength = null;
            pendingHeaderName = null;

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

            if (HasRemainingBlocks)
            {
                while (nextBlockIndex < blocks.Count && !blocks[nextBlockIndex].IsLoadableRomBlock)
                {
                    AdvanceBlockState(blocks[nextBlockIndex]);
                }

                if (!HasRemainingBlocks)
                {
                    CompleteTrap(machine, cpu, success: false, returnAddress: isSyncLoopTrap ? PeekWord(machine, cpu.Regs.SP) : RomTapeReturnAddress);
                    return true;
                }

                TapeBlock block = blocks[nextBlockIndex];
                byte expectedFlag = cpu.Regs.A;
                ushort expectedLength = cpu.Regs.DE;
                ushort destination = cpu.Regs.IX;
                bool isLoad = (cpu.Regs.F & FlagCarry) != 0;

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

                if (state == TapeState.ExpectData)
                {
                    EnsureDataBlock(block);

                    if (expectedDataLength.HasValue && block.Payload!.Length != expectedDataLength.Value)
                    {
                        string displayName = pendingHeaderName ?? "unnamed";
                        throw new InvalidOperationException(
                            $"Tape data block length mismatch for '{displayName}'. Expected {expectedDataLength.Value} bytes, got {block.Payload.Length}.");
                    }
                }

                bool lengthMatches = block.Payload!.Length == expectedLength;
                bool flagMatches = block.Flag == expectedFlag;
                bool canUseEarlySyncTrap = isSyncLoopTrap && lengthMatches;

                if ((flagMatches && lengthMatches) || canUseEarlySyncTrap)
                {
                    success = true;

                    if (isLoad || canUseEarlySyncTrap)
                    {
                        for (int i = 0; i < block.Payload.Length; i++)
                            machine.PokeMemory((ushort)(destination + i), block.Payload[i]);
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
                SyncEarPlaybackToNextBlock();
            }

            CompleteTrap(
                machine,
                cpu,
                success,
                returnAddress: isSyncLoopTrap ? PeekWord(machine, cpu.Regs.SP) : RomTapeReturnAddress);
            return true;
        }

        public bool TryConsumeBootstrapLoad(Spectrum128Machine machine)
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
                return false;

            TapLoader.TapHeaderInfo? header = null;
            if (IsHeaderBlock(blocks[nextBlockIndex]))
            {
                consumedTStates += TapLoader.EstimateTapeBlockDurationTStates(blocks[nextBlockIndex]);
                header = TapLoader.ParseHeaderInfo(blocks[nextBlockIndex]);
                AdvanceBlockState(blocks[nextBlockIndex]);
                SyncEarPlaybackToNextBlock();

                while (nextBlockIndex < blocks.Count && !blocks[nextBlockIndex].IsLoadableRomBlock)
                {
                    consumedTStates += TapLoader.EstimateTapeBlockDurationTStates(blocks[nextBlockIndex]);
                    AdvanceBlockState(blocks[nextBlockIndex]);
                }
            }

            if (nextBlockIndex >= blocks.Count)
                return false;

            TapeBlock dataBlock = blocks[nextBlockIndex];
            EnsureDataBlock(dataBlock);

            if (header != null)
            {
                switch (header.Type)
                {
                    case 0:
                        TapLoader.LoadBasicProgram(machine, header, dataBlock.Payload!);
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

            consumedTStates += TapLoader.EstimateTapeBlockDurationTStates(dataBlock);
            AdvanceBlockState(dataBlock);
            SyncEarPlaybackToNextBlock();
            TapLoader.AdvanceBootstrapTapeTime(machine, consumedTStates);
            return true;
        }

        private void AdvanceBlockState(TapeBlock block)
        {
            nextBlockIndex++;

            if (nextBlockIndex >= blocks.Count)
            {
                state = TapeState.Idle;
                expectedDataLength = null;
                pendingHeaderName = null;
                return;
            }

            if (IsHeaderBlock(block))
            {
                TapLoader.TapHeaderInfo header = TapLoader.ParseHeaderInfo(block);
                expectedDataLength = header.DataLength;
                pendingHeaderName = header.FileName;
                state = TapeState.ExpectData;
                return;
            }

            expectedDataLength = null;
            pendingHeaderName = null;
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

        private static bool IsHeaderBlock(TapeBlock block) => block.IsLoadableRomBlock && block.Flag == HeaderFlag;

        private int FindNextLoadableBlockIndex(int startIndex)
        {
            int index = startIndex;
            while (index < blocks.Count && !blocks[index].IsLoadableRomBlock)
                index++;

            return index;
        }

        private void SyncEarPlaybackToNextBlock()
        {
            int desiredBlockIndex = GetEarPlaybackStartBlockIndex(nextBlockIndex);
            if (desiredBlockIndex == earPlaybackBlockIndex)
                return;

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
                earPlaybackState = EarPlaybackState.Idle;
                earPulseLengthTStates = 0;
                earLevel = false;
                if (!preserveSignalPhase)
                    earPlaybackStarted = false;
                return;
            }

            earPlaybackBlockIndex = blockIndex;
            earStreamByteIndex = 0;
            earBitIndex = 0;
            earPulseRepeatCount = 0;
            earPulseSequenceIndex = 0;
            earNextBlockIndexAfterPause = blockIndex + 1;
            if (!preserveSignalPhase)
            {
                earLevel = true;
                earPlaybackStarted = false;
            }

            TapeBlock block = blocks[blockIndex];
            switch (block.Kind)
            {
                case TapeBlockKind.Data:
                    earPilotPulsesRemaining = block.PilotPulseCount;
                    if (earPilotPulsesRemaining > 0)
                    {
                        earPlaybackState = EarPlaybackState.Pilot;
                        earPulseLengthTStates = block.PilotPulseLength;
                    }
                    else if (block.SyncFirstPulseLength != 0)
                    {
                        earPlaybackState = EarPlaybackState.SyncFirst;
                        earPulseLengthTStates = block.SyncFirstPulseLength;
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
                    earPulseLengthTStates = block.PureTonePulseLength;
                    return;

                case TapeBlockKind.PulseSequence:
                    earPlaybackState = EarPlaybackState.PulseSequence;
                    earPulseLengthTStates = block.PulseSequence![0];
                    return;

                case TapeBlockKind.DirectRecording:
                    earPlaybackState = EarPlaybackState.DirectRecording;
                    earPulseLengthTStates = block.DirectRecordingSampleTStates;
                    earLevel = GetCurrentDirectRecordingLevel(block);
                    return;

                case TapeBlockKind.Pause:
                    earPlaybackState = EarPlaybackState.Pause;
                    earPulseLengthTStates = Math.Max(1, block.PauseAfterBlockMs * 3500);
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
                        earPulseLengthTStates = block.PilotPulseLength;
                        return;
                    }

                    if (block.SyncFirstPulseLength != 0)
                    {
                        earPlaybackState = EarPlaybackState.SyncFirst;
                        earPulseLengthTStates = block.SyncFirstPulseLength;
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
                    earPulseLengthTStates = block.SyncSecondPulseLength;
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
                    }

                    if (earStreamByteIndex < blocks[earPlaybackBlockIndex].StreamByteCount)
                    {
                        earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                        return;
                    }

                    if (block.PauseAfterBlockMs != 0)
                    {
                        earPlaybackState = EarPlaybackState.Pause;
                        earPulseLengthTStates = Math.Max(1, block.PauseAfterBlockMs * 3500);
                        earLevel = false;
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
                            earPulseLengthTStates = Math.Max(1, block.PauseAfterBlockMs * 3500);
                            earLevel = false;
                            return;
                        }

                        StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                        return;
                    }

                    earLevel = GetCurrentDirectRecordingLevel(block);
                    earPulseLengthTStates = block.DirectRecordingSampleTStates;
                    return;
                }

                case EarPlaybackState.PureTone:
                    earLevel = !earLevel;
                    earPilotPulsesRemaining--;
                    if (earPilotPulsesRemaining > 0)
                    {
                        earPulseLengthTStates = block.PureTonePulseLength;
                        return;
                    }

                    StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                    return;

                case EarPlaybackState.PulseSequence:
                    earLevel = !earLevel;
                    earPulseSequenceIndex++;
                    if (block.PulseSequence != null && earPulseSequenceIndex < block.PulseSequence.Length)
                    {
                        earPulseLengthTStates = block.PulseSequence[earPulseSequenceIndex];
                        return;
                    }

                    if (block.PauseAfterBlockMs != 0)
                    {
                        earPlaybackState = EarPlaybackState.Pause;
                        earPulseLengthTStates = Math.Max(1, block.PauseAfterBlockMs * 3500);
                        earLevel = false;
                        return;
                    }

                    StartEarPlaybackBlock(earPlaybackBlockIndex + 1, preserveSignalPhase: true);
                    return;

                case EarPlaybackState.Pause:
                    StartEarPlaybackBlock(earNextBlockIndexAfterPause, preserveSignalPhase: false);
                    return;

                default:
                    earPlaybackState = EarPlaybackState.Idle;
                    earPulseLengthTStates = 0;
                    earLevel = false;
                    return;
            }
        }

        private int GetCurrentBitPulseLengthTStates()
        {
            TapeBlock block = blocks[earPlaybackBlockIndex];
            byte streamByte = block.GetStreamByte(earStreamByteIndex);
            bool bitSet = ((streamByte >> (7 - earBitIndex)) & 0x01) != 0;
            return bitSet ? block.OneBitPulseLength : block.ZeroBitPulseLength;
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

        private const ushort BasicProgramStart = 23755;
        private const ushort MainExecutionLoopAddress = 0x1555;
        private const ushort UsrReturnAddress = 0x2D2B;
        private const ushort EndCalcLiteralAddress = 0x2758;
        private const ushort DefaultStackPointer = 0xFF58;
        private const ushort RomSystemVariablesBase = 0x5C3A;

        private const ushort NewPpcAddress = 23618;
        private const ushort BorderSystemVariableAddress = 23624;
        private const ushort VarsAddress = 23627;
        private const ushort ProgAddress = 23635;
        private const ushort NextLineAddress = 23637;
        private const ushort DataAddress = 23639;
        private const ushort EditLineAddress = 23641;
        private const ushort WorkspaceAddress = 23649;
        private const ushort StackBottomAddress = 23651;
        private const ushort StackEndAddress = 23653;
        private const ushort RamTopAddress = 23730;
        private const ushort PhysicalRamTopAddress = 23732;
        private const ushort Spectrum128TapeLoadBankSelectAddress = 23388;

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
            bool skipCustomHeaderForEarPlayback = true)
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
                skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback);
            machine.MountTape(tape);

            if (effectiveHeader.AutoStartLine < 32768)
            {
                ExecuteBootstrapBasicAutoStart(
                    machine,
                    BasicProgramStart,
                    effectiveProgramLength,
                    effectiveHeader.AutoStartLine);
            }

            int currentPhase = (int)(machine.Cpu.TStates % (ulong)machine.FrameTStates);
            machine.SetSnapshotResumeFramePhase(currentPhase);
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

        internal static TapBootstrapResult LoadAllStandardTapeBlocksAndAutoStart(
            Spectrum128Machine machine,
            string displayName,
            IReadOnlyList<TapeBlock> blocks,
            bool skipCustomHeaderForEarPlayback = true,
            bool remountPlaybackRemainder = true,
            bool stopBeforeFirstCustomHeader = false)
        {
            if (!CanLoadAllStandardTapeBlocks(blocks))
                throw new InvalidOperationException("The tape image contains nonstandard blocks and cannot be fully fake-loaded.");

            bool use128kMode = Requires128kTapeLoadModeForStandardTape(machine, blocks);
            if (RequiresMountedLoadSemanticsForStandardTape(machine, blocks, use128kMode))
            {
                return BootstrapTapeBlocksAndMountRemaining(
                    machine,
                    displayName,
                    blocks,
                    skipCustomHeaderForEarPlayback);
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
                    ignoreLoadStatements: true);
            }

            if (TryFindTapCustomLoaderResumePc(machine, blocks, out ushort resumePc))
                machine.Cpu.Regs.PC = resumePc;

            if (remountPlaybackRemainder && playbackStartBlockIndex.HasValue)
            {
                int adjustedPlaybackStartBlockIndex = SkipSatisfiedStandardLoads(blocks, playbackStartBlockIndex.Value, ignoredLoadCount);
                if (adjustedPlaybackStartBlockIndex < blocks.Count)
                {
                    var playbackTape = new MountedTape(
                        displayName,
                        blocks,
                        adjustedPlaybackStartBlockIndex,
                        skipCustomHeaderForEarPlayback: skipCustomHeaderForEarPlayback);
                    machine.MountTape(playbackTape);
                }
            }

            if (machine.HasMountedTape && !machine.MountedTape!.HasRemainingBlocks)
            {
                machine.EjectTape();
            }

            int currentPhase = (int)(machine.Cpu.TStates % (ulong)machine.FrameTStates);
            machine.SetSnapshotResumeFramePhase(currentPhase);

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
                else if (block.Flag == HeaderFlag)
                {
                    TapHeaderInfo header = ParseHeaderInfo(block);
                    Console.WriteLine($"[TAP] Block {i}: HEADER {GetHeaderTypeName(header.Type)} '{header.FileName}' len={header.DataLength}");
                }
                else
                {
                    Console.WriteLine($"[TAP] Block {i}: DATA flag=0x{block.Flag:X2} len={block.Payload?.Length ?? 0}");
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

        private static ulong EstimateDataBlockDurationTStates(TapeBlock block)
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

        private static void LoadDataBlock(Spectrum128Machine machine, TapHeaderInfo header, byte[] payload)
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

        internal static void LoadBasicProgram(Spectrum128Machine machine, TapHeaderInfo header, byte[] payload)
        {
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
            WriteWord(machine, EditLineAddress, endAddress);
            WriteWord(machine, WorkspaceAddress, endAddress);
            WriteWord(machine, StackBottomAddress, endAddress);
            WriteWord(machine, StackEndAddress, endAddress);

            machine.PokeMemory(endAddress, 0x0D);

            if (header.AutoStartLine < 32768)
            {
                WriteWord(machine, NewPpcAddress, header.AutoStartLine);
                machine.PokeMemory((ushort)(NewPpcAddress + 2), 0);
            }
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

        private static int ExecuteBootstrapBasicAutoStart(
            Spectrum128Machine machine,
            ushort programStart,
            ushort programLength,
            ushort autoStartLine,
            bool ignoreLoadStatements = false)
        {
            if (programLength == 0)
                return 0;

            var executor = new BasicBootstrapExecutor(machine, programStart, programLength, ignoreLoadStatements);
            executor.Execute(autoStartLine);
            return executor.IgnoredLoadCount;
        }

        private static ushort ReadWord(Spectrum128Machine machine, ushort address)
        {
            return (ushort)(machine.PeekMemory(address) | (machine.PeekMemory((ushort)(address + 1)) << 8));
        }

        private static void WriteWord(Spectrum128Machine machine, ushort address, ushort value)
        {
            machine.PokeMemory(address, (byte)(value & 0xFF));
            machine.PokeMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        private sealed class BasicBootstrapExecutor
        {
            private readonly Spectrum128Machine machine;
            private readonly List<BasicLine> lines;
            private readonly Queue<int> dataValues;
            private readonly Dictionary<string, int> variables = new(StringComparer.OrdinalIgnoreCase);
            private readonly bool ignoreLoadStatements;
            public int IgnoredLoadCount { get; private set; }

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

                    string keyword = statement[0];
                    switch (keyword)
                    {
                        case "REM":
                            statementIndex = line.Statements.Count;
                            break;

                        case "BORDER":
                        case "PAPER":
                        case "INK":
                        case "CLS":
                            break;

                        case "LOAD":
                            if (ignoreLoadStatements)
                            {
                                IgnoredLoadCount++;
                            }
                            else if (!machine.TryConsumeBootstrapTapeLoad())
                                throw new InvalidOperationException("BASIC LOAD could not consume a mounted tape block during bootstrap.");
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

                        case "READ":
                        {
                            if (statement.Count < 2 || dataValues.Count == 0)
                                throw new InvalidOperationException("Malformed BASIC READ statement in tape bootstrap.");

                            variables[statement[1]] = dataValues.Dequeue();
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

            private void ApplyClear(int clearAddress)
            {
                ushort top = (ushort)Math.Clamp(clearAddress, 0x5D00, 0xFFFF);
                WriteWord(machine, RamTopAddress, top);
                WriteWord(machine, PhysicalRamTopAddress, top);
                WriteWord(machine, StackBottomAddress, top);
                WriteWord(machine, StackEndAddress, top);
                if (machine.Cpu.Regs.SP > top)
                    machine.Cpu.Regs.SP = top;
            }

            private IEnumerable<int> CollectDataValues()
            {
                foreach (BasicLine line in lines)
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

                    parsedLines.Add(new BasicLine(lineNumber, SplitStatements(Tokenize(lineBytes))));
                    cursor = nextLineAddress;
                }

                return parsedLines;
            }

            private static List<List<string>> SplitStatements(List<string> tokens)
            {
                var statements = new List<List<string>>();
                var current = new List<string>();
                foreach (string token in tokens)
                {
                    if (token == ":")
                    {
                        statements.Add(current);
                        current = new List<string>();
                    }
                    else
                    {
                        current.Add(token);
                    }
                }

                statements.Add(current);
                return statements;
            }

            private static List<string> Tokenize(byte[] lineBytes)
            {
                var tokens = new List<string>();
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
                        tokens.Add(keyword!);
                        continue;
                    }

                    char c = (char)b;
                    if (char.IsDigit(c))
                    {
                        int start = i;
                        while (i + 1 < lineBytes.Length && char.IsDigit((char)lineBytes[i + 1]))
                            i++;
                        tokens.Add(System.Text.Encoding.ASCII.GetString(lineBytes, start, i - start + 1));
                        continue;
                    }

                    if (char.IsLetter(c))
                    {
                        int start = i;
                        while (i + 1 < lineBytes.Length && char.IsLetterOrDigit((char)lineBytes[i + 1]))
                            i++;
                        tokens.Add(System.Text.Encoding.ASCII.GetString(lineBytes, start, i - start + 1));
                        continue;
                    }

                    if (c == '"')
                    {
                        int start = ++i;
                        while (i < lineBytes.Length && (char)lineBytes[i] != '"')
                            i++;
                        tokens.Add(System.Text.Encoding.ASCII.GetString(lineBytes, start, Math.Max(0, i - start)));
                        continue;
                    }

                    if ("()+-*=,:".IndexOf(c) >= 0)
                        tokens.Add(c.ToString());
                }

                return tokens;
            }

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

                        string keyword = statement[0];
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
                    231 => "BORDER",
                    239 => "LOAD",
                    244 => "POKE",
                    249 => "RANDOMIZE",
                    228 => "DATA",
                    235 => "FOR",
                    175 => "CODE",
                    242 => "PAUSE",
                    243 => "NEXT",
                    251 => "CLS",
                    253 => "CLEAR",
                    254 => "RETURN",
                    204 => "TO",
                    _ => null
                };

                return keyword != null;
            }

            private readonly record struct BasicLine(ushort Number, List<List<string>> Statements);
            private readonly record struct ForFrame(string VariableName, int EndValue, int LineIndex, int StatementIndex);
        }

        private sealed class ExpressionParser
        {
            private readonly Spectrum128Machine machine;
            private readonly Dictionary<string, int> variables;
            private readonly List<string> tokens;
            private readonly int endIndex;
            private int index;

            public ExpressionParser(
                Spectrum128Machine machine,
                Dictionary<string, int> variables,
                List<string> tokens,
                int startIndex,
                int endIndex)
            {
                this.machine = machine;
                this.variables = variables;
                this.tokens = tokens;
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
