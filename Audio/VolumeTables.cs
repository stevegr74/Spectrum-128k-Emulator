namespace Spectrum128kEmulator.Audio
{
    internal static class VolumeTables
    {
        // Simple logarithmic-ish 16-step AY level table scaled for 16-bit PCM mixing.
        private static readonly short[] AyLevels =
        {
            0,
            60,
            85,
            120,
            170,
            240,
            340,
            480,
            680,
            960,
            1360,
            1920,
            2720,
            3840,
            5440,
            7680
        };

        public static short GetAyAmplitude(byte volumeRegister)
        {
            int level = volumeRegister & 0x0F;
            return GetAyAmplitudeFromLevel(level);
        }

        public static short GetAyAmplitudeFromLevel(int level)
        {
            if ((uint)level >= AyLevels.Length)
                throw new System.ArgumentOutOfRangeException(nameof(level));

            return AyLevels[level];
        }
    }
}
