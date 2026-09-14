using System;
using System.IO;
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
            pipeline.SubmitFrame(frame);

            Assert.NotNull(output.LastSamples);
            Assert.Equal(881, output.LastSamples!.Length);
            Assert.Equal(1761, output.TotalSamplesWritten);
            Assert.Contains(output.LastSamples, sample => sample != 0);
        }

        [Fact]
        public void Machine_HasAudibleOutput_RequiresAnEnabledAyChannelWithVolume()
        {
            string romFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(romFolder);
            File.WriteAllBytes(Path.Combine(romFolder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(romFolder, "128-1.rom"), new byte[16384]);

            try
            {
                var machine = new Spectrum128Machine(romFolder);
                Assert.False(machine.HasAudibleOutput);

                machine.Ay.SelectRegister(7);
                machine.Ay.WriteRegister(0b0011_1110);
                machine.Ay.SelectRegister(8);
                machine.Ay.WriteRegister(0x0F);
                Assert.True(machine.HasAudibleOutput);

                machine.Ay.SelectRegister(7);
                machine.Ay.WriteRegister(0b0011_1111);
                Assert.False(machine.HasAudibleOutput);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private sealed class RecordingAudioOutput : IAudioOutput
        {
            public RecordingAudioOutput(uint sampleRate)
            {
                SampleRate = sampleRate;
            }

            public uint SampleRate { get; }
            public short[]? LastSamples { get; private set; }
            public int TotalSamplesWritten { get; private set; }

            public void WriteSamples(short[] monoSamples)
            {
                LastSamples = monoSamples ?? throw new ArgumentNullException(nameof(monoSamples));
                TotalSamplesWritten += monoSamples.Length;
            }

            public void Dispose()
            {
            }
        }
    }
}
