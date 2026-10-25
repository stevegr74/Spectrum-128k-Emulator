using System.Text;

namespace Spectrum128kEmulator
{
    public sealed partial class Spectrum128Machine
    {
        private const int DebugHistoryCapacity = 8192;
        private const int DebugWatchHistoryCapacity = 512;
        private readonly Queue<string> recentMemoryEvents = new();
        private readonly Queue<string> recentPortEvents = new();
        private readonly HashSet<ushort> debugMemoryWatchAddresses = new();
        private readonly Queue<string> watchedMemoryEvents = new();
        private readonly Queue<string> focusedInstructionTrace = new();
        private bool autoDebugDumpPending;
        private string? autoDebugDumpReason;
        private string? autoDebugDumpSnapshot;
        private bool autoDebugDumpSuppressed;
        private bool debugEventCaptureEnabled;
        private bool screenWriteDiagnosticsEnabled;
        private ushort? focusedTraceStartPc;
        private ushort? focusedTraceEndPc;
        private int focusedTraceStartFrame;
        private int focusedTraceFrameLimit;
        private int focusedTraceMaxEntries;
        private ushort? focusedTraceStopPc;
        private int floatingBusDisplayStartAdjustTStates;
        private int floatingBusSampleAdjustTStates;

        public Dictionary<ushort, int> ScreenWriteLog { get; } = new();
        public Dictionary<ushort, int> AboveScreenWriteLog { get; } = new();
        public int LastAboveWriteFrame { get; private set; } = -1;

        public void ClearDebugHistory()
        {
            recentMemoryEvents.Clear();
            recentPortEvents.Clear();
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            autoDebugDumpSuppressed = false;
            lastObservedPc = 0;
            interruptPulseEndTStates = null;
            cpu.ClearRecentTrace();
        }

        public void SetDebugEventCaptureEnabled(bool enabled)
        {
            debugEventCaptureEnabled = enabled;
            cpu.SetInstructionTraceCaptureEnabled(enabled);
            if (!enabled)
            {
                recentMemoryEvents.Clear();
                recentPortEvents.Clear();
            }
        }

        // Kept dormant in normal emulation; the manual harness enables this only for
        // sparse, address-specific diagnostics that must survive noisy loader loops.
        public void SetDebugMemoryWriteWatch(IEnumerable<ushort> addresses)
        {
            debugMemoryWatchAddresses.Clear();
            watchedMemoryEvents.Clear();
            foreach (ushort address in addresses)
                debugMemoryWatchAddresses.Add(address);
        }

        public bool TryConsumeAutoDebugDump(out string reason, out string dump)
        {
            if (!autoDebugDumpPending)
            {
                reason = string.Empty;
                dump = string.Empty;
                return false;
            }

            reason = autoDebugDumpReason ?? "Auto trap";
            dump = autoDebugDumpSnapshot ?? BuildDebugDump(reason);
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            return true;
        }

        public string BuildDebugDump(string? reason = null)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(reason))
            {
                sb.AppendLine("=== REASON ===");
                sb.AppendLine(reason);
                sb.AppendLine();
            }

            sb.AppendLine("=== MACHINE STATE ===");
            sb.AppendLine($"Frame={FrameCount} Border={BorderColor} PagedRamBank={PagedRamBank} ScreenBank={ScreenBank} RomBank={CurrentRomBank} PagingLocked={PagingLocked}");
            sb.AppendLine($"RomProfile={RomProfile.Name} AddressTapeServices={(SupportsRomTapeAcceleration ? 1 : 0)}");
            sb.AppendLine($"SpeakerHigh={speakerHigh} SpeakerEdge={SpeakerEdge} FlashPhase={FlashPhase} MountedTape={mountedTape?.DisplayName ?? "(none)"}");
            sb.AppendLine($"TapeTransport={TapeTransportState} Transition={LastTapeTransportTransition} MarkerStop={(IsTapeStoppedAtMarker ? 1 : 0)}");
            if (mountedTape != null)
                sb.AppendLine($"TapeDebug={mountedTape.DebugPlaybackState}");
            sb.AppendLine($"TapeActivityReads={tapeActivity.PortReadCount} KeyboardOnly={tapeActivity.KeyboardOnlyPortReadCount} Observed={(tapeActivity.LoaderActivityObserved ? 1 : 0)} HandoffFrames={tapeActivity.HandoffFrames}");
            sb.AppendLine();

