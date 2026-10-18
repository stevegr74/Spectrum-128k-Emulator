namespace Spectrum128kEmulator
{
    internal sealed class UlaRasterCapture
    {
        internal sealed class QuickState
        {
            internal bool Initialized;
            internal ulong FrameStartTStates;
            internal ulong CapturedUntilTStates;
            internal int NextFetchIndex;
            internal byte[] BitmapBytes = null!;
            internal byte[] AttributeBytes = null!;
            internal UlaFrame? LastCompletedFrame;
        }

        private readonly Func<int> getScreenBank;
        private readonly Func<int, int, byte> readScreenByte;
        private UlaTimingProfile timing;
        private byte[] bitmapBytes = new byte[UlaFrame.CellCount];
        private byte[] attributeBytes = new byte[UlaFrame.CellCount];
        private bool initialized;
        private ulong frameStartTStates;
        private ulong capturedUntilTStates;
        private int nextFetchIndex;

        internal UlaRasterCapture(
            UlaTimingProfile timing,
            Func<int> getScreenBank,
            Func<int, int, byte> readScreenByte)
        {
            this.timing = timing ?? throw new ArgumentNullException(nameof(timing));
            this.getScreenBank = getScreenBank ?? throw new ArgumentNullException(nameof(getScreenBank));
            this.readScreenByte = readScreenByte ?? throw new ArgumentNullException(nameof(readScreenByte));
        }

        internal UlaFrame? LastCompletedFrame { get; private set; }

        internal void Reset(UlaTimingProfile newTiming)
        {
            timing = newTiming ?? throw new ArgumentNullException(nameof(newTiming));
            initialized = false;
            frameStartTStates = 0;
            capturedUntilTStates = 0;
            nextFetchIndex = 0;
            Array.Clear(bitmapBytes);
            Array.Clear(attributeBytes);
            LastCompletedFrame = null;
        }

        internal void AdvanceTo(ulong targetTStates)
        {
            if (!initialized)
                Initialize(targetTStates);
            else if (targetTStates < capturedUntilTStates)
                throw new InvalidOperationException("ULA raster time cannot move backwards.");

            while (true)
            {
                ulong frameEndTStates = frameStartTStates + (ulong)timing.FrameTStates;
                ulong captureEndTStates = Math.Min(targetTStates, frameEndTStates);
                CaptureFetchesThrough(captureEndTStates);

                if (targetTStates < frameEndTStates)
                    break;

                LastCompletedFrame = new UlaFrame(timing.FrameTStates, bitmapBytes, attributeBytes);
                frameStartTStates = frameEndTStates;
                nextFetchIndex = 0;
                Array.Clear(bitmapBytes);
                Array.Clear(attributeBytes);

                if (targetTStates == frameEndTStates)
                    break;
            }

            capturedUntilTStates = targetTStates;
        }

        internal QuickState CaptureQuickState() => new QuickState
        {
            Initialized = initialized,
            FrameStartTStates = frameStartTStates,
            CapturedUntilTStates = capturedUntilTStates,
            NextFetchIndex = nextFetchIndex,
            BitmapBytes = (byte[])bitmapBytes.Clone(),
            AttributeBytes = (byte[])attributeBytes.Clone(),
            LastCompletedFrame = LastCompletedFrame == null
                ? null
                : new UlaFrame(
                    LastCompletedFrame.FrameTStates,
                    LastCompletedFrame.GetBitmapBytesCopy(),
                    LastCompletedFrame.GetAttributeBytesCopy())
        };

        internal void RestoreQuickState(QuickState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (state.BitmapBytes.Length != UlaFrame.CellCount ||
                state.AttributeBytes.Length != UlaFrame.CellCount)
            {
                throw new InvalidOperationException("The ULA raster state is invalid.");
            }

            initialized = state.Initialized;
            frameStartTStates = state.FrameStartTStates;
            capturedUntilTStates = state.CapturedUntilTStates;
            nextFetchIndex = state.NextFetchIndex;
            bitmapBytes = (byte[])state.BitmapBytes.Clone();
            attributeBytes = (byte[])state.AttributeBytes.Clone();
            LastCompletedFrame = state.LastCompletedFrame == null
                ? null
                : new UlaFrame(
                    state.LastCompletedFrame.FrameTStates,
                    state.LastCompletedFrame.GetBitmapBytesCopy(),
                    state.LastCompletedFrame.GetAttributeBytesCopy());
        }

        private void Initialize(ulong targetTStates)
        {
            frameStartTStates = targetTStates - (targetTStates % (ulong)timing.FrameTStates);
            capturedUntilTStates = frameStartTStates;
            nextFetchIndex = 0;
            Array.Clear(bitmapBytes);
            Array.Clear(attributeBytes);
            initialized = true;
        }

        private void CaptureFetchesThrough(ulong targetTStates)
        {
            while (nextFetchIndex < UlaFrame.CellCount * 2)
            {
                int cellIndex = nextFetchIndex >> 1;
                bool attributeFetch = (nextFetchIndex & 1) != 0;
                int line = cellIndex / UlaFrame.CellsPerLine;
                int column = cellIndex % UlaFrame.CellsPerLine;
                int fetchOffset = timing.DisplayFetchStartTState +
                                  (line * timing.LineTStates) +
                                  ((column >> 1) * 8) +
                                  ((column & 1) * 2) +
                                  (attributeFetch ? 1 : 0);
                ulong fetchTStates = frameStartTStates + (ulong)fetchOffset;
                if (fetchTStates > targetTStates)
                    break;

                int screenBank = getScreenBank();
                if (attributeFetch)
                {
                    int attributeOffset = 0x1800 + ((line >> 3) * UlaFrame.CellsPerLine) + column;
                    attributeBytes[cellIndex] = readScreenByte(screenBank, attributeOffset);
                }
                else
                {
                    int charRow = line >> 3;
                    int charLine = line & 0x07;
                    int bitmapOffset = ((charRow & 0x18) << 8) |
                                       ((charRow & 0x07) << 5) |
                                       (charLine << 8) |
                                       column;
                    bitmapBytes[cellIndex] = readScreenByte(screenBank, bitmapOffset);
                }

                nextFetchIndex++;
            }
        }
    }
}
