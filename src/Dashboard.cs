using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    class DashboardFacts
    {
        public int ClipsToday, PromptCount, RemindersToday, FilesToday, Urgent;
        public string MostUsed, Recording;
        public static DashboardFacts Read(long now)
        {
            long today = new DateTime(now).Date.Ticks, tomorrow = new DateTime(now).Date.AddDays(1).Ticks;
            var used = Store.Prompts.OrderByDescending(p => p.Uses).ThenByDescending(p => p.LastUsed).FirstOrDefault();
            return new DashboardFacts
            {
                ClipsToday = Store.Clips.Count(c => c.Time >= today && c.Time < tomorrow), PromptCount = Store.Prompts.Count,
                MostUsed = used != null && used.Uses > 0 ? used.Title + " (" + used.Uses + " uses)" : "No prompts used yet",
                RemindersToday = Store.Notes.Count(n => n.RemindAt >= today && n.RemindAt < tomorrow),
                FilesToday = Store.Activity.Where(a => a.Event == "Opened" && a.Time >= today && a.Time < tomorrow).Select(a => a.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Urgent = Store.Tagged.Count(t => string.Equals(t.Category, "Urgent", StringComparison.OrdinalIgnoreCase)),
                Recording = Recorder.Active ? (Recorder.Paused ? "Paused" : "Recording") : Recorder.Saving ? "Saving" : "Ready"
            };
        }
    }

    class MetricTile : Control
    {
        public string Value = "", Detail = "";
        public double Fraction = -1;
        public double[] Trend;
        bool hover;
        public MetricTile(string title, Action action)
        {
            Text = title; Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.PushButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            if (action != null) Click += delegate { action(); };
            Margin = new Padding(0, 0, Ui.S(12), Ui.S(12));
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.SuppressKeyPress = true; } base.OnKeyDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent == null ? Ui.Bg : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
            { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(hover || Focused ? Ui.Accent : Ui.Line)) g.DrawPath(p, path); }
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Text, Ui.Small, new Rectangle(Ui.S(14), Ui.S(10), Width - Ui.S(28), Ui.S(20)), Ui.Muted, flags);
            TextRenderer.DrawText(g, Value, Height > Ui.S(100) ? Ui.Metric : Ui.Big, new Rectangle(Ui.S(14), Ui.S(30), Width - Ui.S(28), Ui.S(42)), Ui.Text, flags);
            TextRenderer.DrawText(g, Detail, Ui.Small, new Rectangle(Ui.S(14), Ui.S(Height > Ui.S(100) ? 76 : 58), Width - Ui.S(28), Ui.S(42)), Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            if (Fraction >= 0)
            {
                var bar = new Rectangle(Ui.S(14), Height - Ui.S(10), Math.Max(1, Width - Ui.S(28)), Ui.S(4));
                using (var b = new SolidBrush(Ui.Track)) g.FillRectangle(b, bar);
                using (var b = new SolidBrush(Fraction > 0.9 ? Ui.Danger : Ui.Accent)) g.FillRectangle(b, bar.X, bar.Y, (int)(bar.Width * Math.Max(0, Math.Min(1, Fraction))), bar.Height);
            }
            if (Trend != null && Trend.Length > 1 && Height > Ui.S(100))
            {
                var points = new PointF[Trend.Length];
                for (int i = 0; i < points.Length; i++) points[i] = new PointF(Ui.S(14) + i * (Width - Ui.S(28)) / (float)(points.Length - 1), Height - Ui.S(12) - (float)Math.Max(0, Math.Min(100, Trend[i])) * Ui.S(22) / 100f);
                using (var pen = new Pen(Ui.Accent, 2)) g.DrawLines(pen, points);
            }
        }
    }

    class TileGrid : FlowLayoutPanel
    {
        readonly int minimum, tileHeight, maximumColumns;
        bool sizing;
        public TileGrid(int minimum, int height, int columns)
        { this.minimum = minimum; tileHeight = height; maximumColumns = columns; Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; WrapContents = true; Margin = new Padding(0); }
        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 ? proposedSize.Width : Parent != null ? Parent.ClientSize.Width - Parent.Padding.Horizontal : Width;
            width = Math.Max(80, width);
            int columns = Math.Max(1, Math.Min(maximumColumns, width / Math.Max(1, minimum)));
            return new Size(width, ((Controls.Count + columns - 1) / columns) * (tileHeight + Ui.S(12)));
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (!sizing)
            {
                sizing = true;
                int columns = Math.Max(1, Math.Min(maximumColumns, Math.Max(1, ClientSize.Width) / Math.Max(1, minimum)));
                int width = Math.Max(80, ClientSize.Width / columns - Ui.S(12));
                foreach (Control child in Controls) child.Size = new Size(width, tileHeight);
                sizing = false;
            }
            base.OnLayout(e);
        }
    }

    class DriveSnapshot
    {
        public string Value = "Unavailable", Detail = "";
        public double Used = -1;
        static DateTime stamp;
        static readonly Dictionary<string, DriveSnapshot> cache = new Dictionary<string, DriveSnapshot>();
        public static DriveSnapshot Read(string letter)
        {
            if ((DateTime.Now - stamp).TotalSeconds >= 5 || cache.Count == 0)
            {
                stamp = DateTime.Now; cache.Clear();
                foreach (string drive in new[] { "C", "D" })
                {
                    var snapshot = new DriveSnapshot();
                    try { var info = new DriveInfo(drive + @":\"); if (info.IsReady) { long free = info.AvailableFreeSpace, total = info.TotalSize; snapshot.Value = Fmt.Bytes(free) + " free"; snapshot.Detail = "of " + Fmt.Bytes(total); snapshot.Used = total > 0 ? 1 - free / (double)total : 0; } } catch { }
                    cache[drive] = snapshot;
                }
            }
            return cache[letter];
        }
    }

    // PicPick-style "Pick a task..." home: blue section headers, task rows of icon + bold title + grey description.
    class TaskHome : Control
    {
        class Task { public string Glyph, Title, Desc; public Action Run; public Rectangle Bounds; }
        class Section { public string Title; public int Column; public List<Task> Tasks = new List<Task>(); public Rectangle Header; }
        static readonly Font TitleFont = MakeTitleFont();
        static readonly Font HeaderFont = new Font(Ui.FontName, 12f, FontStyle.Bold);
        readonly List<Section> sections = new List<Section>();
        readonly List<Task> all = new List<Task>();
        Task hover; int focus = -1;
        Rectangle welcome, welcomeClose, divider;
        int columns = 2;

        static Font MakeTitleFont()
        {
            foreach (var name in new[] { "Segoe UI Variable Display Light", "Segoe UI Light", Ui.FontName })
                using (var test = new Font(name, 24f)) if (test.Name == name) return new Font(name, 24f);
            return new Font(Ui.FontName, 24f);
        }

        public TaskHome()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true; AccessibleRole = AccessibleRole.List; AccessibleName = "Pick a task";

            var s = Add("New task", 0);
            s.Tasks.Add(T("", "New note", "Sticky note on the desktop  (" + Key("note") + ")", () => App.I.NewNote(null)));
            s.Tasks.Add(T("", "New prompt", "Save a reusable prompt or snippet", () => App.I.NewPrompt()));
            s.Tasks.Add(T("", "New blank image", "Start a picture from an empty canvas", () => SnipEditor.NewBlank()));
            s.Tasks.Add(T("", "Open image file", "Edit, mark up, OCR or print a picture", () => SnipEditor.OpenFile(null)));
            s.Tasks.Add(T("", "Search everything", "Notes, prompts, clips and tagged files  (Ctrl+K)", () => GlobalSearch.Open(FindForm())));

            s = Add("Screen Capture", 0);
            s.Tasks.Add(T("", "Full Screen", "Capture the whole screen", () => App.I.StartSnip("full", 0, false)));
            s.Tasks.Add(T("", "Active Window", "Capture one window", () => App.I.StartSnip("window", 0, false)));
            s.Tasks.Add(T("", "Region", "Drag a rectangle  (" + Key("snip") + ")", () => App.I.StartSnip("rect", 0, false)));
            s.Tasks.Add(T("", "Fixed Region", "Same width x height every time", () => App.I.StartSnip("fixed", 0, false)));
            s.Tasks.Add(T("", "Window Control", "A button, panel or list inside a window", () => App.I.StartSnip("control", 0, false)));
            s.Tasks.Add(T("", "Scrolling Window", "A long page, scrolled and stitched", () => App.I.StartSnip("scroll", 0, false)));
            s.Tasks.Add(T("", "Freehand", "Draw any shape around the area", () => App.I.StartSnip("free", 0, false)));
            s.Tasks.Add(T("", "Repeat Last Capture", "Same mode and area again  (" + Key("repeat") + ")", () => App.I.StartSnip("repeat", 0, false)));
            s.Tasks.Add(T("", "Text from screen", "Copy the text inside an area (OCR)  (" + Key("ocr") + ")", () => App.I.StartSnip("rect", 0, true)));

            s = Add("Graphic Accessories", 1);
            s.Tasks.Add(T("", "Screen Recorder", "Record an area to MP4  (" + Key("record") + ")", () => App.I.StartRecord("rect")));
            foreach (var a in Accessories.List) { string id = a[0]; s.Tasks.Add(T(a[3], a[1], a[2], () => Accessories.Open(id))); }

            s = Add("Notes & prompts", 1);
            s.Tasks.Add(T("", "Clipboard history", "Everything you copied  (" + Key("clips") + ")", () => App.I.ShowMain("clips")));
            s.Tasks.Add(T("", "Prompts & snippets", "Your prompt library  (" + Key("prompts") + ")", () => App.I.ShowMain("prompts")));
            s.Tasks.Add(T("", "Sticky notes", "All notes, reminders and archive", () => App.I.ShowMain("notes")));
            s.Tasks.Add(T("", "Floating cards", "Tools that stay on top of other apps", () => App.I.ShowMain("cards")));

            s = Add("System", 1);
            s.Tasks.Add(T("", "System monitor", "Live processes, CPU and memory", () => App.I.ShowMain("system:procs")));
            s.Tasks.Add(T("", "Cleanup", "Free space on C and D", () => App.I.ShowMain("system:drives")));
            s.Tasks.Add(T("", "Startup apps", "Choose what starts with Windows", () => App.I.ShowMain("system:startup")));
            s.Tasks.Add(T("", "File activity", "Office files you opened and closed", () => App.I.ShowMain("activity")));
            s.Tasks.Add(T("", "Tagged files", "Files you marked as Urgent, Review ...", () => App.I.ShowMain("tagged")));
            foreach (var sec in sections) all.AddRange(sec.Tasks);
        }
        static string Key(string action) { var k = Hotkeys.Get(action); return k.Length > 0 ? k : "no shortcut"; }
        Section Add(string title, int column) { var s = new Section { Title = title, Column = column }; sections.Add(s); return s; }
        static Task T(string glyph, string title, string desc, Action run) { return new Task { Glyph = glyph, Title = title, Desc = desc, Run = run }; }

        bool ShowWelcome { get { return !Store.Settings.WelcomeHidden; } }

        // Lays out for a width and returns the height needed.
        public int Measure(int width)
        {
            columns = width >= Ui.S(720) ? 2 : 1;
            int gap = Ui.S(40), colW = columns == 2 ? (width - gap) / 2 : width;
            int top = Ui.S(58);
            if (ShowWelcome) { welcome = new Rectangle(0, top, width, Ui.S(64)); welcomeClose = new Rectangle(width - Ui.S(84), top + Ui.S(16), Ui.S(72), Ui.S(32)); top += Ui.S(80); }
            else welcome = welcomeClose = Rectangle.Empty;
            var y = new[] { top, top };
            foreach (var s in sections)
            {
                int c = columns == 2 ? s.Column : 0, x = c == 1 ? colW + gap : 0;
                s.Header = new Rectangle(x, y[c], colW, Ui.S(34)); y[c] += Ui.S(42);
                foreach (var t in s.Tasks) { t.Bounds = new Rectangle(x, y[c], colW, Ui.S(56)); y[c] += Ui.S(58); }
                y[c] += Ui.S(24);
            }
            divider = columns == 2 ? new Rectangle(colW + gap / 2, top, 1, Math.Max(y[0], y[1]) - top - Ui.S(24)) : Rectangle.Empty;
            return Math.Max(y[0], y[1]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, "Pick a task...", TitleFont, new Rectangle(0, 0, Width, Ui.S(50)), Ui.Text, flags);
            if (welcome.Width > 0)
            {
                using (var path = Ui.Round(welcome, Ui.S(8))) { using (var b = new SolidBrush(Ui.SelBg)) g.FillPath(b, path); }
                string text = "Welcome! Main shortcuts work in any app:  " + Key("clips") + " clipboard  ·  " + Key("prompts") + " prompts  ·  " + Key("note") + " new note  ·  " + Key("snip") + " snip  ·  " + Key("main") + " this window";
                TextRenderer.DrawText(g, text, Ui.Base, new Rectangle(welcome.X + Ui.S(14), welcome.Y, welcomeClose.X - welcome.X - Ui.S(20), welcome.Height), Ui.Text, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                using (var path = Ui.Round(welcomeClose, Ui.S(6))) { using (var b = new SolidBrush(Ui.AccentFill)) g.FillPath(b, path); }
                TextRenderer.DrawText(g, "Got it", Ui.Bold, welcomeClose, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            if (divider.Width > 0) using (var b = new SolidBrush(Ui.Line)) g.FillRectangle(b, divider);
            foreach (var s in sections)
            {
                TextRenderer.DrawText(g, s.Title, HeaderFont, s.Header, Ui.Header, flags);
                using (var b = new SolidBrush(Ui.Line)) g.FillRectangle(b, s.Header.X, s.Header.Bottom - 1, s.Header.Width, 1);
                foreach (var t in s.Tasks)
                {
                    var r = t.Bounds;
                    bool focused = Focused && focus >= 0 && all[focus] == t;
                    if (t == hover || focused)
                        using (var path = Ui.Round(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Ui.S(6)))
                        {
                            using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                            if (focused) using (var p = new Pen(Ui.Accent)) g.DrawPath(p, path);
                        }
                    TextRenderer.DrawText(g, t.Glyph, Ui.Icon(20f), new Rectangle(r.X + Ui.S(8), r.Y, Ui.S(36), r.Height), Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    int tx = r.X + Ui.S(56), tw = r.Width - Ui.S(60);
                    TextRenderer.DrawText(g, t.Title, Ui.Bold, new Rectangle(tx, r.Y + Ui.S(7), tw, Ui.S(22)), Ui.Text, flags);
                    TextRenderer.DrawText(g, t.Desc, Ui.Small, new Rectangle(tx, r.Y + Ui.S(29), tw, Ui.S(20)), Ui.Muted, flags);
                }
            }
        }

        Task At(Point p) { return all.FirstOrDefault(t => t.Bounds.Contains(p)); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            var t = At(e.Location);
            Cursor = t != null || welcomeClose.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (t != hover) { hover = t; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { if (hover != null) { hover = null; Invalidate(); } base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (welcomeClose.Contains(e.Location)) { Store.Settings.WelcomeHidden = true; Store.MarkSettings(); if (Parent != null) Parent.PerformLayout(); Invalidate(); return; }
            var t = At(e.Location);
            if (t != null) Run(t);
        }
        static void Run(Task t) { try { t.Run(); } catch (Exception ex) { App.Log("Task " + t.Title + ": " + ex.Message); } }
        protected override bool IsInputKey(Keys keyData) { var k = keyData & Keys.KeyCode; return k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || base.IsInputKey(keyData); }
        protected override void OnGotFocus(EventArgs e) { if (focus < 0) focus = 0; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (all.Count == 0) return;
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Right) { focus = Math.Min(all.Count - 1, focus + 1); Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Left) { focus = Math.Max(0, focus - 1); Invalidate(); e.Handled = true; }
            else if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && focus >= 0) { Run(all[focus]); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    class DashboardPanel : LivePanel
    {
        readonly Dictionary<string, MetricTile> tiles = new Dictionary<string, MetricTile>();
        readonly Action update;
        readonly TaskHome tasks;
        readonly Label liveHead;
        readonly TileGrid grid;
        bool laying;
        public DashboardPanel()
        {
            Dock = DockStyle.Fill; AutoScroll = true; BackColor = Ui.Bg;
            tasks = new TaskHome();
            liveHead = new Label { Text = "Live dashboard", Font = new Font(Ui.FontName, 12f, FontStyle.Bold), ForeColor = Ui.Header, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            liveHead.Disposed += delegate { liveHead.Font.Dispose(); };
            grid = new TileGrid(Ui.S(220), Ui.S(136), 4) { Dock = DockStyle.None, AutoSize = false };
            Add("cpu", "CPU", "system:procs"); Add("ram", "RAM", "system:procs"); Add("C", "Disk C", "system:drives"); Add("D", "Disk D", "system:drives");
            Add("apps", "Top 3 memory apps", "system:procs"); Add("clips", "Clipboard items today", "clips"); Add("prompts", "Prompts", "prompts");
            Add("notes", "Reminders due today", "notes"); Add("activity", "Files opened today", "activity"); Add("tagged", "Tagged Urgent", "tagged"); Add("record", "Screen recorder", "snip");
            Controls.Add(tasks); Controls.Add(liveHead); Controls.Add(grid);
            update = () => { if (Live) RefreshMetrics(); };
            ProcessSampler.Updated += update; Store.ClipsChanged += update; Store.PromptsChanged += update; Store.NotesChanged += update; Store.ActivityChanged += update; Store.TaggedChanged += update; Recorder.Finished += update;
        }
        void Add(string key, string title, string page) { var tile = new MetricTile(title, () => App.I.ShowMain(page)); tiles[key] = tile; grid.Controls.Add(tile); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (laying || tasks == null) return;
            laying = true;
            try
            {
                int pad = Ui.S(20), w = Math.Max(Ui.S(240), ClientSize.Width - pad * 2);
                int y = AutoScrollPosition.Y + Ui.S(12);
                int th = tasks.Measure(w); tasks.SetBounds(pad, y, w, th); tasks.Invalidate(); y += th;
                liveHead.SetBounds(pad, y, w, Ui.S(36)); y += Ui.S(44);
                int gh = grid.GetPreferredSize(new Size(w, 0)).Height; grid.SetBounds(pad, y, w, gh); y += gh + pad;
                var min = new Size(0, y - AutoScrollPosition.Y);
                if (AutoScrollMinSize != min) AutoScrollMinSize = min;
            }
            finally { laying = false; }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PerformLayout(); }
        protected override void OnLive(bool on) { ProcessSampler.Watch(this, on); if (on) RefreshMetrics(); }
        void RefreshMetrics()
        {
            var facts = DashboardFacts.Read(Store.Now());
            tiles["cpu"].Value = ProcessSampler.CpuTotal.ToString("0") + "%"; tiles["cpu"].Trend = ProcessSampler.CpuHistory.ToArray();
            tiles["ram"].Value = ProcessSampler.MemLoad + "%"; tiles["ram"].Trend = ProcessSampler.RamHistory.ToArray();
            foreach (string drive in new[] { "C", "D" }) { var d = DriveSnapshot.Read(drive); tiles[drive].Value = d.Value; tiles[drive].Detail = d.Detail; tiles[drive].Fraction = d.Used; }
            var top = ProcessSampler.Last.GroupBy(p => p.Display).Select(g => new { Name = g.Key, Memory = g.Sum(p => p.Mem) }).OrderByDescending(g => g.Memory).Take(3).ToList();
            tiles["apps"].Value = top.Count + " apps"; tiles["apps"].Detail = string.Join("\n", top.Select(p => p.Name + "  " + Fmt.Bytes(p.Memory)));
            tiles["clips"].Value = facts.ClipsToday.ToString("N0"); tiles["prompts"].Value = facts.PromptCount.ToString("N0"); tiles["prompts"].Detail = "Most used: " + facts.MostUsed;
            tiles["notes"].Value = facts.RemindersToday.ToString("N0"); tiles["activity"].Value = facts.FilesToday.ToString("N0"); tiles["activity"].Detail = "Distinct files, through file activity";
            tiles["tagged"].Value = facts.Urgent.ToString("N0"); tiles["record"].Value = facts.Recording;
            foreach (var tile in tiles.Values) { tile.AccessibleName = tile.Text + ": " + tile.Value; tile.Invalidate(); }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { ProcessSampler.Updated -= update; Store.ClipsChanged -= update; Store.PromptsChanged -= update; Store.NotesChanged -= update; Store.ActivityChanged -= update; Store.TaggedChanged -= update; Recorder.Finished -= update; }
            base.Dispose(disposing);
        }
    }

    // Ctrl+K: one box that searches notes, prompts, clips and tagged files.
    class GlobalSearch : Form
    {
        class Hit { public string Kind, Title, Sub; public Action Run; public override string ToString() { return Title; } }
        static GlobalSearch current;
        readonly TextBox box;
        readonly CardList list;
        readonly Timer debounce = new Timer { Interval = 150 };

        public static void Open(IWin32Window owner)
        {
            if (current != null && !current.IsDisposed) { current.Activate(); current.box.Focus(); return; }
            current = new GlobalSearch();
            current.FormClosed += delegate { current = null; };
            current.Show(owner);
            current.Activate();
        }

        GlobalSearch()
        {
            AutoScaleMode = AutoScaleMode.None; Text = "Search everything"; Icon = App.AppIcon; Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = Ui.S(620, 520); MinimumSize = Ui.S(360, 300); KeyPreview = true; ShowInTaskbar = false; TopMost = true;
            Ui.DarkTitle(this);
            box = Ui.Search("Type to search notes, prompts, clips and tagged files...");
            box.Dock = DockStyle.Top;
            list = new CardList { Dock = DockStyle.Fill };
            list.Info = o => { var h = (Hit)o; return new CardInfo { Title = string.IsNullOrEmpty(h.Title) ? "(empty)" : h.Title, Sub = h.Sub, Meta = h.Kind }; };
            var hint = new Label { Dock = DockStyle.Bottom, Height = Ui.S(24), ForeColor = Ui.Muted, Font = Ui.Small, Text = "Enter = open / copy   ·   Esc = close", TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.S(8), 0, 0, 0) };
            var top = new Panel { Dock = DockStyle.Top, Height = box.PreferredHeight + Ui.S(16), Padding = new Padding(Ui.S(8)) };
            top.Controls.Add(box);
            Controls.Add(list); Controls.Add(hint); Controls.Add(top);
            box.TextChanged += delegate { debounce.Stop(); debounce.Start(); };
            debounce.Tick += delegate { debounce.Stop(); Fill(); };
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { RunSelected(); e.SuppressKeyPress = true; }
            };
            list.DoubleClick += delegate { RunSelected(); };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { RunSelected(); e.SuppressKeyPress = true; } };
            Ui.Theme(this);
            Shown += delegate { box.Focus(); Fill(); };
        }

        void RunSelected()
        {
            var hit = (list.SelectedItem ?? (list.Items.Count > 0 ? list.Items[0] : null)) as Hit;
            if (hit == null) return;
            try { hit.Run(); } catch (Exception ex) { App.Log("Search open: " + ex.Message); }
            Close();
        }

        static bool Has(string text, string q) { return text != null && text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0; }

        void Fill()
        {
            string q = box.Text.Trim();
            var hits = new List<Hit>();
            if (q.Length > 0)
            {
                foreach (var n in Store.Notes.Where(n => Has(n.Text, q) || Has(n.Tags, q)).Take(30))
                { var note = n; hits.Add(new Hit { Kind = "Note", Title = Ui.OneLine(n.Text, 90), Sub = "Sticky note" + (string.IsNullOrEmpty(n.Tags) ? "" : "  ·  " + n.Tags), Run = () => App.I.OpenNote(note) }); }
                foreach (var p in Store.Prompts.Where(p => Has(p.Title, q) || Has(p.Body, q) || Has(p.Tags, q) || Has(p.Category, q)).Take(30))
                { var prompt = p; hits.Add(new Hit { Kind = "Prompt", Title = p.Title, Sub = "Prompt  ·  " + Ui.OneLine(p.Body, 80) + "   (Enter copies)", Run = () => { if (App.I.CopyText(prompt.Body)) { prompt.Uses++; prompt.LastUsed = Store.Now(); Store.MarkPrompts(); App.I.Balloon("Prompt copied", prompt.Title); } } }); }
                foreach (var c in Store.Clips.Where(c => Has(c.Text, q) || Has(c.Label, q)).Take(40))
                { var clip = c; hits.Add(new Hit { Kind = "Clip", Title = Ui.OneLine(c.Text, 90), Sub = "Clipboard  ·  " + Ui.Ago(c.Time) + "   (Enter copies)", Run = () => { if (App.I.CopyClip(clip)) App.I.Balloon("Copied", Ui.OneLine(clip.Text, 60)); } }); }
                foreach (var t in Store.Tagged.Where(t => Has(t.Path, q) || Has(t.Note, q) || Has(t.Category, q)).Take(30))
                { var file = t; hits.Add(new Hit { Kind = "File", Title = System.IO.Path.GetFileName(t.Path.TrimEnd('\\')), Sub = t.Category + "  ·  " + t.Path, Run = () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file.Path) { UseShellExecute = true }) }); }
            }
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(hits.Cast<object>().ToArray());
            list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            Text = q.Length == 0 ? "Search everything" : hits.Count + " result(s) - Search everything";
        }

        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void Dispose(bool disposing) { if (disposing) debounce.Dispose(); base.Dispose(disposing); }
    }


    class SystemKpis : LivePanel
    {
        readonly MetricTile cpu, ram, c, d;
        readonly Action update;
        public SystemKpis()
        {
            Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var grid = new TileGrid(Ui.S(155), Ui.S(84), 4);
            cpu = new MetricTile("CPU", () => App.I.ShowMain("system:procs")); ram = new MetricTile("RAM", () => App.I.ShowMain("system:procs"));
            c = new MetricTile("Disk C", () => App.I.ShowMain("system:drives")); d = new MetricTile("Disk D", () => App.I.ShowMain("system:drives"));
            grid.Controls.AddRange(new Control[] { cpu, ram, c, d }); Controls.Add(grid);
            update = () => { if (Live) RefreshMetrics(); }; ProcessSampler.Updated += update;
        }
        protected override void OnLive(bool on) { ProcessSampler.Watch(this, on); if (on) RefreshMetrics(); }
        void RefreshMetrics()
        {
            cpu.Value = ProcessSampler.CpuTotal.ToString("0") + "%"; ram.Value = ProcessSampler.MemLoad + "%";
            var cd = DriveSnapshot.Read("C"); var dd = DriveSnapshot.Read("D"); c.Value = cd.Value; c.Fraction = cd.Used; d.Value = dd.Value; d.Fraction = dd.Used;
            cpu.Invalidate(); ram.Invalidate(); c.Invalidate(); d.Invalidate();
        }
        protected override void Dispose(bool disposing) { if (disposing) ProcessSampler.Updated -= update; base.Dispose(disposing); }
    }
}
