using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Nexus-style home dashboard: title + actions, KPI cards with badges, live CPU/RAM bar chart,
    // clipboard week bars, top memory apps table and a library donut. One owner-drawn control.
    class NexusBoard : Control
    {
        class Hot { public Rectangle R; public string Key; public Action Run; }

        readonly List<Hot> hots = new List<Hot>();
        Hot hover;
        int width = 800;

        // Layout rectangles
        Rectangle titleRect, btnPrimary, btnSecond, btnThird, chartCard, weekCard, appsCard, donutCard;
        readonly Rectangle[] kpi = new Rectangle[4];

        // Data
        readonly string[] kpiLabel = { "CPU", "Memory", "Disk C", "Disk D" };
        readonly string[] kpiGlyph = { "", "", "", "" };
        readonly string[] kpiPage = { "system:procs", "system:procs", "system:drives", "system:drives" };
        readonly string[] kpiValue = { "-", "-", "-", "-" }, kpiSub = { "", "", "", "" }, kpiBadge = { "", "", "", "" };
        readonly bool[] kpiBad = new bool[4];
        double[] cpu = new double[0], ram = new double[0];
        readonly int[] week = new int[7];
        readonly string[] weekDay = new string[7];
        List<KeyValuePair<string, long>> apps = new List<KeyValuePair<string, long>>();
        long memTotal = 1;
        readonly int[] lib = new int[4];
        static readonly string[] libName = { "Notes", "Prompts", "Clips", "Tasks" };

        public NexusBoard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "Dashboard";
        }

        // ---------- data ----------
        public void RefreshData()
        {
            cpu = ProcessSampler.CpuHistory.ToArray();
            ram = ProcessSampler.RamHistory.ToArray();
            double c = ProcessSampler.CpuTotal; int m = ProcessSampler.MemLoad;
            kpiValue[0] = c.ToString("0") + "%"; kpiSub[0] = "Average " + (cpu.Length > 0 ? cpu.Average().ToString("0") : "0") + "% last 2 min";
            kpiBadge[0] = Delta(cpu); kpiBad[0] = c > 85;
            kpiValue[1] = m + "%"; kpiSub[1] = Fmt.Bytes((long)(ProcessSampler.MemTotal - ProcessSampler.MemAvail)) + " of " + Fmt.Bytes((long)ProcessSampler.MemTotal);
            kpiBadge[1] = Delta(ram); kpiBad[1] = m > 85;
            for (int i = 0; i < 2; i++)
            {
                var d = DriveSnapshot.Read(i == 0 ? "C" : "D");
                kpiValue[2 + i] = d.Used < 0 ? "-" : d.Value.Replace(" free", "");
                kpiSub[2 + i] = d.Used < 0 ? "Not available" : "free " + d.Detail;
                kpiBadge[2 + i] = d.Used < 0 ? "" : (d.Used * 100).ToString("0") + "% used";
                kpiBad[2 + i] = d.Used > 0.9;
            }
            var today = DateTime.Today;
            for (int i = 0; i < 7; i++)
            {
                var day = today.AddDays(i - 6);
                long a = day.Ticks, b = day.AddDays(1).Ticks;
                week[i] = Store.Clips.Count(x => x.Time >= a && x.Time < b);
                weekDay[i] = day.ToString("ddd");
            }
            apps = ProcessSampler.Last.GroupBy(p => p.Display).Select(g => new KeyValuePair<string, long>(g.Key, g.Sum(p => p.Mem))).OrderByDescending(k => k.Value).Take(5).ToList();
            memTotal = Math.Max(1, (long)ProcessSampler.MemTotal);
            lib[0] = Store.Notes.Count; lib[1] = Store.Prompts.Count; lib[2] = Store.Clips.Count; lib[3] = Store.Tasks.Count(t => !t.Done);
            Invalidate();
        }

        static string Delta(double[] h)
        {
            if (h.Length < 6) return "";
            double now = h.Skip(h.Length - 3).Average(), before = h.Take(Math.Min(10, h.Length - 3)).Average();
            double d = now - before;
            return (d >= 0 ? "+" : "") + d.ToString("0.0") + "%";
        }

        // ---------- layout ----------
        public int Measure(int w)
        {
            width = w;
            hots.Clear();
            int gap = Ui.S(16), y = 0;
            titleRect = new Rectangle(0, 0, w, Ui.S(56));
            int bh = Ui.S(38);
            int bw1 = TextRenderer.MeasureText("Launch terminals", Ui.Bold).Width + Ui.S(48);
            int bw2 = TextRenderer.MeasureText("New task", Ui.Bold).Width + Ui.S(46);
            int bw3 = TextRenderer.MeasureText("Search", Ui.Bold).Width + Ui.S(46);
            bool roomy = w > Ui.S(640);
            btnPrimary = roomy ? new Rectangle(w - bw1, Ui.S(8), bw1, bh) : Rectangle.Empty;
            btnSecond = roomy ? new Rectangle(btnPrimary.X - Ui.S(10) - bw2, Ui.S(8), bw2, bh) : Rectangle.Empty;
            btnThird = roomy && w > Ui.S(860) ? new Rectangle(btnSecond.X - Ui.S(10) - bw3, Ui.S(8), bw3, bh) : Rectangle.Empty;
            if (roomy)
            {
                hots.Add(new Hot { R = btnPrimary, Key = "launch", Run = () => Term.LaunchCurrent() });
                hots.Add(new Hot { R = btnSecond, Key = "newtask", Run = () => QuickTaskForm.Open() });
                if (!btnThird.IsEmpty) hots.Add(new Hot { R = btnThird, Key = "search", Run = () => GlobalSearch.Open(FindForm()) });
            }
            y = titleRect.Bottom + Ui.S(8);

            int cols = w >= Ui.S(1000) ? 4 : w >= Ui.S(520) ? 2 : 1;
            int kw = (w - gap * (cols - 1)) / cols, kh = Ui.S(138);
            for (int i = 0; i < 4; i++)
            {
                int c = i % cols, r = i / cols;
                kpi[i] = new Rectangle(c * (kw + gap), y + r * (kh + gap), kw, kh);
                string page = kpiPage[i];
                hots.Add(new Hot { R = kpi[i], Key = "kpi" + i, Run = () => App.I.ShowMain(page) });
            }
            y += ((4 + cols - 1) / cols) * (kh + gap);

            bool two = w >= Ui.S(900);
            int big = two ? (w - gap) * 62 / 100 : w, small = two ? w - gap - big : w;
            int ch = Ui.S(300);
            chartCard = new Rectangle(0, y, big, ch);
            weekCard = two ? new Rectangle(big + gap, y, small, ch) : new Rectangle(0, y + ch + gap, w, ch);
            y = weekCard.Bottom + gap;
            hots.Add(new Hot { R = new Rectangle(chartCard.X, chartCard.Y, chartCard.Width, Ui.S(52)), Key = "chart", Run = () => App.I.ShowMain("system:procs") });
            hots.Add(new Hot { R = new Rectangle(weekCard.X, weekCard.Y, weekCard.Width, Ui.S(52)), Key = "week", Run = () => App.I.ShowMain("clips") });

            int th = Ui.S(70) + 5 * Ui.S(44) + Ui.S(10);
            appsCard = new Rectangle(0, y, big, th);
            donutCard = two ? new Rectangle(big + gap, y, small, th) : new Rectangle(0, y + th + gap, w, th);
            y = donutCard.Bottom + gap;
            hots.Add(new Hot { R = new Rectangle(appsCard.X, appsCard.Y, appsCard.Width, appsCard.Height), Key = "apps", Run = () => App.I.ShowMain("system:procs") });
            return y;
        }

        // ---------- paint ----------
        static readonly TextFormatFlags L = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // Title + subtitle
            int hour = DateTime.Now.Hour;
            string greet = hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
            TextRenderer.DrawText(g, "Dashboard", Ui.Title, new Rectangle(0, Ui.S(2), titleRect.Width / 2, Ui.S(30)), Ui.Text, L);
            TextRenderer.DrawText(g, greet + "  ·  " + DateTime.Now.ToString("dddd, d MMMM yyyy"), Ui.Base, new Rectangle(0, Ui.S(32), Math.Max(Ui.S(100), (btnThird.IsEmpty ? btnSecond.IsEmpty ? width : btnSecond.X : btnThird.X) - Ui.S(10)), Ui.S(20)), Ui.Muted, L);
            if (!btnThird.IsEmpty) Button(g, btnThird, "", "Search", false, "search");
            if (!btnSecond.IsEmpty) Button(g, btnSecond, "", "New task", false, "newtask");
            if (!btnPrimary.IsEmpty) Button(g, btnPrimary, "", "Launch terminals", true, "launch");

            for (int i = 0; i < 4; i++) if (kpi[i].IntersectsWith(e.ClipRectangle)) Kpi(g, i);
            if (chartCard.IntersectsWith(e.ClipRectangle)) Chart(g);
            if (weekCard.IntersectsWith(e.ClipRectangle)) Week(g);
            if (appsCard.IntersectsWith(e.ClipRectangle)) Apps(g);
            if (donutCard.IntersectsWith(e.ClipRectangle)) Donut(g);
        }

        bool IsHot(string key) { return hover != null && hover.Key == key; }

        void Button(Graphics g, Rectangle r, string glyph, string text, bool primary, string key)
        {
            bool hot = IsHot(key);
            using (var path = Ui.Round(r, Ui.S(10)))
            {
                var fill = primary ? (hot ? Ui.AccentHover : Ui.AccentFill) : hot ? Ui.HoverBg : Ui.Card;
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (!primary) using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
            }
            var fore = primary ? Color.White : Ui.Text;
            int tw = TextRenderer.MeasureText(text, Ui.Bold).Width;
            int x = r.X + (r.Width - tw - Ui.S(22)) / 2;
            TextRenderer.DrawText(g, glyph, Ui.Icon(10.5f), new Rectangle(x, r.Y, Ui.S(16), r.Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, Ui.Bold, new Rectangle(x + Ui.S(22), r.Y, tw + Ui.S(4), r.Height), fore, L);
        }

        void Card(Graphics g, Rectangle r, bool hot)
        {
            var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var path = Ui.Round(rr, Ui.S(14)))
            {
                using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path);
                using (var p = new Pen(hot ? Ui.Mix(Ui.Line, Ui.Accent, 0.6) : Ui.Line)) g.DrawPath(p, path);
            }
        }

        void CardTitle(Graphics g, Rectangle card, string title, string sub)
        {
            TextRenderer.DrawText(g, title, Ui.Bold, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(14), card.Width - Ui.S(40), Ui.S(22)), Ui.Text, L);
            if (sub != null) TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(36), card.Width - Ui.S(40), Ui.S(16)), Ui.Muted, L);
        }

        void Badge(Graphics g, string text, int right, int y, Color tone)
        {
            if (string.IsNullOrEmpty(text)) return;
            int w = TextRenderer.MeasureText(text, Ui.Small).Width + Ui.S(14), h = Ui.S(22);
            var r = new Rectangle(right - w, y, w, h);
            using (var path = Ui.Round(r, h / 2)) using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 55 : 28, tone))) g.FillPath(b, path);
            TextRenderer.DrawText(g, text, Ui.Small, r, tone, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        static Color Good { get { return Ui.Dark ? Color.FromArgb(52, 211, 153) : Color.FromArgb(5, 150, 105); } }
        static Color Bad { get { return Ui.Dark ? Color.FromArgb(251, 113, 133) : Color.FromArgb(225, 29, 72); } }

        void Kpi(Graphics g, int i)
        {
            var r = kpi[i];
            Card(g, r, IsHot("kpi" + i));
            var tint = Ui.Series(i == 0 ? 0 : i == 1 ? 1 : i == 2 ? 4 : 2);
            int pad = Ui.S(20), ic = Ui.S(36);
            var ir = new Rectangle(r.X + pad, r.Y + Ui.S(18), ic, ic);
            using (var path = Ui.Round(ir, Ui.S(10))) using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 60 : 28, tint))) g.FillPath(b, path);
            TextRenderer.DrawText(g, kpiGlyph[i], Ui.Icon(13f), ir, tint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, kpiLabel[i], Ui.Base, new Rectangle(ir.Right + Ui.S(12), ir.Y, r.Width - ic - pad * 2 - Ui.S(40), ic), Ui.Muted, L);
            TextRenderer.DrawText(g, "", Ui.Icon(10f), new Rectangle(r.Right - pad - Ui.S(18), ir.Y, Ui.S(18), ic), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            var vr = new Rectangle(r.X + pad, ir.Bottom + Ui.S(12), r.Width - pad * 2, Ui.S(36));
            TextRenderer.DrawText(g, kpiValue[i], Ui.Kpi, vr, Ui.Text, L);
            int vw = TextRenderer.MeasureText(kpiValue[i], Ui.Kpi).Width;
            bool rising = kpiBadge[i].StartsWith("+");
            Color tone = i < 2 ? (kpiBad[i] ? Bad : rising ? Bad : Good) : (kpiBad[i] ? Bad : Good);
            if (i < 2 && kpiBadge[i].Length > 0 && !kpiBad[i] && rising && kpiBadge[i] == "+0.0%") tone = Good;
            if (vw + Ui.S(110) < vr.Width) Badge(g, kpiBadge[i], vr.X + vw + Ui.S(12) + TextRenderer.MeasureText(kpiBadge[i], Ui.Small).Width + Ui.S(14), vr.Y + Ui.S(8), tone);
            TextRenderer.DrawText(g, kpiSub[i], Ui.Small, new Rectangle(r.X + pad, vr.Bottom + Ui.S(6), r.Width - pad * 2, Ui.S(18)), Ui.Muted, L);
        }

        void Legend(Graphics g, Rectangle card, string[] names, Color[] colors)
        {
            int x = card.Right - Ui.S(20);
            for (int i = names.Length - 1; i >= 0; i--)
            {
                int tw = TextRenderer.MeasureText(names[i], Ui.Small).Width;
                x -= tw;
                TextRenderer.DrawText(g, names[i], Ui.Small, new Rectangle(x, card.Y + Ui.S(16), tw + 2, Ui.S(18)), Ui.Muted, L);
                x -= Ui.S(14);
                using (var b = new SolidBrush(colors[i])) g.FillEllipse(b, x, card.Y + Ui.S(21), Ui.S(8), Ui.S(8));
                x -= Ui.S(14);
            }
        }

        void Grid(Graphics g, Rectangle plot, int lines, Func<int, string> label)
        {
            using (var pen = new Pen(Ui.Line) { DashStyle = DashStyle.Dash })
                for (int k = 0; k <= lines; k++)
                {
                    int yy = plot.Bottom - k * plot.Height / lines;
                    g.DrawLine(pen, plot.X, yy, plot.Right, yy);
                    if (label != null) TextRenderer.DrawText(g, label(k), Ui.Small, new Rectangle(plot.X - Ui.S(40), yy - Ui.S(8), Ui.S(34), Ui.S(16)), Ui.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
        }

        void Chart(Graphics g)
        {
            var c = chartCard;
            Card(g, c, IsHot("chart"));
            CardTitle(g, c, "Live CPU & RAM", "Last " + (Math.Max(cpu.Length, ram.Length) * 2) + " seconds, every 2 s");
            var a = Ui.Series(0); var b2 = Ui.Series(1);
            Legend(g, c, new[] { "CPU", "RAM" }, new[] { a, b2 });
            var plot = new Rectangle(c.X + Ui.S(56), c.Y + Ui.S(70), c.Width - Ui.S(76), c.Height - Ui.S(100));
            Grid(g, plot, 4, k => (k * 25) + "%");
            int n = Math.Min(30, Math.Max(cpu.Length, ram.Length));
            if (n == 0) { TextRenderer.DrawText(g, "Collecting samples...", Ui.Base, plot, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); return; }
            float slot = plot.Width / 30f, bw = Math.Max(2f, slot * 0.28f);
            for (int i = 0; i < n; i++)
            {
                double cv = cpu.Length > 0 ? cpu[Math.Max(0, cpu.Length - n + i)] : 0, rv = ram.Length > 0 ? ram[Math.Max(0, ram.Length - n + i)] : 0;
                float x = plot.X + (30 - n + i) * slot + slot / 2f;
                Bar(g, x - bw - 1, plot, cv, bw, a);
                Bar(g, x + 1, plot, rv, bw, b2);
            }
        }

        static void Bar(Graphics g, float x, Rectangle plot, double pct, float w, Color color)
        {
            float h = (float)(Math.Max(0, Math.Min(100, pct)) * plot.Height / 100.0);
            if (h < 1) return;
            var r = new RectangleF(x, plot.Bottom - h, w, h);
            float rad = Math.Min(w / 2f, h);
            using (var path = new GraphicsPath())
            {
                path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
                path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
                path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
                path.CloseFigure();
                using (var b = new SolidBrush(color)) g.FillPath(b, path);
            }
        }

        void Week(Graphics g)
        {
            var c = weekCard;
            Card(g, c, IsHot("week"));
            int total = week.Sum();
            CardTitle(g, c, "Clipboard this week", total.ToString("N0") + " items copied in 7 days");
            var plot = new Rectangle(c.X + Ui.S(24), c.Y + Ui.S(78), c.Width - Ui.S(48), c.Height - Ui.S(118));
            int max = Math.Max(1, week.Max());
            float slot = plot.Width / 7f, bw = Math.Min(Ui.S(34), slot * 0.62f);
            for (int i = 0; i < 7; i++)
            {
                float x = plot.X + i * slot + (slot - bw) / 2;
                var track = new Rectangle((int)x, plot.Y, (int)bw, plot.Height);
                using (var path = Ui.Round(track, (int)(bw / 3))) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
                int h = (int)(plot.Height * week[i] / (double)max);
                if (h > 0)
                {
                    var r = new Rectangle((int)x, plot.Bottom - h, (int)bw, h);
                    using (var path = Ui.Round(r, Math.Min((int)(bw / 3), h / 2))) using (var b = new SolidBrush(i == 6 ? Ui.AccentFill : Ui.Mix(Ui.AccentFill, Ui.Card, 0.55))) g.FillPath(b, path);
                }
                if (i == 6 && week[i] > 0)
                {
                    string v = week[i].ToString();
                    var tr = new Rectangle((int)(x + bw / 2) - Ui.S(20), plot.Bottom - h - Ui.S(26), Ui.S(40), Ui.S(20));
                    using (var path = Ui.Round(tr, Ui.S(6))) using (var b = new SolidBrush(Ui.Text)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, v, Ui.Small, tr, Ui.Card, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                TextRenderer.DrawText(g, weekDay[i] ?? "", Ui.Small, new Rectangle((int)(plot.X + i * slot), plot.Bottom + Ui.S(8), (int)slot, Ui.S(18)), i == 6 ? Ui.Text : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }
        }

        void Apps(Graphics g)
        {
            var c = appsCard;
            Card(g, c, IsHot("apps"));
            CardTitle(g, c, "Top memory apps", "Click to open the system monitor");
            int x = c.X + Ui.S(20), w = c.Width - Ui.S(40), y = c.Y + Ui.S(62);
            int col2 = x + w * 48 / 100, col3 = x + w * 70 / 100;
            using (var b = new SolidBrush(Ui.Bg)) g.FillRectangle(b, c.X + 1, y, c.Width - 2, Ui.S(30));
            TextRenderer.DrawText(g, "APPLICATION", Ui.Small, new Rectangle(x, y, col2 - x, Ui.S(30)), Ui.Muted, L);
            TextRenderer.DrawText(g, "MEMORY", Ui.Small, new Rectangle(col2, y, col3 - col2, Ui.S(30)), Ui.Muted, L);
            TextRenderer.DrawText(g, "SHARE OF RAM", Ui.Small, new Rectangle(col3, y, x + w - col3, Ui.S(30)), Ui.Muted, L);
            y += Ui.S(34);
            if (apps.Count == 0) { TextRenderer.DrawText(g, "Collecting...", Ui.Base, new Rectangle(x, y, w, Ui.S(40)), Ui.Muted, L); return; }
            for (int i = 0; i < apps.Count; i++)
            {
                var a = apps[i];
                var row = new Rectangle(x, y, w, Ui.S(44));
                var tint = Ui.Series(i);
                var dot = new Rectangle(x, row.Y + Ui.S(8), Ui.S(28), Ui.S(28));
                using (var path = Ui.Round(dot, Ui.S(8))) using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 60 : 30, tint))) g.FillPath(b, path);
                string initial = string.IsNullOrEmpty(a.Key) ? "?" : a.Key.Substring(0, 1).ToUpperInvariant();
                TextRenderer.DrawText(g, initial, Ui.Bold, dot, tint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, a.Key, Ui.Bold, new Rectangle(dot.Right + Ui.S(10), row.Y, col2 - dot.Right - Ui.S(16), row.Height), Ui.Text, L);
                TextRenderer.DrawText(g, Fmt.Bytes(a.Value), Ui.Base, new Rectangle(col2, row.Y, col3 - col2, row.Height), Ui.Text, L);
                double share = a.Value / (double)memTotal;
                int pw = Math.Max(Ui.S(40), x + w - col3 - Ui.S(56));
                var track = new Rectangle(col3, row.Y + row.Height / 2 - Ui.S(4), pw, Ui.S(8));
                using (var path = Ui.Round(track, Ui.S(4))) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
                int fw = Math.Max(Ui.S(8), (int)(pw * Math.Min(1, share * 3)));
                using (var path = Ui.Round(new Rectangle(track.X, track.Y, fw, track.Height), Ui.S(4))) using (var b = new SolidBrush(tint)) g.FillPath(b, path);
                TextRenderer.DrawText(g, (share * 100).ToString("0.0") + "%", Ui.Small, new Rectangle(track.Right + Ui.S(8), row.Y, Ui.S(50), row.Height), Ui.Muted, L);
                if (i < apps.Count - 1) using (var p = new Pen(Ui.Line)) g.DrawLine(p, x, row.Bottom, x + w, row.Bottom);
                y += row.Height;
            }
        }

        void Donut(Graphics g)
        {
            var c = donutCard;
            Card(g, c, false);
            int total = lib.Sum();
            CardTitle(g, c, "Your library", total.ToString("N0") + " items saved");
            int size = Math.Min(c.Width - Ui.S(60), c.Height - Ui.S(150));
            size = Math.Max(Ui.S(90), Math.Min(Ui.S(190), size));
            var ring = new Rectangle(c.X + (c.Width - size) / 2, c.Y + Ui.S(66), size, size);
            int thick = Math.Max(Ui.S(14), size / 7);
            var inner = Rectangle.Inflate(ring, -thick / 2, -thick / 2);
            using (var p = new Pen(Ui.Track, thick)) g.DrawEllipse(p, inner);
            if (total > 0)
            {
                float start = -90;
                for (int i = 0; i < 4; i++)
                {
                    if (lib[i] == 0) continue;
                    float sweep = 360f * lib[i] / total;
                    using (var p = new Pen(Ui.Series(i), thick) { StartCap = LineCap.Flat, EndCap = LineCap.Flat }) g.DrawArc(p, inner, start, Math.Max(1f, sweep - 1.5f));
                    start += sweep;
                }
            }
            TextRenderer.DrawText(g, total.ToString("N0"), Ui.Kpi, new Rectangle(ring.X, ring.Y + ring.Height / 2 - Ui.S(22), ring.Width, Ui.S(30)), Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "items", Ui.Small, new Rectangle(ring.X, ring.Y + ring.Height / 2 + Ui.S(8), ring.Width, Ui.S(16)), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            // Legend 2x2
            int ly = ring.Bottom + Ui.S(16), colW = (c.Width - Ui.S(40)) / 2;
            for (int i = 0; i < 4; i++)
            {
                int lx = c.X + Ui.S(20) + (i % 2) * colW, yy = ly + (i / 2) * Ui.S(24);
                using (var b = new SolidBrush(Ui.Series(i))) g.FillEllipse(b, lx, yy + Ui.S(5), Ui.S(9), Ui.S(9));
                TextRenderer.DrawText(g, libName[i], Ui.Small, new Rectangle(lx + Ui.S(16), yy, colW / 2, Ui.S(20)), Ui.Muted, L);
                TextRenderer.DrawText(g, lib[i].ToString("N0"), Ui.Bold, new Rectangle(lx + Ui.S(16), yy, colW - Ui.S(24), Ui.S(20)), Ui.Text, L | TextFormatFlags.Right);
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
