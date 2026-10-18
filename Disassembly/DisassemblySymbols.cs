using System.Globalization;
using System.Text;

namespace Spectrum128kEmulator
{
    public readonly record struct DisassemblySymbolKey(
        DisassemblyAddressSpace AddressSpace,
        ushort Address);

    public sealed record DisassemblySymbol(
        DisassemblyAddressSpace AddressSpace,
        ushort Address,
        string Name,
        bool IsAutomatic)
    {
        public DisassemblySymbolKey Key => new(AddressSpace, Address);
    }

    public sealed class DisassemblySymbolTable
    {
        private readonly Dictionary<DisassemblySymbolKey, DisassemblySymbol> symbols = new();
        private readonly Dictionary<string, DisassemblySymbolKey> names = new(StringComparer.OrdinalIgnoreCase);

        public DisassemblySymbolTable(IEnumerable<DisassemblySymbol>? initialSymbols = null)
        {
            if (initialSymbols == null)
                return;

            foreach (DisassemblySymbol symbol in initialSymbols)
                TrySet(symbol.AddressSpace, symbol.Address, symbol.Name, symbol.IsAutomatic, out _);
        }

        public IReadOnlyList<DisassemblySymbol> Symbols => symbols.Values
            .OrderBy(symbol => symbol.AddressSpace.Kind)
            .ThenBy(symbol => symbol.AddressSpace.Bank)
            .ThenBy(symbol => symbol.Address)
            .ToArray();

        public string? GetName(DisassemblyAddressSpace addressSpace, ushort address) =>
            symbols.TryGetValue(new DisassemblySymbolKey(addressSpace, address), out DisassemblySymbol? symbol)
                ? symbol.Name
                : null;

        public bool TryFind(string name, out DisassemblySymbol? symbol)
        {
            symbol = null;
            if (string.IsNullOrWhiteSpace(name) || !names.TryGetValue(name.Trim(), out DisassemblySymbolKey key))
                return false;
            symbol = symbols[key];
            return true;
        }

        public bool TrySet(
            DisassemblyAddressSpace addressSpace,
            ushort address,
            string? name,
            bool isAutomatic,
            out string? error)
        {
            error = Validate(addressSpace, address, name);
            if (error != null)
                return false;

            string normalizedName = name!.Trim();
            var key = new DisassemblySymbolKey(addressSpace, address);
            if (names.TryGetValue(normalizedName, out DisassemblySymbolKey existingKey) && existingKey != key)
            {
                error = $"The label '{normalizedName}' is already assigned to {FormatKey(existingKey)}.";
                return false;
            }

            if (symbols.TryGetValue(key, out DisassemblySymbol? existing))
                names.Remove(existing.Name);

            var symbol = new DisassemblySymbol(addressSpace, address, normalizedName, isAutomatic);
            symbols[key] = symbol;
            names[normalizedName] = key;
            return true;
        }

        public DisassemblySymbol EnsureAutomatic(
            DisassemblyAddressSpace addressSpace,
            ushort address,
            bool isCall)
        {
            var key = new DisassemblySymbolKey(addressSpace, address);
            if (symbols.TryGetValue(key, out DisassemblySymbol? existing))
                return existing;

            string prefix = isCall ? "SUB" : "LOC";
            string spacePrefix = addressSpace.Kind switch
            {
                DisassemblyAddressSpaceKind.Rom => $"ROM{addressSpace.Bank}_",
                DisassemblyAddressSpaceKind.Ram => $"RAM{addressSpace.Bank}_",
                _ => string.Empty
            };
            string baseName = $"{spacePrefix}{prefix}_{address:X4}";
            string name = baseName;
            int suffix = 2;
            while (names.ContainsKey(name))
                name = $"{baseName}_{suffix++}";

            TrySet(addressSpace, address, name, isAutomatic: true, out _);
            return symbols[key];
        }

        public bool Remove(DisassemblyAddressSpace addressSpace, ushort address)
        {
            var key = new DisassemblySymbolKey(addressSpace, address);
            if (!symbols.Remove(key, out DisassemblySymbol? symbol))
                return false;
            names.Remove(symbol.Name);
            return true;
        }

        public bool TryMerge(IEnumerable<DisassemblySymbol> imported, out string? error)
        {
            ArgumentNullException.ThrowIfNull(imported);
            var candidate = new DisassemblySymbolTable(Symbols);
            foreach (DisassemblySymbol symbol in imported)
            {
                if (!candidate.TrySet(symbol.AddressSpace, symbol.Address, symbol.Name, symbol.IsAutomatic, out error))
                    return false;
            }

            symbols.Clear();
            names.Clear();
            foreach (DisassemblySymbol symbol in candidate.Symbols)
                TrySet(symbol.AddressSpace, symbol.Address, symbol.Name, symbol.IsAutomatic, out _);
            error = null;
            return true;
        }

