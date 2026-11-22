using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // History for the pages that need it, one way of working everywhere:
    //  - HistoryView: search box, sortable list (newest first), multi-select, favorites (★), and the actions the
    //    page gives (run again, open, delete, export...). Used by Terminals (launch history) and Agents (chats).
    //  - TermHistory: every terminal you open from the app (folders, command, shell, workspace).
    //  - Bundled: tools packed inside AllInOne.exe (ffmpeg) for PCs that do not have them; unpacked on first use.
    // ===================================================================================================

    class HistRow { public object Tag; public string[] Cells; public bool Fav; public DateTime Time; }

    class HistAction
    {
        public string Name; public bool Primary, CloseAfter, NeedsSelection = true;
        public Action<List<HistRow>> Run;
    }

    class HistoryView : Form
    {
        readonly Func<List<HistRow>> rows;
        readonly TextBox search;
        readonly ListView lv;
        readonly Label count;
        readonly CheckBox onlyFavorites = new CheckBox { Text = "Favorites only", AutoSize = true, Dock = DockStyle.Top };
        List<HistRow> shown = new List<HistRow>();
        int sortCol = -1; bool sortDesc = true;

        public HistoryView(string title, string[] columns, int[] widths, Func<List<HistRow>> rows, IEnumerable<HistAction> actions)
        {
            this.rows = rows;
            Text = title; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(Ui.S(1060), Ui.S(600)); Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text; KeyPreview = true;
            search = new TextBox { Dock = DockStyle.Top, Font = Ui.Base };
            lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = true, HideSelection = false, BorderStyle = BorderStyle.FixedSingle };
            lv.Columns.Add("", Ui.S(26));
            for (int i = 0; i < columns.Length; i++) lv.Columns.Add(columns[i], Ui.S(i < widths.Length ? widths[i] : 140));
            count = new Label { Dock = DockStyle.Bottom, Height = Ui.S(24), ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, Ui.S(6), 0, 0) };
            foreach (var a in actions)
            {
                var act = a;
                bar.Controls.Add(Ui.Btn(act.Name, delegate
                {
                    var sel = Selected();
                    if (act.NeedsSelection && sel.Count == 0) { count.Text = "Select one or more rows first."; return; }
                    act.Run(sel);
                    if (act.CloseAfter) Close(); else Fill();
                }, act.Primary));
            }
            bar.Controls.Add(Ui.Btn("Close", delegate { Close(); }));
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(12)) };
            pad.Controls.Add(lv); pad.Controls.Add(new Panel { Dock = DockStyle.Top, Height = Ui.S(8) }); pad.Controls.Add(onlyFavorites); pad.Controls.Add(search);
            pad.Controls.Add(bar); pad.Controls.Add(count);
            Controls.Add(pad);
            var searchLater = Ui.Debounce(search, 160, Fill);
            search.TextChanged += delegate { searchLater(); };
            onlyFavorites.CheckedChanged += delegate { Fill(); };
            lv.ColumnClick += (s, e) => { if (e.Column == sortCol) sortDesc = !sortDesc; else { sortCol = e.Column; sortDesc = false; } Fill(); };
            lv.DoubleClick += delegate { var first = actions.FirstOrDefault(x => x.Primary); if (first != null && Selected().Count > 0) { first.Run(Selected()); if (first.CloseAfter) Close(); else Fill(); } };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); else if (e.Control && e.KeyCode == Keys.F) { search.Focus(); e.Handled = true; } else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem it in lv.Items) it.Selected = true; e.Handled = true; } };
            Ui.Theme(this);
            Fill();
            Shown += delegate { search.Focus(); };
        }

        List<HistRow> Selected() { return lv.SelectedIndices.Cast<int>().Where(i => i < shown.Count).Select(i => shown[i]).ToList(); }

        public void Fill()
        {
            var all = rows();
            string q = search.Text.Trim().ToLowerInvariant();
            var terms = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = all.Where(r => {
                if (onlyFavorites.Checked && !r.Fav) return false;
                if (terms.Length == 0) return true;
                string haystack = string.Join(" ", r.Cells);
                return terms.All(t => haystack.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            }).ToList();
            if (sortCol <= 0) list = list.OrderByDescending(r => r.Fav).ThenByDescending(r => r.Time).ToList();   // favorites, then newest
            else { int c = sortCol - 1; list = (sortDesc ? list.OrderByDescending(r => c < r.Cells.Length ? r.Cells[c] : "") : list.OrderBy(r => c < r.Cells.Length ? r.Cells[c] : "")).ToList(); }
            shown = list;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var r in list) { var it = new ListViewItem(new[] { r.Fav ? "★" : "" }.Concat(r.Cells).ToArray()); if (r.Fav) it.ForeColor = Ui.Accent; lv.Items.Add(it); }
            lv.EndUpdate();
            count.Text = list.Count + (list.Count == all.Count ? " items" : " of " + all.Count + " items") + "   ·   Ctrl+F search, Ctrl+A select all, double-click = " + "first action, click a column to sort";
        }

        public static void ExportCsv(IWin32Window owner, string name, string[] columns, List<HistRow> rows)
        {
            using (var d = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                var sb = new StringBuilder();
                Func<string, string> esc = v => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";
                sb.AppendLine("Favorite," + string.Join(",", columns.Select(esc)));
                foreach (var r in rows) sb.AppendLine((r.Fav ? "yes" : "") + "," + string.Join(",", r.Cells.Select(esc)));
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(true));
                App.I.Status("Exported " + rows.Count + " rows.");
            }
        }
    }

    // ---------- terminal launch history ----------
    public class TermLaunch
    {
        public long Time; public string Kind = "", Title = "", Command = "", Shell = "", Layout = "";
        public List<string> Folders = new List<string>();
        public TermWorkspace Snapshot;
        public bool Fav;
    }

    static class TermHistory
    {
        public static List<TermLaunch> All { get { return Store.Settings.TermHistory; } }

        public static void Add(string kind, string title, IEnumerable<string> folders, string command, string shell, string layout, TermWorkspace snapshot = null)
        {
            var e = new TermLaunch { Snapshot = snapshot == null ? null : (TermWorkspace)DataSnapshot.Copy(snapshot), Time = Store.Now(), Kind = kind, Title = title ?? "", Folders = (folders ?? new string[0]).Where(f => !string.IsNullOrEmpty(f)).ToList(), Command = command ?? "", Shell = shell ?? "", Layout = layout ?? "" };
            var l = All;
            // The same launch again moves to the top instead of filling the list with copies.
            var same = l.FirstOrDefault(x => SameWorkspace(x.Snapshot, e.Snapshot) && x.Kind == e.Kind && x.Command == e.Command && x.Shell == e.Shell && x.Folders.SequenceEqual(e.Folders, StringComparer.OrdinalIgnoreCase));
            if (same != null) { same.Time = e.Time; same.Title = e.Title; }
            else l.Insert(0, e);
            Store.MarkSettings();
        }

        static bool SameWorkspace(TermWorkspace a, TermWorkspace b)
        {
            if (a == null || b == null) return a == b;
            if (a.Layout != b.Layout || a.Mode != b.Mode || a.Name != b.Name || a.Panes == null || b.Panes == null || a.Panes.Count != b.Panes.Count) return false;
            for (int i = 0; i < a.Panes.Count; i++)
            {
                var x = a.Panes[i]; var y = b.Panes[i];
                if (x == null || y == null || !string.Equals(x.Folder, y.Folder, StringComparison.OrdinalIgnoreCase) || x.Command != y.Command || x.Shell != y.Shell || x.Color != y.Color || x.Name != y.Name) return false;
            }
            return true;
        }
        internal static TermWorkspace Restore(TermLaunch x)
        {
            if (x.Snapshot != null && x.Snapshot.Panes != null && x.Snapshot.Panes.Count > 0 && x.Snapshot.Panes.All(p => p != null))
                return (TermWorkspace)DataSnapshot.Copy(x.Snapshot);
            var folders = x.Folders ?? new List<string>();
            var ws = new TermWorkspace { Name = string.IsNullOrEmpty(x.Title) ? "Terminal" : x.Title,
                Layout = string.IsNullOrEmpty(x.Layout) ? (folders.Count <= 6 ? Math.Max(1, folders.Count).ToString() : "4x2") : x.Layout, Mode = "tiled" };
            foreach (var f in folders.DefaultIfEmpty("")) ws.Panes.Add(new TermPane { Folder = f, Shell = string.IsNullOrEmpty(x.Shell) ? "pwsh" : x.Shell, Command = x.Kind == "workspace" ? "" : x.Command });
            return ws;
        }

        public static void Show(IWin32Window owner, Action<TermWorkspace> edit = null)
        {
            var cols = new[] { "When", "What", "Folders", "Command", "Shell" };
            Func<List<HistRow>> rows = () => All.Select(x => new HistRow
            {
                Tag = x, Fav = x.Fav, Time = new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, x.Time))),
                Cells = new[] { new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, x.Time))).ToString("yyyy-MM-dd HH:mm"), x.Kind == "workspace" ? "Workspace: " + x.Title : "Terminal", string.Join("  |  ", x.Folders), x.Command, x.Shell }
            }).ToList();
            Func<List<HistRow>, List<TermLaunch>> tags = sel => sel.Select(r => (TermLaunch)r.Tag).ToList();
            var actions = new List<HistAction>
            {
                new HistAction { Name = "Open again", Primary = true, Run = sel => { foreach (var x in tags(sel).Take(8)) Relaunch(x); } },
                new HistAction { Name = "Copy commands", Run = sel => App.I.CopyText(string.Join("\r\n", tags(sel).SelectMany(x => x.Snapshot != null ? Restore(x).Panes.Select(p => p.Command) : new[] { x.Command }).Where(c => !string.IsNullOrWhiteSpace(c)))) },
                new HistAction { Name = "Open the folders in Explorer", Run = sel => { foreach (var f in tags(sel).SelectMany(x => x.Folders).Distinct().Take(8)) try { System.Diagnostics.Process.Start("explorer.exe", "\"" + f + "\""); } catch { } } },
                new HistAction { Name = "Save the folders as a folder set", Run = sel => FolderSets.SaveNew(tags(sel).SelectMany(x => x.Folders), owner) },
                new HistAction { Name = "★ Favorite on / off", Run = sel => { foreach (var x in tags(sel)) x.Fav = !x.Fav; Store.MarkSettings(); } },
                new HistAction { Name = "Copy", Run = sel => App.I.CopyText(string.Join("\r\n", sel.Select(r => string.Join("\t", r.Cells)))) },
                new HistAction { Name = "Delete", Run = sel => { foreach (var x in tags(sel)) All.Remove(x); Store.MarkSettings(); } },
                new HistAction { Name = "Clear all but favorites", NeedsSelection = false, Run = sel => { if (MessageBox.Show(owner, "Remove every launch that is not a favorite?", "Terminal history", MessageBoxButtons.OKCancel) == DialogResult.OK) { All.RemoveAll(x => !x.Fav); Store.MarkSettings(); } } },
                new HistAction { Name = "Export CSV...", NeedsSelection = false, Run = sel => HistoryView.ExportCsv(owner, "terminal-history", cols, sel.Count > 0 ? sel : rows()) },
            };
            if (edit != null) actions.Insert(1, new HistAction { Name = "Edit as workspace", CloseAfter = true, Run = sel => edit(Restore(tags(sel).First())) });
            using (var v = new HistoryView("Terminal history", cols, new[] { 130, 170, 420, 160, 90 }, rows, actions)) v.ShowDialog(owner);
        }

        static void Relaunch(TermLaunch x)
        {
            if (x.Kind == "workspace" && x.Folders.Count > 0)
            {
                var ws = Restore(x);
                if (x.Snapshot == null && !string.IsNullOrEmpty(x.Command) && MessageBox.Show("This older history entry contains a combined command summary. Open its folders with empty startup commands?", "Terminal history", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                Term.Launch(ws);
            }
            else Term.Open(x.Folders.FirstOrDefault() ?? "", x.Command, x.Shell.Length > 0 ? x.Shell : null);
        }
    }

    // ---------- tools packed inside the exe ----------
    static class Bundled
    {
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllInOne", "tools"); } }

        public static bool Has(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("tools." + name + ".gz"); }

        // The tool's path: already unpacked, else unpacked now from the exe (a few seconds the first time). Null when not bundled.
        public static string Tool(string name)
        {
            string path = Path.Combine(Dir, name);
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("tools." + name + ".gz"))
            {
                if (s == null) return File.Exists(path) ? path : null;
                string marker = path + ".packed-size";
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
                try
                {
                    var footer = new byte[8];
                    s.Seek(-footer.Length, SeekOrigin.End);
                    if (s.Read(footer, 0, footer.Length) != footer.Length) throw new InvalidDataException("Invalid bundled resource.");
                    s.Position = 0;
                    long fileSize = BitConverter.ToUInt32(footer, 4);
                    string version = s.Length + ":" + Convert.ToBase64String(footer);
                    if (File.Exists(path) && new FileInfo(path).Length == fileSize &&
                        File.Exists(marker) && File.ReadAllText(marker) == version) return path;

                    Directory.CreateDirectory(Dir);
                    if (App.I != null) App.I.Status("Unpacking " + name + " (first use)...");
                    using (var gz = new System.IO.Compression.GZipStream(s, System.IO.Compression.CompressionMode.Decompress))
                    using (var output = File.Create(temp)) gz.CopyTo(output);
                    if (new FileInfo(temp).Length != fileSize) throw new InvalidDataException("Incomplete bundled resource.");
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                    File.WriteAllText(marker, version);
                    return path;
                }
                catch (Exception ex) { App.Log("Unpack " + name + ": " + ex.Message); return null; }
                finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
        }
    }
}
