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
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Agents: chat with coding-agent CLIs (Claude Code, Codex, Cline, Antigravity, OpenCode, or any CLI you add)
    // in 1-6 panes. Every pane is a chat with one or more agents working in one folder; several agents in one chat
    // answer in turn and each sees what the others said. Chats, sessions and layout live in <data folder>\agents.
    // ===================================================================================================

    public class AgentDef
    {
        public string Id = "", Name = "", Exe = "", Args = "", Resume = "", Format = "text", Color = "#3366F0";
        public bool Stdin;
        public string ModeAsk = "", ModeEdit = "", ModeAuto = "";
    }

    public class AgentMsg { public string Role = "", Agent = "", Text = ""; public long Time; }

    public class AgentChat
    {
        public string Id = "", Title = "", Folder = "", Mode = "edit";
        public List<string> Agents = new List<string>();
        public Dictionary<string, string> Sessions = new Dictionary<string, string>();
        public Dictionary<string, int> Seen = new Dictionary<string, int>();
        public long Created, Updated;
        public bool Fav;
        public List<AgentMsg> Messages = new List<AgentMsg>();
    }

    public class AgentLayout { public string Layout = "2"; public List<string> Panes = new List<string>(); public bool HistoryOff; }

    static class AgentStore
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        public static string Dir { get { return Path.Combine(Store.Dir, "agents"); } }
        static string ChatsDir { get { return Path.Combine(Dir, "chats"); } }
        public static string DefsFile { get { return Path.Combine(Dir, "agents.json"); } }
        static readonly object writeLock = new object();
        public static List<AgentChat> Chats = new List<AgentChat>();
        public static bool Loaded;
        public static event Action Changed;
        static List<AgentDef> defs;

        public static List<AgentDef> Defaults()
        {
            return new List<AgentDef>
            {
                new AgentDef { Id = "claude", Name = "Claude Code", Exe = "claude", Format = "claude", Color = "#D97757", Stdin = true,
                    Args = "-p --output-format stream-json --verbose --permission-mode {mode} {resume}", Resume = "--resume {session}",
                    ModeAsk = "plan", ModeEdit = "acceptEdits", ModeAuto = "bypassPermissions" },
                new AgentDef { Id = "codex", Name = "Codex", Exe = "codex", Format = "codex", Color = "#10A37F", Stdin = true,
                    Args = "exec --json --skip-git-repo-check {mode} {resume} -", Resume = "resume {session}",
                    ModeAsk = "-s read-only", ModeEdit = "-s workspace-write", ModeAuto = "--dangerously-bypass-approvals-and-sandbox" },
                new AgentDef { Id = "cline", Name = "Cline", Exe = "cline", Format = "cline", Color = "#6366F1",
                    Args = "--json {mode} {resume} {prompt}", Resume = "--id {session}", ModeAsk = "-p", ModeEdit = "", ModeAuto = "-y" },
                new AgentDef { Id = "agy", Name = "Antigravity", Exe = "agy", Format = "agy", Color = "#4285F4",
                    Args = "--output-format stream-json --mode {mode} {resume} -p {prompt}", Resume = "--conversation {session}",
                    ModeAsk = "plan", ModeEdit = "accept-edits", ModeAuto = "auto" },
                new AgentDef { Id = "opencode", Name = "OpenCode", Exe = "opencode", Format = "opencode", Color = "#F59E0B",
                    Args = "run --format json {resume} {prompt}", Resume = "--session {session}" },
            };
        }

        // Agent list: data\agents\agents.json (written with the defaults the first time, so you can add any CLI there).
        public static List<AgentDef> Defs
        {
            get
            {
                if (defs != null) return defs;
                try
                {
                    if (File.Exists(DefsFile)) defs = Js.Deserialize<List<AgentDef>>(File.ReadAllText(DefsFile, Encoding.UTF8));
                }
                catch (Exception ex) { App.Log("Agents file: " + ex.Message); }
                if (defs == null || defs.Count == 0)
                {
                    defs = Defaults();
                    try { Directory.CreateDirectory(Dir); File.WriteAllText(DefsFile, Pretty(Js.Serialize(defs)), new UTF8Encoding(false)); } catch { }
                }
                return defs;
            }
        }
        public static void ReloadDefs() { defs = null; }
        public static AgentDef Def(string id) { return Defs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)); }
        public static string NameOf(string id) { var d = Def(id); return d != null ? d.Name : id; }
        public static Color ColorOf(string id)
        {
            var d = Def(id);
            try { if (d != null && d.Color.Length > 0) return ColorTranslator.FromHtml(d.Color); } catch { }
            return Ui.Accent;
        }

        static string Pretty(string json)
        {
            var sb = new StringBuilder(); int indent = 0; bool str = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"' && (i == 0 || json[i - 1] != '\\')) str = !str;
                if (str) { sb.Append(c); continue; }
                if (c == '{' || c == '[') { sb.Append(c).Append('\n').Append(' ', ++indent * 2); }
                else if (c == '}' || c == ']') { sb.Append('\n').Append(' ', --indent * 2).Append(c); }
                else if (c == ',') { sb.Append(c).Append('\n').Append(' ', indent * 2); }
                else if (c == ':') sb.Append(": ");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static void LoadAll(Action done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var list = new List<AgentChat>();
                try
                {
                    if (Directory.Exists(ChatsDir))
                        foreach (var f in Directory.GetFiles(ChatsDir, "*.json"))
                        {
                            try { var c = Js.Deserialize<AgentChat>(File.ReadAllText(f, Encoding.UTF8)); if (c != null && c.Id.Length > 0) { Normalize(c); list.Add(c); } }
                            catch (Exception ex) { App.Log("Agent chat " + Path.GetFileName(f) + ": " + ex.Message); }
                        }
                }
                catch (Exception ex) { App.Log("Agent chats: " + ex.Message); }
                try
                {
                    App.I.Sync.BeginInvoke(new Action(() =>
                    {
                        // Keep chats created before loading finished.
                        foreach (var c in Chats) if (!list.Any(x => x.Id == c.Id)) list.Add(c);
                        Chats = list; Loaded = true;
                        if (done != null) done();
                        Raise();
                    }));
                }
                catch { }
            });
        }
        static void Normalize(AgentChat c)
        {
            if (c.Agents == null) c.Agents = new List<string>();
            if (c.Sessions == null) c.Sessions = new Dictionary<string, string>();
            if (c.Seen == null) c.Seen = new Dictionary<string, int>();
            if (c.Messages == null) c.Messages = new List<AgentMsg>();
            if (c.Title == null) c.Title = ""; if (c.Folder == null) c.Folder = ""; if (string.IsNullOrEmpty(c.Mode)) c.Mode = "edit";
        }
        public static void Raise() { var h = Changed; if (h != null) h(); }

        public static AgentChat New(string folder, IEnumerable<string> agents)
        {
            var c = new AgentChat { Id = DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0, 6), Created = Store.Now(), Updated = Store.Now(), Folder = folder ?? "" };
            c.Agents.AddRange(agents);
            Chats.Add(c);
            return c;
        }
        public static AgentChat Find(string id) { return Chats.FirstOrDefault(c => c.Id == id); }

        // Snapshot on the UI thread, write on a worker (temp file + replace, so a crash never leaves half a file).
        public static void Save(AgentChat c)
        {
            if (c == null || c.Messages.Count == 0) return;
            string json; try { json = Js.Serialize(c); } catch (Exception ex) { App.Log("Agent save: " + ex.Message); return; }
            string file = Path.Combine(ChatsDir, c.Id + ".json");
            ThreadPool.QueueUserWorkItem(delegate { WriteFile(file, json); });
            Raise();
        }
        static void WriteFile(string file, string text)
        {
            lock (writeLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    string tmp = file + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    if (File.Exists(file)) File.Replace(tmp, file, null); else File.Move(tmp, file);
                }
                catch (Exception ex) { App.Log("Agent write " + Path.GetFileName(file) + ": " + ex.Message); }
            }
        }
        public static void Delete(AgentChat c)
        {
            Chats.Remove(c);
            try { File.Delete(Path.Combine(ChatsDir, c.Id + ".json")); } catch { }
            Raise();
        }

        public static AgentLayout LoadLayout()
        {
            try { string f = Path.Combine(Dir, "layout.json"); if (File.Exists(f)) { var l = Js.Deserialize<AgentLayout>(File.ReadAllText(f, Encoding.UTF8)); if (l != null) { if (l.Panes == null) l.Panes = new List<string>(); return l; } } } catch { }
            return new AgentLayout();
        }
        public static void SaveLayout(AgentLayout l)
        {
            string json = Js.Serialize(l), file = Path.Combine(Dir, "layout.json");
            ThreadPool.QueueUserWorkItem(delegate { WriteFile(file, json); });
        }

        public static string Markdown(AgentChat c)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(c.Title.Length > 0 ? c.Title : "Chat").Append("\n\n");
            sb.Append("Folder: ").Append(c.Folder).Append("  \nAgents: ").Append(string.Join(", ", c.Agents.Select(NameOf))).Append("\n\n");
            foreach (var m in c.Messages)
            {
                string who = m.Role == "user" ? Ui.UserName : m.Role == "agent" ? NameOf(m.Agent) : m.Role == "tool" ? NameOf(m.Agent) + " (tool)" : m.Role;
                sb.Append("**").Append(who).Append("** - ").Append(new DateTime(m.Time).ToString("yyyy-MM-dd HH:mm")).Append("\n\n");
                sb.Append(m.Role == "tool" ? "`" + m.Text + "`" : m.Text).Append("\n\n");
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Runs one agent turn as a hidden process and turns its JSON lines into chat events.
    // ---------------------------------------------------------------------------------------------------
    class AgentRun
    {
        public event Action<string, string> Event;   // kind (text, tool, error, session, info), payload
        public event Action<int> Exited;
        Process proc;
        volatile bool stopped;
        readonly StringBuilder stderr = new StringBuilder();
        readonly StringBuilder plain = new StringBuilder();
        bool sawText, sawError;
        readonly Dictionary<string, StringBuilder> steps = new Dictionary<string, StringBuilder>();
        string sessionSeen;
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        // Finds the program behind a command name: .exe on PATH, or an npm shim (.cmd/.ps1) resolved to node + script.
        public static bool Resolve(string exe, out string file, out string prefix)
        {
            file = null; prefix = "";
            if (string.IsNullOrWhiteSpace(exe)) return false;
            exe = Environment.ExpandEnvironmentVariables(exe.Trim().Trim('"'));
            if (Path.IsPathRooted(exe) && File.Exists(exe)) { file = exe; return FixShim(ref file, ref prefix); }
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => d.Trim().Length > 0).ToList();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add(Path.Combine(home, ".local\\bin")); dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), exe + "\\bin"));
            foreach (var ext in new[] { ".exe", ".cmd", ".bat", "" })
                foreach (var d in dirs)
                {
                    try { string p = Path.Combine(d.Trim().Trim('"'), exe + ext); if (File.Exists(p) && (ext.Length > 0 || Path.HasExtension(p))) { file = p; return FixShim(ref file, ref prefix); } }
                    catch { }
                }
            return false;
        }
        static bool FixShim(ref string file, ref string prefix)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".exe") return true;
            if (ext == ".cmd" || ext == ".bat")
            {
                try
                {
                    var m = Regex.Match(File.ReadAllText(file), "\"%dp0%\\\\([^\"]+)\"\\s+%\\*");
                    if (m.Success)
                    {
                        string script = Path.Combine(Path.GetDirectoryName(file), m.Groups[1].Value);
                        string node = Path.Combine(Path.GetDirectoryName(file), "node.exe");
                        string n2; string pre;
                        if (!File.Exists(node) && Resolve("node", out n2, out pre)) node = n2;
                        if (File.Exists(script) && File.Exists(node)) { prefix = Quote(script) + " "; file = node; return true; }
                    }
                }
                catch { }
                prefix = "/d /s /c \"\"" + file + "\" "; file = Path.Combine(Environment.SystemDirectory, "cmd.exe");   // fallback (closing quote added in Start)
                return true;
            }
            return true;
        }

        // Windows command-line quoting (backslashes before quotes doubled).
        public static string Quote(string s)
        {
            if (s == null) s = "";
            if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"', '\n', '\r' }) < 0) return s;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in s)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1).Append('"'); bs = 0; continue; }
                sb.Append('\\', bs); bs = 0; sb.Append(c);
            }
            sb.Append('\\', bs * 2).Append('"');
            return sb.ToString();
        }

        public bool Start(AgentDef d, AgentChat chat, string prompt, out string error)
        {
            error = null;
            string file, prefix;
            if (!Resolve(d.Exe, out file, out prefix)) { error = d.Name + " is not installed (\"" + d.Exe + "\" was not found on PATH). Install it or fix agents.json."; return false; }
            string mode = chat.Mode == "ask" ? d.ModeAsk : chat.Mode == "auto" ? d.ModeAuto : d.ModeEdit;
            string session; chat.Sessions.TryGetValue(d.Id, out session);
            string resume = string.IsNullOrEmpty(session) || string.IsNullOrEmpty(d.Resume) ? "" : d.Resume.Replace("{session}", Quote(session));
            string args = d.Args.Replace("{mode}", mode ?? "").Replace("{resume}", resume).Replace("{folder}", Quote(chat.Folder));
            // Arguments that carry the prompt: cmd.exe would mangle & | % ^, so only real programs get it inline.
            args = args.Replace("{prompt}", d.Stdin ? "" : Quote(prompt));
            args = Regex.Replace(args, "\\s{2,}", " ").Trim();
            bool viaCmd = file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && prefix.StartsWith("/d");
            var psi = new ProcessStartInfo(file, prefix + args + (viaCmd ? "\"" : ""))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                WorkingDirectory = Directory.Exists(chat.Folder) ? chat.Folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["FORCE_COLOR"] = "0";
            if (psi.EnvironmentVariables.ContainsKey("CLAUDECODE")) psi.EnvironmentVariables.Remove("CLAUDECODE");
            try
            {
                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.Start();
            }
            catch (Exception ex) { error = "Could not start " + d.Name + ": " + ex.Message; return false; }
            string format = d.Format ?? "text";
            var p = proc;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var input = p.StandardInput.BaseStream;
                    if (d.Stdin) { var bytes = new UTF8Encoding(false).GetBytes(prompt); input.Write(bytes, 0, bytes.Length); }
                    input.Close();
                }
                catch { }
            });
            var errThread = new Thread(() => { try { string l; while ((l = p.StandardError.ReadLine()) != null) lock (stderr) { if (stderr.Length < 20000) stderr.AppendLine(l); } } catch { } }) { IsBackground = true };
            errThread.Start();
            var outThread = new Thread(() =>
            {
                try { string l; while ((l = p.StandardOutput.ReadLine()) != null) { try { Parse(format, l); } catch (Exception ex) { App.Log("Agent parse: " + ex.Message); } } }
                catch { }
                try { p.WaitForExit(); } catch { }
                errThread.Join(2000);
                int code = -1; try { code = p.ExitCode; } catch { }
                if (format == "text" && plain.Length > 0) Emit("text", plain.ToString().TrimEnd());
                if (!sawText && !stopped)
                {
                    string err; lock (stderr) err = stderr.ToString().Trim();
                    if (err.Length > 0) Emit("error", err.Length > 1500 ? "..." + err.Substring(err.Length - 1500) : err);
                    else if (code != 0 && !sawError) Emit("error", d.Name + " exited with code " + code + " and no answer.");
                }
                if (stopped) Emit("info", "Stopped.");
                var h = Exited; if (h != null) h(code);
            }) { IsBackground = true };
            outThread.Start();
            return true;
        }

        public bool Running { get { try { return proc != null && !proc.HasExited; } catch { return false; } } }

        // Ends the agent and every process it started (shells, node workers).
        public void Stop()
        {
            stopped = true;
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    var k = new ProcessStartInfo("taskkill.exe", "/PID " + proc.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true };
                    using (var kp = Process.Start(k)) kp.WaitForExit(5000);
                }
            }
            catch { try { proc.Kill(); } catch { } }
        }

        void Emit(string kind, string text)
        {
            if (string.IsNullOrEmpty(text) && kind != "session") return;
            if (kind == "text") sawText = true;
            if (kind == "error") sawError = true;
            if (kind == "session") { if (text == sessionSeen || string.IsNullOrEmpty(text)) return; sessionSeen = text; }
            var h = Event; if (h != null) h(kind, text);
        }

        // ---------- JSON helpers ----------
        static Dictionary<string, object> D(object o, string key) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null; }
        static string S(object o, string key) { var d = o as Dictionary<string, object>; object v; return d != null && d.TryGetValue(key, out v) && v != null && !(v is Dictionary<string, object>) && !(v is object[]) ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null; }
        static IEnumerable<object> A(object o, string key)
        {
            var d = o as Dictionary<string, object>; object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null || v is string) return Enumerable.Empty<object>();
            var e = v as System.Collections.IEnumerable; return e != null ? e.Cast<object>() : Enumerable.Empty<object>();
        }
        static string Short(string s, int max) { if (s == null) return ""; s = s.Replace("\r", "").Replace("\n", " "); return s.Length > max ? s.Substring(0, max) + "..." : s; }
        // One line that says what a tool call does: the file, command, pattern or query it got.
        static string Describe(string name, object input)
        {
            var d = input as Dictionary<string, object>;
            string detail = null;
            if (d != null)
                foreach (var k in new[] { "command", "file_path", "path", "notebook_path", "pattern", "query", "url", "description", "prompt", "skill" })
                { var v = S(d, k); if (!string.IsNullOrEmpty(v)) { detail = v; break; } }
            else if (input is string) detail = (string)input;
            return Short(name + (detail != null ? "  " + detail : ""), 220);
        }

        void Parse(string format, string line)
        {
            line = line.Trim();
            if (format == "text" || !line.StartsWith("{")) { if (format == "text") plain.AppendLine(line); return; }
            Dictionary<string, object> o;
            try { o = Js.DeserializeObject(line) as Dictionary<string, object>; } catch { return; }
            if (o == null) return;
            string type = S(o, "type") ?? "";
            switch (format)
            {
                case "claude":
                    {
                        var sid = S(o, "session_id"); if (sid != null) Emit("session", sid);
                        if (type == "assistant")
                        {
                            foreach (var c in A(D(o, "message"), "content"))
                            {
                                string ct = S(c, "type");
                                if (ct == "text") Emit("text", S(c, "text"));
                                else if (ct == "tool_use") { var cd = c as Dictionary<string, object>; object inp = null; if (cd != null) cd.TryGetValue("input", out inp); Emit("tool", Describe(S(c, "name"), inp)); }
                            }
                        }
                        else if (type == "result")
                        {
                            bool err = S(o, "is_error") == "True" || (S(o, "subtype") ?? "success") != "success";
                            if (err) Emit("error", S(o, "result") ?? ("Turn ended: " + S(o, "subtype")));
                            else if (!sawText) Emit("text", S(o, "result"));
                        }
                        break;
                    }
                case "codex":
                    {
                        if (type == "thread.started") Emit("session", S(o, "thread_id"));
                        else if (type == "item.completed")
                        {
                            var it = D(o, "item"); string it_ = S(it, "type") ?? "";
                            if (it_ == "agent_message") Emit("text", S(it, "text"));
                            else if (it_ == "command_execution") { string ec = S(it, "exit_code"); Emit("tool", Short("$ " + S(it, "command"), 220) + (ec != null && ec != "0" ? "   (exit " + ec + ")" : "")); }
                            else if (it_ == "file_change") Emit("tool", "Changed  " + Short(string.Join(", ", A(it, "changes").Select(c => S(c, "path")).Where(x => x != null)), 200));
                            else if (it_ == "mcp_tool_call") Emit("tool", "MCP  " + S(it, "server") + "." + S(it, "tool"));
                            else if (it_ == "web_search") Emit("tool", "Search  " + S(it, "query"));
                            else if (it_ == "error") { string m = S(it, "message") ?? ""; if (m.IndexOf("deprecated", StringComparison.OrdinalIgnoreCase) < 0) Emit("error", m); }
                        }
                        else if (type == "turn.failed") Emit("error", S(D(o, "error"), "message") ?? "Turn failed.");
                        else if (type == "error") Emit("error", S(o, "message"));
                        break;
                    }
                case "agy":
                    {
                        string ev = S(o, "event") ?? "";
                        var sid = S(o, "conversation_id"); if (sid != null) Emit("session", sid);
                        if (ev == "step_update")
                        {
                            var su = D(o, "step_update"); string idx = S(su, "step_index") ?? "0", st = S(su, "state") ?? "", kind = S(su, "step_type") ?? "";
                            if (kind == "agent_response")
                            {
                                StringBuilder sb; if (!steps.TryGetValue(idx, out sb)) steps[idx] = sb = new StringBuilder();
                                sb.Append(S(su, "text_delta") ?? "");
                                if (st == "DONE") { Emit("text", sb.ToString().Trim()); steps.Remove(idx); }
                            }
                            else if (st == "DONE" && kind != "user_input")
                            {
                                string what = S(su, "tool_name") ?? S(su, "title") ?? S(su, "command") ?? S(su, "summary");
                                Emit("tool", Short(kind.Replace('_', ' ') + (what != null ? "  " + what : ""), 220));
                            }
                        }
                        else if (ev == "result")
                        {
                            var r = D(o, "result");
                            if ((S(r, "status") ?? "SUCCESS") != "SUCCESS") Emit("error", "Antigravity: " + S(r, "status") + " " + (S(r, "error") ?? ""));
                            else if (!sawText) Emit("text", (S(r, "response") ?? "").Trim());
                        }
                        else if (ev == "error") Emit("error", S(o, "message") ?? S(D(o, "error"), "message"));
                        break;
                    }
                case "cline":
                    {
                        var tid = S(o, "taskId"); if (tid != null) Emit("session", tid);
                        if (type == "agent_event")
                        {
                            var e = D(o, "event"); string et = S(e, "type") ?? "";
                            if (et == "content_end")
                            {
                                string ct = S(e, "contentType") ?? "";
                                if (ct == "text") Emit("text", S(e, "text"));
                                else if (ct != "reasoning" && ct != "thinking") { var ed = e as Dictionary<string, object>; object inp = null; if (ed != null && !ed.TryGetValue("input", out inp)) ed.TryGetValue("toolInput", out inp); Emit("tool", Describe(S(e, "toolName") ?? S(e, "name") ?? ct, inp)); }
                            }
                            else if (et == "done" && !sawText) Emit("text", S(e, "text"));
                            else if (et == "error") Emit("error", S(e, "message") ?? S(e, "error"));
                        }
                        else if (type == "error") Emit("error", S(o, "message"));
                        break;
                    }
                case "opencode":
                    {
                        var sid = S(o, "sessionID"); if (sid != null) Emit("session", sid);
                        var part = D(o, "part");
                        if (type == "text") Emit("text", S(part, "text"));
                        else if (type == "tool_use" || type == "tool") { var stt = D(part, "state"); object inp = stt != null && stt.ContainsKey("input") ? stt["input"] : null; Emit("tool", Describe(S(part, "tool") ?? "tool", inp)); }
                        else if (type == "error") Emit("error", S(D(D(o, "error"), "data"), "message") ?? S(D(o, "error"), "message") ?? "Error");
                        break;
                    }
                default:
                    {
                        string t = S(o, "text") ?? S(o, "result") ?? S(o, "content") ?? S(o, "message");
                        if (t != null) Emit("text", t);
                        break;
                    }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // Transcript: your messages on the right, agent answers as cards with code blocks, tool calls as quiet lines.
    // ---------------------------------------------------------------------------------------------------
    class ChatView : Panel
    {
        AgentChat chat;
        public string Working;         // "Codex is working" while a turn runs
        public long WorkingSince;
        public string LastTool;
        class Seg { public string Text; public bool Code; public int H; }
        class Item { public AgentMsg M; public List<Seg> Segs; public int Y, H; public Rectangle Box; }
        readonly List<Item> items = new List<Item>();
        int measuredFor = -1, measuredCount = -1;
        static Font mono;
        public event Action<string> Status;
        readonly ContextMenuStrip menu;
        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        public static Font Mono
        {
            get
            {
                if (mono == null)
                {
                    string f = Store.Settings.MonoFont; float sz = Math.Max(8f, Store.Settings.MonoFontSize - 0.5f);
                    foreach (var name in new[] { f, "Cascadia Mono", "Consolas" })
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        try { var t = new Font(name, sz); if (string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) { mono = t; break; } t.Dispose(); } catch { }
                    }
                    if (mono == null) mono = new Font(FontFamily.GenericMonospace, sz);
                }
                return mono;
            }
        }

        public ChatView()
        {
            DoubleBuffered = true; AutoScroll = true; BackColor = Ui.Bg;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            menu = Ui.Menu(); Ui.ThemeMenu(menu);
        }
        public void SetChat(AgentChat c) { chat = c; measuredFor = -1; Relayout(true); }

        static List<Seg> Split(string text)
        {
            var segs = new List<Seg>();
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            var sb = new StringBuilder(); bool code = false;
            Action flush = () => { string s = sb.ToString().Trim('\n'); if (s.Trim().Length > 0) segs.Add(new Seg { Text = s, Code = code }); sb.Length = 0; };
            foreach (var l in lines)
            {
                if (l.TrimStart().StartsWith("```")) { flush(); code = !code; continue; }
                sb.Append(l).Append('\n');
            }
            flush();
            return segs;
        }

        public void Relayout(bool stickToBottom)
        {
            if (chat == null) { items.Clear(); AutoScrollMinSize = Size.Empty; Invalidate(); return; }
            bool atBottom = stickToBottom || -AutoScrollPosition.Y + ClientSize.Height >= AutoScrollMinSize.Height - Ui.S(40);
            int w = ClientSize.Width;
            if (w != measuredFor) { items.Clear(); measuredFor = w; }
            // Only new messages are measured; existing ones keep their size until the width changes.
            if (items.Count > chat.Messages.Count) items.Clear();
            using (var g = CreateGraphics())
            {
                int y = items.Count > 0 ? items[items.Count - 1].Y + items[items.Count - 1].H : Ui.S(12);
                for (int i = items.Count; i < chat.Messages.Count; i++)
                {
                    var it = new Item { M = chat.Messages[i] };
                    Measure(g, it, w);
                    it.Y = y; y += it.H;
                    items.Add(it);
                }
                AutoScrollMinSize = new Size(0, y + Ui.S(Working != null ? 64 : 16));
            }
            measuredCount = chat.Messages.Count;
            if (atBottom) ScrollToBottom();
            Invalidate();
        }
        public void ScrollToBottom() { AutoScrollPosition = new Point(0, Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height)); }

        void Measure(Graphics g, Item it, int w)
        {
            int pad = Ui.S(14);
            var m = it.M;
            if (m.Role == "tool")
            {
                it.H = Ui.S(22); it.Box = new Rectangle(pad + Ui.S(6), 0, w - pad * 2 - Ui.S(6), Ui.S(20));
                return;
            }
            if (m.Role == "info" || m.Role == "error")
            {
                int tw = w - pad * 2 - Ui.S(24);
                int th = TextRenderer.MeasureText(g, m.Text, Ui.Small, new Size(Math.Max(40, tw), int.MaxValue), Wrap).Height;
                it.H = th + Ui.S(m.Role == "error" ? 38 : 14); it.Box = new Rectangle(pad, Ui.S(4), w - pad * 2, it.H - Ui.S(8));
                return;
            }
            it.Segs = Split(m.Text);
            if (m.Role == "user")
            {
                int maxW = Math.Max(Ui.S(120), (w - pad * 2) * 82 / 100);
                var sz = TextRenderer.MeasureText(g, m.Text, Ui.Base, new Size(maxW - Ui.S(24), int.MaxValue), Wrap);
                int bw = Math.Min(maxW, sz.Width + Ui.S(26));
                it.Box = new Rectangle(w - pad - bw, Ui.S(4), bw, sz.Height + Ui.S(18));
                it.H = it.Box.Height + Ui.S(14);
                return;
            }
            // agent card
            int inner = w - pad * 2 - Ui.S(28);
            int h = Ui.S(30);
            foreach (var s in it.Segs)
            {
                if (s.Code) s.H = TextRenderer.MeasureText(g, s.Text, Mono, new Size(Math.Max(40, inner - Ui.S(16)), int.MaxValue), Wrap).Height + Ui.S(16);
                else s.H = TextRenderer.MeasureText(g, s.Text, Ui.Base, new Size(Math.Max(40, inner), int.MaxValue), Wrap).Height;
                h += s.H + Ui.S(8);
            }
            it.Box = new Rectangle(pad, Ui.S(4), w - pad * 2, h + Ui.S(4));
            it.H = it.Box.Height + Ui.S(12);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (chat != null && ClientSize.Width != measuredFor && ClientSize.Width > 0) Relayout(false); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            if (chat == null) return;
            if (chat.Messages.Count != measuredCount) Relayout(false);
            int oy = AutoScrollPosition.Y;
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            if (items.Count == 0 && Working == null)
            {
                var r = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
                string who = chat.Agents.Count == 0 ? "an agent" : string.Join(" + ", chat.Agents.Select(AgentStore.NameOf));
                TextRenderer.DrawText(g, "New chat with " + who, Ui.Big, new Rectangle(0, r.Height / 2 - Ui.S(40), r.Width, Ui.S(30)), Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, "Folder: " + (chat.Folder.Length > 0 ? chat.Folder : "(choose one in the header)") + "\nType below. Enter sends, Shift+Enter makes a new line.", Ui.Small,
                    new Rectangle(Ui.S(20), r.Height / 2 - Ui.S(4), r.Width - Ui.S(40), Math.Max(Ui.S(40), r.Height / 2)), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.PathEllipsis);
                return;
            }
            foreach (var it in items)
            {
                int top = it.Y + oy;
                if (top + it.H < 0 || top > ClientSize.Height) continue;
                var m = it.M; var box = new Rectangle(it.Box.X, top + it.Box.Y, it.Box.Width, it.Box.Height);
                if (m.Role == "tool")
                {
                    using (var b = new SolidBrush(AgentStore.ColorOf(m.Agent))) g.FillEllipse(b, box.X, box.Y + box.Height / 2 - Ui.S(3), Ui.S(6), Ui.S(6));
                    TextRenderer.DrawText(g, m.Text, Mono, new Rectangle(box.X + Ui.S(14), box.Y, box.Width - Ui.S(14), box.Height), Ui.Muted, flags | TextFormatFlags.PathEllipsis);
                }
                else if (m.Role == "info")
                    TextRenderer.DrawText(g, m.Text, Ui.Small, box, Ui.Muted, Wrap | TextFormatFlags.HorizontalCenter);
                else if (m.Role == "error")
                {
                    using (var path = Ui.Round(box, Ui.S(8))) { using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 40 : 22, Ui.Danger))) g.FillPath(b, path); using (var p = new Pen(Color.FromArgb(120, Ui.Danger))) g.DrawPath(p, path); }
                    TextRenderer.DrawText(g, (m.Agent.Length > 0 ? AgentStore.NameOf(m.Agent) + ": " : ""), Ui.Bold, new Rectangle(box.X + Ui.S(12), box.Y + Ui.S(4), box.Width, Ui.S(18)), Ui.Danger, flags);
                    TextRenderer.DrawText(g, m.Text, Ui.Small, new Rectangle(box.X + Ui.S(12), box.Y + Ui.S(22), box.Width - Ui.S(24), box.Height - Ui.S(24)), Ui.Text, Wrap);
                }
                else if (m.Role == "user")
                {
                    using (var path = Ui.Round(box, Ui.S(12))) using (var b = new SolidBrush(Ui.AccentFill)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, m.Text, Ui.Base, new Rectangle(box.X + Ui.S(13), box.Y + Ui.S(9), box.Width - Ui.S(24), box.Height - Ui.S(14)), Color.White, Wrap);
                }
                else
                {
                    var col = AgentStore.ColorOf(m.Agent);
                    using (var path = Ui.Round(box, Ui.S(12))) { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(Ui.Line)) g.DrawPath(p, path); }
                    using (var b = new SolidBrush(col)) g.FillEllipse(b, box.X + Ui.S(14), box.Y + Ui.S(13), Ui.S(8), Ui.S(8));
                    TextRenderer.DrawText(g, AgentStore.NameOf(m.Agent), Ui.Bold, new Rectangle(box.X + Ui.S(28), box.Y + Ui.S(6), box.Width / 2, Ui.S(22)), col, flags);
                    TextRenderer.DrawText(g, new DateTime(m.Time).ToString("HH:mm"), Ui.Small, new Rectangle(box.Right - Ui.S(80), box.Y + Ui.S(6), Ui.S(66), Ui.S(22)), Ui.Muted, flags | TextFormatFlags.Right);
                    int y = box.Y + Ui.S(30), x = box.X + Ui.S(14), inner = box.Width - Ui.S(28);
                    foreach (var s in it.Segs)
                    {
                        if (s.Code)
                        {
                            var cr = new Rectangle(x, y, inner, s.H);
                            using (var path = Ui.Round(cr, Ui.S(6))) using (var b = new SolidBrush(Ui.Dark ? Color.FromArgb(18, 20, 26) : Color.FromArgb(244, 246, 250))) g.FillPath(b, path);
                            TextRenderer.DrawText(g, s.Text, Mono, new Rectangle(x + Ui.S(8), y + Ui.S(8), inner - Ui.S(16), s.H - Ui.S(12)), Ui.Text, Wrap);
                        }
                        else TextRenderer.DrawText(g, s.Text, Ui.Base, new Rectangle(x, y, inner, s.H + Ui.S(4)), Ui.Text, Wrap);
                        y += s.H + Ui.S(8);
                    }
                }
            }
            if (Working != null)
            {
                int y = (items.Count > 0 ? items[items.Count - 1].Y + items[items.Count - 1].H : Ui.S(12)) + oy;
                long secs = Math.Max(0, (Store.Now() - WorkingSince) / TimeSpan.TicksPerSecond);
                int dots = (int)((Environment.TickCount / 400) % 4);
                TextRenderer.DrawText(g, Working + new string('.', dots) + "   " + (secs >= 60 ? (secs / 60) + "m " : "") + (secs % 60) + "s", Ui.Bold, new Rectangle(Ui.S(20), y + Ui.S(4), ClientSize.Width - Ui.S(40), Ui.S(22)), Ui.Accent, flags);
                if (!string.IsNullOrEmpty(LastTool)) TextRenderer.DrawText(g, LastTool, Mono, new Rectangle(Ui.S(20), y + Ui.S(26), ClientSize.Width - Ui.S(40), Ui.S(20)), Ui.Muted, flags);
            }
        }

        Item At(Point p) { int y = p.Y - AutoScrollPosition.Y; return items.FirstOrDefault(i => y >= i.Y && y < i.Y + i.H); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Right || chat == null) return;
            var it = At(e.Location);
            menu.Items.Clear();
            if (it != null)
            {
                string text = it.M.Text;
                menu.Items.Add("Copy message", null, delegate { App.I.CopyText(text); Say("Message copied."); });
                var code = it.Segs == null ? new List<Seg>() : it.Segs.Where(s => s.Code).ToList();
                if (code.Count > 0) menu.Items.Add("Copy code", null, delegate { App.I.CopyText(string.Join("\n\n", code.Select(s => s.Text))); Say("Code copied."); });
                menu.Items.Add("Save as prompt", null, delegate { Store.NewPrompt(Short(text, 50), text, "Agents"); Say("Saved to Prompts."); });
                menu.Items.Add(new ToolStripSeparator());
            }
            menu.Items.Add("Copy whole chat (Markdown)", null, delegate { App.I.CopyText(AgentStore.Markdown(chat)); Say("Chat copied as Markdown."); });
            menu.Show(this, e.Location);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var it = At(e.Location);
            if (it != null) { App.I.CopyText(it.M.Text); Say("Message copied."); }
        }
        void Say(string s) { var h = Status; if (h != null) h(s); }
        static string Short(string s, int n) { s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); return s.Length > n ? s.Substring(0, n) + "..." : s; }
    }

    // ---------------------------------------------------------------------------------------------------
    // One chat pane: header (title, agents, folder, mode, actions), transcript, input.
    // ---------------------------------------------------------------------------------------------------
    class ChatPane : Panel
    {
        public AgentChat Chat { get; private set; }
        public event Action<ChatPane> Activated, Changed;
        readonly ChatView view;
        readonly TextBox input;
        readonly Button send;
        readonly PaneHeader header;
        readonly Queue<string> queue = new Queue<string>();
        AgentRun run;
        string runAgent;
        bool active;
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 400 };

        public bool Busy { get { return runAgent != null; } }
        public bool IsActive { get { return active; } set { if (active != value) { active = value; Invalidate(); header.Invalidate(); } } }

        public ChatPane(AgentChat chat)
        {
            Padding = new Padding(Ui.S(1)); BackColor = Ui.Bg;
            view = new ChatView { Dock = DockStyle.Fill };
            view.Status += s => App.I.Status(s);
            view.MouseDown += delegate { Activate(); };
            header = new PaneHeader(this) { Dock = DockStyle.Top };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(74), Padding = new Padding(Ui.S(10), Ui.S(6), Ui.S(10), Ui.S(10)), BackColor = Ui.Bg };
            input = new TextBox { Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = Ui.Base, AcceptsReturn = true, ScrollBars = ScrollBars.None, BackColor = Ui.Card, ForeColor = Ui.Text };
            var inputCard = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(10), Ui.S(8), Ui.S(8), Ui.S(6)), BackColor = Ui.Bg };
            inputCard.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Ui.Round(new Rectangle(0, 0, inputCard.Width - 1, inputCard.Height - 1), Ui.S(10)))
                { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(input.Focused ? Ui.Accent : Ui.Line)) g.DrawPath(p, path); }
            };
            inputCard.Resize += delegate { inputCard.Invalidate(); };
            inputCard.Controls.Add(input);
            input.GotFocus += delegate { inputCard.Invalidate(); input.Visible = true; };
            input.LostFocus += delegate { inputCard.Invalidate(); };
            // Placeholder (cue banners do not work on multi-line boxes): a label inside the box, hidden while there is text.
            var hintLabel = new Label { Text = "Message the agents in this chat  (Enter sends, Shift+Enter new line)", AutoSize = false, Dock = DockStyle.Fill, ForeColor = Ui.Muted, BackColor = Ui.Card, Font = Ui.Base, Cursor = Cursors.IBeam, UseMnemonic = false };
            hintLabel.Click += delegate { input.Focus(); };
            input.Controls.Add(hintLabel);
            input.TextChanged += delegate { hintLabel.Visible = input.TextLength == 0; };
            input.KeyDown += OnInputKey;
            input.GotFocus += delegate { Activate(); };
            send = Ui.Btn("Send", delegate { if (Busy) Stop(); else SendInput(); }, true);
            send.Dock = DockStyle.Right; send.Width = Ui.S(84); send.Margin = new Padding(0);
            bottom.Controls.Add(inputCard);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Ui.S(8), BackColor = Ui.Bg });
            bottom.Controls.Add(send);
            Controls.Add(view); Controls.Add(bottom); Controls.Add(header);
            tick.Tick += delegate { if (Busy) view.Invalidate(); };
            SetChat(chat);
        }

        void Activate() { var h = Activated; if (h != null) h(this); }
        void RaiseChanged() { header.Invalidate(); var h = Changed; if (h != null) h(this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
            using (var p = new Pen(active ? Ui.Accent : Ui.Line, active ? Ui.S(2) : 1)) g.DrawPath(p, path);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }

        public void SetChat(AgentChat chat)
        {
            if (Busy) Stop();
            Chat = chat;
            view.SetChat(chat);
            header.Invalidate();
            RaiseChanged();
        }

        void OnInputKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendInput(); }
            else if (e.KeyCode == Keys.Escape && Busy) { e.SuppressKeyPress = true; Stop(); }
        }
        void SendInput()
        {
            string text = input.Text.Trim();
            if (text.Length == 0 || Busy) return;
            input.Clear();
            Send(text);
        }

        // Adds your message and lets every agent of the chat answer in turn.
        public void Send(string text)
        {
            if (Busy || string.IsNullOrWhiteSpace(text)) return;
            var c = Chat;
            if (c.Agents.Count == 0) c.Agents.Add(AgentStore.Defs[0].Id);
            if (c.Folder.Length == 0 || !Directory.Exists(c.Folder))
            {
                var recent = MetaStore.Recent("folders", 1).FirstOrDefault();
                c.Folder = recent != null && Directory.Exists(recent.Id) ? recent.Id : Store.Settings.TermRecent.FirstOrDefault(Directory.Exists) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (c.Title.Length == 0) c.Title = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (c.Title.Length > 70) c.Title = c.Title.Substring(0, 70) + "...";
            if (!AgentStore.Chats.Contains(c)) AgentStore.Chats.Add(c);
            Add("user", "", text);
            MetaStore.Touch("folders", c.Folder);
            queue.Clear();
            foreach (var a in c.Agents) queue.Enqueue(a);
            send.Text = "Stop";
            tick.Start();
            RunNext();
        }

        void Add(string role, string agent, string text)
        {
            Chat.Messages.Add(new AgentMsg { Role = role, Agent = agent ?? "", Text = text ?? "", Time = Store.Now() });
            Chat.Updated = Store.Now();
            view.Relayout(role == "user");
        }

        // What an agent has not seen yet: your new message, plus what the other agents answered since its last turn.
        string BuildPrompt(string agentId)
        {
            var c = Chat; int seen; c.Seen.TryGetValue(agentId, out seen);
            var news = c.Messages.Skip(Math.Min(seen, c.Messages.Count)).Where(m => (m.Role == "user" || m.Role == "agent") && !(m.Role == "agent" && m.Agent == agentId)).ToList();
            if (news.Count == 1 && news[0].Role == "user") return news[0].Text;
            var sb = new StringBuilder();
            sb.Append("You are in a group chat with the user and other coding agents (" + string.Join(", ", c.Agents.Where(a => a != agentId).Select(AgentStore.NameOf)) + "), all working in this folder. New messages since your last turn:\n\n");
            foreach (var m in news)
            {
                string t = m.Text.Length > 6000 ? m.Text.Substring(0, 6000) + "\n[...]" : m.Text;
                sb.Append("[").Append(m.Role == "user" ? "User" : AgentStore.NameOf(m.Agent)).Append("]\n").Append(t).Append("\n\n");
            }
            sb.Append("Answer the user's latest message. Build on the other agents' answers instead of repeating them.");
            return sb.ToString();
        }

        void RunNext()
        {
            if (queue.Count == 0) { Done(); return; }
            string id = queue.Dequeue();
            var def = AgentStore.Def(id);
            if (def == null) { Add("error", id, "Unknown agent \"" + id + "\" (not in agents.json)."); RunNext(); return; }
            string prompt = BuildPrompt(id);
            var r = new AgentRun();
            runAgent = id; run = r;
            view.Working = def.Name + " is working"; view.WorkingSince = Store.Now(); view.LastTool = null; view.Relayout(false);
            r.Event += (kind, text) => Ui0(() =>
            {
                if (r != run) return;
                if (kind == "session") { Chat.Sessions[id] = text; return; }
                if (kind == "tool") { view.LastTool = text; Add("tool", id, text); return; }
                Add(kind == "text" ? "agent" : kind, id, text);
            });
            r.Exited += code => Ui0(() =>
            {
                if (r != run) return;
                Chat.Seen[id] = Chat.Messages.Count;
                run = null; runAgent = null;
                AgentStore.Save(Chat);
                RunNext();
            });
            string err;
            if (!r.Start(def, Chat, prompt, out err)) { run = null; runAgent = null; Add("error", id, err); RunNext(); return; }
            MetaStore.Touch("agents", id);
            RaiseChanged();
        }
        void Done()
        {
            runAgent = null; run = null;
            view.Working = null; view.LastTool = null; view.Relayout(false);
            send.Text = "Send"; tick.Stop();
            AgentStore.Save(Chat);
            RaiseChanged();
            var f = FindForm();
            if (f != null && !f.ContainsFocus) App.I.Balloon("Agents", (Chat.Title.Length > 0 ? Chat.Title : "Chat") + ": answered.");
        }
        public void Stop()
        {
            queue.Clear();
            var r = run;
            if (r != null) ThreadPool.QueueUserWorkItem(delegate { r.Stop(); });
            else Done();
        }
        void Ui0(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        public void FocusInput() { input.Focus(); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tick.Dispose(); var r = run; if (r != null) { queue.Clear(); try { r.Stop(); } catch { } } }
            base.Dispose(disposing);
        }

        // ---------- header actions ----------
        public void PickAgents(Control anchor, Point at)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            foreach (var d in AgentStore.Defs)
            {
                string id = d.Id; string f, pre; bool installed = AgentRun.Resolve(d.Exe, out f, out pre);
                var item = new ToolStripMenuItem(d.Name + (installed ? "" : "   (not installed)"), Ui.Swatch(AgentStore.ColorOf(id), Ui.S(12))) { Checked = Chat.Agents.Contains(id), CheckOnClick = false };
                item.Click += delegate
                {
                    if (Busy) return;
                    if (Chat.Agents.Contains(id)) { if (Chat.Agents.Count > 1) Chat.Agents.Remove(id); }
                    else Chat.Agents.Add(id);
                    view.Invalidate(); RaiseChanged(); AgentStore.Save(Chat);
                };
                m.Items.Add(item);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Only one agent at a time: click to add or remove. Several = they answer in turn.").Enabled = false;
            m.Items.Add("Edit the agent list (agents.json)...", null, delegate { try { Directory.CreateDirectory(AgentStore.Dir); var unused = AgentStore.Defs; Process.Start("notepad.exe", Quote(AgentStore.DefsFile)); } catch { } });
            m.Items.Add("Reload the agent list", null, delegate { AgentStore.ReloadDefs(); RaiseChanged(); });
            m.Show(anchor, at);
        }
        static string Quote(string s) { return "\"" + s + "\""; }

        public void PickFolder(Control anchor, Point at)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            Action<string> use = p => { Chat.Folder = p; MetaStore.Touch("folders", p); if (Chat.Messages.Count > 0) { Chat.Sessions.Clear(); Add("info", "", "Folder changed to " + p + ". Agents start new sessions there."); } RaiseChanged(); view.Invalidate(); };
            var favs = MetaStore.Favorites("folders");
            foreach (var f in favs.Take(15)) { string p = f.Id; m.Items.Add("★  " + MetaStore.DisplayName(f) + "   " + p, null, delegate { use(p); }); }
            var rec = MetaStore.Recent("folders", 12).Where(x => !x.Fav).ToList();
            if (favs.Count > 0 && rec.Count > 0) m.Items.Add(new ToolStripSeparator());
            foreach (var f in rec) { string p = f.Id; m.Items.Add(MetaStore.DisplayName(f) + "   " + p, null, delegate { use(p); }); }
            if (m.Items.Count > 0) m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Browse...", null, delegate
            {
                using (var d = new FolderBrowserDialog { SelectedPath = Directory.Exists(Chat.Folder) ? Chat.Folder : "" })
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) use(d.SelectedPath);
            });
            if (Directory.Exists(Chat.Folder))
            {
                string cf = Chat.Folder;
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Open in Explorer page", null, delegate { App.I.OpenInExplorerPage(cf); });
                Term.AddMenu(m.Items, () => cf);
            }
            m.Show(anchor, at);
        }

        public void PickMode(Control anchor, Point at)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            foreach (var o in new[] { new[] { "ask", "Ask / plan only: agents read and answer, no edits" }, new[] { "edit", "Edit: agents may change files in the folder" }, new[] { "auto", "Full auto: no permission checks (only in folders you trust)" } })
            {
                string k = o[0];
                m.Items.Add(new ToolStripMenuItem(o[1], null, delegate { Chat.Mode = k; RaiseChanged(); AgentStore.Save(Chat); }) { Checked = Chat.Mode == k });
            }
            m.Show(anchor, at);
        }

        public void ChatMenu(Control anchor, Point at, AgentsPage page)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            m.Items.Add("New chat in this pane", null, delegate { page.NewChatIn(this); });
            m.Items.Add("Rename chat...", null, delegate { var v = Ui.Ask("Chat name", "Name", Chat.Title, FindForm()); if (v != null) { Chat.Title = v.Trim(); RaiseChanged(); AgentStore.Save(Chat); } });
            m.Items.Add(Chat.Fav ? "Remove from favorites" : "Add to favorites", null, delegate { Chat.Fav = !Chat.Fav; RaiseChanged(); AgentStore.Save(Chat); });
            m.Items.Add("Copy chat as Markdown", null, delegate { App.I.CopyText(AgentStore.Markdown(Chat)); });
            m.Items.Add("Export chat...", null, delegate { page.Export(Chat); });
            m.Items.Add("Start fresh sessions (agents forget earlier turns)", null, delegate { Chat.Sessions.Clear(); Chat.Seen.Clear(); Add("info", "", "Agents start new sessions from the next message."); AgentStore.Save(Chat); });
            m.Show(anchor, at);
        }

        // Header: chat title, agent chips, folder, mode, menu.
        class PaneHeader : Control
        {
            readonly ChatPane pane;
            class Hot { public Rectangle R; public string Key; }
            readonly List<Hot> hots = new List<Hot>();
            string hot;
            readonly ToolTip tip = new ToolTip();
            public PaneHeader(ChatPane pane)
            {
                this.pane = pane; Height = Ui.S(70);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
            Rectangle Chip(Graphics g, ref int x, int y, string glyph, string text, Color dot, string key, int maxW)
            {
                int w = Math.Min(maxW, TextRenderer.MeasureText(text, Ui.Small).Width + Ui.S(glyph != null || !dot.IsEmpty ? 34 : 18));
                var r = new Rectangle(x, y, w, Ui.S(24));
                bool isHot = hot == key;
                using (var path = Ui.Round(r, Ui.S(12)))
                {
                    using (var b = new SolidBrush(isHot ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                    using (var p = new Pen(isHot ? Ui.Accent : Ui.Line)) g.DrawPath(p, path);
                }
                int tx = r.X + Ui.S(9);
                if (!dot.IsEmpty) { using (var b = new SolidBrush(dot)) g.FillEllipse(b, r.X + Ui.S(9), r.Y + Ui.S(8), Ui.S(8), Ui.S(8)); tx = r.X + Ui.S(22); }
                else if (glyph != null) { TextRenderer.DrawText(g, glyph, Ui.Icon(9f), new Rectangle(r.X + Ui.S(6), r.Y, Ui.S(18), r.Height), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter); tx = r.X + Ui.S(24); }
                TextRenderer.DrawText(g, text, Ui.Small, new Rectangle(tx, r.Y, r.Right - tx - Ui.S(6), r.Height), Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                hots.Add(new Hot { R = r, Key = key });
                x = r.Right + Ui.S(6);
                return r;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Ui.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
                hots.Clear();
                var c = pane.Chat; if (c == null) return;
                var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                // row 1: title + menu
                var menuR = new Rectangle(Width - Ui.S(38), Ui.S(6), Ui.S(30), Ui.S(26));
                if (hot == "menu") using (var path = Ui.Round(menuR, Ui.S(6))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                TextRenderer.DrawText(g, "", Ui.Icon(11f), menuR, Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                hots.Add(new Hot { R = menuR, Key = "menu" });
                var newR = new Rectangle(menuR.X - Ui.S(32), menuR.Y, Ui.S(30), Ui.S(26));
                if (hot == "new") using (var path = Ui.Round(newR, Ui.S(6))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, path);
                TextRenderer.DrawText(g, "", Ui.Icon(11f), newR, Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                hots.Add(new Hot { R = newR, Key = "new" });
                string title = (c.Fav ? "★ " : "") + (c.Title.Length > 0 ? c.Title : "New chat");
                TextRenderer.DrawText(g, title, Ui.Bold, new Rectangle(Ui.S(12), Ui.S(6), newR.X - Ui.S(16), Ui.S(26)), pane.active ? Ui.Text : Ui.Muted, flags);
                // row 2: agents, folder, mode
                int x = Ui.S(10), y = Ui.S(38);
                int maxW = Math.Max(Ui.S(80), (Width - Ui.S(20)) / 3);
                foreach (var a in c.Agents) Chip(g, ref x, y, null, AgentStore.NameOf(a) + (pane.runAgent == a ? "  •" : ""), AgentStore.ColorOf(a), "agents", Ui.S(140));
                Chip(g, ref x, y, "", c.Agents.Count == 0 ? "Agent" : "", Color.Empty, "agents", Ui.S(80));
                string folder = c.Folder.Length > 0 ? Path.GetFileName(c.Folder.TrimEnd('\\')) : "Folder";
                if (folder.Length == 0) folder = c.Folder;
                Chip(g, ref x, y, "", folder, Color.Empty, "folder", maxW);
                Chip(g, ref x, y, c.Mode == "ask" ? "" : c.Mode == "auto" ? "" : "", c.Mode == "ask" ? "Ask" : c.Mode == "auto" ? "Full auto" : "Edit", Color.Empty, "mode", Ui.S(120));
                using (var p = new Pen(Ui.Line)) g.DrawLine(p, Ui.S(8), Height - 1, Width - Ui.S(8), Height - 1);
            }
            string KeyAt(Point p) { var h = hots.LastOrDefault(x => x.R.Contains(p)); return h != null ? h.Key : null; }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                string k = KeyAt(e.Location);
                if (k != hot)
                {
                    hot = k; Invalidate(); Cursor = k != null ? Cursors.Hand : Cursors.Default;
                    tip.SetToolTip(this, k == "folder" ? pane.Chat.Folder : k == "agents" ? "Agents in this chat (several answer in turn)" : k == "mode" ? "What agents may do" : k == "new" ? "New chat in this pane" : k == "menu" ? "Chat options" : "");
                }
            }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != null) { hot = null; Invalidate(); } }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pane.Activate(); }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                var h = hots.LastOrDefault(x => x.R.Contains(e.Location)); if (h == null) return;
                var at = new Point(h.R.X, h.R.Bottom + Ui.S(2));
                var page = pane.FindPage();
                switch (h.Key)
                {
                    case "agents": pane.PickAgents(this, at); break;
                    case "folder": pane.PickFolder(this, at); break;
                    case "mode": pane.PickMode(this, at); break;
                    case "new": if (page != null) page.NewChatIn(pane); break;
                    case "menu": if (page != null) pane.ChatMenu(this, at, page); break;
                }
            }
        }
        AgentsPage FindPage() { Control c = Parent; while (c != null && !(c is AgentsPage)) c = c.Parent; return c as AgentsPage; }
    }

    // ---------------------------------------------------------------------------------------------------
    // Chat history: favourites first, then newest; search by title, folder and text.
    // ---------------------------------------------------------------------------------------------------
    class ChatHistory : Panel
    {
        public event Action<AgentChat> Open;
        public Func<AgentChat, bool> IsOpen;
        readonly TextBox search;
        readonly ListBox list;
        readonly Label empty;
        List<AgentChat> shown = new List<AgentChat>();
        readonly ContextMenuStrip menu;
        public AgentsPage Page;

        public ChatHistory()
        {
            BackColor = Ui.Card; Padding = new Padding(Ui.S(8), Ui.S(8), Ui.S(8), Ui.S(8));
            var title = new Label { Text = "Chats", Font = Ui.Big, Dock = DockStyle.Top, Height = Ui.S(32), ForeColor = Ui.Text };
            search = Ui.Search("Search chats"); search.Dock = DockStyle.Top;
            search.TextChanged += delegate { Fill(); };
            list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(50), IntegralHeight = false, BackColor = Ui.Card };
            list.DrawItem += DrawRow;
            empty = new Label { Dock = DockStyle.Top, Height = Ui.S(60), ForeColor = Ui.Muted, Font = Ui.Small, Text = "No saved chats yet. Every chat is saved here when an agent answers; star the ones you want on top.", Visible = false };
            list.MouseUp += OnUp;
            list.MouseMove += (s, e) => { int i = list.IndexFromPoint(e.Location); list.Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; };
            menu = Ui.Menu(); Ui.ThemeMenu(menu);
            Controls.Add(list);
            Controls.Add(empty);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(8), BackColor = Ui.Card });
            Controls.Add(search);
            Controls.Add(title);
            AgentStore.Changed += OnChanged;
        }
        void OnChanged() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke((Action)OnChanged); return; } if (Visible) Fill(); }
        protected override void Dispose(bool disposing) { if (disposing) AgentStore.Changed -= OnChanged; base.Dispose(disposing); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Fill(); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height); }

        public void Fill()
        {
            string q = search.Text.Trim();
            IEnumerable<AgentChat> src = AgentStore.Chats.Where(c => c.Messages.Count > 0);
            if (q.Length > 0) src = src.Where(c => c.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Folder.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Messages.Any(m => m.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            shown = src.OrderByDescending(c => c.Fav).ThenByDescending(c => c.Updated).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var c in shown) list.Items.Add(c.Id);
            list.EndUpdate();
            empty.Visible = shown.Count == 0;
            if (q.Length > 0) empty.Text = "No chat matches \"" + q + "\"."; else empty.Text = "No saved chats yet. Every chat is saved here when an agent answers; star the ones you want on top.";
        }

        void DrawRow(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            var c = shown[e.Index]; var g = e.Graphics; var r = e.Bounds;
            bool open = IsOpen != null && IsOpen(c);
            using (var b = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Ui.SelBg : Ui.Card)) g.FillRectangle(b, r);
            if (open) using (var b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, r.X, r.Y + Ui.S(8), Ui.S(3), r.Height - Ui.S(16));
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, (c.Fav ? "★ " : "") + (c.Title.Length > 0 ? c.Title : "Chat"), Ui.Bold, new Rectangle(r.X + Ui.S(10), r.Y + Ui.S(5), r.Width - Ui.S(16), Ui.S(20)), Ui.Text, flags);
            int x = r.X + Ui.S(10);
            foreach (var a in c.Agents.Take(5)) { using (var b = new SolidBrush(AgentStore.ColorOf(a))) g.FillEllipse(b, x, r.Y + Ui.S(32), Ui.S(7), Ui.S(7)); x += Ui.S(10); }
            string when = Ui.Ago(c.Updated);
            TextRenderer.DrawText(g, when + "  ·  " + Path.GetFileName(c.Folder.TrimEnd('\\')), Ui.Small, new Rectangle(x + Ui.S(4), r.Y + Ui.S(26), r.Right - x - Ui.S(10), Ui.S(18)), Ui.Muted, flags);
        }

        void OnUp(object s, MouseEventArgs e)
        {
            int i = list.IndexFromPoint(e.Location);
            if (i < 0 || i >= shown.Count) return;
            var c = shown[i];
            if (e.Button == MouseButtons.Left) { var o = Open; if (o != null) o(c); return; }
            if (e.Button != MouseButtons.Right) return;
            list.SelectedIndex = i;
            menu.Items.Clear();
            menu.Items.Add("Open", null, delegate { var o = Open; if (o != null) o(c); });
            menu.Items.Add(c.Fav ? "Remove from favorites" : "Add to favorites", null, delegate { c.Fav = !c.Fav; AgentStore.Save(c); });
            menu.Items.Add("Rename...", null, delegate { var v = Ui.Ask("Chat name", "Name", c.Title, FindForm()); if (v != null) { c.Title = v.Trim(); AgentStore.Save(c); } });
            menu.Items.Add("Copy as Markdown", null, delegate { App.I.CopyText(AgentStore.Markdown(c)); });
            if (Page != null) menu.Items.Add("Export...", null, delegate { Page.Export(c); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Delete chat", null, delegate
            {
                if (IsOpen != null && IsOpen(c)) { MessageBox.Show(FindForm(), "This chat is open in a pane. Open another chat there first.", App.Name); return; }
                if (MessageBox.Show(FindForm(), "Delete the chat \"" + c.Title + "\"?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) AgentStore.Delete(c);
            });
            menu.Show(list, e.Location);
        }
    }

    // Small pane-layout picker for the Agents bar.
    class LayoutPicker : Control
    {
        public static readonly string[][] Items = { new[] { "1", "1", "1" }, new[] { "2", "2", "1" }, new[] { "3", "3", "1" }, new[] { "4", "4", "1" }, new[] { "6", "6", "1" }, new[] { "2x2", "2", "2" }, new[] { "3x2", "3", "2" } };
        public string Value = "2";
        public event Action Changed;
        int hot = -1;
        public LayoutPicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(Items.Length * Ui.S(40) + Ui.S(4), Ui.S(32)); Margin = new Padding(Ui.S(4), Ui.S(2), Ui.S(10), 0); Cursor = Cursors.Hand;
        }
        Rectangle Cell(int i) { return new Rectangle(Ui.S(2) + i * Ui.S(40), Ui.S(2), Ui.S(36), Ui.S(28)); }
        public static int Count(string id) { return id == "2x2" ? 4 : id == "3x2" ? 6 : int.Parse(id); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Parent != null ? Parent.BackColor : Ui.Bg); g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Items.Length; i++)
            {
                var r = Cell(i); bool sel = Items[i][0] == Value;
                using (var path = Ui.Round(r, Ui.S(6)))
                {
                    using (var b = new SolidBrush(sel ? Ui.SelBg : i == hot ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                    using (var p = new Pen(sel ? Ui.Accent : Ui.Line)) g.DrawPath(p, path);
                }
                int cols = int.Parse(Items[i][1]), rows = int.Parse(Items[i][2]);
                var grid = new Rectangle(r.X + Ui.S(6), r.Y + Ui.S(6), r.Width - Ui.S(12), r.Height - Ui.S(12));
                for (int y = 0; y < rows; y++)
                    for (int x = 0; x < cols; x++)
                    {
                        int x0 = grid.X + grid.Width * x / cols, x1 = grid.X + grid.Width * (x + 1) / cols, y0 = grid.Y + grid.Height * y / rows, y1 = grid.Y + grid.Height * (y + 1) / rows;
                        using (var b = new SolidBrush(sel ? Ui.Accent : Ui.Mix(Ui.Muted, Ui.Card, 0.45))) g.FillRectangle(b, x0, y0, Math.Max(1, x1 - x0 - 1), Math.Max(1, y1 - y0 - 1));
                    }
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = -1; for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(e.Location)) h = i; if (h != hot) { hot = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = -1; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < Items.Length; i++) if (Cell(i).Contains(e.Location) && Items[i][0] != Value) { Value = Items[i][0]; Invalidate(); var h = Changed; if (h != null) h(); }
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // The page: layout picker, history on the left (wide windows), chat panes, "send to all" bar.
    // ---------------------------------------------------------------------------------------------------
    class AgentsPage : Panel
    {
        readonly List<ChatPane> panes = new List<ChatPane>();
        readonly TableLayoutPanel grid;
        readonly ChatHistory history;
        readonly LayoutPicker picker;
        readonly TextBox broadcast;
        readonly Label info;
        AgentLayout layout;
        ChatPane active;
        bool started;

        public AgentsPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            grid = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0, Ui.S(4), 0, 0) };
            history = new ChatHistory { Dock = DockStyle.Left, Width = Ui.S(260), Page = this };
            history.Open += OpenChat;
            history.IsOpen = c => panes.Any(p => p.Chat == c);

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Agents"));
            picker = new LayoutPicker();
            picker.Changed += delegate { layout.Layout = picker.Value; Arrange(); SaveLayout(); };
            bar.Controls.Add(picker);
            bar.Controls.Add(Ui.Btn("New chat", delegate { NewChatIn(active ?? panes.FirstOrDefault()); }, true));
            bar.Controls.Add(Ui.Btn("Chats", delegate { layout.HistoryOff = !layout.HistoryOff; SaveLayout(); PerformLayout(); }));
            bar.Controls.Add(Ui.Btn("Stop all", delegate { foreach (var p in panes) if (p.Busy) p.Stop(); }));
            info = new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(Ui.S(6), Ui.S(9), 0, 0) };
            bar.Controls.Add(info);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(46), Padding = new Padding(0, Ui.S(8), 0, Ui.S(4)) };
            broadcast = new FluentTextBox { Dock = DockStyle.Fill };
            broadcast.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Broadcast(); } };
            EventHandler cue = delegate { try { Native.SendMessage(broadcast.Handle, 0x1501, (IntPtr)1, "Send one message to every pane at once (each pane's agents answer in their own chat)"); } catch { } };
            if (broadcast.IsHandleCreated) cue(broadcast, EventArgs.Empty); else broadcast.HandleCreated += cue;
            var sendAll = Ui.Btn("Send to all panes", delegate { Broadcast(); });
            sendAll.Dock = DockStyle.Right; sendAll.Width = Ui.S(160);
            bottom.Controls.Add(broadcast);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Ui.S(8) });
            bottom.Controls.Add(sendAll);

            Controls.Add(grid);
            Controls.Add(history);
            Controls.Add(bottom);
            Controls.Add(bar);
            Ui.Theme(this);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (history != null && layout != null)
            {
                bool show = !layout.HistoryOff && ClientSize.Width >= Ui.S(1000);
                if (history.Visible != show) history.Visible = show;
                int w = ClientSize.Width >= Ui.S(1700) ? Ui.S(290) : Ui.S(250);
                if (history.Width != w) history.Width = w;
            }
            base.OnLayout(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !started)
            {
                started = true;
                layout = AgentStore.LoadLayout();
                picker.Value = LayoutPicker.Items.Any(i => i[0] == layout.Layout) ? layout.Layout : "2";
                picker.Invalidate();
                Arrange();
                AgentStore.LoadAll(() =>
                {
                    // Re-open the chats the panes showed last time.
                    for (int i = 0; i < panes.Count && i < layout.Panes.Count; i++)
                    {
                        var c = AgentStore.Find(layout.Panes[i]);
                        if (c != null && panes[i].Chat.Messages.Count == 0 && !panes[i].Busy) panes[i].SetChat(c);
                    }
                    UpdateInfo();
                });
                PerformLayout();
            }
        }

        AgentChat Blank(int index)
        {
            string folder = "";
            var used = panes.Select(p => p.Chat != null ? p.Chat.Folder : "").Where(f => f.Length > 0).ToList();
            var recent = MetaStore.Recent("folders", 1).FirstOrDefault();
            if (used.Count > 0) folder = used[0]; else if (recent != null && Directory.Exists(recent.Id)) folder = recent.Id;
            else folder = Store.Settings.TermRecent.FirstOrDefault(Directory.Exists) ?? "";
            // Different default agents per pane, so a 2-4 pane layout starts as a side-by-side comparison.
            var installed = AgentStore.Defs.Where(d => { string f, p; return AgentRun.Resolve(d.Exe, out f, out p); }).Select(d => d.Id).ToList();
            if (installed.Count == 0) installed = AgentStore.Defs.Select(d => d.Id).ToList();
            var c = new AgentChat { Id = DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0, 6), Created = Store.Now(), Updated = Store.Now(), Folder = folder };
            c.Agents.Add(installed[index % installed.Count]);
            return c;
        }

        void Arrange()
        {
            string l = layout.Layout;
            int count = LayoutPicker.Count(l);
            while (panes.Count < count)
            {
                var p = new ChatPane(Blank(panes.Count)) { Margin = new Padding(Ui.S(4)) };
                p.Activated += SetActive;
                p.Changed += delegate { UpdateInfo(); history.Invalidate(true); SaveLayout(); };
                panes.Add(p);
            }
            Ui.Batch(grid, () =>
            {
                grid.Controls.Clear(); grid.ColumnStyles.Clear(); grid.RowStyles.Clear();
                int cols = l == "2x2" ? 2 : l == "3x2" ? 3 : count, rowsN = l == "2x2" || l == "3x2" ? 2 : 1;
                grid.ColumnCount = cols; grid.RowCount = rowsN;
                for (int c = 0; c < cols; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));
                for (int r = 0; r < rowsN; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rowsN));
                for (int i = 0; i < count; i++) { panes[i].Dock = DockStyle.Fill; grid.Controls.Add(panes[i], i % cols, i / cols); }
            });
            // Panes beyond the layout keep running in the background; they come back when the layout grows.
            if (active == null || panes.IndexOf(active) >= count) SetActive(panes[0]);
            UpdateInfo();
        }

        void SetActive(ChatPane p) { active = p; foreach (var x in panes) x.IsActive = x == p; }

        int Visible_ { get { return LayoutPicker.Count(layout.Layout); } }

        void SaveLayout()
        {
            if (layout == null) return;
            layout.Panes = panes.Select(p => p.Chat != null && p.Chat.Messages.Count > 0 ? p.Chat.Id : "").ToList();
            AgentStore.SaveLayout(layout);
        }

        void UpdateInfo()
        {
            int busy = panes.Take(Visible_).Count(p => p.Busy);
            int chats = AgentStore.Chats.Count(c => c.Messages.Count > 0);
            info.Text = (busy > 0 ? busy + " working  ·  " : "") + chats + " saved chats in " + AgentStore.Dir;
        }

        public void NewChatIn(ChatPane p)
        {
            if (p == null) return;
            if (p.Busy && MessageBox.Show(FindForm(), "This pane is working. Stop it and start a new chat?", App.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            var old = p.Chat;
            var c = Blank(panes.IndexOf(p));
            if (old != null) { c.Agents.Clear(); c.Agents.AddRange(old.Agents); if (old.Folder.Length > 0) c.Folder = old.Folder; c.Mode = old.Mode; }
            p.SetChat(c); p.FocusInput(); SaveLayout();
        }

        void OpenChat(AgentChat c)
        {
            var already = panes.Take(Visible_).FirstOrDefault(p => p.Chat == c);
            if (already != null) { SetActive(already); already.FocusInput(); return; }
            // Prefer an empty pane, then the active one if it is idle.
            var target = panes.Take(Visible_).FirstOrDefault(p => p.Chat.Messages.Count == 0 && !p.Busy) ?? (active != null && !active.Busy ? active : panes.Take(Visible_).FirstOrDefault(p => !p.Busy));
            if (target == null) { MessageBox.Show(FindForm(), "Every pane is working. Wait, stop one, or pick a bigger layout.", App.Name); return; }
            target.SetChat(c); SetActive(target); target.FocusInput(); SaveLayout();
        }

        void Broadcast()
        {
            string text = broadcast.Text.Trim(); if (text.Length == 0) return;
            int sent = 0;
            foreach (var p in panes.Take(Visible_)) if (!p.Busy) { p.Send(text); sent++; }
            broadcast.Clear();
            App.I.Status("Sent to " + sent + " pane(s).");
        }

        public void Export(AgentChat c)
        {
            using (var d = new SaveFileDialog { Filter = "Markdown (*.md)|*.md|Text (*.txt)|*.txt", FileName = MakeName(c.Title.Length > 0 ? c.Title : "chat") + ".md" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                try { File.WriteAllText(d.FileName, AgentStore.Markdown(c), new UTF8Encoding(false)); } catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, App.Name); }
            }
        }
        static string MakeName(string s) { foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_'); return s.Length > 60 ? s.Substring(0, 60) : s; }

        // From other pages ("Agents here"): open a new chat in this folder.
        public void UseFolder(string folder)
        {
            if (!started) OnVisibleChanged(EventArgs.Empty);
            var p = panes.Take(Visible_).FirstOrDefault(x => x.Chat.Messages.Count == 0 && !x.Busy) ?? active ?? panes[0];
            if (p.Busy) { p = panes.Take(Visible_).FirstOrDefault(x => !x.Busy); if (p == null) return; }
            if (p.Chat.Messages.Count > 0) NewChatIn(p);
            p.Chat.Folder = folder; MetaStore.Touch("folders", folder);
            p.SetChat(p.Chat); SetActive(p); p.FocusInput();
        }
    }
}
