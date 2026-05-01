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

        private readonly IReadOnlyList<TapLoader.TapBlock> blocks;
        private readonly int initialBlockIndex;
        private int nextBlockIndex;
        private int earPlaybackBlockIndex;
        private int earStreamByteIndex;
        private int earBitIndex;
        private int earPulseRepeatCount;
        private int earPilotPulsesRemaining;
        private int earPulseLengthTStates;
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
            Data
        }

        public MountedTape(string displayName, IReadOnlyList<TapLoader.TapBlock> blocks, int initialBlockIndex = 0)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "unnamed.tap" : displayName;
            this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
            if (initialBlockIndex < 0 || initialBlockIndex > blocks.Count)
                throw new ArgumentOutOfRangeException(nameof(initialBlockIndex));

            this.initialBlockIndex = initialBlockIndex;
            Reset();
        }

        public string DisplayName { get; }
        public bool HasRemainingBlocks => nextBlockIndex < blocks.Count;
        public bool HasMoreBlocks => HasRemainingBlocks;

        public void Reset()
        {
            nextBlockIndex = initialBlockIndex;
            earPlaybackBlockIndex = initialBlockIndex;
            earStreamByteIndex = 0;
            earBitIndex = 0;
            earPulseRepeatCount = 0;
            earPilotPulsesRemaining = 0;
            earPulseLengthTStates = 0;
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

            state = IsHeaderBlock(blocks[initialBlockIndex]) ? TapeState.ExpectHeader : TapeState.ExpectData;
            StartEarPlaybackBlock(initialBlockIndex);
        }

        public bool ReadEarBit(ulong currentTStates)
        {
            if (earPlaybackState == EarPlaybackState.Idle)
                return true;

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
            if (cpu.Regs.PC != RomLoadBytesTrapAddress)
                return false;

            bool success = false;

            if (HasRemainingBlocks)
            {
                TapLoader.TapBlock block = blocks[nextBlockIndex];
                byte expectedFlag = cpu.Regs.A;
                ushort expectedLength = cpu.Regs.DE;
                ushort destination = cpu.Regs.IX;
                bool isLoad = (cpu.Regs.F & FlagCarry) != 0;

                if (state == TapeState.ExpectHeader && !IsHeaderBlock(block))
                {
                    throw new InvalidOperationException(
                        $"Tape sequencing error: expected a header block, found flag 0x{block.Flag:X2}.");
                }

                if (state == TapeState.ExpectData)
                {
                    EnsureDataBlock(block);

                    if (expectedDataLength.HasValue && block.Payload.Length != expectedDataLength.Value)
                    {
                        string displayName = pendingHeaderName ?? "unnamed";
                        throw new InvalidOperationException(
                            $"Tape data block length mismatch for '{displayName}'. Expected {expectedDataLength.Value} bytes, got {block.Payload.Length}.");
                    }
                }

                if (block.Flag == expectedFlag && block.Payload.Length == expectedLength)
                {
                    success = true;

                    if (isLoad)
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
            }

            CompleteTrap(cpu, success);
            return true;
        }

        private void AdvanceBlockState(TapLoader.TapBlock block)
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
            state = IsHeaderBlock(blocks[nextBlockIndex]) ? TapeState.ExpectHeader : TapeState.ExpectData;
        }

        private static void EnsureDataBlock(TapLoader.TapBlock block)
        {
            if (block.Flag != DataFlag)
            {
                throw new InvalidOperationException(
                    $"Tape sequencing error: expected a data block, found flag 0x{block.Flag:X2}.");
            }
        }

        private static bool IsHeaderBlock(TapLoader.TapBlock block) => block.Flag == HeaderFlag;

        private void StartEarPlaybackBlock(int blockIndex)
        {
            if (blockIndex < 0 || blockIndex >= blocks.Count)
            {
                earPlaybackState = EarPlaybackState.Idle;
                earPulseLengthTStates = 0;
                earLevel = true;
                return;
            }

            earPlaybackBlockIndex = blockIndex;
            earStreamByteIndex = 0;
            earBitIndex = 0;
            earPulseRepeatCount = 0;
            earPilotPulsesRemaining = IsHeaderBlock(blocks[blockIndex]) ? HeaderPilotPulseCount : DataPilotPulseCount;
            earPlaybackState = EarPlaybackState.Pilot;
            earPulseLengthTStates = PilotPulseLengthTStates;
            earLevel = true;
            earPlaybackStarted = false;
        }

        private void AdvanceEarPulse()
        {
            earLevel = !earLevel;

            switch (earPlaybackState)
            {
                case EarPlaybackState.Pilot:
                    earPilotPulsesRemaining--;
                    if (earPilotPulsesRemaining > 0)
                    {
                        earPulseLengthTStates = PilotPulseLengthTStates;
                        return;
                    }

                    earPlaybackState = EarPlaybackState.SyncFirst;
                    earPulseLengthTStates = SyncFirstPulseLengthTStates;
                    return;

                case EarPlaybackState.SyncFirst:
                    earPlaybackState = EarPlaybackState.SyncSecond;
                    earPulseLengthTStates = SyncSecondPulseLengthTStates;
                    return;

                case EarPlaybackState.SyncSecond:
                    earPlaybackState = EarPlaybackState.Data;
                    earPulseRepeatCount = 0;
                    earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                    return;

                case EarPlaybackState.Data:
                    earPulseRepeatCount++;
                    if (earPulseRepeatCount < 2)
                    {
                        earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                        return;
                    }

                    earPulseRepeatCount = 0;
                    earBitIndex++;
                    if (earBitIndex >= 8)
                    {
                        earBitIndex = 0;
                        earStreamByteIndex++;
                    }

                    if (earStreamByteIndex < blocks[earPlaybackBlockIndex].StreamByteCount)
                    {
                        earPulseLengthTStates = GetCurrentBitPulseLengthTStates();
                        return;
                    }

                    StartEarPlaybackBlock(earPlaybackBlockIndex + 1);
                    return;

                default:
                    earPlaybackState = EarPlaybackState.Idle;
                    earPulseLengthTStates = 0;
                    earLevel = true;
                    return;
            }
        }

        private int GetCurrentBitPulseLengthTStates()
        {
            TapLoader.TapBlock block = blocks[earPlaybackBlockIndex];
            byte streamByte = block.GetStreamByte(earStreamByteIndex);
            bool bitSet = ((streamByte >> (7 - earBitIndex)) & 0x01) != 0;
            return bitSet ? OneBitPulseLengthTStates : ZeroBitPulseLengthTStates;
        }

        private static void CompleteTrap(Z80Cpu cpu, bool success)
        {
            cpu.Regs.SP += 2;
            cpu.Regs.PC = RomTapeReturnAddress;
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

        private const byte ProgramType = 0;
        private const byte NumberArrayType = 1;
        private const byte CharacterArrayType = 2;
        private const byte CodeType = 3;

        private const ushort BasicProgramStart = 23755;
        private const ushort MainExecutionLoopAddress = 0x1555;
        private const ushort DefaultStackPointer = 0xFF58;

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

        public static TapLoadResult Load(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            IReadOnlyList<TapBlock> blocks = ParseBlocks(fileData);
            if (blocks.Count == 0)
                throw new InvalidOperationException("The .tap file does not contain any blocks.");

            InitializeMachineForFake48kTapeLoad(machine);

            TapHeaderInfo? pendingHeader = null;
            int loadedBlockCount = 0;
            string? autoStartFileName = null;

            foreach (TapBlock block in blocks)
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

                if (block.Payload.Length != pendingHeader.DataLength)
                {
                    throw new InvalidOperationException(
                        $"Tape data block length mismatch for '{pendingHeader.FileName}'. Expected {pendingHeader.DataLength} bytes, got {block.Payload.Length}.");
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
            IReadOnlyList<TapBlock> blocks = ParseBlocks(fileData);
            if (blocks.Count == 0)
                throw new InvalidOperationException("The .tap file does not contain any blocks.");

            var tape = new MountedTape(Path.GetFileName(path), blocks);
            machine.MountTape(tape);
            LogMountedTape(tape, blocks);
            return new TapMountResult(blocks.Count, Path.GetFileName(path));
        }

        public static TapBootstrapResult BootstrapBasicProgramAndMountRemaining(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Tape path must be provided.", nameof(path));

            byte[] fileData = File.ReadAllBytes(path);
            IReadOnlyList<TapBlock> blocks = ParseBlocks(fileData);
            if (blocks.Count < 2)
                throw new InvalidOperationException("The .tap file does not contain enough blocks to bootstrap a BASIC loader.");

            InitializeMachineForFake48kTapeLoad(machine);
            int consumedBlockCount = 0;
            string? autoStartFileName = null;

            while (consumedBlockCount + 1 < blocks.Count)
            {
                TapBlock headerBlock = blocks[consumedBlockCount];
                TapBlock dataBlock = blocks[consumedBlockCount + 1];

                if (!IsStandardHeaderBlock(headerBlock) || dataBlock.Flag != DataFlag)
                {
                    if (consumedBlockCount == 0)
                        throw new InvalidOperationException("The .tap file does not begin with a standard BASIC header/data pair.");

                    break;
                }

                TapHeaderInfo header = ParseHeaderInfo(headerBlock);
                if (consumedBlockCount == 0 && header.Type != ProgramType)
                    throw new InvalidOperationException($"The leading tape header must be BASIC, but was type {header.Type}.");

                if (header.Type == ProgramType)
                {
                    ushort effectiveProgramLength = (ushort)Math.Min(header.ProgramLength, dataBlock.Payload.Length);
                    var effectiveHeader = new TapHeaderInfo(
                        header.Type,
                        header.FileName,
                        (ushort)dataBlock.Payload.Length,
                        header.AutoStartLine,
                        effectiveProgramLength);

                    LoadBasicProgram(machine, effectiveHeader, dataBlock.Payload);
                    if (effectiveHeader.AutoStartLine < 32768)
                        autoStartFileName = effectiveHeader.FileName;
                }
                else if (header.Type == CodeType)
                {
                    LoadBytes(machine, header.StartAddress, dataBlock.Payload);
                }
                else
                {
                    break;
                }

                consumedBlockCount += 2;
            }

            if (consumedBlockCount == 0)
                throw new InvalidOperationException("The .tap file did not contain any supported leading bootstrap blocks.");

            var tape = new MountedTape(Path.GetFileName(path), blocks, initialBlockIndex: consumedBlockCount);
            machine.MountTape(tape);
            LogMountedTape(tape, blocks);
            return new TapBootstrapResult(blocks.Count, consumedBlockCount, Path.GetFileName(path), autoStartFileName);
        }

        private static void LogMountedTape(MountedTape tape, IReadOnlyList<TapBlock> blocks)
        {
            Console.WriteLine($"[TAP] Mounted '{tape.DisplayName}' with {blocks.Count} blocks.");
            for (int i = 0; i < blocks.Count; i++)
            {
                TapBlock block = blocks[i];
                if (block.Flag == HeaderFlag)
                {
                    TapHeaderInfo header = ParseHeaderInfo(block);
                    Console.WriteLine($"[TAP] Block {i}: HEADER {GetHeaderTypeName(header.Type)} '{header.FileName}' len={header.DataLength}");
                }
                else
                {
                    Console.WriteLine($"[TAP] Block {i}: DATA flag=0x{block.Flag:X2} len={block.Payload.Length}");
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

        private static IReadOnlyList<TapBlock> ParseBlocks(byte[] fileData)
        {
            var blocks = new List<TapBlock>();
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

                byte flag = fileData[offset];
                int payloadLength = blockLength - 2;
                byte[] payload = new byte[payloadLength];
                Buffer.BlockCopy(fileData, offset + 1, payload, 0, payloadLength);
                byte checksum = fileData[offset + blockLength - 1];

                ValidateChecksum(fileData, offset, blockLength);

                blocks.Add(new TapBlock(flag, payload, checksum));
                offset += blockLength;
            }

            return blocks;
        }

        private static void InitializeMachineForFake48kTapeLoad(Spectrum128Machine machine)
        {
            machine.Reset();
            machine.ConfigureFor48kTapeLoad(borderColor: 0);

            machine.Cpu.Regs.PC = MainExecutionLoopAddress;
            machine.Cpu.Regs.SP = DefaultStackPointer;
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
                    throw new InvalidOperationException($"Unsupported tape header type {header.Type}.");
            }
        }

        private static void LoadBasicProgram(Spectrum128Machine machine, TapHeaderInfo header, byte[] payload)
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

        private static void LoadBytes(Spectrum128Machine machine, ushort startAddress, byte[] payload)
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

        internal static TapHeaderInfo ParseHeaderInfo(TapBlock block)
        {
            if (block.Payload.Length != TapHeaderPayloadLength)
            {
                throw new InvalidOperationException(
                    $"Tape header blocks must contain exactly {TapHeaderPayloadLength} payload bytes, but got {block.Payload.Length}.");
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

        private static bool IsStandardHeaderBlock(TapBlock block)
        {
            return block.Flag == HeaderFlag && block.Payload.Length == TapHeaderPayloadLength;
        }

        private static void WriteWord(Spectrum128Machine machine, ushort address, ushort value)
        {
            machine.PokeMemory(address, (byte)(value & 0xFF));
            machine.PokeMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        public sealed class TapBlock
        {
            public TapBlock(byte flag, byte[] payload, byte checksum)
            {
                Flag = flag;
                Payload = payload;
                Checksum = checksum;
            }

            public byte Flag { get; }
            public byte[] Payload { get; }
            public byte Checksum { get; }
            public int StreamByteCount => Payload.Length + 2;

            public byte GetStreamByte(int index)
            {
                if (index <= 0)
                    return Flag;
                if (index <= Payload.Length)
                    return Payload[index - 1];
                return Checksum;
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
