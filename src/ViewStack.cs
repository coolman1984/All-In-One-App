using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // VS Code style side bar: stacked, collapsible sections ("views").
    //  - Header: chevron, title, count; click folds, double-click gives the section all the room.
    //  - Open sections share the height; drag the line between two open sections to resize them.
    //  - Right-click a header: show / hide sections, move up / down, reset.
    //  - Folded state, sizes, order and hidden sections are remembered (Settings.ViewStacks[key]).
    class ViewStack : Panel
    {
        class View { public string Id, Title; public Control Body; public bool Open = true, Hidden; public double Weight = 1; public int Count = -1; public Rectangle Header, Sash; public Label Badge; }
        readonly string key;
        readonly List<View> views = new List<View>();
        int dragSash = -1, dragStartY; double dragA, dragB;
        int HeaderH { get { return Ui.S(26); } }

        public ViewStack(string key)
        {
            this.key = key;
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Add(string id, string title, Control body, double weight = 1)
        {
            var v = new View { Id = id, Title = title, Body = body, Weight = weight };
            body.Dock = DockStyle.None;
            Controls.Add(body);
            views.Add(v);
        }

        // Call after all views are added: applies the remembered state.
        public void Restore()
        {
            string s; if (Store.Settings.ViewStacks.TryGetValue(key, out s) && !string.IsNullOrEmpty(s))
            {
                var order = new List<View>();
                foreach (var part in s.Split(';'))
                {
                    var f = part.Split(',');   // id,open,hidden,weight
                    var v = views.FirstOrDefault(x => x.Id == f[0]);
                    if (v == null || f.Length < 4) continue;
                    v.Open = f[1] == "1"; v.Hidden = f[2] == "1";
                    double w; if (double.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out w) && w > 0.05) v.Weight = w;
                    order.Add(v);
                }
                foreach (var v in views) if (!order.Contains(v)) order.Add(v);
                views.Clear(); views.AddRange(order);
            }
            LayoutViews();
        }

        void Save()
        {
            Store.Settings.ViewStacks[key] = string.Join(";", views.Select(v => v.Id + "," + (v.Open ? 1 : 0) + "," + (v.Hidden ? 1 : 0) + "," + v.Weight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
            Store.MarkSettings();
        }

        public void SetCount(string id, int count) { var v = views.FirstOrDefault(x => x.Id == id); if (v != null && v.Count != count) { v.Count = count; Invalidate(); } }
        public void Reveal(string id) { var v = views.FirstOrDefault(x => x.Id == id); if (v != null && (!v.Open || v.Hidden)) { v.Open = true; v.Hidden = false; LayoutViews(); Save(); } }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutViews(); }

        void LayoutViews()
        {
            if (views.Count == 0) return;
            var shown = views.Where(v => !v.Hidden).ToList();
            int open = shown.Count(v => v.Open);
            int free = Math.Max(0, ClientSize.Height - shown.Count * HeaderH);
            double total = shown.Where(v => v.Open).Sum(v => v.Weight);
            int y = 0, used = 0, n = 0;
            SuspendLayout();
            foreach (var v in views)
            {
                if (v.Hidden) { v.Body.Visible = false; v.Header = Rectangle.Empty; v.Sash = Rectangle.Empty; continue; }
                v.Header = new Rectangle(0, y, ClientSize.Width, HeaderH);
                y += HeaderH;
                if (v.Open)
                {
                    n++;
                    int h = n == open ? free - used : (int)(free * v.Weight / Math.Max(0.001, total));
                    h = Math.Max(0, h); used += h;
                    v.Body.SetBounds(Ui.S(4), y, Math.Max(10, ClientSize.Width - Ui.S(6)), Math.Max(0, h - Ui.S(2)));
                    v.Body.Visible = h > Ui.S(8);
                    y += h;
                    v.Sash = n < open ? new Rectangle(0, y - Ui.S(3), ClientSize.Width, Ui.S(6)) : Rectangle.Empty;
                }
                else { v.Body.Visible = false; v.Sash = Rectangle.Empty; }
            }
            ResumeLayout();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(BackColor);
            foreach (var v in views)
            {
                if (v.Hidden) continue;
                var r = v.Header;
                using (var p = new Pen(Ui.Line)) g.DrawLine(p, 0, r.Y, r.Width, r.Y);
                TextRenderer.DrawText(g, v.Open ? "" : "", Ui.Icon(8f), new Rectangle(r.X + Ui.S(2), r.Y, Ui.S(18), r.Height), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, v.Title.ToUpperInvariant(), Ui.Small, new Rectangle(r.X + Ui.S(20), r.Y, r.Width - Ui.S(60), r.Height), Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (v.Count >= 0)
                {
                    string c = v.Count.ToString();
                    var sz = TextRenderer.MeasureText(c, Ui.Small);
                    var br = new Rectangle(r.Right - sz.Width - Ui.S(16), r.Y + (r.Height - Ui.S(18)) / 2, sz.Width + Ui.S(10), Ui.S(18));
                    using (var path = Ui.Round(br, Ui.S(9))) using (var b = new SolidBrush(Ui.Mix(BackColor, Ui.Text, 0.12))) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.FillPath(b, path); }
                    TextRenderer.DrawText(g, c, Ui.Small, br, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        View HeaderAt(Point p) { return views.FirstOrDefault(v => !v.Hidden && v.Header.Contains(p)); }
        int SashAt(Point p) { for (int i = 0; i < views.Count; i++) if (!views[i].Hidden && views[i].Sash.Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragSash >= 0)
            {
                var a = views[dragSash]; var b = NextOpen(dragSash); if (b == null) return;
                var shown = views.Where(v => !v.Hidden).ToList();
                int free = Math.Max(1, ClientSize.Height - shown.Count * HeaderH);
                double total = shown.Where(v => v.Open).Sum(v => v.Weight);
                double delta = (e.Y - dragStartY) * total / free;
                a.Weight = Math.Max(0.1, dragA + delta); b.Weight = Math.Max(0.1, dragB - delta);
                LayoutViews();
                return;
            }
            Cursor = SashAt(e.Location) >= 0 ? Cursors.HSplit : HeaderAt(e.Location) != null ? Cursors.Hand : Cursors.Default;
        }

        View NextOpen(int i) { for (int k = i + 1; k < views.Count; k++) if (!views[k].Hidden && views[k].Open) return views[k]; return null; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int s = SashAt(e.Location);
            if (s >= 0 && e.Button == MouseButtons.Left) { var b = NextOpen(s); if (b == null) return; dragSash = s; dragStartY = e.Y; dragA = views[s].Weight; dragB = b.Weight; Capture = true; }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragSash >= 0) { dragSash = -1; Capture = false; Save(); return; }
            var v = HeaderAt(e.Location); if (v == null) return;
            if (e.Button == MouseButtons.Left) { v.Open = !v.Open; LayoutViews(); Save(); }
            else if (e.Button == MouseButtons.Right) Menu(v, e.Location);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var v = HeaderAt(e.Location); if (v == null) return;
            foreach (var x in views) x.Open = x == v;   // this one gets all the room
            LayoutViews(); Save();
        }

        void Menu(View v, Point at)
        {
            var m = Ui.Menu();
            foreach (var x in views) { var xx = x; m.Items.Add(new ToolStripMenuItem(x.Title, null, delegate { xx.Hidden = !xx.Hidden; if (!xx.Hidden) xx.Open = true; LayoutViews(); Save(); }) { Checked = !x.Hidden }); }
            m.Items.Add(new ToolStripSeparator());
            int i = views.IndexOf(v);
            if (i > 0) m.Items.Add("Move up", null, delegate { views.RemoveAt(i); views.Insert(i - 1, v); LayoutViews(); Save(); });
            if (i < views.Count - 1) m.Items.Add("Move down", null, delegate { views.RemoveAt(i); views.Insert(i + 1, v); LayoutViews(); Save(); });
            m.Items.Add("Open all, equal sizes", null, delegate { foreach (var x in views) { x.Open = true; x.Weight = 1; } LayoutViews(); Save(); });
            Ui.ThemeMenu(m); m.Show(this, at);
        }
    }

    // A tree without the horizontal scroll bar (long names are cut at the edge, like VS Code's outline).
    class NoHScrollTree : TreeView
    {
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.Style |= 0x8000; /* TVS_NOHSCROLL */ return cp; } }
    }
}
