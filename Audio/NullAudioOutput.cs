namespace Spectrum128kEmulator.Audio
{
    public sealed class NullAudioOutput : IAudioOutput
    {
        public NullAudioOutput(uint sampleRate)
        {
            SampleRate = sampleRate;
        }

        public uint SampleRate { get; }

        public void WriteSamples(short[] monoSamples)
        {
        }

        public void Dispose()
        {
        }
    }
}
