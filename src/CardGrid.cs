using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Light owner-drawn virtual grid of cards: flows into as many columns as fit, paints only visible rows.
    public class CardGrid : Control
    {
        public Func<object, CardInfo> Info;
        public event EventHandler SelectedIndexChanged;

        readonly List<object> items = new List<object>();
        readonly HashSet<object> selected = new HashSet<object>();
        readonly VScrollBar scroll;
        int itemHeight, minColumnWidth, maxColumns = 6;
        int cols = 1, cellW = 100, contentH;
        int focus = -1, anchor = -1;
        int updating;
        bool pendingChange;
        SelectionMode selectionMode = SelectionMode.One;

        public CardGrid()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Ui.Bg;
            Font = Ui.Base;
            itemHeight = Ui.S(78);
            minColumnWidth = Ui.S(300);
            scroll = new VScrollBar { Dock = DockStyle.Right, Width = SystemInformation.VerticalScrollBarWidth, Visible = false };
            if (Ui.Dark) Ui.DarkScroll(scroll);
            scroll.ValueChanged += delegate { Invalidate(); };
            Controls.Add(scroll);
        }

        // ---------- Public API ----------

        public int ItemHeight { get { return itemHeight; } set { value = Math.Max(1, value); if (value == itemHeight) return; itemHeight = value; LayoutGrid(); Invalidate(); } }
        public int MinColumnWidth { get { return minColumnWidth; } set { value = Math.Max(1, value); if (value == minColumnWidth) return; minColumnWidth = value; LayoutGrid(); Invalidate(); } }
        public int MaxColumns { get { return maxColumns; } set { value = Math.Max(1, value); if (value == maxColumns) return; maxColumns = value; LayoutGrid(); Invalidate(); } }
        public SelectionMode SelectionMode { get { return selectionMode; } set { selectionMode = value; } }
        public List<object> Items { get { return items; } }

        bool Multi { get { return selectionMode == SelectionMode.MultiExtended || selectionMode == SelectionMode.MultiSimple; } }

        public void BeginUpdate() { updating++; }
        public void EndUpdate()
        {
            if (updating > 0) updating--;
            if (updating == 0) { LayoutGrid(); Invalidate(); FlushChange(); }
        }

        public void SetItems(IEnumerable<object> list)
        {
            var arr = list.ToArray();
            object focusItem = focus >= 0 && focus < items.Count ? items[focus] : null;
            object topItem = null; int topRow = 0;
            int off = Offset;
            if (items.Count > 0) { topRow = off / itemHeight; int ti = Math.Min(items.Count - 1, topRow * cols); topItem = items[ti]; }
            int inRow = off - topRow * itemHeight;
            int oldSelected = selected.Count;

            var present = new HashSet<object>(arr);
            selected.RemoveWhere(o => !present.Contains(o));
            items.Clear();
            items.AddRange(arr);
            focus = focusItem != null ? items.IndexOf(focusItem) : -1;
            if (focus < 0 && selected.Count > 0) focus = items.FindIndex(o => selected.Contains(o));
            anchor = focus;

            LayoutGrid();
            if (topItem != null)
            {
                int ni = items.IndexOf(topItem);
                if (ni >= 0) SetOffset((ni / cols) * itemHeight + inRow);
            }
            Invalidate();
            if (selected.Count != oldSelected) RaiseChanged();
        }

        public int SelectedIndex
        {
            get
            {
                if (selected.Count == 0) return -1;
                if (focus >= 0 && focus < items.Count && selected.Contains(items[focus])) return focus;
                for (int i = 0; i < items.Count; i++) if (selected.Contains(items[i])) return i;
                return -1;
            }
            set
            {
                if (value < 0 || value >= items.Count) { ClearSelected(); return; }
                selected.Clear(); selected.Add(items[value]);
                focus = anchor = value;
                ScrollIntoView(value);
                Invalidate();
                RaiseChanged();
            }
        }

        public object SelectedItem
        {
            get { int i = SelectedIndex; return i >= 0 ? items[i] : null; }
            set
            {
                if (value == null) { ClearSelected(); return; }
                int i = items.IndexOf(value);
                if (i >= 0) SelectedIndex = i;
            }
        }

        public List<object> SelectedItems { get { return items.Where(o => selected.Contains(o)).ToList(); } }
        public List<T> Selected<T>() { return SelectedItems.OfType<T>().ToList(); }
        public bool GetSelected(int index) { return index >= 0 && index < items.Count && selected.Contains(items[index]); }

        public void SetSelected(int index, bool value)
        {
            if (index < 0 || index >= items.Count) return;
            if (!Multi) { if (value) SelectedIndex = index; else if (GetSelected(index)) ClearSelected(); return; }
            bool changed = value ? selected.Add(items[index]) : selected.Remove(items[index]);
            if (value) { focus = index; if (anchor < 0) anchor = index; }
            if (changed) { Invalidate(); RaiseChanged(); }
        }

        public void ClearSelected()
        {
            if (selected.Count == 0) return;
            selected.Clear();
            Invalidate();
            RaiseChanged();
        }

        public int IndexFromPoint(Point p)
        {
            int usable = scroll.Visible ? ClientSize.Width - scroll.Width : ClientSize.Width;
            if (p.X < 0 || p.Y < 0 || p.X >= usable || cellW <= 0) return -1;
            int col = p.X / cellW;
            if (col >= cols) return -1;
            int i = ((p.Y + Offset) / itemHeight) * cols + col;
            return i >= 0 && i < items.Count ? i : -1;
        }

        // ---------- Layout and scrolling ----------

        int Offset { get { return scroll.Visible ? Math.Max(0, Math.Min(scroll.Value, contentH - ClientSize.Height)) : 0; } }

        int Columns(int width) { return Math.Max(1, Math.Min(maxColumns, width / Math.Max(1, minColumnWidth))); }

        void LayoutGrid()
        {
            if (scroll == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            int c = Columns(w);
            int rows = (items.Count + c - 1) / c;
            contentH = rows * itemHeight;
            bool need = contentH > h;
            if (need)
            {
                c = Columns(w - scroll.Width);
                rows = (items.Count + c - 1) / c;
                contentH = rows * itemHeight;
            }
            cols = c;
            int usable = need ? w - scroll.Width : w;
            cellW = Math.Max(1, usable / cols);
            if (scroll.Visible != need) scroll.Visible = need;
            if (need)
            {
                scroll.Minimum = 0;
                scroll.Maximum = contentH;
                scroll.LargeChange = Math.Max(1, h);
                scroll.SmallChange = itemHeight;
                int max = Math.Max(0, contentH - h);
                if (scroll.Value > max) scroll.Value = max;
            }
            else if (scroll.Value != 0) scroll.Value = 0;
        }

        void SetOffset(int value)
        {
            if (!scroll.Visible) return;
            int max = Math.Max(0, contentH - ClientSize.Height);
            value = Math.Max(0, Math.Min(max, value));
            if (value != scroll.Value) scroll.Value = value;
        }

        void ScrollIntoView(int index)
        {
            if (index < 0 || !scroll.Visible) return;
            int y = (index / cols) * itemHeight, off = Offset, h = ClientSize.Height;
            if (y < off) SetOffset(y);
            else if (y + itemHeight > off + h) SetOffset(y + itemHeight - h);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutGrid(); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ---------- Painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (Info == null || items.Count == 0) return;
            int off = Offset, h = ClientSize.Height;
            int first = Math.Max(0, off / itemHeight), last = (off + h) / itemHeight;
            var clip = e.ClipRectangle;
            for (int row = first; row <= last; row++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i = row * cols + c;
                    if (i >= items.Count) return;
                    var cell = new Rectangle(c * cellW, row * itemHeight - off, cellW, itemHeight);
                    if (!cell.IntersectsWith(clip)) continue;
                    CardInfo ci;
                    try { ci = Info(items[i]); } catch { continue; }
                    Ui.DrawCard(g, cell, ci, selected.Contains(items[i]), BackColor);
                    if (Focused && i == focus)
                    {
                        var fr = Rectangle.Inflate(cell, -Ui.S(3), -Ui.S(3));
                        using (var p = new Pen(Ui.Accent) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, fr.X, fr.Y, fr.Width - 1, fr.Height - 1);
                    }
                }
            }
        }

        // ---------- Mouse ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int i = IndexFromPoint(e.Location);
            bool changed = false;
            if (e.Button == MouseButtons.Right)
            {
                if (i >= 0 && !GetSelected(i)) changed = SelectOnly(i);
            }
            else if (e.Button == MouseButtons.Left)
            {
                if (i < 0)
                {
                    if (Multi && selected.Count > 0) { selected.Clear(); changed = true; }
                }
                else if (Multi && (ModifierKeys & Keys.Control) != 0)
                {
                    if (!selected.Remove(items[i])) selected.Add(items[i]);
                    focus = anchor = i; changed = true;
                }
                else if (Multi && (ModifierKeys & Keys.Shift) != 0)
                {
                    SelectRange(anchor < 0 ? i : anchor, i); focus = i; changed = true;
                }
                else changed = SelectOnly(i);
            }
            Invalidate();
            if (changed) RaiseChanged();
            base.OnMouseDown(e);
        }

        bool SelectOnly(int i)
        {
            bool same = selected.Count == 1 && selected.Contains(items[i]);
            selected.Clear(); selected.Add(items[i]);
            focus = anchor = i;
            return !same;
        }

        void SelectRange(int a, int b)
        {
            selected.Clear();
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            for (int k = lo; k <= hi && k < items.Count; k++) selected.Add(items[k]);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (scroll.Visible) SetOffset(Offset - e.Delta * 3 * itemHeight / 120);
            base.OnMouseWheel(e);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            if (IndexFromPoint(PointToClient(Cursor.Position)) >= 0) base.OnDoubleClick(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (IndexFromPoint(e.Location) >= 0) base.OnMouseDoubleClick(e);
        }

        // ---------- Keyboard ----------

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (items.Count > 0)
            {
                if (e.Control && e.KeyCode == Keys.A && Multi)
                {
                    SelectRange(0, items.Count - 1);
                    Invalidate(); RaiseChanged();
                    e.Handled = e.SuppressKeyPress = true;
                }
                else
                {
                    int target = focus, pageRows = Math.Max(1, ClientSize.Height / itemHeight) * cols;
                    bool nav = true;
                    switch (e.KeyCode)
                    {
                        case Keys.Left: target = focus < 0 ? 0 : focus - 1; break;
                        case Keys.Right: target = focus < 0 ? 0 : focus + 1; break;
                        case Keys.Up: target = focus < 0 ? 0 : focus - cols; break;
                        case Keys.Down: target = focus < 0 ? 0 : focus + cols; break;
                        case Keys.PageUp: target = focus < 0 ? 0 : focus - pageRows; break;
                        case Keys.PageDown: target = focus < 0 ? 0 : focus + pageRows; break;
                        case Keys.Home: target = 0; break;
                        case Keys.End: target = items.Count - 1; break;
                        default: nav = false; break;
                    }
                    if (nav)
                    {
                        target = Math.Max(0, Math.Min(items.Count - 1, target));
                        if (Multi && e.Shift) { SelectRange(anchor < 0 ? target : anchor, target); focus = target; }
                        else { selected.Clear(); selected.Add(items[target]); focus = anchor = target; }
                        ScrollIntoView(target);
                        Invalidate(); RaiseChanged();
                        e.Handled = e.SuppressKeyPress = true;
                    }
                }
            }
            base.OnKeyDown(e);
        }

        // ---------- Events ----------

        void RaiseChanged()
        {
            if (updating > 0) { pendingChange = true; return; }
            var h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty);
        }

        void FlushChange()
        {
            if (!pendingChange) return;
            pendingChange = false;
            var h = SelectedIndexChanged; if (h != null) h(this, EventArgs.Empty);
        }
    }
}
