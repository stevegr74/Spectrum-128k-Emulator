namespace Spectrum128kEmulator
{
    public enum DisassemblyClassification
    {
        Unknown,
        Code,
        Data
    }

    public sealed record DisassemblyClassificationRange(
        DisassemblyAddressSpace AddressSpace,
        ushort StartAddress,
        ushort EndAddress,
        DisassemblyClassification Classification);

    public sealed class DisassemblyClassificationMap
    {
        private readonly List<DisassemblyClassificationRange> ranges = [];

        public DisassemblyClassificationMap(IEnumerable<DisassemblyClassificationRange>? initialRanges = null)
        {
            if (initialRanges == null)
                return;

            foreach (DisassemblyClassificationRange range in initialRanges)
            {
                if (IsValid(range))
                    SetRange(range.AddressSpace, range.StartAddress, range.EndAddress, range.Classification);
            }
        }

        public IReadOnlyList<DisassemblyClassificationRange> Ranges => ranges.ToArray();

        public DisassemblyClassification Get(DisassemblyAddressSpace addressSpace, ushort address)
        {
            DisassemblyClassificationRange? range = ranges.FirstOrDefault(candidate =>
                candidate.AddressSpace == addressSpace &&
                address >= candidate.StartAddress &&
                address <= candidate.EndAddress);
            return range?.Classification ?? DisassemblyClassification.Unknown;
        }

        public IEnumerable<ushort> GetCodeEntryPoints(DisassemblyAddressSpace addressSpace) => ranges
            .Where(range => range.AddressSpace == addressSpace && range.Classification == DisassemblyClassification.Code)
            .Select(range => range.StartAddress);

        public bool Contains(
            DisassemblyAddressSpace addressSpace,
            ushort startAddress,
            int length,
            DisassemblyClassification classification)
        {
            if (length <= 0)
                return false;

            for (int offset = 0; offset < length; offset++)
            {
                int address = startAddress + offset;
                if (address >= addressSpace.Length)
                    break;
                if (Get(addressSpace, (ushort)address) == classification)
                    return true;
            }
            return false;
        }

        public void SetRange(
            DisassemblyAddressSpace addressSpace,
            ushort startAddress,
            ushort endAddress,
            DisassemblyClassification classification)
        {
            Validate(addressSpace, startAddress, endAddress, classification);

            var updated = new List<DisassemblyClassificationRange>();
            foreach (DisassemblyClassificationRange existing in ranges)
            {
                if (existing.AddressSpace != addressSpace ||
                    existing.EndAddress < startAddress ||
                    existing.StartAddress > endAddress)
                {
                    updated.Add(existing);
                    continue;
                }

                if (existing.StartAddress < startAddress)
                {
                    updated.Add(existing with { EndAddress = (ushort)(startAddress - 1) });
                }
                if (existing.EndAddress > endAddress)
                {
                    updated.Add(existing with { StartAddress = (ushort)(endAddress + 1) });
                }
            }

            if (classification != DisassemblyClassification.Unknown)
            {
                updated.Add(new DisassemblyClassificationRange(
                    addressSpace,
                    startAddress,
                    endAddress,
                    classification));
            }

            ranges.Clear();
            ranges.AddRange(MergeAdjacent(updated));
        }

        private static IEnumerable<DisassemblyClassificationRange> MergeAdjacent(
            IEnumerable<DisassemblyClassificationRange> source)
        {
            DisassemblyClassificationRange? pending = null;
            foreach (DisassemblyClassificationRange range in source
                .OrderBy(value => value.AddressSpace.Kind)
                .ThenBy(value => value.AddressSpace.Bank)
                .ThenBy(value => value.StartAddress))
            {
                if (pending != null &&
                    pending.AddressSpace == range.AddressSpace &&
                    pending.Classification == range.Classification &&
                    pending.EndAddress + 1 == range.StartAddress)
                {
                    pending = pending with { EndAddress = range.EndAddress };
                    continue;
                }

                if (pending != null)
                    yield return pending;
                pending = range;
            }

            if (pending != null)
                yield return pending;
        }

        private static bool IsValid(DisassemblyClassificationRange range) =>
            IsValidAddressSpace(range.AddressSpace) &&
            range.StartAddress <= range.EndAddress &&
            range.EndAddress < range.AddressSpace.Length &&
            range.Classification is DisassemblyClassification.Code or DisassemblyClassification.Data;

        private static void Validate(
            DisassemblyAddressSpace addressSpace,
            ushort startAddress,
            ushort endAddress,
            DisassemblyClassification classification)
        {
            if (!IsValidAddressSpace(addressSpace))
                throw new ArgumentOutOfRangeException(nameof(addressSpace));
            if (startAddress > endAddress || endAddress >= addressSpace.Length)
                throw new ArgumentOutOfRangeException(nameof(endAddress));
            if (!Enum.IsDefined(classification))
                throw new ArgumentOutOfRangeException(nameof(classification));
        }

        private static bool IsValidAddressSpace(DisassemblyAddressSpace addressSpace) => addressSpace.Kind switch
        {
            DisassemblyAddressSpaceKind.Mapped => addressSpace.Bank == 0,
            DisassemblyAddressSpaceKind.Rom => addressSpace.Bank is >= 0 and < 2,
            DisassemblyAddressSpaceKind.Ram => addressSpace.Bank is >= 0 and < 8,
            _ => false
        };
    }
}
