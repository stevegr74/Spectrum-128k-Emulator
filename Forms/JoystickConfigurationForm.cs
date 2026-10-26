using Spectrum128kEmulator.Input;

namespace Spectrum128kEmulator
{
    public sealed class JoystickConfigurationForm : Form
    {
        private readonly ComboBox emulatedInterface = CreateSelector();
        private readonly ComboBox horizontalAxis = CreateSelector();
        private readonly ComboBox verticalAxis = CreateSelector();
        private readonly ComboBox hatSwitch = CreateSelector();
        private readonly ComboBox fireButton = CreateSelector();
        private readonly TrackBar deadZone = new()
        {
            Minimum = 0,
            Maximum = 95,
            SmallChange = 5,
            LargeChange = 10,
            TickFrequency = 10,
            AutoSize = false,
            Height = 30,
            Dock = DockStyle.Fill
        };
        private readonly Label deadZoneValue = new()
        {
            AutoSize = false,
            Width = 48,
            TextAlign = ContentAlignment.MiddleRight
        };
        private readonly Label livePreview = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = EmulatorUiStyle.UiFont(9F, FontStyle.Bold),
            ForeColor = EmulatorUiStyle.Muted,
            BackColor = EmulatorUiStyle.Panel,
            Text = "Waiting for controller input..."
        };
        private readonly IHostJoystickSource? source;
        private readonly string? preferredDeviceId;
        private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 75 };

        public JoystickConfigurationForm(
            HostJoystickMapping mapping,
            SpectrumJoystickInterface joystickInterface,
            HostJoystickDevice? device,
            IHostJoystickSource? source = null,
            string? preferredDeviceId = null)
        {
            ArgumentNullException.ThrowIfNull(mapping);
            this.source = source;
            this.preferredDeviceId = preferredDeviceId;

            Text = "Joystick Configuration";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 485);
            EmulatorUiStyle.ApplyForm(this);

            PopulateInterfaceSelector(emulatedInterface, joystickInterface);
            PopulateSelector(horizontalAxis, "Axis", device?.AxisCount ?? -1, mapping.HorizontalAxis, allowUnassigned: false);
            PopulateSelector(verticalAxis, "Axis", device?.AxisCount ?? -1, mapping.VerticalAxis, allowUnassigned: false);
            PopulateSelector(hatSwitch, "Hat", device?.SwitchCount ?? -1, mapping.HatSwitch, allowUnassigned: true);
            PopulateSelector(fireButton, "Button", device?.ButtonCount ?? -1, mapping.FireButton, allowUnassigned: true);
            deadZone.Value = (int)Math.Round(Math.Clamp(mapping.DeadZone, 0.0, 0.95) * 100.0);
            UpdateDeadZoneLabel();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 16, 18, 14),
                ColumnCount = 1,
                RowCount = 6,
                BackColor = BackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 245));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(new Label
            {
                Text = "JOYSTICK CONFIGURATION",
                AutoSize = true,
                Font = EmulatorUiStyle.UiFont(13F, FontStyle.Bold),
                ForeColor = EmulatorUiStyle.Ink,
                Margin = new Padding(0, 0, 0, 3)
            }, 0, 0);
            root.Controls.Add(new Label
            {
                Text = device == null
                    ? "No controller is currently detected. These choices will be retained for the next device."
                    : $"{device.DisplayName}  |  {device.AxisCount} axes  |  {device.SwitchCount} hats  |  {device.ButtonCount} buttons",
                AutoSize = true,
                ForeColor = EmulatorUiStyle.Muted,
                MaximumSize = new Size(475, 0),
                Margin = new Padding(0, 0, 0, 12)
            }, 0, 1);

            var mappingGroup = new GroupBox
            {
                Text = "Control mapping",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 10),
                Margin = new Padding(0, 0, 0, 10)
            };
            EmulatorUiStyle.StyleGroup(mappingGroup);
            var mappingLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6
            };
            mappingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mappingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            AddRow(mappingLayout, 0, "Spectrum interface", emulatedInterface);
            AddRow(mappingLayout, 1, "Left / right", horizontalAxis);
            AddRow(mappingLayout, 2, "Up / down", verticalAxis);
            AddRow(mappingLayout, 3, "D-pad / hat", hatSwitch);
            AddRow(mappingLayout, 4, "Fire", fireButton);

            var deadZonePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Margin = Padding.Empty
            };
            deadZonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            deadZonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            deadZonePanel.Controls.Add(deadZone, 0, 0);
            deadZonePanel.Controls.Add(deadZoneValue, 1, 0);
            AddRow(mappingLayout, 5, "Axis dead zone", deadZonePanel);
            mappingGroup.Controls.Add(mappingLayout);
            root.Controls.Add(mappingGroup, 0, 2);

            var previewGroup = new GroupBox
            {
                Text = "Live input preview",
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                Margin = new Padding(0, 0, 0, 10)
            };
            EmulatorUiStyle.StyleGroup(previewGroup);
            previewGroup.Controls.Add(livePreview);
            root.Controls.Add(previewGroup, 0, 3);

            root.Controls.Add(new Label
            {
                Text = "Choose the interface expected by the game, then move the controller and press Fire to verify the mapping. Opposing directions cancel.",
                AutoSize = true,
                MaximumSize = new Size(475, 0),
                ForeColor = EmulatorUiStyle.Muted,
                Margin = new Padding(0, 0, 0, 10)
            }, 0, 4);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = Padding.Empty
            };
            var ok = new Button
            {
                Text = "Save mapping",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                Padding = new Padding(8, 2, 8, 2)
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                Padding = new Padding(8, 2, 8, 2)
            };
            EmulatorUiStyle.StyleButton(ok, primary: true);
            EmulatorUiStyle.StyleButton(cancel);
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 5);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(root);

            deadZone.ValueChanged += (_, _) =>
            {
                UpdateDeadZoneLabel();
                UpdateLivePreview();
            };
            emulatedInterface.SelectedIndexChanged += (_, _) => UpdateLivePreview();
            horizontalAxis.SelectedIndexChanged += (_, _) => UpdateLivePreview();
            verticalAxis.SelectedIndexChanged += (_, _) => UpdateLivePreview();
            hatSwitch.SelectedIndexChanged += (_, _) => UpdateLivePreview();
            fireButton.SelectedIndexChanged += (_, _) => UpdateLivePreview();
            previewTimer.Tick += (_, _) => UpdateLivePreview();
            Shown += (_, _) =>
            {
                UpdateLivePreview();
                previewTimer.Start();
            };
        }

        public HostJoystickMapping SelectedMapping => new(
            GetSelectedValue(horizontalAxis),
            GetSelectedValue(verticalAxis),
            GetSelectedValue(hatSwitch),
            GetSelectedValue(fireButton),
            deadZone.Value / 100.0);

        public SpectrumJoystickInterface SelectedInterface =>
            emulatedInterface.SelectedItem is InterfaceChoice choice
                ? choice.Value
                : SpectrumJoystickInterface.Kempston;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                previewTimer.Dispose();
            base.Dispose(disposing);
        }

        private void UpdateDeadZoneLabel() => deadZoneValue.Text = $"{deadZone.Value}%";

        private void UpdateLivePreview()
        {
            if (source == null || !source.TryRead(preferredDeviceId, out HostJoystickReading reading))
            {
                livePreview.Text = "NO CONTROLLER INPUT";
                livePreview.ForeColor = EmulatorUiStyle.Muted;
                livePreview.BackColor = EmulatorUiStyle.Panel;
                return;
            }

            SpectrumJoystickState state = HostJoystickMapper.Map(reading, SelectedMapping);
            livePreview.Text = $"{FormatInterface(SelectedInterface)}  |  {DescribeState(state)}";
            bool active = state != SpectrumJoystickState.None;
            livePreview.ForeColor = active ? EmulatorUiStyle.Accent : EmulatorUiStyle.Muted;
            livePreview.BackColor = active ? EmulatorUiStyle.AccentSoft : EmulatorUiStyle.Panel;
        }

        private static string DescribeState(SpectrumJoystickState state)
        {
            if (state == SpectrumJoystickState.None)
                return "CENTRED  |  FIRE RELEASED";

            var parts = new List<string>();
            if (state.HasFlag(SpectrumJoystickState.Left))
                parts.Add("LEFT");
            if (state.HasFlag(SpectrumJoystickState.Right))
                parts.Add("RIGHT");
            if (state.HasFlag(SpectrumJoystickState.Up))
                parts.Add("UP");
            if (state.HasFlag(SpectrumJoystickState.Down))
                parts.Add("DOWN");
            if (state.HasFlag(SpectrumJoystickState.Fire))
                parts.Add("FIRE");
            return string.Join("  +  ", parts);
        }

        private static ComboBox CreateSelector() => new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            DropDownHeight = 180
        };

        private static void PopulateInterfaceSelector(ComboBox selector, SpectrumJoystickInterface selectedValue)
        {
            InterfaceChoice[] choices =
            [
                new("Kempston", SpectrumJoystickInterface.Kempston),
                new("Cursor (keys 5, 8, 6, 7, 0)", SpectrumJoystickInterface.Cursor),
                new("Interface II - Player 1 (keys 6-0)", SpectrumJoystickInterface.Sinclair1),
                new("Interface II - Player 2 (keys 1-5)", SpectrumJoystickInterface.Sinclair2)
            ];
            selector.Items.AddRange(choices.Cast<object>().ToArray());
            int selectedIndex = Array.FindIndex(choices, choice => choice.Value == selectedValue);
            selector.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }

        private static string FormatInterface(SpectrumJoystickInterface joystickInterface) => joystickInterface switch
        {
            SpectrumJoystickInterface.Cursor => "CURSOR",
            SpectrumJoystickInterface.Sinclair1 => "INTERFACE II P1",
            SpectrumJoystickInterface.Sinclair2 => "INTERFACE II P2",
            _ => "KEMPSTON"
        };

        private static void PopulateSelector(
            ComboBox selector,
            string itemName,
            int availableCount,
            int selectedValue,
            bool allowUnassigned)
        {
            var choices = new List<MappingChoice>();
            if (allowUnassigned)
                choices.Add(new MappingChoice("Not assigned", -1));

            int count = availableCount >= 0
                ? availableCount
                : Math.Max(8, selectedValue + 1);
            for (int index = 0; index < count; index++)
                choices.Add(new MappingChoice($"{itemName} {index + 1}", index));
            if (choices.Count == 0)
                choices.Add(new MappingChoice("Not available", -1));

            selector.Items.Clear();
            selector.Items.AddRange(choices.Cast<object>().ToArray());
            int selectedIndex = choices.FindIndex(choice => choice.Value == selectedValue);
            selector.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }

        private static int GetSelectedValue(ComboBox selector) =>
            selector.SelectedItem is MappingChoice choice ? choice.Value : -1;

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.Controls.Add(new Label
            {
                Text = label,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 0, 6)
            }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            control.Margin = new Padding(8, 4, 0, 4);
            layout.Controls.Add(control, 1, row);
        }

        private sealed record MappingChoice(string Label, int Value)
        {
            public override string ToString() => Label;
        }

        private sealed record InterfaceChoice(string Label, SpectrumJoystickInterface Value)
        {
            public override string ToString() => Label;
        }
    }
}
