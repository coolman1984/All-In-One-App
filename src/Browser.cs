using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace StickyPro
{
    // ===================================================================================================
    // Browser page: tabs on the Edge engine (WebView2, part of Windows 11) with its own profile in data\browser.
    // History and favorites use MetaStore (scope "web"). Page scripts: data\browser\scripts\*.js, run after each
    // page load when the first line "// @match <pattern with *>" fits the address (no line = every page).
    // Optional automation port (Playwright connect_over_cdp / any DevTools client): localhost only, off by default.
    // The WebView2 SDK DLLs (lib\webview2) are packed in the exe and unpacked to %LocalAppData%\AllInOne\webview2.
    // ===================================================================================================

    static class WebViewHost
    {
        const string Version = "1.0.2903.40";
        static readonly string[] Files = { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" };
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllInOne", "webview2", Version); } }
        public static string Error = "";

        // Called first thing in Main: must not touch any WebView2 type itself.
        public static void Hook() { AppDomain.CurrentDomain.AssemblyResolve += Resolve; }
        static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms") return null;
            return Unpack() ? Assembly.LoadFrom(Path.Combine(Dir, name + ".dll")) : null;
        }

        public static bool Unpack()
        {
            var asm = Assembly.GetExecutingAssembly();
            if (asm.GetManifestResourceInfo("webview2.WebView2Loader.dll") == null)
            {
                Error = "This build has no embedded browser engine.";
                return false;
            }
            try
            {
                Directory.CreateDirectory(Dir);
                foreach (var f in Files)
                {
                    string target = Path.Combine(Dir, f);
                    using (var s = asm.GetManifestResourceStream("webview2." + f))
                    {
                        if (s == null) { Error = "This build has no browser engine files (" + f + ")."; return false; }
                        if (File.Exists(target) && new FileInfo(target).Length == s.Length) continue;
                        using (var o = File.Create(target)) s.CopyTo(o);
                    }
                }
                return true;
            }
            catch (Exception ex) { Error = "Could not unpack the browser engine: " + ex.Message; return false; }
        }

        // The Edge WebView2 runtime version on this PC, or null when it is missing.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Runtime()
        {
            if (!Unpack()) return null;
            try { CoreWebView2Environment.SetLoaderDllFolderPath(Dir); } catch { }   // only allowed before the first environment
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception ex) { Error = "The Microsoft Edge WebView2 runtime is not installed (" + ex.Message + ")."; return null; }
        }
    }

    partial class BrowserPage : Panel
    {
        class Tab { public WebView2 View; public Panel Chip; public Button Title; public string Url = "", Name = "New tab"; public int NavigationRevision; public TabLog Log = new TabLog(); }

        readonly List<Tab> tabs = new List<Tab>();
        readonly FlowLayoutPanel tabBar;
        readonly TextBox address;
        readonly Panel host;
        readonly Label status;
        readonly Button favBtn;
        Tab active;
        Task<CoreWebView2Environment> env;
        static readonly ToolTip tips = new ToolTip();
        public static string ScriptsDir { get { return Path.Combine(Store.Dir, "browser", "scripts"); } }
        public const int AutomationPort = 9333;

        public BrowserPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Browser"));
            bar.Controls.Add(Ui.Btn("New tab", delegate { AddTab(Home, true); }, true));
            var accounts = Ui.Btn("Accounts", delegate { AccountsPanel.Open(FindForm()); });
            tips.SetToolTip(accounts, "Sign in to Claude Code, Codex, Cline, OpenCode, Antigravity; pick the account the app uses");
            bar.Controls.Add(accounts);
            var more = Ui.Btn("More", null);
            more.Click += delegate { MoreMenu(more); };
            bar.Controls.Add(more);

            tabBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, Ui.S(4)) };

            // Address row: [<][>][reload][home] [ address ............ ] [favorite]
            var row = new Panel { Dock = DockStyle.Top, Height = Ui.S(Ui.ControlHeight + 8), Padding = new Padding(0, 0, 0, Ui.S(8)) };
            address = Ui.Search("Search or type a web address");
            address.Dock = DockStyle.Fill;
            address.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(address.Text); } };
            address.GotFocus += delegate { BeginInvoke((Action)address.SelectAll); };
            favBtn = Ui.Btn("Favorite", delegate { ToggleFav(); });
            favBtn.Dock = DockStyle.Right;
            row.Controls.Add(address);
            row.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Ui.S(6) });
            row.Controls.Add(favBtn);
            foreach (var spec in new[] { new[] { "Home", "Home page" }, new[] { "Reload", "Reload (F5)" }, new[] { "Forward", "Forward (Alt+Right)" }, new[] { "Back", "Back (Alt+Left)" } })
            {
                string what = spec[0];
                var b = Ui.Btn(what, delegate { Nav(what); });
                b.Dock = DockStyle.Left; b.Margin = new Padding(0);
                tips.SetToolTip(b, spec[1]);
                row.Controls.Add(new Panel { Dock = DockStyle.Left, Width = Ui.S(4) });
                row.Controls.Add(b);
            }

            host = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card, AllowDrop = true };
            host.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            host.DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) foreach (var f in files.Take(10)) OpenPath(f); };
            status = new Label { Dock = DockStyle.Bottom, Height = Ui.S(22), ForeColor = Ui.Muted, Font = Ui.Small, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(host); Controls.Add(status); Controls.Add(row); Controls.Add(tabBar); Controls.Add(bar);
            Ui.Theme(this);
            Store.MetaChanged += OnMeta;
        }

        string engine = "";
        string StatusLine() { return "Edge engine " + engine + (Store.Settings.BrowserAutomation ? "   ·   automation port " + AutomationPort + " (this PC only)" : "") + (active != null && active.View.CoreWebView2 != null && Math.Abs(active.View.ZoomFactor - 1) > 0.01 ? "   ·   zoom " + Math.Round(active.View.ZoomFactor * 100) + "%" : ""); }
        static string Home { get { return string.IsNullOrWhiteSpace(Store.Settings.BrowserHome) ? "https://www.google.com" : Store.Settings.BrowserHome; } }

        void OnMeta() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke((Action)OnMeta); return; } UpdateFav(); }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible || tabs.Count > 0) return;
            string runtime = WebViewHost.Runtime();
            if (runtime == null) { ShowProblem(WebViewHost.Error); return; }
            engine = runtime;
            status.Text = StatusLine();
            var restore = Store.Settings.BrowserTabs.Where(u => !string.IsNullOrWhiteSpace(u)).ToList();
            if (restore.Count == 0) restore.Add(Home);
            foreach (var u in restore) AddTab(u, false);
            Activate(tabs[Math.Max(0, Math.Min(tabs.Count - 1, Store.Settings.BrowserActive))]);
        }

        void ShowProblem(string text)
        {
            host.Controls.Clear();
            host.Controls.Add(new Label { Dock = DockStyle.Fill, Text = text + "\n\nThe rest of the app works without it.", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted, Font = Ui.Base });
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.T)) { AddTab(Home, true); address.Focus(); return true; }
            if (keyData == (Keys.Control | Keys.W)) { if (active != null) CloseTab(active); return true; }
            if (keyData == (Keys.Control | Keys.L)) { address.Focus(); return true; }
            if (keyData == Keys.F5) { Nav("Reload"); return true; }
            if (keyData == (Keys.Control | Keys.O)) { OpenFile(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { FindInPage(); return true; }
            if (keyData == Keys.F9) { ReadingMode(); return true; }
            if (keyData == (Keys.Control | Keys.P)) { Print(); return true; }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add)) { Zoom(0.1); return true; }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract)) { Zoom(-0.1); return true; }
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0)) { Zoom(0); return true; }
            if (keyData == (Keys.Alt | Keys.Left)) { Nav("Back"); return true; }
            if (keyData == (Keys.Alt | Keys.Right)) { Nav("Forward"); return true; }
            if (keyData == (Keys.Control | Keys.Tab) && tabs.Count > 1) { Activate(tabs[(tabs.IndexOf(active) + 1) % tabs.Count]); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- tabs ----------
        Task<CoreWebView2Environment> Env()
        {
            if (env == null)
            {
                var options = new CoreWebView2EnvironmentOptions(Store.Settings.BrowserAutomation ? "--remote-debugging-port=" + AutomationPort + " --remote-allow-origins=http://localhost:" + AutomationPort : null);
                string profile = Path.Combine(Store.Dir, "browser", "profile");
                Directory.CreateDirectory(profile);
                env = CoreWebView2Environment.CreateAsync(null, profile, options);
            }
            return env;
        }

        void AddTab(string url, bool activate)
        {
            var t = new Tab { Url = url ?? "" };
            t.View = new WebView2 { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Ui.Card };
            host.Controls.Add(t.View);
            t.Chip = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, Ui.S(6), Ui.S(4)) };
            t.Title = Ui.Btn("New tab", delegate { Activate(t); });
            t.Title.Margin = new Padding(0);
            ((FluentButton)t.Title).Glyph = null;   // a page title, not the "New tab" action icon
            t.Title.MouseUp += (s, e) => { if (e.Button == MouseButtons.Middle) CloseTab(t); };
            var close = Ui.Btn("✕", delegate { CloseTab(t); });
            close.Margin = new Padding(Ui.S(2), 0, 0, 0);
            tips.SetToolTip(close, "Close tab (Ctrl+W)");
            var flow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0) };
            flow.Controls.Add(t.Title); flow.Controls.Add(close);
            t.Chip.Controls.Add(flow);
            tabBar.Controls.Add(t.Chip);
            tabs.Add(t);
            Init(t);
            if (activate || active == null) Activate(t);
            SaveTabs();
        }

        async void Init(Tab t)
        {
            try
            {
                var e = await Env();
                if (t.View.IsDisposed) return;
                await t.View.EnsureCoreWebView2Async(e);
                var core = t.View.CoreWebView2;
                if (IsDisposed || t.View.IsDisposed || core == null) return;
                core.NavigationStarting += delegate { t.NavigationRevision++; };
                core.Settings.AreDevToolsEnabled = true;
                core.Settings.IsStatusBarEnabled = true;
                AutoAttach(t, core);
                core.DocumentTitleChanged += delegate { t.Name = core.DocumentTitle; ShowTitle(t); Remember(t); };
                core.SourceChanged += delegate { t.Url = core.Source; if (t == active) { if (!address.Focused) address.Text = t.Url; UpdateFav(); } SaveTabs(); };
                core.NewWindowRequested += (s, a) => { a.Handled = true; AddTab(a.Uri, true); };
                core.DownloadStarting += (s, a) =>
                {
                    var op = a.DownloadOperation; string file = a.ResultFilePath;
                    status.Text = "Downloading " + Path.GetFileName(file) + "...";
                    op.StateChanged += delegate
                    {
                        if (op.State == CoreWebView2DownloadState.Completed)
                        {
                            status.Text = "Downloaded: " + file + "   (More > Downloads)";
                            var dl = Store.Settings.BrowserDownloads; dl.Remove(file); dl.Insert(0, file); if (dl.Count > 40) dl.RemoveRange(40, dl.Count - 40); Store.MarkSettings();
                        }
                        else if (op.State == CoreWebView2DownloadState.Interrupted) status.Text = "Download stopped: " + op.InterruptReason;
                    };
                };
                core.NavigationCompleted += (s, a) =>
                {
                    if (!a.IsSuccess)
                    {
                        var err = a.WebErrorStatus;
                        bool real = err != CoreWebView2WebErrorStatus.OperationCanceled && err != CoreWebView2WebErrorStatus.Unknown && err != CoreWebView2WebErrorStatus.ConnectionAborted;
                        if (t == active && real) status.Text = "Could not load " + t.Url + " (" + err + ").";
                        return;
                    }
                    double z; string site = Site(t.Url);
                    if (site.Length > 0 && Store.Settings.BrowserZoom.TryGetValue(site, out z) && Math.Abs(t.View.ZoomFactor - z) > 0.01) t.View.ZoomFactor = z;
                    if (t == active) status.Text = StatusLine();
                    Remember(t);
                    RunPageScripts(t);
                };
                core.Navigate(Url(t.Url));
            }
            catch (Exception ex)
            {
                App.Log("Browser: " + ex);
                if (t == active) ShowProblem("The browser could not start: " + ex.Message);
            }
        }

        void Activate(Tab t)
        {
            active = t;
            foreach (var x in tabs) { x.View.Visible = x == t; ((FluentButton)x.Title).Primary = x == t; x.Title.Invalidate(); }
            address.Text = t.Url;
            UpdateFav();
            Store.Settings.BrowserActive = tabs.IndexOf(t); Store.MarkSettings();
            if (t.View.CanFocus) t.View.Focus();
        }

        void CloseTab(Tab t)
        {
            int i = tabs.IndexOf(t);
            if (i < 0) return;
            tabs.RemoveAt(i);
            tabBar.Controls.Remove(t.Chip); t.Chip.Dispose();
            host.Controls.Remove(t.View); t.View.Dispose();
            if (tabs.Count == 0) AddTab(Home, true);
            else if (t == active) Activate(tabs[Math.Min(i, tabs.Count - 1)]);
            SaveTabs();
        }

        void ShowTitle(Tab t)
        {
            string name = string.IsNullOrWhiteSpace(t.Name) ? t.Url : t.Name;
            t.Title.Text = name.Length > 26 ? name.Substring(0, 25) + "…" : name;
            tips.SetToolTip(t.Title, name + "\n" + t.Url);
        }

        void SaveTabs()
        {
            Store.Settings.BrowserTabs = tabs.Select(x => x.Url).Where(u => !string.IsNullOrEmpty(u)).ToList();
            Store.MarkSettings();
        }

        // ---------- navigation ----------
        static string Url(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return Home;
            if (Regex.IsMatch(text, "^(https?|file|about|edge|data):", RegexOptions.IgnoreCase)) return text;
            if (Regex.IsMatch(text, @"^[a-zA-Z]:\\")) return new Uri(text).AbsoluteUri;
            if (!text.Contains(" ") && (text.Contains(".") || text.StartsWith("localhost"))) return (text.StartsWith("localhost") ? "http://" : "https://") + text;
            return "https://www.google.com/search?q=" + Uri.EscapeDataString(text);
        }

        public string ActiveUrl { get { return active != null ? active.Url : ""; } }
        public string ActiveTitle { get { return active != null ? active.Name : ""; } }
        public List<Dictionary<string, string>> OpenTabsInfo()
        {
            var list = new List<Dictionary<string, string>>();
            foreach (var t in tabs)
            {
                list.Add(new Dictionary<string, string>
                {
                    { "title", t.Name ?? "" },
                    { "url", t.Url ?? "" },
                    { "active", t == active ? "true" : "false" }
                });
            }
            return list;
        }
        public System.Threading.Tasks.Task<string> RunScript(string js)
        {
            if (active == null || active.View.CoreWebView2 == null) throw new InvalidOperationException("No browser tab is open.");
            return active.View.CoreWebView2.ExecuteScriptAsync(js);
        }

        public const string AxSnapshotScript = @"(function(){
            var heads = [];
            var hNodes = document.querySelectorAll('h1,h2,h3,h4');
            for (var i = 0; i < hNodes.length && heads.length < 30; i++) {
                var ht = (hNodes[i].innerText || '').trim();
                if (ht) heads.push({ tag: hNodes[i].tagName.toLowerCase(), text: ht.slice(0, 140) });
            }
            var items = [];
            var q = document.querySelectorAll('a[href],button,input,select,textarea,[role=button],[role=link],[role=tab],[onclick]');
            for (var j = 0; j < q.length && items.length < 150; j++) {
                var el = q[j];
                var r = el.getBoundingClientRect();
                if (r.width <= 0 || r.height <= 0) continue;
                var tag = el.tagName.toLowerCase();
                var role = el.getAttribute('role') || (tag === 'a' ? 'link' : tag === 'button' ? 'button' : tag);
                var label = el.getAttribute('aria-label') || el.getAttribute('title') || el.getAttribute('placeholder') || '';
                var txt = (el.innerText || el.value || label || '').replace(/\s+/g, ' ').trim().slice(0, 120);
                var sel = el.id ? ('#' + el.id) : (el.name ? (tag + '[name=""' + el.name + '""]') : tag);
                items.push({
                    index: items.length + 1,
                    tag: tag,
                    role: role,
                    type: el.type || '',
                    text: txt,
                    label: label.slice(0, 100),
                    id: el.id || '',
                    name: el.name || '',
                    href: el.href || '',
                    selector: sel,
                    rect: { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) }
                });
            }
            var tables = [];
            var tNodes = document.querySelectorAll('table');
            for (var k = 0; k < tNodes.length && tables.length < 10; k++) {
                var tb = tNodes[k];
                var ths = [];
                var thEls = tb.querySelectorAll('th');
                for (var h = 0; h < thEls.length && h < 20; h++) ths.push((thEls[h].innerText || '').trim());
                tables.push({ index: k + 1, headers: ths, rows: tb.rows ? tb.rows.length : 0 });
            }
            return JSON.stringify({
                title: document.title || '',
                url: location.href || '',
                readyState: document.readyState || '',
                headings: heads,
                interactiveCount: items.length,
                interactive: items,
                tables: tables,
                textPreview: (document.body ? document.body.innerText : '').slice(0, 4000)
            });
        })()";

        public System.Threading.Tasks.Task<string> CallCdp(string method, string paramsJson, int tabIndex)
        {
            Tab t = (tabIndex >= 0 && tabIndex < tabs.Count) ? tabs[tabIndex] : active;
            if (t == null || t.View == null || t.View.CoreWebView2 == null)
                throw new InvalidOperationException("No initialized browser tab is open.");
            string m = (method ?? "").Trim();
            if (string.IsNullOrEmpty(m)
                || string.Equals(m, "ax_snapshot", StringComparison.OrdinalIgnoreCase)
                || string.Equals(m, "dom_snapshot", StringComparison.OrdinalIgnoreCase))
            {
                return t.View.CoreWebView2.ExecuteScriptAsync(AxSnapshotScript);
            }
            string pJson = string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson.Trim();
            return t.View.CoreWebView2.CallDevToolsProtocolMethodAsync(m, pJson);
        }
        public void Go(string text, bool newTab) { if (newTab) AddTab(Url(text), true); else Go(text); }
        public void Go(string text)
        {
            if (active == null) { AddTab(text, true); return; }
            active.Url = Url(text);
            if (active.View.CoreWebView2 != null) active.View.CoreWebView2.Navigate(active.Url);
            if (active.View.CanFocus) active.View.Focus();
        }

        // Another page of the app asks to show a web address here (new tab).
        public void Open(string url) { if (tabs.Count == 0) { Store.Settings.BrowserTabs = new List<string> { url }; return; } AddTab(url, true); }

        void Nav(string what)
        {
            var core = active == null ? null : active.View.CoreWebView2;
            if (what == "Home") { Go(Home); return; }
            if (core == null) return;
            if (what == "Back" && core.CanGoBack) core.GoBack();
            else if (what == "Forward" && core.CanGoForward) core.GoForward();
            else if (what == "Reload") core.Reload();
        }

        // ---------- history and favorites (MetaStore scope "web") ----------
        static void Remember(Tab t)
        {
            if (string.IsNullOrEmpty(t.Url) || t.Url.StartsWith("about:") || t.Url.StartsWith("data:")) return;
            MetaStore.Touch("web", t.Url);
            if (!string.IsNullOrWhiteSpace(t.Name)) { var m = MetaStore.Edit("web", t.Url); if (m.Name != t.Name) { m.Name = t.Name; MetaStore.Changed(); } }
        }

        void ToggleFav() { if (active != null && active.Url.Length > 0) { MetaStore.ToggleFav("web", active.Url); UpdateFav(); } }
        void UpdateFav() { favBtn.Text = active != null && MetaStore.IsFav("web", active.Url) ? "Favorited" : "Favorite"; }

        static string Label(ItemMeta m) { string n = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name; return n.Length > 70 ? n.Substring(0, 69) + "…" : n; }

        void MoreMenu(Control anchor)
        {
            var m = Ui.Menu();
            var fav = new ToolStripMenuItem("Favorites");
            foreach (var f in MetaStore.Favorites("web").Take(40)) { string u = f.Id; fav.DropDownItems.Add(Label(f), null, delegate { AddTab(u, true); }); }
            if (fav.DropDownItems.Count == 0) fav.DropDownItems.Add("(none yet: use Favorite on a page)").Enabled = false;
            m.Items.Add(fav);
            var hist = new ToolStripMenuItem("History");
            foreach (var h in MetaStore.Recent("web", 40)) { string u = h.Id; hist.DropDownItems.Add(Label(h) + "   ·   " + Ui.Ago(h.Used), null, delegate { AddTab(u, true); }); }
            if (hist.DropDownItems.Count == 0) hist.DropDownItems.Add("(empty)").Enabled = false;
            m.Items.Add(hist);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Open a file (report, PDF, email, HTML)...   Ctrl+O", null, delegate { OpenFile(); });
            m.Items.Add("Find on page...   Ctrl+F", null, delegate { FindInPage(); });
            m.Items.Add("Reading mode (text only)   F9", null, delegate { ReadingMode(); });
            var zoom = new ToolStripMenuItem("Zoom");
            zoom.DropDownItems.Add("Larger   Ctrl +", null, delegate { Zoom(0.1); });
            zoom.DropDownItems.Add("Smaller   Ctrl -", null, delegate { Zoom(-0.1); });
            zoom.DropDownItems.Add("Reset   Ctrl 0", null, delegate { Zoom(0); });
            m.Items.Add(zoom);
            m.Items.Add("Print...   Ctrl+P", null, delegate { Print(); });
            m.Items.Add("Save page as PDF...", null, delegate { SavePdf(); });
            m.Items.Add("Save a screenshot of the page...", null, delegate { SaveShot(); });
            var downloads = new ToolStripMenuItem("Downloads");
            foreach (var f in Store.Settings.BrowserDownloads.Where(File.Exists).Take(15))
            {
                string file = f;
                var it = new ToolStripMenuItem(Path.GetFileName(file));
                it.DropDownItems.Add("Open", null, delegate { try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); } catch { } });
                it.DropDownItems.Add("Open here (PDF, pictures, pages)", null, delegate { OpenPath(file); });
                it.DropDownItems.Add("Show in folder", null, delegate { try { Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { } });
                it.DropDownItems.Add(AppLinks.SendTo(file, null, FindForm()));
                downloads.DropDownItems.Add(it);
            }
            if (downloads.DropDownItems.Count == 0) downloads.DropDownItems.Add(new ToolStripMenuItem("(nothing downloaded yet)") { Enabled = false });
            m.Items.Add(downloads);
            if (active != null && active.Url.StartsWith("http"))
            {
                var page = AppLinks.SendTo(null, active.Name + "\n" + active.Url, FindForm()); page.Text = "Send this page to";
                m.Items.Add(page);
                m.Items.Add("Send the selected text to...", null, delegate { SendSelection(); });
                m.Items.Add("Save this page as a Markdown note", null, delegate { SaveAsNote(); });
            }
            m.Items.Add("Downloads folder", null, delegate { try { Process.Start("explorer.exe", "\"" + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") + "\""); } catch { } });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Run JavaScript on this page...", null, delegate { RunScriptPrompt(); });
            m.Items.Add("Run page scripts now", null, delegate { if (active != null) RunPageScripts(active); });
            m.Items.Add("Page scripts folder", null, delegate { Directory.CreateDirectory(ScriptsDir); WriteSample(); try { Process.Start("explorer.exe", "\"" + ScriptsDir + "\""); } catch { } });
            m.Items.Add("Developer tools (F12)", null, delegate { if (active != null && active.View.CoreWebView2 != null) active.View.CoreWebView2.OpenDevToolsWindow(); });
            var agents = new ToolStripMenuItem("Let AI agents use this browser") { Checked = !AgentsPaused };
            agents.Click += delegate { AgentsPaused = !AgentsPaused; Act(AgentsPaused ? "You stopped browser automation." : "You allowed agents to use the browser again."); };
            m.Items.Add(agents);
            m.Items.Add("What agents did in this browser...", null, delegate
            {
                var lines = ActionLog();
                string text = lines.Length == 0 ? "No agent has used the browser since the app started." : string.Join("\r\n", lines.Reverse().Take(300));
                var f = new Form { Text = "Browser: agent activity", Width = Ui.S(640), Height = Ui.S(480), StartPosition = FormStartPosition.CenterParent, ShowIcon = false, MinimizeBox = false };
                f.Controls.Add(new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = text, Font = Ui.Base });
                Ui.Theme(f); f.Show(FindForm());
            });
            var auto = new ToolStripMenuItem("Allow automation tools (port " + AutomationPort + ", this PC only)") { Checked = Store.Settings.BrowserAutomation };
            auto.Click += delegate
            {
                Store.Settings.BrowserAutomation = !Store.Settings.BrowserAutomation; Store.MarkSettings();
                MessageBox.Show(FindForm(), (Store.Settings.BrowserAutomation
                    ? "On after the app restarts. Tools connect with:\nhttp://localhost:" + AutomationPort + "\n(Playwright: chromium.connect_over_cdp)"
                    : "Off after the app restarts."), "Browser");
            };
            m.Items.Add(auto);
            if (Store.Settings.BrowserAutomation) m.Items.Add("Copy automation address", null, delegate { App.I.CopyText("http://localhost:" + AutomationPort); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Open this page in Chrome", null, delegate { if (active != null && active.Url.StartsWith("http")) try { Process.Start(new ProcessStartInfo("chrome.exe", "\"" + active.Url + "\"") { UseShellExecute = true }); } catch { try { Process.Start(active.Url); } catch { } } });
            m.Items.Add("Copy address", null, delegate { if (active != null) App.I.CopyText(active.Url); });
            m.Items.Add("Set this page as home", null, delegate { if (active != null) { Store.Settings.BrowserHome = active.Url; Store.MarkSettings(); } });
            Ui.ThemeMenu(m);
            m.Show(anchor, new Point(0, anchor.Height));
        }

        async void SendSelection()
        {
            if (active == null || active.View.CoreWebView2 == null) return;
            Tab tab = active;
            string text = Unjson(await ReadPageScript(tab, "window.getSelection().toString()"));
            if (IsDisposed || active != tab) return;
            if (string.IsNullOrWhiteSpace(text)) { status.Text = "Select some text on the page first."; return; }
            var sm = Ui.Menu(); var send = AppLinks.SendTo(null, text, FindForm());
            while (send.DropDownItems.Count > 0) { var it = send.DropDownItems[0]; send.DropDownItems.RemoveAt(0); sm.Items.Add(it); }
            Ui.ThemeMenu(sm); sm.Show(Cursor.Position);
        }

        // The page's readable text into a new note in the Markdown vault (Clippings folder), then open it.
        async void SaveAsNote()
        {
            if (active == null || active.View.CoreWebView2 == null) return;
            string vault = MdVault.Current;
            if (string.IsNullOrEmpty(vault)) { status.Text = "Add a Markdown vault first (Markdown notes > Vaults)."; return; }
            Tab tab = active;
            try
            {
                string json = await ReadPageScript(tab, "({title: document.title, url: location.href, text: (document.querySelector('article,main') || document.body).innerText})");
                if (IsDisposed) return;
                if (json == null) { status.Text = "The page changed before it could be saved. Try again."; return; }
                var clip = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }
                    .Deserialize<Dictionary<string, string>>(json);
                if (clip == null || !clip.ContainsKey("text")) { status.Text = "Could not read this page."; return; }
                string name = string.IsNullOrWhiteSpace(clip["title"]) ? clip["url"] : clip["title"];
                string file = MdVault.SaveClipping(vault, name, clip["url"], clip["text"]);
                MdVault.Index(true);
                if (active == tab) App.I.OpenMarkdown(file);
                else status.Text = "Saved to Clippings: " + Path.GetFileName(file);
            }
            catch (Exception ex) { if (!IsDisposed) status.Text = "Could not save this page: " + ex.Message; }
        }

        async Task<string> ReadPageScript(Tab tab, string script)
        {
            try
            {
                int navigation = tab.NavigationRevision;
                var core = tab.View.CoreWebView2;
                string source = core.Source;
                string json = await core.ExecuteScriptAsync(script);
                return !IsDisposed && tabs.Contains(tab) && navigation == tab.NavigationRevision
                    && string.Equals(source, core.Source, StringComparison.Ordinal) ? json : null;
            }
            catch (Exception ex) { App.Log("Read browser page: " + ex.Message); return null; }
        }

        static string Unjson(string json)
        {
            try { return new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<string>(json); } catch { return ""; }
        }

        // ---------- files, emails, reports ----------
        void OpenFile()
        {
            using (var d = new OpenFileDialog { Filter = "Reports, PDF, emails, web pages|*.pdf;*.htm;*.html;*.mht;*.mhtml;*.eml;*.msg;*.mysingle;*.svg;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.txt;*.json;*.xml;*.csv;*.md|All files|*.*", Multiselect = true })
                if (d.ShowDialog(FindForm()) == DialogResult.OK) foreach (var f in d.FileNames) OpenPath(f);
        }

        // A local file in a new tab. Emails are turned into a page first (.eml read here, .msg through Outlook).
        public void OpenPath(string path)
        {
            try
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".eml") path = Mail.EmlToHtml(path);
                else if (ext == ".msg") path = Mail.MsgToHtml(path);
                else if (ext == ".mysingle") { status.Text = "Reading the protected mail through Word..."; Application.DoEvents(); path = Mail.MysingleToHtml(path); }
                if (path == null) return;
                AddTab(new Uri(path).AbsoluteUri, true);
            }
            catch (Exception ex) { status.Text = "Could not open the file: " + ex.Message; }
        }

        // Open a local HTML file (Live Canvas / dashboard) reusing its existing tab if already open, otherwise opening cleanly.
        public void OpenOrReloadFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                string uri = new Uri(Path.GetFullPath(path)).AbsoluteUri;
                string baseUri = StripUriQuery(uri);
                for (int i = 0; i < tabs.Count; i++)
                {
                    var t = tabs[i];
                    if (string.Equals(StripUriQuery(t.Url), baseUri, StringComparison.OrdinalIgnoreCase))
                    {
                        Activate(t);
                        t.Url = uri;
                        if (t.View.CoreWebView2 != null) t.View.CoreWebView2.Navigate(uri);
                        return;
                    }
                }
                AddTab(uri, true);
            }
            catch (Exception ex) { status.Text = "Could not open the file: " + ex.Message; }
        }

        static string StripUriQuery(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return "";
            int q = uri.IndexOfAny(new[] { '?', '#' });
            return q >= 0 ? uri.Substring(0, q) : uri;
        }

        static string Site(string url) { try { var u = new Uri(url); return u.Scheme.StartsWith("http") ? u.Host.ToLowerInvariant() : ""; } catch { return ""; } }

        void Zoom(double step)
        {
            if (active == null) return;
            active.View.ZoomFactor = step == 0 ? 1 : Math.Max(0.25, Math.Min(5, active.View.ZoomFactor + step));
            string site = Site(active.Url);   // remembered per site
            if (site.Length > 0) { if (Math.Abs(active.View.ZoomFactor - 1) < 0.01) Store.Settings.BrowserZoom.Remove(site); else Store.Settings.BrowserZoom[site] = active.View.ZoomFactor; Store.MarkSettings(); }
            status.Text = StatusLine();
        }

        // Reading mode: the page's main text alone, in a calm, readable column (reports, articles, mails).
        // Again (or Reload) brings the page back.
        async void ReadingMode()
        {
            if (active == null || active.View.CoreWebView2 == null) return;
            string bg = Hex(Ui.Card), fg = Hex(Ui.Text), muted = Hex(Ui.Muted), line = Hex(Ui.Line), accent = Hex(Ui.Accent);
            string js = @"(function(){
 if(window.__aioReading){location.reload();return 'off';}
 var c=document.querySelector('article')||document.querySelector('main')||document.querySelector('[role=main]');
 if(!c){var best=null,score=0;document.querySelectorAll('div,section,td').forEach(function(e){var p=e.querySelectorAll(':scope > p').length,t=(e.innerText||'').length;var s=p*400+t*0.1;if(s>score&&t>300){score=s;best=e}});c=best||document.body}
 var clone=c.cloneNode(true);clone.querySelectorAll('script,style,nav,aside,footer,header,form,iframe,button,svg,noscript,[role=navigation],[aria-hidden=true]').forEach(function(e){e.remove()});
 var title=document.title;document.head.innerHTML='<meta charset=utf-8><title>'+title.replace(/</g,'&lt;')+'</title>';
 var st=document.createElement('style');st.textContent='body{margin:0;background:BG;color:FG;font:18px/1.7 Georgia,Cambria,serif}main{max-width:760px;margin:0 auto;padding:40px 28px 90px}h1,h2,h3{font-family:Segoe UI,sans-serif;line-height:1.3}h1{font-size:30px}a{color:ACC}img,video{max-width:100%;height:auto;border-radius:8px}pre,code{font:14px Cascadia Mono,Consolas,monospace;white-space:pre-wrap}table{border-collapse:collapse;font:15px Segoe UI,sans-serif}td,th{border:1px solid LINE;padding:6px 10px}blockquote{border-left:4px solid ACC;margin:1em 0;padding:.2em 1em;color:MUT}.src{font:13px Segoe UI,sans-serif;color:MUT}';
 document.head.appendChild(st);document.body.innerHTML='';var m=document.createElement('main');m.innerHTML='<div class=src>'+location.hostname+' &middot; reading mode (F9 again to leave)</div><h1>'+title.replace(/</g,'&lt;')+'</h1>';m.appendChild(clone);document.body.appendChild(m);
 window.__aioReading=true;scrollTo(0,0);return 'on';})()";
            js = js.Replace("BG", bg).Replace("FG", fg).Replace("MUT", muted).Replace("LINE", line).Replace("ACC", accent);
            try { string r = await active.View.CoreWebView2.ExecuteScriptAsync(js); status.Text = r.Contains("on") ? "Reading mode. F9 again (or Reload) shows the full page." : StatusLine(); }
            catch (Exception ex) { status.Text = "Reading mode: " + ex.Message; }
        }
        static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        async void FindInPage()
        {
            if (active == null || active.View.CoreWebView2 == null) return;
            string q = Ui.Ask("Find on page", "Text (Enter again finds the next one)", lastFind, FindForm());
            if (string.IsNullOrEmpty(q)) return;
            lastFind = q;
            string found = await active.View.CoreWebView2.ExecuteScriptAsync("window.find(" + Js(q) + ", false, false, true)");
            status.Text = found == "true" ? "Found \"" + q + "\". Ctrl+F again for the next one." : "\"" + q + "\" is not on this page.";
        }
        string lastFind = "";
        static string Js(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\""; }

        void Print() { if (active != null && active.View.CoreWebView2 != null) try { active.View.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); } catch (Exception ex) { status.Text = "Print: " + ex.Message; } }

        string AskSave(string filter, string name)
        {
            using (var d = new SaveFileDialog { Filter = filter, FileName = name })
                return d.ShowDialog(FindForm()) == DialogResult.OK ? d.FileName : null;
        }
        static string SafeName(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = (s ?? "").Replace(c, '-'); s = s.Trim(); return s.Length == 0 ? "page" : s.Length > 80 ? s.Substring(0, 80) : s; }

        async void SavePdf()
        {
            if (active == null || active.View.CoreWebView2 == null) return;
            string file = AskSave("PDF|*.pdf", SafeName(active.Name) + ".pdf");
            if (file == null) return;
            status.Text = "Saving PDF...";
            bool ok = await active.View.CoreWebView2.PrintToPdfAsync(file, null);
            status.Text = ok ? "Saved " + file : "Could not save the PDF.";
        }

        async void SaveShot()
        {
            if (active == null || active.View.CoreWebView2 == null) return;
            string file = AskSave("PNG image|*.png", SafeName(active.Name) + ".png");
            if (file == null) return;
            using (var fs = File.Create(file)) await active.View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            status.Text = "Saved " + file;
        }

        // ---------- scripts ----------
        async void RunScriptPrompt()
        {
            if (active == null || active.View.CoreWebView2 == null) return;
            string js = Ui.Ask("Run JavaScript", "Code (the result is shown below the page)", "document.title", FindForm());
            if (string.IsNullOrWhiteSpace(js)) return;
            try { status.Text = "Result: " + await active.View.CoreWebView2.ExecuteScriptAsync(js); }
            catch (Exception ex) { status.Text = "Script error: " + ex.Message; }
        }

        static void WriteSample()
        {
            string p = Path.Combine(ScriptsDir, "example.js.off");
            if (!File.Exists(p))
                try { File.WriteAllText(p, "// @match *example.com*\n// Rename to .js to turn it on. Runs after every page load that matches.\ndocument.body.style.outline = '4px solid orange';\n"); } catch { }
        }

        async void RunPageScripts(Tab t)
        {
            string dir = ScriptsDir;
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.js"))
            {
                try
                {
                    string code = File.ReadAllText(f);
                    var m = Regex.Match(code, @"^\s*//\s*@match\s+(\S+)");
                    if (m.Success && !Regex.IsMatch(t.Url, "^" + Regex.Escape(m.Groups[1].Value).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase)) continue;
                    if (t.View.CoreWebView2 != null) await t.View.CoreWebView2.ExecuteScriptAsync(code);
                }
                catch (Exception ex) { App.Log("Page script " + Path.GetFileName(f) + ": " + ex.Message); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Store.MetaChanged -= OnMeta; foreach (var t in tabs) t.View.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
