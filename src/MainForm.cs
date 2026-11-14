using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    class MainForm : Form
    {
        public bool Exiting;
        readonly NavItem navNotes, navPrompts, navClips, navSettings, navSystem, navCards;
        readonly NotesPage notes;
        readonly PromptsPage prompts;
        readonly ClipsPage clips;
        readonly SystemPage system;
        readonly CardsPage cards;
        readonly NavItem navSnip, navActivity, navTagged, navKeys;
        readonly Panel snipPage, activityPage, taggedPage;

        Panel PageWith(string title, Control inner)
        {
            var p = new Panel { Dock = DockStyle.Fill, Visible = false };
            inner.Dock = DockStyle.Fill;
            p.Controls.Add(inner);
            var bar = Bar();
            bar.Controls.Add(Heading(title));
            var pop = Ui.Btn("Pop out as floating card", null);
            string fid = title == "Snip & record" ? "snip" : title == "File activity" ? "activity" : "tagged";
            pop.Click += delegate { App.I.NewCard(new List<string> { fid }, null); };
            pop.Margin = new Padding(0, Ui.S(4), 0, 0);
            bar.Controls.Add(pop);
            p.Controls.Add(bar);
            return p;
        }
        readonly Label status, keys;
        readonly Timer statusTimer = new Timer();
        string page = "notes";
        public string Page { get { return page; } }

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = App.Name;
            Icon = App.AppIcon;
            Font = Ui.Base;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Size = Ui.S(1120, 700);
            MinimumSize = Ui.S(800, 480);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Ui.DarkTitle(this);

            var content = new Panel();
            content.Dock = DockStyle.Fill;
            content.Padding = new Padding(Ui.S(12), Ui.S(10), Ui.S(12), Ui.S(4));
            notes = new NotesPage(this);
            prompts = new PromptsPage(this);
            clips = new ClipsPage(this);
            system = new SystemPage(this);
            cards = new CardsPage(this);
            system.Visible = false;
            cards.Visible = false;
            content.Controls.Add(notes);
            content.Controls.Add(prompts);
            content.Controls.Add(clips);
            content.Controls.Add(system);
            content.Controls.Add(cards);
            Action<string> st = s => Status(s);
            snipPage = PageWith("Snip & record", new SnipPanel(st));
            activityPage = PageWith("File activity", new ActivityPanel(st));
            taggedPage = PageWith("Tagged files", new TaggedPanel(st));
            content.Controls.Add(snipPage);
            content.Controls.Add(activityPage);
            content.Controls.Add(taggedPage);
            Controls.Add(content);

            status = new Label();
            status.Dock = DockStyle.Bottom;
            status.Height = Ui.S(26);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(Ui.S(14), 0, 0, 0);
            status.ForeColor = Ui.Muted;
            status.Font = Ui.Small;
            Controls.Add(status);
            statusTimer.Interval = 4000;
            statusTimer.Tick += delegate { statusTimer.Stop(); status.Text = ""; };

            var nav = new Panel();
            nav.Dock = DockStyle.Left;
            nav.Width = Ui.S(230);
            nav.BackColor = Ui.Nav;
            nav.Padding = new Padding(0, Ui.S(6), 0, Ui.S(6));

            keys = new Label();
            UpdateShortcutHelp();

            navSettings = new NavItem(Ui.GSettings, "Settings");
            navSettings.Dock = DockStyle.Bottom;
            navSettings.Click += delegate { App.I.ShowSettings(this); };
            nav.Controls.Add(navSettings);
            navKeys = new NavItem("", "Shortcuts");
            navKeys.Dock = DockStyle.Bottom;
            navKeys.Click += delegate { MessageBox.Show(this, keys.Text + "\n\nChange any of them in Settings.", App.Name + " - shortcuts"); };
            nav.Controls.Add(navKeys);

            navSnip = new NavItem("", "Snip & record");
            navActivity = new NavItem("", "File activity");
            navTagged = new NavItem("", "Tagged files");
            navSnip.Click += delegate { ShowPage("snip"); };
            navActivity.Click += delegate { ShowPage("activity"); };
            navTagged.Click += delegate { ShowPage("tagged"); };
            nav.Controls.Add(navTagged);
            nav.Controls.Add(navActivity);
            nav.Controls.Add(navSnip);

            navClips = new NavItem(Ui.GClip, "Clipboard history");
            navPrompts = new NavItem(Ui.GPrompt, "Prompts & snippets");
            navNotes = new NavItem(Ui.GNote, "Sticky notes");
            navSystem = new NavItem("", "System monitor");
            navCards = new NavItem("", "Floating cards");
            navClips.Click += delegate { ShowPage("clips"); };
            navPrompts.Click += delegate { ShowPage("prompts"); };
            navNotes.Click += delegate { ShowPage("notes"); };
            navSystem.Click += delegate { ShowPage("system"); };
            navCards.Click += delegate { ShowPage("cards"); };
            nav.Controls.Add(navCards);
            nav.Controls.Add(navSystem);
            nav.Controls.Add(navClips);
            nav.Controls.Add(navPrompts);
            nav.Controls.Add(navNotes);

            var title = new Label();
            title.Dock = DockStyle.Top;
            title.Height = Ui.S(52);
            title.Text = "        " + App.Name;
            title.Font = Ui.Big;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Image = Ui.Logo(Ui.S(26));
            title.ImageAlign = ContentAlignment.MiddleLeft;
            title.Padding = new Padding(Ui.S(14), 0, 0, 0);
            nav.Controls.Add(title);
            Controls.Add(nav);

            Ui.Theme(this);
            Store.NotesChanged += OnNotes;
            Store.PromptsChanged += OnPrompts;
            Store.ClipsChanged += OnClips;
            ShowPage("notes");
        }

        public void UpdateShortcutHelp()
        {
            var lines = new List<string> { "SHORTCUTS (work in any app)" };
            foreach (var a in Hotkeys.Actions)
            {
                string k = Hotkeys.Get(a[0]);
                if (k.Length > 0) lines.Add(k + "   " + a[3]);
            }
            keys.Text = string.Join("\n", lines);
        }

        void OnNotes() { if (Visible && page == "notes") notes.RefreshList(); else notes.Stale = true; }
        void OnPrompts() { if (Visible && page == "prompts") prompts.RefreshAll(); else prompts.Stale = true; }
        void OnClips() { if (Visible && page == "clips") clips.RefreshList(); else clips.Stale = true; }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) RefreshCurrent();
        }

        void RefreshCurrent()
        {
            if (page == "notes" && notes.Stale) notes.RefreshList();
            if (page == "prompts" && prompts.Stale) prompts.RefreshAll();
            if (page == "clips" && clips.Stale) clips.RefreshList();
            if (page == "cards") cards.RefreshList();
        }

        public void ShowPage(string p)
        {
            if (page == "prompts" && p != "prompts") prompts.Commit();
            if (p.StartsWith("system:")) { system.SelectTab(p.Substring(7)); p = "system"; }
            page = p;
            navNotes.Selected = p == "notes";
            navPrompts.Selected = p == "prompts";
            navClips.Selected = p == "clips";
            navSystem.Selected = p == "system";
            navCards.Selected = p == "cards";
            notes.Visible = p == "notes";
            prompts.Visible = p == "prompts";
            clips.Visible = p == "clips";
            system.Visible = p == "system";
            cards.Visible = p == "cards";
            navSnip.Selected = p == "snip";
            navActivity.Selected = p == "activity";
            navTagged.Selected = p == "tagged";
            snipPage.Visible = p == "snip";
            activityPage.Visible = p == "activity";
            taggedPage.Visible = p == "tagged";
            RefreshCurrent();
            FocusSearch();
        }

        public void FocusSearch()
        {
            if (page == "notes") notes.FocusSearch();
            else if (page == "prompts") prompts.FocusSearch();
            else if (page == "clips") clips.FocusSearch();
            else if (page == "cards") cards.FocusSearch();
        }

        public void UpdatePaused() { clips.UpdatePaused(); }
        public void CommitEdits() { prompts.Commit(); }

        public void Status(string text)
        {
            status.Text = text;
            statusTimer.Stop();
            statusTimer.Start();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && !e.Alt)
            {
                if (e.KeyCode == Keys.D1) { ShowPage("notes"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D2) { ShowPage("prompts"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D3) { ShowPage("clips"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D4) { ShowPage("system"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D5) { ShowPage("cards"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D6) { ShowPage("snip"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D7) { ShowPage("activity"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.D8) { ShowPage("tagged"); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.F) { FocusSearch(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.N && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    if (page == "prompts") prompts.NewPrompt(); else App.I.NewNote(null);
                }
            }
            else if (e.KeyCode == Keys.Escape && !e.Control) { Hide(); e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            prompts.Commit();
            if (!Exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                Ui.Later(1500, App.I.Trim);
                return;
            }
            Store.NotesChanged -= OnNotes;
            Store.PromptsChanged -= OnPrompts;
            Store.ClipsChanged -= OnClips;
            base.OnFormClosing(e);
        }

        public static FlowLayoutPanel Bar()
        {
            var bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top;
            bar.Height = Ui.S(40);
            bar.WrapContents = false;
            bar.Padding = new Padding(0, Ui.S(2), 0, Ui.S(4));
            return bar;
        }

        public static Label Heading(string text)
        {
            var l = new Label();
            l.Text = text;
            l.UseMnemonic = false;
            l.Font = Ui.Big;
            l.AutoSize = true;
            l.Margin = new Padding(0, 0, Ui.S(14), 0);
            return l;
        }

        public static ComboBox Combo(int width, params object[] items)
        {
            var c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = Ui.S(width);
            c.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            c.Items.AddRange(items);
            if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        // Keeps the selected entry of a combo while replacing its items.
        public static void Reload(ComboBox c, IEnumerable<string> fixedItems, IEnumerable<string> items)
        {
            string cur = c.SelectedItem as string;
            c.BeginUpdate();
            c.Items.Clear();
            foreach (var i in fixedItems) c.Items.Add(i);
            foreach (var i in items) c.Items.Add(i);
            int idx = cur == null ? 0 : c.Items.IndexOf(cur);
            c.SelectedIndex = idx < 0 ? 0 : idx;
            c.EndUpdate();
        }
    }

    // ===================== Notes =====================

    class NotesPage : Panel
    {
        public bool Stale = true;
        readonly MainForm owner;
        readonly TextBox search;
        readonly ComboBox view, colorFilter, tagFilter;
        readonly CardList list;
        bool reloading;

        public NotesPage(MainForm owner)
        {
            this.owner = owner;
            Dock = DockStyle.Fill;

            list = new CardList();
            list.Dock = DockStyle.Fill;
            list.ItemHeight = Math.Min(255, Ui.S(78));
            list.Info = o => NoteInfo((Note)o);
            list.DoubleClick += delegate { OpenSelected(); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { OpenSelected(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
            };
            list.ContextMenuStrip = BuildMenu();
            Controls.Add(list);

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Sticky notes"));
            search = Ui.Search("Search notes and tags  (Ctrl+F)");
            search.Width = Ui.S(200);
            search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            search.TextChanged += delegate { RefreshList(); };
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.SuppressKeyPress = true; } };
            bar.Controls.Add(search);
            view = MainForm.Combo(120, "Active notes", "With reminder", "Archived", "All notes");
            view.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(view);
            colorFilter = MainForm.Combo(100, "All colors");
            foreach (var c in Ui.Colors) colorFilter.Items.Add(c.Name);
            colorFilter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(colorFilter);
            tagFilter = MainForm.Combo(105, "All tags");
            tagFilter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(tagFilter);
            bar.Controls.Add(Ui.Btn("+  New note", delegate { App.I.NewNote(null); }, true));
            Controls.Add(bar);
        }

        CardInfo NoteInfo(Note n)
        {
            var c = Ui.ColorOf(n.Color);
            var ci = new CardInfo();
            string text = (n.Text ?? "").Trim();
            ci.Title = text.Length == 0 ? "(empty note)" : Store.TitleFrom(text);
            int nl = text.IndexOf('\n');
            ci.Sub = nl > 0 ? Ui.OneLine(text.Substring(nl + 1), 220) : "";
            var meta = new List<string>();
            if (n.RemindAt > 0) meta.Add("Reminder " + Ui.Ago(n.RemindAt));
            if (n.Tags.Trim().Length > 0) meta.Add("#" + string.Join(" #", Store.SplitTags(n.Tags)));
            if (n.Archived) meta.Add("archived");
            else if (n.Open) meta.Add("open");
            meta.Add(Ui.Ago(n.Modified));
            ci.Meta = string.Join("  ·  ", meta);
            ci.Back = c.Body;
            ci.Strip = c.Header;
            ci.Fore = c.Text;
            ci.SubFore = c.Name == "Charcoal" ? Color.FromArgb(200, 200, 200) : Color.FromArgb(105, 105, 105);
            return ci;
        }

        public void FocusSearch() { search.Focus(); search.SelectAll(); }

        public void RefreshList()
        {
            Stale = false;
            reloading = true;
            MainForm.Reload(tagFilter, new[] { "All tags" }, Store.NoteTags());
            reloading = false;
            string q = search.Text.Trim();
            string col = colorFilter.SelectedIndex > 0 ? (string)colorFilter.SelectedItem : null;
            string tag = tagFilter.SelectedIndex > 0 ? (string)tagFilter.SelectedItem : null;
            int v = view.SelectedIndex;
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var items = Store.Notes
                .Where(n => v == 3 || (v == 2 ? n.Archived : v == 1 ? n.RemindAt > 0 : !n.Archived))
                .Where(n => words.All(w => ((n.Text ?? "") + "\n" + n.Tags).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(n => col == null || n.Color == col)
                .Where(n => tag == null || Store.SplitTags(n.Tags).Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(n => v == 1 ? -n.RemindAt : n.Modified)
                .Cast<object>();
            list.SetItems(items);
        }

        Note Sel() { return list.SelectedItem as Note; }

        void OpenSelected() { var n = Sel(); if (n != null) App.I.OpenNote(n); }

        void DeleteSelected()
        {
            var n = Sel();
            if (n == null) return;
            if (MessageBox.Show(owner, "Delete this note for good?\n\n" + Ui.OneLine(n.Text, 80) + "\n\nTip: Archive keeps it but hides it.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                App.I.DeleteNote(n);
        }

        ContextMenuStrip BuildMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("Open", null, delegate { OpenSelected(); });
            var colors = new ToolStripMenuItem("Color");
            foreach (var c in Ui.Colors)
            {
                var col = c;
                colors.DropDownItems.Add(c.Name, Ui.Swatch(c.Header, Ui.S(16)), delegate
                {
                    var n = Sel(); if (n == null) return;
                    n.Color = col.Name; n.Modified = Store.Now(); Store.MarkNotes(); App.I.RefreshNoteColor(n); Store.RaiseNotes();
                });
            }
            m.Items.Add(colors);
            m.Items.Add("Tags...", null, delegate
            {
                var n = Sel(); if (n == null) return;
                var t = Ui.Ask("Note tags", "Tags, separated by commas (for example: work, ideas):", n.Tags, owner);
                if (t == null) return;
                n.Tags = t.Trim(); Store.MarkNotes(); Store.RaiseNotes();
            });
            m.Items.Add("Remind me...", null, delegate
            {
                var n = Sel(); if (n == null) return;
                long r = Ui.AskReminder(n.RemindAt, owner);
                if (r < 0) return;
                n.RemindAt = r; Store.MarkNotes(); Store.RaiseNotes();
                if (r > 0) owner.Status("Reminder set for " + new DateTime(r).ToString("ddd d MMM HH:mm") + ". The note pops up then.");
            });
            m.Items.Add("Archive / unarchive", null, delegate
            {
                var n = Sel(); if (n == null) return;
                App.I.ArchiveNote(n, !n.Archived);
                owner.Status(n.Archived ? "Archived. Find it under \"Archived\"." : "Back in active notes.");
            });
            m.Items.Add("Copy text", null, delegate { var n = Sel(); if (n != null && App.I.CopyText(n.Text)) owner.Status("Note text copied."); });
            m.Items.Add("Save as prompt", null, delegate
            {
                var n = Sel(); if (n == null || (n.Text ?? "").Trim().Length == 0) return;
                Store.NewPrompt(Store.TitleFrom(n.Text), n.Text.Trim(), "From notes");
                owner.Status("Saved to Prompts > From notes.");
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete", null, delegate { DeleteSelected(); });
            Ui.ThemeMenu(m);
            return m;
        }
    }

    // ===================== Prompts =====================

    class CatItem
    {
        public string Key, Label;
        public int Count;
        public override string ToString() { return Label; }
    }

    class PromptsPage : Panel
    {
        public bool Stale = true;
        readonly MainForm owner;
        readonly ListBox cats;
        readonly CardList list;
        readonly TextBox search, tbTitle, tbTags, tbBody, tbTrigger;
        readonly ComboBox cbCat, sort;
        readonly Button btnFav, btnHistory;
        readonly TableLayoutPanel editor;
        readonly Timer catTimer = new Timer();
        Prompt cur;
        bool loading;
        string catKey = "*all";
        string loadedTitle, loadedBody;

        public PromptsPage(MainForm owner)
        {
            this.owner = owner;
            Dock = DockStyle.Fill;

            // ---- editor (fill) ----
            editor = new TableLayoutPanel();
            editor.Dock = DockStyle.Fill;
            editor.BackColor = Ui.Card;
            editor.Padding = new Padding(Ui.S(14), Ui.S(10), Ui.S(14), Ui.S(10));
            editor.ColumnCount = 1;
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tbTitle = new TextBox(); tbTitle.Font = Ui.Bold;
            cbCat = new ComboBox(); cbCat.DropDownStyle = ComboBoxStyle.DropDown;
            tbTags = new TextBox();
            tbTrigger = new TextBox();
            Ui.Cue(tbTrigger, "e.g.  ;sig   (type this anywhere and it becomes the prompt text)");
            tbBody = new TextBox(); tbBody.Multiline = true; tbBody.ScrollBars = ScrollBars.Vertical; tbBody.AcceptsReturn = true; tbBody.AcceptsTab = true;
            tbBody.Font = new Font(Store.Settings.NoteFontName, 10.5f); tbBody.WordWrap = true;
            foreach (var c in new Control[] { tbTitle, cbCat, tbTags, tbTrigger, tbBody }) { c.Dock = DockStyle.Fill; c.Margin = new Padding(0, 0, 0, Ui.S(6)); }
            AddRow(FieldLabel("Title"), SizeType.AutoSize);
            AddRow(tbTitle, SizeType.AutoSize);
            var two = new TableLayoutPanel(); two.ColumnCount = 2; two.RowCount = 2; two.Dock = DockStyle.Fill; two.AutoSize = true; two.Margin = new Padding(0);
            two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); two.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            two.Controls.Add(FieldLabel("Category"), 0, 0); two.Controls.Add(FieldLabel("Snippet code (optional)"), 1, 0);
            cbCat.Margin = new Padding(0, 0, Ui.S(8), Ui.S(6));
            two.Controls.Add(cbCat, 0, 1); two.Controls.Add(tbTrigger, 1, 1);
            AddRow(two, SizeType.AutoSize);
            AddRow(FieldLabel("Tags  (comma separated)"), SizeType.AutoSize);
            AddRow(tbTags, SizeType.AutoSize);
            AddRow(FieldLabel("Prompt text  -  use {{name}} for blanks you fill in when you use it"), SizeType.AutoSize);
            AddRow(tbBody, SizeType.Percent);
            var actions = new FlowLayoutPanel(); actions.AutoSize = true; actions.Dock = DockStyle.Fill; actions.WrapContents = true; actions.Margin = new Padding(0);
            btnFav = Ui.Btn("Favorite", delegate { ToggleFav(); });
            btnHistory = Ui.Btn("History", delegate { ShowHistory(); });
            actions.Controls.Add(Ui.Btn("Copy  (Ctrl+Enter)", delegate { CopyCurrent(); }, true));
            actions.Controls.Add(btnFav);
            actions.Controls.Add(btnHistory);
            actions.Controls.Add(Ui.Btn("Duplicate", delegate { Duplicate(); }));
            actions.Controls.Add(Ui.Btn("Delete", delegate { DeleteCurrent(); }));
            AddRow(actions, SizeType.AutoSize);
            tbTitle.TextChanged += delegate { Edited(); };
            cbCat.TextChanged += delegate { Edited(); catTimer.Stop(); catTimer.Start(); };
            tbTags.TextChanged += delegate { Edited(); };
            tbTrigger.TextChanged += delegate { Edited(); catTimer.Stop(); catTimer.Start(); };
            tbBody.TextChanged += delegate { Edited(); CheckBodyDirection(); };
            tbBody.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.A) { tbBody.SelectAll(); e.SuppressKeyPress = true; } };
            KeyEventHandler ctrlEnter = (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { CopyCurrent(); e.SuppressKeyPress = true; } };
            tbBody.KeyDown += ctrlEnter; tbTitle.KeyDown += ctrlEnter; tbTags.KeyDown += ctrlEnter;
            catTimer.Interval = 900;
            catTimer.Tick += delegate { catTimer.Stop(); RefreshCats(); Snippets.Update(); };
            Controls.Add(editor);

            // ---- middle: search + list ----
            var mid = new Panel(); mid.Dock = DockStyle.Left; mid.Width = Ui.S(340); mid.Padding = new Padding(Ui.S(8), 0, Ui.S(8), 0);
            list = new CardList(); list.Dock = DockStyle.Fill;
            list.Info = o => PromptInfo((Prompt)o);
            list.SelectedIndexChanged += delegate { LoadEditor(list.SelectedItem as Prompt); };
            list.DoubleClick += delegate { CopyCurrent(); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CopyCurrent(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteCurrent(); e.SuppressKeyPress = true; }
            };
            mid.Controls.Add(list);
            var midBar = new FlowLayoutPanel(); midBar.Dock = DockStyle.Top; midBar.Height = Ui.S(38); midBar.WrapContents = false;
            midBar.Controls.Add(Ui.Btn("+  New prompt", delegate { NewPrompt(); }, true));
            sort = MainForm.Combo(130, "Recent", "Most used", "A to Z");
            sort.Margin = new Padding(0, Ui.S(3), 0, 0);
            sort.SelectedIndexChanged += delegate { RefreshList(); };
            midBar.Controls.Add(sort);
            mid.Controls.Add(midBar);
            search = Ui.Search("Search title, text, tags, code  (Ctrl+F)");
            search.Dock = DockStyle.Top;
            search.TextChanged += delegate { RefreshList(); };
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.SuppressKeyPress = true; } };
            var searchWrap = new Panel(); searchWrap.Dock = DockStyle.Top; searchWrap.Height = Ui.S(34); searchWrap.Padding = new Padding(0, Ui.S(2), 0, Ui.S(6));
            searchWrap.Controls.Add(search);
            mid.Controls.Add(searchWrap);
            Controls.Add(mid);

            // ---- left: categories ----
            var left = new Panel(); left.Dock = DockStyle.Left; left.Width = Ui.S(200);
            cats = new ListBox();
            cats.Dock = DockStyle.Fill;
            cats.BorderStyle = BorderStyle.None;
            cats.DrawMode = DrawMode.OwnerDrawFixed;
            cats.ItemHeight = Ui.S(30);
            cats.IntegralHeight = false;
            cats.DrawItem += DrawCat;
            cats.SelectedIndexChanged += delegate { var c = cats.SelectedItem as CatItem; if (c != null && c.Key != catKey) { catKey = c.Key; RefreshList(); } };
            var catMenu = Ui.Menu();
            catMenu.Items.Add("Rename category", null, delegate { RenameCategory(); });
            Ui.ThemeMenu(catMenu);
            cats.ContextMenuStrip = catMenu;
            cats.MouseDown += (s, e) => { int i = cats.IndexFromPoint(e.Location); if (i >= 0 && e.Button == MouseButtons.Right) cats.SelectedIndex = i; };
            left.Controls.Add(cats);
            var io = new FlowLayoutPanel(); io.Dock = DockStyle.Bottom; io.Height = Ui.S(36); io.WrapContents = false;
            io.Controls.Add(Ui.Btn("Import", delegate { Import(); }));
            io.Controls.Add(Ui.Btn("Export", delegate { Export(); }));
            left.Controls.Add(io);
            var head = MainForm.Heading("Prompts"); head.Dock = DockStyle.Top; head.AutoSize = false; head.Height = Ui.S(40);
            left.Controls.Add(head);
            Controls.Add(left);

            LoadEditor(null);
        }

        void AddRow(Control c, SizeType st)
        {
            editor.RowStyles.Add(st == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            editor.Controls.Add(c, 0, editor.RowStyles.Count - 1);
            editor.RowCount = editor.RowStyles.Count;
        }

        static Label FieldLabel(string t)
        {
            var l = new Label(); l.Text = t; l.AutoSize = true; l.ForeColor = Ui.Muted; l.Font = Ui.Small; l.Margin = new Padding(0, Ui.S(4), 0, Ui.S(2));
            return l;
        }

        void DrawCat(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var c = (CatItem)cats.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Ui.SelBg : Ui.Bg)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Ui.Accent)) e.Graphics.FillRectangle(b, e.Bounds.X, e.Bounds.Y + Ui.S(6), Ui.S(3), e.Bounds.Height - Ui.S(12));
            var r = new Rectangle(e.Bounds.X + Ui.S(12), e.Bounds.Y, e.Bounds.Width - Ui.S(54), e.Bounds.Height);
            var dir = Ui.IsRtl(c.Label) ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
            TextRenderer.DrawText(e.Graphics, c.Label, c.Key.StartsWith("*") ? Ui.Bold : Ui.Base, r, Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | dir);
            var rc = new Rectangle(e.Bounds.Right - Ui.S(44), e.Bounds.Y, Ui.S(36), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, c.Count.ToString(), Ui.Small, rc, Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        CardInfo PromptInfo(Prompt p)
        {
            var ci = new CardInfo();
            ci.Title = (p.Fav ? "★ " : "") + (p.Title.Trim().Length > 0 ? p.Title : "(untitled)");
            ci.Sub = Ui.OneLine(p.Body, 200);
            var meta = new List<string>();
            if (p.Trigger.Trim().Length > 0) meta.Add(p.Trigger.Trim());
            if (p.Category.Trim().Length > 0) meta.Add(p.Category.Trim());
            if (p.Uses > 0) meta.Add(p.Uses + "x");
            ci.Meta = string.Join("  ·  ", meta);
            ci.Strip = p.Fav ? Ui.PinStrip : Color.Empty;
            return ci;
        }

        public void FocusSearch() { search.Focus(); search.SelectAll(); }

        public void RefreshAll() { Stale = false; RefreshCats(); RefreshList(); }

        void RefreshCats()
        {
            var items = new List<CatItem>();
            items.Add(new CatItem { Key = "*all", Label = "All prompts", Count = Store.Prompts.Count });
            items.Add(new CatItem { Key = "*fav", Label = "★ Favorites", Count = Store.Prompts.Count(p => p.Fav) });
            items.Add(new CatItem { Key = "*used", Label = "Most used", Count = Store.Prompts.Count(p => p.Uses > 0) });
            items.Add(new CatItem { Key = "*snip", Label = "Snippets (with code)", Count = Store.Prompts.Count(p => p.Trigger.Trim().Length > 0) });
            foreach (var g in Store.Prompts.GroupBy(p => p.Category.Trim(), StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key == "" ? "￿" : g.Key, StringComparer.OrdinalIgnoreCase))
                items.Add(new CatItem { Key = g.Key, Label = g.Key == "" ? "(no category)" : g.Key, Count = g.Count() });
            cats.BeginUpdate();
            cats.Items.Clear();
            foreach (var i in items) cats.Items.Add(i);
            int idx = items.FindIndex(i => string.Equals(i.Key, catKey, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) { idx = 0; catKey = "*all"; }
            cats.SelectedIndex = idx;
            cats.EndUpdate();

            string typed = cbCat.Text;
            bool wasLoading = loading; loading = true;
            cbCat.BeginUpdate();
            cbCat.Items.Clear();
            foreach (var i in items.Where(i => !i.Key.StartsWith("*") && i.Key != "")) cbCat.Items.Add(i.Key);
            cbCat.EndUpdate();
            if (cbCat.Text != typed) { cbCat.Text = typed; cbCat.SelectionStart = typed.Length; }
            loading = wasLoading;
        }

        void RefreshList()
        {
            string q = search.Text.Trim();
            IEnumerable<Prompt> src = Store.Prompts;
            if (catKey == "*fav") src = src.Where(p => p.Fav);
            else if (catKey == "*used") src = src.Where(p => p.Uses > 0);
            else if (catKey == "*snip") src = src.Where(p => p.Trigger.Trim().Length > 0);
            else if (catKey != "*all") src = src.Where(p => string.Equals(p.Category.Trim(), catKey, StringComparison.OrdinalIgnoreCase));
            if (q.Length > 0)
            {
                var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                src = src.Where(p => words.All(w => (p.Title + "\n" + p.Body + "\n" + p.Tags + "\n" + p.Category + "\n" + p.Trigger).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            if (sort.SelectedIndex == 1 || catKey == "*used") src = src.OrderByDescending(p => p.Uses).ThenByDescending(p => p.LastUsed);
            else if (sort.SelectedIndex == 2) src = src.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase);
            else src = src.OrderByDescending(p => Math.Max(p.Modified, p.LastUsed));
            list.SetItems(src.Cast<object>());
            if (list.SelectedItem == null && cur != null && !list.Items.Contains(cur)) LoadEditor(null);
        }

        void LoadEditor(Prompt p)
        {
            if (p == cur && p != null) return;
            Commit();
            cur = p;
            loading = true;
            editor.Enabled = p != null;
            tbTitle.Text = p != null ? p.Title : "";
            cbCat.Text = p != null ? p.Category : "";
            tbTags.Text = p != null ? p.Tags : "";
            tbTrigger.Text = p != null ? p.Trigger : "";
            tbBody.Text = p != null ? (p.Body ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            loadedTitle = p != null ? p.Title : null;
            loadedBody = p != null ? p.Body : null;
            UpdateButtons();
            loading = false;
            CheckBodyDirection();
        }

        void UpdateButtons()
        {
            btnFav.Text = cur != null && cur.Fav ? "★ Favorite" : "☆ Favorite";
            btnHistory.Text = cur != null && cur.Versions.Count > 0 ? "History (" + cur.Versions.Count + ")" : "History";
        }

        void CheckBodyDirection()
        {
            var want = Ui.IsRtl(tbBody.Text) ? RightToLeft.Yes : RightToLeft.No;
            if (tbBody.RightToLeft != want) BeginInvokeSafe(() => { if (tbBody.RightToLeft != want) tbBody.RightToLeft = want; });
            var wantT = Ui.IsRtl(tbTitle.Text) ? RightToLeft.Yes : RightToLeft.No;
            if (tbTitle.RightToLeft != wantT) BeginInvokeSafe(() => { if (tbTitle.RightToLeft != wantT) tbTitle.RightToLeft = wantT; });
        }

        void BeginInvokeSafe(Action a)
        {
            if (IsHandleCreated) BeginInvoke(a); else a();
        }

        bool dirty;
        void Edited()
        {
            if (loading || cur == null) return;
            cur.Title = tbTitle.Text;
            cur.Category = cbCat.Text.Trim();
            cur.Tags = tbTags.Text;
            cur.Trigger = tbTrigger.Text.Trim();
            cur.Body = tbBody.Text;
            cur.Modified = Store.Now();
            dirty = true;
            Store.MarkPrompts();
            list.Invalidate();
        }

        // Saves a version of the previous text when the prompt text was changed.
        public void Commit()
        {
            if (cur != null && loadedBody != null && (cur.Body != loadedBody || cur.Title != loadedTitle) && loadedBody.Trim().Length > 0)
            {
                cur.Versions.Insert(0, new PromptVersion { Title = loadedTitle, Body = loadedBody, Time = Store.Now() });
                while (cur.Versions.Count > 30) cur.Versions.RemoveAt(cur.Versions.Count - 1);
                loadedBody = cur.Body;
                loadedTitle = cur.Title;
                Store.MarkPrompts();
                UpdateButtons();
            }
            if (dirty) { dirty = false; Store.MarkPrompts(); }
        }

        public void NewPrompt()
        {
            string cat = catKey.StartsWith("*") ? "" : catKey;
            search.Text = "";
            var p = Store.NewPrompt("New prompt", "", cat);
            if (catKey.StartsWith("*")) catKey = "*all";
            RefreshAll();
            list.SelectedItem = p;
            tbTitle.Focus();
            tbTitle.SelectAll();
        }

        void CopyCurrent()
        {
            if (cur == null) return;
            Commit();
            var text = Ui.FillVariables(cur.Body, owner);
            if (text == null) return;
            if (App.I.CopyText(text))
            {
                cur.Uses++; cur.LastUsed = Store.Now();
                Store.MarkPrompts();
                list.Invalidate();
                owner.Status("Prompt copied. Paste it with Ctrl+V.");
            }
            else owner.Status("Nothing to copy - the prompt text is empty.");
        }

        void ToggleFav()
        {
            if (cur == null) return;
            cur.Fav = !cur.Fav;
            UpdateButtons();
            Store.MarkPrompts();
            RefreshCats();
            list.Invalidate();
        }

        void ShowHistory()
        {
            if (cur == null) return;
            Commit();
            if (cur.Versions.Count == 0) { owner.Status("No older versions yet. A version is saved each time you change the text and move on."); return; }
            using (var f = Ui.Dialog("Versions of \"" + Ui.OneLine(cur.Title, 40) + "\"", 760, 460))
            {
                f.FormBorderStyle = FormBorderStyle.Sizable;
                f.MinimumSize = Ui.S(500, 300);
                var lb = new ListBox(); lb.Dock = DockStyle.Left; lb.Width = Ui.S(240); lb.IntegralHeight = false; lb.Font = Ui.Base;
                foreach (var v in cur.Versions) lb.Items.Add(Ui.Ago(v.Time) + "  -  " + Ui.OneLine(v.Title, 30));
                var tb = new TextBox(); tb.Multiline = true; tb.ReadOnly = true; tb.ScrollBars = ScrollBars.Vertical; tb.Dock = DockStyle.Fill; tb.Font = new Font("Segoe UI", 10f);
                var bar = new FlowLayoutPanel(); bar.Dock = DockStyle.Bottom; bar.Height = Ui.S(42); bar.FlowDirection = FlowDirection.RightToLeft; bar.Padding = new Padding(Ui.S(6));
                var restore = Ui.Btn("Restore this version", null, true); restore.DialogResult = DialogResult.OK;
                var close = Ui.Btn("Close", null); close.DialogResult = DialogResult.Cancel;
                bar.Controls.Add(restore); bar.Controls.Add(close);
                lb.SelectedIndexChanged += delegate
                {
                    if (lb.SelectedIndex < 0) return;
                    var v = cur.Versions[lb.SelectedIndex];
                    tb.Text = (v.Body ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
                    tb.RightToLeft = Ui.IsRtl(tb.Text) ? RightToLeft.Yes : RightToLeft.No;
                };
                f.Controls.Add(tb); f.Controls.Add(lb); f.Controls.Add(bar);
                f.CancelButton = close;
                Ui.Theme(f);
                lb.SelectedIndex = 0;
                if (f.ShowDialog(owner) != DialogResult.OK || lb.SelectedIndex < 0) return;
                var chosen = cur.Versions[lb.SelectedIndex];
                // Keep the current text as a version too, so restoring can be undone.
                cur.Versions.Insert(0, new PromptVersion { Title = cur.Title, Body = cur.Body, Time = Store.Now() });
                cur.Title = chosen.Title; cur.Body = chosen.Body; cur.Modified = Store.Now();
                Store.MarkPrompts();
                var p = cur; cur = null; loadedBody = null;
                LoadEditor(p);
                list.Invalidate();
                owner.Status("Version restored. The text before it was kept in History.");
            }
        }

        void Duplicate()
        {
            if (cur == null) return;
            var original = cur; // NewPrompt raises a refresh that temporarily clears cur.
            var p = Store.NewPrompt(original.Title + " (copy)", original.Body, original.Category);
            p.Tags = original.Tags;
            RefreshAll();
            list.SelectedItem = p;
        }

        void DeleteCurrent()
        {
            if (cur == null) return;
            if (MessageBox.Show(owner, "Delete prompt \"" + cur.Title + "\"?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var p = cur;
            int idx = list.SelectedIndex;
            cur = null; loadedBody = null;
            Store.Prompts.Remove(p);
            Store.MarkPrompts();
            Store.RaisePrompts();
            RefreshAll();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(Math.Max(0, idx), list.Items.Count - 1);
            else LoadEditor(null);
        }

        void RenameCategory()
        {
            var c = cats.SelectedItem as CatItem;
            if (c == null || c.Key.StartsWith("*")) return;
            var name = Ui.Ask("Rename category", "New name for \"" + c.Label + "\":", c.Key, owner);
            if (name == null) return;
            name = name.Trim();
            foreach (var p in Store.Prompts.Where(p => string.Equals(p.Category.Trim(), c.Key, StringComparison.OrdinalIgnoreCase))) p.Category = name;
            catKey = name.Length == 0 ? "*all" : name;
            Store.MarkPrompts();
            RefreshAll();
            var keep = list.SelectedItem as Prompt; cur = null; loadedBody = null;
            LoadEditor(keep);
        }

        void Export()
        {
            using (var d = new SaveFileDialog())
            {
                d.Filter = "Prompts (*.json)|*.json";
                d.FileName = "prompts-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json";
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, Store.ExportPrompts(Store.Prompts), new System.Text.UTF8Encoding(false));
                owner.Status("Exported " + Store.Prompts.Count + " prompts.");
            }
        }

        void Import()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "Prompts (*.json)|*.json|Text files (*.txt;*.md)|*.txt;*.md";
                d.Multiselect = true;
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                int n = 0;
                foreach (var f in d.FileNames)
                {
                    try
                    {
                        if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n += Store.ImportPrompts(File.ReadAllText(f)).Count;
                        else { Store.NewPrompt(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f), "Imported"); n++; }
                    }
                    catch (Exception ex) { MessageBox.Show(owner, "Could not import " + Path.GetFileName(f) + ":\n" + ex.Message, App.Name); }
                }
                RefreshAll();
                owner.Status("Imported " + n + " prompts.");
            }
        }
    }

    // ===================== Clipboard history =====================

    class ClipsPage : Panel
    {
        public bool Stale = true;
        readonly MainForm owner;
        readonly TextBox search, preview;
        readonly ComboBox filter, folderFilter;
        readonly CardList list;
        readonly CheckBox pause;
        readonly PictureBox picture;
        readonly Label previewMeta;
        bool reloading;

        public ClipsPage(MainForm owner)
        {
            this.owner = owner;
            Dock = DockStyle.Fill;

            list = new CardList();
            list.Dock = DockStyle.Fill;
            list.SelectionMode = SelectionMode.MultiExtended;
            list.Info = o => ClipInfo((Clip)o);
            list.SelectedIndexChanged += delegate { ShowPreview(); };
            list.DoubleClick += delegate { CopySelected(0); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { CopySelected(0); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.C) { CopySelected(0); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.P) { TogglePin(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.A) { list.BeginUpdate(); for (int i = 0; i < list.Items.Count; i++) list.SetSelected(i, true); list.EndUpdate(); e.SuppressKeyPress = true; }
            };
            list.ContextMenuStrip = Ui.Menu();
            BuildMenu(list.ContextMenuStrip); // has items so it opens; rebuilt each time for fresh folder names
            list.ContextMenuStrip.Opening += (s, e) => BuildMenu(list.ContextMenuStrip);
            Controls.Add(list);

            var right = new Panel(); right.Dock = DockStyle.Right; right.Width = Ui.S(340); right.Padding = new Padding(Ui.S(10), Ui.S(4), 0, Ui.S(4));
            preview = new TextBox(); preview.Multiline = true; preview.ReadOnly = true; preview.ScrollBars = ScrollBars.Vertical; preview.Dock = DockStyle.Fill;
            preview.BorderStyle = BorderStyle.FixedSingle; preview.Font = new Font("Segoe UI", 10f);
            picture = new PictureBox(); picture.Dock = DockStyle.Fill; picture.SizeMode = PictureBoxSizeMode.Zoom; picture.BackColor = Ui.Card; picture.Visible = false;
            previewMeta = new Label(); previewMeta.Dock = DockStyle.Top; previewMeta.Height = Ui.S(24); previewMeta.ForeColor = Ui.Muted; previewMeta.Font = Ui.Small;
            right.Controls.Add(preview);
            right.Controls.Add(picture);
            right.Controls.Add(previewMeta);
            Controls.Add(right);

            var bar2 = MainForm.Bar();
            bar2.Controls.Add(Ui.Btn("Copy", delegate { CopySelected(0); }, true));
            var asBtn = Ui.Btn("Copy as...", null);
            asBtn.Click += delegate { var m = Ui.Menu(); AddTransforms(m.Items); Ui.ThemeMenu(m); m.Show(asBtn, new Point(0, asBtn.Height)); };
            bar2.Controls.Add(asBtn);
            bar2.Controls.Add(Ui.Btn("Pin", delegate { TogglePin(); }));
            var folderBtn = Ui.Btn("Folder...", null);
            folderBtn.Click += delegate { var m = Ui.Menu(); AddFolderItems(m.Items); Ui.ThemeMenu(m); m.Show(folderBtn, new Point(0, folderBtn.Height)); };
            bar2.Controls.Add(folderBtn);
            bar2.Controls.Add(Ui.Btn("Add to queue", delegate { AddToQueue(); }));
            bar2.Controls.Add(Ui.Btn("Delete", delegate { DeleteSelected(); }));
            bar2.Controls.Add(Ui.Btn("Clear unpinned", delegate { ClearUnpinned(); }));
            pause = new CheckBox(); pause.Text = "Pause capture"; pause.AutoSize = true; pause.Margin = new Padding(Ui.S(8), Ui.S(5), 0, 0);
            pause.Checked = Store.Settings.Paused;
            pause.CheckedChanged += delegate { if (pause.Checked != Store.Settings.Paused) App.I.SetPaused(pause.Checked); };
            bar2.Controls.Add(pause);
            Controls.Add(bar2);

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Clipboard history"));
            search = Ui.Search("Search everything you copied  (Ctrl+F)");
            search.Width = Ui.S(260);
            search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            search.TextChanged += delegate { RefreshList(); };
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.SuppressKeyPress = true; } };
            bar.Controls.Add(search);
            filter = MainForm.Combo(120, "Everything", "Pinned", "Text", "Links", "Images", "Files", "Today", "Red", "Orange", "Yellow", "Green", "Blue", "Purple");
            filter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(filter);
            folderFilter = MainForm.Combo(150, "All folders");
            folderFilter.SelectedIndexChanged += delegate { if (!reloading) RefreshList(); };
            bar.Controls.Add(folderFilter);
            Controls.Add(bar);
        }

        public void UpdatePaused() { pause.Checked = Store.Settings.Paused; }

        CardInfo ClipInfo(Clip c)
        {
            var ci = new CardInfo();
            if (c.Kind == "image")
            {
                ci.Title = "Image";
                ci.Sub = c.ImgW + " x " + c.ImgH + " px";
                ci.Thumb = Thumbs.Get(c, Ui.S(90), list.ItemHeight - Ui.S(18));
            }
            else if (c.Kind == "files")
            {
                var files = c.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                ci.Title = files.Length == 1 ? Path.GetFileName(files[0]) : files.Length + " files";
                ci.Sub = Ui.OneLine(c.Text, 200);
            }
            else
            {
                ci.Title = Ui.OneLine(Store.TitleFrom(c.Text), 120);
                ci.Sub = Ui.OneLine(c.Text, 240);
            }
            var meta = new List<string>();
            if (c.Pinned) meta.Add("PINNED");
            if (!string.IsNullOrEmpty(c.Folder)) meta.Add("[" + c.Folder + "]");
            if (!string.IsNullOrEmpty(c.Source)) meta.Add(c.Source);
            meta.Add(Ui.Ago(c.Time));
            ci.Meta = string.Join("  ·  ", meta);
            if (c.Pinned) { ci.Back = Ui.PinBack; ci.Strip = Ui.PinStrip; }
            var lc = Ui.LabelColor(c.Label);
            if (lc != Color.Empty) ci.Strip = lc;
            return ci;
        }

        public void FocusSearch() { search.Focus(); search.SelectAll(); }

        public void RefreshList()
        {
            Stale = false;
            reloading = true;
            MainForm.Reload(folderFilter, new[] { "All folders", "(no folder)" }, Store.Folders());
            reloading = false;
            string q = search.Text.Trim();
            IEnumerable<Clip> src = Store.Clips;
            int f = filter.SelectedIndex;
            switch (f)
            {
                case 1: src = src.Where(c => c.Pinned); break;
                case 2: src = src.Where(c => c.Kind == "text"); break;
                case 3: src = src.Where(c => c.Kind == "text" && (c.Text.StartsWith("http://") || c.Text.StartsWith("https://") || c.Text.StartsWith("www."))); break;
                case 4: src = src.Where(c => c.Kind == "image"); break;
                case 5: src = src.Where(c => c.Kind == "files"); break;
                case 6: { long today = DateTime.Today.Ticks; src = src.Where(c => c.Time >= today); break; }
                default:
                    if (f >= 7) { string label = (string)filter.SelectedItem; src = src.Where(c => c.Label == label); }
                    break;
            }
            if (folderFilter.SelectedIndex == 1) src = src.Where(c => string.IsNullOrEmpty(c.Folder));
            else if (folderFilter.SelectedIndex > 1) { string fo = (string)folderFilter.SelectedItem; src = src.Where(c => string.Equals(c.Folder, fo, StringComparison.OrdinalIgnoreCase)); }
            if (q.Length > 0)
            {
                var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                src = src.Where(c => words.All(w => c.Text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Source ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Folder ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            list.SetItems(src.Cast<object>());
            ShowPreview();
        }

        void ShowPreview()
        {
            var sel = list.Selected<Clip>();
            if (picture.Image != null) { var old = picture.Image; picture.Image = null; old.Dispose(); }
            if (sel.Count == 0) { preview.Visible = true; picture.Visible = false; preview.Text = ""; previewMeta.Text = Store.Clips.Count + " items saved"; return; }
            if (sel.Count > 1)
            {
                picture.Visible = false; preview.Visible = true;
                previewMeta.Text = sel.Count + " selected - Copy joins them, one per line";
                preview.Text = string.Join("\r\n", sel.Where(c => c.Kind != "image").Select(c => c.Text));
                preview.RightToLeft = Ui.IsRtl(preview.Text) ? RightToLeft.Yes : RightToLeft.No;
                return;
            }
            var c1 = sel[0];
            previewMeta.Text = (c1.Kind == "image" ? "Image" : c1.Text.Length + " characters") + (string.IsNullOrEmpty(c1.Source) ? "" : "  ·  from " + c1.Source) + "  ·  " + Ui.Ago(c1.Time) + (c1.Uses > 0 ? "  ·  used " + c1.Uses + "x" : "");
            if (c1.Kind == "image")
            {
                preview.Visible = false; picture.Visible = true;
                picture.Image = Store.LoadImage(c1);
            }
            else
            {
                picture.Visible = false; preview.Visible = true;
                string t = c1.Text.Length > 200000 ? c1.Text.Substring(0, 200000) + "\r\n..." : c1.Text;
                preview.Text = t.Replace("\r\n", "\n").Replace("\n", "\r\n");
                var rtl = Ui.IsRtl(preview.Text) ? RightToLeft.Yes : RightToLeft.No;
                if (preview.RightToLeft != rtl) preview.RightToLeft = rtl;
            }
        }

        // transform 0 = as is
        void CopySelected(int transform)
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) return;
            if (sel.Count == 1 && (transform == 0 || sel[0].Kind == "image"))
            {
                if (App.I.CopyClip(sel[0])) owner.Status("Copied. Paste it with Ctrl+V.");
                return;
            }
            var text = string.Join("\r\n", sel.Where(c => c.Kind != "image").Select(c => c.Text));
            text = Transforms.Apply(transform, text);
            if (App.I.CopyText(text)) owner.Status((sel.Count > 1 ? sel.Count + " items copied together" : "Copied as " + Transforms.Names[transform]) + ". Paste with Ctrl+V.");
        }

        void AddTransforms(ToolStripItemCollection items)
        {
            for (int i = 0; i < Transforms.Names.Length; i++)
            {
                int k = i;
                items.Add(Transforms.Names[i], null, delegate { CopySelected(k); });
            }
        }

        void AddFolderItems(ToolStripItemCollection items)
        {
            foreach (var f in Store.Folders())
            {
                string name = f;
                items.Add(name, null, delegate { SetFolder(name); });
            }
            if (items.Count > 0) items.Add(new ToolStripSeparator());
            items.Add("New folder...", null, delegate
            {
                var name = Ui.Ask("New folder", "Folder name (items in a folder are never deleted automatically):", "", owner);
                if (!string.IsNullOrWhiteSpace(name)) SetFolder(name.Trim());
            });
            items.Add("Remove from folder", null, delegate { SetFolder(""); });
        }

        void SetFolder(string folder)
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) { owner.Status("Select one or more items first."); return; }
            foreach (var c in sel) c.Folder = folder;
            Store.MarkClips();
            RefreshList();
            owner.Status(folder.Length > 0 ? sel.Count + " item(s) moved to folder \"" + folder + "\"." : "Removed from folder.");
        }

        void SetLabel(string label)
        {
            var sel = list.Selected<Clip>();
            foreach (var c in sel) c.Label = label;
            Store.MarkClips();
            list.Invalidate();
        }

        void TogglePin()
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) return;
            bool pin = sel.Any(c => !c.Pinned);
            foreach (var c in sel) c.Pinned = pin;
            Store.MarkClips();
            RefreshList();
            owner.Status(pin ? "Pinned - pinned items are never removed automatically." : "Unpinned.");
        }

        void AddToQueue()
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) { owner.Status("Select the items to paste one by one (Ctrl+click), then click Add to queue."); return; }
            // Oldest first, the order you copied them.
            App.I.AddToQueue(sel.OrderBy(c => c.Time));
            owner.Status(sel.Count + " item(s) in the paste queue. Press " + Hotkeys.Get("next") + " in any app to paste the next one.");
        }

        void DeleteSelected()
        {
            var sel = list.Selected<Clip>();
            if (sel.Count == 0) return;
            if (sel.Any(c => c.Pinned || !string.IsNullOrEmpty(c.Folder)) && MessageBox.Show(owner, "Some selected items are pinned or in a folder. Delete them anyway?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int idx = list.SelectedIndex;
            Store.RemoveClips(sel);
            if (list.Items.Count > 0) { list.ClearSelected(); list.SelectedIndex = Math.Min(Math.Max(0, idx), list.Items.Count - 1); }
        }

        void ClearUnpinned()
        {
            int n = Store.Clips.Count(c => !c.Pinned && string.IsNullOrEmpty(c.Folder));
            if (n == 0) return;
            if (MessageBox.Show(owner, "Delete " + n + " items? Pinned items and items in folders stay.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Store.RemoveClips(Store.Clips.Where(c => !c.Pinned && string.IsNullOrEmpty(c.Folder)));
            Thumbs.Clear();
        }

        void SaveAsPrompt()
        {
            var sel = list.Selected<Clip>().Where(c => c.Kind != "image").ToList();
            if (sel.Count == 0) return;
            foreach (var c in sel) Store.NewPrompt(Store.TitleFrom(c.Text), c.Text.Trim(), "From clipboard");
            owner.Status(sel.Count + " saved to Prompts > From clipboard.");
        }

        void SaveAsNote()
        {
            var sel = list.Selected<Clip>().Where(c => c.Kind != "image").ToList();
            if (sel.Count == 0) return;
            App.I.NewNote(null, string.Join("\r\n\r\n", sel.Select(c => c.Text)));
        }

        void BuildMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            m.Items.Add("Copy\tEnter", null, delegate { CopySelected(0); });
            var asMenu = new ToolStripMenuItem("Copy as");
            AddTransforms(asMenu.DropDownItems);
            m.Items.Add(asMenu);
            m.Items.Add("Pin / Unpin\tCtrl+P", null, delegate { TogglePin(); });
            var folder = new ToolStripMenuItem("Move to folder");
            AddFolderItems(folder.DropDownItems);
            m.Items.Add(folder);
            var label = new ToolStripMenuItem("Color label");
            foreach (var n in Ui.LabelNames)
            {
                string name = n;
                label.DropDownItems.Add(name, Ui.Swatch(Ui.LabelColor(name), Ui.S(16)), delegate { SetLabel(name); });
            }
            label.DropDownItems.Add("No label", null, delegate { SetLabel(""); });
            m.Items.Add(label);
            m.Items.Add("Add to paste queue", null, delegate { AddToQueue(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Save as prompt", null, delegate { SaveAsPrompt(); });
            m.Items.Add("Save as sticky note", null, delegate { SaveAsNote(); });
            m.Items.Add("Open image / file location / link", null, delegate
            {
                var c = list.Selected<Clip>().FirstOrDefault();
                if (c == null) return;
                try
                {
                    if (c.Kind == "image") Process.Start(Store.ImagePath(c));
                    else if (c.Kind == "files") Process.Start("explorer.exe", "/select,\"" + c.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[0] + "\"");
                    else if (c.Text.StartsWith("http")) Process.Start(c.Text.Trim());
                }
                catch { }
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete\tDel", null, delegate { DeleteSelected(); });
            Ui.ThemeMenu(m);
        }
    }
}
