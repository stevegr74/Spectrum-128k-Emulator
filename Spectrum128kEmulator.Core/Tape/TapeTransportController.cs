using System;

namespace Spectrum128kEmulator.Tap
{
    internal sealed class TapeTransportController
    {
        internal sealed record QuickState(
            MountedTape? MountedTape,
            MountedTape.QuickState? MountedTapeState,
            TapeTransportState State);

        public MountedTape? MountedTape { get; private set; }
        public TapeTransportState State { get; private set; } = TapeTransportState.NoTape;
        public bool IsStoppedAtMarker => MountedTape?.IsPausedByStopMarker == true;

        public void Reset()
        {
            MountedTape = null;
            State = TapeTransportState.NoTape;
        }

        public void Mount(MountedTape tape, int playbackClockHz)
        {
            MountedTape = tape ?? throw new ArgumentNullException(nameof(tape));
            MountedTape.ConfigurePlaybackClock(playbackClockHz);
            MountedTape.Reset();
            SynchronizeState(MountedTape);
        }

        public TapeTransportState Toggle(ulong currentTStates)
        {
            if (MountedTape == null)
                return State;

            if (MountedTape.IsPlaybackPaused)
                MountedTape.ResumePlayback(currentTStates);
            else
                MountedTape.PausePlayback(currentTStates);

            SynchronizeState(MountedTape);
            return State;
        }

        public void Eject() => Reset();

        public void CompleteIfCurrent(MountedTape? tape)
        {
            if (!ReferenceEquals(MountedTape, tape))
                return;

            MountedTape = null;
            State = TapeTransportState.Ended;
        }

        public void SynchronizeState(MountedTape? tape)
        {
            if (!ReferenceEquals(MountedTape, tape) || tape == null)
                return;

            State = tape.HasCompletedPlayback
                ? TapeTransportState.Ended
                : tape.IsPlaybackPaused
                    ? TapeTransportState.Stopped
                    : TapeTransportState.Playing;
        }

        public void MarkPlaying(MountedTape tape)
        {
            if (ReferenceEquals(MountedTape, tape))
                State = TapeTransportState.Playing;
        }

        public void MarkStopped(MountedTape tape)
        {
            if (ReferenceEquals(MountedTape, tape))
                State = TapeTransportState.Stopped;
        }

        public QuickState CaptureQuickState() => new QuickState(
            MountedTape,
            MountedTape?.CaptureQuickState(),
            State);

        public void RestoreQuickState(QuickState state)
        {
            MountedTape = state.MountedTape;
            if (MountedTape != null && state.MountedTapeState != null)
                MountedTape.RestoreQuickState(state.MountedTapeState);
            State = state.State;
        }
    }
}
