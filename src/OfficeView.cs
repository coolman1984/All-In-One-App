using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StickyPro
{
    // Read-only document view inside the app: the same preview handlers Windows Explorer uses (Excel, PowerPoint,
    // Word, Visio, PDF, ...). They run in Windows' preview host process, so Office's own code draws the file.
    class OfficeView : Panel
    {
        [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPreviewHandler
        {
            void SetWindow(IntPtr hwnd, ref RECT rect);
            void SetRect(ref RECT rect);
            void DoPreview();
            void Unload();
            void SetFocus();
            void QueryFocus(out IntPtr phwnd);
            [PreserveSig] uint TranslateAccelerator(ref Message pmsg);
        }
        [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithFile { void Initialize([MarshalAs(UnmanagedType.LPWStr)] string path, uint mode); }
        [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IInitializeWithStream { void Initialize(System.Runtime.InteropServices.ComTypes.IStream stream, uint mode); }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; public RECT(Rectangle r) { Left = r.Left; Top = r.Top; Right = r.Right; Bottom = r.Bottom; } }

        [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint ctx, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] static extern int SHCreateStreamOnFileEx(string file, uint mode, uint attrs, bool create, IntPtr template, out System.Runtime.InteropServices.ComTypes.IStream stream);

        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hgt, bool repaint);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        System.Diagnostics.Process hosted; IntPtr hostedWnd;
        public bool Hosted { get { return hostedWnd != IntPtr.Zero; } }

        // Company-protected PDFs: only the authorized reader (Foxit) can read them; its own window is placed inside this view.
        public static string Foxit
        {
            get
            {
                foreach (var p in new[] { @"C:\Program Files\Foxit Software\Foxit PDF Reader\FoxitPDFReader.exe", @"C:\Program Files (x86)\Foxit Software\Foxit PDF Reader\FoxitPDFReader.exe" })
                    if (File.Exists(p)) return p;
                return null;
            }
        }
        public bool OpenInFoxit(string path)
        {
            Close(); Path = path; Error = null;
            string exe = Foxit;
            if (exe == null) { Error = "This PDF is protected and needs Foxit PDF Reader, which is not installed."; return false; }
            if (System.Diagnostics.Process.GetProcessesByName("FoxitPDFReader").Length > 0)
            {   // Foxit is already open: it would add a tab to your window, so the file opens there instead
                System.Diagnostics.Process.Start(exe, "\"" + path + "\"");
                Error = "This PDF is protected; it opened in your Foxit PDF Reader window (close Foxit to see protected PDFs here).";
                return false;
            }
            hosted = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, "\"" + path + "\"") { WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized });
            var until = DateTime.Now.AddSeconds(20);
            while (DateTime.Now < until && !hosted.HasExited)
            {
                hosted.Refresh();
                if (hosted.MainWindowHandle != IntPtr.Zero) break;
                Application.DoEvents(); System.Threading.Thread.Sleep(100);
            }
            if (hosted.HasExited || hosted.MainWindowHandle == IntPtr.Zero) { Error = "Foxit did not start."; return false; }
            hostedWnd = hosted.MainWindowHandle;
            const int GWL_STYLE = -16, WS_CHILD = 0x40000000, WS_CAPTION = 0xC00000, WS_THICKFRAME = 0x40000, WS_POPUP = unchecked((int)0x80000000);
            SetParent(hostedWnd, Handle);
            SetWindowLong(hostedWnd, GWL_STYLE, (GetWindowLong(hostedWnd, GWL_STYLE) | WS_CHILD) & ~(WS_CAPTION | WS_THICKFRAME | WS_POPUP));
            ShowWindow(hostedWnd, 3 /* SW_MAXIMIZE inside the view */); ShowWindow(hostedWnd, 9 /* restore */);
            MoveWindow(hostedWnd, 0, 0, ClientSize.Width, ClientSize.Height, true);
            return true;
        }

        IPreviewHandler handler;
        object comObject;
        public string Path { get; private set; }
        public string Error { get; private set; }

        [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr c);
        [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr inst, int id);
        [DllImport("user32.dll")] static extern int ShowCursor(bool show);
        [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public Point pt; }
        [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
        readonly Timer cursorWatch = new Timer { Interval = 250 };

        public OfficeView()
        {
            BackColor = Color.White;
            // The previewers run in another process and do not always set or show a mouse pointer over the document:
            // while the mouse is over this view the pointer is kept visible.
            cursorWatch.Tick += delegate
            {
                if (!Visible || (handler == null && hostedWnd == IntPtr.Zero)) return;
                if (!RectangleToScreen(ClientRectangle).Contains(Cursor.Position)) return;
                var ci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
                if (GetCursorInfo(ref ci) && (ci.flags & 1) == 0) { for (int i = 0; i < 10 && ShowCursor(true) < 0; i++) { } }
                if (GetCursorInfo(ref ci) && ci.hCursor == IntPtr.Zero) SetCursor(LoadCursor(IntPtr.Zero, 32512 /* arrow */));
            };
            cursorWatch.Start();
        }

        protected override void WndProc(ref Message m)
        {
            // WM_SETCURSOR from the document window: give it the arrow; it may still set its own (text, resize) after this.
            if (m.Msg == 0x20 && m.WParam != Handle && (handler != null || hostedWnd != IntPtr.Zero))
            {
                SetCursor(LoadCursor(IntPtr.Zero, 32512));
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (handler != null) try { handler.SetFocus(); } catch (Exception) { } }

        // The preview handler registered for a file type (HKCR\.ext or its ProgID, ShellEx\{8895b1c6-...}).
        public static Guid? HandlerFor(string ext)
        {
            const string key = @"\ShellEx\{8895b1c6-b41f-4c1c-a562-0d564250836f}";
            try
            {
                using (var k = Registry.ClassesRoot.OpenSubKey(ext + key)) if (k != null && k.GetValue("") is string) return new Guid((string)k.GetValue(""));
                using (var e = Registry.ClassesRoot.OpenSubKey(ext))
                {
                    string progId = e == null ? null : e.GetValue("") as string;
                    if (!string.IsNullOrEmpty(progId)) using (var k = Registry.ClassesRoot.OpenSubKey(progId + key)) if (k != null && k.GetValue("") is string) return new Guid((string)k.GetValue(""));
                }
            }
            catch (Exception) { }
            return null;
        }

        // Office's own previewers (Click-to-Run registers them even when the file-type link points elsewhere).
        static string Known(string ext)
        {
            switch (ext)
            {
                case ".xlsx": case ".xlsm": case ".xlsb": case ".xls": case ".xltx": case ".xltm": case ".csv": return "00020827-0000-0000-C000-000000000046";
                case ".pptx": case ".pptm": case ".ppt": case ".ppsx": case ".pps": case ".potx": return "65235197-874B-4A07-BDC5-E65EA825B718";
                case ".docx": case ".docm": case ".doc": case ".dotx": case ".rtf": return "84F66100-FF7C-4fb4-B0C0-02CD7FB668FE";
                case ".vsdx": case ".vsd": case ".vsdm": return "21E17C2F-AD3A-4b89-841F-09CFE02D16B7";
                case ".pdf": return "3A84F9C2-6164-485C-A7D9-4B27F8AC009E";
            }
            return null;
        }
        public static bool CanView(string ext) { ext = (ext ?? "").ToLowerInvariant(); return Known(ext) != null || HandlerFor(ext) != null; }

        public bool Open(string path)
        {
            Close();
            Path = path; Error = null;
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var list = new System.Collections.Generic.List<Guid>();
            if (ext == ".pdf") list.Add(new Guid("FFBD7029-84D7-4E1E-BE44-B6619BC545ED"));   // Foxit first: it reads company-protected PDFs
            var reg = HandlerFor(ext); if (reg != null && !list.Contains(reg.Value)) list.Add(reg.Value);
            string known = Known(ext); if (known != null && !list.Contains(new Guid(known))) list.Add(new Guid(known));

            if (list.Count == 0) { Error = "No viewer is installed on this PC for " + ext + " files."; return false; }
            var errors = new System.Collections.Generic.List<string>();
            foreach (var g in list)
            {
                string err = TryOne(g, path);
                if (err == null) return true;
                errors.Add(err);
            }
            Error = errors.Count > 0 ? errors[errors.Count - 1] : "No viewer could open this file.";
            return false;
        }

        // One previewer: create it (out of process like Explorer, then in-process), give it the file, show it here.
        string TryOne(Guid clsid, string path)
        {
            try
            {
                Guid c = clsid, unk = new Guid("00000000-0000-0000-C000-000000000046");
                int hr = CoCreateInstance(ref c, IntPtr.Zero, 0x4, ref unk, out comObject);
                if (hr != 0) hr = CoCreateInstance(ref c, IntPtr.Zero, 0x1, ref unk, out comObject);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                var withFile = comObject as IInitializeWithFile;
                if (withFile != null) withFile.Initialize(path, 0 /* STGM_READ */);
                else
                {
                    var withStream = comObject as IInitializeWithStream;
                    if (withStream == null) throw new InvalidOperationException("This viewer can not open files.");
                    System.Runtime.InteropServices.ComTypes.IStream s;
                    Marshal.ThrowExceptionForHR(SHCreateStreamOnFileEx(path, 0x20 /* STGM_READ | SHARE_DENY_WRITE */, 0, false, IntPtr.Zero, out s));
                    withStream.Initialize(s, 0);
                }
                handler = (IPreviewHandler)comObject;
                var r = new RECT(ClientRectangle);
                handler.SetWindow(Handle, ref r);
                handler.DoPreview();
                return null;
            }
            catch (Exception ex) { Close(); return ex.Message; }
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (handler != null) try { var r = new RECT(ClientRectangle); handler.SetRect(ref r); } catch (Exception) { }
            if (hostedWnd != IntPtr.Zero) MoveWindow(hostedWnd, 0, 0, ClientSize.Width, ClientSize.Height, true);
        }

        public void Close()
        {
            if (hosted != null)
            {   // the Foxit window this view started (never one you opened yourself)
                try { if (!hosted.HasExited) { hosted.CloseMainWindow(); if (!hosted.WaitForExit(3000)) hosted.Kill(); } } catch (Exception) { }
                hosted = null; hostedWnd = IntPtr.Zero;
            }
            if (handler != null) try { handler.Unload(); } catch (Exception) { }
            handler = null;
            if (comObject != null) try { Marshal.FinalReleaseComObject(comObject); } catch (Exception) { }
            comObject = null;
        }

        protected override void Dispose(bool disposing) { if (disposing) { cursorWatch.Dispose(); Close(); } base.Dispose(disposing); }
    }
}
