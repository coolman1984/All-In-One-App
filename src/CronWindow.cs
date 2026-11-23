using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace StickyPro
{
    sealed class CronWindow : Form
    {
        readonly FastList jobs = new FastList { MultiSelect = false, FillIndex = 0 };
        readonly TextBox details = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        readonly Timer refresh = new Timer { Interval = 1500 };
        readonly Label message = new Label { AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly Button edit, toggle, run, retry, remove;
        string snapshot;
        bool checking;

        public CronWindow()
        {
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Font = Ui.Base;
            Text = "Scheduled jobs";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = Ui.S(670, 495);
            MinimumSize = Ui.S(575, 370);
            Icon = App.AppIcon;

            jobs.Columns.Add("Job", Ui.S(240));
            jobs.Columns.Add("Schedule", Ui.S(166));
            jobs.Columns.Add("Last run", Ui.S(130));
            jobs.SelectedIndexChanged += delegate { ShowDetails(); };
            jobs.DoubleClick += delegate { if (edit.Enabled) EditSelected(); };

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoSize = true };
            actions.Controls.Add(Ui.Btn("New job", delegate { EditJob(null); }, true));
            edit = Ui.Btn("Edit", delegate { EditSelected(); });
            toggle = Ui.Btn("Disable", delegate { ChangeSelected("toggle"); });
            run = Ui.Btn("Run now", delegate { RunSelected("run_now"); });
            retry = Ui.Btn("Retry", delegate { RunSelected("retry"); });
            remove = Ui.Btn("Delete", delegate { ChangeSelected("delete"); });
            foreach (var button in new[] { edit, toggle, run, retry, remove }) actions.Controls.Add(button);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(12)), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(27)));
            jobs.Dock = details.Dock = message.Dock = DockStyle.Fill;
            message.ForeColor = Ui.Muted;
            details.AccessibleName = "Selected job result";
            jobs.AccessibleName = "Scheduled jobs";
            layout.Controls.Add(actions, 0, 0);
            layout.Controls.Add(jobs, 0, 1);
            layout.Controls.Add(details, 0, 2);
            layout.Controls.Add(message, 0, 3);
            Controls.Add(layout);
            Ui.DarkTitle(this);
            Ui.Theme(this);
            ShowDetails();
            Shown += delegate { Reload(null); refresh.Start(); };
            refresh.Tick += delegate { Reload(null); };
        }

        public static void Open(IWin32Window owner)
        {
            using (var window = new CronWindow())
                if (owner == null) window.ShowDialog(); else window.ShowDialog(owner);
        }

        CronJob Selected() { return jobs.SelectedItems.Count == 0 ? null : (CronJob)jobs.SelectedItems[0].Tag; }

        void Reload(string select)
        {
            List<CronJob> all;
            try { all = CronEngine.LoadJobs(); }
            catch (Exception ex) { message.Text = ex.Message; return; }
            string next = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(all);
            if (next == snapshot && select == null) return;
            snapshot = next;
            if (select == null && Selected() != null) select = Selected().id;
            jobs.BeginUpdate();
            try
            {
                jobs.Items.Clear();
                foreach (var job in all.OrderBy(j => j.name, StringComparer.CurrentCultureIgnoreCase))
                {
                    var row = new ListViewItem(new[] { job.name, job.schedule + (job.enabled ? "" : " · disabled"), string.IsNullOrEmpty(job.run_status) ? "—" : job.run_status }) { Tag = job };
                    jobs.Items.Add(row);
                    row.Selected = job.id == select;
                }
                if (jobs.Items.Count > 0 && jobs.SelectedItems.Count == 0) jobs.Items[0].Selected = true;
            }
            finally { jobs.EndUpdate(); }
            ShowDetails();
        }

        void ShowDetails()
        {
            var job = Selected();
            bool selected = job != null;
            bool available = selected && !checking && job.run_status != "queued" && job.run_status != "running";
            edit.Enabled = toggle.Enabled = run.Enabled = remove.Enabled = available;
            retry.Enabled = available && (job.run_status == "failed" || job.run_status == "interrupted");
            toggle.Text = selected && !job.enabled ? "Enable" : "Disable";
            if (!selected) { details.Text = "Choose a job to see its last result, or select New job to schedule one."; return; }
            string action = !string.IsNullOrWhiteSpace(job.tool) ? "Tool: " + job.tool
                : (!string.IsNullOrWhiteSpace(job.skill) ? "Skill: " + job.skill : "") + "\r\n" + job.prompt;
            string result = string.Join("\r\n", new[] { job.run_error, job.run_output }.Where(v => !string.IsNullOrWhiteSpace(v)));
            DateTime finished;
            string time = DateTime.TryParse(job.run_finished_utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out finished)
                ? finished.ToLocalTime().ToString("g") : "";
            details.Text = string.Join("\r\n\r\n", new[] { action.Trim(), time, result }.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        async void RunSelected(string action)
        {
            var job = Selected();
            if (job == null) return;
            checking = true;
            ShowDetails();
            try
            {
                var result = await System.Threading.Tasks.Task.Run(() => CronEngine.Manage(action, id: job.id));
                if (!IsDisposed)
                    message.Text = true.Equals(result["triggered"]) ? "Triggered. See Last run for the result." : Convert.ToString(result["reason"]);
            }
            catch (Exception ex) { if (!IsDisposed) message.Text = ex.Message; }
            finally { checking = false; if (!IsDisposed) { Reload(job.id); ShowDetails(); } }
        }

        void ChangeSelected(string action)
        {
            var job = Selected();
            if (job == null) return;
            try
            {
                if (action == "delete")
                {
                    if (MessageBox.Show(this, "Delete \"" + job.name + "\"?", Text, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                    CronEngine.Manage("delete", id: job.id);
                }
                else CronEngine.Manage("update", id: job.id, enabled: !job.enabled);
                message.Text = "";
                Reload(job.id);
            }
            catch (Exception ex) { message.Text = ex.Message; }
        }

        void EditSelected() { if (Selected() != null) EditJob(Selected()); }

        void EditJob(CronJob existing)
        {
            var job = existing ?? new CronJob();
            using (var form = Ui.Dialog(existing == null ? "New scheduled job" : "Edit scheduled job", 540, 575))
            {
                var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
                var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(Ui.S(12)) };
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(125)));
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var name = AddField(fields, "Name", job.name);
                var schedule = AddField(fields, "Schedule", job.schedule);
                schedule.AccessibleDescription = "Every 15 minutes, daily at 08:00, or once at a local date and time.";
                AddHint(fields, "Examples: every:15m, daily:08:00, once:2026-12-15T08:00 (local time).");
                var prompt = AddField(fields, "Assistant prompt", job.prompt, true);
                var skill = AddField(fields, "Saved skill", job.skill);
                var tool = AddField(fields, "Registered tool", job.tool);
                var args = AddField(fields, "Tool arguments (JSON)", job.tool_args == null ? "" : new JavaScriptSerializer().Serialize(job.tool_args), true);
                var agent = AddField(fields, "Assistant provider", job.agent);
                var folder = AddField(fields, "Working folder", job.folder);
                var check = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                var checks = new[] { "none", "folder_changed", "sql_nonzero", "overdue_tasks" };
                check.Items.AddRange(new object[] { "Always", "Folder changed", "Query returns a nonzero number", "Tasks are overdue" });
                check.SelectedIndex = Math.Max(0, Array.IndexOf(checks, job.precheck_type));
                AddRow(fields, "Run when", check);
                var input = AddField(fields, "Folder or query", job.precheck_arg);
                EventHandler refreshInput = delegate { input.Enabled = check.SelectedIndex == 1 || check.SelectedIndex == 2; };
                check.SelectedIndexChanged += refreshInput;
                refreshInput(check, EventArgs.Empty);
                var enabled = new CheckBox { Text = "Enabled", AutoSize = true, Checked = job.enabled };
                var once = new CheckBox { Text = "Run once", AutoSize = true, Checked = job.one_shot };
                var options = new FlowLayoutPanel { AutoSize = true };
                options.Controls.Add(enabled); options.Controls.Add(once);
                AddRow(fields, "", options);
                body.Controls.Add(fields);

                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(Ui.S(8)) };
                string id = job.id;
                var save = Ui.Btn("Save", delegate
                {
                    try
                    {
                        var arguments = string.IsNullOrWhiteSpace(args.Text) ? new Dictionary<string, object>()
                            : new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(args.Text);
                        if (arguments == null) throw new ArgumentException("Tool arguments must be a JSON object.");
                        var result = CronEngine.Manage(existing == null ? "create" : "update",
                            id, name.Text, schedule.Text, prompt.Text, skill.Text, checks[check.SelectedIndex], input.Enabled ? input.Text : "",
                            enabled.Checked, once.Checked, tool: tool.Text, toolArgs: arguments, agent: agent.Text, folder: folder.Text);
                        id = Convert.ToString(((Dictionary<string, object>)result["job"])["id"]);
                        form.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MessageBox.Show(form, ex.Message, form.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }, true);
                bottom.Controls.Add(save);
                var cancel = Ui.Btn("Cancel", null);
                cancel.DialogResult = DialogResult.Cancel;
                bottom.Controls.Add(cancel);
                form.CancelButton = cancel;
                form.Controls.Add(body);
                form.Controls.Add(bottom);
                Ui.Theme(form);
                if (form.ShowDialog(this) == DialogResult.OK) { message.Text = ""; Reload(id); }
            }
        }

        static void AddHint(TableLayoutPanel fields, string text)
        {
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { AutoSize = true, Text = text, ForeColor = Ui.Muted, Font = Ui.Small,
                MaximumSize = Ui.S(325, 0), Margin = new Padding(3, 0, 3, Ui.S(7)) }, 1, row);
        }

        static TextBox AddField(TableLayoutPanel fields, string label, string value, bool multiline = false)
        {
            var field = new TextBox { Text = value ?? "", Multiline = multiline, Height = Ui.S(multiline ? 46 : 25) };
            if (multiline) field.ScrollBars = ScrollBars.Vertical;
            AddRow(fields, label, field);
            return field;
        }

        static void AddRow(TableLayoutPanel fields, string label, Control field)
        {
            int row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, Ui.S(6), Ui.S(4), 0) }, 0, row);
            field.AccessibleName = label;
            field.Dock = DockStyle.Fill;
            fields.Controls.Add(field, 1, row);
        }

        protected override void Dispose(bool disposing) { if (disposing) refresh.Dispose(); base.Dispose(disposing); }
    }
}
