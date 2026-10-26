namespace Spectrum128kEmulator.Pokes
{
    public static class PokeApplicationService
    {
        public static IReadOnlyList<PokeWritePreview> Preview(
            Spectrum128Machine machine,
            IReadOnlyList<PokeWrite> writes)
        {
            ArgumentNullException.ThrowIfNull(machine);
            ValidateWrites(machine, writes);
            return writes.Select(write => new PokeWritePreview(write, Read(machine, write))).ToArray();
        }

        public static PokeApplication Apply(
            Spectrum128Machine machine,
            IReadOnlyList<PokeWrite> writes)
        {
            IReadOnlyList<PokeWritePreview> preview = Preview(machine, writes);
            var applied = new List<AppliedPokeWrite>(preview.Count);
            try
            {
                foreach (PokeWritePreview item in preview)
                {
                    Write(machine, item.Write, item.Write.Value);
                    applied.Add(new AppliedPokeWrite(item.Write, item.CurrentValue));
                }
            }
            catch
            {
                for (int index = applied.Count - 1; index >= 0; index--)
                    Write(machine, applied[index].Write, applied[index].OriginalValue);
                throw;
            }

            return new PokeApplication(DateTimeOffset.UtcNow, applied);
        }

        public static bool TryRestore(
            Spectrum128Machine machine,
            PokeApplication application,
            out string error)
        {
            ArgumentNullException.ThrowIfNull(machine);
            ArgumentNullException.ThrowIfNull(application);
            foreach (AppliedPokeWrite applied in application.Writes)
            {
                ValidateWrite(machine, applied.Write);
                byte current = Read(machine, applied.Write);
                if (current != applied.Write.Value)
                {
                    error = $"Cannot restore {applied.Write.Target}: current value {current:X2}H no longer matches applied value {applied.Write.Value:X2}H.";
                    return false;
                }
            }

            foreach (AppliedPokeWrite applied in application.Writes)
                Write(machine, applied.Write, applied.OriginalValue);
            error = string.Empty;
            return true;
        }

        private static void ValidateWrites(Spectrum128Machine machine, IReadOnlyList<PokeWrite> writes)
        {
            ArgumentNullException.ThrowIfNull(writes);
            if (writes.Count == 0)
                throw new ArgumentException("At least one poke write is required.", nameof(writes));

            var targets = new HashSet<(PokeMemorySpace Space, int Bank, ushort Address)>();
            foreach (PokeWrite write in writes)
            {
                ValidateWrite(machine, write);
                if (!targets.Add((write.Space, write.Bank, write.Address)))
                    throw new ArgumentException($"The poke writes {write.Target} more than once.", nameof(writes));
            }
        }

        private static void ValidateWrite(Spectrum128Machine machine, PokeWrite write)
        {
            if (write.Space == PokeMemorySpace.Mapped)
            {
                if (write.Address < 0x4000 || write.Bank != -1)
                    throw new ArgumentOutOfRangeException(nameof(write), "Mapped pokes must target RAM at 4000H-FFFFH without a bank number.");
                return;
            }

            if (write.Space != PokeMemorySpace.RamBank || write.Bank is < 0 or > 7 || write.Address > 0x3FFF)
                throw new ArgumentOutOfRangeException(nameof(write), "Physical pokes require RAM bank 0-7 and offset 0000H-3FFFH.");
            if (machine.MachineModel == SpectrumMachineModel.Spectrum48K && write.Bank is not (0 or 2 or 5))
                throw new InvalidOperationException($"RAM bank {write.Bank} is not part of the 48K model's physical memory map.");
        }

        private static byte Read(Spectrum128Machine machine, PokeWrite write) =>
            write.Space == PokeMemorySpace.Mapped
                ? machine.PeekMemory(write.Address)
                : machine.PeekRamBank(write.Bank, write.Address);

        private static void Write(Spectrum128Machine machine, PokeWrite write, byte value)
        {
            if (write.Space == PokeMemorySpace.Mapped)
                machine.PokeMemory(write.Address, value);
            else
                machine.PokeRamBank(write.Bank, write.Address, value);
        }
    }
}
