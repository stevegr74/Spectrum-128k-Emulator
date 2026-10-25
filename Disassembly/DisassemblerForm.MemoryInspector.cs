namespace Spectrum128kEmulator
{
    public sealed partial class DisassemblerForm
    {
        private Control CreateMemoryPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Paper
            };

            var controls = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Paper
            };

            memoryContextLabel.AutoSize = false;
            memoryContextLabel.Location = new Point(0, 2);
            memoryContextLabel.Size = new Size(310, 18);
            memoryContextLabel.Font = new Font("Consolas", 8.0f, FontStyle.Bold);
            memoryContextLabel.ForeColor = Ink;
            controls.Controls.Add(memoryContextLabel);

            memoryAddressTextBox.Location = new Point(0, 25);
            memoryAddressTextBox.Size = new Size(72, 24);
            memoryAddressTextBox.MaxLength = 4;
            memoryAddressTextBox.Font = new Font("Consolas", 9.0f);
            memoryAddressTextBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                InspectMemoryFromAddressBox();
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            controls.Controls.Add(memoryAddressTextBox);

            Button goButton = CreateButton("Go", 78, 23, 46);
            goButton.Click += (_, _) => InspectMemoryFromAddressBox();
            controls.Controls.Add(goButton);

            memorySourceComboBox.Name = "MemorySourceComboBox";
            memorySourceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            memorySourceComboBox.Location = new Point(130, 25);
            memorySourceComboBox.Size = new Size(105, 24);
            memorySourceComboBox.DropDownWidth = 190;
            memorySourceComboBox.Font = new Font("Consolas", 8.0f);
            memorySourceComboBox.SelectedIndexChanged += (_, _) => InspectSelectedMemorySource();
            controls.Controls.Add(memorySourceComboBox);

            Button listingButton = CreateButton("Listing", 241, 23, 68);
            listingButton.Click += (_, _) => NavigateToSelectedMemoryRow();
            controls.Controls.Add(listingButton);

            memoryList.Name = "MemoryInspectorList";
            memoryList.Dock = DockStyle.Fill;
            memoryList.Font = new Font("Consolas", 8.2f);
            memoryList.IntegralHeight = false;
            memoryList.HorizontalScrollbar = true;
            memoryList.DoubleClick += (_, _) => NavigateToSelectedMemoryRow();

            panel.Controls.Add(memoryList);
            panel.Controls.Add(controls);
            RefreshMemorySources();
            RefreshMemoryInspector();
            return panel;
        }

        private void RefreshMemorySources()
        {
            changingMemorySource = true;
            try
            {
                memorySourceComboBox.Items.Clear();
                DisassemblyCpuState? cpu = snapshot.CpuState;
                if (cpu == null)
                    return;

                memorySourceComboBox.Items.Add(new MemorySourceItem("PC", cpu.PC));
                memorySourceComboBox.Items.Add(new MemorySourceItem("SP", cpu.SP));
                memorySourceComboBox.Items.Add(new MemorySourceItem("BC", cpu.BC));
                memorySourceComboBox.Items.Add(new MemorySourceItem("DE", cpu.DE));
                memorySourceComboBox.Items.Add(new MemorySourceItem("HL", cpu.HL));
                memorySourceComboBox.Items.Add(new MemorySourceItem("IX", cpu.IX));
                memorySourceComboBox.Items.Add(new MemorySourceItem("IY", cpu.IY));
                foreach ((ushort value, int index) in cpu.StackWords.Select((value, index) => (value, index)))
                    memorySourceComboBox.Items.Add(new MemorySourceItem($"SP+{index * 2:X2} value", value));
                if (memorySourceComboBox.Items.Count > 0)
                    memorySourceComboBox.SelectedIndex = 0;
            }
            finally
            {
                changingMemorySource = false;
            }
        }

        private void InspectSelectedMemorySource()
        {
            if (changingMemorySource || memorySourceComboBox.SelectedItem is not MemorySourceItem source)
                return;

            InspectMemory(
                new DisassemblyLocation(DisassemblyAddressSpace.Mapped, source.Address),
                $"Inspecting mapped address {source.Address:X4}H from {source.Name}.");
        }

        private void InspectMemoryFromAddressBox()
        {
            if (!DisassemblyAddressParser.TryParse(memoryAddressTextBox.Text, out ushort address) ||
                address >= memoryAddressSpace.Length)
            {
                statusLabel.Text = $"Enter a hexadecimal memory offset from 0000 to {memoryAddressSpace.Length - 1:X4}.";
                memoryAddressTextBox.SelectAll();
                memoryAddressTextBox.Focus();
                return;
            }

            InspectMemory(
                new DisassemblyLocation(memoryAddressSpace, address),
                $"Inspecting {memoryAddressSpace.FormatAddress(address)} in the frozen capture.");
        }

        private void InspectSelectedInstructionAddress()
        {
            if (SelectedInstruction() is not Spectrum128kEmulator.Z80.Z80Instruction instruction)
                return;

            InspectMemory(
                new DisassemblyLocation(selectedAddressSpace, instruction.Address),
                $"Inspecting instruction bytes at {selectedAddressSpace.FormatAddress(instruction.Address)}.");
        }

        private void InspectSelectedInstructionTarget()
        {
            if (!TryGetSelectedDirectReference(out DisassemblyCrossReference? reference))
                return;

            InspectMemory(
                reference!.Target,
                $"Inspecting {FormatReferenceKind(reference)} target at {reference.Target.AddressSpace.FormatAddress(reference.Target.Address)}.");
        }

        private void InspectMemory(DisassemblyLocation location, string status)
        {
            if (!snapshot.AvailableAddressSpaces.Contains(location.AddressSpace))
                return;

            memoryAddressSpace = location.AddressSpace;
            memoryAddress = (ushort)(location.Address % location.AddressSpace.Length);
            RefreshMemoryInspector();
            inspectorTabs.SelectedIndex = 1;
            statusLabel.Text = status;
        }

        private void RefreshMemoryInspector()
        {
            if (memoryList.IsDisposed || memoryContextLabel.IsDisposed)
                return;
            if (!snapshot.AvailableAddressSpaces.Contains(memoryAddressSpace))
            {
                memoryAddressSpace = DisassemblyAddressSpace.Mapped;
                memoryAddress = snapshot.ProgramCounter;
            }

            memoryContextLabel.Text = $"{memoryAddressSpace.DisplayName}  {memoryAddress:X4}H";
            memoryAddressTextBox.Text = memoryAddress.ToString("X4");
            memoryList.BeginUpdate();
            try
            {
                memoryList.Items.Clear();
                foreach (DisassemblyMemoryRow row in DisassemblyMemoryInspector.CreateRows(
                    snapshot,
                    memoryAddressSpace,
                    memoryAddress))
                {
                    memoryList.Items.Add(new MemoryRowListItem(row));
                }

                MemoryRowListItem? selected = memoryList.Items.Cast<MemoryRowListItem>()
                    .FirstOrDefault(item => item.Row.SelectedByteIndex >= 0);
                if (selected != null)
                    memoryList.SelectedItem = selected;
            }
            finally
            {
                memoryList.EndUpdate();
            }
        }

        private void NavigateToSelectedMemoryRow()
        {
            if (memoryList.SelectedItem is not MemoryRowListItem item)
                return;

            PreserveCurrentHistorySelection();
            SetSelectedAddressSpace(memoryAddressSpace);
            NavigateTo(
                item.Row.Address,
                status: $"Showing listing at {memoryAddressSpace.FormatAddress(item.Row.Address)} from the memory inspector.");
        }

        private sealed record MemorySourceItem(string Name, ushort Address)
        {
            public override string ToString() => $"{Name,-11} {Address:X4}H";
        }

        private sealed record MemoryRowListItem(DisassemblyMemoryRow Row)
        {
            public override string ToString() => $"{Row.Address:X4}  {Row.HexText}  |{Row.AsciiText}|";
        }
    }
}
