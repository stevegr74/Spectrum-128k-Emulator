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

/*        private void Push(ushort value)
        {
            Regs.SP -= 2;
            WriteMemory(Regs.SP, (byte)value);
            WriteMemory((ushort)(Regs.SP + 1), (byte)(value >> 8));
        }*/
        private void Push(ushort value)
        {
            Regs.SP--;
            byte high = (byte)(value >> 8);
            if (IsWatchedStackAddress(Regs.SP))
                RecordStackEvent($"STACK_PUSH_HIGH {Regs.SP:X4}<-{high:X2} value={value:X4}");
            WriteMemory(Regs.SP, high); // high
            TStates += 3;

            Regs.SP--;
            byte low = (byte)(value & 0xFF);
            if (IsWatchedStackAddress(Regs.SP))
                RecordStackEvent($"STACK_PUSH_LOW {Regs.SP:X4}<-{low:X2} value={value:X4}");
            WriteMemory(Regs.SP, low); // low
            TStates += 3;
        }

        /*private ushort Pop()
        {
            ushort value = (ushort)(ReadMemory(Regs.SP) | (ReadMemory((ushort)(Regs.SP + 1)) << 8));
            Regs.SP += 2;
            return value;
        }*/

        private ushort Pop()
        {
            ushort lowAddr = Regs.SP;
            byte low = ReadMemory(Regs.SP);
            if (IsWatchedStackAddress(lowAddr))
                RecordStackEvent($"STACK_POP_LOW {lowAddr:X4}->{low:X2}");
            TStates += 3;
            Regs.SP++;

            ushort highAddr = Regs.SP;
            byte high = ReadMemory(Regs.SP);
            if (IsWatchedStackAddress(highAddr))
                RecordStackEvent($"STACK_POP_HIGH {highAddr:X4}->{high:X2}");
            TStates += 3;
            Regs.SP++;

            ushort value = (ushort)(low | (high << 8));
            if (value == 0x6C53 || value == 0x185C)
                RecordStackEvent($"STACK_POP_VALUE {value:X4}");
            return value;
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
            Regs.R = (byte)((Regs.R & 0x80) | ((Regs.R + 1) & 0x7F));
            return b;
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
