using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Team page: rooms and people on the left, the conversation in the same chat view as the Agents page,
    // and the Update center (versions, PCs, releases, Build & Publish) one click away.
    // ===================================================================================================
    class TeamPage : Panel
    {
        readonly TeamClient c = TeamNet.Me;
        Panel setup, main, chatArea, updates;
        ListBox roomList, peopleList;
        Label title, subtitle, statusLine, replyLabel;
        Panel replyBar;
        TextBox input, search;
        Button sendBtn, reportBtn, pinnedBtn, agentsBtn, centerBtn, helpBtn, adminBtn, attachBtn;
        ChatView view;
        readonly AgentChat shown = new AgentChat { Id = "team-view" };
        readonly Dictionary<AgentMsg, TeamView> map = new Dictionary<AgentMsg, TeamView>();
        readonly Dictionary<string, List<AgentMsg>> localInfo = new Dictionary<string, List<AgentMsg>>();
        string room = "general", lastKey = "";
        TeamMsg replyTo;
        readonly ToolTip tips = new ToolTip();
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer { Interval = 400 };
        int dirty = 1;

        public TeamPage()
        {
            Dock = DockStyle.Fill; BackColor = Ui.Bg;
            BuildSetup(); BuildMain();
            Controls.Add(main); Controls.Add(setup);
            refresh.Tick += delegate
            {
                if (TeamNet.SetupInProgress || !Visible) return;
                if (!setup.Enabled) setup.Enabled = true;
                if (Interlocked.Exchange(ref dirty, 0) != 0) RefreshAll();
            };
            refresh.Start();
            c.Changed += MarkDirty;
            Releases.StateChanged += MarkDirty;
            if (TeamNet.SetupInProgress) { main.Visible = false; setup.Enabled = false; } else ShowRightView();
        }
        void MarkDirty() { Interlocked.Exchange(ref dirty, 1); }
        protected override void Dispose(bool disposing) { if (disposing) { c.Changed -= MarkDirty; Releases.StateChanged -= MarkDirty; refresh.Dispose(); } base.Dispose(disposing); }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            c.ActiveRoom = !TeamNet.SetupInProgress && Visible && main != null && main.Visible ? room : "";
            if (Visible) { MarkDirty(); c.Wake.Set(); }
        }

        Panel waiting; Label waitingText;
        void ShowRightView(string membershipStatus = null)
        {
            bool ok = c.Configured;
            string status = membershipStatus ?? (ok && c.Reachable ? c.MyStatus : "approved");
            bool approved = status == "approved";
            setup.Visible = !ok; main.Visible = ok && approved;
            if (waiting == null)
            {
                waiting = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(30)) };
                waitingText = new Label { Dock = DockStyle.Top, Height = Ui.S(200), Font = Ui.Big, UseMnemonic = false };
                var leave = Ui.Btn("Leave this team folder", delegate { c.Stop(); TeamNet.Started = false; c.Leave(); ShowRightView(); }); leave.Dock = DockStyle.Top;
                waiting.Controls.Add(leave); waiting.Controls.Add(waitingText);
                Controls.Add(waiting); waiting.BringToFront(); Ui.Theme(waiting);
            }
            waiting.Visible = ok && !approved;
            if (ok && !approved)
                waitingText.Text = status == "pending" ? "Waiting for the admin to approve this PC.\n\nYour device code: " + c.DeviceCode + "\nTell the admin this code so they can check it is really you." :
                    status == "suspended" ? "Your team access is paused by the admin." : status == "blocked" ? "This PC is blocked from the team by the admin." : "Your team access was removed by the admin.";
            if (ok) { c.ActiveRoom = Visible && approved ? room : ""; TeamNet.StartIfConfigured(); MarkDirty(); }
        }
        public static string AskText(IWin32Window owner, string caption, string prompt, string value)
        {
            using (var f = new Form { Text = caption, Width = Ui.S(520), Height = Ui.S(240), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false })
            {
                var l = new Label { Text = prompt, Dock = DockStyle.Top, Height = Ui.S(44), Padding = new Padding(Ui.S(8), Ui.S(6), 0, 0) };
                var t = new TextBox { Dock = DockStyle.Fill, Multiline = true, Text = value ?? "", ScrollBars = ScrollBars.Vertical };
                var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(44), FlowDirection = FlowDirection.RightToLeft };
                var ok = Ui.Btn("OK", null, true); ok.DialogResult = DialogResult.OK; var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                row.Controls.Add(ok); row.Controls.Add(cancel);
                f.Controls.Add(t); f.Controls.Add(row); f.Controls.Add(l); f.CancelButton = cancel;
                Ui.Theme(f);
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }

        // ---------- setup: join or create a team ----------
        void BuildSetup()
        {
            setup = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(30)) };
            var t = new Label { Text = "Team", Font = Ui.Big, Dock = DockStyle.Top, Height = Ui.S(44) };
            var info = new Label
            {
                Dock = DockStyle.Top, Height = Ui.S(58), ForeColor = Ui.Muted,
                Text = "Join with the shared folder your admin gave you. Create a team if you will manage it."
            };
            var folderRow = new Panel { Dock = DockStyle.Top, Height = Ui.S(36) };
            var folder = new TextBox { Dock = DockStyle.Fill, Text = string.IsNullOrEmpty(c.Local.Share) ? @"S:\Public_Folder\AllInOneTeam" : c.Local.Share };
            var browse = Ui.Btn("Browse...", null); browse.Dock = DockStyle.Right;
            browse.Click += delegate { using (var d = new FolderBrowserDialog { SelectedPath = !DriveCache.IsSlowPath(folder.Text) && Directory.Exists(folder.Text) ? folder.Text : "" }) if (d.ShowDialog(FindForm()) == DialogResult.OK) folder.Text = d.SelectedPath; };
            folderRow.Controls.Add(folder); folderRow.Controls.Add(browse);
            var nameRow = new Panel { Dock = DockStyle.Top, Height = Ui.S(36) };
            var name = new TextBox { Dock = DockStyle.Fill, Text = c.Local.Name };
            nameRow.Controls.Add(name); nameRow.Controls.Add(new Label { Text = "Your name", Dock = DockStyle.Left, Width = Ui.S(110), TextAlign = ContentAlignment.MiddleLeft });
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Ui.S(48), Padding = new Padding(0, Ui.S(8), 0, 0) };
            var result = new Label { Dock = DockStyle.Top, Height = Ui.S(60), ForeColor = Ui.Muted };
            Func<string, Action<string, string>, System.Threading.Tasks.Task> attempt = async (progress, joinOrCreate) =>
            {
                if (TeamNet.SetupInProgress) return;
                string displayName = name.Text.Trim();
                if (displayName.Length == 0) { result.ForeColor = Ui.Danger; result.Text = "Type your name first."; return; }
                string path = folder.Text.Trim();
                TeamNet.SetupInProgress = true;
                folderRow.Enabled = nameRow.Enabled = buttons.Enabled = false;
                result.ForeColor = Ui.Muted;
                result.Text = progress;
                string status = null;
                try
                {
                    status = await System.Threading.Tasks.Task.Run(() =>
                    {
                        joinOrCreate(path, displayName);
                        return c.Reachable ? c.MyStatus : "approved";
                    });
                }
                catch (Exception ex)
                {
                    if (!IsDisposed) { result.ForeColor = Ui.Danger; result.Text = ex.Message; }
                }
                finally
                {
                    TeamNet.SetupInProgress = false;
                    if (!IsDisposed) folderRow.Enabled = nameRow.Enabled = buttons.Enabled = true;
                }
                if (status != null && !IsDisposed) ShowRightView(status);
            };
            buttons.Controls.Add(Ui.Btn("Join team", async delegate { await attempt("Sending join request…", c.Join); }, true));
            buttons.Controls.Add(Ui.Btn("Create team", async delegate
            {
                if (TeamNet.SetupInProgress) return;
                if (MessageBox.Show(FindForm(), "Create a new team in\n" + folder.Text + "\n\nThis creates the team files there and makes this PC the admin that approves people and publishes app updates.", "Team", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                string p1 = AdminConsole.PromptSecret(FindForm(), "Admin passphrase", "Choose the admin passphrase (8+ characters). It is asked before admin actions; it is not stored anywhere in clear.");
                if (p1 == null) return;
                string p2 = AdminConsole.PromptSecret(FindForm(), "Admin passphrase", "Type it again");
                if (p1 != p2) { result.ForeColor = Ui.Danger; result.Text = "The two passphrases differ."; return; }
                await attempt("Creating the team…", (path, displayName) =>
                {
                    c.Local.Name = displayName;
                    c.SaveLocal();
                    Directory.CreateDirectory(path);
                    c.Create(path, "All in One team", p1);
                });
            }));

            var testFolder = Ui.Btn("Test folder", null);
            folder.TextChanged += delegate { result.Text = ""; };
            testFolder.Click += async delegate
            {
                if (TeamNet.SetupInProgress) return;
                string path = folder.Text.Trim();
                testFolder.Enabled = false;
                result.ForeColor = Ui.Muted;
                result.Text = "Checking the folder…";
                try
                {
                    bool? team = await System.Threading.Tasks.Task.Run<bool?>(() =>
                        Directory.Exists(path) ? (bool?)File.Exists(Path.Combine(path, "team.json")) : null);
                    if (IsDisposed || TeamNet.SetupInProgress || folder.Text.Trim() != path) return;
                    result.ForeColor = team.HasValue ? Ui.Text : Ui.Danger;
                    result.Text = team == true ? "Team found. Choose Join team." :
                        team == false ? "No team here yet. An admin can create one." : "Folder is missing or unreachable.";
                }
                catch (Exception ex)
                {
                    if (!IsDisposed && !TeamNet.SetupInProgress && folder.Text.Trim() == path) { result.ForeColor = Ui.Danger; result.Text = ex.Message; }
                }
                finally { if (!IsDisposed) testFolder.Enabled = true; }
            };
            buttons.Controls.Add(testFolder);
            setup.Controls.Add(result); setup.Controls.Add(buttons); setup.Controls.Add(nameRow); setup.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(8) });
            setup.Controls.Add(folderRow); setup.Controls.Add(new Label { Text = "Team folder", Dock = DockStyle.Top, Height = Ui.S(24) });
            setup.Controls.Add(info); setup.Controls.Add(t);
        }

        // ---------- main: sidebar + chat / update center ----------
        void BuildMain()
        {
            main = new Panel { Dock = DockStyle.Fill };
            var side = new Panel { Dock = DockStyle.Left, Width = PaneGrip.Size("team.side", DockStyle.Left, Ui.S(250), Ui.S(2400)), Padding = new Padding(0, Ui.S(6), Ui.S(6), 0) };
            var roomsHead = new Panel { Dock = DockStyle.Top, Height = Ui.S(30) };
            roomsHead.Controls.Add(new Label { Text = "Rooms", Font = Ui.Bold, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
            var add = Ui.Btn("New room", delegate { NewRoomMenu(); }); add.Dock = DockStyle.Right; tips.SetToolTip(add, "New group or project room");
            roomsHead.Controls.Add(add);
            roomList = new ListBox { Dock = DockStyle.Top, Height = Ui.S(260), BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(30), IntegralHeight = false };
            roomList.DrawItem += DrawRoom;
            roomList.SelectedIndexChanged += delegate { var r = roomList.SelectedItem as TeamRoom; if (r != null && r.Id != room) Open(r.Id); };
            var peopleHead = new Label { Text = "People", Font = Ui.Bold, Dock = DockStyle.Top, Height = Ui.S(30), TextAlign = ContentAlignment.MiddleLeft };
            peopleList = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(40), IntegralHeight = false };
            peopleList.DrawItem += DrawPerson;
            peopleList.DoubleClick += delegate { var d = peopleList.SelectedItem as TeamDevice; if (d != null && d.Id != c.Me) try { Open(c.DirectWith(d).Id); } catch (Exception ex) { App.I.Status(ex.Message); } };
            peopleList.MouseUp += (s, e) => { if (e.Button != MouseButtons.Right) return; int i = peopleList.IndexFromPoint(e.Location); if (i < 0) return; peopleList.SelectedIndex = i; PersonMenu((TeamDevice)peopleList.Items[i]); };
            tips.SetToolTip(peopleList, "Double-click a person for a private conversation");
            side.Controls.Add(peopleList); side.Controls.Add(peopleHead); side.Controls.Add(roomList); side.Controls.Add(roomsHead);

            chatArea = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(6), Ui.S(4), 0, 0) };
            var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(52) };
            title = new Label { Font = Ui.Big, Dock = DockStyle.Top, Height = Ui.S(30), AutoEllipsis = true, UseMnemonic = false };
            subtitle = new Label { Font = Ui.Small, ForeColor = Ui.Muted, Dock = DockStyle.Top, Height = Ui.S(20), AutoEllipsis = true, UseMnemonic = false };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, Ui.S(8), 0, 0) };
            search = new TextBox { Width = Ui.S(170) }; Ui.Cue(search, "Search all rooms");
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Search(search.Text); } };
            pinnedBtn = Ui.Btn("Pinned", delegate { PinnedMenu(); });
            agentsBtn = Ui.Btn("My agents", delegate { AgentsMenu(); });
            centerBtn = Ui.Btn("Update center", delegate { ToggleCenter(); });
            helpBtn = Ui.Btn("Get remote help", delegate { HelpMenu(); });
            adminBtn = Ui.Btn("Admin console", delegate { if (AdminConsole.EnsureUnlocked(c, FindForm())) new AdminConsole(c).Show(FindForm()); }, true);
            var more = Ui.Btn("...", delegate { RoomMenu(); });
            tools.Controls.Add(search); tools.Controls.Add(pinnedBtn); tools.Controls.Add(agentsBtn); tools.Controls.Add(helpBtn); tools.Controls.Add(centerBtn); tools.Controls.Add(adminBtn); tools.Controls.Add(more);
            var titles = new Panel { Dock = DockStyle.Fill }; titles.Controls.Add(subtitle); titles.Controls.Add(title);
            head.Controls.Add(titles); head.Controls.Add(tools);

            view = new ChatView { Dock = DockStyle.Fill, EmptyText = "No messages yet. Say hello!" };
            view.CustomMenu = MessageMenu;
            view.SetChat(shown);

            replyBar = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(28), Visible = false };
            replyLabel = new Label { Dock = DockStyle.Fill, ForeColor = Ui.Accent, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
            var cancelReply = Ui.Btn("✕", delegate { SetReply(null); }); cancelReply.Dock = DockStyle.Right; cancelReply.Width = Ui.S(30); cancelReply.AutoSize = false;
            replyBar.Controls.Add(replyLabel); replyBar.Controls.Add(cancelReply);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(78), Padding = new Padding(0, Ui.S(6), 0, 0) };
            input = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
            Ui.Cue(input, "Message (Enter sends, Shift+Enter new line, @name mentions, /help admin commands)");
            input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendInput(); } };
            input.TextChanged += delegate { if (input.Text.Length > 0) ThreadPool.QueueUserWorkItem(delegate { try { c.SetTyping(room); } catch { } }); };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Ui.S(250), FlowDirection = FlowDirection.LeftToRight };
            sendBtn = Ui.Btn("Send", delegate { SendInput(); }, true);
            attachBtn = Ui.Btn("Files", delegate { PickFiles(); }); tips.SetToolTip(attachBtn, "Send files (or drag them here)");
            reportBtn = Ui.Btn("Report problem", delegate { ReportProblem(); });
            buttons.Controls.Add(sendBtn); buttons.Controls.Add(attachBtn); buttons.Controls.Add(reportBtn);
            bottom.Controls.Add(input); bottom.Controls.Add(buttons);
            statusLine = new Label { Dock = DockStyle.Bottom, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted, AutoEllipsis = true, UseMnemonic = false };

            chatArea.Controls.Add(view); chatArea.Controls.Add(replyBar); chatArea.Controls.Add(statusLine); chatArea.Controls.Add(bottom); chatArea.Controls.Add(head);
            BuildUpdates();
            main.Controls.Add(chatArea); main.Controls.Add(updates); main.Controls.Add(side);
            PaneGrip.Attach(side, "team.side", Ui.S(170));

            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) SendFiles(files); };
            foreach (Control x in new Control[] { view, input, chatArea }) { x.AllowDrop = true; x.DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }; x.DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) SendFiles(files); }; }
        }

        // ---------- refresh ----------
        void RefreshAll()
        {
            if (!c.Configured) { ShowRightView(); return; }
            bool reach = c.Reachable;
            string st = reach ? c.MyStatus : "approved";
            if ((st == "approved") != main.Visible || (waiting != null && waiting.Visible && st == "approved")) ShowRightView();
            if (st != "approved") return;
            // Users see only what the policy allows them (the same rules are enforced again by every receiving PC).
            adminBtn.Visible = c.IsAdmin;
            attachBtn.Visible = c.Can("files", "*");
            agentsBtn.Visible = c.Can("agents", "*");
            var pol = c.Policy;
            helpBtn.Visible = pol != null && pol.Members.Any(m => m.Id != c.Me && TeamSecurity.Allowed(pol, m.Id, "screen.view", c.Me));
            var rooms = reach ? c.Rooms() : new List<TeamRoom>();
            if (rooms.Count > 0 && !rooms.Any(r => r.Id == room)) room = rooms[0].Id;
            SyncList(roomList, rooms.Cast<object>().ToList(), x => ((TeamRoom)x).Id, roomList.Items.Cast<object>().FirstOrDefault(x => ((TeamRoom)x).Id == room));
            if (roomList.SelectedItem == null || ((TeamRoom)roomList.SelectedItem).Id != room) { int i = rooms.FindIndex(r => r.Id == room); if (i >= 0 && i < roomList.Items.Count) roomList.SelectedIndex = i; }
            roomList.Invalidate();
            var devs = reach ? c.Devices() : new List<TeamDevice>();
            SyncList(peopleList, devs.Cast<object>().ToList(), x => ((TeamDevice)x).Id, null);
            peopleList.Invalidate();
            if (updates.Visible) RefreshUpdates();
            else RenderRoom(reach);
        }
        static void SyncList(ListBox l, List<object> items, Func<object, string> key, object select)
        {
            var now = l.Items.Cast<object>().Select(key).ToList();
            if (now.SequenceEqual(items.Select(key)))
            {
                for (int i = 0; i < items.Count; i++) l.Items[i] = items[i];   // fresh data, same order
                return;
            }
            string sel = l.SelectedItem != null ? key(l.SelectedItem) : null;
            l.BeginUpdate(); l.Items.Clear(); foreach (var x in items) l.Items.Add(x); l.EndUpdate();
            int idx = items.FindIndex(x => key(x) == sel); if (idx >= 0) l.SelectedIndex = idx;
        }

        void Open(string id)
        {
            room = id; lastKey = ""; SetReply(null);
            c.ActiveRoom = room; c.Wake.Set();
            if (updates.Visible) ToggleCenter();
            MarkDirty(); RefreshAll();
            input.Focus();
        }

        void RenderRoom(bool reach)
        {
            var r = c.Room(room);
            title.Text = r != null ? (r.Kind == "dm" ? "@ " : "# ") + c.RoomTitle(r) : "Team";
            var devs = c.Devices();
            int online = devs.Count(TeamClient.Online);
            subtitle.Text = !reach ? "Team folder not reachable - messages wait in your outbox and are sent when it is back. (" + c.Root + ")" :
                (r != null && r.Kind == "project" ? "Project: " + r.Project + "  ·  " : "") + online + " of " + devs.Count + " online  ·  my agents here: " + TeamAgents.Mode(c, room) + "  ·  " + c.Root;
            reportBtn.Visible = room == "updates";
            if (!reach && r == null) { view.EmptyText = "The team folder is not reachable: " + c.Root; }
            var rows = reach ? c.View(room) : new List<TeamView>();
            List<AgentMsg> infos; localInfo.TryGetValue(room, out infos);
            string key = rows.Count + "|" + string.Join(",", rows.Select(v => v.M.Id.Substring(0, Math.Min(6, v.M.Id.Length)) + (v.Pending ? "p" : "") + v.Reactions.Count + (v.Edited ? "e" : "") + (v.Deleted ? "d" : "") + (v.Pinned ? "P" : "") + v.Reactions.Sum(x => x.Value.Count))) + "|" + (infos != null ? infos.Count : 0);
            pinnedBtn.Text = "Pinned (" + rows.Count(v => v.Pinned) + ")";
            if (key != lastKey)
            {
                lastKey = key;
                map.Clear();
                var list = new List<AgentMsg>();
                foreach (var v in rows) { var m = ToAgentMsg(v); map[m] = v; list.Add(m); }
                if (infos != null) { list.AddRange(infos); list = list.OrderBy(m => m.Time).ToList(); }
                shown.Messages = list;
                view.SetChat(shown);
                if (Visible && reach) ThreadPool.QueueUserWorkItem(delegate { try { c.MarkRead(room); } catch { } });
            }
            // typing, working agents and read receipts
            var typing = devs.Where(d => d.Id != c.Me && TeamClient.Online(d) && d.TypingRoom == room && TeamClient.NowMs() - d.TypingTs < 8000).Select(d => d.Working.Length > 0 ? d.Working : d.Name + " is typing").ToList();
            string seen = "";
            var mine = rows.LastOrDefault(v => v.M.From == c.Me);
            if (mine != null && reach)
            {
                var reads = c.Reads(room);
                var who = devs.Where(d => d.Id != c.Me && reads.Any(x => x.Key == SafeKey(d.Id) && x.Value >= mine.M.Ts)).Select(d => d.Name).ToList();
                if (who.Count > 0) seen = "Seen by " + string.Join(", ", who);
            }
            string transfer = transferText ?? "";
            statusLine.Text = string.Join("   ·   ", new[] { string.Join(", ", typing) + (typing.Count > 0 ? "..." : ""), seen, transfer, c.Local.Outbox.Count > 0 ? c.Local.Outbox.Count + " message(s) waiting to send" : "" }.Where(x => x.Length > 0));
        }
        static string SafeKey(string id) { var sb = new System.Text.StringBuilder(); foreach (char ch in id) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? char.ToLowerInvariant(ch) : '-'); return sb.ToString().Trim('-'); }

        AgentMsg ToAgentMsg(TeamView v)
        {
            var m = v.M;
            var sb = new System.Text.StringBuilder();
            if (v.Pinned) sb.Append("📌 ");
            if (v.Reply != null) sb.Append("> **").Append(v.Reply.Name).Append(":** ").Append(Ui.OneLine(v.Reply.Kind == "file" ? "[file] " + v.Reply.FileName : v.Reply.Text, 90)).Append("\n\n");
            if (v.Deleted) sb.Append("_(message deleted)_");
            else if (m.Kind == "file") sb.Append("📎 **").Append(m.FileName).Append("**  (").Append(Size(m.Size)).Append(")  ·  right-click > Download");
            else if (m.Kind == "bug") sb.Append("🐞 **Problem report**").Append(m.Version.Length > 0 ? " (version " + m.Version + ")" : "").Append("\n\n").Append(v.Text);
            else if (m.Kind == "release") sb.Append("🚀 ").Append(v.Text);
            else sb.Append(v.Text);
            if (v.Edited && !v.Deleted) sb.Append("  _(edited)_");
            if (v.Reactions.Count > 0) sb.Append("\n\n").Append(string.Join("   ", v.Reactions.Select(x => x.Key + " " + x.Value.Count)));
            if (v.Pending) sb.Append("\n\n_(waiting to send...)_");
            bool me = m.From == c.Me && string.IsNullOrEmpty(m.Agent);
            return new AgentMsg
            {
                Role = me ? "user" : "agent", Agent = string.IsNullOrEmpty(m.Agent) ? "" : AgentIdOf(m.Agent),
                Label = m.Kind == "release" ? "Release · " + m.Name : m.Name, Model = string.IsNullOrEmpty(m.Agent) ? "" : "AI agent",
                Text = sb.ToString(), Time = TeamClient.FromMs(m.Ts).Ticks
            };
        }
        static string AgentIdOf(string handle) { int i = handle.LastIndexOf('-'); return i >= 0 ? handle.Substring(i + 1) : handle; }
        public static string Size(long b) { return b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.0") + " GB" : b >= 1 << 20 ? (b / (double)(1 << 20)).ToString("0.0") + " MB" : (b / 1024.0).ToString("0") + " KB"; }

        // ---------- sidebar drawing ----------
        void DrawRoom(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var r = (TeamRoom)roomList.Items[e.Index];
            bool sel = r.Id == room;
            using (var b = new SolidBrush(sel ? Ui.SelBg : roomList.BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
            string icon = r.Kind == "updates" ? "🚀" : r.Kind == "dm" ? "@" : r.Kind == "project" ? "📁" : "#";
            int unread = 0; try { unread = c.Unread(r.Id); } catch { }
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, icon, Ui.Base, new Rectangle(e.Bounds.X + Ui.S(6), e.Bounds.Y, Ui.S(24), e.Bounds.Height), Ui.Muted, flags);
            TextRenderer.DrawText(e.Graphics, c.RoomTitle(r), unread > 0 ? Ui.Bold : Ui.Base, new Rectangle(e.Bounds.X + Ui.S(30), e.Bounds.Y, e.Bounds.Width - Ui.S(70), e.Bounds.Height), Ui.Text, flags);
            if (unread > 0)
            {
                var badge = new Rectangle(e.Bounds.Right - Ui.S(36), e.Bounds.Y + Ui.S(6), Ui.S(28), e.Bounds.Height - Ui.S(12));
                using (var p = Ui.Round(badge, badge.Height / 2)) using (var b = new SolidBrush(Ui.Accent)) e.Graphics.FillPath(b, p);
                TextRenderer.DrawText(e.Graphics, unread > 99 ? "99+" : unread.ToString(), Ui.Small, badge, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        void DrawPerson(object s, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var d = (TeamDevice)peopleList.Items[e.Index];
            using (var b = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Ui.SelBg : peopleList.BackColor)) e.Graphics.FillRectangle(b, e.Bounds);
            bool on = TeamClient.Online(d);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(on ? Color.FromArgb(34, 197, 94) : Color.FromArgb(150, 150, 150))) e.Graphics.FillEllipse(b, e.Bounds.X + Ui.S(8), e.Bounds.Y + Ui.S(9), Ui.S(9), Ui.S(9));
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics, d.Name + (d.Id == c.Me ? " (you)" : "") + (d.Admin ? "  · admin" : ""), Ui.Base, new Rectangle(e.Bounds.X + Ui.S(24), e.Bounds.Y + Ui.S(3), e.Bounds.Width - Ui.S(28), Ui.S(18)), Ui.Text, flags);
            var parts = new List<string>();
            if (!on) parts.Add("last seen " + TeamClient.FromMs(d.LastSeen).ToString("dd MMM HH:mm"));
            if (d.Version.Length > 0) parts.Add("v" + d.Version);
            if (d.Agents.Count > 0) parts.Add(string.Join(" ", d.Agents.Select(a => "@" + a.Handle)));
            string sub = d.Working.Length > 0 ? d.Working : string.Join("  ·  ", parts);
            TextRenderer.DrawText(e.Graphics, sub, Ui.Small, new Rectangle(e.Bounds.X + Ui.S(24), e.Bounds.Y + Ui.S(20), e.Bounds.Width - Ui.S(28), Ui.S(16)), Ui.Muted, flags);
        }

        // ---------- sending ----------
        void SendInput()
        {
            string text = input.Text.Trim();
            if (text.Length == 0) return;
            if (!c.Configured) return;
            // Admin / status commands answer here (only you see the answer).
            if (text.StartsWith("/") || room == "updates")
            {
                string answer = null;
                try { answer = TeamAdmin.Handle(c, text, q => MessageBox.Show(FindForm(), q, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes); }
                catch (Exception ex) { answer = "Command failed: " + ex.Message; }
                if (answer != null) { Info(answer); input.Clear(); return; }
            }
            var reply = replyTo;
            var m = c.Post(room, "text", text, x => { if (reply != null) x.ReplyTo = reply.Id; });
            TeamAgents.Consider(m);   // my own message may call my own agent
            input.Clear(); SetReply(null);
            MarkDirty();
        }
        void Info(string text)
        {
            List<AgentMsg> l; if (!localInfo.TryGetValue(room, out l)) localInfo[room] = l = new List<AgentMsg>();
            l.Add(new AgentMsg { Role = "info", Text = text, Time = DateTime.Now.Ticks });
            lastKey = ""; MarkDirty();
        }
        void SetReply(TeamMsg m)
        {
            replyTo = m; replyBar.Visible = m != null;
            if (m != null) { replyLabel.Text = "Replying to " + m.Name + ": " + Ui.OneLine(m.Kind == "file" ? m.FileName : m.Text, 100); input.Focus(); }
        }

        string transferText;
        void PickFiles() { using (var d = new OpenFileDialog { Multiselect = true, Title = "Send files to the room" }) if (d.ShowDialog(FindForm()) == DialogResult.OK) SendFiles(d.FileNames); }
        void SendFiles(string[] files)
        {
            var list = files.Where(File.Exists).ToList();
            if (list.Count == 0) { App.I.Status("Folders cannot be sent; zip them first."); return; }
            string target = room;
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var f in list)
                {
                    try { c.SendFile(target, f, (d, t) => { transferText = "Sending " + Path.GetFileName(f) + "  " + (t > 0 ? d * 100 / t : 0) + "%"; MarkDirty(); }, null); }
                    catch (Exception ex) { transferText = "Not sent: " + Path.GetFileName(f) + " - " + ex.Message + " (it resumes automatically)"; MarkDirty(); continue; }
                }
                transferText = null; MarkDirty();
            });
        }
        void Download(TeamMsg m, bool open)
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "All in One Team");
            Directory.CreateDirectory(folder);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string path = c.Download(m, folder, (d, t) => { transferText = "Downloading " + m.FileName + "  " + (t > 0 ? d * 100 / t : 0) + "%"; MarkDirty(); }, null);
                    transferText = "Saved " + path; MarkDirty();
                    if (open) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\"");
                }
                catch (Exception ex) { transferText = "Download failed: " + ex.Message; MarkDirty(); }
            });
        }
        void ReportProblem()
        {
            string text = Ask("Report a problem", "What went wrong? (what you did, what you expected, what happened)", "");
            if (string.IsNullOrWhiteSpace(text)) return;
            c.Post("updates", "bug", text.Trim(), x => x.Version = Releases.MyVersion);
            App.I.Status("Problem reported in Updates & Releases.");
        }

        // ---------- menus ----------
        void MessageMenu(AgentMsg am, ContextMenuStrip menu)
        {
            TeamView v; if (am == null || !map.TryGetValue(am, out v)) return;
            var m = v.M; bool mine = m.From == c.Me && string.IsNullOrEmpty(m.Agent);
            menu.Items.Add("Reply", null, delegate { SetReply(m); });
            var react = new ToolStripMenuItem("React");
            foreach (var em in new[] { "👍", "❤️", "😂", "🎉", "👀", "✅", "❗" }) { string e = em; react.DropDownItems.Add(e, null, delegate { c.Post(room, "reaction", e, x => x.Target = m.Id); }); }
            menu.Items.Add(react);
            if (m.Kind == "file")
            {
                menu.Items.Add("Download", null, delegate { Download(m, false); });
                menu.Items.Add("Download and show in folder", null, delegate { Download(m, true); });
            }
            menu.Items.Add("Copy text", null, delegate { App.I.CopyText(m.Kind == "file" ? m.FileName : v.Text); });
            menu.Items.Add(v.Pinned ? "Unpin" : "Pin", null, delegate { c.Post(room, v.Pinned ? "unpin" : "pin", "", x => x.Target = m.Id); });
            if (mine && !v.Deleted && m.Kind == "text")
                menu.Items.Add("Edit...", null, delegate { string t = Ask("Edit message", "New text", v.Text); if (t != null && t.Trim().Length > 0) c.Post(room, "edit", t.Trim(), x => x.Target = m.Id); });
            if (mine && !v.Deleted) menu.Items.Add("Delete", null, delegate { if (MessageBox.Show(FindForm(), "Delete this message for everyone?", "Team", MessageBoxButtons.YesNo) == DialogResult.Yes) c.Post(room, "delete", "", x => x.Target = m.Id); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Turn into a task", null, delegate
            {
                var t = Store.NewTask(Ui.OneLine((m.Kind == "bug" ? "Bug: " : "") + v.Text, 100));
                t.Notes = v.Text + "\n\nFrom " + m.Name + " in team room " + c.RoomTitle(c.Room(room)) + (m.Version.Length > 0 ? " (version " + m.Version + ")" : "") + "\nteam-bug:" + m.Id;
                Store.TouchTask(t);
                c.Post(room, "text", "Tracked as a task: " + t.Title, x => x.ReplyTo = m.Id);
            });
            if (c.Local.MyAgents.Count > 0)
            {
                var ask = new ToolStripMenuItem("Ask my agent about this");
                foreach (var a in c.Local.MyAgents) { string h = c.Handle(a); ask.DropDownItems.Add("@" + h, null, delegate { input.Text = "@" + h + " "; SetReply(m); input.SelectionStart = input.Text.Length; }); }
                menu.Items.Add(ask);
            }
        }
        // Right-click a person: message, view or control their screen (they approve), share my screen with them.
        void PersonMenu(TeamDevice d)
        {
            if (d.Id == c.Me) return;
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            if (c.Can("dm", d.Id)) menu.Items.Add("Private message", null, delegate { try { Open(c.DirectWith(d).Id); } catch (Exception ex) { App.I.Status(ex.Message); } });
            if (c.Can("screen.view", d.Id)) menu.Items.Add("Ask to view their screen...", null, delegate { AskRemote(d, "view"); });
            if (c.Can("screen.control", d.Id)) menu.Items.Add("Ask to control their screen...", null, delegate { AskRemote(d, "control"); });
            var pol = c.Policy;
            if (TeamSecurity.Allowed(pol, d.Id, "screen.view", c.Me)) menu.Items.Add("Share my screen with " + d.Name + " (view only)", null, delegate { ShareWith(d, "view"); });
            if (TeamSecurity.Allowed(pol, d.Id, "screen.control", c.Me)) menu.Items.Add("Let " + d.Name + " control my screen", null, delegate { ShareWith(d, "control"); });
            if (menu.Items.Count == 0) menu.Items.Add("No actions allowed with " + d.Name).Enabled = false;
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(Cursor.Position);
        }
        void AskRemote(TeamDevice d, string mode)
        {
            string note = AskText(FindForm(), "Remote assistance", "Why do you need " + (mode == "control" ? "control of" : "to see") + " " + d.Name + "'s screen? (shown to them)", "");
            if (note == null) return;
            try { var r = TeamRemote.Ask(c, d.Id, mode, note); TeamRemoteUi.OpenViewer(r); }
            catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Remote assistance"); }
        }
        void ShareWith(TeamDevice d, string mode)
        {
            if (MessageBox.Show(FindForm(), "Share your screen with " + d.Name + (mode == "control" ? " and let them use your mouse and keyboard" : " (view only)") + "?\n\nA red frame shows while it runs; press Stop at any time.", "Remote assistance", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { TeamRemote.Offer(c, d.Id, mode); }
            catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Remote assistance"); }
        }
        // One click for users: pick who helps (only people the admin allowed to help you are listed).
        void HelpMenu()
        {
            var pol = c.Policy; if (pol == null) return;
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            var devs = c.Devices();
            foreach (var m in pol.Members.Where(x => x.Id != c.Me && x.Status == "approved" && TeamSecurity.Allowed(pol, x.Id, "screen.view", c.Me)))
            {
                var mm = m; var d = devs.FirstOrDefault(x => x.Id == m.Id);
                bool on = d != null && TeamClient.Online(d);
                string mode = TeamSecurity.Allowed(pol, m.Id, "screen.control", c.Me) ? "control" : "view";
                var item = menu.Items.Add(m.Name + (on ? "" : " (offline)") + (mode == "control" ? "  -  can help with mouse and keyboard" : "  -  can see your screen"), null, delegate { ShareWith(d ?? new TeamDevice { Id = mm.Id, Name = mm.Name }, mode); });
                item.Enabled = on;
            }
            if (menu.Items.Count == 0) menu.Items.Add("Nobody is set up to help you yet. Ask the admin.").Enabled = false;
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(helpBtn, new Point(0, helpBtn.Height));
        }

        void NewRoomMenu()
        {
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            menu.Items.Add("New group room...", null, delegate { string n = Ask("New room", "Room name", ""); if (!string.IsNullOrWhiteSpace(n)) Open(c.NewGroup(n, "").Id); });
            var proj = new ToolStripMenuItem("New project room");
            foreach (var p in Projects.All().Where(x => c.Can("project", "project:" + x.Path)).Take(40)) { var pp = p; proj.DropDownItems.Add(p.Name + "   " + p.Path, null, delegate { Open(c.NewGroup(pp.Name, pp.Path).Id); }); }
            proj.DropDownItems.Add(new ToolStripSeparator());
            proj.DropDownItems.Add("Choose a folder...", null, delegate { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(FindForm()) == DialogResult.OK) Open(c.NewGroup(Path.GetFileName(d.SelectedPath), d.SelectedPath).Id); });
            menu.Items.Add(proj);
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(Cursor.Position);
        }
        void PinnedMenu()
        {
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            foreach (var v in c.View(room).Where(x => x.Pinned)) { var vv = v; menu.Items.Add(vv.M.Name + ": " + Ui.OneLine(vv.M.Kind == "file" ? vv.M.FileName : vv.Text, 80), null, delegate { view.FindMessage(vv.Text.Length > 20 ? vv.Text.Substring(0, 20) : vv.Text, 0); }); }
            if (menu.Items.Count == 0) menu.Items.Add("No pinned messages. Right-click a message > Pin.").Enabled = false;
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(Cursor.Position);
        }
        void AgentsMenu()
        {
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            menu.Items.Add("Agents I share with the team (others call them with @handle):").Enabled = false;
            foreach (var d in AgentStore.Defs)
            {
                string id = d.Id;
                var item = new ToolStripMenuItem(d.Name + "   @" + c.Handle(id)) { Checked = c.Local.MyAgents.Contains(id) };
                item.Click += delegate { if (!c.Local.MyAgents.Remove(id)) c.Local.MyAgents.Add(id); c.SaveLocal(); c.Heartbeat(true); MarkDirty(); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("In this room my agents may:").Enabled = false;
            foreach (var mode in new[] { new[] { "off", "Not answer" }, new[] { "ask", "Answer only (read-only, default)" }, new[] { "edit", "Answer and change files in the room's project" } })
            {
                string k = mode[0];
                var item = new ToolStripMenuItem(mode[1]) { Checked = TeamAgents.Mode(c, room) == k };
                item.Click += delegate { c.Local.AgentMode[room] = k; c.SaveLocal(); MarkDirty(); };
                menu.Items.Add(item);
            }
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(Cursor.Position);
        }
        void RoomMenu()
        {
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            menu.Items.Add("Copy room as text", null, delegate { App.I.CopyText(string.Join("\n\n", c.View(room).Select(v => "[" + TeamClient.FromMs(v.M.Ts).ToString("dd MMM HH:mm") + "] " + v.M.Name + ": " + (v.M.Kind == "file" ? "[file] " + v.M.FileName : v.Text)))); });
            menu.Items.Add("Open team folder", null, delegate { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + c.Root + "\""); } catch { } });
            menu.Items.Add("Change my name...", null, delegate { string n = Ask("Your name", "Name shown to the team", c.Local.Name); if (!string.IsNullOrWhiteSpace(n)) { c.Local.Name = n.Trim(); c.SaveLocal(); c.Heartbeat(true); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Leave this team folder (keeps it on disk)", null, delegate { if (MessageBox.Show(FindForm(), "Disconnect this PC from the team folder?", "Team", MessageBoxButtons.YesNo) == DialogResult.Yes) { c.Stop(); TeamNet.Started = false; c.Leave(); ShowRightView(); } });
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(Cursor.Position);
        }
        void Search(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return;
            var hits = c.Search(q);
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            foreach (var h in hits.Take(30)) { var hh = h; menu.Items.Add(c.RoomTitle(hh.Key) + "  ·  " + hh.Value.Name + ": " + Ui.OneLine(hh.Value.Kind == "file" ? hh.Value.FileName : hh.Value.Text, 70), null, delegate { Open(hh.Key.Id); view.FindMessage(q, 0); }); }
            if (menu.Items.Count == 0) menu.Items.Add("No messages match \"" + q + "\"").Enabled = false;
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(search, new Point(0, search.Height));
        }

        string Ask(string caption, string prompt, string value)
        {
            using (var f = new Form { Text = caption, Width = Ui.S(520), Height = Ui.S(260), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false })
            {
                var l = new Label { Text = prompt, Dock = DockStyle.Top, Height = Ui.S(28), Padding = new Padding(Ui.S(8), Ui.S(6), 0, 0) };
                var t = new TextBox { Dock = DockStyle.Fill, Multiline = true, Text = value ?? "", ScrollBars = ScrollBars.Vertical };
                var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(44), FlowDirection = FlowDirection.RightToLeft };
                var ok = Ui.Btn("OK", null, true); ok.DialogResult = DialogResult.OK; var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                row.Controls.Add(ok); row.Controls.Add(cancel);
                f.Controls.Add(t); f.Controls.Add(row); f.Controls.Add(l); f.AcceptButton = null; f.CancelButton = cancel;
                Ui.Theme(f);
                return f.ShowDialog(FindForm()) == DialogResult.OK ? t.Text : null;
            }
        }

        // ---------- Update center ----------
        Label upSummary; ListView devList, relList; TextBox relNotes; FlowLayoutPanel adminBar; Button installBtn;
        void BuildUpdates()
        {
            updates = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(Ui.S(8), Ui.S(4), 0, 0) };
            var head = new Panel { Dock = DockStyle.Top, Height = Ui.S(40) };
            head.Controls.Add(new Label { Text = "Update center", Font = Ui.Big, Dock = DockStyle.Fill });
            var back = Ui.Btn("Back to chat", delegate { ToggleCenter(); }); back.Dock = DockStyle.Right;
            head.Controls.Add(back);
            upSummary = new Label { Dock = DockStyle.Top, Height = Ui.S(64), UseMnemonic = false };
            var userBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Ui.S(42) };
            userBar.Controls.Add(Ui.Btn("Check for updates now", delegate { ThreadPool.QueueUserWorkItem(delegate { try { Releases.Check(c, Application.ExecutablePath, Releases.UpdatesDir, Install.IsDevCopy); } catch (Exception ex) { App.Log("Check: " + ex.Message); } MarkDirty(); }); }));
            installBtn = Ui.Btn("Restart and update now", delegate { if (!Releases.InstallNow()) App.I.Status("No update is ready yet."); }, true);
            userBar.Controls.Add(installBtn);
            adminBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Ui.S(42) };
            adminBar.Controls.Add(Ui.Btn("Build && Publish...", delegate { BuildPublishDialog(); }, true));
            adminBar.Controls.Add(Ui.Btn("Promote pilot to stable...", delegate { AdminDo("/promote " + (Ask("Promote", "Percent of PCs that get it now (1-100)", "100") ?? "")); }));
            adminBar.Controls.Add(Ui.Btn("Pilot PCs...", delegate { PilotPcs(); }));
            adminBar.Controls.Add(Ui.Btn("Pause", delegate { AdminDo("/pause"); }));
            adminBar.Controls.Add(Ui.Btn("Resume", delegate { AdminDo("/resume"); }));
            adminBar.Controls.Add(Ui.Btn("Rollback pilot", delegate { AdminDo("/rollback pilot"); }));
            adminBar.Controls.Add(Ui.Btn("Rollback stable", delegate { AdminDo("/rollback stable"); }));
            devList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, MultiSelect = true };
            foreach (var col in new[] { "PC", "Person", "Online", "Version", "Channel", "State", "Progress", "Problem", "Last seen" }) devList.Columns.Add(col, Ui.S(col == "Problem" ? 260 : col == "Version" || col == "Person" ? 140 : 100));
            var lower = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(220) };
            relList = new ListView { Dock = DockStyle.Left, Width = Ui.S(520), View = View.Details, FullRowSelect = true, BorderStyle = BorderStyle.None, MultiSelect = false };
            foreach (var col in new[] { "Version", "Channel", "Published", "By", "Critical" }) relList.Columns.Add(col, Ui.S(col == "Version" || col == "Published" ? 140 : 80));
            relNotes = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            relList.SelectedIndexChanged += delegate { var m = relList.SelectedItems.Count > 0 ? relList.SelectedItems[0].Tag as ReleaseManifest : null; relNotes.Text = m == null ? "" : (m.Notes ?? "").Replace("\n", "\r\n") + "\r\n\r\nSHA-256 " + m.Sha + "\r\nCommit " + m.Commit + "   ·   " + TeamPage.Size(m.Size); };
            lower.Controls.Add(relNotes); lower.Controls.Add(relList);
            var relHead = new Label { Text = "Release history", Font = Ui.Bold, Dock = DockStyle.Bottom, Height = Ui.S(28), TextAlign = ContentAlignment.BottomLeft };
            updates.Controls.Add(devList); updates.Controls.Add(relHead); updates.Controls.Add(lower);
            updates.Controls.Add(new Label { Text = "Computers", Font = Ui.Bold, Dock = DockStyle.Top, Height = Ui.S(26), TextAlign = ContentAlignment.BottomLeft });
            updates.Controls.Add(adminBar); updates.Controls.Add(userBar); updates.Controls.Add(upSummary); updates.Controls.Add(head);
        }
        void ToggleCenter()
        {
            bool show = !updates.Visible;
            updates.Visible = show; chatArea.Visible = !show;
            centerBtn.Text = show ? "Chat" : "Update center";
            MarkDirty(); RefreshAll();
        }
        void AdminDo(string cmd)
        {
            if (!AdminConsole.EnsureUnlocked(c, FindForm())) return;
            string answer;
            try { answer = TeamAdmin.Handle(c, cmd, q => MessageBox.Show(FindForm(), q, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes); }
            catch (Exception ex) { answer = ex.Message; }
            if (answer != null) MessageBox.Show(FindForm(), answer, "Update center");
            MarkDirty();
        }
        void RefreshUpdates()
        {
            string err; var ch = c.Reachable ? Releases.Channels(c, out err) : null;
            bool admin = c.IsAdmin;
            adminBar.Visible = admin;
            installBtn.Enabled = Releases.Ready;
            upSummary.Text = "This PC: " + (Releases.MyVersion.Length > 0 ? Releases.MyVersion : (c.Local.InstalledVersion.Length > 0 ? c.Local.InstalledVersion : "not from a release")) + "   ·   channel " + Releases.MyChannel(c, ch) + "   ·   " + Releases.State +
                (Releases.State == "downloading" ? " " + Releases.Progress + "%" : "") + (Releases.Error.Length > 0 ? "  -  " + Releases.Error : "") +
                (ch != null ? "\nLatest: dev " + Or(ch.Dev) + "   ·   pilot " + Or(ch.Pilot) + " (" + ch.PilotDevices.Count + " PCs)   ·   stable " + Or(ch.Stable) + " (" + ch.Rollout + "% of PCs)" + (ch.Paused ? "   ·   DEPLOYMENT PAUSED" : "") : "\nRelease channels: " + (c.Reachable ? "not readable" : "team folder not reachable")) +
                (admin ? "\nYou are the admin: Build & Publish sends a tested build to dev or pilot; promote it to stable when the pilot PCs are fine." : "");
            var devs = c.Devices(true);
            devList.BeginUpdate(); devList.Items.Clear();
            foreach (var d in devs)
            {
                bool pilot = ch != null && ch.PilotDevices.Contains(d.Id);
                var it = new ListViewItem(new[] { d.Machine, d.Name, TeamClient.Online(d) ? "online" : "offline", d.Version.Length > 0 ? d.Version : "-", d.Channel + (pilot && d.Channel != "pilot" ? " (pilot)" : ""), d.State, d.State == "downloading" ? d.Progress + "%" : "", d.Error, TeamClient.FromMs(d.LastSeen).ToString("dd MMM HH:mm") }) { Tag = d };
                if (d.State == "failed") it.ForeColor = Ui.Danger;
                devList.Items.Add(it);
            }
            devList.EndUpdate();
            string selected = relList.SelectedItems.Count > 0 ? ((ReleaseManifest)relList.SelectedItems[0].Tag).Version : null;
            relList.BeginUpdate(); relList.Items.Clear();
            foreach (var m in Releases.History(c))
            {
                string where = ch == null ? "" : string.Join("+", new[] { m.Version == ch.Dev ? "dev" : null, m.Version == ch.Pilot ? "pilot" : null, m.Version == ch.Stable ? "stable" : null }.Where(x => x != null));
                var it = new ListViewItem(new[] { m.Version, where, TeamClient.FromMs(m.Published).ToString("dd MMM yyyy HH:mm"), m.Publisher, m.Forced ? "yes" : "" }) { Tag = m };
                relList.Items.Add(it);
                if (m.Version == selected) it.Selected = true;
            }
            relList.EndUpdate();
        }
        static string Or(string v) { return string.IsNullOrEmpty(v) ? "-" : v; }

        void PilotPcs()
        {
            if (!AdminConsole.EnsureUnlocked(c, FindForm())) return;
            var sel = devList.SelectedItems.Cast<ListViewItem>().Select(i => (TeamDevice)i.Tag).ToList();
            if (sel.Count == 0) { MessageBox.Show(FindForm(), "Select PCs in the Computers list first, then click Pilot PCs to add or remove them.", "Pilot PCs"); return; }
            try
            {
                string err; var ch = Releases.Channels(c, out err);
                bool allIn = ch != null && sel.All(d => ch.PilotDevices.Contains(d.Id));
                if (MessageBox.Show(FindForm(), (allIn ? "Remove from" : "Add to") + " the pilot group:\n" + string.Join("\n", sel.Select(d => d.Name + " (" + d.Machine + ")")), "Pilot PCs", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                Releases.ChangeChannels(c, x => { foreach (var d in sel) { x.PilotDevices.Remove(d.Id); if (!allIn) x.PilotDevices.Add(d.Id); } }, "pilot devices");
            }
            catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Pilot PCs"); }
            MarkDirty();
        }

        void BuildPublishDialog()
        {
            if (!AdminConsole.EnsureUnlocked(c, FindForm())) return;
            string source = Install.IsDevCopy ? Path.GetDirectoryName(Application.ExecutablePath) : "";
            using (var f = new Form { Text = "Build & Publish", Width = Ui.S(760), Height = Ui.S(620), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false })
            {
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(12)) };
                var srcBox = new TextBox { Dock = DockStyle.Top, Text = source };
                var chan = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList }; chan.Items.AddRange(new object[] { "pilot", "dev" }); chan.SelectedIndex = 0;
                var notes = new TextBox { Dock = DockStyle.Top, Height = Ui.S(170), Multiline = true, ScrollBars = ScrollBars.Vertical };
                try { if (source.Length > 0) notes.Text = TeamPublish.DefaultNotes(c, source).Replace("\n", "\r\n"); } catch { }
                var full = new CheckBox { Text = "Also run the full test suite (slow)", Dock = DockStyle.Top, Height = Ui.S(26) };
                var forced = new CheckBox { Text = "Critical update (installs at once, even while paused)", Dock = DockStyle.Top, Height = Ui.S(26) };
                var log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
                var go = Ui.Btn("Build, test and publish", null, true); go.Dock = DockStyle.Bottom;
                Action<string> say = s => { try { f.BeginInvoke(new Action(() => log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s.Replace("\n", "\r\n") + "\r\n"))); } catch { } };
                go.Click += delegate
                {
                    go.Enabled = false;
                    string s = srcBox.Text.Trim(), ch = (string)chan.SelectedItem, n = notes.Text.Replace("\r\n", "\n").Trim(); bool ft = full.Checked, fc = forced.Checked;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { TeamPublish.BuildAndPublish(c, s, ch, n, ft, fc, say); say("Done. PCs on " + ch + " update within a few minutes; watch the Computers list."); }
                        catch (Exception ex) { say("STOPPED: " + ex.Message); }
                        try { f.BeginInvoke(new Action(() => go.Enabled = true)); } catch { }
                        MarkDirty();
                    });
                };
                pad.Controls.Add(log); pad.Controls.Add(go);
                pad.Controls.Add(new Label { Text = "Progress", Dock = DockStyle.Top, Height = Ui.S(22) });
                pad.Controls.Add(forced); pad.Controls.Add(full); pad.Controls.Add(notes);
                pad.Controls.Add(new Label { Text = "What's new (shown to everyone)", Dock = DockStyle.Top, Height = Ui.S(22) });
                pad.Controls.Add(chan); pad.Controls.Add(new Label { Text = "Channel (stable is reached by promoting a tested pilot release)", Dock = DockStyle.Top, Height = Ui.S(22) });
                pad.Controls.Add(srcBox); pad.Controls.Add(new Label { Text = "Source folder (with build.ps1)", Dock = DockStyle.Top, Height = Ui.S(22) });
                f.Controls.Add(pad);
                Ui.Theme(f);
                f.ShowDialog(FindForm());
            }
        }
    }
}
