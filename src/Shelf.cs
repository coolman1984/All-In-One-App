using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Favourites + recent items of one MetaStore scope, drawn as a grid of tiles that fills whatever space it gets
    // (more columns on wide windows, scrolls when tall). Used for project folders on Terminals, Explorer and Agents.
    // Click = Open; star = favourite; right-click = item menu (caller's items + Organize).
    class ItemShelf : Panel
    {
        public readonly string Scope;
        public string Title = "Folders";
        public string Glyph = "";
        public Func<IEnumerable<string>> ExtraRecent;      // ids from older history lists (shown after the tracked ones)
        public event Action<string> Open;
        public event Action AddClicked;
        public Action<string, ContextMenuStrip> MenuItems;  // add item-specific commands to the right-click menu
        public int MaxRecent = 24;

        class Tile { public string Id, Name, Sub; public bool Fav, Missing; public Color Color; public Rectangle R, Star; }
        readonly List<Tile> tiles = new List<Tile>();
        readonly List<KeyValuePair<string, int>> heads = new List<KeyValuePair<string, int>>();
        Rectangle addLink, clearLink;
        Tile hot; bool hotStar, hotAdd, hotClear;
        bool dirty = true;
        readonly ContextMenuStrip menu = new ContextMenuStrip();

        public ItemShelf(string scope)
        {
            Scope = scope;
            DoubleBuffered = true;   // own scrolling: native AutoScroll moved old pixels and left blank, overlapping tiles
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Store.MetaChanged += OnMeta;
            Ui.ThemeMenu(menu);
        }
        void OnMeta() { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke((Action)OnMeta); return; } Reload(); }
        protected override void Dispose(bool disposing) { if (disposing) { Store.MetaChanged -= OnMeta; menu.Dispose(); } base.Dispose(disposing); }

        public void Reload() { dirty = true; if (Visible) { Build(); Invalidate(); } }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible && dirty) { Build(); Invalidate(); } }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }

        // ----- scrolling done here (whole repaint every time; a thin bar on the right that can be dragged) -----
        int scrollY, contentH; bool dragBar; int dragFrom, dragStart;
        int MaxScroll { get { return Math.Max(0, contentH - ClientSize.Height); } }
        void ScrollTo(int y) { y = Math.Max(0, Math.Min(MaxScroll, y)); if (y != scrollY) { scrollY = y; Invalidate(); } }
        Rectangle Thumb
        {
            get
            {
                if (MaxScroll <= 0) return Rectangle.Empty;
                int h = Math.Max(Ui.S(30), ClientSize.Height * ClientSize.Height / Math.Max(1, contentH));
                int y = (ClientSize.Height - h) * scrollY / MaxScroll;
                return new Rectangle(ClientSize.Width - Ui.S(8), y, Ui.S(6), h);
            }
        }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); ScrollTo(scrollY - e.Delta * Ui.S(60) / 120); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var t = Thumb;
            if (e.Button == MouseButtons.Left && MaxScroll > 0 && e.X >= ClientSize.Width - Ui.S(12))
            {
                if (t.Contains(e.Location)) { dragBar = true; dragFrom = e.Y; dragStart = scrollY; Capture = true; }
                else ScrollTo(scrollY + (e.Y < t.Y ? -ClientSize.Height : ClientSize.Height));
            }
        }

        static string NameOf(string id) { string s = id.TrimEnd('\\', '/'); int c = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/')); return c >= 0 && c < s.Length - 1 ? s.Substring(c + 1) : id; }

        void Build()
        {
            dirty = false;
            tiles.Clear();
            var slow = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<string, ItemMeta, Tile> make = (id, m) =>
            {
                var t = new Tile { Id = id, Name = m != null ? MetaStore.DisplayName(m) : NameOf(id), Sub = id, Fav = m != null && m.Fav, Color = m != null ? MetaStore.ColorOf(m.Color) : Color.Empty };
                // Local folders are checked at once; network folders on a worker (a slow share froze the Terminals page).
                if (DriveCache.IsSlowPath(id)) slow.Add(id);
                else try { t.Missing = id.Length > 2 && id[1] == ':' && !Directory.Exists(id) && !File.Exists(id); } catch { }
                if (m != null && !string.IsNullOrEmpty(m.Note)) t.Sub = m.Note;
                return t;
            };
            foreach (var m in MetaStore.Favorites(Scope)) if (seen.Add(m.Id)) tiles.Add(make(m.Id, m));
            int favCount = tiles.Count;
            foreach (var m in MetaStore.Recent(Scope, MaxRecent)) if (seen.Add(m.Id)) tiles.Add(make(m.Id, m));
            if (ExtraRecent != null)
                foreach (var id in ExtraRecent() ?? Enumerable.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(id) && tiles.Count - favCount < MaxRecent && seen.Add(id)) tiles.Add(make(id, MetaStore.Get(Scope, id)));
            this.favCount = favCount;
            Arrange();
            if (slow.Count > 0)
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var id in slow) { try { if (!Directory.Exists(id) && !File.Exists(id)) missing.Add(id); } catch { } }
                    if (missing.Count == 0) return;
                    try { BeginInvoke((Action)delegate { foreach (var t in tiles) if (missing.Contains(t.Id)) t.Missing = true; Invalidate(); }); } catch { }
                });
        }
        int favCount;

        void Arrange()
        {
            heads.Clear();
            int pad = Ui.S(2), gap = Ui.S(8), th = Ui.S(52), minW = Ui.S(230);
            int w = ClientSize.Width - pad * 2 - Ui.S(12);   // room for the scroll bar
            int cols = Math.Max(1, (w + gap) / (minW + gap));
            int tw = Math.Max(Ui.S(120), (w - gap * (cols - 1)) / cols);
            int y = Ui.S(34);
            addLink = Rectangle.Empty; clearLink = Rectangle.Empty;
            Action<string, int, int> section = (title, from, to) =>
            {
                heads.Add(new KeyValuePair<string, int>(title, y));
                y += Ui.S(24);
                for (int i = from; i < to; i++)
                {
                    int k = i - from;
                    var r = new Rectangle(pad + (k % cols) * (tw + gap), y + (k / cols) * (th + gap), tw, th);
                    tiles[i].R = r;
                    tiles[i].Star = new Rectangle(r.Right - Ui.S(30), r.Y + (r.Height - Ui.S(24)) / 2, Ui.S(24), Ui.S(24));
                }
                int rows = (to - from + cols - 1) / cols;
                y += Math.Max(rows, to == from ? 0 : 1) * (th + gap) + (to == from ? Ui.S(22) : Ui.S(6));
            };
            section("Favorites", 0, favCount);
            section("Recent", favCount, tiles.Count);
            contentH = y;
            scrollY = Math.Max(0, Math.Min(MaxScroll, scrollY));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(0, -scrollY);
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            // header: title + links
            TextRenderer.DrawText(g, Title, Ui.Big, new Rectangle(0, 0, ClientSize.Width, Ui.S(30)), Ui.Text, flags);
            int x = ClientSize.Width - Ui.S(4);
            Func<string, bool, Rectangle> link = (text, isHot) =>
            {
                var sz = TextRenderer.MeasureText(text, Ui.Base);
                x -= sz.Width;
                var r = new Rectangle(x, Ui.S(4), sz.Width, Ui.S(24));
                TextRenderer.DrawText(g, text, Ui.Base, r, isHot ? Ui.AccentHover : Ui.Accent, flags | TextFormatFlags.Right);
                x -= Ui.S(16);
                return r;
            };
            if (AddClicked != null) addLink = link("Add folder...", hotAdd);
            if (tiles.Count > favCount) clearLink = link("Clear recent", hotClear);

            foreach (var h in heads)
            {
                TextRenderer.DrawText(g, h.Key.ToUpperInvariant(), Ui.Small, new Rectangle(Ui.S(2), h.Value, Ui.S(200), Ui.S(20)), Ui.Muted, flags);
            }
            if (favCount == 0 && heads.Count > 0)
                TextRenderer.DrawText(g, "Click the star on a folder to keep it here.", Ui.Small, new Rectangle(Ui.S(2), heads[0].Value + Ui.S(22), ClientSize.Width, Ui.S(18)), Ui.Muted, flags);
            if (tiles.Count == favCount && heads.Count > 1)
                TextRenderer.DrawText(g, "Folders you open appear here.", Ui.Small, new Rectangle(Ui.S(2), heads[1].Value + Ui.S(22), ClientSize.Width, Ui.S(18)), Ui.Muted, flags);

            foreach (var t in tiles)
            {
                bool isHot = t == hot;
                using (var path = Ui.Round(t.R, Ui.S(10)))
                {
                    using (var b = new SolidBrush(isHot ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                    using (var p = new Pen(isHot ? Ui.Accent : Ui.Line)) g.DrawPath(p, path);
                }
                if (!t.Color.IsEmpty) using (var b = new SolidBrush(t.Color)) g.FillRectangle(b, t.R.X + Ui.S(1), t.R.Y + Ui.S(12), Ui.S(3), t.R.Height - Ui.S(24));
                TextRenderer.DrawText(g, Glyph, Ui.Icon(13f), new Rectangle(t.R.X + Ui.S(10), t.R.Y, Ui.S(28), t.R.Height), t.Missing ? Ui.Muted : (t.Color.IsEmpty ? Ui.Accent : t.Color), flags | TextFormatFlags.HorizontalCenter);
                int tx = t.R.X + Ui.S(44), twid = t.Star.X - tx - Ui.S(4);
                TextRenderer.DrawText(g, t.Name, Ui.Bold, new Rectangle(tx, t.R.Y + Ui.S(7), twid, Ui.S(20)), t.Missing ? Ui.Muted : Ui.Text, flags);
                TextRenderer.DrawText(g, t.Missing ? "Not found: " + t.Sub : t.Sub, Ui.Small, new Rectangle(tx, t.R.Y + Ui.S(27), twid, Ui.S(18)), t.Missing ? Ui.Danger : Ui.Muted, flags | TextFormatFlags.PathEllipsis);
                if (t.Fav || isHot)
                    TextRenderer.DrawText(g, t.Fav ? "" : "", Ui.Icon(11f), t.Star, t.Fav ? Color.FromArgb(245, 176, 40) : (isHot && hotStar ? Ui.Accent : Ui.Muted), flags | TextFormatFlags.HorizontalCenter);
            }
            g.ResetTransform();
            var thumb = Thumb;
            if (!thumb.IsEmpty)
                using (var path = Ui.Round(thumb, Ui.S(3))) using (var b = new SolidBrush(dragBar ? Ui.Muted : Ui.Mix(Ui.Bg, Ui.Muted, 0.55))) g.FillPath(b, path);
        }

        Point Doc(Point p) { return new Point(p.X, p.Y + scrollY); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragBar) { int track = ClientSize.Height - Thumb.Height; if (track > 0) ScrollTo(dragStart + (e.Y - dragFrom) * MaxScroll / track); return; }
            var p = Doc(e.Location);
            var t = tiles.FirstOrDefault(x => x.R.Contains(p));
            bool s = t != null && t.Star.Contains(p), a = addLink.Contains(p), c = clearLink.Contains(p);
            if (t != hot || s != hotStar || a != hotAdd || c != hotClear) { hot = t; hotStar = s; hotAdd = a; hotClear = c; Invalidate(); }
            Cursor = t != null || a || c ? Cursors.Hand : Cursors.Default;
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot != null || hotAdd || hotClear) { hot = null; hotAdd = hotClear = hotStar = false; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragBar) { dragBar = false; Capture = false; Invalidate(); return; }
            if (e.X >= ClientSize.Width - Ui.S(12) && MaxScroll > 0) return;
            var p = Doc(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                if (addLink.Contains(p)) { var h = AddClicked; if (h != null) h(); return; }
                if (clearLink.Contains(p))
                {
                    if (MessageBox.Show(FindForm(), "Clear the recent list? Favorites, labels and notes stay.", App.Name, MessageBoxButtons.OKCancel) == DialogResult.OK) MetaStore.ClearHistory(Scope);
                    return;
                }
            }
            var t = tiles.FirstOrDefault(x => x.R.Contains(p));
            if (t == null) return;
            if (e.Button == MouseButtons.Left)
            {
                if (t.Star.Contains(p)) { MetaStore.ToggleFav(Scope, t.Id); return; }
                var o = Open; if (o != null) o(t.Id);
            }
            else if (e.Button == MouseButtons.Right)
            {
                menu.Items.Clear();
                string id = t.Id;
                if (MenuItems != null) MenuItems(id, menu);
                if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Copy path", null, delegate { try { Clipboard.SetText(id); } catch { } });
                menu.Items.Add(AppLinks.SendTo(id, null, FindForm()));
                menu.Items.Add(MetaStore.Menu(Scope, id, NameOf(id), null));
                menu.Show(this, e.Location);
            }
        }
    }
    // Editable combo boxes keep their text selected (blue highlight) after layout changes even when not focused.
    static class ComboFix
    {
        public static void NoSelect(ComboBox c)
        {
            if (c.DropDownStyle != ComboBoxStyle.DropDown) return;
            EventHandler clear = delegate
            {
                if (c.IsDisposed || !c.IsHandleCreated) return;
                c.BeginInvoke((Action)delegate { try { if (!c.IsDisposed && !c.Focused && c.SelectionLength > 0) c.SelectionLength = 0; } catch { } });
            };
            c.Resize += clear; c.HandleCreated += clear; c.Leave += clear; c.VisibleChanged += clear;
            c.SelectedIndexChanged += clear;
        }
    }
}