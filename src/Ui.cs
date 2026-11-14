using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StickyPro
{
    public class NoteColor
    {
        public string Name;
        public Color Header, Body, Text;
        public NoteColor(string name, Color header, Color body, Color text) { Name = name; Header = header; Body = body; Text = text; }
    }

    static partial class Ui
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }

        public const int SpaceSmall = 4, Space = 8, SpaceLarge = 16, Radius = 8, ControlHeight = 32;
        public static readonly string FontName = ChooseFont();
        public static readonly Font Base = new Font(FontName, 10f);
        public static readonly Font Bold = new Font(FontName, 10f, FontStyle.Bold);
        public static readonly Font Small = new Font(FontName, 8.5f);
        public static readonly Font Big = new Font(FontName, 14f, FontStyle.Bold);
        public static readonly Font Metric = new Font(FontName, 24f, FontStyle.Bold);
        public static Font NoteFont = new Font("Segoe UI", 11f);
        static string iconFamily;
        static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, ComboFrame> comboFrames = new System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, ComboFrame>();

        // ---------- Theme ----------
        public static bool Dark;
        public static Color Accent { get { return Dark ? Color.FromArgb(96, 170, 255) : Color.FromArgb(0, 103, 192); } }
        public static Color AccentFill { get { return Color.FromArgb(0, 103, 192); } }
        public static Color Bg { get { return Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
        public static Color Card { get { return Dark ? Color.FromArgb(45, 45, 45) : Color.White; } }
        public static Color Field { get { return Dark ? Color.FromArgb(40, 40, 40) : Color.White; } }
        public static Color Nav { get { return Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(235, 235, 235); } }
        // PicPick-style solid blue sidebar with white text.
        public static Color Sidebar { get { return Dark ? Color.FromArgb(22, 58, 102) : Color.FromArgb(26, 110, 194); } }
        public static Color SidebarSel { get { return Dark ? Color.FromArgb(12, 36, 68) : Color.FromArgb(14, 78, 148); } }
        public static Color SidebarHover { get { return Dark ? Color.FromArgb(33, 74, 124) : Color.FromArgb(46, 128, 210); } }
        public static Color Header { get { return Dark ? Color.FromArgb(110, 180, 255) : Color.FromArgb(26, 110, 194); } }
        public static Color Line { get { return Dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(222, 222, 222); } }
        public static Color Text { get { return Dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(30, 30, 30); } }
        public static Color Muted { get { return Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(105, 105, 105); } }
        public static Color SelBg { get { return Dark ? Color.FromArgb(38, 60, 88) : Color.FromArgb(224, 236, 250); } }
        public static Color HoverBg { get { return Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(236, 236, 236); } }
        public static Color PressBg { get { return Dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(225, 225, 225); } }
        public static Color AccentHover { get { return Color.FromArgb(0, 90, 170); } }
        public static Color AccentPressed { get { return Color.FromArgb(0, 77, 148); } }
        public static Color Success { get { return Dark ? Color.FromArgb(108, 203, 144) : Color.FromArgb(26, 127, 66); } }
        public static Color Danger { get { return Dark ? Color.FromArgb(255, 143, 143) : Color.FromArgb(196, 43, 48); } }
        public static Color Warning { get { return Dark ? Color.FromArgb(249, 202, 101) : Color.FromArgb(151, 98, 0); } }
        public static Color Track { get { return Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(225, 225, 225); } }
        public static Color Canvas { get { return Dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(225, 225, 225); } }
        public static Color Overlay { get { return Color.FromArgb(36, 36, 36); } }
        public static Color OverlayField { get { return Color.FromArgb(60, 60, 60); } }
        public static Color OverlayLine { get { return Color.FromArgb(90, 90, 90); } }
        public static Color NoteMuted { get { return Color.FromArgb(105, 105, 105); } }
        public static Color NoteMutedDark { get { return Color.FromArgb(200, 200, 200); } }
        public static Color PinBack { get { return Dark ? Color.FromArgb(66, 58, 30) : Color.FromArgb(255, 250, 225); } }
        public static readonly Color PinStrip = Color.FromArgb(255, 200, 0);

        public static readonly NoteColor[] Colors = new[]
        {
            new NoteColor("Yellow",   Color.FromArgb(255, 235, 140), Color.FromArgb(255, 247, 209), Color.FromArgb(32, 32, 32)),
            new NoteColor("Green",    Color.FromArgb(182, 232, 172), Color.FromArgb(228, 249, 224), Color.FromArgb(32, 32, 32)),
            new NoteColor("Pink",     Color.FromArgb(255, 187, 220), Color.FromArgb(255, 228, 241), Color.FromArgb(32, 32, 32)),
            new NoteColor("Purple",   Color.FromArgb(218, 192, 255), Color.FromArgb(242, 230, 255), Color.FromArgb(32, 32, 32)),
            new NoteColor("Blue",     Color.FromArgb(180, 220, 255), Color.FromArgb(226, 241, 255), Color.FromArgb(32, 32, 32)),
            new NoteColor("Orange",   Color.FromArgb(255, 200, 150), Color.FromArgb(255, 236, 218), Color.FromArgb(32, 32, 32)),
            new NoteColor("Gray",     Color.FromArgb(214, 212, 210), Color.FromArgb(243, 242, 241), Color.FromArgb(32, 32, 32)),
            new NoteColor("Charcoal", Color.FromArgb(50, 50, 50),    Color.FromArgb(64, 64, 64),    Color.FromArgb(240, 240, 240)),
        };

        // Color labels for clipboard items
        public static readonly string[] LabelNames = { "Red", "Orange", "Yellow", "Green", "Blue", "Purple" };
        public static Color LabelColor(string name)
        {
            switch (name)
            {
                case "Red": return Color.FromArgb(232, 72, 72);
                case "Orange": return Color.FromArgb(245, 150, 40);
                case "Yellow": return Color.FromArgb(240, 200, 0);
                case "Green": return Color.FromArgb(60, 180, 90);
                case "Blue": return Color.FromArgb(50, 130, 230);
                case "Purple": return Color.FromArgb(150, 90, 220);
            }
            return Color.Empty;
        }

        public static NoteColor ColorOf(string name)
        {
            foreach (var c in Colors) if (c.Name == name) return c;
            return Colors[0];
        }

        static string ChooseFont()
        {
            string name = "Segoe UI";
            foreach (var family in FontFamily.Families)
            {
                using (family) if (family.Name == "Segoe UI Variable Text") name = family.Name;
            }
            return name;
        }

        public static void SetNoteFont(string name, float size)
        {
            var old = NoteFont;
            try { NoteFont = new Font(name, size); }
            catch { NoteFont = new Font("Segoe UI", 11f); }
            old.Dispose();
        }

        public static void UseFont(Control control, Font font)
        {
            control.Font = font;
            control.Disposed += delegate { font.Dispose(); };
        }

        public static void ClearControls(Control.ControlCollection controls)
        {
            foreach (Control c in controls.Cast<Control>().ToArray()) c.Dispose();
        }

        public static void ClearItems(ToolStripItemCollection items)
        {
            while (items.Count > 0) { var item = items[0]; items.RemoveAt(0); DisposeItem(item); }
        }

        public static void DisposeItem(ToolStripItem item)
        {
            var menu = item as ToolStripDropDownItem;
            if (menu != null && menu.HasDropDownItems) ClearItems(menu.DropDownItems);
            var image = item.Image;
            item.Image = null;
            if (image != null) image.Dispose();
            item.Dispose();
        }

        public static Font Icon(float size)
        {
            if (iconFamily == null)
            {
                iconFamily = "Segoe UI Symbol";
                foreach (var f in FontFamily.Families)
                {
                    using (f)
                    {
                        if (f.Name == "Segoe Fluent Icons") iconFamily = f.Name;
                        else if (f.Name == "Segoe MDL2 Assets" && iconFamily != "Segoe Fluent Icons") iconFamily = f.Name;
                    }
                }
            }
            Font font;
            if (!iconFonts.TryGetValue(size, out font)) { font = new Font(iconFamily, size); iconFonts[size] = font; }
            return font;
        }

        // Segoe MDL2 / Fluent glyphs
        public const string GAdd = "", GMore = "", GClose = "", GPin = "", GPinned = "",
            GBold = "", GItalic = "", GUnder = "", GBullets = "", GImage = "",
            GDelete = "", GNote = "", GPrompt = "", GClip = "", GCopy = "", GSearch = "",
            GClock = "", GSettings = "";

        static readonly System.Reflection.PropertyInfo doubleBufferedProp =
            typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // Flicker-free resizing: containers paint off-screen. (Panel covers FlowLayoutPanel and TableLayoutPanel.)
        public static void Smooth(Control c)
        {
            if (doubleBufferedProp == null) return;
            if (c is Panel || c is UserControl || c is TabPage || c is SplitContainer || c is Form)
                try { doubleBufferedProp.SetValue(c, true, null); } catch { }
        }

        // Applies several visual changes as one repaint.
        public static void Batch(Control c, Action change)
        {
            if (c == null || !c.IsHandleCreated || !c.Visible) { change(); return; }
            Native.SendMessage(c.Handle, Native.WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try { c.SuspendLayout(); try { change(); } finally { c.ResumeLayout(true); } }
            finally { Native.SendMessage(c.Handle, Native.WM_SETREDRAW, (IntPtr)1, IntPtr.Zero); c.Invalidate(true); }
        }

        // Applies theme colors to inputs (which do not inherit colors) and dark scrollbars / title bar.
        public static void Theme(Control root)
        {
            Smooth(root);
            var menu = root.ContextMenuStrip as OwnedMenu;
            if (menu != null && !menu.Bound)
            {
                menu.Bound = true;
                root.Disposed += delegate { menu.Dispose(); };
            }
            foreach (Control c in root.Controls)
            {
                if (c is TextBox || c is ComboBox || c is NumericUpDown || (c is ListBox && !(c is CardList)) || c is RichTextBox)
                {
                    c.BackColor = c is ListBox ? Bg : Field;
                    c.ForeColor = Text;
                    var cb = c as ComboBox;
                    if (cb != null)
                    {
                        cb.FlatStyle = FlatStyle.Flat;
                        cb.DrawMode = DrawMode.OwnerDrawFixed;
                        cb.ItemHeight = cb.Font.Height + S(SpaceSmall);
                        cb.DrawItem -= DrawCombo; cb.DrawItem += DrawCombo;
                        comboFrames.GetValue(cb, cbox => new ComboFrame(cbox));
                    }
                }
                var check = c as CheckBox;
                if (check != null) { check.FlatStyle = FlatStyle.Flat; check.ForeColor = Text; check.FlatAppearance.BorderColor = Line; check.FlatAppearance.CheckedBackColor = SelBg; }
                var date = c as DateTimePicker;
                if (date != null) { date.CalendarMonthBackground = Field; date.CalendarForeColor = Text; }
                if (c is TextBox || c is ListBox || c is RichTextBox || c is ComboBox || c is ListView || c is NumericUpDown) DarkScroll(c);
                Smooth(c);
                if (c.HasChildren) Theme(c);
            }
        }

        public static void DarkScroll(Control c)
        {
            c.HandleCreated -= ApplyScrollTheme;
            c.HandleCreated += ApplyScrollTheme;
            if (c.IsHandleCreated) ApplyScrollTheme(c, EventArgs.Empty);
        }

        static void ApplyScrollTheme(object sender, EventArgs e) { var c = (Control)sender; try { Native.SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { } }

        static void DrawCombo(object sender, DrawItemEventArgs e)
        {
            var combo = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(selected ? SelBg : Field)) e.Graphics.FillRectangle(b, e.Bounds);
            string text = e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            var r = e.Bounds; r.Inflate(-S(SpaceSmall), 0);
            var dir = IsRtl(text) ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, r, combo.Enabled ? Text : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | dir);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        class ComboFrame : NativeWindow
        {
            readonly ComboBox combo;
            public ComboFrame(ComboBox combo)
            {
                this.combo = combo;
                combo.HandleCreated += delegate { AssignHandle(combo.Handle); };
                combo.HandleDestroyed += delegate { ReleaseHandle(); };
                combo.Disposed += delegate { ReleaseHandle(); };
                if (combo.IsHandleCreated) AssignHandle(combo.Handle);
            }
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                bool printing = m.Msg == 0x317 || m.Msg == 0x318;
                if ((!printing && m.Msg != 0xF) || combo.DropDownStyle == ComboBoxStyle.Simple) return;
                IntPtr dc = printing ? m.WParam : Native.GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using (var g = Graphics.FromHdc(dc))
                    using (var fill = new SolidBrush(combo.Enabled ? Field : Bg))
                    {
                        int arrow = Math.Max(S(24), SystemInformation.VerticalScrollBarWidth);
                        var r = new Rectangle(0, 0, combo.Width - 1, combo.Height - 1);
                        if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
                        {
                            g.FillRectangle(fill, new Rectangle(0, 0, combo.Width, combo.Height));
                            var text = new Rectangle(S(6), 0, Math.Max(0, r.Width - arrow - S(8)), r.Height);
                            var dir = combo.RightToLeft == RightToLeft.Yes || IsRtl(combo.Text) ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
                            TextRenderer.DrawText(g, combo.Text, combo.Font, text, combo.Enabled ? Text : Muted, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | dir);
                        }
                        // Editable combos retain their native edit child and selection.
                        g.FillRectangle(fill, Math.Max(0, r.Right - arrow), 0, arrow + 1, r.Height + 1);
                        int cx = r.Right - arrow / 2, cy = r.Height / 2;
                        using (var ink = new SolidBrush(combo.Enabled ? Text : Muted)) g.FillPolygon(ink, new[] { new Point(cx - S(4), cy - S(1)), new Point(cx + S(4), cy - S(1)), new Point(cx, cy + S(3)) });
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var path = Round(r, S(SpaceSmall)))
                        using (var pen = new Pen(combo.ContainsFocus ? Accent : Line)) g.DrawPath(pen, path);
                    }
                }
                finally { if (!printing) Native.ReleaseDC(Handle, dc); }
            }
        }

        public static void DarkTitle(Form f)
        {
            EventHandler set = delegate { try { int v = Dark ? 1 : 0; Native.DwmSetWindowAttribute(f.Handle, 20, ref v, 4); v = 2; Native.DwmSetWindowAttribute(f.Handle, 33, ref v, 4); } catch { } };
            if (f.IsHandleCreated) set(f, EventArgs.Empty); else f.HandleCreated += set;
        }

        class MenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return HoverBg; } }
            public override Color MenuItemSelectedGradientBegin { get { return HoverBg; } }
            public override Color MenuItemSelectedGradientEnd { get { return HoverBg; } }
            public override Color MenuItemBorder { get { return Line; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color ToolStripDropDownBackground { get { return Card; } }
            public override Color ImageMarginGradientBegin { get { return Card; } }
            public override Color ImageMarginGradientMiddle { get { return Card; } }
            public override Color ImageMarginGradientEnd { get { return Card; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Line; } }
            public override Color CheckBackground { get { return SelBg; } }
            public override Color CheckSelectedBackground { get { return SelBg; } }
            public override Color CheckPressedBackground { get { return SelBg; } }
            public override Color ToolStripGradientBegin { get { return Card; } }
            public override Color ToolStripGradientMiddle { get { return Card; } }
            public override Color ToolStripGradientEnd { get { return Card; } }
            public override Color ToolStripBorder { get { return Line; } }
            public override Color ButtonSelectedGradientBegin { get { return HoverBg; } }
            public override Color ButtonSelectedGradientEnd { get { return HoverBg; } }
            public override Color ButtonPressedGradientBegin { get { return PressBg; } }
            public override Color ButtonPressedGradientEnd { get { return PressBg; } }
            public override Color ButtonCheckedGradientBegin { get { return SelBg; } }
            public override Color ButtonCheckedGradientEnd { get { return SelBg; } }
        }

        public static ContextMenuStrip Menu(bool persistent = false)
        {
            var m = new OwnedMenu { Bound = persistent };
            m.Font = Base;
            m.Closed += delegate
            {
                if (!m.Bound && (m.SourceControl == null || m.SourceControl.ContextMenuStrip != m)) Later(250, () => m.Dispose());
            };
            ThemeMenu(m);
            return m;
        }

        class OwnedMenu : ContextMenuStrip
        {
            public bool Bound;
            // Items are often added after creation: theme them and add icons right before showing.
            protected override void OnOpening(System.ComponentModel.CancelEventArgs e) { base.OnOpening(e); ThemeMenu(this); }
            protected override void Dispose(bool disposing)
            {
                if (disposing) ClearItems(Items);
                base.Dispose(disposing);
            }
        }

        public static void ThemeMenu(ToolStrip m)
        {
            m.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            m.BackColor = Card;
            m.ForeColor = Text;
            foreach (ToolStripItem i in m.Items)
            {
                i.ForeColor = Text;
                AutoIcon(i);
                var mi = i as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) ThemeMenu(mi.DropDown);
            }
        }

        public static Button Btn(string text, EventHandler click, bool primary = false)
        {
            var b = new FluentButton { Primary = primary };
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = primary ? AccentFill : Line;
            b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : HoverBg;
            b.BackColor = primary ? AccentFill : Card;
            b.ForeColor = primary ? Color.White : Text;
            b.Font = Base;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(S(Space), S(SpaceSmall), S(Space), S(SpaceSmall));
            b.Margin = new Padding(0, 0, S(SpaceSmall), S(SpaceSmall));
            b.MinimumSize = S(0, ControlHeight);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            b.Glyph = GlyphFor(text);
            if (b.Glyph != null) b.Text = text.TrimStart('+', '●', ' ');
            if (click != null) b.Click += click;
            return b;
        }

        // Icons everywhere: buttons get a Segoe Fluent/MDL2 icon chosen from their text.
        static readonly string[][] glyphWords = {
            new[] { "run as admin", "" }, new[] { "end task", "" }, new[] { "pop out", "" }, new[] { "copy path", "" },
            new[] { "copy", "" }, new[] { "paste", "" }, new[] { "save swatch", "" }, new[] { "save", "" }, new[] { "back up", "" },
            new[] { "restore", "" }, new[] { "delete", "" }, new[] { "remove", "" }, new[] { "clear", "" }, new[] { "clean", "" },
            new[] { "refresh", "" }, new[] { "repeat", "" }, new[] { "deep scan", "" }, new[] { "search", "" }, new[] { "find", "" },
            new[] { "new note", "" }, new[] { "new prompt", "" }, new[] { "new", "" }, new[] { "add", "" }, new[] { "create", "" },
            new[] { "import", "" }, new[] { "export", "" }, new[] { "folder", "" }, new[] { "open", "" }, new[] { "show in", "" },
            new[] { "settings", "" }, new[] { "shortcut", "" }, new[] { "record", "" }, new[] { "text from screen", "" },
            new[] { "full screen", "" }, new[] { "window control", "" }, new[] { "window", "" }, new[] { "scrolling", "" },
            new[] { "fixed", "" }, new[] { "free", "" }, new[] { "region", "" }, new[] { "snip", "" }, new[] { "capture", "" },
            new[] { "pick from screen", "" }, new[] { "color", "" }, new[] { "print", "" }, new[] { "pin", "" },
            new[] { "history", "" }, new[] { "tag", "" }, new[] { "rename", "" }, new[] { "edit", "" }, new[] { "queue", "" },
            new[] { "stop", "" }, new[] { "pause", "" }, new[] { "resume", "" }, new[] { "start", "" }, new[] { "cancel", "" },
            new[] { "close", "" }, new[] { "got it", "" }, new[] { "ok", "" }, new[] { "use ", "" }, new[] { "set ", "" },
            new[] { "default", "" }, new[] { "favorite", "" }, new[] { "cleanup", "" }, new[] { "fit", "" },
            new[] { "exit", "" }, new[] { "show / hide", "" }, new[] { "graphic", "" }, new[] { "screen", "" },
            new[] { "home", "" }, new[] { "sticky", "" }, new[] { "notes", "" }, new[] { "prompts", "" }, new[] { "clipboard", "" },
            new[] { "system", "" }, new[] { "file activity", "" }, new[] { "magnifier", "" }, new[] { "ruler", "" },
            new[] { "crosshair", "" }, new[] { "protractor", "" }, new[] { "whiteboard", "" },
            new[] { "highlighter", "" }, new[] { "eraser", "" }, new[] { "pen", "" }, new[] { "box", "" }, new[] { "arrow", "" },
            new[] { "text", "" }, new[] { "hide", "" }, new[] { "crop", "" }, new[] { "undo", "" }, new[] { "redo", "" },
            new[] { "data folder", "" }, new[] { "paste queue", "" },
        };

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripItem, object> autoIcons = new System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripItem, object>();
        public static Bitmap GlyphImage(string glyph, int size, Color color)
        {
            var b = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            using (var br = new SolidBrush(color))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph, Icon(size * 0.56f), br, new RectangleF(0, 0, size, size + 1), sf);
            }
            return b;
        }
        // Adds (or re-colours after a theme change) an icon on menu items and toolbar buttons that have none.
        static void AutoIcon(ToolStripItem i)
        {
            if (i is ToolStripSeparator || i is ToolStripControlHost) return;
            object marker;
            bool ours = autoIcons.TryGetValue(i, out marker);
            if (i.Image != null && !ours) return;
            string glyph = GlyphFor(i.Text);
            if (glyph == null) return;
            var old = i.Image;
            i.Image = GlyphImage(glyph, S(16), Accent);
            if (old != null) old.Dispose();
            if (!ours) autoIcons.Add(i, glyph);
            var button = i as ToolStripButton;
            if (button != null && button.DisplayStyle == ToolStripItemDisplayStyle.Text) button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
        }
        public static string GlyphFor(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim().TrimStart('+', '●', ' ').ToLowerInvariant() + " ";
            foreach (var w in glyphWords) if (t.StartsWith(w[0]) || t.Contains(" " + w[0])) return w[1];
            return null;
        }

        public static TextBox Search(string cue)
        {
            var t = new FluentTextBox();
            t.Font = Base;
            t.BorderStyle = BorderStyle.FixedSingle;
            Cue(t, cue);
            return t;
        }

        public static void Cue(TextBox t, string cue)
        {
            EventHandler set = delegate { Native.SendMessage(t.Handle, Native.EM_SETCUEBANNER, (IntPtr)1, cue); };
            if (t.IsHandleCreated) set(t, EventArgs.Empty); else t.HandleCreated += set;
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0) return p;
            int d = Math.Max(1, rad * 2);
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static string Ago(long ticks)
        {
            if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return "";
            var t = new DateTime(ticks);
            var now = DateTime.Now;
            if (t.Date == now.Date) return t.ToString("HH:mm");
            if (t.Date == now.Date.AddDays(-1)) return "Yesterday " + t.ToString("HH:mm");
            if (t.Date == now.Date.AddDays(1)) return "Tomorrow " + t.ToString("HH:mm");
            if (Math.Abs((now - t).TotalDays) < 7) return t.ToString("ddd HH:mm");
            if (t.Year == now.Year) return t.ToString("d MMM HH:mm");
            return t.ToString("d MMM yyyy");
        }

        public static string OneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length > max * 3) s = s.Substring(0, max * 3);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }

        // True when the first strong (letter) character is Arabic / Hebrew / Persian / Urdu.
        public static bool IsRtl(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int n = Math.Min(s.Length, 400);
            for (int i = 0; i < n; i++)
            {
                char c = s[i];
                if ((c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFF)) return true;
                if (char.IsLetter(c)) return false;
            }
            return false;
        }

        public static void Later(int ms, Action a)
        {
            var t = new Timer();
            t.Interval = Math.Max(1, ms);
            t.Tick += delegate { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }

        public static Bitmap Swatch(Color c, int size)
        {
            var b = new Bitmap(size, size);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(c)) g.FillEllipse(br, 1, 1, size - 3, size - 3);
                using (var p = new Pen(Color.FromArgb(120, 0, 0, 0))) g.DrawEllipse(p, 1, 1, size - 3, size - 3);
            }
            return b;
        }

        // ---------- App logo / icon ----------

        public static Bitmap Logo(int s)
        {
            var b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                // "All in One": blue rounded square holding a 2x2 grid of tools; the top-left tile is a yellow note.
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                float m = Math.Max(0.5f, s * 0.04f);
                var r = new RectangleF(m, m, s - 2 * m, s - 2 * m);
                using (var path = Round(Rectangle.Round(r), Math.Max(2, (int)(s * 0.22f))))
                using (var br = new LinearGradientBrush(r, Color.FromArgb(46, 134, 222), Color.FromArgb(18, 88, 168), 90f))
                    g.FillPath(br, path);
                float pad = s * 0.19f, gap = Math.Max(1f, s * 0.08f), tile = (s - 2 * pad - gap) / 2f;
                int rad = Math.Max(1, (int)(tile * 0.28f));
                for (int i = 0; i < 4; i++)
                {
                    float x = pad + (i % 2) * (tile + gap), y = pad + (i / 2) * (tile + gap);
                    var tr = Rectangle.Round(new RectangleF(x, y, tile, tile));
                    var c = i == 0 ? Color.FromArgb(255, 213, 0) : Color.White;
                    using (var p = Round(tr, rad)) using (var br = new SolidBrush(c)) g.FillPath(br, p);
                }
                if (s >= 32)
                {
                    // tiny folded corner on the note tile
                    float x1 = pad + tile, y1 = pad + tile, f = tile * 0.32f;
                    using (var br = new SolidBrush(Color.FromArgb(205, 160, 0)))
                        g.FillPolygon(br, new[] { new PointF(x1 - f, y1), new PointF(x1, y1 - f), new PointF(x1 - f, y1 - f) });
                }
            }
            return b;
        }

        public static byte[] IcoBytes()
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Logo(s))
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
                w.Flush();
                return ms.ToArray();
            }
        }

        // ---------- Dialogs ----------

        public static Form Dialog(string title, int w, int h)
        {
            var f = new Form();
            f.Text = title; f.Font = Base; f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = false; f.MinimizeBox = false; f.StartPosition = FormStartPosition.CenterScreen;
            f.ShowInTaskbar = false; f.TopMost = true; f.AutoScaleMode = AutoScaleMode.None;
            f.BackColor = Card; f.ForeColor = Text; f.Icon = App.AppIcon;
            f.ClientSize = new Size(S(w), S(h));
            f.MaximumSize = Screen.FromPoint(Cursor.Position).WorkingArea.Size;
            // Dialogs can grow (long prompts, many variables) but never shrink below the designed size.
            f.FormBorderStyle = FormBorderStyle.Sizable; f.SizeGripStyle = SizeGripStyle.Show;
            f.Shown += delegate { f.MinimumSize = f.Size; };
            DarkTitle(f);
            return f;
        }

        static readonly Dictionary<string, string> lastValues = new Dictionary<string, string>();

        public static string FillVariables(string body, IWin32Window owner)
        {
            if (string.IsNullOrEmpty(body)) return body;
            var names = new List<string>();
            foreach (Match m in Regex.Matches(body, @"\{\{\s*([^{}]+?)\s*\}\}"))
                if (!names.Contains(m.Groups[1].Value)) names.Add(m.Groups[1].Value);
            if (names.Count == 0) return body;

            using (var f = Dialog("Fill in the prompt", 460, Math.Min(600, 90 + names.Count * 82)))
            {
                var table = new TableLayoutPanel();
                table.Dock = DockStyle.Fill;
                table.Padding = new Padding(S(14));
                table.ColumnCount = 1; table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                table.AutoScroll = true;
                var boxes = new List<TextBox>();
                foreach (var n in names)
                {
                    var l = new Label(); l.Text = n; l.AutoSize = true; l.Font = Bold; l.Margin = new Padding(0, S(6), 0, S(2));
                    var t = new FluentTextBox(); t.Dock = DockStyle.Fill; t.Font = Base;
                    string v; if (lastValues.TryGetValue(n, out v)) t.Text = v;
                    t.Multiline = true; t.Height = S(48); t.ScrollBars = ScrollBars.Vertical; t.AcceptsReturn = false;
                    table.Controls.Add(l); table.Controls.Add(t); boxes.Add(t);
                }
                var bar = new FlowLayoutPanel(); bar.FlowDirection = FlowDirection.RightToLeft; bar.AutoSize = true; bar.Dock = DockStyle.Fill; bar.Margin = new Padding(0, S(12), 0, 0);
                var ok = Btn("Use prompt", null, true); ok.DialogResult = DialogResult.OK;
                var cancel = Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                bar.Controls.Add(ok); bar.Controls.Add(cancel);
                table.Controls.Add(bar);
                f.Controls.Add(table);
                f.AcceptButton = ok; f.CancelButton = cancel;
                Theme(f);
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                string result = body;
                for (int i = 0; i < names.Count; i++)
                {
                    lastValues[names[i]] = boxes[i].Text;
                    string value = boxes[i].Text;
                    result = Regex.Replace(result, @"\{\{\s*" + Regex.Escape(names[i]) + @"\s*\}\}", m => value);
                }
                return result;
            }
        }

        public static bool HasVariables(string body) { return body != null && Regex.IsMatch(body, @"\{\{\s*[^{}]+?\s*\}\}"); }

        public static string Ask(string title, string label, string value, IWin32Window owner)
        {
            using (var f = Dialog(title, 390, 124))
            {
                var fields = LayoutKit.Fields();
                var t = new FluentTextBox { Text = value ?? "" };
                var ok = Btn("OK", null, true); ok.DialogResult = DialogResult.OK;
                var cancel = Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                LayoutKit.Add(fields, new WrapLabel { Text = label }, false); LayoutKit.Add(fields, t, false);
                f.Controls.Add(fields); f.Controls.Add(LayoutKit.Actions(DockStyle.Bottom, ok, cancel));
                f.Height = Math.Max(f.Height, S(190));
                f.AcceptButton = ok; f.CancelButton = cancel;
                Theme(f);
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }

        // Returns new reminder ticks, 0 to remove, or -1 when cancelled.
        public static long AskReminder(long current, IWin32Window owner)
        {
            using (var f = Dialog("Remind me", 420, 190))
            {
                var fields = LayoutKit.Fields();
                var dt = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd d MMM yyyy   HH:mm" };
                DateTime next = current > DateTime.Now.Ticks && current <= DateTime.MaxValue.Ticks ? new DateTime(current) : DateTime.Now.AddHours(1);
                dt.Value = next > dt.MaxDate ? dt.MaxDate : next;
                long result = -1;
                var quick = LayoutKit.Actions(DockStyle.Fill);
                Action<DateTime> pick = d => { result = d.Ticks; f.DialogResult = DialogResult.OK; };
                quick.Controls.Add(Btn("In 15 min", delegate { pick(DateTime.Now.AddMinutes(15)); }));
                quick.Controls.Add(Btn("In 1 hour", delegate { pick(DateTime.Now.AddHours(1)); }));
                quick.Controls.Add(Btn("Tomorrow 9:00", delegate { pick(DateTime.Today.AddDays(1).AddHours(9)); }));
                LayoutKit.Add(fields, new WrapLabel { Text = "When should this note pop up?" }, false);
                LayoutKit.Add(fields, dt, false); LayoutKit.Add(fields, quick, false);
                var ok = Btn("Set reminder", delegate { result = dt.Value.Ticks; f.DialogResult = DialogResult.OK; }, true);
                var cancel = Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                var bar = LayoutKit.Actions(DockStyle.Bottom, ok, cancel);
                if (current > 0) bar.Controls.Add(Btn("Remove", delegate { result = 0; f.DialogResult = DialogResult.OK; }));
                f.Controls.Add(fields); f.Controls.Add(bar); f.ClientSize = S(440, 260); f.CancelButton = cancel;
                Theme(f);
                return f.ShowDialog(owner) == DialogResult.OK ? result : -1;
            }
        }
    }

    // Paint only on input/state changes; no animation timer or extra window per button.
    class FluentButton : Button
    {
        public bool Primary;
        public string Glyph;
        bool hover, pressed, defaultButton;
        int GlyphSpace { get { return string.IsNullOrEmpty(Glyph) ? 0 : Ui.S(22); } }
        public FluentButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text, Font);
            return new Size(Math.Max(MinimumSize.Width, text.Width + GlyphSpace + Padding.Horizontal + Ui.S(4)), Math.Max(MinimumSize.Height, Font.Height + Padding.Vertical + Ui.S(4)));
        }
        public override void NotifyDefault(bool value) { defaultButton = value; base.NotifyDefault(value); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; Invalidate(); base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent == null ? Ui.Bg : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Ui.Bg : Primary ? (pressed ? Ui.AccentPressed : hover ? Ui.AccentHover : Ui.AccentFill) : pressed ? Ui.PressBg : hover ? Ui.HoverBg : BackColor;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Ui.Round(r, Ui.S(Ui.Radius)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var p = new Pen((Focused || defaultButton) && Enabled ? Ui.Accent : Primary ? fill : Ui.Line)) g.DrawPath(p, path);
            }
            var textRect = Rectangle.Inflate(ClientRectangle, -Ui.S(Ui.Space), -Ui.S(2));
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix; else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            var fore = !Enabled ? Ui.Muted : Primary ? Color.White : ForeColor;
            if (GlyphSpace > 0)
            {
                // Icon + text centred together.
                int textW = TextRenderer.MeasureText(Text, Font).Width;
                int total = Math.Min(textRect.Width, GlyphSpace + textW);
                int x = textRect.X + Math.Max(0, (textRect.Width - total) / 2);
                TextRenderer.DrawText(g, Glyph, Ui.Icon(10.5f), new Rectangle(x, textRect.Y, Ui.S(18), textRect.Height), !Enabled ? Ui.Muted : Primary ? Color.White : Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                textRect = new Rectangle(x + GlyphSpace, textRect.Y, Math.Max(1, textRect.Right - x - GlyphSpace), textRect.Height);
                flags = (flags & ~TextFormatFlags.HorizontalCenter) | TextFormatFlags.Left;
            }
            TextRenderer.DrawText(g, Text, Font, textRect, fore, flags);
            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Inflate(r, -Ui.S(3), -Ui.S(3));
                using (var path = Ui.Round(focus, Ui.S(Ui.SpaceSmall)))
                using (var pen = new Pen(Primary ? Color.White : Ui.Accent) { DashStyle = DashStyle.Dot }) g.DrawPath(pen, path);
            }
        }
    }

    // Keep the native edit control (including IME, RTL, accessibility and selection).
    class FluentTextBox : TextBox
    {
        public FluentTextBox() { BorderStyle = BorderStyle.FixedSingle; BackColor = Ui.Field; ForeColor = Ui.Text; }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SendMessage(Handle, 0xD3 /* EM_SETMARGINS */, (IntPtr)3, (IntPtr)((Ui.S(5) << 16) | Ui.S(5)));
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width <= 0 || Height <= 0) return;
            using (var path = Ui.Round(new Rectangle(0, 0, Width, Height), Ui.S(Ui.SpaceSmall)))
            {
                var previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (BorderStyle == BorderStyle.None || !IsHandleCreated) return;
            bool printing = m.Msg == 0x317 || m.Msg == 0x318;
            if (printing || m.Msg == 0x85 || m.Msg == 0xF || m.Msg == 0x7 || m.Msg == 0x8)
            {
                IntPtr dc = printing ? m.WParam : Native.GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using (var g = Graphics.FromHdc(dc))
                    using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(Ui.SpaceSmall)))
                    using (var pen = new Pen(Focused ? Ui.Accent : Ui.Line))
                    {
                        int corner = Ui.S(Ui.SpaceSmall);
                        using (var b = new SolidBrush(Parent == null ? Ui.Bg : Parent.BackColor))
                        {
                            g.FillRectangle(b, 0, 0, corner, corner); g.FillRectangle(b, Width - corner, 0, corner, corner);
                            g.FillRectangle(b, 0, Height - corner, corner, corner); g.FillRectangle(b, Width - corner, Height - corner, corner, corner);
                        }
                        g.SmoothingMode = SmoothingMode.AntiAlias; g.DrawPath(pen, path);
                    }
                }
                finally { if (!printing) Native.ReleaseDC(Handle, dc); }
            }
        }
    }

    // Text actions used by "Paste as" / "Copy as".
    static class Transforms
    {
        public static readonly string[] Names = { "Plain text", "UPPERCASE", "lowercase", "Title Case", "Sentence case", "Trim spaces", "Remove empty lines", "Join into one line" };

        public static string Apply(int i, string t)
        {
            if (t == null) return "";
            switch (i)
            {
                case 1: return t.ToUpper();
                case 2: return t.ToLower();
                case 3: return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(t.ToLower());
                case 4:
                    {
                        var chars = t.ToLower().ToCharArray();
                        bool start = true;
                        for (int k = 0; k < chars.Length; k++)
                        {
                            if (start && char.IsLetter(chars[k])) { chars[k] = char.ToUpper(chars[k]); start = false; }
                            else if (chars[k] == '.' || chars[k] == '!' || chars[k] == '?' || chars[k] == '\n') start = true;
                        }
                        return new string(chars);
                    }
                case 5:
                    return string.Join("\r\n", t.Replace("\r\n", "\n").Split('\n').Select(l => Regex.Replace(l.Trim(), @"[ \t]+", " "))).Trim();
                case 6:
                    return string.Join("\r\n", t.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0));
                case 7:
                    return Regex.Replace(t, @"\s*\r?\n\s*", " ").Trim();
            }
            return t;
        }
    }

    public class CardInfo
    {
        public string Title = "", Sub = "", Meta = "";
        public Color Back = Ui.Card, Fore = Ui.Text, SubFore = Ui.Muted, Strip = Color.Empty;
        public Image Thumb;
    }

    // Owner-drawn list of cards; very light (native ListBox underneath).
    public class CardList : ListBox
    {
        public Func<object, CardInfo> Info;

        public CardList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            BorderStyle = BorderStyle.None;
            BackColor = Ui.Bg;
            ItemHeight = Math.Min(255, Ui.S(66));
            Font = Ui.Base;
            if (Ui.Dark) Ui.DarkScroll(this);
        }

        public void SetItems(IEnumerable<object> items)
        {
            var keep = new HashSet<object>(SelectedItems.Cast<object>());
            object top = TopIndex >= 0 && TopIndex < Items.Count ? Items[TopIndex] : null;
            BeginUpdate();
            Items.Clear();
            var arr = items.ToArray();
            Items.AddRange(arr);
            bool any = false;
            for (int i = 0; i < arr.Length && keep.Count > 0; i++)
            {
                if (keep.Contains(arr[i]))
                {
                    if (SelectionMode == SelectionMode.One) { SelectedIndex = i; any = true; break; }
                    SetSelected(i, true); any = true;
                }
            }
            if (top != null) { int ti = Array.IndexOf(arr, top); if (ti >= 0) TopIndex = ti; }
            EndUpdate();
            if (!any && keep.Count > 0) OnSelectedIndexChanged(EventArgs.Empty);
        }

        public List<T> Selected<T>() { return SelectedItems.Cast<object>().OfType<T>().ToList(); }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int i = IndexFromPoint(e.Location);
                if (i >= 0 && !GetSelected(i))
                {
                    if (SelectionMode != SelectionMode.One) ClearSelected();
                    SelectedIndex = i;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count || Info == null) return;
            var g = e.Graphics;
            CardInfo ci;
            try { ci = Info(Items[e.Index]); }
            catch { using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds); return; }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Ui.DrawCard(g, e.Bounds, ci, sel, BackColor);
        }
    }

    static partial class Ui
    {
        // Paints one card (background, colour strip, border, thumbnail, title, meta, sub text) inside 'bounds'.
        public static void DrawCard(Graphics g, Rectangle bounds, CardInfo ci, bool sel, Color back)
        {
            using (var bg = new SolidBrush(back)) g.FillRectangle(bg, bounds);
            var r = new Rectangle(bounds.X + Ui.S(6), bounds.Y + Ui.S(3), bounds.Width - Ui.S(13), bounds.Height - Ui.S(6));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(r, Ui.S(6)))
            {
                using (var b = new SolidBrush(ci.Back)) g.FillPath(b, path);
                if (ci.Strip != Color.Empty)
                {
                    g.SetClip(path);
                    using (var b = new SolidBrush(ci.Strip)) g.FillRectangle(b, r.X, r.Y, Ui.S(6), r.Height + 1);
                    g.ResetClip();
                }
                using (var p = new Pen(sel ? Ui.Accent : Ui.Line, sel ? Ui.S(2) : 1)) g.DrawPath(p, path);
            }
            g.SmoothingMode = SmoothingMode.None;
            int x = r.X + Ui.S(14);
            if (ci.Thumb != null)
            {
                int th = r.Height - Ui.S(12);
                int tw = Math.Min(Ui.S(90), ci.Thumb.Width * th / Math.Max(1, ci.Thumb.Height));
                g.DrawImage(ci.Thumb, new Rectangle(x, r.Y + Ui.S(6), tw, th));
                x += tw + Ui.S(10);
            }
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            int metaW = 0;
            if (!string.IsNullOrEmpty(ci.Meta))
            {
                metaW = Math.Min(r.Width / 2, TextRenderer.MeasureText(g, ci.Meta, Ui.Small).Width + Ui.S(4));
                TextRenderer.DrawText(g, ci.Meta, Ui.Small, new Rectangle(r.Right - metaW - Ui.S(8), r.Y + Ui.S(7), metaW, Ui.S(18)), ci.SubFore, flags | TextFormatFlags.Right);
            }
            var tf = Ui.IsRtl(ci.Title) ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            var sf = Ui.IsRtl(ci.Sub) ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            TextRenderer.DrawText(g, ci.Title, Ui.Bold, new Rectangle(x, r.Y + Ui.S(5), r.Right - x - metaW - Ui.S(14), Ui.S(22)), ci.Fore, flags | TextFormatFlags.SingleLine | tf);
            TextRenderer.DrawText(g, ci.Sub, Ui.Small, new Rectangle(x, r.Y + Ui.S(27), r.Right - x - Ui.S(10), r.Height - Ui.S(30)), ci.SubFore, flags | TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | sf);
        }
    }

    // A small clickable icon label used on note headers and toolbars.
    public class IconButton : Label
    {
        public Color HoverColor = Color.FromArgb(40, 0, 0, 0);
        bool hover;

        public IconButton(string glyph, float size, string tip, ToolTip tt, EventHandler click)
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = tip;
            Text = glyph;
            Font = Ui.Icon(size);
            TextAlign = ContentAlignment.MiddleCenter;
            Width = Ui.S(34);
            Cursor = Cursors.Hand;
            if (tt != null && tip != null) tt.SetToolTip(this, tip);
            if (click != null) Click += click;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(Rectangle.Inflate(ClientRectangle, -Ui.S(2), -Ui.S(2)), Ui.S(Ui.SpaceSmall)))
            {
                if (hover || Focused) using (var b = new SolidBrush(HoverColor)) e.Graphics.FillPath(b, path);
                if (Focused) using (var p = new Pen(ForeColor)) e.Graphics.DrawPath(p, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Panel that lets the parent borderless form handle resize at its edges.
    public class EdgePanel : Panel
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                var f = FindForm();
                if (f != null && f.FormBorderStyle == FormBorderStyle.None)
                {
                    var p = f.PointToClient(Cursor.Position);
                    int g = Ui.S(6);
                    if (p.X < g || p.Y < g || p.X >= f.ClientSize.Width - g || p.Y >= f.ClientSize.Height - g)
                    {
                        m.Result = (IntPtr)Native.HTTRANSPARENT;
                        return;
                    }
                }
            }
            base.WndProc(ref m);
        }
    }

    public class EdgeFlowPanel : FlowLayoutPanel
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                var form = FindForm();
                if (form != null && form.FormBorderStyle == FormBorderStyle.None)
                {
                    var p = form.PointToClient(Cursor.Position);
                    int edge = Ui.S(6);
                    if (p.X < edge || p.Y < edge || p.X >= form.ClientSize.Width - edge || p.Y >= form.ClientSize.Height - edge) { m.Result = (IntPtr)Native.HTTRANSPARENT; return; }
                }
            }
            base.WndProc(ref m);
        }
    }

    // Left navigation item in the main window.
    public class NavItem : Control
    {
        public string Glyph;
        bool selected, hover;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public NavItem(string glyph, string text)
        {
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTab;
            Glyph = glyph; Text = text;
            Height = Ui.S(46);
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // PicPick look: full-width darker band for the selected page on a solid blue sidebar.
            g.Clear(selected ? Ui.SidebarSel : hover ? Ui.SidebarHover : Ui.Sidebar);
            var r = new Rectangle(Ui.S(8), 0, Width - Ui.S(16), Height);
            if (selected) using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, 0, Ui.S(8), Ui.S(4), Height - Ui.S(16));
            if (Focused && !selected) using (var p = new Pen(Color.FromArgb(180, 255, 255, 255)) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, 2, 2, Width - 5, Height - 5);
            TextRenderer.DrawText(g, Glyph, Ui.Icon(13f), new Rectangle(r.X + Ui.S(6), r.Y, Ui.S(30), r.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Width < Ui.S(100)) return;
            TextRenderer.DrawText(g, Text, selected ? Ui.Bold : Ui.Base, new Rectangle(r.X + Ui.S(46), r.Y, r.Width - Ui.S(46), r.Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
