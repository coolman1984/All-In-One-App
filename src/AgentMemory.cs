using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    class MemorySnapshot
    {
        public string StorageRoot = "";
        public string Soul = "";
        public string Tools = "";
        public string User = "";
        public string Memory = "";
        public string Goals = "";
        public string Heartbeat = "";
        public string DailyTodayDate = "";
        public string DailyToday = "";
        public string DailyYesterdayDate = "";
        public string DailyYesterday = "";
        public string SkillsIndex = "";
        public string CapturedUtc = "";
    }

    public class DynamicToolDef
    {
        public string name = "";
        public string title = "";
        public string description = "";
        public string kind = "sql"; // sql | powershell | cmd | python
        public string script = "";
        public object args_schema;
        public bool read_only;
    }

    public class TodoStepItem
    {
        public int id;
        public string text = "";
        public string status = "pending"; // pending | in_progress | done | blocked
    }

    // Combined Hermes Agent + OpenClaw + DeepSeek Harness persistence layer:
    // - Markdown memory files (SOUL/USER/TOOLS/MEMORY/GOALS/HEARTBEAT + daily/YYYY-MM-DD.md)
    // - Per-session Frozen Snapshots for stable prompt-cache prefixes + Lean Subagents
    // - Self-improving SKILL.md engine with Progressive Disclosure index
    // - Dynamic Tool Registry + Creator Mode (data\assistant\tools\*.json hot-loaded into MCP)
    // - Internal per-chat Todo tracker + boundary-safe Context Compaction with daily memory flush
    static class AgentMemory
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        static readonly object memLock = new object();
        static readonly object snapLock = new object();
        static readonly object todoLock = new object();

        static readonly Dictionary<string, MemorySnapshot> frozenSnapshots = new Dictionary<string, MemorySnapshot>(StringComparer.OrdinalIgnoreCase);
        static string snapshotRoot = "";

        static Dictionary<string, List<TodoStepItem>> todoStore;
        static string todoRoot = "";

        public static string MemoryDir { get { return Path.Combine(AssistantHarness.StorageDir, "memory"); } }
        public static string DailyDir { get { return Path.Combine(MemoryDir, "daily"); } }
        public static string SkillsDir { get { return Path.Combine(AssistantHarness.StorageDir, "skills"); } }
        public static string ToolsDir { get { return Path.Combine(AssistantHarness.StorageDir, "tools"); } }
        public static string TodoFile { get { return Path.Combine(AssistantHarness.StorageDir, "todos.json"); } }

        static readonly string[] CoreFiles = { "SOUL.md", "USER.md", "TOOLS.md", "MEMORY.md", "GOALS.md", "HEARTBEAT.md" };

        // =====================================================================
        // Auto-Seeding (Idempotent, Never Overwrites User Customizations)
        // =====================================================================

        public static void EnsureSeeded()
        {
            lock (memLock)
            {
                Directory.CreateDirectory(MemoryDir);
                Directory.CreateDirectory(DailyDir);
                Directory.CreateDirectory(SkillsDir);
                Directory.CreateDirectory(ToolsDir);

                SeedIfMissing("SOUL.md", DefaultSoul());
                SeedIfMissing("USER.md", DefaultUser());
                SeedIfMissing("TOOLS.md", DefaultTools());
                SeedIfMissing("MEMORY.md", DefaultMemory());
                SeedIfMissing("GOALS.md", DefaultGoals());
                SeedIfMissing("HEARTBEAT.md", DefaultHeartbeat());
            }
        }

        static void SeedIfMissing(string fileName, string content)
        {
            string path = Path.Combine(MemoryDir, fileName);
            if (!File.Exists(path)) WriteAtomic(path, content);
        }

        static string DefaultSoul()
        {
            return
                "# SOUL — All in One Autonomous Companion\n\n" +
                "- You are **All in One (الـ Agent العام)** — the owner's proactive, autonomous desktop operator inside this closed corporate/factory workstation.\n" +
                "- **Open Automation:** Act directly through MCP tools without asking for confirmation or giving manual click-by-click instructions.\n" +
                "- **Conversation Style:** Reply in natural, clear **Egyptian Arabic** — short, direct, and packed with key facts (zero fluff).\n" +
                "- **Visual-First Live Communication Canvas (`Browser`):** Whenever explaining architecture, logic, multi-step workflows, report comparisons, plans, or technical concepts, proactively draw interactive studio-grade visuals in the embedded `Browser` using `canvas_update` or `dashboard_publish`:\n" +
                "  - Build custom interactive SVG diagrams, animated data flows, and crisp telemetry cards with minimal Egyptian Arabic labels (`font-variant-numeric: tabular-nums`, clean `dir=\"rtl\"` layout with `dir=\"ltr\"` technical badges).\n" +
                "  - Use the **Obsidian Dark + Warm Drafting Paper** palette (`oklch` with hex fallbacks, spring motion `cubic-bezier(0.22, 1, 0.36, 1)`), strictly following `skills/anti-slop-web-design/SKILL.md` — **ZERO AI slop, ZERO purple/blue neon clichés, ZERO walls of text**.\n" +
                "  - Auto-flip to `Browser` so the user watches the visual live, and keep the chat reply very short.\n" +
                "- **Continuous Evolution:** Save durable facts in `MEMORY.md`, daily work notes in `daily`, and repeatable workflows via `skill_manage` and `tool_create`.\n";
        }

        static string DefaultUser()
        {
            return
                "# USER — Owner Preferences\n\n" +
                "- **Language:** Natural Egyptian Arabic, concise and direct.\n" +
                "- **Environment:** Isolated corporate/factory local intranet behind an enterprise firewall; Office files may be NASCA DRM protected (handle via live COM or `vault_extract`).\n" +
                "- **Working Style:** Prefers autonomous execution, watching live page navigation across All in One, SQLite cross-report joins, and interactive Live Canvas visuals over long text paragraphs.\n";
        }

        static string DefaultTools()
        {
            return
                "# TOOLS — Local Workstation Map\n\n" +
                "- **20-Page Workspace:** `page_context`, `open_page`, `document_open`, `document_switch`, `document_save`, `explorer_open`, `browser_open`, `markdown_open`, `terminal_open`.\n" +
                "- **NASCA-Safe Office COM:** `excel_info/read/write/formula/sheet_add/format`, `word_read/write`, `ppt_read/write`, `office_extract`.\n" +
                "- **SQLite Vault (`data\\vault.db`):** `vault_list`, `vault_extract`, `vault_schema`, `vault_query` (supports arbitrary cross-report `SQL JOIN`s), `vault_search`.\n" +
                "- **Memory, Skills & Creator Mode:** `memory_manage`, `session_search`, `skill_manage`, `tool_list`, `tool_create`, `tool_delete`, `todo_plan`, `chat_steps`, `chat_compact`.\n" +
                "- **Proactive Automation & Live Canvas:** `cron_manage`, `heartbeat_status`, `watch_folder`, `canvas_update`, `dashboard_publish`, `dashboard_list`.\n";
        }

        static string DefaultMemory()
        {
            return
                "# MEMORY — Durable Facts\n\n" +
                "- Workspace SQLite Vault lives at `data\\vault.db` with WAL mode and per-sheet SQL tables (`data_<docId>_<slug>`).\n" +
                "- Dynamic tools live under `data\\assistant\\tools\\*.json` and hot-register into MCP without rebuilding the app.\n";
        }

        static string DefaultGoals()
        {
            return
                "# GOALS — Standing Proactive Ownership\n\n" +
                "- Keep opened and watched Excel/Word/PowerPoint/PDF reports indexed cleanly in `vault.db`.\n" +
                "- Track overdue tasks and surface only high-signal, non-duplicate reminders.\n" +
                "- Codify repeated multi-step tasks into reusable skills (`skill_manage`) or dynamic tools (`tool_create`).\n";
        }

        static string DefaultHeartbeat()
        {
            return
                "# HEARTBEAT — Idle-Only 0-Token Checks\n\n" +
                "- Run ONLY when the companion is idle.\n" +
                "- Check local signals with 0 LLM tokens: watched report folders, overdue tasks, and fixed local disk usage (> 90%).\n" +
                "- If nothing changed or alert hash was already surfaced within cooldown -> return `HEARTBEAT_OK` silently.\n";
        }

        // =====================================================================
        // Frozen Per-Chat Memory Snapshots (Prompt-Cache Stable + Lean Mode)
        // =====================================================================

        public static MemorySnapshot GetSnapshot(string chatId)
        {
            EnsureSeeded();
            string root = AssistantHarness.StorageDir;
            string key = string.IsNullOrWhiteSpace(chatId) ? "__default__" : chatId.Trim();

            lock (snapLock)
            {
                if (!string.Equals(snapshotRoot, root, StringComparison.OrdinalIgnoreCase))
                {
                    frozenSnapshots.Clear();
                    snapshotRoot = root;
                }
                MemorySnapshot cached;
                if (frozenSnapshots.TryGetValue(key, out cached)) return cached;

                var snap = CaptureSnapshotFromDisk(root);
                frozenSnapshots[key] = snap;
                return snap;
            }
        }

        public static string GetFrozenSnapshot(string chatId, bool lean)
        {
            var snap = GetSnapshot(chatId);
            var sb = new StringBuilder();

            if (!lean && !string.IsNullOrWhiteSpace(snap.Soul))
                AppendSection(sb, "SOUL.md", snap.Soul, 4000);
            if (!string.IsNullOrWhiteSpace(snap.Tools))
                AppendSection(sb, "TOOLS.md", snap.Tools, 3000);
            if (!string.IsNullOrWhiteSpace(snap.User))
                AppendSection(sb, "USER.md", snap.User, 2500);
            if (!string.IsNullOrWhiteSpace(snap.Memory))
                AppendSection(sb, "MEMORY.md", snap.Memory, 4000);
            if (!string.IsNullOrWhiteSpace(snap.Goals))
                AppendSection(sb, "GOALS.md", snap.Goals, 2500);
            if (!string.IsNullOrWhiteSpace(snap.DailyToday))
                AppendSection(sb, "daily/" + snap.DailyTodayDate + ".md", snap.DailyToday, 2500);
            if (!string.IsNullOrWhiteSpace(snap.DailyYesterday))
                AppendSection(sb, "daily/" + snap.DailyYesterdayDate + ".md", snap.DailyYesterday, 2000);
            if (!string.IsNullOrWhiteSpace(snap.SkillsIndex))
                AppendSection(sb, "Skills Index (Progressive Disclosure — call skill_manage read <slug> for full instructions)", snap.SkillsIndex, 3500);

            return sb.ToString().TrimEnd();
        }

        public static void ResetSnapshot(string chatId = null)
        {
            lock (snapLock)
            {
                if (string.IsNullOrWhiteSpace(chatId)) frozenSnapshots.Clear();
                else frozenSnapshots.Remove(chatId.Trim());
            }
        }

        static MemorySnapshot CaptureSnapshotFromDisk(string root)
        {
            DateTime today = DateTime.Now.Date;
            string todayStr = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string yestStr = today.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return new MemorySnapshot
            {
                StorageRoot = root,
                Soul = ReadTextSafe(Path.Combine(MemoryDir, "SOUL.md")),
                Tools = ReadTextSafe(Path.Combine(MemoryDir, "TOOLS.md")),
                User = ReadTextSafe(Path.Combine(MemoryDir, "USER.md")),
                Memory = ReadTextSafe(Path.Combine(MemoryDir, "MEMORY.md")),
                Goals = ReadTextSafe(Path.Combine(MemoryDir, "GOALS.md")),
                Heartbeat = ReadTextSafe(Path.Combine(MemoryDir, "HEARTBEAT.md")),
                DailyTodayDate = todayStr,
                DailyToday = ReadTextSafe(Path.Combine(DailyDir, todayStr + ".md")),
                DailyYesterdayDate = yestStr,
                DailyYesterday = ReadTextSafe(Path.Combine(DailyDir, yestStr + ".md")),
                SkillsIndex = BuildSkillsIndexText(),
                CapturedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            };
        }

        static void AppendSection(StringBuilder sb, string heading, string body, int maxChars)
        {
            string trimmed = (body ?? "").Trim();
            if (trimmed.Length == 0) return;
            if (trimmed.Length > maxChars) trimmed = trimmed.Substring(0, maxChars) + "\n[...]";
            sb.AppendLine("### " + heading);
            sb.AppendLine(trimmed);
            sb.AppendLine();
        }

        // =====================================================================
        // Memory Manage (Atomic Persistence Without Mutating Frozen Snapshots)
        // =====================================================================

        public static Dictionary<string, object> MemoryManage(string file, string action, string content = "", string oldText = "")
        {
            EnsureSeeded();
            string act = string.IsNullOrWhiteSpace(action) ? "read" : action.Trim().ToLowerInvariant();

            if (act == "reset_snapshot")
            {
                ResetSnapshot(string.IsNullOrWhiteSpace(content) ? null : content.Trim());
                return new Dictionary<string, object> { { "action", "reset_snapshot" }, { "ok", true } };
            }

            if (act == "list" || (act == "read" && string.Equals((file ?? "").Trim(), "all", StringComparison.OrdinalIgnoreCase)))
            {
                var files = new List<Dictionary<string, object>>();
                foreach (var name in CoreFiles)
                {
                    string p = Path.Combine(MemoryDir, name);
                    long sz = File.Exists(p) ? new FileInfo(p).Length : 0;
                    files.Add(new Dictionary<string, object> { { "file", name }, { "path", p }, { "size", sz } });
                }
                if (Directory.Exists(DailyDir))
                {
                    foreach (var dp in Directory.GetFiles(DailyDir, "*.md").OrderByDescending(x => x).Take(14))
                    {
                        files.Add(new Dictionary<string, object>
                        {
                            { "file", "daily/" + Path.GetFileName(dp) },
                            { "path", dp },
                            { "size", new FileInfo(dp).Length }
                        });
                    }
                }
                return new Dictionary<string, object> { { "action", "list" }, { "files", files } };
            }

            string targetName;
            string targetPath = ResolveMemoryFile(act == "daily_log" && string.IsNullOrWhiteSpace(file) ? "daily" : file, out targetName);

            lock (memLock)
            {
                if (act == "read")
                {
                    string text = ReadTextSafe(targetPath);
                    return new Dictionary<string, object>
                    {
                        { "file", targetName },
                        { "path", targetPath },
                        { "exists", File.Exists(targetPath) },
                        { "content", text }
                    };
                }

                if (act == "add" || act == "append" || act == "daily_log")
                {
                    string addition = (content ?? "").Trim();
                    if (addition.Length == 0) throw new ArgumentException("content is required for " + act + ".");
                    string existing = ReadTextSafe(targetPath);
                    string line = act == "daily_log"
                        ? ("- [" + DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture) + "] " + addition)
                        : addition;

                    string updated;
                    if (string.IsNullOrWhiteSpace(existing))
                    {
                        if (targetName.StartsWith("daily/", StringComparison.OrdinalIgnoreCase))
                            updated = "# Daily Log — " + Path.GetFileNameWithoutExtension(targetName) + "\n\n" + line + "\n";
                        else
                            updated = line + "\n";
                    }
                    else
                    {
                        updated = existing.TrimEnd('\r', '\n') + "\n" + line + "\n";
                    }
                    WriteAtomic(targetPath, updated);
                    return new Dictionary<string, object>
                    {
                        { "file", targetName },
                        { "path", targetPath },
                        { "action", act },
                        { "ok", true },
                        { "length", updated.Length }
                    };
                }

                if (act == "replace" || act == "patch")
                {
                    string existing = ReadTextSafe(targetPath);
                    string updated;
                    if (!string.IsNullOrEmpty(oldText))
                    {
                        int idx = existing.IndexOf(oldText, StringComparison.Ordinal);
                        if (idx < 0) throw new InvalidOperationException("old_text was not found in " + targetName + ".");
                        updated = existing.Substring(0, idx) + (content ?? "") + existing.Substring(idx + oldText.Length);
                    }
                    else
                    {
                        updated = content ?? "";
                    }
                    WriteAtomic(targetPath, updated);
                    return new Dictionary<string, object>
                    {
                        { "file", targetName },
                        { "path", targetPath },
                        { "action", act },
                        { "ok", true },
                        { "length", updated.Length }
                    };
                }

                if (act == "remove" || act == "delete")
                {
                    string needle = !string.IsNullOrEmpty(oldText) ? oldText : (content ?? "");
                    if (string.IsNullOrEmpty(needle)) throw new ArgumentException("old_text or content is required for remove.");
                    string existing = ReadTextSafe(targetPath);
                    int idx = existing.IndexOf(needle, StringComparison.Ordinal);
                    if (idx < 0) return new Dictionary<string, object> { { "file", targetName }, { "action", "remove" }, { "ok", false }, { "removed", false } };
                    string updated = existing.Remove(idx, needle.Length);
                    updated = Regex.Replace(updated, @"\n{3,}", "\n\n");
                    WriteAtomic(targetPath, updated);
                    return new Dictionary<string, object>
                    {
                        { "file", targetName },
                        { "path", targetPath },
                        { "action", "remove" },
                        { "ok", true },
                        { "removed", true },
                        { "length", updated.Length }
                    };
                }

                throw new ArgumentException("Unknown memory_manage action: " + action + ". Use read, add, replace, remove, daily_log, list, or reset_snapshot.");
            }
        }

        static string ResolveMemoryFile(string file, out string canonicalName)
        {
            string raw = (file ?? "").Trim();
            if (raw.Length == 0) raw = "MEMORY.md";
            string lower = raw.Replace('\\', '/').Trim('/').ToLowerInvariant();

            if (lower == "daily" || lower == "today" || lower == "daily/today")
            {
                string today = DateTime.Now.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                canonicalName = "daily/" + today + ".md";
                return Path.Combine(DailyDir, today + ".md");
            }
            if (lower == "yesterday" || lower == "daily/yesterday")
            {
                string yest = DateTime.Now.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                canonicalName = "daily/" + yest + ".md";
                return Path.Combine(DailyDir, yest + ".md");
            }

            string datePart = lower.StartsWith("daily/") ? lower.Substring(6) : lower;
            if (datePart.EndsWith(".md")) datePart = datePart.Substring(0, datePart.Length - 3);
            if (Regex.IsMatch(datePart, @"^\d{4}-\d{2}-\d{2}$"))
            {
                canonicalName = "daily/" + datePart + ".md";
                return Path.Combine(DailyDir, datePart + ".md");
            }

            string baseName = lower.EndsWith(".md") ? lower : (lower + ".md");
            for (int i = 0; i < CoreFiles.Length; i++)
            {
                if (string.Equals(CoreFiles[i], baseName, StringComparison.OrdinalIgnoreCase))
                {
                    canonicalName = CoreFiles[i];
                    return Path.Combine(MemoryDir, CoreFiles[i]);
                }
            }

            throw new ArgumentException("Unsupported memory file: " + file + ". Use SOUL.md, USER.md, TOOLS.md, MEMORY.md, GOALS.md, HEARTBEAT.md, or daily/YYYY-MM-DD.md.");
        }

        // =====================================================================
        // Self-Improving Skills Engine (Progressive Disclosure Index)
        // =====================================================================

        static List<string> SkillSearchRoots()
        {
            var roots = new List<string>();
            roots.Add(SkillsDir);
            var bases = new List<string>();
            try
            {
                string asmLoc = typeof(AgentMemory).Assembly.Location;
                if (!string.IsNullOrEmpty(asmLoc)) bases.Add(Path.GetDirectoryName(asmLoc));
            }
            catch (Exception) { }
            try
            {
                string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(exeDir)) bases.Add(exeDir);
            }
            catch (Exception) { }

            for (int i = 0; i < bases.Count; i++)
            {
                string b = bases[i];
                if (string.IsNullOrEmpty(b)) continue;
                try
                {
                    string sibling = Path.Combine(b, "skills");
                    if (Directory.Exists(sibling)) roots.Add(sibling);
                    DirectoryInfo parent = Directory.GetParent(b);
                    if (parent != null)
                    {
                        string parentSkills = Path.Combine(parent.FullName, "skills");
                        if (Directory.Exists(parentSkills)) roots.Add(parentSkills);
                    }
                }
                catch (Exception) { }
            }
            return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<Dictionary<string, object>> ListSkills()
        {
            EnsureSeeded();
            var list = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = SkillSearchRoots();

            for (int r = 0; r < roots.Count; r++)
            {
                string root = roots[r];
                if (!Directory.Exists(root)) continue;
                string source = string.Equals(root, SkillsDir, StringComparison.OrdinalIgnoreCase) ? "assistant" : "workspace";
                string[] dirs;
                try { dirs = Directory.GetDirectories(root); }
                catch (Exception) { continue; }
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < dirs.Length; i++)
                {
                    string slug = Path.GetFileName(dirs[i]);
                    string skillFile = Path.Combine(dirs[i], "SKILL.md");
                    if (!File.Exists(skillFile) || !seen.Add(slug)) continue;
                    string name, desc;
                    ParseSkillHeader(ReadTextSafe(skillFile), slug, out name, out desc);
                    list.Add(new Dictionary<string, object>
                    {
                        { "slug", slug },
                        { "name", name },
                        { "description", desc },
                        { "source", source },
                        { "path", skillFile }
                    });
                }
            }
            return list;
        }

        public static string BuildSkillsIndexText()
        {
            var skills = ListSkills();
            if (skills.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < skills.Count; i++)
            {
                var s = skills[i];
                string slug = Convert.ToString(s["slug"], CultureInfo.InvariantCulture);
                string desc = Convert.ToString(s["description"], CultureInfo.InvariantCulture);
                if (desc.Length > 180) desc = desc.Substring(0, 177) + "...";
                sb.Append("- `").Append(slug).Append("`: ").AppendLine(desc);
            }
            return sb.ToString().TrimEnd();
        }

        public static Dictionary<string, object> SkillManage(string action, string slug = "", string content = "", string oldText = "")
        {
            EnsureSeeded();
            string act = string.IsNullOrWhiteSpace(action) ? "list" : action.Trim().ToLowerInvariant();

            if (act == "list")
            {
                var skills = ListSkills();
                return new Dictionary<string, object>
                {
                    { "action", "list" },
                    { "count", skills.Count },
                    { "skills", skills }
                };
            }

            string cleanSlug = SanitizeSlug(slug);
            if (cleanSlug.Length == 0) throw new ArgumentException("slug is required for skill_manage " + act + ".");

            if (act == "read")
            {
                string found = FindSkillPath(cleanSlug);
                if (found == null) throw new FileNotFoundException("Skill not found: " + cleanSlug);
                string body = ReadTextSafe(found);
                string name, desc;
                ParseSkillHeader(body, cleanSlug, out name, out desc);
                return new Dictionary<string, object>
                {
                    { "action", "read" },
                    { "slug", cleanSlug },
                    { "name", name },
                    { "description", desc },
                    { "path", found },
                    { "content", body }
                };
            }

            string userSkillDir = Path.Combine(SkillsDir, cleanSlug);
            string userSkillFile = Path.Combine(userSkillDir, "SKILL.md");

            if (act == "save")
            {
                if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("content is required for skill_manage save.");
                Directory.CreateDirectory(userSkillDir);
                WriteAtomic(userSkillFile, content);
                string name, desc;
                ParseSkillHeader(content, cleanSlug, out name, out desc);
                return new Dictionary<string, object>
                {
                    { "action", "save" },
                    { "slug", cleanSlug },
                    { "name", name },
                    { "description", desc },
                    { "path", userSkillFile },
                    { "saved", true }
                };
            }

            if (act == "patch")
            {
                string srcPath = File.Exists(userSkillFile) ? userSkillFile : FindSkillPath(cleanSlug);
                if (srcPath == null) throw new FileNotFoundException("Skill not found to patch: " + cleanSlug);
                if (string.IsNullOrEmpty(oldText)) throw new ArgumentException("old_text is required for skill_manage patch.");
                string existing = ReadTextSafe(srcPath);
                int idx = existing.IndexOf(oldText, StringComparison.Ordinal);
                if (idx < 0) throw new InvalidOperationException("old_text was not found in skill " + cleanSlug + ".");
                string updated = existing.Substring(0, idx) + (content ?? "") + existing.Substring(idx + oldText.Length);
                Directory.CreateDirectory(userSkillDir);
                WriteAtomic(userSkillFile, updated);
                return new Dictionary<string, object>
                {
                    { "action", "patch" },
                    { "slug", cleanSlug },
                    { "path", userSkillFile },
                    { "patched", true },
                    { "length", updated.Length }
                };
            }

            if (act == "delete")
            {
                bool existed = Directory.Exists(userSkillDir);
                if (existed) Directory.Delete(userSkillDir, true);
                return new Dictionary<string, object>
                {
                    { "action", "delete" },
                    { "slug", cleanSlug },
                    { "deleted", existed }
                };
            }

            throw new ArgumentException("Unknown skill_manage action: " + action + ". Use list, read, save, patch, or delete.");
        }

        static string FindSkillPath(string slug)
        {
            var roots = SkillSearchRoots();
            for (int i = 0; i < roots.Count; i++)
            {
                string candidate = Path.Combine(roots[i], slug, "SKILL.md");
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        static void ParseSkillHeader(string text, string fallbackSlug, out string name, out string desc)
        {
            name = fallbackSlug;
            desc = "";
            if (string.IsNullOrWhiteSpace(text)) return;

            string norm = text.Replace("\r\n", "\n").Replace("\r", "\n");
            if (norm.StartsWith("---\n"))
            {
                int end = norm.IndexOf("\n---", 4, StringComparison.Ordinal);
                if (end > 4)
                {
                    string fm = norm.Substring(4, end - 4);
                    foreach (var rawLine in fm.Split('\n'))
                    {
                        string line = rawLine.Trim();
                        if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring(5).Trim().Trim('"', '\'');
                            if (v.Length > 0) name = v;
                        }
                        else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                        {
                            string v = line.Substring(12).Trim().Trim('"', '\'');
                            if (v.Length > 0) desc = v;
                        }
                    }
                }
            }

            if (desc.Length == 0)
            {
                foreach (var rawLine in norm.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line == "---") continue;
                    if (line.StartsWith("#"))
                    {
                        if (name == fallbackSlug) name = line.TrimStart('#', ' ');
                        continue;
                    }
                    desc = line;
                    break;
                }
            }
            if (desc.Length == 0) desc = name;
        }

        // =====================================================================
        // DeepSeek Harness Dynamic Tool Registry + Creator Mode
        // =====================================================================

        public static List<DynamicToolDef> ToolList()
        {
            EnsureSeeded();
            var list = new List<DynamicToolDef>();
            if (!Directory.Exists(ToolsDir)) return list;
            string[] files;
            try { files = Directory.GetFiles(ToolsDir, "*.json"); }
            catch (Exception) { return list; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    var def = Js.Deserialize<DynamicToolDef>(File.ReadAllText(files[i], Encoding.UTF8));
                    if (def == null) continue;
                    if (string.IsNullOrWhiteSpace(def.name)) def.name = Path.GetFileNameWithoutExtension(files[i]);
                    def.name = SanitizeToolName(def.name);
                    if (def.name.Length == 0) continue;
                    if (string.IsNullOrWhiteSpace(def.title)) def.title = def.name;
                    if (string.IsNullOrWhiteSpace(def.kind)) def.kind = "sql";
                    list.Add(def);
                }
                catch (Exception) { }
            }
            return list;
        }

        public static bool HasDynamicTool(string name)
        {
            string clean = SanitizeToolName(name);
            if (clean.Length == 0) return false;
            return File.Exists(Path.Combine(ToolsDir, clean + ".json"));
        }

        public static Dictionary<string, object> ToolCreate(string name, string title, string description, string kind, string script, object argsSchema, bool? readOnly, HashSet<string> builtInNames)
        {
            EnsureSeeded();
            string cleanName = SanitizeToolName(name);
            if (cleanName.Length == 0) throw new ArgumentException("Valid tool name is required (letters, digits, underscore).");
            if (builtInNames != null && builtInNames.Contains(cleanName))
                throw new InvalidOperationException("Cannot overwrite built-in MCP tool: " + cleanName);

            string cleanKind = string.IsNullOrWhiteSpace(kind) ? "sql" : kind.Trim().ToLowerInvariant();
            if (cleanKind != "sql" && cleanKind != "powershell" && cleanKind != "cmd" && cleanKind != "python")
                throw new ArgumentException("Unsupported tool kind: " + kind + ". Use sql, powershell, cmd, or python.");
            if (string.IsNullOrWhiteSpace(script))
                throw new ArgumentException("script is required.");

            object normalizedSchema = NormalizeArgsSchema(argsSchema);
            var def = new DynamicToolDef
            {
                name = cleanName,
                title = string.IsNullOrWhiteSpace(title) ? cleanName : title.Trim(),
                description = string.IsNullOrWhiteSpace(description) ? ("Dynamic " + cleanKind + " tool: " + cleanName) : description.Trim(),
                kind = cleanKind,
                script = script,
                args_schema = normalizedSchema,
                read_only = readOnly.HasValue ? readOnly.Value : (cleanKind == "sql")
            };

            string path = Path.Combine(ToolsDir, cleanName + ".json");
            WriteAtomic(path, Js.Serialize(def));
            AssistantHarness.Report("Registered dynamic tool: " + cleanName + " (" + cleanKind + ")");
            return new Dictionary<string, object>
            {
                { "created", true },
                { "name", def.name },
                { "title", def.title },
                { "kind", def.kind },
                { "path", path }
            };
        }

        public static Dictionary<string, object> ToolDelete(string name)
        {
            string cleanName = SanitizeToolName(name);
            if (cleanName.Length == 0) throw new ArgumentException("name is required.");
            string path = Path.Combine(ToolsDir, cleanName + ".json");
            bool existed = File.Exists(path);
            if (existed) File.Delete(path);
            return new Dictionary<string, object>
            {
                { "name", cleanName },
                { "deleted", existed }
            };
        }

        public static List<Dictionary<string, object>> BuildDynamicToolSchemas(HashSet<string> builtInNames)
        {
            var result = new List<Dictionary<string, object>>();
            var tools = ToolList();
            for (int i = 0; i < tools.Count; i++)
            {
                var t = tools[i];
                if (builtInNames != null && builtInNames.Contains(t.name)) continue;
                var inputSchema = BuildInputSchema(t.args_schema);
                result.Add(new Dictionary<string, object>
                {
                    { "name", t.name },
                    { "title", t.title },
                    { "description", t.description + " [dynamic:" + t.kind + "]" },
                    { "inputSchema", inputSchema },
                    { "annotations", new Dictionary<string, object>
                        {
                            { "title", t.title },
                            { "readOnlyHint", t.read_only },
                            { "destructiveHint", false },
                            { "openWorldHint", false }
                        }
                    }
                });
            }
            return result;
        }

        public static Dictionary<string, object> RunDynamicTool(string name, Dictionary<string, object> args)
        {
            string cleanName = SanitizeToolName(name);
            string path = Path.Combine(ToolsDir, cleanName + ".json");
            if (!File.Exists(path)) throw new FileNotFoundException("Dynamic tool not found: " + name);

            var def = Js.Deserialize<DynamicToolDef>(File.ReadAllText(path, Encoding.UTF8));
            if (def == null) throw new InvalidOperationException("Invalid dynamic tool definition: " + name);
            string kind = (def.kind ?? "sql").Trim().ToLowerInvariant();
            args = args ?? new Dictionary<string, object>();

            if (kind == "sql")
            {
                string sql = RenderSqlTemplate(def.script ?? "", args);
                int maxRows = 500;
                object mr;
                if (args.TryGetValue("max_rows", out mr) && mr != null)
                {
                    int parsed;
                    if (int.TryParse(Convert.ToString(mr, CultureInfo.InvariantCulture), out parsed) && parsed > 0)
                        maxRows = parsed;
                }
                var qres = SqliteVault.QuerySql(sql, maxRows);
                qres["tool"] = cleanName;
                qres["kind"] = "sql";
                qres["ok"] = true;
                return qres;
            }

            return RunScriptTool(cleanName, kind, def.script ?? "", args);
        }

        static string RenderSqlTemplate(string template, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Count == 0) return template;
            return Regex.Replace(template, @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", delegate(Match m)
            {
                string key = m.Groups[1].Value;
                object val = null;
                foreach (var kv in args)
                {
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        val = kv.Value;
                        break;
                    }
                }
                if (val == null) return "NULL";
                if (val is int || val is long || val is double || val is float || val is decimal)
                    return Convert.ToString(val, CultureInfo.InvariantCulture);
                if (val is bool) return ((bool)val) ? "1" : "0";
                string s = Convert.ToString(val, CultureInfo.InvariantCulture) ?? "";
                return "'" + s.Replace("'", "''") + "'";
            });
        }

        static Dictionary<string, object> RunScriptTool(string name, string kind, string script, Dictionary<string, object> args)
        {
            string ext = kind == "powershell" ? ".ps1" : kind == "python" ? ".py" : ".cmd";
            string tempDir = Path.Combine(Path.GetTempPath(), "AllInOne-tools");
            Directory.CreateDirectory(tempDir);
            string scriptPath = Path.Combine(tempDir, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ext);

            try
            {
                File.WriteAllText(scriptPath, script, Utf8NoBom);
                string exe, cmdArgs;
                if (kind == "powershell")
                {
                    exe = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                    if (!File.Exists(exe)) exe = "powershell.exe";
                    cmdArgs = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + AgentRun.Quote(scriptPath);
                }
                else if (kind == "python")
                {
                    exe = "python";
                    cmdArgs = AgentRun.Quote(scriptPath);
                }
                else
                {
                    exe = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                    cmdArgs = "/d /c " + AgentRun.Quote(scriptPath);
                }

                var psi = new ProcessStartInfo(exe, cmdArgs)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                psi.EnvironmentVariables["AIO_TOOL_NAME"] = name;
                psi.EnvironmentVariables["AIO_TOOL_ARGS"] = Js.Serialize(args);
                psi.EnvironmentVariables["AIO_DATA_DIR"] = string.IsNullOrEmpty(Store.Dir) ? Store.DefaultDir : Store.Dir;
                psi.EnvironmentVariables["AIO_VAULT_DB"] = SqliteVault.DbPath;

                using (var proc = Process.Start(psi))
                {
                    var outTask = proc.StandardOutput.ReadToEndAsync();
                    var errTask = proc.StandardError.ReadToEndAsync();
                    if (!proc.WaitForExit(60000))
                    {
                        try { proc.Kill(); } catch (Exception) { }
                        return new Dictionary<string, object>
                        {
                            { "tool", name },
                            { "kind", kind },
                            { "ok", false },
                            { "exit_code", -1 },
                            { "output", "" },
                            { "error", "Dynamic tool timed out after 60 s." }
                        };
                    }
                    outTask.Wait(2000);
                    errTask.Wait(2000);
                    string stdout = (outTask.IsCompleted ? outTask.Result : "") ?? "";
                    string stderr = (errTask.IsCompleted ? errTask.Result : "") ?? "";
                    return new Dictionary<string, object>
                    {
                        { "tool", name },
                        { "kind", kind },
                        { "ok", proc.ExitCode == 0 },
                        { "exit_code", proc.ExitCode },
                        { "output", stdout.TrimEnd() },
                        { "error", stderr.TrimEnd() }
                    };
                }
            }
            finally
            {
                try { if (File.Exists(scriptPath)) File.Delete(scriptPath); }
                catch (Exception) { }
            }
        }

        static object NormalizeArgsSchema(object raw)
        {
            if (raw == null) return new Dictionary<string, object>();
            string s = raw as string;
            if (s != null)
            {
                string trimmed = s.Trim();
                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                {
                    try { return Js.DeserializeObject(trimmed); }
                    catch (Exception) { return new Dictionary<string, object>(); }
                }
                return new Dictionary<string, object>();
            }
            return raw;
        }

        static Dictionary<string, object> BuildInputSchema(object rawSchema)
        {
            var props = new Dictionary<string, object>();
            var required = new List<string>();

            var dict = rawSchema as Dictionary<string, object>;
            if (dict != null)
            {
                if (dict.ContainsKey("type") && dict.ContainsKey("properties"))
                    return dict;
                foreach (var kv in dict)
                {
                    var sub = kv.Value as Dictionary<string, object>;
                    if (sub != null)
                    {
                        props[kv.Key] = sub;
                    }
                    else
                    {
                        string desc = Convert.ToString(kv.Value ?? "", CultureInfo.InvariantCulture);
                        props[kv.Key] = new Dictionary<string, object> { { "type", "string" }, { "description", desc } };
                    }
                }
            }
            else
            {
                var seq = rawSchema as IEnumerable;
                if (seq != null && !(rawSchema is string))
                {
                    foreach (var item in seq)
                    {
                        string spec = Convert.ToString(item ?? "", CultureInfo.InvariantCulture);
                        if (string.IsNullOrWhiteSpace(spec)) continue;
                        var parts = spec.Split('|');
                        string pName = parts[0].Trim();
                        string pType = parts.Length > 1 && parts[1].Trim().Length > 0 ? parts[1].Trim() : "string";
                        string pDesc = parts.Length > 2 ? parts[2].Trim() : "";
                        bool req = parts.Length > 3 && parts[3].Trim().Equals("required", StringComparison.OrdinalIgnoreCase);
                        if (pName.Length > 0)
                        {
                            props[pName] = new Dictionary<string, object> { { "type", pType }, { "description", pDesc } };
                            if (req) required.Add(pName);
                        }
                    }
                }
            }

            var input = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", props }
            };
            if (required.Count > 0) input["required"] = required.ToArray();
            return input;
        }

        // =====================================================================
        // Internal Per-Chat Todo Tracker (todo_plan)
        // =====================================================================

        public static Dictionary<string, object> TodoManage(string chatId, string action, List<Dictionary<string, object>> steps, int stepIndex, string status, string text)
        {
            string key = string.IsNullOrWhiteSpace(chatId) ? "default" : chatId.Trim();
            string act = string.IsNullOrWhiteSpace(action) ? "get" : action.Trim().ToLowerInvariant();

            lock (todoLock)
            {
                EnsureTodosLoadedLocked();

                if (act == "get")
                {
                    List<TodoStepItem> cur;
                    if (!todoStore.TryGetValue(key, out cur)) cur = new List<TodoStepItem>();
                    return BuildTodoResponse(key, cur);
                }

                if (act == "clear")
                {
                    todoStore.Remove(key);
                    SaveTodosLocked();
                    return new Dictionary<string, object> { { "chat_id", key }, { "cleared", true }, { "steps", new List<object>() } };
                }

                if (act == "set")
                {
                    var list = new List<TodoStepItem>();
                    if (steps != null)
                    {
                        for (int i = 0; i < steps.Count; i++)
                        {
                            var s = steps[i];
                            if (s == null) continue;
                            string stText = DictStr(s, "text");
                            if (stText.Length == 0) stText = DictStr(s, "title");
                            if (stText.Length == 0) continue;
                            string stStatus = NormalizeTodoStatus(DictStr(s, "status"));
                            list.Add(new TodoStepItem { id = list.Count + 1, text = stText, status = stStatus });
                        }
                    }
                    todoStore[key] = list;
                    SaveTodosLocked();
                    ReportTodoProgress(list);
                    return BuildTodoResponse(key, list);
                }

                if (act == "update")
                {
                    List<TodoStepItem> list;
                    if (!todoStore.TryGetValue(key, out list) || list == null) list = todoStore[key] = new List<TodoStepItem>();
                    TodoStepItem item = null;
                    if (stepIndex >= 1 && stepIndex <= list.Count) item = list[stepIndex - 1];
                    else if (stepIndex > 0) item = list.FirstOrDefault(x => x.id == stepIndex);
                    if (item == null)
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            item = new TodoStepItem { id = list.Count + 1, text = text.Trim(), status = NormalizeTodoStatus(status) };
                            list.Add(item);
                        }
                        else throw new ArgumentException("Step " + stepIndex + " not found in todo_plan for chat " + key + ".");
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(status)) item.status = NormalizeTodoStatus(status);
                        if (!string.IsNullOrWhiteSpace(text)) item.text = text.Trim();
                    }
                    SaveTodosLocked();
                    ReportTodoProgress(list);
                    return BuildTodoResponse(key, list);
                }

                throw new ArgumentException("Unknown todo_plan action: " + action + ". Use set, update, get, or clear.");
            }
        }

        public static string FormatTodoForPrompt(string chatId)
        {
            string key = string.IsNullOrWhiteSpace(chatId) ? "default" : chatId.Trim();
            lock (todoLock)
            {
                EnsureTodosLoadedLocked();
                List<TodoStepItem> list;
                if (!todoStore.TryGetValue(key, out list) || list == null || list.Count == 0) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    string mark = it.status == "done" ? "[x]" : it.status == "in_progress" ? "[~]" : it.status == "blocked" ? "[!]" : "[ ]";
                    sb.Append(it.id).Append(". ").Append(mark).Append(' ').AppendLine(it.text);
                }
                return sb.ToString().TrimEnd();
            }
        }

        static void ReportTodoProgress(List<TodoStepItem> list)
        {
            if (list == null || list.Count == 0) return;
            int done = list.Count(x => x.status == "done");
            var active = list.FirstOrDefault(x => x.status == "in_progress") ?? list.FirstOrDefault(x => x.status == "pending") ?? list[list.Count - 1];
            int stepNum = active != null ? active.id : done;
            AssistantHarness.Report("Step " + stepNum + "/" + list.Count + " (" + done + " done): " + (active != null ? active.text : "Completed"));
        }

        static Dictionary<string, object> BuildTodoResponse(string chatId, List<TodoStepItem> list)
        {
            int done = list.Count(x => x.status == "done");
            return new Dictionary<string, object>
            {
                { "chat_id", chatId },
                { "total", list.Count },
                { "done", done },
                { "steps", list.Select(x => new Dictionary<string, object> { { "id", x.id }, { "text", x.text }, { "status", x.status } }).ToList() }
            };
        }

        static string NormalizeTodoStatus(string status)
        {
            string s = (status ?? "").Trim().ToLowerInvariant();
            if (s == "done" || s == "completed" || s == "complete") return "done";
            if (s == "in_progress" || s == "active" || s == "running" || s == "working") return "in_progress";
            if (s == "blocked" || s == "failed") return "blocked";
            return "pending";
        }

        static void EnsureTodosLoadedLocked()
        {
            string root = AssistantHarness.StorageDir;
            if (todoStore != null && string.Equals(todoRoot, root, StringComparison.OrdinalIgnoreCase)) return;
            todoRoot = root;
            todoStore = new Dictionary<string, List<TodoStepItem>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(TodoFile))
                {
                    var loaded = Js.Deserialize<Dictionary<string, List<TodoStepItem>>>(File.ReadAllText(TodoFile, Encoding.UTF8));
                    if (loaded != null)
                    {
                        foreach (var kv in loaded)
                            if (kv.Value != null) todoStore[kv.Key] = kv.Value;
                    }
                }
            }
            catch (Exception) { }
        }

        static void SaveTodosLocked()
        {
            try
            {
                Directory.CreateDirectory(AssistantHarness.StorageDir);
                WriteAtomic(TodoFile, Js.Serialize(todoStore));
            }
            catch (Exception) { }
        }

        // =====================================================================
        // Smart Context Compaction (ProtectLastN = 6 + Daily Memory Flush)
        // =====================================================================

        public const int ProtectLastN = 6;

        public static bool ShouldCompact(AgentChat chat, long usedTokens, long windowTokens)
        {
            if (chat == null || chat.Messages == null || chat.Messages.Count <= ProtectLastN + 2) return false;
            if (windowTokens > 0 && usedTokens > 0 && (double)usedTokens / windowTokens >= 0.75) return true;
            long totalChars = 0;
            for (int i = 0; i < chat.Messages.Count; i++)
                totalChars += (chat.Messages[i].Text ?? "").Length;
            return totalChars > 180000;
        }

        public static Dictionary<string, object> CompactChat(AgentChat chat, int protectLastN = ProtectLastN)
        {
            if (chat == null) throw new ArgumentNullException("chat");
            if (protectLastN < 2) protectLastN = 2;
            var msgs = chat.Messages ?? new List<AgentMsg>();

            // Identify indices of conversational turns (user/agent) so we protect the last N turns cleanly.
            var turnIndices = new List<int>();
            for (int i = 0; i < msgs.Count; i++)
            {
                string r = (msgs[i].Role ?? "").ToLowerInvariant();
                if (r == "user" || r == "agent") turnIndices.Add(i);
            }

            if (turnIndices.Count <= protectLastN + 1)
            {
                return new Dictionary<string, object>
                {
                    { "compacted", false },
                    { "reason", "Chat has " + turnIndices.Count + " conversational turns (<= " + (protectLastN + 1) + "); nothing to compact." },
                    { "kept_messages", msgs.Count }
                };
            }

            int keepFrom = turnIndices[turnIndices.Count - protectLastN];
            // Boundary safety: never split a tool call sequence away from the agent turn it belongs to.
            while (keepFrom > 0 && string.Equals(msgs[keepFrom - 1].Role, "tool", StringComparison.OrdinalIgnoreCase))
                keepFrom--;

            if (keepFrom <= 1)
            {
                return new Dictionary<string, object>
                {
                    { "compacted", false },
                    { "reason", "Not enough earlier messages before protected tail." },
                    { "kept_messages", msgs.Count }
                };
            }

            var toCompress = msgs.Take(keepFrom).ToList();
            var tail = msgs.Skip(keepFrom).ToList();

            var summarySb = new StringBuilder();
            summarySb.AppendLine("[Context Checkpoint — " + toCompress.Count + " earlier messages compacted]");
            int bullets = 0;
            for (int i = 0; i < toCompress.Count && summarySb.Length < 2400; i++)
            {
                var m = toCompress[i];
                string r = (m.Role ?? "").ToLowerInvariant();
                if (r != "user" && r != "agent") continue;
                string shown = ChatView.ShownText(m.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                if (shown.Length == 0) continue;
                if (shown.Length > 220) shown = shown.Substring(0, 217) + "...";
                string who = r == "user" ? "User" : (string.IsNullOrEmpty(m.Label) ? AgentStore.NameOf(m.Agent) : m.Label);
                summarySb.Append("- ").Append(who).Append(": ").AppendLine(shown);
                bullets++;
            }
            string summary = summarySb.ToString().TrimEnd();

            // Flush durable summary note into today's daily log BEFORE discarding middle messages.
            string dailyNote = "Compacted chat " + chat.Id + (string.IsNullOrEmpty(chat.Title) ? "" : " (" + chat.Title + ")")
                + " — " + toCompress.Count + " messages (" + bullets + " key turns).";
            MemoryManage("daily", "daily_log", dailyNote);

            long stepId = 0;
            try
            {
                string payload = Js.Serialize(new Dictionary<string, object>
                {
                    { "removed_messages", toCompress.Count },
                    { "kept_messages", tail.Count + 1 },
                    { "summary", summary }
                });
                stepId = SqliteVault.RecordStep(chat.Id, turnIndices.Count, 0, "checkpoint", "chat_compact", payload, Store.Now());
            }
            catch (Exception) { }

            chat.Messages.Clear();
            chat.Messages.Add(new AgentMsg
            {
                Role = "agent",
                Agent = "checkpoint",
                Label = "Context Checkpoint",
                Text = summary,
                Time = Store.Now()
            });
            chat.Messages.AddRange(tail);
            chat.Sessions.Clear();
            chat.Seen.Clear();
            chat.BranchPrefix = 1;
            chat.Updated = Store.Now();
            if (chat.Persisted) AgentStore.Save(chat);
            ResetSnapshot(chat.Id);

            AssistantHarness.Report("Compacted " + toCompress.Count + " messages -> daily log + checkpoint");
            return new Dictionary<string, object>
            {
                { "compacted", true },
                { "chat_id", chat.Id },
                { "removed_messages", toCompress.Count },
                { "kept_messages", chat.Messages.Count },
                { "daily_flushed", true },
                { "checkpoint_step_id", stepId },
                { "summary", summary }
            };
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        public static string SanitizeSlug(string slug)
        {
            string s = (slug ?? "").Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"[^a-z0-9_-]+", "-").Trim('-', '_');
            if (s.Length > 64) s = s.Substring(0, 64).TrimEnd('-', '_');
            return s;
        }

        public static string SanitizeToolName(string name)
        {
            string s = (name ?? "").Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"[^a-z0-9_]+", "_").Trim('_');
            if (s.Length > 48) s = s.Substring(0, 48).TrimEnd('_');
            if (s.Length > 0 && char.IsDigit(s[0])) s = "t_" + s;
            return s;
        }

        static string ReadTextSafe(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; }
            catch (Exception) { return ""; }
        }

        static void WriteAtomic(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 6);
            File.WriteAllText(tmp, text ?? "", Utf8NoBom);
            if (File.Exists(path)) File.Replace(tmp, path, null, true);
            else File.Move(tmp, path);
        }

        static string DictStr(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture).Trim() : "";
        }
    }
}
