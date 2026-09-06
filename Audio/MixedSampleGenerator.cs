using System;

namespace Spectrum128kEmulator.Audio
{
    public sealed class MixedSampleGenerator
    {
        private readonly BeeperSampleGenerator beeperGenerator;
        private readonly AySampleGenerator ayGenerator;
        private readonly TStateSampleClock sampleClock;

        public MixedSampleGenerator(uint sampleRate)
        {
            if (sampleRate == 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));

            SampleRate = sampleRate;
            beeperGenerator = new BeeperSampleGenerator(sampleRate);
            ayGenerator = new AySampleGenerator(sampleRate);
            sampleClock = new TStateSampleClock(sampleRate);
        }

        public uint SampleRate { get; }

        public short[] GenerateFrameSamples(AudioFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            int sampleCount = sampleClock.Consume(frame.FrameTStates, frame.CpuClockHz);
            bool beeperSilent = !frame.InitialSpeakerHigh && !frame.FinalSpeakerHigh && frame.BeeperEvents.Count == 0;
            if (beeperSilent)
                return ayGenerator.GenerateFrameSamples(frame, sampleCount);

            short[] mixedSamples = beeperGenerator.GenerateFrameSamples(frame, sampleCount);
            if (frame.AyState == null)
                return mixedSamples;

            ayGenerator.MixFrameSamples(frame, mixedSamples, sampleCount);
            return mixedSamples;
        }
    }
}
