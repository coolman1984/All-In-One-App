using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // File and folder search for "Search everything":
    //  1. Everything (voidtools) when it runs: asked through its own message interface (the one its SDK uses), so it
    //     covers every disk instantly.
    //  2. Else the Windows Search index (the folders Windows indexes), through ADO, no extra files.
    // ===================================================================================================

    class FileHit { public string Name = "", Folder = "", Path = ""; public bool IsFolder; }

    static class FileSearch
    {
        public static string Source = "";

        public static List<FileHit> Find(string query, int max)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2) return new List<FileHit>();
            try { var r = Everything.Query(query, max); if (r != null) { Source = "Everything"; return r; } } catch (Exception ex) { App.Log("Everything search: " + ex.Message); }
            try { Source = "Windows Search"; return WindowsIndex(query, max); } catch (Exception ex) { App.Log("Windows Search: " + ex.Message); Source = ""; return new List<FileHit>(); }
        }

        // Windows Search through ADODB (late bound: no reference needed).
        static List<FileHit> WindowsIndex(string query, int max)
        {
            var list = new List<FileHit>();
            var connType = Type.GetTypeFromProgID("ADODB.Connection"); var rsType = Type.GetTypeFromProgID("ADODB.Recordset");
            if (connType == null || rsType == null) return list;
            object conn = Activator.CreateInstance(connType), rs = null;
            try
            {
                connType.InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, conn, new object[] { "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';", "", "", -1 });
                string q = query.Replace("'", "''");
                string sql = "SELECT TOP " + max + " System.ItemPathDisplay, System.ItemNameDisplay, System.ItemType FROM SystemIndex WHERE System.FileName LIKE '%" + q + "%' ORDER BY System.DateModified DESC";
                rs = Activator.CreateInstance(rsType);
                rsType.InvokeMember("Open", System.Reflection.BindingFlags.InvokeMethod, null, rs, new object[] { sql, conn, 0, 1, 1 });
                while (!(bool)rsType.InvokeMember("EOF", System.Reflection.BindingFlags.GetProperty, null, rs, null) && list.Count < max)
                {
                    var fields = rsType.InvokeMember("Fields", System.Reflection.BindingFlags.GetProperty, null, rs, null);
                    Func<int, string> get = i => { var f = fields.GetType().InvokeMember("Item", System.Reflection.BindingFlags.GetProperty, null, fields, new object[] { i }); var v = f.GetType().InvokeMember("Value", System.Reflection.BindingFlags.GetProperty, null, f, null); return v == null || v is DBNull ? "" : Convert.ToString(v); };
                    string path = get(0), name = get(1), type = get(2);
                    if (path.Length > 0) list.Add(new FileHit { Path = path, Name = name.Length > 0 ? name : System.IO.Path.GetFileName(path), Folder = System.IO.Path.GetDirectoryName(path) ?? "", IsFolder = type.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0 || Directory.Exists(path) });
                    rsType.InvokeMember("MoveNext", System.Reflection.BindingFlags.InvokeMethod, null, rs, null);
                }
            }
            finally
            {
                try { if (rs != null) { rsType.InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, rs, null); Marshal.ReleaseComObject(rs); } } catch { }
                try { connType.InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, conn, null); Marshal.ReleaseComObject(conn); } catch { }
            }
            return list;
        }
    }

    // Everything's IPC: a WM_COPYDATA query to its hidden window; the answer comes back as WM_COPYDATA to our window.
    static class Everything
    {
        const int WM_COPYDATA = 0x4A, QUERYW = 2, REPLY = 0x4F4C4C41;   // any id we choose for the reply
        [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, ref COPYDATASTRUCT l, int flags, int timeout, out IntPtr result);

        class Receiver : NativeWindow
        {
            public List<FileHit> Result;
            public Receiver() { CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }   // message-only window
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_COPYDATA)
                {
                    var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
                    if (cds.dwData.ToInt64() == REPLY) { Result = Parse(cds.lpData); m.Result = new IntPtr(1); return; }
                }
                base.WndProc(ref m);
            }
        }

        public static bool Running { get { return FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null) != IntPtr.Zero; } }

        // Null when Everything does not run (or does not answer).
        public static List<FileHit> Query(string search, int max)
        {
            IntPtr ev = FindWindow("EVERYTHING_TASKBAR_NOTIFICATION", null);
            if (ev == IntPtr.Zero) return null;
            var rx = new Receiver();
            try
            {
                // EVERYTHING_IPC_QUERYW: reply_hwnd, reply_copydata_message, search_flags, offset, max_results, search_string (WCHAR, 0-terminated)
                byte[] text = System.Text.Encoding.Unicode.GetBytes(search + "\0");
                int size = 20 + text.Length;
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, 0, (int)rx.Handle.ToInt64());
                    Marshal.WriteInt32(buf, 4, REPLY);
                    Marshal.WriteInt32(buf, 8, 0);
                    Marshal.WriteInt32(buf, 12, 0);
                    Marshal.WriteInt32(buf, 16, max);
                    Marshal.Copy(text, 0, buf + 20, text.Length);
                    var cds = new COPYDATASTRUCT { dwData = new IntPtr(QUERYW), cbData = size, lpData = buf };
                    IntPtr res;
                    if (SendMessageTimeout(ev, WM_COPYDATA, rx.Handle, ref cds, 0x2 /*ABORTIFHUNG*/, 3000, out res) == IntPtr.Zero || res == IntPtr.Zero) return null;
                }
                finally { Marshal.FreeHGlobal(buf); }
                // The answer is sent back while we wait, or right after: pump a little.
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (rx.Result == null && sw.ElapsedMilliseconds < 3000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                return rx.Result;
            }
            finally { rx.DestroyHandle(); }
        }

        // EVERYTHING_IPC_LISTW: totfolders, totfiles, totitems, numfolders, numfiles, numitems, offset, then items
        // { flags, filename_offset, path_offset } with offsets from the start of the list.
        static List<FileHit> Parse(IntPtr list)
        {
            var hits = new List<FileHit>();
            int num = Marshal.ReadInt32(list, 20);
            for (int i = 0; i < num && i < 5000; i++)
            {
                IntPtr item = list + 28 + i * 12;
                int flags = Marshal.ReadInt32(item, 0), nameOff = Marshal.ReadInt32(item, 4), pathOff = Marshal.ReadInt32(item, 8);
                string name = Marshal.PtrToStringUni(list + nameOff) ?? "", folder = Marshal.PtrToStringUni(list + pathOff) ?? "";
                hits.Add(new FileHit { Name = name, Folder = folder, Path = folder.Length > 0 ? System.IO.Path.Combine(folder, name) : name, IsFolder = (flags & 1) != 0 || (flags & 2) != 0 });
            }
            return hits;
        }
    }
}
