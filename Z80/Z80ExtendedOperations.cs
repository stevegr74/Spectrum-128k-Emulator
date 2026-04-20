using System;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        private void InitializeEDTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                edOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL ED 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4}");
                    TStates += 8;
                };
            }

            // =========================
            // IN r,(C)
            // =========================
            edOpcodeTable[0x40] = () => { byte v = ReadPort(Regs.BC); Regs.B = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x48] = () => { byte v = ReadPort(Regs.BC); Regs.C = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x50] = () => { byte v = ReadPort(Regs.BC); Regs.D = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x58] = () => { byte v = ReadPort(Regs.BC); Regs.E = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x60] = () => { byte v = ReadPort(Regs.BC); Regs.H = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x68] = () => { byte v = ReadPort(Regs.BC); Regs.L = v; SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x70] = () => { byte v = ReadPort(Regs.BC); SetInFlags(v); TStates += 12; };
            edOpcodeTable[0x78] = () => { byte v = ReadPort(Regs.BC); Regs.A = v; SetInFlags(v); TStates += 12; };

            // =========================
            // OUT (C),r
            // =========================
            edOpcodeTable[0x41] = () => { WritePortTimed(Regs.BC, Regs.B, 12); };
            edOpcodeTable[0x49] = () => { WritePortTimed(Regs.BC, Regs.C, 12); };
            edOpcodeTable[0x51] = () => { WritePortTimed(Regs.BC, Regs.D, 12); };
            edOpcodeTable[0x59] = () => { WritePortTimed(Regs.BC, Regs.E, 12); };
            edOpcodeTable[0x61] = () => { WritePortTimed(Regs.BC, Regs.H, 12); };
            edOpcodeTable[0x69] = () => { WritePortTimed(Regs.BC, Regs.L, 12); };
            edOpcodeTable[0x71] = () => { WritePortTimed(Regs.BC, 0, 12); };
            edOpcodeTable[0x79] = () => { WritePortTimed(Regs.BC, Regs.A, 12); };

            // =========================
            // Transfer A <-> I/R
            // =========================
            edOpcodeTable[0x47] = () => { Regs.I = Regs.A; TStates += 9; };
            edOpcodeTable[0x4F] = () => { Regs.R = Regs.A; TStates += 9; };

            edOpcodeTable[0x57] = () => // LD A,I
            {
                Regs.A = Regs.I;
                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, IFF2);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 9;
            };

            edOpcodeTable[0x5F] = () => // LD A,R
            {
                Regs.A = Regs.R;
                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, IFF2);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 9;
            };

            // =========================
            // SBC/ADC HL,rr
            // =========================
            edOpcodeTable[0x42] = () => { Regs.HL = Sub16(Regs.HL, Regs.BC, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x52] = () => { Regs.HL = Sub16(Regs.HL, Regs.DE, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x62] = () => { Regs.HL = Sub16(Regs.HL, Regs.HL, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x72] = () => { Regs.HL = Sub16(Regs.HL, Regs.SP, (Regs.F & 0x01) != 0); TStates += 15; };

            edOpcodeTable[0x4A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.BC, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x5A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.DE, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x6A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.HL, (Regs.F & 0x01) != 0); TStates += 15; };
            edOpcodeTable[0x7A] = () => { Regs.HL = Add16WithCarry(Regs.HL, Regs.SP, (Regs.F & 0x01) != 0); TStates += 15; };

            // =========================
            // 16-bit loads via memory
            // =========================
            edOpcodeTable[0x43] = () => { ushort a = FetchWord(); WriteMemory(a, Regs.C); WriteMemory((ushort)(a + 1), Regs.B); TStates += 20; };
            edOpcodeTable[0x53] = () => { ushort a = FetchWord(); WriteMemory(a, Regs.E); WriteMemory((ushort)(a + 1), Regs.D); TStates += 20; };
            edOpcodeTable[0x63] = () => { ushort a = FetchWord(); WriteMemory(a, Regs.L); WriteMemory((ushort)(a + 1), Regs.H); TStates += 20; };
            edOpcodeTable[0x73] = () => { ushort a = FetchWord(); WriteMemory(a, (byte)(Regs.SP & 0xFF)); WriteMemory((ushort)(a + 1), (byte)(Regs.SP >> 8)); TStates += 20; };

            edOpcodeTable[0x4B] = () => { ushort a = FetchWord(); Regs.BC = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };
            edOpcodeTable[0x5B] = () => { ushort a = FetchWord(); Regs.DE = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };
            edOpcodeTable[0x6B] = () => { ushort a = FetchWord(); Regs.HL = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };
            edOpcodeTable[0x7B] = () => { ushort a = FetchWord(); Regs.SP = (ushort)(ReadMemory(a) | (ReadMemory((ushort)(a + 1)) << 8)); TStates += 20; };

            // =========================
            // NEG (ED prefix)
            // =========================
            // All of these opcodes are undocumented aliases of NEG.
            // Z80 defines 8 encodings (ED 44,4C,54,5C,64,6C,74,7C)
            // that all perform: A = 0 - A with full flag behaviour.
            // Must map ALL of them for ZEXDOC/ZEXALL compliance.
            edOpcodeTable[0x44] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x4C] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x54] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x5C] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x64] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x6C] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x74] = () => { NegA(); TStates += 8; };
            edOpcodeTable[0x7C] = () => { NegA(); TStates += 8; };

            // =========================
            // Return / interrupt mode
            // =========================
            edOpcodeTable[0x45] = () => { IFF1 = IFF2; Regs.PC = Pop(); TStates += 14; }; // RETN
            edOpcodeTable[0x4D] = () => { IFF1 = IFF2; Regs.PC = Pop(); TStates += 14; }; // RETI

            edOpcodeTable[0x46] = () => { interruptMode = 0; TStates += 8; };
            edOpcodeTable[0x56] = () => { interruptMode = 1; TStates += 8; };
            edOpcodeTable[0x5E] = () => { interruptMode = 2; TStates += 8; };

            // =========================
            // Decimal rotate through memory
            // =========================
            edOpcodeTable[0x67] = () => // RRD
            {
                byte mem = ReadMemory(Regs.HL);
                byte aLow = (byte)(Regs.A & 0x0F);
                byte newMem = (byte)((aLow << 4) | (mem >> 4));
                byte newA = (byte)((Regs.A & 0xF0) | (mem & 0x0F));

                WriteMemory(Regs.HL, newMem);
                Regs.A = newA;

                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, Parity(Regs.A));
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);

                TStates += 18;
            };

            edOpcodeTable[0x6F] = () => // RLD
            {
                byte mem = ReadMemory(Regs.HL);
                byte aLow = (byte)(Regs.A & 0x0F);
                byte newMem = (byte)((mem << 4) | aLow);
                byte newA = (byte)((Regs.A & 0xF0) | (mem >> 4));

                WriteMemory(Regs.HL, newMem);
                Regs.A = newA;

                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.H, false);
                SetFlag(Flag.P, Parity(Regs.A));
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);

                TStates += 18;
            };

            // =========================
            // Block transfer
            // =========================
            edOpcodeTable[0xA0] = () => // LDI
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL++;
                Regs.DE++;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                TStates += 16;
            };

            edOpcodeTable[0xB0] = () => // LDIR
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL++;
                Regs.DE++;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                if (Regs.BC != 0)
                {
                    Regs.PC = (ushort)(Regs.PC - 2);
                    TStates += 21;
                }
                else
                {
                    TStates += 16;
                }
            };

            edOpcodeTable[0xA8] = () => // LDD
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL--;
                Regs.DE--;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                TStates += 16;
            };

            edOpcodeTable[0xB8] = () => // LDDR
            {
                byte value = ReadMemory(Regs.HL);
                WriteMemory(Regs.DE, value);
                Regs.HL--;
                Regs.DE--;
                Regs.BC--;
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                SetFlag(Flag.P, Regs.BC != 0);

                if (Regs.BC != 0)
                {
                    Regs.PC = (ushort)(Regs.PC - 2);
                    TStates += 21;
                }
                else
                {
                    TStates += 16;
                }
            };

            // =========================
            // Block compare
            // =========================
            edOpcodeTable[0xA1] = () => BlockCompare(true, false);  // CPI
            edOpcodeTable[0xB1] = () => BlockCompare(true, true);   // CPIR
            edOpcodeTable[0xA9] = () => BlockCompare(false, false); // CPD
            edOpcodeTable[0xB9] = () => BlockCompare(false, true);  // CPDR

            // =========================
            // Block I/O
            // =========================
            edOpcodeTable[0xA2] = () => BlockIn(true, false);   // INI
            edOpcodeTable[0xB2] = () => BlockIn(true, true);    // INIR
            edOpcodeTable[0xAA] = () => BlockIn(false, false);  // IND
            edOpcodeTable[0xBA] = () => BlockIn(false, true);   // INDR

            edOpcodeTable[0xA3] = () => BlockOut(true, false);  // OUTI
            edOpcodeTable[0xB3] = () => BlockOut(true, true);   // OTIR
            edOpcodeTable[0xAB] = () => BlockOut(false, false); // OUTD
            edOpcodeTable[0xBB] = () => BlockOut(false, true);  // OTDR
        }

        private void BlockIn(bool increment, bool repeat)
        {
            byte value = ReadPort(Regs.BC);
            WriteMemory(Regs.HL, value);

            Regs.HL = increment ? (ushort)(Regs.HL + 1) : (ushort)(Regs.HL - 1);
            Regs.B = (byte)(Regs.B - 1);

            // Minimal first-pass flag behaviour:
            // N is set
            // Z reflects B == 0
            // Other flags can be refined later if needed for compatibility
            SetFlag(Flag.N, true);
            SetFlag(Flag.Z, Regs.B == 0);

            if (repeat && Regs.B != 0)
            {
                Regs.PC = (ushort)(Regs.PC - 2);
                TStates += 21;
            }
            else
            {
                TStates += 16;
            }
        }

        private void BlockOut(bool increment, bool repeat)
        {
            byte value = ReadMemory(Regs.HL);

            Regs.HL = increment ? (ushort)(Regs.HL + 1) : (ushort)(Regs.HL - 1);
            Regs.B = (byte)(Regs.B - 1);

            SetFlag(Flag.N, true);
            SetFlag(Flag.Z, Regs.B == 0);

            int instructionTStates;
            if (repeat && Regs.B != 0)
            {
                Regs.PC = (ushort)(Regs.PC - 2);
                instructionTStates = 21;
            }
            else
            {
                instructionTStates = 16;
            }

            WritePortTimed(Regs.BC, value, instructionTStates);
        }

        private byte CompareAInternal(byte value)
        {
            byte a = Regs.A;
            int result = a - value;
            byte r = (byte)result;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a ^ value ^ r) & 0x10) != 0);
            SetFlag(Flag.P, OverflowSub(a, value, r));
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, result < 0);

            CopyUndocumentedFlagsFrom(r);
            return r;
        }        

        private void BlockCompare(bool increment, bool repeat)
        {
            bool oldCarry = (Regs.F & (1 << (int)Flag.C)) != 0;

            byte value = ReadMemory(Regs.HL);
            byte r = CompareAInternal(value);

            bool halfBorrow = (Regs.F & (1 << (int)Flag.H)) != 0;

            Regs.HL = increment ? (ushort)(Regs.HL + 1) : (ushort)(Regs.HL - 1);
            Regs.BC--;

            SetFlag(Flag.P, Regs.BC != 0);

            byte n = (byte)(r - (halfBorrow ? 1 : 0));
            SetFlag(Flag.F3, (n & 0x08) != 0);
            SetFlag(Flag.F5, (n & 0x20) != 0);

            SetFlag(Flag.C, oldCarry);

            if (repeat && Regs.BC != 0 && r != 0)
            {
                Regs.PC = (ushort)(Regs.PC - 2);
                TStates += 21;
            }
            else
            {
                TStates += 16;
            }
        }

        private void SetInFlags(byte value)
        {
            SetFlag(Flag.S, (value & 0x80) != 0);
            SetFlag(Flag.Z, value == 0);
            SetFlag(Flag.H, false);
            SetFlag(Flag.P, Parity(value));
            SetFlag(Flag.N, false);
            CopyUndocumentedFlagsFrom(value);
        }
    }
}
