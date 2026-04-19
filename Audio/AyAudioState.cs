using System;

namespace Spectrum128kEmulator.Audio
{
    public sealed class AyAudioState
    {
        private readonly byte[] registers;

        public AyAudioState(byte[] registers)
        {
            if (registers == null)
                throw new ArgumentNullException(nameof(registers));
            if (registers.Length != 16)
                throw new ArgumentException("AY state must contain exactly 16 registers.", nameof(registers));

            this.registers = (byte[])registers.Clone();
        }

        public byte ReadRegister(int register)
        {
            if ((uint)register >= registers.Length)
                throw new ArgumentOutOfRangeException(nameof(register));

            return registers[register];
        }

        public byte[] GetRegistersCopy()
        {
            return (byte[])registers.Clone();
        }
    }
}
