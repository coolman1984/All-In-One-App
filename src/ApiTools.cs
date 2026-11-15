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
        public static readonly string[] Pages = { "dashboard", "tasks", "notes", "prompts", "clips", "agents", "terminals", "explorer", "snip", "chrome", "power", "system", "cards", "activity", "tagged", "shortcuts" };

        // ---------- Schema ----------
        static List<Dictionary<string, object>> schema;
        public static HashSet<string> Names { get { return new HashSet<string>(Schema().Select(t => (string)t["name"])); } }

        public static List<Dictionary<string, object>> Schema()
        {
            if (schema != null) return schema;
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
            l.Add(T("item_organize", "Organize an item", "Favorite, color, tag, note or rename any item of a scope (for folders the id is the full path).", false,
                new[] { "scope|string|folders or agents", "id|string|Item id or folder path", "favorite|boolean|Favorite", "color|string|" + string.Join(", ", MetaStore.Colors) + " or \"\"", "tags|string|Comma separated", "note|string|Note", "name|string|Display name (\"\" = default)" }, "scope", "id"));

            l.Add(T("agents_list", "Coding agents", "Agent CLIs the app can run (Claude Code, Codex, Cline, Antigravity, OpenCode) and whether they are installed.", true, new string[0]));
            l.Add(T("agent_chats", "Agent chats", "Saved multi-agent chats, newest first.", true, new[] { "query|string|Words in the title or folder", "limit|integer|Max results (default 20)" }));
            l.Add(T("agent_chat_get", "Read an agent chat", "The last messages of a chat.", true, new[] { "id|string|Chat id", "last|integer|How many messages (default 20)" }, "id"));
            l.Add(T("agent_ask", "Ask coding agents", "Send a message to one or more coding agents in a folder and wait for their answers. Agents answer in turn and see each other's replies. Continue a chat with chat_id. mode: ask (read only, default), edit (may edit files), auto (no approvals).", false,
                new[] { "prompt|string|Message", "agents|array|Agent ids, e.g. [\"claude\", \"codex\"]", "folder|string|Working folder (new chats)", "chat_id|string|Continue this chat", "mode|string|ask, edit or auto", "timeout_seconds|integer|Max wait per agent (default 900)" }, "prompt"));
            schema = l;
            return l;
        }

        static Dictionary<string, object> T(string name, string title, string desc, bool readOnly, string[] props, params string[] required)
        {
            var p = new Dictionary<string, object>();
            foreach (var spec in props)
            {
                var parts = spec.Split('|');
                var prop = new Dictionary<string, object> { { "type", parts[1] }, { "description", parts[2] } };
                if (parts[1] == "array") prop["items"] = new Dictionary<string, object> { { "type", "string" } };
                if (parts[1] == "object") prop["additionalProperties"] = new Dictionary<string, object> { { "type", "string" } };
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
                case "agent_chats": case "agent_chat_get": case "search_all": EnsureChats(); break;
            }
            return UI(() => RunUi(tool, a));
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
                        { "agent_chats", AgentStore.Chats.Count }, { "favorite_folders", MetaStore.Favorites("folders").Count }, { "now", Iso(DateTime.Now.Ticks) }
                    });
                case "search_all": return SearchAll(a);
                case "open_page":
                {
                    string page = S(a, "page").ToLowerInvariant();
                    if (!Pages.Contains(page)) throw new Exception("Unknown page. Use one of: " + string.Join(", ", Pages));
                    App.I.ShowMain(page); return J(new { opened = page });
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

                case "favorites_list":
                {
                    string scope = S(a, "scope");
                    return J(new
                    {
                        favorites = MetaStore.Favorites(scope).Select(MetaJson),
                        recent = MetaStore.Recent(scope, I(a, "limit", 20)).Select(MetaJson)
                    });
                }
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
                            if (kind == "session") { chat.Sessions[agentId] = text; return; }
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
                    UI(() => { chat.Seen[agentId] = chat.Messages.Count; AgentStore.Save(chat); AgentStore.RaiseChat(chat); return ""; });
                    lock (texts) replies.Add(new { agent = agentId, name = AgentStore.NameOf(agentId), text = string.Join("\n\n", texts), errors = errors.ToArray(), tool_calls = tools });
                }
            }
            finally
            {
                UI(() => { AgentStore.Busy.Remove(chat.Id); AgentStore.Save(chat); AgentStore.RaiseChat(chat); AgentStore.Raise(); return ""; });
            }
            return J(new { chat_id = chat.Id, folder = chat.Folder, mode = chat.Mode, replies = replies });
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
