using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StickyPro
{
    class App : ApplicationContext
    {
        public static App I;
        public static Icon AppIcon;
        public const string Name = "All in One";

        public Control Sync;
        NotifyIcon tray;
        MsgWindow msg;
        MainForm main;
        QuickPopup quick;
        readonly Dictionary<string, NoteForm> noteForms = new Dictionary<string, NoteForm>();
        bool notesHidden;
        DateTime suppressUntil = DateTime.MinValue;
        System.Windows.Forms.Timer clipTimer, reminderTimer;
        string pendingSource = "";
        int clipAttempts;
        ToolStripMenuItem miPause, miStartup, miQueue;
        readonly Dictionary<uint, string> procNames = new Dictionary<uint, string>();
        readonly List<int> registered = new List<int>();

        // Paste queue
        bool queueOn;
        readonly List<Clip> queue = new List<Clip>();

        public App(bool startup)
        {
            I = this;
            Sync = new Control();
            var unused = Sync.Handle;
            AppIcon = new Icon(new MemoryStream(Ui.IcoBytes()));

            Store.Load();
            Ui.Dark = Store.Settings.Dark;
            Ui.ThemeIndex = Store.Settings.Theme;
            Ui.SetDesignFlags(Store.Settings.Design);
            Ui.ApplyLook();
            Ui.SetNoteFont(Store.Settings.NoteFontName, Store.Settings.NoteFontSize);
            BuildTray();
            if (Store.MoveError != null) Balloon("Data folder move failed", "Using the original folder. " + Store.MoveError);
            if (Store.Settings.KeepAwake) Awake.Set(true);
            try { PopupHider.Apply(); } catch (Exception ex) { Log("Popup hider: " + ex.Message); }
            Ui.Later(30000, () => { try { CliUpdates.AutoCheck(); } catch (Exception ex) { Log("CLI updates: " + ex.Message); } });   // once a day, in the background

            msg = new MsgWindow(this);
            var failed = RegisterHotkeys();
            if (!Native.AddClipboardFormatListener(msg.Handle)) Log("Clipboard listener failed");

            clipTimer = new System.Windows.Forms.Timer();
            clipTimer.Interval = 120;
            clipTimer.Tick += delegate { clipTimer.Stop(); CaptureClipboard(); };

            reminderTimer = new System.Windows.Forms.Timer();
            reminderTimer.Interval = 15000;
            reminderTimer.Tick += delegate { CheckReminders(); };
            reminderTimer.Start();

            if (!Store.Settings.FirstRunDone)
            {
                Store.Settings.FirstRunDone = true;
                Store.MarkSettings();
                if (!Install.InTemp) SetStartup(true);
                if (Store.Prompts.Count == 0) SeedPrompts();
                if (!Install.IsInstalledCopy && !Install.IsDevCopy) Ui.Later(800, () => Install.OfferOnFirstRun());
            }
            MigrateStartup();
            miStartup.Checked = IsStartup();

            Store.PromptsChanged += Snippets.Update;
            Snippets.Update();
            Ui.Later(3000, ActivityMonitor.Start);
            Ui.Later(4000, () => { try { CronEngine.Start(); } catch (Exception ex) { Log("Cron engine: " + ex.Message); } });

            foreach (var n in Store.Notes.Where(n => n.Open && !n.Archived).ToList()) OpenNote(n);
            foreach (var d in Store.Cards.Where(c => c.Visible).ToList()) OpenCard(d, false);
            if (!startup) ShowMain(null);
            if (failed.Count > 0)
                Balloon("Some shortcuts are used by another app", string.Join(", ", failed) + " could not be registered. Change them in Settings.");

            Microsoft.Win32.SystemEvents.SessionEnding += delegate { try { Overlay.Shutdown(); } catch { } try { Snippets.Stop(); } catch { } try { CronEngine.Stop(); } catch { } FlushAll(); try { AgentStore.FlushWrites(); } catch { } try { SqliteVault.Close(); } catch { } };
            Application.ApplicationExit += delegate { try { CronEngine.Stop(); } catch { } FlushAll(); Snippets.Stop(); try { SqliteVault.Close(); } catch { } };
            Ui.Later(1500, CheckReminders);
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerMode;
        }

        // ---------- Hotkeys ----------

        public List<string> RegisterHotkeys()
        {
            foreach (int id in registered) Native.UnregisterHotKey(msg.Handle, id);
            registered.Clear();
            var failed = new List<string>();
            for (int i = 0; i < Hotkeys.Actions.Length; i++)
            {
                string combo = Hotkeys.Get(Hotkeys.Actions[i][0]);
                uint mods; Keys key;
                if (!Hotkeys.Parse(combo, out mods, out key)) continue; // empty = disabled
                int id = i + 1;
                if (Native.RegisterHotKey(msg.Handle, id, mods | Native.MOD_NOREPEAT, (uint)key)) registered.Add(id);
                else failed.Add(combo);
            }
            var launchers = Store.Settings.Launchers;
            for (int i = 0; i < launchers.Count; i++)
            {
                uint mods; Keys key;
                if (!Hotkeys.Parse(launchers[i].Keys, out mods, out key)) continue;
                int id = 1000 + i;
                if (Native.RegisterHotKey(msg.Handle, id, mods | Native.MOD_NOREPEAT, (uint)key)) registered.Add(id);
                else failed.Add(launchers[i].Keys + " (" + launchers[i].Name + ")");
            }
            if (main != null && !main.IsDisposed) main.UpdateShortcutHelp();
            return failed;
        }

        public void OnHotkey(int id)
        {
            if (id >= 1000)
            {
                int li = id - 1000;
                if (li < Store.Settings.Launchers.Count) { var u = Store.Settings.Launchers[li]; QuickDock.Launch(new DockItem { Name = u.Name, Path = u.Target, Args = u.Args }); }
                return;
            }
            if (id < 1 || id > Hotkeys.Actions.Length) return;
            switch (Hotkeys.Actions[id - 1][0])
            {
                case "note": NewNote(null); break;
                case "clips": ShowQuick("clips"); break;
                case "prompts": ShowQuick("prompts"); break;
                case "addprompt": AddSelectionAsPrompt(); break;
                case "main": ShowMain(null); break;
                case "toggle": ToggleNotes(); break;
                case "plain": PastePlain(); break;
                case "queue": ToggleQueue(); break;
                case "next": PasteNext(); break;
                case "cards": ToggleCards(); break;
                case "snip": StartSnip("rect", 0, false); break;
                case "repeat": StartSnip("repeat", 0, false); break;
                case "picker": case "magnifier": case "whiteboard": Accessories.Open(Hotkeys.Actions[id - 1][0]); break;
                case "ocr": StartSnip("rect", 0, true); break;
                case "record": StartRecord("rect"); break;
                case "tagfile": TagExplorerSelection(); break;
                case "newtask": QuickTaskForm.Open(); break;
                case "run": QuickRunForm.Open(); break;
                case "ontop": PowerTools.ToggleForeground(); break;
                case "pwshhere": Term.OpenFromExplorer(""); break;
                case "terminals": Term.LaunchCurrent(); break;
                case "overview": Overlay.ShowOverview(); break;
                case "overlay": Overlay.Toggle(); break;
                case "awake": Awake.Set(!Awake.On); Balloon(Awake.On ? "Keep awake: ON" : "Keep awake: OFF", Awake.On ? "The PC will not sleep." : "Normal sleep settings."); break;
            }
        }

        // ---------- Snip / record / tag ----------

        bool snipping;

        public void StartSnip(string mode, int delaySec, bool ocrOnly)
        {
            if (snipping) return;
            if (mode == "fixed" && !CaptureCommands.FixedSize(false)) return;
            snipping = true; // Reserve the overlay during the delay as well.
            bool restoreMain = main != null && !main.IsDisposed && main.Visible && main.WindowState != FormWindowState.Minimized && Form.ActiveForm == main;
            if (restoreMain) main.Hide();
            Action go = () =>
            {
                Bitmap pic = null; Rectangle selected = Rectangle.Empty;
                try
                {
                    if (mode == "repeat")
                    {
                        if (Store.Settings.LastCaptureMode == "scroll") selected = new Rectangle(Store.Settings.LastX, Store.Settings.LastY, Store.Settings.LastW, Store.Settings.LastH);
                        else pic = CaptureCommands.Repeat();
                    }
                    else using (var ov = new SnipOverlay(mode == "scroll" ? "control" : mode, mode == "scroll"))
                    {
                        if (ov.ShowDialog() == DialogResult.OK)
                        {
                            selected = ov.Result; pic = ov.Picture; ov.Picture = null;
                            if (!ocrOnly) CaptureCommands.Remember(mode == "scroll" ? "scroll" : ov.Mode, ov.Result, ov.Mode == "free" ? ov.FreePolygon : null);
                        }
                    }
                    if ((mode == "scroll" || (mode == "repeat" && Store.Settings.LastCaptureMode == "scroll")) && selected.Width > 0)
                    {
                        var scrolling = new ScrollingCapture(selected, result =>
                        {
                            snipping = false;
                            if (restoreMain && main != null && !main.IsDisposed) main.Show();
                            if (result != null) FinishSnip(result, false);
                            else Balloon("No scrolling capture", "Choose a scrollable content area and try again.");
                        });
                        scrolling.Show(); return;
                    }
                }
                catch (Exception ex) { Log("Snip failed: " + ex.Message); Balloon("Could not capture", ex.Message); }
                snipping = false;
                if (restoreMain && main != null && !main.IsDisposed) main.Show();
                if (pic != null) FinishSnip(pic, ocrOnly);
            };
            if (delaySec > 0) { Balloon("Snip in " + delaySec + " seconds", "Get the screen ready."); Ui.Later(delaySec * 1000, go); }
            else Ui.Later(restoreMain ? 300 : 120, go);
        }

        void FinishSnip(Bitmap pic, bool ocrOnly)
        {
                if (pic == null) return;
                if (ocrOnly)
                {
                    Balloon("Reading text...", "One moment.");
                    Ocr.Run(pic, Store.Settings.OcrLang, Sync, (text, err) =>
                    {
                        if (err != null) Balloon("No text found", err);
                        else { CopyText(text); Balloon("Text copied", Ui.OneLine(text, 150)); }
                    });
                    pic.Dispose(); // OCR has already saved its independent temporary image.
                    return;
                }
                string saved = null;
                if (Shot.NeedsSavedFile(Store.Settings))
                {
                    try { saved = Path.Combine(Shot.SnipFolder, Shot.NewName(".png")); pic.Save(saved, System.Drawing.Imaging.ImageFormat.Png); }
                    catch (Exception ex) { saved = null; Log("Snip save: " + ex.Message); }
                }
                if (Store.Settings.SnipAutoCopy)
                {
                    try { Clipboard.SetDataObject(Shot.SnipData(pic, saved, Store.Settings.SnipCopyMode), true, 10, 60); }
                    catch (Exception ex) { Log("Snip copy: " + ex.Message); Balloon("Snip was not copied", ex.Message); }
                }
                SnipPanel.RaiseChanged();
                var ed = new SnipEditor(pic, saved);
                ed.Show();
                ed.Activate();
        }

        public void StartRecord(string mode)
        {
            if (Recorder.Active) { Recorder.Stop(true); return; }
            if (Recorder.Saving) { Balloon("Recording is saving", "Wait for the current recording to finish saving."); return; }
            if (snipping) return;
            snipping = true;
            bool restoreMain = main != null && !main.IsDisposed && main.Visible && Form.ActiveForm == main;
            if (restoreMain) main.Hide();
            Ui.Later(restoreMain ? 300 : 120, () =>
            {
                Rectangle area = Rectangle.Empty;
                snipping = true;
                try { using (var ov = new SnipOverlay(mode, true)) if (ov.ShowDialog() == DialogResult.OK) area = ov.Result; }
                finally { snipping = false; }
                if (area.Width >= 16 && area.Height >= 16) Recorder.Start(area, Store.Settings.RecordMic);
                else if (restoreMain && main != null && !main.IsDisposed) main.Show();
            });
        }

        void TagExplorerSelection()
        {
            var fg = Native.GetForegroundWindow();
            var paths = ExplorerSelection.Get(fg);
            if (paths.Count == 0) { Balloon("Nothing to tag", "Select files or folders in a File Explorer window, then press " + Hotkeys.Get("tagfile") + "."); return; }
            TaggedPanel.AskTag(paths, null);
        }

        // ---------- Floating cards ----------

        readonly Dictionary<string, CardForm> cardForms = new Dictionary<string, CardForm>();

        public void OpenCard(CardDef d, bool activate)
        {
            CardForm f;
            if (cardForms.TryGetValue(d.Id, out f) && !f.IsDisposed)
            {
                if (!f.Visible) f.Show();
                if (activate) { if (d.Collapsed) f.Expand(); f.Activate(); }
                return;
            }
            if (!d.Visible) { d.Visible = true; Store.MarkCards(); }
            f = new CardForm(d);
            cardForms[d.Id] = f;
            CardForm.StartPasteTracking();
            f.FormClosed += delegate
            {
                cardForms.Remove(d.Id);
                if (cardForms.Count == 0) CardForm.StopPasteTracking();
            };
            f.Show();
            Native.EnsureRestored(f);
            if (activate) f.Activate();
            Store.RaiseCards();
        }

        public void NewCard(List<string> features, string title)
        {
            var d = new CardDef();
            d.Id = Store.NewId();
            d.Features = features.ToList();
            d.Title = string.IsNullOrWhiteSpace(title) ? (features.Count == 1 ? Features.Title(features[0]) : "Card " + (Store.Cards.Count + 1)) : title;
            d.Active = features[0];
            if (features.Any(x => x == "procs" || x == "drives" || x == "startup")) { d.W = Ui.S(560); d.H = Ui.S(520); }
            Store.Cards.Add(d);
            Store.MarkCards();
            OpenCard(d, true);
        }

        public void UpdateCard(CardDef d, string title, List<string> features)
        {
            d.Title = title;
            CardForm f;
            if (cardForms.TryGetValue(d.Id, out f) && !f.IsDisposed) { f.Text = title; f.SetFeatures(features); }
            else { d.Features = features.ToList(); if (!features.Contains(d.Active ?? "")) d.Active = features[0]; }
            Store.MarkCards();
            Store.RaiseCards();
        }

        public void CloseCard(CardDef d)
        {
            d.Visible = false;
            CardForm f;
            if (cardForms.TryGetValue(d.Id, out f) && !f.IsDisposed) f.Close();
            Store.MarkCards();
            Store.RaiseCards();
        }

        public void DeleteCard(CardDef d)
        {
            CardForm f;
            if (cardForms.TryGetValue(d.Id, out f) && !f.IsDisposed) f.Close();
            Store.Cards.Remove(d);
            Store.MarkCards();
            Store.RaiseCards();
        }

        bool cardsHidden;
        public void ToggleCards()
        {
            var open = cardForms.Values.Where(f => !f.IsDisposed).ToList();
            if (open.Count > 0 && !cardsHidden && open.Any(f => f.Visible))
            {
                foreach (var f in open) f.Hide();
                cardsHidden = true;
                return;
            }
            cardsHidden = false;
            if (open.Count == 0)
            {
                var defs = Store.Cards.ToList();
                if (defs.Count == 0) { StarterCards(); return; }
                foreach (var d in defs) OpenCard(d, false);
                return;
            }
            foreach (var f in open) f.Show();
        }

        public void StarterCards()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            var a = new CardDef { Id = Store.NewId(), Title = "Clipboard & prompts", Features = new List<string> { "clips", "prompts", "notes" }, Active = "clips", W = Ui.S(380), H = Ui.S(520), X = wa.Right - Ui.S(400), Y = wa.Top + Ui.S(40) };
            var b = new CardDef { Id = Store.NewId(), Title = "System", Features = new List<string> { "procs", "drives", "startup" }, Active = "procs", W = Ui.S(600), H = Ui.S(540), X = wa.Right - Ui.S(1020), Y = wa.Top + Ui.S(40) };
            Store.Cards.Add(a);
            Store.Cards.Add(b);
            Store.MarkCards();
            OpenCard(a, false);
            OpenCard(b, true);
        }

        // ---------- Admin ----------

        public void RestartElevated()
        {
            try
            {
                FlushAll();
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--elevated") { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                Exit();
            }
            catch (System.ComponentModel.Win32Exception) { /* user said no */ }
            catch (Exception ex) { Balloon("Could not restart as admin", ex.Message); }
        }

        void SeedPrompts()
        {
            var p = Store.NewPrompt("Example: explain simply", "Explain {{topic}} in simple words for someone who is not technical. Use short sentences and one example.", "Examples");
            p.Tags = "example, template";
            Store.NewPrompt("Example: improve this text", "Improve the following text. Keep my meaning, fix grammar, make it clear and professional:\n\n{{text}}", "Examples");
        }

        // ---------- Tray ----------

        void BuildTray()
        {
            var menu = Ui.Menu(true);
            menu.Items.Add("Open " + Name, null, delegate { ShowMain(null); }).Font = Ui.Bold;
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("New note", null, delegate { NewNote(null); });
            menu.Items.Add("New task / reminder...", null, delegate { QuickTaskForm.Open(); });
            menu.Items.Add("Scheduled jobs...", null, delegate { CronWindow.Open(main != null && main.Visible ? main : null); });
            menu.Items.Add("Quick run...", null, delegate { QuickRunForm.Open(); });
            menu.Items.Add("New snip", null, delegate { StartSnip("rect", 0, false); });
            var capture = new ToolStripMenuItem("Screen capture");
            foreach (var m in CaptureCommands.Modes) { string mode = m[0]; capture.DropDownItems.Add(m[1], null, delegate { StartSnip(mode, 0, false); }); }
            capture.DropDownItems.Add("Scrolling Window", null, delegate { StartSnip("scroll", 0, false); });
            capture.DropDownItems.Add("Repeat Last Capture", null, delegate { StartSnip("repeat", 0, false); });
            capture.DropDownItems.Add(new ToolStripSeparator());
            capture.DropDownItems.Add("Text from screen (OCR)", null, delegate { StartSnip("rect", 0, true); });
            capture.DropDownItems.Add("Record screen", null, delegate { StartRecord("rect"); });
            menu.Items.Add(capture);
            menu.Items.Add(Accessories.MenuItem());
            var terms = new ToolStripMenuItem("Terminals");
            terms.DropDownItems.Add("Launch my terminals workspace", null, delegate { Term.LaunchCurrent(); });
            terms.DropDownItems.Add("PWSH here (File Explorer folder)", null, delegate { Term.OpenFromExplorer(""); });
            terms.DropDownItems.Add("Claude here (File Explorer folder)", null, delegate { Term.OpenFromExplorer("claude"); });
            terms.DropDownItems.Add("Terminals page...", null, delegate { ShowMain("terminals"); });
            menu.Items.Add(terms);
            menu.Items.Add(new ToolStripSeparator());
            var open = new ToolStripMenuItem("Open page");
            open.DropDownItems.Add("Home", null, delegate { ShowMain("dashboard"); });
            open.DropDownItems.Add("Terminals", null, delegate { ShowMain("terminals"); });
            open.DropDownItems.Add("Tasks & reminders", null, delegate { ShowMain("tasks"); });
            open.DropDownItems.Add("Sticky notes", null, delegate { ShowMain("notes"); });
            open.DropDownItems.Add("Prompts & snippets", null, delegate { ShowMain("prompts"); });
            open.DropDownItems.Add("Clipboard history", null, delegate { ShowMain("clips"); });
            open.DropDownItems.Add("System monitor", null, delegate { ShowMain("system"); });
            open.DropDownItems.Add("Snip & record", null, delegate { ShowMain("snip"); });
            open.DropDownItems.Add("File activity", null, delegate { ShowMain("activity"); });
            open.DropDownItems.Add("Tagged files", null, delegate { ShowMain("tagged"); });
            open.DropDownItems.Add("Explorer", null, delegate { ShowMain("explorer"); });
            open.DropDownItems.Add("Chrome bookmarks", null, delegate { ShowMain("chrome"); });
            open.DropDownItems.Add("Uninstall programs", null, delegate { ShowMain("system:uninstall"); });
            open.DropDownItems.Add("Power tools", null, delegate { ShowMain("power"); });
            open.DropDownItems.Add("Shortcuts", null, delegate { ShowMain("shortcuts"); });
            menu.Items.Add(open);
            menu.Items.Add("Show / hide notes", null, delegate { ToggleNotes(); });
            menu.Items.Add("Show / hide floating cards", null, delegate { ToggleCards(); });
            menu.Items.Add(new ToolStripSeparator());
            miQueue = new ToolStripMenuItem("Paste queue: collect what I copy");
            miQueue.Click += delegate { ToggleQueue(); };
            menu.Items.Add(miQueue);
            miPause = new ToolStripMenuItem("Pause clipboard capture");
            miPause.Checked = Store.Settings.Paused;
            miPause.Click += delegate { SetPaused(!Store.Settings.Paused); };
            menu.Items.Add(miPause);
            var miAwake = new ToolStripMenuItem("Keep the PC awake");
            miAwake.Checked = Store.Settings.KeepAwake;
            miAwake.Click += delegate { Awake.Set(!Awake.On); };
            Awake.Changed += delegate { miAwake.Checked = Awake.On; };
            menu.Items.Add(miAwake);
            var miOverlay = new ToolStripMenuItem("Desktop overlay (dock and menu bar)");
            miOverlay.Checked = Store.Settings.OverlayOn;
            miOverlay.Click += delegate { Overlay.Toggle(); };
            Overlay.Changed += delegate { miOverlay.Checked = Overlay.On; };
            menu.Items.Add(miOverlay);
            miStartup = new ToolStripMenuItem("Start with Windows");
            miStartup.Click += delegate { SetStartup(!IsStartup()); miStartup.Checked = IsStartup(); };
            menu.Items.Add(miStartup);
            if (!Install.IsInstalledCopy && !Install.IsDevCopy) menu.Items.Add("Install for this Windows user...", null, delegate { Install.Run(true); });
            menu.Items.Add("Settings...", null, delegate { ShowSettings(null); });
            menu.Items.Add("Open data folder", null, delegate { try { Process.Start("explorer.exe", "\"" + Store.Dir + "\""); } catch { } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { Exit(); });
            Ui.ThemeMenu(menu);

            tray = new NotifyIcon();
            tray.Icon = AppIcon;
            tray.Text = Name;
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(null); };
        }

        public void Balloon(string title, string text)
        {
            try { tray.ShowBalloonTip(3000, title, text, ToolTipIcon.Info); } catch { }
        }

        public void SetPaused(bool paused)
        {
            Store.Settings.Paused = paused;
            Store.MarkSettings();
            miPause.Checked = paused;
            if (main != null && !main.IsDisposed) main.UpdatePaused();
        }

        void UpdateTrayText()
        {
            string t = Name;
            if (queueOn || queue.Count > 0) t += " - queue: " + queue.Count;
            if (t.Length > 63) t = t.Substring(0, 63);
            tray.Text = t;
            miQueue.Checked = queueOn;
            miQueue.Text = queueOn ? "Paste queue: ON (" + queue.Count + " items)" : "Paste queue: collect what I copy";
        }

        // ---------- Settings / theme ----------

        // Everything that changes how the main window is drawn; a change rebuilds the window.
        static string LookKey()
        {
            var s = Store.Settings;
            return string.Join("|", new object[] { s.Dark, s.Theme, s.Design, s.UiFont, s.UiFontSize, s.HeadingFont, s.HeadingScale, s.AccentColor, s.CornerRadius, s.Shadows, s.AppTitle, s.MonoFont, s.MonoFontSize,
                string.Join(",", s.NavOrder), string.Join(",", s.NavHidden), string.Join(",", s.NavFavorites), string.Join(",", s.PageNames.Select(p => p.Key + "=" + p.Value)) });
        }

        public void ShowSettings(IWin32Window owner)
        {
            string before = LookKey();
            using (var f = new SettingsForm())
            {
                if (f.ShowDialog(owner) != DialogResult.OK) return;
            }
            Store.MarkSettings();
            Ui.SetNoteFont(Store.Settings.NoteFontName, Store.Settings.NoteFontSize);
            Store.TrimNow();
            Snippets.Update();
            miStartup.Checked = IsStartup();
            var failed = RegisterHotkeys();
            if (failed.Count > 0)
                MessageBox.Show("These shortcuts are already used by another app and do not work:\n\n" + string.Join("\n", failed) + "\n\nPick other keys in Settings.", Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (main != null && !main.IsDisposed) main.SetDockVisible(Store.Settings.DockVisible);
            if (LookKey() != before) { Ui.ApplyLook(); ApplyTheme(); }
        }

        public void ToggleDark() { Store.Settings.Dark = !Store.Settings.Dark; Store.MarkSettings(); ApplyTheme(); }
        public void SetTheme(int i) { Store.Settings.Theme = Math.Max(0, Math.Min(Ui.ThemeNames.Length - 1, i)); Store.Settings.AccentColor = ""; Store.MarkSettings(); Ui.ApplyLook(); ApplyTheme(); }
        public void SetDesign(string d) { Store.Settings.Design = Ui.NormalizeDesign(d); Store.Settings.Theme = Ui.DefaultThemeFor(Store.Settings.Design); Store.MarkSettings(); ApplyTheme(); }
        // One click from the look picker: design, colour and light/dark together, one rebuild.
        public void SetLook(string design, int theme, bool dark)
        {
            var s = Store.Settings;
            s.Design = Ui.NormalizeDesign(design);
            if (theme >= 0) { s.Theme = Math.Max(0, Math.Min(Ui.ThemeNames.Length - 1, theme)); s.AccentColor = ""; }
            s.Dark = dark;
            Store.MarkSettings(); Ui.ApplyLook(); ApplyTheme();
        }

        void ApplyTheme()
        {
            if (!ConfirmDocumentClose())
            {
                Status("Appearance changes will apply after restarting All in One.");
                return;
            }
            Ui.Dark = Store.Settings.Dark;
            Ui.ThemeIndex = Store.Settings.Theme;
            Ui.SetDesignFlags(Store.Settings.Design);
            Ui.ThemeMenu(tray.ContextMenuStrip);
            if (Overlay.On) Overlay.ApplySettings();   // dock and menu bar repaint in the new theme
            string page = null;
            bool wasVisible = false;
            if (main != null && !main.IsDisposed)
            {
                wasVisible = main.Visible;
                page = main.Page;
                main.Exiting = true;
                main.Close();
                main.Dispose();
            }
            main = null;
            if (quick != null && !quick.IsDisposed) { quick.Close(); quick.Dispose(); }
            quick = null;
            foreach (var f in cardForms.Values.ToList()) f.Close();
            foreach (var d in Store.Cards.Where(c => c.Visible).ToList()) OpenCard(d, false);
            if (wasVisible) ShowMain(page);
        }

        // ---------- Start with Windows ----------

        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunName = "AllInOne";
        const string OldRunName = "StickyNotesPro"; // name before the rename to "All in One"

        public static bool IsStartup()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null; }
            catch { return false; }
        }

        // After the rename: move an old "start with Windows" entry to the new exe, and keep the path current.
        public static void MigrateStartup()
        {
            try
            {
                if (Install.InTemp) return;
                bool on;
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) on = k != null && (k.GetValue(OldRunName) != null || k.GetValue(RunName) != null);
                if (!on) return;
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    k.DeleteValue(OldRunName, false);
                    string want = "\"" + Application.ExecutablePath + "\" /startup";
                    if (!want.Equals(k.GetValue(RunName) as string, StringComparison.OrdinalIgnoreCase)) k.SetValue(RunName, want);
                }
            }
            catch (Exception ex) { Log("Startup migration failed: " + ex.Message); }
        }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\" /startup");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex) { Log("Startup setting failed: " + ex.Message); }
        }

        // ---------- Clipboard capture ----------

        public void OnClipboardUpdate()
        {
            if (Store.Settings.Paused || DateTime.Now < suppressUntil) return;
            pendingSource = ForegroundProcess();
            clipAttempts = 0;
            clipTimer.Stop();
            clipTimer.Start();
        }

        string ForegroundProcess()
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                if (pid == 0) return "";
                string name;
                if (procNames.TryGetValue(pid, out name)) return name;
                using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName;
                if (procNames.Count > 200) procNames.Clear();
                procNames[pid] = name;
                return name;
            }
            catch { return ""; }
        }

        void CaptureClipboard()
        {
            if (Store.Settings.Paused || DateTime.Now < suppressUntil) return;
            IDataObject d = null;
            try { d = Clipboard.GetDataObject(); }
            catch { if (++clipAttempts < 8) clipTimer.Start(); return; }
            if (d == null) return;
            try
            {
                // Respect apps (password managers etc.) that ask not to be recorded.
                if (d.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || d.GetDataPresent("Clipboard Viewer Ignore")) return;
                if (d.GetDataPresent("CanIncludeInClipboardHistory"))
                {
                    var ms = d.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                    if (ms != null && ms.Length >= 4)
                    {
                        var b = new byte[4];
                        ms.Read(b, 0, 4);
                        if (BitConverter.ToInt32(b, 0) == 0) return;
                    }
                }

                Clip c = null;
                if (d.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = d.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0) { c = new Clip(); c.Kind = "files"; c.Text = string.Join("\r\n", files); }
                }
                if (c == null && (d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent(DataFormats.Text)))
                {
                    var t = (d.GetData(DataFormats.UnicodeText) ?? d.GetData(DataFormats.Text)) as string;
                    if (!string.IsNullOrWhiteSpace(t))
                    {
                        if (t.Length > 2000000) t = t.Substring(0, 2000000);
                        c = new Clip(); c.Kind = "text"; c.Text = t;
                    }
                }
                if (c == null && d.GetDataPresent(DataFormats.Bitmap))
                {
                    var img = d.GetData(DataFormats.Bitmap) as Image;
                    if (img != null)
                    {
                        Bitmap copy;
                        using (img) copy = new Bitmap(img);          // fast copy, releases the clipboard object now
                        string source = pendingSource;
                        System.Threading.ThreadPool.QueueUserWorkItem(delegate
                        {
                            try
                            {
                                string hash, id = Store.NewId(), name = id + ".png";
                                byte[] bytes; int w, h;
                                using (copy) { bytes = Store.EncodePng(copy, out hash); w = copy.Width; h = copy.Height; }
                                File.WriteAllBytes(Path.Combine(Store.ImgDir, name), bytes);
                                Sync.BeginInvoke(new Action(() =>
                                {
                                    try { var clip = Store.ImageClipFrom(bytes, hash, w, h, name); clip.Id = id; AddCaptured(clip, source); }
                                    catch (Exception ex) { Log("Capture failed: " + ex.Message); }
                                }));
                            }
                            catch (Exception ex) { Log("Image capture failed: " + ex.Message); }
                        });
                        return;
                    }
                }
                if (c == null) return;
                AddCaptured(c, pendingSource);
            }
            catch (Exception ex) { Log("Capture failed: " + ex.Message); }
        }

        void AddCaptured(Clip c, string source)
        {
            if (c.Id == null) c.Id = Store.NewId();
            c.Time = Store.Now();
            c.Source = source;
            Store.AddClip(c);
            if (queueOn)
            {
                var stored = Store.Clips.Count > 0 ? Store.Clips[0] : c;
                queue.Add(stored);
                UpdateTrayText();
            }
        }

        // ---------- Putting things back on the clipboard ----------

        public void Suppress() { suppressUntil = DateTime.Now.AddMilliseconds(800); }

        public bool CopyText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                Suppress();
                var d = new DataObject();
                d.SetData(DataFormats.UnicodeText, text);
                Clipboard.SetDataObject(d, true, 10, 60);
                return true;
            }
            catch (Exception ex) { Log("Copy failed: " + ex.Message); return false; }
        }

        public bool CopyClip(Clip c)
        {
            try
            {
                bool ok = false;
                if (c.Kind == "image")
                {
                    using (var img = Store.LoadImage(c))
                    if (img != null)
                    {
                        Suppress();
                        var d = new DataObject();
                        d.SetData(DataFormats.Bitmap, true, img);
                        Clipboard.SetDataObject(d, true, 10, 60);
                        ok = true;
                    }
                }
                else if (c.Kind == "files")
                {
                    var sc = new StringCollection();
                    sc.AddRange(c.Text.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries));
                    Suppress();
                    var d = new DataObject();
                    d.SetFileDropList(sc);
                    d.SetData(DataFormats.UnicodeText, c.Text);
                    Clipboard.SetDataObject(d, true, 10, 60);
                    ok = true;
                }
                else ok = CopyText(c.Text);
                if (ok) Store.TouchClip(c);
                return ok;
            }
            catch (Exception ex) { Log("Copy failed: " + ex.Message); return false; }
        }

        public void PasteTo(IntPtr hwnd, Action<bool> completed = null)
        {
            uint pid;
            if (hwnd == IntPtr.Zero || Native.GetWindowThreadProcessId(hwnd, out pid) == 0)
            {
                if (completed != null) completed(false);
                return;
            }
            uint clipboardVersion = Native.GetClipboardSequenceNumber();
            Native.SetForegroundWindow(hwnd);
            Ui.Later(90, () =>
            {
                AfterModifiers(() =>
                {
                    uint currentPid;
                    bool sent = Native.GetWindowThreadProcessId(hwnd, out currentPid) != 0 && currentPid == pid
                        && Native.GetForegroundWindow() == hwnd && Native.GetClipboardSequenceNumber() == clipboardVersion;
                    if (sent) try { SendKeys.SendWait("^v"); } catch { sent = false; }
                    if (completed != null) completed(sent);
                }, 40, () => { if (completed != null) completed(false); });
            });
        }

        // Pastes text into the active app, then puts the previous clipboard text back.
        public void PasteKeepingClipboard(string text)
        {
            IntPtr target = Native.GetForegroundWindow();
            string old = null;
            try { if (Clipboard.ContainsText()) old = Clipboard.GetText(); } catch { }
            if (!CopyText(text)) return;
            uint ownSequence = Native.GetClipboardSequenceNumber();
            Ui.Later(40, () =>
            {
                AfterModifiers(() =>
                {
                    if (Native.GetClipboardSequenceNumber() != ownSequence || !Native.IsWindow(target) || Native.GetForegroundWindow() != target) return;
                    try { SendKeys.SendWait("^v"); } catch { }
                    Ui.Later(500, () => { if (old != null && Native.GetClipboardSequenceNumber() == ownSequence) CopyText(old); });
                }, 40);
            });
        }

        static void AfterModifiers(Action action, int tries, Action expired = null)
        {
            if (!Native.Down(0x11) && !Native.Down(0x12) && !Native.Down(0x10) && !Native.Down(0x5B) && !Native.Down(0x5C)) { action(); return; }
            if (tries > 0) Ui.Later(25, () => AfterModifiers(action, tries - 1, expired));
            else if (expired != null) expired();
        }

        void PastePlain()
        {
            IntPtr target = Native.GetForegroundWindow();
            string t = null;
            try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); } catch { }
            if (string.IsNullOrEmpty(t)) { Balloon("Nothing to paste", "The clipboard has no text."); return; }
            if (CopyText(t)) PasteTo(target);
        }

        // ---------- Paste queue ----------

        void ToggleQueue()
        {
            if (!queueOn)
            {
                queueOn = true;
                queue.Clear();
                Balloon("Paste queue ON", "Copy your items now (Ctrl+C). Then press " + Hotkeys.Get("next") + " to paste them one by one, in order.");
            }
            else
            {
                queueOn = false;
                Balloon("Paste queue: collecting stopped", queue.Count + " item(s) waiting. Press " + Hotkeys.Get("next") + " to paste the next one.");
            }
            UpdateTrayText();
        }

        public void AddToQueue(IEnumerable<Clip> clips)
        {
            int n = 0;
            foreach (var c in clips) { queue.Add(c); n++; }
            Balloon("Added to paste queue", n + " item(s) added, " + queue.Count + " waiting. Press " + Hotkeys.Get("next") + " to paste the next one.");
            UpdateTrayText();
        }

        void PasteNext()
        {
            if (queue.Count == 0) { Balloon("Paste queue is empty", "Press " + Hotkeys.Get("queue") + " and copy some items first."); return; }
            IntPtr target = Native.GetForegroundWindow();
            var c = queue[0];
            if (!CopyClip(c)) { Balloon("Could not copy queue item", "The item is still in the queue. Try again."); return; }
            queue.RemoveAt(0);
            PasteTo(target);
            if (queue.Count == 0)
            {
                queueOn = false;
                Ui.Later(600, () => Balloon("Paste queue finished", "All items were pasted."));
            }
            UpdateTrayText();
        }

        // ---------- Selection to prompt ----------

        void AddSelectionAsPrompt()
        {
            IntPtr target = Native.GetForegroundWindow();
            AfterModifiers(() =>
            {
                if (Native.GetForegroundWindow() != target) return;
                uint before = Native.GetClipboardSequenceNumber();
                try { SendKeys.SendWait("^c"); } catch { }
                PollSelection(target, before, 20);
            }, 40);
        }

        void PollSelection(IntPtr target, uint before, int tries)
        {
            Ui.Later(25, () =>
            {
                if (Native.GetForegroundWindow() != target) return;
                uint sequence = Native.GetClipboardSequenceNumber();
                if (sequence == before && tries > 0) { PollSelection(target, before, tries - 1); return; }
                if (sequence == before) { Balloon("Nothing selected", "Select some text first, then press " + Hotkeys.Get("addprompt") + "."); return; }
                Ui.Later(60, () =>
                {
                    if (Native.GetClipboardSequenceNumber() != sequence) return;
                    string text = null;
                    try { text = Clipboard.GetText(); } catch { }
                    if (string.IsNullOrWhiteSpace(text)) { Balloon("Nothing selected", "The selection has no text."); return; }
                    Store.NewPrompt(Store.TitleFrom(text), text.Trim(), "Inbox");
                    Balloon("Saved to Prompts", "Category: Inbox - " + Store.TitleFrom(text));
                });
            });
        }

        // ---------- Reminders ----------

        void CheckReminders()
        {
            long now = DateTime.Now.Ticks;
            foreach (var n in Store.Notes.Where(n => n.RemindAt > 0 && n.RemindAt <= now).ToList())
            {
                n.RemindAt = 0;
                n.Archived = false;
                Store.MarkNotes();
                var f = OpenNote(n);
                f.Alert();
                try { SystemSounds.Exclamation.Play(); } catch { }
                Balloon("Reminder", Store.TitleFrom(n.Text).Length > 0 ? Store.TitleFrom(n.Text) : "Sticky note reminder");
                Store.RaiseNotes();
            }
            int shown = 0;
            foreach (var t in Store.Tasks.Where(t => !t.Done && t.RemindAt > 0 && t.RemindAt <= now).OrderBy(t => t.RemindAt).ToList())
            {
                var task = t;
                task.RemindAt = 0; Store.MarkTasks();
                if (shown >= 5 || ReminderToast.OpenCount >= 5) { Balloon("Task reminder", task.Title); continue; }
                shown++;
                var toast = new ReminderToast(task.Title, TaskParse.DueText(task), task.Priority >= 2,
                    () => Store.CompleteTask(task, true),
                    when => { task.RemindAt = when.Ticks; task.Snoozes++; Store.TouchTask(task); },
                    () => { ShowMain("tasks"); if (main != null && !main.IsDisposed) main.SelectTask(task); },
                    null);
                toast.Pop();
                try { SystemSounds.Asterisk.Play(); } catch { }
            }
            if (shown > 0) Store.RaiseTasks();
        }

        void OnPowerMode(object s, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume) Sync.BeginInvoke(new Action(() => Ui.Later(3000, CheckReminders)));
        }

        // ---------- Windows ----------

        // Starts a fresh copy (it waits for this one to exit) and closes this one. Used by data restore.
        public void RestartNow()
        {
            Ui.Later(300, () =>
            {
                try
                {
                    if (!ConfirmDocumentClose()) return;
                    FlushAll();
                    if (!AgentStore.FlushWrites() || Store.Pending) { Balloon("Could not restart", "Pending changes could not be saved. Check free space and folder permissions."); return; }
                    string args = "--elevated" + (string.IsNullOrEmpty(Store.DirOverride) ? "" : " --data " + AgentRun.Quote(Store.DirOverride));
                    Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { UseShellExecute = false });
                }
                catch (Exception ex) { Log("Restart failed: " + ex.Message); Balloon("Restart failed", "Close and start the app again to finish the restore."); return; }
                Exit(true);
            });
        }

        public void NewPrompt()
        {
            ShowMain("prompts");
            if (main != null && !main.IsDisposed) main.StartNewPrompt();
        }

        public void ShowMain(string page)
        {
            if (main == null || main.IsDisposed) main = new MainForm();
            if (page != null) main.ShowPage(page);
            if (!main.Visible) main.Show();
            if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
            Native.EnsureRestored(main);
            main.Activate();
            main.BringToFront();
            main.FocusSearch();
        }

        public void AddTerminalFolder(string folder)
        {
            ShowMain("terminals");
            main.AddTerminalFolder(folder);
        }
        public void Status(string text) { if (main != null && !main.IsDisposed) main.Status(text); }
        public void AddAgentFolder(string folder) { ShowMain("agents"); main.AddAgentFolder(folder); }
        public void OpenInExplorerPage(string folder) { ShowMain("explorer"); main.OpenInExplorer(folder); }
        public void OpenMarkdown(string path) { ShowMain("markdown"); main.OpenMarkdownFile(path); }
        // A web address or a local file (PDF, email, HTML, picture) in a new Browser tab.
        public void OpenInBrowser(string target) { ShowMain("browser"); main.OpenInBrowser(target); }
        // Live Canvas / Dashboard local HTML file: reuses existing tab if already open and reloads cleanly.
        public void OpenOrReloadInBrowser(string path) { ShowMain("browser"); main.OpenOrReloadInBrowser(path); }
        // Text into the message box of the active Agents chat.
        public void SendToAgents(string text) { ShowMain("agents"); main.SendToAgents(text); }
        public DocumentsPage DocumentsView { get { return main != null && !main.IsDisposed ? main.DocumentsView : null; } }
        public ExplorerPage ExplorerView { get { return main != null && !main.IsDisposed ? main.ExplorerView : null; } }
        public BrowserPage BrowserView { get { return main != null && !main.IsDisposed ? main.BrowserView : null; } }
        public MainForm MainView { get { return main != null && !main.IsDisposed ? main : null; } }
        public void OpenDocument(string path) { OpenDocument(path, null); }
        public void OpenDocument(string path, bool? edit) { ShowMain("documents"); main.OpenDocument(path, edit); }
        public void ToggleZen() { if (main != null && !main.IsDisposed) main.SetZen(!main.Zen); }
        public void ToggleAssistant() { ShowMain(main != null && !main.IsDisposed ? main.Page : null); main.SetAssistant(!Store.Settings.AssistantOpen); }
        public Dictionary<string, object> PageContext() { return main != null && !main.IsDisposed ? main.PageContext() : new Dictionary<string, object> { { "page", "" } }; }
        public System.Threading.Tasks.Task<string> BrowserScript(string js) { ShowMain("browser"); return main.BrowserScript(js); }
        public System.Threading.Tasks.Task<string> BrowserCdp(string method, string paramsJson, int tabIndex) { ShowMain("browser"); return main.BrowserCdp(method, paramsJson, tabIndex); }
        public void OpenAgentChat(AgentChat c) { ShowMain("agents"); main.OpenAgentChat(c); }

        void ShowQuick(string mode)
        {
            IntPtr target = Native.GetForegroundWindow();
            if (quick == null || quick.IsDisposed) quick = new QuickPopup();
            quick.Open(mode, target);
        }

        public NoteForm OpenNote(Note n)
        {
            NoteForm f;
            if (noteForms.TryGetValue(n.Id, out f) && !f.IsDisposed)
            {
                if (!f.Visible) f.Show();
                Native.EnsureRestored(f);
                f.Activate();
                return f;
            }
            if (n.Archived) { n.Archived = false; Store.RaiseNotes(); }
            if (!n.Open) { n.Open = true; Store.MarkNotes(); }
            f = new NoteForm(n);
            noteForms[n.Id] = f;
            f.FormClosed += delegate { noteForms.Remove(n.Id); };
            f.Show();
            Native.EnsureRestored(f);
            notesHidden = false;
            return f;
        }

        // Replaces a note's text; an open note window shows it at once.
        public void SetNoteText(Note n, string text)
        {
            NoteForm f;
            if (noteForms.TryGetValue(n.Id, out f) && !f.IsDisposed) { f.SetPlainText(text); return; }
            n.Text = text ?? ""; n.Rtf = ""; n.Rtl = Ui.IsRtl(n.Text);
            n.Modified = Store.Now(); Store.MarkNotes(); Store.RaiseNotes();
        }

        public void CloseNoteWindow(Note n)
        {
            NoteForm f;
            if (noteForms.TryGetValue(n.Id, out f) && !f.IsDisposed) f.Close();
        }

        public void NewNote(Note near, string text = null)
        {
            var n = new Note();
            n.Id = Store.NewId();
            n.Created = n.Modified = Store.Now();
            n.Color = near != null ? near.Color : "Yellow";
            n.W = Ui.S(320); n.H = Ui.S(320);
            if (near != null) { n.X = near.X + near.W + Ui.S(12); n.Y = near.Y; n.Tags = near.Tags; }
            else { var c = Cursor.Position; n.X = c.X - Ui.S(40); n.Y = c.Y - Ui.S(20); }
            if (!string.IsNullOrEmpty(text)) { n.Text = text; n.Rtl = Ui.IsRtl(text); }
            n.Open = true;
            Store.Notes.Insert(0, n);
            Store.MarkNotes();
            Store.RaiseNotes();
            var f = OpenNote(n);
            if (!string.IsNullOrEmpty(text)) f.SetPlainText(text);
            f.FocusText();
        }

        public void DeleteNote(Note n)
        {
            NoteForm f;
            if (noteForms.TryGetValue(n.Id, out f) && !f.IsDisposed) { f.Deleting = true; f.Close(); }
            Store.Notes.Remove(n);
            Store.MarkNotes();
            Store.RaiseNotes();
        }

        public void ArchiveNote(Note n, bool archive)
        {
            if (archive)
            {
                NoteForm f;
                if (noteForms.TryGetValue(n.Id, out f) && !f.IsDisposed) { f.Flush(); f.Deleting = true; f.Close(); }
                n.Open = false;
            }
            n.Archived = archive;
            Store.MarkNotes();
            Store.RaiseNotes();
        }

        public void RefreshNoteColor(Note n)
        {
            NoteForm f;
            if (noteForms.TryGetValue(n.Id, out f) && !f.IsDisposed) f.ApplyColor();
        }

        void ToggleNotes()
        {
            var forms = noteForms.Values.Where(f => !f.IsDisposed).ToList();
            if (forms.Count > 0 && !notesHidden && forms.Any(f => f.Visible))
            {
                foreach (var f in forms) f.Hide();
                notesHidden = true;
                return;
            }
            notesHidden = false;
            if (forms.Count == 0)
            {
                var recent = Store.Notes.Where(n => !n.Archived).OrderByDescending(n => n.Modified).Take(10).ToList();
                if (recent.Count == 0) { NewNote(null); return; }
                foreach (var n in recent) OpenNote(n);
                return;
            }
            foreach (var f in forms) { f.Show(); f.Activate(); }
        }

        public void FlushAll()
        {
            foreach (var f in noteForms.Values.ToList()) if (!f.IsDisposed) f.Flush();
            if (main != null && !main.IsDisposed) main.CommitEdits();
            Store.SaveNow();
        }

        bool exitWaiting;
        bool exiting;
        void RecordingFinished() { Recorder.Finished -= RecordingFinished; exitWaiting = false; Exit(); }

        // After installing: close this copy and start the installed one (it waits for this process to let go).
        public void ExitForInstall(string installedExe)
        {
            Exit();
            if (!exiting) return;
            try { Process.Start(new ProcessStartInfo(installedExe, "--after-install") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installedExe) }); } catch (Exception ex) { Log("Start installed copy: " + ex.Message); }
        }

        bool confirmingDocumentClose;
        bool ConfirmDocumentClose()
        {
            if (confirmingDocumentClose) return false;
            if (main == null || main.IsDisposed) return true;
            confirmingDocumentClose = true;
            try { return main.ConfirmDocumentClose(); }
            finally { confirmingDocumentClose = false; }
        }

        public void Exit() { Exit(false); }
        void Exit(bool documentsConfirmed)
        {
            if (exiting) return;
            if (Recorder.Active || Recorder.Saving)
            {
                if (!exitWaiting) { exitWaiting = true; Recorder.Finished += RecordingFinished; if (Recorder.Active) Recorder.Stop(true); }
                return;
            }
            if (!documentsConfirmed && !ConfirmDocumentClose()) return;
            FlushAll();
            if (!AgentStore.FlushWrites() || Store.Pending) { Balloon("Could not save changes", "The app will stay open and retry. Check free space and folder permissions."); return; }
            exiting = true;
            clipTimer.Dispose(); reminderTimer.Dispose();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerMode;
            Snippets.Stop();
            ActivityMonitor.Stop();
            try { WorkflowRecorder.Stop(); } catch { }
            try { CronEngine.Stop(); } catch { }
            try { SqliteVault.Close(); } catch { }
            if (Recorder.Active) Recorder.Stop(true);
            try { Native.RemoveClipboardFormatListener(msg.Handle); } catch { }
            foreach (int id in registered) Native.UnregisterHotKey(msg.Handle, id);
            try { Overlay.Shutdown(); } catch { }
            try { PopupHider.Stop(); } catch { }
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Dispose();
            foreach (var f in noteForms.Values.ToList()) { f.Exiting = true; f.Close(); }
            foreach (var f in cardForms.Values.ToList()) f.Close();
            CardForm.StopPasteTracking();
            Store.SaveNow();
            if (main != null && !main.IsDisposed) { main.Exiting = true; main.Close(); }
            if (quick != null && !quick.IsDisposed) quick.Dispose();
            Store.StopSaver();
            Store.PromptsChanged -= Snippets.Update;
            Thumbs.Clear();
            msg.Close();
            Sync.Dispose();
            AppIcon.Dispose();
            ExitThread();
        }

        public static void Log(string text)
        {
            try
            {
                string dir = Store.Dir ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "error.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch { }
        }
    }

    // Hidden message-only window: receives global hotkeys and clipboard updates.
    class MsgWindow : NativeWindow
    {
        readonly App app;
        public MsgWindow(App app)
        {
            this.app = app;
            var cp = new CreateParams();
            cp.Parent = (IntPtr)(-3); // HWND_MESSAGE
            CreateHandle(cp);
        }

        public void Close() { DestroyHandle(); }

        protected override void WndProc(ref Message m)
        {
            try
            {
                if (m.Msg == Native.WM_HOTKEY) app.OnHotkey(m.WParam.ToInt32());
                else if (m.Msg == Native.WM_CLIPBOARDUPDATE) app.OnClipboardUpdate();
            }
            catch (Exception ex) { App.Log("Message error: " + ex); }
            base.WndProc(ref m);
        }
    }
}
