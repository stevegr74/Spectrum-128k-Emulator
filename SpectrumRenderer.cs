using System.Drawing;

namespace Spectrum128kEmulator
{
    public static class SpectrumRenderer
    {
        public static void RenderToBitmap(Bitmap bitmap, byte[] screenRam, int borderColor)
        {
            using var g = Graphics.FromImage(bitmap);
            g.Clear(GetSpectrumColor(borderColor, false));

            for (int y = 0; y < 192; y++)
            {
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

                    bool bright = (attr & 0x40) != 0;
                    int ink = attr & 0x07;
                    int paper = (attr >> 3) & 0x07;

                    Color inkColor = GetSpectrumColor(ink, bright);
                    Color paperColor = GetSpectrumColor(paper, bright);

                    for (int bit = 0; bit < 8; bit++)
                    {
                        bool on = (pixelByte & (0x80 >> bit)) != 0;
                        bitmap.SetPixel(x + bit, y, on ? inkColor : paperColor);
                    }
                }
            }
        }

        public static Color GetSpectrumColor(int color, bool bright)
        {
            int intensity = bright ? 255 : 192;

            return color switch
            {
                0 => Color.Black,
                1 => Color.FromArgb(0, 0, intensity),
                2 => Color.FromArgb(intensity, 0, 0),
                3 => Color.FromArgb(intensity, 0, intensity),
                4 => Color.FromArgb(0, intensity, 0),
                5 => Color.FromArgb(0, intensity, intensity),
                6 => Color.FromArgb(intensity, intensity, 0),
                7 => Color.FromArgb(intensity, intensity, intensity),
                _ => Color.Black
            };
        }
    }
}
