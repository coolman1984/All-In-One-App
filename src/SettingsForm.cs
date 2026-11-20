using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    class SettingsForm : Form
    {
        readonly ComboBox theme, fontName, copyMode, colorTheme, uiFont, design;
        readonly NumericUpDown fontSize, maxClips, uiSize;
        readonly CheckBox snippets, startup, autoSave, autoCopy, activity, dockVisible, keepAwake;
        readonly TextBox snipFolder, appTitle, userName;
        readonly ComboBox headFont, monoFont, corner;
        readonly NumericUpDown headScale, monoSize;
        readonly CheckBox shadows;
        readonly CheckBox ovOn, ovDock, ovBar, ovMagnify, ovAutoHide, ovAnim, ovTaskbar, hidePopups;
        readonly ComboBox ovCorner;
        readonly NumericUpDown ovSize;
        static readonly string[] ovCorners = { "off", "topleft", "topright", "bottomleft", "bottomright" };
        readonly Button accentBtn;
        readonly Control accentSwatch;
        string accentHex = Store.Settings.AccentColor;
        static readonly int[] cornerValues = { -1, 0, 4, 8, 12, 16 };
        readonly NumericUpDown fps;
        readonly Dictionary<string, HotkeyBox> boxes = new Dictionary<string, HotkeyBox>();

        public SettingsForm(bool inspectStartup = true)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "Settings - " + App.Name;
            Icon = App.AppIcon;
            Font = Ui.Base;
            BackColor = Ui.Card;
            ForeColor = Ui.Text;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            var work = Screen.FromPoint(Cursor.Position).WorkingArea.Size;
            MaximumSize = work;
            MinimumSize = new Size(Math.Min(work.Width, Ui.S(430)), Math.Min(work.Height, Ui.S(360)));
            var frame = SizeFromClientSize(Size.Empty);
            ClientSize = new Size(Math.Min(work.Width - frame.Width, Ui.S(640)), Math.Min(work.Height - frame.Height, Ui.S(720)));
            Ui.DarkTitle(this);

            var t = new TableLayoutPanel();
            t.SuspendLayout();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(Ui.S(18), Ui.S(12), Ui.S(18), Ui.S(8));
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            t.AutoScroll = true;

            Section(t, "Look");
            theme = new ComboBox(); theme.DropDownStyle = ComboBoxStyle.DropDownList; theme.Items.AddRange(new object[] { "Light", "Dark" });
            theme.SelectedIndex = Store.Settings.Dark ? 1 : 0;
            Row(t, "Light or dark mode", theme);
            design = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            design.Items.AddRange(new object[] { "SerQ (navy sidebar, soft cards)", "Nexus (clean, light sidebar, cards)", "Classic (blue sidebar)", "Soft (charcoal sidebar, coral accent)" });
            design.SelectedIndex = Store.Settings.Design == "soft" ? 3 : Store.Settings.Design == "classic" ? 2 : Store.Settings.Design == "nexus" ? 1 : 0;
            Row(t, "Design", design);
            colorTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Ui.S(22) };
            colorTheme.Items.AddRange(Ui.ThemeNames);
            colorTheme.SelectedIndex = Math.Max(0, Math.Min(Ui.ThemeNames.Length - 1, Store.Settings.Theme));
            colorTheme.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= Ui.ThemeNames.Length) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (var b = new SolidBrush(sel ? Ui.SelBg : Ui.Field)) e.Graphics.FillRectangle(b, e.Bounds);
                var r = new Rectangle(e.Bounds.X + Ui.S(4), e.Bounds.Y + Ui.S(3), Ui.S(26), e.Bounds.Height - Ui.S(6));
                using (var b = new SolidBrush(Ui.ThemeColor(e.Index))) e.Graphics.FillRectangle(b, r);
                int left = r.Right + Ui.S(8);
                TextRenderer.DrawText(e.Graphics, Ui.ThemeNames[e.Index], colorTheme.Font,
                    new Rectangle(left, e.Bounds.Y, Math.Max(0, e.Bounds.Right - left), e.Bounds.Height), Ui.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
            };
            colorTheme.SelectedIndexChanged += delegate { ShowAccent(); };
            Row(t, "Color theme", colorTheme);
            uiFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            uiFont.Items.Add("(default: Segoe UI)");
            foreach (var n in new[] { "Segoe UI", "Segoe UI Variable Text", "Calibri", "Arial", "Tahoma", "Verdana", "Bahnschrift", "Consolas", "Cascadia Code", "Microsoft Sans Serif" })
                using (var probe = new Font(n, 10f)) if (probe.Name == n) uiFont.Items.Add(n);
            int ui = uiFont.Items.IndexOf(Store.Settings.UiFont); uiFont.SelectedIndex = ui > 0 ? ui : 0;
            Row(t, "App font", uiFont);
            uiSize = new NumericUpDown { Minimum = 8, Maximum = 16, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)Math.Max(8f, Math.Min(16f, Store.Settings.UiFontSize)) };
            Row(t, "App font size (the window reopens)", uiSize);
            headFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            headFont.Items.Add("(same as the app font)");
            foreach (var n in new[] { "Segoe UI", "Segoe UI Semibold", "Segoe UI Variable Display", "Bahnschrift", "Calibri", "Cambria", "Georgia", "Arial", "Verdana", "Trebuchet MS", "Cascadia Code" })
                if (Ui.Installed(n)) headFont.Items.Add(n);
            int hf = headFont.Items.IndexOf(Store.Settings.HeadingFont); headFont.SelectedIndex = hf > 0 ? hf : 0;
            Row(t, "Heading font (titles, big numbers)", headFont);
            headScale = new NumericUpDown { Minimum = 85, Maximum = 140, Increment = 5, Value = (decimal)Math.Round(Math.Max(0.85f, Math.Min(1.4f, Store.Settings.HeadingScale)) * 100) };
            Row(t, "Heading size (%)", headScale);
            monoFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            monoFont.Items.Add("(default: Cascadia Mono / Consolas)");
            foreach (var n in new[] { "Cascadia Mono", "Cascadia Code", "Consolas", "Lucida Console", "Courier New", "JetBrains Mono", "Fira Code", "Source Code Pro" })
                if (Ui.Installed(n)) monoFont.Items.Add(n);
            int mf = monoFont.Items.IndexOf(Store.Settings.MonoFont); monoFont.SelectedIndex = mf > 0 ? mf : 0;
            Row(t, "Code font (agent chats, code blocks)", monoFont);
            monoSize = new NumericUpDown { Minimum = 7, Maximum = 20, DecimalPlaces = 1, Increment = 0.5m, Value = (decimal)Math.Max(7f, Math.Min(20f, Store.Settings.MonoFontSize)) };
            Row(t, "Code font size", monoSize);
            var accentBar = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            accentBtn = Ui.Btn("", delegate
            {
                using (var cd = new ColorDialog { FullOpen = true, Color = AccentNow() })
                    if (cd.ShowDialog(this) == DialogResult.OK) { accentHex = ColorTranslator.ToHtml(Color.FromArgb(cd.Color.R, cd.Color.G, cd.Color.B)); ShowAccent(); }
            });
            accentSwatch = new Control { Size = Ui.S(30, 30), Margin = new Padding(0, Ui.S(2), Ui.S(6), 0) };
            accentSwatch.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Ui.Card)) e.Graphics.FillRectangle(b, accentSwatch.ClientRectangle);
                using (var path = Ui.Round(new Rectangle(1, 1, accentSwatch.Width - 3, accentSwatch.Height - 3), Ui.S(6)))
                using (var b = new SolidBrush(AccentNow())) e.Graphics.FillPath(b, path);
            };
            accentBar.Controls.Add(accentSwatch);
            accentBar.Controls.Add(accentBtn);
            accentBar.Controls.Add(Ui.Btn("Reset", delegate { accentHex = ""; ShowAccent(); }));
            ShowAccent();
            Row(t, "Accent color", accentBar);
            corner = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            corner.Items.AddRange(new object[] { "Design default", "Square", "Small", "Medium", "Large", "Extra round" });
            corner.SelectedIndex = Math.Max(0, Array.IndexOf(cornerValues, Store.Settings.CornerRadius));
            Row(t, "Corners", corner);
            shadows = new CheckBox { Text = "Soft shadows under cards (SerQ design)", AutoSize = true, Checked = Store.Settings.Shadows };
            Row(t, "Shadows", shadows);
            dockVisible = new CheckBox { Text = "Show the quick launch bar on the right", AutoSize = true, Checked = Store.Settings.DockVisible };
            Row(t, "Quick launch", dockVisible);
            keepAwake = new CheckBox { Text = "Keep the PC and display awake", AutoSize = true, Checked = Awake.On };
            Row(t, "Awake", keepAwake);
            fontName = new ComboBox(); fontName.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var f in FontFamily.Families) { using (f) fontName.Items.Add(f.Name); }
            int fi = fontName.Items.IndexOf(Store.Settings.NoteFontName);
            fontName.SelectedIndex = fi >= 0 ? fi : Math.Max(0, fontName.Items.IndexOf("Segoe UI"));
            Row(t, "Font for notes and prompts", fontName);
            fontSize = new NumericUpDown(); fontSize.Minimum = 7; fontSize.Maximum = 36; fontSize.DecimalPlaces = 1; fontSize.Increment = 0.5m;
            fontSize.Value = (decimal)Math.Max(7f, Math.Min(36f, Store.Settings.NoteFontSize));
            Row(t, "Font size", fontSize);

            Section(t, "Names and sidebar");
            appTitle = new FluentTextBox { Text = Store.Settings.AppTitle };
            Row(t, "App name (title bar, sidebar; empty = " + App.Name + ")", appTitle);
            userName = new FluentTextBox { Text = Store.Settings.UserName };
            Row(t, "Your name (agent chats and exports; empty = You)", userName);
            var navBar = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            navBar.Controls.Add(Ui.Btn("Customize sidebar...", delegate
            {
                var mf2 = Application.OpenForms.OfType<MainForm>().FirstOrDefault(f => !f.IsDisposed);
                if (mf2 == null) { MessageBox.Show(this, "Open the main window first.", App.Name); return; }
                using (var d = new SidebarDialog(mf2)) if (d.ShowDialog(this) == DialogResult.OK) { Store.MarkSettings(); mf2.BuildNav(); }
            }));
            Row(t, "Rename, reorder, hide or pin pages", navBar);

            Section(t, "Desktop overlay");
            ovOn = new CheckBox { Text = "On (dock, menu bar, all windows)", AutoSize = true, Checked = Overlay.On };
            Row(t, "Overlay", ovOn);
            var ovParts = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            ovDock = new CheckBox { Text = "Dock", AutoSize = true, Checked = Store.Settings.OverlayDock };
            ovBar = new CheckBox { Text = "Menu bar at the top", AutoSize = true, Checked = Store.Settings.OverlayMenuBar };
            ovMagnify = new CheckBox { Text = "Magnify icons", AutoSize = true, Checked = Store.Settings.OverlayMagnify };
            ovAutoHide = new CheckBox { Text = "Auto-hide the dock", AutoSize = true, Checked = Store.Settings.OverlayAutoHide };
            ovTaskbar = new CheckBox { Text = "Hide the Windows taskbar", AutoSize = true, Checked = Store.Settings.OverlayHideTaskbar };
            ovParts.Controls.AddRange(new Control[] { ovDock, ovBar, ovTaskbar, ovMagnify, ovAutoHide });
            Row(t, "Parts", ovParts);
            ovSize = new NumericUpDown { Minimum = 20, Maximum = 96, Increment = 4, Value = Math.Max(20, Math.Min(96, Store.Settings.OverlayDockSize)) };
            Row(t, "Dock icon size (px)", ovSize);
            ovCorner = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            ovCorner.Items.AddRange(new object[] { "Off", "Top left", "Top right", "Bottom left", "Bottom right" });
            ovCorner.SelectedIndex = Store.Settings.OverlayHotCorner ? Math.Max(0, Array.IndexOf(ovCorners, Store.Settings.OverlayCorner)) : 0;
            Row(t, "Hot corner: show all windows", ovCorner);
            ovAnim = new CheckBox { Text = "Animations (magnify, slide, window overview)", AutoSize = true, Checked = Store.Settings.Animations };
            Row(t, "Motion", ovAnim);

            Section(t, "Behaviour");
            hidePopups = new CheckBox { Text = "Hide \"Security Warning\" pop-ups", AutoSize = true, Checked = Store.Settings.HideSecurityPopups };
            Row(t, "Pop-ups", hidePopups);
            maxClips = new NumericUpDown(); maxClips.Minimum = 50; maxClips.Maximum = 20000; maxClips.Increment = 100;
            maxClips.Value = Math.Max(50, Math.Min(20000, Store.Settings.MaxClips));
            Row(t, "Clipboard history size (items)", maxClips);
            snippets = new CheckBox(); snippets.Text = "Expand snippet codes while I type"; snippets.AutoSize = true; snippets.Checked = Store.Settings.Snippets;
            Row(t, "Snippets", snippets);
            startup = new CheckBox(); startup.Text = "Start with Windows"; startup.AutoSize = true; startup.Checked = inspectStartup && App.IsStartup();
            Row(t, "Startup", startup);

            Section(t, "Snip, record and file activity");
            snipFolder = new FluentTextBox { Text = Shot.SnipFolder };
            Row(t, "Save snips to folder", snipFolder);
            autoSave = new CheckBox { Text = "Save every snip automatically", AutoSize = true, Checked = Store.Settings.SnipAutoSave };
            Row(t, "Auto-save", autoSave);
            autoCopy = new CheckBox { Text = "Copy every snip to the clipboard", AutoSize = true, Checked = Store.Settings.SnipAutoCopy };
            Row(t, "Auto-copy", autoCopy);
            copyMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            copyMode.Items.AddRange(new object[] { "Image", "File path", "Both" });
            copyMode.SelectedIndex = Shot.CopyMode(Store.Settings.SnipCopyMode) == "path" ? 1 : Shot.CopyMode(Store.Settings.SnipCopyMode) == "both" ? 2 : 0;
            Row(t, "After a snip, copy", copyMode);
            fps = new NumericUpDown { Minimum = 5, Maximum = 60, Value = Math.Max(5, Math.Min(60, Store.Settings.RecordFps)) };
            Row(t, "Recording frames per second", fps);
            activity = new CheckBox { Text = "Log the files I open and close", AutoSize = true, Checked = Store.Settings.ActivityOn };
            Row(t, "File activity", activity);

            Section(t, "Your data");
            var dataBar = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            dataBar.Controls.Add(Ui.Btn("Back up all data...", delegate { Backup.Export(this); }));
            dataBar.Controls.Add(Ui.Btn("Restore a backup...", delegate { if (Backup.Import(this)) DialogResult = DialogResult.Cancel; }));
            Row(t, "Notes, prompts, clips, cards, settings", dataBar);
            var whereBar = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            var where = new Label { AutoSize = true, MaximumSize = new Size(Ui.S(280), 0), ForeColor = Ui.Muted, Text = Store.Dir, Margin = new Padding(0, Ui.S(6), Ui.S(8), Ui.S(4)) };
            whereBar.Controls.Add(where); whereBar.SetFlowBreak(where, true);
            whereBar.Controls.Add(Ui.Btn("Open", delegate { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Store.Dir + "\""); } catch { } }));
            whereBar.Controls.Add(Ui.Btn("Move data folder...", delegate { MoveData(where); }));
            Row(t, "Data folder", whereBar);

            Section(t, "AI agents (MCP)");
            string exe = Application.ExecutablePath;
            var mcpBar = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            mcpBar.Controls.Add(Ui.Btn("Copy Claude Code command", delegate { App.I.CopyText("claude mcp add --scope user all-in-one -- \"" + exe + "\" --mcp"); App.I.Status("Copied. Run it in a terminal."); }));
            mcpBar.Controls.Add(Ui.Btn("Copy Codex command", delegate { App.I.CopyText("codex mcp add all-in-one -- \"" + exe + "\" --mcp"); App.I.Status("Copied. Run it in a terminal."); }));
            mcpBar.Controls.Add(Ui.Btn("Copy JSON config", delegate
            {
                App.I.CopyText("{\n  \"mcpServers\": {\n    \"all-in-one\": {\n      \"command\": \"" + exe.Replace("\\", "\\\\") + "\",\n      \"args\": [\"--mcp\"]\n    }\n  }\n}");
                App.I.Status("Copied. Paste it into the agent's MCP settings (Cline, Antigravity, Cursor...).");
            }));
            Row(t, "Let any AI agent read and organize your notes, tasks, prompts, clips and folders, and ask the coding agents", mcpBar);

            Section(t, "Shortcuts  (click a box, press the keys; Backspace = off)");
            foreach (var a in Hotkeys.Actions)
            {
                var hb = new HotkeyBox();
                hb.Text = Hotkeys.Get(a[0]);
                boxes[a[0]] = hb;
                Row(t, a[1], hb);
            }

            var bar = LayoutKit.Actions(DockStyle.Bottom);
            var ok = Ui.Btn("Save", delegate { Save(); }, true);
            var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
            var reset = Ui.Btn("Default shortcuts", delegate { foreach (var a in Hotkeys.Actions) boxes[a[0]].Text = a[2]; });
            bar.Controls.Add(ok); bar.Controls.Add(cancel); bar.Controls.Add(reset);
            Controls.Add(t);
            Controls.Add(bar);
            CancelButton = cancel;
            int layoutWidth = -1;
            Action fitRows = delegate
            {
                int width = t.ClientSize.Width;
                if (width == layoutWidth) return;
                layoutWidth = width;
                int innerWidth = Math.Max(1, width - t.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                int labelWidth = innerWidth * 42 / 100;
                t.SuspendLayout();
                try
                {
                    foreach (Control child in t.Controls)
                    {
                        if (child is Label)
                            child.MaximumSize = new Size(Math.Max(1, (t.GetColumnSpan(child) > 1 ? innerWidth : labelWidth) - child.Margin.Horizontal), 0);
                        else if (child is CheckBox || child is FlowLayoutPanel)
                            child.MaximumSize = new Size(Math.Max(1, innerWidth - labelWidth - child.Margin.Horizontal), 0);
                    }
                }
                finally { t.ResumeLayout(true); }
            };
            Ui.Theme(this);
            t.ClientSizeChanged += delegate { fitRows(); };
            fitRows();
            t.ResumeLayout(true);
        }

        Color AccentNow()
        {
            if (!string.IsNullOrEmpty(accentHex)) try { return ColorTranslator.FromHtml(accentHex); } catch { }
            return Ui.ThemeColor(colorTheme != null ? colorTheme.SelectedIndex : Store.Settings.Theme);
        }

        void ShowAccent()
        {
            if (accentSwatch != null) accentSwatch.Invalidate();
            accentBtn.Text = string.IsNullOrEmpty(accentHex) ? "Theme color" : accentHex.ToUpperInvariant();
        }

        void MoveData(Label where)
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the new data folder. Your data is copied there; the old files stay where they are.", ShowNewFolderButton = true })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string err = Store.MoveDataTo(d.SelectedPath);
                if (err != null) { MessageBox.Show(this, "Could not move the data folder: " + err, App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                where.Text = d.SelectedPath + "  (after restart)";
                if (MessageBox.Show(this, "Your data was copied to:\n" + d.SelectedPath + "\n\nThe app uses it after a restart. Restart now?", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    App.I.RestartNow();
            }
        }

        static void Section(TableLayoutPanel t, string text)
        {
            var l = new Label(); l.Text = text; l.Font = Ui.Bold; l.AutoSize = true; l.Margin = new Padding(0, Ui.S(14), 0, Ui.S(6));
            t.Controls.Add(l);
            t.SetColumnSpan(l, 2);
        }

        static void Row(TableLayoutPanel t, string label, Control c)
        {
            var l = new Label(); l.Text = label; l.AutoSize = true; l.Margin = new Padding(0, Ui.S(6), Ui.S(8), Ui.S(4));
            c.AccessibleName = label;
            var number = c as NumericUpDown;
            if (number != null) number.BorderStyle = BorderStyle.FixedSingle;
            c.Margin = new Padding(0, Ui.S(2), 0, Ui.S(4));
            c.Dock = DockStyle.Fill;
            t.Controls.Add(l);
            t.Controls.Add(c);
        }

        void Save()
        {
            // Check shortcuts: valid and not duplicated.
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in Hotkeys.Actions)
            {
                string k = boxes[a[0]].Text.Trim();
                if (k.Length == 0) continue;
                uint mods; Keys key;
                if (!Hotkeys.Parse(k, out mods, out key))
                {
                    MessageBox.Show(this, "\"" + k + "\" for \"" + a[1] + "\" is not a valid shortcut. Use Ctrl, Alt, Shift or Win plus a key.", App.Name);
                    return;
                }
                string other;
                string identity = mods + ":" + (int)key;
                if (seen.TryGetValue(identity, out other))
                {
                    MessageBox.Show(this, k + " is used twice: \"" + other + "\" and \"" + a[1] + "\".", App.Name);
                    return;
                }
                seen[identity] = a[1];
            }
            var s = Store.Settings;
            s.Dark = theme.SelectedIndex == 1;
            s.Theme = colorTheme.SelectedIndex;
            s.Design = design.SelectedIndex == 3 ? "soft" : design.SelectedIndex == 2 ? "classic" : design.SelectedIndex == 1 ? "nexus" : "serq";
            s.UiFont = uiFont.SelectedIndex > 0 ? (string)uiFont.SelectedItem : "";
            s.UiFontSize = (float)uiSize.Value;
            s.HeadingFont = headFont.SelectedIndex > 0 ? (string)headFont.SelectedItem : "";
            s.HeadingScale = (float)headScale.Value / 100f;
            s.MonoFont = monoFont.SelectedIndex > 0 ? (string)monoFont.SelectedItem : "";
            s.MonoFontSize = (float)monoSize.Value;
            s.AccentColor = accentHex ?? "";
            s.CornerRadius = cornerValues[Math.Max(0, corner.SelectedIndex)];
            s.Shadows = shadows.Checked;
            s.AppTitle = appTitle.Text.Trim();
            s.UserName = userName.Text.Trim();
            s.DockVisible = dockVisible.Checked;
            if (keepAwake.Checked != Awake.On) Awake.Set(keepAwake.Checked);
            s.HideSecurityPopups = hidePopups.Checked; PopupHider.Apply();
            s.OverlayDock = ovDock.Checked; s.OverlayMenuBar = ovBar.Checked; s.OverlayHideTaskbar = ovTaskbar.Checked; s.OverlayMagnify = ovMagnify.Checked; s.OverlayAutoHide = ovAutoHide.Checked;
            s.OverlayDockSize = (int)ovSize.Value; s.Animations = ovAnim.Checked;
            s.OverlayHotCorner = ovCorner.SelectedIndex > 0;
            if (ovCorner.SelectedIndex > 0) s.OverlayCorner = ovCorners[ovCorner.SelectedIndex];
            if (ovOn.Checked != Overlay.On) Overlay.Set(ovOn.Checked); else Overlay.ApplySettings();
            s.NoteFontName = fontName.SelectedItem as string ?? "Segoe UI";
            s.NoteFontSize = (float)fontSize.Value;
            s.MaxClips = (int)maxClips.Value;
            s.Snippets = snippets.Checked;
            s.Hotkeys = Hotkeys.Actions.ToDictionary(a => a[0], a => boxes[a[0]].Text.Trim());
            if (startup.Checked != App.IsStartup()) App.SetStartup(startup.Checked);
            s.SnipFolder = snipFolder.Text.Trim();
            s.SnipAutoSave = autoSave.Checked;
            s.SnipAutoCopy = autoCopy.Checked;
            s.SnipCopyMode = copyMode.SelectedIndex == 1 ? "path" : copyMode.SelectedIndex == 2 ? "both" : "image";
            s.RecordFps = (int)fps.Value;
            if (s.ActivityOn != activity.Checked)
            {
                s.ActivityOn = activity.Checked;
                if (s.ActivityOn) ActivityMonitor.Start(); else ActivityMonitor.Stop();
            }
            DialogResult = DialogResult.OK;
        }
    }

    // Text box that records a key combination like "Ctrl+Alt+V".
    class HotkeyBox : FluentTextBox
    {
        public HotkeyBox()
        {
            ReadOnly = true;
            Cursor = Cursors.Hand;
            ShortcutsEnabled = false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) return base.ProcessCmdKey(ref msg, keyData);
            if (key == Keys.Back || key == Keys.Delete) { Text = ""; return true; }
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            bool ctrl = (keyData & Keys.Control) != 0, alt = (keyData & Keys.Alt) != 0, shift = (keyData & Keys.Shift) != 0;
            bool win = Native.Down(0x5B) || Native.Down(0x5C);
            if (!ctrl && !alt && !win && !(key >= Keys.F1 && key <= Keys.F24)) return true; // need a modifier
            Text = Hotkeys.Format(ctrl, alt, shift, win, key);
            return true;
        }
    }
}
