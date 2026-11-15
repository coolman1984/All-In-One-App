using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StickyPro
{
    // Quick-add parser. Examples (now = Wed 14:00):
    //  "Call Ali tomorrow 9am !high #work"  -> "Call Ali", Thu 09:00, high, list work, reminder 09:00
    //  "Pay rent every month 1/12"          -> "Pay rent", 1 Dec all-day, monthly
    //  "Standup every weekday at 9:30"      -> "Standup", Thu 09:30 (time passed today), weekdays
    //  "Buy milk today"                     -> "Buy milk", today all-day, no reminder
    //  "Stretch in 20 min"                  -> "Stretch", 14:20 today, reminder
    //  "Report fri 5pm #work !h"            -> "Report", Fri 17:00, high
    //  "Gym tonight"                        -> "Gym", today 20:00
    //  "Plan trip next week"                -> "Plan trip", next Monday all-day
    //  "Backup daily 21:15"                 -> "Backup", today 21:15, daily
    //  "Dentist 3/12/2027 10:30am"          -> "Dentist", 3 Dec 2027 10:30
    //  "Read !low"                          -> "Read", low priority, no date
    //  "tomorrow"                           -> title "tomorrow" (nothing left), tomorrow all-day
    static class TaskParse
    {
        const RegexOptions O = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        static readonly string[] Days = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };

        public static TaskItem Parse(string text, DateTime now)
        {
            var t = new TaskItem();
            string s = " " + (text ?? "") + " ";
            Match m;
            // Priority
            if ((m = Regex.Match(s, @"(?<=\s)(!high|!h|!!)(?=\s)", O)).Success) { t.Priority = 2; s = Cut(s, m); }
            else if ((m = Regex.Match(s, @"(?<=\s)(!low|!l)(?=\s)", O)).Success) { t.Priority = 0; s = Cut(s, m); }
            // List
            if ((m = Regex.Match(s, @"(?<=\s)#([\w\-]+)(?=\s)", O)).Success) { t.List = m.Groups[1].Value; s = Cut(s, m); }
            // Repeat
            var reps = new[] { new[] { @"every\s+weekday|weekdays", "weekdays" }, new[] { @"every\s+day|daily", "daily" }, new[] { @"every\s+week|weekly", "weekly" }, new[] { @"every\s+month|monthly", "monthly" }, new[] { @"every\s+year|yearly", "yearly" } };
            foreach (var r in reps) if ((m = Regex.Match(s, @"(?<=\s)(" + r[0] + @")(?=\s)", O)).Success) { t.Repeat = r[1]; s = Cut(s, m); break; }

            DateTime? date = null; int hour = -1, minute = 0; bool relativeTime = false;
            // in N units
            if ((m = Regex.Match(s, @"(?<=\s)in\s+(\d+)\s*(min|mins|minute|minutes|h|hr|hrs|hour|hours|d|day|days|w|week|weeks)(?=\s)", O)).Success)
            {
                int n; string u = m.Groups[2].Value.ToLowerInvariant();
                if (!int.TryParse(m.Groups[1].Value, out n)) n = int.MaxValue;
                n = Math.Min(n, u.StartsWith("m") ? 5256000 : u.StartsWith("h") ? 87600 : u.StartsWith("d") ? 3650 : 520);
                try
                {
                    DateTime when = u.StartsWith("m") ? now.AddMinutes(n) : u.StartsWith("h") ? now.AddHours(n) : u.StartsWith("d") ? now.AddDays(n) : now.AddDays(7 * n);
                    date = when.Date;
                    if (u.StartsWith("m") || u.StartsWith("h")) { hour = when.Hour; minute = when.Minute; relativeTime = true; }
                    s = Cut(s, m);
                }
                catch { date = null; hour = -1; relativeTime = false; }
            }
            if (date == null)
            {
                if ((m = Regex.Match(s, @"(?<=\s)tonight(?=\s)", O)).Success) { date = now.Date; hour = 20; s = Cut(s, m); }
                else if ((m = Regex.Match(s, @"(?<=\s)today(?=\s)", O)).Success) { date = now.Date; s = Cut(s, m); }
                else if ((m = Regex.Match(s, @"(?<=\s)(tomorrow|tmr)(?=\s)", O)).Success) { date = now.Date.AddDays(1); s = Cut(s, m); }
                else if ((m = Regex.Match(s, @"(?<=\s)next\s+week(?=\s)", O)).Success)
                { var d = now.Date.AddDays(1); while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1); date = d; s = Cut(s, m); }
                else if ((m = Regex.Match(s, @"(?<=\s)(\d{1,2})/(\d{1,2})(?:/(\d{4}))?(?=\s)", O)).Success)
                {
                    int day = int.Parse(m.Groups[1].Value), mon = int.Parse(m.Groups[2].Value);
                    int year = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : now.Year;
                    try
                    {
                        var d = new DateTime(year, mon, day);
                        if (!m.Groups[3].Success && d < now.Date) d = d.AddYears(1);
                        date = d; s = Cut(s, m);
                    }
                    catch { }
                }
                else
                {
                    for (int i = 0; i < 7 && date == null; i++)
                    {
                        m = Regex.Match(s, @"(?<=\s)(" + Days[i] + "|" + Days[i].Substring(0, 3) + @")(?=\s)", O);
                        if (!m.Success) continue;
                        var d = now.Date.AddDays(1); while ((int)d.DayOfWeek != i) d = d.AddDays(1);
                        date = d; s = Cut(s, m);
                    }
                }
            }
            // Time
            if (!relativeTime)
            {
                if ((m = Regex.Match(s, @"(?<=\s)(?:at\s+)?(\d{1,2})(?:[:.](\d{2}))?\s*(am|pm)(?=\s)", O)).Success)
                {
                    int h = int.Parse(m.Groups[1].Value), mi = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                    bool pm = m.Groups[3].Value.ToLowerInvariant() == "pm";
                    if (h >= 1 && h <= 12 && mi < 60) { hour = h % 12 + (pm ? 12 : 0); minute = mi; s = Cut(s, m); }
                }
                else if ((m = Regex.Match(s, @"(?<=\s)(?:at\s+)?(\d{1,2}):(\d{2})(?=\s)", O)).Success)
                {
                    int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value);
                    if (h < 24 && mi < 60) { hour = h; minute = mi; s = Cut(s, m); }
                }
                else if ((m = Regex.Match(s, @"(?<=\s)at\s+(\d{1,2})(?=\s)", O)).Success)
                {
                    int h = int.Parse(m.Groups[1].Value);
                    if (h < 24) { hour = h; minute = 0; s = Cut(s, m); }
                }
            }
            if (hour >= 0)
            {
                if (date == null) { var d = now.Date.AddHours(hour).AddMinutes(minute); date = d > now ? now.Date : now.Date.AddDays(1); }
                t.Due = date.Value.Date.AddHours(hour).AddMinutes(minute).Ticks; t.DueHasTime = true; t.RemindAt = t.Due;
            }
            else if (date != null) { t.Due = date.Value.Date.Ticks; t.DueHasTime = false; }
            if (t.Repeat != "none" && t.Due == 0) t.Due = now.Date.Ticks;
            string title = Regex.Replace(s, @"\s+", " ").Trim();
            t.Title = title.Length > 0 ? title : (text ?? "").Trim();
            return t;
        }

        static string Cut(string s, Match m) { return s.Substring(0, m.Index) + " " + s.Substring(m.Index + m.Length); }

        public static string DueText(TaskItem t)
        {
            if (t.Due == 0) return "";
            var d = new DateTime(t.Due); var today = DateTime.Today;
            string time = t.DueHasTime ? " " + d.ToString("HH:mm") : "";
            if (!t.Done && (t.DueHasTime ? d < DateTime.Now : d.Date < today))
            {
                int days = (int)(today - d.Date).TotalDays;
                return days <= 0 ? "Overdue" + time : "Overdue " + days + "d";
            }
            if (d.Date == today) return "Today" + time;
            if (d.Date == today.AddDays(1)) return "Tomorrow" + time;
            if (d.Date < today.AddDays(7)) return d.ToString("ddd") + time;
            return d.ToString(d.Year == today.Year ? "ddd d MMM" : "d MMM yyyy") + time;
        }

        public static bool Overdue(TaskItem t) { if (t.Done || t.Due == 0) return false; var d = new DateTime(t.Due); return t.DueHasTime ? d < DateTime.Now : d.Date < DateTime.Today; }
        public static bool DueToday(TaskItem t) { return !t.Done && t.Due > 0 && new DateTime(t.Due).Date == DateTime.Today; }
    }

    // Reminder pop-up at the bottom-right; never takes focus.
    class ReminderToast : Form
    {
        static readonly List<ReminderToast> open = new List<ReminderToast>();
        public static int OpenCount { get { return open.Count; } }
        readonly bool high;
        bool chosen;
        readonly Action dismiss;

        public ReminderToast(string title, string due, bool high, Action done, Action<DateTime> snooze, Action openIt, Action dismiss)
        {
            this.high = high; this.dismiss = dismiss;
            FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Font = Ui.Base; BackColor = Ui.Card; ForeColor = Ui.Text;
            Size = Ui.S(360, 150); Padding = new Padding(Ui.S(16), Ui.S(10), Ui.S(10), Ui.S(8));
            var head = new Label { Text = "Reminder", Font = Ui.Small, ForeColor = Ui.Muted, Dock = DockStyle.Top, Height = Ui.S(20) };
            var close = new Label { Text = Ui.GClose, Font = Ui.Icon(9f), ForeColor = Ui.Muted, AutoSize = false, Size = Ui.S(24, 20), Dock = DockStyle.Right, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand };
            close.Click += delegate { Close(); };
            head.Controls.Add(close);
            var tl = new Label { Text = title, Font = Ui.Bold, Dock = DockStyle.Top, Height = Ui.S(42), AutoEllipsis = true, UseMnemonic = false };
            var dl = new Label { Text = due, Font = Ui.Small, ForeColor = due.StartsWith("Overdue") ? Ui.Danger : Ui.Muted, Dock = DockStyle.Top, Height = Ui.S(20), UseMnemonic = false };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            bar.Controls.Add(Ui.Btn("Done", delegate { chosen = true; if (done != null) done(); Close(); }, true));
            var sn = Ui.Btn("Snooze", null);
            sn.Click += delegate
            {
                var m = Ui.Menu();
                Action<string, DateTime> add = (txt, when) => m.Items.Add(txt, null, delegate { chosen = true; if (snooze != null) snooze(when); Close(); });
                add("5 minutes", DateTime.Now.AddMinutes(5)); add("15 minutes", DateTime.Now.AddMinutes(15)); add("1 hour", DateTime.Now.AddHours(1));
                add("3 hours", DateTime.Now.AddHours(3)); add("Tomorrow 9:00", DateTime.Today.AddDays(1).AddHours(9));
                Ui.ThemeMenu(m); m.Show(sn, new Point(0, sn.Height));
            };
            bar.Controls.Add(sn);
            if (openIt != null) bar.Controls.Add(Ui.Btn("Open", delegate { chosen = true; openIt(); Close(); }));
            Controls.Add(bar); Controls.Add(dl); Controls.Add(tl); Controls.Add(head);
            Ui.Theme(this);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* TOOLWINDOW */; return cp; } }
        public void Pop()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int gap = Ui.S(8);
            int perCol = Math.Max(1, (wa.Height - Ui.S(12)) / (Height + gap));
            Point best = new Point(wa.Right - Width - Ui.S(12), wa.Bottom - Height - Ui.S(12));
            // First free slot: fill a column bottom-up, then continue in a column to the left.
            for (int slot = 0; slot < 200; slot++)
            {
                int col = slot / perCol, row = slot % perCol;
                var p = new Point(wa.Right - Width - Ui.S(12) - col * (Width + gap), wa.Bottom - Height - Ui.S(12) - row * (Height + gap));
                if (p.X < wa.Left) break;
                bool used = false;
                foreach (var t in open) if (t.Left == p.X && t.Top == p.Y) { used = true; break; }
                best = p;
                if (!used) break;
            }
            Location = best;
            open.Add(this);
            Show();
            try { SystemSounds.Asterisk.Play(); } catch { }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Ui.Line)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(high ? Color.FromArgb(239, 68, 68) : Ui.Accent)) e.Graphics.FillRectangle(b, 0, 0, Ui.S(5), Height);
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { open.Remove(this); if (!chosen && dismiss != null) dismiss(); base.OnFormClosed(e); }
    }

    // Small always-on-top quick add box (global hotkey), no main window needed.
    class QuickTaskForm : Form
    {
        static QuickTaskForm current;
        readonly TextBox box;
        public static void Open()
        {
            if (current != null && !current.IsDisposed) { current.Activate(); return; }
            current = new QuickTaskForm(); current.FormClosed += delegate { current = null; };
            current.Show(); current.Activate(); Native.SetForegroundWindow(current.Handle);
        }
        QuickTaskForm()
        {
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.FixedToolWindow; Text = "Quick add task"; TopMost = true; ShowInTaskbar = false;
            Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text; KeyPreview = true; Icon = App.AppIcon;
            var scr = Screen.FromPoint(Cursor.Position).WorkingArea;
            box = Ui.Search("Add a task - e.g. Call Ali tomorrow 9am !high #work");
            box.Dock = DockStyle.Top;
            var hint = new Label { Dock = DockStyle.Top, Height = Ui.S(22), Font = Ui.Small, ForeColor = Ui.Muted, Text = "Enter = add   ·   Esc = close   ·   words: today, tomorrow, fri, 9am, in 20 min, every week, !high, #list" };
            Padding = new Padding(Ui.S(10));
            Controls.Add(hint); Controls.Add(box);
            ClientSize = new Size(Ui.S(560), box.PreferredHeight + Ui.S(46));
            StartPosition = FormStartPosition.Manual; Location = new Point(scr.Left + (scr.Width - Width) / 2, scr.Top + scr.Height / 4);
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && box.Text.Trim().Length > 0)
                {
                    e.SuppressKeyPress = true;
                    var t = Store.AddTask(TaskParse.Parse(box.Text.Trim(), DateTime.Now));
                    App.I.Balloon("Task added", t.Title + (t.Due > 0 ? "  ·  " + TaskParse.DueText(t) : ""));
                    Close();
                }
            };
            Ui.Theme(this); Ui.DarkTitle(this);
            Shown += delegate { box.Focus(); };
        }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (box.Text.Trim().Length == 0) Close(); }
    }

    // ===================== Tasks & reminders page =====================
    class TasksPage : Panel
    {
        class ListEntry { public string Key, Label; public int Count; public bool Red, Header; public override string ToString() { return Label; } }
        public bool Stale = true;
        readonly ListBox lists;
        readonly CardList cards;
        readonly TextBox quick, search;
        readonly Panel detail, multi;
        readonly Label multiLabel;
        string current = "today";
        bool loading;
        TaskItem editing;
        readonly Timer debounce = new Timer { Interval = 400 };
        // detail controls
        readonly TextBox tTitle, tNotes, stepAdd;
        readonly CheckBox cDue, cAllDay, cRemind;
        readonly DateTimePicker dDue, dTime, dRemind;
        readonly ComboBox cbRepeat, cbPriority, cbList;
        readonly CheckedListBox steps;
        static readonly string[] RepeatKeys = { "none", "daily", "weekdays", "weekly", "monthly", "yearly" };

        public TasksPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            // ----- middle -----
            var mid = new Panel { Dock = DockStyle.Fill };
            cards = new CardList { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
            cards.Info = o => Card((TaskItem)o);
            cards.SelectedIndexChanged += delegate { ShowDetail(); };
            cards.DoubleClick += delegate { ToggleSelected(); };
            cards.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space) { ToggleSelected(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
            };
            cards.ContextMenuStrip = BuildMenu();
            mid.Controls.Add(cards);
            quick = Ui.Search("Add a task - e.g. Call Ali tomorrow 9am !high #work   (Enter)");
            quick.Dock = DockStyle.Top;
            quick.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; QuickAdd(); } };
            search = Ui.Search("Filter tasks");
            search.Dock = DockStyle.Top;
            search.TextChanged += delegate { RefreshList(); };
            var qwrap = new Panel { Dock = DockStyle.Top, Height = quick.PreferredHeight + Ui.S(10), Padding = new Padding(0, 0, 0, Ui.S(6)) };
            qwrap.Controls.Add(quick);
            var swrap = new Panel { Dock = DockStyle.Top, Height = search.PreferredHeight + Ui.S(10), Padding = new Padding(0, 0, 0, Ui.S(6)) };
            swrap.Controls.Add(search);
            mid.Controls.Add(swrap); mid.Controls.Add(qwrap);
            Controls.Add(mid);

            // ----- right detail -----
            detail = new Panel { Dock = DockStyle.Right, Width = PaneGrip.Size("tasks.detail", DockStyle.Right, Ui.S(340), Ui.S(1600)), Padding = new Padding(Ui.S(10), 0, 0, 0), AutoScroll = true };
            var f = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            fieldsPanel = f;
            f.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tTitle = new FluentTextBox { Dock = DockStyle.Fill, Font = Ui.Bold };
            tNotes = new FluentTextBox { Dock = DockStyle.Fill, Multiline = true, Height = Ui.S(70), MinimumSize = Ui.S(0, 70), ScrollBars = ScrollBars.Vertical };
            cDue = new CheckBox { Text = "Due date", AutoSize = true };
            cAllDay = new CheckBox { Text = "All day", AutoSize = true, Checked = true };
            dDue = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd d MMM yyyy", Width = Ui.S(150) };
            dTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = Ui.S(80) };
            cRemind = new CheckBox { Text = "Remind me", AutoSize = true };
            dRemind = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "ddd d MMM yyyy  HH:mm", Width = Ui.S(200) };
            cbRepeat = MainForm.Combo(150, "No repeat", "Daily", "Weekdays", "Weekly", "Monthly", "Yearly");
            cbPriority = MainForm.Combo(150, "Low", "Normal", "High");
            cbList = new ComboBox { Width = Ui.S(200), DropDownStyle = ComboBoxStyle.DropDown };
            steps = new CheckedListBox { Dock = DockStyle.Fill, Height = Ui.S(110), CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
            stepAdd = Ui.Search("Add a step (Enter)"); stepAdd.Dock = DockStyle.Fill;
            Func<string, Label> lab = txt => new Label { Text = txt, AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(0, Ui.S(8), 0, Ui.S(2)) };
            Func<Control[], FlowLayoutPanel> row = cs => { var p = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) }; p.Controls.AddRange(cs); return p; };
            var quickRem = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0) };
            quickRem.Controls.Add(Small("15 min", delegate { SetRemind(DateTime.Now.AddMinutes(15)); }));
            quickRem.Controls.Add(Small("1 hour", delegate { SetRemind(DateTime.Now.AddHours(1)); }));
            quickRem.Controls.Add(Small("Tomorrow 9:00", delegate { SetRemind(DateTime.Today.AddDays(1).AddHours(9)); }));
            quickRem.Controls.Add(Small("At due time", delegate { if (editing != null && editing.Due > 0) SetRemind(new DateTime(editing.Due).Date + (cAllDay.Checked ? TimeSpan.FromHours(9) : dTime.Value.TimeOfDay)); }));
            foreach (var c in new Control[] { lab("Title"), tTitle, lab("Notes"), tNotes, row(new Control[] { cDue, cAllDay }), row(new Control[] { dDue, dTime }), lab("Reminder"), row(new Control[] { cRemind, dRemind }), quickRem,
                lab("Repeat"), cbRepeat, lab("Priority"), cbPriority, lab("List"), cbList, lab("Steps (tick when done, Delete removes)"), steps, stepAdd })
            { f.RowStyles.Add(new RowStyle(SizeType.AutoSize)); f.Controls.Add(c); }
            detail.Controls.Add(f);
            multi = new Panel { Dock = DockStyle.Fill, Visible = false };
            multiLabel = new Label { Dock = DockStyle.Top, Height = Ui.S(30), Font = Ui.Bold };
            var mb = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            mb.Controls.Add(Ui.Btn("Mark done", delegate { foreach (var t in Selected()) Store.CompleteTask(t, true); }, true));
            mb.Controls.Add(Ui.Btn("Delete", delegate { DeleteSelected(); }));
            mb.Controls.Add(Ui.Btn("Move to list...", delegate { var n = Ui.Ask("Move to list", "List name", "Inbox", FindForm()); if (!string.IsNullOrEmpty(n)) foreach (var t in Selected()) { t.List = n.Trim(); Store.TouchTask(t); } }));
            multi.Controls.Add(mb); multi.Controls.Add(multiLabel);
            detail.Controls.Add(multi);
            Controls.Add(detail);
            PaneGrip.Attach(detail, "tasks.detail", Ui.S(220));

            // ----- left lists -----
            var left = new Panel { Dock = DockStyle.Left, Width = PaneGrip.Size("tasks.lists", DockStyle.Left, Ui.S(210), Ui.S(800)), Padding = new Padding(0, 0, Ui.S(8), 0) };
            lists = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(34), BorderStyle = BorderStyle.None, IntegralHeight = false };
            lists.DrawItem += DrawList;
            lists.SelectedIndexChanged += delegate { var le = lists.SelectedItem as ListEntry; if (le != null && !le.Header && !loading) { current = le.Key; RefreshList(); } };
            left.Controls.Add(lists);
            var newList = Ui.Btn("+  New list", delegate
            {
                var n = Ui.Ask("New list", "List name", "", FindForm());
                if (string.IsNullOrEmpty(n) || n.Trim().Length == 0) return;
                current = "list:" + n.Trim(); extraList = n.Trim(); RefreshAll(); quick.Focus();
            });
            newList.Dock = DockStyle.Bottom;
            left.Controls.Add(newList);
            Controls.Add(left);
            PaneGrip.Attach(left, "tasks.lists", Ui.S(140));

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Tasks & reminders"));
            var hint = new Label { AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(0, Ui.S(9), 0, 0), Text = "Space = done · Del = delete · Ctrl+Alt+J adds a task from anywhere" };
            bar.Controls.Add(hint);
            Controls.Add(bar);

            // wiring for detail changes
            EventHandler changed = delegate { if (!loading) { dirty = true; debounce.Stop(); debounce.Start(); } };
            detail.Resize += delegate { FitNotes(); };
            detail.VisibleChanged += delegate { FitNotes(); };
            tTitle.TextChanged += changed; tNotes.TextChanged += changed;
            foreach (var c in new Control[] { dDue, dTime, dRemind }) ((DateTimePicker)c).ValueChanged += changed;
            foreach (var c in new[] { cDue, cAllDay, cRemind }) c.CheckedChanged += delegate { UpdateEnabled(); changed(null, EventArgs.Empty); };
            cbRepeat.SelectedIndexChanged += changed; cbPriority.SelectedIndexChanged += changed; cbList.TextChanged += changed;
            steps.ItemCheck += (s, e) => { if (loading || editing == null || e.Index >= editing.Steps.Count) return; editing.Steps[e.Index].Done = e.NewValue == CheckState.Checked; Store.TouchTask(editing); };
            steps.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && editing != null && steps.SelectedIndex >= 0 && steps.SelectedIndex < editing.Steps.Count) { editing.Steps.RemoveAt(steps.SelectedIndex); Store.TouchTask(editing); LoadSteps(); } };
            stepAdd.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && editing != null && stepAdd.Text.Trim().Length > 0) { e.SuppressKeyPress = true; editing.Steps.Add(new SubTask { Text = stepAdd.Text.Trim() }); stepAdd.Text = ""; Store.TouchTask(editing); LoadSteps(); } };
            debounce.Tick += delegate { debounce.Stop(); SaveDetail(); };
            Store.TasksChanged += OnTasks;
            Store.NotesChanged += OnTasks;
            Ui.Theme(this);
            ShowDetail();
        }
        string extraList;
        TableLayoutPanel fieldsPanel;
        bool dirty;          // true only after the user edited a detail control (or SetRemind)
        bool reloadDetail;   // store changed: reload the detail from the model when not dirty
        bool detailSized, fitting;

        // Let the Notes box use the free height of the detail pane.
        void FitNotes()
        {
            if (fitting || fieldsPanel == null || !fieldsPanel.Visible || detail.ClientSize.Height <= 0) return;
            fitting = true;
            try
            {
                fieldsPanel.PerformLayout();
                int other = fieldsPanel.Height - tNotes.Height;
                int h = Math.Max(Ui.S(70), detail.ClientSize.Height - other - Ui.S(8));
                if (h != tNotes.Height) { tNotes.MinimumSize = new Size(0, h); tNotes.Height = h; }
            }
            finally { fitting = false; }
        }

        Button Small(string text, EventHandler click) { var b = Ui.Btn(text, click); b.Font = Ui.Small; b.Height = Ui.S(26); b.Margin = new Padding(0, Ui.S(2), Ui.S(4), 0); return b; }

        void OnTasks() { reloadDetail = true; if (Visible) RefreshAll(); else Stale = true; }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !detailSized && Width > Ui.S(300))
            {
                detailSized = true;
                detail.Width = PaneGrip.Size("tasks.detail", DockStyle.Right, Math.Max(Ui.S(340), Width * 28 / 100), Width);
            }
            if (Visible && Stale) RefreshAll();
        }
        public void FocusQuick() { quick.Focus(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { Store.TasksChanged -= OnTasks; Store.NotesChanged -= OnTasks; debounce.Dispose(); }
            base.Dispose(disposing);
        }

        IEnumerable<TaskItem> Filter(string key)
        {
            var all = Store.Tasks;
            switch (key)
            {
                case "today": return all.Where(t => !t.Done && t.Due > 0 && new DateTime(t.Due).Date <= DateTime.Today);
                case "upcoming": return all.Where(t => !t.Done && t.Due > 0 && new DateTime(t.Due).Date > DateTime.Today && new DateTime(t.Due).Date <= DateTime.Today.AddDays(7));
                case "overdue": return all.Where(TaskParse.Overdue);
                case "reminders": return all.Where(t => !t.Done && t.RemindAt > 0);
                case "all": return all.Where(t => !t.Done);
                case "done": return all.Where(t => t.Done);
            }
            if (key.StartsWith("list:")) { string n = key.Substring(5); return all.Where(t => string.Equals(t.List, n, StringComparison.OrdinalIgnoreCase)); }
            return all;
        }

        public void RefreshAll()
        {
            Stale = false;
            loading = true;
            var entries = new List<ListEntry>();
            Action<string, string, bool> add = (k, label, red) => entries.Add(new ListEntry { Key = k, Label = label, Count = Filter(k).Count(), Red = red });
            add("today", "Today", false); add("upcoming", "Upcoming (7 days)", false); add("overdue", "Overdue", true);
            add("reminders", "Reminders", false); add("all", "All open", false); add("done", "Completed", false);
            entries.Add(new ListEntry { Key = "", Label = "MY LISTS", Header = true });
            var names = Store.TaskLists();
            if (extraList != null && !names.Any(n => string.Equals(n, extraList, StringComparison.OrdinalIgnoreCase))) names.Add(extraList);
            foreach (var n in names) add("list:" + n, n, false);
            lists.BeginUpdate(); lists.Items.Clear(); foreach (var e in entries) lists.Items.Add(e); lists.EndUpdate();
            int idx = entries.FindIndex(e => e.Key == current); if (idx < 0) { current = "today"; idx = 0; }
            lists.SelectedIndex = idx;
            cbList.Items.Clear(); foreach (var n in names) cbList.Items.Add(n);
            loading = false;
            RefreshList();
        }

        public void RefreshList()
        {
            string q = search.Text.Trim();
            var items = Filter(current);
            if (q.Length > 0) items = items.Where(t => t.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || t.Notes.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || t.List.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            var sorted = items.OrderBy(t => t.Done).ThenBy(t => t.Due == 0 ? long.MaxValue : t.Due).ThenByDescending(t => t.Priority).ThenBy(t => t.Created).Cast<object>().ToList();
            cards.SetItems(sorted);
            ShowDetail();
        }

        CardInfo Card(TaskItem t)
        {
            var ci = new CardInfo();
            ci.Title = (t.Done ? "✓ " : "") + (t.Title.Length > 0 ? t.Title : "(untitled)");
            string first = Ui.OneLine(t.Notes, 70);
            int done = t.Steps.Count(s => s.Done);
            ci.Sub = (first.Length > 0 ? first : t.List) + (t.Steps.Count > 0 ? "  ·  " + done + "/" + t.Steps.Count + " steps" : "");
            ci.Meta = TaskParse.DueText(t) + (t.Repeat != "none" ? "  ↻ " + t.Repeat : "") + (t.RemindAt > 0 ? "  ·  reminder" : "");
            // Colour carries meaning only: high priority gets a red edge, everything else none.
            if (t.Priority == 2) ci.Strip = Color.FromArgb(239, 68, 68);
            if (t.Done) ci.Fore = Ui.Muted;
            if (TaskParse.Overdue(t)) ci.SubFore = Ui.Danger;
            return ci;
        }

        void DrawList(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var le = (ListEntry)lists.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0 && !le.Header;
            using (var b = new SolidBrush(sel ? Ui.SelBg : Ui.Bg)) e.Graphics.FillRectangle(b, e.Bounds);
            var r = e.Bounds; r.Inflate(-Ui.S(10), 0);
            if (le.Header) { TextRenderer.DrawText(e.Graphics, le.Label, Ui.Small, r, Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Bottom); return; }
            if (sel) using (var b = new SolidBrush(Ui.Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Ui.S(6), Ui.S(3), e.Bounds.Height - Ui.S(12));
            TextRenderer.DrawText(e.Graphics, le.Label, sel ? Ui.Bold : Ui.Base, r, Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (le.Count > 0) TextRenderer.DrawText(e.Graphics, le.Count.ToString(), Ui.Bold, r, le.Red ? Ui.Danger : Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        List<TaskItem> Selected() { return cards.SelectedItems.Cast<object>().OfType<TaskItem>().ToList(); }

        void QuickAdd()
        {
            string text = quick.Text.Trim();
            if (text.Length == 0) return;
            var t = TaskParse.Parse(text, DateTime.Now);
            if (!text.Contains("#") && current.StartsWith("list:")) t.List = current.Substring(5);
            if (current == "today" && t.Due == 0) t.Due = DateTime.Today.Ticks;
            Store.AddTask(t);
            quick.Text = "";
            extraList = null;
        }

        void ToggleSelected() { foreach (var t in Selected()) Store.CompleteTask(t, !t.Done); }

        void DeleteSelected()
        {
            var sel = Selected(); if (sel.Count == 0) return;
            if (MessageBox.Show(FindForm(), "Delete " + sel.Count + " task(s)?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var t in sel) Store.Tasks.Remove(t);
            editing = null; dirty = false; debounce.Stop();
            Store.MarkTasks(); Store.RaiseTasks();
        }

        ContextMenuStrip BuildMenu()
        {
            var m = Ui.Menu();
            Action<string, Action<TaskItem>> each = (text, a) => m.Items.Add(text, null, delegate { foreach (var t in Selected()) a(t); Store.MarkTasks(); Store.RaiseTasks(); });
            m.Items.Add("Done / undo\tSpace", null, delegate { ToggleSelected(); });
            m.Items.Add(new ToolStripSeparator());
            each("Due today", t => { t.Due = DateTime.Today.Ticks; t.DueHasTime = false; t.Modified = Store.Now(); });
            each("Due tomorrow", t => { t.Due = DateTime.Today.AddDays(1).Ticks; t.DueHasTime = false; t.Modified = Store.Now(); });
            each("Due next week", t => { var d = DateTime.Today.AddDays(1); while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1); t.Due = d.Ticks; t.DueHasTime = false; t.Modified = Store.Now(); });
            each("No due date", t => { t.Due = 0; t.Modified = Store.Now(); });
            m.Items.Add(new ToolStripSeparator());
            each("Remind in 15 min", t => t.RemindAt = DateTime.Now.AddMinutes(15).Ticks);
            each("Remind in 1 hour", t => t.RemindAt = DateTime.Now.AddHours(1).Ticks);
            each("Remind tomorrow 9:00", t => t.RemindAt = DateTime.Today.AddDays(1).AddHours(9).Ticks);
            each("No reminder", t => t.RemindAt = 0);
            var pr = new ToolStripMenuItem("Priority");
            foreach (var p in new[] { "High", "Normal", "Low" }) { int v = p == "High" ? 2 : p == "Normal" ? 1 : 0; pr.DropDownItems.Add(p, null, delegate { foreach (var t in Selected()) t.Priority = v; Store.MarkTasks(); Store.RaiseTasks(); }); }
            m.Items.Add(pr);
            var mv = new ToolStripMenuItem("Move to list");
            m.Opening += delegate
            {
                mv.DropDownItems.Clear();
                foreach (var n in Store.TaskLists()) { string name = n; mv.DropDownItems.Add(name, null, delegate { foreach (var t in Selected()) t.List = name; Store.MarkTasks(); Store.RaiseTasks(); }); }
                mv.DropDownItems.Add("New list...", null, delegate { var n = Ui.Ask("Move to list", "List name", "", FindForm()); if (!string.IsNullOrEmpty(n)) { foreach (var t in Selected()) t.List = n.Trim(); Store.MarkTasks(); Store.RaiseTasks(); } });
            };
            m.Items.Add(mv);
            m.Items.Add("Duplicate", null, delegate { foreach (var t in Selected()) Store.AddTask(new TaskItem { Title = t.Title, Notes = t.Notes, List = t.List, Priority = t.Priority, Due = t.Due, DueHasTime = t.DueHasTime, Repeat = t.Repeat, Steps = t.Steps.Select(s => new SubTask { Text = s.Text }).ToList() }); });
            m.Items.Add("Copy text", null, delegate { App.I.CopyText(string.Join("\n", Selected().Select(t => t.Title + (t.Due > 0 ? "  (" + TaskParse.DueText(t) + ")" : "")))); });
            m.Items.Add("Make a sticky note", null, delegate { foreach (var t in Selected()) App.I.NewNote(null, t.Title + (t.Notes.Length > 0 ? "\n" + t.Notes : "")); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete\tDel", null, delegate { DeleteSelected(); });
            Ui.ThemeMenu(m);
            return m;
        }

        // ----- detail editor -----
        void ShowDetail()
        {
            var sel = Selected();
            if (sel.Count > 1) { SaveDetail(); editing = null; dirty = false; multi.Visible = true; multiLabel.Text = sel.Count + " tasks selected"; foreach (Control c in detail.Controls) if (c != multi) c.Visible = false; return; }
            multi.Visible = false; foreach (Control c in detail.Controls) if (c != multi) c.Visible = true;
            var t = sel.Count == 1 ? sel[0] : null;
            if (t == editing && t != null)
            {
                // Same task: only refresh from the model after a store change, and never over unsaved user edits.
                if (reloadDetail && !dirty) { reloadDetail = false; LoadDetail(t); }
                FitNotes();
                return;
            }
            SaveDetail();
            editing = t; dirty = false; reloadDetail = false;
            LoadDetail(t);
            FitNotes();
        }

        void LoadDetail(TaskItem t)
        {
            loading = true;
            try
            {
                bool has = t != null;
                foreach (Control c in detail.Controls) c.Enabled = has;
                if (!has) { tTitle.Text = ""; tNotes.Text = ""; steps.Items.Clear(); return; }
                tTitle.Text = t.Title; tNotes.Text = t.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n");
                cDue.Checked = t.Due > 0; cAllDay.Checked = !t.DueHasTime;
                var due = t.Due > 0 ? new DateTime(t.Due) : DateTime.Today.AddHours(9);
                dDue.Value = Clamp(due.Date); dTime.Value = Clamp(due);
                cRemind.Checked = t.RemindAt > 0; dRemind.Value = Clamp(t.RemindAt > 0 ? new DateTime(t.RemindAt) : DateTime.Now.AddHours(1));
                cbRepeat.SelectedIndex = Math.Max(0, Array.IndexOf(RepeatKeys, t.Repeat));
                cbPriority.SelectedIndex = Math.Max(0, Math.Min(2, t.Priority));
                cbList.Text = t.List;
                LoadSteps();
                UpdateEnabled();
            }
            finally { loading = false; }
        }

        static DateTime Clamp(DateTime d) { if (d < DateTimePicker.MinimumDateTime) return DateTimePicker.MinimumDateTime; if (d > DateTimePicker.MaximumDateTime) return DateTimePicker.MaximumDateTime; return d; }

        void LoadSteps()
        {
            bool was = loading; loading = true;
            steps.Items.Clear();
            if (editing != null) foreach (var s in editing.Steps) steps.Items.Add(s.Text, s.Done);
            loading = was;
        }

        void UpdateEnabled() { dDue.Enabled = cDue.Checked; cAllDay.Enabled = cDue.Checked; dTime.Enabled = cDue.Checked && !cAllDay.Checked; dRemind.Enabled = cRemind.Checked; }

        void SetRemind(DateTime when) { if (editing == null) return; loading = true; cRemind.Checked = true; dRemind.Value = Clamp(when); loading = false; UpdateEnabled(); dirty = true; SaveDetail(); }

        void SaveDetail()
        {
            debounce.Stop();
            var t = editing;
            if (t == null || !dirty) return;
            dirty = false;
            if (!Store.Tasks.Contains(t)) return;
            t.Title = tTitle.Text.Trim();
            t.Notes = tNotes.Text.Replace("\r\n", "\n");
            if (cDue.Checked) { t.DueHasTime = !cAllDay.Checked; t.Due = (dDue.Value.Date + (t.DueHasTime ? dTime.Value.TimeOfDay : TimeSpan.Zero)).Ticks; }
            else t.Due = 0;
            t.RemindAt = cRemind.Checked ? dRemind.Value.Ticks : 0;
            t.Repeat = RepeatKeys[Math.Max(0, cbRepeat.SelectedIndex)];
            t.Priority = Math.Max(0, cbPriority.SelectedIndex);
            t.List = cbList.Text.Trim().Length > 0 ? cbList.Text.Trim() : "Inbox";
            t.Modified = Store.Now();
            Store.MarkTasks();
            // Repaint the card only; a full refresh would rebuild the list while typing.
            cards.Invalidate();
        }

        public void Select(TaskItem t)
        {
            current = t.Done ? "done" : "all"; search.Text = ""; RefreshAll();
            int i = cards.Items.IndexOf(t);
            if (i >= 0) { cards.ClearSelected(); cards.SelectedIndex = i; }
        }
    }
}
