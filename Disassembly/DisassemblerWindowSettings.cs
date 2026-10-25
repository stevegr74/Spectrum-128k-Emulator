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
        public const int CurrentLayoutVersion = 1;

        public int LayoutVersion { get; set; }
        public int Width { get; set; } = 1160;
        public int Height { get; set; } = 680;
        public int[] ColumnWidths { get; set; } = [108, 52, 125, 120, 240, 90];
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
                    return CreateDefaults();

                DisassemblerWindowSettings settings =
                    JsonSerializer.Deserialize<DisassemblerWindowSettings>(File.ReadAllText(path))
                    ?? CreateDefaults();
                settings.ColumnWidths ??= [108, 52, 125, 120, 240, 90];
                if (settings.LayoutVersion < DisassemblerWindowSettings.CurrentLayoutVersion)
                {
                    settings.Width = Math.Min(settings.Width, 1160);
                    if (settings.ColumnWidths.SequenceEqual([108, 52, 125, 120, 340, 90]))
                        settings.ColumnWidths = [108, 52, 125, 120, 240, 90];
                    settings.LayoutVersion = DisassemblerWindowSettings.CurrentLayoutVersion;
                }
                settings.Breakpoints ??= [];
                settings.Symbols ??= [];
                settings.Classifications ??= [];
                return settings;
            }
            catch
            {
                return CreateDefaults();
            }
        }

        public void Save(DisassemblerWindowSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.LayoutVersion = DisassemblerWindowSettings.CurrentLayoutVersion;
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static DisassemblerWindowSettings CreateDefaults() => new()
        {
            LayoutVersion = DisassemblerWindowSettings.CurrentLayoutVersion
        };
    }
}
