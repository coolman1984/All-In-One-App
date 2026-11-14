using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Full-width top bar: brand, colourful compact dashboard chips (click = go to the page), quick toggles and clock.
    // One owner-drawn control; works only while the main window is on screen.
    class TopBar : LivePanel
    {
        class Chip
        {
            public string Key, Glyph, Label, Value, Tip, Page;
            public Color Tint;
            public double Bar = -1;          // 0..1 usage bar at the pill bottom
            public double[] Spark;
            public Rectangle Bounds;
            public bool Visible;
        }
        class Toggle { public string Key; public Rectangle Bounds; public bool Visible = true; }

        readonly Func<int> brandWidth;
        readonly List<Chip> chips = new List<Chip>();
        readonly List<Toggle> toggles = new List<Toggle>();
        readonly ToolTip tip = new ToolTip();
        readonly Timer clock = new Timer { Interval = 1000 };
        readonly Action update, redraw;
        Bitmap logo;
        Rectangle brandRect, clockRect, moreRect;
        bool compact;
        List<Chip> hidden = new List<Chip>();
        object hover;
        int lastMinute = -1;
        string tipFor;

        public TopBar(Func<int> brandWidth)
        {
            this.brandWidth = brandWidth;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top; Height = Ui.S(58); BackColor = Ui.Sidebar; ForeColor = Color.White;
            Add("cpu", "", "CPU", "#3B82F6", "system:procs");
            Add("ram", "", "RAM", "#8B5CF6", "system:procs");
            Add("c", "", "C:", "#14B8A6", "system:drives");
            Add("d", "", "D:", "#06B6D4", "system:drives");
            Add("apps", "", "Top app", "#F97316", "system:procs");
            Add("tasks", "", "Due today", "#64748B", "tasks");
            Add("clips", "", "Clips", "#22C55E", "clips");
            Add("prompts", "", "Prompts", "#6366F1", "prompts");
            Add("files", "", "Files", "#0EA5E9", "activity");
            Add("urgent", "", "Urgent", "#64748B", "tagged");
            Add("rec", "", "Recorder", "#64748B", "snip");
            foreach (var k in new[] { "theme", "awake", "wifi" }) toggles.Add(new Toggle { Key = k });
            clock.Tick += delegate { if (DateTime.Now.Minute != lastMinute) { lastMinute = DateTime.Now.Minute; Invalidate(clockRect); } };
            update = () => { if (Live) RefreshValues(); };
            redraw = () => { if (!IsDisposed) Invalidate(); };
            ProcessSampler.Updated += update; Store.ClipsChanged += update; Store.PromptsChanged += update; Store.NotesChanged += update;
            Store.ActivityChanged += update; Store.TaggedChanged += update; Store.TasksChanged += update; Recorder.Finished += update;
            Awake.Changed += redraw; WiFi.Changed += redraw;
            Cursor = Cursors.Default;
        }

        void Add(string key, string glyph, string label, string hex, string page)
        {
            chips.Add(new Chip { Key = key, Glyph = glyph, Label = label, Tint = ColorTranslator.FromHtml(hex), Page = page, Value = "-" });
        }
        Chip C(string key) { return chips.First(c => c.Key == key); }

        protected override void OnLive(bool on)
        {
            ProcessSampler.Watch(this, on);
            if (on) { clock.Start(); RefreshValues(); if (WiFi.State == "?") WiFi.Refresh(); }
            else clock.Stop();
        }

        static readonly Color Red = Color.FromArgb(239, 68, 68), Amber = Color.FromArgb(245, 158, 11), Slate = Color.FromArgb(100, 116, 139);

        void RefreshValues()
        {
            var facts = DashboardFacts.Read(Store.Now());
            var cpu = C("cpu"); cpu.Value = ProcessSampler.CpuTotal.ToString("0") + "%"; cpu.Spark = ProcessSampler.CpuHistory.ToArray();
            cpu.Tint = ProcessSampler.CpuTotal > 85 ? Red : ColorTranslator.FromHtml("#3B82F6");
            var ram = C("ram"); ram.Value = ProcessSampler.MemLoad + "%"; ram.Bar = ProcessSampler.MemLoad / 100.0;
            ram.Tint = ProcessSampler.MemLoad > 85 ? Red : ColorTranslator.FromHtml("#8B5CF6");
            ram.Tip = "RAM " + ProcessSampler.MemLoad + "% used (" + Fmt.Bytes((long)(ProcessSampler.MemTotal - ProcessSampler.MemAvail)) + " of " + Fmt.Bytes((long)ProcessSampler.MemTotal) + ")";
            foreach (var key in new[] { "c", "d" })
            {
                var d = DriveSnapshot.Read(key.ToUpperInvariant()); var chip = C(key);
                chip.Visible = d.Used >= 0;
                chip.Value = d.Value.Replace(" free", "") + " free"; chip.Bar = d.Used; chip.Tip = key.ToUpperInvariant() + ": " + d.Value + " " + d.Detail;
                chip.Tint = d.Used > 0.95 ? Red : d.Used > 0.9 ? Amber : ColorTranslator.FromHtml(key == "c" ? "#14B8A6" : "#06B6D4");
            }
            var top = ProcessSampler.Last.GroupBy(p => p.Display).Select(g => new { Name = g.Key, Memory = g.Sum(p => p.Mem) }).OrderByDescending(g => g.Memory).Take(3).ToList();
            var apps = C("apps");
            apps.Value = top.Count > 0 ? Ui.OneLine(top[0].Name, 14) + " " + Fmt.Bytes(top[0].Memory) : "-";
            apps.Tip = "Top memory apps:\n" + string.Join("\n", top.Select(p => p.Name + "  " + Fmt.Bytes(p.Memory)));
            var tasks = C("tasks"); tasks.Value = facts.RemindersToday.ToString(); tasks.Tint = facts.RemindersToday > 0 ? Red : Slate;
            tasks.Tip = "Due today / overdue tasks and reminders: " + facts.RemindersToday;
            C("clips").Value = facts.ClipsToday + " today";
            var pr = C("prompts"); pr.Value = facts.PromptCount.ToString(); pr.Tip = "Prompts: " + facts.PromptCount + "\nMost used: " + facts.MostUsed;
            C("files").Value = facts.FilesToday + " today";
            var urgent = C("urgent"); urgent.Value = facts.Urgent.ToString(); urgent.Tint = facts.Urgent > 0 ? Red : Slate;
            var rec = C("rec"); rec.Value = facts.Recording; rec.Tint = facts.Recording == "Recording" ? Red : Slate;
            foreach (var c in chips) { if (c.Key != "c" && c.Key != "d") c.Visible = true; if (c.Tip == null || c.Key == "clips" || c.Key == "files" || c.Key == "urgent" || c.Key == "rec" || c.Key == "cpu") c.Tip = c.Label + ": " + c.Value; }
            LayoutBar();
            Invalidate();
        }

        // ---------- Layout ----------
        int ChipH { get { return Ui.S(38); } }
        int NaturalWidth(Chip c, bool small)
        {
            int text = TextRenderer.MeasureText(c.Value, Ui.Bold, Size.Empty, TextFormatFlags.NoPadding).Width;
            if (!small) text = Math.Max(text, TextRenderer.MeasureText(c.Label, Ui.Small, Size.Empty, TextFormatFlags.NoPadding).Width);
            return Ui.S(6) + Ui.S(26) + Ui.S(8) + text + Ui.S(14);
        }

        void LayoutBar()
        {
            int h = Height, bw = Math.Max(Ui.S(56), brandWidth());
            brandRect = new Rectangle(0, 0, bw, h);
            int right = Width - Ui.S(10);
            int clockW = Math.Max(TextRenderer.MeasureText("00:00", Ui.Bold).Width, TextRenderer.MeasureText(DateTime.Now.ToString("ddd d MMM"), Ui.Small).Width) + Ui.S(12);
            clockRect = new Rectangle(right - clockW, 0, clockW, h); right = clockRect.Left - Ui.S(6);
            int d = Ui.S(36);
            for (int i = toggles.Count - 1; i >= 0; i--)
            {
                var t = toggles[i];
                t.Visible = !(t.Key == "wifi" && WiFi.State == "None");
                if (!t.Visible) continue;
                t.Bounds = new Rectangle(right - d, (h - d) / 2, d, d); right -= d + Ui.S(6);
            }
            int left = bw + Ui.S(12), gap = Ui.S(6), avail = right - Ui.S(8) - left;
            var shown = chips.Where(c => c.Visible).ToList();
            hidden = new List<Chip>();
            compact = false;
            int total = shown.Sum(c => NaturalWidth(c, false)) + gap * (shown.Count - 1);
            if (total > avail) { compact = true; total = shown.Sum(c => NaturalWidth(c, true)) + gap * (shown.Count - 1); }
            int moreW = Ui.S(46);
            while (shown.Count > 0 && total > avail - (hidden.Count > 0 || total > avail ? moreW + gap : 0))
            {
                var last = shown[shown.Count - 1]; shown.RemoveAt(shown.Count - 1); hidden.Insert(0, last);
                total = shown.Sum(c => NaturalWidth(c, compact)) + gap * Math.Max(0, shown.Count - 1);
            }
            foreach (var c in chips) c.Bounds = Rectangle.Empty;
            // Spread leftover width evenly so the bar is filled.
            int extra = hidden.Count == 0 && shown.Count > 0 ? Math.Max(0, Math.Min(Ui.S(40) * shown.Count, avail - total)) / shown.Count : 0;
            int x = left, y = (h - ChipH) / 2;
            foreach (var c in shown) { int w = NaturalWidth(c, compact) + extra; c.Bounds = new Rectangle(x, y, w, ChipH); x += w + gap; }
            moreRect = hidden.Count > 0 ? new Rectangle(x, y, moreW, ChipH) : Rectangle.Empty;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated) { LayoutBar(); Invalidate(); } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); LayoutBar(); }

        // ---------- Paint ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Sidebar);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // Brand
            using (var b = new SolidBrush(Ui.SidebarSel)) g.FillRectangle(b, brandRect);
            if (logo == null) logo = Ui.Logo(Ui.S(28));
            int lx = brandRect.Width >= Ui.S(150) ? Ui.S(16) : (brandRect.Width - logo.Width) / 2;
            g.DrawImage(logo, lx, (Height - logo.Height) / 2);
            if (brandRect.Width >= Ui.S(150))
                TextRenderer.DrawText(g, App.Name, Ui.Big, new Rectangle(lx + logo.Width + Ui.S(10), 0, brandRect.Width - lx - logo.Width - Ui.S(12), Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            foreach (var c in chips) if (!c.Bounds.IsEmpty && c.Bounds.IntersectsWith(e.ClipRectangle)) DrawChip(g, c);
            if (!moreRect.IsEmpty) DrawPill(g, moreRect, Color.FromArgb(70, 255, 255, 255), hover as string == "more", "+" + hidden.Count, null);
            foreach (var t in toggles) if (t.Visible) DrawToggle(g, t);
            // Clock
            var now = DateTime.Now;
            bool ch = hover as string == "clock";
            if (ch) using (var path = Ui.Round(Rectangle.Inflate(clockRect, 0, -Ui.S(9)), Ui.S(8))) using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(b, path);
            var top = new Rectangle(clockRect.X, clockRect.Y + Ui.S(10), clockRect.Width, Ui.S(20));
            TextRenderer.DrawText(g, now.ToString("HH:mm"), Ui.Bold, top, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, now.ToString("ddd d MMM"), Ui.Small, new Rectangle(clockRect.X, top.Bottom, clockRect.Width, Ui.S(18)), Color.FromArgb(215, 255, 255, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }

        void DrawPill(Graphics g, Rectangle r, Color fill, bool hot, string text, Font font)
        {
            using (var path = Ui.Round(r, r.Height / 2))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (hot) using (var p = new Pen(Color.FromArgb(160, 255, 255, 255), 1.5f)) g.DrawPath(p, path);
            }
            if (text != null) TextRenderer.DrawText(g, text, font ?? Ui.Bold, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        void DrawChip(Graphics g, Chip c)
        {
            var r = c.Bounds;
            DrawPill(g, r, Color.FromArgb(235, c.Tint), ReferenceEquals(hover, c), null, null);
            int gd = Ui.S(26);
            var gr = new Rectangle(r.X + Ui.S(6), r.Y + (r.Height - gd) / 2, gd, gd);
            using (var b = new SolidBrush(Color.FromArgb(64, 255, 255, 255))) g.FillEllipse(b, gr);
            TextRenderer.DrawText(g, c.Glyph, Ui.Icon(10.5f), gr, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int tx = gr.Right + Ui.S(8), tw = r.Right - tx - Ui.S(10);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (compact)
                TextRenderer.DrawText(g, c.Value, Ui.Bold, new Rectangle(tx, r.Y, tw, r.Height), Color.White, flags | TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(g, c.Label, Ui.Small, new Rectangle(tx, r.Y + Ui.S(3), tw, Ui.S(15)), Color.FromArgb(205, 255, 255, 255), flags);
                TextRenderer.DrawText(g, c.Value, Ui.Bold, new Rectangle(tx, r.Y + Ui.S(17), tw, Ui.S(18)), Color.White, flags);
            }
            if (c.Spark != null && c.Spark.Length > 1)
            {
                var pts = new PointF[c.Spark.Length];
                float sx = r.X + r.Height / 2f, sw = r.Width - r.Height;
                for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(sx + i * sw / (pts.Length - 1), r.Bottom - Ui.S(3) - (float)Math.Max(0, Math.Min(100, c.Spark[i])) * Ui.S(8) / 100f);
                using (var p = new Pen(Color.FromArgb(120, 255, 255, 255), 1f)) g.DrawLines(p, pts);
            }
            else if (c.Bar >= 0)
            {
                int bx = r.X + r.Height / 2, bwid = r.Width - r.Height, by = r.Bottom - Ui.S(5);
                using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillRectangle(b, bx, by, bwid, Ui.S(3));
                using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, bx, by, (int)(bwid * Math.Min(1, c.Bar)), Ui.S(3));
            }
        }

        string ToggleGlyph(Toggle t)
        {
            switch (t.Key)
            {
                case "theme": return Ui.Dark ? "" : "";
                case "awake": return "";
                default: return WiFi.State == "Off" ? "" : "";
            }
        }
        bool ToggleOn(Toggle t) { return t.Key == "awake" ? Awake.On : t.Key == "wifi" ? WiFi.State == "On" : false; }

        void DrawToggle(Graphics g, Toggle t)
        {
            bool on = ToggleOn(t), hot = ReferenceEquals(hover, t);
            var fill = on ? Color.White : hot ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(28, 255, 255, 255);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, t.Bounds);
            var fore = on ? Ui.Sidebar : Color.White;
            if (t.Key == "wifi" && WiFi.Busy) fore = Color.FromArgb(120, fore);
            TextRenderer.DrawText(g, ToggleGlyph(t), Ui.Icon(13f), t.Bounds, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        // ---------- Mouse ----------
        object HitTest(Point p)
        {
            if (brandRect.Contains(p)) return "brand";
            if (clockRect.Contains(p)) return "clock";
            if (moreRect.Contains(p)) return "more";
            foreach (var c in chips) if (c.Bounds.Contains(p)) return c;
            foreach (var t in toggles) if (t.Visible && t.Bounds.Contains(p)) return t;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var h = HitTest(e.Location);
            if (!Equals(h, hover)) { hover = h; Cursor = h == null ? Cursors.Default : Cursors.Hand; Invalidate(); }
            string text = TipText(h);
            if (text != tipFor) { tipFor = text; tip.SetToolTip(this, text ?? ""); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover != null) { hover = null; Invalidate(); } tipFor = null; tip.SetToolTip(this, ""); }

        string TipText(object h)
        {
            var c = h as Chip; if (c != null) return c.Tip ?? (c.Label + ": " + c.Value);
            var t = h as Toggle;
            if (t != null) return t.Key == "theme" ? (Ui.Dark ? "Switch to light mode" : "Switch to dark mode") : t.Key == "awake" ? "Keep screen awake (no sleep / screensaver): " + (Awake.On ? "on" : "off") : "Wi-Fi: " + (WiFi.Busy ? "working..." : WiFi.State);
            var s = h as string;
            if (s == "brand") return "Home";
            if (s == "clock") return DateTime.Now.ToString("dddd d MMMM yyyy") + "\nClick: date & time tools";
            if (s == "more") return string.Join("\n", hidden.Select(x => x.Label + ": " + x.Value));
            return null;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var h = HitTest(e.Location);
            var c = h as Chip;
            if (e.Button == MouseButtons.Right) { ShowContext(e.Location, c); return; }
            if (e.Button != MouseButtons.Left) return;
            if (c != null) { App.I.ShowMain(c.Page); return; }
            var t = h as Toggle;
            if (t != null)
            {
                if (t.Key == "theme") BeginInvoke(new Action(() => App.I.ToggleDark()));
                else if (t.Key == "awake") Awake.Set(!Awake.On);
                else WiFi.Toggle();
                return;
            }
            var s = h as string;
            if (s == "brand") App.I.ShowMain("dashboard");
            else if (s == "clock") { var m = DateTimeTools.Menu(); m.Show(this, new Point(clockRect.Left, clockRect.Bottom)); }
            else if (s == "more")
            {
                var m = Ui.Menu();
                foreach (var x in hidden) { var page = x.Page; m.Items.Add(x.Label + ": " + x.Value, null, delegate { App.I.ShowMain(page); }); }
                Ui.ThemeMenu(m); m.Show(this, new Point(moreRect.Left, moreRect.Bottom));
            }
        }

        void ShowContext(Point p, Chip c)
        {
            var m = Ui.Menu();
            if (c != null) { var page = c.Page; m.Items.Add("Open " + c.Label, null, delegate { App.I.ShowMain(page); }); m.Items.Add("Copy \"" + c.Label + ": " + c.Value + "\"", null, delegate { App.I.CopyText(c.Label + ": " + c.Value); }); m.Items.Add(new ToolStripSeparator()); }
            m.Items.Add("Copy all values", null, delegate { App.I.CopyText(string.Join("\n", chips.Where(x => x.Visible).Select(x => x.Label + ": " + x.Value))); });
            m.Items.Add(Ui.Dark ? "Light mode" : "Dark mode", null, delegate { BeginInvoke(new Action(() => App.I.ToggleDark())); });
            var themes = new ToolStripMenuItem("Theme");
            for (int i = 0; i < Ui.ThemeNames.Length; i++) { int k = i; var it = new ToolStripMenuItem(Ui.ThemeNames[i], Ui.Swatch(Ui.ThemeColor(i), Ui.S(14)), delegate { BeginInvoke(new Action(() => App.I.SetTheme(k))); }) { Checked = Ui.ThemeIndex == i }; themes.DropDownItems.Add(it); }
            m.Items.Add(themes);
            m.Items.Add((Awake.On ? "Stop keeping awake" : "Keep screen awake"), null, delegate { Awake.Set(!Awake.On); });
            m.Items.Add("Wi-Fi " + (WiFi.State == "On" ? "off" : "on"), null, delegate { WiFi.Toggle(); });
            m.Items.Add("Task Manager", null, delegate { Hidden.Open("taskmgr.exe"); });
            Ui.ThemeMenu(m);
            m.Show(this, p);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ProcessSampler.Updated -= update; Store.ClipsChanged -= update; Store.PromptsChanged -= update; Store.NotesChanged -= update;
                Store.ActivityChanged -= update; Store.TaggedChanged -= update; Store.TasksChanged -= update; Recorder.Finished -= update;
                Awake.Changed -= redraw; WiFi.Changed -= redraw;
                clock.Dispose(); tip.Dispose(); if (logo != null) logo.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
