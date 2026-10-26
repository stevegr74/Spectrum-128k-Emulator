using Spectrum128kEmulator.Pokes;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class PokeManagerTests
    {
        private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        [Fact]
        public void Library_ParsesValidatedMappedAndBankedWrites()
        {
            PokeLibrary library = PokeLibrary.Parse(LibraryJson(
                """
                {
                  "id": "infinite-lives",
                  "name": "Infinite lives",
                  "description": "Keeps the counter full.",
                  "writes": [
                    { "space": "mapped", "address": "0x8000", "value": "255" },
                    { "space": "ramBank", "bank": 7, "offset": "$0123", "value": "2AH" }
                  ]
                }
                """));

            PokeGameDefinition game = Assert.Single(library.Games);
            Assert.Equal("Example Game", game.Title);
            Assert.Equal([SpectrumMachineModel.Spectrum48K, SpectrumMachineModel.Spectrum128K], game.Models.Order().ToArray());
            Assert.Contains(HashA, game.Sha256);
            PokeDefinition poke = Assert.Single(game.Pokes);
            Assert.Equal(PokeWrite.Mapped(0x8000, 255), poke.Writes[0]);
            Assert.Equal(PokeWrite.RamBank(7, 0x0123, 0x2A), poke.Writes[1]);
        }

        [Fact]
        public void Library_RejectsDuplicateTargetsAndInvalidChecksums()
        {
            string duplicateTargets = LibraryJson(
                """
                {
                  "id": "bad",
                  "name": "Bad",
                  "writes": [
                    { "space": "mapped", "address": "8000H", "value": "1" },
                    { "space": "mapped", "address": "0x8000", "value": "2" }
                  ]
                }
                """);
            Assert.Throws<InvalidDataException>(() => PokeLibrary.Parse(duplicateTargets));

            string invalidHash = LibraryJson(
                """
                {
                  "id": "valid",
                  "name": "Valid",
                  "writes": [
                    { "space": "mapped", "address": "0x8000", "value": "1" }
                  ]
                }
                """).Replace(HashA, "not-a-hash", StringComparison.Ordinal);
            Assert.Throws<InvalidDataException>(() => PokeLibrary.Parse(invalidHash));
        }

        [Fact]
        public void Library_MatchesExactChecksumBeforeFilenameAlias()
        {
            PokeLibrary library = PokeLibrary.Parse(LibraryJson(
                """
                {
                  "id": "test",
                  "name": "Test",
                  "writes": [
                    { "space": "mapped", "address": "0x8000", "value": "1" }
                  ]
                }
                """));

            PokeGameMatch exact = Assert.Single(library.FindMatches(
                new PokeMediaIdentity("unrelated.tzx", HashA),
                SpectrumMachineModel.Spectrum128K));
            Assert.Equal(PokeMatchKind.ExactChecksum, exact.Kind);

            PokeGameMatch alias = Assert.Single(library.FindMatches(
                new PokeMediaIdentity("Example Game (Release 2).tzx", new string('B', 64)),
                SpectrumMachineModel.Spectrum128K));
            Assert.Equal(PokeMatchKind.Alias, alias.Kind);
        }

        [Fact]
        public void Application_AppliesMappedAndPhysicalWritesAndRestoresOriginals()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.PokeMemory(0x8000, 0x11);
                machine.PokeRamBank(7, 0x0123, 0x22);
                PokeWrite[] writes =
                [
                    PokeWrite.Mapped(0x8000, 0xAA),
                    PokeWrite.RamBank(7, 0x0123, 0xBB)
                ];

                IReadOnlyList<PokeWritePreview> preview = PokeApplicationService.Preview(machine, writes);
                Assert.Equal([0x11, 0x22], preview.Select(item => (int)item.CurrentValue).ToArray());

                PokeApplication application = PokeApplicationService.Apply(machine, writes);
                Assert.Equal(0xAA, machine.PeekMemory(0x8000));
                Assert.Equal(0xBB, machine.PeekRamBank(7, 0x0123));
                Assert.True(PokeApplicationService.TryRestore(machine, application, out string error), error);
                Assert.Equal(0x11, machine.PeekMemory(0x8000));
                Assert.Equal(0x22, machine.PeekRamBank(7, 0x0123));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Application_RefusesRestoreAfterGameChangesAppliedByte()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.PokeMemory(0x8000, 0x11);
                PokeApplication application = PokeApplicationService.Apply(
                    machine,
                    [PokeWrite.Mapped(0x8000, 0xAA)]);
                machine.PokeMemory(0x8000, 0x44);

                Assert.False(PokeApplicationService.TryRestore(machine, application, out string error));
                Assert.Contains("no longer matches", error);
                Assert.Equal(0x44, machine.PeekMemory(0x8000));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Application_RejectsRomDuplicateAndUnavailable48kBankWrites()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    PokeApplicationService.Apply(machine, [PokeWrite.Mapped(0x3FFF, 1)]));
                Assert.Throws<ArgumentException>(() =>
                    PokeApplicationService.Apply(machine, [PokeWrite.Mapped(0x8000, 1), PokeWrite.Mapped(0x8000, 2)]));

                machine.Reset(SpectrumMachineModel.Spectrum48K);
                Assert.Throws<InvalidOperationException>(() =>
                    PokeApplicationService.Apply(machine, [PokeWrite.RamBank(7, 0, 1)]));
                PokeApplicationService.Apply(machine, [PokeWrite.RamBank(5, 0, 1)]);
                Assert.Equal(1, machine.PeekRamBank(5, 0));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static string LibraryJson(string poke) =>
            $$"""
            {
              "version": 1,
              "games": [
                {
                  "id": "example-game",
                  "title": "Example Game",
                  "aliases": ["Example"],
                  "models": ["48K", "128K"],
                  "sha256": ["{{HashA}}"],
                  "pokes": [{{poke}}]
                }
              ]
            }
            """;

        private static string CreateTempRoms()
        {
            string root = Path.Combine(Path.GetTempPath(), "PokeManagerTests_" + Guid.NewGuid().ToString("N"));
            string roms = Path.Combine(root, "ROMs");
            Directory.CreateDirectory(roms);
            File.WriteAllBytes(Path.Combine(roms, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(roms, "128-1.rom"), new byte[16384]);
            return roms;
        }
    }
}
