using System;
using System.Threading;
using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public sealed class EmulationLoopControllerTests
    {
        [Fact]
        public void AcquirePause_WaitsForLoopAndSupportsNestedLeases()
        {
            using var controller = new EmulationLoopController();
            using var loopStarted = new ManualResetEventSlim();
            int iterations = 0;
            controller.Start(cancellationToken =>
            {
                loopStarted.Set();
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (controller.WaitWhilePaused(cancellationToken))
                        continue;

                    Interlocked.Increment(ref iterations);
                }
            });

            Assert.True(loopStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref iterations) > 0, TimeSpan.FromSeconds(1)));

            int pauseCount = 0;
            int resumeCount = 0;
            using (IDisposable outer = controller.AcquirePause(() => pauseCount++, () => resumeCount++))
            {
                int pausedAt = Volatile.Read(ref iterations);
                Thread.Sleep(10);
                Assert.Equal(pausedAt, Volatile.Read(ref iterations));

                using IDisposable inner = controller.AcquirePause(() => pauseCount++, () => resumeCount++);
                Assert.Equal(1, pauseCount);
                Assert.Equal(0, resumeCount);
            }

            Assert.Equal(1, resumeCount);
            int resumedAt = Volatile.Read(ref iterations);
            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref iterations) > resumedAt,
                TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void Start_RejectsSecondLoop()
        {
            using var controller = new EmulationLoopController();
            controller.Start(cancellationToken => cancellationToken.WaitHandle.WaitOne());

            Assert.Throws<InvalidOperationException>(() => controller.Start(_ => { }));
        }

        [Fact]
        public void AcquirePause_RejectsControllerWithoutRunningLoop()
        {
            using var controller = new EmulationLoopController();

            Assert.Throws<InvalidOperationException>(() =>
                controller.AcquirePause(() => { }, () => { }));
        }
    }
}
