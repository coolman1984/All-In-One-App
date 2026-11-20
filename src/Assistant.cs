using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // The Assistant: one chat docked on the right of every page (Ctrl+J or the top-bar button). It is the full Agents chat
    // (model, effort, account, mode, folder, web search, files, slash commands, limits) whose agent also gets the app's own
    // tools (MCP "all-in-one": notes, tasks, prompts, clipboard, pages, browser, Markdown, Explorer, terminals), so you can
    // give orders on any page and it does the work. Each message carries the page you are on (hidden in the chat).
    // Orders: one-click requests for the current page, all orders in a menu, and your own (prompts in category "Orders").
    class AssistantPanel : Panel
    {
        readonly MainForm main;
        readonly ComboBox chats;
        readonly FlowLayoutPanel orders;
        readonly Panel host;
        ChatPane pane;
        bool filling;
        string lastPage = "";
        const string Collection = "Assistant";

        // page key -> (button text, order). "*" = every page.
        static readonly string[][] Orders =
        {
            new[] { "*", "Daily brief", "Give me a short daily brief: open and overdue tasks (tasks_search), what is due today, my newest sticky notes and anything urgent. End with the 3 things I should do first." },
            new[] { "*", "Plan my day", "Look at my open tasks (tasks_search) and plan my day in time blocks. Then ask me before creating anything." },
            new[] { "*", "Last copy -> task", "Read my latest clipboard item (clips_search, limit 1) and turn it into a well-written task (task_create) with a due date if it mentions one." },
            new[] { "*", "Find anything", "Search everything in the app (search_all) for what I describe next and show the best matches with how to open them. What should I look for?" },
            new[] { "markdown", "Summarize note", "Summarize the open note: a short paragraph and 3-6 bullets." },
            new[] { "markdown", "Note -> tasks", "Extract every action item from the open note and create each one as a task in the app (task_create) with owner/date in the notes when given. List what you created." },
            new[] { "markdown", "Improve note", "Rewrite the open note so it is clearer and better organized (headings, bullets, tables where useful). Same meaning and language. Show me the result first, then save it into the file only if I say yes." },
            new[] { "markdown", "Link notes", "Find other notes in this vault related to the open note and suggest [[links]] to add, with one line why each." },
            new[] { "markdown", "Translate note", "Translate the open note (Arabic <-> English), keep the Markdown, and save it as a new note next to it with ' (translated)' in the name." },
            new[] { "markdown", "Flashcards", "Make 10 question/answer flashcards from the open note as a Markdown table." },
            new[] { "browser", "Summarize page", "Read the browser tab (browser_read) and summarize it: key points, numbers, dates." },
            new[] { "browser", "Page -> note", "Read the browser tab (browser_read) and save a clean summary with the source link as a new sticky note (note_create)." },
            new[] { "browser", "Extract tables", "Read the browser tab (browser_run_js to collect the tables) and give me every table as Markdown." },
            new[] { "tasks", "Break down task", "Take my most urgent open task (tasks_search) and break it into clear steps (task_update add_steps)." },
            new[] { "tasks", "Overdue review", "List my overdue tasks (tasks_search due_before today) and propose new dates. Change them only after I agree." },
            new[] { "tasks", "Weekly review", "Write my weekly review from finished and open tasks: done, still open, problems, next week." },
            new[] { "notes", "Organize notes", "Look at my sticky notes (notes_search) and propose tags and colors to organize them. Apply only after I agree." },
            new[] { "notes", "Notes -> tasks", "Find to-dos inside my sticky notes and create them as tasks (task_create). List what you created." },
            new[] { "prompts", "Improve prompts", "Review my 5 most used prompts (prompts_search) and suggest a better version of each. Save changes only after I agree (prompt_save)." },
            new[] { "prompts", "New prompt", "Help me write a new reusable prompt with {{blanks}} for a task I will describe, then save it (prompt_save). What is the task?" },
            new[] { "clips", "Clean last copy", "Take my latest clipboard text (clips_search limit 1), clean it (spacing, typos, structure) and put the result on the clipboard (clipboard_copy)." },
            new[] { "explorer", "Explain folder", "Explain what is in the folder I have open in Explorer (page_context, then read the files): purpose, main files, how to run it." },
            new[] { "explorer", "Write README", "Write a README.md for the folder I have open in Explorer, based on its files. Show it first; create the file only if I agree." },
            new[] { "explorer", "Find big files", "In the folder I have open in Explorer, find the biggest files and folders and tell me what is safe to clean." },
            new[] { "terminals", "How to run", "For my most recent project folder, tell me how to build and run it, as commands I can paste." },
            new[] { "dashboard", "What needs me", "What needs my attention now? Check tasks, reminders and agent limits (agent_limits), short answer." },
        };

        public AssistantPanel(MainForm main)
        {
            this.main = main;
            Padding = new Padding(Ui.S(8), Ui.S(10), Ui.S(10), Ui.S(6)); BackColor = Ui.Bg;

            var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(36), Padding = new Padding(0, Ui.S(3), 0, Ui.S(3)) };
            var title = new Label { Text = "Assistant", Font = Ui.Bold, Dock = DockStyle.Left, Width = Ui.S(86), TextAlign = ContentAlignment.MiddleLeft };
            chats = MainForm.Combo(160); chats.Dock = DockStyle.Fill;
            chats.SelectedIndexChanged += delegate { if (filling) return; var c = chats.SelectedItem as ChatItem; if (c != null && pane != null && pane.Chat != c.Chat && !pane.Busy) pane.SetChat(c.Chat); };
            var fresh = Ui.Btn("New", delegate { NewChat(); }); fresh.Dock = DockStyle.Right;
            var close = Ui.Btn("✕", delegate { main.SetAssistant(false); }); ((FluentButton)close).Glyph = null; close.Dock = DockStyle.Right; close.Width = Ui.S(36); close.AutoSize = false;
            new ToolTip().SetToolTip(close, "Hide (Ctrl+J)");
            // Orders fold away (remembered), like a VS Code section.
            var fold = new IconButton(OrdersOpen ? "\uE70D" : "\uE76C", 8f, "Show or hide the order buttons", new ToolTip(), null) { Dock = DockStyle.Right, Width = Ui.S(30) };
            fold.Click += delegate { OrdersOpen = !OrdersOpen; fold.Text = OrdersOpen ? "\uE70D" : "\uE76C"; orders.Visible = OrdersOpen; };
            head.Controls.Add(chats); head.Controls.Add(Gap()); head.Controls.Add(fold); head.Controls.Add(fresh); head.Controls.Add(Gap()); head.Controls.Add(close); head.Controls.Add(title);

            orders = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, Ui.S(4), 0, Ui.S(4)), Visible = OrdersOpen };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, Ui.S(4), 0, 0) };
            bottom.Controls.Add(Small("Copy answer", delegate { string a = Last(); if (a.Length > 0) App.I.CopyText(a); }));
            bottom.Controls.Add(Small("Answer -> note", delegate { string a = Last(); if (a.Length > 0) main.InsertIntoNote(a); }));
            bottom.Controls.Add(Small("Answer -> task", delegate { string a = Last(); if (a.Length > 0) { var t = Store.NewTask(Ui.OneLine(a, 100)); t.Notes = a; Store.TouchTask(t); App.I.Status("Task created."); } }));

            host = new Panel { Dock = DockStyle.Fill };
            host.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Loading...", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted });
            Controls.Add(host); Controls.Add(bottom); Controls.Add(orders); Controls.Add(head);
            Ui.Theme(this);
            PageChanged(main.Page);
            if (AgentStore.Loaded) Start(); else AgentStore.LoadAll(() => { if (!IsDisposed) Start(); });
        }

        static bool OrdersOpen
        {
            get { string v; return !Store.Settings.ViewStacks.TryGetValue("assistant.orders", out v) || v != "0"; }
            set { Store.Settings.ViewStacks["assistant.orders"] = value ? "1" : "0"; Store.MarkSettings(); }
        }
        static Panel Gap() { return new Panel { Dock = DockStyle.Right, Width = Ui.S(4) }; }
        static Button Small(string text, EventHandler click) { var b = Ui.Btn(text, click); ((FluentButton)b).Glyph = null; b.Margin = new Padding(0, 0, Ui.S(4), Ui.S(2)); return b; }

        class ChatItem
        {
            public AgentChat Chat;
            public override string ToString() { string t = string.IsNullOrWhiteSpace(Chat.Title) ? "New chat" : Chat.Title; return t + "   ·   " + Ui.Ago(Chat.Updated); }
        }

        // Folders the Assistant may work in (besides its own folder): what is open now plus every Markdown vault.
        public static List<string> Dirs()
        {
            var l = new List<string>();
            try
            {
                var ctx = App.I.PageContext(); object v;
                if (ctx.TryGetValue("note", out v) && Convert.ToString(v).Length > 0) l.Add(Path.GetDirectoryName(Convert.ToString(v)));
                if (ctx.TryGetValue("folder", out v) && Convert.ToString(v).Length > 0) l.Add(Convert.ToString(v));
            }
            catch (Exception) { }
            l.AddRange(Store.Settings.MdVaults ?? new List<string>());
            l.Add(Path.GetDirectoryName(Application.ExecutablePath));
            return l.Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        public void FocusInput() { if (pane != null) pane.FocusInput(); }

        // Orders for this page first, then the ones for every page; all orders + yours in the menu.
        public void PageChanged(string page)
        {
            page = page ?? ""; if (page == lastPage && orders.Controls.Count > 0) return;
            lastPage = page;
            orders.SuspendLayout(); Ui.ClearControls(orders.Controls);
            foreach (var o in Orders.Where(o => o[0] == page).Concat(Orders.Where(o => o[0] == "*")).Take(7))
            { var oo = o; var b = Small(oo[1], delegate { Order(oo[2]); }); new ToolTip().SetToolTip(b, oo[2]); orders.Controls.Add(b); }
            var all = Small("All orders ▾", null);
            all.Click += delegate { OrdersMenu(all); };
            orders.Controls.Add(all);
            orders.ResumeLayout();
        }

        void OrdersMenu(Control anchor)
        {
            var m = Ui.Menu();
            foreach (var g in Orders.GroupBy(o => o[0]))
            {
                var sub = new ToolStripMenuItem(g.Key == "*" ? "Any page" : Ui.PageName(g.Key, char.ToUpper(g.Key[0]) + g.Key.Substring(1)));
                foreach (var o in g) { var oo = o; sub.DropDownItems.Add(oo[1], null, delegate { Order(oo[2]); }); }
                m.Items.Add(sub);
            }
            var mine = Store.Prompts.Where(p => string.Equals(p.Category, "Orders", StringComparison.OrdinalIgnoreCase)).OrderByDescending(p => p.Uses).Take(40).ToList();
            m.Items.Add(new ToolStripSeparator());
            var my = new ToolStripMenuItem("My orders");
            foreach (var p in mine) { var pp = p; my.DropDownItems.Add(pp.Title, null, delegate { var text = Ui.FillVariables(pp.Body, FindForm()); if (text != null) { pp.Uses++; Store.MarkPrompts(); Order(text); } }); }
            if (mine.Count == 0) my.DropDownItems.Add(new ToolStripMenuItem("Add prompts with category \"Orders\" on the Prompts page") { Enabled = false });
            m.Items.Add(my);
            Ui.ThemeMenu(m);
            m.Show(anchor, new Point(0, anchor.Height));
        }

        void Start()
        {
            var last = AgentStore.Chats.Where(c => c.Collection == Collection).OrderByDescending(c => c.Updated).FirstOrDefault();
            Ui.ClearControls(host.Controls);
            if (last != null && last.Mode == "edit") { last.Mode = "auto"; AgentStore.Save(last); }   // older Assistant chats could not reach your files
            pane = new ChatPane(last ?? Create()) { Dock = DockStyle.Fill };
            pane.OutgoingFilter = AddContext;
            pane.NewChatRequested = NewChat;
            host.Controls.Add(pane);
            Ui.Theme(pane);
            AgentStore.Changed += OnChanged;
            FillChats();
        }

        void OnChanged() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke((Action)OnChanged); return; } Ui.Debounce("assistant.chats", 400, FillChats); }

        void FillChats()
        {
            if (IsDisposed || pane == null) return;
            filling = true;
            chats.BeginUpdate(); chats.Items.Clear();
            foreach (var c in AgentStore.Chats.Where(c => c.Collection == Collection && (c.HasContent || c == pane.Chat)).OrderByDescending(c => c.Updated).Take(40))
                chats.Items.Add(new ChatItem { Chat = c });
            chats.EndUpdate();
            for (int i = 0; i < chats.Items.Count; i++) if (((ChatItem)chats.Items[i]).Chat == pane.Chat) chats.SelectedIndex = i;
            filling = false;
        }

        AgentChat Create()
        {
            string agent = AgentStore.Def("claude") != null ? "claude" : (AgentStore.Defs.Select(d => d.Id).FirstOrDefault() ?? "claude");
            var c = AgentStore.New(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), new[] { agent });
            c.Collection = Collection; c.Mode = "auto";   // Full auto: it works for you without stopping for permission
            return c;
        }

        void NewChat()
        {
            if (pane == null) return;
            if (pane.Busy) { App.I.Status("The assistant is still working. Stop it first."); return; }
            var old = pane.Chat; var c = Create();
            if (old != null)
            {
                c.Agents.Clear(); c.Agents.AddRange(old.Agents); c.Mode = old.Mode; c.Web = old.Web;
                foreach (var kv in old.Models) c.Models[kv.Key] = kv.Value;
                foreach (var kv in old.Efforts) c.Efforts[kv.Key] = kv.Value;
                foreach (var kv in old.Accounts) c.Accounts[kv.Key] = kv.Value;
            }
            pane.SetChat(c); pane.FocusInput(); FillChats();
        }

        // Hidden context for every message: the page and what is open on it, and how to act in the app.
        string AddContext(string text)
        {
            var ctx = main.PageContext();
            var lines = new List<string> { "You are the All in One assistant. You can act in the app with the all-in-one MCP tools (page_context, open_page, search_all, notes/tasks/prompts/clips tools, markdown_open, explorer_open, terminal_open, browser_open, browser_read, browser_run_js). Do the work instead of explaining how; ask before deleting or overwriting.", "Current page: " + Convert.ToString(ctx["page"]) };
            foreach (var kv in ctx) if (kv.Key != "page") lines.Add(kv.Key + ": " + Convert.ToString(kv.Value));
            string note; object v;
            if (ctx.TryGetValue("note", out v) && (note = Convert.ToString(v)).Length > 0 && pane.Chat.Folder != Path.GetDirectoryName(note) && !pane.Chat.HasContent) pane.Chat.Folder = Path.GetDirectoryName(note);
            return "⟦" + string.Join("\n", lines) + "⟧\n\n" + text;
        }

        void Order(string text)
        {
            if (pane == null) return;
            if (pane.Busy) { App.I.Status("The assistant is still working."); return; }
            pane.SendText(text);
        }

        string Last() { return pane == null ? "" : ChatView.ShownText(pane.LastAnswer ?? "").Trim(); }

        protected override void Dispose(bool disposing) { if (disposing) AgentStore.Changed -= OnChanged; base.Dispose(disposing); }
    }
}
