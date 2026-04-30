using Spectrum128kEmulator.Z80;
using Xunit;
using System.Collections.Generic;

namespace Spectrum128kEmulator.Tests
{
    public class CpuMicroTests
    {
        private static bool FlagSet(byte f, int bit) => (f & (1 << bit)) != 0;
        
        [Fact]
        public void Dd_E9_Jumps_To_IX()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            memory[0x0000] = 0xDD;
            memory[0x0001] = 0x21;
            memory[0x0002] = 0x34;
            memory[0x0003] = 0x12;
            memory[0x0004] = 0xDD;
            memory[0x0005] = 0xE9;

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.Equal((ushort)0x1234, cpu.Regs.PC);
        }

        [Fact]
        public void Fd_E9_Jumps_To_IY()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            memory[0x0000] = 0xFD;
            memory[0x0001] = 0x21;
            memory[0x0002] = 0x78;
            memory[0x0003] = 0x56;
            memory[0x0004] = 0xFD;
            memory[0x0005] = 0xE9;

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.Equal((ushort)0x5678, cpu.Regs.PC);
        }

        [Fact]
        public void Fd_24_Increments_Iyh()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            memory[0x0000] = 0xFD;
            memory[0x0001] = 0x21;
            memory[0x0002] = 0x00;
            memory[0x0003] = 0x40;
            memory[0x0004] = 0xFD;
            memory[0x0005] = 0x24;

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.Equal((ushort)0x4100, cpu.Regs.IY);
        }

        [Fact]
        public void Ed_In_A_C_Sets_Zero_Flag_From_Input()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value,
                ReadPort = _ => 0x00
            };

            memory[0x0000] = 0x01;
            memory[0x0001] = 0xFE;
            memory[0x0002] = 0x00;
            memory[0x0003] = 0xED;
            memory[0x0004] = 0x78;

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.Equal((byte)0x00, cpu.Regs.A);
            Assert.True((cpu.Regs.F & 0x40) != 0);
        }

        [Fact]
        public void AdcHlBc_NoCarryIn_Adds_And_Leaves_N_Clear()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD HL,1234
            memory[0x0000] = 0x21;
            memory[0x0001] = 0x34;
            memory[0x0002] = 0x12;

            // LD BC,1111
            memory[0x0003] = 0x01;
            memory[0x0004] = 0x11;
            memory[0x0005] = 0x11;

            // XOR A  -> clears carry and gives known flags
            memory[0x0006] = 0xAF;

            // ED 4A -> ADC HL,BC
            memory[0x0007] = 0xED;
            memory[0x0008] = 0x4A;

            cpu.Reset();
            cpu.Step(); // LD HL,1234
            cpu.Step(); // LD BC,1111
            cpu.Step(); // XOR A
            cpu.Step(); // ADC HL,BC

            Assert.Equal((ushort)0x2345, cpu.Regs.HL);
            Assert.False(FlagSet(cpu.Regs.F, 0)); // C
            Assert.False(FlagSet(cpu.Regs.F, 1)); // N
            Assert.False(FlagSet(cpu.Regs.F, 4)); // H
            Assert.False(FlagSet(cpu.Regs.F, 6)); // Z
            Assert.False(FlagSet(cpu.Regs.F, 7)); // S
        }

        [Fact]
        public void AdcHlBc_CarryIn_Sets_H_And_Correct_Result()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD HL,0FFF
            memory[0x0000] = 0x21;
            memory[0x0001] = 0xFF;
            memory[0x0002] = 0x0F;

            // LD BC,0000
            memory[0x0003] = 0x01;
            memory[0x0004] = 0x00;
            memory[0x0005] = 0x00;

            // SCF -> carry in = 1
            memory[0x0006] = 0x37;

            // ED 4A -> ADC HL,BC
            memory[0x0007] = 0xED;
            memory[0x0008] = 0x4A;

            cpu.Reset();
            cpu.Step(); // LD HL,0FFF
            cpu.Step(); // LD BC,0000
            cpu.Step(); // SCF
            cpu.Step(); // ADC HL,BC

            Assert.Equal((ushort)0x1000, cpu.Regs.HL);
            Assert.False(FlagSet(cpu.Regs.F, 0)); // C
            Assert.False(FlagSet(cpu.Regs.F, 1)); // N
            Assert.True(FlagSet(cpu.Regs.F, 4));  // H
            Assert.False(FlagSet(cpu.Regs.F, 6)); // Z
            Assert.False(FlagSet(cpu.Regs.F, 7)); // S
        }

        [Fact]
        public void SbcHlBc_NoCarryIn_Subtracts_And_Sets_N()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD HL,2345
            memory[0x0000] = 0x21;
            memory[0x0001] = 0x45;
            memory[0x0002] = 0x23;

            // LD BC,1111
            memory[0x0003] = 0x01;
            memory[0x0004] = 0x11;
            memory[0x0005] = 0x11;

            // XOR A -> clear carry
            memory[0x0006] = 0xAF;

            // ED 42 -> SBC HL,BC
            memory[0x0007] = 0xED;
            memory[0x0008] = 0x42;

            cpu.Reset();
            cpu.Step(); // LD HL,2345
            cpu.Step(); // LD BC,1111
            cpu.Step(); // XOR A
            cpu.Step(); // SBC HL,BC

            Assert.Equal((ushort)0x1234, cpu.Regs.HL);
            Assert.False(FlagSet(cpu.Regs.F, 0)); // C
            Assert.True(FlagSet(cpu.Regs.F, 1));  // N
            Assert.False(FlagSet(cpu.Regs.F, 4)); // H
            Assert.False(FlagSet(cpu.Regs.F, 6)); // Z
            Assert.False(FlagSet(cpu.Regs.F, 7)); // S
        }

        [Fact]
        public void SbcHlBc_CarryIn_Borrows_And_Sets_C_And_H()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD HL,1000
            memory[0x0000] = 0x21;
            memory[0x0001] = 0x00;
            memory[0x0002] = 0x10;

            // LD BC,1000
            memory[0x0003] = 0x01;
            memory[0x0004] = 0x00;
            memory[0x0005] = 0x10;

            // SCF -> carry in = 1
            memory[0x0006] = 0x37;

            // ED 42 -> SBC HL,BC
            memory[0x0007] = 0xED;
            memory[0x0008] = 0x42;

            cpu.Reset();
            cpu.Step(); // LD HL,1000
            cpu.Step(); // LD BC,1000
            cpu.Step(); // SCF
            cpu.Step(); // SBC HL,BC

            Assert.Equal((ushort)0xFFFF, cpu.Regs.HL);
            Assert.True(FlagSet(cpu.Regs.F, 0));  // C
            Assert.True(FlagSet(cpu.Regs.F, 1));  // N
            Assert.True(FlagSet(cpu.Regs.F, 4));  // H
            Assert.False(FlagSet(cpu.Regs.F, 6)); // Z
            Assert.True(FlagSet(cpu.Regs.F, 7));  // S
        }

        [Fact]
        public void BitB_CopiesUndocumentedFlagsFromOperand()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD B,28h ; BIT 0,B
            memory[0x0000] = 0x06;
            memory[0x0001] = 0x28;
            memory[0x0002] = 0xCB;
            memory[0x0003] = 0x40;

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.True(FlagSet(cpu.Regs.F, 3));
            Assert.True(FlagSet(cpu.Regs.F, 5));
        }

        [Fact]
        public void BitHlm_CopiesUndocumentedFlagsFromAddressHighByte()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD HL,2810h ; BIT 0,(HL)
            memory[0x0000] = 0x21;
            memory[0x0001] = 0x10;
            memory[0x0002] = 0x28;
            memory[0x0003] = 0xCB;
            memory[0x0004] = 0x46;
            memory[0x2810] = 0x01;

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.True(FlagSet(cpu.Regs.F, 3));
            Assert.True(FlagSet(cpu.Regs.F, 5));
        }

        [Fact]
        public void Ldi_SetsUndocumentedFlags_FromBits1And3_Of_APlusCopiedValue()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD A,01 ; LD HL,4000 ; LD DE,5000 ; LD BC,0001 ; LDI
            memory[0x0000] = 0x3E; memory[0x0001] = 0x01;
            memory[0x0002] = 0x21; memory[0x0003] = 0x00; memory[0x0004] = 0x40;
            memory[0x0005] = 0x11; memory[0x0006] = 0x00; memory[0x0007] = 0x50;
            memory[0x0008] = 0x01; memory[0x0009] = 0x01; memory[0x000A] = 0x00;
            memory[0x000B] = 0xED; memory[0x000C] = 0xA0;
            memory[0x4000] = 0x01; // A+value = 02h => F5 set from bit1, F3 clear

            cpu.Reset();
            for (int i = 0; i < 5; i++) cpu.Step();

            Assert.False(FlagSet(cpu.Regs.F, 3));
            Assert.True(FlagSet(cpu.Regs.F, 5));
        }

        [Fact]
        public void Cpi_SetsUndocumentedFlags_FromBits1And3_Of_IntermediateValue()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // Choose values so r = A - (HL) = 02h, H clear, so n = 02h.
            // F3 should come from bit 3 of n (clear), F5 from bit 1 of n (set).
            memory[0x0000] = 0x3E; memory[0x0001] = 0x04; // LD A,04
            memory[0x0002] = 0x21; memory[0x0003] = 0x00; memory[0x0004] = 0x40; // LD HL,4000
            memory[0x0005] = 0x01; memory[0x0006] = 0x01; memory[0x0007] = 0x00; // LD BC,0001
            memory[0x0008] = 0xED; memory[0x0009] = 0xA1; // CPI
            memory[0x4000] = 0x02;

            cpu.Reset();
            for (int i = 0; i < 4; i++) cpu.Step();

            Assert.False(FlagSet(cpu.Regs.F, 3));
            Assert.True(FlagSet(cpu.Regs.F, 5));
        }

        [Fact]
        public void Ei_Delays_Interrupt_Accept_Until_After_One_Following_Instruction()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            memory[0x0000] = 0xFB; // EI
            memory[0x0001] = 0x00; // NOP
            memory[0x0002] = 0x00; // NOP

            cpu.Reset();
            cpu.InterruptPending = true;

            cpu.ExecuteCycles(8);

            Assert.Equal((ushort)0x0002, cpu.Regs.PC);
            Assert.True(cpu.IFF1);
            Assert.True(cpu.IFF2);
            Assert.True(cpu.InterruptPending);

            cpu.ExecuteCycles(13);

            Assert.Equal((ushort)0x0038, cpu.Regs.PC);
            Assert.False(cpu.IFF1);
            Assert.True(cpu.IFF2);
            Assert.False(cpu.InterruptPending);
            Assert.Equal((byte)0x02, memory[0xFFFD]);
            Assert.Equal((byte)0x00, memory[0xFFFE]);
        }

        [Fact]
        public void Interrupt_Acknowledge_Increments_R_Register()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            memory[0x0000] = 0x00; // NOP

            cpu.Reset();
            cpu.Regs.R = 0x2A;
            cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
            cpu.InterruptPending = true;

            cpu.ExecuteCycles(13);

            Assert.Equal((byte)0x2B, cpu.Regs.R);
            Assert.Equal((ushort)0x0038, cpu.Regs.PC);
        }

        [Fact]
        public void PushIx_Uses15TStates_And_Writes_Stack_Frame()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            memory[0x0000] = 0xDD;
            memory[0x0001] = 0xE5; // PUSH IX

            cpu.Reset();
            cpu.Regs.IX = 0x1234;
            cpu.Regs.SP = 0x9000;

            cpu.ExecuteCycles(15);

            Assert.Equal((ushort)0x8FFE, cpu.Regs.SP);
            Assert.Equal((byte)0x12, memory[0x8FFF]);
            Assert.Equal((byte)0x34, memory[0x8FFE]);
            Assert.Equal((ulong)15, cpu.TStates);
        }

        [Fact]
        public void PopIx_Uses14TStates_And_Restores_Value()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            memory[0x0000] = 0xDD;
            memory[0x0001] = 0xE1; // POP IX
            memory[0x8FFE] = 0x34;
            memory[0x8FFF] = 0x12;

            cpu.Reset();
            cpu.Regs.SP = 0x8FFE;

            cpu.ExecuteCycles(14);

            Assert.Equal((ushort)0x1234, cpu.Regs.IX);
            Assert.Equal((ushort)0x9000, cpu.Regs.SP);
            Assert.Equal((ulong)14, cpu.TStates);
        }

        [Fact]
        public void LdSpPtr_Uses20TStates_And_Separates_Byte_Reads()
        {
            var memory = new byte[65536];
            var readTStates = new List<ulong>();
            var cpu = new Z80Cpu();
            cpu.ReadMemory = addr =>
            {
                if (addr == 0x4000 || addr == 0x4001)
                    readTStates.Add(cpu.TStates);

                return memory[addr];
            };
            cpu.WriteMemory = (addr, value) => memory[addr] = value;

            memory[0x0000] = 0xED;
            memory[0x0001] = 0x7B; // LD SP,(4000h)
            memory[0x0002] = 0x00;
            memory[0x0003] = 0x40;
            memory[0x4000] = 0x34;
            memory[0x4001] = 0x12;

            cpu.Reset();
            cpu.Step();

            Assert.Equal((ushort)0x1234, cpu.Regs.SP);
            Assert.Equal((ulong)20, cpu.TStates);
            Assert.Equal(new ulong[] { 14, 17 }, readTStates);
        }

        [Fact]
        public void LdPtrSp_Uses20TStates_And_Separates_Byte_Writes()
        {
            var memory = new byte[65536];
            var writeTStates = new List<ulong>();
            var cpu = new Z80Cpu();
            cpu.ReadMemory = addr => memory[addr];
            cpu.WriteMemory = (addr, value) =>
            {
                if (addr == 0x4000 || addr == 0x4001)
                    writeTStates.Add(cpu.TStates);

                memory[addr] = value;
            };

            memory[0x0000] = 0xED;
            memory[0x0001] = 0x73; // LD (4000h),SP
            memory[0x0002] = 0x00;
            memory[0x0003] = 0x40;

            cpu.Reset();
            cpu.Regs.SP = 0x1234;
            cpu.Step();

            Assert.Equal((byte)0x34, memory[0x4000]);
            Assert.Equal((byte)0x12, memory[0x4001]);
            Assert.Equal((ulong)20, cpu.TStates);
            Assert.Equal(new ulong[] { 14, 17 }, writeTStates);
        }

        [Fact]
        public void Djnz_Uses13TStates_And_Fetches_Displacement_After_Initial_Delay()
        {
            var memory = new byte[65536];
            var readTStates = new List<ulong>();
            var cpu = new Z80Cpu();
            cpu.ReadMemory = addr =>
            {
                if (addr == 0x0001)
                    readTStates.Add(cpu.TStates);

                return memory[addr];
            };
            cpu.WriteMemory = (_, _) => { };

            memory[0x0000] = 0x10; // DJNZ +2
            memory[0x0001] = 0x02;

            cpu.Reset();
            cpu.Regs.B = 0x02;
            cpu.Step();

            Assert.Equal((ushort)0x0004, cpu.Regs.PC);
            Assert.Equal((byte)0x01, cpu.Regs.B);
            Assert.Equal((ulong)13, cpu.TStates);
            Assert.Contains(5UL, readTStates);
            Assert.Equal(5UL, readTStates[^1]);
        }

        [Fact]
        public void IncIxd_CopiesUndocumentedFlagsFromResult()
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value
            };

            // LD IX,4000 ; INC (IX+1)
            memory[0x0000] = 0xDD; memory[0x0001] = 0x21; memory[0x0002] = 0x00; memory[0x0003] = 0x40;
            memory[0x0004] = 0xDD; memory[0x0005] = 0x34; memory[0x0006] = 0x01;
            memory[0x4001] = 0x27; // result 28h -> F3 and F5 set

            cpu.Reset();
            cpu.Step();
            cpu.Step();

            Assert.True(FlagSet(cpu.Regs.F, 3));
            Assert.True(FlagSet(cpu.Regs.F, 5));
        }

        [Theory]
        [InlineData(0x8E, 0x04, 0x94, 0x90)]
        [InlineData(0x8E, 0x05, 0xF4, 0x91)]
        [InlineData(0x99, 0x00, 0x99, 0x84)]
        public void Daa_KnownEdgeCases(byte a, byte f, byte expectedA, byte expectedF)
        {
            var cpu = new Z80Cpu();

            cpu.ReadMemory = _ => 0x27; // DAA
            cpu.WriteMemory = (_, _) => { };

            cpu.Regs.A = a;
            cpu.Regs.F = f;

            cpu.Step();

            Assert.Equal(expectedA, cpu.Regs.A);
            Assert.Equal(expectedF & 0xD7, cpu.Regs.F & 0xD7);
        }
    }
}
