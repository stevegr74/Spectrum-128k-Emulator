using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Spectrum128kEmulator
{
    public static class SpectrumRenderer
    {
        private static readonly int[] NormalPalette =
        {
            unchecked((int)0xFF000000), // black
            unchecked((int)0xFF0000D7), // blue
            unchecked((int)0xFFD70000), // red
            unchecked((int)0xFFD700D7), // magenta
            unchecked((int)0xFF00D700), // green
            unchecked((int)0xFF00D7D7), // cyan
            unchecked((int)0xFFD7D700), // yellow
            unchecked((int)0xFFD7D7D7)  // white
        };

        private static readonly int[] BrightPalette =
        {
            unchecked((int)0xFF000000), // black
            unchecked((int)0xFF0000FF), // blue
            unchecked((int)0xFFFF0000), // red
            unchecked((int)0xFFFF00FF), // magenta
            unchecked((int)0xFF00FF00), // green
            unchecked((int)0xFF00FFFF), // cyan
            unchecked((int)0xFFFFFF00), // yellow
            unchecked((int)0xFFFFFFFF)  // white
        };

        public static void RenderToBitmap(Bitmap bitmap, byte[] screenRam, int borderColor, bool flashPhase)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));
            if (screenRam == null)
                throw new ArgumentNullException(nameof(screenRam));
            if (screenRam.Length < 0x1B00)
                throw new ArgumentException("Screen RAM must contain at least 0x1B00 bytes.", nameof(screenRam));
            if (bitmap.Width != Spectrum128Machine.ScreenWidth || bitmap.Height != Spectrum128Machine.ScreenHeight)
                throw new ArgumentException("Bitmap must match Spectrum screen dimensions.", nameof(bitmap));
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ArgumentException("Bitmap must use PixelFormat.Format32bppArgb.", nameof(bitmap));

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);

            try
            {
                unsafe
                {
                    int* basePtr = (int*)data.Scan0;
                    int stridePixels = data.Stride / 4;

                    int borderArgb = GetSpectrumColorArgb(borderColor & 0x07, false);

                    for (int y = 0; y < 192; y++)
                    {
                        int* rowPtr = basePtr + (y * stridePixels);

                        int charRow = y >> 3;
                        int charLine = y & 7;

                        for (int x = 0; x < 256; x += 8)
                        {
                            int column = x >> 3;

                            int pixelOffset = ((charRow & 0x18) << 8) |
                                              ((charRow & 0x07) << 5) |
                                              (charLine << 8) |
                                              column;

                            int attrOffset = 0x1800 + (charRow * 32) + column;

                            byte pixelByte = screenRam[pixelOffset];
                            byte attr = screenRam[attrOffset];

                            bool flash = (attr & 0x80) != 0;
                            bool bright = (attr & 0x40) != 0;

                            int paper = (attr >> 3) & 0x07;
                            int ink = attr & 0x07;

                            if (flash && flashPhase)
                            {
                                int temp = ink;
                                ink = paper;
                                paper = temp;
                            }

                            int inkArgb = GetSpectrumColorArgb(ink, bright);
                            int paperArgb = GetSpectrumColorArgb(paper, bright);

                            rowPtr[x + 0] = (pixelByte & 0x80) != 0 ? inkArgb : paperArgb;
                            rowPtr[x + 1] = (pixelByte & 0x40) != 0 ? inkArgb : paperArgb;
                            rowPtr[x + 2] = (pixelByte & 0x20) != 0 ? inkArgb : paperArgb;
                            rowPtr[x + 3] = (pixelByte & 0x10) != 0 ? inkArgb : paperArgb;
                            rowPtr[x + 4] = (pixelByte & 0x08) != 0 ? inkArgb : paperArgb;
                            rowPtr[x + 5] = (pixelByte & 0x04) != 0 ? inkArgb : paperArgb;
                            rowPtr[x + 6] = (pixelByte & 0x02) != 0 ? inkArgb : paperArgb;
                            rowPtr[x + 7] = (pixelByte & 0x01) != 0 ? inkArgb : paperArgb;
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static Color GetSpectrumColor(int color, bool bright)
        {
            return Color.FromArgb(GetSpectrumColorArgb(color, bright));
        }

        private static int GetSpectrumColorArgb(int color, bool bright)
        {
            color &= 0x07;
            return bright ? BrightPalette[color] : NormalPalette[color];
        }
    }
}
