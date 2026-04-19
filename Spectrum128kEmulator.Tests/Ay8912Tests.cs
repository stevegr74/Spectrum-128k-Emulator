using Xunit;
using Spectrum128kEmulator.Audio;

namespace Spectrum128kEmulator.Tests
{
    public class Ay8912Tests
    {
        [Fact]
        public void Ay_Select_And_Write_Register_Works()
        {
            var ay = new Ay8912();

            ay.SelectRegister(3);
            ay.WriteRegister(0xAA);

            Assert.Equal(0xAA, ay.ReadRegister(3));
        }

        [Fact]
        public void Selecting_Register_Masks_To_Low_4_Bits()
        {
            var ay = new Ay8912();

            ay.SelectRegister(0x13);
            ay.WriteRegister(0x55);

            Assert.Equal((byte)3, ay.CurrentRegister);
            Assert.Equal((byte)0x55, ay.ReadRegister(3));
        }
    }
}
