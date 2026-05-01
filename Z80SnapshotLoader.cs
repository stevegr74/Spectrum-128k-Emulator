using System;
using System.Collections.Generic;
using System.IO;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public static class Z80SnapshotLoader
    {
        private const int HeaderSizeV1 = 30;
        private const int Ram48Size = 48 * 1024;
        private const int RamBankSize = 16 * 1024;
        private const byte CompressionFlag = 0x20;

        public static void Load(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Snapshot path must be provided.", nameof(path));

            byte[] data = File.ReadAllBytes(path);
            if (data.Length < HeaderSizeV1)
                throw new InvalidOperationException("File is too small to be a valid .z80 snapshot.");

            ushort programCounter = ReadWord(data, 6);
            if (programCounter != 0)
            {
                LoadV1(machine, data, programCounter);
                return;
            }

            LoadV2OrV3(machine, data);
        }

        public static void LoadZ80v1(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Snapshot path must be provided.", nameof(path));

            byte[] data = File.ReadAllBytes(path);
            if (data.Length < HeaderSizeV1)
                throw new InvalidOperationException("File is too small to be a valid .z80 snapshot.");

            ushort programCounter = ReadWord(data, 6);
            if (programCounter == 0)
                throw new InvalidOperationException("Expected a v1 .z80 snapshot, but found an extended v2/v3 snapshot.");

            LoadV1(machine, data, programCounter);
        }

        private static void LoadV1(Spectrum128Machine machine, byte[] data, ushort programCounter)
        {
            machine.Reset();
            machine.ConfigureFor48kSnapshot(borderColor: (NormalizeFlagsByte(data[12]) >> 1) & 0x07);

            RestoreCommonRegisters(machine.Cpu.Regs, data, programCounter);

            bool iff1 = data[27] != 0;
            bool iff2 = data[28] != 0;
            int interruptMode = data[29] & 0x03;

            byte[] ram48 = (NormalizeFlagsByte(data[12]) & CompressionFlag) != 0
                ? DecompressV1Ram48(data, HeaderSizeV1)
                : ReadUncompressedRam48(data, HeaderSizeV1);

            machine.Load48kSnapshotRam(ram48);
            FinalizeLoad(machine, iff1, iff2, interruptMode);
        }

        private static void LoadV2OrV3(Spectrum128Machine machine, byte[] data)
        {
            if (data.Length < HeaderSizeV1 + 2)
                throw new InvalidOperationException("Extended .z80 snapshot is truncated before the additional header length.");

            int additionalHeaderLength = ReadWord(data, HeaderSizeV1);
            int additionalHeaderOffset = HeaderSizeV1 + 2;
            int blockOffset = additionalHeaderOffset + additionalHeaderLength;

            if (data.Length < blockOffset)
                throw new InvalidOperationException("Extended .z80 snapshot is truncated inside the additional header.");
            if (additionalHeaderLength < 3)
                throw new InvalidOperationException("Extended .z80 snapshot additional header is too short.");

            ushort programCounter = ReadWord(data, additionalHeaderOffset);
            byte hardwareMode = data[additionalHeaderOffset + 2];
            byte last7ffd = additionalHeaderLength >= 4 ? data[additionalHeaderOffset + 3] : (byte)0;
            byte flags = NormalizeFlagsByte(data[12]);

            machine.Reset();
            RestoreCommonRegisters(machine.Cpu.Regs, data, programCounter);

            bool iff1 = data[27] != 0;
            bool iff2 = data[28] != 0;
            int interruptMode = data[29] & 0x03;
            int borderColor = (flags >> 1) & 0x07;

            if (Is48kHardware(additionalHeaderLength, hardwareMode))
            {
                Load48kPageBlocks(machine, data, blockOffset);
                machine.ConfigureFor48kSnapshot(borderColor);
            }
            else if (Is128kHardware(additionalHeaderLength, hardwareMode))
            {
                Load128kPageBlocks(machine, data, blockOffset);
                machine.ConfigureFor128kSnapshot(last7ffd, borderColor);
            }
            else
            {
                throw new InvalidOperationException(
                    $"This .z80 hardware mode is not supported yet (headerLength={additionalHeaderLength}, hardwareMode={hardwareMode}).");
            }

            FinalizeLoad(machine, iff1, iff2, interruptMode);

            if (Is48kHardware(additionalHeaderLength, hardwareMode))
                machine.SetInitialInterruptDelay(Spectrum128Machine.Default48kSnapshotInitialInterruptDelay);
        }

        private static void RestoreCommonRegisters(Z80Registers regs, byte[] data, ushort programCounter)
        {
            regs.A = data[0];
            regs.F = data[1];
            regs.C = data[2];
            regs.B = data[3];
            regs.L = data[4];
            regs.H = data[5];
            regs.PC = programCounter;
            regs.SP = ReadWord(data, 8);
            regs.I = data[10];
            regs.R = (byte)((data[11] & 0x7F) | ((NormalizeFlagsByte(data[12]) & 0x01) << 7));
            regs.E = data[13];
            regs.D = data[14];
            regs.C_ = data[15];
            regs.B_ = data[16];
            regs.E_ = data[17];
            regs.D_ = data[18];
            regs.L_ = data[19];
            regs.H_ = data[20];
            regs.A_ = data[21];
            regs.F_ = data[22];
            regs.IY = ReadWord(data, 23);
            regs.IX = ReadWord(data, 25);
        }

        private static void Load48kPageBlocks(Spectrum128Machine machine, byte[] data, int offset)
        {
            byte[]? page4 = null;
            byte[]? page5 = null;
            byte[]? page8 = null;

            foreach (MemoryBlock block in EnumerateMemoryBlocks(data, offset))
            {
                switch (block.PageNumber)
                {
                    case 4:
                        page4 = block.Data;
                        break;
                    case 5:
                        page5 = block.Data;
                        break;
                    case 8:
                        page8 = block.Data;
                        break;
                }
            }

            if (page4 == null || page5 == null || page8 == null)
                throw new InvalidOperationException("48K .z80 snapshot is missing one or more required RAM pages (4, 5, 8).");

            machine.Load48kSnapshotRam(Join48kPages(page8, page4, page5));
        }

        private static void Load128kPageBlocks(Spectrum128Machine machine, byte[] data, int offset)
        {
            bool[] loadedBanks = new bool[8];

            foreach (MemoryBlock block in EnumerateMemoryBlocks(data, offset))
            {
                int bank = Map128kPageToRamBank(block.PageNumber);
                if (bank < 0)
                    continue;

                machine.LoadRamBank(bank, block.Data);
                loadedBanks[bank] = true;
            }

            for (int bank = 0; bank < loadedBanks.Length; bank++)
            {
                if (!loadedBanks[bank])
                    throw new InvalidOperationException($"128K .z80 snapshot is missing RAM bank {bank}.");
            }
        }

        private static IEnumerable<MemoryBlock> EnumerateMemoryBlocks(byte[] data, int offset)
        {
            int source = offset;

            while (source < data.Length)
            {
                if (source + 3 > data.Length)
                    throw new InvalidOperationException("Truncated .z80 memory block header.");

                int blockLength = ReadWord(data, source);
                int pageNumber = data[source + 2];
                source += 3;

                byte[] blockData;
                if (blockLength == 0xFFFF)
                {
                    if (source + RamBankSize > data.Length)
                        throw new InvalidOperationException("Truncated uncompressed .z80 memory block.");

                    blockData = new byte[RamBankSize];
                    Buffer.BlockCopy(data, source, blockData, 0, RamBankSize);
                    source += RamBankSize;
                }
                else
                {
                    if (source + blockLength > data.Length)
                        throw new InvalidOperationException("Truncated compressed .z80 memory block.");

                    blockData = DecompressBlock(data, source, blockLength);
                    source += blockLength;
                }

                yield return new MemoryBlock(pageNumber, blockData);
            }
        }

        private static byte[] Join48kPages(byte[] page8, byte[] page4, byte[] page5)
        {
            byte[] ram48 = new byte[Ram48Size];
            Buffer.BlockCopy(page8, 0, ram48, 0, RamBankSize);
            Buffer.BlockCopy(page4, 0, ram48, RamBankSize, RamBankSize);
            Buffer.BlockCopy(page5, 0, ram48, RamBankSize * 2, RamBankSize);
            return ram48;
        }

        private static int Map128kPageToRamBank(int pageNumber)
        {
            return pageNumber switch
            {
                3 => 0,
                4 => 1,
                5 => 2,
                6 => 3,
                7 => 4,
                8 => 5,
                9 => 6,
                10 => 7,
                _ => -1
            };
        }

        private static bool Is48kHardware(int additionalHeaderLength, byte hardwareMode)
        {
            if (additionalHeaderLength == 23)
                return hardwareMode == 0 || hardwareMode == 1;
            if (additionalHeaderLength == 54 || additionalHeaderLength == 55)
                return hardwareMode == 0 || hardwareMode == 1 || hardwareMode == 3;
            return false;
        }

        private static bool Is128kHardware(int additionalHeaderLength, byte hardwareMode)
        {
            if (additionalHeaderLength == 23)
                return hardwareMode == 3 || hardwareMode == 4;
            if (additionalHeaderLength == 54 || additionalHeaderLength == 55)
                return hardwareMode == 4 || hardwareMode == 5 || hardwareMode == 12;
            return false;
        }

        private static void FinalizeLoad(Spectrum128Machine machine, bool iff1, bool iff2, int interruptMode)
        {
            machine.Cpu.RestoreInterruptState(iff1, iff2, interruptMode);
            machine.Cpu.ClearSnapshotExecutionState();
            machine.ClearLogs();
            machine.ClearKeyboard();
        }

        private static byte[] ReadUncompressedRam48(byte[] data, int offset)
        {
            int remaining = data.Length - offset;
            if (remaining != Ram48Size)
                throw new InvalidOperationException(
                    $"Uncompressed .z80 v1 snapshots must contain exactly {Ram48Size} bytes of RAM data. Got {remaining}.");

            byte[] ram48 = new byte[Ram48Size];
            Buffer.BlockCopy(data, offset, ram48, 0, Ram48Size);
            return ram48;
        }

        private static byte[] DecompressV1Ram48(byte[] data, int offset)
        {
            byte[] ram48 = new byte[Ram48Size];
            int source = offset;
            int target = 0;

            while (source < data.Length && target < ram48.Length)
            {
                if (source + 3 < data.Length &&
                    source + 4 == data.Length &&
                    data[source] == 0x00 &&
                    data[source + 1] == 0xED &&
                    data[source + 2] == 0xED &&
                    data[source + 3] == 0x00)
                {
                    break;
                }

                if (source + 3 < data.Length &&
                    data[source] == 0xED &&
                    data[source + 1] == 0xED)
                {
                    int count = data[source + 2];
                    byte value = data[source + 3];

                    if (count == 0)
                        throw new InvalidOperationException("Invalid .z80 v1 compressed run length of zero.");

                    if (target + count > ram48.Length)
                        throw new InvalidOperationException("Compressed .z80 RAM expands beyond 48K.");

                    for (int i = 0; i < count; i++)
                        ram48[target++] = value;

                    source += 4;
                    continue;
                }

                ram48[target++] = data[source++];
            }

            if (target != ram48.Length)
                throw new InvalidOperationException(
                    $"Compressed .z80 RAM did not expand to exactly {Ram48Size} bytes. Expanded to {target}.");

            return ram48;
        }

        private static byte[] DecompressBlock(byte[] data, int offset, int length)
        {
            byte[] block = new byte[RamBankSize];
            int source = offset;
            int end = offset + length;
            int target = 0;

            while (source < end)
            {
                if (source + 3 < end &&
                    data[source] == 0xED &&
                    data[source + 1] == 0xED)
                {
                    int count = data[source + 2];
                    byte value = data[source + 3];

                    if (count == 0)
                        throw new InvalidOperationException("Invalid .z80 compressed block run length of zero.");

                    if (target + count > block.Length)
                        throw new InvalidOperationException("Compressed .z80 memory block expands beyond 16K.");

                    for (int i = 0; i < count; i++)
                        block[target++] = value;

                    source += 4;
                    continue;
                }

                if (target >= block.Length)
                    throw new InvalidOperationException("Compressed .z80 memory block expands beyond 16K.");

                block[target++] = data[source++];
            }

            if (target != block.Length)
                throw new InvalidOperationException(
                    $"Compressed .z80 memory block did not expand to exactly {RamBankSize} bytes. Expanded to {target}.");

            return block;
        }

        private static byte NormalizeFlagsByte(byte value) => value == 0xFF ? (byte)0x01 : value;

        private static ushort ReadWord(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private readonly record struct MemoryBlock(int PageNumber, byte[] Data);
    }
}
