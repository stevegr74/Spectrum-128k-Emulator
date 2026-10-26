using Windows.Gaming.Input;

namespace Spectrum128kEmulator
{
    public sealed class WindowsRawGameControllerSource : IHostJoystickSource
    {
        private RawGameController? activeController;
        private string? activePreferredDeviceId;
        private bool[] buttons = Array.Empty<bool>();
        private GameControllerSwitchPosition[] switches = Array.Empty<GameControllerSwitchPosition>();
        private double[] axes = Array.Empty<double>();

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
                RawGameController? controller = GetActiveController(preferredDeviceId);
                if (controller == null)
                    return false;

                controller.GetCurrentReading(buttons, switches, axes);
                reading = new HostJoystickReading(
                    GetControllerId(controller),
                    buttons,
                    switches.Select(MapSwitch).ToArray(),
                    axes);
                return true;
            }
            catch
            {
                ClearActiveController();
                return false;
            }
        }

        public void Dispose()
        {
            ClearActiveController();
        }

        private RawGameController? GetActiveController(string? preferredDeviceId)
        {
            if (activeController != null &&
                string.Equals(activePreferredDeviceId, preferredDeviceId, StringComparison.Ordinal) &&
                (preferredDeviceId == null || GetControllerId(activeController) == preferredDeviceId))
            {
                return activeController;
            }

            RawGameController? controller = ResolveController(preferredDeviceId);
            if (controller == null)
            {
                ClearActiveController();
                return null;
            }

            activeController = controller;
            activePreferredDeviceId = preferredDeviceId;
            buttons = new bool[controller.ButtonCount];
            switches = new GameControllerSwitchPosition[controller.SwitchCount];
            axes = new double[controller.AxisCount];
            return controller;
        }

        private void ClearActiveController()
        {
            activeController = null;
            activePreferredDeviceId = null;
            buttons = Array.Empty<bool>();
            switches = Array.Empty<GameControllerSwitchPosition>();
            axes = Array.Empty<double>();
        }

        private static RawGameController? ResolveController(string? preferredDeviceId)
        {
            IReadOnlyList<RawGameController> controllers = RawGameController.RawGameControllers;
            if (controllers.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(preferredDeviceId))
                return controllers[0];
            return controllers.FirstOrDefault(controller =>
                       string.Equals(GetControllerId(controller), preferredDeviceId, StringComparison.Ordinal)) ??
                   controllers[0];
        }

        private static HostJoystickDevice ToDevice(RawGameController controller) =>
            new(
                GetControllerId(controller),
                string.IsNullOrWhiteSpace(controller.DisplayName) ? "USB game controller" : controller.DisplayName,
                controller.AxisCount,
                controller.ButtonCount,
                controller.SwitchCount);

        private static string GetControllerId(RawGameController controller) =>
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
