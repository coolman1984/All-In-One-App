using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    static class PowerTools
    {
        // ---------- Windows ----------
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] static extern bool LockWorkStation();

        public class Win { public IntPtr Handle; public string Title, Process; public bool OnTop; public int Pid; }

        public static List<Win> Windows()
        {
            var list = new List<Win>(); var sb = new StringBuilder(512);
            uint me = (uint)Process.GetCurrentProcess().Id;
            var names = new Dictionary<uint, string>();
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetWindow(h, 4 /* GW_OWNER */) != IntPtr.Zero) return true;
                int ex = GetWindowLong(h, -20);
                if ((ex & 0x80) != 0) return true; // tool window
                sb.Length = 0; if (GetWindowText(h, sb, 512) == 0) return true;
                uint pid; GetWindowThreadProcessId(h, out pid);
                string pn;
                if (!names.TryGetValue(pid, out pn)) { try { pn = Process.GetProcessById((int)pid).ProcessName; } catch { pn = "?"; } names[pid] = pn; }
                if (sb.ToString() == "Program Manager") return true;
                list.Add(new Win { Handle = h, Title = sb.ToString(), Process = pn, Pid = (int)pid, OnTop = (ex & 0x8) != 0 });
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool IsTopMost(IntPtr h) { return (GetWindowLong(h, -20) & 0x8) != 0; }
        public static void SetTopMost(IntPtr h, bool on) { SetWindowPos(h, on ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x1 | 0x2 | 0x10); }

        // Hotkey: toggle "always on top" for the window you are in.
        public static void ToggleForeground()
        {
            var h = GetForegroundWindow(); if (h == IntPtr.Zero) return;
            bool on = !IsTopMost(h); SetTopMost(h, on);
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            App.I.Balloon(on ? "Always on top: ON" : "Always on top: OFF", sb.ToString());
        }

        public static void SetOpacity(IntPtr h, int percent)
        {
            int ex = GetWindowLong(h, -20);
            if (percent >= 100) { SetWindowLong(h, -20, ex & ~0x80000); return; }
            SetWindowLong(h, -20, ex | 0x80000);
            SetLayeredWindowAttributes(h, 0, (byte)(255 * Math.Max(20, percent) / 100), 0x2);
        }

        public static void Lock() { LockWorkStation(); }

        // ---------- Who is using this file (Restart Manager) ----------
        [StructLayout(LayoutKind.Sequential)] struct RM_UNIQUE_PROCESS { public int dwProcessId; public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string strServiceShortName;
            public int ApplicationType; public uint AppStatus; public uint TSSessionId; [MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmStartSession(out uint session, int flags, StringBuilder key);
        [DllImport("rstrtmgr.dll")] static extern int RmEndSession(uint session);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] static extern int RmRegisterResources(uint session, uint nFiles, string[] files, uint nApps, IntPtr apps, uint nServices, string[] services);
        [DllImport("rstrtmgr.dll")] static extern int RmGetList(uint session, out uint needed, ref uint count, [In, Out] RM_PROCESS_INFO[] info, ref uint reasons);

        public class Locker { public int Pid; public string App, Process, Path; }

        public static List<Locker> Lockers(string path)
        {
            var files = new List<string>();
            if (Directory.Exists(path)) { try { files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(400)); } catch { } }
            else files.Add(path);
            var result = new List<Locker>();
            foreach (var chunk in files.Select((f, i) => new { f, i }).GroupBy(x => x.i / 100).Select(g => g.Select(x => x.f).ToArray()))
            {
                uint session; var key = new StringBuilder(64);
                if (RmStartSession(out session, 0, key) != 0) continue;
                try
                {
                    if (RmRegisterResources(session, (uint)chunk.Length, chunk, 0, IntPtr.Zero, 0, null) != 0) continue;
                    uint needed, count = 0, reasons = 0;
                    int r = RmGetList(session, out needed, ref count, null, ref reasons);
                    if (r == 234 && needed > 0)
                    {
                        var info = new RM_PROCESS_INFO[needed]; count = needed;
                        if (RmGetList(session, out needed, ref count, info, ref reasons) == 0)
                            for (int i = 0; i < count; i++)
                            {
                                int pid = info[i].Process.dwProcessId;
                                if (result.Any(x => x.Pid == pid)) continue;
                                string pn = "", pp = "";
                                try { var p = Process.GetProcessById(pid); pn = p.ProcessName; try { pp = p.MainModule.FileName; } catch { } } catch { }
                                result.Add(new Locker { Pid = pid, App = info[i].strAppName, Process = pn, Path = pp });
                            }
                    }
                }
                finally { RmEndSession(session); }
            }
            return result;
        }

        public static void WhoUses(string path, IWin32Window owner)
        {
            using (var f = new WhoUsesForm(path)) f.ShowDialog(owner);
        }

        // ---------- hosts ----------
        public static string HostsPath { get { return Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts"); } }
    }

    class WhoUsesForm : Form
    {
        readonly FastList lv;
        readonly Label info;
        readonly TextBox path;
        List<PowerTools.Locker> rows = new List<PowerTools.Locker>();

        public WhoUsesForm(string start)
        {
            AutoScaleMode = AutoScaleMode.None; Text = "What is using this file - " + App.Name; Icon = App.AppIcon; Font = Ui.Base;
            ClientSize = Ui.S(640, 380); StartPosition = FormStartPosition.CenterParent; BackColor = Ui.Card; ForeColor = Ui.Text; AllowDrop = true;
            Ui.DarkTitle(this);
            lv = new FastList { Dock = DockStyle.Fill, VirtualMode = true, MultiSelect = false };
            lv.Columns.Add("Program", Ui.S(200)); lv.Columns.Add("PID", Ui.S(60)); lv.Columns.Add("Path", Ui.S(340)); lv.FillIndex = 2;
            lv.RetrieveVirtualItem += (s, e) =>
            {
                var r = e.ItemIndex < rows.Count ? rows[e.ItemIndex] : new PowerTools.Locker();
                var it = new ListViewItem(string.IsNullOrEmpty(r.App) ? r.Process : r.App); it.SubItems.Add(r.Pid.ToString()); it.SubItems.Add(r.Path ?? ""); e.Item = it;
            };
            var m = Ui.Menu();
            m.Items.Add("End this program", null, delegate { Kill(); });
            m.Items.Add("Open its folder", null, delegate { var r = Sel(); if (r != null && File.Exists(r.Path)) Hidden.Open("explorer.exe", "/select,\"" + r.Path + "\""); });
            m.Items.Add("Copy name", null, delegate { var r = Sel(); if (r != null) App.I.CopyText(r.Process); });
            Ui.ThemeMenu(m); lv.ContextMenuStrip = m;
            Controls.Add(lv);
            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = Ui.S(40), ColumnCount = 3, Padding = new Padding(Ui.S(6)) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            path = new FluentTextBox { Dock = DockStyle.Fill, Text = start ?? "" };
            path.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Scan(); } };
            top.Controls.Add(path);
            top.Controls.Add(Ui.Btn("Browse...", delegate { using (var d = new OpenFileDialog()) if (d.ShowDialog(this) == DialogResult.OK) { path.Text = d.FileName; Scan(); } }));
            top.Controls.Add(Ui.Btn("Check", delegate { Scan(); }, true));
            Controls.Add(top);
            var bottom = LayoutKit.Actions(DockStyle.Bottom);
            bottom.Controls.Add(Ui.Btn("End selected program", delegate { Kill(); }));
            bottom.Controls.Add(Ui.Btn("Close", delegate { Close(); }));
            info = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(8), Ui.S(8), 0, 0), Text = "Drop a file or folder here, or type a path." };
            bottom.Controls.Add(info);
            Controls.Add(bottom);
            DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null && f.Length > 0) { path.Text = f[0]; Scan(); } };
            Ui.Theme(this);
            Shown += delegate { if (path.Text.Length > 0) Scan(); };
        }

        PowerTools.Locker Sel() { return lv.SelectedIndices.Count > 0 && lv.SelectedIndices[0] < rows.Count ? rows[lv.SelectedIndices[0]] : null; }

        void Scan()
        {
            string p = path.Text.Trim().Trim('"');
            if (!File.Exists(p) && !Directory.Exists(p)) { info.Text = "Path not found."; return; }
            info.Text = "Checking...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<PowerTools.Locker> l; string err = null;
                try { l = PowerTools.Lockers(p); } catch (Exception ex) { l = new List<PowerTools.Locker>(); err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        rows = l; lv.VirtualListSize = rows.Count; lv.Invalidate();
                        info.Text = err ?? (rows.Count == 0 ? "Nothing is using it. You can move or delete it." : rows.Count + " program(s) are using it." + (Admin.IsAdmin ? "" : " (Run as admin to also see system programs.)"));
                    }));
                }
                catch { }
            });
        }

        void Kill()
        {
            var r = Sel(); if (r == null) return;
            if (MessageBox.Show(this, "End " + r.Process + " (PID " + r.Pid + ")? Unsaved work in it is lost.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                Process.GetProcessById(r.Pid).Kill();
                var tm = new System.Windows.Forms.Timer { Interval = 300 };
                tm.Tick += delegate { tm.Stop(); tm.Dispose(); if (!IsDisposed) Scan(); };
                tm.Start();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, App.Name); }
        }
    }

    class QuickRunForm : Form
    {
        static QuickRunForm open;
        readonly TextBox box;
        readonly ListBox hints;
        List<string[]> items = new List<string[]>();

        public static void Open()
        {
            if (open != null && !open.IsDisposed) { open.Activate(); return; }
            open = new QuickRunForm(); open.Show(); open.Activate(); Native.SetForegroundWindow(open.Handle);
        }

        QuickRunForm()
        {
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            BackColor = Ui.Card; ForeColor = Ui.Text; Font = Ui.Base; StartPosition = FormStartPosition.Manual;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = Ui.S(560, 330); Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + wa.Height / 5);
            Padding = new Padding(Ui.S(10));
            box = new FluentTextBox { Dock = DockStyle.Top, Font = new Font(Ui.FontName, 14f) };
            hints = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Ui.Card, ForeColor = Ui.Text, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(28) };
            hints.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= items.Count) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? Ui.SelBg : Ui.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, items[e.Index][0], Ui.Bold, new Rectangle(e.Bounds.X + Ui.S(6), e.Bounds.Y, e.Bounds.Width / 2, e.Bounds.Height), Ui.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, items[e.Index][1], Ui.Small, new Rectangle(e.Bounds.X + e.Bounds.Width / 2, e.Bounds.Y, e.Bounds.Width / 2 - Ui.S(6), e.Bounds.Height), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.PathEllipsis);
            };
            hints.DoubleClick += delegate { Go(); };
            var help = new Label { Dock = DockStyle.Bottom, Height = Ui.S(22), ForeColor = Ui.Muted, Font = Ui.Small, Text = "Program, folder, file, web address, = math, ? web search, > command   ·   Enter run · Ctrl+Enter as admin · Esc close" };
            Controls.Add(hints); Controls.Add(help); Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(6) }); Controls.Add(box);
            box.TextChanged += delegate { Fill(); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(e.Control); }
                else if (e.KeyCode == Keys.Down && hints.Items.Count > 0) { hints.SelectedIndex = Math.Min(hints.Items.Count - 1, hints.SelectedIndex + 1); e.Handled = true; }
                else if (e.KeyCode == Keys.Up && hints.Items.Count > 0) { hints.SelectedIndex = Math.Max(0, hints.SelectedIndex - 1); e.Handled = true; }
            };
            Deactivate += delegate { if (!IsDisposed) Close(); };
            Paint += (s, e) => { using (var p = new Pen(Ui.Accent, 2)) e.Graphics.DrawRectangle(p, 1, 1, Width - 2, Height - 2); };
            Ui.Theme(this);
            Fill();
        }

        void Fill()
        {
            string q = box.Text.Trim();
            var l = new List<string[]>();
            if (q.StartsWith("="))
            {
                string r; try { r = Convert.ToString(new System.Data.DataTable().Compute(q.Substring(1), null), System.Globalization.CultureInfo.InvariantCulture); } catch { r = "..."; }
                l.Add(new[] { "= " + r, "Enter copies the result", "calc:" + r });
            }
            else if (q.StartsWith("?")) l.Add(new[] { "Search the web: " + q.Substring(1).Trim(), "Google in Chrome", "web:" + q.Substring(1).Trim() });
            else if (q.StartsWith(">")) l.Add(new[] { "Run command: " + q.Substring(1).Trim(), "cmd /k", "cmd:" + q.Substring(1).Trim() });
            else
            {
                if (q.Length > 0) l.Add(new[] { q, "Run", "run:" + q });
                foreach (var d in Store.Settings.Dock.Where(d => q.Length == 0 || d.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(8)) l.Add(new[] { d.Name, d.Path, "dock:" + Store.Settings.Dock.IndexOf(d) });
                foreach (var t in Tools.Where(t => q.Length > 0 && t[0].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(8)) l.Add(new[] { t[0], t[1], "run:" + t[1] });
                if (q.Length > 1 && !q.Contains(" ") && q.Contains(".")) l.Add(new[] { "Open " + q, "web address", "url:" + q });
            }
            items = l;
            hints.BeginUpdate(); hints.Items.Clear(); foreach (var x in l) hints.Items.Add(x[0]); hints.EndUpdate();
            if (hints.Items.Count > 0) hints.SelectedIndex = 0;
        }

        static readonly string[][] Tools =
        {
            new[] { "Task Manager", "taskmgr.exe" }, new[] { "Control Panel", "control.exe" }, new[] { "Settings", "ms-settings:" },
            new[] { "Device Manager", "devmgmt.msc" }, new[] { "Services", "services.msc" }, new[] { "Registry Editor", "regedit.exe" },
            new[] { "Notepad", "notepad.exe" }, new[] { "Calculator", "calc.exe" }, new[] { "Paint", "mspaint.exe" }, new[] { "Command prompt", "cmd.exe" },
            new[] { "PowerShell", "powershell.exe" }, new[] { "Terminal", "wt.exe" }, new[] { "Disk Management", "diskmgmt.msc" }, new[] { "Event Viewer", "eventvwr.msc" },
            new[] { "Network connections", "ncpa.cpl" }, new[] { "Sound", "mmsys.cpl" }, new[] { "Power options", "powercfg.cpl" }, new[] { "Programs and Features", "appwiz.cpl" },
            new[] { "System properties", "sysdm.cpl" }, new[] { "Environment variables", "rundll32.exe sysdm.cpl,EditEnvironmentVariables" }, new[] { "Display settings", "ms-settings:display" },
            new[] { "Wi-Fi settings", "ms-settings:network-wifi" }, new[] { "Bluetooth", "ms-settings:bluetooth" }, new[] { "Windows Update", "ms-settings:windowsupdate" },
            new[] { "Date and time", "ms-settings:dateandtime" }, new[] { "Resource Monitor", "resmon.exe" }, new[] { "Snipping Tool", "snippingtool.exe" }, new[] { "Character Map", "charmap.exe" },
            new[] { "Task Scheduler", "taskschd.msc" }, new[] { "Computer Management", "compmgmt.msc" }, new[] { "Firewall", "wf.msc" }, new[] { "Downloads folder", "shell:Downloads" },
            new[] { "Startup folder", "shell:startup" }, new[] { "Temp folder", "%TEMP%" }, new[] { "AppData", "%APPDATA%" }, new[] { "Recycle Bin", "shell:RecycleBinFolder" },
        };

        void Go(bool admin = false)
        {
            int i = hints.SelectedIndex; if (i < 0 || i >= items.Count) return;
            string a = items[i][2]; int c = a.IndexOf(':'); string kind = a.Substring(0, c), v = a.Substring(c + 1);
            Close();
            try
            {
                switch (kind)
                {
                    case "calc": App.I.CopyText(v); break;
                    case "web": Hidden.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(v)); break;
                    case "url": Hidden.Open(v.Contains("://") ? v : "https://" + v); break;
                    case "cmd": Process.Start(new ProcessStartInfo("cmd.exe", "/k " + v) { UseShellExecute = true, Verb = admin ? "runas" : "" }); break;
                    case "dock": QuickDock.Launch(Store.Settings.Dock[int.Parse(v)]); break;
                    default:
                        string file = Environment.ExpandEnvironmentVariables(v), args = "";
                        if (!File.Exists(file) && !Directory.Exists(file) && file.Contains(" ") && !file.Contains("://")) { int s = file.IndexOf(' '); args = file.Substring(s + 1); file = file.Substring(0, s); }
                        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, Verb = admin ? "runas" : "" });
                        break;
                }
            }
            catch (Exception ex) { App.I.Balloon("Could not run", ex.Message); }
        }
    }

    class PowerPage : Panel
    {
        readonly TabStrip tabs;
        readonly Panel host;
        readonly Control[] views = new Control[4];

        public PowerPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, Ui.S(4), 0, 0) };
            host.Layout += delegate { Arrange(); };
            Controls.Add(host);
            // Tabs sit in the header bar instead of a row of their own.
            tabs = new TabStrip { Dock = DockStyle.None, AutoSize = true, Margin = new Padding(0) };
            tabs.Add("Windows (always on top)"); tabs.Add("Quick actions"); tabs.Add("Hosts file"); tabs.Add("What is using a file");
            tabs.Changed += Show;
            Resize += delegate { if (Wide != split) Show(selected); };
            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Power tools"));
            bar.Controls.Add(tabs);
            bar.Controls.Add(Ui.Btn("Quick run  " + Hotkeys.Get("run"), delegate { QuickRunForm.Open(); }, true));
            Controls.Add(bar);
            Ui.Theme(this);
            tabs.Select(0);
        }

        public void SelectTab(int i) { tabs.Select(i); }

        // Wide windows: the window list stays on the left and the chosen tool fills the right (its tab is hidden then).
        int selected, rightTab = 1;
        bool split;
        bool Wide { get { return Width >= Ui.S(1400); } }

        Control View(int i)
        {
            if (views[i] == null)
            {
                views[i] = i == 0 ? WindowsView() : i == 1 ? ActionsView() : i == 2 ? HostsView() : WhoView();
                views[i].Dock = DockStyle.None; views[i].Visible = false; host.Controls.Add(views[i]); Ui.Theme(views[i]);
            }
            return views[i];
        }

        void Show(int i)
        {
            selected = i;
            if (i > 0) rightTab = i;
            split = Wide;
            tabs.ShowTab(0, !split);
            if (split && tabs.Selected != rightTab) { tabs.Select(rightTab); return; }
            bool wasVisible0 = views[0] != null && views[0].Visible;
            SuspendLayout();
            if (split) { View(0); View(rightTab); } else View(i);
            for (int k = 0; k < views.Length; k++) if (views[k] != null) views[k].Visible = split ? (k == 0 || k == rightTab) : k == i;
            ResumeLayout(true);
            host.PerformLayout();
            if (views[0] != null && views[0].Visible && !wasVisible0) { var r = views[0].Tag as Action; if (r != null) r(); }
        }

        void Arrange()
        {
            var r = host.DisplayRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            if (split && views[0] != null && views[rightTab] != null)
            {
                int gap = Ui.S(16), left = (r.Width - gap) * 48 / 100;
                Fit(views[0], new Rectangle(r.X, r.Y, left, r.Height));
                Fit(views[rightTab], new Rectangle(r.X + left + gap, r.Y, r.Width - left - gap, r.Height));
            }
            else if (views[selected] != null) Fit(views[selected], r);
        }

        static void Fit(Control c, Rectangle r) { if (c.Bounds != r) c.Bounds = r; }

        Control WindowsView()
        {
            var p = new Panel();
            var rows = new List<PowerTools.Win>();
            var lv = new FastList { Dock = DockStyle.Fill, VirtualMode = true, MultiSelect = true, CheckBoxes = false };
            lv.Columns.Add("On top", Ui.S(64)); lv.Columns.Add("Window", Ui.S(420)); lv.Columns.Add("Program", Ui.S(160)); lv.FillIndex = 1;
            lv.RetrieveVirtualItem += (s, e) =>
            {
                var w = e.ItemIndex < rows.Count ? rows[e.ItemIndex] : new PowerTools.Win { Title = "", Process = "" };
                var it = new ListViewItem(w.OnTop ? "✔ ON" : ""); it.SubItems.Add(w.Title); it.SubItems.Add(w.Process);
                if (w.OnTop) it.ForeColor = Ui.Accent;
                e.Item = it;
            };
            Action reload = () => { rows = PowerTools.Windows(); lv.VirtualListSize = rows.Count; lv.Invalidate(); };
            Func<List<PowerTools.Win>> sel = () => { var l = new List<PowerTools.Win>(); foreach (int i in lv.SelectedIndices) if (i < rows.Count) l.Add(rows[i]); return l; };
            Action<bool?> top = on => { foreach (var w in sel()) PowerTools.SetTopMost(w.Handle, on ?? !PowerTools.IsTopMost(w.Handle)); reload(); };
            lv.DoubleClick += delegate { top(null); };
            var m = Ui.Menu();
            m.Items.Add("Toggle always on top", null, delegate { top(null); });
            m.Items.Add("Always on top: ON", null, delegate { top(true); });
            m.Items.Add("Always on top: OFF", null, delegate { top(false); });
            var op = new ToolStripMenuItem("See-through");
            foreach (int pc in new[] { 100, 90, 80, 70, 50, 30 }) { int v = pc; op.DropDownItems.Add(v + " %", null, delegate { foreach (var w in sel()) PowerTools.SetOpacity(w.Handle, v); }); }
            m.Items.Add(op);
            m.Items.Add("Bring to front", null, delegate { var w = sel().FirstOrDefault(); if (w != null) Native.SetForegroundWindow(w.Handle); });
            m.Items.Add("Copy title", null, delegate { App.I.CopyText(string.Join("\n", sel().Select(w => w.Title))); });
            m.Items.Add("Refresh", null, delegate { reload(); });
            Ui.ThemeMenu(m); lv.ContextMenuStrip = m;
            p.Controls.Add(lv);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, Ui.S(4), 0, Ui.S(4)) };
            bar.Controls.Add(Ui.Btn("Toggle on top", delegate { top(null); }, true));
            bar.Controls.Add(Ui.Btn("Refresh", delegate { reload(); }));
            bar.Controls.Add(new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(8), Ui.S(9), 0, 0), Text = "Double-click a window to pin it on top. Anywhere: " + Hotkeys.Get("ontop") + " pins the window you are in." });
            p.Controls.Add(bar);
            p.Tag = reload;
            return p;
        }

        Control ActionsView()
        {
            var outer = new Panel { AutoScroll = true };
            var flow = new TileHost { Dock = DockStyle.Top, Height = Ui.S(100) };
            outer.Controls.Add(flow);
            Action<string, string, string, Action> tile = (glyph, title, sub, act) =>
            {
                var b = new Button { Size = Ui.S(220, 84), FlatStyle = FlatStyle.Flat, BackColor = Ui.Card, ForeColor = Ui.Text, Margin = new Padding(Ui.S(4)), TextAlign = ContentAlignment.MiddleLeft, ImageAlign = ContentAlignment.MiddleLeft, TextImageRelation = TextImageRelation.ImageBeforeText, Padding = new Padding(Ui.S(8), 0, 0, 0) };
                b.FlatAppearance.BorderColor = Ui.Line; b.FlatAppearance.MouseOverBackColor = Ui.HoverBg;
                b.Image = Ui.GlyphImage(glyph, Ui.S(26), Ui.Accent);
                b.Text = "  " + title + "\n  " + sub; b.Font = Ui.Base;
                b.Click += delegate { try { act(); } catch (Exception ex) { App.I.Balloon("Failed", ex.Message); } };
                flow.Controls.Add(b);
            };
            tile("", "Keep awake", "Toggle: no sleep", () => Awake.Set(!Awake.On));
            tile("", "Wi-Fi on / off", "Toggle the Wi-Fi radio", () => WiFi.Toggle());
            tile("", "Date and time", "Windows settings", () => DateTimeTools.OpenSettings());
            tile("", "Sync the clock now", "Needs admin", () => DateTimeTools.SyncNow());
            tile("", "Lock the PC", "Win+L", () => PowerTools.Lock());
            tile("", "Sleep", "Suspend now", () => Application.SetSuspendState(PowerState.Suspend, false, false));
            tile("", "Empty Recycle Bin", "Asks Windows first", () => SHEmptyRecycleBin(Handle, null, 0));
            tile("", "Flush DNS", "ipconfig /flushdns", () => Hidden.Async(() => Hidden.Run("ipconfig.exe", "/flushdns", 15000), r => App.I.Balloon("DNS cache", "Flushed.")));
            tile("", "Restart Explorer", "Fix a stuck taskbar", () =>
            {
                if (MessageBox.Show(FindForm(), "Restart Windows Explorer (taskbar and folders)? Open folder windows close.", App.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                Hidden.Async(() => { Hidden.Run("taskkill.exe", "/f /im explorer.exe", 10000); Thread.Sleep(800); Process.Start("explorer.exe"); return ""; }, null);
            });
            tile("", "Clear temp files", "Your Temp folder, to the bin", () =>
            {
                string t = Path.GetTempPath(); var items = new List<string>();
                try { items.AddRange(Directory.GetFileSystemEntries(t)); } catch { }
                if (items.Count == 0 || MessageBox.Show(FindForm(), "Move " + items.Count + " items in your Temp folder to the Recycle Bin? Files in use are skipped.", App.Name, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                App.I.Balloon("Temp files", "Clearing " + items.Count + " items in the background...");
                ThreadPool.QueueUserWorkItem(delegate
                {
                    int ok = 0, skipped = 0;
                    foreach (var item in items)
                    {
                        if (RecycleQuiet(item)) ok++; else skipped++;
                    }
                    try { App.I.Sync.BeginInvoke(new Action(() => App.I.Balloon("Temp files", "Cleared " + ok + " items, skipped " + skipped + " in use."))); }
                    catch { }
                });
            });
            tile("", "Quick run", Hotkeys.Get("run"), () => QuickRunForm.Open());
            tile("", "Shortcuts", "All keys in one place", () => App.I.ShowMain("shortcuts"));
            tile("", "Uninstall programs", "Deep clean leftovers", () => App.I.ShowMain("system:uninstall"));
            tile("", "Chrome bookmarks", "Manage and open", () => App.I.ShowMain("chrome"));
            tile("", "Explorer panes", "Up to 4 side by side", () => App.I.ShowMain("explorer"));
            tile("", "Environment variables", "Edit for your account", () => Hidden.Open("rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"));
            tile("", "Display settings", "Resolution and scale", () => Hidden.Open("ms-settings:display"));
            tile("", "Sound", "Output and input devices", () => Hidden.Open("mmsys.cpl"));
            return outer;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr h, string root, uint flags);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TempOp { public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref TempOp op);

        // Recycle one path with no dialogs: ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI. True when it is gone.
        static bool RecycleQuiet(string path)
        {
            try
            {
                var op = new TempOp { wFunc = 3, pFrom = path + "\0\0", fFlags = 0x40 | 0x10 | 0x4 | 0x400 };
                int r = SHFileOperation(ref op);
                return r == 0 && !op.fAnyOperationsAborted;
            }
            catch { return false; }
        }

        // Tiles in an evenly stretched grid; the host sets its own height.
        class TileHost : Panel
        {
            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                int w = ClientSize.Width, n = Controls.Count;
                if (w <= 0 || n == 0) return;
                int pad = Ui.S(4), gap = Ui.S(8), th = Ui.S(84);
                int cols = Math.Max(1, Math.Min(8, w / Ui.S(240)));
                int tw = Math.Max(Ui.S(120), (w - pad * 2 - gap * (cols - 1)) / cols);
                for (int i = 0; i < n; i++)
                {
                    var c = Controls[i];
                    c.SetBounds(pad + (i % cols) * (tw + gap), pad + (i / cols) * (th + gap), tw, th);
                }
                int rows = (n + cols - 1) / cols;
                int h = pad * 2 + rows * th + (rows - 1) * gap;
                if (Height != h) Height = h;
            }
        }

        Control HostsView()
        {
            var p = new Panel();
            var tb = new FluentTextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 10.5f) };
            var status = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(8), Ui.S(9), 0, 0) };
            Action load = () => { try { tb.Text = File.ReadAllText(PowerTools.HostsPath).Replace("\r\n", "\n").Replace("\n", "\r\n"); status.Text = PowerTools.HostsPath + (Admin.IsAdmin ? "" : "   (read-only: saving needs admin)"); } catch (Exception ex) { status.Text = ex.Message; } };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, Ui.S(4), 0, Ui.S(4)) };
            bar.Controls.Add(Ui.Btn("Save", delegate
            {
                if (!Admin.IsAdmin)
                {
                    if (MessageBox.Show(FindForm(), "Saving the hosts file needs administrator rights. Restart the app as administrator now?", App.Name, MessageBoxButtons.YesNo) == DialogResult.Yes) App.I.RestartElevated();
                    return;
                }
                try
                {
                    string dir = Path.Combine(Store.Dir, "hosts-backups"); Directory.CreateDirectory(dir);
                    File.Copy(PowerTools.HostsPath, Path.Combine(dir, "hosts " + DateTime.Now.ToString("yyyy-MM-dd HHmmss")), true);
                    File.WriteAllText(PowerTools.HostsPath, tb.Text, new UTF8Encoding(false));
                    int x; Hidden.Run("ipconfig.exe", "/flushdns", 10000, out x);
                    status.Text = "Saved. Backup in " + dir;
                }
                catch (Exception ex) { MessageBox.Show(FindForm(), "Save failed: " + ex.Message, App.Name); }
            }, true));
            bar.Controls.Add(Ui.Btn("Reload", delegate { load(); }));
            bar.Controls.Add(Ui.Btn("Add block line", delegate { var h = Ui.Ask("Block a site", "Site name (e.g. example.com)", "", FindForm()); if (!string.IsNullOrEmpty(h)) tb.AppendText("\r\n0.0.0.0 " + h.Trim()); }));
            bar.Controls.Add(Ui.Btn("Backups", delegate { string dir = Path.Combine(Store.Dir, "hosts-backups"); Directory.CreateDirectory(dir); Hidden.Open(dir); }));
            bar.Controls.Add(status);
            p.Controls.Add(tb); p.Controls.Add(bar);
            load();
            return p;
        }

        Control WhoView()
        {
            var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Padding = new Padding(Ui.S(8)), AllowDrop = true };
            p.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(Ui.S(700), 0), Text = "Windows says \"the file is open in another program\"? Find out which program holds it, then close that program.\n\nDrop a file or folder here, or pick one:" });
            p.Controls.Add(Ui.Btn("Pick a file...", delegate { using (var d = new OpenFileDialog()) if (d.ShowDialog(FindForm()) == DialogResult.OK) PowerTools.WhoUses(d.FileName, FindForm()); }, true));
            p.Controls.Add(Ui.Btn("Pick a folder...", delegate { using (var d = new FolderBrowserDialog()) if (d.ShowDialog(FindForm()) == DialogResult.OK) PowerTools.WhoUses(d.SelectedPath, FindForm()); }));
            p.DragEnter += (s, e) => e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            p.DragDrop += (s, e) => { var f = e.Data.GetData(DataFormats.FileDrop) as string[]; if (f != null && f.Length > 0) PowerTools.WhoUses(f[0], FindForm()); };
            return p;
        }
    }

    // One place for every shortcut: this app's global keys, your own launch keys, in-app keys and Windows keys.
    class ShortcutsPage : Panel
    {
        readonly TabStrip tabs;
        readonly Panel host;
        readonly Control[] views = new Control[4];
        public ShortcutsPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            host = new Panel { Dock = DockStyle.Fill }; Controls.Add(host);
            tabs = new TabStrip();
            tabs.Add("App shortcuts (global)"); tabs.Add("My launch shortcuts"); tabs.Add("Keys inside the app"); tabs.Add("Windows shortcuts");
            tabs.Changed += i =>
            {
                if (views[i] == null) { views[i] = i == 0 ? AppKeys() : i == 1 ? Launchers() : i == 2 ? Reference(InApp) : Reference(WindowsKeys); views[i].Dock = DockStyle.Fill; host.Controls.Add(views[i]); Ui.Theme(views[i]); }
                for (int k = 0; k < 4; k++) if (views[k] != null) views[k].Visible = k == i;
            };
            Controls.Add(tabs);
            var bar = MainForm.Bar(); bar.Controls.Add(MainForm.Heading("Shortcuts")); Controls.Add(bar);
            Ui.Theme(this);
            tabs.Select(0);
        }

        public void SelectTab(int i) { tabs.Select(i); }

        Control AppKeys()
        {
            var p = new Panel();
            // Constrained to a sensible width: the table does not stretch across a wide window.
            var t = new TableLayoutPanel { Dock = DockStyle.Left, ColumnCount = 3, AutoScroll = true, Padding = new Padding(Ui.S(8)), Width = Ui.S(320 + 200 + 190 + 16 + 24) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(320))); t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(200))); t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(190)));
            var boxes = new Dictionary<string, HotkeyBox>();
            foreach (var a in Hotkeys.Actions)
            {
                var l = new Label { Text = a[1], AutoSize = true, Margin = new Padding(0, Ui.S(8), Ui.S(8), 0) };
                var hb = new HotkeyBox { Text = Hotkeys.Get(a[0]), Width = Ui.S(190), Margin = new Padding(0, Ui.S(3), 0, Ui.S(3)) };
                boxes[a[0]] = hb;
                var d = new Label { Text = "default " + a[2], AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(Ui.S(8), Ui.S(9), 0, 0) };
                t.Controls.Add(l); t.Controls.Add(hb); t.Controls.Add(d);
            }
            p.Controls.Add(t);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, Ui.S(4), 0, Ui.S(4)) };
            var status = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(8), Ui.S(9), 0, 0), Text = "Click a box and press the keys. Backspace turns a shortcut off." };
            bar.Controls.Add(Ui.Btn("Save shortcuts", delegate
            {
                var seen = new Dictionary<string, string>();
                var launcherKeys = new Dictionary<string, string>();
                foreach (var u in Store.Settings.Launchers)
                {
                    uint lm; Keys lk;
                    if (u == null || string.IsNullOrEmpty(u.Keys) || !Hotkeys.Parse(u.Keys, out lm, out lk)) continue;
                    launcherKeys[lm + ":" + (int)lk] = string.IsNullOrEmpty(u.Name) ? u.Keys : u.Name;
                }
                foreach (var a in Hotkeys.Actions)
                {
                    string k = boxes[a[0]].Text.Trim(); if (k.Length == 0) continue;
                    uint mods; Keys key; if (!Hotkeys.Parse(k, out mods, out key)) { status.Text = k + " is not valid."; return; }
                    string id = mods + ":" + (int)key, other;
                    if (seen.TryGetValue(id, out other)) { status.Text = k + " is used twice: " + other + " and " + a[1]; return; }
                    if (launcherKeys.TryGetValue(id, out other)) { status.Text = k + " is already used by launcher " + other; return; }
                    seen[id] = a[1];
                }
                Store.Settings.Hotkeys = Hotkeys.Actions.ToDictionary(a => a[0], a => boxes[a[0]].Text.Trim());
                Store.MarkSettings();
                var failed = App.I.RegisterHotkeys();
                status.Text = failed.Count == 0 ? "Saved. All shortcuts work." : "Saved, but used by another app: " + string.Join(", ", failed);
            }, true));
            bar.Controls.Add(Ui.Btn("Defaults", delegate { foreach (var a in Hotkeys.Actions) boxes[a[0]].Text = a[2]; }));
            bar.Controls.Add(Ui.Btn("Copy list", delegate { App.I.CopyText(string.Join("\r\n", Hotkeys.Actions.Select(a => Hotkeys.Get(a[0]) + "\t" + a[1]))); }));
            bar.Controls.Add(status);
            p.Controls.Add(bar);
            return p;
        }

        FastList launchList;
        Control Launchers()
        {
            var p = new Panel();
            var lv = launchList = new FastList { Dock = DockStyle.Fill, VirtualMode = true, MultiSelect = false };
            lv.Columns.Add("Keys", Ui.S(140)); lv.Columns.Add("Name", Ui.S(200)); lv.Columns.Add("Opens", Ui.S(360)); lv.FillIndex = 2;
            lv.RetrieveVirtualItem += (s, e) =>
            {
                var l = Store.Settings.Launchers; var u = e.ItemIndex < l.Count ? l[e.ItemIndex] : new UserShortcut();
                var it = new ListViewItem(u.Keys); it.SubItems.Add(u.Name); it.SubItems.Add(u.Target + (u.Args.Length > 0 ? " " + u.Args : "")); e.Item = it;
            };
            lv.VirtualListSize = Store.Settings.Launchers.Count;
            Func<UserShortcut> sel = () => lv.SelectedIndices.Count > 0 && lv.SelectedIndices[0] < Store.Settings.Launchers.Count ? Store.Settings.Launchers[lv.SelectedIndices[0]] : null;
            lv.DoubleClick += delegate { var u = sel(); if (u != null && Edit(u, FindForm())) Changed(); };
            var m = Ui.Menu();
            m.Items.Add("Run now", null, delegate { var u = sel(); if (u != null) Hidden.Open(u.Target, u.Args); });
            m.Items.Add("Edit...", null, delegate { var u = sel(); if (u != null && Edit(u, FindForm())) Changed(); });
            m.Items.Add("Add to quick launch", null, delegate { var u = sel(); if (u != null) QuickDock.AddItem(u.Name, u.Target, u.Args); });
            m.Items.Add("Delete", null, delegate { var u = sel(); if (u != null) { Store.Settings.Launchers.Remove(u); Changed(); } });
            Ui.ThemeMenu(m); lv.ContextMenuStrip = m;
            p.Controls.Add(lv);
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, Ui.S(4), 0, Ui.S(4)) };
            bar.Controls.Add(Ui.Btn("New shortcut...", delegate { AddLauncherFor("", "", "", FindForm()); }, true));
            bar.Controls.Add(Ui.Btn("Edit", delegate { var u = sel(); if (u != null && Edit(u, FindForm())) Changed(); }));
            bar.Controls.Add(Ui.Btn("Delete", delegate { var u = sel(); if (u != null) { Store.Settings.Launchers.Remove(u); Changed(); } }));
            bar.Controls.Add(new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(Ui.S(8), Ui.S(9), 0, 0), Text = "Your own keys that open any app, file, folder or web page from anywhere in Windows." });
            p.Controls.Add(bar);
            return p;
        }

        static void Changed()
        {
            Store.MarkSettings();
            var failed = App.I.RegisterHotkeys();
            if (failed.Count > 0) App.I.Balloon("Some keys are taken", string.Join(", ", failed) + " are used by another app.");
            foreach (var f in Application.OpenForms.OfType<MainForm>()) f.RefreshLaunchers();
        }

        public void RefreshLaunchers() { if (launchList != null) { launchList.VirtualListSize = Store.Settings.Launchers.Count; launchList.Invalidate(); } }

        public static void AddLauncherFor(string name, string target, string args, IWin32Window owner)
        {
            var u = new UserShortcut { Id = Guid.NewGuid().ToString("N"), Name = name ?? "", Target = target ?? "", Args = args ?? "" };
            if (!Edit(u, owner)) return;
            Store.Settings.Launchers.Add(u);
            Changed();
        }

        static bool Edit(UserShortcut u, IWin32Window owner)
        {
            using (var f = new Form { Text = "Launch shortcut - " + App.Name, Icon = App.AppIcon, AutoScaleMode = AutoScaleMode.None, Font = Ui.Base, ClientSize = Ui.S(520, 250), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Ui.Card, ForeColor = Ui.Text })
            {
                Ui.DarkTitle(f);
                var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(Ui.S(14)) };
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(110))); t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var keys = new HotkeyBox { Text = u.Keys, Dock = DockStyle.Fill };
                var name = new FluentTextBox { Text = u.Name, Dock = DockStyle.Fill };
                var target = new FluentTextBox { Text = u.Target, Dock = DockStyle.Fill };
                var args = new FluentTextBox { Text = u.Args, Dock = DockStyle.Fill };
                Action<string, Control, Control> row = (label, c, extra) => { t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, Ui.S(8), 0, 0) }); t.Controls.Add(c); if (extra != null) t.Controls.Add(extra); else t.Controls.Add(new Label { Width = 1 }); };
                row("Keys", keys, null);
                row("Name", name, null);
                row("Opens", target, Ui.Btn("Browse...", delegate { using (var d = new OpenFileDialog { Filter = "All files|*.*" }) if (d.ShowDialog(f) == DialogResult.OK) { target.Text = d.FileName; if (name.Text.Length == 0) name.Text = Path.GetFileNameWithoutExtension(d.FileName); } }));
                row("Arguments", args, null);
                t.Controls.Add(new Label { Text = "Opens can be a program, file, folder, web address or ms-settings: page.", AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small }, 1, 4);
                var bar = LayoutKit.Actions(DockStyle.Bottom);
                var ok = Ui.Btn("Save", null, true); var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
                ok.Click += delegate
                {
                    uint mods; Keys k;
                    if (!Hotkeys.Parse(keys.Text, out mods, out k)) { MessageBox.Show(f, "Click the Keys box and press a combination like Ctrl+Alt+1.", App.Name); return; }
                    if (target.Text.Trim().Length == 0) { MessageBox.Show(f, "Choose what it opens.", App.Name); return; }
                    string id = mods + ":" + (int)k;
                    foreach (var a in Hotkeys.Actions) { uint m2; Keys k2; if (Hotkeys.Parse(Hotkeys.Get(a[0]), out m2, out k2) && m2 + ":" + (int)k2 == id) { MessageBox.Show(f, keys.Text + " is already used by \"" + a[1] + "\".", App.Name); return; } }
                    foreach (var o in Store.Settings.Launchers) { uint m2; Keys k2; if (o != u && Hotkeys.Parse(o.Keys, out m2, out k2) && m2 + ":" + (int)k2 == id) { MessageBox.Show(f, keys.Text + " is already used by \"" + o.Name + "\".", App.Name); return; } }
                    u.Keys = keys.Text.Trim(); u.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : Path.GetFileNameWithoutExtension(target.Text.Trim()); u.Target = target.Text.Trim().Trim('"'); u.Args = args.Text.Trim();
                    f.DialogResult = DialogResult.OK;
                };
                bar.Controls.Add(ok); bar.Controls.Add(cancel);
                f.Controls.Add(t); f.Controls.Add(bar); f.AcceptButton = ok; f.CancelButton = cancel;
                Ui.Theme(f);
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }

        static readonly string[][] InApp =
        {
            new[] { "Ctrl+1 ... Ctrl+9, Ctrl+0", "Main window: switch pages" }, new[] { "Ctrl+F", "Search on the current page" }, new[] { "Ctrl+K", "Search everything (Home)" },
            new[] { "F5", "Explorer: copy selection to the next pane" }, new[] { "F6", "Explorer: move selection to the next pane" }, new[] { "F2", "Explorer: rename" },
            new[] { "F7 / Ctrl+Shift+N", "Explorer: new folder" }, new[] { "Del / Shift+Del", "Explorer: Recycle Bin / delete for good" }, new[] { "Ctrl+C / X / V", "Explorer: copy, cut, paste (works with Windows Explorer too)" },
            new[] { "Backspace / Alt+Up", "Explorer: parent folder" }, new[] { "Alt+Left / Alt+Right", "Explorer: back / forward" }, new[] { "Ctrl+L", "Explorer: type a path" },
            new[] { "Alt+Enter", "Explorer: properties" }, new[] { "Enter", "Tasks: add the typed task (\"call Ali tomorrow 3pm !high #work\")" },
            new[] { "Ctrl+N", "Sticky note: new note" }, new[] { "Esc", "Close popups and quick windows" }, new[] { "Ctrl+Enter", "Quick run: run as administrator" },
        };
        static readonly string[][] WindowsKeys =
        {
            new[] { "Win+E", "File Explorer" }, new[] { "Win+D", "Show the desktop" }, new[] { "Win+L", "Lock the PC" }, new[] { "Win+V", "Windows clipboard history" },
            new[] { "Win+Shift+S", "Snipping Tool" }, new[] { "Win+.", "Emoji and symbols" }, new[] { "Win+Tab", "Task view" }, new[] { "Win+Ctrl+D", "New virtual desktop" },
            new[] { "Win+Ctrl+Left / Right", "Switch virtual desktop" }, new[] { "Win+Left / Right / Up", "Snap the window" }, new[] { "Win+Z", "Snap layouts" },
            new[] { "Win+Shift+Left / Right", "Move window to the other monitor" }, new[] { "Win+P", "Projector / second screen" }, new[] { "Win+K", "Cast to a device" },
            new[] { "Win+I", "Settings" }, new[] { "Win+X", "Power user menu" }, new[] { "Win+R", "Run box" }, new[] { "Win+A", "Quick settings" }, new[] { "Win+N", "Notifications and calendar" },
            new[] { "Win+H", "Voice typing" }, new[] { "Win+Ctrl+Shift+B", "Reset the graphics driver" }, new[] { "Win+Pause", "System information" }, new[] { "Win+M", "Minimize all" },
            new[] { "Win+Home", "Minimize all but the active window" }, new[] { "Win+1 ... 9", "Open taskbar app 1-9" }, new[] { "Win+T", "Cycle taskbar apps" }, new[] { "Win++ / Win+Esc", "Magnifier on / off" },
            new[] { "Ctrl+Shift+Esc", "Task Manager" }, new[] { "Alt+Tab", "Switch windows" }, new[] { "Alt+F4", "Close the window" }, new[] { "Ctrl+Shift+T", "Chrome: reopen closed tab" },
            new[] { "Ctrl+Win+Enter", "Narrator" }, new[] { "PrtScn", "Screenshot (Snipping Tool)" }, new[] { "F2 / F5", "Rename / refresh in Explorer" }, new[] { "Shift+F10", "Right-click menu" },
        };

        Control Reference(string[][] list)
        {
            var p = new Panel();
            var filter = Ui.Search("Filter shortcuts"); filter.Dock = DockStyle.Top;
            var lv = new FastList { Dock = DockStyle.Fill, VirtualMode = true };
            lv.Columns.Add("Keys", Ui.S(220)); lv.Columns.Add("What it does", Ui.S(500)); lv.FillIndex = 1;
            var rows = list.ToList();
            lv.RetrieveVirtualItem += (s, e) => { var r = e.ItemIndex < rows.Count ? rows[e.ItemIndex] : new[] { "", "" }; var it = new ListViewItem(r[0]); it.SubItems.Add(r[1]); e.Item = it; };
            lv.VirtualListSize = rows.Count;
            filter.TextChanged += delegate { string q = filter.Text.Trim(); rows = list.Where(r => q.Length == 0 || r[0].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || r[1].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList(); lv.VirtualListSize = rows.Count; lv.Invalidate(); };
            var m = Ui.Menu(); m.Items.Add("Copy", null, delegate { if (lv.SelectedIndices.Count > 0) { var r = rows[lv.SelectedIndices[0]]; App.I.CopyText(r[0] + " - " + r[1]); } }); Ui.ThemeMenu(m); lv.ContextMenuStrip = m;
            p.Controls.Add(lv); p.Controls.Add(filter);
            return p;
        }
    }
}
