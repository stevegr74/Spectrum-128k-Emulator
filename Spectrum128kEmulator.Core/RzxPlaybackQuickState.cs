namespace Spectrum128kEmulator
{
    public sealed partial class RzxPlaybackSession
    {
        public sealed class QuickState
        {
            internal int FrameIndex;
            internal byte[] LastFrameInputs = Array.Empty<byte>();
            internal byte[] CurrentFrameInputs = Array.Empty<byte>();
            internal int CurrentFrameInputIndex;
        }

        public QuickState CaptureQuickState()
        {
            return new QuickState
            {
                FrameIndex = frameIndex,
                LastFrameInputs = (byte[])lastFrameInputs.Clone(),
                CurrentFrameInputs = (byte[])currentFrameInputs.Clone(),
                CurrentFrameInputIndex = currentFrameInputIndex
            };
        }

        public void RestoreQuickState(QuickState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (state.FrameIndex < 0 || state.FrameIndex > frames.Count)
                throw new InvalidOperationException("The saved RZX frame index is outside this playback session.");
            if (state.CurrentFrameInputIndex < 0 || state.CurrentFrameInputIndex > state.CurrentFrameInputs.Length)
                throw new InvalidOperationException("The saved RZX input index is outside the current frame.");

            frameIndex = state.FrameIndex;
            lastFrameInputs = (byte[])state.LastFrameInputs.Clone();
            currentFrameInputs = (byte[])state.CurrentFrameInputs.Clone();
            currentFrameInputIndex = state.CurrentFrameInputIndex;
        }
    }
}
