namespace Spectrum128kEmulator
{
    public partial class MainForm
    {
        private void LoadSnaSnapshotFromDialog()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Load .sna Snapshot",
                Filter = "Spectrum snapshots (*.sna)|*.sna|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            using IDisposable pauseLease = AcquireEmulationPauseLease();

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                ExecuteWithEmulationPaused(() =>
                {
                    ClearHostAndSpectrumInputState();
                    lock (machineLock)
                    {
                        Spectrum128Machine newMachine = CreateConfiguredMachine();
                        SnapshotLoader.Load(newMachine, dialog.FileName, selectedMachineModel);
                        machine = newMachine;
                    }
                    ResetFrameScheduler();
                    RecreateAudioPipeline();
                    lock (machineLock)
                        machine.ClearDebugHistory();
                });
                SetLoadedMediaDisplayName(dialog.FileName);
                SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
                PresentCurrentMachineFrame(frameClock.ElapsedTicks, force: true);
                screenBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Failed to load snapshot:\n{ex.Message}",
                    "Snapshot Load Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void MountTapFromDialog()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Mount Tape Image",
                Filter = "Spectrum tape images (*.tap;*.tzx)|*.tap;*.tzx|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            using IDisposable pauseLease = AcquireEmulationPauseLease();

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                string extension = Path.GetExtension(dialog.FileName);
                ExecuteWithEmulationPaused(() =>
                {
                    ClearHostAndSpectrumInputState();
                    if (extension.Equals(".tzx", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (machineLock)
                        {
                            Spectrum128Machine newMachine = CreateConfiguredMachine();
                            Tap.TzxLoader.LoadWithPolicy(newMachine, dialog.FileName);
                            machine = newMachine;
                        }
                    }
                    else
                    {
                        lock (machineLock)
                        {
                            Spectrum128Machine newMachine = CreateConfiguredMachine();
                            Tap.TapLoader.LoadWithPolicy(newMachine, dialog.FileName);
                            machine = newMachine;
                        }
                    }

                    ResetFrameScheduler();
                    RecreateAudioPipeline();
                    lock (machineLock)
                        machine.ClearDebugHistory();
                });
                SetLoadedMediaDisplayName(dialog.FileName);
                SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
                ShowMountedTapeTransportFeedback();
                PresentCurrentMachineFrame(frameClock.ElapsedTicks, force: true);
                screenBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Failed to mount tape:\n{ex.Message}",
                    "Tape Mount Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void ReplaceMountedTapeFromDialog()
        {
            lock (machineLock)
            {
                if (!machine.HasMountedTape)
                    return;
            }

            using var dialog = new OpenFileDialog
            {
                Title = "Replace Mounted Tape (Preserve Machine)",
                Filter = "Spectrum tape images (*.tap;*.tzx)|*.tap;*.tzx|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            using IDisposable pauseLease = AcquireEmulationPauseLease();

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                ExecuteWithEmulationPaused(() =>
                {
                    ClearHostAndSpectrumInputState();
                    ReplaceMountedTape(dialog.FileName);
                });

                SetLoadedMediaDisplayName(dialog.FileName);
                SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
                ShowMountedTapeTransportFeedback();
                PresentCurrentMachineFrame(frameClock.ElapsedTicks, force: true);
                screenBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Failed to replace tape:\n{ex.Message}",
                    "Tape Replacement Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private Tap.TapMountResult ReplaceMountedTape(string path)
        {
            string extension = Path.GetExtension(path);
            lock (machineLock)
            {
                if (!machine.HasMountedTape)
                    throw new InvalidOperationException("There is no mounted tape to replace.");

                return extension.Equals(".tzx", StringComparison.OrdinalIgnoreCase)
                    ? Tap.TzxLoader.Mount(machine, path)
                    : extension.Equals(".tap", StringComparison.OrdinalIgnoreCase)
                        ? Tap.TapLoader.Mount(machine, path)
                        : throw new InvalidOperationException("Tape replacement requires a .tap or .tzx image.");
            }
        }

        private void LoadSnapshotOrRecordingFromDialog()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Load Snapshot or RZX Recording",
                Filter = "Spectrum snapshots and recordings (*.z80;*.szx;*.rzx)|*.z80;*.szx;*.rzx|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            using IDisposable pauseLease = AcquireEmulationPauseLease();

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                string extension = Path.GetExtension(dialog.FileName);
                if (extension.Equals(".rzx", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteWithEmulationPaused(() =>
                    {
                        ClearHostAndSpectrumInputState();
                        lock (machineLock)
                        {
                            Spectrum128Machine newMachine = CreateConfiguredMachine();
                            RzxLoader.Load(newMachine, dialog.FileName);
                            machine = newMachine;
                        }
                        ResetFrameScheduler();
                        RecreateAudioPipeline();
                        lock (machineLock)
                            machine.ClearDebugHistory();
                    });
                }
                else if (extension.Equals(".szx", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteWithEmulationPaused(() =>
                    {
                        ClearHostAndSpectrumInputState();
                        lock (machineLock)
                        {
                            Spectrum128Machine newMachine = CreateConfiguredMachine();
                            SzxSnapshotLoader.Load(newMachine, dialog.FileName);
                            machine = newMachine;
                        }
                        ResetFrameScheduler();
                        RecreateAudioPipeline();
                        lock (machineLock)
                            machine.ClearDebugHistory();
                    });
                }
                else if (extension.Equals(".z80", StringComparison.OrdinalIgnoreCase))
                {
                    ExecuteWithEmulationPaused(() =>
                    {
                        ClearHostAndSpectrumInputState();
                        lock (machineLock)
                        {
                            Spectrum128Machine newMachine = CreateConfiguredMachine();
                            Z80SnapshotLoader.Load(newMachine, dialog.FileName, selectedMachineModel);
                            machine = newMachine;
                        }
                        ResetFrameScheduler();
                        RecreateAudioPipeline();
                        lock (machineLock)
                            machine.ClearDebugHistory();
                    });
                }
                else
                {
                    throw new InvalidOperationException("Select a .z80 or .szx snapshot, or an .rzx recording.");
                }
                SetLoadedMediaDisplayName(dialog.FileName);
                SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
                PresentCurrentMachineFrame(frameClock.ElapsedTicks, force: true);
                screenBox.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Failed to load snapshot:\n{ex.Message}",
                    "Snapshot Load Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
