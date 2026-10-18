using System.Text;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public static class DisassemblyListingFormatter
    {
        public static IReadOnlyList<Z80Instruction> DisassembleCompleteAddressSpace(
            DisassemblySnapshot snapshot,
            ushort startAddress)
        {
            return DisassembleCompleteAddressSpace(snapshot, DisassemblyAddressSpace.Mapped, startAddress);
        }

        public static IReadOnlyList<Z80Instruction> DisassembleCompleteAddressSpace(
            DisassemblySnapshot snapshot,
            DisassemblyAddressSpace addressSpace,
            ushort startAddress)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var instructions = new List<Z80Instruction>();
            int length = addressSpace.Length;
            int address = startAddress % length;
            int coveredBytes = 0;
            while (coveredBytes < length)
            {
                ushort instructionAddress = (ushort)address;
                Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(
                    instructionAddress,
                    value => snapshot.ReadMemory(addressSpace, value));
                instructions.Add(instruction);
                int instructionLength = Math.Max(1, instruction.Length);
                coveredBytes += instructionLength;
                address = (address + instructionLength) % addressSpace.Length;
            }

            return instructions;
        }

        public static string Format(
            DisassemblySnapshot snapshot,
            ushort listingAddress,
            IEnumerable<Z80Instruction> instructions,
            bool includeHeader)
        {
            return Format(
                snapshot,
                DisassemblyAddressSpace.Mapped,
                listingAddress,
                instructions,
                includeHeader);
        }

        public static string Format(
            DisassemblySnapshot snapshot,
            DisassemblyAddressSpace addressSpace,
            ushort listingAddress,
            IEnumerable<Z80Instruction> instructions,
            bool includeHeader)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(instructions);

            var builder = new StringBuilder();
            if (includeHeader)
            {
                builder.AppendLine("; Z80 disassembly - immutable paused capture");
                builder.AppendLine($"; Captured UTC: {snapshot.CapturedAtUtc:O}");
                builder.AppendLine($"; Captured PC: {snapshot.ProgramCounter:X4}H");
                builder.AppendLine($"; Address space: {addressSpace.DisplayName}");
                builder.AppendLine($"; Listing address: {addressSpace.FormatAddress(listingAddress)}");
                builder.AppendLine($"; {snapshot.MappingText}");
                builder.AppendLine();
            }

            foreach (Z80Instruction instruction in instructions)
            {
                if (addressSpace.Kind == DisassemblyAddressSpaceKind.Mapped)
                {
                    builder.AppendLine($"{instruction.Address:X4}:  {instruction.BytesText,-14}  {instruction.Mnemonic}");
                }
                else
                {
                    string addressText = $"{addressSpace.DisplayName}:{instruction.Address:X4}:";
                    builder.AppendLine($"{addressText,-13}  {instruction.BytesText,-14}  {instruction.Mnemonic}");
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
