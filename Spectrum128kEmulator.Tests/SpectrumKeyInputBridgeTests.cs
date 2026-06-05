using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class SpectrumKeyInputBridgeTests
    {
        [Fact]
        public void SingleTap_Holds_For_Minimum_Tick_Window_Before_Releasing()
        {
            var bridge = new SpectrumKeyInputBridge(8);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Enter, true) },
                bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, 0).ToArray());

            Assert.Empty(bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, 0));
            Assert.Empty(bridge.CollectStateChanges(_ => 0UL, 0));

            Assert.Empty(bridge.CollectStateChanges(_ => 0UL, 1));

            Assert.Empty(bridge.CollectStateChanges(_ => 0UL, 2));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Enter, false) },
                bridge.CollectStateChanges(_ => 0UL, 3).ToArray());
        }

        [Fact]
        public void DeferredRelease_Releases_When_Minimum_Hold_Window_Expires()
        {
            var bridge = new SpectrumKeyInputBridge(8);

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, 10);
            Assert.Empty(bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, 10));

            for (int frame = 10; frame < 13; frame++)
                Assert.Empty(bridge.CollectStateChanges(_ => 0UL, frame));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Enter, false) },
                bridge.CollectStateChanges(_ => 0UL, 13).ToArray());
        }

        [Fact]
        public void RepeatedRapidSameKeyTaps_With_PerSliceScans_Behave_Like_A_Continuous_Hold()
        {
            var bridge = new SpectrumKeyInputBridge(8);
            int releaseCount = 0;

            for (int frame = 0; frame < 50; frame++)
            {
                bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, frame);
                bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, frame);

                foreach (var change in bridge.CollectStateChanges(_ => 0UL, frame))
                {
                    if (!change.Pressed)
                        releaseCount++;
                }
            }

            Assert.Equal(0, releaseCount);

            for (int frame = 50; frame < 60; frame++)
            {
                foreach (var change in bridge.CollectStateChanges(_ => 0UL, frame))
                {
                    if (!change.Pressed)
                        releaseCount++;
                }
            }

            Assert.Equal(1, releaseCount);
        }

        [Fact]
        public void RepeatedRapidTaps_With_LaggingScans_Still_End_As_A_Single_Hold_Then_Release()
        {
            var bridge = new SpectrumKeyInputBridge(8);
            int releaseCount = 0;

            for (int frame = 0; frame < 50; frame++)
            {
                bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, frame);
                bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, frame);

                foreach (var change in bridge.CollectStateChanges(_ => 0UL, frame))
                {
                    if (!change.Pressed)
                        releaseCount++;
                }
            }

            for (int frame = 50; frame < 120; frame++)
            {
                foreach (var change in bridge.CollectStateChanges(_ => 0UL, frame))
                {
                    if (!change.Pressed)
                        releaseCount++;
                }
            }

            Assert.Equal(1, releaseCount);
        }

        [Fact]
        public void TwoQuickTaps_Before_FirstScan_Behave_Like_One_Longer_Hold()
        {
            var bridge = new SpectrumKeyInputBridge(8);
            var releases = new List<int>();

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, 0);
            bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, 0);
            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, 1);
            bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, 1);

            for (int frame = 1; frame <= 4; frame++)
            {
                foreach (var change in bridge.CollectStateChanges(_ => 0UL, frame))
                {
                    if (!change.Pressed)
                        releases.Add(frame);
                }
            }

            Assert.Equal(new[] { 4 }, releases);
        }

        [Fact]
        public void TwoQuickTaps_With_InputEdgeScans_Still_Behave_Like_One_Hold()
        {
            var bridge = new SpectrumKeyInputBridge(8);
            int releaseCount = 0;

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, 0);
            foreach (var change in bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, 0))
            {
                if (!change.Pressed)
                    releaseCount++;
            }
            foreach (var change in bridge.CollectStateChanges(_ => 0UL, 0))
            {
                if (!change.Pressed)
                    releaseCount++;
            }

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, 1);
            foreach (var change in bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, 1))
            {
                if (!change.Pressed)
                    releaseCount++;
            }
            foreach (var change in bridge.CollectStateChanges(_ => 0UL, 1))
            {
                if (!change.Pressed)
                    releaseCount++;
            }

            Assert.Equal(0, releaseCount);

            Assert.Empty(bridge.CollectStateChanges(_ => 0UL, 2));

            foreach (var change in bridge.CollectStateChanges(_ => 0UL, 4))
            {
                if (!change.Pressed)
                    releaseCount++;
            }

            Assert.Equal(1, releaseCount);
        }

        [Fact]
        public void MenuCadence_QuickTap_Stays_Down_Until_Next_FrameScale_Sample()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 20);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0).ToArray());

            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 1));

            for (int tick = 1; tick < 20; tick++)
                Assert.Empty(bridge.CollectStateChanges(_ => 0UL, tick));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 20).ToArray());
        }

        [Fact]
        public void MenuCadence_QuickRetaps_Queue_Distinct_Menu_Pulses()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0).ToArray());
            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 1));

            Assert.Empty(bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 30));
            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 31));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 40).ToArray());
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.CollectStateChanges(_ => 0UL, 41).ToArray());
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 81).ToArray());
        }

        [Fact]
        public void CompositeKey_QuickRetap_WithinContinuationWindow_DoesNotReleaseBetweenTaps()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0);
            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 20));
            Assert.Empty(bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 30));
            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 31));

            for (int tick = 20; tick < 40; tick++)
                Assert.Empty(bridge.CollectStateChanges(_ => 0UL, tick));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 40).ToArray());

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.CollectStateChanges(_ => 0UL, 41).ToArray());

            for (int tick = 42; tick < 81; tick++)
                Assert.Empty(bridge.CollectStateChanges(_ => 0UL, tick));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 81).ToArray());
        }

        [Fact]
        public void SingleRowKey_ReleasesImmediatelyOnceHoldWindowHasElapsed()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 20, sameKeyContinuationTicks: 75);

            bridge.RegisterKeyDown(Keys.Enter, new[] { 6 }, _ => 0UL, 0);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Enter, false) },
                bridge.RegisterKeyUp(Keys.Enter, _ => 0UL, 25).ToArray());
        }

        [Fact]
        public void CompositeKey_RealisticRapidTapGaps_Remain_Distinct_Pulses()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0).ToArray());

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 80).ToArray());

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 130).ToArray());
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 210).ToArray());
        }

        [Fact]
        public void CompositeKey_OverlappingRetap_Queues_A_FollowOn_Pulse()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0);
            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 20));
            Assert.Empty(bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 30));
            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 50));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 40).ToArray());
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.CollectStateChanges(_ => 0UL, 41).ToArray());
            Assert.Empty(bridge.CollectStateChanges(_ => 0UL, 69));
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 81).ToArray());
        }

        [Fact]
        public void CompositeKey_Gap_Longer_Than_Continuation_Releases_Between_Taps()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0);
            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 80).ToArray());

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 260).ToArray());
        }

        [Fact]
        public void CompositeKey_FirstQuickTap_Holds_Long_Enough_To_Be_Observed()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0).ToArray());

            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 20));

            for (int tick = 20; tick < 40; tick++)
                Assert.Empty(bridge.CollectStateChanges(_ => 0UL, tick));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 40).ToArray());
        }

        [Fact]
        public void CompositeKey_SlowerRepeatedTaps_Remain_Distinct_And_Long_Enough_To_Be_Seen()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0).ToArray());

            Assert.Empty(bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 25));

            for (int tick = 25; tick < 40; tick++)
                Assert.Empty(bridge.CollectStateChanges(_ => 0UL, tick));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.CollectStateChanges(_ => 0UL, 40).ToArray());

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 170).ToArray());
        }

        [Fact]
        public void CompositeKey_HeldDown_Remains_Pressed_Until_KeyUp()
        {
            var bridge = new SpectrumKeyInputBridge(8, minHoldTicks: 40, sameKeyContinuationTicks: 90);

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, true) },
                bridge.RegisterKeyDown(Keys.Down, new[] { 0, 4 }, _ => 0UL, 0).ToArray());

            for (int tick = 1; tick < 120; tick++)
                Assert.Empty(bridge.CollectStateChanges(_ => 0UL, tick));

            Assert.Equal(
                new[] { new SpectrumKeyInputBridge.SpectrumKeyStateChange(Keys.Down, false) },
                bridge.RegisterKeyUp(Keys.Down, _ => 0UL, 120).ToArray());
        }
    }
}
