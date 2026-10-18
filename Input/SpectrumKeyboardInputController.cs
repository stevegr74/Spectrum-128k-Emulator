using System.Windows.Forms;

namespace Spectrum128kEmulator
{
    public sealed class SpectrumKeyboardInputController
    {
        private readonly SpectrumKeyInputBridge bridge = new();
        private readonly HashSet<Keys> logicalKeysDown = new();
        private readonly bool[,] appliedMatrix = new bool[8, 5];

        public bool HandleHostKeyEvent(
            Keys key,
            bool isDown,
            Func<int, ulong> getRowScanCount,
            ulong currentTStates,
            ulong maximumDeferredReleaseTStates,
            Action<int, int, bool> setMatrixKey)
        {
            if (!SpectrumKeyboardMapping.IsMappedKey(key))
                return false;

            IReadOnlyList<SpectrumKeyInputBridge.SpectrumKeyStateChange> changes = isDown
                ? bridge.RegisterKeyDown(key, SpectrumKeyboardMapping.GetRows(key), getRowScanCount, currentTStates)
                : bridge.RegisterKeyUp(key, getRowScanCount, currentTStates, maximumDeferredReleaseTStates);

            return ApplyChanges(changes, setMatrixKey);
        }

        public bool Advance(
            Func<int, ulong> getRowScanCount,
            ulong currentTStates,
            Action<int, int, bool> setMatrixKey) =>
            ApplyChanges(bridge.CollectStateChanges(getRowScanCount, currentTStates), setMatrixKey);

        public void Reset()
        {
            bridge.Reset();
            logicalKeysDown.Clear();
            Array.Clear(appliedMatrix);
        }

        public string DescribeKeyState(Keys key, Func<int, ulong> getRowScanCount) =>
            bridge.DescribeKeyState(key, getRowScanCount);

        private bool ApplyChanges(
            IReadOnlyList<SpectrumKeyInputBridge.SpectrumKeyStateChange> changes,
            Action<int, int, bool> setMatrixKey)
        {
            if (changes.Count == 0)
                return false;

            foreach (SpectrumKeyInputBridge.SpectrumKeyStateChange change in changes)
            {
                if (change.Pressed)
                    logicalKeysDown.Add(change.Key);
                else
                    logicalKeysDown.Remove(change.Key);
            }

            bool[,] desiredMatrix = new bool[8, 5];
            foreach (Keys key in logicalKeysDown)
            {
                foreach (SpectrumKeyboardMapping.MatrixPosition position in SpectrumKeyboardMapping.GetPositions(key))
                    desiredMatrix[position.Row, position.Bit] = true;
            }

            for (int row = 0; row < 8; row++)
            {
                for (int bit = 0; bit < 5; bit++)
                {
                    if (desiredMatrix[row, bit] == appliedMatrix[row, bit])
                        continue;

                    appliedMatrix[row, bit] = desiredMatrix[row, bit];
                    setMatrixKey(row, bit, appliedMatrix[row, bit]);
                }
            }

            return true;
        }
    }
}
