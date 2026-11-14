using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Small fast popup opened by the clipboard / prompts shortcuts: search, pick, paste into the app you were in.
    class QuickPopup : Form
    {
        readonly TextBox search;
        readonly CardList list;
        readonly Label tabClips, tabPrompts, hint;
        string mode = "clips";
        IntPtr target;
        bool busy;

        public QuickPopup()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            StartPosition = FormStartPosition.Manual;
            Size = Ui.S(500, 540);
            BackColor = Ui.Dark ? Color.FromArgb(90, 90, 90) : Color.FromArgb(190, 190, 190);
            ForeColor = Ui.Text;
            Padding = new Padding(1);
            Icon = App.AppIcon;

            var inner = new Panel();
            inner.Dock = DockStyle.Fill;
            inner.BackColor = Ui.Bg;
            inner.Padding = new Padding(Ui.S(8));

            list = new CardList();
            list.Dock = DockStyle.Fill;
            list.ItemHeight = Math.Min(255, Ui.S(60));
            list.Info = Info;
            list.DoubleClick += delegate { Pick(true, 0); };
            list.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowPasteAs(e.Location); };
            inner.Controls.Add(list);

            hint = new Label();
            hint.Dock = DockStyle.Bottom;
            hint.Height = Ui.S(54);
            hint.Font = Ui.Small;
            hint.ForeColor = Ui.Muted;
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Text = "Enter paste (plain text)  ·  Ctrl+Enter paste as UPPERCASE, trim...\nShift+Enter copy only  ·  Ctrl+1..9 quick paste  ·  Tab switch list\nCtrl+P pin  ·  Del delete  ·  Esc close";
            inner.Controls.Add(hint);

            search = Ui.Search("Type to search...");
            search.Dock = DockStyle.Top;
            search.Font = new Font("Segoe UI", 11f);
            search.TextChanged += delegate { Fill(); };
            var sw = new Panel(); sw.Dock = DockStyle.Top; sw.Height = Ui.S(38); sw.Padding = new Padding(0, Ui.S(4), 0, Ui.S(6));
            sw.Controls.Add(search);
            inner.Controls.Add(sw);

            var tabs = new Panel(); tabs.Dock = DockStyle.Top; tabs.Height = Ui.S(32);
            tabPrompts = Tab("Prompts  (" + Hotkeys.Get("prompts") + ")", "prompts");
            tabClips = Tab("Clipboard  (" + Hotkeys.Get("clips") + ")", "clips");
            tabs.Controls.Add(tabPrompts);
            tabs.Controls.Add(tabClips);
            inner.Controls.Add(tabs);

            Controls.Add(inner);
            Ui.Theme(this);
        }

        Label Tab(string text, string m)
        {
            var l = new Label();
            l.Text = text;
            l.Dock = DockStyle.Left;
            l.Width = Ui.S(220);
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Cursor = Cursors.Hand;
            l.Click += delegate { SetMode(m); search.Focus(); };
            return l;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= Native.CS_DROPSHADOW; cp.ExStyle |= 0x80; /* WS_EX_TOOLWINDOW */ return cp; }
        }

        public void Open(string m, IntPtr targetWindow)
        {
            target = targetWindow;
            search.Text = "";
            SetMode(m);
            var c = Cursor.Position;
            var wa = Screen.FromPoint(c).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(c.X - Width / 3, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(c.Y + Ui.S(12), wa.Bottom - Height));
            Location = new Point(x, y);
            Show();
            Native.EnsureRestored(this);
            Activate();
            Native.SetForegroundWindow(Handle);
            search.Focus();
        }

        void SetMode(string m)
        {
            mode = m;
            tabClips.Font = m == "clips" ? Ui.Bold : Ui.Base;
            tabPrompts.Font = m == "prompts" ? Ui.Bold : Ui.Base;
            tabClips.BackColor = m == "clips" ? Ui.Card : Ui.Bg;
            tabPrompts.BackColor = m == "prompts" ? Ui.Card : Ui.Bg;
            tabClips.ForeColor = m == "clips" ? Ui.Accent : Ui.Muted;
            tabPrompts.ForeColor = m == "prompts" ? Ui.Accent : Ui.Muted;
            Fill();
        }

        void Fill()
        {
            string q = search.Text.Trim();
            var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            IEnumerable<object> items;
            if (mode == "clips")
            {
                items = Store.Clips
                    .Where(c => words.All(w => c.Text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Source ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0 || (c.Folder ?? "").IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    .Take(300).Cast<object>();
            }
            else
            {
                items = Store.Prompts
                    .Where(p => words.All(w => (p.Title + "\n" + p.Body + "\n" + p.Tags + "\n" + p.Category + "\n" + p.Trigger).IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(p => p.Fav).ThenByDescending(p => p.Uses).ThenByDescending(p => p.LastUsed)
                    .Take(300).Cast<object>();
            }
            list.SetItems(items);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        CardInfo Info(object o)
        {
            int idx = list.Items.IndexOf(o);
            string num = idx >= 0 && idx < 9 ? (idx + 1) + "   " : "";
            var ci = new CardInfo();
            var c = o as Clip;
            if (c != null)
            {
                if (c.Kind == "image") { ci.Title = num + "Image " + c.ImgW + " x " + c.ImgH; ci.Thumb = Thumbs.Get(c, Ui.S(90), list.ItemHeight - Ui.S(18)); }
                else if (c.Kind == "files") { ci.Title = num + Path.GetFileName(c.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)[0]); }
                else ci.Title = num + Ui.OneLine(Store.TitleFrom(c.Text), 100);
                ci.Sub = c.Kind == "image" ? "" : Ui.OneLine(c.Text, 160);
                ci.Meta = (c.Pinned ? "PINNED  ·  " : "") + (string.IsNullOrEmpty(c.Folder) ? "" : "[" + c.Folder + "]  ·  ") + Ui.Ago(c.Time);
                if (c.Pinned) { ci.Back = Ui.PinBack; ci.Strip = Ui.PinStrip; }
                var lc = Ui.LabelColor(c.Label);
                if (lc != Color.Empty) ci.Strip = lc;
                return ci;
            }
            var p = (Prompt)o;
            ci.Title = num + (p.Fav ? "★ " : "") + (p.Title.Trim().Length > 0 ? p.Title : "(untitled)");
            ci.Sub = Ui.OneLine(p.Body, 160);
            ci.Meta = (p.Trigger.Trim().Length > 0 ? p.Trigger.Trim() + "  ·  " : "") + p.Category;
            if (p.Fav) ci.Strip = Ui.PinStrip;
            return ci;
        }

        void MoveSel(int d)
        {
            if (list.Items.Count == 0) return;
            int i = list.SelectedIndex < 0 ? 0 : list.SelectedIndex + d;
            list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, i));
        }

        void ShowPasteAs(Point at)
        {
            var o = list.SelectedItem;
            if (o == null) return;
            var c = o as Clip;
            if (c != null && c.Kind == "image") return;
            busy = true;
            var m = Ui.Menu();
            for (int i = 0; i < Transforms.Names.Length; i++)
            {
                int k = i;
                m.Items.Add("Paste as " + Transforms.Names[i], null, delegate { busy = false; Pick(true, k); });
            }
            m.Items.Add(new ToolStripSeparator());
            for (int i = 1; i < Transforms.Names.Length; i++)
            {
                int k = i;
                m.Items.Add("Copy as " + Transforms.Names[i], null, delegate { busy = false; Pick(false, k); });
            }
            Ui.ThemeMenu(m);
            m.Closed += delegate { busy = false; Ui.Later(300, () => m.Dispose()); };
            m.Show(list, at);
        }

        // transform 0 = plain text as is
        void Pick(bool paste, int transform)
        {
            var o = list.SelectedItem;
            if (o == null) return;
            bool ok;
            var c = o as Clip;
            if (c != null)
            {
                if (transform == 0 || c.Kind == "image") ok = App.I.CopyClip(c);
                else
                {
                    ok = App.I.CopyText(Transforms.Apply(transform, c.Text));
                    if (ok) Store.TouchClip(c);
                }
            }
            else
            {
                var p = (Prompt)o;
                busy = true;
                string text;
                try { text = Ui.FillVariables(p.Body, this); }
                finally { busy = false; }
                if (text == null) { Activate(); search.Focus(); return; }
                ok = App.I.CopyText(Transforms.Apply(transform, text));
                if (ok) { p.Uses++; p.LastUsed = Store.Now(); Store.MarkPrompts(); }
            }
            Hide();
            if (ok && paste) App.I.PasteTo(target);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Hide(); return true;
                case Keys.Tab:
                case Keys.Shift | Keys.Tab: SetMode(mode == "clips" ? "prompts" : "clips"); search.Focus(); return true;
                case Keys.Down: MoveSel(1); return true;
                case Keys.Up: MoveSel(-1); return true;
                case Keys.PageDown: MoveSel(8); return true;
                case Keys.PageUp: MoveSel(-8); return true;
                case Keys.Enter: Pick(true, 0); return true;
                case Keys.Shift | Keys.Enter: Pick(false, 0); return true;
                case Keys.Control | Keys.Enter:
                    if (list.SelectedIndex >= 0)
                    {
                        var r = list.GetItemRectangle(list.SelectedIndex);
                        ShowPasteAs(new Point(r.X + Ui.S(40), r.Bottom - Ui.S(10)));
                    }
                    return true;
                case Keys.Control | Keys.P:
                    {
                        var c = list.SelectedItem as Clip;
                        if (c != null) { c.Pinned = !c.Pinned; Store.MarkClips(); list.Invalidate(); }
                        var p = list.SelectedItem as Prompt;
                        if (p != null) { p.Fav = !p.Fav; Store.MarkPrompts(); list.Invalidate(); }
                        return true;
                    }
                case Keys.Delete:
                    if (search.TextLength > 0) break; // Delete edits the search text first
                    {
                        var c = list.SelectedItem as Clip;
                        if (c != null)
                        {
                            int i = list.SelectedIndex;
                            Store.RemoveClips(new[] { c });
                            Fill();
                            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
                            return true;
                        }
                    }
                    break;
            }
            Keys k = keyData & Keys.KeyCode;
            if ((keyData & Keys.Modifiers) == Keys.Control && k >= Keys.D1 && k <= Keys.D9)
            {
                int i = k - Keys.D1;
                if (i < list.Items.Count) { list.SelectedIndex = i; Pick(true, 0); }
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!busy) Hide();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) Ui.Later(2000, App.I.Trim);
        }
    }
}
