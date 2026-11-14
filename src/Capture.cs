using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StickyPro
{
    static class CaptureCommands
    {
        public static readonly string[][] Modes = {
            new[] { "full", "Full screen" }, new[] { "window", "Window" }, new[] { "rect", "Region" },
            new[] { "fixed", "Fixed Region" }, new[] { "control", "Window Control" }, new[] { "free", "Freehand" }
        };
        public static bool FixedSize(bool force)
        {
            var s = Store.Settings;
            if (s.FixedReady && !force && s.FixedW <= Shot.Virtual.Width && s.FixedH <= Shot.Virtual.Height) return true;
            using (var f = Ui.Dialog("Fixed Region size", 360, 230))
            {
                var fields = LayoutKit.Fields();
                int maxW = Math.Max(16, Math.Min(10000, Shot.Virtual.Width)), maxH = Math.Max(16, Math.Min(10000, Shot.Virtual.Height));
                var w = new NumericUpDown { Minimum = 16, Maximum = maxW, Value = Math.Max(16, Math.Min(maxW, s.FixedW)) };
                var h = new NumericUpDown { Minimum = 16, Maximum = maxH, Value = Math.Max(16, Math.Min(maxH, s.FixedH)) };
                LayoutKit.Add(fields, new WrapLabel { Text = "Width in pixels" }, false); LayoutKit.Add(fields, w, false);
                LayoutKit.Add(fields, new WrapLabel { Text = "Height in pixels" }, false); LayoutKit.Add(fields, h, false);
                var ok = Ui.Btn("Use size", null, true); ok.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                f.Controls.Add(fields); f.Controls.Add(LayoutKit.Actions(DockStyle.Bottom, ok, cancel)); f.AcceptButton = ok; f.CancelButton = cancel; Ui.Theme(f);
                if (f.ShowDialog() != DialogResult.OK) return false;
                s.FixedW = (int)w.Value; s.FixedH = (int)h.Value; s.FixedReady = true; Store.MarkSettings(); return true;
            }
        }
        public static Rectangle FixedAt(Point cursor, Size size, Rectangle desktop)
        {
            int w = Math.Min(Math.Max(1, size.Width), desktop.Width), h = Math.Min(Math.Max(1, size.Height), desktop.Height);
            return new Rectangle(Math.Max(desktop.Left, Math.Min(desktop.Right - w, cursor.X - w / 2)), Math.Max(desktop.Top, Math.Min(desktop.Bottom - h, cursor.Y - h / 2)), w, h);
        }
        public static void Remember(string mode, Rectangle area, IEnumerable<Point> polygon)
        {
            var s = Store.Settings; s.LastCaptureMode = mode; s.LastX = area.X; s.LastY = area.Y; s.LastW = area.Width; s.LastH = area.Height;
            s.LastPolygon = polygon == null ? new List<int>() : polygon.Take(10000).SelectMany(p => new[] { p.X, p.Y }).ToList(); Store.MarkSettings();
        }
        public static Bitmap Repeat()
        {
            var s = Store.Settings; var area = new Rectangle(s.LastX, s.LastY, s.LastW, s.LastH);
            if (area.Width < 2 || area.Height < 2) throw new InvalidOperationException("Take a snip before using Repeat Last Capture.");
            if (!Shot.Virtual.Contains(area)) throw new InvalidOperationException("The previous capture area is no longer on screen.");
            var image = Shot.Capture(area);
            if (s.LastCaptureMode != "free" || s.LastPolygon == null || s.LastPolygon.Count < 6) return image;
            try
            {
                var points = new List<Point>();
                for (int i = 0; i + 1 < s.LastPolygon.Count; i += 2) points.Add(new Point(s.LastPolygon[i] - area.X, s.LastPolygon[i + 1] - area.Y));
                var masked = new Bitmap(image.Width, image.Height);
                using (var g = Graphics.FromImage(masked)) using (var path = new GraphicsPath()) { path.AddPolygon(points.ToArray()); g.Clear(Color.White); g.SetClip(path); g.DrawImageUnscaled(image, 0, 0); }
                return masked;
            }
            finally { image.Dispose(); }
        }
    }

    static class CaptureNative
    {
        [StructLayout(LayoutKind.Sequential)] public struct PointNative { public int X, Y; public PointNative(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct RectNative { public int L, T, R, B; }
        delegate bool EnumProc(IntPtr hwnd, IntPtr value);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr value);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref PointNative point);
        [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr hwnd, PointNative point, uint flags);
        public static IntPtr WindowAt(Point point)
        {
            IntPtr found = IntPtr.Zero;
            uint me; Native.GetWindowThreadProcessId(App.I == null ? IntPtr.Zero : App.I.Sync.Handle, out me);
            EnumWindows((hwnd, ignored) =>
            {
                uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == me || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                RectNative r; if (GetWindowRect(hwnd, out r) && Rectangle.FromLTRB(r.L, r.T, r.R, r.B).Contains(point)) { found = hwnd; return false; }
                return true;
            }, IntPtr.Zero);
            for (int i = 0; found != IntPtr.Zero && i < 32; i++)
            {
                var p = new PointNative(point.X, point.Y); ScreenToClient(found, ref p);
                var child = ChildWindowFromPointEx(found, p, 1 | 2 | 4); if (child == IntPtr.Zero || child == found) break; found = child;
            }
            return found;
        }
        public static Rectangle BoundsAt(Point point)
        {
            RectNative r; var hwnd = WindowAt(point);
            return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out r) ? Rectangle.FromLTRB(r.L, r.T, r.R, r.B) : Screen.FromPoint(point).Bounds;
        }
    }
}
