using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator
{
    public sealed class RzxPlaybackSession
    {
        private readonly IReadOnlyList<RzxFrame> frames;
        private int frameIndex;
        private byte[] lastFrameInputs = Array.Empty<byte>();
        private byte[] currentFrameInputs = Array.Empty<byte>();
        private int currentFrameInputIndex;

        public RzxPlaybackSession(string displayName, IReadOnlyList<RzxFrame> frames)
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "unnamed.rzx" : displayName;
            this.frames = frames ?? throw new ArgumentNullException(nameof(frames));
        }

        public string DisplayName { get; }
        public bool HasMoreFrames => frameIndex < frames.Count;
        public int FrameIndex => frameIndex;

        public bool TryBeginNextFrame(out ushort instructionFetchCount)
        {
            if (!HasMoreFrames)
            {
                instructionFetchCount = 0;
                currentFrameInputs = Array.Empty<byte>();
                currentFrameInputIndex = 0;
                return false;
            }

            RzxFrame frame = frames[frameIndex++];
            instructionFetchCount = frame.InstructionFetchCount;
            currentFrameInputs = frame.IsRepeatedFrame ? lastFrameInputs : frame.InputBytes;
            currentFrameInputIndex = 0;
            if (!frame.IsRepeatedFrame)
                lastFrameInputs = frame.InputBytes;
            return true;
        }

        public bool TryReadPortValue(out byte value)
        {
            if (currentFrameInputIndex >= currentFrameInputs.Length)
            {
                value = 0xFF;
                return false;
            }

            value = currentFrameInputs[currentFrameInputIndex++];
            return true;
        }
    }

    public readonly record struct RzxFrame(ushort InstructionFetchCount, byte[] InputBytes, bool IsRepeatedFrame);
}
