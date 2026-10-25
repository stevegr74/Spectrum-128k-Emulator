namespace Spectrum128kEmulator
{
    public sealed partial class DisassemblerForm
    {
        private const int MemoryMapDiagramMinimumWidth = 400;

        private void ConfigureMappingPanel(Panel toolbar)
        {
            mappingPanel.Name = "MemoryMapPanel";
            mappingPanel.Location = new Point(642, 8);
            mappingPanel.Size = new Size(318, 56);
            mappingPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            mappingPanel.BackColor = toolbar.BackColor;
            mappingPanel.Resize += (_, _) => UpdateMappingPanelMode();

            mappingSummaryLabel.Name = "MemoryMapSummary";
            mappingSummaryLabel.Location = new Point(0, 0);
            mappingSummaryLabel.Size = new Size(mappingPanel.Width, 17);
            mappingSummaryLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            mappingSummaryLabel.Font = new Font("Consolas", 7.8f, FontStyle.Bold);
            mappingSummaryLabel.ForeColor = Color.FromArgb(50, 64, 68);
            mappingSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;

            mappingBlocksPanel.Name = "MemoryMapBlocks";
            mappingBlocksPanel.Location = new Point(0, 18);
            mappingBlocksPanel.Size = new Size(mappingPanel.Width, 23);
            mappingBlocksPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            mappingBlocksPanel.ColumnCount = mappingBlockLabels.Length;
            mappingBlocksPanel.RowCount = 1;
            mappingBlocksPanel.BackColor = toolbar.BackColor;
            mappingBlocksPanel.Margin = Padding.Empty;
            mappingBlocksPanel.Padding = Padding.Empty;
            mappingBlocksPanel.ColumnStyles.Clear();
            for (int index = 0; index < mappingBlockLabels.Length; index++)
            {
                mappingBlocksPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
                Label blockLabel = mappingBlockLabels[index];
                blockLabel.Name = $"MemoryMapBlock{index}";
                blockLabel.Dock = DockStyle.Fill;
                blockLabel.Margin = new Padding(1, 0, 1, 0);
                blockLabel.BorderStyle = BorderStyle.FixedSingle;
                blockLabel.Font = new Font("Consolas", 7.2f, FontStyle.Bold);
                blockLabel.TextAlign = ContentAlignment.MiddleCenter;
                mappingBlocksPanel.Controls.Add(blockLabel, index, 0);
            }

            screenBankLabel.Name = "MemoryMapScreenBank";
            screenBankLabel.Location = new Point(0, 42);
            screenBankLabel.Size = new Size(mappingPanel.Width, 14);
            screenBankLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            screenBankLabel.Font = new Font("Consolas", 7.2f, FontStyle.Bold);
            screenBankLabel.ForeColor = Color.FromArgb(28, 108, 94);
            screenBankLabel.TextAlign = ContentAlignment.MiddleLeft;

            compactMappingLabel.Name = "CompactMemoryMap";
            compactMappingLabel.Dock = DockStyle.Fill;
            compactMappingLabel.Font = new Font("Consolas", 7.8f, FontStyle.Regular);
            compactMappingLabel.ForeColor = Color.FromArgb(50, 64, 68);
            compactMappingLabel.TextAlign = ContentAlignment.TopLeft;

            mappingPanel.Controls.Add(mappingSummaryLabel);
            mappingPanel.Controls.Add(mappingBlocksPanel);
            mappingPanel.Controls.Add(screenBankLabel);
            mappingPanel.Controls.Add(compactMappingLabel);
            toolbar.Resize += (_, _) => LayoutMappingPanel(toolbar);
            LayoutMappingPanel(toolbar);
            UpdateMappingPanelMode();
        }

        private void LayoutMappingPanel(Panel toolbar)
        {
            const int left = 642;
            const int rightMargin = 18;
            if (toolbar.ClientSize.Width <= left + rightMargin)
                return;

            mappingPanel.SetBounds(left, 8, toolbar.ClientSize.Width - left - rightMargin, 56);
        }

        private void UpdateMappingPanel(string viewRange)
        {
            string model = snapshot.MachineModel == SpectrumMachineModel.Spectrum48K ? "48K" : "128K";
            mappingSummaryLabel.Text =
                $"VIEW {selectedAddressSpace.DisplayName} | LISTING {listingAddress:X4}H | PC {snapshot.ProgramCounter:X4}H | {model}";

            int[] mappedBanks = [-1, 5, 2, snapshot.PagedRamBank];
            string[] labels =
            [
                $"0000 ROM {snapshot.CurrentRomBank}",
                "4000 RAM 5",
                "8000 RAM 2",
                $"C000 RAM {snapshot.PagedRamBank}"
            ];
            bool[] listingBlocks = GetListingMemoryBlocks(mappedBanks);
            for (int index = 0; index < mappingBlockLabels.Length; index++)
            {
                Label block = mappingBlockLabels[index];
                bool isScreenBank = index > 0 && mappedBanks[index] == snapshot.ScreenBank;
                block.Text = labels[index];
                block.BackColor = listingBlocks[index]
                    ? isScreenBank ? Color.FromArgb(70, 205, 176) : Color.FromArgb(95, 225, 195)
                    : isScreenBank ? Color.FromArgb(214, 237, 229) : Color.FromArgb(235, 237, 232);
                block.ForeColor = Ink;
            }

            screenBankLabel.Text = $"DISPLAY RAM {snapshot.ScreenBank}";

            string[] mappingParts = snapshot.MappingText.Split(" | ");
            compactMappingLabel.Text =
                $"VIEW {selectedAddressSpace.DisplayName} | {viewRange} | LISTING {listingAddress:X4}H\r\n" +
                $"CAPTURED PC {snapshot.ProgramCounter:X4}H | {string.Join(" | ", mappingParts.Take(2))}\r\n" +
                $"    | {string.Join(" | ", mappingParts.Skip(2))}";
            UpdateMappingPanelMode();
        }

        private bool[] GetListingMemoryBlocks(int[] mappedBanks)
        {
            var result = new bool[mappingBlockLabels.Length];
            if (selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped)
            {
                result[listingAddress >> 14] = true;
                return result;
            }

            if (selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Rom)
            {
                result[0] = selectedAddressSpace.Bank == snapshot.CurrentRomBank;
                return result;
            }

            for (int index = 1; index < mappedBanks.Length; index++)
                result[index] = selectedAddressSpace.Bank == mappedBanks[index];
            return result;
        }

        private void UpdateMappingPanelMode()
        {
            bool showDiagram = mappingPanel.Width >= MemoryMapDiagramMinimumWidth;
            mappingSummaryLabel.Visible = showDiagram;
            mappingBlocksPanel.Visible = showDiagram;
            screenBankLabel.Visible = showDiagram;
            compactMappingLabel.Visible = !showDiagram;
            if (!showDiagram)
                compactMappingLabel.BringToFront();
            else
            {
                mappingSummaryLabel.BringToFront();
                mappingBlocksPanel.BringToFront();
                screenBankLabel.BringToFront();
            }
        }
    }
}
