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
                RemindersToday = Store.Notes.Count(n => n.RemindAt >= today && n.RemindAt < tomorrow) + Store.Tasks.Count(t => !t.Done && t.Due > 0 && t.Due < tomorrow),
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
            if (Ui.Navy) Ui.FillCard(g, new Rectangle(Ui.S(3), Ui.S(1), Width - Ui.S(7), Height - Ui.S(7)), Ui.S(14), hover || Focused);
            else using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
            { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(hover || Focused ? Ui.Accent : Ui.Line)) g.DrawPath(p, path); }
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Text, Ui.Small, new Rectangle(Ui.S(14), Ui.S(10), Width - Ui.S(28), Ui.S(20)), Ui.Muted, flags);
            TextRenderer.DrawText(g, Value, Height > Ui.S(100) ? Ui.Metric : Ui.Big, new Rectangle(Ui.S(14), Ui.S(30), Width - Ui.S(28), Ui.S(42)), Ui.Text, flags);
            int detailTop = Height > Ui.S(100) ? Ui.S(74) : Ui.S(58);
            TextRenderer.DrawText(g, Detail, Ui.Small, new Rectangle(Ui.S(14), detailTop, Width - Ui.S(28), Math.Max(Ui.S(16), Height - detailTop - Ui.S(12))), Ui.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
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
        int lastTileWidth = -1, lastTileCount;
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
                if (width != lastTileWidth || Controls.Count != lastTileCount)
                {
                    lastTileWidth = width; lastTileCount = Controls.Count;
                    foreach (Control child in Controls) child.Size = new Size(width, tileHeight);
                }
                sizing = false;
            }
            base.OnLayout(e);
        }
    }

    class DriveSnapshot
    {
        public string Value = "Unavailable", Detail = "", Letter = "", Label = "";
        public double Used = -1;
        public long Free, Total;
        static DateTime stamp = DateTime.MinValue;
        // Every fixed drive that answered, in letter order.
        public static List<DriveSnapshot> All()
        {
            Read("C");
            return cache.Values.Where(s => s.Used >= 0).OrderBy(s => s.Letter).ToList();
        }
        static volatile Dictionary<string, DriveSnapshot> cache;
        static int refreshing;
        // Never blocks the UI: drive queries (IsReady can hang on removable or network drives) run on a worker;
        // callers get the last known values and fresh ones on their next refresh.
        public static DriveSnapshot Read(string letter)
        {
            if (cache == null) cache = Query();     // first call: one quick synchronous read so the UI has values
            else if ((DateTime.Now - stamp).TotalSeconds >= 5 && System.Threading.Interlocked.Exchange(ref refreshing, 1) == 0)
            {
                stamp = DateTime.Now;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { try { cache = Query(); } finally { refreshing = 0; } });
            }
            DriveSnapshot s;
            return cache.TryGetValue(letter, out s) ? s : new DriveSnapshot();
        }
        static Dictionary<string, DriveSnapshot> Query()
        {
            stamp = DateTime.Now;
            var map = new Dictionary<string, DriveSnapshot>();
            var letters = new List<string> { "C", "D" };
            try { foreach (var di in DriveInfo.GetDrives()) { string l = di.Name.Substring(0, 1).ToUpperInvariant(); if (di.DriveType == DriveType.Fixed && !letters.Contains(l)) letters.Add(l); } } catch { }
            foreach (string drive in letters)
            {
                var snapshot = new DriveSnapshot { Letter = drive };
                try
                {
                    var info = new DriveInfo(drive + @":\");
                    if (info.DriveType == DriveType.Fixed && info.IsReady)
                    {
                        long free = info.AvailableFreeSpace, total = info.TotalSize;
                        snapshot.Free = free; snapshot.Total = total;
                        snapshot.Value = Fmt.Bytes(free) + " free"; snapshot.Detail = "of " + Fmt.Bytes(total); snapshot.Used = total > 0 ? 1 - free / (double)total : 0;
                        try { snapshot.Label = info.VolumeLabel ?? ""; } catch { }
                    }
                }
                catch { }
                map[drive] = snapshot;
            }
            return map;
        }
    }

    // PicPick-style "Pick a task..." home: blue section headers, task rows of icon + bold title + grey description.
    class TaskHome : Control
    {
        class Task { public string Glyph, Title, Desc; public Action Run; public Rectangle Bounds; }
        class Section { public string Title; public int Column; public List<Task> Tasks = new List<Task>(); public Rectangle Header, Card; }
        static readonly Font TitleFont = MakeTitleFont();
        static readonly Font HeaderFont = new Font(Ui.FontName, 12f, FontStyle.Bold);
        readonly List<Section> sections = new List<Section>();
        readonly List<Task> all = new List<Task>();
        Task hover; int focus = -1;
        Rectangle welcome, welcomeClose;
        readonly List<Rectangle> dividers = new List<Rectangle>();
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
            s.Tasks.Add(T("\uE73A", "New task / reminder", "\"call Ali tomorrow 3pm !high\"  (" + Key("newtask") + ")", () => QuickTaskForm.Open()));
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

            s = Add("Notes & prompts", 2);
            s.Tasks.Add(T("", "Clipboard history", "Everything you copied  (" + Key("clips") + ")", () => App.I.ShowMain("clips")));
            s.Tasks.Add(T("", "Prompts & snippets", "Your prompt library  (" + Key("prompts") + ")", () => App.I.ShowMain("prompts")));
            s.Tasks.Add(T("", "Sticky notes", "All notes, reminders and archive", () => App.I.ShowMain("notes")));
            s.Tasks.Add(T("", "Floating cards", "Tools that stay on top of other apps", () => App.I.ShowMain("cards")));

            s = Add("System", 2);
            s.Tasks.Add(T("", "System monitor", "Live processes, CPU and memory", () => App.I.ShowMain("system:procs")));
            s.Tasks.Add(T("", "Cleanup", "Free space on C and D", () => App.I.ShowMain("system:drives")));
            s.Tasks.Add(T("", "Startup apps", "Choose what starts with Windows", () => App.I.ShowMain("system:startup")));
            s.Tasks.Add(T("", "File activity", "Office files you opened and closed", () => App.I.ShowMain("activity")));
            s.Tasks.Add(T("", "Tagged files", "Files you marked as Urgent, Review ...", () => App.I.ShowMain("tagged")));
            s = Add("Tools", 1);
            s.Tasks.Add(T("\uE756", "Terminals", "4+ PWSH / Claude panes side by side  (" + Key("terminals") + ")", () => App.I.ShowMain("terminals")));
            s.Tasks.Add(T("\uE99A", "PWSH here", "PowerShell in the File Explorer folder  (" + Key("pwshhere") + ")", () => Term.OpenFromExplorer("")));
            s.Tasks.Add(T("\uE73A", "Tasks & reminders", "Lists, due dates, repeats  (Ctrl+9)", () => App.I.ShowMain("tasks")));
            s.Tasks.Add(T("\uEC50", "Explorer", "Up to 4 folder panes, fast copy", () => App.I.ShowMain("explorer")));
            s.Tasks.Add(T("\uE774", "Chrome bookmarks", "Manage, search, open in Chrome", () => App.I.ShowMain("chrome")));
            s.Tasks.Add(T("\uEA86", "Chrome extensions", "All profiles, on/off, manage", () => App.I.ShowMain("chrome:extensions")));
            s.Tasks.Add(T("\uE74D", "Uninstall programs", "Deep clean of leftovers", () => App.I.ShowMain("system:uninstall")));
            s.Tasks.Add(T("\uE945", "Power tools", "Always on top, hosts, who uses a file", () => App.I.ShowMain("power")));
            s.Tasks.Add(T("\uE756", "Quick run", "Run anything  (" + Key("run") + ")", () => QuickRunForm.Open()));
            s.Tasks.Add(T("\uE765", "Shortcuts", "All keys, your own launch keys", () => App.I.ShowMain("shortcuts")));
            foreach (var sec in sections) all.AddRange(sec.Tasks);
        }
        static string Key(string action) { var k = Hotkeys.Get(action); return k.Length > 0 ? k : "no shortcut"; }
        Section Add(string title, int column) { var s = new Section { Title = title, Column = column }; sections.Add(s); return s; }
        static Task T(string glyph, string title, string desc, Action run) { return new Task { Glyph = glyph, Title = title, Desc = desc, Run = run }; }

        bool ShowWelcome { get { return !Store.Settings.WelcomeHidden; } }

        // Lays out for a width and returns the height needed.
        public int Measure(int width)
        {
            columns = width >= Ui.S(1380) ? 3 : width >= Ui.S(720) ? 2 : 1;
            int gap = Ui.S(40), colW = (width - gap * (columns - 1)) / columns;
            int top = Ui.S(58);
            if (ShowWelcome) { welcome = new Rectangle(0, top, width, Ui.S(64)); welcomeClose = new Rectangle(width - Ui.S(84), top + Ui.S(16), Ui.S(72), Ui.S(32)); top += Ui.S(80); }
            else welcome = welcomeClose = Rectangle.Empty;
            var y = new[] { top, top, top, top };
            if (Ui.Nexus)
            {
                // White rounded cards, one per section, placed masonry-style (each into the shortest column)
                // so no column ends far below the others; 4 columns on very wide windows.
                if (width >= Ui.S(1700)) columns = 4;
                gap = Ui.S(18); colW = (width - gap * (columns - 1)) / columns;
                foreach (var s in sections)
                {
                    int c = 0;
                    for (int k = 1; k < columns; k++) if (y[k] < y[c]) c = k;
                    int x = c * (colW + gap);
                    int cy = y[c], ix = x + Ui.S(10), iw = colW - Ui.S(20);
                    s.Header = new Rectangle(x + Ui.S(20), cy + Ui.S(10), colW - Ui.S(40), Ui.S(34));
                    int ty = cy + Ui.S(50);
                    foreach (var t in s.Tasks) { t.Bounds = new Rectangle(ix, ty, iw, Ui.S(54)); ty += Ui.S(56); }
                    s.Card = new Rectangle(x, cy, colW, ty - cy + Ui.S(8));
                    y[c] = s.Card.Bottom + gap;
                }
                dividers.Clear();
                return y.Max();
            }
            foreach (var s in sections)
            {
                int c = columns == 3 ? s.Column : columns == 2 ? (s.Column == 0 ? 0 : 1) : 0, x = c * (colW + gap);
                s.Header = new Rectangle(x, y[c], colW, Ui.S(34)); y[c] += Ui.S(42);
                foreach (var t in s.Tasks) { t.Bounds = new Rectangle(x, y[c], colW, Ui.S(56)); y[c] += Ui.S(58); }
                y[c] += Ui.S(24);
            }
            int bottom = Math.Max(y[0], Math.Max(y[1], y[2]));
            dividers.Clear();
            for (int d = 1; d < columns; d++) dividers.Add(new Rectangle(d * (colW + gap) - gap / 2, top, 1, bottom - top - Ui.S(24)));
            return bottom;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            var clip = e.ClipRectangle;
            var titleRect = new Rectangle(0, 0, Width, Ui.S(50));
            if (titleRect.IntersectsWith(clip))
            {
                if (Ui.Nexus) TextRenderer.DrawText(g, "Quick actions", Ui.Title, titleRect, Ui.Text, flags);
                else TextRenderer.DrawText(g, "Pick a task...", TitleFont, titleRect, Ui.Text, flags);
            }
            if (Ui.Nexus)
                foreach (var s in sections)
                    if (Rectangle.Inflate(s.Card, Ui.S(8), Ui.S(8)).IntersectsWith(clip))
                        Ui.FillCard(g, new Rectangle(s.Card.X, s.Card.Y, s.Card.Width - 1, s.Card.Height - 1), Ui.S(Ui.Navy ? 18 : 14), false);
            if (welcome.Width > 0 && welcome.IntersectsWith(clip))
            {
                using (var path = Ui.Round(welcome, Ui.S(8))) { using (var b = new SolidBrush(Ui.SelBg)) g.FillPath(b, path); }
                string text = "Welcome! Main shortcuts work in any app:  " + Key("clips") + " clipboard  ·  " + Key("prompts") + " prompts  ·  " + Key("note") + " new note  ·  " + Key("snip") + " snip  ·  " + Key("main") + " this window";
                TextRenderer.DrawText(g, text, Ui.Base, new Rectangle(welcome.X + Ui.S(14), welcome.Y, welcomeClose.X - welcome.X - Ui.S(20), welcome.Height), Ui.Text, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                using (var path = Ui.Round(welcomeClose, Ui.S(6))) { using (var b = new SolidBrush(Ui.AccentFill)) g.FillPath(b, path); }
                TextRenderer.DrawText(g, "Got it", Ui.Bold, welcomeClose, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            foreach (var dv in dividers) if (dv.IntersectsWith(clip)) using (var b = new SolidBrush(Ui.Line)) g.FillRectangle(b, dv);
            foreach (var s in sections)
            {
                if (s.Header.IntersectsWith(clip))
                {
                    TextRenderer.DrawText(g, s.Title, Ui.Nexus ? Ui.Bold : HeaderFont, s.Header, Ui.Header, flags);
                    if (!Ui.Nexus) using (var b = new SolidBrush(Ui.Line)) g.FillRectangle(b, s.Header.X, s.Header.Bottom - 1, s.Header.Width, 1);
                }
                foreach (var t in s.Tasks)
                {
                    var r = t.Bounds;
                    if (!r.IntersectsWith(clip)) continue;
                    bool focused = Focused && focus >= 0 && all[focus] == t;
                    if (t == hover || focused)
                        using (var path = Ui.Round(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), Ui.S(6)))
                        {
                            using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                            if (focused) using (var p = new Pen(Ui.Accent)) g.DrawPath(p, path);
                        }
                    if (Ui.Nexus)
                    {
                        var ir = new Rectangle(r.X + Ui.S(8), r.Y + (r.Height - Ui.S(36)) / 2, Ui.S(36), Ui.S(36));
                        using (var path = Ui.Round(ir, Ui.S(10))) using (var b = new SolidBrush(Ui.SelBg)) g.FillPath(b, path);
                        TextRenderer.DrawText(g, t.Glyph, Ui.Icon(13f), ir, Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    }
                    else TextRenderer.DrawText(g, t.Glyph, Ui.Icon(20f), new Rectangle(r.X + Ui.S(8), r.Y, Ui.S(36), r.Height), Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
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
        readonly Action update, storeUpdate;
        readonly Timer storeTimer = new Timer { Interval = 400 };
        DashboardFacts facts;
        readonly TaskHome tasks;
        readonly Label liveHead;
        readonly TileGrid grid;
        readonly NexusBoard board;
        bool laying, lastWelcome;
        int lastTaskWidth = -1, lastTaskHeight, lastBoardWidth = -1, boardHeight;
        public DashboardPanel()
        {
            Dock = DockStyle.Fill; AutoScroll = true; BackColor = Ui.Bg;
            tasks = new TaskHome();
            liveHead = new Label { Text = "Live dashboard", Font = new Font(Ui.FontName, 12f, FontStyle.Bold), ForeColor = Ui.Header, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            liveHead.Disposed += delegate { liveHead.Font.Dispose(); };
            grid = new TileGrid(Ui.S(220), Ui.S(136), 6) { Dock = DockStyle.None, AutoSize = false };
            Add("cpu", "CPU", "system:procs"); Add("ram", "RAM", "system:procs"); Add("C", "Disk C", "system:drives"); Add("D", "Disk D", "system:drives");
            Add("apps", "Top 3 memory apps", "system:procs"); Add("clips", "Clipboard items today", "clips"); Add("prompts", "Prompts", "prompts");
            Add("notes", "Reminders due today", "notes"); Add("activity", "Files opened today", "activity"); Add("tagged", "Tagged Urgent", "tagged"); Add("record", "Screen recorder", "snip");
            if (Ui.Nexus) { board = new NexusBoard(); Controls.Add(tasks); Controls.Add(board); }
            else { Controls.Add(tasks); Controls.Add(liveHead); Controls.Add(grid); }
            update = () => { if (Live) RefreshMetrics(false); };
            // Store events come in bursts (typing, copying): recompute the data once, 400 ms after the last event.
            storeUpdate = () => { if (Live) { storeTimer.Stop(); storeTimer.Start(); } };
            storeTimer.Tick += delegate { storeTimer.Stop(); if (Live) RefreshMetrics(true); };
            ProcessSampler.Updated += update; Store.ClipsChanged += storeUpdate; Store.PromptsChanged += storeUpdate; Store.NotesChanged += storeUpdate; Store.ActivityChanged += storeUpdate; Store.TaggedChanged += storeUpdate; Recorder.Finished += storeUpdate; Store.TasksChanged += storeUpdate;
        }
        void Add(string key, string title, string page) { var tile = new MetricTile(title, () => App.I.ShowMain(page)); tiles[key] = tile; grid.Controls.Add(tile); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (laying || tasks == null) return;
            laying = true;
            try
            {
                int pad = Ui.S(Ui.Nexus ? 6 : 20), w = Math.Max(Ui.S(240), ClientSize.Width - pad * 2);
                int y = AutoScrollPosition.Y + Ui.S(12);
                if (board != null)
                {
                    if (w != lastBoardWidth) { lastBoardWidth = w; boardHeight = board.Measure(w); board.Invalidate(); }
                    var bb = new Rectangle(pad, y, w, boardHeight);
                    if (board.Bounds != bb) board.Bounds = bb;
                    y += boardHeight + Ui.S(4);
                }
                else
                {
                // Live data first so it is always in view, then the PicPick-style task list.
                var headBounds = new Rectangle(pad, y, w, Ui.S(36));
                if (liveHead.Bounds != headBounds) liveHead.Bounds = headBounds;
                y += Ui.S(42);
                int gh = grid.GetPreferredSize(new Size(w, 0)).Height;
                var gridBounds = new Rectangle(pad, y, w, gh);
                if (grid.Bounds != gridBounds) grid.Bounds = gridBounds;
                y += gh + Ui.S(8);
                }
                bool welcomeShown = !Store.Settings.WelcomeHidden;
                if (w != lastTaskWidth || welcomeShown != lastWelcome)
                {
                    lastTaskWidth = w; lastWelcome = welcomeShown;
                    lastTaskHeight = tasks.Measure(w);
                    tasks.SetBounds(pad, y, w, lastTaskHeight); tasks.Invalidate();
                }
                else if (tasks.Top != y || tasks.Left != pad) tasks.Location = new Point(pad, y);
                y += lastTaskHeight + pad;
                var min = new Size(0, y - AutoScrollPosition.Y);
                if (AutoScrollMinSize != min) AutoScrollMinSize = min;
            }
            finally { laying = false; }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PerformLayout(); }
        // Keep the live tiles in view: do not jump to the task list when it takes keyboard focus.
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
        protected override void OnLive(bool on) { ProcessSampler.Watch(this, on); if (on) RefreshMetrics(true); }
        void RefreshMetrics(bool data)
        {
            if (board != null) { board.RefreshData(data); return; }
            if (data || facts == null) facts = DashboardFacts.Read(Store.Now());
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
            if (disposing) { ProcessSampler.Updated -= update; Store.ClipsChanged -= storeUpdate; Store.PromptsChanged -= storeUpdate; Store.NotesChanged -= storeUpdate; Store.ActivityChanged -= storeUpdate; Store.TaggedChanged -= storeUpdate; Recorder.Finished -= storeUpdate; Store.TasksChanged -= storeUpdate; storeTimer.Dispose(); }
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
                foreach (var tk in Store.Tasks.Where(t => Has(t.Title, q) || Has(t.Notes, q) || Has(t.List, q)).Take(30))
                { var task = tk; hits.Add(new Hit { Kind = "Task", Title = (tk.Done ? "\u2714 " : "") + tk.Title, Sub = "Task  ·  " + tk.List + (tk.Due > 0 ? "  ·  " + TaskParse.DueText(tk) : ""), Run = () => { App.I.ShowMain("tasks"); var mf = Application.OpenForms.OfType<MainForm>().FirstOrDefault(); if (mf != null) mf.SelectTask(task); } }); }
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


    // Task-Manager-style performance strip: one owner-drawn control, fixed height (no AutoSize measuring),
    // 4 cards in one row on wide windows, 2 x 2 on narrow ones. CPU and memory show a 2-minute area chart.
    class SystemKpis : LivePanel
    {
        class Card { public string Title, Value, Detail, Page; public double[] Trend; public double Fraction = -1; public Color Color; public Rectangle Bounds; }
        readonly Card[] cards;
        readonly Action update;
        int hot = -1;
        public SystemKpis()
        {
            Dock = DockStyle.Top;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            cards = new[]
            {
                new Card { Title = "CPU", Page = "system:procs", Color = Color.FromArgb(51, 102, 240) },
                new Card { Title = "Memory", Page = "system:procs", Color = Color.FromArgb(139, 92, 246) },
                new Card { Title = "Disk C:", Page = "system:drives", Color = Color.FromArgb(16, 185, 129) },
                new Card { Title = "Disk D:", Page = "system:drives", Color = Color.FromArgb(16, 185, 129) }
            };
            foreach (var c in cards) { c.Value = "..."; c.Detail = ""; }
            Height = Ui.S(128);
            update = () => { if (Live) RefreshMetrics(); }; ProcessSampler.Updated += update;
        }
        // Always one row when there is any room; small windows get short cards (no detail line) so the list keeps the height.
        int Columns { get { return Width >= Ui.S(520) ? 4 : 2; } }
        bool Compact { get { return Width < Ui.S(1100) || (Parent != null && Parent.Height < Ui.S(820)); } }
        int CardH { get { return Compact ? Ui.S(72) : Ui.S(108); } }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int rows = (cards.Length + Columns - 1) / Columns;
            int want = rows * (CardH + Ui.S(8)) + Ui.S(4);
            if (Height != want) Height = want;
            Arrange();
        }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); if (Parent != null) Parent.Resize += delegate { OnResize(EventArgs.Empty); Invalidate(); }; }
        void Arrange()
        {
            int cols = Columns, gap = Ui.S(Compact ? 10 : 14), top = Ui.S(6), h = CardH;
            int w = Math.Max(Ui.S(120), (Width - gap * (cols - 1)) / cols);
            for (int i = 0; i < cards.Length; i++)
                cards[i].Bounds = new Rectangle((i % cols) * (w + gap), top + (i / cols) * (h + Ui.S(8)), w, h);
        }
        protected override void OnLive(bool on) { ProcessSampler.Watch(this, on); if (on) RefreshMetrics(); }
        void RefreshMetrics()
        {
            var list = ProcessSampler.Last;
            var top = list.Count == 0 ? null : list.OrderByDescending(p => p.Cpu).First();
            cards[0].Value = ProcessSampler.CpuTotal.ToString("0") + "%";
            cards[0].Detail = Environment.ProcessorCount + " logical cores  ·  " + list.Count + " processes" + (top != null && top.Cpu >= 1 ? "  ·  top: " + top.Display : "");
            cards[0].Trend = ProcessSampler.CpuHistory.ToArray();
            double used = ProcessSampler.MemTotal - (double)ProcessSampler.MemAvail;
            cards[1].Value = ProcessSampler.MemLoad + "%";
            cards[1].Detail = ProcessSampler.MemTotal == 0 ? "" : Fmt.Bytes((long)used) + " of " + Fmt.Bytes((long)ProcessSampler.MemTotal) + "  ·  " + Fmt.Bytes((long)ProcessSampler.MemAvail) + " free";
            cards[1].Trend = ProcessSampler.RamHistory.ToArray();
            for (int k = 0; k < 2; k++)
            {
                var snap = DriveSnapshot.Read(k == 0 ? "C" : "D");
                var c = cards[2 + k];
                c.Value = snap.Value; c.Fraction = snap.Used;
                c.Detail = snap.Used >= 0 ? (snap.Used * 100).ToString("0") + "% used  ·  " + snap.Detail : "Not available";
            }
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1; for (int i = 0; i < cards.Length; i++) if (cards[i].Bounds.Contains(e.Location)) h = i;
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot >= 0) { hot = -1; Invalidate(); } }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (var c in cards) if (c.Bounds.Contains(e.Location)) { App.I.ShowMain(c.Page); break; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent == null ? Ui.Bg : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            if (cards[0].Bounds.Width == 0) Arrange();
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            for (int i = 0; i < cards.Length; i++)
            {
                var c = cards[i]; var r = c.Bounds;
                var body = new Rectangle(r.X + Ui.S(3), r.Y + Ui.S(1), r.Width - Ui.S(7), r.Height - Ui.S(7));
                Ui.FillCard(g, body, Ui.S(14), i == hot);
                if (!Ui.Navy) using (var p = new Pen(i == hot ? Ui.Accent : Ui.Line)) using (var path = Ui.Round(body, Ui.S(14))) g.DrawPath(p, path);
                // chart area: right part of the card
                bool compact = Compact;
                int chartW = c.Trend != null ? body.Width * (compact ? 34 : 38) / 100 : 0;
                int textW = body.Width - chartW - Ui.S(28);
                using (var b = new SolidBrush(c.Color)) g.FillEllipse(b, body.X + Ui.S(16), body.Y + Ui.S(compact ? 13 : 17), Ui.S(8), Ui.S(8));
                TextRenderer.DrawText(g, c.Title, Ui.Bold, new Rectangle(body.X + Ui.S(30), body.Y + Ui.S(compact ? 6 : 10), textW, Ui.S(22)), Ui.Muted, flags | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, c.Value, compact ? Ui.Big : Ui.Kpi, new Rectangle(body.X + Ui.S(14), body.Y + Ui.S(compact ? 26 : 32), textW + Ui.S(14), Ui.S(compact ? 26 : 38)), Ui.Text, flags | TextFormatFlags.VerticalCenter);
                if (!compact) TextRenderer.DrawText(g, c.Detail, Ui.Small, new Rectangle(body.X + Ui.S(16), body.Y + (c.Fraction >= 0 ? Ui.S(66) : Ui.S(72)), body.Width - chartW - Ui.S(24), Ui.S(20)), Ui.Muted, flags);
                if (c.Fraction >= 0)
                {
                    var bar = new Rectangle(body.X + Ui.S(16), body.Bottom - Ui.S(14), body.Width - Ui.S(32), Ui.S(6));
                    using (var path = Ui.Round(bar, Ui.S(3))) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
                    int fw = (int)(bar.Width * Math.Max(0, Math.Min(1, c.Fraction)));
                    if (fw > Ui.S(6)) using (var path = Ui.Round(new Rectangle(bar.X, bar.Y, fw, bar.Height), Ui.S(3))) using (var b = new SolidBrush(c.Fraction > 0.9 ? Ui.Danger : c.Color)) g.FillPath(b, path);
                }
                if (c.Trend != null && c.Trend.Length > 1 && chartW > Ui.S(40))
                {
                    var area = new Rectangle(body.Right - chartW - Ui.S(10), body.Y + Ui.S(14), chartW, body.Height - Ui.S(28));
                    using (var p = new Pen(Ui.Line) { DashStyle = DashStyle.Dot })
                        for (int k = 1; k < 4; k++) g.DrawLine(p, area.X, area.Y + area.Height * k / 4, area.Right, area.Y + area.Height * k / 4);
                    int n = 60; var pts = new PointF[c.Trend.Length];
                    for (int k = 0; k < pts.Length; k++)
                    {
                        int slot = n - pts.Length + k;
                        pts[k] = new PointF(area.X + slot * area.Width / (float)(n - 1), area.Bottom - (float)Math.Max(0, Math.Min(100, c.Trend[k])) * area.Height / 100f);
                    }
                    using (var fill = new GraphicsPath())
                    {
                        fill.AddLines(pts); fill.AddLine(pts[pts.Length - 1], new PointF(pts[pts.Length - 1].X, area.Bottom)); fill.AddLine(new PointF(pts[pts.Length - 1].X, area.Bottom), new PointF(pts[0].X, area.Bottom)); fill.CloseFigure();
                        using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 50 : 34, c.Color))) g.FillPath(b, fill);   // flat fill, as in Task Manager
                    }
                    using (var pen = new Pen(c.Color, Ui.S(2)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, pts);
                    var last = pts[pts.Length - 1];
                    using (var b = new SolidBrush(c.Color)) g.FillEllipse(b, last.X - Ui.S(3), last.Y - Ui.S(3), Ui.S(6), Ui.S(6));
                }
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) ProcessSampler.Updated -= update; base.Dispose(disposing); }
    }
}
