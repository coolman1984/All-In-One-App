using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    class TaggedPanel : LivePanel
    {
        readonly FastList lv;
        readonly TextBox search;
        readonly FlowLayoutPanel moveBar;
        readonly Label countLabel;
        readonly Action<string> status;
        readonly Action onChange;
        bool stale = true, filling;
        string categoryKey = "";
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 250 };

        // Per-path file metadata, resolved on a worker thread so Fill never touches the disk on the UI thread.
        class Meta { public bool Exists; public long Size = -1; public DateTime Mod = DateTime.MinValue; public int Icon = -1; }
        readonly Dictionary<string, Meta> meta = new Dictionary<string, Meta>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, bool> pendingMeta = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        readonly object metaLock = new object();
        bool metaRunning;

        // What the right-click menu acts on (set each time the menu opens).
        List<TaggedFile> menuTargets;
        List<TaggedFile> MenuTargets() { return menuTargets ?? Targets(); }

        public TaggedPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;
            AllowDrop = true;

            lv = new FastList { Dock = DockStyle.Fill, CheckBoxes = true, MultiSelect = true, ShowGroups = true, AllowDrop = true };
            lv.SmallImageList = ProcMeta.Icons;
            lv.Columns.Add("Name", Ui.S(260));
            lv.Columns.Add("Location", Ui.S(380));
            lv.Columns.Add("Size", Ui.S(80), HorizontalAlignment.Right);
            lv.Columns.Add("Modified", Ui.S(120));
            lv.Columns.Add("Tagged", Ui.S(120));
            lv.Columns.Add("Note", Ui.S(200));
            lv.FillIndex = 1;
            lv.DoubleClick += delegate { OpenSel(); };
            lv.ItemChecked += delegate { if (!filling) UpdateCount(); };
            lv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { OpenSel(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { RemoveChecked(true); e.SuppressKeyPress = true; }
            };
            lv.DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; };
            lv.DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                // Drop onto a group's item -> that category; otherwise ask.
                var pt = lv.PointToClient(new Point(e.X, e.Y));
                var hit = lv.HitTest(pt);
                string cat = hit.Item != null && hit.Item.Group != null ? (string)hit.Item.Group.Tag : null;
                if (cat != null) AddPaths(files, cat); else AskTag(files.ToList(), FindForm());
            };
            var m = Ui.Menu();
            m.Items.Add("Open", null, delegate { OpenSel(); });
            m.Items.Add("Open folder", null, delegate { OpenFolderSel(); });
            Term.AddMenu(m.Items, () => { var t = MenuTargets().FirstOrDefault(); return t == null ? null : t.Path; });
            var copyItem = m.Items.Add("Copy path", null, delegate { var t = MenuTargets(); if (t.Count > 0) App.I.CopyText(string.Join("\r\n", t.Select(x => x.Path))); });
            m.Items.Add(AppLinks.LazySendTo(() => { var t = MenuTargets(); return t.Count > 0 ? t[0].Path : null; }, null, FindForm()));
            m.Items.Add("Note...", null, delegate
            {
                var t = MenuTargets().FirstOrDefault(); if (t == null) return;
                var n = Ui.Ask("Note", "A short note for " + Path.GetFileName(t.Path) + ":", t.Note, FindForm());
                if (n != null) { t.Note = n; Store.MarkTagged(); Store.RaiseTagged(); }
            });
            var removeItem = m.Items.Add("Remove from list", null, delegate { RemoveList(MenuTargets()); });
            Ui.ThemeMenu(m);
            int baseCount = m.Items.Count; // everything after this is rebuilt on each Opening
            m.Opening += delegate
            {
                // Act on the right-clicked row unless it is one of the ticked rows.
                ListViewItem hit = null;
                try { var pt = lv.PointToClient(Cursor.Position); if (lv.ClientRectangle.Contains(pt)) hit = lv.HitTest(pt).Item; } catch { }
                var ticked = lv.CheckedItems.Cast<ListViewItem>().Select(i => (TaggedFile)i.Tag).ToList();
                bool tickedMode = false;
                if (hit != null && !hit.Checked)
                {
                    menuTargets = new List<TaggedFile> { (TaggedFile)hit.Tag };
                    if (!hit.Selected) { lv.SelectedItems.Clear(); hit.Selected = true; }
                }
                else if (ticked.Count > 0) { menuTargets = ticked; tickedMode = true; }
                else menuTargets = lv.SelectedItems.Cast<ListViewItem>().Select(i => (TaggedFile)i.Tag).ToList();
                copyItem.Text = tickedMode ? "Copy path (" + ticked.Count + " ticked)" : "Copy path";
                removeItem.Text = tickedMode ? "Remove " + ticked.Count + " ticked" : menuTargets.Count > 1 ? "Remove " + menuTargets.Count + " selected" : "Remove from list";
                while (m.Items.Count > baseCount) { var item = m.Items[baseCount]; m.Items.RemoveAt(baseCount); Ui.DisposeItem(item); }
                m.Items.Add(new ToolStripSeparator());
                foreach (var c in Store.TagCats) { string name = c.Name; m.Items.Add((tickedMode ? "Move " + ticked.Count + " ticked to " : "Move to ") + name, Ui.Swatch(Ui.LabelColor(c.Color), Ui.S(14)), delegate { MoveTo(name, MenuTargets()); }); }
                Ui.ThemeMenu(m);
            };
            lv.ContextMenuStrip = m;
            Controls.Add(lv);

            moveBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 38), WrapContents = true };
            Controls.Add(moveBar);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 0, 0, Ui.S(4)) };
            search = Ui.Search("Search files");
            search.Width = Ui.S(200);
            search.Margin = new Padding(0, Ui.S(4), Ui.S(6), 0);
            searchTimer.Tick += delegate { searchTimer.Stop(); if (Live) Fill(false); else stale = true; };
            search.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            bar.Controls.Add(search);
            bar.Controls.Add(Ui.Btn("Add file", delegate { AddFileDialog(); }, true));
            bar.Controls.Add(Ui.Btn("Add folder", delegate { AddFolderDialog(); }));
            bar.Controls.Add(Ui.Btn("Select all", delegate { filling = true; bool all = lv.Items.Cast<ListViewItem>().Any(i => !i.Checked); foreach (ListViewItem i in lv.Items) i.Checked = all; filling = false; UpdateCount(); }));
            bar.Controls.Add(Ui.Btn("Delete selected", delegate { RemoveChecked(false); }));
            bar.Controls.Add(Ui.Btn("New category", delegate { NewCategory(); }));
            countLabel = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(6), Ui.S(9), 0, 0) };
            bar.Controls.Add(countLabel);
            Controls.Add(bar);

            var hint = new WrapLabel { Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted, Text = "Drag files or folders here, or select them in Explorer and press " + Hotkeys.Get("tagfile") + ". Tick items, then Move to a category." };
            Controls.Add(hint);

            onChange = () => { if (Live) { RefreshCategories(); Fill(true); } else stale = true; };
            Store.TaggedChanged += onChange;
            Ui.Theme(this);
            BuildMoveBar();
        }

        protected override void OnLive(bool on) { if (on && stale) { RefreshCategories(); Fill(true); } }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Store.TaggedChanged -= onChange; searchTimer.Stop(); searchTimer.Dispose(); }
            base.Dispose(disposing);
        }

        void BuildMoveBar()
        {
            categoryKey = CategoryKey();
            Ui.ClearControls(moveBar.Controls);
            moveBar.Controls.Add(new Label { Text = "Move selected to:", AutoSize = true, Margin = new Padding(0, Ui.S(9), Ui.S(6), 0) });
            foreach (var c in Store.TagCats)
            {
                string name = c.Name;
                var b = Ui.Btn("● " + c.Name, delegate { MoveTo(name); });
                b.ForeColor = Ui.LabelColor(c.Color);
                b.Font = Ui.Bold;
                var cm = Ui.Menu();
                cm.Items.Add("Rename...", null, delegate { RenameCategory(name); });
                cm.Items.Add("Delete category", null, delegate { DeleteCategory(name); });
                Ui.ThemeMenu(cm);
                b.ContextMenuStrip = cm;
                Ui.Theme(b);
                moveBar.Controls.Add(b);
            }
        }

        static string CategoryKey() { return string.Join("\0", Store.TagCats.Select(c => c.Name + "\u0001" + c.Color)); }
        void RefreshCategories() { if (categoryKey != CategoryKey()) BuildMoveBar(); }

        void Fill(bool refreshMeta)
        {
            stale = false;
            filling = true;
            var words = search.Text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var checkedIds = new HashSet<string>(lv.CheckedItems.Cast<ListViewItem>().Select(i => ((TaggedFile)i.Tag).Id));
            var selectedIds = new HashSet<string>(lv.SelectedItems.Cast<ListViewItem>().Select(i => ((TaggedFile)i.Tag).Id));
            var tagged = Store.Tagged.ToList();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in tagged) { int k; string cn = t.Category ?? ""; counts.TryGetValue(cn, out k); counts[cn] = k + 1; }
            var pending = new List<KeyValuePair<string, bool>>();
            if (refreshMeta) foreach (var t in tagged) pending.Add(new KeyValuePair<string, bool>(t.Path, t.IsFolder));
            ListViewGroup firstGroup = null;
            lv.BeginUpdate();
            try
            {
                lv.Items.Clear();
                lv.Groups.Clear();
                var groups = new Dictionary<string, ListViewGroup>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in Store.TagCats)
                {
                    int n; counts.TryGetValue(c.Name ?? "", out n);
                    var g = new ListViewGroup("●  " + c.Name + "   (" + n + ")") { Tag = c.Name };
                    lv.Groups.Add(g);
                    groups[c.Name ?? ""] = g;
                    if (firstGroup == null) firstGroup = g;
                }
                var items = new List<ListViewItem>();
                foreach (var t in tagged.Where(t => words.All(w => (t.Path ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (t.Note ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).OrderByDescending(t => t.Added))
                {
                    if (string.IsNullOrEmpty(t.Path)) continue;
                    Meta mt;
                    lock (metaLock) meta.TryGetValue(t.Path, out mt);
                    if (mt == null && !refreshMeta) pending.Add(new KeyValuePair<string, bool>(t.Path, t.IsFolder));
                    bool exists = mt == null || mt.Exists; // unknown yet: assume present until the worker answers
                    string name = Path.GetFileName(t.Path.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(name)) name = t.Path;
                    var it = new ListViewItem(name) { Tag = t, Checked = checkedIds.Contains(t.Id), Selected = selectedIds.Contains(t.Id) };
                    if (t.IsFolder) it.ImageIndex = ProcMeta.FolderIcon();
                    else if (mt != null && mt.Exists)
                    {
                        if (mt.Icon < 0) mt.Icon = ProcMeta.IconFor(t.Path);
                        it.ImageIndex = mt.Icon;
                    }
                    it.SubItems.Add(t.Path);
                    it.SubItems.Add(mt != null && mt.Size >= 0 ? Fmt.Bytes(mt.Size) : "");
                    it.SubItems.Add(mt == null ? "" : mt.Mod == DateTime.MinValue ? (mt.Exists ? "" : "not found") : mt.Mod.ToString("yyyy-MM-dd HH:mm"));
                    it.SubItems.Add(Ui.Ago(t.Added));
                    it.SubItems.Add(t.Note ?? "");
                    if (!exists) it.ForeColor = Ui.Muted;
                    ListViewGroup g;
                    if (groups.TryGetValue(t.Category ?? "", out g) || (g = firstGroup) != null) it.Group = g;
                    items.Add(it);
                }
                lv.Items.AddRange(items.ToArray());
            }
            finally
            {
                lv.EndUpdate();
                filling = false;
            }
            UpdateCount();
            if (pending.Count > 0) QueueMeta(pending);
        }

        // Resolves exists / size / modified for the given paths off the UI thread, then refills once.
        void QueueMeta(List<KeyValuePair<string, bool>> items)
        {
            lock (metaLock)
            {
                foreach (var kv in items) pendingMeta[kv.Key] = kv.Value;
                if (metaRunning) return;
                metaRunning = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    while (true)
                    {
                        List<KeyValuePair<string, bool>> batch;
                        lock (metaLock)
                        {
                            if (pendingMeta.Count == 0) { metaRunning = false; break; }
                            batch = pendingMeta.ToList();
                            pendingMeta.Clear();
                        }
                        foreach (var kv in batch)
                        {
                            var m = new Meta();
                            try
                            {
                                if (kv.Value) { m.Exists = Directory.Exists(kv.Key); if (m.Exists) m.Mod = Directory.GetLastWriteTime(kv.Key); }
                                else
                                {
                                    var fi = new FileInfo(kv.Key);
                                    m.Exists = fi.Exists;
                                    if (m.Exists) { m.Size = fi.Length; m.Mod = fi.LastWriteTime; }
                                }
                            }
                            catch { }
                            lock (metaLock) meta[kv.Key] = m;
                        }
                    }
                }
                catch { lock (metaLock) metaRunning = false; }
                UI(() => { if (Live) Fill(false); else stale = true; });
            });
        }

        void UpdateCount()
        {
            countLabel.Text = Store.Tagged.Count + " tagged" + (lv.CheckedItems.Count > 0 ? ", " + lv.CheckedItems.Count + " selected" : "");
        }

        // Checked items, or the highlighted ones when nothing is ticked.
        List<TaggedFile> Targets()
        {
            var t = lv.CheckedItems.Cast<ListViewItem>().Select(i => (TaggedFile)i.Tag).ToList();
            if (t.Count == 0) t = lv.SelectedItems.Cast<ListViewItem>().Select(i => (TaggedFile)i.Tag).ToList();
            return t;
        }

        void OpenSel()
        {
            var t = lv.SelectedItems.Cast<ListViewItem>().Select(i => (TaggedFile)i.Tag).FirstOrDefault();
            if (t == null) return;
            try { Process.Start(t.IsFolder ? "explorer.exe" : t.Path, t.IsFolder ? "\"" + t.Path + "\"" : null); }
            catch (Exception ex) { status("Could not open: " + ex.Message); }
        }

        void OpenFolderSel()
        {
            var t = MenuTargets().FirstOrDefault();
            if (t == null) return;
            try
            {
                if (t.IsFolder) Process.Start("explorer.exe", "\"" + t.Path + "\"");
                else Process.Start("explorer.exe", "/select,\"" + t.Path + "\"");
            }
            catch { }
        }

        void MoveTo(string cat) { MoveTo(cat, Targets()); }

        void MoveTo(string cat, List<TaggedFile> t)
        {
            if (t == null || t.Count == 0) { status("Tick the files to move first."); return; }
            foreach (var x in t) x.Category = cat;
            Store.MarkTagged();
            var moved = new HashSet<TaggedFile>(t);
            filling = true; foreach (ListViewItem i in lv.Items) if (moved.Contains((TaggedFile)i.Tag)) i.Checked = false; filling = false;
            Store.RaiseTagged();
            status(t.Count + " moved to " + cat + ".");
        }

        void RemoveChecked(bool selectedToo)
        {
            var t = lv.CheckedItems.Cast<ListViewItem>().Select(i => (TaggedFile)i.Tag).ToList();
            if (t.Count == 0 && selectedToo) t = lv.SelectedItems.Cast<ListViewItem>().Select(i => (TaggedFile)i.Tag).ToList();
            RemoveList(t);
        }

        void RemoveList(List<TaggedFile> t)
        {
            if (t == null || t.Count == 0) { status("Tick the items to remove first."); return; }
            if (MessageBox.Show(FindForm(), "Remove " + t.Count + " item(s) from the tagged list?\nThe files themselves are NOT deleted.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var set = new HashSet<TaggedFile>(t);
            Store.Tagged.RemoveAll(set.Contains);
            Store.MarkTagged();
            Store.RaiseTagged();
        }

        void AddFileDialog()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "Tag files" })
                if (d.ShowDialog(FindForm()) == DialogResult.OK) AskTag(d.FileNames.ToList(), FindForm());
        }

        void AddFolderDialog()
        {
            using (var d = new FolderBrowserDialog { Description = "Tag a folder" })
                if (d.ShowDialog(FindForm()) == DialogResult.OK) AskTag(new List<string> { d.SelectedPath }, FindForm());
        }

        public static void AddPaths(IEnumerable<string> paths, string cat)
        {
            int n = 0;
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var existing = Store.Tagged.FirstOrDefault(t => string.Equals(t.Path, p, StringComparison.OrdinalIgnoreCase));
                if (existing != null) { existing.Category = cat; n++; continue; }
                Store.Tagged.Add(new TaggedFile { Id = Store.NewId(), Path = p, IsFolder = Directory.Exists(p), Category = cat, Added = Store.Now() });
                n++;
            }
            Store.MarkTagged();
            Store.RaiseTagged();
            if (n > 0) App.I.Balloon("Tagged", n + " item(s) added to " + cat + ".");
        }

        // Small menu at the mouse to pick the category.
        public static void AskTag(List<string> paths, IWin32Window owner)
        {
            if (paths == null || paths.Count == 0) return;
            var m = Ui.Menu();
            m.Items.Add(new ToolStripLabel("Tag " + (paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\')) : paths.Count + " items") + " as:") { Font = Ui.Bold });
            foreach (var c in Store.TagCats)
            {
                string name = c.Name;
                m.Items.Add(c.Name, Ui.Swatch(Ui.LabelColor(c.Color), Ui.S(16)), delegate { AddPaths(paths, name); });
            }
            Ui.ThemeMenu(m);
            m.Closed += delegate { Ui.Later(300, () => m.Dispose()); };
            // From the global hotkey no window of ours is active, so the menu would not close on a click elsewhere.
            if (owner == null)
            {
                try { var sync = App.I == null ? null : App.I.Sync; if (sync != null && sync.IsHandleCreated) Native.SetForegroundWindow(sync.Handle); } catch { }
            }
            m.Show(Cursor.Position);
        }

        void NewCategory()
        {
            var name = Ui.Ask("New category", "Category name:", "", FindForm());
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (Store.TagCats.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            var colors = Ui.LabelNames;
            string color = colors.FirstOrDefault(c => !Store.TagCats.Any(t => t.Color == c)) ?? "Blue";
            Store.TagCats.Add(new TagCategory { Name = name, Color = color });
            Store.MarkTagged();
            BuildMoveBar();
            Store.RaiseTagged();
        }

        void RenameCategory(string old)
        {
            var name = Ui.Ask("Rename category", "New name:", old, FindForm());
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (Store.TagCats.Any(c => !string.Equals(c.Name, old, StringComparison.OrdinalIgnoreCase) && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) { status("That category already exists."); return; }
            foreach (var c in Store.TagCats.Where(c => c.Name == old)) c.Name = name;
            foreach (var t in Store.Tagged.Where(t => t.Category == old)) t.Category = name;
            Store.MarkTagged();
            BuildMoveBar();
            Store.RaiseTagged();
        }

        void DeleteCategory(string name)
        {
            if (Store.TagCats.Count <= 1) return;
            int n = Store.Tagged.Count(t => t.Category == name);
            if (MessageBox.Show(FindForm(), "Delete category \"" + name + "\"?" + (n > 0 ? "\nIts " + n + " item(s) move to " + Store.TagCats.First(c => c.Name != name).Name + "." : ""), App.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            string to = Store.TagCats.First(c => c.Name != name).Name;
            foreach (var t in Store.Tagged.Where(t => t.Category == name)) t.Category = to;
            Store.TagCats.RemoveAll(c => c.Name == name);
            Store.MarkTagged();
            BuildMoveBar();
            Store.RaiseTagged();
        }
    }

    // Reads the files selected in the active Explorer window.
    static class ExplorerSelection
    {
        public static List<string> Get(IntPtr foreground)
        {
            var result = new List<string>();
            object shell = null, windows = null;
            try
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                shell = Activator.CreateInstance(t);
                windows = t.InvokeMember("Windows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                int count = (int)windows.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, windows, null);
                for (int i = 0; i < count; i++)
                {
                    object w = null, doc = null, items = null, folder = null, self = null;
                    try
                    {
                        w = windows.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, windows, new object[] { i });
                        if (w == null) continue;
                        long hwnd = Convert.ToInt64(w.GetType().InvokeMember("HWND", System.Reflection.BindingFlags.GetProperty, null, w, null));
                        if (hwnd != foreground.ToInt64()) continue;
                        doc = w.GetType().InvokeMember("Document", System.Reflection.BindingFlags.GetProperty, null, w, null);
                        items = doc.GetType().InvokeMember("SelectedItems", System.Reflection.BindingFlags.InvokeMethod, null, doc, null);
                        int n = (int)items.GetType().InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, items, null);
                        for (int k = 0; k < n; k++)
                        {
                            object it = null;
                            try { it = items.GetType().InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, items, new object[] { k }); result.Add((string)it.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, it, null)); }
                            finally { if (it != null) Marshal.ReleaseComObject(it); }
                        }
                        if (n == 0)
                        {
                            // Nothing selected: tag the folder that is open.
                            folder = doc.GetType().InvokeMember("Folder", System.Reflection.BindingFlags.GetProperty, null, doc, null);
                            self = folder.GetType().InvokeMember("Self", System.Reflection.BindingFlags.GetProperty, null, folder, null);
                            result.Add((string)self.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, self, null));
                        }
                    }
                    catch { }
                    finally
                    {
                        if (self != null) Marshal.ReleaseComObject(self);
                        if (folder != null) Marshal.ReleaseComObject(folder);
                        if (items != null) Marshal.ReleaseComObject(items);
                        if (doc != null) Marshal.ReleaseComObject(doc);
                        if (w != null) Marshal.ReleaseComObject(w);
                    }
                }
            }
            catch (Exception ex) { App.Log("Explorer selection: " + ex.Message); }
            finally { if (windows != null) Marshal.ReleaseComObject(windows); if (shell != null) Marshal.ReleaseComObject(shell); }
            return result;
        }
    }
}
