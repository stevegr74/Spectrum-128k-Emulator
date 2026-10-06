using Spectrum128kEmulator.Audio;
using Spectrum128kEmulator.Tap;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed partial class Spectrum128Machine
    {
        public sealed class QuickState
        {
            internal Z80Cpu.QuickState CpuState = null!;
            internal Ay8912.QuickState AyState = null!;
            internal byte[][] RamBanks = null!;
            internal byte[] KeyboardMatrix = null!;
            internal ulong[] KeyboardRowScanCounts = null!;
            internal SpectrumMachineModel Model;
            internal int PagedRamBank;
            internal int CurrentRomBank;
            internal bool PagingLocked;
            internal int ScreenBank;
            internal int BorderColor;
            internal int FrameCount;
            internal byte Last7ffdValue;
            internal bool Uses48kMemoryMap;
            internal int FrameTStates;
            internal byte LastAyRegister;
            internal bool SpeakerHigh;
            internal bool MicHigh;
            internal bool SpeakerEdge;
            internal bool FrameStartSpeakerHigh;
            internal ulong FrameStartTStates;
            internal int FrameStartBorderColor;
            internal int CurrentFrameExecutedTStates;
            internal int LastAudioFrameTStates;
            internal BeeperEvent[] BeeperEvents = null!;
            internal AyRegisterWrite[] AyWrites = null!;
            internal BorderEvent[] BorderEvents = null!;
            internal BorderFrame LastCompletedBorderFrame = null!;
            internal AyAudioState? FrameStartAyState;
            internal bool CaptureAudioFramesEnabled;
            internal int FloatingBusDisplayStartAdjustTStates;
            internal int FloatingBusSampleAdjustTStates;
            internal int TStatesUntilNextInterrupt;
            internal bool RealignInterruptPhaseAfterNextAccept;
            internal ulong? InterruptPulseEndTStates;
            internal MountedTape? MountedTape;
            internal MountedTape.QuickState? MountedTapeState;
            internal TapeTransportState TapeTransportState;
            internal ulong MountedTapePortReadCount;
            internal ulong MountedTapePortReadCountAtFrameStart;
            internal ulong MountedTapeKeyboardOnlyPortReadCount;
            internal ulong MountedTapeKeyboardOnlyPortReadCountAtFrameStart;
            internal bool MountedTapeLoaderActivityObserved;
            internal int MountedTapeHandoffFrames;
            internal RzxPlaybackSession? RzxPlayback;
            internal RzxPlaybackSession.QuickState? RzxPlaybackState;
            internal MountedLoadContinuationController.QuickState MountedLoadContinuationState = null!;

            internal QuickState(DateTime capturedAtUtc, SpectrumMachineModel model, ushort programCounter)
            {
                CapturedAtUtc = capturedAtUtc;
                MachineModel = model;
                ProgramCounter = programCounter;
            }

            public DateTime CapturedAtUtc { get; }
            public SpectrumMachineModel MachineModel { get; }
            public ushort ProgramCounter { get; }
        }

        public QuickState CaptureQuickState()
        {
            var state = new QuickState(DateTime.UtcNow, MachineModel, cpu.Regs.PC)
            {
                CpuState = cpu.CaptureQuickState(),
                AyState = ay.CaptureQuickState(),
                RamBanks = CloneRamBanks(ramBanks),
                KeyboardMatrix = (byte[])keyboardMatrix.Clone(),
                KeyboardRowScanCounts = (ulong[])keyboardRowScanCounts.Clone(),
                Model = MachineModel,
                PagedRamBank = PagedRamBank,
                CurrentRomBank = CurrentRomBank,
                PagingLocked = PagingLocked,
                ScreenBank = ScreenBank,
                BorderColor = BorderColor,
                FrameCount = FrameCount,
                Last7ffdValue = last7ffdValue,
                Uses48kMemoryMap = uses48kMemoryMap,
                FrameTStates = frameTStates,
                LastAyRegister = lastAyRegister,
                SpeakerHigh = speakerHigh,
                MicHigh = micHigh,
                SpeakerEdge = SpeakerEdge,
                FrameStartSpeakerHigh = frameStartSpeakerHigh,
                FrameStartTStates = frameStartTStates,
                FrameStartBorderColor = frameStartBorderColor,
                CurrentFrameExecutedTStates = currentFrameExecutedTStates,
                LastAudioFrameTStates = lastAudioFrameTStates,
                BeeperEvents = beeperEvents.ToArray(),
                AyWrites = ayWrites.ToArray(),
                BorderEvents = borderEvents.ToArray(),
                LastCompletedBorderFrame = new BorderFrame(
                    lastCompletedBorderFrame.FrameTStates,
                    lastCompletedBorderFrame.InitialColor,
                    lastCompletedBorderFrame.Events),
                FrameStartAyState = frameStartAyState == null
                    ? null
                    : new AyAudioState(frameStartAyState.GetRegistersCopy()),
                CaptureAudioFramesEnabled = captureAudioFramesEnabled,
                FloatingBusDisplayStartAdjustTStates = floatingBusDisplayStartAdjustTStates,
                FloatingBusSampleAdjustTStates = floatingBusSampleAdjustTStates,
                TStatesUntilNextInterrupt = tStatesUntilNextInterrupt,
                RealignInterruptPhaseAfterNextAccept = realignInterruptPhaseAfterNextAccept,
                InterruptPulseEndTStates = interruptPulseEndTStates,
                MountedTape = mountedTape,
                MountedTapeState = mountedTape?.CaptureQuickState(),
                TapeTransportState = TapeTransportState,
                MountedTapePortReadCount = mountedTapePortReadCount,
                MountedTapePortReadCountAtFrameStart = mountedTapePortReadCountAtFrameStart,
                MountedTapeKeyboardOnlyPortReadCount = mountedTapeKeyboardOnlyPortReadCount,
                MountedTapeKeyboardOnlyPortReadCountAtFrameStart = mountedTapeKeyboardOnlyPortReadCountAtFrameStart,
                MountedTapeLoaderActivityObserved = mountedTapeLoaderActivityObserved,
                MountedTapeHandoffFrames = mountedTapeHandoffFrames,
                RzxPlayback = rzxPlayback,
                RzxPlaybackState = rzxPlayback?.CaptureQuickState(),
                MountedLoadContinuationState = mountedLoadContinuation.CaptureQuickState()
            };

            return state;
        }

        public void RestoreQuickState(QuickState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            ValidateQuickState(state);

            ClearLogs();
            ClearDebugHistory();
            for (int bank = 0; bank < ramBanks.Length; bank++)
                Buffer.BlockCopy(state.RamBanks[bank], 0, ramBanks[bank], 0, ramBanks[bank].Length);
            Buffer.BlockCopy(state.KeyboardMatrix, 0, keyboardMatrix, 0, keyboardMatrix.Length);
            Array.Copy(state.KeyboardRowScanCounts, keyboardRowScanCounts, keyboardRowScanCounts.Length);

            MachineModel = state.Model;
            PagedRamBank = state.PagedRamBank;
            CurrentRomBank = state.CurrentRomBank;
            PagingLocked = state.PagingLocked;
            ScreenBank = state.ScreenBank;
            BorderColor = state.BorderColor;
            FrameCount = state.FrameCount;
            last7ffdValue = state.Last7ffdValue;
            uses48kMemoryMap = state.Uses48kMemoryMap;
            frameTStates = state.FrameTStates;
            lastAyRegister = state.LastAyRegister;
            speakerHigh = state.SpeakerHigh;
            micHigh = state.MicHigh;
            SpeakerEdge = state.SpeakerEdge;
            frameStartSpeakerHigh = state.FrameStartSpeakerHigh;
            frameStartTStates = state.FrameStartTStates;
            frameStartBorderColor = state.FrameStartBorderColor;
            currentFrameExecutedTStates = state.CurrentFrameExecutedTStates;
            lastAudioFrameTStates = state.LastAudioFrameTStates;
            captureAudioFramesEnabled = state.CaptureAudioFramesEnabled;
            floatingBusDisplayStartAdjustTStates = state.FloatingBusDisplayStartAdjustTStates;
            floatingBusSampleAdjustTStates = state.FloatingBusSampleAdjustTStates;
            tStatesUntilNextInterrupt = state.TStatesUntilNextInterrupt;
            realignInterruptPhaseAfterNextAccept = state.RealignInterruptPhaseAfterNextAccept;
            interruptPulseEndTStates = state.InterruptPulseEndTStates;

            cpu.RestoreQuickState(state.CpuState);
            ay.RestoreQuickState(state.AyState);
            RestoreFrameCaptureState(state);
            RestoreMediaState(state);
            RestoreMountedLoadContinuation(state);
        }

        private void RestoreFrameCaptureState(QuickState state)
        {
            beeperEvents.Clear();
            beeperEvents.AddRange(state.BeeperEvents);
            ayWrites.Clear();
            ayWrites.AddRange(state.AyWrites);
            borderEvents.Clear();
            borderEvents.AddRange(state.BorderEvents);
            lastCompletedBorderFrame = new BorderFrame(
                state.LastCompletedBorderFrame.FrameTStates,
                state.LastCompletedBorderFrame.InitialColor,
                state.LastCompletedBorderFrame.Events);
            frameStartAyState = state.FrameStartAyState == null
                ? null
                : new AyAudioState(state.FrameStartAyState.GetRegistersCopy());
            completedAudioFrames.Clear();
        }

        private void RestoreMediaState(QuickState state)
        {
            mountedTape = state.MountedTape;
            if (mountedTape != null && state.MountedTapeState != null)
                mountedTape.RestoreQuickState(state.MountedTapeState);
            TapeTransportState = state.TapeTransportState;
            mountedTapePortReadCount = state.MountedTapePortReadCount;
            mountedTapePortReadCountAtFrameStart = state.MountedTapePortReadCountAtFrameStart;
            mountedTapeKeyboardOnlyPortReadCount = state.MountedTapeKeyboardOnlyPortReadCount;
            mountedTapeKeyboardOnlyPortReadCountAtFrameStart = state.MountedTapeKeyboardOnlyPortReadCountAtFrameStart;
            mountedTapeLoaderActivityObserved = state.MountedTapeLoaderActivityObserved;
            mountedTapeHandoffFrames = state.MountedTapeHandoffFrames;

            rzxPlayback = state.RzxPlayback;
            if (rzxPlayback != null && state.RzxPlaybackState != null)
                rzxPlayback.RestoreQuickState(state.RzxPlaybackState);
        }

        private void RestoreMountedLoadContinuation(QuickState state)
        {
            mountedLoadContinuation.RestoreQuickState(state.MountedLoadContinuationState);
        }

        private static byte[][] CloneRamBanks(byte[][] source)
        {
            var copy = new byte[source.Length][];
            for (int bank = 0; bank < source.Length; bank++)
                copy[bank] = (byte[])source[bank].Clone();
            return copy;
        }

        private static void ValidateQuickState(QuickState state)
        {
            if (state.RamBanks.Length != 8 || state.RamBanks.Any(bank => bank.Length != 16384))
                throw new InvalidOperationException("The quick state RAM layout is invalid.");
            if (state.KeyboardMatrix.Length != 8 || state.KeyboardRowScanCounts.Length != 8)
                throw new InvalidOperationException("The quick state keyboard layout is invalid.");
            if (state.MountedLoadContinuationState == null)
                throw new InvalidOperationException("The quick state loader continuation is invalid.");
        }
    }
}
