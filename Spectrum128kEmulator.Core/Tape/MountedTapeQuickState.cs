namespace Spectrum128kEmulator.Tap
{
    public sealed partial class MountedTape
    {
        public sealed class QuickState
        {
            internal int NextBlockIndex;
            internal int EarPlaybackBlockIndex;
            internal int EarStreamByteIndex;
            internal int EarBitIndex;
            internal int EarPulseRepeatCount;
            internal int EarPilotPulsesRemaining;
            internal int EarPulseLengthTStates;
            internal int EarPulseSequenceIndex;
            internal int EarNextBlockIndexAfterPause;
            internal int EarPauseLowTailTStates;
            internal int PendingPrePlaybackPauseTStates;
            internal int RomStreamTrapBlockIndex;
            internal int RomStreamTrapByteIndex;
            internal ulong LastEarSampleTStates;
            internal bool EarLevel;
            internal bool EarPlaybackStarted;
            internal bool RetainedByteStreamTrapAvailable;
            internal bool PlaybackCompleted;
            internal bool PlaybackPaused;
            internal bool PausedByStopMarker;
            internal ulong PausedPulseElapsedTStates;
            internal int StopMarkerResumeBlockIndex;
            internal int EarPlaybackStateValue;
            internal int TapeStateValue;
            internal int? ExpectedDataLength;
            internal string? PendingHeaderName;
            internal TapLoader.TapHeaderInfo? PendingHeaderInfo;
        }

        public QuickState CaptureQuickState()
        {
            return new QuickState
            {
                NextBlockIndex = nextBlockIndex,
                EarPlaybackBlockIndex = earPlaybackBlockIndex,
                EarStreamByteIndex = earStreamByteIndex,
                EarBitIndex = earBitIndex,
                EarPulseRepeatCount = earPulseRepeatCount,
                EarPilotPulsesRemaining = earPilotPulsesRemaining,
                EarPulseLengthTStates = earPulseLengthTStates,
                EarPulseSequenceIndex = earPulseSequenceIndex,
                EarNextBlockIndexAfterPause = earNextBlockIndexAfterPause,
                EarPauseLowTailTStates = earPauseLowTailTStates,
                PendingPrePlaybackPauseTStates = pendingPrePlaybackPauseTStates,
                RomStreamTrapBlockIndex = romStreamTrapBlockIndex,
                RomStreamTrapByteIndex = romStreamTrapByteIndex,
                LastEarSampleTStates = lastEarSampleTStates,
                EarLevel = earLevel,
                EarPlaybackStarted = earPlaybackStarted,
                RetainedByteStreamTrapAvailable = retainedByteStreamTrapAvailable,
                PlaybackCompleted = playbackCompleted,
                PlaybackPaused = playbackPaused,
                PausedByStopMarker = pausedByStopMarker,
                PausedPulseElapsedTStates = pausedPulseElapsedTStates,
                StopMarkerResumeBlockIndex = stopMarkerResumeBlockIndex,
                EarPlaybackStateValue = (int)earPlaybackState,
                TapeStateValue = (int)state,
                ExpectedDataLength = expectedDataLength,
                PendingHeaderName = pendingHeaderName,
                PendingHeaderInfo = pendingHeaderInfo
            };
        }

        public void RestoreQuickState(QuickState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            nextBlockIndex = state.NextBlockIndex;
            earPlaybackBlockIndex = state.EarPlaybackBlockIndex;
            earStreamByteIndex = state.EarStreamByteIndex;
            earBitIndex = state.EarBitIndex;
            earPulseRepeatCount = state.EarPulseRepeatCount;
            earPilotPulsesRemaining = state.EarPilotPulsesRemaining;
            earPulseLengthTStates = state.EarPulseLengthTStates;
            earPulseSequenceIndex = state.EarPulseSequenceIndex;
            earNextBlockIndexAfterPause = state.EarNextBlockIndexAfterPause;
            earPauseLowTailTStates = state.EarPauseLowTailTStates;
            pendingPrePlaybackPauseTStates = state.PendingPrePlaybackPauseTStates;
            romStreamTrapBlockIndex = state.RomStreamTrapBlockIndex;
            romStreamTrapByteIndex = state.RomStreamTrapByteIndex;
            lastEarSampleTStates = state.LastEarSampleTStates;
            earLevel = state.EarLevel;
            earPlaybackStarted = state.EarPlaybackStarted;
            retainedByteStreamTrapAvailable = state.RetainedByteStreamTrapAvailable;
            playbackCompleted = state.PlaybackCompleted;
            playbackPaused = state.PlaybackPaused;
            pausedByStopMarker = state.PausedByStopMarker;
            pausedPulseElapsedTStates = state.PausedPulseElapsedTStates;
            stopMarkerResumeBlockIndex = state.StopMarkerResumeBlockIndex;
            earPlaybackState = (EarPlaybackState)state.EarPlaybackStateValue;
            this.state = (TapeState)state.TapeStateValue;
            expectedDataLength = state.ExpectedDataLength;
            pendingHeaderName = state.PendingHeaderName;
            pendingHeaderInfo = state.PendingHeaderInfo;
        }
    }
}
