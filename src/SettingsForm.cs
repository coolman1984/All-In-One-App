using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    class SettingsForm : Form
    {
        readonly ComboBox theme, fontName;
        readonly NumericUpDown fontSize, maxClips;
        readonly CheckBox snippets, startup, autoSave, autoCopy, activity;
        readonly TextBox snipFolder;
        readonly NumericUpDown fps;
        readonly Dictionary<string, HotkeyBox> boxes = new Dictionary<string, HotkeyBox>();

        public SettingsForm()
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = "Settings - " + App.Name;
            Icon = App.AppIcon;
            Font = Ui.Base;
            BackColor = Ui.Card;
            ForeColor = Ui.Text;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ClientSize = Ui.S(560, 720);
            Ui.DarkTitle(this);

            var t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(Ui.S(18), Ui.S(12), Ui.S(18), Ui.S(8));
            t.ColumnCount = 2;
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(250)));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.AutoScroll = true;

            Section(t, "Look");
            theme = new ComboBox(); theme.DropDownStyle = ComboBoxStyle.DropDownList; theme.Items.AddRange(new object[] { "Light", "Dark" });
            theme.SelectedIndex = Store.Settings.Dark ? 1 : 0;
            Row(t, "Theme (main window and popups)", theme);
            fontName = new ComboBox(); fontName.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (var f in FontFamily.Families) fontName.Items.Add(f.Name);
            int fi = fontName.Items.IndexOf(Store.Settings.NoteFontName);
            fontName.SelectedIndex = fi >= 0 ? fi : Math.Max(0, fontName.Items.IndexOf("Segoe UI"));
            Row(t, "Font for notes and prompts", fontName);
            fontSize = new NumericUpDown(); fontSize.Minimum = 7; fontSize.Maximum = 36; fontSize.DecimalPlaces = 1; fontSize.Increment = 0.5m;
            fontSize.Value = (decimal)Math.Max(7f, Math.Min(36f, Store.Settings.NoteFontSize));
            Row(t, "Font size", fontSize);

            Section(t, "Behaviour");
            maxClips = new NumericUpDown(); maxClips.Minimum = 50; maxClips.Maximum = 20000; maxClips.Increment = 100;
            maxClips.Value = Math.Max(50, Math.Min(20000, Store.Settings.MaxClips));
            Row(t, "Clipboard history size (items)", maxClips);
            snippets = new CheckBox(); snippets.Text = "Expand snippet codes while I type"; snippets.AutoSize = true; snippets.Checked = Store.Settings.Snippets;
            Row(t, "Snippets", snippets);
            startup = new CheckBox(); startup.Text = "Start with Windows"; startup.AutoSize = true; startup.Checked = App.IsStartup();
            Row(t, "Startup", startup);

            Section(t, "Snip, record and file activity");
            snipFolder = new TextBox { Text = Shot.SnipFolder };
            Row(t, "Save snips to folder", snipFolder);
            autoSave = new CheckBox { Text = "Save every snip automatically", AutoSize = true, Checked = Store.Settings.SnipAutoSave };
            Row(t, "Auto-save", autoSave);
            autoCopy = new CheckBox { Text = "Copy every snip to the clipboard", AutoSize = true, Checked = Store.Settings.SnipAutoCopy };
            Row(t, "Auto-copy", autoCopy);
            fps = new NumericUpDown { Minimum = 5, Maximum = 60, Value = Math.Max(5, Math.Min(60, Store.Settings.RecordFps)) };
            Row(t, "Recording frames per second", fps);
            activity = new CheckBox { Text = "Log the files I open and close", AutoSize = true, Checked = Store.Settings.ActivityOn };
            Row(t, "File activity", activity);

            Section(t, "Shortcuts  (click a box, press the keys; Backspace = off)");
            foreach (var a in Hotkeys.Actions)
            {
                var hb = new HotkeyBox();
                hb.Text = Hotkeys.Get(a[0]);
                boxes[a[0]] = hb;
                Row(t, a[1], hb);
            }

            var bar = new FlowLayoutPanel(); bar.Dock = DockStyle.Bottom; bar.Height = Ui.S(50); bar.FlowDirection = FlowDirection.RightToLeft; bar.Padding = new Padding(Ui.S(12), Ui.S(8), Ui.S(12), 0);
            var ok = Ui.Btn("Save", delegate { Save(); }, true);
            var cancel = Ui.Btn("Cancel", null); cancel.DialogResult = DialogResult.Cancel;
            var reset = Ui.Btn("Default shortcuts", delegate { foreach (var a in Hotkeys.Actions) boxes[a[0]].Text = a[2]; });
            bar.Controls.Add(ok); bar.Controls.Add(cancel); bar.Controls.Add(reset);
            Controls.Add(t);
            Controls.Add(bar);
            CancelButton = cancel;
            Ui.Theme(this);
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
            c.Margin = new Padding(0, Ui.S(2), 0, Ui.S(4));
            if (!(c is CheckBox)) c.Width = Ui.S(250);
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
            s.NoteFontName = fontName.SelectedItem as string ?? "Segoe UI";
            s.NoteFontSize = (float)fontSize.Value;
            s.MaxClips = (int)maxClips.Value;
            s.Snippets = snippets.Checked;
            s.Hotkeys = Hotkeys.Actions.ToDictionary(a => a[0], a => boxes[a[0]].Text.Trim());
            if (startup.Checked != App.IsStartup()) App.SetStartup(startup.Checked);
            s.SnipFolder = snipFolder.Text.Trim();
            s.SnipAutoSave = autoSave.Checked;
            s.SnipAutoCopy = autoCopy.Checked;
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
    class HotkeyBox : TextBox
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
