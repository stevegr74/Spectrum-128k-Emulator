using System;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        private enum Flag : byte
        {
            C = 0,
            N = 1,
            P = 2,
            F3 = 3,
            H = 4,
            F5 = 5,
            Z = 6,
            S = 7
        }

        private static bool Parity(byte value)
        {
            int count = 0;
            for (int i = 0; i < 8; i++)
            {
                if (((value >> i) & 1) != 0)
                    count++;
            }

            return (count & 1) == 0;
        }

        private void SetFlag(Flag f, bool set)
        {
            if (set)
                Regs.F |= (byte)(1 << (int)f);
            else
                Regs.F &= (byte)~(1 << (int)f);
        }

        private void CopyUndocumentedFlagsFrom(byte value)
        {
            SetFlag(Flag.F3, (value & 0x08) != 0);
            SetFlag(Flag.F5, (value & 0x20) != 0);
        }

        private void ApplyScfCcfUndocumentedFlags()
        {
            byte f3f5 = (byte)(((qFlags ^ Regs.F) | Regs.A) & 0x28);
            Regs.F = (byte)((Regs.F & 0xD7) | f3f5);
        }
    }
}
