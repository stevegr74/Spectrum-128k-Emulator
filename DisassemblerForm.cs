using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed class DisassemblerForm : Form
    {
        private const int InstructionCount = 96;
        private static readonly Color Ink = Color.FromArgb(30, 39, 46);
        private static readonly Color Accent = Color.FromArgb(0, 130, 155);
        private static readonly Color Paper = Color.FromArgb(242, 239, 228);

        private readonly Func<DisassemblySnapshot> captureSnapshot;
        private readonly TextBox addressTextBox = new TextBox();
        private readonly Label mappingLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly ListView instructionList = new ListView();
        private readonly Font listingFont = new Font("Consolas", 10.0f, FontStyle.Regular);
        private readonly Font currentInstructionFont = new Font("Consolas", 10.0f, FontStyle.Bold);
        private DisassemblySnapshot snapshot;
        private ushort listingAddress;

        public DisassemblerForm(Func<DisassemblySnapshot> captureSnapshot)
        {
            this.captureSnapshot = captureSnapshot ?? throw new ArgumentNullException(nameof(captureSnapshot));
            snapshot = captureSnapshot();

            Text = "Z80 Disassembler";
            ClientSize = new Size(880, 640);
            MinimumSize = new Size(720, 480);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Paper;

            Controls.Add(CreateContent());
            Controls.Add(CreateToolbar());
            Controls.Add(CreateHeader());

            KeyDown += DisassemblerForm_KeyDown;
            Shown += (_, _) => NavigateTo(snapshot.ProgramCounter);
        }

        private Control CreateHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = Ink,
                Padding = new Padding(20, 11, 20, 8)
            };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Z80 DISASSEMBLER",
                Font = new Font("Consolas", 17.0f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(18, 10)
            });
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Read-only memory view. Emulator execution is paused.",
                Font = new Font(FontFamily.GenericSansSerif, 9.0f),
                ForeColor = Color.FromArgb(185, 214, 218),
                Location = new Point(20, 45)
            });
            return header;
        }

        private Control CreateToolbar()
        {
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Color.FromArgb(222, 226, 220),
                Padding = new Padding(18, 10, 18, 8)
            };

            var addressLabel = new Label
            {
                AutoSize = true,
                Text = "ADDRESS (HEX)",
                Font = new Font(FontFamily.GenericSansSerif, 8.0f, FontStyle.Bold),
                ForeColor = Ink,
                Location = new Point(18, 10)
            };
            addressTextBox.Location = new Point(18, 31);
            addressTextBox.Size = new Size(92, 24);
            addressTextBox.Font = listingFont;
            addressTextBox.MaxLength = 6;
            addressTextBox.KeyDown += AddressTextBox_KeyDown;

            Button goButton = CreateButton("Go", 120, 29, 54);
            goButton.Click += (_, _) => NavigateFromAddressBox();
            Button pcButton = CreateButton("Go to PC", 184, 29, 82);
            pcButton.Click += (_, _) => NavigateTo(snapshot.ProgramCounter);
            Button refreshButton = CreateButton("Refresh", 276, 29, 76);
            refreshButton.Click += (_, _) => RefreshSnapshot();
            Button copyButton = CreateButton("Copy", 362, 29, 68);
            copyButton.Click += (_, _) => CopyListing();

            mappingLabel.AutoSize = false;
            mappingLabel.Location = new Point(448, 11);
            mappingLabel.Size = new Size(410, 45);
            mappingLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            mappingLabel.Font = new Font("Consolas", 8.5f, FontStyle.Regular);
            mappingLabel.ForeColor = Color.FromArgb(50, 64, 68);
            mappingLabel.TextAlign = ContentAlignment.MiddleLeft;

            toolbar.Controls.AddRange([addressLabel, addressTextBox, goButton, pcButton, refreshButton, copyButton, mappingLabel]);
            return toolbar;
        }

        private Control CreateContent()
        {
            var content = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 12, 18, 12),
                BackColor = Paper
            };

            instructionList.Dock = DockStyle.Fill;
            instructionList.View = View.Details;
            instructionList.FullRowSelect = true;
            instructionList.HideSelection = false;
            instructionList.MultiSelect = true;
            instructionList.GridLines = false;
            instructionList.Font = listingFont;
            instructionList.BackColor = Color.FromArgb(251, 250, 245);
            instructionList.ForeColor = Ink;
            instructionList.Columns.Add("Address", 90, HorizontalAlignment.Left);
            instructionList.Columns.Add("Bytes", 170, HorizontalAlignment.Left);
            instructionList.Columns.Add("Instruction", 420, HorizontalAlignment.Left);
            instructionList.Columns.Add("Target", 100, HorizontalAlignment.Left);
            instructionList.DoubleClick += (_, _) => NavigateToSelectedTarget();

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 27;
            statusLabel.Font = new Font(FontFamily.GenericSansSerif, 8.5f);
            statusLabel.ForeColor = Color.FromArgb(75, 75, 70);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            content.Controls.Add(instructionList);
            content.Controls.Add(statusLabel);
            return content;
        }

        private static Button CreateButton(string text, int x, int y, int width)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 27),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Ink,
                UseVisualStyleBackColor = false
            };
        }

        private void RefreshSnapshot()
        {
            snapshot = captureSnapshot();
            PopulateListing();
            statusLabel.Text = $"Memory refreshed. PC={snapshot.ProgramCounter:X4}H. Double-click a branch target to follow it.";
        }

        private void NavigateFromAddressBox()
        {
            if (!DisassemblyAddressParser.TryParse(addressTextBox.Text, out ushort address))
            {
                statusLabel.Text = "Enter a hexadecimal address from 0000 to FFFF.";
                addressTextBox.SelectAll();
                addressTextBox.Focus();
                return;
            }

            NavigateTo(address);
        }

        private void NavigateTo(ushort address)
        {
            listingAddress = address;
            addressTextBox.Text = address.ToString("X4");
            PopulateListing();
            statusLabel.Text = $"Showing {InstructionCount} instructions from {address:X4}H. Double-click a branch target to follow it.";
        }

        private void PopulateListing()
        {
            IReadOnlyList<Z80Instruction> instructions = Z80InstructionDisassembler.DisassembleBlock(
                listingAddress,
                InstructionCount,
                snapshot.ReadMemory);

            instructionList.BeginUpdate();
            try
            {
                instructionList.Items.Clear();
                foreach (Z80Instruction instruction in instructions)
                {
                    var item = new ListViewItem(instruction.Address.ToString("X4"))
                    {
                        Tag = instruction
                    };
                    item.SubItems.Add(instruction.BytesText);
                    item.SubItems.Add(instruction.Mnemonic);
                    item.SubItems.Add(instruction.BranchTarget is ushort target ? target.ToString("X4") : string.Empty);

                    if (instruction.Address == snapshot.ProgramCounter)
                    {
                        item.BackColor = Color.FromArgb(255, 230, 145);
                        item.ForeColor = Color.FromArgb(65, 45, 0);
                        item.Font = currentInstructionFont;
                    }

                    instructionList.Items.Add(item);
                }
            }
            finally
            {
                instructionList.EndUpdate();
            }

            mappingLabel.Text = $"PC {snapshot.ProgramCounter:X4}H\r\n{snapshot.MappingText}";
        }

        private void NavigateToSelectedTarget()
        {
            if (instructionList.SelectedItems.Count != 1 ||
                instructionList.SelectedItems[0].Tag is not Z80Instruction { BranchTarget: ushort target })
            {
                return;
            }

            NavigateTo(target);
        }

        private void CopyListing()
        {
            IEnumerable<ListViewItem> rows = instructionList.SelectedItems.Count > 0
                ? instructionList.SelectedItems.Cast<ListViewItem>()
                : instructionList.Items.Cast<ListViewItem>();
            string text = string.Join(Environment.NewLine, rows.Select(FormatRow));
            if (text.Length == 0)
                return;

            Clipboard.SetText(text);
            statusLabel.Text = instructionList.SelectedItems.Count > 0
                ? $"Copied {instructionList.SelectedItems.Count} selected instruction(s)."
                : $"Copied all {instructionList.Items.Count} displayed instructions.";
        }

        private static string FormatRow(ListViewItem item)
        {
            return $"{item.Text}  {item.SubItems[1].Text,-14}  {item.SubItems[2].Text}";
        }

        private void AddressTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;

            NavigateFromAddressBox();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void DisassemblerForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is not (Keys.F6 or Keys.Escape))
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                listingFont.Dispose();
                currentInstructionFont.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
