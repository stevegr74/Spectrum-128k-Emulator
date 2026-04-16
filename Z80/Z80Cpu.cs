using System;
using System.Collections.Generic;

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
        public Action<string>? Trace { get; set; }

        private bool halted = false;
        public bool IsHalted => halted;
        public bool InterruptPending { get; set; } = false;
        public bool IFF1 { get; private set; } = false;
        public bool IFF2 { get; private set; } = false;

        private int eiDelay = 0;
        private int interruptMode = 1;

        private readonly Action[] opcodeTable = new Action[256];
        private readonly Action[] cbOpcodeTable = new Action[256];
        private readonly Action[] edOpcodeTable = new Action[256];
        private readonly Action[] ddOpcodeTable = new Action[256];
        private readonly Action[] fdOpcodeTable = new Action[256];

        private readonly Queue<string> recentTrace = new Queue<string>();
        private bool reportedHighRamEntry = false;
        private bool flagsChangedLastInstruction = false;
        private byte lastFlagsBeforeInstruction = 0;

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

        private byte IXH
        {
            get => (byte)(Regs.IX >> 8);
            set => Regs.IX = (ushort)((value << 8) | (Regs.IX & 0x00FF));
        }

        private byte IXL
        {
            get => (byte)(Regs.IX & 0x00FF);
            set => Regs.IX = (ushort)((Regs.IX & 0xFF00) | value);
        }

        private byte IYH
        {
            get => (byte)(Regs.IY >> 8);
            set => Regs.IY = (ushort)((value << 8) | (Regs.IY & 0x00FF));
        }

        private byte IYL
        {
            get => (byte)(Regs.IY & 0x00FF);
            set => Regs.IY = (ushort)((Regs.IY & 0xFF00) | value);
        }

        public Z80Cpu()
        {
            InitializeOpcodeTable();
            InitializeCBTable();
            InitializeEDTable();
            InitializeDDTable();
            InitializeFDTable();
        }

        // =========================================================
        // Public control
        // =========================================================

        public void Reset()
        {
            Regs.PC = 0;
            Regs.SP = 0xFFFF;
            Regs.I = 0;
            Regs.R = 0;

            halted = false;
            InterruptPending = false;
            IFF1 = false;
            IFF2 = false;
            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;

            eiDelay = 0;
            interruptMode = 1;

            reportedHighRamEntry = false;
            recentTrace.Clear();
            TStates = 0;
        }

        public void ExecuteCycles(ulong cycles)
        {
            ulong target = TStates + cycles;

            while (TStates < target)
            {
                if (InterruptPending && IFF1)
                {
                    if (Regs.SP < 0x4000)
                    {
                        Trace?.Invoke($"INT with BAD SP: PC={Regs.PC:X4} SP={Regs.SP:X4} IX={Regs.IX:X4} IY={Regs.IY:X4}");
                    }

                    InterruptPending = false;
                    halted = false;

                    IFF1 = false;
                    IFF2 = false;

                    Push(Regs.PC);

                    switch (interruptMode)
                    {
                        case 0:
                        case 1:
                            Regs.PC = 0x0038;
                            break;

                        case 2:
                            ushort vector = (ushort)((Regs.I << 8) | 0xFF);
                            byte low = ReadMemory(vector);
                            byte high = ReadMemory((ushort)(vector + 1));
                            Regs.PC = (ushort)(low | (high << 8));
                            break;
                    }

                    TStates += 13;
                    continue;
                }

                if (halted)
                {
                    TStates += 4;
                    continue;
                }

                Step();
            }
        }

        public void Step()
        {
            ushort pcBefore = Regs.PC;
            ushort spBefore = Regs.SP;
            ushort ixBefore = Regs.IX;
            ushort iyBefore = Regs.IY;
            byte fBefore = Regs.F;

            byte op = FetchByte();

            RecordTrace(pcBefore, op);

            if (!reportedHighRamEntry && pcBefore >= 0xC000)
            {
                reportedHighRamEntry = true;
                Trace?.Invoke("=== ENTERED HIGH RAM ===");
                foreach (var line in recentTrace)
                    Trace?.Invoke(line);
            }

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
            else if (op == 0xDD)
            {
                byte ddOp = FetchByte();
                if (ddOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchByte();
                    ExecuteIndexedCB(Regs.IX, disp, cbOp);
                }
                else
                {
                    ddOpcodeTable[ddOp]();
                }
            }
            else if (op == 0xFD)
            {
                byte fdOp = FetchByte();
                if (fdOp == 0xCB)
                {
                    sbyte disp = (sbyte)FetchByte();
                    byte cbOp = FetchByte();
                    ExecuteIndexedCB(Regs.IY, disp, cbOp);
                }
                else
                {
                    fdOpcodeTable[fdOp]();
                }
            }
            else
            {
                opcodeTable[op]();
            }

            if (spBefore != Regs.SP || ixBefore != Regs.IX || iyBefore != Regs.IY)
            {
                Trace?.Invoke(
                    $"STATE PC={pcBefore:X4} OP={op:X2} SP {spBefore:X4}->{Regs.SP:X4} IX {ixBefore:X4}->{Regs.IX:X4} IY {iyBefore:X4}->{Regs.IY:X4}");
            }

            if (Regs.SP < 0x4000)
            {
                Trace?.Invoke(
                    $"BAD SP after PC={pcBefore:X4} OP={op:X2}: SP={Regs.SP:X4} IX={Regs.IX:X4} IY={Regs.IY:X4}");
            }

            if (eiDelay > 0)
            {
                eiDelay--;
                if (eiDelay == 0)
                {
                    IFF1 = true;
                    IFF2 = true;
                }
            }

            lastFlagsBeforeInstruction = fBefore;
            flagsChangedLastInstruction = Regs.F != fBefore;
        }

        public void RestoreInterruptState(bool iff1, bool iff2, int interruptMode)
        {
            IFF1 = iff1;
            IFF2 = iff2;
            this.interruptMode = interruptMode & 0x03;
            eiDelay = 0;
        }

        public void ClearSnapshotExecutionState()
        {
            halted = false;
            InterruptPending = false;
            TStates = 0;
            flagsChangedLastInstruction = false;
            lastFlagsBeforeInstruction = 0;            
        }

        // =========================================================
        // Opcode tables
        // =========================================================

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
                byte oldA = Regs.A;
                byte oldF = Regs.F;

                bool oldC = (oldF & (1 << (int)Flag.C)) != 0;
                bool oldH = (oldF & (1 << (int)Flag.H)) != 0;
                bool oldN = (oldF & (1 << (int)Flag.N)) != 0;

                int correction = 0;
                bool carry = oldC;

                if (!oldN)
                {
                    if (oldH || (oldA & 0x0F) > 0x09)
                        correction |= 0x06;

                    if (oldC || oldA > 0x99)
                    {
                        correction |= 0x60;
                        carry = true;
                    }

                    Regs.A = (byte)(oldA + correction);
                    SetFlag(Flag.H, ((oldA & 0x0F) + (correction & 0x0F)) > 0x0F);
                }
                else
                {
                    if (oldH || (oldA & 0x0F) > 0x09)
                        correction |= 0x06;

                    if (oldC)
                        correction |= 0x60;

                    Regs.A = (byte)(oldA - correction);
                    SetFlag(Flag.H, ((oldA ^ Regs.A) & 0x10) != 0);
                }

                SetFlag(Flag.S, (Regs.A & 0x80) != 0);
                SetFlag(Flag.Z, Regs.A == 0);
                SetFlag(Flag.P, Parity(Regs.A));
                SetFlag(Flag.C, carry);
                // N unchanged
                CopyUndocumentedFlagsFrom(Regs.A);

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

            opcodeTable[0xF3] = () => // DI
            {
                IFF1 = false;
                IFF2 = false;
                eiDelay = 0;
                TStates += 4;
            };

            opcodeTable[0xFB] = () => // EI
            {
                eiDelay = 2;
                TStates += 4;
            };

            opcodeTable[0xD3] = () => // OUT (n),A
            {
                byte low = FetchByte();
                ushort port = (ushort)((Regs.A << 8) | low);
                WritePort(port, Regs.A);
                TStates += 11;
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
            edOpcodeTable[0x41] = () => { WritePort(Regs.BC, Regs.B); TStates += 12; };
            edOpcodeTable[0x49] = () => { WritePort(Regs.BC, Regs.C); TStates += 12; };
            edOpcodeTable[0x51] = () => { WritePort(Regs.BC, Regs.D); TStates += 12; };
            edOpcodeTable[0x59] = () => { WritePort(Regs.BC, Regs.E); TStates += 12; };
            edOpcodeTable[0x61] = () => { WritePort(Regs.BC, Regs.H); TStates += 12; };
            edOpcodeTable[0x69] = () => { WritePort(Regs.BC, Regs.L); TStates += 12; };
            edOpcodeTable[0x71] = () => { WritePort(Regs.BC, 0); TStates += 12; };
            edOpcodeTable[0x79] = () => { WritePort(Regs.BC, Regs.A); TStates += 12; };

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
        }

        private void CopyUndocumentedFlagsFrom(byte value)
        {
            SetFlag(Flag.F3, (value & 0x08) != 0);
            SetFlag(Flag.F5, (value & 0x20) != 0);
        }

        private void ApplyScfCcfUndocumentedFlags()
        {
            byte source = flagsChangedLastInstruction
                ? Regs.A
                : (byte)(Regs.A | lastFlagsBeforeInstruction);

            SetFlag(Flag.F3, (source & 0x08) != 0);
            SetFlag(Flag.F5, (source & 0x20) != 0);
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

        private void InitializeDDTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                ddOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL DD 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4} IX=0x{Regs.IX:X4}");
                    opcodeTable[op]();
                };
            }

            // 16-bit IX core
            ddOpcodeTable[0x09] = () => { Regs.IX = Add16(Regs.IX, Regs.BC); TStates += 15; };
            ddOpcodeTable[0x19] = () => { Regs.IX = Add16(Regs.IX, Regs.DE); TStates += 15; };
            ddOpcodeTable[0x21] = () => { Regs.IX = FetchWord(); TStates += 10; };
            ddOpcodeTable[0x22] = () =>
            {
                ushort addr = FetchWord();
                WriteMemory(addr, (byte)(Regs.IX & 0xFF));
                WriteMemory((ushort)(addr + 1), (byte)(Regs.IX >> 8));
                TStates += 16;
            };
            ddOpcodeTable[0x23] = () => { Regs.IX++; TStates += 10; };
            ddOpcodeTable[0x24] = () => { IXH = Inc8(IXH); TStates += 8; };
            ddOpcodeTable[0x25] = () => { IXH = Dec8(IXH); TStates += 8; };
            ddOpcodeTable[0x26] = () => { IXH = FetchByte(); TStates += 11; };
            ddOpcodeTable[0x29] = () => { Regs.IX = Add16(Regs.IX, Regs.IX); TStates += 15; };
            ddOpcodeTable[0x2A] = () =>
            {
                ushort addr = FetchWord();
                byte low = ReadMemory(addr);
                byte high = ReadMemory((ushort)(addr + 1));
                Regs.IX = (ushort)(low | (high << 8));
                TStates += 16;
            };
            ddOpcodeTable[0x2B] = () => { Regs.IX--; TStates += 10; };
            ddOpcodeTable[0x2C] = () => { IXL = Inc8(IXL); TStates += 8; };
            ddOpcodeTable[0x2D] = () => { IXL = Dec8(IXL); TStates += 8; };
            ddOpcodeTable[0x2E] = () => { IXL = FetchByte(); TStates += 11; };
            ddOpcodeTable[0x34] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IX + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old + 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x0F);
                SetFlag(Flag.P, old == 0x7F);
                SetFlag(Flag.N, false);
                TStates += 23;
            };
            ddOpcodeTable[0x35] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IX + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old - 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x00);
                SetFlag(Flag.P, old == 0x80);
                SetFlag(Flag.N, true);
                TStates += 23;
            };
            ddOpcodeTable[0x36] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                byte n = FetchByte();
                WriteMemory((ushort)(Regs.IX + d), n);
                TStates += 19;
            };
            ddOpcodeTable[0x39] = () => { Regs.IX = Add16(Regs.IX, Regs.SP); TStates += 15; };

            // LD r, IXH/IXL/(IX+d)
            ddOpcodeTable[0x44] = () => { Regs.B = IXH; TStates += 8; };
            ddOpcodeTable[0x45] = () => { Regs.B = IXL; TStates += 8; };
            ddOpcodeTable[0x46] = () => { sbyte d = (sbyte)FetchByte(); Regs.B = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            ddOpcodeTable[0x4C] = () => { Regs.C = IXH; TStates += 8; };
            ddOpcodeTable[0x4D] = () => { Regs.C = IXL; TStates += 8; };
            ddOpcodeTable[0x4E] = () => { sbyte d = (sbyte)FetchByte(); Regs.C = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            ddOpcodeTable[0x54] = () => { Regs.D = IXH; TStates += 8; };
            ddOpcodeTable[0x55] = () => { Regs.D = IXL; TStates += 8; };
            ddOpcodeTable[0x56] = () => { sbyte d = (sbyte)FetchByte(); Regs.D = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            ddOpcodeTable[0x5C] = () => { Regs.E = IXH; TStates += 8; };
            ddOpcodeTable[0x5D] = () => { Regs.E = IXL; TStates += 8; };
            ddOpcodeTable[0x5E] = () => { sbyte d = (sbyte)FetchByte(); Regs.E = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            // LD IXH/IXL, r
            ddOpcodeTable[0x60] = () => { IXH = Regs.B; TStates += 8; };
            ddOpcodeTable[0x61] = () => { IXH = Regs.C; TStates += 8; };
            ddOpcodeTable[0x62] = () => { IXH = Regs.D; TStates += 8; };
            ddOpcodeTable[0x63] = () => { IXH = Regs.E; TStates += 8; };

            ddOpcodeTable[0x64] = () => { IXH = IXH; TStates += 8; };
            ddOpcodeTable[0x65] = () => { IXH = IXL; TStates += 8; };
            ddOpcodeTable[0x6C] = () => { IXL = IXH; TStates += 8; };
            ddOpcodeTable[0x6D] = () => { IXL = IXL; TStates += 8; };

            ddOpcodeTable[0x67] = () => { IXH = Regs.A; TStates += 8; };
            ddOpcodeTable[0x68] = () => { IXL = Regs.B; TStates += 8; };
            ddOpcodeTable[0x69] = () => { IXL = Regs.C; TStates += 8; };
            ddOpcodeTable[0x6A] = () => { IXL = Regs.D; TStates += 8; };
            ddOpcodeTable[0x6B] = () => { IXL = Regs.E; TStates += 8; };

            ddOpcodeTable[0x66] = () => { sbyte d = (sbyte)FetchByte(); Regs.H = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };
            ddOpcodeTable[0x6E] = () => { sbyte d = (sbyte)FetchByte(); Regs.L = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };
            ddOpcodeTable[0x6F] = () => { IXL = Regs.A; TStates += 8; };

            // LD (IX+d), r
            for (int r = 0; r < 8; r++)
            {
                if (r == 6) continue;
                int rr = r;
                ddOpcodeTable[0x70 + r] = () =>
                {
                    sbyte d = (sbyte)FetchByte();
                    ushort addr = (ushort)(Regs.IX + d);
                    byte value = rr switch
                    {
                        0 => Regs.B,
                        1 => Regs.C,
                        2 => Regs.D,
                        3 => Regs.E,
                        4 => Regs.H,
                        5 => Regs.L,
                        7 => Regs.A,
                        _ => 0
                    };
                    WriteMemory(addr, value);
                    TStates += 19;
                };
            }

            ddOpcodeTable[0x7C] = () => { Regs.A = IXH; TStates += 8; };
            ddOpcodeTable[0x7D] = () => { Regs.A = IXL; TStates += 8; };
            ddOpcodeTable[0x7E] = () => { sbyte d = (sbyte)FetchByte(); Regs.A = ReadMemory((ushort)(Regs.IX + d)); TStates += 19; };

            // ALU IXH/IXL/(IX+d)
            ddOpcodeTable[0x84] = () => AddA(IXH, false, 8);
            ddOpcodeTable[0x85] = () => AddA(IXL, false, 8);
            ddOpcodeTable[0x86] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IX + d)), false, 19); };
            ddOpcodeTable[0x8C] = () => AddA(IXH, true, 8);
            ddOpcodeTable[0x8D] = () => AddA(IXL, true, 8);
            ddOpcodeTable[0x8E] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IX + d)), true, 19); };

            ddOpcodeTable[0x94] = () => SubA(IXH, false, 8);
            ddOpcodeTable[0x95] = () => SubA(IXL, false, 8);
            ddOpcodeTable[0x96] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IX + d)), false, 19); };
            ddOpcodeTable[0x9C] = () => SubA(IXH, true, 8);
            ddOpcodeTable[0x9D] = () => SubA(IXL, true, 8);
            ddOpcodeTable[0x9E] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IX + d)), true, 19); };

            ddOpcodeTable[0xA4] = () => AndA(IXH, 8);
            ddOpcodeTable[0xA5] = () => AndA(IXL, 8);
            ddOpcodeTable[0xA6] = () => { sbyte d = (sbyte)FetchByte(); AndA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xAC] = () => XorA(IXH, 8);
            ddOpcodeTable[0xAD] = () => XorA(IXL, 8);
            ddOpcodeTable[0xAE] = () => { sbyte d = (sbyte)FetchByte(); XorA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xB4] = () => OrA(IXH, 8);
            ddOpcodeTable[0xB5] = () => OrA(IXL, 8);
            ddOpcodeTable[0xB6] = () => { sbyte d = (sbyte)FetchByte(); OrA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xBC] = () => CpA(IXH, 8);
            ddOpcodeTable[0xBD] = () => CpA(IXL, 8);
            ddOpcodeTable[0xBE] = () => { sbyte d = (sbyte)FetchByte(); CpA(ReadMemory((ushort)(Regs.IX + d)), 19); };

            ddOpcodeTable[0xE1] = () => { Regs.IX = Pop(); TStates += 10; };
            ddOpcodeTable[0xE3] = () =>
            {
                byte low = ReadMemory(Regs.SP);
                byte high = ReadMemory((ushort)(Regs.SP + 1));
                WriteMemory(Regs.SP, (byte)(Regs.IX & 0xFF));
                WriteMemory((ushort)(Regs.SP + 1), (byte)(Regs.IX >> 8));
                Regs.IX = (ushort)(low | (high << 8));
                TStates += 19;
            };
            ddOpcodeTable[0xE5] = () => { Push(Regs.IX); TStates += 11; };
            ddOpcodeTable[0xE9] = () => { Regs.PC = Regs.IX; TStates += 8; };
            ddOpcodeTable[0xF9] = () => { Regs.SP = Regs.IX; TStates += 6; };
        }

        private void InitializeFDTable()
        {
            for (int i = 0; i < 256; i++)
            {
                int op = i;
                fdOpcodeTable[i] = () =>
                {
                    Trace?.Invoke($"UNIMPL FD 0x{op:X2} at PC=0x{(ushort)(Regs.PC - 2):X4} IY=0x{Regs.IY:X4}");
                    opcodeTable[op]();
                };
            }

            // 16-bit IY core
            fdOpcodeTable[0x09] = () => { Regs.IY = Add16(Regs.IY, Regs.BC); TStates += 15; };
            fdOpcodeTable[0x19] = () => { Regs.IY = Add16(Regs.IY, Regs.DE); TStates += 15; };
            fdOpcodeTable[0x21] = () => { Regs.IY = FetchWord(); TStates += 10; };
            fdOpcodeTable[0x22] = () =>
            {
                ushort addr = FetchWord();
                WriteMemory(addr, (byte)(Regs.IY & 0xFF));
                WriteMemory((ushort)(addr + 1), (byte)(Regs.IY >> 8));
                TStates += 16;
            };
            fdOpcodeTable[0x23] = () => { Regs.IY++; TStates += 10; };
            fdOpcodeTable[0x24] = () => { IYH = Inc8(IYH); TStates += 8; };
            fdOpcodeTable[0x25] = () => { IYH = Dec8(IYH); TStates += 8; };
            fdOpcodeTable[0x26] = () => { IYH = FetchByte(); TStates += 11; };
            fdOpcodeTable[0x29] = () => { Regs.IY = Add16(Regs.IY, Regs.IY); TStates += 15; };
            fdOpcodeTable[0x2A] = () =>
            {
                ushort addr = FetchWord();
                byte low = ReadMemory(addr);
                byte high = ReadMemory((ushort)(addr + 1));
                Regs.IY = (ushort)(low | (high << 8));
                TStates += 16;
            };
            fdOpcodeTable[0x2B] = () => { Regs.IY--; TStates += 10; };
            fdOpcodeTable[0x2C] = () => { IYL = Inc8(IYL); TStates += 8; };
            fdOpcodeTable[0x2D] = () => { IYL = Dec8(IYL); TStates += 8; };
            fdOpcodeTable[0x2E] = () => { IYL = FetchByte(); TStates += 11; };
            fdOpcodeTable[0x34] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IY + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old + 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x0F);
                SetFlag(Flag.P, old == 0x7F);
                SetFlag(Flag.N, false);
                TStates += 23;
            };
            fdOpcodeTable[0x35] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IY + d);
                byte old = ReadMemory(addr);
                byte value = (byte)(old - 1);
                WriteMemory(addr, value);
                SetFlag(Flag.S, (value & 0x80) != 0);
                SetFlag(Flag.Z, value == 0);
                SetFlag(Flag.H, (old & 0x0F) == 0x00);
                SetFlag(Flag.P, old == 0x80);
                SetFlag(Flag.N, true);
                TStates += 23;
            };
            fdOpcodeTable[0x36] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                byte n = FetchByte();
                WriteMemory((ushort)(Regs.IY + d), n);
                TStates += 19;
            };
            fdOpcodeTable[0x39] = () => { Regs.IY = Add16(Regs.IY, Regs.SP); TStates += 15; };

            // LD r, IYH/IYL/(IY+d)
            fdOpcodeTable[0x44] = () => { Regs.B = IYH; TStates += 8; };
            fdOpcodeTable[0x45] = () => { Regs.B = IYL; TStates += 8; };
            fdOpcodeTable[0x46] = () => { sbyte d = (sbyte)FetchByte(); Regs.B = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            fdOpcodeTable[0x4C] = () => { Regs.C = IYH; TStates += 8; };
            fdOpcodeTable[0x4D] = () => { Regs.C = IYL; TStates += 8; };
            fdOpcodeTable[0x4E] = () => { sbyte d = (sbyte)FetchByte(); Regs.C = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            fdOpcodeTable[0x54] = () => { Regs.D = IYH; TStates += 8; };
            fdOpcodeTable[0x55] = () => { Regs.D = IYL; TStates += 8; };
            fdOpcodeTable[0x56] = () => { sbyte d = (sbyte)FetchByte(); Regs.D = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            fdOpcodeTable[0x5C] = () => { Regs.E = IYH; TStates += 8; };
            fdOpcodeTable[0x5D] = () => { Regs.E = IYL; TStates += 8; };
            fdOpcodeTable[0x5E] = () => { sbyte d = (sbyte)FetchByte(); Regs.E = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            // LD IYH/IYL, r
            fdOpcodeTable[0x60] = () => { IYH = Regs.B; TStates += 8; };
            fdOpcodeTable[0x61] = () => { IYH = Regs.C; TStates += 8; };
            fdOpcodeTable[0x62] = () => { IYH = Regs.D; TStates += 8; };
            fdOpcodeTable[0x63] = () => { IYH = Regs.E; TStates += 8; };
            fdOpcodeTable[0x64] = () => { IYH = IYH; TStates += 8; };
            fdOpcodeTable[0x65] = () => { IYH = IYL; TStates += 8; };
            fdOpcodeTable[0x67] = () => { IYH = Regs.A; TStates += 8; };

            fdOpcodeTable[0x68] = () => { IYL = Regs.B; TStates += 8; };
            fdOpcodeTable[0x69] = () => { IYL = Regs.C; TStates += 8; };
            fdOpcodeTable[0x6A] = () => { IYL = Regs.D; TStates += 8; };
            fdOpcodeTable[0x6B] = () => { IYL = Regs.E; TStates += 8; };
            fdOpcodeTable[0x6C] = () => { IYL = IYH; TStates += 8; };
            fdOpcodeTable[0x6D] = () => { IYL = IYL; TStates += 8; };            

            fdOpcodeTable[0x66] = () => { sbyte d = (sbyte)FetchByte(); Regs.H = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };
            fdOpcodeTable[0x6E] = () => { sbyte d = (sbyte)FetchByte(); Regs.L = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };
            fdOpcodeTable[0x6F] = () => { IYL = Regs.A; TStates += 8; };

            // LD (IY+d), r
            for (int r = 0; r < 8; r++)
            {
                if (r == 6) continue;
                int rr = r;
                fdOpcodeTable[0x70 + r] = () =>
                {
                    sbyte d = (sbyte)FetchByte();
                    ushort addr = (ushort)(Regs.IY + d);
                    byte value = rr switch
                    {
                        0 => Regs.B,
                        1 => Regs.C,
                        2 => Regs.D,
                        3 => Regs.E,
                        4 => Regs.H,
                        5 => Regs.L,
                        7 => Regs.A,
                        _ => 0
                    };
                    WriteMemory(addr, value);
                    TStates += 19;
                };
            }

            fdOpcodeTable[0x7C] = () => { Regs.A = IYH; TStates += 8; };
            fdOpcodeTable[0x7D] = () => { Regs.A = IYL; TStates += 8; };
            fdOpcodeTable[0x7E] = () => { sbyte d = (sbyte)FetchByte(); Regs.A = ReadMemory((ushort)(Regs.IY + d)); TStates += 19; };

            // ALU IYH/IYL/(IY+d)
            fdOpcodeTable[0x84] = () => AddA(IYH, false, 8);
            fdOpcodeTable[0x85] = () => AddA(IYL, false, 8);
            fdOpcodeTable[0x86] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IY + d)), false, 19); };
            fdOpcodeTable[0x8C] = () => AddA(IYH, true, 8);
            fdOpcodeTable[0x8D] = () => AddA(IYL, true, 8);
            fdOpcodeTable[0x8E] = () => { sbyte d = (sbyte)FetchByte(); AddA(ReadMemory((ushort)(Regs.IY + d)), true, 19); };

            fdOpcodeTable[0x94] = () => SubA(IYH, false, 8);
            fdOpcodeTable[0x95] = () => SubA(IYL, false, 8);
            fdOpcodeTable[0x96] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IY + d)), false, 19); };
            fdOpcodeTable[0x9C] = () => SubA(IYH, true, 8);
            fdOpcodeTable[0x9D] = () => SubA(IYL, true, 8);
            fdOpcodeTable[0x9E] = () => { sbyte d = (sbyte)FetchByte(); SubA(ReadMemory((ushort)(Regs.IY + d)), true, 19); };

            fdOpcodeTable[0xA4] = () => AndA(IYH, 8);
            fdOpcodeTable[0xA5] = () => AndA(IYL, 8);
            fdOpcodeTable[0xA6] = () => { sbyte d = (sbyte)FetchByte(); AndA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xAC] = () => XorA(IYH, 8);
            fdOpcodeTable[0xAD] = () => XorA(IYL, 8);
            fdOpcodeTable[0xAE] = () => { sbyte d = (sbyte)FetchByte(); XorA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xB4] = () => OrA(IYH, 8);
            fdOpcodeTable[0xB5] = () => OrA(IYL, 8);
            fdOpcodeTable[0xB6] = () => { sbyte d = (sbyte)FetchByte(); OrA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xBC] = () => CpA(IYH, 8);
            fdOpcodeTable[0xBD] = () => CpA(IYL, 8);
            fdOpcodeTable[0xBE] = () => { sbyte d = (sbyte)FetchByte(); CpA(ReadMemory((ushort)(Regs.IY + d)), 19); };

            fdOpcodeTable[0xE1] = () => { Regs.IY = Pop(); TStates += 10; };
            fdOpcodeTable[0xE3] = () =>
            {
                byte low = ReadMemory(Regs.SP);
                byte high = ReadMemory((ushort)(Regs.SP + 1));
                WriteMemory(Regs.SP, (byte)(Regs.IY & 0xFF));
                WriteMemory((ushort)(Regs.SP + 1), (byte)(Regs.IY >> 8));
                Regs.IY = (ushort)(low | (high << 8));
                TStates += 19;
            };
            fdOpcodeTable[0xE5] = () => { Push(Regs.IY); TStates += 11; };
            fdOpcodeTable[0xE9] = () => { Regs.PC = Regs.IY; TStates += 8; };
            fdOpcodeTable[0xF9] = () => { Regs.SP = Regs.IY; TStates += 6; };
        }

        // =========================================================
        // Indexed CB
        // =========================================================

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

        // =========================================================
        // Trace
        // =========================================================

        private void RecordTrace(ushort pcBefore, byte op)
        {
            string line = $"PC={pcBefore:X4} OP={op:X2} SP={Regs.SP:X4} AF={Regs.AF:X4} BC={Regs.BC:X4} DE={Regs.DE:X4} HL={Regs.HL:X4}";
            recentTrace.Enqueue(line);
            if (recentTrace.Count > 40)
                recentTrace.Dequeue();
        }

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

        private byte Inc8(byte old)
        {
            byte value = (byte)(old + 1);
            SetFlag(Flag.S, (value & 0x80) != 0);
            SetFlag(Flag.Z, value == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x0F);
            SetFlag(Flag.P, old == 0x7F);
            SetFlag(Flag.N, false);
            return value;
        }

        private byte Dec8(byte old)
        {
            byte value = (byte)(old - 1);
            SetFlag(Flag.S, (value & 0x80) != 0);
            SetFlag(Flag.Z, value == 0);
            SetFlag(Flag.H, (old & 0x0F) == 0x00);
            SetFlag(Flag.P, old == 0x80);
            SetFlag(Flag.N, true);
            return value;
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

            TStates += (idx == 6 ? 11UL : 4UL);
        }

        // =========================================================
        // ALU helpers
        // =========================================================

        private static bool OverflowAdd(byte a, byte b, byte r)
        {
            return ((a ^ r) & (b ^ r) & 0x80) != 0;
        }

        private static bool OverflowSub(byte a, byte b, byte r)
        {
            return ((a ^ b) & (a ^ r) & 0x80) != 0;
        }

        private void AddA(byte value, bool carry, int baseT)
        {
            int c = carry ? (Regs.F & 0x01) : 0;
            byte a = Regs.A;
            int result = a + value + c;
            byte r = (byte)result;

            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a & 0x0F) + (value & 0x0F) + c) > 0x0F);
            SetFlag(Flag.P, OverflowAdd(a, value, r));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, result > 0xFF);

            TStates += (ulong)baseT;
        }

        private void SubA(byte value, bool carry, int baseT)
        {
            int c = carry ? (Regs.F & 0x01) : 0;
            byte a = Regs.A;
            int result = a - value - c;
            byte r = (byte)result;

            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a ^ value ^ r) & 0x10) != 0);
            SetFlag(Flag.P, OverflowSub(a, value, r));
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, result < 0);

            TStates += (ulong)baseT;
        }

        private void AndA(byte value, int baseT)
        {
            Regs.A &= value;

            SetFlag(Flag.S, (Regs.A & 0x80) != 0);
            SetFlag(Flag.Z, Regs.A == 0);
            SetFlag(Flag.H, true);
            SetFlag(Flag.P, Parity(Regs.A));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, false);

            TStates += (ulong)baseT;
        }

        private void XorA(byte value, int baseT)
        {
            Regs.A ^= value;

            SetFlag(Flag.S, (Regs.A & 0x80) != 0);
            SetFlag(Flag.Z, Regs.A == 0);
            SetFlag(Flag.H, false);
            SetFlag(Flag.P, Parity(Regs.A));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, false);

            TStates += (ulong)baseT;
        }

        private void OrA(byte value, int baseT)
        {
            Regs.A |= value;

            SetFlag(Flag.S, (Regs.A & 0x80) != 0);
            SetFlag(Flag.Z, Regs.A == 0);
            SetFlag(Flag.H, false);
            SetFlag(Flag.P, Parity(Regs.A));
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, false);

            TStates += (ulong)baseT;
        }

        private void CpA(byte value, int baseT)
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

            TStates += (ulong)baseT;
        }

        private ushort Add16(ushort a, ushort b)
        {
            int result = a + b;
            ushort r = (ushort)result;

            SetFlag(Flag.N, false);
            SetFlag(Flag.H, ((a & 0x0FFF) + (b & 0x0FFF)) > 0x0FFF);
            SetFlag(Flag.C, result > 0xFFFF);
            CopyUndocumentedFlagsFrom((byte)(r >> 8));

            return (ushort)result;
        }

        private ushort Add16WithCarry(ushort a, ushort b, bool carry)
        {
            int c = carry ? 1 : 0;
            int result = a + b + c;
            ushort r = (ushort)result;

            SetFlag(Flag.S, (r & 0x8000) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a & 0x0FFF) + (b & 0x0FFF) + c) > 0x0FFF);
            SetFlag(Flag.P, (((a ^ ~b) & (a ^ r)) & 0x8000) != 0);
            SetFlag(Flag.N, false);
            SetFlag(Flag.C, result > 0xFFFF);
            CopyUndocumentedFlagsFrom((byte)(r >> 8));

            return r;
        }

        private ushort Sub16(ushort a, ushort b, bool carry)
        {
            int c = carry ? 1 : 0;
            int result = a - b - c;
            ushort r = (ushort)result;

            SetFlag(Flag.S, (r & 0x8000) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, ((a ^ b ^ r) & 0x1000) != 0);
            SetFlag(Flag.P, (((a ^ b) & (a ^ r)) & 0x8000) != 0);
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, result < 0);
            CopyUndocumentedFlagsFrom((byte)(r >> 8));

            return r;
        }

        private void NegA()
        {
            byte oldA = Regs.A;
            int result = 0 - oldA;
            byte r = (byte)result;

            Regs.A = r;

            SetFlag(Flag.S, (r & 0x80) != 0);
            SetFlag(Flag.Z, r == 0);
            SetFlag(Flag.H, (oldA & 0x0F) != 0);
            SetFlag(Flag.P, oldA == 0x80); // overflow
            SetFlag(Flag.N, true);
            SetFlag(Flag.C, oldA != 0);

            CopyUndocumentedFlagsFrom(r);
        }

        // =========================================================
        // Flag and parity helpers
        // =========================================================

        private void SetShiftRotateFlags(byte result, bool carry)
        {
            Regs.F = 0;
            if ((result & 0x80) != 0) Regs.F |= 0x80;
            if (result == 0) Regs.F |= 0x40;
            if (Parity(result)) Regs.F |= 0x04;
            if (carry) Regs.F |= 0x01;
            Regs.F |= (byte)(result & 0x28);
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

        private bool Parity(byte value)
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

        // =========================================================
        // Stack and fetch helpers
        // =========================================================

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
            Regs.R = (byte)((Regs.R & 0x80) | ((Regs.R + 1) & 0x7F));
            return b;
        }

        private ushort FetchWord()
        {
            byte low = FetchByte();
            byte high = FetchByte();
            return (ushort)(low | (high << 8));
        }
    }
}
