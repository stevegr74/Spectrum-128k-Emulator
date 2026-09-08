namespace Spectrum128kEmulator
{
    public sealed class EmulationPauseLeaseManager
    {
        private int activeLeaseCount;

        public int ActiveLeaseCount => Volatile.Read(ref activeLeaseCount);

        public IDisposable Acquire(Action onFirstAcquire, Action onLastRelease)
        {
            if (onFirstAcquire == null)
                throw new ArgumentNullException(nameof(onFirstAcquire));
            if (onLastRelease == null)
                throw new ArgumentNullException(nameof(onLastRelease));

            if (Interlocked.Increment(ref activeLeaseCount) == 1)
                onFirstAcquire();

            return new PauseLease(this, onLastRelease);
        }

        private void Release(Action onLastRelease)
        {
            int remainingLeases = Interlocked.Decrement(ref activeLeaseCount);
            if (remainingLeases < 0)
            {
                Interlocked.Increment(ref activeLeaseCount);
                throw new InvalidOperationException("A pause lease was released more than once.");
            }

            if (remainingLeases == 0)
                onLastRelease();
        }

        private sealed class PauseLease : IDisposable
        {
            private readonly EmulationPauseLeaseManager owner;
            private Action? onLastRelease;

            public PauseLease(EmulationPauseLeaseManager owner, Action onLastRelease)
            {
                this.owner = owner;
                this.onLastRelease = onLastRelease;
            }

            public void Dispose()
            {
                Action? releaseAction = Interlocked.Exchange(ref onLastRelease, null);
                if (releaseAction != null)
                    owner.Release(releaseAction);
            }
        }
    }
}
