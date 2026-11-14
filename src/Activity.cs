using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // Watches which files you open and close:
    //  - Excel, Word, PowerPoint: read through their own COM automation (authorized way for protected files)
    //  - every other app: Windows "Recent items" + window titles for the close time
    static class ActivityMonitor
    {
        static Thread worker;
        static volatile bool running;
        static bool startPending;
        static int generation;
        static FileSystemWatcher recentWatcher;
        static readonly object gate = new object();
        static readonly Queue<string> recentQueue = new Queue<string>();
        static HashSet<string> officeOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> officeProc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Non-Office files opened: path -> seen in a window title yet?
        static readonly Dictionary<string, bool> otherOpen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, DateTime> otherOpenedAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public static void Start()
        {
            if (running || !Store.Settings.ActivityOn) return;
            if (worker != null) { startPending = true; return; }
            startPending = false;
            running = true;
            try
            {
                string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                recentWatcher = new FileSystemWatcher(recent, "*.lnk") { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, IncludeSubdirectories = false };
                FileSystemEventHandler h = (s, e) => { lock (gate) { if (!recentQueue.Contains(e.FullPath)) recentQueue.Enqueue(e.FullPath); } };
                recentWatcher.Created += h;
                recentWatcher.Changed += h;
                recentWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { App.Log("Recent watcher failed: " + ex.Message); }
            worker = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Activity" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        public static void Stop()
        {
            startPending = false;
            running = false;
            generation++;
            if (recentWatcher != null) { recentWatcher.EnableRaisingEvents = false; recentWatcher.Dispose(); recentWatcher = null; }
        }

        static void Loop()
        {
            try
            {
            bool first = true;
            while (running)
            {
                try
                {
                    PollOffice(first);
                    PollRecent();
                    PollTitles();
                }
                catch (Exception ex) { App.Log("Activity: " + ex.Message); }
                first = false;
                for (int i = 0; i < 30 && running; i++) Thread.Sleep(100); // every 3 s
            }
            }
            finally
            {
                try { App.I.Sync.BeginInvoke(new Action(() =>
                {
                    worker = null;
                    officeOpen.Clear(); officeProc.Clear(); otherOpen.Clear(); otherOpenedAt.Clear();
                    lock (gate) recentQueue.Clear();
                    if (startPending && Store.Settings.ActivityOn) Start();
                })); } catch { }
            }
        }

        // ---------- Office through COM ----------

        static void PollOffice(bool first)
        {
            var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses()) { using (p) { try { names.Add(p.ProcessName); } catch { } } }
            if (names.Contains("EXCEL")) ReadOffice("Excel.Application", "Workbooks", "Excel", now);
            if (names.Contains("WINWORD")) ReadOffice("Word.Application", "Documents", "Word", now);
            if (names.Contains("POWERPNT")) ReadOffice("PowerPoint.Application", "Presentations", "PowerPoint", now);
            if (now.Count == 0 && officeOpen.Count == 0) return;
            foreach (var f in now.Where(f => !officeOpen.Contains(f))) Add(officeProc[f], f, "Opened");
            if (!first || officeOpen.Count > 0)
                foreach (var f in officeOpen.Where(f => !now.Contains(f)).ToList())
                {
                    // A COM read can fail while Office is busy; only call it closed if the app is gone or the read worked.
                    string proc; officeProc.TryGetValue(f, out proc);
                    Add(proc ?? "Office", f, "Closed");
                }
            officeOpen = now;
            foreach (var path in officeProc.Keys.Where(p => !now.Contains(p)).ToList()) officeProc.Remove(path);
        }

        static void ReadOffice(string progId, string collection, string label, HashSet<string> into)
        {
            object app = null, coll = null;
            try
            {
                app = Marshal.GetActiveObject(progId);
                coll = app.GetType().InvokeMember(collection, System.Reflection.BindingFlags.GetProperty, null, app, null);
                int count = (int)coll.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, coll, null);
                for (int i = 1; i <= count; i++)
                {
                    object item = null;
                    try
                    {
                        // Item is a parameterized property in the Office object models.
                        item = coll.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, coll, new object[] { i });
                        string full = (string)item.GetType().InvokeMember("FullName", System.Reflection.BindingFlags.GetProperty, null, item, null);
                        if (!string.IsNullOrEmpty(full) && (full.Contains("\\") || full.Contains("/")))
                        {
                            into.Add(full);
                            officeProc[full] = label;
                        }
                    }
                    finally { if (item != null) Marshal.ReleaseComObject(item); }
                }
            }
            catch (Exception ex)
            {
                // Reflection wraps COM errors. A failed read is not evidence of closure.
                foreach (var kv in officeProc.Where(kv => kv.Value == label && officeOpen.Contains(kv.Key))) into.Add(kv.Key);
                LogOnce(label + " read: " + (ex.InnerException ?? ex).Message);
            }
            finally
            {
                if (coll != null) Marshal.ReleaseComObject(coll);
                if (app != null) Marshal.ReleaseComObject(app);
            }
        }

        static readonly HashSet<string> logged = new HashSet<string>();
        static void LogOnce(string msg) { if (logged.Count < 128 && logged.Add(msg)) App.Log("Activity " + msg); }

        // ---------- Other apps through Recent items ----------

        static void PollRecent()
        {
            var items = new List<string>();
            lock (gate) { while (recentQueue.Count > 0) items.Add(recentQueue.Dequeue()); }
            foreach (var lnk in items.Distinct())
            {
                string target = ShortcutTarget(lnk);
                if (string.IsNullOrEmpty(target) || Directory.Exists(target)) continue;
                string kind = KindOf(target);
                if (kind == "Excel" || kind == "Word" || kind == "PowerPoint")
                {
                    if (officeOpen.Contains(target)) continue; // COM already reported it
                }
                // Same file reported again within a minute -> ignore.
                DateTime t;
                if (otherOpenedAt.TryGetValue(target, out t) && (DateTime.Now - t).TotalSeconds < 60) continue;
                otherOpenedAt[target] = DateTime.Now;
                otherOpen[target] = false;
                Add(kind, target, "Opened");
            }
            foreach (var path in otherOpenedAt.Keys.Where(p => !otherOpen.ContainsKey(p) && (DateTime.Now - otherOpenedAt[p]).TotalMinutes > 5).ToList()) otherOpenedAt.Remove(path);
        }

        static void PollTitles()
        {
            if (otherOpen.Count == 0) return;
            var titles = WindowTitles();
            foreach (var path in otherOpen.Keys.ToList())
            {
                string name = Path.GetFileName(path), stem = Path.GetFileNameWithoutExtension(path);
                bool visible = titles.Any(t => t.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 || (stem.Length > 3 && t.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0));
                if (visible) otherOpen[path] = true;
                else if (otherOpen[path])
                {
                    otherOpen.Remove(path);
                    Add(KindOf(path), path, "Closed");
                }
                else if ((DateTime.Now - otherOpenedAt[path]).TotalMinutes > 2) otherOpen.Remove(path); // never had its own window title
            }
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

        static List<string> WindowTitles()
        {
            var list = new List<string>();
            var sb = new StringBuilder(512);
            EnumWindows((h, l) =>
            {
                if (IsWindowVisible(h)) { sb.Length = 0; if (GetWindowText(h, sb, 512) > 0) list.Add(sb.ToString()); }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static string ShortcutTarget(string lnk)
        {
            for (int i = 0; i < 3; i++)
            {
                object shell = null, sc = null;
                try
                {
                    if (!File.Exists(lnk)) return null;
                    var t = Type.GetTypeFromProgID("WScript.Shell");
                    shell = Activator.CreateInstance(t);
                    sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    string target = (string)sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                    return target;
                }
                catch { Thread.Sleep(150); }
                finally { if (sc != null) Marshal.ReleaseComObject(sc); if (shell != null) Marshal.ReleaseComObject(shell); }
            }
            return null;
        }

        public static string KindOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".xls": case ".xlsx": case ".xlsm": case ".xlsb": case ".csv": return "Excel";
                case ".doc": case ".docx": case ".docm": case ".rtf": return "Word";
                case ".ppt": case ".pptx": case ".pptm": return "PowerPoint";
                case ".pdf": return "PDF Reader";
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".webp": case ".tif": case ".tiff": case ".heic": return "Image Viewer";
                case ".txt": case ".md": case ".log": case ".json": case ".xml": case ".ini": case ".cs": case ".py": case ".js": case ".ps1": case ".ahk": case ".html": case ".css": case ".sql": case ".yaml": case ".yml": return "Text Editor";
                case ".mp4": case ".mkv": case ".avi": case ".mov": case ".mp3": case ".wav": return "Media Player";
                case ".zip": case ".7z": case ".rar": return "Archive";
                case ".dwg": case ".dxf": return "AutoCAD";
                case ".msg": case ".eml": return "Outlook";
            }
            return "Other";
        }

        static void Add(string process, string path, string ev)
        {
            if (!running) return;
            int run = generation;
            long size = -1;
            if (ev == "Opened") { try { var fi = new FileInfo(path); if (fi.Exists) size = fi.Length; } catch { } }
            var rec = new ActivityRecord { Id = Store.NewId(), Process = process, Path = path, Event = ev, Time = Store.Now(), Size = size };
            App.I.Sync.BeginInvoke(new Action(() =>
            {
                if (!running || run != generation) return;
                rec.Fav = Store.Activity.Any(a => a.Fav && string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
                Store.Activity.Insert(0, rec);
                if (Store.Activity.Count > 20000)
                {
                    for (int i = Store.Activity.Count - 1; i >= 0 && Store.Activity.Count > 18000; i--)
                        if (!Store.Activity[i].Fav) Store.Activity.RemoveAt(i);
                }
                Store.MarkActivity();
                Store.RaiseActivity();
                if (ev == "Opened" && rec.Fav && Store.Settings.ActivityAutoBackupFav) Backup(rec, null);
            }));
        }

        // Copies the file to data\backups\<date>\<time>_<name>. Protected files stay protected (byte copy).
        public static string Backup(ActivityRecord rec, Action<string> done)
        {
            string path = rec.Path;
            if (!File.Exists(path)) { if (done != null) done("File not found: " + path); return null; }
            string dir = Path.Combine(Store.Dir, "backups", DateTime.Now.ToString("yyyy-MM-dd"));
            string dest = Path.Combine(dir, DateTime.Now.ToString("HHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + "_" + Path.GetFileName(path));
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                try { Directory.CreateDirectory(dir); File.Copy(path, dest, false); }
                catch (Exception ex) { err = ex.Message; }
                try { App.I.Sync.BeginInvoke(new Action(() =>
                {
                    if (err == null)
                    {
                        foreach (var a in Store.Activity.Where(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase))) { a.Backup = dest; a.BackupTime = Store.Now(); }
                        Store.MarkActivity();
                        Store.RaiseActivity();
                    }
                    if (done != null) done(err == null ? "Backup saved: " + dest : "Backup failed: " + err);
                })); } catch { }
            });
            return dest;
        }
    }

    class ActivityPanel : LivePanel
    {
        readonly FastList lv;
        readonly TextBox search;
        readonly ComboBox typeFilter, timeFilter;
        readonly CheckBox favOnly;
        readonly Label count;
        readonly Action<string> status;
        readonly Action onChange;
        List<ActivityRecord> rows = new List<ActivityRecord>();
        bool stale = true;
        int sortCol = 5; bool sortDesc = true;

        public ActivityPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;
            lv = new FastList { Dock = DockStyle.Fill, VirtualMode = true, MultiSelect = true };
            lv.Columns.Add("★", Ui.S(32), HorizontalAlignment.Center);
            lv.Columns.Add("Process", Ui.S(110));
            lv.Columns.Add("File name", Ui.S(240));
            lv.Columns.Add("Folder", Ui.S(250));
            lv.Columns.Add("Event", Ui.S(70));
            lv.Columns.Add("Time", Ui.S(140));
            lv.Columns.Add("Size", Ui.S(80), HorizontalAlignment.Right);
            lv.Columns.Add("Backup", Ui.S(130));
            lv.RetrieveVirtualItem += Retrieve;
            lv.ColumnClick += (s, e) => { if (sortCol == e.Column) sortDesc = !sortDesc; else { sortCol = e.Column; sortDesc = e.Column == 5 || e.Column == 6; } Fill(); };
            lv.MouseClick += (s, e) =>
            {
                var hit = lv.HitTest(e.Location);
                if (hit.Item == null || e.Button != MouseButtons.Left) return;
                int col = hit.Item.SubItems.IndexOf(hit.SubItem);
                var rec = rows[hit.Item.Index];
                if (col == 0) ToggleFav(new List<ActivityRecord> { rec });
                else if (col == 7) BackupRecs(new List<ActivityRecord> { rec });
            };
            lv.DoubleClick += delegate { var r = Sel().FirstOrDefault(); if (r != null) OpenFile(r.Path); };
            lv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) { DeleteSel(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { var r = Sel().FirstOrDefault(); if (r != null) OpenFile(r.Path); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.A) { lv.BeginUpdate(); for (int i = 0; i < rows.Count; i++) lv.SelectedIndices.Add(i); lv.EndUpdate(); e.SuppressKeyPress = true; }
            };
            var m = Ui.Menu();
            m.Items.Add("Open file", null, delegate { var r = Sel().FirstOrDefault(); if (r != null) OpenFile(r.Path); });
            m.Items.Add("Open folder", null, delegate { OpenDir(); });
            m.Items.Add("Back up now", null, delegate { BackupRecs(Sel()); });
            m.Items.Add("Open the backup", null, delegate { var r = Sel().FirstOrDefault(x => x.Backup != null); if (r != null && File.Exists(r.Backup)) try { Process.Start("explorer.exe", "/select,\"" + r.Backup + "\""); } catch { } });
            m.Items.Add("Favorite on/off", null, delegate { ToggleFav(Sel()); });
            m.Items.Add("Tag file...", null, delegate { var r = Sel(); if (r.Count > 0) TaggedPanel.AskTag(r.Select(x => x.Path).Distinct().ToList(), FindForm()); });
            m.Items.Add("Copy path", null, delegate { var r = Sel(); if (r.Count > 0) App.I.CopyText(string.Join("\r\n", r.Select(x => x.Path).Distinct())); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete records", null, delegate { DeleteSel(); });
            Ui.ThemeMenu(m);
            lv.ContextMenuStrip = m;
            Controls.Add(lv);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 0, 0, Ui.S(4)) };
            search = Ui.Search("Search file, folder, app");
            search.Width = Ui.S(200);
            search.Margin = new Padding(0, Ui.S(4), Ui.S(6), 0);
            search.TextChanged += delegate { Fill(); };
            bar.Controls.Add(search);
            typeFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(120), Margin = new Padding(0, Ui.S(4), Ui.S(6), 0) };
            typeFilter.Items.AddRange(new object[] { "All types", "Excel", "Word", "PowerPoint", "PDF Reader", "Image Viewer", "Text Editor", "Media Player", "Other" });
            typeFilter.SelectedIndex = 0;
            typeFilter.SelectedIndexChanged += delegate { Fill(); };
            bar.Controls.Add(typeFilter);
            timeFilter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(110), Margin = new Padding(0, Ui.S(4), Ui.S(6), 0) };
            timeFilter.Items.AddRange(new object[] { "All time", "Today", "Last 7 days", "Last 30 days" });
            timeFilter.SelectedIndex = 0;
            timeFilter.SelectedIndexChanged += delegate { Fill(); };
            bar.Controls.Add(timeFilter);
            favOnly = new CheckBox { Text = "★ Favorites", AutoSize = true, Margin = new Padding(0, Ui.S(7), Ui.S(6), 0) };
            favOnly.CheckedChanged += delegate { Fill(); };
            bar.Controls.Add(favOnly);
            bar.Controls.Add(Ui.Btn("Backup", delegate { BackupRecs(Sel()); }, true));
            bar.Controls.Add(Ui.Btn("Open dir", delegate { OpenDir(); }));
            bar.Controls.Add(Ui.Btn("Delete", delegate { DeleteSel(); }));
            bar.Controls.Add(Ui.Btn("Clear", delegate { ClearAll(); }));
            var data = Ui.Btn("Data", null);
            data.Click += delegate
            {
                var dm = Ui.Menu();
                dm.Items.Add("Export to CSV (Excel)...", null, delegate { Export(); });
                dm.Items.Add("Open backups folder", null, delegate { string d = Path.Combine(Store.Dir, "backups"); Directory.CreateDirectory(d); try { Process.Start("explorer.exe", "\"" + d + "\""); } catch { } });
                var on = new ToolStripMenuItem("Logging on") { Checked = Store.Settings.ActivityOn };
                on.Click += delegate { Store.Settings.ActivityOn = !Store.Settings.ActivityOn; Store.MarkSettings(); if (Store.Settings.ActivityOn) ActivityMonitor.Start(); else ActivityMonitor.Stop(); };
                dm.Items.Add(on);
                var ab = new ToolStripMenuItem("Auto-backup favorites when opened") { Checked = Store.Settings.ActivityAutoBackupFav };
                ab.Click += delegate { Store.Settings.ActivityAutoBackupFav = !Store.Settings.ActivityAutoBackupFav; Store.MarkSettings(); };
                dm.Items.Add(ab);
                Ui.ThemeMenu(dm);
                dm.Show(data, new Point(0, data.Height));
            };
            bar.Controls.Add(data);
            count = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(6), Ui.S(9), 0, 0) };
            bar.Controls.Add(count);
            Controls.Add(bar);

            onChange = () => { if (Live) Fill(); else stale = true; };
            Store.ActivityChanged += onChange;
            Ui.Theme(this);
        }

        protected override void OnLive(bool on) { if (on && stale) Fill(); }
        protected override void Dispose(bool disposing) { if (disposing) Store.ActivityChanged -= onChange; base.Dispose(disposing); }

        void Fill()
        {
            stale = false;
            string q = search.Text.Trim();
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string type = typeFilter.SelectedIndex > 0 ? (string)typeFilter.SelectedItem : null;
            long since = timeFilter.SelectedIndex == 1 ? DateTime.Today.Ticks : timeFilter.SelectedIndex == 2 ? DateTime.Today.AddDays(-7).Ticks : timeFilter.SelectedIndex == 3 ? DateTime.Today.AddDays(-30).Ticks : 0;
            IEnumerable<ActivityRecord> src = Store.Activity.Where(a =>
                a.Time >= since && (type == null || a.Process == type) && (!favOnly.Checked || a.Fav) &&
                words.All(w => a.Path.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || a.Process.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            Func<ActivityRecord, IComparable> key;
            switch (sortCol)
            {
                case 0: key = a => a.Fav ? 1 : 0; break;
                case 1: key = a => a.Process; break;
                case 2: key = a => Path.GetFileName(a.Path).ToLowerInvariant(); break;
                case 3: key = a => (Path.GetDirectoryName(a.Path) ?? "").ToLowerInvariant(); break;
                case 4: key = a => a.Event; break;
                case 6: key = a => a.Size; break;
                case 7: key = a => a.BackupTime; break;
                default: key = a => a.Time; break;
            }
            var keep = new HashSet<ActivityRecord>(Sel());
            rows = (sortDesc ? src.OrderByDescending(key) : src.OrderBy(key)).ToList();
            lv.BeginUpdate();
            lv.VirtualListSize = rows.Count;
            lv.SelectedIndices.Clear();
            if (keep.Count > 0) for (int i = 0; i < rows.Count; i++) if (keep.Contains(rows[i])) lv.SelectedIndices.Add(i);
            lv.EndUpdate();
            lv.Invalidate();
            count.Text = rows.Count.ToString("N0") + " records" + (Store.Settings.ActivityOn ? "" : "  (logging off)");
        }

        void Retrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex >= rows.Count) { e.Item = new ListViewItem(""); for (int i = 0; i < 7; i++) e.Item.SubItems.Add(""); return; }
            var a = rows[e.ItemIndex];
            var it = new ListViewItem(a.Fav ? "★" : "☆");
            it.UseItemStyleForSubItems = false;
            it.SubItems.Add(a.Process);
            it.SubItems.Add(Path.GetFileName(a.Path), Ui.Text, Ui.Card, Ui.Bold);
            it.SubItems.Add(Path.GetDirectoryName(a.Path) ?? "");
            it.SubItems.Add(a.Event, a.Event == "Opened" ? Color.FromArgb(40, 160, 90) : Ui.Muted, Ui.Card, Ui.Bold);
            it.SubItems.Add(a.Time > 0 && a.Time <= DateTime.MaxValue.Ticks ? new DateTime(a.Time).ToString("yyyy-MM-dd HH:mm:ss") : "");
            it.SubItems.Add(a.Size >= 0 ? Fmt.Bytes(a.Size) : "-");
            if (a.Backup != null) it.SubItems.Add("✓ " + Ui.Ago(a.BackupTime), Color.FromArgb(40, 160, 90), Ui.Card, Ui.Base);
            else it.SubItems.Add("⚠ SAVE!", Color.FromArgb(230, 150, 20), Ui.Card, Ui.Bold);
            if (a.Fav) it.SubItems[0].ForeColor = Color.FromArgb(240, 180, 0);
            foreach (ListViewItem.ListViewSubItem si in it.SubItems) if (si.BackColor != Ui.Card) si.BackColor = Ui.Card;
            e.Item = it;
        }

        List<ActivityRecord> Sel()
        {
            var r = new List<ActivityRecord>();
            foreach (int i in lv.SelectedIndices) if (i >= 0 && i < rows.Count) r.Add(rows[i]);
            return r;
        }

        void ToggleFav(List<ActivityRecord> recs)
        {
            if (recs.Count == 0) return;
            bool on = recs.Any(r => !r.Fav);
            var paths = new HashSet<string>(recs.Select(r => r.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var a in Store.Activity.Where(a => paths.Contains(a.Path))) a.Fav = on;
            Store.MarkActivity();
            Fill();
        }

        void BackupRecs(List<ActivityRecord> recs)
        {
            if (recs.Count == 0) { status("Select the files to back up first."); return; }
            foreach (var r in recs.GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                ActivityMonitor.Backup(r, s => status(s));
        }

        static void OpenFile(string p)
        {
            try { Process.Start(p); } catch (Exception ex) { MessageBox.Show("Could not open: " + ex.Message, App.Name); }
        }

        void OpenDir()
        {
            var r = Sel().FirstOrDefault();
            if (r == null) return;
            try
            {
                if (File.Exists(r.Path)) Process.Start("explorer.exe", "/select,\"" + r.Path + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(r.Path) + "\"");
            }
            catch { }
        }

        void DeleteSel()
        {
            var s = Sel();
            if (s.Count == 0) return;
            var set = new HashSet<ActivityRecord>(s);
            Store.Activity.RemoveAll(set.Contains);
            Store.MarkActivity();
            Store.RaiseActivity();
            status(s.Count + " record(s) deleted. Your files were not touched.");
        }

        void ClearAll()
        {
            int n = Store.Activity.Count(a => !a.Fav);
            if (n == 0) return;
            if (MessageBox.Show(FindForm(), "Clear " + n + " records? Favorites stay. Your files are not touched.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Store.Activity.RemoveAll(a => !a.Fav);
            Store.MarkActivity();
            Store.RaiseActivity();
        }

        void Export()
        {
            using (var d = new SaveFileDialog { Filter = "CSV (opens in Excel)|*.csv", FileName = "file-activity-" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                var sb = new StringBuilder("Favorite,Process,File name,Folder,Event,Time,Size bytes,Backup\r\n");
                foreach (var a in rows)
                    sb.AppendLine(string.Join(",", new[] { a.Fav ? "yes" : "", a.Process, Path.GetFileName(a.Path), Path.GetDirectoryName(a.Path) ?? "", a.Event, new DateTime(a.Time).ToString("yyyy-MM-dd HH:mm:ss"), a.Size.ToString(), a.Backup ?? "" }.Select(Csv)));
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                status("Exported " + rows.Count + " records.");
            }
        }

        static string Csv(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }
    }
}
