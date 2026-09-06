using System;

namespace Spectrum128kEmulator
{
    public sealed class SpectrumFrameBuffer
    {
        private static readonly uint[] NormalPalette =
        {
            0xFF000000, 0xFF0000D7, 0xFFD70000, 0xFFD700D7,
            0xFF00D700, 0xFF00D7D7, 0xFFD7D700, 0xFFD7D7D7
        };

        private static readonly uint[] BrightPalette =
        {
            0xFF000000, 0xFF0000FF, 0xFFFF0000, 0xFFFF00FF,
            0xFF00FF00, 0xFF00FFFF, 0xFFFFFF00, 0xFFFFFFFF
        };

        public const int Width = Spectrum128Machine.ScreenWidth;
        public const int Height = Spectrum128Machine.ScreenHeight;

        private readonly uint[] pixels = new uint[Width * Height];

        public ReadOnlyMemory<uint> Pixels => pixels;

        public void Render(byte[] screenRam, int borderColor, bool flashPhase)
        {
            Render(screenRam, new BorderFrame(Spectrum128Machine.FrameTStates128, borderColor, Array.Empty<BorderEvent>()), flashPhase);
        }

        public void Render(byte[] screenRam, BorderFrame borderFrame, bool flashPhase)
        {
            if (screenRam == null)
                throw new ArgumentNullException(nameof(screenRam));
            if (borderFrame == null)
                throw new ArgumentNullException(nameof(borderFrame));
            if (screenRam.Length < 0x1B00)
                throw new ArgumentException("Screen RAM must contain at least 0x1B00 bytes.", nameof(screenRam));

            RenderBorder(borderFrame);

            for (int y = 0; y < Spectrum128Machine.ActiveDisplayHeight; y++)
            {
                int rowOffset = (y + Spectrum128Machine.BorderTopHeight) * Width + Spectrum128Machine.BorderLeftWidth;
                int charRow = y >> 3;
                int charLine = y & 7;

                for (int x = 0; x < Spectrum128Machine.ActiveDisplayWidth; x += 8)
                {
                    int column = x >> 3;
                    int pixelOffset = ((charRow & 0x18) << 8) |
                                      ((charRow & 0x07) << 5) |
                                      (charLine << 8) |
                                      column;
                    int attrOffset = 0x1800 + (charRow * 32) + column;

                    byte pixelByte = screenRam[pixelOffset];
                    byte attr = screenRam[attrOffset];
                    int paper = (attr >> 3) & 0x07;
                    int ink = attr & 0x07;

                    if ((attr & 0x80) != 0 && flashPhase)
                        (ink, paper) = (paper, ink);

                    uint inkArgb = GetSpectrumColorArgb(ink, (attr & 0x40) != 0);
                    uint paperArgb = GetSpectrumColorArgb(paper, (attr & 0x40) != 0);
                    int pixelIndex = rowOffset + x;

                    pixels[pixelIndex + 0] = (pixelByte & 0x80) != 0 ? inkArgb : paperArgb;
                    pixels[pixelIndex + 1] = (pixelByte & 0x40) != 0 ? inkArgb : paperArgb;
                    pixels[pixelIndex + 2] = (pixelByte & 0x20) != 0 ? inkArgb : paperArgb;
                    pixels[pixelIndex + 3] = (pixelByte & 0x10) != 0 ? inkArgb : paperArgb;
                    pixels[pixelIndex + 4] = (pixelByte & 0x08) != 0 ? inkArgb : paperArgb;
                    pixels[pixelIndex + 5] = (pixelByte & 0x04) != 0 ? inkArgb : paperArgb;
                    pixels[pixelIndex + 6] = (pixelByte & 0x02) != 0 ? inkArgb : paperArgb;
                    pixels[pixelIndex + 7] = (pixelByte & 0x01) != 0 ? inkArgb : paperArgb;
                }
            }
        }

        private void RenderBorder(BorderFrame borderFrame)
        {
            int lineTStates = borderFrame.FrameTStates == Spectrum128Machine.FrameTStates48 ? 224 : 228;
            int activeDisplayStart = borderFrame.FrameTStates == Spectrum128Machine.FrameTStates48 ? 14347 : 14361;
            int firstVisibleTState = activeDisplayStart -
                                     (Spectrum128Machine.BorderTopHeight * lineTStates) -
                                     ((Spectrum128Machine.BorderLeftWidth * lineTStates) / Width);
            int eventIndex = 0;
            int color = borderFrame.InitialColor;
            IReadOnlyList<BorderEvent> events = borderFrame.Events;

            for (int y = 0; y < Height; y++)
            {
                int rowOffset = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    int tState = firstVisibleTState + (y * lineTStates) + ((x * lineTStates) / Width);
                    while (eventIndex < events.Count && events[eventIndex].TStateOffset <= tState)
                    {
                        color = events[eventIndex].Color;
                        eventIndex++;
                    }

                    pixels[rowOffset + x] = GetSpectrumColorArgb(color, bright: false);
                }
            }
        }

        public static uint GetSpectrumColorArgb(int color, bool bright)
        {
            color &= 0x07;
            return bright ? BrightPalette[color] : NormalPalette[color];
        }
    }
}
