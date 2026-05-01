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
        public const int FrameTStates48Snapshot = 70908;
        public const int FrameTStates128 = 70908;
        public const int CpuClockHz48 = 3500000;
        public const int CpuClockHz128 = 3546900;
        // 48K snapshots do not encode the current frame phase. A short initial delay
        // moves Exolon onto the later failure path instead of the early FCxx trap.
        public const int Default48kSnapshotInitialInterruptDelay = 16;
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
        
        // AY-3-8912
        private readonly Audio.Ay8912 ay = new Audio.Ay8912();

        public Audio.Ay8912 Ay => ay;

        private byte lastAyRegister;
        private bool speakerHigh;
        private bool frameStartSpeakerHigh;
        private ulong frameStartTStates;
        private readonly List<Audio.BeeperEvent> beeperEvents = new List<Audio.BeeperEvent>();
        private readonly List<Audio.AyRegisterWrite> ayWrites = new List<Audio.AyRegisterWrite>();
        private Audio.AyAudioState? frameStartAyState;

        private const int DebugHistoryCapacity = 8192;
        private readonly Queue<string> recentMemoryEvents = new Queue<string>();
        private readonly Queue<string> recentPortEvents = new Queue<string>();
        private readonly Queue<string> watchedWrites = new Queue<string>();
        private bool autoDebugDumpPending;
        private string? autoDebugDumpReason;
        private string? autoDebugDumpSnapshot;
        private bool autoDebugDumpSuppressed;
        private bool interruptStallTrapArmed;
        private ushort lastLoopPc;
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
            ClearDebugHistory();
            tStatesUntilNextInterrupt = 0;
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
            speakerHigh = false;
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
            ClearDebugHistory();
            interruptStallTrapArmed = false;
            tStatesUntilNextInterrupt = 0;
        }

        public void ExecuteFrame()
        {
            BeginFrameAudioCapture();
            int remainingFrameTStates = frameTStates;

            while (remainingFrameTStates > 0)
            {
                if (tStatesUntilNextInterrupt == 0)
                {
                    TriggerFrameInterrupt();
                    tStatesUntilNextInterrupt = frameTStates;
                }

                int executionChunk = Math.Min(remainingFrameTStates, tStatesUntilNextInterrupt);
                cpu.ExecuteCycles((ulong)executionChunk);
                remainingFrameTStates -= executionChunk;
                tStatesUntilNextInterrupt -= executionChunk;
            }

            FrameCount++;
        }

        public Audio.AudioFrame DrainAudioFrame()
        {
            return new Audio.AudioFrame(
                frameTStates,
                CurrentCpuClockHz,
                frameStartSpeakerHigh,
                speakerHigh,
                beeperEvents,
                ay.CaptureAudioState(),
                frameStartAyState,
                ayWrites);
        }

        public void ClearDebugHistory()
        {
            recentMemoryEvents.Clear();
            recentPortEvents.Clear();
            watchedWrites.Clear();
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            autoDebugDumpSuppressed = false;
            lastLoopPc = 0;
            lastObservedPc = 0;
            interruptPulseEndTStates = null;
            cpu.ClearRecentTrace();
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
            sb.AppendLine();

            sb.AppendLine("=== CPU STATE ===");
            sb.AppendLine($"TStates={cpu.TStates}");
            sb.AppendLine($"PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}");
            sb.AppendLine($"I={cpu.Regs.I:X2} R={cpu.Regs.R:X2} IM={cpu.InterruptMode} IFF1={(cpu.IFF1 ? 1 : 0)} IFF2={(cpu.IFF2 ? 1 : 0)} HALT={(cpu.IsHalted ? 1 : 0)} INTP={(cpu.InterruptPending ? 1 : 0)}");
            sb.AppendLine();

            sb.AppendLine("=== STACK BYTES ===");
            AppendMemoryWindow(sb, cpu.Regs.SP, 32);
            AppendMemoryWindow(sb, 0x6C40, 32, "=== MEMORY 6C40 ===");
            AppendMemoryWindow(sb, 0xFED8, 16, "=== MEMORY FED8 ===");
            AppendMemoryWindow(sb, 0x78D8, 16, "=== MEMORY 78D8 ===");
            AppendMemoryWindow(sb, 0x5C3B, 64, "=== SYSTEM VARS 5C3B ===");
            AppendMemoryWindow(sb, 0xFF80, 128, "=== MEMORY FF80 ===");
            AppendMemoryWindow(sb, 0xF320, 32, "=== MEMORY F320 ===");
            AppendMemoryWindow(sb, 0xB1A0, 48, "=== MEMORY B1A0 ===");
            AppendMemoryWindow(sb, 0xBEE0, 32, "=== MEMORY BEE0 ===");
            AppendMemoryWindow(sb, 0x8038, 32, "=== MEMORY 8038 ===");
            AppendMemoryWindow(sb, 0xAA60, 32, "=== MEMORY AA60 ===");
            AppendMemoryWindow(sb, 0xBA10, 32, "=== MEMORY BA10 ===");
            AppendMemoryWindow(sb, 0xD350, 176, "=== MEMORY D350 ===");
            ushort iyWindowStart = cpu.Regs.IY >= 8 ? (ushort)(cpu.Regs.IY - 8) : (ushort)0;
            AppendMemoryWindow(sb, iyWindowStart, 32, $"=== MEMORY IY-8 ({iyWindowStart:X4}) ===");

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

            sb.AppendLine("=== WATCHED WRITES ===");
            foreach (string line in watchedWrites)
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
            recentMemoryEvents.Enqueue(line);
            while (recentMemoryEvents.Count > DebugHistoryCapacity)
                recentMemoryEvents.Dequeue();
        }

        private void RecordPortEvent(string line)
        {
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

        private void RecordMenuPortSample(ushort port, ulong sampleTStates, byte value)
        {
            if (frameTStates != FrameTStates48)
                return;

            if ((port & 0x00FF) != 0x00F7)
                return;

            ushort pc = cpu.Regs.PC;
            if (pc < 0xFFA8 || pc > 0xFFC0)
                return;

            ulong frameOffset = sampleTStates % (ulong)FrameTStates48;
            RecordPortEvent(
                $"T={cpu.TStates,10} IN {port:X4}={value:X2} sample={sampleTStates} frame={frameOffset} " +
                $"PC={pc:X4} SP={cpu.Regs.SP:X4} BC={cpu.Regs.BC:X4} HL={cpu.Regs.HL:X4}");
        }

        private void RecordWatchedWrite(ushort addr, byte value)
        {
            bool inPrimaryRange = addr >= 0x52ED && addr <= 0x52F9;
            bool inSecondaryRange = addr >= 0xC5DB && addr <= 0xC5FA;
            bool inLegacyRange =
                addr == 0x5C0C ||
                addr == 0x5C3B ||
                addr == 0x5C3D ||
                addr == 0x5C3E ||
                addr == 0x5C5D ||
                addr == 0x5C5E ||
                addr == 0x5C65 ||
                addr == 0x5C66 ||
                addr == 0x5C74;
            bool inStackWindow = addr >= 0x17C7 && addr <= 0x17E1;
            bool inLdSpSourceWindow = addr >= 0xFED8 && addr <= 0xFEDF;
            bool inTempStackSaveWindow = addr >= 0x78D8 && addr <= 0x78DF;
            bool inRuntimeStackWindow = addr >= 0x8038 && addr <= 0x8057;
            bool inBadReturnWindow = addr >= 0xAA60 && addr <= 0xAA7F;
            bool inRamJumpWindow = addr >= 0xBA10 && addr <= 0xBA2F;

            if (!inPrimaryRange &&
                !inSecondaryRange &&
                !inLegacyRange &&
                !inStackWindow &&
                !inLdSpSourceWindow &&
                !inTempStackSaveWindow &&
                !inRuntimeStackWindow &&
                !inBadReturnWindow &&
                !inRamJumpWindow)
                return;

            string line =
                $"T={cpu.TStates,10} WATCH {addr:X4} <- {value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} " +
                $"AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}";
            watchedWrites.Enqueue(line);
            while (watchedWrites.Count > DebugHistoryCapacity)
                watchedWrites.Dequeue();
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
        }

        public void DisableFocusedInstructionTrace()
        {
            focusedTraceStartPc = null;
            focusedTraceEndPc = null;
            focusedTraceStartFrame = 0;
            focusedTraceFrameLimit = 0;
            focusedTraceMaxEntries = 0;
            focusedInstructionTrace.Clear();
        }

        public void SetInitialInterruptDelay(int tStatesUntilNextInterrupt)
        {
            if (tStatesUntilNextInterrupt < 0 || tStatesUntilNextInterrupt > frameTStates)
                throw new ArgumentOutOfRangeException(nameof(tStatesUntilNextInterrupt));

            this.tStatesUntilNextInterrupt = tStatesUntilNextInterrupt;
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

        public byte[] GetRamBankCopy(int bank)
        {
            if ((uint)bank >= ramBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));

            return (byte[])ramBanks[bank].Clone();
        }

        public byte PeekMemory(ushort addr) => ReadMemory(addr);

        public void PokeMemory(ushort addr, byte value) => WriteMemory(addr, value);

        public byte DebugReadPort(ushort port) => ReadPort(port);

        public void DebugWritePort(ushort port, byte value) => WritePort(port, value);

        public void MountTape(MountedTape tape)
        {
            mountedTape = tape ?? throw new ArgumentNullException(nameof(tape));
            mountedTape.Reset();
        }

        public void EjectTape()
        {
            mountedTape = null;
        }

        public bool TryServiceTapeTrap()
        {
            return mountedTape != null && mountedTape.TryHandleRomLoadTrap(this, cpu);
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

            bool pastStartupWindow = z80.TStates >= (ulong)(frameTStates * 2);

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

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0xBA1C &&
                z80.Regs.SP >= 0x8000 &&
                z80.Regs.SP <= 0x80FF &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"Exolon entered BA1C RAM worker: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} " +
                    $"PrevPC={lastObservedPc:X4}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0xAA70 &&
                z80.Regs.SP >= 0x8000 &&
                z80.Regs.SP <= 0x80FF &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"Exolon entered AA70 late RAM path: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} " +
                    $"PrevPC={lastObservedPc:X4}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0x15DE &&
                z80.Regs.SP >= 0xFF40 &&
                z80.Regs.SP <= 0xFF60 &&
                lastObservedPc != 0x15DE &&
                lastObservedPc != 0x15E1 &&
                lastObservedPc != 0x15E2 &&
                lastObservedPc != 0x15E6 &&
                lastObservedPc != 0x15E7 &&
                lastObservedPc != 0x15E8 &&
                lastObservedPc != 0x15EB &&
                lastObservedPc != 0x15EC &&
                lastObservedPc != 0x15ED &&
                lastObservedPc != 0x15F7 &&
                lastObservedPc != 0x15F8 &&
                lastObservedPc != 0x15F9 &&
                lastObservedPc != 0x15FA &&
                lastObservedPc != 0x15FB &&
                lastObservedPc != 0x15FE &&
                lastObservedPc != 0x15FF &&
                lastObservedPc != 0x1600 &&
                lastObservedPc != 0x162C &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"Exolon entered ROM WAIT-KEY path: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} " +
                    $"PrevPC={lastObservedPc:X4}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0x051E &&
                z80.Regs.SP >= 0x3800 &&
                z80.Regs.SP <= 0x38FF &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"ROM 051E output loop reached: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} " +
                    $"PrevPC={lastObservedPc:X4}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0x24FB &&
                ReadMemory(0x5C5D) == 0xFF &&
                ReadMemory(0x5C5E) == 0xFF &&
                z80.Regs.IY == 0xB331 &&
                z80.Regs.SP >= 0x7B00 &&
                z80.Regs.SP <= 0x7CFF &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"Parser entered RST 18 with CH_ADD=FFFF: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} " +
                    $"PrevPC={lastObservedPc:X4} " +
                    $"24FB={ReadMemory(0x24FB):X2} 24FC={ReadMemory(0x24FC):X2} 24FD={ReadMemory(0x24FD):X2} " +
                    $"24FE={ReadMemory(0x24FE):X2} 24FF={ReadMemory(0x24FF):X2} 2500={ReadMemory(0x2500):X2} " +
                    $"0018={ReadMemory(0x0018):X2} 0019={ReadMemory(0x0019):X2} 001A={ReadMemory(0x001A):X2} " +
                    $"001B={ReadMemory(0x001B):X2} 001C={ReadMemory(0x001C):X2} 001D={ReadMemory(0x001D):X2} " +
                    $"5C3B={ReadMemory(0x5C3B):X2} 5C5D={ReadMemory(0x5C5D):X2} 5C5E={ReadMemory(0x5C5E):X2} " +
                    $"5C65={ReadMemory(0x5C65):X2} 5C66={ReadMemory(0x5C66):X2} 5C71={ReadMemory(0x5C71):X2}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0x26D7 &&
                z80.Regs.IY == 0xB331 &&
                z80.Regs.SP >= 0x7B00 &&
                z80.Regs.SP <= 0x7CFF &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"Game entered ROM error branch: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} " +
                    $"PrevPC={lastObservedPc:X4} " +
                    $"26D3={ReadMemory(0x26D3):X2} 26D4={ReadMemory(0x26D4):X2} 26D5={ReadMemory(0x26D5):X2} " +
                    $"26D6={ReadMemory(0x26D6):X2} 26D7={ReadMemory(0x26D7):X2} 26D8={ReadMemory(0x26D8):X2} " +
                    $"26D9={ReadMemory(0x26D9):X2} " +
                    $"26DA={ReadMemory(0x26DA):X2} 26DB={ReadMemory(0x26DB):X2} 26DC={ReadMemory(0x26DC):X2} " +
                    $"26DD={ReadMemory(0x26DD):X2} 26DE={ReadMemory(0x26DE):X2} 26DF={ReadMemory(0x26DF):X2} " +
                    $"B4={ReadMemory(0x33B4):X2} B5={ReadMemory(0x33B5):X2} B6={ReadMemory(0x33B6):X2} " +
                    $"B7={ReadMemory(0x33B7):X2} " +
                    $"B8={ReadMemory(0x33B8):X2} B9={ReadMemory(0x33B9):X2} BA={ReadMemory(0x33BA):X2} " +
                    $"BB={ReadMemory(0x33BB):X2} BC={ReadMemory(0x33BC):X2} BD={ReadMemory(0x33BD):X2} " +
                    $"BE={ReadMemory(0x33BE):X2} BF={ReadMemory(0x33BF):X2} C0={ReadMemory(0x33C0):X2} " +
                    $"C1={ReadMemory(0x33C1):X2} C2={ReadMemory(0x33C2):X2} C3={ReadMemory(0x33C3):X2} " +
                    $"A9={ReadMemory(0x33A9):X2} AA={ReadMemory(0x33AA):X2} AB={ReadMemory(0x33AB):X2} " +
                    $"SP0={ReadMemory(z80.Regs.SP):X2} SP1={ReadMemory((ushort)(z80.Regs.SP + 1)):X2} " +
                    $"SP2={ReadMemory((ushort)(z80.Regs.SP + 2)):X2} SP3={ReadMemory((ushort)(z80.Regs.SP + 3)):X2}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0x0058 &&
                z80.Regs.IY == 0xB331 &&
                z80.Regs.SP >= 0x7B00 &&
                z80.Regs.SP <= 0x7CFF &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"ROM error handler entered: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0x11B6 &&
                ReadMemory(z80.Regs.SP) == 0xFF &&
                ReadMemory((ushort)(z80.Regs.SP + 1)) == 0xFF &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"ROM RET to FFFF armed: PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} " +
                    $"AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)}");
            }

            if (interruptStallTrapArmed && !autoDebugDumpPending && !autoDebugDumpSuppressed)
            {
                bool inLoopWindow = z80.Regs.PC >= 0xB8CD && z80.Regs.PC <= 0xC217;
                bool stalledInterruptState = !z80.IFF1 && z80.InterruptPending;

                if (inLoopWindow && stalledInterruptState)
                {
                    if (z80.TStates - z80.LastInterruptProgressTStates >= 100000UL)
                    {
                        interruptStallTrapArmed = false;
                        RequestAutoDebugDump(
                            $"Exolon interrupt-stall trap: IFF1=0 and INTP=1 with no INT_ACCEPT/EI_EFFECT for {z80.TStates - z80.LastInterruptProgressTStates} T-states. LastPC={lastLoopPc:X4} CurrentPC={z80.Regs.PC:X4}");
                    }
                }

                lastLoopPc = z80.Regs.PC;
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed &&
                z80.TStates - z80.LastInterruptProgressTStates >= 200000UL)
            {
                RequestAutoDebugDump(
                    $"General interrupt-progress stall: no INT_ACCEPT/EI_EFFECT/DI/EI progress for {z80.TStates - z80.LastInterruptProgressTStates} T-states. " +
                    $"PC={z80.Regs.PC:X4} SP={z80.Regs.SP:X4} AF={z80.Regs.AF:X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} INTP={(z80.InterruptPending ? 1 : 0)} PrevPC={lastObservedPc:X4}");
            }

            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                z80.Regs.PC == 0xFFA1)
            {
                ulong frameOffset = z80.TStates % (ulong)FrameTStates48;
                RecordPortEvent(
                    $"T={z80.TStates,10} MENU_COMPARE A={z80.Regs.A:X2} H={z80.Regs.H:X2} C={z80.Regs.C:X2} " +
                    $"frame={frameOffset} SP={z80.Regs.SP:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4}");
            }

            lastObservedPc = z80.Regs.PC;

            return mountedTape != null && mountedTape.TryHandleRomLoadTrap(this, z80);
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

            byte oldValue = ReadMemory(addr);

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
            RecordWatchedWrite(addr, value);

            bool pastStartupWindow = cpu.TStates >= (ulong)(frameTStates * 2);
            if (frameTStates == FrameTStates48 &&
                pastStartupWindow &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                if (PeekMemory(0x804E) == 0xDC &&
                    PeekMemory(0x804F) == 0xA9 &&
                    cpu.Regs.SP >= 0x8040 &&
                    cpu.Regs.SP <= 0x8052)
                {
                    RequestAutoDebugDump(
                        $"Exolon staged A9DC return on live stack: writer={addr:X4} value={value:X2} " +
                        $"PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} " +
                        $"DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}");
                }
                else
                if (addr == 0x5C3B && value != oldValue)
                {
                    RequestAutoDebugDump(
                        $"System var 5C3B changed: {oldValue:X2}->{value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} " +
                        $"AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} " +
                        $"IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}");
                }
                else if ((addr == 0x5C65 || addr == 0x5C66) && value != oldValue)
                {
                    RequestAutoDebugDump(
                        $"System var {addr:X4} changed: {oldValue:X2}->{value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} " +
                        $"AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} " +
                        $"IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}");
                }
                else if ((addr == 0x5C5D || addr == 0x5C5E) &&
                    oldValue == 0xFF &&
                    value != oldValue &&
                    cpu.Regs.PC != 0x007B)
                {
                    RequestAutoDebugDump(
                        $"System var {addr:X4} changed from FF: {oldValue:X2}->{value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} " +
                        $"AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4} " +
                        $"IX={cpu.Regs.IX:X4} IY={cpu.Regs.IY:X4}");
                }
            }

            if (frameTStates == FrameTStates48 &&
                addr >= 0xFFC0 &&
                cpu.Regs.PC >= 0x11DC &&
                cpu.Regs.PC <= 0x11E0 &&
                !autoDebugDumpPending &&
                !autoDebugDumpSuppressed)
            {
                RequestAutoDebugDump(
                    $"ROM fill loop wrote high RAM: addr={addr:X4} value={value:X2} " +
                    $"PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4} AF={cpu.Regs.AF:X4} BC={cpu.Regs.BC:X4} " +
                    $"DE={cpu.Regs.DE:X4} HL={cpu.Regs.HL:X4}");
            }
        }

        public byte ReadPort(ushort port)
        {
            if ((port & 0x0001) == 0)
            {
                byte result = 0xFF;
                byte high = (byte)(port >> 8);

                for (int row = 0; row < 8; row++)
                {
                    if ((high & (1 << row)) == 0)
                        result &= keyboardMatrix[row];
                }

                bool earHigh = mountedTape?.ReadEarBit(cpu.TStates) ?? true;
                if (earHigh)
                    result |= 0x40;
                else
                    result = (byte)(result & ~0x40);
                return result;
            }

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
                return ReadPort(port);

            if (frameTStates == FrameTStates48)
            {
                long adjustedSampleTStates = (long)cpu.TStates + sampleOffsetTStates + floatingBusSampleAdjustTStates;
                ulong sampleTStates = adjustedSampleTStates > 0 ? (ulong)adjustedSampleTStates : 0UL;
                byte value = ReadFloatingBus48(sampleTStates);
                RecordMenuPortSample(port, sampleTStates, value);
                return value;
            }

            return 0xFF;
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
            // Standard 48K layout inside the current 128K machine model.
            PagedRamBank = 0;
            ScreenBank = 5;
            CurrentRomBank = 1; // Use the 48 BASIC ROM in your current setup.
            PagingLocked = true;
            BorderColor = borderColor & 0x07;
            frameTStates = FrameTStates48;
            FrameCount = 0;
            floatingBusDisplayStartAdjustTStates = 0;
            floatingBusSampleAdjustTStates = 0;
            interruptStallTrapArmed = true;
            lastLoopPc = 0;
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            autoDebugDumpSuppressed = false;
            tStatesUntilNextInterrupt = Default48kSnapshotInitialInterruptDelay;
        }

        public void ConfigureFor48kTapeLoad(int borderColor)
        {
            ConfigureFor48kSnapshot(borderColor);
            frameTStates = FrameTStates48;
            tStatesUntilNextInterrupt = 0;
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
            interruptStallTrapArmed = false;
            lastLoopPc = 0;
            autoDebugDumpPending = false;
            autoDebugDumpReason = null;
            autoDebugDumpSnapshot = null;
            autoDebugDumpSuppressed = false;
            tStatesUntilNextInterrupt = 0;
        }

        private void WritePort(ushort port, byte value)
        {
            RecordPortEvent($"T={cpu.TStates,10} OUT {port:X4}={value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
            SpeakerEdge = false;

            if ((port & 0x0001) == 0)
            {
                BorderColor = value & 0x07;

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
            frameStartAyState = ay.CaptureAudioState();
            beeperEvents.Clear();
            ayWrites.Clear();
        }

        private void RecordBeeperEvent(bool newSpeakerHigh)
        {
            ulong elapsed = cpu.TStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)frameTStates, elapsed);
            beeperEvents.Add(new Audio.BeeperEvent(offset, newSpeakerHigh));
        }

        private void RecordAyWrite(byte register, byte value)
        {
            ulong elapsed = cpu.TStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)frameTStates, elapsed);
            ayWrites.Add(new Audio.AyRegisterWrite(offset, register, value));
        }
    }
}
