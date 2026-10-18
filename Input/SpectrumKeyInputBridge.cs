using System.Linq;
using System.Windows.Forms;

namespace Spectrum128kEmulator
{
    public sealed class SpectrumKeyInputBridge
    {
        private readonly Dictionary<Keys, KeyState> keyStates = new();

        private sealed class KeyState
        {
            public int[] Rows = Array.Empty<int>();
            public ulong[] ScanCountsAtPress = Array.Empty<ulong>();
            public bool LogicalDown;
            public bool PhysicalDown;
            public bool PendingRelease;
            public ulong ReleaseDeadlineTStates;
        }

        public readonly record struct SpectrumKeyStateChange(Keys Key, bool Pressed);

        public void Reset() => keyStates.Clear();

        public IReadOnlyList<SpectrumKeyStateChange> RegisterKeyDown(
            Keys key,
            int[] rows,
            Func<int, ulong> getRowScanCount,
            ulong currentTStates)
        {
            if (!keyStates.TryGetValue(key, out KeyState? state))
            {
                state = new KeyState();
                keyStates[key] = state;
            }

            if (state.PhysicalDown)
                return Array.Empty<SpectrumKeyStateChange>();

            state.PhysicalDown = true;
            state.PendingRelease = false;
            state.ReleaseDeadlineTStates = 0;

            if (state.LogicalDown)
                return Array.Empty<SpectrumKeyStateChange>();

            state.Rows = rows.Distinct().ToArray();
            state.ScanCountsAtPress = state.Rows.Select(getRowScanCount).ToArray();
            state.LogicalDown = true;
            return new[] { new SpectrumKeyStateChange(key, true) };
        }

        public IReadOnlyList<SpectrumKeyStateChange> RegisterKeyUp(
            Keys key,
            Func<int, ulong> getRowScanCount,
            ulong currentTStates,
            ulong maximumDeferredReleaseTStates)
        {
            if (!keyStates.TryGetValue(key, out KeyState? state) || !state.PhysicalDown)
                return Array.Empty<SpectrumKeyStateChange>();

            state.PhysicalDown = false;
            if (!state.LogicalDown)
            {
                keyStates.Remove(key);
                return Array.Empty<SpectrumKeyStateChange>();
            }

            if (WasObserved(state, getRowScanCount))
                return Release(key, state);

            state.PendingRelease = true;
            state.ReleaseDeadlineTStates = AddSaturating(currentTStates, maximumDeferredReleaseTStates);
            return Array.Empty<SpectrumKeyStateChange>();
        }

        public IReadOnlyList<SpectrumKeyStateChange> CollectStateChanges(
            Func<int, ulong> getRowScanCount,
            ulong currentTStates)
        {
            if (keyStates.Count == 0)
                return Array.Empty<SpectrumKeyStateChange>();

            List<SpectrumKeyStateChange>? changes = null;
            foreach ((Keys key, KeyState state) in keyStates.ToArray())
            {
                if (!state.PendingRelease || state.PhysicalDown)
                    continue;

                if (!WasObserved(state, getRowScanCount) && currentTStates < state.ReleaseDeadlineTStates)
                    continue;

                changes ??= new List<SpectrumKeyStateChange>();
                changes.AddRange(Release(key, state));
            }

            return changes ?? (IReadOnlyList<SpectrumKeyStateChange>)Array.Empty<SpectrumKeyStateChange>();
        }

        public string DescribeKeyState(Keys key, Func<int, ulong> getRowScanCount)
        {
            if (!keyStates.TryGetValue(key, out KeyState? state))
                return "state=(none)";

            string rows = string.Join(",", state.Rows.Select((row, index) =>
                $"{row}:{state.ScanCountsAtPress[index]}->{getRowScanCount(row)}"));
            return $"state=logicalDown={state.LogicalDown} physicalDown={state.PhysicalDown} pendingRelease={state.PendingRelease} releaseDeadlineTStates={state.ReleaseDeadlineTStates} rows=[{rows}]";
        }

        private IReadOnlyList<SpectrumKeyStateChange> Release(Keys key, KeyState state)
        {
            state.LogicalDown = false;
            state.PendingRelease = false;
            keyStates.Remove(key);
            return new[] { new SpectrumKeyStateChange(key, false) };
        }

        private static bool WasObserved(KeyState state, Func<int, ulong> getRowScanCount)
        {
            for (int i = 0; i < state.Rows.Length; i++)
            {
                if (getRowScanCount(state.Rows[i]) <= state.ScanCountsAtPress[i])
                    return false;
            }

            return true;
        }

        private static ulong AddSaturating(ulong left, ulong right) =>
            ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    }
}
