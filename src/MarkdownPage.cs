using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Markdown notes: an Obsidian-style reader and editor for folders of .md files ("vaults").
    //  Left:   vaults, filter, Favorites (★), Recent (history), the folder tree.
    //  Middle: the note - Read (formatted), Edit (plain text, auto-save) or Split - with Back / Forward history.
    //  Right:  outline (headings), backlinks (notes that link here with [[name]]), tags in the vault.
    //  Wiki links [[note]] / [[note|text]] open or create notes, #tags search, - [ ] tasks, front matter, Ctrl+O
    //  quick switcher, Ctrl+Shift+F full-text search, daily note. Favorites and recent notes use the shared MetaStore
    //  (scope "md"), so they behave like favorites everywhere in the app.
    // ===================================================================================================

    static class MdVault
    {
        static readonly object gate = new object();
        static List<string> files = new List<string>();
        static string indexedFor = "", requestedRoot = "";
        static int requestedRevision;
        static bool indexing;
        public static event Action Indexed;
        static readonly string[] Skip = { "node_modules", ".git", ".venv", "venv", "__pycache__", "bin", "obj", "dist", "build", ".next", "site-packages" };

        public static List<string> Vaults { get { return Store.Settings.MdVaults; } }
        public static string Current { get { var v = Vaults; int i = Store.Settings.MdVaultIndex; return v.Count == 0 ? "" : v[Math.Max(0, Math.Min(v.Count - 1, i))]; } }
        public static List<string> Files { get { return SearchPaths().ToList(); } }
        public static bool Busy { get { lock (gate) return indexing; } }

        // All .md files of the current vault, found on a worker (big folders never block the window).
        public static void Index(bool force)
        {
            string root = Current;
            lock (gate)
            {
                if (!force && string.Equals(root, indexing ? requestedRoot : indexedFor, StringComparison.OrdinalIgnoreCase)) return;
                requestedRoot = root;
                requestedRevision++;
                if (root.Length == 0) { indexedFor = ""; files = new List<string>(); }
                if (indexing || root.Length == 0) return;
                indexing = true;
            }
            ThreadPool.QueueUserWorkItem(delegate { IndexLatest(); });
        }

        static void IndexLatest()
        {
            for (;;)
            {
                string root;
                int revision;
                lock (gate) { root = requestedRoot; revision = requestedRevision; }
                var found = Scan(root, revision);
                lock (gate)
                {
                    if (revision != requestedRevision) continue;
                    files = found ?? new List<string>();
                    indexedFor = found != null ? root : "";
                    indexing = false;
                }
                if (App.I != null) try
                {
                    App.I.Sync.BeginInvoke(new Action(() =>
                    {
                        if (revision != requestedRevision) return;
                        var updated = Indexed;
                        if (updated != null) updated();
                    }));
                }
                catch (InvalidOperationException) { }
                return;
            }
        }

        static List<string> Scan(string root, int revision)
        {
            if (root.Length == 0 || !Directory.Exists(root)) return null;
            const int limit = 20000;
            var found = new List<string>();
            var folders = new Stack<DirectoryInfo>();
            folders.Push(new DirectoryInfo(root));
            while (folders.Count > 0 && found.Count < limit)
            {
                if (revision != Volatile.Read(ref requestedRevision)) return null;
                var folder = folders.Pop();
                try
                {
                    foreach (var file in folder.EnumerateFiles("*.md"))
                    {
                        if (revision != Volatile.Read(ref requestedRevision)) return null;
                        found.Add(file.FullName);
                        if (found.Count == limit) break;
                    }
                    if (found.Count == limit) break;
                    foreach (var sub in folder.EnumerateDirectories())
                    {
                        if (revision != Volatile.Read(ref requestedRevision)) return null;
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (sub.Name.Equals(".claude", StringComparison.OrdinalIgnoreCase) ||
                            (!sub.Name.StartsWith(".", StringComparison.Ordinal) && !Skip.Contains(sub.Name, StringComparer.OrdinalIgnoreCase))) folders.Push(sub);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.Security.SecurityException) { }
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        // The index is replaced atomically. A search can enumerate this snapshot without
        // touching disk or copying every path, including during a vault switch.
        public static IEnumerable<string> SearchPaths()
        {
            string current = Current;
            lock (gate) return string.Equals(indexedFor, current, StringComparison.OrdinalIgnoreCase)
                ? (IEnumerable<string>)files : new string[0];
        }

        // [[name]] -> the file: same name (case-insensitive), or a path inside the vault.
        public static string Resolve(string target)
        {
            target = (target ?? "").Split('#')[0].Trim();
            if (target.Length == 0) return null;
            string withExt = target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? target : target + ".md";
            var all = Files;
            string p = all.FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(withExt), StringComparison.OrdinalIgnoreCase) && f.Replace('\\', '/').EndsWith(withExt.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            return p ?? all.FirstOrDefault(f => string.Equals(Path.GetFileName(f), Path.GetFileName(withExt), StringComparison.OrdinalIgnoreCase));
        }

        public static string Read(string path) { try { return File.ReadAllText(path, Encoding.UTF8); } catch { return null; } }
        static readonly Dictionary<string, KeyValuePair<DateTime, string>> textCache = new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.OrdinalIgnoreCase);
        // Note text for search: read once, again only when the file changed; big files (> 400 KB) are skipped.
        public static string TextOf(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 400000) return null;
                KeyValuePair<DateTime, string> c;
                lock (textCache) if (textCache.TryGetValue(path, out c) && c.Key == fi.LastWriteTimeUtc) return c.Value;
                string t = Read(path);
                lock (textCache) { if (textCache.Count > 3000) textCache.Clear(); textCache[path] = new KeyValuePair<DateTime, string>(fi.LastWriteTimeUtc, t); }
                return t;
            }
            catch { return null; }
        }
    }

    class MarkdownPage : Panel
    {
        readonly ComboBox vault;
        readonly TextBox filter;
        readonly TreeView tree;
        readonly Label title, info;
        readonly MdView view;
        readonly TextBox editor;
        readonly ListBox backlinks, tags;
        readonly TreeView outline;
        readonly Dictionary<int, TreeNode> outlineNodes = new Dictionary<int, TreeNode>();
        readonly ListView results;
        readonly Panel center, right;
        readonly SplitContainer split;
        readonly IconButton modeRead, modeEdit, modeSplit, backBtn, fwdBtn, favBtn;
        readonly EditorTabs tabs;       // VS Code style tabs for open notes
        readonly ViewStack side;        // the right side: collapsible, resizable sections
        readonly FluentTextBox outlineFilter;
        string headingNow = "";
        readonly List<string> back = new List<string>(), fwd = new List<string>();
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 900 };
        string current, loadedText;
        bool loading;
        bool refreshVaultOnShow;
        bool splitPositioned;
        int backlinksRevision, tagsRevision, searchRevision, openRevision;
        readonly ContextMenuStrip menu = Ui.Menu(true);
        Control ai;   // the AI moved to the Assistant (right of every page); kept for the column fitting code
        Panel leftPane; bool fitting;
        // The note always keeps room: on narrow windows the outline column hides first, then the AI panel narrows,
        // then it steps aside (it comes back when there is room again).
        protected override void OnLayout(LayoutEventArgs e) { FitColumns(); base.OnLayout(e); }
        void FitColumns()
        {
            if (fitting || right == null || leftPane == null || ClientSize.Width <= 0) return;
            fitting = true;
            try
            {
                int w = ClientSize.Width, min = Ui.S(440);
                bool wantAi = ai != null && Store.Settings.MdAiOpen;
                bool showLeft = !Store.Settings.MdTreeHidden && !zen;
                Func<bool, bool, int> room = (r, a) => w - (showLeft ? leftPane.Width : 0) - (r ? right.Width : 0) - (a && ai != null ? ai.Width : 0);
                bool showRight = !Store.Settings.MdSideHidden && !zen, showAi = wantAi;
                if (room(showRight, showAi) < min && showAi) showRight = false;
                if (showAi && room(showRight, showAi) < min) { int need = min - room(showRight, showAi); int nw = Math.Max(Ui.S(280), ai.Width - need); if (nw != ai.Width) ai.Width = nw; }
                if (showAi && room(showRight, showAi) < min) showLeft = false;   // you asked for the AI: the note tree makes room
                if (showAi && room(showRight, showAi) < min) { showAi = false; showLeft = true; }
                if (!showAi && room(true, false) >= min && !Store.Settings.MdSideHidden && !zen) showRight = true;
                if (!showAi && room(showRight, false) < min) showRight = false;      // no AI here any more: outline steps aside first,
                if (!showAi && room(showRight, false) < min) showLeft = false;       // then the note tree
                // Set, not compare: Visible reads false while any parent is hidden, so a comparison can skip a needed change.
                leftPane.Visible = showLeft;
                right.Visible = showRight;
                if (ai != null) ai.Visible = showAi;
            }
            finally { fitting = false; }
        }
        ListBox mentions, outgoing;

        public MarkdownPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Markdown notes"));
            vault = MainForm.Combo(Ui.S(230));
            vault.SelectedIndexChanged += delegate { if (loading || vault.SelectedIndex < 0) return; if (vault.SelectedIndex >= MdVault.Vaults.Count) { AddVault(); return; } openRevision++; searchRevision++; backlinksRevision++; Store.Settings.MdVaultIndex = vault.SelectedIndex; Store.MarkSettings(); MdVault.Index(true); FillTree(); };
            bar.Controls.Add(vault);
            bar.Controls.Add(Ui.Btn("New note", delegate { NewNote(null); }, true));
            bar.Controls.Add(Ui.Btn("Today", delegate { DailyNote(); }));
            var fromTemplate = Ui.Btn("Template", null);
            fromTemplate.Click += delegate { TemplateMenu(fromTemplate); };
            bar.Controls.Add(Tip(fromTemplate, "New note from a template (yours: put .md files in the vault's _templates folder)"));
            bar.Controls.Add(Tip(Ui.Btn("Open note", delegate { QuickSwitch(); }), "Find a note by name (Ctrl+O)"));
            bar.Controls.Add(Tip(Ui.Btn("Random", delegate { var all = MdVault.Files; if (all.Count > 0) Open(all[new Random().Next(all.Count)], true); }), "Open a random note"));
            bar.Controls.Add(Tip(Ui.Btn("Search", delegate { FullSearch(Ui.Ask("Search in all notes", "Text or #tag", filter.Text, FindForm())); }), "Search inside all notes (Ctrl+Shift+F)"));
            bar.Controls.Add(Ui.Btn("Vaults...", delegate { VaultMenu(); }));
            var import = Ui.Btn("Import", null);
            import.Click += delegate { ImportMenu(import); };
            bar.Controls.Add(Tip(import, "Open or bring in Markdown files and folders (or drop them on this page)"));
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) ImportPaths(files); };

            // ---- left: filter + tree (favorites, recent, all) ----
            filter = new FluentTextBox { Dock = DockStyle.Top };
            EventHandler cue = delegate { try { Native.SendMessage(filter.Handle, 0x1501, (IntPtr)1, "Filter notes"); } catch { } };
            if (filter.IsHandleCreated) cue(filter, EventArgs.Empty); else filter.HandleCreated += cue;
            filter.TextChanged += delegate { Ui.Debounce("md.filter", 200, FillTree); };
            filter.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FullSearch(filter.Text); } };
            tree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, HideSelection = false, FullRowSelect = true, ShowLines = false, Indent = Ui.S(16), ItemHeight = Ui.S(24), Font = Ui.Base };
            tree.NodeMouseClick += (s, e) => { tree.SelectedNode = e.Node; if (e.Button == MouseButtons.Left && e.Node.Name == "file") Open((string)e.Node.Tag, true); else if (e.Button == MouseButtons.Right) NodeMenu(e.Node, e.Location); };
            tree.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && tree.SelectedNode != null && tree.SelectedNode.Name == "file") Open((string)tree.SelectedNode.Tag, true); };
            var left = new Panel { Dock = DockStyle.Left, Width = Ui.S(270), Padding = new Padding(0, Ui.S(4), Ui.S(8), 0) };
            left.Controls.Add(tree); left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(6) }); left.Controls.Add(filter);

            // ---- right: outline, backlinks, tags ----
            backlinks = SideList(); tags = SideList();
            // Outline like Obsidian: nested headings, collapsible, the one you read is highlighted.
            outline = new NoHScrollTree { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ShowLines = false, ShowRootLines = true, ShowPlusMinus = true, HideSelection = false, FullRowSelect = true, ItemHeight = Ui.S(24), Indent = Ui.S(14), Font = Ui.Base };
            outline.NodeMouseClick += (s, e) => { if (e.Node.Tag is int && e.X > e.Node.Bounds.Left - Ui.S(4)) { outline.SelectedNode = e.Node; view.ScrollTo((int)e.Node.Tag); } };
            backlinks.DoubleClick += delegate { var p = backlinks.SelectedItem as PathItem; if (p != null) Open(p.Path, true); };
            backlinks.Click += delegate { var p = backlinks.SelectedItem as PathItem; if (p != null) Open(p.Path, true); };
            tags.Click += delegate { if (tags.SelectedItem != null) FullSearch(((string)tags.SelectedItem).Split(' ')[0]); };
            right = new Panel { Dock = DockStyle.Right, Width = PaneGrip.Size("md.side", DockStyle.Right, Ui.S(260), Ui.S(700)), Padding = new Padding(Ui.S(6), Ui.S(2), 0, 0) };
            mentions = SideList(); outgoing = SideList();
            mentions.Click += delegate { var p = mentions.SelectedItem as PathItem; if (p != null) Open(p.Path, true); };
            outgoing.Click += delegate { var p = outgoing.SelectedItem as PathItem; if (p != null) { if (File.Exists(p.Path)) Open(p.Path, true); else OnLink("wiki:" + p.Label); } };
            outlineFilter = new FluentTextBox { Dock = DockStyle.Top };
            EventHandler ocue = delegate { try { Native.SendMessage(outlineFilter.Handle, 0x1501, (IntPtr)1, "Filter headings (Ctrl+Shift+O)"); } catch { } };
            if (outlineFilter.IsHandleCreated) ocue(outlineFilter, EventArgs.Empty); else outlineFilter.HandleCreated += ocue;
            outlineFilter.TextChanged += delegate { FillOutline(); };
            outlineFilter.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && outline.Nodes.Count > 0) { e.SuppressKeyPress = true; var n = outline.SelectedNode ?? outline.Nodes[0]; if (n.Tag is int) view.ScrollTo((int)n.Tag); } else if (e.KeyCode == Keys.Down) { e.SuppressKeyPress = true; outline.Focus(); } };
            outline.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && outline.SelectedNode != null && outline.SelectedNode.Tag is int) view.ScrollTo((int)outline.SelectedNode.Tag); };
            var outlineBox = new Panel();
            outlineBox.Controls.Add(outline); outlineBox.Controls.Add(outlineFilter);
            side = new ViewStack("md.side") { Dock = DockStyle.Fill };
            side.Add("outline", "Outline", outlineBox, 2.2);
            side.Add("backlinks", "Backlinks", backlinks, 1);
            side.Add("outgoing", "Outgoing links", outgoing, 1);
            side.Add("mentions", "Unlinked mentions", mentions, 0.8);
            side.Add("tags", "Tags", tags, 1);
            side.Restore();
            right.Controls.Add(side);

            // ---- middle: title row, reader / editor, search results ----
            // Breadcrumbs (vault > folder > note > heading you read); click: jump to a heading.
            title = new Label { Dock = DockStyle.Top, Height = Ui.S(26), Font = Ui.Small, UseMnemonic = false, AutoEllipsis = true, Padding = new Padding(Ui.S(8), 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
            title.Click += delegate { HeadingsMenu(); };
            Tip(title, "Click: jump to a heading of this note");
            // Tabs for open notes + editor actions on the right, like VS Code.
            var tabBar = new Panel { Dock = DockStyle.Top, Height = Ui.S(34) };
            tabs = new EditorTabs { Dock = DockStyle.Fill };
            tabs.SetAll(Store.Settings.MdTabs.Where(File.Exists));
            tabs.Activated += p => Open(p, true);
            tabs.AllClosed += p => { if (string.Equals(current, p, StringComparison.OrdinalIgnoreCase)) { Save(); current = null; ShowWelcome(); } };
            tabs.Changed += () => { Store.Settings.MdTabs = tabs.Paths.ToList(); Store.MarkSettings(); };
            var acts = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(Ui.S(4), Ui.S(1), 0, 0) };
            backBtn = Act("", "Back (Alt+Left)", GoBack); fwdBtn = Act("", "Forward (Alt+Right)", GoForward);
            modeRead = Act("", "Read (Ctrl+E switches)", () => SetMode("read")); modeEdit = Act("", "Edit (Ctrl+E switches)", () => SetMode("edit")); modeSplit = Act("", "Split: edit and read side by side (Ctrl+\\)", () => SetMode("split"));
            favBtn = Act("", "Favorite", () => { if (current != null) { MetaStore.ToggleFav("md", current); UpdateFav(); FillTree(); } });
            var asstBtn = Act("", "Assistant (Ctrl+J): give orders about this note", () => App.I.ToggleAssistant());
            IconButton more = null; more = Act("", "More: layout, export, print, send", () => MoreMenu(more));
            foreach (var b in new Control[] { backBtn, fwdBtn, modeRead, modeEdit, modeSplit, favBtn, asstBtn, more }) acts.Controls.Add(b);
            tabBar.Controls.Add(tabs); tabBar.Controls.Add(acts);
            info = new Label { Dock = DockStyle.Bottom, Height = Ui.S(22), ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
            view = new MdView { Dock = DockStyle.Fill };
            view.LinkClicked += OnLink;
            view.HeadingSeen += i =>
            {
                TreeNode n; if (!outlineNodes.TryGetValue(i, out n)) return;
                if (outline.SelectedNode != n) { outline.SelectedNode = n; n.EnsureVisible(); }
                headingNow = n.Text; title.Text = Crumbs();
            };
            view.PreviewFor = target =>
            {
                string name = target.Split('#')[0], heading = target.Contains("#") ? target.Substring(target.IndexOf('#') + 1) : "";
                string p = MdVault.Resolve(name);
                if (p == null) return Tuple.Create(name, "*Not created yet. Click to create it.*");
                string body = Regex.Replace((MdVault.TextOf(p) ?? "").Replace("\r", ""), "^---\n[\\s\\S]*?\n---\n", "");
                if (heading.Length > 0) body = MdWeb.Section(body, heading);
                if (body.Length > 1800) body = body.Substring(0, 1800) + "...";
                return Tuple.Create(Path.GetFileNameWithoutExtension(p) + (heading.Length > 0 ? " > " + heading : ""), body);
            };
            view.TextEdited += t => { editor.Text = t; };   // a tick box clicked in the reader: save it to the file
            editor = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AcceptsTab = true, Font = ChatView.Mono, BorderStyle = BorderStyle.None, MaxLength = 0, WordWrap = true };
            editor.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.V && PastePicture()) e.SuppressKeyPress = true; };
            var em = Ui.Menu(true);
            em.Items.Add("Cut", null, delegate { editor.Cut(); }); em.Items.Add("Copy", null, delegate { editor.Copy(); }); em.Items.Add("Paste", null, delegate { if (!PastePicture()) editor.Paste(); });
            em.Items.Add(new ToolStripSeparator());
            em.Items.Add("Extract the selection to a new note", null, delegate { ExtractSelection(); });
            em.Items.Add("Ask the Assistant about the selection", null, delegate { if (!Store.Settings.AssistantOpen) App.I.ToggleAssistant(); });
            em.Items.Add("Link the selection [[ ]]", null, delegate { if (editor.SelectionLength > 0) editor.SelectedText = "[[" + editor.SelectedText.Trim() + "]]"; });
            em.Items.Add("Highlight the selection == ==", null, delegate { if (editor.SelectionLength > 0) editor.SelectedText = "==" + editor.SelectedText + "=="; });
            Ui.ThemeMenu(em); editor.ContextMenuStrip = em;
            editor.TextChanged += delegate { if (loading) return; saveTimer.Stop(); saveTimer.Start(); info.Text = "Editing...  (saves by itself, Ctrl+S saves now)"; Ui.Debounce("md.preview", 350, () => { if (split.Panel2.Visible) Render(editor.Text); }); };
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
            split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = Ui.S(6), FixedPanel = FixedPanel.None };
            split.SizeChanged += delegate { PositionSplit(); };
            split.SizeChanged += delegate { if (Store.Settings.MdMode == "split") LayoutSplit(); };
            split.Panel1.Controls.Add(editor); split.Panel2.Controls.Add(view);
            results = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, Visible = false, BorderStyle = BorderStyle.None };
            results.Columns.Add("Note", Ui.S(220)); results.Columns.Add("Where it matches", Ui.S(520)); results.Columns.Add("Folder", Ui.S(260));
            results.DoubleClick += delegate { if (results.SelectedItems.Count > 0) Open((string)results.SelectedItems[0].Tag, true); };
            results.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && results.SelectedItems.Count > 0) Open((string)results.SelectedItems[0].Tag, true); };
            center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(4), Ui.S(4), Ui.S(4), 0) };
            center.Controls.Add(split); center.Controls.Add(results); center.Controls.Add(info); center.Controls.Add(title); center.Controls.Add(tabBar);

            Controls.Add(center); Controls.Add(right); Controls.Add(left); Controls.Add(bar);
            leftPane = left;
            PaneGrip.Attach(right, "md.side", Ui.S(220));
            if (right.Width < Ui.S(220)) right.Width = Ui.S(260);
            Ui.Theme(this);
            Ui.ThemeMenu(menu);
            FillVaults();
            SetMode(Store.Settings.MdMode ?? "read");
            ShowWelcome();
            MdVault.Indexed += OnVaultIndexed;
            Store.MetaChanged += OnMeta;
        }

        void OnVaultIndexed()
        {
            if (IsDisposed) return;
            refreshVaultOnShow = !Visible;
            if (refreshVaultOnShow) return;
            FillTree(); FillTags(); if (current != null) FillBacklinks();
        }
        void OnMeta() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke((Action)OnMeta); return; } Ui.Debounce("md.meta", 300, FillTree); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                MdVault.Indexed -= OnVaultIndexed; Store.MetaChanged -= OnMeta;
                backlinksRevision++; tagsRevision++; searchRevision++; openRevision++;
                Save(); saveTimer.Dispose(); menu.Dispose();
            }
            base.Dispose(disposing);
        }

        static readonly ToolTip tips = new ToolTip();
        static Control Tip(Control c, string text) { tips.SetToolTip(c, text); return c; }
        IconButton Act(string glyph, string tip, Action a)
        {
            var b = new IconButton(glyph, 10.5f, tip, tips, delegate { a(); }) { Height = Ui.S(32), Width = Ui.S(34), Margin = new Padding(0) };
            b.HoverColor = Color.FromArgb(40, Ui.Text);
            return b;
        }
        void NavState()
        {
            backBtn.Enabled = back.Count > 0; fwdBtn.Enabled = fwd.Count > 0;
            backBtn.ForeColor = backBtn.Enabled ? Ui.Text : Ui.Mix(Ui.Bg, Ui.Text, 0.35); fwdBtn.ForeColor = fwdBtn.Enabled ? Ui.Text : Ui.Mix(Ui.Bg, Ui.Text, 0.35);
        }
        string Crumbs()
        {
            if (current == null) return "";
            var parts = new List<string>();
            string root = MdVault.Current;
            if (!string.IsNullOrEmpty(root) && current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(Path.GetFileName(root.TrimEnd('\\')));
                string rel = Path.GetDirectoryName(current).Substring(Math.Min(root.TrimEnd('\\').Length, Path.GetDirectoryName(current).Length)).Trim('\\');
                if (rel.Length > 0) parts.AddRange(rel.Split('\\'));
            }
            else parts.Add(Path.GetFileName(Path.GetDirectoryName(current)));
            parts.Add(Path.GetFileNameWithoutExtension(current));
            if (headingNow.Length > 0) parts.Add(headingNow);
            return string.Join("  ›  ", parts.Where(x => !string.IsNullOrEmpty(x)));
        }
        void HeadingsMenu()
        {
            if (current == null || view.Headings.Count == 0) return;
            var m = Ui.Menu();
            foreach (var h in view.Headings.Take(80)) { int at = h.Item3; m.Items.Add(new string(' ', Math.Max(0, h.Item1 - 1) * 4) + h.Item2.Trim(), null, delegate { view.ScrollTo(at); }); }
            Ui.ThemeMenu(m); m.Show(title, new Point(Ui.S(8), title.Height));
        }
        void FillOutline()
        {
            outline.BeginUpdate(); outline.Nodes.Clear(); outlineNodes.Clear();
            string q = outlineFilter.Text.Trim();
            var stack = new List<KeyValuePair<int, TreeNode>>();
            foreach (var h in view.Headings)
            {
                var node = new TreeNode(h.Item2.Trim()) { Tag = h.Item3 };
                outlineNodes[h.Item3] = node;
                if (q.Length > 0) { if (h.Item2.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) outline.Nodes.Add(node); continue; }   // filtered: a flat list of matches
                while (stack.Count > 0 && stack[stack.Count - 1].Key >= h.Item1) stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0) outline.Nodes.Add(node); else stack[stack.Count - 1].Value.Nodes.Add(node);
                stack.Add(new KeyValuePair<int, TreeNode>(h.Item1, node));
            }
            outline.ExpandAll();
            if (outline.Nodes.Count > 0) outline.Nodes[0].EnsureVisible();
            outline.EndUpdate();
            side.SetCount("outline", view.Headings.Count);
        }
        void ToggleTree() { Store.Settings.MdTreeHidden = leftPane.Visible; Store.MarkSettings(); FitColumns(); }
        void ToggleSide() { Store.Settings.MdSideHidden = right.Visible; Store.MarkSettings(); FitColumns(); }
        bool zen;
        public void SetZen(bool on) { zen = on; FitColumns(); }
        void MoreMenu(Control anchor)
        {
            var m = Ui.Menu();
            m.Items.Add(new ToolStripMenuItem("Note tree", null, delegate { ToggleTree(); }) { Checked = leftPane.Visible, ShortcutKeyDisplayString = "Ctrl+B" });
            m.Items.Add(new ToolStripMenuItem("Side panel (outline, links, tags)", null, delegate { ToggleSide(); }) { Checked = right.Visible, ShortcutKeyDisplayString = "Ctrl+Shift+B" });
            m.Items.Add(new ToolStripMenuItem("Zen mode (only the note)", null, delegate { App.I.ToggleZen(); }) { ShortcutKeyDisplayString = "F11" });
            m.Items.Add(new ToolStripMenuItem("Assistant", null, delegate { App.I.ToggleAssistant(); }) { Checked = Store.Settings.AssistantOpen, ShortcutKeyDisplayString = "Ctrl+J" });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Graph view", null, delegate { if (MdVault.Current.Length == 0) { AddVault(); return; } new MdGraph(current, p => Open(p, true)).Show(FindForm()); });
            m.Items.Add("Export PDF", null, delegate { ExportPdf(); });
            m.Items.Add("Print", null, delegate { view.Print(); });
            if (current != null)
            {
                var send = AppLinks.SendTo(current, null, FindForm()); send.Text = "Send to"; m.Items.Add(send);
                m.Items.Add("Open with...", null, delegate { try { Process.Start(new ProcessStartInfo(current) { UseShellExecute = true }); } catch { } });
                m.Items.Add("Show in Explorer", null, delegate { App.I.OpenInExplorerPage(Path.GetDirectoryName(current)); });
                m.Items.Add("Copy path", null, delegate { App.I.CopyText(current); });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Close tab  (Ctrl+W)", null, delegate { tabs.CloseActive(); });
            m.Items.Add("Reopen closed tab  (Ctrl+Shift+T)", null, delegate { tabs.ReopenClosed(); });
            m.Items.Add("Reset layout", null, delegate { Store.Settings.MdTreeHidden = Store.Settings.MdSideHidden = false; Store.Settings.ViewStacks.Remove("md.side"); Store.MarkSettings(); App.I.Status("Layout reset: the side panel returns to its default sections the next time the page opens."); FitColumns(); });
            Ui.ThemeMenu(m); m.Show(anchor, new Point(0, anchor.Height));
        }
        static ListBox SideList() { return new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, Font = Ui.Base }; }
        static Control Section(string text, Control body, int weight)
        {
            var p = new Panel { Dock = DockStyle.Top, Height = Ui.S(weight == 2 ? 280 : weight == 1 ? 170 : 120), Padding = new Padding(0, 0, 0, Ui.S(8)) };
            p.Controls.Add(body);
            p.Controls.Add(new Label { Dock = DockStyle.Top, Height = Ui.S(24), Text = text.ToUpperInvariant(), Font = Ui.Small, ForeColor = Ui.Muted });
            return p;
        }
        class OutlineItem { public string Text; public int Y; public override string ToString() { return Text; } }
        class PathItem { public string Path, Label; public override string ToString() { return Label; } }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { Save(); return; }
            MdVault.Index(false);
            if (refreshVaultOnShow) OnVaultIndexed();
            if (current == null && !string.IsNullOrEmpty(Store.Settings.MdLast)) Open(Store.Settings.MdLast, false);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.O) || keyData == (Keys.Control | Keys.P)) { QuickSwitch(); return true; }
            if (keyData == (Keys.Control | Keys.B))
            {
                if (editor.Focused && editor.SelectionLength > 0) { editor.SelectedText = "**" + editor.SelectedText + "**"; return true; }
                ToggleTree(); return true;
            }
            if (keyData == (Keys.Control | Keys.Shift | Keys.B)) { ToggleSide(); return true; }
            if (keyData == (Keys.Control | Keys.W)) { tabs.CloseActive(); return true; }
            if (keyData == (Keys.Control | Keys.Tab)) { tabs.Next(1); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { tabs.Next(-1); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.T)) { tabs.ReopenClosed(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.O)) { if (Store.Settings.MdSideHidden) ToggleSide(); side.Reveal("outline"); outlineFilter.Focus(); outlineFilter.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.Oem5)) { SetMode(Store.Settings.MdMode == "split" ? "read" : "split"); return true; }
            if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { NewNote(null); return true; }
            if (keyData == (Keys.Control | Keys.E)) { SetMode(Store.Settings.MdMode == "edit" ? "read" : "edit"); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.F)) { FullSearch(Ui.Ask("Search in all notes", "Text or #tag", "", FindForm())); return true; }
            if (keyData == (Keys.Alt | Keys.Left)) { GoBack(); return true; }
            if (keyData == (Keys.Alt | Keys.Right)) { GoForward(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- vaults ----------
        void FillVaults()
        {
            loading = true;
            vault.Items.Clear();
            foreach (var v in MdVault.Vaults) vault.Items.Add("Vault: " + (Path.GetFileName(v.TrimEnd('\\')) is string && Path.GetFileName(v.TrimEnd('\\')).Length > 0 ? Path.GetFileName(v.TrimEnd('\\')) : v));
            vault.Items.Add("Add a vault folder...");
            vault.SelectedIndex = MdVault.Vaults.Count == 0 ? -1 : Math.Max(0, Math.Min(MdVault.Vaults.Count - 1, Store.Settings.MdVaultIndex));
            loading = false;
            MdVault.Index(false);
            FillTree();
        }

        void AddVault()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose a folder of Markdown notes (a vault). Sub-folders are included." })
                if (d.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    if (!MdVault.Vaults.Contains(d.SelectedPath, StringComparer.OrdinalIgnoreCase)) MdVault.Vaults.Add(d.SelectedPath);
                    Store.Settings.MdVaultIndex = MdVault.Vaults.FindIndex(v => string.Equals(v, d.SelectedPath, StringComparison.OrdinalIgnoreCase));
                    Store.MarkSettings();
                    MdVault.Index(true);
                }
            FillVaults();
        }

        void VaultMenu()
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            m.Items.Add("Add a vault folder...", null, delegate { AddVault(); });
            if (MdVault.Current.Length > 0)
            {
                m.Items.Add("Index the vault again", null, delegate { MdVault.Index(true); });
                m.Items.Add("Open the vault in Explorer", null, delegate { App.I.OpenInExplorerPage(MdVault.Current); });
                m.Items.Add("Remove this vault from the list (files stay)", null, delegate { MdVault.Vaults.RemoveAt(Math.Max(0, Math.Min(MdVault.Vaults.Count - 1, Store.Settings.MdVaultIndex))); Store.Settings.MdVaultIndex = 0; Store.MarkSettings(); FillVaults(); });
            }
            m.Show(Cursor.Position);
        }

        // ---------- tree: Favorites, Recent, then the folders ----------
        void FillTree()
        {
            if (IsDisposed) return;
            string root = MdVault.Current, q = filter.Text.Trim();
            var all = MdVault.Files;
            tree.BeginUpdate(); tree.Nodes.Clear();
            Func<string, TreeNode> fileNode = p => new TreeNode(Path.GetFileNameWithoutExtension(p)) { Name = "file", Tag = p, ToolTipText = p };
            var favs = MetaStore.Favorites("md").Select(m => m.Id).ToList();
            if (favs.Count > 0) { var n = tree.Nodes.Add("★  Favorites"); foreach (var p in favs) n.Nodes.Add(fileNode(p)); n.Expand(); }
            var rec = MetaStore.Recent("md", 12).Select(m => m.Id).Where(p => !favs.Contains(p)).ToList();
            if (rec.Count > 0) { var n = tree.Nodes.Add("Recent"); foreach (var p in rec) n.Nodes.Add(fileNode(p)); n.Expand(); }
            if (root.Length > 0)
            {
                var rootNode = tree.Nodes.Add(Path.GetFileName(root.TrimEnd('\\')) + (MdVault.Busy ? "   (indexing...)" : "   (" + all.Count + " notes)"));
                rootNode.Tag = root;
                var folders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase) { { root.TrimEnd('\\'), rootNode } };
                foreach (var f in all.Where(f => q.Length == 0 || Path.GetFileName(f).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(5000))
                {
                    string dir = Path.GetDirectoryName(f).TrimEnd('\\');
                    TreeNode parent = FolderNode(folders, dir, root.TrimEnd('\\'));
                    parent.Nodes.Add(fileNode(f));
                }
                rootNode.Expand();
                if (q.Length > 0) rootNode.ExpandAll();
            }
            else tree.Nodes.Add("Add a vault: Vaults... > Add a vault folder");
            tree.EndUpdate();
            SelectCurrentInTree();
        }

        static TreeNode FolderNode(Dictionary<string, TreeNode> folders, string dir, string root)
        {
            TreeNode n;
            if (folders.TryGetValue(dir, out n)) return n;
            string parent = Path.GetDirectoryName(dir);
            var p = parent == null || dir.Length <= root.Length ? folders[root] : FolderNode(folders, parent.TrimEnd('\\'), root);
            n = p.Nodes.Add(Path.GetFileName(dir)); n.Tag = dir;
            folders[dir] = n;
            return n;
        }

        void SelectCurrentInTree()
        {
            if (current == null) return;
            foreach (TreeNode r in tree.Nodes) { var hit = Find(r, current); if (hit != null) { tree.SelectedNode = hit; return; } }
        }
        static TreeNode Find(TreeNode n, string path) { if (n.Name == "file" && string.Equals((string)n.Tag, path, StringComparison.OrdinalIgnoreCase)) return n; foreach (TreeNode c in n.Nodes) { var r = Find(c, path); if (r != null) return r; } return null; }

        void NodeMenu(TreeNode node, Point at)
        {
            string p = node.Tag as string; if (p == null) return;
            menu.Items.Clear();
            if (node.Name == "file")
            {
                menu.Items.Add("Open", null, delegate { Open(p, true); });
                menu.Items.Add(MetaStore.Get("md", p) != null && MetaStore.Get("md", p).Fav ? "Remove from favorites" : "Add to favorites", null, delegate { MetaStore.ToggleFav("md", p); });
                menu.Items.Add("Rename...", null, delegate { Rename(p); });
                menu.Items.Add("Copy [[link]]", null, delegate { App.I.CopyText("[[" + Path.GetFileNameWithoutExtension(p) + "]]"); });
                menu.Items.Add("Copy path", null, delegate { App.I.CopyText(p); });
                menu.Items.Add(AppLinks.SendTo(p, null, FindForm()));
                menu.Items.Add("Show in Explorer", null, delegate { App.I.OpenInExplorerPage(Path.GetDirectoryName(p)); });
                menu.Items.Add("Delete (Recycle Bin)", null, delegate { Delete(p); });
            }
            else
            {
                menu.Items.Add("New note here", null, delegate { NewNote(p); });
                menu.Items.Add("Show in Explorer", null, delegate { App.I.OpenInExplorerPage(p); });
            }
            menu.Show(tree, at);
        }

        // ---------- open / navigate ----------
        public string CurrentPath { get { return current; } }
        public void AppendText(string s) { if (current == null || string.IsNullOrWhiteSpace(s)) return; if (Store.Settings.MdMode == "read") SetMode("split"); editor.AppendText((editor.Text.EndsWith("\n") ? "\n" : "\n\n") + s.Trim() + "\n"); info.Text = "Answer added to the note."; }
        public string SelectedText { get { return editor.Visible && editor.SelectionLength > 0 ? editor.SelectedText : ""; } }
        public void Open(string path, bool record)
        {
            RequestOpen(path, record, null);
        }

        void RequestOpen(string path, bool record, Action onSuccess)
        {
            if (string.IsNullOrEmpty(path)) return;
            int revision = ++openRevision;
            searchRevision++;
            info.Text = "Opening note…";
            string previousBuffer = loadedText;
            Control dispatch = App.I != null ? App.I.Sync : this;
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (revision != Volatile.Read(ref openRevision)) return;
                string text = MdVault.Read(path);
                DateTime modified = DateTime.MinValue;
                if (text != null) try { modified = File.GetLastWriteTime(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { dispatch.BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || revision != openRevision) return;
                    if (text == null) { info.Text = "Could not open: " + path; return; }
                    bool preserveBuffer = string.Equals(current, path, StringComparison.OrdinalIgnoreCase) &&
                        (editor.Text != loadedText || loadedText != previousBuffer);
                    if (!Save()) return;
                    if (onSuccess != null) onSuccess();
                    ShowNote(path, preserveBuffer ? loadedText : text, modified, record);
                })); } catch (InvalidOperationException) { }
            });
        }

        void ShowNote(string path, string text, DateTime modified, bool record)
        {
            if (record && current != null && !string.Equals(current, path, StringComparison.OrdinalIgnoreCase)) { back.Add(current); fwd.Clear(); }
            current = path;
            Store.Settings.MdLast = path; Store.MarkSettings();
            MetaStore.Touch("md", path);
            loading = true; editor.Text = text; loading = false; loadedText = text;
            results.Visible = false; split.Visible = true;
            headingNow = ""; tabs.Show(path);
            title.Text = Crumbs();
            int words = WordCount(text);
            info.Text = words.ToString("N0") + " words, " + Math.Max(1, (int)Math.Round(words / 220.0)) + " min read   ·   " + text.Length.ToString("N0") + " characters" + (modified == DateTime.MinValue ? "" : "   ·   " + modified.ToString("yyyy-MM-dd HH:mm"));
            tips.SetToolTip(info, path);
            Render(text);
            FillBacklinks();
            FillOutgoing(text);
            UpdateFav();
            NavState();
            SelectCurrentInTree();
        }

        void GoBack() { if (back.Count > 0) RequestOpen(back[back.Count - 1], false, () => { back.RemoveAt(back.Count - 1); if (current != null) fwd.Add(current); }); }
        void GoForward() { if (fwd.Count > 0) RequestOpen(fwd[fwd.Count - 1], false, () => { fwd.RemoveAt(fwd.Count - 1); if (current != null) back.Add(current); }); }

        void Render(string text)
        {
            view.SetText(text, current);
            FillOutline();
        }

        void UpdateFav() { var m = current == null ? null : MetaStore.Get("md", current); bool on = m != null && m.Fav; favBtn.Text = on ? "" : ""; favBtn.ForeColor = on ? Ui.Accent : Ui.Text; tips.SetToolTip(favBtn, on ? "Favorite (click to remove)" : "Add to favorites"); }

        void OnLink(string url)
        {
            if (url.StartsWith("wiki:"))
            {
                string target = url.Substring(5), heading = "";
                int hash = target.IndexOf('#'); if (hash >= 0) { heading = target.Substring(hash + 1).Trim(); target = target.Substring(0, hash); }
                var p = MdVault.Resolve(target);
                if (p != null)
                {
                    if (!string.Equals(p, current, StringComparison.OrdinalIgnoreCase)) Open(p, true);
                    if (heading.Length > 0) Ui.Later(700, () => { var h = view.Headings.FirstOrDefault(x => string.Equals(x.Item2.Trim(), heading, StringComparison.OrdinalIgnoreCase)); if (h != null) view.ScrollTo(h.Item3); });
                }
                else if (MessageBox.Show(FindForm(), "The note \"" + target + "\" does not exist yet. Create it?", "Markdown notes", MessageBoxButtons.OKCancel) == DialogResult.OK) Create(Path.Combine(current != null ? Path.GetDirectoryName(current) : MdVault.Current, Safe(target) + ".md"), "# " + target + "\n\n");
                return;
            }
            if (url.StartsWith("tag:")) { FullSearch("#" + url.Substring(4)); return; }
            if (!Regex.IsMatch(url, "^[a-z]+:", RegexOptions.IgnoreCase) && current != null)
            {
                // A relative link to another note or file.
                string rel = Uri.UnescapeDataString(url.Split('#')[0]).Replace('/', '\\');
                string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current), rel));
                if (full.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) { Open(full, true); return; }
            }
            Md.Open(url);
        }

        // ---------- modes ----------
        void SetMode(string mode)
        {
            if (mode != "edit" && mode != "split") mode = "read";
            if (Store.Settings.MdMode != mode) { Store.Settings.MdMode = mode; Store.MarkSettings(); }
            if (mode == "split") LayoutSplit();
            else
            {
                splitPositioned = false;
                if (mode == "edit") { split.Panel1Collapsed = false; split.Panel2Collapsed = true; }
                else { split.Panel2Collapsed = false; split.Panel1Collapsed = true; }
            }
            if (mode != "edit" && current != null) Render(editor.Text);
            foreach (var b in new[] { modeRead, modeEdit, modeSplit }) { b.ForeColor = Ui.Text; b.HoverColor = Color.FromArgb(40, Ui.Text); }
            (mode == "edit" ? modeEdit : mode == "split" ? modeSplit : modeRead).ForeColor = Ui.Accent;
            if (mode != "read") editor.Focus();
        }

        void LayoutSplit()
        {
            int available = split.ClientSize.Width - split.SplitterWidth;
            if (available < split.Panel1MinSize + split.Panel2MinSize)
            {
                splitPositioned = false;
                split.Panel1Collapsed = false;
                split.Panel2Collapsed = true;
                return;
            }
            split.Panel1Collapsed = false;
            split.Panel2Collapsed = false;
            PositionSplit();
        }

        // Halfway split, once the container has a real width (a saved "split" mode opens the page before layout).
        void PositionSplit()
        {
            if (splitPositioned || split.Panel1Collapsed || split.Panel2Collapsed) return;
            int lo = split.Panel1MinSize, hi = split.Width - split.Panel2MinSize - split.SplitterWidth;
            if (hi <= lo) return;
            splitPositioned = true;
            split.SplitterDistance = Math.Max(lo, Math.Min(split.Width / 2, hi));
        }

        // ---------- import: files, folders, drag and drop ----------
        void ImportMenu(Control anchor)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            m.Items.Add("Open Markdown files...", null, delegate
            {
                using (var d = new OpenFileDialog { Filter = "Markdown and text|*.md;*.markdown;*.mdx;*.mdown;*.txt|All files|*.*", Multiselect = true })
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) ImportPaths(d.FileNames);
            });
            m.Items.Add("Add a folder as a vault...", null, delegate { AddVault(); });
            m.Items.Add("Copy files into this vault...", null, delegate
            {
                if (MdVault.Current.Length == 0) { AddVault(); return; }
                using (var d = new OpenFileDialog { Filter = "Markdown and text|*.md;*.markdown;*.mdx;*.mdown;*.txt|All files|*.*", Multiselect = true })
                {
                    if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                    string target = Path.Combine(MdVault.Current, "Imported"); Directory.CreateDirectory(target);
                    string first = null; int n = 0;
                    foreach (var f in d.FileNames)
                    {
                        string name = Path.GetFileNameWithoutExtension(f) + ".md", to = Path.Combine(target, name);
                        for (int k = 2; File.Exists(to); k++) to = Path.Combine(target, Path.GetFileNameWithoutExtension(f) + " (" + k + ").md");
                        try { File.Copy(f, to); n++; if (first == null) first = to; } catch (Exception ex) { App.Log("Markdown import: " + ex.Message); }
                    }
                    MdVault.Index(true);
                    info.Text = n + " file(s) copied to " + target;
                    if (first != null) Open(first, true);
                }
            });
            m.Show(anchor, new Point(0, anchor.Height));
        }

        // Dropped or chosen paths: folders become vaults, files open (from anywhere, no copy needed).
        void ImportPaths(string[] paths)
        {
            string first = null; bool vaults = false;
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    if (!MdVault.Vaults.Contains(p, StringComparer.OrdinalIgnoreCase)) MdVault.Vaults.Add(p);
                    Store.Settings.MdVaultIndex = MdVault.Vaults.FindIndex(v => string.Equals(v, p, StringComparison.OrdinalIgnoreCase));
                    Store.MarkSettings(); vaults = true;
                }
                else if (File.Exists(p) && first == null) first = p;
            }
            if (vaults) { MdVault.Index(true); FillVaults(); }
            if (first != null) Open(first, true);
        }

        // ---------- files ----------
        bool Save()
        {
            saveTimer.Stop();
            if (current == null || editor.Text == loadedText) return true;
            try { File.WriteAllText(current, editor.Text, new UTF8Encoding(false)); loadedText = editor.Text; info.Text = "Saved " + DateTime.Now.ToString("HH:mm:ss") + "   ·   " + current; return true; }
            catch (Exception ex) { info.Text = "Could not save: " + ex.Message; return false; }
        }

        static string Safe(string name) { foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-'); return name.Trim(); }

        void Create(string path, string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) File.WriteAllText(path, text, new UTF8Encoding(false));
                MdVault.Index(true);
                Open(path, true);
                if (Store.Settings.MdMode == "read") SetMode("split");
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Could not create the note: " + ex.Message, "Markdown notes"); }
        }

        // ---------- templates ----------
        static readonly string[][] BuiltIn =
        {
            new[] { "Meeting notes", "# {{title}}\n\n**Date:** {{date}} {{time}}  \n**People:** \n\n## Agenda\n- \n\n## Notes\n\n## Decisions\n- \n\n## Actions\n- [ ] \n" },
            new[] { "Project", "---\nstatus: active\nstarted: {{date}}\n---\n\n# {{title}}\n\n## Goal\n\n## Plan\n- [ ] \n\n## Risks\n\n## Links\n" },
            new[] { "Decision record", "# {{title}}\n\n**Date:** {{date}}\n\n## Context\n\n## Options\n1. \n2. \n\n## Decision\n\n## Why\n" },
            new[] { "Weekly review", "# Week of {{date}}\n\n## Done\n- \n\n## Next week\n- [ ] \n\n## Problems\n\n## Notes\n" },
            new[] { "Report", "# {{title}}\n\n**Date:** {{date}}\n\n## Summary\n\n## Numbers\n| Item | Value | Change |\n|---|---|---|\n|  |  |  |\n\n## Findings\n\n## Next steps\n- [ ] \n" },
        };

        void TemplateMenu(Control anchor)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            foreach (var t in BuiltIn) { var tt = t; m.Items.Add(tt[0], null, delegate { NewFromTemplate(tt[0], tt[1]); }); }
            string dir = MdVault.Current.Length > 0 ? Path.Combine(MdVault.Current, "_templates") : null;
            if (dir != null && Directory.Exists(dir))
            {
                var mine = Directory.GetFiles(dir, "*.md");
                if (mine.Length > 0) m.Items.Add(new ToolStripSeparator());
                foreach (var f in mine) { string file = f; m.Items.Add(Path.GetFileNameWithoutExtension(file), null, delegate { NewFromTemplate(Path.GetFileNameWithoutExtension(file), MdVault.Read(file) ?? ""); }); }
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Open the templates folder", null, delegate
            {
                if (MdVault.Current.Length == 0) { AddVault(); return; }
                Directory.CreateDirectory(dir); App.I.OpenInExplorerPage(dir);
            });
            m.Show(anchor, new Point(0, anchor.Height));
        }

        void NewFromTemplate(string kind, string body)
        {
            string folder = current != null ? Path.GetDirectoryName(current) : MdVault.Current;
            if (string.IsNullOrEmpty(folder)) { AddVault(); return; }
            var name = Ui.Ask("New " + kind.ToLowerInvariant(), "Name", kind + " " + DateTime.Now.ToString("yyyy-MM-dd"), FindForm());
            if (string.IsNullOrWhiteSpace(name)) return;
            string text = body.Replace("{{title}}", name.Trim()).Replace("{{date}}", DateTime.Now.ToString("yyyy-MM-dd")).Replace("{{time}}", DateTime.Now.ToString("HH:mm"));
            Create(Path.Combine(folder, Safe(name) + ".md"), text);
        }

        void ExportPdf()
        {
            if (current == null) return;
            using (var d = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = Path.GetFileNameWithoutExtension(current) + ".pdf" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                if (Store.Settings.MdMode == "edit") SetMode("read");
                info.Text = "Saving PDF...";
                string file = d.FileName;
                view.ExportPdf(file, ok => { info.Text = ok ? "Saved " + file : "Could not save the PDF (the web reader is needed)."; });
            }
        }

        void NewNote(string folder)
        {
            string dir = folder ?? (current != null ? Path.GetDirectoryName(current) : MdVault.Current);
            if (string.IsNullOrEmpty(dir)) { AddVault(); return; }
            var name = Ui.Ask("New note", "Name", "Untitled " + DateTime.Now.ToString("yyyy-MM-dd HHmm"), FindForm());
            if (string.IsNullOrWhiteSpace(name)) return;
            Create(Path.Combine(dir, Safe(name) + ".md"), "# " + name.Trim() + "\n\n");
        }

        void DailyNote()
        {
            if (MdVault.Current.Length == 0) { AddVault(); return; }
            string name = DateTime.Today.ToString("yyyy-MM-dd");
            string existing = MdVault.Resolve(name);
            if (existing != null) { Open(existing, true); return; }
            Create(Path.Combine(MdVault.Current, "Daily", name + ".md"), "# " + DateTime.Today.ToString("dddd, d MMMM yyyy") + "\n\n## Tasks\n- [ ] \n\n## Notes\n\n");
        }

        void Rename(string p)
        {
            var name = Ui.Ask("Rename note", "Name", Path.GetFileNameWithoutExtension(p), FindForm());
            if (string.IsNullOrWhiteSpace(name)) return;
            string to = Path.Combine(Path.GetDirectoryName(p), Safe(name) + ".md");
            try
            {
                if (!Save()) return;
                File.Move(p, to);
                tabs.Rename(p, to);
                if (string.Equals(current, p, StringComparison.OrdinalIgnoreCase)) current = to;
                int notes = UpdateLinks(Path.GetFileNameWithoutExtension(p), Path.GetFileNameWithoutExtension(to));
                if (notes > 0) info.Text = "Renamed; links updated in " + notes + " note(s).";
                MdVault.Index(true); Open(to, false);
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Could not rename: " + ex.Message, "Markdown notes"); }
        }

        // Rename: [[old]], [[old|x]], [[old#h]] and (old.md) links in every note of the vault point to the new name.
        int UpdateLinks(string oldName, string newName)
        {
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) return 0;
            var wiki = new Regex(@"\[\[" + Regex.Escape(oldName) + @"(?=[\]|#])", RegexOptions.IgnoreCase);
            var md = new Regex(@"\]\(([^)]*?)" + Regex.Escape(Uri.EscapeDataString(oldName)) + @"\.md", RegexOptions.IgnoreCase);
            var md2 = new Regex(@"\]\(([^)]*?)" + Regex.Escape(oldName) + @"\.md", RegexOptions.IgnoreCase);
            int changed = 0;
            foreach (var f in MdVault.Files)
            {
                string t = MdVault.Read(f); if (t == null) continue;
                string n = wiki.Replace(t, "[[" + newName);
                n = md.Replace(n, m => "](" + m.Groups[1].Value + Uri.EscapeDataString(newName) + ".md");
                n = md2.Replace(n, m => "](" + m.Groups[1].Value + newName + ".md");
                if (n != t) { try { File.WriteAllText(f, n, new UTF8Encoding(false)); changed++; } catch (Exception ex) { App.Log("Rename links: " + ex.Message); } }
            }
            return changed;
        }

        void Delete(string p)
        {
            if (MessageBox.Show(FindForm(), "Move \"" + Path.GetFileName(p) + "\" to the Recycle Bin?", "Markdown notes", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            try
            {
                openRevision++;
                if (!Leftovers.Recycle(p)) throw new IOException("the file could not be moved to the Recycle Bin");
                tabs.Close(p);
            if (string.Equals(current, p, StringComparison.OrdinalIgnoreCase)) { current = null; ShowWelcome(); }
                MdVault.Index(true);
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), "Could not delete: " + ex.Message, "Markdown notes"); }
        }

        void ShowWelcome()
        {
            if (current != null) return;
            title.Text = MdVault.Current.Length == 0 ? "Markdown notes" : "Pick a note on the left";
            view.SetText(MdVault.Current.Length == 0
                ? "# Your Markdown notes\n\nAdd a **vault**: any folder with `.md` files (sub-folders included). Use **Vaults... > Add a vault folder**.\n\n- **Read**, **Edit** or **Split** view, saves by itself\n- `[[Note name]]` links between notes, **backlinks** on the right\n- `#tags`, `- [ ]` tasks, tables, code\n- **Ctrl+O** switch note, **Ctrl+N** new note, **Ctrl+Shift+F** search in all notes, **Alt+Left / Right** back and forward\n- ★ favorites and recent notes on the left"
                : "# " + Path.GetFileName(MdVault.Current.TrimEnd('\\')) + "\n\nChoose a note on the left, press **Ctrl+O** to find one by name, or **Today** for today's daily note.", null);
        }

        // ---------- backlinks, tags, search ----------
        // Links from this note to others ([[x]] and relative .md links); missing ones are shown too (click creates).
        // A picture on the clipboard goes into the vault (attachments folder) and the note gets ![[name]].
        bool PastePicture()
        {
            if (current == null || !Clipboard.ContainsImage()) return false;
            try
            {
                string root = MdVault.Current.Length > 0 && current.StartsWith(MdVault.Current, StringComparison.OrdinalIgnoreCase) ? MdVault.Current : Path.GetDirectoryName(current);
                string dir = Path.Combine(root, "attachments"); Directory.CreateDirectory(dir);
                string name = "Pasted image " + DateTime.Now.ToString("yyyyMMddHHmmss") + ".png";
                using (var img = Clipboard.GetImage()) img.Save(Path.Combine(dir, name), System.Drawing.Imaging.ImageFormat.Png);
                editor.SelectedText = "![[" + name + "]]";
                info.Text = "Picture saved to " + Path.Combine(dir, name);
                return true;
            }
            catch (Exception ex) { info.Text = "Could not paste the picture: " + ex.Message; return false; }
        }

        // Selection -> its own note; the selection becomes a [[link]] to it.
        void ExtractSelection()
        {
            if (current == null || editor.SelectionLength == 0) return;
            string sel = editor.SelectedText;
            string first = Regex.Replace(sel.Trim().Split('\n')[0], "^[#>\\-*\\s]+", "").Trim();
            var name = Ui.Ask("Extract to a new note", "Name", first.Length > 60 ? first.Substring(0, 60) : first, FindForm());
            if (string.IsNullOrWhiteSpace(name)) return;
            string file = Path.Combine(Path.GetDirectoryName(current), Safe(name) + ".md");
            if (File.Exists(file)) { MessageBox.Show(FindForm(), "A note with that name already exists.", "Markdown notes"); return; }
            File.WriteAllText(file, sel.Trim() + "\n", new UTF8Encoding(false));
            editor.SelectedText = "[[" + Safe(name) + "]]";
            Save(); MdVault.Index(true);
            info.Text = "Extracted to " + file;
        }

        static int WordCount(string t) { return string.IsNullOrEmpty(t) ? 0 : Regex.Matches(t, @"[\p{L}\p{N}][\p{L}\p{N}'-]*").Count; }

        void FillOutgoing(string text)
        {
            outgoing.BeginUpdate(); outgoing.Items.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(text ?? "", @"\[\[([^\]|#]+)"))
            {
                string n = m.Groups[1].Value.Trim(); if (!seen.Add(n)) continue;
                string p = MdVault.Resolve(n);
                outgoing.Items.Add(new PathItem { Path = p ?? "", Label = n + (p == null ? "   (not created)" : "") });
                if (outgoing.Items.Count >= 150) break;
            }
            side.SetCount("outgoing", outgoing.Items.Count);
            if (outgoing.Items.Count == 0) outgoing.Items.Add("(no links)");
            outgoing.EndUpdate();
        }

        void FillBacklinks()
        {
            int revision = ++backlinksRevision;
            backlinks.Items.Clear();
            if (current == null) return;
            string name = Path.GetFileNameWithoutExtension(current), me = current;
            var files = MdVault.Files;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var found = new List<PathItem>(); var plain = new List<PathItem>();
                var word = new Regex(@"(?<![\w\[])" + Regex.Escape(name) + @"(?![\w\]])", RegexOptions.IgnoreCase);
                var rx = new Regex(@"\[\[" + Regex.Escape(name) + @"(\|[^\]]*)?(#[^\]]*)?\]\]|\]\([^)]*" + Regex.Escape(Uri.EscapeDataString(name).Replace("%20", " ")) + @"(\.md)?[)#]", RegexOptions.IgnoreCase);
                foreach (var f in files)
                {
                    if (revision != Volatile.Read(ref backlinksRevision)) return;
                    if (f == me) continue;
                    var t = MdVault.Read(f);
                    if (t != null && rx.IsMatch(t)) found.Add(new PathItem { Path = f, Label = Path.GetFileNameWithoutExtension(f) });
                    else if (t != null && name.Length >= 3 && plain.Count < 100 && word.IsMatch(t)) plain.Add(new PathItem { Path = f, Label = Path.GetFileNameWithoutExtension(f) });   // mentions without a link
                    if (found.Count >= 200) break;
                }
                try { BeginInvoke((Action)delegate { if (IsDisposed || current != me || revision != backlinksRevision) return; backlinks.Items.Clear(); foreach (var b in found) backlinks.Items.Add(b); if (found.Count == 0) backlinks.Items.Add("(no notes link here)"); side.SetCount("backlinks", found.Count);
                    mentions.Items.Clear(); foreach (var b in plain) mentions.Items.Add(b); if (plain.Count == 0) mentions.Items.Add("(none)"); side.SetCount("mentions", plain.Count); }); } catch (InvalidOperationException) { }
            });
        }

        void FillTags()
        {
            int revision = ++tagsRevision;
            string root = MdVault.Current;
            var files = MdVault.Files;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var rx = new Regex(@"(?<![\w&/#])#([A-Za-z][\w\-/]{1,40})");
                foreach (var f in files.Take(4000))
                {
                    if (revision != Volatile.Read(ref tagsRevision)) return;
                    var t = MdVault.Read(f);
                    if (t == null) continue;
                    t = Regex.Replace(t, "```[\\s\\S]*?```", "");
                    foreach (Match m in rx.Matches(t)) { int c; counts.TryGetValue(m.Groups[1].Value, out c); counts[m.Groups[1].Value] = c + 1; }
                }
                var top = counts.OrderByDescending(x => x.Value).Take(300).Select(kv => (object)("#" + kv.Key + "   " + kv.Value)).ToArray();
                try { BeginInvoke((Action)delegate { if (IsDisposed || revision != tagsRevision || root != MdVault.Current) return; tags.BeginUpdate(); tags.Items.Clear(); tags.Items.AddRange(top); tags.EndUpdate(); side.SetCount("tags", top.Length); }); } catch (InvalidOperationException) { }
            });
        }

        void FullSearch(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return;
            q = q.Trim();
            openRevision++;
            int revision = ++searchRevision;
            string root = MdVault.Current;
            if (!Save()) return;
            split.Visible = false; results.Visible = true; results.Items.Clear();
            title.Text = "Search: " + q;
            info.Text = "Searching...";
            var files = MdVault.Files;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var hits = new List<ListViewItem>();
                foreach (var f in files)
                {
                    if (revision != Volatile.Read(ref searchRevision)) return;
                    var t = MdVault.Read(f); if (t == null) continue;
                    int i = t.IndexOf(q, StringComparison.OrdinalIgnoreCase);
                    bool nameHit = Path.GetFileNameWithoutExtension(f).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (i < 0 && !nameHit) continue;
                    string snip = i < 0 ? "(name)" : Ui.OneLine(t.Substring(Math.Max(0, i - 50), Math.Min(t.Length - Math.Max(0, i - 50), 160)), 160);
                    hits.Add(new ListViewItem(new[] { Path.GetFileNameWithoutExtension(f), snip, Path.GetDirectoryName(f) }) { Tag = f });
                    if (hits.Count >= 500) break;
                }
                try { BeginInvoke((Action)delegate { if (IsDisposed || revision != searchRevision || root != MdVault.Current) return; results.BeginUpdate(); results.Items.Clear(); results.Items.AddRange(hits.ToArray()); results.EndUpdate(); info.Text = hits.Count + " note(s) contain \"" + q + "\". Double-click or Enter opens one."; }); } catch (InvalidOperationException) { }
            });
        }

        void QuickSwitch()
        {
            var files = MdVault.Files;
            using (var f = new Form { Text = "Open a note", StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(Ui.S(640), Ui.S(420)), Font = Ui.Base, BackColor = Ui.Bg, ForeColor = Ui.Text, KeyPreview = true })
            {
                var box = new TextBox { Dock = DockStyle.Top, Font = Ui.Base };
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
                Action fill = () =>
                {
                    string q = box.Text.Trim();
                    var recent = MetaStore.Recent("md", 30).Select(m => m.Id).Where(File.Exists);
                    var pool = q.Length == 0 ? recent.Concat(files).Distinct() : files.Where(p => Path.GetFileNameWithoutExtension(p).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(p => Path.GetFileNameWithoutExtension(p).StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
                    list.BeginUpdate(); list.Items.Clear(); foreach (var p in pool.Take(200)) list.Items.Add(new PathItem { Path = p, Label = Path.GetFileNameWithoutExtension(p) + "      " + Path.GetDirectoryName(p) }); list.EndUpdate();
                    if (list.Items.Count > 0) list.SelectedIndex = 0;
                };
                string chosen = null;
                Action pick = () => { var it = list.SelectedItem as PathItem; if (it != null) { chosen = it.Path; f.Close(); } };
                box.TextChanged += delegate { fill(); };
                box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down && list.Items.Count > 0) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; } else if (e.KeyCode == Keys.Up && list.Items.Count > 0) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; } else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; pick(); } };
                list.DoubleClick += delegate { pick(); };
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(10)) }; pad.Controls.Add(list); pad.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(8) }); pad.Controls.Add(box);
                f.Controls.Add(pad); Ui.Theme(f); fill();
                f.Shown += delegate { box.Focus(); };
                f.ShowDialog(FindForm());
                if (chosen != null) Open(chosen, true);
            }
        }
    }

    // ---------- the formatted reader (scrolls, draws only what is on screen, clickable links) ----------
    class MdView : Panel
    {
        class Seg { public string Text; public bool Code, Front; public List<MdBlock> Blocks; public int Y, H; }
        readonly List<Seg> segs = new List<Seg>();
        readonly List<MdLink> links = new List<MdLink>();
        MdWeb web;
        public event Action<string> TextEdited;
        public event Action<int> HeadingSeen;   // the heading at the top of the reader while you scroll
        static readonly Regex HeadingLine = new Regex(@"^(#{1,6})\s+(.+?)\s*#*\s*$");
        string text = "";
        int laidFor = -1, contentH;
        public event Action<string> LinkClicked;
        public List<Tuple<int, string, int>> Headings = new List<Tuple<int, string, int>>();

        public MdView()
        {
            DoubleBuffered = true; AutoScroll = true; BackColor = Ui.Card;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            if (MdWeb.Available)
            {
                AutoScroll = false;
                web = new MdWeb();
                web.LinkClicked += u => { var h = LinkClicked; if (h != null) h(u); };
                web.HeadingSeen += i => { var h = HeadingSeen; if (h != null) h(i); };
                web.TaskToggled += (i, done) => { text = MdWeb.SetTask(text, i, done); var h = TextEdited; if (h != null) h(text); };
                Controls.Add(web);
            }
        }

        public void SetText(string t, string path)
        {
            if (web != null)
            {
                text = t ?? "";
                // Outline: headings outside code blocks; the index is what ScrollTo receives.
                Headings.Clear(); bool code = false; int n = 0;
                string body = Regex.Replace(text.Replace("\r", ""), "^---\n[\\s\\S]*?\n---\n", "");
                foreach (var line in body.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("```")) { code = !code; continue; }
                    var m = code ? Match.Empty : HeadingLine.Match(line);
                    if (m.Success) Headings.Add(Tuple.Create(m.Groups[1].Length, m.Groups[2].Value, n++));
                }
                web.Show(MdWeb.Expand(text, path), path);
                return;
            }
            text = t ?? ""; laidFor = -1;
            Layout0();
            AutoScrollPosition = Point.Empty;
            Invalidate();
        }

        public void Print() { if (web != null) web.Print(); }
        // Live text (the AI panel while an answer streams): no page reload, scrolled to the end.
        public void Stream(string t, string path) { if (web != null) { text = t ?? ""; web.Update(MdWeb.Expand(text, path), true); } else { SetText(t, path); AutoScrollPosition = new Point(0, AutoScrollMinSize.Height); } }
        public Func<string, Tuple<string, string>> PreviewFor { set { if (web != null) web.PreviewFor = value; } }
        public void ExportPdf(string file, Action<bool> done) { if (web != null) web.ExportPdf(file, done); else done(false); }
        public void ScrollTo(int y) { if (web != null) { web.ScrollToHeading(y); return; } AutoScrollPosition = new Point(0, Math.Max(0, y - Ui.S(10))); Invalidate(); }

        int Pad { get { return Math.Max(Ui.S(16), Math.Min(Ui.S(64), (ClientSize.Width - Ui.S(900)) / 2)); } }   // readable line length on wide windows

        void Layout0()
        {
            segs.Clear(); Headings.Clear(); links.Clear();
            int w = Math.Max(Ui.S(120), ClientSize.Width - Pad * 2);
            laidFor = ClientSize.Width;
            string body = text.Replace("\r", "");
            // front matter: --- ... --- at the top
            var fm = Regex.Match(body, "^---\n([\\s\\S]*?)\n---\n");
            if (fm.Success) { segs.Add(new Seg { Text = fm.Groups[1].Value, Code = true, Front = true }); body = body.Substring(fm.Length); }
            var sb = new StringBuilder(); bool code = false;
            foreach (var line in body.Split('\n'))
            {
                if (line.TrimStart().StartsWith("```")) { if (sb.Length > 0 || code) segs.Add(new Seg { Text = sb.ToString().TrimEnd('\n'), Code = code }); sb.Length = 0; code = !code; continue; }
                sb.Append(line).Append('\n');
            }
            if (sb.Length > 0) segs.Add(new Seg { Text = sb.ToString().TrimEnd('\n'), Code = code });
            using (var g = CreateGraphics())
            {
                int y = Ui.S(16);
                foreach (var s in segs)
                {
                    s.Y = y;
                    if (s.Code) s.H = TextRenderer.MeasureText(g, s.Text.Length == 0 ? " " : s.Text, ChatView.Mono, new Size(w - Ui.S(24), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + Ui.S(20);
                    else
                    {
                        s.Blocks = Md.Parse(s.Text);
                        s.H = Md.Layout(g, s.Blocks, w);
                        foreach (var b in s.Blocks.Where(b => b.Kind == "h")) Headings.Add(Tuple.Create(b.Level, string.Concat(b.Runs.Select(r => r.Text)), y + b.Y));
                    }
                    y += s.H + Ui.S(12);
                }
                contentH = y + Ui.S(24);
            }
            AutoScrollMinSize = new Size(0, contentH);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (web == null && ClientSize.Width != laidFor && ClientSize.Width > 0) Ui.Debounce("mdview.layout" + GetHashCode(), 80, () => { if (!IsDisposed) { Layout0(); Invalidate(); } }); }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            int oy = AutoScrollPosition.Y, x = Pad, w = Math.Max(Ui.S(120), ClientSize.Width - Pad * 2);
            var clip = e.ClipRectangle;
            links.RemoveAll(l => l.R.IntersectsWith(new Rectangle(clip.X, clip.Y - oy, clip.Width, clip.Height)));
            foreach (var s in segs)
            {
                int top = s.Y + oy;
                if (top > clip.Bottom) break;
                if (top + s.H < clip.Top) continue;
                if (s.Code)
                {
                    var r = new Rectangle(x, top, w, s.H);
                    using (var path = Ui.Round(r, Ui.S(8))) using (var b = new SolidBrush(Ui.Dark ? Color.FromArgb(24, 26, 32) : Color.FromArgb(244, 246, 250))) g.FillPath(b, path);
                    if (s.Front) TextRenderer.DrawText(g, "PROPERTIES", Ui.Small, new Rectangle(r.Right - Ui.S(110), r.Y + Ui.S(4), Ui.S(100), Ui.S(16)), Ui.Muted, TextFormatFlags.Right);
                    TextRenderer.DrawText(g, s.Text, ChatView.Mono, new Rectangle(x + Ui.S(12), top + Ui.S(10), w - Ui.S(24), s.H - Ui.S(14)), Ui.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                }
                else
                {
                    var fresh = new List<MdLink>();
                    Md.Draw(g, s.Blocks, x, top, w, clip, Ui.Text, fresh);
                    foreach (var l in fresh) { l.R.Offset(0, -oy); links.Add(l); }
                }
            }
        }

        MdLink LinkAt(Point p) { var q = new Point(p.X, p.Y - AutoScrollPosition.Y); return links.LastOrDefault(l => l.R.Contains(q)); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Cursor = LinkAt(e.Location) != null ? Cursors.Hand : Cursors.Default; }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Focus();
            if (e.Button == MouseButtons.Left) { var l = LinkAt(e.Location); if (l != null) { var h = LinkClicked; if (h != null) h(l.Url); } }
            else if (e.Button == MouseButtons.Right)
            {
                var m = Ui.Menu(); Ui.ThemeMenu(m);
                m.Items.Add("Copy all text", null, delegate { App.I.CopyText(text); });
                m.Show(this, e.Location);
            }
        }
    }
}
