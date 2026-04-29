using System;
using Spectrum128kEmulator.Audio;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class BeeperSampleGeneratorTests
    {
        [Fact]
        public void GenerateFrameSamples_DoesNotInject_LastSampleSpike_ForFrameEndEdge()
        {
            var generator = new BeeperSampleGenerator(44100);
            var frame = new AudioFrame(
                Spectrum128Machine.FrameTStates128,
                Spectrum128Machine.CpuClockHz128,
                false,
                true,
                new[] { new BeeperEvent(Spectrum128Machine.FrameTStates128 - 1, true) });

            short[] samples = generator.GenerateFrameSamples(frame);

            Assert.NotEmpty(samples);
            Assert.Equal((short)-6000, samples[^1]);
        }

        [Fact]
        public void GenerateFrameSamples_Carries_FrameEndState_IntoNextFrame()
        {
            var generator = new BeeperSampleGenerator(44100);
            var frame = new AudioFrame(
                Spectrum128Machine.FrameTStates128,
                Spectrum128Machine.CpuClockHz128,
                true,
                true,
                Array.Empty<BeeperEvent>());

            short[] samples = generator.GenerateFrameSamples(frame);

            Assert.NotEmpty(samples);
            Assert.Equal((short)6000, samples[0]);
        }
    }
}
