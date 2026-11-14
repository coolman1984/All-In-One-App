using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StickyPro
{
    // Owns its frames. Only unmatched bottom bands are retained; output is capped.
    class ScrollStitcher : IDisposable
    {
        readonly List<Bitmap> bands = new List<Bitmap>();
        readonly long maxPixels;
        Bitmap previous;
        int height, width, expected;
        public bool Capped;
        public int Height { get { return height; } }
        public ScrollStitcher(long maxPixels) { this.maxPixels = maxPixels; }
        public int Add(Bitmap image)
        {
            if (previous == null)
            {
                if ((long)image.Width * image.Height > maxPixels) { image.Dispose(); throw new InvalidOperationException("Select a smaller scrolling area to keep memory use low."); }
                previous = image; bands.Add(image); width = image.Width; height = image.Height; expected = image.Height / 3; return image.Height;
            }
            int shift;
            try { shift = FindOffset(previous, image, expected); }
            catch { image.Dispose(); throw; }
            if (shift <= 0) { image.Dispose(); return shift; }
            if ((long)width * (height + shift) > maxPixels) { Capped = true; image.Dispose(); return 0; }
            bands.Add(image.Clone(new Rectangle(0, image.Height - shift, width, shift), PixelFormat.Format32bppArgb));
            height += shift; expected = shift;
            if (!ReferenceEquals(previous, bands[0])) previous.Dispose(); previous = image;
            return shift;
        }
        public Bitmap Build()
        {
            if (bands.Count == 0) return null;
            var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(result)) { int y = 0; foreach (var band in bands) { g.DrawImageUnscaled(band, 0, y); y += band.Height; } }
            return result;
        }
        public static int FindOffset(Bitmap a, Bitmap b, int expected)
        {
            if (a.Size != b.Size || a.Width < 32 || a.Height < 64) return -1;
            var rect = new Rectangle(Point.Empty, a.Size);
            var ad = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bd = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    double unchanged = 0; int samples = 0;
                    for (int y = 8; y < a.Height - 8; y += Math.Max(1, a.Height / 20))
                        for (int x = 8; x < a.Width - 8; x += Math.Max(1, a.Width / 12)) { unchanged += Difference(ad, bd, x, y, y); samples++; }
                    if (samples > 0 && unchanged / samples < 1) return 0;
                    double best = double.MaxValue; int found = -1;
                    int start = Math.Max(8, a.Height / 6);
                    for (int shift = 1; shift < a.Height - start - 24; shift++)
                    {
                        int overlap = a.Height - shift; double score = 0; int count = 0;
                        for (int j = 0; j < 12; j++)
                        {
                            int y = start + j * (overlap - start - 12) / 12;
                            for (int i = 1; i <= 10; i++) { int x = i * (a.Width - 16) / 12; score += Difference(ad, bd, x, y + shift, y); count++; }
                        }
                        score /= Math.Max(1, count);
                        if (score < best - 0.05 || (Math.Abs(score - best) < 0.05 && Math.Abs(shift - expected) < Math.Abs(found - expected))) { best = score; found = shift; }
                    }
                    return best <= 12 ? found : -1;
                }
                finally { b.UnlockBits(bd); }
            }
            finally { a.UnlockBits(ad); }
        }
        static int Difference(BitmapData a, BitmapData b, int x, int ay, int by)
        {
            int av = Marshal.ReadInt32(a.Scan0, ay * a.Stride + x * 4), bv = Marshal.ReadInt32(b.Scan0, by * b.Stride + x * 4);
            return (Math.Abs((av & 255) - (bv & 255)) + Math.Abs(((av >> 8) & 255) - ((bv >> 8) & 255)) + Math.Abs(((av >> 16) & 255) - ((bv >> 16) & 255))) / 3;
        }
        public void Dispose() { foreach (var image in bands.Concat(new[] { previous }).Where(i => i != null).Distinct()) image.Dispose(); bands.Clear(); previous = null; }
    }

    class ScrollingCapture : Form
    {
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        readonly Rectangle area;
        readonly IntPtr target;
        readonly Action<Bitmap> complete;
        readonly ScrollStitcher stitch = new ScrollStitcher(8L * 1024 * 1024);
        readonly Timer timer = new Timer { Interval = 500 };
        readonly Label info;
        bool done, affinity, retry;
        int stable, frames, detents;
        const int EscapeId = 24001;
        public ScrollingCapture(Rectangle area, Action<Bitmap> complete)
        {
            this.area = area; this.complete = complete;
            target = CaptureNative.WindowAt(new Point(area.Left + area.Width / 2, area.Top + area.Height / 2));
            Text = "Scrolling capture"; Font = Ui.Base; BackColor = Ui.Card; ForeColor = Ui.Text; TopMost = true; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; ClientSize = Ui.S(330, 115); FormBorderStyle = FormBorderStyle.FixedToolWindow;
            var wa = Screen.FromRectangle(area).WorkingArea; Location = new Point(wa.Right - Width, wa.Top);
            info = new WrapLabel { Text = "Capturing… Esc or Stop keeps the captured portion.", Dock = DockStyle.Fill, Padding = new Padding(Ui.S(10)) };
            Controls.Add(info); Controls.Add(LayoutKit.Actions(DockStyle.Bottom, Ui.Btn("Stop / use capture", delegate { Finish(); }, true)));
            Ui.DarkTitle(this); Ui.Theme(this);
            timer.Tick += delegate { Step(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e); affinity = Shot.SetWindowDisplayAffinity(Handle, 0x11);
            Native.RegisterHotKey(Handle, EscapeId, 0, (uint)Keys.Escape);
            if (target == IntPtr.Zero || area.Width < 32 || area.Height < 96) { Finish(); return; }
            Native.SetForegroundWindow(target); timer.Start();
        }
        void Wheel(int delta)
        {
            var point = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            long coords = (uint)((point.X & 65535) | ((point.Y & 65535) << 16));
            PostMessage(target, 0x20A, new IntPtr((long)(uint)((delta & 65535) << 16)), new IntPtr(coords));
            detents += -delta / 120;
        }
        void Step()
        {
            if (done) return;
            try
            {
                if (!Native.IsWindow(target) || frames >= 40) { Finish(); return; }
                if (!affinity) { Hide(); DwmFlush(); }
                Bitmap image;
                try { image = Shot.Capture(area); } finally { if (!affinity && !done) Show(); }
                int added = stitch.Add(image); frames++;
                info.Text = "Captured " + stitch.Height + " px high. Esc stops.";
                if (stitch.Capped) { info.Text = "Memory limit reached; using captured portion."; Finish(); return; }
                if (added < 0)
                {
                    if (!retry) { retry = true; Wheel(240); return; }
                    Finish(); return;
                }
                if (added == 0) { if (++stable >= 2) Finish(); return; }
                stable = 0; retry = false; Wheel(-360);
            }
            catch (Exception ex) { App.Log("Scrolling capture: " + ex.Message); Finish(); }
        }
        void Finish()
        {
            if (done) return; done = true; timer.Stop(); Native.UnregisterHotKey(Handle, EscapeId);
            if (detents > 0 && Native.IsWindow(target)) Wheel(detents * 120);
            Bitmap image = null;
            try { image = stitch.Build(); } catch (Exception ex) { App.Log("Scrolling result: " + ex.Message); }
            stitch.Dispose(); Close(); complete(image);
        }
        protected override void WndProc(ref Message m) { if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == EscapeId) { Finish(); return; } base.WndProc(ref m); }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (!done) { e.Cancel = true; Ui.Later(1, Finish); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); stitch.Dispose(); if (IsHandleCreated) Native.UnregisterHotKey(Handle, EscapeId); } base.Dispose(disposing); }
    }
}
