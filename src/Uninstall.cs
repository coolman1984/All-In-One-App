using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StickyPro
{
    class InstalledApp
    {
        public string Name = "", Publisher = "", Version = "", Location = "", UninstallCmd = "", QuietCmd = "", Icon = "", Kind = "";
        public RegistryHive Hive; public RegistryView View; public string SubKey = "";
        public string PackageFullName;
        public long SizeBytes; public DateTime Installed; public bool IsMsi; public string MsiCode;
        public string KeyPath { get { return SubKey.Length == 0 ? "" : (Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE\\" : "HKEY_CURRENT_USER\\") + SubKey; } }
    }

    static class Programs
    {
        const string UKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        // Worker thread.
        public static List<InstalledApp> Load(bool store)
        {
            var list = new List<InstalledApp>();
            Read(list, RegistryHive.LocalMachine, RegistryView.Registry64, "x64");
            Read(list, RegistryHive.LocalMachine, RegistryView.Registry32, "x86");
            Read(list, RegistryHive.CurrentUser, RegistryView.Registry64, "User");
            var dedup = list.GroupBy(a => (a.Name + "|" + a.Version).ToLowerInvariant())
                .Select(g => g.OrderByDescending(a => a.UninstallCmd.Length > 0).ThenByDescending(a => a.Location.Length > 0).First()).ToList();
            if (store) dedup.AddRange(StoreApps());
            return dedup.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Worker thread. Store apps only (slow: waits for PowerShell).
        public static List<InstalledApp> LoadStore() { return StoreApps().ToList(); }

        static void Read(List<InstalledApp> list, RegistryHive hive, RegistryView view, string kind)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(hive, view))
                using (var root = b.OpenSubKey(UKey))
                {
                    if (root == null) return;
                    foreach (var name in root.GetSubKeyNames())
                    {
                        try
                        {
                            using (var k = root.OpenSubKey(name))
                            {
                                if (k == null) continue;
                                string dn = S(k, "DisplayName"), un = S(k, "UninstallString");
                                if (dn.Length == 0 || un.Length == 0) continue;
                                if (I(k, "SystemComponent") == 1 || S(k, "ParentKeyName").Length > 0) continue;
                                string rt = S(k, "ReleaseType");
                                if (rt == "Update" || rt == "Hotfix" || rt == "Security Update") continue;
                                var a = new InstalledApp
                                {
                                    Name = dn, Publisher = S(k, "Publisher"), Version = S(k, "DisplayVersion"), Location = S(k, "InstallLocation").Trim('"'),
                                    UninstallCmd = un, QuietCmd = S(k, "QuietUninstallString"), Icon = S(k, "DisplayIcon"), Kind = kind, Hive = hive, View = view, SubKey = UKey + "\\" + name
                                };
                                a.SizeBytes = I(k, "EstimatedSize") * 1024L;
                                DateTime d; if (DateTime.TryParseExact(S(k, "InstallDate"), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out d)) a.Installed = d;
                                if (I(k, "WindowsInstaller") == 1 || Regex.IsMatch(un, @"msiexec", RegexOptions.IgnoreCase))
                                {
                                    var m = Regex.Match(name + " " + un, @"\{[0-9A-Fa-f\-]{36}\}");
                                    if (m.Success) { a.IsMsi = true; a.MsiCode = m.Value; }
                                }
                                list.Add(a);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }
        static string S(RegistryKey k, string n) { try { var v = k.GetValue(n); return v == null ? "" : v.ToString().Trim(); } catch { return ""; } }
        static long I(RegistryKey k, string n) { try { var v = k.GetValue(n); return v is int ? (int)v : v is long ? (long)v : 0; } catch { return 0; } }

        static IEnumerable<InstalledApp> StoreApps()
        {
            var res = new List<InstalledApp>();
            string o = Hidden.PowerShell("Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | ForEach-Object { $_.Name + '|' + $_.PackageFullName + '|' + $_.Version + '|' + $_.Publisher + '|' + $_.InstallLocation }", 30000);
            if (o == null) return res;
            foreach (var line in o.Split('\n'))
            {
                var p = line.Trim().Split('|'); if (p.Length < 5) continue;
                string nm = p[0].Split('.').Last();
                nm = Regex.Replace(nm, "(?<=[a-z])(?=[A-Z])", " ");
                string pub = Regex.Match(p[3], @"CN=([^,]+)").Groups[1].Value;
                res.Add(new InstalledApp { Name = nm + "  (" + p[0] + ")", PackageFullName = p[1], Version = p[2], Publisher = pub, Location = p[4], Kind = "Store", UninstallCmd = "Remove-AppxPackage" });
            }
            return res;
        }

        public static bool StillInstalled(InstalledApp a)
        {
            if (a.Kind == "Store")
            {
                string o = Hidden.PowerShell("if (Get-AppxPackage | Where-Object { $_.PackageFullName -eq '" + a.PackageFullName.Replace("'", "''") + "' }) { 'yes' } else { 'no' }", 30000);
                return o != null && o.Contains("yes");
            }
            try { using (var b = RegistryKey.OpenBaseKey(a.Hive, a.View)) using (var k = b.OpenSubKey(a.SubKey)) return k != null; } catch { return true; }
        }

        // Starts the uninstaller and waits (worker thread). Returns an error text or null.
        public static string RunUninstaller(InstalledApp a, bool quiet)
        {
            try
            {
                Process p;
                if (a.Kind == "Store")
                {
                    Hidden.PowerShell("Remove-AppxPackage -Package '" + a.PackageFullName.Replace("'", "''") + "'", 600000);
                    return null;
                }
                if (a.IsMsi && !quiet) p = Process.Start(new ProcessStartInfo("msiexec.exe", "/x " + a.MsiCode) { UseShellExecute = true });
                else
                {
                    string file, args; Split(quiet && a.QuietCmd.Length > 0 ? a.QuietCmd : a.UninstallCmd, out file, out args);
                    p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
                }
                if (p != null) { p.WaitForExit(30 * 60 * 1000); p.Dispose(); }
                Thread.Sleep(3000);
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public static void Split(string cmd, out string file, out string args)
        {
            cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (cmd.StartsWith("\"")) { int e = cmd.IndexOf('"', 1); if (e > 0) { file = cmd.Substring(1, e - 1); args = cmd.Substring(e + 1).Trim(); return; } }
            int x = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (x > 0) { file = cmd.Substring(0, x + 4); args = cmd.Substring(x + 4).Trim(); return; }
            int sp = cmd.IndexOf(' ');
            file = sp > 0 ? cmd.Substring(0, sp) : cmd; args = sp > 0 ? cmd.Substring(sp + 1) : "";
        }
    }

    class Leftover
    {
        public string Group, Text, Path, Confidence; // Group: Folders & files | Shortcuts | Registry | Startup entries | Services
        public bool Check, InfoOnly, IsValue, Hklm;
        public RegistryHive Hive; public RegistryView View; public string Key, ValueName;
        public long Size = -1;
    }

    static class Leftovers
    {
        static readonly string[] NeverNames = { "Common Files", "Microsoft", "WindowsApps", "Microsoft.NET", "Windows Defender", "Packages", "Windows", "Temp", "Programs", "Intel", "NVIDIA Corporation", "AMD", "Google", "Mozilla" };

        static string Clean(string s)
        {
            s = Regex.Replace(s ?? "", @"\((x64|x86|64-bit|32-bit|R|TM)\)|\b(x64|x86|64-bit|32-bit)\b|\d+(\.\d+)+|\(.*?\)|\[.*?\]", " ", RegexOptions.IgnoreCase);
            s = s.Replace("®", "").Replace("™", "");
            return Regex.Replace(s, @"\s+", " ").Trim(' ', '-', '_', ',');
        }
        static string CleanPub(string s)
        {
            s = Regex.Replace(s ?? "", @",?\s*(Inc\.?|Ltd\.?|LLC|Corporation|Corp\.?|GmbH|Co\.|Limited|S\.A\.)\s*$", "", RegexOptions.IgnoreCase);
            return s.Trim(' ', ',', '.');
        }
        static bool Eq(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        static bool Under(string child, string parent)
        {
            if (string.IsNullOrEmpty(child) || string.IsNullOrEmpty(parent)) return false;
            string c = child.TrimEnd('\\') + "\\", p = parent.TrimEnd('\\') + "\\";
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        // Worker thread. others = every other installed program (to protect their folders).
        public static List<Leftover> Scan(InstalledApp a, List<InstalledApp> all, bool includeKey)
        {
            var res = new List<Leftover>();
            string core = Clean(a.Name), pub = CleanPub(a.Publisher);
            if (a.Kind == "Store") core = Clean(a.Name.Split('(')[0]);
            if (core.Length < 4) core = a.Name.Trim();
            // Store apps have generic names ("Photos", "Mail"): a name-only match is never "Safe" and never pre-ticked.
            bool store = a.Kind == "Store";
            string nameConf = store ? "Maybe" : "Safe";
            var otherLocs = all.Where(o => o != a && !string.IsNullOrEmpty(o.Location) && Directory.Exists(o.Location)).Select(o => o.Location.TrimEnd('\\')).ToList();
            string self = Application.StartupPath;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Func<string, bool> safeFolder = path =>
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path) || seen.Contains(path)) return false;
                if (Under(path, win) || Under(self, path) || Under(path, self)) return false;
                if (NeverNames.Any(n => Eq(Path.GetFileName(path.TrimEnd('\\')), n))) return false;
                foreach (var o in otherLocs) if (Under(o, path) || Under(path, o)) return false;
                if (Path.GetPathRoot(path).TrimEnd('\\').Length >= path.TrimEnd('\\').Length) return false;
                return true;
            };
            Action<string, string> addFolder = (path, conf) =>
            {
                if (!safeFolder(path)) return;
                if (CountFiles(path, 20000) >= 20000) return;
                seen.Add(path);
                res.Add(new Leftover { Group = "Folders & files", Text = path, Path = path, Confidence = conf, Check = conf == "Safe" });
            };
            if (a.Kind != "Store" && !string.IsNullOrEmpty(a.Location)) addFolder(a.Location.TrimEnd('\\'), "Safe");
            var roots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string[] dirs; try { dirs = Directory.GetDirectories(root); } catch { continue; }
                foreach (var d in dirs)
                {
                    string n = Path.GetFileName(d);
                    if (Eq(n, core)) addFolder(d, nameConf);
                    else if (pub.Length >= 4 && Eq(n, pub))
                    {
                        string inner = Path.Combine(d, core);
                        if (Directory.Exists(inner)) addFolder(inner, nameConf);
                        string[] subs; try { subs = Directory.GetDirectories(d); } catch { subs = new string[0]; }
                        if (subs.Length <= 1) addFolder(d, "Maybe");
                    }
                    else if (core.Length >= 5 && n.StartsWith(core, StringComparison.OrdinalIgnoreCase)) addFolder(d, "Maybe");
                }
            }
            // Shortcuts
            var lnkRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) };
            foreach (var r in lnkRoots.Where(x => !string.IsNullOrEmpty(x) && Directory.Exists(x)))
                foreach (var f in Files(r, "*.lnk", 3))
                    if (Path.GetFileNameWithoutExtension(f).IndexOf(core, StringComparison.OrdinalIgnoreCase) >= 0 && !seen.Any(s => Under(f, s)))
                        res.Add(new Leftover { Group = "Shortcuts", Text = f, Path = f, Confidence = nameConf, Check = !store });
            // Start menu folder named like the app
            foreach (var r in lnkRoots.Take(2).Where(x => !string.IsNullOrEmpty(x) && Directory.Exists(x)))
            {
                string d = Path.Combine(r, core); if (Directory.Exists(d) && !seen.Contains(d)) { seen.Add(d); res.Add(new Leftover { Group = "Shortcuts", Text = d, Path = d, Confidence = nameConf, Check = !store }); }
            }
            // Registry keys
            var regs = new List<string> { @"Software\" + core };
            if (pub.Length >= 3) regs.Insert(0, @"Software\" + pub + @"\" + core);
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in hive == RegistryHive.CurrentUser ? new[] { RegistryView.Registry64 } : new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    foreach (var k in regs) AddKey(res, hive, view, k, nameConf);
                    if (pub.Length >= 4 && !Eq(pub, "Microsoft"))
                        try
                        {
                            using (var b = RegistryKey.OpenBaseKey(hive, view)) using (var pk = b.OpenSubKey(@"Software\" + pub))
                                if (pk != null && pk.SubKeyCount <= 1) AddKey(res, hive, view, @"Software\" + pub, "Maybe");
                        }
                        catch { }
                    // Run entries
                    foreach (var run in new[] { @"Software\Microsoft\Windows\CurrentVersion\Run", @"Software\Microsoft\Windows\CurrentVersion\RunOnce" })
                        try
                        {
                            using (var b = RegistryKey.OpenBaseKey(hive, view)) using (var rk = b.OpenSubKey(run))
                            {
                                if (rk == null) continue;
                                foreach (var vn in rk.GetValueNames())
                                {
                                    string cmd = (rk.GetValue(vn) ?? "").ToString();
                                    if ((a.Location.Length > 3 && cmd.IndexOf(a.Location.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0) || cmd.IndexOf(core, StringComparison.OrdinalIgnoreCase) >= 0 || Eq(vn, core))
                                        res.Add(new Leftover { Group = "Startup entries", Text = Root(hive) + "\\" + run + "  :  " + vn + " = " + cmd, Confidence = nameConf, Check = !store, Hive = hive, View = view, Key = run, ValueName = vn, IsValue = true, Hklm = hive == RegistryHive.LocalMachine });
                                }
                            }
                        }
                        catch { }
                }
            if (includeKey && a.SubKey.Length > 0) AddKey(res, a.Hive, a.View, a.SubKey, "Safe");
            // Services (info only)
            if (a.Location.Length > 3)
                try
                {
                    using (var sv = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                        foreach (var n in sv.GetSubKeyNames())
                            try { using (var k = sv.OpenSubKey(n)) { string ip = (k.GetValue("ImagePath") ?? "").ToString(); if (ip.IndexOf(a.Location.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) >= 0) res.Add(new Leftover { Group = "Services (info only)", Text = "Service: " + n + "  (" + ip + ")  - remove with an admin command if needed", InfoOnly = true, Confidence = "Info" }); } }
                            catch { }
                }
                catch { }
            return res;
        }

        static string Root(RegistryHive h) { return h == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER"; }

        static void AddKey(List<Leftover> res, RegistryHive hive, RegistryView view, string key, string conf)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(hive, view)) using (var k = b.OpenSubKey(key))
                {
                    if (k == null) return;
                    string text = Root(hive) + "\\" + key + (view == RegistryView.Registry32 && hive == RegistryHive.LocalMachine ? "  (32-bit)" : "");
                    if (res.Any(r => r.Text == text)) return;
                    res.Add(new Leftover { Group = "Registry", Text = text, Confidence = conf, Check = conf == "Safe", Hive = hive, View = view, Key = key, Hklm = hive == RegistryHive.LocalMachine });
                }
            }
            catch { }
        }

        static int CountFiles(string dir, int max)
        {
            int n = 0;
            try { foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) if (++n >= max) break; } catch { }
            return n;
        }
        public static long Size(string path)
        {
            try { if (File.Exists(path)) return new FileInfo(path).Length; long s = 0; foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories)) s += f.Length; return s; } catch { return 0; }
        }
        static IEnumerable<string> Files(string dir, string pattern, int depth)
        {
            var list = new List<string>();
            try { list.AddRange(Directory.GetFiles(dir, pattern)); if (depth > 0) foreach (var d in Directory.GetDirectories(dir)) list.AddRange(Files(d, pattern, depth - 1)); } catch { }
            return list;
        }

        // ----- deletion -----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
        // True when the path is on a local fixed drive (which has a Recycle Bin). Network, removable and unknown drives: false.
        public static bool OnFixedDrive(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || root.StartsWith("\\\\")) return false;
                return new DriveInfo(root).DriveType == DriveType.Fixed;
            }
            catch { return false; }
        }

        // Recycle Bin only: paths on drives without a bin are refused (they would be deleted for good).
        public static bool Recycle(string path) { return Recycle(path, false); }

        public static bool Recycle(string path, bool allowPermanent)
        {
            if (!allowPermanent && !OnFixedDrive(path)) return false;
            var op = new SHFILEOPSTRUCT { wFunc = 3 /* FO_DELETE */, pFrom = path + "\0\0", fFlags = 0x40 | 0x10 | 0x4 | 0x400 /* ALLOWUNDO|NOCONFIRMATION|SILENT|NOERRORUI */ };
            return SHFileOperation(ref op) == 0 && !File.Exists(path) && !Directory.Exists(path);
        }

        public static bool ExportKey(Leftover l, string file)
        {
            string key = Root(l.Hive) + "\\" + l.Key;
            int code;
            string args = "export \"" + key + "\" \"" + file + "\" /y" + (l.Hklm ? (l.View == RegistryView.Registry32 ? " /reg:32" : " /reg:64") : "");
            Hidden.Run("reg.exe", args, 30000, out code);
            return code == 0 && File.Exists(file);
        }

        public static bool DeleteReg(Leftover l)
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(l.Hive, l.View))
                {
                    if (l.IsValue) { using (var k = b.OpenSubKey(l.Key, true)) { if (k == null) return true; k.DeleteValue(l.ValueName, false); } }
                    else b.DeleteSubKeyTree(l.Key, false);
                }
                return true;
            }
            catch { return false; }
        }
    }

    class LeftoverForm : Form
    {
        readonly FastList lv;
        readonly Label status;
        readonly InstalledApp app;
        readonly List<Leftover> items;
        readonly Button btnDelete;
        public LeftoverForm(InstalledApp app, List<Leftover> items)
        {
            this.app = app; this.items = items;
            AutoScaleMode = AutoScaleMode.None; Text = "Leftovers of " + app.Name; Icon = App.AppIcon; Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterScreen; ClientSize = Ui.S(900, 560); MinimumSize = Ui.S(520, 320); ShowInTaskbar = true;
            Ui.DarkTitle(this);
            lv = new FastList { Dock = DockStyle.Fill, CheckBoxes = true, ShowGroups = true };
            lv.Columns.Add("Item", Ui.S(560)); lv.Columns.Add("Confidence", Ui.S(90)); lv.Columns.Add("Size", Ui.S(90), HorizontalAlignment.Right);
            lv.FillIndex = 0;
            var groups = new Dictionary<string, ListViewGroup>();
            foreach (var l in items)
            {
                ListViewGroup g; if (!groups.TryGetValue(l.Group, out g)) { g = new ListViewGroup(l.Group); groups[l.Group] = g; lv.Groups.Add(g); }
                var li = new ListViewItem(l.Text, g) { Tag = l, Checked = l.Check && !l.InfoOnly };
                li.SubItems.Add(l.Confidence); li.SubItems.Add(l.Path != null ? "..." : "");
                if (l.Confidence == "Maybe") li.ForeColor = Ui.Warning; else if (l.InfoOnly) li.ForeColor = Ui.Muted;
                lv.Items.Add(li);
            }
            lv.ItemCheck += (s, e) => { var l = (Leftover)lv.Items[e.Index].Tag; if (l.InfoOnly) e.NewValue = CheckState.Unchecked; };
            var m = Ui.Menu();
            m.Items.Add("Open location", null, delegate { OpenSel(); });
            Term.AddMenu(m.Items, () => { if (lv.SelectedItems.Count == 0) return null; var p = ((Leftover)lv.SelectedItems[0].Tag).Path; return p != null && (Directory.Exists(p) || File.Exists(p)) ? p : null; });
            m.Items.Add("Copy path", null, delegate { if (lv.SelectedItems.Count > 0) App.I.CopyText(((Leftover)lv.SelectedItems[0].Tag).Text); });
            Ui.ThemeMenu(m); lv.ContextMenuStrip = m;
            lv.DoubleClick += delegate { OpenSel(); };
            status = new Label { Dock = DockStyle.Bottom, Height = Ui.S(26), ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.MiddleLeft, Text = items.Count == 0 ? "No leftovers found. The program is clean." : items.Count + " item(s) found. Safe items are ticked; yellow \"Maybe\" items need your check. Files go to the Recycle Bin; registry keys are backed up first." };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(Ui.S(8)) };
            btnDelete = Ui.Btn("Delete selected", delegate { DeleteSelected(); }, true);
            bar.Controls.Add(btnDelete);
            bar.Controls.Add(Ui.Btn("Select safe only", delegate { foreach (ListViewItem li in lv.Items) { var l = (Leftover)li.Tag; li.Checked = l.Confidence == "Safe" && !l.InfoOnly; } }));
            bar.Controls.Add(Ui.Btn("Select none", delegate { foreach (ListViewItem li in lv.Items) li.Checked = false; }));
            bar.Controls.Add(Ui.Btn("Open location", delegate { OpenSel(); }));
            bar.Controls.Add(Ui.Btn("Close", delegate { Close(); }));
            Controls.Add(lv); Controls.Add(bar); Controls.Add(status);
            Ui.Theme(this);
            Shown += delegate { ComputeSizes(); };
        }

        void ComputeSizes()
        {
            var rows = lv.Items.Cast<ListViewItem>().Where(li => ((Leftover)li.Tag).Path != null).ToList();
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var li in rows)
                {
                    var l = (Leftover)li.Tag; long s = Leftovers.Size(l.Path); l.Size = s;
                    var item = li;
                    try { BeginInvoke(new Action(() => { if (!IsDisposed) item.SubItems[2].Text = Fmt.Bytes(s); })); } catch { return; }
                }
            });
        }

        void OpenSel()
        {
            if (lv.SelectedItems.Count == 0) return;
            var l = (Leftover)lv.SelectedItems[0].Tag;
            if (l.Path != null) { if (Directory.Exists(l.Path)) Hidden.Open("explorer.exe", "\"" + l.Path + "\""); else Hidden.Open("explorer.exe", "/select,\"" + l.Path + "\""); }
            else if (l.Key != null) RegEdit.Open(Leftovers_Root(l) + "\\" + l.Key);
        }
        static string Leftovers_Root(Leftover l) { return l.Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER"; }

        void DeleteSelected()
        {
            var sel = lv.CheckedItems.Cast<ListViewItem>().Select(li => (Leftover)li.Tag).Where(l => !l.InfoOnly).ToList();
            if (sel.Count == 0) return;
            if (MessageBox.Show(this, "Remove " + sel.Count + " item(s)?\n\nFiles and folders go to the Recycle Bin. Registry keys are exported to a .reg backup first.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            // Drives without a Recycle Bin (network, removable) would delete for good: ask first.
            bool allowPermanent = false;
            var noBin = sel.Where(l => l.Path != null && !Leftovers.OnFixedDrive(l.Path)).ToList();
            if (noBin.Count > 0)
            {
                var dr = MessageBox.Show(this, noBin.Count + " item(s) are on a drive without a Recycle Bin (network or removable) and would be deleted permanently:\n\n" + string.Join("\n", noBin.Select(l => l.Text).Take(8)) + "\n\nYes = delete them permanently\nNo = skip them\nCancel = stop", App.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (dr == DialogResult.Cancel) return;
                allowPermanent = dr == DialogResult.Yes;
            }
            btnDelete.Enabled = false;
            string backupDir = Path.Combine(Store.Dir, "uninstall-backups");
            string safeName = Regex.Replace(app.Name, @"[^\w\- ]", "_");
            bool admin = AdminCache.Is;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int done = 0, failed = 0, skipped = 0; long bytes = 0; var needAdmin = new List<string>(); int n = 0;
                var removed = new HashSet<Leftover>();
                try { Directory.CreateDirectory(backupDir); } catch { }
                foreach (var l in sel)
                {
                    n++;
                    var cur = l;
                    try { BeginInvoke(new Action(() => status.Text = "Removing " + cur.Text + " ...")); } catch { }
                    string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), pf86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? "", pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    bool machine = l.Hklm || (l.Path != null && (l.Path.StartsWith(pf, StringComparison.OrdinalIgnoreCase) || (pf86.Length > 0 && l.Path.StartsWith(pf86, StringComparison.OrdinalIgnoreCase)) || l.Path.StartsWith(pd, StringComparison.OrdinalIgnoreCase)));
                    if (l.Path != null)
                    {
                        if (!allowPermanent && !Leftovers.OnFixedDrive(l.Path)) { skipped++; continue; }
                        if (Leftovers.Recycle(l.Path, allowPermanent)) { done++; bytes += Math.Max(0, l.Size); removed.Add(l); }
                        else if (machine && !admin) needAdmin.Add(l.Text); else failed++;
                    }
                    else
                    {
                        string file = Path.Combine(backupDir, safeName + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + n + ".reg");
                        if (!Leftovers.ExportKey(l, file)) { failed++; continue; }
                        if (Leftovers.DeleteReg(l)) { done++; removed.Add(l); } else if (machine && !admin) needAdmin.Add(l.Text); else failed++;
                    }
                }
                int notRemoved = failed + skipped + needAdmin.Count;
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) return;
                        // Only rows that were really removed leave the list; failed ones stay ticked so you can retry.
                        foreach (var li in lv.CheckedItems.Cast<ListViewItem>().ToList()) if (removed.Contains((Leftover)li.Tag)) li.Remove();
                        status.Text = "Removed " + done + " item(s), " + Fmt.Bytes(bytes) + " to the Recycle Bin." + (notRemoved > 0 ? " " + notRemoved + " could not be removed." : "");
                        string msg = "Removed " + done + " item(s) (" + Fmt.Bytes(bytes) + " to the Recycle Bin)." + (notRemoved > 0 ? "\n" + notRemoved + " item(s) could not be removed (in use, skipped, or needs admin)." : "") +
                            (needAdmin.Count > 0 ? "\n\nNeeds admin (use \"Run as admin\" and scan again):\n" + string.Join("\n", needAdmin.Take(10)) : "") + "\n\nRegistry backups: " + backupDir + "\nOpen the backups folder?";
                        if (MessageBox.Show(this, msg, App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Hidden.Open("explorer.exe", "\"" + backupDir + "\"");
                        btnDelete.Enabled = true;
                    }));
                }
                catch { }
            });
        }
    }

    static class RegEdit
    {
        public static void Open(string fullKey)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit")) k.SetValue("LastKey", "Computer\\" + fullKey);
                Hidden.Open("regedit.exe", "/m");
            }
            catch (Exception ex) { App.Log("regedit: " + ex.Message); }
        }
    }

    // Admin.IsAdmin builds a WindowsIdentity every time; the answer never changes while the app runs.
    static class AdminCache
    {
        static bool? value;
        public static bool Is { get { if (value == null) value = Admin.IsAdmin; return value.Value; } }
    }

    class UninstallPanel : LivePanel
    {
        readonly FastList lv;
        readonly TextBox search;
        readonly ComboBox filter;
        readonly Label summary;
        readonly Action<string> status;
        List<InstalledApp> all = new List<InstalledApp>();
        List<InstalledApp> rows = new List<InstalledApp>();
        bool loaded, busy, storeLoading;
        int sortCol; bool sortDesc;
        int reloadGen;
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 250 };

        public UninstallPanel(Action<string> status)
        {
            this.status = status ?? (s => { });
            Dock = DockStyle.Fill;
            lv = new FastList { Dock = DockStyle.Fill, VirtualMode = true, MultiSelect = true, SmallImageList = ProcMeta.Icons };
            lv.Columns.Add("Name", Ui.S(280)); lv.Columns.Add("Publisher", Ui.S(170)); lv.Columns.Add("Version", Ui.S(100));
            lv.Columns.Add("Installed", Ui.S(95)); lv.Columns.Add("Size", Ui.S(80), HorizontalAlignment.Right); lv.Columns.Add("Type", Ui.S(60)); lv.Columns.Add("Location", Ui.S(260));
            lv.Weights = new[] { 3, 1, 0, 0, 0, 0, 2 }; // Name, Publisher, Version, Installed, Size, Type, Location
            lv.RetrieveVirtualItem += Retrieve;
            lv.ColumnClick += (s, e) => { if (sortCol == e.Column) sortDesc = !sortDesc; else { sortCol = e.Column; sortDesc = e.Column == 3 || e.Column == 4; } Rebuild(); };
            lv.DoubleClick += delegate { Uninstall(false); };
            lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) { Uninstall(false); e.SuppressKeyPress = true; } else if (e.Control && e.KeyCode == Keys.F) { search.Focus(); e.SuppressKeyPress = true; } };
            lv.ContextMenuStrip = BuildMenu();
            Controls.Add(lv);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = Ui.S(0, 38), WrapContents = true };
            search = Ui.Search("Search programs"); search.Width = Ui.S(190); search.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            searchTimer.Tick += delegate { searchTimer.Stop(); Rebuild(); };
            search.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            filter = MainForm.Combo(170, "All programs", "Desktop apps", "Store apps", "Large (> 500 MB)", "Installed last 30 days");
            filter.SelectedIndex = 0; filter.Margin = new Padding(0, Ui.S(4), Ui.S(8), 0);
            filter.SelectedIndexChanged += delegate { Rebuild(); };
            bar.Controls.Add(search); bar.Controls.Add(filter);
            bar.Controls.Add(Ui.Btn("Uninstall", delegate { Uninstall(false); }, true));
            bar.Controls.Add(Ui.Btn("Force remove", delegate { ForceRemove(); }));
            bar.Controls.Add(Ui.Btn("Scan leftovers", delegate { var a = Sel().FirstOrDefault(); if (a != null) ScanLeftovers(a, false); }));
            bar.Controls.Add(Ui.Btn("Refresh", delegate { Reload(); }));
            bar.Controls.Add(Ui.Btn("Compute sizes", delegate { ComputeSizes(); }));
            bar.Controls.Add(Ui.Btn("Export CSV", delegate { ExportCsv(); }));
            bar.Controls.Add(Ui.Btn("Apps & features", delegate { Hidden.Open("ms-settings:appsfeatures"); }));
            if (!AdminCache.Is) bar.Controls.Add(Ui.Btn("Run as admin", delegate { App.I.RestartElevated(); }));
            Controls.Add(bar);
            summary = new Label { Dock = DockStyle.Top, Height = Ui.S(24), Font = Ui.Bold, TextAlign = ContentAlignment.MiddleLeft };
            Controls.Add(summary);
            Ui.Theme(this);
        }

        protected override void OnLive(bool on) { if (on && !loaded) Reload(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { searchTimer.Stop(); searchTimer.Dispose(); }
            base.Dispose(disposing);
        }

        void Reload()
        {
            loaded = true; summary.Text = "Loading programs...";
            int gen = ++reloadGen;
            storeLoading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                // Desktop programs (registry) show at once; Store apps (PowerShell, slow) are appended when ready.
                List<InstalledApp> list;
                try { list = Programs.Load(false); } catch (Exception ex) { App.Log("Programs: " + ex.Message); list = new List<InstalledApp>(); }
                UI(() => { if (gen != reloadGen) return; all = list; RebuildCore(true); });
                List<InstalledApp> store;
                try { store = Programs.LoadStore(); } catch (Exception ex) { App.Log("Store apps: " + ex.Message); store = new List<InstalledApp>(); }
                UI(() =>
                {
                    if (gen != reloadGen) return;
                    storeLoading = false;
                    all = list.Concat(store).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    RebuildCore(true);
                });
            });
        }

        void Rebuild() { RebuildCore(false); }

        void RebuildCore(bool keepSelection)
        {
            var keep = keepSelection ? new HashSet<InstalledApp>(Sel()) : null;
            string q = search.Text.Trim();
            IEnumerable<InstalledApp> items = all;
            switch (filter.SelectedIndex)
            {
                case 1: items = items.Where(a => a.Kind != "Store"); break;
                case 2: items = items.Where(a => a.Kind == "Store"); break;
                case 3: items = items.Where(a => a.SizeBytes > 500L << 20); break;
                case 4: items = items.Where(a => a.Installed > DateTime.Today.AddDays(-30)); break;
            }
            if (q.Length > 0) items = items.Where(a => a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || a.Publisher.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            Func<InstalledApp, IComparable> key;
            switch (sortCol)
            {
                case 1: key = a => a.Publisher.ToLowerInvariant(); break;
                case 2: key = a => a.Version; break;
                case 3: key = a => a.Installed; break;
                case 4: key = a => a.SizeBytes; break;
                case 5: key = a => a.Kind; break;
                case 6: key = a => a.Location.ToLowerInvariant(); break;
                default: key = a => a.Name.ToLowerInvariant(); break;
            }
            rows = (sortDesc ? items.OrderByDescending(key) : items.OrderBy(key)).ToList();
            lv.BeginUpdate(); lv.SelectedIndices.Clear(); lv.VirtualListSize = rows.Count;
            if (keep != null && keep.Count > 0) for (int i = 0; i < rows.Count; i++) if (keep.Contains(rows[i])) lv.SelectedIndices.Add(i);
            lv.EndUpdate(); lv.Invalidate();
            summary.Text = rows.Count + " programs" + (rows.Count != all.Count ? " (of " + all.Count + ")" : "") + ", " + Fmt.Bytes(rows.Sum(a => a.SizeBytes)) + (storeLoading ? "   ·   loading Store apps..." : "") + (AdminCache.Is ? "" : "   ·   Some programs need \"Run as admin\" to remove leftovers");
        }

        void Retrieve(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= rows.Count) { e.Item = new ListViewItem(""); e.Item.SubItems.AddRange(new[] { "", "", "", "", "", "" }); return; }
            var a = rows[e.ItemIndex];
            var it = new ListViewItem(a.Name);
            string icon = a.Icon.Split(',')[0].Trim('"');
            it.ImageIndex = icon.Length > 0 ? ProcMeta.IconFor(Environment.ExpandEnvironmentVariables(icon)) : 0;
            it.SubItems.Add(a.Publisher); it.SubItems.Add(a.Version); it.SubItems.Add(a.Installed == DateTime.MinValue ? "" : a.Installed.ToString("yyyy-MM-dd"));
            it.SubItems.Add(a.SizeBytes > 0 ? Fmt.Bytes(a.SizeBytes) : "-"); it.SubItems.Add(a.Kind); it.SubItems.Add(a.Location);
            e.Item = it;
        }

        List<InstalledApp> Sel() { var r = new List<InstalledApp>(); foreach (int i in lv.SelectedIndices) if (i >= 0 && i < rows.Count) r.Add(rows[i]); return r; }

        ContextMenuStrip BuildMenu()
        {
            var m = Ui.Menu();
            m.Items.Add("Uninstall\tDel", null, delegate { Uninstall(false); });
            var quiet = m.Items.Add("Quiet uninstall", null, delegate { Uninstall(true); });
            m.Items.Add("Force remove (uninstaller broken)", null, delegate { ForceRemove(); });
            m.Items.Add("Scan leftovers", null, delegate { var a = Sel().FirstOrDefault(); if (a != null) ScanLeftovers(a, false); });
            m.Items.Add(new ToolStripSeparator());
            var openF = m.Items.Add("Open install folder", null, delegate { var a = Sel().FirstOrDefault(); if (a != null && Directory.Exists(a.Location)) Hidden.Open("explorer.exe", "\"" + a.Location + "\""); });
            Term.AddMenu(m.Items, () => { var a = Sel().FirstOrDefault(); return a != null && Directory.Exists(a.Location) ? a.Location : null; });
            var reg = m.Items.Add("Open in Registry Editor", null, delegate { var a = Sel().FirstOrDefault(); if (a != null && a.KeyPath.Length > 0) RegEdit.Open(a.KeyPath); });
            m.Items.Add("Search the web", null, delegate { var a = Sel().FirstOrDefault(); if (a != null) Hidden.Open("https://www.bing.com/search?q=" + Uri.EscapeDataString(a.Name + " uninstall")); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Copy name", null, delegate { App.I.CopyText(string.Join("\n", Sel().Select(a => a.Name))); });
            m.Items.Add("Copy uninstall command", null, delegate { var a = Sel().FirstOrDefault(); if (a != null) App.I.CopyText(a.Kind == "Store" ? "Remove-AppxPackage -Package '" + a.PackageFullName + "'" : a.UninstallCmd); });
            m.Items.Add("Properties", null, delegate { var a = Sel().FirstOrDefault(); if (a != null) Props(a); });
            m.Opening += delegate
            {
                var a = Sel().FirstOrDefault();
                quiet.Enabled = a != null && a.QuietCmd.Length > 0;
                openF.Enabled = a != null && Directory.Exists(a.Location);
                reg.Enabled = a != null && a.KeyPath.Length > 0;
            };
            Ui.ThemeMenu(m);
            return m;
        }

        void Props(InstalledApp a)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Name: " + a.Name).AppendLine("Publisher: " + a.Publisher).AppendLine("Version: " + a.Version).AppendLine("Type: " + a.Kind)
              .AppendLine("Installed: " + (a.Installed == DateTime.MinValue ? "-" : a.Installed.ToString("yyyy-MM-dd"))).AppendLine("Size: " + (a.SizeBytes > 0 ? Fmt.Bytes(a.SizeBytes) : "-"))
              .AppendLine("Location: " + a.Location).AppendLine("Uninstall: " + a.UninstallCmd).AppendLine("Quiet uninstall: " + a.QuietCmd)
              .AppendLine("MSI: " + (a.IsMsi ? a.MsiCode : "no")).AppendLine("Registry: " + a.KeyPath).AppendLine("Package: " + (a.PackageFullName ?? ""));
            MessageBox.Show(FindForm(), sb.ToString(), a.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Uninstall(bool quiet)
        {
            var sel = Sel(); if (sel.Count == 0 || busy) return;
            if (sel.Any(a => a.Name.IndexOf(App.Name, StringComparison.OrdinalIgnoreCase) >= 0)) { status("This app cannot uninstall itself."); return; }
            if (MessageBox.Show(FindForm(), "Uninstall " + sel.Count + " program(s)?\n\n" + string.Join("\n", sel.Select(a => "• " + a.Name).Take(15)) + "\n\nEach uninstaller runs; then a leftover scan opens.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            busy = true;
            var snapshot = all.ToList();
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var a in sel)
                {
                    var app = a;
                    UI(() => summary.Text = "Uninstalling " + app.Name + " ... (finish the uninstaller window)");
                    string err = Programs.RunUninstaller(a, quiet);
                    bool still = Programs.StillInstalled(a);
                    List<Leftover> left = null;
                    try { left = Leftovers.Scan(a, snapshot, false); } catch (Exception ex) { App.Log("Leftovers: " + ex.Message); }
                    UI(() =>
                    {
                        if (err != null) status("Could not start the uninstaller of " + app.Name + ": " + err + " - try Force remove.");
                        else status(still ? app.Name + " is still installed (cancelled?). Leftover scan opened anyway." : app.Name + " was uninstalled. Check its leftovers.");
                        if (left != null) new LeftoverForm(app, left).Show(FindForm());
                    });
                }
                UI(() => { busy = false; Reload(); });
            });
        }

        void ForceRemove()
        {
            var a = Sel().FirstOrDefault(); if (a == null) return;
            if (MessageBox.Show(FindForm(), "Force remove " + a.Name + "?\n\nThe uninstaller is skipped. A leftover scan opens, including the program's uninstall entry, so you can remove its folders and registry keys.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            ScanLeftovers(a, true);
        }

        void ScanLeftovers(InstalledApp a, bool includeKey)
        {
            status("Scanning leftovers of " + a.Name + " ...");
            var snapshot = all.ToList();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Leftover> left;
                try { left = Leftovers.Scan(a, snapshot, includeKey); } catch (Exception ex) { App.Log("Leftovers: " + ex.Message); left = new List<Leftover>(); }
                UI(() => { status(left.Count + " leftover item(s) found for " + a.Name + "."); new LeftoverForm(a, left).Show(FindForm()); });
            });
        }

        void ComputeSizes()
        {
            var todo = all.Where(a => a.SizeBytes <= 0 && a.Location.Length > 3).ToList();
            summary.Text = "Measuring " + todo.Count + " install folders...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var a in todo) { if (IsDisposed) return; try { if (Directory.Exists(a.Location)) a.SizeBytes = Leftovers.Size(a.Location); } catch { } }
                UI(Rebuild);
            });
        }

        void ExportCsv()
        {
            using (var d = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "installed programs " + DateTime.Now.ToString("yyyy-MM-dd") + ".csv" })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                Func<string, string> q = s => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                var sb = new StringBuilder("Name,Publisher,Version,Installed,SizeBytes,Type,Location,Uninstall\r\n");
                foreach (var a in rows) sb.Append(string.Join(",", new[] { q(a.Name), q(a.Publisher), q(a.Version), q(a.Installed == DateTime.MinValue ? "" : a.Installed.ToString("yyyy-MM-dd")), a.SizeBytes.ToString(), q(a.Kind), q(a.Location), q(a.UninstallCmd) })).Append("\r\n");
                try { File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true)); status("Exported " + rows.Count + " programs."); } catch (Exception ex) { status("Export failed: " + ex.Message); }
            }
        }
    }
}