        public static string? Validate(
            DisassemblyAddressSpace addressSpace,
            ushort address,
            string? name)
        {
            bool validAddressSpace = addressSpace.Kind switch
            {
                DisassemblyAddressSpaceKind.Mapped => addressSpace.Bank == 0,
                DisassemblyAddressSpaceKind.Rom => addressSpace.Bank is >= 0 and < 2,
                DisassemblyAddressSpaceKind.Ram => addressSpace.Bank is >= 0 and < 8,
                _ => false
            };
            if (!validAddressSpace)
                return "The symbol has an invalid memory-bank identity.";
            if (address >= addressSpace.Length)
                return $"The address is outside {addressSpace.DisplayName}.";
            if (string.IsNullOrWhiteSpace(name))
                return "Enter a label name.";

            string value = name.Trim();
            if (!(char.IsLetter(value[0]) || value[0] == '_') ||
                value.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
            {
                return "Labels must start with a letter or underscore and contain only letters, digits, or underscores.";
            }
            return null;
        }

        private static string FormatKey(DisassemblySymbolKey key) =>
            $"{DisassemblySymbolFile.FormatAddressSpace(key.AddressSpace)}:{key.Address:X4}";
    }

    public static class DisassemblySymbolFile
    {
        public static string Format(IEnumerable<DisassemblySymbol> symbols)
        {
            ArgumentNullException.ThrowIfNull(symbols);
            var builder = new StringBuilder();
            builder.AppendLine("; Spectrum 128K Emulator debugger symbols");
            builder.AppendLine("; Format: MAP|ROMn|RAMn:HHHH LabelName");
            foreach (DisassemblySymbol symbol in symbols
                .OrderBy(value => value.AddressSpace.Kind)
                .ThenBy(value => value.AddressSpace.Bank)
                .ThenBy(value => value.Address))
            {
                builder.AppendLine($"{FormatAddressSpace(symbol.AddressSpace)}:{symbol.Address:X4} {symbol.Name}");
            }
            return builder.ToString().TrimEnd();
        }

        public static bool TryParse(string? text, out IReadOnlyList<DisassemblySymbol> symbols, out string? error)
        {
            var parsed = new DisassemblySymbolTable();
            var locations = new HashSet<DisassemblySymbolKey>();
            string[] lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    continue;

                int separator = line.IndexOfAny([' ', '\t']);
                if (separator < 0)
                {
                    symbols = Array.Empty<DisassemblySymbol>();
                    error = $"Line {index + 1}: expected an address and label name.";
                    return false;
                }

                string location = line[..separator].Trim();
                string name = line[(separator + 1)..].Trim();
                int colon = location.IndexOf(':');
                if (colon < 1 || !TryParseAddressSpace(location[..colon], out DisassemblyAddressSpace addressSpace) ||
                    !ushort.TryParse(location[(colon + 1)..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort address))
                {
                    symbols = Array.Empty<DisassemblySymbol>();
                    error = $"Line {index + 1}: invalid bank-qualified hexadecimal address.";
                    return false;
                }

                if (!parsed.TrySet(addressSpace, address, name, isAutomatic: false, out string? symbolError))
                {
                    symbols = Array.Empty<DisassemblySymbol>();
                    error = $"Line {index + 1}: {symbolError}";
                    return false;
                }
                var key = new DisassemblySymbolKey(addressSpace, address);
                if (!locations.Add(key))
                {
                    symbols = Array.Empty<DisassemblySymbol>();
                    error = $"Line {index + 1}: the location is already defined in this file.";
                    return false;
                }
            }

            symbols = parsed.Symbols;
            error = null;
            return true;
        }

        public static string FormatAddressSpace(DisassemblyAddressSpace addressSpace) => addressSpace.Kind switch
        {
            DisassemblyAddressSpaceKind.Rom => $"ROM{addressSpace.Bank}",
            DisassemblyAddressSpaceKind.Ram => $"RAM{addressSpace.Bank}",
            _ => "MAP"
        };

        private static bool TryParseAddressSpace(string text, out DisassemblyAddressSpace addressSpace)
        {
            addressSpace = DisassemblyAddressSpace.Mapped;
            if (text.Equals("MAP", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text.StartsWith("ROM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(text[3..], out int romBank) && romBank is >= 0 and < 2)
            {
                addressSpace = DisassemblyAddressSpace.Rom(romBank);
                return true;
            }
            if (text.StartsWith("RAM", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(text[3..], out int ramBank) && ramBank is >= 0 and < 8)
            {
                addressSpace = DisassemblyAddressSpace.Ram(ramBank);
                return true;
            }
            return false;
        }
    }
}
