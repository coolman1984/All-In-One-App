using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
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
        public ResponsivePane(Control primary, Control detail, int width, int breakpoint, int height)
        {
            this.detail = detail; preferredWidth = width; this.breakpoint = breakpoint; preferredHeight = height;
            Dock = DockStyle.Fill;
            primary.Dock = DockStyle.Fill;
            Controls.Add(primary); Controls.Add(detail);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (detail != null)
            {
                if (ClientSize.Width < breakpoint) { detail.Dock = DockStyle.Bottom; detail.Height = Math.Max(Ui.S(80), Math.Min(preferredHeight, ClientSize.Height / 3)); }
                else { detail.Dock = DockStyle.Right; detail.Width = Math.Min(preferredWidth, ClientSize.Width * 45 / 100); }
            }
            base.OnLayout(e);
        }
    }
}
