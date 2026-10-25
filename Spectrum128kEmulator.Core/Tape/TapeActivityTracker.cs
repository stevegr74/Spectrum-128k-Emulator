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
