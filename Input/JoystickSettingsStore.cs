using System.Text.Json;

namespace Spectrum128kEmulator
{
    public sealed class JoystickSettingsStore
    {
        private readonly string path;

        public JoystickSettingsStore(string? path = null)
        {
            this.path = path ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectrum128kEmulator",
                "joystick-settings.json");
        }

        public HostJoystickSettings Load()
        {
            try
            {
                if (!File.Exists(path))
                    return new HostJoystickSettings();
                return JsonSerializer.Deserialize<HostJoystickSettings>(File.ReadAllText(path)) ??
                    new HostJoystickSettings();
            }
            catch
            {
                return new HostJoystickSettings();
            }
        }

        public void Save(HostJoystickSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
