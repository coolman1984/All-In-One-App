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
            public bool Alert;               // colour (Tint) only when something needs attention
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
        readonly Action update, redraw, storeUpdate;
        readonly Timer factsTimer = new Timer { Interval = 400 };
        DashboardFacts facts; DateTime factsAt;
        string layoutKey;
        readonly Dictionary<string, int> widthCache = new Dictionary<string, int>();
        Bitmap logo;
        Rectangle brandRect, clockRect, moreRect, searchRect;
        bool compact;
        List<Chip> hidden = new List<Chip>();
        object hover;
        int lastMinute = -1;
        string tipFor;

        public TopBar(Func<int> brandWidth)
        {
            this.brandWidth = brandWidth;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top; Height = Ui.S(Ui.Nexus ? 64 : 58); BackColor = Ui.Nexus ? Ui.Card : Ui.Sidebar; ForeColor = Ui.Nexus ? Ui.Text : Color.White;
            Add("cpu", "", "CPU", "#3B82F6", "system:procs");
            Add("ram", "", "RAM", "#8B5CF6", "system:procs");
            Add("c", "", "C:", "#14B8A6", "system:drives");
            Add("d", "", "D:", "#06B6D4", "system:drives");
            Add("apps", "", "Top app", "#F97316", "system:procs");
            Add("tasks", "", "Due today", "#64748B", "tasks");
            Add("urgent", "", "Urgent", "#64748B", "tagged");
            Add("rec", "", "Recorder", "#64748B", "snip");
            foreach (var k in new[] { "assistant", "overlay", "theme", "awake", "wifi" }) toggles.Add(new Toggle { Key = k });
            clock.Tick += delegate { if (DateTime.Now.Minute != lastMinute) { lastMinute = DateTime.Now.Minute; Invalidate(clockRect); } };
            update = () => { if (Live) RefreshValues(); };
            // Store events can come in bursts (typing in a note, copying): recompute the data facts once, 400 ms after the last one.
            storeUpdate = () => { facts = null; if (Live) { factsTimer.Stop(); factsTimer.Start(); } };
            factsTimer.Tick += delegate { factsTimer.Stop(); if (Live) RefreshValues(); };
            redraw = () => { if (!IsDisposed) Invalidate(); };
            ProcessSampler.Updated += update; Store.ClipsChanged += storeUpdate; Store.PromptsChanged += storeUpdate; Store.NotesChanged += storeUpdate;
            Store.ActivityChanged += storeUpdate; Store.TaggedChanged += storeUpdate; Store.TasksChanged += storeUpdate; Recorder.Finished += storeUpdate;
            Awake.Changed += redraw; WiFi.Changed += redraw; Overlay.Changed += redraw;
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
            if (facts == null || (DateTime.Now - factsAt).TotalSeconds >= 60) { facts = DashboardFacts.Read(Store.Now()); factsAt = DateTime.Now; }
            var cpu = C("cpu"); cpu.Value = ProcessSampler.CpuTotal.ToString("0") + "%"; cpu.Spark = ProcessSampler.CpuHistory.ToArray();
            cpu.Alert = ProcessSampler.CpuTotal > 85; cpu.Tint = Red;
            var ram = C("ram"); ram.Value = ProcessSampler.MemLoad + "%"; ram.Bar = ProcessSampler.MemLoad / 100.0;
            ram.Alert = ProcessSampler.MemLoad > 85; ram.Tint = ProcessSampler.MemLoad > 92 ? Red : Amber;
            ram.Tip = "RAM " + ProcessSampler.MemLoad + "% used (" + Fmt.Bytes((long)(ProcessSampler.MemTotal - ProcessSampler.MemAvail)) + " of " + Fmt.Bytes((long)ProcessSampler.MemTotal) + ")";
            foreach (var key in new[] { "c", "d" })
            {
                var d = DriveSnapshot.Read(key.ToUpperInvariant()); var chip = C(key);
                chip.Visible = d.Used >= 0;
                chip.Value = d.Value.Replace(" free", "") + " free"; chip.Bar = d.Used; chip.Tip = key.ToUpperInvariant() + ": " + d.Value + " " + d.Detail;
                chip.Alert = d.Used > 0.9; chip.Tint = d.Used > 0.95 ? Red : Amber;
            }
            var top = ProcessSampler.MemoryApps;
            var apps = C("apps");
            apps.Value = top.Count > 0 ? Ui.OneLine(top[0].Name, 14) + " " + Fmt.Bytes(top[0].Mem) : "-";
            apps.Tip = "Top memory apps:\n" + string.Join("\n", top.Take(3).Select(p => p.Name + "  " + Fmt.Bytes(p.Mem)));
            var tasks = C("tasks"); tasks.Value = facts.RemindersToday.ToString(); tasks.Alert = facts.RemindersToday > 0; tasks.Tint = Red;
            tasks.Tip = "Due today / overdue tasks and reminders: " + facts.RemindersToday;
            var urgent = C("urgent"); urgent.Value = facts.Urgent.ToString(); urgent.Alert = true; urgent.Tint = Red;
            var rec = C("rec"); rec.Value = facts.Recording; rec.Alert = facts.Recording == "Recording"; rec.Tint = Red;
            // Chips that would only say "0" or "Ready" are hidden: the bar shows what needs attention.
            foreach (var c in chips) { if (c.Key != "c" && c.Key != "d") c.Visible = true; if (c.Tip == null || c.Key == "urgent" || c.Key == "rec" || c.Key == "cpu") c.Tip = c.Label + ": " + c.Value; }
            urgent.Visible = facts.Urgent > 0;
            rec.Visible = facts.Recording != "Ready";
            // Re-layout only when a chip text or visibility changed (layout measures text for every chip).
            var sig = new System.Text.StringBuilder();
            foreach (var c in chips) sig.Append(c.Visible ? c.Value : "\u0001").Append('\u0002');
            string k = sig.ToString();
            if (k != layoutKey) { layoutKey = k; LayoutBar(); }
            Invalidate();
        }

        // ---------- Layout ----------
        int ChipH { get { return Ui.S(38); } }
        int NaturalWidth(Chip c, bool small)
        {
            string ck = (small ? "s|" : "f|") + c.Value + "|" + c.Label;
            int cached;
            if (widthCache.TryGetValue(ck, out cached)) return cached;
            if (widthCache.Count > 400) widthCache.Clear();
            int w = MeasureChip(c, small);
            widthCache[ck] = w;
            return w;
        }
        int MeasureChip(Chip c, bool small)
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
            int left = bw + Ui.S(12), gap = Ui.S(Ui.Nexus ? 8 : 6);
            searchRect = Rectangle.Empty;
            if (Ui.Nexus)
            {
                // Search pill (full when there is room, icon-only when narrow).
                int sh = Ui.S(40), room = right - left;
                int sw = room > Ui.S(900) ? Ui.S(280) : room > Ui.S(560) ? Ui.S(200) : sh;
                searchRect = new Rectangle(left + Ui.S(8), (h - sh) / 2, sw, sh);
                left = searchRect.Right + Ui.S(14);
            }
            int avail = right - Ui.S(8) - left;
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
            if (Ui.Nexus) { PaintNexus(e); return; }
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
                TextRenderer.DrawText(g, Ui.AppTitle, Ui.Big, new Rectangle(lx + logo.Width + Ui.S(10), 0, brandRect.Width - lx - logo.Width - Ui.S(12), Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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

        // ---------- Nexus paint: white bar, search pill, soft chips, outline toggles ----------
        void PaintNexus(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Navy ? Ui.Bg : Ui.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (Ui.Navy)
            {
                // Top of the floating navy sidebar card (the sidebar panel paints the rest).
                var card = new Rectangle(Ui.S(12), Ui.S(12), brandRect.Width - Ui.S(14), Height + Ui.S(40));
                Ui.Shadow(g, card, Ui.S(18));
                using (var path = Ui.Round(card, Ui.S(18))) using (var b = new SolidBrush(Ui.Sidebar)) g.FillPath(b, path);
            }
            else
            {
                using (var b = new SolidBrush(Ui.Sidebar)) g.FillRectangle(b, brandRect);
                using (var p = new Pen(Ui.Line))
                {
                    g.DrawLine(p, 0, Height - 1, Width, Height - 1);
                    g.DrawLine(p, brandRect.Right - 1, 0, brandRect.Right - 1, Height);
                }
            }
            // Brand
            if (logo == null) logo = Ui.Logo(Ui.S(30));
            bool wide = brandRect.Width >= Ui.S(150);
            int off = Ui.Navy ? Ui.S(12) : 0, cy = Ui.Navy ? Ui.S(6) : 0;
            int lx = wide ? Ui.S(20) + off : off + (brandRect.Width - off - Ui.S(2) - logo.Width) / 2;
            g.DrawImage(logo, lx, (Height - logo.Height) / 2 + cy);
            if (wide)
                TextRenderer.DrawText(g, Ui.AppTitle, Ui.Title, new Rectangle(lx + logo.Width + Ui.S(10), cy, brandRect.Width - lx - logo.Width - Ui.S(14), Height), Ui.Navy ? Color.White : Ui.SidebarText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            // Search
            if (!searchRect.IsEmpty)
            {
                bool hot = hover as string == "search";
                if (!Ui.Flow) Ui.Shadow(g, searchRect, Ui.S(10));
                using (var path = Ui.Round(searchRect, Ui.S(Ui.Flow ? 12 : 10)))
                {
                    using (var b = new SolidBrush(hot ? Ui.HoverBg : Ui.Navy ? Ui.Card : Ui.Bg)) g.FillPath(b, path);
                    using (var p = new Pen(hot ? Ui.Accent : Ui.Flow ? Ui.Mix(Ui.Line, Ui.Bg, 0.35) : Ui.Line)) g.DrawPath(p, path);
                }
                var ir = new Rectangle(searchRect.X + Ui.S(4), searchRect.Y, Ui.S(32), searchRect.Height);
                TextRenderer.DrawText(g, "", Ui.Icon(11f), ir, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                if (searchRect.Width > Ui.S(120))
                {
                    string key = "Ctrl+K";
                    int kw = TextRenderer.MeasureText(key, Ui.Small).Width + Ui.S(6);
                    var kr = new Rectangle(searchRect.Right - kw - Ui.S(8), searchRect.Y + Ui.S(9), kw, searchRect.Height - Ui.S(18));
                    using (var path = Ui.Round(kr, Ui.S(5))) { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(Ui.Line)) g.DrawPath(p, path); }
                    TextRenderer.DrawText(g, key, Ui.Small, kr, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, Ui.Flow ? "Search this app..." : "Search this app", Ui.Base, new Rectangle(ir.Right, searchRect.Y, Math.Max(1, kr.Left - ir.Right - Ui.S(4)), searchRect.Height), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
            }
            foreach (var c in chips) if (!c.Bounds.IsEmpty && c.Bounds.IntersectsWith(e.ClipRectangle)) DrawChipNexus(g, c);
            if (!moreRect.IsEmpty)
            {
                using (var path = Ui.Round(moreRect, Ui.S(10)))
                {
                    using (var b = new SolidBrush(hover as string == "more" ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                    using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
                }
                TextRenderer.DrawText(g, "+" + hidden.Count, Ui.Bold, moreRect, Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            foreach (var t in toggles)
            {
                if (!t.Visible) continue;
                bool on = ToggleOn(t), hot = ReferenceEquals(hover, t);
                var r = Rectangle.Inflate(t.Bounds, -1, -1);
                using (var b = new SolidBrush(on ? Ui.Accent : hot ? Ui.HoverBg : Ui.Card)) g.FillEllipse(b, r);
                if (!on) using (var p = new Pen(Ui.Line)) g.DrawEllipse(p, r);
                var fore = on ? Color.White : Ui.Muted;
                if (t.Key == "wifi" && WiFi.Busy) fore = Color.FromArgb(120, fore);
                TextRenderer.DrawText(g, ToggleGlyph(t), Ui.Icon(12f), t.Bounds, fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var now = DateTime.Now;
            if (hover as string == "clock") using (var path = Ui.Round(Rectangle.Inflate(clockRect, 0, -Ui.S(10)), Ui.S(8))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
            var top = new Rectangle(clockRect.X, clockRect.Y + Ui.S(12), clockRect.Width, Ui.S(20));
            TextRenderer.DrawText(g, now.ToString("HH:mm"), Ui.Bold, top, Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, now.ToString("ddd d MMM"), Ui.Small, new Rectangle(clockRect.X, top.Bottom, clockRect.Width, Ui.S(18)), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        }

        static Color FlowChipAccent(string key)
        {
            switch (key)
            {
                case "cpu": return Color.FromArgb(47, 102, 246);
                case "ram": return Color.FromArgb(139, 92, 246);
                case "c": return Color.FromArgb(34, 197, 139);
                case "d": return Color.FromArgb(14, 165, 233);
                case "apps": return Color.FromArgb(245, 158, 45);
                case "tasks": return Color.FromArgb(47, 102, 246);
                default: return Color.FromArgb(239, 68, 68);
            }
        }

        void DrawChipNexus(Graphics g, Chip c)
        {
            var r = c.Bounds;
            bool hot = ReferenceEquals(hover, c);
            var chipTint = c.Alert ? c.Tint : Ui.Flow ? FlowChipAccent(c.Key) : Ui.Muted;
            using (var path = Ui.Round(r, Ui.S(Ui.Flow ? 11 : 10)))
            {
                using (var b = new SolidBrush(hot ? Ui.HoverBg : Ui.Flow ? Ui.Bg : Ui.Card)) g.FillPath(b, path);
                using (var p = new Pen(c.Alert ? Color.FromArgb(150, c.Tint) : hot ? Ui.Accent : Ui.Flow ? Ui.Line : Ui.Navy ? Ui.Mix(Ui.Line, Ui.Card, 0.4) : Ui.Line)) g.DrawPath(p, path);
            }
            int gd = Ui.S(26);
            var gr = new Rectangle(r.X + Ui.S(6), r.Y + (r.Height - gd) / 2, gd, gd);
            if (Ui.Flow)
                using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 42 : 24, chipTint))) g.FillEllipse(b, gr);
            TextRenderer.DrawText(g, c.Glyph, Ui.Icon(10.5f), gr, chipTint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int tx = gr.Right + Ui.S(8), tw = Math.Max(1, r.Right - tx - Ui.S(10));
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (compact)
                TextRenderer.DrawText(g, c.Value, Ui.Bold, new Rectangle(tx, r.Y, tw, r.Height), Ui.Text, flags | TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(g, c.Label, Ui.Small, new Rectangle(tx, r.Y + Ui.S(3), tw, Ui.S(15)), Ui.Muted, flags);
                TextRenderer.DrawText(g, c.Value, Ui.Bold, new Rectangle(tx, r.Y + Ui.S(17), tw, Ui.S(18)), Ui.Text, flags);
            }
            if (c.Bar >= 0 && (c.Spark == null || c.Spark.Length < 2))
            {
                int bx = r.X + Ui.S(10), bwid = Math.Max(1, r.Width - Ui.S(20)), by = r.Bottom - Ui.S(4);
                using (var b = new SolidBrush(Ui.Track)) g.FillRectangle(b, bx, by, bwid, Ui.S(2));
                using (var b = new SolidBrush(c.Alert ? c.Tint : Ui.Flow ? chipTint : Ui.Accent)) g.FillRectangle(b, bx, by, (int)(bwid * Math.Min(1, c.Bar)), Ui.S(2));
            }
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
            DrawPill(g, r, c.Alert ? Color.FromArgb(235, c.Tint) : Color.FromArgb(38, 255, 255, 255), ReferenceEquals(hover, c), null, null);
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
                // Classic pills are shorter: text sits higher so the usage bar below never touches it.
                TextRenderer.DrawText(g, c.Label, Ui.Small, new Rectangle(tx, r.Y + Ui.S(2), tw, Ui.S(15)), Color.FromArgb(205, 255, 255, 255), flags);
                TextRenderer.DrawText(g, c.Value, Ui.Bold, new Rectangle(tx, r.Y + Ui.S(14), tw, Ui.S(18)), Color.White, flags);
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
                int bx = tx, bwid = Math.Max(Ui.S(10), r.Right - Ui.S(12) - tx), by = r.Bottom - Ui.S(4);
                using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillRectangle(b, bx, by, bwid, Ui.S(2));
                using (var b = new SolidBrush(Color.White)) g.FillRectangle(b, bx, by, (int)(bwid * Math.Min(1, c.Bar)), Ui.S(2));
            }
        }

        string ToggleGlyph(Toggle t)
        {
            switch (t.Key)
            {
                case "assistant": return "";
                case "theme": return Ui.Dark ? "" : "";
                case "overlay": return "";
                case "awake": return "";
                default: return WiFi.State == "Off" ? "" : "";
            }
        }
        bool ToggleOn(Toggle t) { return t.Key == "assistant" ? Store.Settings.AssistantOpen : t.Key == "overlay" ? Overlay.On : t.Key == "awake" ? Awake.On : t.Key == "wifi" ? WiFi.State == "On" : false; }

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
            if (searchRect.Contains(p)) return "search";
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
            if (t != null) return t.Key == "assistant" ? "Assistant: chat with an agent that works the app for you (Ctrl+J)" : t.Key == "overlay" ? "Desktop overlay (dock, menu bar, hot corner, window overview): " + (Overlay.On ? "on" : "off") : t.Key == "theme" ? "Look: design, colour, light or dark" : t.Key == "awake" ? "Keep screen awake (no sleep / screensaver): " + (Awake.On ? "on" : "off") : "Wi-Fi: " + (WiFi.Busy ? "working..." : WiFi.State);
            var s = h as string;
            if (s == "brand") return "Home";
            if (s == "search") return "Search notes, prompts, clips, tasks and pages";
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
                if (t.Key == "theme") LookPicker.Open(this, PointToScreen(new Point(e.X, Height)));   // designs and light/dark, one click
                else if (t.Key == "assistant") App.I.ToggleAssistant();
                else if (t.Key == "awake") Awake.Set(!Awake.On);
                else if (t.Key == "overlay") Overlay.Toggle();
                else WiFi.Toggle();
                return;
            }
            var s = h as string;
            if (s == "brand") App.I.ShowMain("dashboard");
            else if (s == "search") GlobalSearch.Open(FindForm());
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
            m.Items.Add("Choose a look...", null, delegate { LookPicker.Open(this, Cursor.Position); });
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
                ProcessSampler.Updated -= update; Store.ClipsChanged -= storeUpdate; Store.PromptsChanged -= storeUpdate; Store.NotesChanged -= storeUpdate;
                Store.ActivityChanged -= storeUpdate; Store.TaggedChanged -= storeUpdate; Store.TasksChanged -= storeUpdate; Recorder.Finished -= storeUpdate;
                factsTimer.Dispose();
                Awake.Changed -= redraw; WiFi.Changed -= redraw; Overlay.Changed -= redraw;
                clock.Dispose(); tip.Dispose(); if (logo != null) logo.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
