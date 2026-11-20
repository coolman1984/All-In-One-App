using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace StickyPro
{
    // ---------------------------------------------------------------------------------------------------
    // Desktop overlay: a dock with magnification, a menu bar, a hot corner and a window overview with live
    // thumbnails, drawn by this app above the Windows desktop. It does not change Windows itself; turning it
    // off removes everything (the menu bar gives its space back).
    // ---------------------------------------------------------------------------------------------------
    static class Overlay
    {
        public static bool On { get; private set; }
        public static event Action Changed;
        static OverlayDock dock;
        static OverlayMenuBar bar;
        static Timer corner;
        static OverviewForm overview;
        static Timer guard;
        static IntPtr hook;
        static readonly N.WinEventProc hookProc = OnForeground;   // kept alive: the hook calls it from native code
        static long cornerSince;
        static bool cornerArmed = true;

        public static void Set(bool on)
        {
            On = on;
            if (Store.Settings.OverlayOn != on) { Store.Settings.OverlayOn = on; Store.MarkSettings(); }
            ApplySettings();
            var h = Changed; if (h != null) h();
        }
        public static void Toggle()
        {
            Set(!On);
            if (On) App.I.Balloon("Overlay on", "To exit: the red Exit overlay button (top right), the red button at the end of the dock, or " + Hotkeys.Get("overlay") + ".");
        }

        // App exit: remove the windows (the menu bar gives its screen space back) without changing the saved setting.
        public static void Shutdown()
        {
            On = false;
            ApplySettings();
            if (overview != null && !overview.IsDisposed) overview.Close();
        }

        // Creates or removes the parts that are switched on in Settings.
        public static void ApplySettings()
        {
            var s = Store.Settings;
            bool wantDock = On && s.OverlayDock, wantBar = On && s.OverlayMenuBar, wantCorner = On && s.OverlayHotCorner && s.OverlayCorner != "off";
            // Full-screen mode: the Windows taskbar hides while the overlay's own bars are on.
            bool hideTaskbar = On && s.OverlayHideTaskbar && (wantDock || wantBar);
            if (hideTaskbar) Taskbar.Hide(); else Taskbar.Restore();
            if (hideTaskbar && guard == null) { guard = new Timer { Interval = 500 }; guard.Tick += delegate { Taskbar.Enforce(); }; guard.Start(); }
            else if (!hideTaskbar && guard != null) { guard.Stop(); guard.Dispose(); guard = null; }
            // The menu bar and the dock react the moment another window becomes active.
            if (On && hook == IntPtr.Zero) hook = N.SetWinEventHook(3, 3, IntPtr.Zero, hookProc, 0, 0, 0);   // EVENT_SYSTEM_FOREGROUND, out of context
            else if (!On && hook != IntPtr.Zero) { N.UnhookWinEvent(hook); hook = IntPtr.Zero; }
            if (wantBar && (bar == null || bar.IsDisposed)) { bar = new OverlayMenuBar(); bar.Show(); }
            else if (!wantBar && bar != null) { bar.Close(); bar.Dispose(); bar = null; }
            else if (bar != null) bar.Invalidate();
            if (wantDock && (dock == null || dock.IsDisposed)) { dock = new OverlayDock(); dock.Show(); }
            else if (!wantDock && dock != null) { dock.Close(); dock.Dispose(); dock = null; }
            else if (dock != null) dock.Reload();
            if (wantCorner && corner == null) { corner = new Timer { Interval = 90 }; corner.Tick += delegate { CheckCorner(); }; corner.Start(); }
            else if (!wantCorner && corner != null) { corner.Stop(); corner.Dispose(); corner = null; }
        }

        static void OnForeground(IntPtr h, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            try
            {
                if (bar != null && !bar.IsDisposed) bar.Sample();
            }
            catch (Exception ex) { App.Log("Overlay foreground: " + ex.Message); }
        }

        public static void ShowOverview()
        {
            if (overview != null && !overview.IsDisposed) { overview.Close(); return; }
            overview = new OverviewForm();
            overview.FormClosed += delegate { overview = null; };
            overview.Show();
            Wins.ForceForeground(overview.Handle);
        }

        static void CheckCorner()
        {
            var p = Cursor.Position;
            var b = Screen.FromPoint(p).Bounds;
            string c = Store.Settings.OverlayCorner;
            int x = c.EndsWith("right") ? b.Right - 1 : b.Left, y = c.StartsWith("bottom") ? b.Bottom - 1 : b.Top;
            bool inCorner = Math.Abs(p.X - x) <= 2 && Math.Abs(p.Y - y) <= 2;
            bool near = Math.Abs(p.X - x) <= 24 && Math.Abs(p.Y - y) <= 24;
            if (!near) { cornerArmed = true; cornerSince = 0; return; }
            if (!inCorner || !cornerArmed || Control.MouseButtons != MouseButtons.None) { cornerSince = 0; return; }
            if (cornerSince == 0) { cornerSince = Environment.TickCount; return; }
            if (Environment.TickCount - cornerSince < 180 || Wins.ForegroundIsFullscreen()) return;
            cornerArmed = false; cornerSince = 0;
            ShowOverview();
        }

        public static bool Animate { get { return Store.Settings.Animations; } }
        public static bool IsOverlayWindow(IntPtr h)
        {
            return (dock != null && !dock.IsDisposed && dock.IsHandleCreated && dock.Handle == h) || (bar != null && !bar.IsDisposed && bar.IsHandleCreated && bar.Handle == h)
                || (overview != null && !overview.IsDisposed && overview.IsHandleCreated && overview.Handle == h);
        }
    }

    // ---------- Top-level windows, app identity, icons ----------
    class WinInfo { public IntPtr Hwnd; public string Title = "", Path = "", Class = ""; public int Pid; public bool Minimized; }

    static class Wins
    {
        static readonly Dictionary<int, string> pathByPid = new Dictionary<int, string>();
        static readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> lnkTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly int myPid = Process.GetCurrentProcess().Id;

        // Windows that a taskbar would show, in Z order (top first).
        public static List<WinInfo> List()
        {
            var list = new List<WinInfo>();
            N.EnumWindows((h, l) =>
            {
                if (!N.IsWindowVisible(h) || N.GetWindow(h, 4) != IntPtr.Zero) return true;
                int ex = N.GetWindowLong(h, -20);
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true;   // tool window without APPWINDOW
                int len = N.GetWindowTextLength(h);
                if (len == 0) return true;
                int cloaked; if (N.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                var cls = new StringBuilder(256); N.GetClassName(h, cls, 256);
                string c = cls.ToString();
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return true;
                if (Overlay.IsOverlayWindow(h)) return true;
                var sb = new StringBuilder(len + 1); N.GetWindowText(h, sb, sb.Capacity);
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                var w = new WinInfo { Hwnd = h, Title = sb.ToString(), Class = c, Pid = (int)pid, Minimized = N.IsIconic(h) };
                w.Path = PathOf((int)pid);
                // Store apps live in ApplicationFrameHost: use the app process inside the frame.
                if (w.Path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    N.EnumChildWindows(h, (ch, l2) => { uint cp; N.GetWindowThreadProcessId(ch, out cp); if (cp != pid) { w.Path = PathOf((int)cp); return false; } return true; }, IntPtr.Zero);
                }
                list.Add(w);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static string PathOf(int pid)
        {
            string p;
            if (pathByPid.TryGetValue(pid, out p)) return p;
            p = "";
            IntPtr h = N.OpenProcess(0x1000, false, pid);
            if (h != IntPtr.Zero)
            {
                try { var sb = new StringBuilder(1024); int size = sb.Capacity; if (N.QueryFullProcessImageName(h, 0, sb, ref size)) p = sb.ToString(); }
                finally { N.CloseHandle(h); }
            }
            if (pathByPid.Count > 500) pathByPid.Clear();
            pathByPid[pid] = p;
            return p;
        }

        // A shortcut's target (for matching pinned shortcuts with running apps).
        public static string Target(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return path ?? "";
            string t;
            if (lnkTargets.TryGetValue(path, out t)) return t;
            t = path;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(type);
                object link = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { path });
                string target = (string)link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null);
                if (!string.IsNullOrEmpty(target)) t = target;
                Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell);
            }
            catch { }
            lnkTargets[path] = t;
            return t;
        }

        // Same program: same path, or an app execution alias (WindowsApps\wt.exe) and the packaged exe it starts.
        public static bool SameApp(string pinned, string running)
        {
            if (string.IsNullOrEmpty(pinned) || string.IsNullOrEmpty(running)) return false;
            if (pinned.Equals(running, StringComparison.OrdinalIgnoreCase)) return true;
            if (pinned.IndexOf(@"\Microsoft\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0) return false;
            string a = Path.GetFileNameWithoutExtension(pinned), b = Path.GetFileNameWithoutExtension(running);
            if (a.Equals("wt", StringComparison.OrdinalIgnoreCase)) a = "WindowsTerminal";
            return a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }

        public static string NameOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return "App";
            if (path.StartsWith("::")) return "Recycle Bin";
            if (Directory.Exists(path)) { string d = Path.GetFileName(path.TrimEnd('\\')); return d.Length > 0 ? d : path; }
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(path);
            if (path.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) return "File Explorer";
            try { return ProcMeta.Describe(path, Path.GetFileNameWithoutExtension(path)); } catch { return Path.GetFileNameWithoutExtension(path); }
        }

        // App icon at exactly this pixel size, premultiplied, cached: scaled once from a 256 px master with bicubic so it is
        // drawn 1:1 (sharp) instead of being stretched every frame.
        public static Bitmap Icon(string path, int size)
        {
            size = Math.Max(8, size);
            string key = path + "|" + size;
            Bitmap b;
            if (icons.TryGetValue(key, out b)) return b;
            var master = Master(path);
            b = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.SmoothingMode = SmoothingMode.HighQuality;
                g.DrawImage(master, new Rectangle(0, 0, size, size));
            }
            if (icons.Count > 900) icons.Clear();   // many sizes of many apps: start over (bitmaps in use stay alive until drawn)
            icons[key] = b;
            return b;
        }

        static Bitmap Master(string path)
        {
            string key = path + "|master";
            Bitmap b;
            if (icons.TryGetValue(key, out b)) return b;
            b = ShellImage(IconSource(path), 256);
            if (b == null)
                try { using (var ic = System.Drawing.Icon.ExtractAssociatedIcon(path)) b = new Bitmap(ic.ToBitmap()); } catch { }
            if (b == null) { b = new Bitmap(64, 64, PixelFormat.Format32bppPArgb); using (var g = Graphics.FromImage(b)) using (var br = new SolidBrush(Color.FromArgb(120, 128, 128, 140))) { g.SmoothingMode = SmoothingMode.AntiAlias; g.FillEllipse(br, 4, 4, 56, 56); } }
            icons[key] = b;
            return b;
        }

        // App execution aliases (WindowsApps\wt.exe) have no icon of their own: use the packaged app.
        static string IconSource(string path)
        {
            if (path.IndexOf(@"\Microsoft\WindowsApps\", StringComparison.OrdinalIgnoreCase) < 0) return path;
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (name == "wt") return @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
            foreach (var w in List()) if (SameApp(path, w.Path)) return w.Path;
            return path;
        }

        static Bitmap ShellImage(string path, int size)
        {
            try
            {
                N.IShellItemImageFactory f;
                var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                if (N.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out f) != 0 || f == null) return null;
                IntPtr hbm;
                int hr = f.GetImage(new N.SIZE { cx = size, cy = size }, 0x1 | 0x4, out hbm); // BIGGERSIZEOK | ICONONLY
                Marshal.ReleaseComObject(f);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                try
                {
                    var ds = new N.DIBSECTION();
                    if (N.GetObject(hbm, Marshal.SizeOf(typeof(N.DIBSECTION)), ref ds) == 0 || ds.dsBm.bmBits == IntPtr.Zero || ds.dsBm.bmBitsPixel != 32) return null;
                    int w = ds.dsBm.bmWidth, h = ds.dsBm.bmHeight, stride = ds.dsBm.bmWidthBytes;
                    var raw = new byte[stride * h];
                    bool bottomUp = ds.dsBmih.biHeight > 0;
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(new IntPtr(ds.dsBm.bmBits.ToInt64() + (long)(bottomUp ? h - 1 - y : y) * stride), raw, y * stride, stride);
                    // Many icons come with straight (not premultiplied) alpha; reading them as premultiplied gave coloured
                    // speckles on the edges. A colour channel above its alpha can only happen with straight alpha.
                    bool straight = false;
                    for (int i = 0; i + 3 < raw.Length; i += 4) { byte a = raw[i + 3]; if (raw[i] > a || raw[i + 1] > a || raw[i + 2] > a) { straight = true; break; } }
                    var fmt = straight ? PixelFormat.Format32bppArgb : PixelFormat.Format32bppPArgb;
                    using (var src = new Bitmap(w, h, fmt))
                    {
                        var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, fmt);
                        for (int y = 0; y < h; y++) Marshal.Copy(raw, y * stride, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), Math.Min(stride, data.Stride));
                        src.UnlockBits(data);
                        var result = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
                        using (var g = Graphics.FromImage(result))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(src, new Rectangle(0, 0, size, size));
                        }
                        return result;
                    }
                }
                finally { N.DeleteObject(hbm); }
            }
            catch { return null; }
        }

        public static bool Launch(string path, string args)
        {
            try
            {
                if (path.StartsWith("::")) { Process.Start("explorer.exe", "shell:RecycleBinFolder"); return true; }
                Process.Start(new ProcessStartInfo(path, args ?? "") { UseShellExecute = true, WorkingDirectory = File.Exists(path) ? Path.GetDirectoryName(path) : "" });
                return true;
            }
            catch (Exception ex) { App.I.Balloon("Could not open", Path.GetFileName(path) + ": " + ex.Message); return false; }
        }

        public static void Activate(IntPtr h)
        {
            if (N.IsIconic(h)) N.ShowWindow(h, 9);
            if (!N.SetForegroundWindow(h) || N.GetForegroundWindow() != h) ForceForeground(h);
        }

        // SetForegroundWindow with the input of the current foreground thread attached (works from the hot corner too).
        public static void ForceForeground(IntPtr h)
        {
            IntPtr fg = N.GetForegroundWindow();
            uint pid; uint fgThread = N.GetWindowThreadProcessId(fg, out pid), me = N.GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me)
            {
                N.AttachThreadInput(me, fgThread, true);
                N.SetForegroundWindow(h); N.BringWindowToTop(h);
                N.AttachThreadInput(me, fgThread, false);
            }
            else { N.SetForegroundWindow(h); N.BringWindowToTop(h); }
        }

        public static void Close(IntPtr h) { N.PostMessage(h, 0x0010, IntPtr.Zero, IntPtr.Zero); }

        public static void OpenStart()
        {
            N.keybd_event(0x11, 0, 0, UIntPtr.Zero); N.keybd_event(0x1B, 0, 0, UIntPtr.Zero);       // Ctrl down, Esc down
            N.keybd_event(0x1B, 0, 2, UIntPtr.Zero); N.keybd_event(0x11, 0, 2, UIntPtr.Zero);       // Esc up, Ctrl up
        }
        public static void Minimize(IntPtr h) { N.ShowWindow(h, 6); }
        public static bool IsTopmost(IntPtr h) { return (N.GetWindowLong(h, -20) & 0x8) != 0; }
        public static void SetTopmost(IntPtr h, bool on) { N.SetWindowPos(h, new IntPtr(on ? -1 : -2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }

        public static Rectangle Bounds(IntPtr h)
        {
            N.RECT r;
            if (N.DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(N.RECT))) == 0) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (N.GetWindowRect(h, out r)) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return Rectangle.Empty;
        }

        public static bool ForegroundIsFullscreen()
        {
            IntPtr fg = N.GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;
            var cls = new StringBuilder(64); N.GetClassName(fg, cls, 64);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || Overlay.IsOverlayWindow(fg)) return false;
            var r = Bounds(fg);
            var s = Screen.FromHandle(fg).Bounds;
            return r.Left <= s.Left && r.Top <= s.Top && r.Right >= s.Right && r.Bottom >= s.Bottom;
        }
    }

    // ---------- Windows taskbar (hidden while the overlay is on) ----------
    // Hidden with the taskbar's own auto-hide state so its strip is given back to windows; the user's own state is saved
    // first and put back when the overlay goes off, when the app exits, or on the next start after a crash.
    static class Taskbar
    {
        static IntPtr Main { get { return N.FindWindow("Shell_TrayWnd", null); } }

        static List<IntPtr> Bars()
        {
            var l = new List<IntPtr>();
            IntPtr main = Main;
            if (main != IntPtr.Zero) l.Add(main);
            IntPtr h = IntPtr.Zero;
            while ((h = N.FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) l.Add(h);
            return l;
        }

        static readonly Stopwatch clock = Stopwatch.StartNew();   // not Environment.TickCount: it turns negative after 24.9 days
        static long revealUntil;
        static bool revealing;
        public static bool Revealed { get { return revealing && clock.ElapsedMilliseconds < revealUntil; } }

        static void SetState(int state)
        {
            IntPtr tray = Main;
            if (tray == IntPtr.Zero) return;
            var b = Abd(tray); b.lParam = (IntPtr)state;
            N.SHAppBarMessage(10 /*ABM_SETSTATE*/, ref b);
        }

        static N.APPBARDATA Abd(IntPtr h) { var a = new N.APPBARDATA(); a.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)); a.hWnd = h; return a; }

        public static void Hide()
        {
            IntPtr tray = Main;
            if (tray == IntPtr.Zero) return;
            var s = Store.Settings;
            if (!s.OverlayTaskbarHidden)
            {
                var a = Abd(tray);
                s.OverlayTaskbarState = (int)N.SHAppBarMessage(4 /*ABM_GETSTATE*/, ref a);
                s.OverlayTaskbarHidden = true;
                Store.MarkSettings();
                try { Store.SaveNow(); } catch { }   // so a crash can still put the taskbar back on the next start
            }
            revealing = false;
            SetState(1);   // ABS_AUTOHIDE: its strip goes back to windows
            foreach (var h in Bars()) N.ShowWindow(h, 0);
            // Windows applies the auto-hide state a moment later and shows a 2 px strip again: hide that too.
            Ui.Later(300, Enforce); Ui.Later(1000, Enforce);
        }

        // Explorer restarts or the auto-hide reveal can show it again.
        public static void Enforce()
        {
            if (!Store.Settings.OverlayTaskbarHidden) return;
            var bars = Bars();
            if (revealing)
            {
                // The revealed taskbar stays while the mouse is on it (or on the menus it opened above it).
                var p = Cursor.Position;
                foreach (var h in bars) { N.RECT r; if (N.GetWindowRect(h, out r) && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top - 2 && p.Y < r.Bottom) { revealUntil = clock.ElapsedMilliseconds + 1500; break; } }
                if (Revealed) return;
                revealing = false;
                SetState(1);
            }
            foreach (var h in bars) if (N.IsWindowVisible(h)) N.ShowWindow(h, 0);
        }

        // The tray button: the real taskbar in full (notification icons, Wi-Fi, sound, clock) for a few seconds, then hidden
        // again. In auto-hide state it would only show a 2 px strip, so it is switched to normal while revealed.
        public static void Reveal()
        {
            if (!Store.Settings.OverlayTaskbarHidden) return;
            revealUntil = clock.ElapsedMilliseconds + 6000;
            if (!revealing) { revealing = true; SetState(0); }
            foreach (var h in Bars()) N.ShowWindow(h, 5);
        }

        public static void Restore()
        {
            var s = Store.Settings;
            if (!s.OverlayTaskbarHidden) return;
            revealing = false;
            SetState(s.OverlayTaskbarState);
            foreach (var h in Bars()) N.ShowWindow(h, 5);
            s.OverlayTaskbarHidden = false;
            Store.MarkSettings();
        }
    }

    // ---------- Dock ----------
    // Drawn straight into one DIB section that the layered window shows (no per-frame bitmap copies); easing is time based
    // so motion stays smooth whatever the timer does. While it is not auto-hidden it reserves its strip at the bottom of
    // the screen like the taskbar does, so maximized windows end above it instead of under it.
    class OverlayDock : Form
    {
        class Item { public string Path = "", Args = "", Name = "", Match = "", Special; public bool Pinned; public List<WinInfo> Windows = new List<WinInfo>(); public RectangleF Rect; public float Scale = 1f; public long BounceStart; }

        readonly List<Item> items = new List<Item>();
        readonly Timer anim = new Timer { Interval = 16 }, refresh = new Timer { Interval = 1000 }, edge = new Timer { Interval = 50 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        float mouseX = -1, hoverAmount, hideOffset;
        bool mouseIn, hidden, appbar, fsHidden;
        int hot = -1;
        long leftAt, lastFrame, lastShown;
        uint callback;
        string lastSignature = "";
        RectangleF panelRect;
        IntPtr memDC, dib, oldBmp, bits;
        Bitmap surface;
        const int BounceMs = 1100;

        int Base { get { return Ui.S(Math.Max(20, Math.Min(96, Store.Settings.OverlayDockSize))); } }
        float MaxScale { get { return Store.Settings.OverlayMagnify && Overlay.Animate ? 1.4f : 1f; } }
        int Pad { get { return Math.Max(Ui.S(4), Base / 5); } }
        int DotSpace { get { return Ui.S(5); } }
        int PanelH { get { return Base + Pad * 2 + DotSpace; } }
        int Reserve { get { return PanelH + Ui.S(6); } }
        int Headroom { get { return (int)Math.Ceiling(Base * (MaxScale - 1)) + Base / 2 + Ui.S(34); } }

        public OverlayDock()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            AllowDrop = true;
            anim.Tick += delegate { Step(); };
            refresh.Tick += delegate { RefreshWindows(false); };
            edge.Tick += delegate { CheckEdge(); };
            DragEnter += (s, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; };
            DragDrop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files == null) return;
                foreach (var f in files) if (!Store.Settings.OverlayApps.Any(a => a.Path.Equals(f, StringComparison.OrdinalIgnoreCase))) Store.Settings.OverlayApps.Add(new DockItem { Name = Wins.NameOf(f), Path = f });
                Store.MarkSettings(); RefreshWindows(true);
            };
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x80 | 0x08000000 | 0x8; return cp; }   // layered, tool, no-activate, topmost
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            callback = N.RegisterWindowMessage("AllInOneOverlayDock");
            if (Store.Settings.OverlayApps.Count == 0) SeedApps();
            Place();
            RefreshWindows(true);
            refresh.Start(); edge.Start();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            SetAppBar(false);
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopAnim(); refresh.Stop(); edge.Stop();
            anim.Dispose(); refresh.Dispose(); edge.Dispose();
            FreeSurface();
            base.OnFormClosed(e);
        }

        static void SeedApps()
        {
            var l = Store.Settings.OverlayApps;
            l.Add(new DockItem { Name = "File Explorer", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") });
            foreach (var c in new[] { Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"), Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"), Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Google\Chrome\Application\chrome.exe") })
                if (File.Exists(c)) { l.Add(new DockItem { Name = "Google Chrome", Path = c }); break; }
            string wt = Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Microsoft\WindowsApps\wt.exe");
            if (File.Exists(wt)) l.Add(new DockItem { Name = "Terminal", Path = wt });
            l.Add(new DockItem { Name = Ui.AppTitle, Path = Application.ExecutablePath });
            Store.MarkSettings();
        }

        public void Reload() { Place(); RefreshWindows(true); }

        // ---------- Position and the reserved strip ----------
        N.APPBARDATA Abd() { var a = new N.APPBARDATA(); a.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)); a.hWnd = Handle; return a; }

        void SetAppBar(bool on)
        {
            if (!IsHandleCreated || on == appbar) return;
            var a = Abd();
            if (on) { a.uCallbackMessage = callback; appbar = N.SHAppBarMessage(0 /*ABM_NEW*/, ref a) != 0; }
            else { N.SHAppBarMessage(1 /*ABM_REMOVE*/, ref a); appbar = false; }
        }

        void Place()
        {
            if (!IsHandleCreated) return;
            var scr = Screen.PrimaryScreen.Bounds;
            SetAppBar(!Store.Settings.OverlayAutoHide);
            int bottom;
            if (appbar)
            {
                var a = Abd();
                a.uEdge = 3;   // ABE_BOTTOM
                a.rc = new N.RECT { Left = scr.Left, Right = scr.Right, Top = scr.Bottom - Reserve, Bottom = scr.Bottom };
                N.SHAppBarMessage(2 /*ABM_QUERYPOS*/, ref a);
                a.rc.Top = a.rc.Bottom - Reserve;
                N.SHAppBarMessage(3 /*ABM_SETPOS*/, ref a);
                bottom = a.rc.Bottom;
            }
            else bottom = Store.Settings.OverlayTaskbarHidden ? scr.Bottom : Screen.PrimaryScreen.WorkingArea.Bottom;
            int h = Reserve + Headroom;
            N.SetWindowPos(Handle, new IntPtr(-1), scr.Left, bottom - h, scr.Width, h, 0x0010);
        }

        // Pinned apps first, then running apps that are not pinned, then All windows and the Recycle Bin.
        public void RefreshWindows(bool force)
        {
            if (IsDisposed) return;
            var wins = Wins.List();
            var list = new List<Item>();
            list.Add(new Item { Special = "start", Name = "Start" });
            foreach (var d in Store.Settings.OverlayApps)
                list.Add(new Item { Path = d.Path, Args = d.Args, Name = string.IsNullOrEmpty(d.Name) ? Wins.NameOf(d.Path) : d.Name, Match = Wins.Target(d.Path), Pinned = true });
            foreach (var w in wins)
            {
                if (string.IsNullOrEmpty(w.Path)) continue;
                var it = list.FirstOrDefault(x => Wins.SameApp(x.Match, w.Path));
                if (it == null) { it = new Item { Path = w.Path, Name = Wins.NameOf(w.Path), Match = w.Path }; list.Add(it); }
                if (w.Path.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) && w.Class != "CabinetWClass") continue;
                it.Windows.Add(w);
            }
            list.RemoveAll(x => x.Special == null && !x.Pinned && x.Windows.Count == 0);
            list.Add(new Item { Special = "overview", Name = "All windows" });
            list.Add(new Item { Special = "trash", Path = "::{645FF040-5081-101B-9F08-00AA002F954E}", Name = "Recycle Bin" });
            if (Store.Settings.OverlayTaskbarHidden) list.Add(new Item { Special = "tray", Name = "Windows tray (notification icons, Wi-Fi, sound, clock)" });
            list.Add(new Item { Special = "exit", Name = "Exit overlay  (" + Hotkeys.Get("overlay") + ")" });
            string sig = string.Join("|", list.Select(x => x.Path + ":" + x.Windows.Count + ":" + x.Pinned));
            foreach (var x in list) { var old = items.FirstOrDefault(o => o.Path == x.Path && o.Special == x.Special); if (old != null) { x.Scale = old.Scale; x.BounceStart = x.Windows.Count > 0 && old.Windows.Count == 0 ? 0 : old.BounceStart; } }
            items.Clear(); items.AddRange(list);
            if (hot >= items.Count) hot = -1;
            if (force || sig != lastSignature) { lastSignature = sig; Render(); }
        }

        // ---------- Hover, auto-hide and full-screen apps ----------
        RectangleF HoverZone()
        {
            float top = panelRect.Top;
            foreach (var it in items) top = Math.Min(top, it.Rect.Top);
            return new RectangleF(Left + panelRect.Left, Top + top - Ui.S(4), panelRect.Width, panelRect.Bottom - top + Ui.S(8));
        }

        void CheckEdge() { CheckEdgeAt(Cursor.Position); }

        void CheckEdgeAt(Point p)
        {
            long now = clock.ElapsedMilliseconds;
            // A full-screen app or the revealed Windows taskbar: stay out of the way.
            bool fs = Wins.ForegroundIsFullscreen();
            if (fs != fsHidden) { fsHidden = fs; StartAnim(); }   // no edge line over full-screen videos and games
            if (fs || Taskbar.Revealed)
            {
                if (!hidden || mouseIn) { hidden = true; mouseIn = false; hot = -1; StartAnim(); }
                return;
            }
            bool auto = Store.Settings.OverlayAutoHide;
            // Auto-hide: touching the bottom edge of the screen anywhere brings the dock up at once.
            bool edgeHit = auto && p.Y >= Bottom - 1 && p.X >= Left && p.X < Right;
            // Mouse messages stop over transparent pixels, so hover is tracked here too, not only in OnMouseMove.
            bool over = !hidden && HoverZone().Contains(p);
            if (over || edgeHit)
            {
                lastShown = now;
                float mx = p.X - Left;
                if (!mouseIn || Math.Abs(mx - mouseX) > 0.5f)
                {
                    mouseIn = true; mouseX = mx;
                    hot = over ? HitTest(new Point(p.X - Left, p.Y - Top)) : -1;
                    StartAnim();
                }
            }
            else if (mouseIn) { mouseIn = false; hot = -1; leftAt = now; StartAnim(); }
            bool wantHidden = auto && !mouseIn && now - lastShown > 400;
            if (wantHidden != hidden) { hidden = wantHidden; StartAnim(); }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            if (appbar && callback != 0 && m.Msg == callback)
            {
                if (m.WParam.ToInt32() == 1) { Place(); Render(); }   // ABN_POSCHANGED
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (fsHidden || Taskbar.Revealed) return;
            mouseIn = true; mouseX = e.X;
            lastShown = clock.ElapsedMilliseconds;
            if (hidden) hidden = false;   // touched the bottom edge: up at once
            hot = HitTest(e.Location);
            if (hoverAmount < 1f || anim.Enabled) StartAnim(); else Render();   // follow the mouse in the same frame
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = HitTest(e.Location);
            if (i < 0) return;
            var it = items[i];
            if (e.Button == MouseButtons.Left) OpenItem(it);
            else if (e.Button == MouseButtons.Middle && it.Special == null) Wins.Launch(it.Path, it.Args);
            else if (e.Button == MouseButtons.Right) ShowMenu(it);
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var r = items[i].Rect; r.Inflate(Base * 0.12f, Pad);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        void OpenItem(Item it)
        {
            if (it.Special == "overview") { Overlay.ShowOverview(); return; }
            if (it.Special == "trash") { Wins.Launch(it.Path, null); return; }
            if (it.Special == "start") { Wins.OpenStart(); return; }
            if (it.Special == "exit") { BeginInvoke(new Action(() => Overlay.Set(false))); return; }
            if (it.Special == "tray") { mouseIn = false; hot = -1; hidden = true; StartAnim(); Taskbar.Reveal(); return; }
            var live = it.Windows;
            if (live.Count == 0) { if (Wins.Launch(it.Path, it.Args)) { it.BounceStart = clock.ElapsedMilliseconds; StartAnim(); } return; }
            IntPtr fg = N.GetForegroundWindow();
            int at = live.FindIndex(w => w.Hwnd == fg);
            // Click the active app again to cycle through its windows (one window: minimize it).
            var target = at >= 0 && live.Count > 1 ? live[(at + 1) % live.Count] : live[0];
            if (at >= 0 && live.Count == 1) { Wins.Minimize(target.Hwnd); return; }
            Wins.Activate(target.Hwnd);
        }

        void ShowMenu(Item it)
        {
            var m = Ui.Menu();
            if (it.Special == null)
            {
                foreach (var w in it.Windows.Take(12)) { var hw = w.Hwnd; m.Items.Add((w.Minimized ? "(minimized) " : "") + Ui.OneLine(w.Title, 60), null, delegate { Wins.Activate(hw); }); }
                if (it.Windows.Count > 0) m.Items.Add(new ToolStripSeparator());
                m.Items.Add(it.Windows.Count > 0 ? "New window" : "Open", null, delegate { Wins.Launch(it.Path, it.Args); });
                var pinned = Store.Settings.OverlayApps.FirstOrDefault(d => d.Path.Equals(it.Path, StringComparison.OrdinalIgnoreCase));
                m.Items.Add(pinned != null ? "Remove from Dock" : "Keep in Dock", null, delegate
                {
                    if (pinned != null) Store.Settings.OverlayApps.Remove(pinned);
                    else Store.Settings.OverlayApps.Add(new DockItem { Name = it.Name, Path = it.Path });
                    Store.MarkSettings(); RefreshWindows(true);
                });
                if (pinned != null)
                {
                    int idx = Store.Settings.OverlayApps.IndexOf(pinned);
                    if (idx > 0) m.Items.Add("Move left", null, delegate { var l = Store.Settings.OverlayApps; l.RemoveAt(idx); l.Insert(idx - 1, pinned); Store.MarkSettings(); RefreshWindows(true); });
                    if (idx < Store.Settings.OverlayApps.Count - 1) m.Items.Add("Move right", null, delegate { var l = Store.Settings.OverlayApps; l.RemoveAt(idx); l.Insert(idx + 1, pinned); Store.MarkSettings(); RefreshWindows(true); });
                }
                if (File.Exists(it.Match)) m.Items.Add("Show in File Explorer", null, delegate { try { Process.Start("explorer.exe", "/select,\"" + it.Match + "\""); } catch { } });
                if (it.Windows.Count > 0) m.Items.Add(it.Windows.Count > 1 ? "Close all " + it.Windows.Count + " windows" : "Close window", null, delegate { foreach (var w in it.Windows) Wins.Close(w.Hwnd); });
                m.Items.Add(new ToolStripSeparator());
            }
            m.Items.Add("Show all windows", null, delegate { Overlay.ShowOverview(); });
            var size = new ToolStripMenuItem("Icon size");
            foreach (int px in new[] { 24, 28, 32, 40, 48, 64 })
            {
                int v = px;
                size.DropDownItems.Add(new ToolStripMenuItem(v + " px", null, delegate { Store.Settings.OverlayDockSize = v; Store.MarkSettings(); Reload(); }) { Checked = Store.Settings.OverlayDockSize == v });
            }
            m.Items.Add(size);
            m.Items.Add(Store.Settings.OverlayAutoHide ? "Turn hiding off" : "Turn hiding on", null, delegate { Store.Settings.OverlayAutoHide = !Store.Settings.OverlayAutoHide; Store.MarkSettings(); Reload(); });
            m.Items.Add(Store.Settings.OverlayMagnify ? "Turn magnification off" : "Turn magnification on", null, delegate { Store.Settings.OverlayMagnify = !Store.Settings.OverlayMagnify; Store.MarkSettings(); Reload(); });
            m.Items.Add("Overlay settings...", null, delegate { App.I.ShowSettings(null); });
            m.Items.Add("Turn the overlay off", null, delegate { Overlay.Set(false); });
            m.Show(Cursor.Position);
        }

        // ---------- Animation ----------
        void StartAnim()
        {
            if (anim.Enabled) return;
            lastFrame = clock.ElapsedMilliseconds - 10;
            anim.Start();
        }

        void StopAnim()
        {
            anim.Stop();
        }

        void Step()
        {
            long now = clock.ElapsedMilliseconds;
            float dt = Math.Max(1, Math.Min(48, now - lastFrame));
            lastFrame = now;
            bool moving = Overlay.Animate, busy = false;
            float kHover = moving ? 1f - (float)Math.Exp(-dt / 45f) : 1f, kSlide = moving ? 1f - (float)Math.Exp(-dt / 32f) : 1f;
            float th = mouseIn && !hidden ? 1f : 0f;
            hoverAmount += (th - hoverAmount) * kHover;
            if (Math.Abs(th - hoverAmount) < 0.004f) hoverAmount = th; else busy = true;
            float to = hidden ? PanelH + Ui.S(10) : 0f;
            hideOffset += (to - hideOffset) * kSlide;
            if (Math.Abs(to - hideOffset) < 0.5f) hideOffset = to; else busy = true;
            foreach (var it in items) if (it.BounceStart > 0) { if (now - it.BounceStart < BounceMs) busy = true; else it.BounceStart = 0; }
            Render();
            if (!busy) StopAnim();
        }

        // ---------- Layout ----------
        void Arrange()
        {
            int b = Base; float gap = b * 0.22f, sep = Ui.S(12);
            int n = items.Count;
            float max = MaxScale, range = b * 3f;
            float baseTotal = 0;
            for (int i = 0; i < n; i++) baseTotal += b + gap + (IsSepBefore(i) ? sep : 0);
            baseTotal -= gap;
            float x = (Width - baseTotal) / 2f;
            var centers = new float[n];
            for (int i = 0; i < n; i++) { if (IsSepBefore(i)) x += sep; centers[i] = x + b / 2f; x += b + gap; }
            float total = 0;
            for (int i = 0; i < n; i++)
            {
                float f = 0;
                if (mouseX >= 0 && hoverAmount > 0) { float d = Math.Abs(mouseX - centers[i]); if (d < range) f = (float)(Math.Cos(Math.PI * d / range) + 1) / 2f; }
                items[i].Scale = 1 + (max - 1) * f * hoverAmount;
                total += b * items[i].Scale + gap + (IsSepBefore(i) ? sep : 0);
            }
            total -= gap;
            float left = (Width - total) / 2f, panelBottom = Height - Ui.S(3) + hideOffset;
            float iconBottom = panelBottom - Pad - DotSpace;
            long now = clock.ElapsedMilliseconds;
            x = left;
            for (int i = 0; i < n; i++)
            {
                if (IsSepBefore(i)) x += sep;
                float s = b * items[i].Scale, bounce = 0;
                if (items[i].BounceStart > 0)
                {
                    double t = (now - items[i].BounceStart) / (double)BounceMs;
                    if (t < 1) bounce = (float)(Math.Abs(Math.Sin(t * Math.PI * 2)) * (1 - t) * b * 0.6);
                }
                items[i].Rect = new RectangleF(x, iconBottom - s - bounce, s, s);
                x += s + gap;
            }
            panelRect = new RectangleF(left - Pad, panelBottom - PanelH, total + Pad * 2, PanelH);
        }

        bool IsSepBefore(int i)
        {
            if (i == 0) return false;
            var a = items[i - 1]; var c = items[i];
            if (a.Special == "start") return true;
            if (c.Special != null && a.Special == null) return true;
            return a.Pinned && !c.Pinned && c.Special == null;
        }

        // ---------- Drawing ----------
        void EnsureSurface(int w, int h)
        {
            if (surface != null && surface.Width == w && surface.Height == h) return;
            FreeSurface();
            var bi = new N.BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr screen = N.GetDC(IntPtr.Zero);
            memDC = N.CreateCompatibleDC(screen);
            N.ReleaseDC(IntPtr.Zero, screen);
            dib = N.CreateDIBSection(memDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            oldBmp = N.SelectObject(memDC, dib);
            surface = new Bitmap(w, h, w * 4, PixelFormat.Format32bppPArgb, bits);
        }

        void FreeSurface()
        {
            if (surface != null) { surface.Dispose(); surface = null; }
            if (memDC != IntPtr.Zero) { N.SelectObject(memDC, oldBmp); N.DeleteDC(memDC); memDC = IntPtr.Zero; }
            if (dib != IntPtr.Zero) { N.DeleteObject(dib); dib = IntPtr.Zero; }
        }

        void Render()
        {
            if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
            EnsureSurface(Width, Height);
            Arrange();
            bool dark = Ui.Dark;

            using (var g = Graphics.FromImage(surface))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var pr = Rectangle.Round(panelRect);
                int rad = Math.Max(2, (int)(Math.Min(Ui.S(16), pr.Height * 0.32f) * Ui.RadiusScale));
                for (int i = 3; i >= 1; i--)
                    using (var path = Ui.Round(new Rectangle(pr.X - i, pr.Y - i + Ui.S(2), pr.Width + i * 2, pr.Height + i * 2), rad + i))
                    using (var br = new SolidBrush(Color.FromArgb(12, 0, 0, 0))) g.FillPath(br, path);
                using (var path = Ui.Round(pr, rad))
                {
                    using (var br = new SolidBrush(dark ? Color.FromArgb(200, 32, 34, 42) : Color.FromArgb(196, 246, 247, 251))) g.FillPath(br, path);
                    using (var pen = new Pen(dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255))) g.DrawPath(pen, path);
                }
                using (var path = Ui.Round(new Rectangle(pr.X - 1, pr.Y - 1, pr.Width + 2, pr.Height + 2), rad + 1))
                using (var pen = new Pen(Color.FromArgb(dark ? 90 : 36, 0, 0, 0))) g.DrawPath(pen, path);
                using (var sepPen = new Pen(Color.FromArgb(dark ? 70 : 55, dark ? 255 : 0, dark ? 255 : 0, dark ? 255 : 0)))
                using (var dotBrush = new SolidBrush(dark ? Color.FromArgb(230, 235, 235, 240) : Color.FromArgb(200, 30, 30, 40)))
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i]; var r = it.Rect;
                        if (IsSepBefore(i)) { float sx = r.X - Base * 0.11f - Ui.S(6); g.DrawLine(sepPen, sx, pr.Y + Pad, sx, pr.Bottom - Pad); }
                        if (it.Special == "overview") DrawGlyphTile(g, r, "", false);
                        else if (it.Special == "start") DrawStartTile(g, r, dark);
                        else if (it.Special == "tray") DrawGlyphTile(g, r, "", true);
                        else if (it.Special == "exit") DrawExitTile(g, r);
                        else
                        {
                            int s = Math.Max(8, (int)Math.Round(r.Width));
                            g.DrawImage(Wins.Icon(it.Path, s), new Rectangle((int)Math.Round(r.X + (r.Width - s) / 2f), (int)Math.Round(r.Y + (r.Height - s) / 2f), s, s));
                        }
                        if (it.Windows.Count > 0) { float d = Math.Max(3f, 3.5f * Ui.Scale); g.FillEllipse(dotBrush, r.X + r.Width / 2 - d / 2, pr.Bottom - DotSpace - d / 2 - Ui.S(1), d, d); }
                    }
                }
                if (hot >= 0 && hot < items.Count && mouseIn && !hidden && hoverAmount > 0.5f)
                {
                    var it = items[hot]; var r = it.Rect;
                    string name = it.Name + (it.Windows.Count > 1 ? "  (" + it.Windows.Count + ")" : "");
                    var f = Ui.Base;
                    var sz = g.MeasureString(name, f);
                    var lr = new RectangleF(r.X + r.Width / 2 - sz.Width / 2 - Ui.S(9), r.Y - sz.Height - Ui.S(14), sz.Width + Ui.S(18), sz.Height + Ui.S(6));
                    if (lr.Y < 1) lr.Y = 1;
                    if (lr.X < 2) lr.X = 2; if (lr.Right > Width - 2) lr.X = Width - 2 - lr.Width;
                    using (var path = Ui.Round(Rectangle.Round(lr), Ui.S(7)))
                    using (var br = new SolidBrush(dark ? Color.FromArgb(235, 44, 46, 56) : Color.FromArgb(240, 250, 250, 252))) g.FillPath(br, path);
                    using (var br = new SolidBrush(dark ? Color.FromArgb(240, 240, 244) : Color.FromArgb(30, 30, 36)))
                        g.DrawString(name, f, br, lr.X + Ui.S(9), lr.Y + Ui.S(3));
                }
            }
            if (Store.Settings.OverlayAutoHide && !fsHidden)
                using (var g = Graphics.FromImage(surface)) using (var br = new SolidBrush(Color.FromArgb(1, 0, 0, 0))) { g.CompositingMode = CompositingMode.SourceCopy; g.FillRectangle(br, 0, Height - 1, Width, 1); }
            var size = new N.SIZE { cx = Width, cy = Height };
            var src = new N.POINT(); var dst = new N.POINT { x = Left, y = Top };
            var blend = new N.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            N.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDC, ref src, 0, ref blend, 2);
        }

        static void DrawGlyphTile(Graphics g, RectangleF r, string glyph, bool subtle)
        {
            var rr = Rectangle.Round(r);
            bool dark = Ui.Dark;
            using (var path = Ui.Round(rr, Math.Max(2, (int)(rr.Width * 0.22f * Ui.RadiusScale))))
            {
                if (subtle) using (var br = new SolidBrush(dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(150, 255, 255, 255))) g.FillPath(br, path);
                else using (var br = new LinearGradientBrush(rr, Ui.Mix(Ui.AccentFill, Color.White, 0.25), Ui.AccentFill, 90f)) g.FillPath(br, path);
            }
            var f = Ui.Icon((float)Math.Round(Math.Max(7f, rr.Width * 0.36f / Ui.Scale) * 2) / 2f);
            using (var br = new SolidBrush(subtle ? (dark ? Color.FromArgb(235, 235, 240) : Color.FromArgb(40, 40, 48)) : Color.White))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(glyph, f, br, r, sf);
        }

        static void DrawExitTile(Graphics g, RectangleF r)
        {
            var rr = Rectangle.Round(r);
            using (var path = Ui.Round(rr, Math.Max(2, (int)(rr.Width * 0.22f * Ui.RadiusScale))))
            using (var br = new LinearGradientBrush(rr, Color.FromArgb(240, 96, 96), Color.FromArgb(200, 50, 56), 90f)) g.FillPath(br, path);
            var f = Ui.Icon((float)Math.Round(Math.Max(7f, rr.Width * 0.38f / Ui.Scale) * 2) / 2f);
            using (var br = new SolidBrush(Color.White))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("", f, br, r, sf);   // power
        }

        // Start button: four panes, like the Windows start button.
        static void DrawStartTile(Graphics g, RectangleF r, bool dark)
        {
            float pad = r.Width * 0.2f, gap = Math.Max(1f, r.Width * 0.05f);
            float s = (r.Width - pad * 2 - gap) / 2f;
            var c1 = Color.FromArgb(0, 120, 212); var c2 = Color.FromArgb(70, 170, 255);
            for (int i = 0; i < 4; i++)
            {
                var q = new RectangleF(r.X + pad + (i % 2) * (s + gap), r.Y + pad + (i / 2) * (s + gap), s, s);
                using (var br = new LinearGradientBrush(RectangleF.Inflate(q, 1, 1), c2, c1, 45f)) g.FillRectangle(br, q);
            }
        }
    }

    // ---------- Menu bar (an app bar at the top: maximized windows stay below it) ----------
    class OverlayMenuBar : Form
    {
        readonly Timer tick = new Timer { Interval = 500 };
        uint callback;
        bool registered;
        IntPtr activeWin;
        string activePath = "", activeName = "", activeTitle = "";
        Rectangle logoRect, appRect, clockRect, overviewRect, cpuRect, exitRect;
        long prevIdle, prevKernel, prevUser; int cpu, mem; int sampleCount;
        int BarH { get { return Ui.S(30); } }

        public OverlayMenuBar()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            DoubleBuffered = true; Font = Ui.Base;
            BackColor = Ui.Dark ? Color.FromArgb(28, 29, 34) : Color.FromArgb(244, 245, 248);
            var scr = Screen.PrimaryScreen.Bounds;
            Bounds = new Rectangle(scr.Left, scr.Top, scr.Width, BarH);
            tick.Tick += delegate { Sample(); };
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; } }
        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            callback = N.RegisterWindowMessage("AllInOneOverlayMenuBar");
            var abd = Abd(); abd.uCallbackMessage = callback;
            registered = N.SHAppBarMessage(0 /*ABM_NEW*/, ref abd) != 0;
            PlaceBar();
            Sample(); tick.Start();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            tick.Stop();
            if (registered) { var abd = Abd(); N.SHAppBarMessage(1 /*ABM_REMOVE*/, ref abd); registered = false; }
            base.OnHandleDestroyed(e);
        }

        N.APPBARDATA Abd() { var a = new N.APPBARDATA(); a.cbSize = Marshal.SizeOf(typeof(N.APPBARDATA)); a.hWnd = Handle; return a; }

        void PlaceBar()
        {
            var scr = Screen.PrimaryScreen.Bounds;
            var abd = Abd();
            abd.uEdge = 1; // ABE_TOP
            abd.rc = new N.RECT { Left = scr.Left, Top = scr.Top, Right = scr.Right, Bottom = scr.Top + BarH };
            if (registered)
            {
                N.SHAppBarMessage(2 /*ABM_QUERYPOS*/, ref abd);
                abd.rc.Bottom = abd.rc.Top + BarH;
                N.SHAppBarMessage(3 /*ABM_SETPOS*/, ref abd);
            }
            N.SetWindowPos(Handle, new IntPtr(-1), abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, BarH, 0x0010);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // no activate on click
            if (registered && m.Msg == callback)
            {
                int code = m.WParam.ToInt32();
                if (code == 1) PlaceBar();                                                     // ABN_POSCHANGED
                else if (code == 2) N.SetWindowPos(Handle, new IntPtr(m.LParam != IntPtr.Zero ? 1 : -1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // full-screen app: step back
                return;
            }
            base.WndProc(ref m);
        }

        public void Sample()
        {
            IntPtr fg = N.GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != Handle && !Overlay.IsOverlayWindow(fg))
            {
                var cls = new StringBuilder(64); N.GetClassName(fg, cls, 64);
                string c = cls.ToString();
                activeWin = fg;
                if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") { activePath = ""; activeName = "Desktop"; activeTitle = ""; }
                else
                {
                    uint pid; N.GetWindowThreadProcessId(fg, out pid);
                    activePath = Wins.PathOf((int)pid);
                    if (activePath.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                        N.EnumChildWindows(fg, (ch, l) => { uint cp; N.GetWindowThreadProcessId(ch, out cp); if (cp != pid) { activePath = Wins.PathOf((int)cp); return false; } return true; }, IntPtr.Zero);
                    activeName = Wins.NameOf(activePath);
                    int len = N.GetWindowTextLength(fg); var sb = new StringBuilder(len + 1); N.GetWindowText(fg, sb, sb.Capacity); activeTitle = sb.ToString();
                }
            }
            if (sampleCount++ % 4 == 0)
            {
                long idle, kernel, user;
                if (N.GetSystemTimes(out idle, out kernel, out user))
                {
                    long dIdle = idle - prevIdle, dTotal = (kernel - prevKernel) + (user - prevUser);
                    if (prevKernel != 0 && dTotal > 0) cpu = (int)Math.Round(Math.Max(0, Math.Min(100, (1.0 - (double)dIdle / dTotal) * 100)));
                    prevIdle = idle; prevKernel = kernel; prevUser = user;
                }
                var ms = new N.MEMSTATUS(); if (N.GlobalMemoryStatusEx(ms)) mem = (int)ms.dwMemoryLoad;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fg = Ui.Text; var muted = Ui.Muted;
            g.Clear(Ui.Dark ? Color.FromArgb(28, 29, 34) : Color.FromArgb(244, 245, 248));   // follows a theme switch
            using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            int x = Ui.S(14), h = Height;
            if (App.AppIcon != null) using (var ico = new Icon(App.AppIcon, Ui.S(16), Ui.S(16))) g.DrawIcon(ico, new Rectangle(x, (h - Ui.S(16)) / 2, Ui.S(16), Ui.S(16)));
            logoRect = new Rectangle(x - Ui.S(8), 0, Ui.S(32), h);
            x += Ui.S(30);
            var nameSize = TextRenderer.MeasureText(activeName, Ui.Bold, Size.Empty, TextFormatFlags.NoPadding);
            appRect = new Rectangle(x - Ui.S(8), 0, nameSize.Width + Ui.S(16), h);
            TextRenderer.DrawText(g, activeName, Ui.Bold, new Rectangle(x, 0, nameSize.Width + 2, h), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            x += nameSize.Width + Ui.S(18);
            // Right side: Exit overlay, clock, memory, CPU, all windows.
            int right = Width - Ui.S(6);
            string exitText = "Exit overlay";
            var es = TextRenderer.MeasureText(exitText, Ui.Base, Size.Empty, TextFormatFlags.NoPadding);
            exitRect = new Rectangle(right - es.Width - Ui.S(34), Ui.S(4), es.Width + Ui.S(34), h - Ui.S(8));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(exitRect, Ui.S(6))) using (var br = new SolidBrush(Color.FromArgb(220, 60, 64))) g.FillPath(br, path);
            TextRenderer.DrawText(g, "", Ui.Icon(8f), new Rectangle(exitRect.X + Ui.S(8), exitRect.Y, Ui.S(14), exitRect.Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, exitText, Ui.Base, new Rectangle(exitRect.X + Ui.S(26), exitRect.Y, es.Width + 2, exitRect.Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            right = exitRect.Left - Ui.S(16);
            var now = DateTime.Now;
            string clock = now.ToString("ddd d MMM") + "   " + now.ToString("HH:mm");
            var cs = TextRenderer.MeasureText(clock, Ui.Base, Size.Empty, TextFormatFlags.NoPadding);
            clockRect = new Rectangle(right - cs.Width - Ui.S(8), 0, cs.Width + Ui.S(16), h);
            TextRenderer.DrawText(g, clock, Ui.Base, new Rectangle(right - cs.Width, 0, cs.Width + 2, h), fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            right -= cs.Width + Ui.S(22);
            var power = SystemInformation.PowerStatus;
            if (power.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery && power.BatteryLifePercent <= 1f)
            {
                string bat = (int)Math.Round(power.BatteryLifePercent * 100) + "%" + (power.PowerLineStatus == PowerLineStatus.Online ? " +" : "");
                var bs = TextRenderer.MeasureText(bat, Ui.Base, Size.Empty, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, bat, Ui.Base, new Rectangle(right - bs.Width, 0, bs.Width + 2, h), power.BatteryLifePercent < 0.2f ? Ui.Danger : fg, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                right -= bs.Width + Ui.S(18);
            }
            string stats = "CPU " + cpu + "%   RAM " + mem + "%";
            var ss = TextRenderer.MeasureText(stats, Ui.Base, Size.Empty, TextFormatFlags.NoPadding);
            cpuRect = new Rectangle(right - ss.Width - Ui.S(8), 0, ss.Width + Ui.S(16), h);
            TextRenderer.DrawText(g, stats, Ui.Base, new Rectangle(right - ss.Width, 0, ss.Width + 2, h), cpu > 85 || mem > 90 ? Ui.Danger : muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            right -= ss.Width + Ui.S(20);
            overviewRect = new Rectangle(right - Ui.S(22), 0, Ui.S(28), h);
            TextRenderer.DrawText(g, "", Ui.Icon(10.5f), overviewRect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            right -= Ui.S(36);
            if (activeTitle.Length > 0 && activeTitle != activeName && right - x > Ui.S(80))
                TextRenderer.DrawText(g, activeTitle, Ui.Base, new Rectangle(x, 0, right - x, h), muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
            var at = new Point(PointToScreen(new Point(e.X, Height)).X, PointToScreen(new Point(0, Height)).Y);
            if (exitRect.Contains(e.Location)) { BeginInvoke(new Action(() => Overlay.Set(false))); return; }
            if (logoRect.Contains(e.Location)) LogoMenu().Show(new Point(PointToScreen(new Point(logoRect.Left, Height)).X, at.Y));
            else if (appRect.Contains(e.Location)) AppMenu().Show(new Point(PointToScreen(new Point(appRect.Left, Height)).X, at.Y));
            else if (overviewRect.Contains(e.Location)) Overlay.ShowOverview();
            else if (cpuRect.Contains(e.Location)) App.I.ShowMain("system");
            else if (clockRect.Contains(e.Location)) ClockMenu().Show(new Point(PointToScreen(new Point(clockRect.Left, Height)).X, at.Y));
        }

        ContextMenuStrip LogoMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("About this PC", null, delegate { AboutPc(); });
            m.Items.Add(Ui.AppTitle, null, delegate { App.I.ShowMain(null); });
            m.Items.Add("Show all windows", null, delegate { Overlay.ShowOverview(); });
            m.Items.Add("Windows settings", null, delegate { try { Process.Start("ms-settings:"); } catch { } });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Lock", null, delegate { N.LockWorkStation(); });
            m.Items.Add("Sleep", null, delegate { Application.SetSuspendState(PowerState.Suspend, false, false); });
            m.Items.Add("Restart...", null, delegate { if (MessageBox.Show("Restart the PC now? Save your work first.", Ui.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { try { Overlay.Shutdown(); } catch { } try { Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); } catch { } } });
            m.Items.Add("Shut down...", null, delegate { if (MessageBox.Show("Shut down the PC now? Save your work first.", Ui.AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { try { Overlay.Shutdown(); } catch { } try { Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); } catch { } } });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Overlay settings...", null, delegate { App.I.ShowSettings(null); });
            m.Items.Add("Turn the overlay off", null, delegate { Overlay.Set(false); });
            return m;
        }

        ContextMenuStrip AppMenu()
        {
            var m = Ui.Menu();
            var w = activeWin; string path = activePath, name = activeName;
            bool real = w != IntPtr.Zero && path.Length > 0;
            if (real)
            {
                m.Items.Add("Minimize", null, delegate { Wins.Minimize(w); });
                var top = new ToolStripMenuItem("Keep on top") { Checked = Wins.IsTopmost(w) };
                top.Click += delegate { Wins.SetTopmost(w, !Wins.IsTopmost(w)); };
                m.Items.Add(top);
                m.Items.Add("Close window", null, delegate { Wins.Close(w); });
                if (File.Exists(path)) m.Items.Add("Show in File Explorer", null, delegate { try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } });
                if (!Store.Settings.OverlayApps.Any(d => d.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                    m.Items.Add("Keep in Dock", null, delegate { Store.Settings.OverlayApps.Add(new DockItem { Name = name, Path = path }); Store.MarkSettings(); Overlay.ApplySettings(); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Quit " + name, null, delegate { foreach (var x in Wins.List().Where(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) Wins.Close(x.Hwnd); });
            }
            else m.Items.Add("Show all windows", null, delegate { Overlay.ShowOverview(); });
            return m;
        }

        ContextMenuStrip ClockMenu()
        {
            var m = Ui.Menu();
            var now = DateTime.Now;
            m.Items.Add(now.ToString("dddd, d MMMM yyyy"), null, delegate { App.I.CopyText(now.ToString("yyyy-MM-dd")); });
            m.Items.Add("Copy date and time", null, delegate { App.I.CopyText(DateTime.Now.ToString("yyyy-MM-dd HH:mm")); });
            m.Items.Add("Tasks for today", null, delegate { App.I.ShowMain("tasks"); });
            m.Items.Add("Date and time settings", null, delegate { try { Process.Start("ms-settings:dateandtime"); } catch { } });
            return m;
        }

        static void AboutPc()
        {
            string os = Environment.OSVersion.VersionString, cpuName = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null) os = (k.GetValue("ProductName") as string ?? "Windows") + " " + (k.GetValue("DisplayVersion") as string ?? "") + " (build " + (k.GetValue("CurrentBuild") as string ?? "") + ")";
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (k != null) cpuName = ((k.GetValue("ProcessorNameString") as string) ?? "").Trim();
            }
            catch { }
            var ms = new N.MEMSTATUS(); N.GlobalMemoryStatusEx(ms);
            var up = TimeSpan.FromMilliseconds(Environment.TickCount & int.MaxValue);
            MessageBox.Show("Computer: " + Environment.MachineName + "\nWindows: " + os + "\nProcessor: " + cpuName + " (" + Environment.ProcessorCount + " threads)" +
                "\nMemory: " + (ms.ullTotalPhys / (1024.0 * 1024 * 1024)).ToString("0.0") + " GB" + "\nUp for: " + (int)up.TotalDays + " d " + up.Hours + " h " + up.Minutes + " min", "About this PC");
        }
    }

    // ---------- Window overview (live thumbnails of every window) ----------
    class OverviewForm : Form
    {
        class Tile { public WinInfo Win; public IntPtr Thumb; public Rectangle Dest, From; public Size Src; }
        readonly List<Tile> tiles = new List<Tile>();
        readonly Timer anim = new Timer { Interval = 15 };
        Bitmap backdrop;
        int hover = -1, sel = -1;
        long animStart;
        const int AnimMs = 170;
        bool ready;

        public OverviewForm()
        {
            var scr = Screen.FromPoint(Cursor.Position).Bounds;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true; KeyPreview = true;
            DoubleBuffered = true; Bounds = scr; BackColor = Color.Black;
            try
            {
                // Blurred, darkened picture of the screen as the background: one scaled-down copy straight from the screen
                // (no full-size capture), then stretched back up.
                int sw = Math.Max(1, scr.Width / 16), sh = Math.Max(1, scr.Height / 16);
                using (var small = new Bitmap(sw, sh, PixelFormat.Format32bppRgb))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        IntPtr dst = g.GetHdc(), screen = N.GetDC(IntPtr.Zero);
                        try { N.SetStretchBltMode(dst, 4 /*HALFTONE*/); N.StretchBlt(dst, 0, 0, sw, sh, screen, scr.X, scr.Y, scr.Width, scr.Height, 0x00CC0020); }
                        finally { N.ReleaseDC(IntPtr.Zero, screen); g.ReleaseHdc(dst); }
                    }
                    {
                        backdrop = new Bitmap(scr.Width, scr.Height, PixelFormat.Format32bppRgb);
                        using (var g = Graphics.FromImage(backdrop))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.PixelOffsetMode = PixelOffsetMode.Half;
                            g.DrawImage(small, new Rectangle(0, 0, scr.Width, scr.Height));
                            using (var br = new SolidBrush(Color.FromArgb(Ui.Dark ? 150 : 110, 10, 12, 18))) g.FillRectangle(br, 0, 0, scr.Width, scr.Height);
                        }
                    }
                }
            }
            catch { backdrop = null; }
            anim.Tick += delegate { StepAnim(); };
        }

        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            foreach (var w in Wins.List())
            {
                IntPtr th;
                if (N.DwmRegisterThumbnail(Handle, w.Hwnd, out th) != 0) continue;
                N.SIZE sz; N.DwmQueryThumbnailSourceSize(th, out sz);
                if (sz.cx <= 0 || sz.cy <= 0) { N.DwmUnregisterThumbnail(th); continue; }
                var b = Wins.Bounds(w.Hwnd);
                tiles.Add(new Tile { Win = w, Thumb = th, Src = new Size(sz.cx, sz.cy), From = w.Minimized ? Rectangle.Empty : new Rectangle(b.X - Left, b.Y - Top, b.Width, b.Height) });
            }
            LayoutTiles();
            animStart = Environment.TickCount;
            if (Overlay.Animate) { StepAnim(); anim.Start(); } else { foreach (var t in tiles) Place(t, t.Dest); ready = true; }
            Invalidate();
        }

        void LayoutTiles()
        {
            int n = tiles.Count;
            if (n == 0) return;
            var area = new Rectangle(Ui.S(60), Ui.S(70), Width - Ui.S(120), Height - Ui.S(140));
            int gap = Ui.S(28), label = Ui.S(30);
            int bestCols = 1; double best = 0;
            for (int c = 1; c <= n; c++)
            {
                int r = (n + c - 1) / c;
                double cw = (area.Width - (c - 1) * gap) / (double)c, ch = (area.Height - (r - 1) * gap) / (double)r - label;
                double s = Math.Min(cw / 16.0, ch / 10.0);
                if (s > best) { best = s; bestCols = c; }
            }
            int cols = bestCols, rows = (n + cols - 1) / cols;
            int cellW = (area.Width - (cols - 1) * gap) / cols, cellH = (area.Height - (rows - 1) * gap) / rows;
            for (int i = 0; i < n; i++)
            {
                int row = i / cols, col = i % cols;
                int inRow = Math.Min(cols, n - row * cols);
                int rowLeft = area.X + (area.Width - (inRow * cellW + (inRow - 1) * gap)) / 2;
                var cell = new Rectangle(rowLeft + col * (cellW + gap), area.Y + row * (cellH + gap), cellW, cellH - label);
                var t = tiles[i];
                double k = Math.Min(1.0, Math.Min(cell.Width / (double)t.Src.Width, cell.Height / (double)t.Src.Height));
                int w = (int)(t.Src.Width * k), h = (int)(t.Src.Height * k);
                t.Dest = new Rectangle(cell.X + (cell.Width - w) / 2, cell.Y + (cell.Height - h) / 2, w, h);
                if (t.From.IsEmpty) { var c = new Point(t.Dest.X + t.Dest.Width / 2, t.Dest.Y + t.Dest.Height / 2); t.From = new Rectangle(c.X - w / 4, c.Y - h / 4, w / 2, h / 2); }
            }
        }

        void StepAnim()
        {
            double p = Math.Min(1.0, (Environment.TickCount - animStart) / (double)AnimMs);
            double e = 1 - Math.Pow(1 - p, 3);
            foreach (var t in tiles)
            {
                var r = new Rectangle((int)(t.From.X + (t.Dest.X - t.From.X) * e), (int)(t.From.Y + (t.Dest.Y - t.From.Y) * e),
                    (int)(t.From.Width + (t.Dest.Width - t.From.Width) * e), (int)(t.From.Height + (t.Dest.Height - t.From.Height) * e));
                Place(t, r);
            }
            if (p >= 1) { anim.Stop(); ready = true; Invalidate(); }
        }

        void Place(Tile t, Rectangle r)
        {
            var props = new N.DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = 0x1 | 0x4 | 0x8 | 0x10,
                rcDestination = new N.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom },
                opacity = 255, fVisible = true, fSourceClientAreaOnly = false
            };
            N.DwmUpdateThumbnailProperties(t.Thumb, ref props);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (backdrop != null) e.Graphics.DrawImageUnscaled(backdrop, 0, 0); else e.Graphics.Clear(Color.FromArgb(24, 26, 32));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (tiles.Count == 0)
            {
                TextRenderer.DrawText(g, "No open windows", Ui.Big, ClientRectangle, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            TextRenderer.DrawText(g, "Click a window to open it  -  middle-click closes it  -  Esc to go back", Ui.Small, new Rectangle(0, Ui.S(24), Width, Ui.S(24)), Color.FromArgb(200, 255, 255, 255), TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            if (!ready) return;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                bool on = i == hover || i == sel;
                if (on)
                {
                    var hr = Rectangle.Inflate(t.Dest, Ui.S(6), Ui.S(6));
                    using (var path = Ui.Round(hr, Ui.S(10))) using (var pen = new Pen(Ui.Mix(Ui.AccentFill, Color.White, 0.35), Ui.S(3))) g.DrawPath(pen, path);
                }
                var icon = Wins.Icon(t.Win.Path, 32);
                int iconSize = Ui.S(18);
                string title = t.Win.Title;
                var lr = new Rectangle(t.Dest.X, t.Dest.Bottom + Ui.S(10), t.Dest.Width, Ui.S(24));
                var ts = TextRenderer.MeasureText(title, Ui.Base, new Size(Math.Max(10, lr.Width - iconSize - Ui.S(24)), lr.Height), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                int tw = Math.Min(lr.Width - iconSize - Ui.S(24), ts.Width);
                var pill = new Rectangle(lr.X + (lr.Width - (tw + iconSize + Ui.S(20))) / 2, lr.Y, tw + iconSize + Ui.S(20), lr.Height);
                using (var path = Ui.Round(pill, Ui.S(8))) using (var br = new SolidBrush(on ? Color.FromArgb(200, Ui.AccentFill) : Color.FromArgb(120, 0, 0, 0))) g.FillPath(br, path);
                g.DrawImage(icon, new Rectangle(pill.X + Ui.S(8), pill.Y + (pill.Height - iconSize) / 2, iconSize, iconSize));
                TextRenderer.DrawText(g, title, Ui.Base, new Rectangle(pill.X + iconSize + Ui.S(14), pill.Y, tw + 2, pill.Height), Color.White, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        int TileAt(Point p) { for (int i = 0; i < tiles.Count; i++) if (Rectangle.Inflate(tiles[i].Dest, Ui.S(6), Ui.S(30)).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = TileAt(e.Location);
            if (h == hover) return;
            InvalidateTile(hover); hover = h; InvalidateTile(hover);   // repaint only the two tiles, not the whole screen
        }

        void InvalidateTile(int i)
        {
            if (i < 0 || i >= tiles.Count) return;
            var r = tiles[i].Dest;
            Invalidate(Rectangle.FromLTRB(r.Left - Ui.S(12), r.Top - Ui.S(12), r.Right + Ui.S(12), r.Bottom + Ui.S(44)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = TileAt(e.Location);
            if (e.Button == MouseButtons.Middle && i >= 0) { CloseTile(i); return; }
            if (e.Button != MouseButtons.Left) return;
            if (i >= 0) Open(i); else Close();
        }

        void CloseTile(int i)
        {
            var t = tiles[i];
            Wins.Close(t.Win.Hwnd);
            N.DwmUnregisterThumbnail(t.Thumb);
            tiles.RemoveAt(i);
            hover = sel = -1;
            LayoutTiles();
            foreach (var x in tiles) Place(x, x.Dest);
            Invalidate();
        }

        void Open(int i)
        {
            var h = tiles[i].Win.Hwnd;
            Close();
            Wins.Activate(h);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { Close(); return; }
            if (tiles.Count == 0) return;
            if (e.KeyCode == Keys.Enter && sel >= 0) { Open(sel); return; }
            int d = e.KeyCode == Keys.Right || e.KeyCode == Keys.Tab ? 1 : e.KeyCode == Keys.Left ? -1 : 0;
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                // Next tile below / above: nearest centre in that direction.
                var cur = sel >= 0 ? tiles[sel].Dest : tiles[0].Dest;
                int best = -1; double bd = double.MaxValue;
                for (int i = 0; i < tiles.Count; i++)
                {
                    var r = tiles[i].Dest;
                    if (e.KeyCode == Keys.Down ? r.Top <= cur.Top : r.Top >= cur.Top) continue;
                    double dist = Math.Abs(r.X + r.Width / 2 - cur.X - cur.Width / 2) + Math.Abs(r.Y - cur.Y) * 2;
                    if (dist < bd) { bd = dist; best = i; }
                }
                if (best >= 0) { sel = best; Invalidate(); }
                return;
            }
            if (d != 0) { sel = sel < 0 ? 0 : (sel + d + tiles.Count) % tiles.Count; Invalidate(); e.Handled = true; }
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Tab || keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }

        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (ready && !IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed) Close(); })); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            anim.Stop(); anim.Dispose();
            foreach (var t in tiles) N.DwmUnregisterThumbnail(t.Thumb);
            tiles.Clear();
            if (backdrop != null) backdrop.Dispose();
            base.OnFormClosed(e);
        }
    }

    // ---------- Native ----------
    static class N
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
        [DllImport("gdi32.dll")] public static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("shell32.dll")] public static extern uint SHAppBarMessage(uint msg, ref APPBARDATA data);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUnregisterThumbnail(IntPtr thumb);
        [DllImport("dwmapi.dll")] public static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES p);
        [DllImport("dwmapi.dll")] public static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, ref DIBSECTION ds);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [StructLayout(LayoutKind.Sequential)]
        public struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags; public RECT rcDestination; public RECT rcSource; public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible; [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
        [StructLayout(LayoutKind.Sequential)] public struct DIBSECTION { public BITMAP dsBm; public BITMAPINFOHEADER dsBmih; public uint b0, b1, b2; public IntPtr dshSection; public uint dsOffset; }
        [StructLayout(LayoutKind.Sequential)]
        public class MEMSTATUS
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMSTATUS)); public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
    }
}
