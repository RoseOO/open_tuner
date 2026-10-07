using System.Drawing;
using System.Windows.Forms;

namespace opentuner.Utilities
{
    /// <summary>
    /// A light-weight, consistent visual theme for the WinForms UI.
    ///
    /// It deliberately only touches standard controls and skips the project's
    /// custom-drawn controls (video hosts, spectrum, dynamic property panels) so
    /// that functionality is preserved. All styling is idempotent - callers may
    /// safely re-apply the theme.
    /// </summary>
    public static class Theme
    {
        public static readonly Font DefaultFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font HeaderFont = new Font("Segoe UI Semibold", 9.75F, FontStyle.Regular, GraphicsUnit.Point);

        public static readonly Color WindowBack = Color.FromArgb(245, 246, 248);
        public static readonly Color PanelBack = Color.White;
        public static readonly Color Border = Color.FromArgb(214, 219, 226);
        public static readonly Color Text = Color.FromArgb(30, 32, 36);
        public static readonly Color MutedText = Color.FromArgb(100, 105, 112);
        public static readonly Color LinkText = Color.FromArgb(0, 102, 204);

        public static readonly Color Accent = Color.FromArgb(0, 120, 212);
        public static readonly Color AccentHover = Color.FromArgb(16, 110, 190);
        public static readonly Color AccentPressed = Color.FromArgb(0, 90, 158);

        public static readonly Color ButtonHover = Color.FromArgb(233, 236, 240);
        public static readonly Color ButtonPressed = Color.FromArgb(220, 224, 229);

        public static void Apply(Form form)
        {
            if (form == null)
                return;

            form.Font = DefaultFont;
            form.BackColor = WindowBack;
            form.ForeColor = Text;

            StyleChildren(form);
        }

        /// <summary>
        /// Highlights a button as the primary action for its screen.
        /// </summary>
        public static void MakePrimary(Button button)
        {
            if (button == null)
                return;

            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.Font = HeaderFont;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentPressed;
            button.Cursor = Cursors.Hand;
        }

        private static void StyleChildren(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (ShouldSkip(child))
                    continue;

                Style(child);

                // ToolStrip/MenuStrip own their items, so only descend into normal controls.
                if (child.HasChildren && !(child is ToolStrip))
                    StyleChildren(child);
            }
        }

        private static bool ShouldSkip(Control control)
        {
            string ns = control.GetType().Namespace ?? "";

            // Leave custom / third-party rendered controls alone.
            return ns.StartsWith("opentuner.Utilities")
                || ns.StartsWith("opentuner.ExtraFeatures")
                || ns.StartsWith("LibVLCSharp")
                || ns.StartsWith("FlyleafLib");
        }

        private static void Style(Control c)
        {
            if (c.Font.Name != DefaultFont.Name || c.Font.Size != DefaultFont.Size)
                c.Font = DefaultFont;

            if (c is Button b)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = ButtonHover;
                b.FlatAppearance.MouseDownBackColor = ButtonPressed;
                b.UseVisualStyleBackColor = false;
                if (b.BackColor == Color.Empty || b.BackColor == SystemColors.Control)
                    b.BackColor = PanelBack;
                b.ForeColor = Text;
            }
            else if (c is TextBox t)
            {
                t.BorderStyle = BorderStyle.FixedSingle;
                t.BackColor = PanelBack;
                t.ForeColor = Text;
            }
            else if (c is ComboBox cb)
            {
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = PanelBack;
                cb.ForeColor = Text;
            }
            else if (c is ListBox lb)
            {
                lb.BorderStyle = BorderStyle.FixedSingle;
                lb.BackColor = PanelBack;
                lb.ForeColor = Text;
            }
            else if (c is GroupBox g)
            {
                g.ForeColor = MutedText;
                g.Font = HeaderFont;
                g.BackColor = Color.Transparent;
            }
            else if (c is TabControl tc)
            {
                StyleTabControl(tc);
            }
            else if (c is TabPage tp)
            {
                tp.BackColor = WindowBack;
                tp.ForeColor = Text;
            }
            else if (c is MenuStrip || c is ToolStrip)
            {
                StyleToolStrip((ToolStrip)c);
            }
            else if (c is CheckBox || c is RadioButton)
            {
                c.ForeColor = Text;
                c.BackColor = Color.Transparent;
            }
            else if (c is LinkLabel)
            {
                c.ForeColor = LinkText;
                c.BackColor = Color.Transparent;
            }
            else if (c is Label)
            {
                c.ForeColor = Text;
                if (c.BackColor != Color.Transparent)
                    c.BackColor = Color.Transparent;
            }
        }

        private static void StyleTabControl(TabControl tc)
        {
            tc.Font = DefaultFont;
            tc.DrawMode = TabDrawMode.OwnerDrawFixed;
            tc.ItemSize = new Size(0, 28);
            tc.SizeMode = TabSizeMode.Normal;
            tc.Padding = new Point(16, 5);

            // avoid stacking handlers if the theme is applied more than once
            tc.DrawItem -= TabControl_DrawItem;
            tc.DrawItem += TabControl_DrawItem;
        }

        private static void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            TabControl tc = (TabControl)sender;

            if (e.Index < 0 || e.Index >= tc.TabPages.Count)
                return;

            TabPage page = tc.TabPages[e.Index];
            Rectangle rect = tc.GetTabRect(e.Index);
            bool selected = tc.SelectedIndex == e.Index;

            Color back = selected ? Accent : PanelBack;
            Color fore = selected ? Color.White : Text;

            using (SolidBrush brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, rect);

            using (Pen pen = new Pen(Border))
                e.Graphics.DrawLine(pen, rect.Left, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

            TextRenderer.DrawText(
                e.Graphics,
                page.Text,
                selected ? HeaderFont : DefaultFont,
                rect,
                fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void StyleToolStrip(ToolStrip ts)
        {
            ts.Font = DefaultFont;
            ts.BackColor = PanelBack;
            ts.ForeColor = Text;
            ts.GripStyle = ToolStripGripStyle.Hidden;
            ts.Padding = new Padding(6, 2, 6, 2);
            ts.Renderer = new ToolStripProfessionalRenderer(new ModernColorTable());
            ts.RenderMode = ToolStripRenderMode.Professional;
        }

        private sealed class ModernColorTable : ProfessionalColorTable
        {
            public override Color ToolStripGradientBegin => PanelBack;
            public override Color ToolStripGradientMiddle => PanelBack;
            public override Color ToolStripGradientEnd => PanelBack;

            public override Color MenuStripGradientBegin => PanelBack;
            public override Color MenuStripGradientEnd => PanelBack;

            public override Color MenuItemSelected => Color.FromArgb(232, 240, 250);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(232, 240, 250);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(232, 240, 250);

            public override Color MenuItemPressedGradientBegin => Color.FromArgb(232, 240, 250);
            public override Color MenuItemPressedGradientEnd => Color.FromArgb(232, 240, 250);

            public override Color MenuItemBorder => Accent;
            public override Color MenuBorder => Border;

            public override Color ToolStripBorder => Border;

            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;

            public override Color ImageMarginGradientBegin => PanelBack;
            public override Color ImageMarginGradientMiddle => PanelBack;
            public override Color ImageMarginGradientEnd => PanelBack;

            public override Color ButtonSelectedHighlight => Color.FromArgb(232, 240, 250);
            public override Color ButtonSelectedBorder => Accent;
        }
    }
}
