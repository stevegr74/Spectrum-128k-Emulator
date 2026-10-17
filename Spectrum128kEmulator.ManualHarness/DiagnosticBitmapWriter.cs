namespace Spectrum128kEmulator.ManualHarness
{
    internal static class DiagnosticBitmapWriter
    {
        private const int FileHeaderSize = 14;
        private const int InfoHeaderSize = 40;

        public static void Write(string path, ReadOnlySpan<uint> argbPixels, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A bitmap path is required.", nameof(path));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (argbPixels.Length != checked(width * height))
                throw new ArgumentException("Pixel count must match the bitmap dimensions.", nameof(argbPixels));

            int pixelDataSize = checked(width * height * sizeof(uint));
            int pixelDataOffset = FileHeaderSize + InfoHeaderSize;
            int fileSize = checked(pixelDataOffset + pixelDataSize);

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new BinaryWriter(stream);
            writer.Write((ushort)0x4D42);
            writer.Write(fileSize);
            writer.Write(0);
            writer.Write(pixelDataOffset);

            writer.Write(InfoHeaderSize);
            writer.Write(width);
            writer.Write(-height); // Negative height stores rows in top-down display order.
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(0);
            writer.Write(pixelDataSize);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            foreach (uint pixel in argbPixels)
                writer.Write(pixel);
        }
    }
}
