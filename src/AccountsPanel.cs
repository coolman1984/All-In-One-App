using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // Accounts for the agent CLIs, in one place (Browser > Accounts, Agents > account menus use the same data).
    // Shows who is signed in to Claude Code, Codex, Cline, OpenCode and Antigravity, signs in (the CLI opens its own
    // sign-in page in your browser), adds named accounts (Claude Code / Codex), and picks the account new chats use.
    // Only file names and key names are read; no token or key value is ever read or shown.
    static class CliAccounts
    {
        static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

        // "Signed in" for the CLI's own default account, from the presence of its credential file.
        public static string Status(string id)
        {
            try
            {
                switch (id)
                {
                    case "claude":
                        if (File.Exists(Path.Combine(AgentAccounts.Home("claude", ""), ".credentials.json"))) return "Signed in";
                        return Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") != null ? "API key in the environment" : "Not signed in";
                    case "codex":
                        return File.Exists(Path.Combine(AgentAccounts.Home("codex", ""), "auth.json")) ? "Signed in" : "Not signed in";
                    case "cline":
                        var keys = ClineKeys();
                        return keys.Count > 0 ? "Signed in: " + string.Join(", ", keys) : "Not signed in";
                    case "opencode":
                        return File.Exists(Path.Combine(Home, ".local", "share", "opencode", "auth.json")) ? "Signed in" : "Not signed in";
                    case "agy":
                        return Antigravity();
                    default:
                        return "Sign-in is checked by the CLI itself";
                }
            }
            catch (Exception ex) { return "Unknown (" + ex.Message + ")"; }
        }

        // Antigravity CLI keeps its Google sign-in in Windows Credential Manager ("gemini:antigravity") and writes
        // "OAuth: authenticated successfully as <email>" to ~\.gemini\antigravity-cli\cli.log. Only the entry's existence
        // and the log line are read; the token itself is never touched.
        public static string Antigravity()
        {
            bool keyring = HasCredential("gemini:antigravity");
            string email = null;
            string log = Path.Combine(Home, ".gemini", "antigravity-cli", "cli.log");
            try
            {
                if (File.Exists(log))
                    using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (fs.Length > 2000000) fs.Seek(-2000000, SeekOrigin.End);
                        string text = new StreamReader(fs).ReadToEnd();
                        var all = System.Text.RegularExpressions.Regex.Matches(text, @"authenticated successfully as (\S+@\S+)");
                        if (all.Count > 0) email = all[all.Count - 1].Groups[1].Value.Trim();
                    }
            }
            catch { }
            if (!keyring) return "Not signed in" + (email != null ? " (last account: " + email + ")" : "");
            return "Signed in" + (email != null ? " as " + email : "");
        }

        public static void SignOutAntigravity()
        {
            CredDelete("gemini:antigravity", 1 /* CRED_TYPE_GENERIC */, 0);
        }

        static bool HasCredential(string target)
        {
            int count; IntPtr list;
            if (!CredEnumerate(target + "*", 0, out count, out list)) return false;
            CredFree(list);
            return count > 0;
        }
        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);
        [System.Runtime.InteropServices.DllImport("advapi32.dll")] static extern void CredFree(IntPtr buffer);
        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool CredDelete(string target, int type, int flags);

        const string ClineStatusScript = @"
