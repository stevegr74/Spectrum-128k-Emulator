//#define EXTENDED_DEBUG
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Spectrum128kEmulator.Audio;

namespace Spectrum128kEmulator
{
    public partial class MainForm : Form
    {
        private static readonly bool LogFrameDiagnostics = false;
        private static readonly bool LogUnimplementedOpcodes = true;
        private static readonly bool LogPagingWrites = false;
        private static readonly bool EnableInputDiagnostics = false;
        private static readonly bool EnablePerformanceDiagnostics = false;
        private const int InputPollingSliceMilliseconds = 1;
        private const int TurboTapeLoadFactor = 8;
        private const int ProtectedStreamTurboTapeLoadFactor = 2;
        private const int MinTurboTapeLoadFactor = 1;
        private const double TurboTickSlowThresholdMilliseconds = 12.0;
        private const double TurboTickRecoverThresholdMilliseconds = 6.0;
        private const int PostLoadInputSuppressionMilliseconds = 250;

        private int framesRenderedThisSecond;
        private int displayedFps;
        private int displayedFrameCount;
        private long lastStatsTicks;
        private readonly System.Diagnostics.Stopwatch frameClock = System.Diagnostics.Stopwatch.StartNew();
        private long lastSchedulerTicks;
        private long lastPresentationTicks;
        private double accumulatedEmulationTStates;
        private int currentTurboTapeLoadFactor = TurboTapeLoadFactor;
        private const int MaxCatchUpFramesPerTick = 2;
        private const int PresentationFramesPerSecond = 50;
        private static readonly long PresentationIntervalTicks =
            System.Diagnostics.Stopwatch.Frequency / PresentationFramesPerSecond;
        private readonly Bitmap screenBitmap = new Bitmap(Spectrum128Machine.ScreenWidth, Spectrum128Machine.ScreenHeight, PixelFormat.Format32bppArgb);
        private readonly System.Windows.Forms.Timer frameTimer = new System.Windows.Forms.Timer { Interval = 1 };
        private readonly PictureBox screenBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.StretchImage,
            TabStop = true
        };
        private readonly Label fpsLabel = new Label();

        private readonly string romFolder;
        private Spectrum128Machine machine;
        private readonly object machineLock = new();
        private readonly object inputStateLock = new();
        private readonly object presentationStateLock = new();
        private readonly object audioPipelineLock = new();
        private readonly HashSet<Keys> pressedSpectrumKeys = new();
        private readonly Queue<SpectrumHostKeyEvent> pendingSpectrumHostKeyEvents = new();
        private readonly SpectrumKeyInputBridge spectrumKeyInputBridge = new(8, 40, 90);
        private readonly CancellationTokenSource emulationLoopCts = new();
        private Task? emulationLoopTask;
        private volatile bool pauseEmulationRequested;
        private volatile bool emulationLoopPaused;
        private AudioPipeline audioPipeline;
        private int inputBridgeTick;
        private readonly object inputDiagnosticsLock = new();
        private StreamWriter? inputDiagnosticsWriter;
        private string? inputDiagnosticsPath;
        private readonly object performanceDiagnosticsLock = new();
        private StreamWriter? performanceDiagnosticsWriter;
        private long lastPerformanceStatsTicks;
        private int performanceTickCount;
        private int performancePresentedFrames;
        private int performanceCompletedFrames;
        private double performanceExecuteSliceMilliseconds;
        private double performanceAudioSubmitMilliseconds;
        private double performanceRenderMilliseconds;
        private double performancePresentMilliseconds;
        private readonly byte[] latestScreenBankData = new byte[0x4000];
        private int latestBorderColor;
        private bool latestFlashPhase;
        private int totalPresentedFrameCount;
        private bool hasPresentationState;
        private long suppressSpectrumHostInputUntilTicks;

        public MainForm()
        {
            Text = "Spectrum 128K Emulator";
            ClientSize = new Size(512, 384);
            Controls.Add(screenBox);

            fpsLabel.Text = "FPS=0 Frame=0";
            fpsLabel.AutoSize = true;
            fpsLabel.ForeColor = Color.White;
            fpsLabel.BackColor = Color.Black;
            fpsLabel.Location = new Point(5, 5);

            Controls.Add(fpsLabel);
            fpsLabel.BringToFront();

            romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
            machine = CreateConfiguredMachine();
            audioPipeline = CreateAudioPipeline();
            InitializeKeyboard();
            InitializeInputDiagnostics();
            InitializePerformanceDiagnostics();
            long now = frameClock.ElapsedTicks;
            lastSchedulerTicks = now;
            lastPresentationTicks = now;
            accumulatedEmulationTStates = 0;
            PublishPresentationState();
            emulationLoopTask = Task.Run(() => EmulationLoop(emulationLoopCts.Token));
            frameTimer.Tick += FrameTimer_Tick;
            frameTimer.Start();
            lastStatsTicks = now;
            lastPerformanceStatsTicks = now;
            Console.WriteLine("=== Emulator started - ROM loaded - CPU Reset ===");
        }

