using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    static class ShellOps
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SHFILEINFO { public IntPtr hIcon; public int iIcon; public uint dwAttributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool SHObjectProperties(IntPtr hwnd, uint type, string name, string page);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        public const uint FO_MOVE = 1, FO_COPY = 2, FO_DELETE = 3, FO_RENAME = 4;

        // Windows shows its own progress / conflict dialogs. Returns true when not aborted.
        public static bool Run(IWin32Window owner, uint func, IEnumerable<string> from, string to, bool allowUndo = true)
        {
            var list = from.Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (list.Count == 0) return false;
            var op = new SHFILEOPSTRUCT { hwnd = owner == null ? IntPtr.Zero : owner.Handle, wFunc = func, pFrom = string.Join("\0", list) + "\0\0", pTo = to == null ? null : to + "\0\0", fFlags = (ushort)(allowUndo ? 0x40 : 0) };
            if (func == FO_COPY || func == FO_MOVE) op.fFlags |= 0x8; // FOF_RENAMEONCOLLISION when copying onto itself
            int r = SHFileOperation(ref op);
            return r == 0 && !op.fAnyOperationsAborted;
        }
        public static void Properties(string path) { try { SHObjectProperties(IntPtr.Zero, 2, path, null); } catch { } }

        static IntPtr sysList;
        public static IntPtr SystemImageList()
        {
            if (sysList == IntPtr.Zero) { var info = new SHFILEINFO(); sysList = SHGetFileInfo("C:\\", 0, ref info, (uint)Marshal.SizeOf(info), 0x4000 | 0x1 /* SYSICONINDEX | SMALLICON */); }
            return sysList;
        }
        static readonly Dictionary<string, int> extIcons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> fileIcons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static bool PerFile(string path, bool dir)
        {
            if (dir) return false;
            string ext = Path.GetExtension(path);
            return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Length == 0;
        }
        // True for files whose icon depends on the file itself (exe, lnk, ico, no extension): slow, so callers may resolve them on a worker.
        public static bool NeedsPerFile(string path) { return PerFile(path, false); }
        public static bool TryGetFileIcon(string path, out int idx) { lock (fileIcons) return fileIcons.TryGetValue(path, out idx); }
        // Cheap extension-based icon (no disk access), used as a placeholder while the real one resolves.
        public static int GenericIconIndex(string path) { return Resolve(path, false, false); }
        public static int IconIndex(string path, bool dir)
        {
            bool perFile = PerFile(path, dir);
            int c;
            if (perFile && TryGetFileIcon(path, out c)) return c;
            int i = Resolve(path, dir, perFile);
            if (perFile) lock (fileIcons) { if (fileIcons.Count > 20000) fileIcons.Clear(); fileIcons[path] = i; }
            return i;
        }
        static int Resolve(string path, bool dir, bool perFile)
        {
            string ext = dir ? "<dir>" : Path.GetExtension(path);
            int i;
            if (!perFile) lock (extIcons) { if (extIcons.TryGetValue(ext, out i)) return i; }
            var info = new SHFILEINFO();
            uint flags = 0x4000 | 0x1;
            if (!perFile) flags |= 0x10; // USEFILEATTRIBUTES (fast, no disk access)
            SHGetFileInfo(perFile ? path : (dir ? "folder" : "file" + ext), dir ? 0x10u : 0x80u, ref info, (uint)Marshal.SizeOf(info), flags);
            if (!perFile) lock (extIcons) extIcons[ext] = info.iIcon;
            return info.iIcon;
        }
    }

    class Entry { public string Path, Name, Type, Extra; public long Size; public DateTime Modified; public bool Dir; public int Icon = -1; public bool IconQueued; }

    class FolderPane : Panel
    {
        public event Action<FolderPane> Activated;
        public event Action<FolderPane> CopyToOther, MoveToOther;
        public string Current = "";
        readonly TextBox address, filter;
        readonly FastList lv;
        readonly Label foot;
        readonly List<string> back = new List<string>(), fwd = new List<string>();
        List<Entry> all = new List<Entry>(), rows = new List<Entry>();
        int sortCol; bool sortDesc;
        bool active;
        int loadId;
        FileSystemWatcher watcher;
        string selectAfterLoad;
        bool shown, pausedWatch;
        readonly Queue<Entry> iconQueue = new Queue<Entry>();
        bool iconWorker;
        readonly System.Windows.Forms.Timer refreshSoon = new System.Windows.Forms.Timer { Interval = 400 };
        readonly Panel head;

        public FolderPane()
        {
            Dock = DockStyle.Fill; Padding = new Padding(Ui.S(3));
            lv = new FastList { Dock = DockStyle.Fill, VirtualMode = true, MultiSelect = true, LabelEdit = false, AllowDrop = true };
            lv.Columns.Add("Name", Ui.S(240)); lv.Columns.Add("Size", Ui.S(104), HorizontalAlignment.Right); lv.Columns.Add("Modified", Ui.S(120)); lv.Columns.Add("Type", Ui.S(110));
            lv.FillIndex = 0; lv.DropOrder = new[] { 3, 2, 1 }; lv.MinFill = Ui.S(170);
            lv.RetrieveVirtualItem += Retrieve;
            lv.ColumnClick += (s, e) => { if (sortCol == e.Column) sortDesc = !sortDesc; else { sortCol = e.Column; sortDesc = e.Column == 1 || e.Column == 2; } Apply(); };
            lv.DoubleClick += delegate { OpenSelected(); };
            lv.KeyDown += OnKeys;
            lv.GotFocus += delegate { Activate(); };
            lv.MouseDown += delegate { Activate(); };
            lv.ItemDrag += delegate { var sel = SelectedPaths(); if (sel.Count > 0) { var data = new DataObject(DataFormats.FileDrop, sel.ToArray()); lv.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); } };
            lv.DragEnter += FilesOver; lv.DragOver += FilesOver;
            lv.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null || Current.Length == 0) return;
                bool move = (e.KeyState & 4) != 0 || (e.Effect == DragDropEffects.Move);
                Transfer(files, Current, move);
            };
            lv.ContextMenuStrip = BuildMenu();
            lv.HandleCreated += delegate { ShellOps.SendMessage(lv.Handle, 0x1003 /* LVM_SETIMAGELIST */, (IntPtr)1 /* LVSIL_SMALL */, ShellOps.SystemImageList()); };
            Controls.Add(lv);

            head = new Panel { Dock = DockStyle.Top, Height = Ui.S(34), Padding = new Padding(0, Ui.S(2), 0, Ui.S(4)) };
            address = new FluentTextBox { Dock = DockStyle.Fill };
            address.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Navigate(Environment.ExpandEnvironmentVariables(address.Text.Trim().Trim('"'))); } else if (e.KeyCode == Keys.Escape) { address.Text = Current; lv.Focus(); } };
            address.GotFocus += delegate { Activate(); };
            filter = Ui.Search("Filter"); filter.Dock = DockStyle.Right; filter.Width = Ui.S(90);
            filter.TextChanged += delegate { Apply(); };
            var nav = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            nav.Controls.Add(NavBtn("\uE72B", "Back (Alt+Left)", delegate { GoBack(); }));
            nav.Controls.Add(NavBtn("\uE72A", "Forward (Alt+Right)", delegate { GoForward(); }));
            nav.Controls.Add(NavBtn("\uE74A", "Up (Backspace)", delegate { GoUp(); }));
            nav.Controls.Add(NavBtn("\uE80F", "This PC", delegate { Navigate(""); }));
            head.Controls.Add(address); head.Controls.Add(filter); head.Controls.Add(nav);
            Controls.Add(head);
            foot = new Label { Dock = DockStyle.Bottom, Height = Ui.S(20), Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(foot);
            refreshSoon.Tick += delegate { refreshSoon.Stop(); Load(false); };
            Ui.Theme(this);
        }

        Button NavBtn(string glyph, string tip, EventHandler click)
        {
            var b = new Button { Text = glyph, Font = Ui.Icon(10f), Size = Ui.S(30, 28), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, Ui.S(2), 0), TabStop = false, ForeColor = Ui.Text, BackColor = Ui.Bg };
            b.FlatAppearance.BorderSize = 0; b.Click += click; b.Click += delegate { Activate(); };
            var t = new ToolTip(); t.SetToolTip(b, tip); b.Disposed += delegate { t.Dispose(); };
            return b;
        }

        void FilesOver(object s, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop) || Current.Length == 0) { e.Effect = DragDropEffects.None; return; }
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            bool sameDrive = files != null && files.Length > 0 && string.Equals(Path.GetPathRoot(files[0]), Path.GetPathRoot(Current), StringComparison.OrdinalIgnoreCase);
            e.Effect = (e.KeyState & 8) != 0 ? DragDropEffects.Copy : (e.KeyState & 4) != 0 ? DragDropEffects.Move : sameDrive ? DragDropEffects.Move : DragDropEffects.Copy;
        }

        public bool IsActive { get { return active; } set { active = value; head.BackColor = value ? Ui.SelBg : Ui.Bg; Invalidate(); } }
        void Activate() { var h = Activated; if (h != null) h(this); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(active ? Ui.Accent : Ui.Line, active ? 2 : 1)) e.Graphics.DrawRectangle(p, 1, 1, Width - 2, Height - 2);
        }
        public void FocusList() { lv.Focus(); }

        public void Navigate(string path, bool record = true)
        {
            if (path.Length > 0 && !Directory.Exists(path))
            {
                if (File.Exists(path)) { Hidden.Open(path); return; }
                MessageBox.Show(FindForm(), "Folder not found:\n" + path, App.Name); address.Text = Current; return;
            }
            if (record && Current != path) { back.Add(Current); fwd.Clear(); }
            if (!string.Equals(Current, path, StringComparison.OrdinalIgnoreCase)) lv.SelectedIndices.Clear();
            Current = path; address.Text = path.Length == 0 ? "This PC" : path; filter.Text = "";
            if (record && path.Length > 3) MetaStore.Touch("folders", path.TrimEnd('\\'));
            Watch();
            Load(true);
        }
        void GoBack() { if (back.Count == 0) return; fwd.Add(Current); var p = back[back.Count - 1]; back.RemoveAt(back.Count - 1); Navigate(p, false); }
        void GoForward() { if (fwd.Count == 0) return; back.Add(Current); var p = fwd[fwd.Count - 1]; fwd.RemoveAt(fwd.Count - 1); Navigate(p, false); }
        void GoUp()
        {
            if (Current.Length == 0) return;
            var parent = Directory.GetParent(Current.TrimEnd('\\') + (Current.EndsWith(":") ? "\\" : ""));
            string old = Current;
            // Load is asynchronous: remember the folder we came from and select it when the listing arrives.
            selectAfterLoad = old;
            Navigate(parent == null || old.TrimEnd('\\').Length <= 2 ? "" : parent.FullName);
            selectAfterLoad = null;
        }

        void StopWatch()
        {
            if (watcher != null) { try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { } watcher = null; }
        }

        // Called by the Explorer page: panes that are hidden (page not visible, or removed by a smaller layout) release their watcher.
        public void SetShown(bool on)
        {
            if (on == shown) return;
            shown = on;
            if (on)
            {
                Watch();
                if (pausedWatch) { pausedWatch = false; Load(false); }
            }
            else
            {
                pausedWatch = watcher != null;
                StopWatch();
                refreshSoon.Stop();
            }
        }

        void Watch()
        {
            StopWatch();
            if (!shown || Current.Length == 0) return;
            try
            {
                // Names only: size / last-write events fire continuously during downloads and caused reload storms.
                watcher = new FileSystemWatcher(Current) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                FileSystemEventHandler h = (s, e) => { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed) { refreshSoon.Stop(); refreshSoon.Start(); } })); } catch { } };
                watcher.Created += h; watcher.Deleted += h; watcher.Renamed += (s, e) => h(s, null);
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void Load(bool resetScroll)
        {
            int id = ++loadId;
            string path = Current;
            string selectName = selectAfterLoad; selectAfterLoad = null;
            var keep = new HashSet<string>(resetScroll ? new List<string>() : SelectedPaths(), StringComparer.OrdinalIgnoreCase);
            foot.Text = "Loading...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = new List<Entry>(); string error = null;
                try
                {
                    if (path.Length == 0)
                    {
                        foreach (var d in DriveInfo.GetDrives())
                        {
                            string label = d.Name;
                            try { if (d.IsReady) label = (string.IsNullOrEmpty(d.VolumeLabel) ? (d.DriveType == DriveType.Fixed ? "Local Disk" : d.DriveType == DriveType.Network ? "Network" : d.DriveType == DriveType.Removable ? "USB drive" : d.DriveType.ToString()) : d.VolumeLabel) + " (" + d.Name.TrimEnd('\\') + ")"; } catch { }
                            long free = 0, total = 0; try { if (d.IsReady) { free = d.AvailableFreeSpace; total = d.TotalSize; } } catch { }
                            list.Add(new Entry { Path = d.Name, Name = label, Dir = true, Type = total > 0 ? "Drive, " + Fmt.Bytes(total) : "Drive", Size = free, Extra = total > 0 ? (100 - free * 100 / total) + "% used" : "" });
                        }
                        foreach (var sf in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.UserProfile })
                        { string p = Environment.GetFolderPath(sf); if (Directory.Exists(p)) list.Add(new Entry { Path = p, Name = Path.GetFileName(p), Dir = true, Type = "Folder" }); }
                        string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                        if (Directory.Exists(dl)) list.Add(new Entry { Path = dl, Name = "Downloads", Dir = true, Type = "Folder" });
                    }
                    else
                    {
                        foreach (var fi in new DirectoryInfo(path).EnumerateFileSystemInfos())
                        {
                            if ((fi.Attributes & FileAttributes.Hidden) != 0 && (fi.Attributes & FileAttributes.System) != 0) continue;
                            bool dir = (fi.Attributes & FileAttributes.Directory) != 0;
                            var e = new Entry { Path = fi.FullName, Name = fi.Name, Dir = dir, Modified = fi.LastWriteTime };
                            if (!dir) { e.Size = ((FileInfo)fi).Length; string ext = fi.Extension; e.Type = ext.Length > 1 ? ext.Substring(1).ToUpperInvariant() + " file" : "File"; } else e.Type = "Folder";
                            list.Add(e);
                        }
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    App.I.Sync.BeginInvoke(new Action(() =>
                    {
                        if (id != loadId || IsDisposed) return;
                        all = list; Apply();
                        int si = -1;
                        if (selectName != null) si = rows.FindIndex(r => string.Equals(r.Path.TrimEnd('\\'), selectName.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                        if (si >= 0) SelectIndex(si);
                        else if (keep.Count > 0) { lv.SelectedIndices.Clear(); for (int i = 0; i < rows.Count; i++) if (keep.Contains(rows[i].Path)) lv.SelectedIndices.Add(i); }
                        else if (resetScroll && rows.Count > 0) SelectIndex(0);
                        if (error != null) foot.Text = error;
                    }));
                }
                catch { }
            });
        }

        void Apply()
        {
            string q = filter.Text.Trim();
            IEnumerable<Entry> items = all;
            if (q.Length > 0) items = items.Where(e => e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            Func<Entry, IComparable> key;
            switch (sortCol) { case 1: key = e => e.Size; break; case 2: key = e => e.Modified; break; case 3: key = e => e.Type; break; default: key = e => e.Name.ToLowerInvariant(); break; }
            var dirs = items.Where(e => e.Dir); var files = items.Where(e => !e.Dir);
            if (Current.Length == 0) rows = items.ToList();
            else rows = (sortDesc ? dirs.OrderByDescending(key) : dirs.OrderBy(key)).Concat(sortDesc ? files.OrderByDescending(key) : files.OrderBy(key)).ToList();
            lv.BeginUpdate(); lv.SelectedIndices.Clear(); lv.VirtualListSize = rows.Count; lv.EndUpdate(); lv.Invalidate();
            int nd = rows.Count(r => r.Dir), nf = rows.Count - nd;
            foot.Text = nd + " folders, " + nf + " files" + (nf > 0 ? ", " + Fmt.Bytes(rows.Where(r => !r.Dir).Sum(r => r.Size)) : "");
        }

        void Retrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= rows.Count) { e.Item = new ListViewItem(""); e.Item.SubItems.AddRange(new[] { "", "", "" }); return; }
            var r = rows[e.ItemIndex];
            int icon = r.Icon;
            if (icon < 0)
            {
                if (r.Dir) icon = r.Icon = ShellOps.IconIndex(r.Path, !r.Type.StartsWith("Drive"));
                else if (!ShellOps.NeedsPerFile(r.Path)) icon = r.Icon = ShellOps.IconIndex(r.Path, false);
                else
                {
                    // exe / lnk / ico / no extension: resolved once per path on a worker; show the generic icon meanwhile.
                    int cached;
                    if (ShellOps.TryGetFileIcon(r.Path, out cached)) icon = r.Icon = cached;
                    else { icon = ShellOps.GenericIconIndex(r.Path); QueueIcon(r); }
                }
            }
            var it = new ListViewItem(r.Name) { ImageIndex = icon };
            it.SubItems.Add(r.Type.StartsWith("Drive") ? (r.Size > 0 ? Fmt.Bytes(r.Size) + " free" : "") : r.Dir ? "" : Fmt.Bytes(r.Size));
            it.SubItems.Add(r.Extra ?? (r.Modified == DateTime.MinValue ? "" : r.Modified.ToString("yyyy-MM-dd HH:mm")));
            it.SubItems.Add(r.Type);
            e.Item = it;
        }

        void QueueIcon(Entry r)
        {
            lock (iconQueue)
            {
                if (r.IconQueued) return;
                r.IconQueued = true;
                iconQueue.Enqueue(r);
                if (iconWorker) return;
                iconWorker = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    Entry e;
                    lock (iconQueue) { if (iconQueue.Count == 0) { iconWorker = false; break; } e = iconQueue.Dequeue(); }
                    try { e.Icon = ShellOps.IconIndex(e.Path, false); } catch { e.IconQueued = false; }
                    bool last; lock (iconQueue) last = iconQueue.Count == 0;
                    if (last) try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed) lv.Invalidate(); })); } catch { }
                }
            });
        }

        void SelectIndex(int i) { lv.SelectedIndices.Clear(); lv.SelectedIndices.Add(i); try { lv.Items[i].Focused = true; lv.EnsureVisible(i); } catch { } }

        public List<string> SelectedPaths() { var l = new List<string>(); foreach (int i in lv.SelectedIndices) if (i >= 0 && i < rows.Count) l.Add(rows[i].Path); return l; }
        Entry FirstSel() { foreach (int i in lv.SelectedIndices) if (i >= 0 && i < rows.Count) return rows[i]; return null; }

        void OpenSelected()
        {
            var e = FirstSel(); if (e == null) return;
            if (e.Dir) Navigate(e.Path);
            else foreach (var p in SelectedPaths().Take(20)) Hidden.Open(p);
        }

        void OnKeys(object s, KeyEventArgs e)
        {
            bool h = true;
            if (e.KeyCode == Keys.Enter) OpenSelected();
            else if (e.KeyCode == Keys.Back || (e.Alt && e.KeyCode == Keys.Up)) GoUp();
            else if (e.Alt && e.KeyCode == Keys.Left) GoBack();
            else if (e.Alt && e.KeyCode == Keys.Right) GoForward();
            else if (e.Alt && e.KeyCode == Keys.Enter) { var f = FirstSel(); if (f != null) ShellOps.Properties(f.Path); }
            else if (e.KeyCode == Keys.Delete) Delete(e.Shift);
            else if (e.KeyCode == Keys.F2) Rename();
            else if (e.KeyCode == Keys.F5) { var c = CopyToOther; if (c != null) c(this); }
            else if (e.KeyCode == Keys.F6) { var m = MoveToOther; if (m != null) m(this); }
            else if (e.KeyCode == Keys.F7 || (e.Control && e.Shift && e.KeyCode == Keys.N)) NewFolder();
            else if (e.Control && e.KeyCode == Keys.R) Load(false);
            else if (e.Control && e.KeyCode == Keys.C) ClipCopy(false);
            else if (e.Control && e.KeyCode == Keys.X) ClipCopy(true);
            else if (e.Control && e.KeyCode == Keys.V) Paste();
            else if (e.Control && e.KeyCode == Keys.A) { lv.BeginUpdate(); for (int i = 0; i < rows.Count; i++) lv.SelectedIndices.Add(i); lv.EndUpdate(); }
            else if (e.Control && e.KeyCode == Keys.L) { address.Focus(); address.SelectAll(); }
            else if (e.Control && e.KeyCode == Keys.F) filter.Focus();
            else h = false;
            if (h) { e.SuppressKeyPress = true; e.Handled = true; }
        }

        // Returns true only when the shell operation ran and was not aborted.
        public bool Transfer(IEnumerable<string> files, string target, bool move)
        {
            var list = files.Where(f => !string.Equals(Path.GetDirectoryName(f.TrimEnd('\\')), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || !move).ToList();
            if (list.Count == 0 || string.IsNullOrEmpty(target)) return false;
            var owner = FindForm();
            return ShellOps.Run(owner, move ? ShellOps.FO_MOVE : ShellOps.FO_COPY, list, target);
        }

        void ClipCopy(bool cut)
        {
            var sel = SelectedPaths(); if (sel.Count == 0) return;
            var sc = new StringCollection(); sc.AddRange(sel.ToArray());
            var data = new DataObject(); data.SetFileDropList(sc);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            try { Clipboard.SetDataObject(data, true); } catch { }
            foot.Text = (cut ? "Cut " : "Copied ") + sel.Count + " item(s). Ctrl+V in any pane or in Windows Explorer.";
        }

        void Paste()
        {
            if (Current.Length == 0) return;
            try
            {
                var data = Clipboard.GetDataObject();
                if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = (string[])data.GetData(DataFormats.FileDrop);
                bool move = false;
                var eff = data.GetData("Preferred DropEffect") as MemoryStream;
                if (eff != null) { var b = new byte[4]; eff.Read(b, 0, 4); move = (BitConverter.ToInt32(b, 0) & 2) != 0; }
                if (Transfer(files, Current, move) && move) Clipboard.Clear();
            }
            catch (Exception ex) { foot.Text = "Paste failed: " + ex.Message; }
        }

        void Delete(bool permanent)
        {
            var sel = SelectedPaths(); if (sel.Count == 0 || Current.Length == 0) return;
            if (permanent && MessageBox.Show(FindForm(), "Delete " + sel.Count + " item(s) PERMANENTLY (not to the Recycle Bin)?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ShellOps.Run(FindForm(), ShellOps.FO_DELETE, sel, null, !permanent);
        }

        void Rename()
        {
            var e = FirstSel(); if (e == null || Current.Length == 0) return;
            var n = Ui.Ask("Rename", "New name", e.Name, FindForm());
            if (string.IsNullOrEmpty(n) || n == e.Name) return;
            try
            {
                string target = Path.Combine(Path.GetDirectoryName(e.Path.TrimEnd('\\')), n);
                if (e.Dir) Directory.Move(e.Path, target); else File.Move(e.Path, target);
                Load(false);
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Rename failed: " + ex.Message, App.Name); }
        }

        void NewFolder()
        {
            if (Current.Length == 0) return;
            var n = Ui.Ask("New folder", "Folder name", "New folder", FindForm()); if (string.IsNullOrEmpty(n)) return;
            try { Directory.CreateDirectory(Path.Combine(Current, n)); Load(false); } catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, App.Name); }
        }

        ContextMenuStrip BuildMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("Open\tEnter", null, delegate { OpenSelected(); });
            m.Items.Add("Open with...", null, delegate { var f = FirstSel(); if (f != null && !f.Dir) Hidden.Open("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + f.Path); });
            m.Items.Add("Open in Windows Explorer", null, delegate { var f = FirstSel(); Hidden.Open("explorer.exe", f == null ? "\"" + Current + "\"" : "/select,\"" + f.Path + "\""); });
            Term.AddMenu(m.Items, () => Current);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Copy\tCtrl+C", null, delegate { ClipCopy(false); });
            m.Items.Add("Cut\tCtrl+X", null, delegate { ClipCopy(true); });
            m.Items.Add("Paste\tCtrl+V", null, delegate { Paste(); });
            m.Items.Add("Copy to next pane\tF5", null, delegate { var c = CopyToOther; if (c != null) c(this); });
            m.Items.Add("Move to next pane\tF6", null, delegate { var c = MoveToOther; if (c != null) c(this); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Copy path", null, delegate { App.I.CopyText(string.Join("\n", SelectedPaths().DefaultIfEmpty(Current))); });
            m.Items.Add("Copy name", null, delegate { App.I.CopyText(string.Join("\n", SelectedPaths().Select(p => Path.GetFileName(p.TrimEnd('\\'))))); });
            m.Items.Add("Rename\tF2", null, delegate { Rename(); });
            m.Items.Add("New folder\tF7", null, delegate { NewFolder(); });
            m.Items.Add("Delete (Recycle Bin)\tDel", null, delegate { Delete(false); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Add to favorites", null, delegate { foreach (var p in SelectedPaths().Where(Directory.Exists).DefaultIfEmpty(Current)) if (p.Length > 0) MetaStore.SetFav("folders", p.TrimEnd('\\'), true); });
            m.Items.Add("Add to quick launch", null, delegate { foreach (var p in SelectedPaths().DefaultIfEmpty(Current)) if (p.Length > 0) QuickDock.AddItem(Path.GetFileName(p.TrimEnd('\\')), p, ""); });
            m.Items.Add("What is using this file?", null, delegate { var f = FirstSel(); if (f != null) PowerTools.WhoUses(f.Path, FindForm()); });
            m.Items.Add("Folder size", null, delegate
            {
                var f = FirstSel(); if (f == null || !f.Dir) return; string p = f.Path; foot.Text = "Measuring " + p + " ...";
                ThreadPool.QueueUserWorkItem(delegate { long s = Leftovers.Size(p); try { BeginInvoke(new Action(() => foot.Text = p + " = " + Fmt.Bytes(s))); } catch { } });
            });
            m.Items.Add("Properties\tAlt+Enter", null, delegate { var f = FirstSel(); ShellOps.Properties(f != null ? f.Path : Current); });
            m.Items.Add("Refresh\tCtrl+R", null, delegate { Load(false); });
            Ui.ThemeMenu(m);
            return m;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopWatch(); refreshSoon.Dispose(); }
            base.Dispose(disposing);
        }
    }

    class ExplorerPage : Panel
    {
        readonly List<FolderPane> panes = new List<FolderPane>();
        readonly TableLayoutPanel grid;
        readonly ComboBox layout;
        readonly PlacesBar places;
        FolderPane active;
        static readonly string[] Layouts = { "1", "2", "3", "4", "2x2" };
        bool started;

        public ExplorerPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            grid = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            Controls.Add(grid);
            places = new PlacesBar { Dock = DockStyle.Left, Width = Ui.S(230) };
            places.Open += path => { if (!started) { started = true; Arrange(); } var p = active ?? panes.FirstOrDefault(); if (p != null) { p.Navigate(path); p.FocusList(); } };
            Controls.Add(places);
            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Explorer"));
            layout = MainForm.Combo(Ui.S(150), "1 pane", "2 panes", "3 panes", "4 panes", "2 x 2 quarters");
            layout.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            int li = Array.IndexOf(Layouts, Store.Settings.ExplorerLayout); layout.SelectedIndex = li < 0 ? 1 : li;
            layout.SelectedIndexChanged += delegate { Store.Settings.ExplorerLayout = Layouts[layout.SelectedIndex]; Store.MarkSettings(); Arrange(); };
            bar.Controls.Add(layout);
            bar.Controls.Add(Ui.Btn("Places", delegate { PlacesOff = !PlacesOff; PerformLayout(); }));
            bar.Controls.Add(Ui.Btn("Copy to next  F5", delegate { if (active != null) CopyNext(active, false); }, true));
            bar.Controls.Add(Ui.Btn("Move to next  F6", delegate { if (active != null) CopyNext(active, true); }));
            bar.Controls.Add(Ui.Btn("Same folder in all", delegate { if (active != null) foreach (var p in panes) if (p != active) p.Navigate(active.Current); }));
            bar.Controls.Add(Ui.Btn("PWSH here", delegate { var p = active ?? panes.FirstOrDefault(); if (p != null) Term.Open(p.Current, ""); }));
            bar.Controls.Add(Ui.Btn("Claude here", delegate { var p = active ?? panes.FirstOrDefault(); if (p != null) Term.Open(p.Current, "claude"); }));
            bar.Controls.Add(Ui.Btn("Terminal per pane", delegate
            {
                string l = Layouts[Math.Max(0, layout.SelectedIndex)];
                int count = Math.Min(panes.Count, l == "2x2" ? 4 : int.Parse(l));
                var ws = new TermWorkspace { Name = "Explorer panes", Layout = l, Mode = "tiled" };
                for (int i = 0; i < count; i++) ws.Panes.Add(new TermPane { Folder = panes[i].Current, Shell = Store.Settings.TermShell });
                Term.Launch(ws);
            }));
            var hint = new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(0, Ui.S(9), 0, 0), Text = "Drag files between panes · Ctrl+C / X / V · F2 rename · F7 new folder · Del = Recycle Bin" };
            bar.Controls.Add(hint);
            Controls.Add(bar);
            Ui.Theme(this);
        }

        static bool PlacesOff
        {
            get { int v; return Store.Settings.PaneSizes != null && Store.Settings.PaneSizes.TryGetValue("explorer.places.off", out v) && v == 1; }
            set { if (Store.Settings.PaneSizes == null) Store.Settings.PaneSizes = new Dictionary<string, int>(); Store.Settings.PaneSizes["explorer.places.off"] = value ? 1 : 0; Store.MarkSettings(); }
        }
        // The places column shows when there is room (and the user has not hidden it); narrow windows give the panes all the width.
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (places != null)
            {
                bool show = !PlacesOff && ClientSize.Width >= Ui.S(900);
                if (places.Visible != show) places.Visible = show;
                int w = ClientSize.Width >= Ui.S(1600) ? Ui.S(250) : Ui.S(210);
                if (places.Width != w) places.Width = w;
            }
            base.OnLayout(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && places != null) places.Reload();
            if (Visible && !started) { started = true; Arrange(); }
            ApplyShown();
        }

        // Only panes that are on screen keep a file watcher.
        void ApplyShown()
        {
            if (panes == null || grid == null) return;   // Visible can change inside the constructor, before the panes exist
            int count = Math.Min(panes.Count, grid.Controls.Count);
            bool vis = Visible && !IsDisposed;
            for (int i = 0; i < panes.Count; i++) panes[i].SetShown(vis && i < count);
        }

        void Arrange()
        {
            string l = Layouts[Math.Max(0, layout.SelectedIndex)];
            int count = l == "2x2" ? 4 : int.Parse(l);
            while (panes.Count < count)
            {
                var p = new FolderPane();
                p.Activated += SetActive; p.CopyToOther += x => CopyNext(x, false); p.MoveToOther += x => CopyNext(x, true);
                int i = panes.Count;
                string start = i < Store.Settings.ExplorerPaths.Count ? Store.Settings.ExplorerPaths[i] : "";
                if (start == null || (start.Length > 0 && !Directory.Exists(start))) start = i == 0 ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : i == 1 ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                panes.Add(p);
                p.Navigate(start, false);
            }
            Ui.Batch(grid, () =>
            {
                grid.Controls.Clear(); grid.ColumnStyles.Clear(); grid.RowStyles.Clear();
                int cols = l == "2x2" ? 2 : count, rowsN = l == "2x2" ? 2 : 1;
                grid.ColumnCount = cols; grid.RowCount = rowsN;
                for (int c = 0; c < cols; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
                for (int r = 0; r < rowsN; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowsN));
                for (int i = 0; i < count; i++) { panes[i].Margin = new Padding(Ui.S(2)); grid.Controls.Add(panes[i], i % cols, i / cols); }
            });
            if (active == null || panes.IndexOf(active) >= count) SetActive(panes[0]);
            ApplyShown();
        }

        void SetActive(FolderPane p)
        {
            active = p;
            foreach (var x in panes) x.IsActive = x == p;
            SavePaths();
        }

        void SavePaths()
        {
            var cur = panes.Select(p => p.Current).ToList();
            var old = Store.Settings.ExplorerPaths;
            if (old != null && old.Count == cur.Count && old.SequenceEqual(cur)) return; // nothing changed: do not dirty the settings
            Store.Settings.ExplorerPaths = cur;
            Store.MarkSettings();
        }

        void CopyNext(FolderPane from, bool move)
        {
            int visible = grid.Controls.Count;
            if (visible < 2) { MessageBox.Show(FindForm(), "Open 2 or more panes to copy between them.", App.Name); return; }
            int i = panes.IndexOf(from);
            var to = panes[(i + 1) % visible];
            var sel = from.SelectedPaths();
            if (sel.Count == 0 || to.Current.Length == 0) return;
            from.Transfer(sel, to.Current, move);
        }

        public void OpenPath(string path) { if (!started) { started = true; Arrange(); } if (active != null) active.Navigate(path); }
        protected override void Dispose(bool disposing) { if (disposing && panes.Count > 0) SavePaths(); base.Dispose(disposing); }
    }

    // Left side of the Explorer page: favourite folders, drives with usage bars and recently visited folders.
    // Click opens the place in the active pane; right-click manages it (favourite, colour, name, note...).
    class PlacesBar : Control
    {
        public event Action<string> Open;
        class Row { public string Id, Name, Glyph, Free; public double Used = -1; public Color Color; public bool Head, Fav; public Rectangle R; }
        readonly List<Row> rows = new List<Row>();
        Row hot; int scroll, contentH;
        bool dirty = true;
        readonly ContextMenuStrip menu = Ui.Menu();
        public PlacesBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Store.MetaChanged += OnMeta;
            Ui.ThemeMenu(menu);
        }
        void OnMeta() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke((Action)OnMeta); return; } dirty = true; if (Visible) Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) { Store.MetaChanged -= OnMeta; menu.Dispose(); } base.Dispose(disposing); }
        public void Reload() { dirty = true; Invalidate(); }

        void Build()
        {
            dirty = false; rows.Clear();
            Action<string> head = s => rows.Add(new Row { Head = true, Name = s });
            head("Favorites");
            var favs = MetaStore.Favorites("folders");
            foreach (var m in favs) rows.Add(new Row { Id = m.Id, Name = MetaStore.DisplayName(m), Glyph = "", Color = MetaStore.ColorOf(m.Color), Fav = true });
            if (favs.Count == 0)
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var p in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.Combine(home, "Downloads"), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
                    if (Directory.Exists(p)) rows.Add(new Row { Id = p, Name = Path.GetFileName(p), Glyph = "" });
            }
            head("Drives");
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    var r = new Row { Id = d.Name, Name = d.Name.TrimEnd('\\'), Glyph = d.DriveType == DriveType.Network ? "" : d.DriveType == DriveType.Removable ? "" : "" };
                    try { if (d.IsReady) { r.Name = (string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel) + " (" + d.Name.TrimEnd('\\') + ")"; if (d.TotalSize > 0) { r.Used = 1.0 - (double)d.AvailableFreeSpace / d.TotalSize; r.Free = Fmt.Bytes(d.AvailableFreeSpace) + " free"; } } } catch { }
                    rows.Add(r);
                }
            }
            catch { }
            var recent = MetaStore.Recent("folders", 12).Where(m => !m.Fav).ToList();
            if (recent.Count > 0)
            {
                head("Recent");
                foreach (var m in recent) rows.Add(new Row { Id = m.Id, Name = MetaStore.DisplayName(m), Glyph = "", Color = MetaStore.ColorOf(m.Color) });
            }
        }

        void Arrange()
        {
            int y = Ui.S(6) - scroll;
            foreach (var r in rows)
            {
                int h = r.Head ? Ui.S(28) : r.Used >= 0 ? Ui.S(54) : Ui.S(30);
                r.R = new Rectangle(Ui.S(6), y, Width - Ui.S(12), h);
                y += h;
            }
            contentH = y + scroll + Ui.S(8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (dirty) Build();
            Arrange();
            var g = e.Graphics; g.Clear(Ui.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Ui.Line)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            foreach (var r in rows)
            {
                if (r.R.Bottom < 0 || r.R.Top > Height) continue;
                if (r.Head) { TextRenderer.DrawText(g, r.Name.ToUpperInvariant(), Ui.Small, new Rectangle(r.R.X + Ui.S(6), r.R.Y + Ui.S(8), r.R.Width, Ui.S(18)), Ui.Muted, flags); continue; }
                if (r == hot) using (var path = Ui.Round(r.R, Ui.S(6))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                var line = new Rectangle(r.R.X, r.R.Y, r.R.Width, Ui.S(30));
                Color gc = r.Fav ? Color.FromArgb(245, 176, 40) : !r.Color.IsEmpty ? r.Color : Ui.Accent;
                TextRenderer.DrawText(g, r.Glyph, Ui.Icon(10.5f), new Rectangle(line.X + Ui.S(4), line.Y, Ui.S(24), line.Height), gc, flags | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, r.Name, Ui.Base, new Rectangle(line.X + Ui.S(32), line.Y, line.Width - Ui.S(36), line.Height), Ui.Text, flags);
                if (r.Used >= 0)
                {
                    var bar = new Rectangle(line.X + Ui.S(34), r.R.Y + Ui.S(30), line.Width - Ui.S(44), Ui.S(5));
                    using (var b = new SolidBrush(Ui.Track)) g.FillRectangle(b, bar);
                    Color uc = r.Used > 0.9 ? Ui.Danger : r.Used > 0.8 ? Ui.Warning : Ui.Accent;
                    using (var b = new SolidBrush(uc)) g.FillRectangle(b, bar.X, bar.Y, (int)(bar.Width * Math.Min(1, r.Used)), bar.Height);
                    if (r.Free != null) TextRenderer.DrawText(g, r.Free, Ui.Small, new Rectangle(bar.X, r.R.Y + Ui.S(34), bar.Width, Ui.S(16)), r.Used > 0.9 ? Ui.Danger : Ui.Muted, flags);
                }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int max = Math.Max(0, contentH - Height);
            int s = Math.Max(0, Math.Min(max, scroll - e.Delta / 3));
            if (s != scroll) { scroll = s; Invalidate(); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var r = rows.FirstOrDefault(x => !x.Head && x.R.Contains(e.Location));
            if (r != hot) { hot = r; Invalidate(); Cursor = r != null ? Cursors.Hand : Cursors.Default; }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != null) { hot = null; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var r = rows.FirstOrDefault(x => !x.Head && x.R.Contains(e.Location));
            if (r == null) return;
            if (e.Button == MouseButtons.Left) { var o = Open; if (o != null) o(r.Id); return; }
            if (e.Button != MouseButtons.Right) return;
            string id = r.Id;
            menu.Items.Clear();
            menu.Items.Add("Open", null, delegate { var o = Open; if (o != null) o(id); });
            Term.AddMenu(menu.Items, () => id);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Copy path", null, delegate { App.I.CopyText(id); });
            if (id.Length > 3) menu.Items.Add(MetaStore.Menu("folders", id.TrimEnd('\\'), Path.GetFileName(id.TrimEnd('\\')), null));
            menu.Show(this, e.Location);
        }
    }

    // Narrow, customizable quick-launch strip on the right side of the main window.
    class QuickDock : Panel
    {
        static readonly List<QuickDock> docks = new List<QuickDock>();
        readonly FlowLayoutPanel flow;
        readonly ToolTip tip = new ToolTip();
        // Dock icons by item path; owned by this dock, disposed when the path leaves the dock or the dock closes.
        readonly Dictionary<string, Image> iconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public QuickDock()
        {
            Dock = DockStyle.Right; Width = Ui.S(58); BackColor = Ui.Nav; Padding = new Padding(Ui.S(4));
            flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, AllowDrop = true };
            flow.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; };
            flow.DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) foreach (var p in f) AddItem(Path.GetFileNameWithoutExtension(p.TrimEnd('\\')), p, ""); };
            Controls.Add(flow);
            var add = new Button { Text = Ui.GAdd, Font = Ui.Icon(12f), Dock = DockStyle.Bottom, Height = Ui.S(40), FlatStyle = FlatStyle.Flat, ForeColor = Ui.Text, BackColor = Ui.Nav };
            add.FlatAppearance.BorderSize = 0;
            add.Click += delegate { AddMenu().Show(add, new Point(0, 0), ToolStripDropDownDirection.AboveLeft); };
            tip.SetToolTip(add, "Add an app, folder, file or web address (or drop files here)");
            Controls.Add(add);
            docks.Add(this);
            Seed();
            Rebuild();
        }

        static void Seed()
        {
            if (Store.Settings.DockSeeded) return;
            Store.Settings.DockSeeded = true;
            var d = Store.Settings.Dock;
            d.Add(new DockItem { Name = "File Explorer", Path = "explorer.exe" });
            d.Add(new DockItem { Name = "Downloads", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") });
            d.Add(new DockItem { Name = "Documents", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) });
            d.Add(new DockItem { Name = "Notepad", Path = "notepad.exe" });
            d.Add(new DockItem { Name = "Calculator", Path = "calc.exe" });
            d.Add(new DockItem { Name = "Command prompt", Path = "cmd.exe" });
            d.Add(new DockItem { Name = "Task Manager", Path = "taskmgr.exe" });
            d.Add(new DockItem { Name = "Control Panel", Path = "control.exe" });
            d.Add(new DockItem { Name = "Settings", Path = "ms-settings:" });
            Store.MarkSettings();
        }

        public static void AddItem(string name, string path, string args)
        {
            if (string.IsNullOrEmpty(path)) return;
            Store.Settings.Dock.Add(new DockItem { Name = string.IsNullOrEmpty(name) ? path : name, Path = path, Args = args ?? "" });
            Store.MarkSettings();
            foreach (var d in docks.ToList()) if (!d.IsDisposed) d.Rebuild();
            App.I.Balloon("Added to quick launch", name);
        }

        ContextMenuStrip AddMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("App or file...", null, delegate
            {
                using (var d = new OpenFileDialog { Filter = "Apps and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All files|*.*", Multiselect = true })
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) foreach (var f in d.FileNames) AddItem(Path.GetFileNameWithoutExtension(f), f, "");
            });
            m.Items.Add("Folder...", null, delegate { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(FindForm()) == DialogResult.OK) AddItem(Path.GetFileName(d.SelectedPath.TrimEnd('\\')), d.SelectedPath, ""); });
            m.Items.Add("Web address...", null, delegate { var u = Ui.Ask("Add web address", "Address", "https://", FindForm()); if (!string.IsNullOrEmpty(u) && u.Length > 8) { Uri uri; string host = Uri.TryCreate(u.Contains("://") ? u : "https://" + u, UriKind.Absolute, out uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : u; var n = Ui.Ask("Add web address", "Name", host, FindForm()); AddItem(n, u, ""); } });
            m.Items.Add("Command...", null, delegate { var c = Ui.Ask("Add command", "Program (e.g. cmd.exe, ms-settings:display)", "", FindForm()); if (string.IsNullOrEmpty(c)) return; var a = Ui.Ask("Add command", "Arguments (optional)", "", FindForm()) ?? ""; AddItem(Path.GetFileNameWithoutExtension(c), c, a); });
            var win = new ToolStripMenuItem("Windows tools");
            foreach (var t in new[] { new[] { "Device Manager", "devmgmt.msc" }, new[] { "Services", "services.msc" }, new[] { "Disk Management", "diskmgmt.msc" }, new[] { "Event Viewer", "eventvwr.msc" }, new[] { "Registry Editor", "regedit.exe" }, new[] { "Network connections", "ncpa.cpl" }, new[] { "Sound", "mmsys.cpl" }, new[] { "Power options", "powercfg.cpl" }, new[] { "System properties", "sysdm.cpl" }, new[] { "Programs and Features", "appwiz.cpl" }, new[] { "Snipping Tool", "snippingtool.exe" }, new[] { "Paint", "mspaint.exe" }, new[] { "Resource Monitor", "resmon.exe" } })
            { var x = t; win.DropDownItems.Add(x[0], null, delegate { AddItem(x[0], x[1], ""); }); }
            m.Items.Add(win);
            Ui.ThemeMenu(m);
            return m;
        }

        public void Rebuild()
        {
            Ui.Batch(flow, () =>
            {
                foreach (Control c in flow.Controls.Cast<Control>().ToList())
                {
                    var oldMenu = c.ContextMenuStrip;
                    if (oldMenu != null) { c.ContextMenuStrip = null; oldMenu.Dispose(); }
                    c.Dispose();
                }
                var dockItems = Store.Settings.Dock.ToList();
                // Drop cached icons whose path is no longer in the dock.
                var live = new HashSet<string>(dockItems.Select(d => d.Path ?? ""), StringComparer.OrdinalIgnoreCase);
                foreach (var stale in iconCache.Keys.Where(k => !live.Contains(k)).ToList()) { iconCache[stale].Dispose(); iconCache.Remove(stale); }
                foreach (var item in dockItems)
                {
                    var it = item;
                    var b = new Button { Size = Ui.S(44, 42), FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 0, Ui.S(4)), BackColor = Ui.Nav, ForeColor = Ui.Text, TabStop = false, AllowDrop = true };
                    b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Ui.HoverBg;
                    Image img;
                    if (!iconCache.TryGetValue(it.Path ?? "", out img))
                    {
                        img = IconOf(it.Path);
                        if (img != null) iconCache[it.Path ?? ""] = img; // missing icons are not cached, so they are retried on the next rebuild
                    }
                    if (img != null) b.Image = img; else { b.Text = it.Name.Length > 0 ? it.Name.Substring(0, 1).ToUpperInvariant() : "?"; b.Font = Ui.Big; }
                    tip.SetToolTip(b, it.Name + "\n" + it.Path + (it.Args.Length > 0 ? " " + it.Args : ""));
                    b.Click += delegate { Launch(it); };
                    b.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
                    b.DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null) Hidden.Open(it.Path, string.Join(" ", f.Select(x => "\"" + x + "\""))); };
                    var m = Ui.Menu();
                    m.Items.Add("Open", null, delegate { Launch(it); });
                    m.Items.Add("Run as administrator", null, delegate { try { Process.Start(new ProcessStartInfo(it.Path, it.Args) { UseShellExecute = true, Verb = "runas" }); } catch { } });
                    m.Items.Add("Open file location", null, delegate { if (File.Exists(it.Path) || Directory.Exists(it.Path)) Hidden.Open("explorer.exe", "/select,\"" + it.Path + "\""); });
                    Term.AddMenu(m.Items, () => it.Path);
                    m.Items.Add("Rename...", null, delegate { var n = Ui.Ask("Rename", "Name", it.Name, FindForm()); if (!string.IsNullOrEmpty(n)) { it.Name = n; Store.MarkSettings(); Rebuild(); } });
                    m.Items.Add("Edit arguments...", null, delegate { var a = Ui.Ask("Arguments", "Arguments", it.Args, FindForm()); if (a != null) { it.Args = a; Store.MarkSettings(); Rebuild(); } });
                    m.Items.Add("Give it a global shortcut...", null, delegate { ShortcutsPage.AddLauncherFor(it.Name, it.Path, it.Args, FindForm()); });
                    m.Items.Add("Move up", null, delegate { MoveItem(it, -1); });
                    m.Items.Add("Move down", null, delegate { MoveItem(it, 1); });
                    m.Items.Add(new ToolStripSeparator());
                    m.Items.Add("Remove", null, delegate { Store.Settings.Dock.Remove(it); Store.MarkSettings(); Rebuild(); });
                    Ui.ThemeMenu(m);
                    b.ContextMenuStrip = m;
                    flow.Controls.Add(b);
                }
            });
        }

        void MoveItem(DockItem it, int dir)
        {
            var l = Store.Settings.Dock; int i = l.IndexOf(it), j = i + dir;
            if (i < 0 || j < 0 || j >= l.Count) return;
            l.RemoveAt(i); l.Insert(j, it); Store.MarkSettings(); Rebuild();
        }

        public static void Launch(DockItem it)
        {
            if (Directory.Exists(it.Path)) { var f = Application.OpenForms.OfType<MainForm>().FirstOrDefault(); if (f != null && f.Visible && Control.ModifierKeys != Keys.Shift) { f.OpenInExplorer(it.Path); return; } }
            Hidden.Open(it.Path, it.Args);
        }

        static Image IconOf(string path)
        {
            try
            {
                string p = Environment.ExpandEnvironmentVariables(path);
                if (!File.Exists(p) && !Directory.Exists(p) && !p.Contains("\\") && !p.Contains(":"))
                {
                    string sys = Path.Combine(Environment.SystemDirectory, p); if (File.Exists(sys)) p = sys;
                    else { string win = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p); if (File.Exists(win)) p = win; }
                }
                if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return Ui.GlyphImage("\uE774", Ui.S(26), Ui.Accent);
                if (p.StartsWith("ms-settings", StringComparison.OrdinalIgnoreCase)) return Ui.GlyphImage("\uE713", Ui.S(26), Ui.Accent);
                if (!File.Exists(p) && !Directory.Exists(p)) return null;
                var info = new ShellOps.SHFILEINFO();
                ShellOps.SHGetFileInfo(p, 0, ref info, (uint)Marshal.SizeOf(info), 0x100 /* ICON */ | 0x0 /* LARGE */);
                if (info.hIcon == IntPtr.Zero) return null;
                try
                {
                    using (var ic = Icon.FromHandle(info.hIcon))
                    {
                        var bmp = new Bitmap(Ui.S(28), Ui.S(28));
                        using (var g = Graphics.FromImage(bmp)) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; using (var big = ic.ToBitmap()) g.DrawImage(big, 0, 0, bmp.Width, bmp.Height); }
                        return bmp;
                    }
                }
                finally { DestroyIcon(info.hIcon); } // Icon.FromHandle does not own the handle
            }
            catch { return null; }
        }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        protected override void Dispose(bool disposing)
        {
            if (disposing) { docks.Remove(this); tip.Dispose(); foreach (var im in iconCache.Values) im.Dispose(); iconCache.Clear(); }
            base.Dispose(disposing);
        }
    }
}
