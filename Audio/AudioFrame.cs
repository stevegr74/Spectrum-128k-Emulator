using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator.Audio
{
    public sealed class AudioFrame
    {
        private readonly BeeperEvent[] beeperEvents;

        public AudioFrame(int frameTStates, bool initialSpeakerHigh, bool finalSpeakerHigh, IReadOnlyList<BeeperEvent> beeperEvents, AyAudioState? ayState = null)
        {
            if (frameTStates <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameTStates));

            this.beeperEvents = beeperEvents == null
                ? throw new ArgumentNullException(nameof(beeperEvents))
                : CopyEvents(beeperEvents);

            FrameTStates = frameTStates;
            InitialSpeakerHigh = initialSpeakerHigh;
            FinalSpeakerHigh = finalSpeakerHigh;
            AyState = ayState;
        }

        public int FrameTStates { get; }
        public bool InitialSpeakerHigh { get; }
        public bool FinalSpeakerHigh { get; }
        public IReadOnlyList<BeeperEvent> BeeperEvents => beeperEvents;
        public AyAudioState? AyState { get; }

        private static BeeperEvent[] CopyEvents(IReadOnlyList<BeeperEvent> source)
        {
            if (source.Count == 0)
                return Array.Empty<BeeperEvent>();

            var copy = new BeeperEvent[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];

            return copy;
        }
    }
}
