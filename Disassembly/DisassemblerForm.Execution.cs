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
            previousCpuState = snapshot.CpuState;
            snapshot = captureSnapshot();
            RefreshMemorySources();
            SetSelectedAddressSpace(DisassemblyAddressSpace.Mapped);
            PopulateAddressSpaceChoices();
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
                Dock = DockStyle.Fill,
                BackColor = Paper
            };

            var title = new Label
            {
                Text = "BREAKPOINTS",
                Dock = DockStyle.Top,
                Height = 24,
                Font = EmulatorUiStyle.UiFont(8F, FontStyle.Bold),
                ForeColor = EmulatorUiStyle.Muted,
                TextAlign = ContentAlignment.MiddleLeft
            };
            breakpointList.Dock = DockStyle.Fill;
            breakpointList.CheckOnClick = true;
            breakpointList.Font = listingFont;
            breakpointList.BackColor = EmulatorUiStyle.Field;
            breakpointList.ForeColor = Ink;
            breakpointList.ItemCheck += BreakpointList_ItemCheck;
            breakpointList.DoubleClick += (_, _) => NavigateToSelectedBreakpoint();

            var removeButton = new Button
            {
                Text = "Remove",
                Dock = DockStyle.Bottom,
                Height = 28
            };
            EmulatorUiStyle.StyleButton(removeButton);
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
            if (executionRunning || selectedAddressSpace.Kind != DisassemblyAddressSpaceKind.Mapped ||
                SelectedInstruction() is not Z80Instruction instruction)
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
            if (executionRunning || selectedAddressSpace.Kind != DisassemblyAddressSpaceKind.Mapped ||
                SelectedInstruction() is not Z80Instruction instruction)
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
            {
                SetSelectedAddressSpace(DisassemblyAddressSpace.Mapped);
                NavigateTo(item.Address);
            }
        }

        private void UpdateExecutionControls()
        {
            runButton.Enabled = !executionRunning;
            pauseButton.Enabled = executionRunning;
            stepIntoButton.Enabled = !executionRunning;
            stepOverButton.Enabled = !executionRunning;
            stepOutButton.Enabled = !executionRunning;
            bool mappedView = selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped;
            runToCursorButton.Enabled = !executionRunning && mappedView;
            toggleBreakpointButton.Enabled = !executionRunning && mappedView;
            breakpointList.Enabled = !executionRunning;
            EmulatorUiStyle.StyleButton(runButton, primary: !executionRunning);
            EmulatorUiStyle.StyleButton(pauseButton, primary: executionRunning);
        }

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
