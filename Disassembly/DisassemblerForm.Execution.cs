using Spectrum128kEmulator.Debugging;
using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public enum DebuggerExecutionCommand
    {
        Run,
        Pause,
        StepInto,
        StepOver,
        StepOut,
        RunToCursor
    }

    public sealed partial class DisassemblerForm
    {
        internal void ApplyExecutionStop(DebuggerStop stop)
        {
            executionRunning = false;
            snapshot = captureSnapshot();
            NavigateTo(
                snapshot.ProgramCounter,
                status: FormatStopStatus(stop),
                selectedAddress: snapshot.ProgramCounter);
            UpdateCaptureState();
            UpdateExecutionControls();
            instructionList.Focus();
        }

        internal void SetExecutionRunning()
        {
            executionRunning = true;
            UpdateCaptureState();
            UpdateExecutionControls();
            statusLabel.Text = "Execution is running. The listing remains frozen until the next pause.";
        }

        private Panel CreateBreakpointPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 174,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Paper
            };

            var title = new Label
            {
                Text = "BREAKPOINTS",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new Font(FontFamily.GenericSansSerif, 7.5f, FontStyle.Bold),
                ForeColor = Ink,
                TextAlign = ContentAlignment.MiddleLeft
            };
            breakpointList.Dock = DockStyle.Fill;
            breakpointList.CheckOnClick = true;
            breakpointList.Font = listingFont;
            breakpointList.ItemCheck += BreakpointList_ItemCheck;
            breakpointList.DoubleClick += (_, _) => NavigateToSelectedBreakpoint();

            var removeButton = new Button
            {
                Text = "Remove",
                Dock = DockStyle.Bottom,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Ink
            };
            removeButton.Click += (_, _) => RemoveSelectedBreakpoint();

            panel.Controls.Add(breakpointList);
            panel.Controls.Add(removeButton);
            panel.Controls.Add(title);
            return panel;
        }

        private void BeginExecution(DebuggerExecutionCommand command)
        {
            if (executionRunning)
                return;

            requestExecution(command, null);
            SetExecutionRunning();
        }

        private void RunToCursor()
        {
            if (executionRunning || SelectedInstruction() is not Z80Instruction instruction)
                return;

            requestExecution(DebuggerExecutionCommand.RunToCursor, instruction.Address);
            SetExecutionRunning();
        }

        private void RequestPause()
        {
            if (!executionRunning)
                return;

            requestExecution(DebuggerExecutionCommand.Pause, null);
            pauseButton.Enabled = false;
            statusLabel.Text = "Pause requested; waiting for the next instruction boundary.";
        }

        private void ToggleBreakpointAtSelection()
        {
            if (executionRunning || SelectedInstruction() is not Z80Instruction instruction)
                return;

            DebuggerBreakpoint? existing = executionController.GetBreakpoints()
                .FirstOrDefault(breakpoint => breakpoint.Address == instruction.Address);
            if (existing == null)
                executionController.SetBreakpoint(instruction.Address);
            else
                executionController.RemoveBreakpoint(instruction.Address);

            RefreshBreakpointList();
            PopulateListing(instruction.Address);
        }

        private void RefreshBreakpointList()
        {
            refreshingBreakpointList = true;
            try
            {
                breakpointList.Items.Clear();
                foreach (DebuggerBreakpoint breakpoint in executionController.GetBreakpoints())
                    breakpointList.Items.Add(new BreakpointListItem(breakpoint.Address), breakpoint.Enabled);
            }
            finally
            {
                refreshingBreakpointList = false;
            }
        }

        private void BreakpointList_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (refreshingBreakpointList || breakpointList.Items[e.Index] is not BreakpointListItem item)
                return;

            executionController.SetBreakpoint(item.Address, e.NewValue == CheckState.Checked);
            BeginInvoke(() => PopulateListing(SelectedInstruction()?.Address ?? listingAddress));
        }

        private void RemoveSelectedBreakpoint()
        {
            if (executionRunning || breakpointList.SelectedItem is not BreakpointListItem item)
                return;

            executionController.RemoveBreakpoint(item.Address);
            RefreshBreakpointList();
            PopulateListing(SelectedInstruction()?.Address ?? listingAddress);
        }

        private void NavigateToSelectedBreakpoint()
        {
            if (breakpointList.SelectedItem is BreakpointListItem item)
                NavigateTo(item.Address);
        }

        private void UpdateExecutionControls()
        {
            runButton.Enabled = !executionRunning;
            pauseButton.Enabled = executionRunning;
            stepIntoButton.Enabled = !executionRunning;
            stepOverButton.Enabled = !executionRunning;
            stepOutButton.Enabled = !executionRunning;
            runToCursorButton.Enabled = !executionRunning;
            toggleBreakpointButton.Enabled = !executionRunning;
            breakpointList.Enabled = !executionRunning;
        }

        private void UpdateCpuState()
        {
            DisassemblyCpuState? cpu = snapshot.CpuState;
            if (cpu == null)
            {
                cpuStateLabel.Text = "CPU state was not included in this capture.";
                return;
            }

            string flags = FormatFlags((byte)cpu.AF);
            string stack = string.Join(' ', cpu.StackWords.Select((value, index) => $"SP+{index * 2:X2}:{value:X4}"));
            cpuStateLabel.Text =
                $"AF {cpu.AF:X4} [{flags}]  BC {cpu.BC:X4}  DE {cpu.DE:X4}  HL {cpu.HL:X4}  IX {cpu.IX:X4}  IY {cpu.IY:X4}  SP {cpu.SP:X4}  PC {cpu.PC:X4}\r\n" +
                $"AF' {cpu.AlternateAF:X4}  BC' {cpu.AlternateBC:X4}  DE' {cpu.AlternateDE:X4}  HL' {cpu.AlternateHL:X4}  I {cpu.I:X2}  R {cpu.R:X2}  IM {cpu.InterruptMode}  IFF {Bool(cpu.IFF1)}/{Bool(cpu.IFF2)}  HALT {Bool(cpu.IsHalted)}  INT {Bool(cpu.InterruptPending)}\r\n" +
                $"T {cpu.TStates}  {stack}";
        }

        private static string FormatFlags(byte flags)
        {
            const string names = "SZ5H3PNC";
            return new string(names.Select((name, bit) => (flags & (0x80 >> bit)) != 0 ? name : '-').ToArray());
        }

        private static int Bool(bool value) => value ? 1 : 0;

        private static string FormatStopStatus(DebuggerStop stop) => stop.Reason switch
        {
            DebuggerStopReason.StepComplete => $"Step complete at {stop.Address:X4}H.",
            DebuggerStopReason.StepOverComplete => $"Step over complete at {stop.Address:X4}H.",
            DebuggerStopReason.StepOutComplete => $"Step out complete at {stop.Address:X4}H.",
            DebuggerStopReason.Breakpoint => $"Breakpoint reached at {stop.Address:X4}H.",
            DebuggerStopReason.RunToAddress => $"Run to cursor reached {stop.Address:X4}H.",
            _ => $"Paused at {stop.Address:X4}H."
        };

        private sealed record BreakpointListItem(ushort Address)
        {
            public override string ToString() => $"{Address:X4}H";
        }
    }
}