            sb.AppendLine("=== CPU STATE ===");
            sb.AppendLine($"TStates={cpu.TStates}");
            sb.AppendLine($"PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} AF={cpu.Regs.AF:X4} AF'={((cpu.Regs.A_ << 8) | cpu.Regs.F_):X4} BC={cpu.Regs.BC:X4} DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}");
            sb.AppendLine($"I={cpu.Regs.I:X2} R={cpu.Regs.R:X2} IM={cpu.InterruptMode} IFF1={(cpu.IFF1 ? 1 : 0)} IFF2={(cpu.IFF2 ? 1 : 0)} HALT={(cpu.IsHalted ? 1 : 0)} INTP={(cpu.InterruptPending ? 1 : 0)}");
            sb.AppendLine();

            sb.AppendLine("=== STACK BYTES ===");
            AppendMemoryWindow(sb, cpu.Regs.SP, 32);
            AppendMemoryWindow(sb, cpu.Regs.PC, 24, "=== MEMORY PC ===");
            AppendMemoryWindow(sb, 0x5C00, 32, "=== MEMORY 5C00 ===");
            AppendMemoryWindow(sb, 0x5C30, 48, "=== MEMORY 5C30 ===");
            AppendMemoryWindow(sb, cpu.Regs.IY, 16, "=== MEMORY IY ===");
            AppendMemoryWindow(sb, 0x5CB0, 16, "=== MEMORY 5CB0 ===");
            AppendMemoryWindow(sb, 0x10A8, 24, "=== MEMORY 10A8 ===");
            AppendMemoryWindow(sb, 0x111D, 24, "=== MEMORY 111D ===");
            AppendMemoryWindow(sb, 0xFD00, 64, "=== MEMORY FD00 ===");
            AppendMemoryWindow(sb, 0xFD60, 64, "=== MEMORY FD60 ===");
            AppendMemoryWindow(sb, 0xFD3E, 64, "=== MEMORY FD3E ===");
            AppendMemoryWindow(sb, 0xFE00, 64, "=== MEMORY FE00 ===");
            AppendMemoryWindow(sb, 0xFE14, 64, "=== MEMORY FE14 ===");
            AppendMemoryWindow(sb, 0xFE42, 64, "=== MEMORY FE42 ===");
            AppendMemoryWindow(sb, 0xFE80, 32, "=== MEMORY FE80 ===");
            AppendMemoryWindow(sb, 0xFE9C, 64, "=== MEMORY FE9C ===");
            AppendMemoryWindow(sb, 0xFED8, 32, "=== MEMORY FED8 ===");
            AppendMemoryWindow(sb, 0x5B00, 32, "=== MEMORY 5B00 ===");

            sb.AppendLine("=== KEYBOARD MATRIX ===");
            for (int row = 0; row < keyboardMatrix.Length; row++)
                sb.AppendLine($"Row{row}={keyboardMatrix[row]:X2} Scans={keyboardRowScanCounts[row]}");
            sb.AppendLine();

            sb.AppendLine("=== RECENT CPU TRACE ===");
            foreach (string line in cpu.GetRecentTraceSnapshot())
                sb.AppendLine(line);
            sb.AppendLine();

            if (focusedInstructionTrace.Count > 0)
            {
                sb.AppendLine("=== FOCUSED INSTRUCTION TRACE ===");
                foreach (string line in focusedInstructionTrace)
                    sb.AppendLine(line);
                sb.AppendLine();
            }

            sb.AppendLine("=== INTERRUPT EVENTS ===");
            foreach (string line in cpu.GetRecentInterruptEventsSnapshot())
                sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("=== RECENT MEMORY EVENTS ===");
            foreach (string line in recentMemoryEvents)
                sb.AppendLine(line);
            sb.AppendLine();

            if (watchedMemoryEvents.Count > 0 || debugMemoryWatchAddresses.Count > 0)
            {
                sb.AppendLine("=== WATCHED MEMORY WRITES ===");
                sb.AppendLine($"Addresses={string.Join(',', debugMemoryWatchAddresses.Select(address => address.ToString("X4")))}");
                foreach (string line in watchedMemoryEvents)
                    sb.AppendLine(line);
                sb.AppendLine();
            }

            sb.AppendLine("=== RECENT PORT EVENTS ===");
            foreach (string line in recentPortEvents)
                sb.AppendLine(line);

