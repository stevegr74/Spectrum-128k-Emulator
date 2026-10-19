using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public enum DisassemblyCrossReferenceKind
    {
        Call,
        Jump,
        RelativeJump
    }

    public readonly record struct DisassemblyLocation(
        DisassemblyAddressSpace AddressSpace,
        ushort Address);

    public sealed record DisassemblyCrossReference(
        DisassemblyLocation Source,
        DisassemblyLocation Target,
        DisassemblyCrossReferenceKind Kind,
        string Mnemonic);

    public sealed class DisassemblyCrossReferenceIndex
    {
        private const int MaximumInstructions = 32768;
        private readonly Dictionary<DisassemblyLocation, List<DisassemblyCrossReference>> incoming = new();
        private readonly Dictionary<DisassemblyLocation, List<DisassemblyCrossReference>> outgoing = new();

        public static DisassemblyCrossReferenceIndex Build(
            DisassemblySnapshot snapshot,
            DisassemblyAddressSpace addressSpace,
            IEnumerable<ushort> entryPoints,
            DisassemblyClassificationMap? classifications = null)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(entryPoints);

            var index = new DisassemblyCrossReferenceIndex();
            var pending = new Queue<ushort>(entryPoints
                .Where(address => address < addressSpace.Length)
                .Distinct());
            var visited = new HashSet<ushort>();

            while (pending.Count > 0 && visited.Count < MaximumInstructions)
            {
                int address = pending.Dequeue();
                while (address < addressSpace.Length && visited.Count < MaximumInstructions)
                {
                    ushort instructionAddress = (ushort)address;
                    if (!visited.Add(instructionAddress))
                        break;
                    if (classifications?.Get(addressSpace, instructionAddress) == DisassemblyClassification.Data)
                        break;

                    Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(
                        instructionAddress,
                        value => snapshot.ReadMemory(addressSpace, value));
                    if (classifications?.Contains(
                        addressSpace,
                        instructionAddress,
                        Math.Max(1, instruction.Length),
                        DisassemblyClassification.Data) == true)
                    {
                        break;
                    }
                    string mnemonic = instruction.Mnemonic.Trim();

                    if (instruction.BranchTarget is ushort encodedTarget &&
                        TryResolveTarget(snapshot, addressSpace, instruction, encodedTarget, out DisassemblyLocation target))
                    {
                        var source = new DisassemblyLocation(addressSpace, instructionAddress);
                        var reference = new DisassemblyCrossReference(
                            source,
                            target,
                            Classify(instruction),
                            mnemonic);
                        index.Add(reference);
                        if (target.AddressSpace == addressSpace &&
                            classifications?.Get(addressSpace, target.Address) != DisassemblyClassification.Data &&
                            !visited.Contains(target.Address))
                        {
                            pending.Enqueue(target.Address);
                        }
                    }

                    if (EndsSequentialFlow(mnemonic))
                        break;

                    int length = Math.Max(1, instruction.Length);
                    address += length;
                }
            }

            return index;
        }

        public IReadOnlyList<DisassemblyCrossReference> GetIncoming(
            DisassemblyAddressSpace addressSpace,
            ushort address) => Get(incoming, new DisassemblyLocation(addressSpace, address));

        public IReadOnlyList<DisassemblyCrossReference> GetOutgoing(
            DisassemblyAddressSpace addressSpace,
            ushort address) => Get(outgoing, new DisassemblyLocation(addressSpace, address));

        private void Add(DisassemblyCrossReference reference)
        {
            Add(incoming, reference.Target, reference);
            Add(outgoing, reference.Source, reference);
        }

        private static void Add(
            Dictionary<DisassemblyLocation, List<DisassemblyCrossReference>> lookup,
            DisassemblyLocation location,
            DisassemblyCrossReference reference)
        {
            if (!lookup.TryGetValue(location, out List<DisassemblyCrossReference>? references))
            {
                references = new List<DisassemblyCrossReference>();
                lookup.Add(location, references);
            }
            references.Add(reference);
        }

        private static IReadOnlyList<DisassemblyCrossReference> Get(
            Dictionary<DisassemblyLocation, List<DisassemblyCrossReference>> lookup,
            DisassemblyLocation location) => lookup.TryGetValue(location, out List<DisassemblyCrossReference>? references)
                ? references.OrderBy(reference => reference.Source.AddressSpace.Kind)
                    .ThenBy(reference => reference.Source.AddressSpace.Bank)
                    .ThenBy(reference => reference.Source.Address)
                    .ToArray()
                : Array.Empty<DisassemblyCrossReference>();

        private static DisassemblyCrossReferenceKind Classify(Z80Instruction instruction)
        {
            string mnemonic = instruction.Mnemonic.TrimStart();
            if (mnemonic.StartsWith("CALL ", StringComparison.OrdinalIgnoreCase) ||
                mnemonic.StartsWith("RST ", StringComparison.OrdinalIgnoreCase))
            {
                return DisassemblyCrossReferenceKind.Call;
            }
            if (mnemonic.StartsWith("JR", StringComparison.OrdinalIgnoreCase) ||
                mnemonic.StartsWith("DJNZ", StringComparison.OrdinalIgnoreCase))
            {
                return DisassemblyCrossReferenceKind.RelativeJump;
            }
            return DisassemblyCrossReferenceKind.Jump;
        }

        private static bool EndsSequentialFlow(string mnemonic)
        {
            if (mnemonic is "RET" or "RETI" or "RETN" ||
                mnemonic.StartsWith("JP (", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (mnemonic.StartsWith("JP ", StringComparison.OrdinalIgnoreCase))
                return !mnemonic.Contains(',');

            return mnemonic.StartsWith("JR,", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveTarget(
            DisassemblySnapshot snapshot,
            DisassemblyAddressSpace sourceSpace,
            Z80Instruction instruction,
            ushort encodedTarget,
            out DisassemblyLocation target)
        {
            if (sourceSpace.Kind == DisassemblyAddressSpaceKind.Mapped)
            {
                target = new DisassemblyLocation(sourceSpace, encodedTarget);
                return true;
            }

            bool relative = instruction.Mnemonic.StartsWith("JR", StringComparison.OrdinalIgnoreCase) ||
                instruction.Mnemonic.StartsWith("DJNZ", StringComparison.OrdinalIgnoreCase);
            if (relative && encodedTarget < sourceSpace.Length)
            {
                target = new DisassemblyLocation(sourceSpace, encodedTarget);
                return true;
            }

            if (sourceSpace.Kind == DisassemblyAddressSpaceKind.Rom && encodedTarget < 0x4000)
            {
                target = new DisassemblyLocation(sourceSpace, encodedTarget);
                return true;
            }

            if (sourceSpace.Kind == DisassemblyAddressSpaceKind.Ram &&
                TryResolveMappedRamOffset(snapshot, sourceSpace.Bank, encodedTarget, out ushort offset))
            {
                target = new DisassemblyLocation(sourceSpace, offset);
                return true;
            }

            target = new DisassemblyLocation(DisassemblyAddressSpace.Mapped, encodedTarget);
            return true;
        }

        private static bool TryResolveMappedRamOffset(
            DisassemblySnapshot snapshot,
            int bank,
            ushort address,
            out ushort offset)
        {
            if (bank == 5 && address is >= 0x4000 and < 0x8000)
            {
                offset = (ushort)(address - 0x4000);
                return true;
            }
            if (bank == 2 && address is >= 0x8000 and < 0xC000)
            {
                offset = (ushort)(address - 0x8000);
                return true;
            }
            if (bank == snapshot.PagedRamBank && address >= 0xC000)
            {
                offset = (ushort)(address - 0xC000);
                return true;
            }

            offset = 0;
            return false;
        }
    }
}
