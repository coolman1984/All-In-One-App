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
        // Model and effort: "--model {model}" style templates, the choices, and the CLI command that lists models live.
        public string ModelArg = "", EffortArg = "", ModelsCmd = "";
        public List<string> Models = new List<string>(), Efforts = new List<string>();
    }

    public class AgentMsg { public string Role = "", Agent = "", Text = "", Label = "", Model = ""; public long Time; public List<string> Files; }   // Model: model, effort and account that wrote an answer   // Files: attachments of a user message   // Label: who spoke, when not just the agent (council members)

    public class AgentChat
    {
        public string Id = "", Title = "", Folder = "", Mode = "edit";
        public List<string> Agents = new List<string>();
        public Dictionary<string, string> Sessions = new Dictionary<string, string>();
        public Dictionary<string, int> Seen = new Dictionary<string, int>();
        public Dictionary<string, string> Accounts = new Dictionary<string, string>();
        public Dictionary<string, string> Models = new Dictionary<string, string>(), Efforts = new Dictionary<string, string>();   // per agent; empty = CLI default
        public List<string> Off = new List<string>();   // agents of the chat that do not answer now
        public bool Separate;   // each agent answers only your message (does not see the other agents' answers)
        public bool Council, LeaderTeam; public int Rounds = 1; public string Chair = "";   // model council mode
        public bool Web;   // agents search the web (and GitHub) before answering
        public int BranchPrefix;
        internal string PendingInput = "";
        internal bool Persisted;
        public string Collection = "";
        public bool Archived;
        internal bool HasContent { get { return Messages.Count > 0 || !string.IsNullOrEmpty(PendingInput) || Fav || Archived || !string.IsNullOrEmpty(Collection) || !string.IsNullOrEmpty(Title); } }
        public List<CouncilMember> Members = new List<CouncilMember>();
        public long Created, Updated;
        public bool Fav;
        public List<AgentMsg> Messages = new List<AgentMsg>();
    }

    public class AgentLayout
    {
        public string Layout = "2"; public List<string> Panes = new List<string>(); public bool HistoryOff;
        public List<float> ColW = new List<float>(), RowH = new List<float>();   // pane sizes you dragged (percent)
        public List<AgentWorkspace> Saved = new List<AgentWorkspace>();           // saved arrangements
    }

    static class AgentStore
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        public static string Dir { get { return Path.Combine(Store.Dir, "agents"); } }
        static string ChatsDir { get { return Path.Combine(Dir, "chats"); } }
        public static string DefsFile { get { return Path.Combine(Dir, "agents.json"); } }
        static readonly object writeLock = new object();
        static readonly object fileLock = new object();
        static readonly HashSet<string> recoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    foreach (var d in defs) AgentModels.Fill(d);
                    try { Directory.CreateDirectory(Dir); File.WriteAllText(DefsFile, Pretty(Js.Serialize(defs)), new UTF8Encoding(false)); } catch { }
                }
                // Older agents.json files have no model / effort settings: add the built-in ones.
                foreach (var d in defs) { if (d.Models == null) d.Models = new List<string>(); if (d.Efforts == null) d.Efforts = new List<string>(); AgentModels.Fill(d); }
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

        static bool loading;
        static readonly List<Action> afterLoad = new List<Action>();
        static string ChatDirFor(AgentChat c) { return AssistantHarness.IsAssistant(c) ? AssistantHarness.ChatsDir : ChatsDir; }
        static string ChatFile(AgentChat c) { return Path.Combine(ChatDirFor(c), c.Id + ".json"); }

        public static void LoadAll(Action done)
        {
            if (Loaded) { if (done != null) done(); return; }
            if (done != null) afterLoad.Add(done);
            if (loading) return;
            loading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var byId = new Dictionary<string, AgentChat>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string dir in new[] { ChatsDir, AssistantHarness.ChatsDir })
                    {
                        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                        foreach (var f in Directory.GetFiles(dir, "*.json*")
                            .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
                            .Select(p => p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? p.Substring(0, p.Length - 4) : p)
                            .Distinct(StringComparer.OrdinalIgnoreCase))
                        {
                            var c = ReadChat(f);
                            if (c != null) byId[c.Id] = c;
                        }
                    }
                }
                catch (Exception ex) { App.Log("Agent chats: " + ex.Message); }
                var list = byId.Values.ToList();
                Action finish = () =>
                {
                    // Keep chats created before loading finished.
                    var live = new HashSet<string>(Chats.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
                    list.RemoveAll(c => live.Contains(c.Id)); list.AddRange(Chats);
                    Chats = list; Loaded = true; loading = false;
                    var callbacks = afterLoad.ToArray(); afterLoad.Clear();
                    foreach (var callback in callbacks) callback();
                    Raise();
                };
                try
                {
                    if (App.I != null && App.I.Sync != null && !App.I.Sync.IsDisposed) App.I.Sync.BeginInvoke(finish);
                    else finish();
                }
                catch { loading = false; }
            });
        }
        internal static AgentChat ReadChat(string file)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
            foreach (var path in new[] { file, file + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var c = js.Deserialize<AgentChat>(File.ReadAllText(path, Encoding.UTF8));
                    if (c == null) continue;
                    c.Id = Path.GetFileNameWithoutExtension(file);
                    Normalize(c); c.Persisted = true;
                    if (path != file) lock (fileLock) recoveredFiles.Add(file);
                    foreach (var draft in new[] { file + ".draft", file + ".draft.bak" })
                    {
                        if (!File.Exists(draft)) continue;
                        try {
                            c.PendingInput = js.Deserialize<string>(File.ReadAllText(draft, Encoding.UTF8)) ?? "";
                            if (draft.EndsWith(".bak")) lock (fileLock) recoveredFiles.Add(file + ".draft");
                            break;
                        }
                        catch (Exception ex) { App.Log("Read chat draft: " + ex.Message); }
                    }
                    return c;
                }
                catch (Exception ex) { App.Log("Read chat " + Path.GetFileName(path) + ": " + ex.Message); }
            }
            return null;
        }

        static readonly Dictionary<string, string> drafts = new Dictionary<string, string>();
        static readonly System.Threading.Timer draftTimer = new System.Threading.Timer(o => FlushDrafts(), null, -1, -1);
        public static void SetDraft(AgentChat c, string value)
        {
            if (c == null || c.PendingInput == value) return;
            bool wasVisible = c.HasContent;
            c.PendingInput = value;
            if (!c.Persisted && c.HasContent) Save(c);
            else if (wasVisible != c.HasContent) Raise();
            lock (writeLock)
            {
                if (drafts.Count == 0) draftTimer.Change(450, -1);
                drafts[ChatFile(c) + ".draft"] = value;
            }
        }
        static void FlushDrafts()
        {
            lock (writeLock)
            {
                foreach (var entry in drafts) QueueWrite(entry.Key, entry.Value);
                drafts.Clear();
            }
        }

        static void Normalize(AgentChat c)
        {
            if (c.Agents == null) c.Agents = new List<string>();
            if (c.Sessions == null) c.Sessions = new Dictionary<string, string>();
            if (c.Seen == null) c.Seen = new Dictionary<string, int>();
            if (c.Accounts == null) c.Accounts = new Dictionary<string, string>();
            if (c.Models == null) c.Models = new Dictionary<string, string>();
            if (c.Efforts == null) c.Efforts = new Dictionary<string, string>();
            if (c.Off == null) c.Off = new List<string>();
            string cm;
            if (c.Models != null && c.Models.TryGetValue("cline", out cm) && (cm == "cline-pass: stealth/ox-alpha" || cm == "cline-pass: z-ai/glm-5.3-flash")) c.Models.Remove("cline");
            if (c.Members == null) c.Members = new List<CouncilMember>();
            c.Members.RemoveAll(m => m == null || string.IsNullOrEmpty(m.Agent));
            foreach (var m in c.Members) { if (m.Model == null) m.Model = ""; if (m.Effort == null) m.Effort = ""; if (m.Role == null) m.Role = ""; if (string.IsNullOrEmpty(m.Key)) m.Key = Guid.NewGuid().ToString("N").Substring(0, 8); }
            if (c.Messages == null) c.Messages = new List<AgentMsg>();
            c.Agents.RemoveAll(a => string.IsNullOrWhiteSpace(a));
            c.Messages.RemoveAll(m => m == null);
            foreach (var message in c.Messages)
            {
                if (message.Text == null) message.Text = "";
                if (message.Role == null) message.Role = "";
                if (message.Agent == null) message.Agent = "";
                if (message.Label == null) message.Label = ""; if (message.Model == null) message.Model = "";
                if (message.Time < 0 || message.Time > DateTime.MaxValue.Ticks) message.Time = 0;
            }
            c.Created = Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, c.Created));
            c.Updated = Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, c.Updated));
            if (c.Title == null) c.Title = ""; if (c.Folder == null) c.Folder = ""; if (string.IsNullOrEmpty(c.Mode)) c.Mode = "edit";
        }
        public static void Raise() { var h = Changed; if (h != null) h(); }
        // Chats being answered right now (Agents page or MCP agent_ask), and a chat changed outside its pane.
        public static readonly HashSet<string> Busy = new HashSet<string>();
        public static event Action<AgentChat> ChatUpdated;
        public static void RaiseChat(AgentChat c) { var h = ChatUpdated; if (h != null) h(c); }

        // What an agent has not seen yet: the user's new message, plus what the other agents answered since its last turn.
        public static string PromptFor(AgentChat c, string agentId)
        {
            string prompt = DocumentPrompt(c, ConversationPrompt(c, agentId));
            string sharedHint = AssistantHarness.SharedHarnessHint(c, agentId);
            if (!string.IsNullOrEmpty(sharedHint) && prompt.IndexOf("session_search", StringComparison.OrdinalIgnoreCase) < 0)
                prompt += "\n\n" + sharedHint;
            return prompt;
        }
        internal static string DocumentPrompt(AgentChat c, string prompt)
        {
            var latest = c.Messages.LastOrDefault(m => m.Role == "user");
            // Only an explicit request for a file ("create an Excel workbook", "export as pdf") switches the reply to a
            // file card. Before, any mention matched ("summarize this pdf", an inserted "notes.md"), so normal answers
            // were hidden inside a "Save" card.
            if (latest != null && latest.Text.IndexOf("file:filename.", StringComparison.OrdinalIgnoreCase) < 0 &&
                System.Text.RegularExpressions.Regex.IsMatch(latest.Text,
                    @"\b(create|make|generate|export|save|build|produce|prepare|convert|turn)\b[^\r\n.?!]{0,60}?(\b(xlsx|docx|pptx|pdf|csv|txt|md|excel|powerpoint|spreadsheet|workbook|slides?|deck)\b|\b(word|markdown|text)\s+(document|file))",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                prompt += "\n\nWhen delivering a requested document, put the complete Markdown content inside a fenced block starting with ```file:filename.ext using a descriptive filename and the requested extension (xlsx, docx, pptx, pdf, csv, md or txt). Use Markdown tables for spreadsheets and one heading per slide for presentations. If the file contains code fences, use a longer outer backtick fence.";
            return prompt;
        }
        static string ConversationPrompt(AgentChat c, string agentId)
        {
            string session = AgentAccounts.Session(agentId, AgentAccounts.Selected(c, agentId));
            int seen; c.Seen.TryGetValue(session, out seen);
            int prefix = Math.Min(c.Messages.Count, Math.Max(0, c.BranchPrefix));
            seen = Math.Max(prefix, seen);
            var news = c.Messages.Skip(Math.Min(seen, c.Messages.Count)).Where(m => (m.Role == "user" || m.Role == "agent") && !(m.Role == "agent" && m.Agent == agentId)).ToList();
            if (c.Separate) news = news.Where(m => m.Role == "user").ToList();   // answers on its own
            // Agents that cannot resume a session (Cline): give them the recent conversation so they keep the thread.
            var def = Def(agentId);
            string earlier = def != null && string.IsNullOrEmpty(def.Resume) && seen > 0 ? History(c, agentId, Math.Min(seen, c.Messages.Count)) :
                prefix > 0 && !c.Sessions.ContainsKey(session) ? History(c, agentId, seen) : "";
            if (earlier.Length > 0) return earlier + "New since your last turn:\n\n" + string.Join("\n\n", news.Select(m => "[" + (m.Role == "user" ? "User" : NameOf(m.Agent)) + "]\n" + m.Text)) + "\n\nAnswer the user's latest message.";
            if (news.Count == 1 && news[0].Role == "user") return news[0].Text;
            if (c.Separate) return string.Join("\n\n", news.Select(m => m.Text));
            var sb = new StringBuilder();
            sb.Append("You are in a group chat with the user and other coding agents (" + string.Join(", ", c.Agents.Where(a => a != agentId).Select(NameOf)) + "), all working in this folder. New messages since your last turn:\n\n");
            foreach (var m in news)
            {
                string t = m.Role == "agent" && m.Text.Length > 12000 ? m.Text.Substring(0, 12000) + "\n[...]" : m.Text;   // your own text is never cut
                sb.Append("[").Append(m.Role == "user" ? "User" : NameOf(m.Agent)).Append("]\n").Append(t).Append("\n\n");
            }
            sb.Append("Answer the user's latest message. Build on the other agents' answers instead of repeating them.");
            return sb.ToString();
        }

        // The last messages before "upTo" (yours and the agents', this agent's own as "You"), at most about 12000 characters.
        internal static string History(AgentChat c, string agentId, int upTo)
        {
            var parts = new List<string>(); int size = 0;
            for (int i = Math.Min(upTo, c.Messages.Count) - 1; i >= 0 && parts.Count < 12; i--)
            {
                var m = c.Messages[i];
                if (m.Role != "user" && m.Role != "agent") continue;
                if (c.Separate && m.Role == "agent" && m.Agent != agentId) continue;
                string who = m.Role == "user" ? "User" : m.Agent == agentId && m.Label.Length == 0 ? "You" : (m.Label.Length > 0 ? m.Label : NameOf(m.Agent));
                string t = m.Text.Length > 3000 ? m.Text.Substring(0, 3000) + " [...]" : m.Text;
                if (size + t.Length > 12000 && parts.Count > 0) break;
                parts.Insert(0, "[" + who + "]\n" + t); size += t.Length;
            }
            return parts.Count == 0 ? "" : "Earlier in this chat (for context):\n\n" + string.Join("\n\n", parts) + "\n\n";
        }

        public static AgentChat New(string folder, IEnumerable<string> agents)
        {
            var c = new AgentChat { Id = DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0, 6), Created = Store.Now(), Updated = Store.Now(), Folder = folder ?? "" };
            c.Agents.AddRange(agents);
            Chats.Add(c);
            return c;
        }
        public static AgentChat Find(string id) { return Chats.FirstOrDefault(c => c.Id == id); }

        public static AgentChat CopyThrough(AgentChat source, int count)
        {
            var c = new AgentChat { Id = Guid.NewGuid().ToString("N"), Created = Store.Now(), Updated = Store.Now(),
                Folder = source.Folder, Collection = source.Collection, Mode = source.Mode, Separate = source.Separate, Council = source.Council,
                Rounds = source.Rounds, Chair = source.Chair, Web = source.Web, LeaderTeam = source.LeaderTeam };
            c.Agents.AddRange(source.Agents); c.Off.AddRange(source.Off);
            c.Accounts = new Dictionary<string, string>(source.Accounts);
            c.Models = new Dictionary<string, string>(source.Models); c.Efforts = new Dictionary<string, string>(source.Efforts);
            c.Members = source.Members.Select(x => new CouncilMember { Agent = x.Agent, Model = x.Model, Effort = x.Effort, Role = x.Role, Off = x.Off, Key = x.Key, Account = x.Account }).ToList();
            c.Messages = source.Messages.Take(count).Select(x => new AgentMsg { Role = x.Role, Agent = x.Agent, Label = x.Label, Model = x.Model, Text = x.Text, Time = x.Time, Files = x.Files == null ? null : x.Files.ToList() }).ToList();
            c.BranchPrefix = c.Messages.Count;
            c.Title = count > 0 ? "Branch · " + source.Title : source.Title.Length > 0 ? source.Title + " (copy)" : "";
            return c;
        }

        // Snapshot on the UI thread, write on a worker (temp file + replace, so a crash never leaves half a file).
        public static void Save(AgentChat c)
        {
            if (c == null || (!c.HasContent && !c.Persisted)) return;
            if (!c.Persisted && !Chats.Contains(c)) Chats.Add(c);
            c.Persisted = true;
            object snapshot; try { snapshot = DataSnapshot.Copy(c); } catch (Exception ex) { App.Log("Agent save: " + ex.Message); return; }
            string file = ChatFile(c);
            QueueWrite(file, snapshot);
            SqliteVault.IndexChatAsync(c);
            Raise();
        }
        static readonly Dictionary<string, object> pending = new Dictionary<string, object>();
        static readonly Dictionary<string, object> failed = new Dictionary<string, object>();
        static bool writing;
        static readonly HashSet<string> deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static void QueueWrite(string file, object snapshot)
        {
            lock (writeLock)
            {
                deleted.Remove(file);
                pending[file] = snapshot;
                if (writing) return;
                writing = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    string path; object value;
                    lock (writeLock)
                    {
                        if (pending.Count == 0) { writing = false; deleted.Clear(); Monitor.PulseAll(writeLock); return; }
                        var next = pending.First(); path = next.Key; value = next.Value; pending.Remove(path);
                    }
                    bool ok = false;
                    try { ok = WriteFile(path, new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 }.Serialize(value)); }
                    catch (Exception ex) { App.Log("Agent save: " + ex.Message); }
                    lock (writeLock) { if (ok || deleted.Contains(path)) failed.Remove(path); else failed[path] = value; }
                }
            });
        }
        public static bool FlushWrites()
        {
            FlushDrafts();
            List<KeyValuePair<string, object>> retry;
            lock (writeLock)
            {
                var wait = Stopwatch.StartNew();
                while (writing && wait.ElapsedMilliseconds < 10000) Monitor.Wait(writeLock, 100);
                if (writing) return false;
                retry = failed.ToList();
            }
            foreach (var entry in retry)
            {
                try
                {
                    if (WriteFile(entry.Key, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(entry.Value)))
                        lock (writeLock) failed.Remove(entry.Key);
                }
                catch (Exception ex) { App.Log("Agent retry: " + ex.Message); }
            }
            lock (writeLock) return failed.Count == 0;
        }

        static bool WriteFile(string file, string text)
        {
            lock (fileLock)
            {
                try
                {
                    lock (writeLock) if (deleted.Contains(file)) return true;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    string tmp = file + ".tmp";
                    File.WriteAllText(tmp, text, new UTF8Encoding(false));
                    for (int attempt = 0; ; attempt++)
                    {
                        try { if (File.Exists(file)) File.Replace(tmp, file, recoveredFiles.Contains(file) ? null : file + ".bak", true); else File.Move(tmp, file); break; }
                        catch (IOException) { if (attempt >= 4) throw; Thread.Sleep(120 * (attempt + 1)); }
                        catch (UnauthorizedAccessException) { if (attempt >= 4) throw; Thread.Sleep(120 * (attempt + 1)); }
                    }
                    recoveredFiles.Remove(file);
                    return true;
                }
                catch (Exception ex) { App.Log("Agent write " + Path.GetFileName(file) + ": " + ex.Message); return false; }
            }
        }
        public static void Delete(AgentChat c)
        {
            Chats.Remove(c);
            var paths = new[] { Path.Combine(ChatsDir, c.Id + ".json"), Path.Combine(AssistantHarness.ChatsDir, c.Id + ".json") }
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            lock (fileLock)
            {
                foreach (string path in paths)
                {
                    lock (writeLock) { drafts.Remove(path + ".draft"); }
                    foreach (string target in new[] { path, path + ".draft" })
                    {
                        lock (writeLock) { if (writing) deleted.Add(target); pending.Remove(target); failed.Remove(target); }
                        recoveredFiles.Remove(target);
                        try { File.Delete(target); File.Delete(target + ".bak"); }
                        catch (Exception ex) { App.Log("Delete chat: " + ex.Message); }
                    }
                }
            }
            Raise();
        }

        public static AgentLayout LoadLayout()
        {
            string file = Path.Combine(Dir, "layout.json");
            foreach (string candidate in new[] { file, file + ".bak" })
            {
                try {
                    if (!File.Exists(candidate)) continue;
                    var l = Js.Deserialize<AgentLayout>(File.ReadAllText(candidate, Encoding.UTF8));
                    if (l == null) continue;
                    if (l.Panes == null) l.Panes = new List<string>();
                    if (l.Saved == null) l.Saved = new List<AgentWorkspace>();
                    if (!LayoutPicker.Items.Any(x => x[0] == l.Layout)) l.Layout = "2";
                    if (candidate != file) lock (fileLock) recoveredFiles.Add(file);
                    return l;
                } catch (Exception ex) { App.Log("Read chat layout: " + ex.Message); }
            }
            return new AgentLayout();
        }
        public static void SaveLayout(AgentLayout l)
        {
            object snapshot = DataSnapshot.Copy(l); string file = Path.Combine(Dir, "layout.json");
            QueueWrite(file, snapshot);
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
    // How full an agent's context is after its latest model call (Window 0 = not reported yet).
    class AgentUsage { public string Agent = "", Account = ""; public long Used, Window; }

    class AgentRun
    {
        public event Action<string, string> Event;   // kind (text, tool, error, session, info), payload
        public event Action<int> Exited;
        public event Action<AgentUsage> Usage;        // context readings; a separate event so transcripts never show them
        public List<string> Attach = new List<string>();   // files of this message (images go to Codex as --image)
        public string AgentId { get; private set; }
        public string Account { get; private set; }
        // Claude Code with --input-format stream-json keeps stdin open while it works, so a message written now
        // reaches it after the current tool calls ("steer"). Input closes at the first result; the CLI still answers
        // what it already received, then exits.
        System.Collections.Concurrent.BlockingCollection<string> writer;
        bool streamInput;
        long lastUsed;
        public bool CanSteer { get { var w = writer; return streamInput && w != null && !w.IsAddingCompleted && !stopped && Running; } }
        public bool Steer(string text)
        {
            var w = writer;
            if (!CanSteer || string.IsNullOrWhiteSpace(text)) return false;
            try { return w.TryAdd(UserLine(text)); } catch (InvalidOperationException) { return false; }
        }
        void CloseInput() { var w = writer; if (w != null) try { w.CompleteAdding(); } catch { } }
        static string UserLine(string text)
        {
            var message = new Dictionary<string, object> { { "role", "user" }, { "content", text ?? "" } };
            return Js.Serialize(new Dictionary<string, object> { { "type", "user" }, { "message", message }, { "parent_tool_use_id", null } }) + "\n";
        }
        public static bool IsImage(string path)
        {
            string e = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".gif" || e == ".webp" || e == ".bmp";
        }
        void ReportUsage(long used, long window)
        {
            if (used > 0) lastUsed = used;
            var h = Usage; if (h != null && lastUsed > 0) h(new AgentUsage { Agent = AgentId ?? "", Account = Account ?? "", Used = lastUsed, Window = window });
        }
        // Claude's rate_limit_event: rate_limit_info.unifiedWindows.five_hour / seven_day (utilization 0..1, resetsAt epoch).
        void ClaudeLimits(Dictionary<string, object> o)
        {
            var info = D(o, "rate_limit_info");
            if (info == null) return;
            var row = new LimitRow { Agent = "Claude Code", Account = string.IsNullOrEmpty(Account) ? "main" : Account, Observed = DateTime.Now };
            var windows = D(info, "unifiedWindows");
            if (windows != null) { LimitWindow(D(windows, "five_hour"), out row.FiveHour, out row.FiveResets); LimitWindow(D(windows, "seven_day"), out row.Weekly, out row.WeekResets); }
            else
            {
                double? used; DateTime? at; LimitWindow(info, out used, out at);
                string kind = S(info, "rateLimitType") ?? "";
                if (kind == "five_hour") { row.FiveHour = used; row.FiveResets = at; }
                else if (kind.StartsWith("seven_day")) { row.Weekly = used; row.WeekResets = at; }
            }
            AgentLimits.Report(row);
        }
        static void LimitWindow(Dictionary<string, object> w, out double? used, out DateTime? resets)
        {
            used = null; resets = null;
            if (w == null) return;
            double u, at;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (double.TryParse(S(w, "utilization") ?? "", System.Globalization.NumberStyles.Float, inv, out u)) used = Math.Round(u <= 1.0001 ? u * 100 : u, 1);
            if (double.TryParse(S(w, "resetsAt") ?? "", System.Globalization.NumberStyles.Float, inv, out at) && at > 0)
                resets = DateTimeOffset.FromUnixTimeMilliseconds((long)(at > 1e11 ? at : at * 1000)).LocalDateTime;
            if (resets != null && resets <= DateTime.Now) used = 0;
        }
        public Action<string> PlainLine;
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

        public string ModelOverride, EffortOverride, SessionKey, AccountOverride;
        public string UsedLabel = "";
        public string ActiveSessionKey { get; private set; }
        public const string WebNote = "[Search the web for current information before you answer, and GitHub when code, libraries or repositories are involved. List the links you used at the end.]\n\n";
        public bool Start(AgentDef d, AgentChat chat, string prompt, out string error)
        {
            error = null;
            string file, prefix;
            if (!Resolve(d.Exe, out file, out prefix)) { error = d.Name + " is not installed (\"" + d.Exe + "\" was not found on PATH). Install it or fix agents.json."; return false; }
            string mode = chat.Mode == "ask" ? d.ModeAsk : chat.Mode == "auto" ? d.ModeAuto : d.ModeEdit;
            string account = AccountOverride ?? AgentAccounts.Selected(chat, d.Id);
            ActiveSessionKey = AgentAccounts.Session(SessionKey ?? d.Id, account);
            string session; chat.Sessions.TryGetValue(ActiveSessionKey, out session);
            string resume = string.IsNullOrEmpty(session) || string.IsNullOrEmpty(d.Resume) ? "" : d.Resume.Replace("{session}", Quote(session));
            if (d.Id == "codex" && Store.Settings.CodexManaged)
            {
                // The company policy (enterprise requirements) allows only sandbox read-only / workspace-write and
                // approval on-request; asking for more only prints an error and falls back anyway.
                if ((mode ?? "").Contains("--dangerously-bypass-approvals-and-sandbox") || (mode ?? "").Contains("danger-full-access")) mode = "-s workspace-write";
                mode = (mode ?? "") + " -c approval_policy=on-request";
            }
            string args = d.Args.Replace("{mode}", mode ?? "").Replace("{resume}", resume).Replace("{folder}", Quote(chat.Folder));
            // Arguments that carry the prompt: cmd.exe would mangle & | % ^, so only real programs get it inline.
            if (!d.Stdin && prompt.Length > 6000)
            {
                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "AllInOne-agents"); Directory.CreateDirectory(dir);
                    string msgFile = Path.Combine(dir, "message-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".md");
                    File.WriteAllText(msgFile, prompt, new UTF8Encoding(false));
                    prompt = "My full message is long, so it is saved in this file: " + msgFile + "\nRead the whole file first, then answer it as my message.";
                }
                catch (Exception ex) { App.Log("Long message file: " + ex.Message); }
            }
            if (chat.Web && d.Id == "claude" && !args.Contains("--allowedTools")) args += " --allowedTools WebSearch WebFetch";   // headless Claude may search
            // The Assistant (collection "Assistant") works the app itself via AssistantHarness.
            if (AssistantHarness.IsAssistant(chat))
                args = AssistantHarness.ApplyMcpArgs(d, chat, args);
            string clineModel = null;
            if (d.Id == "cline")
            {
                string selected = ModelOverride ?? AgentModels.ChatModel(chat, d.Id);
                if (string.IsNullOrWhiteSpace(selected)) selected = AgentModels.DefaultModel(d);
                clineModel = AgentModels.CanonicalClineModel(selected);
                if (!string.IsNullOrWhiteSpace(selected) && clineModel == null)
                {
                    error = "Choose a Cline model in provider: model format.";
                    return false;
                }
            }
            args = AgentModels.Inject(args, ModelOverride != null ? AgentModels.ArgsFor(d, ModelOverride, EffortOverride) : AgentModels.Args(d, chat));   // chosen model and effort
            if (d.Id == "codex" && account.Length > 0) args = "-c cli_auth_credentials_store=file " + args;
            AgentId = d.Id; Account = account;
            // What actually answers: shown in the answer header ("claude-opus-5-5 · high · work").
            {
                string um = ModelOverride, ue = EffortOverride;
                if (um == null) { if (chat.Models != null) chat.Models.TryGetValue(d.Id, out um); if (chat.Efforts != null) chat.Efforts.TryGetValue(d.Id, out ue); }
                if (string.IsNullOrWhiteSpace(um)) um = AgentModels.DefaultModel(d);
                if (string.IsNullOrWhiteSpace(ue)) ue = AgentModels.DefaultEffort(d);
                var parts = new List<string>();
                parts.Add(string.IsNullOrWhiteSpace(um) ? "default model" : um.Trim());
                if (!string.IsNullOrWhiteSpace(ue) && AgentModels.HasEffort(d)) parts.Add(ue.Trim());
                if (!string.IsNullOrEmpty(account)) parts.Add(account);
                UsedLabel = string.Join(" · ", parts);
            }
            var files = (Attach ?? new List<string>()).Where(File.Exists).ToList();
            if (d.Format == "claude" && files.Count > 0)   // headless Claude may read the attachments (outside the project folder)
                foreach (var folder in files.Select(Path.GetDirectoryName).Distinct(StringComparer.OrdinalIgnoreCase)) args += " --add-dir " + Quote(folder);
            var images = files.Where(IsImage).ToList();
            if (d.Format == "codex" && images.Count > 0)   // real image input; "=" keeps the list from swallowing the "-" (stdin) argument
                args = Regex.Replace(args, @"(^|\s)exec(\s|$)", m => m.Value.TrimEnd() + " " + Quote("--image=" + string.Join(",", images)) + " ", RegexOptions.None, TimeSpan.FromSeconds(1));
            streamInput = d.Stdin && d.Format == "claude" && args.Contains("--output-format stream-json") && !args.Contains("--input-format");
            if (streamInput) args += " --input-format stream-json";
            args = Regex.Replace(args, "\\s{2,}", " ").Trim();
            bool viaCmd = file.EndsWith("cmd.exe", StringComparison.OrdinalIgnoreCase) && prefix.StartsWith("/d");
            if (viaCmd && !d.Stdin) { error = "This CLI needs an executable or a Node entry point to receive a message."; return false; }
            args = args.Replace("{prompt}", d.Stdin ? "" : Quote(prompt));
            var psi = new ProcessStartInfo(file, prefix + args + (viaCmd ? "\"" : ""))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                WorkingDirectory = Directory.Exists(chat.Folder) ? chat.Folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            psi.EnvironmentVariables["FORCE_COLOR"] = "0";
            if (psi.EnvironmentVariables.ContainsKey("CLAUDECODE")) psi.EnvironmentVariables.Remove("CLAUDECODE");
            if (clineModel == null) return StartProcess(psi, d, account, prompt, out error);

            AgentModels.LoadClineModels(d, false).ContinueWith(task =>
            {
                string reason;
                if (stopped) Emit("info", "Stopped.");
                else if (task.Result.Length > 0 && !task.Result.Contains(clineModel, StringComparer.Ordinal))
                    Emit("error", "Cline model is unavailable in the configured provider catalog: " + clineModel + ". Choose a model from the Model menu.");
                else if (!StartProcess(psi, d, account, prompt, out reason)) Emit("error", reason);
                else
                {
                    if (stopped) Stop();
                    return;
                }
                var exited = Exited; if (exited != null) exited(-1);
            }, System.Threading.Tasks.TaskScheduler.Default);
            return true;
        }

        bool StartProcess(ProcessStartInfo psi, AgentDef d, string account, string prompt, out string error)
        {
            error = null;
            try
            {
                AgentAccounts.Apply(psi, d.Id, account);
                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.Start();
                ConsoleGuard.Watch(proc);   // consoles its helpers open stay hidden
            }
            catch (Exception ex) { error = "Could not start " + d.Name + ": " + ex.Message; return false; }
            string format = d.Format ?? "text";
            var p = proc;
            if (streamInput)
            {
                var w = new System.Collections.Concurrent.BlockingCollection<string>();
                w.Add(UserLine(prompt));
                writer = w;
                new Thread(() =>
                {
                    try
                    {
                        var input = p.StandardInput.BaseStream; var utf8 = new UTF8Encoding(false);
                        foreach (var line in w.GetConsumingEnumerable()) { var bytes = utf8.GetBytes(line); input.Write(bytes, 0, bytes.Length); input.Flush(); }
                        input.Close();
                    }
                    catch { }
                }) { IsBackground = true, Name = "agent stdin" }.Start();
            }
            else ThreadPool.QueueUserWorkItem(delegate
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
                CloseInput();
                if (format == "codex" && !string.IsNullOrEmpty(sessionSeen))   // Codex writes context and limits to its session log
                    try { long used, window; if (AgentLimits.CodexContext(Account, sessionSeen, out used, out window)) ReportUsage(used, window); }
                    catch (Exception ex) { App.Log("Codex context: " + ex.Message); }
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
            CloseInput();
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

        bool policyNoted;
        void Emit(string kind, string text)
        {
            if (string.IsNullOrEmpty(text) && kind != "session") return;
            if (kind == "error" || kind == "info") text = Clean(text);
            // Codex under a company policy: say it once, plainly, and use the allowed settings from the next run on.
            if ((kind == "error" || kind == "info") && text.IndexOf("disallowed by requirements", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bool first = !Store.Settings.CodexManaged;
                if (first) { Store.Settings.CodexManaged = true; Store.MarkSettings(); }
                if (policyNoted) return;
                policyNoted = true;
                kind = "info";
                text = "Your company limits Codex to working inside the project folder and asking before risky steps, so \"Full auto\" runs as \"Edit\" here." + (first ? " The app uses these settings from now on." : "");
            }
            if (kind == "text") sawText = true;
            if (kind == "error") sawError = true;
            if (kind == "session") { if (text == sessionSeen || string.IsNullOrEmpty(text)) return; sessionSeen = text; }
            var h = Event; if (h != null) h(kind, text);
        }

        // Error text with a whole web page in it (a company web gateway / proxy answering instead of the AI service):
        // keep one readable line - status, the page title and the address - instead of the HTML.
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text) || (text.IndexOf("<html", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) < 0)) return text;
            string head = text.Substring(0, Math.Max(0, Math.Min(text.Length, text.IndexOf('<') > 0 ? text.IndexOf('<') : 0))).Trim().TrimEnd(':');
            var title = Regex.Match(text, @"<title>\s*(.*?)\s*</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var url = Regex.Match(text, @"url:\s*(\S+)");
            var status = Regex.Match(text, @"status (\d{3})[^:<]*");
            string why = title.Success ? title.Groups[1].Value : "a web page";
            bool gateway = Regex.IsMatch(why, "gateway|proxy|blocked|notification|security", RegexOptions.IgnoreCase);
            return (head.Length > 0 ? head + " " : "") + "(" + (status.Success ? status.Value.Trim() : "error") + ": " + why +
                (url.Success ? ", " + url.Groups[1].Value.TrimEnd(',', '.') : "") + ")" +
                (gateway ? "\nYour company network gateway answered instead of the AI service (often a short block or a rate limit). The CLI retries by itself; if it keeps failing, try again later or ask IT to allow this address." : "");
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
            if (format == "text" || !line.StartsWith("{")) { if (format == "text") { plain.AppendLine(line); if (PlainLine != null) PlainLine(line); } return; }
            Dictionary<string, object> o;
            try { o = Js.DeserializeObject(line) as Dictionary<string, object>; } catch { return; }
            if (o == null) return;
            string type = S(o, "type") ?? "";
            switch (format)
            {
                case "claude":
                    {
                        var sid = S(o, "session_id"); if (sid != null) Emit("session", sid);
                        if (type == "rate_limit_event") { ClaudeLimits(o); break; }
                        if (type == "assistant" && S(o, "parent_tool_use_id") == null)
                        {   // context now = everything the model read for this call (subagent calls have their own context)
                            var use = D(D(o, "message"), "usage");
                            long total = 0, n;
                            foreach (var k in new[] { "input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens" })
                                if (long.TryParse(S(use, k) ?? "", out n)) total += n;
                            if (total > 0) ReportUsage(total, 0);
                        }
                        if (type == "result")
                        {
                            CloseInput();
                            long window = 0, w;
                            var models = o.ContainsKey("modelUsage") ? o["modelUsage"] as Dictionary<string, object> : null;
                            if (models != null) foreach (var mu in models.Values) if (long.TryParse(S(mu, "contextWindow") ?? "", out w)) window = Math.Max(window, w);
                            if (window > 0) ReportUsage(0, window);
                        }
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

    // Auto Account/Model Fallback selector when a run fails with rate-limit, quota, or transient provider errors.
    static class AgentFallback
    {
        public static bool IsRetryableError(string errorText)
        {
            if (string.IsNullOrWhiteSpace(errorText)) return false;
            string s = errorText.ToLowerInvariant();
            return s.Contains("rate limit") || s.Contains("rate_limit") || s.Contains("429")
                || s.Contains("too many requests") || s.Contains("quota") || s.Contains("usage limit")
                || s.Contains("overloaded") || s.Contains("capacity") || s.Contains("529")
                || s.Contains("502") || s.Contains("503") || s.Contains("temporarily unavailable")
                || s.Contains("routing discovery timed out") || s.Contains("account/read failed");
        }

        public static bool PickAlternate(AgentDef def, AgentChat chat, string failedAccount, string failedModel, out string nextAccount, out string nextModel)
        {
            nextAccount = null;
            nextModel = null;
            if (def == null) return false;
            string curAcc = failedAccount ?? "";
            string curModel = failedModel ?? "";

            // 1. Prefer another configured account for the same CLI (skipping accounts with >= 90% usage).
            if (AgentAccounts.Supported(def.Id))
            {
                var candidates = new List<string>();
                try
                {
                    foreach (var n in AgentAccounts.Names(def.Id))
                        if (!string.Equals(n, curAcc, StringComparison.OrdinalIgnoreCase)) candidates.Add(n);
                }
                catch (Exception) { }
                if (curAcc.Length > 0) candidates.Add(""); // CLI default as fallback when a named account fails

                for (int i = 0; i < candidates.Count; i++)
                {
                    string acc = candidates[i];
                    LimitRow lim = null;
                    try { lim = AgentLimits.Find(def.Id, acc); }
                    catch (Exception) { }
                    if (lim != null && lim.High) continue;
                    nextAccount = acc;
                    return true;
                }
            }

            // 2. Otherwise pick an alternate model from user favorites or built-in model list.
            if (AgentModels.HasModels(def))
            {
                string effectiveCur = curModel.Length > 0 ? curModel : AgentModels.DefaultModel(def);
                var models = new List<string>();
                try { models.AddRange(AgentModels.Mine(def)); }
                catch (Exception) { }
                if (def.Models != null) models.AddRange(def.Models);

                for (int i = 0; i < models.Count; i++)
                {
                    string m = (models[i] ?? "").Trim();
                    if (m.Length == 0) continue;
                    if (string.Equals(m, effectiveCur, StringComparison.OrdinalIgnoreCase)) continue;
                    if (AgentModels.IsDead(def, m)) continue;
                    nextModel = m;
                    return true;
                }
            }

            return false;
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
        class Seg { public string FileName, Info; public string Text; public bool Code; public int H; public List<MdBlock> Md; }
        class Item { public AgentMsg M; public List<Seg> Segs; public int Y, H; public Rectangle Box; public string Shown; }
        // A user bubble shows at most this many characters. Measuring and wrapping a whole inserted file (up to 256 KB)
        // on every layout and paint froze the pane, and GDI text measuring returns wrong sizes past about 43,000 characters.
        const int BubbleChars = 3000;
        // Text between ⟦ and ⟧ at the start of a message is context for the agent (page, note, rules); it is not shown.
        public static string ShownText(string text) { if (text != null && text.StartsWith("⟦")) { int e = text.IndexOf("⟧"); if (e > 0) return text.Substring(e + 1).TrimStart('\r', '\n', ' '); } return text ?? ""; }
        static string Bubble(string text)
        {
            text = ShownText(text);
            if (text.Length <= BubbleChars) return text;
            int cut = BubbleChars;
            if (char.IsHighSurrogate(text[cut - 1])) cut--;
            return text.Substring(0, cut) + "\r\n\r\n... " + (text.Length - cut).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) +
                " more characters not shown. The agent gets the full message; right-click > Copy message copies all of it.";
        }
        readonly List<Item> items = new List<Item>();
        AgentMsg foundMessage;
        int measuredFor = -1, measuredCount = -1;
        static Font mono;
        public event Action<string> Status;
        public event Action<string, string> Collab;   // ("quote" | "ask" | "panes", text)
        public event Action<AgentMsg> Branch;
        readonly ContextMenuStrip menu;
        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        // ----- chat text size (Settings.AgentTextScale): message text only, headers keep the interface size -----
        public static event Action ZoomChanged;
        public static float Zoom { get { return Store.Settings.AgentTextScale; } }
        static Font zBase, zSmall, zMono; static string zKey;
        static void ZFonts()
        {
            string key = Zoom + "|" + Ui.Base.GetHashCode() + "|" + Mono.GetHashCode();
            if (key == zKey) return;
            zKey = key;
            zBase = new Font(Ui.Base.FontFamily, Ui.Base.Size * Zoom); zSmall = new Font(Ui.Small.FontFamily, Ui.Small.Size * Zoom); zMono = new Font(Mono.FontFamily, Mono.Size * Zoom);
        }
        static Font TBase { get { ZFonts(); return zBase; } }
        static Font TSmall { get { ZFonts(); return zSmall; } }
        static Font TMono { get { ZFonts(); return zMono; } }
        public static void SetZoom(float z)
        {
            z = (float)Math.Round(Math.Max(0.7f, Math.Min(2.5f, z)), 2);
            if (z == Store.Settings.AgentTextScale) return;
            Store.Settings.AgentTextScale = z; Store.MarkSettings();
            var h = ZoomChanged; if (h != null) h();
        }
        void OnZoom() { if (IsDisposed) return; measuredFor = -1; Relayout(false); Invalidate(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ZoomChanged += OnZoom; }
        protected override void OnHandleDestroyed(EventArgs e) { ZoomChanged -= OnZoom; base.OnHandleDestroyed(e); }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0) { SetZoom(Zoom + (e.Delta > 0 ? 0.1f : -0.1f)); return; }   // Ctrl+wheel = text size
            base.OnMouseWheel(e);
        }

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
            menu = Ui.Menu(true); Ui.ThemeMenu(menu);
        }
        public void SetChat(AgentChat c) { chat = c; foundMessage = null; measuredFor = -1; links.Clear(); Relayout(true); }
        public string FindMessage(string query, int direction)
        {
            if (chat != null && items.Count != chat.Messages.Count) Relayout(false);
            var matches = string.IsNullOrWhiteSpace(query) ? new List<Item>() :
                items.Where(i => i.M.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (matches.Count == 0)
            {
                foundMessage = null; Invalidate();
                return string.IsNullOrWhiteSpace(query) ? "" : "0 matches";
            }
            int previous = matches.FindIndex(i => i.M == foundMessage);
            int index = direction == 0 || previous < 0 ? (direction < 0 ? matches.Count - 1 : 0) :
                (previous + direction + matches.Count) % matches.Count;
            var hit = matches[index];
            foundMessage = hit.M;
            AutoScrollPosition = new Point(0, Math.Max(0, hit.Y - Ui.S(4)));
            Invalidate();
            return (index + 1) + " / " + matches.Count;
        }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }   // no half-moved pictures while scrolling
        readonly List<MdLink> links = new List<MdLink>();
        MdLink LinkAt(Point p) { var q = new Point(p.X, p.Y - AutoScrollPosition.Y); return links.LastOrDefault(l => l.R.Contains(q)); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var l = LinkAt(e.Location); Cursor = l != null || FileAt(e.Location) != null ? Cursors.Hand : Cursors.Default; }

        static List<Seg> Split(string text)
        {
            var segs = new List<Seg>();
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            var sb = new StringBuilder(); bool code = false; string fileName = null;
            int fenceWidth = 0;
            Action flush = () => {
                string value = sb.Length > 0 ? sb.ToString(0, sb.Length - 1) : "";
                if (fileName != null || value.Trim().Length > 0) segs.Add(new Seg { Text = value, Code = code, FileName = fileName, Info = fileName != null ? ChatFiles.Describe(fileName, value) : null });
                sb.Length = 0;
            };
            foreach (var l in lines)
            {
                string marker = l.Trim();
                int ticks = 0;
                while (ticks < marker.Length && marker[ticks] == (char)96) ticks++;
                bool closes = code && ticks >= fenceWidth && ticks == marker.Length;
                if (closes || (!code && ticks >= 3))
                {
                    flush(); code = !code; fileName = null;
                    fenceWidth = code ? ticks : 0;
                    string info = marker.Substring(ticks).Trim();
                    if (code && info.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    {
                        string proposed = info.Substring(5).Trim();
                        if (ChatFiles.IsName(proposed)) fileName = proposed;
                    }
                    continue;
                }
                if (!code && l.Trim().Length == 0 && sb.Length > 0) { flush(); continue; }
                sb.Append(l).Append('\n');
            }
            if (code) fileName = null; // A partial streamed file stays visible as code.
            flush();
            return segs;
        }

        public void Relayout(bool stickToBottom)
        {
            if (chat == null) { items.Clear(); AutoScrollMinSize = Size.Empty; Invalidate(); return; }
            bool atBottom = stickToBottom || -AutoScrollPosition.Y + ClientSize.Height >= AutoScrollMinSize.Height - Ui.S(40);
            int w = ClientSize.Width;
            if (w != measuredFor) { items.Clear(); measuredFor = w; links.Clear(); }
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
        public void JumpToVerdict()
        {
            var last = items.FindLastIndex(i => i.M.Role == "agent" && i.M.Label.StartsWith("Verdict · ", StringComparison.Ordinal));
            if (last < 0) { Say("This conversation has no council verdict yet."); return; }
            while (last > 0 && items[last - 1].M.Role == "agent" && items[last - 1].M.Label == items[last].M.Label) last--;
            foundMessage = items[last].M;
            AutoScrollPosition = new Point(0, Math.Max(0, items[last].Y - Ui.S(4))); Invalidate();
        }
        public void InvalidateWorking()
        {
            if (Working == null) return;
            int y = (items.Count > 0 ? items[items.Count - 1].Y + items[items.Count - 1].H : Ui.S(12)) + AutoScrollPosition.Y;
            Invalidate(new Rectangle(0, y, ClientSize.Width, Ui.S(52)));
        }

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
                int th = TextRenderer.MeasureText(g, m.Text, TSmall, new Size(Math.Max(40, tw), int.MaxValue), Wrap).Height;
                it.H = th + Ui.S(m.Role == "error" ? 38 : 14); it.Box = new Rectangle(pad, Ui.S(4), w - pad * 2, it.H - Ui.S(8));
                return;
            }
            it.Segs = Split(m.Text);
            if (m.Role == "user")
            {
                int maxW = Math.Max(Ui.S(120), (w - pad * 2) * 82 / 100);
                it.Shown = Bubble(m.Text);
                var sz = TextRenderer.MeasureText(g, it.Shown, TBase, new Size(maxW - Ui.S(24), int.MaxValue), Wrap);
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
                if (s.FileName != null) s.H = Ui.S(38);
                else if (s.Code) s.H = TextRenderer.MeasureText(g, s.Text, TMono, new Size(Math.Max(40, inner - Ui.S(16)), int.MaxValue), Wrap).Height + Ui.S(16);
                else { s.Md = Md.Parse(s.Text); Md.Zoom = Zoom; try { s.H = Md.Layout(g, s.Md, Math.Max(40, inner)); } finally { Md.Zoom = 1f; } }   // tables, headings, lists, bold, links
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
                string who = chat.Council ? "the model council (" + chat.Members.Count(x => !x.Off) + " members)" : chat.Agents.Count == 0 ? "an agent" : string.Join(" + ", chat.Agents.Select(AgentStore.NameOf));
                TextRenderer.DrawText(g, "New chat with " + who, Ui.Big, new Rectangle(0, r.Height / 2 - Ui.S(40), r.Width, Ui.S(30)), Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, "Folder: " + (chat.Folder.Length > 0 ? chat.Folder : "(choose one in the header)") + "\nType below. Enter sends, Shift+Enter makes a new line.", Ui.Small,
                    new Rectangle(Ui.S(20), r.Height / 2 - Ui.S(4), r.Width - Ui.S(40), Math.Max(Ui.S(40), r.Height / 2)), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.PathEllipsis);
                return;
            }
            var clip = e.ClipRectangle;
            links.RemoveAll(l => l.R.IntersectsWith(new Rectangle(clip.X, clip.Y - oy, clip.Width, clip.Height)));
            for (int i = FirstAt(clip.Top - oy); i < items.Count; i++)
            {
                var it = items[i];
                int top = it.Y + oy;
                if (top >= clip.Bottom) break;
                var m = it.M; var box = new Rectangle(it.Box.X, top + it.Box.Y, it.Box.Width, it.Box.Height);
                if (m == foundMessage) using (var marker = new SolidBrush(Ui.Accent))
                    g.FillRectangle(marker, Math.Max(0, box.Left - Ui.S(6)), box.Top, Ui.S(4), box.Height);
                if (m.Role == "tool")
                {
                    using (var b = new SolidBrush(AgentStore.ColorOf(m.Agent))) g.FillEllipse(b, box.X, box.Y + box.Height / 2 - Ui.S(3), Ui.S(6), Ui.S(6));
                    TextRenderer.DrawText(g, m.Text, Mono, new Rectangle(box.X + Ui.S(14), box.Y, box.Width - Ui.S(14), box.Height), Ui.Muted, flags | TextFormatFlags.PathEllipsis);
                }
                else if (m.Role == "info")
                    TextRenderer.DrawText(g, m.Text, TSmall, box, Ui.Muted, Wrap | TextFormatFlags.HorizontalCenter);
                else if (m.Role == "error")
                {
                    using (var path = Ui.Round(box, Ui.S(8))) { using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 40 : 22, Ui.Danger))) g.FillPath(b, path); using (var p = new Pen(Color.FromArgb(120, Ui.Danger))) g.DrawPath(p, path); }
                    TextRenderer.DrawText(g, (m.Label.Length > 0 ? m.Label + ": " : m.Agent.Length > 0 ? AgentStore.NameOf(m.Agent) + ": " : ""), Ui.Bold, new Rectangle(box.X + Ui.S(12), box.Y + Ui.S(4), box.Width, Ui.S(18)), Ui.Danger, flags);
                    TextRenderer.DrawText(g, m.Text, TSmall, new Rectangle(box.X + Ui.S(12), box.Y + Ui.S(22), box.Width - Ui.S(24), box.Height - Ui.S(24)), Ui.Text, Wrap);
                }
                else if (m.Role == "user")
                {
                    using (var path = Ui.Round(box, Ui.S(12))) using (var b = new SolidBrush(Ui.AccentFill)) g.FillPath(b, path);
                    TextRenderer.DrawText(g, it.Shown ?? m.Text, TBase, new Rectangle(box.X + Ui.S(13), box.Y + Ui.S(9), box.Width - Ui.S(24), box.Height - Ui.S(14)), Color.White, Wrap);
                }
                else
                {
                    var col = AgentStore.ColorOf(m.Agent);
                    using (var path = Ui.Round(box, Ui.S(12))) { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(Ui.Line)) g.DrawPath(p, path); }
                    using (var b = new SolidBrush(col)) g.FillEllipse(b, box.X + Ui.S(14), box.Y + Ui.S(13), Ui.S(8), Ui.S(8));
                    string who = m.Label.Length > 0 ? m.Label : AgentStore.NameOf(m.Agent);
                    TextRenderer.DrawText(g, who, Ui.Bold, new Rectangle(box.X + Ui.S(28), box.Y + Ui.S(6), box.Width * 3 / 4, Ui.S(22)), col, flags);
                    if (!string.IsNullOrEmpty(m.Model))
                    {
                        int ww = TextRenderer.MeasureText(g, who, Ui.Bold, new Size(int.MaxValue, Ui.S(22)), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                        int mx = box.X + Ui.S(28) + ww + Ui.S(10), mw = box.Right - Ui.S(90) - mx;
                        if (mw > Ui.S(60)) TextRenderer.DrawText(g, m.Model, Ui.Small, new Rectangle(mx, box.Y + Ui.S(6), mw, Ui.S(22)), Ui.Muted, flags | TextFormatFlags.EndEllipsis);
                    }
                    TextRenderer.DrawText(g, new DateTime(m.Time).ToString("HH:mm"), Ui.Small, new Rectangle(box.Right - Ui.S(80), box.Y + Ui.S(6), Ui.S(66), Ui.S(22)), Ui.Muted, flags | TextFormatFlags.Right);
                    int y = box.Y + Ui.S(30), x = box.X + Ui.S(14), inner = box.Width - Ui.S(28);
                    foreach (var s in it.Segs)
                    {
                        if (y > clip.Bottom) break;
                        if (y + s.H < clip.Top) { y += s.H + Ui.S(8); continue; }
                        if (s.FileName != null)
                        {
                            var fileBox = new Rectangle(x, y, inner, s.H);
                            using (var path = Ui.Round(fileBox, Ui.S(6))) using (var brush = new SolidBrush(Ui.SelBg)) g.FillPath(brush, path);
                            TextRenderer.DrawText(g, "Click to save the file  ·  " + s.FileName + "  ·  " + s.Info, Ui.Bold, Rectangle.Inflate(fileBox, -Ui.S(10), 0), Ui.Accent, flags);
                        }
                        else if (s.Code)
                        {
                            var cr = new Rectangle(x, y, inner, s.H);
                            using (var path = Ui.Round(cr, Ui.S(6))) using (var b = new SolidBrush(Ui.Dark ? Color.FromArgb(18, 20, 26) : Color.FromArgb(244, 246, 250))) g.FillPath(b, path);
                            TextRenderer.DrawText(g, s.Text, TMono, new Rectangle(x + Ui.S(8), y + Ui.S(8), inner - Ui.S(16), s.H - Ui.S(12)), Ui.Text, Wrap);
                        }
                        else
                        {
                            var fresh = new List<MdLink>();
                            Md.Zoom = Zoom; try { Md.Draw(g, s.Md, x, y, inner, clip, Ui.Text, fresh); } finally { Md.Zoom = 1f; }
                            foreach (var l in fresh) { l.R.Offset(0, -oy); links.Add(l); }   // kept in content coordinates
                        }
                        y += s.H + Ui.S(8);
                    }
                }
            }
            if (Working != null)
            {
                int y = (items.Count > 0 ? items[items.Count - 1].Y + items[items.Count - 1].H : Ui.S(12)) + oy;
                long secs = Math.Max(0, (Store.Now() - WorkingSince) / TimeSpan.TicksPerSecond);
                int dots = (int)((uint)Environment.TickCount / 400 % 4);
                TextRenderer.DrawText(g, Working + new string('.', dots) + "   " + (secs >= 60 ? (secs / 60) + "m " : "") + (secs % 60) + "s", Ui.Bold, new Rectangle(Ui.S(20), y + Ui.S(4), ClientSize.Width - Ui.S(40), Ui.S(22)), Ui.Accent, flags);
                if (!string.IsNullOrEmpty(LastTool)) TextRenderer.DrawText(g, LastTool, Mono, new Rectangle(Ui.S(20), y + Ui.S(26), ClientSize.Width - Ui.S(40), Ui.S(20)), Ui.Muted, flags);
            }
        }

        int FirstAt(int y)
        {
            int lo = 0, hi = items.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (items[mid].Y + items[mid].H <= y) lo = mid + 1; else hi = mid;
            }
            return lo;
        }
        Item At(Point p)
        {
            int y = p.Y - AutoScrollPosition.Y, i = FirstAt(y);
            return i < items.Count && items[i].Y <= y ? items[i] : null;
        }
        Seg FileAt(Point point)
        {
            var it = At(point);
            if (it == null || it.M.Role != "agent" || it.Segs == null) return null;
            int top = it.Y + it.Box.Y + AutoScrollPosition.Y + Ui.S(30);
            foreach (var segment in it.Segs)
            {
                if (point.Y < top) break;
                if (segment.FileName != null && new Rectangle(it.Box.X + Ui.S(14), top, it.Box.Width - Ui.S(28), segment.H).Contains(point)) return segment;
                top += segment.H + Ui.S(8);
            }
            return null;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) {
                var file = FileAt(e.Location);
                if (file != null) { ChatFiles.Choose(this, file.FileName, file.Text); return; }
                var link = LinkAt(e.Location); if (link != null) Md.Open(link.Url); return; }
            if (e.Button != MouseButtons.Right || chat == null) return;
            var it = At(e.Location);
            menu.Items.Clear();
            if (it != null)
            {
                string text = it.M.Text;
                if (it.M.Role == "user" || it.M.Role == "agent")
                    menu.Items.Add("Branch from this message", null, delegate { var h = Branch; if (h != null) h(it.M); });
                if (it.M.Role == "agent")
                {
                    var save = new ToolStripMenuItem("Save reply as");
                    for (int n = 0; n < ChatFiles.Extensions.Length; n++)
                    {
                        string name = "Reply." + ChatFiles.Extensions[n];
                        save.DropDownItems.Add(ChatFiles.Names[n] + "…", null, delegate { ChatFiles.Choose(this, name, text); });
                    }
                    menu.Items.Add(save);
                }
                menu.Items.Add("Copy message", null, delegate { App.I.CopyText(text); Say("Message copied."); });
                menu.Items.Add(AppLinks.SendTo(null, text, FindForm()));   // the answer to a note, task, prompt, Markdown, Browser...
                var code = it.Segs == null ? new List<Seg>() : it.Segs.Where(s => s.Code).ToList();
                if (code.Count > 0)
                {
                    var saveCode = new ToolStripMenuItem("Save code block as file");
                    for (int n = 0; n < code.Count; n++)
                    {
                        var segment = code[n];
                        saveCode.DropDownItems.Add("Block " + (n + 1) + "  ·  " + Short(segment.Text, 32), null,
                            delegate { ChatFiles.Choose(this, "Code.txt", segment.Text, true); });
                    }
                    menu.Items.Add(saveCode);
                }
                if (code.Count > 0) menu.Items.Add("Copy code", null, delegate { App.I.CopyText(string.Join("\n\n", code.Select(s => s.Text))); Say("Code copied."); });
                menu.Items.Add("Save as prompt", null, delegate { Store.NewPrompt(Short(text, 50), text, "Agents"); Say("Saved to Prompts."); });
                menu.Items.Add(new ToolStripSeparator());
                if (it.M.Role == "user") menu.Items.Add("Edit as a new question", null, delegate { var h = Collab; if (h != null) h("edit", text); });
                menu.Items.Add("Quote in my reply", null, delegate { var h = Collab; if (h != null) h("quote", text); });
                menu.Items.Add("Ask another agent about this...", null, delegate { var h = Collab; if (h != null) h("ask", text); });
                menu.Items.Add("Send to the other panes", null, delegate { var h = Collab; if (h != null) h("panes", text); });
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
    // The message box: Ctrl+V, Shift+Insert and the right-click Paste all send WM_PASTE. Pasting picks up a copied
    // image (screenshot) or copied files as attachments; text pastes as usual.
    class PasteBox : TextBox
    {
        public Func<bool> Pasting;
        protected override void WndProc(ref Message m)
        {
            const int WM_PASTE = 0x0302;
            if (m.Msg == WM_PASTE && Pasting != null && Pasting()) return;
            base.WndProc(ref m);
        }
    }

    partial class ChatPane : Panel
    {
        public AgentChat Chat { get; private set; }
        // Files for the next message (copied into data\agents\attachments\<chat>), messages typed while an agent works,
        // and the context reading of each chat, agent and account (kept while the app runs).
        readonly List<string> attachments = new List<string>();
        readonly List<KeyValuePair<string, List<string>>> queued = new List<KeyValuePair<string, List<string>>>();
        List<string> nextFiles;
        FlowLayoutPanel attachBar;
        Label usageLine;
        static readonly Dictionary<string, AgentUsage> contexts = new Dictionary<string, AgentUsage>();
        public event Action<ChatPane> Activated, Changed;
        readonly ChatView view;
        readonly Panel findBar;
        readonly TextBox findText;
        readonly Label findCount;
        readonly TextBox input;
        readonly Button send;
        readonly PaneHeader header;
        readonly CommandList cmdList = new CommandList();
        readonly Panel bottom;
        readonly TargetStrip strip;
        static Font inputFont; static float inputFor;
        static Font InputFont() { if (inputFont == null || inputFor != ChatView.Zoom) { inputFor = ChatView.Zoom; inputFont = new Font(Ui.Base.FontFamily, Ui.Base.Size * ChatView.Zoom); } return inputFont; }
        void OnZoom() { if (!IsDisposed) input.Font = InputFont(); }
        int InputH { get { int h = Store.Settings.AgentInputHeight; return h > 0 ? Ui.S(h) : Ui.S(140); } }
        readonly Queue<string> queue = new Queue<string>();
        AgentRun run;
        string runAgent;
        bool active;
        readonly System.Windows.Forms.Timer checkpoint = new System.Windows.Forms.Timer { Interval = 1500 };
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 400 };

        public bool Busy { get { return runAgent != null || councilBusy; } }
        string netRetry; readonly HashSet<string> retried = new HashSet<string>();   // one automatic retry per agent per message
        readonly HashSet<string> fallbackTried = new HashSet<string>();              // one account/model fallback per agent per message
        string fallbackAccountOverride, fallbackModelOverride;
        readonly List<AgentRun> councilRuns = new List<AgentRun>();
        bool councilBusy;
        public bool IsActive { get { return active; } set { if (active != value) { active = value; Invalidate(); header.Invalidate(); } } }

        public ChatPane(AgentChat chat)
        {
            Padding = new Padding(Ui.S(1)); BackColor = Ui.Bg;
            view = new ChatView { Dock = DockStyle.Fill };
            view.Status += s => App.I.Status(s);
            view.MouseDown += delegate { Activate(); };
            header = new PaneHeader(this) { Dock = DockStyle.Top };
            bottom = new Panel { Dock = DockStyle.Bottom, Height = InputH, Padding = new Padding(Ui.S(10), 0, Ui.S(10), Ui.S(10)), BackColor = Ui.Bg };
            input = new PasteBox { Multiline = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = InputFont(), AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, BackColor = Ui.Card, ForeColor = Ui.Text, MaxLength = 0 };   // no length limit
            var inputCard = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(10), Ui.S(8), Ui.S(8), Ui.S(6)), BackColor = Ui.Bg };
            inputCard.Paint += (s, e) =>
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Ui.Round(new Rectangle(0, 0, inputCard.Width - 1, inputCard.Height - 1), Ui.S(10)))
                { using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path); using (var p = new Pen(input.Focused ? Ui.Accent : Ui.Line)) g.DrawPath(p, path); }
            };
            inputCard.Resize += delegate { inputCard.Invalidate(); };
            inputCard.Controls.Add(input);
            ((PasteBox)input).Pasting = PasteAttachments;
            attachBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Ui.S(30), WrapContents = false, AutoScroll = true, BackColor = Ui.Card, Visible = false, Margin = new Padding(0), Padding = new Padding(0, 0, 0, Ui.S(2)) };
            inputCard.Controls.Add(attachBar);
            foreach (Control target in new Control[] { input, view })
            {
                target.AllowDrop = true;
                target.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
                target.DragDrop += (s, e) => { var dropped = e.Data.GetData(DataFormats.FileDrop) as string[]; if (dropped != null) AddFiles(dropped); };
            }
            input.GotFocus += delegate { inputCard.Invalidate(); input.Visible = true; };
            input.LostFocus += delegate { inputCard.Invalidate(); };
            // Placeholder (cue banners do not work on multi-line boxes): a label inside the box, hidden while there is text.
            var hintLabel = new Label { Text = "Message   (Enter send, / commands, @name one agent)", AutoSize = false, Dock = DockStyle.Fill, ForeColor = Ui.Muted, BackColor = Ui.Card, Font = Ui.Base, Cursor = Cursors.IBeam, UseMnemonic = false };
            hintLabel.Click += delegate { input.Focus(); };
            input.Controls.Add(hintLabel);
            input.AccessibleName = "Chat message";
            input.GotFocus += delegate { hintLabel.Visible = false; };
            input.LostFocus += delegate { hintLabel.Visible = input.TextLength == 0; };
            input.TextChanged += delegate {
                hintLabel.Visible = input.TextLength == 0 && !input.Focused;
                bool first = Chat != null && !Chat.HasContent;
                AgentStore.SetDraft(Chat, input.Text);
                if (first && Chat != null && Chat.HasContent) RaiseChanged();
                UpdateCmdList();
                UpdateSend();
            };
            input.KeyDown += OnInputKey;
            input.LostFocus += delegate { BeginInvoke(new Action(() => { if (!cmdList.Focused && !input.Focused) cmdList.Visible = false; })); };
            cmdList.MouseClick += delegate { AcceptCmd(); };
            Controls.Add(cmdList);
            input.GotFocus += delegate { Activate(); };
            // While an agent works: an empty box makes this Stop; with text it steers (Claude) or queues (the others).
            send = Ui.Btn("Send", delegate { if (Busy && input.Text.Trim().Length == 0 && attachments.Count == 0) Stop(); else SendInput(); if (input.CanFocus) input.Focus(); }, true);
            send.Dock = DockStyle.Fill; send.Margin = new Padding(0);
            var cmdBtn = Ui.Btn("/", delegate { OpenCommands(); });
            cmdBtn.AutoSize = false; cmdBtn.Dock = DockStyle.Right; cmdBtn.Width = Ui.S(40); cmdBtn.Margin = new Padding(0);
            new ToolTip().SetToolTip(cmdBtn, "Commands: help, favorites, your own commands");
            var create = Ui.Btn("Files", delegate { FileRequestMenu(); });
            create.AutoSize = false; create.Dock = DockStyle.Fill; create.Margin = new Padding(0);
            // Buttons stay at the bottom right however tall the box is: [Files][/] above [Send].
            var topRow = new Panel { Dock = DockStyle.Top, Height = Ui.S(Ui.ControlHeight), BackColor = Ui.Bg };
            topRow.Controls.Add(create); topRow.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Ui.S(6), BackColor = Ui.Bg }); topRow.Controls.Add(cmdBtn);
            var btnRow = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(Ui.ControlHeight * 2 + 6), BackColor = Ui.Bg };
            btnRow.Controls.Add(send); btnRow.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(6), BackColor = Ui.Bg }); btnRow.Controls.Add(topRow);
            var rightCol = new Panel { Dock = DockStyle.Right, Width = Ui.S(138), BackColor = Ui.Bg };
            rightCol.Controls.Add(btnRow);
            new ToolTip().SetToolTip(create, "Put a text file into your message, or ask the agent to make an Excel, Word, PowerPoint, PDF or other file.\nThe answer shows a Save card; click it to save the file.");
            bottom.Controls.Add(inputCard);
            bottom.Controls.Add(new Panel { Dock = DockStyle.Right, Width = Ui.S(8), BackColor = Ui.Bg });
            bottom.Controls.Add(rightCol);
            // Who answers this message, in what order; and the expand button.
            strip = new TargetStrip(this) { Dock = DockStyle.Top };
            bottom.Controls.Add(strip);
            // When the "Answer:" row wraps onto more lines, the box area grows so the text box keeps its room.
            strip.Resize += delegate { if (bottom.Height < MinInputH()) bottom.Height = MinInputH(); };
            // Drag the top edge to make the box taller or shorter.
            var grip = new Panel { Dock = DockStyle.Top, Height = Ui.S(7), Cursor = Cursors.SizeNS, BackColor = Ui.Bg };
            int dragY = 0, dragH = 0; bool dragging = false;
            grip.Paint += (s, e) => { using (var b = new SolidBrush(Ui.Line)) e.Graphics.FillRectangle(b, grip.Width / 2 - Ui.S(18), Ui.S(2), Ui.S(36), Ui.S(3)); };
            grip.MouseDown += (s, e) => { dragging = true; dragY = Cursor.Position.Y; dragH = bottom.Height; };
            grip.MouseMove += (s, e) => { if (dragging) SetInputHeight(dragH + dragY - Cursor.Position.Y, false); };
            grip.MouseUp += (s, e) => { if (dragging) { dragging = false; SetInputHeight(bottom.Height, true); } };
            new ToolTip().SetToolTip(grip, "Drag to make the message box taller or shorter");
            bottom.Controls.Add(grip);
            // Context used and the account's 5-hour / weekly limits, for each agent of this chat.
            usageLine = new Label { Dock = DockStyle.Bottom, Height = Ui.S(20), ForeColor = Ui.Muted, BackColor = Ui.Bg, Font = Ui.Small, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand, Text = "" };
            usageLine.Click += delegate { using (var d = new LimitsDialog()) d.ShowDialog(FindForm()); };
            new ToolTip().SetToolTip(usageLine, "Context = how full the agent's memory of this chat is (after its latest step).\n5h / week = the account's usage limits. Click for all accounts.");
            bottom.Controls.Add(usageLine);
            if (bottom.Height < MinInputH()) bottom.Height = MinInputH();
            usageLine.VisibleChanged += delegate { if (bottom.Height < MinInputH()) bottom.Height = MinInputH(); };
            findBar = new Panel { Dock = DockStyle.Top, Height = Ui.S(34), Padding = new Padding(Ui.S(5)), Visible = false };
            findText = Ui.Search("Find in this chat"); findText.Dock = DockStyle.Fill;
            findCount = new Label { Dock = DockStyle.Right, Width = Ui.S(68), TextAlign = ContentAlignment.MiddleCenter };
            findBar.Controls.Add(findText); findBar.Controls.Add(findCount);
            foreach (string action in new[] { "Previous", "Next", "Close" })
            {
                string name = action;
                var b = Ui.Btn(name, delegate { if (name == "Close") CloseFind(); else FindText(name == "Next" ? 1 : -1); });
                b.Dock = DockStyle.Right; b.AutoSize = false; b.Width = Ui.S(name == "Previous" ? 66 : 46);
                findBar.Controls.Add(b);
            }
            var refreshFind = Ui.Debounce(findText, 150, () => { if (findBar.Visible && lastFind != findText.Text) FindText(0); });
            findText.TextChanged += delegate { refreshFind(); };
            Controls.Add(view); Controls.Add(findBar); Controls.Add(bottom); Controls.Add(header);
            checkpoint.Tick += delegate { checkpoint.Stop(); AgentStore.Save(Chat); };
            tick.Tick += delegate { if (Busy) view.InvalidateWorking(); };   // only the "working" line, not the whole chat
            view.Collab += OnCollab;
            view.Branch += message =>
            {
                var page = FindPage();
                int index = Chat.Messages.IndexOf(message);
                if (page == null || index < 0) return;
                var branch = AgentStore.CopyThrough(Chat, index + 1);
                AgentStore.Chats.Add(branch); AgentStore.Save(branch); page.OpenChatPublic(branch);
            };
            SetChat(chat);
        }

        void FileRequestMenu()
        {
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            var attach = menu.Items.Add("Attach images or files…", null, delegate { BeginInvoke(new Action(ChooseAttachments)); });
            attach.ToolTipText = "The agent gets the file paths and can open them. Claude Code and Codex see images. Same as Ctrl+V or drag and drop.";
            var pasteImage = menu.Items.Add("Paste image from clipboard", null, delegate { if (!PasteAttachments()) App.I.Status("The clipboard has no image or files."); });
            pasteImage.Enabled = Clipboard.ContainsImage() || Clipboard.ContainsFileDropList();
            menu.Items.Add("Put a text file into my message…", null, delegate { BeginInvoke(new Action(InsertFile)); }).ToolTipText = "Copies the file's text into the message box.";
            menu.Items.Add(new ToolStripSeparator());
            var head = new ToolStripMenuItem("Ask the agent to make a file:") { Enabled = false };
            menu.Items.Add(head);
            for (int i = 0; i < ChatFiles.Extensions.Length; i++)
            {
                string ext = ChatFiles.Extensions[i];
                var item = menu.Items.Add("     " + ChatFiles.Names[i] + "  (." + ext + ")", null, delegate {
                    input.AppendText((input.TextLength == 0 ? "" : "\r\n\r\n") + ChatFiles.Request(ext) + " "); input.SelectionStart = input.TextLength; input.Focus();
                });
                item.ToolTipText = "Adds a request to your message. Write what the file should contain, then send. The answer shows a Save card.";
            }
            menu.ShowItemToolTips = true;
            // Dispose later: the click handler may still be running when the menu closes.
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); }; menu.Show(Cursor.Position);
        }

        async void InsertFile()
        {
            var target = Chat;
            if (target == null) return;
            string path;
            using (var choose = new OpenFileDialog { Filter = "Text, Markdown, code and data|*.txt;*.md;*.csv;*.json;*.xml;*.yaml;*.cs;*.py;*.js;*.html;*.css;*.sql|All files|*.*" })
            {
                if (choose.ShowDialog(FindForm()) != DialogResult.OK) return;
                path = choose.FileName;
            }
            try
            {
                string text = await System.Threading.Tasks.Task.Run(() => ChatFiles.ReadAttachment(path));
                if (IsDisposed) return;
                if (target == Chat) { input.AppendText((input.TextLength == 0 ? "" : "\r\n\r\n") + text); input.Focus(); }
                else if (AgentStore.Chats.Contains(target))
                    AgentStore.SetDraft(target, target.PendingInput + (target.PendingInput.Length == 0 ? "" : "\r\n\r\n") + text);
                else MessageBox.Show(FindForm(), "The original chat was closed. Reopen it to insert the file.", "Chat files");
            }
            catch (Exception ex) { if (!IsDisposed) MessageBox.Show(FindForm(), ex.Message, "Could not insert file"); }
        }

        // ---------- attachments (Ctrl+V, drag and drop, Files menu) ----------
        string AttachDir() { return Path.Combine(AgentStore.Dir, "attachments", Chat == null ? "chat" : Chat.Id); }

        // Called on WM_PASTE: true when the clipboard held an image or files (they become attachments).
        bool PasteAttachments()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var list = Clipboard.GetFileDropList();
                    var paths = new string[list.Count]; list.CopyTo(paths, 0);
                    AddFiles(paths); return paths.Length > 0;
                }
                if (Clipboard.ContainsImage() && !Clipboard.ContainsText())
                {
                    using (var image = Clipboard.GetImage())
                    {
                        if (image == null) return false;
                        string dir = AttachDir(); Directory.CreateDirectory(dir);
                        string file = Path.Combine(dir, "image-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                        image.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        AddFiles(new[] { file });
                    }
                    return true;
                }
            }
            catch (Exception ex) { App.I.Status("Could not paste: " + ex.Message); }
            return false;
        }
        void ChooseAttachments()
        {
            using (var choose = new OpenFileDialog { Multiselect = true, Title = "Attach images or files", Filter = "All files|*.*|Images|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp" })
                if (choose.ShowDialog(FindForm()) == DialogResult.OK) AddFiles(choose.FileNames);
        }
        void AddFiles(IEnumerable<string> paths)
        {
            foreach (string p in paths)
                if (!string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p)) && !attachments.Contains(p, StringComparer.OrdinalIgnoreCase)) attachments.Add(p);
            if (attachments.Count > 20) { attachments.RemoveRange(20, attachments.Count - 20); App.I.Status("At most 20 attachments per message."); }
            RefreshAttachBar();
            if (input.CanFocus) input.Focus();
        }
        void RefreshAttachBar()
        {
            attachBar.SuspendLayout();
            foreach (Control c in attachBar.Controls.Cast<Control>().ToList()) c.Dispose();
            foreach (string p in attachments)
            {
                string path = p;
                string name = Path.GetFileName(path.TrimEnd('\\', '/')); if (name.Length > 28) name = name.Substring(0, 25) + "...";
                var chip = new Label { AutoSize = true, Text = (AgentRun.IsImage(path) ? "Image · " : Directory.Exists(path) ? "Folder · " : "File · ") + name + "   ×",
                    ForeColor = Ui.Text, BackColor = Ui.Bg, Font = Ui.Small, Padding = new Padding(Ui.S(6), Ui.S(3), Ui.S(6), Ui.S(3)), Margin = new Padding(0, 0, Ui.S(6), 0), Cursor = Cursors.Hand, UseMnemonic = false };
                new ToolTip().SetToolTip(chip, path + "\nClick to remove.");
                chip.Click += delegate { attachments.Remove(path); RefreshAttachBar(); };
                attachBar.Controls.Add(chip);
            }
            attachBar.Visible = attachments.Count > 0;
            attachBar.ResumeLayout();
            UpdateSend();
        }
        // The text the agent reads about the attached files (every CLI can open a path; Claude and Codex also see images).
        static string AttachNote(List<string> files)
        {
            if (files == null || files.Count == 0) return "";
            var sb = new StringBuilder("\n\n[Attached files. Open them from these paths:]");
            foreach (string f in files) sb.Append("\n- ").Append(f);
            return sb.ToString();
        }

        // ---------- send button, steer and queue ----------
        void UpdateSend()
        {
            if (send == null) return;
            bool has = input.Text.Trim().Length > 0 || attachments.Count > 0;
            string text = !Busy ? "Send" : !has ? "Stop" : run != null && run.CanSteer && !councilBusy ? "Steer" : "Queue";
            if (queued.Count > 0 && !has && Busy) text = "Stop (" + queued.Count + " queued)";
            if (send.Text != text) send.Text = text;
        }
        // While an agent works: Claude Code takes the message now (it reads it after the current tool calls);
        // the others get it as the next message when this turn ends.
        void SteerOrQueue(string text)
        {
            var files = attachments.ToList();
            string full = text + AttachNote(files);
            var r = run;
            if (files.Count == 0 && r != null && !councilBusy && r.CanSteer && r.Steer(full))
            {
                Add("user", "", full);
                AgentStore.Save(Chat);
                App.I.Status("Sent to " + AgentStore.NameOf(runAgent) + " while it works. It reads it after its current step.");
            }
            else
            {
                queued.Add(new KeyValuePair<string, List<string>>(text, files));
                App.I.Status("Queued (" + queued.Count + "). It is sent when this turn ends. Press Stop to send it now.");
            }
            input.Clear(); attachments.Clear(); RefreshAttachBar();
        }
        void SendQueued()
        {
            if (queued.Count == 0 || Busy) return;
            var all = queued.ToList(); queued.Clear();
            string text = string.Join("\n\n", all.Select(q => q.Key).Where(t => t.Length > 0));
            var files = all.SelectMany(q => q.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            nextFiles = files;
            Send(text.Length > 0 ? text : "See the attached files.");
            nextFiles = null;
        }

        // ---------- context and limits line ----------
        static string UsageKey(string chat, string agent, string account) { return chat + "|" + agent + "|" + (account ?? ""); }
        void OnUsage(AgentUsage u)
        {
            if (Chat == null) return;
            AgentUsage old;
            lock (contexts)
            {
                string key = UsageKey(Chat.Id, u.Agent, u.Account);
                if (u.Window == 0 && contexts.TryGetValue(key, out old)) u.Window = old.Window;   // keep the last known window size
                contexts[key] = u;
            }
            UpdateUsage();
        }
        void UpdateUsage()
        {
            if (usageLine == null || usageLine.IsDisposed || Chat == null) return;
            var parts = new List<string>();
            foreach (string id in Chat.Agents.Where(a => !Chat.Off.Contains(a)).Take(4))
            {
                string account = AgentAccounts.Selected(Chat, id);
                var bits = new List<string>();
                AgentUsage u;
                lock (contexts) contexts.TryGetValue(UsageKey(Chat.Id, id, account), out u);
                if (u != null && u.Used > 0)
                    bits.Add(u.Window > 0 ? "context " + Math.Min(100, (int)Math.Round(u.Used * 100.0 / u.Window)) + "% (" + Tokens(u.Used) + "/" + Tokens(u.Window) + ")" : "context " + Tokens(u.Used));
                var l = AgentLimits.Find(id, account);
                if (l != null) bits.Add("5h " + AgentLimits.Pct(l.FiveHour) + " · week " + AgentLimits.Pct(l.Weekly));
                if (bits.Count > 0) parts.Add(AgentStore.NameOf(id) + (account.Length > 0 ? " (" + account + ")" : "") + ": " + string.Join(" · ", bits));
            }
            string text = string.Join("     ", parts);
            if (usageLine.Text != text) usageLine.Text = text;
            usageLine.Visible = text.Length > 0;
        }
        static string Tokens(long n) { return n >= 1000000 ? (n / 1000000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M" : n >= 1000 ? (n / 1000) + "k" : n.ToString(); }
        void OnLimitsUpdated() { Ui0(UpdateUsage); }

        void Activate() { var h = Activated; if (h != null) h(this); }

        // Message box height: drag the grip or use the expand button; remembered for all panes.
        void SetInputHeight(int h, bool save)
        {
            int min = MinInputH(), max = Math.Max(min, Height - header.Height - Ui.S(80));
            bottom.Height = Math.Max(min, Math.Min(max, h));
            if (save) { Store.Settings.AgentInputHeight = (int)Math.Round(bottom.Height / Ui.Scale); Store.MarkSettings(); }
        }
        public void ToggleExpand()
        {
            bool big = bottom.Height > Height / 2;
            SetInputHeight(big ? InputHDefault() : (int)(Height * 0.62), false);
            input.Focus();
        }
        int InputHDefault() { int h = Store.Settings.AgentInputHeight; return Math.Max(MinInputH(), h > 0 && Ui.S(h) < Height / 2 ? Ui.S(h) : Ui.S(140)); }
        // Grip + "Answer:" row + the box (fits [Files][/] over [Send]) + usage line + bottom padding.
        int MinInputH()
        {
            int h = Ui.S(7) + Ui.S(Ui.ControlHeight * 2 + 6) + Ui.S(10);
            if (strip != null) h += strip.Height;
            if (usageLine != null && usageLine.Visible) h += usageLine.Height;
            return h;
        }

        // The agents that answer the next message, in order: @name at the start picks only those; else the ones turned on.
        List<string> Responders(ref string text)
        {
            var c = Chat; var pick = new List<string>();
            var m = Regex.Match(text, "^(\\s*@([\\w.-]+)[,\\s]*)+");
            if (m.Success)
            {
                foreach (Capture cap in m.Groups[2].Captures)
                {
                    string n = cap.Value.ToLowerInvariant();
                    var id = c.Agents.FirstOrDefault(a => a.ToLowerInvariant() == n || AgentStore.NameOf(a).ToLowerInvariant().Replace(" ", "").StartsWith(n));
                    if (id != null && !pick.Contains(id)) pick.Add(id);
                }
                if (pick.Count > 0) { text = text.Substring(m.Length).Trim(); return pick; }
            }
            return c.Agents.Where(a => !c.Off.Contains(a)).ToList();
        }
        void RaiseChanged() { header.Invalidate(); if (strip != null) strip.Invalidate(); var h = Changed; if (h != null) h(this); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
            using (var p = new Pen(active ? Ui.Accent : Ui.Line, active ? Ui.S(2) : 1)) g.DrawPath(p, path);
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); InvalidateBorder(); }
        void InvalidateBorder() { int b = Ui.S(14); Invalidate(new Rectangle(0, 0, Width, b)); Invalidate(new Rectangle(0, Height - b, Width, b)); Invalidate(new Rectangle(0, 0, b, Height)); Invalidate(new Rectangle(Width - b, 0, b, Height)); }   // only the rounded border, not the whole pane

        public void SetChat(AgentChat chat)
        {
            if (Busy)
            {
                // Switching away from a working chat: stop it AND detach its run now. Before, the stopped run kept
                // reporting until it exited, so its last output, its session id and its "done" landed in the newly
                // opened chat, and the old chat stayed marked busy.
                string waiting = string.Join("\n\n", queued.Select(q => q.Key).Where(t => t.Length > 0).ToArray());
                queued.Clear();
                Stop();
                if (run != null) { run = null; Done(); }
                if (waiting.Length > 0) input.Text = (input.Text.Trim().Length > 0 ? input.Text.TrimEnd() + "\r\n\r\n" : "") + waiting;   // kept as the old chat's draft
            }
            if (checkpoint.Enabled) { checkpoint.Stop(); AgentStore.Save(Chat); }
            ResetTeamDesk();
            findBar.Visible = false; findText.Clear(); findCount.Text = ""; lastFind = "";
            if (Chat != null) AgentStore.SetDraft(Chat, input.Text);
            Chat = chat;
            queued.Clear(); attachments.Clear(); if (attachBar != null) RefreshAttachBar();
            input.Text = chat == null ? "" : chat.PendingInput;
            input.SelectionStart = input.TextLength;
            view.SetChat(chat);
            UpdateUsage();
            header.Invalidate();
            RaiseChanged();
        }

        string lastFind = "";
        void FindText(int direction)
        {
            if (lastFind != findText.Text) direction = 0;
            lastFind = findText.Text;
            findCount.Text = view.FindMessage(lastFind, direction);
        }
        void OpenFind() { findBar.Visible = true; findText.Focus(); findText.SelectAll(); FindText(0); }
        void CloseFind() { findBar.Visible = false; view.FindMessage("", 0); input.Focus(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == (Keys.Control | Keys.F)) { OpenFind(); return true; }
            // Chat text size: Ctrl + / Ctrl - / Ctrl 0 (also Ctrl+mouse wheel over the chat).
            if (keys == (Keys.Control | Keys.Oemplus) || keys == (Keys.Control | Keys.Add)) { ChatView.SetZoom(ChatView.Zoom + 0.1f); return true; }
            if (keys == (Keys.Control | Keys.OemMinus) || keys == (Keys.Control | Keys.Subtract)) { ChatView.SetZoom(ChatView.Zoom - 0.1f); return true; }
            if (keys == (Keys.Control | Keys.D0) || keys == (Keys.Control | Keys.NumPad0)) { ChatView.SetZoom(1f); return true; }
            if (findBar.Visible)
            {
                if (keys == Keys.Escape) { CloseFind(); return true; }
                if (keys == Keys.F3 || (findText.Focused && keys == Keys.Enter)) { FindText(1); return true; }
                if (keys == (Keys.Shift | Keys.F3) || (findText.Focused && keys == (Keys.Shift | Keys.Enter))) { FindText(-1); return true; }
            }
            return base.ProcessCmdKey(ref msg, keys);
        }

        void OnInputKey(object s, KeyEventArgs e)
        {
            if (cmdList.Visible && cmdList.Items.Count > 0)
            {
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up) { e.SuppressKeyPress = true; cmdList.SelectedIndex = Math.Max(0, Math.Min(cmdList.Items.Count - 1, cmdList.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1))); return; }
                if (e.KeyCode == Keys.Tab || (e.KeyCode == Keys.Enter && !e.Shift && !input.Text.Contains(" "))) { e.SuppressKeyPress = true; AcceptCmd(); return; }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; cmdList.Visible = false; return; }
            }
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendInput(); }
            else if (e.KeyCode == Keys.Escape && Busy) { e.SuppressKeyPress = true; if (queued.Count > 0) App.I.Status("Stopped. The queued messages were dropped."); queued.Clear(); Stop(); }   // the Stop button instead sends the queue
        }
        // ---------- "/" commands ----------
        void UpdateCmdList()
        {
            string t = input.Text;
            if (!t.StartsWith("/") || t.Contains(" ") || t.Contains("\n")) { cmdList.Visible = false; return; }
            string q = t.Substring(1).ToLowerInvariant();
            var all = AgentCommands.Catalog(Chat);
            var hits = all.Where(c => c.Name.Substring(1).ToLowerInvariant().StartsWith(q)).Concat(all.Where(c => !c.Name.Substring(1).ToLowerInvariant().StartsWith(q) && (c.Name + " " + c.Desc).ToLowerInvariant().Contains(q))).Take(40).ToList();
            if (hits.Count == 0) { cmdList.Visible = false; return; }
            cmdList.Fill(hits);
            int rows = Math.Min(8, hits.Count);
            int bottomH = input.Parent.Parent.Height;
            cmdList.Bounds = new Rectangle(Ui.S(10), Height - bottomH - rows * cmdList.ItemHeight - Ui.S(4), Width - Ui.S(20), rows * cmdList.ItemHeight + 2);
            cmdList.Visible = true; cmdList.BringToFront();
        }

        void AcceptCmd()
        {
            var c = cmdList.Current; cmdList.Visible = false;
            if (c == null) return;
            bool needsArgs = c.Usage.Contains("[") || c.Usage.Contains("|") || c.Kind != "app";
            if (!needsArgs) { input.Clear(); RunCommand(c.Name, ""); return; }
            input.Text = c.Name + " "; input.SelectionStart = input.TextLength; input.Focus();
        }

        void OpenCommands()
        {
            using (var b = new CommandBrowser(Chat))
                if (b.ShowDialog(FindForm()) == DialogResult.OK && b.Picked != null) { input.Text = b.Picked; input.SelectionStart = input.TextLength; }
            input.Focus();
        }

        // App commands run here; agent commands go only to their agent; file and saved commands are expanded first.
        bool RunCommand(string name, string args)
        {
            var c = AgentCommands.Catalog(Chat).FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (c == null) return false;
            var hdr = header; var at = new Point(Ui.S(10), hdr.Height);
            switch (c.Kind == "app" ? name.ToLowerInvariant() : "")
            {
                case "/help": OpenCommands(); return true;
                case "/model": SetModelOrEffort(true, args); return true;
                case "/effort": SetModelOrEffort(false, args); return true;
                case "/mode":
                    if (args == "ask" || args == "edit" || args == "auto") { Chat.Mode = args; RaiseChanged(); AgentStore.Save(Chat); App.I.Status("Mode: " + args); }
                    else PickMode(hdr, at);
                    return true;
                case "/agents": PickAgents(hdr, at); return true;
                case "/folder": PickFolder(hdr, at); return true;
                case "/new": { var p = FindPage(); if (p != null) p.NewChatIn(this); return true; }
                case "/fresh": Chat.Sessions.Clear(); Chat.Seen.Clear(); Add("info", "", "Agents start new sessions from the next message."); AgentStore.Save(Chat); return true;
                case "/rename": if (args.Length > 0) { Chat.Title = args; RaiseChanged(); AgentStore.Save(Chat); } return true;
                case "/fav": Chat.Fav = !Chat.Fav; RaiseChanged(); AgentStore.Save(Chat); return true;
                case "/copy": App.I.CopyText(AgentStore.Markdown(Chat)); App.I.Status("Chat copied."); return true;
                case "/export": { var p = FindPage(); if (p != null) p.Export(Chat); return true; }
                case "/stop": Stop(); return true;
            }
            if (c.Kind == "app") return false;
            string only = c.Agent.Length > 0 && Chat.Agents.Contains(c.Agent) ? c.Agent : null;
            string text = c.Kind == "native" ? (name + " " + args).Trim() : AgentCommands.Expand(c, args);
            if (text.Length == 0) { App.I.Status("The command " + name + " is empty."); return true; }
            Send(text, only);
            return true;
        }

        // /model and /effort: a name applies to the agents that offer it; no name opens the menu of the first agent.
        async void SetModelOrEffort(bool model, string value)
        {
            var targets = Chat.Agents.Select(AgentStore.Def).Where(d => d != null && (model ? AgentModels.HasModels(d) : AgentModels.HasEffort(d))).ToList();
            if (targets.Count == 0) { App.I.Status("No agent in this chat lets you choose the " + (model ? "model" : "effort") + " from here."); return; }
            if (value.Length == 0) { AgentMenu(targets[0].Id, header, new Point(Ui.S(10), header.Height)); return; }
            var cline = model ? targets.FirstOrDefault(d => d.Id == "cline") : null;
            if (cline != null && value != "default")
            {
                if (targets.Count == 1 || value.IndexOf(": ", StringComparison.Ordinal) >= 0)
                {
                    string selection = AgentModels.CanonicalClineModel(value);
                    if (selection == null) { App.I.Status("Use /model provider: model for Cline."); return; }
                    var chat = Chat;
                    string previous = AgentModels.ChatModel(chat, cline.Id);
                    App.I.Status("Checking the Cline model catalog...");
                    var models = await AgentModels.LoadClineModels(cline);
                    if (IsDisposed || Chat != chat || AgentModels.ChatModel(chat, cline.Id) != previous) return;
                    if (!models.Contains(selection, StringComparer.Ordinal))
                    {
                        App.I.Status(models.Length == 0 ? "The Cline model catalog is unavailable." : "Cline model unavailable: " + selection);
                        return;
                    }
                    chat.Models[cline.Id] = selection;
                    Changed2();
                    App.I.Status("Model: " + selection);
                    return;
                }
                targets.Remove(cline);
            }
            foreach (var d in targets)
            {
                var dict = model ? Chat.Models : Chat.Efforts;
                bool known = model ? true : d.Efforts.Contains(value);
                if (known) dict[d.Id] = value == "default" ? "" : value;
            }
            RaiseChanged(); AgentStore.Save(Chat);
            App.I.Status((model ? "Model: " : "Effort: ") + value);
        }

        // An agent's chip: model, effort, remove.
        public void AgentMenu(string id, Control anchor, Point at)
        {
            var d = AgentStore.Def(id); if (d == null) return;
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            m.Items.Add(new ToolStripMenuItem(d.Name) { Enabled = false });
            AgentAccounts.AddMenu(m.Items, Chat, d, Busy || AgentStore.Busy.Contains(Chat.Id), Changed2, FindForm());
            string cur;
            if (id == "cline")
            {
                var chat = Chat;
                string selected = AgentModels.ChatModel(chat, id);
                m.Items.Add("Model:  " + (selected.Length > 0 ? selected : "default"), null, delegate
                {
                    string v = TeamModelPicker.Pick(FindForm(), d, selected);
                    if (v != null && Chat == chat) { chat.Models[id] = v; if (v.Length > 0) { var mine = AgentModels.Mine(d); mine.Remove(v); mine.Insert(0, v); if (mine.Count > 20) mine.RemoveRange(20, mine.Count - 20); Store.MarkSettings(); } Changed2(); }
                });
            }
            else if (AgentModels.HasModels(d))
            {
                Chat.Models.TryGetValue(id, out cur); cur = cur ?? "";
                var mm = new ToolStripMenuItem("Model:  " + (cur.Length > 0 ? cur : "default"));
                Action<string> setModel = v => { Chat.Models[id] = v; RaiseChanged(); AgentStore.Save(Chat); };
                string defM = AgentModels.DefaultModel(d);
                mm.DropDownItems.Add(new ToolStripMenuItem(defM.Length > 0 ? "App default: " + defM : "Default (the CLI's own setting)", null, delegate { setModel(""); }) { Checked = cur.Length == 0 });
                if (cur.Length > 0) mm.DropDownItems.Add("Make \"" + cur + "\" the app default for " + d.Name + " chats", null, delegate { Store.Settings.AgentDefaultModel[id] = cur; Store.MarkSettings(); setModel(""); });
                if (defM.Length > 0) mm.DropDownItems.Add("Clear the app default (use the CLI's own model)", null, delegate { Store.Settings.AgentDefaultModel.Remove(id); Store.MarkSettings(); RaiseChanged(); });
                mm.DropDownItems.Add(new ToolStripSeparator());
                var mine = AgentModels.Mine(d);
                foreach (var name in mine.Take(40)) { string v = name; mm.DropDownItems.Add(new ToolStripMenuItem("★  " + v, null, delegate { setModel(v); }) { Checked = cur == v }); }
                if (mine.Count > 0) mm.DropDownItems.Add(new ToolStripSeparator());
                var models = AgentModels.List(d, null).Where(x => !mine.Contains(x) && !AgentModels.IsDead(d, x)).ToList();
                foreach (var name in models.Take(60)) { string v = name; mm.DropDownItems.Add(new ToolStripMenuItem(v, null, delegate { setModel(v); }) { Checked = cur == v }); }
                if (cur.Length > 0 && !models.Contains(cur)) mm.DropDownItems.Add(new ToolStripMenuItem(cur, null, delegate { setModel(cur); }) { Checked = true });
                if (AgentModels.Loading(d)) mm.DropDownItems.Add(new ToolStripMenuItem("Loading the full list from " + d.Name + "...") { Enabled = false });
                mm.DropDownItems.Add(new ToolStripSeparator());
                mm.DropDownItems.Add("Type a model name...", null, delegate { var v = Ui.Ask("Model for " + d.Name, "Model", cur, FindForm()); if (v != null) { setModel(v.Trim()); AgentModels.Remember(d, v); } });
                mm.DropDownItems.Add("Your models (paste a list)...", null, delegate
                {
                    var v = AskLines.Show("Your models for " + d.Name, "One model per line. They are shown first (★) in this menu.", string.Join("\r\n", AgentModels.Mine(d)), FindForm());
                    if (v == null) return;
                    var l = AgentModels.Mine(d); l.Clear();
                    foreach (var line in v.Replace("\r", "").Split(new[] { '\n' }).Select(x => x.Trim()).Where(x => x.Length > 0)) if (!l.Contains(line)) l.Add(line);
                    Store.MarkSettings();
                });
                if (!string.IsNullOrEmpty(d.ModelsCmd)) mm.DropDownItems.Add("Reload the list from " + d.Name, null, delegate { AgentModels.Forget(d); AgentModels.List(d, () => App.I.Status(d.Name + ": model list loaded.")); });
                m.Items.Add(mm);
            }
            else m.Items.Add(new ToolStripMenuItem("Model: set in " + d.Name + "'s own settings") { Enabled = false });
            if (AgentModels.HasEffort(d))
            {
                Chat.Efforts.TryGetValue(id, out cur); cur = cur ?? "";
                var em = new ToolStripMenuItem("Effort:  " + (cur.Length > 0 ? cur : "default"));
                Action<string> setEffort = v => { Chat.Efforts[id] = v; RaiseChanged(); AgentStore.Save(Chat); };
                string defE = AgentModels.DefaultEffort(d);
                em.DropDownItems.Add(new ToolStripMenuItem(defE.Length > 0 ? "App default: " + defE : "Default", null, delegate { setEffort(""); }) { Checked = cur.Length == 0 });
                if (cur.Length > 0) em.DropDownItems.Add("Make \"" + cur + "\" the app default", null, delegate { Store.Settings.AgentDefaultEffort[id] = cur; Store.MarkSettings(); setEffort(""); });
                foreach (var lv in d.Efforts) { string v = lv; em.DropDownItems.Add(new ToolStripMenuItem(v, null, delegate { setEffort(v); }) { Checked = cur == v }); }
                m.Items.Add(em);
            }
            m.Items.Add(new ToolStripSeparator());
            int idx = Chat.Agents.IndexOf(id);
            m.Items.Add(new ToolStripMenuItem(Chat.Off.Contains(id) ? "Answers: off (click to turn on)" : "Answers: on (click to turn off)", null, delegate { ToggleOff(id); }));
            if (idx > 0) m.Items.Add("Answer earlier (move left)", null, delegate { MoveAgent(id, -1); });
            if (idx >= 0 && idx < Chat.Agents.Count - 1) m.Items.Add("Answer later (move right)", null, delegate { MoveAgent(id, 1); });
            if (Chat.Agents.Count > 1) m.Items.Add("Only " + d.Name + " answers", null, delegate { Chat.Off.Clear(); Chat.Off.AddRange(Chat.Agents.Where(a => a != id)); Changed2(); });
            if (Chat.Agents.Count > 1) m.Items.Add("Remove " + d.Name + " from this chat", null, delegate { if (!Busy) { Chat.Agents.Remove(id); Chat.Off.Remove(id); Changed2(); } });
            m.Items.Add("Add or remove agents...", null, delegate { PickAgents(anchor, at); });
            m.Show(anchor, at);
            // Start loading the live model list now, so it is there next time.
            if (id != "cline" && AgentModels.HasModels(d)) AgentModels.List(d, null);
        }

        public void ToggleOff(string id) { if (!Chat.Off.Remove(id)) Chat.Off.Add(id); Changed2(); }

        // The chosen model no longer exists: switch to the replacement the error names, else to the CLI's default.
        void ModelGone(string agentId, CouncilMember member, string text)
        {
            var d = AgentStore.Def(agentId); if (d == null) return;
            string cur;
            if (member != null) cur = member.Model; else Chat.Models.TryGetValue(agentId, out cur);
            if (string.IsNullOrEmpty(cur)) { Add("info", "", d.Name + ": its default model is not available. Click the " + d.Name + " chip > Model and pick another one (or add yours with \"Your models\")."); return; }
            string repl = AgentModels.Retire(d, cur, text);
            if (member != null) member.Model = repl; else Chat.Models[agentId] = repl;
            Add("info", "", d.Name + ": the model \"" + cur + "\" is no longer available. " + (repl.Length > 0
                ? "This chat now uses its replacement \"" + repl + "\" (also added to your models). Send your message again."
                : "This chat now uses " + d.Name + "'s default model. Pick another one in the " + d.Name + " chip if you like."));
            RaiseChanged(); AgentStore.Save(Chat);
        }
        public void MoveAgent(string id, int d)
        {
            int i = Chat.Agents.IndexOf(id), j = i + d;
            if (i < 0 || j < 0 || j >= Chat.Agents.Count || Busy) return;
            Chat.Agents.RemoveAt(i); Chat.Agents.Insert(j, id); Changed2();
        }
        public void ToggleSeparate() { Chat.Separate = !Chat.Separate; Changed2(); App.I.Status(Chat.Separate ? "Separate answers: each agent answers only your message." : "Shared answers: each agent also sees the answers before it."); }
        public void Changed2() { RaiseChanged(); strip.Invalidate(); view.Invalidate(); AgentStore.Save(Chat); }

        public void InsertText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string cur = input.Text;
            input.Text = cur.Length == 0 ? text : cur.TrimEnd() + "\r\n\r\n" + text;
            input.SelectionStart = input.TextLength;
            FocusInput();
        }
        void SendInput()
        {
            string text = input.Text.Trim();
            if (text.StartsWith("/") && !Busy)
            {
                int sp = text.IndexOfAny(new[] { ' ', '\n' });
                string name = sp < 0 ? text : text.Substring(0, sp), args = sp < 0 ? "" : text.Substring(sp + 1).Trim();
                if (RunCommand(name, args)) { input.Clear(); cmdList.Visible = false; return; }
            }
            if (Busy && (text.Length > 0 || attachments.Count > 0)) { SteerOrQueue(text); UpdateSend(); return; }
            if (text.Length == 0 && attachments.Count > 0) text = "See the attached files.";
            if (text.Length > 0 && OutgoingFilter != null) text = OutgoingFilter(text);
            if (text.Length == 0 || Busy || AgentStore.Busy.Contains(Chat.Id)) { if (Chat != null && AgentStore.Busy.Contains(Chat.Id)) App.I.Status("This chat is being answered through MCP. Wait for it to finish."); return; }
            int countBeforeSend = Chat.Messages.Count;
            nextFiles = attachments.ToList();
            Send(text);
            nextFiles = null;
            if (Chat.Messages.Skip(countBeforeSend).Any(m => m.Role == "user")) { input.Clear(); attachments.Clear(); RefreshAttachBar(); }
            strip.Invalidate();
        }

        // Adds your message and lets every agent of the chat answer in turn.
        public void Send(string text) { Send(text, null); }
        public void Send(string text, string onlyAgent)
        {
            if (Busy || string.IsNullOrWhiteSpace(text)) return;
            var c = Chat;
            if (AgentStore.Busy.Contains(c.Id)) { App.I.Status("This chat is being answered through MCP. Wait for it to finish."); return; }
            if (c.Council && onlyAgent == null) { SendCouncil(text); return; }
            if (c.Agents.Count == 0) c.Agents.Add(AgentStore.Defs[0].Id);
            var who = onlyAgent != null ? new List<string> { onlyAgent } : Responders(ref text);
            if (who.Count == 0) { App.I.Status("No agent is turned on to answer. Click an agent above the message box to turn it on."); return; }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (c.Folder.Length == 0 || !Directory.Exists(c.Folder))
            {
                var recent = MetaStore.Recent("folders", 1).FirstOrDefault();
                c.Folder = recent != null && Directory.Exists(recent.Id) ? recent.Id : Store.Settings.TermRecent.FirstOrDefault(Directory.Exists) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (c.Title.Length == 0) c.Title = ChatView.ShownText(text).Replace("\r", " ").Replace("\n", " ").Trim();
            if (c.Title.Length > 70) c.Title = c.Title.Substring(0, 70) + "...";
            if (!AgentStore.Chats.Contains(c)) AgentStore.Chats.Add(c);
            AddUser(text);
            AgentStore.Save(c);
            MetaStore.Touch("folders", c.Folder);
            queue.Clear();
            retried.Clear(); netRetry = null;
            fallbackTried.Clear(); fallbackAccountOverride = null; fallbackModelOverride = null;
            foreach (var a in who) queue.Enqueue(a);
            AgentStore.Busy.Add(c.Id);
            send.Text = "Stop";
            tick.Start();
            RunNext();
        }

        // Your message, with the attachments chosen for it (nextFiles) listed at the end so every agent can open them.
        void AddUser(string text)
        {
            var files = nextFiles != null && nextFiles.Count > 0 ? nextFiles.ToList() : null;
            Add("user", "", text + AttachNote(files));
            Chat.Messages[Chat.Messages.Count - 1].Files = files;
        }
        // The attachments of the newest user message (for --image / --add-dir).
        List<string> LatestFiles()
        {
            for (int i = Chat.Messages.Count - 1; i >= 0; i--)
                if (Chat.Messages[i].Role == "user") return Chat.Messages[i].Files == null ? new List<string>() : Chat.Messages[i].Files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
            return new List<string>();
        }
        void Add(string role, string agent, string text, string label = "")
        {
            Chat.Messages.Add(new AgentMsg { Role = role, Agent = agent ?? "", Label = label, Text = text ?? "", Time = Store.Now() });
            Chat.Updated = Store.Now();
            if (role != "user" && !checkpoint.Enabled) checkpoint.Start();
            view.Relayout(role == "user");
        }

        // What an agent has not seen yet: your new message, plus what the other agents answered since its last turn.
        string BuildPrompt(string agentId) { return AgentStore.PromptFor(Chat, agentId); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); AgentStore.ChatUpdated += OnChatUpdated; AgentLimits.Updated += OnLimitsUpdated; ChatView.ZoomChanged += OnZoom; UpdateUsage(); }
        protected override void OnHandleDestroyed(EventArgs e) { AgentStore.ChatUpdated -= OnChatUpdated; AgentLimits.Updated -= OnLimitsUpdated; ChatView.ZoomChanged -= OnZoom; base.OnHandleDestroyed(e); }
        // The chat was answered elsewhere (MCP agent_ask): show the new messages.
        void OnChatUpdated(AgentChat c) { if (c == Chat && run == null) { view.Relayout(true); RaiseChanged(); } }

        void RunNext()
        {
            if (queue.Count == 0) { Done(); return; }
            string id = queue.Dequeue();
            var def = AgentStore.Def(id);
            if (def == null) { Add("error", id, "Unknown agent \"" + id + "\" (not in agents.json)."); RunNext(); return; }
            string prompt = BuildPrompt(id);
            if (Chat.Web) prompt = AgentRun.WebNote + prompt;
            var r = new AgentRun();
            if (fallbackAccountOverride != null) { r.AccountOverride = fallbackAccountOverride; fallbackAccountOverride = null; }
            if (fallbackModelOverride != null) { r.ModelOverride = fallbackModelOverride; fallbackModelOverride = null; }
            r.Attach = LatestFiles();
            r.Usage += u => Ui0(() => OnUsage(u));
            runAgent = id; run = r;
            bool gotText = false;
            string lastErr = null;
            int stepSeq = 0;
            int turnIdx = Chat != null && Chat.Messages != null ? Chat.Messages.Count : 0;
            view.Working = def.Name + " is working"; view.WorkingSince = Store.Now(); view.LastTool = null; view.Relayout(false);
            r.Event += (kind, text) => Ui0(() =>
            {
                if (r != run) return;
                if (kind == "session") { Chat.Sessions[r.ActiveSessionKey] = text; return; }
                if (kind == "tool")
                {
                    if (text == "system message" || string.IsNullOrWhiteSpace(text)) return;
                    view.LastTool = text; Add("tool", id, text);
                    if (AssistantHarness.IsAssistant(Chat))
                    {
                        string cid = Chat.Id; int sIdx = ++stepSeq; string toolLine = text;
                        ThreadPool.QueueUserWorkItem(delegate { try { SqliteVault.RecordStep(cid, turnIdx, sIdx, "tool_call", id, toolLine, Store.Now()); } catch (Exception) { } });
                    }
                    return;
                }
                if (kind == "text") gotText = true;
                if (kind == "error") lastErr = text;
                Add(kind == "text" ? "agent" : kind, id, text);
                if (kind == "text" && Chat.Messages.Count > 0) Chat.Messages[Chat.Messages.Count - 1].Model = r.UsedLabel;
                if (text != null && (text.Contains("routing discovery timed out") || text.Contains("account/read failed"))) netRetry = id;
                if (AgentModels.IsModelGone(text)) ModelGone(id, null, text);
                else if (text != null && (text.Contains("headless mode cannot prompt") || text.Contains("--dangerously-skip-permissions")))
                    Add("info", "", AgentStore.NameOf(id) + " is not allowed to run commands in " + (Chat.Mode == "ask" ? "Ask" : "Edit") + " mode. To let it, click the mode chip in the header and choose Full auto (only in folders you trust).");
            });
            r.Exited += code => Ui0(() =>
            {
                if (r != run) return;
                // "Seen" moves on only when this turn is final: a retry must resend the same message, not an empty
                // prompt (the agent never got an answer through).
                int seenUpTo = Chat.Messages.Count;
                string usedAccount = r.Account ?? "";
                string usedModel = r.ModelOverride;
                if (string.IsNullOrEmpty(usedModel) && Chat.Models != null) Chat.Models.TryGetValue(id, out usedModel);
                run = null; runAgent = null;
                AgentStore.Save(Chat);
                if (!gotText && !string.IsNullOrEmpty(lastErr) && AgentFallback.IsRetryableError(lastErr) && !fallbackTried.Contains(id))
                {
                    string nextAcc, nextModel;
                    if (AgentFallback.PickAlternate(def, Chat, usedAccount, usedModel, out nextAcc, out nextModel))
                    {
                        fallbackTried.Add(id);
                        netRetry = null;
                        fallbackAccountOverride = nextAcc;
                        fallbackModelOverride = nextModel;
                        string targetDesc = nextAcc != null ? ("account \"" + (nextAcc.Length == 0 ? "CLI default" : nextAcc) + "\"") : ("model \"" + nextModel + "\"");
                        App.Log("Auto-fallback for " + def.Name + ": switching to " + targetDesc);
                        Add("info", "", def.Name + " hit a rate/transient limit. Automatically retrying once on " + targetDesc + "...");
                        var rest = queue.ToList(); queue.Clear(); queue.Enqueue(id); foreach (var q in rest) queue.Enqueue(q);
                        RunNext();
                        return;
                    }
                }
                if (netRetry == id && !retried.Contains(id) && !gotText)
                {
                    retried.Add(id); netRetry = null;
                    Add("info", "", AgentStore.NameOf(id) + " could not reach its server in time. Trying once more...");
                    var rest = queue.ToList(); queue.Clear(); queue.Enqueue(id); foreach (var q in rest) queue.Enqueue(q);
                    RunNext();
                    return;
                }
                Chat.Seen[r.ActiveSessionKey] = seenUpTo;
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
            if (Chat != null) AgentStore.Busy.Remove(Chat.Id);
            runAgent = null; run = null;
            view.Working = null; view.LastTool = null; view.Relayout(false);
            send.Text = "Send"; tick.Stop(); checkpoint.Stop();
            AgentStore.Save(Chat);
            RaiseChanged();
            UpdateUsage();
            // Messages typed while the agents worked go out now, as one message.
            if (queued.Count > 0) { BeginInvoke(new Action(() => { SendQueued(); UpdateSend(); })); return; }
            UpdateSend();
            var f = FindForm();
            if (f != null && !f.ContainsFocus) App.I.Balloon("Agents", (Chat.Title.Length > 0 ? Chat.Title : "Chat") + ": answered.");
        }
        // ---------- model council ----------
        void EnsureChat(string text)
        {
            var c = Chat;
            if (c.Folder.Length == 0 || !Directory.Exists(c.Folder))
            {
                var recent = MetaStore.Recent("folders", 1).FirstOrDefault();
                c.Folder = recent != null && Directory.Exists(recent.Id) ? recent.Id : Store.Settings.TermRecent.FirstOrDefault(Directory.Exists) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            if (c.Title.Length == 0) c.Title = ChatView.ShownText(text).Replace("\r", " ").Replace("\n", " ").Trim();
            if (c.Title.Length > 70) c.Title = c.Title.Substring(0, 70) + "...";
            if (!AgentStore.Chats.Contains(c)) AgentStore.Chats.Add(c);
        }

        void SendCouncil(string text)
        {
            var c = Chat;
            var members = c.Members.Where(m => !m.Off && AgentStore.Def(m.Agent) != null).ToList();
            if (members.Count == 0) { App.I.Status("No council member is turned on. Click the council chip to set it up."); EditCouncil(); return; }
            if (c.LeaderTeam && members.Count < 2) { App.I.Status("Choose a leader and at least one worker."); EditCouncil(); return; }
            EnsureChat(text);
            AddUser(text);
            AgentStore.Save(c);
            MetaStore.Touch("folders", c.Folder);
            AgentStore.Busy.Add(c.Id); councilBusy = true;
            send.Text = "Stop"; tick.Start();
            if (c.LeaderTeam) StartLeaderTeam(members, text);
            else CouncilRound(members, text, new Dictionary<string, string>(), 0, Math.Max(0, Math.Min(4, c.Rounds)));
            RaiseChanged();
        }

        // Round 0: everyone answers alone (all at the same time). Rounds 1..n: each reads the others and improves. Then the verdict.
        void CouncilRound(List<CouncilMember> members, string q, Dictionary<string, string> last, int round, int total)
        {
            if (!councilBusy) return;
            if (round > total || (round > 0 && members.Count < 2)) { CouncilVerdict(members, q, last); return; }
            Add("info", "", round == 0 ? "Council · everyone answers on their own" : "Council · discussion round " + round + " of " + total);
            var snapshot = new Dictionary<string, string>(last);
            int pending = members.Count;
            view.Working = "Council: " + members.Count + " members thinking"; view.WorkingSince = Store.Now(); view.Relayout(false);
            foreach (var m in members)
            {
                var mm = m;
                string prompt = round == 0 ? Council.FirstRound(mm, Chat, q) : Council.Discussion(mm, Chat, q, snapshot, round);
                if (round == 0 && Chat.BranchPrefix > 0 && !Chat.Sessions.ContainsKey(AgentAccounts.Session("council:" + mm.Key, string.IsNullOrEmpty(mm.Account) ? AgentAccounts.Selected(Chat, mm.Agent) : mm.Account)))
                    prompt = AgentStore.History(Chat, mm.Agent, Chat.BranchPrefix) + prompt;
                if (Chat.Web) prompt = AgentRun.WebNote + prompt;
                StartMember(mm, prompt, (round == 0 ? "" : "Round " + round + " · ") + mm.Label, answer =>
                {
                    if (!string.IsNullOrWhiteSpace(answer)) last[mm.Key] = answer;
                    else if (last.ContainsKey(mm.Key)) Add("info", "", mm.Label + " did not finish this round. The earlier answer is retained.");
                    pending--;
                    view.Working = "Council · " + (members.Count - pending) + " of " + members.Count + " finished";
                    view.InvalidateWorking();
                    if (pending == 0)
                    {
                        var respondents = members.Where(member => last.ContainsKey(member.Key)).ToList();
                        if (respondents.Count < members.Count)
                            Add("info", "", "No answer from " + string.Join(", ", members.Except(respondents).Select(member => member.Label)) + ".");
                        if (respondents.Count < 2) CouncilVerdict(respondents, q, last);
                        else CouncilRound(respondents, q, last, round + 1, total);
                    }
                });
            }
        }

        void CouncilVerdict(List<CouncilMember> members, string q, Dictionary<string, string> last)
        {
            if (!councilBusy) return;
            if (members.Count < 2 || last.Count == 0)
            {
                Add("info", "", last.Count == 0 ? "Council finished without an answer." : "Only " + members[0].Label + " answered. See the individual response above.");
                CouncilDone(); return;
            }
            var chair = members.FirstOrDefault(m => m.Key == Chat.Chair) ?? members[0];
            Add("info", "", "Council · verdict by " + chair.Label);
            view.Working = "Writing the verdict"; view.WorkingSince = Store.Now(); view.Relayout(false);
            StartMember(chair, AgentStore.DocumentPrompt(Chat, Council.Verdict(Chat, q, last)), "Verdict · " + chair.Label, a =>
            {
                if (string.IsNullOrWhiteSpace(a)) Add("info", "", "The chair could not finish the verdict. The member answers are available above.");
                CouncilDone();
            });
        }

        void StartMember(CouncilMember m, string prompt, string label, Action<string> done, TeamWorker worker = null, string teamSession = null, bool hideReply = false)
        {
            var d = AgentStore.Def(m.Agent);
            var r = new AgentRun { ModelOverride = m.Model ?? "", EffortOverride = m.Effort ?? "", AccountOverride = string.IsNullOrEmpty(m.Account) ? null : m.Account, SessionKey = teamSession ?? ("council:" + m.Key + (label.StartsWith("Verdict") ? ":chair" : "")) };
            var answer = new StringBuilder();
            bool failed = false;
            r.Attach = LatestFiles();
            r.Usage += u => Ui0(() => OnUsage(u));
            councilRuns.Add(r);
            if (worker != null) { worker.Run = r; worker.ClearLive(); r.PlainLine = worker.LiveLine; }
            r.Event += (kind, t) => Ui0(() =>
            {
                if (!councilRuns.Contains(r)) return;
                if (kind == "session") { Chat.Sessions[r.ActiveSessionKey] = t; return; }
                if (worker != null) worker.Append(kind, t);
                if (kind == "tool") { view.LastTool = m.Label + ": " + t; view.InvalidateWorking(); return; }   // many members: keep the transcript readable
                if (kind == "text") { answer.Append(t).Append('\n'); if (hideReply) return; }
                if (kind == "error") failed = true;
                if (AgentModels.IsModelGone(t)) ModelGone(m.Agent, m, t);
                Chat.Messages.Add(new AgentMsg { Role = kind == "text" ? "agent" : kind, Agent = m.Agent, Label = label, Text = t ?? "", Time = Store.Now(), Model = kind == "text" ? r.UsedLabel : "" });
                Chat.Updated = Store.Now();
                view.Relayout(false);
            });
            r.Exited += code => Ui0(() =>
            {
                if (!councilRuns.Remove(r)) return;
                AgentStore.Save(Chat);
                done(code == 0 && !failed ? answer.ToString().Trim() : null);
            });
            string err = "Unknown agent";
            string runPrompt = AssistantHarness.IsAssistant(Chat) ? AssistantHarness.WrapTeamPromptLean(Chat, prompt, worker != null) : prompt;
            if (d == null || !r.Start(d, Chat, runPrompt, out err)) { councilRuns.Remove(r); Add("error", m.Agent, m.Label + ": " + err); done(null); return; }
            MetaStore.Touch("agents", m.Agent);
        }

        void CouncilDone()
        {
            if (!councilBusy) return;
            councilBusy = false; councilRuns.Clear();
            Done();
        }

        public void SetAssistantMode(string mode)
        {
            if (Chat == null) return;
            if (Busy) { if (App.I != null) App.I.Status("Wait for the current turn to finish before changing mode."); return; }
            mode = (mode ?? "solo").Trim().ToLowerInvariant();
            if (mode == "solo")
            {
                Chat.Council = false;
                Chat.LeaderTeam = false;
            }
            else if (mode == "council" || mode == "leader")
            {
                if (Chat.Members == null) Chat.Members = new List<CouncilMember>();
                if (Chat.Members.Count == 0) Chat.Members.AddRange(Council.Starter());
                if (mode == "leader")
                {
                    var activeMembers = Chat.Members.Where(m => !m.Off && AgentStore.Def(m.Agent) != null).ToList();
                    if (activeMembers.Count == 1)
                        Chat.Members.Add(new CouncilMember { Agent = activeMembers[0].Agent, Role = "Engineer" });
                }
                Chat.Council = true;
                Chat.LeaderTeam = mode == "leader";
            }
            else return;
            header.Invalidate();
            if (strip != null) strip.Invalidate();
            view.Invalidate();
            AgentStore.Save(Chat);
            RaiseChanged();
        }

        public void EditCouncil() { ConfigureTeam(Chat.LeaderTeam); }
        public void ConfigureTeam(bool leader)
        {
            if (councilBusy) { App.I.Status("Council settings are available when this turn finishes."); return; }
            using (var dlg = new CouncilDialog(Chat, leader))
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { RaiseChanged(); strip.Invalidate(); view.Invalidate(); AgentStore.Save(Chat); }
        }

        // ---------- collaboration from a message ----------
        void OnCollab(string kind, string text)
        {
            if (kind == "edit")
            {
                if (input.TextLength > 0) input.AppendText(Environment.NewLine + Environment.NewLine);
                int start = input.TextLength;
                input.AppendText(text); input.Select(start, input.TextLength - start); input.Focus();
            }
            else if (kind == "quote")
            {
                string q = string.Join("\n", text.Replace("\r", "").Split('\n').Take(40).Select(l => "> " + l));
                input.Text = q + "\n\n" + input.Text; input.SelectionStart = input.TextLength; input.Focus();
            }
            else if (kind == "ask")
            {
                var m = Ui.Menu(); Ui.ThemeMenu(m);
                foreach (var d in AgentStore.Defs)
                {
                    var dd = d;
                    m.Items.Add(dd.Name + (Chat.Agents.Contains(dd.Id) ? "" : "  (adds it to this chat)"), Ui.Swatch(AgentStore.ColorOf(dd.Id), Ui.S(12)), delegate
                    {
                        if (Busy) return;
                        if (!Chat.Agents.Contains(dd.Id)) { Chat.Agents.Add(dd.Id); Chat.Off.Add(dd.Id); }
                        Send("Please review this answer from the chat. Say what is right, what is wrong, and what you would do instead:\n\n" + text, dd.Id);
                    });
                }
                m.Show(Cursor.Position);
            }
            else if (kind == "panes") { var p = FindPage(); if (p != null) p.SendToOthers(this, "Here is an answer from another chat. Read it and tell me what you think, then continue with your own view:\n\n" + text); }
        }
        public bool IsEmpty { get { return Chat == null || !Chat.HasContent; } }

        public void Stop()
        {
            if (councilBusy)
            {
                CancelTeamTurn();
                councilBusy = false;
                foreach (var cr in councilRuns.ToList()) { var x = cr; ThreadPool.QueueUserWorkItem(delegate { try { x.Stop(); } catch { } }); }
                councilRuns.Clear();
                Done();
                return;
            }
            queue.Clear();
            var r = run;
            if (r != null) ThreadPool.QueueUserWorkItem(delegate { r.Stop(); });
            else Done();
        }
        void Ui0(Action a) { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { } }

        public void FocusInput() { input.Focus(); }
        // Hosts (the Markdown page) can add context to every message you send, and send text themselves.
        public Func<string, string> OutgoingFilter;
        public void SendText(string text) { if (Busy || string.IsNullOrWhiteSpace(text)) return; input.Text = text; SendInput(); }
        public string LastAnswer { get { var m = Chat == null ? null : Chat.Messages.LastOrDefault(x => x.Role == "agent"); return m == null ? "" : m.Text; } }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (checkpoint.Enabled) AgentStore.Save(Chat); checkpoint.Dispose(); ResetTeamDesk(); tick.Dispose(); var r = run; if (r != null) { queue.Clear(); try { r.Stop(); } catch { } } foreach (var cr in councilRuns.ToList()) try { cr.Stop(); } catch { } }
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
            m.Items.Add("Click to add or remove an agent. Several answer in turn.").Enabled = false;
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

        // Outside the Agents page (the Assistant) the host says how to start a new chat here.
        public Action NewChatRequested;
        void NewHere(AgentsPage page) { if (page != null) page.NewChatIn(this); else if (NewChatRequested != null) NewChatRequested(); }
        public void ChatMenu(Control anchor, Point at, AgentsPage page)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            m.Items.Add("New chat in this pane", null, delegate { NewHere(page); });
            m.Items.Add("Find in this chat  (Ctrl+F)", null, delegate { OpenFind(); });
            if (Chat.Council || Chat.Messages.Any(x => x.Label.StartsWith("Verdict · ", StringComparison.Ordinal)))
                m.Items.Add("Jump to latest verdict", null, delegate { view.JumpToVerdict(); });
            m.Items.Add("Rename chat...", null, delegate { var v = Ui.Ask("Chat name", "Name", Chat.Title, FindForm()); if (v != null) { Chat.Title = v.Trim(); RaiseChanged(); AgentStore.Save(Chat); } });
            m.Items.Add(Chat.Fav ? "Remove from favorites" : "Add to favorites", null, delegate { Chat.Fav = !Chat.Fav; RaiseChanged(); AgentStore.Save(Chat); });
            m.Items.Add("Copy chat as Markdown", null, delegate { App.I.CopyText(AgentStore.Markdown(Chat)); });
            if (page != null) m.Items.Add("Export chat...", null, delegate { page.Export(Chat); });
            m.Items.Add("Start fresh sessions (agents forget earlier turns)", null, delegate { Chat.Sessions.Clear(); Chat.Seen.Clear(); Add("info", "", "Agents start new sessions from the next message."); AgentStore.Save(Chat); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(Chat.Council ? "Model council: on (click to turn off)" : "Model council: turn on", null, delegate { if (!Chat.Council && Chat.Members.Count == 0) EditCouncil(); else { Chat.Council = !Chat.Council; RaiseChanged(); AgentStore.Save(Chat); view.Invalidate(); } }) { Checked = Chat.Council });
            m.Items.Add("Team settings…", null, delegate { EditCouncil(); });
            if (Chat.LeaderTeam) m.Items.Add("Leader workspace…", null, delegate { OpenLeaderTeam(); });
            m.Items.Add(new ToolStripSeparator());
            var lastUser = Chat.Messages.LastOrDefault(x => x.Role == "user");
            var lastAnswer = Chat.Messages.LastOrDefault(x => x.Role == "agent");
            if (lastUser != null) m.Items.Add("Ask the last question again", null, delegate { if (!Busy) Send(lastUser.Text); });
            if (lastAnswer != null)
            {
                m.Items.Add("Copy the last answer", null, delegate { App.I.CopyText(lastAnswer.Text); App.I.Status("Answer copied."); });
                m.Items.Add("Send the last answer to the other panes", null, delegate { OnCollab("panes", lastAnswer.Text); });
                m.Items.Add("Ask another agent about the last answer...", null, delegate { OnCollab("ask", lastAnswer.Text); });
            }
            m.Items.Add(new ToolStripMenuItem("Web search (agents search the web and GitHub)", null, delegate { Chat.Web = !Chat.Web; Changed2(); }) { Checked = Chat.Web });
            m.Items.Add("Duplicate this chat (same setup, no messages)", null, delegate
            {
                var c = AgentStore.CopyThrough(Chat, 0);
                if (page != null) { var p = page.EmptyPaneOr(this); p.SetChat(c); } else if (!Busy) SetChat(c);
            });
            if (Directory.Exists(Chat.Folder)) { string cf = Chat.Folder; m.Items.Add("Open a terminal in the folder", null, delegate { Term.Open(cf, ""); }); }
            m.Items.Add("Clear the messages (keep the setup)", null, delegate { if (Busy) return; if (MessageBox.Show(FindForm(), "Clear all messages of this chat?", "Agents", MessageBoxButtons.OKCancel) != DialogResult.OK) return; Chat.Messages.Clear(); Chat.BranchPrefix = 0; Chat.Seen.Clear(); Chat.Sessions.Clear(); view.SetChat(Chat); AgentStore.Save(Chat); RaiseChanged(); });
            m.Items.Add("Delete this chat...", null, delegate { if (Busy) return; if (MessageBox.Show(FindForm(), "Delete this chat for good?", "Agents", MessageBoxButtons.OKCancel) != DialogResult.OK) return; AgentStore.Delete(Chat); NewHere(page); });
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
            Rectangle Chip(Graphics g, ref int x, ref int y, string glyph, string text, Color dot, string key, int maxW)
            {
                int w = Math.Min(Math.Min(maxW, Width - Ui.S(20)), TextRenderer.MeasureText(text, Ui.Small).Width + Ui.S(glyph != null || !dot.IsEmpty ? 34 : 18));
                if (x > Ui.S(10) && x + w > Width - Ui.S(10)) { x = Ui.S(10); y += Ui.S(28); }   // wrap: chips flow onto the next row
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
                if (c.Council) Chip(g, ref x, ref y, "\uE716", (c.LeaderTeam ? "Leader team · " + c.Members.Count(z => !z.Off) + " members" : "Model council · " + c.Members.Count(z => !z.Off) + " members · " + c.Rounds + (c.Rounds == 1 ? " round" : " rounds")) + (pane.councilBusy ? "  •" : ""), Color.Empty, "council", Ui.S(320));
                else foreach (var a in c.Agents) Chip(g, ref x, ref y, null, AgentStore.NameOf(a) + (AgentAccounts.Selected(c, a).Length > 0 ? " [" + AgentAccounts.Selected(c, a) + "]" : "") + AgentModels.Label(c, a) + (pane.runAgent == a ? "  •" : ""), AgentStore.ColorOf(a), "agent:" + a, Ui.S(260));
                if (!c.Council) Chip(g, ref x, ref y, "", c.Agents.Count == 0 ? "Agent" : "", Color.Empty, "agents", Ui.S(80));
                string folder = c.Folder.Length > 0 ? Path.GetFileName(c.Folder.TrimEnd('\\')) : "Folder";
                if (folder.Length == 0) folder = c.Folder;
                Chip(g, ref x, ref y, "", folder, Color.Empty, "folder", maxW);
                Chip(g, ref x, ref y, c.Mode == "ask" ? "" : c.Mode == "auto" ? "" : "", c.Mode == "ask" ? "Ask" : c.Mode == "auto" ? "Full auto" : "Edit", Color.Empty, "mode", Ui.S(120));
                using (var p = new Pen(Ui.Line)) g.DrawLine(p, Ui.S(8), Height - 1, Width - Ui.S(8), Height - 1);
                int need = y + Ui.S(32);
                if (need != Height) BeginInvoke(new Action(() => { if (!IsDisposed) Height = need; }));
            }
            string KeyAt(Point p) { var h = hots.LastOrDefault(x => x.R.Contains(p)); return h != null ? h.Key : null; }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                string k = KeyAt(e.Location);
                if (k != hot)
                {
                    hot = k; Invalidate(); Cursor = k != null ? Cursors.Hand : Cursors.Default;
                    tip.SetToolTip(this, k == "folder" ? pane.Chat.Folder : k != null && k.StartsWith("agent:") ? "Model, effort and more for this agent" : k == "agents" ? "Add or remove agents (several answer in turn)" : k == "mode" ? "What agents may do" : k == "new" ? "New chat in this pane" : k == "menu" ? "Chat options" : "");
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
                if (h.Key.StartsWith("agent:")) { pane.AgentMenu(h.Key.Substring(6), this, at); return; }
                if (h.Key == "council") { pane.EditCouncil(); return; }
                switch (h.Key)
                {
                    case "agents": pane.PickAgents(this, at); break;
                    case "folder": pane.PickFolder(this, at); break;
                    case "mode": pane.PickMode(this, at); break;
                    case "new": pane.NewHere(page); break;
                    case "menu": pane.ChatMenu(this, at, page); break;
                }
            }
        }
        AgentsPage FindPage() { Control c = Parent; while (c != null && !(c is AgentsPage)) c = c.Parent; return c as AgentsPage; }

        // Above the message box: "Answer:" 1 Claude  2 Codex ... (click = on/off, right-click = order),
        // Shared / Separate answers, and the expand button.
        class TargetStrip : Control
        {
            readonly ChatPane pane;
            class Hot { public Rectangle R; public string Key; }
            readonly List<Hot> hots = new List<Hot>();
            string hot;
            readonly ToolTip tip = new ToolTip();
            public TargetStrip(ChatPane pane)
            {
                this.pane = pane; Height = Ui.S(32);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Ui.Bg); g.SmoothingMode = SmoothingMode.AntiAlias; hots.Clear();
                var c = pane.Chat; if (c == null) return;
                var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                int y = Ui.S(4), h = Ui.S(24);
                var expR = new Rectangle(Width - Ui.S(30), y, Ui.S(28), h);
                if (hot == "expand") using (var p = Ui.Round(expR, Ui.S(6))) using (var b = new SolidBrush(Ui.HoverBg)) g.FillPath(b, p);
                TextRenderer.DrawText(g, "", Ui.Icon(9.5f), expR, Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                hots.Add(new Hot { R = expR, Key = "expand" });
                int x = 0;
                TextRenderer.DrawText(g, "Answer:", Ui.Small, new Rectangle(x, y, Ui.S(52), h), Ui.Muted, flags);
                x += Ui.S(54);
                int n = 0, right = expR.X - Ui.S(8);
                string sepText = c.Separate ? "Separate answers" : "Shared answers";
                int sepW = TextRenderer.MeasureText(sepText, Ui.Small).Width + Ui.S(30);
                int rowY = y;
                if (c.Council)
                {
                    foreach (var mb in c.Members)
                    {
                        string label = (mb.Off ? "" : "✓ ") + mb.Label;
                        int w = Math.Min(Ui.S(260), TextRenderer.MeasureText(label, Ui.Small).Width + Ui.S(28));
                        if (x > Ui.S(54) && x + w > right) { x = Ui.S(54); y += Ui.S(28); }
                        var r = new Rectangle(x, y, w, h); var col = AgentStore.ColorOf(mb.Agent);
                        using (var p = Ui.Round(r, Ui.S(12))) { using (var b = new SolidBrush(mb.Off ? Ui.Bg : Color.FromArgb(Ui.Dark ? 70 : 40, col))) g.FillPath(b, p); using (var pen = new Pen(mb.Off ? Ui.Line : col)) g.DrawPath(pen, p); }
                        TextRenderer.DrawText(g, label, Ui.Small, new Rectangle(r.X + Ui.S(10), r.Y, r.Width - Ui.S(14), r.Height), mb.Off ? Ui.Muted : Ui.Text, flags);
                        hots.Add(new Hot { R = r, Key = "m:" + mb.Key });
                        x = r.Right + Ui.S(6);
                    }
                    string rt = "Rounds: " + c.Rounds;
                    var rr = new Rectangle(x, y, TextRenderer.MeasureText(rt, Ui.Small).Width + Ui.S(20), h);
                    if (rr.Right > right) { x = Ui.S(54); y += Ui.S(28); rr.X = x; rr.Y = y; }
                    using (var p = Ui.Round(rr, Ui.S(12))) { using (var b = new SolidBrush(hot == "rounds" ? Ui.HoverBg : Ui.Card)) g.FillPath(b, p); using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p); }
                    TextRenderer.DrawText(g, rt, Ui.Small, rr, Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    hots.Add(new Hot { R = rr, Key = "rounds" });
                    int needC = y + h + Ui.S(6);
                    if (needC != Height) BeginInvoke(new Action(() => { if (!IsDisposed) Height = needC; }));
                    return;
                }
                foreach (var a in c.Agents)
                {
                    bool on = !c.Off.Contains(a);
                    if (on) n++;
                    string label = (on ? n + "  " : "") + AgentStore.NameOf(a);
                    int w = Math.Min(Ui.S(170), TextRenderer.MeasureText(label, Ui.Small).Width + Ui.S(30));
                    if (x > Ui.S(54) && x + w > right) { x = Ui.S(54); y += Ui.S(28); }   // wrap
                    var r = new Rectangle(x, y, w, h);
                    var col = AgentStore.ColorOf(a);
                    using (var p = Ui.Round(r, Ui.S(12)))
                    {
                        using (var b = new SolidBrush(on ? Color.FromArgb(Ui.Dark ? 70 : 40, col) : Ui.Bg)) g.FillPath(b, p);
                        using (var pen = new Pen(on ? col : (hot == "a:" + a ? Ui.Accent : Ui.Line), on ? 1.5f * Ui.Scale : 1f)) g.DrawPath(pen, p);
                    }
                    using (var b = new SolidBrush(on ? col : Ui.Muted)) g.FillEllipse(b, r.X + Ui.S(9), r.Y + Ui.S(8), Ui.S(8), Ui.S(8));
                    TextRenderer.DrawText(g, label, Ui.Small, new Rectangle(r.X + Ui.S(21), r.Y, r.Width - Ui.S(25), r.Height), on ? Ui.Text : Ui.Muted, flags);
                    hots.Add(new Hot { R = r, Key = "a:" + a });
                    x = r.Right + Ui.S(6);
                }
                if (c.Agents.Count > 1)
                {
                    if (x + Ui.S(4) + sepW > right) { x = Ui.S(50); y += Ui.S(28); }
                    var r = new Rectangle(x + Ui.S(4), y, sepW, h);
                    using (var p = Ui.Round(r, Ui.S(12))) { using (var b = new SolidBrush(hot == "sep" ? Ui.HoverBg : Ui.Card)) g.FillPath(b, p); using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p); }
                    TextRenderer.DrawText(g, c.Separate ? "" : "", Ui.Icon(8.5f), new Rectangle(r.X + Ui.S(6), r.Y, Ui.S(16), r.Height), Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, sepText, Ui.Small, new Rectangle(r.X + Ui.S(24), r.Y, r.Width - Ui.S(26), r.Height), Ui.Text, flags);
                    hots.Add(new Hot { R = r, Key = "sep" });
                }
                {
                    string wt = c.Web ? "Web search: on" : "Web search";
                    int ww = TextRenderer.MeasureText(wt, Ui.Small).Width + Ui.S(30);
                    if (x + Ui.S(8) + ww > right) { x = Ui.S(50); y += Ui.S(28); } else x += c.Agents.Count > 1 ? sepW + Ui.S(10) : Ui.S(4);
                    var r = new Rectangle(x, y, ww, h);
                    using (var p = Ui.Round(r, Ui.S(12))) { using (var b = new SolidBrush(c.Web ? Color.FromArgb(Ui.Dark ? 70 : 40, Ui.Accent) : (hot == "web" ? Ui.HoverBg : Ui.Card))) g.FillPath(b, p); using (var pen = new Pen(c.Web ? Ui.Accent : Ui.Line)) g.DrawPath(pen, p); }
                    TextRenderer.DrawText(g, "", Ui.Icon(8.5f), new Rectangle(r.X + Ui.S(6), r.Y, Ui.S(16), r.Height), c.Web ? Ui.Accent : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    TextRenderer.DrawText(g, wt, Ui.Small, new Rectangle(r.X + Ui.S(24), r.Y, r.Width - Ui.S(26), r.Height), Ui.Text, flags);
                    hots.Add(new Hot { R = r, Key = "web" });
                }
                int need = y + h + Ui.S(6);
                if (need != Height) BeginInvoke(new Action(() => { if (!IsDisposed) Height = need; }));
            }
            string KeyAt(Point p) { var h = hots.LastOrDefault(x => x.R.Contains(p)); return h != null ? h.Key : null; }
            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                string k = KeyAt(e.Location);
                if (k == hot) return;
                hot = k; Invalidate(); Cursor = k != null ? Cursors.Hand : Cursors.Default;
                tip.SetToolTip(this, k == null ? "" : k == "expand" ? "Bigger / smaller message box" : k == "sep"
                    ? (pane.Chat.Separate ? "Separate: each agent answers only your message. Click for shared." : "Shared: each agent also sees the answers before it. Click for separate.")
                    : "Click: this agent answers or not.  Right-click: change the order.  Tip: start a message with @name to ask only that agent.");
            }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != null) { hot = null; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                string k = KeyAt(e.Location); if (k == null) return;
                if (k == "expand") { pane.ToggleExpand(); return; }
                if (k == "sep") { pane.ToggleSeparate(); return; }
                if (k == "web") { pane.Chat.Web = !pane.Chat.Web; pane.Changed2(); App.I.Status(pane.Chat.Web ? "Web search on: agents search the web and GitHub before answering." : "Web search off."); return; }
                if (k == "rounds") { pane.Chat.Rounds = (pane.Chat.Rounds + 1) % 4; pane.Changed2(); return; }
                if (k.StartsWith("m:")) { var mb = pane.Chat.Members.FirstOrDefault(z => z.Key == k.Substring(2)); if (mb != null) { if (e.Button == MouseButtons.Right) pane.EditCouncil(); else { mb.Off = !mb.Off; pane.Changed2(); } } return; }
                string id = k.Substring(2);
                if (e.Button == MouseButtons.Right)
                {
                    var r = hots.First(x => x.Key == k).R;
                    pane.AgentMenu(id, this, new Point(r.X, r.Bottom));
                }
                else pane.ToggleOff(id);
            }
        }
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
        readonly ComboBox filter = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        bool updatingFilter;
        const string FolderPrefix = "Folder  ·  ";
        readonly ContextMenuStrip menu;
        public AgentsPage Page;

        public ChatHistory()
        {
            BackColor = Ui.Card; Padding = new Padding(Ui.S(8), Ui.S(8), Ui.S(8), Ui.S(8));
            var title = new Label { Text = "Chats", Font = Ui.Big, Dock = DockStyle.Top, Height = Ui.S(32), ForeColor = Ui.Text };
            search = Ui.Search("Search chats"); search.Dock = DockStyle.Top;
            var searchLater = Ui.Debounce(search, 160, Fill);
            search.TextChanged += delegate { searchLater(); };
            filter.SelectedIndexChanged += delegate { if (!updatingFilter) Fill(); };
            list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(50), IntegralHeight = false, BackColor = Ui.Card };
            list.DrawItem += DrawRow;
            empty = new Label { Dock = DockStyle.Top, Height = Ui.S(60), ForeColor = Ui.Muted, Font = Ui.Small, Text = "No conversations in this view.", Visible = false };
            list.MouseUp += OnUp;
            list.MouseMove += (s, e) => { int i = list.IndexFromPoint(e.Location); list.Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; };
            menu = Ui.Menu(true); Ui.ThemeMenu(menu);
            Controls.Add(list);
            Controls.Add(empty);
            Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(8), BackColor = Ui.Card });
            Controls.Add(search);
            Controls.Add(filter);
            Controls.Add(title);
            AgentStore.Changed += OnChanged;
        }
        bool refreshPending;
        void OnChanged()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { BeginInvoke((Action)OnChanged); return; }
            if (!Visible || refreshPending) return;
            refreshPending = true;
            BeginInvoke(new Action(() => { refreshPending = false; if (!IsDisposed && Visible) Fill(); }));
        }
        protected override void Dispose(bool disposing) { if (disposing) AgentStore.Changed -= OnChanged; base.Dispose(disposing); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) Fill(); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height); }

        public void Fill()
        {
            if (list == null || search == null) return;
            string q = search.Text.Trim();
            string scope = filter.SelectedItem as string ?? "All chats";
            var options = new[] { "All chats", "Favorites", "Drafts", "Archived" }
                .Concat(AgentStore.Chats.Where(c => !AssistantHarness.IsAssistant(c)).Select(c => c.Collection).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => FolderPrefix + x)).ToArray();
            if (!filter.Items.Cast<string>().SequenceEqual(options))
            {
                updatingFilter = true;
                filter.BeginUpdate(); filter.Items.Clear(); filter.Items.AddRange(options);
                filter.SelectedIndex = Math.Max(0, Array.IndexOf(options, scope)); filter.EndUpdate();
                scope = (string)filter.SelectedItem; updatingFilter = false;
            }
            IEnumerable<AgentChat> src = AgentStore.Chats.Where(c => !AssistantHarness.IsAssistant(c) && c.HasContent && c.Archived == (scope == "Archived"));
            if (scope == "Favorites") src = src.Where(c => c.Fav);
            if (scope == "Drafts") src = src.Where(c => c.PendingInput.Length > 0);
            if (scope.StartsWith(FolderPrefix)) src = src.Where(c => string.Equals(c.Collection, scope.Substring(FolderPrefix.Length), StringComparison.OrdinalIgnoreCase));
            if (q.Length > 0) src = src.Where(c => c.PendingInput.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Folder.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || c.Messages.Any(m => m.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            shown = src.OrderByDescending(c => c.Fav).ThenByDescending(c => c.Updated).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var c in shown) list.Items.Add(c.Id);
            list.EndUpdate();
            empty.Visible = shown.Count == 0;
            if (q.Length > 0) empty.Text = "No chat matches \"" + q + "\"."; else empty.Text = "No conversations in this view.";
        }

        void DrawRow(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= shown.Count) return;
            var c = shown[e.Index]; var g = e.Graphics; var r = e.Bounds;
            bool open = IsOpen != null && IsOpen(c);
            using (var b = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Ui.SelBg : Ui.Card)) g.FillRectangle(b, r);
            if (open) using (var b = new SolidBrush(Ui.Accent)) g.FillRectangle(b, r.X, r.Y + Ui.S(8), Ui.S(3), r.Height - Ui.S(16));
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, (c.Fav ? "★ " : "") + (c.Title.Length > 0 ? c.Title : c.PendingInput.Length > 0 ? "Draft · " + c.PendingInput.Replace("\r", " ").Replace("\n", " ") : "Chat"), Ui.Bold, new Rectangle(r.X + Ui.S(10), r.Y + Ui.S(5), r.Width - Ui.S(16), Ui.S(20)), Ui.Text, flags);
            int x = r.X + Ui.S(10);
            foreach (var a in c.Agents.Take(5)) { using (var b = new SolidBrush(AgentStore.ColorOf(a))) g.FillEllipse(b, x, r.Y + Ui.S(32), Ui.S(7), Ui.S(7)); x += Ui.S(10); }
            string when = Ui.Ago(c.Updated);
            TextRenderer.DrawText(g, when + "  ·  " + (string.IsNullOrEmpty(c.Collection) ? "" : c.Collection + "  ·  ") + Path.GetFileName(c.Folder.TrimEnd('\\')), Ui.Small, new Rectangle(x + Ui.S(4), r.Y + Ui.S(26), r.Right - x - Ui.S(10), Ui.S(18)), Ui.Muted, flags);
        }

        static void ChangeFolder(string oldName, string newName)
        {
            foreach (var c in AgentStore.Chats.Where(x => string.Equals(x.Collection, oldName, StringComparison.OrdinalIgnoreCase)).ToList())
            { c.Collection = newName; AgentStore.Save(c); }
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
            var move = new ToolStripMenuItem("Move to folder");
            move.DropDownItems.Add("New folder…", null, delegate {
                var name = Ui.Ask("Chat folder", "Folder name", "", FindForm());
                if (!string.IsNullOrWhiteSpace(name)) { c.Collection = name.Trim(); AgentStore.Save(c); }
            });
            foreach (var name in AgentStore.Chats.Where(x => !AssistantHarness.IsAssistant(x)).Select(x => x.Collection).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            {
                string folder = name;
                move.DropDownItems.Add(folder, null, delegate { c.Collection = folder; AgentStore.Save(c); });
            }
            if (!string.IsNullOrEmpty(c.Collection))
            {
                move.DropDownItems.Add("Remove chat from folder", null, delegate { c.Collection = ""; AgentStore.Save(c); });
                move.DropDownItems.Add("Rename this folder…", null, delegate {
                    var name = Ui.Ask("Rename chat folder", "Folder name", c.Collection, FindForm());
                    if (!string.IsNullOrWhiteSpace(name)) ChangeFolder(c.Collection, name.Trim());
                });
                move.DropDownItems.Add("Dissolve folder (keep chats)…", null, delegate {
                    if (MessageBox.Show(FindForm(), "Move all conversations out of " + c.Collection + "?", "Chat folders", MessageBoxButtons.OKCancel) == DialogResult.OK) ChangeFolder(c.Collection, "");
                });
            }
            menu.Items.Add(move);
            menu.Items.Add(c.Archived ? "Restore to chats" : "Archive chat", null, delegate { c.Archived = !c.Archived; AgentStore.Save(c); });
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
            grid.MouseMove += GridMove; grid.MouseDown += GridDown; grid.MouseUp += GridUp; grid.MouseDoubleClick += GridDouble;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveLayoutNow(); };
            history = new ChatHistory { Dock = DockStyle.Left, Width = Ui.S(260), Page = this };
            history.Open += OpenChat;
            history.IsOpen = c => panes.Any(p => p.Chat == c);

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Agents"));
            picker = new LayoutPicker();
            picker.Changed += delegate { layout.Layout = picker.Value; Arrange(); SaveLayout(); };
            bar.Controls.Add(picker);
            bar.Controls.Add(Ui.Btn("New chat", delegate { NewChatIn(active ?? panes.FirstOrDefault()); }, true));
            bar.Controls.Add(Ui.Btn("Model council", delegate { var p = active ?? panes.FirstOrDefault(); if (p == null) return; if (!p.IsEmpty) { p = EmptyPaneOr(p); if (!p.IsEmpty) NewChatIn(p); } p.EditCouncil(); }));
            bar.Controls.Add(Ui.Btn("Leader team", delegate { var p = active ?? panes.FirstOrDefault(); if (p != null) p.OpenLeaderTeam(); }));
            bar.Controls.Add(Ui.Btn("Workspaces", delegate { WorkspaceMenu(); }));
            Button setsBtn = null;
            setsBtn = Ui.Btn("Folder sets", delegate { FolderSets.ShowMenu(setsBtn, new Point(0, setsBtn.Height), () => panes.Take(Visible_).Select(p => p.Chat != null ? p.Chat.Folder : "").ToList(), OpenSet, 6, "Agents"); });
            bar.Controls.Add(setsBtn);
            bar.Controls.Add(Ui.Btn("Chats", delegate { layout.HistoryOff = !layout.HistoryOff; SaveLayout(); PerformLayout(); }));
            bar.Controls.Add(Ui.Btn("History", delegate { ShowHistory(); }));
            bar.Controls.Add(Ui.Btn("Updates", delegate { CliUpdates.Show(FindForm()); }));
            bar.Controls.Add(Ui.Btn("Stop all", delegate { foreach (var p in panes) if (p.Busy) p.Stop(); }));
            // Chat text size: one button (menu: larger, smaller, reset); Ctrl + / Ctrl - / Ctrl 0 and Ctrl+wheel also work.
            var size = Ui.Btn("Text 100%", null);
            ((FluentButton)size).Glyph = null;
            size.Click += delegate
            {
                var zm = Ui.Menu(); Ui.ThemeMenu(zm);
                zm.Items.Add("Larger   Ctrl +", null, delegate { ChatView.SetZoom(ChatView.Zoom + 0.1f); });
                zm.Items.Add("Smaller   Ctrl -", null, delegate { ChatView.SetZoom(ChatView.Zoom - 0.1f); });
                zm.Items.Add("Reset to 100%   Ctrl 0", null, delegate { ChatView.SetZoom(1f); });
                foreach (var pct in new[] { 90, 110, 125, 150 }) { float z = pct / 100f; zm.Items.Add(pct + "%", null, delegate { ChatView.SetZoom(z); }); }
                zm.Show(size, new Point(0, size.Height));
            };
            bar.Controls.Add(size);
            Action showSize = () => size.Text = "Text " + Math.Round(ChatView.Zoom * 100) + "%";
            showSize(); ChatView.ZoomChanged += showSize; Disposed += delegate { ChatView.ZoomChanged -= showSize; };
            var tips = new ToolTip(); Disposed += delegate { tips.Dispose(); };
            tips.SetToolTip(size, "Chat text size (also Ctrl + / Ctrl - / Ctrl 0 and Ctrl+mouse wheel)");
            info = new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(Ui.S(6), Ui.S(9), 0, 0) };
            bar.Controls.Add(info);
            limitsLabel = new Label { AutoSize = true, Font = Ui.Small, Margin = new Padding(Ui.S(14), Ui.S(9), 0, 0), Cursor = Cursors.Hand, Text = "Limits: reading..." };
            limitsLabel.Click += delegate { using (var d = new LimitsDialog()) d.ShowDialog(FindForm()); };
            new ToolTip().SetToolTip(limitsLabel, "5-hour and weekly usage limits of the agent CLIs. Click for details.");
            bar.Controls.Add(limitsLabel);
            AgentLimits.Updated += ShowLimits;
            limitsTimer.Tick += delegate { if (Visible) AgentLimits.Refresh(false); };
            limitsTimer.Start();

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(46), Padding = new Padding(0, Ui.S(8), 0, Ui.S(4)) };
            broadcast = new FluentTextBox { Dock = DockStyle.Fill };
            broadcast.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Broadcast(); } };
            EventHandler cue = delegate { try { Native.SendMessage(broadcast.Handle, 0x1501, (IntPtr)1, "Message every pane"); } catch { } };
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
            if (Visible) AgentLimits.Refresh(false);
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
                        if (c != null && panes[i].IsEmpty && !panes[i].Busy) panes[i].SetChat(c);
                    }
                    UpdateInfo();
                });
                PerformLayout();
            }
            if (Visible && active != null && IsHandleCreated) BeginInvoke(new Action(() => {
                if (!IsDisposed && Visible && !ContainsFocus && FindForm() != null && FindForm().ContainsFocus) active.FocusInput();
            }));
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
                p.Changed += delegate { UpdateInfo(); history.Invalidate(true); SaveLayout(); };   // SaveLayout is batched (timer)
                panes.Add(p);
            }
            Ui.Batch(grid, () =>
            {
                grid.Controls.Clear(); grid.ColumnStyles.Clear(); grid.RowStyles.Clear();
                int cols = l == "2x2" ? 2 : l == "3x2" ? 3 : count, rowsN = l == "2x2" || l == "3x2" ? 2 : 1;
                grid.ColumnCount = cols; grid.RowCount = rowsN;
                bool cw = layout.ColW != null && layout.ColW.Count == cols, rh = layout.RowH != null && layout.RowH.Count == rowsN;
                for (int c = 0; c < cols; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, cw ? layout.ColW[c] : 100f / cols));
                for (int r = 0; r < rowsN; r++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, rh ? layout.RowH[r] : 100f / rowsN));
                for (int i = 0; i < count; i++) { panes[i].Dock = DockStyle.Fill; grid.Controls.Add(panes[i], i % cols, i / cols); }
            });
            // Panes beyond the layout keep running in the background; they come back when the layout grows.
            if (active == null || panes.IndexOf(active) >= count) SetActive(panes[0]);
            UpdateInfo();
        }

        void SetActive(ChatPane p) { active = p; foreach (var x in panes) x.IsActive = x == p; }

        int Visible_ { get { return LayoutPicker.Count(layout.Layout); } }

        // Many small changes while agents answer: save at most about once a second.
        readonly System.Windows.Forms.Timer saveTimer = new System.Windows.Forms.Timer { Interval = 900 };
        void SaveLayout() { saveTimer.Stop(); saveTimer.Start(); }
        void SaveLayoutNow()
        {
            if (layout == null || !AgentStore.Loaded) return;
            layout.Panes = panes.Select(p => p.Chat != null && p.Chat.HasContent ? p.Chat.Id : "").ToList();
            AgentStore.SaveLayout(layout);
        }

        readonly Label limitsLabel;
        readonly System.Windows.Forms.Timer limitsTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        protected override void Dispose(bool disposing) { if (disposing) { AgentLimits.Updated -= ShowLimits; limitsTimer.Dispose(); } base.Dispose(disposing); }
        void ShowLimits()
        {
            if (IsDisposed || limitsLabel == null) return;
            limitsLabel.Text = AgentLimits.Summary();
            limitsLabel.ForeColor = AgentLimits.Last.Any(r => r.High) ? Ui.Danger : Ui.Muted;
        }

        void UpdateInfo()
        {
            int busy = panes.Take(Visible_).Count(p => p.Busy);
            int chats = AgentStore.Chats.Count(c => !AssistantHarness.IsAssistant(c) && c.Messages.Count > 0);
            info.Text = (busy > 0 ? busy + " working  ·  " : "") + chats + " saved chats";
        }

        // ---------- draggable dividers between panes ----------
        int dragCol = -1, dragRow = -1, dragStart;
        int[] dragSizes;
        void Boundary(Point p, out int col, out int row)
        {
            col = row = -1;
            int[] w = grid.GetColumnWidths(), h = grid.GetRowHeights();
            int x = 0; for (int i = 0; i < w.Length - 1; i++) { x += w[i]; if (Math.Abs(p.X - x) <= Ui.S(7)) { col = i; return; } }
            int y = grid.Padding.Top; for (int i = 0; i < h.Length - 1; i++) { y += h[i]; if (Math.Abs(p.Y - y) <= Ui.S(7)) { row = i; return; } }
        }
        void GridMove(object s, MouseEventArgs e)
        {
            if (dragCol >= 0 || dragRow >= 0)
            {
                int d = (dragCol >= 0 ? e.X : e.Y) - dragStart, i = dragCol >= 0 ? dragCol : dragRow, min = Ui.S(dragCol >= 0 ? 240 : 160);
                var sz = (int[])dragSizes.Clone();
                d = Math.Max(min - sz[i], Math.Min(sz[i + 1] - min, d));
                sz[i] += d; sz[i + 1] -= d;
                float total = sz.Sum();
                grid.SuspendLayout();
                for (int k = 0; k < sz.Length; k++) { if (dragCol >= 0) grid.ColumnStyles[k].Width = sz[k] * 100f / total; else grid.RowStyles[k].Height = sz[k] * 100f / total; }
                grid.ResumeLayout(true);
                return;
            }
            int c, r; Boundary(e.Location, out c, out r);
            grid.Cursor = c >= 0 ? Cursors.VSplit : r >= 0 ? Cursors.HSplit : Cursors.Default;
        }
        void GridDown(object s, MouseEventArgs e)
        {
            int c, r; Boundary(e.Location, out c, out r);
            if (c >= 0) { dragCol = c; dragStart = e.X; dragSizes = grid.GetColumnWidths(); grid.Capture = true; }
            else if (r >= 0) { dragRow = r; dragStart = e.Y; dragSizes = grid.GetRowHeights(); grid.Capture = true; }
        }
        void GridUp(object s, MouseEventArgs e)
        {
            if (dragCol < 0 && dragRow < 0) return;
            if (dragCol >= 0) layout.ColW = grid.ColumnStyles.Cast<ColumnStyle>().Select(x => x.Width).ToList();
            else layout.RowH = grid.RowStyles.Cast<RowStyle>().Select(x => x.Height).ToList();
            dragCol = dragRow = -1; grid.Capture = false; SaveLayout();
        }
        void GridDouble(object s, MouseEventArgs e)
        {
            int c, r; Boundary(e.Location, out c, out r);
            if (c < 0 && r < 0) return;
            if (c >= 0) layout.ColW = new List<float>(); else layout.RowH = new List<float>();   // equal sizes again
            Arrange(); SaveLayout();
        }

        // ---------- workspaces: saved arrangements of panes and their chats ----------
        void WorkspaceMenu()
        {
            if (layout.Saved == null) layout.Saved = new List<AgentWorkspace>();
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            m.Items.Add("Save this arrangement as...", null, delegate
            {
                var n = Ui.Ask("Save workspace", "Name", "Workspace " + (layout.Saved.Count + 1), FindForm());
                if (string.IsNullOrWhiteSpace(n)) return;
                SaveLayoutNow();
                layout.Saved.Add(new AgentWorkspace { Name = n.Trim(), Layout = layout.Layout, Panes = layout.Panes.ToList(), ColW = (layout.ColW ?? new List<float>()).ToList(), RowH = (layout.RowH ?? new List<float>()).ToList() });
                SaveLayoutNow(); App.I.Status("Workspace saved.");
            });
            if (layout.Saved.Count > 0) m.Items.Add(new ToolStripSeparator());
            foreach (var w in layout.Saved.Where(x => x.Fav).Concat(layout.Saved.Where(x => !x.Fav))) { var ww = w; m.Items.Add((ww.Fav ? "★  " : "") + ww.Name + "   (" + LayoutPicker.Count(ww.Layout) + " panes)", null, delegate { OpenWorkspace(ww); }); }
            if (layout.Saved.Count > 0)
            {
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Manage workspaces...", null, delegate
                {
                    var L = layout.Saved;
                    using (var dlg = new ListManager("Agents workspaces", () => L.Select(x => x.Name + "   (" + LayoutPicker.Count(x.Layout) + " panes)").ToList(), i => L[i].Fav,
                        i => OpenWorkspace(L[i]),
                        i => { var n = Ui.Ask("Rename workspace", "Name", L[i].Name, FindForm()); if (!string.IsNullOrWhiteSpace(n)) L[i].Name = n.Trim(); SaveLayoutNow(); },
                        i => { L[i].Fav = !L[i].Fav; SaveLayoutNow(); },
                        i => { var w = L[i]; L.Insert(i + 1, new AgentWorkspace { Name = w.Name + " (copy)", Layout = w.Layout, Panes = w.Panes.ToList(), ColW = w.ColW.ToList(), RowH = w.RowH.ToList() }); SaveLayoutNow(); },
                        (i, d) => { int j = i + d; if (j < 0 || j >= L.Count) return; var w = L[i]; L.RemoveAt(i); L.Insert(j, w); SaveLayoutNow(); },
                        i => { L.RemoveAt(i); SaveLayoutNow(); }))
                        dlg.ShowDialog(FindForm());
                });
            }
            m.Show(Cursor.Position);
        }

        void OpenWorkspace(AgentWorkspace w)
        {
            if (panes.Any(p => p.Busy)) { App.I.Status("Wait for the agents to finish (or Stop all) before opening a workspace."); return; }
            layout.Layout = w.Layout; layout.ColW = (w.ColW ?? new List<float>()).ToList(); layout.RowH = (w.RowH ?? new List<float>()).ToList();
            picker.Value = layout.Layout; picker.Invalidate();
            Arrange();
            for (int i = 0; i < panes.Count && i < LayoutPicker.Count(w.Layout); i++)
            {
                var c = i < w.Panes.Count ? AgentStore.Find(w.Panes[i]) : null;
                panes[i].SetChat(c ?? Blank(i));
            }
            SaveLayout(); UpdateInfo();
            App.I.Status("Workspace opened: " + w.Name);
        }

        // A folder set: one new chat per folder (up to 6), with the same agents as the pane had.
        void OpenFolderSet(FolderSet s)
        {
            int n = Math.Max(1, Math.Min(6, s.Folders.Count));
            layout.Layout = n <= 4 ? n.ToString() : "6"; layout.ColW = new List<float>(); layout.RowH = new List<float>();
            picker.Value = layout.Layout; picker.Invalidate();
            Arrange();
            int skipped = 0;
            for (int i = 0; i < n && i < panes.Count; i++)
            {
                if (panes[i].Busy) { skipped++; continue; }
                var old = panes[i].Chat;
                var c = Blank(i); c.Folder = s.Folders[i];
                if (old != null && old.Agents.Count > 0) { c.Agents.Clear(); c.Agents.AddRange(old.Agents); }
                panes[i].SetChat(c);
            }
            SaveLayout(); UpdateInfo();
            App.I.Status("Folder set \"" + s.Name + "\": a new chat per folder." + (skipped > 0 ? " " + skipped + " busy pane(s) kept their chat." : ""));
        }

        // Folder sets: one new chat per folder (up to 6 panes). Busy panes keep their chat.
        void OpenSet(FolderSet s)
        {
            var list = s.Folders.Where(Directory.Exists).Take(6).ToList();
            if (list.Count == 0) { MessageBox.Show(FindForm(), "None of the folders of \"" + s.Name + "\" exist on this PC.", "Folder sets"); return; }
            layout.Layout = list.Count <= 4 ? list.Count.ToString() : "6";
            layout.ColW = new List<float>(); layout.RowH = new List<float>();
            picker.Value = layout.Layout; picker.Invalidate();
            Arrange();
            for (int i = 0; i < list.Count && i < panes.Count; i++)
            {
                if (panes[i].Busy) continue;
                var c = Blank(i); c.Folder = list[i]; c.Title = "";
                panes[i].SetChat(c);
            }
            SaveLayout(); UpdateInfo();
            App.I.Status("Opened folder set: " + s.Name + " (one chat per folder).");
        }

        // ---------- chat history manager: search, sort, many at once ----------
        void ShowHistory()
        {
            var cols = new[] { "Last message", "Chat", "Agents", "Folder", "Messages", "Created" };
            Func<List<HistRow>> rows = () => AgentStore.Chats.Where(c => !AssistantHarness.IsAssistant(c) && c.HasContent).Select(c => new HistRow
            {
                Tag = c, Fav = c.Fav, Time = new DateTime(Math.Max(c.Updated, c.Created)),
                Cells = new[] { new DateTime(Math.Max(c.Updated, c.Created)).ToString("yyyy-MM-dd HH:mm"), c.Title.Length > 0 ? c.Title : "(no title)",
                    c.Council ? "Model council (" + c.Members.Count + ")" : string.Join(", ", c.Agents.Select(AgentStore.NameOf)), c.Folder, c.Messages.Count.ToString(), new DateTime(c.Created).ToString("yyyy-MM-dd") }
            }).ToList();
            Func<List<HistRow>, List<AgentChat>> chats = sel => sel.Select(r => (AgentChat)r.Tag).ToList();
            var actions = new List<HistAction>
            {
                new HistAction { Name = "Open", Primary = true, CloseAfter = true, Run = sel => { foreach (var c in chats(sel).Take(Visible_)) OpenChat(c); } },
                new HistAction { Name = "★ Favorite on / off", Run = sel => { foreach (var c in chats(sel)) { c.Fav = !c.Fav; AgentStore.Save(c); } } },
                new HistAction { Name = "Rename...", Run = sel => { var c = chats(sel)[0]; var v = Ui.Ask("Chat name", "Name", c.Title, FindForm()); if (v != null) { c.Title = v.Trim(); AgentStore.Save(c); } } },
                new HistAction { Name = "Move to folder…", Run = sel => { var name = Ui.Ask("Move chats", "Folder name (empty removes folder)", "", FindForm()); if (name != null) foreach (var c in chats(sel)) { c.Collection = name.Trim(); AgentStore.Save(c); } } },
                new HistAction { Name = "Archive / restore", Run = sel => { foreach (var c in chats(sel)) { c.Archived = !c.Archived; AgentStore.Save(c); } } },
                new HistAction { Name = "Copy as Markdown", Run = sel => App.I.CopyText(string.Join("\r\n\r\n---\r\n\r\n", chats(sel).Select(AgentStore.Markdown))) },
                new HistAction { Name = "Export...", Run = sel => { foreach (var c in chats(sel).Take(20)) Export(c); } },
                new HistAction { Name = "Delete", Run = sel =>
                    {
                        var list = chats(sel).Where(c => !panes.Any(p => p.Chat == c)).ToList();
                        if (list.Count == 0 || MessageBox.Show(FindForm(), "Delete " + list.Count + " chat(s) for good? (Chats open in a pane are kept.)", "Agents", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                        foreach (var c in list) AgentStore.Delete(c);
                    } },
                new HistAction { Name = "Delete older than 30 days (not ★)", NeedsSelection = false, Run = sel =>
                    {
                        long cut = DateTime.Now.AddDays(-30).Ticks;
                        var list = AgentStore.Chats.Where(c => !AssistantHarness.IsAssistant(c) && !c.Fav && Math.Max(c.Updated, c.Created) < cut && !panes.Any(p => p.Chat == c)).ToList();
                        if (list.Count == 0) { App.I.Status("No chats older than 30 days."); return; }
                        if (MessageBox.Show(FindForm(), "Delete " + list.Count + " chat(s) older than 30 days (favorites kept)?", "Agents", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                        foreach (var c in list) AgentStore.Delete(c);
                    } },
                new HistAction { Name = "Export CSV...", NeedsSelection = false, Run = sel => HistoryView.ExportCsv(FindForm(), "agent-chats", cols, sel.Count > 0 ? sel : rows()) },
            };
            using (var v = new HistoryView("Agents chat history", cols, new[] { 130, 300, 190, 300, 70, 90 }, rows, actions)) v.ShowDialog(FindForm());
            history.Invalidate(true); UpdateInfo();
        }

        // ---------- collaboration between panes ----------
        public void OpenChatPublic(AgentChat c) { if (!AgentStore.Loaded) { AgentStore.LoadAll(() => OpenChat(c)); return; } OpenChat(c); }
        public void SendToOthers(ChatPane from, string text)
        {
            int n = 0;
            foreach (var p in panes.Take(Visible_)) if (p != from && !p.Busy) { p.Send(text); n++; }
            App.I.Status(n == 0 ? "No other free pane. Add a pane with the layout buttons." : "Sent to " + n + " pane(s).");
        }
        public ChatPane EmptyPaneOr(ChatPane fallback)
        {
            return panes.Take(Visible_).FirstOrDefault(p => p.IsEmpty && !p.Busy) ?? fallback;
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
            var target = panes.Take(Visible_).FirstOrDefault(p => p.IsEmpty && !p.Busy) ?? (active != null && !active.Busy ? active : panes.Take(Visible_).FirstOrDefault(p => !p.Busy));
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
        public void InsertText(string text)
        {
            if (!started) OnVisibleChanged(EventArgs.Empty);
            var p = active ?? panes[0];
            p.InsertText(text);
        }
        public void UseFolder(string folder)
        {
            if (!started) OnVisibleChanged(EventArgs.Empty);
            var p = panes.Take(Visible_).FirstOrDefault(x => x.IsEmpty && !x.Busy) ?? active ?? panes[0];
            if (p.Busy) { p = panes.Take(Visible_).FirstOrDefault(x => !x.Busy); if (p == null) return; }
            if (!p.IsEmpty) NewChatIn(p);
            p.Chat.Folder = folder; MetaStore.Touch("folders", folder);
            p.SetChat(p.Chat); SetActive(p); p.FocusInput();
        }
    }
}
