using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StickyPro
{
    static class AgentAccounts
    {
        public static bool Supported(string id) { return id == "codex" || id == "claude"; }
        static string Root(string id)
        {
            if (!Supported(id)) throw new ArgumentException("This CLI does not support named accounts.");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".herdr-accounts", id);
        }
        public static bool Valid(string name)
        {
            if (name == null || !Regex.IsMatch(name, @"\A[a-z0-9][a-z0-9_-]{0,23}\z")) return false;
            return !Regex.IsMatch(name, @"^(con|prn|aux|nul|com[1-9]|lpt[1-9])$");
        }
        public static string[] Names(string id)
        {
            string root = Root(id);
            if (!Directory.Exists(root)) return new string[0];
            return Directory.GetDirectories(root).Select(Path.GetFileName).Where(Valid).OrderBy(x => x).ToArray();
        }
        public static string Selected(AgentChat chat, string id)
        {
            string name;
            if (!Supported(id)) return "";
            if (chat != null && chat.Accounts != null && chat.Accounts.TryGetValue(id, out name)) return name ?? "";
            // No choice in this chat: the account picked in Browser > Accounts (if it still exists).
            string preferred;
            if (Store.Settings.AgentDefaultAccount.TryGetValue(id, out preferred) && Valid(preferred) && Directory.Exists(Path.Combine(Root(id), preferred))) return preferred;
            return "";
        }
        // The CLI home folder of an account: where Codex writes its session logs (sessions\...\rollout-*.jsonl).
        // Empty name = the CLI's default home (CODEX_HOME / CLAUDE_CONFIG_DIR when set, else ~\.codex / ~\.claude).
        public static string Home(string id, string name)
        {
            if (!string.IsNullOrEmpty(name) && Supported(id) && Valid(name)) return Path.Combine(Root(id), name);
            string env = Environment.GetEnvironmentVariable(id == "codex" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), id == "codex" ? ".codex" : ".claude");
        }
        // Terminals opened from the app run "claude" / "codex" with the default account you chose on the Accounts page
        // (no sign-out needed): the command first points the CLI at that account's folder.
        public static string TerminalCommand(string shell, string command)
        {
            string c = (command ?? "").Trim();
            string id = Starts(c, "claude") ? "claude" : Starts(c, "codex") ? "codex" : null;
            if (id == null || Store.Settings == null || Store.Settings.AgentDefaultAccount == null) return c;
            string acc; if (!Store.Settings.AgentDefaultAccount.TryGetValue(id, out acc) || string.IsNullOrEmpty(acc) || !Valid(acc)) return c;
            string dir = Path.Combine(Root(id), acc); if (!Directory.Exists(dir)) return c;
            string v = id == "codex" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR";
            if (shell == "cmd") return "set \"" + v + "=" + dir + "\"&& " + c;
            return "$env:" + v + "='" + dir.Replace("'", "''") + "'; " + c;
        }
        static bool Starts(string c, string word) { return c.StartsWith(word, StringComparison.OrdinalIgnoreCase) && (c.Length == word.Length || c[word.Length] == ' '); }

        public static string Session(string key, string account)
        {
            return string.IsNullOrEmpty(account) ? key : key + "|account:" + account;
        }
        internal static void Configure(ProcessStartInfo start, string id, string name, string root, bool login)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!Supported(id) || !Valid(name)) throw new ArgumentException("Invalid CLI account name.");
            string folder = Path.Combine(root, name);
            if (login) Directory.CreateDirectory(folder);
            else if (!Directory.Exists(folder)) throw new IOException("Account folder missing. Sign in to " + name + " again.");
            start.EnvironmentVariables[id == "codex" ? "CODEX_HOME" : "CLAUDE_CONFIG_DIR"] = folder;
            foreach (string variable in id == "codex" ? new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN" } :
                new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CODE_USE_BEDROCK", "CLAUDE_CODE_USE_VERTEX", "CLAUDE_CODE_USE_FOUNDRY" })
                start.EnvironmentVariables.Remove(variable);
        }
        public static void Apply(ProcessStartInfo start, string id, string name)
        {
            if (!string.IsNullOrEmpty(name)) Configure(start, id, name, Root(id), false);
        }
        public static void Login(AgentDef provider, string name)
        {
            string file, prefix;
            if (!AgentRun.Resolve(provider.Exe, out file, out prefix)) throw new IOException(provider.Name + " is not installed.");
            string args = provider.Id == "codex" ? "-c cli_auth_credentials_store=file login" : "auth login";
            var psi = new ProcessStartInfo(file, prefix + args + (prefix.StartsWith("/d") ? "\"" : ""))
                { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            Configure(psi, provider.Id, name, Root(provider.Id), true);
            using (Process.Start(psi)) { }
        }
        public static void AddMenu(ToolStripItemCollection items, AgentChat chat, AgentDef provider, bool busy, Action changed, IWin32Window owner)
        {
            if (!Supported(provider.Id)) return;
            string selected = Selected(chat, provider.Id);
            var menu = new ToolStripMenuItem("Account · " + (selected.Length == 0 ? "CLI default" : selected)) { Enabled = !busy };
            items.Add(menu);
            Action<string> choose = name => { chat.Accounts[provider.Id] = name; changed(); };
            menu.DropDownItems.Add(new ToolStripMenuItem("CLI default", null, delegate { choose(""); }) { Checked = selected.Length == 0 });
            try
            {
                foreach (string item in Names(provider.Id))
                {
                    string name = item;
                    menu.DropDownItems.Add(new ToolStripMenuItem(name, null, delegate { choose(name); }) { Checked = selected == name });
                }
            }
            catch (Exception ex) { menu.DropDownItems.Add(ex.Message).Enabled = false; }
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add("Sign in to a named account…", null, delegate
            {
                string name = Ui.Ask(provider.Name + " sign-in", "Account name (letters, numbers, - or _)", selected, owner);
                if (name == null) return;
                try { name = name.Trim().ToLowerInvariant(); Login(provider, name); choose(name); }
                catch (Exception ex) { MessageBox.Show(owner, ex.Message, provider.Name); }
            });
        }
    }
}
