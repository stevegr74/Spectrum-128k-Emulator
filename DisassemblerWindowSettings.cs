using System.Text.Json;

namespace Spectrum128kEmulator
{
    public sealed class DisassemblerWindowSettings
    {
        public int Width { get; set; } = 980;
        public int Height { get; set; } = 680;
        public int[] ColumnWidths { get; set; } = [90, 170, 480, 100];
        public ushort? LastListingAddress { get; set; }
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
                settings.ColumnWidths ??= [90, 170, 480, 100];
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
