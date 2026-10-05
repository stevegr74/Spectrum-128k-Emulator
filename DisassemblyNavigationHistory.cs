namespace Spectrum128kEmulator
{
    public readonly record struct DisassemblyNavigationLocation(ushort ListingAddress, ushort SelectedAddress);

    public sealed class DisassemblyNavigationHistory
    {
        private readonly List<DisassemblyNavigationLocation> entries = new();
        private int currentIndex = -1;

        public bool CanGoBack => currentIndex > 0;
        public bool CanGoForward => currentIndex >= 0 && currentIndex < entries.Count - 1;
        public DisassemblyNavigationLocation? Current => currentIndex >= 0 ? entries[currentIndex] : null;

        public bool NavigateTo(ushort address)
        {
            return NavigateTo(new DisassemblyNavigationLocation(address, address));
        }

        public bool NavigateTo(DisassemblyNavigationLocation location)
        {
            if (Current == location)
                return false;

            if (CanGoForward)
                entries.RemoveRange(currentIndex + 1, entries.Count - currentIndex - 1);

            entries.Add(location);
            currentIndex = entries.Count - 1;
            return true;
        }

        public bool UpdateCurrentSelection(ushort selectedAddress)
        {
            if (currentIndex < 0 || entries[currentIndex].SelectedAddress == selectedAddress)
                return false;

            entries[currentIndex] = entries[currentIndex] with { SelectedAddress = selectedAddress };
            return true;
        }

        public bool TryGoBack(out DisassemblyNavigationLocation location)
        {
            if (!CanGoBack)
            {
                location = default;
                return false;
            }

            location = entries[--currentIndex];
            return true;
        }

        public bool TryGoForward(out DisassemblyNavigationLocation location)
        {
            if (!CanGoForward)
            {
                location = default;
                return false;
            }

            location = entries[++currentIndex];
            return true;
        }
    }
}
