using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace StickyPro
{
    // Drive names, labels and free space, read on worker threads and cached. Asking a network or card-reader drive
    // ("IsReady", free space, label) can take seconds; doing that while painting froze the whole window (Explorer
    // places bar: up to 12.5 s while resizing). Painting now only reads this cache. Each drive is read on its own
    // thread with a time limit, so one slow drive never holds up the others.
    static class DriveCache
    {
        public class D { public string Name = "", Label = ""; public DriveType Type; public bool Ready; public long Free, Total; }

        static List<D> list = new List<D>();
        static DateTime at = DateTime.MinValue;
        static int busy;
        public static event Action Changed;

        // The cached drives (letters and types at once; labels and free space as soon as the worker has them).
        public static List<D> Get()
        {
            lock (typeof(DriveCache))
            {
                if (list.Count == 0)
                    try { list = DriveInfo.GetDrives().Select(x => new D { Name = x.Name, Type = SafeType(x) }).ToList(); } catch { }
            }
            if (DateTime.Now - at > TimeSpan.FromSeconds(30)) Refresh();
            lock (typeof(DriveCache)) return list.ToList();
        }

        static DriveType SafeType(DriveInfo d) { try { return d.DriveType; } catch { return DriveType.Unknown; } }

        public static void Refresh()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            at = DateTime.Now;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var result = new List<D>();
                try
                {
                    var drives = DriveInfo.GetDrives();
                    var jobs = drives.Select(x =>
                    {
                        var d = new D { Name = x.Name, Type = SafeType(x) };
                        var t = new Thread(() =>
                        {
                            try
                            {
                                if (x.IsReady)
                                {
                                    d.Ready = true;
                                    d.Label = x.VolumeLabel ?? "";
                                    d.Total = x.TotalSize; d.Free = x.AvailableFreeSpace;
                                }
                            }
                            catch { }
                        }) { IsBackground = true };
                        t.Start();
                        return new { d, t };
                    }).ToList();
                    var deadline = DateTime.Now.AddSeconds(4);
                    foreach (var j in jobs) { var left = deadline - DateTime.Now; if (left > TimeSpan.Zero) j.t.Join(left); result.Add(j.d); }
                }
                catch { }
                lock (typeof(DriveCache)) { if (result.Count > 0) list = result; }
                at = DateTime.Now;
                Interlocked.Exchange(ref busy, 0);
                var h = Changed;
                if (h != null) try { App.I.Sync.BeginInvoke(h); } catch { }
            });
        }

        public static string Title(D d)
        {
            string kind = d.Type == DriveType.Network ? "Network" : d.Type == DriveType.Removable ? "USB drive" : "Local Disk";
            return (d.Ready && d.Label.Length > 0 ? d.Label : kind) + " (" + d.Name.TrimEnd('\\') + ")";
        }

        // "Does this folder exist?" without blocking: local paths answer at once, network paths (\\server, mapped
        // network drives) are checked on a worker and reported through done (on the UI thread).
        public static bool IsSlowPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith(@"\\")) return true;
            if (path.Length >= 2 && path[1] == ':')
            {
                string root = path.Substring(0, 2).ToUpperInvariant() + "\\";
                lock (typeof(DriveCache))
                {
                    var d = list.FirstOrDefault(x => string.Equals(x.Name, root, StringComparison.OrdinalIgnoreCase));
                    if (d == null) { try { d = new D { Name = root, Type = SafeType(new DriveInfo(root)) }; } catch { return true; } }
                    return d.Type != DriveType.Fixed;
                }
            }
            return false;
        }
    }
}
