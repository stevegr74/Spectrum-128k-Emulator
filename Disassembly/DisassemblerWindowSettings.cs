using System.Text.Json;

namespace Spectrum128kEmulator
{
    public sealed class DisassemblerBreakpointSetting
    {
        public ushort Address { get; set; }
        public bool Enabled { get; set; } = true;
    }

    public sealed class DisassemblerSymbolSetting
    {
        public DisassemblyAddressSpaceKind AddressSpaceKind { get; set; }
        public int Bank { get; set; }
        public ushort Address { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsAutomatic { get; set; }
    }

    public sealed class DisassemblerClassificationSetting
    {
        public string Context { get; set; } = "default";
        public DisassemblyAddressSpaceKind AddressSpaceKind { get; set; }
        public int Bank { get; set; }
        public ushort StartAddress { get; set; }
        public ushort EndAddress { get; set; }
        public DisassemblyClassification Classification { get; set; }
    }

    public sealed class DisassemblerWindowSettings
    {
        public int Width { get; set; } = 980;
        public int Height { get; set; } = 680;
        public int[] ColumnWidths { get; set; } = [108, 52, 125, 120, 340, 90];
        public ushort? LastListingAddress { get; set; }
        public DisassemblyAddressSpaceKind LastAddressSpaceKind { get; set; }
        public int LastAddressSpaceBank { get; set; }
        public List<DisassemblerBreakpointSetting> Breakpoints { get; set; } = [];
        public List<DisassemblerSymbolSetting> Symbols { get; set; } = [];
        public List<DisassemblerClassificationSetting> Classifications { get; set; } = [];
    }

    public sealed class DisassemblerWindowSettingsStore
    {
        private readonly string path;

        public DisassemblerWindowSettingsStore(string? path = null)
        {
            this.path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectrum128kEmulator",
                "disassembler-settings.json");
        }

        public DisassemblerWindowSettings Load()
        {
            try
            {
                if (!File.Exists(path))
                    return new DisassemblerWindowSettings();

                DisassemblerWindowSettings settings =
                    JsonSerializer.Deserialize<DisassemblerWindowSettings>(File.ReadAllText(path))
                    ?? new DisassemblerWindowSettings();
                settings.ColumnWidths ??= [108, 52, 125, 120, 340, 90];
                settings.Breakpoints ??= [];
                settings.Symbols ??= [];
                settings.Classifications ??= [];
                return settings;
            }
            catch
            {
                return new DisassemblerWindowSettings();
            }
        }

        public void Save(DisassemblerWindowSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
