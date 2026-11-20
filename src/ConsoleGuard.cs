using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace StickyPro
{
    // Agent CLIs (Codex, Claude Code, Cline, ...) start their own helpers: shells, git, node, build tools. Some of them
    // open a console window of their own, which pops up over your work. While an agent runs, this watcher hides every
    // classic console window whose process descends from that agent. Your own terminals are never touched: only
    // windows of processes started by a registered agent are hidden.
    static class ConsoleGuard
    {
        static readonly object gate = new object();
        static readonly HashSet<int> roots = new HashSet<int>();
        static Thread worker;

        public static void Watch(Process p)
        {
            if (p == null) return;
            int id; try { id = p.Id; } catch { return; }
            lock (gate)
            {
                roots.Add(id);
                if (worker == null || !worker.IsAlive) { worker = new Thread(Loop) { IsBackground = true, Name = "console guard", Priority = ThreadPriority.BelowNormal }; worker.Start(); }
            }
            try { p.EnableRaisingEvents = true; p.Exited += delegate { lock (gate) roots.Remove(id); }; } catch { }
        }

        static void Loop()
        {
            while (true)
            {
                int[] watch;
                lock (gate) { watch = new int[roots.Count]; roots.CopyTo(watch); if (watch.Length == 0) { worker = null; return; } }
                try { Sweep(new HashSet<int>(watch)); } catch (Exception ex) { App.Log("Console guard: " + ex.Message); }
                Thread.Sleep(150);
            }
        }

        static void Sweep(HashSet<int> agentRoots)
        {
            Dictionary<int, int> parents = null;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                var cls = new StringBuilder(64); GetClassName(h, cls, 64);
                if (cls.ToString() != "ConsoleWindowClass") return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                if (parents == null) parents = ParentMap();
                if (Descends((int)pid, agentRoots, parents)) ShowWindow(h, 0 /* SW_HIDE */);
                return true;
            }, IntPtr.Zero);
        }

        static bool Descends(int pid, HashSet<int> agentRoots, Dictionary<int, int> parents)
        {
            for (int depth = 0; depth < 16 && pid > 4; depth++)
            {
                if (agentRoots.Contains(pid)) return true;
                int parent; if (!parents.TryGetValue(pid, out parent) || parent == pid) return false;
                pid = parent;
            }
            return false;
        }

        // pid -> parent pid for every running process (one fast snapshot).
        static Dictionary<int, int> ParentMap()
        {
            var map = new Dictionary<int, int>();
            IntPtr snap = CreateToolhelp32Snapshot(2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return map;
            try
            {
                var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (Process32First(snap, ref e)) do { map[(int)e.th32ProcessID] = (int)e.th32ParentProcessID; } while (Process32Next(snap, ref e));
            }
            finally { CloseHandle(snap); }
            return map;
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
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
