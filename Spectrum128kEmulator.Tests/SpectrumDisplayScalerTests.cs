using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumDisplayScalerTests
    {
        [Fact]
        public void NativeScale_CopiesPixelsWithoutModification()
        {
            uint[] source = { 1, 2, 3, 4 };
            uint[] destination = new uint[4];

            SpectrumDisplayScaler.Scale(source, 2, 2, destination, 2, scale: 1);

            Assert.Equal(source, destination);
        }

        [Fact]
        public void Scale2x_SmoothsSupportedDiagonalEdge()
        {
            uint[] source =
            {
                0, 1, 0,
                1, 2, 0,
                0, 0, 0
            };
            uint[] destination = new uint[6 * 6];

            SpectrumDisplayScaler.Scale(source, 3, 3, destination, 6, scale: 2);

            Assert.Equal(1u, destination[(2 * 6) + 2]);
            Assert.Equal(2u, destination[(2 * 6) + 3]);
            Assert.Equal(2u, destination[(3 * 6) + 2]);
            Assert.Equal(0u, destination[(3 * 6) + 3]);
        }

        [Fact]
        public void Scale3x_SmoothsSupportedDiagonalEdge()
        {
            uint[] source =
            {
                0, 1, 0,
                1, 2, 0,
                0, 0, 0
            };
            uint[] destination = new uint[9 * 9];

            SpectrumDisplayScaler.Scale(source, 3, 3, destination, 9, scale: 3);

            Assert.Equal(1u, destination[(3 * 9) + 3]);
            Assert.Equal(1u, destination[(3 * 9) + 4]);
            Assert.Equal(2u, destination[(3 * 9) + 5]);
            Assert.Equal(1u, destination[(4 * 9) + 3]);
            Assert.Equal(2u, destination[(4 * 9) + 4]);
            Assert.Equal(0u, destination[(4 * 9) + 5]);
            Assert.Equal(2u, destination[(5 * 9) + 3]);
            Assert.Equal(0u, destination[(5 * 9) + 4]);
            Assert.Equal(0u, destination[(5 * 9) + 5]);
        }

        [Fact]
        public void DisplayModes_UseExactAspectPreservingLayouts()
        {
            SpectrumDisplayLayout native = SpectrumDisplayModes.GetLayout(SpectrumDisplayMode.Native);
            SpectrumDisplayLayout enhanced2x = SpectrumDisplayModes.GetLayout(SpectrumDisplayMode.Enhanced2x);
            SpectrumDisplayLayout enhanced3x = SpectrumDisplayModes.GetLayout(SpectrumDisplayMode.Enhanced3x);

            Assert.Equal((320, 240, 1, 0, 0), (native.ClientWidth, native.ClientHeight, native.Scale, native.ViewportX, native.ViewportY));
            Assert.Equal((640, 480, 2, 0, 0), (enhanced2x.ClientWidth, enhanced2x.ClientHeight, enhanced2x.Scale, enhanced2x.ViewportX, enhanced2x.ViewportY));
            Assert.Equal((960, 720, 3, 0, 0), (enhanced3x.ClientWidth, enhanced3x.ClientHeight, enhanced3x.Scale, enhanced3x.ViewportX, enhanced3x.ViewportY));
        }

        [Fact]
        public void DisplayModes_CycleInPresentationOrder()
        {
            Assert.Equal(SpectrumDisplayMode.Enhanced2x, SpectrumDisplayModes.Next(SpectrumDisplayMode.Native));
            Assert.Equal(SpectrumDisplayMode.Enhanced3x, SpectrumDisplayModes.Next(SpectrumDisplayMode.Enhanced2x));
            Assert.Equal(SpectrumDisplayMode.Native, SpectrumDisplayModes.Next(SpectrumDisplayMode.Enhanced3x));
        }
    }
}
