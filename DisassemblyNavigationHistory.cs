namespace Spectrum128kEmulator
{
    public sealed class DisassemblyNavigationHistory
    {
        private readonly List<ushort> entries = new();
        private int currentIndex = -1;

        public bool CanGoBack => currentIndex > 0;
        public bool CanGoForward => currentIndex >= 0 && currentIndex < entries.Count - 1;
        public ushort? Current => currentIndex >= 0 ? entries[currentIndex] : null;

        public bool NavigateTo(ushort address)
        {
            if (Current == address)
                return false;

            if (CanGoForward)
                entries.RemoveRange(currentIndex + 1, entries.Count - currentIndex - 1);

            entries.Add(address);
            currentIndex = entries.Count - 1;
            return true;
        }

        public bool TryGoBack(out ushort address)
        {
            if (!CanGoBack)
            {
                address = 0;
                return false;
            }

            address = entries[--currentIndex];
            return true;
        }

        public bool TryGoForward(out ushort address)
        {
            if (!CanGoForward)
            {
                address = 0;
                return false;
            }

            address = entries[++currentIndex];
            return true;
        }
    }
}
