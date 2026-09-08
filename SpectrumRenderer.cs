using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Spectrum128kEmulator
{
    public static class SpectrumRenderer
    {
        public static void RenderToBitmap(Bitmap bitmap, byte[] screenRam, int borderColor, bool flashPhase)
        {
            var frameBuffer = new SpectrumFrameBuffer();
            frameBuffer.Render(screenRam, borderColor, flashPhase);
            RenderToBitmap(bitmap, frameBuffer);
        }

        public static void RenderToBitmap(Bitmap bitmap, SpectrumFrameBuffer frameBuffer)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));
            if (frameBuffer == null)
                throw new ArgumentNullException(nameof(frameBuffer));
            if (bitmap.Width != SpectrumFrameBuffer.Width || bitmap.Height != SpectrumFrameBuffer.Height)
                throw new ArgumentException("Bitmap must match Spectrum screen dimensions.", nameof(bitmap));
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ArgumentException("Bitmap must use PixelFormat.Format32bppArgb.", nameof(bitmap));

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                unsafe
                {
                    uint* destination = (uint*)data.Scan0;
                    int stridePixels = data.Stride / sizeof(uint);
                    ReadOnlySpan<uint> source = frameBuffer.Pixels.Span;

                    for (int y = 0; y < SpectrumFrameBuffer.Height; y++)
                    {
                        uint* row = destination + (y * stridePixels);
                        source.Slice(y * SpectrumFrameBuffer.Width, SpectrumFrameBuffer.Width).CopyTo(
                            new Span<uint>(row, SpectrumFrameBuffer.Width));
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static void RenderScaledToBitmap(Bitmap bitmap, SpectrumFrameBuffer frameBuffer, int scale)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));
            if (frameBuffer == null)
                throw new ArgumentNullException(nameof(frameBuffer));
            if (scale is < 1 or > 3)
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (bitmap.Width != SpectrumFrameBuffer.Width * scale || bitmap.Height != SpectrumFrameBuffer.Height * scale)
                throw new ArgumentException("Bitmap must match the scaled Spectrum screen dimensions.", nameof(bitmap));
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ArgumentException("Bitmap must use PixelFormat.Format32bppArgb.", nameof(bitmap));

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            try
            {
                unsafe
                {
                    uint* destination = (uint*)data.Scan0;
                    int stridePixels = data.Stride / sizeof(uint);
                    var destinationPixels = new Span<uint>(destination, stridePixels * bitmap.Height);
                    SpectrumDisplayScaler.Scale(
                        frameBuffer.Pixels.Span,
                        SpectrumFrameBuffer.Width,
                        SpectrumFrameBuffer.Height,
                        destinationPixels,
                        stridePixels,
                        scale);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        public static Color GetSpectrumColor(int color, bool bright) =>
            Color.FromArgb(unchecked((int)SpectrumFrameBuffer.GetSpectrumColorArgb(color, bright)));
    }
}
