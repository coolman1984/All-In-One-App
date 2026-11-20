using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // Panel that knows when it is really on screen, so live views only work while seen.
    class LivePanel : Panel
    {
        bool live;
        Form liveForm;
        protected bool Live { get { return live; } }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Check(); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); Check(); }
        protected void Check()
        {
            var form = FindForm();
            if (form != liveForm) { if (liveForm != null) liveForm.Resize -= FormResize; liveForm = form; if (liveForm != null) liveForm.Resize += FormResize; }
            bool now = Visible && !IsDisposed && form != null && form.WindowState != FormWindowState.Minimized;
            if (now == live) return;
            live = now;
            OnLive(now);
        }
        void FormResize(object sender, EventArgs e) { Check(); }
        protected virtual void OnLive(bool on) { }
        protected override void Dispose(bool disposing)
        {
            if (disposing && liveForm != null) liveForm.Resize -= FormResize;
            if (disposing && live) { live = false; OnLive(false); }
            base.Dispose(disposing);
        }
        protected void UI(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) a(); })); } catch { }
        }
    }

    class FastList : ListView
    {
        readonly HeaderWindow header;
        public FastList()
        {
            header = new HeaderWindow(this);
            DoubleBuffered = true;
            View = View.Details;
            FullRowSelect = true;
            HideSelection = false;
            BorderStyle = BorderStyle.None;
            Font = Ui.Base;
            BackColor = Ui.Card;
            ForeColor = Ui.Text;
            if (Ui.Dark) Ui.DarkScroll(this);
            OwnerDraw = true;
            DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Ui.Nav)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var r = e.Bounds; r.Inflate(e.Bounds.Width < Ui.S(48) ? -Ui.S(2) : -Ui.S(Ui.Space), 0);   // narrow icon columns keep their glyph
                var align = e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : e.Header.TextAlign == HorizontalAlignment.Center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left;
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Ui.Bold, r, Ui.Text, align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            DrawItem += (s, e) => { e.DrawDefault = true; };
            DrawSubItem += (s, e) => { if (CustomCell != null && e.Item != null && !e.Item.Selected && CustomCell(e)) return; e.DrawDefault = true; };
        }

        // Optional custom painter for single cells (return true when the cell was painted). Selected rows keep the system look.
        public Func<DrawListViewSubItemEventArgs, bool> CustomCell;

        // Task-Manager-style heat cell: tinted background (0..1 intensity) and right-aligned text.
        public static void HeatCell(DrawListViewSubItemEventArgs e, double intensity, Color text)
        {
            var g = e.Graphics; var r = e.Bounds;
            using (var b = new SolidBrush(Ui.Card)) g.FillRectangle(b, r);
            intensity = Math.Max(0, Math.Min(1, intensity));
            if (intensity > 0.02)
            {
                var heat = Ui.Dark ? Color.FromArgb((int)(40 + 120 * intensity), 255, 176, 32) : Color.FromArgb((int)(28 + 150 * intensity), 255, 186, 0);
                using (var b = new SolidBrush(heat)) g.FillRectangle(b, r.X + 1, r.Y, r.Width - 2, r.Height - 1);
            }
            var tr = r; tr.Inflate(-Ui.S(8), 0);
            TextRenderer.DrawText(g, e.SubItem.Text, e.Item.Font ?? Ui.Base, tr, text, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            IntPtr hwnd = Native.SendMessage(Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
            if (hwnd != IntPtr.Zero) header.AssignHandle(hwnd);
            try { BeginInvoke(new Action(FillLastColumn)); } catch { }
        }

        // One column (the last by default, or FillIndex) stretches to fill the free width (never narrower than its designed width).
        public int FillIndex = -1;
        bool filling; ColumnHeader minFor; int minWidth;
        int FillTarget { get { return FillIndex >= 0 && FillIndex < Columns.Count ? FillIndex : Columns.Count - 1; } }
        // Optional: share the free width between several columns (weight per column, 0 = fixed). Overrides FillIndex.
        public int[] Weights;
        // Optional: columns to hide (in this order) when the list is too narrow; MinFill = smallest width of the fill column.
        public int[] DropOrder; public int MinFill;
        int[] dropDesign;
        int[] designWidths, applied;   // applied = widths this code set (their change notices arrive later, after filling)
        void FillLastColumn()
        {
            if (filling || Columns.Count == 0 || !IsHandleCreated || View != View.Details) return;
            filling = true;
            try
            {
                if (Weights != null)
                {
                    if (designWidths == null || designWidths.Length != Columns.Count)
                    { designWidths = new int[Columns.Count]; for (int i = 0; i < Columns.Count; i++) designWidths[i] = Columns[i].Width; applied = new int[Columns.Count]; }
                    int total = 0, wsum = 0;
                    for (int i = 0; i < Columns.Count; i++) { total += designWidths[i]; if (i < Weights.Length) wsum += Weights[i]; }
                    int extra = Math.Max(0, ClientSize.Width - total - 1);
                    int given = 0, lastW = -1;
                    for (int i = 0; i < Columns.Count; i++) if (i < Weights.Length && Weights[i] > 0) lastW = i;
                    for (int i = 0; i < Columns.Count; i++)
                    {
                        int add = 0;
                        if (wsum > 0 && i < Weights.Length && Weights[i] > 0) add = i == lastW ? extra - given : extra * Weights[i] / wsum;
                        given += add;
                        int cw = designWidths[i] + add;
                        applied[i] = cw;
                        if (Columns[i].Width != cw) Columns[i].Width = cw;
                    }
                    return;
                }
                int ti = FillTarget;
                var col = Columns[ti];
                if (!ReferenceEquals(col, minFor)) { minFor = col; minWidth = col.Width; }
                if (DropOrder != null)
                {
                    // Narrow list: hide the least useful columns (in DropOrder) before squeezing the fill column below MinFill.
                    if (dropDesign == null || dropDesign.Length != Columns.Count) { dropDesign = new int[Columns.Count]; for (int i = 0; i < Columns.Count; i++) dropDesign[i] = Columns[i].Width; applied = new int[Columns.Count]; }
                    int avail = ClientSize.Width - 1, minFill = MinFill > 0 ? MinFill : Ui.S(140);
                    var widths = (int[])dropDesign.Clone();
                    int fixedSum = 0;
                    for (int i = 0; i < widths.Length; i++) if (i != ti) fixedSum += widths[i];
                    foreach (int d in DropOrder) { if (fixedSum + minFill <= avail) break; if (d != ti && d < widths.Length) { fixedSum -= widths[d]; widths[d] = 0; } }
                    widths[ti] = Math.Max(minFill, avail - fixedSum);
                    for (int i = 0; i < widths.Length; i++) { applied[i] = widths[i]; if (Columns[i].Width != widths[i]) Columns[i].Width = widths[i]; }
                    return;
                }
                int used = 0;
                for (int i = 0; i < Columns.Count; i++) if (i != ti) used += Columns[i].Width;
                int w = Math.Max(minWidth, ClientSize.Width - used - 1);
                if (col.Width != w) col.Width = w;
            }
            finally { filling = false; }
        }
        int lastFillW = -1;
        // Fill whenever the width is known for real: a resize before the handle exists, or while hidden, used to be
        // remembered and then skipped, so the columns never stretched on wide windows.
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated && ClientSize.Width != lastFillW) { lastFillW = ClientSize.Width; FillLastColumn(); } }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible && IsHandleCreated) { lastFillW = ClientSize.Width; FillLastColumn(); } }
        protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
        {
            base.OnColumnWidthChanged(e);
            if (filling) return;
            if (Weights != null)
            {
                // The user dragged a column: keep that as its new base width, re-share the rest.
                if (designWidths != null && e.ColumnIndex < designWidths.Length)
                {
                    int w = Columns[e.ColumnIndex].Width;
                    if (applied != null && e.ColumnIndex < applied.Length && applied[e.ColumnIndex] == w) return;   // our own fill, not a drag
                    if (e.ColumnIndex >= Weights.Length || Weights[e.ColumnIndex] == 0) designWidths[e.ColumnIndex] = w;
                    else { Weights = (int[])Weights.Clone(); Weights[e.ColumnIndex] = 0; designWidths[e.ColumnIndex] = w; }
                }
                FillLastColumn();
            }
            else
            {
                if (DropOrder != null && dropDesign != null && e.ColumnIndex < dropDesign.Length && e.ColumnIndex != FillTarget)
                {
                    int w = Columns[e.ColumnIndex].Width;
                    if (applied != null && applied[e.ColumnIndex] == w) return;   // our own change
                    if (w > 0) dropDesign[e.ColumnIndex] = w;                     // the user dragged it
                }
                if (e.ColumnIndex != FillTarget) FillLastColumn();
            }
        }
        protected override void OnHandleDestroyed(EventArgs e) { header.ReleaseHandle(); base.OnHandleDestroyed(e); }

        // Native headers leave their unused trailing area white in dark mode.
        class HeaderWindow : NativeWindow
        {
            readonly FastList owner;
            public HeaderWindow(FastList owner) { this.owner = owner; }
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0xF && m.Msg != 0x318 && m.Msg != 0x317) return;
                using (var g = m.Msg == 0x318 || m.Msg == 0x317 ? Graphics.FromHdc(m.WParam) : Graphics.FromHwnd(Handle))
                using (var b = new SolidBrush(Ui.Nav))
                {
                    int right = owner.Columns.Cast<ColumnHeader>().Sum(c => c.Width) - Native.GetScrollPos(owner.Handle, 0);
                    var bounds = Rectangle.Ceiling(g.VisibleClipBounds);
                    if (right < bounds.Right) g.FillRectangle(b, Math.Max(0, right), 0, bounds.Right - Math.Max(0, right), bounds.Height);
                }
            }
        }
    }

    // Simple tab strip: underlined labels.
    class TabStrip : FlowLayoutPanel
    {
        readonly List<Label> tabs = new List<Label>();
        public int Selected = -1;
        public event Action<int> Changed;

        public TabStrip()
        {
            Dock = DockStyle.Top;
            Height = Ui.S(34);
            WrapContents = false;
            Padding = new Padding(0);
        }

        public void Add(string text)
        {
            var l = new Label();
            l.Text = text;
            l.UseMnemonic = false;
            l.AutoSize = true;
            l.Padding = new Padding(Ui.S(10), Ui.S(7), Ui.S(10), Ui.S(7));
            l.Margin = new Padding(0, 0, Ui.S(2), 0);
            l.Cursor = Cursors.Hand;
            int i = tabs.Count;
            l.Click += delegate { Select(i); };
            l.Paint += (s, e) =>
            {
                if (tabs.IndexOf(l) == Selected)
                    using (var b = new SolidBrush(Ui.Accent)) e.Graphics.FillRectangle(b, Ui.S(8), l.Height - Ui.S(3), l.Width - Ui.S(16), Ui.S(3));
            };
            tabs.Add(l);
            Controls.Add(l);
        }

        // Hide a tab that is always on screen anyway (wide split views).
        public void ShowTab(int i, bool show) { if (i >= 0 && i < tabs.Count && tabs[i].Visible != show) tabs[i].Visible = show; }

        public void Select(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            Selected = i;
            for (int k = 0; k < tabs.Count; k++)
            {
                tabs[k].Font = k == i ? Ui.Bold : Ui.Base;
                tabs[k].ForeColor = k == i ? Ui.Text : Ui.Muted;
                tabs[k].Invalidate();
            }
            var h = Changed; if (h != null) h(i);
        }
    }

    static class Admin
    {
        public static bool IsAdmin
        {
            get { try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
        }
    }

    // ======================= Processes =======================

    class ProcessesPanel : LivePanel
    {
        readonly FastList lv;
        readonly TextBox search;
        readonly CheckBox group;
        readonly Label summary;
        List<ProcInfo> rows = new List<ProcInfo>();
        int sortCol = 3;
        bool sortDesc = true;
        readonly Action onUpdate;
        readonly Action<string> status;

        public ProcessesPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;

            lv = new FastList();
            lv.Dock = DockStyle.Fill;
            lv.VirtualMode = true;
            lv.MultiSelect = true;
            lv.SmallImageList = ProcMeta.Icons;
            lv.Columns.Add("Name", Ui.S(280));
            lv.Columns.Add("Count / PID", Ui.S(100), HorizontalAlignment.Right);
            lv.Columns.Add("CPU", Ui.S(90), HorizontalAlignment.Right);
            lv.Columns.Add("Memory", Ui.S(110), HorizontalAlignment.Right);
            lv.Columns.Add("Disk I/O", Ui.S(100), HorizontalAlignment.Right);
            lv.Columns.Add("Threads", Ui.S(80), HorizontalAlignment.Right);
            lv.Columns.Add("Process", Ui.S(170));
            lv.Columns.Add("Location", Ui.S(240));
            lv.Weights = new[] { 2, 0, 0, 0, 0, 0, 1, 3 };
            lv.CustomCell = e =>
            {
                if (e.ColumnIndex < 2 || e.ColumnIndex > 4 || e.ItemIndex < 0 || e.ItemIndex >= rows.Count) return false;
                var p = rows[e.ItemIndex];
                double heat = e.ColumnIndex == 2 ? p.Cpu / 25.0 : e.ColumnIndex == 3 ? p.Mem / (1536.0 * 1024 * 1024) : p.Io / (20.0 * 1024 * 1024);
                FastList.HeatCell(e, heat, e.Item.ForeColor);
                return true;
            };
            lv.RetrieveVirtualItem += Retrieve;
            lv.ColumnClick += (s, e) => { if (sortCol == e.Column) sortDesc = !sortDesc; else { sortCol = e.Column; sortDesc = e.Column != 0; } Rebuild(); };
            lv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { EndSelected(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.F) { search.Focus(); e.SuppressKeyPress = true; }
            };
            var m = Ui.Menu();
            m.Items.Add("End task\tDel", null, delegate { EndSelected(); });
            m.Items.Add("Open file location", null, delegate
            {
                var p = SelectedRows().FirstOrDefault(r => !string.IsNullOrEmpty(r.Path));
                if (p != null) try { Process.Start("explorer.exe", "/select,\"" + p.Path + "\""); } catch { }
            });
            m.Items.Add("Copy name", null, delegate { var p = SelectedRows().FirstOrDefault(); if (p != null) App.I.CopyText(p.Display); });
            m.Items.Add("Copy path", null, delegate { var p = SelectedRows().FirstOrDefault(r => !string.IsNullOrEmpty(r.Path)); if (p != null) App.I.CopyText(p.Path); });
            m.Items.Add("File properties", null, delegate
            {
                var p = SelectedRows().FirstOrDefault(r => !string.IsNullOrEmpty(r.Path));
                if (p != null) ShellOps.Properties(p.Path);
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Search the web for it", null, delegate
            {
                var p = SelectedRows().FirstOrDefault();
                if (p != null) try { Process.Start("https://www.bing.com/search?q=" + Uri.EscapeDataString(p.Name + " process")); } catch { }
            });
            Ui.ThemeMenu(m);
            lv.ContextMenuStrip = m;
            Controls.Add(lv);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 36), WrapContents = true };
            search = Ui.Search("Filter processes");
            search.Width = Ui.S(260);
            search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            search.TextChanged += delegate { Rebuild(); };
            bar.Controls.Add(search);
            group = new CheckBox { Text = "Group by app", AutoSize = true, Checked = true, Margin = new Padding(0, Ui.S(6), Ui.S(8), 0) };
            group.CheckedChanged += delegate { Rebuild(); };
            bar.Controls.Add(group);
            bar.Controls.Add(Ui.Btn("End task", delegate { EndSelected(); }, true));
            summary = new Label { AutoSize = true, Font = Ui.Bold, UseMnemonic = false, Margin = new Padding(Ui.S(16), Ui.S(9), 0, 0) };
            bar.Controls.Add(summary);
            Controls.Add(bar);

            onUpdate = () => { if (Live) Rebuild(); };
            ProcessSampler.Updated += onUpdate;
            Ui.Theme(this);
        }

        protected override void OnLive(bool on) { ProcessSampler.Watch(this, on); if (on) Rebuild(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ProcessSampler.Updated -= onUpdate;
            base.Dispose(disposing);
        }

        void Rebuild()
        {
            var src = ProcessSampler.Last;
            string q = search.Text.Trim();
            IEnumerable<ProcInfo> items = src;
            if (group.Checked)
            {
                items = src.GroupBy(p => p.Display, StringComparer.OrdinalIgnoreCase).Select(g =>
                {
                    var first = g.First();
                    if (g.Count() == 1) return first;
                    return new ProcInfo
                    {
                        Display = first.Display, Name = first.Name, Path = first.Path, Count = g.Count(), Pid = first.Pid,
                        Mem = g.Sum(x => x.Mem), Cpu = g.Sum(x => x.Cpu), Io = g.Sum(x => x.Io), Threads = g.Sum(x => x.Threads), Pids = g.Select(x => x.Pid).ToList()
                    };
                });
            }
            if (q.Length > 0) items = items.Where(p => p.Display.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || p.Pid.ToString() == q);
            Func<ProcInfo, IComparable> key;
            switch (sortCol)
            {
                case 0: key = p => p.Display.ToLowerInvariant(); break;
                case 1: key = p => group.Checked ? p.Count : p.Pid; break;
                case 2: key = p => p.Cpu; break;
                case 4: key = p => p.Io; break;
                case 5: key = p => p.Threads; break;
                case 6: key = p => p.Name.ToLowerInvariant(); break;
                case 7: key = p => (p.Path ?? "").ToLowerInvariant(); break;
                default: key = p => p.Mem; break;
            }
            var list = (sortDesc ? items.OrderByDescending(key) : items.OrderBy(key)).ToList();

            var keep = new HashSet<string>(SelectedRows().Select(r => r.Key));
            string focused = lv.FocusedItem != null && lv.FocusedItem.Index < rows.Count ? rows[lv.FocusedItem.Index].Key : null;
            int top = 0;
            try { if (lv.VirtualListSize > 0 && lv.TopItem != null) top = lv.TopItem.Index; } catch { }
            rows = list;
            lv.BeginUpdate();
            lv.VirtualListSize = rows.Count;
            lv.SelectedIndices.Clear();
            for (int i = 0; i < rows.Count; i++)
                if (keep.Contains(rows[i].Key)) lv.SelectedIndices.Add(i);
            lv.EndUpdate();
            if (focused != null) { int fi = rows.FindIndex(r => r.Key == focused); if (fi >= 0) try { lv.Items[fi].Focused = true; } catch { } }
            if (top > 0 && top < rows.Count) try { lv.TopItem = lv.Items[top]; } catch { }
            lv.Invalidate();

            double used = ProcessSampler.MemTotal - ProcessSampler.MemAvail;
            summary.Text = "CPU " + ProcessSampler.CpuTotal.ToString("0") + "%    RAM " + ProcessSampler.MemLoad + "%  (" + Fmt.Bytes((long)used) + " of " + Fmt.Bytes((long)ProcessSampler.MemTotal) + ")    " + src.Count + " processes";
            summary.ForeColor = ProcessSampler.MemLoad >= 85 ? Ui.Danger : Ui.Text;
        }

        void Retrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= rows.Count) { e.Item = new ListViewItem(""); e.Item.SubItems.AddRange(new[] { "", "", "", "", "", "", "" }); return; }
            var p = rows[e.ItemIndex];
            var it = new ListViewItem(p.Display);
            it.ImageIndex = ProcMeta.IconFor(p.Path);
            it.SubItems.Add(p.Pids != null ? "(" + p.Count + ")" : p.Pid.ToString());
            it.SubItems.Add(p.Cpu < 0.05 ? "0 %" : p.Cpu.ToString("0.0") + " %");
            it.SubItems.Add(Fmt.Bytes(p.Mem));
            it.SubItems.Add(p.Io < 1024 ? "0" : Fmt.Bytes((long)p.Io) + "/s");
            it.SubItems.Add(p.Threads.ToString());
            it.SubItems.Add(p.Name);
            it.SubItems.Add(string.IsNullOrEmpty(p.Path) ? "" : System.IO.Path.GetDirectoryName(p.Path));
            if (p.Mem > 500L << 20) it.ForeColor = Ui.Danger;
            e.Item = it;
        }

        List<ProcInfo> SelectedRows()
        {
            var r = new List<ProcInfo>();
            foreach (int i in lv.SelectedIndices) if (i >= 0 && i < rows.Count) r.Add(rows[i]);
            return r;
        }

        void EndSelected()
        {
            var sel = SelectedRows();
            if (sel.Count == 0) return;
            var crit = sel.Where(p => ProcessSampler.IsCritical(p.Name)).ToList();
            if (crit.Count > 0) { status("Not ended: " + string.Join(", ", crit.Select(c => c.Display)) + " - Windows needs it to run."); sel = sel.Except(crit).ToList(); }
            if (sel.Count == 0) return;
            if (sel.Any(p => p.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) &&
                MessageBox.Show(FindForm(), "End Windows Explorer? The taskbar and desktop disappear until it restarts.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            var pids = sel.SelectMany(p => p.Pids ?? new List<int> { p.Pid }).ToList();
            var res = ProcessSampler.Kill(pids);
            status(res[1] == 0 ? "Ended " + string.Join(", ", sel.Select(s => s.Display)) + "."
                : "Ended " + res[0] + ", could not end " + res[1] + " (protected or needs admin).");
            Ui.Later(250, ProcessSampler.Sample);
        }
    }

    // ======================= Startup apps =======================

    class StartupPanel : LivePanel
    {
        readonly FastList lv;
        readonly Action<string> status;
        List<StartupItem> items = new List<StartupItem>();
        bool loaded;

        public StartupPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;
            lv = new FastList();
            lv.Dock = DockStyle.Fill;
            lv.MultiSelect = true;
            lv.SmallImageList = ProcMeta.Icons;
            lv.Columns.Add("Name", Ui.S(220));
            lv.Columns.Add("Status", Ui.S(80));
            lv.Columns.Add("Publisher", Ui.S(170));
            lv.Columns.Add("Where", Ui.S(170));
            lv.Columns.Add("Command", Ui.S(400));
            lv.Weights = new[] { 2, 0, 1, 1, 4 };
            lv.DoubleClick += delegate { Toggle(null); };
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Toggle(null); e.SuppressKeyPress = true; } };
            var m = Ui.Menu();
            m.Items.Add("Enable", null, delegate { Toggle(true); });
            m.Items.Add("Disable", null, delegate { Toggle(false); });
            m.Items.Add("Open file location", null, delegate { OpenLocation(); });
            Ui.ThemeMenu(m);
            lv.ContextMenuStrip = m;
            Controls.Add(lv);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 38), WrapContents = true };
            bar.Controls.Add(Ui.Btn("Enable", delegate { Toggle(true); }, true));
            bar.Controls.Add(Ui.Btn("Disable", delegate { Toggle(false); }));
            bar.Controls.Add(Ui.Btn("Open location", delegate { OpenLocation(); }));
            bar.Controls.Add(Ui.Btn("Refresh", delegate { Reload(); }));
            var hint = new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(Ui.S(6), Ui.S(9), 0, 0), Text = "Double-click to switch on/off. Takes effect at next sign-in." };
            bar.Controls.Add(hint);
            Controls.Add(bar);
            Ui.Theme(this);
        }

        protected override void OnLive(bool on) { if (on && !loaded) Reload(); }

        public void Reload()
        {
            loaded = true;
            status("Loading...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<StartupItem> list;
                try { list = StartupManager.Load(); } catch (Exception ex) { App.Log("Startup load: " + ex.Message); list = new List<StartupItem>(); }
                UI(() => { items = list; Fill(); });
            });
        }

        void Fill()
        {
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var it in items)
            {
                var li = new ListViewItem(it.Name);
                li.ImageIndex = ProcMeta.IconFor(it.Path);
                li.SubItems.Add(it.Enabled ? "Enabled" : "Disabled");
                li.SubItems.Add(it.Publisher ?? "");
                li.SubItems.Add(it.Source);
                li.SubItems.Add(it.Command ?? "");
                li.ForeColor = it.Enabled ? Ui.Text : Ui.Muted;
                li.Tag = it;
                lv.Items.Add(li);
            }
            lv.EndUpdate();
            status(items.Count(i => i.Enabled) + " of " + items.Count + " startup apps are enabled.");
        }

        void Toggle(bool? on)
        {
            var errors = new List<string>();
            int changed = 0;
            foreach (ListViewItem li in lv.SelectedItems)
            {
                var it = (StartupItem)li.Tag;
                bool want = on ?? !it.Enabled;
                if (want == it.Enabled) continue;
                var err = StartupManager.SetEnabled(it, want);
                if (err != null) errors.Add(err); else changed++;
                li.SubItems[1].Text = it.Enabled ? "Enabled" : "Disabled";
                li.ForeColor = it.Enabled ? Ui.Text : Ui.Muted;
            }
            if (errors.Count > 0) status(errors[0]);
            else if (changed > 0) status("Done. " + items.Count(i => i.Enabled) + " of " + items.Count + " startup apps are enabled.");
        }

        void OpenLocation()
        {
            if (lv.SelectedItems.Count == 0) return;
            var it = (StartupItem)lv.SelectedItems[0].Tag;
            if (!string.IsNullOrEmpty(it.Path) && File.Exists(it.Path)) try { Process.Start("explorer.exe", "/select,\"" + it.Path + "\""); } catch { }
            else status("The file for this item was not found.");
        }
    }

    // ======================= Drives & cleanup =======================

    class DriveTile : Control
    {
        public string Letter, Label, Info, Delta;
        public double Used;
        public DriveTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = Ui.S(320, 92);
            Margin = new Padding(0, 0, Ui.S(12), Ui.S(8));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(Ui.S(3), Ui.S(1), Width - Ui.S(7), Height - Ui.S(7));
            Ui.FillCard(g, r, Ui.S(12), false);
            if (!Ui.Navy) using (var path = Ui.Round(r, Ui.S(12))) using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Label, Ui.Bold, new Rectangle(r.X + Ui.S(14), r.Y + Ui.S(8), r.Width - Ui.S(100), Ui.S(22)), Ui.Text, flags);
            TextRenderer.DrawText(g, (Used * 100).ToString("0") + "% used", Ui.Bold, new Rectangle(r.X + Ui.S(14), r.Y + Ui.S(8), r.Width - Ui.S(28), Ui.S(22)), Used >= 0.9 ? Ui.Danger : Ui.Muted, flags | TextFormatFlags.Right);
            var bar = new Rectangle(r.X + Ui.S(14), r.Y + Ui.S(34), r.Width - Ui.S(28), Ui.S(10));
            using (var path = Ui.Round(bar, Ui.S(5))) using (var b = new SolidBrush(Ui.Track)) g.FillPath(b, path);
            Color fill = Used >= 0.9 ? Color.FromArgb(218, 38, 38) : Used >= 0.75 ? Color.FromArgb(235, 150, 30) : Color.FromArgb(38, 160, 218);
            int fw = (int)(bar.Width * Math.Max(0, Math.Min(1, Used)));
            if (fw > Ui.S(10)) using (var path = Ui.Round(new Rectangle(bar.X, bar.Y, fw, bar.Height), Ui.S(5))) using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            TextRenderer.DrawText(g, Info, Ui.Small, new Rectangle(r.X + Ui.S(14), r.Y + Ui.S(50), r.Width - Ui.S(28), Ui.S(18)), Ui.Muted, flags);
            if (!string.IsNullOrEmpty(Delta))
                TextRenderer.DrawText(g, Delta, Ui.Small, new Rectangle(r.X + Ui.S(14), r.Y + Ui.S(66), r.Width - Ui.S(28), Ui.S(18)), Delta.StartsWith("-") ? Ui.Danger : Ui.Success, flags);
        }
    }

    // Row of equal tiles that share the full width (wraps when a tile would be narrower than MinTile). Sets its own height.
    class TileRow : Panel
    {
        public int MinTile = Ui.S(260), MaxTile = Ui.S(520), TileHeight = Ui.S(92);
        public TileRow() { Dock = DockStyle.Top; Height = TileHeight; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var kids = Controls.Cast<Control>().Where(c => c.Visible).ToList();
            int n = Math.Max(1, kids.Count), gap = Ui.S(12), w = Math.Max(1, ClientSize.Width);
            int cols = Math.Max(1, Math.Min(n, (w + gap) / (MinTile + gap)));
            int tw = Math.Min(MaxTile, (w - gap * (cols - 1)) / cols);
            for (int i = 0; i < kids.Count; i++)
                kids[i].SetBounds((i % cols) * (tw + gap), (i / cols) * (TileHeight + Ui.S(6)), tw, TileHeight);
            int rows = (kids.Count + cols - 1) / cols;
            int h = Math.Max(TileHeight, rows * (TileHeight + Ui.S(6)));
            if (Height != h) Height = h;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); PerformLayout(); }
    }

    class DrivesPanel : LivePanel
    {
        readonly TileRow tiles;
        readonly Dictionary<string, DriveTile> tileMap = new Dictionary<string, DriveTile>();
        readonly FastList lvClean, lvBig, lvFolders;
        readonly TabStrip tabs;
        readonly Label info;
        readonly Button btnScan, btnDeep, btnDelete, btnCancel;
        readonly System.Windows.Forms.Timer driveTimer = new System.Windows.Forms.Timer();
        System.Windows.Forms.Timer scanProgress;
        readonly Action<string> status;
        List<CleanTarget> targets = new List<CleanTarget>();
        List<BigFile> bigFiles = new List<BigFile>();
        volatile bool cancel, busy;
        DateTime lastHistory = DateTime.MinValue;
        bool measured;

        public DrivesPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;

            lvClean = new FastList { Dock = DockStyle.Fill, CheckBoxes = true };
            lvClean.Columns.Add("What", Ui.S(300));
            lvClean.Columns.Add("Size", Ui.S(90), HorizontalAlignment.Right);
            lvClean.Columns.Add("Change", Ui.S(100), HorizontalAlignment.Right);
            lvClean.Columns.Add("Files", Ui.S(80), HorizontalAlignment.Right);
            lvClean.Columns.Add("Details", Ui.S(420));
            lvClean.Weights = new[] { 1, 0, 0, 0, 2 };
            lvClean.ItemChecked += (s, e) => { var t = e.Item.Tag as CleanTarget; if (t != null) t.Checked = e.Item.Checked; UpdateDeleteButton(); };

            lvBig = new FastList { Dock = DockStyle.Fill, CheckBoxes = true, Visible = false };
            lvBig.Columns.Add("File", Ui.S(300));
            lvBig.Columns.Add("Size", Ui.S(90), HorizontalAlignment.Right);
            lvBig.Columns.Add("Modified", Ui.S(110));
            lvBig.Columns.Add("Folder", Ui.S(520));
            lvBig.Weights = new[] { 1, 0, 0, 2 };
            lvBig.ItemChecked += delegate { UpdateDeleteButton(); };
            lvBig.DoubleClick += delegate { if (lvBig.SelectedItems.Count > 0) Reveal(((BigFile)lvBig.SelectedItems[0].Tag).Path); };

            lvFolders = new FastList { Dock = DockStyle.Fill, Visible = false };
            lvFolders.Columns.Add("Folder", Ui.S(520));
            lvFolders.Columns.Add("Size", Ui.S(90), HorizontalAlignment.Right);
            lvFolders.Columns.Add("Change since last scan", Ui.S(180), HorizontalAlignment.Right);
            lvFolders.FillIndex = 0;
            lvFolders.DoubleClick += delegate { if (lvFolders.SelectedItems.Count > 0) try { Process.Start("explorer.exe", "\"" + (string)lvFolders.SelectedItems[0].Tag + "\""); } catch { } };

            BuildMenus();
            Controls.Add(lvClean);
            Controls.Add(lvBig);
            Controls.Add(lvFolders);

            tabs = new TabStrip();
            tabs.Add("Cleanup");
            tabs.Add("Big files");
            tabs.Add("Biggest folders");
            tabs.Changed += i => { lvClean.Visible = i == 0; lvBig.Visible = i == 1; lvFolders.Visible = i == 2; };
            Controls.Add(tabs);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 38), WrapContents = true };
            btnScan = Ui.Btn("Refresh", delegate { MeasureAll(); });
            btnDeep = Ui.Btn("Deep scan (big files)", delegate { DeepScan(); });
            btnDelete = Ui.Btn("Delete selected", delegate { DeleteSelected(); }, true);
            btnCancel = Ui.Btn("Stop", delegate { cancel = true; });
            btnCancel.Visible = false;
            bar.Controls.Add(btnDelete);
            bar.Controls.Add(btnScan);
            bar.Controls.Add(btnDeep);
            bar.Controls.Add(btnCancel);
            if (!Admin.IsAdmin) bar.Controls.Add(Ui.Btn("Run as admin", delegate { App.I.RestartElevated(); }));
            Controls.Add(bar);

            info = new Label { Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(info);

            tiles = new TileRow();
            Controls.Add(tiles);

            driveTimer.Interval = 5000;
            driveTimer.Tick += delegate { UpdateDrives(); };
            tabs.Select(0);
            Ui.Theme(this);
            UpdateDeleteButton();
        }

        protected override void OnLive(bool on)
        {
            if (on) { UpdateDrives(); driveTimer.Start(); if (!measured) MeasureAll(); }
            else driveTimer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { cancel = true; driveTimer.Dispose(); if (scanProgress != null) scanProgress.Dispose(); }
            base.Dispose(disposing);
        }

        void UpdateDrives()
        {
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).ToArray(); } catch { return; }
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            bool saveHistory = (DateTime.Now - lastHistory).TotalMinutes >= 5;
            foreach (var d in drives)
            {
                string letter = d.Name.Substring(0, 2);
                DriveTile t;
                if (!tileMap.TryGetValue(letter, out t)) { t = new DriveTile { Letter = letter }; tileMap[letter] = t; tiles.Controls.Add(t); }
                long free = d.AvailableFreeSpace, total = d.TotalSize;
                t.Label = (string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel) + " (" + letter + ")";
                t.Info = Fmt.Bytes(free) + " free of " + Fmt.Bytes(total);
                t.Used = total > 0 ? 1.0 - (double)free / total : 0;
                // Compare with the most recent earlier day we know.
                var hist = Store.History.DriveFreeDay;
                string prevKey = hist.Keys.Where(k => k.StartsWith(letter + "|") && string.CompareOrdinal(k, letter + "|" + today) < 0).OrderByDescending(k => k).FirstOrDefault();
                long prevFree;
                t.Delta = prevKey != null && hist.TryGetValue(prevKey, out prevFree) && Math.Abs(free - prevFree) >= (50L << 20)
                    ? (free - prevFree > 0 ? "+" : "-") + Fmt.Bytes(Math.Abs(free - prevFree)) + " free since " + (prevKey.EndsWith(DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd")) ? "yesterday" : prevKey.Substring(3))
                    : "";
                hist[letter + "|" + today] = free;
                t.Invalidate();
            }
            if (saveHistory)
            {
                lastHistory = DateTime.Now;
                string cutoff = DateTime.Today.AddDays(-60).ToString("yyyy-MM-dd");
                foreach (var k in Store.History.DriveFreeDay.Keys.Where(k => string.CompareOrdinal(k.Substring(3), cutoff) < 0).ToList()) Store.History.DriveFreeDay.Remove(k);
                Store.MarkHistory();
            }
        }

        void SetBusy(bool b, string text)
        {
            busy = b;
            btnScan.Enabled = !b; btnDeep.Enabled = !b; btnDelete.Enabled = !b && AnyChecked();
            btnCancel.Visible = b;
            if (text != null) info.Text = text;
        }

        bool AnyChecked() { return lvClean.CheckedItems.Count > 0 || lvBig.CheckedItems.Count > 0; }

        void UpdateDeleteButton()
        {
            if (busy) return;
            long total = targets.Where(t => t.Checked && t.Size > 0).Sum(t => t.Size) + lvBig.CheckedItems.Cast<ListViewItem>().Sum(i => ((BigFile)i.Tag).Size);
            btnDelete.Text = total > 0 ? "Delete selected (" + Fmt.Bytes(total) + ")" : "Delete selected";
            btnDelete.Enabled = AnyChecked();
        }

        // Quick: measure every cleanup location (seconds).
        void MeasureAll()
        {
            if (busy) return;
            measured = true;
            cancel = false;
            var oldChecks = targets.ToDictionary(t => t.Id, t => t.Checked);
            var previousSizes = new Dictionary<string, long>(Store.History.CleanSizes);
            SetBusy(true, "Measuring cleanup locations...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = Cleaner.Targets();
                foreach (var t in list)
                {
                    bool c; if (oldChecks.TryGetValue(t.Id, out c)) t.Checked = c;
                    long prev; t.Prev = previousSizes.TryGetValue(t.Id, out prev) ? prev : -1;
                }
                UI(() => { targets = list; FillClean(); });
                foreach (var t in list)
                {
                    if (cancel) break;
                    try { Cleaner.Measure(t); } catch { t.Size = 0; }
                    var tt = t;
                    UI(() => UpdateCleanRow(tt));
                }
                UI(() =>
                {
                    foreach (var t in targets.Where(x => x.Size >= 0)) Store.History.CleanSizes[t.Id] = t.Size;
                    Store.MarkHistory();
                    long total = targets.Where(x => x.Size > 0).Sum(x => x.Size);
                    SetBusy(false, "Can be freed: " + Fmt.Bytes(total) + "   (checked items are safe to delete; tick others if you want)" + (Admin.IsAdmin ? "" : "   Some Windows items need \"Run as admin\"."));
                    UpdateDeleteButton();
                    UpdateDrives();
                });
            });
        }

        void FillClean()
        {
            lvClean.BeginUpdate();
            lvClean.Items.Clear();
            foreach (var t in targets)
            {
                var li = new ListViewItem(t.Name) { Tag = t, Checked = t.Checked };
                li.SubItems.Add("...");
                li.SubItems.Add("");
                li.SubItems.Add("");
                li.SubItems.Add(t.Desc + (t.Admin && !Admin.IsAdmin ? "  [needs admin]" : ""));
                lvClean.Items.Add(li);
            }
            lvClean.EndUpdate();
        }

        void UpdateCleanRow(CleanTarget t)
        {
            foreach (ListViewItem li in lvClean.Items)
            {
                if (li.Tag != t) continue;
                li.SubItems[1].Text = t.Size < 0 ? "..." : Fmt.Bytes(t.Size);
                li.SubItems[2].Text = Fmt.Delta(t.Size, t.Prev);
                li.SubItems[3].Text = t.Recycle ? t.Files + " items" : t.Files.ToString("N0");
                if (t.Size > (1L << 30)) li.ForeColor = Ui.Warning;
            }
            UpdateDeleteButton();
        }

        // Deep: walk whole drives for big files and folder sizes (background, low priority).
        void DeepScan()
        {
            if (busy) return;
            cancel = false;
            string[] roots;
            try { roots = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.Name).ToArray(); } catch { return; }
            SetBusy(true, "Deep scan started...");
            long files = 0, bytes = 0;
            string current = "";
            var progress = scanProgress = new System.Windows.Forms.Timer { Interval = 300 };
            progress.Tick += delegate { info.Text = "Scanning " + files.ToString("N0") + " files, " + Fmt.Bytes(bytes) + "   " + current; };
            progress.Start();
            var t = new Thread(() =>
            {
                var big = new List<BigFile>();
                var folders = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                string key = null;
                foreach (var root in roots)
                {
                    if (cancel) break;
                    FastFiles.Walk(root, (dir, name, size, written) =>
                    {
                        files++; bytes += size;
                        if (key != null) { long v; folders.TryGetValue(key, out v); folders[key] = v + size; }
                        if (size >= 100L << 20)
                        {
                            string full = dir + "\\" + name;
                            if (!Cleaner.IsProtected(full)) big.Add(new BigFile { Path = full, Size = size, Modified = written });
                        }
                    }, () => cancel, dir =>
                    {
                        current = dir;
                        key = Truncate(dir, 3);
                    });
                }
                // Roll depth-3 sizes up to depth 1 and 2.
                var all = new Dictionary<string, long>(folders, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in folders)
                {
                    for (int depth = 1; depth <= 2; depth++)
                    {
                        string up = Truncate(kv.Key, depth);
                        if (string.Equals(up, kv.Key, StringComparison.OrdinalIgnoreCase)) continue;
                        long v; all.TryGetValue(up, out v); all[up] = v + kv.Value;
                    }
                }
                var topBig = big.OrderByDescending(b => b.Size).Take(300).ToList();
                var topFolders = all.Where(kv => kv.Key.Length > 3).OrderByDescending(kv => kv.Value).Take(200).ToList();
                bool stopped = cancel;
                UI(() =>
                {
                    progress.Stop(); progress.Dispose();
                    bigFiles = topBig;
                    FillBig();
                    FillFolders(topFolders);
                    SetBusy(false, (stopped ? "Scan stopped. " : "Scan done. ") + files.ToString("N0") + " files, " + Fmt.Bytes(bytes) + " checked. Big files (100 MB+): " + topBig.Count + ". Tick the ones you do not need, then Delete selected.");
                    tabs.Select(1);
                    UpdateDeleteButton();
                });
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
        }

        static string Truncate(string dir, int depth)
        {
            // "D:\a\b\c\d" -> depth 3 -> "D:\a\b\c"
            int count = 0;
            for (int i = 3; i < dir.Length; i++)
            {
                if (dir[i] == '\\') { count++; if (count == depth) return dir.Substring(0, i); }
            }
            return dir;
        }

        void FillBig()
        {
            lvBig.BeginUpdate();
            lvBig.Items.Clear();
            foreach (var b in bigFiles)
            {
                var li = new ListViewItem(Path.GetFileName(b.Path)) { Tag = b };
                li.SubItems.Add(Fmt.Bytes(b.Size));
                li.SubItems.Add(DateTime.FromFileTime(b.Modified).ToString("d MMM yyyy"));
                li.SubItems.Add(Path.GetDirectoryName(b.Path));
                lvBig.Items.Add(li);
            }
            lvBig.EndUpdate();
        }

        void FillFolders(List<KeyValuePair<string, long>> top)
        {
            var hist = Store.History.FolderSizes;
            lvFolders.BeginUpdate();
            lvFolders.Items.Clear();
            foreach (var kv in top)
            {
                var li = new ListViewItem(kv.Key) { Tag = kv.Key };
                li.SubItems.Add(Fmt.Bytes(kv.Value));
                long prev;
                li.SubItems.Add(hist.TryGetValue(kv.Key, out prev) ? Fmt.Delta(kv.Value, prev) : "new");
                if (hist.ContainsKey(kv.Key) && kv.Value - prev > (500L << 20)) li.ForeColor = Ui.Warning;
                lvFolders.Items.Add(li);
            }
            lvFolders.EndUpdate();
            Store.History.FolderSizes = top.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            Store.History.LastDeepScan = DateTime.Now.Ticks;
            Store.MarkHistory();
        }

        void DeleteSelected()
        {
            if (busy) return;
            var cleanSel = targets.Where(t => t.Checked && t.Size != 0).ToList();
            var bigSel = lvBig.CheckedItems.Cast<ListViewItem>().Select(i => (BigFile)i.Tag).ToList();
            if (cleanSel.Count == 0 && bigSel.Count == 0) return;
            long total = cleanSel.Sum(t => Math.Max(0, t.Size)) + bigSel.Sum(b => b.Size);
            string msg = "Delete " + Fmt.Bytes(total) + "?\n\n";
            if (cleanSel.Count > 0) msg += "Cleanup: " + string.Join(", ", cleanSel.Select(t => t.Name)) + "\n";
            if (bigSel.Count > 0) msg += "\n" + bigSel.Count + " big file(s) go to the Recycle Bin (empty it to free the space).\n";
            msg += "\nFiles that are in use are skipped.";
            if (MessageBox.Show(FindForm(), msg, App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            cancel = false;
            SetBusy(true, "Deleting...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                long freed = 0; int bigFailed = 0;
                foreach (var t in cleanSel)
                {
                    if (cancel) break;
                    var tt = t;
                    UI(() => info.Text = "Cleaning " + tt.Name + "...");
                    try { freed += Cleaner.Clean(t, () => cancel); } catch (Exception ex) { App.Log("Clean " + t.Id + ": " + ex.Message); }
                }
                var removed = new List<BigFile>();
                foreach (var b in bigSel)
                {
                    if (cancel) break;
                    try
                    {
                        if (!File.Exists(b.Path) || Leftovers.Recycle(b.Path)) { freed += b.Size; removed.Add(b); }
                        else bigFailed++;
                    }
                    catch { bigFailed++; }
                }
                UI(() =>
                {
                    bigFiles.RemoveAll(removed.Contains);
                    FillBig();
                    SetBusy(false, null);
                    status("Cleaned " + Fmt.Bytes(freed) + (removed.Count > 0 ? " (big files are in the Recycle Bin)." : ".") + (bigFailed > 0 ? " " + bigFailed + " file(s) could not be deleted (in use or protected)." : ""));
                    measured = false;
                    MeasureAll();
                });
            });
        }

        static void Reveal(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        static void CheckAll(FastList lv, bool on)
        {
            lv.BeginUpdate();
            foreach (ListViewItem i in lv.Items) i.Checked = on;
            lv.EndUpdate();
        }

        // Right-click menus for the three lists (Part 9: right-click functions everywhere).
        void BuildMenus()
        {
            var mc = Ui.Menu();
            mc.Items.Add("Tick all", null, delegate { CheckAll(lvClean, true); });
            mc.Items.Add("Untick all", null, delegate { CheckAll(lvClean, false); });
            mc.Items.Add(new ToolStripSeparator());
            mc.Items.Add("Copy details", null, delegate
            {
                var t = lvClean.SelectedItems.Count > 0 ? lvClean.SelectedItems[0].Tag as CleanTarget : null;
                if (t != null) App.I.CopyText(t.Name + " - " + (t.Size < 0 ? "?" : Fmt.Bytes(t.Size)) + " - " + t.Desc);
            });
            mc.Items.Add("Refresh sizes", null, delegate { MeasureAll(); });
            Ui.ThemeMenu(mc); lvClean.ContextMenuStrip = mc;

            Func<BigFile> big = () => lvBig.SelectedItems.Count > 0 ? lvBig.SelectedItems[0].Tag as BigFile : null;
            var mb = Ui.Menu();
            mb.Items.Add("Show in folder", null, delegate { var b = big(); if (b != null) Reveal(b.Path); });
            Term.AddMenu(mb.Items, () => { var b = big(); return b == null ? null : b.Path; });
            mb.Items.Add("Copy path", null, delegate
            {
                var paths = lvBig.SelectedItems.Cast<ListViewItem>().Select(i => ((BigFile)i.Tag).Path).ToList();
                if (paths.Count > 0) App.I.CopyText(string.Join(Environment.NewLine, paths));
            });
            mb.Items.Add(new ToolStripSeparator());
            mb.Items.Add("Tick selected", null, delegate { foreach (ListViewItem i in lvBig.SelectedItems) i.Checked = true; });
            mb.Items.Add("Untick all", null, delegate { CheckAll(lvBig, false); });
            Ui.ThemeMenu(mb); lvBig.ContextMenuStrip = mb;

            Func<string> folder = () => lvFolders.SelectedItems.Count > 0 ? lvFolders.SelectedItems[0].Tag as string : null;
            var mf = Ui.Menu();
            mf.Items.Add("Open folder", null, delegate { var f = folder(); if (f != null) try { Process.Start("explorer.exe", "\"" + f + "\""); } catch { } });
            mf.Items.Add("Copy path", null, delegate { var f = folder(); if (f != null) App.I.CopyText(f); });
            Term.AddMenu(mf.Items, folder);
            Ui.ThemeMenu(mf); lvFolders.ContextMenuStrip = mf;
        }
    }

    // ======================= Main window "System" page =======================

    class SystemPage : Panel
    {
        readonly TabStrip tabs;
        readonly Control[] views;
        readonly Button pop;
        static readonly string[] ids = { "procs", "startup", "drives", "uninstall" };

        public SystemPage(MainForm owner)
        {
            Dock = DockStyle.Fill;
            Action<string> st = s => owner.Status(s);
            views = new Control[] { new ProcessesPanel(st), new StartupPanel(st), new DrivesPanel(st), new UninstallPanel(st) };
            host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, Ui.S(4), 0, 0) };
            foreach (var v in views) { v.Visible = false; v.Dock = DockStyle.None; host.Controls.Add(v); }
            host.Layout += delegate { ArrangeViews(); };
            Controls.Add(host);

            var top = MainForm.Bar();
            var head = MainForm.Heading("System");
            top.Controls.Add(head);
            tabs = new TabStrip { Dock = DockStyle.None, AutoSize = true, Margin = new Padding(0) };
            tabs.Add("Processes");
            tabs.Add("Startup apps");
            tabs.Add("Drives & cleanup");
            tabs.Add("Uninstall");
            var kpis = new SystemKpis();
            this.kpis = kpis;
            tabs.Changed += i =>
            {
                selected = i;
                if (i > 0) rightTab = i;
                SuspendLayout();
                ApplyVisibility();
                ResumeLayout(true);
            };
            Resize += delegate { bool s = Wide; if (s != split) { split = s; SuspendLayout(); ApplyVisibility(); ResumeLayout(true); } };
            top.Controls.Add(tabs);
            pop = Ui.Btn("Pop out as floating card", delegate { if (selected >= 0 && selected < 3) App.I.NewCard(new List<string> { ids[selected] }, null); });
            top.Controls.Add(pop);
            Controls.Add(kpis);
            Controls.Add(top);
            tabs.Select(0);
        }

        public void SelectTab(string id) { int i = Array.IndexOf(ids, id); if (i >= 0) tabs.Select(i); }

        // Wide windows: Processes stays on the left and the chosen other tab (Startup / Drives / Uninstall) fills the right,
        // instead of one list with a wide empty area. Narrow windows: one tab at a time.
        readonly Panel host;
        readonly SystemKpis kpis;
        int selected, rightTab = 1;
        bool split;
        bool Wide { get { return Width >= Ui.S(1450); } }

        void ApplyVisibility()
        {
            split = Wide;
            // Split: Processes is always on the left, so its tab hides and the strip shows which tab fills the right side.
            tabs.ShowTab(0, !split);
            if (split && tabs.Selected != rightTab) { tabs.Select(rightTab); return; }
            for (int k = 0; k < views.Length; k++) views[k].Visible = split ? (k == 0 || k == rightTab) : k == selected;
            pop.Visible = selected != 3;
            kpis.Visible = split || selected == 0;   // the performance strip belongs to Processes
            host.PerformLayout();
        }

        void ArrangeViews()
        {
            var r = host.DisplayRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            if (split)
            {
                int gap = Ui.S(16), left = (r.Width - gap) * 54 / 100;
                SetBoundsIfChanged(views[0], new Rectangle(r.X, r.Y, left, r.Height));
                SetBoundsIfChanged(views[rightTab], new Rectangle(r.X + left + gap, r.Y, r.Width - left - gap, r.Height));
            }
            else SetBoundsIfChanged(views[selected], r);
        }

        static void SetBoundsIfChanged(Control c, Rectangle r) { if (c.Bounds != r) c.Bounds = r; }
    }
}
