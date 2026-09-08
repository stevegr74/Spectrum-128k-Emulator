using Xunit;

namespace Spectrum128kEmulator.Tests
{
    public class EmulationPauseLeaseManagerTests
    {
        [Fact]
        public void NestedLeases_PauseOnceAndResumeAfterTheFinalRelease()
        {
            var manager = new EmulationPauseLeaseManager();
            int pauseCount = 0;
            int resumeCount = 0;

            using IDisposable outerLease = manager.Acquire(() => pauseCount++, () => resumeCount++);
            Assert.Equal(1, pauseCount);
            Assert.Equal(0, resumeCount);
            Assert.Equal(1, manager.ActiveLeaseCount);

            using (manager.Acquire(() => pauseCount++, () => resumeCount++))
            {
                Assert.Equal(1, pauseCount);
                Assert.Equal(0, resumeCount);
                Assert.Equal(2, manager.ActiveLeaseCount);
            }

            Assert.Equal(1, pauseCount);
            Assert.Equal(0, resumeCount);
            Assert.Equal(1, manager.ActiveLeaseCount);

            outerLease.Dispose();

            Assert.Equal(1, pauseCount);
            Assert.Equal(1, resumeCount);
            Assert.Equal(0, manager.ActiveLeaseCount);
        }

        [Fact]
        public void Lease_DisposeIsIdempotent()
        {
            var manager = new EmulationPauseLeaseManager();
            int resumeCount = 0;
            IDisposable lease = manager.Acquire(() => { }, () => resumeCount++);

            lease.Dispose();
            lease.Dispose();

            Assert.Equal(1, resumeCount);
            Assert.Equal(0, manager.ActiveLeaseCount);
        }
    }
}
