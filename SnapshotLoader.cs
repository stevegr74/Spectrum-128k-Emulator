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

        public static void LoadSna48k(Spectrum128Machine machine, string path)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Snapshot path must be provided.", nameof(path));

            byte[] data = File.ReadAllBytes(path);
            if (data.Length != Sna48FileSize)
                throw new InvalidOperationException(
                    $"Only 48K .sna snapshots are supported right now. Expected {Sna48FileSize} bytes, got {data.Length}.");

            machine.Reset();
            machine.ConfigureFor48kSnapshot(borderColor: data[26] & 0x07);

            Z80Registers regs = machine.Cpu.Regs;

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

            bool iff2 = data[19] != 0;
            regs.R = data[20];

            regs.F = data[21];
            regs.A = data[22];
            regs.SP = ReadWord(data, 23);

            int interruptMode = data[25] & 0x03;

            byte[] ram48 = new byte[Sna48RamSize];
            Buffer.BlockCopy(data, Sna48HeaderSize, ram48, 0, Sna48RamSize);
            machine.Load48kSnapshotRam(ram48);

            // In 48K .sna, PC is stored on the stack.
            ushort pc = (ushort)(
                machine.PeekMemory(regs.SP) |
                (machine.PeekMemory((ushort)(regs.SP + 1)) << 8));

            regs.PC = pc;
            regs.SP += 2;

            machine.Cpu.RestoreInterruptState(
                iff1: iff2,
                iff2: iff2,
                interruptMode: interruptMode);

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
