using System;

namespace Spectrum128kEmulator.Audio
{
    public interface IAudioOutput : IDisposable
    {
        uint SampleRate { get; }
        void WriteSamples(short[] monoSamples);
    }
}
