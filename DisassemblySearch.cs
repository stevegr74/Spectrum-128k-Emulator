using System.Globalization;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public enum DisassemblySearchMode
    {
        Address,
        Bytes,
        Mnemonic
    }

    public sealed record DisassemblySearchResult(ushort Address, string Description);

    public static class DisassemblySearch
    {
        public static bool TryFindNext(
            DisassemblySnapshot snapshot,
            DisassemblySearchMode mode,
            string? query,
            ushort startAddress,
            out DisassemblySearchResult? result,
            out string? error)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            result = null;
            error = null;

            if (mode == DisassemblySearchMode.Address)
            {
                if (!DisassemblyAddressParser.TryParse(query, out ushort address))
                {
                    error = "Enter a hexadecimal address from 0000 to FFFF.";
                    return false;
                }

                result = new DisassemblySearchResult(address, $"Address {address:X4}H");
                return true;
            }

            if (mode == DisassemblySearchMode.Bytes)
            {
                if (!TryParseBytes(query, out byte[] bytes))
                {
                    error = "Enter hexadecimal bytes such as 3E 10 or DD,21,00,80.";
                    return false;
                }

                for (int offset = 0; offset < 65536; offset++)
                {
                    ushort candidate = (ushort)(startAddress + offset);
                    bool matches = true;
                    for (int index = 0; index < bytes.Length; index++)
                    {
                        if (snapshot.ReadMemory((ushort)(candidate + index)) != bytes[index])
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        result = new DisassemblySearchResult(candidate, $"Bytes {string.Join(' ', bytes.Select(value => value.ToString("X2")))}");
                        return true;
                    }
                }

                error = "Byte sequence not found in this capture.";
                return false;
            }

            string mnemonicQuery = query?.Trim() ?? string.Empty;
            if (mnemonicQuery.Length == 0)
            {
                error = "Enter mnemonic text such as CALL, LD A, or (IX+.";
                return false;
            }

            for (int offset = 0; offset < 65536; offset++)
            {
                ushort candidate = (ushort)(startAddress + offset);
                Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(candidate, snapshot.ReadMemory);
                if (instruction.Mnemonic.Contains(mnemonicQuery, StringComparison.OrdinalIgnoreCase))
                {
                    result = new DisassemblySearchResult(candidate, $"Mnemonic {instruction.Mnemonic}");
                    return true;
                }
            }

            error = "Mnemonic text not found in this capture.";
            return false;
        }

        public static bool TryParseBytes(string? text, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim().Replace(',', ' ').Replace('-', ' ');
            string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Length == 1 && tokens[0].Length > 2 && (tokens[0].Length & 1) == 0)
            {
                string compact = tokens[0];
                tokens = Enumerable.Range(0, compact.Length / 2)
                    .Select(index => compact.Substring(index * 2, 2))
                    .ToArray();
            }

            var parsed = new byte[tokens.Length];
            for (int index = 0; index < tokens.Length; index++)
            {
                string token = tokens[index];
                if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    token = token[2..];
                if (token.Length is < 1 or > 2 ||
                    !byte.TryParse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed[index]))
                {
                    return false;
                }
            }

            bytes = parsed;
            return bytes.Length > 0;
        }
    }
}
