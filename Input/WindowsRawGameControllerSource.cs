using Windows.Gaming.Input;

namespace Spectrum128kEmulator
{
    public sealed class WindowsRawGameControllerSource : IHostJoystickSource
    {
        private readonly Dictionary<string, ControllerBuffers> buffersByDeviceId = new(StringComparer.Ordinal);

        public IReadOnlyList<HostJoystickDevice> GetDevices()
        {
            try
            {
                return RawGameController.RawGameControllers
                    .Select(ToDevice)
                    .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<HostJoystickDevice>();
            }
        }

        public bool TryRead(string? preferredDeviceId, out HostJoystickReading reading)
        {
            reading = null!;
            try
            {
                RawGameController? controller = ResolveController(preferredDeviceId);
                if (controller == null)
                    return false;

                string deviceId = GetControllerId(controller);
                if (!buffersByDeviceId.TryGetValue(deviceId, out ControllerBuffers? buffers) ||
                    !ReferenceEquals(buffers.Controller, controller))
                {
                    buffers = new ControllerBuffers(controller);
                    buffersByDeviceId[deviceId] = buffers;
                }

                controller.GetCurrentReading(buffers.Buttons, buffers.Switches, buffers.Axes);
                reading = new HostJoystickReading(
                    deviceId,
                    buffers.Buttons,
                    buffers.Switches.Select(MapSwitch).ToArray(),
                    buffers.Axes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose() => buffersByDeviceId.Clear();

        private static RawGameController? ResolveController(string? preferredDeviceId)
        {
            IReadOnlyList<RawGameController> controllers = RawGameController.RawGameControllers;
            if (controllers.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(preferredDeviceId))
                return controllers[0];
            return controllers.FirstOrDefault(controller =>
                string.Equals(GetControllerId(controller), preferredDeviceId, StringComparison.Ordinal) ||
                string.Equals(GetLegacyControllerId(controller), preferredDeviceId, StringComparison.Ordinal));
        }

        private static HostJoystickDevice ToDevice(RawGameController controller) =>
            new(
                GetControllerId(controller),
                string.IsNullOrWhiteSpace(controller.DisplayName) ? "USB game controller" : controller.DisplayName,
                controller.AxisCount,
                controller.ButtonCount,
                controller.SwitchCount,
                GetLegacyControllerId(controller));

        private static string GetControllerId(RawGameController controller) =>
            controller.NonRoamableId;

        private static string GetLegacyControllerId(RawGameController controller) =>
            $"{controller.HardwareVendorId:X4}:{controller.HardwareProductId:X4}:{controller.DisplayName}";

        private static HostJoystickHatPosition MapSwitch(GameControllerSwitchPosition position) => position switch
        {
            GameControllerSwitchPosition.Up => HostJoystickHatPosition.Up,
            GameControllerSwitchPosition.UpRight => HostJoystickHatPosition.UpRight,
            GameControllerSwitchPosition.Right => HostJoystickHatPosition.Right,
            GameControllerSwitchPosition.DownRight => HostJoystickHatPosition.DownRight,
            GameControllerSwitchPosition.Down => HostJoystickHatPosition.Down,
            GameControllerSwitchPosition.DownLeft => HostJoystickHatPosition.DownLeft,
            GameControllerSwitchPosition.Left => HostJoystickHatPosition.Left,
            GameControllerSwitchPosition.UpLeft => HostJoystickHatPosition.UpLeft,
            _ => HostJoystickHatPosition.Centered
        };

        private sealed class ControllerBuffers
        {
            public ControllerBuffers(RawGameController controller)
            {
                Controller = controller;
                Buttons = new bool[controller.ButtonCount];
                Switches = new GameControllerSwitchPosition[controller.SwitchCount];
                Axes = new double[controller.AxisCount];
            }

            public RawGameController Controller { get; }
            public bool[] Buttons { get; }
            public GameControllerSwitchPosition[] Switches { get; }
            public double[] Axes { get; }
        }
    }

    public sealed class NullHostJoystickSource : IHostJoystickSource
    {
        public IReadOnlyList<HostJoystickDevice> GetDevices() => Array.Empty<HostJoystickDevice>();

        public bool TryRead(string? preferredDeviceId, out HostJoystickReading reading)
        {
            reading = null!;
            return false;
        }

        public void Dispose()
        {
        }
    }

    public static class HostJoystickSourceFactory
    {
        public static IHostJoystickSource Create()
        {
            try
            {
                return new WindowsRawGameControllerSource();
            }
            catch
            {
                return new NullHostJoystickSource();
            }
        }
    }
}
