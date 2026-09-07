using System;
using System.Collections.Generic;

namespace Spectrum128kEmulator
{
    public readonly record struct BorderEvent(int TStateOffset, int Color);

    public sealed class BorderFrame
    {
        public BorderFrame(int frameTStates, int initialColor, IReadOnlyList<BorderEvent> events)
        {
            FrameTStates = frameTStates;
            InitialColor = initialColor & 0x07;
            Events = events == null ? Array.Empty<BorderEvent>() : Copy(events);
        }

        public int FrameTStates { get; }
        public int InitialColor { get; }
        public IReadOnlyList<BorderEvent> Events { get; }

        private static BorderEvent[] Copy(IReadOnlyList<BorderEvent> events)
        {
            var copy = new BorderEvent[events.Count];
            for (int index = 0; index < events.Count; index++)
                copy[index] = events[index];
            return copy;
        }
    }
}
