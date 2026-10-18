using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblySymbolTests
    {
        [Fact]
        public void SymbolTable_KeepsBankQualifiedAddressesDistinct()
        {
            var symbols = new DisassemblySymbolTable();

            Assert.True(symbols.TrySet(DisassemblyAddressSpace.Mapped, 0x1234, "MappedStart", false, out _));
            Assert.True(symbols.TrySet(DisassemblyAddressSpace.Ram(7), 0x1234, "LevelData", false, out _));

            Assert.Equal("MappedStart", symbols.GetName(DisassemblyAddressSpace.Mapped, 0x1234));
            Assert.Equal("LevelData", symbols.GetName(DisassemblyAddressSpace.Ram(7), 0x1234));
        }

        [Theory]
        [InlineData("")]
        [InlineData("9Start")]
        [InlineData("Has Space")]
        [InlineData("Bad-Name")]
        public void SymbolTable_RejectsInvalidLabelNames(string name)
        {
            var symbols = new DisassemblySymbolTable();

            Assert.False(symbols.TrySet(DisassemblyAddressSpace.Mapped, 0x8000, name, false, out string? error));
            Assert.NotNull(error);
        }

        [Fact]
        public void SymbolTable_RejectsDuplicateNamesAtDifferentLocations()
        {
            var symbols = new DisassemblySymbolTable();
            Assert.True(symbols.TrySet(DisassemblyAddressSpace.Mapped, 0x8000, "MainLoop", false, out _));

            Assert.False(symbols.TrySet(DisassemblyAddressSpace.Ram(3), 0x0100, "mainloop", false, out string? error));
            Assert.Contains("already assigned", error);
        }

        [Fact]
        public void AutomaticSymbols_AreStableAndDistinguishCallsFromBranches()
        {
            var symbols = new DisassemblySymbolTable();

            DisassemblySymbol call = symbols.EnsureAutomatic(DisassemblyAddressSpace.Mapped, 0x80BD, isCall: true);
            DisassemblySymbol branch = symbols.EnsureAutomatic(DisassemblyAddressSpace.Ram(7), 0x0123, isCall: false);

            Assert.Equal("SUB_80BD", call.Name);
            Assert.Equal("RAM7_LOC_0123", branch.Name);
            Assert.Same(call, symbols.EnsureAutomatic(DisassemblyAddressSpace.Mapped, 0x80BD, isCall: false));
        }

        [Fact]
        public void SymbolFile_RoundTripsMappedAndPhysicalSymbols()
        {
            DisassemblySymbol[] source =
            [
                new(DisassemblyAddressSpace.Mapped, 0x8000, "Entry", false),
                new(DisassemblyAddressSpace.Rom(1), 0x0010, "RomRoutine", false),
                new(DisassemblyAddressSpace.Ram(6), 0x2345, "GameData", false)
            ];

            string text = DisassemblySymbolFile.Format(source);

            Assert.True(DisassemblySymbolFile.TryParse(text, out IReadOnlyList<DisassemblySymbol> parsed, out _));
            Assert.Equal(source, parsed);
            Assert.Contains("MAP:8000 Entry", text);
            Assert.Contains("ROM1:0010 RomRoutine", text);
            Assert.Contains("RAM6:2345 GameData", text);
        }

        [Fact]
        public void SymbolFile_ReportsLineAndDoesNotReturnPartialImport()
        {
            const string text = "MAP:8000 Valid\nRAM9:1234 Invalid";

            Assert.False(DisassemblySymbolFile.TryParse(text, out IReadOnlyList<DisassemblySymbol> parsed, out string? error));
            Assert.Empty(parsed);
            Assert.Contains("Line 2", error);
        }

        [Fact]
        public void SymbolFile_RejectsDuplicateLocations()
        {
            const string text = "MAP:8000 FirstName\nMAP:8000 SecondName";

            Assert.False(DisassemblySymbolFile.TryParse(text, out IReadOnlyList<DisassemblySymbol> parsed, out string? error));
            Assert.Empty(parsed);
            Assert.Contains("already defined", error);
        }

        [Fact]
        public void SymbolTable_RejectsInvalidPersistedBankIdentity()
        {
            var symbols = new DisassemblySymbolTable();

            Assert.False(symbols.TrySet(
                DisassemblyAddressSpace.Ram(9),
                0x0100,
                "InvalidBank",
                false,
                out string? error));
            Assert.Contains("invalid memory-bank", error);
        }

        [Fact]
        public void ListingFormatter_EmitsLabelsAndTargetAnnotations()
        {
            var memory = new byte[65536];
            memory[0x8000] = 0xCD;
            memory[0x8001] = 0x10;
            memory[0x8002] = 0x80;
            var snapshot = new DisassemblySnapshot(memory, 0x8000, SpectrumMachineModel.Spectrum128K, 1, 0, 5);
            var symbols = new DisassemblySymbolTable();
            Assert.True(symbols.TrySet(DisassemblyAddressSpace.Mapped, 0x8000, "Entry", false, out _));
            Assert.True(symbols.TrySet(DisassemblyAddressSpace.Mapped, 0x8010, "DrawSprite", false, out _));
            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x8000, snapshot.ReadMemory);

            string listing = DisassemblyListingFormatter.Format(
                snapshot,
                DisassemblyAddressSpace.Mapped,
                0x8000,
                [instruction],
                includeHeader: false,
                symbols);

            Assert.Contains("Entry:", listing);
            Assert.Contains("CALL 8010H  ; -> DrawSprite", listing);
        }
    }
}
