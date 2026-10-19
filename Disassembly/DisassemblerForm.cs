using Spectrum128kEmulator.Z80;
using Spectrum128kEmulator.Debugging;

namespace Spectrum128kEmulator
{
    public sealed partial class DisassemblerForm : Form
    {
        private const int InstructionCount = 96;
        private static readonly Color Ink = Color.FromArgb(30, 39, 46);
        private static readonly Color Paper = Color.FromArgb(242, 239, 228);

        private readonly Func<DisassemblySnapshot> captureSnapshot;
        private readonly DebuggerExecutionController executionController;
        private readonly Action<DebuggerExecutionCommand, ushort?> requestExecution;
        private readonly DisassemblyNavigationHistory navigationHistory = new();
        private readonly DisassemblySymbolTable symbols;
        private readonly DisassemblyClassificationMap classifications;
        private readonly string classificationContext;
        private readonly DisassemblerWindowSettingsStore settingsStore;
        private readonly DisassemblerWindowSettings settings;
        private readonly TextBox addressTextBox = new();
        private readonly TextBox searchTextBox = new();
        private readonly ComboBox searchModeComboBox = new();
        private readonly ComboBox addressSpaceComboBox = new();
        private readonly Label mappingLabel = new();
        private readonly Label captureStateLabel = new();
        private readonly Label statusLabel = new();
        private readonly Label cpuStateLabel = new();
        private readonly ListView instructionList = new();
        private readonly ListBox crossReferenceList = new();
        private readonly CheckedListBox breakpointList = new();
        private readonly Button backButton;
        private readonly Button forwardButton;
        private Button runButton = null!;
        private Button pauseButton = null!;
        private Button stepIntoButton = null!;
        private Button stepOverButton = null!;
        private Button stepOutButton = null!;
        private Button runToCursorButton = null!;
        private Button toggleBreakpointButton = null!;
        private Button exportButton = null!;
        private readonly Font listingFont = new("Consolas", 10.0f, FontStyle.Regular);
        private readonly Font currentInstructionFont = new("Consolas", 10.0f, FontStyle.Bold);
        private readonly System.Windows.Forms.Timer captureAgeTimer = new() { Interval = 1000 };
        private DisassemblySnapshot snapshot;
        private DisassemblyAddressSpace selectedAddressSpace;
        private ushort listingAddress;
        private string? lastSearchQuery;
        private DisassemblySearchMode? lastSearchMode;
        private DisassemblyAddressSpace? lastSearchAddressSpace;
        private ushort lastSearchAddress;
        private bool executionRunning;
        private bool refreshingBreakpointList;
        private bool changingAddressSpace;
        private DisassemblyCrossReferenceIndex crossReferences = new();

        public DisassemblerForm(Func<DisassemblySnapshot> captureSnapshot)
            : this(
                captureSnapshot,
                new DebuggerExecutionController(),
                (_, _) => { },
                new DisassemblerWindowSettingsStore())
        {
        }

        internal DisassemblerForm(
            Func<DisassemblySnapshot> captureSnapshot,
            DisassemblerWindowSettingsStore settingsStore)
            : this(
                captureSnapshot,
                new DebuggerExecutionController(),
                (_, _) => { },
                settingsStore)
        {
        }

