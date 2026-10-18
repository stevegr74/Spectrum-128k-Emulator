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

        [Fact]
        public void Snapshot_ClonesCpuStackContext()
        {
            var stackWords = new ushort[] { 0x1234, 0x5678 };
            var cpuState = new DisassemblyCpuState(
                0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0x8000, 0x4000, 0, 0,
                false, false, 1, false, false, 0,
                stackWords);

            var snapshot = new DisassemblySnapshot(
                new byte[65536],
                0x4000,
                SpectrumMachineModel.Spectrum48K,
                1,
                0,
                5,
                cpuState: cpuState);
            stackWords[0] = 0xFFFF;

            Assert.Equal((ushort)0x1234, snapshot.CpuState!.StackWords[0]);
        }

        [Fact]
        public void Snapshot_ClonesAndReadsPhysicalBanks()
        {
            byte[][] romBanks = CreateBanks(2);
            byte[][] ramBanks = CreateBanks(8);
            romBanks[1][0x1234] = 0xA5;
            ramBanks[7][0x2345] = 0x5A;

            var snapshot = new DisassemblySnapshot(
                new byte[65536],
                0,
                SpectrumMachineModel.Spectrum128K,
                1,
                7,
                5,
                romBanks: romBanks,
                ramBanks: ramBanks);
            romBanks[1][0x1234] = 0;
            ramBanks[7][0x2345] = 0;

            Assert.Equal(11, snapshot.AvailableAddressSpaces.Count);
            Assert.Equal((byte)0xA5, snapshot.ReadMemory(DisassemblyAddressSpace.Rom(1), 0x1234));
            Assert.Equal((byte)0x5A, snapshot.ReadMemory(DisassemblyAddressSpace.Ram(7), 0x2345));
            Assert.Equal((byte)0xFF, snapshot.ReadMemory(DisassemblyAddressSpace.Ram(7), 0x4000));
        }

        [Fact]
        public void Snapshot_RejectsIncompletePhysicalBankSets()
        {
            Assert.Throws<ArgumentException>(() => new DisassemblySnapshot(
                new byte[65536],
                0,
                SpectrumMachineModel.Spectrum128K,
                1,
                0,
                5,
                romBanks: [new byte[0x4000]]));

            byte[][] ramBanks = CreateBanks(8);
            ramBanks[3] = new byte[100];
            Assert.Throws<ArgumentException>(() => new DisassemblySnapshot(
                new byte[65536],
                0,
                SpectrumMachineModel.Spectrum128K,
                1,
                0,
                5,
                ramBanks: ramBanks));
        }

        private static byte[][] CreateBanks(int count) =>
            Enumerable.Range(0, count).Select(_ => new byte[0x4000]).ToArray();
    }
}
