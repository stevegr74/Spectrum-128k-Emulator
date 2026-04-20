using System;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        private void InitializeCBTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                cbOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL CB 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4}");
                    TStates += 8;
                };
            }

            for (int r = 0; r < 8; r++)
            {
                int rr = r;

                cbOpcodeTable[0x00 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x80) != 0; byte res = (byte)((v << 1) | (c ? 1 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x08 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x01) != 0; byte res = (byte)((v >> 1) | (c ? 0x80 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x10 + r] = () => { byte v = GetReg((byte)rr); bool oldC = (Regs.F & 0x01) != 0; bool c = (v & 0x80) != 0; byte res = (byte)((v << 1) | (oldC ? 1 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x18 + r] = () => { byte v = GetReg((byte)rr); bool oldC = (Regs.F & 0x01) != 0; bool c = (v & 0x01) != 0; byte res = (byte)((v >> 1) | (oldC ? 0x80 : 0)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x20 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x80) != 0; byte res = (byte)(v << 1); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x28 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x01) != 0; byte res = (byte)((v >> 1) | (v & 0x80)); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x30 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x80) != 0; byte res = (byte)((v << 1) | 0x01); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
                cbOpcodeTable[0x38 + r] = () => { byte v = GetReg((byte)rr); bool c = (v & 0x01) != 0; byte res = (byte)(v >> 1); SetReg((byte)rr, res); SetShiftRotateFlags(res, c); TStates += (rr == 6 ? 15UL : 8UL); };
            }

            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit;
                int r = reg;
                int op = 0x40 + (bit << 3) + reg;

                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    bool set = (val & (1 << b)) != 0;
                    SetFlag(Flag.Z, !set);
                    SetFlag(Flag.N, false);
                    SetFlag(Flag.H, true);
                    SetFlag(Flag.S, b == 7 && set);
                    SetFlag(Flag.P, !set);

                    if (r == 6)
                    {
                        SetFlag(Flag.F3, (Regs.H & 0x08) != 0);
                        SetFlag(Flag.F5, (Regs.H & 0x20) != 0);
                    }
                    else
                    {
                        CopyUndocumentedFlagsFrom(val);
                    }

                    TStates += (r == 6 ? 12UL : 8UL);
                };
            }

            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit;
                int r = reg;
                int op = 0x80 + (bit << 3) + reg;

                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    SetReg((byte)r, (byte)(val & ~(1 << b)));
                    TStates += (r == 6 ? 15UL : 8UL);
                };
            }

            for (int bit = 0; bit < 8; bit++)
            for (int reg = 0; reg < 8; reg++)
            {
                int b = bit;
                int r = reg;
                int op = 0xC0 + (bit << 3) + reg;

                cbOpcodeTable[op] = () =>
                {
                    byte val = GetReg((byte)r);
                    SetReg((byte)r, (byte)(val | (1 << b)));
                    TStates += (r == 6 ? 15UL : 8UL);
                };
            }
        }

        private void ExecuteIndexedCB(ushort indexReg, sbyte disp, byte cbOp)
        {
            ushort addr = (ushort)(indexReg + disp);
            byte value = ReadMemory(addr);

            int group = (cbOp >> 6) & 0x03;
            int y = (cbOp >> 3) & 0x07;
            int z = cbOp & 0x07;

            switch (group)
            {
                case 0:
                {
                    byte result = value;
                    bool carry = false;

                    switch (y)
                    {
                        case 0: carry = (value & 0x80) != 0; result = (byte)((value << 1) | (carry ? 1 : 0)); break; // RLC
                        case 1: carry = (value & 0x01) != 0; result = (byte)((value >> 1) | (carry ? 0x80 : 0x00)); break; // RRC
                        case 2:
                        {
                            bool oldCarry = (Regs.F & 0x01) != 0;
                            carry = (value & 0x80) != 0;
                            result = (byte)((value << 1) | (oldCarry ? 1 : 0));
                            break;
                        }
                        case 3:
                        {
                            bool oldCarry = (Regs.F & 0x01) != 0;
                            carry = (value & 0x01) != 0;
                            result = (byte)((value >> 1) | (oldCarry ? 0x80 : 0x00));
                            break;
                        }
                        case 4: carry = (value & 0x80) != 0; result = (byte)(value << 1); break; // SLA
                        case 5: carry = (value & 0x01) != 0; result = (byte)((value >> 1) | (value & 0x80)); break; // SRA
                        case 6: carry = (value & 0x80) != 0; result = (byte)((value << 1) | 0x01); break; // SLL
                        case 7: carry = (value & 0x01) != 0; result = (byte)(value >> 1); break; // SRL
                    }

                    WriteMemory(addr, result);
                    SetShiftRotateFlags(result, carry);

                    if (z != 6)
                        SetReg((byte)z, result);

                    TStates += 23;
                    break;
                }

                case 1: // BIT
                {
                    bool bitSet = (value & (1 << y)) != 0;
                    SetFlag(Flag.Z, !bitSet);
                    SetFlag(Flag.N, false);
                    SetFlag(Flag.H, true);
                    SetFlag(Flag.P, !bitSet);
                    SetFlag(Flag.S, y == 7 && bitSet);
                    SetFlag(Flag.F3, (((byte)(addr >> 8)) & 0x08) != 0);
                    SetFlag(Flag.F5, (((byte)(addr >> 8)) & 0x20) != 0);
                    TStates += 20;
                    break;
                }

                case 2: // RES
                {
                    byte result = (byte)(value & ~(1 << y));
                    WriteMemory(addr, result);
                    if (z != 6)
                        SetReg((byte)z, result);
                    TStates += 23;
                    break;
                }

                case 3: // SET
                {
                    byte result = (byte)(value | (1 << y));
                    WriteMemory(addr, result);
                    if (z != 6)
                        SetReg((byte)z, result);
                    TStates += 23;
                    break;
                }
            }
        }

        private void SetShiftRotateFlags(byte result, bool carry)
        {
            Regs.F = 0;
            if ((result & 0x80) != 0) Regs.F |= 0x80;
            if (result == 0) Regs.F |= 0x40;
            if (Parity(result)) Regs.F |= 0x04;
            if (carry) Regs.F |= 0x01;
            Regs.F |= (byte)(result & 0x28);
        }
    }
}
