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
                Width = 220,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Paper
            };

            Panel crossReferencePanel = CreateCrossReferencePanel();
            Panel breakpointPanel = CreateBreakpointPanel();
            breakpointPanel.Dock = DockStyle.Bottom;
            breakpointPanel.Height = 220;

            panel.Controls.Add(crossReferencePanel);
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
                Font = new Font(FontFamily.GenericSansSerif, 7.5f, FontStyle.Bold),
                ForeColor = Ink,
                TextAlign = ContentAlignment.MiddleLeft
            });

            crossReferenceList.Name = "CrossReferenceList";
            crossReferenceList.Dock = DockStyle.Fill;
            crossReferenceList.Font = new Font("Consolas", 8.5f);
            crossReferenceList.IntegralHeight = false;
            crossReferenceList.DoubleClick += (_, _) => NavigateToSelectedCrossReference();
            return panel;
        }

        private void BuildCrossReferenceIndex()
        {
            IEnumerable<ushort> roots = classifications.GetCodeEntryPoints(selectedAddressSpace).Append(listingAddress);
            if (selectedAddressSpace.Kind == DisassemblyAddressSpaceKind.Mapped)
                roots = roots.Append(snapshot.ProgramCounter);

            crossReferences = DisassemblyCrossReferenceIndex.Build(snapshot, selectedAddressSpace, roots, classifications);
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

            PreserveCurrentHistorySelection();
            SetSelectedAddressSpace(item.NavigationTarget.AddressSpace);
            NavigateTo(
                item.NavigationTarget.Address,
                status: $"Followed {item.DirectionText.ToLowerInvariant()} cross-reference to {item.NavigationTarget.AddressSpace.FormatAddress(item.NavigationTarget.Address)}.");
        }

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
                    _ => "JUMP"
                };
                return $"{DirectionText,-3} {kind,-4} {AddressSpacePrefix(location.AddressSpace)}:{location.Address:X4}";
            }
        }
    }
}
