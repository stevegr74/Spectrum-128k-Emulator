using Spectrum128kEmulator.Input;

namespace Spectrum128kEmulator
{
    public partial class MainForm
    {
        private void InitializeJoystickMenu()
        {
            joystickMenuItem.DropDownOpening += (_, _) => RebuildJoystickMenu();
            RebuildJoystickMenu();
        }

        private void RebuildJoystickMenu()
        {
            joystickMenuItem.DropDownItems.Clear();
            HostJoystickSettings settings = GetJoystickSettingsSnapshot();
            IReadOnlyList<HostJoystickDevice> devices = joystickSource.GetDevices();

            var disabled = new ToolStripMenuItem("Off") { Checked = !settings.Enabled };
            disabled.Click += (_, _) => SelectJoystick(enabled: false, preferredDeviceId: null);
            joystickMenuItem.DropDownItems.Add(disabled);

            var automatic = new ToolStripMenuItem("Automatic")
            {
                Checked = settings.Enabled && settings.PreferredDeviceId == null
            };
            automatic.Click += (_, _) => SelectJoystick(enabled: true, preferredDeviceId: null);
            joystickMenuItem.DropDownItems.Add(automatic);
            joystickMenuItem.DropDownItems.Add(new ToolStripSeparator());

            foreach (HostJoystickDevice device in devices)
            {
                var item = new ToolStripMenuItem(device.DisplayName)
                {
                    Checked = settings.Enabled && settings.PreferredDeviceId == device.Id,
                    ToolTipText = $"{device.AxisCount} axes, {device.SwitchCount} switches, {device.ButtonCount} buttons"
                };
                string deviceId = device.Id;
                item.Click += (_, _) => SelectJoystick(enabled: true, preferredDeviceId: deviceId);
                joystickMenuItem.DropDownItems.Add(item);
            }

            if (devices.Count == 0)
                joystickMenuItem.DropDownItems.Add(new ToolStripMenuItem("No controllers detected") { Enabled = false });
            else if (settings.Enabled && settings.PreferredDeviceId != null && devices.All(device => device.Id != settings.PreferredDeviceId))
                joystickMenuItem.DropDownItems.Add(new ToolStripMenuItem("Selected controller unavailable - using automatic") { Enabled = false });

            joystickMenuItem.DropDownItems.Add(new ToolStripSeparator());
            joystickMenuItem.DropDownItems.Add(new ToolStripMenuItem($"Interface: {FormatJoystickInterface(settings.EffectiveInterface)}") { Enabled = false });
            var configure = new ToolStripMenuItem("Configure...");
            configure.Click += (_, _) => ConfigureJoystick();
            joystickMenuItem.DropDownItems.Add(configure);
        }

        private void SelectJoystick(bool enabled, string? preferredDeviceId)
        {
            HostJoystickSettings updated;
            lock (inputStateLock)
            {
                joystickSettings = joystickSettings with
                {
                    Enabled = enabled,
                    PreferredDeviceId = preferredDeviceId
                };
                pendingJoystickReset = true;
                updated = joystickSettings;
            }

            joystickSettingsStore.Save(updated);
            RebuildJoystickMenu();
            screenBox.Focus();
        }

        private void ConfigureJoystick()
        {
            using IDisposable pauseLease = AcquireEmulationPauseLease();
            HostJoystickSettings settings = GetJoystickSettingsSnapshot();
            IReadOnlyList<HostJoystickDevice> devices = joystickSource.GetDevices();
            HostJoystickDevice? selected = settings.PreferredDeviceId == null
                ? devices.FirstOrDefault()
                : devices.FirstOrDefault(device => device.Id == settings.PreferredDeviceId);
            using var dialog = new JoystickConfigurationForm(
                settings.EffectiveMapping,
                settings.EffectiveInterface,
                selected,
                joystickSource,
                settings.PreferredDeviceId);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            HostJoystickSettings updated;
            lock (inputStateLock)
            {
                joystickSettings = joystickSettings with
                {
                    Mapping = dialog.SelectedMapping,
                    Interface = dialog.SelectedInterface
                };
                pendingJoystickReset = true;
                updated = joystickSettings;
            }
            joystickSettingsStore.Save(updated);
            screenBox.Focus();
        }

        private HostJoystickSettings GetJoystickSettingsSnapshot()
        {
            lock (inputStateLock)
                return joystickSettings;
        }

        private void PollJoystick(Spectrum128Machine activeMachine)
        {
            HostJoystickSettings settings;
            bool reset;
            lock (inputStateLock)
            {
                settings = joystickSettings;
                reset = pendingJoystickReset;
                pendingJoystickReset = false;
            }

            if (reset)
                activeMachine.ClearJoystick();

            long now = frameClock.ElapsedTicks;
            if (!reset && now < nextJoystickPollTicks)
                return;
            nextJoystickPollTicks = now + (System.Diagnostics.Stopwatch.Frequency / 200);
            SpectrumJoystickInterface joystickInterface = settings.Enabled
                ? settings.EffectiveInterface
                : SpectrumJoystickInterface.None;
            activeMachine.SetJoystickState(joystickInterface, joystickInputController.Poll(settings));
        }

        private void ResetJoystickInputBridgeState()
        {
            lock (inputStateLock)
                pendingJoystickReset = true;
            nextJoystickPollTicks = 0;
        }

        private static string FormatJoystickInterface(SpectrumJoystickInterface joystickInterface) => joystickInterface switch
        {
            SpectrumJoystickInterface.Kempston => "Kempston",
            SpectrumJoystickInterface.Cursor => "Cursor (keys 5, 8, 6, 7, 0)",
            SpectrumJoystickInterface.Sinclair1 => "Interface II - Player 1 (keys 6-0)",
            SpectrumJoystickInterface.Sinclair2 => "Interface II - Player 2 (keys 1-5)",
            _ => "None"
        };
    }
}
