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

    class DashboardPanel : LivePanel
    {
        readonly Dictionary<string, MetricTile> tiles = new Dictionary<string, MetricTile>();
        readonly Action update;
        public DashboardPanel()
        {
            Dock = DockStyle.Fill; AutoScroll = true; BackColor = Ui.Bg;
            var table = LayoutKit.Fields(); table.Dock = DockStyle.Top; table.AutoScroll = false; table.AutoSize = true;
            LayoutKit.Add(table, new WrapLabel { Text = "Home / Dashboard", Font = Ui.Big }, false);
            var actions = LayoutKit.Actions(DockStyle.Fill,
                Ui.Btn("New note", delegate { App.I.NewNote(null); }, true), Ui.Btn("Snip", delegate { App.I.StartSnip("rect", 0, false); }),
                Ui.Btn("Record", delegate { App.I.StartRecord("rect"); }), Ui.Btn("Text from screen", delegate { App.I.StartSnip("rect", 0, true); }),
                Ui.Btn("Cleanup", delegate { App.I.ShowMain("system:drives"); }));
            LayoutKit.Add(table, actions, false);
            var captures = LayoutKit.Actions(DockStyle.Fill);
            foreach (var capture in CaptureCommands.Modes) { string mode = capture[0]; captures.Controls.Add(Ui.Btn(capture[1], delegate { App.I.StartSnip(mode, 0, false); })); }
            captures.Controls.Add(Ui.Btn("Scrolling Window", delegate { App.I.StartSnip("scroll", 0, false); }));
            captures.Controls.Add(Ui.Btn("Repeat Last Capture", delegate { App.I.StartSnip("repeat", 0, false); }));
            LayoutKit.Add(table, captures, false);
            var grid = new TileGrid(Ui.S(220), Ui.S(136), 4); grid.Dock = DockStyle.Fill;
            Add(grid, "cpu", "CPU", "system:procs"); Add(grid, "ram", "RAM", "system:procs"); Add(grid, "C", "Disk C", "system:drives"); Add(grid, "D", "Disk D", "system:drives");
            Add(grid, "apps", "Top 3 memory apps", "system:procs"); Add(grid, "clips", "Clipboard items today", "clips"); Add(grid, "prompts", "Prompts", "prompts");
            Add(grid, "notes", "Reminders due today", "notes"); Add(grid, "activity", "Files opened today", "activity"); Add(grid, "tagged", "Tagged Urgent", "tagged"); Add(grid, "record", "Screen recorder", "snip");
            LayoutKit.Add(table, grid, false); Controls.Add(table);
            update = () => { if (Live) RefreshMetrics(); };
            ProcessSampler.Updated += update; Store.ClipsChanged += update; Store.PromptsChanged += update; Store.NotesChanged += update; Store.ActivityChanged += update; Store.TaggedChanged += update; Recorder.Finished += update;
        }
        void Add(TileGrid grid, string key, string title, string page) { var tile = new MetricTile(title, () => App.I.ShowMain(page)); tiles[key] = tile; grid.Controls.Add(tile); }
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
