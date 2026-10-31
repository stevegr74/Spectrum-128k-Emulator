using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public static class SzxSnapshotLoader
    {
        private const int FileHeaderSize = 8;
        private const int BlockHeaderSize = 8;
        private const int RamBankSize = 16 * 1024;
        private const byte SupportedMajorVersion = 1;
        private const byte Machine48K = 1;
        private const byte Machine128K = 2;
        private const byte AlternateTimingsFlag = 0x01;
        private const ushort CompressedRamFlag = 0x0001;

        private static readonly HashSet<string> UnsupportedHardwareBlocks = new(StringComparer.Ordinal)
        {
            "ROM\0", "MFCE", "USPE", "DRUM", "TAPE", "IF2R", "AMXM",
            "ZXPR", "IF1\0", "MDRV", "+3\0\0", "DSK\0", "LEC\0", "LCRP",
            "SCLD", "B128", "BDSK", "GS\0\0", "GSRP", "COVX", "DOCK",
            "ZXAT", "ATRP", "ZXCF", "CFRP", "PLSD", "PDSK", "OPUS",
            "ODSK", "SIDE", "DIDE", "DIRP", "DMMC", "DMRP", "SNET",
            "SNEF", "SNER", "PLTT", "ZMMC"
        };

        public static void Load(Spectrum128Machine machine, string path)
        {
            ArgumentNullException.ThrowIfNull(machine);
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Snapshot path must be provided.", nameof(path));

            Load(machine, File.ReadAllBytes(path));
        }

        public static void Load(Spectrum128Machine machine, byte[] data)
        {
            ArgumentNullException.ThrowIfNull(machine);
            ArgumentNullException.ThrowIfNull(data);

            ParsedSnapshot snapshot = Parse(data);
            Apply(machine, snapshot);
        }

        private static ParsedSnapshot Parse(byte[] data)
        {
            if (data.Length < FileHeaderSize)
                throw new InvalidOperationException("File is too small to be a valid .szx snapshot.");
            if (Encoding.ASCII.GetString(data, 0, 4) != "ZXST")
                throw new InvalidOperationException("Invalid .szx file signature.");
            if (data[4] != SupportedMajorVersion)
                throw new NotSupportedException($"SZX major version {data[4]} is not supported.");
            if ((data[7] & AlternateTimingsFlag) != 0)
                throw new NotSupportedException("SZX snapshots using alternate machine timings are not supported.");
            if ((data[7] & ~AlternateTimingsFlag) != 0)
                throw new NotSupportedException($"SZX header flags 0x{data[7]:X2} are not supported.");

            SpectrumMachineModel model = data[6] switch
            {
                Machine48K => SpectrumMachineModel.Spectrum48K,
                Machine128K => SpectrumMachineModel.Spectrum128K,
                _ => throw new NotSupportedException($"SZX machine id {data[6]} is not supported; only 48K and original 128K snapshots are accepted.")
            };

            CpuState? cpu = null;
            SpectrumState? spectrum = null;
            AyState? ay = null;
            var ramBanks = new Dictionary<int, byte[]>();
            int offset = FileHeaderSize;

            while (offset < data.Length)
            {
                if (data.Length - offset < BlockHeaderSize)
                    throw new InvalidOperationException("Truncated .szx block header.");

                string id = Encoding.Latin1.GetString(data, offset, 4);
                uint size = ReadDword(data, offset + 4);
                offset += BlockHeaderSize;
                if (size > int.MaxValue || size > data.Length - offset)
                    throw new InvalidOperationException($"SZX block '{DisplayId(id)}' is truncated.");

                int length = (int)size;
                if (UnsupportedHardwareBlocks.Contains(id))
                    throw new NotSupportedException($"SZX block '{DisplayId(id)}' requires unsupported hardware state.");

                switch (id)
                {
                    case "Z80R":
                        if (cpu != null)
                            throw new InvalidOperationException("SZX snapshot contains more than one Z80R block.");
                        cpu = ParseCpu(data, offset, length);
                        break;

                    case "SPCR":
                        if (spectrum != null)
                            throw new InvalidOperationException("SZX snapshot contains more than one SPCR block.");
                        spectrum = ParseSpectrum(data, offset, length, model);
                        break;

                    case "RAMP":
                        (int bank, byte[] page) = ParseRamPage(data, offset, length);
                        if (!ramBanks.TryAdd(bank, page))
                            throw new InvalidOperationException($"SZX snapshot contains duplicate RAM page {bank}.");
                        break;

                    case "AY\0\0":
                        if (ay != null)
                            throw new InvalidOperationException("SZX snapshot contains more than one AY block.");
                        ay = ParseAy(data, offset, length, model);
                        break;

                    case "KEYB":
                        ValidateKeyboard(data, offset, length, model);
                        break;

                    case "JOY\0":
                        ValidateJoystick(data, offset, length);
                        break;
                }

                offset += length;
            }

            if (cpu == null)
                throw new InvalidOperationException("SZX snapshot is missing the required Z80R block.");
            if (spectrum == null)
                throw new InvalidOperationException("SZX snapshot is missing the required SPCR block.");

            int[] requiredBanks = model == SpectrumMachineModel.Spectrum48K
                ? new[] { 5, 2, 0 }
                : new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            foreach (int bank in requiredBanks)
            {
                if (!ramBanks.ContainsKey(bank))
                    throw new InvalidOperationException($"SZX snapshot is missing required RAM page {bank}.");
            }

            if (model == SpectrumMachineModel.Spectrum48K)
            {
                foreach (int bank in ramBanks.Keys)
                {
                    if (bank != 5 && bank != 2 && bank != 0)
                        throw new NotSupportedException($"48K SZX snapshot contains unsupported RAM page {bank}.");
                }
            }

            if (model == SpectrumMachineModel.Spectrum128K && ay == null)
                throw new InvalidOperationException("128K SZX snapshot is missing the required AY block.");

            int frameTStates = model == SpectrumMachineModel.Spectrum48K
                ? Spectrum128Machine.FrameTStates48
                : Spectrum128Machine.FrameTStates128;
            if (cpu.FrameCycles >= frameTStates)
                throw new InvalidOperationException($"SZX CPU frame position {cpu.FrameCycles} is outside the {frameTStates}-T-state frame.");

            return new ParsedSnapshot(model, cpu, spectrum, ay, ramBanks);
        }

        private static CpuState ParseCpu(byte[] data, int offset, int length)
        {
            if (length < 37)
                throw new InvalidOperationException("SZX Z80R block is too short.");

            byte iff1 = data[offset + 26];
            byte iff2 = data[offset + 27];
            byte interruptMode = data[offset + 28];
            byte holdInterruptCycles = data[offset + 33];
            byte flags = data[offset + 34];
            if (iff1 > 1 || iff2 > 1)
                throw new InvalidOperationException("SZX Z80R block contains an invalid interrupt flip-flop value.");
            if (interruptMode > 2)
                throw new InvalidOperationException($"SZX Z80R block contains invalid interrupt mode {interruptMode}.");
            if (holdInterruptCycles > 48)
                throw new InvalidOperationException("SZX Z80R interrupt hold value exceeds 48 T-states.");
            if ((flags & ~0x07) != 0)
                throw new NotSupportedException($"SZX Z80R flags 0x{flags:X2} are not supported.");
            if ((flags & 0x03) == 0x03)
                throw new InvalidOperationException("SZX Z80R block cannot be both halted and interrupt-suppressed.");

            return new CpuState(
                ReadWord(data, offset), ReadWord(data, offset + 2), ReadWord(data, offset + 4), ReadWord(data, offset + 6),
                ReadWord(data, offset + 8), ReadWord(data, offset + 10), ReadWord(data, offset + 12), ReadWord(data, offset + 14),
                ReadWord(data, offset + 16), ReadWord(data, offset + 18), ReadWord(data, offset + 20), ReadWord(data, offset + 22),
                data[offset + 24], data[offset + 25], iff1 != 0, iff2 != 0, interruptMode,
                ReadDword(data, offset + 29), holdInterruptCycles, flags);
        }

        private static SpectrumState ParseSpectrum(byte[] data, int offset, int length, SpectrumMachineModel model)
        {
            if (length < 8)
                throw new InvalidOperationException("SZX SPCR block is too short.");
            if (data[offset] > 7)
                throw new InvalidOperationException($"SZX SPCR block contains invalid border colour {data[offset]}.");
            if (model == SpectrumMachineModel.Spectrum48K && data[offset + 1] != 0)
                throw new InvalidOperationException("48K SZX snapshot contains a non-zero 7FFD paging value.");
            if (data[offset + 2] != 0)
                throw new NotSupportedException("SZX snapshot requires +2A/+3 or clone paging through port 1FFD/EFF7.");
            for (int reservedOffset = 4; reservedOffset < 8; reservedOffset++)
            {
                if (data[offset + reservedOffset] != 0)
                    throw new InvalidOperationException("SZX SPCR block contains non-zero reserved data.");
            }

            return new SpectrumState(data[offset], data[offset + 1], data[offset + 3]);
        }

        private static (int Bank, byte[] Data) ParseRamPage(byte[] data, int offset, int length)
        {
            if (length < 3)
                throw new InvalidOperationException("SZX RAMP block is too short.");

            ushort flags = ReadWord(data, offset);
            if ((flags & ~CompressedRamFlag) != 0)
                throw new NotSupportedException($"SZX RAMP flags 0x{flags:X4} are not supported.");

            int bank = data[offset + 2];
            if ((uint)bank > 7)
                throw new NotSupportedException($"SZX RAM page {bank} is not supported.");

            int payloadOffset = offset + 3;
            int payloadLength = length - 3;
            byte[] page;
            if ((flags & CompressedRamFlag) == 0)
            {
                if (payloadLength != RamBankSize)
                    throw new InvalidOperationException($"Uncompressed SZX RAM page {bank} must be exactly {RamBankSize} bytes.");
                page = new byte[RamBankSize];
                Buffer.BlockCopy(data, payloadOffset, page, 0, RamBankSize);
            }
            else
            {
                page = DecompressRamPage(data, payloadOffset, payloadLength, bank);
            }

            return (bank, page);
        }

        private static AyState ParseAy(byte[] data, int offset, int length, SpectrumMachineModel model)
        {
            if (length < 18)
                throw new InvalidOperationException("SZX AY block is too short.");
            if (model == SpectrumMachineModel.Spectrum48K)
                throw new NotSupportedException("48K SZX snapshots requiring an external AY sound interface are not supported.");
            if (data[offset] != 0)
                throw new NotSupportedException($"SZX AY peripheral flags 0x{data[offset]:X2} are not supported.");
            if (data[offset + 1] > 15)
                throw new InvalidOperationException("SZX AY block selects an invalid register.");

            byte[] registers = new byte[16];
            Buffer.BlockCopy(data, offset + 2, registers, 0, registers.Length);
            return new AyState(data[offset + 1], registers);
        }

        private static void ValidateKeyboard(byte[] data, int offset, int length, SpectrumMachineModel model)
        {
            if (length < 4)
                throw new InvalidOperationException("SZX KEYB block is too short.");
            uint flags = ReadDword(data, offset);
            if ((flags & ~1U) != 0)
                throw new NotSupportedException($"SZX KEYB flags 0x{flags:X8} are not supported.");
            if ((flags & 1U) != 0 && model == SpectrumMachineModel.Spectrum48K)
                throw new NotSupportedException("SZX snapshot requires Issue 2 keyboard/ULA behavior, which is not supported.");
            if (length >= 5 && data[offset + 4] > 8)
                throw new InvalidOperationException("SZX KEYB block contains an invalid keyboard joystick type.");
        }

        private static void ValidateJoystick(byte[] data, int offset, int length)
        {
            if (length < 6)
                throw new InvalidOperationException("SZX JOY block is too short.");
            uint flags = ReadDword(data, offset);
            if (flags != 0)
                throw new NotSupportedException($"SZX JOY flags 0x{flags:X8} are not supported.");
            if (data[offset + 4] > 8 || data[offset + 5] > 8)
                throw new InvalidOperationException("SZX JOY block contains an invalid joystick type.");
        }

        private static byte[] DecompressRamPage(byte[] data, int offset, int length, int bank)
        {
            try
            {
                using var input = new MemoryStream(data, offset, length, writable: false);
                using var zlib = new ZLibStream(input, CompressionMode.Decompress);
                byte[] page = new byte[RamBankSize];
                int total = 0;
                while (total < page.Length)
                {
                    int read = zlib.Read(page, total, page.Length - total);
                    if (read == 0)
                        break;
                    total += read;
                }

                if (total != page.Length || zlib.ReadByte() != -1)
                    throw new InvalidOperationException($"Compressed SZX RAM page {bank} did not expand to exactly {RamBankSize} bytes.");
                return page;
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException($"Compressed SZX RAM page {bank} contains invalid Zlib data.", ex);
            }
        }

        private static void Apply(Spectrum128Machine machine, ParsedSnapshot snapshot)
        {
            machine.Reset(snapshot.Model);

            if (snapshot.Model == SpectrumMachineModel.Spectrum48K)
            {
                byte[] ram48 = new byte[48 * 1024];
                Buffer.BlockCopy(snapshot.RamBanks[5], 0, ram48, 0, RamBankSize);
                Buffer.BlockCopy(snapshot.RamBanks[2], 0, ram48, RamBankSize, RamBankSize);
                Buffer.BlockCopy(snapshot.RamBanks[0], 0, ram48, RamBankSize * 2, RamBankSize);
                machine.Load48kSnapshotRam(ram48);
                machine.ConfigureFor48kSnapshot(snapshot.Spectrum.Border, SpectrumMachineModel.Spectrum48K);
            }
            else
            {
                foreach ((int bank, byte[] page) in snapshot.RamBanks)
                    machine.LoadRamBank(bank, page);
                machine.ConfigureFor128kSnapshot(snapshot.Spectrum.Port7ffd, snapshot.Spectrum.Border);
            }

            RestoreCpu(machine.Cpu, snapshot.Cpu);
            machine.RestoreSnapshotUlaState(snapshot.Spectrum.PortFe);
            if (snapshot.Ay != null)
                machine.RestoreSnapshotAyState(snapshot.Ay.SelectedRegister, snapshot.Ay.Registers);

            machine.SetSnapshotResumeFramePhase((int)snapshot.Cpu.FrameCycles);
            machine.ClearLogs();
            machine.RestoreSnapshotInterruptPulse(snapshot.Cpu.HoldInterruptCycles);
            machine.ClearKeyboard();
        }

        private static void RestoreCpu(Z80Cpu cpu, CpuState state)
        {
            Z80Registers regs = cpu.Regs;
            regs.AF = state.AF;
            regs.BC = state.BC;
            regs.DE = state.DE;
            regs.HL = state.HL;
            regs.F_ = (byte)state.AF_;
            regs.A_ = (byte)(state.AF_ >> 8);
            regs.C_ = (byte)state.BC_;
            regs.B_ = (byte)(state.BC_ >> 8);
            regs.E_ = (byte)state.DE_;
            regs.D_ = (byte)(state.DE_ >> 8);
            regs.L_ = (byte)state.HL_;
            regs.H_ = (byte)(state.HL_ >> 8);
            regs.IX = state.IX;
            regs.IY = state.IY;
            regs.SP = state.SP;
            regs.PC = state.PC;
            regs.I = state.I;
            regs.R = state.R;
            cpu.RestoreInterruptState(state.IFF1, state.IFF2, state.InterruptMode);
            cpu.RestoreSnapshotExecutionState(
                halted: (state.Flags & 0x02) != 0,
                interruptPending: false,
                suppressInterrupts: (state.Flags & 0x01) != 0,
                flagsSet: (state.Flags & 0x04) != 0);
        }

        private static ushort ReadWord(byte[] data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));

        private static uint ReadDword(byte[] data, int offset) =>
            (uint)(data[offset] |
                   (data[offset + 1] << 8) |
                   (data[offset + 2] << 16) |
                   (data[offset + 3] << 24));

        private static string DisplayId(string id) => id.Replace("\0", "\\0", StringComparison.Ordinal);

        private sealed record ParsedSnapshot(
            SpectrumMachineModel Model,
            CpuState Cpu,
            SpectrumState Spectrum,
            AyState? Ay,
            IReadOnlyDictionary<int, byte[]> RamBanks);

        private sealed record CpuState(
            ushort AF, ushort BC, ushort DE, ushort HL,
            ushort AF_, ushort BC_, ushort DE_, ushort HL_,
            ushort IX, ushort IY, ushort SP, ushort PC,
            byte I, byte R, bool IFF1, bool IFF2, byte InterruptMode,
            uint FrameCycles, byte HoldInterruptCycles, byte Flags);

        private sealed record SpectrumState(byte Border, byte Port7ffd, byte PortFe);
        private sealed record AyState(byte SelectedRegister, byte[] Registers);
    }
}
