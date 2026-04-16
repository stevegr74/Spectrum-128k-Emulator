using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class DaaFlagOpsTests
    {
        private const byte FlagC  = 0x01;
        private const byte FlagN  = 0x02;
        private const byte FlagPV = 0x04;
        private const byte FlagF3 = 0x08;
        private const byte FlagH  = 0x10;
        private const byte FlagF5 = 0x20;
        private const byte FlagZ  = 0x40;
        private const byte FlagS  = 0x80;

        private static Z80Cpu CreateCpu(byte opcode, byte a, byte f)
        {
            byte[] memory = new byte[65536];
            memory[0x0000] = opcode;

            var cpu = new Z80Cpu
            {
                ReadMemory = addr => memory[addr],
                WriteMemory = (addr, value) => memory[addr] = value,
                ReadPort = _ => 0xFF,
                WritePort = (_, _) => { }
            };

            cpu.Reset();
            cpu.Regs.PC = 0x0000;
            cpu.Regs.A = a;
            cpu.Regs.F = f;

            return cpu;
        }

        private static byte Flags(
            bool s = false,
            bool z = false,
            bool f5 = false,
            bool h = false,
            bool f3 = false,
            bool pv = false,
            bool n = false,
            bool c = false)
        {
            byte f = 0;
            if (s)  f |= FlagS;
            if (z)  f |= FlagZ;
            if (f5) f |= FlagF5;
            if (h)  f |= FlagH;
            if (f3) f |= FlagF3;
            if (pv) f |= FlagPV;
            if (n)  f |= FlagN;
            if (c)  f |= FlagC;
            return f;
        }

        private static bool Has(byte f, byte mask) => (f & mask) != 0;

        [Fact]
        public void Cpl_Inverts_A_Sets_N_And_H_Preserves_C_S_Z_PV_And_Copies_F3_F5()
        {
            var cpu = CreateCpu(
                opcode: 0x2F,           // CPL
                a: 0x35,
                f: Flags(s: true, z: false, pv: true, c: true));

            cpu.Step();

            Assert.Equal(0xCA, cpu.Regs.A);

            Assert.True(Has(cpu.Regs.F, FlagN));
            Assert.True(Has(cpu.Regs.F, FlagH));

            Assert.True(Has(cpu.Regs.F, FlagS));      // preserved
            Assert.False(Has(cpu.Regs.F, FlagZ));     // preserved
            Assert.True(Has(cpu.Regs.F, FlagPV));     // preserved
            Assert.True(Has(cpu.Regs.F, FlagC));      // preserved

            Assert.True(Has(cpu.Regs.F, FlagF3));     // 0xCA -> bit 3 set
            Assert.False(Has(cpu.Regs.F, FlagF5));    // 0xCA -> bit 5 clear
        }

        [Fact]
        public void Scf_Sets_C_Clears_N_H_Preserves_S_Z_PV_And_Copies_F3_F5_From_A()
        {
            var cpu = CreateCpu(
                opcode: 0x37,           // SCF
                a: 0x28,
                f: Flags(s: true, z: true, pv: true, h: true, n: true, c: false));

            cpu.Step();

            Assert.Equal(0x28, cpu.Regs.A);

            Assert.True(Has(cpu.Regs.F, FlagC));
            Assert.False(Has(cpu.Regs.F, FlagN));
            Assert.False(Has(cpu.Regs.F, FlagH));

            Assert.True(Has(cpu.Regs.F, FlagS));      // preserved
            Assert.True(Has(cpu.Regs.F, FlagZ));      // preserved
            Assert.True(Has(cpu.Regs.F, FlagPV));     // preserved

            Assert.True(Has(cpu.Regs.F, FlagF3));     // 0x28 -> bit 3 set
            Assert.True(Has(cpu.Regs.F, FlagF5));     // 0x28 -> bit 5 set
        }

        [Fact]
        public void Ccf_Toggles_C_Sets_H_From_Old_C_Clears_N_Preserves_S_Z_PV_And_Copies_F3_F5()
        {
            var cpu = CreateCpu(
                opcode: 0x3F,           // CCF
                a: 0x08,
                f: Flags(s: false, z: true, pv: false, c: true));

            cpu.Step();

            Assert.Equal(0x08, cpu.Regs.A);

            Assert.False(Has(cpu.Regs.F, FlagC));     // toggled from old carry=1
            Assert.True(Has(cpu.Regs.F, FlagH));      // old carry
            Assert.False(Has(cpu.Regs.F, FlagN));

            Assert.False(Has(cpu.Regs.F, FlagS));     // preserved
            Assert.True(Has(cpu.Regs.F, FlagZ));      // preserved
            Assert.False(Has(cpu.Regs.F, FlagPV));    // preserved

            Assert.True(Has(cpu.Regs.F, FlagF3));     // 0x08 -> bit 3 set
            Assert.False(Has(cpu.Regs.F, FlagF5));    // 0x08 -> bit 5 clear
        }

        [Theory]
        // Addition-side boundary cases
        [InlineData(0x09, 0x00, 0x09, false)]
        [InlineData(0x0A, 0x00, 0x10, false)]
        [InlineData(0x0F, 0x00, 0x15, false)]
        [InlineData(0x10, 0x10, 0x16, false)] // H set
        [InlineData(0x99, 0x00, 0x99, false)]
        [InlineData(0x9A, 0x00, 0x00, true)]
        [InlineData(0x15, 0x10, 0x1B, false)] // H set
        [InlineData(0xA0, 0x00, 0x00, true)]
        // Subtraction-side boundary cases
        [InlineData(0x0F, 0x12, 0x09, false)] // N|H
        [InlineData(0x9F, 0x03, 0x39, true)]  // N|C
        [InlineData(0xFF, 0x13, 0x99, true)]  // N|H|C
        public void Daa_Boundary_Cases_Check_A_And_Carry(
            byte initialA,
            byte initialF,
            byte expectedA,
            bool expectedC)
        {
            var cpu = CreateCpu(
                opcode: 0x27,           // DAA
                a: initialA,
                f: initialF);

            cpu.Step();

            Assert.Equal(expectedA, cpu.Regs.A);
            Assert.Equal(expectedC, Has(cpu.Regs.F, FlagC));

            // DAA result flags that should always reflect final A
            Assert.Equal((expectedA & 0x80) != 0, Has(cpu.Regs.F, FlagS));
            Assert.Equal(expectedA == 0, Has(cpu.Regs.F, FlagZ));
            Assert.Equal(Parity(expectedA), Has(cpu.Regs.F, FlagPV));
            Assert.Equal((expectedA & 0x08) != 0, Has(cpu.Regs.F, FlagF3));
            Assert.Equal((expectedA & 0x20) != 0, Has(cpu.Regs.F, FlagF5));
        }

        [Theory]
        // These are aimed at catching whether N is preserved correctly.
        [InlineData(0x15, 0x00, false)]
        [InlineData(0x15, 0x02, true)]
        [InlineData(0x9F, 0x03, true)]
        [InlineData(0x0A, 0x00, false)]
        public void Daa_Preserves_N(byte initialA, byte initialF, bool expectedN)
        {
            var cpu = CreateCpu(
                opcode: 0x27,
                a: initialA,
                f: initialF);

            cpu.Step();

            Assert.Equal(expectedN, Has(cpu.Regs.F, FlagN));
        }

        [Theory]
        // A compact set specifically for half-carry observation around the nibble adjust path.
        [InlineData(0x0A, 0x00)]
        [InlineData(0x0F, 0x00)]
        [InlineData(0x10, 0x10)]
        [InlineData(0x15, 0x10)]
        [InlineData(0x9A, 0x00)]
        [InlineData(0x0F, 0x12)]
        [InlineData(0xFF, 0x13)]
        public void Daa_Produces_Deterministic_HalfCarry(byte initialA, byte initialF)
        {
            var cpu1 = CreateCpu(0x27, initialA, initialF);
            var cpu2 = CreateCpu(0x27, initialA, initialF);

            cpu1.Step();
            cpu2.Step();

            Assert.Equal(Has(cpu1.Regs.F, FlagH), Has(cpu2.Regs.F, FlagH));
            Assert.Equal(cpu1.Regs.A, cpu2.Regs.A);
            Assert.Equal(cpu1.Regs.F, cpu2.Regs.F);
        }

        private static bool Parity(byte value)
        {
            int bits = 0;
            for (int i = 0; i < 8; i++)
            {
                if (((value >> i) & 1) != 0)
                    bits++;
            }

            return (bits & 1) == 0;
        }
    }
}
