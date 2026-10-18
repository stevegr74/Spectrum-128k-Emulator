using Xunit;
using Spectrum128kEmulator.Debugging;

namespace Spectrum128kEmulator.Tests
{
    public class DebuggerMachineExecutionTests
    {
        [Fact]
        public void DebugStop_PreservesExecutedFrameProgress()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                machine.SetFrameTimingForDebug(8);
                ulong initialFetchCount = machine.Cpu.InstructionFetchCount;
                machine.Cpu.StopBeforeInstruction = cpu => cpu.InstructionFetchCount > initialFetchCount;

                Assert.Equal(0, machine.ExecuteTimeSlice(8, out int stoppedTStates));
                Assert.Equal(4, stoppedTStates);
                Assert.True(machine.Cpu.ExecutionStopped);

                machine.Cpu.StopBeforeInstruction = null;
                Assert.Equal(1, machine.ExecuteTimeSlice(4));
                Assert.Equal(1, machine.FrameCount);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void StepInto_ExecutesExactlyOneInstructionOnTheMachine()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                var controller = new DebuggerExecutionController();
                controller.BeginStepInto(machine.Cpu.Regs.PC, machine.Cpu.InstructionFetchCount);
                machine.Cpu.StopBeforeInstruction = cpu => controller.ShouldStopBeforeInstruction(
                    cpu.Regs.PC,
                    cpu.Regs.SP,
                    cpu.InstructionFetchCount);

                machine.ExecuteTimeSlice(100, out int executedTStates);

                Assert.True(machine.Cpu.ExecutionStopped);
                Assert.Equal(4, executedTStates);
                Assert.Equal((ushort)0x0001, machine.Cpu.Regs.PC);
                Assert.Equal((ulong)1, machine.Cpu.InstructionFetchCount);
                Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
                Assert.Equal(DebuggerStopReason.StepComplete, stop!.Reason);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void Run_StopsBeforeEnabledBreakpointOnTheMachine()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                var controller = new DebuggerExecutionController();
                controller.SetBreakpoint(0x0002);
                controller.BeginRun(machine.Cpu.Regs.PC, machine.Cpu.InstructionFetchCount);
                machine.Cpu.StopBeforeInstruction = cpu => controller.ShouldStopBeforeInstruction(
                    cpu.Regs.PC,
                    cpu.Regs.SP,
                    cpu.InstructionFetchCount);

                machine.ExecuteTimeSlice(100, out int executedTStates);

                Assert.True(machine.Cpu.ExecutionStopped);
                Assert.Equal(8, executedTStates);
                Assert.Equal((ushort)0x0002, machine.Cpu.Regs.PC);
                Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
                Assert.Equal(DebuggerStopReason.Breakpoint, stop!.Reason);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void StepOver_CallRunsSubroutineAndStopsAtFollowingInstruction()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                var controller = new DebuggerExecutionController();
                machine.Cpu.Regs.PC = 0x8000;
                machine.Cpu.Regs.SP = 0xFF00;
                machine.PokeMemory(0x8000, 0xCD);
                machine.PokeMemory(0x8001, 0x05);
                machine.PokeMemory(0x8002, 0x80);
                machine.PokeMemory(0x8005, 0x00);
                machine.PokeMemory(0x8006, 0xC9);
                controller.BeginStepOver(0x8000, 0xFF00, 0, instructionLength: 3, isCallLike: true);
                machine.Cpu.StopBeforeInstruction = cpu => controller.ShouldStopBeforeInstruction(
                    cpu.Regs.PC,
                    cpu.Regs.SP,
                    cpu.InstructionFetchCount);

                machine.ExecuteTimeSlice(100, out int executedTStates);

                Assert.True(machine.Cpu.ExecutionStopped);
                Assert.Equal(31, executedTStates);
                Assert.Equal((ushort)0x8003, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0xFF00, machine.Cpu.Regs.SP);
                Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
                Assert.Equal(DebuggerStopReason.StepOverComplete, stop!.Reason);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        [Fact]
        public void StepOut_RunsUntilReturnAddressIsPoppedFromStack()
        {
            string romFolder = CreateTempRoms();
            try
            {
                var machine = new Spectrum128Machine(romFolder);
                var controller = new DebuggerExecutionController();
                machine.Cpu.Regs.PC = 0x8005;
                machine.Cpu.Regs.SP = 0xFEFE;
                machine.PokeMemory(0x8005, 0x00);
                machine.PokeMemory(0x8006, 0xC9);
                machine.PokeMemory(0xFEFE, 0x03);
                machine.PokeMemory(0xFEFF, 0x80);
                controller.BeginStepOut(0x8005, 0xFEFE, 0, returnAddress: 0x8003);
                machine.Cpu.StopBeforeInstruction = cpu => controller.ShouldStopBeforeInstruction(
                    cpu.Regs.PC,
                    cpu.Regs.SP,
                    cpu.InstructionFetchCount);

                machine.ExecuteTimeSlice(100, out int executedTStates);

                Assert.True(machine.Cpu.ExecutionStopped);
                Assert.Equal(14, executedTStates);
                Assert.Equal((ushort)0x8003, machine.Cpu.Regs.PC);
                Assert.Equal((ushort)0xFF00, machine.Cpu.Regs.SP);
                Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
                Assert.Equal(DebuggerStopReason.StepOutComplete, stop!.Reason);
            }
            finally
            {
                Directory.Delete(romFolder, true);
            }
        }

        private static string CreateTempRoms()
        {
            string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "128-0.rom"), new byte[16384]);
            File.WriteAllBytes(Path.Combine(folder, "128-1.rom"), new byte[16384]);
            return folder;
        }
    }
}
