using System.Drawing;
using System.Windows.Forms;

namespace Spectrum128kEmulator
{
    public sealed class EmulatorHelpForm : Form
    {
        private const int ContentWidth = 590;

        public EmulatorHelpForm()
        {
            Text = "Spectrum 128K Emulator Help";
            ClientSize = new Size(640, 600);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            BackColor = Color.FromArgb(238, 238, 238);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 74,
                BackColor = Color.FromArgb(25, 25, 25),
                Padding = new Padding(18, 10, 18, 8)
            };
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Controls and Shortcuts",
                Font = new Font(FontFamily.GenericSansSerif, 16.0f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(18, 10)
            });
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "Press F1 or Escape to close this window.",
                Font = new Font(FontFamily.GenericSansSerif, 9.0f),
                ForeColor = Color.Gainsboro,
                Location = new Point(20, 43)
            });

            var closeButton = new Button
            {
                Dock = DockStyle.Bottom,
                Text = "Close",
                Height = 36,
                Margin = new Padding(10)
            };
            closeButton.Click += (_, _) => Close();

            var content = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(16, 14, 16, 10),
                BackColor = BackColor
            };
            content.Controls.Add(CreateShortcutSection("Display", new[]
            {
                ("F1", "Show or close this help window"),
                ("F2", "Show or hide the FPS and display-mode overlay"),
                ("F3", "Reset and toggle between 128K and 48K machine modes"),
                ("F4", "Cycle 1x Native, 2x Enhanced, and 3x Enhanced"),
                ("F5", "Stop or resume tape; its state appears on-screen and in the title"),
                ("Right-click", "Open the quick menu for display, machine, tape, loading, and diagnostics")
            }));
            content.Controls.Add(CreateShortcutSection("Loading and Diagnostics", new[]
            {
                ("F9", "Load a 48K-format .sna snapshot"),
                ("F10", "Load a .z80 snapshot or .rzx recording"),
                ("F11", "Mount a .tap or .tzx tape image"),
                ("F12", "Write a machine diagnostic dump")
            }));
            content.Controls.Add(CreateShortcutSection("Spectrum Keyboard", new[]
            {
                ("Letters / numbers", "Map to their Spectrum keys"),
                ("Enter / Space", "Map directly to the Spectrum keyboard"),
                ("Ctrl / Alt", "Map to Spectrum CAPS SHIFT"),
                ("Arrow keys", "Provide cursor-style Spectrum input chords")
            }));

            Controls.Add(content);
            Controls.Add(closeButton);
            Controls.Add(header);
            KeyDown += EmulatorHelpForm_KeyDown;
        }

        private static Control CreateShortcutSection(string title, (string Key, string Description)[] rows)
        {
            var panel = new Panel
            {
                Width = ContentWidth,
                Height = 42 + (rows.Length * 25),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Margin = new Padding(0, 0, 0, 12)
            };
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = rows.Length + 1,
                Padding = new Padding(12, 0, 12, 0),
                Margin = Padding.Empty
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            for (int index = 0; index < rows.Length; index++)
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            var titleLabel = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Text = title,
                Font = new Font(FontFamily.GenericSansSerif, 10.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 70, 125),
                Margin = Padding.Empty
            };
            table.Controls.Add(titleLabel, 0, 0);
            table.SetColumnSpan(titleLabel, 2);

            for (int index = 0; index < rows.Length; index++)
            {
                int row = index + 1;
                table.Controls.Add(new Label
                {
                    AutoSize = true,
                    Text = rows[index].Key,
                    Font = new Font(FontFamily.GenericMonospace, 9.0f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(70, 70, 70),
                    Margin = Padding.Empty,
                    Anchor = AnchorStyles.Left
                }, 0, row);
                table.Controls.Add(new Label
                {
                    AutoSize = true,
                    MaximumSize = new Size(410, 0),
                    Text = rows[index].Description,
                    Font = new Font(FontFamily.GenericSansSerif, 9.0f),
                    ForeColor = Color.FromArgb(45, 45, 45),
                    Margin = Padding.Empty,
                    Anchor = AnchorStyles.Left
                }, 1, row);
            }

            panel.Controls.Add(table);
            return panel;
        }

        private void EmulatorHelpForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is Keys.F1 or Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                Close();
            }
        }
    }
}
