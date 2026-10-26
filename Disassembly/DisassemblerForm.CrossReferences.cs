using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed partial class DisassemblerForm
    {
        private Panel CreateInspectorPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 340,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Paper
            };

            inspectorTabs.Dock = DockStyle.Fill;
            inspectorTabs.Font = EmulatorUiStyle.UiFont(8F);
            var referencesPage = new TabPage("References") { BackColor = Paper };
            referencesPage.Controls.Add(CreateCrossReferencePanel());
            var memoryPage = new TabPage("Memory") { BackColor = Paper };
            memoryPage.Controls.Add(CreateMemoryPanel());
            inspectorTabs.TabPages.Add(referencesPage);
            inspectorTabs.TabPages.Add(memoryPage);

            Panel breakpointPanel = CreateBreakpointPanel();
            breakpointPanel.Dock = DockStyle.Bottom;
            breakpointPanel.Height = 130;

            panel.Controls.Add(inspectorTabs);
            panel.Controls.Add(breakpointPanel);
            return panel;
        }

        private Panel CreateCrossReferencePanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 0, 10),
                BackColor = Paper
            };
            panel.Controls.Add(crossReferenceList);
            panel.Controls.Add(new Label
            {
                Text = "CROSS-REFERENCES",
                Dock = DockStyle.Top,
                Height = 24,
                Font = EmulatorUiStyle.UiFont(8F, FontStyle.Bold),
                ForeColor = EmulatorUiStyle.Muted,
                TextAlign = ContentAlignment.MiddleLeft
            });

            crossReferenceList.Name = "CrossReferenceList";
            crossReferenceList.Dock = DockStyle.Fill;
            crossReferenceList.Font = EmulatorUiStyle.MonoFont(8.5f);
            crossReferenceList.BackColor = EmulatorUiStyle.Field;
            crossReferenceList.ForeColor = Ink;
            crossReferenceList.IntegralHeight = false;
            crossReferenceList.DoubleClick += (_, _) => NavigateToSelectedCrossReference();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Follow Reference", null, (_, _) => NavigateToSelectedCrossReference());
            menu.Items.Add("Inspect Referenced Memory", null, (_, _) => InspectSelectedCrossReference());
            crossReferenceList.ContextMenuStrip = menu;
            return panel;
        }

        private void BuildCrossReferenceIndex()
        {
            var combined = new DisassemblyCrossReferenceIndex();
            foreach (DisassemblyAddressSpace addressSpace in snapshot.AvailableAddressSpaces)
            {
                IEnumerable<ushort> roots = classifications.GetCodeEntryPoints(addressSpace)
                    .Concat(analysisRoots
                        .Where(location => location.AddressSpace == addressSpace)
                        .Select(location => location.Address));
                if (addressSpace == selectedAddressSpace)
                    roots = roots.Append(listingAddress);
                if (addressSpace.Kind == DisassemblyAddressSpaceKind.Mapped)
                    roots = roots.Append(snapshot.ProgramCounter);

                ushort[] distinctRoots = roots.Distinct().ToArray();
                if (distinctRoots.Length == 0)
                    continue;
                combined.MergeFrom(DisassemblyCrossReferenceIndex.Build(
                    snapshot,
                    addressSpace,
                    distinctRoots,
                    classifications));
            }
            crossReferences = combined;
        }

        private void RefreshCrossReferenceList()
        {
            crossReferenceList.BeginUpdate();
            try
            {
                crossReferenceList.Items.Clear();
                if (SelectedInstruction() is not Z80Instruction instruction)
                    return;

                foreach (DisassemblyCrossReference reference in crossReferences.GetIncoming(selectedAddressSpace, instruction.Address))
                    crossReferenceList.Items.Add(CrossReferenceListItem.Incoming(reference));
                foreach (DisassemblyCrossReference reference in crossReferences.GetOutgoing(selectedAddressSpace, instruction.Address))
                    crossReferenceList.Items.Add(CrossReferenceListItem.Outgoing(reference));

                if (crossReferenceList.Items.Count == 0)
                    crossReferenceList.Items.Add("No direct references");
            }
            finally
            {
                crossReferenceList.EndUpdate();
            }
        }

        private void NavigateToSelectedCrossReference()
        {
            if (crossReferenceList.SelectedItem is not CrossReferenceListItem item)
                return;

            analysisRoots.Add(item.Reference.Source);
            PreserveCurrentHistorySelection();
            SetSelectedAddressSpace(item.NavigationTarget.AddressSpace);
            NavigateTo(
                item.NavigationTarget.Address,
                status: $"Followed {item.DirectionText.ToLowerInvariant()} cross-reference to {item.NavigationTarget.AddressSpace.FormatAddress(item.NavigationTarget.Address)}.");
        }

        private void InspectSelectedCrossReference()
        {
            if (crossReferenceList.SelectedItem is not CrossReferenceListItem item)
                return;

            InspectMemory(
                item.NavigationTarget,
                $"Inspecting {item.DirectionText.ToLowerInvariant()} reference at {item.NavigationTarget.AddressSpace.FormatAddress(item.NavigationTarget.Address)}.");
        }

        private static string FormatReferenceKind(DisassemblyCrossReference reference) => reference.Kind switch
        {
            DisassemblyCrossReferenceKind.Call => "call",
            DisassemblyCrossReferenceKind.RelativeJump => "relative jump",
            DisassemblyCrossReferenceKind.Jump => "jump",
            DisassemblyCrossReferenceKind.DataRead => reference.Width == 1 ? "byte read" : "word read",
            DisassemblyCrossReferenceKind.DataWrite => reference.Width == 1 ? "byte write" : "word write",
            _ => "reference"
        };

        private sealed record CrossReferenceListItem(
            string DirectionText,
            DisassemblyCrossReference Reference,
            DisassemblyLocation NavigationTarget)
        {
            public static CrossReferenceListItem Incoming(DisassemblyCrossReference reference) =>
                new("IN", reference, reference.Source);

            public static CrossReferenceListItem Outgoing(DisassemblyCrossReference reference) =>
                new("OUT", reference, reference.Target);

            public override string ToString()
            {
                DisassemblyLocation location = NavigationTarget;
                string kind = Reference.Kind switch
                {
                    DisassemblyCrossReferenceKind.Call => "CALL",
                    DisassemblyCrossReferenceKind.RelativeJump => "REL",
                    DisassemblyCrossReferenceKind.Jump => "JUMP",
                    DisassemblyCrossReferenceKind.DataRead => Reference.Width == 1 ? "READ8" : "READ16",
                    DisassemblyCrossReferenceKind.DataWrite => Reference.Width == 1 ? "WRITE8" : "WRITE16",
                    _ => "REF"
                };
                string paddedKind = Reference.Kind is DisassemblyCrossReferenceKind.DataRead or DisassemblyCrossReferenceKind.DataWrite
                    ? $"{kind,-7}"
                    : $"{kind,-4}";
                return $"{DirectionText,-3} {paddedKind} {AddressSpacePrefix(location.AddressSpace)}:{location.Address:X4}";
            }
        }
    }
}