            return sb.ToString();
        }

        private void AppendMemoryWindow(StringBuilder sb, ushort start, int length, string? title = null)
        {
            if (!string.IsNullOrEmpty(title))
                sb.AppendLine(title);

            for (int i = 0; i < length; i += 8)
            {
                ushort addr = (ushort)(start + i);
                sb.Append($"{addr:X4}: ");
                for (int j = 0; j < 8 && (i + j) < length; j++)
                {
                    ushort a = (ushort)(addr + j);
                    sb.Append($"{PeekMemory(a):X2} ");
                }
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        private void RecordMemoryEvent(string line)
        {
            if (!debugEventCaptureEnabled)
                return;

            recentMemoryEvents.Enqueue(line);
            while (recentMemoryEvents.Count > DebugHistoryCapacity)
                recentMemoryEvents.Dequeue();
        }

        private void RecordPortEvent(string line)
        {
            if (!debugEventCaptureEnabled)
                return;

            recentPortEvents.Enqueue(line);
            while (recentPortEvents.Count > DebugHistoryCapacity)
                recentPortEvents.Dequeue();
        }

        private void RecordWatchedMemoryEvent(string line)
        {
            watchedMemoryEvents.Enqueue(line);
            while (watchedMemoryEvents.Count > DebugWatchHistoryCapacity)
                watchedMemoryEvents.Dequeue();
        }

        private void RequestAutoDebugDump(string reason)
        {
            if (autoDebugDumpPending || autoDebugDumpSuppressed)
                return;

            autoDebugDumpPending = true;
            autoDebugDumpReason = reason;
            autoDebugDumpSnapshot = BuildDebugDump(reason);
            autoDebugDumpSuppressed = true;
        }

        public void ClearLogs()
        {
            ScreenWriteLog.Clear();
            AboveScreenWriteLog.Clear();
            LastAboveWriteFrame = -1;
        }

        public void EnableFocusedInstructionTrace(ushort startPc, ushort endPc, int startFrame, int frameLimit, int maxEntries)
        {
            if (endPc < startPc)
                throw new ArgumentOutOfRangeException(nameof(endPc), "End PC must be greater than or equal to start PC.");
            if (startFrame < 0)
                throw new ArgumentOutOfRangeException(nameof(startFrame));
            if (frameLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(frameLimit));
            if (maxEntries <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxEntries));

            focusedTraceStartPc = startPc;
            focusedTraceEndPc = endPc;
            focusedTraceStartFrame = startFrame;
            focusedTraceFrameLimit = frameLimit;
            focusedTraceMaxEntries = maxEntries;
            focusedTraceStopPc = null;
            focusedInstructionTrace.Clear();
            debugEventCaptureEnabled = true;
            cpu.SetInstructionTraceCaptureEnabled(true);
        }

        public void DisableFocusedInstructionTrace()
        {
            focusedTraceStartPc = null;
            focusedTraceEndPc = null;
            focusedTraceStartFrame = 0;
            focusedTraceFrameLimit = 0;
            focusedTraceMaxEntries = 0;
            focusedTraceStopPc = null;
            focusedInstructionTrace.Clear();
            debugEventCaptureEnabled = false;
            cpu.SetInstructionTraceCaptureEnabled(false);
            recentMemoryEvents.Clear();
            recentPortEvents.Clear();
        }

        public void SetFocusedInstructionTraceStopProgramCounter(ushort programCounter)
        {
            if (!focusedTraceStartPc.HasValue)
                throw new InvalidOperationException("A focused instruction trace must be enabled before setting its stop PC.");

            focusedTraceStopPc = programCounter;
        }

        public void SetFrameTimingForDebug(int frameTStates)
        {
            if (frameTStates <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameTStates));

            this.frameTStates = frameTStates;
            if (tStatesUntilNextInterrupt > frameTStates)
                tStatesUntilNextInterrupt = frameTStates;
        }

        public void Set48kFloatingBusTimingAdjustments(int displayStartAdjustTStates, int sampleAdjustTStates)
        {
            floatingBusDisplayStartAdjustTStates = displayStartAdjustTStates;
            floatingBusSampleAdjustTStates = sampleAdjustTStates;
        }

        public void SetScreenWriteDiagnosticsEnabled(bool enabled)
        {
            screenWriteDiagnosticsEnabled = enabled;
            if (!enabled)
                ClearLogs();
        }
    }
}
