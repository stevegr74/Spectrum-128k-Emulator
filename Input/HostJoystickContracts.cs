using Spectrum128kEmulator.Input;

namespace Spectrum128kEmulator
{
    public enum HostJoystickHatPosition
    {
        Centered,
        Up,
        UpRight,
        Right,
        DownRight,
        Down,
        DownLeft,
        Left,
        UpLeft
    }

    public sealed record HostJoystickDevice(
        string Id,
        string DisplayName,
        int AxisCount,
        int ButtonCount,
        int SwitchCount,
        string? LegacyId = null);

    public sealed class HostJoystickReading
    {
        public HostJoystickReading(
            string deviceId,
            IEnumerable<bool> buttons,
            IEnumerable<HostJoystickHatPosition> switches,
            IEnumerable<double> axes)
        {
            DeviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));
            Buttons = Array.AsReadOnly(buttons.ToArray());
            Switches = Array.AsReadOnly(switches.ToArray());
            Axes = Array.AsReadOnly(axes.ToArray());
        }

        public string DeviceId { get; }
        public IReadOnlyList<bool> Buttons { get; }
        public IReadOnlyList<HostJoystickHatPosition> Switches { get; }
        public IReadOnlyList<double> Axes { get; }
    }

    public interface IHostJoystickSource : IDisposable
    {
        IReadOnlyList<HostJoystickDevice> GetDevices();
        bool TryRead(string? preferredDeviceId, out HostJoystickReading reading);
    }

    public sealed record HostJoystickMapping(
        int HorizontalAxis = 0,
        int VerticalAxis = 1,
        int HatSwitch = 0,
        int FireButton = 0,
        double DeadZone = 0.25);

    public sealed record HostJoystickChannelSettings(
        bool Enabled = true,
        string? PreferredDeviceId = null,
        HostJoystickMapping? Mapping = null,
        SpectrumJoystickInterface? Interface = null)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public HostJoystickMapping EffectiveMapping => Mapping ?? new HostJoystickMapping();

        [System.Text.Json.Serialization.JsonIgnore]
        public SpectrumJoystickInterface EffectiveInterface => Interface ?? SpectrumJoystickInterface.Kempston;
    }

    public sealed record HostJoystickSettings(
        bool Enabled = true,
        string? PreferredDeviceId = null,
        HostJoystickMapping? Mapping = null,
        SpectrumJoystickInterface? Interface = null,
        HostJoystickChannelSettings? Controller2 = null)
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public HostJoystickMapping EffectiveMapping => Mapping ?? new HostJoystickMapping();

        // A null interface migrates settings written before interface selection existed.
        [System.Text.Json.Serialization.JsonIgnore]
        public SpectrumJoystickInterface EffectiveInterface => Interface ?? SpectrumJoystickInterface.Kempston;

        [System.Text.Json.Serialization.JsonIgnore]
        public HostJoystickChannelSettings Controller1Settings =>
            new(Enabled, PreferredDeviceId, Mapping, Interface);

        [System.Text.Json.Serialization.JsonIgnore]
        public HostJoystickChannelSettings Controller2Settings =>
            Controller2 ?? new HostJoystickChannelSettings(
                Enabled: false,
                Interface: SpectrumJoystickInterface.SinclairKeys6To0);

        public HostJoystickSettings WithController1(HostJoystickChannelSettings settings) =>
            this with
            {
                Enabled = settings.Enabled,
                PreferredDeviceId = settings.PreferredDeviceId,
                Mapping = settings.Mapping,
                Interface = settings.Interface
            };
    }

    public static class HostJoystickMapper
    {
        public static SpectrumJoystickState Map(
            HostJoystickReading reading,
            HostJoystickMapping mapping)
        {
            ArgumentNullException.ThrowIfNull(reading);
            ArgumentNullException.ThrowIfNull(mapping);

            double deadZone = Math.Clamp(mapping.DeadZone, 0.0, 0.95);
            double horizontal = ReadAxis(reading.Axes, mapping.HorizontalAxis);
            double vertical = ReadAxis(reading.Axes, mapping.VerticalAxis);
            bool left = horizontal < -deadZone;
            bool right = horizontal > deadZone;
            bool up = vertical < -deadZone;
            bool down = vertical > deadZone;

            if ((uint)mapping.HatSwitch < reading.Switches.Count)
            {
                HostJoystickHatPosition hat = reading.Switches[mapping.HatSwitch];
                left |= hat is HostJoystickHatPosition.Left or HostJoystickHatPosition.UpLeft or HostJoystickHatPosition.DownLeft;
                right |= hat is HostJoystickHatPosition.Right or HostJoystickHatPosition.UpRight or HostJoystickHatPosition.DownRight;
                up |= hat is HostJoystickHatPosition.Up or HostJoystickHatPosition.UpLeft or HostJoystickHatPosition.UpRight;
                down |= hat is HostJoystickHatPosition.Down or HostJoystickHatPosition.DownLeft or HostJoystickHatPosition.DownRight;
            }

            SpectrumJoystickState state = SpectrumJoystickState.None;
            if (right && !left)
                state |= SpectrumJoystickState.Right;
            if (left && !right)
                state |= SpectrumJoystickState.Left;
            if (down && !up)
                state |= SpectrumJoystickState.Down;
            if (up && !down)
                state |= SpectrumJoystickState.Up;
            if ((uint)mapping.FireButton < reading.Buttons.Count && reading.Buttons[mapping.FireButton])
                state |= SpectrumJoystickState.Fire;
            return state;
        }

        private static double ReadAxis(IReadOnlyList<double> axes, int index) =>
            (uint)index < axes.Count ? Math.Clamp((axes[index] * 2.0) - 1.0, -1.0, 1.0) : 0.0;
    }

    public sealed class HostJoystickInputController
    {
        private readonly IHostJoystickSource source;

        public HostJoystickInputController(IHostJoystickSource source) =>
            this.source = source ?? throw new ArgumentNullException(nameof(source));

        public SpectrumJoystickState Poll(
            HostJoystickChannelSettings settings,
            string? resolvedDeviceId = null)
        {
            ArgumentNullException.ThrowIfNull(settings);
            string? deviceId = resolvedDeviceId ?? settings.PreferredDeviceId;
            if (!settings.Enabled || !source.TryRead(deviceId, out HostJoystickReading reading))
                return SpectrumJoystickState.None;

            return HostJoystickMapper.Map(reading, settings.EffectiveMapping);
        }
    }
}
