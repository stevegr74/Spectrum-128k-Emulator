namespace Spectrum128kEmulator
{
    public static class SpectrumDisplayScaler
    {
        public static void Scale(
            ReadOnlySpan<uint> source,
            int sourceWidth,
            int sourceHeight,
            Span<uint> destination,
            int destinationStride,
            int scale)
        {
            if (sourceWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            if (sourceHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceHeight));
            if (scale is < 1 or > 3)
                throw new ArgumentOutOfRangeException(nameof(scale));
            if (source.Length < sourceWidth * sourceHeight)
                throw new ArgumentException("Source does not contain a complete image.", nameof(source));

            int destinationWidth = checked(sourceWidth * scale);
            int destinationHeight = checked(sourceHeight * scale);
            if (destinationStride < destinationWidth || destination.Length < destinationStride * destinationHeight)
                throw new ArgumentException("Destination does not contain a complete scaled image.", nameof(destination));

            for (int y = 0; y < sourceHeight; y++)
            {
                for (int x = 0; x < sourceWidth; x++)
                {
                    uint center = GetPixel(source, sourceWidth, sourceHeight, x, y);
                    if (scale == 1)
                    {
                        destination[(y * destinationStride) + x] = center;
                        continue;
                    }

                    uint above = GetPixel(source, sourceWidth, sourceHeight, x, y - 1);
                    uint left = GetPixel(source, sourceWidth, sourceHeight, x - 1, y);
                    uint right = GetPixel(source, sourceWidth, sourceHeight, x + 1, y);
                    uint below = GetPixel(source, sourceWidth, sourceHeight, x, y + 1);

                    if (scale == 2)
                        WriteScale2x(destination, destinationStride, x, y, above, left, center, right, below);
                    else
                        WriteScale3x(destination, destinationStride, x, y, above, left, center, right, below,
                            GetPixel(source, sourceWidth, sourceHeight, x - 1, y - 1),
                            GetPixel(source, sourceWidth, sourceHeight, x + 1, y - 1),
                            GetPixel(source, sourceWidth, sourceHeight, x - 1, y + 1),
                            GetPixel(source, sourceWidth, sourceHeight, x + 1, y + 1));
                }
            }
        }

        private static void WriteScale2x(Span<uint> destination, int stride, int x, int y, uint above, uint left, uint center, uint right, uint below)
        {
            uint topLeft = center;
            uint topRight = center;
            uint bottomLeft = center;
            uint bottomRight = center;
            if (above != below && left != right)
            {
                topLeft = left == above ? left : center;
                topRight = above == right ? right : center;
                bottomLeft = left == below ? left : center;
                bottomRight = below == right ? right : center;
            }

            int destinationX = x * 2;
            int destinationY = y * 2;
            destination[(destinationY * stride) + destinationX] = topLeft;
            destination[(destinationY * stride) + destinationX + 1] = topRight;
            destination[((destinationY + 1) * stride) + destinationX] = bottomLeft;
            destination[((destinationY + 1) * stride) + destinationX + 1] = bottomRight;
        }

        private static void WriteScale3x(Span<uint> destination, int stride, int x, int y, uint above, uint left, uint center, uint right, uint below, uint upperLeft, uint upperRight, uint lowerLeft, uint lowerRight)
        {
            uint topLeft = center;
            uint top = center;
            uint topRight = center;
            uint middleLeft = center;
            uint middleRight = center;
            uint bottomLeft = center;
            uint bottom = center;
            uint bottomRight = center;

            if (above != below && left != right)
            {
                topLeft = left == above ? left : center;
                top = (left == above && center != upperRight) || (above == right && center != upperLeft) ? above : center;
                topRight = above == right ? right : center;
                middleLeft = (left == above && center != lowerLeft) || (left == below && center != upperLeft) ? left : center;
                middleRight = (above == right && center != lowerRight) || (below == right && center != upperRight) ? right : center;
                bottomLeft = left == below ? left : center;
                bottom = (left == below && center != lowerRight) || (below == right && center != lowerLeft) ? below : center;
                bottomRight = below == right ? right : center;
            }

            int destinationX = x * 3;
            int destinationY = y * 3;
            destination[(destinationY * stride) + destinationX] = topLeft;
            destination[(destinationY * stride) + destinationX + 1] = top;
            destination[(destinationY * stride) + destinationX + 2] = topRight;
            destination[((destinationY + 1) * stride) + destinationX] = middleLeft;
            destination[((destinationY + 1) * stride) + destinationX + 1] = center;
            destination[((destinationY + 1) * stride) + destinationX + 2] = middleRight;
            destination[((destinationY + 2) * stride) + destinationX] = bottomLeft;
            destination[((destinationY + 2) * stride) + destinationX + 1] = bottom;
            destination[((destinationY + 2) * stride) + destinationX + 2] = bottomRight;
        }

        private static uint GetPixel(ReadOnlySpan<uint> source, int width, int height, int x, int y)
        {
            int clampedX = Math.Clamp(x, 0, width - 1);
            int clampedY = Math.Clamp(y, 0, height - 1);
            return source[(clampedY * width) + clampedX];
        }
    }
}
