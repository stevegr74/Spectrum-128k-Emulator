namespace Spectrum128kEmulator.Audio
{
    public class Ay8912
    {
        private readonly byte[] registers = new byte[16];
        private int selectedRegister;

        public void SelectRegister(byte reg)
        {
            selectedRegister = reg & 0x0F;
        }

        public void WriteRegister(byte value)
        {
            registers[selectedRegister] = value;
        }

        public byte ReadRegister(byte reg)
        {
            return registers[reg & 0x0F];
        }

        public byte CurrentRegister => (byte)selectedRegister;
    }
}
