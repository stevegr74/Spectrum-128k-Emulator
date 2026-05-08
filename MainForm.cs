//#define EXTENDED_DEBUG
using System.Drawing.Imaging;
using System.IO;
using Spectrum128kEmulator.Audio;

namespace Spectrum128kEmulator
{
    public partial class MainForm : Form
    {
        private static readonly bool LogFrameDiagnostics = false;
        private static readonly bool LogUnimplementedOpcodes = true;
        private static readonly bool LogPagingWrites = false;
        private static readonly bool LogKeyEvents = false;
        private const int MaxDeferredSpectrumKeyReleaseFrames = 8;

        private int framesRenderedThisSecond;
        private long lastStatsTicks;
        private readonly System.Diagnostics.Stopwatch frameClock = System.Diagnostics.Stopwatch.StartNew();
        private long lastSchedulerTicks;
        private long accumulatedTicks;
        private readonly long ticksPerFrame = System.Diagnostics.Stopwatch.Frequency / 50;
        private const int MaxCatchUpFramesPerTick = 2;
        private readonly Bitmap screenBitmap = new Bitmap(Spectrum128Machine.ScreenWidth, Spectrum128Machine.ScreenHeight, PixelFormat.Format32bppArgb);
        private readonly System.Windows.Forms.Timer frameTimer = new System.Windows.Forms.Timer { Interval = 1 };
        private readonly PictureBox screenBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.StretchImage,
            TabStop = true
        };
        private readonly Label fpsLabel = new Label();

        private readonly Spectrum128Machine machine;
        private readonly HashSet<Keys> pressedSpectrumKeys = new();
        private readonly Dictionary<Keys, (int[] Rows, ulong[] ScanCounts)> activeSpectrumKeyScans = new();
        private readonly Dictionary<Keys, PendingSpectrumKeyRelease> pendingSpectrumKeyReleases = new();
        private AudioPipeline audioPipeline;

        private readonly record struct PendingSpectrumKeyRelease(int[] Rows, ulong[] ScanCounts, int ReleaseFrame);

        public MainForm()
        {
            Text = "Spectrum 128K Emulator";
            ClientSize = new Size(512, 384);
            Controls.Add(screenBox);

            fpsLabel.Text = "FPS=0";
            fpsLabel.AutoSize = true;
            fpsLabel.ForeColor = Color.White;
            fpsLabel.BackColor = Color.Black;
            fpsLabel.Location = new Point(5, 5);

            Controls.Add(fpsLabel);
            fpsLabel.BringToFront();

            string romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
            machine = new Spectrum128Machine(romFolder);
            audioPipeline = CreateAudioPipeline();
            machine.Trace = s =>
            {
                if ((LogUnimplementedOpcodes && s.StartsWith("UNIMPL")) ||
                    (LogPagingWrites && s.StartsWith("[7FFD]")))
                {
                    Console.WriteLine(s);
                    Console.Out.Flush();
                }
            };
            InitializeKeyboard();
            long now = frameClock.ElapsedTicks;
            lastSchedulerTicks = now;
            accumulatedTicks = 0;
            frameTimer.Tick += FrameTimer_Tick;
            frameTimer.Start();
            lastStatsTicks = now;
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
            pressedSpectrumKeys.Clear();
            activeSpectrumKeyScans.Clear();
            pendingSpectrumKeyReleases.Clear();
            machine.ClearKeyboard();
        }

