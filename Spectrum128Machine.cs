using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Tap;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed class Spectrum128Machine
    {
        public const int FrameTStates48 = 69888;
        public const int FrameTStates128 = 70908;
        public const int CpuClockHz48 = 3500000;
        public const int CpuClockHz128 = 3546900;
        // 48K snapshots do not encode the current ULA raster phase. Resume inside the
        // active display window so contention-sensitive beeper loops restart on a stable,
        // representative phase instead of the uncontended top border.
        public const int Default48kSnapshotResumeFramePhase = 27347;
        public const int CpuClockHz = CpuClockHz128;
        public const int ScreenWidth = 256;
        public const int ScreenHeight = 192;
        private const int DisplayLineTStates = 224;
        private const int DisplayAreaStartTStates48 = 14347;
        private const int DisplayAreaVisibleLineTStates = 128;
        private const int DisplayAreaLineCount = 192;
        private const int Default48kFloatingBusDisplayStartAdjustTStates = 0;
        private const int Default48kFloatingBusSampleAdjustTStates = 1;

        private readonly Z80Cpu cpu = new Z80Cpu();
        private readonly byte[][] ramBanks = new byte[8][];
        private readonly byte[][] romBanks = new byte[2][];
        private readonly byte[] keyboardMatrix = new byte[8]
        {
            0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        };
        private readonly ulong[] keyboardRowScanCounts = new ulong[8];
        
        // AY-3-8912
        private readonly Audio.Ay8912 ay = new Audio.Ay8912();

        public Audio.Ay8912 Ay => ay;

        private byte lastAyRegister;
        private bool speakerHigh;
        private bool micHigh;
        private bool frameStartSpeakerHigh;
        private ulong frameStartTStates;
        private int currentFrameExecutedTStates;
        private int lastAudioFrameTStates = FrameTStates128;
        private readonly List<Audio.BeeperEvent> beeperEvents = new List<Audio.BeeperEvent>();
        private readonly List<Audio.AyRegisterWrite> ayWrites = new List<Audio.AyRegisterWrite>();
        private Audio.AyAudioState? frameStartAyState;
        private readonly Queue<Audio.AudioFrame> completedAudioFrames = new Queue<Audio.AudioFrame>();
        private bool captureAudioFramesEnabled = true;

        private const int DebugHistoryCapacity = 8192;
        private readonly Queue<string> recentMemoryEvents = new Queue<string>();
        private readonly Queue<string> recentPortEvents = new Queue<string>();
        private bool autoDebugDumpPending;
        private string? autoDebugDumpReason;
        private string? autoDebugDumpSnapshot;
        private bool autoDebugDumpSuppressed;
        private bool debugEventCaptureEnabled;
        private ushort lastObservedPc;
        private ulong? interruptPulseEndTStates;
        private ushort? focusedTraceStartPc;
        private ushort? focusedTraceEndPc;
        private int focusedTraceStartFrame;
        private int focusedTraceFrameLimit;
        private int focusedTraceMaxEntries;
        private readonly Queue<string> focusedInstructionTrace = new Queue<string>();
        private int floatingBusDisplayStartAdjustTStates;
        private int floatingBusSampleAdjustTStates;
        private int frameTStates = FrameTStates128;
        private int tStatesUntilNextInterrupt;
        private bool realignInterruptPhaseAfterNextAccept;
        private Func<Spectrum128Machine, ushort?>? pendingMountedLoadUsrContinuationResolver;
        private bool pendingMountedLoadUsrContinuationRequiresUsrReturnAddress;
        private ushort? pendingMountedLoadBasicResumeLine;
        private byte pendingMountedLoadBasicResumeStatement;
        private MountedLoadInterpreterContext? pendingMountedLoadInterpreterContext;
        private MountedLoadBasicVariableArea? pendingMountedLoadBasicVariableArea;
        private MountedLoadResumeCursorOverride? pendingMountedLoadResumeCursorOverride;
        private bool pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry;
        private ulong pendingMountedLoadNextStreamingInterpreterRefreshTStates;

        private const ushort RomMainLoopLoadReturnPcMin = 0x15F7;
        private const ushort RomMainLoopLoadReturnPcMax = 0x15FA;
        private const ushort RomLiveLoadLoopReturnPcMin = 0x0E51;
        private const ushort RomLiveLoadLoopReturnPcMax = 0x0E5C;
        private const ushort RomWorkspaceCallbackReturnPc = 0x5E87;
        private const ushort RomKeyboardInputCallbackPcMin = 0x10A8;
        private const ushort RomKeyboardInputCallbackPcMax = 0x10B4;
        private const ushort RomMountedLoadServicePcMin = 0x1615;
        private const ushort RomMountedLoadServicePcMax = 0x16E4;
        private const ushort RomMountedLoadStackServicePcMin = 0x3631;
        private const ushort RomMountedLoadStackServicePcMax = 0x3634;
        private const ushort UsrReturnAddress = 0x2D2B;
        private const ushort EndCalcLiteralAddress = 0x2758;
        private const ushort MainExecutionLoopAddress = 0x1555;
        private const ushort NewPpcAddress = 23618;
        private const ushort NspPcAddress = 23620;
        private const ushort PpcAddress = 23621;
        private const ushort SubPpcAddress = 23623;
        private const ushort CurChlAddress = 23633;
        private const ushort EditLineAddress = 23641;
        private const ushort KCurAddress = 23643;
        private const ushort ChAddAddress = 23645;
        private const ushort XPtrAddress = 23647;
        private const ushort WorkspaceAddress = 23649;
        private const ushort StackBottomAddress = 23651;
        private const ushort StackEndAddress = 23653;
        private const ushort VarsAddress = 23627;
        private const ushort ProgAddress = 23635;
        private const ushort NextLineAddress = 23637;
        private const ushort DataAddress = 23639;
        private const ushort ScreenChannelDescriptorAddress = 23734;
        private const ushort KeyboardChannelDescriptorAddress = 23739;
        private const ushort OldPpcAddress = 23662;
        private const ushort OspPcAddress = 23664;
        private const uint MountedLoadStreamingInterpreterRefreshIntervalTStates = 4096;

        private readonly record struct MountedLoadInterpreterContext(
            ushort Vars,
            ushort Prog,
            ushort NextLine,
            ushort Data,
            ushort CurChl,
            ushort EditLine,
            ushort KCur,
            ushort ChAdd,
            ushort XPtr,
            ushort Workspace,
            ushort StackBottom,
            ushort StackEnd);

        private readonly record struct MountedLoadBasicVariableArea(
            ushort Vars,
            ushort EditLine,
            byte[] Data);

        private readonly record struct MountedLoadResumeCursorOverride(
            ushort? KCur,
            ushort? ChAdd,
            ushort? XPtr);

        public bool SpeakerHigh => speakerHigh;
        public bool SpeakerEdge { get; private set; }

        private void HandleAyPortWrite(ushort port, byte value)
        {
            // 128K AY ports:
            // 0xFFFD selects AY register, 0xBFFD writes the selected register.
            if ((port & 0xC002) == 0xC000)
            {
                lastAyRegister = (byte)(value & 0x0F);
                ay.SelectRegister(lastAyRegister);
                return;
            }

            if ((port & 0xC002) == 0x8000)
            {
                ay.WriteRegister(value);
                RecordAyWrite(lastAyRegister, value);
            }
        }

        private byte last7ffdValue = 0xFF;
        private MountedTape? mountedTape;
        private RzxPlaybackSession? rzxPlayback;
        public MountedTape? MountedTape => mountedTape;

        public Spectrum128Machine(string romFolder)
        {
            if (string.IsNullOrWhiteSpace(romFolder))
                throw new ArgumentException("ROM folder must be provided.", nameof(romFolder));

            for (int i = 0; i < ramBanks.Length; i++)
                ramBanks[i] = new byte[16384];

            for (int i = 0; i < romBanks.Length; i++)
                romBanks[i] = new byte[16384];

            LoadRoms(romFolder);
            InitializeScreenRam();
            ClearKeyboard();

            cpu.ReadMemory = ReadMemoryWithContention;
            cpu.WriteMemory = WriteMemoryWithContention;
            cpu.ReadPort = ReadPortWithContention;
            cpu.ReadPortTimed = ReadPortTimed;
            cpu.WritePort = WritePortWithContention;
            cpu.BeforeInstruction = HandleBeforeInstruction;
            cpu.Reset();
            frameStartTStates = cpu.TStates;
            frameStartSpeakerHigh = speakerHigh;
            frameStartAyState = ay.CaptureAudioState();
            beeperEvents.Clear();
            ayWrites.Clear();
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
            ClearDebugHistory();
            tStatesUntilNextInterrupt = 0;
            realignInterruptPhaseAfterNextAccept = false;
        }

        public Z80Cpu Cpu => cpu;

        public Action<string>? Trace
        {
            get => cpu.Trace;
            set => cpu.Trace = value;
        }

        public int PagedRamBank { get; private set; }
        public int CurrentRomBank { get; private set; }
        public bool PagingLocked { get; private set; }
        public int ScreenBank { get; private set; } = 5;
        public int BorderColor { get; private set; } = 1;
        public int FrameCount { get; private set; }
        public int FrameTStates => frameTStates;
        public int CurrentCpuClockHz => frameTStates == FrameTStates48 ? CpuClockHz48 : CpuClockHz128;
        public bool FlashPhase => ((FrameCount / 16) & 1) != 0;

        public Dictionary<ushort, int> ScreenWriteLog { get; } = new Dictionary<ushort, int>();
        public Dictionary<ushort, int> AboveScreenWriteLog { get; } = new Dictionary<ushort, int>();
        public int LastAboveWriteFrame { get; private set; } = -1;
        public bool HasMountedTape => mountedTape != null;
        public string? MountedTapeName => mountedTape?.DisplayName;
        public bool HasRzxPlayback => rzxPlayback != null;
        public string? RzxPlaybackName => rzxPlayback?.DisplayName;
        public bool HasPendingMountedLoadUsrContinuation =>
            pendingMountedLoadUsrContinuationResolver != null || pendingMountedLoadBasicResumeLine.HasValue;

        public void Reset()
        {
            PagedRamBank = 0;
            CurrentRomBank = 0;
            PagingLocked = false;
            ScreenBank = 5;
            BorderColor = 1;
            FrameCount = 0;
            LastAboveWriteFrame = -1;
            last7ffdValue = 0xFF;
            mountedTape = null;
            rzxPlayback = null;
            pendingMountedLoadUsrContinuationResolver = null;
            pendingMountedLoadUsrContinuationRequiresUsrReturnAddress = false;
            pendingMountedLoadBasicResumeLine = null;
            pendingMountedLoadBasicResumeStatement = 0;
            pendingMountedLoadInterpreterContext = null;
            pendingMountedLoadResumeCursorOverride = null;
            speakerHigh = false;
            micHigh = false;
            SpeakerEdge = false;
            frameTStates = FrameTStates128;
            floatingBusDisplayStartAdjustTStates = 0;
            floatingBusSampleAdjustTStates = 0;

            ClearLogs();
            ClearKeyboard();
            ClearRam();
            InitializeScreenRam();
            ay.Reset();
            lastAyRegister = 0;
            cpu.Reset();
            frameStartTStates = cpu.TStates;
            frameStartSpeakerHigh = speakerHigh;
            frameStartAyState = ay.CaptureAudioState();
            beeperEvents.Clear();
            ayWrites.Clear();
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
            ClearDebugHistory();
            tStatesUntilNextInterrupt = 0;
            realignInterruptPhaseAfterNextAccept = false;
        }

        public void ExecuteFrame()
        {
            if (rzxPlayback != null)
            {
                ExecuteRzxFrame();
                return;
            }

            ExecuteTimeSlice(frameTStates);
        }

        private void ExecuteRzxFrame()
        {
            BeginFrameAudioCapture();

            if (rzxPlayback == null || !rzxPlayback.TryBeginNextFrame(out ushort instructionFetchCount))
            {
                rzxPlayback = null;
                ExecuteFrame();
                return;
            }

            cpu.ExecuteInstructionFetches(instructionFetchCount);
            TriggerFrameInterrupt();
            lastAudioFrameTStates = (int)Math.Min((ulong)int.MaxValue, cpu.TStates - frameStartTStates);
            completedAudioFrames.Enqueue(new Audio.AudioFrame(
                lastAudioFrameTStates,
                CurrentCpuClockHz,
                frameStartSpeakerHigh,
                speakerHigh,
                beeperEvents,
                ay.CaptureAudioState(),
                frameStartAyState,
                ayWrites));
            currentFrameExecutedTStates = 0;
            FrameCount++;
        }

        public Audio.AudioFrame DrainAudioFrame()
        {
            if (completedAudioFrames.Count != 0)
                return completedAudioFrames.Dequeue();

            return new Audio.AudioFrame(
                lastAudioFrameTStates,
                CurrentCpuClockHz,
                frameStartSpeakerHigh,
                speakerHigh,
                beeperEvents,
                ay.CaptureAudioState(),
                frameStartAyState,
                ayWrites);
        }

        public bool TryDequeueCompletedAudioFrame(out Audio.AudioFrame frame)
        {
            if (completedAudioFrames.Count != 0)
            {
                frame = completedAudioFrames.Dequeue();
                return true;
            }

            frame = default!;
            return false;
        }

        public int ExecuteTimeSlice(int tStatesBudget)
        {
            return ExecuteTimeSlice(tStatesBudget, out _);
        }

        public int ExecuteTimeSlice(int tStatesBudget, out int executedTStates)
        {
            executedTStates = 0;
            if (tStatesBudget <= 0)
                return 0;

            if (rzxPlayback != null)
            {
                int completedRzxFrames = 0;
                while (tStatesBudget >= frameTStates)
                {
                    ulong tStatesBefore = cpu.TStates;
                    ExecuteRzxFrame();
                    executedTStates += (int)Math.Min((ulong)int.MaxValue, cpu.TStates - tStatesBefore);
                    completedRzxFrames++;
                    tStatesBudget -= frameTStates;
                }

                return completedRzxFrames;
            }

            int completedFrames = 0;
            while (tStatesBudget > 0)
            {
                if (currentFrameExecutedTStates == 0)
                    BeginFrameAudioCapture();

                int remainingFrameTStates = frameTStates - currentFrameExecutedTStates;
                if (tStatesUntilNextInterrupt == 0)
                {
                    TriggerFrameInterrupt();
                    tStatesUntilNextInterrupt = realignInterruptPhaseAfterNextAccept
                        ? remainingFrameTStates
                        : frameTStates;
                    realignInterruptPhaseAfterNextAccept = false;
                }

                int executionChunk = Math.Min(tStatesBudget, Math.Min(remainingFrameTStates, tStatesUntilNextInterrupt));
                ulong tStatesBefore = cpu.TStates;
                cpu.ExecuteCycles((ulong)executionChunk);
                int actualExecutedTStates = (int)Math.Min((ulong)int.MaxValue, cpu.TStates - tStatesBefore);
                executedTStates += actualExecutedTStates;
                mountedTape?.AdvanceToTime(cpu.TStates);
                tStatesBudget -= actualExecutedTStates;
                if (tStatesBudget < 0)
                    tStatesBudget = 0;

                remainingFrameTStates -= actualExecutedTStates;
                currentFrameExecutedTStates += actualExecutedTStates;
                tStatesUntilNextInterrupt -= actualExecutedTStates;
                if (tStatesUntilNextInterrupt < 0)
                    tStatesUntilNextInterrupt = 0;

                if (remainingFrameTStates > 0)
                    continue;

                lastAudioFrameTStates = (int)Math.Min((ulong)int.MaxValue, cpu.TStates - frameStartTStates);
                if (captureAudioFramesEnabled)
                {
                    completedAudioFrames.Enqueue(new Audio.AudioFrame(
                        lastAudioFrameTStates,
                        CurrentCpuClockHz,
                        frameStartSpeakerHigh,
                        speakerHigh,
                        beeperEvents,
                        ay.CaptureAudioState(),
                        frameStartAyState,
                        ayWrites));
                }
                else
                {
                    beeperEvents.Clear();
                    ayWrites.Clear();
                    frameStartAyState = null;
                }

                currentFrameExecutedTStates = 0;
                FrameCount++;
                completedFrames++;
            }

            return completedFrames;
        }

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
            if (!enabled)
            {
                recentMemoryEvents.Clear();
                recentPortEvents.Clear();
            }
        }

        public void SetAudioFrameCaptureEnabled(bool enabled)
        {
            captureAudioFramesEnabled = enabled;
            if (!enabled)
            {
                completedAudioFrames.Clear();
                beeperEvents.Clear();
                ayWrites.Clear();
                frameStartAyState = null;
            }
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
            sb.AppendLine($"SpeakerHigh={speakerHigh} SpeakerEdge={SpeakerEdge} FlashPhase={FlashPhase} MountedTape={mountedTape?.DisplayName ?? "(none)"}");
            if (mountedTape != null)
                sb.AppendLine($"TapeDebug={mountedTape.DebugPlaybackState}");
            sb.AppendLine();

            sb.AppendLine("=== CPU STATE ===");
            sb.AppendLine($"TStates={cpu.TStates}");
            sb.AppendLine($"PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}");
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
                sb.AppendLine($"Row{row}={keyboardMatrix[row]:X2}");
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

        private ulong Get48kContentionDelay(ulong tStates)
        {
            if (frameTStates != FrameTStates48)
                return 0;

            ulong frameOffset = tStates % (ulong)FrameTStates48;
            if (frameOffset < 14335UL)
                return 0;

            ulong displayOffset = frameOffset - 14335UL;
            if (displayOffset >= (ulong)(DisplayLineTStates * DisplayAreaLineCount))
                return 0;

            int lineTState = (int)(displayOffset % (ulong)DisplayLineTStates);
            if (lineTState >= DisplayAreaVisibleLineTStates)
                return 0;

            int phase = lineTState & 0x07;
            return phase < 6 ? (ulong)(6 - phase) : 0;
        }

        private bool Is48kContendedMemoryAddress(ushort addr) => addr >= 0x4000 && addr < 0x8000;

        private void Apply48kMemoryContention(ushort addr)
        {
            if (!Is48kContendedMemoryAddress(addr))
                return;

            ulong delay = Get48kContentionDelay(cpu.TStates);
            if (delay != 0)
                cpu.AddTStates(delay);
        }

        private ulong Get48kIoContentionDelay(ushort port)
        {
            if (frameTStates != FrameTStates48)
                return 0;

            bool highContended = (port & 0xFF00) >= 0x4000 && (port & 0xFF00) <= 0x7F00;
            bool lowBitClear = (port & 0x0001) == 0;

            if (!highContended && !lowBitClear)
                return 0;

            ulong currentTStates = cpu.TStates;
            ulong totalDelay = 0;

            void ApplyContendedSegment(int advanceTStates)
            {
                ulong delay = Get48kContentionDelay(currentTStates);
                totalDelay += delay;
                currentTStates += delay + (ulong)advanceTStates;
            }

            void ApplyUncontendedSegment(int advanceTStates)
            {
                currentTStates += (ulong)advanceTStates;
            }

            if (highContended && lowBitClear)
            {
                ApplyContendedSegment(1);
                ApplyContendedSegment(3);
            }
            else if (highContended)
            {
                ApplyContendedSegment(1);
                ApplyContendedSegment(1);
                ApplyContendedSegment(1);
                ApplyContendedSegment(1);
            }
            else
            {
                ApplyUncontendedSegment(1);
                ApplyContendedSegment(3);
            }

            return totalDelay;
        }

        private byte ReadMemoryWithContention(ushort addr)
        {
            Apply48kMemoryContention(addr);
            return ReadMemory(addr);
        }

        private void WriteMemoryWithContention(ushort addr, byte value)
        {
            Apply48kMemoryContention(addr);
            WriteMemory(addr, value);
        }

        private byte ReadPortWithContention(ushort port)
        {
            ulong delay = Get48kIoContentionDelay(port);
            if (delay != 0)
                cpu.AddTStates(delay);

            return ReadPort(port);
        }

        private void WritePortWithContention(ushort port, byte value)
        {
            ulong delay = Get48kIoContentionDelay(port);
            if (delay != 0)
                cpu.AddTStates(delay);

            WritePort(port, value);
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

        public void ClearKeyboard()
        {
            for (int i = 0; i < keyboardMatrix.Length; i++)
                keyboardMatrix[i] = 0xFF;
        }

        public void SetKey(int row, int bit, bool pressed)
        {
            if ((uint)row >= keyboardMatrix.Length)
                throw new ArgumentOutOfRangeException(nameof(row));
            if ((uint)bit >= 5)
                throw new ArgumentOutOfRangeException(nameof(bit));

            if (pressed)
                keyboardMatrix[row] = (byte)(keyboardMatrix[row] & ~(1 << bit));
            else
                keyboardMatrix[row] = (byte)(keyboardMatrix[row] | (1 << bit));
        }

        public byte[] GetKeyboardMatrixCopy() => (byte[])keyboardMatrix.Clone();

        public ulong GetKeyboardRowScanCount(int row)
        {
            if ((uint)row >= keyboardRowScanCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(row));

            return keyboardRowScanCounts[row];
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
            focusedInstructionTrace.Clear();
            debugEventCaptureEnabled = true;
        }

        public void DisableFocusedInstructionTrace()
        {
            focusedTraceStartPc = null;
            focusedTraceEndPc = null;
            focusedTraceStartFrame = 0;
            focusedTraceFrameLimit = 0;
            focusedTraceMaxEntries = 0;
            focusedInstructionTrace.Clear();
            debugEventCaptureEnabled = false;
            recentMemoryEvents.Clear();
            recentPortEvents.Clear();
        }

        public void SetInitialInterruptDelay(int tStatesUntilNextInterrupt)
        {
            if (tStatesUntilNextInterrupt < 0 || tStatesUntilNextInterrupt > frameTStates)
                throw new ArgumentOutOfRangeException(nameof(tStatesUntilNextInterrupt));

            this.tStatesUntilNextInterrupt = tStatesUntilNextInterrupt;
            realignInterruptPhaseAfterNextAccept = false;
        }

        public void SetSnapshotInitialInterruptDelay(int tStatesUntilNextInterrupt)
        {
            if (tStatesUntilNextInterrupt < 0 || tStatesUntilNextInterrupt > frameTStates)
                throw new ArgumentOutOfRangeException(nameof(tStatesUntilNextInterrupt));

            this.tStatesUntilNextInterrupt = tStatesUntilNextInterrupt;
            realignInterruptPhaseAfterNextAccept = tStatesUntilNextInterrupt != 0;
        }

        public void SetSnapshotResumeFramePhase(int framePhaseTStates)
        {
            if (framePhaseTStates < 0 || framePhaseTStates >= frameTStates)
                throw new ArgumentOutOfRangeException(nameof(framePhaseTStates));

            int currentPhase = (int)(cpu.TStates % (ulong)frameTStates);
            int advance = framePhaseTStates - currentPhase;
            if (advance < 0)
                advance += frameTStates;

            if (advance != 0)
                cpu.AdvanceTStates((uint)advance);

            tStatesUntilNextInterrupt = framePhaseTStates == 0
                ? 0
                : frameTStates - framePhaseTStates;
            realignInterruptPhaseAfterNextAccept = false;
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

        public byte[] GetScreenBankData() => ramBanks[ScreenBank];

        public void ForceApply7ffdValue(byte value)
        {
            int oldRam = PagedRamBank;
            int oldScreen = ScreenBank;
            int oldRom = CurrentRomBank;

            PagedRamBank = value & 0x07;
            ScreenBank = ((value & 0x08) != 0) ? 7 : 5;
            CurrentRomBank = ((value & 0x10) != 0) ? 1 : 0;

            if ((value & 0x20) != 0)
                PagingLocked = true;

            byte newPaging = (byte)(value & 0x3F);
            if (newPaging != last7ffdValue)
            {
                last7ffdValue = newPaging;
                Trace?.Invoke(
                    $"[7FFD*] PC=0x{cpu.Regs.PC:X4} Frame={FrameCount} RAM {oldRam}->{PagedRamBank} SCREEN {oldScreen}->{ScreenBank} ROM {oldRom}->{CurrentRomBank} VAL=0x{value:X2}");
            }
        }

        public byte[] GetRamBankCopy(int bank)
        {
            if ((uint)bank >= ramBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));

            return (byte[])ramBanks[bank].Clone();
        }

        public byte PeekMemory(ushort addr) => ReadMemory(addr);

        public void PokeMemory(ushort addr, byte value) => WriteMemory(addr, value);

        public byte DebugReadPort(ushort port) => ReadPort(port);
        public string GetMountedTapeDebugState() => mountedTape?.DebugPlaybackState ?? "(none)";

        public void DebugWritePort(ushort port, byte value) => WritePort(port, value);

        public void MountTape(MountedTape tape)
        {
            mountedTape = tape ?? throw new ArgumentNullException(nameof(tape));
            mountedTape.Reset();
        }

        public void AttachRzxPlayback(RzxPlaybackSession playback)
        {
            rzxPlayback = playback ?? throw new ArgumentNullException(nameof(playback));
        }

        public void DetachRzxPlayback()
        {
            rzxPlayback = null;
        }

        public void EjectTape()
        {
            mountedTape = null;
        }

        public bool TryServiceTapeTrap()
        {
            return mountedTape != null && mountedTape.TryHandleRomLoadTrap(this, cpu);
        }

        public BootstrapTapeLoadResult TryConsumeBootstrapTapeLoad()
        {
            return mountedTape != null
                ? mountedTape.TryConsumeBootstrapLoad(this)
                : BootstrapTapeLoadResult.None;
        }

        public void SetPendingMountedLoadUsrContinuation(ushort entryPoint)
        {
            CaptureMountedLoadInterpreterContextIfNeeded();
            pendingMountedLoadUsrContinuationResolver = _ => entryPoint;
            pendingMountedLoadUsrContinuationRequiresUsrReturnAddress = false;
            pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry = false;
            pendingMountedLoadNextStreamingInterpreterRefreshTStates = 0;
        }

        public void SetPendingMountedLoadUsrContinuationResolver(
            Func<Spectrum128Machine, ushort?> resolver,
            bool requireUsrReturnAddress = false)
        {
            CaptureMountedLoadInterpreterContextIfNeeded();
            pendingMountedLoadUsrContinuationResolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            pendingMountedLoadUsrContinuationRequiresUsrReturnAddress = requireUsrReturnAddress;
            pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry = false;
            pendingMountedLoadNextStreamingInterpreterRefreshTStates = 0;
        }

        public void SetPendingMountedLoadDirectUsrContextPolicy(bool preserveLiveInterpreterState)
        {
            pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry = preserveLiveInterpreterState;
        }

        internal bool PendingMountedLoadUsrContinuationRequiresUsrReturnAddress =>
            pendingMountedLoadUsrContinuationRequiresUsrReturnAddress;

        public void ClearPendingMountedLoadUsrContinuation()
        {
            pendingMountedLoadUsrContinuationResolver = null;
            pendingMountedLoadUsrContinuationRequiresUsrReturnAddress = false;
            pendingMountedLoadBasicResumeLine = null;
            pendingMountedLoadBasicResumeStatement = 0;
            pendingMountedLoadInterpreterContext = null;
            pendingMountedLoadBasicVariableArea = null;
            pendingMountedLoadResumeCursorOverride = null;
            pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry = false;
            pendingMountedLoadNextStreamingInterpreterRefreshTStates = 0;
        }

        public void RefreshPendingMountedLoadInterpreterContext(bool forceVariableAreaRefresh = false)
        {
            if (forceVariableAreaRefresh || !pendingMountedLoadBasicVariableArea.HasValue)
            {
                ushort vars = ReadWord(VarsAddress);
                ushort eLine = ReadWord(EditLineAddress);
                byte[] variableData = Array.Empty<byte>();
                if (vars < eLine)
                {
                    int length = eLine - vars;
                    variableData = new byte[length];
                    for (int i = 0; i < length; i++)
                        variableData[i] = PeekMemory((ushort)(vars + i));
                }

                pendingMountedLoadBasicVariableArea = new MountedLoadBasicVariableArea(
                    vars,
                    eLine,
                    variableData);
            }

            if (pendingMountedLoadInterpreterContext.HasValue)
            {
                MountedLoadInterpreterContext original = pendingMountedLoadInterpreterContext.Value;
                pendingMountedLoadInterpreterContext = new MountedLoadInterpreterContext(
                    original.Vars,
                    original.Prog,
                    original.NextLine,
                    original.Data,
                    ReadWord(CurChlAddress),
                    ReadWord(EditLineAddress),
                    ReadWord(KCurAddress),
                    ReadWord(ChAddAddress),
                    ReadWord(XPtrAddress),
                    ReadWord(WorkspaceAddress),
                    ReadWord(StackBottomAddress),
                    ReadWord(StackEndAddress));
                return;
            }

            pendingMountedLoadInterpreterContext = new MountedLoadInterpreterContext(
                ReadWord(VarsAddress),
                ReadWord(ProgAddress),
                ReadWord(NextLineAddress),
                ReadWord(DataAddress),
                ReadWord(CurChlAddress),
                ReadWord(EditLineAddress),
                ReadWord(KCurAddress),
                ReadWord(ChAddAddress),
                ReadWord(XPtrAddress),
                ReadWord(WorkspaceAddress),
                ReadWord(StackBottomAddress),
                ReadWord(StackEndAddress));
        }

        private bool PendingMountedLoadHasPreservedBasicVariableSnapshot()
        {
            return pendingMountedLoadBasicVariableArea.HasValue &&
                   pendingMountedLoadBasicVariableArea.Value.Data.Length > 0;
        }

        public void SetPendingMountedLoadBasicResume(ushort lineNumber, byte statementIndex)
        {
            pendingMountedLoadBasicResumeLine = lineNumber;
            pendingMountedLoadBasicResumeStatement = statementIndex;
        }

        public void SetPendingMountedLoadResumeCursorOverride(
            ushort? kCur = null,
            ushort? chAdd = null,
            ushort? xPtr = null)
        {
            pendingMountedLoadResumeCursorOverride = new MountedLoadResumeCursorOverride(kCur, chAdd, xPtr);
        }

        internal bool TryGetPendingMountedLoadBasicVariableArea(out ushort vars, out ushort eLine)
        {
            vars = 0;
            eLine = 0;
            if (!pendingMountedLoadBasicVariableArea.HasValue)
                return false;

            MountedLoadBasicVariableArea context = pendingMountedLoadBasicVariableArea.Value;
            if (context.Vars >= context.EditLine)
                return false;

            vars = context.Vars;
            eLine = context.EditLine;
            return true;
        }

        internal bool TryGetPendingMountedLoadBasicVariableSnapshot(out ushort vars, out byte[] data)
        {
            vars = 0;
            data = Array.Empty<byte>();
            if (!pendingMountedLoadBasicVariableArea.HasValue)
                return false;

            MountedLoadBasicVariableArea context = pendingMountedLoadBasicVariableArea.Value;
            if (context.Vars >= context.EditLine || context.Data.Length == 0)
                return false;

            vars = context.Vars;
            data = context.Data;
            return true;
        }

        private bool HandleBeforeInstruction(Z80Cpu z80)
        {
            if (interruptPulseEndTStates.HasValue &&
                z80.InterruptPending &&
                z80.TStates >= interruptPulseEndTStates.Value)
            {
                z80.InterruptPending = false;
                interruptPulseEndTStates = null;
            }

            if (focusedTraceStartPc.HasValue &&
                focusedTraceEndPc.HasValue &&
                FrameCount >= focusedTraceStartFrame &&
                FrameCount < focusedTraceStartFrame + focusedTraceFrameLimit &&
                z80.Regs.PC >= focusedTraceStartPc.Value &&
                z80.Regs.PC <= focusedTraceEndPc.Value)
            {
                focusedInstructionTrace.Enqueue(
                    $"F={FrameCount,4} T={z80.TStates,10} PC={z80.Regs.PC:X4} " +
                    $"OP={PeekMemory(z80.Regs.PC):X2} N={PeekMemory((ushort)(z80.Regs.PC + 1)):X2} {PeekMemory((ushort)(z80.Regs.PC + 2)):X2} " +
                    $"SP={z80.Regs.SP:X4} AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} I={z80.Regs.I:X2} R={z80.Regs.R:X2} " +
                    $"IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} INTP={(z80.InterruptPending ? 1 : 0)}");

                while (focusedInstructionTrace.Count > focusedTraceMaxEntries)
                    focusedInstructionTrace.Dequeue();
            }

            lastObservedPc = z80.Regs.PC;

            if (TryResumePendingMountedLoadUsrContinuation(z80))
                return false;

            return mountedTape != null && mountedTape.TryHandleRomLoadTrap(this, z80);
        }

        private bool TryResumePendingMountedLoadUsrContinuation(Z80Cpu z80)
        {
            if (pendingMountedLoadUsrContinuationResolver == null ||
                mountedTape == null)
            {
                return false;
            }

            if (z80.Regs.PC == UsrReturnAddress)
                RefreshPendingMountedLoadInterpreterContext(forceVariableAreaRefresh: true);

            if (pendingMountedLoadUsrContinuationRequiresUsrReturnAddress &&
                z80.Regs.PC != UsrReturnAddress)
            {
                return false;
            }

            if (!pendingMountedLoadUsrContinuationRequiresUsrReturnAddress &&
                mountedTape.IsActivelyStreamingEarSignal &&
                !PendingMountedLoadHasPreservedBasicVariableSnapshot() &&
                z80.TStates >= pendingMountedLoadNextStreamingInterpreterRefreshTStates)
            {
                RefreshPendingMountedLoadInterpreterContext(forceVariableAreaRefresh: true);
                pendingMountedLoadNextStreamingInterpreterRefreshTStates =
                    z80.TStates + MountedLoadStreamingInterpreterRefreshIntervalTStates;
            }

            if (mountedTape.IsActivelyStreamingEarSignal)
                return false;

            if (!CanResumeMountedLoadUsrContinuation(z80.Regs.PC, mountedTape))
                return false;

            Func<Spectrum128Machine, ushort?> resolver = pendingMountedLoadUsrContinuationResolver;
            pendingMountedLoadUsrContinuationResolver = null;
            pendingMountedLoadUsrContinuationRequiresUsrReturnAddress = false;
            ushort? resolvedEntryPoint = resolver(this);
            bool preserveLiveInterpreterStateForDirectUsrEntry =
                pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry;
            pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry = false;
            if (!resolvedEntryPoint.HasValue)
                return false;

            if (pendingMountedLoadBasicResumeLine.HasValue)
            {
                ushort lineNumber = pendingMountedLoadBasicResumeLine.Value;
                byte statementIndex = pendingMountedLoadBasicResumeStatement;
                pendingMountedLoadBasicResumeLine = null;
                pendingMountedLoadBasicResumeStatement = 0;
                RestoreInterpreterWorkspaceForRomBasicResume();
                ApplyPendingMountedLoadResumeCursorOverride();
                WriteMemory(NewPpcAddress, (byte)(lineNumber & 0xFF));
                WriteMemory((ushort)(NewPpcAddress + 1), (byte)(lineNumber >> 8));
                WriteMemory(NspPcAddress, statementIndex);
                WriteMemory(PpcAddress, (byte)(lineNumber & 0xFF));
                WriteMemory((ushort)(PpcAddress + 1), (byte)(lineNumber >> 8));
                WriteMemory(SubPpcAddress, statementIndex);
                z80.Regs.PC = MainExecutionLoopAddress;
                return true;
            }

            ushort entryPoint = resolvedEntryPoint.Value;

            if (entryPoint == 0)
            {
                EnterUsr0Mode(z80);
                return true;
            }

            if (!preserveLiveInterpreterStateForDirectUsrEntry)
            {
                RestoreInterpreterWorkspaceForDirectUsrEntry();
                ApplyPendingMountedLoadResumeCursorOverride();
            }
            else
            {
                pendingMountedLoadResumeCursorOverride = null;
            }
            z80.Regs.SP -= 2;
            WriteMemory(z80.Regs.SP, (byte)(UsrReturnAddress & 0xFF));
            WriteMemory((ushort)(z80.Regs.SP + 1), (byte)(UsrReturnAddress >> 8));

            z80.Regs.BC = entryPoint;
            z80.Regs.H_ = (byte)(EndCalcLiteralAddress >> 8);
            z80.Regs.L_ = (byte)(EndCalcLiteralAddress & 0xFF);
            z80.Regs.PC = entryPoint;
            return true;
        }

        private void RestoreInterpreterWorkspaceForRomBasicResume()
        {
            if (pendingMountedLoadInterpreterContext.HasValue)
            {
                MountedLoadInterpreterContext context = pendingMountedLoadInterpreterContext.Value;
                WriteWord(VarsAddress, context.Vars);
                WriteWord(ProgAddress, context.Prog);
                WriteWord(NextLineAddress, context.NextLine);
                WriteWord(DataAddress, context.Data);
                WriteWord(CurChlAddress, context.CurChl);
                WriteWord(EditLineAddress, context.EditLine);
                WriteWord(KCurAddress, context.KCur);
                WriteWord(ChAddAddress, context.ChAdd);
                WriteWord(XPtrAddress, context.XPtr);
                WriteWord(WorkspaceAddress, context.Workspace);
                WriteWord(StackBottomAddress, context.StackBottom);
                WriteWord(StackEndAddress, context.StackEnd);
                WriteMemory(context.EditLine, 0x0D);
                WriteMemory((ushort)(context.EditLine + 1), 0x00);
                return;
            }

            ushort eLine = ReadWord(EditLineAddress);
            WriteMemory(eLine, 0x0D);
            WriteMemory((ushort)(eLine + 1), 0x00);
            WriteWord(WorkspaceAddress, eLine);
            WriteWord(StackBottomAddress, eLine);
            WriteWord(StackEndAddress, eLine);
            WriteWord(KCurAddress, eLine);
            WriteWord(ChAddAddress, eLine);
            WriteWord(XPtrAddress, eLine);
        }

        private void CaptureMountedLoadInterpreterContextIfNeeded()
        {
            if (pendingMountedLoadInterpreterContext.HasValue)
                return;

            RefreshPendingMountedLoadInterpreterContext();
        }

        private void RestoreInterpreterWorkspaceForDirectUsrEntry()
        {
            if (!pendingMountedLoadInterpreterContext.HasValue)
                return;

            MountedLoadInterpreterContext context = pendingMountedLoadInterpreterContext.Value;
            WriteWord(VarsAddress, context.Vars);
            WriteWord(ProgAddress, context.Prog);
            WriteWord(NextLineAddress, context.NextLine);
            WriteWord(DataAddress, context.Data);
            WriteWord(CurChlAddress, context.CurChl);
            WriteWord(EditLineAddress, context.EditLine);
            WriteWord(KCurAddress, context.KCur);
            WriteWord(ChAddAddress, context.ChAdd);
            WriteWord(XPtrAddress, context.XPtr);
            WriteWord(WorkspaceAddress, context.Workspace);
            WriteWord(StackBottomAddress, context.StackBottom);
            WriteWord(StackEndAddress, context.StackEnd);
        }

        private void ApplyPendingMountedLoadResumeCursorOverride()
        {
            if (!pendingMountedLoadResumeCursorOverride.HasValue)
                return;

            MountedLoadResumeCursorOverride cursorOverride = pendingMountedLoadResumeCursorOverride.Value;
            pendingMountedLoadResumeCursorOverride = null;

            if (cursorOverride.KCur.HasValue)
                WriteWord(KCurAddress, cursorOverride.KCur.Value);

            if (cursorOverride.ChAdd.HasValue)
                WriteWord(ChAddAddress, cursorOverride.ChAdd.Value);

            if (cursorOverride.XPtr.HasValue)
                WriteWord(XPtrAddress, cursorOverride.XPtr.Value);
        }

        private ushort ReadWord(ushort address)
        {
            byte low = PeekMemory(address);
            byte high = PeekMemory((ushort)(address + 1));
            return (ushort)(low | (high << 8));
        }

        private void WriteWord(ushort address, ushort value)
        {
            WriteMemory(address, (byte)(value & 0xFF));
            WriteMemory((ushort)(address + 1), (byte)(value >> 8));
        }

        private static bool CanResumeMountedLoadUsrContinuation(ushort pc, MountedTape mountedTape)
        {
            if (pc == UsrReturnAddress)
                return true;

            if (!IsMountedLoadReturnPc(pc))
                return false;

            return mountedTape.IsAtMountedLoadUsrContinuationBoundary;
        }

        private static bool IsMountedLoadReturnPc(ushort pc)
        {
            return (pc >= RomMainLoopLoadReturnPcMin && pc <= RomMainLoopLoadReturnPcMax) ||
                   (pc >= RomLiveLoadLoopReturnPcMin && pc <= RomLiveLoadLoopReturnPcMax) ||
                   (pc >= 0x15FD && pc <= 0x1600) ||
                   (pc >= RomKeyboardInputCallbackPcMin && pc <= RomKeyboardInputCallbackPcMax) ||
                   pc == RomWorkspaceCallbackReturnPc ||
                   (pc >= RomMountedLoadServicePcMin && pc <= RomMountedLoadServicePcMax) ||
                   (pc >= RomMountedLoadStackServicePcMin && pc <= RomMountedLoadStackServicePcMax);
        }

        private void EnterUsr0Mode(Z80Cpu z80)
        {
            pendingMountedLoadBasicResumeLine = null;
            pendingMountedLoadBasicResumeStatement = 0;
            pendingMountedLoadInterpreterContext = null;
            pendingMountedLoadBasicVariableArea = null;
            pendingMountedLoadResumeCursorOverride = null;
            pendingMountedLoadPreserveLiveInterpreterStateForDirectUsrEntry = false;
            pendingMountedLoadNextStreamingInterpreterRefreshTStates = 0;
            if (mountedTape != null && !mountedTape.HasMoreBlocks)
                mountedTape = null;
            CurrentRomBank = 1;
            PagingLocked = false;
            frameTStates = FrameTStates128;
            last7ffdValue = (byte)(
                (PagedRamBank & 0x07) |
                (ScreenBank == 7 ? 0x08 : 0x00) |
                0x10);

            // USR 0 enters ROM48 from BASIC, but it should not carry a stale
            // mounted-line continuation cursor into the fresh ROM entry path.
            WriteWord(NewPpcAddress, 0);
            WriteMemory(NspPcAddress, 0);
            WriteWord(PpcAddress, 0);
            WriteMemory(SubPpcAddress, 0);
            WriteWord(OldPpcAddress, 0);
            WriteMemory(OspPcAddress, 0);
            InitializeUsr0InterpreterWorkspace();

            z80.Regs.BC = 0;
            z80.Regs.H_ = (byte)(EndCalcLiteralAddress >> 8);
            z80.Regs.L_ = (byte)(EndCalcLiteralAddress & 0xFF);
            z80.Regs.PC = 0;
        }

        private void InitializeUsr0InterpreterWorkspace()
        {
            ushort eLine = ReadWord(EditLineAddress);
            if (eLine < 0x5B00)
                eLine = (ushort)(ReadWord(VarsAddress) + 1);

            WriteMemory(eLine, 0x0D);
            WriteMemory((ushort)(eLine + 1), 0x00);
            WriteWord(CurChlAddress, KeyboardChannelDescriptorAddress);
            WriteWord(KCurAddress, eLine);
            WriteWord(ChAddAddress, eLine);
            WriteWord(XPtrAddress, 0);
            ushort workspace = (ushort)(eLine + 1);
            WriteWord(WorkspaceAddress, workspace);
            WriteWord(StackBottomAddress, workspace);
            WriteWord(StackEndAddress, workspace);
        }

        private void LoadRoms(string romFolder)
        {
            string rom0Path = Path.Combine(romFolder, "128-0.rom");
            string rom1Path = Path.Combine(romFolder, "128-1.rom");

            byte[] rom0 = File.ReadAllBytes(rom0Path);
            byte[] rom1 = File.ReadAllBytes(rom1Path);

            if (rom0.Length != 16384 || rom1.Length != 16384)
                throw new InvalidOperationException("ROM files must be 16KB each.");

            romBanks[0] = rom0;
            romBanks[1] = rom1;
        }

        private void ClearRam()
        {
            for (int bank = 0; bank < ramBanks.Length; bank++)
                Array.Clear(ramBanks[bank], 0, ramBanks[bank].Length);
        }

        private void InitializeScreenRam()
        {
            byte[] screenRam = ramBanks[5];

            for (int i = 0; i < 0x1800; i++)
                screenRam[i] = 0;

            for (int i = 0x1800; i < 0x1B00; i++)
                screenRam[i] = 0x38;
        }

        private byte ReadMemory(ushort addr)
        {
            if (addr < 0x4000)
                return romBanks[CurrentRomBank][addr];

            int bank = addr switch
            {
                < 0x8000 => 5,
                < 0xC000 => 2,
                _ => PagedRamBank
            };

            return ramBanks[bank][addr & 0x3FFF];
        }

        private void WriteMemory(ushort addr, byte value)
        {
            if (addr < 0x4000)
                return;

            int bank = addr switch
            {
                < 0x8000 => 5,
                < 0xC000 => 2,
                _ => PagedRamBank
            };

            if (addr >= 0x4000 && addr < 0x5B00)
            {
                if (!ScreenWriteLog.ContainsKey(addr))
                    ScreenWriteLog[addr] = 0;
                ScreenWriteLog[addr]++;
            }
            else if (addr >= 0x5B00 && addr < 0x5C00)
            {
                if (!AboveScreenWriteLog.ContainsKey(addr))
                    AboveScreenWriteLog[addr] = 0;
                AboveScreenWriteLog[addr]++;
                LastAboveWriteFrame = FrameCount;
            }

            ramBanks[bank][addr & 0x3FFF] = value;
            RecordMemoryEvent($"T={cpu.TStates,10} W {addr:X4}={value:X2} bank={bank} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
        }

        public byte ReadPort(ushort port)
        {
            if (rzxPlayback != null && rzxPlayback.TryReadPortValue(out byte replayValue))
                return replayValue;

            if ((port & 0x0001) == 0)
                return ReadKeyboardEarPort(port, cpu.TStates);

            if (frameTStates == FrameTStates48)
                return ReadFloatingBus48(cpu.TStates);

            return 0xFF;
        }

        private byte ReadPortTimed(ushort port, int sampleOffsetTStates)
        {
            ulong contentionDelay = Get48kIoContentionDelay(port);
            if (contentionDelay != 0)
                cpu.AddTStates(contentionDelay);

            if ((port & 0x0001) == 0)
            {
                long adjustedSampleTStates = (long)cpu.TStates + sampleOffsetTStates;
                ulong sampleTStates = adjustedSampleTStates > 0 ? (ulong)adjustedSampleTStates : 0UL;
                return ReadKeyboardEarPort(port, sampleTStates);
            }

            if (frameTStates == FrameTStates48)
            {
                long adjustedSampleTStates = (long)cpu.TStates + sampleOffsetTStates + floatingBusSampleAdjustTStates;
                ulong sampleTStates = adjustedSampleTStates > 0 ? (ulong)adjustedSampleTStates : 0UL;
                return ReadFloatingBus48(sampleTStates);
            }

            return 0xFF;
        }

        private byte ReadKeyboardEarPort(ushort port, ulong sampleTStates)
        {
            byte result = 0xFF;
            byte high = (byte)(port >> 8);

            for (int row = 0; row < 8; row++)
            {
                if ((high & (1 << row)) == 0)
                {
                    keyboardRowScanCounts[row]++;
                    result &= keyboardMatrix[row];
                }
            }

            bool tapeEarHigh = mountedTape?.ReadEarBit(sampleTStates) ?? true;
            // On Issue 3-era Spectrum hardware the FE input bit follows the EAR/speaker
            // line, while MIC-only output does not read back as a stable high level.
            bool earHigh = tapeEarHigh || speakerHigh;
            if (earHigh)
                result |= 0x40;
            else
                result = (byte)(result & ~0x40);

            return result;
        }

        private byte ReadFloatingBus48(ulong sampleTStates)
        {
            long adjustedFrameOffset = (long)(sampleTStates % (ulong)FrameTStates48) - floatingBusDisplayStartAdjustTStates;
            if (adjustedFrameOffset < 0)
                adjustedFrameOffset += FrameTStates48;

            ulong frameOffset = (ulong)adjustedFrameOffset;
            if (frameOffset < DisplayAreaStartTStates48)
                return 0xFF;

            int displayOffset = (int)(frameOffset - DisplayAreaStartTStates48);
            int displaySpan = DisplayLineTStates * DisplayAreaLineCount;
            if (displayOffset >= displaySpan)
                return 0xFF;

            int line = displayOffset / DisplayLineTStates;
            int lineTState = displayOffset % DisplayLineTStates;
            if (lineTState >= DisplayAreaVisibleLineTStates)
                return 0xFF;

            int fetchPhase = lineTState & 0x07;
            int column = (lineTState >> 3) << 1;
            int charRow = line >> 3;
            int charLine = line & 0x07;
            int pixelOffset = ((charRow & 0x18) << 8) |
                              ((charRow & 0x07) << 5) |
                              (charLine << 8) |
                              column;
            int attrOffset = 0x1800 + (charRow * 32) + column;

            // The ULA drives two pixel/attribute pairs every 8 T-states. Outside
            // those fetches, odd-port reads see the undriven bus rather than a
            // sticky copy of the final attribute byte.
            if (fetchPhase < 4)
            {
                int pairOffset = fetchPhase >= 2 ? 1 : 0;
                return (fetchPhase & 0x01) == 0
                    ? ramBanks[ScreenBank][pixelOffset + pairOffset]
                    : ramBanks[ScreenBank][attrOffset + pairOffset];
            }

            return 0xFF;
        }

        public void ConfigureFor48kSnapshot(int borderColor)
        {
            Configure48kSnapshotCore(borderColor, FrameTStates48);
        }

        public void ConfigureFor48kZ80Snapshot(int borderColor)
        {
            Configure48kSnapshotCore(borderColor, FrameTStates128);
        }

        public void ConfigureFor48kTapeLoad(int borderColor)
        {
            ConfigureFor48kSnapshot(borderColor);
            frameTStates = FrameTStates48;
            tStatesUntilNextInterrupt = 0;
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
        }

        public void ConfigureFor128kTapeLoad(int borderColor)
        {
            PagedRamBank = 0;
            ScreenBank = 5;
            CurrentRomBank = 1;
            PagingLocked = false;
            BorderColor = borderColor & 0x07;
            frameTStates = FrameTStates128;
            FrameCount = 0;
            floatingBusDisplayStartAdjustTStates = 0;
            floatingBusSampleAdjustTStates = 0;
            last7ffdValue = 0x10;
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            autoDebugDumpSuppressed = false;
            tStatesUntilNextInterrupt = 0;
            realignInterruptPhaseAfterNextAccept = false;
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
        }

        private void Configure48kSnapshotCore(int borderColor, int targetFrameTStates)
        {
            // Standard 48K layout inside the current 128K machine model.
            PagedRamBank = 0;
            ScreenBank = 5;
            CurrentRomBank = 1; // Use the 48 BASIC ROM in your current setup.
            PagingLocked = true;
            BorderColor = borderColor & 0x07;
            frameTStates = targetFrameTStates;
            FrameCount = 0;
            floatingBusDisplayStartAdjustTStates = 0;
            floatingBusSampleAdjustTStates = 0;
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            autoDebugDumpSuppressed = false;
            tStatesUntilNextInterrupt = 0;
            realignInterruptPhaseAfterNextAccept = false;
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
        }

        public void Load48kSnapshotRam(byte[] ram48)
        {
            if (ram48 == null)
                throw new ArgumentNullException(nameof(ram48));
            if (ram48.Length != 48 * 1024)
                throw new ArgumentException("48K snapshot RAM must be exactly 49152 bytes.", nameof(ram48));

            // 0x4000-0x7FFF -> bank 5
            Buffer.BlockCopy(ram48, 0, ramBanks[5], 0, 0x4000);

            // 0x8000-0xBFFF -> bank 2
            Buffer.BlockCopy(ram48, 0x4000, ramBanks[2], 0, 0x4000);

            // 0xC000-0xFFFF -> bank 0 in 48K mode
            Buffer.BlockCopy(ram48, 0x8000, ramBanks[0], 0, 0x4000);
        }


        public void LoadRamBank(int bank, byte[] data)
        {
            if ((uint)bank >= ramBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length != 16 * 1024)
                throw new ArgumentException("RAM bank data must be exactly 16384 bytes.", nameof(data));

            Buffer.BlockCopy(data, 0, ramBanks[bank], 0, data.Length);
        }

        public void ConfigureFor128kSnapshot(byte last7ffdValue, int borderColor)
        {
            PagedRamBank = last7ffdValue & 0x07;
            ScreenBank = ((last7ffdValue & 0x08) != 0) ? 7 : 5;
            CurrentRomBank = ((last7ffdValue & 0x10) != 0) ? 1 : 0;
            PagingLocked = (last7ffdValue & 0x20) != 0;
            BorderColor = borderColor & 0x07;
            frameTStates = FrameTStates128;
            FrameCount = 0;
            floatingBusDisplayStartAdjustTStates = 0;
            floatingBusSampleAdjustTStates = 0;
            this.last7ffdValue = (byte)(last7ffdValue & 0x3F);
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            autoDebugDumpSuppressed = false;
            tStatesUntilNextInterrupt = 0;
            realignInterruptPhaseAfterNextAccept = false;
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
        }

        private void WritePort(ushort port, byte value)
        {
            RecordPortEvent($"T={cpu.TStates,10} OUT {port:X4}={value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
            SpeakerEdge = false;

            if ((port & 0x0001) == 0)
            {
                BorderColor = value & 0x07;
                micHigh = (value & 0x08) != 0;

                bool newSpeakerHigh = (value & 0x10) != 0;
                if (newSpeakerHigh != speakerHigh)
                {
                    speakerHigh = newSpeakerHigh;
                    SpeakerEdge = true;
                    RecordBeeperEvent(newSpeakerHigh);
                }

                HandleAyPortWrite(port, value);
                return;
            }

            HandleAyPortWrite(port, value);

            if ((port & 0x8002) == 0 && (port & 0x00FF) == 0xFD)
            {
                if (PagingLocked)
                    return;

                int oldRam = PagedRamBank;
                int oldScreen = ScreenBank;
                int oldRom = CurrentRomBank;

                PagedRamBank = value & 0x07;
                ScreenBank = ((value & 0x08) != 0) ? 7 : 5;
                CurrentRomBank = ((value & 0x10) != 0) ? 1 : 0;

                if ((value & 0x20) != 0)
                    PagingLocked = true;

                byte newPaging = (byte)(value & 0x3F);
                if (newPaging != last7ffdValue)
                {
                    last7ffdValue = newPaging;
                    Trace?.Invoke(
                        $"[7FFD] PC=0x{cpu.Regs.PC:X4} Frame={FrameCount} RAM {oldRam}->{PagedRamBank} SCREEN {oldScreen}->{ScreenBank} ROM {oldRom}->{CurrentRomBank} VAL=0x{value:X2}");
                }
            }
        }

        private void TriggerFrameInterrupt()
        {
            cpu.InterruptPending = true;
            interruptPulseEndTStates = cpu.TStates + 32UL;
        }

        private void BeginFrameAudioCapture()
        {
            frameStartTStates = cpu.TStates;
            frameStartSpeakerHigh = speakerHigh;
            frameStartAyState = captureAudioFramesEnabled ? ay.CaptureAudioState() : null;
            beeperEvents.Clear();
            ayWrites.Clear();
        }

        private void RecordBeeperEvent(bool newSpeakerHigh)
        {
            if (!captureAudioFramesEnabled)
                return;

            ulong elapsed = cpu.TStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)int.MaxValue, elapsed);
            beeperEvents.Add(new Audio.BeeperEvent(offset, newSpeakerHigh));
        }

        private void RecordAyWrite(byte register, byte value)
        {
            if (!captureAudioFramesEnabled)
                return;

            ulong elapsed = cpu.TStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)int.MaxValue, elapsed);
            ayWrites.Add(new Audio.AyRegisterWrite(offset, register, value));
        }
    }
}
