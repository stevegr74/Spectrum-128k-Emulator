using System.Security.Cryptography;

namespace Spectrum128kEmulator.Pokes
{
    public enum PokeMemorySpace
    {
        Mapped,
        RamBank
    }

    public readonly record struct PokeWrite(
        PokeMemorySpace Space,
        ushort Address,
        byte Value,
        int Bank = -1)
    {
        public static PokeWrite Mapped(ushort address, byte value) =>
            new(PokeMemorySpace.Mapped, address, value);

        public static PokeWrite RamBank(int bank, ushort offset, byte value) =>
            new(PokeMemorySpace.RamBank, offset, value, bank);

        public string Target => Space == PokeMemorySpace.Mapped
            ? $"Mapped {Address:X4}H"
            : $"RAM {Bank}:{Address:X4}H";
    }

    public sealed record PokeDefinition(
        string Id,
        string Name,
        string Description,
        IReadOnlyList<PokeWrite> Writes);

    public sealed record PokeGameDefinition(
        string Id,
        string Title,
        IReadOnlyList<string> Aliases,
        IReadOnlySet<SpectrumMachineModel> Models,
        IReadOnlySet<string> Sha256,
        IReadOnlyList<PokeDefinition> Pokes)
    {
        public bool Supports(SpectrumMachineModel model) => Models.Contains(model);
    }

    public sealed record PokeMediaIdentity(string FileName, string Sha256)
    {
        public static PokeMediaIdentity FromFile(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            using FileStream stream = File.OpenRead(path);
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            return new PokeMediaIdentity(Path.GetFileName(path), hash);
        }
    }

    public enum PokeMatchKind
    {
        None,
        Alias,
        ExactChecksum
    }

    public sealed record PokeGameMatch(PokeGameDefinition Game, PokeMatchKind Kind);

    public readonly record struct PokeWritePreview(PokeWrite Write, byte CurrentValue)
    {
        public bool ChangesValue => CurrentValue != Write.Value;
    }

    public sealed record AppliedPokeWrite(PokeWrite Write, byte OriginalValue);

    public sealed record PokeApplication(
        DateTimeOffset AppliedAt,
        IReadOnlyList<AppliedPokeWrite> Writes);
}
