using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator.Audio
{
    public readonly struct AyRegisterWrite
    {
        public AyRegisterWrite(int tState, byte register, byte value)
        {
            if (tState < 0)
                throw new ArgumentOutOfRangeException(nameof(tState));

            TState = tState;
            Register = (byte)(register & 0x0F);
            Value = value;
        }

        public int TState { get; }
        public byte Register { get; }
        public byte Value { get; }
    }

    public sealed class AudioFrame
    {
        private readonly BeeperEvent[] beeperEvents;
        private readonly AyRegisterWrite[] ayWrites;

        public AudioFrame(
            int frameTStates,
            int cpuClockHz,
            bool initialSpeakerHigh,
            bool finalSpeakerHigh,
            IReadOnlyList<BeeperEvent> beeperEvents,
            AyAudioState? ayState = null,
            AyAudioState? initialAyState = null,
            IReadOnlyList<AyRegisterWrite>? ayWrites = null)
        {
            if (frameTStates <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameTStates));
            if (cpuClockHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(cpuClockHz));

            this.beeperEvents = beeperEvents == null
                ? throw new ArgumentNullException(nameof(beeperEvents))
                : CopyEvents(beeperEvents);

            this.ayWrites = ayWrites == null
                ? Array.Empty<AyRegisterWrite>()
                : CopyWrites(ayWrites);

            FrameTStates = frameTStates;
            CpuClockHz = cpuClockHz;
            InitialSpeakerHigh = initialSpeakerHigh;
            FinalSpeakerHigh = finalSpeakerHigh;
            AyState = ayState;
            InitialAyState = initialAyState;
        }

        public int FrameTStates { get; }
        public int CpuClockHz { get; }
        public bool InitialSpeakerHigh { get; }
        public bool FinalSpeakerHigh { get; }
        public IReadOnlyList<BeeperEvent> BeeperEvents => beeperEvents;
        public AyAudioState? AyState { get; }
        public AyAudioState? InitialAyState { get; }
        public IReadOnlyList<AyRegisterWrite> AyWrites => ayWrites;

        private static BeeperEvent[] CopyEvents(IReadOnlyList<BeeperEvent> source)
        {
            if (source.Count == 0)
                return Array.Empty<BeeperEvent>();

            var copy = new BeeperEvent[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];

            return copy;
        }

        private static AyRegisterWrite[] CopyWrites(IReadOnlyList<AyRegisterWrite> source)
        {
            if (source.Count == 0)
                return Array.Empty<AyRegisterWrite>();

            var copy = new AyRegisterWrite[source.Count];
            for (int i = 0; i < source.Count; i++)
                copy[i] = source[i];

            return copy;
        }
    }
}
