namespace Spectrum128kEmulator.Tape
{
    /// <summary>
    /// Keeps a live tape stream at real time once it has entered an audible stage.
    /// A protected loader can leave its EAR stream mounted while game code runs;
    /// transient silence must not make that game alternate between real time and turbo.
    /// </summary>
    public sealed class TapeTurboPlaybackPolicy
    {
        private bool hasEnteredAudibleStreamingStage;

        public bool IsTurboPlaybackAllowed(bool isStreamingEarSignal, bool hasAudibleOutput)
        {
            if (isStreamingEarSignal && hasAudibleOutput)
                hasEnteredAudibleStreamingStage = true;

            return isStreamingEarSignal && !hasEnteredAudibleStreamingStage;
        }

        public void Reset()
        {
            hasEnteredAudibleStreamingStage = false;
        }
    }
}
