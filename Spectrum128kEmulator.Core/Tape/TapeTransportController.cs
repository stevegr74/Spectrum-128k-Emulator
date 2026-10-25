using System;

namespace Spectrum128kEmulator.Tap
{
    internal sealed class TapeTransportController
    {
        internal sealed record QuickState(
            MountedTape? MountedTape,
            MountedTape.QuickState? MountedTapeState,
            TapeTransportState State,
            TapeTransportTransition LastTransition);

        public MountedTape? MountedTape { get; private set; }
        public TapeTransportState State { get; private set; } = TapeTransportState.NoTape;
        public TapeTransportTransition LastTransition { get; private set; } = TapeTransportTransition.None;
        public bool IsStoppedAtMarker => MountedTape?.IsPausedByStopMarker == true;

        public void Reset()
        {
            MountedTape = null;
            State = TapeTransportState.NoTape;
            LastTransition = TapeTransportTransition.Reset;
        }

        public void Mount(
            MountedTape tape,
            int playbackClockHz,
            bool stopAtProtectedStageHandoffs)
        {
            MountedTape = tape ?? throw new ArgumentNullException(nameof(tape));
            MountedTape.ConfigurePlaybackClock(
                playbackClockHz,
                stopAtProtectedStageHandoffs);
            MountedTape.Reset();
            SynchronizeState(MountedTape);
            LastTransition = TapeTransportTransition.Mounted;
        }

        public TapeTransportState Toggle(ulong currentTStates)
        {
            if (MountedTape == null)
                return State;

            bool wasPaused = MountedTape.IsPlaybackPaused;
            if (wasPaused)
                MountedTape.ResumePlayback(currentTStates);
            else
                MountedTape.PausePlayback(currentTStates);

            SynchronizeState(MountedTape);
            LastTransition = wasPaused
                ? TapeTransportTransition.ManualResume
                : TapeTransportTransition.ManualStop;
            return State;
        }

        public void Eject()
        {
            MountedTape = null;
            State = TapeTransportState.NoTape;
            LastTransition = TapeTransportTransition.Ejected;
        }

        public void CompleteIfCurrent(MountedTape? tape)
        {
            if (!ReferenceEquals(MountedTape, tape))
                return;

            MountedTape = null;
            State = TapeTransportState.Ended;
            LastTransition = TapeTransportTransition.Ended;
        }

        public void SynchronizeState(MountedTape? tape)
        {
            if (!ReferenceEquals(MountedTape, tape) || tape == null)
                return;

            TapeTransportState previousState = State;
            State = tape.HasCompletedPlayback
                ? TapeTransportState.Ended
                : tape.IsPlaybackPaused
                    ? TapeTransportState.Stopped
                    : TapeTransportState.Playing;
            if (State != previousState)
            {
                if (tape.IsPausedByStopMarker)
                    LastTransition = TapeTransportTransition.StopMarker;
                else if (tape.IsPausedByProtectedStageHandoff)
                    LastTransition = TapeTransportTransition.ProtectedStageHandoff;
            }
        }

        public void MarkPlaying(MountedTape tape)
        {
            if (ReferenceEquals(MountedTape, tape))
            {
                State = TapeTransportState.Playing;
                LastTransition = TapeTransportTransition.AutomaticRomResume;
            }
        }

        public void MarkStopped(MountedTape tape)
        {
            if (ReferenceEquals(MountedTape, tape))
            {
                State = TapeTransportState.Stopped;
                LastTransition = TapeTransportTransition.AutomaticHandoffStop;
            }
        }

        public QuickState CaptureQuickState() => new QuickState(
            MountedTape,
            MountedTape?.CaptureQuickState(),
            State,
            LastTransition);

        public void RestoreQuickState(QuickState state)
        {
            MountedTape = state.MountedTape;
            if (MountedTape != null && state.MountedTapeState != null)
                MountedTape.RestoreQuickState(state.MountedTapeState);
            State = state.State;
            LastTransition = state.LastTransition;
        }
    }
}
