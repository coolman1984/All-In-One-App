using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Models, effort levels and "/" commands for the Agents page.
    //  - Model and effort are chosen per agent in each chat and passed to the CLI (--model, --effort, ...).
    //  - "/" in the message box lists commands: app commands (handled here), the agent's own commands and skills,
    //    custom command files, and your own saved commands. Favorites come first, in your order.
    // ===================================================================================================

    public class AgentCustomCmd { public string Name = "", Text = "", Agent = ""; }

    class AgentCmd
    {
        public string Name = "", Desc = "", Usage = "", Agent = "", Source = "", Kind = "native", File = "", Text = "";
        public string Key { get { return Agent + "|" + Name; } }
    }

    static class AgentModels
    {
        static readonly Dictionary<string, List<string>> live = new Dictionary<string, List<string>>();
        static readonly HashSet<string> loading = new HashSet<string>();

        // Built-in choices; agents.json can override Models / Efforts / ModelArg / EffortArg / ModelsCmd per agent.
        public static void Fill(AgentDef d)
        {
            if (d == null) return;
            switch ((d.Id ?? "").ToLowerInvariant())
            {
                case "claude":
                    Def(d, "--model {model}", "--effort {effort}", "", new[] { "opus", "sonnet", "haiku", "fable", "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-2" }, new[] { "low", "medium", "high", "xhigh", "max" });
                    break;
                case "codex":
                    {
                        List<string> efforts;
                        var models = CodexModels(out efforts);
                        Def(d, "-m {model}", "-c model_reasoning_effort={effort}", "", models, efforts.Count > 0 ? efforts.ToArray() : new[] { "minimal", "low", "medium", "high", "xhigh" });
                        // The cache changes when Codex updates: always offer the current list.
                        if (models.Length > 0) d.Models = models.ToList();
                    }
                    break;
                case "cline":
                    Def(d, "-P {provider} -m {model}", "--thinking {effort}", "", new string[0], new[] { "none", "low", "medium", "high", "xhigh" });
                    // Cline 3.x: "--id <session>" makes JSON mode reject the message (tested 2026-11-16), so no resume:
                    // the app sends the recent conversation with each message instead.
                    if (d.Resume == "--id {session}") d.Resume = "";
                    break;
                case "agy":
                    Def(d, "--model {model}", "--effort {effort}", "models", new[] { "gemini-4-argon-high", "gemini-4-hydrogen-high", "gemini-3.8-flash-high", "claude-opus-5-5-high", "claude-sonnet-5-5-high" }, new[] { "low", "medium", "high", "xhigh", "max" });
                    // Full auto must also let it run commands (headless mode cannot ask).
                    if (d.ModeAuto == "auto") d.ModeAuto = "auto --dangerously-skip-permissions";
                    // Ask and Edit: headless Antigravity denies every command it cannot ask about, and then gives no answer
                    // at all ("understand the project" needs to list and read files). Allow read-only commands only.
                    if (d.ModeEdit == "accept-edits") d.ModeEdit = "accept-edits --settings " + AgentRun.Quote(AgyReadOnly);
                    if (d.ModeAsk == "plan") d.ModeAsk = "plan --settings " + AgentRun.Quote(AgyReadOnly);
                    break;
                case "opencode":
                    Def(d, "-m {model}", "--variant {effort}", "models", new string[0], new[] { "minimal", "low", "medium", "high", "max" });
                    break;
            }
        }

        static void Def(AgentDef d, string modelArg, string effortArg, string listCmd, string[] models, string[] efforts)
        {
            if (string.IsNullOrEmpty(d.ModelArg)) d.ModelArg = modelArg;
            if (string.IsNullOrEmpty(d.EffortArg)) d.EffortArg = effortArg;
            if (string.IsNullOrEmpty(d.ModelsCmd)) d.ModelsCmd = listCmd;
            if (d.Models == null || d.Models.Count == 0) d.Models = models.ToList();
            if (d.Efforts == null || d.Efforts.Count == 0) d.Efforts = efforts.ToList();
        }

        // Codex: every model in its own model cache (shown ones first) and their effort levels; else the config model.
        static string[] CodexModels(out List<string> efforts)
        {
            efforts = new List<string>();
            var l = new List<string>();
            try
            {
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "models_cache.json");
                if (File.Exists(cache))
                {
                    var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var root = js.DeserializeObject(File.ReadAllText(cache, Encoding.UTF8)) as Dictionary<string, object>;
                    object arr;
                    if (root != null && root.TryGetValue("models", out arr) && arr is object[])
                    {
                        var hidden = new List<string>();
                        foreach (var o in (object[])arr)
                        {
                            var m = o as Dictionary<string, object>; if (m == null) continue;
                            object slug, vis, levels;
                            if (!m.TryGetValue("slug", out slug) || slug == null) continue;
                            bool hide = m.TryGetValue("visibility", out vis) && Convert.ToString(vis) == "hide";
                            (hide ? hidden : l).Add(Convert.ToString(slug));
                            if (m.TryGetValue("supported_reasoning_levels", out levels) && levels is object[])
                                foreach (var lv in (object[])levels)
                                {
                                    var ld = lv as Dictionary<string, object>; object eff;
                                    string e = ld != null && ld.TryGetValue("effort", out eff) ? Convert.ToString(eff) : Convert.ToString(lv);
                                    if (!string.IsNullOrEmpty(e) && !efforts.Contains(e)) efforts.Add(e);
                                }
                        }
                        l.AddRange(hidden);
                    }
                }
            }
            catch (Exception ex) { App.Log("Codex models: " + ex.Message); }
            string[] order = { "none", "minimal", "low", "medium", "high", "xhigh", "max" };
            efforts = efforts.OrderBy(e => { int i = Array.IndexOf(order, e); return i < 0 ? 99 : i; }).ToList();
            if (l.Count > 0) return l.ToArray();
            try
            {
                string cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
                if (File.Exists(cfg))
                    foreach (var line in File.ReadAllLines(cfg))
                    {
                        var m = Regex.Match(line, "^\\s*model\\s*=\\s*\"([^\"]+)\"");
                        if (m.Success) { l.Add(m.Groups[1].Value); break; }
                    }
            }
            catch { }
            foreach (var m in new[] { "gpt-6.1-sol", "gpt-6.1", "gpt-6.1-mini" }) if (!l.Contains(m)) l.Add(m);
            return l.ToArray();
        }

        const string ClineCatalogScript = @"