        private static AudioPipeline CreateAudioPipeline()
        {
            try
            {
                return new AudioPipeline(new WaveOutAudioOutput());
            }
            catch
            {
                return new AudioPipeline(new NullAudioOutput(44100));
            }
        }

        private Spectrum128Machine CreateConfiguredMachine()
        {
            var configuredMachine = new Spectrum128Machine(romFolder);
            configuredMachine.Trace = s =>
            {
                if ((LogUnimplementedOpcodes && s.StartsWith("UNIMPL")) ||
                    (LogPagingWrites && s.StartsWith("[7FFD]")))
                {
                    Console.WriteLine(s);
                    Console.Out.Flush();
                }
            };
            return configuredMachine;
        }

        private void InitializeKeyboard()
        {
            KeyPreview = true;
            PreviewKeyDown += MainForm_PreviewKeyDown;
            KeyDown += MainForm_KeyDown;
            KeyUp += MainForm_KeyUp;
            Deactivate += MainForm_Deactivate;
            screenBox.MouseClick += (_, _) => screenBox.Focus();
            screenBox.PreviewKeyDown += MainForm_PreviewKeyDown;
            Shown += (_, _) => screenBox.Focus();
        }

        private void MainForm_Deactivate(object? sender, EventArgs e)
        {
            LogInputDiagnostic("deactivate", null, "clearing input state");
            ClearHostAndSpectrumInputState();
        }

