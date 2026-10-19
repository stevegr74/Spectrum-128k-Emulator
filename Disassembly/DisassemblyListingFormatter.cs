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
            ushort startAddress,
            DisassemblyClassificationMap? classifications = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            var instructions = new List<Z80Instruction>();
            int length = addressSpace.Length;
            int address = startAddress % length;
            int coveredBytes = 0;
            while (coveredBytes < length)
            {
                ushort instructionAddress = (ushort)address;
                Z80Instruction instruction = DisassembleAt(
                    snapshot,
                    addressSpace,
                    instructionAddress,
                    classifications);
                instructions.Add(instruction);
                int instructionLength = Math.Max(1, instruction.Length);
                coveredBytes += instructionLength;
                address = (address + instructionLength) % addressSpace.Length;
            }

            return instructions;
        }

        public static Z80Instruction DisassembleAt(
            DisassemblySnapshot snapshot,
            DisassemblyAddressSpace addressSpace,
            ushort address,
            DisassemblyClassificationMap? classifications = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            byte value = snapshot.ReadMemory(addressSpace, address);
            if (classifications?.Get(addressSpace, address) == DisassemblyClassification.Data)
                return new Z80Instruction(address, [value], 1, $"DB {value:X2}H", null);

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(
                address,
                candidate => snapshot.ReadMemory(addressSpace, candidate));
            return classifications?.Contains(
                addressSpace,
                address,
                Math.Max(1, instruction.Length),
                DisassemblyClassification.Data) == true
                    ? new Z80Instruction(address, [value], 1, $"DB {value:X2}H", null)
                    : instruction;
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
            bool includeHeader,
            DisassemblySymbolTable? symbols = null,
            DisassemblyClassificationMap? classifications = null)
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
                string? label = symbols?.GetName(addressSpace, instruction.Address);
                if (label != null)
                    builder.AppendLine($"{label}:");

                var annotations = new List<string>();
                if (instruction.BranchTarget is ushort target &&
                    symbols?.GetName(addressSpace, target) is string targetLabel)
                {
                    annotations.Add($"-> {targetLabel}");
                }
                DisassemblyClassification classification = classifications?.Get(addressSpace, instruction.Address)
                    ?? DisassemblyClassification.Unknown;
                if (classification != DisassemblyClassification.Unknown)
                    annotations.Add(classification.ToString().ToUpperInvariant());
                string targetComment = annotations.Count == 0
                    ? string.Empty
                    : $"  ; {string.Join(", ", annotations)}";
                if (addressSpace.Kind == DisassemblyAddressSpaceKind.Mapped)
                {
                    builder.AppendLine($"{instruction.Address:X4}:  {instruction.BytesText,-14}  {instruction.Mnemonic}{targetComment}");
                }
                else
                {
                    string addressText = $"{addressSpace.DisplayName}:{instruction.Address:X4}:";
                    builder.AppendLine($"{addressText,-13}  {instruction.BytesText,-14}  {instruction.Mnemonic}{targetComment}");
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
