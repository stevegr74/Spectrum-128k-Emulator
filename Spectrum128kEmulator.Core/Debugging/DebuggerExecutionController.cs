namespace Spectrum128kEmulator.Debugging
{
    public enum DebuggerExecutionMode
    {
        Paused,
        Running,
        StepInto,
        StepOver,
        StepOut,
        RunToAddress
    }

    public enum DebuggerStopReason
    {
        PauseRequested,
        StepComplete,
        StepOverComplete,
        StepOutComplete,
        Breakpoint,
        RunToAddress
    }

    public sealed record DebuggerBreakpoint(ushort Address, bool Enabled);

    public sealed record DebuggerStop(DebuggerStopReason Reason, ushort Address);

    public sealed class DebuggerExecutionController
    {
        private readonly object gate = new();
        private readonly Dictionary<ushort, bool> breakpoints = new();
        private DebuggerExecutionMode mode = DebuggerExecutionMode.Paused;
        private ulong resumeInstructionFetchCount;
        private ushort resumeProgramCounter;
        private ushort? runToAddress;
        private ushort? expectedStackPointer;
        private DebuggerStop? pendingStop;
        private bool pauseWasRequested;

        public DebuggerExecutionMode Mode
        {
            get
            {
                lock (gate)
                    return mode;
            }
        }

        public IReadOnlyList<DebuggerBreakpoint> GetBreakpoints()
        {
            lock (gate)
            {
                return breakpoints
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new DebuggerBreakpoint(pair.Key, pair.Value))
                    .ToArray();
            }
        }

        public void SetBreakpoint(ushort address, bool enabled = true)
        {
            lock (gate)
                breakpoints[address] = enabled;
        }

        public bool RemoveBreakpoint(ushort address)
        {
            lock (gate)
                return breakpoints.Remove(address);
        }

        public void ReplaceBreakpoints(IEnumerable<DebuggerBreakpoint> replacements)
        {
            ArgumentNullException.ThrowIfNull(replacements);
            lock (gate)
            {
                breakpoints.Clear();
                foreach (DebuggerBreakpoint breakpoint in replacements)
                    breakpoints[breakpoint.Address] = breakpoint.Enabled;
            }
        }

        public void BeginRun(ushort programCounter, ulong instructionFetchCount)
        {
            BeginExecution(DebuggerExecutionMode.Running, programCounter, instructionFetchCount, null, null);
        }

        public void BeginStepInto(ushort programCounter, ulong instructionFetchCount)
        {
            BeginExecution(DebuggerExecutionMode.StepInto, programCounter, instructionFetchCount, null, null);
        }

        public void BeginStepOver(
            ushort programCounter,
            ushort stackPointer,
            ulong instructionFetchCount,
            int instructionLength,
            bool isCallLike)
        {
            if (instructionLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(instructionLength));

            if (!isCallLike)
            {
                BeginStepInto(programCounter, instructionFetchCount);
                return;
            }

            BeginExecution(
                DebuggerExecutionMode.StepOver,
                programCounter,
                instructionFetchCount,
                (ushort)(programCounter + instructionLength),
                stackPointer);
        }

        public void BeginStepOut(
            ushort programCounter,
            ushort stackPointer,
            ulong instructionFetchCount,
            ushort returnAddress)
        {
            BeginExecution(
                DebuggerExecutionMode.StepOut,
                programCounter,
                instructionFetchCount,
                returnAddress,
                (ushort)(stackPointer + 2));
        }

        public void BeginRunToAddress(ushort address, ushort programCounter, ulong instructionFetchCount)
        {
            BeginExecution(DebuggerExecutionMode.RunToAddress, programCounter, instructionFetchCount, address, null);
        }

        public void RequestPause()
        {
            lock (gate)
            {
                if (mode == DebuggerExecutionMode.Paused)
                    return;

                mode = DebuggerExecutionMode.Paused;
                pauseWasRequested = true;
                runToAddress = null;
                expectedStackPointer = null;
            }
        }

        public bool ShouldStopBeforeInstruction(
            ushort programCounter,
            ushort stackPointer,
            ulong instructionFetchCount)
        {
            lock (gate)
            {
                if (mode == DebuggerExecutionMode.Paused)
                {
                    if (pauseWasRequested)
                    {
                        pauseWasRequested = false;
                        pendingStop = new DebuggerStop(DebuggerStopReason.PauseRequested, programCounter);
                    }
                    return true;
                }

                if (instructionFetchCount == resumeInstructionFetchCount && programCounter == resumeProgramCounter)
                    return false;

                if (mode == DebuggerExecutionMode.StepInto)
                    return Stop(DebuggerStopReason.StepComplete, programCounter);

                if (mode == DebuggerExecutionMode.StepOver &&
                    programCounter == runToAddress &&
                    stackPointer == expectedStackPointer)
                {
                    return Stop(DebuggerStopReason.StepOverComplete, programCounter);
                }

                if (mode == DebuggerExecutionMode.StepOut &&
                    programCounter == runToAddress &&
                    stackPointer == expectedStackPointer)
                {
                    return Stop(DebuggerStopReason.StepOutComplete, programCounter);
                }

                if (mode == DebuggerExecutionMode.RunToAddress && programCounter == runToAddress)
                    return Stop(DebuggerStopReason.RunToAddress, programCounter);

                if (breakpoints.TryGetValue(programCounter, out bool enabled) && enabled)
                    return Stop(DebuggerStopReason.Breakpoint, programCounter);

                return false;
            }
        }

        public bool TryConsumeStop(out DebuggerStop? stop)
        {
            lock (gate)
            {
                stop = pendingStop;
                pendingStop = null;
                return stop != null;
            }
        }

        private void BeginExecution(
            DebuggerExecutionMode executionMode,
            ushort programCounter,
            ulong instructionFetchCount,
            ushort? temporaryAddress,
            ushort? stackPointer)
        {
            lock (gate)
            {
                mode = executionMode;
                resumeProgramCounter = programCounter;
                resumeInstructionFetchCount = instructionFetchCount;
                runToAddress = temporaryAddress;
                expectedStackPointer = stackPointer;
                pendingStop = null;
                pauseWasRequested = false;
            }
        }

        private bool Stop(DebuggerStopReason reason, ushort address)
        {
            mode = DebuggerExecutionMode.Paused;
            runToAddress = null;
            expectedStackPointer = null;
            pendingStop = new DebuggerStop(reason, address);
            return true;
        }
    }
}
