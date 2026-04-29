using System;
using Spectrum128kEmulator.Audio;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class AudioPipelineTests
    {
        [Fact]
        public void SubmitFrame_WritesMixedSamples_ToOutput()
        {
            var output = new RecordingAudioOutput(44100);
            using var pipeline = new AudioPipeline(output);

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

            var frame = new AudioFrame(
                Spectrum128Machine.FrameTStates128,
                Spectrum128Machine.CpuClockHz128,
                false,
                true,
                new[] { new BeeperEvent(Spectrum128Machine.FrameTStates128 / 2, true) },
                ayState);

            pipeline.SubmitFrame(frame);

            Assert.NotNull(output.LastSamples);
            Assert.NotEmpty(output.LastSamples!);
            Assert.Contains(output.LastSamples!, sample => sample != 0);
        }

        [Fact]
        public void SubmitFrame_Uses48kCpuClock_For48kAudioFrames()
        {
            var output = new RecordingAudioOutput(44100);
            using var pipeline = new AudioPipeline(output);

            var frame = new AudioFrame(
                Spectrum128Machine.FrameTStates48,
                Spectrum128Machine.CpuClockHz48,
                false,
                false,
                Array.Empty<BeeperEvent>());

            pipeline.SubmitFrame(frame);

            Assert.NotNull(output.LastSamples);
            Assert.Equal(881, output.LastSamples!.Length);
        }

        private sealed class RecordingAudioOutput : IAudioOutput
        {
            public RecordingAudioOutput(uint sampleRate)
            {
                SampleRate = sampleRate;
            }

            public uint SampleRate { get; }
            public short[]? LastSamples { get; private set; }

            public void WriteSamples(short[] monoSamples)
            {
                LastSamples = monoSamples ?? throw new ArgumentNullException(nameof(monoSamples));
            }

            public void Dispose()
            {
            }
        }
    }
}
