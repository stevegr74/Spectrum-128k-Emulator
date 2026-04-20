using System;

namespace Spectrum128kEmulator.Z80
{
    public partial class Z80Cpu
    {
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
                byte value = Inc8(old);
                WriteMemory(addr, value);
                TStates += 23;
            };
            ddOpcodeTable[0x35] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IX + d);
                byte old = ReadMemory(addr);
                byte value = Dec8(old);
                WriteMemory(addr, value);
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
                byte value = Inc8(old);
                WriteMemory(addr, value);
                TStates += 23;
            };
            fdOpcodeTable[0x35] = () =>
            {
                sbyte d = (sbyte)FetchByte();
                ushort addr = (ushort)(Regs.IY + d);
                byte old = ReadMemory(addr);
                byte value = Dec8(old);
                WriteMemory(addr, value);
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
    }
}
