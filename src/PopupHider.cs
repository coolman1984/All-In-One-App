using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace StickyPro
{
    // Hides the company security agent's "Security Warning" pop-ups (ICTray / ESCORT) the moment they appear.
    // It only hides the window: the agent and its policy are not touched, and each warning's text is kept in
    // <data>\security-warnings.log so nothing is lost.
    static class PopupHider
    {
        static IntPtr hook;
        static readonly WinEventProc proc = OnShow;   // kept alive: native code calls it
        static Timer sweep;
        static readonly Dictionary<int, bool> agentPid = new Dictionary<int, bool>();
        static readonly HashSet<IntPtr> logged = new HashSet<IntPtr>();
        static readonly Dictionary<IntPtr, string> lastText = new Dictionary<IntPtr, string>();
        public static int HiddenCount;

        public static void Apply()
        {
            bool on = Store.Settings.HideSecurityPopups;
            if (on && hook == IntPtr.Zero)
            {
                hook = SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, proc, 0, 0, 0);   // EVENT_OBJECT_SHOW, out of context
                sweep = new Timer { Interval = 1500 };   // backup for anything the event missed
                sweep.Tick += delegate { Sweep(); };
                sweep.Start();
                Sweep();
            }
            else if (!on && hook != IntPtr.Zero)
            {
                UnhookWinEvent(hook); hook = IntPtr.Zero;
                if (sweep != null) { sweep.Stop(); sweep.Dispose(); sweep = null; }
            }
        }

        public static void Stop() { if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; } }

        static void OnShow(IntPtr h, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 || child != 0 || hwnd == IntPtr.Zero) return;   // windows only, not their parts
            try { Check(hwnd); } catch { }
        }

        static void Sweep()
        {
            try
            {
                IntPtr w = IntPtr.Zero;
                while ((w = FindWindowEx(IntPtr.Zero, w, "#32770", "Security Warning")) != IntPtr.Zero) Check(w);
            }
            catch (Exception ex) { App.Log("Popup hider: " + ex.Message); }
        }

        static void Check(IntPtr hwnd)
        {
            var cls = new StringBuilder(16);
            if (GetClassName(hwnd, cls, 16) == 0 || cls.ToString() != "#32770") return;
            var title = new StringBuilder(64);
            GetWindowText(hwnd, title, 64);
            if (title.ToString() != "Security Warning") return;
            uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (!IsAgent((int)pid)) return;
            string text = DialogText(hwnd);
            string seen;
            if (text.Length > 0 && (!lastText.TryGetValue(hwnd, out seen) || seen != text))
            {
                lastText[hwnd] = text;
                try { File.AppendAllText(Path.Combine(Store.Dir, "security-warnings.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine); } catch { }
            }
            if (IsWindowVisible(hwnd)) { ShowWindow(hwnd, 0); HiddenCount++; }
            if (lastText.Count > 200) lastText.Clear();
        }

        // ICTray / ICTray64 (ESCORT PC security agent). Its process cannot be opened, so a snapshot is used.
        static bool IsAgent(int pid)
        {
            bool yes;
            if (agentPid.TryGetValue(pid, out yes)) return yes;
            yes = false;
            IntPtr snap = CreateToolhelp32Snapshot(2, 0);
            if (snap != new IntPtr(-1))
            {
                try
                {
                    // The agent hides its process from process lists (only WMI shows it): a "Security Warning" dialog
                    // whose process is missing from the snapshot is the agent's. Normal programs are always listed.
                    bool listed = false;
                    var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                    for (bool ok = Process32First(snap, ref e); ok; ok = Process32Next(snap, ref e))
                        if (e.th32ProcessID == pid) { listed = true; yes = e.szExeFile.StartsWith("ICTray", StringComparison.OrdinalIgnoreCase); break; }
                    if (!listed) yes = true;
                }
                finally { CloseHandle(snap); }
            }
            if (agentPid.Count > 100) agentPid.Clear();
            agentPid[pid] = yes;
            return yes;
        }

        static string DialogText(IntPtr hwnd)
        {
            var parts = new List<string>();
            EnumChildWindows(hwnd, (c, l) =>
            {
                int n = GetWindowTextLength(c);
                if (n > 0 && n < 4000) { var sb = new StringBuilder(n + 1); GetWindowText(c, sb, sb.Capacity); string s = sb.ToString().Trim(); if (s.Length > 0 && s != "OK") parts.Add(s.Replace("\r", " ").Replace("\n", " ")); }
                return true;
            }, IntPtr.Zero);
            return string.Join(" | ", parts);
        }

        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }
    }
}