console.log = console.info = console.warn = console.error = () => {};
const c = require(process.argv[1]);
(async () => {
    const manager = new c.ProviderSettingsManager();
    await c.ensureCustomProvidersLoaded(manager);
    const { providers } = await c.listLocalProviders(manager, { isClinePassEnabled: true });
    const ids = providers.filter(p => p.oauthAccessTokenPresent === true ||
        (typeof p.apiKey === 'string' && p.apiKey.trim().length > 0)).map(p => p.id);
    process.stdout.write(JSON.stringify(ids), () => process.exit(0));
})().catch(() => process.exit(1));";

        public static string ClineStatus(string exe)
        {
            string[] ids = null;
            try
            {
                string output = AgentModels.RunClineCore(exe, ClineStatusScript);
                if (output != null) ids = new JavaScriptSerializer().Deserialize<string[]>(output);
            }
            catch { }

            if (ids == null)
            {
                var legacy = ClineKeys();
                return legacy.Count > 0 ? "Signed in: " + string.Join(", ", legacy) : "Could not check Cline sign-in";
            }
            if (ids.Length == 0) return "Not signed in";

            bool hasPass = ids.Contains("cline-pass");
            var labels = ids.Where(id => !hasPass || id != "cline").Select(id =>
                id == "cline-pass" ? "Cline Pass" : id == "cline" ? "Cline account" :
                id.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0 ? "ChatGPT (Codex)" : id);
            return "Signed in: " + string.Join(", ", labels.Distinct());
        }

        // Legacy file-based Cline installs: show provider names only.
        static List<string> ClineKeys()
        {
            var l = new List<string>();
            string f = Path.Combine(Home, ".cline", "data", "secrets.json");
            if (!File.Exists(f)) return l;
            var o = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>;
            if (o == null) return l;
            foreach (var kv in o)
            {
                if (kv.Value == null || Convert.ToString(kv.Value).Length == 0) continue;
                string k = kv.Key;
                string name = k.StartsWith("cline") ? "Cline account" : k.Contains("codex") ? "ChatGPT (Codex)" : k.Replace("ApiKey", "").Replace("-oauth-credentials", "");
                if (!l.Contains(name)) l.Add(name);
            }
            return l;
        }

        public static bool NamedAccountSignedIn(string id, string name)
        {
            string home = AgentAccounts.Home(id, name);
            return File.Exists(Path.Combine(home, id == "codex" ? "auth.json" : ".credentials.json"));
        }

        // Sign in to the CLI's own default account: a visible window, because the CLI asks you questions.
        public static void SignInDefault(AgentDef d)
        {
            string args = d.Id == "claude" ? "auth login" : d.Id == "codex" ? "login" : d.Id == "cline" ? "auth" : d.Id == "opencode" ? "auth login" : "";
            Term.Open(Home, (d.Exe ?? d.Id) + (args.Length > 0 ? " " + args : ""));
        }
    }

    class AccountsPanel : Form
    {
        readonly FlowLayoutPanel rows;
        static readonly string[] Order = { "claude", "codex", "cline", "opencode", "agy" };

        public static void Open(IWin32Window owner) { App.I.ShowMain("accounts"); }   // the Accounts page replaces this window

        AccountsPanel()
        {
            Text = "Agent accounts"; Icon = App.AppIcon; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text; ClientSize = Ui.S(760, 600); MinimumSize = Ui.S(520, 360);
            var head = new Label { Dock = DockStyle.Top, Height = Ui.S(56), Padding = new Padding(Ui.S(18), Ui.S(10), Ui.S(18), 0), Font = Ui.Big, Text = "Agent accounts" };
            var note = new Label { Dock = DockStyle.Top, Height = Ui.S(40), Padding = new Padding(Ui.S(18), 0, Ui.S(18), 0), ForeColor = Ui.Muted,
                Text = "Sign in once here; every page of the app (Agents, council, leader team, terminals) uses the account. Sign-in opens in your browser. No keys are shown." };
            rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(Ui.S(14), 0, Ui.S(14), Ui.S(14)) };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(52), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), 0) };
            bar.Controls.Add(Ui.Btn("Close", delegate { Close(); }));
            bar.Controls.Add(Ui.Btn("Refresh", delegate { Fill(); }, true));
            Controls.Add(rows); Controls.Add(bar); Controls.Add(note); Controls.Add(head);
            Ui.Theme(this); Ui.DarkTitle(this);
            Activated += delegate { Fill(); };   // back from a sign-in window: show the new state
            Fill();
        }

        void Fill()
        {
            rows.SuspendLayout();
            Ui.ClearControls(rows.Controls);
            int w = Math.Max(Ui.S(400), rows.ClientSize.Width - Ui.S(40));
            foreach (var d in AgentStore.Defs.OrderBy(x => { int i = Array.IndexOf(Order, x.Id); return i < 0 ? 99 : i; }))
            {
                string file, prefix; bool installed = AgentRun.Resolve(d.Exe, out file, out prefix);
                var card = new Panel { Width = w, Height = Ui.S(74), Margin = new Padding(0, 0, 0, Ui.S(10)), BackColor = Ui.Card, Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(10)) };
                string status = installed ? CliAccounts.Status(d.Id) : "Not installed";
                bool ok = status.StartsWith("Signed in") || status.StartsWith("API key");
                var name = new Label { AutoSize = false, Location = new Point(Ui.S(14), Ui.S(10)), Size = new Size(Ui.S(260), Ui.S(24)), Font = Ui.Bold, Text = d.Name, BackColor = Ui.Card };
                var st = new Label { AutoSize = false, AutoEllipsis = true, Location = new Point(Ui.S(14), Ui.S(36)), Size = new Size(w - Ui.S(330), Ui.S(22)), BackColor = Ui.Card,
                    ForeColor = !installed ? Ui.Muted : ok ? Ui.Success : Ui.Warning, Text = (ok ? "● " : "○ ") + status };
                card.Controls.Add(name); card.Controls.Add(st);
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Ui.S(300), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Card, WrapContents = false, Padding = new Padding(0, Ui.S(8), 0, 0) };
                var def = d;
                if (installed)
                {
                    buttons.Controls.Add(Ui.Btn(ok ? "Sign in again" : "Sign in", delegate { CliAccounts.SignInDefault(def); }, !ok));
                    if (AgentAccounts.Supported(d.Id))
                    {
                        var more = Ui.Btn("Accounts", null);
                        more.Click += delegate { NamedMenu(def, more); };
                        buttons.Controls.Add(more);
                    }
                }
                card.Controls.Add(buttons);
                card.Paint += (s, e) => { using (var p = new Pen(Ui.Line)) e.Graphics.DrawRectangle(p, 0, 0, card.Width - 1, card.Height - 1); };
                rows.Controls.Add(card);
                if (AgentAccounts.Supported(d.Id)) AddNamedRows(d, w);
            }
            rows.ResumeLayout();
        }

        // Named accounts (Claude Code, Codex): one line each, the one new chats use is marked.
        void AddNamedRows(AgentDef d, int w)
        {
            string[] names; try { names = AgentAccounts.Names(d.Id); } catch { return; }
            string preferred; Store.Settings.AgentDefaultAccount.TryGetValue(d.Id, out preferred); preferred = preferred ?? "";
            foreach (var n in names)
            {
                string acc = n;
                bool signed = CliAccounts.NamedAccountSignedIn(d.Id, n);
                var line = new Label
                {
                    AutoSize = false, Width = w, Height = Ui.S(28), Margin = new Padding(Ui.S(24), 0, 0, Ui.S(6)), Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleLeft,
                    Text = "   " + (preferred == n ? "★ " : "   ") + d.Name + " · " + n + "      " + (signed ? "signed in" : "not signed in") + (preferred == n ? "      (new chats use this)" : ""),
                    ForeColor = signed ? Ui.Text : Ui.Muted
                };
                var def = d;
                line.Click += delegate { NamedMenu(def, line, acc); };
                rows.Controls.Add(line);
            }
        }

        void NamedMenu(AgentDef d, Control anchor, string account = null)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            if (account != null)
            {
                m.Items.Add("New chats use " + account, null, delegate { Store.Settings.AgentDefaultAccount[d.Id] = account; Store.MarkSettings(); Fill(); });
                m.Items.Add("Sign in to " + account + " again", null, delegate { try { AgentAccounts.Login(d, account); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); } });
                m.Items.Add("Open its folder", null, delegate { App.I.OpenInExplorerPage(AgentAccounts.Home(d.Id, account)); });
                m.Items.Add(new ToolStripSeparator());
            }
            m.Items.Add("New chats use the CLI's own account", null, delegate { Store.Settings.AgentDefaultAccount.Remove(d.Id); Store.MarkSettings(); Fill(); });
            m.Items.Add("Add a named account (sign in)...", null, delegate
            {
                string name = Ui.Ask(d.Name + " account", "A short name (letters, numbers, - or _), for example work or personal", "", this);
                if (string.IsNullOrWhiteSpace(name)) return;
                name = name.Trim().ToLowerInvariant();
                if (!AgentAccounts.Valid(name)) { MessageBox.Show(this, "Use letters, numbers, - or _ (up to 24).", Text); return; }
                try { AgentAccounts.Login(d, name); Store.Settings.AgentDefaultAccount[d.Id] = name; Store.MarkSettings(); Fill(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            });
            m.Show(anchor, new Point(0, anchor.Height));
        }
    }
}
