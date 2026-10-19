using Spectrum128kEmulator.Z80;

namespace Spectrum128kEmulator
{
    public sealed partial class DisassemblerForm
    {
        private void SetClassificationAtSelection(DisassemblyClassification classification)
        {
            Z80Instruction[] selected = instructionList.SelectedItems
                .Cast<ListViewItem>()
                .OrderBy(item => item.Index)
                .Select(item => (Z80Instruction)item.Tag!)
                .ToArray();
            if (selected.Length == 0)
                return;

            foreach (Z80Instruction instruction in selected)
            {
                int length = Math.Max(1, instruction.Length);
                if (classification == DisassemblyClassification.Code)
                {
                    Z80Instruction decoded = Z80InstructionDisassembler.Disassemble(
                        instruction.Address,
                        value => snapshot.ReadMemory(selectedAddressSpace, value));
                    length = Math.Max(1, decoded.Length);
                }
                int end = Math.Min(
                    selectedAddressSpace.Length - 1,
                    instruction.Address + length - 1);
                classifications.SetRange(
                    selectedAddressSpace,
                    instruction.Address,
                    (ushort)end,
                    classification);
            }

            ushort selection = selected[0].Address;
            PopulateListing(selection);
            string action = classification switch
            {
                DisassemblyClassification.Code => "Marked as code",
                DisassemblyClassification.Data => "Marked as data",
                _ => "Cleared classification"
            };
            statusLabel.Text = $"{action}: {selectedAddressSpace.FormatAddress(selection)} ({selected.Length} selected row(s)).";
        }

        private Z80Instruction DisassembleForListing(ushort address)
            => DisassemblyListingFormatter.DisassembleAt(
                snapshot,
                selectedAddressSpace,
                address,
                classifications);

        private string FormatClassification(ushort address) =>
            classifications.Get(selectedAddressSpace, address) switch
            {
                DisassemblyClassification.Code => "CODE",
                DisassemblyClassification.Data => "DATA",
                _ => string.Empty
            };
    }
}
