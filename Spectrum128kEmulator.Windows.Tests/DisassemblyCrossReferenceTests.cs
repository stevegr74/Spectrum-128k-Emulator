using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblyCrossReferenceTests
    {
        [Fact]
        public void Build_FollowsReachableControlFlowWithoutTreatingArbitraryDataAsCode()
        {
            var memory = new byte[65536];
            Write(memory, 0x8000, 0xCD, 0x10, 0x80); // CALL 8010H
            Write(memory, 0x8003, 0xC2, 0x20, 0x80); // JP NZ,8020H
            Write(memory, 0x8006, 0xC3, 0x30, 0x80); // JP 8030H
            memory[0x8010] = 0xC9;
            memory[0x8020] = 0xC9;
            memory[0x8030] = 0xC9;
            Write(memory, 0x9000, 0xCD, 0x10, 0x90); // Unreachable bytes that resemble code.

            DisassemblyCrossReferenceIndex index = DisassemblyCrossReferenceIndex.Build(
                CreateSnapshot(memory),
                DisassemblyAddressSpace.Mapped,
                [0x8000]);

            AssertReference(index.GetOutgoing(DisassemblyAddressSpace.Mapped, 0x8000), 0x8000, 0x8010, DisassemblyCrossReferenceKind.Call);
            AssertReference(index.GetIncoming(DisassemblyAddressSpace.Mapped, 0x8020), 0x8003, 0x8020, DisassemblyCrossReferenceKind.Jump);
            AssertReference(index.GetIncoming(DisassemblyAddressSpace.Mapped, 0x8030), 0x8006, 0x8030, DisassemblyCrossReferenceKind.Jump);
            Assert.Empty(index.GetIncoming(DisassemblyAddressSpace.Mapped, 0x9010));
        }

        [Fact]
        public void Build_KeepsRelativeTargetsInTheirPhysicalBank()
        {
            byte[][] ramBanks = CreateBanks(8);
            Write(ramBanks[7], 0x0100, 0x18, 0x02); // JR 0104H
            ramBanks[7][0x0104] = 0xC9;

            DisassemblyCrossReferenceIndex index = DisassemblyCrossReferenceIndex.Build(
                CreateSnapshot(new byte[65536], ramBanks),
                DisassemblyAddressSpace.Ram(7),
                [0x0100]);

            DisassemblyCrossReference reference = Assert.Single(
                index.GetIncoming(DisassemblyAddressSpace.Ram(7), 0x0104));
            Assert.Equal(new DisassemblyLocation(DisassemblyAddressSpace.Ram(7), 0x0100), reference.Source);
            Assert.Equal(DisassemblyCrossReferenceKind.RelativeJump, reference.Kind);
            Assert.Empty(index.GetIncoming(DisassemblyAddressSpace.Ram(6), 0x0104));
        }

        [Fact]
        public void Build_TreatsRestartAsCallToItsFixedVector()
        {
            var memory = new byte[65536];
            memory[0x8000] = 0xEF; // RST 28H
            memory[0x8001] = 0xC9;
            memory[0x0028] = 0xC9;

            DisassemblyCrossReferenceIndex index = DisassemblyCrossReferenceIndex.Build(
                CreateSnapshot(memory),
                DisassemblyAddressSpace.Mapped,
                [0x8000]);

            AssertReference(index.GetIncoming(DisassemblyAddressSpace.Mapped, 0x0028), 0x8000, 0x0028, DisassemblyCrossReferenceKind.Call);
        }

        [Fact]
        public void Build_QualifiesAbsoluteRamTargetOnlyWhenCapturedMappingProvesIt()
        {
            byte[][] ramBanks = CreateBanks(8);
            Write(ramBanks[7], 0x0100, 0xCD, 0x00, 0xC2); // CALL C200H
            Write(ramBanks[6], 0x0100, 0xCD, 0x00, 0xC2);

            DisassemblySnapshot snapshot = CreateSnapshot(new byte[65536], ramBanks);
            DisassemblyCrossReferenceIndex activeBank = DisassemblyCrossReferenceIndex.Build(
                snapshot,
                DisassemblyAddressSpace.Ram(7),
                [0x0100]);
            DisassemblyCrossReferenceIndex inactiveBank = DisassemblyCrossReferenceIndex.Build(
                snapshot,
                DisassemblyAddressSpace.Ram(6),
                [0x0100]);

            Assert.Single(activeBank.GetIncoming(DisassemblyAddressSpace.Ram(7), 0x0200));
            Assert.Single(inactiveBank.GetIncoming(DisassemblyAddressSpace.Mapped, 0xC200));
            Assert.Empty(inactiveBank.GetIncoming(DisassemblyAddressSpace.Ram(6), 0x0200));
        }

        private static void AssertReference(
            IReadOnlyList<DisassemblyCrossReference> references,
            ushort source,
            ushort target,
            DisassemblyCrossReferenceKind kind)
        {
            DisassemblyCrossReference reference = Assert.Single(references);
            Assert.Equal(new DisassemblyLocation(DisassemblyAddressSpace.Mapped, source), reference.Source);
            Assert.Equal(new DisassemblyLocation(DisassemblyAddressSpace.Mapped, target), reference.Target);
            Assert.Equal(kind, reference.Kind);
        }

        private static DisassemblySnapshot CreateSnapshot(byte[] memory, IReadOnlyList<byte[]>? ramBanks = null) =>
            new(
                memory,
                programCounter: 0x8000,
                SpectrumMachineModel.Spectrum128K,
                currentRomBank: 1,
                pagedRamBank: 7,
                screenBank: 5,
                romBanks: CreateBanks(2),
                ramBanks: ramBanks ?? CreateBanks(8));

        private static byte[][] CreateBanks(int count) =>
            Enumerable.Range(0, count).Select(_ => new byte[0x4000]).ToArray();

        private static void Write(byte[] destination, int address, params byte[] bytes) =>
            Array.Copy(bytes, 0, destination, address, bytes.Length);
    }
}
