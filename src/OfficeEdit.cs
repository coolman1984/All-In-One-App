using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace StickyPro
{
    // Edit mode of the Documents page: the real Excel, Word or PowerPoint window lives inside the tab.
    // The file is opened through the application's COM automation (works for company-protected files), with the full
    // ribbon, Ctrl+S, formulas, everything. Excel and Word get their own process per tab (quit when the tab closes);
    // PowerPoint is one shared app: only this presentation is opened and closed, your other presentations stay as they are.
    class OfficeEdit : Panel
    {
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

        public string Path { get; private set; }
        public string Error { get; private set; }
        public string Kind { get; private set; }   // excel / word / powerpoint
        object app, doc;
        internal object ComApp { get { return app; } }
        internal object ComDoc { get { return doc; } }
        IntPtr wnd;
        bool startedPowerPoint;
        readonly Timer watch = new Timer { Interval = 1000 };
        readonly Timer follow = new Timer { Interval = 150 };
        Rectangle placed;
        bool shown;
        IntPtr lastForeground;
        int lastRaiseTick;
        public event Action Lost;   // the Office window went away (closed from its own menu)

        public OfficeEdit()
        {
            BackColor = Color.White;
            watch.Tick += delegate { if (wnd != IntPtr.Zero && !IsWindow(wnd)) { wnd = IntPtr.Zero; watch.Stop(); Release(); if (Lost != null) Lost(); } };
            follow.Tick += delegate { UpdatePlacement(); };
        }

        public static string KindOf(string path)
        {
            switch (System.IO.Path.GetExtension(path ?? "").ToLowerInvariant())
            {
                case ".xlsx": case ".xlsm": case ".xlsb": case ".xls": case ".csv": return "excel";
                case ".docx": case ".docm": case ".doc": case ".rtf": return "word";
                case ".pptx": case ".pptm": case ".ppt": case ".ppsx": return "powerpoint";
            }
            return null;
        }

        public bool Open(string path)
        {
            Path = path; Error = null; Kind = KindOf(path);
            if (Kind == null) { Error = "Editing here works for Excel, Word and PowerPoint files."; return false; }
            if (OpenElsewhere(path)) { Error = "the file is already open in " + Kind + " (or by someone else). Close it there to edit it here, or use Open in its app."; return false; }
            try
            {
                if (Kind == "excel")
                {
                    app = Create("Excel.Application");
                    doc = Call(Get(app, "Workbooks"), "Open", path);
                    wnd = new IntPtr(Convert.ToInt64(Get(app, "Hwnd")));
                }
                else if (Kind == "word")
                {
                    app = Create("Word.Application");
                    string tag = "AIO-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    Set(app, "Caption", tag);
                    doc = Call(Get(app, "Documents"), "Open", path);
                    Set(app, "Visible", true);
                    wnd = Find("OpusApp", tag, 0);
                }
                else
                {
                    startedPowerPoint = Process.GetProcessesByName("POWERPNT").Length == 0;
                    app = Create("PowerPoint.Application");
                    // Open(FileName, ReadOnly, Untitled, WithWindow)
                    doc = Call(Get(app, "Presentations"), "Open", path, 0, 0, -1);
                    var p = Process.GetProcessesByName("POWERPNT").FirstOrDefault();
                    wnd = Find("PPTFrameClass", System.IO.Path.GetFileNameWithoutExtension(path), p == null ? 0 : (uint)p.Id);
                }
                if (wnd == IntPtr.Zero) throw new InvalidOperationException("The " + Kind + " window was not found.");
                if (Kind == "excel") { Set(app, "Visible", true); Set(app, "UserControl", true); }
                Host();
                watch.Start();
                return true;
            }
            catch (Exception ex)
            {
                Error = (ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                Close(false);
                return false;
            }
        }

        // Owner or parent HWNDs from another thread join its input queue to ours.
        // Keep Office top-level and place it over the pane without changing focus.
        const uint NoActivate = 0x0010, AsyncPosition = 0x4000, NoZOrder = 0x0004;
        const uint NoSize = 0x0001, NoMove = 0x0002, FrameChanged = 0x0020, ShowWindowFlag = 0x0040, HideWindowFlag = 0x0080;
        const uint WindowAbove = 3, WindowOwner = 4;

        void Host()
        {
            const int GWL_STYLE = -16, WS_CHILD = 0x40000000, WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000, WS_POPUP = unchecked((int)0x80000000), WS_SYSMENU = 0x80000, WS_MINIMIZEBOX = 0x20000, WS_MAXIMIZEBOX = 0x10000;
            const int GWL_EXSTYLE = -20, WS_EX_APPWINDOW = 0x40000, WS_EX_TOOLWINDOW = 0x80;
            if (IsZoomed(wnd) || IsIconic(wnd)) ShowWindowAsync(wnd, 9 /* restore */);
            SetWindowLong(wnd, GWL_STYLE, (GetWindowLong(wnd, GWL_STYLE) | WS_POPUP) & ~(WS_CHILD | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX));
            SetWindowLong(wnd, GWL_EXSTYLE, (GetWindowLong(wnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
            placed = Rectangle.Empty;
            shown = IsWindowVisible(wnd);
            lastForeground = IntPtr.Zero;
            lastRaiseTick = Environment.TickCount;
            UpdatePlacement();
        }

        void UpdatePlacement()
        {
            if (wnd == IntPtr.Zero || IsDisposed) return;
            if (Visible) follow.Start(); else follow.Stop();
            Place(Native.GetForegroundWindow());
        }

        void Place(IntPtr foreground)
        {
            if (wnd == IntPtr.Zero || IsDisposed) return;
            var form = FindForm();
            bool hostActive = form != null && form.IsHandleCreated && foreground == form.Handle;
            bool foregroundChanged = foreground != lastForeground;
            lastForeground = foreground;
            bool onScreen = Visible && form != null && form.WindowState != FormWindowState.Minimized &&
                ClientSize.Width > 20 && ClientSize.Height > 20;
            if (!onScreen || (!hostActive && !EditorHasForeground(foreground)))
            {
                HideEditor();
                return;
            }

            Rectangle bounds = RectangleToScreen(ClientRectangle);
            // The window manager can finish raising the host after its focus change was observed.
            // Check the actual order again, but throttle requests if the editor is busy.
            int tick = Environment.TickCount;
            bool canRaise = foregroundChanged || unchecked(tick - lastRaiseTick) >= 500;
            bool raise = hostActive && (!shown || (canRaise && IsAbove(form.Handle, wnd)));
            if (shown && bounds == placed && !raise) return;

            // Place the editor just above its host. Leave unrelated windows and Office dialogs in their current order.
            IntPtr after = IntPtr.Zero;
            if (raise)
            {
                after = GetWindow(form.Handle, WindowAbove);
                if (after == wnd || (after != IntPtr.Zero && (GetWindowLong(after, -20) & 0x8) != 0)) after = IntPtr.Zero;
            }
            uint flags = NoActivate | AsyncPosition | ShowWindowFlag;
            if (!raise) flags |= NoZOrder;
            if (placed.IsEmpty) flags |= FrameChanged;
            if (SetWindowPos(wnd, after, bounds.X, bounds.Y, bounds.Width, bounds.Height, flags))
            {
                if (raise) lastRaiseTick = tick;
                placed = bounds;
                shown = true;
            }
        }

        bool EditorHasForeground(IntPtr foreground)
        {
            for (IntPtr window = foreground; window != IntPtr.Zero; window = GetWindow(window, WindowOwner))
                if (window == wnd) return true;
            return false;
        }

        static bool IsAbove(IntPtr window, IntPtr other)
        {
            for (IntPtr above = GetWindow(other, WindowAbove); above != IntPtr.Zero; above = GetWindow(above, WindowAbove))
                if (above == window) return true;
            return false;
        }

        void HideEditor()
        {
            if (!shown) return;
            SetWindowPos(wnd, IntPtr.Zero, 0, 0, 0, 0, NoActivate | AsyncPosition | NoZOrder | NoSize | NoMove | HideWindowFlag);
            shown = false;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdatePlacement(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); UpdatePlacement(); }

        // Office keeps a "~$name" lock file next to a file it has open.
        public static bool OpenElsewhere(string path)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(path), name = System.IO.Path.GetFileName(path);
                // Excel: ~$ + full name; Word / PowerPoint: ~$ + the name without its first two letters (long names)
                if (File.Exists(System.IO.Path.Combine(dir, "~$" + name))) return true;
                if (name.Length > 2 && File.Exists(System.IO.Path.Combine(dir, "~$" + name.Substring(2)))) return true;
                if (name.Length > 1 && File.Exists(System.IO.Path.Combine(dir, "~$" + name.Substring(1)))) return true;
            }
            catch (Exception) { }
            try { using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { } return false; } catch (IOException) { return true; } catch (UnauthorizedAccessException) { return false; }
        }
        public bool Dirty
        {
            get
            {
                if (doc == null) return false;
                try { return !Convert.ToBoolean(Get(doc, "Saved")); }
                catch (Exception ex) { App.Log("Could not read Office save state: " + ex.Message); return true; }
            }
        }

        public bool Save()
        {
            if (doc == null) return false;
            try
            {
                Call(doc, "Save");
                if (Dirty) throw new IOException("Office still reports unsaved changes.");
                Error = null;
                if (App.I != null) App.I.Status(System.IO.Path.GetFileName(Path) + " saved.");
                return true;
            }
            catch (Exception ex)
            {
                Error = "Could not save: " + ex.GetBaseException().Message;
                if (App.I != null) App.I.Status(Error);
                return false;
            }
        }

        public bool ConfirmClose()
        {
            return ConfirmClose(() => MessageBox.Show(FindForm(), "Save changes to " + System.IO.Path.GetFileName(Path) + "?", "Documents",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3));
        }

        bool ConfirmClose(Func<DialogResult> ask)
        {
            if (!Dirty) return true;
            var choice = ask();
            return choice == DialogResult.No || (choice == DialogResult.Yes && Save());
        }

        public bool Close(bool ask)
        {
            if (ask && !ConfirmClose()) return false;
            watch.Stop(); follow.Stop();
            if (wnd != IntPtr.Zero && IsWindow(wnd)) HideEditor();
            try { if (doc != null) { if (Kind == "excel") Call(doc, "Close", false); else if (Kind == "word") Call(doc, "Close", 0); else { Set(doc, "Saved", -1); Call(doc, "Close"); } } } catch (Exception) { }
            if (doc != null && Marshal.IsComObject(doc)) Marshal.FinalReleaseComObject(doc);
            doc = null;
            try
            {
                if (app != null && Kind != "powerpoint") Call(app, "Quit");
                else if (app != null && startedPowerPoint && Convert.ToInt32(Get(Get(app, "Presentations"), "Count")) == 0) Call(app, "Quit");
            }
            catch (Exception) { }
            Release();
            wnd = IntPtr.Zero;
            return true;
        }

        void Release()
        {
            if (doc != null) try { Marshal.FinalReleaseComObject(doc); } catch (Exception) { }
            if (app != null) try { Marshal.FinalReleaseComObject(app); } catch (Exception) { }
            doc = null; app = null;
        }

        static IntPtr Find(string cls, string titlePart, uint pid)
        {
            IntPtr found = IntPtr.Zero;
            for (int tries = 0; tries < 50 && found == IntPtr.Zero; tries++)
            {
                EnumWindows((h, l) =>
                {
                    var c = new StringBuilder(64); GetClassName(h, c, 64);
                    if (c.ToString() != cls) return true;
                    if (pid != 0) { uint p; GetWindowThreadProcessId(h, out p); if (p != pid) return true; }
                    var t = new StringBuilder(512); GetWindowText(h, t, 512);
                    if (t.ToString().IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
                    return true;
                }, IntPtr.Zero);
                if (found == IntPtr.Zero) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
            }
            return found;
        }

        static object Create(string progId)
        {
            var t = Type.GetTypeFromProgID(progId);
            if (t == null) throw new InvalidOperationException(progId.Split('.')[0] + " is not installed.");
            return Activator.CreateInstance(t);
        }
        static object Get(object o, string n) { return o.GetType().InvokeMember(n, BindingFlags.GetProperty, null, o, null); }
        static void Set(object o, string n, object v) { o.GetType().InvokeMember(n, BindingFlags.SetProperty, null, o, new[] { v }); }
        static object Call(object o, string n, params object[] a) { return o.GetType().InvokeMember(n, BindingFlags.InvokeMethod, null, o, a); }

        protected override void Dispose(bool disposing) { if (disposing) { if (doc != null || app != null) Close(false); watch.Dispose(); follow.Dispose(); } base.Dispose(disposing); }
    }
}
