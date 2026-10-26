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
                controller.Poll(new HostJoystickChannelSettings(PreferredDeviceId: "second")));
            Assert.Equal("second", source.LastPreferredDeviceId);

            source.Available = false;
            Assert.Equal(SpectrumJoystickState.None, controller.Poll(new HostJoystickChannelSettings()));

            source.Available = true;
            Assert.Equal(SpectrumJoystickState.None, controller.Poll(new HostJoystickChannelSettings(Enabled: false)));
            Assert.Equal(2, source.ReadCount);
        }

        [Fact]
        public void InputController_PollsTwoDevicesIndependently()
        {
            var source = new MultiJoystickSource(
                new HostJoystickReading("pad-1", [true], [], [0.5, 0.5]),
                new HostJoystickReading("pad-2", [false], [], [0.0, 0.5]));
            var controller = new HostJoystickInputController(source);

            SpectrumJoystickState first = controller.Poll(
                new HostJoystickChannelSettings(PreferredDeviceId: "pad-1"),
                "pad-1");
            SpectrumJoystickState second = controller.Poll(
                new HostJoystickChannelSettings(PreferredDeviceId: "pad-2"),
                "pad-2");

            Assert.Equal(SpectrumJoystickState.Fire, first);
            Assert.Equal(SpectrumJoystickState.Left, second);
            Assert.Equal(["pad-1", "pad-2"], source.RequestedDeviceIds);
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
                    Interface: SpectrumJoystickInterface.SinclairKeys6To0,
                    Controller2: new HostJoystickChannelSettings(
                        Enabled: true,
                        PreferredDeviceId: "controller-3",
                        Interface: SpectrumJoystickInterface.SinclairKeys1To5));

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
                Assert.True(settings.Controller1Settings.Enabled);
                Assert.False(settings.Controller2Settings.Enabled);
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
                    var device = new HostJoystickDevice(
                        "stable-pad-id",
                        "Test Controller",
                        4,
                        6,
                        2,
                        LegacyId: "legacy-pad-id");
                    using var form = new JoystickConfigurationForm(
                        new HostJoystickChannelSettings(
                            PreferredDeviceId: "legacy-pad-id",
                            Mapping: expected,
                            Interface: SpectrumJoystickInterface.SinclairKeys1To5),
                        new HostJoystickChannelSettings(
                            Enabled: false,
                            Interface: SpectrumJoystickInterface.SinclairKeys6To0),
                        [device]);

                    Assert.Equal(expected, form.Controller1Settings.EffectiveMapping);
                    Assert.Equal(SpectrumJoystickInterface.SinclairKeys1To5, form.Controller1Settings.EffectiveInterface);
                    Assert.Equal("stable-pad-id", form.Controller1Settings.PreferredDeviceId);
                    Assert.False(form.Controller2Settings.Enabled);
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

        [Theory]
        [InlineData(SpectrumJoystickInterface.SinclairKeys1To5, "Sinclair 1 (keys 1-5; Interface II socket 2)")]
        [InlineData(SpectrumJoystickInterface.SinclairKeys6To0, "Sinclair 2 (keys 6-0; Interface II socket 1)")]
        public void ConfigurationForm_UsesGameFacingSinclairNames(
            SpectrumJoystickInterface joystickInterface,
            string expectedLabel)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new JoystickConfigurationForm(
                        new HostJoystickChannelSettings(Interface: joystickInterface),
                        new HostJoystickChannelSettings(Enabled: false),
                        []);
                    string[] labels = Descendants(form)
                        .OfType<System.Windows.Forms.ComboBox>()
                        .SelectMany(selector => selector.Items.Cast<object>())
                        .Select(item => item.ToString() ?? string.Empty)
                        .ToArray();

                    Assert.Contains(expectedLabel, labels);
                    Assert.Equal(joystickInterface, form.Controller1Settings.EffectiveInterface);
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

        private static IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control root)
        {
            foreach (System.Windows.Forms.Control child in root.Controls)
            {
                yield return child;
                foreach (System.Windows.Forms.Control descendant in Descendants(child))
                    yield return descendant;
            }
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

        private sealed class MultiJoystickSource(params HostJoystickReading[] readings) : IHostJoystickSource
        {
            private readonly Dictionary<string, HostJoystickReading> readingsById =
                readings.ToDictionary(reading => reading.DeviceId, StringComparer.Ordinal);

            public List<string?> RequestedDeviceIds { get; } = new();

            public IReadOnlyList<HostJoystickDevice> GetDevices() => readingsById.Values
                .Select(reading => new HostJoystickDevice(
                    reading.DeviceId,
                    reading.DeviceId,
                    reading.Axes.Count,
                    reading.Buttons.Count,
                    reading.Switches.Count))
                .ToArray();

            public bool TryRead(string? preferredDeviceId, out HostJoystickReading reading)
            {
                RequestedDeviceIds.Add(preferredDeviceId);
                if (preferredDeviceId != null && readingsById.TryGetValue(preferredDeviceId, out HostJoystickReading? found))
                {
                    reading = found;
                    return true;
                }

                reading = null!;
                return false;
            }

            public void Dispose()
            {
            }
        }
    }
}
