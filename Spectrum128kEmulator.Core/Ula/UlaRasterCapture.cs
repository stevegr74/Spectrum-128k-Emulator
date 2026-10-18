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
            internal int CurrentBorderColor;
            internal int FrameStartBorderColor;
            internal BorderEvent[] BorderEvents = null!;
            internal UlaFrame? LastCompletedFrame;
        }

        private readonly Func<int> getScreenBank;
        private readonly Func<int> getBorderColor;
        private readonly Func<int, int, byte> readScreenByte;
        private UlaTimingProfile timing;
        private byte[] bitmapBytes = new byte[UlaFrame.CellCount];
        private byte[] attributeBytes = new byte[UlaFrame.CellCount];
        private bool initialized;
        private ulong frameStartTStates;
        private ulong capturedUntilTStates;
        private int nextFetchIndex;
        private int currentBorderColor;
        private int frameStartBorderColor;
        private readonly List<BorderEvent> borderEvents = new();

        internal UlaRasterCapture(
            UlaTimingProfile timing,
            Func<int> getScreenBank,
            Func<int> getBorderColor,
            Func<int, int, byte> readScreenByte)
        {
            this.timing = timing ?? throw new ArgumentNullException(nameof(timing));
            this.getScreenBank = getScreenBank ?? throw new ArgumentNullException(nameof(getScreenBank));
            this.getBorderColor = getBorderColor ?? throw new ArgumentNullException(nameof(getBorderColor));
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
            currentBorderColor = 0;
            frameStartBorderColor = 0;
            borderEvents.Clear();
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

                var completedBorderFrame = new BorderFrame(
                    timing.FrameTStates,
                    frameStartBorderColor,
                    borderEvents);
                LastCompletedFrame = new UlaFrame(
                    timing.FrameTStates,
                    bitmapBytes,
                    attributeBytes,
                    completedBorderFrame);
                frameStartTStates = frameEndTStates;
                nextFetchIndex = 0;
                frameStartBorderColor = currentBorderColor;
                borderEvents.Clear();
                Array.Clear(bitmapBytes);
                Array.Clear(attributeBytes);

                if (targetTStates == frameEndTStates)
                    break;
            }

            capturedUntilTStates = targetTStates;
        }

        internal void RecordBorderChange(ulong eventTStates, int color)
        {
            AdvanceTo(eventTStates);
            int offset = (int)Math.Min(
                (ulong)int.MaxValue,
                eventTStates - frameStartTStates);
            currentBorderColor = color & 0x07;
            borderEvents.Add(new BorderEvent(offset, currentBorderColor));
        }

        internal QuickState CaptureQuickState() => new QuickState
        {
            Initialized = initialized,
            FrameStartTStates = frameStartTStates,
            CapturedUntilTStates = capturedUntilTStates,
            NextFetchIndex = nextFetchIndex,
            BitmapBytes = (byte[])bitmapBytes.Clone(),
            AttributeBytes = (byte[])attributeBytes.Clone(),
            CurrentBorderColor = currentBorderColor,
            FrameStartBorderColor = frameStartBorderColor,
            BorderEvents = borderEvents.ToArray(),
            LastCompletedFrame = LastCompletedFrame == null
                ? null
                : new UlaFrame(
                    LastCompletedFrame.FrameTStates,
                    LastCompletedFrame.GetBitmapBytesCopy(),
                    LastCompletedFrame.GetAttributeBytesCopy(),
                    LastCompletedFrame.BorderFrame)
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
            currentBorderColor = state.CurrentBorderColor;
            frameStartBorderColor = state.FrameStartBorderColor;
            borderEvents.Clear();
            borderEvents.AddRange(state.BorderEvents);
            LastCompletedFrame = state.LastCompletedFrame == null
                ? null
                : new UlaFrame(
                    state.LastCompletedFrame.FrameTStates,
                    state.LastCompletedFrame.GetBitmapBytesCopy(),
                    state.LastCompletedFrame.GetAttributeBytesCopy(),
                    state.LastCompletedFrame.BorderFrame);
        }

        private void Initialize(ulong targetTStates)
        {
            frameStartTStates = targetTStates - (targetTStates % (ulong)timing.FrameTStates);
            capturedUntilTStates = frameStartTStates;
            nextFetchIndex = 0;
            currentBorderColor = getBorderColor() & 0x07;
            frameStartBorderColor = currentBorderColor;
            borderEvents.Clear();
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
