using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Sidebar customization state: your page names, order, hidden pages and favorites.
    class NavState
    {
        public Dictionary<string, string> Names;
        public List<string> Order, Hidden, Fav;

        // Live view of Settings (changes go straight into Settings).
        public static NavState Of(Settings s) { return new NavState { Names = s.PageNames, Order = s.NavOrder, Hidden = s.NavHidden, Fav = s.NavFavorites }; }
        public NavState Copy() { return new NavState { Names = new Dictionary<string, string>(Names), Order = Order.ToList(), Hidden = Hidden.ToList(), Fav = Fav.ToList() }; }
        public void SaveTo(Settings s) { s.PageNames = Names; s.NavOrder = Order; s.NavHidden = Hidden; s.NavFavorites = Fav; }
        public string NameOf(MainForm.NavPage p) { string v; return Names.TryGetValue(p.Key, out v) && !string.IsNullOrEmpty(v) ? v : p.Name; }
    }

    static class NavModel
    {
        public static readonly string[] Groups = { "General", "Tools", "System" };

        // Pages in display order: Favorites first, then each group in your order.
        public static List<KeyValuePair<string, List<MainForm.NavPage>>> Layout(List<MainForm.NavPage> pages, NavState st, bool includeHidden)
        {
            Func<MainForm.NavPage, int> rank = p => { int i = st.Order.IndexOf(p.Key); return i >= 0 ? i : 1000 + pages.IndexOf(p); };
            var groups = new List<KeyValuePair<string, List<MainForm.NavPage>>>();
            var fav = st.Fav.Select(k => pages.FirstOrDefault(p => p.Key == k)).Where(p => p != null && (includeHidden || !st.Hidden.Contains(p.Key))).ToList();
            if (fav.Count > 0) groups.Add(new KeyValuePair<string, List<MainForm.NavPage>>("Favorites", fav));
            foreach (var g in Groups)
            {
                var list = pages.Where(p => p.Group == g && !fav.Contains(p) && (includeHidden || !st.Hidden.Contains(p.Key))).OrderBy(rank).ToList();
                if (list.Count > 0) groups.Add(new KeyValuePair<string, List<MainForm.NavPage>>(g, list));
            }
            return groups;
        }

        // Moves a page one place inside its group (or inside Favorites). Returns false at the edge.
        public static bool Move(List<MainForm.NavPage> pages, NavState st, MainForm.NavPage p, int delta)
        {
            var layout = Layout(pages, st, true);
            var group = layout.FirstOrDefault(g => g.Value.Contains(p));
            if (group.Value == null) return false;
            var list = group.Value;
            int i = list.IndexOf(p), j = i + delta;
            if (j < 0 || j >= list.Count) return false;
            list[i] = list[j]; list[j] = p;
            if (group.Key == "Favorites") { st.Fav.Clear(); st.Fav.AddRange(list.Select(x => x.Key)); return true; }
            // Keep a full order of every page so later moves stay stable.
            var all = layout.SelectMany(g => g.Value).Select(x => x.Key).ToList();
            foreach (var x in pages) if (!all.Contains(x.Key)) all.Add(x.Key);
            st.Order.Clear(); st.Order.AddRange(all);
            return true;
        }
    }

    // Settings > Customize sidebar: rename, reorder, hide and pin pages.
    class SidebarDialog : Form
    {
        readonly MainForm main;
        readonly NavState st;
        readonly ListBox list;
        readonly List<object> rows = new List<object>();   // string = group title, NavPage = page
        readonly Button up, down, rename, hide, fav;

        public SidebarDialog(MainForm main)
        {
            this.main = main;
            st = NavState.Of(Store.Settings).Copy();
            AutoScaleMode = AutoScaleMode.None;
            Text = "Customize sidebar - " + Ui.AppTitle;
            Icon = App.AppIcon; Font = Ui.Base; BackColor = Ui.Card; ForeColor = Ui.Text;
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = Ui.S(520, 560); MinimumSize = Ui.S(420, 420);
            Ui.DarkTitle(this);

            var hint = new Label { Dock = DockStyle.Top, Height = Ui.S(54), Padding = new Padding(Ui.S(16), Ui.S(12), Ui.S(16), 0), ForeColor = Ui.Muted, Text = "Rename, reorder, hide or pin pages." };
            list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(34), BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = Ui.Field, ForeColor = Ui.Text };
            list.DrawItem += DrawRow;
            list.SelectedIndexChanged += delegate { SkipHeader(); UpdateButtons(); };
            list.DoubleClick += delegate { Rename(); };
            list.KeyDown += (s, e) =>
            {
                if (e.Alt && e.KeyCode == Keys.Up) { MoveSel(-1); e.Handled = true; }
                else if (e.Alt && e.KeyCode == Keys.Down) { MoveSel(1); e.Handled = true; }
                else if (e.KeyCode == Keys.F2) { Rename(); e.Handled = true; }
            };
            var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(16), Ui.S(4), Ui.S(8), Ui.S(8)) };
            listHost.Controls.Add(list);

            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Ui.S(150), FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, Ui.S(4), Ui.S(12), 0) };
            up = SideBtn("Move up", delegate { MoveSel(-1); });
            down = SideBtn("Move down", delegate { MoveSel(1); });
            rename = SideBtn("Rename...", delegate { Rename(); });
            hide = SideBtn("Hide", delegate { ToggleHidden(); });
            fav = SideBtn("Add to favorites", delegate { ToggleFav(); });
            var reset = SideBtn("Reset all", delegate { st.Names.Clear(); st.Order.Clear(); st.Hidden.Clear(); st.Fav.Clear(); Fill(null); });
            side.Controls.AddRange(new Control[] { up, down, rename, hide, fav, reset });

            var bar = LayoutKit.Actions(DockStyle.Bottom);
            var ok = Ui.Btn("Save", delegate { st.SaveTo(Store.Settings); DialogResult = DialogResult.OK; }, true);
            var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
            bar.Controls.Add(ok); bar.Controls.Add(cancel);
            CancelButton = cancel;

            Controls.Add(listHost); Controls.Add(side); Controls.Add(hint); Controls.Add(bar);
            Ui.Theme(this);
            Fill(null);
        }

        Button SideBtn(string text, EventHandler click)
        {
            var b = Ui.Btn(text, click);
            b.AutoSize = false; b.Width = Ui.S(138); b.Margin = new Padding(0, 0, 0, Ui.S(6));
            return b;
        }

        MainForm.NavPage Sel { get { int i = list.SelectedIndex; return i >= 0 && i < rows.Count ? rows[i] as MainForm.NavPage : null; } }

        void Fill(MainForm.NavPage keep)
        {
            rows.Clear();
            foreach (var g in NavModel.Layout(main.NavPages, st, true)) { rows.Add(g.Key); rows.AddRange(g.Value.Cast<object>()); }
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var r in rows) list.Items.Add(r is string ? (string)r : ((MainForm.NavPage)r).Key);
            list.EndUpdate();
            int at = keep != null ? rows.IndexOf(keep) : 1;
            if (at >= 0 && at < rows.Count) list.SelectedIndex = at;
            UpdateButtons();
        }

        void SkipHeader()
        {
            int i = list.SelectedIndex;
            if (i >= 0 && i < rows.Count && rows[i] is string && i + 1 < rows.Count) list.SelectedIndex = i + 1;
        }

        void UpdateButtons()
        {
            var p = Sel;
            foreach (var b in new[] { up, down, rename, hide, fav }) b.Enabled = p != null;
            if (p == null) return;
            hide.Text = st.Hidden.Contains(p.Key) ? "Show" : "Hide";
            fav.Text = st.Fav.Contains(p.Key) ? "Unpin favorite" : "Add to favorites";
        }

        void MoveSel(int d) { var p = Sel; if (p != null && NavModel.Move(main.NavPages, st, p, d)) Fill(p); }
        void ToggleHidden() { var p = Sel; if (p == null) return; if (!st.Hidden.Remove(p.Key)) st.Hidden.Add(p.Key); Fill(p); }
        void ToggleFav() { var p = Sel; if (p == null) return; if (!st.Fav.Remove(p.Key)) st.Fav.Add(p.Key); Fill(p); }
        void Rename()
        {
            var p = Sel; if (p == null) return;
            string v = Ui.Ask("Rename page", "Name in the sidebar (empty = " + p.Name + ")", st.NameOf(p), this);
            if (v == null) return;
            v = v.Trim();
            if (v.Length == 0 || v == p.Name) st.Names.Remove(p.Key); else st.Names[p.Key] = v;
            Fill(p);
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= rows.Count) return;
            var g = e.Graphics; var r = e.Bounds;
            var head = rows[e.Index] as string;
            if (head != null)
            {
                using (var b = new SolidBrush(Ui.Field)) g.FillRectangle(b, r);
                TextRenderer.DrawText(g, head.ToUpperInvariant(), Ui.Small, new Rectangle(r.X + Ui.S(8), r.Y + Ui.S(8), r.Width - Ui.S(16), r.Height - Ui.S(8)), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            var p = (MainForm.NavPage)rows[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0, hidden = st.Hidden.Contains(p.Key);
            using (var b = new SolidBrush(sel ? Ui.SelBg : Ui.Field)) g.FillRectangle(b, r);
            var fg = hidden ? Ui.Muted : Ui.Text;
            TextRenderer.DrawText(g, p.Glyph, Ui.Icon(11f), new Rectangle(r.X + Ui.S(6), r.Y, Ui.S(28), r.Height), hidden ? Ui.Muted : Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            string name = st.NameOf(p);
            string note = (name != p.Name ? "was " + p.Name : "") + (hidden ? (name != p.Name ? ", " : "") + "hidden" : "");
            var nameSize = TextRenderer.MeasureText(name, Ui.Base, Size.Empty, TextFormatFlags.NoPadding);
            int x = r.X + Ui.S(40);
            TextRenderer.DrawText(g, name, Ui.Base, new Rectangle(x, r.Y, r.Width - x - Ui.S(8), r.Height), fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (note.Length > 0)
                TextRenderer.DrawText(g, note, Ui.Small, new Rectangle(x + nameSize.Width + Ui.S(10), r.Y, Math.Max(1, r.Right - x - nameSize.Width - Ui.S(18)), r.Height), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
