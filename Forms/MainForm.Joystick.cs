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
            polledJoystickDevices = devices;
            nextJoystickDeviceRefreshTicks = frameClock.ElapsedTicks + System.Diagnostics.Stopwatch.Frequency;

            AddControllerMenu("Controller 1", 1, settings.Controller1Settings, devices);
            AddControllerMenu("Controller 2", 2, settings.Controller2Settings, devices);
            joystickMenuItem.DropDownItems.Add(new ToolStripSeparator());
            var configure = new ToolStripMenuItem("Configure both controllers...");
            configure.Click += (_, _) => ConfigureJoystick();
            joystickMenuItem.DropDownItems.Add(configure);
        }

        private void AddControllerMenu(
            string label,
            int controllerNumber,
            HostJoystickChannelSettings channel,
            IReadOnlyList<HostJoystickDevice> devices)
        {
            var controllerMenu = new ToolStripMenuItem(label);
            var disabled = new ToolStripMenuItem("Off") { Checked = !channel.Enabled };
            disabled.Click += (_, _) => SelectJoystick(controllerNumber, enabled: false, preferredDeviceId: null);
            controllerMenu.DropDownItems.Add(disabled);

            var automatic = new ToolStripMenuItem("Automatic")
            {
                Checked = channel.Enabled && channel.PreferredDeviceId == null
            };
            automatic.Click += (_, _) => SelectJoystick(controllerNumber, enabled: true, preferredDeviceId: null);
            controllerMenu.DropDownItems.Add(automatic);
            controllerMenu.DropDownItems.Add(new ToolStripSeparator());

            foreach (HostJoystickDevice device in devices)
            {
                var item = new ToolStripMenuItem(device.DisplayName)
                {
                    Checked = channel.Enabled && DeviceMatchesPreference(device, channel.PreferredDeviceId),
                    ToolTipText = $"{device.AxisCount} axes, {device.SwitchCount} switches, {device.ButtonCount} buttons"
                };
                string deviceId = device.Id;
                item.Click += (_, _) => SelectJoystick(controllerNumber, enabled: true, preferredDeviceId: deviceId);
                controllerMenu.DropDownItems.Add(item);
            }

            if (devices.Count == 0)
                controllerMenu.DropDownItems.Add(new ToolStripMenuItem("No controllers detected") { Enabled = false });
            else if (channel.Enabled && channel.PreferredDeviceId != null &&
                     devices.All(device => !DeviceMatchesPreference(device, channel.PreferredDeviceId)))
                controllerMenu.DropDownItems.Add(new ToolStripMenuItem("Selected controller unavailable") { Enabled = false });

            controllerMenu.DropDownItems.Add(new ToolStripSeparator());
            controllerMenu.DropDownItems.Add(
                new ToolStripMenuItem($"Interface: {FormatJoystickInterface(channel.EffectiveInterface)}") { Enabled = false });
            joystickMenuItem.DropDownItems.Add(controllerMenu);
        }

        private void SelectJoystick(int controllerNumber, bool enabled, string? preferredDeviceId)
        {
            HostJoystickSettings current = GetJoystickSettingsSnapshot();
            HostJoystickChannelSettings channel = GetControllerSettings(current, controllerNumber) with
            {
                Enabled = enabled,
                PreferredDeviceId = preferredDeviceId
            };
            HostJoystickChannelSettings other = GetControllerSettings(current, controllerNumber == 1 ? 2 : 1);
            if (channel.Enabled && other.Enabled && channel.EffectiveInterface == other.EffectiveInterface)
            {
                MessageBox.Show(
                    this,
                    "Controller 1 and Controller 2 must use different Spectrum interfaces. Use Configure both controllers to choose the mappings.",
                    "Joystick Configuration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (channel.Enabled && other.Enabled &&
                PreferencesClaimSameDevice(channel.PreferredDeviceId, other.PreferredDeviceId, polledJoystickDevices))
            {
                MessageBox.Show(
                    this,
                    "The same USB controller cannot be assigned to both controller slots.",
                    "Joystick Configuration",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            HostJoystickSettings updated;
            lock (inputStateLock)
            {
                joystickSettings = SetControllerSettings(joystickSettings, controllerNumber, channel);
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
            using var dialog = new JoystickConfigurationForm(
                settings.Controller1Settings,
                settings.Controller2Settings,
                devices,
                joystickSource);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            HostJoystickSettings updated;
            lock (inputStateLock)
            {
                joystickSettings = settings
                    .WithController1(dialog.Controller1Settings) with
                    {
                        Controller2 = dialog.Controller2Settings
                    };
                pendingJoystickReset = true;
                updated = joystickSettings;
            }

            joystickSettingsStore.Save(updated);
            RebuildJoystickMenu();
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

            long now = frameClock.ElapsedTicks;
            if (!reset && now < nextJoystickPollTicks)
                return;
            nextJoystickPollTicks = now + (System.Diagnostics.Stopwatch.Frequency / 200);

            if (reset || now >= nextJoystickDeviceRefreshTicks)
            {
                polledJoystickDevices = joystickSource.GetDevices();
                nextJoystickDeviceRefreshTicks = now + System.Diagnostics.Stopwatch.Frequency;
            }
            IReadOnlyList<HostJoystickDevice> devices = polledJoystickDevices;
            var claimedDeviceIds = new HashSet<string>(StringComparer.Ordinal);
            var states = new Dictionary<SpectrumJoystickInterface, SpectrumJoystickState>();
            PollController(settings.Controller1Settings, devices, claimedDeviceIds, states);
            PollController(settings.Controller2Settings, devices, claimedDeviceIds, states);

            activeMachine.ClearJoystick();
            foreach ((SpectrumJoystickInterface joystickInterface, SpectrumJoystickState state) in states)
                activeMachine.SetJoystickState(joystickInterface, state);
        }

        private void PollController(
            HostJoystickChannelSettings channel,
            IReadOnlyList<HostJoystickDevice> devices,
            HashSet<string> claimedDeviceIds,
            Dictionary<SpectrumJoystickInterface, SpectrumJoystickState> states)
        {
            if (!channel.Enabled)
                return;

            string? deviceId = ResolveDeviceId(channel, devices, claimedDeviceIds);
            if (deviceId == null)
                return;

            claimedDeviceIds.Add(deviceId);
            SpectrumJoystickInterface joystickInterface = channel.EffectiveInterface;
            SpectrumJoystickState state = joystickInputController.Poll(channel, deviceId);
            if (joystickInterface != SpectrumJoystickInterface.None)
                states[joystickInterface] = states.GetValueOrDefault(joystickInterface) | state;
        }

        private static string? ResolveDeviceId(
            HostJoystickChannelSettings channel,
            IReadOnlyList<HostJoystickDevice> devices,
            HashSet<string> claimedDeviceIds)
        {
            if (channel.PreferredDeviceId != null)
            {
                HostJoystickDevice? preferred = devices.FirstOrDefault(device =>
                    DeviceMatchesPreference(device, channel.PreferredDeviceId));
                return preferred != null && !claimedDeviceIds.Contains(preferred.Id)
                    ? preferred.Id
                    : null;
            }

            return devices.FirstOrDefault(device => !claimedDeviceIds.Contains(device.Id))?.Id;
        }

        private static bool DeviceMatchesPreference(HostJoystickDevice device, string? preferredDeviceId) =>
            preferredDeviceId != null &&
            (string.Equals(device.Id, preferredDeviceId, StringComparison.Ordinal) ||
             string.Equals(device.LegacyId, preferredDeviceId, StringComparison.Ordinal));

        private static bool PreferencesClaimSameDevice(
            string? firstPreference,
            string? secondPreference,
            IReadOnlyList<HostJoystickDevice> devices)
        {
            if (firstPreference == null || secondPreference == null)
                return false;
            if (string.Equals(firstPreference, secondPreference, StringComparison.Ordinal))
                return true;

            HostJoystickDevice? first = devices.FirstOrDefault(device =>
                DeviceMatchesPreference(device, firstPreference));
            HostJoystickDevice? second = devices.FirstOrDefault(device =>
                DeviceMatchesPreference(device, secondPreference));
            return first != null && second != null && first.Id == second.Id;
        }

        private void ResetJoystickInputBridgeState()
        {
            lock (inputStateLock)
                pendingJoystickReset = true;
            nextJoystickPollTicks = 0;
            nextJoystickDeviceRefreshTicks = 0;
        }

        private static HostJoystickChannelSettings GetControllerSettings(
            HostJoystickSettings settings,
            int controllerNumber) =>
            controllerNumber == 1 ? settings.Controller1Settings : settings.Controller2Settings;

        private static HostJoystickSettings SetControllerSettings(
            HostJoystickSettings settings,
            int controllerNumber,
            HostJoystickChannelSettings channel) =>
            controllerNumber == 1
                ? settings.WithController1(channel)
                : settings with { Controller2 = channel };

        private static string FormatJoystickInterface(SpectrumJoystickInterface joystickInterface) => joystickInterface switch
        {
            SpectrumJoystickInterface.Kempston => "Kempston",
            SpectrumJoystickInterface.Cursor => "Cursor (keys 5, 8, 6, 7, 0)",
            SpectrumJoystickInterface.SinclairKeys1To5 => "Sinclair 1 (keys 1-5; Interface II socket 2)",
            SpectrumJoystickInterface.SinclairKeys6To0 => "Sinclair 2 (keys 6-0; Interface II socket 1)",
            _ => "None"
        };
    }
}
