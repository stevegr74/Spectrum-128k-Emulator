using System.Linq;
using System.Windows.Forms;

namespace Spectrum128kEmulator
{
    public sealed class SpectrumKeyInputBridge
    {
        private readonly int minHoldTicks;
        private readonly int sameKeyContinuationTicks;
        private readonly Dictionary<Keys, KeyState> keyStates = new();

        private sealed class KeyState
        {
            public int[] Rows = Array.Empty<int>();
            public bool LogicalDown;
            public bool PhysicalDown;
            public bool PendingRelease;
            public int PressTick;
            public int ReleaseNotBeforeTick;
            public int ReleaseGraceUntilTick;
        }

        public readonly record struct SpectrumKeyStateChange(Keys Key, bool Pressed);

        public SpectrumKeyInputBridge(int maxDeferredReleaseTicks, int minHoldTicks = 3, int sameKeyContinuationTicks = 0)
        {
            this.minHoldTicks = minHoldTicks;
            this.sameKeyContinuationTicks = sameKeyContinuationTicks;
        }

        public void Reset()
        {
            keyStates.Clear();
        }

        public IReadOnlyList<SpectrumKeyStateChange> RegisterKeyDown(Keys key, int[] rows, Func<int, ulong> getRowScanCount, int tick)
        {
            if (!keyStates.TryGetValue(key, out KeyState? state))
            {
                state = new KeyState();
                keyStates[key] = state;
            }

            state.Rows = rows;
            state.PhysicalDown = true;

            if (!state.LogicalDown)
            {
                state.LogicalDown = true;
                state.PendingRelease = false;
                state.PressTick = tick;
                state.ReleaseNotBeforeTick = tick + minHoldTicks;
                state.ReleaseGraceUntilTick = 0;
                return new[] { new SpectrumKeyStateChange(key, true) };
            }

            if (state.PendingRelease)
            {
                state.PendingRelease = false;
                state.ReleaseNotBeforeTick = tick + minHoldTicks;
                state.ReleaseGraceUntilTick = 0;
            }
            else
            {
                state.ReleaseNotBeforeTick = Math.Max(state.ReleaseNotBeforeTick, tick + minHoldTicks);
            }

            return Array.Empty<SpectrumKeyStateChange>();
        }

        public IReadOnlyList<SpectrumKeyStateChange> RegisterKeyUp(Keys key, Func<int, ulong> getRowScanCount, int tick)
        {
            if (!keyStates.TryGetValue(key, out KeyState? state))
                return new[] { new SpectrumKeyStateChange(key, false) };

            state.PhysicalDown = false;

            if (!state.LogicalDown)
            {
                keyStates.Remove(key);
                return new[] { new SpectrumKeyStateChange(key, false) };
            }

            if (tick >= state.ReleaseNotBeforeTick)
            {
                if (state.Rows.Length > 1 && sameKeyContinuationTicks > 0)
                {
                    state.PendingRelease = true;
                    state.ReleaseGraceUntilTick = tick + sameKeyContinuationTicks;
                    return Array.Empty<SpectrumKeyStateChange>();
                }

                state.LogicalDown = false;
                state.PendingRelease = false;
                state.ReleaseGraceUntilTick = 0;
                keyStates.Remove(key);
                return new[] { new SpectrumKeyStateChange(key, false) };
            }

            state.PendingRelease = true;
            return Array.Empty<SpectrumKeyStateChange>();
        }

        public IReadOnlyList<SpectrumKeyStateChange> CollectStateChanges(Func<int, ulong> getRowScanCount, int tick)
        {
            if (keyStates.Count == 0)
                return Array.Empty<SpectrumKeyStateChange>();

            List<SpectrumKeyStateChange>? changes = null;
            foreach (var pair in keyStates.ToArray())
            {
                Keys key = pair.Key;
                KeyState state = pair.Value;
                if (!state.PendingRelease)
                    continue;

                if (tick < state.ReleaseNotBeforeTick)
                    continue;

                if (state.ReleaseGraceUntilTick != 0 && tick < state.ReleaseGraceUntilTick)
                    continue;

                changes ??= new List<SpectrumKeyStateChange>();
                changes.Add(new SpectrumKeyStateChange(key, false));

                state.LogicalDown = false;
                state.PendingRelease = false;
                state.ReleaseGraceUntilTick = 0;

                if (!state.PhysicalDown)
                    keyStates.Remove(key);
            }

            return changes ?? (IReadOnlyList<SpectrumKeyStateChange>)Array.Empty<SpectrumKeyStateChange>();
        }

        public string DescribeKeyState(Keys key, Func<int, ulong> getRowScanCount)
        {
            if (!keyStates.TryGetValue(key, out KeyState? state))
                return "state=(none)";

            string rows = state.Rows.Length == 0
                ? "-"
                : string.Join(",", state.Rows.Select(row => $"{row}:{getRowScanCount(row)}"));

            return $"state=logicalDown={state.LogicalDown} physicalDown={state.PhysicalDown} pendingRelease={state.PendingRelease} pressTick={state.PressTick} releaseNotBefore={state.ReleaseNotBeforeTick} releaseGraceUntil={state.ReleaseGraceUntilTick} rows=[{rows}]";
        }
    }
}
