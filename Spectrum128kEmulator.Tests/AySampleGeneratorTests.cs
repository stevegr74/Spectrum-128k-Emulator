
using System;
using System.Collections.Generic;
using System.Linq;
using Spectrum128kEmulator.Audio;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class AySampleGeneratorTests
    {
        [Fact]
        public void GenerateFrameSamples_ProducesAudibleTone_WhenChannelAEnabled()
        {
            var generator = new AySampleGenerator(44100);
            var ayState = new AyAudioState(new byte[16]
            {
                0x20, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0b00111110,
                0x0F, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00
            });
            var frame = new AudioFrame(Spectrum128Machine.FrameTStates128, false, false, Array.Empty<BeeperEvent>(), ayState);

            short[] samples = generator.GenerateFrameSamples(frame);

            Assert.Contains(samples, s => s > 0);
            Assert.Contains(samples, s => s == 0);
        }

        [Fact]
        public void GenerateFrameSamples_ReturnsSilence_WhenToneDisabledInMixer()
        {
            var generator = new AySampleGenerator(44100);
            var ayState = new AyAudioState(new byte[16]
            {
                0x20, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0b00111111,
                0x0F, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00
            });
            var frame = new AudioFrame(Spectrum128Machine.FrameTStates128, false, false, Array.Empty<BeeperEvent>(), ayState);

            short[] samples = generator.GenerateFrameSamples(frame);

            Assert.True(samples.All(s => s == 0));
        }

        [Fact]
        public void GenerateFrameSamples_UsesEnvelopeLevels_WhenEnvelopeVolumeEnabled()
        {
            var generator = new AySampleGenerator(44100);
            var ayState = new AyAudioState(new byte[16]
            {
                0x10, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0b00111110,
                0x10, 0x00, 0x00,
                0x01, 0x00, 0x0C, 0x00, 0x00
            });
            var frame = new AudioFrame(Spectrum128Machine.FrameTStates128, false, false, Array.Empty<BeeperEvent>(), ayState);

            short[] samples = generator.GenerateFrameSamples(frame);
            int distinctLevels = samples.Where(sample => sample > 0).Distinct().Count();

            Assert.True(distinctLevels > 4);
        }

        [Fact]
        public void GenerateFrameSamples_HoldsLow_ForOneShotDecayEnvelope()
        {
            var generator = new AySampleGenerator(44100);
            var ayState = new AyAudioState(new byte[16]
            {
                0x10, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0b00111110,
                0x10, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00, 0x00
            });
            var frame = new AudioFrame(Spectrum128Machine.FrameTStates128, false, false, Array.Empty<BeeperEvent>(), ayState);

            short[] samples = generator.GenerateFrameSamples(frame);
            short[] tail = samples.Skip(Math.Max(0, samples.Length - 64)).ToArray();

            Assert.All(tail, sample => Assert.Equal((short)0, sample));
        }

        [Fact]
        public void GenerateFrameSamples_HoldsHigh_ForOneShotAttackEnvelope()
        {
            var generator = new AySampleGenerator(44100);
            var ayState = new AyAudioState(new byte[16]
            {
                0x10, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0b00111110,
                0x10, 0x00, 0x00,
                0x01, 0x00, 0x04, 0x00, 0x00
            });
            var frame = new AudioFrame(Spectrum128Machine.FrameTStates128, false, false, Array.Empty<BeeperEvent>(), ayState);

            short[] samples = generator.GenerateFrameSamples(frame);
            short[] tail = samples.Skip(Math.Max(0, samples.Length - 64)).ToArray();

            Assert.Contains(tail, sample => sample == 7680);
            Assert.All(tail, sample => Assert.True(sample == 0 || sample == 7680));
        }

        [Fact]
        public void GenerateFrameSamples_ProducesAudibleNoise_WhenNoiseEnabledAndToneDisabled()
        {
            var generator = new AySampleGenerator(44100);
            var ayState = new AyAudioState(new byte[16]
            {
                0x00, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x01,
                0b00110111,
                0x0F, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00
            });
            var frame = new AudioFrame(Spectrum128Machine.FrameTStates128, false, false, Array.Empty<BeeperEvent>(), ayState);

            short[] samples = generator.GenerateFrameSamples(frame);

            Assert.Contains(samples, s => s > 0);
            Assert.Contains(samples, s => s == 0);
        }

        [Fact]
        public void GenerateFrameSamples_AppliesMidFrameToneWrite()
        {
            var generator = new AySampleGenerator(44100);
            var initialState = new AyAudioState(new byte[16]
            {
                0x40, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0b00111110,
                0x0F, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00
            });
            var endState = new AyAudioState(new byte[16]
            {
                0x08, 0x00,
                0x00, 0x00,
                0x00, 0x00,
                0x00,
                0b00111110,
                0x0F, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00
            });
            var writes = new List<AyRegisterWrite>
            {
                new AyRegisterWrite(Spectrum128Machine.FrameTStates128 / 2, 0, 0x08)
            };
            var frame = new AudioFrame(
                Spectrum128Machine.FrameTStates128,
                false,
                false,
                Array.Empty<BeeperEvent>(),
                endState,
                initialState,
                writes);

            short[] samples = generator.GenerateFrameSamples(frame);
            int halfway = samples.Length / 2;

            int firstHalfTransitions = CountTransitions(samples.Take(halfway));
            int secondHalfTransitions = CountTransitions(samples.Skip(halfway));

            Assert.True(secondHalfTransitions > firstHalfTransitions);
        }

        [Fact]
        public void AyAudioState_IsImmutable_FromSourceArray()
        {
            byte[] registers = new byte[16];
            registers[0] = 0x34;

            var state = new AyAudioState(registers);
            registers[0] = 0x12;

            Assert.Equal((byte)0x34, state.ReadRegister(0));
        }

        private static int CountTransitions(IEnumerable<short> samples)
        {
            int transitions = 0;
            short? previous = null;

            foreach (short sample in samples)
            {
                if (previous.HasValue && previous.Value != sample)
                    transitions++;

                previous = sample;
            }

            return transitions;
        }
    }
}
