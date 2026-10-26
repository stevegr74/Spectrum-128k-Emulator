using System.Reflection;
using Spectrum128kEmulator.Input;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class SpectrumJoystickTests
    {
        [Theory]
        [InlineData(SpectrumMachineModel.Spectrum48K)]
        [InlineData(SpectrumMachineModel.Spectrum128K)]
        public void KempstonPort_ReturnsActiveHighState_InBothMachineModels(SpectrumMachineModel model)
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.Reset(model);
            machine.SetJoystickState(
                SpectrumJoystickInterface.Kempston,
                SpectrumJoystickState.Right |
                SpectrumJoystickState.Up |
                SpectrumJoystickState.Fire);

            Assert.Equal(0x19, machine.DebugReadPort(0x001F));
            Assert.Equal(0x19, machine.DebugReadPort(0xAB1F));
        }

        [Fact]
        public void KempstonState_MasksUnsupportedBits_AndCanBeCleared()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);

            machine.SetJoystickState(SpectrumJoystickInterface.Kempston, (SpectrumJoystickState)0xFF);
            Assert.Equal(SpectrumJoystickState.All, machine.GetJoystickState(SpectrumJoystickInterface.Kempston));
            Assert.True(machine.IsJoystickInterfaceActive(SpectrumJoystickInterface.Kempston));
            Assert.Equal(0x1F, machine.DebugReadPort(0x001F));

            machine.ClearJoystick();
            Assert.Equal(SpectrumJoystickState.None, machine.GetJoystickState(SpectrumJoystickInterface.Kempston));
            Assert.False(machine.IsJoystickInterfaceActive(SpectrumJoystickInterface.Kempston));
        }

        [Fact]
        public void NonKempstonOddPort_StillUsesFloatingBus()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(SpectrumJoystickInterface.Kempston, SpectrumJoystickState.All);

            Assert.NotEqual(0x1F, machine.DebugReadPort(0x003F));
        }

        [Fact]
        public void TimedPortRead_UsesKempstonState()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(
                SpectrumJoystickInterface.Kempston,
                SpectrumJoystickState.Left | SpectrumJoystickState.Down);
            MethodInfo readPortTimed = typeof(Spectrum128Machine).GetMethod(
                "ReadPortTimed",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            byte value = (byte)readPortTimed.Invoke(machine, [unchecked((ushort)0xFF1F), 3])!;

            Assert.Equal(0x06, value);
        }

        [Fact]
        public void SinclairKeysSixThroughZero_MapToInterfaceTwoSocketOne()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(
                SpectrumJoystickInterface.SinclairKeys6To0,
                SpectrumJoystickState.Left | SpectrumJoystickState.Up | SpectrumJoystickState.Fire);

            Assert.Equal(0x0C, machine.DebugReadPort(0xEFFE) & 0x1F);
            Assert.Equal(0x1F, machine.DebugReadPort(0xF7FE) & 0x1F);
        }

        [Fact]
        public void Cursor_MapsDirectionsAndFireToCursorKeysAndZero()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(
                SpectrumJoystickInterface.Cursor,
                SpectrumJoystickState.Left |
                SpectrumJoystickState.Right |
                SpectrumJoystickState.Down |
                SpectrumJoystickState.Up |
                SpectrumJoystickState.Fire);

            Assert.Equal(0x0F, machine.DebugReadPort(0xF7FE) & 0x1F); // 5
            Assert.Equal(0x02, machine.DebugReadPort(0xEFFE) & 0x1F); // 6, 7, 8, 0
        }

        [Fact]
        public void SinclairKeysOneThroughFive_MapToInterfaceTwoSocketTwo()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(
                SpectrumJoystickInterface.SinclairKeys1To5,
                SpectrumJoystickState.Right | SpectrumJoystickState.Down | SpectrumJoystickState.Fire);

            Assert.Equal(0x09, machine.DebugReadPort(0xF7FE) & 0x1F);
            Assert.Equal(0x1F, machine.DebugReadPort(0xEFFE) & 0x1F);
        }

        [Fact]
        public void SinclairJoystickAndKeyboardContributionsRemainIndependent()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetKey(row: 4, bit: 3, pressed: true); // 7
            machine.SetJoystickState(SpectrumJoystickInterface.SinclairKeys6To0, SpectrumJoystickState.Left); // 6

            Assert.Equal(0x07, machine.DebugReadPort(0xEFFE) & 0x1F);

            machine.SetJoystickState(SpectrumJoystickInterface.SinclairKeys6To0, SpectrumJoystickState.None);
            Assert.Equal(0x17, machine.DebugReadPort(0xEFFE) & 0x1F);
        }

        [Fact]
        public void BothSinclairJoysticks_AreReadableSimultaneously()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(
                SpectrumJoystickInterface.SinclairKeys1To5,
                SpectrumJoystickState.Left | SpectrumJoystickState.Fire);
            machine.SetJoystickState(
                SpectrumJoystickInterface.SinclairKeys6To0,
                SpectrumJoystickState.Right | SpectrumJoystickState.Up);

            Assert.Equal(0x0E, machine.DebugReadPort(0xF7FE) & 0x1F); // 1 and 5
            Assert.Equal(0x15, machine.DebugReadPort(0xEFFE) & 0x1F); // 7 and 9
        }

        [Fact]
        public void CursorAndSinclairInputs_CombineOnSharedKeyboardRows()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(SpectrumJoystickInterface.Cursor, SpectrumJoystickState.Left);
            machine.SetJoystickState(SpectrumJoystickInterface.SinclairKeys1To5, SpectrumJoystickState.Fire);

            Assert.Equal(0x0F, machine.DebugReadPort(0xF7FE) & 0x1F); // 5 from both mappings
        }

        [Theory]
        [InlineData(SpectrumJoystickInterface.None)]
        [InlineData(SpectrumJoystickInterface.Cursor)]
        [InlineData(SpectrumJoystickInterface.SinclairKeys6To0)]
        [InlineData(SpectrumJoystickInterface.SinclairKeys1To5)]
        public void NonKempstonInterfaces_DoNotClaimKempstonPort(SpectrumJoystickInterface joystickInterface)
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(joystickInterface, SpectrumJoystickState.All);

            Assert.NotEqual(0x1F, machine.DebugReadPort(0x001F));
        }

        [Fact]
        public void RzxInputTakesPrecedenceOverLiveKempstonState()
        {
            using var roms = TestRomFolder.Create();
            var machine = new Spectrum128Machine(roms.Path);
            machine.SetJoystickState(SpectrumJoystickInterface.Kempston, SpectrumJoystickState.All);
            var replay = new RzxPlaybackSession("input.rzx", [new RzxFrame(1, [0x12, 0x0A], false)]);
            Assert.True(replay.TryBeginNextFrame(out _));
            machine.AttachRzxPlayback(replay);

            Assert.Equal(0x12, machine.DebugReadPort(0x001F));

            MethodInfo readPortTimed = typeof(Spectrum128Machine).GetMethod(
                "ReadPortTimed",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.Equal(0x0A, (byte)readPortTimed.Invoke(machine, [unchecked((ushort)0x001F), 3])!);
        }

        private sealed class TestRomFolder : IDisposable
        {
            private TestRomFolder(string path) => Path = path;

            public string Path { get; }

            public static TestRomFolder Create()
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(path);
                File.WriteAllBytes(System.IO.Path.Combine(path, "128-0.rom"), new byte[16384]);
                File.WriteAllBytes(System.IO.Path.Combine(path, "128-1.rom"), new byte[16384]);
                return new TestRomFolder(path);
            }

            public void Dispose() => Directory.Delete(Path, true);
        }
    }
}
