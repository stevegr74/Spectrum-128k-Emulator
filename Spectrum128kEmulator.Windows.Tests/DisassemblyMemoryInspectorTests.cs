using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblyMemoryInspectorTests
    {
        [Fact]
        public void CreateRows_FormatsMappedBytesAndMarksSelectedAddress()
        {
            byte[] memory = Enumerable.Range(0, 65536).Select(value => (byte)value).ToArray();
            DisassemblySnapshot snapshot = CreateSnapshot(memory);

            IReadOnlyList<DisassemblyMemoryRow> rows = DisassemblyMemoryInspector.CreateRows(
                snapshot,
                DisassemblyAddressSpace.Mapped,
                0x8003,
                rowCount: 2);

            Assert.Equal((ushort)0x8000, rows[0].Address);
            Assert.Equal(3, rows[0].SelectedByteIndex);
            Assert.Equal([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07], rows[0].Bytes);
            Assert.Contains("[03]", rows[0].HexText);
            Assert.Equal("........", rows[0].AsciiText);
            Assert.Equal((ushort)0x8008, rows[1].Address);
        }

        [Fact]
        public void CreateRows_ReadsPhysicalBankAndStopsAtItsBoundary()
        {
            byte[][] ramBanks = CreateBanks(8);
            for (int index = 0; index < 8; index++)
                ramBanks[7][0x3FF8 + index] = (byte)(0x41 + index);
            DisassemblySnapshot snapshot = CreateSnapshot(new byte[65536], ramBanks);

            IReadOnlyList<DisassemblyMemoryRow> rows = DisassemblyMemoryInspector.CreateRows(
                snapshot,
                DisassemblyAddressSpace.Ram(7),
                0x3FFD,
                rowCount: 4);

            DisassemblyMemoryRow row = Assert.Single(rows);
            Assert.Equal((ushort)0x3FF8, row.Address);
            Assert.Equal(5, row.SelectedByteIndex);
            Assert.Equal("ABCDEFGH", row.AsciiText);
        }

        private static DisassemblySnapshot CreateSnapshot(
            byte[] memory,
            IReadOnlyList<byte[]>? ramBanks = null) =>
            new(
                memory,
                0x8000,
                SpectrumMachineModel.Spectrum128K,
                1,
                7,
                5,
                romBanks: CreateBanks(2),
                ramBanks: ramBanks ?? CreateBanks(8));

        private static byte[][] CreateBanks(int count) =>
            Enumerable.Range(0, count).Select(_ => new byte[0x4000]).ToArray();
    }
}
