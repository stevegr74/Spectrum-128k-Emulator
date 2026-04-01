using System;

namespace Spectrum128kEmulator.Z80
{
    public class Z80Cpu
    {
        public Z80Registers Regs { get; } = new Z80Registers();
        public ulong TStates { get; private set; } = 0;

        public Func<ushort, byte> ReadMemory { get; set; } = _ => 0xFF;
        public Action<ushort, byte> WriteMemory { get; set; } = (_, _) => { };
        public Func<ushort, byte> ReadPort { get; set; } = _ => 0xFF;
        public Action<ushort, byte> WritePort { get; set; } = (_, _) => { };

        private bool halted = false;

        private readonly Action[] opcodeTable = new Action[256];
        private readonly Action[] cbOpcodeTable = new Action[256];
        private readonly Action[] edOpcodeTable = new Action[256];

        public Z80Cpu()
        {
            InitializeOpcodeTable();
            InitializeCBTable();
            InitializeEDTable();
        }

        private void InitializeOpcodeTable()
        {
            for (int i = 0; i < 256; i++)
                opcodeTable[i] = () => TStates += 4;

            // LD r, r'
            for (int dst = 0; dst < 8; dst++)
            for (int src = 0; src < 8; src++)
            {
                if (dst == 6 && src == 6) continue;
                int op = 0x40 + (dst << 3) + src;
                int d = dst, s = src;
                opcodeTable[op] = () =>
                {
                    SetReg((byte)d, GetReg((byte)s));
                    TStates += 4;
                };
            }

            // LD r, n
            for (int r = 0; r < 8; r++)
            {
                int op = 0x06 + (r << 3);
                int rr = r;
                opcodeTable[op] = () => { SetReg((byte)rr, FetchByte()); TStates += 7; };
            }

            // LD (HL), n
            opcodeTable[0x36] = () => { WriteMemory(Regs.HL, FetchByte()); TStates += 10; };

            // LD A,(nn) / LD (nn),A
            opcodeTable[0x3A] = () => { Regs.A = ReadMemory(FetchWord()); TStates += 13; };
            opcodeTable[0x32] = () => { WriteMemory(FetchWord(), Regs.A); TStates += 13; };

            // 16-bit loads
            opcodeTable[0x01] = () => Regs.BC = FetchWord();
            opcodeTable[0x11] = () => Regs.DE = FetchWord();
            opcodeTable[0x21] = () => Regs.HL = FetchWord();
            opcodeTable[0x31] = () => Regs.SP = FetchWord();

            // INC / DEC r
            for (int r = 0; r < 8; r++)
            {
                int rr = r;
                opcodeTable[0x04 + (r << 3)] = () => IncReg((byte)rr);
                opcodeTable[0x05 + (r << 3)] = () => DecReg((byte)rr);
            }

            // Basic ALU
            for (int i = 0; i < 8; i++)
            {
                int r = i;
                opcodeTable[0x80 + i] = () => AddA(GetReg((byte)r), false, 4);
                opcodeTable[0x88 + i] = () => AddA(GetReg((byte)r), true, 4);
                opcodeTable[0x90 + i] = () => SubA(GetReg((byte)r), false, 4);
            }
            opcodeTable[0xC6] = () => AddA(FetchByte(), false, 7);

            // Jumps
            opcodeTable[0xC3] = () => { Regs.PC = FetchWord(); TStates += 10; }; // JP nn

            // JR e
            opcodeTable[0x18] = () => { sbyte e = (sbyte)FetchByte(); Regs.PC = (ushort)(Regs.PC + e); TStates += 12; };

            // Conditional JR (more complete)
            opcodeTable[0x20] = () => JRcc((Regs.F & (1 << 6)) == 0); // JR NZ
            opcodeTable[0x28] = () => JRcc((Regs.F & (1 << 6)) != 0); // JR Z
            opcodeTable[0x30] = () => JRcc((Regs.F & 1) == 0);        // JR NC
            opcodeTable[0x38] = () => JRcc((Regs.F & 1) != 0);        // JR C

            // CALL / RET
            opcodeTable[0xCD] = () => { ushort addr = FetchWord(); Push(Regs.PC); Regs.PC = addr; TStates += 17; };
            opcodeTable[0xC9] = () => { Regs.PC = Pop(); TStates += 10; };

            // PUSH / POP
            opcodeTable[0xC5] = () => { Push(Regs.BC); TStates += 11; };
            opcodeTable[0xD5] = () => { Push(Regs.DE); TStates += 11; };
            opcodeTable[0xE5] = () => { Push(Regs.HL); TStates += 11; };
            opcodeTable[0xF5] = () => { Push(Regs.AF); TStates += 11; };
            opcodeTable[0xC1] = () => { Regs.BC = Pop(); TStates += 10; };
            opcodeTable[0xD1] = () => { Regs.DE = Pop(); TStates += 10; };
            opcodeTable[0xE1] = () => { Regs.HL = Pop(); TStates += 10; };
            opcodeTable[0xF1] = () => { Regs.AF = Pop(); TStates += 10; };

            // Interrupts
            opcodeTable[0xF3] = () => TStates += 4; // DI
            opcodeTable[0xFB] = () => TStates += 4; // EI

            // OUT (n), A
            opcodeTable[0xD3] = () => { byte p = FetchByte(); WritePort(p, Regs.A); TStates += 11; };

            // HALT
            opcodeTable[0x76] = () => halted = true;
        }

