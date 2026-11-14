using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StickyPro
{
    // PicPick-style graphic accessories. Each one is created only when opened and fully disposed on close.
    static class Accessories
    {
        // id, title, one-line description, glyph
        public static readonly string[][] List = {
            new[] { "picker",     "Color Picker",  "Pick any pixel on the screen and copy its code", "\uE790" },
            new[] { "palette",    "Color Palette", "Tune colors, keep swatches, copy HEX / RGB / HSL", "\uE771" },
            new[] { "magnifier",  "Magnifier",     "Zoom around the mouse pointer (2x - 8x)", "\uE71E" },
            new[] { "ruler",      "Pixel Ruler",   "Measure anything on the screen in pixels", "\uED5E" },
            new[] { "crosshair",  "CrossHair",     "Exact X, Y position and distances", "\uE81D" },
            new[] { "protractor", "Protractor",    "Measure the angle between two lines", "\uE7AD" },
            new[] { "whiteboard", "WhiteBoard",    "Draw on top of the screen", "\uE76D" },
        };

        static readonly Dictionary<string, Form> open = new Dictionary<string, Form>();

        public static void Open(string id)
        {
            Form existing;
            if (open.TryGetValue(id, out existing) && !existing.IsDisposed) { Native.EnsureRestored(existing); existing.Activate(); return; }
            Form f = null;
            try
            {
                switch (id)
                {
                    case "picker": f = new ColorPickerOverlay(); break;
                    case "palette": f = new ColorPaletteForm(Color.Empty); break;
                    case "magnifier": f = new MagnifierForm(); break;
                    case "ruler": f = new PixelRuler(); break;
                    case "crosshair": f = new CrossHairOverlay(); break;
                    case "protractor": f = new ProtractorOverlay(); break;
                    case "whiteboard": f = new WhiteBoardOverlay(); break;
                    case "record": App.I.StartRecord("rect"); return;
                    default: return;
                }
                open[id] = f;
                f.FormClosed += delegate { open.Remove(id); Ui.Later(800, App.I.Trim); };
                f.Show();
                f.Activate();
            }
            catch (Exception ex)
            {
                if (f != null) f.Dispose();
                App.Log("Accessory " + id + ": " + ex.Message);
                App.I.Balloon("Could not open the tool", ex.Message);
            }
        }

        public static void ShowPalette(Color c)
        {
            Form existing;
            if (open.TryGetValue("palette", out existing) && !existing.IsDisposed) { ((ColorPaletteForm)existing).SetColor(c); existing.Activate(); return; }
            var f = new ColorPaletteForm(c);
            open["palette"] = f;
            f.FormClosed += delegate { open.Remove("palette"); };
            f.Show(); f.Activate();
        }

        public static ToolStripMenuItem MenuItem()
        {
            var m = new ToolStripMenuItem("Graphic accessories");
            foreach (var a in List) { string id = a[0]; m.DropDownItems.Add(a[1], null, delegate { Open(id); }); }
            m.DropDownItems.Add("Screen Recorder", null, delegate { App.I.StartRecord("rect"); });
            return m;
        }

        // ---------- color helpers ----------
        public static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
        public static string Rgb(Color c) { return "rgb(" + c.R + ", " + c.G + ", " + c.B + ")"; }
        public static string Hsl(Color c) { double h, s, l; ToHsl(c, out h, out s, out l); return "hsl(" + Math.Round(h) + ", " + Math.Round(s * 100) + "%, " + Math.Round(l * 100) + "%)"; }
        public static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2; h = 0; s = 0;
            double d = max - min;
            if (d > 1e-9)
            {
                s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0); else if (max == g) h = (b - r) / d + 2; else h = (r - g) / d + 4;
                h *= 60;
            }
        }
        public static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360.0;
            if (s <= 0) { int v = Clamp(l * 255); return Color.FromArgb(v, v, v); }
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            return Color.FromArgb(Clamp(Hue(p, q, h + 1 / 3.0) * 255), Clamp(Hue(p, q, h) * 255), Clamp(Hue(p, q, h - 1 / 3.0) * 255));
        }
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        static int Clamp(double v) { return Math.Max(0, Math.Min(255, (int)Math.Round(v))); }
        public static bool TryParse(string text, out Color c)
        {
            c = Color.Empty;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().TrimStart('#');
            if (t.Length == 3) t = new string(new[] { t[0], t[0], t[1], t[1], t[2], t[2] });
            int v;
            if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out v)) { c = Color.FromArgb((v >> 16) & 255, (v >> 8) & 255, v & 255); return true; }
            return false;
        }
        public static void Remember(Color c)
        {
            var list = Store.Settings.ColorHistory;
            string h = Hex(c);
            list.Remove(h); list.Insert(0, h);
            while (list.Count > 24) list.RemoveAt(list.Count - 1);
            Store.MarkSettings();
        }
    }

    // ---------- Base for full-screen tools drawn over a frozen picture of the desktop ----------
    class FrozenOverlay : Form
    {
        protected readonly Bitmap Frozen;
        protected readonly Rectangle Virt;
        protected Point Mouse;
        public FrozenOverlay(bool dim)
        {
            Virt = Shot.Virtual;
            using (var raw = Shot.Capture(Virt))
            {
                // Premultiplied pixels draw several times faster, which keeps mouse tracking smooth on big screens.
                Frozen = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(Frozen))
                {
                    g.DrawImageUnscaled(raw, 0, 0);
                    if (dim) using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(b, 0, 0, Frozen.Width, Frozen.Height);
                }
            }
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; Bounds = Virt;
            TopMost = true; ShowInTaskbar = false; KeyPreview = true; DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            Cursor = Cursors.Cross; Font = Ui.Base;
        }
        protected Point ToScreen(Point p) { return new Point(p.X + Virt.X, p.Y + Virt.Y); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { Mouse = e.Location; Invalidate(); base.OnMouseMove(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImage(Frozen, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel); }
        protected static void Label(Graphics g, string text, Point at, Rectangle bounds)
        {
            var size = TextRenderer.MeasureText(text, Ui.Bold);
            var r = new Rectangle(at.X + Ui.S(16), at.Y + Ui.S(16), size.Width + Ui.S(10), size.Height + Ui.S(6));
            if (r.Right > bounds.Right) r.X = at.X - r.Width - Ui.S(16);
            if (r.Bottom > bounds.Bottom) r.Y = at.Y - r.Height - Ui.S(16);
            using (var b = new SolidBrush(Color.FromArgb(230, 20, 20, 20))) g.FillRectangle(b, r);
            TextRenderer.DrawText(g, text, Ui.Bold, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        protected override void Dispose(bool disposing) { if (disposing) Frozen.Dispose(); base.Dispose(disposing); }
    }

    // ---------- Color Picker ----------
    class ColorPickerOverlay : FrozenOverlay
    {
        public ColorPickerOverlay() : base(false) { Text = "Color Picker"; }
        Color At(Point p)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= Frozen.Width || p.Y >= Frozen.Height) return Color.Black;
            var c = Frozen.GetPixel(p.X, p.Y); return Color.FromArgb(c.R, c.G, c.B);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(Frozen, 0, 0);
            const int cells = 15; int z = Ui.S(9), size = cells * z;
            var loupe = new Rectangle(Mouse.X + Ui.S(24), Mouse.Y + Ui.S(24), size, size + Ui.S(46));
            if (loupe.Right > ClientSize.Width) loupe.X = Mouse.X - loupe.Width - Ui.S(24);
            if (loupe.Bottom > ClientSize.Height) loupe.Y = Mouse.Y - loupe.Height - Ui.S(24);
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.FillRectangle(Brushes.Black, loupe);
            g.DrawImage(Frozen, new Rectangle(loupe.X, loupe.Y, size, size), new Rectangle(Mouse.X - cells / 2, Mouse.Y - cells / 2, cells, cells), GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using (var p = new Pen(Color.White)) g.DrawRectangle(p, loupe.X + (cells / 2) * z, loupe.Y + (cells / 2) * z, z, z);
            using (var p = new Pen(Color.FromArgb(0, 150, 255), 2)) g.DrawRectangle(p, loupe);
            var c = At(Mouse);
            using (var b = new SolidBrush(c)) g.FillRectangle(b, loupe.X + Ui.S(6), loupe.Y + size + Ui.S(8), Ui.S(30), Ui.S(30));
            var sp = ToScreen(Mouse);
            TextRenderer.DrawText(g, Accessories.Hex(c) + "\n" + c.R + ", " + c.G + ", " + c.B + "   (" + sp.X + ", " + sp.Y + ")", Ui.Small,
                new Rectangle(loupe.X + Ui.S(42), loupe.Y + size + Ui.S(4), loupe.Width - Ui.S(44), Ui.S(40)), Color.White, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "Click to pick a color  \u00B7  Esc to cancel", Ui.Bold, new Point(Ui.S(16), Ui.S(12)), Color.White, Color.FromArgb(0, 103, 192));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var old = Mouse; Mouse = e.Location;
            int pad = Ui.S(260);
            Invalidate(new Rectangle(old.X - pad, old.Y - pad, pad * 2, pad * 2)); Invalidate(new Rectangle(Mouse.X - pad, Mouse.Y - pad, pad * 2, pad * 2));
            Invalidate(new Rectangle(0, 0, Ui.S(420), Ui.S(50)));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            var c = At(e.Location);
            Accessories.Remember(c);
            App.I.CopyText(Accessories.Hex(c));
            Close();
            Accessories.ShowPalette(c);
            App.I.Balloon("Color copied", Accessories.Hex(c) + "   " + Accessories.Rgb(c));
        }
    }

    // ---------- Color Palette ----------
    class ColorPaletteForm : Form
    {
        Color color;
        bool updating;
        readonly Panel preview;
        readonly TrackBar h, s, l, r, g, b;
        readonly Label hv, sv, lv, rv, gv, bv;
        readonly TextBox hex;
        readonly FlowLayoutPanel swatches, recent;

        public ColorPaletteForm(Color start)
        {
            AutoScaleMode = AutoScaleMode.None; Text = "Color Palette"; Icon = App.AppIcon; Font = Ui.Base; BackColor = Ui.Card; ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = Ui.S(460, 600); MinimumSize = Ui.S(320, 360); TopMost = true; KeyPreview = true;
            Ui.DarkTitle(this);
            var t = LayoutKit.Fields();
            preview = new Panel { Height = Ui.S(70), BorderStyle = BorderStyle.FixedSingle };
            LayoutKit.Add(t, preview, false);
            hex = new FluentTextBox { Font = Ui.Bold };
            hex.TextChanged += delegate { Color c; if (!updating && Accessories.TryParse(hex.Text, out c)) SetColor(c, false); };
            LayoutKit.Add(t, hex, false);
            LayoutKit.Add(t, LayoutKit.Actions(DockStyle.Fill,
                Ui.Btn("Copy HEX", delegate { App.I.CopyText(Accessories.Hex(color)); }, true),
                Ui.Btn("Copy RGB", delegate { App.I.CopyText(Accessories.Rgb(color)); }),
                Ui.Btn("Copy HSL", delegate { App.I.CopyText(Accessories.Hsl(color)); }),
                Ui.Btn("Pick from screen", delegate { Hide(); Ui.Later(200, () => { Close(); Accessories.Open("picker"); }); }),
                Ui.Btn("Save swatch", delegate { var list = Store.Settings.Swatches; string x = Accessories.Hex(color); if (!list.Contains(x)) { list.Add(x); while (list.Count > 48) list.RemoveAt(0); Store.MarkSettings(); FillSwatches(); } })), false);
            h = Slider(t, "Hue", 359, out hv); s = Slider(t, "Saturation", 100, out sv); l = Slider(t, "Lightness", 100, out lv);
            r = Slider(t, "Red", 255, out rv); g = Slider(t, "Green", 255, out gv); b = Slider(t, "Blue", 255, out bv);
            EventHandler fromHsl = delegate { if (!updating) SetColor(Accessories.FromHsl(h.Value, s.Value / 100.0, l.Value / 100.0), true); };
            EventHandler fromRgb = delegate { if (!updating) SetColor(Color.FromArgb(r.Value, g.Value, b.Value), false); };
            h.ValueChanged += fromHsl; s.ValueChanged += fromHsl; l.ValueChanged += fromHsl;
            r.ValueChanged += fromRgb; g.ValueChanged += fromRgb; b.ValueChanged += fromRgb;
            LayoutKit.Add(t, new Label { Text = "Saved swatches (right-click to remove)", AutoSize = true, Font = Ui.Bold }, false);
            swatches = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            LayoutKit.Add(t, swatches, false);
            LayoutKit.Add(t, new Label { Text = "Recent picks", AutoSize = true, Font = Ui.Bold }, false);
            recent = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            LayoutKit.Add(t, recent, false);
            Controls.Add(t);
            FillSwatches();
            Ui.Theme(this);
            SetColor(start.IsEmpty ? (Store.Settings.ColorHistory.Count > 0 && Accessories.TryParse(Store.Settings.ColorHistory[0], out start) ? start : Color.FromArgb(26, 110, 194)) : start, false);
        }

        TrackBar Slider(TableLayoutPanel t, string name, int max, out Label value)
        {
            var row = new TableLayoutPanel { ColumnCount = 3, AutoSize = false, Height = Ui.S(36) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(86)));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(44)));
            var bar = new TrackBar { Maximum = max, TickStyle = TickStyle.None, Dock = DockStyle.Fill, AutoSize = false, Height = Ui.S(30) };
            value = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight };
            row.Controls.Add(new Label { Text = name, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            row.Controls.Add(bar, 1, 0); row.Controls.Add(value, 2, 0);
            LayoutKit.Add(t, row, false);
            return bar;
        }

        public void SetColor(Color c) { SetColor(c, false); }
        void SetColor(Color c, bool keepHsl)
        {
            color = Color.FromArgb(c.R, c.G, c.B);
            updating = true;
            try
            {
                preview.BackColor = color;
                if (!hex.Focused) hex.Text = Accessories.Hex(color);
                r.Value = color.R; g.Value = color.G; b.Value = color.B;
                if (!keepHsl) { double hh, ss, ll; Accessories.ToHsl(color, out hh, out ss, out ll); h.Value = Math.Min(359, (int)Math.Round(hh)); s.Value = (int)Math.Round(ss * 100); l.Value = (int)Math.Round(ll * 100); }
                hv.Text = h.Value + "\u00B0"; sv.Text = s.Value + "%"; lv.Text = l.Value + "%"; rv.Text = r.Value.ToString(); gv.Text = g.Value.ToString(); bv.Text = b.Value.ToString();
                Text = "Color Palette - " + Accessories.Hex(color);
            }
            finally { updating = false; }
        }

        void FillSwatches()
        {
            Fill(swatches, Store.Settings.Swatches, true);
            Fill(recent, Store.Settings.ColorHistory, false);
        }
        void Fill(FlowLayoutPanel panel, List<string> codes, bool removable)
        {
            Ui.ClearControls(panel.Controls);
            foreach (var code in codes.ToList())
            {
                Color c; if (!Accessories.TryParse(code, out c)) continue;
                string x = code;
                var sw = new Panel { Size = Ui.S(28, 28), BackColor = c, Margin = new Padding(Ui.S(2)), BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand };
                var col = c;
                sw.MouseUp += (o, e) =>
                {
                    if (e.Button == MouseButtons.Right && removable) { Store.Settings.Swatches.Remove(x); Store.MarkSettings(); BeginInvoke(new Action(FillSwatches)); }
                    else if (e.Button == MouseButtons.Left) SetColor(col, false);
                };
                panel.Controls.Add(sw);
            }
            if (panel.Controls.Count == 0) panel.Controls.Add(new Label { Text = "None yet", AutoSize = true, ForeColor = Ui.Muted });
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) Close(); base.OnKeyDown(e); }
    }

    // ---------- Magnifier: follows the mouse, click-through, Esc closes, Ctrl+Plus / Ctrl+Minus zoom ----------
    class MagnifierForm : Form
    {
        readonly Timer timer = new Timer { Interval = 40 };
        int zoom = 3;
        Bitmap buffer;
        Point last = new Point(int.MinValue, 0);
        const int KeyEsc = 25001, KeyIn = 25002, KeyOut = 25003;
        public MagnifierForm()
        {
            Text = "Magnifier"; FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Size = Ui.S(320, 220); DoubleBuffered = true; BackColor = Color.Black;
            Opacity = 0.99; // makes the window layered so WS_EX_TRANSPARENT lets clicks pass through
            timer.Tick += delegate { Step(); };
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x20 | 0x80 | 0x08000000; return cp; } } // transparent, toolwindow, noactivate
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Shot.SetWindowDisplayAffinity(Handle, 0x11);
            Native.RegisterHotKey(Handle, KeyEsc, 0, (uint)Keys.Escape);
            Native.RegisterHotKey(Handle, KeyIn, 2, (uint)Keys.Oemplus);
            Native.RegisterHotKey(Handle, KeyOut, 2, (uint)Keys.OemMinus);
            timer.Start(); Step();
            App.I.Balloon("Magnifier", "Esc closes \u00B7 Ctrl + / Ctrl - change the zoom");
        }
        void Step()
        {
            var p = Cursor.Position;
            var wa = Screen.FromPoint(p).WorkingArea;
            int x = p.X + Ui.S(24), y = p.Y + Ui.S(24);
            if (x + Width > wa.Right) x = p.X - Width - Ui.S(24);
            if (y + Height > wa.Bottom) y = p.Y - Height - Ui.S(24);
            if (Left != x || Top != y) Location = new Point(x, y);
            if (p == last && buffer != null) return;
            last = p;
            int w = Math.Max(1, Width / zoom), h = Math.Max(1, Height / zoom);
            if (buffer == null || buffer.Width != w || buffer.Height != h) { if (buffer != null) buffer.Dispose(); buffer = new Bitmap(w, h, PixelFormat.Format32bppArgb); }
            try { using (var g = Graphics.FromImage(buffer)) g.CopyFromScreen(p.X - w / 2, p.Y - h / 2, 0, 0, buffer.Size, CopyPixelOperation.SourceCopy); } catch { }
            Invalidate();
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (buffer == null) { g.Clear(Color.Black); return; }
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(buffer, new Rectangle(0, 0, buffer.Width * zoom, buffer.Height * zoom));
            g.PixelOffsetMode = PixelOffsetMode.Default;
            int cx = (buffer.Width / 2) * zoom, cy = (buffer.Height / 2) * zoom;
            using (var p = new Pen(Color.FromArgb(160, 255, 0, 0))) { g.DrawLine(p, cx + zoom / 2, 0, cx + zoom / 2, Height); g.DrawLine(p, 0, cy + zoom / 2, Width, cy + zoom / 2); }
            using (var p = new Pen(Color.FromArgb(0, 150, 255), 2)) g.DrawRectangle(p, 1, 1, Width - 2, Height - 2);
            TextRenderer.DrawText(g, zoom + "x", Ui.Bold, new Point(Ui.S(6), Ui.S(4)), Color.White, Color.FromArgb(0, 103, 192));
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == KeyEsc) { Close(); return; }
                if (id == KeyIn) { zoom = Math.Min(8, zoom + 1); last = new Point(int.MinValue, 0); Step(); return; }
                if (id == KeyOut) { zoom = Math.Max(2, zoom - 1); last = new Point(int.MinValue, 0); Step(); return; }
            }
            base.WndProc(ref m);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            Native.UnregisterHotKey(Handle, KeyEsc); Native.UnregisterHotKey(Handle, KeyIn); Native.UnregisterHotKey(Handle, KeyOut);
            base.OnFormClosed(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); if (buffer != null) buffer.Dispose(); } base.Dispose(disposing); }
    }

    // ---------- Pixel Ruler: drag to move, drag the end to resize, double-click or right-click to rotate ----------
    class PixelRuler : Form
    {
        bool vertical;
        readonly Timer timer = new Timer { Interval = 50 };
        Point lastCursor;
        public PixelRuler()
        {
            Text = "Pixel Ruler"; FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; DoubleBuffered = true; KeyPreview = true; Opacity = 0.92; Font = Ui.Small;
            var p = Cursor.Position; var wa = Screen.FromPoint(p).WorkingArea;
            Bounds = new Rectangle(Math.Max(wa.Left, Math.Min(wa.Right - Ui.S(640), p.X - Ui.S(320))), Math.Max(wa.Top, Math.Min(wa.Bottom - Ui.S(56), p.Y + Ui.S(30))), Ui.S(640), Ui.S(56));
            MinimumSize = new Size(Ui.S(40), Ui.S(40));
            var menu = Ui.Menu();
            menu.Items.Add("Rotate (double-click)", null, delegate { Rotate(); });
            menu.Items.Add("Close (Esc)", null, delegate { Close(); });
            ContextMenuStrip = menu;
            timer.Tick += delegate { var c = Cursor.Position; if (c != lastCursor) { lastCursor = c; Invalidate(); } };
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); timer.Start(); }
        void Rotate()
        {
            vertical = !vertical;
            var c = new Point(Left + Width / 2, Top + Height / 2);
            Size = new Size(Height, Width);
            Location = new Point(c.X - Width / 2, c.Y - Height / 2);
        }
        protected override void OnDoubleClick(EventArgs e) { Rotate(); base.OnDoubleClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            int step = e.Shift ? 10 : 1;
            if (e.KeyCode == Keys.Left) Left -= step; else if (e.KeyCode == Keys.Right) Left += step; else if (e.KeyCode == Keys.Up) Top -= step; else if (e.KeyCode == Keys.Down) Top += step;
            base.OnKeyDown(e);
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x84) // WM_NCHITTEST: end grip resizes, the rest moves
            {
                long lp = m.LParam.ToInt64();
                var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int grip = Ui.S(10);
                if (!vertical && p.X >= Width - grip) m.Result = (IntPtr)11;       // HTRIGHT
                else if (vertical && p.Y >= Height - grip) m.Result = (IntPtr)15;  // HTBOTTOM
                else if ((int)m.Result == 1) m.Result = (IntPtr)2;                  // HTCAPTION
            }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.FromArgb(255, 236, 160));
            int length = vertical ? Height : Width, thick = vertical ? Width : Height;
            using (var pen = new Pen(Color.FromArgb(60, 50, 20)))
            {
                for (int i = 0; i < length; i += 2)
                {
                    int t = i % 50 == 0 ? thick / 2 : i % 10 == 0 ? thick / 3 : thick / 6;
                    if (vertical) g.DrawLine(pen, 0, i, t, i); else g.DrawLine(pen, i, 0, i, t);
                    if (i % 100 == 0 && i > 0)
                    {
                        if (vertical) TextRenderer.DrawText(g, i.ToString(), Ui.Small, new Point(thick / 2 + Ui.S(2), i - Ui.S(7)), Color.FromArgb(60, 50, 20));
                        else TextRenderer.DrawText(g, i.ToString(), Ui.Small, new Point(i + Ui.S(2), thick / 2), Color.FromArgb(60, 50, 20));
                    }
                }
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            var c = PointToClient(Cursor.Position);
            int pos = vertical ? c.Y : c.X;
            if (pos >= 0 && pos < length)
            {
                using (var red = new Pen(Color.Red)) { if (vertical) g.DrawLine(red, 0, pos, thick, pos); else g.DrawLine(red, pos, 0, pos, thick); }
                string label = pos + " px";
                if (vertical) TextRenderer.DrawText(g, label, Ui.Bold, new Point(Ui.S(2), Math.Min(length - Ui.S(18), pos + Ui.S(2))), Color.Red);
                else TextRenderer.DrawText(g, label, Ui.Bold, new Point(Math.Min(length - Ui.S(60), pos + Ui.S(4)), thick - Ui.S(20)), Color.Red);
            }
            string size = length + " px";
            if (vertical) TextRenderer.DrawText(g, size, Ui.Small, new Point(Ui.S(2), length - Ui.S(30)), Color.FromArgb(60, 50, 20));
            else TextRenderer.DrawText(g, size, Ui.Small, new Point(length - Ui.S(70), thick - Ui.S(20)), Color.FromArgb(60, 50, 20));
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); base.OnFormClosed(e); }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); if (ContextMenuStrip != null) ContextMenuStrip.Dispose(); } base.Dispose(disposing); }
    }

    // ---------- CrossHair: position, click to set a reference point, distance from it ----------
    class CrossHairOverlay : FrozenOverlay
    {
        Point? reference;
        public CrossHairOverlay() : base(false) { Text = "CrossHair"; Cursor = Cursors.Cross; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(Frozen, 0, 0);
            using (var p = new Pen(Color.FromArgb(200, 255, 0, 0))) { g.DrawLine(p, Mouse.X, 0, Mouse.X, Height); g.DrawLine(p, 0, Mouse.Y, Width, Mouse.Y); }
            var sp = ToScreen(Mouse);
            string text = "X " + sp.X + "   Y " + sp.Y;
            if (reference.HasValue)
            {
                var r = reference.Value;
                using (var p = new Pen(Color.FromArgb(0, 150, 255), 2) { DashStyle = DashStyle.Dash }) { g.DrawLine(p, r, Mouse); g.DrawRectangle(p, Math.Min(r.X, Mouse.X), Math.Min(r.Y, Mouse.Y), Math.Abs(Mouse.X - r.X), Math.Abs(Mouse.Y - r.Y)); }
                g.FillEllipse(Brushes.DeepSkyBlue, r.X - 4, r.Y - 4, 8, 8);
                int dx = Mouse.X - r.X, dy = Mouse.Y - r.Y;
                text += "\nW " + Math.Abs(dx) + "   H " + Math.Abs(dy) + "   distance " + Math.Sqrt(dx * dx + dy * dy).ToString("0.0");
            }
            Label(g, text, Mouse, ClientRectangle);
            TextRenderer.DrawText(g, "Click to set a reference point  \u00B7  click again to move it  \u00B7  Esc to close", Ui.Bold, new Point(Ui.S(16), Ui.S(12)), Color.White, Color.FromArgb(0, 103, 192));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { if (reference.HasValue) { reference = null; Invalidate(); } else Close(); return; }
            reference = e.Location; Invalidate();
        }
    }

    // ---------- Protractor: drag from the vertex along the first line, then move and click for the second ----------
    class ProtractorOverlay : FrozenOverlay
    {
        Point vertex, armA;
        int stage; // 0 waiting, 1 dragging first arm, 2 second arm follows mouse, 3 fixed
        Point armB;
        public ProtractorOverlay() : base(true) { Text = "Protractor"; }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { if (stage > 0) { stage = 0; Invalidate(); } else Close(); return; }
            if (stage == 0 || stage == 3) { vertex = armA = e.Location; stage = 1; }
            else if (stage == 2) { armB = e.Location; stage = 3; }
            Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (stage == 1) { armA = e.Location; stage = Distance(vertex, armA) < 5 ? 0 : 2; Invalidate(); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Mouse = e.Location;
            if (stage == 1) armA = e.Location; else if (stage == 2) armB = e.Location;
            Invalidate();
        }
        static double Distance(Point a, Point b) { int dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
        static double Angle(Point o, Point p) { return Math.Atan2(-(p.Y - o.Y), p.X - o.X) * 180 / Math.PI; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.DrawImageUnscaled(Frozen, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            string help = stage == 0 ? "Press at the corner and drag along the first line" : stage == 1 ? "Release at the end of the first line" : stage == 2 ? "Move to the second line and click" : "Click to measure again  \u00B7  right-click to clear  \u00B7  Esc to close";
            TextRenderer.DrawText(g, help, Ui.Bold, new Point(Ui.S(16), Ui.S(12)), Color.White, Color.FromArgb(0, 103, 192));
            if (stage == 0) return;
            using (var pen = new Pen(Color.FromArgb(255, 200, 0), 2))
            {
                g.DrawLine(pen, vertex, armA);
                if (stage >= 2) g.DrawLine(pen, vertex, armB);
            }
            g.FillEllipse(Brushes.Gold, vertex.X - 4, vertex.Y - 4, 8, 8);
            double a1 = Angle(vertex, armA);
            string text = "Line 1: " + Normal(a1).ToString("0.0") + "\u00B0";
            if (stage >= 2 && Distance(vertex, armB) > 3)
            {
                double a2 = Angle(vertex, armB);
                double between = Math.Abs(a2 - a1) % 360; if (between > 180) between = 360 - between;
                int radius = (int)Math.Max(20, Math.Min(80, Math.Min(Distance(vertex, armA), Distance(vertex, armB)) / 2));
                float start = (float)-a1, sweep = (float)(-(a2 - a1));
                while (sweep > 180) sweep -= 360; while (sweep < -180) sweep += 360;
                using (var arc = new Pen(Color.FromArgb(0, 180, 255), 2)) g.DrawArc(arc, vertex.X - radius, vertex.Y - radius, radius * 2, radius * 2, start, sweep);
                text = "Angle " + between.ToString("0.0") + "\u00B0   (" + (360 - between).ToString("0.0") + "\u00B0 outside)\nLine 1: " + Normal(a1).ToString("0.0") + "\u00B0   Line 2: " + Normal(a2).ToString("0.0") + "\u00B0";
            }
            Label(g, text, stage >= 2 ? armB : armA, ClientRectangle);
        }
        static double Normal(double a) { return (a + 360) % 360; }
    }

    // ---------- WhiteBoard: draw over a still picture of the screen ----------
    class WhiteBoardOverlay : FrozenOverlay
    {
        class Stroke { public List<Point> Pts = new List<Point>(); public Color Color; public float Width; public bool Marker; }
        readonly List<Stroke> strokes = new List<Stroke>();
        Stroke live;
        Color color = Color.Red;
        float width = 4;
        bool marker, blank;
        readonly FlowLayoutPanel bar;

        public WhiteBoardOverlay() : base(false)
        {
            Text = "WhiteBoard"; Cursor = Cursors.Cross;
            bar = new FlowLayoutPanel { AutoSize = true, BackColor = Color.FromArgb(40, 40, 40), Padding = new Padding(Ui.S(4)), WrapContents = true, MaximumSize = new Size(Screen.FromPoint(Cursor.Position).WorkingArea.Width - Ui.S(20), 0) };
            Add("Pen", delegate { marker = false; width = 4; });
            Add("Highlighter", delegate { marker = true; width = 18; });
            foreach (var c in new[] { Color.Red, Color.FromArgb(255, 200, 0), Color.FromArgb(40, 170, 70), Color.FromArgb(30, 120, 230), Color.Black, Color.White })
            {
                var col = c;
                var b = new Button { Size = Ui.S(28, 28), BackColor = c, FlatStyle = FlatStyle.Flat, Margin = new Padding(Ui.S(2)), Cursor = Cursors.Hand };
                b.Click += delegate { color = col; };
                bar.Controls.Add(b);
            }
            Add("Thin", delegate { width = marker ? 10 : 2; });
            Add("Thick", delegate { width = marker ? 26 : 8; });
            Add("Undo", delegate { if (strokes.Count > 0) { strokes.RemoveAt(strokes.Count - 1); Invalidate(); } });
            Add("Clear", delegate { strokes.Clear(); Invalidate(); });
            Add("Screen / Blank", delegate { blank = !blank; Invalidate(); });
            Add("Copy", delegate { using (var img = Render()) try { Clipboard.SetImage(img); App.I.Balloon("WhiteBoard", "Copied to the clipboard."); } catch { } });
            Add("Edit as snip", delegate { var img = Render(); Close(); var ed = new SnipEditor(img, null); ed.Show(); ed.Activate(); });
            Add("Close (Esc)", delegate { Close(); });
            Controls.Add(bar);
            Load += delegate
            {
                var pr = Screen.FromPoint(Cursor.Position).Bounds;
                bar.Location = new Point(pr.Left - Virt.Left + (pr.Width - bar.Width) / 2, pr.Top - Virt.Top + Ui.S(10));
            };
        }
        void Add(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(60, 60, 60), Font = Ui.Base, Margin = new Padding(Ui.S(2)), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 90);
            b.Click += click;
            bar.Controls.Add(b);
        }
        Bitmap Render()
        {
            var img = new Bitmap(Frozen.Width, Frozen.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(img)) Draw(g);
            return img;
        }
        void Draw(Graphics g)
        {
            if (blank) g.Clear(Color.White); else g.DrawImageUnscaled(Frozen, 0, 0);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var s in strokes) DrawStroke(g, s);
            if (live != null) DrawStroke(g, live);
        }
        static void DrawStroke(Graphics g, Stroke s)
        {
            if (s.Pts.Count == 0) return;
            using (var p = new Pen(s.Marker ? Color.FromArgb(110, s.Color) : s.Color, s.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (s.Pts.Count == 1) using (var b = new SolidBrush(p.Color)) g.FillEllipse(b, s.Pts[0].X - s.Width / 2, s.Pts[0].Y - s.Width / 2, s.Width, s.Width);
                else g.DrawLines(p, s.Pts.ToArray());
            }
        }
        protected override void OnPaint(PaintEventArgs e) { Draw(e.Graphics); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { if (strokes.Count > 0) { strokes.RemoveAt(strokes.Count - 1); Invalidate(); } return; }
            if (e.Button != MouseButtons.Left) return;
            live = new Stroke { Color = color, Width = width, Marker = marker };
            live.Pts.Add(e.Location); Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (live == null) return;
            var last = live.Pts[live.Pts.Count - 1];
            live.Pts.Add(e.Location);
            int pad = (int)width + 4;
            Invalidate(Rectangle.FromLTRB(Math.Min(last.X, e.X) - pad, Math.Min(last.Y, e.Y) - pad, Math.Max(last.X, e.X) + pad, Math.Max(last.Y, e.Y) + pad));
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (live == null) return;
            strokes.Add(live); live = null; Invalidate();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z) { if (strokes.Count > 0) { strokes.RemoveAt(strokes.Count - 1); Invalidate(); } e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
    }
}
