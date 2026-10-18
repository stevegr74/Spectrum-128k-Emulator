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

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 100));
            Assert.True(controller.ShouldStopBeforeInstruction(0x8001, 101));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal(new DebuggerStop(DebuggerStopReason.StepComplete, 0x8001), stop);
        }

        [Fact]
        public void Run_SkipsBreakpointAtResumeBoundaryButStopsWhenItIsReachedAgain()
        {
            var controller = new DebuggerExecutionController();
            controller.SetBreakpoint(0x9000);
            controller.BeginRun(0x9000, 40);

            Assert.False(controller.ShouldStopBeforeInstruction(0x9000, 40));
            Assert.False(controller.ShouldStopBeforeInstruction(0x9001, 41));
            Assert.True(controller.ShouldStopBeforeInstruction(0x9000, 42));
        }

        [Fact]
        public void DisabledBreakpoint_DoesNotStopExecution()
        {
            var controller = new DebuggerExecutionController();
            controller.SetBreakpoint(0x8123, enabled: false);
            controller.BeginRun(0x8000, 10);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 10));
            Assert.False(controller.ShouldStopBeforeInstruction(0x8123, 11));
        }

        [Fact]
        public void RunToAddress_StopsWithoutCreatingPersistentBreakpoint()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginRunToAddress(0x4567, 0x4000, 20);

            Assert.False(controller.ShouldStopBeforeInstruction(0x4000, 20));
            Assert.False(controller.ShouldStopBeforeInstruction(0x4566, 21));
            Assert.True(controller.ShouldStopBeforeInstruction(0x4567, 22));
            Assert.Empty(controller.GetBreakpoints());
        }

        [Fact]
        public void PauseRequest_StopsAtNextInstructionBoundary()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginRun(0x1000, 7);
            controller.RequestPause();

            Assert.True(controller.ShouldStopBeforeInstruction(0x1234, 7));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal(DebuggerStopReason.PauseRequested, stop!.Reason);
        }

        [Fact]
        public void StepInto_StopsAtInterruptVectorBeforeExecutingHandlerInstruction()
        {
            var controller = new DebuggerExecutionController();
            controller.BeginStepInto(0x8000, 100);

            Assert.False(controller.ShouldStopBeforeInstruction(0x8000, 100));
            Assert.True(controller.ShouldStopBeforeInstruction(0x0038, 100));
            Assert.True(controller.TryConsumeStop(out DebuggerStop? stop));
            Assert.Equal((ushort)0x0038, stop!.Address);
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
