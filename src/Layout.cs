using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Small round button that sits on the edge of a side panel (sidebar, quick launch bar) and folds / unfolds it.
    class EdgeToggle : Control
    {
        public bool PointsLeft;
        bool hot;
        readonly ToolTip tip = new ToolTip();
        string tipText;
        public string Tip { set { if (value != tipText) { tipText = value; tip.SetToolTip(this, value); } } }
        public EdgeToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(Ui.S(24), Ui.S(24)); Cursor = Cursors.Hand; TabStop = false;
            using (var path = new System.Drawing.Drawing2D.GraphicsPath()) { path.AddEllipse(0, 0, Width, Height); Region = new Region(path); }
            AccessibleRole = AccessibleRole.PushButton; AccessibleName = "Fold or unfold the side panel";
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(hot ? Ui.AccentFill : Ui.Card)) g.FillEllipse(b, r);
            using (var p = new Pen(hot ? Ui.AccentFill : Ui.Line)) g.DrawEllipse(p, r);
            TextRenderer.DrawText(g, PointsLeft ? "" : "", Ui.Icon(7.5f), new Rectangle(0, 0, Width, Height), hot ? Color.White : Ui.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    static class LayoutKit
    {
        public static FlowLayoutPanel Actions(DockStyle dock, params Control[] controls)
        {
            var bar = new FlowLayoutPanel { Dock = dock, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(Ui.S(8)), Margin = new Padding(0) };
            if (dock == DockStyle.Bottom) bar.FlowDirection = FlowDirection.RightToLeft;
            bar.Controls.AddRange(controls);
            return bar;
        }
        public static TableLayoutPanel Fields()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(Ui.S(14)) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return table;
        }
        public static void Add(TableLayoutPanel table, Control control, bool fill)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(fill ? SizeType.Percent : SizeType.AutoSize, fill ? 100 : 0));
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 0, 0, Ui.S(8));
            table.Controls.Add(control, 0, row);
        }
        public static void FlowTools(ToolStrip tools, Form owner)
        {
            tools.LayoutStyle = ToolStripLayoutStyle.Flow;
            ((FlowLayoutSettings)tools.LayoutSettings).WrapContents = true;
            tools.AutoSize = true;
            EventHandler resize = delegate { tools.MaximumSize = new Size(Math.Max(100, owner.ClientSize.Width), 0); };
            owner.Resize += resize;
            resize(owner, EventArgs.Empty);
        }
    }

    class WrapLabel : Label
    {
        Control host;
        public WrapLabel() { AutoSize = true; UseMnemonic = false; }
        protected override void OnParentChanged(EventArgs e)
        {
            if (host != null) host.SizeChanged -= HostResize;
            base.OnParentChanged(e);
            host = Parent;
            if (host != null) host.SizeChanged += HostResize;
            HostResize(this, EventArgs.Empty);
        }
        void HostResize(object sender, EventArgs e)
        {
            if (host == null || IsDisposed) return;
            int width = Math.Max(40, host.ClientSize.Width - host.Padding.Horizontal - Margin.Horizontal);
            if (MaximumSize.Width != width) MaximumSize = new Size(width, 0);
        }
        protected override void Dispose(bool disposing) { if (disposing && host != null) host.SizeChanged -= HostResize; base.Dispose(disposing); }
    }

    class ResponsivePane : Panel
    {
        readonly Control detail;
        readonly int preferredWidth, breakpoint, preferredHeight;
        readonly string key;
        bool applying, sized;
        public ResponsivePane(Control primary, Control detail, int width, int breakpoint, int height, string key = null)
        {
            this.detail = detail; preferredWidth = width; this.breakpoint = breakpoint; preferredHeight = height; this.key = key;
            Dock = DockStyle.Fill;
            primary.Dock = DockStyle.Fill;
            Controls.Add(primary); Controls.Add(detail);
            if (key != null) PaneGrip.Attach(detail, key);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (detail != null && !applying)
            {
                applying = true;
                try
                {
                    // Size is set when the orientation changes (or first time); after that the user's drag wins,
                    // only clamped so the main list always keeps room.
                    var want = ClientSize.Width < breakpoint ? DockStyle.Bottom : DockStyle.Right;
                    if (detail.Dock != want || !sized)
                    {
                        // Not "sized" until the real window width is known (the first layout often runs at a tiny transient size).
                        detail.Dock = want; sized = ClientSize.Width > Ui.S(600);
                        if (want == DockStyle.Bottom) detail.Height = PaneGrip.Size(key, want, Math.Max(Ui.S(80), Math.Min(preferredHeight, ClientSize.Height / 3)), ClientSize.Height);
                        // Wide windows: the detail pane grows with the window (about a third) instead of staying a narrow strip.
                        else detail.Width = PaneGrip.Size(key, want, Math.Min(Math.Max(preferredWidth, ClientSize.Width * 36 / 100), ClientSize.Width * 45 / 100), ClientSize.Width);
                    }
                    else if (want == DockStyle.Right && detail.Width > ClientSize.Width * 80 / 100) detail.Width = Math.Max(Ui.S(60), ClientSize.Width * 80 / 100);
                    else if (want == DockStyle.Bottom && detail.Height > ClientSize.Height * 80 / 100) detail.Height = Math.Max(Ui.S(60), ClientSize.Height * 80 / 100);
                }
                finally { applying = false; }
            }
            base.OnLayout(e);
        }
    }

    // Draggable divider beside a docked side panel. The size the user drags to is remembered per key and orientation.
    static class PaneGrip
    {
        static bool Vertical(DockStyle d) { return d == DockStyle.Top || d == DockStyle.Bottom; }
        static string Key(string key, DockStyle d) { return key + (Vertical(d) ? ".h" : ".w"); }

        public static int Size(string key, DockStyle dock, int fallback, int available)
        {
            int v;
            if (key == null || Store.Settings.PaneSizes == null || !Store.Settings.PaneSizes.TryGetValue(Key(key, dock), out v) || v <= 0) return fallback;
            return Math.Max(Ui.S(60), Math.Min(v, Math.Max(Ui.S(60), available * 80 / 100)));
        }

        public static Splitter Attach(Control panel, string key, int minSize = 0)
        {
            var parent = panel.Parent;
            if (parent == null) throw new InvalidOperationException("Add the panel to its parent before attaching a divider.");
            var sp = new GripSplitter { MinSize = minSize > 0 ? minSize : Ui.S(80), MinExtra = Ui.S(140) };
            parent.Controls.Add(sp);
            // The divider must sit just inside the panel in docking order.
            parent.Controls.SetChildIndex(sp, parent.Controls.GetChildIndex(panel));
            EventHandler sync = delegate
            {
                bool side = panel.Dock == DockStyle.Left || panel.Dock == DockStyle.Right || Vertical(panel.Dock);
                if (side && sp.Dock != panel.Dock) sp.Dock = panel.Dock;
                if (Vertical(sp.Dock)) sp.Height = Ui.S(6); else sp.Width = Ui.S(6);
                sp.Visible = side && panel.Visible;
            };
            panel.DockChanged += sync; panel.VisibleChanged += sync; sync(panel, EventArgs.Empty);
            sp.SplitterMoved += delegate
            {
                if (Store.Settings.PaneSizes == null) Store.Settings.PaneSizes = new Dictionary<string, int>();
                Store.Settings.PaneSizes[Key(key, panel.Dock)] = Vertical(panel.Dock) ? panel.Height : panel.Width;
                Store.MarkSettings();
            };
            return sp;
        }

        // Thin, quiet divider that lights up while hovered or dragged.
        class GripSplitter : Splitter
        {
            bool hot;
            public GripSplitter() { BackColor = Ui.Bg; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }
            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
                bool vertical = Dock == DockStyle.Top || Dock == DockStyle.Bottom;
                using (var b = new SolidBrush(hot ? Ui.Accent : Ui.Line))
                {
                    if (vertical) e.Graphics.FillRectangle(b, 0, Height / 2 - (hot ? 1 : 0), Width, hot ? 3 : 1);
                    else e.Graphics.FillRectangle(b, Width / 2 - (hot ? 1 : 0), 0, hot ? 3 : 1, Height);
                    // grip dots in the middle
                    for (int i = -2; i <= 2; i++)
                    {
                        if (vertical) e.Graphics.FillEllipse(b, Width / 2 + i * Ui.S(6) - 1, Height / 2 - 1, 3, 3);
                        else e.Graphics.FillEllipse(b, Width / 2 - 1, Height / 2 + i * Ui.S(6) - 1, 3, 3);
                    }
                }
            }
        }
    }
}
