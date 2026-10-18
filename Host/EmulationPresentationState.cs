namespace Spectrum128kEmulator
{
    public sealed class EmulationPresentationState
    {
        private readonly object sync = new();
        private readonly byte[] screenBankData = new byte[0x4000];
        private int borderColor;
        private BorderFrame? borderFrame;
        private UlaFrame? ulaFrame;
        private bool flashPhase;
        private bool hasValue;

        public bool HasValue
        {
            get
            {
                lock (sync)
                    return hasValue;
            }
        }

        public void Publish(Spectrum128Machine machine)
        {
            if (machine == null)
                throw new ArgumentNullException(nameof(machine));

            byte[] source = machine.GetScreenBankData();
            lock (sync)
            {
                Buffer.BlockCopy(source, 0, screenBankData, 0, screenBankData.Length);
                borderColor = machine.BorderColor;
                borderFrame = machine.LastCompletedBorderFrame;
                ulaFrame = machine.LastCompletedUlaFrame;
                flashPhase = machine.FlashPhase;
                hasValue = true;
            }
        }

        public bool TryCapture(out EmulationPresentationSnapshot snapshot)
        {
            lock (sync)
            {
                if (!hasValue)
                {
                    snapshot = default;
                    return false;
                }

                snapshot = new EmulationPresentationSnapshot(
                    (byte[])screenBankData.Clone(),
                    borderColor,
                    borderFrame,
                    ulaFrame,
                    flashPhase);
                return true;
            }
        }
    }

    public readonly record struct EmulationPresentationSnapshot(
        byte[] ScreenBankData,
        int BorderColor,
        BorderFrame? BorderFrame,
        UlaFrame? UlaFrame,
        bool FlashPhase);
}
