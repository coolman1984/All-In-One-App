using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace StickyPro
{
    class NoteForm : Form
    {
        public readonly Note N;
        public bool Deleting, Exiting;
        readonly EdgePanel header, bodyHost, toolbar;
        readonly RichTextBox rtb;
        readonly IconButton btnAdd, btnPin, btnMenu, btnClose, btnClock;
        readonly Label btnRtl;
        readonly ToolTip tips = new ToolTip();
        readonly Timer saveTimer = new Timer();
        bool loading = true;

        public NoteForm(Note n)
        {
            N = n;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            Icon = App.AppIcon;
            Text = "Sticky note";
            MinimumSize = Ui.S(200, 150);
            TopMost = n.Top;

            // body (fill) first, then edges - WinForms docks the last added first.
            bodyHost = new EdgePanel();
            bodyHost.Dock = DockStyle.Fill;
            bodyHost.Padding = new Padding(Ui.S(12), Ui.S(6), Ui.S(8), Ui.S(4));
            rtb = new RichTextBox();
            rtb.Dock = DockStyle.Fill;
            rtb.BorderStyle = BorderStyle.None;
            Ui.UseFont(rtb, new Font(Ui.NoteFont, Ui.NoteFont.Style));
            rtb.DetectUrls = true;
            rtb.AcceptsTab = true;
            rtb.EnableAutoDragDrop = true;
            rtb.ScrollBars = RichTextBoxScrollBars.Vertical;
            rtb.HideSelection = false;
            rtb.LinkClicked += (s, e) => { try { Process.Start(e.LinkText); } catch { } };
            rtb.TextChanged += delegate
            {
                if (loading) return;
                saveTimer.Stop(); saveTimer.Start();
                // Auto right-to-left when an empty note starts with Arabic.
                if (!N.Rtl && rtb.TextLength > 0 && rtb.TextLength < 4 && Ui.IsRtl(rtb.Text))
                {
                    N.Rtl = true;
                    Ui.Later(1, () => { if (!IsDisposed) { SetRtl(true); rtb.Select(rtb.TextLength, 0); } });
                }
            };
            bodyHost.Controls.Add(rtb);
            Controls.Add(bodyHost);

            toolbar = new EdgePanel();
            toolbar.Dock = DockStyle.Bottom;
            toolbar.Height = Ui.S(34);
            toolbar.Padding = new Padding(Ui.S(6), 0, Ui.S(6), Ui.S(2));
            btnRtl = new IconButton("ع", 10f, "Right-to-left / Arabic (Ctrl+Shift+R)", tips, delegate { SetRtl(!N.Rtl); rtb.Focus(); });
            Ui.UseFont(btnRtl, new Font("Segoe UI", 11f, FontStyle.Bold));
            btnRtl.Dock = DockStyle.Right;
            toolbar.Controls.Add(btnRtl);
            AddTool(Ui.GImage, "Insert image", delegate { InsertImage(); });
            AddTool(Ui.GBullets, "Bullets (Ctrl+Shift+L)", delegate { ToggleBullets(); });
            AddTool("S", "Strikethrough (Ctrl+T)", delegate { ToggleStyle(FontStyle.Strikeout); });
            Ui.UseFont(toolbar.Controls[toolbar.Controls.Count - 1], new Font("Segoe UI", 10.5f, FontStyle.Strikeout));
            AddTool(Ui.GUnder, "Underline (Ctrl+U)", delegate { ToggleStyle(FontStyle.Underline); });
            AddTool(Ui.GItalic, "Italic (Ctrl+I)", delegate { ToggleStyle(FontStyle.Italic); });
            AddTool(Ui.GBold, "Bold (Ctrl+B)", delegate { ToggleStyle(FontStyle.Bold); });
            Controls.Add(toolbar);

            header = new EdgePanel();
            header.Dock = DockStyle.Top;
            header.Height = Ui.S(34);
            header.MouseDown += DragMouseDown;
            header.DoubleClick += delegate { ToggleCollapse(); };
            btnAdd = new IconButton(Ui.GAdd, 10f, "New note (Ctrl+N)", tips, delegate { App.I.NewNote(N); });
            btnAdd.Dock = DockStyle.Left;
            btnClock = new IconButton(Ui.GClock, 10f, "Reminder", tips, delegate { AskReminder(); });
            btnClock.Dock = DockStyle.Left;
            btnPin = new IconButton(Ui.GPin, 10f, "Keep on top", tips, delegate { TogglePin(); });
            btnPin.Dock = DockStyle.Right;
            btnMenu = new IconButton(Ui.GMore, 10f, "Menu", tips, delegate { ShowMenu(); });
            btnMenu.Dock = DockStyle.Right;
            btnClose = new IconButton(Ui.GClose, 10f, "Close note (Ctrl+W)", tips, delegate { Close(); });
            btnClose.Dock = DockStyle.Right;
            header.Controls.Add(btnClock);
            header.Controls.Add(btnAdd);
            header.Controls.Add(btnPin);
            header.Controls.Add(btnMenu);
            header.Controls.Add(btnClose);
            Controls.Add(header);

            saveTimer.Interval = 450;
            saveTimer.Tick += delegate { Flush(); };

            if (!string.IsNullOrEmpty(n.Rtf))
            {
                try { rtb.Rtf = n.Rtf; } catch { rtb.Text = n.Text; }
            }
            else rtb.Text = n.Text ?? "";
            try { rtb.ZoomFactor = n.Zoom; } catch { }
            ApplyRtl();

            int w = n.W > 0 ? n.W : Ui.S(320), h = n.H > 0 ? n.H : Ui.S(320);
            var rect = new Rectangle(n.X, n.Y, w, h);
            if (n.X == -1 && n.Y == -1) { var c = Cursor.Position; rect.X = c.X - Ui.S(40); rect.Y = c.Y - Ui.S(20); }
            Bounds = FitOnScreen(rect);
            ApplyColor();
            UpdatePin();
            UpdateClock();
            toolbar.Visible = false;
            loading = false;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= Native.CS_DROPSHADOW; return cp; }
        }

        static Rectangle FitOnScreen(Rectangle r)
        {
            var wa = Screen.FromRectangle(r).WorkingArea;
            if (r.Width > wa.Width) r.Width = wa.Width;
            if (r.Height > wa.Height) r.Height = wa.Height;
            if (r.Right > wa.Right) r.X = wa.Right - r.Width;
            if (r.Bottom > wa.Bottom) r.Y = wa.Bottom - r.Height;
            if (r.X < wa.Left) r.X = wa.Left;
            if (r.Y < wa.Top) r.Y = wa.Top;
            return r;
        }

        void AddTool(string glyph, string tip, EventHandler click)
        {
            var b = new IconButton(glyph, 10f, tip, tips, click);
            b.Dock = DockStyle.Left;
            b.Width = Ui.S(32);
            toolbar.Controls.Add(b);
        }

        public void ApplyColor()
        {
            var c = Ui.ColorOf(N.Color);
            BackColor = c.Body;
            header.BackColor = c.Header;
            bodyHost.BackColor = c.Body;
            toolbar.BackColor = c.Body;
            bool wasLoading = loading;
            loading = true;
            rtb.BackColor = c.Body;
            rtb.ForeColor = c.Text;
            loading = wasLoading;
            bool dark = c.Name == "Charcoal";
            Color hov = dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);
            foreach (Control x in header.Controls) { x.ForeColor = c.Text; var ib = x as IconButton; if (ib != null) ib.HoverColor = hov; }
            foreach (Control x in toolbar.Controls) { x.ForeColor = c.Text; var ib = x as IconButton; if (ib != null) ib.HoverColor = hov; }
            UpdateRtlButton();
        }

        // ---------- Right-to-left ----------

        void SetRtl(bool on)
        {
            N.Rtl = on;
            Store.MarkNotes();
            ApplyRtl();
        }

        void ApplyRtl()
        {
            bool wasLoading = loading;
            loading = true;
            int sel = rtb.SelectionStart;
            rtb.RightToLeft = N.Rtl ? RightToLeft.Yes : RightToLeft.No;
            // Paragraph alignment follows the direction.
            rtb.SelectAll();
            rtb.SelectionAlignment = N.Rtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            rtb.Select(Math.Min(sel, rtb.TextLength), 0);
            loading = wasLoading;
            UpdateRtlButton();
        }

        void UpdateRtlButton()
        {
            if (btnRtl == null) return;
            var c = Ui.ColorOf(N.Color);
            btnRtl.ForeColor = N.Rtl ? Color.FromArgb(0, 103, 192) : c.Text;
            if (c.Name == "Charcoal" && N.Rtl) btnRtl.ForeColor = Color.FromArgb(120, 190, 255);
        }

        // ---------- Pin / reminder / collapse ----------

        void UpdatePin()
        {
            btnPin.Text = N.Top ? Ui.GPinned : Ui.GPin;
            tips.SetToolTip(btnPin, N.Top ? "Unpin (stop keeping on top)" : "Keep on top");
        }

        void TogglePin()
        {
            N.Top = !N.Top;
            TopMost = N.Top;
            UpdatePin();
            Store.MarkNotes();
        }

        void UpdateClock()
        {
            btnClock.Visible = N.RemindAt > 0;
            if (N.RemindAt > 0) tips.SetToolTip(btnClock, "Reminder: " + new DateTime(N.RemindAt).ToString("ddd d MMM HH:mm") + " (click to change)");
        }

        void AskReminder()
        {
            long r = Ui.AskReminder(N.RemindAt, this);
            if (r < 0) return;
            N.RemindAt = r;
            Store.MarkNotes();
            Store.RaiseNotes();
            UpdateClock();
        }

        public void Alert()
        {
            if (expandedHeight != 0) ToggleCollapse();
            TopMost = true;
            Activate();
            var c = Ui.ColorOf(N.Color);
            int flashes = 0;
            var t = new Timer();
            t.Interval = 300;
            t.Tick += delegate
            {
                if (IsDisposed) { t.Dispose(); return; }
                flashes++;
                header.BackColor = flashes % 2 == 1 ? Color.FromArgb(255, 120, 60) : c.Header;
                if (flashes >= 8) { t.Stop(); t.Dispose(); header.BackColor = c.Header; TopMost = N.Top; }
            };
            t.Start();
            UpdateClock();
        }

        int expandedHeight;
        void ToggleCollapse()
        {
            if (expandedHeight == 0)
            {
                expandedHeight = Height;
                MinimumSize = new Size(MinimumSize.Width, header.Height);
                Height = header.Height;
            }
            else
            {
                Height = expandedHeight;
                expandedHeight = 0;
                MinimumSize = Ui.S(200, 150);
            }
        }

        void DragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST && expandedHeight == 0)
            {
                base.WndProc(ref m);
                var p = PointToClient(Cursor.Position);
                int g = Ui.S(6);
                bool l = p.X < g, r = p.X >= ClientSize.Width - g, t = p.Y < g, b = p.Y >= ClientSize.Height - g;
                if (t && l) m.Result = (IntPtr)13;
                else if (t && r) m.Result = (IntPtr)14;
                else if (b && l) m.Result = (IntPtr)16;
                else if (b && r) m.Result = (IntPtr)17;
                else if (l) m.Result = (IntPtr)10;
                else if (r) m.Result = (IntPtr)11;
                else if (t) m.Result = (IntPtr)12;
                else if (b) m.Result = (IntPtr)15;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (expandedHeight == 0) toolbar.Visible = true;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            toolbar.Visible = false;
            Flush();
        }

        protected override void OnMove(EventArgs e) { base.OnMove(e); SaveBounds(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); SaveBounds(); }

        void SaveBounds()
        {
            if (loading || WindowState != FormWindowState.Normal || !Visible) return;
            N.X = Left; N.Y = Top; N.W = Width;
            if (expandedHeight == 0) N.H = Height;
            Store.MarkNotes();
        }

        public void Flush()
        {
            saveTimer.Stop();
            if (loading || IsDisposed) return;
            string rtf;
            try { rtf = rtb.Rtf; } catch { return; }
            if (rtf != N.Rtf)
            {
                N.Rtf = rtf;
                N.Text = rtb.Text;
                N.Modified = Store.Now();
                Store.MarkNotes();
                Store.RaiseNotes();
            }
            float z = rtb.ZoomFactor;
            if (Math.Abs(z - N.Zoom) > 0.001f) { N.Zoom = z; Store.MarkNotes(); }
        }

        public void SetPlainText(string text) { rtb.Text = text; ApplyRtl(); Flush(); }
        public void FocusText() { Activate(); rtb.Focus(); rtb.SelectionStart = rtb.TextLength; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Flush();
            if (!Deleting && !Exiting)
            {
                bool empty = rtb.Text.Trim().Length == 0 && (N.Rtf == null || N.Rtf.IndexOf("\\pict", StringComparison.Ordinal) < 0 && N.Rtf.IndexOf("\\object", StringComparison.Ordinal) < 0);
                if (empty && e.CloseReason == CloseReason.UserClosing && N.RemindAt == 0) { Store.Notes.Remove(N); Store.MarkNotes(); Store.RaiseNotes(); }
                else if (e.CloseReason == CloseReason.UserClosing) { N.Open = false; Store.MarkNotes(); Store.RaiseNotes(); }
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            saveTimer.Dispose();
            tips.Dispose();
            base.OnFormClosed(e);
        }

        // ---------- Formatting ----------

        void ToggleStyle(FontStyle s)
        {
            var selected = rtb.SelectionFont;
            var f = selected ?? rtb.Font;
            try { using (var styled = new Font(f, f.Style ^ s)) rtb.SelectionFont = styled; } catch { }
            finally { if (selected != null) selected.Dispose(); }
            rtb.Focus();
        }

        void ToggleBullets()
        {
            rtb.SelectionBullet = !rtb.SelectionBullet;
            rtb.BulletIndent = Ui.S(12);
            rtb.Focus();
        }

        void InsertImage()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|All files|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var src = Image.FromFile(dlg.FileName))
                    {
                        int maxW = Ui.S(600);
                        double s = src.Width > maxW ? (double)maxW / src.Width : 1;
                        using (var bmp = new Bitmap(src, Math.Max(1, (int)(src.Width * s)), Math.Max(1, (int)(src.Height * s))))
                            PasteImage(bmp);
                    }
                }
                catch (Exception ex) { MessageBox.Show(this, "Could not insert the image: " + ex.Message, App.Name); }
            }
        }

        void PasteImage(Image img)
        {
            // RichTextBox inserts pictures through the clipboard; keep the user's clipboard intact.
            string oldText = null; Image oldImg = null;
            try { if (Clipboard.ContainsText()) oldText = Clipboard.GetText(); else if (Clipboard.ContainsImage()) oldImg = Clipboard.GetImage(); } catch { }
            var d = new DataObject();
            d.SetData(DataFormats.Bitmap, true, img);
            App.I.Suppress();
            try
            {
                Clipboard.SetDataObject(d, false, 10, 60);
                rtb.Paste(DataFormats.GetFormat(DataFormats.Bitmap));
            }
            finally
            {
                try
                {
                    App.I.Suppress();
                    if (oldText != null) Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, oldText), true, 10, 60);
                    else if (oldImg != null) Clipboard.SetDataObject(new DataObject(DataFormats.Bitmap, oldImg), true, 10, 60);
                }
                catch { }
                if (oldImg != null) oldImg.Dispose();
            }
        }

        // ---------- Menu ----------

        void ShowMenu()
        {
            var m = Ui.Menu();
            m.Font = Ui.Base;
            foreach (var c in Ui.Colors)
            {
                var col = c;
                var item = new ToolStripMenuItem(c.Name, Ui.Swatch(c.Header, Ui.S(16)), delegate
                {
                    N.Color = col.Name; N.Modified = Store.Now(); ApplyColor(); Store.MarkNotes(); Store.RaiseNotes();
                });
                item.Checked = N.Color == c.Name;
                m.Items.Add(item);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(N.RemindAt > 0 ? "Reminder: " + new DateTime(N.RemindAt).ToString("ddd d MMM HH:mm") + "..." : "Remind me...", null, delegate { AskReminder(); });
            m.Items.Add("Tags..." + (N.Tags.Trim().Length > 0 ? "  (" + N.Tags.Trim() + ")" : ""), null, delegate
            {
                var t = Ui.Ask("Note tags", "Tags, separated by commas (for example: work, ideas):", N.Tags, this);
                if (t == null) return;
                N.Tags = t.Trim(); Store.MarkNotes(); Store.RaiseNotes();
            });
            m.Items.Add((N.Rtl ? "Left-to-right text" : "Right-to-left text (Arabic)") + "\tCtrl+Shift+R", null, delegate { SetRtl(!N.Rtl); });
            m.Items.Add((N.Top ? "Unpin from top" : "Keep on top"), null, delegate { TogglePin(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Notes list", null, delegate { App.I.ShowMain("notes"); });
            m.Items.Add("Copy all text", null, delegate { App.I.CopyText(rtb.Text); });
            m.Items.Add("Save as prompt", null, delegate
            {
                Flush();
                if (rtb.Text.Trim().Length == 0) return;
                Store.NewPrompt(Store.TitleFrom(rtb.Text), rtb.Text.Trim(), "From notes");
                App.I.ShowMain("prompts");
            });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Archive note (keep it, hide it)", null, delegate { App.I.ArchiveNote(N, true); });
            m.Items.Add("Delete note\tCtrl+D", null, delegate { AskDelete(); });
            Ui.ThemeMenu(m);
            m.Closed += delegate { Ui.Later(200, () => m.Dispose()); };
            m.Show(btnMenu, new Point(0, btnMenu.Height));
        }

        void AskDelete()
        {
            if (rtb.Text.Trim().Length == 0 || MessageBox.Show(this, "Delete this note?\n\nTip: use Archive to hide a note but keep it.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                App.I.DeleteNote(N);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool handled = true;
            Keys k = e.KeyCode;
            if (e.Control && e.Shift && k == Keys.L) ToggleBullets();
            else if (e.Control && e.Shift && k == Keys.R) SetRtl(!N.Rtl);
            else if (e.Control && e.Shift && k == Keys.V)
            {
                try { var t = Clipboard.GetText(); if (!string.IsNullOrEmpty(t)) rtb.SelectedText = t; } catch { }
            }
            else if (e.Control && !e.Shift && !e.Alt)
            {
                switch (k)
                {
                    case Keys.N: App.I.NewNote(N); break;
                    case Keys.W: Close(); break;
                    case Keys.D: AskDelete(); break;
                    case Keys.B: ToggleStyle(FontStyle.Bold); break;
                    case Keys.I: ToggleStyle(FontStyle.Italic); break;
                    case Keys.U: ToggleStyle(FontStyle.Underline); break;
                    case Keys.T: ToggleStyle(FontStyle.Strikeout); break;
                    case Keys.Oemplus: case Keys.Add: rtb.ZoomFactor = Math.Min(4f, rtb.ZoomFactor + 0.1f); Flush(); break;
                    case Keys.OemMinus: case Keys.Subtract: rtb.ZoomFactor = Math.Max(0.5f, rtb.ZoomFactor - 0.1f); Flush(); break;
                    case Keys.D0: case Keys.NumPad0: rtb.ZoomFactor = 1f; Flush(); break;
                    default: handled = false; break;
                }
            }
            else handled = false;
            if (handled) { e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
    }
}
