namespace Spectrum128kEmulator
{
    public sealed class UlaFrame
    {
        internal const int CellsPerLine = Spectrum128Machine.ActiveDisplayWidth / 8;
        internal const int CellCount = CellsPerLine * Spectrum128Machine.ActiveDisplayHeight;

        private readonly byte[] bitmapBytes;
        private readonly byte[] attributeBytes;

        internal UlaFrame(int frameTStates, byte[] bitmapBytes, byte[] attributeBytes)
        {
            if (bitmapBytes.Length != CellCount)
                throw new ArgumentException("Bitmap capture has an invalid length.", nameof(bitmapBytes));
            if (attributeBytes.Length != CellCount)
                throw new ArgumentException("Attribute capture has an invalid length.", nameof(attributeBytes));

            FrameTStates = frameTStates;
            this.bitmapBytes = (byte[])bitmapBytes.Clone();
            this.attributeBytes = (byte[])attributeBytes.Clone();
        }

        public int FrameTStates { get; }

        internal byte GetBitmapByte(int line, int column) =>
            bitmapBytes[(line * CellsPerLine) + column];

        internal byte GetAttributeByte(int line, int column) =>
            attributeBytes[(line * CellsPerLine) + column];

        internal byte[] GetBitmapBytesCopy() => (byte[])bitmapBytes.Clone();
        internal byte[] GetAttributeBytesCopy() => (byte[])attributeBytes.Clone();
    }
}
