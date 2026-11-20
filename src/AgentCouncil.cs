using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Model council: several models (any CLI, any model, same CLI twice is fine) answer the same question on their
    // own, then discuss each other's answers for a few rounds, then a chair writes the final verdict.
    // Also: saved pane arrangements (workspaces) for the Agents page, a reusable list manager, a multi-line input.
    // ===================================================================================================

    public class CouncilMember
    {
        public string Agent = "", Model = "", Effort = "", Role = "";
        public bool Off;
        public string Account = "";
        public string Key = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Label { get { return AgentStore.NameOf(Agent) + (string.IsNullOrEmpty(Account) ? "" : " [" + Account + "]") + (Model.Length > 0 ? " · " + Model : "") + (Role.Length > 0 ? " (" + Role + ")" : ""); } }
    }

    public class AgentWorkspace
    {
        public string Name = "", Layout = "2";
        public bool Fav;
        public List<string> Panes = new List<string>();
        public List<float> ColW = new List<float>(), RowH = new List<float>();
    }

    static class Council
    {
        public static readonly string[] Roles = { "", "Critic", "Optimist", "Engineer", "Architect", "Security reviewer", "Product thinker", "Devil's advocate", "Teacher (simple words)" };

        // A ready council from the installed CLIs: one strong model each.
        public static List<CouncilMember> Starter()
        {
            var l = new List<CouncilMember>();
            Action<string, string, string> add = (a, m, r) => { var d = AgentStore.Def(a); string f, p; if (d != null && AgentRun.Resolve(d.Exe, out f, out p)) l.Add(new CouncilMember { Agent = a, Model = m, Role = r }); };
            add("claude", "opus", "Architect");
            add("codex", "", "Engineer");
            add("agy", "gemini-4-argon-high", "Critic");
            add("cline", "", "Devil's advocate");
            if (l.Count == 0 && AgentStore.Defs.Count > 0) l.Add(new CouncilMember { Agent = AgentStore.Defs[0].Id });
            return l;
        }

        public static string RoleText(CouncilMember m)
        {
            return m.Role.Length == 0 ? "" : "Your role in this council: " + m.Role + ". Answer from that point of view.\n\n";
        }

        public static string FirstRound(CouncilMember m, AgentChat c, string question)
        {
            return "You are one member of a model council (" + Others(c, m) + " also answer). Answer the question below on your own: clear, practical, with your reasons.\n"
                + RoleText(m) + "Question:\n" + question;
        }

        public static string Discussion(CouncilMember m, AgentChat c, string question, Dictionary<string, string> last, int round)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Model council, discussion round " + round + ". The question was:\n").Append(question).Append("\n\nThe other members answered:\n\n");
            foreach (var o in c.Members.Where(x => x.Key != m.Key && last.ContainsKey(x.Key))) sb.Append("[").Append(o.Label).Append("]\n").Append(Cut(last[o.Key])).Append("\n\n");
            sb.Append(RoleText(m)).Append("Say where you agree and disagree, correct mistakes, and give your improved answer. Be short: only what is new.");
            return sb.ToString();
        }

        public static string Verdict(AgentChat c, string question, Dictionary<string, string> last)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("You chair a model council. The question was:\n").Append(question).Append("\n\nThe members' latest answers:\n\n");
            foreach (var o in c.Members.Where(x => last.ContainsKey(x.Key))) sb.Append("[").Append(o.Label).Append("]\n").Append(Cut(last[o.Key])).Append("\n\n");
            sb.Append("Write the council's verdict in simple English:\n1. Final answer / recommendation\n2. Where the members agree\n3. Where they disagree and who is more convincing, and why\n4. Risks or open questions");
            return sb.ToString();
        }

        static string Others(AgentChat c, CouncilMember m) { var o = c.Members.Where(x => x.Key != m.Key && !x.Off).Select(x => x.Label).ToList(); return o.Count == 0 ? "no one else" : string.Join(", ", o); }
        static string Cut(string s) { return s.Length > 8000 ? s.Substring(0, 8000) + "\n[...]" : s; }
    }

    // ---------- council setup ----------
    class CouncilDialog : Form
    {
        readonly AgentChat chat;
        readonly List<CouncilMember> members;
        readonly List<string> chairKeys = new List<string>();
        readonly DataGridView grid;
        readonly NumericUpDown rounds;
        readonly ComboBox chair;
        readonly CheckBox on, leaderMode;

        public CouncilDialog(AgentChat chat) : this(chat, chat.LeaderTeam) { }
        public CouncilDialog(AgentChat chat, bool leader)
        {
            this.chat = chat;
            members = chat.Members.Count > 0 ? chat.Members.Select(m => new CouncilMember { Agent = m.Agent, Model = m.Model, Effort = m.Effort, Role = m.Role, Off = m.Off, Key = m.Key, Account = m.Account }).ToList() : Council.Starter();
            Text = "Agent team setup"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(Ui.S(900), Ui.S(520)); Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text;
            var intro = new Label { Dock = DockStyle.Top, Height = Ui.S(58), UseMnemonic = false, ForeColor = Ui.Muted, Padding = new Padding(Ui.S(12), Ui.S(10), Ui.S(12), 0),
                Text = "Pick a CLI, model and role for each member. A leader assigns work and reviews it." };
            grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Ui.Card, BorderStyle = BorderStyle.None, EditMode = DataGridViewEditMode.EditOnEnter };
            var colOn = new DataGridViewCheckBoxColumn { HeaderText = "On", FillWeight = 12 };
            var colAgent = new DataGridViewComboBoxColumn { HeaderText = "Agent (CLI)", FillWeight = 40, FlatStyle = FlatStyle.Flat };
            foreach (var d in AgentStore.Defs) colAgent.Items.Add(d.Name);
            var colModel = new DataGridViewTextBoxColumn { HeaderText = "Model (empty = CLI default)", FillWeight = 70 };
            var colEffort = new DataGridViewComboBoxColumn { HeaderText = "Effort", FillWeight = 30, FlatStyle = FlatStyle.Flat };
            foreach (var e in new[] { "", "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" }) colEffort.Items.Add(e);
            var colRole = new DataGridViewComboBoxColumn { HeaderText = "Role", FillWeight = 45, FlatStyle = FlatStyle.Flat };
            foreach (var r in Council.Roles) colRole.Items.Add(r);
            grid.Columns.AddRange(colOn, colAgent, colModel, colEffort, colRole, new DataGridViewTextBoxColumn { HeaderText = "Account", FillWeight = 42 });
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.DataError += (s, e) => { e.ThrowException = false; };
            grid.CellClick += (s, e) => { if (e.RowIndex >= 0 && e.ColumnIndex == 2) PickModel(e.RowIndex); if (e.RowIndex >= 0 && e.ColumnIndex == 5) PickAccount(e.RowIndex); };
            foreach (var m in members) AddRow(m);

            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Ui.S(200), FlowDirection = FlowDirection.TopDown, Padding = new Padding(Ui.S(10)) };
            side.Controls.Add(Ui.Btn("Add member", delegate { var m = new CouncilMember { Agent = AgentStore.Defs[0].Id }; members.Add(m); AddRow(m); FillChair(); }));
            side.Controls.Add(Ui.Btn("Remove member", delegate { if (grid.CurrentRow != null && members.Count > 1) { int index = grid.CurrentRow.Index; members.RemoveAt(index); grid.Rows.RemoveAt(index); FillChair(); } }));
            side.Controls.Add(Ui.Btn("Move up", delegate { MoveRow(-1); }));
            side.Controls.Add(Ui.Btn("Move down", delegate { MoveRow(1); }));
            side.Controls.Add(Ui.Btn("Starter council", delegate { Read(); members.Clear(); members.AddRange(Council.Starter()); grid.Rows.Clear(); foreach (var m in members) AddRow(m); FillChair(); }));
            side.Controls.Add(new Label { Text = "Discussion rounds", AutoSize = true, Margin = new Padding(0, Ui.S(14), 0, 0) });
            rounds = new NumericUpDown { Minimum = 0, Maximum = 4, Value = Math.Max(0, Math.Min(4, chat.Rounds)), Width = Ui.S(80) };
            side.Controls.Add(rounds);
            side.Controls.Add(new Label { Text = "0 skips discussion rounds", AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small });
            side.Controls.Add(new Label { Text = "Chair or team leader", AutoSize = true, Margin = new Padding(0, Ui.S(12), 0, 0) });
            chair = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.S(180) };
            side.Controls.Add(chair);
            chair.DropDown += delegate { FillChair(); };
            FillChair();

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(48), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.S(8)) };
            var ok = Ui.Btn("Save", delegate { Save(); DialogResult = DialogResult.OK; Close(); }, true);
            bottom.Controls.Add(ok);
            bottom.Controls.Add(Ui.Btn("Cancel", delegate { Close(); }));
            leaderMode = new CheckBox { Text = "Leader assigns tasks", Checked = leader, AutoSize = true, Margin = new Padding(Ui.S(6)) };
            bottom.Controls.Add(leaderMode); rounds.Enabled = !leader;
            leaderMode.CheckedChanged += delegate { rounds.Enabled = !leaderMode.Checked; if (leaderMode.Checked) on.Checked = true; };
            on = new CheckBox { Text = "Team enabled", Checked = leader || chat.Council || chat.Members.Count == 0, AutoSize = true, Margin = new Padding(Ui.S(8), Ui.S(8), Ui.S(20), 0) };
            bottom.Controls.Add(on);
            Controls.Add(grid); Controls.Add(side); Controls.Add(intro); Controls.Add(bottom);
            Ui.Theme(this);
            AcceptButton = ok;
        }

        void AddRow(CouncilMember m)
        {
            var d = AgentStore.Def(m.Agent);
            grid.Rows.Add(!m.Off, d != null ? d.Name : AgentStore.Defs[0].Name, m.Model, m.Effort, Council.Roles.Contains(m.Role) ? m.Role : "", m.Account);
        }

        void PickModel(int row)
        {
            Read();
            var m = members[row]; var d = AgentStore.Def(m.Agent); if (d == null) return;
            string choice = TeamModelPicker.Pick(this, d, m.Model);
            if (choice != null) { grid.Rows[row].Cells[2].Value = choice; AgentModels.Remember(d, choice); }
        }

        void PickAccount(int row)
        {
            Read();
            var member = members[row];
            if (!AgentAccounts.Supported(member.Agent)) return;
            var menu = Ui.Menu(); Ui.ThemeMenu(menu);
            menu.Items.Add("Use chat account", null, delegate { grid.Rows[row].Cells[5].Value = ""; });
            try { foreach (string profile in AgentAccounts.Names(member.Agent)) { string name = profile; menu.Items.Add(name, null, delegate { grid.Rows[row].Cells[5].Value = name; }); } }
            catch (Exception ex) { menu.Items.Add(ex.Message).Enabled = false; }
            menu.Items.Add("Sign in to another account…", null, delegate {
                string name = Ui.Ask("Account sign-in", "Account name", "", this);
                if (name == null) return;
                try { name = name.Trim().ToLowerInvariant(); AgentAccounts.Login(AgentStore.Def(member.Agent), name); grid.Rows[row].Cells[5].Value = name; }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Account sign-in"); }
            });
            var r = grid.GetCellDisplayRectangle(5, row, false); menu.Show(grid, new Point(r.X, r.Bottom));
        }

        void MoveRow(int dlt)
        {
            if (grid.CurrentRow == null) return;
            Read();
            int i = grid.CurrentRow.Index, j = i + dlt; if (j < 0 || j >= members.Count) return;
            var m = members[i]; members.RemoveAt(i); members.Insert(j, m);
            grid.Rows.Clear(); foreach (var x in members) AddRow(x);
            grid.CurrentCell = grid.Rows[j].Cells[1];
            FillChair();
        }

        void Read()
        {
            grid.EndEdit();
            for (int i = 0; i < members.Count && i < grid.Rows.Count; i++)
            {
                var row = grid.Rows[i]; var m = members[i];
                m.Off = !(row.Cells[0].Value is bool && (bool)row.Cells[0].Value);
                string an = Convert.ToString(row.Cells[1].Value);
                var d = AgentStore.Defs.FirstOrDefault(x => x.Name == an); if (d != null) m.Agent = d.Id;
                m.Model = (Convert.ToString(row.Cells[2].Value) ?? "").Trim();
                m.Effort = Convert.ToString(row.Cells[3].Value) ?? "";
                m.Role = Convert.ToString(row.Cells[4].Value) ?? "";
                m.Account = AgentAccounts.Supported(m.Agent) ? (Convert.ToString(row.Cells[5].Value) ?? "").Trim() : "";
            }
        }

        void FillChair()
        {
            Read();
            string cur = chair.SelectedIndex >= 0 ? chairKeys[chair.SelectedIndex] : chat.Chair;
            chair.Items.Clear(); chairKeys.Clear();
            chair.Items.Add("First responding member"); chairKeys.Add("");
            foreach (var m in members.Where(member => !member.Off)) { chair.Items.Add(m.Label); chairKeys.Add(m.Key); }
            chair.SelectedIndex = Math.Max(0, chairKeys.IndexOf(cur));
        }

        void Save()
        {
            FillChair();
            chat.Members = members;
            chat.Rounds = (int)rounds.Value;
            chat.Chair = chairKeys[chair.SelectedIndex];
            chat.Council = on.Checked; chat.LeaderTeam = chat.Council && leaderMode.Checked;
        }
    }

    // ---------- a list you can open, rename, favorite, duplicate, order and delete ----------
    class ListManager : Form
    {
        readonly ListBox list;
        readonly Func<List<string>> names;
        readonly Func<int, bool> isFav;
        public ListManager(string title, Func<List<string>> names, Func<int, bool> isFav, Action<int> open, Action<int> rename, Action<int> fav, Action<int> duplicate, Action<int, int> move, Action<int> delete)
        {
            this.names = names; this.isFav = isFav;
            Text = title; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
            ClientSize = new Size(Ui.S(560), Ui.S(420)); Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text;
            list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Ui.S(170), FlowDirection = FlowDirection.TopDown, Padding = new Padding(Ui.S(10)) };
            Action<string, Action<int>, bool> btn = (t, a, close) => side.Controls.Add(Ui.Btn(t, delegate { int i = list.SelectedIndex; if (i < 0) return; a(i); if (close) Close(); else Fill(i); }));
            if (open != null) btn("Open", open, true);
            if (rename != null) btn("Rename...", rename, false);
            if (fav != null) btn("Favorite on / off", fav, false);
            if (duplicate != null) btn("Duplicate", duplicate, false);
            if (move != null) { btn("Move up", i => move(i, -1), false); side.Controls.Add(Ui.Btn("Move down", delegate { int i = list.SelectedIndex; if (i < 0) return; move(i, 1); Fill(Math.Min(i + 1, list.Items.Count - 1)); })); }
            if (delete != null) btn("Delete", delete, false);
            side.Controls.Add(Ui.Btn("Close", delegate { Close(); }));
            list.DoubleClick += delegate { if (open != null && list.SelectedIndex >= 0) { open(list.SelectedIndex); Close(); } };
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(10)) }; pad.Controls.Add(list);
            Controls.Add(pad); Controls.Add(side);
            Ui.Theme(this);
            Fill(0);
        }
        void Fill(int sel)
        {
            var n = names();
            list.BeginUpdate(); list.Items.Clear();
            for (int i = 0; i < n.Count; i++) list.Items.Add((isFav != null && isFav(i) ? "★  " : "     ") + n[i]);
            list.EndUpdate();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Max(0, Math.Min(sel, list.Items.Count - 1));
        }
    }

    // ---------- multi-line input (paste a list) ----------
    static class AskLines
    {
        public static string Show(string title, string hint, string text, IWin32Window owner)
        {
            using (var f = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false, ClientSize = new Size(Ui.S(520), Ui.S(380)), Font = Ui.Base, BackColor = Ui.Bg, ForeColor = Ui.Text })
            {
                var lab = new Label { Dock = DockStyle.Top, Height = Ui.S(48), Text = hint, ForeColor = Ui.Muted, Padding = new Padding(Ui.S(10), Ui.S(8), Ui.S(10), 0), UseMnemonic = false };
                var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Text = text ?? "", Font = AgentFonts.Mono, MaxLength = 0 };
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(46), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.S(6)) };
                string result = null;
                bar.Controls.Add(Ui.Btn("Save", delegate { result = box.Text; f.Close(); }, true));
                bar.Controls.Add(Ui.Btn("Cancel", delegate { f.Close(); }));
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(10), 0, Ui.S(10), 0) }; pad.Controls.Add(box);
                f.Controls.Add(pad); f.Controls.Add(lab); f.Controls.Add(bar);
                Ui.Theme(f);
                f.ShowDialog(owner);
                return result;
            }
        }
    }

    static class AgentFonts { public static Font Mono { get { return ChatView.Mono; } } }
}
