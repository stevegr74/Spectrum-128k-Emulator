namespace Spectrum128kEmulator
{
    public sealed class EmulatorHelpForm : Form
    {
        private readonly ListBox sectionList = new();
        private readonly GroupBox detailsGroup = new();
        private readonly Panel detailsPanel = new();

        private static readonly HelpSection[] Sections =
        [
            new("Display and state",
            [
                ("F1", "Show or close this help window"),
                ("F2", "Show or hide compact FPS, model, tape, and display diagnostics"),
                ("F3", "Reset and switch between 128K and 48K machine modes"),
                ("F4", "Cycle 1x Native, 2x Enhanced, and 3x Enhanced"),
                ("F5", "Stop or resume tape; its state appears in the on-screen transport badge"),
                ("F6", "Open or close the paused debugger and disassembler"),
                ("F7", "Save the current machine state to the temporary Quick State slot"),
                ("F8", "Restore the temporary Quick State; unavailable until one is saved"),
                ("Right-click", "Open the quick menu for joystick selection and the Poke Manager")
            ]),
            new("Loading and diagnostics",
            [
                ("F9", "Load a 48K or 128K .sna snapshot"),
                ("F10", "Load a .z80 snapshot or play an .rzx recording"),
                ("F11", "Mount a .tap or .tzx tape image"),
                ("Shift+F11", "Replace the mounted tape while preserving the running machine for multi-side releases"),
                ("F12", "Write a machine diagnostic dump")
            ]),
            new("Debugger window",
            [
                ("Arrow keys", "Move through listing rows; Enter follows a direct branch target"),
                ("Alt+Left / Right", "Move backward or forward through address history"),
                ("Ctrl+G / Ctrl+P", "Focus address entry or return to the captured PC"),
                ("Ctrl+F / F3", "Focus search or find the next address, byte, or mnemonic match"),
                ("F5", "Run or request a pause at the next instruction boundary"),
                ("F9", "Toggle a breakpoint on the selected instruction"),
                ("F10 / F11", "Step over a call or step into one instruction"),
                ("Shift+F11", "Step out using the return address currently on the stack"),
                ("Ctrl+F10", "Run to the selected instruction without adding a breakpoint"),
                ("Ctrl+R", "Refresh the immutable capture while remaining paused"),
                ("Ctrl+C", "Copy selected rows with capture metadata"),
                ("Ctrl+Shift+C", "Copy every displayed row with capture metadata"),
                ("Memory view", "Select mapped 64K, ROM banks, or RAM banks for the listing"),
                ("CPU state links", "Inspect register addresses in Memory; use PC or stack values to navigate the listing"),
                ("Memory tab", "Inspect hex/ASCII bytes from registers, stack, listing, or targets"),
                ("Ctrl+L / Right-click", "Edit labels, mark code/data/unknown, manage symbols, or inspect memory"),
                ("Ctrl+E", "Export a complete 64K mapped or 16K physical-bank listing")
            ]),
            new("Spectrum keyboard",
            [
                ("Letters / numbers", "Map to their Spectrum keys"),
                ("Enter / Space", "Map directly to the Spectrum keyboard"),
                ("Shift", "Maps to Spectrum CAPS SHIFT"),
                ("Ctrl / Alt", "Map to Spectrum SYMBOL SHIFT"),
                ("PC punctuation", "Maps common BASIC punctuation to Spectrum SYMBOL SHIFT chords"),
                ("Arrow keys", "Provide cursor-style Spectrum input chords")
            ]),
            new("Joystick",
            [
                ("Controller 1 / 2", "Configure and monitor two independent host controllers"),
                ("Off", "Disconnect that controller channel from the emulated machine"),
                ("Automatic", "Use the first host controller not claimed by the other channel"),
                ("Kempston", "Expose active-high directions and Fire through port 1FH"),
                ("Cursor", "Map directions and Fire to keys 5, 8, 6, 7, and 0"),
                ("Sinclair", "Sinclair 1 uses keys 1-5 (socket 2); Sinclair 2 uses keys 6-0 (socket 1)"),
                ("Configure", "Map both controllers; duplicate devices and interfaces are rejected")
            ])
        ];

        public EmulatorHelpForm()
        {
            Text = "Spectrum 128K Emulator Help";
            ClientSize = new Size(930, 600);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            EmulatorUiStyle.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 14, 18, 14),
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.Controls.Add(CreateHeader(), 0, 0);
            root.Controls.Add(CreateBody(), 0, 1);
            root.Controls.Add(CreateFooter(), 0, 2);
            Controls.Add(root);

            sectionList.SelectedIndexChanged += (_, _) => ShowSelectedSection();
            sectionList.SelectedIndex = 0;
            KeyDown += EmulatorHelpForm_KeyDown;
        }

        private static Control CreateHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            Label title = EmulatorUiStyle.CreateTitle("CONTROLS AND SHORTCUTS");
            title.Location = new Point(0, 0);
            Label subtitle = EmulatorUiStyle.CreateSubtitle("Press F1 or Escape to close this window.");
            subtitle.Location = new Point(1, 29);
            Label reference = EmulatorUiStyle.CreateSubtitle("QUICK REFERENCE", accent: true);
            reference.AutoSize = false;
            reference.Dock = DockStyle.Right;
            reference.Width = 140;
            reference.Padding = new Padding(0, 4, 0, 0);
            reference.TextAlign = ContentAlignment.TopRight;
            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            panel.Controls.Add(reference);
            return panel;
        }

        private Control CreateBody()
        {
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var sectionsGroup = new GroupBox { Text = "Sections", Dock = DockStyle.Fill, Padding = new Padding(10) };
            EmulatorUiStyle.StyleGroup(sectionsGroup);
            sectionList.Dock = DockStyle.Fill;
            sectionList.BorderStyle = BorderStyle.None;
            sectionList.BackColor = EmulatorUiStyle.Paper;
            sectionList.ForeColor = EmulatorUiStyle.Ink;
            sectionList.Font = EmulatorUiStyle.UiFont(9F);
            sectionList.DrawMode = DrawMode.OwnerDrawFixed;
            sectionList.ItemHeight = 34;
            sectionList.Items.AddRange(Sections.Cast<object>().ToArray());
            sectionList.DrawItem += DrawSectionItem;
            sectionsGroup.Controls.Add(sectionList);

            detailsGroup.Dock = DockStyle.Fill;
            detailsGroup.Margin = new Padding(12, 0, 0, 0);
            detailsGroup.Padding = new Padding(12, 8, 12, 12);
            EmulatorUiStyle.StyleGroup(detailsGroup);
            detailsPanel.Dock = DockStyle.Fill;
            detailsPanel.AutoScroll = true;
            detailsGroup.Controls.Add(detailsPanel);

            body.Controls.Add(sectionsGroup, 0, 0);
            body.Controls.Add(detailsGroup, 1, 0);
            return body;
        }

        private Control CreateFooter()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 9, 0, 0)
            };
            var close = new Button { Text = "Close", Width = 90, Height = 30 };
            EmulatorUiStyle.StyleButton(close, primary: true);
            close.Click += (_, _) => Close();
            panel.Controls.Add(close);
            return panel;
        }

        private void ShowSelectedSection()
        {
            if (sectionList.SelectedItem is not HelpSection section)
                return;

            detailsGroup.Text = section.Title;
            detailsPanel.Controls.Clear();
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = section.Rows.Length,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int row = 0; row < section.Rows.Length; row++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
                table.Controls.Add(new Label
                {
                    Text = section.Rows[row].Key,
                    Dock = DockStyle.Fill,
                    Font = EmulatorUiStyle.MonoFont(9F, FontStyle.Bold),
                    ForeColor = EmulatorUiStyle.Accent,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 0, 0, 0)
                }, 0, row);
                table.Controls.Add(new Label
                {
                    Text = section.Rows[row].Description,
                    Dock = DockStyle.Fill,
                    Font = EmulatorUiStyle.UiFont(9F),
                    ForeColor = EmulatorUiStyle.Ink,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 0, 0, 0)
                }, 1, row);
            }
            detailsPanel.Controls.Add(table);
        }

        private void DrawSectionItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? EmulatorUiStyle.AccentSoft : EmulatorUiStyle.Paper);
            using var foreground = new SolidBrush(selected ? EmulatorUiStyle.Accent : EmulatorUiStyle.Ink);
            e.Graphics.FillRectangle(background, e.Bounds);
            e.Graphics.DrawString(sectionList.Items[e.Index].ToString(), sectionList.Font, foreground, e.Bounds.X + 8, e.Bounds.Y + 8);
            e.DrawFocusRectangle();
        }

        private void EmulatorHelpForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is not (Keys.F1 or Keys.Escape))
                return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            Close();
        }

        private sealed record HelpSection(string Title, (string Key, string Description)[] Rows)
        {
            public override string ToString() => Title;
        }
    }
}
