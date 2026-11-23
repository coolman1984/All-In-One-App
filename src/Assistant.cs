using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // The Assistant: standalone autonomous companion docked on the right of every page (Ctrl+J or top-bar button).
    // Uses AssistantHarness for isolated storage (data\assistant\chats), open-automation companion identity,
    // 20-page live awareness, Solo / Council / Leader Team switching, and live activity narration.
    class AssistantPanel : Panel
    {
        readonly MainForm main;
        readonly ComboBox chats;
        readonly FlowLayoutPanel modeBar;
        readonly Button btnSolo, btnCouncil, btnLeader, btnTeam, btnWorkspace, btnRec;
        readonly Label statusBar;
        readonly FlowLayoutPanel orders;
        readonly Panel host;
        readonly ToolTip tips = new ToolTip();
        ChatPane pane;
        bool filling;
        string lastPage = "";
        const string Collection = AssistantHarness.Collection;

        // page key -> (button text, order). "*" = every page.
        static readonly string[][] Orders =
        {
            new[] { "*", "Daily brief", "Give me a short daily brief: open and overdue tasks (tasks_search), what is due today, my newest sticky notes and anything urgent. End with the 3 things I should do first." },
            new[] { "*", "Plan my day", "Look at my open tasks (tasks_search) and plan my day in time blocks. Then ask me before creating anything." },
            new[] { "*", "Last copy -> task", "Read my latest clipboard item (clips_search, limit 1) and turn it into a well-written task (task_create) with a due date if it mentions one." },
            new[] { "*", "Find anything", "Search everything in the app (search_all) for what I describe next and show the best matches with how to open them. What should I look for?" },
            new[] { "documents", "List open docs", "Check documents_list and summarize what files are open right now, their edit/preview mode, and which one is active." },
            new[] { "documents", "Summarize doc", "Inspect the active document on the Documents page (documents_list / page_context) and give me a concise executive summary with key numbers and actions." },
            new[] { "documents", "Save active doc", "Save the active edited document on the Documents page (document_save) and confirm the result." },
            new[] { "markdown", "Summarize note", "Summarize the open note: a short paragraph and 3-6 bullets." },
            new[] { "markdown", "Note -> tasks", "Extract every action item from the open note and create each one as a task in the app (task_create) with owner/date in the notes when given. List what you created." },
            new[] { "markdown", "Improve note", "Rewrite the open note so it is clearer and better organized (headings, bullets, tables where useful). Same meaning and language." },
            new[] { "markdown", "Link notes", "Find other notes in this vault related to the open note and suggest [[links]] to add, with one line why each." },
            new[] { "markdown", "Translate note", "Translate the open note (Arabic <-> English), keep the Markdown, and save it as a new note next to it with ' (translated)' in the name." },
            new[] { "markdown", "Flashcards", "Make 10 question/answer flashcards from the open note as a Markdown table." },
            new[] { "browser", "Summarize page", "Read the browser tab (browser_read) and summarize it: key points, numbers, dates." },
            new[] { "browser", "Page -> note", "Read the browser tab (browser_read) and save a clean summary with the source link as a new sticky note (note_create)." },
            new[] { "browser", "Extract tables", "Read the browser tab (browser_run_js to collect the tables) and give me every table as Markdown." },
            new[] { "tasks", "Break down task", "Take my most urgent open task (tasks_search) and break it into clear steps (task_update add_steps)." },
            new[] { "tasks", "Overdue review", "List my overdue tasks (tasks_search due_before today) and propose new dates." },
            new[] { "tasks", "Weekly review", "Write my weekly review from finished and open tasks: done, still open, problems, next week." },
            new[] { "notes", "Organize notes", "Look at my sticky notes (notes_search) and organize them with clean tags and colors." },
            new[] { "notes", "Notes -> tasks", "Find to-dos inside my sticky notes and create them as tasks (task_create). List what you created." },
            new[] { "prompts", "Improve prompts", "Review my 5 most used prompts (prompts_search) and suggest a better version of each." },
            new[] { "prompts", "New prompt", "Help me write a new reusable prompt with {{blanks}} for a task I will describe, then save it (prompt_save). What is the task?" },
            new[] { "clips", "Clean last copy", "Take my latest clipboard text (clips_search limit 1), clean it (spacing, typos, structure) and put the result on the clipboard (clipboard_copy)." },
            new[] { "explorer", "Explain folder", "Explain what is in the folder I have open in Explorer (page_context, then read the files): purpose, main files, how to run it." },
            new[] { "explorer", "Write README", "Write a README.md for the folder I have open in Explorer, based on its files." },
            new[] { "explorer", "Find big files", "In the folder I have open in Explorer, find the biggest files and folders and tell me what is safe to clean." },
            new[] { "terminals", "How to run", "For my most recent project folder, tell me how to build and run it, as commands I can paste." },
            new[] { "dashboard", "What needs me", "What needs my attention now? Check tasks, reminders and agent limits (agent_limits), short answer." },
        };

        public ChatPane Pane { get { return pane; } }
        public string StatusText { get { return statusBar != null ? statusBar.Text : ""; } }

        public AssistantPanel(MainForm main)
        {
            this.main = main;
            Padding = new Padding(Ui.S(8), Ui.S(10), Ui.S(10), Ui.S(6)); BackColor = Ui.Bg;

            var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(36), Padding = new Padding(0, Ui.S(3), 0, Ui.S(3)) };
            var title = new Label { Text = "Assistant", Font = Ui.Bold, Dock = DockStyle.Left, Width = Ui.S(86), TextAlign = ContentAlignment.MiddleLeft };
            chats = MainForm.Combo(160); chats.Dock = DockStyle.Fill;
            chats.SelectedIndexChanged += delegate
            {
                if (filling) return;
                var c = chats.SelectedItem as ChatItem;
                if (c == null || pane == null || pane.Chat == c.Chat) return;
                if (pane.Busy)
                {
                    FillChats();
                    if (App.I != null) App.I.Status("The assistant is still working. Stop it first.");
                    return;
                }
                pane.SetChat(c.Chat);
                UpdateModeBar();
            };
            var fresh = Ui.Btn("New", delegate { NewChat(); }); fresh.Dock = DockStyle.Right;
            var close = Ui.Btn("✕", delegate { main.SetAssistant(false); }); ((FluentButton)close).Glyph = null; close.Dock = DockStyle.Right; close.Width = Ui.S(36); close.AutoSize = false;
            close.AccessibleName = "Hide the Assistant";
            tips.SetToolTip(close, "Hide (Ctrl+J)");
            // Orders fold away (remembered), like a VS Code section.
            var fold = new IconButton(OrdersOpen ? "\uE70D" : "\uE76C", 8f, "Show or hide the order buttons", tips, null) { Dock = DockStyle.Right, Width = Ui.S(30) };
            fold.Click += delegate { OrdersOpen = !OrdersOpen; fold.Text = OrdersOpen ? "\uE70D" : "\uE76C"; orders.Visible = OrdersOpen; };
            // Expand to the whole window / restore to the side (also: double-click the divider, drag it to any width).
            expand = new IconButton("", 9f, "Expand to full window / back to side panel (Ctrl+Shift+J)", tips, null) { Dock = DockStyle.Right, Width = Ui.S(30) };
            expand.Click += delegate { main.SetAssistantWide(!main.AssistantWide); };
            head.Controls.Add(chats); head.Controls.Add(Gap()); head.Controls.Add(fold); head.Controls.Add(expand); head.Controls.Add(fresh); head.Controls.Add(Gap()); head.Controls.Add(close); head.Controls.Add(title);

            // Chat modes and workflow controls.
            modeBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, Ui.S(2), 0, Ui.S(2)), Margin = new Padding(0) };
            btnSolo = ModeBtn("Solo", "Single autonomous companion", delegate { SetMode("solo"); });
            btnCouncil = ModeBtn("Council", "Model council: multiple models discuss and write a verdict", delegate { SetMode("council"); });
            btnLeader = ModeBtn("Leader", "Leader team: leader assigns tasks to workers and synthesizes results", delegate { SetMode("leader"); });
            btnTeam = ModeBtn("Team…", "Configure council / leader team roster and roles", delegate { if (pane != null) { pane.EditCouncil(); UpdateModeBar(); } });
            btnWorkspace = ModeBtn("Workspace", "Open Leader Team workspace", delegate { if (pane != null) pane.OpenLeaderTeam(); });
            btnWorkspace.Visible = false;
            btnRec = ModeBtn("Record", "Record the steps of a workflow", delegate { ToggleRecording(); });
            modeBar.Controls.Add(btnSolo);
            modeBar.Controls.Add(btnCouncil);
            modeBar.Controls.Add(btnLeader);
            modeBar.Controls.Add(btnTeam);
            modeBar.Controls.Add(btnWorkspace);
            modeBar.Controls.Add(btnRec);
            modeBar.Controls.Add(ModeBtn("Build workflow", "Turn the last recording into a reusable workflow", delegate { BuildLastRecording(); }));

            // Live Companion Status bar: shows current page awareness and updates live on AssistantHarness.Activity.
            statusBar = new Label
            {
                Dock = DockStyle.Top,
                Height = Ui.S(22),
                Font = Ui.Small,
                ForeColor = Ui.Muted,
                AutoEllipsis = true,
                UseMnemonic = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(Ui.S(2), 0, Ui.S(2), 0),
                Text = "Seeing: Home"
            };

            orders = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(0, Ui.S(4), 0, Ui.S(4)), Visible = OrdersOpen };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, Ui.S(4), 0, 0) };
            bottom.Controls.Add(Small("Copy answer", delegate { string a = Last(); if (a.Length > 0 && App.I != null) App.I.CopyText(a); }));
            bottom.Controls.Add(Small("Save to note", delegate { string a = Last(); if (a.Length > 0) main.InsertIntoNote(a); }));
            bottom.Controls.Add(Small("Create task", delegate { string a = Last(); if (a.Length > 0) { var t = Store.NewTask(Ui.OneLine(a, 100)); t.Notes = a; Store.TouchTask(t); if (App.I != null) App.I.Status("Task created."); } }));

            host = new Panel { Dock = DockStyle.Fill };
            host.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Loading...", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted });
            Controls.Add(host); Controls.Add(bottom); Controls.Add(orders); Controls.Add(statusBar); Controls.Add(modeBar); Controls.Add(head);
            Ui.Theme(this);
            AssistantHarness.Activity += OnActivity;
            WorkflowRecorder.StateChanged += OnRecorderState;
            PageChanged(main.Page);
            if (AgentStore.Loaded) Start(); else AgentStore.LoadAll(() => { if (!IsDisposed) Start(); });
        }

        static bool OrdersOpen
        {
            get { string v; return !Store.Settings.ViewStacks.TryGetValue("assistant.orders", out v) || v != "0"; }
            set { Store.Settings.ViewStacks["assistant.orders"] = value ? "1" : "0"; Store.MarkSettings(); }
        }
        IconButton expand;
        public void WideChanged(bool wide)
        {
            if (expand == null) return;
            expand.Text = wide ? "" : "";
            expand.AccessibleName = wide ? "Restore the Assistant side panel" : "Expand the Assistant";
            tips.SetToolTip(expand, expand.AccessibleName + " (Ctrl+Shift+J)");
        }
        static Panel Gap() { return new Panel { Dock = DockStyle.Right, Width = Ui.S(4) }; }
        static Button Small(string text, EventHandler click) { var b = Ui.Btn(text, click); ((FluentButton)b).Glyph = null; b.Margin = new Padding(0, 0, Ui.S(4), Ui.S(2)); return b; }

        Button ModeBtn(string text, string tip, EventHandler click)
        {
            var b = Ui.Btn(text, click);
            ((FluentButton)b).Glyph = null;
            b.Font = Ui.Small;
            b.MinimumSize = Ui.S(0, 26);
            b.Padding = new Padding(Ui.S(8), Ui.S(2), Ui.S(8), Ui.S(2));
            b.Margin = new Padding(0, 0, Ui.S(4), Ui.S(2));
            tips.SetToolTip(b, tip);
            return b;
        }

        class ChatItem
        {
            public AgentChat Chat;
            public override string ToString() { string t = string.IsNullOrWhiteSpace(Chat.Title) ? "New chat" : Chat.Title; return t + "   ·   " + Ui.Ago(Chat.Updated); }
        }

        // Backward-compatible forwarder to AssistantHarness.Dirs().
        public static List<string> Dirs() { return AssistantHarness.Dirs(); }

        public void FocusInput() { if (pane != null) pane.FocusInput(); }

        public void SetMode(string mode)
        {
            if (pane == null) return;
            pane.SetAssistantMode(mode);
            UpdateModeBar();
        }

        void UpdateModeBar()
        {
            if (IsDisposed || btnSolo == null) return;
            var c = pane != null ? pane.Chat : null;
            bool isLeader = c != null && c.Council && c.LeaderTeam;
            bool isCouncil = c != null && c.Council && !c.LeaderTeam;
            bool isSolo = !isCouncil && !isLeader;

            StyleModeBtn(btnSolo, isSolo);
            StyleModeBtn(btnCouncil, isCouncil);
            StyleModeBtn(btnLeader, isLeader);
            btnTeam.Enabled = pane != null;
            btnWorkspace.Visible = isLeader;
            btnWorkspace.Enabled = isLeader && pane != null;
            UpdateRecButton();
        }

        void UpdateRecButton()
        {
            if (IsDisposed || btnRec == null) return;
            bool rec = WorkflowRecorder.IsRecording;
            int steps = WorkflowRecorder.CurrentStepCount;
            btnRec.Text = rec ? ("Stop (" + steps + ")") : "Record";
            var fb = btnRec as FluentButton;
            if (fb != null) fb.Primary = rec;
            btnRec.BackColor = rec ? Color.FromArgb(217, 72, 34) : Ui.SelBg;
            btnRec.ForeColor = rec ? Color.White : Ui.Text;
            btnRec.Invalidate();
        }

        void OnRecorderState()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)OnRecorderState); } catch { }
                return;
            }
            UpdateRecButton();
            if (WorkflowRecorder.IsRecording && statusBar != null)
            {
                statusBar.ForeColor = Color.FromArgb(217, 72, 34);
                statusBar.Text = "Recording workflow · " + WorkflowRecorder.CurrentStepCount + " steps";
            }
        }

        public void ToggleRecording()
        {
            bool stopping = WorkflowRecorder.IsRecording;
            try
            {
                var result = stopping ? WorkflowRecorder.Stop() : WorkflowRecorder.Start(
                    "Workflow on " + (string.IsNullOrEmpty(main.Page) ? "desktop" : main.Page), "Recorded from Assistant");
                if (true.Equals(result["ok"]))
                {
                    if (stopping)
                    {
                        statusBar.ForeColor = Ui.Muted;
                        statusBar.Text = "Recording saved. Choose Build workflow to use it.";
                    }
                }
                else ShowRecordingError(stopping, CtxStr(result, "error"));
            }
            catch (Exception ex) { ShowRecordingError(stopping, ex.Message); }
            finally { UpdateRecButton(); }
        }

        void ShowRecordingError(bool stopping, string error)
        {
            statusBar.ForeColor = Ui.Danger;
            statusBar.Text = (stopping ? "Could not save recording. " : "Could not start recording. ") + error;
            tips.SetToolTip(statusBar, statusBar.Text);
        }

        void BuildLastRecording()
        {
            if (pane == null || pane.Busy || WorkflowRecorder.IsRecording)
            {
                statusBar.Text = "Finish the current activity before building a workflow.";
                return;
            }

            var result = WorkflowRecorder.Get("latest");
            object state, count, id;
            if (!true.Equals(result["ok"]) || !result.TryGetValue("status", out state) || !Equals(state, "completed") ||
                !result.TryGetValue("stepCount", out count) || Convert.ToInt32(count, CultureInfo.InvariantCulture) == 0 ||
                !result.TryGetValue("id", out id))
            {
                statusBar.Text = "Record and save a workflow with at least one step first.";
                return;
            }

            string recording = Convert.ToString(id, CultureInfo.InvariantCulture);
            string prompt = "Build a reusable workflow from recording " + recording + ". " +
                "Use record_list with this recording ID. Generalize the captured steps using the appropriate native tools. " +
                "Save a named skill with skill_manage and an executable dynamic tool with tool_create. " +
                "Verify the tool with disposable inputs. Report the tool name, required parameters, and exact results you verified. " +
                "If a step cannot be verified or automated, report where it stopped. " +
                "Show how to schedule the verified tool using Scheduled jobs.";
            pane.Send(AddContext(prompt));
        }

        static void StyleModeBtn(Button b, bool active)
        {
            var fb = b as FluentButton;
            if (fb != null) fb.Primary = active;
            b.BackColor = active ? Ui.AccentFill : Ui.SelBg;
            b.ForeColor = active ? Color.White : Ui.Text;
            b.Invalidate();
        }

        void OnActivity(string action)
        {
            if (IsDisposed || string.IsNullOrWhiteSpace(action)) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(SetActivityText), action); } catch { }
                return;
            }
            SetActivityText(action);
        }

        void SetActivityText(string action)
        {
            if (IsDisposed || statusBar == null) return;
            statusBar.ForeColor = Ui.Accent;
            statusBar.Text = action;
        }

        public void UpdateStatusStrip()
        {
            if (IsDisposed || statusBar == null || main == null) return;
            statusBar.ForeColor = Ui.Muted;
            var context = main.PageContext();
            statusBar.Text = "Seeing: " + DescribeContext(context);
            tips.SetToolTip(statusBar, CtxStr(context, "page") == "documents" ? CtxStr(context, "documents_current") : "");
        }

        static string DescribeContext(Dictionary<string, object> ctx)
        {
            if (ctx == null) return "Home";
            string p = CtxStr(ctx, "page");
            string name = p.Length == 0 ? "Home" : Ui.PageName(p, char.ToUpper(p[0]) + p.Substring(1));
            string detail = "";
            if (p == "documents")
            {
                string doc = CtxStr(ctx, "documents_current");
                if (doc.Length > 0) detail = Path.GetFileName(doc) + " (" + CtxStr(ctx, "documents_mode") + ")";
                else detail = "no open document";
            }
            else if (p == "explorer")
            {
                string f = CtxStr(ctx, "explorer_folder");
                if (f.Length > 0) detail = f;
                var sel = ctx.ContainsKey("explorer_selected") ? ctx["explorer_selected"] as ICollection<string> : null;
                if (sel != null && sel.Count > 0) detail += (detail.Length > 0 ? " (" + sel.Count + " selected)" : sel.Count + " selected");
            }
            else if (p == "browser")
            {
                string bt = CtxStr(ctx, "browser_title");
                string bu = CtxStr(ctx, "browser_url");
                detail = bt.Length > 0 ? bt : bu;
            }
            else if (p == "markdown")
            {
                string n = CtxStr(ctx, "markdown_note");
                detail = n.Length > 0 ? Path.GetFileName(n) : CtxStr(ctx, "markdown_vault");
            }
            else if (p == "tasks" || p == "dashboard")
            {
                string open = CtxStr(ctx, "tasks_open");
                string due = CtxStr(ctx, "tasks_due_today");
                if (open.Length > 0) detail = open + " open tasks" + (due.Length > 0 && due != "0" ? ", " + due + " due today" : "");
            }
            else if (p == "notes")
            {
                string active = CtxStr(ctx, "notes_active");
                if (active.Length > 0) detail = active + " active notes";
            }
            else if (p == "prompts")
            {
                string cnt = CtxStr(ctx, "prompts_count");
                if (cnt.Length > 0) detail = cnt + " prompts";
            }
            else if (p == "clips")
            {
                string cnt = CtxStr(ctx, "clips_count");
                if (cnt.Length > 0) detail = cnt + " clips";
            }
            else if (p == "tagged")
            {
                string cnt = CtxStr(ctx, "tagged_count");
                if (cnt.Length > 0) detail = cnt + " tagged items";
            }
            else if (p == "activity")
            {
                string cnt = CtxStr(ctx, "activity_count");
                if (cnt.Length > 0) detail = cnt + " records";
            }
            else if (p == "system")
            {
                detail = CtxStr(ctx, "system_tab");
            }
            else if (p == "chrome")
            {
                detail = CtxStr(ctx, "chrome_tab");
            }
            return detail.Length > 0 ? name + " · " + detail : name;
        }

        static string CtxStr(Dictionary<string, object> ctx, string key)
        {
            object v;
            return ctx != null && ctx.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        // Orders for this page first, then the ones for every page; all orders + yours in the menu.
        public void PageChanged(string page)
        {
            page = page ?? "";
            UpdateStatusStrip();
            if (page == lastPage && orders.Controls.Count > 0) return;
            lastPage = page;
            orders.SuspendLayout(); Ui.ClearControls(orders.Controls);
            foreach (var o in Orders.Where(o => o[0] == page).Concat(Orders.Where(o => o[0] == "*")).Take(2))
            { var oo = o; var b = Small(oo[1], delegate { Order(oo[2]); }); tips.SetToolTip(b, oo[2]); orders.Controls.Add(b); }
            var all = Small("More actions ▾", null);
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
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)Start); } catch { } return; }
            var last = AgentStore.Chats.Where(c => AssistantHarness.IsAssistant(c)).OrderByDescending(c => c.Updated).FirstOrDefault();
            Ui.ClearControls(host.Controls);
            if (last != null && last.Mode != "auto") { last.Mode = "auto"; AgentStore.Save(last); }
            pane = new ChatPane(last ?? Create()) { Dock = DockStyle.Fill };
            pane.OutgoingFilter = AddContext;
            pane.NewChatRequested = NewChat;
            pane.Changed += delegate { UpdateModeBar(); };
            host.Controls.Add(pane);
            Ui.Theme(pane);
            AgentStore.Changed += OnChanged;
            FillChats();
            UpdateModeBar();
            UpdateStatusStrip();
        }

        void OnChanged() { if (IsDisposed) return; if (InvokeRequired) { try { BeginInvoke((Action)OnChanged); } catch { } return; } Ui.Debounce("assistant.chats", 400, FillChats); }

        void FillChats()
        {
            if (IsDisposed || pane == null) return;
            filling = true;
            chats.BeginUpdate(); chats.Items.Clear();
            foreach (var c in AgentStore.Chats.Where(c => AssistantHarness.IsAssistant(c) && (c.HasContent || c == pane.Chat)).OrderByDescending(c => c.Updated).Take(40))
                chats.Items.Add(new ChatItem { Chat = c });
            chats.EndUpdate();
            for (int i = 0; i < chats.Items.Count; i++) if (((ChatItem)chats.Items[i]).Chat == pane.Chat) chats.SelectedIndex = i;
            filling = false;
            UpdateModeBar();
        }

        AgentChat Create()
        {
            string agent = AgentStore.Def("claude") != null ? "claude" : (AgentStore.Defs.Select(d => d.Id).FirstOrDefault() ?? "claude");
            var c = AgentStore.New(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), new[] { agent });
            c.Collection = Collection; c.Mode = "auto";   // Full auto: autonomous companion inside closed corporate workstation
            return c;
        }

        void NewChat()
        {
            if (pane == null) return;
            if (pane.Busy) { if (App.I != null) App.I.Status("The assistant is still working. Stop it first."); return; }
            var old = pane.Chat; var c = Create();
            if (old != null)
            {
                c.Agents.Clear(); c.Agents.AddRange(old.Agents); c.Mode = "auto"; c.Web = old.Web;
                c.Council = old.Council; c.LeaderTeam = old.LeaderTeam; c.Rounds = old.Rounds; c.Chair = old.Chair;
                c.Members = old.Members == null ? new List<CouncilMember>() : old.Members.Select(m => new CouncilMember
                {
                    Agent = m.Agent, Model = m.Model, Effort = m.Effort, Role = m.Role, Off = m.Off, Key = m.Key, Account = m.Account
                }).ToList();
                foreach (var kv in old.Models) c.Models[kv.Key] = kv.Value;
                foreach (var kv in old.Efforts) c.Efforts[kv.Key] = kv.Value;
                foreach (var kv in old.Accounts) c.Accounts[kv.Key] = kv.Value;
            }
            pane.SetChat(c); pane.FocusInput(); FillChats(); UpdateModeBar();
        }

        // Hidden context for every message: companion identity, rules, and 20-page live context.
        string AddContext(string text)
        {
            var ctx = main.PageContext();
            if (pane != null && pane.Chat != null)
            {
                pane.Chat.Collection = Collection;
                pane.Chat.Mode = "auto";
                if (!pane.Chat.HasContent)
                {
                    string note = CtxStr(ctx, "markdown_note");
                    string doc = CtxStr(ctx, "documents_current");
                    string folder = CtxStr(ctx, "explorer_folder");
                    string page = CtxStr(ctx, "page");
                    string targetDir = page == "documents" && doc.Length > 0 ? Path.GetDirectoryName(doc)
                        : page == "markdown" && note.Length > 0 ? Path.GetDirectoryName(note)
                        : page == "explorer" ? folder : "";
                    if (!string.IsNullOrEmpty(targetDir) && pane.Chat.Folder != targetDir)
                        try { if (Directory.Exists(targetDir)) pane.Chat.Folder = targetDir; } catch { }
                }
            }
            return AssistantHarness.WrapMessage(pane != null ? pane.Chat : null, ctx, text);
        }

        void Order(string text)
        {
            if (pane == null) return;
            if (pane.Busy) { if (App.I != null) App.I.Status("The assistant is still working."); return; }
            pane.SendText(text);
        }

        string Last() { return pane == null ? "" : ChatView.ShownText(pane.LastAnswer ?? "").Trim(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                AgentStore.Changed -= OnChanged;
                AssistantHarness.Activity -= OnActivity;
                WorkflowRecorder.StateChanged -= OnRecorderState;
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
