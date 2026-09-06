using Spectrum128kEmulator.Audio;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class TStateSampleClockTests
    {
        [Fact]
        public void Consume_ProducesTheSameSampleCountForSplitOrCombinedTStates()
        {
            const uint sampleRate = 44100;
            const int cpuClockHz = Spectrum128Machine.CpuClockHz128;
            var splitClock = new TStateSampleClock(sampleRate);
            var combinedClock = new TStateSampleClock(sampleRate);

            int splitSamples = splitClock.Consume(1_001, cpuClockHz) +
                               splitClock.Consume(2_003, cpuClockHz) +
                               splitClock.Consume(3_007, cpuClockHz);
            int combinedSamples = combinedClock.Consume(6_011, cpuClockHz);

            Assert.Equal(combinedSamples, splitSamples);
        }

        [Fact]
        public void Consume_DoesNotRoundEachShortSliceUpToOneSample()
        {
            var clock = new TStateSampleClock(44100);

            Assert.Equal(0, clock.Consume(1, Spectrum128Machine.CpuClockHz128));
            Assert.Equal(0, clock.Consume(1, Spectrum128Machine.CpuClockHz128));
        }
    }
}