        internal DisassemblerForm(
            Func<DisassemblySnapshot> captureSnapshot,
            DebuggerExecutionController executionController,
            Action<DebuggerExecutionCommand, ushort?> requestExecution,
            DisassemblerWindowSettingsStore settingsStore)
        {
            this.captureSnapshot = captureSnapshot ?? throw new ArgumentNullException(nameof(captureSnapshot));
            this.executionController = executionController ?? throw new ArgumentNullException(nameof(executionController));
            this.requestExecution = requestExecution ?? throw new ArgumentNullException(nameof(requestExecution));
            this.settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            settings = settingsStore.Load();
            symbols = new DisassemblySymbolTable(settings.Symbols.Select(symbol => new DisassemblySymbol(
                new DisassemblyAddressSpace(symbol.AddressSpaceKind, symbol.Bank),
                symbol.Address,
                symbol.Name,
                symbol.IsAutomatic)));
            snapshot = captureSnapshot();
            classificationContext = snapshot.AnalysisContext;
            classifications = new DisassemblyClassificationMap(settings.Classifications
                .Where(classification => string.Equals(
                    classification.Context,
                    classificationContext,
                    StringComparison.Ordinal))
                .Select(classification => new DisassemblyClassificationRange(
                    new DisassemblyAddressSpace(classification.AddressSpaceKind, classification.Bank),
                    classification.StartAddress,
                    classification.EndAddress,
                    classification.Classification)));
            executionController.ReplaceBreakpoints(settings.Breakpoints.Select(
                breakpoint => new DebuggerBreakpoint(breakpoint.Address, breakpoint.Enabled)));
            selectedAddressSpace = RestoreAddressSpace(settings, snapshot);
            backButton = CreateButton("< Back", 18, 29, 68);
            forwardButton = CreateButton("Forward >", 94, 29, 82);

            Text = "Z80 Disassembler - Paused Capture";
            MinimumSize = new Size(980, 620);
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
            RefreshBreakpointList();
            UpdateExecutionControls();

            KeyDown += DisassemblerForm_KeyDown;
            FormClosing += (_, _) => SaveWindowSettings();
            captureAgeTimer.Tick += (_, _) => UpdateCaptureState();
            Shown += (_, _) =>
            {
                ushort initialAddress = settings.LastListingAddress ??
                    (selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped ? snapshot.ProgramCounter : (ushort)0);
                NavigateTo(NormalizeAddress(initialAddress));
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

            var memoryViewPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 205,
                BackColor = Ink
            };
            var addressSpaceLabel = new Label
            {
                AutoSize = true,
                Text = "MEMORY VIEW",
                Font = new Font(FontFamily.GenericSansSerif, 7.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(190, 205, 207),
                Location = new Point(0, 0)
            };
            memoryViewPanel.Controls.Add(addressSpaceLabel);
            addressSpaceComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            addressSpaceComboBox.Font = new Font("Consolas", 9.0f);
            addressSpaceComboBox.Location = new Point(0, 22);
            addressSpaceComboBox.Size = new Size(185, 25);
            PopulateAddressSpaceChoices();
            addressSpaceComboBox.SelectedIndexChanged += (_, _) => SelectAddressSpaceFromComboBox();
            memoryViewPanel.Controls.Add(addressSpaceComboBox);
            header.Controls.Add(memoryViewPanel);
            UpdateCaptureState();
            return header;
        }

        private Control CreateToolbar()
        {
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 168,
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
            pcButton.Click += (_, _) => NavigateToMappedProgramCounter();
            toolbar.Controls.Add(pcButton);
            Button refreshButton = CreateButton("Refresh", 400, 29, 72);
            refreshButton.Click += (_, _) => RefreshSnapshot();
            toolbar.Controls.Add(refreshButton);
            Button copyButton = CreateButton("Copy", 480, 29, 62);
            copyButton.Click += (_, _) => CopyListing(selectedOnly: true);
            toolbar.Controls.Add(copyButton);
            exportButton = CreateButton("Export 64K", 550, 29, 78);
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

            toolbar.Controls.Add(CreateToolbarLabel("EXECUTION", 18, 65));
            runButton = CreateButton("Run", 18, 82, 54);
            runButton.Click += (_, _) => BeginExecution(DebuggerExecutionCommand.Run);
            toolbar.Controls.Add(runButton);
            pauseButton = CreateButton("Pause", 78, 82, 60);
            pauseButton.Click += (_, _) => RequestPause();
            toolbar.Controls.Add(pauseButton);
            stepIntoButton = CreateButton("Step Into", 144, 82, 74);
            stepIntoButton.Click += (_, _) => BeginExecution(DebuggerExecutionCommand.StepInto);
            toolbar.Controls.Add(stepIntoButton);
            stepOverButton = CreateButton("Step Over", 224, 82, 78);
            stepOverButton.Click += (_, _) => BeginExecution(DebuggerExecutionCommand.StepOver);
            toolbar.Controls.Add(stepOverButton);
            stepOutButton = CreateButton("Step Out", 308, 82, 72);
            stepOutButton.Click += (_, _) => BeginExecution(DebuggerExecutionCommand.StepOut);
            toolbar.Controls.Add(stepOutButton);
            runToCursorButton = CreateButton("Run to Cursor", 386, 82, 102);
            runToCursorButton.Click += (_, _) => RunToCursor();
            toolbar.Controls.Add(runToCursorButton);
            toggleBreakpointButton = CreateButton("Toggle Breakpoint", 494, 82, 126);
            toggleBreakpointButton.Click += (_, _) => ToggleBreakpointAtSelection();
            toolbar.Controls.Add(toggleBreakpointButton);

            toolbar.Controls.Add(CreateToolbarLabel("FIND IN CAPTURE", 18, 116));
            searchModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            searchModeComboBox.Items.AddRange(Enum.GetNames<DisassemblySearchMode>());
            searchModeComboBox.SelectedIndex = 0;
            searchModeComboBox.Location = new Point(18, 133);
            searchModeComboBox.Size = new Size(105, 24);
            toolbar.Controls.Add(searchModeComboBox);

            searchTextBox.Location = new Point(131, 133);
            searchTextBox.Size = new Size(270, 24);
            searchTextBox.Font = listingFont;
            searchTextBox.PlaceholderText = "8000, 3E 10, or CALL";
            searchTextBox.KeyDown += SearchTextBox_KeyDown;
            toolbar.Controls.Add(searchTextBox);

            Button findButton = CreateButton("Find Next", 409, 131, 86);
            findButton.Click += (_, _) => FindNext();
            toolbar.Controls.Add(findButton);
            toolbar.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "F5 run/pause | F9 breakpoint | F10 over | F11 into | Shift+F11 out",
                Font = new Font(FontFamily.GenericSansSerif, 8.0f),
                ForeColor = Color.FromArgb(65, 75, 75),
                Location = new Point(510, 137)
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
            instructionList.Columns.Add("Address", 108, HorizontalAlignment.Left);
            instructionList.Columns.Add("Type", 52, HorizontalAlignment.Left);
            instructionList.Columns.Add("Label", 125, HorizontalAlignment.Left);
            instructionList.Columns.Add("Bytes", 120, HorizontalAlignment.Left);
            instructionList.Columns.Add("Instruction", 340, HorizontalAlignment.Left);
            instructionList.Columns.Add("Target", 90, HorizontalAlignment.Left);
            instructionList.DoubleClick += (_, _) => NavigateToSelectedTarget();
            instructionList.SelectedIndexChanged += (_, _) => RefreshCrossReferenceList();
            instructionList.ContextMenuStrip = CreateListingContextMenu();

            cpuStateLabel.Dock = DockStyle.Top;
            cpuStateLabel.Height = 66;
            cpuStateLabel.Padding = new Padding(8, 5, 8, 4);
            cpuStateLabel.Font = new Font("Consolas", 9.0f, FontStyle.Regular);
            cpuStateLabel.BackColor = Color.FromArgb(232, 230, 219);
            cpuStateLabel.ForeColor = Ink;

            Panel inspectorPanel = CreateInspectorPanel();

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 29;
            statusLabel.Font = new Font(FontFamily.GenericSansSerif, 8.5f);
            statusLabel.ForeColor = Color.FromArgb(75, 75, 70);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;

            content.Controls.Add(instructionList);
            content.Controls.Add(inspectorPanel);
            content.Controls.Add(cpuStateLabel);
            content.Controls.Add(statusLabel);
            return content;
        }

        private ContextMenuStrip CreateListingContextMenu()
        {
            var menu = new ContextMenuStrip();
            ToolStripItem followTargetItem = menu.Items.Add("Follow Target", null, (_, _) => NavigateToSelectedTarget());
            ToolStripItem editLabelItem = menu.Items.Add("Add/Edit Label...", null, (_, _) => EditLabelAtSelection());
            ToolStripItem removeLabelItem = menu.Items.Add("Remove Label", null, (_, _) => RemoveLabelAtSelection());
            ToolStripMenuItem classificationItem = new("Classification");
            classificationItem.DropDownItems.Add("Mark Selected As Code", null, (_, _) => SetClassificationAtSelection(DisassemblyClassification.Code));
            classificationItem.DropDownItems.Add("Mark Selected As Data", null, (_, _) => SetClassificationAtSelection(DisassemblyClassification.Data));
            classificationItem.DropDownItems.Add("Clear Selected Marking", null, (_, _) => SetClassificationAtSelection(DisassemblyClassification.Unknown));
            menu.Items.Add(classificationItem);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripItem runToCursorItem = menu.Items.Add("Run to Cursor", null, (_, _) => RunToCursor());
            ToolStripItem breakpointItem = menu.Items.Add("Toggle Breakpoint", null, (_, _) => ToggleBreakpointAtSelection());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Copy Selected", null, (_, _) => CopyListing(selectedOnly: true));
            menu.Items.Add("Copy All Displayed", null, (_, _) => CopyListing(selectedOnly: false));
            menu.Items.Add("Export Complete View...", null, (_, _) => ExportListing());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Import Symbols...", null, (_, _) => ImportSymbols());
            menu.Items.Add("Export Symbols...", null, (_, _) => ExportSymbols());
            menu.Opening += (_, _) =>
            {
                followTargetItem.Enabled = SelectedInstruction()?.BranchTarget is ushort target &&
                    target < selectedAddressSpace.Length;
                editLabelItem.Enabled = SelectedInstruction() != null;
                removeLabelItem.Enabled = SelectedInstruction() is Z80Instruction selected &&
                    symbols.GetName(selectedAddressSpace, selected.Address) != null;
                classificationItem.Enabled = instructionList.SelectedItems.Count > 0;
                bool mappedSelection = selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped &&
                    SelectedInstruction() != null;
                runToCursorItem.Enabled = !executionRunning && mappedSelection;
                breakpointItem.Enabled = !executionRunning && mappedSelection;
            };
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
            if (executionRunning)
                return;

            ushort selectedAddress = SelectedInstruction()?.Address ?? listingAddress;
            snapshot = captureSnapshot();
            if (!snapshot.AvailableAddressSpaces.Contains(selectedAddressSpace))
                selectedAddressSpace = DisassemblyAddressSpace.Mapped;
            PopulateAddressSpaceChoices();
            selectedAddress = NormalizeAddress(selectedAddress);
            listingAddress = NormalizeAddress(listingAddress);
            PopulateListing(selectedAddress);
            UpdateCaptureState();
            UpdateCpuState();
            statusLabel.Text = $"Capture refreshed at PC={snapshot.ProgramCounter:X4}H; {selectedAddressSpace.DisplayName} remains at {listingAddress:X4}H.";
        }

        private void NavigateFromAddressBox()
        {
            if (!DisassemblyAddressParser.TryParse(addressTextBox.Text, out ushort address))
            {
                if (symbols.TryFind(addressTextBox.Text, out DisassemblySymbol? symbol) &&
                    snapshot.AvailableAddressSpaces.Contains(symbol!.AddressSpace))
                {
                    PreserveCurrentHistorySelection();
                    SetSelectedAddressSpace(symbol.AddressSpace);
                    NavigateTo(symbol.Address, status: $"Showing label {symbol.Name} at {symbol.AddressSpace.FormatAddress(symbol.Address)}.");
                    return;
                }

                statusLabel.Text = "Enter a hexadecimal address or label name.";
                addressTextBox.SelectAll();
                addressTextBox.Focus();
                return;
            }
            if (address >= selectedAddressSpace.Length)
            {
                statusLabel.Text = $"Enter a hexadecimal offset from 0000 to {selectedAddressSpace.Length - 1:X4} for {selectedAddressSpace.DisplayName}.";
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
            address = NormalizeAddress(address);
            selection = NormalizeAddress(selection);
            if (recordHistory)
            {
                PreserveCurrentHistorySelection();
                navigationHistory.NavigateTo(new DisassemblyNavigationLocation(address, selection, selectedAddressSpace));
            }

            listingAddress = address;
            addressTextBox.Text = address.ToString("X4");
            PopulateListing(selection);
            UpdateNavigationButtons();
            statusLabel.Text = status ?? $"Showing {selectedAddressSpace.DisplayName} from {address:X4}H in the frozen capture.";
        }

        private void GoBack()
        {
            PreserveCurrentHistorySelection();
            if (navigationHistory.TryGoBack(out DisassemblyNavigationLocation location))
            {
                SetSelectedAddressSpace(location.AddressSpace);
                NavigateTo(
                    location.ListingAddress,
                    recordHistory: false,
                    status: $"Back to {location.AddressSpace.FormatAddress(location.SelectedAddress)}.",
                    selectedAddress: location.SelectedAddress);
            }
        }

        private void GoForward()
        {
            PreserveCurrentHistorySelection();
            if (navigationHistory.TryGoForward(out DisassemblyNavigationLocation location))
            {
                SetSelectedAddressSpace(location.AddressSpace);
                NavigateTo(
                    location.ListingAddress,
                    recordHistory: false,
                    status: $"Forward to {location.AddressSpace.FormatAddress(location.SelectedAddress)}.",
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

        private void PopulateAddressSpaceChoices()
        {
            changingAddressSpace = true;
            try
            {
                addressSpaceComboBox.Items.Clear();
                foreach (DisassemblyAddressSpace addressSpace in snapshot.AvailableAddressSpaces)
                    addressSpaceComboBox.Items.Add(addressSpace);
                addressSpaceComboBox.SelectedItem = selectedAddressSpace;
            }
            finally
            {
                changingAddressSpace = false;
            }
        }

        private void SelectAddressSpaceFromComboBox()
        {
            if (changingAddressSpace || addressSpaceComboBox.SelectedItem is not DisassemblyAddressSpace addressSpace ||
                addressSpace == selectedAddressSpace)
            {
                return;
            }

            PreserveCurrentHistorySelection();
            selectedAddressSpace = addressSpace;
            lastSearchAddressSpace = null;
            ushort address = addressSpace.Kind == DisassemblyAddressSpaceKind.Mapped
                ? snapshot.ProgramCounter
                : (ushort)0;
            NavigateTo(address, status: $"Inspecting immutable {addressSpace.DisplayName} capture.");
        }

        private void SetSelectedAddressSpace(DisassemblyAddressSpace addressSpace)
        {
            if (!snapshot.AvailableAddressSpaces.Contains(addressSpace))
                addressSpace = DisassemblyAddressSpace.Mapped;

            selectedAddressSpace = addressSpace;
            changingAddressSpace = true;
            try
            {
                addressSpaceComboBox.SelectedItem = addressSpace;
            }
            finally
            {
                changingAddressSpace = false;
            }
        }

        private void NavigateToMappedProgramCounter()
        {
            PreserveCurrentHistorySelection();
            SetSelectedAddressSpace(DisassemblyAddressSpace.Mapped);
            NavigateTo(snapshot.ProgramCounter);
        }

        private IReadOnlyList<Z80Instruction> DisassembleVisibleInstructions()
        {
            if (selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped)
            {
                var mappedInstructions = new List<Z80Instruction>();
                ushort mappedAddress = listingAddress;
                while (mappedInstructions.Count < InstructionCount)
                {
                    Z80Instruction instruction = DisassembleForListing(mappedAddress);
                    mappedInstructions.Add(instruction);
                    mappedAddress = (ushort)(mappedAddress + Math.Max(1, instruction.Length));
                }
                return mappedInstructions;
            }

            var instructions = new List<Z80Instruction>();
            int address = listingAddress;
            while (instructions.Count < InstructionCount && address < selectedAddressSpace.Length)
            {
                Z80Instruction instruction = DisassembleForListing((ushort)address);
                instructions.Add(instruction);
                address += Math.Max(1, instruction.Length);
            }
            return instructions;
        }

        private ushort NormalizeAddress(ushort address) =>
            (ushort)(address % selectedAddressSpace.Length);

        private static string AddressSpacePrefix(DisassemblyAddressSpace addressSpace) => addressSpace.Kind switch
        {
            DisassemblyAddressSpaceKind.Rom => $"ROM{addressSpace.Bank}",
            DisassemblyAddressSpaceKind.Ram => $"RAM{addressSpace.Bank}",
            _ => "MAP"
        };

        private static string AddressSpaceFileName(DisassemblyAddressSpace addressSpace) => addressSpace.Kind switch
        {
            DisassemblyAddressSpaceKind.Rom => $"rom{addressSpace.Bank}",
            DisassemblyAddressSpaceKind.Ram => $"ram{addressSpace.Bank}",
            _ => "mapped64k"
        };

        private static DisassemblyAddressSpace RestoreAddressSpace(
            DisassemblerWindowSettings settings,
            DisassemblySnapshot snapshot)
        {
            var requested = new DisassemblyAddressSpace(
                settings.LastAddressSpaceKind,
                settings.LastAddressSpaceBank);
            return snapshot.AvailableAddressSpaces.Contains(requested)
                ? requested
                : DisassemblyAddressSpace.Mapped;
        }

        private void PopulateListing(ushort selectedAddress)
        {
            BuildCrossReferenceIndex();
            IReadOnlyList<Z80Instruction> instructions = DisassembleVisibleInstructions();
            Dictionary<ushort, bool> breakpoints = executionController.GetBreakpoints()
                .ToDictionary(breakpoint => breakpoint.Address, breakpoint => breakpoint.Enabled);

            instructionList.BeginUpdate();
            try
            {
                instructionList.Items.Clear();
                foreach (Z80Instruction instruction in instructions)
                {
                    string addressText = selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped
                        ? instruction.Address.ToString("X4")
                        : $"{AddressSpacePrefix(selectedAddressSpace)}:{instruction.Address:X4}";
                    var item = new ListViewItem(addressText) { Tag = instruction };
                    item.SubItems.Add(FormatClassification(instruction.Address));
                    item.SubItems.Add(symbols.GetName(selectedAddressSpace, instruction.Address) ?? string.Empty);
                    item.SubItems.Add(instruction.BytesText);
                    item.SubItems.Add(instruction.Mnemonic);
                    item.SubItems.Add(FormatTarget(instruction));
                    if (selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped &&
                        instruction.Address == snapshot.ProgramCounter)
                    {
                        item.BackColor = Color.FromArgb(255, 230, 145);
                        item.ForeColor = Color.FromArgb(65, 45, 0);
                        item.Font = currentInstructionFont;
                    }
                    else if (selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped &&
                        breakpoints.TryGetValue(instruction.Address, out bool enabled))
                    {
                        item.BackColor = enabled
                            ? Color.FromArgb(255, 220, 215)
                            : Color.FromArgb(230, 230, 226);
                        item.ForeColor = enabled
                            ? Color.FromArgb(120, 30, 25)
                            : Color.FromArgb(105, 105, 100);
                    }
                    else if (classifications.Get(selectedAddressSpace, instruction.Address) == DisassemblyClassification.Data)
                    {
                        item.BackColor = Color.FromArgb(255, 244, 204);
                    }
                    else if (classifications.Get(selectedAddressSpace, instruction.Address) == DisassemblyClassification.Code)
                    {
                        item.BackColor = Color.FromArgb(225, 239, 247);
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

            string viewRange = selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped
                ? "0000-FFFF"
                : "OFFSET 0000-3FFF";
            mappingLabel.Text = $"VIEW {selectedAddressSpace.DisplayName} | {viewRange} | LISTING {listingAddress:X4}H\r\nCAPTURED PC {snapshot.ProgramCounter:X4}H | {snapshot.MappingText}";
            exportButton.Text = selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped
                ? "Export 64K"
                : "Export 16K";
            UpdateCpuState();
            UpdateExecutionControls();
            RefreshCrossReferenceList();
        }

        private void NavigateToSelectedTarget()
        {
            if (SelectedInstruction()?.BranchTarget is ushort target)
            {
                if (target >= selectedAddressSpace.Length)
                {
                    statusLabel.Text = $"Target {target:X4}H is outside {selectedAddressSpace.DisplayName}; switch to Mapped 64K to follow its logical address.";
                    return;
                }
                bool isCall = SelectedInstruction()!.Mnemonic.StartsWith("CALL ", StringComparison.OrdinalIgnoreCase) ||
                    SelectedInstruction()!.Mnemonic.StartsWith("RST ", StringComparison.OrdinalIgnoreCase);
                DisassemblySymbol symbol = symbols.EnsureAutomatic(selectedAddressSpace, target, isCall);
                NavigateTo(target);
                statusLabel.Text = $"Followed target {symbol.Name} at {selectedAddressSpace.FormatAddress(target)}.";
            }
            else
                statusLabel.Text = "The selected instruction has no direct branch target.";
        }

        private Z80Instruction? SelectedInstruction()
        {
            return instructionList.SelectedItems.Count == 1
                ? instructionList.SelectedItems[0].Tag as Z80Instruction
                : null;
        }

        private string FormatTarget(Z80Instruction instruction)
        {
            if (instruction.BranchTarget is not ushort target)
                return string.Empty;

            string? label = target < selectedAddressSpace.Length
                ? symbols.GetName(selectedAddressSpace, target)
                : null;
            return label == null ? target.ToString("X4") : $"{label} ({target:X4})";
        }

        private void EditLabelAtSelection()
        {
            if (SelectedInstruction() is not Z80Instruction instruction)
                return;

            string location = selectedAddressSpace.FormatAddress(instruction.Address);
            string? currentName = symbols.GetName(selectedAddressSpace, instruction.Address);
            while (DisassemblyLabelDialog.TryShow(this, location, currentName, out string name))
            {
                if (symbols.TrySet(selectedAddressSpace, instruction.Address, name, isAutomatic: false, out string? error))
                {
                    PopulateListing(instruction.Address);
                    statusLabel.Text = $"Label {name} assigned to {location}.";
                    return;
                }

                MessageBox.Show(this, error, "Invalid Label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                currentName = name;
            }
        }

        private void RemoveLabelAtSelection()
        {
            if (SelectedInstruction() is not Z80Instruction instruction ||
                !symbols.Remove(selectedAddressSpace, instruction.Address))
            {
                return;
            }

            PopulateListing(instruction.Address);
            statusLabel.Text = $"Removed label from {selectedAddressSpace.FormatAddress(instruction.Address)}.";
        }

        private void ImportSymbols()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import Debugger Symbols",
                Filter = "Symbol files (*.sym;*.txt)|*.sym;*.txt|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                string text = File.ReadAllText(dialog.FileName);
                if (!DisassemblySymbolFile.TryParse(text, out IReadOnlyList<DisassemblySymbol> imported, out string? parseError))
                {
                    MessageBox.Show(this, parseError, "Symbol Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!symbols.TryMerge(imported, out string? mergeError))
                {
                    MessageBox.Show(this, mergeError, "Symbol Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                PopulateListing(SelectedInstruction()?.Address ?? listingAddress);
                statusLabel.Text = $"Imported {imported.Count} symbol(s) from {Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Symbol Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportSymbols()
        {
            if (symbols.Symbols.Count == 0)
            {
                statusLabel.Text = "There are no symbols to export.";
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "Export Debugger Symbols",
                Filter = "Symbol file (*.sym)|*.sym|Text file (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "debugger-symbols.sym",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, DisassemblySymbolFile.Format(symbols.Symbols));
                statusLabel.Text = $"Exported {symbols.Symbols.Count} symbol(s) to {Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Symbol Export Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void FindNext()
        {
            var mode = (DisassemblySearchMode)searchModeComboBox.SelectedIndex;
            string query = searchTextBox.Text.Trim();
            if (mode == DisassemblySearchMode.Address &&
                symbols.TryFind(query, out DisassemblySymbol? symbol) &&
                snapshot.AvailableAddressSpaces.Contains(symbol!.AddressSpace))
            {
                PreserveCurrentHistorySelection();
                SetSelectedAddressSpace(symbol.AddressSpace);
                NavigateTo(symbol.Address, status: $"Found label {symbol.Name} at {symbol.AddressSpace.FormatAddress(symbol.Address)}.");
                instructionList.Focus();
                return;
            }
            bool sameSearch = lastSearchMode == mode &&
                lastSearchAddressSpace == selectedAddressSpace &&
                string.Equals(lastSearchQuery, query, StringComparison.OrdinalIgnoreCase);
            ushort startAddress = sameSearch ? (ushort)(lastSearchAddress + 1) : listingAddress;

            if (!DisassemblySearch.TryFindNext(snapshot, selectedAddressSpace, mode, query, startAddress, classifications, out DisassemblySearchResult? result, out string? error))
            {
                statusLabel.Text = error ?? "No match found.";
                searchTextBox.SelectAll();
                searchTextBox.Focus();
                return;
            }

            lastSearchMode = mode;
            lastSearchAddressSpace = selectedAddressSpace;
            lastSearchQuery = query;
            lastSearchAddress = result!.Address;
            NavigateTo(result.Address, status: $"Found {result.Description}. F3 finds the next match.");
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

            Clipboard.SetText(DisassemblyListingFormatter.Format(
                snapshot,
                selectedAddressSpace,
                listingAddress,
                instructions,
                includeHeader: true,
                symbols,
                classifications));
            statusLabel.Text = $"Copied {instructions.Count} instruction(s) with capture metadata.";
        }

        private void ExportListing()
        {
            using var dialog = new SaveFileDialog
            {
                Title = $"Export Complete {selectedAddressSpace.DisplayName} Disassembly",
                Filter = "Assembly listing (*.asm)|*.asm|Text file (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = $"disassembly-{AddressSpaceFileName(selectedAddressSpace)}-{listingAddress:X4}.asm",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            IReadOnlyList<Z80Instruction> instructions =
                DisassemblyListingFormatter.DisassembleCompleteAddressSpace(snapshot, selectedAddressSpace, listingAddress, classifications);
            File.WriteAllText(dialog.FileName, DisassemblyListingFormatter.Format(
                snapshot,
                selectedAddressSpace,
                listingAddress,
                instructions,
                includeHeader: true,
                symbols,
                classifications));
            statusLabel.Text = $"Exported complete {selectedAddressSpace.DisplayName} listing ({instructions.Count} instructions) to {Path.GetFileName(dialog.FileName)}.";
        }

        private void UpdateCaptureState()
        {
            if (executionRunning)
            {
                captureStateLabel.Text = "RUNNING | CAPTURE FROZEN UNTIL NEXT PAUSE";
                return;
            }

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
            settings.LastAddressSpaceKind = selectedAddressSpace.Kind;
            settings.LastAddressSpaceBank = selectedAddressSpace.Bank;
            settings.Breakpoints = executionController.GetBreakpoints()
                .Select(breakpoint => new DisassemblerBreakpointSetting
                {
                    Address = breakpoint.Address,
                    Enabled = breakpoint.Enabled
                })
                .ToList();
            settings.Symbols = symbols.Symbols.Select(symbol => new DisassemblerSymbolSetting
            {
                AddressSpaceKind = symbol.AddressSpace.Kind,
                Bank = symbol.AddressSpace.Bank,
                Address = symbol.Address,
                Name = symbol.Name,
                IsAutomatic = symbol.IsAutomatic
            }).ToList();
            settings.Classifications = settings.Classifications
                .Where(classification => !string.Equals(
                    classification.Context,
                    classificationContext,
                    StringComparison.Ordinal))
                .Concat(classifications.Ranges.Select(classification => new DisassemblerClassificationSetting
                {
                    Context = classificationContext,
                    AddressSpaceKind = classification.AddressSpace.Kind,
                    Bank = classification.AddressSpace.Bank,
                    StartAddress = classification.StartAddress,
                    EndAddress = classification.EndAddress,
                    Classification = classification.Classification
                }))
                .ToList();
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
            else if (e.Control && e.KeyCode == Keys.L)
                EditLabelAtSelection();
            else if (e.KeyCode == Keys.F5)
            {
                if (executionRunning)
                    RequestPause();
                else
                    BeginExecution(DebuggerExecutionCommand.Run);
            }
            else if (e.KeyCode == Keys.F9)
                ToggleBreakpointAtSelection();
            else if (e.KeyCode == Keys.F10 && !e.Control)
                BeginExecution(DebuggerExecutionCommand.StepOver);
            else if (e.Shift && e.KeyCode == Keys.F11)
                BeginExecution(DebuggerExecutionCommand.StepOut);
            else if (e.KeyCode == Keys.F11)
                BeginExecution(DebuggerExecutionCommand.StepInto);
            else if (e.Control && e.KeyCode == Keys.F10)
                RunToCursor();
            else if (e.Control && e.KeyCode == Keys.R)
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
