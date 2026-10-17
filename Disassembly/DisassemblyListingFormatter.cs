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
            ArgumentNullException.ThrowIfNull(snapshot);
            var instructions = new List<Z80Instruction>();
            ushort address = startAddress;
            int coveredBytes = 0;
            while (coveredBytes < 65536)
            {
                Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(address, snapshot.ReadMemory);
                instructions.Add(instruction);
                int length = Math.Max(1, instruction.Length);
                coveredBytes += length;
                address = (ushort)(address + length);
            }

            return instructions;
        }

        public static string Format(
            DisassemblySnapshot snapshot,
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
                builder.AppendLine($"; Listing address: {listingAddress:X4}H");
                builder.AppendLine($"; {snapshot.MappingText}");
                builder.AppendLine();
            }

            foreach (Z80Instruction instruction in instructions)
                builder.AppendLine($"{instruction.Address:X4}:  {instruction.BytesText,-14}  {instruction.Mnemonic}");

            return builder.ToString().TrimEnd();
        }
    }
}
