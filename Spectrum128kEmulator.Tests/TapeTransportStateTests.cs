using System;
using System.IO;
using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class TapeTransportStateTests
    {
        [Fact]
        public void NewMachine_AndToggleWithoutTape_RemainNoTape()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);

                AssertTransport(machine, TapeTransportState.NoTape, null);
                Assert.Equal(TapeTransportTransition.None, machine.LastTapeTransportTransition);
                Assert.Equal(TapeTransportState.NoTape, machine.ToggleTapeTransport());
                AssertTransport(machine, TapeTransportState.NoTape, null);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void MountTape_ReplacesExistingTapeAndResetsIncomingTransport()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                MountedTape original = CreatePlayingTape("original.tap");
                MountedTape replacement = CreatePlayingTape("replacement.tap");
                replacement.ReadEarBit(0);
                replacement.ReadEarBit(75);
                replacement.PausePlayback(75);

                machine.MountTape(original);
                machine.MountTape(replacement);

                AssertTransport(machine, TapeTransportState.Playing, replacement);
                Assert.Equal(TapeTransportTransition.Mounted, machine.LastTapeTransportTransition);
                Assert.False(replacement.IsPlaybackPaused);
                Assert.True(replacement.ReadEarBit(0));
                Assert.True(replacement.ReadEarBit(100));
                Assert.False(replacement.ReadEarBit(101));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void EjectTape_ReleasesMountedTapeAndReturnsToNoTape()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(CreatePlayingTape("eject.tap"));
                machine.ToggleTapeTransport();
                Assert.Equal(TapeTransportTransition.ManualStop, machine.LastTapeTransportTransition);

                machine.EjectTape();

                AssertTransport(machine, TapeTransportState.NoTape, null);
                Assert.Equal(TapeTransportTransition.Ejected, machine.LastTapeTransportTransition);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Theory]
        [InlineData(SpectrumMachineModel.Spectrum48K)]
        [InlineData(SpectrumMachineModel.Spectrum128K)]
        public void Reset_ClearsTapeAndPendingContinuation(SpectrumMachineModel model)
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(CreatePlayingTape("reset.tap"));
                machine.SetPendingMountedLoadUsrContinuation(0x8000);

                machine.Reset(model);

                AssertTransport(machine, TapeTransportState.NoTape, null);
                Assert.Equal(TapeTransportTransition.Reset, machine.LastTapeTransportTransition);
                Assert.False(machine.HasPendingMountedLoadUsrContinuation);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void StopMarker_RecordsStructuredTransitionReason()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new MountedTape(
                    "marker.tzx",
                    new[]
                    {
                        TapeBlock.CreatePureTone(pulseLength: 1, pulseCount: 1),
                        TapeBlock.CreateStopTape(),
                        TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 1)
                    }));

                machine.ExecuteTimeSlice(32);
                machine.ExecuteTimeSlice(32);

                AssertTransport(machine, TapeTransportState.Stopped, machine.MountedTape);
                Assert.Equal(TapeTransportTransition.StopMarker, machine.LastTapeTransportTransition);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void QuickState_RestoresMountedTapeCursorAndStoppedTransport()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                MountedTape capturedTape = CreatePlayingTape("captured.tap");
                machine.MountTape(capturedTape);
                machine.DebugReadPort(0x00FE);
                machine.Cpu.AddTStates(75);
                machine.DebugReadPort(0x00FE);
                machine.ToggleTapeTransport();
                Spectrum128Machine.QuickState state = machine.CaptureQuickState();

                machine.MountTape(CreatePlayingTape("replacement.tap"));
                machine.RestoreQuickState(state);

                AssertTransport(machine, TapeTransportState.Stopped, capturedTape);
                Assert.Equal(TapeTransportTransition.ManualStop, machine.LastTapeTransportTransition);
                Assert.Equal(TapeTransportState.Playing, machine.ToggleTapeTransport());
                Assert.Equal(TapeTransportTransition.ManualResume, machine.LastTapeTransportTransition);
                Assert.True(capturedTape.ReadEarBit(100));
                Assert.False(capturedTape.ReadEarBit(101));
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void QuickState_RestoresEndedStateWithoutMountedTape()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.MountTape(new MountedTape(
                    "ended.tap",
                    new[] { TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 1) },
                    initialBlockIndex: 1));
                machine.ExecuteTimeSlice(1);
                Spectrum128Machine.QuickState state = machine.CaptureQuickState();

                machine.MountTape(CreatePlayingTape("replacement.tap"));
                machine.RestoreQuickState(state);

                AssertTransport(machine, TapeTransportState.Ended, null);
                Assert.Equal(TapeTransportTransition.Ended, machine.LastTapeTransportTransition);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static MountedTape CreatePlayingTape(string name) => new MountedTape(
            name,
            new[] { TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 3) });

        private static void AssertTransport(
            Spectrum128Machine machine,
            TapeTransportState expectedState,
            MountedTape? expectedTape)
        {
            Assert.Equal(expectedState, machine.TapeTransportState);
            Assert.Equal(expectedTape != null, machine.HasMountedTape);
            Assert.Same(expectedTape, machine.MountedTape);

            if (expectedState == TapeTransportState.Playing)
                Assert.False(machine.MountedTape!.IsPlaybackPaused);
            else if (expectedState == TapeTransportState.Stopped)
                Assert.True(machine.MountedTape!.IsPlaybackPaused);
        }

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }
    }
}
