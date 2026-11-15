using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Home dashboard (SerQ / Nexus designs): welcome header with actions, live overview area chart, library donut,
    // four KPI cards, storage funnel and a top-apps list with status pills. One owner-drawn control.
    class NexusBoard : Control
    {
        class Hot { public Rectangle R; public string Key; public Action Run; }

        readonly List<Hot> hots = new List<Hot>();
        Hot hover;
        int width = 800;

        Rectangle header, btnNew, btnTerm, bell, avatar, overview, donut, storage, apps;
        readonly Rectangle[] kpi = new Rectangle[4];

        // Data
        double[] cpu = new double[0];
        double cpuNow; int memLoad; string memUsed = "-", memDelta = "", cpuAvg = "-", cpuDelta = "";
        readonly string[] kpiLabel = { "Tasks due today", "Clips today", "Files opened today", "Tagged urgent" };
        readonly string[] kpiGlyph = { "", "", "", "" };
        readonly string[] kpiPage = { "tasks", "clips", "activity", "tagged" };
        readonly int[] kpiValue = new int[4];
        readonly string[] kpiNote = { "", "", "", "" };
        readonly int[] kpiTrend = new int[4];   // 1 good (green), -1 bad (red), 0 neutral
        readonly int[] lib = new int[4];
        static readonly string[] libName = { "Notes", "Prompts", "Clips", "Open tasks" };
        DriveSnapshot driveC, driveD;
        List<KeyValuePair<string, long>> top = new List<KeyValuePair<string, long>>();
        Dictionary<string, int> procCount = new Dictionary<string, int>();
        int dueToday;

        public NexusBoard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "Dashboard";
        }

        static string UserName()
        {
            string n = Environment.UserName ?? "";
            var parts = n.Split(new[] { '.', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string best = parts.OrderByDescending(p => p.Length).FirstOrDefault() ?? n;
            return best.Length == 0 ? "there" : char.ToUpperInvariant(best[0]) + best.Substring(1).ToLowerInvariant();
        }

        // ---------- data ----------
        bool dataLoaded;
        // live = only the sampler values (every 2 s); data = also the store facts (only when the store changed).
        public void RefreshData() { RefreshData(true); }
        public void RefreshData(bool data)
        {
            cpu = ProcessSampler.CpuHistory.ToArray();
            var ram = ProcessSampler.RamHistory.ToArray();
            cpuNow = ProcessSampler.CpuTotal; memLoad = ProcessSampler.MemLoad;
            memUsed = Fmt.Bytes((long)(ProcessSampler.MemTotal - ProcessSampler.MemAvail));
            memDelta = Delta(ram); cpuDelta = Delta(cpu);
            cpuAvg = (cpu.Length > 0 ? cpu.Average() : cpuNow).ToString("0") + "%";

            driveC = DriveSnapshot.Read("C"); driveD = DriveSnapshot.Read("D");
            var groups = ProcessSampler.Last.GroupBy(p => p.Display).ToList();
            top = groups.Select(g => new KeyValuePair<string, long>(g.Key, g.Sum(p => p.Mem))).OrderByDescending(k => k.Value).Take(10).ToList();
            procCount = groups.ToDictionary(g => g.Key ?? "", g => g.Count());
            if (data || !dataLoaded) { dataLoaded = true; RefreshStoreData(); }
            Invalidate();
        }

        void RefreshStoreData()
        {
            var facts = DashboardFacts.Read(Store.Now());
            long today = DateTime.Today.Ticks, yesterday = DateTime.Today.AddDays(-1).Ticks;
            int clipsY = Store.Clips.Count(c => c.Time >= yesterday && c.Time < today);
            int filesY = Store.Activity.Where(a => a.Event == "Opened" && a.Time >= yesterday && a.Time < today).Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            int overdue = Store.Tasks.Count(t => !t.Done && t.Due > 0 && t.Due < today);
            dueToday = facts.RemindersToday;
            kpiValue[0] = facts.RemindersToday; kpiNote[0] = overdue > 0 ? overdue + " overdue" : "Nothing overdue"; kpiTrend[0] = overdue > 0 ? -1 : 1;
            kpiValue[1] = facts.ClipsToday; kpiNote[1] = Vs(facts.ClipsToday, clipsY); kpiTrend[1] = facts.ClipsToday >= clipsY ? 1 : -1;
            kpiValue[2] = facts.FilesToday; kpiNote[2] = Vs(facts.FilesToday, filesY); kpiTrend[2] = facts.FilesToday >= filesY ? 1 : -1;
            kpiValue[3] = facts.Urgent; kpiNote[3] = Store.Tagged.Count + " tagged in total"; kpiTrend[3] = facts.Urgent > 0 ? -1 : 1;

            lib[0] = Store.Notes.Count; lib[1] = Store.Prompts.Count; lib[2] = Store.Clips.Count; lib[3] = Store.Tasks.Count(t => !t.Done);
        }

        static string Vs(int now, int before)
        {
            if (before == 0) return now == 0 ? "Same as yesterday" : "↑ " + now + " vs yesterday";
            int pct = (int)Math.Round((now - before) * 100.0 / before);
            return (pct >= 0 ? "↑ " : "↓ ") + Math.Abs(pct) + "% vs yesterday";
        }

        static string Delta(double[] h)
        {
            if (h.Length < 6) return "";
            double d = h.Skip(h.Length - 3).Average() - h.Take(Math.Min(10, h.Length - 3)).Average();
            return (d >= 0 ? "↑ " : "↓ ") + Math.Abs(d).ToString("0.0") + "% vs 2 min ago";
        }

        // ---------- layout ----------
        // Lays out for a width (a small inner margin keeps the card shadows visible) and returns the height.
        public int Measure(int full)
        {
            int m = Ui.S(8);
            int h = Arrange(Math.Max(Ui.S(200), full - 2 * m));
            var off = new Size(m, m);
            header.Offset(m, m); btnNew.Offset(m, m); btnTerm.Offset(m, m); bell.Offset(m, m); avatar.Offset(m, m);
            overview.Offset(m, m); donut.Offset(m, m); storage.Offset(m, m); apps.Offset(m, m);
            for (int i = 0; i < 4; i++) kpi[i].Offset(m, m);
            foreach (var hot in hots) hot.R = new Rectangle(hot.R.Location + off, hot.R.Size);
            return h + 2 * m;
        }

        int Arrange(int w)
        {
            width = w;
            hots.Clear();
            int gap = Ui.S(18);
            header = new Rectangle(0, 0, w, Ui.S(70));
            int bh = Ui.S(40), c = Ui.S(42);
            avatar = new Rectangle(w - c, Ui.S(10), c, c);
            bell = new Rectangle(avatar.X - Ui.S(12) - c, Ui.S(10), c, c);
            int bw = TextRenderer.MeasureText("New task", Ui.Bold).Width + Ui.S(52);
            int tw = TextRenderer.MeasureText("Terminals", Ui.Bold).Width + Ui.S(52);
            bool roomy = w > Ui.S(700);
            btnNew = roomy ? new Rectangle(bell.X - Ui.S(16) - bw, Ui.S(11), bw, bh) : Rectangle.Empty;
            btnTerm = roomy && w > Ui.S(900) ? new Rectangle(btnNew.X - Ui.S(10) - tw, Ui.S(11), tw, bh) : Rectangle.Empty;
            hots.Add(new Hot { R = bell, Key = "bell", Run = () => App.I.ShowMain("tasks") });
            hots.Add(new Hot { R = avatar, Key = "avatar", Run = () => App.I.ShowSettings(FindForm()) });
            if (roomy) hots.Add(new Hot { R = btnNew, Key = "new", Run = () => QuickTaskForm.Open() });
            if (!btnTerm.IsEmpty) hots.Add(new Hot { R = btnTerm, Key = "term", Run = () => App.I.ShowMain("terminals") });
            int y = header.Bottom + Ui.S(10);

            bool two = w >= Ui.S(900);
            int left = two ? (w - gap) * 58 / 100 : w, right = two ? w - gap - left : w;
            int h1 = Ui.S(340);
            overview = new Rectangle(0, y, left, h1);
            donut = two ? new Rectangle(left + gap, y, right, h1) : new Rectangle(0, y + h1 + gap, w, h1);
            y = donut.Bottom + gap;
            hots.Add(new Hot { R = new Rectangle(overview.X, overview.Y, overview.Width, Ui.S(60)), Key = "overview", Run = () => App.I.ShowMain("system:procs") });
            hots.Add(new Hot { R = ViewAll(donut), Key = "donutall", Run = () => App.I.ShowMain("notes") });

            int cols = w >= Ui.S(980) ? 4 : w >= Ui.S(500) ? 2 : 1;
            int kw = (w - gap * (cols - 1)) / cols, kh = Ui.S(128);
            for (int i = 0; i < 4; i++)
            {
                kpi[i] = new Rectangle((i % cols) * (kw + gap), y + (i / cols) * (kh + gap), kw, kh);
                string page = kpiPage[i];
                hots.Add(new Hot { R = kpi[i], Key = "kpi" + i, Run = () => App.I.ShowMain(page) });
            }
            y += ((4 + cols - 1) / cols) * (kh + gap);

            int sl = two ? (w - gap) * 42 / 100 : w, sr = two ? w - gap - sl : w;
            int h3 = Ui.S(330);
            storage = new Rectangle(0, y, sl, h3);
            apps = two ? new Rectangle(sl + gap, y, sr, h3) : new Rectangle(0, y + h3 + gap, w, h3);
            y = apps.Bottom + gap;
            hots.Add(new Hot { R = new Rectangle(storage.X, storage.Y, storage.Width, Ui.S(60)), Key = "storage", Run = () => App.I.ShowMain("system:drives") });
            hots.Add(new Hot { R = ViewAll(apps), Key = "appsall", Run = () => App.I.ShowMain("system:procs") });
            return y + Ui.S(6);
        }

        static Rectangle ViewAll(Rectangle card)
        {
            int w = TextRenderer.MeasureText("View all", Ui.Bold).Width + Ui.S(28);
            return new Rectangle(card.Right - Ui.S(22) - w, card.Y + Ui.S(22), w, Ui.S(34));
        }

        // ---------- paint helpers ----------
        static readonly TextFormatFlags L = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        static readonly TextFormatFlags C = TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        static Color Green { get { return Ui.Dark ? Color.FromArgb(52, 211, 153) : Color.FromArgb(18, 168, 110); } }
        static Color Red { get { return Ui.Dark ? Color.FromArgb(251, 113, 133) : Color.FromArgb(229, 72, 77); } }
        static Color Amber { get { return Color.FromArgb(245, 176, 40); } }
        static Color Grey { get { return Ui.Dark ? Color.FromArgb(90, 98, 120) : Color.FromArgb(208, 213, 221); } }
        static Color Blue { get { return Ui.Accent; } }

        static Font emoji, badge, huge; static string fontKey;
        static void Fonts()
        {
            string k = Ui.FontName + Ui.Kpi.Size;
            if (k == fontKey) return;
            fontKey = k;
            emoji = new Font("Segoe UI Emoji", Ui.Title.SizeInPoints * 0.85f);
            badge = new Font(Ui.FontName, 7f, FontStyle.Bold);
            huge = new Font(Ui.FontName, Ui.Kpi.SizeInPoints * 1.25f, FontStyle.Bold);
        }
        static Font EmojiFont { get { Fonts(); return emoji; } }
        static Font BadgeFont { get { Fonts(); return badge; } }
        static Font HugeFont { get { Fonts(); return huge; } }

        bool IsHot(string key) { return hover != null && hover.Key == key; }
        int Radius { get { return Ui.S(Ui.Navy ? 18 : 14); } }

        void Card(Graphics g, Rectangle r, bool hot) { Ui.FillCard(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Radius, hot); }

        void Title(Graphics g, Rectangle card, string title, string sub)
        {
            TextRenderer.DrawText(g, title, Ui.Big, new Rectangle(card.X + Ui.S(24), card.Y + Ui.S(20), card.Width - Ui.S(150), Ui.S(26)), Ui.Text, L);
            TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(card.X + Ui.S(24), card.Y + Ui.S(46), card.Width - Ui.S(150), Ui.S(18)), Ui.Muted, L);
        }

        void Outline(Graphics g, Rectangle r, string text, string key)
        {
            using (var path = Ui.Round(r, Ui.S(10)))
            {
                using (var b = new SolidBrush(IsHot(key) ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, text, Ui.Bold, r, Ui.Text, C);
        }

        static void Pill(Graphics g, Rectangle r, string text, Color tone)
        {
            using (var path = Ui.Round(r, Ui.S(8))) using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 50 : 30, tone))) g.FillPath(b, path);
            TextRenderer.DrawText(g, text, Ui.Small, r, tone, C);
        }

        static void IconTile(Graphics g, Rectangle r, string glyph, Color tone, float size)
        {
            using (var path = Ui.Round(r, Ui.S(12))) using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 55 : 26, tone))) g.FillPath(b, path);
            TextRenderer.DrawText(g, glyph, Ui.Icon(size), r, tone, C);
        }

        // ---------- paint ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            if (header.Width <= Ui.S(40) || header.Height <= 0) return;   // not measured yet (first paint before layout)
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var clip = e.ClipRectangle;
            if (header.IntersectsWith(clip)) Header(g);
            if (overview.IntersectsWith(clip)) Overview(g);
            if (donut.IntersectsWith(clip)) Donut(g);
            for (int i = 0; i < 4; i++) if (kpi[i].IntersectsWith(clip)) Kpi(g, i);
            if (storage.IntersectsWith(clip)) Storage(g);
            if (apps.IntersectsWith(clip)) Apps(g);
        }

        void Header(Graphics g)
        {
            int limit = (btnTerm.Width > 0 ? btnTerm.X : btnNew.Width > 0 ? btnNew.X : bell.X) - Ui.S(12);
            string hello = "Welcome back, ", name = UserName();
            int hw = TextRenderer.MeasureText(g, hello, Ui.Title, Size.Empty, TextFormatFlags.NoPadding).Width;
            int nw = TextRenderer.MeasureText(g, name, Ui.Title, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, hello, Ui.Title, new Rectangle(0, Ui.S(6), Math.Min(hw + 2, limit), Ui.S(32)), Ui.Text, L);
            if (hw + nw < limit)
            {
                TextRenderer.DrawText(g, name, Ui.Title, new Rectangle(hw, Ui.S(6), nw + 2, Ui.S(32)), Ui.Accent, L);
                if (hw + nw + Ui.S(30) < limit) TextRenderer.DrawText(g, "\U0001F44B", EmojiFont, new Rectangle(hw + nw + Ui.S(6), Ui.S(6), Ui.S(30), Ui.S(32)), Amber, L);
            }
            TextRenderer.DrawText(g, "Here's what's happening on your PC today  ·  " + DateTime.Now.ToString("dddd, d MMMM"), Ui.Base, new Rectangle(0, Ui.S(40), Math.Max(Ui.S(80), limit), Ui.S(20)), Ui.Muted, L);

            if (btnTerm.Width > 0) { var r = btnTerm; Ui.Shadow(g, r, Ui.S(10)); Outline(g, r, "", "term"); BtnText(g, r, "", "Terminals", Ui.Text); }
            if (btnNew.Width > 0)
            {
                var r = btnNew;
                Ui.Shadow(g, r, Ui.S(10));
                using (var path = Ui.Round(r, Ui.S(10)))
                using (var b = new LinearGradientBrush(r, IsHot("new") ? Ui.AccentHover : Ui.Mix(Ui.AccentFill, Color.White, 0.12), IsHot("new") ? Ui.AccentPressed : Ui.AccentFill, LinearGradientMode.Vertical))
                    g.FillPath(b, path);
                BtnText(g, r, "", "New task", Color.White);
            }
            // Bell with a count badge, avatar with initials
            Ui.Shadow(g, bell, bell.Width / 2);
            using (var b = new SolidBrush(IsHot("bell") ? Ui.HoverBg : Ui.Card)) g.FillEllipse(b, bell);
            TextRenderer.DrawText(g, "", Ui.Icon(13f), bell, Ui.Text, C);
            if (dueToday > 0)
            {
                var br = new Rectangle(bell.Right - Ui.S(16), bell.Y - Ui.S(2), Ui.S(18), Ui.S(18));
                using (var b = new SolidBrush(Blue)) g.FillEllipse(b, br);
                TextRenderer.DrawText(g, dueToday > 9 ? "9+" : dueToday.ToString(), BadgeFont, br, Color.White, C);
            }
            if (avatar.Width <= 0) return;
            Ui.Shadow(g, avatar, avatar.Width / 2);
            using (var b = new LinearGradientBrush(avatar, Ui.Mix(Ui.AccentFill, Color.White, 0.25), Ui.Sidebar, LinearGradientMode.ForwardDiagonal)) g.FillEllipse(b, avatar);
            using (var p = new Pen(Ui.Card, Ui.S(2))) g.DrawEllipse(p, avatar);
            string init = UserName().Substring(0, 1);
            TextRenderer.DrawText(g, init, Ui.Big, avatar, Color.White, C);
        }

        static void BtnText(Graphics g, Rectangle r, string glyph, string text, Color fore)
        {
            int tw = TextRenderer.MeasureText(text, Ui.Bold).Width;
            int x = r.X + (r.Width - tw - Ui.S(24)) / 2;
            TextRenderer.DrawText(g, glyph, Ui.Icon(10.5f), new Rectangle(x, r.Y, Ui.S(18), r.Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, Ui.Bold, new Rectangle(x + Ui.S(24), r.Y, tw + Ui.S(4), r.Height), fore, L);
        }

        void Overview(Graphics g)
        {
            var c = overview;
            Card(g, c, IsHot("overview"));
            Title(g, c, "System Overview", "Live CPU load of your PC, every 2 seconds");
            var pill = new Rectangle(c.Right - Ui.S(24) - Ui.S(118), c.Y + Ui.S(22), Ui.S(118), Ui.S(32));
            using (var path = Ui.Round(pill, Ui.S(9))) { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(Ui.Line)) g.DrawPath(p, path); }
            TextRenderer.DrawText(g, "Last 2 min  ˅", Ui.Small, pill, Ui.Text, C);

            int statsH = Ui.S(96);
            var plot = new Rectangle(c.X + Ui.S(28), c.Y + Ui.S(86), c.Width - Ui.S(56), c.Height - Ui.S(86) - statsH - Ui.S(10));
            var data = cpu.Length >= 2 ? cpu : new double[] { cpuNow, cpuNow };
            double max = Math.Max(25, data.Max() * 1.25);
            var pts = new PointF[data.Length];
            for (int i = 0; i < data.Length; i++)
                pts[i] = new PointF(plot.X + i * plot.Width / (float)(data.Length - 1), plot.Bottom - (float)(Math.Min(max, data[i]) / max * plot.Height));
            using (var area = new GraphicsPath())
            {
                area.AddCurve(pts, 0.5f);
                area.AddLine(pts[pts.Length - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                area.CloseFigure();
                var gr = new Rectangle(plot.X, plot.Y - 1, plot.Width, plot.Height + 2);
                using (var b = new LinearGradientBrush(gr, Color.FromArgb(Ui.Dark ? 80 : 60, Blue), Color.FromArgb(0, Blue), LinearGradientMode.Vertical)) g.FillPath(b, area);
            }
            using (var pen = new Pen(Blue, Ui.S(5) / 2f) { LineJoin = LineJoin.Round }) g.DrawCurve(pen, pts, 0.5f);
            var end = pts[pts.Length - 1];
            using (var b = new SolidBrush(Ui.Card)) g.FillEllipse(b, end.X - Ui.S(7), end.Y - Ui.S(7), Ui.S(14), Ui.S(14));
            using (var b = new SolidBrush(Blue)) g.FillEllipse(b, end.X - Ui.S(5), end.Y - Ui.S(5), Ui.S(10), Ui.S(10));
            // Callout with the live value
            var co = new Rectangle((int)end.X - Ui.S(84), Math.Max(plot.Y - Ui.S(18), (int)end.Y - Ui.S(62)), Ui.S(78), Ui.S(48));
            Ui.Shadow(g, co, Ui.S(10));
            using (var path = Ui.Round(co, Ui.S(10))) { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(Ui.Line)) g.DrawPath(p, path); }
            TextRenderer.DrawText(g, cpuNow.ToString("0") + "%", Ui.Bold, new Rectangle(co.X, co.Y + Ui.S(5), co.Width, Ui.S(20)), Ui.Text, C);
            TextRenderer.DrawText(g, "✓ live CPU", Ui.Small, new Rectangle(co.X, co.Y + Ui.S(25), co.Width, Ui.S(16)), Green, C);

            // Stats strip
            var s = new Rectangle(c.X + Ui.S(16), c.Bottom - statsH - Ui.S(14), c.Width - Ui.S(32), statsH);
            using (var path = Ui.Round(s, Ui.S(12))) using (var b = new SolidBrush(Ui.Navy ? Ui.Mix(Ui.Bg, Ui.Card, 0.45) : Ui.Bg)) g.FillPath(b, path);
            int half = s.Width / 2;
            Stat(g, new Rectangle(s.X, s.Y, half, s.Height), "Memory in use", memUsed, memLoad + "% of RAM   " + memDelta, memDelta.StartsWith("↑") ? Red : Green);
            using (var p = new Pen(Ui.Line)) g.DrawLine(p, s.X + half, s.Y + Ui.S(16), s.X + half, s.Bottom - Ui.S(16));
            Stat(g, new Rectangle(s.X + half, s.Y, half, s.Height), "Average CPU", cpuAvg, cpuDelta.Length > 0 ? cpuDelta : "collecting...", cpuDelta.StartsWith("↑") ? Red : Green);
        }

        static void Stat(Graphics g, Rectangle r, string label, string value, string note, Color tone)
        {
            int x = r.X + Ui.S(20), w = r.Width - Ui.S(30);
            TextRenderer.DrawText(g, label, Ui.Small, new Rectangle(x, r.Y + Ui.S(12), w, Ui.S(18)), Ui.Muted, L);
            TextRenderer.DrawText(g, value, Ui.Kpi, new Rectangle(x, r.Y + Ui.S(30), w, Ui.S(34)), Ui.Text, L);
            TextRenderer.DrawText(g, note, Ui.Small, new Rectangle(x, r.Y + Ui.S(66), w, Ui.S(18)), tone, L);
        }

        void Donut(Graphics g)
        {
            var c = donut;
            Card(g, c, false);
            Title(g, c, "Your Library", "Everything you have saved");
            Outline(g, ViewAll(c), "View all", "donutall");
            int total = lib.Sum();
            var colors = new[] { Green, Blue, Amber, Grey };
            int legendW = Math.Min(Ui.S(190), c.Width / 2 - Ui.S(10));
            int size = Math.Min(c.Height - Ui.S(120), c.Width - legendW - Ui.S(70));
            size = Math.Max(Ui.S(100), Math.Min(Ui.S(200), size));
            var ring = new Rectangle(c.X + Ui.S(30), c.Y + Ui.S(84) + (c.Height - Ui.S(104) - size) / 2, size, size);
            int thick = Math.Max(Ui.S(16), size / 7);
            var inner = Rectangle.Inflate(ring, -thick / 2, -thick / 2);
            using (var p = new Pen(Ui.Track, thick)) g.DrawEllipse(p, inner);
            if (total > 0)
            {
                float start = -90;
                for (int i = 0; i < 4; i++)
                {
                    if (lib[i] == 0) continue;
                    float sweep = 360f * lib[i] / total;
                    using (var p = new Pen(colors[i], thick)) g.DrawArc(p, inner, start, Math.Max(1f, sweep - 2f));
                    start += sweep;
                }
            }
            TextRenderer.DrawText(g, total.ToString("N0"), Ui.Kpi, new Rectangle(ring.X, ring.Y + ring.Height / 2 - Ui.S(24), ring.Width, Ui.S(32)), Ui.Text, C);
            TextRenderer.DrawText(g, "Total items", Ui.Small, new Rectangle(ring.X, ring.Y + ring.Height / 2 + Ui.S(8), ring.Width, Ui.S(16)), Ui.Muted, C);
            int lx = ring.Right + Ui.S(30), lw = c.Right - Ui.S(28) - lx, ly = ring.Y + ring.Height / 2 - Ui.S(68);
            for (int i = 0; i < 4; i++)
            {
                int yy = ly + i * Ui.S(36);
                using (var b = new SolidBrush(colors[i])) g.FillEllipse(b, lx, yy + Ui.S(7), Ui.S(11), Ui.S(11));
                TextRenderer.DrawText(g, libName[i], Ui.Base, new Rectangle(lx + Ui.S(22), yy, lw - Ui.S(70), Ui.S(26)), Ui.Text, L);
                TextRenderer.DrawText(g, lib[i].ToString("N0"), Ui.Bold, new Rectangle(lx, yy, lw, Ui.S(26)), Ui.Text, L | TextFormatFlags.Right);
            }
        }

        void Kpi(Graphics g, int i)
        {
            var r = kpi[i];
            Card(g, r, IsHot("kpi" + i));
            var tones = new[] { Blue, Amber, Green, Red };
            var ir = new Rectangle(r.X + Ui.S(22), r.Y + Ui.S(22), Ui.S(44), Ui.S(44));
            IconTile(g, ir, kpiGlyph[i], tones[i], 14f);
            int tx = ir.Right + Ui.S(16), tw = r.Right - tx - Ui.S(16);
            TextRenderer.DrawText(g, kpiLabel[i], Ui.Base, new Rectangle(tx, r.Y + Ui.S(20), tw, Ui.S(20)), Ui.Text, L);
            TextRenderer.DrawText(g, kpiValue[i].ToString("N0"), Ui.Kpi, new Rectangle(tx, r.Y + Ui.S(42), tw, Ui.S(34)), Ui.Text, L);
            TextRenderer.DrawText(g, kpiNote[i], Ui.Small, new Rectangle(r.X + Ui.S(22), r.Bottom - Ui.S(36), r.Width - Ui.S(40), Ui.S(18)), kpiTrend[i] > 0 ? Green : kpiTrend[i] < 0 ? Red : Ui.Muted, L);
        }

        void Storage(Graphics g)
        {
            var c = storage;
            Card(g, c, IsHot("storage"));
            Title(g, c, "Storage", "Free space on your drives");
            var d = driveC ?? DriveSnapshot.Read("C");
            int x = c.X + Ui.S(28), colW = Math.Max(Ui.S(120), c.Width * 45 / 100);
            string free = d.Used < 0 ? "-" : d.Value.Replace(" free", "");
            TextRenderer.DrawText(g, free, HugeFont, new Rectangle(x, c.Y + Ui.S(88), colW, Ui.S(44)), Blue, L);
            TextRenderer.DrawText(g, "Free on C:  " + d.Detail, Ui.Base, new Rectangle(x, c.Y + Ui.S(134), colW, Ui.S(20)), Ui.Muted, L);
            using (var p = new Pen(Ui.Line)) g.DrawLine(p, x, c.Y + Ui.S(170), x + colW - Ui.S(20), c.Y + Ui.S(170));
            var dd = driveD ?? DriveSnapshot.Read("D");
            TextRenderer.DrawText(g, "Used on C:", Ui.Small, new Rectangle(x, c.Y + Ui.S(186), colW, Ui.S(18)), Ui.Muted, L);
            TextRenderer.DrawText(g, d.Used < 0 ? "-" : (d.Used * 100).ToString("0.0") + "%", Ui.Kpi, new Rectangle(x, c.Y + Ui.S(206), colW, Ui.S(34)), Ui.Text, L);
            string dn = dd.Used < 0 ? "No D: drive" : "D: " + dd.Value;
            TextRenderer.DrawText(g, (d.Used > 0.9 ? "↑ almost full, clean up" : "✓ healthy") + "   ·   " + dn, Ui.Small, new Rectangle(x, c.Y + Ui.S(244), c.Width - Ui.S(56), Ui.S(18)), d.Used > 0.9 ? Red : Green, L);

            // Funnel: total -> used -> free (decorative tiers in the accent colour)
            int fx = c.X + colW + Ui.S(20), fw = c.Right - Ui.S(28) - fx;
            if (fw < Ui.S(100)) return;
            int top = c.Y + Ui.S(86), tierH = Ui.S(44), gap = Ui.S(6);
            string[] labels = { "Total", "Used", "Free" };
            for (int t = 0; t < 3; t++)
            {
                float wTop = fw * (1f - t * 0.2f), wBot = fw * (1f - (t + 1) * 0.2f) + Ui.S(10);
                float cx = fx + fw / 2f, yy = top + t * (tierH + gap);
                using (var path = new GraphicsPath())
                {
                    path.AddLine(cx - wTop / 2, yy, cx + wTop / 2, yy);
                    path.AddLine(cx + wTop / 2, yy, cx + wBot / 2, yy + tierH);
                    path.AddLine(cx + wBot / 2, yy + tierH, cx - wBot / 2, yy + tierH);
                    path.CloseFigure();
                    var shade = Ui.Mix(Ui.AccentFill, Color.White, 0.12 + t * 0.2);
                    using (var b = new LinearGradientBrush(new RectangleF(cx - wTop / 2, yy, wTop, tierH + 1), Ui.Mix(shade, Color.White, 0.18), shade, LinearGradientMode.Vertical)) g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, labels[t], Ui.Small, new Rectangle((int)(cx - wTop / 2), (int)yy, (int)wTop, tierH), Color.White, C);
            }
            int by = top + 3 * (tierH + gap);
            TextRenderer.DrawText(g, d.Detail.Replace("of ", "") + " total", Ui.Small, new Rectangle(fx, by, fw, Ui.S(18)), Ui.Muted, C);
        }

        void Apps(Graphics g)
        {
            var c = apps;
            Card(g, c, false);
            Title(g, c, "Top Memory Apps", "Apps using the most RAM right now");
            Outline(g, ViewAll(c), "View all", "appsall");
            int x = c.X + Ui.S(24), w = c.Width - Ui.S(48), y = c.Y + Ui.S(82), rh = Ui.S(58);
            if (top.Count == 0) { TextRenderer.DrawText(g, "Collecting...", Ui.Base, new Rectangle(x, y, w, Ui.S(40)), Ui.Muted, L); return; }
            // Wide card: two columns; show as many rows as the card height holds.
            int cols = w > Ui.S(820) ? 2 : 1, cgap = Ui.S(28);
            int colW = (w - cgap * (cols - 1)) / cols;
            int perCol = Math.Max(1, (c.Bottom - y - Ui.S(10)) / rh);
            int count = Math.Min(top.Count, perCol * cols);
            int y0 = y;
            for (int i = 0; i < count; i++)
            {
                var a = top[i];
                int col = i / perCol;
                x = c.X + Ui.S(24) + col * (colW + cgap); y = y0 + (i % perCol) * rh;
                var row = new Rectangle(x, y, colW, rh);
                var av = new Rectangle(x, row.Y + (rh - Ui.S(40)) / 2, Ui.S(40), Ui.S(40));
                var tone = new[] { Blue, Green, Amber, Color.FromArgb(139, 92, 246) }[i % 4];
                using (var b = new LinearGradientBrush(av, Ui.Mix(tone, Color.White, 0.25), tone, LinearGradientMode.ForwardDiagonal)) g.FillEllipse(b, av);
                string name = string.IsNullOrEmpty(a.Key) ? "?" : a.Key;
                TextRenderer.DrawText(g, name.Substring(0, 1).ToUpperInvariant(), Ui.Bold, av, Color.White, C);
                int tx = av.Right + Ui.S(14), statusW = Ui.S(86), memW = Ui.S(84);
                int textW = row.Right - statusW - memW - Ui.S(16) - tx;
                int n; procCount.TryGetValue(a.Key ?? "", out n);
                TextRenderer.DrawText(g, name, Ui.Bold, new Rectangle(tx, row.Y + Ui.S(9), textW, Ui.S(20)), Ui.Text, L);
                TextRenderer.DrawText(g, n + (n == 1 ? " process" : " processes"), Ui.Small, new Rectangle(tx, row.Y + Ui.S(30), textW, Ui.S(18)), Ui.Muted, L);
                TextRenderer.DrawText(g, Fmt.Bytes(a.Value), Ui.Base, new Rectangle(row.Right - statusW - memW - Ui.S(8), row.Y, memW, rh), Ui.Text, L | TextFormatFlags.Right);
                long gb = 1L << 30;
                string status = a.Value > 2 * gb ? "Heavy" : a.Value > gb / 2 ? "Normal" : "Light";
                Color st = status == "Heavy" ? Amber : status == "Normal" ? Blue : Green;
                Pill(g, new Rectangle(row.Right - statusW, row.Y + (rh - Ui.S(26)) / 2, statusW, Ui.S(26)), status, st);
                if (i % perCol < perCol - 1 && i < count - 1) using (var p = new Pen(Ui.Line)) g.DrawLine(p, tx, row.Bottom, row.Right, row.Bottom);
            }
        }

        // ---------- mouse ----------
        Hot At(Point p) { for (int i = hots.Count - 1; i >= 0; i--) if (hots[i].R.Contains(p)) return hots[i]; return null; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = At(e.Location);
            if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { if (hover != null) { hover = null; Invalidate(); } base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var h = At(e.Location);
            if (h != null) try { h.Run(); } catch (Exception ex) { App.Log("Dashboard: " + ex.Message); }
        }
    }
}
