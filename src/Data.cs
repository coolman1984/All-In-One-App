using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace StickyPro
{
    public class Note
    {
        public string Id;
        public string Rtf = "";
        public string Text = "";
        public string Color = "Yellow";
        public int X = -1, Y = -1, W = 0, H = 0;
        public bool Open;
        public bool Top;
        public float Zoom = 1f;
        public long Created, Modified;
        public string Tags = "";
        public bool Archived;
        public long RemindAt;
        public bool Rtl;
    }

    public class PromptVersion
    {
        public string Title = "";
        public string Body = "";
        public long Time;
    }

    public class Prompt
    {
        public string Id;
        public string Title = "";
        public string Body = "";
        public string Category = "";
        public string Tags = "";
        public bool Fav;
        public int Uses;
        public long Created, Modified, LastUsed;
        public string Trigger = "";
        public List<PromptVersion> Versions = new List<PromptVersion>();
    }

    public class Clip
    {
        public string Id;
        public string Kind = "text"; // text | image | files
        public string Text = "";
        public string Image;         // file name inside data\images
        public string Hash;          // image hash for de-duplication
        public int ImgW, ImgH;
        public string Source = "";
        public long Time;
        public bool Pinned;
        public int Uses;
        public string Folder = "";
        public string Label = "";   // color label name, "" = none
    }

    public class ActivityRecord
    {
        public string Id;
        public string Process = "";   // Excel, Word, PowerPoint, Image Viewer, Text Editor, PDF Reader, Other
        public string Path = "";
        public string Event = "";     // Opened / Closed
        public long Time;
        public long Size = -1;
        public bool Fav;
        public string Backup;         // backup file path when saved
        public long BackupTime;
    }

    public class TaggedFile
    {
        public string Id;
        public string Path = "";
        public bool IsFolder;
        public string Category = "Urgent";
        public long Added;
        public string Note = "";
    }

    public class TagCategory
    {
        public string Name = "";
        public string Color = "Red";
    }

    public class CardDef
    {
        public string Id;
        public string Title = "Card";
        public List<string> Features = new List<string>();
        public string Active;
        public int X = -1, Y = -1, W = 0, H = 0;
        public bool Top = true;
        public bool Collapsed;
        public bool Visible = true;
        public int Opacity = 100;
    }

    public class SubTask { public string Text = ""; public bool Done; }
    public class TaskItem
    {
        public string Id;
        public string Title = "";
        public string Notes = "";
        public string List = "Inbox";
        public int Priority = 1;             // 0 low, 1 normal, 2 high
        public long Due;                     // ticks, 0 = no due date
        public bool DueHasTime;
        public long RemindAt;                // ticks, 0 = none
        public string Repeat = "none";       // none | daily | weekdays | weekly | monthly | yearly
        public bool Done;
        public long DoneAt, Created, Modified;
        public List<SubTask> Steps = new List<SubTask>();
        public int Snoozes;
    }

    // Quick-launch dock item and user launch shortcut (global hotkey that opens anything).
    public class DockItem { public string Name = ""; public string Path = ""; public string Args = ""; }
    public class UserShortcut { public string Id; public string Keys = ""; public string Name = ""; public string Target = ""; public string Args = ""; }

    public class Settings
    {
        public bool KeepAwake;
        public int Theme;                    // 0..5, see Ui.Themes
        public string UiFont = "";           // "" = Segoe UI (Variable)
        public float UiFontSize = 10f;
        public bool DockVisible = true;
        public List<DockItem> Dock = new List<DockItem>();
        public List<UserShortcut> Launchers = new List<UserShortcut>();
        public List<string> ExplorerPaths = new List<string>();
        public string ExplorerLayout = "2";  // 1 | 2 | 3 | 4 | 2x2
        public bool DockSeeded;
        public bool Paused;
        public int MaxClips = 1000;
        public bool FirstRunDone;
        public bool Dark;
        public bool Snippets = true;
        public string NoteFontName = "Segoe UI";
        public float NoteFontSize = 11f;
        public Dictionary<string, string> Hotkeys = new Dictionary<string, string>();
        public string SnipFolder = "";
        public bool SnipAutoSave = true;
        public bool SnipAutoCopy = true;
        public string SnipCopyMode = "image";
        public int FixedW = 800, FixedH = 600;
        public bool FixedReady;
        public string LastCaptureMode = "rect";
        public int LastX, LastY, LastW, LastH;
        public List<int> LastPolygon = new List<int>();
        public string OcrLang = "";
        public bool RecordMic;
        public int RecordFps = 30;
        public bool ActivityOn = true;
        public bool ActivityAutoBackupFav;
        public List<string> ColorHistory = new List<string>();
        public List<string> Swatches = new List<string>();
        public bool WelcomeHidden;
        public Dictionary<string, int> PaneSizes = new Dictionary<string, int>();
        public string Design = "";           // "" (not chosen yet -> serq) | serq | nexus | classic
        public int DesignRev;                // 2 = the SerQ default was applied once
        public List<TermWorkspace> TermWorkspaces = new List<TermWorkspace>();
        public int TermLast;
        public List<string> TermRecent = new List<string>();
        public string TermShell = "pwsh";    // shell for "PWSH here": pwsh | powershell | cmd

        // Appearance (Settings > Appearance)
        public string HeadingFont = "";      // "" = same as UiFont
        public float HeadingScale = 1f;      // 0.85 .. 1.4, size of page titles and card titles
        public string MonoFont = "";         // "" = Cascadia Mono / Consolas (paths, commands, code)
        public float MonoFontSize = 10f;
        public string Density = "normal";    // compact | normal | roomy (list row height, paddings)
        public string AccentColor = "";      // "#RRGGBB" overrides the theme accent; "" = theme colour
        public int CornerRadius = -1;        // -1 = design default, else 0..24 px
        public bool Shadows = true;          // card shadows
        public bool Animations = true;       // hover fades, overlay dock magnification
        public string UserName = "";         // shown where the app greets or signs (prompts {{name}}); "" = Windows user
        public string AppTitle = "";         // window / sidebar title; "" = All in One
        // Sidebar management
        public Dictionary<string, string> PageNames = new Dictionary<string, string>();   // page key -> your name for it
        public List<string> NavOrder = new List<string>();     // page keys in your order (missing ones keep the default place)
        public List<string> NavHidden = new List<string>();    // page keys hidden from the sidebar
        public List<string> NavFavorites = new List<string>(); // page keys pinned in a Favorites group at the top
        // Desktop overlay (dock, menu bar, hot corners, window overview)
        public bool OverlayOn;
        public bool OverlayDock = true, OverlayMenuBar = true, OverlayHotCorner = true, OverlayMagnify = true, OverlayAutoHide;
        public int OverlayDockSize = 56;     // icon size in px at 100 % scale
        public string OverlayDockEdge = "bottom";   // bottom | left | right
        public string OverlayCorner = "topleft";    // hot corner that opens the window overview
        public List<DockItem> OverlayApps = new List<DockItem>();   // pinned dock apps
    }

    // Generic management record for an item in any tab (folder, workspace, bookmark, program, process, ...):
    // favourite, manual order, colour label, tags, note and usage. Scope = tab ("explorer", "terminals", ...),
    // Id = a stable id inside that tab (usually a path or URL). Saved in data\meta.json.
    public class ItemMeta
    {
        public string Scope = "", Id = "";
        public bool Fav;
        public int Order;                    // manual order inside the scope (0 = none)
        public string Color = "";            // colour label name or #RRGGBB, "" = none
        public string Tags = "";             // comma separated
        public string Note = "";
        public string Name = "";             // your display name for the item ("" = default)
        public int Uses;
        public long Used, Created;           // ticks
    }

    // Terminals page: one pane = folder + shell + optional start command (e.g. claude).
    public class TermPane { public string Folder = ""; public string Shell = "pwsh"; public string Command = ""; public string Name = ""; public string Color = ""; }
    public class TermWorkspace { public string Name = "My terminals"; public string Layout = "4"; public string Mode = "tiled"; public List<TermPane> Panes = new List<TermPane>(); }

    static class Store
    {
        public static string Dir, ImgDir;
        public static List<Note> Notes = new List<Note>();
        public static List<Prompt> Prompts = new List<Prompt>();
        public static List<Clip> Clips = new List<Clip>();
        public static Settings Settings = new Settings();
        public static List<CardDef> Cards = new List<CardDef>();
        public static SysHistory History = new SysHistory();
        public static List<ActivityRecord> Activity = new List<ActivityRecord>();
        public static List<TaggedFile> Tagged = new List<TaggedFile>();
        public static List<TagCategory> TagCats = new List<TagCategory>();
        public static List<TaskItem> Tasks = new List<TaskItem>();
        public static List<ItemMeta> Meta = new List<ItemMeta>();
        public static event Action MetaChanged;
        static bool metaDirty;
        public static void MarkMeta() { metaDirty = true; Kick(); }
        public static void RaiseMeta() { var h = MetaChanged; if (h != null) h(); }
        public static event Action TasksChanged;
        static bool tasksDirty;
        public static void MarkTasks() { tasksDirty = true; Kick(); }
        public static void RaiseTasks() { var h = TasksChanged; if (h != null) h(); }

        public static event Action NotesChanged, PromptsChanged, ClipsChanged, CardsChanged, ActivityChanged, TaggedChanged;

        static bool notesDirty, promptsDirty, clipsDirty, settingsDirty, cardsDirty, historyDirty, activityDirty, taggedDirty;
        static bool activityWait;
        static int activityDue;
        static bool ActivityWaiting() { return activityWait && unchecked(Environment.TickCount - activityDue) < 0; }
        public static void MarkActivity()
        {
            activityDirty = true;
            if (!activityWait) { activityWait = true; activityDue = unchecked(Environment.TickCount + 5000); }
            Kick();
        }
        public static void MarkTagged() { taggedDirty = true; Kick(); }
        public static void RaiseActivity() { var h = ActivityChanged; if (h != null) h(); }
        public static void RaiseTagged() { var h = TaggedChanged; if (h != null) h(); }
        public static void MarkCards() { cardsDirty = true; Kick(); }
        public static void MarkHistory() { historyDirty = true; Kick(); }
        public static void RaiseCards() { var h = CardsChanged; if (h != null) h(); }
        static System.Windows.Forms.Timer saveTimer;
        static readonly HashSet<string> recovered = new HashSet<string>();
        public static bool Pending { get { return notesDirty || promptsDirty || clipsDirty || settingsDirty || cardsDirty || historyDirty || activityDirty || taggedDirty || tasksDirty || metaDirty; } }
        public static void StopSaver() { if (saveTimer != null) { saveTimer.Dispose(); saveTimer = null; } }

        public static string NewId() { return Guid.NewGuid().ToString("N"); }
        public static long Now() { return DateTime.Now.Ticks; }

        static JavaScriptSerializer Js()
        {
            var j = new JavaScriptSerializer();
            j.MaxJsonLength = int.MaxValue;
            j.RecursionLimit = 64;
            return j;
        }

        public static void Load()
        {
            Dir = DataFolder();
            ImgDir = Path.Combine(Dir, "images");
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory(ImgDir);
            Backup.ApplyPending(Dir);
            Notes = Read<List<Note>>("notes.json") ?? new List<Note>();
            Prompts = Read<List<Prompt>>("prompts.json") ?? new List<Prompt>();
            Clips = Read<List<Clip>>("clips.json") ?? new List<Clip>();
            Settings = Read<Settings>("settings.json") ?? new Settings();
            Cards = Read<List<CardDef>>("cards.json") ?? new List<CardDef>();
            History = Read<SysHistory>("history.json") ?? new SysHistory();
            Activity = Read<List<ActivityRecord>>("activity.json") ?? new List<ActivityRecord>();
            Tagged = Read<List<TaggedFile>>("tagged.json") ?? new List<TaggedFile>();
            TagCats = Read<List<TagCategory>>("tagcats.json") ?? new List<TagCategory>();
            Activity.RemoveAll(a => a == null || a.Path == null);
            Tagged.RemoveAll(t => t == null || string.IsNullOrEmpty(t.Path));
            TagCats.RemoveAll(c => c == null || string.IsNullOrEmpty(c.Name));
            if (TagCats.Count == 0)
                TagCats.AddRange(new[] { new TagCategory { Name = "Urgent", Color = "Red" }, new TagCategory { Name = "Review", Color = "Yellow" }, new TagCategory { Name = "Important", Color = "Green" } });
            foreach (var a in Activity) { if (a.Id == null) a.Id = NewId(); if (a.Process == null) a.Process = ""; if (a.Event == null) a.Event = ""; }
            foreach (var a in Activity) { if (a.Time < 0 || a.Time > DateTime.MaxValue.Ticks) a.Time = 0; }
            foreach (var t in Tagged) { if (t.Id == null) t.Id = NewId(); if (t.Category == null) t.Category = TagCats[0].Name; if (t.Note == null) t.Note = ""; }
            Cards.RemoveAll(c => c == null);
            foreach (var c in Cards) { if (c.Id == null) c.Id = NewId(); if (c.Features == null) c.Features = new List<string>(); if (c.Title == null) c.Title = "Card"; if (c.Opacity < 30 || c.Opacity > 100) c.Opacity = 100; }
            if (History.CleanSizes == null) History.CleanSizes = new Dictionary<string, long>();
            if (History.FolderSizes == null) History.FolderSizes = new Dictionary<string, long>();
            if (History.DriveFreeDay == null) History.DriveFreeDay = new Dictionary<string, long>();
            foreach (var k in History.DriveFreeDay.Keys.Where(k => k.Length < 3).ToList()) History.DriveFreeDay.Remove(k);
            if (Settings.MaxClips < 50) Settings.MaxClips = 1000;
            Tasks = Read<List<TaskItem>>("tasks.json") ?? new List<TaskItem>();
            Tasks.RemoveAll(t => t == null);
            foreach (var t in Tasks)
            {
                if (t.Id == null) t.Id = NewId(); if (t.Title == null) t.Title = ""; if (t.Notes == null) t.Notes = ""; if (string.IsNullOrEmpty(t.List)) t.List = "Inbox";
                if (string.IsNullOrEmpty(t.Repeat)) t.Repeat = "none"; if (t.Steps == null) t.Steps = new List<SubTask>(); t.Steps.RemoveAll(x => x == null);
                foreach (var st in t.Steps) if (st.Text == null) st.Text = "";
                if (t.Due < 0 || t.Due > DateTime.MaxValue.Ticks) t.Due = 0; if (t.RemindAt < 0 || t.RemindAt > DateTime.MaxValue.Ticks) t.RemindAt = 0;
            }
            if (Settings.Dock == null) Settings.Dock = new List<DockItem>();
            Settings.Dock.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Path));
            if (Settings.Launchers == null) Settings.Launchers = new List<UserShortcut>();
            Settings.Launchers.RemoveAll(l => l == null);
            foreach (var l in Settings.Launchers) { if (l.Id == null) l.Id = NewId(); if (l.Keys == null) l.Keys = ""; if (l.Name == null) l.Name = ""; if (l.Target == null) l.Target = ""; if (l.Args == null) l.Args = ""; }
            if (Settings.ExplorerPaths == null) Settings.ExplorerPaths = new List<string>();
            if (string.IsNullOrEmpty(Settings.ExplorerLayout)) Settings.ExplorerLayout = "2";
            if (Settings.Theme < 0 || Settings.Theme > 5) Settings.Theme = 0;
            if (Settings.UiFont == null) Settings.UiFont = "";
            if (float.IsNaN(Settings.UiFontSize) || Settings.UiFontSize < 8 || Settings.UiFontSize > 16) Settings.UiFontSize = 10f;
            Notes.RemoveAll(n => n == null);
            Prompts.RemoveAll(p => p == null);
            Clips.RemoveAll(c => c == null || (c.Kind == "image" && (!ValidImageName(c.Image) || !File.Exists(ImagePath(c)))));
            foreach (var n in Notes) { if (n.Id == null) n.Id = NewId(); if (n.Rtf == null) n.Rtf = ""; if (n.Text == null) n.Text = ""; if (float.IsNaN(n.Zoom) || float.IsInfinity(n.Zoom) || n.Zoom < 0.5f || n.Zoom > 4f) n.Zoom = 1f; if (n.Tags == null) n.Tags = ""; if (n.RemindAt < 0 || n.RemindAt > DateTime.MaxValue.Ticks) n.RemindAt = 0; }
            foreach (var p in Prompts) { NormalizePrompt(p); }
            foreach (var c in Clips) { if (c.Id == null) c.Id = NewId(); if (c.Text == null) c.Text = ""; if (c.Source == null) c.Source = ""; if (c.Folder == null) c.Folder = ""; if (c.Label == null) c.Label = ""; }
            if (Settings.Hotkeys == null) Settings.Hotkeys = new Dictionary<string, string>();
            if (Settings.ColorHistory == null) Settings.ColorHistory = new List<string>();
            if (Settings.Swatches == null) Settings.Swatches = new List<string>();
            if (Settings.PaneSizes == null) Settings.PaneSizes = new Dictionary<string, int>();
            if (Settings.TermWorkspaces == null) Settings.TermWorkspaces = new List<TermWorkspace>();
            Settings.TermWorkspaces.RemoveAll(w => w == null);
            foreach (var w in Settings.TermWorkspaces) { if (w.Panes == null) w.Panes = new List<TermPane>(); w.Panes.RemoveAll(p => p == null); }
            if (Settings.TermRecent == null) Settings.TermRecent = new List<string>();
            if (string.IsNullOrEmpty(Settings.TermShell)) Settings.TermShell = "pwsh";
            foreach (var w in Settings.TermWorkspaces) foreach (var p in w.Panes) { if (p.Name == null) p.Name = ""; if (p.Color == null) p.Color = ""; }
            Meta = Read<List<ItemMeta>>("meta.json") ?? new List<ItemMeta>();
            Meta.RemoveAll(m => m == null || string.IsNullOrEmpty(m.Scope) || string.IsNullOrEmpty(m.Id));
            foreach (var m in Meta) { if (m.Color == null) m.Color = ""; if (m.Tags == null) m.Tags = ""; if (m.Note == null) m.Note = ""; if (m.Name == null) m.Name = ""; }
            var s = Settings;
            if (s.HeadingFont == null) s.HeadingFont = ""; if (s.MonoFont == null) s.MonoFont = ""; if (s.AccentColor == null) s.AccentColor = "";
            if (s.UserName == null) s.UserName = ""; if (s.AppTitle == null) s.AppTitle = "";
            if (s.Density != "compact" && s.Density != "roomy") s.Density = "normal";
            if (float.IsNaN(s.HeadingScale) || s.HeadingScale < 0.85f || s.HeadingScale > 1.4f) s.HeadingScale = 1f;
            if (float.IsNaN(s.MonoFontSize) || s.MonoFontSize < 7 || s.MonoFontSize > 20) s.MonoFontSize = 10f;
            if (s.CornerRadius > 24) s.CornerRadius = 24;
            if (s.PageNames == null) s.PageNames = new Dictionary<string, string>();
            if (s.NavOrder == null) s.NavOrder = new List<string>();
            if (s.NavHidden == null) s.NavHidden = new List<string>();
            if (s.NavFavorites == null) s.NavFavorites = new List<string>();
            if (s.OverlayApps == null) s.OverlayApps = new List<DockItem>();
            s.OverlayApps.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Path));
            if (s.OverlayDockSize < 32 || s.OverlayDockSize > 128) s.OverlayDockSize = 56;
            if (s.OverlayDockEdge != "left" && s.OverlayDockEdge != "right") s.OverlayDockEdge = "bottom";
            if (string.IsNullOrEmpty(s.OverlayCorner)) s.OverlayCorner = "topleft";
            // New design: switch once to the SerQ look (navy sidebar, blue accent); Nexus and Classic stay available in Settings.
            if (string.IsNullOrEmpty(Settings.Design) || (Settings.DesignRev < 2 && Settings.Design == "nexus")) { Settings.Design = "serq"; Settings.Theme = Ui.SerqTheme; }
            Settings.DesignRev = 2;
            if (string.IsNullOrEmpty(Settings.NoteFontName)) Settings.NoteFontName = "Segoe UI";
            if (float.IsNaN(Settings.NoteFontSize) || float.IsInfinity(Settings.NoteFontSize) || Settings.NoteFontSize < 6 || Settings.NoteFontSize > 48) Settings.NoteFontSize = 11f;

            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 700;
            saveTimer.Tick += delegate { saveTimer.Stop(); asyncSave = true; try { SaveNow(); } finally { asyncSave = false; } };
        }

        // Where all data lives. Default: "data" next to the exe. The user can choose any folder (Settings > Data);
        // the choice is kept in data-location.txt next to the exe, so the app finds it before loading anything.
        // Next to the exe when that folder can be written (portable); otherwise %LocalAppData%\AllInOne (see Install).
        static string dataRoot;
        static string DataRoot { get { if (dataRoot == null) dataRoot = Install.DataRoot(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')); return dataRoot; } }
        public static string LocationFile { get { return Path.Combine(DataRoot, "data-location.txt"); } }
        public static string DefaultDir { get { return Path.Combine(DataRoot, "data"); } }
        // "AllInOne.exe --data <folder>" uses that folder for this run only (portable use, tests).
        public static string DirOverride;
        public static string DataFolder()
        {
            if (!string.IsNullOrEmpty(DirOverride)) { Directory.CreateDirectory(DirOverride); return DirOverride; }
            try
            {
                if (File.Exists(LocationFile))
                {
                    string p = File.ReadAllText(LocationFile).Trim();
                    if (p.Length > 0 && Path.IsPathRooted(p)) { Directory.CreateDirectory(p); return p; }
                }
            }
            catch (Exception ex) { App.Log("Data folder: " + ex.Message + " - using the default."); }
            return DefaultDir;
        }
        // Copies every data file to newDir and points the app there (takes effect after a restart). Old files stay.
        public static string MoveDataTo(string newDir)
        {
            try
            {
                newDir = Path.GetFullPath(newDir);
                if (string.Equals(newDir.TrimEnd('\\'), Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return "That is already the data folder.";
                SaveNow(); FlushWrites();
                Directory.CreateDirectory(newDir);
                foreach (var f in Directory.GetFiles(Dir, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(Dir.TrimEnd('\\').Length + 1);
                    string to = Path.Combine(newDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    if (!File.Exists(to)) File.Copy(f, to);
                }
                if (string.Equals(newDir.TrimEnd('\\'), DefaultDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { if (File.Exists(LocationFile)) File.Delete(LocationFile); }
                else File.WriteAllText(LocationFile, newDir, new UTF8Encoding(false));
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static T Read<T>(string name) where T : class
        {
            string p = Path.Combine(Dir, name);
            foreach (string file in new[] { p, p + ".bak" })
            {
                try
                {
                    if (File.Exists(file))
                    {
                        var o = Js().Deserialize<T>(File.ReadAllText(file, Encoding.UTF8));
                        if (o != null) { if (file != p) recovered.Add(name); return o; }
                    }
                }
                catch (Exception ex)
                {
                    App.Log("Could not read " + file + ": " + ex.Message);
                    try { File.Copy(file, file + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
                }
            }
            return null;
        }

        static void WriteText(string name, string json)
        {
            string p = Path.Combine(Dir, name);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            lock (recovered)
            {
                // Do not replace a known-good recovery backup with the corrupt primary.
                if (File.Exists(p)) File.Replace(tmp, p, recovered.Contains(name) ? null : p + ".bak", true);
                else File.Move(tmp, p);
                recovered.Remove(name);
            }
        }

        // Background writer: the UI thread only takes a shallow snapshot; serialization and disk I/O
        // happen on the worker, so they never block a frame.
        // One writer, latest content per file wins, so writes cannot overtake each other.
        static readonly object ioLock = new object();
        static readonly Dictionary<string, object> pendingWrites = new Dictionary<string, object>();
        static bool writerBusy;
        static void QueueWrite(string name, object snapshot)
        {
            lock (ioLock)
            {
                pendingWrites[name] = snapshot;
                if (writerBusy) return;
                writerBusy = true;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Drain(); });
        }

        // Shallow copy of a settings-like object: its own fields plus a one-level copy of every list/dictionary field,
        // so the UI thread can keep editing while the worker serializes.
        static T Snap<T>(T o) where T : class
        {
            if (o == null) return o;
            try
            {
                var clone = (T)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(o, null);
                foreach (var f in typeof(T).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                {
                    object v = f.GetValue(clone);
                    if (v == null) continue;
                    Type t = v.GetType();
                    if (t.IsGenericType)
                    {
                        Type d = t.GetGenericTypeDefinition();
                        if (d == typeof(List<>) || d == typeof(Dictionary<,>)) f.SetValue(clone, Activator.CreateInstance(t, v));
                    }
                }
                return clone;
            }
            catch { return o; }
        }

        static void Drain()
        {
            while (true)
            {
                string name; object snapshot;
                lock (ioLock)
                {
                    if (pendingWrites.Count == 0) { writerBusy = false; System.Threading.Monitor.PulseAll(ioLock); return; }
                    var first = pendingWrites.First(); name = first.Key; snapshot = first.Value; pendingWrites.Remove(name);
                }
                try { WriteText(name, Js().Serialize(snapshot)); }
                catch (Exception ex)
                {
                    App.Log("Save failed " + name + ": " + ex.Message);
                    string n = name;
                    try { if (App.I != null) App.I.Sync.BeginInvoke(new Action(() => Remark(n))); } catch { }
                }
            }
        }
        static void Remark(string name)
        {
            switch (name)
            {
                case "notes.json": notesDirty = true; break;
                case "prompts.json": promptsDirty = true; break;
                case "clips.json": clipsDirty = true; break;
                case "settings.json": settingsDirty = true; break;
                case "cards.json": cardsDirty = true; break;
                case "history.json": historyDirty = true; break;
                case "activity.json": activityDirty = true; break;
                case "tasks.json": tasksDirty = true; break;
                case "meta.json": metaDirty = true; break;
                default: taggedDirty = true; break;
            }
            if (saveTimer != null) { saveTimer.Interval = 5000; saveTimer.Start(); }
        }
        // Waits until queued writes are on disk (used before exit, backup and restore).
        public static void FlushWrites()
        {
            lock (ioLock)
            {
                var until = DateTime.Now.AddSeconds(10);
                while (writerBusy && DateTime.Now < until) System.Threading.Monitor.Wait(ioLock, 500);
            }
        }

        static bool asyncSave;
        static void Write(string name, object o)
        {
            // Async: o is already a snapshot, serialized on the worker. Sync (exit, backup, restore): serialize here.
            if (asyncSave) QueueWrite(name, o);
            else { string json = Js().Serialize(o); FlushWrites(); WriteText(name, json); }
        }

        static void Kick() { if (saveTimer != null) { saveTimer.Stop(); saveTimer.Start(); } }

        public static void MarkNotes() { notesDirty = true; Kick(); }
        public static void MarkPrompts() { promptsDirty = true; Kick(); }
        public static void MarkClips() { clipsDirty = true; Kick(); }
        public static void MarkSettings() { settingsDirty = true; Kick(); }

        public static void RaiseNotes() { var h = NotesChanged; if (h != null) h(); }
        public static void RaisePrompts() { var h = PromptsChanged; if (h != null) h(); }
        public static void RaiseClips() { var h = ClipsChanged; if (h != null) h(); }

        public static void SaveNow()
        {
            if (notesDirty) notesDirty = !Safe("notes.json", Notes.ToList());
            if (promptsDirty) promptsDirty = !Safe("prompts.json", Prompts.ToList());
            if (clipsDirty) clipsDirty = !Safe("clips.json", Clips.ToList());
            if (settingsDirty) settingsDirty = !Safe("settings.json", asyncSave ? (object)Snap(Settings) : Settings);
            if (cardsDirty) cardsDirty = !Safe("cards.json", Cards.ToList());
            if (historyDirty) historyDirty = !Safe("history.json", asyncSave ? (object)Snap(History) : History);
            // Activity is large and changes often: the timer path saves it at most about every 5 s; exit/backup saves at once.
            if (activityDirty && (!asyncSave || !ActivityWaiting()))
            {
                activityDirty = !Safe("activity.json", Activity.ToList());
                activityWait = false;
            }
            if (tasksDirty) tasksDirty = !Safe("tasks.json", Tasks.ToList());
            if (metaDirty) metaDirty = !Safe("meta.json", Meta.ToList());
            if (taggedDirty)
            {
                bool filesOk = Safe("tagged.json", Tagged.ToList());
                bool catsOk = Safe("tagcats.json", TagCats.ToList());
                taggedDirty = !filesOk || !catsOk;
            }
            bool actWaiting = activityDirty && activityWait;
            if (saveTimer != null && (notesDirty || promptsDirty || clipsDirty || settingsDirty || cardsDirty || historyDirty || taggedDirty || tasksDirty || metaDirty || (activityDirty && !actWaiting)))
            {
                saveTimer.Interval = 5000;
                saveTimer.Start();
            }
            else if (saveTimer != null && actWaiting)
            {
                saveTimer.Interval = 700; // re-check until the activity save is due
                saveTimer.Start();
            }
            else if (saveTimer != null) saveTimer.Interval = 700;
            if (!asyncSave) FlushWrites(); // a direct SaveNow (exit, backup) returns only when everything is on disk
        }

        static bool Safe(string name, object o)
        {
            try { Write(name, o); return true; }
            catch (Exception ex) { App.Log("Save failed " + name + ": " + ex.Message); return false; }
        }

        // ---------- Clipboard history ----------

        public static void AddClip(Clip c)
        {
            Clip same = null;
            if (c.Kind == "image") same = Clips.FirstOrDefault(x => x.Kind == "image" && x.Hash == c.Hash);
            else same = Clips.FirstOrDefault(x => x.Kind == c.Kind && x.Text == c.Text);
            if (same != null)
            {
                Clips.Remove(same);
                same.Time = c.Time;
                same.Source = c.Source;
                if (c.Kind == "image" && c.Image != same.Image) DeleteImageFile(c.Image);
                c = same;
            }
            Clips.Insert(0, c);
            Trim();
            MarkClips();
            RaiseClips();
        }

        public static void TouchClip(Clip c)
        {
            c.Uses++;
            c.Time = Now();
            // A paste queue can still hold an item explicitly removed from history.
            if (!Clips.Remove(c)) return;
            Clips.Insert(0, c);
            MarkClips();
            RaiseClips();
        }

        static void Trim()
        {
            int max = Settings.MaxClips;
            int unpinned = 0, images = 0;
            for (int i = 0; i < Clips.Count; i++)
            {
                var c = Clips[i];
                if (c.Pinned || !string.IsNullOrEmpty(c.Folder)) continue;
                unpinned++;
                if (c.Kind == "image") images++;
                if (unpinned > max || (c.Kind == "image" && images > 150))
                {
                    RemoveClipAt(i);
                    i--;
                    if (c.Kind == "image") images--;
                    unpinned--;
                }
            }
        }

        static void RemoveClipAt(int i)
        {
            var c = Clips[i];
            Clips.RemoveAt(i);
            if (c.Kind == "image") DeleteImageFile(c.Image);
            Thumbs.Forget(c.Id);
        }

        public static void RemoveClips(IEnumerable<Clip> list)
        {
            foreach (var c in list.ToList())
            {
                int i = Clips.IndexOf(c);
                if (i >= 0) RemoveClipAt(i);
            }
            MarkClips();
            RaiseClips();
        }

        static void DeleteImageFile(string name)
        {
            if (!ValidImageName(name)) return;
            try { File.Delete(Path.Combine(ImgDir, name)); } catch { }
        }

        // Worker-thread safe: no access to Clips. Returns png bytes and hash.
        public static byte[] EncodePng(Image img, out string hash)
        {
            using (var ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                byte[] bytes = ms.ToArray();
                using (var sha = SHA1.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                return bytes;
            }
        }

        // UI thread: builds the clip. writtenName != null means a worker already wrote that file into ImgDir.
        public static Clip ImageClipFrom(byte[] bytes, string hash, int w, int h, string writtenName)
        {
            var existing = Clips.FirstOrDefault(x => x.Kind == "image" && x.Hash == hash);
            var c = new Clip();
            c.Id = NewId();
            c.Kind = "image";
            c.Hash = hash;
            c.ImgW = w;
            c.ImgH = h;
            c.Text = "Image " + w + " x " + h;
            if (existing != null)
            {
                c.Image = existing.Image;
                if (writtenName != null && writtenName != existing.Image) DeleteImageFile(writtenName);
            }
            else if (writtenName != null) c.Image = writtenName;
            else
            {
                c.Image = c.Id + ".png";
                File.WriteAllBytes(Path.Combine(ImgDir, c.Image), bytes);
            }
            return c;
        }

        public static Clip MakeImageClip(Image img)
        {
            string hash;
            var bytes = EncodePng(img, out hash);
            return ImageClipFrom(bytes, hash, img.Width, img.Height, null);
        }

        public static void TrimNow() { Trim(); MarkClips(); RaiseClips(); }

        public static List<string> Folders()
        {
            return Clips.Select(c => c.Folder).Where(f => !string.IsNullOrEmpty(f)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<string> NoteTags()
        {
            return Notes.SelectMany(n => SplitTags(n.Tags)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static IEnumerable<string> SplitTags(string tags)
        {
            return (tags ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0);
        }

        static bool ValidImageName(string name)
        {
            return !string.IsNullOrEmpty(name) && name != "." && name != ".." && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && Path.GetFileName(name) == name;
        }

        public static string ImagePath(Clip c) { return ValidImageName(c.Image) ? Path.Combine(ImgDir, c.Image) : ""; }

        public static Image LoadImage(Clip c)
        {
            try
            {
                var bytes = File.ReadAllBytes(ImagePath(c));
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        // ---------- Prompts ----------

        public static Prompt NewPrompt(string title, string body, string category)
        {
            var p = new Prompt();
            p.Id = NewId();
            p.Title = title ?? "";
            p.Body = body ?? "";
            p.Category = category ?? "";
            p.Created = p.Modified = Now();
            Prompts.Insert(0, p);
            MarkPrompts();
            RaisePrompts();
            return p;
        }

        public static string TitleFrom(string text)
        {
            // Walk line by line without splitting the whole text (clips can be megabytes; this runs on every paint).
            if (string.IsNullOrEmpty(text)) return "";
            int pos = 0, limit = Math.Min(text.Length, 64 * 1024);
            while (pos < limit)
            {
                int nl = text.IndexOf('\n', pos, limit - pos);
                int end = nl < 0 ? limit : nl;
                var t = text.Substring(pos, Math.Min(end - pos, 400)).Trim();
                if (t.Length > 0) return t.Length > 70 ? t.Substring(0, 70) + "..." : t;
                if (nl < 0) break;
                pos = nl + 1;
            }
            return "";
        }

        // ---------- Tasks ----------

        public static TaskItem AddTask(TaskItem t)
        {
            t.Id = NewId(); t.Created = t.Modified = Now();
            if (string.IsNullOrEmpty(t.List)) t.List = "Inbox";
            Tasks.Insert(0, t); MarkTasks(); RaiseTasks(); return t;
        }
        public static TaskItem NewTask(string title) { return AddTask(new TaskItem { Title = title ?? "" }); }

        public static void TouchTask(TaskItem t) { t.Modified = Now(); MarkTasks(); RaiseTasks(); }

        public static void CompleteTask(TaskItem t, bool done)
        {
            if (done && t.Repeat != "none" && t.Due > 0)
            {
                long oldDue = t.Due, newDue = NextOccurrence(t.Due, t.Repeat);
                t.Due = newDue;
                t.RemindAt = t.RemindAt > 0 || t.DueHasTime ? (t.RemindAt > 0 ? t.RemindAt + (newDue - oldDue) : (t.DueHasTime ? newDue : 0)) : 0;
                foreach (var s in t.Steps) s.Done = false;
                t.Snoozes = 0;
            }
            else { t.Done = done; t.DoneAt = done ? Now() : 0; }
            TouchTask(t);
        }

        public static long NextOccurrence(long ticks, string repeat)
        {
            var d = new DateTime(ticks);
            var now = DateTime.Now;
            for (int guard = 0; guard < 5000; guard++)
            {
                switch (repeat)
                {
                    case "daily": d = d.AddDays(1); break;
                    case "weekdays": d = d.AddDays(1); while (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) d = d.AddDays(1); break;
                    case "weekly": d = d.AddDays(7); break;
                    case "monthly": d = d.AddMonths(1); break;
                    case "yearly": d = d.AddYears(1); break;
                    default: return ticks;
                }
                if (d > now) break;
            }
            return d.Ticks;
        }

        public static List<string> TaskLists()
        {
            var l = Tasks.Select(t => t.List).Where(x => !string.IsNullOrEmpty(x) && !x.Equals("Inbox", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            l.Insert(0, "Inbox");
            return l;
        }

        public static string ExportPrompts(List<Prompt> list) { return Js().Serialize(list); }

        public static List<Prompt> ImportPrompts(string json)
        {
            var list = Js().Deserialize<List<Prompt>>(json) ?? new List<Prompt>();
            var added = new List<Prompt>();
            foreach (var p in list)
            {
                if (p == null) continue;
                if (Prompts.Any(x => x.Title == p.Title && x.Body == p.Body)) continue;
                p.Id = NewId();
                NormalizePrompt(p);
                if (p.Created == 0) p.Created = Now();
                if (p.Modified == 0) p.Modified = Now();
                Prompts.Add(p);
                added.Add(p);
            }
            MarkPrompts();
            RaisePrompts();
            return added;
        }

        static void NormalizePrompt(Prompt p)
        {
            if (p.Id == null) p.Id = NewId();
            if (p.Title == null) p.Title = "";
            if (p.Body == null) p.Body = "";
            if (p.Category == null) p.Category = "";
            if (p.Tags == null) p.Tags = "";
            if (p.Trigger == null) p.Trigger = "";
            if (p.Versions == null) p.Versions = new List<PromptVersion>();
            p.Versions.RemoveAll(v => v == null);
            foreach (var v in p.Versions) { if (v.Title == null) v.Title = ""; if (v.Body == null) v.Body = ""; }
        }
    }

    // Small in-memory thumbnail cache for clipboard images.
    static class Thumbs
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();

        public static Image Get(Clip c, int maxW, int maxH)
        {
            Image t;
            string key = c.Id + "|" + maxW + "|" + maxH;
            if (cache.TryGetValue(key, out t)) return t;
            t = null;
            var img = Store.LoadImage(c);
            if (img != null)
            {
                double s = Math.Min((double)maxW / img.Width, (double)maxH / img.Height);
                if (s > 1) s = 1;
                int w = Math.Max(1, (int)(img.Width * s)), h = Math.Max(1, (int)(img.Height * s));
                var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, w, h);
                }
                img.Dispose();
                t = bmp;
            }
            if (cache.Count > 400) Clear();
            cache[key] = t;
            return t;
        }

        public static void Forget(string id)
        {
            foreach (var k in cache.Keys.Where(k => k.StartsWith(id + "|")).ToList())
            {
                if (cache[k] != null) cache[k].Dispose();
                cache.Remove(k);
            }
        }

        public static void Clear()
        {
            foreach (var v in cache.Values) if (v != null) v.Dispose();
            cache.Clear();
        }
    }
}
