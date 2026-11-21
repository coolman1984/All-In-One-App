using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    class MainForm : Form
    {
        public bool Exiting;
        readonly NavItem navNotes, navPrompts, navClips, navSettings, navSystem, navCards;
        readonly NavItem navHome;
        NotesPage notes;
        PromptsPage prompts;
        ClipsPage clips;
        SystemPage system;
        CardsPage cards;
        readonly NavItem navSnip, navActivity, navTagged, navKeys;
        readonly NavItem navTasks, navExplorer, navChrome, navPower;
        TasksPage tasks;
        ExplorerPage explorer;
        ChromePage chrome;
        ShortcutsPage shortcuts;
        readonly TopBar topBar;
        readonly QuickDock dock;
        readonly NavItem navTerminals;
        TerminalPage terminals;
        MarkdownPage markdown;
        NavItem navMarkdown;
        BrowserPage browser;
        DocumentsPage documents; NavItem navDocs;
        NavItem navBrowser;
        AccountsPage accountsPage;
        NavItem navAccounts;
        AgentsPage agents;
        readonly NavItem navAgents;

        Panel PageWith(string title, Control inner)
        {
            var p = new Panel { Dock = DockStyle.Fill, Visible = false };
            inner.Dock = DockStyle.Fill;
            p.Controls.Add(inner);
            var bar = Bar();
            bar.Controls.Add(Heading(title));
            var pop = Ui.Btn("Pop out", null);
            new ToolTip().SetToolTip(pop, "Open this page as a floating card");
            string fid = title == "Snip & record" ? "snip" : title == "File activity" ? "activity" : "tagged";
            pop.Click += delegate { App.I.NewCard(new List<string> { fid }, null); };
            pop.Margin = new Padding(0, Ui.S(4), 0, 0);
            bar.Controls.Add(pop);
            p.Controls.Add(bar);
            return p;
        }
        readonly Panel content;
        readonly Label status, keys;
        readonly Timer statusTimer = new Timer();
        readonly ToolTip navigationTips = new ToolTip();
        string page = "dashboard";
        public string Page { get { return page; } }

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = Ui.AppTitle;
            Icon = App.AppIcon;
            Font = Ui.Base;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(Ui.S(1120), work.Width), Math.Min(Ui.S(700), work.Height));
            MinimumSize = new Size(Math.Min(Ui.S(640), work.Width), Math.Min(Ui.S(400), work.Height));
            StartPosition = FormStartPosition.Manual;
            Location = new Point(work.Left + (work.Width - Width) / 2, work.Top + (work.Height - Height) / 2);
            KeyPreview = true;
            Ui.DarkTitle(this);

            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = Ui.Nexus ? new Padding(Ui.S(22), Ui.S(16), Ui.S(22), Ui.S(6)) : new Padding(Ui.S(12), Ui.S(10), Ui.S(12), Ui.S(4));
            Controls.Add(content);
            // The Assistant: one chat on the right of every page (Ctrl+J), created when first shown.
            assistantHost = new Panel { Dock = DockStyle.Right, Width = PaneGrip.Size("assistant", DockStyle.Right, Ui.S(420), Ui.S(1200)), Visible = false };
            Controls.Add(assistantHost);
            PaneGrip.Attach(assistantHost, "assistant", Ui.S(320));
            if (Store.Settings.AssistantOpen) Shown += delegate { SetAssistant(true); };

            status = new Label();
            status.Dock = DockStyle.Bottom;
            status.Height = Ui.S(26);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(Ui.S(14), 0, 0, 0);
            status.ForeColor = Ui.Muted;
            status.Font = Ui.Small;
            Controls.Add(status);
            statusTimer.Interval = 4000;
            statusTimer.Tick += delegate { statusTimer.Stop(); status.Text = ""; };
            dock = new QuickDock { Visible = Store.Settings.DockVisible };
            Controls.Add(dock);

            var nav = new Panel();
            navPanel = nav;
            nav.Dock = DockStyle.Left;
            nav.Width = Ui.S(230);
            nav.BackColor = Ui.Sidebar; nav.ForeColor = Ui.SidebarText;
            nav.Padding = new Padding(0, Ui.S(6), 0, Ui.S(6));
            if (Ui.Navy)
            {
                // Floating navy card: continues the rounded top drawn by the top bar, rounded at the bottom.
                nav.BackColor = Ui.Bg;
                nav.Padding = new Padding(Ui.S(12), Ui.S(4), Ui.S(2), Ui.S(26));
                nav.Resize += delegate { nav.Invalidate(); };
                nav.Paint += (s, e) =>
                {
                    var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    var r = new Rectangle(Ui.S(12), -Ui.S(40), nav.Width - Ui.S(14), nav.Height + Ui.S(40) - Ui.S(14));
                    Ui.Shadow(g, r, Ui.S(18));
                    using (var path = Ui.Round(r, Ui.S(18))) using (var b = new SolidBrush(Ui.Sidebar)) g.FillPath(b, path);
                };
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(nav, true, null);
            }
            var navigation = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = Ui.S(0, 336), BackColor = Ui.Sidebar };
            Ui.DarkScroll(navigation);
            nav.Controls.Add(navigation);

            keys = new Label();
            UpdateShortcutHelp();

            navSettings = new NavItem(Ui.GSettings, "Settings");
            navSettings.Dock = DockStyle.Bottom;
            navSettings.Click += delegate { App.I.ShowSettings(this); };
            nav.Controls.Add(navSettings);
            navKeys = new NavItem("", "Shortcuts");
            navKeys.Dock = DockStyle.Bottom;
            navKeys.Click += delegate { ShowPage("shortcuts"); };
            nav.Controls.Add(navKeys);

            navSnip = new NavItem("", "Snip & record");
            navActivity = new NavItem("", "File activity");
            navTagged = new NavItem("", "Tagged files");
            navSnip.Click += delegate { ShowPage("snip"); };
            navActivity.Click += delegate { ShowPage("activity"); };
            navTagged.Click += delegate { ShowPage("tagged"); };
            navPower = new NavItem("\uE945", "Power tools");
            navPower.Click += delegate { ShowPage("power"); };

            navClips = new NavItem(Ui.GClip, "Clipboard history");
            navPrompts = new NavItem(Ui.GPrompt, "Prompts & snippets");
            navNotes = new NavItem(Ui.GNote, "Sticky notes");
            navSystem = new NavItem("", "System monitor");
            navCards = new NavItem("", "Floating cards");
            navClips.Click += delegate { ShowPage("clips"); };
            navPrompts.Click += delegate { ShowPage("prompts"); };
            navNotes.Click += delegate { ShowPage("notes"); };
            navSystem.Click += delegate { ShowPage("system"); };
            navCards.Click += delegate { ShowPage("cards"); };
            navChrome = new NavItem("\uE774", "Chrome bookmarks");
            navChrome.Click += delegate { ShowPage("chrome"); };
            navExplorer = new NavItem("\uEC50", "Explorer");
            navExplorer.Click += delegate { ShowPage("explorer"); };
            navTasks = new NavItem("\uE73A", "Tasks & reminders");
            navTasks.Click += delegate { ShowPage("tasks"); };
            navHome = new NavItem("", "Home");
            navHome.Click += delegate { ShowPage("dashboard"); };
            navigation.Controls.Add(navHome);
            navTerminals = new NavItem("", "Terminals");
            navTerminals.Click += delegate { ShowPage("terminals"); };
            navMarkdown = new NavItem("", "Markdown notes");
            navMarkdown.Click += delegate { ShowPage("markdown"); };
            navBrowser = new NavItem("", "Browser");
            navBrowser.Click += delegate { ShowPage("browser"); };
            navDocs = new NavItem("", "Documents");
            navDocs.Click += delegate { ShowPage("documents"); };
            navAccounts = new NavItem("", "Accounts");
            navAccounts.Click += delegate { ShowPage("accounts"); };
            navAgents = new NavItem("", "Agents");
            navAgents.Click += delegate { ShowPage("agents"); };
            // Sidebar pages: your names, order, favorites and hidden pages come from Settings (right-click a page).
            navList = navigation;
            AddNavPage("dashboard", navHome, "General"); AddNavPage("tasks", navTasks, "General"); AddNavPage("notes", navNotes, "General");
            AddNavPage("prompts", navPrompts, "General"); AddNavPage("clips", navClips, "General");
            AddNavPage("agents", navAgents, "Tools"); AddNavPage("terminals", navTerminals, "Tools"); AddNavPage("explorer", navExplorer, "Tools"); AddNavPage("markdown", navMarkdown, "Tools"); AddNavPage("browser", navBrowser, "Tools"); AddNavPage("documents", navDocs, "Tools"); AddNavPage("accounts", navAccounts, "Tools");
            AddNavPage("snip", navSnip, "Tools"); AddNavPage("chrome", navChrome, "Tools"); AddNavPage("power", navPower, "Tools");
            AddNavPage("system", navSystem, "System"); AddNavPage("cards", navCards, "System"); AddNavPage("activity", navActivity, "System"); AddNavPage("tagged", navTagged, "System");
            BuildNav();
            if (Ui.Nexus) { var support = new NavGroup("Support") { Dock = DockStyle.Bottom }; nav.Controls.Add(support); nav.Controls.SetChildIndex(support, 1); }

            Controls.Add(nav);
            topBar = new TopBar(() => nav.Width);
            Controls.Add(topBar);
            var navGrip = PaneGrip.Attach(nav, "nav", Ui.S(56));
            // Sidebar: drag its edge to any width (narrow = icons only). Small windows fold it automatically.
            // Round edge buttons: fold the sidebar to icons / hide the quick launch bar, so pages get the space.
            navToggle = new EdgeToggle(); dockToggle = new EdgeToggle();
            Controls.Add(navToggle); Controls.Add(dockToggle);
            navToggle.Click += delegate { Store.Settings.PaneSizes["nav.collapsed"] = NavCollapsed ? 0 : 1; Store.MarkSettings(); AdaptNav(nav); };
            dockToggle.Click += delegate { Store.Settings.DockVisible = !dock.Visible; Store.MarkSettings(); SetDockVisible(Store.Settings.DockVisible); };
            EventHandler place = delegate { PlaceToggles(nav); };
            nav.SizeChanged += place; dock.VisibleChanged += place; dock.SizeChanged += place; topBar.SizeChanged += place; Resize += place;
            EventHandler adaptNav = delegate { AdaptNav(nav); };
            nav.SizeChanged += delegate { topBar.Invalidate(); topBar.PerformLayout(); };
            navGrip.BackColor = Ui.Navy ? Ui.Bg : Ui.Sidebar;
            if (Ui.Nexus && !Ui.Navy) navGrip.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, 0, navGrip.Height); };
            Resize += adaptNav; adaptNav(this, EventArgs.Empty);

            Ui.Theme(this);
            navigationTips.SetToolTip(navSettings, "Settings"); navigationTips.SetToolTip(navKeys, "Shortcuts");
            Store.NotesChanged += OnNotes;
            Store.PromptsChanged += OnPrompts;
            Store.ClipsChanged += OnClips;
            ShowPage("dashboard");
        }

        readonly EdgeToggle navToggle, dockToggle;
        Panel navPanel;

        // ---------- Sidebar pages ----------
        public class NavPage { public string Key, Glyph, Name, Group; public NavItem Item; }
        readonly List<NavPage> navPages = new List<NavPage>();
        Panel navList;
        public List<NavPage> NavPages { get { return navPages; } }

        void AddNavPage(string key, NavItem item, string group)
        {
            var p = new NavPage { Key = key, Glyph = item.Glyph, Name = item.Text, Group = group, Item = item };
            navPages.Add(p);
            item.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) NavMenu(p, item.PointToScreen(e.Location)); };
            // Drop files or folders on a page in the sidebar to hand them to that page.
            if (DropTargets.Contains(key))
            {
                item.AllowDrop = true;
                item.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effect = DragDropEffects.Copy; item.Selected = true; } };
                item.DragLeave += delegate { item.Selected = Page == key; };
                item.DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; item.Selected = Page == key; if (files != null && files.Length > 0) DropOn(key, files); };
            }
        }

        static readonly string[] DropTargets = { "explorer", "terminals", "agents", "browser", "documents", "markdown", "tagged" };
        void DropOn(string key, string[] paths)
        {
            string first = paths[0];
            bool folder = System.IO.Directory.Exists(first);
            string dir = folder ? first : System.IO.Path.GetDirectoryName(first);
            switch (key)
            {
                case "explorer": App.I.OpenInExplorerPage(dir); break;
                case "terminals": App.I.AddTerminalFolder(dir); break;
                case "agents":
                    if (folder) App.I.AddAgentFolder(first);
                    else App.I.SendToAgents("Please look at " + (paths.Length == 1 ? "this file: " + first : "these files:\r\n" + string.Join("\r\n", paths.Take(20))));
                    break;
                case "browser": if (!folder) foreach (var f in paths.Take(10)) App.I.OpenInBrowser(f); break;
                case "markdown": App.I.OpenMarkdown(first); break;
                case "documents": if (!folder) foreach (var f in paths.Take(12)) OpenDocument(f); break;
                case "tagged": TaggedPanel.AskTag(paths.ToList(), this); break;
            }
        }

        // Pages in display order: Favorites first, then General / Tools / System in your order.
        List<KeyValuePair<string, List<NavPage>>> NavLayout(bool includeHidden) { return NavModel.Layout(navPages, NavState.Of(Store.Settings), includeHidden); }

        public void BuildNav()
        {
            if (navList == null) return;
            var order = new List<Control>();
            foreach (var g in NavLayout(false))
            {
                if (Ui.Nexus) order.Add(new NavGroup(g.Key));
                foreach (var p in g.Value) { p.Item.Text = Ui.PageName(p.Key, p.Name); navigationTips.SetToolTip(p.Item, p.Item.Text); order.Add(p.Item); }
            }
            navList.SuspendLayout();
            foreach (var old in navList.Controls.OfType<NavGroup>().ToList()) { navList.Controls.Remove(old); old.Dispose(); }
            navList.Controls.Clear();
            int navH = 0;
            // Dock Top stacks in reverse, so add bottom-up.
            for (int oi = order.Count - 1; oi >= 0; oi--) { order[oi].Dock = DockStyle.Top; navList.Controls.Add(order[oi]); navH += order[oi].Height; }
            navList.AutoScrollMinSize = new Size(0, navH + Ui.S(6));
            navList.ResumeLayout(true);
            if (topBar != null) topBar.Invalidate();
        }

        void NavMenu(NavPage p, Point at)
        {
            var s = Store.Settings;
            var m = Ui.Menu();
            m.Items.Add("Open", null, delegate { ShowPage(p.Key); });
            m.Items.Add("Rename...", null, delegate
            {
                string v = Ui.Ask("Rename page", "Name in the sidebar (empty = " + p.Name + ")", Ui.PageName(p.Key, p.Name), this);
                if (v == null) return;
                v = v.Trim();
                if (v.Length == 0 || v == p.Name) s.PageNames.Remove(p.Key); else s.PageNames[p.Key] = v;
                NavChanged();
            });
            bool fav = s.NavFavorites.Contains(p.Key);
            m.Items.Add(fav ? "Remove from favorites" : "Add to favorites", null, delegate { if (fav) s.NavFavorites.Remove(p.Key); else s.NavFavorites.Add(p.Key); NavChanged(); });
            m.Items.Add("Move up", null, delegate { MoveNav(p, -1); });
            m.Items.Add("Move down", null, delegate { MoveNav(p, 1); });
            m.Items.Add("Hide from the sidebar", null, delegate { if (!s.NavHidden.Contains(p.Key)) s.NavHidden.Add(p.Key); NavChanged(); });
            m.Items.Add(new ToolStripSeparator());
            if (s.NavHidden.Count > 0)
            {
                var hidden = new ToolStripMenuItem("Show a hidden page");
                foreach (var k in s.NavHidden.ToList())
                {
                    var hp = navPages.FirstOrDefault(x => x.Key == k); if (hp == null) continue;
                    string key = k;
                    hidden.DropDownItems.Add(Ui.PageName(hp.Key, hp.Name), null, delegate { s.NavHidden.Remove(key); NavChanged(); });
                }
                m.Items.Add(hidden);
            }
            m.Items.Add("Customize sidebar...", null, delegate { CustomizeNav(); });
            m.Show(at);
        }

        // Moves a page inside its group (or inside Favorites).
        void MoveNav(NavPage p, int delta) { if (NavModel.Move(navPages, NavState.Of(Store.Settings), p, delta)) NavChanged(); }

        public void CustomizeNav()
        {
            using (var d = new SidebarDialog(this)) if (d.ShowDialog(this) == DialogResult.OK) NavChanged();
        }

        void NavChanged() { Store.MarkSettings(); BuildNav(); }
        bool NavCollapsed { get { int v; return Store.Settings.PaneSizes.TryGetValue("nav.collapsed", out v) && v == 1; } }
        void AdaptNav(Panel nav)
        {
            bool compact = Width < Ui.S(820) || NavCollapsed;
            nav.Width = compact ? Ui.S(64) : PaneGrip.Size("nav", DockStyle.Left, Ui.S(224), Width / 2);
            PlaceToggles(nav);
        }
        void PlaceToggles(Panel nav)
        {
            nav = nav ?? navPanel;
            if (navToggle == null || dockToggle == null || nav == null || topBar == null) return;
            int d = navToggle.Width, y = topBar.Bottom + Ui.S(10);
            bool navLeft = nav.Width > Ui.S(80), dockLeft = !dock.Visible;
            if (navToggle.PointsLeft != navLeft) { navToggle.PointsLeft = navLeft; navToggle.Invalidate(); }
            navToggle.Tip = navLeft ? "Fold the sidebar to icons" : "Expand the sidebar";
            var np = new Point(nav.Right - d / 2 - Ui.S(2), y);
            if (navToggle.Location != np) navToggle.Location = np;
            if (dockToggle.PointsLeft != dockLeft) { dockToggle.PointsLeft = dockLeft; dockToggle.Invalidate(); }
            dockToggle.Tip = dock.Visible ? "Hide the quick launch bar" : "Show the quick launch bar";
            var dp = new Point(dock.Visible ? dock.Left - d / 2 : ClientSize.Width - d - Ui.S(4), y);
            if (dockToggle.Location != dp) dockToggle.Location = dp;
            if (Controls.GetChildIndex(navToggle) > 1 || Controls.GetChildIndex(dockToggle) > 1) { navToggle.BringToFront(); dockToggle.BringToFront(); }
        }

        public void UpdateShortcutHelp()
        {
            var lines = new List<string> { "SHORTCUTS (work in any app)" };
            foreach (var a in Hotkeys.Actions)
            {
                string k = Hotkeys.Get(a[0]);
                if (k.Length > 0) lines.Add(k + "   " + a[3]);
            }
            keys.Text = string.Join("\n", lines);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                statusTimer.Dispose();
                navigationTips.Dispose();
                Store.NotesChanged -= OnNotes; Store.PromptsChanged -= OnPrompts; Store.ClipsChanged -= OnClips;
            }
            base.Dispose(disposing);
        }

        void OnNotes() { if (notes == null) return; if (Visible && page == "notes") notes.RefreshList(); else notes.Stale = true; }
        void OnPrompts() { if (prompts == null) return; if (Visible && page == "prompts") prompts.RefreshAll(); else prompts.Stale = true; }
        void OnClips() { if (clips == null) return; if (Visible && page == "clips") clips.RefreshList(); else clips.Stale = true; }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) RefreshCurrent();
        }

        void RefreshCurrent()
        {
            if (notes != null && page == "notes" && notes.Stale) notes.RefreshList();
            if (prompts != null && page == "prompts" && prompts.Stale) prompts.RefreshAll();
            if (clips != null && page == "clips" && clips.Stale) clips.RefreshList();
            if (cards != null && page == "cards") cards.RefreshList();
        }

        public void ShowPage(string p)
        {
            if (string.IsNullOrEmpty(p)) p = "dashboard";
            string key = p.Split(':')[0];
            if (p != key && key != "system" && key != "chrome") return;
            if (key == "home") key = "dashboard";
            EnsurePage(key);
            if (!loadedPages.ContainsKey(key)) return;
            if (prompts != null && page == "prompts" && p != "prompts") prompts.Commit();
            if (p.StartsWith("system:")) { system.SelectTab(p.Substring(7)); p = "system"; }
            if (p.StartsWith("chrome:")) { chrome.SelectTab(p.Substring(7)); p = "chrome"; }
            if (p == "home") p = "dashboard";
            string target = p;
            Ui.Batch(content, () =>
            {
                page = target;
                navHome.Selected = target == "dashboard";
                foreach (var entry in loadedPages) entry.Value.Visible = entry.Key == target;
                navNotes.Selected = target == "notes";
                navPrompts.Selected = target == "prompts";
                navClips.Selected = target == "clips";
                navSystem.Selected = target == "system";
                navCards.Selected = target == "cards";
                navSnip.Selected = target == "snip";
                navActivity.Selected = target == "activity";
                navTagged.Selected = target == "tagged";
                navTasks.Selected = target == "tasks";
                navExplorer.Selected = target == "explorer";
                navChrome.Selected = target == "chrome";
                navPower.Selected = target == "power";
                navKeys.Selected = target == "shortcuts";
                navTerminals.Selected = target == "terminals";
                navAgents.Selected = target == "agents";
                navMarkdown.Selected = target == "markdown";
                navBrowser.Selected = target == "browser";
                navDocs.Selected = target == "documents";
                navAccounts.Selected = target == "accounts";
            });
            RefreshCurrent();
            FocusSearch();
            PageChangedForAssistant();
        }

        // Pages retain their state once visited; unused tools allocate no controls or handles.
        readonly Dictionary<string, Control> loadedPages = new Dictionary<string, Control>();
        void EnsurePage(string key)
        {
            if (loadedPages.ContainsKey(key)) return;
            Control control;
            Action<string> report = s => Status(s);
            switch (key)
            {
                case "dashboard": control = new DashboardPanel(); break;
                case "notes": control = notes = new NotesPage(this); break;
                case "prompts": control = prompts = new PromptsPage(this); break;
                case "clips": control = clips = new ClipsPage(this); break;
                case "system": control = system = new SystemPage(this); break;
                case "cards": control = cards = new CardsPage(this); break;
                case "snip": control = PageWith("Snip & record", new SnipPanel(report)); break;
                case "activity": control = PageWith("File activity", new ActivityPanel(report)); break;
                case "tagged": control = PageWith("Tagged files", new TaggedPanel(report)); break;
                case "tasks": control = tasks = new TasksPage(); break;
                case "explorer": control = explorer = new ExplorerPage(); break;
                case "chrome": control = chrome = new ChromePage(this); break;
                case "power": control = new PowerPage(); break;
                case "shortcuts": control = shortcuts = new ShortcutsPage(); break;
                case "terminals": control = terminals = new TerminalPage(); break;
                case "agents": control = agents = new AgentsPage(); break;
                case "markdown": control = markdown = new MarkdownPage(); break;
                case "browser": control = browser = new BrowserPage(); break;
                case "documents": control = documents = new DocumentsPage(); break;
                case "accounts": control = accountsPage = new AccountsPage(); break;
                default: return;
            }
            control.Visible = false;
            content.Controls.Add(control);
            Ui.Theme(control);
            loadedPages.Add(key, control);
        }

        public void FocusSearch()
        {
            if (page == "notes") notes.FocusSearch();
            else if (page == "prompts") prompts.FocusSearch();
            else if (page == "clips") clips.FocusSearch();
            else if (page == "cards") cards.FocusSearch();
            else if (page == "tasks") tasks.FocusQuick();
        }

        public DocumentsPage DocumentsView { get { return documents; } }
        public ExplorerPage ExplorerView { get { return explorer; } }
        public BrowserPage BrowserView { get { return browser; } }

        public void SelectTask(TaskItem t) { ShowPage("tasks"); tasks.Select(t); }
        public void OpenInExplorer(string path) { ShowPage("explorer"); explorer.OpenPath(path); PageChangedForAssistant(); }
        public void OpenMarkdownFile(string path) { ShowPage("markdown"); if (markdown != null) markdown.Open(path, true); PageChangedForAssistant(); }
        public void OpenDocument(string path, bool? edit = null) { ShowPage("documents"); if (documents != null) documents.Open(path, edit); PageChangedForAssistant(); }
        public void OpenInBrowser(string target)
        {
            ShowPage("browser");
            if (browser == null || string.IsNullOrEmpty(target)) return;
            bool file = false; try { file = System.IO.File.Exists(target); } catch { }
            if (file) browser.OpenPath(target); else browser.Go(target, true);
            PageChangedForAssistant();
        }
        public void OpenOrReloadInBrowser(string path)
        {
            ShowPage("browser");
            if (browser == null || string.IsNullOrEmpty(path)) return;
            browser.OpenOrReloadFile(path);
            PageChangedForAssistant();
        }
        // Live 20-page awareness for the Assistant Companion and MCP tool page_context (in-memory only, never blocks on disk/network).
        public Dictionary<string, object> PageContext()
        {
            string p = page ?? "";
            string docCur = documents != null ? (documents.CurrentPath ?? "") : "";
            var docTabs = documents != null ? documents.OpenTabs : new List<string>();
            bool docEdit = documents != null && docCur.Length > 0 && documents.IsEditing(docCur);
            string expFolder = explorer != null ? (explorer.CurrentFolder ?? "") : "";
            var expSel = explorer != null ? explorer.SelectedPaths().Take(25).ToList() : new List<string>();
            string brTitle = browser != null ? (browser.ActiveTitle ?? "") : "";
            string brUrl = browser != null ? (browser.ActiveUrl ?? "") : "";
            string mdNote = markdown != null ? (markdown.CurrentPath ?? "") : "";

            var d = new Dictionary<string, object>
            {
                { "page", p },
                { "loaded_pages", loadedPages.Keys.OrderBy(k => k).ToList() },
                { "documents_current", docCur },
                { "documents_tabs", docTabs },
                { "documents_editing", docEdit },
                { "explorer_folder", expFolder },
                { "explorer_selected", expSel },
                { "browser_title", brTitle },
                { "browser_url", brUrl },
                { "markdown_note", mdNote }
            };

            if (documents != null && documents.ActiveEditor != null && documents.ActiveEditor.Kind == "excel")
            {
                try
                {
                    var xctx = OfficeCom.ExcelQuickContext(documents.ActiveEditor);
                    if (xctx != null)
                    {
                        foreach (var kv in xctx) d[kv.Key] = kv.Value;
                    }
                }
                catch (Exception) { }
            }

            switch (p)
            {
                case "dashboard":
                    d["tasks_open"] = Store.Tasks.Count(t => !t.Done);
                    d["tasks_due_today"] = Store.Tasks.Count(TaskParse.DueToday);
                    d["tasks_overdue"] = Store.Tasks.Count(TaskParse.Overdue);
                    d["notes_active"] = Store.Notes.Count(n => !n.Archived);
                    d["prompts_count"] = Store.Prompts.Count;
                    d["clips_count"] = Store.Clips.Count;
                    d["tagged_count"] = Store.Tagged.Count;
                    d["activity_count"] = Store.Activity.Count;
                    break;
                case "tasks":
                    d["tasks_open"] = Store.Tasks.Count(t => !t.Done);
                    d["tasks_done"] = Store.Tasks.Count(t => t.Done);
                    d["tasks_due_today"] = Store.Tasks.Count(TaskParse.DueToday);
                    d["tasks_overdue"] = Store.Tasks.Count(TaskParse.Overdue);
                    d["task_lists"] = Store.TaskLists();
                    break;
                case "notes":
                    d["notes_active"] = Store.Notes.Count(n => !n.Archived);
                    d["notes_archived"] = Store.Notes.Count(n => n.Archived);
                    d["notes_open_windows"] = Store.Notes.Count(n => n.Open && !n.Archived);
                    d["notes_with_reminder"] = Store.Notes.Count(n => n.RemindAt > 0);
                    break;
                case "prompts":
                    d["prompts_count"] = Store.Prompts.Count;
                    d["prompts_favorites"] = Store.Prompts.Count(x => x.Fav);
                    d["prompts_categories"] = Store.Prompts.Select(x => x.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
                    break;
                case "clips":
                    d["clips_count"] = Store.Clips.Count;
                    d["clips_pinned"] = Store.Clips.Count(c => c.Pinned);
                    d["clips_paused"] = Store.Settings.Paused;
                    break;
                case "documents":
                    d["documents_mode"] = docCur.Length > 0 ? (docEdit ? "edit" : "preview") : (Store.Settings.ViewerEdit ? "edit" : "preview");
                    d["documents_kind"] = documents != null ? (documents.ActiveKind ?? "") : "";
                    d["documents_tab_count"] = docTabs.Count;
                    break;
                case "explorer":
                    d["folder"] = expFolder;
                    d["explorer_panes"] = explorer != null ? explorer.PaneFolders() : new List<string>();
                    d["explorer_layout"] = Store.Settings.ExplorerLayout ?? "";
                    break;
                case "browser":
                    d["browser_tabs"] = browser != null ? browser.OpenTabsInfo() : new List<Dictionary<string, string>>();
                    d["browser_automation"] = Store.Settings.BrowserAutomation;
                    break;
                case "markdown":
                    d["note"] = mdNote;
                    d["markdown_vault"] = MdVault.Current ?? "";
                    d["markdown_mode"] = Store.Settings.MdMode ?? "read";
                    if (markdown != null)
                    {
                        string sel = markdown.SelectedText;
                        if (sel.Length > 0) d["selected_text"] = sel.Length > 4000 ? sel.Substring(0, 4000) : sel;
                    }
                    break;
                case "agents":
                    d["agents_chats_count"] = AgentStore.Loaded ? AgentStore.Chats.Count(c => !AssistantHarness.IsAssistant(c) && c.HasContent) : 0;
                    d["agents_busy_count"] = AgentStore.Busy.Count;
                    if (!string.IsNullOrEmpty(Store.Dir)) d["agents_defs"] = AgentStore.Defs.Select(x => x.Id).ToList();
                    break;
                case "terminals":
                    d["terminals_shell"] = Store.Settings.TermShell ?? "pwsh";
                    d["terminals_workspaces"] = (Store.Settings.TermWorkspaces ?? new List<TermWorkspace>()).Select(w => w.Name).Take(20).ToList();
                    d["terminals_recent"] = (Store.Settings.TermRecent ?? new List<string>()).Take(10).ToList();
                    break;
                case "system":
                    d["system_tab"] = system != null ? system.ActiveTab : "procs";
                    break;
                case "chrome":
                    d["chrome_tab"] = chrome != null ? chrome.ActiveTab : "bookmarks";
                    break;
                case "power":
                    d["keep_awake"] = Awake.On;
                    d["overlay_on"] = Overlay.On;
                    break;
                case "shortcuts":
                    d["launchers_count"] = Store.Settings.Launchers != null ? Store.Settings.Launchers.Count : 0;
                    d["hotkeys_count"] = Hotkeys.Actions.Length;
                    break;
                case "cards":
                    d["cards_count"] = Store.Cards.Count;
                    d["cards_visible"] = Store.Cards.Count(c => c.Visible);
                    break;
                case "snip":
                    d["snip_auto_save"] = Store.Settings.SnipAutoSave;
                    d["snip_copy_mode"] = Store.Settings.SnipCopyMode ?? "image";
                    d["recorder_active"] = Recorder.Active;
                    break;
                case "activity":
                    d["activity_count"] = Store.Activity.Count;
                    d["activity_on"] = Store.Settings.ActivityOn;
                    d["activity_recent"] = Store.Activity.OrderByDescending(a => a.Time).Take(5).Select(a => a.Path).ToList();
                    break;
                case "tagged":
                    d["tagged_count"] = Store.Tagged.Count;
                    d["tagged_categories"] = Store.TagCats.Select(c => c.Name).ToList();
                    break;
                case "accounts":
                    d["accounts_default_map"] = Store.Settings.AgentDefaultAccount != null ? new Dictionary<string, string>(Store.Settings.AgentDefaultAccount) : new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(Store.Dir)) d["agents_defs"] = AgentStore.Defs.Select(x => x.Id).ToList();
                    break;
            }
            return d;
        }
        public System.Threading.Tasks.Task<string> BrowserScript(string js) { if (browser == null) { ShowPage("browser"); } if (browser == null) throw new InvalidOperationException("The Browser page is not open."); return browser.RunScript(js); }
        Panel assistantHost; AssistantPanel assistant;
        // Zen mode (F11, Esc leaves): only the page; the menu, top bar, dock and status line step aside.
        bool zen;
        public bool Zen { get { return zen; } }
        public void SetZen(bool on)
        {
            zen = on;
            SuspendLayout();
            if (navPanel != null) navPanel.Visible = !on;
            if (topBar != null) topBar.Visible = !on;
            dock.Visible = !on && Store.Settings.DockVisible;
            status.Visible = !on;
            if (navToggle != null) navToggle.Visible = !on;
            if (dockToggle != null) dockToggle.Visible = !on;
            ResumeLayout();
            if (markdown != null) markdown.SetZen(on);
            if (on) Status("Zen mode: F11 or Esc to leave.");
        }
        public void SetAssistant(bool show)
        {
            if (show && assistant == null) { assistant = new AssistantPanel(this) { Dock = DockStyle.Fill }; assistantHost.Controls.Add(assistant); }
            assistantHost.Visible = show;
            if (Store.Settings.AssistantOpen != show) { Store.Settings.AssistantOpen = show; Store.MarkSettings(); }
            if (topBar != null) topBar.Invalidate();
            if (show && assistant != null) assistant.FocusInput();
        }
        public void PageChangedForAssistant() { if (assistant != null) assistant.PageChanged(page); }
        public void InsertIntoNote(string text) { if (markdown != null && markdown.CurrentPath != null) { ShowPage("markdown"); markdown.AppendText(text); } }
        public void SendToAgents(string text) { ShowPage("agents"); if (agents != null) agents.InsertText(text); }
        public void OpenAgentChat(AgentChat c) { ShowPage("agents"); if (agents != null) agents.OpenChatPublic(c); }
        public void AddTerminalFolder(string folder) { ShowPage("terminals"); terminals.UseFolder(folder); }
        public void AddAgentFolder(string folder) { ShowPage("agents"); agents.UseFolder(folder); }
        public void RefreshLaunchers() { if (shortcuts != null) shortcuts.RefreshLaunchers(); }
        public void SetDockVisible(bool v) { dock.Visible = v; PlaceToggles(null); }

        public void StartNewPrompt() { ShowPage("prompts"); prompts.NewPrompt(); }
        public void UpdatePaused() { if (clips != null) clips.UpdatePaused(); }
        public void CommitEdits() { if (prompts != null) prompts.Commit(); }
        public bool ConfirmDocumentClose() { return documents == null || documents.ConfirmCloseEditors(); }

        public void Status(string text)
        {
            status.Text = text;
            statusTimer.Stop();
            statusTimer.Start();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11 && !e.Control && !e.Alt && !e.Shift) { SetZen(!zen); e.SuppressKeyPress = true; return; }
            if (zen && e.KeyCode == Keys.Escape) { SetZen(false); e.SuppressKeyPress = true; return; }
            if (e.Control && e.Alt && e.KeyCode == Keys.B) { SetAssistant(!assistantHost.Visible); e.SuppressKeyPress = true; return; }   // VS Code: secondary side bar
            if (e.Control && !e.Alt)
            {
                if (e.KeyCode == Keys.D0) { ShowPage("dashboard"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D1) { ShowPage("notes"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D2) { ShowPage("prompts"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D3) { ShowPage("clips"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D4) { ShowPage("system"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D5) { ShowPage("cards"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D6) { ShowPage("snip"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D7) { ShowPage("activity"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D8) { ShowPage("tagged"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D9) { ShowPage("tasks"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.F && page != "explorer") { FocusSearch(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.K || (e.KeyCode == Keys.P && e.Shift)) { GlobalSearch.Open(this); e.SuppressKeyPress = true; }   // Ctrl+Shift+P like VS Code
                else if (e.KeyCode == Keys.J) { SetAssistant(!assistantHost.Visible); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.B && !e.Shift) { Store.Settings.PaneSizes["nav.collapsed"] = NavCollapsed ? 0 : 1; Store.MarkSettings(); AdaptNav(navPanel); e.SuppressKeyPress = true; }   // VS Code: side bar
                else if (e.KeyCode == Keys.N && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    if (page == "prompts") prompts.NewPrompt(); else if (page == "tasks") tasks.FocusQuick(); else App.I.NewNote(null);
                }
            }
            else if (e.KeyCode == Keys.Escape && !e.Control && !(ActiveControl is TextBoxBase)) { Hide(); e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (prompts != null) prompts.Commit();
            if (!Exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            Store.NotesChanged -= OnNotes;
            Store.PromptsChanged -= OnPrompts;
            Store.ClipsChanged -= OnClips;
            base.OnFormClosing(e);
        }

        public static FlowLayoutPanel Bar()
        {
            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.MinimumSize = Ui.S(0, 40);
            bar.WrapContents = true;
            bar.Padding = new Padding(0, Ui.S(6), 0, Ui.S(10));
            return bar;
        }

        public static Label Heading(string text)
        {
            var l = new PageHeading();
            l.Text = text;
            l.Accent = PageHeading.ColorFor(text);
            l.Padding = new Padding(Ui.S(14), 0, 0, 0);
            l.UseMnemonic = false;
            l.Font = Ui.Nexus ? Ui.Title : Ui.Big;
            l.ForeColor = Ui.Header; // PicPick-style blue page headers (dark text in Nexus)
            l.AutoSize = true;
            l.Margin = new Padding(0, 0, Ui.S(14), 0);
            return l;
        }

        public static ComboBox Combo(int width, params object[] items)
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = Ui.S(width);
            c.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            c.Items.AddRange(items);
            if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        // Keeps the selected entry of a combo while replacing its items.
        public static void Reload(ComboBox c, IEnumerable<string> fixedItems, IEnumerable<string> items)
        {
            string cur = c.SelectedItem as string;
            c.BeginUpdate();
            c.Items.Clear();
            foreach (var i in fixedItems) c.Items.Add(i);
            foreach (var i in items) c.Items.Add(i);
            int idx = cur == null ? 0 : c.Items.IndexOf(cur);
            c.SelectedIndex = idx < 0 ? 0 : idx;
            c.EndUpdate();
        }
    }

    // ===================== Notes =====================

    class NotesPage : Panel
    {
        public bool Stale = true;
        readonly MainForm owner;
        readonly TextBox search;
        readonly ComboBox view, colorFilter, tagFilter;
        readonly CardGrid list;
        bool reloading;

        public NotesPage(MainForm owner)
        {
            this.owner = owner;
            Dock = DockStyle.Fill;

            list = new CardGrid();
            list.Dock = DockStyle.Fill;
            list.ItemHeight = Ui.S(78);
            list.MinColumnWidth = Ui.S(320);
            list.Info = o => NoteInfo((Note)o);
            list.DoubleClick += delegate { OpenSelected(); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { OpenSelected(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
            };
            list.ContextMenuStrip = BuildMenu();
            Controls.Add(list);

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Sticky notes"));
            search = Ui.Search("Search notes and tags  (Ctrl+F)");
            search.Width = Ui.S(200);
            search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            var searchSoon = Ui.Debounce(search, 200, RefreshList); search.TextChanged += delegate { searchSoon(); };
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.SuppressKeyPress = true; } };
            bar.Controls.Add(search);
            view = MainForm.Combo(120, "Active notes", "With reminder", "Archived", "All notes");
            view.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(view);
            colorFilter = MainForm.Combo(100, "All colors");
            foreach (var c in Ui.Colors) colorFilter.Items.Add(c.Name);
            colorFilter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(colorFilter);
            tagFilter = MainForm.Combo(105, "All tags");
            tagFilter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(tagFilter);
            bar.Controls.Add(Ui.Btn("+  New note", delegate { App.I.NewNote(null); }, true));
            Controls.Add(bar);
        }

        CardInfo NoteInfo(Note n)
        {
            var c = Ui.ColorOf(n.Color);
            var ci = new CardInfo();
            string text = (n.Text ?? "").Trim();
            ci.Title = text.Length == 0 ? "(empty note)" : Store.TitleFrom(text);
            int nl = text.IndexOf('\n');
            ci.Sub = nl > 0 ? Ui.OneLine(text.Substring(nl + 1), 220) : "";
            var meta = new List<string>();
            if (n.RemindAt > 0) meta.Add("Reminder " + Ui.Ago(n.RemindAt));
            if (n.Tags.Trim().Length > 0) meta.Add("#" + string.Join(" #", Store.SplitTags(n.Tags)));
            if (n.Archived) meta.Add("archived");
            else if (n.Open) meta.Add("open");
            meta.Add(Ui.Ago(n.Modified));
            ci.Meta = string.Join("  ·  ", meta);
            ci.Back = c.Body;
            ci.Strip = c.Header;
            ci.Fore = c.Text;
            ci.SubFore = c.Name == "Charcoal" ? Ui.NoteMutedDark : Ui.NoteMuted;
            return ci;
        }

        public void FocusSearch() { search.Focus(); search.SelectAll(); }

        public void RefreshList()
        {
            Stale = false;
            reloading = true;
            MainForm.Reload(tagFilter, new[] { "All tags" }, Store.NoteTags());
            reloading = false;
            string q = search.Text.Trim();
            string col = colorFilter.SelectedIndex > 0 ? (string)colorFilter.SelectedItem : null;
            string tag = tagFilter.SelectedIndex > 0 ? (string)tagFilter.SelectedItem : null;
            int v = view.SelectedIndex;
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var items = Store.Notes
                .Where(n => v == 3 || (v == 2 ? n.Archived : v == 1 ? n.RemindAt > 0 : !n.Archived))
                .Where(n => words.All(w => ((n.Text ?? "") + "\n" + n.Tags).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(n => col == null || n.Color == col)
                .Where(n => tag == null || Store.SplitTags(n.Tags).Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(n => v == 1 ? -n.RemindAt : n.Modified)
                .Cast<object>();
            list.SetItems(items);
        }

        Note Sel() { return list.SelectedItem as Note; }

        void OpenSelected() { var n = Sel(); if (n != null) App.I.OpenNote(n); }

        void DeleteSelected()
        {
            var n = Sel();
            if (n == null) return;
            if (MessageBox.Show(owner, "Delete this note for good?\n\n" + Ui.OneLine(n.Text, 80) + "\n\nTip: Archive keeps it but hides it.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                App.I.DeleteNote(n);
        }

        ContextMenuStrip BuildMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("Open", null, delegate { OpenSelected(); });
            m.Items.Add(AppLinks.LazySendTo(null, () => { var n = Sel(); return n == null ? null : n.Text; }, null));
            var colors = new ToolStripMenuItem("Color");
            foreach (var c in Ui.Colors)
            {
                var col = c;
                colors.DropDownItems.Add(c.Name, Ui.Swatch(c.Header, Ui.S(16)), delegate
                {
                    var n = Sel(); if (n == null) return;
                    n.Color = col.Name; n.Modified = Store.Now(); Store.MarkNotes(); App.I.RefreshNoteColor(n); Store.RaiseNotes();
                });
            }
            m.Items.Add(colors);
            m.Items.Add("Tags...", null, delegate
            {
                var n = Sel(); if (n == null) return;
                var t = Ui.Ask("Note tags", "Tags, separated by commas (for example: work, ideas):", n.Tags, owner);
                if (t == null) return;
                n.Tags = t.Trim(); Store.MarkNotes(); Store.RaiseNotes();
            });
            m.Items.Add("Remind me...", null, delegate
            {
                var n = Sel(); if (n == null) return;
                long r = Ui.AskReminder(n.RemindAt, owner);
                if (r < 0) return;
                n.RemindAt = r; Store.MarkNotes(); Store.RaiseNotes();
                if (r > 0) owner.Status("Reminder set for " + new DateTime(r).ToString("ddd d MMM HH:mm") + ". The note pops up then.");
            });
            m.Items.Add("Archive / unarchive", null, delegate
            {
                var n = Sel(); if (n == null) return;
                App.I.ArchiveNote(n, !n.Archived);
                owner.Status(n.Archived ? "Archived. Find it under \"Archived\"." : "Back in active notes.");
            });
            m.Items.Add("Copy text", null, delegate { var n = Sel(); if (n != null && App.I.CopyText(n.Text)) owner.Status("Note text copied."); });
            m.Items.Add("Save as prompt", null, delegate
            {
                var n = Sel(); if (n == null || (n.Text ?? "").Trim().Length == 0) return;
                Store.NewPrompt(Store.TitleFrom(n.Text), n.Text.Trim(), "From notes");
                owner.Status("Saved to Prompts > From notes.");
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete", null, delegate { DeleteSelected(); });
            Ui.ThemeMenu(m);
            return m;
        }
    }

    // ===================== Prompts =====================

    class CatItem
    {
        public string Key, Label;
        public int Count;
        public override string ToString() { return Label; }
    }

    class PromptsPage : Panel
    {
        public bool Stale = true;
        readonly MainForm owner;
        readonly ListBox cats;
        readonly CardList list;
        readonly TextBox search, tbTitle, tbTags, tbBody, tbTrigger;
        readonly ComboBox cbCat, sort, compactCats;
        readonly Button btnFav, btnHistory;
        readonly TableLayoutPanel editor;
        readonly Timer catTimer = new Timer();
        Prompt cur;
        bool loading;
        string catKey = "*all";
        string loadedTitle, loadedBody;

        public PromptsPage(MainForm owner)
        {
            this.owner = owner;
            Dock = DockStyle.Fill;

            // ---- editor (fill) ----
            editor = new TableLayoutPanel();
            editor.Dock = DockStyle.Fill;
            editor.BackColor = Ui.Card;
            editor.Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(10));
            editor.ColumnCount = 1;
            editor.AutoScroll = true;
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tbTitle = new TextBox(); tbTitle.Font = Ui.Bold; tbTitle.AccessibleName = "Title";
            cbCat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat, Font = Ui.Base };
            tbTags = new TextBox { AccessibleName = "Tags", Font = Ui.Base };
            tbTrigger = new TextBox { AccessibleName = "Snippet code", Font = Ui.Base };
            Ui.Cue(tbTrigger, "e.g. ;sig");
            cbCat.AccessibleName = "Category"; Ui.Cue(tbTags, "Separate tags with commas");
            tbBody = new TextBox { AccessibleName = "Prompt text" }; tbBody.Multiline = true; tbBody.ScrollBars = ScrollBars.Vertical; tbBody.AcceptsReturn = true; tbBody.AcceptsTab = true;
            Ui.UseFont(tbBody, new Font(Store.Settings.NoteFontName, 10.5f)); tbBody.WordWrap = true;
            foreach (var c in new Control[] { tbTitle, cbCat, tbTags, tbTrigger, tbBody }) { c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 0, 0, Ui.S(6)); }
            AddRow(FieldLabel("Title"), SizeType.AutoSize);
            AddRow(EditorField(tbTitle, false), SizeType.AutoSize);
            var two = new TableLayoutPanel(); two.ColumnCount = 2; two.RowCount = 2; two.Dock = DockStyle.Fill; two.AutoSize = true; two.Margin = new Padding(0);
            two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            two.Controls.Add(FieldLabel("Category"), 0, 0); two.Controls.Add(FieldLabel("Snippet code"), 1, 0);
            var categoryField = EditorField(cbCat, false); categoryField.Margin = new Padding(0, 0, Ui.S(10), Ui.S(6));
            two.Controls.Add(categoryField, 0, 1); two.Controls.Add(EditorField(tbTrigger, false), 1, 1);
            AddRow(two, SizeType.AutoSize);
            AddRow(FieldLabel("Tags"), SizeType.AutoSize);
            AddRow(EditorField(tbTags, false), SizeType.AutoSize);
            var bodyLabel = FieldLabel("Prompt text");
            AddRow(bodyLabel, SizeType.AutoSize);
            var editorTips = new ToolTip();
            tbBody.AccessibleDescription = "Use {{name}} to insert a fill-in field.";
            editorTips.SetToolTip(bodyLabel, tbBody.AccessibleDescription);
            editorTips.SetToolTip(tbBody, tbBody.AccessibleDescription);
            editorTips.SetToolTip(tbTrigger, "Typing this code expands the prompt.");
            editor.Disposed += delegate { editorTips.Dispose(); };
            AddRow(EditorField(tbBody, true), SizeType.Percent);
            var actions = new FlowLayoutPanel(); actions.AutoSize = true; actions.Dock = DockStyle.Fill; actions.WrapContents = true; actions.Margin = new Padding(0);
            btnFav = Ui.Btn("Favorite", delegate { ToggleFav(); });
            btnHistory = Ui.Btn("History", delegate { ShowHistory(); });
            actions.Controls.Add(Ui.Btn("Copy", delegate { CopyCurrent(); }, true));
            var sendPrompt = Ui.Btn("Send to", null);
            sendPrompt.Click += delegate
            {
                if (cur == null) return; Commit();
                var sm = Ui.Menu(); var send = AppLinks.SendTo(null, cur.Body, owner);
                while (send.DropDownItems.Count > 0) { var it = send.DropDownItems[0]; send.DropDownItems.RemoveAt(0); sm.Items.Add(it); }
                Ui.ThemeMenu(sm); sm.Show(sendPrompt, new Point(0, sendPrompt.Height));
            };
            actions.Controls.Add(sendPrompt);
            actions.Controls.Add(btnFav);
            actions.Controls.Add(btnHistory);
            actions.Controls.Add(Ui.Btn("Duplicate", delegate { Duplicate(); }));
            actions.Controls.Add(Ui.Btn("Delete", delegate { DeleteCurrent(); }));
            AddRow(actions, SizeType.AutoSize);
            tbTitle.TextChanged += delegate { Edited(); };
            cbCat.TextChanged += delegate { Edited(); catTimer.Stop(); catTimer.Start(); };
            tbTags.TextChanged += delegate { Edited(); };
            tbTrigger.TextChanged += delegate { Edited(); catTimer.Stop(); catTimer.Start(); };
            tbBody.TextChanged += delegate { Edited(); CheckBodyDirection(); };
            tbBody.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.A) { tbBody.SelectAll(); e.SuppressKeyPress = true; } };
            KeyEventHandler ctrlEnter = (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { CopyCurrent(); e.SuppressKeyPress = true; } };
            tbBody.KeyDown += ctrlEnter; tbTitle.KeyDown += ctrlEnter; tbTags.KeyDown += ctrlEnter;
            catTimer.Interval = 900;
            catTimer.Tick += delegate { catTimer.Stop(); RefreshCats(); Snippets.Update(); };
            Controls.Add(editor);

            // ---- middle: search + list ----
            var mid = new Panel(); mid.Dock = DockStyle.Left; mid.Width = Ui.S(340); mid.Padding = new Padding(Ui.S(8), 0, Ui.S(8), 0);
            list = new CardList(); list.Dock = DockStyle.Fill;
            list.Info = o => PromptInfo((Prompt)o);
            list.SelectedIndexChanged += delegate { LoadEditor(list.SelectedItem as Prompt); };
            list.DoubleClick += delegate { CopyCurrent(); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CopyCurrent(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteCurrent(); e.SuppressKeyPress = true; }
            };
            mid.Controls.Add(list);
            var midBar = MainForm.Bar();
            midBar.Controls.Add(Ui.Btn("+  New prompt", delegate { NewPrompt(); }, true));
            sort = MainForm.Combo(130, "Recent", "Most used", "A to Z");
            sort.Margin = new Padding(0, Ui.S(3), 0, 0);
            sort.SelectedIndexChanged += delegate { RefreshList(); };
            midBar.Controls.Add(sort);
            mid.Controls.Add(midBar);
            search = Ui.Search("Search title, text, tags, code  (Ctrl+F)");
            search.Dock = DockStyle.Top;
            var searchSoon = Ui.Debounce(search, 200, RefreshList); search.TextChanged += delegate { searchSoon(); };
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.SuppressKeyPress = true; } };
            var searchWrap = new Panel(); searchWrap.Dock = DockStyle.Top; searchWrap.Height = Ui.S(34); searchWrap.Padding = new Padding(0, Ui.S(2), 0, Ui.S(6));
            searchWrap.Controls.Add(search);
            mid.Controls.Add(searchWrap);
            compactCats = MainForm.Combo(200);
            compactCats.Dock = DockStyle.Top;
            compactCats.SelectedIndexChanged += delegate { if (compactCats.SelectedIndex >= 0 && compactCats.SelectedIndex < cats.Items.Count) cats.SelectedIndex = compactCats.SelectedIndex; };
            mid.Controls.Add(compactCats);
            Controls.Add(mid);

            // ---- left: categories ----
            var left = new Panel(); left.Dock = DockStyle.Left; left.Width = Ui.S(200);
            cats = new ListBox();
            cats.Dock = DockStyle.Fill;
            cats.BorderStyle = BorderStyle.None;
            cats.DrawMode = DrawMode.OwnerDrawFixed;
            cats.ItemHeight = Ui.S(30);
            cats.IntegralHeight = false;
            cats.DrawItem += DrawCat;
            cats.SelectedIndexChanged += delegate { var c = cats.SelectedItem as CatItem; if (c != null && c.Key != catKey) { catKey = c.Key; RefreshList(); } if (cats.SelectedIndex >= 0 && cats.SelectedIndex < compactCats.Items.Count) compactCats.SelectedIndex = cats.SelectedIndex; };
            var catMenu = Ui.Menu();
            catMenu.Items.Add("Rename category", null, delegate { RenameCategory(); });
            Ui.ThemeMenu(catMenu);
            cats.ContextMenuStrip = catMenu;
            cats.MouseDown += (s, e) => { int i = cats.IndexFromPoint(e.Location); if (i >= 0 && e.Button == MouseButtons.Right) cats.SelectedIndex = i; };
            left.Controls.Add(cats);
            var io = LayoutKit.Actions(DockStyle.Bottom);
            io.Controls.Add(Ui.Btn("Import", delegate { Import(); }));
            io.Controls.Add(Ui.Btn("Export", delegate { Export(); }));
            mid.Controls.Add(io);
            var head = MainForm.Heading("Prompts"); head.Dock = DockStyle.Top; head.AutoSize = false; head.Height = Ui.S(40);
            left.Controls.Add(head);
            Controls.Add(left);
            PaneGrip.Attach(mid, "prompts.list", Ui.S(160));
            PaneGrip.Attach(left, "prompts.cats", Ui.S(110));

            // The editor is a TableLayoutPanel with AutoSize rows: double-buffer it and batch the pane changes
            // into one layout pass, so live window resizing stays smooth.
            try { typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(editor, true, null); } catch { }
            EventHandler layout = delegate
            {
                SuspendLayout();
                var before = left.Visible + "|" + left.Width + "|" + mid.Dock + "|" + mid.Width + "|" + mid.Height + "|" + compactCats.Visible;
                try
                {
                    bool compact = Width < Ui.S(850);
                    if (left.Visible == compact) left.Visible = !compact;
                    if (compactCats.Visible != compact) compactCats.Visible = compact;
                    if (!compact) { int lw = PaneGrip.Size("prompts.cats", DockStyle.Left, Ui.S(200), Width / 2); if (left.Width != lw) left.Width = lw; }
                    bool stacked = Width < Ui.S(600);
                    var md = stacked ? DockStyle.Top : DockStyle.Left;
                    if (mid.Dock != md) mid.Dock = md;
                    if (stacked) mid.Height = PaneGrip.Size("prompts.list", DockStyle.Top, Ui.S(190), Height);
                    else
                    {
                        // Wide windows: the prompt list grows (about a quarter of the page, 340 to 560 px) instead of a fixed strip.
                        int want = compact ? Math.Max(Ui.S(180), Math.Min(Ui.S(300), Width * 42 / 100)) : Math.Max(Ui.S(340), Math.Min(Ui.S(560), Width * 26 / 100));
                        int mw = PaneGrip.Size("prompts.list", DockStyle.Left, want, Width - (compact ? 0 : left.Width));
                        if (mid.Width != mw) mid.Width = mw;
                    }
                }
                finally { ResumeLayout(before != left.Visible + "|" + left.Width + "|" + mid.Dock + "|" + mid.Width + "|" + mid.Height + "|" + compactCats.Visible); }
            };
            Resize += (s, e) => Ui.Debounce("prompts.layout", 90, () => layout(this, EventArgs.Empty));   // once after the resize settles
            layout(this, EventArgs.Empty);

            LoadEditor(null);
        }

        void AddRow(Control c, SizeType st)
        {
            editor.RowStyles.Add(st == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            editor.Controls.Add(c, 0, editor.RowStyles.Count - 1);
            editor.RowCount = editor.RowStyles.Count;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Commit(); catTimer.Dispose(); }
            base.Dispose(disposing);
        }

        static Panel EditorField(Control input, bool multiline)
        {
            var frame = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Field,
                Padding = new Padding(Ui.S(8), Ui.S(5), Ui.S(6), Ui.S(5)), Margin = new Padding(0, 0, 0, Ui.S(6)) };
            var box = input as TextBox;
            if (box != null) box.BorderStyle = BorderStyle.None;
            int inputHeight = input.PreferredSize.Height;
            int height = Math.Max(Ui.S(34), inputHeight + Ui.S(8));
            if (!multiline)
            {
                int above = (height - inputHeight) / 2;
                frame.Padding = new Padding(Ui.S(8), above, Ui.S(6), height - inputHeight - above);
            }
            frame.Height = height; frame.MinimumSize = new Size(0, multiline ? Ui.S(68) : height);
            if (!multiline) frame.MaximumSize = new Size(0, height);
            input.Dock = DockStyle.Fill; input.Margin = Padding.Empty; frame.Controls.Add(input);
            frame.Paint += (s, e) => ControlPaint.DrawBorder(e.Graphics, frame.ClientRectangle,
                input.ContainsFocus ? Ui.Accent : Ui.Muted, ButtonBorderStyle.Solid);
            input.Enter += delegate { frame.Invalidate(); }; input.Leave += delegate { frame.Invalidate(); };
            return frame;
        }

        static Label FieldLabel(string t)
        {
            var l = new Label(); l.Text = t; l.AutoSize = true; l.ForeColor = Ui.Text; l.Font = Ui.Bold; l.Margin = new Padding(0, Ui.S(4), 0, Ui.S(4));
            return l;
        }

        void DrawCat(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var c = (CatItem)cats.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Ui.SelBg : Ui.Bg)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Ui.Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Ui.S(6), Ui.S(3), e.Bounds.Height - Ui.S(12));
            var r = new Rectangle(e.Bounds.X + Ui.S(12), e.Bounds.Y, e.Bounds.Width - Ui.S(54), e.Bounds.Height);
            var dir = Ui.IsRtl(c.Label) ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, c.Label, c.Key.StartsWith("*") ? Ui.Bold : Ui.Base, r, Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | dir);
            var rc = new Rectangle(e.Bounds.Right - Ui.S(44), e.Bounds.Y, Ui.S(36), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, c.Count.ToString(), Ui.Small, rc, Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        CardInfo PromptInfo(Prompt p)
        {
            var ci = new CardInfo();
            ci.Title = (p.Fav ? "★ " : "") + (p.Title.Trim().Length > 0 ? p.Title : "(untitled)");
            ci.Sub = Ui.OneLine(p.Body, 200);
            var meta = new List<string>();
            if (p.Trigger.Trim().Length > 0) meta.Add(p.Trigger.Trim());
            if (p.Category.Trim().Length > 0) meta.Add(p.Category.Trim());
            if (p.Uses > 0) meta.Add(p.Uses + "x");
            ci.Meta = string.Join("  ·  ", meta);
            ci.Strip = p.Fav ? Ui.PinStrip : Color.Empty;
            return ci;
        }

        public void FocusSearch() { search.Focus(); search.SelectAll(); }

        public void RefreshAll() { Stale = false; RefreshCats(); RefreshList(); }

        void RefreshCats()
        {
            var items = new List<CatItem>();
            items.Add(new CatItem { Key = "*all", Label = "All prompts", Count = Store.Prompts.Count });
            items.Add(new CatItem { Key = "*fav", Label = "★ Favorites", Count = Store.Prompts.Count(p => p.Fav) });
            items.Add(new CatItem { Key = "*used", Label = "Most used", Count = Store.Prompts.Count(p => p.Uses > 0) });
            items.Add(new CatItem { Key = "*snip", Label = "Snippets (with code)", Count = Store.Prompts.Count(p => p.Trigger.Trim().Length > 0) });
            foreach (var g in Store.Prompts.GroupBy(p => p.Category.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key == "" ? "￿" : g.Key, StringComparer.OrdinalIgnoreCase))
                items.Add(new CatItem { Key = g.Key, Label = g.Key == "" ? "(no category)" : g.Key, Count = g.Count() });
            cats.BeginUpdate();
            cats.Items.Clear();
            foreach (var i in items) cats.Items.Add(i);
            int idx = items.FindIndex(i => string.Equals(i.Key, catKey, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { idx = 0; catKey = "*all"; }
            cats.SelectedIndex = idx;
            cats.EndUpdate();
            compactCats.BeginUpdate();
            compactCats.Items.Clear();
            compactCats.Items.AddRange(items.Cast<object>().ToArray());
            compactCats.SelectedIndex = idx;
            compactCats.EndUpdate();

            string typed = cbCat.Text;
            bool wasLoading = loading; loading = true;
            cbCat.BeginUpdate();
            cbCat.Items.Clear();
            foreach (var i in items.Where(i => !i.Key.StartsWith("*") && i.Key != "")) cbCat.Items.Add(i.Key);
            cbCat.EndUpdate();
            if (cbCat.Text != typed) { cbCat.Text = typed; cbCat.SelectionStart = typed.Length; }
            loading = wasLoading;
        }

        void RefreshList()
        {
            string q = search.Text.Trim();
            IEnumerable<Prompt> src = Store.Prompts;
            if (catKey == "*fav") src = src.Where(p => p.Fav);
            else if (catKey == "*used") src = src.Where(p => p.Uses > 0);
            else if (catKey == "*snip") src = src.Where(p => p.Trigger.Trim().Length > 0);
            else if (catKey != "*all") src = src.Where(p => string.Equals(p.Category.Trim(), catKey, StringComparison.OrdinalIgnoreCase));
            if (q.Length > 0)
            {
                var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                src = src.Where(p => words.All(w => (p.Title + "\n" + p.Body + "\n" + p.Tags + "\n" + p.Category + "\n" + p.Trigger).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            if (sort.SelectedIndex == 1 || catKey == "*used") src = src.OrderByDescending(p => p.Uses).ThenByDescending(p => p.LastUsed);
            else if (sort.SelectedIndex == 2) src = src.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase);
            else src = src.OrderByDescending(p => Math.Max(p.Modified, p.LastUsed));
            list.SetItems(src.Cast<object>());
            if (list.SelectedItem == null && cur != null && !list.Items.Contains(cur)) LoadEditor(null);
        }

        Label editorHint;
        void LoadEditor(Prompt p)
        {
            if (p == cur && p != null) return;
            Commit();
            cur = p;
            loading = true;
            // No prompt chosen: a short hint instead of greyed-out fields (unreadable in dark mode).
            editor.Visible = p != null;
            if (editorHint == null && editor.Parent != null)
            {
                editorHint = new Label { Dock = DockStyle.Top, Height = Ui.S(110), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted, BackColor = Ui.Bg,
                    Text = "Pick a prompt on the left\r\nor press New prompt." };
                editor.Parent.Controls.Add(editorHint);
            }
            if (editorHint != null) editorHint.Visible = p == null;
            tbTitle.Text = p != null ? p.Title : "";
            cbCat.Text = p != null ? p.Category : "";
            tbTags.Text = p != null ? p.Tags : "";
            tbTrigger.Text = p != null ? p.Trigger : "";
            tbBody.Text = p != null ? (p.Body ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            loadedTitle = p != null ? p.Title : null;
            loadedBody = p != null ? p.Body : null;
            UpdateButtons();
            loading = false;
            CheckBodyDirection();
        }

        void UpdateButtons()
        {
            btnFav.Text = cur != null && cur.Fav ? "Remove favorite" : "Favorite";
            btnHistory.Text = cur != null && cur.Versions.Count > 0 ? "History (" + cur.Versions.Count + ")" : "History";
        }

        void CheckBodyDirection()
        {
            var want = Ui.IsRtl(tbBody.Text) ? RightToLeft.Yes : RightToLeft.No;
            if (tbBody.RightToLeft != want) BeginInvokeSafe(() => { if (tbBody.RightToLeft != want) tbBody.RightToLeft = want; });
            var wantT = Ui.IsRtl(tbTitle.Text) ? RightToLeft.Yes : RightToLeft.No;
            if (tbTitle.RightToLeft != wantT) BeginInvokeSafe(() => { if (tbTitle.RightToLeft != wantT) tbTitle.RightToLeft = wantT; });
        }

        void BeginInvokeSafe(Action a)
        {
            if (IsHandleCreated) BeginInvoke(a); else a();
        }

        bool dirty;
        void Edited()
        {
            if (loading || cur == null) return;
            cur.Title = tbTitle.Text;
            cur.Category = cbCat.Text.Trim();
            cur.Tags = tbTags.Text;
            cur.Trigger = tbTrigger.Text.Trim();
            cur.Body = tbBody.Text;
            cur.Modified = Store.Now();
            dirty = true;
            Store.MarkPrompts();
            list.Invalidate();
        }

        // Saves a version of the previous text when the prompt text was changed.
        public void Commit()
        {
            if (cur != null && loadedBody != null && (cur.Body != loadedBody || cur.Title != loadedTitle) && loadedBody.Trim().Length > 0)
            {
                cur.Versions.Insert(0, new PromptVersion { Title = loadedTitle, Body = loadedBody, Time = Store.Now() });
                while (cur.Versions.Count > 30) cur.Versions.RemoveAt(cur.Versions.Count - 1);
                loadedBody = cur.Body;
                loadedTitle = cur.Title;
                Store.MarkPrompts();
                UpdateButtons();
            }
            if (dirty) { dirty = false; Store.MarkPrompts(); }
        }

        public void NewPrompt()
        {
            string cat = catKey.StartsWith("*") ? "" : catKey;
            search.Text = "";
            var p = Store.NewPrompt("New prompt", "", cat);
            if (catKey.StartsWith("*")) catKey = "*all";
            RefreshAll();
            list.SelectedItem = p;
            tbTitle.Focus();
            tbTitle.SelectAll();
        }

        void CopyCurrent()
        {
            if (cur == null) return;
            Commit();
            var text = Ui.FillVariables(cur.Body, owner);
            if (text == null) return;
            if (App.I.CopyText(text))
            {
                cur.Uses++; cur.LastUsed = Store.Now();
                Store.MarkPrompts();
                list.Invalidate();
                owner.Status("Prompt copied. Paste it with Ctrl+V.");
            }
            else owner.Status("Nothing to copy - the prompt text is empty.");
        }

        void ToggleFav()
        {
            if (cur == null) return;
            cur.Fav = !cur.Fav;
            UpdateButtons();
            Store.MarkPrompts();
            RefreshCats();
            list.Invalidate();
        }

        void ShowHistory()
        {
            if (cur == null) return;
            Commit();
            if (cur.Versions.Count == 0) { owner.Status("No older versions yet. A version is saved each time you change the text and move on."); return; }
            using (var f = Ui.Dialog("Versions of \"" + Ui.OneLine(cur.Title, 40) + "\"", 760, 460))
            {
                f.FormBorderStyle = FormBorderStyle.Sizable;
                f.MinimumSize = Ui.S(500, 300);
                var lb = new ListBox(); lb.Dock = DockStyle.Left; lb.Width = Ui.S(240); lb.IntegralHeight = false; lb.Font = Ui.Base;
                foreach (var v in cur.Versions) lb.Items.Add(Ui.Ago(v.Time) + "  -  " + Ui.OneLine(v.Title, 30));
                var tb = new FluentTextBox(); tb.Multiline = true; tb.ReadOnly = true; tb.ScrollBars = ScrollBars.Vertical; tb.Dock = DockStyle.Fill; tb.Font = Ui.Base;
                var bar = new FlowLayoutPanel(); bar.Dock = DockStyle.Bottom; bar.Height = Ui.S(42); bar.FlowDirection = FlowDirection.RightToLeft; bar.Padding = new Padding(Ui.S(6));
                var restore = Ui.Btn("Restore this version", null, true); restore.DialogResult = DialogResult.OK;
                var close = Ui.Btn("Close", null); close.DialogResult = DialogResult.Cancel;
                bar.Controls.Add(restore); bar.Controls.Add(close);
                lb.SelectedIndexChanged += delegate
                {
                    if (lb.SelectedIndex < 0) return;
                    var v = cur.Versions[lb.SelectedIndex];
                    tb.Text = (v.Body ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                    tb.RightToLeft = Ui.IsRtl(tb.Text) ? RightToLeft.Yes : RightToLeft.No;
                };
                f.Controls.Add(tb); f.Controls.Add(lb); f.Controls.Add(bar);
                f.CancelButton = close;
                Ui.Theme(f);
                lb.SelectedIndex = 0;
                if (f.ShowDialog(owner) != DialogResult.OK || lb.SelectedIndex < 0) return;
                var chosen = cur.Versions[lb.SelectedIndex];
                // Keep the current text as a version too, so restoring can be undone.
                cur.Versions.Insert(0, new PromptVersion { Title = cur.Title, Body = cur.Body, Time = Store.Now() });
                cur.Title = chosen.Title; cur.Body = chosen.Body; cur.Modified = Store.Now();
                Store.MarkPrompts();
                var p = cur; cur = null; loadedBody = null;
                LoadEditor(p);
                list.Invalidate();
                owner.Status("Version restored. The text before it was kept in History.");
            }
        }

        void Duplicate()
        {
            if (cur == null) return;
            var original = cur; // NewPrompt raises a refresh that temporarily clears cur.
            var p = Store.NewPrompt(original.Title + " (copy)", original.Body, original.Category);
            p.Tags = original.Tags;
            RefreshAll();
            list.SelectedItem = p;
        }

        void DeleteCurrent()
        {
            if (cur == null) return;
            if (MessageBox.Show(owner, "Delete prompt \"" + cur.Title + "\"?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var p = cur;
            int idx = list.SelectedIndex;
            cur = null; loadedBody = null;
            Store.Prompts.Remove(p);
            Store.MarkPrompts();
            Store.RaisePrompts();
            RefreshAll();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(Math.Max(0, idx), list.Items.Count - 1);
            else LoadEditor(null);
        }

        void RenameCategory()
        {
            var c = cats.SelectedItem as CatItem;
            if (c == null || c.Key.StartsWith("*")) return;
            var name = Ui.Ask("Rename category", "New name for \"" + c.Label + "\":", c.Key, owner);
            if (name == null) return;
            name = name.Trim();
            foreach (var p in Store.Prompts.Where(p => string.Equals(p.Category.Trim(), c.Key, StringComparison.OrdinalIgnoreCase))) p.Category = name;
            catKey = name.Length == 0 ? "*all" : name;
            Store.MarkPrompts();
            RefreshAll();
            var keep = list.SelectedItem as Prompt; cur = null; loadedBody = null;
            LoadEditor(keep);
        }

        void Export()
        {
            using (var d = new SaveFileDialog())
            {
                d.Filter = "Prompts (*.json)|*.json";
                d.FileName = "prompts-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json";
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, Store.ExportPrompts(Store.Prompts), new System.Text.UTF8Encoding(false));
                owner.Status("Exported " + Store.Prompts.Count + " prompts.");
            }
        }

        void Import()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "Prompts (*.json)|*.json|Text files (*.txt;*.md)|*.txt;*.md";
                d.Multiselect = true;
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                int n = 0;
                foreach (var f in d.FileNames)
                {
                    try
                    {
                        if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n += Store.ImportPrompts(File.ReadAllText(f)).Count;
                        else { Store.NewPrompt(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f), "Imported"); n++; }
                    }
                    catch (Exception ex) { MessageBox.Show(owner, "Could not import " + Path.GetFileName(f) + ":\n" + ex.Message, App.Name); }
                }
                RefreshAll();
                owner.Status("Imported " + n + " prompts.");
            }
        }
    }

    // ===================== Clipboard history =====================

    class ClipsPage : Panel
    {
        const int PreviewLimit = 32000;
        public bool Stale = true;
        readonly MainForm owner;
        readonly TextBox search, preview;
        readonly ComboBox filter, folderFilter;
        readonly CardList list;
        readonly CheckBox pause;
        readonly PictureBox picture;
        readonly Label previewMeta;
        bool reloading;
        bool decodingPreview;
        int previewRevision;
        string pendingPreview;

        public ClipsPage(MainForm owner)
        {
            this.owner = owner;
            Dock = DockStyle.Fill;

            list = new CardList();
            list.Dock = DockStyle.Fill;
            list.SelectionMode = SelectionMode.MultiExtended;
            list.Info = o => ClipInfo((Clip)o);
            var previewSoon = Ui.Debounce(list, 60, ShowPreview);
            list.SelectedIndexChanged += delegate { previewSoon(); };
            list.DoubleClick += delegate { CopySelected(0); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CopySelected(0); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.C) { CopySelected(0); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.P) { TogglePin(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.A) { list.SelectAll(); e.SuppressKeyPress = true; }
            };
            list.ContextMenuStrip = Ui.Menu();
            BuildMenu(list.ContextMenuStrip); // has items so it opens; rebuilt each time for fresh folder names
            list.ContextMenuStrip.Opening += (s, e) => BuildMenu(list.ContextMenuStrip);
            Controls.Add(list);

            var right = new Panel(); right.Dock = DockStyle.Right; right.Width = Ui.S(340); right.Padding = new Padding(Ui.S(10), Ui.S(4), 0, Ui.S(4));
            preview = new FluentTextBox(); preview.Multiline = true; preview.ReadOnly = true; preview.ScrollBars = ScrollBars.Vertical; preview.Dock = DockStyle.Fill;
            preview.BorderStyle = BorderStyle.FixedSingle; preview.Font = Ui.Base;
            picture = new PictureBox(); picture.Dock = DockStyle.Fill; picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Ui.Card; picture.Visible = false;
            previewMeta = new Label(); previewMeta.Dock = DockStyle.Top; previewMeta.Height = Ui.S(24); previewMeta.ForeColor = Ui.Muted; previewMeta.Font = Ui.Small;
            right.Controls.Add(preview);
            right.Controls.Add(picture);
            right.Controls.Add(previewMeta);
            Controls.Add(new ResponsivePane(list, right, Ui.S(340), Ui.S(780), Ui.S(210), "clips.preview"));

            var bar2 = MainForm.Bar();
            bar2.Controls.Add(Ui.Btn("Copy", delegate { CopySelected(0); }, true));
            var asBtn = Ui.Btn("Copy as...", null);
            asBtn.Click += delegate { var m = Ui.Menu(); AddTransforms(m.Items); Ui.ThemeMenu(m); m.Show(asBtn, new Point(0, asBtn.Height)); };
            bar2.Controls.Add(asBtn);
            bar2.Controls.Add(Ui.Btn("Pin", delegate { TogglePin(); }));
            var folderBtn = Ui.Btn("Folder...", null);
            folderBtn.Click += delegate { var m = Ui.Menu(); AddFolderItems(m.Items); Ui.ThemeMenu(m); m.Show(folderBtn, new Point(0, folderBtn.Height)); };
            bar2.Controls.Add(folderBtn);
            bar2.Controls.Add(Ui.Btn("Add to queue", delegate { AddToQueue(); }));
            bar2.Controls.Add(Ui.Btn("Delete", delegate { DeleteSelected(); }));
            bar2.Controls.Add(Ui.Btn("Clear unpinned", delegate { ClearUnpinned(); }));
            pause = new CheckBox(); pause.Text = "Pause capture"; pause.AutoSize = true; pause.Margin = new Padding(Ui.S(8), Ui.S(5), 0, 0);
            pause.Checked = Store.Settings.Paused;
            pause.CheckedChanged += delegate { if (pause.Checked != Store.Settings.Paused) App.I.SetPaused(pause.Checked); };
            bar2.Controls.Add(pause);
            Controls.Add(bar2);

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Clipboard history"));
            search = Ui.Search("Search everything you copied  (Ctrl+F)");
            search.Width = Ui.S(260);
            search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            var searchSoon = Ui.Debounce(search, 200, RefreshList); search.TextChanged += delegate { searchSoon(); };
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.SuppressKeyPress = true; } };
            bar.Controls.Add(search);
            filter = MainForm.Combo(120, "Everything", "Pinned", "Text", "Links", "Images", "Files", "Today", "Red", "Orange", "Yellow", "Green", "Blue", "Purple");
            filter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(filter);
            folderFilter = MainForm.Combo(150, "All folders");
            folderFilter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(folderFilter);
            Controls.Add(bar);
        }

        public void UpdatePaused() { pause.Checked = Store.Settings.Paused; }

        protected override void Dispose(bool disposing)
        {
            if (disposing && picture != null && picture.Image != null) { picture.Image.Dispose(); picture.Image = null; }
            base.Dispose(disposing);
        }

        CardInfo ClipInfo(Clip c)
        {
            var ci = new CardInfo();
            if (c.Kind == "image")
            {
                ci.Title = "Image";
                ci.Sub = c.ImgW + " x " + c.ImgH + " px";
                ci.Thumb = Thumbs.Get(c, Ui.S(90), list.ItemHeight - Ui.S(18), list);
            }
            else if (c.Kind == "files")
            {
                var files = (c.Text ?? "").Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                ci.Title = files.Length == 1 ? Ui.FirstFileName(c.Text) : files.Length + " files";
                ci.Sub = Ui.OneLine(c.Text, 200);
            }
            else
            {
                ci.Title = Ui.OneLine(Store.TitleFrom(c.Text), 120);
                ci.Sub = Ui.OneLine(c.Text, 240);
            }
            var meta = new List<string>();
            if (c.Pinned) meta.Add("PINNED");
            if (!string.IsNullOrEmpty(c.Folder)) meta.Add("[" + c.Folder + "]");
            if (!string.IsNullOrEmpty(c.Source)) meta.Add(c.Source);
            meta.Add(Ui.Ago(c.Time));
            ci.Meta = string.Join("  ·  ", meta);
            if (c.Pinned) { ci.Back = Ui.PinBack; ci.Strip = Ui.PinStrip; }
            var lc = Ui.LabelColor(c.Label);
            if (lc != Color.Empty) ci.Strip = lc;
            return ci;
        }

        public void FocusSearch() { search.Focus(); search.SelectAll(); }

        public void RefreshList()
        {
            Stale = false;
            reloading = true;
            MainForm.Reload(folderFilter, new[] { "All folders", "(no folder)" }, Store.Folders());
            reloading = false;
            string q = search.Text.Trim();
            IEnumerable<Clip> src = Store.Clips;
            int f = filter.SelectedIndex;
            switch (f)
            {
                case 1: src = src.Where(c => c.Pinned); break;
                case 2: src = src.Where(c => c.Kind == "text"); break;
                case 3: src = src.Where(c => c.Kind == "text" && (c.Text.StartsWith("http://") || c.Text.StartsWith("https://") || c.Text.StartsWith("www."))); break;
                case 4: src = src.Where(c => c.Kind == "image"); break;
                case 5: src = src.Where(c => c.Kind == "files"); break;
                case 6: { long today = DateTime.Today.Ticks; src = src.Where(c => c.Time >= today); break; }
                default:
                    if (f >= 7) { string label = (string)filter.SelectedItem; src = src.Where(c => c.Label == label); }
                    break;
            }
            if (folderFilter.SelectedIndex == 1) src = src.Where(c => string.IsNullOrEmpty(c.Folder));
            else if (folderFilter.SelectedIndex > 1) { string fo = (string)folderFilter.SelectedItem; src = src.Where(c => string.Equals(c.Folder, fo, StringComparison.OrdinalIgnoreCase)); }
            if (q.Length > 0)
            {
                var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                src = src.Where(c => words.All(w => c.Text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Source ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Folder ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            list.SetItems(src.Cast<object>());
            if (list.SelectedIndex < 0) previewMeta.Text = Store.Clips.Count + " items saved";
        }

        void ShowPreview()
        {
            previewRevision++;
            pendingPreview = null;
            var sel = list.Selected<Clip>();
            if (picture.Image != null) { var old = picture.Image; picture.Image = null; old.Dispose(); }
            if (sel.Count == 0) { preview.Visible = true; picture.Visible = false; preview.Text = Store.Clips.Count == 0 ? "Copy something anywhere and it appears here." : "Select an item to see it here."; previewMeta.Text = Store.Clips.Count + " items saved"; return; }
            if (sel.Count > 1)
            {
                picture.Visible = false; preview.Visible = true;
                previewMeta.Text = sel.Count + " selected - Copy joins them, one per line";
                preview.Text = PreviewText(sel);
                preview.RightToLeft = Ui.IsRtl(preview.Text) ? RightToLeft.Yes : RightToLeft.No;
                return;
            }
            var c1 = sel[0];
            previewMeta.Text = (c1.Kind == "image" ? "Image" : c1.Text.Length + " characters") + (string.IsNullOrEmpty(c1.Source) ? "" : "  ·  from " + c1.Source) + "  ·  " + Ui.Ago(c1.Time) + (c1.Uses > 0 ? "  ·  used " + c1.Uses + "x" : "");
            if (c1.Kind == "image")
            {
                preview.Visible = false; picture.Visible = true;
                pendingPreview = Store.ImagePath(c1);
                DecodePreview();
            }
            else
            {
                picture.Visible = false; preview.Visible = true;
                string t = PreviewText(sel);
                preview.Text = t.Replace("\r\n", "\n").Replace("\n", "\r\n");
                var rtl = Ui.IsRtl(preview.Text) ? RightToLeft.Yes : RightToLeft.No;
                if (preview.RightToLeft != rtl) preview.RightToLeft = rtl;
            }
        }

        void DecodePreview()
        {
            if (decodingPreview || string.IsNullOrEmpty(pendingPreview) || !IsHandleCreated) return;
            string path = pendingPreview;
            pendingPreview = null;
            int revision = previewRevision;
            decodingPreview = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var image = Thumbs.LoadScaled(path, 1280, 960);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        decodingPreview = false;
                        if (!IsDisposed && previewRevision == revision)
                        {
                            picture.Image = image;
                            if (image == null) previewMeta.Text = "Image unavailable";
                        }
                        else if (image != null) image.Dispose();
                        if (!IsDisposed) DecodePreview();
                    }));
                }
                catch (InvalidOperationException) { if (image != null) image.Dispose(); }
            });
        }

        internal static string PreviewText(IEnumerable<Clip> clips)
        {
            var preview = new System.Text.StringBuilder();
            bool first = true;
            foreach (var clip in clips)
            {
                if (clip.Kind == "image") continue;
                string text = clip.Text ?? "";
                if (!first)
                {
                    if (preview.Length > PreviewLimit - 2) return preview.Append("\r\n…").ToString();
                    preview.Append("\r\n");
                }
                int remaining = PreviewLimit - preview.Length;
                if (text.Length > remaining)
                {
                    int length = remaining > 0 && char.IsHighSurrogate(text[remaining - 1]) ? remaining - 1 : remaining;
                    return preview.Append(text, 0, length).Append("\r\n…").ToString();
                }
                preview.Append(text);
                first = false;
            }
            return preview.ToString();
        }

        // transform 0 = as is
        void CopySelected(int transform)
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) return;
            if (sel.Count == 1 && (transform == 0 || sel[0].Kind == "image"))
            {
                if (App.I.CopyClip(sel[0])) owner.Status("Copied. Paste it with Ctrl+V.");
                return;
            }
            var text = string.Join("\r\n", sel.Where(c => c.Kind != "image").Select(c => c.Text));
            text = Transforms.Apply(transform, text);
            if (App.I.CopyText(text)) owner.Status((sel.Count > 1 ? sel.Count + " items copied together" : "Copied as " + Transforms.Names[transform]) + ". Paste with Ctrl+V.");
        }

        void AddTransforms(ToolStripItemCollection items)
        {
            for (int i = 0; i < Transforms.Names.Length; i++)
            {
                int k = i;
                items.Add(Transforms.Names[i], null, delegate { CopySelected(k); });
            }
        }

        void AddFolderItems(ToolStripItemCollection items)
        {
            foreach (var f in Store.Folders())
            {
                string name = f;
                items.Add(name, null, delegate { SetFolder(name); });
            }
            if (items.Count > 0) items.Add(new ToolStripSeparator());
            items.Add("New folder...", null, delegate
            {
                var name = Ui.Ask("New folder", "Folder name (items in a folder are never deleted automatically):", "", owner);
                if (!string.IsNullOrWhiteSpace(name)) SetFolder(name.Trim());
            });
            items.Add("Remove from folder", null, delegate { SetFolder(""); });
        }

        void SetFolder(string folder)
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) { owner.Status("Select one or more items first."); return; }
            foreach (var c in sel) c.Folder = folder;
            Store.MarkClips();
            RefreshList();
            owner.Status(folder.Length > 0 ? sel.Count + " item(s) moved to folder \"" + folder + "\"." : "Removed from folder.");
        }

        void SetLabel(string label)
        {
            var sel = list.Selected<Clip>();
            foreach (var c in sel) c.Label = label;
            Store.MarkClips();
            list.Invalidate();
        }

        void TogglePin()
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) return;
            bool pin = sel.Any(c => !c.Pinned);
            foreach (var c in sel) c.Pinned = pin;
            Store.MarkClips();
            RefreshList();
            owner.Status(pin ? "Pinned - pinned items are never removed automatically." : "Unpinned.");
        }

        void AddToQueue()
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) { owner.Status("Select the items to paste one by one (Ctrl+click), then click Add to queue."); return; }
            // Oldest first, the order you copied them.
            App.I.AddToQueue(sel.OrderBy(c => c.Time));
            owner.Status(sel.Count + " item(s) in the paste queue. Press " + Hotkeys.Get("next") + " in any app to paste the next one.");
        }

        void DeleteSelected()
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) return;
            if (sel.Any(c => c.Pinned || !string.IsNullOrEmpty(c.Folder)) && MessageBox.Show(owner, "Some selected items are pinned or in a folder. Delete them anyway?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int idx = list.SelectedIndex;
            Store.RemoveClips(sel);
            if (list.Items.Count > 0) { list.ClearSelected(); list.SelectedIndex = Math.Min(Math.Max(0, idx), list.Items.Count - 1); }
        }

        void ClearUnpinned()
        {
            int n = Store.Clips.Count(c => !c.Pinned && string.IsNullOrEmpty(c.Folder));
            if (n == 0) return;
            if (MessageBox.Show(owner, "Delete " + n + " items? Pinned items and items in folders stay.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Store.RemoveClips(Store.Clips.Where(c => !c.Pinned && string.IsNullOrEmpty(c.Folder)));
            Thumbs.Clear();
        }

        void SaveAsPrompt()
        {
            var sel = list.Selected<Clip>().Where(c => c.Kind != "image").ToList();
            if (sel.Count == 0) return;
            foreach (var c in sel) Store.NewPrompt(Store.TitleFrom(c.Text), c.Text.Trim(), "From clipboard");
            owner.Status(sel.Count + " saved to Prompts > From clipboard.");
        }

        void SaveAsNote()
        {
            var sel = list.Selected<Clip>().Where(c => c.Kind != "image").ToList();
            if (sel.Count == 0) return;
            App.I.NewNote(null, string.Join("\r\n\r\n", sel.Select(c => c.Text)));
        }

        void BuildMenu(ContextMenuStrip m)
        {
            Ui.ClearItems(m.Items);
            m.Items.Add("Copy\tEnter", null, delegate { CopySelected(0); });
            var asMenu = new ToolStripMenuItem("Copy as");
            AddTransforms(asMenu.DropDownItems);
            m.Items.Add(asMenu);
            var one = list.Selected<Clip>().FirstOrDefault();
            if (one != null)
            {
                string t = one.Text ?? "";
                bool isPath = t.Length > 2 && t.Length < 400 && t.IndexOf('\n') < 0 && (System.IO.File.Exists(t.Trim('"')) || System.IO.Directory.Exists(t.Trim('"')));
                m.Items.Add(AppLinks.SendTo(isPath ? t.Trim('"') : null, isPath ? null : (t.Length > 0 ? t : null), FindForm()));
            }
            m.Items.Add("Pin / Unpin\tCtrl+P", null, delegate { TogglePin(); });
            var folder = new ToolStripMenuItem("Move to folder");
            AddFolderItems(folder.DropDownItems);
            m.Items.Add(folder);
            var label = new ToolStripMenuItem("Color label");
            foreach (var n in Ui.LabelNames)
            {
                string name = n;
                label.DropDownItems.Add(name, Ui.Swatch(Ui.LabelColor(name), Ui.S(16)), delegate { SetLabel(name); });
            }
            label.DropDownItems.Add("No label", null, delegate { SetLabel(""); });
            m.Items.Add(label);
            m.Items.Add("Add to paste queue", null, delegate { AddToQueue(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Save as prompt", null, delegate { SaveAsPrompt(); });
            m.Items.Add("Save as sticky note", null, delegate { SaveAsNote(); });
            m.Items.Add("Open image / file location / link", null, delegate
            {
                var c = list.Selected<Clip>().FirstOrDefault();
                if (c == null) return;
                try
                {
                    if (c.Kind == "image") Process.Start(Store.ImagePath(c));
                    else if (c.Kind == "files" && !string.IsNullOrEmpty(c.Text)) Process.Start("explorer.exe", "/select,\"" + c.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() + "\"");
                    else if (c.Text.StartsWith("http")) Process.Start(c.Text.Trim());
                }
                catch { }
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete\tDel", null, delegate { DeleteSelected(); });
            Ui.ThemeMenu(m);
        }
    }
}

namespace StickyPro
{
    // Page title with a slim rounded bar in the page's own color (what the page is for), in light and dark mode.
    class PageHeading : System.Windows.Forms.Label
    {
        public System.Drawing.Color Accent = System.Drawing.Color.Empty;

        static readonly string[][] Map =
        {
            new[] { "agent", "#9B5CF6" }, new[] { "council", "#9B5CF6" }, new[] { "terminal", "#10B981" }, new[] { "explorer", "#F97316" },
            new[] { "snip", "#EF4444" }, new[] { "record", "#EF4444" }, new[] { "chrome", "#3B82F6" }, new[] { "power", "#EAB308" },
            new[] { "system", "#14B8A6" }, new[] { "task", "#6366F1" }, new[] { "note", "#F59E0B" }, new[] { "prompt", "#8B5CF6" },
            new[] { "clip", "#06B6D4" }, new[] { "card", "#EC4899" }, new[] { "activity", "#64748B" }, new[] { "tag", "#84CC16" },
            new[] { "home", "#3B82F6" }, new[] { "dashboard", "#3B82F6" }, new[] { "setting", "#94A3B8" }, new[] { "shortcut", "#94A3B8" },
            new[] { "uninstall", "#F43F5E" }, new[] { "startup", "#14B8A6" }, new[] { "drive", "#0EA5E9" }
        };

        public static System.Drawing.Color ColorFor(string title)
        {
            string t = (title ?? "").ToLowerInvariant();
            foreach (var m in Map) if (t.Contains(m[0])) return System.Drawing.ColorTranslator.FromHtml(m[1]);
            return Ui.Accent;
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Accent.IsEmpty) return;
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int h = (int)(Font.Height * 0.72), w = Ui.S(5);
            var r = new System.Drawing.Rectangle(Ui.S(2), (Height - h) / 2 + Ui.S(1), w, h);
            using (var path = Ui.Round(r, w / 2)) using (var b = new System.Drawing.SolidBrush(Accent)) g.FillPath(b, path);
        }
    }
}
