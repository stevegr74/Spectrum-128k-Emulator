using Spectrum128kEmulator.Input;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class HostJoystickInputTests
    {
        [Fact]
        public void Mapper_CombinesAxesHatAndFire()
        {
            var reading = new HostJoystickReading(
                "pad",
                [true, false],
                [HostJoystickHatPosition.UpRight],
                [0.9, 0.5]);

            SpectrumJoystickState state = HostJoystickMapper.Map(reading, new HostJoystickMapping());

            Assert.Equal(
                SpectrumJoystickState.Right | SpectrumJoystickState.Up | SpectrumJoystickState.Fire,
                state);
        }

        [Fact]
        public void Mapper_AppliesDeadZoneAndCancelsOpposingDirections()
        {
            var insideDeadZone = new HostJoystickReading(
                "pad",
                [false],
                [HostJoystickHatPosition.Centered],
                [0.60, 0.40]);
            Assert.Equal(
                SpectrumJoystickState.None,
                HostJoystickMapper.Map(insideDeadZone, new HostJoystickMapping(DeadZone: 0.25)));

            var opposing = new HostJoystickReading(
                "pad",
                [false],
                [HostJoystickHatPosition.Left],
                [1.0, 0.5]);
            Assert.Equal(
                SpectrumJoystickState.None,
                HostJoystickMapper.Map(opposing, new HostJoystickMapping()));
        }

        [Fact]
        public void Mapper_IgnoresDisabledOrUnavailableControls()
        {
            var reading = new HostJoystickReading("pad", [true], [], []);
            var mapping = new HostJoystickMapping(12, 13, -1, -1, 0.25);

            Assert.Equal(SpectrumJoystickState.None, HostJoystickMapper.Map(reading, mapping));
        }

        [Fact]
        public void InputController_UsesPreferredDeviceAndClearsWhenUnavailableOrDisabled()
        {
            var source = new FakeJoystickSource
            {
                Reading = new HostJoystickReading("second", [true], [], [0.5, 0.5])
            };
            var controller = new HostJoystickInputController(source);

            Assert.Equal(
                SpectrumJoystickState.Fire,
                controller.Poll(new HostJoystickSettings(PreferredDeviceId: "second")));
            Assert.Equal("second", source.LastPreferredDeviceId);

            source.Available = false;
            Assert.Equal(SpectrumJoystickState.None, controller.Poll(new HostJoystickSettings()));

            source.Available = true;
            Assert.Equal(SpectrumJoystickState.None, controller.Poll(new HostJoystickSettings(Enabled: false)));
            Assert.Equal(2, source.ReadCount);
        }

        [Fact]
        public void SettingsStore_RoundTripsAndRecoversFromInvalidJson()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "joystick.json");
            try
            {
                var store = new JoystickSettingsStore(path);
                var expected = new HostJoystickSettings(
                    Enabled: true,
                    PreferredDeviceId: "controller-2",
                    Mapping: new HostJoystickMapping(3, 4, -1, 7, 0.35),
                    Interface: SpectrumJoystickInterface.Sinclair1);

                store.Save(expected);
                Assert.Equal(expected, store.Load());

                File.WriteAllText(path, "not json");
                Assert.Equal(new HostJoystickSettings(), store.Load());
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void SettingsWrittenBeforeInterfaceSelection_DefaultToKempston()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "joystick.json");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, "{\"Enabled\":true,\"PreferredDeviceId\":null}");

                HostJoystickSettings settings = new JoystickSettingsStore(path).Load();

                Assert.Null(settings.Interface);
                Assert.Equal(SpectrumJoystickInterface.Kempston, settings.EffectiveInterface);
            }
            finally
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void ConfigurationForm_RetainsTheDisplayedMapping()
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var expected = new HostJoystickMapping(1, 2, 1, 3, 0.35);
                    var device = new HostJoystickDevice("pad", "Test Controller", 4, 6, 2);
                    using var form = new JoystickConfigurationForm(
                        expected,
                        SpectrumJoystickInterface.Sinclair2,
                        device);

                    Assert.Equal(expected, form.SelectedMapping);
                    Assert.Equal(SpectrumJoystickInterface.Sinclair2, form.SelectedInterface);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The joystick configuration test timed out.");
            if (failure != null)
                throw new Xunit.Sdk.XunitException(failure.ToString());
        }

        private sealed class FakeJoystickSource : IHostJoystickSource
        {
            public bool Available { get; set; } = true;
            public HostJoystickReading Reading { get; set; } = new("pad", [], [], []);
            public string? LastPreferredDeviceId { get; private set; }
            public int ReadCount { get; private set; }

            public IReadOnlyList<HostJoystickDevice> GetDevices() =>
                [new HostJoystickDevice(Reading.DeviceId, "Test Controller", Reading.Axes.Count, Reading.Buttons.Count, Reading.Switches.Count)];

            public bool TryRead(string? preferredDeviceId, out HostJoystickReading reading)
            {
                ReadCount++;
                LastPreferredDeviceId = preferredDeviceId;
                reading = Reading;
                return Available && (preferredDeviceId == null || preferredDeviceId == Reading.DeviceId);
            }

            public void Dispose()
            {
            }
        }
    }
}
