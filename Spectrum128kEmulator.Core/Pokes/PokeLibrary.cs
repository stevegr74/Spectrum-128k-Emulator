using System.Globalization;
using System.Text.Json;

namespace Spectrum128kEmulator.Pokes
{
    public sealed class PokeLibrary
    {
        public const int CurrentVersion = 1;

        public PokeLibrary(IReadOnlyList<PokeGameDefinition> games)
        {
            Games = games ?? throw new ArgumentNullException(nameof(games));
        }

        public IReadOnlyList<PokeGameDefinition> Games { get; }

        public static PokeLibrary Empty { get; } = new(Array.Empty<PokeGameDefinition>());

        public static PokeLibrary Load(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return Parse(File.ReadAllText(path));
        }

        public static PokeLibrary Parse(string json)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            LibraryDocument document;
            try
            {
                document = JsonSerializer.Deserialize<LibraryDocument>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("The poke library is empty.");
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"The poke library JSON is invalid: {exception.Message}", exception);
            }

            if (document.Version != CurrentVersion)
                throw new InvalidDataException($"Unsupported poke library version {document.Version}; expected {CurrentVersion}.");

            var games = new List<PokeGameDefinition>();
            var gameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var checksums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameDocument game in document.Games ?? [])
            {
                string id = Required(game.Id, "game id");
                string title = Required(game.Title, $"title for game '{id}'");
                if (!gameIds.Add(id))
                    throw new InvalidDataException($"Duplicate poke-library game id '{id}'.");

                string[] aliases = (game.Aliases ?? [])
                    .Select(alias => Required(alias, $"alias for game '{id}'"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                HashSet<SpectrumMachineModel> models = ParseModels(game.Models, id);
                var gameChecksums = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string rawChecksum in game.Sha256 ?? [])
                {
                    string checksum = Required(rawChecksum, $"SHA-256 for game '{id}'").ToUpperInvariant();
                    if (checksum.Length != 64 || !checksum.All(Uri.IsHexDigit))
                        throw new InvalidDataException($"Game '{id}' contains an invalid SHA-256 value.");
                    if (!gameChecksums.Add(checksum))
                        throw new InvalidDataException($"Game '{id}' contains duplicate SHA-256 value '{checksum}'.");
                    if (!checksums.Add(checksum))
                        throw new InvalidDataException($"SHA-256 value '{checksum}' is assigned to more than one game.");
                }

                var pokes = new List<PokeDefinition>();
                var pokeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (PokeDocument poke in game.Pokes ?? [])
                {
                    string pokeId = Required(poke.Id, $"poke id for game '{id}'");
                    string pokeName = Required(poke.Name, $"name for poke '{pokeId}'");
                    if (!pokeIds.Add(pokeId))
                        throw new InvalidDataException($"Game '{id}' contains duplicate poke id '{pokeId}'.");

                    PokeWrite[] writes = (poke.Writes ?? [])
                        .Select((write, index) => ParseWrite(write, id, pokeId, index))
                        .ToArray();
                    if (writes.Length == 0)
                        throw new InvalidDataException($"Poke '{id}/{pokeId}' has no writes.");
                    if (writes.Select(TargetKey).Distinct().Count() != writes.Length)
                        throw new InvalidDataException($"Poke '{id}/{pokeId}' writes the same target more than once.");

                    pokes.Add(new PokeDefinition(
                        pokeId,
                        pokeName,
                        poke.Description?.Trim() ?? string.Empty,
                        writes));
                }

                if (pokes.Count == 0)
                    throw new InvalidDataException($"Game '{id}' has no pokes.");

                games.Add(new PokeGameDefinition(id, title, aliases, models, gameChecksums, pokes));
            }

            return new PokeLibrary(games.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        public IReadOnlyList<PokeGameMatch> FindMatches(PokeMediaIdentity? media, SpectrumMachineModel model)
        {
            string mediaBaseName = media == null ? string.Empty : Path.GetFileNameWithoutExtension(media.FileName);
            return Games
                .Where(game => game.Supports(model))
                .Select(game => new PokeGameMatch(game, Match(game, media, mediaBaseName)))
                .OrderByDescending(match => match.Kind)
                .ThenBy(match => match.Game.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static PokeMatchKind Match(PokeGameDefinition game, PokeMediaIdentity? media, string mediaBaseName)
        {
            if (media != null && game.Sha256.Contains(media.Sha256))
                return PokeMatchKind.ExactChecksum;
            if (!string.IsNullOrWhiteSpace(mediaBaseName) &&
                (string.Equals(game.Title, mediaBaseName, StringComparison.OrdinalIgnoreCase) ||
                 game.Aliases.Any(alias => mediaBaseName.Contains(alias, StringComparison.OrdinalIgnoreCase))))
            {
                return PokeMatchKind.Alias;
            }

            return PokeMatchKind.None;
        }

        private static HashSet<SpectrumMachineModel> ParseModels(List<string>? values, string gameId)
        {
            if (values == null || values.Count == 0)
                return [SpectrumMachineModel.Spectrum48K, SpectrumMachineModel.Spectrum128K];

            var models = new HashSet<SpectrumMachineModel>();
            foreach (string raw in values)
            {
                string value = Required(raw, $"model for game '{gameId}'");
                SpectrumMachineModel model = value.ToUpperInvariant() switch
                {
                    "48K" => SpectrumMachineModel.Spectrum48K,
                    "128K" => SpectrumMachineModel.Spectrum128K,
                    _ => throw new InvalidDataException($"Game '{gameId}' contains unsupported model '{value}'.")
                };
                if (!models.Add(model))
                    throw new InvalidDataException($"Game '{gameId}' contains duplicate model '{value}'.");
            }

            return models;
        }

        private static PokeWrite ParseWrite(WriteDocument write, string gameId, string pokeId, int index)
        {
            string context = $"write {index + 1} in '{gameId}/{pokeId}'";
            string space = Required(write.Space, $"space for {context}");
            byte value = checked((byte)ParseNumber(write.Value, 0xFF, $"value for {context}"));
            if (space.Equals("mapped", StringComparison.OrdinalIgnoreCase))
            {
                ushort address = checked((ushort)ParseNumber(write.Address, 0xFFFF, $"address for {context}"));
                if (address < 0x4000)
                    throw new InvalidDataException($"Mapped {context} targets ROM at {address:X4}H.");
                if (write.Bank != null || write.Offset != null)
                    throw new InvalidDataException($"Mapped {context} must not specify bank or offset.");
                return PokeWrite.Mapped(address, value);
            }

            if (space.Equals("ramBank", StringComparison.OrdinalIgnoreCase))
            {
                if (!write.Bank.HasValue || write.Bank.Value is < 0 or > 7)
                    throw new InvalidDataException($"RAM-bank {context} requires bank 0-7.");
                int bank = write.Bank.Value;
                ushort offset = checked((ushort)ParseNumber(write.Offset, 0x3FFF, $"offset for {context}"));
                if (write.Address != null)
                    throw new InvalidDataException($"RAM-bank {context} must not specify a mapped address.");
                return PokeWrite.RamBank(bank, offset, value);
            }

            throw new InvalidDataException($"{context} has unsupported space '{space}'.");
        }

        public static int ParseNumber(string? raw, int maximum, string fieldName)
        {
            string value = Required(raw, fieldName);
            NumberStyles style = NumberStyles.Integer;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value[2..];
                style = NumberStyles.AllowHexSpecifier;
            }
            else if (value.StartsWith('$'))
            {
                value = value[1..];
                style = NumberStyles.AllowHexSpecifier;
            }
            else if (value.EndsWith("H", StringComparison.OrdinalIgnoreCase))
            {
                value = value[..^1];
                style = NumberStyles.AllowHexSpecifier;
            }

            if (!int.TryParse(value, style, CultureInfo.InvariantCulture, out int result) || result < 0 || result > maximum)
                throw new InvalidDataException($"Invalid {fieldName} '{raw}'.");
            return result;
        }

        private static string Required(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Missing {fieldName}.");
            return value.Trim();
        }

        private static string TargetKey(PokeWrite write) => $"{write.Space}:{write.Bank}:{write.Address}";

        private sealed class LibraryDocument
        {
            public int Version { get; set; }
            public List<GameDocument>? Games { get; set; }
        }

        private sealed class GameDocument
        {
            public string? Id { get; set; }
            public string? Title { get; set; }
            public List<string>? Aliases { get; set; }
            public List<string>? Models { get; set; }
            public List<string>? Sha256 { get; set; }
            public List<PokeDocument>? Pokes { get; set; }
        }

        private sealed class PokeDocument
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public string? Description { get; set; }
            public List<WriteDocument>? Writes { get; set; }
        }

        private sealed class WriteDocument
        {
            public string? Space { get; set; }
            public string? Address { get; set; }
            public int? Bank { get; set; }
            public string? Offset { get; set; }
            public string? Value { get; set; }
        }
    }
}
