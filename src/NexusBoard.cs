using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    class NexusBoard : Control
    {
        class Hot { public Rectangle R; public string Key; public Action Run; }

        readonly List<Hot> hots = new List<Hot>();
        Hot hover;
        readonly LinkLabel attention;
        string attentionPage;

        // Shared / SerQ-Nexus rects
        Rectangle header, btnNew, btnTerm, bell, gear, overview, next, today, storage, apps;
        readonly Rectangle[] todayCol = new Rectangle[4];
        readonly List<Rectangle> taskRows = new List<Rectangle>();

        // Flow-specific rects
        Rectangle donut, weekCard, kanbanCard;
        readonly Rectangle[] kpiCards = new Rectangle[4];
        readonly Rectangle[] kanbanCols = new Rectangle[4];
        readonly List<Rectangle>[] kanbanTaskRects = new List<Rectangle>[4];
        readonly Rectangle[] kanbanAddRects = new Rectangle[4];

        // Data
        double[] cpu = new double[0], ram = new double[0];
        double cpuNow, cpuPeak; int memLoad; string memUsed = "-", memTotal = "-";
        int dueToday, overdue, openTasks, doneTasks, totalTasks, pinnedClips;
        DateTime lastStoreDay = DateTime.MinValue;
        List<TaskItem> upNext = new List<TaskItem>();
        readonly List<TaskItem>[] kanbanTasks = new List<TaskItem>[4];
        readonly bool[] weekHasTask = new bool[7];
        readonly string[] todayLabel = { "Due today", "Copied today", "Files opened today", "Tagged urgent" };
        readonly string[] todayPage = { "tasks", "clips", "activity", "tagged" };
        readonly int[] todayValue = new int[4];
        readonly string[] todayNote = { "", "", "", "" };
        readonly bool[] todayAlert = new bool[4];
        List<DriveSnapshot> drives = new List<DriveSnapshot>();
        List<MemoryApp> topApps = new List<MemoryApp>();

        public NexusBoard()
        {
            for (int i = 0; i < 4; i++) { kanbanTasks[i] = new List<TaskItem>(); kanbanTaskRects[i] = new List<Rectangle>(); }
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "Home";
            Color alertText = Ui.Dark ? Ui.Danger : Ui.Mix(Ui.Danger, Color.Black, 0.25);
            attention = new LinkLabel
            {
                Font = Ui.Base, Visible = false, UseMnemonic = false, AutoEllipsis = true,
                LinkBehavior = LinkBehavior.AlwaysUnderline, TextAlign = ContentAlignment.MiddleLeft,
                LinkColor = alertText, ActiveLinkColor = alertText, VisitedLinkColor = alertText
            };
            attention.LinkClicked += delegate { if (App.I != null) App.I.ShowMain(attentionPage); };
            Controls.Add(attention);
        }

        // ---------- data ----------
        bool dataLoaded;
        // live = only the sampler values (every 2 s); data = also the store facts (only when the store changed or day rolled over).
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
            topApps = ProcessSampler.MemoryApps;
            if (data || !dataLoaded || lastStoreDay != DateTime.Today) { dataLoaded = true; lastStoreDay = DateTime.Today; RefreshStoreData(); }
            UpdateAttention();
            Invalidate();
        }

        void UpdateAttention()
        {
            int low = drives.Count(d => d.Used > 0.9);
            attentionPage = overdue > 0 ? "tasks" : "system:drives";
            string text = overdue > 0 ? overdue + (overdue == 1 ? " overdue task" : " overdue tasks") :
                low > 0 ? low + (low == 1 ? " drive nearly full" : " drives nearly full") : "";
            if (attention.Text == text) return;
            attention.Text = text;
            attention.AccessibleDescription = overdue > 0 ? "Open tasks" : "Open storage";
            attention.Visible = text.Length > 0;
            ArrangeAttention();
        }

        void ArrangeAttention()
        {
            int width = Math.Min(header.Width, TextRenderer.MeasureText(attention.Text, attention.Font).Width + Ui.S(4));
            attention.SetBounds(header.X, header.Y + Ui.S(37), width, Ui.S(23));
        }

        void RefreshStoreData()
        {
            var facts = DashboardFacts.Read(Store.Now());
            var todayDate = DateTime.Today;
            long today0 = todayDate.Ticks, tomorrow = todayDate.AddDays(1).Ticks;
            totalTasks = Store.Tasks.Count;
            doneTasks = Store.Tasks.Count(t => t.Done);
            overdue = Store.Tasks.Count(t => TaskParse.Overdue(t));
            dueToday = Store.Tasks.Count(t => !t.Done && t.Due >= today0 && t.Due < tomorrow && !TaskParse.Overdue(t));
            var open = Store.Tasks.Where(t => !t.Done).ToList();
            openTasks = open.Count;
            // Next tasks: dated ones by due time (overdue first), then undated by priority.
            upNext = open.Where(t => t.Due > 0).OrderBy(t => t.Due)
                .Concat(open.Where(t => t.Due == 0).OrderByDescending(t => t.Priority).ThenByDescending(t => t.Created))
                .Take(12).ToList();

            // This Week (Mon..Sun) task indicator dots
            int dow = ((int)todayDate.DayOfWeek + 6) % 7; // 0 = Mon .. 6 = Sun
            var mon = todayDate.AddDays(-dow);
            for (int d = 0; d < 7; d++)
            {
                long d0 = mon.AddDays(d).Ticks, d1 = mon.AddDays(d + 1).Ticks;
                weekHasTask[d] = open.Any(t => t.Due >= d0 && t.Due < d1);
            }

            // 4 Kanban buckets for Flow Task Board
            var kHigh = open.Where(t => TaskParse.Overdue(t) || t.Priority == 2).OrderBy(t => t.Due == 0 ? long.MaxValue : t.Due).ToList();
            var kHighIds = new HashSet<string>(kHigh.Select(t => t.Id));
            var kToday = open.Where(t => !kHighIds.Contains(t.Id) && t.Due >= today0 && t.Due < tomorrow).OrderBy(t => t.Due).ToList();
            var kTodayIds = new HashSet<string>(kToday.Select(t => t.Id));
            var kTodo = open.Where(t => !kHighIds.Contains(t.Id) && !kTodayIds.Contains(t.Id))
                .OrderBy(t => t.Due == 0 ? long.MaxValue : t.Due).ThenByDescending(t => t.Priority).ToList();
            var kDone = Store.Tasks.Where(t => t.Done).OrderByDescending(t => t.Modified > 0 ? t.Modified : t.Created).ToList();
            kanbanTasks[0] = kTodo;
            kanbanTasks[1] = kToday;
            kanbanTasks[2] = kHigh;
            kanbanTasks[3] = kDone;

            todayValue[0] = dueToday + overdue;
            todayNote[0] = overdue > 0 ? overdue + " overdue" : "Nothing overdue"; todayAlert[0] = overdue > 0;
            todayValue[1] = facts.ClipsToday;
            pinnedClips = Store.Clips.Count(c => c.Pinned);
            todayNote[1] = pinnedClips + " pinned  ·  " + Store.Clips.Count.ToString("N0") + " in history";
            todayValue[2] = facts.FilesToday;
            var last = Store.Activity.Where(a => a.Event == "Opened").OrderByDescending(a => a.Time).FirstOrDefault();
            todayNote[2] = !Store.Settings.ActivityOn ? "File activity logging is off" : last != null ? "Last: " + ActivityMonitor.SafeName(last.Path) : "No files yet";
            todayValue[3] = facts.Urgent; todayAlert[3] = false;
            todayNote[3] = Store.Tagged.Count + " tagged in total";
        }

        // ---------- layout ----------
        public int Measure(int full)
        {
            int m = Ui.S(8);
            int inner = Math.Max(Ui.S(200), full - 2 * m);
            int h = Ui.Flow ? ArrangeFlow(inner) : Arrange(inner);
            var off = new Size(m, m);
            Func<Rectangle, Rectangle> o = r => r.Width > 0 ? new Rectangle(r.Location + off, r.Size) : r;
            header = o(header); btnNew = o(btnNew); btnTerm = o(btnTerm); bell = o(bell); gear = o(gear);
            ArrangeAttention();
            overview = o(overview); next = o(next); today = o(today); storage = o(storage); apps = o(apps);
            donut = o(donut); weekCard = o(weekCard); kanbanCard = o(kanbanCard);
            for (int i = 0; i < 4; i++)
            {
                todayCol[i] = o(todayCol[i]);
                kpiCards[i] = o(kpiCards[i]);
                kanbanCols[i] = o(kanbanCols[i]);
                kanbanAddRects[i] = o(kanbanAddRects[i]);
                for (int j = 0; j < kanbanTaskRects[i].Count; j++) kanbanTaskRects[i][j] = o(kanbanTaskRects[i][j]);
            }
            for (int i = 0; i < taskRows.Count; i++) taskRows[i] = o(taskRows[i]);
            foreach (var hot in hots) hot.R = new Rectangle(hot.R.Location + off, hot.R.Size);
            return h + 2 * m;
        }

        int ArrangeFlow(int w)
        {
            hots.Clear(); taskRows.Clear();
            for (int i = 0; i < 4; i++) { kanbanTaskRects[i].Clear(); kanbanCols[i] = Rectangle.Empty; kanbanAddRects[i] = Rectangle.Empty; }
            today = Rectangle.Empty; next = Rectangle.Empty; btnTerm = Rectangle.Empty;

            int gap = Ui.S(16);
            header = new Rectangle(0, 0, w, Ui.S(60));
            int bh = Ui.S(36), c = Ui.S(36);
            gear = new Rectangle(w - c, 0, c, c);
            bell = new Rectangle(gear.X - Ui.S(8) - c, 0, c, c);
            int bw = TextRenderer.MeasureText("New task", Ui.Bold).Width + Ui.S(42);
            btnNew = w >= Ui.S(360) ? new Rectangle(bell.X - Ui.S(10) - bw, 0, bw, bh) : Rectangle.Empty;

            hots.Add(new Hot { R = bell, Key = "bell", Run = () => App.I.ShowMain("tasks") });
            hots.Add(new Hot { R = gear, Key = "gear", Run = () => App.I.ShowSettings(FindForm()) });
            if (btnNew.Width > 0) hots.Add(new Hot { R = btnNew, Key = "new", Run = () => QuickTaskForm.Open() });
            int y = header.Bottom + Ui.S(10);

            // Keep daily activity and task counts together.
            int kCols = w >= Ui.S(760) ? 4 : 2;
            int kRows = 4 / kCols;
            int kh = Ui.S(78);
            int kw = (w - gap * (kCols - 1)) / kCols;
            string[] kPages = { "tasks", "tasks", "clips", "activity" };
            for (int i = 0; i < 4; i++)
            {
                int col = i % kCols, row = i / kCols;
                int x = col * (kw + gap);
                int cardW = col == kCols - 1 ? w - x : kw;
                kpiCards[i] = new Rectangle(x, y + row * (kh + gap), cardW, kh);
                string page = kPages[i];
                hots.Add(new Hot { R = kpiCards[i], Key = "kpi" + i, Run = () => App.I.ShowMain(page) });
            }
            y += kRows * kh + (kRows - 1) * gap + gap;

            // Row 2: 3-Column Analytics Row (Storage Progress | Tasks Overview Donut | This Week)
            int r2h = Ui.S(306);
            if (w >= Ui.S(1020))
            {
                int w1 = (w - 2 * gap) * 38 / 100;
                int w2 = (w - 2 * gap) * 27 / 100;
                int w3 = w - 2 * gap - w1 - w2;
                storage = new Rectangle(0, y, w1, r2h);
                donut = new Rectangle(storage.Right + gap, y, w2, r2h);
                weekCard = new Rectangle(donut.Right + gap, y, w3, r2h);
                y = storage.Bottom + gap;
            }
            else if (w >= Ui.S(680))
            {
                int w1 = (w - gap) * 56 / 100;
                int w2 = w - gap - w1;
                storage = new Rectangle(0, y, w1, r2h);
                donut = new Rectangle(storage.Right + gap, y, w2, r2h);
                y = storage.Bottom + gap;
                weekCard = new Rectangle(0, y, w, Ui.S(254));
                y = weekCard.Bottom + gap;
            }
            else
            {
                storage = new Rectangle(0, y, w, r2h); y = storage.Bottom + gap;
                donut = new Rectangle(0, y, w, Ui.S(270)); y = donut.Bottom + gap;
                weekCard = new Rectangle(0, y, w, Ui.S(264)); y = weekCard.Bottom + gap;
            }
            hots.Add(new Hot { R = LinkRect(storage, "View All"), Key = "storageall", Run = () => App.I.ShowMain("system:drives") });
            hots.Add(new Hot { R = new Rectangle(donut.X, donut.Y, donut.Width, Ui.S(54)), Key = "donuthead", Run = () => App.I.ShowMain("tasks") });
            hots.Add(new Hot { R = LinkRect(weekCard, "View All"), Key = "tasksall", Run = () => App.I.ShowMain("tasks") });

            int wy = weekCard.Y + Ui.S(128), wrh = Ui.S(46);
            for (int i = 0; i < 3 && wy + wrh <= weekCard.Bottom - Ui.S(10); i++, wy += wrh)
            {
                var r = new Rectangle(weekCard.X + Ui.S(14), wy, weekCard.Width - Ui.S(28), wrh);
                taskRows.Add(r);
                int k = i;
                hots.Add(new Hot { R = r, Key = "task" + i, Run = () => OpenTask(k) });
            }
            if (taskRows.Count > 0)
            {
                // Full empty-state clickable area when no tasks exist
                var emptyR = new Rectangle(weekCard.X + Ui.S(16), weekCard.Y + Ui.S(128), weekCard.Width - Ui.S(32), Math.Max(Ui.S(54), weekCard.Bottom - (weekCard.Y + Ui.S(140))));
                hots.Add(new Hot { R = emptyR, Key = "taskempty", Run = () => QuickTaskForm.Open() });
            }

            // Row 3: 2-Column Bottom Analytics (Top Memory Apps | Stacked System Activity Chart)
            int r3h = Ui.S(284);
            if (w >= Ui.S(860))
            {
                int w1 = (w - gap) * 44 / 100;
                int w2 = w - gap - w1;
                apps = new Rectangle(0, y, w1, r3h);
                overview = new Rectangle(apps.Right + gap, y, w2, r3h);
                y = apps.Bottom + gap;
            }
            else
            {
                apps = new Rectangle(0, y, w, r3h); y = apps.Bottom + gap;
                overview = new Rectangle(0, y, w, r3h); y = overview.Bottom + gap;
            }
            hots.Add(new Hot { R = LinkRect(apps, "View All"), Key = "appsall", Run = () => App.I.ShowMain("system:procs") });
            hots.Add(new Hot { R = new Rectangle(overview.X, overview.Y, overview.Width, Ui.S(54)), Key = "overview", Run = () => App.I.ShowMain("system:procs") });

            // Row 4: Task Board Preview (4 Pastel Kanban Columns)
            int kbCols = w >= Ui.S(880) ? 4 : 2;
            int kbRows = 4 / kbCols;
            int colGap = Ui.S(12), pad = Ui.S(18), headH = Ui.S(52);
            int cardH = Ui.S(58), cardGap = Ui.S(8), addH = Ui.S(28);
            int colH = Ui.S(40) + 3 * cardH + 2 * cardGap + Ui.S(10) + addH + Ui.S(10);
            int kbH = headH + kbRows * colH + (kbRows - 1) * colGap + pad;
            kanbanCard = new Rectangle(0, y, w, kbH);
            hots.Add(new Hot { R = LinkRect(kanbanCard, "View Full Board"), Key = "kanbanall", Run = () => App.I.ShowMain("tasks") });

            int innerW = w - 2 * pad;
            int colW = (innerW - colGap * (kbCols - 1)) / kbCols;
            for (int i = 0; i < 4; i++)
            {
                int col = i % kbCols, row = i / kbCols;
                int cx = pad + col * (colW + colGap);
                int cw = col == kbCols - 1 ? innerW - col * (colW + colGap) : colW;
                int cy = kanbanCard.Y + headH + row * (colH + colGap);
                kanbanCols[i] = new Rectangle(cx, cy, cw, colH);
                int ty = cy + Ui.S(40);
                for (int j = 0; j < 3; j++)
                {
                    var tr = new Rectangle(cx + Ui.S(10), ty, cw - Ui.S(20), cardH);
                    kanbanTaskRects[i].Add(tr);
                    int ci = i, cj = j;
                    hots.Add(new Hot { R = tr, Key = "kb_" + ci + "_" + cj, Run = () => OpenKanbanTask(ci, cj) });
                    ty += cardH + cardGap;
                }
                kanbanAddRects[i] = new Rectangle(cx + Ui.S(10), kanbanCols[i].Bottom - addH - Ui.S(8), cw - Ui.S(20), addH);
                hots.Add(new Hot { R = kanbanAddRects[i], Key = "kbadd" + i, Run = () => QuickTaskForm.Open() });
            }
            y = kanbanCard.Bottom + gap;
            return y + Ui.S(6);
        }

        int Arrange(int w)
        {
            hots.Clear(); taskRows.Clear();
            donut = Rectangle.Empty; weekCard = Rectangle.Empty; kanbanCard = Rectangle.Empty;
            for (int i = 0; i < 4; i++) { kpiCards[i] = Rectangle.Empty; kanbanCols[i] = Rectangle.Empty; kanbanTaskRects[i].Clear(); kanbanAddRects[i] = Rectangle.Empty; }

            int gap = Ui.S(18);
            header = new Rectangle(0, 0, w, Ui.S(64));
            int bh = Ui.S(38), c = Ui.S(38);
            gear = new Rectangle(w - c, 0, c, c);
            bell = new Rectangle(gear.X - Ui.S(10) - c, 0, c, c);
            int bw = TextRenderer.MeasureText("New task", Ui.Bold).Width + Ui.S(48);
            int tw = TextRenderer.MeasureText("Terminals", Ui.Bold).Width + Ui.S(48);
            bool roomy = w > Ui.S(700);
            btnNew = roomy ? new Rectangle(bell.X - Ui.S(14) - bw, 0, bw, bh) : Rectangle.Empty;
            btnTerm = roomy && w > Ui.S(900) ? new Rectangle(btnNew.X - Ui.S(10) - tw, 0, tw, bh) : Rectangle.Empty;
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
            hots.Add(new Hot { R = new Rectangle(next.X + Ui.S(16), next.Y + Ui.S(70), next.Width - Ui.S(32), Ui.S(90)), Key = "taskempty", Run = () => QuickTaskForm.Open() });

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
            OpenTaskItem(upNext[i]);
        }

        void OpenKanbanTask(int col, int idx)
        {
            if (col < 0 || col >= 4 || idx < 0 || idx >= kanbanTasks[col].Count) return;
            OpenTaskItem(kanbanTasks[col][idx]);
        }

        static void OpenTaskItem(TaskItem t)
        {
            App.I.ShowMain("tasks");
            var mf = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (mf != null) mf.SelectTask(t);
        }

        // Text link at the top right of a card ("View All ›").
        static Rectangle LinkRect(Rectangle card, string text)
        {
            int w = TextRenderer.MeasureText(text + "  ›", Ui.Base).Width + Ui.S(8);
            return new Rectangle(card.Right - Ui.S(20) - w, card.Y + Ui.S(16), w, Ui.S(26));
        }

        // ---------- paint helpers ----------
        static readonly TextFormatFlags L = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        static readonly TextFormatFlags C = TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
        static Color Cpu { get { return Ui.Accent; } }
        static Color Mem { get { return Ui.Atelier || Ui.Nordic ? Ui.SecondAccent : Color.FromArgb(139, 92, 246); } }
        static Color FlowBlue { get { return Ui.Atelier || Ui.Nordic ? Ui.AccentFill : Color.FromArgb(47, 102, 246); } }
        static Color FlowSky { get { return Ui.Atelier || Ui.Nordic ? Ui.SecondAccent : Color.FromArgb(112, 161, 255); } }
        static Color FlowGreen { get { return Ui.Nordic ? Ui.AccentFill : Ui.Atelier ? Ui.SecondAccent : Color.FromArgb(34, 197, 139); } }
        static readonly Color FlowAmber = Color.FromArgb(245, 158, 45);
        static Color FlowPurple { get { return Ui.Atelier ? Color.FromArgb(180, 83, 9) : Ui.Nordic ? Ui.SecondAccent : Color.FromArgb(139, 92, 246); } }
        static readonly Color FlowRed = Color.FromArgb(239, 68, 68);

        static Font badge; static string fontKey;
        static Font BadgeFont { get { string k = Ui.FontName; if (k != fontKey) { fontKey = k; badge = new Font(Ui.FontName, 7f, FontStyle.Bold); } return badge; } }

        bool IsHot(string key) { return hover != null && hover.Key == key; }
        int Radius { get { return Ui.S(Ui.Flow ? 14 : Ui.Navy ? 16 : 12); } }

        void Card(Graphics g, Rectangle r) { Card(g, r, false); }
        void Card(Graphics g, Rectangle r, bool hot) { Ui.FillCard(g, new Rectangle(r.X, r.Y, Math.Max(1, r.Width - 1), Math.Max(1, r.Height - 1)), Radius, hot); }

        void Title(Graphics g, Rectangle card, string title, string sub, int reserve)
        {
            int tw = Math.Max(1, card.Width - Ui.S(40) - reserve);
            TextRenderer.DrawText(g, title, Ui.Big, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(16), tw, Ui.S(26)), Ui.Text, L);
            if (sub != null) TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(card.X + Ui.S(20), card.Y + Ui.S(42), tw, Ui.S(18)), Ui.Muted, L);
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
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = Ui.Round(r, r.Height / 2)) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
            double clamped = Math.Max(0, Math.Min(1, frac));
            if (clamped <= 0) return;
            int fw = Math.Min(r.Width, Math.Max(r.Height, (int)Math.Round(r.Width * clamped)));
            using (var path = Ui.Round(new Rectangle(r.X, r.Y, fw, r.Height), r.Height / 2)) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        }

        static void Pill(Graphics g, Rectangle r, Color bg, Color fg, string text)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var path = Ui.Round(r, r.Height / 2)) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
            TextRenderer.DrawText(g, text, Ui.Small, r, fg, C);
        }

        static Color Usage(double f) { return f > 0.9 ? Ui.Danger : f > 0.8 ? Ui.Warning : Ui.Accent; }

        // ---------- paint dispatch ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            if (header.Width <= Ui.S(40) || header.Height <= 0) return;   // not measured yet
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var clip = e.ClipRectangle;
            if (Ui.Flow)
            {
                if (header.IntersectsWith(clip)) HeaderFlow(g);
                for (int i = 0; i < 4; i++) if (kpiCards[i].IntersectsWith(clip)) KpiCardFlow(g, i);
                if (storage.IntersectsWith(clip)) StorageFlow(g);
                if (donut.IntersectsWith(clip)) DonutFlow(g);
                if (weekCard.IntersectsWith(clip)) WeekFlow(g);
                if (apps.IntersectsWith(clip)) AppsFlow(g);
                if (overview.IntersectsWith(clip)) ActivityChartFlow(g);
                if (kanbanCard.IntersectsWith(clip)) KanbanFlow(g);
                return;
            }
            if (header.IntersectsWith(clip)) Header(g);
            if (overview.IntersectsWith(clip)) Overview(g);
            if (next.IntersectsWith(clip)) Next(g);
            if (today.IntersectsWith(clip)) Today(g);
            if (storage.IntersectsWith(clip)) Storage(g);
            if (apps.IntersectsWith(clip)) Apps(g);
        }

        // =========================================================================
        // FLOW DASHBOARD (FlowTask Project Dashboard Visual Layout)
        // =========================================================================
        void HeaderFlow(Graphics g)
        {
            DrawHomeTitle(g, btnNew.Width > 0 ? btnNew.X : bell.X);

            if (btnNew.Width > 0)
            {
                var r = btnNew;
                using (var path = Ui.Round(r, Ui.S(10))) using (var b = new SolidBrush(IsHot("new") ? Ui.AccentHover : Ui.AccentFill)) g.FillPath(b, path);
                BtnText(g, r, "\uE710", "New task", Color.White);
            }
            RoundButton(g, bell, "\uEA8F", "bell");
            int badgeN = dueToday;
            if (badgeN > 0)
            {
                var br = new Rectangle(bell.Right - Ui.S(15), bell.Y - Ui.S(3), Ui.S(18), Ui.S(18));
                using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, br);
                TextRenderer.DrawText(g, badgeN > 9 ? "9+" : badgeN.ToString(), BadgeFont, br, Color.White, C);
            }
            RoundButton(g, gear, "\uE713", "gear");
        }

        void KpiCardFlow(Graphics g, int i)
        {
            var r = kpiCards[i];
            Card(g, r, IsHot("kpi" + i));

            string label, value, detail;
            if (i == 0)
            {
                label = "Open tasks"; value = openTasks.ToString("N0");
                detail = dueToday > 0 ? dueToday + " due today" : "";
            }
            else if (i == 1)
            {
                label = "Completed"; value = doneTasks.ToString("N0");
                detail = "of " + totalTasks.ToString("N0") + (totalTasks == 1 ? " task" : " tasks");
            }
            else if (i == 2)
            {
                label = "Copied today"; value = todayValue[1].ToString("N0");
                detail = todayNote[1];
            }
            else
            {
                label = "Files opened"; value = todayValue[2].ToString("N0");
                detail = todayValue[3] > 0 ? todayValue[3] + " tagged urgent" : todayNote[2];
            }

            int inset = Ui.S(r.Width < Ui.S(150) ? 10 : 16);
            int width = Math.Max(1, r.Width - inset * 2);
            TextRenderer.DrawText(g, label, Ui.Small, new Rectangle(r.X + inset, r.Y + Ui.S(9), width, Ui.S(18)), Ui.Muted, L);
            TextRenderer.DrawText(g, value, Ui.Kpi, new Rectangle(r.X + inset, r.Y + Ui.S(26), width, Ui.S(29)), Ui.Text, L);
            if (detail.Length > 0)
                TextRenderer.DrawText(g, detail, Ui.Small, new Rectangle(r.X + inset, r.Y + Ui.S(56), width, Ui.S(16)), i == 3 && todayValue[3] > 0 ? Ui.Danger : Ui.Muted, L);
        }

        void StorageFlow(Graphics g)
        {
            var c = storage;
            Card(g, c);
            Title(g, c, "Storage", null, LinkRect(c, "View All").Width);
            Link(g, c, "View All", "storageall");
            if (drives.Count == 0)
            {
                TextRenderer.DrawText(g, "No fixed drives found", Ui.Base, new Rectangle(c.X + Ui.S(20), c.Y + Ui.S(64), Math.Max(1, c.Width - Ui.S(40)), Ui.S(28)), Ui.Muted, L);
                return;
            }
            int x = c.X + Ui.S(20), w = Math.Max(1, c.Width - Ui.S(40)), y = c.Y + Ui.S(54), rh = Ui.S(54);
            int maxBottom = drives.Count > 4 ? c.Bottom - Ui.S(26) : c.Bottom - Ui.S(10);
            int shown = 0;
            for (int i = 0; i < drives.Count; i++)
            {
                if (y + rh > maxBottom) break;
                var d = drives[i];
                Color accent = Usage(d.Used);
                int ib = Ui.S(34);
                var ir = new Rectangle(x, y + Ui.S(6), ib, ib);
                using (var path = Ui.Round(ir, Ui.S(9)))
                using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 44 : 28, accent))) g.FillPath(b, path);
                TextRenderer.DrawText(g, "\uEDA2", Ui.Icon(12f), ir, accent, C);

                int pct = (int)Math.Round(Math.Max(0, Math.Min(1, d.Used)) * 100);
                string pctStr = pct + "%";
                int pw = TextRenderer.MeasureText(pctStr, Ui.Bold).Width + Ui.S(6);
                int tx = ir.Right + Ui.S(12), tw = Math.Max(1, x + w - pw - Ui.S(8) - tx);
                string name = d.Letter + ":" + (d.Label.Length > 0 ? "  " + d.Label : " Local Disk");
                TextRenderer.DrawText(g, name, Ui.Bold, new Rectangle(tx, y + Ui.S(2), tw, Ui.S(20)), Ui.Text, L);
                TextRenderer.DrawText(g, pctStr, Ui.Bold, new Rectangle(x + w - pw, y + Ui.S(12), pw, Ui.S(22)), d.Used > 0.9 ? Ui.Danger : Ui.Text, L | TextFormatFlags.Right);
                Bar(g, new Rectangle(tx, y + Ui.S(26), Math.Max(1, x + w - pw - Ui.S(12) - tx), Ui.S(7)), d.Used, accent);
                TextRenderer.DrawText(g, Fmt.Bytes(d.Free) + " free of " + Fmt.Bytes(d.Total), Ui.Small, new Rectangle(tx, y + Ui.S(35), tw, Ui.S(16)), Ui.Muted, L);
                y += rh; shown++;
            }
            if (drives.Count > shown)
            {
                int rem = drives.Count - shown;
                TextRenderer.DrawText(g, "+" + rem + (rem == 1 ? " more drive" : " more drives"), Ui.Small, new Rectangle(x, c.Bottom - Ui.S(22), w, Ui.S(16)), Ui.Muted, L);
            }
        }

        void DonutFlow(Graphics g)
        {
            var c = donut;
            Card(g, c, IsHot("donuthead"));
            Title(g, c, "Tasks Overview", null, 0);

            int ringAreaTop = c.Y + Ui.S(52);
            int legendH = Ui.S(82);
            int availH = Math.Max(Ui.S(80), c.Bottom - ringAreaTop - legendH - Ui.S(12));
            int diam = Math.Max(Ui.S(76), Math.Min(Ui.S(134), Math.Min(availH, c.Width - Ui.S(56))));
            int thick = Math.Max(Ui.S(11), diam / 8);
            var rr = new Rectangle(c.X + (c.Width - diam) / 2, ringAreaTop + (availH - diam) / 2, diam, diam);

            using (var trackPen = new Pen(Ui.Track, thick)) g.DrawEllipse(trackPen, rr);

            int inProg = Math.Max(0, openTasks - overdue);
            if (totalTasks > 0)
            {
                float start = -90f;
                float aDone = (float)(doneTasks * 360.0 / totalTasks);
                float aProg = (float)(inProg * 360.0 / totalTasks);
                float aOver = Math.Max(0f, 360f - aDone - aProg);
                if (aDone > 0.5f) { using (var p = new Pen(Ui.AccentFill, thick)) g.DrawArc(p, rr, start, aDone); start += aDone; }
                if (aProg > 0.5f) { using (var p = new Pen(FlowSky, thick)) g.DrawArc(p, rr, start, aProg); start += aProg; }
                if (aOver > 0.5f) { using (var p = new Pen(FlowAmber, thick)) g.DrawArc(p, rr, start, aOver); }
            }

            // Center total
            var centerNum = new Rectangle(rr.X + thick, rr.Y + diam / 2 - Ui.S(20), Math.Max(1, diam - 2 * thick), Ui.S(26));
            var centerSub = new Rectangle(rr.X + thick, centerNum.Bottom - Ui.S(2), Math.Max(1, diam - 2 * thick), Ui.S(18));
            TextRenderer.DrawText(g, totalTasks.ToString("N0"), Ui.Big, centerNum, Ui.Text, C);
            TextRenderer.DrawText(g, "Total Tasks", Ui.Small, centerSub, Ui.Muted, C);

            // 3-row bottom legend
            string[] legLbl = { "Completed", "In Progress", "Overdue" };
            int[] legVal = { doneTasks, inProg, overdue };
            Color[] legCol = { Ui.AccentFill, FlowSky, overdue > 0 ? FlowAmber : Ui.Muted };
            int ly = c.Bottom - legendH - Ui.S(6), lx = c.X + Ui.S(20), lw = Math.Max(1, c.Width - Ui.S(40));
            for (int i = 0; i < 3; i++)
            {
                int rowY = ly + i * Ui.S(24);
                using (var b = new SolidBrush(legCol[i])) g.FillEllipse(b, lx, rowY + Ui.S(7), Ui.S(8), Ui.S(8));
                string v = legVal[i].ToString("N0");
                int vw = TextRenderer.MeasureText(v, Ui.Bold).Width + Ui.S(4);
                TextRenderer.DrawText(g, legLbl[i], Ui.Small, new Rectangle(lx + Ui.S(16), rowY, Math.Max(1, lw - vw - Ui.S(20)), Ui.S(22)), Ui.Muted, L);
                TextRenderer.DrawText(g, v, Ui.Bold, new Rectangle(lx + lw - vw, rowY, vw, Ui.S(22)), Ui.Text, L | TextFormatFlags.Right);
            }
        }

        void WeekFlow(Graphics g)
        {
            var c = weekCard;
            Card(g, c);
            Title(g, c, "This Week", null, LinkRect(c, "View All").Width);
            Link(g, c, "View All", "tasksall");

            // 7-day strip (Mon..Sun)
            var todayDate = DateTime.Today;
            int dow = ((int)todayDate.DayOfWeek + 6) % 7;
            var mon = todayDate.AddDays(-dow);
            string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            int sx = c.X + Ui.S(16), sw = Math.Max(Ui.S(140), c.Width - Ui.S(32));
            int colW = sw / 7;
            int stripY = c.Y + Ui.S(52);
            for (int d = 0; d < 7; d++)
            {
                int cx = sx + d * colW + colW / 2;
                var dt = mon.AddDays(d);
                bool isToday = d == dow;
                TextRenderer.DrawText(g, days[d], Ui.Small, new Rectangle(sx + d * colW, stripY, colW, Ui.S(16)), isToday ? Ui.Accent : Ui.Muted, C);
                int cd = Ui.S(28);
                var cr = new Rectangle(cx - cd / 2, stripY + Ui.S(20), cd, cd);
                if (isToday) using (var b = new SolidBrush(Ui.AccentFill)) g.FillEllipse(b, cr);
                TextRenderer.DrawText(g, dt.Day.ToString(), isToday ? Ui.Bold : Ui.Base, cr, isToday ? Color.White : Ui.Text, C);
                if (weekHasTask[d])
                    using (var b = new SolidBrush(isToday ? FlowAmber : Ui.Accent)) g.FillEllipse(b, cx - Ui.S(2), cr.Bottom + Ui.S(4), Ui.S(5), Ui.S(5));
            }
            using (var p = new Pen(Ui.Line)) g.DrawLine(p, c.X + Ui.S(18), c.Y + Ui.S(116), c.Right - Ui.S(18), c.Y + Ui.S(116));

            if (upNext.Count == 0)
            {
                var er = new Rectangle(c.X + Ui.S(20), c.Y + Ui.S(134), Math.Max(1, c.Width - Ui.S(40)), Ui.S(54));
                TextRenderer.DrawText(g, "No tasks planned yet. Click here or press Ctrl+Alt+J to add your first task.", Ui.Base, er, Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                return;
            }
            for (int i = 0; i < taskRows.Count && i < upNext.Count; i++)
            {
                var t = upNext[i]; var r = taskRows[i];
                if (IsHot("task" + i)) using (var path = Ui.Round(r, Ui.S(9))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                bool late = TaskParse.Overdue(t);
                Color dotCol = late ? FlowRed : t.Priority == 2 ? FlowAmber : i == 0 ? Ui.AccentFill : FlowGreen;
                using (var b = new SolidBrush(dotCol)) g.FillEllipse(b, r.X + Ui.S(6), r.Y + Ui.S(14), Ui.S(8), Ui.S(8));
                string due = t.Due > 0 ? TaskParse.DueText(t) : t.List;
                int dw = Math.Min(r.Width / 3, TextRenderer.MeasureText(due, Ui.Small).Width + Ui.S(8));
                int tx = r.X + Ui.S(22), tw = Math.Max(1, r.Right - Ui.S(8) - dw - Ui.S(6) - tx);
                TextRenderer.DrawText(g, t.Title.Length > 0 ? Ui.OneLine(t.Title, 140) : "(untitled)", Ui.Bold, new Rectangle(tx, r.Y + Ui.S(4), tw, Ui.S(20)), Ui.Text, L);
                TextRenderer.DrawText(g, t.List + (t.Steps.Count > 0 ? "  ·  " + t.Steps.Count(s => s.Done) + "/" + t.Steps.Count + " steps" : ""), Ui.Small, new Rectangle(tx, r.Y + Ui.S(24), tw, Ui.S(17)), Ui.Muted, L);
                TextRenderer.DrawText(g, due, Ui.Small, new Rectangle(r.Right - Ui.S(8) - dw, r.Y, dw, r.Height), late ? Ui.Danger : Ui.Muted, L | TextFormatFlags.Right);
            }
        }

        void AppsFlow(Graphics g)
        {
            var c = apps;
            Card(g, c);
            Title(g, c, "Top Memory Apps", null, LinkRect(c, "View All").Width);
            Link(g, c, "View All", "appsall");
            int y0 = c.Y + Ui.S(56), rh = Ui.S(42), x = c.X + Ui.S(20), w = Math.Max(1, c.Width - Ui.S(40));
            if (topApps.Count == 0)
            {
                TextRenderer.DrawText(g, "Waiting for the first sample...", Ui.Base, new Rectangle(x, y0, w, Ui.S(36)), Ui.Muted, L);
                return;
            }
            double totalMem = Math.Max(1, (double)ProcessSampler.MemTotal);
            Color[] avCols = { FlowBlue, FlowPurple, FlowGreen, FlowAmber, FlowRed };
            int maxRows = Math.Min(topApps.Count, Math.Max(1, (c.Bottom - y0 - Ui.S(10)) / rh));
            for (int i = 0; i < maxRows; i++)
            {
                var a = topApps[i];
                int y = y0 + i * rh;
                int av = Ui.S(30);
                var ar = new Rectangle(x, y + (rh - av) / 2, av, av);
                int icon = string.IsNullOrEmpty(a.Path) ? 0 : ProcMeta.IconFor(a.Path);
                if (icon > 0)
                {
                    using (var b = new SolidBrush(Ui.Bg)) g.FillEllipse(b, ar);
                    int isz = Ui.S(16);
                    ProcMeta.Icons.Draw(g, ar.X + (av - isz) / 2, ar.Y + (av - isz) / 2, icon);
                }
                else
                {
                    Color ac = avCols[i % avCols.Length];
                    using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 48 : 30, ac))) g.FillEllipse(b, ar);
                    TextRenderer.DrawText(g, a.Name.Length > 0 ? a.Name.Substring(0, 1).ToUpperInvariant() : "?", Ui.Bold, ar, ac, C);
                }
                double share = a.Mem / totalMem;
                Color pillFg = share > 0.20 ? FlowRed : share > 0.10 ? FlowAmber : FlowGreen;
                Color pillBg = Color.FromArgb(Ui.Dark ? 40 : 28, pillFg);
                string pillText = "●  " + Fmt.Bytes(a.Mem);
                int pw = Math.Min(w / 2, TextRenderer.MeasureText(pillText, Ui.Small).Width + Ui.S(18));
                var pr = new Rectangle(x + w - pw, y + (rh - Ui.S(24)) / 2, pw, Ui.S(24));
                Pill(g, pr, pillBg, Ui.Dark ? Ui.Mix(pillFg, Color.White, 0.25) : Ui.Mix(pillFg, Color.Black, 0.15), pillText);

                int tx = ar.Right + Ui.S(12), tw = Math.Max(1, pr.Left - Ui.S(10) - tx);
                TextRenderer.DrawText(g, a.Name.Length > 0 ? a.Name : "?", Ui.Bold, new Rectangle(tx, y + Ui.S(3), tw, Ui.S(19)), Ui.Text, L);
                string sub = (a.Count > 1 ? a.Count + " processes  ·  " : "1 process  ·  ") + (share * 100).ToString(share < 0.1 ? "0.0" : "0") + "% RAM";
                TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(tx, y + Ui.S(21), tw, Ui.S(16)), Ui.Muted, L);
            }
        }

        void ActivityChartFlow(Graphics g)
        {
            var c = overview;
            Card(g, c, IsHot("overview"));
            string badgeTxt = "Live 2 min";
            int bw = TextRenderer.MeasureText(badgeTxt, Ui.Small).Width + Ui.S(18);
            var br = new Rectangle(c.Right - Ui.S(20) - bw, c.Y + Ui.S(16), bw, Ui.S(24));
            Title(g, c, "System Activity", null, bw + Ui.S(12));
            using (var path = Ui.Round(br, Ui.S(8)))
            {
                using (var b = new SolidBrush(Ui.Bg)) g.FillPath(b, path);
                using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, badgeTxt, Ui.Small, br, Ui.Muted, C);

            int axisW = Ui.S(34), legendH = Ui.S(28), xLblH = Ui.S(20);
            var plot = new Rectangle(c.X + Ui.S(18) + axisW, c.Y + Ui.S(58), Math.Max(Ui.S(80), c.Width - Ui.S(38) - axisW), Math.Max(Ui.S(70), c.Height - Ui.S(68) - legendH - xLblH));
            using (var p = new Pen(Ui.Line) { DashStyle = DashStyle.Dot })
                for (int k = 0; k <= 4; k++)
                {
                    int gy = plot.Y + plot.Height * k / 4;
                    g.DrawLine(p, plot.X, gy, plot.Right, gy);
                    TextRenderer.DrawText(g, (100 - k * 25).ToString(), Ui.Small, new Rectangle(c.X + Ui.S(12), gy - Ui.S(9), axisW - Ui.S(2), Ui.S(18)), Ui.Muted, L | TextFormatFlags.Right);
                }

            // Build 7 stacked vertical columns from live CPU & RAM history
            const int buckets = 7;
            string[] xLbls = { "-2m", "-100s", "-80s", "-60s", "-40s", "-20s", "Now" };
            double[] bCpu = new double[buckets], bRam = new double[buckets], bCap = new double[buckets];
            for (int b = 0; b < buckets; b++)
            {
                bCpu[b] = SampleBucket(cpu, b, buckets, cpuNow);
                bRam[b] = SampleBucket(ram, b, buckets, memLoad);
                bCap[b] = Math.Min(100, Math.Max(bCpu[b], bRam[b]) + 14);
            }

            int slotW = Math.Max(Ui.S(14), plot.Width / buckets);
            int barW = Math.Max(Ui.S(10), Math.Min(Ui.S(26), slotW * 48 / 100));
            Color capCol = Ui.Atelier
                ? (Ui.Dark ? Color.FromArgb(42, 48, 64) : Color.FromArgb(232, 224, 209))
                : Ui.Nordic
                    ? (Ui.Dark ? Color.FromArgb(34, 52, 46) : Color.FromArgb(219, 232, 226))
                    : (Ui.Dark ? Color.FromArgb(38, 58, 98) : Color.FromArgb(219, 232, 255));
            for (int b = 0; b < buckets; b++)
            {
                int cx = plot.X + b * slotW + slotW / 2;
                int bx = cx - barW / 2;
                double vCpu = Math.Max(0, Math.Min(100, bCpu[b]));
                double vRam = Math.Max(0, Math.Min(100, bRam[b]));
                // Visual stacked representation: bottom = CPU (scaled cleanly), middle = Memory, top = peak headroom
                int hTotal = Math.Max(Ui.S(10), (int)Math.Round(Math.Min(100, Math.Max(vRam, vCpu) + 18) * plot.Height / 100.0));
                int hMid = Math.Max(Ui.S(6), (int)Math.Round(Math.Max(vRam, vCpu) * plot.Height / 100.0));
                int hBot = Math.Max(Ui.S(4), (int)Math.Round(Math.Min(vCpu, Math.Max(vRam, vCpu)) * plot.Height / 100.0));
                if (hBot > hMid - Ui.S(4)) hBot = Math.Max(Ui.S(4), hMid * 55 / 100);

                var fullBar = new Rectangle(bx, plot.Bottom - hTotal, barW, hTotal);
                using (var path = Ui.Round(fullBar, Ui.S(6)))
                {
                    using (var brCap = new SolidBrush(capCol)) g.FillPath(brCap, path);
                    using (var oldClip = g.Clip)
                    {
                        g.SetClip(path);
                        using (var brMid = new SolidBrush(FlowSky)) g.FillRectangle(brMid, bx, plot.Bottom - hMid, barW, hMid);
                        using (var brBot = new SolidBrush(Ui.AccentFill)) g.FillRectangle(brBot, bx, plot.Bottom - hBot, barW, hBot);
                        g.Clip = oldClip;
                    }
                }
                TextRenderer.DrawText(g, xLbls[b], Ui.Small, new Rectangle(plot.X + b * slotW, plot.Bottom + Ui.S(4), slotW, xLblH), Ui.Muted, C);
            }

            // Bottom dot legend
            string[] leg = { "CPU " + cpuNow.ToString("0") + "%", "Memory " + memLoad + "%", "Peak " + cpuPeak.ToString("0") + "%" };
            Color[] legC = { Ui.AccentFill, FlowSky, capCol };
            int totalLegW = 0;
            int[] lws = new int[3];
            for (int i = 0; i < 3; i++) { lws[i] = TextRenderer.MeasureText(leg[i], Ui.Small).Width + Ui.S(22); totalLegW += lws[i]; }
            totalLegW += Ui.S(28);
            int lx = c.X + Math.Max(Ui.S(16), (c.Width - totalLegW) / 2);
            int ly = c.Bottom - legendH - Ui.S(4);
            for (int i = 0; i < 3; i++)
            {
                using (var b = new SolidBrush(legC[i])) g.FillEllipse(b, lx, ly + Ui.S(7), Ui.S(8), Ui.S(8));
                TextRenderer.DrawText(g, leg[i], Ui.Small, new Rectangle(lx + Ui.S(13), ly, lws[i] - Ui.S(10), Ui.S(22)), Ui.Muted, L);
                lx += lws[i] + Ui.S(14);
            }
        }

        static double SampleBucket(double[] data, int bucket, int buckets, double fallback)
        {
            if (data == null || data.Length == 0) return fallback;
            if (data.Length < buckets)
            {
                int idx = Math.Min(data.Length - 1, bucket * data.Length / buckets);
                return data[idx];
            }
            int from = bucket * data.Length / buckets;
            int to = Math.Max(from + 1, (bucket + 1) * data.Length / buckets);
            double sum = 0;
            for (int i = from; i < to && i < data.Length; i++) sum += data[i];
            return sum / Math.Max(1, Math.Min(to, data.Length) - from);
        }

        void KanbanFlow(Graphics g)
        {
            var c = kanbanCard;
            Card(g, c);
            // Tiny blue columns glyph next to "Task Board"
            TextRenderer.DrawText(g, "\uE8A9", Ui.Icon(12f), new Rectangle(c.X + Ui.S(18), c.Y + Ui.S(17), Ui.S(22), Ui.S(24)), Ui.Accent, C);
            TextRenderer.DrawText(g, "Task Board", Ui.Big, new Rectangle(c.X + Ui.S(42), c.Y + Ui.S(16), Math.Max(1, c.Width - LinkRect(c, "View Full Board").Width - Ui.S(64)), Ui.S(26)), Ui.Text, L);
            Link(g, c, "View Full Board", "kanbanall");

            string[] titles = { "To Do", "Due Today", "High / Late", "Done" };
            Color[] colBgs = Ui.Atelier
                ? (Ui.Dark
                    ? new[] { Color.FromArgb(25, 29, 40), Color.FromArgb(34, 26, 26), Color.FromArgb(38, 31, 22), Color.FromArgb(20, 36, 36) }
                    : new[] { Color.FromArgb(239, 234, 224), Color.FromArgb(246, 235, 228), Color.FromArgb(247, 238, 222), Color.FromArgb(231, 240, 238) })
                : Ui.Nordic
                    ? (Ui.Dark
                        ? new[] { Color.FromArgb(27, 39, 35), Color.FromArgb(22, 38, 48), Color.FromArgb(40, 34, 22), Color.FromArgb(20, 44, 36) }
                        : new[] { Color.FromArgb(231, 236, 233), Color.FromArgb(230, 239, 246), Color.FromArgb(247, 240, 224), Color.FromArgb(226, 242, 235) })
                    : (Ui.Dark
                        ? new[] { Color.FromArgb(24, 33, 53), Color.FromArgb(22, 37, 66), Color.FromArgb(43, 35, 20), Color.FromArgb(18, 43, 34) }
                        : new[] { Color.FromArgb(244, 246, 250), Color.FromArgb(238, 244, 255), Color.FromArgb(255, 248, 230), Color.FromArgb(236, 253, 245) });
            Color[] titleCols = Ui.Atelier
                ? (Ui.Dark
                    ? new[] { Color.FromArgb(214, 211, 203), Color.FromArgb(255, 130, 96), Color.FromArgb(251, 191, 36), Color.FromArgb(45, 212, 191) }
                    : new[] { Color.FromArgb(74, 71, 64), Color.FromArgb(217, 72, 34), Color.FromArgb(180, 83, 9), Color.FromArgb(31, 94, 91) })
                : Ui.Nordic
                    ? (Ui.Dark
                        ? new[] { Color.FromArgb(200, 216, 210), Color.FromArgb(99, 179, 237), Color.FromArgb(251, 191, 36), Color.FromArgb(32, 201, 151) }
                        : new[] { Color.FromArgb(52, 74, 66), Color.FromArgb(43, 108, 176), Color.FromArgb(180, 83, 9), Color.FromArgb(13, 138, 100) })
                    : (Ui.Dark
                        ? new[] { Color.FromArgb(203, 213, 225), Color.FromArgb(147, 197, 253), Color.FromArgb(252, 211, 77), Color.FromArgb(110, 231, 183) }
                        : new[] { Color.FromArgb(51, 65, 85), Color.FromArgb(37, 99, 235), Color.FromArgb(180, 83, 9), Color.FromArgb(5, 150, 105) });

            for (int i = 0; i < 4; i++)
            {
                var cr = kanbanCols[i];
                if (cr.Width <= 0) continue;
                using (var path = Ui.Round(cr, Ui.S(12))) using (var b = new SolidBrush(colBgs[i])) g.FillPath(b, path);

                int cnt = kanbanTasks[i].Count;
                int tw = TextRenderer.MeasureText(titles[i], Ui.Bold).Width + Ui.S(4);
                TextRenderer.DrawText(g, titles[i], Ui.Bold, new Rectangle(cr.X + Ui.S(12), cr.Y + Ui.S(10), Math.Min(cr.Width - Ui.S(46), tw), Ui.S(22)), titleCols[i], L);
                var badgeR = new Rectangle(cr.X + Ui.S(14) + Math.Min(cr.Width - Ui.S(52), tw), cr.Y + Ui.S(12), Ui.S(24), Ui.S(18));
                Pill(g, badgeR, Color.FromArgb(Ui.Dark ? 55 : 32, titleCols[i]), titleCols[i], cnt > 99 ? "99+" : cnt.ToString());

                if (cnt == 0)
                {
                    TextRenderer.DrawText(g, i == 3 ? "No completed tasks yet" : "Nothing here", Ui.Small, new Rectangle(cr.X + Ui.S(12), cr.Y + Ui.S(46), Math.Max(1, cr.Width - Ui.S(24)), Ui.S(24)), Ui.Muted, L);
                }
                else
                {
                    int show = Math.Min(3, Math.Min(cnt, kanbanTaskRects[i].Count));
                    for (int j = 0; j < show; j++)
                    {
                        var t = kanbanTasks[i][j];
                        var tr = kanbanTaskRects[i][j];
                        bool hot = IsHot("kb_" + i + "_" + j);
                        using (var path = Ui.Round(tr, Ui.S(9)))
                        {
                            using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path);
                            using (var p = new Pen(hot ? Ui.Accent : Color.FromArgb(Ui.Dark ? 50 : 28, titleCols[i]))) g.DrawPath(p, path);
                        }
                        if (i == 3) // Done card with Emerald checkmark circle
                        {
                            int cd = Ui.S(22);
                            var chk = new Rectangle(tr.X + Ui.S(10), tr.Y + (tr.Height - cd) / 2, cd, cd);
                            using (var b = new SolidBrush(FlowGreen)) g.FillEllipse(b, chk);
                            TextRenderer.DrawText(g, "\uE73E", Ui.Icon(9.5f), chk, Color.White, C);
                            int tx = chk.Right + Ui.S(10), wT = Math.Max(1, tr.Right - tx - Ui.S(10));
                            TextRenderer.DrawText(g, t.Title.Length > 0 ? Ui.OneLine(t.Title, 80) : "(untitled)", Ui.Bold, new Rectangle(tx, tr.Y + Ui.S(9), wT, Ui.S(20)), Ui.Text, L);
                            TextRenderer.DrawText(g, t.List, Ui.Small, new Rectangle(tx, tr.Y + Ui.S(31), wT, Ui.S(17)), Ui.Muted, L);
                        }
                        else
                        {
                            int tx = tr.X + Ui.S(10), wT = Math.Max(1, tr.Width - Ui.S(20));
                            TextRenderer.DrawText(g, t.Title.Length > 0 ? Ui.OneLine(t.Title, 80) : "(untitled)", Ui.Bold, new Rectangle(tx, tr.Y + Ui.S(7), wT, Ui.S(20)), Ui.Text, L);
                            string pLabel = t.Priority == 2 ? "High" : t.Priority == 1 ? "Medium" : "Low";
                            Color pCol = t.Priority == 2 ? FlowRed : t.Priority == 1 ? FlowAmber : FlowGreen;
                            int pw = TextRenderer.MeasureText(pLabel, Ui.Small).Width + Ui.S(14);
                            var pr = new Rectangle(tx, tr.Y + Ui.S(31), pw, Ui.S(19));
                            Pill(g, pr, Color.FromArgb(Ui.Dark ? 46 : 30, pCol), Ui.Dark ? Ui.Mix(pCol, Color.White, 0.25) : Ui.Mix(pCol, Color.Black, 0.12), pLabel);
                            string due = t.Due > 0 ? TaskParse.DueText(t) : t.List;
                            int dx = pr.Right + Ui.S(8);
                            if (dx + Ui.S(20) < tr.Right - Ui.S(8))
                                TextRenderer.DrawText(g, due, Ui.Small, new Rectangle(dx, tr.Y + Ui.S(31), Math.Max(1, tr.Right - Ui.S(8) - dx), Ui.S(19)), TaskParse.Overdue(t) ? Ui.Danger : Ui.Muted, L);
                        }
                    }
                }

                var ar = kanbanAddRects[i];
                if (IsHot("kbadd" + i)) using (var path = Ui.Round(ar, Ui.S(7))) using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path);
                TextRenderer.DrawText(g, "+ Add Task", Ui.Small, ar, IsHot("kbadd" + i) ? Ui.Accent : Ui.Muted, C);
            }
        }

        // =========================================================================
        // SERQ / NEXUS DASHBOARD (preserved intact when !Ui.Flow)
        // =========================================================================
        void Header(Graphics g)
        {
            DrawHomeTitle(g, btnTerm.Width > 0 ? btnTerm.X : btnNew.Width > 0 ? btnNew.X : bell.X);

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
            int badgeN = dueToday;
            if (badgeN > 0)
            {
                var br = new Rectangle(bell.Right - Ui.S(15), bell.Y - Ui.S(3), Ui.S(18), Ui.S(18));
                using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, br);
                TextRenderer.DrawText(g, badgeN > 9 ? "9+" : badgeN.ToString(), BadgeFont, br, Color.White, C);
            }
            RoundButton(g, gear, "", "gear");
        }

        void DrawHomeTitle(Graphics g, int rightEdge)
        {
            int width = Math.Max(1, rightEdge - Ui.S(10) - header.X);
            TextRenderer.DrawText(g, "Home", Ui.Title, new Rectangle(header.X, header.Y, width, Ui.S(36)), Ui.Text, L);
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
            Title(g, c, "CPU and memory", "Last 2 minutes, sampled every 2 seconds", Math.Max(0, c.Right - lx));
            int ly = c.Y + Ui.S(18);
            using (var b = new SolidBrush(Cpu)) g.FillEllipse(b, lx, ly + Ui.S(8), Ui.S(10), Ui.S(10));
            TextRenderer.DrawText(g, l1, Ui.Bold, new Rectangle(lx + Ui.S(16), ly, w1 + 4, Ui.S(26)), Ui.Text, L);
            int lx2 = lx + Ui.S(16) + w1 + Ui.S(18);
            using (var b = new SolidBrush(Mem)) g.FillEllipse(b, lx2, ly + Ui.S(8), Ui.S(10), Ui.S(10));
            TextRenderer.DrawText(g, l2, Ui.Bold, new Rectangle(lx2 + Ui.S(16), ly, w2 + 4, Ui.S(26)), Ui.Text, L);

            int statsH = Ui.S(62);
            int axis = Ui.S(38);
            var plot = new Rectangle(c.X + Ui.S(22) + axis, c.Y + Ui.S(78), Math.Max(Ui.S(40), c.Width - Ui.S(44) - axis), Math.Max(Ui.S(40), c.Height - Ui.S(78) - statsH - Ui.S(18)));
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
            var s = new Rectangle(c.X + Ui.S(22), c.Bottom - statsH - Ui.S(10), Math.Max(1, c.Width - Ui.S(44)), statsH);
            using (var p = new Pen(Ui.Line)) g.DrawLine(p, s.X, s.Y, s.Right, s.Y);
            int third = Math.Max(1, s.Width / 3);
            Stat(g, new Rectangle(s.X, s.Y, third, s.Height), "CPU peak (2 min)", cpuPeak.ToString("0") + "%");
            Stat(g, new Rectangle(s.X + third, s.Y, third, s.Height), "Memory in use", ProcessSampler.MemTotal == 0 ? "-" : memUsed + " of " + memTotal);
            Stat(g, new Rectangle(s.X + 2 * third, s.Y, Math.Max(1, s.Width - 2 * third), s.Height), "Processes", ProcessSampler.Last.Count.ToString("N0"));
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
            int w = Math.Max(1, r.Width - Ui.S(8));
            TextRenderer.DrawText(g, label, Ui.Small, new Rectangle(r.X, r.Y + Ui.S(10), w, Ui.S(18)), Ui.Muted, L);
            TextRenderer.DrawText(g, value, Ui.Big, new Rectangle(r.X, r.Y + Ui.S(28), w, Ui.S(28)), Ui.Text, L);
        }

        void Next(Graphics g)
        {
            var c = next;
            Card(g, c);
            Title(g, c, "Up next", openTasks == 0 ? "No open tasks" : openTasks + (openTasks == 1 ? " open task" : " open tasks"), LinkRect(c, "All tasks").Width);
            Link(g, c, "All tasks", "tasksall");
            if (upNext.Count == 0)
            {
                var r = new Rectangle(c.X + Ui.S(22), c.Y + Ui.S(84), Math.Max(1, c.Width - Ui.S(44)), Ui.S(60));
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
                int tw = Math.Max(1, r.Right - Ui.S(10) - dw - Ui.S(10) - x);
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
                int x = r.X + Ui.S(22), w = Math.Max(1, r.Width - Ui.S(36));
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
            int x = c.X + Ui.S(22), w = Math.Max(1, c.Width - Ui.S(44)), y = c.Y + Ui.S(78), rh = Ui.S(52);
            foreach (var d in drives)
            {
                if (y + rh > c.Bottom - Ui.S(10)) break;
                string name = d.Letter + ":" + (d.Label.Length > 0 ? "  " + d.Label : "");
                string val = Fmt.Bytes(d.Free) + " free of " + Fmt.Bytes(d.Total);
                int vw = TextRenderer.MeasureText(val, Ui.Small).Width + Ui.S(4);
                TextRenderer.DrawText(g, name, Ui.Bold, new Rectangle(x, y, Math.Max(1, w - vw - Ui.S(8)), Ui.S(22)), Ui.Text, L);
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
            int y0 = c.Y + Ui.S(76), rh = Ui.S(44), w = Math.Max(1, c.Width - Ui.S(44));
            if (topApps.Count == 0) { TextRenderer.DrawText(g, "Waiting for the first sample...", Ui.Base, new Rectangle(c.X + Ui.S(22), y0, w, Ui.S(40)), Ui.Muted, L); return; }
            int cols = w > Ui.S(760) ? 2 : 1, cgap = Ui.S(28);
            int colW = Math.Max(1, (w - cgap * (cols - 1)) / cols);
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
                int tx = ir.Right + Ui.S(10), tw = Math.Max(1, x + colW - vw - Ui.S(8) - tx);
                TextRenderer.DrawText(g, (a.Name.Length > 0 ? a.Name : "?") + (a.Count > 1 ? "  (" + a.Count + ")" : ""), Ui.Base, new Rectangle(tx, y, tw, Ui.S(24)), Ui.Text, L);
                TextRenderer.DrawText(g, val, Ui.Small, new Rectangle(x + colW - vw, y, vw, Ui.S(24)), Ui.Muted, L | TextFormatFlags.Right);
                Bar(g, new Rectangle(tx, y + Ui.S(28), Math.Max(1, x + colW - tx), Ui.S(5)), share, Mem);
            }
        }

        // ---------- mouse ----------
        Hot At(Point p)
        {
            for (int i = hots.Count - 1; i >= 0; i--)
            {
                var h = hots[i];
                if (!h.R.Contains(p)) continue;
                if (h.Key == "taskempty") { if (upNext.Count > 0) continue; return h; }
                if (h.Key.StartsWith("task") && h.Key != "tasksall") { int k; if (int.TryParse(h.Key.Substring(4), out k) && k >= upNext.Count) continue; }
                if (h.Key.StartsWith("kb_"))
                {
                    var parts = h.Key.Split('_');
                    int ci, cj;
                    if (parts.Length == 3 && int.TryParse(parts[1], out ci) && int.TryParse(parts[2], out cj))
                        if (ci < 0 || ci >= 4 || cj >= kanbanTasks[ci].Count) continue;
                }
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
