using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblyClassificationTests
    {
        [Fact]
        public void Map_SplitsOverwritesClearsAndMergesRanges()
        {
            var map = new DisassemblyClassificationMap();
            DisassemblyAddressSpace space = DisassemblyAddressSpace.Mapped;

            map.SetRange(space, 0x1000, 0x100F, DisassemblyClassification.Code);
            map.SetRange(space, 0x1008, 0x100B, DisassemblyClassification.Data);
            map.SetRange(space, 0x1009, 0x100A, DisassemblyClassification.Unknown);

            Assert.Equal(
                [
                    new DisassemblyClassificationRange(space, 0x1000, 0x1007, DisassemblyClassification.Code),
                    new DisassemblyClassificationRange(space, 0x1008, 0x1008, DisassemblyClassification.Data),
                    new DisassemblyClassificationRange(space, 0x100B, 0x100B, DisassemblyClassification.Data),
                    new DisassemblyClassificationRange(space, 0x100C, 0x100F, DisassemblyClassification.Code)
                ],
                map.Ranges);

            map.SetRange(space, 0x1008, 0x100B, DisassemblyClassification.Code);
            Assert.Equal(
                new DisassemblyClassificationRange(space, 0x1000, 0x100F, DisassemblyClassification.Code),
                Assert.Single(map.Ranges));
        }

        [Fact]
        public void Map_KeepsMappedAndPhysicalBanksDistinct()
        {
            var map = new DisassemblyClassificationMap();
            map.SetRange(DisassemblyAddressSpace.Mapped, 0x0100, 0x0103, DisassemblyClassification.Code);
            map.SetRange(DisassemblyAddressSpace.Ram(7), 0x0100, 0x0103, DisassemblyClassification.Data);

            Assert.Equal(DisassemblyClassification.Code, map.Get(DisassemblyAddressSpace.Mapped, 0x0102));
            Assert.Equal(DisassemblyClassification.Data, map.Get(DisassemblyAddressSpace.Ram(7), 0x0102));
            Assert.Equal(DisassemblyClassification.Unknown, map.Get(DisassemblyAddressSpace.Ram(6), 0x0102));
        }

        [Fact]
        public void CrossReferences_RecordTargetButDoNotDecodeConfirmedData()
        {
            var memory = new byte[65536];
            Write(memory, 0x8000, 0xC3, 0x10, 0x80); // JP 8010H
            Write(memory, 0x8010, 0xCD, 0x20, 0x80); // Bytes deliberately marked as data.
            memory[0x8020] = 0xC9;
            var map = new DisassemblyClassificationMap();
            map.SetRange(DisassemblyAddressSpace.Mapped, 0x8010, 0x8012, DisassemblyClassification.Data);

            DisassemblyCrossReferenceIndex index = DisassemblyCrossReferenceIndex.Build(
                CreateSnapshot(memory),
                DisassemblyAddressSpace.Mapped,
                [0x8000],
                map);

            Assert.Single(index.GetIncoming(DisassemblyAddressSpace.Mapped, 0x8010));
            Assert.Empty(index.GetOutgoing(DisassemblyAddressSpace.Mapped, 0x8010));
            Assert.Empty(index.GetIncoming(DisassemblyAddressSpace.Mapped, 0x8020));
        }

        [Fact]
        public void CodeRangeStart_CanSeedCrossReferenceAnalysis()
        {
            var memory = new byte[65536];
            Write(memory, 0x9000, 0xCD, 0x10, 0x90);
            memory[0x9003] = 0xC9;
            memory[0x9010] = 0xC9;
            var map = new DisassemblyClassificationMap();
            map.SetRange(DisassemblyAddressSpace.Mapped, 0x9000, 0x9003, DisassemblyClassification.Code);

            DisassemblyCrossReferenceIndex index = DisassemblyCrossReferenceIndex.Build(
                CreateSnapshot(memory),
                DisassemblyAddressSpace.Mapped,
                map.GetCodeEntryPoints(DisassemblyAddressSpace.Mapped),
                map);

            Assert.Single(index.GetIncoming(DisassemblyAddressSpace.Mapped, 0x9010));
        }

        [Fact]
        public void ListingAndMnemonicSearch_RespectConfirmedData()
        {
            var memory = new byte[65536];
            Write(memory, 0x8000, 0xCD, 0x10, 0x90);
            Write(memory, 0x8100, 0xCD, 0x10, 0x90);
            DisassemblySnapshot snapshot = CreateSnapshot(memory);
            var map = new DisassemblyClassificationMap();
            map.SetRange(DisassemblyAddressSpace.Mapped, 0x8000, 0x8002, DisassemblyClassification.Data);

            Z80Instruction data = DisassemblyListingFormatter.DisassembleAt(
                snapshot,
                DisassemblyAddressSpace.Mapped,
                0x8000,
                map);
            string listing = DisassemblyListingFormatter.Format(
                snapshot,
                DisassemblyAddressSpace.Mapped,
                0x8000,
                [data],
                includeHeader: false,
                classifications: map);
            bool found = DisassemblySearch.TryFindNext(
                snapshot,
                DisassemblyAddressSpace.Mapped,
                DisassemblySearchMode.Mnemonic,
                "CALL 9010H",
                0x8000,
                map,
                out DisassemblySearchResult? result,
                out _);

            Assert.Equal("DB CDH", data.Mnemonic);
            Assert.Contains("; DATA", listing);
            Assert.True(found);
            Assert.Equal((ushort)0x8100, result!.Address);
        }

        private static DisassemblySnapshot CreateSnapshot(byte[] memory) =>
            new(memory, 0x8000, SpectrumMachineModel.Spectrum128K, 1, 7, 5);

        private static void Write(byte[] destination, int address, params byte[] bytes) =>
            Array.Copy(bytes, 0, destination, address, bytes.Length);
    }
}
