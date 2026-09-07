using System;

namespace Spectrum128kEmulator.Audio
{
    public sealed class TStateSampleClock
    {
        private readonly uint sampleRate;
        private int cpuClockHz;
        private ulong remainder;

        public TStateSampleClock(uint sampleRate)
        {
            if (sampleRate == 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));

            this.sampleRate = sampleRate;
        }

        public int Consume(int elapsedTStates, int currentCpuClockHz)
        {
            if (elapsedTStates < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedTStates));
            if (currentCpuClockHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(currentCpuClockHz));

            if (cpuClockHz != 0 && cpuClockHz != currentCpuClockHz)
                remainder = 0;

            cpuClockHz = currentCpuClockHz;
            ulong scaledTStates = remainder + ((ulong)elapsedTStates * sampleRate);
            int samples = checked((int)(scaledTStates / (uint)cpuClockHz));
            remainder = scaledTStates % (uint)cpuClockHz;
            return samples;
        }
    }
}
