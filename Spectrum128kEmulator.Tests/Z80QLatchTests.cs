using Spectrum128kEmulator.Z80;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class Z80QLatchTests
    {
        private const byte UndocumentedFlags = 0x28;

        [Fact]
        public void FlagWritingInstruction_SetsQ_WhenFlagsValueIsUnchanged()
        {
            var (cpu, _) = CreateCpu(0xB7, 0x37); // OR A; SCF
            cpu.Regs.A = 0x28;
            cpu.Regs.F = 0x2C;

            cpu.Step();
            cpu.Regs.A = 0;
            cpu.Step();

            Assert.Equal(0, cpu.Regs.F & UndocumentedFlags);
        }

        [Fact]
        public void ExAf_DoesNotSetQ_WhenVisibleFlagsChange()
        {
            var (cpu, _) = CreateCpu(0x08, 0x37); // EX AF,AF'; SCF
            cpu.Regs.AF = 0;
            cpu.Regs.A_ = 0;
            cpu.Regs.F_ = UndocumentedFlags;

            cpu.Step();
            cpu.Step();

            Assert.Equal(UndocumentedFlags, cpu.Regs.F & UndocumentedFlags);
        }

        [Fact]
        public void PopAf_DoesNotSetQ_WhenVisibleFlagsChange()
        {
            var (cpu, memory) = CreateCpu(0xF1, 0x37); // POP AF; SCF
            cpu.Regs.SP = 0x8000;
            memory[0x8000] = UndocumentedFlags;
            memory[0x8001] = 0;

            cpu.Step();
            cpu.Step();

            Assert.Equal(UndocumentedFlags, cpu.Regs.F & UndocumentedFlags);
        }

        [Theory]
        [InlineData(0xDD)]
        [InlineData(0xFD)]
        public void IgnoredIndexPrefix_ClearsPriorQInfluenceOnScf(byte prefix)
        {
            var (cpu, memory) = CreateCpu(0xFE, 0x08, prefix, 0x37); // CP 08; DD/FD SCF
            cpu.Regs.A = 0;

            cpu.Step();
            cpu.Step();

            Assert.Equal(0x08, cpu.Regs.F & UndocumentedFlags);
            Assert.Equal(4, cpu.Regs.PC);
            Assert.Equal(prefix, memory[2]);
        }

        [Fact]
        public void InterruptAcknowledge_ClearsPriorQInfluence()
        {
            var (cpu, memory) = CreateCpu(0xFE, 0x08); // CP 08
            memory[0x0038] = 0x37; // SCF
            cpu.Regs.A = 0;
            cpu.Regs.SP = 0x9000;

            cpu.Step();
            cpu.RestoreInterruptState(iff1: true, iff2: true, interruptMode: 1);
            cpu.InterruptPending = true;
            cpu.ExecuteCycles(1);
            cpu.Step();

            Assert.Equal(0x08, cpu.Regs.F & UndocumentedFlags);
        }

        [Fact]
        public void QuickState_RestoresQAtInstructionBoundary()
        {
            var (cpu, memory) = CreateCpu(0xFE, 0x08, 0x00, 0x37); // CP 08; NOP; SCF
            cpu.Regs.A = 0;

            cpu.Step();
            Z80Cpu.QuickState saved = cpu.CaptureQuickState();
            cpu.Step();
            cpu.RestoreQuickState(saved);
            cpu.Regs.PC = 3;
            cpu.Step();

            Assert.Equal(0, cpu.Regs.F & UndocumentedFlags);
            Assert.Equal(0x37, memory[3]);
        }

        private static (Z80Cpu Cpu, byte[] Memory) CreateCpu(params byte[] program)
        {
            byte[] memory = new byte[65536];
            program.CopyTo(memory, 0);

            var cpu = new Z80Cpu
            {
                ReadMemory = address => memory[address],
                WriteMemory = (address, value) => memory[address] = value,
                ReadPort = _ => 0xFF,
                WritePort = (_, _) => { }
            };
            cpu.Reset();
            cpu.Regs.PC = 0;
            return (cpu, memory);
        }
    }
}
