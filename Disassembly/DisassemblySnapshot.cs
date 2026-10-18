using System.Globalization;

namespace Spectrum128kEmulator
{
    public sealed record DisassemblyCpuState(
        ushort AF,
        ushort BC,
        ushort DE,
        ushort HL,
        ushort AlternateAF,
        ushort AlternateBC,
        ushort AlternateDE,
        ushort AlternateHL,
        ushort IX,
        ushort IY,
        ushort SP,
        ushort PC,
        byte I,
        byte R,
        bool IFF1,
        bool IFF2,
        int InterruptMode,
        bool IsHalted,
        bool InterruptPending,
        ulong TStates,
        IReadOnlyList<ushort> StackWords);

    public sealed class DisassemblySnapshot
    {
        private readonly byte[] memory;

        public DisassemblySnapshot(
            byte[] memory,
            ushort programCounter,
            SpectrumMachineModel machineModel,
            int currentRomBank,
            int pagedRamBank,
            int screenBank,
            DateTime? capturedAtUtc = null,
            DisassemblyCpuState? cpuState = null)
        {
            ArgumentNullException.ThrowIfNull(memory);
            if (memory.Length != 65536)
                throw new ArgumentException("A disassembly snapshot must contain the full logical 64K address space.", nameof(memory));

            this.memory = (byte[])memory.Clone();
            ProgramCounter = programCounter;
            MachineModel = machineModel;
            CurrentRomBank = currentRomBank;
            PagedRamBank = pagedRamBank;
            ScreenBank = screenBank;
            CapturedAtUtc = capturedAtUtc ?? DateTime.UtcNow;
            CpuState = cpuState is null
                ? null
                : cpuState with { StackWords = Array.AsReadOnly(cpuState.StackWords.ToArray()) };
        }

        public ushort ProgramCounter { get; }
        public SpectrumMachineModel MachineModel { get; }
        public int CurrentRomBank { get; }
        public int PagedRamBank { get; }
        public int ScreenBank { get; }
        public DateTime CapturedAtUtc { get; }
        public DisassemblyCpuState? CpuState { get; }

        public byte ReadMemory(ushort address) => memory[address];

        public string MappingText => MachineModel == SpectrumMachineModel.Spectrum48K
            ? "48K | ROM 1 | 4000-7FFF: RAM 5 | 8000-BFFF: RAM 2 | C000-FFFF: RAM 0"
            : $"128K | ROM {CurrentRomBank} | 4000-7FFF: RAM 5 | 8000-BFFF: RAM 2 | C000-FFFF: RAM {PagedRamBank} | Screen: RAM {ScreenBank}";
    }

    public static class DisassemblyAddressParser
    {
        public static bool TryParse(string? text, out ushort address)
        {
            address = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string value = text.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                value = value[2..];
            else if (value.StartsWith('$'))
                value = value[1..];
            else if (value.EndsWith('H') || value.EndsWith('h'))
                value = value[..^1];

            return value.Length is > 0 and <= 4 &&
                ushort.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
        }
    }
}
