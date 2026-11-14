using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    static class Features
    {
        public static readonly string[][] All =
        {
            new[] { "clips", "Clipboard" },
            new[] { "prompts", "Prompts" },
            new[] { "notes", "Notes" },
            new[] { "procs", "Processes" },
            new[] { "drives", "Drives & cleanup" },
            new[] { "startup", "Startup apps" },
            new[] { "snip", "Snip & record" },
            new[] { "activity", "File activity" },
            new[] { "tagged", "Tagged files" },
        };

        public static string Title(string id)
        {
            foreach (var f in All) if (f[0] == id) return f[1];
            return id;
        }

        public static Control Create(string id, CardForm card)
        {
            Action<string> st = s => card.Status(s);
            switch (id)
            {
                case "clips": case "prompts": case "notes": return new QuickListPanel(id, () => card.LastExternal, st);
                case "procs": return new ProcessesPanel(st);
                case "drives": return new DrivesPanel(st);
                case "startup": return new StartupPanel(st);
                case "snip": return new SnipPanel(st);
                case "activity": return new ActivityPanel(st);
                case "tagged": return new TaggedPanel(st);
            }
            return new Label { Text = "Unknown feature: " + id };
        }
    }

    // Compact searchable list of clipboard items, prompts or notes for floating cards.
    class QuickListPanel : LivePanel
    {
        readonly string mode;
        readonly Func<IntPtr> target;
        readonly Action<string> status;
        readonly TextBox search;
        readonly CardList list;
        bool stale = true;
        readonly Action onChange;

        public QuickListPanel(string mode, Func<IntPtr> target, Action<string> status)
        {
            this.mode = mode; this.target = target; this.status = status;
            Dock = DockStyle.Fill;
            list = new CardList { Dock = DockStyle.Fill };
            list.ItemHeight = Math.Min(255, Ui.S(mode == "notes" ? 64 : 56));
            list.SelectionMode = SelectionMode.One;
            list.Info = Info;
            list.DoubleClick += delegate { Act(true, 0); };
            list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Act(!e.Shift, 0); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Delete) { Delete(); e.SuppressKeyPress = true; }
            };
            list.ContextMenuStrip = Ui.Menu();
            BuildMenu();
            list.ContextMenuStrip.Opening += delegate { BuildMenu(); };
            Controls.Add(list);

            search = Ui.Search(mode == "clips" ? "Search clipboard" : mode == "prompts" ? "Search prompts" : "Search notes");
            search.Dock = DockStyle.Top;
            search.TextChanged += delegate { Fill(); };
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down) { list.Focus(); if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { if (list.Items.Count > 0) { if (list.SelectedIndex < 0) list.SelectedIndex = 0; Act(true, 0); } e.SuppressKeyPress = true; }
            };
            var wrap = new Panel { Dock = DockStyle.Top, Height = Ui.S(30), Padding = new Padding(Ui.S(2), Ui.S(2), Ui.S(2), Ui.S(4)) };
            wrap.Controls.Add(search);
            Controls.Add(wrap);

            onChange = () => { if (Live) Fill(); else stale = true; };
            if (mode == "clips") Store.ClipsChanged += onChange;
            else if (mode == "prompts") Store.PromptsChanged += onChange;
            else Store.NotesChanged += onChange;
            Ui.Theme(this);
        }

        protected override void OnLive(bool on) { if (on && stale) Fill(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Store.ClipsChanged -= onChange; Store.PromptsChanged -= onChange; Store.NotesChanged -= onChange;
            }
            base.Dispose(disposing);
        }

        void Fill()
        {
            stale = false;
            var words = search.Text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<object> items;
            if (mode == "clips")
                items = Store.Clips.Where(c => words.All(w => c.Text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Folder ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)).Take(200).Cast<object>();
            else if (mode == "prompts")
                items = Store.Prompts.Where(p => words.All(w => (p.Title + "\n" + p.Body + "\n" + p.Tags + "\n" + p.Category + "\n" + p.Trigger).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(p => p.Fav).ThenByDescending(p => p.Uses).ThenByDescending(p => p.LastUsed).Take(200).Cast<object>();
            else
                items = Store.Notes.Where(n => !n.Archived && words.All(w => ((n.Text ?? "") + "\n" + n.Tags).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(n => n.Modified).Take(200).Cast<object>();
            list.SetItems(items);
        }

        CardInfo Info(object o)
        {
            var ci = new CardInfo();
            var c = o as Clip;
            if (c != null)
            {
                if (c.Kind == "image") { ci.Title = "Image " + c.ImgW + " x " + c.ImgH; ci.Thumb = Thumbs.Get(c, Ui.S(80), list.ItemHeight - Ui.S(18)); }
                else if (c.Kind == "files") ci.Title = Path.GetFileName(c.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[0]);
                else ci.Title = Ui.OneLine(Store.TitleFrom(c.Text), 100);
                ci.Sub = c.Kind == "image" ? "" : Ui.OneLine(c.Text, 140);
                ci.Meta = (c.Pinned ? "PINNED  ·  " : "") + Ui.Ago(c.Time);
                if (c.Pinned) { ci.Back = Ui.PinBack; ci.Strip = Ui.PinStrip; }
                var lc = Ui.LabelColor(c.Label);
                if (lc != Color.Empty) ci.Strip = lc;
                return ci;
            }
            var p = o as Prompt;
            if (p != null)
            {
                ci.Title = (p.Fav ? "★ " : "") + (p.Title.Trim().Length > 0 ? p.Title : "(untitled)");
                ci.Sub = Ui.OneLine(p.Body, 140);
                ci.Meta = p.Trigger.Trim().Length > 0 ? p.Trigger.Trim() : p.Category;
                if (p.Fav) ci.Strip = Ui.PinStrip;
                return ci;
            }
            var n = (Note)o;
            var nc = Ui.ColorOf(n.Color);
            ci.Title = (n.Text ?? "").Trim().Length == 0 ? "(empty note)" : Store.TitleFrom(n.Text);
            int nl = (n.Text ?? "").Trim().IndexOf('\n');
            ci.Sub = nl > 0 ? Ui.OneLine(n.Text.Trim().Substring(nl + 1), 140) : "";
            ci.Meta = (n.RemindAt > 0 ? "Reminder " + Ui.Ago(n.RemindAt) + "  ·  " : "") + Ui.Ago(n.Modified);
            ci.Back = nc.Body; ci.Strip = nc.Header; ci.Fore = nc.Text;
            ci.SubFore = nc.Name == "Charcoal" ? Color.FromArgb(200, 200, 200) : Color.FromArgb(105, 105, 105);
            return ci;
        }

        // paste: true = paste into the app used before this card; transform = text action
        void Act(bool paste, int transform)
        {
            var o = list.SelectedItem;
            if (o == null) return;
            var n = o as Note;
            if (n != null) { App.I.OpenNote(n); return; }
            bool ok;
            var c = o as Clip;
            if (c != null)
            {
                if (transform == 0 || c.Kind == "image") ok = App.I.CopyClip(c);
                else { ok = App.I.CopyText(Transforms.Apply(transform, c.Text)); if (ok) Store.TouchClip(c); }
            }
            else
            {
                var p = (Prompt)o;
                var text = Ui.FillVariables(p.Body, FindForm());
                if (text == null) return;
                ok = App.I.CopyText(Transforms.Apply(transform, text));
                if (ok) { p.Uses++; p.LastUsed = Store.Now(); Store.MarkPrompts(); }
            }
            if (!ok) return;
            IntPtr t = target();
            if (paste && t != IntPtr.Zero && Native.IsWindow(t)) { App.I.PasteTo(t); status("Pasted."); }
            else status("Copied - paste with Ctrl+V.");
        }

        void Delete()
        {
            var o = list.SelectedItem;
            var c = o as Clip;
            if (c != null) { Store.RemoveClips(new[] { c }); return; }
        }

        void BuildMenu()
        {
            var m = list.ContextMenuStrip;
            m.Items.Clear();
            if (mode == "notes")
            {
                m.Items.Add("Open note", null, delegate { Act(false, 0); });
                m.Items.Add("Copy text", null, delegate { var n = list.SelectedItem as Note; if (n != null && App.I.CopyText(n.Text)) status("Copied."); });
                m.Items.Add("New note", null, delegate { App.I.NewNote(null); });
            }
            else
            {
                m.Items.Add("Paste into last app\tEnter", null, delegate { Act(true, 0); });
                m.Items.Add("Copy only\tShift+Enter", null, delegate { Act(false, 0); });
                var asMenu = new ToolStripMenuItem("Paste as");
                for (int i = 1; i < Transforms.Names.Length; i++) { int k = i; asMenu.DropDownItems.Add(Transforms.Names[i], null, delegate { Act(true, k); }); }
                m.Items.Add(asMenu);
                if (mode == "clips")
                {
                    m.Items.Add("Pin / Unpin", null, delegate { var c = list.SelectedItem as Clip; if (c != null) { c.Pinned = !c.Pinned; Store.MarkClips(); list.Invalidate(); } });
                    m.Items.Add("Delete\tDel", null, delegate { Delete(); });
                }
                else m.Items.Add("Favorite on/off", null, delegate { var p = list.SelectedItem as Prompt; if (p != null) { p.Fav = !p.Fav; Store.MarkPrompts(); list.Invalidate(); } });
            }
            Ui.ThemeMenu(m);
        }
    }

    // A floating, resizable, always-on-top card that hosts one or more features as tabs,
    // and can shrink to a small pin chip.
    class CardForm : Form
    {
        public readonly CardDef D;
        public IntPtr LastExternal;
        readonly EdgePanel header, body;
        readonly FlowLayoutPanel tabBar;
        readonly IconButton btnTop, btnHide, btnMenu, btnClose;
        readonly Label chip, statusLabel;
        readonly ToolTip tips = new ToolTip();
        readonly Dictionary<string, Control> views = new Dictionary<string, Control>();
        readonly Timer statusTimer = new Timer();
        bool loading = true;
        Point chipDown;
        bool chipDragging;
        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public CardForm(CardDef d)
        {
            D = d;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = d.Title;
            Icon = App.AppIcon;
            BackColor = Ui.Line;
            ForeColor = Ui.Text;
            Font = Ui.Base;
            Padding = new Padding(1);
            TopMost = d.Top;
            Opacity = Math.Max(0.3, Math.Min(1.0, d.Opacity / 100.0));
            MinimumSize = Ui.S(240, 160);

            body = new EdgePanel { Dock = DockStyle.Fill, BackColor = Ui.Bg, Padding = new Padding(Ui.S(6), Ui.S(4), Ui.S(6), Ui.S(2)) };
            Controls.Add(body);

            statusLabel = new Label { Dock = DockStyle.Bottom, Height = Ui.S(20), Font = Ui.Small, ForeColor = Ui.Muted, BackColor = Ui.Bg, Padding = new Padding(Ui.S(8), 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(statusLabel);
            statusTimer.Interval = 4000;
            statusTimer.Tick += delegate { statusTimer.Stop(); statusLabel.Text = ""; };

            header = new EdgePanel { Dock = DockStyle.Top, Height = Ui.S(32), BackColor = Ui.Nav };
            tabBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Ui.Nav, Padding = new Padding(Ui.S(4), Ui.S(4), 0, 0) };
            tabBar.MouseDown += Drag;
            header.MouseDown += Drag;
            header.Controls.Add(tabBar);
            btnTop = new IconButton(Ui.GPin, 9f, "Keep on top of other windows", tips, delegate { SetTop(!D.Top); });
            btnHide = new IconButton("", 9f, "Shrink to a small pin (click the pin to open again)", tips, delegate { Collapse(); });
            btnMenu = new IconButton(Ui.GMore, 9f, "Card menu: features, opacity, rename", tips, delegate { ShowMenu(btnMenu, new Point(0, btnMenu.Height)); });
            btnClose = new IconButton(Ui.GClose, 9f, "Close card (reopen it from Floating cards)", tips, delegate { CloseCard(); });
            foreach (var b in new[] { btnTop, btnHide, btnMenu, btnClose }) { b.Dock = DockStyle.Right; b.Width = Ui.S(30); b.ForeColor = Ui.Text; b.HoverColor = Ui.Dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(30, 0, 0, 0); }
            header.Controls.Add(btnTop);
            header.Controls.Add(btnHide);
            header.Controls.Add(btnMenu);
            header.Controls.Add(btnClose);
            Controls.Add(header);

            chip = new Label
            {
                Dock = DockStyle.Fill, Visible = false, Text = Ui.GPin, Font = Ui.Icon(13f), TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(255, 228, 120), ForeColor = Color.FromArgb(60, 50, 10), Cursor = Cursors.Hand
            };
            chip.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { chipDown = Cursor.Position; chipDragging = false; } };
            chip.MouseMove += (s, e) =>
            {
                if (e.Button != MouseButtons.Left || chipDragging) return;
                var p = Cursor.Position;
                if (Math.Abs(p.X - chipDown.X) + Math.Abs(p.Y - chipDown.Y) > Ui.S(4))
                {
                    chipDragging = true;
                    Native.ReleaseCapture();
                    Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
                }
            };
            chip.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left && !chipDragging) Expand(); };
            var chipMenu = Ui.Menu();
            chipMenu.Items.Add("Open", null, delegate { Expand(); });
            chipMenu.Items.Add("Close card", null, delegate { CloseCard(); });
            Ui.ThemeMenu(chipMenu);
            chip.ContextMenuStrip = chipMenu;
            Controls.Add(chip);
            tips.SetToolTip(chip, d.Title + " - click to open, drag to move");

            if (D.Features.Count == 0) D.Features.Add("clips");
            BuildTabs();
            int w = d.W > 0 ? d.W : Ui.S(380), h = d.H > 0 ? d.H : Ui.S(460);
            var rect = new Rectangle(d.X, d.Y, w, h);
            if (d.X == -1 && d.Y == -1) { var c = Cursor.Position; rect.X = c.X - Ui.S(60); rect.Y = c.Y - Ui.S(20); }
            Bounds = Fit(rect);
            UpdateTop();
            ShowFeature(D.Active != null && D.Features.Contains(D.Active) ? D.Active : D.Features[0]);
            loading = false;
            if (D.Collapsed) { D.Collapsed = false; Collapse(); }
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= Native.CS_DROPSHADOW; cp.ExStyle |= 0x80; /* tool window: not in Alt+Tab */ return cp; }
        }

        static Rectangle Fit(Rectangle r)
        {
            var wa = Screen.FromRectangle(r).WorkingArea;
            if (r.Width > wa.Width) r.Width = wa.Width;
            if (r.Height > wa.Height) r.Height = wa.Height;
            if (r.Right > wa.Right) r.X = wa.Right - r.Width;
            if (r.Bottom > wa.Bottom) r.Y = wa.Bottom - r.Height;
            if (r.X < wa.Left) r.X = wa.Left;
            if (r.Y < wa.Top) r.Y = wa.Top;
            return r;
        }

        public void Status(string s)
        {
            statusLabel.Text = s;
            statusTimer.Stop(); statusTimer.Start();
        }

        void BuildTabs()
        {
            tabBar.Controls.Clear();
            foreach (var id in D.Features)
            {
                string fid = id;
                var l = new Label { Text = Features.Title(id), UseMnemonic = false, AutoSize = true, Cursor = Cursors.Hand, Padding = new Padding(Ui.S(6), Ui.S(3), Ui.S(6), Ui.S(3)), Margin = new Padding(0, 0, Ui.S(2), 0), Tag = id };
                l.Click += delegate { ShowFeature(fid); };
                l.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left && e.Clicks == 1) { } };
                tabBar.Controls.Add(l);
            }
            if (D.Features.Count == 1)
            {
                // Single feature: show the card title instead of a tab.
                ((Label)tabBar.Controls[0]).Text = D.Title == "Card" ? Features.Title(D.Features[0]) : D.Title;
            }
            HighlightTabs();
        }

        void HighlightTabs()
        {
            foreach (Control c in tabBar.Controls)
            {
                bool on = (string)c.Tag == D.Active;
                c.Font = on ? Ui.Bold : Ui.Base;
                c.ForeColor = on ? Ui.Text : Ui.Muted;
                c.BackColor = on && D.Features.Count > 1 ? Ui.Bg : Ui.Nav;
            }
        }

        void ShowFeature(string id)
        {
            if (!D.Features.Contains(id)) return;
            Control v;
            if (!views.TryGetValue(id, out v))
            {
                v = Features.Create(id, this);
                v.Dock = DockStyle.Fill;
                v.Visible = false;
                views[id] = v;
                body.Controls.Add(v);
            }
            foreach (var kv in views) kv.Value.Visible = kv.Key == id;
            D.Active = id;
            HighlightTabs();
            if (!loading) Store.MarkCards();
        }

        public void SetFeatures(List<string> features)
        {
            if (features.Count == 0) return;
            D.Features = features.ToList();
            foreach (var k in views.Keys.ToList())
                if (!D.Features.Contains(k)) { var v = views[k]; views.Remove(k); body.Controls.Remove(v); v.Dispose(); }
            BuildTabs();
            ShowFeature(D.Features.Contains(D.Active ?? "") ? D.Active : D.Features[0]);
            Store.MarkCards();
            Store.RaiseCards();
        }

        void SetTop(bool on)
        {
            D.Top = on;
            TopMost = on;
            UpdateTop();
            Store.MarkCards();
        }

        void UpdateTop()
        {
            btnTop.Text = D.Top ? Ui.GPinned : Ui.GPin;
            tips.SetToolTip(btnTop, D.Top ? "On top of other windows (click to stop)" : "Keep on top of other windows");
        }

        public void Collapse()
        {
            if (D.Collapsed) return;
            SaveBounds();
            D.Collapsed = true;
            header.Visible = false; body.Visible = false; statusLabel.Visible = false;
            chip.Visible = true;
            MinimumSize = Size.Empty;
            Padding = new Padding(0);
            TopMost = true;
            var pos = Location;
            Size = Ui.S(38, 34);
            Location = pos;
            Store.MarkCards();
            Store.RaiseCards();
            Ui.Later(1500, App.I.Trim);
        }

        public void Expand()
        {
            if (!D.Collapsed) return;
            int savedW = D.W, savedH = D.H;
            loading = true;
            D.Collapsed = false;
            chip.Visible = false;
            Padding = new Padding(1);
            header.Visible = true; body.Visible = true; statusLabel.Visible = true;
            MinimumSize = Ui.S(240, 160);
            TopMost = D.Top;
            Bounds = Fit(new Rectangle(Left, Top, savedW > 0 ? savedW : Ui.S(380), savedH > 0 ? savedH : Ui.S(460)));
            loading = false;
            SaveBounds();
            Activate();
            Store.MarkCards();
            Store.RaiseCards();
        }

        void CloseCard()
        {
            D.Visible = false;
            Store.MarkCards();
            Close();
            Store.RaiseCards();
        }

        void Drag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks > 1) { Collapse(); return; }
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        void SaveBounds()
        {
            if (loading || D.Collapsed || WindowState != FormWindowState.Normal) return;
            D.X = Left; D.Y = Top; D.W = Width; D.H = Height;
            Store.MarkCards();
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (D.Collapsed) { if (!loading) { D.X = Left; D.Y = Top; Store.MarkCards(); } }
            else SaveBounds();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); SaveBounds(); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0006 /* WM_ACTIVATE */ && (m.WParam.ToInt64() & 0xFFFF) != 0 && m.LParam != IntPtr.Zero)
            {
                uint pid;
                Native.GetWindowThreadProcessId(m.LParam, out pid);
                if (pid != myPid) LastExternal = m.LParam;
            }
            if (m.Msg == Native.WM_NCHITTEST && !D.Collapsed)
            {
                base.WndProc(ref m);
                var p = PointToClient(Cursor.Position);
                int g = Ui.S(6);
                bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
                if (t && l) m.Result = (IntPtr)13;
                else if (t && r) m.Result = (IntPtr)14;
                else if (b && l) m.Result = (IntPtr)16;
                else if (b && r) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10;
                else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12;
                else if (b) m.Result = (IntPtr)15;
                return;
            }
            base.WndProc(ref m);
        }

        void ShowMenu(Control at, Point p)
        {
            var m = Ui.Menu();
            var feats = new ToolStripMenuItem("Features in this card");
            foreach (var f in Features.All)
            {
                string id = f[0];
                var it = new ToolStripMenuItem(f[1]) { Checked = D.Features.Contains(id) };
                it.Click += delegate
                {
                    var list = D.Features.ToList();
                    if (list.Contains(id)) { if (list.Count > 1) list.Remove(id); }
                    else list.Add(id);
                    SetFeatures(list);
                };
                feats.DropDownItems.Add(it);
            }
            m.Items.Add(feats);
            m.Items.Add("Rename card...", null, delegate
            {
                var t = Ui.Ask("Rename card", "Card name:", D.Title, this);
                if (string.IsNullOrWhiteSpace(t)) return;
                D.Title = t.Trim(); Text = D.Title; BuildTabs(); Store.MarkCards(); Store.RaiseCards();
            });
            var op = new ToolStripMenuItem("Opacity");
            foreach (int v in new[] { 100, 90, 80, 70, 60, 50 })
            {
                int val = v;
                var it = new ToolStripMenuItem(v + " %") { Checked = D.Opacity == v };
                it.Click += delegate { D.Opacity = val; Opacity = val / 100.0; Store.MarkCards(); };
                op.DropDownItems.Add(it);
            }
            m.Items.Add(op);
            var top = new ToolStripMenuItem("Keep on top") { Checked = D.Top };
            top.Click += delegate { SetTop(!D.Top); };
            m.Items.Add(top);
            m.Items.Add("Shrink to pin\t(double-click top bar)", null, delegate { Collapse(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Manage all cards...", null, delegate { App.I.ShowMain("cards"); });
            m.Items.Add("Close card", null, delegate { CloseCard(); });
            m.Items.Add("Delete card", null, delegate { App.I.DeleteCard(D); });
            Ui.ThemeMenu(m);
            m.Closed += delegate { Ui.Later(300, () => m.Dispose()); };
            m.Show(at, p);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            statusTimer.Dispose();
            tips.Dispose();
            base.OnFormClosed(e);
        }
    }

    // ======================= Main window "Floating cards" page =======================

    class CardsPage : Panel
    {
        readonly MainForm owner;
        readonly CardList list;
        public bool Stale = true;

        public CardsPage(MainForm owner)
        {
            this.owner = owner;
            Dock = DockStyle.Fill;
            list = new CardList { Dock = DockStyle.Fill };
            list.Info = o =>
            {
                var d = (CardDef)o;
                var ci = new CardInfo();
                ci.Title = d.Title;
                ci.Sub = string.Join("  ·  ", d.Features.Select(Features.Title));
                ci.Meta = !d.Visible ? "closed" : d.Collapsed ? "shrunk to pin" : "open" + (d.Top ? ", on top" : "");
                ci.Strip = d.Visible ? Ui.PinStrip : Color.Empty;
                return ci;
            };
            list.DoubleClick += delegate { ShowSel(); };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ShowSel(); e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Delete) { DeleteSel(); e.SuppressKeyPress = true; } };
            Controls.Add(list);

            var help = new Label
            {
                Dock = DockStyle.Top, Height = Ui.S(62), ForeColor = Ui.Muted, Font = Ui.Small,
                Text = "Floating cards stay on top of any app. Put one feature in a card, or several features as tabs in one master card.\n" +
                       "Drag the top bar to move, drag edges to resize, double-click the top bar (or the  –  button) to shrink a card to a small pin.\n" +
                       Hotkeys.Get("cards") + " shows / hides all cards. In Clipboard and Prompts cards, Enter or double-click pastes into the app you used last."
            };
            Controls.Add(help);

            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Floating cards"));
            bar.Controls.Add(Ui.Btn("+  New card", delegate { Edit(null); }, true));
            bar.Controls.Add(Ui.Btn("Edit", delegate { var d = list.SelectedItem as CardDef; if (d != null) Edit(d); }));
            bar.Controls.Add(Ui.Btn("Open", delegate { ShowSel(); }));
            bar.Controls.Add(Ui.Btn("Close", delegate { var d = list.SelectedItem as CardDef; if (d != null) App.I.CloseCard(d); }));
            bar.Controls.Add(Ui.Btn("Delete", delegate { DeleteSel(); }));
            bar.Controls.Add(Ui.Btn("Show / hide all", delegate { App.I.ToggleCards(); }));
            bar.Controls.Add(Ui.Btn("Starter cards", delegate { App.I.StarterCards(); }));
            Controls.Add(bar);
            Store.CardsChanged += OnCards;
            Ui.Theme(this);
        }

        void OnCards() { if (Visible) RefreshList(); else Stale = true; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Store.CardsChanged -= OnCards;
            base.Dispose(disposing);
        }

        public void RefreshList()
        {
            Stale = false;
            list.SetItems(Store.Cards.Cast<object>());
        }

        public void FocusSearch() { list.Focus(); }

        void ShowSel() { var d = list.SelectedItem as CardDef; if (d != null) App.I.OpenCard(d, true); }

        void DeleteSel()
        {
            var d = list.SelectedItem as CardDef;
            if (d == null) return;
            if (MessageBox.Show(owner, "Delete card \"" + d.Title + "\"? (Your data is not touched.)", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                App.I.DeleteCard(d);
        }

        void Edit(CardDef d)
        {
            using (var f = Ui.Dialog(d == null ? "New floating card" : "Edit card", 380, 360))
            {
                var l1 = new Label { Text = "Card name:", AutoSize = true, Location = new Point(Ui.S(14), Ui.S(12)) };
                var name = new TextBox { Text = d != null ? d.Title : "My card", Width = Ui.S(350), Location = new Point(Ui.S(14), Ui.S(34)) };
                var l2 = new Label { Text = "Features inside this card (each one becomes a tab):", AutoSize = true, Location = new Point(Ui.S(14), Ui.S(70)) };
                var cl = new CheckedListBox { Location = new Point(Ui.S(14), Ui.S(94)), Size = Ui.S(350, 190), CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
                foreach (var ft in Features.All) cl.Items.Add(ft[1], d != null ? d.Features.Contains(ft[0]) : ft[0] == "clips");
                var ok = Ui.Btn(d == null ? "Create card" : "Save", null, true); ok.DialogResult = DialogResult.OK; ok.Location = new Point(Ui.S(190), Ui.S(304));
                var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(Ui.S(290), Ui.S(304));
                f.Controls.AddRange(new Control[] { l1, name, l2, cl, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                Ui.Theme(f);
                if (f.ShowDialog(owner) != DialogResult.OK) return;
                var feats = new List<string>();
                for (int i = 0; i < Features.All.Length; i++) if (cl.GetItemChecked(i)) feats.Add(Features.All[i][0]);
                if (feats.Count == 0) { owner.Status("Pick at least one feature."); return; }
                string title = string.IsNullOrWhiteSpace(name.Text) ? "Card" : name.Text.Trim();
                if (d == null) App.I.NewCard(feats, title);
                else App.I.UpdateCard(d, title, feats);
            }
        }
    }
}
