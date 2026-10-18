using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class Z80IndexedTimingTests
    {
        [Theory]
        [InlineData(0xDD, 0x00, 8UL)]  // ignored prefix + NOP
        [InlineData(0xFD, 0x00, 8UL)]
        [InlineData(0xDD, 0x21, 14UL)] // LD IX,nn
        [InlineData(0xFD, 0x21, 14UL)] // LD IY,nn
        [InlineData(0xDD, 0x22, 20UL)] // LD (nn),IX
        [InlineData(0xFD, 0x22, 20UL)] // LD (nn),IY
        [InlineData(0xDD, 0x2A, 20UL)] // LD IX,(nn)
        [InlineData(0xFD, 0x2A, 20UL)] // LD IY,(nn)
        [InlineData(0xDD, 0xE3, 23UL)] // EX (SP),IX
        [InlineData(0xFD, 0xE3, 23UL)] // EX (SP),IY
        [InlineData(0xDD, 0xF9, 10UL)] // LD SP,IX
        [InlineData(0xFD, 0xF9, 10UL)] // LD SP,IY
        public void IndexedInstructions_UseDocumentedBaseTiming(
            byte prefix,
            byte opcode,
            ulong expectedTStates)
        {
            var memory = new byte[65536];
            var cpu = new Z80Cpu
            {
                ReadMemory = address => memory[address],
                WriteMemory = (address, value) => memory[address] = value
            };
            memory[0] = prefix;
            memory[1] = opcode;
            memory[2] = 0x00;
            memory[3] = 0x20;
            memory[0x2000] = 0x34;
            memory[0x2001] = 0x12;

            cpu.Reset();
            cpu.Regs.SP = 0x3000;
            memory[0x3000] = 0x78;
            memory[0x3001] = 0x56;
            cpu.Step();

            Assert.Equal(expectedTStates, cpu.TStates);
        }
    }
}
