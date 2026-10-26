using Spectrum128kEmulator.Pokes;

namespace Spectrum128kEmulator
{
    public partial class MainForm
    {
        private void OpenPokeManager()
        {
            using IDisposable pauseLease = AcquireEmulationPauseLease();
            PokeLibrary library = LoadPokeLibrary(out string? warning);
            if (warning != null)
            {
                MessageBox.Show(
                    this,
                    $"The poke library could not be loaded. One-off writes remain available.\n\n{warning}",
                    "Poke Library Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            using var form = new PokeManagerForm(
                library,
                loadedMediaIdentity,
                selectedMachineModel,
                writes =>
                {
                    lock (machineLock)
                        return PokeApplicationService.Preview(machine, writes);
                },
                writes =>
                {
                    lock (machineLock)
                    {
                        PokeApplication application = PokeApplicationService.Apply(machine, writes);
                        pokeApplications.Push(application);
                        return application;
                    }
                },
                () => pokeApplications.Count > 0,
                RestoreLastPokeApplication);
            form.ShowDialog(this);
            PresentCurrentMachineFrame(frameClock.ElapsedTicks, force: true);
            screenBox.Focus();
        }

        private PokeLibrary LoadPokeLibrary(out string? warning)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Pokes", "pokes.json");
            try
            {
                warning = null;
                return File.Exists(path)
                    ? PokeLibrary.Load(path)
                    : PokeLibrary.Empty;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                warning = exception.Message;
                return PokeLibrary.Empty;
            }
        }

        private (bool Success, string Message) RestoreLastPokeApplication()
        {
            if (pokeApplications.Count == 0)
                return (false, "There is no session poke to restore.");

            lock (machineLock)
            {
                PokeApplication application = pokeApplications.Peek();
                if (!PokeApplicationService.TryRestore(machine, application, out string error))
                    return (false, error);
                pokeApplications.Pop();
            }

            return (true, "Restored the bytes changed by the most recent poke.");
        }
    }
}
