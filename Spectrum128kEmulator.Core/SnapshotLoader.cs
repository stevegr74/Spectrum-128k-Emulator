using System;
using System.IO;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public static class SnapshotLoader
    {
        private const int Sna48HeaderSize = 27;
        private const int Sna48RamSize = 48 * 1024;
        private const int Sna48FileSize = Sna48HeaderSize + Sna48RamSize;
        private const int RamBankSize = 16 * 1024;
        private const int Sna128ExtensionOffset = Sna48FileSize;
        private const int Sna128RamPagesOffset = Sna128ExtensionOffset + 4;
        private const int Sna128StandardFileSize = Sna128RamPagesOffset + (5 * RamBankSize);
        private const int Sna128DuplicatePageFileSize = Sna128RamPagesOffset + (6 * RamBankSize);

        public static void Load(Spectrum128Machine machine, string path)
        {
            Load(machine, path, SpectrumMachineModel.Spectrum48K);
        }

        public static void Load(Spectrum128Machine machine, string path, SpectrumMachineModel hardwareModel)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Snapshot path must be provided.", nameof(path));

            Load(machine, File.ReadAllBytes(path), hardwareModel);
        }

        public static void Load(Spectrum128Machine machine, byte[] data)
        {
            Load(machine, data, SpectrumMachineModel.Spectrum48K);
        }

        public static void Load(Spectrum128Machine machine, byte[] data, SpectrumMachineModel hardwareModel)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            switch (data.Length)
            {
                case Sna48FileSize:
                    LoadSna48k(machine, data, hardwareModel);
                    return;
                case Sna128StandardFileSize:
                case Sna128DuplicatePageFileSize:
                    LoadSna128k(machine, data);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported .sna file size {data.Length}. Expected {Sna48FileSize} bytes for 48K, " +
                        $"or {Sna128StandardFileSize}/{Sna128DuplicatePageFileSize} bytes for 128K.");
            }
        }

        public static void LoadSna48k(Spectrum128Machine machine, string path)
        {
            LoadSna48k(machine, path, SpectrumMachineModel.Spectrum48K);
        }

        public static void LoadSna48k(Spectrum128Machine machine, string path, SpectrumMachineModel hardwareModel)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Snapshot path must be provided.", nameof(path));

            LoadSna48k(machine, File.ReadAllBytes(path), hardwareModel);
        }

        public static void LoadSna48k(Spectrum128Machine machine, byte[] data)
        {
            LoadSna48k(machine, data, SpectrumMachineModel.Spectrum48K);
        }

        public static void LoadSna48k(Spectrum128Machine machine, byte[] data, SpectrumMachineModel hardwareModel)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.Length != Sna48FileSize)
                throw new InvalidOperationException(
                    $"LoadSna48k requires a {Sna48FileSize}-byte 48K .sna snapshot, got {data.Length} bytes.");

            machine.Reset(hardwareModel);
            machine.ConfigureFor48kSnapshot(borderColor: data[26] & 0x07, hardwareModel);

            Z80Registers regs = machine.Cpu.Regs;
            RestoreHeaderRegisters(regs, data);

            byte[] ram48 = new byte[Sna48RamSize];
            Buffer.BlockCopy(data, Sna48HeaderSize, ram48, 0, Sna48RamSize);
            machine.Load48kSnapshotRam(ram48);

            // In 48K .sna, PC is stored on the stack.
            ushort pc = (ushort)(
                machine.PeekMemory(regs.SP) |
                (machine.PeekMemory((ushort)(regs.SP + 1)) << 8));

            regs.PC = pc;
            regs.SP += 2;

            FinalizeLoad(machine, data);
            machine.SetSnapshotResumeFramePhase(Spectrum128Machine.Default48kSnapshotResumeFramePhase);
        }

        public static void LoadSna128k(Spectrum128Machine machine, byte[] data)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length != Sna128StandardFileSize && data.Length != Sna128DuplicatePageFileSize)
                throw new InvalidOperationException(
                    $"Expected a 128K .sna file of {Sna128StandardFileSize} or {Sna128DuplicatePageFileSize} bytes, got {data.Length}.");

            byte last7ffdValue = data[Sna128ExtensionOffset + 2];
            int pagedBank = last7ffdValue & 0x07;
            int uniqueInitialBanks = pagedBank == 2 || pagedBank == 5 ? 2 : 3;
            int expectedLength = Sna128RamPagesOffset + ((8 - uniqueInitialBanks) * RamBankSize);
            if (data.Length != expectedLength)
                throw new InvalidOperationException(
                    $"128K .sna paging metadata requires a {expectedLength}-byte layout, but the file contains {data.Length} bytes.");
            if (data[Sna128ExtensionOffset + 3] != 0)
                throw new NotSupportedException("128K .sna snapshots with the TR-DOS ROM paged are not supported.");

            machine.Reset(SpectrumMachineModel.Spectrum128K);
            RestoreHeaderRegisters(machine.Cpu.Regs, data);
            machine.Cpu.Regs.PC = ReadWord(data, Sna128ExtensionOffset);

            bool[] loadedBanks = new bool[8];
            LoadBank(machine, data, Sna48HeaderSize, 5, loadedBanks);
            LoadBank(machine, data, Sna48HeaderSize + RamBankSize, 2, loadedBanks);
            LoadBank(machine, data, Sna48HeaderSize + (2 * RamBankSize), pagedBank, loadedBanks);

            int sourceOffset = Sna128RamPagesOffset;
            for (int bank = 0; bank < loadedBanks.Length; bank++)
            {
                if (loadedBanks[bank])
                    continue;

                LoadBank(machine, data, sourceOffset, bank, loadedBanks);
                sourceOffset += RamBankSize;
            }

            machine.ConfigureFor128kSnapshot(last7ffdValue, borderColor: data[26] & 0x07);
            FinalizeLoad(machine, data);
        }

        private static void RestoreHeaderRegisters(Z80Registers regs, byte[] data)
        {
            regs.I = data[0];
            regs.L_ = data[1];
            regs.H_ = data[2];
            regs.E_ = data[3];
            regs.D_ = data[4];
            regs.C_ = data[5];
            regs.B_ = data[6];
            regs.F_ = data[7];
            regs.A_ = data[8];
            regs.L = data[9];
            regs.H = data[10];
            regs.E = data[11];
            regs.D = data[12];
            regs.C = data[13];
            regs.B = data[14];
            regs.IY = ReadWord(data, 15);
            regs.IX = ReadWord(data, 17);
            regs.R = data[20];
            regs.F = data[21];
            regs.A = data[22];
            regs.SP = ReadWord(data, 23);
        }

        private static void LoadBank(
            Spectrum128Machine machine,
            byte[] data,
            int sourceOffset,
            int bank,
            bool[] loadedBanks)
        {
            byte[] page = new byte[RamBankSize];
            Buffer.BlockCopy(data, sourceOffset, page, 0, RamBankSize);
            machine.LoadRamBank(bank, page);
            loadedBanks[bank] = true;
        }

        private static void FinalizeLoad(Spectrum128Machine machine, byte[] data)
        {
            bool iff2 = (data[19] & 0x04) != 0;
            machine.Cpu.RestoreInterruptState(
                iff1: iff2,
                iff2: iff2,
                interruptMode: data[25] & 0x03);
            machine.Cpu.ClearSnapshotExecutionState();
            machine.ClearLogs();
            machine.ClearKeyboard();
        }

        private static ushort ReadWord(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }
    }
}
