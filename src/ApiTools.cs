using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace StickyPro
{
    // Tools the app offers to AI agents (MCP) and scripts (App API). Each returns JSON text.
    static class ApiTools
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        public static readonly string[] Pages = { "dashboard", "tasks", "notes", "prompts", "clips", "agents", "terminals", "explorer", "markdown", "browser", "documents", "accounts", "snip", "chrome", "power", "system", "cards", "activity", "tagged", "shortcuts" };

        // ---------- Schema ----------
        static List<Dictionary<string, object>> builtInSchema;
        static HashSet<string> builtInNames;
        public static HashSet<string> Names { get { return new HashSet<string>(Schema().Select(t => (string)t["name"])); } }

        static List<Dictionary<string, object>> BuiltInSchema()
        {
            if (builtInSchema != null) return builtInSchema;
            var l = new List<Dictionary<string, object>>();
            l.Add(T("app_status", "App status", "Data folder, counts of notes, prompts, clips, open tasks and agent chats. Start here.", true, new string[0]));
            l.Add(T("search_all", "Search everything", "Search notes, tasks, prompts, clipboard history and agent chats at once. Every word must match.", true,
                new[] { "query|string|Words to find", "limit|integer|Hits per kind (default 8)" }, "query"));
            l.Add(T("open_page", "Open a page", "Show the app window on a page: " + string.Join(", ", Pages) + ".", false, new[] { "page|string|Page key" }, "page"));
            l.Add(T("notify", "Notify the user", "Show a Windows notification from the app.", false, new[] { "text|string|Message", "title|string|Title (default All in One)" }, "text"));

            l.Add(T("notes_search", "Search sticky notes", "List notes, newest first. Filter by words and tag.", true,
                new[] { "query|string|Words that must all appear", "tag|string|Only notes with this tag", "include_archived|boolean|Also archived notes", "limit|integer|Max results (default 30)" }));
            l.Add(T("note_get", "Read a note", "Full text and details of one note.", true, new[] { "id|string|Note id" }, "id"));
            l.Add(T("note_create", "Create a note", "New sticky note. Colors: " + string.Join(", ", Ui.Colors.Select(c => c.Name)) + ".", false,
                new[] { "text|string|Note text", "tags|string|Comma separated tags", "color|string|Color name", "open|boolean|Show it on the desktop" }, "text"));
            l.Add(T("note_update", "Update a note", "Change text, append text, tags, color, or archive / restore a note.", false,
                new[] { "id|string|Note id", "text|string|New full text", "append|string|Text to add at the end", "tags|string|Comma separated tags", "color|string|Color name", "archived|boolean|Archive (true) or restore (false)" }, "id"));

            l.Add(T("tasks_search", "Search tasks", "Tasks and reminders. Open tasks by default, soonest due first.", true,
                new[] { "query|string|Words that must all appear", "list|string|Only this list", "include_done|boolean|Also finished tasks", "due_before|string|Only due before this date (ISO)", "limit|integer|Max results (default 50)" }));
            l.Add(T("task_lists", "Task lists", "Names of the task lists.", true, new string[0]));
            l.Add(T("task_create", "Create a task", "New task with optional due date (ISO date or date-time), priority (low, normal, high), repeat (none, daily, weekdays, weekly, monthly, yearly) and steps.", false,
                new[] { "title|string|Task title", "notes|string|Details", "list|string|List name (default Inbox)", "priority|string|low, normal or high", "due|string|ISO date or date-time", "remind_at|string|ISO date-time for a reminder", "repeat|string|Repeat rule", "steps|array|Sub-steps" }, "title"));
            l.Add(T("task_update", "Update a task", "Change fields, finish / reopen, or add steps. Use \"\" to clear due.", false,
                new[] { "id|string|Task id", "title|string|Title", "notes|string|Details", "list|string|List", "priority|string|low, normal or high", "due|string|ISO date or date-time (\"\" clears)", "done|boolean|Finish (true) or reopen (false)", "add_steps|array|Steps to add" }, "id"));

            l.Add(T("prompts_search", "Search prompts", "The user's prompt library (title, category, tags). Most used first.", true,
                new[] { "query|string|Words that must all appear", "category|string|Only this category", "favorites_only|boolean|Only favorites", "limit|integer|Max results (default 30)" }));
            l.Add(T("prompt_get", "Read a prompt", "Full prompt text with its {{blanks}}.", true, new[] { "id|string|Prompt id" }, "id"));
            l.Add(T("prompt_save", "Create or update a prompt", "Without id creates a prompt; with id updates it (the old text is kept as a version).", false,
                new[] { "id|string|Prompt id to update", "title|string|Title", "body|string|Prompt text, {{name}} marks a blank", "category|string|Category", "tags|string|Comma separated tags", "favorite|boolean|Favorite" }));
            l.Add(T("prompt_fill", "Fill a prompt", "Returns the prompt text with the {{blanks}} filled and counts one use.", false,
                new[] { "id|string|Prompt id", "values|object|Blank name -> value" }, "id"));

            l.Add(T("clips_search", "Search clipboard history", "What the user copied, newest first.", true,
                new[] { "query|string|Words that must all appear", "kind|string|text, image or files", "pinned_only|boolean|Only pinned", "folder|string|Only this folder", "limit|integer|Max results (default 20)" }));
            l.Add(T("clip_get", "Read a clip", "Full text of one clipboard item.", true, new[] { "id|string|Clip id" }, "id"));
            l.Add(T("clip_organize", "Organize a clip", "Pin, file into a folder or color-label a clipboard item.", false,
                new[] { "id|string|Clip id", "pinned|boolean|Pin", "folder|string|Folder name (\"\" = none)", "label|string|Color label (\"\" = none)" }, "id"));
            l.Add(T("clipboard_copy", "Copy to the clipboard", "Put text on the Windows clipboard.", false, new[] { "text|string|Text to copy" }, "text"));

            l.Add(T("favorites_list", "Favorites and recent items", "Favorite and recent items of a scope: folders (project folders used by Terminals, Explorer, Agents) or agents.", true,
                new[] { "scope|string|folders or agents", "limit|integer|Max recent (default 20)" }, "scope"));
            l.Add(T("folder_sets", "Folder sets", "Saved groups of folders the user opens together (in Explorer, Terminals and Agents).", true, new string[0]));
            l.Add(T("folder_set_save", "Save a folder set", "Create or replace a folder set (a named group of folders opened together).", false,
                new[] { "name|string|Set name", "folders|string|Folder paths, one per line or separated by ;", "favorite|boolean|Favorite" }, "name", "folders"));
            l.Add(T("folder_sets_list", "Folder sets", "Named groups of folders the user opens together (shared by Explorer, Terminals and Agents).", true,
                new string[0]));
            l.Add(T("agent_limits", "Agent usage limits", "5-hour and weekly usage limits of the agent CLIs (Claude Code, Codex; others report none), with reset times.", true,
                new string[0]));
            l.Add(T("item_organize", "Organize an item", "Favorite, color, tag, note or rename any item of a scope (for folders the id is the full path).", false,
                new[] { "scope|string|folders or agents", "id|string|Item id or folder path", "favorite|boolean|Favorite", "color|string|" + string.Join(", ", MetaStore.Colors) + " or \"\"", "tags|string|Comma separated", "note|string|Note", "name|string|Display name (\"\" = default)" }, "scope", "id"));

            l.Add(T("agents_list", "Coding agents", "Agent CLIs the app can run (Claude Code, Codex, Cline, Antigravity, OpenCode) and whether they are installed.", true, new string[0]));
            l.Add(T("agent_chats", "Agent chats", "Saved multi-agent chats, newest first.", true, new[] { "query|string|Words in the title or folder", "limit|integer|Max results (default 20)" }));
            l.Add(T("agent_chat_get", "Read an agent chat", "The last messages of a chat.", true, new[] { "id|string|Chat id", "last|integer|How many messages (default 20)" }, "id"));
            l.Add(T("agent_ask", "Ask coding agents", "Send a message to one or more coding agents in a folder and wait for their answers. Agents answer in turn and see each other's replies. Continue a chat with chat_id. mode: ask (read only, default), edit (may edit files), auto (no approvals).", false,
                new[] { "prompt|string|Message", "agents|array|Agent ids, e.g. [\"claude\", \"codex\"]", "folder|string|Working folder (new chats)", "chat_id|string|Continue this chat", "mode|string|ask, edit or auto", "timeout_seconds|integer|Max wait per agent (default 900)" }, "prompt"));
            // ---- control of the app's pages (the Assistant uses these to work for you) ----
            l.Add(T("page_context", "What the user sees", "The open page and its context: the Markdown note, the Explorer folder, the Browser tab (title, address).", true, new string[0]));
            l.Add(T("markdown_open", "Open a Markdown note", "Show a .md file on the Markdown notes page.", false, new[] { "path|string|Full path of the .md file" }, "path"));
            l.Add(T("explorer_open", "Open a folder in Explorer page", "Show a folder in the app's Explorer page.", false, new[] { "path|string|Folder path" }, "path"));
            l.Add(T("terminal_open", "Open a terminal", "Open a PowerShell terminal in a folder, optionally running a command.", false, new[] { "folder|string|Folder", "command|string|Command to run" }, "folder"));
            l.Add(T("document_open", "Open a document in the app", "Open an Excel, PowerPoint, Word, Visio or PDF file on the Documents page (optionally choosing edit or preview mode); emails (.eml, .msg, .mysingle) open on the Browser page.", false,
                new[] { "path|string|Full file path", "edit|boolean|true = edit in Office, false = read-only preview, omit = page default" }, "path"));
            l.Add(T("documents_list", "List open documents", "List open document tabs on the Documents page, active document, edit/preview mode and kind.", true, new string[0]));
            l.Add(T("document_switch", "Switch document tab", "Switch to an already-open document tab (by path or filename) or open the file on the Documents page.", false,
                new[] { "path|string|Full file path or open tab filename" }, "path"));
            l.Add(T("document_save", "Save a document", "Save the active or named edited Office document on the Documents page.", false,
                new[] { "path|string|Optional full file path or tab filename (defaults to active document)" }));
            // ---- live Office COM control & background bulk extraction (NASCA DRM-safe) ----
            l.Add(T("excel_info", "Excel workbook info", "Inspect a live Excel workbook (active sheet, selection, all sheet names and dimensions). Uses active Excel tab or opens path in Edit mode.", true,
                new[] { "path|string|Optional full file path or open tab filename" }));
            l.Add(T("excel_read", "Read Excel cells", "Read cells from a live Excel worksheet (range or UsedRange) in one bulk COM call.", true,
                new[] { "path|string|Optional full file path or open tab filename", "sheet|string|Optional sheet name (defaults to active sheet)", "range|string|Optional A1 range e.g. A1:F30 (defaults to UsedRange)", "max_rows|integer|Max rows to read (default 200)", "max_cols|integer|Max columns to read (default 40)" }));
            l.Add(T("excel_write", "Write Excel cells", "Write a 2D array of values or formulas starting at start_cell in one bulk COM call.", false,
                new[] { "path|string|Optional full file path or open tab filename", "sheet|string|Optional sheet name (defaults to active sheet)", "start_cell|string|Top-left cell e.g. A1 (default A1)", "values|matrix|2D array of rows (or JSON string of 2D array)", "save|boolean|Save workbook after writing (default false)" }, "values"));
            l.Add(T("excel_formula", "Set Excel formula", "Set a formula on a live Excel cell and return its calculated value and formatted text.", false,
                new[] { "path|string|Optional full file path or open tab filename", "sheet|string|Optional sheet name (defaults to active sheet)", "cell|string|Target cell address e.g. D10", "formula|string|Excel formula e.g. =SUM(D2:D9)", "save|boolean|Save workbook after setting formula (default false)" }, "cell", "formula"));
            l.Add(T("excel_sheet_add", "Add Excel worksheet", "Add a new worksheet to a live Excel workbook and optionally activate it.", false,
                new[] { "path|string|Optional full file path or open tab filename", "name|string|Worksheet name", "activate|boolean|Activate the new sheet (default true)" }, "name"));
            l.Add(T("excel_format", "Format Excel range", "Apply bold, background/font hex colors (#RRGGBB), number format, or AutoFit to a live Excel range.", false,
                new[] { "path|string|Optional full file path or open tab filename", "sheet|string|Optional sheet name (defaults to active sheet)", "range|string|Range address e.g. A1:F1", "bold|boolean|Set bold font", "bg_color|string|Background hex color e.g. #EAF1FF or none", "font_color|string|Font hex color e.g. #1E293B", "number_format|string|Excel number format e.g. #,##0.00 or 0.0%", "auto_fit|boolean|Auto-fit column widths (default false)" }, "range"));
            l.Add(T("word_read", "Read Word document", "Read body text and all tables from a live Word document.", true,
                new[] { "path|string|Optional full file path or open tab filename", "max_chars|integer|Max characters of body text (default 30000)" }));
            l.Add(T("word_write", "Edit Word document", "Append text, find/replace text, or insert a formatted table in a live Word document.", false,
                new[] { "path|string|Optional full file path or open tab filename", "mode|string|append, replace, or insert_table", "text|string|Text to append or replacement text", "find|string|Text to find (for mode=replace)", "rows|str_matrix|2D array of table rows (for mode=insert_table)", "save|boolean|Save document after editing (default false)" }, "mode"));
            l.Add(T("ppt_read", "Read PowerPoint presentation", "Read all slides (titles, texts, tables, speaker notes) from a live PowerPoint presentation.", true,
                new[] { "path|string|Optional full file path or open tab filename" }));
            l.Add(T("ppt_write", "Edit PowerPoint presentation", "Edit a slide title/body or add a new slide in a live PowerPoint presentation.", false,
                new[] { "path|string|Optional full file path or open tab filename", "slide|integer|1-based slide index (default 1, or appended when add_slide=true)", "title|string|Slide title text", "body|string|Slide body text", "add_slide|boolean|Add a new slide instead of editing an existing one (default false)", "save|boolean|Save presentation after editing (default false)" }));
            l.Add(T("office_extract", "Extract Office or PDF file", "Read-only background extraction of Excel, CSV, Word, PowerPoint, or PDF files (works on NASCA DRM files) off the UI thread.", true,
                new[] { "path|string|Full file path", "max_rows|integer|Max rows per sheet/table (default 25000)", "preview_rows|integer|Preview rows per table in output (default 50)" }, "path"));
            l.Add(T("browser_open", "Open in the app browser", "Open a web address or a local file (PDF, HTML, email) in a new tab of the app's Browser page.", false, new[] { "target|string|URL or file path" }, "target"));
            l.Add(T("browser_read", "Read the browser tab", "Title, address and visible text of the active tab of the app's Browser page.", true, new[] { "max_chars|integer|Limit (default 20000)" }));
            l.Add(T("browser_run_js", "Run JavaScript in the browser tab", "Run a script in the active tab (click, fill, extract). Returns the result as JSON.", false, new[] { "script|string|JavaScript expression" }, "script"));

            // ---- Native Windows SQLite Vault (data\vault.db, WAL, per-sheet SQL JOINs, FTS5) ----
            l.Add(T("vault_list", "List indexed vault documents", "List Excel/CSV/Word/PowerPoint/PDF files indexed in data\\vault.db with their dynamic SQL sheet table names.", true,
                new[] { "query|string|Optional filter text", "limit|integer|Max documents (default 50)" }));
            l.Add(T("vault_extract", "Extract and index file into SQLite Vault", "Extract an Office/CSV/PDF file (NASCA DRM-safe via COM) off the UI thread and persist its sheets as real SQL tables in data\\vault.db. Uses smart cache when file size and mtime are unchanged unless force=true.", false,
                new[] { "path|string|Full file path", "force|boolean|Re-extract even if cached unchanged (default false)", "max_rows|integer|Max rows per sheet (default 25000)" }, "path"));
            l.Add(T("vault_schema", "Inspect SQLite Vault table schemas", "List dynamic SQL sheet tables (data_<docId>_<sheet>) and their column names for writing SQL queries and cross-report JOINs.", true,
                new[] { "doc_id|integer|Optional document id (omit or 0 for all tables)" }));
            l.Add(T("vault_query", "Run SQL query on SQLite Vault", "Execute arbitrary SQL (including cross-report JOIN, GROUP BY, SUM, AVG) against data\\vault.db off the UI thread.", true,
                new[] { "sql|string|SQL query to execute", "max_rows|integer|Max rows returned (default 500)" }, "sql"));
            l.Add(T("vault_search", "Full-text search SQLite Vault", "Full-text search across all indexed document sheets, sections and text in data\\vault.db.", true,
                new[] { "query|string|Search query", "limit|integer|Max results (default 20)" }, "query"));

            // ---- Hermes / OpenClaw / DeepSeek Memory, Skills, Dynamic Tools, Todo & Compaction ----
            l.Add(T("memory_manage", "Read or update companion memory", "Read, add, replace, remove, daily_log, list, or reset_snapshot on SOUL.md, USER.md, TOOLS.md, MEMORY.md, GOALS.md, HEARTBEAT.md, or daily/YYYY-MM-DD.md.", false,
                new[] { "file|string|SOUL.md, USER.md, TOOLS.md, MEMORY.md, GOALS.md, HEARTBEAT.md, daily, or all", "action|string|read, add, replace, remove, daily_log, list, or reset_snapshot", "content|string|Text to add or replace with", "old_text|string|Existing text to match for replace/remove" }, "action"));
            l.Add(T("session_search", "Search past chat sessions", "Full-text search across Assistant and Agents chat transcripts in vault.db with temporal decay and MMR deduplication.", true,
                new[] { "query|string|Search query", "scope|string|assistant, agents, or empty for both", "limit|integer|Max results (default 15)" }, "query"));
            l.Add(T("skill_manage", "Manage procedural skills (SKILL.md)", "List, read, save, patch, or delete self-improving SKILL.md playbooks (Progressive Disclosure).", false,
                new[] { "action|string|list, read, save, patch, or delete", "slug|string|Skill slug (e.g. monthly-factory-report)", "content|string|Full SKILL.md content (save) or replacement text (patch)", "old_text|string|Existing snippet to replace (patch)" }, "action"));
            l.Add(T("tool_list", "List dynamic tools", "List all hot-loaded custom tools registered under data\\assistant\\tools\\*.json.", true, new string[0]));
            l.Add(T("tool_create", "Create a dynamic MCP tool", "Creator Mode: register a reusable sql, powershell, cmd, or python tool under data\\assistant\\tools\\*.json. Hot-loads immediately into MCP without rebuilding.", false,
                new[] { "name|string|Unique tool name (lowercase letters, digits, underscores)", "title|string|Human-readable title", "description|string|What the tool does", "kind|string|sql, powershell, cmd, or python", "script|string|SQL template (supports {{arg}}) or script body (receives JSON args in AIO_TOOL_ARGS)", "args_schema|any_object|Optional parameter schema object or JSON string", "read_only|boolean|Whether the tool is read-only (default true for sql)" }, "name", "kind", "script"));
            l.Add(T("tool_delete", "Delete a dynamic MCP tool", "Remove a dynamic tool from data\\assistant\\tools\\.", false,
                new[] { "name|string|Dynamic tool name" }, "name"));
            l.Add(T("todo_plan", "Manage per-chat step plan", "Set, update, get, or clear the active step checklist for a chat (reports live progress in the status strip).", false,
                new[] { "action|string|set, update, get, or clear", "chat_id|string|Chat id (default 'default')", "steps|any_array|Array of step strings or {text,status} objects (for set)", "step|integer|1-based step index (for update)", "status|string|pending, in_progress, done, or blocked", "text|string|Optional updated step text" }, "action"));
            l.Add(T("chat_steps", "Inspect or fork chat step events", "Read event-sourced step_events for a chat or fork a chat's step timeline at step_id.", false,
                new[] { "chat_id|string|Chat id", "action|string|list (default) or fork", "step_id|integer|Step id to fork at (for action=fork)", "new_chat_id|string|Optional new chat id for fork", "limit|integer|Max events (default 200)" }, "chat_id"));
            l.Add(T("chat_compact", "Compact chat context safely", "Flush a durable summary to today's daily log, protect the last N conversational turns without splitting tool pairs, and insert a clean checkpoint.", false,
                new[] { "chat_id|string|Chat id to compact", "protect_last_n|integer|Number of recent turns to keep verbatim (default 6)" }, "chat_id"));

            // ---- Proactive Cron, Idle-Only Heartbeat & Folder Watchers ----
            l.Add(T("cron_manage", "Manage proactive cron jobs and follow-ups", "List, create, follow_up (one-shot self-schedule), update, delete, or run_now on scheduled routines with 0-token pre-checks and [SILENT] suppression.", false,
                new[] { "action|string|list, create, follow_up, update, delete, or run_now", "id|string|Job id (for update, delete, run_now)", "name|string|Job name", "schedule|string|every:15m, daily:08:00, or once:YYYY-MM-DDTHH:mm", "prompt|string|Task prompt when triggered", "skill|string|Optional skill slug", "precheck_type|string|none, folder_changed, sql_nonzero, or overdue_tasks", "precheck_arg|string|Folder path or SQL query for 0-token pre-check", "enabled|boolean|Enable or disable job", "one_shot|boolean|Disable automatically after firing once", "in_minutes|integer|Relative minutes for action=follow_up (default 15)" }, "action"));
            l.Add(T("heartbeat_status", "Check or run idle-only Heartbeat", "Inspect HEARTBEAT.md/GOALS.md status or run an immediate 0-token local heartbeat check (returns HEARTBEAT_OK when clean).", true,
                new[] { "run_now|boolean|Run the 0-token local heartbeat check immediately (default false)", "force|boolean|Run even if an Assistant chat is marked busy (default false)" }));
            l.Add(T("watch_folder", "Watch a folder for automatic vault indexing", "Add, remove, or list FileSystemWatcher folders whose new/modified Office and PDF files auto-index into vault.db after a 1.5 s debounce.", false,
                new[] { "action|string|add (default), remove, or list", "path|string|Folder path (required for add/remove)" }));

            // ---- Visual-First Live Communication Canvas & Interactive Dashboards ----
            l.Add(T("dashboard_publish", "Publish interactive HTML dashboard", "Write an offline studio-grade HTML dashboard to data\\dashboards\\<slug>.html and open/refresh it in the embedded Browser (reuses existing tab).", false,
                new[] { "slug|string|Dashboard slug (e.g. q4-sales-comparison)", "title|string|Dashboard title", "html|string|Full HTML5 document or studio HTML fragment (follow skills/anti-slop-web-design/SKILL.md)", "open|boolean|Open/reload in Browser automatically (default true)" }, "slug", "html"));
            l.Add(T("canvas_update", "Update the Live Communication Canvas", "Overwrite data\\dashboards\\live-canvas.html with an interactive SVG diagram, architecture flow, comparison, or telemetry view and auto-flip/reload the Browser tab without duplicating tabs.", false,
                new[] { "html|string|Full HTML5 document or visual fragment (follow skills/anti-slop-web-design/SKILL.md)", "title|string|Canvas title (default Live Canvas)", "open|boolean|Open/reload in Browser automatically (default true)" }, "html"));
            l.Add(T("dashboard_list", "List published dashboards", "List saved HTML dashboards and Live Canvas files in data\\dashboards\\.", true, new string[0]));

            builtInSchema = l;
            builtInNames = new HashSet<string>(l.Select(t => (string)t["name"]), StringComparer.OrdinalIgnoreCase);
            return l;
        }

        public static List<Dictionary<string, object>> Schema()
        {
            var baseList = BuiltInSchema();
            var combined = new List<Dictionary<string, object>>(baseList);
            try
            {
                var dyn = AgentMemory.BuildDynamicToolSchemas(builtInNames);
                if (dyn != null && dyn.Count > 0) combined.AddRange(dyn);
            }
            catch (Exception) { }
            return combined;
        }

        static Dictionary<string, object> T(string name, string title, string desc, bool readOnly, string[] props, params string[] required)
        {
            var p = new Dictionary<string, object>();
            foreach (var spec in props)
            {
                var parts = spec.Split('|');
                var prop = new Dictionary<string, object> { { "type", parts[1] }, { "description", parts[2] } };
                if (parts[1] == "array") prop["items"] = new Dictionary<string, object> { { "type", "string" } };
                else if (parts[1] == "any_array") prop["type"] = "array";
                else if (parts[1] == "matrix") { prop["type"] = "array"; prop["items"] = new Dictionary<string, object> { { "type", "array" } }; }
                else if (parts[1] == "str_matrix") { prop["type"] = "array"; prop["items"] = new Dictionary<string, object> { { "type", "array" }, { "items", new Dictionary<string, object> { { "type", "string" } } } }; }
                else if (parts[1] == "object") prop["additionalProperties"] = new Dictionary<string, object> { { "type", "string" } };
                else if (parts[1] == "any_object") prop["type"] = "object";
                p[parts[0]] = prop;
            }
            var input = new Dictionary<string, object> { { "type", "object" }, { "properties", p } };
            if (required.Length > 0) input["required"] = required;
            return new Dictionary<string, object>
            {
                { "name", name }, { "title", title }, { "description", desc }, { "inputSchema", input },
                { "annotations", new Dictionary<string, object> { { "title", title }, { "readOnlyHint", readOnly }, { "destructiveHint", false }, { "openWorldHint", name == "agent_ask" } } }
            };
        }

        // ---------- Run ----------
        public static string Run(string tool, Dictionary<string, object> a)
        {
            switch (tool)
            {
                case "agent_ask": return AgentAsk(a);
                case "office_extract": return OfficeExtract(a);
                case "browser_read": case "browser_run_js": return BrowserScript(tool, a);
                // Phase 3: Off-UI-thread Vault, Memory, Skills, Dynamic Tools, Cron, Heartbeat, Live Canvas
                case "vault_list": return J(SqliteVault.ListDocs(S(a, "query"), I(a, "limit", 50)));
                case "vault_extract": return VaultExtract(a);
                case "vault_schema": return J(SqliteVault.GetSchema(L64(a, "doc_id", 0)));
                case "vault_query": return J(SqliteVault.QuerySql(S(a, "sql"), I(a, "max_rows", 500)));
                case "vault_search": return J(SqliteVault.SearchVault(S(a, "query"), I(a, "limit", 20)));
                case "memory_manage": return J(AgentMemory.MemoryManage(S(a, "file"), S(a, "action"), S(a, "content"), S(a, "old_text")));
                case "session_search": return J(SqliteVault.SearchSessions(S(a, "query"), S(a, "scope"), I(a, "limit", 15)));
                case "skill_manage": return J(AgentMemory.SkillManage(S(a, "action"), S(a, "slug"), S(a, "content"), S(a, "old_text")));
                case "tool_list": return J(AgentMemory.ToolList());
                case "tool_create": return ToolCreate(a);
                case "tool_delete": return J(AgentMemory.ToolDelete(S(a, "name")));
                case "todo_plan": return TodoPlanTool(a);
                case "chat_steps": return ChatStepsTool(a);
                case "chat_compact": return ChatCompactTool(a);
                case "cron_manage": return CronManageTool(a);
                case "heartbeat_status": return J(CronEngine.HeartbeatStatus(B(a, "run_now") == true, B(a, "force") == true));
                case "watch_folder": return WatchFolderTool(a);
                case "dashboard_publish": return J(DashboardGen.Publish(S(a, "slug"), S(a, "title"), S(a, "html"), B(a, "open") != false));
                case "canvas_update": return J(DashboardGen.UpdateCanvas(S(a, "html"), Has(a, "title") ? S(a, "title") : "Live Canvas", B(a, "open") != false));
                case "dashboard_list": return J(DashboardGen.ListDashboards());
                case "agent_chats": case "agent_chat_get": case "search_all": EnsureChats(); break;
            }
            if (AgentMemory.HasDynamicTool(tool))
                return J(AgentMemory.RunDynamicTool(tool, a));
            return UI(() => RunUi(tool, a));
        }

        // Browser scripts finish asynchronously on the UI thread: start them there, wait here (the pipe thread).
        static string BrowserScript(string tool, Dictionary<string, object> a)
        {
            var done = new ManualResetEvent(false);
            string result = null, error = null;
            App.I.Sync.BeginInvoke(new Action(async () =>
            {
                try
                {
                    string js = tool == "browser_read"
                        ? "JSON.stringify({title:document.title,url:location.href,text:(document.body?document.body.innerText:'').slice(0," + (Has(a, "max_chars") ? Convert.ToInt32(a["max_chars"]) : 20000) + ")})"
                        : S(a, "script");
                    result = await App.I.BrowserScript(js);
                }
                catch (Exception ex) { error = ex.Message; }
                done.Set();
            }));
            if (!done.WaitOne(30000)) throw new Exception("The browser did not answer in 30 s.");
            if (error != null) throw new Exception(error);
            return result ?? "null";
        }

        static string UI(Func<string> f)
        {
            var sync = App.I.Sync;
            if (!sync.InvokeRequired) return f();
            return (string)sync.Invoke(f);
        }

        static string RunUi(string tool, Dictionary<string, object> a)
        {
            switch (tool)
            {
                case "app_status":
                    return J(new Dictionary<string, object>
                    {
                        { "app", Ui.AppTitle }, { "data_folder", Store.Dir }, { "notes", Store.Notes.Count(n => !n.Archived) }, { "archived_notes", Store.Notes.Count(n => n.Archived) },
                        { "open_tasks", Store.Tasks.Count(t => !t.Done) }, { "prompts", Store.Prompts.Count }, { "clips", Store.Clips.Count },
                        { "agent_chats", AgentStore.Loaded ? AgentStore.Chats.Count : ChatFileCount() }, { "favorite_folders", MetaStore.Favorites("folders").Count }, { "now", Iso(DateTime.Now.Ticks) }
                    });
                case "search_all": return SearchAll(a);
                case "open_page":
                {
                    string page = S(a, "page").ToLowerInvariant();
                    if (!Pages.Contains(page)) throw new Exception("Unknown page. Use one of: " + string.Join(", ", Pages));
                    App.I.ShowMain(page);
                    AssistantHarness.Report("Switched to " + Ui.PageName(page, char.ToUpper(page[0]) + page.Substring(1)));
                    return J(new { opened = page });
                }
                case "page_context": return J(App.I.PageContext());
                case "markdown_open":
                {
                    string p = S(a, "path"); if (!File.Exists(p)) throw new Exception("No such file: " + p);
                    App.I.OpenMarkdown(p);
                    AssistantHarness.Report("Opened " + Path.GetFileName(p) + " in Markdown");
                    return J(new { opened = p });
                }
                case "explorer_open":
                {
                    string p = S(a, "path"); if (!Directory.Exists(p)) throw new Exception("No such folder: " + p);
                    App.I.OpenInExplorerPage(p);
                    string label = Path.GetFileName(p.TrimEnd('\\', '/'));
                    AssistantHarness.Report("Opened " + (label.Length > 0 ? label : p) + " in Explorer");
                    return J(new { opened = p });
                }
                case "terminal_open":
                {
                    string p = S(a, "folder"); if (!Directory.Exists(p)) throw new Exception("No such folder: " + p);
                    Term.Open(p, Has(a, "command") ? S(a, "command") : "");
                    string label = Path.GetFileName(p.TrimEnd('\\', '/'));
                    AssistantHarness.Report("Opened terminal in " + (label.Length > 0 ? label : p));
                    return J(new { opened = p });
                }
                case "document_open":
                {
                    string p = S(a, "path"); if (!System.IO.File.Exists(p)) throw new Exception("File not found: " + p);
                    bool? edit = B(a, "edit");
                    App.I.OpenDocument(p, edit);
                    AssistantHarness.Report("Opened " + Path.GetFileName(p) + " in Documents");
                    var dv = App.I.DocumentsView;
                    return J(new { opened = p, editing = dv != null && dv.IsEditing(p), kind = dv != null ? dv.ActiveKind : (OfficeEdit.KindOf(p) ?? Path.GetExtension(p).TrimStart('.').ToLowerInvariant()) });
                }
                case "documents_list":
                {
                    var dv = App.I.DocumentsView;
                    string cur = dv != null ? (dv.CurrentPath ?? "") : "";
                    var openTabs = dv != null ? dv.OpenTabs : new List<string>();
                    return J(new
                    {
                        current = cur,
                        mode = cur.Length > 0 && dv != null ? (dv.IsEditing(cur) ? "edit" : "preview") : (Store.Settings.ViewerEdit ? "edit" : "preview"),
                        kind = dv != null ? (dv.ActiveKind ?? "") : "",
                        tabs = openTabs.Select(p => new
                        {
                            path = p,
                            editing = dv != null && dv.IsEditing(p),
                            kind = OfficeEdit.KindOf(p) ?? Path.GetExtension(p ?? "").TrimStart('.').ToLowerInvariant()
                        }).ToList()
                    });
                }
                case "document_switch":
                {
                    string p = S(a, "path");
                    if (string.IsNullOrWhiteSpace(p)) throw new Exception("path is required.");
                    App.I.ShowMain("documents");
                    var dv = App.I.DocumentsView;
                    if (dv == null || !dv.SwitchTo(p)) throw new Exception("Could not switch to document: " + p);
                    string cur = dv.CurrentPath ?? p;
                    AssistantHarness.Report("Switched to " + Path.GetFileName(cur) + " in Documents");
                    return J(new { switched = cur, editing = dv.IsEditing(cur), kind = dv.ActiveKind ?? "" });
                }
                case "document_save":
                {
                    string p = S(a, "path");
                    var dv = App.I.DocumentsView;
                    if (dv == null) return J(new { saved = false, path = p, error = "Documents page is not open." });
                    string target = string.IsNullOrWhiteSpace(p) ? (dv.CurrentPath ?? "") : p;
                    string err;
                    bool saved = dv.SaveDocument(target, out err);
                    if (saved && target.Length > 0) AssistantHarness.Report("Saved " + Path.GetFileName(target) + " in Documents");
                    return J(new { saved = saved, path = target, error = err });
                }
                case "excel_info":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "excel");
                    AssistantHarness.Report("Inspecting Excel " + Path.GetFileName(ed.Path));
                    return J(OfficeCom.ExcelInfo(ed));
                }
                case "excel_read":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "excel");
                    string sheet = S(a, "sheet");
                    string range = S(a, "range");
                    int maxRows = I(a, "max_rows", 200);
                    int maxCols = I(a, "max_cols", 40);
                    AssistantHarness.Report("Reading Excel " + Path.GetFileName(ed.Path) + (sheet.Length > 0 ? " [" + sheet + "]" : ""));
                    return J(OfficeCom.ExcelRead(ed, sheet, range, maxRows, maxCols));
                }
                case "excel_write":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "excel");
                    string sheet = S(a, "sheet");
                    string startCell = Has(a, "start_cell") ? S(a, "start_cell") : "A1";
                    var values = Matrix(a, "values");
                    bool save = B(a, "save") == true;
                    AssistantHarness.Report("Writing Excel " + Path.GetFileName(ed.Path) + " at " + startCell + (save ? " (saving)" : ""));
                    return J(OfficeCom.ExcelWrite(ed, sheet, startCell, values, save));
                }
                case "excel_formula":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "excel");
                    string sheet = S(a, "sheet");
                    string cell = S(a, "cell");
                    string formula = S(a, "formula");
                    bool save = B(a, "save") == true;
                    AssistantHarness.Report("Setting formula " + cell + " in " + Path.GetFileName(ed.Path));
                    return J(OfficeCom.ExcelFormula(ed, sheet, cell, formula, save));
                }
                case "excel_sheet_add":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "excel");
                    string name = S(a, "name");
                    bool activate = B(a, "activate") != false;
                    AssistantHarness.Report("Adding Excel sheet " + name + " in " + Path.GetFileName(ed.Path));
                    return J(OfficeCom.ExcelSheetAdd(ed, name, activate));
                }
                case "excel_format":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "excel");
                    string sheet = S(a, "sheet");
                    string range = S(a, "range");
                    bool? bold = B(a, "bold");
                    string bg = S(a, "bg_color");
                    string fg = S(a, "font_color");
                    string nf = S(a, "number_format");
                    bool autoFit = B(a, "auto_fit") == true;
                    AssistantHarness.Report("Formatting Excel " + Path.GetFileName(ed.Path) + " " + range);
                    return J(OfficeCom.ExcelFormat(ed, sheet, range, bold, bg, fg, nf, autoFit));
                }
                case "word_read":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "word");
                    int maxChars = I(a, "max_chars", 30000);
                    AssistantHarness.Report("Reading Word " + Path.GetFileName(ed.Path));
                    return J(OfficeCom.WordRead(ed, maxChars));
                }
                case "word_write":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "word");
                    string mode = S(a, "mode");
                    string text = Has(a, "text") ? S(a, "text") : null;
                    string find = S(a, "find");
                    var rows = Has(a, "rows") ? StrMatrix(a, "rows") : null;
                    bool save = B(a, "save") == true;
                    AssistantHarness.Report("Editing Word " + Path.GetFileName(ed.Path) + " (" + mode + ")");
                    return J(OfficeCom.WordWrite(ed, mode, text, find, rows, save));
                }
                case "ppt_read":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "powerpoint");
                    AssistantHarness.Report("Reading PowerPoint " + Path.GetFileName(ed.Path));
                    return J(OfficeCom.PptRead(ed));
                }
                case "ppt_write":
                {
                    var ed = ResolveOfficeEditor(S(a, "path"), "powerpoint");
                    int slide = I(a, "slide", 0);
                    string title = Has(a, "title") ? S(a, "title") : null;
                    string body = Has(a, "body") ? S(a, "body") : null;
                    bool addSlide = B(a, "add_slide") == true;
                    bool save = B(a, "save") == true;
                    AssistantHarness.Report((addSlide ? "Adding slide in " : "Editing slide " + (slide > 0 ? slide : 1) + " in ") + Path.GetFileName(ed.Path));
                    return J(OfficeCom.PptWrite(ed, slide, title, body, addSlide, save));
                }
                case "browser_open":
                {
                    string t = S(a, "target"); if (string.IsNullOrWhiteSpace(t)) throw new Exception("Give a URL or a file path.");
                    App.I.OpenInBrowser(t);
                    AssistantHarness.Report("Opened " + Cut(t, 60) + " in Browser");
                    return J(new { opened = t });
                }
                case "notify": App.I.Balloon(Has(a, "title") ? S(a, "title") : Ui.AppTitle, S(a, "text")); return J(new { shown = true });

                case "notes_search":
                {
                    string tag = S(a, "tag"); bool arch = B(a, "include_archived") == true;
                    var q = Store.Notes.Where(n => (arch || !n.Archived) && Match(n.Text + " " + n.Tags, S(a, "query")) && (tag.Length == 0 || Store.SplitTags(n.Tags).Any(x => x.Equals(tag, StringComparison.OrdinalIgnoreCase))))
                        .OrderByDescending(n => n.Modified).Take(I(a, "limit", 30));
                    return J(q.Select(n => new { id = n.Id, title = Store.TitleFrom(n.Text), tags = n.Tags, color = n.Color, archived = n.Archived, modified = Iso(n.Modified), preview = Cut(n.Text, 200) }));
                }
                case "note_get":
                {
                    var n = Note(a);
                    return J(new { id = n.Id, text = n.Text, tags = n.Tags, color = n.Color, archived = n.Archived, open = n.Open, created = Iso(n.Created), modified = Iso(n.Modified), remind_at = Iso(n.RemindAt) });
                }
                case "note_create":
                {
                    var n = new Note { Id = Store.NewId(), Created = Store.Now(), Modified = Store.Now(), Text = S(a, "text"), Tags = S(a, "tags"), Color = NoteColorName(S(a, "color")), W = Ui.S(320), H = Ui.S(320) };
                    n.Rtl = Ui.IsRtl(n.Text);
                    Store.Notes.Insert(0, n); Store.MarkNotes(); Store.RaiseNotes();
                    if (B(a, "open") == true) App.I.OpenNote(n);
                    return J(new { id = n.Id, created = true });
                }
                case "note_update":
                {
                    var n = Note(a);
                    if (Has(a, "text") || Has(a, "append"))
                    {
                        string text = Has(a, "text") ? S(a, "text") : n.Text;
                        if (Has(a, "append")) text = text.TrimEnd() + (text.Trim().Length > 0 ? "\n" : "") + S(a, "append");
                        App.I.SetNoteText(n, text);
                    }
                    if (Has(a, "tags")) n.Tags = S(a, "tags");
                    if (Has(a, "color")) n.Color = NoteColorName(S(a, "color"));
                    n.Modified = Store.Now(); Store.MarkNotes(); Store.RaiseNotes();
                    var arch = B(a, "archived");
                    if (arch != null && arch.Value != n.Archived) App.I.ArchiveNote(n, arch.Value);
                    return J(new { id = n.Id, updated = true });
                }

                case "tasks_search":
                {
                    string list = S(a, "list"); bool done = B(a, "include_done") == true;
                    long before = Has(a, "due_before") ? Date(S(a, "due_before")) : 0;
                    var q = Store.Tasks.Where(t => (done || !t.Done) && (list.Length == 0 || t.List.Equals(list, StringComparison.OrdinalIgnoreCase)) && Match(t.Title + " " + t.Notes, S(a, "query")) && (before == 0 || (t.Due > 0 && t.Due < before)))
                        .OrderBy(t => t.Done).ThenBy(t => t.Due == 0 ? long.MaxValue : t.Due).ThenByDescending(t => t.Priority).Take(I(a, "limit", 50));
                    return J(q.Select(TaskJson));
                }
                case "task_lists": return J(Store.TaskLists());
                case "task_create":
                {
                    var t = new TaskItem { Title = S(a, "title"), Notes = S(a, "notes"), List = Has(a, "list") ? S(a, "list") : "Inbox" };
                    if (Has(a, "priority")) t.Priority = Prio(S(a, "priority"));
                    if (Has(a, "due")) { t.Due = Date(S(a, "due")); t.DueHasTime = S(a, "due").Contains(":"); }
                    if (Has(a, "remind_at")) t.RemindAt = Date(S(a, "remind_at"));
                    if (Has(a, "repeat")) t.Repeat = S(a, "repeat");
                    foreach (var s in L(a, "steps")) t.Steps.Add(new SubTask { Text = s });
                    Store.AddTask(t);
                    return J(TaskJson(t));
                }
                case "task_update":
                {
                    var t = Store.Tasks.FirstOrDefault(x => x.Id == S(a, "id"));
                    if (t == null) throw new Exception("No task with id " + S(a, "id"));
                    if (Has(a, "title")) t.Title = S(a, "title");
                    if (Has(a, "notes")) t.Notes = S(a, "notes");
                    if (Has(a, "list")) t.List = S(a, "list");
                    if (Has(a, "priority")) t.Priority = Prio(S(a, "priority"));
                    if (Has(a, "due")) { string d = S(a, "due"); t.Due = d.Length == 0 ? 0 : Date(d); t.DueHasTime = d.Contains(":"); }
                    foreach (var s in L(a, "add_steps")) t.Steps.Add(new SubTask { Text = s });
                    var done = B(a, "done");
                    if (done != null && done.Value != t.Done) Store.CompleteTask(t, done.Value); else Store.TouchTask(t);
                    return J(TaskJson(t));
                }

                case "prompts_search":
                {
                    string cat = S(a, "category"); bool fav = B(a, "favorites_only") == true;
                    var q = Store.Prompts.Where(p => (!fav || p.Fav) && (cat.Length == 0 || p.Category.Equals(cat, StringComparison.OrdinalIgnoreCase)) && Match(p.Title + " " + p.Body + " " + p.Category + " " + p.Tags, S(a, "query")))
                        .OrderByDescending(p => p.Fav).ThenByDescending(p => p.Uses).ThenByDescending(p => p.Modified).Take(I(a, "limit", 30));
                    return J(q.Select(p => new { id = p.Id, title = p.Title, category = p.Category, tags = p.Tags, favorite = p.Fav, uses = p.Uses, blanks = Blanks(p.Body), preview = Cut(p.Body, 200) }));
                }
                case "prompt_get":
                {
                    var p = Prompt(S(a, "id"));
                    return J(new { id = p.Id, title = p.Title, body = p.Body, category = p.Category, tags = p.Tags, favorite = p.Fav, uses = p.Uses, blanks = Blanks(p.Body), versions = p.Versions.Count, modified = Iso(p.Modified) });
                }
                case "prompt_save":
                {
                    Prompt p;
                    if (Has(a, "id") && S(a, "id").Length > 0)
                    {
                        p = Prompt(S(a, "id"));
                        string title = Has(a, "title") ? S(a, "title") : p.Title, body = Has(a, "body") ? S(a, "body") : p.Body;
                        if (title != p.Title || body != p.Body) p.Versions.Insert(0, new PromptVersion { Title = p.Title, Body = p.Body, Time = Store.Now() });
                        p.Title = title; p.Body = body;
                        if (Has(a, "category")) p.Category = S(a, "category");
                    }
                    else
                    {
                        if (S(a, "body").Length == 0) throw new Exception("body is required for a new prompt.");
                        p = Store.NewPrompt(Has(a, "title") ? S(a, "title") : Store.TitleFrom(S(a, "body")), S(a, "body"), S(a, "category"));
                    }
                    if (Has(a, "tags")) p.Tags = S(a, "tags");
                    var fav = B(a, "favorite"); if (fav != null) p.Fav = fav.Value;
                    p.Modified = Store.Now(); Store.MarkPrompts(); Store.RaisePrompts();
                    return J(new { id = p.Id, saved = true });
                }
                case "prompt_fill":
                {
                    var p = Prompt(S(a, "id"));
                    p.Uses++; p.LastUsed = Store.Now(); Store.MarkPrompts();
                    return Fill(p.Body, Has(a, "values") ? a["values"] as Dictionary<string, object> : null);
                }
                case "_prompts":
                    return J(Store.Prompts.OrderByDescending(p => p.Fav).ThenByDescending(p => p.Uses).Take(200).Select(p => new { name = PromptName(p), title = p.Title, description = (p.Category.Length > 0 ? p.Category + ": " : "") + Cut(p.Body, 120), vars = Blanks(p.Body) }));
                case "_prompt_fill":
                {
                    var p = Store.Prompts.FirstOrDefault(x => PromptName(x) == S(a, "name"));
                    if (p == null) throw new Exception("No prompt named " + S(a, "name"));
                    p.Uses++; p.LastUsed = Store.Now(); Store.MarkPrompts();
                    return Fill(p.Body, a.ContainsKey("values") ? a["values"] as Dictionary<string, object> : null);
                }

                case "clips_search":
                {
                    string kind = S(a, "kind"), folder = S(a, "folder"); bool pinned = B(a, "pinned_only") == true;
                    var q = Store.Clips.Where(c => (kind.Length == 0 || c.Kind == kind) && (!pinned || c.Pinned) && (folder.Length == 0 || string.Equals(c.Folder, folder, StringComparison.OrdinalIgnoreCase)) && Match(c.Text + " " + c.Source, S(a, "query")))
                        .OrderByDescending(c => c.Time).Take(I(a, "limit", 20));
                    return J(q.Select(c => new { id = c.Id, kind = c.Kind, time = Iso(c.Time), source = c.Source, pinned = c.Pinned, folder = c.Folder, label = c.Label, preview = c.Kind == "image" ? "image " + c.ImgW + "x" + c.ImgH : Cut(c.Text, 300) }));
                }
                case "clip_get":
                {
                    var c = Clip(S(a, "id"));
                    return J(new { id = c.Id, kind = c.Kind, text = c.Text, image_file = c.Kind == "image" ? Store.ImagePath(c) : null, time = Iso(c.Time), source = c.Source, pinned = c.Pinned, folder = c.Folder, label = c.Label });
                }
                case "clip_organize":
                {
                    var c = Clip(S(a, "id"));
                    var pin = B(a, "pinned"); if (pin != null) c.Pinned = pin.Value;
                    if (Has(a, "folder")) c.Folder = S(a, "folder");
                    if (Has(a, "label")) c.Label = S(a, "label");
                    Store.MarkClips(); Store.RaiseClips();
                    return J(new { id = c.Id, pinned = c.Pinned, folder = c.Folder, label = c.Label });
                }
                case "clipboard_copy": return J(new { copied = App.I.CopyText(S(a, "text")) });

                case "folder_sets":
                    return J(FolderSets.Ordered().Select(x => new { name = x.Name, favorite = x.Fav, folders = x.Folders, note = x.Note, uses = x.Uses, used = Iso(x.Used) }));
                case "folder_set_save":
                {
                    string name = S(a, "name").Trim();
                    var folders = FolderSets.Clean(S(a, "folders").Replace("\r", "").Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries));
                    if (name.Length == 0 || folders.Count == 0) throw new Exception("name and folders are required.");
                    var set = FolderSets.All.FirstOrDefault(x => x.Name == name);
                    if (set == null) { set = new FolderSet { Name = name, Created = Store.Now() }; FolderSets.All.Add(set); }
                    set.Folders = folders;
                    var fav = B(a, "favorite"); if (fav != null) set.Fav = fav.Value;
                    FolderSets.Save();
                    return J(new { saved = name, folders = folders.Count });
                }
                case "favorites_list":
                {
                    string scope = S(a, "scope");
                    return J(new
                    {
                        favorites = MetaStore.Favorites(scope).Select(MetaJson),
                        recent = MetaStore.Recent(scope, I(a, "limit", 20)).Select(MetaJson)
                    });
                }
                case "folder_sets_list":
                    return J(FolderSets.Ordered().Select(x => new { name = x.Name, favorite = x.Fav, folders = x.Folders, note = x.Note, uses = x.Uses }));
                case "agent_limits":
                    return J(AgentLimits.ReadAll().Select(x => new { agent = x.Agent, account = x.Account, five_hour_pct = x.FiveHour, five_hour_resets = x.FiveResets.HasValue ? x.FiveResets.Value.ToString("s") : null, weekly_pct = x.Weekly, weekly_resets = x.WeekResets.HasValue ? x.WeekResets.Value.ToString("s") : null, note = x.Note }));
                case "item_organize":
                {
                    string scope = S(a, "scope"), id = S(a, "id");
                    if (scope.Length == 0 || id.Length == 0) throw new Exception("scope and id are required.");
                    var fav = B(a, "favorite"); if (fav != null) MetaStore.SetFav(scope, id, fav.Value);
                    if (Has(a, "color")) MetaStore.SetColor(scope, id, S(a, "color"));
                    if (Has(a, "tags")) MetaStore.SetTags(scope, id, S(a, "tags"));
                    if (Has(a, "note")) MetaStore.SetNote(scope, id, S(a, "note"));
                    if (Has(a, "name")) MetaStore.SetName(scope, id, S(a, "name"));
                    var m = MetaStore.Get(scope, id);
                    return J(m == null ? (object)new { scope = scope, id = id } : MetaJson(m));
                }

                case "agents_list":
                    return J(AgentStore.Defs.Select(d => { string f, pre; return new { id = d.Id, name = d.Name, installed = AgentRun.Resolve(d.Exe, out f, out pre) }; }));
                case "agent_chats":
                    return J(AgentStore.Chats.Where(c => Match(c.Title + " " + c.Folder, S(a, "query"))).OrderByDescending(c => c.Updated).Take(I(a, "limit", 20))
                        .Select(c => new { id = c.Id, title = c.Title, folder = c.Folder, agents = c.Agents, mode = c.Mode, favorite = c.Fav, messages = c.Messages.Count, updated = Iso(c.Updated) }));
                case "agent_chat_get":
                {
                    var c = AgentStore.Find(S(a, "id"));
                    if (c == null) throw new Exception("No chat with id " + S(a, "id"));
                    int last = Math.Max(1, I(a, "last", 20));
                    return J(new { id = c.Id, title = c.Title, folder = c.Folder, agents = c.Agents, mode = c.Mode, total = c.Messages.Count,
                        messages = c.Messages.Skip(Math.Max(0, c.Messages.Count - last)).Select(m => new { role = m.Role, agent = m.Agent, text = m.Text, time = Iso(m.Time) }) });
                }
            }
            throw new Exception("Unknown tool: " + tool);
        }

        // ---------- Search everything ----------
        static string SearchAll(Dictionary<string, object> a)
        {
            string q = S(a, "query"); int n = I(a, "limit", 8);
            if (q.Trim().Length == 0) throw new Exception("query is required.");
            return J(new
            {
                notes = Store.Notes.Where(x => Match(x.Text + " " + x.Tags, q)).OrderByDescending(x => x.Modified).Take(n).Select(x => new { id = x.Id, title = Store.TitleFrom(x.Text), archived = x.Archived }),
                tasks = Store.Tasks.Where(x => Match(x.Title + " " + x.Notes, q)).OrderBy(x => x.Done).Take(n).Select(x => new { id = x.Id, title = x.Title, done = x.Done, due = Iso(x.Due) }),
                prompts = Store.Prompts.Where(x => Match(x.Title + " " + x.Body + " " + x.Tags, q)).Take(n).Select(x => new { id = x.Id, title = x.Title, category = x.Category }),
                clips = Store.Clips.Where(x => x.Kind != "image" && Match(x.Text, q)).OrderByDescending(x => x.Time).Take(n).Select(x => new { id = x.Id, preview = Cut(x.Text, 120), time = Iso(x.Time) }),
                agent_chats = AgentStore.Chats.Where(x => Match(x.Title + " " + x.Folder, q) || x.Messages.Any(m => Match(m.Text, q))).OrderByDescending(x => x.Updated).Take(n).Select(x => new { id = x.Id, title = x.Title, folder = x.Folder })
            });
        }

        // ---------- Agents ----------
        static int ChatFileCount() { try { string d = Path.Combine(AgentStore.Dir, "chats"); return Directory.Exists(d) ? Directory.GetFiles(d, "*.json").Length : 0; } catch { return 0; } }

        static void EnsureChats()
        {
            if (AgentStore.Loaded) return;
            var done = new ManualResetEvent(false);
            App.I.Sync.BeginInvoke(new Action(() => AgentStore.LoadAll(() => done.Set())));
            done.WaitOne(15000);
        }

        static string AgentAsk(Dictionary<string, object> a)
        {
            EnsureChats();
            string prompt = S(a, "prompt");
            if (prompt.Trim().Length == 0) throw new Exception("prompt is required.");
            int timeout = Math.Max(30, Math.Min(7200, I(a, "timeout_seconds", 900)));
            AgentChat chat = null;
            List<string> agents = null;
            UI(() =>
            {
                string id = S(a, "chat_id");
                if (id.Length > 0)
                {
                    chat = AgentStore.Find(id);
                    if (chat == null) throw new Exception("No chat with id " + id);
                }
                else
                {
                    var ids = L(a, "agents");
                    if (ids.Count == 0) ids.Add("claude");
                    foreach (var x in ids) if (AgentStore.Def(x) == null) throw new Exception("Unknown agent \"" + x + "\". Use agents_list.");
                    string folder = S(a, "folder");
                    if (folder.Length == 0) { var r = MetaStore.Recent("folders", 1).FirstOrDefault(); folder = r != null ? r.Id : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
                    if (!Directory.Exists(folder)) throw new Exception("Folder not found: " + folder);
                    chat = AgentStore.New(folder, ids);
                    chat.Mode = "ask";
                    chat.Title = Cut(prompt.Replace("\r", " ").Replace("\n", " ").Trim(), 70);
                }
                if (Has(a, "mode")) { string m = S(a, "mode"); if (m != "ask" && m != "edit" && m != "auto") throw new Exception("mode must be ask, edit or auto."); chat.Mode = m; }
                if (AgentStore.Busy.Contains(chat.Id)) throw new Exception("This chat is busy in the Agents page. Wait or use another chat.");
                AgentStore.Busy.Add(chat.Id);
                chat.Messages.Add(new AgentMsg { Role = "user", Agent = "", Text = prompt, Time = Store.Now() });
                chat.Updated = Store.Now();
                agents = chat.Agents.ToList();
                MetaStore.Touch("folders", chat.Folder);
                AgentStore.Save(chat); AgentStore.RaiseChat(chat);
                return "";
            });
            var replies = new List<object>();
            try
            {
                foreach (var agentId in agents)
                {
                    var texts = new List<string>(); var errors = new List<string>(); int tools = 0;
                    var exited = new ManualResetEvent(false);
                    var run = new AgentRun();
                    run.Event += (kind, text) =>
                    {
                        lock (texts) { if (kind == "text") texts.Add(text); else if (kind == "error") errors.Add(text); else if (kind == "tool") tools++; }
                        App.I.Sync.BeginInvoke(new Action(() =>
                        {
                            if (kind == "session") { chat.Sessions[run.ActiveSessionKey] = text; return; }
                            if (kind == "info") return;
                            chat.Messages.Add(new AgentMsg { Role = kind == "text" ? "agent" : kind, Agent = agentId, Text = text, Time = Store.Now() });
                            chat.Updated = Store.Now();
                        }));
                    };
                    run.Exited += code => exited.Set();
                    string err = null;
                    bool started = (bool)App.I.Sync.Invoke(new Func<bool>(() =>
                    {
                        var def = AgentStore.Def(agentId);
                        if (def == null) { err = "Unknown agent " + agentId; return false; }
                        string p = AgentStore.PromptFor(chat, agentId);
                        if (!run.Start(def, chat, p, out err)) return false;
                        MetaStore.Touch("agents", agentId);
                        return true;
                    }));
                    if (!started) { UI(() => { chat.Messages.Add(new AgentMsg { Role = "error", Agent = agentId, Text = err ?? "Could not start.", Time = Store.Now() }); return ""; }); replies.Add(new { agent = agentId, error = err }); continue; }
                    if (!exited.WaitOne(timeout * 1000)) { run.Stop(); errors.Add("Stopped after " + timeout + " s."); exited.WaitOne(5000); }
                    UI(() => { chat.Seen[run.ActiveSessionKey] = chat.Messages.Count; AgentStore.Save(chat); AgentStore.RaiseChat(chat); return ""; });
                    lock (texts) replies.Add(new { agent = agentId, name = AgentStore.NameOf(agentId), text = string.Join("\n\n", texts), errors = errors.ToArray(), tool_calls = tools });
                }
            }
            finally
            {
                UI(() => { AgentStore.Busy.Remove(chat.Id); AgentStore.Save(chat); AgentStore.RaiseChat(chat); AgentStore.Raise(); return ""; });
            }
            return J(new { chat_id = chat.Id, folder = chat.Folder, mode = chat.Mode, replies = replies });
        }

        // ---------- Office COM Helpers & Background Extractor ----------
        static string OfficeExtract(Dictionary<string, object> a)
        {
            string p = S(a, "path").Trim();
            if (p.Length == 0) throw new Exception("path is required.");
            if (!File.Exists(p)) throw new Exception("File not found: " + p);
            int maxRows = Math.Max(1, Math.Min(200000, I(a, "max_rows", 25000)));
            int previewRows = Math.Max(0, Math.Min(500, I(a, "preview_rows", 50)));

            AssistantHarness.Report("Extracting " + Path.GetFileName(p) + "...");
            ExtractedDoc doc = OfficeCom.ExtractFile(p, maxRows);
            AssistantHarness.Report("Extracted " + Path.GetFileName(p) + " (" + doc.Tables.Count + " tables, " + doc.Sections.Count + " sections)");

            return J(new
            {
                path = doc.Path,
                kind = doc.Kind,
                is_drm = doc.IsDrm,
                file_size = doc.FileSize,
                modified_ticks = doc.ModifiedTicks,
                error = doc.Error,
                table_count = doc.Tables.Count,
                section_count = doc.Sections.Count,
                tables = doc.Tables.Select(t => new
                {
                    name = t.Name,
                    total_rows = t.TotalRows,
                    total_cols = t.TotalCols,
                    columns = t.Columns,
                    preview_rows = t.Rows.Take(previewRows).ToList()
                }).ToList(),
                sections = doc.Sections.Select(s => new
                {
                    index = s.Index,
                    title = s.Title,
                    text = Cut(s.Text, 4000)
                }).ToList(),
                full_text_length = doc.FullText != null ? doc.FullText.Length : 0,
                full_text_preview = Cut(doc.FullText, 20000)
            });
        }

        // ---------- Phase 3 Tool Handlers (Off-UI-Thread) ----------
        static string VaultExtract(Dictionary<string, object> a)
        {
            string p = S(a, "path").Trim();
            if (p.Length == 0) throw new Exception("path is required.");
            bool force = B(a, "force") == true;
            int maxRows = Math.Max(1, Math.Min(200000, I(a, "max_rows", 25000)));
            var res = SqliteVault.ExtractAndStore(p, force, maxRows);
            bool cached = res.ContainsKey("cached") && true.Equals(res["cached"]);
            if (!cached && !res.ContainsKey("error"))
                AssistantHarness.Report("Indexed " + Path.GetFileName(p) + " -> vault.db");
            return J(res);
        }

        static string ToolCreate(Dictionary<string, object> a)
        {
            BuiltInSchema();
            object argsSchema = Has(a, "args_schema") ? a["args_schema"] : null;
            return J(AgentMemory.ToolCreate(
                S(a, "name"),
                S(a, "title"),
                S(a, "description"),
                S(a, "kind"),
                S(a, "script"),
                argsSchema,
                B(a, "read_only"),
                builtInNames));
        }

        static string TodoPlanTool(Dictionary<string, object> a)
        {
            string action = S(a, "action");
            string chatId = S(a, "chat_id");
            var steps = new List<Dictionary<string, object>>();
            if (Has(a, "steps"))
            {
                object raw = a["steps"];
                string rawStr = raw as string;
                if (rawStr != null && rawStr.Trim().StartsWith("["))
                {
                    try { raw = Js.DeserializeObject(rawStr); }
                    catch (Exception) { }
                }
                var seq = raw as IEnumerable;
                if (seq != null && !(raw is string))
                {
                    foreach (var item in seq)
                    {
                        var d = item as Dictionary<string, object>;
                        if (d != null) steps.Add(d);
                        else if (item != null)
                            steps.Add(new Dictionary<string, object> { { "text", Convert.ToString(item, CultureInfo.InvariantCulture) }, { "status", "pending" } });
                    }
                }
            }
            return J(AgentMemory.TodoManage(chatId, action, steps, I(a, "step", 0), S(a, "status"), S(a, "text")));
        }

        static string ChatStepsTool(Dictionary<string, object> a)
        {
            string chatId = S(a, "chat_id");
            string action = Has(a, "action") ? S(a, "action").Trim().ToLowerInvariant() : "list";
            if (action == "fork")
                return J(SqliteVault.ForkAt(chatId, L64(a, "step_id", 0), S(a, "new_chat_id")));
            return J(SqliteVault.GetSteps(chatId, I(a, "limit", 200)));
        }

        static string ChatCompactTool(Dictionary<string, object> a)
        {
            string chatId = S(a, "chat_id").Trim();
            if (chatId.Length == 0) throw new Exception("chat_id is required.");
            int keepN = I(a, "protect_last_n", AgentMemory.ProtectLastN);
            if (App.I != null)
            {
                EnsureChats();
                return UI(() =>
                {
                    var c = AgentStore.Find(chatId);
                    if (c == null) throw new Exception("No chat with id " + chatId);
                    return J(AgentMemory.CompactChat(c, keepN));
                });
            }
            var chat = AgentStore.Find(chatId);
            if (chat == null) throw new Exception("No chat with id " + chatId);
            return J(AgentMemory.CompactChat(chat, keepN));
        }

        static string CronManageTool(Dictionary<string, object> a)
        {
            return J(CronEngine.Manage(
                S(a, "action"),
                S(a, "id"),
                S(a, "name"),
                S(a, "schedule"),
                S(a, "prompt"),
                S(a, "skill"),
                S(a, "precheck_type"),
                S(a, "precheck_arg"),
                B(a, "enabled"),
                B(a, "one_shot"),
                I(a, "in_minutes", 0)));
        }

        static string WatchFolderTool(Dictionary<string, object> a)
        {
            string act = Has(a, "action") ? S(a, "action").Trim().ToLowerInvariant() : "add";
            if (act == "list")
                return J(new { watched_folders = CronEngine.WatchedFolders() });
            if (act == "remove" || act == "unwatch" || act == "delete")
                return J(CronEngine.UnwatchFolder(S(a, "path")));
            return J(CronEngine.WatchFolder(S(a, "path")));
        }

        static long L64(Dictionary<string, object> a, string k, long def)
        {
            long v;
            return Has(a, k) && long.TryParse(S(a, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        static OfficeEdit ResolveOfficeEditor(string path, string expectedKind)
        {
            var dv = App.I != null ? App.I.DocumentsView : null;
            string p = (path ?? "").Trim();

            if (p.Length > 0)
            {
                if (dv != null)
                {
                    var ed = FindEditorIn(dv, p, expectedKind);
                    if (ed != null)
                    {
                        if (!string.Equals(dv.CurrentPath, ed.Path, StringComparison.OrdinalIgnoreCase))
                            dv.SwitchTo(ed.Path);
                        return ed;
                    }
                }
                if (!File.Exists(p)) throw new Exception("File not found: " + p);
                string kind = OfficeEdit.KindOf(p);
                if (!string.Equals(kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("File is " + (kind ?? Path.GetExtension(p)) + ", expected " + expectedKind + ": " + p);
                App.I.OpenDocument(p, true);
                dv = App.I.DocumentsView;
                var opened = dv != null ? dv.EditorFor(p) : null;
                if (opened == null)
                    throw new Exception("Could not open " + Path.GetFileName(p) + " in live " + expectedKind + " Edit mode (check if it is already open in another app). Use office_extract for read-only extraction.");
                return opened;
            }

            if (dv != null && dv.ActiveEditor != null && string.Equals(dv.ActiveEditor.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                return dv.ActiveEditor;

            if (dv != null && !string.IsNullOrEmpty(dv.CurrentPath)
                && string.Equals(OfficeEdit.KindOf(dv.CurrentPath), expectedKind, StringComparison.OrdinalIgnoreCase)
                && File.Exists(dv.CurrentPath))
            {
                App.I.OpenDocument(dv.CurrentPath, true);
                dv = App.I.DocumentsView;
                if (dv != null && dv.ActiveEditor != null && string.Equals(dv.ActiveEditor.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                    return dv.ActiveEditor;
            }

            if (dv != null)
            {
                var matches = new List<OfficeEdit>();
                foreach (var tab in dv.OpenTabs)
                {
                    var e = dv.EditorFor(tab);
                    if (e != null && string.Equals(e.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                        matches.Add(e);
                }
                if (matches.Count == 1) return matches[0];
            }

            throw new Exception("No active " + expectedKind + " document is open in Edit mode. Pass a file path or open one on the Documents page.");
        }

        static OfficeEdit FindEditorIn(DocumentsPage dv, string pathOrName, string expectedKind)
        {
            var exact = dv.EditorFor(pathOrName);
            if (exact != null)
            {
                if (!string.Equals(exact.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("Open document is " + exact.Kind + ", expected " + expectedKind + ".");
                return exact;
            }
            var byName = new List<OfficeEdit>();
            foreach (var tab in dv.OpenTabs)
            {
                if (string.Equals(Path.GetFileName(tab), pathOrName, StringComparison.OrdinalIgnoreCase))
                {
                    var e = dv.EditorFor(tab);
                    if (e != null && string.Equals(e.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                        byName.Add(e);
                }
            }
            return byName.Count == 1 ? byName[0] : null;
        }

        static List<List<object>> Matrix(Dictionary<string, object> a, string k)
        {
            if (!Has(a, k)) return new List<List<object>>();
            object raw = a[k];
            string s = raw as string;
            if (s != null)
            {
                string trimmed = s.Trim();
                if (trimmed.StartsWith("["))
                {
                    try { raw = Js.DeserializeObject(trimmed); }
                    catch (Exception ex) { throw new Exception("Invalid JSON array for " + k + ": " + ex.Message); }
                }
                else
                {
                    return new List<List<object>> { new List<object> { s } };
                }
            }
            var outer = raw as IEnumerable;
            if (outer == null || raw is string)
                return new List<List<object>> { new List<object> { raw } };

            var rows = new List<List<object>>();
            var flat = new List<object>();
            foreach (var item in outer)
            {
                var inner = item as IEnumerable;
                if (inner != null && !(item is string) && !(item is IDictionary))
                {
                    var row = new List<object>();
                    foreach (var cell in inner) row.Add(cell);
                    rows.Add(row);
                }
                else
                {
                    flat.Add(item);
                }
            }
            if (flat.Count > 0) rows.Add(flat);
            return rows;
        }

        static List<List<string>> StrMatrix(Dictionary<string, object> a, string k)
        {
            var m = Matrix(a, k);
            var outRows = new List<List<string>>(m.Count);
            foreach (var r in m)
            {
                var row = new List<string>(r.Count);
                foreach (var c in r)
                    row.Add(c == null ? "" : Convert.ToString(c, CultureInfo.InvariantCulture));
                outRows.Add(row);
            }
            return outRows;
        }

        // ---------- Helpers ----------
        static string J(object o) { return Js.Serialize(o); }
        static bool Has(Dictionary<string, object> a, string k) { return a != null && a.ContainsKey(k) && a[k] != null; }
        static string S(Dictionary<string, object> a, string k) { return McpServer.Str(a, k); }
        static int I(Dictionary<string, object> a, string k, int def) { int v; return Has(a, k) && int.TryParse(S(a, k), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def; }
        static bool? B(Dictionary<string, object> a, string k)
        {
            if (!Has(a, k)) return null;
            if (a[k] is bool) return (bool)a[k];
            string s = S(a, k).ToLowerInvariant(); return s == "true" || s == "1" || s == "yes";
        }
        static List<string> L(Dictionary<string, object> a, string k)
        {
            if (!Has(a, k)) return new List<string>();
            var e = a[k] as IEnumerable;
            if (a[k] is string || e == null) return S(a, k).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            return e.Cast<object>().Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).Where(x => !string.IsNullOrEmpty(x)).ToList();
        }
        static string Iso(long ticks) { return ticks > 0 && ticks < DateTime.MaxValue.Ticks ? new DateTime(ticks).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) : null; }
        static long Date(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant();
            if (s == "today") return DateTime.Today.Ticks;
            if (s == "tomorrow") return DateTime.Today.AddDays(1).Ticks;
            DateTime d;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out d)) return d.ToLocalTime().Ticks;
            throw new Exception("Not a date: \"" + s + "\". Use ISO like 2026-11-20 or 2026-11-20T15:30.");
        }
        static int Prio(string s) { s = s.ToLowerInvariant(); return s == "low" || s == "0" ? 0 : s == "high" || s == "2" ? 2 : 1; }
        static string Cut(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) + "..." : s; }
        static bool Match(string text, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            text = text ?? "";
            return query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(w => text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        static object TaskJson(TaskItem t)
        {
            return new { id = t.Id, title = t.Title, notes = t.Notes, list = t.List, priority = t.Priority == 0 ? "low" : t.Priority == 2 ? "high" : "normal", due = Iso(t.Due), remind_at = Iso(t.RemindAt), repeat = t.Repeat, done = t.Done, steps = t.Steps.Select(s => new { text = s.Text, done = s.Done }) };
        }
        static object MetaJson(ItemMeta m) { return new { id = m.Id, name = MetaStore.DisplayName(m), favorite = m.Fav, color = m.Color, tags = m.Tags, note = m.Note, uses = m.Uses, used = Iso(m.Used) }; }
        static Note Note(Dictionary<string, object> a) { var n = Store.Notes.FirstOrDefault(x => x.Id == S(a, "id")); if (n == null) throw new Exception("No note with id " + S(a, "id")); return n; }
        static Prompt Prompt(string id) { var p = Store.Prompts.FirstOrDefault(x => x.Id == id); if (p == null) throw new Exception("No prompt with id " + id); return p; }
        static Clip Clip(string id) { var c = Store.Clips.FirstOrDefault(x => x.Id == id); if (c == null) throw new Exception("No clip with id " + id); return c; }
        static string NoteColorName(string c)
        {
            var m = Ui.Colors.FirstOrDefault(x => x.Name.Equals(c ?? "", StringComparison.OrdinalIgnoreCase));
            return m != null ? m.Name : "Yellow";
        }
        static readonly Regex blank = new Regex(@"\{\{\s*([^{}]+?)\s*\}\}");
        static List<string> Blanks(string body) { return blank.Matches(body ?? "").Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        static string Fill(string body, Dictionary<string, object> values)
        {
            if (values == null) return body;
            return blank.Replace(body, m => { object v; var key = values.Keys.FirstOrDefault(k => k.Equals(m.Groups[1].Value, StringComparison.OrdinalIgnoreCase)); return key != null && (v = values[key]) != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : m.Value; });
        }
        static string PromptName(Prompt p)
        {
            string slug = Regex.Replace((p.Title ?? "").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            if (slug.Length > 40) slug = slug.Substring(0, 40).Trim('-');
            return (slug.Length > 0 ? slug + "-" : "prompt-") + (p.Id ?? "").Substring(0, Math.Min(6, (p.Id ?? "").Length));
        }
    }
}
