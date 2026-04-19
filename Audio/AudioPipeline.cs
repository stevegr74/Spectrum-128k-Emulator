using System;

namespace Spectrum128kEmulator.Audio
{
    public sealed class AudioPipeline : IDisposable
    {
        private readonly MixedSampleGenerator sampleGenerator;
        private readonly IAudioOutput output;
        private bool disposed;

        public AudioPipeline(IAudioOutput output)
        {
            this.output = output ?? throw new ArgumentNullException(nameof(output));
            sampleGenerator = new MixedSampleGenerator(output.SampleRate);
        }

        public uint SampleRate => output.SampleRate;

        public void SubmitFrame(AudioFrame frame)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            short[] samples = sampleGenerator.GenerateFrameSamples(frame);
            if (samples.Length > 0)
                output.WriteSamples(samples);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            output.Dispose();
            disposed = true;
        }
    }
}
