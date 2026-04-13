using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class CpuMicroTests
    {
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
    }
}
