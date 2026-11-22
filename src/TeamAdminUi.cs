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
    // Remote assistance UI: approval dialog, the always-visible red frame + banner with Stop, the viewer window.
    // ===================================================================================================
    static class TeamRemoteUi
    {
        static TeamClient c; static bool attached;
        public static void Attach(TeamClient client)
        {
            c = client; if (attached) return; attached = true;
            TeamRemote.RequestReceived += r => OnUi(() => AskApproval(r));
            TeamRemote.ShareOffered += r => OnUi(() =>
            {
                var m = TeamSecurity.Member(c.Policy, r.Sharer);
                if (MessageBox.Show(App.I.MainView, (m != null ? m.Name : r.Sharer) + " shares their screen with you (" + r.Mode + ").\n\nOpen it now?", "Remote assistance", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenViewer(r);
                else TeamRemote.End(c, r.Sid, "helper declined");
            });
            TeamRemote.HostStarted += h => OnUi(() => ShowIndicator(h));
        }
        static void OnUi(Action a) { try { App.I.Sync.BeginInvoke(a); } catch { } }

        // The person whose screen is asked for decides, every time.
        static void AskApproval(RemoteRequest r)
        {
            var viewer = TeamSecurity.Member(c.Policy, r.Viewer);
            string who = viewer != null ? viewer.Name + " (" + viewer.Machine + ")" : r.Viewer;
            using (var f = new Form { Text = "Remote assistance request", Width = Ui.S(520), Height = Ui.S(250), StartPosition = FormStartPosition.CenterScreen, TopMost = true, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false })
            {
                var l = new Label { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(14)), Font = Ui.Base, Text = who + " asks to " + (r.Mode == "control" ? "VIEW AND CONTROL" : "VIEW") + " your screen.\n\n" + (r.Note.Length > 0 ? "\"" + r.Note + "\"\n\n" : "") + "You see a red frame while it runs and can stop it at any time. It ends by itself after " + c.Policy.SessionMinutes + " minutes." };
                var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(48), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.S(8)) };
                string answer = "deny";
                var deny = Ui.Btn("Deny", delegate { answer = "deny"; f.Close(); });
                var view = Ui.Btn("Allow viewing only", delegate { answer = "view"; f.Close(); }, r.Mode != "control");
                row.Controls.Add(deny); row.Controls.Add(view);
                if (r.Mode == "control") row.Controls.Add(Ui.Btn("Allow control", delegate { answer = "control"; f.Close(); }, true));
                f.Controls.Add(l); f.Controls.Add(row); Ui.Theme(f);
                var timer = new System.Windows.Forms.Timer { Interval = 90000 }; timer.Tick += delegate { timer.Stop(); answer = "deny"; f.Close(); }; timer.Start();   // no answer = no
                f.ShowDialog(App.I.MainView);
                timer.Dispose();
                try { if (answer == "deny") TeamRemote.Deny(c, r); else TeamRemote.Approve(c, r, answer); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Remote assistance"); }
            }
        }

        // Red frame around all screens + banner with time left and Stop. Closes when the session ends.
        static void ShowIndicator(RemoteHost h)
        {
            var vs = SystemInformation.VirtualScreen; int t = Ui.S(4);
            var frames = new List<Form>();
            foreach (var r in new[] { new Rectangle(vs.Left, vs.Top, vs.Width, t), new Rectangle(vs.Left, vs.Bottom - t, vs.Width, t), new Rectangle(vs.Left, vs.Top, t, vs.Height), new Rectangle(vs.Right - t, vs.Top, t, vs.Height) })
            {
                var e = new EdgeForm { Bounds = r }; e.Show(); frames.Add(e);
            }
            var banner = new Form { FormBorderStyle = FormBorderStyle.None, TopMost = true, ShowInTaskbar = false, BackColor = Color.FromArgb(200, 30, 30), Width = Ui.S(560), Height = Ui.S(40), StartPosition = FormStartPosition.Manual };
            var scr = Screen.PrimaryScreen.Bounds; banner.Location = new Point(scr.Left + (scr.Width - banner.Width) / 2, scr.Top + Ui.S(6));
            var text = new Label { Dock = DockStyle.Fill, ForeColor = Color.White, Font = Ui.Bold, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.S(10), 0, 0, 0), UseMnemonic = false };
            var stop = new Button { Text = "Stop", Dock = DockStyle.Right, Width = Ui.S(90), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(200, 30, 30), Font = Ui.Bold };
            stop.Click += delegate { h.Stop("stopped by the person sharing"); };
            banner.Controls.Add(text); banner.Controls.Add(stop);
            var tick = new System.Windows.Forms.Timer { Interval = 1000 };
            Action close = () => { tick.Stop(); tick.Dispose(); foreach (var e in frames) e.Close(); banner.Close(); };
            tick.Tick += delegate
            {
                if (!h.Running) { close(); return; }
                long s = h.SecondsLeft;
                text.Text = "● " + h.ViewerName + (h.A.Mode == "control" ? " is CONTROLLING your screen" : " is viewing your screen") + "   ·   " + (s / 60) + ":" + (s % 60).ToString("00") + " left";
            };
            h.Stopped += x => OnUi(() => { if (!banner.IsDisposed) { close(); App.I.Balloon("Remote assistance", "Screen sharing ended: " + x.StopReason); } });
            banner.Show(); tick.Start();
        }
        class EdgeForm : Form
        {
            public EdgeForm() { FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false; BackColor = Color.FromArgb(220, 30, 30); StartPosition = FormStartPosition.Manual; }
            protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x20 | 0x80 | 0x08000000; return p; } }   // click-through, tool window, no activate
            protected override bool ShowWithoutActivation { get { return true; } }
        }

        // ---------- viewer ----------
        public static void OpenViewer(RemoteRequest r)
        {
            var v = new TeamRemote.Viewer(c, r);
            var f = new Form { Text = "Remote screen", Width = Ui.S(1200), Height = Ui.S(780), StartPosition = FormStartPosition.CenterScreen, KeyPreview = true, BackColor = Color.Black };
            var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            var bar = new Panel { Dock = DockStyle.Top, Height = Ui.S(36), BackColor = Ui.Card };
            var status = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Ui.S(8), 0, 0, 0), UseMnemonic = false, Text = "Waiting for approval on the other PC..." };
            var stop = Ui.Btn("Stop", delegate { v.Stop("stopped by the helper"); f.Close(); }, true); stop.Dock = DockStyle.Right;
            bar.Controls.Add(status); bar.Controls.Add(stop);
            f.Controls.Add(pic); f.Controls.Add(bar); Ui.Theme(bar);
            Size img = Size.Empty;
            Func<Point, PointF?> map = p =>
            {
                if (img.IsEmpty) return null;
                float scale = Math.Min((float)pic.Width / img.Width, (float)pic.Height / img.Height);
                float w = img.Width * scale, h = img.Height * scale, x0 = (pic.Width - w) / 2, y0 = (pic.Height - h) / 2;
                if (p.X < x0 || p.Y < y0 || p.X > x0 + w || p.Y > y0 + h) return null;
                return new PointF((p.X - x0) / w, (p.Y - y0) / h);
            };
            Action<string, MouseEventArgs> mouse = (kind, e) =>
            {
                var q = map(e.Location); if (q == null) return;
                int b = e.Button == MouseButtons.Right ? 2 : e.Button == MouseButtons.Middle ? 3 : 1;
                ThreadPool.QueueUserWorkItem(delegate { try { v.Send(new RemoteInput { Kind = kind, X = q.Value.X, Y = q.Value.Y, Button = b, Delta = e.Delta }); } catch { } });
            };
            long lastMove = 0;
            pic.MouseMove += (s, e) => { if (Environment.TickCount - lastMove < 60) return; lastMove = Environment.TickCount; mouse("move", e); };
            pic.MouseDown += (s, e) => mouse("down", e);
            pic.MouseUp += (s, e) => mouse("up", e);
            pic.MouseWheel += (s, e) => mouse("wheel", e);
            f.KeyDown += (s, e) => { e.Handled = true; e.SuppressKeyPress = true; int vk = (int)e.KeyCode; ThreadPool.QueueUserWorkItem(delegate { try { v.Send(new RemoteInput { Kind = "keydown", Vk = vk }); } catch { } }); };
            f.KeyUp += (s, e) => { e.Handled = true; int vk = (int)e.KeyCode; ThreadPool.QueueUserWorkItem(delegate { try { v.Send(new RemoteInput { Kind = "keyup", Vk = vk }); } catch { } }); };
            var timer = new System.Windows.Forms.Timer { Interval = 200 };
            bool busy = false;
            timer.Tick += delegate
            {
                if (busy) return; busy = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    byte[] jpg = null; bool ready = false, ended = false;
                    try { ready = v.Ready(); ended = v.Ended(); if (ready && !ended) jpg = v.Frame(); } catch { }
                    try
                    {
                        f.BeginInvoke(new Action(() =>
                        {
                            busy = false;
                            if (ended) { timer.Stop(); status.Text = "Session ended: " + v.Reason; return; }
                            if (!ready) return;
                            var m = TeamSecurity.Member(c.Policy, r.Sharer);
                            status.Text = (m != null ? m.Name : r.Sharer) + "'s screen  ·  " + (v.A.Mode == "control" ? "you can use mouse and keyboard" : "view only") + "  ·  " + Math.Max(0, (v.A.Expires - TeamClient.NowMs()) / 60000) + " min left";
                            if (jpg != null) { var old = pic.Image; var bmp = Image.FromStream(new MemoryStream(jpg)); img = bmp.Size; pic.Image = bmp; if (old != null) old.Dispose(); }
                        }));
                    }
                    catch { busy = false; }
                });
            };
            f.FormClosed += delegate { timer.Stop(); if (!v.Ended()) v.Stop("viewer closed"); };
            timer.Start(); f.Show();
        }
    }

    // ===================================================================================================
    // Admin console: people & PCs, permissions, rooms, remote sessions, audit, settings. Admin mode only.
    // ===================================================================================================
    class AdminConsole : Form
    {
        readonly TeamClient c;
        ListView people, grants, roomsList, sessions, audit;
        TextBox auditFilter;
        public static bool EnsureUnlocked(TeamClient c, IWin32Window owner)
        {
            if (TeamSecurity.Unlocked(c)) return true;
            if (!c.IsAdmin) { MessageBox.Show(owner, "Only the team admin's PC can do this.", "Admin"); return false; }
            for (int i = 0; i < 3; i++)
            {
                string p = PromptSecret(owner, "Admin mode", "Admin passphrase");
                if (p == null) return false;
                if (TeamSecurity.Unlock(c, p)) return true;
                MessageBox.Show(owner, "Wrong passphrase.", "Admin");
            }
            return false;
        }
        public static string PromptSecret(IWin32Window owner, string caption, string prompt)
        {
            using (var f = new Form { Text = caption, Width = Ui.S(420), Height = Ui.S(170), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false })
            {
                var l = new Label { Text = prompt, Dock = DockStyle.Top, Height = Ui.S(28), Padding = new Padding(Ui.S(10), Ui.S(8), 0, 0) };
                var t = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true, Margin = new Padding(Ui.S(10)) };
                var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(46), FlowDirection = FlowDirection.RightToLeft };
                var ok = Ui.Btn("OK", null, true); ok.DialogResult = DialogResult.OK; var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                row.Controls.Add(ok); row.Controls.Add(cancel);
                f.Controls.Add(row); f.Controls.Add(t); f.Controls.Add(l); f.AcceptButton = ok; f.CancelButton = cancel; Ui.Theme(f);
                return f.ShowDialog(owner) == DialogResult.OK ? t.Text : null;
            }
        }

        public AdminConsole(TeamClient c)
        {
            this.c = c;
            Text = "Admin console - " + (c.Team != null ? c.Team.Name : "team"); Width = Ui.S(1150); Height = Ui.S(720); StartPosition = FormStartPosition.CenterScreen;
            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(PeopleTab()); tabs.TabPages.Add(GrantsTab()); tabs.TabPages.Add(RoomsTab()); tabs.TabPages.Add(SessionsTab()); tabs.TabPages.Add(AuditTab()); tabs.TabPages.Add(SettingsTab());
            Controls.Add(tabs);
            Ui.Theme(this);
            var refresh = new System.Windows.Forms.Timer { Interval = 5000 }; refresh.Tick += delegate { if (Visible) RefreshAll(false); }; refresh.Start();
            FormClosed += delegate { refresh.Dispose(); };
            Shown += delegate { RefreshAll(true); };
        }
        static ListView List(params string[] cols) { var l = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, BorderStyle = BorderStyle.None, HideSelection = false }; foreach (var x in cols) l.Columns.Add(x, Ui.S(x.Length > 10 ? 200 : 120)); return l; }
        static FlowLayoutPanel Bar() { return new FlowLayoutPanel { Dock = DockStyle.Top, Height = Ui.S(44), Padding = new Padding(Ui.S(4)) }; }
        void Do(Action a) { if (!EnsureUnlocked(c, this)) return; try { a(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Admin"); } RefreshAll(true); }
        bool Sure(string q) { return MessageBox.Show(this, q, "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes; }
        string SelId(ListView l) { return l.SelectedItems.Count > 0 ? l.SelectedItems[0].Tag as string : null; }

        TabPage PeopleTab()
        {
            var tp = new TabPage("People & PCs");
            people = List("Name", "PC / user", "Status", "Role", "Groups", "Online", "Version", "Update state", "Device code", "Last seen");
            var bar = Bar();
            bar.Controls.Add(Ui.Btn("Approve", delegate { var id = SelId(people); if (id == null) return; var j = TeamSecurity.PendingJoins(c).FirstOrDefault(x => x.Id == id); string code = j != null ? TeamSecurity.Fingerprint(j.PublicKey) : ""; if (Sure("Approve this PC?\n\nDevice code: " + code + "\nCompare it with the code shown on that PC before you approve.")) Do(() => TeamSecurity.Approve(c, id)); }, true));
            bar.Controls.Add(Ui.Btn("Suspend", delegate { var id = SelId(people); if (id != null && Sure("Suspend this PC? It keeps its history but cannot read or send until restored.")) Do(() => TeamSecurity.SetStatus(c, id, "suspended", false)); }));
            bar.Controls.Add(Ui.Btn("Restore", delegate { var id = SelId(people); if (id != null && Sure("Give this PC its access back?")) Do(() => TeamSecurity.SetStatus(c, id, "approved", false)); }));
            bar.Controls.Add(Ui.Btn("Revoke", delegate { var id = SelId(people); if (id != null && Sure("Revoke this PC? Room keys are rotated so it cannot read anything new.")) Do(() => TeamSecurity.SetStatus(c, id, "revoked", false)); }));
            bar.Controls.Add(Ui.Btn("Close app now", delegate { var id = SelId(people); if (id != null && Sure("Close All in One on this PC now (work is saved)? It can reconnect afterwards.")) Do(() => TeamSecurity.SetStatus(c, id, "approved", true)); }));
            bar.Controls.Add(Ui.Btn("Block + close app", delegate { var id = SelId(people); if (id != null && Sure("Close All in One on this PC and block it until you restore it?")) Do(() => TeamSecurity.SetStatus(c, id, "blocked", true)); }));
            bar.Controls.Add(Ui.Btn("Groups...", delegate
            {
                var id = SelId(people); var m = TeamSecurity.Member(c.Policy, id); if (m == null) return;
                string g = TeamPage.AskText(this, "Groups", "Groups for " + m.Name + " (comma separated, e.g. engineering, pilot). Known: " + string.Join(", ", c.Policy.GroupNames), string.Join(", ", m.Groups));
                if (g != null) Do(() => TeamSecurity.SetGroups(c, id, g.Split(',')));
            }));
            tp.Controls.Add(people); tp.Controls.Add(bar);
            return tp;
        }
        TabPage GrantsTab()
        {
            var tp = new TabPage("Permissions");
            grants = List("Who", "May", "Target", "Note");
            var bar = Bar();
            bar.Controls.Add(Ui.Btn("Add permission...", delegate { AddGrant(); }, true));
            bar.Controls.Add(Ui.Btn("Remove", delegate { var g = grants.SelectedItems.Count > 0 ? grants.SelectedItems[0].Tag as TeamGrant : null; if (g != null && Sure("Remove this permission? It takes effect on every PC within seconds.")) Do(() => TeamSecurity.Revoke(c, g.Subject, g.Feature, g.Target)); }));
            var note = new Label { Dock = DockStyle.Bottom, Height = Ui.S(40), ForeColor = Ui.Muted, Text = "Default deny: only what is listed here is allowed (admins may do everything). Who: * = everyone, group:<name>, or one PC. Target: *, room:<id>, group:<name>, a PC, project:<folder>." };
            tp.Controls.Add(grants); tp.Controls.Add(note); tp.Controls.Add(bar);
            return tp;
        }
        void AddGrant()
        {
            var p = c.Policy; if (p == null) return;
            using (var f = new Form { Text = "Add permission", Width = Ui.S(560), Height = Ui.S(300), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, ShowInTaskbar = false })
            {
                var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(Ui.S(10)) };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(110))); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                Func<IEnumerable<string>, ComboBox> combo = items => { var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown }; foreach (var x in items) cb.Items.Add(x); if (cb.Items.Count > 0) cb.SelectedIndex = 0; return cb; };
                var whoItems = new List<string> { "*" }; whoItems.AddRange(p.GroupNames.Select(g => "group:" + g)); whoItems.AddRange(p.Members.Select(m => m.Id + "  (" + m.Name + ")"));
                var who = combo(whoItems);
                var what = combo(TeamSecurity.Features.Select(x => x + "  - " + TeamSecurity.Describe(x)));
                var targetItems = new List<string> { "*" }; targetItems.AddRange(c.AllRooms().Select(r => "room:" + r.Id)); targetItems.AddRange(p.GroupNames.Select(g => "group:" + g)); targetItems.AddRange(p.Members.Select(m => m.Id + "  (" + m.Name + ")")); targetItems.AddRange(Projects.All().Take(40).Select(x => "project:" + x.Path));
                var target = combo(targetItems);
                var note = new TextBox { Dock = DockStyle.Fill };
                foreach (var row in new[] { new object[] { "Who", who }, new object[] { "May", what }, new object[] { "Target", target }, new object[] { "Note", note } }) { table.Controls.Add(new Label { Text = (string)row[0], TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }); table.Controls.Add((Control)row[1]); }
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(46), FlowDirection = FlowDirection.RightToLeft };
                var ok = Ui.Btn("Add", null, true); ok.DialogResult = DialogResult.OK; var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                bar.Controls.Add(ok); bar.Controls.Add(cancel);
                f.Controls.Add(table); f.Controls.Add(bar); Ui.Theme(f);
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Func<string, string> first = s => (s ?? "").Split(new[] { "  " }, StringSplitOptions.None)[0].Trim();
                string s1 = first(who.Text), s2 = first(what.Text), s3 = first(target.Text), n = note.Text;
                if (s2.StartsWith("screen.") && !Sure("Allow " + s1 + " to " + (s2 == "screen.control" ? "CONTROL" : "view") + " the screens of " + s3 + "?\nThe other person still has to approve each session.")) return;
                Do(() => TeamSecurity.Grant(c, s1, s2, s3, n));
            }
        }
        TabPage RoomsTab()
        {
            var tp = new TabPage("Rooms");
            roomsList = List("Room", "Kind", "Project", "Members who can read", "Key epoch");
            var bar = Bar();
            bar.Controls.Add(Ui.Btn("Re-check keys now", delegate { Do(() => TeamSecurity.SyncKeys(c, null, true)); }));
            tp.Controls.Add(roomsList); tp.Controls.Add(bar);
            return tp;
        }
        TabPage SessionsTab()
        {
            var tp = new TabPage("Remote sessions");
            sessions = List("Started", "Helper", "Screen of", "Mode", "State", "Ends / reason");
            var bar = Bar();
            bar.Controls.Add(Ui.Btn("End selected session", delegate { var sid = SelId(sessions); if (sid != null && Sure("End this remote session now?")) Do(() => TeamRemote.End(c, sid, "ended by the admin")); }, true));
            tp.Controls.Add(sessions); tp.Controls.Add(bar);
            return tp;
        }
        TabPage AuditTab()
        {
            var tp = new TabPage("Audit log");
            audit = List("Time", "PC", "Person", "Action", "Detail");
            var bar = Bar();
            auditFilter = new TextBox { Width = Ui.S(260) }; Ui.Cue(auditFilter, "Filter (person, action, text)");
            auditFilter.TextChanged += delegate { FillAudit(); };
            bar.Controls.Add(auditFilter);
            bar.Controls.Add(Ui.Btn("Refresh", delegate { FillAudit(); }));
            tp.Controls.Add(audit); tp.Controls.Add(bar);
            return tp;
        }
        TabPage SettingsTab()
        {
            var tp = new TabPage("Settings");
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Ui.S(120), FlowDirection = FlowDirection.TopDown, Padding = new Padding(Ui.S(10)) };
            bar.Controls.Add(Ui.Btn("Remote session time limit...", delegate { string m = TeamPage.AskText(this, "Session limit", "Minutes a remote session may last (1-240)", c.Policy.SessionMinutes.ToString()); int n; if (m != null && int.TryParse(m, out n)) Do(() => TeamSecurity.ChangePolicy(c, p => p.SessionMinutes = Math.Max(1, Math.Min(240, n)), "session minutes " + n)); }));
            bar.Controls.Add(Ui.Btn("Change admin passphrase...", delegate { if (!EnsureUnlocked(c, this)) return; string a = PromptSecret(this, "New passphrase", "New admin passphrase (8+ characters)"); if (a == null) return; string b = PromptSecret(this, "New passphrase", "Type it again"); if (a != b) { MessageBox.Show(this, "The two entries differ."); return; } try { TeamSecurity.SetPassphrase(c, a); c.Audit("admin-passphrase", "changed"); MessageBox.Show(this, "Passphrase changed."); } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }));
            bar.Controls.Add(Ui.Btn("Lock admin mode now", delegate { TeamSecurity.Lock(c); Close(); }));
            tp.Controls.Add(bar);
            return tp;
        }

        void RefreshAll(bool full)
        {
            var p = c.Policy; if (p == null) return;
            var devs = c.Devices(true).ToDictionary(d => d.Id);
            string sel = SelId(people);
            people.BeginUpdate(); people.Items.Clear();
            foreach (var j in TeamSecurity.PendingJoins(c))
                people.Items.Add(new ListViewItem(new[] { j.Name, j.Machine + " / " + j.User, "WAITING FOR APPROVAL", "", "", "", "", "", TeamSecurity.Fingerprint(j.PublicKey), TeamClient.FromMs(j.Ts).ToString("dd MMM HH:mm") }) { Tag = j.Id, ForeColor = Ui.Accent });
            foreach (var m in p.Members.OrderBy(x => x.Name))
            {
                TeamDevice d; devs.TryGetValue(m.Id, out d);
                var it = new ListViewItem(new[] { m.Name, m.Machine + " / " + m.User, m.Status, m.Role, string.Join(", ", m.Groups), d != null && TeamClient.Online(d) ? "online" : "offline", d != null ? d.Version : "", d != null ? d.State + (d.Error.Length > 0 ? " - " + d.Error : "") : "", TeamSecurity.Fingerprint(m.PublicKey), d != null ? TeamClient.FromMs(d.LastSeen).ToString("dd MMM HH:mm") : "" }) { Tag = m.Id };
                if (m.Status != "approved") it.ForeColor = Ui.Danger;
                people.Items.Add(it);
                if (m.Id == sel) it.Selected = true;
            }
            people.EndUpdate();
            Func<string, string> name = id => { var m = TeamSecurity.Member(p, id); return m != null ? m.Name : id; };
            Func<string, string> label = s => s.StartsWith("group:") || s.StartsWith("room:") || s.StartsWith("project:") || s == "*" ? (s == "*" ? "everyone / anything" : s) : name(s);
            grants.BeginUpdate(); grants.Items.Clear();
            foreach (var g in p.Grants) grants.Items.Add(new ListViewItem(new[] { label(g.Subject), g.Feature + "  (" + TeamSecurity.Describe(g.Feature) + ")", label(g.Target), g.Note }) { Tag = g });
            grants.EndUpdate();
            roomsList.BeginUpdate(); roomsList.Items.Clear();
            foreach (var r in c.AllRooms(true)) roomsList.Items.Add(new ListViewItem(new[] { c.RoomTitle(r), r.Kind, r.Project, string.Join(", ", TeamSecurity.RoomMembers(p, r).Select(name)), TeamSecurity.LatestEpoch(c, r.Id).ToString() }));
            roomsList.EndUpdate();
            string ss = SelId(sessions);
            sessions.BeginUpdate(); sessions.Items.Clear();
            foreach (var s in TeamRemote.Sessions(c, 72))
            {
                string state = s.Ended ? "ended" : s.A != null ? "ACTIVE" : "waiting for approval";
                var it = new ListViewItem(new[] { TeamClient.FromMs(s.R.Ts).ToString("dd MMM HH:mm"), name(s.R.Viewer), name(s.R.Sharer), s.A != null ? s.A.Mode : s.R.Mode, state, s.Ended ? s.Reason : s.A != null ? TeamClient.FromMs(s.A.Expires).ToString("HH:mm") : "" }) { Tag = s.R.Sid };
                if (state == "ACTIVE") it.ForeColor = Ui.Danger;
                sessions.Items.Add(it); if (s.R.Sid == ss) it.Selected = true;
            }
            sessions.EndUpdate();
            if (full) FillAudit();
        }
        void FillAudit()
        {
            var rows = new List<string[]>();
            try
            {
                foreach (var f in Directory.GetFiles(Path.Combine(c.Root, "audit"), "*.jsonl"))
                    foreach (var line in TeamClient.ReadShared(f).Split('\n').Reverse().Take(400))
                    {
                        if (line.Trim().Length == 0) continue;
                        try { var d = TeamClient.FromJson<Dictionary<string, object>>(line); rows.Add(new[] { Convert.ToString(d["ts"]), Convert.ToString(d["device"]), Convert.ToString(d["name"]), Convert.ToString(d["action"]), Convert.ToString(d["detail"]) }); } catch { }
                    }
            }
            catch { }
            string q = auditFilter.Text.Trim();
            audit.BeginUpdate(); audit.Items.Clear();
            foreach (var r in rows.Where(x => q.Length == 0 || x.Any(y => y.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).OrderByDescending(x => long.Parse(x[0])).Take(500))
                audit.Items.Add(new ListViewItem(new[] { TeamClient.FromMs(long.Parse(r[0])).ToString("dd MMM HH:mm:ss"), r[1], r[2], r[3], r[4] }));
            audit.EndUpdate();
        }
    }
}

