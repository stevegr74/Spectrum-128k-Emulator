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
                fpsLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
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
                Title = "Mount .tap Tape Image",
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
                string displayName = Path.GetFileName(dialog.FileName);
                ExecuteWithEmulationPaused(() =>
                {
                    ClearHostAndSpectrumInputState();
                    if (extension.Equals(".tzx", StringComparison.OrdinalIgnoreCase))
                    {
                        Tap.TapeExecutionResult result;
                        lock (machineLock)
                        {
                            Spectrum128Machine newMachine = CreateConfiguredMachine();
                            result = Tap.TzxLoader.LoadWithPolicy(newMachine, dialog.FileName);
                            machine = newMachine;
                        }
                        fpsLabel.Text = DescribeTapeExecution("TZX", displayName, result);
                    }
                    else
                    {
                        Tap.TapeExecutionResult result;
                        lock (machineLock)
                        {
                            Spectrum128Machine newMachine = CreateConfiguredMachine();
                            result = Tap.TapLoader.LoadWithPolicy(newMachine, dialog.FileName);
                            machine = newMachine;
                        }
                        fpsLabel.Text = DescribeTapeExecution("TAP", displayName, result);
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

        private static string DescribeTapeExecution(string format, string displayName, Tap.TapeExecutionResult result)
        {
            return result.Strategy switch
            {
                Tap.TapeLoadStrategy.FullFakeLoad =>
                    $"{format} loaded: {displayName} ({result.ConsumedBlockCount}/{result.TotalBlockCount} blocks)",
                Tap.TapeLoadStrategy.BootstrapHybrid =>
                    $"{format} bootstrapped: {displayName} ({result.ConsumedBlockCount}/{result.TotalBlockCount} blocks)",
                _ =>
                    $"{format} mounted: {displayName} ({result.TotalBlockCount} blocks)"
            };
        }

        private void LoadSnapshotOrRecordingFromDialog()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Load .z80 Snapshot or .rzx Recording",
                Filter = "Z80 snapshots and recordings (*.z80;*.rzx)|*.z80;*.rzx|All files (*.*)|*.*",
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
                else
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
                SetLoadedMediaDisplayName(dialog.FileName);
                fpsLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
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
