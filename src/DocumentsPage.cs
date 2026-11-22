using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Documents: a read-only viewer for Excel, PowerPoint, Word, Visio and PDF files, in tabs, inside the app.
    // Office draws them (its own previewers, the ones Windows Explorer uses), so company-protected (NASCA) files open too.
    // Protected PDFs are shown by Foxit PDF Reader inside the tab. Emails (.eml, .msg, .mysingle) open on the Browser page.
    class DocumentsPage : Panel
    {
        public static readonly string[] Types = { ".xlsx", ".xlsm", ".xlsb", ".xls", ".csv", ".pptx", ".pptm", ".ppt", ".ppsx", ".docx", ".docm", ".doc", ".rtf", ".vsdx", ".vsd", ".pdf" };
        public static bool Handles(string path) { return Types.Contains(Path.GetExtension(path ?? "").ToLowerInvariant()); }

        readonly EditorTabs tabs;
        readonly Panel host;
        readonly Label info, empty;
        readonly Dictionary<string, OfficeView> views = new Dictionary<string, OfficeView>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, OfficeEdit> edits = new Dictionary<string, OfficeEdit>(StringComparer.OrdinalIgnoreCase);
        readonly Button modeBtn, saveBtn;
        readonly List<Button> documentActions = new List<Button>();
        static bool EditMode { get { return Store.Settings.ViewerEdit; } }
        // A file opened with an explicit choice (Explorer: preview / edit) keeps it; others follow Edit mode.
        readonly Dictionary<string, bool> chosen = Store.Settings.ViewerTabModes;
        bool EditFor(string path) { bool e; return chosen.TryGetValue(path, out e) ? e : EditMode; }
        string current;
        const int MaxLive = 6;   // previewers kept alive; older tabs reopen when you go back to them

        public DocumentsPage()
        {
            Dock = DockStyle.Fill; Visible = false;
            var bar = MainForm.Bar();
            bar.Controls.Add(MainForm.Heading("Documents"));
            bar.Controls.Add(Ui.Btn("Open...", delegate { Pick(); }, true));
            // Edit: the real Excel / Word / PowerPoint inside the tab (full ribbon, Ctrl+S). View: fast, read only.
            modeBtn = Ui.Btn(EditMode ? "Edit mode: on" : "Edit mode: off", delegate { SetMode(!EditMode); });
            Tip(modeBtn, "On: Excel, Word and PowerPoint open inside the tab and you can edit and save. Off: a fast read-only view.");
            bar.Controls.Add(modeBtn);
            saveBtn = Ui.Btn("Save", delegate
            {
                OfficeEdit editor;
                if (current != null && edits.TryGetValue(current, out editor))
                {
                    string error;
                    SaveDocument(current, out error);
                }
            });
            Tip(saveBtn, "Save the file (Ctrl+S inside the document does the same)");
            bar.Controls.Add(saveBtn);
            saveBtn.Enabled = false;
            AddDocumentAction(bar, Ui.Btn("Open in its app", delegate { if (current != null) try { Process.Start(new ProcessStartInfo(current) { UseShellExecute = true }); } catch (Exception ex) { info.Text = ex.Message; } }));
            AddDocumentAction(bar, Ui.Btn("Show in Explorer", delegate { if (current != null) App.I.OpenInExplorerPage(Path.GetDirectoryName(current)); }));
            AddDocumentAction(bar, Ui.Btn("Reload", delegate { if (current != null) { if (DropEdit(current, true)) { DropView(current); Show(current); } } }));
            AddDocumentAction(bar, Ui.Btn("Copy path", delegate { if (current != null) App.I.CopyText(current); }));
            var ask = Ui.Btn("Ask the Assistant", delegate { if (current != null) { if (!Store.Settings.AssistantOpen) App.I.ToggleAssistant(); } });
            AddDocumentAction(bar, ask);

            tabs = new EditorTabs { Dock = DockStyle.Top, EmptyText = "No open documents  ·  Ctrl+O opens one" };
            tabs.SetAll(Store.Settings.ViewerTabs.Where(File.Exists));
            foreach (var path in chosen.Keys.Where(p => !tabs.Paths.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList()) chosen.Remove(path);
            tabs.Activated += p => Show(p);
            tabs.CanClose = p => DropEdit(p, true);
            tabs.AllClosed += p => { Drop(p); current = null; ShowEmpty(); };
            tabs.Changed += () =>
            {
                foreach (var gone in views.Keys.Concat(chosen.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Where(k => !tabs.Paths.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList()) Drop(gone);
                Store.Settings.ViewerTabs = tabs.Paths.ToList(); Store.MarkSettings();
            };
            info = new Label { Dock = DockStyle.Bottom, Height = Ui.S(22), ForeColor = Ui.Muted, Font = Ui.Small, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            host = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Card };
            empty = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Ui.Muted, Font = Ui.Base,
                Text = "Open or drop an Office document or PDF here.\r\n\r\nEdit mode lets you edit Excel, Word and PowerPoint files.\r\nVisio and PDF files open in preview.\r\n\r\nCtrl+W closes a tab. Ctrl+Tab switches tabs."
            };
            host.Controls.Add(empty);
            var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Ui.S(4)) };
            center.Controls.Add(host); center.Controls.Add(info); center.Controls.Add(tabs);
            Controls.Add(center); Controls.Add(bar);
            AllowDrop = true;
            DragEnter += (s, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            DragDrop += (s, e) => { var files = e.Data.GetData(DataFormats.FileDrop) as string[]; if (files != null) foreach (var f in files.Take(12)) Open(f); };
            Ui.Theme(this);
            host.BackColor = Ui.Card;
        }

        void AddDocumentAction(Control bar, Button button)
        {
            button.Enabled = false;
            documentActions.Add(button);
            bar.Controls.Add(button);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && current == null && tabs.Paths.Count > 0) Show(tabs.Paths[0]);
        }

        void Pick()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Filter = "Documents|" + string.Join(";", Types.Select(t => "*" + t)) + "|Emails|*.eml;*.msg;*.mysingle|All files|*.*" })
                if (d.ShowDialog(FindForm()) == DialogResult.OK) foreach (var f in d.FileNames) Open(f);
        }

        public string CurrentPath { get { return current; } }
        public List<string> OpenTabs { get { return tabs != null ? tabs.Paths.ToList() : new List<string>(); } }
        public bool IsEditing(string path) { return !string.IsNullOrEmpty(path) && edits.ContainsKey(path); }
        public string ActiveKind
        {
            get
            {
                OfficeEdit e;
                if (current != null && edits.TryGetValue(current, out e)) return e.Kind ?? "";
                if (current == null) return "";
                return OfficeEdit.KindOf(current) ?? Path.GetExtension(current).TrimStart('.').ToLowerInvariant();
            }
        }
        public OfficeEdit ActiveEditor { get { OfficeEdit e; return current != null && edits.TryGetValue(current, out e) ? e : null; } }
        public OfficeEdit EditorFor(string path) { OfficeEdit e; return !string.IsNullOrEmpty(path) && edits.TryGetValue(path, out e) ? e : null; }

        public bool SwitchTo(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string match = tabs != null ? tabs.Paths.FirstOrDefault(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) : null;
            if (match == null && tabs != null)
            {
                var byName = tabs.Paths.Where(p => string.Equals(Path.GetFileName(p), path, StringComparison.OrdinalIgnoreCase)).ToList();
                if (byName.Count == 1) match = byName[0];
            }
            if (match != null)
            {
                Show(match);
                return true;
            }
            if (File.Exists(path))
            {
                Open(path, null);
                return string.Equals(current, path, StringComparison.OrdinalIgnoreCase)
                    || (tabs != null && tabs.Paths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)));
            }
            return false;
        }

        public bool SaveDocument(string path, out string error)
        {
            error = null;
            string target = string.IsNullOrWhiteSpace(path) ? current : path;
            if (string.IsNullOrWhiteSpace(target)) { error = "No active document."; return false; }
            OfficeEdit editor;
            if (!edits.TryGetValue(target, out editor))
            {
                var matches = edits.Where(kv => string.Equals(Path.GetFileName(kv.Key), target, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                if (matches.Count > 1) { error = "Multiple edited documents match. Pass the full path: " + target; return false; }
                if (matches.Count == 1) { target = matches[0].Key; editor = matches[0].Value; }
            }
            if (editor == null) { error = "Document is not open in Edit mode: " + target; return false; }
            bool ok = editor.Save();
            error = editor.Error;
            if (string.Equals(target, current, StringComparison.OrdinalIgnoreCase))
            {
                info.ForeColor = ok ? Ui.Muted : Ui.Warning;
                info.Text = editor.Error ?? (current + "   ·   saved");
            }
            return ok;
        }

        // Any file: documents open here, emails and web files on the Browser page, the rest in their own app.
        public void Open(string path) { Open(path, null); }
        public void Open(string path, bool? edit)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (edit.HasValue)
            {
                if (!edit.Value && !DropEdit(path, true)) return;
                chosen[path] = edit.Value;
                Store.MarkSettings();
            }
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".eml" || ext == ".msg" || ext == ".mysingle" || ext == ".htm" || ext == ".html" || ext == ".mht") { App.I.OpenInBrowser(path); return; }
            if (!Handles(path) && !OfficeView.CanView(ext)) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { info.Text = ex.Message; } return; }
            Show(path);
        }

        void Show(string path)
        {
            current = path;
            foreach (var button in documentActions) button.Enabled = true;
            string editError = null;
            tabs.Show(path);
            info.ForeColor = Ui.Muted;
            OfficeEdit ed;
            if (EditFor(path) && OfficeEdit.KindOf(path) != null)
            {
                if (!edits.TryGetValue(path, out ed))
                {
                    // the read-only preview keeps the file open: release it before Office opens the file for editing
                    OfficeView pv; if (views.TryGetValue(path, out pv)) { views.Remove(path); host.Controls.Remove(pv); pv.Dispose(); }
                    ed = new OfficeEdit { Dock = DockStyle.Fill, Visible = false };
                    host.Controls.Add(ed);
                    foreach (Control c in host.Controls) c.Visible = c == ed;
                    info.Text = "Opening " + Path.GetFileName(path) + " in " + OfficeEdit.KindOf(path) + " for editing...";
                    if (!ed.Open(path)) { editError = "Not editable here: " + ed.Error; host.Controls.Remove(ed); ed.Dispose(); }
                    else
                    {
                        edits[path] = ed;
                        string p2 = path;
                        ed.Lost += () => { OfficeEdit gone; if (edits.TryGetValue(p2, out gone)) { edits.Remove(p2); host.Controls.Remove(gone); gone.Dispose(); } if (current == p2) Show(p2); };
                    }
                }
                if (edits.TryGetValue(path, out ed))
                {
                    foreach (Control c in host.Controls) c.Visible = c == ed;
                    saveBtn.Enabled = true;
                    info.Text = path + "   ·   editing in " + ed.Kind + " (Ctrl+S saves)" + (Mail.IsDrm(path) ? "   ·   protected (NASCA)" : "");
                    SqliteVault.QueueExtract(path);
                    return;
                }
            }
            saveBtn.Enabled = false;
            OfficeView v;
            if (!views.TryGetValue(path, out v))
            {
                v = new OfficeView { Dock = DockStyle.Fill, Visible = false };
                host.Controls.Add(v);
                views[path] = v;
                foreach (var old in views.Keys.Where(k => k != path).Take(Math.Max(0, views.Count - MaxLive)).ToList()) DropView(old);
            }
            foreach (Control c in host.Controls) c.Visible = c == v;
            if (v.Path == null)
            {
                info.Text = "Opening " + Path.GetFileName(path) + "...";
                bool protectedPdf = Path.GetExtension(path).ToLowerInvariant() == ".pdf" && Mail.IsDrm(path);
                bool ok = protectedPdf ? v.OpenInFoxit(path) : v.Open(path);
                if (!ok) { info.Text = editError == null ? v.Error : editError + "   ·   " + v.Error; info.ForeColor = Ui.Warning; return; }
            }
            long size = 0; try { size = new FileInfo(path).Length; } catch (IOException) { }
            info.Text = (editError != null ? editError + "   ·   " : "") + path + "   ·   " + (size / 1024.0 / 1024.0).ToString("0.0") + " MB" + (Mail.IsDrm(path) ? "   ·   protected (NASCA)" : "") + "   ·   read only";
            info.ForeColor = editError != null ? Ui.Warning : Ui.Muted;
            SqliteVault.QueueExtract(path);
        }
        void SetMode(bool edit)
        {
            if (!edit && !ConfirmCloseEditors()) return;
            Store.Settings.ViewerEdit = edit; Store.MarkSettings(); chosen.Clear();
            modeBtn.Text = edit ? "Edit mode: on" : "Edit mode: off";
            if (!edit) foreach (var k in edits.Keys.ToList()) DropEdit(k, false);
            if (current != null) Show(current);
        }

        public bool ConfirmCloseEditors()
        {
            foreach (var editor in edits.Values.ToList()) if (!editor.ConfirmClose()) return false;
            return true;
        }

        bool DropEdit(string path, bool ask)
        {
            OfficeEdit editor;
            if (!edits.TryGetValue(path, out editor)) return true;
            if (!editor.Close(ask)) return false;
            edits.Remove(path);
            host.Controls.Remove(editor);
            editor.Dispose();
            return true;
        }

        static readonly ToolTip tips = new ToolTip();
        static void Tip(Control c, string t) { tips.SetToolTip(c, t); }

        void Drop(string path)
        {
            chosen.Remove(path);
            DropView(path);
        }

        void DropView(string path)
        {
            OfficeView v;
            if (!views.TryGetValue(path, out v)) return;
            views.Remove(path);
            host.Controls.Remove(v);
            v.Dispose();
        }

        void ShowEmpty()
        {
            foreach (Control c in host.Controls) c.Visible = c == empty;
            foreach (var button in documentActions) button.Enabled = false;
            saveBtn.Enabled = false;
            info.Text = "";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.W)) { tabs.CloseActive(); return true; }
            if (keyData == (Keys.Control | Keys.Tab)) { tabs.Next(1); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { tabs.Next(-1); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.T)) { tabs.ReopenClosed(); return true; }
            if (keyData == (Keys.Control | Keys.O)) { Pick(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing) { if (disposing) { foreach (var k in edits.Keys.ToList()) DropEdit(k, false); foreach (var k in views.Keys.ToList()) DropView(k); } base.Dispose(disposing); }
    }
}
