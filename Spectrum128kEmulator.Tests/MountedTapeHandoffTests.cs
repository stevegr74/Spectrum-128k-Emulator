using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class MountedTapeHandoffTests
    {
        [Fact]
        public void ProtectedLongPause_In48kMode_StopsBeforeFollowingStageAndResumesThere()
        {
            MountedTape tape = CreateProtectedMultiloadTape();
            tape.ConfigurePlaybackClock(
                Spectrum128Machine.CpuClockHz48,
                stopAtProtectedStageHandoffs: true);

            tape.ReadEarBit(0);
            tape.ReadEarBit(100);

            Assert.True(tape.IsPlaybackPaused);
            Assert.True(tape.IsPausedByProtectedStageHandoff);
            Assert.False(tape.IsPausedByStopMarker);

            Assert.True(tape.ResumePlayback(500));
            Assert.False(tape.IsPlaybackPaused);
            Assert.True(tape.IsActivelyDrivingEarLine);
        }

        [Fact]
        public void ProtectedLongPause_In128kMode_ContinuesWithoutTransportStop()
        {
            MountedTape tape = CreateProtectedMultiloadTape();
            tape.ConfigurePlaybackClock(
                Spectrum128Machine.CpuClockHz128,
                stopAtProtectedStageHandoffs: false);

            tape.ReadEarBit(0);
            tape.ReadEarBit(100);

            Assert.False(tape.IsPlaybackPaused);
            Assert.False(tape.IsPausedByProtectedStageHandoff);
        }

        [Fact]
        public void ProtectedShortPause_In48kMode_RemainsContinuous()
        {
            var tape = new MountedTape(
                "short-pause.tzx",
                new[]
                {
                    TapeBlock.CreateByteStreamData(
                        new byte[] { 0x80, 0x00 },
                        zeroBitPulseLength: 1,
                        oneBitPulseLength: 1,
                        usedBitsInLastByte: 8,
                        pauseAfterBlockMs: 9999),
                    TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 2)
                });
            tape.ConfigurePlaybackClock(
                Spectrum128Machine.CpuClockHz48,
                stopAtProtectedStageHandoffs: true);

            tape.ReadEarBit(0);
            tape.ReadEarBit(100);

            Assert.False(tape.IsPlaybackPaused);
        }

        [Fact]
        public void ProtectedStageHandoff_QuickStateRestoresBoundaryAndResumePosition()
        {
            MountedTape tape = CreateProtectedMultiloadTape();
            tape.ConfigurePlaybackClock(
                Spectrum128Machine.CpuClockHz48,
                stopAtProtectedStageHandoffs: true);
            tape.ReadEarBit(0);
            tape.ReadEarBit(100);
            MountedTape.QuickState state = tape.CaptureQuickState();

            Assert.True(tape.ResumePlayback(500));
            tape.RestoreQuickState(state);

            Assert.True(tape.IsPlaybackPaused);
            Assert.True(tape.IsPausedByProtectedStageHandoff);
            Assert.True(tape.ResumePlayback(600));
            Assert.True(tape.IsActivelyDrivingEarLine);
        }

        private static MountedTape CreateProtectedMultiloadTape() => new MountedTape(
            "multiload.tzx",
            new[]
            {
                TapeBlock.CreateByteStreamData(
                    new byte[] { 0x80, 0x00 },
                    zeroBitPulseLength: 1,
                    oneBitPulseLength: 1,
                    usedBitsInLastByte: 8,
                    pauseAfterBlockMs: 10000),
                TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 2)
            });
    }
}
