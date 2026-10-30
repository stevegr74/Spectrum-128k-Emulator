using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Spectrum128kEmulator.Tap;
using Spectrum128kEmulator.Input;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed partial class Spectrum128Machine
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
        public const int ActiveDisplayWidth = 256;
        public const int ActiveDisplayHeight = 192;
        public const int BorderLeftWidth = 32;
        public const int BorderTopHeight = 24;
        public const int ScreenWidth = ActiveDisplayWidth + (BorderLeftWidth * 2);
        public const int ScreenHeight = ActiveDisplayHeight + (BorderTopHeight * 2);
        private const int TapeLoaderActivityReadsPerFrame = 64;
        private const int TapeLoaderHandoffFramesBeforeStop = 3;
        private const int TapeLoaderSearchReadsBeforeAutomaticResume = 64;

        private readonly Z80Cpu cpu = new Z80Cpu();
        private readonly byte[][] ramBanks = new byte[8][];
        private readonly byte[][] romBanks = new byte[2][];
        private readonly byte[] keyboardMatrix = new byte[8]
        {
            0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF
        };
        private readonly ulong[] keyboardRowScanCounts = new ulong[8];
        private readonly SpectrumJoystickState[] joystickStates =
            new SpectrumJoystickState[Enum.GetValues<SpectrumJoystickInterface>().Length];
        private readonly bool[] activeJoystickInterfaces =
            new bool[Enum.GetValues<SpectrumJoystickInterface>().Length];
        private readonly TapeActivityTracker tapeActivity = new TapeActivityTracker();
        private readonly TapeTransportController tapeTransport = new TapeTransportController();
        
        // AY-3-8912
        private readonly Audio.Ay8912 ay = new Audio.Ay8912();

        public Audio.Ay8912 Ay => ay;

        public bool HasAudibleOutput
        {
            get
            {
                if (speakerHigh || beeperEvents.Count != 0)
                    return true;

                if (!IsAyHardwareEnabled)
                    return false;

                byte mixer = ay.ReadRegister(7);
                for (int channel = 0; channel < 3; channel++)
                {
                    byte volume = ay.ReadRegister((byte)(8 + channel));
                    bool channelHasVolume = (volume & 0x1F) != 0;
                    bool toneEnabled = (mixer & (1 << channel)) == 0;
                    bool noiseEnabled = (mixer & (1 << (channel + 3))) == 0;
                    if (channelHasVolume && (toneEnabled || noiseEnabled))
                        return true;
                }

                return false;
            }
        }

        private byte lastAyRegister;
        private bool speakerHigh;
        private bool micHigh;
        private bool frameStartSpeakerHigh;
        private ulong frameStartTStates;
        private int frameStartBorderColor;
        private int currentFrameExecutedTStates;
        private int lastAudioFrameTStates = FrameTStates128;
        private readonly List<Audio.BeeperEvent> beeperEvents = new List<Audio.BeeperEvent>();
        private readonly List<Audio.AyRegisterWrite> ayWrites = new List<Audio.AyRegisterWrite>();
        private readonly List<BorderEvent> borderEvents = new List<BorderEvent>();
        private BorderFrame lastCompletedBorderFrame = new BorderFrame(FrameTStates128, 1, Array.Empty<BorderEvent>());
        private readonly UlaRasterCapture ulaRaster;
        private Audio.AyAudioState? frameStartAyState;
        private readonly Queue<Audio.AudioFrame> completedAudioFrames = new Queue<Audio.AudioFrame>();
        private bool captureAudioFramesEnabled = true;
        private bool currentAudioFrameCaptureValid = true;

        private ushort lastObservedPc;
        private ulong? interruptPulseEndTStates;
        private int frameTStates = FrameTStates128;
        private bool uses48kMemoryMap;
        private int tStatesUntilNextInterrupt;
        private bool realignInterruptPhaseAfterNextAccept;
        private readonly MountedLoadContinuationController mountedLoadContinuation;

        internal Func<Spectrum128Machine, ushort?>? PendingMountedLoadResolver
        {
            get => mountedLoadContinuation.Resolver;
            set => mountedLoadContinuation.Resolver = value;
        }

        public bool SpeakerHigh => speakerHigh;
        public bool SpeakerEdge { get; private set; }
        private bool IsAyHardwareEnabled => MachineModel == SpectrumMachineModel.Spectrum128K;

        private void HandleAyPortWrite(ushort port, byte value, ulong eventTStates)
        {
            if (!IsAyHardwareEnabled)
                return;

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
                RecordAyWrite(lastAyRegister, value, eventTStates);
            }
        }

        private byte last7ffdValue = 0xFF;
        private RzxPlaybackSession? rzxPlayback;
        private MountedTape? mountedTape => tapeTransport.MountedTape;
        public MountedTape? MountedTape => tapeTransport.MountedTape;
        public TapeTransportState TapeTransportState => tapeTransport.State;
        public TapeTransportTransition LastTapeTransportTransition => tapeTransport.LastTransition;
        public bool IsTapeStoppedAtMarker => tapeTransport.IsStoppedAtMarker;

        public Spectrum128Machine(string romFolder)
            : this(romFolder, romProfileOverride: null)
        {
        }

        internal Spectrum128Machine(string romFolder, SpectrumRomProfile? romProfileOverride)
        {
            if (string.IsNullOrWhiteSpace(romFolder))
                throw new ArgumentException("ROM folder must be provided.", nameof(romFolder));

            for (int i = 0; i < ramBanks.Length; i++)
                ramBanks[i] = new byte[16384];

            for (int i = 0; i < romBanks.Length; i++)
                romBanks[i] = new byte[16384];

            LoadRoms(romFolder);
            if (romProfileOverride != null)
                RomProfile = romProfileOverride;
            mountedLoadContinuation = new MountedLoadContinuationController(this);
            InitializeScreenRam();
            ClearKeyboard();
            ulaRaster = new UlaRasterCapture(
                TimingProfile,
                () => ScreenBank,
                () => BorderColor,
                (bank, offset) => ramBanks[bank][offset]);

            cpu.ReadMemory = ReadMemoryWithContention;
            cpu.ReadMemoryTimed = ReadMemoryWithContentionAtOffset;
            cpu.WriteMemory = WriteMemoryWithContention;
            cpu.WriteMemoryTimed = WriteMemoryWithContentionAtOffset;
            cpu.ReadPort = ReadPortWithContention;
            cpu.ReadPortTimed = ReadPortTimed;
            cpu.WritePort = WritePortWithContention;
            cpu.BeforeInstruction = HandleBeforeInstruction;
            cpu.Reset();
            frameStartTStates = cpu.TStates;
            frameStartSpeakerHigh = speakerHigh;
            frameStartAyState = CaptureAyAudioState();
            currentAudioFrameCaptureValid = captureAudioFramesEnabled;
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
        public int CurrentCpuClockHz => TimingProfile.CpuClockHz;
        public SpectrumMachineModel MachineModel { get; private set; } = SpectrumMachineModel.Spectrum128K;
        public SpectrumRomProfile RomProfile { get; private set; } = null!;
        public bool SupportsRomTapeAcceleration => RomProfile.SupportsAddressBasedTapeServices;
        public bool FlashPhase => ((FrameCount / 16) & 1) != 0;
        public BorderFrame LastCompletedBorderFrame => lastCompletedBorderFrame;
        public UlaFrame? LastCompletedUlaFrame => ulaRaster.LastCompletedFrame;
        private UlaTimingProfile TimingProfile => UlaTimingProfile.ForFrameLength(frameTStates);

        public bool HasMountedTape => mountedTape != null;
        public string? MountedTapeName => mountedTape?.DisplayName;
        public bool HasRzxPlayback => rzxPlayback != null;
        public string? RzxPlaybackName => rzxPlayback?.DisplayName;
        public bool HasPendingMountedLoadUsrContinuation =>
            mountedLoadContinuation.HasPending;

        public void Reset()
        {
            Reset(SpectrumMachineModel.Spectrum128K);
        }

        public void Reset(SpectrumMachineModel model)
        {
            MachineModel = model;
            PagedRamBank = 0;
            CurrentRomBank = model == SpectrumMachineModel.Spectrum48K ? 1 : 0;
            PagingLocked = model == SpectrumMachineModel.Spectrum48K;
            uses48kMemoryMap = model == SpectrumMachineModel.Spectrum48K;
            ScreenBank = 5;
            BorderColor = 1;
            FrameCount = 0;
            LastAboveWriteFrame = -1;
            last7ffdValue = model == SpectrumMachineModel.Spectrum48K ? (byte)0x10 : (byte)0xFF;
            tapeTransport.Reset();
            ResetMountedTapeActivityTracking();
            rzxPlayback = null;
            mountedLoadContinuation.Clear();
            speakerHigh = false;
            micHigh = false;
            SpeakerEdge = false;
            frameTStates = model == SpectrumMachineModel.Spectrum48K ? FrameTStates48 : FrameTStates128;
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
            frameStartBorderColor = BorderColor;
            frameStartSpeakerHigh = speakerHigh;
            frameStartAyState = CaptureAyAudioState();
            currentAudioFrameCaptureValid = captureAudioFramesEnabled;
            beeperEvents.Clear();
            ayWrites.Clear();
            borderEvents.Clear();
            lastCompletedBorderFrame = new BorderFrame(frameTStates, BorderColor, Array.Empty<BorderEvent>());
            ulaRaster.Reset(TimingProfile);
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
            ulaRaster.AdvanceTo(cpu.TStates);
            TriggerFrameInterrupt();
            lastAudioFrameTStates = (int)Math.Min((ulong)int.MaxValue, cpu.TStates - frameStartTStates);
            if (captureAudioFramesEnabled && currentAudioFrameCaptureValid)
            {
                completedAudioFrames.Enqueue(new Audio.AudioFrame(
                    lastAudioFrameTStates,
                    CurrentCpuClockHz,
                    frameStartSpeakerHigh,
                    speakerHigh,
                    beeperEvents,
                    CaptureAyAudioState(),
                    frameStartAyState,
                    GetAyWritesForAudioFrame()));
            }
            currentFrameExecutedTStates = 0;
            CompleteFrameBorderCapture();
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
                CaptureAyAudioState(),
                frameStartAyState,
                GetAyWritesForAudioFrame());
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
                if (tStatesUntilNextInterrupt <= 0)
                {
                    TriggerFrameInterrupt();
                    if (realignInterruptPhaseAfterNextAccept)
                        tStatesUntilNextInterrupt = remainingFrameTStates;
                    else
                        tStatesUntilNextInterrupt += frameTStates;
                    realignInterruptPhaseAfterNextAccept = false;
                }

                int executionChunk = Math.Min(tStatesBudget, Math.Min(remainingFrameTStates, tStatesUntilNextInterrupt));
                ulong tStatesBefore = cpu.TStates;
                cpu.ExecuteCycles((ulong)executionChunk);
                ulaRaster.AdvanceTo(cpu.TStates);
                int actualExecutedTStates = (int)Math.Min((ulong)int.MaxValue, cpu.TStates - tStatesBefore);
                executedTStates += actualExecutedTStates;
                MountedTape? activeTape = mountedTape;
                activeTape?.AdvanceToTime(cpu.TStates);
                bool tapeHasEnded = activeTape?.HasCompletedPlayback == true ||
                    (activeTape?.IsExhaustedAndIdle == true && !HasPendingMountedLoadUsrContinuation);
                if (tapeHasEnded && ReferenceEquals(mountedTape, activeTape))
                    tapeTransport.CompleteIfCurrent(activeTape);
                else if (ReferenceEquals(mountedTape, activeTape) && activeTape != null)
                    tapeTransport.SynchronizeState(activeTape);

                tStatesBudget -= actualExecutedTStates;
                if (tStatesBudget < 0)
                    tStatesBudget = 0;

                remainingFrameTStates -= actualExecutedTStates;
                currentFrameExecutedTStates += actualExecutedTStates;
                tStatesUntilNextInterrupt -= actualExecutedTStates;

                if (remainingFrameTStates > 0)
                {
                    // A diagnostic client may stop before the next instruction. Preserve
                    // any cycles already executed, but do not spin on an unchanged clock.
                    if (cpu.ExecutionStopped || actualExecutedTStates == 0)
                        break;
                    continue;
                }

                lastAudioFrameTStates = (int)Math.Min((ulong)int.MaxValue, cpu.TStates - frameStartTStates);
                if (captureAudioFramesEnabled && currentAudioFrameCaptureValid)
                {
                    completedAudioFrames.Enqueue(new Audio.AudioFrame(
                        lastAudioFrameTStates,
                        CurrentCpuClockHz,
                        frameStartSpeakerHigh,
                        speakerHigh,
                        beeperEvents,
                        CaptureAyAudioState(),
                        frameStartAyState,
                        GetAyWritesForAudioFrame()));
                }
                else
                {
                    beeperEvents.Clear();
                    ayWrites.Clear();
                    frameStartAyState = null;
                }

                currentFrameExecutedTStates = 0;
                CompleteFrameBorderCapture();
                FrameCount++;
                UpdateAutomaticTapeStopAtFrameBoundary();
                completedFrames++;

                if (cpu.ExecutionStopped)
                    break;
            }

            return completedFrames;
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
                currentAudioFrameCaptureValid = false;
            }
        }

        private ulong GetUlaContentionDelay(ulong tStates)
        {
            UlaTimingProfile timing = TimingProfile;

            ulong frameOffset = tStates % (ulong)timing.FrameTStates;
            if (frameOffset < (ulong)timing.ContentionStartTState)
                return 0;

            ulong displayOffset = frameOffset - (ulong)timing.ContentionStartTState;
            if (displayOffset >= (ulong)(timing.LineTStates * timing.DisplayLineCount))
                return 0;

            int lineTState = (int)(displayOffset % (ulong)timing.LineTStates);
            if (lineTState >= timing.DisplayFetchTStatesPerLine)
                return 0;

            int phase = lineTState & 0x07;
            return phase < 6 ? (ulong)(6 - phase) : 0;
        }

        private bool Is48kContendedMemoryAddress(ushort addr)
        {
            if (addr >= 0x4000 && addr < 0x8000)
                return true;

            return !uses48kMemoryMap && addr >= 0xC000 && (PagedRamBank & 0x01) != 0;
        }

        private void Apply48kMemoryContention(ushort addr)
        {
            if (!Is48kContendedMemoryAddress(addr))
                return;

            ulong delay = GetUlaContentionDelay(cpu.TStates);
            if (delay != 0)
                cpu.AddTStates(delay);
        }

        private ulong GetIoContentionDelay(ushort port, int ioCycleStartOffsetTStates = 0)
        {
            bool highContended = Is48kContendedMemoryAddress((ushort)(port & 0xFF00));
            bool lowBitClear = (port & 0x0001) == 0;

            if (!highContended && !lowBitClear)
                return 0;

            // CPU instruction handlers account for their documented duration as a
            // single total.  Port contention starts at the I/O cycle, not at the
            // instruction's opcode-fetch boundary.
            ulong currentTStates = cpu.TStates + (ulong)Math.Max(0, ioCycleStartOffsetTStates);
            ulong totalDelay = 0;

            void ApplyContendedSegment(int advanceTStates)
            {
                ulong delay = GetUlaContentionDelay(currentTStates);
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

        private byte ReadMemoryWithContentionAtOffset(ushort addr, int offsetTStates)
        {
            if (Is48kContendedMemoryAddress(addr))
            {
                ulong delay = GetUlaContentionDelay(
                    cpu.TStates + (ulong)Math.Max(0, offsetTStates));
                if (delay != 0)
                    cpu.AddTStates(delay);
            }

            return ReadMemory(addr);
        }

        private void WriteMemoryWithContention(ushort addr, byte value)
        {
            Apply48kMemoryContention(addr);
            const ulong memoryWriteCycleTStates = 3;
            AdvanceUlaBeforeScreenWrite(addr, cpu.TStates + memoryWriteCycleTStates);
            WriteMemory(addr, value);
        }

        private void WriteMemoryWithContentionAtOffset(ushort addr, byte value, int offsetTStates)
        {
            int safeOffsetTStates = Math.Max(0, offsetTStates);
            if (Is48kContendedMemoryAddress(addr))
            {
                ulong delay = GetUlaContentionDelay(
                    cpu.TStates + (ulong)safeOffsetTStates);
                if (delay != 0)
                    cpu.AddTStates(delay);
            }

            const ulong memoryWriteCycleTStates = 3;
            AdvanceUlaBeforeScreenWrite(
                addr,
                cpu.TStates + (ulong)safeOffsetTStates + memoryWriteCycleTStates);
            WriteMemory(addr, value);
        }

        private void AdvanceUlaBeforeScreenWrite(ushort addr, ulong writeTStates)
        {
            if (addr < 0x4000)
                return;

            int bank = GetMappedRamBank(addr);
            int offset = addr & 0x3FFF;
            if ((bank == 5 || bank == 7) && offset < 0x1B00)
                ulaRaster.AdvanceTo(writeTStates);
        }

        private byte ReadPortWithContention(ushort port)
        {
            ulong delay = GetIoContentionDelay(port);
            if (delay != 0)
                cpu.AddTStates(delay);

            return ReadPort(port);
        }

        private void WritePortWithContention(ushort port, byte value)
        {
            ulong delay = GetIoContentionDelay(port);
            if (delay != 0)
                cpu.AddTStates(delay);

            const ulong portCycleTStates = 4;
            WritePort(port, value, cpu.TStates + portCycleTStates);
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

        public void SetJoystickState(SpectrumJoystickInterface joystickInterface, SpectrumJoystickState state)
        {
            if (joystickInterface == SpectrumJoystickInterface.None)
                return;
            activeJoystickInterfaces[(int)joystickInterface] = true;
            joystickStates[(int)joystickInterface] = state & SpectrumJoystickState.All;
        }

        public SpectrumJoystickState GetJoystickState(SpectrumJoystickInterface joystickInterface) =>
            joystickInterface == SpectrumJoystickInterface.None
                ? SpectrumJoystickState.None
                : joystickStates[(int)joystickInterface];

        public bool IsJoystickInterfaceActive(SpectrumJoystickInterface joystickInterface) =>
            joystickInterface != SpectrumJoystickInterface.None && activeJoystickInterfaces[(int)joystickInterface];

        public void ClearJoystick()
        {
            Array.Clear(joystickStates);
            Array.Clear(activeJoystickInterfaces);
        }

        public byte[] GetKeyboardMatrixCopy() => (byte[])keyboardMatrix.Clone();

        public ulong GetKeyboardRowScanCount(int row)
        {
            if ((uint)row >= keyboardRowScanCounts.Length)
                throw new ArgumentOutOfRangeException(nameof(row));

            return keyboardRowScanCounts[row];
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

        public void RestoreSnapshotInterruptPulse(byte remainingTStates)
        {
            if (remainingTStates > 48)
                throw new ArgumentOutOfRangeException(nameof(remainingTStates));

            cpu.InterruptPending = remainingTStates != 0;
            interruptPulseEndTStates = remainingTStates == 0
                ? null
                : cpu.TStates + remainingTStates;
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

        public byte PeekRamBank(int bank, ushort offset)
        {
            if ((uint)bank >= ramBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));
            if (offset >= 0x4000)
                throw new ArgumentOutOfRangeException(nameof(offset));

            return ramBanks[bank][offset];
        }

        public void PokeRamBank(int bank, ushort offset, byte value)
        {
            if ((uint)bank >= ramBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));
            if (offset >= 0x4000)
                throw new ArgumentOutOfRangeException(nameof(offset));

            ramBanks[bank][offset] = value;
        }

        public byte[] GetRomBankCopy(int bank)
        {
            if ((uint)bank >= romBanks.Length)
                throw new ArgumentOutOfRangeException(nameof(bank));

            return (byte[])romBanks[bank].Clone();
        }

        public byte PeekMemory(ushort addr) => ReadMemory(addr);

        public void PokeMemory(ushort addr, byte value) => WriteMemory(addr, value);

        public byte DebugReadPort(ushort port) => ReadPort(port);
        public string GetMountedTapeDebugState() => mountedTape?.DebugPlaybackState ?? "(none)";

        public void DebugWritePort(ushort port, byte value) => WritePort(port, value);

        public void MountTape(MountedTape tape)
        {
            tapeTransport.Mount(tape, CurrentCpuClockHz);
            ResetMountedTapeActivityTracking();
        }

        public TapeTransportState ToggleTapeTransport()
        {
            if (mountedTape == null)
                return TapeTransportState;

            TapeTransportState state = tapeTransport.Toggle(cpu.TStates);
            ResetMountedTapeActivityTracking();
            return state;
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
            tapeTransport.Eject();
            ResetMountedTapeActivityTracking();
        }

        public bool TryServiceTapeTrap()
        {
            return TryHandleMountedTapeTrap(cpu);
        }

        public BootstrapTapeLoadResult TryConsumeBootstrapTapeLoad(ushort? codeLoadAddress = null)
        {
            return mountedTape != null
                ? mountedTape.TryConsumeBootstrapLoad(this, codeLoadAddress)
                : BootstrapTapeLoadResult.None;
        }

        public void SetPendingMountedLoadUsrContinuation(ushort entryPoint)
        {
            mountedLoadContinuation.SetEntryPoint(entryPoint);
        }

        public void SetPendingMountedLoadUsrContinuationResolver(
            Func<Spectrum128Machine, ushort?> resolver,
            bool requireUsrReturnAddress = false)
        {
            mountedLoadContinuation.SetResolver(resolver, requireUsrReturnAddress);
        }

        public void SetPendingMountedLoadDirectUsrContextPolicy(bool preserveLiveInterpreterState)
        {
            mountedLoadContinuation.SetDirectUsrContextPolicy(preserveLiveInterpreterState);
        }

        internal bool PendingMountedLoadUsrContinuationRequiresUsrReturnAddress =>
            mountedLoadContinuation.RequiresUsrReturnAddress;

        public void ClearPendingMountedLoadUsrContinuation()
        {
            mountedLoadContinuation.Clear();
        }

        public void RefreshPendingMountedLoadInterpreterContext(bool forceVariableAreaRefresh = false)
        {
            mountedLoadContinuation.RefreshInterpreterContext(forceVariableAreaRefresh);
        }

        public void SetPendingMountedLoadBasicResume(ushort lineNumber, byte statementIndex)
        {
            mountedLoadContinuation.SetBasicResume(lineNumber, statementIndex);
        }

        public void SetPendingMountedLoadResumeCursorOverride(
            ushort? kCur = null,
            ushort? chAdd = null,
            ushort? xPtr = null)
        {
            mountedLoadContinuation.SetResumeCursorOverride(kCur, chAdd, xPtr);
        }

        internal bool TryGetPendingMountedLoadBasicVariableArea(out ushort vars, out ushort eLine)
        {
            return mountedLoadContinuation.TryGetBasicVariableArea(out vars, out eLine);
        }

        internal bool TryGetPendingMountedLoadBasicVariableSnapshot(out ushort vars, out byte[] data)
        {
            return mountedLoadContinuation.TryGetBasicVariableSnapshot(out vars, out data);
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

            if (focusedTraceStopPc.HasValue && z80.Regs.PC == focusedTraceStopPc.Value)
            {
                // Preserve the lead-in to a known branch without allowing its
                // tight loop to evict the diagnostic history.
                focusedTraceStartPc = null;
                focusedTraceEndPc = null;
                focusedTraceStopPc = null;
                cpu.SetInstructionTraceCaptureEnabled(false);
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
                    $"SP={z80.Regs.SP:X4} AF={z80.Regs.AF:X4} AF'={((z80.Regs.A_ << 8) | z80.Regs.F_):X4} BC={z80.Regs.BC:X4} DE={z80.Regs.DE:X4} HL={z80.Regs.HL:X4} " +
                    $"IX={z80.Regs.IX:X4} IY={z80.Regs.IY:X4} I={z80.Regs.I:X2} R={z80.Regs.R:X2} " +
                    $"IFF1={(z80.IFF1 ? 1 : 0)} IFF2={(z80.IFF2 ? 1 : 0)} INTP={(z80.InterruptPending ? 1 : 0)}");

                while (focusedInstructionTrace.Count > focusedTraceMaxEntries)
                    focusedInstructionTrace.Dequeue();
            }

            lastObservedPc = z80.Regs.PC;

            if (TryResumePendingMountedLoadUsrContinuation(z80))
                return false;

            return TryHandleMountedTapeTrap(z80);
        }

        private bool TryHandleMountedTapeTrap(Z80Cpu z80)
        {
            if (!SupportsRomTapeAcceleration)
                return false;

            MountedTape? activeTape = mountedTape;
            if (activeTape == null || !activeTape.TryHandleRomLoadTrap(this, z80))
                return false;

            if (ReferenceEquals(mountedTape, activeTape))
                tapeActivity.ObserveLoaderActivity();
            return true;
        }

        private bool TryResumePendingMountedLoadUsrContinuation(Z80Cpu z80)
        {
            return mountedLoadContinuation.TryResume(z80);
        }

        internal void PrepareMountedLoadUsr0HardwareMode(Z80Cpu z80)
        {
            MachineModel = SpectrumMachineModel.Spectrum128K;
            if (mountedTape != null && !mountedTape.HasMoreBlocks)
                tapeTransport.CompleteIfCurrent(mountedTape);
            z80.ResetExecutionStatePreserveTiming();
            CurrentRomBank = 1;
            PagingLocked = false;
            uses48kMemoryMap = false;
            frameTStates = FrameTStates128;
            last7ffdValue = (byte)(
                (PagedRamBank & 0x07) |
                (ScreenBank == 7 ? 0x08 : 0x00) |
                0x10);
            speakerHigh = false;
            micHigh = false;
            SpeakerEdge = false;
            frameStartTStates = z80.TStates;
            frameStartSpeakerHigh = speakerHigh;
            frameStartAyState = CaptureAyAudioState();
            currentAudioFrameCaptureValid = captureAudioFramesEnabled;
            beeperEvents.Clear();
            ayWrites.Clear();
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
            tStatesUntilNextInterrupt = 0;
            realignInterruptPhaseAfterNextAccept = false;
            ulaRaster.Reset(TimingProfile);
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
            RomProfile = SpectrumRomProfile.Detect(rom0, rom1);
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

            int bank = GetMappedRamBank(addr);

            return ramBanks[bank][addr & 0x3FFF];
        }

        private void WriteMemory(ushort addr, byte value)
        {
            if (addr < 0x4000)
                return;

            int bank = GetMappedRamBank(addr);

            if (screenWriteDiagnosticsEnabled && addr >= 0x4000 && addr < 0x5B00)
            {
                if (!ScreenWriteLog.ContainsKey(addr))
                    ScreenWriteLog[addr] = 0;
                ScreenWriteLog[addr]++;
            }
            else if (screenWriteDiagnosticsEnabled && addr >= 0x5B00 && addr < 0x5C00)
            {
                if (!AboveScreenWriteLog.ContainsKey(addr))
                    AboveScreenWriteLog[addr] = 0;
                AboveScreenWriteLog[addr]++;
                LastAboveWriteFrame = FrameCount;
            }

            ramBanks[bank][addr & 0x3FFF] = value;
            if (debugMemoryWatchAddresses.Count != 0 && debugMemoryWatchAddresses.Contains(addr))
                RecordWatchedMemoryEvent($"F={FrameCount,6} T={cpu.TStates,10} W {addr:X4}={value:X2} bank={bank} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
            if (debugEventCaptureEnabled)
                RecordMemoryEvent($"T={cpu.TStates,10} W {addr:X4}={value:X2} bank={bank} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
        }

        private int GetMappedRamBank(ushort addr) => addr switch
        {
            < 0x8000 => 5,
            < 0xC000 => 2,
            _ => PagedRamBank
        };

        public byte ReadPort(ushort port)
        {
            if (rzxPlayback != null && rzxPlayback.TryReadPortValue(out byte replayValue))
                return replayValue;

            byte value;
            if ((port & 0x0001) == 0)
                value = ReadKeyboardEarPort(port, cpu.TStates);
            else if (IsJoystickInterfaceActive(SpectrumJoystickInterface.Kempston) && IsKempstonJoystickPort(port))
                value = (byte)GetJoystickState(SpectrumJoystickInterface.Kempston);
            else if (IsAyRegisterReadPort(port))
                value = ay.ReadRegister(ay.CurrentRegister);
            else
                value = ReadFloatingBus(cpu.TStates);

            if (debugEventCaptureEnabled)
                RecordPortEvent($"T={cpu.TStates,10} IN  {port:X4}={value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");

            return value;
        }

        private byte ReadPortTimed(ushort port, int sampleOffsetTStates)
        {
            // The read occurs on the third T-state of the four-T-state I/O cycle.
            // Apply the full delay to instruction timing, while sampling EAR at
            // the actual point inside that delayed cycle.
            const int ioCyclesBeforeSample = 3;
            ulong contentionDelay = GetIoContentionDelay(port, sampleOffsetTStates - ioCyclesBeforeSample);
            ulong sampleTStates = cpu.TStates + (ulong)sampleOffsetTStates + contentionDelay;
            if (contentionDelay != 0)
                cpu.AddTStates(contentionDelay);

            if (rzxPlayback != null && rzxPlayback.TryReadPortValue(out byte replayValue))
                return replayValue;

            if ((port & 0x0001) == 0)
            {
                byte keyboardValue = ReadKeyboardEarPort(port, sampleTStates);
                if (debugEventCaptureEnabled)
                    RecordPortEvent($"T={sampleTStates,10} IN  {port:X4}={keyboardValue:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
                return keyboardValue;
            }

            byte value;
            if (IsJoystickInterfaceActive(SpectrumJoystickInterface.Kempston) && IsKempstonJoystickPort(port))
            {
                value = (byte)GetJoystickState(SpectrumJoystickInterface.Kempston);
            }
            else if (IsAyRegisterReadPort(port))
            {
                value = ay.ReadRegister(ay.CurrentRegister);
            }
            else
            {
                long adjustedSampleTStates = (long)sampleTStates + floatingBusSampleAdjustTStates;
                ulong floatingBusSampleTStates = adjustedSampleTStates > 0 ? (ulong)adjustedSampleTStates : 0UL;
                value = ReadFloatingBus(floatingBusSampleTStates);
            }

            if (debugEventCaptureEnabled)
                RecordPortEvent($"T={sampleTStates,10} IN  {port:X4}={value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
            return value;
        }

        private bool IsAyRegisterReadPort(ushort port) =>
            IsAyHardwareEnabled && (port & 0xC002) == 0xC000;

        private static bool IsKempstonJoystickPort(ushort port) =>
            (port & 0x00FF) == 0x001F;

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
                    result &= ReadJoystickKeyboardRow(row);
                }
            }

            bool keyboardOnlyRead = mountedTape != null && IsKeyboardOnlyAccumulatorRead();
            if (mountedTape != null)
            {
                tapeActivity.RecordPortRead(keyboardOnlyRead);
                TryAutomaticallyResumeTapeForRomLoaderRead(sampleTStates);
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

        private byte ReadJoystickKeyboardRow(int row)
        {
            byte value = 0xFF;
            SpectrumJoystickState cursorState = GetJoystickState(SpectrumJoystickInterface.Cursor);
            if (row == 3)
                ClearJoystickKey(ref value, cursorState, SpectrumJoystickState.Left, 4);  // 5
            else if (row == 4)
            {
                ClearJoystickKey(ref value, cursorState, SpectrumJoystickState.Down, 4);  // 6
                ClearJoystickKey(ref value, cursorState, SpectrumJoystickState.Up, 3);    // 7
                ClearJoystickKey(ref value, cursorState, SpectrumJoystickState.Right, 2); // 8
                ClearJoystickKey(ref value, cursorState, SpectrumJoystickState.Fire, 0);  // 0
            }

            SpectrumJoystickState sinclairKeys6To0 = GetJoystickState(SpectrumJoystickInterface.SinclairKeys6To0);
            if (row == 4)
            {
                ClearJoystickKey(ref value, sinclairKeys6To0, SpectrumJoystickState.Left, 4);  // 6
                ClearJoystickKey(ref value, sinclairKeys6To0, SpectrumJoystickState.Right, 3); // 7
                ClearJoystickKey(ref value, sinclairKeys6To0, SpectrumJoystickState.Down, 2);  // 8
                ClearJoystickKey(ref value, sinclairKeys6To0, SpectrumJoystickState.Up, 1);    // 9
                ClearJoystickKey(ref value, sinclairKeys6To0, SpectrumJoystickState.Fire, 0);  // 0
            }

            SpectrumJoystickState sinclairKeys1To5 = GetJoystickState(SpectrumJoystickInterface.SinclairKeys1To5);
            if (row == 3)
            {
                ClearJoystickKey(ref value, sinclairKeys1To5, SpectrumJoystickState.Left, 0);  // 1
                ClearJoystickKey(ref value, sinclairKeys1To5, SpectrumJoystickState.Right, 1); // 2
                ClearJoystickKey(ref value, sinclairKeys1To5, SpectrumJoystickState.Down, 2);  // 3
                ClearJoystickKey(ref value, sinclairKeys1To5, SpectrumJoystickState.Up, 3);    // 4
                ClearJoystickKey(ref value, sinclairKeys1To5, SpectrumJoystickState.Fire, 4);  // 5
            }

            return value;
        }

        private static void ClearJoystickKey(
            ref byte value,
            SpectrumJoystickState state,
            SpectrumJoystickState control,
            int bit)
        {
            if ((state & control) != 0)
                value = (byte)(value & ~(1 << bit));
        }

        private void TryAutomaticallyResumeTapeForRomLoaderRead(ulong sampleTStates)
        {
            MountedTape? activeTape = mountedTape;
            bool resumed = tapeActivity.TryAutomaticallyResume(
                activeTape,
                RomProfile.IsTapeInputRoutinePortRead(cpu.Regs.PC),
                sampleTStates,
                TapeLoaderSearchReadsBeforeAutomaticResume);
            if (resumed && activeTape != null)
            {
                tapeTransport.MarkPlaying(activeTape);
                Trace?.Invoke(
                    $"[TapeTransport] Auto-resume frame={FrameCount} pc=0x{cpu.Regs.PC:X4} {activeTape.DebugPlaybackState}");
            }
        }

        private bool IsKeyboardOnlyAccumulatorRead()
        {
            ushort pc = cpu.Regs.PC;
            bool inputTargetsAccumulator =
                PeekMemory((ushort)(pc - 2)) == 0xDB ||
                (PeekMemory((ushort)(pc - 2)) == 0xED &&
                 PeekMemory((ushort)(pc - 1)) == 0x78);
            if (!inputTargetsAccumulator)
                return false;

            byte nextOpcode = PeekMemory(pc);
            if (nextOpcode == 0x2F) // CPL is commonly used before testing active-low keys.
            {
                pc++;
                nextOpcode = PeekMemory(pc);
            }

            byte immediate = PeekMemory((ushort)(pc + 1));
            return (nextOpcode == 0xE6 && (immediate & 0x40) == 0) ||
                   (nextOpcode == 0xF6 && (immediate & 0x40) != 0);
        }

        private void UpdateAutomaticTapeStopAtFrameBoundary()
        {
            MountedTape? activeTape = mountedTape;
            bool stopped = tapeActivity.TryAutomaticallyStop(
                activeTape,
                cpu.IsHalted,
                cpu.Regs.PC,
                HasPendingMountedLoadUsrContinuation,
                cpu.TStates,
                TapeLoaderActivityReadsPerFrame,
                TapeLoaderHandoffFramesBeforeStop);
            if (stopped && activeTape != null)
            {
                tapeTransport.MarkStopped(activeTape);
                Trace?.Invoke(
                    $"[TapeTransport] Auto-stop frame={FrameCount} pc=0x{cpu.Regs.PC:X4} {activeTape.DebugPlaybackState}");
            }
        }

        private void ResetMountedTapeActivityTracking()
        {
            tapeActivity.Reset();
        }

        private byte ReadFloatingBus(ulong sampleTStates)
        {
            UlaTimingProfile timing = TimingProfile;
            long adjustedFrameOffset = (long)(sampleTStates % (ulong)timing.FrameTStates) - floatingBusDisplayStartAdjustTStates;
            if (adjustedFrameOffset < 0)
                adjustedFrameOffset += timing.FrameTStates;

            ulong frameOffset = (ulong)adjustedFrameOffset;
            if (frameOffset < (ulong)timing.DisplayFetchStartTState)
                return 0xFF;

            int displayOffset = (int)(frameOffset - (ulong)timing.DisplayFetchStartTState);
            int displaySpan = timing.LineTStates * timing.DisplayLineCount;
            if (displayOffset >= displaySpan)
                return 0xFF;

            int line = displayOffset / timing.LineTStates;
            int lineTState = displayOffset % timing.LineTStates;
            if (lineTState >= timing.DisplayFetchTStatesPerLine)
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
            Configure48kSnapshotCore(borderColor, SpectrumMachineModel.Spectrum48K, FrameTStates48);
        }

        public void ConfigureFor48kSnapshot(int borderColor, SpectrumMachineModel hardwareModel)
        {
            int targetFrameTStates = hardwareModel == SpectrumMachineModel.Spectrum128K
                ? FrameTStates128
                : FrameTStates48;
            Configure48kSnapshotCore(borderColor, hardwareModel, targetFrameTStates);
        }

        public void ConfigureFor48kZ80Snapshot(int borderColor)
        {
            Configure48kSnapshotCore(borderColor, SpectrumMachineModel.Spectrum48K, FrameTStates128);
        }

        public void ConfigureFor48kTapeLoad(int borderColor)
        {
            ConfigureFor48kSnapshot(borderColor);
            frameTStates = FrameTStates48;
            tStatesUntilNextInterrupt = 0;
            currentFrameExecutedTStates = 0;
            completedAudioFrames.Clear();
            ulaRaster.Reset(TimingProfile);
        }

        public void ConfigureFor128kTapeLoad(int borderColor)
        {
            MachineModel = SpectrumMachineModel.Spectrum128K;
            PagedRamBank = 0;
            ScreenBank = 5;
            CurrentRomBank = 1;
            PagingLocked = false;
            uses48kMemoryMap = false;
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
            ulaRaster.Reset(TimingProfile);
        }

        private void Configure48kSnapshotCore(int borderColor, SpectrumMachineModel hardwareModel, int targetFrameTStates)
        {
            // The snapshot file layout can be 48K even when the selected hardware
            // is 128K, for example 48K-format saves that still use the AY chip.
            MachineModel = hardwareModel;
            PagedRamBank = 0;
            ScreenBank = 5;
            CurrentRomBank = 1; // Use the 48 BASIC ROM in your current setup.
            PagingLocked = true;
            uses48kMemoryMap = true;
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
            ulaRaster.Reset(TimingProfile);
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

        public void RestoreSnapshotUlaState(byte portFeValue)
        {
            micHigh = (portFeValue & 0x08) != 0;
            speakerHigh = (portFeValue & 0x10) != 0;
            SpeakerEdge = false;
            frameStartSpeakerHigh = speakerHigh;
            frameStartBorderColor = BorderColor;
            borderEvents.Clear();
            beeperEvents.Clear();
        }

        public void RestoreSnapshotAyState(byte selectedRegister, byte[] registers)
        {
            ArgumentNullException.ThrowIfNull(registers);
            if (registers.Length != 16)
                throw new ArgumentException("AY snapshot state must contain exactly 16 registers.", nameof(registers));

            ay.Reset();
            for (byte register = 0; register < registers.Length; register++)
            {
                ay.SelectRegister(register);
                ay.WriteRegister(registers[register]);
            }

            lastAyRegister = (byte)(selectedRegister & 0x0F);
            ay.SelectRegister(lastAyRegister);
            ayWrites.Clear();
            frameStartAyState = CaptureAyAudioState();
        }

        public void ConfigureFor128kSnapshot(byte last7ffdValue, int borderColor)
        {
            MachineModel = SpectrumMachineModel.Spectrum128K;
            PagedRamBank = last7ffdValue & 0x07;
            ScreenBank = ((last7ffdValue & 0x08) != 0) ? 7 : 5;
            CurrentRomBank = ((last7ffdValue & 0x10) != 0) ? 1 : 0;
            PagingLocked = (last7ffdValue & 0x20) != 0;
            uses48kMemoryMap = false;
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
            ulaRaster.Reset(TimingProfile);
        }

        private void WritePort(ushort port, byte value, ulong? eventTStates = null)
        {
            ulong writeTStates = eventTStates ?? cpu.TStates;
            if (debugEventCaptureEnabled)
                RecordPortEvent($"T={writeTStates,10} OUT {port:X4}={value:X2} PC={cpu.Regs.PC:X4} SP={cpu.Regs.SP:X4}");
            SpeakerEdge = false;

            if ((port & 0x0001) == 0)
            {
                int newBorderColor = value & 0x07;
                ulaRaster.RecordBorderChange(writeTStates, newBorderColor);
                BorderColor = newBorderColor;
                RecordBorderEvent(BorderColor, writeTStates);
                micHigh = (value & 0x08) != 0;

                bool newSpeakerHigh = (value & 0x10) != 0;
                if (newSpeakerHigh != speakerHigh)
                {
                    speakerHigh = newSpeakerHigh;
                    SpeakerEdge = true;
                    RecordBeeperEvent(newSpeakerHigh, writeTStates);
                }

                HandleAyPortWrite(port, value, writeTStates);
                return;
            }

            HandleAyPortWrite(port, value, writeTStates);

            if ((port & 0x8002) == 0 && (port & 0x00FF) == 0xFD)
            {
                if (PagingLocked)
                    return;

                ulaRaster.AdvanceTo(writeTStates);

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
            ulaRaster.AdvanceTo(cpu.TStates);
            frameStartTStates = cpu.TStates;
            frameStartBorderColor = BorderColor;
            frameStartSpeakerHigh = speakerHigh;
            frameStartAyState = captureAudioFramesEnabled ? CaptureAyAudioState() : null;
            currentAudioFrameCaptureValid = captureAudioFramesEnabled;
            beeperEvents.Clear();
            ayWrites.Clear();
            borderEvents.Clear();
        }

        private void RecordBeeperEvent(bool newSpeakerHigh, ulong eventTStates)
        {
            if (!captureAudioFramesEnabled)
                return;

            ulong elapsed = eventTStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)int.MaxValue, elapsed);
            beeperEvents.Add(new Audio.BeeperEvent(offset, newSpeakerHigh));
        }

        private void RecordAyWrite(byte register, byte value, ulong eventTStates)
        {
            if (!captureAudioFramesEnabled || !IsAyHardwareEnabled)
                return;

            ulong elapsed = eventTStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)int.MaxValue, elapsed);
            ayWrites.Add(new Audio.AyRegisterWrite(offset, register, value));
        }

        private Audio.AyAudioState? CaptureAyAudioState()
        {
            return IsAyHardwareEnabled ? ay.CaptureAudioState() : null;
        }

        private IReadOnlyList<Audio.AyRegisterWrite> GetAyWritesForAudioFrame()
        {
            return IsAyHardwareEnabled ? ayWrites : Array.Empty<Audio.AyRegisterWrite>();
        }

        private void RecordBorderEvent(int color, ulong eventTStates)
        {
            ulong elapsed = eventTStates - frameStartTStates;
            int offset = (int)Math.Min((ulong)int.MaxValue, elapsed);
            borderEvents.Add(new BorderEvent(offset, color));
        }

        private void CompleteFrameBorderCapture()
        {
            lastCompletedBorderFrame = new BorderFrame(frameTStates, frameStartBorderColor, borderEvents);
        }
    }
}