        private void MainForm_PreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (IsSpectrumMappedKey(e.KeyCode))
                e.IsInputKey = true;
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (pressedSpectrumKeys.Contains(e.KeyCode))
            {
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
            pressedSpectrumKeys.Remove(e.KeyCode);
            HandleKey(e.KeyCode, false);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x0100;
            const int WM_SYSKEYDOWN = 0x0104;

            Keys keyCode = keyData & Keys.KeyCode;
            if ((msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN) &&
                IsSpectrumMappedKey(keyCode))
            {
                if (pressedSpectrumKeys.Add(keyCode))
                    HandleKey(keyCode, true);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void HandleKey(Keys key, bool pressed)
        {
            if (pressed && key == Keys.F9)
            {
                pressedSpectrumKeys.Clear();
                activeSpectrumKeyScans.Clear();
                pendingSpectrumKeyReleases.Clear();
                LoadSnaSnapshotFromDialog();
                return;
            }

            if (pressed && key == Keys.F10)
            {
                pressedSpectrumKeys.Clear();
                activeSpectrumKeyScans.Clear();
                pendingSpectrumKeyReleases.Clear();
                LoadSnapshotOrRecordingFromDialog();
                return;
            }

            if (pressed && key == Keys.F11)
            {
                pressedSpectrumKeys.Clear();
                activeSpectrumKeyScans.Clear();
                pendingSpectrumKeyReleases.Clear();
                MountTapFromDialog();
                return;
            }

            if (pressed && key == Keys.F12)
            {
                DumpMachineDebugState();
                return;
            }

            if (pressed)
            {
                pendingSpectrumKeyReleases.Remove(key);
                ApplySpectrumKeyState(key, true);

                int[] rows = GetSpectrumKeyRows(key);
                if (rows.Length != 0)
                {
                    ulong[] scanCounts = new ulong[rows.Length];
                    for (int i = 0; i < rows.Length; i++)
                        scanCounts[i] = machine.GetKeyboardRowScanCount(rows[i]);
                    activeSpectrumKeyScans[key] = (rows, scanCounts);
                }
            }
            else
            {
                if (!activeSpectrumKeyScans.TryGetValue(key, out var scanState) || scanState.Rows.Length == 0)
                {
                    ApplySpectrumKeyState(key, false);
                }
                else if (RowsScannedSinceKeyDown(scanState.Rows, scanState.ScanCounts))
                {
                    activeSpectrumKeyScans.Remove(key);
                    ApplySpectrumKeyState(key, false);
                }
                else
                {
                    pendingSpectrumKeyReleases[key] = new PendingSpectrumKeyRelease(
                        scanState.Rows,
                        scanState.ScanCounts,
                        machine.FrameCount);
                }
            }

            if (LogKeyEvents)
            {
                Console.WriteLine(
                    $"KEYEVENT key={key} pressed={pressed} PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} IFF1={machine.Cpu.IFF1} MATRIX={string.Join(" ", machine.GetKeyboardMatrixCopy().Select(b => $"0x{b:X2}"))}");
                Console.Out.Flush();
            }
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

        private bool RowsScannedSinceKeyDown(int[] rows, ulong[] scanCounts)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                if (machine.GetKeyboardRowScanCount(rows[i]) <= scanCounts[i])
                    return false;
            }

            return true;
        }

        private void ProcessPendingSpectrumKeyReleases()
        {
            if (pendingSpectrumKeyReleases.Count == 0)
                return;

            List<Keys>? releasableKeys = null;
            foreach (var entry in pendingSpectrumKeyReleases)
            {
                bool rowsScanned = RowsScannedSinceKeyDown(entry.Value.Rows, entry.Value.ScanCounts);
                bool releaseExpired = machine.FrameCount - entry.Value.ReleaseFrame >= MaxDeferredSpectrumKeyReleaseFrames;
                if (!rowsScanned && !releaseExpired)
                    continue;

                releasableKeys ??= new List<Keys>();
                releasableKeys.Add(entry.Key);
            }

            if (releasableKeys == null)
                return;

            foreach (Keys key in releasableKeys)
            {
                pendingSpectrumKeyReleases.Remove(key);
                activeSpectrumKeyScans.Remove(key);
                ApplySpectrumKeyState(key, false);
            }
        }

        private void ApplySpectrumKeyState(Keys key, bool pressed)
        {
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
                SnapshotLoader.LoadSna48k(machine, dialog.FileName);

                SpectrumRenderer.RenderToBitmap(
                    screenBitmap,
                    machine.GetScreenBankData(),
                    machine.BorderColor,
                    machine.FlashPhase);

                screenBox.Image = screenBitmap;
                fpsLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
                ResetFrameScheduler();
                RecreateAudioPipeline();
                machine.ClearDebugHistory();
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
                if (extension.Equals(".tzx", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Tap.TapBootstrapResult bootstrap = Tap.TzxLoader.LoadAllStandardBlocksAndAutoStart(machine, dialog.FileName);
                        fpsLabel.Text = $"TZX loaded: {displayName} ({bootstrap.ConsumedBlockCount}/{bootstrap.TotalBlockCount} blocks)";
                    }
                    catch (InvalidOperationException)
                    {
                        try
                        {
                            Tap.TapBootstrapResult bootstrap = Tap.TzxLoader.BootstrapBasicProgramAndMountRemaining(machine, dialog.FileName);
                            fpsLabel.Text = $"TZX bootstrapped: {displayName} ({bootstrap.ConsumedBlockCount}/{bootstrap.TotalBlockCount} blocks)";
                        }
                        catch (InvalidOperationException)
                        {
                            Tap.TapMountResult result = Tap.TzxLoader.Mount(machine, dialog.FileName);
                            fpsLabel.Text = $"TZX mounted: {displayName} ({result.TotalBlockCount} blocks)";
                        }
                    }
                }
                else
                {
                    try
                    {
                        Tap.TapBootstrapResult bootstrap = Tap.TapLoader.LoadAllStandardBlocksAndAutoStart(machine, dialog.FileName);
                        fpsLabel.Text = $"TAP loaded: {displayName} ({bootstrap.ConsumedBlockCount}/{bootstrap.TotalBlockCount} blocks)";
                    }
                    catch (InvalidOperationException)
                    {
                        try
                        {
                            Tap.TapBootstrapResult bootstrap = Tap.TapLoader.BootstrapBasicProgramAndMountRemaining(machine, dialog.FileName);
                            fpsLabel.Text = $"TAP bootstrapped: {displayName} ({bootstrap.ConsumedBlockCount}/{bootstrap.TotalBlockCount} blocks)";
                        }
                        catch (InvalidOperationException)
                        {
                            Tap.TapMountResult result = Tap.TapLoader.Mount(machine, dialog.FileName);
                            fpsLabel.Text = $"TAP mounted: {displayName} ({result.TotalBlockCount} blocks)";
                        }
                    }
                }
                ResetFrameScheduler();
                machine.ClearDebugHistory();
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
                    RzxLoader.Load(machine, dialog.FileName);
                else
                    Z80SnapshotLoader.Load(machine, dialog.FileName);

                SpectrumRenderer.RenderToBitmap(
                    screenBitmap,
                    machine.GetScreenBankData(),
                    machine.BorderColor,
                    machine.FlashPhase);

                screenBox.Image = screenBitmap;
                fpsLabel.Text = $"Loaded: {Path.GetFileName(dialog.FileName)}";
                ResetFrameScheduler();
                RecreateAudioPipeline();
                machine.ClearDebugHistory();
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
                string dump = machine.BuildDebugDump("Manual F12 debug dump.");
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
                return machine.BuildDebugDump($"Crash context: {context}");
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
                long elapsedTicks = now - lastSchedulerTicks;
                if (elapsedTicks < 0)
                    elapsedTicks = 0;

                lastSchedulerTicks = now;
                accumulatedTicks += elapsedTicks;

                long maxAccumulatedTicks = ticksPerFrame * MaxCatchUpFramesPerTick;
                if (accumulatedTicks > maxAccumulatedTicks)
                    accumulatedTicks = maxAccumulatedTicks;

                int executedFrames = 0;
                while (accumulatedTicks >= ticksPerFrame && executedFrames < MaxCatchUpFramesPerTick)
                {
                    machine.ExecuteFrame();
                    ProcessPendingSpectrumKeyReleases();
                    audioPipeline.SubmitFrame(machine.DrainAudioFrame());

                    if (machine.TryConsumeAutoDebugDump(out string autoReason, out string autoDump))
                        WriteDebugDumpToFile(autoDump, autoReason);

                    accumulatedTicks -= ticksPerFrame;
                    executedFrames++;
                }

                if (executedFrames == 0)
                {
                    UpdateStats(now);
                    return;
                }

                SpectrumRenderer.RenderToBitmap(
                    screenBitmap,
                    machine.GetScreenBankData(),
                    machine.BorderColor,
                    machine.FlashPhase);

                screenBox.Image = screenBitmap;
                framesRenderedThisSecond++;

                UpdateStats(now);

                if (LogFrameDiagnostics && machine.FrameCount % 20 == 0)
                {
                    byte[] bank = machine.GetScreenBankData();
                    int nonZeroPixels = 0;
                    int nonZeroAttrs = 0;

                    for (int i = 0; i < 0x1800; i++)
                        if (bank[i] != 0) nonZeroPixels++;

                    for (int i = 0x1800; i < 0x1B00; i++)
                        if (bank[i] != 0x38) nonZeroAttrs++;

                    int screenWrites = machine.ScreenWriteLog.Values.Sum();
                    int aboveWrites = machine.AboveScreenWriteLog.Values.Sum();

                    string writeNote = machine.LastAboveWriteFrame < machine.FrameCount - 20
                        ? " [WRITES STOPPED]"
                        : string.Empty;

                    Console.WriteLine(
                        $"Frame {machine.FrameCount}: PC=0x{machine.Cpu.Regs.PC:X4} SP=0x{machine.Cpu.Regs.SP:X4} IFF1={machine.Cpu.IFF1} Pixels={nonZeroPixels} Attrs={nonZeroAttrs} | ScreenAddr writes={screenWrites} AboveAddr writes={aboveWrites} LastWrite@Frame{machine.LastAboveWriteFrame}{writeNote}");

                    Console.Out.Flush();
                }
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

        private void ResetFrameScheduler()
        {
            long now = frameClock.ElapsedTicks;
            lastSchedulerTicks = now;
            accumulatedTicks = 0;
            lastStatsTicks = now;
            framesRenderedThisSecond = 0;
        }

        private void RecreateAudioPipeline()
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

        private void UpdateStats(long nowTicks)
        {
            long ticksPerSecond = System.Diagnostics.Stopwatch.Frequency;

            if (nowTicks - lastStatsTicks >= ticksPerSecond)
            {
                fpsLabel.Text = $"FPS={framesRenderedThisSecond} Frame={machine.FrameCount}";
                framesRenderedThisSecond = 0;
                lastStatsTicks = nowTicks;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                audioPipeline.Dispose();
                screenBitmap.Dispose();
                frameTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