console.log = console.info = console.warn = console.error = () => {};
const c = require(process.argv[1]);
(async () => {
    const manager = new c.ProviderSettingsManager();
    await c.ensureCustomProvidersLoaded(manager);
    const { providers } = await c.listLocalProviders(manager, { isClinePassEnabled: true });
    // The API's apiKey field contains the actual key. Use readiness metadata only.
    const rank = p => p.oauthAccessTokenPresent || (p.configured && !p.auth?.apiKeyOptional) ? 2 : p.configured ? 1 : 0;
    const rows = providers.filter(p => p.configured || p.id === 'cline-pass').sort((a, b) => rank(b) - rank(a));
    const catalogs = await Promise.all(rows.map(async p => {
        let timer;
        try {
            const config = manager.getProviderConfig(p.id, { includeKnownModels: false });
            const latest = c.getLocalProviderModels(p.id, config, { loadLatest: true });
            const timeout = new Promise(resolve => { timer = setTimeout(resolve, 4500); });
            return (await Promise.race([latest, timeout]))?.models || p.modelList || [];
        } catch {
            return p.modelList || [];
        } finally {
            clearTimeout(timer);
        }
    }));
    const output = [];
    rows.forEach((p, i) => {
        for (const m of catalogs[i])
            if (m.id && (!m.operation || m.operation === 'language'))
                output.push(JSON.stringify([p.id, m.id]));
    });
    process.stdout.write(output.join('\n'), () => process.exit(0));
})().catch(() => { process.exit(1); });";

        class ClineCatalogLoad
        {
            public Task<string[]> Models;
            public DateTime Started;
        }

        static readonly Dictionary<string, ClineCatalogLoad> clineLoads = new Dictionary<string, ClineCatalogLoad>(StringComparer.OrdinalIgnoreCase);

        public static Task<string[]> LoadClineModels(AgentDef d, bool refresh = true)
        {
            lock (clineLoads)
            {
                ClineCatalogLoad load;
                string exe = d.Exe ?? "";
                if (clineLoads.TryGetValue(exe, out load))
                {
                    if (!load.Models.IsCompleted) return load.Models;
                    if (!refresh && load.Models.Status == TaskStatus.RanToCompletion && load.Models.Result.Length > 0 &&
                        DateTime.UtcNow - load.Started < TimeSpan.FromMinutes(1)) return load.Models;
                }
                load = new ClineCatalogLoad { Started = DateTime.UtcNow, Models = Task.Run(() => ClineModels(d)) };
                clineLoads[exe] = load;
                return load.Models;
            }
        }

        internal static string RunClineCore(string exe, string command)
        {
            string node, prefix;
            if (!AgentRun.Resolve(exe, out node, out prefix) || prefix.Length == 0 || !Path.GetFileName(node).Equals("node.exe", StringComparison.OrdinalIgnoreCase))
                return null;
            string script = Path.GetFullPath(prefix.Trim().Trim('"'));
            string module = null;
            for (var dir = Directory.GetParent(script); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, @"node_modules\@cline\core\dist\index.js");
                if (File.Exists(candidate)) { module = candidate; break; }
            }
            if (module == null) return null;
            var psi = new ProcessStartInfo(node, "-e " + AgentRun.Quote(command) + " -- " + AgentRun.Quote(module))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(script)
            };
            using (var proc = Process.Start(psi))
            {
                proc.StandardInput.Close();
                var data = proc.StandardOutput.ReadToEndAsync();
                proc.ErrorDataReceived += delegate { };
                proc.BeginErrorReadLine();
                if (!proc.WaitForExit(20000)) { try { proc.Kill(); } catch (InvalidOperationException) { } return null; }
                return proc.ExitCode == 0 && data.Wait(1000) ? data.Result : null;
            }
        }

        public static string[] ClineModels(AgentDef d)
        {
            var l = new List<string>();
            try
            {
                string output = RunClineCore(d.Exe, ClineCatalogScript);
                if (output == null) return l.ToArray();
                var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!line.StartsWith("[", StringComparison.Ordinal)) continue;
                    var row = js.DeserializeObject(line) as object[];
                    if (row == null || row.Length != 2) continue;
                    string provider = row[0] as string, model = row[1] as string;
                    if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model)) continue;
                    string v = provider + ": " + model;
                    if (seen.Add(v)) l.Add(v);
                }
            }
            catch (Exception ex) { App.Log("Cline models: " + ex.GetType().Name); }
            return l.ToArray();
        }

        static string MigrateClineModel(string value)
        {
            return value == "cline-pass: moonshotai/kimi-k3.1-pro" ? "cline-pass: cline-pass/kimi-k3.1-pro" : value;
        }

        public static string CanonicalClineModel(string value)
        {
            value = MigrateClineModel((value ?? "").Trim());
            int colon = value.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) return null;
            string provider = value.Substring(0, colon).Trim(), model = value.Substring(colon + 2).Trim();
            if (!Regex.IsMatch(provider, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") || model.Length == 0 || model[0] == '-' || model.Any(char.IsControl)) return null;
            switch (provider)
            {
                case "openai": provider = "openai-compatible"; break;
                case "togetherai": provider = "together"; break;
                case "sap-ai-core": provider = "sapaicore"; break;
            }
            return MigrateClineModel(provider + ": " + model);
        }

        // Read-only commands Antigravity may run in Ask / Edit mode (permissions.allow; each rule matches commands that
        // start with it). Anything else is still denied; Full auto allows everything.
        static readonly string[] AgyReadCommands =
        {
            "Get-ChildItem", "Get-Content", "Get-Item", "Get-Location", "Select-String", "Test-Path", "Resolve-Path", "Measure-Object",
            "dir", "ls", "type", "cat", "tree", "where", "findstr", "rg", "fd", "pwd",
            "git status", "git log", "git diff", "git show", "git branch", "git remote", "git ls-files", "git rev-parse",
            "python --version", "node --version", "npm --version", "npm ls", "pip list", "pip show", "dotnet --version"
        };
        public static string AgyReadOnly
        {
            get { return "{\"permissions\":{\"allow\":[" + string.Join(",", AgyReadCommands.Select(c => "\"command(" + c + ")\"")) + "]}}"; }
        }

        public static bool HasModels(AgentDef d) { return d != null && !string.IsNullOrEmpty(d.ModelArg); }

        // Your own model names for this agent (for example Cline Pass models), kept in Settings and shown first.
        public static List<string> Mine(AgentDef d)
        {
            List<string> l;
            if (!Store.Settings.AgentMyModels.TryGetValue(d.Id, out l) || l == null) { l = new List<string>(); Store.Settings.AgentMyModels[d.Id] = l; }
            return l;
        }
        public static void Remember(AgentDef d, string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return;
            var l = Mine(d); model = model.Trim();
            if (!l.Contains(model) && !(d.Models ?? new List<string>()).Contains(model)) { l.Insert(0, model); Store.MarkSettings(); }
        }
        public static List<string> Choices(AgentDef d) { return Mine(d).Concat(List(d, null)).Distinct().Where(m => !IsDead(d, m)).ToList(); }
        public static bool IsDead(AgentDef d, string model) { return Store.Settings.AgentDeadModels.Contains(d.Id + "|" + model); }

        // The CLI said the model does not exist any more (for example a retired test model): hide it, and find the
        // replacement the error names ("Use it now: https://openrouter.ai/<id>"). Returns the replacement or "".
        public static string Retire(AgentDef d, string model, string errorText)
        {
            if (string.IsNullOrEmpty(model)) return "";
            string key = d.Id + "|" + model;
            if (!Store.Settings.AgentDeadModels.Contains(key)) Store.Settings.AgentDeadModels.Add(key);
            Mine(d).Remove(model);
            string repl = "";
            var r = Regex.Match(errorText ?? "", "openrouter\\.ai/([\\w.\\-]+/[\\w.\\-:]+)");
            if (r.Success)
            {
                int colon = model.IndexOf(": ");
                repl = (colon > 0 ? model.Substring(0, colon + 2) : "") + r.Groups[1].Value.TrimEnd('.', ',');
                if (!Mine(d).Contains(repl)) Mine(d).Insert(0, repl);
            }
            Store.MarkSettings();
            return repl;
        }

        // "failed to invoke model 'x' ... 404", "model not found", "does not exist": the chosen model is gone.
        public static bool IsModelGone(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return Regex.IsMatch(text, "(invoke model|model)[^\\n]{0,80}(status 404|\"code\":404|not found|does not exist|no longer available|testing period)", RegexOptions.IgnoreCase);
        }
        public static bool HasEffort(AgentDef d) { return d != null && !string.IsNullOrEmpty(d.EffortArg) && d.Efforts != null && d.Efforts.Count > 0; }

        // The models to offer: the live list from the CLI when it has one (loaded in the background), else the built-in list.
        public static List<string> List(AgentDef d, Action loaded)
        {
            List<string> l;
            lock (live) if (live.TryGetValue(d.Id, out l)) return l;
            if (!string.IsNullOrEmpty(d.ModelsCmd)) Load(d, loaded);
            return d.Models ?? new List<string>();
        }
        public static bool Loading(AgentDef d) { lock (live) return loading.Contains(d.Id); }
        public static void Forget(AgentDef d) { lock (live) live.Remove(d.Id); }

        static void Load(AgentDef d, Action loaded)
        {
            lock (live) { if (loading.Contains(d.Id)) return; loading.Add(d.Id); }
            ThreadPool.QueueUserWorkItem(delegate
            {
                var result = new List<string>();
                try
                {
                    string file, prefix;
                    if (AgentRun.Resolve(d.Exe, out file, out prefix))
                    {
                        bool viaCmd = file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && prefix.StartsWith("/d");
                        var psi = new ProcessStartInfo(file, prefix + d.ModelsCmd + (viaCmd ? "\"" : "")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8 };
                        using (var p = Process.Start(psi))
                        {
                            p.StandardInput.Close();
                            var text = p.StandardOutput.ReadToEndAsync();
                            if (!p.WaitForExit(40000)) { try { p.Kill(); } catch { } }
                            else
                                foreach (var raw in text.Result.Split('\n'))
                                {
                                    string tok = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                                    if (tok.Length > 1 && Regex.IsMatch(tok, "^[A-Za-z0-9][A-Za-z0-9._/:@-]*$") && !tok.EndsWith(":") && !tok.StartsWith("Fetching", StringComparison.OrdinalIgnoreCase) && !result.Contains(tok)) result.Add(tok);
                                }
                        }
                    }
                }
                catch (Exception ex) { App.Log("Model list " + d.Id + ": " + ex.Message); }
                lock (live) { loading.Remove(d.Id); if (result.Count > 0) live[d.Id] = result; }
                if (loaded != null) try { App.I.Sync.BeginInvoke(loaded); } catch { }
            });
        }

        // Extra CLI arguments for the chosen model and effort ("" when the CLI default is used).
        public static string Args(AgentDef d, AgentChat chat)
        {
            string e = "";
            if (chat.Efforts != null) chat.Efforts.TryGetValue(d.Id, out e);
            return ArgsFor(d, ChatModel(chat, d.Id), e);
        }

        public static string ChatModel(AgentChat chat, string id)
        {
            string v;
            if (chat.Models == null || !chat.Models.TryGetValue(id, out v) || v == null) return "";
            if (id != "cline") return v;
            string migrated = MigrateClineModel(v);
            if (migrated != v) chat.Models[id] = migrated;
            return migrated;
        }

        public static string DefaultModel(AgentDef d)
        {
            var defaults = Store.Settings.AgentDefaultModel;
            string v;
            if (!defaults.TryGetValue(d.Id, out v) || v == null) return "";
            if (d.Id != "cline") return v;
            string migrated = MigrateClineModel(v);
            if (migrated != v) { defaults[d.Id] = migrated; Store.MarkSettings(); }
            return migrated;
        }
        public static string DefaultEffort(AgentDef d) { string v; return Store.Settings.AgentDefaultEffort.TryGetValue(d.Id, out v) ? v ?? "" : ""; }

        public static string ArgsFor(AgentDef d, string m, string e)
        {
            if (string.IsNullOrWhiteSpace(m)) m = DefaultModel(d);     // the app default, when the chat did not pick one
            if (string.IsNullOrWhiteSpace(e)) e = DefaultEffort(d);
            var sb = new StringBuilder();
            bool modelHasEffort = false;
            if (d.Id == "cline")
            {
                string selection = CanonicalClineModel(m);
                if (selection != null)
                {
                    int colon = selection.IndexOf(": ", StringComparison.Ordinal);
                    sb.Append("-P ").Append(AgentRun.Quote(selection.Substring(0, colon)))
                        .Append(" -m ").Append(AgentRun.Quote(selection.Substring(colon + 2))).Append(' ');
                }
            }
            else if (!string.IsNullOrWhiteSpace(m) && HasModels(d))
            {
                m = m.Trim();
                string arg = d.ModelArg;
                // Custom agents can also use a provider placeholder.
                int colon = m.IndexOf(": ");
                if (arg.Contains("{provider}"))
                {
                    if (colon > 0) { arg = arg.Replace("{provider}", AgentRun.Quote(m.Substring(0, colon).Trim())); m = m.Substring(colon + 2).Trim(); }
                    else arg = Regex.Replace(arg, "\\S*\\s*\\{provider\\}\\s*", "");
                }
                sb.Append(arg.Replace("{model}", AgentRun.Quote(m))).Append(' ');
                // Models named "...-high" already carry their effort (Antigravity): an effort flag would conflict.
                int dash = m.LastIndexOf('-');
                modelHasEffort = d.Id == "agy" && dash > 0 && d.Efforts != null && d.Efforts.Contains(m.Substring(dash + 1).ToLowerInvariant());
            }
            if (!modelHasEffort && !string.IsNullOrWhiteSpace(e) && HasEffort(d)) sb.Append(d.EffortArg.Replace("{effort}", e.Trim())).Append(' ');
            return sb.ToString().Trim();
        }

        // Puts the extra arguments after a subcommand ("exec", "run") or at the start.
        public static string Inject(string args, string extra)
        {
            if (string.IsNullOrEmpty(extra)) return args;
            args = args.TrimStart();
            var m = Regex.Match(args, "^([A-Za-z][\\w-]*)(\\s|$)");
            if (m.Success) return m.Groups[1].Value + " " + extra + " " + args.Substring(m.Length);
            return extra + " " + args;
        }

        public static string Label(AgentChat chat, string id)
        {
            string m = ChatModel(chat, id), e = "", s = "";
            if (chat.Efforts != null) chat.Efforts.TryGetValue(id, out e);
            var d = AgentStore.Def(id);
            if (string.IsNullOrWhiteSpace(m) && d != null) m = DefaultModel(d);
            if (string.IsNullOrWhiteSpace(e) && d != null) e = DefaultEffort(d);
            if (!string.IsNullOrWhiteSpace(m)) s += " · " + m;
            if (!string.IsNullOrWhiteSpace(e)) s += " · " + e;
            return s;
        }
    }

    static class AgentCommands
    {
        static readonly AgentCmd[] AppCmds =
        {
            new AgentCmd { Name = "/help", Desc = "Open the command help: every command, what it does, favorites", Kind = "app" },
            new AgentCmd { Name = "/model", Usage = "/model [name]", Desc = "Choose the model (no name: pick from the list)", Kind = "app" },
            new AgentCmd { Name = "/effort", Usage = "/effort [low|medium|high|xhigh|max]", Desc = "Choose how hard the agent thinks", Kind = "app" },
            new AgentCmd { Name = "/mode", Usage = "/mode ask|edit|auto", Desc = "Ask = read only, Edit = may change files, Auto = no permission checks", Kind = "app" },
            new AgentCmd { Name = "/agents", Desc = "Add or remove agents in this chat", Kind = "app" },
            new AgentCmd { Name = "/folder", Desc = "Change the project folder of this chat", Kind = "app" },
            new AgentCmd { Name = "/new", Desc = "New chat in this pane", Kind = "app" },
            new AgentCmd { Name = "/fresh", Desc = "Agents start new sessions (forget earlier turns)", Kind = "app" },
            new AgentCmd { Name = "/rename", Usage = "/rename new name", Desc = "Rename this chat", Kind = "app" },
            new AgentCmd { Name = "/fav", Desc = "Add or remove this chat from favorites", Kind = "app" },
            new AgentCmd { Name = "/copy", Desc = "Copy the chat as Markdown", Kind = "app" },
            new AgentCmd { Name = "/export", Desc = "Save the chat to a file", Kind = "app" },
            new AgentCmd { Name = "/stop", Desc = "Stop the agent that is answering", Kind = "app" },
        };

        // The agents' own commands that work when the CLI runs one message at a time.
        static readonly string[][] Native =
        {
            new[] { "claude", "/init", "Create or update CLAUDE.md for this project" },
            new[] { "claude", "/review", "Review the current changes or a pull request" },
            new[] { "claude", "/security-review", "Security review of the pending changes" },
            new[] { "claude", "/compact", "Shrink the conversation to save context" },
            new[] { "agy", "/init", "Create project instructions for this folder" },
        };

        public static List<AgentCmd> Catalog(AgentChat chat)
        {
            var l = new List<AgentCmd>();
            foreach (var c in AppCmds) { var x = Copy(c); x.Source = "App"; if (x.Usage.Length == 0) x.Usage = x.Name; l.Add(x); }
            var agents = chat != null && chat.Agents.Count > 0 ? chat.Agents : AgentStore.Defs.Select(d => d.Id).ToList();
            string folder = chat != null ? chat.Folder : "";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var a in agents)
            {
                string name = AgentStore.NameOf(a);
                foreach (var n in Native.Where(n => n[0] == a)) l.Add(new AgentCmd { Name = n[1], Desc = n[2], Agent = a, Source = name, Kind = "native", Usage = n[1] });
                if (a == "claude")
                {
                    AddFiles(l, Path.Combine(folder ?? "", ".claude", "commands"), a, name + " · project command", "native");
                    AddFiles(l, Path.Combine(home, ".claude", "commands"), a, name + " · your command", "native");
                    AddSkills(l, Path.Combine(folder ?? "", ".claude", "skills"), a, name + " · project skill");
                    AddSkills(l, Path.Combine(home, ".claude", "skills"), a, name + " · skill");
                }
                else if (a == "codex")
                    AddFiles(l, Path.Combine(home, ".codex", "prompts"), a, name + " · prompt file", "expand");
            }
            foreach (var c in Store.Settings.AgentCustomCmds)
                if (c != null && c.Name.Length > 0 && (c.Agent.Length == 0 || agents.Contains(c.Agent)))
                    l.Add(new AgentCmd { Name = "/" + c.Name.TrimStart('/'), Desc = Ui.OneLine(c.Text, 90), Text = c.Text, Agent = c.Agent, Source = "Your command" + (c.Agent.Length > 0 ? " · " + AgentStore.NameOf(c.Agent) : ""), Kind = "text", Usage = "/" + c.Name.TrimStart('/') + " [more text]" });
            // Favorites first, in your order; then the rest.
            var favs = Store.Settings.AgentCmdFavs;
            return l.OrderBy(c => { int i = favs.IndexOf(c.Key); return i < 0 ? int.MaxValue : i; }).ToList();
        }

        static AgentCmd Copy(AgentCmd c) { return new AgentCmd { Name = c.Name, Desc = c.Desc, Usage = c.Usage, Agent = c.Agent, Source = c.Source, Kind = c.Kind, File = c.File, Text = c.Text }; }

        static void AddFiles(List<AgentCmd> l, string dir, string agent, string source, string kind)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*.md", SearchOption.AllDirectories).Take(200))
                {
                    string rel = f.Substring(dir.Length).TrimStart('\\').Replace('\\', ':');
                    string name = "/" + (kind == "expand" ? "prompts:" : "") + rel.Substring(0, rel.Length - 3);
                    if (l.Any(x => x.Agent == agent && x.Name == name)) continue;
                    l.Add(new AgentCmd { Name = name, Desc = Front(f, "description"), Agent = agent, Source = source, Kind = kind, File = f, Usage = name + " [arguments]" });
                }
            }
            catch { }
        }

        static void AddSkills(List<AgentCmd> l, string dir, string agent, string source)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var d in Directory.GetDirectories(dir).Take(200))
                {
                    string f = Path.Combine(d, "SKILL.md");
                    if (!File.Exists(f)) continue;
                    string n = Front(f, "name"); if (n.Length == 0) n = Path.GetFileName(d);
                    string name = "/" + n;
                    if (l.Any(x => x.Agent == agent && x.Name == name)) continue;
                    l.Add(new AgentCmd { Name = name, Desc = Front(f, "description"), Agent = agent, Source = source, Kind = "native", File = f, Usage = name + " [what you want]" });
                }
            }
            catch { }
        }

        // One field from the --- front matter --- of a command or skill file.
        static string Front(string file, string key)
        {
            try
            {
                using (var r = new StreamReader(file))
                {
                    if (r.ReadLine() != "---") return "";
                    for (int i = 0; i < 40; i++)
                    {
                        string line = r.ReadLine(); if (line == null || line == "---") break;
                        if (line.StartsWith(key + ":")) return Ui.OneLine(line.Substring(key.Length + 1).Trim().Trim('"', '\''), 160);
                    }
                }
            }
            catch { }
            return "";
        }

        // A command file's text without its front matter, with $ARGUMENTS filled in.
        public static string Expand(AgentCmd c, string args)
        {
            string t = c.Kind == "text" ? c.Text : "";
            if (c.Kind == "expand") try { t = File.ReadAllText(c.File, Encoding.UTF8); } catch { }
            if (t.StartsWith("---")) { int end = t.IndexOf("\n---", 3); if (end > 0) t = t.Substring(t.IndexOf('\n', end + 1) + 1); }
            if (t.Contains("$ARGUMENTS")) t = t.Replace("$ARGUMENTS", args);
            else if (args.Length > 0) t = t.TrimEnd() + "\n\n" + args;
            return t.Trim();
        }

        public static bool IsFav(AgentCmd c) { return Store.Settings.AgentCmdFavs.Contains(c.Key); }
        public static void ToggleFav(AgentCmd c)
        {
            var f = Store.Settings.AgentCmdFavs;
            if (!f.Remove(c.Key)) f.Add(c.Key);
            Store.MarkSettings();
        }
        public static void MoveFav(AgentCmd c, int d)
        {
            var f = Store.Settings.AgentCmdFavs; int i = f.IndexOf(c.Key), j = i + d;
            if (i < 0 || j < 0 || j >= f.Count) return;
            f.RemoveAt(i); f.Insert(j, c.Key); Store.MarkSettings();
        }
    }

    // ---------- the list that opens above the message box while you type "/" ----------
    class CommandList : ListBox
    {
        public List<AgentCmd> Items2 = new List<AgentCmd>();
        public CommandList()
        {
            DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = Ui.S(30); BorderStyle = BorderStyle.FixedSingle; IntegralHeight = false;
            Font = Ui.Base; BackColor = Ui.Card; ForeColor = Ui.Text; Visible = false; TabStop = false;
        }
        public void Fill(List<AgentCmd> cmds)
        {
            Items2 = cmds;
            BeginUpdate(); Items.Clear(); foreach (var c in cmds) Items.Add(c.Name); EndUpdate();
            if (Items.Count > 0) SelectedIndex = 0;
        }
        public AgentCmd Current { get { return SelectedIndex >= 0 && SelectedIndex < Items2.Count ? Items2[SelectedIndex] : null; } }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items2.Count) return;
            var c = Items2[e.Index]; var g = e.Graphics; var r = e.Bounds;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Ui.HoverBg : Ui.Card)) g.FillRectangle(b, r);
            if (sel) using (var b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, r.X, r.Y, Ui.S(3), r.Height);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            string star = AgentCommands.IsFav(c) ? "★ " : "";
            int nameW = Math.Min(r.Width / 2, TextRenderer.MeasureText(star + c.Name, Ui.Bold).Width + Ui.S(14));
            TextRenderer.DrawText(g, star + c.Name, Ui.Bold, new Rectangle(r.X + Ui.S(10), r.Y, nameW, r.Height), c.Kind == "app" ? Ui.Accent : Ui.Text, flags);
            int srcW = Math.Min(Ui.S(170), TextRenderer.MeasureText(c.Source, Ui.Small).Width + Ui.S(8));
            TextRenderer.DrawText(g, c.Desc, Ui.Small, new Rectangle(r.X + Ui.S(10) + nameW, r.Y, r.Width - nameW - srcW - Ui.S(20), r.Height), Ui.Muted, flags);
            TextRenderer.DrawText(g, c.Source, Ui.Small, new Rectangle(r.Right - srcW - Ui.S(6), r.Y, srcW, r.Height), Ui.Muted, flags | TextFormatFlags.Right);
        }
    }

    // ---------- command help: every command, what it does, favorites and their order, your own commands ----------
    class CommandBrowser : Form
    {
        readonly AgentChat chat;
        readonly TextBox search;
        readonly CommandList list;
        readonly Label title, info;
        readonly Button favBtn, upBtn, downBtn, editBtn, delBtn;
        public string Picked;

        public CommandBrowser(AgentChat chat)
        {
            this.chat = chat;
            Text = "Commands"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            ClientSize = new Size(Ui.S(820), Ui.S(560)); Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text; KeyPreview = true;
            search = new TextBox { Dock = DockStyle.Top, Font = Ui.Base };
            list = new CommandList { Dock = DockStyle.Fill, Visible = true };
            var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(12), Ui.S(12), Ui.S(6), Ui.S(12)) };
            left.Controls.Add(list); left.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(8) }); left.Controls.Add(search);
            var right = new Panel { Dock = DockStyle.Right, Width = Ui.S(270), Padding = new Padding(Ui.S(8), Ui.S(12), Ui.S(12), Ui.S(12)) };
            title = new Label { Dock = DockStyle.Top, Height = Ui.S(30), Font = Ui.Bold, UseMnemonic = false };
            info = new Label { Dock = DockStyle.Fill, ForeColor = Ui.Text, UseMnemonic = false };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(150), FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            buttons.Controls.Add(Ui.Btn("Insert", delegate { Insert(); }, true));
            favBtn = Ui.Btn("Add to favorites", delegate { var c = list.Current; if (c != null) { AgentCommands.ToggleFav(c); Refill(c.Key); } });
            upBtn = Ui.Btn("Move up", delegate { var c = list.Current; if (c != null) { AgentCommands.MoveFav(c, -1); Refill(c.Key); } });
            downBtn = Ui.Btn("Move down", delegate { var c = list.Current; if (c != null) { AgentCommands.MoveFav(c, 1); Refill(c.Key); } });
            buttons.Controls.Add(favBtn); buttons.Controls.Add(upBtn); buttons.Controls.Add(downBtn);
            buttons.Controls.Add(Ui.Btn("New command...", delegate { EditCustom(null); }));
            editBtn = Ui.Btn("Edit", delegate { EditCustom(list.Current); });
            delBtn = Ui.Btn("Delete", delegate { DeleteCustom(list.Current); });
            buttons.Controls.Add(editBtn); buttons.Controls.Add(delBtn);
            right.Controls.Add(info); right.Controls.Add(title); right.Controls.Add(buttons);
            Controls.Add(left); Controls.Add(right);
            search.TextChanged += delegate { Refill(null); };
            list.SelectedIndexChanged += delegate { ShowInfo(); };
            list.DoubleClick += delegate { Insert(); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Insert(); }
                else if ((e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) && search.Focused && list.Items.Count > 0)
                { e.Handled = true; list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1))); }
            };
            Ui.Theme(this);
            Refill(null);
        }

        void Refill(string keepKey)
        {
            string q = search.Text.Trim().ToLowerInvariant();
            var all = AgentCommands.Catalog(chat).Where(c => q.Length == 0 || (c.Name + " " + c.Desc + " " + c.Source).ToLowerInvariant().Contains(q)).ToList();
            list.Fill(all);
            if (keepKey != null) { int i = all.FindIndex(c => c.Key == keepKey); if (i >= 0) list.SelectedIndex = i; }
            ShowInfo();
        }

        void ShowInfo()
        {
            var c = list.Current;
            if (c == null) { title.Text = "No command"; info.Text = "Nothing matches the search."; return; }
            bool fav = AgentCommands.IsFav(c);
            title.Text = (fav ? "★ " : "") + c.Name;
            string how = c.Kind == "app" ? "Done by the app right away (nothing is sent to the agents)."
                : c.Kind == "native" ? "Sent to " + (c.Agent.Length > 0 ? AgentStore.NameOf(c.Agent) : "the agents") + " exactly as typed; that agent runs it."
                : c.Kind == "expand" ? "The app puts the file's text in your message (with what you type after it) and sends it to " + AgentStore.NameOf(c.Agent) + "."
                : "Your saved text is sent" + (c.Agent.Length > 0 ? " to " + AgentStore.NameOf(c.Agent) : " to every agent in the chat") + ", with what you type after it.";
            info.Text = (c.Desc.Length > 0 ? c.Desc + "\n\n" : "") + "Use:  " + c.Usage + "\n\nFrom:  " + c.Source + "\n\n" + how + (c.File.Length > 0 ? "\n\nFile:  " + c.File : "")
                + "\n\nTip: type / in a chat to see this list; Tab or Enter picks a command.";
            favBtn.Text = fav ? "Remove from favorites" : "Add to favorites";
            upBtn.Enabled = downBtn.Enabled = fav;
            editBtn.Enabled = delBtn.Enabled = c.Kind == "text";
        }

        void Insert() { var c = list.Current; if (c == null) return; Picked = c.Name + " "; DialogResult = DialogResult.OK; Close(); }

        void EditCustom(AgentCmd c)
        {
            var existing = c != null && c.Kind == "text" ? Store.Settings.AgentCustomCmds.FirstOrDefault(x => "/" + x.Name.TrimStart('/') == c.Name && x.Agent == c.Agent) : null;
            string name = Ui.Ask("Command name (what you type after /)", "Name", existing != null ? existing.Name : "", this);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = Regex.Replace(name.Trim().TrimStart('/'), "\\s+", "-");
            string text = Ui.Ask("Text the command sends (use $ARGUMENTS for what you type after it)", "Text", existing != null ? existing.Text : "", this);
            if (string.IsNullOrWhiteSpace(text)) return;
            if (existing == null) { existing = new AgentCustomCmd(); Store.Settings.AgentCustomCmds.Add(existing); }
            existing.Name = name; existing.Text = text;
            Store.MarkSettings();
            Refill("|/" + name);
        }

        void DeleteCustom(AgentCmd c)
        {
            if (c == null || c.Kind != "text") return;
            if (MessageBox.Show(this, "Delete the command " + c.Name + "?", "Commands", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            Store.Settings.AgentCustomCmds.RemoveAll(x => "/" + x.Name.TrimStart('/') == c.Name && x.Agent == c.Agent);
            Store.Settings.AgentCmdFavs.Remove(c.Key);
            Store.MarkSettings();
            Refill(null);
        }
    }
}
