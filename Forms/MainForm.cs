//#define EXTENDED_DEBUG
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Spectrum128kEmulator.Audio;
using Spectrum128kEmulator.Debugging;
using Spectrum128kEmulator.Pokes;
using Spectrum128kEmulator.Tap;
using Spectrum128kEmulator.Tape;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public partial class MainForm : Form
    {
        private static readonly bool LogFrameDiagnostics = false;
        private static readonly bool LogUnimplementedOpcodes = false;
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
        private const double TapeTransportFullOverlaySeconds = 3.0;
        private const double TapeTransportStoppedIconSeconds = 5.0;
        private const double QuickStateOverlaySeconds = 2.5;
        private const string ApplicationTitle = "Spectrum 128K Emulator";
        private const int MaximumMediaTitleLength = 48;

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
        private readonly Bitmap enhanced2xBitmap = new Bitmap(Spectrum128Machine.ScreenWidth * 2, Spectrum128Machine.ScreenHeight * 2, PixelFormat.Format32bppArgb);
        private readonly Bitmap enhanced3xBitmap = new Bitmap(Spectrum128Machine.ScreenWidth * 3, Spectrum128Machine.ScreenHeight * 3, PixelFormat.Format32bppArgb);
        private readonly SpectrumFrameBuffer presentationFrameBuffer = new SpectrumFrameBuffer();
        private readonly System.Windows.Forms.Timer frameTimer = new System.Windows.Forms.Timer { Interval = 1 };
        private readonly PictureBox screenBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black,
            SizeMode = PictureBoxSizeMode.CenterImage,
            TabStop = true
        };
        private readonly Label fpsLabel = new Label();
        private readonly ContextMenuStrip displayContextMenu = new ContextMenuStrip();
        private readonly ToolStripMenuItem nativeDisplayMenuItem = new ToolStripMenuItem("1x Native");
        private readonly ToolStripMenuItem enhanced2xDisplayMenuItem = new ToolStripMenuItem("2x Enhanced");
        private readonly ToolStripMenuItem enhanced3xDisplayMenuItem = new ToolStripMenuItem("3x Enhanced");
        private readonly ToolStripMenuItem reset128kMenuItem = new ToolStripMenuItem("Reset to 128K");
        private readonly ToolStripMenuItem reset48kMenuItem = new ToolStripMenuItem("Reset to 48K");
        private readonly ToolStripMenuItem statusOverlayMenuItem = new ToolStripMenuItem("F2 - Status Overlay");
        private readonly ToolStripMenuItem tapeTransportMenuItem = new ToolStripMenuItem("F5 - Stop / Resume Tape");
        private readonly ToolStripMenuItem disassemblerMenuItem = new ToolStripMenuItem("F6 - Debugger / Disassembler");
        private readonly ToolStripMenuItem saveQuickStateMenuItem = new ToolStripMenuItem("F7 - Save Quick State");
        private readonly ToolStripMenuItem restoreQuickStateMenuItem = new ToolStripMenuItem("F8 - Restore Quick State");
        private readonly ToolStripMenuItem pokeManagerMenuItem = new ToolStripMenuItem("Poke Manager...");

        private readonly string romFolder;
        private Spectrum128Machine machine;
        private readonly object machineLock = new();
        private readonly object inputStateLock = new();
        private readonly object audioPipelineLock = new();
        private readonly Queue<SpectrumHostKeyEvent> pendingSpectrumHostKeyEvents = new();
        private readonly SpectrumKeyboardInputController keyboardInputController = new();
        private SpectrumKeyboardMessageFilter? keyboardMessageFilter;
        private readonly EmulationLoopController emulationLoop = new();
        private readonly DebuggerExecutionController debuggerExecutionController = new();
        private int debuggerStopNotificationQueued;
        private Exception? emulationLoopFailure;
        private string? emulationLoopFailureReportPath;
        private AudioPipeline audioPipeline;
        private ulong inputEmulatedTStates;
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
        private readonly TapeTurboPlaybackPolicy tapeTurboPlaybackPolicy = new();
        private readonly EmulationPresentationState presentationState = new();
        private int totalPresentedFrameCount;
        private long suppressSpectrumHostInputUntilTicks;
        private SpectrumDisplayMode displayMode = SpectrumDisplayMode.Enhanced2x;
        private SpectrumMachineModel selectedMachineModel = SpectrumMachineModel.Spectrum128K;
        private bool isStatusOverlayVisible;
        private TapeTransportState lastObservedTapeTransportState = TapeTransportState.NoTape;
        private bool lastObservedTapeMarkerStop;
        private TapeTransportState tapeTransportOverlayState = TapeTransportState.NoTape;
        private bool tapeTransportOverlayMarkerStop;
        private long tapeTransportFullOverlayExpiresAtTicks;
        private long tapeTransportIconExpiresAtTicks;
        private Spectrum128Machine.QuickState? quickState;
        private string? loadedMediaDisplayName;
        private string? quickStateMediaDisplayName;
        private PokeMediaIdentity? loadedMediaIdentity;
        private PokeMediaIdentity? quickStateMediaIdentity;
        private readonly Stack<PokeApplication> pokeApplications = new();
        private string? quickStateOverlayText;
        private long quickStateOverlayExpiresAtTicks;
        private EmulatorHelpForm? helpForm;
        private IDisposable? helpPauseLease;
        private DisassemblerForm? disassemblerForm;
        private IDisposable? disassemblerPauseLease;

        public MainForm()
        {
            Text = ApplicationTitle;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Controls.Add(screenBox);

            fpsLabel.Text = "FPS=0 Frame=0";
            fpsLabel.AutoSize = true;
            fpsLabel.ForeColor = Color.White;
            fpsLabel.BackColor = Color.Black;
            fpsLabel.Location = new Point(5, 5);
            fpsLabel.Visible = false;

            Controls.Add(fpsLabel);
            fpsLabel.BringToFront();

            romFolder = Path.Combine(AppContext.BaseDirectory, "ROMs");
            machine = CreateConfiguredMachine();
            InitializeDisplayModeSelector();
            ApplyDisplayMode(SpectrumDisplayMode.Enhanced2x);

            audioPipeline = CreateAudioPipeline();
            InitializeKeyboard();
            InitializeInputDiagnostics();
            InitializePerformanceDiagnostics();
            long now = frameClock.ElapsedTicks;
            lastSchedulerTicks = now;
            lastPresentationTicks = now;
            accumulatedEmulationTStates = 0;
            PublishPresentationState();
            emulationLoop.Start(EmulationLoop);
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

        private void InitializeDisplayModeSelector()
        {
            displayContextMenu.Items.Add("F1 - Help", null, (_, _) => ToggleHelpWindow());
            displayContextMenu.Items.Add(new ToolStripSeparator());

            statusOverlayMenuItem.Click += (_, _) => ToggleStatusOverlay();
            displayContextMenu.Items.Add(statusOverlayMenuItem);

            var machineModelMenuItem = new ToolStripMenuItem("F3 - Reset / Switch Model");
            reset128kMenuItem.Click += (_, _) => ResetToMachineModel(SpectrumMachineModel.Spectrum128K);
            reset48kMenuItem.Click += (_, _) => ResetToMachineModel(SpectrumMachineModel.Spectrum48K);
            machineModelMenuItem.DropDownItems.AddRange(new ToolStripItem[]
            {
                reset128kMenuItem,
                reset48kMenuItem
            });
            displayContextMenu.Items.Add(machineModelMenuItem);

            var displaySizeMenuItem = new ToolStripMenuItem("F4 - Display Size");
            nativeDisplayMenuItem.Click += (_, _) => SelectDisplayMode(SpectrumDisplayMode.Native);
            enhanced2xDisplayMenuItem.Click += (_, _) => SelectDisplayMode(SpectrumDisplayMode.Enhanced2x);
            enhanced3xDisplayMenuItem.Click += (_, _) => SelectDisplayMode(SpectrumDisplayMode.Enhanced3x);

            displaySizeMenuItem.DropDownItems.AddRange(new ToolStripItem[]
            {
                nativeDisplayMenuItem,
                enhanced2xDisplayMenuItem,
                enhanced3xDisplayMenuItem
            });
            displayContextMenu.Items.Add(displaySizeMenuItem);
            displayContextMenu.Items.Add(new ToolStripSeparator());

            tapeTransportMenuItem.Click += (_, _) => ToggleTapeTransport();
            displayContextMenu.Items.Add(tapeTransportMenuItem);
            displayContextMenu.Items.Add(new ToolStripSeparator());

            disassemblerMenuItem.Click += (_, _) => ToggleDisassemblerWindow();
            displayContextMenu.Items.Add(disassemblerMenuItem);

            saveQuickStateMenuItem.Click += (_, _) => SaveQuickState();
            displayContextMenu.Items.Add(saveQuickStateMenuItem);
            restoreQuickStateMenuItem.Click += (_, _) => RestoreQuickState();
            displayContextMenu.Items.Add(restoreQuickStateMenuItem);

            displayContextMenu.Items.Add(new ToolStripSeparator());
            displayContextMenu.Items.Add("F9 - Load .sna Snapshot", null, (_, _) => LoadSnaSnapshotFromDialog());
            displayContextMenu.Items.Add("F10 - Load .z80 Snapshot / Play .rzx Recording", null, (_, _) => LoadSnapshotOrRecordingFromDialog());
            displayContextMenu.Items.Add("F11 - Mount .tap or .tzx Tape Image", null, (_, _) => MountTapFromDialog());
            displayContextMenu.Items.Add(new ToolStripSeparator());
            displayContextMenu.Items.Add("F12 - Write Machine Diagnostic Dump", null, (_, _) => DumpMachineDebugState());
            displayContextMenu.Items.Add(new ToolStripSeparator());
            pokeManagerMenuItem.Click += (_, _) => OpenPokeManager();
            displayContextMenu.Items.Add(pokeManagerMenuItem);
            displayContextMenu.Opening += (_, _) =>
            {
                UpdateTapeTransportMenuItem();
                UpdateQuickStateMenuItems();
            };
            UpdateMachineModelMenuItems();
            UpdateTapeTransportMenuItem();
            UpdateQuickStateMenuItems();
            screenBox.ContextMenuStrip = displayContextMenu;
        }

        private void SelectDisplayMode(SpectrumDisplayMode mode)
        {
            if (displayMode == mode)
            {
                screenBox.Focus();
                return;
            }

            ApplyDisplayMode(mode);
            PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
            screenBox.Focus();
        }

        private void ApplyDisplayMode(SpectrumDisplayMode mode)
        {
            displayMode = mode;
            SpectrumDisplayLayout layout = SpectrumDisplayModes.GetLayout(mode);
            ClientSize = new Size(layout.ClientWidth, layout.ClientHeight);
            fpsLabel.Location = SpectrumDisplayModes.GetStatusOverlayLocation(layout, fpsLabel.Height);
            nativeDisplayMenuItem.Checked = mode == SpectrumDisplayMode.Native;
            enhanced2xDisplayMenuItem.Checked = mode == SpectrumDisplayMode.Enhanced2x;
            enhanced3xDisplayMenuItem.Checked = mode == SpectrumDisplayMode.Enhanced3x;
            statusOverlayMenuItem.Checked = isStatusOverlayVisible;
            screenBox.Image = GetPresentationBitmap(mode);
            UpdateStatsLabel();
        }

        private void UpdateMachineModelMenuItems()
        {
            reset128kMenuItem.Checked = selectedMachineModel == SpectrumMachineModel.Spectrum128K;
            reset48kMenuItem.Checked = selectedMachineModel == SpectrumMachineModel.Spectrum48K;
        }

        private static string FormatMachineModel(SpectrumMachineModel model)
        {
            return model == SpectrumMachineModel.Spectrum48K ? "48K" : "128K";
        }

        private Bitmap GetPresentationBitmap(SpectrumDisplayMode mode)
        {
            return mode switch
            {
                SpectrumDisplayMode.Native => screenBitmap,
                SpectrumDisplayMode.Enhanced2x => enhanced2xBitmap,
                SpectrumDisplayMode.Enhanced3x => enhanced3xBitmap,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
        }

        private static bool IsPlainShortcut(KeyEventArgs e, Keys key) =>
            e.KeyCode == key && !e.Alt && !e.Control && !e.Shift;

        private void ToggleStatusOverlay()
        {
            isStatusOverlayVisible = !isStatusOverlayVisible;
            statusOverlayMenuItem.Checked = isStatusOverlayVisible;
            UpdateStatsLabel();
            fpsLabel.Visible = isStatusOverlayVisible;
            screenBox.Focus();
        }

        private void ToggleTapeTransport()
        {
            TapeTransportState state = TapeTransportState.NoTape;
            bool markerStop = false;
            ExecuteWithEmulationPaused(() =>
            {
                lock (machineLock)
                {
                    state = machine.ToggleTapeTransport();
                    markerStop = machine.IsTapeStoppedAtMarker;
                }
            });

            lastObservedTapeTransportState = state;
            lastObservedTapeMarkerStop = markerStop;
            if (state is TapeTransportState.Playing or TapeTransportState.Stopped)
                ShowTapeTransportOverlay(state, markerStop);

            UpdateTapeTransportMenuItem();
            UpdateStatsLabel();
            PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
            screenBox.Focus();
        }

        private void UpdateTapeTransportMenuItem()
        {
            TapeTransportState state = machine.TapeTransportState;
            tapeTransportMenuItem.Enabled = state is TapeTransportState.Playing or TapeTransportState.Stopped;
            tapeTransportMenuItem.Text = state switch
            {
                TapeTransportState.Playing => "F5 - Stop Tape",
                TapeTransportState.Stopped => "F5 - Resume Tape",
                _ => "F5 - Stop / Resume Tape"
            };
        }

        private void SaveQuickState()
        {
            ExecuteWithEmulationPaused(() =>
            {
                lock (machineLock)
                    quickState = machine.CaptureQuickState();
            });
            quickStateMediaDisplayName = loadedMediaDisplayName;
            quickStateMediaIdentity = loadedMediaIdentity;

            UpdateQuickStateMenuItems();
            ShowQuickStateOverlay("QUICK STATE SAVED");
            fpsLabel.Text = $"Quick state saved: PC={quickState!.ProgramCounter:X4} {FormatMachineModel(quickState.MachineModel)}";
            PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
            screenBox.Focus();
        }

        private void RestoreQuickState()
        {
            Spectrum128Machine.QuickState? state = quickState;
            if (state == null)
            {
                screenBox.Focus();
                return;
            }

            ExecuteWithEmulationPaused(() =>
            {
                loadedMediaDisplayName = quickStateMediaDisplayName;
                loadedMediaIdentity = quickStateMediaIdentity;
                pokeApplications.Clear();
                lock (machineLock)
                    machine.RestoreQuickState(state);
                ClearHostAndSpectrumInputState();
                ResetFrameScheduler();
                RecreateAudioPipeline();
            });

            ShowMountedTapeTransportFeedback();
            ShowQuickStateOverlay("QUICK STATE RESTORED");
            fpsLabel.Text = $"Quick state restored: PC={state.ProgramCounter:X4} {FormatMachineModel(state.MachineModel)}";
            SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
            PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
            screenBox.Focus();
        }

        private void UpdateQuickStateMenuItems()
        {
            restoreQuickStateMenuItem.Enabled = quickState != null;
        }

        private void ShowQuickStateOverlay(string text)
        {
            quickStateOverlayText = text;
            quickStateOverlayExpiresAtTicks = frameClock.ElapsedTicks +
                (long)(QuickStateOverlaySeconds * System.Diagnostics.Stopwatch.Frequency);
        }

        private void ToggleHelpWindow()
        {
            if (helpForm is { IsDisposed: false })
            {
                helpForm.Close();
                return;
            }

            helpPauseLease = AcquireEmulationPauseLease();
            try
            {
                helpForm = new EmulatorHelpForm();
                helpForm.FormClosed += (_, _) =>
                {
                    helpForm = null;
                    helpPauseLease?.Dispose();
                    helpPauseLease = null;
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(() => screenBox.Focus());
                };
                helpForm.Show(this);
            }
            catch
            {
                helpPauseLease?.Dispose();
                helpPauseLease = null;
                throw;
            }
        }

        private void ToggleDisassemblerWindow()
        {
            if (disassemblerForm is { IsDisposed: false })
            {
                disassemblerForm.Close();
                return;
            }

            disassemblerPauseLease = AcquireEmulationPauseLease();
            try
            {
                lock (machineLock)
                {
                    machine.Cpu.StopBeforeInstruction = cpu => debuggerExecutionController.ShouldStopBeforeInstruction(
                        cpu.Regs.PC,
                        cpu.Regs.SP,
                        cpu.InstructionFetchCount);
                }

                disassemblerForm = new DisassemblerForm(
                    CaptureDisassemblySnapshot,
                    debuggerExecutionController,
                    HandleDebuggerExecutionCommand,
                    new DisassemblerWindowSettingsStore());
                disassemblerForm.FormClosed += (_, _) =>
                {
                    EnsureDebuggerPaused();
                    lock (machineLock)
                        machine.Cpu.StopBeforeInstruction = null;
                    disassemblerForm = null;
                    disassemblerPauseLease?.Dispose();
                    disassemblerPauseLease = null;
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke(() => screenBox.Focus());
                };
                disassemblerForm.Show(this);
            }
            catch
            {
                lock (machineLock)
                    machine.Cpu.StopBeforeInstruction = null;
                disassemblerPauseLease?.Dispose();
                disassemblerPauseLease = null;
                throw;
            }
        }

        private void HandleDebuggerExecutionCommand(DebuggerExecutionCommand command, ushort? address)
        {
            if (command == DebuggerExecutionCommand.Pause)
            {
                debuggerExecutionController.RequestPause();
                return;
            }

            if (disassemblerPauseLease == null)
                return;

            lock (machineLock)
            {
                ulong instructionFetchCount = machine.Cpu.InstructionFetchCount;
                ushort programCounter = machine.Cpu.Regs.PC;
                switch (command)
                {
                    case DebuggerExecutionCommand.Run:
                        debuggerExecutionController.BeginRun(programCounter, instructionFetchCount);
                        break;

                    case DebuggerExecutionCommand.StepInto:
                        debuggerExecutionController.BeginStepInto(programCounter, instructionFetchCount);
                        break;

                    case DebuggerExecutionCommand.StepOver:
                        Z80Instruction instruction = Z80InstructionDisassembler.Disassemble(
                            programCounter,
                            machine.PeekMemory);
                        bool isCallLike = instruction.Mnemonic.StartsWith("CALL ", StringComparison.Ordinal) ||
                            instruction.Mnemonic.StartsWith("RST ", StringComparison.Ordinal);
                        debuggerExecutionController.BeginStepOver(
                            programCounter,
                            machine.Cpu.Regs.SP,
                            instructionFetchCount,
                            instruction.Length,
                            isCallLike);
                        break;

                    case DebuggerExecutionCommand.StepOut:
                        ushort stackPointer = machine.Cpu.Regs.SP;
                        ushort returnAddress = (ushort)(
                            machine.PeekMemory(stackPointer) |
                            (machine.PeekMemory((ushort)(stackPointer + 1)) << 8));
                        debuggerExecutionController.BeginStepOut(
                            programCounter,
                            stackPointer,
                            instructionFetchCount,
                            returnAddress);
                        break;

                    case DebuggerExecutionCommand.RunToCursor when address.HasValue:
                        debuggerExecutionController.BeginRunToAddress(address.Value, programCounter, instructionFetchCount);
                        break;

                    default:
                        return;
                }
            }

            disassemblerPauseLease.Dispose();
            disassemblerPauseLease = null;
        }

        private void EnsureDebuggerPaused()
        {
            if (disassemblerPauseLease != null)
                return;

            debuggerExecutionController.RequestPause();
            disassemblerPauseLease = AcquireEmulationPauseLease();
        }

        private void QueueDebuggerStop(DebuggerStop stop)
        {
            if (Interlocked.Exchange(ref debuggerStopNotificationQueued, 1) != 0)
                return;

            try
            {
                BeginInvoke(new Action(() =>
                {
                    Interlocked.Exchange(ref debuggerStopNotificationQueued, 0);
                    if (disassemblerForm is not { IsDisposed: false } || disassemblerPauseLease != null)
                        return;

                    disassemblerPauseLease = AcquireEmulationPauseLease();
                    disassemblerForm.ApplyExecutionStop(stop);
                }));
            }
            catch
            {
                Interlocked.Exchange(ref debuggerStopNotificationQueued, 0);
            }
        }

        private DisassemblySnapshot CaptureDisassemblySnapshot()
        {
            lock (machineLock)
            {
                var memory = new byte[65536];
                for (int address = 0; address < memory.Length; address++)
                    memory[address] = machine.PeekMemory((ushort)address);

                var registers = machine.Cpu.Regs;
                var stackWords = new ushort[4];
                for (int index = 0; index < stackWords.Length; index++)
                {
                    ushort address = (ushort)(registers.SP + index * 2);
                    stackWords[index] = (ushort)(memory[address] | (memory[(ushort)(address + 1)] << 8));
                }

                var cpuState = new DisassemblyCpuState(
                    registers.AF,
                    registers.BC,
                    registers.DE,
                    registers.HL,
                    (ushort)((registers.A_ << 8) | registers.F_),
                    (ushort)((registers.B_ << 8) | registers.C_),
                    (ushort)((registers.D_ << 8) | registers.E_),
                    (ushort)((registers.H_ << 8) | registers.L_),
                    registers.IX,
                    registers.IY,
                    registers.SP,
                    registers.PC,
                    registers.I,
                    registers.R,
                    machine.Cpu.IFF1,
                    machine.Cpu.IFF2,
                    machine.Cpu.InterruptMode,
                    machine.Cpu.IsHalted,
                    machine.Cpu.InterruptPending,
                    machine.Cpu.TStates,
                    stackWords);
                byte[][] romBanks = Enumerable.Range(0, 2).Select(machine.GetRomBankCopy).ToArray();
                byte[][] ramBanks = Enumerable.Range(0, 8).Select(machine.GetRamBankCopy).ToArray();

                return new DisassemblySnapshot(
                    memory,
                    machine.Cpu.Regs.PC,
                    machine.MachineModel,
                    machine.CurrentRomBank,
                    machine.PagedRamBank,
                    machine.ScreenBank,
                    cpuState: cpuState,
                    romBanks: romBanks,
                    ramBanks: ramBanks,
                    analysisContext: $"{machine.MachineModel}:{loadedMediaDisplayName ?? "(no media)"}");
            }
        }

        private Spectrum128Machine CreateConfiguredMachine()
        {
            var configuredMachine = new Spectrum128Machine(romFolder);
            if (selectedMachineModel != SpectrumMachineModel.Spectrum128K)
                configuredMachine.Reset(selectedMachineModel);
            configuredMachine.SetScreenWriteDiagnosticsEnabled(LogFrameDiagnostics);
            if (LogUnimplementedOpcodes || LogPagingWrites)
            {
                configuredMachine.Trace = s =>
                {
                    if ((LogUnimplementedOpcodes && s.StartsWith("UNIMPL")) ||
                        (LogPagingWrites && s.StartsWith("[7FFD]")))
                    {
                        Console.WriteLine(s);
                        Console.Out.Flush();
                    }
                };
            }
            return configuredMachine;
        }

        private void ToggleMachineModel()
        {
            SpectrumMachineModel targetModel = selectedMachineModel == SpectrumMachineModel.Spectrum128K
                ? SpectrumMachineModel.Spectrum48K
                : SpectrumMachineModel.Spectrum128K;
            ResetToMachineModel(targetModel);
        }

        private void ResetToMachineModel(SpectrumMachineModel model)
        {
            ExecuteWithEmulationPaused(() =>
            {
                selectedMachineModel = model;
                loadedMediaDisplayName = null;
                loadedMediaIdentity = null;
                pokeApplications.Clear();
                ClearHostAndSpectrumInputState();
                lock (machineLock)
                {
                    machine = CreateConfiguredMachine();
                    machine.ClearDebugHistory();
                }
                ResetFrameScheduler();
                RecreateAudioPipeline();
            });

            UpdateMachineModelMenuItems();
            fpsLabel.Text = $"Reset: {FormatMachineModel(model)}";
            SuppressSpectrumHostInputForMilliseconds(PostLoadInputSuppressionMilliseconds);
            PresentCurrentMachineFrame(frameClock.ElapsedTicks + PresentationIntervalTicks);
            screenBox.Focus();
        }

        private void FrameTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                long now = frameClock.ElapsedTicks;
                UpdateTapeTransportFeedback(now);
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
                    if (emulationLoop.WaitWhilePaused(cancellationToken))
                        continue;
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

                    int speedMultiplier = GetEmulationSpeedMultiplier(activeMachine);
                    accumulatedEmulationTStates +=
                        (double)elapsedTicks *
                        activeMachine.CurrentCpuClockHz *
                        speedMultiplier /
                        System.Diagnostics.Stopwatch.Frequency;

                    int maxAccumulatedTStates = activeMachine.FrameTStates * MaxCatchUpFramesPerTick;
                    if (accumulatedEmulationTStates > maxAccumulatedTStates)
                        accumulatedEmulationTStates = maxAccumulatedTStates;

                    int actualExecutedTStates = 0;
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
                                inputEmulatedTStates += (ulong)Math.Max(0, executedSliceTStates);
                                ProcessPendingSpectrumKeyReleases();

                                if (activeMachine.Cpu.ExecutionStopped &&
                                    debuggerExecutionController.TryConsumeStop(out DebuggerStop? debuggerStop))
                                {
                                    QueueDebuggerStop(debuggerStop!);
                                }

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

                    if (completedFrames > 0 || !presentationState.HasValue)
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
                string? crashPath = null;
                try
                {
                    string debugFolder = Path.Combine(AppContext.BaseDirectory, "debug");
                    Directory.CreateDirectory(debugFolder);
                    string fileName = $"emulation-loop-crash-{DateTime.Now:yyyyMMdd-HHmmssfff}.txt";
                    crashPath = Path.Combine(debugFolder, fileName);
                    File.WriteAllText(crashPath, $"Exception: {ex}{Environment.NewLine}");
                }
                catch
                {
                }

                Volatile.Write(ref emulationLoopFailure, ex);
                Volatile.Write(ref emulationLoopFailureReportPath, crashPath);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        frameTimer.Stop();
                        string reportDetail = crashPath == null
                            ? string.Empty
                            : $"\n\nDiagnostic saved to:\n{crashPath}";
                        MessageBox.Show(
                            this,
                            $"The emulation loop stopped:\n{ex.Message}{reportDetail}",
                            "Emulator Crash",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }));
                }
                catch
                {
                }
            }
        }

        private void ExecuteWithEmulationPaused(Action action)
        {
            using IDisposable pauseLease = AcquireEmulationPauseLease();
            action();
        }

        private IDisposable AcquireEmulationPauseLease()
        {
            Exception? failure = Volatile.Read(ref emulationLoopFailure);
            if (failure != null)
            {
                string? reportPath = Volatile.Read(ref emulationLoopFailureReportPath);
                string reportDetail = reportPath == null ? string.Empty : $" Diagnostic: {reportPath}";
                throw new InvalidOperationException(
                    $"The emulation loop stopped: {failure.Message}.{reportDetail}",
                    failure);
            }

            return emulationLoop.AcquirePause(
                () =>
                {
                    frameTimer.Stop();
                },
                () =>
                {
                    long now = frameClock.ElapsedTicks;
                    lastSchedulerTicks = now;
                    lastPresentationTicks = now;
                    if (!IsDisposed)
                        frameTimer.Start();
                });
        }

        private void ClearHostAndSpectrumInputState()
        {
            ResetInputBridgeState();
            lock (machineLock)
            {
                inputEmulatedTStates = 0;
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
            presentationState.Publish(sourceMachine);
        }

        private void PresentCurrentMachineFrame(long now, bool force = false)
        {
            if (force)
                lastPresentationTicks = now;
            else if (!ShouldPresentFrame(now))
                return;

            if (!presentationState.TryCapture(out EmulationPresentationSnapshot snapshot))
                return;

            byte[] screenBankCopy = snapshot.ScreenBankData;
            int borderColor = snapshot.BorderColor;
            BorderFrame? borderFrame = snapshot.BorderFrame;
            UlaFrame? ulaFrame = snapshot.UlaFrame;
            bool flashPhase = snapshot.FlashPhase;

            long renderStartTicks = frameClock.ElapsedTicks;
            if (borderFrame == null)
                presentationFrameBuffer.Render(screenBankCopy, borderColor, flashPhase);
            else if (ulaFrame != null)
                presentationFrameBuffer.Render(ulaFrame, flashPhase);
            else
                presentationFrameBuffer.Render(screenBankCopy, borderFrame, flashPhase);
            SpectrumDisplayLayout layout = SpectrumDisplayModes.GetLayout(displayMode);
            Bitmap presentationBitmap = GetPresentationBitmap(displayMode);
            if (layout.Scale == 1)
                SpectrumRenderer.RenderToBitmap(presentationBitmap, presentationFrameBuffer);
            else
                SpectrumRenderer.RenderScaledToBitmap(presentationBitmap, presentationFrameBuffer, layout.Scale);
            DrawTapeTransportOverlay(presentationBitmap, layout.Scale, now);
            DrawQuickStateOverlay(presentationBitmap, layout.Scale, now);
            long renderEndTicks = frameClock.ElapsedTicks;

            screenBox.Image = presentationBitmap;
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
                selectedMachineModel = machine.MachineModel;
                lastSchedulerTicks = now;
                lastPresentationTicks = now;
                accumulatedEmulationTStates = 0;
                currentTurboTapeLoadFactor = TurboTapeLoadFactor;
                tapeTurboPlaybackPolicy.Reset();
                inputEmulatedTStates = 0;
                lastObservedTapeTransportState = machine.TapeTransportState;
                lastObservedTapeMarkerStop = machine.IsTapeStoppedAtMarker;
            }
            if (lastObservedTapeTransportState == TapeTransportState.NoTape)
                ClearTapeTransportOverlay();
            PublishPresentationState();
            lastStatsTicks = now;
            framesRenderedThisSecond = 0;
            displayedFps = 0;
            displayedFrameCount = 0;
            totalPresentedFrameCount = 0;
            UpdateMachineModelMenuItems();
            UpdateWindowTitle();
            UpdateTapeTransportMenuItem();
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
            string tapeState = FormatTapeTransportState(machine.TapeTransportState, machine.IsTapeStoppedAtMarker);
            fpsLabel.Text = $"FPS={displayedFps} Frame={displayedFrameCount} Model={FormatMachineModel(selectedMachineModel)} Tape={tapeState} Display={SpectrumDisplayModes.GetLayout(displayMode).Label}";
        }

        private void ShowMountedTapeTransportFeedback()
        {
            TapeTransportState state = machine.TapeTransportState;
            lastObservedTapeTransportState = state;
            lastObservedTapeMarkerStop = machine.IsTapeStoppedAtMarker;
            UpdateTapeTransportMenuItem();
            if (state == TapeTransportState.Playing)
                ShowTapeTransportOverlay(state, markerStop: false);
        }

        private void UpdateTapeTransportFeedback(long nowTicks)
        {
            TapeTransportState state = machine.TapeTransportState;
            bool markerStop = machine.IsTapeStoppedAtMarker;
            if (state != lastObservedTapeTransportState || markerStop != lastObservedTapeMarkerStop)
            {
                lastObservedTapeTransportState = state;
                lastObservedTapeMarkerStop = markerStop;

                if (state is TapeTransportState.Playing or TapeTransportState.Stopped or TapeTransportState.Ended)
                    ShowTapeTransportOverlay(state, markerStop);
                else if (state == TapeTransportState.NoTape)
                    ClearTapeTransportOverlay();

                UpdateTapeTransportMenuItem();
                UpdateStatsLabel();
            }

            if (tapeTransportOverlayState == TapeTransportState.Ended &&
                nowTicks >= tapeTransportIconExpiresAtTicks)
            {
                ClearTapeTransportOverlay();
            }
        }

        private void ShowTapeTransportOverlay(TapeTransportState state, bool markerStop)
        {
            tapeTransportOverlayState = state;
            tapeTransportOverlayMarkerStop = markerStop;
            long nowTicks = frameClock.ElapsedTicks;
            tapeTransportFullOverlayExpiresAtTicks =
                nowTicks + (long)(TapeTransportFullOverlaySeconds * System.Diagnostics.Stopwatch.Frequency);
            tapeTransportIconExpiresAtTicks = state switch
            {
                TapeTransportState.Playing => long.MaxValue,
                TapeTransportState.Stopped =>
                    nowTicks + (long)(TapeTransportStoppedIconSeconds * System.Diagnostics.Stopwatch.Frequency),
                _ => tapeTransportFullOverlayExpiresAtTicks
            };
        }

        private void ClearTapeTransportOverlay()
        {
            tapeTransportOverlayState = TapeTransportState.NoTape;
            tapeTransportOverlayMarkerStop = false;
            tapeTransportFullOverlayExpiresAtTicks = 0;
            tapeTransportIconExpiresAtTicks = 0;
        }

        private void DrawTapeTransportOverlay(Bitmap bitmap, int scale, long nowTicks)
        {
            if (tapeTransportOverlayState == TapeTransportState.NoTape ||
                nowTicks >= tapeTransportIconExpiresAtTicks)
                return;

            string text = tapeTransportOverlayState switch
            {
                TapeTransportState.Playing => "TAPE PLAYING - F5 STOP",
                TapeTransportState.Stopped when tapeTransportOverlayMarkerStop => "TAPE AUTO-STOPPED - F5 RESUME",
                TapeTransportState.Stopped => "TAPE PAUSED - F5 RESUME",
                TapeTransportState.Ended => "TAPE ENDED",
                _ => string.Empty
            };
            Color accent = tapeTransportOverlayState switch
            {
                TapeTransportState.Playing => Color.FromArgb(60, 220, 150),
                TapeTransportState.Stopped => Color.FromArgb(255, 190, 55),
                _ => Color.FromArgb(210, 215, 220)
            };
            bool showFullOverlay = nowTicks < tapeTransportFullOverlayExpiresAtTicks;

            int x = 8 * scale;
            int y = 8 * scale;
            int width = (showFullOverlay ? (tapeTransportOverlayMarkerStop ? 238 : 190) : 30) * scale;
            int height = (showFullOverlay ? 30 : 24) * scale;
            int iconLeft = x + (8 * scale);
            int iconSize = (showFullOverlay ? 14 : 11) * scale;
            int iconTop = y + ((height - iconSize) / 2);

            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var backgroundBrush = new SolidBrush(Color.FromArgb(showFullOverlay ? 210 : 168, 8, 12, 14));
            using var accentBrush = new SolidBrush(accent);
            using var borderPen = new Pen(Color.FromArgb(showFullOverlay ? 235 : 190, accent), Math.Max(1, scale));
            using var font = new Font(FontFamily.GenericSansSerif, 8.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.FillRectangle(backgroundBrush, x, y, width, height);
            graphics.DrawRectangle(borderPen, x, y, width - 1, height - 1);

            if (tapeTransportOverlayState == TapeTransportState.Playing)
            {
                graphics.FillPolygon(accentBrush, new[]
                {
                    new Point(iconLeft, iconTop),
                    new Point(iconLeft, iconTop + iconSize),
                    new Point(iconLeft + iconSize, iconTop + (iconSize / 2))
                });
            }
            else
            {
                graphics.FillRectangle(accentBrush, iconLeft, iconTop, iconSize, iconSize);
            }

            if (showFullOverlay)
                graphics.DrawString(text, font, accentBrush, x + (28 * scale), y + (9 * scale));
        }

        private void DrawQuickStateOverlay(Bitmap bitmap, int scale, long nowTicks)
        {
            if (quickStateOverlayText == null || nowTicks >= quickStateOverlayExpiresAtTicks)
                return;

            Color accent = quickStateOverlayText.EndsWith("RESTORED", StringComparison.Ordinal)
                ? Color.FromArgb(255, 190, 55)
                : Color.FromArgb(60, 220, 190);
            int width = 150 * scale;
            int height = 30 * scale;
            int x = bitmap.Width - width - (8 * scale);
            int y = 8 * scale;

            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var backgroundBrush = new SolidBrush(Color.FromArgb(210, 8, 12, 14));
            using var accentBrush = new SolidBrush(accent);
            using var borderPen = new Pen(Color.FromArgb(235, accent), Math.Max(1, scale));
            using var font = new Font(FontFamily.GenericSansSerif, 8.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.FillRectangle(backgroundBrush, x, y, width, height);
            graphics.DrawRectangle(borderPen, x, y, width - 1, height - 1);
            graphics.DrawString(quickStateOverlayText, font, accentBrush, x + (9 * scale), y + (9 * scale));
        }

        private void UpdateWindowTitle()
        {
            string mediaTitle = loadedMediaDisplayName is null
                ? string.Empty
                : $" - {loadedMediaDisplayName}";
            Text = $"{ApplicationTitle}{mediaTitle}";
        }

        private void SetLoadedMediaDisplayName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).Trim();
            loadedMediaDisplayName = ShortenMediaTitle(name);
            loadedMediaIdentity = PokeMediaIdentity.FromFile(path);
            pokeApplications.Clear();
            UpdateWindowTitle();
        }

        private static string? ShortenMediaTitle(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            if (name.Length <= MaximumMediaTitleLength)
                return name;

            const int suffixLength = 12;
            int prefixLength = MaximumMediaTitleLength - suffixLength - 3;
            return $"{name[..prefixLength]}...{name[^suffixLength..]}";
        }

        private static string FormatTapeTransportState(TapeTransportState state, bool markerStop)
        {
            return state switch
            {
                TapeTransportState.Playing => "Playing",
                TapeTransportState.Stopped when markerStop => "Stopped(marker)",
                TapeTransportState.Stopped => "Stopped",
                TapeTransportState.Ended => "Ended",
                _ => "None"
            };
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
            return tapeTurboPlaybackPolicy.IsTurboPlaybackAllowed(
                activeMachine.MountedTape?.IsActivelyStreamingEarSignal == true,
                activeMachine.HasAudibleOutput);
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
                if (keyboardMessageFilter != null)
                {
                    Application.RemoveMessageFilter(keyboardMessageFilter);
                    keyboardMessageFilter = null;
                }
                emulationLoop.Dispose();
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
                enhanced2xBitmap.Dispose();
                enhanced3xBitmap.Dispose();
                displayContextMenu.Dispose();
                frameTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private readonly record struct SpectrumHostKeyEvent(Keys Key, bool IsDown, bool Reset = false);
    }
}
