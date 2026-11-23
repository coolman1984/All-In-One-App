using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // One-click look switcher (top bar theme button): every design in light and dark as a live mini preview,
    // plus the accent colours. A click applies design + colour + light/dark in one rebuild.
    class LookPicker : Form
    {
        class Preset { public string Design, Name; public int Theme; public bool Dark; public Rectangle R; }
        static readonly string[][] Designs = { new[] { "flow", "Flow" }, new[] { "atelier", "Atelier" }, new[] { "nordic", "Nordic" }, new[] { "soft", "Soft" }, new[] { "serq", "SerQ" }, new[] { "nexus", "Nexus" }, new[] { "classic", "Classic" } };
        readonly List<Preset> presets = new List<Preset>();
        readonly List<KeyValuePair<int, Rectangle>> swatches = new List<KeyValuePair<int, Rectangle>>();
        object hot;
        static LookPicker current;

        public static void Open(Control from, Point screen)
        {
            if (current != null && !current.IsDisposed) { current.Close(); return; }
            var area = Screen.FromPoint(screen).WorkingArea;
            current = new LookPicker();
            current.LayoutPresets(area.Size);
            current.Location = new Point(Math.Max(area.Left, Math.Min(area.Right - current.Width, screen.X - current.Width / 2)), Math.Max(area.Top, Math.Min(area.Bottom - current.Height, screen.Y + Ui.S(6))));
            current.Show(from.FindForm());
        }

        LookPicker()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            DoubleBuffered = true; BackColor = Ui.Card; KeyPreview = true;
            AutoScroll = true;
            LayoutPresets(Screen.FromPoint(Cursor.Position).WorkingArea.Size);
            Scroll += delegate { Invalidate(); };
            var tip = new ToolTip(); Disposed += delegate { tip.Dispose(); };
            MouseMove += (s, e) =>
            {
                var at = ContentPoint(e.Location);
                object h = presets.FirstOrDefault(p => PresetHit(p, at));
                if (h == null) { var sw = swatches.FirstOrDefault(x => x.Value.Contains(at)); if (sw.Value.Width > 0) h = sw.Key; }
                if (!Equals(h, hot)) { hot = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; tip.SetToolTip(this, h is int ? Ui.ThemeNames[(int)h] : ""); Invalidate(); }
            };
            MouseLeave += delegate { hot = null; Invalidate(); };
            MouseClick += (s, e) =>
            {
                var at = ContentPoint(e.Location);
                var p = presets.FirstOrDefault(x => PresetHit(x, at));
                var sw = swatches.FirstOrDefault(x => x.Value.Contains(at));
                if (p == null && sw.Value.Width == 0) return;
                Close();
                if (p != null) App.I.SetLook(p.Design, p.Theme, p.Dark);
                else App.I.SetLook(Store.Settings.Design, sw.Key, Store.Settings.Dark);
            };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Deactivate += delegate { if (!IsDisposed) Close(); };
            FormClosed += delegate { current = null; };
        }

        void LayoutPresets(Size available)
        {
            presets.Clear();
            swatches.Clear();
            int width = Ui.S(136), height = Ui.S(94), gap = Ui.S(10), pad = Ui.S(16), top = Ui.S(44);
            int maxWidth = available.Width - SystemInformation.VerticalScrollBarWidth;
            int columns = Math.Max(1, Math.Min(Designs.Length, (maxWidth - pad * 2 + gap) / (width + gap)));
            int gridWidth = pad * 2 + columns * width + (columns - 1) * gap;
            int rowHeight = height + Ui.S(32);
            for (int d = 0; d < Designs.Length; d++)
                for (int dark = 0; dark < 2; dark++)
                {
                    string key = Designs[d][0];
                    presets.Add(new Preset
                    {
                        Design = key, Name = Designs[d][1] + (dark == 1 ? " dark" : " light"), Dark = dark == 1,
                        Theme = Ui.DefaultThemeFor(key),
                        R = new Rectangle(pad + (d % columns) * (width + gap), top + (d / columns * 2 + dark) * rowHeight, width, height)
                    });
                }
            int swatchTop = top + ((Designs.Length + columns - 1) / columns) * 2 * rowHeight + Ui.S(25);
            int swatchSize = Ui.S(26), swatchStep = swatchSize + gap;
            int swatchColumns = Math.Max(1, (gridWidth - pad * 2 + gap) / swatchStep);
            for (int i = 0; i < Ui.ThemeNames.Length; i++)
                swatches.Add(new KeyValuePair<int, Rectangle>(i,
                    new Rectangle(pad + i % swatchColumns * swatchStep, swatchTop + i / swatchColumns * swatchStep, swatchSize, swatchSize)));
            int contentHeight = swatches.Last().Value.Bottom + pad;
            ClientSize = new Size(Math.Min(available.Width, gridWidth + (contentHeight > available.Height ? SystemInformation.VerticalScrollBarWidth : 0)),
                Math.Min(available.Height, contentHeight));
            AutoScrollMinSize = new Size(gridWidth, contentHeight);
        }

        Point ContentPoint(Point point) { return new Point(point.X - AutoScrollPosition.X, point.Y - AutoScrollPosition.Y); }
        static bool PresetHit(Preset preset, Point point)
        {
            return Rectangle.FromLTRB(preset.R.Left, preset.R.Top, preset.R.Right, preset.R.Bottom + Ui.S(27)).Contains(point);
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; } }

        // The colours of one design, read by switching the flags for a moment (painting is on the UI thread).
        struct Pal { public Color Bg, Card, Side, SideSel, SideText, Text, Muted, Line, Accent; }
        static Pal Read(string design, int theme, bool dark)
        {
            bool d0 = Ui.Dark, n0 = Ui.Nexus, v0 = Ui.Navy, s0 = Ui.Soft, f0 = Ui.Flow, at0 = Ui.Atelier, no0 = Ui.Nordic; int t0 = Ui.ThemeIndex; var a0 = Ui.CustomAccent;
            try
            {
                Ui.Dark = dark; Ui.SetDesignFlags(design); Ui.ThemeIndex = theme; Ui.CustomAccent = null;
                return new Pal { Bg = Ui.Bg, Card = Ui.Card, Side = Ui.Sidebar, SideSel = Ui.SidebarSel, SideText = Ui.SidebarMuted, Text = Ui.Text, Muted = Ui.Muted, Line = Ui.Line, Accent = Ui.AccentFill };
            }
            finally { Ui.Dark = d0; Ui.Nexus = n0; Ui.Navy = v0; Ui.Soft = s0; Ui.Flow = f0; Ui.Atelier = at0; Ui.Nordic = no0; Ui.ThemeIndex = t0; Ui.CustomAccent = a0; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Ui.Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            var scroll = AutoScrollPosition;
            g.TranslateTransform(scroll.X, scroll.Y);
            TextRenderer.DrawText(g, "Appearance", Ui.Bold, new Point(Ui.S(16), Ui.S(14)), Ui.Text,
                TextFormatFlags.PreserveGraphicsTranslateTransform);
            foreach (var p in presets)
            {
                var c = Read(p.Design, p.Theme, p.Dark);
                bool now = Store.Settings.Design == p.Design && Store.Settings.Dark == p.Dark;
                var r = p.R;
                using (var path = Ui.Round(r, Ui.S(10)))
                {
                    using (var b = new SolidBrush(c.Bg)) g.FillPath(b, path);
                    using (var old = g.Clip)
                    {
                        g.SetClip(path);
                        // sidebar, card, accent pill, text lines
                        bool flowLike = p.Design == "flow" || p.Design == "atelier" || p.Design == "nordic";
                        var side = new Rectangle(r.X + Ui.S(6), r.Y + Ui.S(6), Ui.S(28), r.Height - Ui.S(12));
                        using (var sp = Ui.Round(side, Ui.S(6)))
                        {
                            using (var b = new SolidBrush(c.Side)) g.FillPath(b, sp);
                            if (flowLike) using (var pen = new Pen(c.Line)) g.DrawPath(pen, sp);
                        }
                        if (flowLike)
                        {
                            using (var bp = Ui.Round(new Rectangle(side.X + Ui.S(4), side.Y + Ui.S(7), side.Width - Ui.S(8), Ui.S(12)), Ui.S(4)))
                            using (var b = new SolidBrush(c.SideSel)) g.FillPath(b, bp);
                            using (var b = new SolidBrush(c.Accent)) g.FillEllipse(b, side.X + Ui.S(10), side.Y + Ui.S(9), Ui.S(8), Ui.S(8));
                            for (int i = 1; i < 4; i++) using (var b = new SolidBrush(c.SideText)) g.FillEllipse(b, side.X + Ui.S(10), side.Y + Ui.S(10) + i * Ui.S(14), Ui.S(8), Ui.S(8));
                        }
                        else
                        {
                            for (int i = 0; i < 4; i++) using (var b = new SolidBrush(c.SideText)) g.FillEllipse(b, side.X + Ui.S(10), side.Y + Ui.S(10) + i * Ui.S(14), Ui.S(8), Ui.S(8));
                        }
                        var card = new Rectangle(side.Right + Ui.S(8), r.Y + Ui.S(10), r.Right - side.Right - Ui.S(16), Ui.S(46));
                        using (var cp = Ui.Round(card, Ui.S(6))) { using (var b = new SolidBrush(c.Card)) g.FillPath(b, cp); using (var pen = new Pen(c.Line)) g.DrawPath(pen, cp); }
                        using (var b = new SolidBrush(c.Text)) g.FillRectangle(b, card.X + Ui.S(8), card.Y + Ui.S(9), card.Width / 2, Ui.S(5));
                        using (var b = new SolidBrush(c.Muted)) g.FillRectangle(b, card.X + Ui.S(8), card.Y + Ui.S(20), card.Width * 2 / 3, Ui.S(4));
                        using (var ap = Ui.Round(new Rectangle(card.X + Ui.S(8), card.Bottom - Ui.S(15), Ui.S(34), Ui.S(10)), Ui.S(5))) using (var b = new SolidBrush(c.Accent)) g.FillPath(b, ap);
                        if (flowLike)
                        {
                            Color dot1 = p.Design == "atelier" ? Color.FromArgb(31, 94, 91) : p.Design == "nordic" ? Color.FromArgb(43, 108, 176) : Color.FromArgb(34, 197, 139);
                            Color dot2 = p.Design == "atelier" ? Color.FromArgb(200, 125, 18) : Color.FromArgb(245, 158, 45);
                            using (var b = new SolidBrush(dot1)) g.FillEllipse(b, card.Right - Ui.S(28), card.Bottom - Ui.S(15), Ui.S(9), Ui.S(9));
                            using (var b = new SolidBrush(dot2)) g.FillEllipse(b, card.Right - Ui.S(16), card.Bottom - Ui.S(15), Ui.S(9), Ui.S(9));
                        }
                        using (var b = new SolidBrush(c.Card)) g.FillRectangle(b, card.X, card.Bottom + Ui.S(6), card.Width / 2 - Ui.S(3), r.Bottom - card.Bottom - Ui.S(12));
                        using (var b = new SolidBrush(c.Card)) g.FillRectangle(b, card.X + card.Width / 2 + Ui.S(3), card.Bottom + Ui.S(6), card.Width / 2 - Ui.S(3), r.Bottom - card.Bottom - Ui.S(12));
                        g.Clip = old;
                    }
                    using (var pen = new Pen(now ? Ui.Accent : ReferenceEquals(hot, p) ? Ui.Mix(Ui.Line, Ui.Accent, 0.6) : Ui.Line, now ? Ui.S(2) : 1)) g.DrawPath(pen, path);
                }
                TextRenderer.DrawText(g, p.Name, now ? Ui.Bold : Ui.Base,
                    new Rectangle(r.X, r.Bottom + Ui.S(6), r.Width, Ui.S(22)), now ? Ui.Accent : Ui.Text,
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsTranslateTransform);
            }
            if (swatches.Count > 0) TextRenderer.DrawText(g, "Accent colour", Ui.Small, new Point(swatches[0].Value.X, swatches[0].Value.Y - Ui.S(20)), Ui.Muted, TextFormatFlags.PreserveGraphicsTranslateTransform);
            foreach (var sw in swatches)
            {
                bool now = Store.Settings.Theme == sw.Key && string.IsNullOrEmpty(Store.Settings.AccentColor);
                var r = sw.Value;
                using (var b = new SolidBrush(Ui.ThemeColor(sw.Key))) g.FillEllipse(b, r);
                if (now || Equals(hot, sw.Key)) using (var pen = new Pen(Ui.Text, Ui.S(2))) g.DrawEllipse(pen, Rectangle.Inflate(r, Ui.S(3), Ui.S(3)));
            }
        }
    }
}
