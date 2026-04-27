using System;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
        private void InitializeOpcodeTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                opcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL OP 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 1):X4} SP=0x{Regs.SP:X4}");
                    TStates += 4;
                };
            }

            opcodeTable[0x00] = () => TStates += 4; // NOP

            opcodeTable[0x07] = () => // RLCA
            {
                bool carry = (Regs.A & 0x80) != 0;
                Regs.A = (byte)((Regs.A << 1) | (carry ? 1 : 0));
                SetFlag(Flag.C, carry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);                
                TStates += 4;
            };

            opcodeTable[0x0F] = () => // RRCA
            {
                bool carry = (Regs.A & 0x01) != 0;
                Regs.A = (byte)((Regs.A >> 1) | (carry ? 0x80 : 0x00));
                SetFlag(Flag.C, carry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x17] = () => // RLA
            {
                bool oldCarry = (Regs.F & 0x01) != 0;
                bool newCarry = (Regs.A & 0x80) != 0;
                Regs.A = (byte)((Regs.A << 1) | (oldCarry ? 1 : 0));
                SetFlag(Flag.C, newCarry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x1F] = () => // RRA
            {
                bool oldCarry = (Regs.F & 0x01) != 0;
                bool newCarry = (Regs.A & 0x01) != 0;
                Regs.A = (byte)((Regs.A >> 1) | (oldCarry ? 0x80 : 0x00));
                SetFlag(Flag.C, newCarry);
                SetFlag(Flag.H, false);
                SetFlag(Flag.N, false);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x08] = () => // EX AF,AF'
            {
                ushort temp = Regs.AF;
                Regs.AF = (ushort)((Regs.A_ << 8) | Regs.F_);
                Regs.A_ = (byte)(temp >> 8);
                Regs.F_ = (byte)temp;
                TStates += 4;
            };

            opcodeTable[0x02] = () => { WriteMemory(Regs.BC, Regs.A); TStates += 7; };
            opcodeTable[0x0A] = () => { Regs.A = ReadMemory(Regs.BC); TStates += 7; };
            opcodeTable[0x12] = () => { WriteMemory(Regs.DE, Regs.A); TStates += 7; };
            opcodeTable[0x1A] = () => { Regs.A = ReadMemory(Regs.DE); TStates += 7; };

            opcodeTable[0x09] = () => { Regs.HL = Add16(Regs.HL, Regs.BC); TStates += 11; };
            opcodeTable[0x19] = () => { Regs.HL = Add16(Regs.HL, Regs.DE); TStates += 11; };
            opcodeTable[0x29] = () => { Regs.HL = Add16(Regs.HL, Regs.HL); TStates += 11; };
            opcodeTable[0x39] = () => { Regs.HL = Add16(Regs.HL, Regs.SP); TStates += 11; };

            opcodeTable[0x10] = () => // DJNZ e
            {
                sbyte e = (sbyte)FetchByte();
                Regs.B--;
                if (Regs.B != 0)
                {
                    Regs.PC = (ushort)(Regs.PC + e);
                    TStates += 13;
                }
                else
                {
                    TStates += 8;
                }
            };

            opcodeTable[0x18] = () => // JR e
            {
                sbyte e = (sbyte)FetchByte();
                Regs.PC = (ushort)(Regs.PC + e);
                TStates += 12;
            };

            opcodeTable[0x20] = () => JRcc((Regs.F & 0x40) == 0); // JR NZ
            opcodeTable[0x28] = () => JRcc((Regs.F & 0x40) != 0); // JR Z
            opcodeTable[0x30] = () => JRcc((Regs.F & 0x01) == 0); // JR NC
            opcodeTable[0x38] = () => JRcc((Regs.F & 0x01) != 0); // JR C

            opcodeTable[0x22] = () => // LD (nn),HL
            {
                ushort addr = FetchWord();
                WriteMemory(addr, Regs.L);
                WriteMemory((ushort)(addr + 1), Regs.H);
                TStates += 16;
            };

            opcodeTable[0x27] = () => // DAA
            {
                const byte FlagC = 0x01;
                const byte FlagN = 0x02;
                const byte FlagH = 0x10;

                int index =
                    Regs.A
                    | ((Regs.F & FlagC) != 0 ? 0x100 : 0)
                    | ((Regs.F & FlagN) != 0 ? 0x200 : 0)
                    | ((Regs.F & FlagH) != 0 ? 0x400 : 0);

                ushort af = DaaAfTable[index];
                Regs.A = (byte)(af >> 8);
                Regs.F = (byte)af;

                TStates += 4;
            };

            opcodeTable[0x2A] = () => // LD HL,(nn)
            {
                ushort addr = FetchWord();
                byte low = ReadMemory(addr);
                byte high = ReadMemory((ushort)(addr + 1));
                Regs.HL = (ushort)(low | (high << 8));
                TStates += 16;
            };

            opcodeTable[0x2F] = () => // CPL
            {
                Regs.A = (byte)~Regs.A;
                SetFlag(Flag.N, true);
                SetFlag(Flag.H, true);
                CopyUndocumentedFlagsFrom(Regs.A);
                TStates += 4;
            };

            opcodeTable[0x37] = () => // SCF
            {
                SetFlag(Flag.C, true);
                SetFlag(Flag.N, false);
                SetFlag(Flag.H, false);
                ApplyScfCcfUndocumentedFlags();
                TStates += 4;
            };

            opcodeTable[0x3F] = () => // CCF
            {
                bool oldC = (Regs.F & (1 << (int)Flag.C)) != 0;
                SetFlag(Flag.C, !oldC);
                SetFlag(Flag.N, false);
                SetFlag(Flag.H, oldC);
                ApplyScfCcfUndocumentedFlags();
                TStates += 4;
            };

            for (int dst = 0; dst < 8; dst++)
            for (int src = 0; src < 8; src++)
            {
                if (dst == 6 && src == 6) continue;

                int op = 0x40 + (dst << 3) + src;
                int d = dst;
                int s = src;

                opcodeTable[op] = () =>
                {
                    SetReg((byte)d, GetReg((byte)s));
                    TStates += 4;
                };
            }

            opcodeTable[0x76] = () => // HALT
            {
                halted = true;
                TStates += 4;
            };

            for (int r = 0; r < 8; r++)
            {
                int op = 0x06 + (r << 3);
                int rr = r;
                opcodeTable[op] = () =>
                {
                    SetReg((byte)rr, FetchByte());
                    TStates += 7;
                };
            }

            opcodeTable[0x36] = () => // LD (HL),n
            {
                WriteMemory(Regs.HL, FetchByte());
                TStates += 10;
            };

            for (int r = 0; r < 8; r++)
            {
                if (r == 6) continue;
                int rr = r;
                opcodeTable[0x70 + r] = () =>
                {
                    WriteMemory(Regs.HL, GetReg((byte)rr));
                    TStates += 7;
                };
            }

            opcodeTable[0x01] = () => { Regs.BC = FetchWord(); TStates += 10; };
            opcodeTable[0x11] = () => { Regs.DE = FetchWord(); TStates += 10; };
            opcodeTable[0x21] = () => { Regs.HL = FetchWord(); TStates += 10; };
            opcodeTable[0x31] = () => { Regs.SP = FetchWord(); TStates += 10; };

            opcodeTable[0x3A] = () => { Regs.A = ReadMemory(FetchWord()); TStates += 13; };
            opcodeTable[0x32] = () => { WriteMemory(FetchWord(), Regs.A); TStates += 13; };

            opcodeTable[0x03] = () => { Regs.BC++; TStates += 6; };
            opcodeTable[0x0B] = () => { Regs.BC--; TStates += 6; };
            opcodeTable[0x13] = () => { Regs.DE++; TStates += 6; };
            opcodeTable[0x1B] = () => { Regs.DE--; TStates += 6; };
            opcodeTable[0x23] = () => { Regs.HL++; TStates += 6; };
            opcodeTable[0x2B] = () => { Regs.HL--; TStates += 6; };
            opcodeTable[0x33] = () => { Regs.SP++; TStates += 6; };
            opcodeTable[0x3B] = () => { Regs.SP--; TStates += 6; };

            for (int r = 0; r < 8; r++)
            {
                int rr = r;
                opcodeTable[0x04 + (r << 3)] = () => IncReg((byte)rr);
                opcodeTable[0x05 + (r << 3)] = () => DecReg((byte)rr);
            }

            opcodeTable[0xD9] = () => // EXX
            {
                (Regs.B, Regs.B_) = (Regs.B_, Regs.B);
                (Regs.C, Regs.C_) = (Regs.C_, Regs.C);
                (Regs.D, Regs.D_) = (Regs.D_, Regs.D);
                (Regs.E, Regs.E_) = (Regs.E_, Regs.E);
                (Regs.H, Regs.H_) = (Regs.H_, Regs.H);
                (Regs.L, Regs.L_) = (Regs.L_, Regs.L);
                TStates += 4;
            };

            opcodeTable[0xEB] = () => // EX DE,HL
            {
                ushort temp = Regs.DE;
                Regs.DE = Regs.HL;
                Regs.HL = temp;
                TStates += 4;
            };

            opcodeTable[0xE3] = () => // EX (SP),HL
            {
                byte low = ReadMemory(Regs.SP);
                byte high = ReadMemory((ushort)(Regs.SP + 1));
                WriteMemory(Regs.SP, Regs.L);
                WriteMemory((ushort)(Regs.SP + 1), Regs.H);
                Regs.HL = (ushort)(low | (high << 8));
                TStates += 19;
            };

            opcodeTable[0xE9] = () => // JP (HL)
            {
                Regs.PC = Regs.HL;
                TStates += 4;
            };

            opcodeTable[0xF9] = () => // LD SP,HL
            {
                Regs.SP = Regs.HL;
                TStates += 6;
            };

            for (int i = 0; i < 8; i++)
            {
                int r = i;
                opcodeTable[0x80 + i] = () => AddA(GetReg((byte)r), false, 4);
                opcodeTable[0x88 + i] = () => AddA(GetReg((byte)r), true, 4);
                opcodeTable[0x90 + i] = () => SubA(GetReg((byte)r), false, 4);
                opcodeTable[0x98 + i] = () => SubA(GetReg((byte)r), true, 4);
                opcodeTable[0xA0 + i] = () => AndA(GetReg((byte)r), 4);
                opcodeTable[0xA8 + i] = () => XorA(GetReg((byte)r), 4);
                opcodeTable[0xB0 + i] = () => OrA(GetReg((byte)r), 4);
                opcodeTable[0xB8 + i] = () => CpA(GetReg((byte)r), 4);
            }

            opcodeTable[0xC6] = () => AddA(FetchByte(), false, 7);
            opcodeTable[0xCE] = () => AddA(FetchByte(), true, 7);
            opcodeTable[0xD6] = () => SubA(FetchByte(), false, 7);
            opcodeTable[0xDE] = () => SubA(FetchByte(), true, 7);
            opcodeTable[0xE6] = () => AndA(FetchByte(), 7);
            opcodeTable[0xEE] = () => XorA(FetchByte(), 7);
            opcodeTable[0xF6] = () => OrA(FetchByte(), 7);
            opcodeTable[0xFE] = () => CpA(FetchByte(), 7);

            opcodeTable[0xC3] = () => // JP nn
            {
                Regs.PC = FetchWord();
                TStates += 10;
            };

            opcodeTable[0xC2] = () => JPcc((Regs.F & 0x40) == 0);
            opcodeTable[0xCA] = () => JPcc((Regs.F & 0x40) != 0);
            opcodeTable[0xD2] = () => JPcc((Regs.F & 0x01) == 0);
            opcodeTable[0xDA] = () => JPcc((Regs.F & 0x01) != 0);
            opcodeTable[0xE2] = () => JPcc((Regs.F & 0x04) == 0);
            opcodeTable[0xEA] = () => JPcc((Regs.F & 0x04) != 0);
            opcodeTable[0xF2] = () => JPcc((Regs.F & 0x80) == 0);
            opcodeTable[0xFA] = () => JPcc((Regs.F & 0x80) != 0);

            opcodeTable[0xCD] = () => // CALL nn
            {
                ushort addr = FetchWord();
                Push(Regs.PC);
                Regs.PC = addr;
                TStates += 17;
            };

            opcodeTable[0xC4] = () => CALLcc((Regs.F & 0x40) == 0);
            opcodeTable[0xCC] = () => CALLcc((Regs.F & 0x40) != 0);
            opcodeTable[0xD4] = () => CALLcc((Regs.F & 0x01) == 0);
            opcodeTable[0xDC] = () => CALLcc((Regs.F & 0x01) != 0);
            opcodeTable[0xE4] = () => CALLcc((Regs.F & 0x04) == 0);
            opcodeTable[0xEC] = () => CALLcc((Regs.F & 0x04) != 0);
            opcodeTable[0xF4] = () => CALLcc((Regs.F & 0x80) == 0);
            opcodeTable[0xFC] = () => CALLcc((Regs.F & 0x80) != 0);

            opcodeTable[0xC9] = () => // RET
            {
                Regs.PC = Pop();
                TStates += 10;
            };

            opcodeTable[0xC0] = () => RETcc((Regs.F & 0x40) == 0);
            opcodeTable[0xC8] = () => RETcc((Regs.F & 0x40) != 0);
            opcodeTable[0xD0] = () => RETcc((Regs.F & 0x01) == 0);
            opcodeTable[0xD8] = () => RETcc((Regs.F & 0x01) != 0);
            opcodeTable[0xE0] = () => RETcc((Regs.F & 0x04) == 0);
            opcodeTable[0xE8] = () => RETcc((Regs.F & 0x04) != 0);
            opcodeTable[0xF0] = () => RETcc((Regs.F & 0x80) == 0);
            opcodeTable[0xF8] = () => RETcc((Regs.F & 0x80) != 0);

            opcodeTable[0xC5] = () => { Push(Regs.BC); TStates += 11; };
            opcodeTable[0xD5] = () => { Push(Regs.DE); TStates += 11; };
            opcodeTable[0xE5] = () => { Push(Regs.HL); TStates += 11; };
            opcodeTable[0xF5] = () => { Push(Regs.AF); TStates += 11; };

            opcodeTable[0xC1] = () => { Regs.BC = Pop(); TStates += 10; };
            opcodeTable[0xD1] = () => { Regs.DE = Pop(); TStates += 10; };
            opcodeTable[0xE1] = () => { Regs.HL = Pop(); TStates += 10; };
            opcodeTable[0xF1] = () => { Regs.AF = Pop(); TStates += 10; };

/*            opcodeTable[0xF3] = () => // DI
            {
                RecordInterruptEvent("DI_EXEC", true);

                foreach (var line in recentTrace)
                    RecordInterruptEvent("TRACE_BEFORE_DI " + line, true);

                IFF1 = false;
                IFF2 = false;
                eiDelay = 0;

                RecordInterruptEvent("DI_EFFECT", true);
                TStates += 4;
            };*/
            opcodeTable[0xF3] = () => // DI
            {
                RecordInterruptEvent(
                    $"DI_EXEC PC={lastPcBeforeStep:X4} T={TStates} " +
                    $"SP={Regs.SP:X4} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4} " +
                    $"IX={Regs.IX:X4} IY={Regs.IY:X4} " +
                    $"IFF1={(IFF1 ? 1 : 0)} IFF2={(IFF2 ? 1 : 0)}",
                    true);

                foreach (var line in recentTrace)
                    RecordInterruptEvent("TRACE_BEFORE_DI " + line, true);

                IFF1 = false;
                IFF2 = false;
                eiDelay = 0;

                RecordInterruptEvent("DI_EFFECT", true);
                TStates += 4;
            };

            opcodeTable[0xFB] = () => // EI
            {
                RecordInterruptEvent("EI_EXEC", true);
                eiDelay = 2;
                TStates += 4;
            };

            opcodeTable[0xD3] = () => // OUT (n),A
            {
                byte low = FetchByte();
                ushort port = (ushort)((Regs.A << 8) | low);
                WritePortTimed(port, Regs.A, 11);
            };

            opcodeTable[0xDB] = () => // IN A,(n)
            {
                byte low = FetchByte();
                ushort port = (ushort)((Regs.A << 8) | low);
                Regs.A = ReadPort(port);
                TStates += 11;
            };

            for (int i = 0; i < 8; i++)
            {
                int addr = i * 8;
                opcodeTable[0xC7 + i * 8] = () =>
                {
                    Push(Regs.PC);
                    Regs.PC = (ushort)addr;
                    TStates += 11;
                };
            }
        }

        // =========================================================
        // Flow helpers
        // =========================================================

        private void RETcc(bool condition)
        {
            if (condition)
            {
                Regs.PC = Pop();
                TStates += 11;
            }
            else
            {
                TStates += 5;
            }
        }

        private void CALLcc(bool condition)
        {
            ushort addr = FetchWord();
            if (condition)
            {
                Push(Regs.PC);
                Regs.PC = addr;
                TStates += 17;
            }
            else
            {
                TStates += 10;
            }
        }

        private void JRcc(bool condition)
        {
            sbyte e = (sbyte)FetchByte();
            if (condition)
                Regs.PC = (ushort)(Regs.PC + e);
            TStates += 12;
        }

        private void JPcc(bool condition)
        {
            ushort addr = FetchWord();
            if (condition)
                Regs.PC = addr;
            TStates += 10;
        }
    }
}
