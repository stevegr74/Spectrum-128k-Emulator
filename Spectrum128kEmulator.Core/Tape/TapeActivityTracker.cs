namespace Spectrum128kEmulator.Tap
{
    internal sealed class TapeActivityTracker
    {
        internal sealed record QuickState(
            ulong PortReadCount,
            ulong PortReadCountAtFrameStart,
            ulong KeyboardOnlyPortReadCount,
            ulong KeyboardOnlyPortReadCountAtFrameStart,
            bool LoaderActivityObserved,
            int HandoffFrames,
            bool AutoPausedAtHandoff,
            int AutomaticResumeReads);

        public ulong PortReadCount { get; set; }
        public ulong PortReadCountAtFrameStart { get; set; }
        public ulong KeyboardOnlyPortReadCount { get; set; }
        public ulong KeyboardOnlyPortReadCountAtFrameStart { get; set; }
        public bool LoaderActivityObserved { get; set; }
        public int HandoffFrames { get; set; }
        public bool AutoPausedAtHandoff { get; set; }
        public int AutomaticResumeReads { get; set; }

        public void RecordPortRead(bool keyboardOnly)
        {
            PortReadCount++;
            if (keyboardOnly)
                KeyboardOnlyPortReadCount++;
        }

        public void ObserveLoaderActivity()
        {
            LoaderActivityObserved = true;
        }

        public bool TryAutomaticallyResume(
            MountedTape? tape,
            bool isRecognizedRomTapeRead,
            ulong sampleTStates,
            int requiredReadCount)
        {
            if (tape == null ||
                !AutoPausedAtHandoff ||
                !tape.IsPlaybackPaused ||
                tape.IsPausedByStopMarker ||
                tape.IsPausedByProtectedStageHandoff)
            {
                AutomaticResumeReads = 0;
                return false;
            }

            if (!isRecognizedRomTapeRead)
            {
                AutomaticResumeReads = 0;
                return false;
            }

            AutomaticResumeReads++;
            if (AutomaticResumeReads < requiredReadCount)
                return false;

            bool resumed = tape.ResumePlayback(sampleTStates);
            Reset();
            return resumed;
        }

        public bool TryAutomaticallyStop(
            MountedTape? tape,
            bool cpuIsHalted,
            ushort programCounter,
            bool hasPendingContinuation,
            ulong currentTStates,
            int activityReadsPerFrame,
            int requiredHandoffFrames)
        {
            ulong readsThisFrame = PortReadCount - PortReadCountAtFrameStart;
            PortReadCountAtFrameStart = PortReadCount;
            ulong keyboardOnlyReadsThisFrame =
                KeyboardOnlyPortReadCount - KeyboardOnlyPortReadCountAtFrameStart;
            KeyboardOnlyPortReadCountAtFrameStart = KeyboardOnlyPortReadCount;
            ulong earSensitiveReadsThisFrame = readsThisFrame >= keyboardOnlyReadsThisFrame
                ? readsThisFrame - keyboardOnlyReadsThisFrame
                : 0;

            if (tape == null || tape.IsPlaybackPaused || tape.HasCompletedPlayback)
            {
                LoaderActivityObserved = false;
                HandoffFrames = 0;
                return false;
            }

            if (earSensitiveReadsThisFrame > (ulong)activityReadsPerFrame)
            {
                LoaderActivityObserved = true;
                HandoffFrames = 0;
                return false;
            }

            bool sustainedKeyboardOnlyWait =
                keyboardOnlyReadsThisFrame > (ulong)activityReadsPerFrame &&
                earSensitiveReadsThisFrame <= (ulong)activityReadsPerFrame;
            bool lowRateHaltLoop =
                cpuIsHalted &&
                readsThisFrame <= (ulong)activityReadsPerFrame;
            bool loaderHasRelinquishedTape =
                LoaderActivityObserved &&
                (sustainedKeyboardOnlyWait || lowRateHaltLoop) &&
                programCounter >= 0x4000 &&
                tape.IsActivelyDrivingEarLine &&
                !hasPendingContinuation;
            if (!loaderHasRelinquishedTape)
            {
                HandoffFrames = 0;
                return false;
            }

            HandoffFrames++;
            if (HandoffFrames < requiredHandoffFrames)
                return false;

            bool stopped = tape.PausePlayback(currentTStates);
            if (stopped)
            {
                AutoPausedAtHandoff = true;
                AutomaticResumeReads = 0;
            }

            HandoffFrames = 0;
            return stopped;
        }

        public void Reset()
        {
            PortReadCount = 0;
            PortReadCountAtFrameStart = 0;
            KeyboardOnlyPortReadCount = 0;
            KeyboardOnlyPortReadCountAtFrameStart = 0;
            LoaderActivityObserved = false;
            HandoffFrames = 0;
            AutoPausedAtHandoff = false;
            AutomaticResumeReads = 0;
        }

        public QuickState CaptureQuickState() => new QuickState(
            PortReadCount,
            PortReadCountAtFrameStart,
            KeyboardOnlyPortReadCount,
            KeyboardOnlyPortReadCountAtFrameStart,
            LoaderActivityObserved,
            HandoffFrames,
            AutoPausedAtHandoff,
            AutomaticResumeReads);

        public void RestoreQuickState(QuickState state)
        {
            PortReadCount = state.PortReadCount;
            PortReadCountAtFrameStart = state.PortReadCountAtFrameStart;
            KeyboardOnlyPortReadCount = state.KeyboardOnlyPortReadCount;
            KeyboardOnlyPortReadCountAtFrameStart = state.KeyboardOnlyPortReadCountAtFrameStart;
            LoaderActivityObserved = state.LoaderActivityObserved;
            HandoffFrames = state.HandoffFrames;
            AutoPausedAtHandoff = state.AutoPausedAtHandoff;
            AutomaticResumeReads = state.AutomaticResumeReads;
        }
    }
}
