using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumKeyInputBridgeTests
    {
        private const ulong FrameTStates = 69888;

        [Fact]
        public void QuickTap_RemainsPressed_UntilItsRowHasBeenScanned()
        {
            var bridge = new SpectrumKeyInputBridge();
            ulong[] scans = new ulong[8];

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Enter, true) },
                bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, row => scans[row], 0));
            Assert.Empty(bridge.RegisterKeyUp(Keys.Enter, row => scans[row], 0, FrameTStates));
            Assert.Empty(bridge.CollectStateChanges(row => scans[row], 1000));

            scans[6]++;
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Enter, false) },
                bridge.CollectStateChanges(row => scans[row], 1001));
        }

        [Fact]
        public void CompositeKey_IsReleasedOnlyAfterEveryRequiredRowWasScanned()
        {
            var bridge = new SpectrumKeyInputBridge();
            ulong[] scans = new ulong[8];

            bridge.RegisterKeyDown(Keys.Left, new[] { 0, 3 }, row => scans[row], 0);
            bridge.RegisterKeyUp(Keys.Left, row => scans[row], 10, FrameTStates);

            scans[3]++;
            Assert.Empty(bridge.CollectStateChanges(row => scans[row], 20));

            scans[0]++;
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Left, false) },
                bridge.CollectStateChanges(row => scans[row], 21));
        }

        [Fact]
        public void UnobservedTap_IsReleasedAtOneFrameSafetyDeadline()
        {
            var bridge = new SpectrumKeyInputBridge();

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0, 500);
            bridge.RegisterKeyUp(Keys.Enter, _ => 0, 500, FrameTStates);

            Assert.Empty(bridge.CollectStateChanges(_ => 0, 500 + FrameTStates - 1));
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Enter, false) },
                bridge.CollectStateChanges(_ => 0, 500 + FrameTStates));
        }

        [Fact]
        public void HeldKey_DoesNotUseTheSafetyDeadline()
        {
            var bridge = new SpectrumKeyInputBridge();

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0, 0);

            Assert.Empty(bridge.CollectStateChanges(_ => 0, FrameTStates * 10));
        }

        [Fact]
        public void RepeatedWindowsKeyDown_DoesNotCreateAdditionalEdges()
        {
            var bridge = new SpectrumKeyInputBridge();

            Assert.Single(bridge.RegisterKeyDown(Keys.A, new[] { 1 }, _ => 0, 0));
            Assert.Empty(bridge.RegisterKeyDown(Keys.A, new[] { 1 }, _ => 0, 1000));
        }

        [Fact]
        public void RetapBeforeAnyScan_RemainsOneContinuousHardwareState()
        {
            var bridge = new SpectrumKeyInputBridge();

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0, 0);
            bridge.RegisterKeyUp(Keys.Enter, _ => 0, 100, FrameTStates);

            Assert.Empty(bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0, 200));
            Assert.Empty(bridge.CollectStateChanges(_ => 0, FrameTStates * 2));
        }
    }
}
