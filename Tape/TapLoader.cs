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

    public sealed class MountedTape
    {
        private const ushort RomTapeReturnAddress = 0x053F;
        private const ushort RomLoadBytesTrapAddress = 0x056B;
        private const byte FlagCarry = 0x01;
        private readonly IReadOnlyList<TapLoader.TapBlock> blocks;
        private int nextBlockIndex;
        private int earBlockIndex;
        private int earByteIndex;
        private int earBitIndex;
        private int earSubPhase;

        public MountedTape(string displayName, IReadOnlyList<TapLoader.TapBlock> blocks)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "unnamed.tap" : displayName;
            this.blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        }

        public string DisplayName { get; }
        public bool HasRemainingBlocks => nextBlockIndex < blocks.Count;

        public bool ReadEarBit()
        {
            if (blocks.Count == 0)
                return true;

            TapLoader.TapBlock block = blocks[earBlockIndex % blocks.Count];
            byte streamByte = block.GetStreamByte(earByteIndex);
            bool bit = ((streamByte >> (7 - earBitIndex)) & 0x01) != 0;

            earSubPhase++;
            if (earSubPhase >= 4)
            {
                earSubPhase = 0;
                earBitIndex++;
                if (earBitIndex >= 8)
                {
                    earBitIndex = 0;
                    earByteIndex++;
                    if (earByteIndex >= block.StreamByteCount)
                    {
                        earByteIndex = 0;
                        earBlockIndex = (earBlockIndex + 1) % blocks.Count;
                    }
                }
            }

            return bit;
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
                byte expectedFlag = cpu.Regs.A_;
                ushort expectedLength = cpu.Regs.DE;
                ushort destination = cpu.Regs.IX;
                bool isLoad = (cpu.Regs.F_ & FlagCarry) != 0;

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

                nextBlockIndex++;
            }

            CompleteTrap(cpu, success);
            return true;
        }

        private static void CompleteTrap(Z80Cpu cpu, bool success)
        {
            cpu.Regs.SP += 2;
            cpu.Regs.PC = RomTapeReturnAddress;
            cpu.Regs.IX += cpu.Regs.DE;
            cpu.Regs.DE = 0;
            cpu.Regs.F = success ? FlagCarry : (byte)0;
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

            TapHeader? pendingHeader = null;
            int loadedBlockCount = 0;
            string? autoStartFileName = null;

            foreach (TapBlock block in blocks)
            {
                if (block.Flag == HeaderFlag)
                {
                    pendingHeader = ParseHeader(block);
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

            machine.MountTape(new MountedTape(Path.GetFileName(path), blocks));
            return new TapMountResult(blocks.Count, Path.GetFileName(path));
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
                    throw new InvalidOperationException($"Invalid tape block length {blockLength}. Each block must contain at least a flag byte and checksum byte.");

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
            machine.ConfigureFor48kSnapshot(borderColor: 0);

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

        private static void LoadDataBlock(Spectrum128Machine machine, TapHeader header, byte[] payload)
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

        private static void LoadBasicProgram(Spectrum128Machine machine, TapHeader header, byte[] payload)
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

        private static TapHeader ParseHeader(TapBlock block)
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

            return new TapHeader(type, fileName, dataLength, parameter1, parameter2);
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

        private sealed class TapHeader
        {
            public TapHeader(byte type, string fileName, ushort dataLength, ushort parameter1, ushort parameter2)
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
