using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // Opens real terminals (Windows Terminal when installed, else the console) in any folder:
    // one pane, a split-pane Windows Terminal window, or several windows tiled side by side on the screen.
    static class Term
    {
        public static readonly string[] Shells = { "pwsh", "powershell", "cmd" };
        public static readonly string[] Commands = { "claude", "claude --continue", "claude --resume", "codex", "git status", "npm run dev" };
        // id, label, columns, rows
        public static readonly string[][] Layouts =
        {
            new[] { "1", "1", "1", "1" },
            new[] { "2", "2", "2", "1" },
            new[] { "3", "3", "3", "1" },
            new[] { "4", "4", "4", "1" },
            new[] { "2x2", "2 x 2", "2", "2" },
            new[] { "5", "5", "5", "1" },
            new[] { "6", "6", "6", "1" },
            new[] { "3x2", "3 x 2", "3", "2" },
            new[] { "4x2", "4 x 2", "4", "2" },
        };

        public static void Grid(string layout, out int cols, out int rows)
        {
            cols = 4; rows = 1;
            foreach (var l in Layouts) if (l[0] == layout) { cols = int.Parse(l[2]); rows = int.Parse(l[3]); return; }
        }
        public static int Count(string layout) { int c, r; Grid(layout, out c, out r); return c * r; }

        // ---------- shells ----------
        static string pwsh, wt;
        static bool probed;
        static void Probe()
        {
            if (probed) return;
            probed = true;
            string pf = Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files";
            string apps = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", @"Microsoft\WindowsApps");
            pwsh = Find("pwsh.exe", new[] { Path.Combine(pf, @"PowerShell\7\pwsh.exe"), Path.Combine(pf, @"PowerShell\7-preview\pwsh.exe"), Path.Combine(apps, "pwsh.exe") });
            wt = Find("wt.exe", new[] { Path.Combine(apps, "wt.exe") });
        }
        static string Find(string exe, string[] known)
        {
            foreach (var k in known) try { if (File.Exists(k)) return k; } catch { }
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try { if (dir.Trim().Length == 0) continue; var p = Path.Combine(dir.Trim().Trim('"'), exe); if (File.Exists(p)) return p; } catch { }
            }
            return null;
        }
        public static bool HasWt { get { Probe(); return wt != null; } }
        public static bool HasPwsh7 { get { Probe(); return pwsh != null; } }
        public static string ShellExe(string shell)
        {
            Probe();
            if (shell == "cmd") return Path.Combine(Environment.SystemDirectory, "cmd.exe");
            if (shell == "powershell" || pwsh == null) return Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            return pwsh;
        }
        public static string ShellName(string shell) { return shell == "cmd" ? "Command Prompt" : shell == "powershell" || !HasPwsh7 ? "Windows PowerShell" : "PowerShell 7"; }

        public static string ShellArgs(string shell, string command)
        {
            command = (command ?? "").Trim();
            if (shell == "cmd") return command.Length > 0 ? "/K " + command : "";
            return command.Length > 0 ? "-NoLogo -NoExit -Command \"" + command.Replace("\"", "\\\"") + "\"" : "-NoLogo";
        }

        static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

        // A usable folder for any path: the folder itself, the folder of a file, the nearest existing parent, else home.
        public static string Folder(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return Home;
                path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                for (int i = 0; i < 32 && !string.IsNullOrEmpty(path); i++)
                {
                    if (Directory.Exists(path)) return path;
                    if (File.Exists(path)) return Path.GetDirectoryName(path);
                    path = Path.GetDirectoryName(path);
                }
            }
            catch { }
            return Home;
        }

        static string Quote(string s) { return "\"" + s + "\""; }
        static string DirArg(string dir) { return Quote(dir.EndsWith("\\") ? dir + "." : dir); }
        static string Title(string dir) { string n = Path.GetFileName(dir.TrimEnd('\\')); return string.IsNullOrEmpty(n) ? dir : n; }

        // One Windows Terminal pane: folder, tab title, shell and its arguments. ';' separates wt commands, so escape it.
        static string PaneArgs(TermPane p)
        {
            string dir = Folder(p.Folder);
            string shellPart = Quote(ShellExe(p.Shell)) + " " + ShellArgs(p.Shell, p.Command);
            string title = string.IsNullOrWhiteSpace(p.Name) ? Title(dir) : p.Name.Trim();
            return "-d " + DirArg(dir) + " --title " + Quote(title.Replace("\"", "").TrimEnd('\\')) + " " + shellPart.Replace(";", "\\;");
        }

        static Process Start(string exe, string args, string dir)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = dir };
            return Process.Start(psi);
        }

        public static void Remember(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return;
            var list = Store.Settings.TermRecent;
            list.RemoveAll(x => string.Equals(x, folder, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, folder);
            while (list.Count > 20) list.RemoveAt(list.Count - 1);
            Store.MarkSettings();
            MetaStore.Touch("folders", folder);
        }

        // PWSH (or the chosen shell) in one folder, optionally running a command such as "claude".
        public static void Open(string folder, string command) { Open(folder, command, null); }
        public static void Open(string folder, string command, string shell)
        {
            string dir = Folder(folder);
            shell = shell ?? Store.Settings.TermShell ?? "pwsh";
            try
            {
                var p = new TermPane { Folder = dir, Shell = shell, Command = command ?? "" };
                if (HasWt) Start(wt, "-w new nt " + PaneArgs(p), dir);
                else Start(ShellExe(shell), ShellArgs(shell, command), dir);
                Remember(dir);
            }
            catch (Exception ex) { App.Log("Terminal: " + ex.Message); MessageBox.Show("Could not open a terminal in " + dir + ":\n" + ex.Message, App.Name); }
        }

        // Ctrl+Alt+E: PWSH in the folder of the File Explorer window in front (selected folder, folder of a selected file,
        // or the open folder). Outside File Explorer: the last terminal folder.
        public static void OpenFromExplorer(string command)
        {
            string folder = null;
            try { var paths = ExplorerSelection.Get(Native.GetForegroundWindow()); if (paths.Count > 0) folder = Folder(paths[0]); } catch { }
            if (folder == null && Store.Settings.TermRecent.Count > 0) folder = Store.Settings.TermRecent[0];
            Open(folder, command);
        }

        // Adds "PWSH here" and "Claude here" to any menu that knows a folder.
        public static void AddMenu(ToolStripItemCollection items, Func<string> folder)
        {
            items.Add("PWSH here", null, delegate { var f = Safe(folder); if (f != null) Open(f, ""); });
            items.Add("Claude here (in PWSH)", null, delegate { var f = Safe(folder); if (f != null) Open(f, "claude"); });
            items.Add("Add to my terminals workspace", null, delegate { var f = Safe(folder); if (f != null) App.I.AddTerminalFolder(f); });
        }
        static string Safe(Func<string> f) { try { var s = f(); return string.IsNullOrWhiteSpace(s) ? null : s; } catch { return null; } }

        // ---------- workspaces ----------
        public static TermWorkspace Current
        {
            get
            {
                var s = Store.Settings;
                if (s.TermWorkspaces.Count == 0)
                {
                    var ws = new TermWorkspace { Name = "My terminals", Layout = "4", Mode = "tiled" };
                    string start = s.TermRecent.FirstOrDefault() ?? s.ExplorerPaths.FirstOrDefault(p => !string.IsNullOrEmpty(p) && Directory.Exists(p)) ?? Home;
                    for (int i = 0; i < 4; i++) ws.Panes.Add(new TermPane { Folder = start, Shell = s.TermShell });
                    s.TermWorkspaces.Add(ws);
                    s.TermLast = 0;
                    Store.MarkSettings();
                }
                if (s.TermLast < 0 || s.TermLast >= s.TermWorkspaces.Count) s.TermLast = 0;
                return s.TermWorkspaces[s.TermLast];
            }
        }

        public static TermPane PaneAt(TermWorkspace ws, int i)
        {
            while (ws.Panes.Count <= i) ws.Panes.Add(new TermPane { Folder = ws.Panes.Count > 0 ? ws.Panes[ws.Panes.Count - 1].Folder : Home, Shell = ws.Panes.Count > 0 ? ws.Panes[ws.Panes.Count - 1].Shell : "pwsh" });
            return ws.Panes[i];
        }

        public static void LaunchCurrent() { Launch(Current); }

        public static void Launch(TermWorkspace ws)
        {
            int cols, rows; Grid(ws.Layout, out cols, out rows);
            int n = cols * rows;
            var panes = Enumerable.Range(0, n).Select(i => PaneAt(ws, i)).Select(p => new TermPane { Folder = Folder(p.Folder), Shell = p.Shell, Command = p.Command }).ToList();
            foreach (var p in panes.AsEnumerable().Reverse()) Remember(p.Folder);
            try
            {
                if (ws.Mode == "panes" && HasWt) { Start(wt, SplitArgs(panes, cols, rows), panes[0].Folder); return; }
            }
            catch (Exception ex) { App.Log("Terminal panes: " + ex.Message); }
            var t = new Thread(() => Tile(panes, cols, rows)) { IsBackground = true, Name = "Terminal tiling" };
            t.Start();
        }

        // One Windows Terminal window (maximized) split into cols x rows panes of equal size.
        static string SplitArgs(List<TermPane> panes, int cols, int rows)
        {
            var parts = new List<string>();
            parts.Add("-w new" + (cols * rows > 1 ? " -M" : "") + " nt " + PaneArgs(panes[0]));
            for (int c = 1; c < cols; c++) parts.Add("sp -V -s " + F((cols - c) / (double)(cols - c + 1)) + " " + PaneArgs(panes[c]));
            if (rows > 1)
                for (int c = 0; c < cols; c++)
                {
                    parts.Add("fp -t " + c);
                    for (int r = 1; r < rows; r++) parts.Add("sp -H -s " + F((rows - r) / (double)(rows - r + 1)) + " " + PaneArgs(panes[r * cols + c]));
                }
            if (cols * rows > 1) parts.Add("fp -t 0");
            return string.Join(" ; ", parts);
        }
        static string F(double d) { return d.ToString("0.####", CultureInfo.InvariantCulture); }

        // Separate windows, each moved into its own cell of the monitor under the mouse (like 4 terminals side by side).
        static void Tile(List<TermPane> panes, int cols, int rows)
        {
            try { SetThreadDpiAwarenessContext((IntPtr)(-4)); } catch { }   // physical pixels on every monitor
            RECT work;
            try
            {
                POINT cur; GetCursorPos(out cur);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                GetMonitorInfo(MonitorFromPoint(cur, 2), ref mi);
                work = mi.rcWork;
            }
            catch { var wa = Screen.PrimaryScreen.WorkingArea; work = new RECT { Left = wa.Left, Top = wa.Top, Right = wa.Right, Bottom = wa.Bottom }; }
            int W = work.Right - work.Left, H = work.Bottom - work.Top;
            var placed = new List<KeyValuePair<IntPtr, Rectangle>>();
            for (int i = 0; i < panes.Count; i++)
            {
                int c = i % cols, r = i / cols;
                var slot = new Rectangle(work.Left + W * c / cols, work.Top + H * r / rows, W * (c + 1) / cols - W * c / cols, H * (r + 1) / rows - H * r / rows);
                var before = TermWindows();
                var p = panes[i];
                try
                {
                    if (HasWt) Start(wt, "-w new nt " + PaneArgs(p), p.Folder);
                    else Start(ShellExe(p.Shell), ShellArgs(p.Shell, p.Command), p.Folder);
                }
                catch (Exception ex) { App.Log("Terminal " + (i + 1) + ": " + ex.Message); continue; }
                IntPtr h = IntPtr.Zero;
                var until = DateTime.Now.AddSeconds(12);
                while (DateTime.Now < until)
                {
                    Thread.Sleep(100);
                    h = TermWindows().FirstOrDefault(x => !before.Contains(x));
                    if (h != IntPtr.Zero) break;
                }
                if (h == IntPtr.Zero) { App.Log("Terminal " + (i + 1) + ": window not found to place."); continue; }
                Thread.Sleep(250);
                Place(h, slot);
                placed.Add(new KeyValuePair<IntPtr, Rectangle>(h, slot));
            }
            // Windows Terminal may resize itself once ready: place every window once more.
            Thread.Sleep(600);
            foreach (var kv in placed) Place(kv.Key, kv.Value);
        }

        static void Place(IntPtr h, Rectangle slot)
        {
            try
            {
                if (!IsWindow(h)) return;
                ShowWindow(h, 9 /* SW_RESTORE */);
                int l = 0, t = 0, r = 0, b = 0;
                RECT wr, fr;
                // Windows 10/11 frames have invisible resize borders: compensate so the visible edges touch.
                if (GetWindowRect(h, out wr) && DwmGetWindowAttribute(h, 9 /* EXTENDED_FRAME_BOUNDS */, out fr, Marshal.SizeOf(typeof(RECT))) == 0)
                { l = fr.Left - wr.Left; t = fr.Top - wr.Top; r = wr.Right - fr.Right; b = wr.Bottom - fr.Bottom; }
                SetWindowPos(h, IntPtr.Zero, slot.X - l, slot.Y - t, slot.Width + l + r, slot.Height + t + b, 0x0004 | 0x0010 /* NOZORDER | NOACTIVATE */);
            }
            catch { }
        }

        static HashSet<IntPtr> TermWindows()
        {
            var set = new HashSet<IntPtr>();
            var cls = new System.Text.StringBuilder(128);
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                cls.Length = 0; GetClassName(h, cls, cls.Capacity);
                string c = cls.ToString();
                if (c == "CASCADIA_HOSTING_WINDOW_CLASS" || c == "ConsoleWindowClass") set.Add(h);
                return true;
            }, IntPtr.Zero);
            return set;
        }

        // ---------- native ----------
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hgt, uint flags);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);
    }

    // ===================== Terminals page =====================

    class TerminalPage : Panel
    {
        readonly FlowLayoutPanel layouts, rowsHost;
        readonly ComboBox workspaces, runAll;
        readonly SegButton modeTiled, modePanes;
        readonly TermPreview preview;
        readonly Button launch;
        readonly Label hint;
        readonly ItemShelf shelf;
        bool loading;

        TermWorkspace Ws { get { return Term.Current; } }

        public TerminalPage()
        {
            Dock = DockStyle.Fill;

            // Header: title + workspace picker
            var top = MainForm.Bar();
            top.Controls.Add(MainForm.Heading("Terminals"));
            workspaces = MainForm.Combo(200);
            workspaces.SelectedIndexChanged += delegate { if (loading || workspaces.SelectedIndex < 0) return; Store.Settings.TermLast = workspaces.SelectedIndex; Store.MarkSettings(); LoadWs(); };
            top.Controls.Add(workspaces);
            top.Controls.Add(Ui.Btn("New workspace", delegate { NewWorkspace(); }));
            top.Controls.Add(Ui.Btn("Rename", delegate { var n = Ui.Ask("Rename workspace", "Name", Ws.Name, FindForm()); if (!string.IsNullOrWhiteSpace(n)) { Ws.Name = n.Trim(); Store.MarkSettings(); FillWorkspaces(); } }));
            top.Controls.Add(Ui.Btn("Delete", delegate { DeleteWorkspace(); }));

            // Layout chips + mode
            var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, Ui.S(4), 0, Ui.S(4)) };
            layouts = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 0, Ui.S(18), 0) };
            foreach (var l in Term.Layouts)
            {
                string id = l[0];
                var chip = new LayoutChip(id, l[1], int.Parse(l[2]), int.Parse(l[3]));
                chip.Click += delegate { Ws.Layout = id; Store.MarkSettings(); LoadWs(); };
                layouts.Controls.Add(chip);
            }
            options.Controls.Add(layouts);
            var modes = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, Ui.S(8), 0, 0), FlowDirection = FlowDirection.TopDown };
            modeTiled = new SegButton("Separate windows, tiled side by side", "\uE737");
            modePanes = new SegButton("One Windows Terminal window, split panes", "\uE8A9");
            modeTiled.Click += delegate { Ws.Mode = "tiled"; Store.MarkSettings(); LoadWs(); };
            modePanes.Click += delegate { Ws.Mode = "panes"; Store.MarkSettings(); LoadWs(); };
            modes.Controls.Add(modeTiled); modes.Controls.Add(modePanes);
            options.Controls.Add(modes);

            preview = new TermPreview { Dock = DockStyle.Top, Height = Ui.S(188) };
            preview.PaneClicked += i => { if (i < rowsHost.Controls.Count) { var row = rowsHost.Controls[i] as PaneRow; if (row != null) row.FocusFolder(); } };

            rowsHost = new FlowLayoutPanel { Dock = DockStyle.Top, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, Ui.S(6), 0, 0) };
            rowsHost.Resize += delegate { FitRows(); };

            // Bottom actions
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, Ui.S(8), 0, Ui.S(2)) };
            launch = Ui.Btn("Launch terminals", delegate { Commit(); Term.Launch(Ws); App.I.Balloon("Terminals", "Opening " + Term.Count(Ws.Layout) + " terminal(s)..."); }, true);
            launch.MinimumSize = Ui.S(190, 38);
            actions.Controls.Add(launch);
            actions.Controls.Add(Ui.Btn("Same folder in all", delegate { SameFolder(); }));
            var runLabel = new Label { Text = "Run in every pane:", AutoSize = true, Margin = new Padding(Ui.S(10), Ui.S(9), Ui.S(4), 0), ForeColor = Ui.Muted };
            actions.Controls.Add(runLabel);
            runAll = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = Ui.S(170), Margin = new Padding(0, Ui.S(4), Ui.S(4), 0) };
            runAll.Items.Add("(nothing - just the shell)");
            runAll.Items.AddRange(Term.Commands);
            actions.Controls.Add(runAll);
            ComboFix.NoSelect(runAll);
            actions.Controls.Add(Ui.Btn("Apply", delegate { ApplyRunAll(); }));
            hint = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = Ui.S(22), ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.MiddleLeft };

            // Project folders (favourites + recent) fill the space under the pane rows.
            shelf = new ItemShelf("folders") { Dock = DockStyle.Fill, Title = "Project folders", ExtraRecent = () => Store.Settings.TermRecent, Padding = new Padding(0, Ui.S(10), 0, 0) };
            shelf.Open += id => UseFolder(id);
            shelf.AddClicked += delegate
            {
                using (var d = new FolderBrowserDialog { Description = "Add a project folder to favorites" })
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) MetaStore.SetFav("folders", d.SelectedPath, true);
            };
            shelf.MenuItems = (id, m) =>
            {
                int n = Term.Count(Ws.Layout);
                var into = new ToolStripMenuItem("Use in pane");
                for (int i = 0; i < n; i++) { int k = i; into.DropDownItems.Add("Pane " + (k + 1), null, delegate { Commit(); Term.PaneAt(Ws, k).Folder = id; Store.MarkSettings(); LoadWs(); }); }
                m.Items.Add(into);
                m.Items.Add("Use in every pane", null, delegate { Commit(); for (int i = 0; i < n; i++) Term.PaneAt(Ws, i).Folder = id; Store.MarkSettings(); LoadWs(); });
                m.Items.Add(new ToolStripSeparator());
                Term.AddMenu(m.Items, () => id);
                m.Items.Add("Open in File Explorer", null, delegate { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + id + "\""); } catch { } });
            };

            Controls.Add(shelf);
            Controls.Add(rowsHost);
            Controls.Add(preview);
            Controls.Add(options);
            Controls.Add(top);
            Controls.Add(hint);
            Controls.Add(actions);
            Ui.Theme(this);
            FillWorkspaces();
            LoadWs();
        }

        // Wide window: the live preview sits on the right at full height (pane rows on the left);
        // narrow window: preview on top, taller when there is room.
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (preview != null)
            {
                bool wide = ClientSize.Width >= Ui.S(1250);
                var dock = wide ? DockStyle.Right : DockStyle.Top;
                if (preview.Dock != dock) preview.Dock = dock;
                if (wide) { int w = Math.Max(Ui.S(420), ClientSize.Width * 42 / 100); if (preview.Width != w) preview.Width = w; }
                else { int h = Math.Max(Ui.S(188), Math.Min(Ui.S(320), ClientSize.Height / 4)); if (preview.Height != h) preview.Height = h; }
            }
            if (rowsHost != null) FitRows();
            base.OnLayout(e);
        }

        void FillWorkspaces()
        {
            loading = true;
            workspaces.BeginUpdate();
            workspaces.Items.Clear();
            var cur = Ws;
            foreach (var w in Store.Settings.TermWorkspaces) workspaces.Items.Add(w.Name);
            workspaces.SelectedIndex = Math.Max(0, Store.Settings.TermWorkspaces.IndexOf(cur));
            workspaces.EndUpdate();
            loading = false;
        }

        void NewWorkspace()
        {
            Commit();
            var n = Ui.Ask("New workspace", "Name (for example the project)", "Workspace " + (Store.Settings.TermWorkspaces.Count + 1), FindForm());
            if (string.IsNullOrWhiteSpace(n)) return;
            var src = Ws;
            var ws = new TermWorkspace { Name = n.Trim(), Layout = src.Layout, Mode = src.Mode, Panes = src.Panes.Select(p => new TermPane { Folder = p.Folder, Shell = p.Shell, Command = p.Command, Name = p.Name, Color = p.Color }).ToList() };
            Store.Settings.TermWorkspaces.Add(ws);
            Store.Settings.TermLast = Store.Settings.TermWorkspaces.Count - 1;
            Store.MarkSettings();
            FillWorkspaces(); LoadWs();
        }

        void DeleteWorkspace()
        {
            if (Store.Settings.TermWorkspaces.Count <= 1) { MessageBox.Show(FindForm(), "Keep at least one workspace.", App.Name); return; }
            if (MessageBox.Show(FindForm(), "Delete the workspace \"" + Ws.Name + "\"?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Store.Settings.TermWorkspaces.RemoveAt(Store.Settings.TermLast);
            Store.Settings.TermLast = 0;
            Store.MarkSettings();
            FillWorkspaces(); LoadWs();
        }

        void LoadWs()
        {
            var ws = Ws;
            int n = Term.Count(ws.Layout);
            foreach (LayoutChip c in layouts.Controls) c.Selected = c.Id == ws.Layout;
            modeTiled.Selected = ws.Mode != "panes"; modePanes.Selected = ws.Mode == "panes";
            modePanes.Enabled = Term.HasWt;
            launch.Text = n == 1 ? "Launch 1 terminal" : "Launch " + n + " terminals";
            Ui.Batch(rowsHost, () =>
            {
                Ui.ClearControls(rowsHost.Controls);
                for (int i = 0; i < n; i++)
                {
                    var row = new PaneRow(i, Term.PaneAt(ws, i));
                    row.Changed += delegate { preview.Invalidate(); };
                    rowsHost.Controls.Add(row);
                }
                FitRows();
            });
            preview.Set(ws);
            hint.Text = (Term.HasWt ? "Windows Terminal found." : "Windows Terminal not found: plain console windows are used and split panes are off.")
                + "  " + Term.ShellName("pwsh") + (Term.HasPwsh7 ? "" : " (install PowerShell 7 for pwsh)") + ".   Global keys: "
                + Key("terminals") + " launch this workspace,  " + Key("pwshhere") + " PWSH in the File Explorer folder.";
            Ui.Theme(rowsHost);
        }

        static string Key(string a) { var k = Hotkeys.Get(a); return k.Length > 0 ? k : "(no key)"; }

        void FitRows()
        {
            int w = Math.Max(Ui.S(300), rowsHost.ClientSize.Width - Ui.S(4));
            foreach (Control c in rowsHost.Controls) if (c.Width != w) c.Width = w;
            // Pane rows take just their height (capped so the folder shelf below keeps room); scroll beyond that.
            int content = rowsHost.Padding.Vertical + rowsHost.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
            int cap = Math.Max(Ui.S(110), (ClientSize.Height - Ui.S(260)) * 55 / 100);
            int h = Math.Min(content, cap);
            if (rowsHost.Height != h) rowsHost.Height = h;
        }

        void Commit() { foreach (PaneRow r in rowsHost.Controls.OfType<PaneRow>()) r.Commit(); Store.MarkSettings(); }

        // Puts a folder into the first pane that has no folder yet (or pane 1 when all are set).
        public void UseFolder(string path)
        {
            Commit();
            int n = Term.Count(Ws.Layout), target = 0;
            for (int i = 0; i < n; i++) if (string.IsNullOrWhiteSpace(Term.PaneAt(Ws, i).Folder)) { target = i; break; }
            Term.PaneAt(Ws, target).Folder = Term.Folder(path);
            Store.MarkSettings(); LoadWs();
        }

        void SameFolder()
        {
            Commit();
            var first = Term.PaneAt(Ws, 0).Folder;
            using (var d = new FolderBrowserDialog { Description = "Folder for every pane", SelectedPath = Term.Folder(first) })
            {
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                for (int i = 0; i < Term.Count(Ws.Layout); i++) Term.PaneAt(Ws, i).Folder = d.SelectedPath;
            }
            Store.MarkSettings(); LoadWs();
        }

        void ApplyRunAll()
        {
            Commit();
            string cmd = runAll.SelectedIndex == 0 ? "" : runAll.Text.Trim();
            if (cmd.StartsWith("(")) cmd = "";
            for (int i = 0; i < Term.Count(Ws.Layout); i++) Term.PaneAt(Ws, i).Command = cmd;
            Store.MarkSettings(); LoadWs();
        }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible && rowsHost != null) Commit(); }
    }

    // One pane: number, folder (recent folders), browse, shell, start command, open just this one.
    class PaneRow : Panel
    {
        readonly TermPane pane;
        readonly ComboBox folder, shell, command;
        public event EventHandler Changed;

        public PaneRow(int index, TermPane pane)
        {
            this.pane = pane;
            Height = Ui.S(46); Margin = new Padding(0, 0, 0, Ui.S(6));
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(Ui.S(8), Ui.S(6), Ui.S(8), Ui.S(4)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(34)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(118)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(170)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var paneColor = MetaStore.ColorOf(pane.Color);
            var num = new Label { Text = (index + 1).ToString(), Font = Ui.Bold, ForeColor = paneColor.IsEmpty ? Ui.Accent : paneColor, TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Cursor = Cursors.Hand };
            new ToolTip().SetToolTip(num, string.IsNullOrEmpty(pane.Name) ? "Click to name this pane or give it a color" : pane.Name + " (click to change)");
            num.Click += delegate
            {
                var m = new ContextMenuStrip(); Ui.ThemeMenu(m);
                m.Items.Add("Name this pane...", null, delegate
                {
                    var v = Ui.Ask("Pane name", "Name shown as the terminal tab title (empty = folder name):", pane.Name ?? "", FindForm());
                    if (v != null) { pane.Name = v.Trim(); Raise(); }
                });
                foreach (var c in MetaStore.Colors)
                {
                    string cc = c;
                    m.Items.Add(new ToolStripMenuItem(c, Ui.Swatch(MetaStore.ColorOf(c), Ui.S(14)), delegate { pane.Color = cc; num.ForeColor = MetaStore.ColorOf(cc); Raise(); }) { Checked = pane.Color == c });
                }
                m.Items.Add("No color", null, delegate { pane.Color = ""; num.ForeColor = Ui.Accent; Raise(); });
                m.Show(num, new Point(0, num.Height));
            };
            folder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Text = pane.Folder, Margin = new Padding(0, Ui.S(2), Ui.S(6), 0) };
            foreach (var r in Store.Settings.TermRecent) folder.Items.Add(r);
            folder.Text = pane.Folder;
            folder.TextChanged += delegate { pane.Folder = folder.Text.Trim(); Raise(); };
            ComboFix.NoSelect(folder);
            var browse = Ui.Btn("Folder...", delegate
            {
                using (var d = new FolderBrowserDialog { SelectedPath = Term.Folder(folder.Text) })
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) folder.Text = d.SelectedPath;
            });
            browse.Margin = new Padding(0, 0, Ui.S(6), 0);
            shell = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, Ui.S(2), Ui.S(6), 0) };
            shell.Items.AddRange(Term.Shells);
            shell.SelectedIndex = Math.Max(0, Array.IndexOf(Term.Shells, pane.Shell));
            shell.SelectedIndexChanged += delegate { pane.Shell = (string)shell.SelectedItem; Raise(); };
            command = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Margin = new Padding(0, Ui.S(2), Ui.S(6), 0) };
            command.Items.AddRange(Term.Commands);
            command.Text = pane.Command;
            command.TextChanged += delegate { pane.Command = command.Text.Trim(); Raise(); };
            ComboFix.NoSelect(command);
            EventHandler cue = delegate { try { Native.SendMessage(command.Handle, 0x1703 /* CB_SETCUEBANNER */, IntPtr.Zero, "start command (optional)"); } catch { } };
            if (command.IsHandleCreated) cue(command, EventArgs.Empty); else command.HandleCreated += cue;
            var open = Ui.Btn("Open", delegate { Commit(); Term.Open(pane.Folder, pane.Command, pane.Shell); });
            open.Margin = new Padding(0);
            t.Controls.Add(num, 0, 0); t.Controls.Add(folder, 1, 0); t.Controls.Add(browse, 2, 0); t.Controls.Add(shell, 3, 0); t.Controls.Add(command, 4, 0); t.Controls.Add(open, 5, 0);
            Controls.Add(t);
            BackColor = Ui.Card;
        }

        void Raise() { Store.MarkSettings(); var h = Changed; if (h != null) h(this, EventArgs.Empty); }
        public void Commit() { pane.Folder = folder.Text.Trim(); pane.Command = command.Text.Trim(); if (shell.SelectedItem != null) pane.Shell = (string)shell.SelectedItem; }
        public void FocusFolder() { folder.Focus(); folder.SelectAll(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.Round(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
            {
                using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path);
                using (var p = new Pen(Ui.Line)) g.DrawPath(p, path);
            }
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
    }

    // A layout choice drawn as a mini grid of panes.
    class LayoutChip : Control
    {
        public readonly string Id;
        readonly int cols, rows;
        bool selected, hover;
        public bool Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        public LayoutChip(string id, string label, int cols, int rows)
        {
            Id = id; Text = label; this.cols = cols; this.rows = rows;
            Size = Ui.S(74, 64); Margin = new Padding(0, 0, Ui.S(8), Ui.S(8)); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.RadioButton; AccessibleName = (cols * rows) + " panes";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Ui.Round(r, Ui.S(10)))
            {
                using (var b = new SolidBrush(selected ? Ui.SelBg : hover ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                using (var p = new Pen(selected || Focused ? Ui.Accent : Ui.Line, selected ? Ui.S(2) : 1)) g.DrawPath(p, path);
            }
            var grid = new Rectangle(Ui.S(12), Ui.S(9), Width - Ui.S(24), Ui.S(30));
            int gap = Ui.S(2);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int x0 = grid.X + grid.Width * x / cols, x1 = grid.X + grid.Width * (x + 1) / cols;
                    int y0 = grid.Y + grid.Height * y / rows, y1 = grid.Y + grid.Height * (y + 1) / rows;
                    var cell = new Rectangle(x0, y0, x1 - x0 - gap, y1 - y0 - gap);
                    using (var path = Ui.Round(cell, Ui.S(2)))
                    using (var b = new SolidBrush(selected ? Ui.Accent : Ui.Mix(Ui.Muted, Ui.Card, 0.45))) g.FillPath(b, path);
                }
            TextRenderer.DrawText(g, Text, selected ? Ui.Bold : Ui.Small, new Rectangle(0, Height - Ui.S(22), Width, Ui.S(20)), selected ? Ui.Accent : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Two-state option button (icon + text) used for the launch mode.
    class SegButton : Control
    {
        readonly string glyph;
        bool selected, hover;
        public bool Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        public SegButton(string text, string glyph)
        {
            Text = text; this.glyph = glyph; Cursor = Cursors.Hand; TabStop = true;
            Size = new Size(TextRenderer.MeasureText(text, Ui.Bold).Width + Ui.S(58), Ui.S(34)); Margin = new Padding(0, 0, 0, Ui.S(6));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnClick(EventArgs e) { if (Enabled) base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Ui.Round(r, Ui.S(8)))
            {
                using (var b = new SolidBrush(selected ? Ui.SelBg : hover && Enabled ? Ui.HoverBg : Ui.Card)) g.FillPath(b, path);
                using (var p = new Pen(selected ? Ui.Accent : Ui.Line)) g.DrawPath(p, path);
            }
            var dot = new Rectangle(Ui.S(10), (Height - Ui.S(14)) / 2, Ui.S(14), Ui.S(14));
            using (var p = new Pen(selected ? Ui.Accent : Ui.Muted, Ui.S(2))) g.DrawEllipse(p, dot);
            if (selected) using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, Rectangle.Inflate(dot, -Ui.S(4), -Ui.S(4)));
            var fore = !Enabled ? Ui.Mix(Ui.Muted, Ui.Card, 0.4) : selected ? Ui.Text : Ui.Muted;
            TextRenderer.DrawText(g, glyph, Ui.Icon(11f), new Rectangle(Ui.S(30), 0, Ui.S(20), Height), selected ? Ui.Accent : fore, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Text, selected ? Ui.Bold : Ui.Base, new Rectangle(Ui.S(54), 0, Width - Ui.S(58), Height), fore, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }

    // Live preview of the workspace: dark terminal tiles in the chosen arrangement.
    class TermPreview : Control
    {
        TermWorkspace ws;
        readonly List<Rectangle> cells = new List<Rectangle>();
        int hover = -1;
        static Font mono;
        public event Action<int> PaneClicked;

        public TermPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0);
        }
        static Font Mono
        {
            get
            {
                if (mono == null)
                    foreach (var n in new[] { "Cascadia Mono", "Cascadia Code", "Consolas" })
                        using (var test = new Font(n, 9f)) if (test.Name == n) { mono = new Font(n, 8.5f); break; }
                return mono ?? (mono = new Font(FontFamily.GenericMonospace, 8.5f));
            }
        }
        public void Set(TermWorkspace w) { ws = w; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            if (ws == null) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cols, rows; Term.Grid(ws.Layout, out cols, out rows);
            var area = new Rectangle(0, Ui.S(4), Width - 1, Height - Ui.S(10));
            // "Screen" frame
            using (var path = Ui.Round(area, Ui.S(12)))
            {
                using (var b = new LinearGradientBrush(area, Ui.Dark ? Color.FromArgb(30, 33, 42) : Color.FromArgb(226, 229, 238), Ui.Dark ? Color.FromArgb(20, 22, 28) : Color.FromArgb(206, 211, 224), 90f)) g.FillPath(b, path);
            }
            var inner = Rectangle.Inflate(area, -Ui.S(10), -Ui.S(10));
            int gap = ws.Mode == "panes" ? Ui.S(2) : Ui.S(6);
            cells.Clear();
            var bg = Color.FromArgb(14, 16, 22);
            var title = Color.FromArgb(32, 36, 46);
            for (int i = 0; i < cols * rows; i++)
            {
                int c = i % cols, r = i / cols;
                int x0 = inner.X + inner.Width * c / cols, x1 = inner.X + inner.Width * (c + 1) / cols;
                int y0 = inner.Y + inner.Height * r / rows, y1 = inner.Y + inner.Height * (r + 1) / rows;
                var cell = new Rectangle(x0, y0, x1 - x0 - gap, y1 - y0 - gap);
                cells.Add(cell);
                var p = Term.PaneAt(ws, i);
                using (var path = Ui.Round(cell, Ui.S(6)))
                {
                    using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                    g.SetClip(path);
                    int th = Math.Min(Ui.S(20), cell.Height / 3);
                    using (var b = new SolidBrush(title)) g.FillRectangle(b, cell.X, cell.Y, cell.Width, th);
                    using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, cell.X + Ui.S(7), cell.Y + (th - Ui.S(7)) / 2, Ui.S(7), Ui.S(7));
                    string dir = Term.Folder(p.Folder);
                    string name = Path.GetFileName(dir.TrimEnd('\\')); if (string.IsNullOrEmpty(name)) name = dir;
                    var flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
                    TextRenderer.DrawText(g, (i + 1) + "  " + name, Ui.Small, new Rectangle(cell.X + Ui.S(18), cell.Y, cell.Width - Ui.S(22), th), Color.FromArgb(225, 228, 236), flags | TextFormatFlags.VerticalCenter);
                    int y = cell.Y + th + Ui.S(6), lh = Mono.Height + Ui.S(1);
                    string prompt = p.Shell == "cmd" ? dir + ">" : "PS " + dir + ">";
                    if (y + lh <= cell.Bottom) TextRenderer.DrawText(g, prompt, Mono, new Rectangle(cell.X + Ui.S(8), y, cell.Width - Ui.S(12), lh), Color.FromArgb(150, 200, 255), flags);
                    y += lh;
                    if (!string.IsNullOrEmpty(p.Command) && y + lh <= cell.Bottom)
                        TextRenderer.DrawText(g, "> " + p.Command, Mono, new Rectangle(cell.X + Ui.S(8), y, cell.Width - Ui.S(12), lh), Color.FromArgb(120, 230, 160), flags);
                    g.ResetClip();
                    if (i == hover) using (var pen = new Pen(Ui.Accent, Ui.S(2))) g.DrawPath(pen, path);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = cells.FindIndex(c => c.Contains(e.Location));
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { if (hover >= 0) { hover = -1; Invalidate(); } base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int i = cells.FindIndex(c => c.Contains(e.Location));
            if (i < 0 || ws == null) return;
            if (e.Clicks == 1 && PaneClicked != null) PaneClicked(i);
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = cells.FindIndex(c => c.Contains(e.Location));
            if (i >= 0 && ws != null) { var p = Term.PaneAt(ws, i); Term.Open(p.Folder, p.Command, p.Shell); }
        }
    }
}
