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
        // Runs an expensive re-arrangement once, shortly after the last call (e.g. after a window resize settles),
        // instead of on every resize step. Each key has its own timer.
        static readonly Dictionary<string, Timer> debounce = new Dictionary<string, Timer>();
        public static void Debounce(string key, int ms, Action a)
        {
            Timer t;
            if (!debounce.TryGetValue(key, out t)) { t = new Timer(); debounce[key] = t; }
            t.Stop(); t.Interval = Math.Max(1, ms);
            t.Tag = a;
            t.Tick -= DebounceTick; t.Tick += DebounceTick;
            t.Start();
        }
        static void DebounceTick(object s, EventArgs e)
        {
            var t = (Timer)s;
            t.Stop();
            var action = t.Tag as Action;
            t.Tag = null;
            if (action != null) try { action(); } catch (Exception ex) { App.Log("Debounce: " + ex.Message); }
        }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }

        public const int SpaceSmall = 4, Space = 8, SpaceLarge = 16, Radius = 8, ControlHeight = 32;
        public static string FontName = ChooseFont();
        public static Font Base = new Font(FontName, 10f);
        public static Font Bold = new Font(FontName, 10f, FontStyle.Bold);
        public static Font Small = new Font(FontName, 8.5f);
        public static Font Big = new Font(FontName, 14f, FontStyle.Bold);
        public static Font Metric = new Font(FontName, 24f, FontStyle.Bold);
        public static Font Title = new Font(FontName, 17f, FontStyle.Bold);
        public static Font Kpi = new Font(FontName, 20f, FontStyle.Bold);
        // Interface font and size from Settings. Old fonts are not disposed (open windows may still use them).
        public static void InitFonts(string name, float size) { InitFonts(name, size, "", 1f); }
        // Heading font / scale drive page titles, card titles and big numbers (Big, Title, Metric, Kpi).
        public static void InitFonts(string name, float size, string heading, float headingScale)
        {
            if (size < 8 || size > 16) size = 10f;
            if (float.IsNaN(headingScale) || headingScale < 0.85f || headingScale > 1.4f) headingScale = 1f;
            string family = Installed(name) ? name : ChooseFont();
            string head = Installed(heading) ? heading : family;
            var hs = FontStyle.Bold;
            if (!Installed(heading))
                foreach (var semi in new[] { "Segoe UI Variable Display Semib", "Segoe UI Semibold" })
                    if (Installed(semi)) { head = semi; hs = FontStyle.Regular; break; }   // semibold face: calmer than bold
            float k = size / 10f, h = k * headingScale;
            FontName = family;
            Base = new Font(family, size); Bold = new Font(family, size, FontStyle.Bold); Small = new Font(family, 8.5f * k);
            Big = new Font(head, 14f * h, hs); Metric = new Font(head, 24f * h, hs);
            Title = new Font(head, 17f * h, hs); Kpi = new Font(head, 20f * h, hs);
        }
        public static bool Installed(string family)
        {
            if (string.IsNullOrEmpty(family)) return false;
            try { using (var test = new Font(family, 10f)) return string.Equals(test.Name, family, StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        // Look settings that are read while painting: custom accent, corner radius scale, card shadows.
        public static Color? CustomAccent;
        public static float RadiusScale = 1f;
        public static bool Shadows = true;
        public static void ApplyLook()
        {
            var s = Store.Settings;
            CustomAccent = null;
            if (!string.IsNullOrEmpty(s.AccentColor)) try { CustomAccent = ColorTranslator.FromHtml(s.AccentColor); } catch { }
            RadiusScale = s.CornerRadius < 0 ? 1f : s.CornerRadius / 8f;
            Shadows = s.Shadows;
            InitFonts(s.UiFont, s.UiFontSize, s.HeadingFont, s.HeadingScale);
        }
        // Your name for the app (Settings > Names) or "All in One".
        public static string AppTitle { get { return Store.Settings != null && !string.IsNullOrEmpty(Store.Settings.AppTitle) ? Store.Settings.AppTitle : App.Name; } }
        // Your name (Settings > Names) or "You".
        public static string UserName { get { return Store.Settings != null && !string.IsNullOrEmpty(Store.Settings.UserName) ? Store.Settings.UserName : "You"; } }
        // Your name for a page in the sidebar, or the default.
        public static string PageName(string key, string def)
        {
            string v; return Store.Settings != null && Store.Settings.PageNames.TryGetValue(key, out v) && !string.IsNullOrEmpty(v) ? v : def;
        }

        // Colour themes: the accent (and the classic sidebar colour) follow the theme.
        public static readonly string[] ThemeNames = { "Ocean blue", "Royal purple", "Emerald", "Teal", "Sunset orange", "Rose", "Nexus indigo", "SerQ blue", "Soft coral" };
        static readonly Color[] themeBase = { Color.FromArgb(26, 110, 194), Color.FromArgb(109, 62, 196), Color.FromArgb(22, 128, 84), Color.FromArgb(13, 122, 134), Color.FromArgb(206, 92, 20), Color.FromArgb(190, 38, 94), Color.FromArgb(91, 76, 230), Color.FromArgb(51, 102, 240), Color.FromArgb(232, 88, 44) };
        public const int SerqTheme = 7, SoftTheme = 8;
        // Soft design: charcoal floating sidebar, quiet grey canvas, white cards, coral accent (uses the SerQ layout).
        public static bool Soft;
        // SerQ design: Nexus layout with a floating navy sidebar, blue-grey canvas and soft card shadows.
        public static bool Navy;
        public static int ThemeIndex;
        public static Color ThemeColor(int i) { return themeBase[Math.Max(0, Math.Min(themeBase.Length - 1, i))]; }
        // Nexus design (clean white sidebar, soft grey canvas, white cards) or the classic solid-colour sidebar.
        public static bool Nexus = true;
        static Color H(string hex) { return ColorTranslator.FromHtml(hex); }
        // Chart / KPI palette (Nexus): accent, teal, light accent, light teal, amber, rose.
        public static Color Series(int i)
        {
            switch (i % 6)
            {
                case 0: return Accent;
                case 1: return H("#22C3B4");
                case 2: return Mix(AccentFill, Color.White, 0.42);
                case 3: return H("#7FE3D8");
                case 4: return H("#F59E0B");
                default: return H("#F43F5E");
            }
        }
        // Soft drop shadow under a rounded card (SerQ look); a no-op in the other designs.
        public static void Shadow(Graphics g, Rectangle r, int radius)
        {
            if (!Navy || !Shadows) return;
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            int steps = 6;
            for (int i = steps; i >= 1; i--)
            {
                var s = new Rectangle(r.X - i + 1, r.Y - i + S(4), r.Width + 2 * i - 2, r.Height + 2 * i - S(2));
                using (var path = Round(s, radius + i))
                using (var b = new SolidBrush(Color.FromArgb(Dark ? 14 : 7, 30, 45, 90))) g.FillPath(b, path);
            }
            g.SmoothingMode = old;
        }
        // Card with optional shadow, fill and hairline border.
        public static void FillCard(Graphics g, Rectangle r, int radius, bool hot)
        {
            Shadow(g, r, radius);
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            {
                using (var b = new SolidBrush(Card)) g.FillPath(b, path);
                using (var p = new Pen(hot ? Mix(Line, Accent, 0.6) : Navy ? Mix(Line, Card, 0.4) : Line)) g.DrawPath(p, path);
            }
            g.SmoothingMode = old;
        }
        public static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        static Color TB { get { return CustomAccent ?? ThemeColor(ThemeIndex); } }
        public static Font NoteFont = new Font("Segoe UI", 11f);
        static string iconFamily;
        static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();

        // ---------- Theme ----------
        public static bool Dark;
        public static Color Accent { get { return Dark ? Mix(TB, Color.White, 0.45) : Mix(TB, Color.Black, 0.08); } }
        public static Color AccentFill { get { return Mix(TB, Color.Black, 0.08); } }
        public static Color Bg { get { return Soft ? (Dark ? H("#121214") : H("#ECECEF")) : Navy ? (Dark ? H("#0C1120") : H("#EEF2F8")) : Nexus ? (Dark ? H("#0E1015") : H("#F5F6F9")) : Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
        public static Color Card { get { return Soft ? (Dark ? H("#1C1C20") : H("#FFFFFF")) : Navy ? (Dark ? H("#151C2E") : H("#FFFFFF")) : Nexus ? (Dark ? H("#171A21") : Color.White) : Dark ? Color.FromArgb(45, 45, 45) : Color.White; } }
        public static Color Field { get { return Soft ? (Dark ? H("#212126") : H("#FFFFFF")) : Navy ? (Dark ? H("#18203A") : H("#FFFFFF")) : Nexus ? (Dark ? H("#1B1E26") : Color.White) : Dark ? Color.FromArgb(40, 40, 40) : Color.White; } }
        public static Color Nav { get { return Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(235, 235, 235); } }
        // Nexus: white sidebar with dark text; classic: PicPick-style solid colour sidebar with white text.
        public static Color Sidebar { get { return Soft ? (Dark ? H("#0B0B0D") : H("#26262A")) : Navy ? (Dark ? H("#111936") : H("#1B2759")) : Nexus ? (Dark ? H("#13151B") : Color.White) : Dark ? Mix(TB, Color.Black, 0.5) : TB; } }
        public static Color SidebarSel { get { return Soft ? (Dark ? H("#26262C") : H("#3A3A40")) : Navy ? (Dark ? H("#22306A") : H("#2C3D86")) : Nexus ? (Dark ? H("#232733") : H("#F1F2F6")) : Dark ? Mix(TB, Color.Black, 0.7) : Mix(TB, Color.Black, 0.3); } }
        public static Color SidebarHover { get { return Soft ? (Dark ? H("#18181B") : H("#303035")) : Navy ? (Dark ? H("#1A2550") : H("#233270")) : Nexus ? (Dark ? H("#1B1E26") : H("#F7F8FA")) : Dark ? Mix(TB, Color.Black, 0.38) : Mix(TB, Color.White, 0.12); } }
        public static Color SidebarText { get { return Soft ? (Dark ? H("#ECECEF") : H("#F3F3F5")) : Navy ? (Dark ? H("#E3E8F7") : H("#E8EDFB")) : Nexus ? (Dark ? H("#E7E9EE") : H("#1F2430")) : Color.White; } }
        public static Color SidebarMuted { get { return Soft ? (Dark ? H("#85858F") : H("#9B9BA3")) : Navy ? (Dark ? H("#8D99C2") : H("#9EAAD3")) : Nexus ? (Dark ? H("#7D8496") : H("#8A909E")) : Color.FromArgb(205, 255, 255, 255); } }
        public static Color Header { get { return Nexus ? Text : Dark ? Mix(TB, Color.White, 0.5) : TB; } }
        public static Color Line { get { return Soft ? (Dark ? H("#2B2B31") : H("#E3E3E7")) : Navy ? (Dark ? H("#252D44") : H("#E5E9F2")) : Nexus ? (Dark ? H("#272B35") : H("#E7E9EE")) : Dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(222, 222, 222); } }
        public static Color Text { get { return Soft ? (Dark ? H("#EEEEF1") : H("#17171A")) : Navy ? (Dark ? H("#EDF0F7") : H("#101828")) : Nexus ? (Dark ? H("#ECEEF3") : H("#111827")) : Dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(30, 30, 30); } }
        public static Color Muted { get { return Soft ? (Dark ? H("#9A9AA4") : H("#6C6C75")) : Navy ? (Dark ? H("#97A0B8") : H("#667085")) : Nexus ? (Dark ? H("#959CAD") : H("#6B7280")) : Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(105, 105, 105); } }
        public static Color SelBg { get { return Nexus ? (Dark ? Mix(TB, Card, 0.80) : Mix(TB, Color.White, 0.9)) : Dark ? Color.FromArgb(38, 60, 88) : Color.FromArgb(224, 236, 250); } }
        public static Color HoverBg { get { return Soft ? (Dark ? H("#25252A") : H("#F2F2F4")) : Navy ? (Dark ? H("#1C2438") : H("#F2F5FB")) : Nexus ? (Dark ? H("#20242D") : H("#F3F4F7")) : Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(236, 236, 236); } }
        public static Color PressBg { get { return Soft ? (Dark ? H("#2E2E34") : H("#E7E7EA")) : Navy ? (Dark ? H("#242D45") : H("#E8EDF7")) : Nexus ? (Dark ? H("#2A2F3A") : H("#E9EBF0")) : Dark ? Color.FromArgb(65, 65, 65) : Color.FromArgb(225, 225, 225); } }
        public static Color AccentHover { get { return Mix(TB, Color.Black, 0.2); } }
        public static Color AccentPressed { get { return Mix(TB, Color.Black, 0.3); } }
        public static Color Success { get { return Dark ? Color.FromArgb(108, 203, 144) : Color.FromArgb(26, 127, 66); } }
        public static Color Danger { get { return Dark ? Color.FromArgb(255, 143, 143) : Color.FromArgb(196, 43, 48); } }
        public static Color Warning { get { return Dark ? Color.FromArgb(249, 202, 101) : Color.FromArgb(151, 98, 0); } }
        public static Color Track { get { return Soft ? (Dark ? H("#2A2A30") : H("#E8E8EC")) : Navy ? (Dark ? H("#232B42") : H("#EDF1F8")) : Nexus ? (Dark ? H("#2A2E38") : H("#EEF0F4")) : Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(225, 225, 225); } }
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
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, DarkComboFace> comboFaces =
            new System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, DarkComboFace>();

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
                        // The light selection field and arrow use the system paint path.
                        if (cb.DrawMode == DrawMode.Normal)
                        {
                            cb.DrawMode = DrawMode.OwnerDrawFixed;
                            cb.ItemHeight = cb.Font.Height + S(SpaceSmall);
                            cb.DrawItem += DrawCombo;
                        }
                        comboFaces.GetValue(cb, combo => new DarkComboFace(combo));
                    }
                }
                var check = c as CheckBox;
                if (check != null) { check.FlatStyle = FlatStyle.Flat; check.ForeColor = Text; check.FlatAppearance.BorderColor = Line; check.FlatAppearance.CheckedBackColor = SelBg; }
                var tree = c as TreeView;
                if (tree != null) { tree.BackColor = Card; tree.ForeColor = Text; tree.LineColor = Line; DarkScroll(tree); }
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
            string text = e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
            var r = e.Bounds; r.Inflate(-S(SpaceSmall), 0);
            var dir = IsRtl(text) ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, text, combo.Font, r, combo.Enabled ? Text : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | dir);
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        sealed class DarkComboFace : NativeWindow
        {
            readonly ComboBox combo;

            public DarkComboFace(ComboBox combo)
            {
                this.combo = combo;
                combo.HandleCreated += delegate { AssignHandle(combo.Handle); };
                combo.HandleDestroyed += delegate { ReleaseHandle(); };
                if (combo.IsHandleCreated) AssignHandle(combo.Handle);
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (!Dark || combo.DropDownStyle == ComboBoxStyle.Simple) return;
                bool print = message.Msg == 0x317 || message.Msg == 0x318;
                if (!print && message.Msg != 0xF) return;
                IntPtr window = Handle;
                IntPtr dc = print ? message.WParam : Native.GetWindowDC(window);
                if (dc == IntPtr.Zero) return;
                try
                {
                    using (var g = Graphics.FromHdc(dc)) DrawFace(g);
                }
                finally { if (!print) Native.ReleaseDC(window, dc); }
            }

            void DrawFace(Graphics g)
            {
                int width = combo.Width, height = combo.Height;
                if (width < 2 || height < 2) return;
                bool rtl = combo.RightToLeft == RightToLeft.Yes;
                int button = Math.Min(width, Math.Max(S(24), SystemInformation.VerticalScrollBarWidth));
                int buttonX = rtl ? 0 : width - button;
                int arrowX = buttonX + button / 2, mid = height / 2;
                Color background = combo.Enabled ? Field : Bg;
                Color foreground = combo.Enabled ? Text : Muted;
                using (var fill = new SolidBrush(background))
                using (var ink = new SolidBrush(foreground))
                using (var border = new Pen(combo.ContainsFocus ? Accent : Line))
                {
                    if (combo.DropDownStyle == ComboBoxStyle.DropDownList)
                    {
                        g.FillRectangle(fill, 0, 0, width, height);
                        string text = combo.Text;
                        var bounds = new Rectangle((rtl ? button : 0) + S(6), 0, Math.Max(0, width - button - S(12)), height);
                        var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
                        if (rtl || IsRtl(text)) flags |= TextFormatFlags.RightToLeft | TextFormatFlags.Right;
                        TextRenderer.DrawText(g, text, combo.Font, bounds, foreground, flags);
                    }
                    else g.FillRectangle(fill, buttonX, 0, button, height);
                    g.FillPolygon(ink, new[] { new Point(arrowX - S(4), mid - S(1)), new Point(arrowX + S(4), mid - S(1)), new Point(arrowX, mid + S(3)) });
                    g.DrawRectangle(border, 0, 0, width - 1, height - 1);
                }
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
            new[] { "pwsh", "" }, new[] { "claude", "" }, new[] { "launch", "" }, new[] { "terminal", "" }, new[] { "workspace", "" }, new[] { "apply", "" },
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
            var t = new FluentTextBox { SearchIcon = true };
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
            int d = Math.Max(1, (int)(rad * 2 * RadiusScale));
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
            // Collapse whitespace without Regex (this runs for every visible card on every paint).
            var sb = new System.Text.StringBuilder(Math.Min(s.Length, max + 4));
            bool space = false;
            foreach (char ch in s)
            {
                if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(ch);
                if (sb.Length > max) break;
            }
            s = sb.ToString();
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }

        // File name of the first path in a "files" clip (safe for empty text or invalid characters).
        public static string FirstFileName(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(no files)";
            var parts = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "(no files)";
            try { string n = Path.GetFileName(parts[0].TrimEnd('\\')); return string.IsNullOrEmpty(n) ? parts[0] : n; } catch { return parts[0]; }
        }

        // Returns an action that runs `a` once, `ms` after the last call (typing in a search box, bursts of store events).
        public static Action Debounce(Control owner, int ms, Action a)
        {
            var t = new Timer { Interval = ms };
            t.Tick += delegate { t.Stop(); if (!owner.IsDisposed) a(); };
            owner.Disposed += delegate { t.Dispose(); };
            return () => { t.Stop(); t.Start(); };
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
        // Layout asks for this on every resize step: measure the text only when text, font or padding change.
        string sizeKey; Size sizeCache; int textWKey = -1; string textWFor;
        public override Size GetPreferredSize(Size proposedSize)
        {
            string key = Text + "\u0001" + Font.GetHashCode() + "|" + GlyphSpace + "|" + Padding + "|" + MinimumSize;
            if (key == sizeKey) return sizeCache;
            var text = TextRenderer.MeasureText(Text, Font);
            sizeCache = new Size(Math.Max(MinimumSize.Width, text.Width + GlyphSpace + Padding.Horizontal + Ui.S(4)), Math.Max(MinimumSize.Height, Font.Height + Padding.Vertical + Ui.S(4)));
            sizeKey = key;
            return sizeCache;
        }
        int TextWidth() { string k = Text + "\u0001" + Font.GetHashCode(); if (k != textWFor) { textWKey = TextRenderer.MeasureText(Text, Font).Width; textWFor = k; } return textWKey; }
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
            var under = Parent == null ? Ui.Bg : Parent.BackColor;
            Color fill = !Enabled ? Ui.Mix(under, Ui.Text, 0.03)
                : Primary ? (pressed ? Ui.AccentPressed : hover ? Ui.AccentHover : Ui.AccentFill)
                : Ui.Mix(under, Ui.Text, pressed ? 0.15 : hover ? 0.10 : Ui.Dark ? 0.07 : 0.055);   // soft secondary button
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Ui.Round(r, Ui.S(Ui.Radius)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if ((Focused && ShowFocusCues) || defaultButton) using (var p = new Pen(Ui.Accent)) g.DrawPath(p, path);
            }
            var textRect = Rectangle.Inflate(ClientRectangle, -Ui.S(Ui.Space), -Ui.S(2));
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix; else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            var fore = !Enabled ? Ui.Muted : Primary ? Color.White : ForeColor;
            if (GlyphSpace > 0)
            {
                // Icon + text centred together.
                int textW = TextWidth();
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
        // Search boxes: a magnifier glyph and the same height as buttons and combos, text centred vertically.
        public bool SearchIcon;
        bool Tall { get { return SearchIcon && !Multiline; } }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int left = Tall ? Ui.S(26) : Ui.S(5);
            Native.SendMessage(Handle, 0xD3 /* EM_SETMARGINS */, (IntPtr)3, (IntPtr)((Ui.S(5) << 16) | left));
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (IsHandleCreated) OnHandleCreated(e); }
        void PaintGlyph(Graphics g)
        {
            var ir = new Rectangle(Ui.S(4), 0, Ui.S(18), ClientSize.Height);
            using (var b = new SolidBrush(BackColor)) g.FillRectangle(b, 0, 0, Ui.S(24), ClientSize.Height);
            TextRenderer.DrawText(g, "", Ui.Icon(9.5f), ir, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
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
            if (Tall && IsHandleCreated && (m.Msg == 0xF || m.Msg == 0x318 /* WM_PRINTCLIENT */))
            {
                if (m.Msg == 0x318) using (var g = Graphics.FromHdc(m.WParam)) PaintGlyph(g);
                else using (var g = Graphics.FromHwnd(Handle)) PaintGlyph(g);
            }
            if (BorderStyle == BorderStyle.None || !IsHandleCreated) return;
            bool printing = m.Msg == 0x317 || m.Msg == 0x318;
            if (printing || m.Msg == 0xF || m.Msg == 0x85 || m.Msg == 0x7 || m.Msg == 0x8)   // the themed edit redraws its frame during WM_PAINT too
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
        bool batchingSelection, selectionChanged;

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
            var keep = new HashSet<object>(Selected<object>());
            object top = TopIndex >= 0 && TopIndex < Items.Count ? Items[TopIndex] : null;
            var arr = items.ToArray();
            UpdateSelection(delegate
            {
                Items.Clear();
                Items.AddRange(arr);
                RestoreSelection(arr, keep);
                if (top != null) { int ti = Array.IndexOf(arr, top); if (ti >= 0) TopIndex = ti; }
                if (keep.Count > 0) selectionChanged = true;
            });
        }

        public void SelectAll()
        {
            if (Items.Count == 0 || !MultiSelect) return;
            UpdateSelection(delegate
            {
                if (IsHandleCreated)
                {
                    SetNativeSelected(-1, true);
                    SetSelected(0, true);
                }
                else for (int i = 0; i < Items.Count; i++) SetSelected(i, true);
            });
        }

        bool MultiSelect { get { return SelectionMode == SelectionMode.MultiExtended || SelectionMode == SelectionMode.MultiSimple; } }

        void SetNativeSelected(int index, bool selected)
        {
            Native.SendMessage(Handle, 0x185 /* LB_SETSEL */, selected ? (IntPtr)1 : IntPtr.Zero, (IntPtr)index);
        }

        void RestoreSelection(object[] items, HashSet<object> keep)
        {
            if (items.Length == 0 || keep.Count == 0 || SelectionMode == SelectionMode.None) return;
            if (MultiSelect && IsHandleCreated)
            {
                int retain = items.Count(keep.Contains);
                if (retain == 0) return;
                bool selectMost = retain > items.Length / 2;
                if (selectMost) SetNativeSelected(-1, true);
                for (int i = 0; i < items.Length; i++)
                {
                    bool selected = keep.Contains(items[i]);
                    if (selected != selectMost) SetNativeSelected(i, selected);
                }
                // One managed call tells WinForms to synchronize SelectedItems with the native state.
                SetSelected(0, keep.Contains(items[0]));
                return;
            }
            for (int i = 0; i < items.Length; i++)
            {
                if (!keep.Contains(items[i])) continue;
                if (SelectionMode == SelectionMode.One) { SelectedIndex = i; break; }
                SetSelected(i, true);
            }
        }

        void UpdateSelection(Action change)
        {
            BeginUpdate();
            batchingSelection = true;
            try { change(); }
            finally
            {
                batchingSelection = false;
                EndUpdate();
                if (selectionChanged) { selectionChanged = false; OnSelectedIndexChanged(EventArgs.Empty); }
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            if (batchingSelection) selectionChanged = true;
            else base.OnSelectedIndexChanged(e);
        }

        public List<T> Selected<T>()
        {
            if (!MultiSelect || !IsHandleCreated) return SelectedItems.Cast<object>().OfType<T>().ToList();
            int count = Native.SendMessage(Handle, 0x190 /* LB_GETSELCOUNT */, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (count <= 0) return new List<T>();
            var indices = new int[count];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(indices, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { count = Native.SendMessage(Handle, 0x191 /* LB_GETSELITEMS */, (IntPtr)count, pin.AddrOfPinnedObject()).ToInt32(); }
            finally { pin.Free(); }
            var selected = new List<T>(Math.Max(0, count));
            for (int i = 0; i < count; i++) if (Items[indices[i]] is T) selected.Add((T)Items[indices[i]]);
            return selected;
        }

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
            using (var path = Ui.Round(r, Ui.S(Ui.Nexus ? 10 : 6)))
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

        public string Badge;     // small pill on the right ("NEW", a count); null = none
        public NavItem(string glyph, string text)
        {
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTab;
            Glyph = glyph; Text = text;
            Height = Ui.Navy ? Ui.S(36) : Ui.Nexus ? Ui.S(40) : Ui.S(46);   // floating sidebars: all pages fit without scrolling
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
            if (Ui.Nexus) { PaintNexus(g); return; }
            // PicPick look: full-width darker band for the selected page on a solid blue sidebar.
            g.Clear(selected ? Ui.SidebarSel : hover ? Ui.SidebarHover : Ui.Sidebar);
            var r = new Rectangle(Ui.S(8), 0, Width - Ui.S(16), Height);
            if (selected) using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, 0, Ui.S(8), Ui.S(4), Height - Ui.S(16));
            if (Focused && !selected) using (var p = new Pen(Color.FromArgb(180, 255, 255, 255)) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, 2, 2, Width - 5, Height - 5);
            TextRenderer.DrawText(g, Glyph, Ui.Icon(13f), new Rectangle(r.X + Ui.S(6), r.Y, Ui.S(30), r.Height), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Width < Ui.S(100)) return;
            TextRenderer.DrawText(g, Text, selected ? Ui.Bold : Ui.Base, new Rectangle(r.X + Ui.S(46), r.Y, r.Width - Ui.S(46), r.Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // Nexus look: white sidebar, soft rounded pill for the selected / hovered page, grey icons, accent icon when selected.
        void PaintNexus(Graphics g)
        {
            g.Clear(Ui.Sidebar);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool narrow = Width < Ui.S(100);
            var pill = new Rectangle(Ui.S(Ui.Navy ? 6 : 10), Ui.S(2), Width - Ui.S(Ui.Navy ? 12 : 20), Height - Ui.S(4));
            if (selected || hover || Focused)
                using (var path = Ui.Round(pill, Ui.S(Ui.Navy ? 10 : 8)))
                {
                    using (var b = new SolidBrush(selected ? Ui.SidebarSel : Ui.SidebarHover)) g.FillPath(b, path);
                    if (selected && Ui.Navy)
                    {
                        var mark = Ui.Soft ? Ui.Mix(Ui.AccentFill, Color.White, 0.15) : Color.FromArgb(120, 160, 255);
                        using (var p = new Pen(Color.FromArgb(150, mark))) g.DrawPath(p, path);
                        using (var b = new SolidBrush(mark)) g.FillRectangle(b, pill.Right - Ui.S(3), pill.Y + Ui.S(8), Ui.S(3), pill.Height - Ui.S(16));
                    }
                    if (Focused && !selected) using (var p = new Pen(Ui.Navy ? Ui.SidebarMuted : Ui.Accent) { DashStyle = DashStyle.Dot }) g.DrawPath(p, path);
                }
            var iconRect = narrow ? new Rectangle(0, 0, Width, Height) : new Rectangle(pill.X + Ui.S(8), 0, Ui.S(26), Height);
            var iconColor = Ui.Navy ? (selected ? Color.White : Ui.SidebarMuted) : selected ? Ui.Accent : Ui.SidebarMuted;
            TextRenderer.DrawText(g, Glyph, Ui.Icon(12f), iconRect, iconColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            if (narrow) return;
            int tx = pill.X + Ui.S(38), right = pill.Right - Ui.S(6);
            if (!string.IsNullOrEmpty(Badge))
            {
                var size = TextRenderer.MeasureText(Badge, Ui.Small, Size.Empty, TextFormatFlags.NoPadding);
                var br = new Rectangle(right - size.Width - Ui.S(14), (Height - Ui.S(20)) / 2, size.Width + Ui.S(14), Ui.S(20));
                using (var path = Ui.Round(br, Ui.S(6)))
                using (var b = new SolidBrush(Ui.Navy ? Ui.AccentFill : Ui.SelBg)) g.FillPath(b, path);
                TextRenderer.DrawText(g, Badge, Ui.Small, br, Ui.Navy ? Color.White : Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                right = br.Left - Ui.S(6);
            }
            TextRenderer.DrawText(g, Text, selected ? Ui.Bold : Ui.Base, new Rectangle(tx, 0, Math.Max(1, right - tx), Height), Ui.Navy && !selected ? Ui.Mix(Ui.SidebarText, Ui.Sidebar, 0.12) : Ui.Navy ? Color.White : Ui.SidebarText, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Small upper-case group title in the sidebar ("GENERAL", "TOOLS"...); a thin line when the sidebar is narrow.
    public class NavGroup : Control
    {
        public NavGroup(string text)
        {
            Text = text.ToUpperInvariant(); Dock = DockStyle.Top; Height = Ui.S(34); TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Sidebar);
            if (Width < Ui.S(100)) { using (var b = new SolidBrush(Ui.Navy ? Ui.SidebarSel : Ui.Line)) g.FillRectangle(b, Ui.S(14), Height / 2, Width - Ui.S(28), 1); return; }
            TextRenderer.DrawText(g, Text, Ui.Small, new Rectangle(Ui.S(22), Ui.S(10), Width - Ui.S(30), Height - Ui.S(10)), Ui.SidebarMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
