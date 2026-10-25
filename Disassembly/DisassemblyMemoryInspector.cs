namespace Spectrum128kEmulator
{
    public sealed record DisassemblyMemoryRow(
        ushort Address,
        IReadOnlyList<byte> Bytes,
        int SelectedByteIndex)
    {
        public string HexText => string.Join(' ', Bytes.Select((value, index) =>
            index == SelectedByteIndex ? $"[{value:X2}]" : $"{value:X2}"));

        public string AsciiText => new(Bytes.Select(value => value is >= 32 and <= 126 ? (char)value : '.').ToArray());
    }

    public static class DisassemblyMemoryInspector
    {
        public static IReadOnlyList<DisassemblyMemoryRow> CreateRows(
            DisassemblySnapshot snapshot,
            DisassemblyAddressSpace addressSpace,
            ushort selectedAddress,
            int rowCount = 16,
            int bytesPerRow = 8)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            if (rowCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(rowCount));
            if (bytesPerRow <= 0 || bytesPerRow > 16)
                throw new ArgumentOutOfRangeException(nameof(bytesPerRow));

            int length = addressSpace.Length;
            int normalizedAddress = selectedAddress % length;
            int firstAddress = normalizedAddress - (normalizedAddress % bytesPerRow);
            var rows = new List<DisassemblyMemoryRow>(rowCount);

            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                int rowAddress = firstAddress + (rowIndex * bytesPerRow);
                if (addressSpace.Kind != DisassemblyAddressSpaceKind.Mapped && rowAddress >= length)
                    break;

                rowAddress %= length;
                var bytes = new byte[Math.Min(bytesPerRow, length - rowAddress)];
                for (int index = 0; index < bytes.Length; index++)
                    bytes[index] = snapshot.ReadMemory(addressSpace, (ushort)(rowAddress + index));

                int selectedIndex = normalizedAddress >= rowAddress && normalizedAddress < rowAddress + bytes.Length
                    ? normalizedAddress - rowAddress
                    : -1;
                rows.Add(new DisassemblyMemoryRow((ushort)rowAddress, bytes, selectedIndex));
            }

            return rows;
        }
    }
}
