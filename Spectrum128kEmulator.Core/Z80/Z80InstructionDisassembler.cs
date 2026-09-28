using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator.Z80
{
    public static class Z80InstructionDisassembler
    {
        private static readonly string[] R = ["B", "C", "D", "E", "H", "L", "(HL)", "A"];
        private static readonly string[] Rp = ["BC", "DE", "HL", "SP"];
        private static readonly string[] Rp2 = ["BC", "DE", "HL", "AF"];
        private static readonly string[] Cc = ["NZ", "Z", "NC", "C", "PO", "PE", "P", "M"];
        private static readonly string[] Alu = ["ADD A,", "ADC A,", "SUB ", "SBC A,", "AND ", "XOR ", "OR ", "CP "];
        private static readonly string[] Rot = ["RLC", "RRC", "RL", "RR", "SLA", "SRA", "SLL", "SRL"];
        private static readonly string[] Im = ["0", "0", "1", "2", "0", "0", "1", "2"];

        public static Z80Instruction Disassemble(ushort address, Func<ushort, byte> readMemory)
        {
            ArgumentNullException.ThrowIfNull(readMemory);

            var reader = new InstructionReader(address, readMemory);
            byte op = reader.ReadByte();

            string mnemonic;
            ushort? branchTarget = null;

            switch (op)
            {
                case 0xCB:
                    mnemonic = DecodeCb(reader.ReadByte(), indexedOperand: null);
                    break;
                case 0xED:
                    mnemonic = DecodeEd(reader, out branchTarget);
                    break;
                case 0xDD:
                    mnemonic = DecodeIndexed(reader, "IX", out branchTarget);
                    break;
                case 0xFD:
                    mnemonic = DecodeIndexed(reader, "IY", out branchTarget);
                    break;
                default:
                    mnemonic = DecodeBase(op, reader, out branchTarget);
                    break;
            }

            return reader.ToInstruction(mnemonic, branchTarget);
        }

        public static IReadOnlyList<Z80Instruction> DisassembleBlock(
            ushort address,
            int instructionCount,
            Func<ushort, byte> readMemory)
        {
            if (instructionCount < 0)
                throw new ArgumentOutOfRangeException(nameof(instructionCount));

            var instructions = new List<Z80Instruction>(instructionCount);
            ushort pc = address;
            for (int i = 0; i < instructionCount; i++)
            {
                Z80Instruction instruction = Disassemble(pc, readMemory);
                instructions.Add(instruction);
                pc = (ushort)(pc + instruction.Length);
            }

            return instructions;
        }

        private static string DecodeBase(byte op, InstructionReader reader, out ushort? branchTarget)
        {
            branchTarget = null;
            int x = op >> 6;
            int y = (op >> 3) & 0x07;
            int z = op & 0x07;
            int p = y >> 1;
            int q = y & 0x01;

            if (x == 1)
                return op == 0x76 ? "HALT" : $"LD {R[y]},{R[z]}";

            if (x == 2)
                return $"{Alu[y]}{R[z]}";

            if (x == 0)
            {
                if (z == 0)
                {
                    return y switch
                    {
                        0 => "NOP",
                        1 => "EX AF,AF'",
                        2 => Jr("DJNZ", reader, out branchTarget),
                        3 => Jr("JR", reader, out branchTarget),
                        _ => Jr($"JR {Cc[y - 4]}", reader, out branchTarget),
                    };
                }

                if (z == 1)
                    return q == 0 ? $"LD {Rp[p]},{Word(reader)}" : $"ADD HL,{Rp[p]}";

                if (z == 2)
                {
                    return y switch
                    {
                        0 => "LD (BC),A",
                        1 => "LD A,(BC)",
                        2 => "LD (DE),A",
                        3 => "LD A,(DE)",
                        4 => $"LD ({Word(reader)}),HL",
                        5 => $"LD HL,({Word(reader)})",
                        6 => $"LD ({Word(reader)}),A",
                        _ => $"LD A,({Word(reader)})",
                    };
                }

                if (z == 3)
                    return q == 0 ? $"INC {Rp[p]}" : $"DEC {Rp[p]}";

                if (z == 4)
                    return $"INC {R[y]}";

                if (z == 5)
                    return $"DEC {R[y]}";

                if (z == 6)
                    return $"LD {R[y]},{Byte(reader)}";

                return y switch
                {
                    0 => "RLCA",
                    1 => "RRCA",
                    2 => "RLA",
                    3 => "RRA",
                    4 => "DAA",
                    5 => "CPL",
                    6 => "SCF",
                    _ => "CCF",
                };
            }

            if (z == 0)
                return $"RET {Cc[y]}";

            if (z == 1)
                return q == 0 ? $"POP {Rp2[p]}" : y switch
                {
                    1 => "RET",
                    3 => "EXX",
                    5 => "JP (HL)",
                    7 => "LD SP,HL",
                    _ => Db(reader, op),
                };

            if (z == 2)
            {
                ushort target = reader.ReadWord();
                branchTarget = target;
                return $"JP {Cc[y]},{Addr(target)}";
            }

            if (z == 3)
            {
                return y switch
                {
                    0 => Jp(reader, out branchTarget),
                    2 => $"OUT ({Byte(reader)}),A",
                    3 => $"IN A,({Byte(reader)})",
                    4 => "EX (SP),HL",
                    5 => "EX DE,HL",
                    6 => "DI",
                    7 => "EI",
                    _ => Db(reader, op),
                };
            }

            if (z == 4)
            {
                ushort target = reader.ReadWord();
                branchTarget = target;
                return $"CALL {Cc[y]},{Addr(target)}";
            }

            if (z == 5)
                return q == 0 ? $"PUSH {Rp2[p]}" : y == 1 ? Call(reader, out branchTarget) : Db(reader, op);

            if (z == 6)
                return $"{Alu[y]}{Byte(reader)}";

            return $"RST {y * 8:00H}";
        }

        private static string DecodeEd(InstructionReader reader, out ushort? branchTarget)
        {
            branchTarget = null;
            byte op = reader.ReadByte();
            int x = op >> 6;
            int y = (op >> 3) & 0x07;
            int z = op & 0x07;
            int p = y >> 1;
            int q = y & 0x01;

            if (x == 1)
            {
                if (z == 0)
                    return y == 6 ? "IN (C)" : $"IN {R[y]},(C)";

                if (z == 1)
                    return y == 6 ? "OUT (C),0" : $"OUT (C),{R[y]}";

                if (z == 2)
                    return q == 0 ? $"SBC HL,{Rp[p]}" : $"ADC HL,{Rp[p]}";

                if (z == 3)
                    return q == 0 ? $"LD ({Word(reader)}),{Rp[p]}" : $"LD {Rp[p]},({Word(reader)})";

                if (z == 4)
                    return "NEG";

                if (z == 5)
                    return y == 1 ? "RETI" : "RETN";

                if (z == 6)
                    return $"IM {Im[y]}";

                return y switch
                {
                    0 => "LD I,A",
                    1 => "LD R,A",
                    2 => "LD A,I",
                    3 => "LD A,R",
                    4 => "RRD",
                    5 => "RLD",
                    _ => Db(reader, 0xED, op),
                };
            }

            if (x == 2 && z <= 3 && y >= 4)
            {
                string[] block = z switch
                {
                    0 => ["LDI", "CPI", "INI", "OUTI"],
                    1 => ["LDD", "CPD", "IND", "OUTD"],
                    2 => ["LDIR", "CPIR", "INIR", "OTIR"],
                    _ => ["LDDR", "CPDR", "INDR", "OTDR"],
                };

                return block[y - 4];
            }

            return Db(reader, 0xED, op);
        }

        private static string DecodeIndexed(InstructionReader reader, string index, out ushort? branchTarget)
        {
            branchTarget = null;
            byte op = reader.ReadByte();

            if (op == 0xCB)
            {
                sbyte displacement = unchecked((sbyte)reader.ReadByte());
                byte cb = reader.ReadByte();
                return DecodeCb(cb, IndexedAddress(index, displacement));
            }

            string high = index + "H";
            string low = index + "L";
            int x = op >> 6;
            int y = (op >> 3) & 0x07;
            int z = op & 0x07;

            if (op is 0x26 or 0x2E)
                return $"LD {(op == 0x26 ? high : low)},{Byte(reader)}";

            if (op is 0x34 or 0x35)
            {
                sbyte d = unchecked((sbyte)reader.ReadByte());
                return $"{(op == 0x34 ? "INC" : "DEC")} {IndexedAddress(index, d)}";
            }

            if (op == 0x36)
            {
                sbyte d = unchecked((sbyte)reader.ReadByte());
                return $"LD {IndexedAddress(index, d)},{Byte(reader)}";
            }

            if (x == 1 && op != 0x76)
            {
                string indexedMemory = IndexedAddress(index, null);
                if (y == 6 || z == 6)
                {
                    sbyte d = unchecked((sbyte)reader.ReadByte());
                    indexedMemory = IndexedAddress(index, d);
                }

                string[] indexedRegisters = [R[0], R[1], R[2], R[3], high, low, indexedMemory, R[7]];
                return $"LD {indexedRegisters[y]},{indexedRegisters[z]}";
            }

            if (x == 2 && z is 4 or 5 or 6)
            {
                string operand = z switch
                {
                    4 => high,
                    5 => low,
                    _ => IndexedAddress(index, unchecked((sbyte)reader.ReadByte())),
                };

                return $"{Alu[y]}{operand}";
            }

            if ((op & 0xCF) == 0x09)
                return $"ADD {index},{Rp[(op >> 4) & 0x03]}";

            if (op is 0x21)
                return $"LD {index},{Word(reader)}";

            if (op is 0x22)
                return $"LD ({Word(reader)}),{index}";

            if (op is 0x2A)
                return $"LD {index},({Word(reader)})";

            if (op is 0x23 or 0x2B)
                return $"{(op == 0x23 ? "INC" : "DEC")} {index}";

            if (op is 0x24 or 0x25 or 0x2C or 0x2D)
                return $"{(op is 0x24 or 0x2C ? "INC" : "DEC")} {(op is 0x24 or 0x25 ? high : low)}";

            if (op is 0xE1)
                return $"POP {index}";

            if (op is 0xE3)
                return $"EX (SP),{index}";

            if (op is 0xE5)
                return $"PUSH {index}";

            if (op is 0xE9)
                return $"JP ({index})";

            if (op is 0xF9)
                return $"LD SP,{index}";

            return DecodeBase(op, reader, out branchTarget);
        }

        private static string DecodeCb(byte op, string? indexedOperand)
        {
            int x = op >> 6;
            int y = (op >> 3) & 0x07;
            int z = op & 0x07;
            string operand = indexedOperand ?? R[z];

            return x switch
            {
                0 => $"{Rot[y]} {operand}{IndexedResultSuffix(indexedOperand, z)}",
                1 => $"BIT {y},{operand}",
                2 => $"RES {y},{operand}{IndexedResultSuffix(indexedOperand, z)}",
                _ => $"SET {y},{operand}{IndexedResultSuffix(indexedOperand, z)}",
            };
        }

        private static string IndexedResultSuffix(string? indexedOperand, int z)
        {
            if (indexedOperand == null || z == 6)
                return string.Empty;

            return $",{R[z]}";
        }

        private static string Jr(string op, InstructionReader reader, out ushort? branchTarget)
        {
            sbyte offset = unchecked((sbyte)reader.ReadByte());
            ushort target = (ushort)(reader.CurrentAddress + offset);
            branchTarget = target;
            return $"{op},{Addr(target)}";
        }

        private static string Jp(InstructionReader reader, out ushort? branchTarget)
        {
            ushort target = reader.ReadWord();
            branchTarget = target;
            return $"JP {Addr(target)}";
        }

        private static string Call(InstructionReader reader, out ushort? branchTarget)
        {
            ushort target = reader.ReadWord();
            branchTarget = target;
            return $"CALL {Addr(target)}";
        }

        private static string Word(InstructionReader reader) => Addr(reader.ReadWord());

        private static string Byte(InstructionReader reader) => $"{reader.ReadByte():X2}H";

        private static string Addr(ushort address) => $"{address:X4}H";

        private static string IndexedAddress(string index, sbyte? displacement)
        {
            if (displacement == null)
                return $"({index}+d)";

            sbyte value = displacement.Value;
            int magnitude = value < 0 ? -(int)value : value;
            return value >= 0
                ? $"({index}+{value:X2}H)"
                : $"({index}-{magnitude:X2}H)";
        }

        private static string Db(InstructionReader reader, params byte[] bytes)
        {
            return bytes.Length == 1
                ? $"DB {bytes[0]:X2}H"
                : $"DB {string.Join(",", Array.ConvertAll(bytes, b => $"{b:X2}H"))}";
        }

        private sealed class InstructionReader
        {
            private readonly ushort start;
            private readonly Func<ushort, byte> readMemory;
            private readonly List<byte> bytes = new();

            public InstructionReader(ushort start, Func<ushort, byte> readMemory)
            {
                this.start = start;
                this.readMemory = readMemory;
            }

            public ushort CurrentAddress => (ushort)(start + bytes.Count);

            public byte ReadByte()
            {
                byte value = readMemory(CurrentAddress);
                bytes.Add(value);
                return value;
            }

            public ushort ReadWord()
            {
                byte low = ReadByte();
                byte high = ReadByte();
                return (ushort)(low | (high << 8));
            }

            public Z80Instruction ToInstruction(string mnemonic, ushort? branchTarget)
            {
                return new Z80Instruction(start, bytes.ToArray(), bytes.Count, mnemonic, branchTarget);
            }
        }
    }
}
