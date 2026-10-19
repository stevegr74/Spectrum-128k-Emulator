using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DisassemblyNavigationTests
    {
        [Fact]
        public void NavigationHistory_BackForwardAndNewBranchBehaveLikeBrowserHistory()
        {
            var history = new DisassemblyNavigationHistory();

            Assert.True(history.NavigateTo(0x1000));
            Assert.True(history.NavigateTo(0x2000));
            Assert.True(history.NavigateTo(0x3000));
            Assert.True(history.TryGoBack(out DisassemblyNavigationLocation back));
            Assert.Equal((ushort)0x2000, back.ListingAddress);
            Assert.True(history.TryGoForward(out DisassemblyNavigationLocation forward));
            Assert.Equal((ushort)0x3000, forward.ListingAddress);
            Assert.True(history.TryGoBack(out _));

            Assert.True(history.NavigateTo(0x4000));
            Assert.False(history.CanGoForward);
            Assert.Equal((ushort)0x4000, history.Current?.ListingAddress);
        }

        [Fact]
        public void NavigationHistory_DoesNotDuplicateCurrentAddress()
        {
            var history = new DisassemblyNavigationHistory();

            Assert.True(history.NavigateTo(0x1234));
            Assert.False(history.NavigateTo(0x1234));
            Assert.False(history.CanGoBack);
        }

        [Fact]
        public void NavigationHistory_BackRestoresSelectedBranchSourceWithinOriginalListing()
        {
            var history = new DisassemblyNavigationHistory();

            Assert.True(history.NavigateTo(0x1000));
            Assert.True(history.UpdateCurrentSelection(0x1015));
            Assert.True(history.NavigateTo(0x4000));

            Assert.True(history.TryGoBack(out DisassemblyNavigationLocation back));
            Assert.Equal((ushort)0x1000, back.ListingAddress);
            Assert.Equal((ushort)0x1015, back.SelectedAddress);
        }

        [Fact]
        public void NavigationHistory_RestoresPhysicalAddressSpace()
        {
            var history = new DisassemblyNavigationHistory();
            var ramSeven = DisassemblyAddressSpace.Ram(7);

            Assert.True(history.NavigateTo(new DisassemblyNavigationLocation(0x0100, 0x0104, ramSeven)));
            Assert.True(history.NavigateTo(new DisassemblyNavigationLocation(0x8000, 0x8000)));
            Assert.True(history.TryGoBack(out DisassemblyNavigationLocation back));

            Assert.Equal(ramSeven, back.AddressSpace);
            Assert.Equal((ushort)0x0104, back.SelectedAddress);
        }

        [Theory]
        [InlineData("3E 10", "3E 10")]
        [InlineData("DD,21,00,80", "DD 21 00 80")]
        [InlineData("C30080", "C3 00 80")]
        [InlineData("0xAF 00", "AF 00")]
        public void ByteParser_AcceptsReadableAndCompactHex(string text, string expected)
        {
            Assert.True(DisassemblySearch.TryParseBytes(text, out byte[] bytes));
            Assert.Equal(expected, string.Join(' ', bytes.Select(value => value.ToString("X2"))));
        }

        [Theory]
        [InlineData("")]
        [InlineData("GG")]
        [InlineData("123")]
        public void ByteParser_RejectsInvalidInput(string text)
        {
            Assert.False(DisassemblySearch.TryParseBytes(text, out _));
        }

        [Fact]
        public void Search_FindsAddressBytesAndMnemonicAcrossCapture()
        {
            byte[] memory = Enumerable.Repeat((byte)0x00, 65536).ToArray();
            memory[0x1234] = 0xC3;
            memory[0x1235] = 0x78;
            memory[0x1236] = 0x56;
            DisassemblySnapshot snapshot = CreateSnapshot(memory);

            Assert.True(DisassemblySearch.TryFindNext(
                snapshot, DisassemblySearchMode.Address, "BEEF", 0, out DisassemblySearchResult? address, out _));
            Assert.Equal((ushort)0xBEEF, address!.Address);

            Assert.True(DisassemblySearch.TryFindNext(
                snapshot, DisassemblySearchMode.Bytes, "C3 78 56", 0x1000, out DisassemblySearchResult? bytes, out _));
            Assert.Equal((ushort)0x1234, bytes!.Address);

            Assert.True(DisassemblySearch.TryFindNext(
                snapshot, DisassemblySearchMode.Mnemonic, "JP 5678", 0x1000, out DisassemblySearchResult? mnemonic, out _));
            Assert.Equal((ushort)0x1234, mnemonic!.Address);
        }

        [Fact]
        public void ByteSearch_WrapsAcrossAddressSpaceEnd()
        {
            var memory = new byte[65536];
            memory[0xFFFF] = 0xAA;
            memory[0] = 0xBB;
            DisassemblySnapshot snapshot = CreateSnapshot(memory);

            Assert.True(DisassemblySearch.TryFindNext(
                snapshot, DisassemblySearchMode.Bytes, "AA BB", 0xFF00, out DisassemblySearchResult? result, out _));
            Assert.Equal((ushort)0xFFFF, result!.Address);
        }

        [Fact]
        public void PhysicalBankSearch_IsBoundedAndDoesNotWrapSequenceAcrossBankEnd()
        {
            byte[][] romBanks = CreateBanks(2);
            byte[][] ramBanks = CreateBanks(8);
            ramBanks[3][0x0100] = 0x3E;
            ramBanks[3][0x0101] = 0x42;
            ramBanks[3][0x3FFF] = 0xAA;
            ramBanks[3][0] = 0xBB;
            DisassemblySnapshot snapshot = CreateSnapshot(new byte[65536], romBanks: romBanks, ramBanks: ramBanks);

            Assert.True(DisassemblySearch.TryFindNext(
                snapshot, DisassemblyAddressSpace.Ram(3), DisassemblySearchMode.Bytes, "3E 42", 0,
                out DisassemblySearchResult? result, out _));
            Assert.Equal((ushort)0x0100, result!.Address);
            Assert.False(DisassemblySearch.TryFindNext(
                snapshot, DisassemblyAddressSpace.Ram(3), DisassemblySearchMode.Bytes, "AA BB", 0,
                out _, out _));
            Assert.False(DisassemblySearch.TryFindNext(
                snapshot, DisassemblyAddressSpace.Ram(3), DisassemblySearchMode.Address, "4000", 0,
                out _, out _));
        }

        [Fact]
        public void ListingFormatter_IncludesCaptureContextAndStableRows()
        {
            var memory = new byte[65536];
            memory[0x8000] = 0x3E;
            memory[0x8001] = 0x10;
            var captured = new DateTime(2026, 9, 28, 20, 30, 0, DateTimeKind.Utc);
            DisassemblySnapshot snapshot = CreateSnapshot(memory, captured);
            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x8000, snapshot.ReadMemory);

            string listing = DisassemblyListingFormatter.Format(snapshot, 0x8000, new[] { instruction }, includeHeader: true);

            Assert.Contains("Captured UTC: 2026-09-28T20:30:00.0000000Z", listing);
            Assert.Contains("Captured PC: 8000H", listing);
            Assert.Contains("Listing address: 8000H", listing);
            Assert.Contains("8000:  3E 10", listing);
            Assert.Contains("LD A,10H", listing);
        }

        [Fact]
        public void CompleteListing_CoversEntireMappedAddressSpaceFromRequestedAddress()
        {
            DisassemblySnapshot snapshot = CreateSnapshot(new byte[65536]);

            IReadOnlyList<Z80Instruction> instructions =
                DisassemblyListingFormatter.DisassembleCompleteAddressSpace(snapshot, 0x8000);

            Assert.Equal(65536, instructions.Count);
            Assert.Equal((ushort)0x8000, instructions[0].Address);
            Assert.Equal((ushort)0x7FFF, instructions[^1].Address);
        }

        [Fact]
        public void PhysicalBankListing_CoversOnlySelectedBankAndQualifiesExportedAddresses()
        {
            byte[][] romBanks = CreateBanks(2);
            byte[][] ramBanks = CreateBanks(8);
            ramBanks[4][0x0200] = 0x3E;
            ramBanks[4][0x0201] = 0x10;
            DisassemblySnapshot snapshot = CreateSnapshot(new byte[65536], romBanks: romBanks, ramBanks: ramBanks);

            IReadOnlyList<Z80Instruction> instructions =
                DisassemblyListingFormatter.DisassembleCompleteAddressSpace(
                    snapshot,
                    DisassemblyAddressSpace.Ram(4),
                    0x0200);
            string listing = DisassemblyListingFormatter.Format(
                snapshot,
                DisassemblyAddressSpace.Ram(4),
                0x0200,
                instructions.Take(1),
                includeHeader: true);

            Assert.InRange(instructions.Sum(instruction => Math.Max(1, instruction.Length)), 0x4000, 0x4003);
            Assert.Contains("Address space: RAM 4", listing);
            Assert.Contains("RAM 4:0200:", listing);
            Assert.Contains("LD A,10H", listing);
        }

        [Fact]
        public void WindowSettings_RoundTripAndRecoverFromInvalidJson()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "disassembler.json");
            try
            {
                var store = new DisassemblerWindowSettingsStore(path);
                store.Save(new DisassemblerWindowSettings
                {
                    Width = 1111,
                    Height = 777,
                    ColumnWidths = [80, 160, 500, 90],
                    LastListingAddress = 0xABCD,
                    LastAddressSpaceKind = DisassemblyAddressSpaceKind.Ram,
                    LastAddressSpaceBank = 6,
                    Breakpoints =
                    [
                        new DisassemblerBreakpointSetting { Address = 0x8123, Enabled = false }
                    ],
                    Symbols =
                    [
                        new DisassemblerSymbolSetting
                        {
                            AddressSpaceKind = DisassemblyAddressSpaceKind.Rom,
                            Bank = 1,
                            Address = 0x1234,
                            Name = "RomRoutine",
                            IsAutomatic = true
                        }
                    ],
                    Classifications =
                    [
                        new DisassemblerClassificationSetting
                        {
                            AddressSpaceKind = DisassemblyAddressSpaceKind.Ram,
                            Bank = 7,
                            StartAddress = 0x0200,
                            EndAddress = 0x021F,
                            Classification = DisassemblyClassification.Data
                        }
                    ]
                });

                DisassemblerWindowSettings loaded = store.Load();
                Assert.Equal(1111, loaded.Width);
                Assert.Equal(777, loaded.Height);
                Assert.Equal(new[] { 80, 160, 500, 90 }, loaded.ColumnWidths);
                Assert.Equal((ushort)0xABCD, loaded.LastListingAddress);
                Assert.Equal(DisassemblyAddressSpaceKind.Ram, loaded.LastAddressSpaceKind);
                Assert.Equal(6, loaded.LastAddressSpaceBank);
                Assert.Single(loaded.Breakpoints);
                Assert.Equal((ushort)0x8123, loaded.Breakpoints[0].Address);
                Assert.False(loaded.Breakpoints[0].Enabled);
                Assert.Single(loaded.Symbols);
                Assert.Equal("RomRoutine", loaded.Symbols[0].Name);
                Assert.True(loaded.Symbols[0].IsAutomatic);
                DisassemblerClassificationSetting classification = Assert.Single(loaded.Classifications);
                Assert.Equal(DisassemblyAddressSpaceKind.Ram, classification.AddressSpaceKind);
                Assert.Equal(7, classification.Bank);
                Assert.Equal((ushort)0x0200, classification.StartAddress);
                Assert.Equal((ushort)0x021F, classification.EndAddress);
                Assert.Equal(DisassemblyClassification.Data, classification.Classification);

                File.WriteAllText(path, "not-json");
                Assert.Null(store.Load().LastListingAddress);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        private static DisassemblySnapshot CreateSnapshot(
            byte[] memory,
            DateTime? capturedAtUtc = null,
            IReadOnlyList<byte[]>? romBanks = null,
            IReadOnlyList<byte[]>? ramBanks = null)
        {
            return new DisassemblySnapshot(
                memory,
                programCounter: 0x8000,
                SpectrumMachineModel.Spectrum128K,
                currentRomBank: 1,
                pagedRamBank: 7,
                screenBank: 5,
                capturedAtUtc,
                romBanks: romBanks,
                ramBanks: ramBanks);
        }

        private static byte[][] CreateBanks(int count) =>
            Enumerable.Range(0, count).Select(_ => new byte[0x4000]).ToArray();
    }
}
