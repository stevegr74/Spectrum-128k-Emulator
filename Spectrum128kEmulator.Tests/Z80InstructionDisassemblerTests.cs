using System;
using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class Z80InstructionDisassemblerTests
    {
        [Fact]
        public void Disassemble_ImmediateWord_ReturnsLengthBytesAndMnemonic()
        {
            byte[] memory = CreateMemory(0x21, 0x34, 0x12);

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x8000, addr => memory[addr - 0x8000]);

            Assert.Equal((ushort)0x8000, instruction.Address);
            Assert.Equal(new byte[] { 0x21, 0x34, 0x12 }, instruction.Bytes);
            Assert.Equal(3, instruction.Length);
            Assert.Equal("LD HL,1234H", instruction.Mnemonic);
            Assert.Null(instruction.BranchTarget);
            Assert.Equal("21 34 12", instruction.BytesText);
        }

        [Fact]
        public void Disassemble_RelativeBranch_ReturnsResolvedTarget()
        {
            byte[] memory = CreateMemory(0x20, 0xFA);

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x4000, addr => memory[addr - 0x4000]);

            Assert.Equal(2, instruction.Length);
            Assert.Equal("JR NZ,3FFCH", instruction.Mnemonic);
            Assert.Equal((ushort)0x3FFC, instruction.BranchTarget);
        }

        [Fact]
        public void Disassemble_RelativeBranch_WrapsAtAddressSpaceEnd()
        {
            var memory = new byte[65536];
            memory[0xFFFF] = 0x18;
            memory[0x0000] = 0x02;

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0xFFFF, addr => memory[addr]);

            Assert.Equal(new byte[] { 0x18, 0x02 }, instruction.Bytes);
            Assert.Equal("JR,0003H", instruction.Mnemonic);
            Assert.Equal((ushort)0x0003, instruction.BranchTarget);
        }

        [Fact]
        public void Disassemble_IndexedDisplacement_ReturnsFullInstructionLength()
        {
            byte[] memory = CreateMemory(0xDD, 0x36, 0xFE, 0x7B);

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x2000, addr => memory[addr - 0x2000]);

            Assert.Equal(new byte[] { 0xDD, 0x36, 0xFE, 0x7B }, instruction.Bytes);
            Assert.Equal(4, instruction.Length);
            Assert.Equal("LD (IX-02H),7BH", instruction.Mnemonic);
        }

        [Fact]
        public void Disassemble_IyCbIndexedBit_ReturnsIndexedPrefixMnemonic()
        {
            byte[] memory = CreateMemory(0xFD, 0xCB, 0x05, 0x56);

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x2000, addr => memory[addr - 0x2000]);

            Assert.Equal(new byte[] { 0xFD, 0xCB, 0x05, 0x56 }, instruction.Bytes);
            Assert.Equal(4, instruction.Length);
            Assert.Equal("BIT 2,(IY+05H)", instruction.Mnemonic);
        }

        [Fact]
        public void Disassemble_DdCbShiftWithRegisterCopy_ReturnsDestinationRegister()
        {
            byte[] memory = CreateMemory(0xDD, 0xCB, 0x80, 0x11);

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x2000, addr => memory[addr - 0x2000]);

            Assert.Equal(4, instruction.Length);
            Assert.Equal("RL (IX-80H),C", instruction.Mnemonic);
        }

        [Fact]
        public void Disassemble_EdUnsupported_ReturnsDbFallback()
        {
            byte[] memory = CreateMemory(0xED, 0x00);

            Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(0x1234, addr => memory[addr - 0x1234]);

            Assert.Equal(2, instruction.Length);
            Assert.Equal("DB EDH,00H", instruction.Mnemonic);
        }

        [Fact]
        public void DisassembleBlock_AdvancesByInstructionLength()
        {
            byte[] memory = CreateMemory(0x3E, 0x10, 0xCB, 0x7C, 0xC9);

            var instructions = Z80InstructionDisassembler.DisassembleBlock(0x6000, 3, addr => memory[addr - 0x6000]);

            Assert.Collection(
                instructions,
                first => Assert.Equal("LD A,10H", first.Mnemonic),
                second => Assert.Equal("BIT 7,H", second.Mnemonic),
                third => Assert.Equal("RET", third.Mnemonic));
        }

        private static byte[] CreateMemory(params byte[] bytes)
        {
            return bytes;
        }
    }
}
