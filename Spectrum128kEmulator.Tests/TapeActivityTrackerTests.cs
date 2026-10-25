using Spectrum128kEmulator.Tap;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class TapeActivityTrackerTests
    {
        [Fact]
        public void AutomaticHandoff_StopThenRecognizedReads_ResumesAtExactCursor()
        {
            var tracker = new TapeActivityTracker();
            MountedTape tape = CreateDrivingTape();
            tracker.ObserveLoaderActivity();

            Assert.False(TryStop(tracker, tape));
            Assert.False(TryStop(tracker, tape));
            Assert.True(TryStop(tracker, tape));
            Assert.True(tape.IsPlaybackPaused);
            Assert.True(tracker.AutoPausedAtHandoff);

            for (int read = 1; read < 64; read++)
                Assert.False(tracker.TryAutomaticallyResume(tape, true, 500, 64));

            Assert.True(tracker.TryAutomaticallyResume(tape, true, 500, 64));
            Assert.False(tape.IsPlaybackPaused);
            Assert.False(tracker.AutoPausedAtHandoff);
            Assert.False(tape.ReadEarBit(599));
            Assert.True(tape.ReadEarBit(600));
        }

        [Theory]
        [InlineData(false, true, 0x8000, false)]
        [InlineData(true, false, 0x8000, false)]
        [InlineData(true, true, 0x3FFF, false)]
        [InlineData(true, true, 0x8000, true)]
        public void AutomaticHandoff_StopRequiresEverySafetyGate(
            bool loaderActivityObserved,
            bool cpuIsHalted,
            int programCounter,
            bool hasPendingContinuation)
        {
            var tracker = new TapeActivityTracker();
            MountedTape tape = CreateDrivingTape();
            if (loaderActivityObserved)
                tracker.ObserveLoaderActivity();

            for (int frame = 0; frame < 4; frame++)
            {
                Assert.False(tracker.TryAutomaticallyStop(
                    tape,
                    cpuIsHalted,
                    (ushort)programCounter,
                    hasPendingContinuation,
                    100,
                    activityReadsPerFrame: 64,
                    requiredHandoffFrames: 3));
            }

            Assert.False(tape.IsPlaybackPaused);
        }

        [Fact]
        public void QuickState_RestoresAllActivityAndHandoffCounters()
        {
            var tracker = new TapeActivityTracker();
            tracker.RecordPortRead(keyboardOnly: false);
            tracker.RecordPortRead(keyboardOnly: true);
            tracker.ObserveLoaderActivity();
            TapeActivityTracker.QuickState state = tracker.CaptureQuickState();

            tracker.Reset();
            tracker.RestoreQuickState(state);

            Assert.Equal((ulong)2, tracker.PortReadCount);
            Assert.Equal((ulong)1, tracker.KeyboardOnlyPortReadCount);
            Assert.True(tracker.LoaderActivityObserved);
        }

        [Fact]
        public void AutomaticHandoff_KeyboardEvidenceMaySpanIdleFrames()
        {
            var tracker = new TapeActivityTracker();
            MountedTape tape = CreateDrivingTape();
            tracker.ObserveLoaderActivity();

            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            Assert.False(RecordIdleFrame(tracker, tape));
            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            Assert.False(RecordIdleFrame(tracker, tape));
            Assert.True(RecordKeyboardWaitFrame(tracker, tape));

            Assert.True(tape.IsPlaybackPaused);
        }

        [Fact]
        public void AutomaticHandoff_IdleFramesBeforeKeyboardEvidenceDoNotCauseEarlyStop()
        {
            var tracker = new TapeActivityTracker();
            MountedTape tape = CreateDrivingTape();
            tracker.ObserveLoaderActivity();

            Assert.False(RecordIdleFrame(tracker, tape));
            Assert.False(RecordIdleFrame(tracker, tape));
            Assert.False(RecordIdleFrame(tracker, tape));
            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            Assert.False(tape.IsPlaybackPaused);

            Assert.True(RecordKeyboardWaitFrame(tracker, tape));
            Assert.True(tape.IsPlaybackPaused);
        }

        [Fact]
        public void AutomaticHandoff_EarReadCancelsAccumulatedKeyboardEvidence()
        {
            var tracker = new TapeActivityTracker();
            MountedTape tape = CreateDrivingTape();
            tracker.ObserveLoaderActivity();

            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            tracker.RecordPortRead(keyboardOnly: false);
            Assert.False(RecordActiveFrame(tracker, tape));
            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            Assert.False(RecordKeyboardWaitFrame(tracker, tape));
            Assert.True(RecordKeyboardWaitFrame(tracker, tape));
        }

        private static bool TryStop(TapeActivityTracker tracker, MountedTape tape) =>
            tracker.TryAutomaticallyStop(
                tape,
                cpuIsHalted: true,
                programCounter: 0x8000,
                hasPendingContinuation: false,
                currentTStates: 100,
                activityReadsPerFrame: 64,
                requiredHandoffFrames: 3);

        private static bool RecordKeyboardWaitFrame(TapeActivityTracker tracker, MountedTape tape)
        {
            for (int read = 0; read < 65; read++)
                tracker.RecordPortRead(keyboardOnly: true);
            return RecordActiveFrame(tracker, tape);
        }

        private static bool RecordIdleFrame(TapeActivityTracker tracker, MountedTape tape) =>
            RecordActiveFrame(tracker, tape);

        private static bool RecordActiveFrame(TapeActivityTracker tracker, MountedTape tape) =>
            tracker.TryAutomaticallyStop(
                tape,
                cpuIsHalted: false,
                programCounter: 0x8000,
                hasPendingContinuation: false,
                currentTStates: 100,
                activityReadsPerFrame: 64,
                requiredHandoffFrames: 3);

        private static MountedTape CreateDrivingTape()
        {
            var tape = new MountedTape(
                "policy.tap",
                new[] { TapeBlock.CreatePureTone(pulseLength: 100, pulseCount: 4) });
            tape.ReadEarBit(0);
            return tape;
        }
    }
}
