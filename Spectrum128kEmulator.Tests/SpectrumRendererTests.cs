using System.Drawing;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumRendererTests
    {
        [Fact]
        public void RenderToBitmap_Renders_Flash_And_NonFlash_Correctly()
        {
            using var bitmap = new Bitmap(
                Spectrum128Machine.ScreenWidth,
                Spectrum128Machine.ScreenHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            byte[] screenRam = new byte[0x1B00];

            // Top-left 8-pixel cell, first scanline:
            // 1000 0000 -> first pixel on, remaining 7 off
            screenRam[0] = 0x80;

            // Attribute for top-left cell:
            // FLASH=1, BRIGHT=0, PAPER=1 (blue), INK=7 (white)
            screenRam[0x1800] = (byte)(0x80 | (1 << 3) | 7);

            SpectrumRenderer.RenderToBitmap(bitmap, screenRam, borderColor: 0, flashPhase: false);

            Color litNormal = bitmap.GetPixel(0, 0);
            Color unlitNormal = bitmap.GetPixel(1, 0);

            Assert.Equal(SpectrumRenderer.GetSpectrumColor(7, false).ToArgb(), litNormal.ToArgb());
            Assert.Equal(SpectrumRenderer.GetSpectrumColor(1, false).ToArgb(), unlitNormal.ToArgb());

            SpectrumRenderer.RenderToBitmap(bitmap, screenRam, borderColor: 0, flashPhase: true);

            Color litFlash = bitmap.GetPixel(0, 0);
            Color unlitFlash = bitmap.GetPixel(1, 0);

            Assert.Equal(SpectrumRenderer.GetSpectrumColor(1, false).ToArgb(), litFlash.ToArgb());
            Assert.Equal(SpectrumRenderer.GetSpectrumColor(7, false).ToArgb(), unlitFlash.ToArgb());
        }

        [Fact]
        public void RenderToBitmap_Does_Not_Swap_When_Flash_Bit_Is_Clear()
        {
            using var bitmap = new Bitmap(
                Spectrum128Machine.ScreenWidth,
                Spectrum128Machine.ScreenHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            byte[] screenRam = new byte[0x1B00];
            screenRam[0] = 0x80;

            // FLASH=0, BRIGHT=0, PAPER=1, INK=7
            screenRam[0x1800] = (byte)((1 << 3) | 7);

            SpectrumRenderer.RenderToBitmap(bitmap, screenRam, borderColor: 0, flashPhase: false);
            Color litNormal = bitmap.GetPixel(0, 0);
            Color unlitNormal = bitmap.GetPixel(1, 0);

            SpectrumRenderer.RenderToBitmap(bitmap, screenRam, borderColor: 0, flashPhase: true);
            Color litFlash = bitmap.GetPixel(0, 0);
            Color unlitFlash = bitmap.GetPixel(1, 0);

            Assert.Equal(litNormal.ToArgb(), litFlash.ToArgb());
            Assert.Equal(unlitNormal.ToArgb(), unlitFlash.ToArgb());
        }
    }
}