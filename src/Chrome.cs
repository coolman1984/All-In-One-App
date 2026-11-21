using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    static class ChromeInfo
    {
        public static string UserData { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data"); } }
        public static string Exe
        {
            get
            {
                foreach (var p in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    string f = Path.Combine(p, @"Google\Chrome\Application\chrome.exe");
                    if (File.Exists(f)) return f;
                }
                return "chrome.exe";
            }
        }
        public static bool Running { get { try { return Process.GetProcessesByName("chrome").Length > 0; } catch { return false; } } }

        // Profile folder -> display name.
        public static List<KeyValuePair<string, string>> Profiles()
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var st = js.DeserializeObject(File.ReadAllText(Path.Combine(UserData, "Local State"))) as Dictionary<string, object>;
                var cache = ((st["profile"] as Dictionary<string, object>)["info_cache"]) as Dictionary<string, object>;
                foreach (var kv in cache)
                {
                    var d = kv.Value as Dictionary<string, object>;
                    string name = d != null && d.ContainsKey("name") ? Convert.ToString(d["name"]) : kv.Key;
                    if (Directory.Exists(Path.Combine(UserData, kv.Key))) list.Add(new KeyValuePair<string, string>(kv.Key, name + "  (" + kv.Key + ")"));
                }
            }
            catch { }
            if (list.Count == 0 && Directory.Exists(Path.Combine(UserData, "Default"))) list.Add(new KeyValuePair<string, string>("Default", "Default"));
            return list.OrderBy(k => k.Key == "Default" ? "" : k.Key).ToList();
        }

        public static void Open(string profile, string url, bool newWindow = false)
        {
            try { Process.Start(new ProcessStartInfo(Exe, "--profile-directory=\"" + profile + "\" " + (newWindow ? "--new-window " : "") + "\"" + url.Replace("\"", "%22") + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { App.Log("Chrome open: " + ex.Message); App.I.Balloon("Could not start Chrome", ex.Message); }
        }
    }

    // ===================== Bookmarks =====================
    class BookmarksPanel : LivePanel
    {
        readonly ComboBox profile;
        readonly TreeView tree;
        readonly FastList lv;
        readonly TextBox search;
        readonly Label info;
        readonly Button btnSave;
        Dictionary<string, object> doc;
        List<Dictionary<string, object>> shown = new List<Dictionary<string, object>>();
        bool dirty, loaded;
        string file;
        int prevProfile = -1, loadSeq;
        bool reverting;
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 250 };
        // Lazy index of the loaded document: node -> parent, plus the flat node list. Invalidated on every mutation.
        Dictionary<Dictionary<string, object>, Dictionary<string, object>> parents;
        List<Dictionary<string, object>> flat;
        readonly Action<string> status;
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };

        public BookmarksPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;
            lv = new FastList { Dock = DockStyle.Fill, MultiSelect = true, AllowDrop = false };
            lv.Columns.Add("Name", Ui.S(320)); lv.Columns.Add("Address", Ui.S(360)); lv.Columns.Add("Folder", Ui.S(160)); lv.Columns.Add("Added", Ui.S(100));
            lv.Weights = new[] { 3, 4, 1, 0 };
            lv.DoubleClick += delegate { OpenSelected(false); };
            lv.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { OpenSelected(false); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.F2) { EditSelected(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.C) { App.I.CopyText(string.Join("\n", SelNodes().Select(n => Str(n, "url")))); e.SuppressKeyPress = true; }
            };
            lv.ContextMenuStrip = ItemMenu();
            tree = new TreeView { Dock = DockStyle.Left, Width = PaneGrip.Size("bm.tree", DockStyle.Left, Ui.S(240), Ui.S(900)), BorderStyle = BorderStyle.None, HideSelection = false, ShowLines = false, FullRowSelect = true, ItemHeight = Ui.S(26) };
            tree.AfterSelect += delegate { if (search.Text.Length == 0) ShowFolder(); };
            tree.ContextMenuStrip = FolderMenu();
            tree.NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right) tree.SelectedNode = e.Node; };
            tree.MouseDown += (s, e) => { if (e.Button == MouseButtons.Right) { var hit = tree.GetNodeAt(e.X, e.Y); if (hit != null) tree.SelectedNode = hit; } };
            Controls.Add(lv); Controls.Add(tree);
            PaneGrip.Attach(tree, "bm.tree", Ui.S(120));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 38), WrapContents = true };
            profile = MainForm.Combo(Ui.S(200)); profile.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            profile.SelectedIndexChanged += delegate
            {
                if (reverting || profile.SelectedIndex < 0) return;
                if (dirty && !Confirm("Discard unsaved bookmark changes?"))
                {
                    reverting = true;
                    try { profile.SelectedIndex = prevProfile; } finally { reverting = false; }
                    return;
                }
                prevProfile = profile.SelectedIndex;
                LoadProfile();
            };
            search = Ui.Search("Search all bookmarks"); search.Width = Ui.S(200); search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            searchTimer.Tick += delegate { searchTimer.Stop(); if (search.Text.Trim().Length == 0) ShowFolder(); else ShowSearch(); };
            search.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            bar.Controls.Add(profile); bar.Controls.Add(search);
            bar.Controls.Add(Ui.Btn("Open", delegate { OpenSelected(false); }, true));
            bar.Controls.Add(Ui.Btn("Add", delegate { AddBookmark(); }));
            bar.Controls.Add(Ui.Btn("New folder", delegate { NewFolder(); }));
            bar.Controls.Add(Ui.Btn("Edit", delegate { EditSelected(); }));
            bar.Controls.Add(Ui.Btn("Delete", delegate { DeleteSelected(); }));
            btnSave = Ui.Btn("Save to Chrome", delegate { Save(); });
            bar.Controls.Add(btnSave);
            bar.Controls.Add(Ui.Btn("Reload", delegate { if (!dirty || Confirm("Discard unsaved bookmark changes?")) LoadProfile(); }));
            bar.Controls.Add(Ui.Btn("Export HTML", delegate { ExportHtml(); }));
            bar.Controls.Add(Ui.Btn("Find duplicates", delegate { Duplicates(); }));
            Controls.Add(bar);
            info = new Label { Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(info);
            Ui.Theme(this);
            tree.BackColor = Ui.Bg; tree.ForeColor = Ui.Text;
        }

        bool Confirm(string q) { return MessageBox.Show(FindForm(), q, App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes; }

        protected override void OnLive(bool on)
        {
            if (!on || loaded) return;
            loaded = true;
            info.Text = "Loading...";
            status("Loading Chrome profiles...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<KeyValuePair<string, string>> ps = null;
                try { ps = ChromeInfo.Profiles(); } catch { }
                UI(() =>
                {
                    profile.Items.Clear();
                    if (ps != null) foreach (var p in ps) profile.Items.Add(new ProfileItem { Key = p.Key, Name = p.Value });
                    if (profile.Items.Count == 0) { status(""); info.Text = "Google Chrome user data was not found on this PC."; return; }
                    profile.SelectedIndex = 0;
                });
            });
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { searchTimer.Stop(); searchTimer.Dispose(); }
            base.Dispose(disposing);
        }
        public class ProfileItem { public string Key, Name; public override string ToString() { return Name; } }
        string Profile { get { var p = profile.SelectedItem as ProfileItem; return p == null ? "Default" : p.Key; } }

        void LoadProfile()
        {
            string f = Path.Combine(ChromeInfo.UserData, Profile, "Bookmarks");
            int seq = ++loadSeq;
            // Nothing is loaded (and nothing can be saved) until the background read finishes.
            doc = null; InvalidateIndex(); dirty = false; UpdateDirty();
            tree.BeginUpdate(); tree.Nodes.Clear(); tree.EndUpdate();
            lv.Items.Clear(); shown = new List<Dictionary<string, object>>();
            info.Text = "Loading...";
            status("Loading bookmarks...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, object> d = null; string err = null;
                try
                {
                    var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
                    d = File.Exists(f) ? js.DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object> : null;
                }
                catch (Exception ex) { err = "Could not read bookmarks: " + ex.Message; }
                UI(() => { if (seq == loadSeq) ApplyLoaded(f, d, err); });
            });
        }

        void ApplyLoaded(string f, Dictionary<string, object> d, string err)
        {
            file = f; doc = d; InvalidateIndex();
            info.Text = err ?? "";
            dirty = false; UpdateDirty();
            tree.BeginUpdate(); tree.Nodes.Clear();
            if (doc != null)
            {
                var roots = doc.ContainsKey("roots") ? doc["roots"] as Dictionary<string, object> : null;
                foreach (var key in new[] { "bookmark_bar", "other", "synced" })
                {
                    if (roots == null || !roots.ContainsKey(key)) continue;
                    var n = roots[key] as Dictionary<string, object>; if (n == null) continue;
                    var tn = MakeNode(n); tree.Nodes.Add(tn);
                }
                if (tree.Nodes.Count > 0) { tree.Nodes[0].Expand(); tree.SelectedNode = tree.Nodes[0]; }
            }
            tree.EndUpdate();
            status("");
            int count = doc == null ? 0 : All().Count(n => Str(n, "type") == "url");
            info.Text = doc == null ? (info.Text.Length > 0 ? info.Text : "No bookmarks file for this profile.") : count + " bookmarks.  Double-click opens in Chrome.  Changes are saved to Chrome with \"Save to Chrome\" (Chrome must be closed; a backup is kept).";
        }

        TreeNode MakeNode(Dictionary<string, object> n)
        {
            var tn = new TreeNode(Str(n, "name")) { Tag = n };
            foreach (var c in Children(n)) if (Str(c, "type") == "folder") tn.Nodes.Add(MakeNode(c));
            return tn;
        }

        static string Str(Dictionary<string, object> n, string k) { object v; return n != null && n.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        static List<Dictionary<string, object>> Children(Dictionary<string, object> n)
        {
            object v; if (n == null || !n.TryGetValue("children", out v)) return new List<Dictionary<string, object>>();
            var arr = v as object[]; if (arr != null) { var l = arr.OfType<Dictionary<string, object>>().ToList(); n["children"] = l.Cast<object>().ToList(); return Children(n); }
            var list = v as List<object>; if (list != null) return list.OfType<Dictionary<string, object>>().ToList();
            var al = v as ArrayList; if (al != null) { var l2 = al.Cast<object>().ToList(); n["children"] = l2; return l2.OfType<Dictionary<string, object>>().ToList(); }
            return new List<Dictionary<string, object>>();
        }
        static List<object> RawChildren(Dictionary<string, object> n) { Children(n); object v; if (!n.TryGetValue("children", out v) || !(v is List<object>)) { n["children"] = new List<object>(); } return (List<object>)n["children"]; }

        void InvalidateIndex() { parents = null; flat = null; }

        void EnsureIndex()
        {
            if (parents != null && flat != null) return;
            var par = new Dictionary<Dictionary<string, object>, Dictionary<string, object>>();
            var all = new List<Dictionary<string, object>>();
            var roots = doc == null || !doc.ContainsKey("roots") ? null : doc["roots"] as Dictionary<string, object>;
            if (roots != null)
            {
                var stack = new Stack<Dictionary<string, object>>();
                foreach (var v in roots.Values) { var d = v as Dictionary<string, object>; if (d != null) stack.Push(d); }
                while (stack.Count > 0)
                {
                    var n = stack.Pop(); all.Add(n);
                    foreach (var c in Children(n)) { par[c] = n; stack.Push(c); }
                }
            }
            parents = par; flat = all;
        }

        IEnumerable<Dictionary<string, object>> All() { EnsureIndex(); return flat; }
        Dictionary<string, object> ParentOf(Dictionary<string, object> node)
        {
            if (node == null) return null;
            EnsureIndex();
            Dictionary<string, object> p; return parents.TryGetValue(node, out p) ? p : null;
        }
        // True when target is node itself or lies anywhere inside node's subtree.
        static bool IsInside(Dictionary<string, object> node, Dictionary<string, object> target)
        {
            if (node == target) return true;
            foreach (var c in Children(node)) if (Str(c, "type") == "folder" && IsInside(c, target)) return true;
            return false;
        }

        static string ChromeTime(string s) { long v; if (!long.TryParse(s, out v) || v <= 0) return ""; try { return new DateTime(1601, 1, 1).AddTicks(v * 10).ToLocalTime().ToString("yyyy-MM-dd"); } catch { return ""; } }
        static string NowChrome() { return ((DateTime.UtcNow - new DateTime(1601, 1, 1)).Ticks / 10).ToString(); }

        Dictionary<string, object> CurrentFolder { get { return tree.SelectedNode == null ? null : tree.SelectedNode.Tag as Dictionary<string, object>; } }

        void ShowFolder()
        {
            var f = CurrentFolder;
            Fill(f == null ? new List<Dictionary<string, object>>() : Children(f), f == null ? "" : Str(f, "name"));
        }
        void ShowSearch()
        {
            string q = search.Text.Trim();
            Fill(All().Where(n => Str(n, "type") == "url" && (Str(n, "name").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || Str(n, "url").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).Take(2000).ToList(), null);
        }
        void Fill(List<Dictionary<string, object>> items, string folder)
        {
            shown = items;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var n in items)
            {
                bool isFolder = Str(n, "type") == "folder";
                var li = new ListViewItem(Str(n, "name")) { Tag = n };
                li.SubItems.Add(isFolder ? Children(n).Count + " items" : Str(n, "url"));
                li.SubItems.Add(folder ?? Str(ParentOf(n), "name"));
                li.SubItems.Add(ChromeTime(Str(n, "date_added")));
                if (isFolder) li.Font = Ui.Bold;
                lv.Items.Add(li);
            }
            lv.EndUpdate();
        }

        List<Dictionary<string, object>> SelNodes() { return lv.SelectedItems.Cast<ListViewItem>().Select(i => (Dictionary<string, object>)i.Tag).ToList(); }

        void OpenSelected(bool newWindow)
        {
            var sel = SelNodes();
            if (sel.Count == 1 && Str(sel[0], "type") == "folder")
            {
                foreach (TreeNode tn in AllTreeNodes(tree.Nodes)) if (tn.Tag == sel[0]) { tree.SelectedNode = tn; search.Text = ""; return; }
            }
            foreach (var n in sel.Where(n => Str(n, "type") == "url").Take(30)) ChromeInfo.Open(Profile, Str(n, "url"), newWindow);
        }
        static IEnumerable<TreeNode> AllTreeNodes(TreeNodeCollection nodes) { foreach (TreeNode n in nodes) { yield return n; foreach (var c in AllTreeNodes(n.Nodes)) yield return c; } }

        ContextMenuStrip ItemMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("Open in Chrome\tEnter", null, delegate { OpenSelected(false); });
            m.Items.Add("Open in new window", null, delegate { OpenSelected(true); });
            m.Items.Add("Open in incognito", null, delegate { foreach (var n in SelNodes().Where(x => Str(x, "type") == "url").Take(10)) Hidden.Open(ChromeInfo.Exe, "--incognito \"" + Str(n, "url") + "\""); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Copy address\tCtrl+C", null, delegate { App.I.CopyText(string.Join("\n", SelNodes().Select(n => Str(n, "url")))); });
            m.Items.Add("Copy as Markdown link", null, delegate { App.I.CopyText(string.Join("\n", SelNodes().Select(n => "[" + Str(n, "name") + "](" + Str(n, "url") + ")"))); });
            m.Items.Add("Save as prompt / snippet", null, delegate { foreach (var n in SelNodes()) Store.NewPrompt(Str(n, "name"), Str(n, "url"), "Bookmarks"); status("Saved to Prompts > Bookmarks."); });
            m.Items.Add("Add to quick launch", null, delegate { foreach (var n in SelNodes().Where(x => Str(x, "type") == "url")) QuickDock.AddItem(Str(n, "name"), Str(n, "url"), ""); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Edit...\tF2", null, delegate { EditSelected(); });
            var move = new ToolStripMenuItem("Move to folder");
            m.Items.Add(move);
            m.Items.Add("Delete\tDel", null, delegate { DeleteSelected(); });
            m.Opening += delegate
            {
                move.DropDownItems.Clear();
                foreach (var f in All().Where(n => Str(n, "type") == "folder").Take(200))
                { var folder = f; move.DropDownItems.Add(Str(f, "name"), null, delegate { MoveTo(folder); }); }
            };
            Ui.ThemeMenu(m);
            return m;
        }
        ContextMenuStrip FolderMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("Open all in Chrome", null, delegate { var f = CurrentFolder; if (f == null) return; var urls = Children(f).Where(n => Str(n, "type") == "url").ToList(); if (urls.Count > 15 && !Confirm("Open " + urls.Count + " tabs?")) return; foreach (var n in urls) ChromeInfo.Open(Profile, Str(n, "url")); });
            m.Items.Add("New folder here", null, delegate { NewFolder(); });
            m.Items.Add("Rename folder", null, delegate { var f = CurrentFolder; if (f == null || tree.SelectedNode.Parent == null) return; var n = Ui.Ask("Rename folder", "Name", Str(f, "name"), FindForm()); if (!string.IsNullOrEmpty(n)) { f["name"] = n; tree.SelectedNode.Text = n; Touch(); } });
            m.Items.Add("Delete folder", null, delegate
            {
                var f = CurrentFolder; if (f == null || tree.SelectedNode.Parent == null) return;
                if (!Confirm("Delete folder \"" + Str(f, "name") + "\" and everything in it?")) return;
                var p = ParentOf(f); if (p != null) RawChildren(p).Remove(f); tree.SelectedNode.Remove(); Touch(); ShowFolder();
            });
            Ui.ThemeMenu(m);
            return m;
        }

        void Touch() { InvalidateIndex(); dirty = true; UpdateDirty(); }
        void UpdateDirty() { btnSave.Text = dirty ? "Save to Chrome  *" : "Save to Chrome"; btnSave.Enabled = dirty; }

        int NextId() { int max = 0; foreach (var n in All()) { int v; if (int.TryParse(Str(n, "id"), out v)) max = Math.Max(max, v); } return max + 1; }

        void AddBookmark()
        {
            var f = CurrentFolder; if (f == null || doc == null) return;
            string clip = ""; try { clip = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : ""; } catch { }
            var url = Ui.Ask("Add bookmark", "Address", clip.StartsWith("http") ? clip : "https://", FindForm()); if (string.IsNullOrEmpty(url)) return;
            var name = Ui.Ask("Add bookmark", "Name", url, FindForm()); if (string.IsNullOrEmpty(name)) return;
            RawChildren(f).Add(new Dictionary<string, object> { { "date_added", NowChrome() }, { "guid", Guid.NewGuid().ToString() }, { "id", NextId().ToString() }, { "name", name }, { "type", "url" }, { "url", url } });
            Touch(); ShowFolder();
        }
        void NewFolder()
        {
            var f = CurrentFolder; if (f == null || doc == null) return;
            var name = Ui.Ask("New folder", "Folder name", "", FindForm()); if (string.IsNullOrEmpty(name)) return;
            var nf = new Dictionary<string, object> { { "children", new List<object>() }, { "date_added", NowChrome() }, { "date_modified", NowChrome() }, { "guid", Guid.NewGuid().ToString() }, { "id", NextId().ToString() }, { "name", name }, { "type", "folder" } };
            RawChildren(f).Add(nf);
            tree.SelectedNode.Nodes.Add(new TreeNode(name) { Tag = nf });
            Touch(); ShowFolder();
        }
        void EditSelected()
        {
            var n = SelNodes().FirstOrDefault(); if (n == null) return;
            var name = Ui.Ask("Edit bookmark", "Name", Str(n, "name"), FindForm()); if (string.IsNullOrEmpty(name)) return;
            n["name"] = name;
            if (Str(n, "type") == "url") { var url = Ui.Ask("Edit bookmark", "Address", Str(n, "url"), FindForm()); if (!string.IsNullOrEmpty(url)) n["url"] = url; }
            else foreach (var tn in AllTreeNodes(tree.Nodes)) if (tn.Tag == n) tn.Text = name;
            Touch(); if (search.Text.Length > 0) ShowSearch(); else ShowFolder();
        }
        void DeleteSelected()
        {
            var sel = SelNodes(); if (sel.Count == 0) return;
            if (!Confirm("Delete " + sel.Count + " bookmark(s)?")) return;
            foreach (var n in sel) { var p = ParentOf(n); if (p != null) RawChildren(p).Remove(n); foreach (var tn in AllTreeNodes(tree.Nodes).ToList()) if (tn.Tag == n) tn.Remove(); }
            Touch(); if (search.Text.Length > 0) ShowSearch(); else ShowFolder();
        }
        void MoveTo(Dictionary<string, object> folder)
        {
            int skipped = 0, moved = 0;
            foreach (var n in SelNodes())
            {
                if (n == folder) { skipped++; continue; }
                // A folder cannot be moved into itself or into one of its own subfolders (the subtree would be lost).
                if (Str(n, "type") == "folder" && IsInside(n, folder)) { skipped++; continue; }
                var p = ParentOf(n); if (p != null) RawChildren(p).Remove(n);
                RawChildren(folder).Add(n); moved++;
            }
            if (skipped > 0) status(skipped + " item(s) not moved: a folder cannot be moved into itself or its own subfolder.");
            if (moved == 0) return;
            Touch(); LoadTreeKeep();
        }
        void LoadTreeKeep()
        {
            var keep = CurrentFolder;
            tree.BeginUpdate(); tree.Nodes.Clear();
            var roots = doc["roots"] as Dictionary<string, object>;
            foreach (var key in new[] { "bookmark_bar", "other", "synced" }) if (roots != null && roots.ContainsKey(key)) tree.Nodes.Add(MakeNode((Dictionary<string, object>)roots[key]));
            tree.EndUpdate();
            foreach (var tn in AllTreeNodes(tree.Nodes)) if (tn.Tag == keep) { tree.SelectedNode = tn; break; }
            ShowFolder();
        }

        void Save()
        {
            if (doc == null || !dirty) return;
            if (ChromeInfo.Running) { MessageBox.Show(FindForm(), "Close Google Chrome first (all windows), then press Save again.\n\nChrome keeps bookmarks in memory and would overwrite the changes.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try
            {
                string bdir = Path.Combine(Store.Dir, "chrome-backups"); Directory.CreateDirectory(bdir);
                File.Copy(file, Path.Combine(bdir, Profile + " Bookmarks " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                doc.Remove("checksum");
                DataFiles.WriteAllTextAtomic(file, Js.Serialize(doc), new UTF8Encoding(false));
                dirty = false; UpdateDirty();
                status("Bookmarks saved to Chrome. Backup in data\\chrome-backups.");
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Could not save: " + ex.Message, App.Name); }
        }

        void ExportHtml()
        {
            if (doc == null) return;
            using (var d = new SaveFileDialog { Filter = "HTML|*.html", FileName = "bookmarks " + DateTime.Now.ToString("yyyy-MM-dd") + ".html" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                var sb = new StringBuilder("<!DOCTYPE NETSCAPE-Bookmark-file-1>\n<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">\n<TITLE>Bookmarks</TITLE>\n<H1>Bookmarks</H1>\n<DL><p>\n");
                Action<Dictionary<string, object>, int> walk = null;
                walk = (n, depth) =>
                {
                    string pad = new string(' ', depth * 4);
                    foreach (var c in Children(n))
                    {
                        if (Str(c, "type") == "folder") { sb.Append(pad + "<DT><H3>" + H(Str(c, "name")) + "</H3>\n" + pad + "<DL><p>\n"); walk(c, depth + 1); sb.Append(pad + "</DL><p>\n"); }
                        else sb.Append(pad + "<DT><A HREF=\"" + H(Str(c, "url")) + "\">" + H(Str(c, "name")) + "</A>\n");
                    }
                };
                foreach (var v in ((Dictionary<string, object>)doc["roots"]).Values) { var r = v as Dictionary<string, object>; if (r != null && Str(r, "type") == "folder") { sb.Append("<DT><H3>" + H(Str(r, "name")) + "</H3>\n<DL><p>\n"); walk(r, 1); sb.Append("</DL><p>\n"); } }
                sb.Append("</DL><p>\n");
                try { File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(false)); status("Exported."); } catch (Exception ex) { status("Export failed: " + ex.Message); }
            }
        }

        static string H(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"); }

        void Duplicates()
        {
            var dups = All().Where(n => Str(n, "type") == "url").GroupBy(n => Str(n, "url").TrimEnd('/'), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1)).ToList();
            Fill(dups, null);
            status(dups.Count == 0 ? "No duplicate bookmarks." : dups.Count + " duplicate(s) shown (the first copy of each is kept out of the list). Select and Delete to remove.");
        }
    }

    // ===================== Extensions =====================
    class ExtInfo { public string Id, Name, Version, Desc, Folder, Profile; public bool Enabled; }

    class ExtensionsPanel : LivePanel
    {
        readonly FastList lv;
        readonly TextBox search;
        readonly Label info;
        readonly Action<string> status;
        List<ExtInfo> all = new List<ExtInfo>();
        bool loaded;

        public ExtensionsPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;
            lv = new FastList { Dock = DockStyle.Fill, MultiSelect = true };
            lv.Columns.Add("Extension", Ui.S(260)); lv.Columns.Add("State", Ui.S(80)); lv.Columns.Add("Version", Ui.S(90)); lv.Columns.Add("Profile", Ui.S(120)); lv.Columns.Add("Description", Ui.S(360));
            lv.DoubleClick += delegate { Details(); };
            var m = Ui.Menu();
            m.Items.Add("Manage in Chrome (enable / disable / remove)", null, delegate { Details(); });
            m.Items.Add("Open Chrome Web Store page", null, delegate { foreach (var e in Sel()) ChromeInfo.Open(e.Profile, "https://chromewebstore.google.com/detail/" + e.Id); });
            m.Items.Add("Open extension folder", null, delegate { var e = Sel().FirstOrDefault(); if (e != null) Hidden.Open("explorer.exe", "\"" + e.Folder + "\""); });
            m.Items.Add("Copy ID", null, delegate { App.I.CopyText(string.Join("\n", Sel().Select(e => e.Id))); });
            m.Items.Add("Copy list", null, delegate { App.I.CopyText(string.Join("\n", Sel().Select(e => e.Name + " " + e.Version + " (" + e.Id + ")"))); });
            m.Items.Add("Search the web", null, delegate { var e = Sel().FirstOrDefault(); if (e != null) Hidden.Open("https://www.bing.com/search?q=" + Uri.EscapeDataString(e.Name + " chrome extension")); });
            Ui.ThemeMenu(m); lv.ContextMenuStrip = m;
            Controls.Add(lv);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 38), WrapContents = true };
            search = Ui.Search("Search extensions"); search.Width = Ui.S(200); search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            search.TextChanged += delegate { Fill(); };
            bar.Controls.Add(search);
            bar.Controls.Add(Ui.Btn("Manage in Chrome", delegate { Details(); }, true));
            bar.Controls.Add(Ui.Btn("Web Store page", delegate { foreach (var e in Sel()) ChromeInfo.Open(e.Profile, "https://chromewebstore.google.com/detail/" + e.Id); }));
            bar.Controls.Add(Ui.Btn("All extensions page", delegate { ChromeInfo.Open("Default", "chrome://extensions/"); }));
            bar.Controls.Add(Ui.Btn("Shortcuts page", delegate { ChromeInfo.Open("Default", "chrome://extensions/shortcuts"); }));
            bar.Controls.Add(Ui.Btn("Refresh", delegate { Reload(); }));
            Controls.Add(bar);
            info = new Label { Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft, Text = "Chrome allows this only on its own page. Double-click an extension to open it." };
            Controls.Add(info);
            Ui.Theme(this);
        }

        protected override void OnLive(bool on) { if (on && !loaded) Reload(); }

        void Reload()
        {
            loaded = true; status("Reading Chrome extensions...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = new List<ExtInfo>();
                try { foreach (var p in ChromeInfo.Profiles()) list.AddRange(Read(p.Key)); } catch (Exception ex) { App.Log("Extensions: " + ex.Message); }
                UI(() => { all = list; Fill(); status(list.Count + " extensions in " + list.Select(e => e.Profile).Distinct().Count() + " profile(s)."); });
            });
        }

        static IEnumerable<ExtInfo> Read(string profile)
        {
            var res = new List<ExtInfo>();
            string root = Path.Combine(ChromeInfo.UserData, profile, "Extensions");
            if (!Directory.Exists(root)) return res;
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            Dictionary<string, object> settings = null;
            foreach (var pf in new[] { "Secure Preferences", "Preferences" })
                try
                {
                    var d = js.DeserializeObject(File.ReadAllText(Path.Combine(ChromeInfo.UserData, profile, pf))) as Dictionary<string, object>;
                    var ex = d != null && d.ContainsKey("extensions") ? d["extensions"] as Dictionary<string, object> : null;
                    var st = ex != null && ex.ContainsKey("settings") ? ex["settings"] as Dictionary<string, object> : null;
                    if (st != null) { settings = st; break; }
                }
                catch { }
            foreach (var dir in Directory.GetDirectories(root))
            {
                string id = Path.GetFileName(dir);
                if (id == "Temp") continue;
                var ver = Directory.GetDirectories(dir).OrderByDescending(v => v).FirstOrDefault(); if (ver == null) continue;
                try
                {
                    var man = js.DeserializeObject(File.ReadAllText(Path.Combine(ver, "manifest.json"))) as Dictionary<string, object>;
                    if (man == null) continue;
                    var e = new ExtInfo { Id = id, Folder = ver, Profile = profile, Version = Convert.ToString(man.ContainsKey("version") ? man["version"] : ""), Enabled = true };
                    e.Name = Msg(js, ver, man, Convert.ToString(man.ContainsKey("name") ? man["name"] : id));
                    e.Desc = Msg(js, ver, man, Convert.ToString(man.ContainsKey("description") ? man["description"] : ""));
                    if (settings != null && settings.ContainsKey(id))
                    {
                        var s = settings[id] as Dictionary<string, object>;
                        if (s != null && s.ContainsKey("disable_reasons")) { var dr = s["disable_reasons"]; e.Enabled = dr == null || (dr is int && (int)dr == 0) || (dr is object[] && ((object[])dr).Length == 0) || (dr is ArrayList && ((ArrayList)dr).Count == 0); }
                        if (s != null && s.ContainsKey("state") && s["state"] is int && (int)s["state"] == 0) e.Enabled = false;
                    }
                    res.Add(e);
                }
                catch { }
            }
            return res;
        }

        static string Msg(JavaScriptSerializer js, string folder, Dictionary<string, object> man, string text)
        {
            if (!text.StartsWith("__MSG_")) return text;
            string key = text.Substring(6).TrimEnd('_');
            string loc = man.ContainsKey("default_locale") ? Convert.ToString(man["default_locale"]) : "en";
            foreach (var l in new[] { "en", "en_US", "en_GB", loc })
                try
                {
                    var f = Path.Combine(folder, "_locales", l, "messages.json"); if (!File.Exists(f)) continue;
                    var d = js.DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>;
                    foreach (var kv in d) if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) { var m = kv.Value as Dictionary<string, object>; if (m != null && m.ContainsKey("message")) return Convert.ToString(m["message"]); }
                }
                catch { }
            return key;
        }

        void Fill()
        {
            string q = search.Text.Trim();
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var e in all.Where(x => q.Length == 0 || x.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || x.Id.Contains(q)).OrderBy(x => x.Name))
            {
                var li = new ListViewItem(e.Name) { Tag = e };
                li.SubItems.Add(e.Enabled ? "On" : "Off"); li.SubItems.Add(e.Version); li.SubItems.Add(e.Profile); li.SubItems.Add(e.Desc);
                if (!e.Enabled) li.ForeColor = Ui.Muted;
                lv.Items.Add(li);
            }
            lv.EndUpdate();
        }

        List<ExtInfo> Sel() { return lv.SelectedItems.Cast<ListViewItem>().Select(i => (ExtInfo)i.Tag).ToList(); }
        void Details() { var e = Sel().FirstOrDefault(); if (e != null) ChromeInfo.Open(e.Profile, "chrome://extensions/?id=" + e.Id); else ChromeInfo.Open("Default", "chrome://extensions/"); }
    }

    class ChromePage : Panel
    {
        readonly TabStrip tabs;
        readonly Control[] views;
        public ChromePage(MainForm owner)
        {
            Dock = DockStyle.Fill; Visible = false;
            Action<string> st = s => owner.Status(s);
            views = new Control[] { new BookmarksPanel(st), new ExtensionsPanel(st) };
            host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, Ui.S(4), 0, 0) };
            foreach (var v in views) { v.Visible = false; v.Dock = DockStyle.None; host.Controls.Add(v); }
            host.Layout += delegate { Arrange(); };
            Controls.Add(host);
            var top = MainForm.Bar();
            top.Controls.Add(MainForm.Heading("Chrome"));
            tabs = new TabStrip { Dock = DockStyle.None, AutoSize = true, Margin = new Padding(0) };
            tabs.Add("Bookmarks"); tabs.Add("Extensions");
            tabs.Changed += i => { selected = i; ApplyVisibility(); };
            Resize += delegate { if (Wide != split) ApplyVisibility(); };
            top.Controls.Add(tabs);
            top.Controls.Add(Ui.Btn("Open Chrome", delegate { Hidden.Open(ChromeInfo.Exe); }));
            Controls.Add(top);
            tabs.Select(0);
        }
        public void SelectTab(string id) { tabs.Select(id == "extensions" ? 1 : 0); }
        public string ActiveTab { get { return selected == 1 ? "extensions" : "bookmarks"; } }

        // Wide windows show bookmarks and extensions side by side (no tabs); narrow windows show one tab at a time.
        readonly Panel host;
        int selected;
        bool split;
        bool Wide { get { return Width >= Ui.S(1300); } }

        void ApplyVisibility()
        {
            split = Wide;
            tabs.Visible = !split;
            SuspendLayout();
            for (int k = 0; k < views.Length; k++) views[k].Visible = split || k == selected;
            ResumeLayout(true);
            host.PerformLayout();
        }

        void Arrange()
        {
            var r = host.DisplayRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            if (split)
            {
                int gap = Ui.S(16), left = (r.Width - gap) * 58 / 100;
                Fit(views[0], new Rectangle(r.X, r.Y, left, r.Height));
                Fit(views[1], new Rectangle(r.X + left + gap, r.Y, r.Width - left - gap, r.Height));
            }
            else Fit(views[Math.Max(0, selected)], r);
        }

        static void Fit(Control c, Rectangle r) { if (c.Bounds != r) c.Bounds = r; }
    }
}
