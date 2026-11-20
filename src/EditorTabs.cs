using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // VS Code style editor tabs: one tab per open file; the active tab has an accent line on top.
    //  - Click opens, middle-click or the x closes, drag a tab to reorder, mouse wheel scrolls when they do not fit.
    //  - Right-click: Close, Close others, Close to the right, Close all, Copy path, Show in Explorer.
    //  - Ctrl+W closes, Ctrl+Tab / Ctrl+Shift+Tab switch, Ctrl+Shift+T reopens the last closed (the owner page wires the keys).
    class EditorTabs : Control
    {
        readonly List<string> tabs = new List<string>();
        readonly List<string> closed = new List<string>();
        readonly List<Rectangle> rects = new List<Rectangle>();
        string active; int hover = -1, hoverX = -1, scroll, dragFrom = -1; Point downAt;
        public event Action<string> Activated;     // a tab was clicked: open this file
        public event Action<string> AllClosed;     // the last tab closed (argument: the path that closed)
        public Func<string, bool> CanClose;
        public event Action Changed;               // the list changed (save it)

        public EditorTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Ui.S(34); Font = Ui.Base;
        }

        public IList<string> Paths { get { return tabs.AsReadOnly(); } }
        public string Active { get { return active; } }
        public string EmptyText = "No open notes  ·  Ctrl+O opens one";

        public void SetAll(IEnumerable<string> paths) { tabs.Clear(); foreach (var p in paths) if (!string.IsNullOrEmpty(p) && !Has(p)) tabs.Add(p); Invalidate(); }
        bool Has(string p) { return tabs.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)); }
        int IndexOf(string p) { return tabs.FindIndex(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase)); }

        // The page opened a file: show it as the active tab (added after the current one when new).
        public void Show(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!Has(path)) { int at = active == null ? -1 : IndexOf(active); tabs.Insert(at < 0 ? tabs.Count : at + 1, path); Raise(); }
            active = path; EnsureVisible(IndexOf(path)); Invalidate();
        }

        // A file was renamed or moved.
        public void Rename(string from, string to) { int i = IndexOf(from); if (i < 0) return; tabs[i] = to; if (string.Equals(active, from, StringComparison.OrdinalIgnoreCase)) active = to; Raise(); Invalidate(); }

        public void Close(string path)
        {
            int i = IndexOf(path); if (i < 0 || (CanClose != null && !CanClose(tabs[i]))) return;
            tabs.RemoveAt(i); closed.Add(path); if (closed.Count > 30) closed.RemoveAt(0);
            Raise();
            if (string.Equals(active, path, StringComparison.OrdinalIgnoreCase))
            {
                active = null;
                if (tabs.Count > 0) { var next = tabs[Math.Min(i, tabs.Count - 1)]; if (Activated != null) Activated(next); }
                else if (AllClosed != null) AllClosed(path);
            }
            Invalidate();
        }
        public void CloseActive() { if (active != null) Close(active); }
        public void ReopenClosed() { while (closed.Count > 0) { var p = closed[closed.Count - 1]; closed.RemoveAt(closed.Count - 1); if (File.Exists(p)) { if (Activated != null) Activated(p); return; } } }
        public void Next(int dir)
        {
            if (tabs.Count < 2 || active == null) return;
            int i = IndexOf(active); i = ((i + dir) % tabs.Count + tabs.Count) % tabs.Count;
            if (Activated != null) Activated(tabs[i]);
        }
        void Raise() { if (Changed != null) Changed(); }

        int TabWidth(string p)
        {
            int w = TextRenderer.MeasureText(FileName(p), Font).Width + Ui.S(46);
            return Math.Max(Ui.S(90), Math.Min(Ui.S(220), w));
        }
        static string FileName(string p) { return Path.GetFileNameWithoutExtension(p); }

        void Measure()
        {
            rects.Clear(); int x = -scroll;
            foreach (var p in tabs) { int w = TabWidth(p); rects.Add(new Rectangle(x, 0, w, Height)); x += w; }
        }
        int Total() { return tabs.Sum(p => TabWidth(p)); }
        void EnsureVisible(int i)
        {
            if (i < 0) return; Measure(); if (i >= rects.Count) return;
            var r = rects[i];
            if (r.Left < 0) scroll += r.Left; else if (r.Right > Width) scroll += r.Right - Width;
            scroll = Math.Max(0, Math.Min(scroll, Math.Max(0, Total() - Width)));
        }
        Rectangle CloseBox(Rectangle r) { int s = Ui.S(18); return new Rectangle(r.Right - s - Ui.S(6), r.Y + (r.Height - s) / 2, s, s); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; var bg = Parent != null ? Parent.BackColor : Ui.Bg;
            g.Clear(Ui.Mix(bg, Ui.Text, 0.04));
            Measure();
            using (var line = new Pen(Ui.Line)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            for (int i = 0; i < tabs.Count; i++)
            {
                var r = rects[i]; if (r.Right < 0 || r.Left > Width) continue;
                bool on = string.Equals(tabs[i], active, StringComparison.OrdinalIgnoreCase);
                using (var b = new SolidBrush(on ? Ui.Card : i == hover ? Ui.Mix(bg, Ui.Text, 0.08) : Ui.Mix(bg, Ui.Text, 0.04))) g.FillRectangle(b, r);
                if (on) using (var a = new SolidBrush(Ui.Accent)) g.FillRectangle(a, r.X, 0, r.Width, Ui.S(2));
                using (var sep = new Pen(Ui.Line)) g.DrawLine(sep, r.Right - 1, Ui.S(6), r.Right - 1, r.Bottom - Ui.S(6));
                var tr = new Rectangle(r.X + Ui.S(12), r.Y, r.Width - Ui.S(40), r.Height);
                TextRenderer.DrawText(g, FileName(tabs[i]), on ? Ui.Bold : Font, tr, on ? Ui.Text : Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (on || i == hover)
                {
                    var cb = CloseBox(r);
                    if (i == hover && cb.Contains(hoverX, Height / 2)) using (var hb = new SolidBrush(Ui.Mix(bg, Ui.Text, 0.15))) g.FillRectangle(hb, cb);
                    TextRenderer.DrawText(g, "", Ui.Icon(7.5f), cb, Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            if (tabs.Count == 0) TextRenderer.DrawText(g, "  " + EmptyText, Ui.Small, ClientRectangle, Ui.Muted, TextFormatFlags.VerticalCenter);
        }

        int HitTab(Point p) { Measure(); for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTab(e.Location);
            if (dragFrom >= 0 && e.Button == MouseButtons.Left && Math.Abs(e.X - downAt.X) > Ui.S(8) && h >= 0 && h != dragFrom)
            { var p = tabs[dragFrom]; tabs.RemoveAt(dragFrom); tabs.Insert(h, p); dragFrom = h; Raise(); }
            if (h != hover || e.X != hoverX) { hover = h; hoverX = e.X; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); downAt = e.Location; dragFrom = e.Button == MouseButtons.Left ? HitTab(e.Location) : -1; }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            bool dragged = Math.Abs(e.X - downAt.X) > Ui.S(8); dragFrom = -1;
            int i = HitTab(e.Location); if (i < 0) return;
            string p = tabs[i];
            if (e.Button == MouseButtons.Middle) { Close(p); return; }
            if (e.Button == MouseButtons.Right) { Menu(p, e.Location); return; }
            if (e.Button != MouseButtons.Left || dragged) return;
            if (CloseBox(rects[i]).Contains(e.Location)) { Close(p); return; }
            if (!string.Equals(p, active, StringComparison.OrdinalIgnoreCase) && Activated != null) Activated(p);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(scroll - e.Delta / 2, Math.Max(0, Total() - Width))); Invalidate();
        }

        void Menu(string p, Point at)
        {
            var m = Ui.Menu();
            m.Items.Add("Close", null, delegate { Close(p); });
            m.Items.Add("Close others", null, delegate { foreach (var x in tabs.Where(x => x != p).ToList()) Close(x); });
            m.Items.Add("Close to the right", null, delegate { int i = IndexOf(p); foreach (var x in tabs.Skip(i + 1).ToList()) Close(x); });
            m.Items.Add("Close all", null, delegate { foreach (var x in tabs.ToList()) Close(x); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Copy path", null, delegate { App.I.CopyText(p); });
            m.Items.Add("Show in Explorer", null, delegate { App.I.OpenInExplorerPage(Path.GetDirectoryName(p)); });
            Ui.ThemeMenu(m); m.Show(this, at);
        }
    }
}
