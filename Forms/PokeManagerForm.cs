using Spectrum128kEmulator.Pokes;

namespace Spectrum128kEmulator
{
    public sealed class PokeManagerForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(29, 41, 46);
        private static readonly Color Paper = Color.FromArgb(239, 238, 228);
        private static readonly Color Accent = Color.FromArgb(23, 151, 126);
        private static readonly Color Warning = Color.FromArgb(190, 112, 26);
        private static readonly Color MatchPaper = Color.FromArgb(222, 240, 232);
        private static readonly Color SuggestionPaper = Color.FromArgb(248, 235, 215);
        private static readonly Color BrowsePaper = Color.FromArgb(224, 224, 214);

        private readonly Func<IReadOnlyList<PokeWrite>, IReadOnlyList<PokeWritePreview>> previewWrites;
        private readonly Func<IReadOnlyList<PokeWrite>, PokeApplication> applyWrites;
        private readonly Func<bool> canRestore;
        private readonly Func<(bool Success, string Message)> restoreLast;
        private readonly Func<int, int, bool> confirmApply;
        private readonly IReadOnlyList<PokeGameMatch> matches;
        private readonly List<PokeWrite> pendingWrites = [];

        private readonly ComboBox gameSelector = new() { Name = "PokeGameSelector", DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckedListBox pokeList = new()
        {
            Name = "PokeList",
            CheckOnClick = true,
            IntegralHeight = false,
            DisplayMember = nameof(PokeDefinition.Name)
        };
        private readonly Label matchLabel = new() { Name = "PokeMatchLabel", AutoSize = false };
        private readonly Label descriptionLabel = new() { Name = "PokeDescription", AutoSize = false };
        private readonly ListView libraryPreview = CreatePreviewList("PokeLibraryPreview");
        private readonly Button applyLibraryButton = new() { Name = "ApplyLibraryPokes", Text = "Apply Checked", Height = 34 };
        private readonly Button restoreLibraryButton = new() { Name = "RestoreLastPoke", Text = "Restore Last", Height = 34 };

        private readonly ComboBox spaceSelector = new() { Name = "PokeSpaceSelector", DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown bankSelector = new() { Name = "PokeBankSelector", Minimum = 0, Maximum = 7, Width = 55 };
        private readonly Label addressLabel = new() { Text = "Address", AutoSize = true };
        private readonly TextBox addressText = new() { Name = "PokeAddressText", Text = "8000H", Width = 100 };
        private readonly TextBox valueText = new() { Name = "PokeValueText", Text = "00H", Width = 80 };
        private readonly ListView pendingList = CreatePreviewList("PokePendingList");
        private readonly Button applyPendingButton = new() { Name = "ApplyPendingPokes", Text = "Apply Pending", Height = 34 };
        private readonly Label statusLabel = new() { Name = "PokeStatus", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };

        public PokeManagerForm(
            PokeLibrary library,
            PokeMediaIdentity? media,
            SpectrumMachineModel model,
            Func<IReadOnlyList<PokeWrite>, IReadOnlyList<PokeWritePreview>> previewWrites,
            Func<IReadOnlyList<PokeWrite>, PokeApplication> applyWrites,
            Func<bool> canRestore,
            Func<(bool Success, string Message)> restoreLast,
            Func<int, int, bool>? confirmApply = null)
        {
            ArgumentNullException.ThrowIfNull(library);
            this.previewWrites = previewWrites ?? throw new ArgumentNullException(nameof(previewWrites));
            this.applyWrites = applyWrites ?? throw new ArgumentNullException(nameof(applyWrites));
            this.canRestore = canRestore ?? throw new ArgumentNullException(nameof(canRestore));
            this.restoreLast = restoreLast ?? throw new ArgumentNullException(nameof(restoreLast));
            this.confirmApply = confirmApply ?? ConfirmApply;
            matches = library.FindMatches(media, model);

            Text = "Poke Manager - Paused";
            ClientSize = new Size(940, 650);
            MinimumSize = new Size(820, 580);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Paper;
            ForeColor = Ink;
            Font = new Font(FontFamily.GenericSansSerif, 9.0f);
            KeyPreview = true;

            Controls.Add(CreateTabs());
            Controls.Add(CreateStatusBar());
            Controls.Add(CreateHeader(media, model));

            gameSelector.SelectedIndexChanged += (_, _) => PopulatePokes();
            pokeList.ItemCheck += (_, _) => BeginInvoke(RefreshLibraryPreview);
            pokeList.SelectedIndexChanged += (_, _) => RefreshDescription();
            applyLibraryButton.Click += (_, _) => ApplyLibrarySelection();
            restoreLibraryButton.Click += (_, _) => RestoreLastApplication();
            spaceSelector.SelectedIndexChanged += (_, _) => UpdateOneOffAddressMode();
            applyPendingButton.Click += (_, _) => ApplyPendingWrites();
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            PopulateGames();
            spaceSelector.Items.AddRange(["Mapped 64K", "Physical RAM bank"]);
            spaceSelector.SelectedIndex = 0;
            applyPendingButton.Enabled = false;
            UpdateRestoreButtons();
        }

        private Control CreateHeader(PokeMediaIdentity? media, SpectrumMachineModel model)
        {
            var panel = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Ink };
            panel.Controls.Add(new Label
            {
                Text = "POKE MANAGER",
                AutoSize = true,
                Location = new Point(22, 15),
                ForeColor = Color.White,
                Font = new Font(FontFamily.GenericMonospace, 19.0f, FontStyle.Bold)
            });
            string mediaText = media == null
                ? "No media loaded"
                : $"{media.FileName}  |  SHA-256 {media.Sha256[..12]}...";
            panel.Controls.Add(new Label
            {
                Text = $"PAUSED  |  {(model == SpectrumMachineModel.Spectrum48K ? "48K" : "128K")}  |  {mediaText}",
                AutoSize = true,
                Location = new Point(24, 56),
                ForeColor = Color.FromArgb(92, 220, 190),
                Font = new Font(FontFamily.GenericMonospace, 9.5f, FontStyle.Bold)
            });
            return panel;
        }

        private Control CreateTabs()
        {
            var tabs = new TabControl { Name = "PokeTabs", Dock = DockStyle.Fill, Padding = new Point(18, 5) };
            tabs.TabPages.Add(CreateLibraryTab());
            tabs.TabPages.Add(CreateOneOffTab());
            return tabs;
        }

        private TabPage CreateLibraryTab()
        {
            var page = new TabPage("Library") { BackColor = Paper, Padding = new Padding(16) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

            var gameLabel = new Label { Text = "GAME", Dock = DockStyle.Top, Height = 18, Font = BoldFont() };
            var gamePanel = new Panel { Dock = DockStyle.Fill };
            gameSelector.Dock = DockStyle.Bottom;
            gamePanel.Controls.Add(gameSelector);
            gamePanel.Controls.Add(gameLabel);
            layout.Controls.Add(gamePanel, 0, 0);
            layout.SetColumnSpan(gamePanel, 2);

            matchLabel.Dock = DockStyle.Fill;
            matchLabel.Padding = new Padding(8, 7, 8, 4);
            matchLabel.BackColor = BrowsePaper;
            matchLabel.BorderStyle = BorderStyle.FixedSingle;
            layout.Controls.Add(matchLabel, 0, 1);
            layout.SetColumnSpan(matchLabel, 2);

            pokeList.Dock = DockStyle.Fill;
            layout.Controls.Add(pokeList, 0, 2);
            var previewPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
            descriptionLabel.Dock = DockStyle.Top;
            descriptionLabel.Height = 54;
            descriptionLabel.Padding = new Padding(0, 0, 0, 6);
            libraryPreview.Dock = DockStyle.Fill;
            previewPanel.Controls.Add(libraryPreview);
            previewPanel.Controls.Add(descriptionLabel);
            layout.Controls.Add(previewPanel, 1, 2);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            applyLibraryButton.Width = 145;
            applyLibraryButton.BackColor = Accent;
            applyLibraryButton.ForeColor = Color.White;
            restoreLibraryButton.Width = 125;
            buttons.Controls.Add(applyLibraryButton);
            buttons.Controls.Add(restoreLibraryButton);
            layout.Controls.Add(buttons, 0, 3);
            layout.SetColumnSpan(buttons, 2);

            page.Controls.Add(layout);
            return page;
        }

        private TabPage CreateOneOffTab()
        {
            var page = new TabPage("One-off") { BackColor = Paper, Padding = new Padding(16) };
            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 74,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(0, 7, 0, 0)
            };
            top.Controls.Add(Field("SPACE", spaceSelector, 165));
            top.Controls.Add(Field("BANK", bankSelector, 70));
            top.Controls.Add(Field(addressLabel, addressText, 115));
            top.Controls.Add(Field("VALUE", valueText, 95));
            var add = new Button { Name = "AddPendingPoke", Text = "Add Write", Width = 105, Height = 30, Margin = new Padding(12, 20, 0, 0) };
            add.Click += (_, _) => AddPendingWrite();
            top.Controls.Add(add);

            pendingList.Dock = DockStyle.Fill;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            applyPendingButton.Width = 145;
            applyPendingButton.BackColor = Accent;
            applyPendingButton.ForeColor = Color.White;
            var clear = new Button { Name = "ClearPendingPokes", Text = "Clear", Width = 90, Height = 34 };
            clear.Click += (_, _) =>
            {
                pendingWrites.Clear();
                RefreshPendingList();
            };
            var remove = new Button { Name = "RemovePendingPoke", Text = "Remove", Width = 90, Height = 34 };
            remove.Click += (_, _) => RemovePendingWrite();
            buttons.Controls.Add(applyPendingButton);
            buttons.Controls.Add(clear);
            buttons.Controls.Add(remove);

            page.Controls.Add(pendingList);
            page.Controls.Add(buttons);
            page.Controls.Add(top);
            return page;
        }

        private Control CreateStatusBar()
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(16, 4, 12, 4) };
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.Text = "Choose library pokes or queue a one-off write. All changes are previewed before application.";
            var close = new Button { Text = "Close", Dock = DockStyle.Right, Width = 100 };
            close.Click += (_, _) => Close();
            panel.Controls.Add(statusLabel);
            panel.Controls.Add(close);
            return panel;
        }

        private void PopulateGames()
        {
            gameSelector.Items.Clear();
            gameSelector.Items.Add("Choose a game...");
            foreach (PokeGameMatch match in matches)
                gameSelector.Items.Add(new GameChoice(match));

            if (matches.Count == 0)
            {
                gameSelector.Items.Clear();
                gameSelector.Enabled = false;
                matchLabel.Text = "The library contains no entries compatible with this machine model. One-off writes remain available.";
                pokeList.Enabled = false;
                applyLibraryButton.Enabled = false;
                return;
            }

            int recommendedIndex = matches
                .Select((match, index) => (match, index))
                .Where(item => item.match.Kind != PokeMatchKind.None)
                .Select(item => item.index + 1)
                .DefaultIfEmpty(0)
                .First();
            gameSelector.SelectedIndex = recommendedIndex;
        }

        private void PopulatePokes()
        {
            pokeList.Items.Clear();
            if (gameSelector.SelectedItem is not GameChoice choice)
            {
                descriptionLabel.Text = "Choose a library game to inspect its available pokes.";
                libraryPreview.Items.Clear();
                applyLibraryButton.Enabled = false;
                matchLabel.Text = "No matching game found  |  Choose a library entry to browse its pokes.";
                matchLabel.ForeColor = Color.DimGray;
                matchLabel.BackColor = BrowsePaper;
                return;
            }

            foreach (PokeDefinition poke in choice.Match.Game.Pokes)
                pokeList.Items.Add(poke);
            if (pokeList.Items.Count > 0)
                pokeList.SelectedIndex = 0;

            matchLabel.Text = choice.Match.Kind switch
            {
                PokeMatchKind.ExactChecksum => "Exact game match  |  Verified by media checksum and machine model.",
                PokeMatchKind.Alias => "Filename suggestion  |  Confirm the game version before applying a poke.",
                _ => "Library entry  |  Not matched to the currently loaded media."
            };
            matchLabel.ForeColor = choice.Match.Kind switch
            {
                PokeMatchKind.ExactChecksum => Accent,
                PokeMatchKind.Alias => Warning,
                _ => Color.DimGray
            };
            matchLabel.BackColor = choice.Match.Kind switch
            {
                PokeMatchKind.ExactChecksum => MatchPaper,
                PokeMatchKind.Alias => SuggestionPaper,
                _ => BrowsePaper
            };
            RefreshDescription();
            RefreshLibraryPreview();
        }

        private void RefreshDescription()
        {
            descriptionLabel.Text = pokeList.SelectedItem is PokeDefinition poke
                ? string.IsNullOrWhiteSpace(poke.Description) ? poke.Name : $"{poke.Name}\r\n{poke.Description}"
                : "Select a poke to inspect its writes.";
        }

        private void RefreshLibraryPreview()
        {
            PokeWrite[] writes = CheckedPokes().SelectMany(poke => poke.Writes).ToArray();
            if (writes.Length == 0 && pokeList.SelectedItem is PokeDefinition selected)
                writes = selected.Writes.ToArray();
            PopulatePreview(libraryPreview, writes);
            applyLibraryButton.Enabled = CheckedPokes().Any();
        }

        private IEnumerable<PokeDefinition> CheckedPokes() => pokeList.CheckedItems.Cast<PokeDefinition>();

        private void ApplyLibrarySelection()
        {
            PokeWrite[] writes = CheckedPokes().SelectMany(poke => poke.Writes).ToArray();
            Apply(writes, "library poke");
        }

        private void AddPendingWrite()
        {
            try
            {
                byte value = checked((byte)PokeLibrary.ParseNumber(valueText.Text, 0xFF, "value"));
                PokeWrite write;
                if (spaceSelector.SelectedIndex == 0)
                {
                    ushort address = checked((ushort)PokeLibrary.ParseNumber(addressText.Text, 0xFFFF, "mapped address"));
                    write = PokeWrite.Mapped(address, value);
                }
                else
                {
                    ushort offset = checked((ushort)PokeLibrary.ParseNumber(addressText.Text, 0x3FFF, "bank offset"));
                    write = PokeWrite.RamBank((int)bankSelector.Value, offset, value);
                }

                if (pendingWrites.Any(existing => existing.Space == write.Space && existing.Bank == write.Bank && existing.Address == write.Address))
                    throw new InvalidOperationException($"{write.Target} is already queued.");
                previewWrites([write]);
                pendingWrites.Add(write);
                RefreshPendingList();
                SetStatus($"Queued {write.Target} = {write.Value:X2}H.", false);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
            {
                SetStatus(exception.Message, true);
            }
        }

        private void RemovePendingWrite()
        {
            if (pendingList.SelectedIndices.Count == 0)
                return;
            pendingWrites.RemoveAt(pendingList.SelectedIndices[0]);
            RefreshPendingList();
        }

        private void RefreshPendingList()
        {
            PopulatePreview(pendingList, pendingWrites);
            applyPendingButton.Enabled = pendingWrites.Count > 0;
        }

        private void ApplyPendingWrites()
        {
            PokeWrite[] writes = pendingWrites.ToArray();
            if (Apply(writes, "one-off poke"))
            {
                pendingWrites.Clear();
                RefreshPendingList();
            }
        }

        private bool Apply(IReadOnlyList<PokeWrite> writes, string description)
        {
            if (writes.Count == 0)
            {
                SetStatus("Select or queue at least one write.", true);
                return false;
            }

            try
            {
                IReadOnlyList<PokeWritePreview> preview = previewWrites(writes);
                int changed = preview.Count(item => item.ChangesValue);
                if (!confirmApply(writes.Count, changed))
                    return false;

                applyWrites(writes);
                SetStatus($"Applied {description}: {writes.Count} write(s), {changed} byte(s) changed.", false);
                UpdateRestoreButtons();
                RefreshLibraryPreview();
                return true;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                SetStatus(exception.Message, true);
                return false;
            }
        }

        private void RestoreLastApplication()
        {
            (bool success, string message) = restoreLast();
            SetStatus(message, !success);
            UpdateRestoreButtons();
            RefreshLibraryPreview();
            RefreshPendingList();
        }

        private void UpdateRestoreButtons()
        {
            restoreLibraryButton.Enabled = canRestore();
        }

        private void UpdateOneOffAddressMode()
        {
            bool physical = spaceSelector.SelectedIndex == 1;
            bankSelector.Enabled = physical;
            addressLabel.Text = physical ? "OFFSET" : "ADDRESS";
            addressText.Text = physical ? "0000H" : "8000H";
        }

        private void PopulatePreview(ListView list, IReadOnlyList<PokeWrite> writes)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                if (writes.Count == 0)
                    return;

                IReadOnlyList<PokeWritePreview> preview = previewWrites(writes);
                foreach (PokeWritePreview item in preview)
                {
                    var row = new ListViewItem(item.Write.Target);
                    row.SubItems.Add($"{item.CurrentValue:X2}H");
                    row.SubItems.Add($"{item.Write.Value:X2}H");
                    if (item.ChangesValue)
                        row.BackColor = Color.FromArgb(224, 243, 236);
                    list.Items.Add(row);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                SetStatus(exception.Message, true);
            }
            finally
            {
                list.EndUpdate();
            }
        }

        private void SetStatus(string text, bool isError)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = isError ? Color.Firebrick : Ink;
        }

        private bool ConfirmApply(int writeCount, int changedCount) =>
            MessageBox.Show(
                this,
                $"Apply {writeCount} write(s), changing {changedCount} byte(s)?",
                "Confirm Poke",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) == DialogResult.OK;

        private static ListView CreatePreviewList(string name)
        {
            var list = new ListView
            {
                Name = name,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false
            };
            list.Columns.Add("Target", 210);
            list.Columns.Add("Current", 85);
            list.Columns.Add("New", 85);
            return list;
        }

        private static Control Field(string label, Control control, int width) =>
            Field(new Label { Text = label, AutoSize = true }, control, width);

        private static Control Field(Label label, Control control, int width)
        {
            var panel = new Panel { Width = width, Height = 58, Margin = new Padding(0, 0, 10, 0) };
            label.Location = new Point(0, 0);
            label.Font = BoldFont();
            control.Location = new Point(0, 21);
            control.Width = width - 8;
            panel.Controls.Add(control);
            panel.Controls.Add(label);
            return panel;
        }

        private static Font BoldFont() => new(FontFamily.GenericSansSerif, 8.5f, FontStyle.Bold);

        private sealed record GameChoice(PokeGameMatch Match)
        {
            public override string ToString() => Match.Game.Title;
        }
    }
}
