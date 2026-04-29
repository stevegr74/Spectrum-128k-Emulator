using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator.Audio
{
    public sealed class BeeperSampleGenerator
    {
        private readonly short highAmplitude;
        private readonly short lowAmplitude;

        public BeeperSampleGenerator(uint sampleRate = 44100, short amplitude = 6000)
        {
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (amplitude <= 0)
                throw new ArgumentOutOfRangeException(nameof(amplitude));

            SampleRate = sampleRate;
            highAmplitude = amplitude;
            lowAmplitude = (short)-amplitude;
        }

        public uint SampleRate { get; }

        public short[] GenerateFrameSamples(AudioFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            int sampleCount = Math.Max(1, (int)Math.Round((double)frame.FrameTStates * SampleRate / frame.CpuClockHz));
            short[] samples = new short[sampleCount];

            bool speakerHigh = frame.InitialSpeakerHigh;
            int eventIndex = 0;
            IReadOnlyList<BeeperEvent> events = frame.BeeperEvents;

            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                int tStateOffset = (int)(((long)sampleIndex * frame.FrameTStates) / sampleCount);

                while (eventIndex < events.Count && events[eventIndex].TStateOffset <= tStateOffset)
                {
                    speakerHigh = events[eventIndex].SpeakerHigh;
                    eventIndex++;
                }

                samples[sampleIndex] = speakerHigh ? highAmplitude : lowAmplitude;
            }

            return samples;
        }
    }
}
