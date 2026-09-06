using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class BlockIoFlagTests
    {
        [Theory]
        [InlineData(0xA2, 0x02, 0xFF, 0x12, 0x34, 0x81, 0x06, 0x1235)] // INI: C + 1 wraps
        [InlineData(0xA2, 0x02, 0xFE, 0x12, 0x34, 0x81, 0x13, 0x1235)] // INI: H/C carry
        [InlineData(0xAA, 0x81, 0x00, 0x12, 0x34, 0x80, 0x97, 0x1233)] // IND
        [InlineData(0xB2, 0x01, 0x00, 0x12, 0x34, 0x00, 0x40, 0x1235)] // INIR, terminal iteration
        public void BlockInput_SetsHardwareDerivedFlagsAndWritesInput(
            byte opcode,
            byte initialB,
            byte c,
            byte h,
            byte l,
            byte input,
            byte expectedFlags,
            ushort expectedHl)
        {
            var memory = new byte[65536];
            var cpu = CreateCpu(memory, input, out _);
            memory[0] = 0xED;
            memory[1] = opcode;
            cpu.Reset();
            cpu.Regs.B = initialB;
            cpu.Regs.C = c;
            cpu.Regs.H = h;
            cpu.Regs.L = l;

            cpu.Step();

            Assert.Equal(input, memory[(h << 8) | l]);
            Assert.Equal((byte)(initialB - 1), cpu.Regs.B);
            Assert.Equal(expectedHl, cpu.Regs.HL);
            Assert.Equal(expectedFlags, cpu.Regs.F);
        }

        [Theory]
        [InlineData(0xA3, 0x02, 0x00, 0x12, 0x3F, 0xC1, 0x17, 0x1240)] // OUTI
        [InlineData(0xAB, 0x81, 0x00, 0x12, 0x00, 0x81, 0x93, 0x11FF)] // OUTD
        [InlineData(0xB3, 0x29, 0x00, 0x12, 0xFF, 0x00, 0x2C, 0x1300)] // OTIR, terminal iteration
        public void BlockOutput_SetsHardwareDerivedFlagsAndWritesOutput(
            byte opcode,
            byte initialB,
            byte c,
            byte h,
            byte l,
            byte output,
            byte expectedFlags,
            ushort expectedHl)
        {
            var memory = new byte[65536];
            var cpu = CreateCpu(memory, 0xFF, out var writes);
            memory[0] = 0xED;
            memory[1] = opcode;
            memory[(h << 8) | l] = output;
            cpu.Reset();
            cpu.Regs.B = initialB;
            cpu.Regs.C = c;
            cpu.Regs.H = h;
            cpu.Regs.L = l;

            cpu.Step();

            Assert.Equal((byte)(initialB - 1), cpu.Regs.B);
            Assert.Equal(expectedHl, cpu.Regs.HL);
            Assert.Equal(expectedFlags, cpu.Regs.F);
            Assert.Single(writes);
            Assert.Equal(output, writes[0].Value);
            Assert.Equal((ushort)(((initialB - 1) << 8) | c), writes[0].Port);
        }

        [Theory]
        [InlineData(0xB2)] // INIR
        [InlineData(0xBA)] // INDR
        [InlineData(0xB3)] // OTIR
        [InlineData(0xBB)] // OTDR
        public void RepeatingBlockIo_RewindsProgramCounterAndUsesTwentyOneTStates(byte opcode)
        {
            var memory = new byte[65536];
            var cpu = CreateCpu(memory, 0x55, out _);
            memory[0] = 0xED;
            memory[1] = opcode;
            memory[0x1234] = 0x55;
            cpu.Reset();
            cpu.Regs.BC = 0x0200;
            cpu.Regs.HL = 0x1234;

            cpu.Step();

            Assert.Equal((ushort)0, cpu.Regs.PC);
            Assert.Equal((ulong)21, cpu.TStates);
        }

        private static Z80Cpu CreateCpu(byte[] memory, byte input, out List<(ushort Port, byte Value)> writes)
        {
            var portWrites = new List<(ushort Port, byte Value)>();
            writes = portWrites;
            return new Z80Cpu
            {
                ReadMemory = address => memory[address],
                WriteMemory = (address, value) => memory[address] = value,
                ReadPort = _ => input,
                WritePort = (port, value) => portWrites.Add((port, value))
            };
        }
    }
}
