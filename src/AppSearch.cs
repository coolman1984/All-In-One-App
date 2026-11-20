using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StickyPro
{
    // Ctrl+K: search inside the app (pages, actions, tasks, notes, Markdown, prompts, agent chats with their messages,
    // folder sets, tagged items, terminal history, clipboard). A floating palette with:
    //  - scopes (All, Pages, Tasks...) - click, or Tab / Shift+Tab
    //  - inline autocomplete from your past searches and the best title (Right arrow or Tab accepts it)
    //  - favourites (Ctrl+D or the star) and history: shown when the box is empty
    //  - the matched words highlighted and a snippet of where they match
    class GlobalSearch : Form
    {
        internal enum Kind { Page, Action, Task, Note, Markdown, Prompt, Chat, FolderSet, Tagged, Terminal, Web, Clip }
        internal sealed class Hit
        {
            public Kind Group;
            public int Rank;
            public long Updated;
            public string Title, Detail;
            public string Path, Text;   // the real file / folder behind a result, and its text (for "Send to")
            public Action Open;
            public string Key { get { return Group + "|" + Title; } }
        }

        static readonly string[] GroupNames = { "Pages", "Actions", "Tasks", "Sticky notes", "Markdown", "Prompts", "Agent chats", "Folder sets", "Tagged", "Terminals", "Web pages", "Clipboard" };
        static readonly string[] Glyphs = { "", "", "", "", "", "", "", "", "", "", "", "" };
        static readonly IComparer<Hit> ResultOrder = Comparer<Hit>.Create(Compare);
        static readonly char[] LineBreaks = { '\r', '\n' };
        const string Caption = "Search this app";
        const int ClipboardLimit = 6;
        static GlobalSearch current;
        readonly MainForm main;
        readonly GhostBox box;
        readonly ResultList list;
        readonly ScopeBar scopes;
        readonly Label status;
        readonly Timer timer = new Timer();
        IEnumerator<Hit> scan;
        readonly List<Hit> results = new List<Hit>();
        string[] words = new string[0];

        public static void Open(IWin32Window owner)
        {
            if (current != null && !current.IsDisposed) { current.Activate(); current.box.Focus(); return; }
            current = new GlobalSearch(owner as MainForm);
            current.FormClosed += delegate { current = null; };
            var host = owner as Form;
            if (host != null && host.Visible && host.WindowState != FormWindowState.Minimized)
                current.Location = new Point(host.Left + (host.Width - current.Width) / 2, host.Top + Math.Min(Ui.S(90), Math.Max(0, host.Height - current.Height) / 2));
            else current.StartPosition = FormStartPosition.CenterScreen;
            current.Show(owner);
            current.Activate();
        }

        internal GlobalSearch(MainForm main)
        {
            this.main = main;
            AutoScaleMode = AutoScaleMode.None; Text = Caption; Icon = App.AppIcon;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            Font = Ui.Base; BackColor = Ui.Card; ForeColor = Ui.Text;
            ClientSize = Ui.S(800, 560); KeyPreview = true; ShowInTaskbar = false;
            Padding = new Padding(1);

            // Search row: [magnifier] [box with autocomplete ghost] [Esc]
            var top = new Panel { Dock = DockStyle.Top, Height = Ui.S(58), BackColor = Ui.Card, Padding = new Padding(Ui.S(52), Ui.S(17), Ui.S(56), 0) };
            box = new GhostBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font(Ui.Base.FontFamily, Ui.Base.Size * 1.3f), BackColor = Ui.Card, ForeColor = Ui.Text, MaxLength = 256, AccessibleName = Caption };
            box.Cue = "Search pages, actions, tasks, notes, prompts, chats...";
            top.Controls.Add(box);
            top.Paint += (s, e) =>
            {
                TextRenderer.DrawText(e.Graphics, "", Ui.Icon(13f), new Rectangle(Ui.S(16), 0, Ui.S(28), top.Height), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                var esc = new Rectangle(top.Width - Ui.S(46), (top.Height - Ui.S(22)) / 2, Ui.S(32), Ui.S(22));
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = Ui.Round(esc, Ui.S(5))) using (var pen = new Pen(Ui.Line)) e.Graphics.DrawPath(pen, p);
                TextRenderer.DrawText(e.Graphics, "Esc", Ui.Small, esc, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1);
            };
            scopes = new ScopeBar { Dock = DockStyle.Top, Height = Ui.S(42) };
            scopes.Changed += delegate { StartSearch(); box.Focus(); };
            list = new ResultList(this) { Dock = DockStyle.Fill };
            status = new Label { Dock = DockStyle.Bottom, Height = Ui.S(34), Font = Ui.Small, ForeColor = Ui.Muted, BackColor = Ui.Card,
                Padding = new Padding(Ui.S(16), 0, Ui.S(12), 0), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                Text = "↑↓ move   Enter open   Tab scope   → accept suggestion   Ctrl+D favourite   Esc close" };
            status.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, status.Width, 0); };
            Controls.Add(list); Controls.Add(status); Controls.Add(scopes); Controls.Add(top);
            timer.Tick += SearchTick;
            box.TextChanged += delegate { StartSearch(); };
            list.Opened += RunRow;
            Deactivate += delegate { if (!IsDisposed && !list.MenuOpen) Close(); };
            Shown += delegate { box.Focus(); StartSearch(); };
            HandleCreated += delegate { try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { } };
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }   // drop shadow
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var pen = new Pen(Ui.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }

        // ---------- searching ----------
        void StartSearch()
        {
            StopSearch();
            results.Clear();
            string query = box.Text.Trim();
            words = query.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (query.Length == 0) { ShowHome(); box.Ghost = ""; return; }
            timer.Interval = 90;
            timer.Start();
        }

        void SearchTick(object sender, EventArgs e)
        {
            if (scan == null) scan = Find(main, box.Text.Trim()).GetEnumerator();
            var budget = Stopwatch.StartNew();   // keep the window responsive on big histories
            do
            {
                if (!scan.MoveNext()) { StopSearch(); ShowResults(); return; }
                var h = scan.Current;
                if (h != null && (scopes.Scope < 0 || (int)h.Group == scopes.Scope)) KeepResult(results, h);
            } while (budget.ElapsedMilliseconds < 8);
            timer.Interval = 10;
        }

        void StopSearch() { timer.Stop(); if (scan != null) { scan.Dispose(); scan = null; } }

        void ShowResults()
        {
            var rows = new List<object>();
            Kind? last = null;
            foreach (var h in results)
            {
                if (last != h.Group) { rows.Add(GroupNames[(int)h.Group].ToUpperInvariant()); last = h.Group; }
                rows.Add(h);
            }
            if (rows.Count == 0) rows.Add(new Note { Text = "Nothing in the app matches \"" + box.Text.Trim() + "\"" + (scopes.Scope >= 0 ? " in " + GroupNames[scopes.Scope] + ". Try All." : ".") });
            list.SetRows(rows, words);
            box.Ghost = Suggest(box.Text);
        }

        // Empty box: favourites, recent searches and recently opened items.
        void ShowHome()
        {
            var rows = new List<object>();
            var favs = MetaStore.Favorites("search").Take(8).Select(m => Resolve(m.Id)).Where(h => h != null).ToList();
            if (favs.Count > 0) { rows.Add("FAVOURITES"); rows.AddRange(favs.Cast<object>()); }
            var searches = Store.Settings.SearchHistory.Take(6).ToList();
            if (searches.Count > 0) { rows.Add("RECENT SEARCHES"); rows.AddRange(searches.Select(s => (object)new Past { Text = s })); }
            var opened = MetaStore.Recent("search", 12).Where(m => !m.Fav).Take(6).Select(m => Resolve(m.Id)).Where(h => h != null).ToList();
            if (opened.Count > 0) { rows.Add("RECENTLY OPENED"); rows.AddRange(opened.Cast<object>()); }
            if (rows.Count == 0) rows.Add(new Note { Text = "Type to search everything you keep in the app. Star results with Ctrl+D to keep them here." });
            list.SetRows(rows, new string[0]);
        }

        // A favourite or recent item stored as "Group|Title": search for the title again and take the same item.
        Hit Resolve(string key)
        {
            int bar = key.IndexOf('|');
            if (bar <= 0) return null;
            string group = key.Substring(0, bar), title = key.Substring(bar + 1);
            try { return Find(main, title).FirstOrDefault(h => h != null && h.Group.ToString() == group && h.Title == title); }
            catch { return null; }
        }

        // Autocomplete: a past search, else the best result title, that starts with what you typed.
        string Suggest(string typed)
        {
            if (string.IsNullOrEmpty(typed) || typed.EndsWith(" ")) return "";
            string s = Store.Settings.SearchHistory.FirstOrDefault(x => x.Length > typed.Length && x.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                ?? results.Select(h => h.Title).FirstOrDefault(x => x != null && x.Length > typed.Length && x.StartsWith(typed, StringComparison.OrdinalIgnoreCase));
            return s == null ? "" : s.Substring(typed.Length);
        }

        void Remember(string query)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2) return;
            var h = Store.Settings.SearchHistory;
            h.RemoveAll(x => string.Equals(x, query, StringComparison.OrdinalIgnoreCase));
            h.Insert(0, query);
            if (h.Count > 30) h.RemoveRange(30, h.Count - 30);
            Store.MarkSettings();
        }

        void RunRow(object row)
        {
            var past = row as Past;
            if (past != null) { box.Text = past.Text; box.SelectionStart = box.TextLength; return; }
            var hit = row as Hit;
            if (hit == null || hit.Open == null) return;
            Remember(box.Text);
            MetaStore.Touch("search", hit.Key);
            Close();
            try { hit.Open(); } catch (Exception ex) { App.Log("Search open: " + ex.Message); }
        }

        void ToggleFav(Hit h)
        {
            if (h == null) return;
            MetaStore.ToggleFav("search", h.Key);
            list.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keys)
        {
            if (keys == Keys.Escape) { if (box.TextLength > 0) box.Clear(); else Close(); return true; }
            if (keys == Keys.Enter) { RunRow(list.Current); return true; }
            if (keys == Keys.Up || keys == Keys.Down) { list.Move(keys == Keys.Up ? -1 : 1); return true; }
            if (keys == Keys.PageUp || keys == Keys.PageDown) { list.Move(keys == Keys.PageUp ? -8 : 8); return true; }
            if (keys == (Keys.Control | Keys.D)) { ToggleFav(list.Current as Hit); return true; }
            if ((keys == Keys.Tab || keys == Keys.Right) && box.Ghost.Length > 0 && box.SelectionStart == box.TextLength) { box.AcceptGhost(); return true; }
            if (keys == Keys.Tab) { scopes.Step(1); return true; }
            if (keys == (Keys.Shift | Keys.Tab)) { scopes.Step(-1); return true; }
            if (keys == Keys.Delete && box.TextLength == 0 && list.Current is Past)
            {
                Store.Settings.SearchHistory.RemoveAll(x => x == ((Past)list.Current).Text); Store.MarkSettings(); ShowHome(); return true;
            }
            return base.ProcessCmdKey(ref msg, keys);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopSearch(); timer.Dispose(); }
            base.Dispose(disposing);
        }

        class Past { public string Text; }
        class Note { public string Text; }

        // ---------- result list (owner drawn: headers, rows with icon, highlighted title, snippet, star) ----------
        class ResultList : Control
        {
            readonly GlobalSearch owner;
            List<object> rows = new List<object>();
            string[] marks = new string[0];
            int sel = -1, hover = -1, scroll;
            public bool MenuOpen;
            public event Action<object> Opened;
            int RowH { get { return Ui.S(52); } }
            int HeadH { get { return Ui.S(30); } }
            public ResultList(GlobalSearch owner)
            {
                this.owner = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Ui.Card;
            }
            public object Current { get { return sel >= 0 && sel < rows.Count ? rows[sel] : null; } }
            static bool Pickable(object o) { return o is Hit || o is Past; }
            int H(object o) { return o is string ? HeadH : o is Past ? Ui.S(38) : RowH; }
            int Top(int i) { int y = Ui.S(6); for (int k = 0; k < i; k++) y += H(rows[k]); return y; }
            int Total { get { return Top(rows.Count) + Ui.S(8); } }

            public void SetRows(List<object> r, string[] words)
            {
                rows = r; marks = words; scroll = 0; hover = -1;
                sel = rows.FindIndex(Pickable);
                Invalidate();
            }
            public void Move(int d)
            {
                if (rows.Count == 0) return;
                int i = sel, steps = Math.Abs(d), dir = Math.Sign(d);
                while (steps > 0) { int j = i + dir; while (j >= 0 && j < rows.Count && !Pickable(rows[j])) j += dir; if (j < 0 || j >= rows.Count) break; i = j; steps--; }
                if (i != sel) { sel = i; Reveal(); Invalidate(); }
            }
            void Reveal()
            {
                int t = Top(sel), b = t + H(rows[sel]);
                if (t - (sel > 0 && rows[sel - 1] is string ? HeadH : 0) < scroll) scroll = Math.Max(0, t - (sel > 0 && rows[sel - 1] is string ? HeadH : 0) - Ui.S(4));
                else if (b > scroll + Height) scroll = b - Height + Ui.S(6);
            }
            int At(Point p) { int y = p.Y + scroll; for (int i = 0; i < rows.Count; i++) { int t = Top(i); if (y >= t && y < t + H(rows[i])) return i; } return -1; }
            Rectangle StarRect(Rectangle r) { return new Rectangle(r.Right - Ui.S(40), r.Y + (r.Height - Ui.S(28)) / 2, Ui.S(28), Ui.S(28)); }

            protected override void OnMouseWheel(MouseEventArgs e) { scroll = Math.Max(0, Math.Min(Math.Max(0, Total - Height), scroll - e.Delta / 2)); Invalidate(); }
            protected override void OnMouseMove(MouseEventArgs e) { int i = At(e.Location); if (i != hover) { hover = i; Cursor = i >= 0 && Pickable(rows[i]) ? Cursors.Hand : Cursors.Default; Invalidate(); } }
            protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
            protected override void OnMouseClick(MouseEventArgs e)
            {
                int i = At(e.Location);
                if (i < 0 || !Pickable(rows[i])) return;
                sel = i; Invalidate();
                var hit = rows[i] as Hit;
                var r = new Rectangle(Ui.S(8), Top(i) - scroll, Width - Ui.S(16), H(rows[i]));
                if (e.Button == MouseButtons.Right && hit != null)
                {
                    var m = Ui.Menu(); Ui.ThemeMenu(m);
                    m.Items.Add("Open", null, delegate { var h = Opened; if (h != null) h(hit); });
                    m.Items.Add(MetaStore.IsFav("search", hit.Key) ? "Remove from favourites" : "Add to favourites", null, delegate { owner.ToggleFav(hit); });
                    m.Items.Add("Copy title", null, delegate { App.I.CopyText(hit.Title); });
                    if (!string.IsNullOrEmpty(hit.Path)) { m.Items.Add(new ToolStripSeparator()); AppLinks.AddPathItems(m.Items, hit.Path, owner); }
                    else if (!string.IsNullOrEmpty(hit.Text)) { m.Items.Add(new ToolStripSeparator()); m.Items.Add(AppLinks.SendTo(null, hit.Text, owner)); }
                    Ui.ThemeMenu(m);
                    MenuOpen = true; m.Closed += delegate { MenuOpen = false; owner.box.Focus(); };
                    m.Show(this, e.Location);
                    return;
                }
                if (hit != null && StarRect(r).Contains(e.Location)) { owner.ToggleFav(hit); return; }
                if (e.Button == MouseButtons.Left) { var h = Opened; if (h != null) h(rows[i]); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Ui.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
                int y = Ui.S(6) - scroll, x = Ui.S(8), w = Width - Ui.S(16);
                for (int i = 0; i < rows.Count; i++)
                {
                    var o = rows[i]; int h = H(o);
                    if (y + h < 0) { y += h; continue; }
                    if (y > Height) break;
                    var r = new Rectangle(x, y, w, h);
                    if (o is string) TextRenderer.DrawText(g, (string)o, Ui.Small, new Rectangle(x + Ui.S(10), y + Ui.S(10), w, h - Ui.S(10)), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    else if (o is Note) TextRenderer.DrawText(g, ((Note)o).Text, Ui.Base, new Rectangle(x + Ui.S(10), y, w - Ui.S(20), Ui.S(80)), Ui.Muted, TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter);
                    else
                    {
                        if (i == sel || i == hover)
                            using (var p = Ui.Round(Rectangle.Inflate(r, 0, -Ui.S(2)), Ui.S(8))) using (var b = new SolidBrush(i == sel ? Ui.SelBg : Ui.HoverBg)) g.FillPath(b, p);
                        var past = o as Past;
                        if (past != null)
                        {
                            TextRenderer.DrawText(g, "", Ui.Icon(10f), new Rectangle(x + Ui.S(14), y, Ui.S(20), h), Ui.Muted, TextFormatFlags.VerticalCenter);
                            TextRenderer.DrawText(g, past.Text, Ui.Base, new Rectangle(x + Ui.S(44), y, w - Ui.S(120), h), Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                            if (i == sel) TextRenderer.DrawText(g, "Del removes", Ui.Small, new Rectangle(r.Right - Ui.S(110), y, Ui.S(100), h), Ui.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                        }
                        else DrawHit(g, (Hit)o, r, i == sel);
                    }
                    y += h;
                }
            }

            void DrawHit(Graphics g, Hit hit, Rectangle r, bool selected)
            {
                // icon tile
                var tile = new Rectangle(r.X + Ui.S(8), r.Y + (r.Height - Ui.S(34)) / 2, Ui.S(34), Ui.S(34));
                using (var p = Ui.Round(tile, Ui.S(8))) using (var b = new SolidBrush(Ui.Mix(Ui.Card, Ui.Accent, Ui.Dark ? 0.22 : 0.12))) g.FillPath(b, p);
                TextRenderer.DrawText(g, Glyphs[(int)hit.Group], Ui.Icon(11f), tile, Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                int tx = tile.Right + Ui.S(12), right = r.Right - Ui.S(150);
                bool hasDetail = !string.IsNullOrEmpty(hit.Detail);
                var titleRect = new Rectangle(tx, r.Y + (hasDetail ? Ui.S(7) : 0), right - tx, hasDetail ? Ui.S(22) : r.Height);
                DrawMarked(g, hit.Title ?? "", titleRect);
                if (hasDetail) TextRenderer.DrawText(g, hit.Detail, Ui.Small, new Rectangle(tx, r.Y + Ui.S(28), right - tx, Ui.S(18)), Ui.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, selected ? "Enter" : GroupNames[(int)hit.Group], Ui.Small, new Rectangle(right, r.Y, Ui.S(100), r.Height), Ui.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                bool fav = MetaStore.IsFav("search", hit.Key);
                if (fav || selected) TextRenderer.DrawText(g, fav ? "" : "", Ui.Icon(11f), StarRect(r), fav ? Color.FromArgb(240, 170, 0) : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // The title with every searched word in bold accent.
            void DrawMarked(Graphics g, string text, Rectangle r)
            {
                const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
                var spans = new bool[text.Length];
                foreach (var m in marks) { if (m.Length == 0) continue; int at = 0; while ((at = text.IndexOf(m, at, StringComparison.OrdinalIgnoreCase)) >= 0) { for (int k = at; k < at + m.Length; k++) spans[k] = true; at += m.Length; } }
                int x = r.X, i = 0;
                while (i < text.Length && x < r.Right)
                {
                    bool on = spans[i]; int j = i; while (j < text.Length && spans[j] == on) j++;
                    string part = text.Substring(i, j - i);
                    var font = on ? Ui.Bold : Ui.Base;
                    int pw = TextRenderer.MeasureText(g, part, font, new Size(int.MaxValue, r.Height), f).Width;
                    if (x + pw > r.Right) { TextRenderer.DrawText(g, part, font, new Rectangle(x, r.Y, r.Right - x, r.Height), on ? Ui.Accent : Ui.Text, f | TextFormatFlags.EndEllipsis); break; }
                    TextRenderer.DrawText(g, part, font, new Rectangle(x, r.Y, pw, r.Height), on ? Ui.Accent : Ui.Text, f);
                    x += pw; i = j;
                }
            }
        }

        // ---------- scope chips ----------
        class ScopeBar : Control
        {
            static readonly int[] Order = { -1, (int)Kind.Page, (int)Kind.Action, (int)Kind.Task, (int)Kind.Note, (int)Kind.Markdown, (int)Kind.Prompt, (int)Kind.Chat, (int)Kind.FolderSet, (int)Kind.Tagged, (int)Kind.Terminal, (int)Kind.Web, (int)Kind.Clip };
            public int Scope = -1;
            public event Action Changed;
            readonly List<Rectangle> chips = new List<Rectangle>();
            int hover = -1;
            public ScopeBar() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); BackColor = Ui.Card; }
            static readonly string[] Short = { "Pages", "Actions", "Tasks", "Notes", "Markdown", "Prompts", "Chats", "Folders", "Tagged", "Terminal", "Web", "Clipboard" };
            static string Name(int k) { return k < 0 ? "All" : Short[k]; }
            public void Step(int d) { int i = Array.IndexOf(Order, Scope); Scope = Order[(i + d + Order.Length) % Order.Length]; Invalidate(); var h = Changed; if (h != null) h(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(Ui.Card); g.SmoothingMode = SmoothingMode.AntiAlias;
                chips.Clear();
                int x = Ui.S(14), y = Ui.S(8), h = Ui.S(26);
                for (int i = 0; i < Order.Length; i++)
                {
                    string t = Name(Order[i]);
                    int w = TextRenderer.MeasureText(g, t, Ui.Small).Width + Ui.S(14);
                    var r = new Rectangle(x, y, w, h); chips.Add(r);
                    bool on = Order[i] == Scope;
                    using (var p = Ui.Round(r, h / 2))
                    {
                        using (var b = new SolidBrush(on ? Ui.AccentFill : i == hover ? Ui.HoverBg : Ui.Card)) g.FillPath(b, p);
                        if (!on) using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
                    }
                    TextRenderer.DrawText(g, t, Ui.Small, r, on ? Color.White : Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x += w + Ui.S(6);
                }
            }
            protected override void OnMouseMove(MouseEventArgs e) { int i = chips.FindIndex(r => r.Contains(e.Location)); if (i != hover) { hover = i; Cursor = i >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); } }
            protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }
            protected override void OnMouseClick(MouseEventArgs e) { int i = chips.FindIndex(r => r.Contains(e.Location)); if (i < 0) return; Scope = Order[i]; Invalidate(); var h = Changed; if (h != null) h(); }
        }

        // ---------- the search box: placeholder and inline autocomplete ("ghost" text after the caret) ----------
        class GhostBox : TextBox
        {
            public string Cue = "";
            string ghost = "";
            public string Ghost { get { return ghost; } set { value = value ?? ""; if (value != ghost) { ghost = value; Invalidate(); } } }
            public void AcceptGhost() { if (ghost.Length == 0) return; string add = ghost; ghost = ""; Text += add; SelectionStart = TextLength; }
            protected override void OnTextChanged(EventArgs e) { ghost = ""; base.OnTextChanged(e); Invalidate(); }
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0xF || !IsHandleCreated) return;
                using (var g = Graphics.FromHwnd(Handle))
                {
                    const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.Top;
                    if (TextLength == 0) { TextRenderer.DrawText(g, Cue, Font, new Rectangle(1, 0, Width, Height), Ui.Muted, f | TextFormatFlags.EndEllipsis); return; }
                    if (ghost.Length == 0 || SelectionStart != TextLength) return;
                    int tw = TextRenderer.MeasureText(g, Text, Font, new Size(int.MaxValue, Height), f).Width;
                    if (tw < Width - Ui.S(20)) TextRenderer.DrawText(g, ghost, Font, new Rectangle(1 + tw, 0, Width - tw, Height), Ui.Muted, f);
                }
            }
        }

        // ---------- ranking (also used by tools\test-forms.ps1) ----------
        internal static void KeepResult(List<Hit> hits, Hit hit)
        {
            int i = hits.BinarySearch(hit, ResultOrder);
            if (i < 0) i = ~i;
            hits.Insert(i, hit);
            int left = i, right = i + 1;
            while (left > 0 && hits[left - 1].Group == hit.Group) left--;
            while (right < hits.Count && hits[right].Group == hit.Group) right++;
            if (right - left > (hit.Group == Kind.Clip ? ClipboardLimit : 12)) hits.RemoveAt(right - 1);
        }

        static int Compare(Hit a, Hit b)
        {
            int c = ((int)a.Group).CompareTo((int)b.Group);
            if (c == 0) c = a.Rank.CompareTo(b.Rank);
            if (c == 0) c = b.Updated.CompareTo(a.Updated);
            if (c == 0) c = StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title);
            if (c == 0) c = StringComparer.Ordinal.Compare(a.Detail, b.Detail);
            return c;
        }

        sealed class Query
        {
            readonly string phrase;
            readonly string[] words;
            public Query(string text)
            {
                words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                phrase = string.Join(" ", words);
            }

            public Hit Match(Kind kind, string title, string fields, string body, long updated)
            {
                title = title ?? "";
                int rank = Rank(title, fields, body);
                if (rank < 0) return null;
                return new Hit { Group = kind, Rank = rank, Title = title, Updated = updated, Detail = rank == 3 ? Snippet(body) : null };
            }

            public int Rank(string title, string fields, string body)
            {
                if (string.Equals(title, phrase, StringComparison.OrdinalIgnoreCase)) return 0;
                if (title != null && title.StartsWith(phrase, StringComparison.OrdinalIgnoreCase)) return 1;
                bool titleOnly = true;
                foreach (string word in words)
                {
                    if (Has(title, word)) continue;
                    titleOnly = false;
                    if (!Has(fields, word) && !Has(body, word)) return -1;
                }
                return titleOnly ? 2 : 3;
            }

            // Where the first word matches inside a longer text, on one line.
            public string Snippet(string body)
            {
                if (string.IsNullOrEmpty(body)) return null;
                foreach (var w in words)
                {
                    int i = body.IndexOf(w, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) continue;
                    int start = Math.Max(0, i - 40);
                    string s = Ui.OneLine(body.Substring(start, Math.Min(body.Length - start, 160)), 120);
                    return (start > 0 ? "…" : "") + s;
                }
                return null;
            }

            static bool Has(string text, string word) { return text != null && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        struct ClipMatch { public Clip Clip; public int Rank; }

        // Each yield advances one candidate. Snapshot lists of references on the UI thread so
        // edits between timer ticks cannot invalidate an active enumerator.
        internal static IEnumerable<Hit> Find(MainForm main, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;
            var q = new Query(text);
            var settings = q.Match(Kind.Page, "Settings", "preferences appearance theme font shortcuts", null, 0);
            if (settings != null) settings.Open = () => App.I.ShowSettings(main);
            yield return settings;
            var shortcuts = q.Match(Kind.Page, "Shortcuts", "keyboard hotkeys", null, 0);
            if (shortcuts != null) shortcuts.Open = () => App.I.ShowMain("shortcuts");
            yield return shortcuts;
            if (main != null)
                foreach (var page in main.NavPages.ToArray())
                {
                    var hit = q.Match(Kind.Page, Ui.PageName(page.Key, page.Name), page.Key, page.Name, 0);
                    if (hit != null) { hit.Detail = "Page"; hit.Open = () => App.I.ShowMain(page.Key); }
                    yield return hit;
                }
            foreach (var a in Actions())
            {
                var hit = q.Match(Kind.Action, a.Item1, a.Item2, null, 0);
                if (hit != null) { hit.Detail = a.Item2; hit.Open = a.Item3; }
                yield return hit;
            }
            foreach (var task in Store.Tasks.ToArray())
            {
                var hit = q.Match(Kind.Task, task.Title, task.List, task.Notes, task.Modified);
                if (hit != null)
                {
                    hit.Detail = hit.Detail ?? (task.Done ? "Completed" : task.List); hit.Text = task.Title + (string.IsNullOrEmpty(task.Notes) ? "" : "\n" + task.Notes);
                    hit.Open = () => { App.I.ShowMain("tasks"); var window = Application.OpenForms.OfType<MainForm>().FirstOrDefault(); if (window != null) window.SelectTask(task); };
                }
                yield return hit;
            }
            foreach (var note in Store.Notes.ToArray())
            {
                var hit = q.Match(Kind.Note, Preview(note.Text, "Untitled note"), note.Tags, note.Text, note.Modified);
                if (hit != null) { hit.Detail = hit.Detail ?? (note.Archived ? "Archived" : note.Tags); hit.Text = note.Text; hit.Open = () => App.I.OpenNote(note); }
                yield return hit;
            }
            foreach (string path in MdVault.SearchPaths())
            {
                var hit = q.Match(Kind.Markdown, NameOf(path, true), null, MdVault.TextOf(path), 0);   // name first, then the text inside
                if (hit != null) { hit.Detail = path; hit.Path = path; hit.Open = () => App.I.OpenMarkdown(path); }
                yield return hit;
            }
            foreach (var prompt in Store.Prompts.ToArray())
            {
                var hit = q.Match(Kind.Prompt, prompt.Title, prompt.Category + " " + prompt.Tags, prompt.Body, prompt.Modified);
                if (hit != null)
                {
                    hit.Detail = (hit.Detail ?? prompt.Category) + "   ·   Enter copies"; hit.Text = prompt.Body;
                    hit.Open = () => { if (App.I.CopyText(prompt.Body)) { prompt.Uses++; prompt.LastUsed = Store.Now(); Store.MarkPrompts(); App.I.Balloon("Prompt copied", prompt.Title); } };
                }
                yield return hit;
            }
            if (AgentStore.Loaded)
                foreach (var chat in AgentStore.Chats.ToArray())
                {
                    // Title and folder first; else the newest message that contains the words (shown as the snippet).
                    var hit = q.Match(Kind.Chat, chat.Title, chat.Folder, null, chat.Updated);
                    if (hit != null) hit.Detail = chat.Folder;
                    else
                    {
                        var msgs = chat.Messages.ToArray();
                        for (int i = msgs.Length - 1; i >= Math.Max(0, msgs.Length - 400) && hit == null; i--)
                            if (msgs[i].Role != "tool" && msgs[i].Role != "info" && msgs[i].Role != "error")
                                hit = q.Match(Kind.Chat, chat.Title, chat.Folder, msgs[i].Text, chat.Updated);
                    }
                    if (hit != null) { hit.Path = chat.Folder; hit.Open = () => App.I.OpenAgentChat(chat); }
                    yield return hit;
                }
            foreach (var set in FolderSets.All.ToArray())
            {
                string first = set.Folders.FirstOrDefault();
                var hit = q.Match(Kind.FolderSet, set.Name, string.Join(" ", set.Folders), set.Note, set.Used);
                if (hit != null && !string.IsNullOrEmpty(first)) { hit.Detail = hit.Detail ?? first; hit.Path = first; hit.Open = () => App.I.OpenInExplorerPage(first); }
                else hit = null;
                yield return hit;
            }
            foreach (var tagged in Store.Tagged.ToArray())
            {
                var hit = q.Match(Kind.Tagged, NameOf(tagged.Path, false), tagged.Path + " " + tagged.Category, tagged.Note, tagged.Added);
                if (hit != null && !string.IsNullOrEmpty(tagged.Path))
                {
                    hit.Detail = hit.Detail ?? (tagged.Category + "   ·   " + tagged.Path); hit.Path = tagged.Path;
                    hit.Open = () => { if (tagged.IsFolder) App.I.OpenInExplorerPage(tagged.Path); else Process.Start(new ProcessStartInfo(tagged.Path) { UseShellExecute = true }); };
                }
                else hit = null;
                yield return hit;
            }
            foreach (var terminal in TermHistory.All.ToArray())
            {
                var hit = q.Match(Kind.Terminal, terminal.Title, null, null, terminal.Time);
                if (hit != null) { hit.Detail = "Terminal history"; hit.Open = () => TermHistory.Show(main); }
                yield return hit;
            }
            // Browser history and favourites (page title, then the address).
            foreach (var w in MetaStore.All("web").ToArray())
            {
                string url = w.Id;
                var hit = q.Match(Kind.Web, string.IsNullOrWhiteSpace(w.Name) ? url : w.Name, url, null, w.Used);
                if (hit != null) { hit.Detail = (w.Fav ? "★ " : "") + url; hit.Text = url; hit.Open = () => App.I.OpenInBrowser(url); }
                yield return hit;
            }
            // Build previews and actions only for the best matches in a potentially large clipboard history.
            var clips = new List<ClipMatch>(ClipboardLimit + 1);
            foreach (var clip in Store.Clips.ToArray())
            {
                int rank = q.Rank(clip.Label, clip.Source, clip.Text);
                if (rank >= 0)
                {
                    int i = 0;
                    while (i < clips.Count && (clips[i].Rank < rank || (clips[i].Rank == rank && clips[i].Clip.Time >= clip.Time))) i++;
                    if (i < ClipboardLimit)
                    {
                        clips.Insert(i, new ClipMatch { Clip = clip, Rank = rank });
                        if (clips.Count > ClipboardLimit) clips.RemoveAt(ClipboardLimit);
                    }
                }
                yield return null;
            }
            foreach (var match in clips)
            {
                var clip = match.Clip;
                string age = clip.Time > 0 && clip.Time <= DateTime.MaxValue.Ticks ? Ui.Ago(clip.Time) + "   ·   " : "";
                yield return new Hit
                {
                    Group = Kind.Clip, Rank = match.Rank, Updated = clip.Time,
                    Title = Preview(clip.Text, string.IsNullOrWhiteSpace(clip.Label) ? "Clipboard item" : clip.Label),
                    Detail = age + "Enter copies", Text = clip.Text,
                    Open = () => { if (App.I.CopyClip(clip)) App.I.Balloon("Copied", "Clipboard item copied"); }
                };
            }
        }

        // Things you can do from the search box (name, words it also answers to, action).
        static IEnumerable<Tuple<string, string, Action>> Actions()
        {
            yield return Tuple.Create("New sticky note", "create add note", (Action)(() => App.I.NewNote(null)));
            yield return Tuple.Create("New prompt", "create add snippet", (Action)(() => App.I.NewPrompt()));
            yield return Tuple.Create("Snip the screen", "screenshot capture", (Action)(() => App.I.StartSnip("rect", 0, false)));
            yield return Tuple.Create("Record the screen", "video capture recorder", (Action)(() => App.I.StartRecord("rect")));
            yield return Tuple.Create(Ui.Dark ? "Switch to light mode" : "Switch to dark mode", "theme dark light mode", (Action)(() => App.I.ToggleDark()));
            yield return Tuple.Create("Choose a look", "theme design colour color accent", (Action)(() => LookPicker.Open(Application.OpenForms.OfType<MainForm>().FirstOrDefault() ?? (Control)new Control(), Cursor.Position)));
            yield return Tuple.Create("Show or hide floating cards", "cards widgets", (Action)(() => App.I.ToggleCards()));
        }

        static string Preview(string text, string fallback)
        {
            if (string.IsNullOrEmpty(text)) return fallback;
            int count = Math.Min(240, text.Length);
            int lineEnd = text.IndexOfAny(LineBreaks, 0, count);
            string title = Ui.OneLine(text.Substring(0, lineEnd < 0 ? count : lineEnd), 80);
            return title.Length == 0 ? fallback : title;
        }

        static string NameOf(string path, bool omitExtension)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return omitExtension ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path.TrimEnd('\\', '/')); }
            catch (ArgumentException) { return ""; }
        }
    }
}
