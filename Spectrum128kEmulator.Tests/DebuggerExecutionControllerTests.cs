using Spectrum128kEmulator.Debugging;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class DebuggerExecutionControllerTests
    {
        [Fact]
        public void StepInto_SkipsCurrentBoundaryThenStopsBeforeNextInstruction()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginStepInto(0x8000, 100);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 0xFF00, 100));
            Assert.True(controller.ShouldStopBeforeInstruction(0x8001, 0xFF00, 101));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal(new DebuggerStop(DebuggerStopReason.StepComplete, 0x8001), stop);
        }

        [Fact]
        public void Run_SkipsBreakpointAtResumeBoundaryButStopsWhenItIsReachedAgain()
        {
            var controller = new DebuggerExecutionController();
            controller.SetBreakpoint(0x9000);
            controller.BeginRun(0x9000, 40);

            Assert.False(controller.ShouldStopBeforeInstruction(0x9000, 0xFF00, 40));
            Assert.False(controller.ShouldStopBeforeInstruction(0x9001, 0xFF00, 41));
            Assert.True(controller.ShouldStopBeforeInstruction(0x9000, 0xFF00, 42));
        }

        [Fact]
        public void DisabledBreakpoint_DoesNotStopExecution()
        {
            var controller = new DebuggerExecutionController();
            controller.SetBreakpoint(0x8123, enabled: false);
            controller.BeginRun(0x8000, 10);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 0xFF00, 10));
            Assert.False(controller.ShouldStopBeforeInstruction(0x8123, 0xFF00, 11));
        }

        [Fact]
        public void RunToAddress_StopsWithoutCreatingPersistentBreakpoint()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginRunToAddress(0x4567, 0x4000, 20);

            Assert.False(controller.ShouldStopBeforeInstruction(0x4000, 0xFF00, 20));
            Assert.False(controller.ShouldStopBeforeInstruction(0x4566, 0xFF00, 21));
            Assert.True(controller.ShouldStopBeforeInstruction(0x4567, 0xFF00, 22));
            Assert.Empty(controller.GetBreakpoints());
        }

        [Fact]
        public void PauseRequest_StopsAtNextInstructionBoundary()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginRun(0x1000, 7);
            controller.RequestPause();

            Assert.True(controller.ShouldStopBeforeInstruction(0x1234, 0xFF00, 7));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal(DebuggerStopReason.PauseRequested, stop!.Reason);
        }

        [Fact]
        public void StepInto_StopsAtInterruptVectorBeforeExecutingHandlerInstruction()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginStepInto(0x8000, 100);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 0xFF00, 100));
            Assert.True(controller.ShouldStopBeforeInstruction(0x0038, 0xFEFE, 100));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal((ushort)0x0038, stop!.Address);
        }

        [Fact]
        public void StepOver_CallWaitsForReturnAddressAndOriginalStackPointer()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginStepOver(0x8000, 0xFF00, 100, instructionLength: 3, isCallLike: true);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 0xFF00, 100));
            Assert.False(controller.ShouldStopBeforeInstruction(0x9000, 0xFEFE, 101));
            Assert.False(controller.ShouldStopBeforeInstruction(0x8003, 0xFEFC, 102));
            Assert.True(controller.ShouldStopBeforeInstruction(0x8003, 0xFF00, 103));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal(DebuggerStopReason.StepOverComplete, stop!.Reason);
        }

        [Fact]
        public void StepOver_NonCallBehavesAsStepInto()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginStepOver(0x8000, 0xFF00, 100, instructionLength: 1, isCallLike: false);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 0xFF00, 100));
            Assert.True(controller.ShouldStopBeforeInstruction(0x8001, 0xFF00, 101));
            Assert.Equal(DebuggerExecutionMode.Paused, controller.Mode);
        }

        [Fact]
        public void StepOver_StillHonorsBreakpointInsideCalledCode()
        {
            var controller = new DebuggerExecutionController();
            controller.SetBreakpoint(0x9001);
            controller.BeginStepOver(0x8000, 0xFF00, 100, instructionLength: 3, isCallLike: true);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 0xFF00, 100));
            Assert.True(controller.ShouldStopBeforeInstruction(0x9001, 0xFEFE, 101));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal(DebuggerStopReason.Breakpoint, stop!.Reason);
        }

        [Fact]
        public void StepOut_WaitsForCapturedReturnAddressAndUnwoundStackPointer()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginStepOut(0x9000, 0xFEF0, 100, returnAddress: 0x8123);

            Assert.False(controller.ShouldStopBeforeInstruction(0x9000, 0xFEF0, 100));
            Assert.False(controller.ShouldStopBeforeInstruction(0x8123, 0xFEEE, 101));
            Assert.True(controller.ShouldStopBeforeInstruction(0x8123, 0xFEF2, 102));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal(DebuggerStopReason.StepOutComplete, stop!.Reason);
        }

        [Fact]
        public void ReplacingBreakpoints_DeduplicatesAndSortsAddresses()
        {
            var controller = new DebuggerExecutionController();
            controller.ReplaceBreakpoints(new[]
            {
                new DebuggerBreakpoint(0x9000, false),
                new DebuggerBreakpoint(0x1000, true),
                new DebuggerBreakpoint(0x9000, true)
            });

            Assert.Equal(
                new[]
                {
                    new DebuggerBreakpoint(0x1000, true),
                    new DebuggerBreakpoint(0x9000, true)
                },
                controller.GetBreakpoints());
        }
    }
}
