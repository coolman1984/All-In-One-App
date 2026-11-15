using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace StickyPro
{
    // ======================= Processes =======================

    class ProcInfo
    {
        public int Pid, ParentPid, Threads, Count = 1;
        public string Name = "", Display = "", Path;
        public long Mem, CpuTime, IoBytes, Created;
        public double Cpu, Io;
        public List<int> Pids;
        public string Key { get { return Pids != null ? "g:" + Display : "p:" + Pid; } }
    }

    // One NtQuerySystemInformation call gives every process with memory, CPU time and I/O:
    // no handles, no exceptions, very cheap. Samples only while some view is visible.
    static class ProcessSampler
    {
        [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int retLen);
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [StructLayout(LayoutKind.Sequential)]
        class MEMSTATUS { public uint dwLength = 64; public uint dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMSTATUS m);

        static IntPtr buf = IntPtr.Zero;
        static int bufLen;
        static Dictionary<int, long[]> prev = new Dictionary<int, long[]>();
        static long prevStamp, prevIdle, prevKernel, prevUser;
        static readonly HashSet<object> watchers = new HashSet<object>();
        static System.Windows.Forms.Timer timer;
        static readonly object sampleLock = new object();
        static bool sampling;              // UI thread only

        class SampleResult { public List<ProcInfo> List; public double Cpu; public int MemLoad; public ulong MemTotal, MemAvail; public bool Ok; }

        public static List<ProcInfo> Last = new List<ProcInfo>();
        public static readonly Queue<double> CpuHistory = new Queue<double>();
        public static readonly Queue<double> RamHistory = new Queue<double>();
        public static void PushHistory(Queue<double> history, double value) { if (history.Count >= 60) history.Dequeue(); history.Enqueue(value); }
        public static double CpuTotal;
        public static int MemLoad;
        public static ulong MemTotal, MemAvail;
        public static event Action Updated;

        public static void Watch(object who, bool on)
        {
            if (on) watchers.Add(who); else watchers.Remove(who);
            if (timer == null)
            {
                timer = new System.Windows.Forms.Timer();
                timer.Interval = 2000;
                timer.Tick += delegate { Sample(); };
            }
            if (watchers.Count > 0 && !timer.Enabled) { Sample(); timer.Start(); }
            else if (watchers.Count == 0 && timer.Enabled) { timer.Stop(); lock (sampleLock) { prev.Clear(); prevStamp = 0; } }
        }

        public static void Sample()
        {
            if (sampling) return;              // one sample at a time
            sampling = true;
            // ProcMeta owns an ImageList: create it on the UI thread, not on the worker.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(ProcMeta).TypeHandle);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                SampleResult r = null;
                try { r = SampleCore(); } catch (Exception ex) { App.Log("Process sample failed: " + ex.Message); }
                try { App.I.Sync.BeginInvoke(new Action(() => Publish(r))); }
                catch { sampling = false; }
            });
        }

        static void Publish(SampleResult r)
        {
            sampling = false;
            if (r != null && r.Ok)
            {
                Last = r.List; CpuTotal = r.Cpu; MemLoad = r.MemLoad; MemTotal = r.MemTotal; MemAvail = r.MemAvail;
            }
            PushHistory(CpuHistory, CpuTotal); PushHistory(RamHistory, MemLoad);
            var h = Updated; if (h != null) h();
        }

        static SampleResult SampleCore()
        {
            lock (sampleLock)
            {
            var r = new SampleResult();
            r.Cpu = CpuTotal; r.MemLoad = MemLoad; r.MemTotal = MemTotal; r.MemAvail = MemAvail;
            if (IntPtr.Size != 8) return r;
            if (bufLen == 0) { bufLen = 1024 * 1024; buf = Marshal.AllocHGlobal(bufLen); }
            int ret, status;
            while ((status = NtQuerySystemInformation(5, buf, bufLen, out ret)) == unchecked((int)0xC0000004))
            {
                Marshal.FreeHGlobal(buf);
                bufLen = Math.Max(ret + 128 * 1024, bufLen * 2);
                buf = Marshal.AllocHGlobal(bufLen);
            }
            if (status != 0) return r;

            long now = Stopwatch.GetTimestamp();
            double secs = prevStamp == 0 ? 0 : (now - prevStamp) / (double)Stopwatch.Frequency;
            int cores = Environment.ProcessorCount;
            var list = new List<ProcInfo>(320);
            var next = new Dictionary<int, long[]>(320);
            int offset = 0;
            while (true)
            {
                IntPtr p = buf + offset;
                int nextOff = Marshal.ReadInt32(p, 0);
                int pid = (int)Marshal.ReadInt64(p, 80);
                if (pid != 0)
                {
                    var pi = new ProcInfo();
                    pi.Pid = pid;
                    pi.Threads = Marshal.ReadInt32(p, 4);
                    pi.Mem = Marshal.ReadInt64(p, 8);           // private working set = Task Manager "Memory"
                    pi.Created = Marshal.ReadInt64(p, 32);
                    pi.CpuTime = Marshal.ReadInt64(p, 40) + Marshal.ReadInt64(p, 48);
                    pi.ParentPid = (int)Marshal.ReadInt64(p, 88);
                    pi.IoBytes = Marshal.ReadInt64(p, 232) + Marshal.ReadInt64(p, 240);
                    int nameLen = Marshal.ReadInt16(p, 56);
                    IntPtr namePtr = Marshal.ReadIntPtr(p, 64);
                    pi.Name = namePtr == IntPtr.Zero ? "System" : Marshal.PtrToStringUni(namePtr, nameLen / 2);
                    long[] old;
                    if (secs > 0 && prev.TryGetValue(pid, out old) && old[2] == pi.Created)
                    {
                        pi.Cpu = Math.Max(0, (pi.CpuTime - old[0]) / (secs * 1e7 * cores) * 100);
                        pi.Io = Math.Max(0, (pi.IoBytes - old[1]) / secs);
                    }
                    next[pid] = new[] { pi.CpuTime, pi.IoBytes, pi.Created };
                    list.Add(pi);
                }
                if (nextOff == 0) break;
                offset += nextOff;
            }
            prev = next;
            prevStamp = now;

            long idle, kernel, user;
            if (GetSystemTimes(out idle, out kernel, out user))
            {
                long dIdle = idle - prevIdle, dTotal = (kernel - prevKernel) + (user - prevUser);
                if (prevKernel != 0 && dTotal > 0) r.Cpu = Math.Max(0, Math.Min(100, (1.0 - (double)dIdle / dTotal) * 100));
                prevIdle = idle; prevKernel = kernel; prevUser = user;
            }
            var ms = new MEMSTATUS();
            if (GlobalMemoryStatusEx(ms)) { r.MemLoad = (int)ms.dwMemoryLoad; r.MemTotal = ms.ullTotalPhys; r.MemAvail = ms.ullAvailPhys; }

            ProcMeta.Fill(list);
            r.List = list; r.Ok = true;
            return r;
            }
        }

        // ---------- Ending tasks ----------

        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        static readonly HashSet<string> critical = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe", "services.exe", "lsass.exe", "LsaIso.exe",
            "svchost.exe", "fontdrvhost.exe", "dwm.exe", "Memory Compression", "Secure System", "MsMpEng.exe", "audiodg.exe", "sihost.exe"
        };

        public static bool IsCritical(string name) { return critical.Contains(name); }

        // Returns (killed, failed)
        public static int[] Kill(IEnumerable<int> pids)
        {
            int ok = 0, fail = 0;
            int me = Process.GetCurrentProcess().Id;
            foreach (int pid in pids)
            {
                if (pid == me || pid <= 4) { fail++; continue; }
                IntPtr h = OpenProcess(0x0001 /* PROCESS_TERMINATE */, false, pid);
                if (h == IntPtr.Zero) { fail++; continue; }
                if (TerminateProcess(h, 1)) ok++; else fail++;
                CloseHandle(h);
            }
            return new[] { ok, fail };
        }
    }

    // Path, friendly name and icon of processes, cached so each process is looked up once.
    static class ProcMeta
    {
        [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);

        class Entry { public long Created; public string Path; }
        static readonly Dictionary<int, Entry> byPid = new Dictionary<int, Entry>();
        static readonly Dictionary<string, string> descByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public static readonly System.Windows.Forms.ImageList Icons = new System.Windows.Forms.ImageList();
        static readonly Dictionary<string, int> iconIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        static ProcMeta()
        {
            Icons.ColorDepth = System.Windows.Forms.ColorDepth.Depth32Bit;
            Icons.ImageSize = new Size(Ui.S(16), Ui.S(16));
            using (var blank = new Bitmap(Ui.S(16), Ui.S(16))) { Icons.Images.Add(blank); var unused = Icons.Handle; }
        }

        public static void Fill(List<ProcInfo> list)
        {
            var alive = new HashSet<int>();
            foreach (var p in list)
            {
                alive.Add(p.Pid);
                Entry e;
                if (!byPid.TryGetValue(p.Pid, out e) || e.Created != p.Created)
                {
                    e = new Entry { Created = p.Created, Path = QueryPath(p.Pid) };
                    byPid[p.Pid] = e;
                }
                p.Path = e.Path;
                p.Display = Describe(e.Path, p.Name);
            }
            if (byPid.Count > alive.Count + 200)
                foreach (var dead in byPid.Keys.Where(k => !alive.Contains(k)).ToList()) byPid.Remove(dead);
        }

        static string QueryPath(int pid)
        {
            IntPtr h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { ProcessSampler.CloseHandle(h); }
        }

        public static string Describe(string path, string fallback)
        {
            if (string.IsNullOrEmpty(path)) return fallback;
            lock (descByPath)
            {
            string d;
            if (descByPath.TryGetValue(path, out d)) return d ?? fallback;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                d = string.IsNullOrWhiteSpace(vi.FileDescription) ? null : vi.FileDescription.Trim();
            }
            catch { d = null; }
            if (descByPath.Count >= 1024) descByPath.Clear();
            descByPath[path] = d;
            return d ?? fallback;
            }
        }

        public static string Company(string path)
        {
            try { return string.IsNullOrEmpty(path) || !File.Exists(path) ? "" : (FileVersionInfo.GetVersionInfo(path).CompanyName ?? "").Trim(); }
            catch { return ""; }
        }

        public static int IconFor(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            int i;
            if (iconIndex.TryGetValue(path, out i)) return i;
            if (Icons.Images.Count >= 1024 || iconIndex.Count >= 2048) return 0;
            i = 0;
            try
            {
                if (File.Exists(path))
                    using (var ic = Icon.ExtractAssociatedIcon(path))
                    using (var bmp = ic.ToBitmap())
                    {
                        using (var scaled = new Bitmap(bmp, Icons.ImageSize)) Icons.Images.Add(scaled);
                        i = Icons.Images.Count - 1;
                    }
            }
            catch { i = 0; }
            iconIndex[path] = i;
            return i;
        }
    }

    // ======================= Startup apps =======================

    class StartupItem
    {
        public string Name, Command, Path, Publisher, Source;
        public bool Enabled;
        public RegistryHive ApprovedHive;
        public string ApprovedSub, ApprovedName;
        public string PackageKey; // for packaged (Store) apps
        public bool Machine { get { return ApprovedHive == RegistryHive.LocalMachine; } }
    }

    static class StartupManager
    {
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        const string PkgRoot = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

        public static List<StartupItem> Load()
        {
            var list = new List<StartupItem>();
            ReadRun(list, RegistryHive.CurrentUser, RegistryView.Registry64, "Run", "Registry (this user)");
            ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, "Run", "Registry (all users)");
            ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, "Run32", "Registry (all users, 32-bit)");
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, "Startup folder (this user)");
            ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, "Startup folder (all users)");
            ReadPackages(list);
            return list.OrderBy(i => i.Enabled ? 0 : 1).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void ReadRun(List<StartupItem> list, RegistryHive hive, RegistryView view, string approvedSub, string source)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(hive, view))
                using (var k = b.OpenSubKey(RunPath))
                {
                    if (k == null) return;
                    foreach (var name in k.GetValueNames())
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var cmd = Convert.ToString(k.GetValue(name));
                        var it = new StartupItem { Command = cmd, Source = source, ApprovedHive = hive, ApprovedSub = approvedSub, ApprovedName = name };
                        it.Path = ExeFromCommand(cmd);
                        it.Name = ProcMeta.Describe(it.Path, name);
                        it.Publisher = ProcMeta.Company(it.Path);
                        it.Enabled = ReadApproved(hive, approvedSub, name);
                        list.Add(it);
                    }
                }
            }
            catch (Exception ex) { App.Log("Startup read failed: " + ex.Message); }
        }

        static void ReadFolder(List<StartupItem> list, string folder, RegistryHive hive, string source)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                foreach (var f in Directory.GetFiles(folder))
                {
                    string file = System.IO.Path.GetFileName(f);
                    if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string target = f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(f) : f;
                    var it = new StartupItem { Command = target ?? f, Path = target, Source = source, ApprovedHive = hive, ApprovedSub = "StartupFolder", ApprovedName = file };
                    it.Name = ProcMeta.Describe(target, System.IO.Path.GetFileNameWithoutExtension(f));
                    it.Publisher = ProcMeta.Company(target);
                    it.Enabled = ReadApproved(hive, "StartupFolder", file);
                    list.Add(it);
                }
            }
            catch (Exception ex) { App.Log("Startup folder read failed: " + ex.Message); }
        }

        static void ReadPackages(List<StartupItem> list)
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(PkgRoot))
                {
                    if (root == null) return;
                    foreach (var pkg in root.GetSubKeyNames())
                    {
                        using (var pk = root.OpenSubKey(pkg))
                        {
                            if (pk == null) continue;
                            foreach (var task in pk.GetSubKeyNames())
                            {
                                using (var tk = pk.OpenSubKey(task))
                                {
                                    if (tk == null) continue;
                                    var st = tk.GetValue("State");
                                    if (!(st is int)) continue;
                                    int state = (int)st;
                                    string fam = pkg.Split('_')[0];
                                    string nice = fam.Contains(".") ? fam.Substring(fam.LastIndexOf('.') + 1) : fam;
                                    list.Add(new StartupItem
                                    {
                                        Name = nice, Command = pkg + " (" + task + ")", Publisher = fam.Contains(".") ? fam.Substring(0, fam.IndexOf('.')) : "",
                                        Source = "Store app", Enabled = state == 2 || state == 4, PackageKey = PkgRoot + "\\" + pkg + "\\" + task,
                                        ApprovedHive = RegistryHive.CurrentUser
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { App.Log("Startup packages read failed: " + ex.Message); }
        }

        static bool ReadApproved(RegistryHive hive, string sub, string name)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (var k = b.OpenSubKey(Approved + sub))
                {
                    var v = k == null ? null : k.GetValue(name) as byte[];
                    return v == null || v.Length == 0 || (v[0] & 1) == 0;
                }
            }
            catch { return true; }
        }

        // Returns null when done, or an error message.
        public static string SetEnabled(StartupItem it, bool on)
        {
            try
            {
                if (it.PackageKey != null)
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(it.PackageKey, true))
                    {
                        if (k == null) return "Not found.";
                        int st = k.GetValue("State") is int ? (int)k.GetValue("State") : 0;
                        if (st == 3 || st == 4) return "This app is controlled by company policy.";
                        k.SetValue("State", on ? 2 : 1, RegistryValueKind.DWord);
                    }
                }
                else
                {
                    using (var b = RegistryKey.OpenBaseKey(it.ApprovedHive, RegistryView.Registry64))
                    using (var k = b.CreateSubKey(Approved + it.ApprovedSub))
                    {
                        var data = new byte[12];
                        data[0] = (byte)(on ? 2 : 3);
                        if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
                        k.SetValue(it.ApprovedName, data, RegistryValueKind.Binary);
                    }
                }
                it.Enabled = on;
                return null;
            }
            catch (UnauthorizedAccessException) { return "\"" + it.Name + "\" is set for all users - restart this app as administrator to change it."; }
            catch (System.Security.SecurityException) { return "\"" + it.Name + "\" is set for all users - restart this app as administrator to change it."; }
            catch (Exception ex) { return ex.Message; }
        }

        public static string ExeFromCommand(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return null;
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            string p;
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                p = end > 0 ? cmd.Substring(1, end - 1) : cmd.Trim('"');
            }
            else
            {
                int i = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                p = i > 0 ? cmd.Substring(0, i + 4) : cmd.Split(' ')[0];
            }
            if (!System.IO.Path.IsPathRooted(p))
            {
                string sys = System.IO.Path.Combine(Environment.SystemDirectory, p);
                if (File.Exists(sys)) return sys;
            }
            return p;
        }

        static string ShortcutTarget(string lnk)
        {
            object shell = null, sc = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                shell = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                string target = (string)sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null);
                return string.IsNullOrEmpty(target) ? null : target;
            }
            catch { return null; }
            finally { if (sc != null) Marshal.ReleaseComObject(sc); if (shell != null) Marshal.ReleaseComObject(shell); }
        }
    }

    // ======================= Disk cleanup =======================

    class CleanTarget
    {
        public string Id, Name, Desc;
        public bool Checked, Admin;
        public List<string> Dirs = new List<string>();
        public int MinAgeHours;        // only files older than this
        public bool Recycle;           // Recycle Bin
        public long Size = -1, Files, Prev = -1;
    }

    class BigFile
    {
        public string Path;
        public long Size, Modified;
    }

    static class FastFiles
    {
        // FILETIME fields are two DWORDs (4-byte aligned), so Pack = 4 keeps the real layout.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
        struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public long ftCreationTime, ftLastAccessTime, ftLastWriteTime;
            public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr FindFirstFileEx(string name, int infoLevel, out WIN32_FIND_DATA data, int searchOp, IntPtr filter, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool FindNextFile(IntPtr h, out WIN32_FIND_DATA data);
        [DllImport("kernel32.dll")] static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool DeleteFile(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool RemoveDirectory(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool SetFileAttributes(string path, uint attr);

        const uint DIR = 0x10, REPARSE = 0x400, READONLY = 0x1;

        static string Long(string path) { return path.StartsWith(@"\\?\") ? path : @"\\?\" + path; }

        // Calls onFile(dir, name, size, lastWriteFileTime) for every file; skips links/junctions.
        public static void Walk(string root, Action<string, string, long, long> onFile, Func<bool> cancel, Action<string> onDir)
        {
            var stack = new Stack<string>();
            stack.Push(root.TrimEnd('\\'));
            while (stack.Count > 0)
            {
                if (cancel != null && cancel()) return;
                string dir = stack.Pop();
                if (onDir != null) onDir(dir);
                WIN32_FIND_DATA d;
                IntPtr h = FindFirstFileEx(Long(dir) + @"\*", 1 /* Basic */, out d, 0, IntPtr.Zero, 2 /* LARGE_FETCH */);
                if (h == new IntPtr(-1)) continue;
                try
                {
                    do
                    {
                        if (cancel != null && cancel()) return;
                        string n = d.cFileName;
                        if (n == "." || n == "..") continue;
                        if ((d.dwFileAttributes & REPARSE) != 0) continue;
                        if ((d.dwFileAttributes & DIR) != 0) stack.Push(dir + "\\" + n);
                        else onFile(dir, n, ((long)d.nFileSizeHigh << 32) | d.nFileSizeLow, d.ftLastWriteTime);
                    } while (FindNextFile(h, out d));
                }
                finally { FindClose(h); }
            }
        }

        public static long[] Measure(string root, int minAgeHours)
        {
            long size = 0, count = 0;
            if (!Directory.Exists(root)) return new long[] { 0, 0 };
            long limit = minAgeHours > 0 ? DateTime.Now.AddHours(-minAgeHours).ToFileTime() : long.MaxValue;
            Walk(root, (dir, n, s, w) => { if (w <= limit) { size += s; count++; } }, null, null);
            return new[] { size, count };
        }

        // Deletes the contents of root (not root itself). Skips files in use. Returns freed bytes.
        public static long Clean(string root, int minAgeHours, Func<bool> cancel)
        {
            if (!Directory.Exists(root)) return 0;
            long freed = 0;
            long limit = minAgeHours > 0 ? DateTime.Now.AddHours(-minAgeHours).ToFileTime() : long.MaxValue;
            var dirs = new List<string>();
            Walk(root, (dir, n, s, w) =>
            {
                if (w > limit) return;
                string p = Long(dir + "\\" + n);
                if (DeleteFile(p) || (SetFileAttributes(p, 0x80) && DeleteFile(p))) freed += s;
            }, cancel, d => dirs.Add(d));
            // Remove now-empty folders, deepest first (never the root).
            string r = root.TrimEnd('\\');
            foreach (var d in dirs.OrderByDescending(x => x.Length))
                if (!string.Equals(d, r, StringComparison.OrdinalIgnoreCase)) RemoveDirectory(Long(d));
            return freed;
        }
    }

    static class Cleaner
    {
        [StructLayout(LayoutKind.Sequential)] // natural (8-byte) packing on 64-bit
        struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, int flags);

        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }
        static string Roaming { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        static string WinDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        static string ProgData { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData); } }

        public static List<CleanTarget> Targets()
        {
            var t = new List<CleanTarget>();
            t.Add(new CleanTarget { Id = "temp", Name = "Temporary files (yours)", Desc = "Leftovers from apps and installers older than 2 days", Checked = true, MinAgeHours = 48, Dirs = { Path.GetTempPath() } });
            t.Add(new CleanTarget { Id = "wintemp", Name = "Windows temporary files", Desc = "C:\\Windows\\Temp older than 2 days (some need admin)", Checked = true, MinAgeHours = 48, Admin = true, Dirs = { Path.Combine(WinDir, "Temp") } });
            t.Add(Browser("chrome", "Google Chrome cache", Path.Combine(Local, @"Google\Chrome\User Data")));
            t.Add(Browser("edge", "Microsoft Edge cache", Path.Combine(Local, @"Microsoft\Edge\User Data")));
            var ff = new CleanTarget { Id = "firefox", Name = "Firefox cache", Desc = "Web page cache; pages load again from the web", Checked = true };
            string ffRoot = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ffRoot)) foreach (var p in SafeDirs(ffRoot)) ff.Dirs.Add(Path.Combine(p, "cache2"));
            t.Add(ff);
            t.Add(AppCaches());
            t.Add(new CleanTarget { Id = "inet", Name = "Internet cache (Office / Windows)", Desc = "INetCache used by Office and Windows components", Checked = true, Dirs = { Path.Combine(Local, @"Microsoft\Windows\INetCache") } });
            t.Add(new CleanTarget
            {
                Id = "dumps", Name = "Crash dumps and error reports", Desc = "Saved after apps crash; only useful for debugging", Checked = true,
                Dirs = { Path.Combine(Local, "CrashDumps"), Path.Combine(Local, @"Microsoft\Windows\WER"), Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportArchive"), Path.Combine(ProgData, @"Microsoft\Windows\WER\ReportQueue") }
            });
            t.Add(new CleanTarget { Id = "wu", Name = "Windows Update downloads", Desc = "Installed update files Windows keeps (needs admin)", Checked = true, Admin = true, Dirs = { Path.Combine(WinDir, @"SoftwareDistribution\Download") } });
            t.Add(new CleanTarget { Id = "recycle", Name = "Recycle Bin (all drives)", Desc = "Files you deleted earlier - gone for good after this", Checked = false, Recycle = true });
            t.Add(new CleanTarget
            {
                Id = "dev", Name = "Developer download caches", Desc = "npm, pip, yarn, NuGet, uv, pnpm caches - downloaded again when needed", Checked = false,
                Dirs = { Path.Combine(Local, "npm-cache"), Path.Combine(Roaming, "npm-cache"), Path.Combine(Local, @"pip\Cache"), Path.Combine(Local, @"Yarn\Cache"), Path.Combine(Local, @"NuGet\v3-cache"), Path.Combine(Local, @"NuGet\plugins-cache"), Path.Combine(Local, @"uv\cache"), Path.Combine(Local, @"pnpm\store") }
            });
            return t;
        }

        static CleanTarget Browser(string id, string name, string userData)
        {
            var c = new CleanTarget { Id = id, Name = name, Desc = "Web page cache; your logins, history and bookmarks are kept", Checked = true };
            if (!Directory.Exists(userData)) return c;
            foreach (var prof in SafeDirs(userData))
            {
                string pn = Path.GetFileName(prof);
                if (!(pn == "Default" || pn.StartsWith("Profile ") || pn == "Guest Profile" || pn == "System Profile")) continue;
                foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage", @"Service Worker\ScriptCache", "DawnGraphiteCache", "DawnWebGPUCache" })
                    c.Dirs.Add(Path.Combine(prof, sub));
            }
            foreach (var sub in new[] { "ShaderCache", "GrShaderCache", "GraphiteDawnCache" }) c.Dirs.Add(Path.Combine(userData, sub));
            return c;
        }

        // Electron / Chromium apps keep "Cache", "Code Cache", "GPUCache" folders (VS Code, Teams, Claude, Slack...).
        static CleanTarget AppCaches()
        {
            var c = new CleanTarget { Id = "apps", Name = "App caches (VS Code, Teams, Claude, Slack ...)", Desc = "Cache folders of desktop apps built on Chromium; rebuilt automatically", Checked = true };
            foreach (var root in new[] { Roaming, Local })
            {
                foreach (var app in SafeDirs(root))
                {
                    string n = Path.GetFileName(app);
                    if (n.Equals("Google", StringComparison.OrdinalIgnoreCase) || n.Equals("Mozilla", StringComparison.OrdinalIgnoreCase) || n.Equals("Packages", StringComparison.OrdinalIgnoreCase) || n.Equals("Temp", StringComparison.OrdinalIgnoreCase)) continue;
                    if (n.Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var ms in new[] { "Teams" }) AddCacheDirs(c, Path.Combine(app, ms));
                        continue;
                    }
                    AddCacheDirs(c, app);
                }
            }
            string newTeams = Path.Combine(Local, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams\EBWebView");
            if (Directory.Exists(newTeams)) foreach (var prof in SafeDirs(newTeams)) AddCacheDirs(c, Path.Combine(prof, "Default"));
            return c;
        }

        static void AddCacheDirs(CleanTarget c, string app)
        {
            foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", "CachedData", @"Service Worker\CacheStorage" })
            {
                string d = Path.Combine(app, sub);
                if (Directory.Exists(d)) c.Dirs.Add(d);
            }
        }

        static IEnumerable<string> SafeDirs(string root)
        {
            try { return Directory.GetDirectories(root); } catch { return new string[0]; }
        }

        public static void Measure(CleanTarget t)
        {
            if (t.Recycle)
            {
                var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
                if (SHQueryRecycleBin(null, ref info) == 0) { t.Size = info.i64Size; t.Files = info.i64NumItems; }
                else t.Size = 0;
                return;
            }
            long size = 0, files = 0;
            foreach (var d in t.Dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var r = FastFiles.Measure(d, t.MinAgeHours);
                size += r[0]; files += r[1];
            }
            t.Size = size; t.Files = files;
        }

        public static long Clean(CleanTarget t, Func<bool> cancel)
        {
            if (t.Recycle)
            {
                long before = Math.Max(0, t.Size);
                SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); // no confirm, no progress, no sound
                return before;
            }
            long freed = 0;
            foreach (var d in t.Dirs.Distinct(StringComparer.OrdinalIgnoreCase)) freed += FastFiles.Clean(d, t.MinAgeHours, cancel);
            return freed;
        }

        // Paths whose files must never be offered for deletion in the big files list.
        public static bool IsProtected(string path)
        {
            string p = path.ToLowerInvariant();
            string win = WinDir.ToLowerInvariant();
            if (p.StartsWith(win + "\\") || p.Contains("\\program files") || p.StartsWith(ProgData.ToLowerInvariant() + "\\microsoft")) return true;
            string name = Path.GetFileName(p);
            return name == "pagefile.sys" || name == "hiberfil.sys" || name == "swapfile.sys" || name == "dumpstack.log.tmp";
        }
    }

    // History of drive free space, cleanup sizes and folder sizes, to show what grew.
    public class SysHistory
    {
        public Dictionary<string, long> CleanSizes = new Dictionary<string, long>();
        public Dictionary<string, long> FolderSizes = new Dictionary<string, long>();
        public Dictionary<string, long> DriveFreeDay = new Dictionary<string, long>(); // "C:|2026-11-14" -> free bytes
        public long LastDeepScan;
    }

    static class Fmt
    {
        public static string Bytes(long b)
        {
            double v = Math.Abs(b);
            string s;
            if (v >= 1L << 40) s = (v / (1L << 40)).ToString("0.00") + " TB";
            else if (v >= 1L << 30) s = (v / (1L << 30)).ToString(v >= 10L << 30 ? "0.0" : "0.00") + " GB";
            else if (v >= 1L << 20) s = (v / (1L << 20)).ToString("0.0") + " MB";
            else if (v >= 1L << 10) s = (v / (1L << 10)).ToString("0") + " KB";
            else s = v.ToString("0") + " B";
            return (b < 0 ? "-" : "") + s;
        }

        public static string Delta(long now, long prev)
        {
            if (prev < 0 || now < 0) return "";
            long d = now - prev;
            if (Math.Abs(d) < 1L << 20) return "no change";
            return (d > 0 ? "+" : "") + Bytes(d);
        }
    }
}
