using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Updates for the agent CLIs: the installed version (from the CLI itself), the newest version (npm registry),
    // and a one-click update that runs the CLI's own update command in a visible terminal. Checked in the background
    // once a day; a balloon tells when updates are waiting.
    // ===================================================================================================

    class CliInfo
    {
        public string Name = "", Exe = "", VersionArgs = "--version", Npm = "", UpdateCmd = "", Installed = "", Latest = "", Note = "";
        public bool Found;
        public bool HasUpdate { get { return Found && Installed.Length > 0 && Latest.Length > 0 && CliUpdates.Newer(Latest, Installed); } }
        public string Status { get { return !Found ? "not installed" : Latest.Length == 0 ? "latest unknown" : HasUpdate ? "update available" : "up to date"; } }
    }

    static class CliUpdates
    {
        public static List<CliInfo> Last = new List<CliInfo>();
        public static DateTime CheckedAt;
        public static event Action Updated;
        static int busy;

        static List<CliInfo> Catalog()
        {
            return new List<CliInfo>
            {
                new CliInfo { Name = "Claude Code", Exe = "claude", Npm = "@anthropic-ai/claude-code", UpdateCmd = "claude update" },
                new CliInfo { Name = "Codex", Exe = "codex", Npm = "@openai/codex", UpdateCmd = "npm install -g @openai/codex@latest" },
                new CliInfo { Name = "Cline", Exe = "cline", Npm = "cline", UpdateCmd = "npm install -g cline@latest", VersionArgs = "--version" },
                new CliInfo { Name = "Antigravity", Exe = "agy", UpdateCmd = "agy update", Note = "No public version list: Update runs its own updater." },
                new CliInfo { Name = "OpenCode", Exe = "opencode", Npm = "opencode-ai", UpdateCmd = "opencode upgrade" },
            };
        }

        // Once a day in the background (and on demand from the window).
        public static void AutoCheck()
        {
            if (DateTime.Now - Store.Settings.CliCheckedAt < TimeSpan.FromHours(20)) return;
            Check(list =>
            {
                Store.Settings.CliCheckedAt = DateTime.Now; Store.MarkSettings();
                int n = list.Count(x => x.HasUpdate);
                if (n > 0) App.I.Balloon("Agent CLI updates", n + " update" + (n == 1 ? "" : "s") + " ready: " + string.Join(", ", list.Where(x => x.HasUpdate).Select(x => x.Name)) + ". Agents page > Updates.");
            });
        }

        public static void Check(Action<List<CliInfo>> done)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                var list = Catalog();
                var threads = list.Select(c => { var t = new Thread(() => Fill(c)) { IsBackground = true }; t.Start(); return t; }).ToList();
                foreach (var t in threads) t.Join(TimeSpan.FromSeconds(90));
                Last = list; CheckedAt = DateTime.Now;
                Interlocked.Exchange(ref busy, 0);
                try { App.I.Sync.BeginInvoke(new Action(() => { if (done != null) done(list); var h = Updated; if (h != null) h(); })); } catch { }
            });
        }
        public static bool Busy { get { return busy == 1; } }

        static void Fill(CliInfo c)
        {
            try
            {
                string file, prefix;
                c.Found = AgentRun.Resolve(c.Exe, out file, out prefix);
                if (c.Found)
                {
                    c.Installed = FromPackage(c) ?? Ver(Run(file, prefix + c.VersionArgs, 60000));
                }
                if (c.Npm.Length > 0)
                {
                    var req = (HttpWebRequest)WebRequest.Create("https://registry.npmjs.org/" + c.Npm.Replace("/", "%2F") + "/latest");
                    req.Timeout = 20000; req.UserAgent = "AllInOne";
                    // Company networks: use the same proxy the CLIs use (HTTPS_PROXY), else the Windows proxy.
                    string px = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("HTTP_PROXY");
                    if (!string.IsNullOrEmpty(px)) try { req.Proxy = new WebProxy(px) { UseDefaultCredentials = true }; } catch { }
                    using (var r = req.GetResponse()) using (var sr = new StreamReader(r.GetResponseStream()))
                    {
                        var d = new JavaScriptSerializer().DeserializeObject(sr.ReadToEnd()) as Dictionary<string, object>;
                        object v; if (d != null && d.TryGetValue("version", out v)) c.Latest = Convert.ToString(v);
                    }
                }
            }
            catch (Exception ex) { c.Note = (c.Note.Length > 0 ? c.Note + " " : "") + "Check failed: " + ex.Message; }
        }

        // npm installs: the version in the global package.json is exact and instant.
        static string FromPackage(CliInfo c)
        {
            if (c.Npm.Length == 0) return null;
            try
            {
                string pj = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", c.Npm.Replace('/', '\\'), "package.json");
                if (!File.Exists(pj)) return null;
                var d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(pj)) as Dictionary<string, object>;
                object v; return d != null && d.TryGetValue("version", out v) ? Convert.ToString(v) : null;
            }
            catch { return null; }
        }

        static string Run(string file, string args, int ms)
        {
            bool viaCmd = file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && args.StartsWith("/d");
            var psi = new ProcessStartInfo(file, args + (viaCmd ? "\"" : "")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
            using (var p = Process.Start(psi))
            {
                p.StandardInput.Close();
                var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } return ""; }
                return o.Result + "\n" + e.Result;
            }
        }

        static string Ver(string text) { var m = Regex.Match(text ?? "", @"\d+\.\d+(\.\d+)?([-.][0-9A-Za-z.]+)?"); return m.Success ? m.Value : ""; }

        public static bool Newer(string a, string b)
        {
            Func<string, int[]> parts = s => Regex.Matches(s ?? "", @"\d+").Cast<Match>().Take(4).Select(m => { int n; int.TryParse(m.Value, out n); return n; }).ToArray();
            var x = parts(a); var y = parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++) { int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0; if (p != q) return p > q; }
            return false;
        }

        // Runs the update in a terminal the user can watch (some updaters ask a question or need a restart).
        public static void Update(CliInfo c)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Term.Open(home, c.UpdateCmd + "; Write-Host ''; Write-Host 'Done. You can close this window.' -ForegroundColor Green", "pwsh");
        }

        public static void Show(IWin32Window owner) { using (var d = new CliUpdatesDialog()) d.ShowDialog(owner); }
    }

    class CliUpdatesDialog : Form
    {
        readonly ListView lv;
        readonly Label status;
        public CliUpdatesDialog()
        {
            Text = "Agent CLI updates"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(Ui.S(880), Ui.S(380)); Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text;
            lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, BorderStyle = BorderStyle.FixedSingle, HideSelection = false };
            foreach (var c in new[] { new[] { "CLI", "130" }, new[] { "Installed", "110" }, new[] { "Newest", "110" }, new[] { "Status", "140" }, new[] { "Update command", "330" } }) lv.Columns.Add(c[0], Ui.S(int.Parse(c[1])));
            status = new Label { Dock = DockStyle.Bottom, Height = Ui.S(26), ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, Ui.S(6), 0, 0) };
            bar.Controls.Add(Ui.Btn("Update selected", delegate { foreach (var c in Selected()) CliUpdates.Update(c); }, true));
            bar.Controls.Add(Ui.Btn("Update all that have an update", delegate { var l = CliUpdates.Last.Where(x => x.HasUpdate).ToList(); if (l.Count == 0) status.Text = "Everything is up to date."; foreach (var c in l) CliUpdates.Update(c); }));
            bar.Controls.Add(Ui.Btn("Check again", delegate { Check(); }));
            bar.Controls.Add(Ui.Btn("Close", delegate { Close(); }));
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(12)) };
            pad.Controls.Add(lv); pad.Controls.Add(bar); pad.Controls.Add(status);
            Controls.Add(pad);
            Ui.Theme(this);
            lv.DoubleClick += delegate { foreach (var c in Selected()) CliUpdates.Update(c); };
            Action fill = Fill; CliUpdates.Updated += fill; FormClosed += delegate { CliUpdates.Updated -= fill; };
            Fill();
            Check();
        }
        List<CliInfo> Selected() { return lv.SelectedItems.Cast<ListViewItem>().Select(i => (CliInfo)i.Tag).Where(c => c.Found || c.UpdateCmd.StartsWith("npm")).ToList(); }
        void Check() { status.Text = "Checking installed and newest versions..."; CliUpdates.Check(null); }
        void Fill()
        {
            if (IsDisposed) return;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var c in CliUpdates.Last)
            {
                var it = new ListViewItem(new[] { c.Name, c.Installed.Length > 0 ? c.Installed : "-", c.Latest.Length > 0 ? c.Latest : "-", c.Status, c.UpdateCmd }) { Tag = c };
                if (c.HasUpdate) it.ForeColor = Ui.Accent; else if (!c.Found) it.ForeColor = Ui.Muted;
                if (c.Note.Length > 0) it.ToolTipText = c.Note;
                lv.Items.Add(it);
            }
            lv.ShowItemToolTips = true;
            lv.EndUpdate();
            int n = CliUpdates.Last.Count(x => x.HasUpdate);
            if (CliUpdates.Last.Count > 0) status.Text = (n == 0 ? "All installed CLIs are up to date." : n + " update(s) available. Select and press Update (it opens a terminal so you can watch).") + "   Checked " + CliUpdates.CheckedAt.ToString("HH:mm") + ".";
        }
    }
}
