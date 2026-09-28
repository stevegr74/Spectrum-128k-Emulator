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
            Assert.True(history.TryGoBack(out ushort back));
            Assert.Equal((ushort)0x2000, back);
            Assert.True(history.TryGoForward(out ushort forward));
            Assert.Equal((ushort)0x3000, forward);
            Assert.True(history.TryGoBack(out _));

            Assert.True(history.NavigateTo(0x4000));
            Assert.False(history.CanGoForward);
            Assert.Equal((ushort)0x4000, history.Current);
        }

        [Fact]
        public void NavigationHistory_DoesNotDuplicateCurrentAddress()
        {
            var history = new DisassemblyNavigationHistory();

            Assert.True(history.NavigateTo(0x1234));
            Assert.False(history.NavigateTo(0x1234));
            Assert.False(history.CanGoBack);
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
                    LastListingAddress = 0xABCD
                });

                DisassemblerWindowSettings loaded = store.Load();
                Assert.Equal(1111, loaded.Width);
                Assert.Equal(777, loaded.Height);
                Assert.Equal(new[] { 80, 160, 500, 90 }, loaded.ColumnWidths);
                Assert.Equal((ushort)0xABCD, loaded.LastListingAddress);

                File.WriteAllText(path, "not-json");
                Assert.Null(store.Load().LastListingAddress);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        private static DisassemblySnapshot CreateSnapshot(byte[] memory, DateTime? capturedAtUtc = null)
        {
            return new DisassemblySnapshot(
                memory,
                programCounter: 0x8000,
                SpectrumMachineModel.Spectrum128K,
                currentRomBank: 1,
                pagedRamBank: 7,
                screenBank: 5,
                capturedAtUtc);
        }
    }
}
