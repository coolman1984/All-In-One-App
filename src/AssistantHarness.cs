using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // Standalone harness for the All in One Autonomous Assistant Companion.
    // Owns companion identity, 3-Tier Prompt Builder (Stable -> Context -> Volatile),
    // Lean Subagents (strips SOUL.md for workers), live activity narration, isolated chat storage,
    // working-directory discovery, hidden context wrapping (Solo / Council / Leader Team),
    // shared Agents-page MCP hint, and per-CLI MCP argument wiring.
    static class AssistantHarness
    {
        public const string Collection = "Assistant";
        const int MaxProjectDocChars = 2048;
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        static readonly HashSet<string> McpCapableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "claude", "codex", "agy", "cline", "opencode" };

        public static event Action<string> Activity;

        public static bool IsAssistant(AgentChat chat)
        {
            return chat != null && string.Equals(chat.Collection, Collection, StringComparison.OrdinalIgnoreCase);
        }

        public static void Report(string action)
        {
            if (string.IsNullOrWhiteSpace(action)) return;
            var h = Activity;
            if (h == null) return;
            foreach (Action<string> listener in h.GetInvocationList())
            {
                try { listener(action); }
                catch (Exception) { }
            }
        }

        public static string StorageDir
        {
            get
            {
                string root = string.IsNullOrEmpty(Store.Dir) ? Store.DefaultDir : Store.Dir;
                return Path.Combine(root, "assistant");
            }
        }

        public static string ChatsDir
        {
            get { return Path.Combine(StorageDir, "chats"); }
        }

        // Directories the Assistant companion may access directly (distinct, existing).
        public static List<string> Dirs()
        {
            var candidates = new List<string>();
            try
            {
                if (App.I != null)
                {
                    var ctx = App.I.PageContext();
                    AddDirFromPath(candidates, Str(ctx, "markdown_note"), true);
                    AddDirFromPath(candidates, Str(ctx, "note"), true);
                    AddDirFromPath(candidates, Str(ctx, "explorer_folder"), false);
                    AddDirFromPath(candidates, Str(ctx, "folder"), false);
                    AddDirFromPath(candidates, Str(ctx, "documents_current"), true);
                }
            }
            catch (Exception) { }

            if (Store.Settings != null && Store.Settings.MdVaults != null)
                candidates.AddRange(Store.Settings.MdVaults);

            if (!string.IsNullOrEmpty(Store.Dir))
            {
                candidates.Add(Store.Dir);
                candidates.Add(Path.Combine(Store.Dir, "dashboards"));
                candidates.Add(StorageDir);
            }

            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(exeDir)) candidates.Add(exeDir);
            }
            catch (Exception) { }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var raw in candidates)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string dir = raw.Trim();
                if (dir.Length > 3) dir = dir.TrimEnd('\\', '/');
                if (!seen.Add(dir)) continue;
                try { if (Directory.Exists(dir)) result.Add(dir); }
                catch (Exception) { }
            }
            return result;
        }

        static void AddDirFromPath(List<string> target, string path, bool isFile)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string dir = isFile ? Path.GetDirectoryName(path) : path;
                if (!string.IsNullOrWhiteSpace(dir)) target.Add(dir);
            }
            catch (Exception) { }
        }

        // Tier 1 (Stable, Cache-Pinned) for full companion turns.
        public static string SystemPrompt(AgentChat chat, Dictionary<string, object> ctx)
        {
            return BuildStableTier(chat, false);
        }

        // Tier 1 (Stable) for Lean Subagent workers (omits SOUL.md to keep worker prompts lean).
        public static string SystemPromptLean(AgentChat chat, Dictionary<string, object> ctx)
        {
            return BuildStableTier(chat, true);
        }

        static string BuildStableTier(AgentChat chat, bool lean)
        {
            string chatId = chat != null ? chat.Id : "__default__";
            MemorySnapshot snap = null;
            try { snap = AgentMemory.GetSnapshot(chatId); }
            catch (Exception) { }

            var sb = new StringBuilder();
            sb.AppendLine("[TIER 1: STABLE]");
            sb.AppendLine("You are All in One — the user's autonomous desktop companion and operator inside this closed corporate workstation.");
            if (!lean && snap != null && !string.IsNullOrWhiteSpace(snap.Soul))
            {
                sb.AppendLine();
                sb.AppendLine(Cut(snap.Soul.Trim(), 3500));
            }
            sb.AppendLine();
            sb.AppendLine("Operating rules:");
            sb.AppendLine("- Full open automation inside this isolated workstation is intentional by design.");
            sb.AppendLine("- Act directly on the user's behalf instead of explaining manual steps.");
            sb.AppendLine("- NEVER ask for permission before opening pages, editing files, running tools, or navigating.");
            sb.AppendLine("- Autonomously flip pages (open_page, document_open, document_switch, explorer_open, browser_open, markdown_open) so the user can watch what you open and do.");
            sb.AppendLine("- Visual-First Live Communication Canvas: Whenever explaining architecture, logic, multi-step workflows, report comparisons, plans, or technical concepts, proactively draw interactive studio-grade visuals in the embedded Browser using canvas_update or dashboard_publish (custom interactive SVG diagrams, animated data flows, crisp telemetry cards, minimal Egyptian Arabic labels, Obsidian Dark + Warm Drafting Paper OKLCH palette, spring motion — strictly following skills/anti-slop-web-design/SKILL.md, ZERO AI slop, ZERO walls of text), auto-flip to Browser, and keep the chat text reply very short.");
            sb.AppendLine("Available All in One MCP tools (all-in-one):");
            sb.AppendLine("- Live context & navigation: page_context, open_page, app_status, search_all, notify.");
            sb.AppendLine("- Documents (Excel/Word/PowerPoint/PDF): documents_list, document_open (with optional edit true/false), document_switch, document_save.");
            sb.AppendLine("- Live Office COM & bulk extraction (NASCA DRM-safe): excel_info, excel_read, excel_write, excel_formula, excel_sheet_add, excel_format, word_read, word_write, ppt_read, ppt_write, office_extract.");
            sb.AppendLine("- SQLite Vault (persistent cross-report SQL JOINs & FTS): vault_list, vault_extract, vault_schema, vault_query, vault_search.");
            sb.AppendLine("- Memory, Skills, Creator Mode & Planning: memory_manage, session_search, skill_manage, tool_list, tool_create, tool_delete, todo_plan, chat_steps, chat_compact.");
            sb.AppendLine("- Proactive Cron, Heartbeat & Folder Watchers: cron_manage, heartbeat_status, watch_folder.");
            sb.AppendLine("- Live Communication Canvas & Dashboards: canvas_update, dashboard_publish, dashboard_list.");
            sb.AppendLine("- Explorer & Terminals: explorer_open, terminal_open, folder_sets, folder_sets_list, folder_set_save, favorites_list, item_organize.");
            sb.AppendLine("- Browser: browser_open, browser_read, browser_run_js.");
            sb.AppendLine("- Markdown notes: markdown_open.");
            sb.AppendLine("- Workspace data & coding agents: notes_search, note_get, note_create, note_update, tasks_search, task_lists, task_create, task_update, prompts_search, prompt_get, prompt_save, prompt_fill, clips_search, clip_get, clip_organize, clipboard_copy, agents_list, agent_chats, agent_chat_get, agent_ask, agent_limits.");

            if (snap != null && !string.IsNullOrWhiteSpace(snap.Tools))
            {
                sb.AppendLine();
                sb.AppendLine(Cut(snap.Tools.Trim(), 2500));
            }

            if (lean)
            {
                sb.AppendLine("Team mode: Lean Subagent Worker is active (SOUL persona omitted to minimize tokens). Execute your assigned task directly using full MCP and file tools, and report concrete verified evidence.");
            }
            else if (chat != null && chat.LeaderTeam)
            {
                sb.AppendLine("Team mode: Leader Team is active. Every member (leader and workers) is part of the All in One Companion with full MCP app access. As leader, delegate concrete tasks across workers and synthesize verified outcomes; as worker, execute your assignment directly with full app/file tools and report concrete evidence.");
            }
            else if (chat != null && chat.Council)
            {
                sb.AppendLine("Team mode: Model Council is active. All council members share full awareness of the All in One app and full MCP app access; analyze, verify directly, and act when helpful.");
            }

            return sb.ToString().TrimEnd();
        }

        static string BuildProjectContextTier(AgentChat chat, Dictionary<string, object> ctx)
        {
            string folder = ResolveActiveFolder(chat, ctx);
            if (string.IsNullOrEmpty(folder)) return "";
            try
            {
                if (DriveCache.IsSlowPath(folder) || !Directory.Exists(folder)) return "";
            }
            catch (Exception) { return ""; }

            var sb = new StringBuilder();
            int remaining = MaxProjectDocChars;
            foreach (string fileName in new[] { "CLAUDE.md", "AGENTS.md" })
            {
                if (remaining <= 120) break;
                string full = Path.Combine(folder, fileName);
                try
                {
                    if (!File.Exists(full)) continue;
                    string text = ReadPrefix(full, remaining);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    sb.AppendLine("### Project " + fileName + " (" + folder + ")");
                    sb.AppendLine(text.Trim());
                    sb.AppendLine();
                    remaining -= text.Length;
                }
                catch (Exception) { }
            }
            if (sb.Length == 0) return "";
            return "[TIER 2: PROJECT CONTEXT]\n" + sb.ToString().TrimEnd();
        }

        static string BuildVolatileTier(AgentChat chat, Dictionary<string, object> ctx, bool lean)
        {
            string chatId = chat != null ? chat.Id : "__default__";
            var lines = new List<string>();
            lines.Add("[TIER 3: VOLATILE]");

            if (!lean)
            {
                try
                {
                    var snap = AgentMemory.GetSnapshot(chatId);
                    if (snap != null)
                    {
                        AddMemBlock(lines, "USER.md", snap.User, 2000);
                        AddMemBlock(lines, "MEMORY.md", snap.Memory, 3200);
                        AddMemBlock(lines, "GOALS.md", snap.Goals, 2000);
                        if (!string.IsNullOrWhiteSpace(snap.DailyToday))
                            AddMemBlock(lines, "daily/" + snap.DailyTodayDate + ".md", snap.DailyToday, 2000);
                        if (!string.IsNullOrWhiteSpace(snap.DailyYesterday))
                            AddMemBlock(lines, "daily/" + snap.DailyYesterdayDate + ".md", snap.DailyYesterday, 1500);
                        if (!string.IsNullOrWhiteSpace(snap.SkillsIndex))
                            AddMemBlock(lines, "Skills Index (call skill_manage read <slug> when needed)", snap.SkillsIndex, 2800);
                    }
                }
                catch (Exception) { }
            }

            try
            {
                string todo = AgentMemory.FormatTodoForPrompt(chatId);
                if (!string.IsNullOrWhiteSpace(todo))
                    AddMemBlock(lines, "Active Todo Plan", todo, 1500);
            }
            catch (Exception) { }

            AppendContextLines(lines, ctx);
            return string.Join("\n", lines.ToArray()).TrimEnd();
        }

        public static string BuildThreeTierHeader(AgentChat chat, Dictionary<string, object> ctx, bool lean)
        {
            var tiers = new List<string>();
            string t1 = BuildStableTier(chat, lean);
            if (!string.IsNullOrWhiteSpace(t1)) tiers.Add(t1);
            string t2 = BuildProjectContextTier(chat, ctx);
            if (!string.IsNullOrWhiteSpace(t2)) tiers.Add(t2);
            string t3 = BuildVolatileTier(chat, ctx, lean);
            if (!string.IsNullOrWhiteSpace(t3)) tiers.Add(t3);
            return string.Join("\n\n", tiers.ToArray());
        }

        public static string WrapMessage(AgentChat chat, Dictionary<string, object> ctx, string userText)
        {
            string header = SanitizeBrackets(BuildThreeTierHeader(chat, ctx, false));
            return "⟦" + header + "⟧\n\n" + (userText ?? "");
        }

        public static string WrapTeamPrompt(AgentChat chat, string prompt)
        {
            return WrapTeamPromptLean(chat, prompt, false);
        }

        public static string WrapTeamPromptLean(AgentChat chat, string prompt, bool lean)
        {
            if (!IsAssistant(chat)) return prompt ?? "";
            var ctx = SafePageContext();
            string header = SanitizeBrackets(BuildThreeTierHeader(chat, ctx, lean));
            return "⟦" + header + "⟧\n\n" + (prompt ?? "");
        }

        // Shared Harness Hook for regular Agents page chats (opt-in MCP capability awareness for coding CLIs).
        public static string SharedHarnessHint(AgentChat chat, string agentId)
        {
            if (chat == null || IsAssistant(chat)) return "";
            if (string.IsNullOrWhiteSpace(agentId) || !McpCapableIds.Contains(agentId.Trim())) return "";
            return "[All in One shared MCP (when attached): session_search, memory_manage, skill_manage, vault_list/vault_schema/vault_query/vault_search, canvas_update, dashboard_publish.]";
        }

        static string ResolveActiveFolder(AgentChat chat, Dictionary<string, object> ctx)
        {
            if (chat != null && !string.IsNullOrWhiteSpace(chat.Folder)) return chat.Folder.Trim();
            string exp = Str(ctx, "explorer_folder");
            if (exp.Length > 0) return exp;
            string note = Str(ctx, "markdown_note");
            if (note.Length > 0) { try { return Path.GetDirectoryName(note); } catch (Exception) { } }
            string doc = Str(ctx, "documents_current");
            if (doc.Length > 0) { try { return Path.GetDirectoryName(doc); } catch (Exception) { } }
            return "";
        }

        static string ReadPrefix(string path, int maxChars)
        {
            using (var sr = new StreamReader(path, Encoding.UTF8, true))
            {
                char[] buf = new char[maxChars];
                int n = sr.Read(buf, 0, maxChars);
                if (n <= 0) return "";
                string s = new string(buf, 0, n);
                if (!sr.EndOfStream) s += "\n[...]";
                return s;
            }
        }

        static void AddMemBlock(List<string> lines, string title, string content, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            lines.Add("### " + title);
            lines.Add(Cut(content.Trim(), maxChars));
        }

        static Dictionary<string, object> SafePageContext()
        {
            try
            {
                if (App.I != null) return App.I.PageContext();
            }
            catch (Exception) { }
            return new Dictionary<string, object> { { "page", "" } };
        }

        static void AppendContextLines(List<string> lines, Dictionary<string, object> ctx)
        {
            if (ctx == null || ctx.Count == 0) return;
            string page = Str(ctx, "page");
            lines.Add("Current page: " + page);
            foreach (var kv in ctx)
            {
                if (kv.Key == "page") continue;
                string val = FormatValue(kv.Value);
                if (val.Length == 0) continue;
                lines.Add(kv.Key + ": " + val);
            }
        }

        static string FormatValue(object value)
        {
            if (value == null) return "";
            var s = value as string;
            if (s != null) return Cut(s, 4000);
            if (value is bool) return ((bool)value) ? "true" : "false";
            if (value is IFormattable) return Convert.ToString(value, CultureInfo.InvariantCulture);
            if (value is IEnumerable || value is IDictionary)
            {
                try { return Cut(Js.Serialize(value), 4000); }
                catch (Exception) { return Cut(Convert.ToString(value, CultureInfo.InvariantCulture), 4000); }
            }
            return Cut(Convert.ToString(value, CultureInfo.InvariantCulture), 4000);
        }

        static string Cut(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }

        static string SanitizeBrackets(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('⟦', '[').Replace('⟧', ']');
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        public static string ApplyMcpArgs(AgentDef d, AgentChat chat, string args)
        {
            if (d == null || !IsAssistant(chat)) return args ?? "";
            args = args ?? "";
            string id = (d.Id ?? "").ToLowerInvariant();
            string exe = Application.ExecutablePath;

            string cfg = WriteMcpConfig(exe);

            if (id == "claude" || id == "codex")
            {
                foreach (var dir in Dirs().Take(12))
                {
                    string flag = "--add-dir " + AgentRun.Quote(dir);
                    if (args.IndexOf(flag, StringComparison.OrdinalIgnoreCase) < 0)
                        args += " " + flag;
                }
            }

            if (id == "claude")
            {
                if (!string.IsNullOrEmpty(cfg) && args.IndexOf("--mcp-config", StringComparison.OrdinalIgnoreCase) < 0)
                    args += " --mcp-config " + AgentRun.Quote(cfg);
                if (args.IndexOf("mcp__all-in-one", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    args = args.Contains("--allowedTools ")
                        ? args.Replace("--allowedTools ", "--allowedTools mcp__all-in-one ")
                        : args + " --allowedTools mcp__all-in-one";
                }
            }
            else if (id == "codex")
            {
                if (args.IndexOf("mcp_servers.all_in_one.command", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    string cmdVal = exe.IndexOf('\'') >= 0
                        ? "\"" + exe.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
                        : "'" + exe + "'";
                    args = "-c " + AgentRun.Quote("mcp_servers.all_in_one.command=" + cmdVal)
                        + " -c " + AgentRun.Quote("mcp_servers.all_in_one.args=[\"--mcp\"]")
                        + " " + args;
                }
            }
            // agy, cline, opencode: keep args untouched so no unsupported CLI flags break startup;
            // they use their installed user MCP config / local named pipe cleanly.

            return args;
        }

        static string WriteMcpConfig(string exe)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "AllInOne-agents");
                Directory.CreateDirectory(dir);
                string cfg = Path.Combine(dir, "assistant-mcp.json");
                var payload = new Dictionary<string, object>
                {
                    {
                        "mcpServers", new Dictionary<string, object>
                        {
                            {
                                "all-in-one", new Dictionary<string, object>
                                {
                                    { "command", exe },
                                    { "args", new[] { "--mcp" } }
                                }
                            }
                        }
                    }
                };
                File.WriteAllText(cfg, Js.Serialize(payload), new UTF8Encoding(false));
                return cfg;
            }
            catch (Exception ex)
            {
                App.Log("Assistant MCP: " + ex.Message);
                return null;
            }
        }
    }
}