        private void MainForm_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (IsSpectrumMappedKey(e.KeyCode))
                e.IsInputKey = true;
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (IsSpectrumMappedKey(e.KeyCode))
            {
                LogInputDiagnostic("keydown", e.KeyCode, "mapped event");
                QueueSpectrumHostKeyEvent(e.KeyCode, true);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            HandleKey(e.KeyCode, true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void MainForm_KeyUp(object? sender, KeyEventArgs e)
        {
            if (IsSpectrumMappedKey(e.KeyCode))
            {
                LogInputDiagnostic("keyup", e.KeyCode, "mapped event");
                QueueSpectrumHostKeyEvent(e.KeyCode, false);
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            HandleKey(e.KeyCode, false);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void HandleKey(Keys key, bool pressed)
        {
            if (pressed && key == Keys.F9)
            {
                LoadSnaSnapshotFromDialog();
                return;
            }

            if (pressed && key == Keys.F10)
            {
                LoadSnapshotOrRecordingFromDialog();
                return;
            }

            if (pressed && key == Keys.F11)
            {
                MountTapFromDialog();
                return;
            }

            if (pressed && key == Keys.F12)
            {
                DumpMachineDebugState();
                return;
            }

            QueueSpectrumHostKeyEvent(key, pressed);
        }

        private bool IsSpectrumMappedKey(Keys key)
        {
            return key switch
            {
                Keys.Left or Keys.Down or Keys.Up or Keys.Right or Keys.Back => true,
                Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => true,
                Keys.Z or Keys.X or Keys.C or Keys.V => true,
                Keys.A or Keys.S or Keys.D or Keys.F or Keys.G => true,
                Keys.Q or Keys.W or Keys.E or Keys.R or Keys.T => true,
                Keys.D1 or Keys.NumPad1 or Keys.D2 or Keys.NumPad2 or Keys.D3 or Keys.NumPad3 or
                Keys.D4 or Keys.NumPad4 or Keys.D5 or Keys.NumPad5 => true,
                Keys.D0 or Keys.NumPad0 or Keys.D9 or Keys.NumPad9 or Keys.D8 or Keys.NumPad8 or
                Keys.D7 or Keys.NumPad7 or Keys.D6 or Keys.NumPad6 => true,
                Keys.P or Keys.O or Keys.I or Keys.U or Keys.Y => true,
                Keys.Enter or Keys.L or Keys.K or Keys.J or Keys.H => true,
                Keys.Space or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
                Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.M or Keys.N or Keys.B => true,
                _ => false
            };
        }

        private int[] GetSpectrumKeyRows(Keys key)
        {
            return key switch
            {
                Keys.Left => new[] { 0, 3 },
                Keys.Down => new[] { 0, 4 },
                Keys.Up => new[] { 0, 4 },
                Keys.Right => new[] { 0, 4 },
                Keys.Back => new[] { 0, 4 },
                Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or
                Keys.Z or Keys.X or Keys.C or Keys.V => new[] { 0 },
                Keys.A or Keys.S or Keys.D or Keys.F or Keys.G => new[] { 1 },
                Keys.Q or Keys.W or Keys.E or Keys.R or Keys.T => new[] { 2 },
                Keys.D1 or Keys.NumPad1 or Keys.D2 or Keys.NumPad2 or Keys.D3 or Keys.NumPad3 or
                Keys.D4 or Keys.NumPad4 or Keys.D5 or Keys.NumPad5 => new[] { 3 },
                Keys.D0 or Keys.NumPad0 or Keys.D9 or Keys.NumPad9 or Keys.D8 or Keys.NumPad8 or
                Keys.D7 or Keys.NumPad7 or Keys.D6 or Keys.NumPad6 => new[] { 4 },
                Keys.P or Keys.O or Keys.I or Keys.U or Keys.Y => new[] { 5 },
                Keys.Enter or Keys.L or Keys.K or Keys.J or Keys.H => new[] { 6 },
                Keys.Space or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or
                Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.M or Keys.N or Keys.B => new[] { 7 },
                _ => Array.Empty<int>()
            };
        }

        private void ProcessPendingSpectrumKeyReleases()
        {
            foreach (var change in spectrumKeyInputBridge.CollectStateChanges(machine.GetKeyboardRowScanCount, inputBridgeTick))
            {
                LogInputDiagnostic("bridge-pending-change", change.Key, $"pressed={change.Pressed} {DescribeBridgeState(change.Key)}");
                ApplySpectrumKeyState(change.Key, change.Pressed);
            }
        }

        private void QueueSpectrumHostKeyEvent(Keys key, bool isDown)
        {
            if (frameClock.ElapsedTicks < Volatile.Read(ref suppressSpectrumHostInputUntilTicks))
            {
                LogInputDiagnostic("queue-suppressed", key, $"isDown={isDown}");
                return;
            }

            lock (inputStateLock)
            {
                pendingSpectrumHostKeyEvents.Enqueue(new SpectrumHostKeyEvent(key, isDown));
            }
        }

        private void SuppressSpectrumHostInputForMilliseconds(int milliseconds)
        {
            if (milliseconds <= 0)
                return;

            long durationTicks = milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000;
            Volatile.Write(ref suppressSpectrumHostInputUntilTicks, frameClock.ElapsedTicks + durationTicks);
        }

        private void ResetInputBridgeState()
        {
            lock (inputStateLock)
            {
                pressedSpectrumKeys.Clear();
                pendingSpectrumHostKeyEvents.Clear();
                spectrumKeyInputBridge.Reset();
            }
        }

        private void DrainPendingSpectrumHostKeyEvents()
        {
            while (true)
            {
                SpectrumHostKeyEvent? keyEvent = null;
                lock (inputStateLock)
                {
                    if (pendingSpectrumHostKeyEvents.Count == 0)
                        break;

                    keyEvent = pendingSpectrumHostKeyEvents.Dequeue();
                }

                UpdateSpectrumMappedKeyState(keyEvent.Value.Key, keyEvent.Value.IsDown);
            }
        }

        private bool UpdateSpectrumMappedKeyState(Keys key, bool isDown)
        {
            bool previousState = pressedSpectrumKeys.Contains(key);
            if (previousState == isDown)
            {
                LogInputDiagnostic("update-skip", key, $"isDown={isDown} previousState={previousState} {DescribeBridgeState(key)}");
                return false;
            }

            if (isDown)
                pressedSpectrumKeys.Add(key);
            else
                pressedSpectrumKeys.Remove(key);

            int[] rows = GetSpectrumKeyRows(key);
            LogInputDiagnostic("update-begin", key, $"isDown={isDown} previousState={previousState} {DescribeBridgeState(key)}");
            if (rows.Length > 1)
            {
                foreach (var change in isDown
                    ? spectrumKeyInputBridge.RegisterKeyDown(key, rows, machine.GetKeyboardRowScanCount, inputBridgeTick)
                    : spectrumKeyInputBridge.RegisterKeyUp(key, machine.GetKeyboardRowScanCount, inputBridgeTick))
                {
                    LogInputDiagnostic("bridge-change", change.Key, $"pressed={change.Pressed} source={(isDown ? "keydown" : "keyup")} {DescribeBridgeState(change.Key)}");
                    ApplySpectrumKeyState(change.Key, change.Pressed);
                }
            }
            else
            {
                LogInputDiagnostic("bridge-bypass", key, $"pressed={isDown} source={(isDown ? "keydown" : "keyup")}");
                ApplySpectrumKeyState(key, isDown);
            }

            LogInputDiagnostic("update-end", key, $"isDown={isDown} {DescribeBridgeState(key)}");

            return true;
        }

        private void ApplySpectrumKeyState(Keys key, bool pressed)
        {
            LogInputDiagnostic("apply", key, $"pressed={pressed}");
            switch (key)
            {
                case Keys.Left:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(3, 4, pressed);
                    break;
                case Keys.Down:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 4, pressed);
                    break;
                case Keys.Up:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 3, pressed);
                    break;
                case Keys.Right:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 2, pressed);
                    break;
                case Keys.Back:
                    machine.SetKey(0, 0, pressed);
                    machine.SetKey(4, 0, pressed);
                    break;

                case Keys.ShiftKey:
                case Keys.LShiftKey:
                case Keys.RShiftKey:
                    machine.SetKey(0, 0, pressed); break;
                case Keys.Z:
                    machine.SetKey(0, 1, pressed); break;
                case Keys.X:
                    machine.SetKey(0, 2, pressed); break;
                case Keys.C:
                    machine.SetKey(0, 3, pressed); break;
                case Keys.V:
                    machine.SetKey(0, 4, pressed); break;

                case Keys.A:
                    machine.SetKey(1, 0, pressed); break;
                case Keys.S:
                    machine.SetKey(1, 1, pressed); break;
                case Keys.D:
                    machine.SetKey(1, 2, pressed); break;
                case Keys.F:
                    machine.SetKey(1, 3, pressed); break;
                case Keys.G:
                    machine.SetKey(1, 4, pressed); break;

                case Keys.Q:
                    machine.SetKey(2, 0, pressed); break;
                case Keys.W:
                    machine.SetKey(2, 1, pressed); break;
                case Keys.E:
                    machine.SetKey(2, 2, pressed); break;
                case Keys.R:
                    machine.SetKey(2, 3, pressed); break;
                case Keys.T:
                    machine.SetKey(2, 4, pressed); break;

                case Keys.D1:
                case Keys.NumPad1:
                    machine.SetKey(3, 0, pressed); break;
                case Keys.D2:
                case Keys.NumPad2:
                    machine.SetKey(3, 1, pressed); break;
                case Keys.D3:
                case Keys.NumPad3:
                    machine.SetKey(3, 2, pressed); break;
                case Keys.D4:
                case Keys.NumPad4:
                    machine.SetKey(3, 3, pressed); break;
                case Keys.D5:
                case Keys.NumPad5:
                    machine.SetKey(3, 4, pressed); break;

                case Keys.D0:
                case Keys.NumPad0:
                    machine.SetKey(4, 0, pressed); break;
                case Keys.D9:
                case Keys.NumPad9:
                    machine.SetKey(4, 1, pressed); break;
                case Keys.D8:
                case Keys.NumPad8:
                    machine.SetKey(4, 2, pressed); break;
                case Keys.D7:
                case Keys.NumPad7:
                    machine.SetKey(4, 3, pressed); break;
                case Keys.D6:
                case Keys.NumPad6:
                    machine.SetKey(4, 4, pressed); break;

                case Keys.P:
                    machine.SetKey(5, 0, pressed); break;
                case Keys.O:
                    machine.SetKey(5, 1, pressed); break;
                case Keys.I:
                    machine.SetKey(5, 2, pressed); break;
                case Keys.U:
                    machine.SetKey(5, 3, pressed); break;
                case Keys.Y:
                    machine.SetKey(5, 4, pressed); break;

                case Keys.Enter:
                    machine.SetKey(6, 0, pressed); break;
                case Keys.L:
                    machine.SetKey(6, 1, pressed); break;
                case Keys.K:
                    machine.SetKey(6, 2, pressed); break;
                case Keys.J:
                    machine.SetKey(6, 3, pressed); break;
                case Keys.H:
                    machine.SetKey(6, 4, pressed); break;

                case Keys.Space:
                    machine.SetKey(7, 0, pressed); break;
                case Keys.ControlKey:
                case Keys.LControlKey:
                case Keys.RControlKey:
                case Keys.Menu:
                case Keys.LMenu:
                case Keys.RMenu:
                    machine.SetKey(7, 1, pressed); break;
                case Keys.M:
                    machine.SetKey(7, 2, pressed); break;
                case Keys.N:
                    machine.SetKey(7, 3, pressed); break;
                case Keys.B:
                    machine.SetKey(7, 4, pressed); break;
            }
        }

        private void LoadSnaSnapshotFromDialog()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Load 48K .sna Snapshot",
                Filter = "Spectrum snapshots (*.sna)|*.sna|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

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
                        SnapshotLoader.LoadSna48k(newMachine, dialog.FileName);
                        machine = newMachine;
                    }
                    ResetFrameScheduler();
                    RecreateAudioPipeline();
                    lock (machineLock)
                        machine.ClearDebugHistory();
                });
                fpsLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
                SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
                PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
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
                SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
                PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
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
                            Z80SnapshotLoader.Load(newMachine, dialog.FileName);
                            machine = newMachine;
                        }
                        ResetFrameScheduler();
                        RecreateAudioPipeline();
                        lock (machineLock)
                            machine.ClearDebugHistory();
                    });
                }
                fpsLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
                PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
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

        private void DumpMachineDebugState()
        {
            try
            {
                string dump = string.Empty;
                ExecuteWithEmulationPaused(() =>
                {
                    dump = machine.BuildDebugDump("Manual F12 debug dump.");
                });
                WriteDebugDumpToFile(dump, "Manual F12");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Failed to write debug dump:\n{ex.Message}",
                    "Debug Dump Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void WriteDebugDumpToFile(string dump, string reason)
        {
            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            string fileName = $"machine-debug-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt";
            string path = Path.Combine(debugFolder, fileName);
            File.WriteAllText(path, dump);
            fpsLabel.Text = $"{reason}: {fileName}";
        }

        private void WriteCrashReport(Exception ex, string context)
        {
            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            string fileName = $"crash-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt";
            string path = Path.Combine(debugFolder, fileName);

            string report =
                $"Context: {context}{Environment.NewLine}" +
                $"Exception: {ex}{Environment.NewLine}{Environment.NewLine}" +
                BuildCrashDiagnosticDump(context);

            File.WriteAllText(path, report);
            fpsLabel.Text = $"Crash: {fileName}";
        }

        public string BuildCrashDiagnosticDump(string context)
        {
            try
            {
                string dump = string.Empty;
                ExecuteWithEmulationPaused(() =>
                {
                    dump = machine.BuildDebugDump($"Crash context: {context}");
                });
                return dump;
            }
            catch (Exception dumpEx)
            {
                return $"Failed to build machine dump: {dumpEx}";
            }
        }

        private void FrameTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                long now = frameClock.ElapsedTicks;
                PresentCurrentMachineFrame(now);
                UpdateStats(now);

            }
            catch (Exception ex)
            {
                frameTimer.Stop();
                try
                {
                    WriteCrashReport(ex, "FrameTimer_Tick");
                }
                catch
                {
                }

                MessageBox.Show(
                    this,
                    $"Unhandled emulator error:\n{ex.Message}",
                    "Emulator Crash",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void EmulationLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (pauseEmulationRequested)
                    {
                        emulationLoopPaused = true;
                        Thread.Sleep(1);
                        continue;
                    }

                    emulationLoopPaused = false;
                    long tickStartTicks = frameClock.ElapsedTicks;
                    List<AudioFrame>? completedFramesToSubmit = null;
                    string? autoReason = null;
                    string? autoDump = null;
                    bool hasAutoDebugDump = false;
                    int completedFrames = 0;
                    double executeSliceMilliseconds = 0;
                    double audioSubmitMilliseconds = 0;
                    bool presentedFrame = false;
                    Spectrum128Machine activeMachine = machine;
                    long now = frameClock.ElapsedTicks;
                long elapsedTicks = now - lastSchedulerTicks;
                if (elapsedTicks < 0)
                    elapsedTicks = 0;

                    lastSchedulerTicks = now;

                    accumulatedEmulationTStates +=
                        (double)elapsedTicks *
                        activeMachine.CurrentCpuClockHz *
                        GetEmulationSpeedMultiplier(activeMachine) /
                        System.Diagnostics.Stopwatch.Frequency;

                    int maxAccumulatedTStates = activeMachine.FrameTStates * MaxCatchUpFramesPerTick;
                    if (accumulatedEmulationTStates > maxAccumulatedTStates)
                        accumulatedEmulationTStates = maxAccumulatedTStates;

                int wholeTStatesBudget = (int)accumulatedEmulationTStates;
                    if (wholeTStatesBudget > 0)
                    {
                        int tStatesBudget = wholeTStatesBudget;
                        bool suppressAudioSubmission = ShouldSuppressAudioSubmissionDuringTurboTapeLoad(activeMachine);
                        if (suppressAudioSubmission)
                            activeMachine.SetAudioFrameCaptureEnabled(false);

                        long executeStartTicks = frameClock.ElapsedTicks;
                        try
                        {
                            int actualExecutedTStates = 0;
                            while (tStatesBudget > 0)
                            {
                                DrainPendingSpectrumHostKeyEvents();
                                bool executeWholeFrame = activeMachine.HasMountedTape && tStatesBudget >= activeMachine.FrameTStates;
                                int executedSliceTStates;
                                if (executeWholeFrame)
                                {
                                    int frameBudget = activeMachine.FrameTStates;
                                    int frameCountBefore = activeMachine.FrameCount;
                                    activeMachine.ExecuteFrame();
                                    completedFrames += activeMachine.FrameCount - frameCountBefore;
                                    executedSliceTStates = frameBudget;
                                }
                                else
                                {
                                    int tStatesPerSlice = Math.Max(1, activeMachine.CurrentCpuClockHz / 1000 * InputPollingSliceMilliseconds);
                                    int sliceBudget = Math.Min(tStatesBudget, tStatesPerSlice);
                                    completedFrames += activeMachine.ExecuteTimeSlice(sliceBudget, out executedSliceTStates);
                                }

                                actualExecutedTStates += executedSliceTStates;
                                inputBridgeTick++;
                                ProcessPendingSpectrumKeyReleases();

                                if (!suppressAudioSubmission)
                                {
                                    completedFramesToSubmit ??= new List<AudioFrame>();
                                    while (activeMachine.TryDequeueCompletedAudioFrame(out var completedAudioFrame))
                                        completedFramesToSubmit.Add(completedAudioFrame);
                                }
                                else
                                {
                                    while (activeMachine.TryDequeueCompletedAudioFrame(out _))
                                    {
                                    }
                                }

                                tStatesBudget -= executedSliceTStates;
                                if (executedSliceTStates <= 0)
                                    break;
                            }

                            accumulatedEmulationTStates -= actualExecutedTStates;
                            if (accumulatedEmulationTStates < 0)
                                accumulatedEmulationTStates = 0;
                        }
                        finally
                        {
                            if (suppressAudioSubmission)
                                activeMachine.SetAudioFrameCaptureEnabled(true);
                        }

                        executeSliceMilliseconds = (frameClock.ElapsedTicks - executeStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        hasAutoDebugDump = activeMachine.TryConsumeAutoDebugDump(out autoReason!, out autoDump!);
                    }

                    if (completedFrames > 0 || !hasPresentationState)
                        PublishPresentationState(activeMachine);

                    UpdateTurboTapeLoadFactor(activeMachine, tickStartTicks, frameClock.ElapsedTicks);
                    RecordPerformanceSample(frameClock.ElapsedTicks, completedFrames, presentedFrame, executeSliceMilliseconds, audioSubmitMilliseconds, 0, 0);

                    if (completedFramesToSubmit != null && completedFramesToSubmit.Count != 0)
                    {
                        long audioStartTicks = frameClock.ElapsedTicks;
                        lock (audioPipelineLock)
                        {
                            foreach (var completedAudioFrame in completedFramesToSubmit)
                                audioPipeline.SubmitFrame(completedAudioFrame);
                        }
                        audioSubmitMilliseconds = (frameClock.ElapsedTicks - audioStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                        RecordPerformanceSample(frameClock.ElapsedTicks, 0, false, 0, audioSubmitMilliseconds, 0, 0);
                    }

                    if (hasAutoDebugDump && !string.IsNullOrEmpty(autoDump))
                    {
                        try
                        {
                            BeginInvoke(new Action(() => WriteDebugDumpToFile(autoDump, autoReason ?? "Auto trap")));
                        }
                        catch
                        {
                        }
                    }

                    Thread.Sleep(1);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
                    Directory.CreateDirectory(debugFolder);
                    string fileName = $"emulation-loop-crash-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt";
                    string path = Path.Combine(debugFolder, fileName);
                    File.WriteAllText(path, $"Exception: {ex}{Environment.NewLine}");
                }
                catch
                {
                }
            }
        }

        private void ExecuteWithEmulationPaused(Action action)
        {
            pauseEmulationRequested = true;
            frameTimer.Stop();
            try
            {
                SpinWait spinner = new SpinWait();
                while (!emulationLoopPaused)
                    spinner.SpinOnce();

                action();
            }
            finally
            {
                long now = frameClock.ElapsedTicks;
                lastSchedulerTicks = now;
                lastPresentationTicks = now;
                pauseEmulationRequested = false;
                frameTimer.Start();
            }
        }

        private void ClearHostAndSpectrumInputState()
        {
            ResetInputBridgeState();
            lock (machineLock)
            {
                inputBridgeTick = 0;
                machine.ClearKeyboard();
            }
        }

        private void PublishPresentationState()
        {
            lock (machineLock)
                PublishPresentationState(machine);
        }

        private void PublishPresentationState(Spectrum128Machine sourceMachine)
        {
            byte[] screenBankData = sourceMachine.GetScreenBankData();
            lock (presentationStateLock)
            {
                Buffer.BlockCopy(screenBankData, 0, latestScreenBankData, 0, latestScreenBankData.Length);
                latestBorderColor = sourceMachine.BorderColor;
                latestFlashPhase = sourceMachine.FlashPhase;
                hasPresentationState = true;
            }
        }

        private void PresentCurrentMachineFrame(long now)
        {
            if (!ShouldPresentFrame(now))
                return;

            byte[] screenBankCopy = new byte[latestScreenBankData.Length];
            int borderColor;
            bool flashPhase;
            lock (presentationStateLock)
            {
                if (!hasPresentationState)
                    return;

                Buffer.BlockCopy(latestScreenBankData, 0, screenBankCopy, 0, latestScreenBankData.Length);
                borderColor = latestBorderColor;
                flashPhase = latestFlashPhase;
            }

            long renderStartTicks = frameClock.ElapsedTicks;
            SpectrumRenderer.RenderToBitmap(
                screenBitmap,
                screenBankCopy,
                borderColor,
                flashPhase);
            long renderEndTicks = frameClock.ElapsedTicks;

            screenBox.Image = screenBitmap;
            framesRenderedThisSecond++;
            totalPresentedFrameCount++;

            if (LogFrameDiagnostics && totalPresentedFrameCount % 20 == 0)
            {
                int nonZeroPixels = 0;
                int nonZeroAttrs = 0;

                for (int i = 0; i < 0x1800; i++)
                    if (screenBankCopy[i] != 0) nonZeroPixels++;

                for (int i = 0x1800; i < 0x1B00; i++)
                    if (screenBankCopy[i] != 0x38) nonZeroAttrs++;

                int screenWrites;
                int aboveWrites;
                int lastAboveWriteFrame;
                lock (machineLock)
                {
                    screenWrites = machine.ScreenWriteLog.Values.Sum();
                    aboveWrites = machine.AboveScreenWriteLog.Values.Sum();
                    lastAboveWriteFrame = machine.LastAboveWriteFrame;
                }

                string writeNote = lastAboveWriteFrame < machine.FrameCount - 20
                    ? " [WRITES STOPPED]"
                    : string.Empty;

                Console.WriteLine(
                    $"PresentedFrame {totalPresentedFrameCount}: Pixels={nonZeroPixels} Attrs={nonZeroAttrs} | ScreenAddr writes={screenWrites} AboveAddr writes={aboveWrites} LastWrite@MachineFrame{lastAboveWriteFrame}{writeNote}");

                Console.Out.Flush();
            }

            RecordPerformanceSample(
                now,
                0,
                true,
                0,
                0,
                (renderEndTicks - renderStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency,
                (frameClock.ElapsedTicks - renderEndTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        }

        private void ResetFrameScheduler()
        {
            long now = frameClock.ElapsedTicks;
            lock (machineLock)
            {
                lastSchedulerTicks = now;
                lastPresentationTicks = now;
                accumulatedEmulationTStates = 0;
                currentTurboTapeLoadFactor = TurboTapeLoadFactor;
                inputBridgeTick = 0;
            }
            PublishPresentationState();
            lastStatsTicks = now;
            framesRenderedThisSecond = 0;
            displayedFps = 0;
            displayedFrameCount = 0;
            totalPresentedFrameCount = 0;
            UpdateStatsLabel();
        }

        private bool ShouldPresentFrame(long nowTicks)
        {
            if (nowTicks - lastPresentationTicks < PresentationIntervalTicks)
                return false;

            long elapsed = nowTicks - lastPresentationTicks;
            long intervals = Math.Max(1, elapsed / PresentationIntervalTicks);
            lastPresentationTicks += intervals * PresentationIntervalTicks;
            return true;
        }

        private void RecreateAudioPipeline()
        {
            lock (audioPipelineLock)
            {
                try
                {
                    audioPipeline.Dispose();
                }
                catch
                {
                }

                audioPipeline = CreateAudioPipeline();
            }
        }

        private void UpdateStats(long nowTicks)
        {
            long ticksPerSecond = System.Diagnostics.Stopwatch.Frequency;

            if (nowTicks - lastStatsTicks >= ticksPerSecond)
            {
                displayedFps = framesRenderedThisSecond;
                displayedFrameCount = totalPresentedFrameCount;
                framesRenderedThisSecond = 0;
                lastStatsTicks = nowTicks;
                UpdateStatsLabel();
            }
        }

        private void UpdateStatsLabel()
        {
            fpsLabel.Text = $"FPS={displayedFps} Frame={displayedFrameCount}";
        }

        private int GetEmulationSpeedMultiplier(Spectrum128Machine activeMachine)
        {
            if (!IsTurboTapeLoadActive(activeMachine))
                return 1;

            int ceiling = activeMachine.MountedTape?.IsStreamingProtectedByteStream == true
                ? ProtectedStreamTurboTapeLoadFactor
                : TurboTapeLoadFactor;

            if (currentTurboTapeLoadFactor > ceiling)
                currentTurboTapeLoadFactor = ceiling;

            return Math.Min(currentTurboTapeLoadFactor, ceiling);
        }

        private bool ShouldSuppressAudioSubmissionDuringTurboTapeLoad(Spectrum128Machine activeMachine)
        {
            return IsTurboTapeLoadActive(activeMachine);
        }

        private bool IsTurboTapeLoadActive(Spectrum128Machine activeMachine)
        {
            return activeMachine.MountedTape?.IsActivelyStreamingEarSignal == true;
        }

        private void UpdateTurboTapeLoadFactor(Spectrum128Machine activeMachine, long tickStartTicks, long tickEndTicks)
        {
            if (!IsTurboTapeLoadActive(activeMachine))
            {
                currentTurboTapeLoadFactor = TurboTapeLoadFactor;
                return;
            }

            int ceiling = activeMachine.MountedTape?.IsStreamingProtectedByteStream == true
                ? ProtectedStreamTurboTapeLoadFactor
                : TurboTapeLoadFactor;

            double elapsedMilliseconds = (tickEndTicks - tickStartTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (elapsedMilliseconds >= TurboTickSlowThresholdMilliseconds)
            {
                currentTurboTapeLoadFactor = Math.Max(MinTurboTapeLoadFactor, currentTurboTapeLoadFactor - 1);
                return;
            }

            if (elapsedMilliseconds <= TurboTickRecoverThresholdMilliseconds)
                currentTurboTapeLoadFactor = Math.Min(ceiling, currentTurboTapeLoadFactor + 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                emulationLoopCts.Cancel();
                try
                {
                    emulationLoopTask?.Wait(1000);
                }
                catch
                {
                }
                lock (inputDiagnosticsLock)
                {
                    inputDiagnosticsWriter?.Dispose();
                    inputDiagnosticsWriter = null;
                }
                lock (performanceDiagnosticsLock)
                {
                    performanceDiagnosticsWriter?.Dispose();
                    performanceDiagnosticsWriter = null;
                }
                lock (audioPipelineLock)
                    audioPipeline.Dispose();
                screenBitmap.Dispose();
                frameTimer.Dispose();
                emulationLoopCts.Dispose();
            }

            base.Dispose(disposing);
        }

        private void InitializeInputDiagnostics()
        {
            if (!EnableInputDiagnostics)
                return;

            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            inputDiagnosticsPath = Path.Combine(debugFolder, $"input-diagnostics-{DateTime.Now:yyyyMMdd-HHmmssfff}.log");
            inputDiagnosticsWriter = new StreamWriter(new FileStream(inputDiagnosticsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
            LogInputDiagnostic("init", null, $"path={inputDiagnosticsPath}");
        }

        private void InitializePerformanceDiagnostics()
        {
            if (!EnablePerformanceDiagnostics)
                return;

            string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
            Directory.CreateDirectory(debugFolder);
            string performancePath = Path.Combine(debugFolder, $"performance-diagnostics-{DateTime.Now:yyyyMMdd-HHmmssfff}.log");
            performanceDiagnosticsWriter = new StreamWriter(new FileStream(performancePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true
            };
        }

        private string DescribeBridgeState(Keys key)
        {
            return spectrumKeyInputBridge.DescribeKeyState(key, machine.GetKeyboardRowScanCount);
        }

        private string DescribeKeyboardMatrix(Keys key)
        {
            int[] rows = GetSpectrumKeyRows(key);
            if (rows.Length == 0)
                return "matrixRows=[]";

            byte[] matrix = machine.GetKeyboardMatrixCopy();
            return "matrixRows=[" + string.Join(",", rows.Select(row => $"{row}:{matrix[row]:X2}/scan={machine.GetKeyboardRowScanCount(row)}")) + "]";
        }

        private void LogInputDiagnostic(string phase, Keys? key, string extra)
        {
            if (!EnableInputDiagnostics)
                return;

            lock (inputDiagnosticsLock)
            {
                if (inputDiagnosticsWriter == null)
                    return;

                string keyPart = key.HasValue ? $" key={key.Value}" : string.Empty;
                string matrixPart = key.HasValue ? $" {DescribeKeyboardMatrix(key.Value)}" : string.Empty;
                inputDiagnosticsWriter.WriteLine(
                    $"{DateTime.Now:HH:mm:ss.fff} phase={phase}{keyPart} tick={inputBridgeTick} frame={machine.FrameCount} tstates={machine.Cpu.TStates} pc=0x{machine.Cpu.Regs.PC:X4}{matrixPart} {extra}");
            }
        }

        private void RecordPerformanceSample(
            long nowTicks,
            int completedFrames,
            bool presentedFrame,
            double executeSliceMilliseconds,
            double audioSubmitMilliseconds,
            double renderMilliseconds,
            double presentMilliseconds)
        {
            if (!EnablePerformanceDiagnostics)
                return;

            lock (performanceDiagnosticsLock)
            {
                performanceTickCount++;
                performanceCompletedFrames += completedFrames;
                if (presentedFrame)
                    performancePresentedFrames++;
                performanceExecuteSliceMilliseconds += executeSliceMilliseconds;
                performanceAudioSubmitMilliseconds += audioSubmitMilliseconds;
                performanceRenderMilliseconds += renderMilliseconds;
                performancePresentMilliseconds += presentMilliseconds;

                long ticksPerSecond = System.Diagnostics.Stopwatch.Frequency;
                if (nowTicks - lastPerformanceStatsTicks < ticksPerSecond || performanceDiagnosticsWriter == null)
                    return;

                performanceDiagnosticsWriter.WriteLine(
                    $"{DateTime.Now:HH:mm:ss.fff} ticks={performanceTickCount} completedFrames={performanceCompletedFrames} presentedFrames={performancePresentedFrames} " +
                    $"turboActive={IsTurboTapeLoadActive(machine)} protected={machine.MountedTape?.IsStreamingProtectedByteStream == true} " +
                    $"execMs={performanceExecuteSliceMilliseconds:F2} audioMs={performanceAudioSubmitMilliseconds:F2} renderMs={performanceRenderMilliseconds:F2} presentMs={performancePresentMilliseconds:F2} " +
                    $"fpsLabel={framesRenderedThisSecond} frame={machine.FrameCount}");

                performanceTickCount = 0;
                performanceCompletedFrames = 0;
                performancePresentedFrames = 0;
                performanceExecuteSliceMilliseconds = 0;
                performanceAudioSubmitMilliseconds = 0;
                performanceRenderMilliseconds = 0;
                performancePresentMilliseconds = 0;
                lastPerformanceStatsTicks = nowTicks;
            }
        }

        private readonly record struct SpectrumHostKeyEvent(Keys Key, bool IsDown);
    }
}
