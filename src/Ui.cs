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

    static class Ui
    {
        public static float Scale = 1f;
        public static int S(int v) { return (int)Math.Round(v * Scale); }
        public static Size S(int w, int h) { return new Size(S(w), S(h)); }

        public static readonly Font Base = new Font("Segoe UI", 10f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 10f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 14f);
        public static Font NoteFont = new Font("Segoe UI", 11f);
        static string iconFamily;
        static readonly Dictionary<float, Font> iconFonts = new Dictionary<float, Font>();

        // ---------- Theme ----------
        public static bool Dark;
        public static Color Accent { get { return Dark ? Color.FromArgb(96, 170, 255) : Color.FromArgb(0, 103, 192); } }
        public static Color AccentFill { get { return Color.FromArgb(0, 103, 192); } }
        public static Color Bg { get { return Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }
        public static Color Card { get { return Dark ? Color.FromArgb(45, 45, 45) : Color.White; } }
        public static Color Field { get { return Dark ? Color.FromArgb(40, 40, 40) : Color.White; } }
        public static Color Nav { get { return Dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(235, 235, 235); } }
        public static Color Line { get { return Dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(222, 222, 222); } }
        public static Color Text { get { return Dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(30, 30, 30); } }
        public static Color Muted { get { return Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(105, 105, 105); } }
        public static Color SelBg { get { return Dark ? Color.FromArgb(38, 60, 88) : Color.FromArgb(224, 236, 250); } }
        public static Color HoverBg { get { return Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(236, 236, 236); } }
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

        public static void SetNoteFont(string name, float size)
        {
            try { NoteFont = new Font(name, size); }
            catch { NoteFont = new Font("Segoe UI", 11f); }
        }

        public static Font Icon(float size)
        {
            if (iconFamily == null)
            {
                iconFamily = "Segoe UI Symbol";
                foreach (var f in FontFamily.Families)
                {
                    if (f.Name == "Segoe Fluent Icons") { iconFamily = f.Name; break; }
                    if (f.Name == "Segoe MDL2 Assets") iconFamily = f.Name;
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

        // Applies theme colors to inputs (which do not inherit colors) and dark scrollbars / title bar.
        public static void Theme(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is TextBox || c is ComboBox || c is NumericUpDown || (c is ListBox && !(c is CardList)) || c is RichTextBox)
                {
                    c.BackColor = c is ListBox ? Bg : Field;
                    c.ForeColor = Text;
                    var cb = c as ComboBox;
                    if (cb != null) cb.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
                }
                if (Dark && (c is TextBox || c is ListBox || c is RichTextBox || c is ComboBox)) DarkScroll(c);
                if (c.HasChildren) Theme(c);
            }
        }

        public static void DarkScroll(Control c)
        {
            EventHandler set = delegate { try { Native.SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { } };
            if (c.IsHandleCreated) set(c, EventArgs.Empty); else c.HandleCreated += set;
        }

        public static void DarkTitle(Form f)
        {
            EventHandler set = delegate { try { int v = Dark ? 1 : 0; Native.DwmSetWindowAttribute(f.Handle, 20, ref v, 4); } catch { } };
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
        }

        public static ContextMenuStrip Menu()
        {
            var m = new ContextMenuStrip();
            m.Font = Base;
            ThemeMenu(m);
            return m;
        }

        public static void ThemeMenu(ToolStrip m)
        {
            m.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            m.BackColor = Card;
            m.ForeColor = Text;
            foreach (ToolStripItem i in m.Items)
            {
                i.ForeColor = Text;
                var mi = i as ToolStripMenuItem;
                if (mi != null && mi.HasDropDownItems) ThemeMenu(mi.DropDown);
            }
        }

        public static Button Btn(string text, EventHandler click, bool primary = false)
        {
            var b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = primary ? AccentFill : Line;
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 90, 170) : HoverBg;
            b.BackColor = primary ? AccentFill : Card;
            b.ForeColor = primary ? Color.White : Text;
            b.Font = Base;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(S(6), S(1), S(6), S(1));
            b.Margin = new Padding(0, 0, S(6), 0);
            b.Cursor = Cursors.Hand;
            b.UseVisualStyleBackColor = false;
            if (click != null) b.Click += click;
            return b;
        }

        public static TextBox Search(string cue)
        {
            var t = new TextBox();
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
            if (ticks <= 0) return "";
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
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float m = s * 0.06f;
                var r = new Rectangle((int)m, (int)m, (int)(s - 2 * m), (int)(s - 2 * m));
                using (var path = Round(r, (int)(s * 0.18f)))
                using (var br = new SolidBrush(Color.FromArgb(52, 52, 52)))
                    g.FillPath(br, path);
                float x0 = s * 0.42f, y0 = s * 0.42f, x1 = s * 0.80f, y1 = s * 0.80f, f = s * 0.13f;
                var poly = new[] { new PointF(x0, y0), new PointF(x1, y0), new PointF(x1, y1 - f), new PointF(x1 - f, y1), new PointF(x0, y1) };
                using (var br = new SolidBrush(Color.FromArgb(255, 213, 0))) g.FillPolygon(br, poly);
                var fold = new[] { new PointF(x1, y1 - f), new PointF(x1 - f, y1 - f), new PointF(x1 - f, y1) };
                using (var br = new SolidBrush(Color.FromArgb(200, 160, 0))) g.FillPolygon(br, fold);
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
                table.ColumnCount = 1;
                table.AutoScroll = true;
                var boxes = new List<TextBox>();
                foreach (var n in names)
                {
                    var l = new Label(); l.Text = n; l.AutoSize = true; l.Font = Bold; l.Margin = new Padding(0, S(6), 0, S(2));
                    var t = new TextBox(); t.Width = S(420); t.Font = Base;
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
                var l = new Label(); l.Text = label; l.AutoSize = true; l.Location = new Point(S(14), S(14));
                var t = new TextBox(); t.Text = value ?? ""; t.Width = S(360); t.Location = new Point(S(14), S(40));
                var ok = Btn("OK", null, true); ok.DialogResult = DialogResult.OK; ok.Location = new Point(S(220), S(80));
                var cancel = Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(S(300), S(80));
                f.Controls.Add(l); f.Controls.Add(t); f.Controls.Add(ok); f.Controls.Add(cancel);
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
                var l = new Label(); l.Text = "When should this note pop up?"; l.AutoSize = true; l.Location = new Point(S(14), S(14));
                var dt = new DateTimePicker();
                dt.Format = DateTimePickerFormat.Custom;
                dt.CustomFormat = "ddd d MMM yyyy   HH:mm";
                dt.Width = S(390); dt.Location = new Point(S(14), S(42));
                dt.Value = current > DateTime.Now.Ticks ? new DateTime(current) : DateTime.Now.AddHours(1);
                long result = -1;
                var quick = new FlowLayoutPanel(); quick.Location = new Point(S(14), S(82)); quick.Size = new Size(S(400), S(36));
                Action<DateTime> pick = d => { result = d.Ticks; f.DialogResult = DialogResult.OK; };
                quick.Controls.Add(Btn("In 15 min", delegate { pick(DateTime.Now.AddMinutes(15)); }));
                quick.Controls.Add(Btn("In 1 hour", delegate { pick(DateTime.Now.AddHours(1)); }));
                quick.Controls.Add(Btn("Tomorrow 9:00", delegate { pick(DateTime.Today.AddDays(1).AddHours(9)); }));
                var bar = new FlowLayoutPanel(); bar.FlowDirection = FlowDirection.RightToLeft; bar.Location = new Point(S(14), S(138)); bar.Size = new Size(S(392), S(40));
                var ok = Btn("Set reminder", delegate { result = dt.Value.Ticks; f.DialogResult = DialogResult.OK; }, true);
                var cancel = Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                bar.Controls.Add(ok); bar.Controls.Add(cancel);
                if (current > 0) bar.Controls.Add(Btn("Remove", delegate { result = 0; f.DialogResult = DialogResult.OK; }));
                f.Controls.Add(l); f.Controls.Add(dt); f.Controls.Add(quick); f.Controls.Add(bar);
                f.CancelButton = cancel;
                Theme(f);
                return f.ShowDialog(owner) == DialogResult.OK ? result : -1;
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
            using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds);
            CardInfo ci;
            try { ci = Info(Items[e.Index]); } catch { return; }
            bool sel = (e.State & DrawItemState.Selected) != 0;
            var r = new Rectangle(e.Bounds.X + Ui.S(6), e.Bounds.Y + Ui.S(3), e.Bounds.Width - Ui.S(13), e.Bounds.Height - Ui.S(6));
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

        protected override void OnPaint(PaintEventArgs e)
        {
            if (hover) using (var b = new SolidBrush(HoverColor)) e.Graphics.FillRectangle(b, ClientRectangle);
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

    // Left navigation item in the main window.
    public class NavItem : Control
    {
        public string Glyph;
        bool selected, hover;
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }

        public NavItem(string glyph, string text)
        {
            Glyph = glyph; Text = text;
            Height = Ui.S(42);
            Dock = DockStyle.Top;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Nav);
            var r = new Rectangle(Ui.S(8), Ui.S(3), Width - Ui.S(16), Height - Ui.S(6));
            if (selected || hover)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Ui.Round(r, Ui.S(6)))
                using (var b = new SolidBrush(selected ? Ui.SelBg : Ui.HoverBg))
                    g.FillPath(b, path);
                if (selected) using (var b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, r.X, r.Y + Ui.S(10), Ui.S(3), r.Height - Ui.S(20));
            }
            TextRenderer.DrawText(g, Glyph, Ui.Icon(12f), new Rectangle(r.X + Ui.S(8), r.Y, Ui.S(28), r.Height), Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Text, selected ? Ui.Bold : Ui.Base, new Rectangle(r.X + Ui.S(44), r.Y, r.Width - Ui.S(44), r.Height), Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
