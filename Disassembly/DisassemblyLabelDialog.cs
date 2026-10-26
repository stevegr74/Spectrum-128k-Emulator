namespace Spectrum128kEmulator
{
    internal sealed class DisassemblyLabelDialog : Form
    {
        private readonly TextBox nameTextBox = new();

        private DisassemblyLabelDialog(string location, string? currentName)
        {
            Text = currentName == null ? "Add Label" : "Edit Label";
            ClientSize = new Size(390, 132);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            EmulatorUiStyle.ApplyForm(this);

            Controls.Add(new Label
            {
                AutoSize = true,
                Text = $"Label for {location}",
                Location = new Point(16, 14)
            });
            nameTextBox.Location = new Point(16, 40);
            nameTextBox.Size = new Size(358, 25);
            nameTextBox.Text = currentName ?? string.Empty;
            nameTextBox.SelectAll();
            Controls.Add(nameTextBox);

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(218, 86),
                Size = new Size(74, 29)
            };
            EmulatorUiStyle.StyleButton(okButton, primary: true);
            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(300, 86),
                Size = new Size(74, 29)
            };
            EmulatorUiStyle.StyleButton(cancelButton);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
            Shown += (_, _) => nameTextBox.Focus();
        }

        public static bool TryShow(
            IWin32Window owner,
            string location,
            string? currentName,
            out string name)
        {
            using var dialog = new DisassemblyLabelDialog(location, currentName);
            if (dialog.ShowDialog(owner) != DialogResult.OK)
            {
                name = string.Empty;
                return false;
            }
            name = dialog.nameTextBox.Text.Trim();
            return true;
        }
    }
}
