using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Home dashboard (SerQ / Nexus designs). Every number on it is real and every element does something:
    // date + status line, CPU / memory history on a fixed 0-100 % scale, the next tasks, today's counts,
    // every fixed drive, and the apps using the most memory (real icons, share of RAM). One owner-drawn control.
    class NexusBoard : Control
    {
        class Hot { public Rectangle R; public string Key; public Action Run; }

        readonly List<Hot> hots = new List<Hot>();
        Hot hover;

        Rectangle header, btnNew, btnTerm, bell, gear, overview, next, today, storage, apps;
        readonly Rectangle[] todayCol = new Rectangle[4];
        readonly List<Rectangle> taskRows = new List<Rectangle>();

        // Data
        double[] cpu = new double[0], ram = new double[0];
        double cpuNow, cpuPeak; int memLoad; string memUsed = "-", memTotal = "-";
        int dueToday, overdue;
        List<TaskItem> upNext = new List<TaskItem>();
        readonly string[] todayLabel = { "Due today", "Copied today", "Files opened today", "Tagged urgent" };
        readonly string[] todayPage = { "tasks", "clips", "activity", "tagged" };
        readonly int[] todayValue = new int[4];
        readonly string[] todayNote = { "", "", "", "" };
        readonly bool[] todayAlert = new bool[4];
        List<DriveSnapshot> drives = new List<DriveSnapshot>();
        class AppRow { public string Name, Path; public long Mem; public int Count; }
        List<AppRow> topApps = new List<AppRow>();

        public NexusBoard()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "Dashboard";
        }

        // ---------- data ----------
        bool dataLoaded;
        // live = only the sampler values (every 2 s); data = also the store facts (only when the store changed).
        public void RefreshData() { RefreshData(true); }
        public void RefreshData(bool data)
        {
            cpu = ProcessSampler.CpuHistory.ToArray();
            ram = ProcessSampler.RamHistory.ToArray();
            cpuNow = ProcessSampler.CpuTotal; memLoad = ProcessSampler.MemLoad;
            cpuPeak = cpu.Length > 0 ? cpu.Max() : cpuNow;
            memUsed = Fmt.Bytes((long)(ProcessSampler.MemTotal - ProcessSampler.MemAvail));
            memTotal = Fmt.Bytes((long)ProcessSampler.MemTotal);
            drives = DriveSnapshot.All();
            topApps = ProcessSampler.Last.GroupBy(p => p.Display ?? "")
                .Select(g => new AppRow { Name = g.Key, Mem = g.Sum(p => p.Mem), Count = g.Count(), Path = g.Select(p => p.Path).FirstOrDefault(p => !string.IsNullOrEmpty(p)) })
                .OrderByDescending(a => a.Mem).Take(12).ToList();
            if (data || !dataLoaded) { dataLoaded = true; RefreshStoreData(); }
            Invalidate();
        }

        void RefreshStoreData()
        {
            var facts = DashboardFacts.Read(Store.Now());
            long today0 = DateTime.Today.Ticks, tomorrow = DateTime.Today.AddDays(1).Ticks;
            overdue = Store.Tasks.Count(t => TaskParse.Overdue(t));
            dueToday = Store.Tasks.Count(t => !t.Done && t.Due >= today0 && t.Due < tomorrow && !TaskParse.Overdue(t));
            // Next tasks: dated ones by due time (overdue first), then undated by priority.
            var open = Store.Tasks.Where(t => !t.Done).ToList();
            upNext = open.Where(t => t.Due > 0).OrderBy(t => t.Due)
                .Concat(open.Where(t => t.Due == 0).OrderByDescending(t => t.Priority).ThenByDescending(t => t.Created))
                .Take(12).ToList();

            todayValue[0] = dueToday + overdue;
            todayNote[0] = overdue > 0 ? overdue + " overdue" : "Nothing overdue"; todayAlert[0] = overdue > 0;
            todayValue[1] = facts.ClipsToday;
            int pinned = Store.Clips.Count(c => c.Pinned);
            todayNote[1] = pinned + " pinned  ·  " + Store.Clips.Count.ToString("N0") + " in history";
            todayValue[2] = facts.FilesToday;
            var last = Store.Activity.Where(a => a.Event == "Opened").OrderByDescending(a => a.Time).FirstOrDefault();
            todayNote[2] = !Store.Settings.ActivityOn ? "File activity logging is off" : last != null ? "Last: " + ActivityMonitor.SafeName(last.Path) : "No files yet";
            todayValue[3] = facts.Urgent; todayAlert[3] = false;
            todayNote[3] = Store.Tagged.Count + " tagged in total";
        }

        // ---------- layout ----------
        // Lays out for a width (a small inner margin keeps the card shadows visible) and returns the height.
        public int Measure(int full)
        {
            int m = Ui.S(8);
            int h = Arrange(Math.Max(Ui.S(200), full - 2 * m));
            var off = new Size(m, m);
            Func<Rectangle, Rectangle> o = r => r.Width > 0 ? new Rectangle(r.Location + off, r.Size) : r;
            header = o(header); btnNew = o(btnNew); btnTerm = o(btnTerm); bell = o(bell); gear = o(gear);
            overview = o(overview); next = o(next); today = o(today); storage = o(storage); apps = o(apps);
            for (int i = 0; i < 4; i++) todayCol[i] = o(todayCol[i]);
            for (int i = 0; i < taskRows.Count; i++) taskRows[i] = o(taskRows[i]);
            foreach (var hot in hots) hot.R = new Rectangle(hot.R.Location + off, hot.R.Size);
            return h + 2 * m;
        }

        int Arrange(int w)
        {
            hots.Clear(); taskRows.Clear();
            int gap = Ui.S(18);
            header = new Rectangle(0, 0, w, Ui.S(64));
            int bh = Ui.S(38), c = Ui.S(38);
            gear = new Rectangle(w - c, Ui.S(10), c, c);
            bell = new Rectangle(gear.X - Ui.S(10) - c, Ui.S(10), c, c);
            int bw = TextRenderer.MeasureText("New task", Ui.Bold).Width + Ui.S(48);
            int tw = TextRenderer.MeasureText("Terminals", Ui.Bold).Width + Ui.S(48);
            bool roomy = w > Ui.S(700);
            btnNew = roomy ? new Rectangle(bell.X - Ui.S(14) - bw, Ui.S(10), bw, bh) : Rectangle.Empty;
            btnTerm = roomy && w > Ui.S(900) ? new Rectangle(btnNew.X - Ui.S(10) - tw, Ui.S(10), tw, bh) : Rectangle.Empty;
            hots.Add(new Hot { R = bell, Key = "bell", Run = () => App.I.ShowMain("tasks") });
            hots.Add(new Hot { R = gear, Key = "gear", Run = () => App.I.ShowSettings(FindForm()) });
            if (roomy) hots.Add(new Hot { R = btnNew, Key = "new", Run = () => QuickTaskForm.Open() });
            if (btnTerm.Width > 0) hots.Add(new Hot { R = btnTerm, Key = "term", Run = () => App.I.ShowMain("terminals") });
            int y = header.Bottom + Ui.S(8);

            bool two = w >= Ui.S(900);
            int left = two ? (w - gap) * 60 / 100 : w, right = two ? w - gap - left : w;
            int h1 = Ui.S(320);
            overview = new Rectangle(0, y, left, h1);
            next = two ? new Rectangle(left + gap, y, right, h1) : new Rectangle(0, y + h1 + gap, w, h1);
            y = next.Bottom + gap;
            hots.Add(new Hot { R = new Rectangle(overview.X, overview.Y, overview.Width, Ui.S(64)), Key = "overview", Run = () => App.I.ShowMain("system:procs") });
            hots.Add(new Hot { R = LinkRect(next, "All tasks"), Key = "tasksall", Run = () => App.I.ShowMain("tasks") });
            int rh = Ui.S(46), ry = next.Y + Ui.S(70);
            for (int i = 0; ry + rh <= next.Bottom - Ui.S(12); i++, ry += rh)
            {
                var r = new Rectangle(next.X + Ui.S(12), ry, next.Width - Ui.S(24), rh);
                taskRows.Add(r);
                int k = i;
                hots.Add(new Hot { R = r, Key = "task" + i, Run = () => OpenTask(k) });
            }

            int cols = w >= Ui.S(640) ? 4 : 2, rows = 4 / cols, th = Ui.S(100);
            today = new Rectangle(0, y, w, rows * th);
            int cw = w / cols;
            for (int i = 0; i < 4; i++)
            {
                todayCol[i] = new Rectangle((i % cols) * cw, y + (i / cols) * th, i % cols == cols - 1 ? w - (cols - 1) * cw : cw, th);
                string page = todayPage[i];
                hots.Add(new Hot { R = todayCol[i], Key = "today" + i, Run = () => App.I.ShowMain(page) });
            }
            y = today.Bottom + gap;

            int sl = two ? (w - gap) * 42 / 100 : w, sr = two ? w - gap - sl : w;
            int h3 = Ui.S(300);
            storage = new Rectangle(0, y, sl, h3);
            apps = two ? new Rectangle(sl + gap, y, sr, h3) : new Rectangle(0, y + h3 + gap, w, h3);
            y = apps.Bottom + gap;
            hots.Add(new Hot { R = LinkRect(storage, "Drives & cleanup"), Key = "storageall", Run = () => App.I.ShowMain("system:drives") });
            hots.Add(new Hot { R = LinkRect(apps, "Processes"), Key = "appsall", Run = () => App.I.ShowMain("system:procs") });
            return y + Ui.S(6);
        }

        void OpenTask(int i)
        {
            if (i >= upNext.Count) { QuickTaskForm.Open(); return; }
            var t = upNext[i];
            App.I.ShowMain("tasks");
            var mf = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (mf != null) mf.SelectTask(t);
        }

        // Text link at the top right of a card ("Processes ›").
        static Rectangle LinkRect(Rectangle card, string text)
        {
            int w = TextRenderer.MeasureText(text + "  ›", Ui.Base).Width + Ui.S(8);
            return new Rectangle(card.Right - Ui.S(20) - w, card.Y + Ui.S(18), w, Ui.S(26));
        }

        // ---------- paint helpers ----------
        static readonly TextFormatFlags L = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        static readonly TextFormatFlags C = TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        static Color Cpu { get { return Ui.Accent; } }
        static Color Mem { get { return Color.FromArgb(139, 92, 246); } }   // Task Manager's memory colour

        static Font badge; static string fontKey;
        static Font BadgeFont { get { string k = Ui.FontName; if (k != fontKey) { fontKey = k; badge = new Font(Ui.FontName, 7f, FontStyle.Bold); } return badge; } }

        bool IsHot(string key) { return hover != null && hover.Key == key; }
        int Radius { get { return Ui.S(Ui.Navy ? 16 : 12); } }

        void Card(Graphics g, Rectangle r) { Ui.FillCard(g, new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Radius, false); }

        void Title(Graphics g, Rectangle card, string title, string sub, int reserve)
        {
            TextRenderer.DrawText(g, title, Ui.Big, new Rectangle(card.X + Ui.S(22), card.Y + Ui.S(16), card.Width - Ui.S(44) - reserve, Ui.S(26)), Ui.Text, L);
            if (sub != null) TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(card.X + Ui.S(22), card.Y + Ui.S(42), card.Width - Ui.S(44) - reserve, Ui.S(18)), Ui.Muted, L);
        }

        void Link(Graphics g, Rectangle card, string text, string key)
        {
            var r = LinkRect(card, text);
            bool hot = IsHot(key);
            using (var f = hot ? new Font(Ui.Base, FontStyle.Underline) : null)
                TextRenderer.DrawText(g, text + "  ›", f ?? Ui.Base, r, Ui.Accent, L | TextFormatFlags.Right);
        }

        static void Bar(Graphics g, Rectangle r, double frac, Color fill)
        {
            using (var path = Ui.Round(r, r.Height / 2)) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
            int fw = (int)(r.Width * Math.Max(0, Math.Min(1, frac)));
            if (fw >= r.Height) using (var path = Ui.Round(new Rectangle(r.X, r.Y, fw, r.Height), r.Height / 2)) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        }

        static Color Usage(double f) { return f > 0.9 ? Ui.Danger : f > 0.8 ? Ui.Warning : Ui.Accent; }

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
            if (next.IntersectsWith(clip)) Next(g);
            if (today.IntersectsWith(clip)) Today(g);
            if (storage.IntersectsWith(clip)) Storage(g);
            if (apps.IntersectsWith(clip)) Apps(g);
        }

        void Header(Graphics g)
        {
            int limit = (btnTerm.Width > 0 ? btnTerm.X : btnNew.Width > 0 ? btnNew.X : bell.X) - Ui.S(12) - header.X;
            TextRenderer.DrawText(g, DateTime.Now.ToString("dddd, d MMMM"), Ui.Title, new Rectangle(header.X, header.Y + Ui.S(4), limit, Ui.S(32)), Ui.Text, L);
            // Status line: plain facts, warnings in red.
            string calm = dueToday == 0 ? "Nothing due today" : dueToday + (dueToday == 1 ? " task due today" : " tasks due today");
            var warn = new List<string>();
            if (overdue > 0) warn.Add(overdue + " overdue");
            foreach (var d in drives) if (d.Used > 0.9) warn.Add(d.Letter + ": " + (d.Used * 100).ToString("0") + "% full");
            int cw = TextRenderer.MeasureText(g, calm, Ui.Base, Size.Empty, TextFormatFlags.NoPadding).Width;
            var sr = new Rectangle(header.X, header.Y + Ui.S(38), limit, Ui.S(20));
            TextRenderer.DrawText(g, calm, Ui.Base, sr, Ui.Muted, L);
            if (warn.Count > 0 && cw + Ui.S(40) < limit)
                TextRenderer.DrawText(g, "  ·  " + string.Join("  ·  ", warn), Ui.Base, new Rectangle(sr.X + cw, sr.Y, limit - cw, sr.Height), Ui.Danger, L);

            if (btnTerm.Width > 0)
            {
                var r = btnTerm;
                using (var path = Ui.Round(r, Ui.S(9)))
                {
                    using (var b = new SolidBrush(IsHot("term") ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                    using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
                }
                BtnText(g, r, "", "Terminals", Ui.Text);
            }
            if (btnNew.Width > 0)
            {
                var r = btnNew;
                using (var path = Ui.Round(r, Ui.S(9))) using (var b = new SolidBrush(IsHot("new") ? Ui.AccentHover : Ui.AccentFill)) g.FillPath(b, path);
                BtnText(g, r, "", "New task", Color.White);
            }
            RoundButton(g, bell, "", "bell");
            int badgeN = dueToday + overdue;
            if (badgeN > 0)
            {
                var br = new Rectangle(bell.Right - Ui.S(15), bell.Y - Ui.S(3), Ui.S(18), Ui.S(18));
                using (var b = new SolidBrush(overdue > 0 ? Ui.Danger : Ui.Accent)) g.FillEllipse(b, br);
                TextRenderer.DrawText(g, badgeN > 9 ? "9+" : badgeN.ToString(), BadgeFont, br, Color.White, C);
            }
            RoundButton(g, gear, "", "gear");
        }

        void RoundButton(Graphics g, Rectangle r, string glyph, string key)
        {
            var rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var b = new SolidBrush(IsHot(key) ? Ui.HoverBg : Ui.Card)) g.FillEllipse(b, rr);
            using (var p = new Pen(Ui.Line)) g.DrawEllipse(p, rr);
            TextRenderer.DrawText(g, glyph, Ui.Icon(12f), r, Ui.Text, C);
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
            Card(g, c);
            // Legend doubles as the current values.
            string l1 = "CPU " + cpuNow.ToString("0") + "%", l2 = "Memory " + memLoad + "%";
            int w1 = TextRenderer.MeasureText(l1, Ui.Bold).Width, w2 = TextRenderer.MeasureText(l2, Ui.Bold).Width;
            int lx = c.Right - Ui.S(22) - w2 - Ui.S(18) - w1 - Ui.S(18) - Ui.S(16);
            Title(g, c, "CPU and memory", "Last 2 minutes, sampled every 2 seconds", c.Right - lx);
            int ly = c.Y + Ui.S(18);
            using (var b = new SolidBrush(Cpu)) g.FillEllipse(b, lx, ly + Ui.S(8), Ui.S(10), Ui.S(10));
            TextRenderer.DrawText(g, l1, Ui.Bold, new Rectangle(lx + Ui.S(16), ly, w1 + 4, Ui.S(26)), Ui.Text, L);
            int lx2 = lx + Ui.S(16) + w1 + Ui.S(18);
            using (var b = new SolidBrush(Mem)) g.FillEllipse(b, lx2, ly + Ui.S(8), Ui.S(10), Ui.S(10));
            TextRenderer.DrawText(g, l2, Ui.Bold, new Rectangle(lx2 + Ui.S(16), ly, w2 + 4, Ui.S(26)), Ui.Text, L);

            int statsH = Ui.S(62);
            int axis = Ui.S(38);
            var plot = new Rectangle(c.X + Ui.S(22) + axis, c.Y + Ui.S(78), c.Width - Ui.S(44) - axis, c.Height - Ui.S(78) - statsH - Ui.S(18));
            // Fixed 0-100 % scale with three grid lines (no auto-zoom that exaggerates small changes).
            using (var p = new Pen(Ui.Line) { DashStyle = DashStyle.Dot })
                for (int k = 0; k <= 2; k++)
                {
                    int gy = plot.Y + plot.Height * k / 2;
                    g.DrawLine(p, plot.X, gy, plot.Right, gy);
                    TextRenderer.DrawText(g, (100 - k * 50) + "%", Ui.Small, new Rectangle(c.X + Ui.S(16), gy - Ui.S(9), axis - Ui.S(2), Ui.S(18)), Ui.Muted, L | TextFormatFlags.Right);
                }
            Series(g, plot, cpu, Cpu, true);
            Series(g, plot, ram, Mem, false);

            // Facts strip
            var s = new Rectangle(c.X + Ui.S(22), c.Bottom - statsH - Ui.S(10), c.Width - Ui.S(44), statsH);
            using (var p = new Pen(Ui.Line)) g.DrawLine(p, s.X, s.Y, s.Right, s.Y);
            int third = s.Width / 3;
            Stat(g, new Rectangle(s.X, s.Y, third, s.Height), "CPU peak (2 min)", cpuPeak.ToString("0") + "%");
            Stat(g, new Rectangle(s.X + third, s.Y, third, s.Height), "Memory in use", ProcessSampler.MemTotal == 0 ? "-" : memUsed + " of " + memTotal);
            Stat(g, new Rectangle(s.X + 2 * third, s.Y, s.Width - 2 * third, s.Height), "Processes", ProcessSampler.Last.Count.ToString("N0"));
        }

        static void Series(Graphics g, Rectangle plot, double[] data, Color color, bool fill)
        {
            if (data == null || data.Length < 2) return;
            const int n = 60;   // 2 minutes at 2 s; the line grows in from the right like Task Manager
            var pts = new PointF[data.Length];
            for (int k = 0; k < pts.Length; k++)
            {
                int slot = n - pts.Length + k;
                pts[k] = new PointF(plot.X + Math.Max(0, slot) * plot.Width / (float)(n - 1), plot.Bottom - (float)Math.Max(0, Math.Min(100, data[k])) * plot.Height / 100f);
            }
            if (fill)
                using (var path = new GraphicsPath())
                {
                    path.AddLines(pts);
                    path.AddLine(pts[pts.Length - 1], new PointF(pts[pts.Length - 1].X, plot.Bottom));
                    path.AddLine(new PointF(pts[pts.Length - 1].X, plot.Bottom), new PointF(pts[0].X, plot.Bottom));
                    path.CloseFigure();
                    using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 46 : 30, color))) g.FillPath(b, path);
                }
            using (var pen = new Pen(color, Ui.S(2)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
        }

        static void Stat(Graphics g, Rectangle r, string label, string value)
        {
            TextRenderer.DrawText(g, label, Ui.Small, new Rectangle(r.X, r.Y + Ui.S(10), r.Width - Ui.S(8), Ui.S(18)), Ui.Muted, L);
            TextRenderer.DrawText(g, value, Ui.Big, new Rectangle(r.X, r.Y + Ui.S(28), r.Width - Ui.S(8), Ui.S(28)), Ui.Text, L);
        }

        void Next(Graphics g)
        {
            var c = next;
            Card(g, c);
            int open = Store.Tasks.Count(t => !t.Done);
            Title(g, c, "Up next", open == 0 ? "No open tasks" : open + (open == 1 ? " open task" : " open tasks"), LinkRect(c, "All tasks").Width);
            Link(g, c, "All tasks", "tasksall");
            if (upNext.Count == 0)
            {
                var r = new Rectangle(c.X + Ui.S(22), c.Y + Ui.S(84), c.Width - Ui.S(44), Ui.S(60));
                TextRenderer.DrawText(g, "Nothing planned. Click here or press Ctrl+Alt+J to add a task\n(\"call Ali tomorrow 3pm !high\").", Ui.Base, r, Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return;
            }
            for (int i = 0; i < taskRows.Count && i < upNext.Count; i++)
            {
                var t = upNext[i]; var r = taskRows[i];
                if (IsHot("task" + i)) using (var path = Ui.Round(r, Ui.S(8))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                bool late = TaskParse.Overdue(t);
                string due = t.Due > 0 ? TaskParse.DueText(t) : "No date";
                int dw = Math.Min(r.Width / 3, TextRenderer.MeasureText(due, Ui.Small).Width + Ui.S(6));
                int x = r.X + Ui.S(10);
                // Priority marker only when it means something.
                if (t.Priority == 2) using (var b = new SolidBrush(Ui.Danger)) g.FillEllipse(b, x, r.Y + Ui.S(13), Ui.S(7), Ui.S(7));
                x += Ui.S(14);
                int tw = r.Right - Ui.S(10) - dw - Ui.S(10) - x;
                TextRenderer.DrawText(g, t.Title.Length > 0 ? Ui.OneLine(t.Title, 200) : "(untitled)", Ui.Bold, new Rectangle(x, r.Y + Ui.S(4), tw, Ui.S(20)), Ui.Text, L);
                string sub = t.List + (t.Steps.Count > 0 ? "  ·  " + t.Steps.Count(s => s.Done) + "/" + t.Steps.Count + " steps" : "");
                TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(x, r.Y + Ui.S(24), tw, Ui.S(17)), Ui.Muted, L);
                TextRenderer.DrawText(g, due, Ui.Small, new Rectangle(r.Right - Ui.S(10) - dw, r.Y, dw, r.Height), late ? Ui.Danger : Ui.Muted, L | TextFormatFlags.Right);
                if (i < taskRows.Count - 1 && i < upNext.Count - 1 && !IsHot("task" + i) && !IsHot("task" + (i + 1)))
                    using (var p = new Pen(Ui.Line)) g.DrawLine(p, x, r.Bottom, r.Right - Ui.S(10), r.Bottom);
            }
        }

        void Today(Graphics g)
        {
            Card(g, today);
            for (int i = 0; i < 4; i++)
            {
                var r = todayCol[i];
                if (IsHot("today" + i))
                    using (var path = Ui.Round(Rectangle.Inflate(r, -Ui.S(6), -Ui.S(6)), Ui.S(10))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                int x = r.X + Ui.S(22), w = r.Width - Ui.S(36);
                TextRenderer.DrawText(g, todayLabel[i], Ui.Base, new Rectangle(x, r.Y + Ui.S(14), w, Ui.S(20)), Ui.Muted, L);
                TextRenderer.DrawText(g, todayValue[i].ToString("N0"), Ui.Kpi, new Rectangle(x, r.Y + Ui.S(34), w, Ui.S(34)), Ui.Text, L);
                TextRenderer.DrawText(g, todayNote[i], Ui.Small, new Rectangle(x, r.Y + Ui.S(70), w, Ui.S(18)), todayAlert[i] ? Ui.Danger : Ui.Muted, L);
                bool lastInRow = r.Right >= today.Right - 2;
                if (!lastInRow) using (var p = new Pen(Ui.Line)) g.DrawLine(p, r.Right, r.Y + Ui.S(18), r.Right, r.Bottom - Ui.S(18));
            }
        }

        void Storage(Graphics g)
        {
            var c = storage;
            Card(g, c);
            long free = drives.Sum(d => d.Free), total = drives.Sum(d => d.Total);
            Title(g, c, "Storage", drives.Count == 0 ? "No fixed drives found" : Fmt.Bytes(free) + " free of " + Fmt.Bytes(total) + " on " + drives.Count + (drives.Count == 1 ? " drive" : " drives"), LinkRect(c, "Drives & cleanup").Width);
            Link(g, c, "Drives & cleanup", "storageall");
            int x = c.X + Ui.S(22), w = c.Width - Ui.S(44), y = c.Y + Ui.S(78), rh = Ui.S(52);
            foreach (var d in drives)
            {
                if (y + rh > c.Bottom - Ui.S(10)) break;
                string name = d.Letter + ":" + (d.Label.Length > 0 ? "  " + d.Label : "");
                string val = Fmt.Bytes(d.Free) + " free of " + Fmt.Bytes(d.Total);
                int vw = TextRenderer.MeasureText(val, Ui.Small).Width + Ui.S(4);
                TextRenderer.DrawText(g, name, Ui.Bold, new Rectangle(x, y, w - vw - Ui.S(8), Ui.S(22)), Ui.Text, L);
                TextRenderer.DrawText(g, val, Ui.Small, new Rectangle(x + w - vw, y, vw, Ui.S(22)), d.Used > 0.9 ? Ui.Danger : Ui.Muted, L | TextFormatFlags.Right);
                Bar(g, new Rectangle(x, y + Ui.S(28), w, Ui.S(8)), d.Used, Usage(d.Used));
                y += rh;
            }
        }

        void Apps(Graphics g)
        {
            var c = apps;
            Card(g, c);
            Title(g, c, "Memory by app", ProcessSampler.MemTotal == 0 ? "Grouped by app" : "Grouped by app, share of " + memTotal + " RAM", LinkRect(c, "Processes").Width);
            Link(g, c, "Processes", "appsall");
            int y0 = c.Y + Ui.S(76), rh = Ui.S(44), w = c.Width - Ui.S(44);
            if (topApps.Count == 0) { TextRenderer.DrawText(g, "Waiting for the first sample...", Ui.Base, new Rectangle(c.X + Ui.S(22), y0, w, Ui.S(40)), Ui.Muted, L); return; }
            int cols = w > Ui.S(760) ? 2 : 1, cgap = Ui.S(28);
            int colW = (w - cgap * (cols - 1)) / cols;
            int perCol = Math.Max(1, (c.Bottom - y0 - Ui.S(8)) / rh);
            int count = Math.Min(topApps.Count, perCol * cols);
            double totalMem = Math.Max(1, (double)ProcessSampler.MemTotal);
            int isz = Ui.S(16);
            for (int i = 0; i < count; i++)
            {
                var a = topApps[i];
                int x = c.X + Ui.S(22) + (i / perCol) * (colW + cgap), y = y0 + (i % perCol) * rh;
                int icon = string.IsNullOrEmpty(a.Path) ? 0 : ProcMeta.IconFor(a.Path);
                var ir = new Rectangle(x, y + Ui.S(4), isz, isz);
                if (icon > 0) ProcMeta.Icons.Draw(g, ir.X, ir.Y, icon);
                else
                {
                    using (var b = new SolidBrush(Ui.Track)) g.FillEllipse(b, ir);
                    TextRenderer.DrawText(g, a.Name.Length > 0 ? a.Name.Substring(0, 1).ToUpperInvariant() : "?", Ui.Small, ir, Ui.Muted, C);
                }
                double share = a.Mem / totalMem;
                string val = Fmt.Bytes(a.Mem) + "  ·  " + (share * 100).ToString(share < 0.1 ? "0.0" : "0") + "%";
                int vw = TextRenderer.MeasureText(val, Ui.Small).Width + Ui.S(4);
                int tx = ir.Right + Ui.S(10), tw = x + colW - vw - Ui.S(8) - tx;
                TextRenderer.DrawText(g, (a.Name.Length > 0 ? a.Name : "?") + (a.Count > 1 ? "  (" + a.Count + ")" : ""), Ui.Base, new Rectangle(tx, y, tw, Ui.S(24)), Ui.Text, L);
                TextRenderer.DrawText(g, val, Ui.Small, new Rectangle(x + colW - vw, y, vw, Ui.S(24)), Ui.Muted, L | TextFormatFlags.Right);
                Bar(g, new Rectangle(tx, y + Ui.S(28), x + colW - tx, Ui.S(5)), share, Mem);
            }
        }

        // ---------- mouse ----------
        Hot At(Point p)
        {
            for (int i = hots.Count - 1; i >= 0; i--)
            {
                var h = hots[i];
                if (!h.R.Contains(p)) continue;
                // Task rows below the last task are empty space, except the first (it opens New task when the list is empty).
                if (h.Key.StartsWith("task") && h.Key != "tasksall") { int k; if (int.TryParse(h.Key.Substring(4), out k) && k >= Math.Max(1, upNext.Count)) continue; }
                return h;
            }
            return null;
        }
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