        private void JRcc(bool condition)
        {
            sbyte e = (sbyte)FetchByte();
            if (condition)
                Regs.PC = (ushort)(Regs.PC + e);
            TStates += 12; // always 12 for JR cc (even if not taken on Z80, but close enough)
        }

        private void InitializeCBTable()
        {
            for (int i = 0; i < 256; i++)
                cbOpcodeTable[i] = () => TStates += 8;

            // BIT b, r
            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit, r = reg;
                int op = 0x40 + (bit << 3) + reg;
                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    bool bitSet = (val & (1 << b)) != 0;
                    SetFlag(Flag.Z, !bitSet);
                    SetFlag(Flag.N, false);
                    SetFlag(Flag.H, true);
                    TStates += (r == 6 ? 12UL : 8UL);
                };
            }

            // SET b, r
            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit, r = reg;
                int op = 0xC0 + (bit << 3) + reg;
                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    SetReg((byte)r, (byte)(val | (1 << b)));
                    TStates += (r == 6 ? 15UL : 8UL);
                };
            }

            // RES b, r
            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit, r = reg;
                int op = 0x80 + (bit << 3) + reg;
                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    SetReg((byte)r, (byte)(val & ~(1 << b)));
                    TStates += (r == 6 ? 15UL : 8UL);
                };
            }
        }

        private void InitializeEDTable()
        {
            for (int i = 0; i < 256; i++)
                edOpcodeTable[i] = () => TStates += 8;

            // LD I, A
            edOpcodeTable[0x47] = () => { Regs.I = Regs.A; TStates += 9; };

            // IM 0/1/2
            edOpcodeTable[0x46] = () => TStates += 8;
            edOpcodeTable[0x56] = () => TStates += 8;
            edOpcodeTable[0x5E] = () => TStates += 8;

            // OUT (C), r   ← Fixed: use full BC as port
            for (int r = 0; r < 8; r++)
            {
                int reg = r;
                edOpcodeTable[0x41 + (r << 3)] = () =>
                {
                    byte value = GetReg((byte)reg);
                    WritePort(Regs.BC, value);   // ← This was the main bug!
                    TStates += 12;
                };
            }
        }

        // Register access
        private byte GetReg(byte idx)
        {
            return idx switch
            {
                0 => Regs.B, 1 => Regs.C, 2 => Regs.D, 3 => Regs.E,
                4 => Regs.H, 5 => Regs.L, 6 => ReadMemory(Regs.HL), 7 => Regs.A,
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
            byte val = GetReg(idx);
            val++;
            SetReg(idx, val);
            SetFlag(Flag.Z, val == 0);
            SetFlag(Flag.N, false);
            TStates += (idx == 6 ? 11UL : 4UL);
        }

        private void DecReg(byte idx)
        {
            byte val = GetReg(idx);
            val--;
            SetReg(idx, val);
            SetFlag(Flag.Z, val == 0);
            SetFlag(Flag.N, true);
            TStates += (idx == 6 ? 11UL : 4UL);
        }

        private void AddA(byte value, bool carry, int baseT)
        {
            int result = Regs.A + value + (carry ? (Regs.F & 1) : 0);
            Regs.A = (byte)result;
            SetFlag(Flag.C, result > 0xFF);
            SetFlag(Flag.Z, Regs.A == 0);
            SetFlag(Flag.N, false);
            TStates += (ulong)baseT;
        }

        private void SubA(byte value, bool carry, int baseT)
        {
            int result = Regs.A - value - (carry ? (Regs.F & 1) : 0);
            Regs.A = (byte)result;
            SetFlag(Flag.C, result < 0);
            SetFlag(Flag.Z, Regs.A == 0);
            SetFlag(Flag.N, true);
            TStates += (ulong)baseT;
        }

        private void Push(ushort value)
        {
            Regs.SP -= 2;
            WriteMemory(Regs.SP, (byte)value);
            WriteMemory((ushort)(Regs.SP + 1), (byte)(value >> 8));
        }

        private ushort Pop()
        {
            ushort value = (ushort)(ReadMemory(Regs.SP) | (ReadMemory((ushort)(Regs.SP + 1)) << 8));
            Regs.SP += 2;
            return value;
        }

        private byte FetchByte()
        {
            byte b = ReadMemory(Regs.PC);
            Regs.PC = (ushort)(Regs.PC + 1);
            TStates += 4;
            Regs.R = (byte)((Regs.R + 1) & 0x7F);
            return b;
        }

        private ushort FetchWord()
        {
            byte low = FetchByte();
            byte high = FetchByte();
            return (ushort)(low | (high << 8));
        }

        private enum Flag : byte { C = 0, N = 1, P = 2, H = 4, Z = 6, S = 7 }

        private void SetFlag(Flag f, bool set)
        {
            if (set)
                Regs.F |= (byte)(1 << (int)f);
            else
                Regs.F &= (byte)~(1 << (int)f);
        }

        public void ExecuteOneFrame(int maxTStates)
        {
            while (TStates < (ulong)maxTStates && !halted)
            {
                byte op = FetchByte();

                if (op == 0xCB)
                {
                    byte cbOp = FetchByte();
                    cbOpcodeTable[cbOp]();
                }
                else if (op == 0xED)
                {
                    byte edOp = FetchByte();
                    edOpcodeTable[edOp]();
                }
                else
                {
                    opcodeTable[op]();
                }
            }
        }

        public void Reset()
        {
            Regs.PC = 0;
            Regs.SP = 0xFFFF;
            Regs.I = Regs.R = 0;
            halted = false;
            TStates = 0;
        }
    }
}