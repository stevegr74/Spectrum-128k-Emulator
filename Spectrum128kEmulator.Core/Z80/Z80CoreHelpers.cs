using System;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        // =========================================================
        // Register helpers
        // =========================================================

        private byte GetReg(byte idx)
        {
            return idx switch
            {
                0 => Regs.B,
                1 => Regs.C,
                2 => Regs.D,
                3 => Regs.E,
                4 => Regs.H,
                5 => Regs.L,
                6 => ReadMemory(Regs.HL),
                7 => Regs.A,
                _ => 0
            };
        }

        private void SetReg(byte idx, byte val)
        {
            switch (idx)
            {
                case 0: Regs.B = val; break;
                case 1: Regs.C = val; break;
                case 2: Regs.D = val; break;
                case 3: Regs.E = val; break;
                case 4: Regs.H = val; break;
                case 5: Regs.L = val; break;
                case 6: WriteMemory(Regs.HL, val); break;
                case 7: Regs.A = val; break;
            }
        }

        private void IncReg(byte idx)
        {
            byte old = GetReg(idx);
            byte val = (byte)(old + 1);
            SetReg(idx, val);

            SetFlag(Flag.S, (val & 0x80) != 0);
            SetFlag(Flag.Z, val == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x0F);
            SetFlag(Flag.P, old == 0x7F);
            SetFlag(Flag.N, false);
            CopyUndocumentedFlagsFrom(val);

            TStates += (idx == 6 ? 11UL : 4UL);
        }

        private void DecReg(byte idx)
        {
            byte old = GetReg(idx);
            byte val = (byte)(old - 1);
            SetReg(idx, val);

            SetFlag(Flag.S, (val & 0x80) != 0);
            SetFlag(Flag.Z, val == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x00);
            SetFlag(Flag.P, old == 0x80);
            SetFlag(Flag.N, true);
            CopyUndocumentedFlagsFrom(val);

            TStates += (idx == 6 ? 11UL : 4UL);
        }

        // =========================================================
        // Stack and fetch helpers
        // =========================================================

        private void Push(ushort value)
        {
            Regs.SP--;
            byte high = (byte)(value >> 8);
            WriteMemory(Regs.SP, high); // high
            TStates += 3;

            Regs.SP--;
            byte low = (byte)(value & 0xFF);
            WriteMemory(Regs.SP, low); // low
            TStates += 3;
        }

        private ushort Pop()
        {
            byte low = ReadMemory(Regs.SP);
            TStates += 3;
            Regs.SP++;

            byte high = ReadMemory(Regs.SP);
            TStates += 3;
            Regs.SP++;

            return (ushort)(low | (high << 8));
        }

        // Fetch helpers do not add T-states here.
        // Instruction handlers own the full documented timing for the instruction.
        //
        // The Z80 refresh register increments on opcode fetch/M1 cycles, including
        // prefix bytes, but not on ordinary operand bytes.
        private byte FetchOpcodeByte()
        {
            byte b = ReadMemory(Regs.PC);
            Regs.PC = (ushort)(Regs.PC + 1);
            IncrementRefreshRegister();
            InstructionFetchCount++;
            return b;
        }

        private void IncrementRefreshRegister()
        {
            Regs.R = (byte)((Regs.R & 0x80) | ((Regs.R + 1) & 0x7F));
        }

        private byte FetchByte()
        {
            byte b = ReadMemory(Regs.PC);
            Regs.PC = (ushort)(Regs.PC + 1);
            return b;
        }

        private ushort FetchWord()
        {
            byte low = FetchByte();
            byte high = FetchByte();
            return (ushort)(low | (high << 8));
        }

        private void WriteWordWithAccessSpacing(ushort addr, byte low, byte high, int totalTStates)
        {
            TStates += (ulong)(totalTStates - 6);
            WriteMemory(addr, low);
            TStates += 3;
            WriteMemory((ushort)(addr + 1), high);
            TStates += 3;
        }

        private ushort ReadWordWithAccessSpacing(ushort addr, int totalTStates)
        {
            TStates += (ulong)(totalTStates - 6);
            byte low = ReadMemory(addr);
            TStates += 3;
            byte high = ReadMemory((ushort)(addr + 1));
            TStates += 3;
            return (ushort)(low | (high << 8));
        }
    }
}
