namespace Spectrum128kEmulator
{
    internal static class EmulatorUiStyle
    {
        public static readonly Color Paper = Color.FromArgb(246, 246, 242);
        public static readonly Color Panel = Color.FromArgb(239, 239, 233);
        public static readonly Color Field = Color.White;
        public static readonly Color Ink = Color.FromArgb(30, 45, 49);
        public static readonly Color Muted = Color.FromArgb(75, 85, 85);
        public static readonly Color Border = Color.FromArgb(201, 207, 202);
        public static readonly Color Accent = Color.FromArgb(8, 121, 101);
        public static readonly Color AccentSoft = Color.FromArgb(213, 238, 231);
        public static readonly Color Selection = Color.FromArgb(223, 241, 235);
        public static readonly Color Changed = Color.FromArgb(255, 230, 145);
        public static readonly Color Warning = Color.FromArgb(164, 93, 16);

        public static Font UiFont(float size = 9F, FontStyle style = FontStyle.Regular) =>
            new("Segoe UI", size, style);

        public static Font MonoFont(float size = 9F, FontStyle style = FontStyle.Regular) =>
            new("Consolas", size, style);

        public static void ApplyForm(Form form)
        {
            form.BackColor = Paper;
            form.ForeColor = Ink;
            form.Font = UiFont();
        }

        public static void StyleButton(Button button, bool primary = false)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.BackColor = primary ? Accent : Field;
            button.ForeColor = primary ? Color.White : Ink;
            button.UseVisualStyleBackColor = false;
            button.Font = UiFont(9F, FontStyle.Regular);
        }

        public static void StyleGroup(GroupBox group)
        {
            group.BackColor = Paper;
            group.ForeColor = Ink;
            group.Font = UiFont(9F, FontStyle.Regular);
        }

        public static Label CreateTitle(string text) => new()
        {
            AutoSize = true,
            Text = text,
            Font = UiFont(13F, FontStyle.Bold),
            ForeColor = Ink
        };

        public static Label CreateSubtitle(string text, bool accent = false) => new()
        {
            AutoSize = true,
            Text = text,
            Font = UiFont(9F, accent ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = accent ? Accent : Muted
        };

        public static Label CreateSectionLabel(string text) => new()
        {
            AutoSize = true,
            Text = text.ToUpperInvariant(),
            Font = UiFont(8F, FontStyle.Bold),
            ForeColor = Muted
        };
    }
}
