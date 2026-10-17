using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed class DisassemblerForm : Form
    {
        private const int InstructionCount = 96;
        private static readonly Color Ink = Color.FromArgb(30, 39, 46);
        private static readonly Color Paper = Color.FromArgb(242, 239, 228);

        private readonly Func<DisassemblySnapshot> captureSnapshot;
        private readonly DisassemblyNavigationHistory navigationHistory = new();
        private readonly DisassemblerWindowSettingsStore settingsStore;
        private readonly DisassemblerWindowSettings settings;
        private readonly TextBox addressTextBox = new();
        private readonly TextBox searchTextBox = new();
        private readonly ComboBox searchModeComboBox = new();
        private readonly Label mappingLabel = new();
        private readonly Label captureStateLabel = new();
        private readonly Label statusLabel = new();
        private readonly ListView instructionList = new();
        private readonly Button backButton;
        private readonly Button forwardButton;
        private readonly Font listingFont = new("Consolas", 10.0f, FontStyle.Regular);
        private readonly Font currentInstructionFont = new("Consolas", 10.0f, FontStyle.Bold);
        private readonly System.Windows.Forms.Timer captureAgeTimer = new() { Interval = 1000 };
        private DisassemblySnapshot snapshot;
        private ushort listingAddress;
        private string? lastSearchQuery;
        private DisassemblySearchMode? lastSearchMode;
        private ushort lastSearchAddress;

        public DisassemblerForm(Func<DisassemblySnapshot> captureSnapshot)
            : this(captureSnapshot, new DisassemblerWindowSettingsStore())
        {
        }

        internal DisassemblerForm(
            Func<DisassemblySnapshot> captureSnapshot,
            DisassemblerWindowSettingsStore settingsStore)
        {
            this.captureSnapshot = captureSnapshot ?? throw new ArgumentNullException(nameof(captureSnapshot));
            this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            settings = settingsStore.Load();
            snapshot = captureSnapshot();
            backButton = CreateButton("< Back", 18, 29, 68);
            forwardButton = CreateButton("Forward >", 94, 29, 82);

            Text = "Z80 Disassembler - Paused Capture";
            MinimumSize = new Size(760, 540);
            ClientSize = new Size(
                Math.Clamp(settings.Width, MinimumSize.Width, 1800),
                Math.Clamp(settings.Height, MinimumSize.Height, 1200));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            BackColor = Paper;

            Controls.Add(CreateContent());
            Controls.Add(CreateToolbar());
            Controls.Add(CreateHeader());
            ApplySavedColumnWidths();

            KeyDown += DisassemblerForm_KeyDown;
            FormClosing += (_, _) => SaveWindowSettings();
            captureAgeTimer.Tick += (_, _) => UpdateCaptureState();
            Shown += (_, _) =>
            {
                NavigateTo(settings.LastListingAddress ?? snapshot.ProgramCounter);
                captureAgeTimer.Start();
                instructionList.Focus();
            };
        }

        private Control CreateHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = Ink,
                Padding = new Padding(20, 9, 20, 7)
            };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Z80 DISASSEMBLER",
                Font = new Font("Consolas", 17.0f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(18, 8)
            });

            captureStateLabel.AutoSize = true;
            captureStateLabel.Font = new Font("Consolas", 9.0f, FontStyle.Bold);
            captureStateLabel.ForeColor = Color.FromArgb(95, 225, 195);
            captureStateLabel.Location = new Point(20, 42);
            header.Controls.Add(captureStateLabel);
            UpdateCaptureState();
            return header;
        }

        private Control CreateToolbar()
        {
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 112,
                BackColor = Color.FromArgb(222, 226, 220),
                Padding = new Padding(18, 8, 18, 8)
            };

            toolbar.Controls.Add(CreateToolbarLabel("NAVIGATION", 18, 8));
            backButton.Click += (_, _) => GoBack();
            forwardButton.Click += (_, _) => GoForward();
            toolbar.Controls.Add(backButton);
            toolbar.Controls.Add(forwardButton);

            toolbar.Controls.Add(CreateToolbarLabel("ADDRESS", 190, 8));
            addressTextBox.Location = new Point(190, 31);
            addressTextBox.Size = new Size(88, 24);
            addressTextBox.Font = listingFont;
            addressTextBox.MaxLength = 6;
            addressTextBox.KeyDown += AddressTextBox_KeyDown;
            toolbar.Controls.Add(addressTextBox);

            Button goButton = CreateButton("Go", 286, 29, 50);
            goButton.Click += (_, _) => NavigateFromAddressBox();
            toolbar.Controls.Add(goButton);
            Button pcButton = CreateButton("PC", 344, 29, 48);
            pcButton.Click += (_, _) => NavigateTo(snapshot.ProgramCounter);
            toolbar.Controls.Add(pcButton);
            Button refreshButton = CreateButton("Refresh", 400, 29, 72);
            refreshButton.Click += (_, _) => RefreshSnapshot();
            toolbar.Controls.Add(refreshButton);
            Button copyButton = CreateButton("Copy", 480, 29, 62);
            copyButton.Click += (_, _) => CopyListing(selectedOnly: true);
            toolbar.Controls.Add(copyButton);
            Button exportButton = CreateButton("Export 64K", 550, 29, 78);
            exportButton.Click += (_, _) => ExportListing();
            toolbar.Controls.Add(exportButton);

            mappingLabel.AutoSize = false;
            mappingLabel.Location = new Point(642, 8);
            mappingLabel.Size = new Size(318, 50);
            mappingLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            mappingLabel.Font = new Font("Consolas", 8.2f, FontStyle.Regular);
            mappingLabel.ForeColor = Color.FromArgb(50, 64, 68);
            mappingLabel.TextAlign = ContentAlignment.MiddleLeft;
            toolbar.Controls.Add(mappingLabel);

            toolbar.Controls.Add(CreateToolbarLabel("FIND IN CAPTURE", 18, 65));
            searchModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            searchModeComboBox.Items.AddRange(Enum.GetNames<DisassemblySearchMode>());
            searchModeComboBox.SelectedIndex = 0;
            searchModeComboBox.Location = new Point(18, 83);
            searchModeComboBox.Size = new Size(105, 24);
            toolbar.Controls.Add(searchModeComboBox);

            searchTextBox.Location = new Point(131, 83);
            searchTextBox.Size = new Size(270, 24);
            searchTextBox.Font = listingFont;
            searchTextBox.PlaceholderText = "8000, 3E 10, or CALL";
            searchTextBox.KeyDown += SearchTextBox_KeyDown;
            toolbar.Controls.Add(searchTextBox);

            Button findButton = CreateButton("Find Next", 409, 81, 86);
            findButton.Click += (_, _) => FindNext();
            toolbar.Controls.Add(findButton);
            toolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Ctrl+F find | F3 next | Alt+Left/Right history | Enter follows target",
                Font = new Font(FontFamily.GenericSansSerif, 8.0f),
                ForeColor = Color.FromArgb(65, 75, 75),
                Location = new Point(510, 87)
            });

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
            instructionList.Columns.Add("Instruction", 480, HorizontalAlignment.Left);
            instructionList.Columns.Add("Target", 100, HorizontalAlignment.Left);
            instructionList.DoubleClick += (_, _) => NavigateToSelectedTarget();
            instructionList.ContextMenuStrip = CreateListingContextMenu();

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 29;
            statusLabel.Font = new Font(FontFamily.GenericSansSerif, 8.5f);
            statusLabel.ForeColor = Color.FromArgb(75, 75, 70);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            content.Controls.Add(instructionList);
            content.Controls.Add(statusLabel);
            return content;
        }

        private ContextMenuStrip CreateListingContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Follow Target", null, (_, _) => NavigateToSelectedTarget());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Copy Selected", null, (_, _) => CopyListing(selectedOnly: true));
            menu.Items.Add("Copy All Displayed", null, (_, _) => CopyListing(selectedOnly: false));
            menu.Items.Add("Export Complete 64K Listing...", null, (_, _) => ExportListing());
            menu.Opening += (_, _) => menu.Items[0].Enabled = SelectedInstruction()?.BranchTarget != null;
            return menu;
        }

        private static Label CreateToolbarLabel(string text, int x, int y)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                Font = new Font(FontFamily.GenericSansSerif, 7.5f, FontStyle.Bold),
                ForeColor = Ink,
                Location = new Point(x, y)
            };
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
            ushort selectedAddress = SelectedInstruction()?.Address ?? listingAddress;
            snapshot = captureSnapshot();
            PopulateListing(selectedAddress);
            UpdateCaptureState();
            statusLabel.Text = $"Capture refreshed at PC={snapshot.ProgramCounter:X4}H; listing remains at {listingAddress:X4}H.";
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

        private void NavigateTo(
            ushort address,
            bool recordHistory = true,
            string? status = null,
            ushort? selectedAddress = null)
        {
            ushort selection = selectedAddress ?? address;
            if (recordHistory)
            {
                PreserveCurrentHistorySelection();
                navigationHistory.NavigateTo(new DisassemblyNavigationLocation(address, selection));
            }

            listingAddress = address;
            addressTextBox.Text = address.ToString("X4");
            PopulateListing(selection);
            UpdateNavigationButtons();
            statusLabel.Text = status ?? $"Showing {InstructionCount} instructions from {address:X4}H in the frozen capture.";
        }

        private void GoBack()
        {
            PreserveCurrentHistorySelection();
            if (navigationHistory.TryGoBack(out DisassemblyNavigationLocation location))
            {
                NavigateTo(
                    location.ListingAddress,
                    recordHistory: false,
                    status: $"Back to {location.SelectedAddress:X4}H.",
                    selectedAddress: location.SelectedAddress);
            }
        }

        private void GoForward()
        {
            PreserveCurrentHistorySelection();
            if (navigationHistory.TryGoForward(out DisassemblyNavigationLocation location))
            {
                NavigateTo(
                    location.ListingAddress,
                    recordHistory: false,
                    status: $"Forward to {location.SelectedAddress:X4}H.",
                    selectedAddress: location.SelectedAddress);
            }
        }

        private void PreserveCurrentHistorySelection()
        {
            if (SelectedInstruction() is Z80Instruction instruction)
                navigationHistory.UpdateCurrentSelection(instruction.Address);
        }

        private void UpdateNavigationButtons()
        {
            backButton.Enabled = navigationHistory.CanGoBack;
            forwardButton.Enabled = navigationHistory.CanGoForward;
        }

        private void PopulateListing(ushort selectedAddress)
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
                    var item = new ListViewItem(instruction.Address.ToString("X4")) { Tag = instruction };
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

                ListViewItem? itemToSelect = instructionList.Items
                    .Cast<ListViewItem>()
                    .FirstOrDefault(item => ((Z80Instruction)item.Tag!).Address == selectedAddress);
                itemToSelect ??= instructionList.Items.Count > 0 ? instructionList.Items[0] : null;
                if (itemToSelect != null)
                {
                    itemToSelect.Selected = true;
                    itemToSelect.Focused = true;
                    itemToSelect.EnsureVisible();
                }
            }
            finally
            {
                instructionList.EndUpdate();
            }

            mappingLabel.Text = $"CAPTURED PC {snapshot.ProgramCounter:X4}H | LISTING {listingAddress:X4}H\r\n{snapshot.MappingText}";
        }

        private void NavigateToSelectedTarget()
        {
            if (SelectedInstruction()?.BranchTarget is ushort target)
                NavigateTo(target);
            else
                statusLabel.Text = "The selected instruction has no direct branch target.";
        }

        private Z80Instruction? SelectedInstruction()
        {
            return instructionList.SelectedItems.Count == 1
                ? instructionList.SelectedItems[0].Tag as Z80Instruction
                : null;
        }

        private void FindNext()
        {
            var mode = (DisassemblySearchMode)searchModeComboBox.SelectedIndex;
            string query = searchTextBox.Text.Trim();
            bool sameSearch = lastSearchMode == mode && string.Equals(lastSearchQuery, query, StringComparison.OrdinalIgnoreCase);
            ushort startAddress = sameSearch ? (ushort)(lastSearchAddress + 1) : listingAddress;

            if (!DisassemblySearch.TryFindNext(snapshot, mode, query, startAddress, out DisassemblySearchResult? result, out string? error))
            {
                statusLabel.Text = error ?? "No match found.";
                searchTextBox.SelectAll();
                searchTextBox.Focus();
                return;
            }

            lastSearchMode = mode;
            lastSearchQuery = query;
            lastSearchAddress = result!.Address;
            NavigateTo(result.Address, status: $"Found {result.Description} at {result.Address:X4}H. F3 finds the next match.");
            instructionList.Focus();
        }

        private void CopyListing(bool selectedOnly)
        {
            if (selectedOnly && instructionList.SelectedItems.Count == 0)
            {
                statusLabel.Text = "Select one or more instructions to copy.";
                instructionList.Focus();
                return;
            }

            IReadOnlyList<Z80Instruction> instructions = selectedOnly
                ? instructionList.SelectedItems.Cast<ListViewItem>().OrderBy(item => item.Index).Select(item => (Z80Instruction)item.Tag!).ToArray()
                : instructionList.Items.Cast<ListViewItem>().Select(item => (Z80Instruction)item.Tag!).ToArray();
            if (instructions.Count == 0)
                return;

            Clipboard.SetText(DisassemblyListingFormatter.Format(snapshot, listingAddress, instructions, includeHeader: true));
            statusLabel.Text = $"Copied {instructions.Count} instruction(s) with capture metadata.";
        }

        private void ExportListing()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Export Complete 64K Disassembly",
                Filter = "Assembly listing (*.asm)|*.asm|Text file (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"disassembly-{listingAddress:X4}.asm",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            IReadOnlyList<Z80Instruction> instructions =
                DisassemblyListingFormatter.DisassembleCompleteAddressSpace(snapshot, listingAddress);
            File.WriteAllText(dialog.FileName, DisassemblyListingFormatter.Format(snapshot, listingAddress, instructions, includeHeader: true));
            statusLabel.Text = $"Exported complete 64K listing ({instructions.Count} instructions) to {Path.GetFileName(dialog.FileName)}.";
        }

        private void UpdateCaptureState()
        {
            TimeSpan age = DateTime.UtcNow - snapshot.CapturedAtUtc;
            if (age < TimeSpan.Zero)
                age = TimeSpan.Zero;
            string ageText = age.TotalMinutes >= 1
                ? $"{(int)age.TotalMinutes}m {age.Seconds}s old"
                : $"{Math.Max(0, (int)age.TotalSeconds)}s old";
            captureStateLabel.Text = $"PAUSED | IMMUTABLE CAPTURE | {snapshot.CapturedAtUtc:HH:mm:ss} UTC | {ageText}";
        }

        private void ApplySavedColumnWidths()
        {
            if (settings.ColumnWidths.Length != instructionList.Columns.Count)
                return;
            for (int index = 0; index < instructionList.Columns.Count; index++)
                instructionList.Columns[index].Width = Math.Clamp(settings.ColumnWidths[index], 50, 1200);
        }

        private void SaveWindowSettings()
        {
            settings.Width = ClientSize.Width;
            settings.Height = ClientSize.Height;
            settings.ColumnWidths = instructionList.Columns.Cast<ColumnHeader>().Select(column => column.Width).ToArray();
            settings.LastListingAddress = listingAddress;
            try
            {
                settingsStore.Save(settings);
            }
            catch
            {
                // Settings persistence must never prevent the debugger from closing.
            }
        }

        private void AddressTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;
            NavigateFromAddressBox();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void SearchTextBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
                return;
            FindNext();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void DisassemblerForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Alt && e.KeyCode == Keys.Left)
                GoBack();
            else if (e.Alt && e.KeyCode == Keys.Right)
                GoForward();
            else if (e.Control && e.KeyCode == Keys.G)
            {
                addressTextBox.SelectAll();
                addressTextBox.Focus();
            }
            else if (e.Control && e.KeyCode == Keys.P)
                NavigateTo(snapshot.ProgramCounter);
            else if (e.Control && e.KeyCode == Keys.F)
            {
                searchTextBox.SelectAll();
                searchTextBox.Focus();
            }
            else if (e.KeyCode == Keys.F3)
                FindNext();
            else if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
                RefreshSnapshot();
            else if (e.Control && e.Shift && e.KeyCode == Keys.C)
                CopyListing(selectedOnly: false);
            else if (e.Control && e.KeyCode == Keys.C && instructionList.ContainsFocus)
                CopyListing(selectedOnly: true);
            else if (e.Control && e.KeyCode == Keys.E)
                ExportListing();
            else if (e.KeyCode == Keys.Enter && instructionList.ContainsFocus)
                NavigateToSelectedTarget();
            else if (e.KeyCode is Keys.F6 or Keys.Escape)
                Close();
            else
                return;

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                captureAgeTimer.Dispose();
                listingFont.Dispose();
                currentInstructionFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
