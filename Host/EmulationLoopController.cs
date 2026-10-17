namespace Spectrum128kEmulator
{
    public sealed class EmulationLoopController : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly EmulationPauseLeaseManager pauseLeases = new();
        private Task? loopTask;
        private volatile bool pauseRequested;
        private volatile bool loopPaused;

        public bool IsRunning => loopTask != null && !loopTask.IsCompleted;

        public void Start(Action<CancellationToken> loop)
        {
            if (loop == null)
                throw new ArgumentNullException(nameof(loop));
            if (loopTask != null)
                throw new InvalidOperationException("The emulation loop has already been started.");

            loopTask = Task.Run(() => loop(cancellation.Token));
        }

        public bool WaitWhilePaused(CancellationToken cancellationToken)
        {
            if (!pauseRequested)
            {
                loopPaused = false;
                return false;
            }

            loopPaused = true;
            cancellationToken.WaitHandle.WaitOne(1);
            return true;
        }

        public IDisposable AcquirePause(Action onFirstAcquire, Action onLastRelease)
        {
            if (loopTask == null || loopTask.IsCompleted)
                throw new InvalidOperationException("The emulation loop is not running.");

            IDisposable lease = pauseLeases.Acquire(
                () =>
                {
                    pauseRequested = true;
                    onFirstAcquire();
                },
                () =>
                {
                    onLastRelease();
                    pauseRequested = false;
                });

            SpinWait spinner = new();
            while (!loopPaused && !cancellation.IsCancellationRequested && !loopTask.IsCompleted)
                spinner.SpinOnce();

            if (!loopPaused)
            {
                lease.Dispose();
                throw new InvalidOperationException("The emulation loop ended before it acknowledged the pause request.");
            }

            return lease;
        }

        public void Dispose()
        {
            cancellation.Cancel();
            try
            {
                loopTask?.Wait(1000);
            }
            catch
            {
            }

            cancellation.Dispose();
        }
    }
}
