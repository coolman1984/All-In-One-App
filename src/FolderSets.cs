using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Folder sets: named groups of folders you always open together (for example "Reporting" = 3 project folders).
    // One shared list, used the same way in Explorer, Terminals and Agents: save the folders you have open as a set,
    // open a set in one click (one pane per folder), and manage them: favorite, order, rename, edit the folders,
    // duplicate, delete, export / import to share. Kept in the settings file; AI agents see them through MCP.
    // ===================================================================================================

    public class FolderSet
    {
        public string Name = "", Note = "";
        public bool Fav;
        public List<string> Folders = new List<string>();
        public long Created, Used;
        public int Uses;
    }

    static class FolderSets
    {
        public static List<FolderSet> All { get { return Store.Settings.FolderSets; } }
        public static event Action Changed;

        public static void Save() { Store.MarkSettings(); var h = Changed; if (h != null) h(); }
        public static void Touch(FolderSet s) { s.Used = Store.Now(); s.Uses++; foreach (var f in s.Folders) MetaStore.Touch("folders", f); Save(); }

        // Favorites first, then your order.
        public static List<FolderSet> Ordered() { return All.Where(s => s.Fav).Concat(All.Where(s => !s.Fav)).ToList(); }

        public static FolderSet SaveNew(IEnumerable<string> folders, IWin32Window owner)
        {
            var list = Clean(folders);
            if (list.Count == 0) { MessageBox.Show(owner, "There are no folders open to save.", "Folder sets"); return null; }
            string guess = string.Join(" + ", list.Take(3).Select(Short)) + (list.Count > 3 ? " +" + (list.Count - 3) : "");
            var name = Ui.Ask("Save these " + list.Count + " folders as a set", "Name", guess, owner);
            if (string.IsNullOrWhiteSpace(name)) return null;
            var s = new FolderSet { Name = name.Trim(), Folders = list, Created = Store.Now() };
            All.Add(s); Save();
            App.I.Status("Folder set saved: " + s.Name);
            return s;
        }

        public static List<string> Clean(IEnumerable<string> folders)
        {
            var l = new List<string>();
            foreach (var f in folders ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(f)) continue;
                string p = f.Trim().TrimEnd('\\'); if (p.Length == 2 && p[1] == ':') p += "\\";
                if (!l.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) l.Add(p);
            }
            return l;
        }

        public static string Short(string path) { string n = Path.GetFileName(path.TrimEnd('\\')); return string.IsNullOrEmpty(n) ? path : n; }
        public static string Label(FolderSet s) { return (s.Fav ? "★  " : "") + s.Name + "   (" + s.Folders.Count + (s.Folders.Count == 1 ? " folder)" : " folders)"); }

        // The "Folder sets" menu of a page: save what is open, open a set, manage.
        public static void ShowMenu(Control anchor, Point at, Func<List<string>> openFolders, Action<FolderSet> open, int maxPanes, string pageName)
        {
            var m = Ui.Menu(); Ui.ThemeMenu(m);
            m.Items.Add("Save the open folders as a set...", null, delegate { SaveNew(openFolders(), anchor.FindForm()); });
            var sets = Ordered();
            if (sets.Count > 0) m.Items.Add(new ToolStripSeparator());
            foreach (var s in sets.Take(30))
            {
                var ss = s;
                var item = new ToolStripMenuItem(Label(ss) + (ss.Folders.Count > maxPanes ? "   (first " + maxPanes + " here)" : ""), null, delegate { Touch(ss); open(ss); });
                item.ToolTipText = string.Join("\n", ss.Folders) + (ss.Note.Length > 0 ? "\n\n" + ss.Note : "");
                m.Items.Add(item);
            }
            if (sets.Count == 0) m.Items.Add(new ToolStripMenuItem("No sets yet: open your folders, then save them as a set") { Enabled = false });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Manage folder sets...", null, delegate
            {
                using (var dlg = new FolderSetManager(openFolders, s => { Touch(s); open(s); }, pageName)) dlg.ShowDialog(anchor.FindForm());
            });
            m.ShowItemToolTips = true;
            m.Show(anchor, at);
        }

        public static void Export(IWin32Window owner)
        {
            using (var d = new SaveFileDialog { Filter = "Folder sets (*.json)|*.json", FileName = "folder-sets.json" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                File.WriteAllText(d.FileName, new JavaScriptSerializer().Serialize(All), new UTF8Encoding(false));
                App.I.Status("Exported " + All.Count + " folder sets.");
            }
        }

        public static void Import(IWin32Window owner)
        {
            using (var d = new OpenFileDialog { Filter = "Folder sets (*.json)|*.json" })
            {
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    var list = new JavaScriptSerializer().Deserialize<List<FolderSet>>(File.ReadAllText(d.FileName, Encoding.UTF8)) ?? new List<FolderSet>();
                    int n = 0;
                    foreach (var s in list.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name)))
                    {
                        s.Folders = Clean(s.Folders); if (s.Note == null) s.Note = "";
                        if (All.Any(x => x.Name == s.Name && x.Folders.SequenceEqual(s.Folders))) continue;
                        All.Add(s); n++;
                    }
                    Save(); App.I.Status("Imported " + n + " folder sets.");
                }
                catch (Exception ex) { MessageBox.Show(owner, "Could not read the file: " + ex.Message, "Folder sets"); }
            }
        }
    }

    // ---------- manage sets: left the sets, right the folders of the selected set ----------
    class FolderSetManager : Form
    {
        readonly ListBox sets, folders;
        readonly TextBox note;
        readonly Func<List<string>> openFolders;
        readonly Action<FolderSet> open;
        bool filling;

        public FolderSetManager(Func<List<string>> openFolders, Action<FolderSet> open, string pageName)
        {
            this.openFolders = openFolders; this.open = open;
            Text = "Folder sets"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(Ui.S(960), Ui.S(560)); Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text;
            sets = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
            folders = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.MultiExtended };
            note = new TextBox { Dock = DockStyle.Bottom, Height = Ui.S(54), Multiline = true, ScrollBars = ScrollBars.Vertical };

            var setBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(92), WrapContents = true };
            Action<string, Action<FolderSet, int>> sb = (t, a) => setBtns.Controls.Add(Ui.Btn(t, delegate { int i = sets.SelectedIndex; if (i < 0 && t != "New from open folders" && t != "Import...") return; a(i >= 0 ? FolderSets.All[i] : null, i); Fill(sets.SelectedIndex); }));
            setBtns.Controls.Add(Ui.Btn("Open in " + pageName, delegate { var s = Current; if (s == null) return; open(s); Close(); }, true));
            sb("New from open folders", (s, i) => { var n = FolderSets.SaveNew(openFolders(), this); if (n != null) Fill(FolderSets.All.Count - 1); });
            sb("Rename", (s, i) => { var n = Ui.Ask("Rename set", "Name", s.Name, this); if (!string.IsNullOrWhiteSpace(n)) { s.Name = n.Trim(); FolderSets.Save(); } });
            sb("★ Favorite", (s, i) => { s.Fav = !s.Fav; FolderSets.Save(); });
            sb("Duplicate", (s, i) => { FolderSets.All.Insert(i + 1, new FolderSet { Name = s.Name + " (copy)", Folders = s.Folders.ToList(), Note = s.Note, Created = Store.Now() }); FolderSets.Save(); });
            sb("Move up", (s, i) => MoveSet(i, -1));
            sb("Move down", (s, i) => MoveSet(i, 1));
            sb("Delete", (s, i) => { if (MessageBox.Show(this, "Delete the set \"" + s.Name + "\"? (The folders are not touched.)", "Folder sets", MessageBoxButtons.OKCancel) == DialogResult.OK) { FolderSets.All.RemoveAt(i); FolderSets.Save(); } });
            sb("Export...", (s, i) => FolderSets.Export(this));
            sb("Import...", (s, i) => FolderSets.Import(this));

            var folderBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(46), WrapContents = true };
            folderBtns.Controls.Add(Ui.Btn("Add folder...", delegate
            {
                var s = Current; if (s == null) return;
                using (var d = new FolderBrowserDialog()) if (d.ShowDialog(this) == DialogResult.OK) { s.Folders = FolderSets.Clean(s.Folders.Concat(new[] { d.SelectedPath })); FolderSets.Save(); ShowFolders(); }
            }));
            folderBtns.Controls.Add(Ui.Btn("Add the open folders", delegate { var s = Current; if (s == null) return; s.Folders = FolderSets.Clean(s.Folders.Concat(openFolders())); FolderSets.Save(); ShowFolders(); }));
            folderBtns.Controls.Add(Ui.Btn("Remove", delegate { var s = Current; if (s == null) return; foreach (int k in folders.SelectedIndices.Cast<int>().OrderByDescending(k => k)) s.Folders.RemoveAt(k); FolderSets.Save(); ShowFolders(); }));
            folderBtns.Controls.Add(Ui.Btn("Up", delegate { MoveFolder(-1); }));
            folderBtns.Controls.Add(Ui.Btn("Down", delegate { MoveFolder(1); }));

            var left = new Panel { Dock = DockStyle.Left, Width = Ui.S(420), Padding = new Padding(Ui.S(10)) };
            left.Controls.Add(sets); left.Controls.Add(setBtns);
            left.Controls.Add(new Label { Dock = DockStyle.Top, Height = Ui.S(24), Text = "Sets (favorites ★ first in menus)", ForeColor = Ui.Muted });
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(10)) };
            right.Controls.Add(folders); right.Controls.Add(folderBtns);
            right.Controls.Add(new Label { Dock = DockStyle.Bottom, Height = Ui.S(22), Text = "Note", ForeColor = Ui.Muted });
            right.Controls.Add(note);
            right.Controls.Add(new Label { Dock = DockStyle.Top, Height = Ui.S(24), Text = "Folders in this set (in order: pane 1, 2, 3...)", ForeColor = Ui.Muted });
            // Docking: the note sits under the buttons; re-order so the list fills the rest.
            note.BringToFront(); folders.BringToFront();
            Controls.Add(right); Controls.Add(left);

            sets.SelectedIndexChanged += delegate { if (!filling) ShowFolders(); };
            sets.DoubleClick += delegate { var s = Current; if (s != null) { open(s); Close(); } };
            folders.DoubleClick += delegate { var s = Current; if (s != null && folders.SelectedIndex >= 0) try { System.Diagnostics.Process.Start("explorer.exe", "\"" + s.Folders[folders.SelectedIndex] + "\""); } catch { } };
            note.TextChanged += delegate { var s = Current; if (s != null && !filling && s.Note != note.Text) { s.Note = note.Text; Store.MarkSettings(); } };
            Ui.Theme(this);
            Fill(0);
        }

        FolderSet Current { get { int i = sets.SelectedIndex; return i >= 0 && i < FolderSets.All.Count ? FolderSets.All[i] : null; } }

        void Fill(int sel)
        {
            filling = true;
            sets.BeginUpdate(); sets.Items.Clear();
            foreach (var s in FolderSets.All) sets.Items.Add(FolderSets.Label(s) + (s.Uses > 0 ? "   · used " + s.Uses + "x" : ""));
            sets.EndUpdate();
            if (sets.Items.Count > 0) sets.SelectedIndex = Math.Max(0, Math.Min(sel, sets.Items.Count - 1));
            filling = false;
            ShowFolders();
        }

        void ShowFolders()
        {
            filling = true;
            var s = Current;
            folders.BeginUpdate(); folders.Items.Clear();
            if (s != null) for (int i = 0; i < s.Folders.Count; i++) folders.Items.Add((i + 1) + ".  " + s.Folders[i] + (Directory.Exists(s.Folders[i]) ? "" : "   (missing)"));
            folders.EndUpdate();
            note.Text = s != null ? s.Note : ""; note.Enabled = s != null;
            filling = false;
        }

        void MoveSet(int i, int d) { int j = i + d; var L = FolderSets.All; if (j < 0 || j >= L.Count) return; var s = L[i]; L.RemoveAt(i); L.Insert(j, s); FolderSets.Save(); sets.SelectedIndex = j; }

        void MoveFolder(int d)
        {
            var s = Current; int i = folders.SelectedIndex; if (s == null || i < 0) return;
            int j = i + d; if (j < 0 || j >= s.Folders.Count) return;
            var f = s.Folders[i]; s.Folders.RemoveAt(i); s.Folders.Insert(j, f); FolderSets.Save(); ShowFolders();
            folders.ClearSelected(); folders.SelectedIndex = j;
        }
    }
}
