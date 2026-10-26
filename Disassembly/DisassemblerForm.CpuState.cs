using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed partial class DisassemblerForm
    {
        private static readonly Color CpuStateBackground = EmulatorUiStyle.Panel;
        private static readonly Color CpuStateChanged = EmulatorUiStyle.Changed;
        private static readonly Color CpuFlagActive = EmulatorUiStyle.AccentSoft;
        private static readonly Color CpuFlagInactive = Color.FromArgb(225, 227, 222);

        private Control CreateCpuStatePanel()
        {
            cpuStatePanel.Name = "CpuStatePanel";
            cpuStatePanel.Dock = DockStyle.Top;
            cpuStatePanel.Height = 72;
            cpuStatePanel.Margin = Padding.Empty;
            cpuStatePanel.Padding = Padding.Empty;
            cpuStatePanel.BackColor = EmulatorUiStyle.Border;
            cpuStatePanel.ColumnCount = 4;
            cpuStatePanel.RowCount = 1;
            cpuStatePanel.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            cpuStatePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31f));
            cpuStatePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23f));
            cpuStatePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f));
            cpuStatePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));

            cpuStatePanel.Controls.Add(CreateCpuGroup("REGISTERS", CreateRegisterGrid()), 0, 0);
            cpuStatePanel.Controls.Add(CreateCpuGroup("FLOW", CreateFlowGrid()), 1, 0);
            cpuStatePanel.Controls.Add(CreateCpuGroup("CPU STATE", CreateProcessorStateGrid()), 2, 0);
            cpuStatePanel.Controls.Add(CreateCpuGroup("STACK", CreateStackGrid()), 3, 0);
            return cpuStatePanel;
        }

        private Control CreateCpuGroup(string title, Control body)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 1, 0),
                BackColor = CpuStateBackground
            };
            var heading = new Label
            {
                Location = new Point(8, 2),
                Size = new Size(184, 15),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = title,
                Font = EmulatorUiStyle.UiFont(8F, FontStyle.Bold),
                ForeColor = EmulatorUiStyle.Muted,
                TextAlign = ContentAlignment.MiddleLeft
            };
            body.Dock = DockStyle.None;
            body.Location = new Point(8, 18);
            body.Size = new Size(184, 54);
            body.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            panel.Controls.Add(body);
            panel.Controls.Add(heading);
            return panel;
        }

        private Control CreateRegisterGrid()
        {
            TableLayoutPanel grid = CreateGrid(4, 2);
            string[] keys = ["AF", "BC", "DE", "HL", "AF2", "BC2", "DE2", "HL2"];
            string[] captions = ["AF", "BC", "DE", "HL", "AF'", "BC'", "DE'", "HL'"];
            for (int index = 0; index < keys.Length; index++)
            {
                bool clickable = keys[index] is not "AF" and not "AF2";
                grid.Controls.Add(CreateCpuValueLabel(keys[index], captions[index], clickable), index % 4, index / 4);
            }
            return grid;
        }

        private Control CreateFlowGrid()
        {
            TableLayoutPanel grid = CreateGrid(2, 3);
            grid.Controls.Add(CreateCpuValueLabel("PC", "PC", clickable: true), 0, 0);
            grid.Controls.Add(CreateCpuValueLabel("SP", "SP", clickable: true), 1, 0);
            grid.Controls.Add(CreateCpuValueLabel("IX", "IX", clickable: true), 0, 1);
            grid.Controls.Add(CreateCpuValueLabel("IY", "IY", clickable: true), 1, 1);

            currentInstructionLabel.Name = "CpuCurrentInstruction";
            currentInstructionLabel.Dock = DockStyle.Fill;
            currentInstructionLabel.Margin = Padding.Empty;
            currentInstructionLabel.Font = EmulatorUiStyle.MonoFont(7.5f, FontStyle.Bold);
            currentInstructionLabel.ForeColor = EmulatorUiStyle.Accent;
            currentInstructionLabel.TextAlign = ContentAlignment.MiddleLeft;
            currentInstructionLabel.AutoEllipsis = true;
            currentInstructionLabel.Cursor = Cursors.Hand;
            currentInstructionLabel.Click += (_, _) => NavigateFromCpuState("PC");
            grid.Controls.Add(currentInstructionLabel, 0, 2);
            grid.SetColumnSpan(currentInstructionLabel, 2);
            return grid;
        }

        private Control CreateProcessorStateGrid()
        {
            TableLayoutPanel panel = CreateGrid(1, 2);
            panel.RowStyles.Clear();
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            var flags = CreateGrid(8, 1);
            const string flagNames = "SZ5H3PNC";
            for (int index = 0; index < cpuFlagLabels.Length; index++)
            {
                Label flag = cpuFlagLabels[index];
                flag.Name = $"CpuFlag{flagNames[index]}";
                flag.Dock = DockStyle.Fill;
                flag.Margin = new Padding(1, 0, 1, 1);
                flag.Font = EmulatorUiStyle.MonoFont(7.2f, FontStyle.Bold);
                flag.Text = flagNames[index].ToString();
                flag.TextAlign = ContentAlignment.MiddleCenter;
                flags.Controls.Add(flag, index, 0);
            }

            TableLayoutPanel state = CreateGrid(4, 2);
            state.Controls.Add(CreateCpuValueLabel("I", "I"), 0, 0);
            state.Controls.Add(CreateCpuValueLabel("R", "R"), 1, 0);
            state.Controls.Add(CreateCpuValueLabel("IM", "IM"), 2, 0);
            state.Controls.Add(CreateCpuValueLabel("IFF", "IFF"), 3, 0);
            state.Controls.Add(CreateCpuValueLabel("HALT", "HALT"), 0, 1);
            state.Controls.Add(CreateCpuValueLabel("INT", "INT"), 1, 1);
            Label tStates = CreateCpuValueLabel("T", "T");
            state.Controls.Add(tStates, 2, 1);
            state.SetColumnSpan(tStates, 2);

            panel.Controls.Add(flags, 0, 0);
            panel.Controls.Add(state, 0, 1);
            return panel;
        }

        private Control CreateStackGrid()
        {
            TableLayoutPanel grid = CreateGrid(2, 2);
            for (int index = 0; index < 4; index++)
            {
                string key = $"Stack{index}";
                grid.Controls.Add(CreateCpuValueLabel(key, $"SP+{index * 2:X2}", clickable: true), index % 2, index / 2);
            }
            return grid;
        }

        private static TableLayoutPanel CreateGrid(int columns, int rows)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columns,
                RowCount = rows,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = CpuStateBackground,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            grid.ColumnStyles.Clear();
            grid.RowStyles.Clear();
            for (int column = 0; column < columns; column++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int row = 0; row < rows; row++)
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 18f));
            return grid;
        }

        private Label CreateCpuValueLabel(string key, string caption, bool clickable = false)
        {
            Label label;
            if (clickable)
            {
                var link = new LinkLabel
                {
                    LinkBehavior = LinkBehavior.NeverUnderline,
                    LinkColor = EmulatorUiStyle.Accent,
                    ActiveLinkColor = Color.FromArgb(5, 90, 75),
                    VisitedLinkColor = EmulatorUiStyle.Accent,
                    Cursor = Cursors.Hand
                };
                link.LinkClicked += (_, _) => NavigateFromCpuState(key);
                label = link;
            }
            else
                label = new Label();

            label.Name = $"CpuValue{key}";
            label.Tag = caption;
            label.Dock = DockStyle.Fill;
            label.Margin = Padding.Empty;
            label.Font = EmulatorUiStyle.MonoFont(7.5f);
            label.ForeColor = Ink;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            cpuValueLabels.Add(key, label);
            return label;
        }

        private void UpdateCpuState()
        {
            DisassemblyCpuState? cpu = snapshot.CpuState;
            if (cpu == null)
            {
                foreach ((string key, Label label) in cpuValueLabels)
                    SetCpuValue(key, "--", changed: false);
                foreach (Label flag in cpuFlagLabels)
                    flag.BackColor = CpuFlagInactive;
                currentInstructionLabel.Text = "CPU state unavailable";
                return;
            }

            DisassemblyCpuState? previous = previousCpuState;
            SetCpuValue("AF", cpu.AF, previous?.AF);
            SetCpuValue("BC", cpu.BC, previous?.BC);
            SetCpuValue("DE", cpu.DE, previous?.DE);
            SetCpuValue("HL", cpu.HL, previous?.HL);
            SetCpuValue("AF2", cpu.AlternateAF, previous?.AlternateAF);
            SetCpuValue("BC2", cpu.AlternateBC, previous?.AlternateBC);
            SetCpuValue("DE2", cpu.AlternateDE, previous?.AlternateDE);
            SetCpuValue("HL2", cpu.AlternateHL, previous?.AlternateHL);
            SetCpuValue("PC", cpu.PC, previous?.PC);
            SetCpuValue("SP", cpu.SP, previous?.SP);
            SetCpuValue("IX", cpu.IX, previous?.IX);
            SetCpuValue("IY", cpu.IY, previous?.IY);
            SetCpuValue("I", cpu.I.ToString("X2"), previous != null && cpu.I != previous.I);
            SetCpuValue("R", cpu.R.ToString("X2"), previous != null && cpu.R != previous.R);
            SetCpuValue("IM", cpu.InterruptMode.ToString(), previous != null && cpu.InterruptMode != previous.InterruptMode);
            SetCpuValue("IFF", $"{Bit(cpu.IFF1)}/{Bit(cpu.IFF2)}", previous != null && (cpu.IFF1 != previous.IFF1 || cpu.IFF2 != previous.IFF2));
            SetCpuValue("HALT", Bit(cpu.IsHalted), previous != null && cpu.IsHalted != previous.IsHalted);
            SetCpuValue("INT", Bit(cpu.InterruptPending), previous != null && cpu.InterruptPending != previous.InterruptPending);
            SetCpuValue("T", cpu.TStates.ToString(), previous != null && cpu.TStates != previous.TStates);

            for (int index = 0; index < 4; index++)
            {
                ushort value = index < cpu.StackWords.Count ? cpu.StackWords[index] : (ushort)0;
                ushort? previousValue = previous != null && index < previous.StackWords.Count
                    ? previous.StackWords[index]
                    : null;
                SetCpuValue($"Stack{index}", value, previousValue);
            }

            UpdateFlagIndicators((byte)cpu.AF, previous == null ? null : (byte?)previous.AF);
            currentInstructionLabel.Text = "@PC " + Z80InstructionDisassembler
                .Disassemble(cpu.PC, snapshot.ReadMemory)
                .Mnemonic;
        }

        private void SetCpuValue(string key, ushort value, ushort? previous) =>
            SetCpuValue(key, value.ToString("X4"), previous.HasValue && value != previous.Value);

        private void SetCpuValue(string key, string value, bool changed)
        {
            Label label = cpuValueLabels[key];
            label.Text = $"{label.Tag} {value}";
            label.BackColor = changed ? CpuStateChanged : CpuStateBackground;
        }

        private void UpdateFlagIndicators(byte flags, byte? previousFlags)
        {
            for (int index = 0; index < cpuFlagLabels.Length; index++)
            {
                int mask = 0x80 >> index;
                bool active = (flags & mask) != 0;
                bool changed = previousFlags.HasValue && ((flags ^ previousFlags.Value) & mask) != 0;
                Label flag = cpuFlagLabels[index];
                flag.BackColor = changed ? CpuStateChanged : active ? CpuFlagActive : CpuFlagInactive;
                flag.ForeColor = active ? Ink : EmulatorUiStyle.Muted;
            }
        }

        private void NavigateFromCpuState(string key)
        {
            DisassemblyCpuState? cpu = snapshot.CpuState;
            if (cpu == null || !TryGetCpuAddress(cpu, key, out ushort address))
                return;

            if (key == "PC" || key.StartsWith("Stack", StringComparison.Ordinal))
            {
                PreserveCurrentHistorySelection();
                SetSelectedAddressSpace(DisassemblyAddressSpace.Mapped);
                NavigateTo(address, status: $"Showing mapped address {address:X4}H from {cpuValueLabels[key].Tag}.");
                return;
            }

            InspectMemory(
                new DisassemblyLocation(DisassemblyAddressSpace.Mapped, address),
                $"Inspecting mapped address {address:X4}H from {cpuValueLabels[key].Tag}.");
        }

        private static bool TryGetCpuAddress(DisassemblyCpuState cpu, string key, out ushort address)
        {
            address = key switch
            {
                "BC" => cpu.BC,
                "DE" => cpu.DE,
                "HL" => cpu.HL,
                "BC2" => cpu.AlternateBC,
                "DE2" => cpu.AlternateDE,
                "HL2" => cpu.AlternateHL,
                "PC" => cpu.PC,
                "SP" => cpu.SP,
                "IX" => cpu.IX,
                "IY" => cpu.IY,
                "Stack0" when cpu.StackWords.Count > 0 => cpu.StackWords[0],
                "Stack1" when cpu.StackWords.Count > 1 => cpu.StackWords[1],
                "Stack2" when cpu.StackWords.Count > 2 => cpu.StackWords[2],
                "Stack3" when cpu.StackWords.Count > 3 => cpu.StackWords[3],
                _ => 0
            };
            return key is "BC" or "DE" or "HL" or "BC2" or "DE2" or "HL2" or "PC" or "SP" or "IX" or "IY" ||
                key.StartsWith("Stack", StringComparison.Ordinal) && int.TryParse(key.AsSpan(5), out int index) && index < cpu.StackWords.Count;
        }

        private static string Bit(bool value) => value ? "1" : "0";
    }
}
