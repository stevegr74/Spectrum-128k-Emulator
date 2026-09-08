namespace Spectrum128kEmulator
{
    public enum SpectrumDisplayMode
    {
        Native,
        Enhanced2x,
        Enhanced3x
    }

    public readonly record struct SpectrumDisplayLayout(
        int ClientWidth,
        int ClientHeight,
        int Scale,
        int ViewportX,
        int ViewportY,
        string Label);

    public static class SpectrumDisplayModes
    {
        public static SpectrumDisplayLayout GetLayout(SpectrumDisplayMode mode)
        {
            return mode switch
            {
                SpectrumDisplayMode.Native => new SpectrumDisplayLayout(
                    SpectrumFrameBuffer.Width,
                    SpectrumFrameBuffer.Height,
                    1,
                    0,
                    0,
                    "1x Native"),
                SpectrumDisplayMode.Enhanced2x => new SpectrumDisplayLayout(
                    SpectrumFrameBuffer.Width * 2,
                    SpectrumFrameBuffer.Height * 2,
                    2,
                    0,
                    0,
                    "2x Enhanced"),
                SpectrumDisplayMode.Enhanced3x => new SpectrumDisplayLayout(
                    SpectrumFrameBuffer.Width * 3,
                    720,
                    3,
                    0,
                    0,
                    "3x Enhanced"),
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        public static SpectrumDisplayMode Next(SpectrumDisplayMode mode)
        {
            return mode switch
            {
                SpectrumDisplayMode.Native => SpectrumDisplayMode.Enhanced2x,
                SpectrumDisplayMode.Enhanced2x => SpectrumDisplayMode.Enhanced3x,
                SpectrumDisplayMode.Enhanced3x => SpectrumDisplayMode.Native,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }
    }
}
