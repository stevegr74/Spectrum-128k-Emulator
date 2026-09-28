using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblySnapshotTests
    {
        [Theory]
        [InlineData("1234", 0x1234)]
        [InlineData("0xABCD", 0xABCD)]
        [InlineData("$00ff", 0x00FF)]
        [InlineData("8000H", 0x8000)]
        public void AddressParser_AcceptsCommonHexFormats(string text, int expected)
        {
            Assert.True(DisassemblyAddressParser.TryParse(text, out ushort address));
            Assert.Equal((ushort)expected, address);
        }

        [Theory]
        [InlineData("")]
        [InlineData("10000")]
        [InlineData("XYZ")]
        [InlineData("0x")]
        public void AddressParser_RejectsInvalidOrOutOfRangeValues(string text)
        {
            Assert.False(DisassemblyAddressParser.TryParse(text, out _));
        }

        [Fact]
        public void Snapshot_ClonesMemoryAndDescribes128kPaging()
        {
            var memory = new byte[65536];
            memory[0xC000] = 0xC3;

            var snapshot = new DisassemblySnapshot(
                memory,
                0xC000,
                SpectrumMachineModel.Spectrum128K,
                currentRomBank: 1,
                pagedRamBank: 7,
                screenBank: 5);
            memory[0xC000] = 0x00;

            Assert.Equal((byte)0xC3, snapshot.ReadMemory(0xC000));
            Assert.Contains("128K", snapshot.MappingText);
            Assert.Contains("RAM 7", snapshot.MappingText);
            Assert.Contains("ROM 1", snapshot.MappingText);
        }

        [Fact]
        public void Snapshot_RequiresFullAddressSpace()
        {
            Assert.Throws<ArgumentException>(() => new DisassemblySnapshot(
                new byte[1024],
                0,
                SpectrumMachineModel.Spectrum48K,
                1,
                0,
                5));
        }
    }
}
