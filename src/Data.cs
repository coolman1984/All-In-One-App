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
        public string Design = "";           // "" (not chosen yet -> flow) | flow | serq | nexus | classic | soft
        public int DesignRev;                // 3 = the Flow default was applied once
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
        public bool OverlayDock = true, OverlayMenuBar = true, OverlayHotCorner, OverlayMagnify = true, OverlayAutoHide = true;
        public int OverlayDockSize = 48;     // icon size in px at 100 % scale
        public bool HideSecurityPopups = false;  // optional: hide corporate security agent pop-ups (off by default for safety)
        public bool OverlayHideTaskbar = true;   // full-screen mode: hide the Windows taskbar while the overlay is on
        public bool OverlayTaskbarHidden;        // the overlay hid it (restored on the next start after a crash)
        public int OverlayTaskbarState;          // the taskbar's own auto-hide state before that
        public int OverlayDockVersion;
        public string OverlayDockEdge = "bottom";   // bottom | left | right
        public string OverlayCorner = "topleft";    // hot corner that opens the window overview
        public List<DockItem> OverlayApps = new List<DockItem>();   // pinned dock apps
        public int ExplorerSortCol = 2; public bool ExplorerSortDesc = true;   // Explorer panes: Modified, newest first
        public float AgentTextScale = 1f;   // Agents chat text size (1 = interface size)
        public int AgentInputHeight;   // message box height on the Agents page (0 = default)
        public Dictionary<string, List<string>> AgentMyModels = new Dictionary<string, List<string>>();   // your own model names per agent (shown first)
        public List<FolderSet> FolderSets = new List<FolderSet>();
        public List<TermLaunch> TermHistory = new List<TermLaunch>();
        public DateTime CliCheckedAt;   // last automatic agent CLI update check
        public List<string> MdVaults = new List<string>();   // Markdown notes: folders of .md files
        public string MdAiAgent = "claude"; public bool MdAiOpen = true;   // old Markdown AI panel (kept for old settings files)
        public bool AssistantOpen;   // the Assistant panel (right side of every page)
        public Dictionary<string, string> ViewStacks = new Dictionary<string, string>();   // VS Code style side sections: state per stack
        public List<string> MdTabs = new List<string>();   // Markdown: open note tabs
        public List<string> ViewerTabs = new List<string>();   // Documents page: open files
        public bool ViewerEdit = true;   // Documents page: Excel / Word / PowerPoint open inside the tab for editing
        public bool MdTreeHidden, MdSideHidden;   // Markdown: note tree (Ctrl+B) and side panel (Ctrl+Shift+B) hidden
        public List<string> SearchHistory = new List<string>();   // Ctrl+K: recent searches (newest first)
        public Dictionary<string, string> AgentDefaultAccount = new Dictionary<string, string>();   // CLI id -> named account new chats use
        public bool CodexManaged;   // Codex refused full access ("disallowed by requirements"): use workspace-write + on-request
        public List<string> BrowserTabs = new List<string>();
        public List<string> BrowserDownloads = new List<string>();                     // newest first
        public Dictionary<string, double> BrowserZoom = new Dictionary<string, double>();   // site -> zoom  // Browser page: open tabs (restored at start)
        public int BrowserActive; public string BrowserHome = ""; public bool BrowserAutomation;
        public int MdVaultIndex; public string MdMode = "read", MdLast = "";   // terminals opened from the app (newest first)
        // App-only defaults per agent for chats that did not pick their own (the CLI's own settings are not touched).
        public Dictionary<string, string> AgentDefaultModel = new Dictionary<string, string>(), AgentDefaultEffort = new Dictionary<string, string>();
        public int AgentDefaultsVersion;   // saved groups of folders (Explorer, Terminals, Agents)
        public List<string> AgentDeadModels = new List<string>();   // "agent|model" the CLI said no longer exist (hidden from menus)
        public List<string> AgentCmdFavs = new List<string>();      // favorite "/" commands of the Agents page, in your order
        public List<AgentCustomCmd> AgentCustomCmds = new List<AgentCustomCmd>();   // your own "/" commands
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
    public class TermWorkspace { public bool Fav; public string Name = "My terminals"; public string Layout = "4"; public string Mode = "tiled"; public List<TermPane> Panes = new List<TermPane>(); }

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
        public static bool Pending { get { return WritePending || notesDirty || promptsDirty || clipsDirty || settingsDirty || cardsDirty || historyDirty || activityDirty || taggedDirty || tasksDirty || metaDirty; } }
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
            Backup.ApplyPending(Dir);
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory(ImgDir);
            Notes = Read<List<Note>>("notes.json") ?? new List<Note>();
            Prompts = Read<List<Prompt>>("prompts.json") ?? new List<Prompt>();
            Clips = Read<List<Clip>>("clips.json") ?? new List<Clip>();
            Settings = Read<Settings>("settings.json") ?? new Settings();
            Cards = Read<List<CardDef>>("cards.json") ?? new List<CardDef>();
            History = Read<SysHistory>("history.json") ?? new SysHistory();
            Activity = Read<List<ActivityRecord>>("activity.json") ?? new List<ActivityRecord>();
            Tagged = Read<List<TaggedFile>>("tagged.json") ?? new List<TaggedFile>();
            TagCats = Read<List<TagCategory>>("tagcats.json") ?? new List<TagCategory>();
            Tasks = Read<List<TaskItem>>("tasks.json") ?? new List<TaskItem>();
            Meta = Read<List<ItemMeta>>("meta.json") ?? new List<ItemMeta>();
            Normalize();

            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 700;
            saveTimer.Tick += delegate { saveTimer.Stop(); asyncSave = true; try { SaveNow(); } finally { asyncSave = false; } };
            // A data file that was busy at start stays write-protected; look again every 30 s and merge it back in.
            retryTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            retryTimer.Tick += delegate { if (LockedSections().Length == 0) { retryTimer.Stop(); return; } string m = RetryLocked(); if (m != null) App.Log(m); };
            if (LockedSections().Length > 0) retryTimer.Start();
        }

        // Repairs missing / invalid values after loading or merging. Safe to run more than once.
        static void Normalize()
        {
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
            if (Settings.Theme < 0 || Settings.Theme >= Ui.ThemeNames.Length) Settings.Theme = 0;
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
            Meta.RemoveAll(m => m == null || string.IsNullOrEmpty(m.Scope) || string.IsNullOrEmpty(m.Id));
            foreach (var m in Meta) { if (m.Color == null) m.Color = ""; if (m.Tags == null) m.Tags = ""; if (m.Note == null) m.Note = ""; if (m.Name == null) m.Name = ""; }
            var s = Settings;
            if (s.HeadingFont == null) s.HeadingFont = ""; if (s.MonoFont == null) s.MonoFont = ""; if (s.AccentColor == null) s.AccentColor = "";
            if (s.UserName == null) s.UserName = ""; if (s.AppTitle == null) s.AppTitle = "";
            if (s.Density != "compact" && s.Density != "roomy") s.Density = "normal";
            if (float.IsNaN(s.HeadingScale) || s.HeadingScale < 0.85f || s.HeadingScale > 1.4f) s.HeadingScale = 1f;
            if (float.IsNaN(s.MonoFontSize) || s.MonoFontSize < 7 || s.MonoFontSize > 20) s.MonoFontSize = 10f;
            if (float.IsNaN(s.AgentTextScale) || s.AgentTextScale < 0.7f || s.AgentTextScale > 2.5f) s.AgentTextScale = 1f;
            if (s.CornerRadius > 24) s.CornerRadius = 24;
            if (s.PageNames == null) s.PageNames = new Dictionary<string, string>();
            if (s.NavOrder == null) s.NavOrder = new List<string>();
            if (s.NavHidden == null) s.NavHidden = new List<string>();
            if (s.NavFavorites == null) s.NavFavorites = new List<string>();
            if (s.OverlayApps == null) s.OverlayApps = new List<DockItem>();
            if (s.AgentCmdFavs == null) s.AgentCmdFavs = new List<string>();
            if (s.AgentDeadModels == null) s.AgentDeadModels = new List<string>();
            if (s.TermHistory == null) s.TermHistory = new List<TermLaunch>();
            if (s.MdVaults == null) s.MdVaults = new List<string>();
            if (s.BrowserTabs == null) s.BrowserTabs = new List<string>();
            if (s.BrowserDownloads == null) s.BrowserDownloads = new List<string>();
            if (s.BrowserZoom == null) s.BrowserZoom = new Dictionary<string, double>();
            if (s.SearchHistory == null) s.SearchHistory = new List<string>();
            if (s.AgentDefaultAccount == null) s.AgentDefaultAccount = new Dictionary<string, string>();
            if (s.ViewStacks == null) s.ViewStacks = new Dictionary<string, string>();
            if (s.MdTabs == null) s.MdTabs = new List<string>();
            if (s.ViewerTabs == null) s.ViewerTabs = new List<string>();
            s.MdVaults.RemoveAll(string.IsNullOrWhiteSpace);
            s.TermHistory.RemoveAll(x => x == null);
            foreach (var x in s.TermHistory) { if (x.Folders == null) x.Folders = new List<string>(); if (x.Kind == null) x.Kind = ""; if (x.Title == null) x.Title = ""; if (x.Command == null) x.Command = ""; if (x.Shell == null) x.Shell = ""; if (x.Layout == null) x.Layout = ""; }
            if (s.FolderSets == null) s.FolderSets = new List<FolderSet>();
            if (s.AgentDefaultModel == null) s.AgentDefaultModel = new Dictionary<string, string>();
            if (s.AgentDefaultEffort == null) s.AgentDefaultEffort = new Dictionary<string, string>();
            if (s.AgentDefaultsVersion < 1)
            {
                // Owner's choice (2026-11-16): Cline in chats = Kimi K3.1 Pro on Cline Pass, effort high.
                s.AgentDefaultModel["cline"] = "cline-pass: moonshotai/kimi-k3.1-pro";
                s.AgentDefaultsVersion = 1;
            }
            if (s.AgentDefaultsVersion < 2) { s.AgentDefaultEffort["cline"] = "high"; s.AgentDefaultsVersion = 2; }
            s.FolderSets.RemoveAll(x => x == null);
            foreach (var fs in s.FolderSets) { if (fs.Name == null) fs.Name = ""; if (fs.Note == null) fs.Note = ""; if (fs.Folders == null) fs.Folders = new List<string>(); fs.Folders.RemoveAll(string.IsNullOrWhiteSpace); }
            if (s.AgentMyModels == null) s.AgentMyModels = new Dictionary<string, List<string>>();
            if (s.AgentCustomCmds == null) s.AgentCustomCmds = new List<AgentCustomCmd>();
            s.AgentCustomCmds.RemoveAll(c => c == null || string.IsNullOrEmpty(c.Name));
            s.OverlayApps.RemoveAll(d => d == null || string.IsNullOrEmpty(d.Path));
            if (s.OverlayDockVersion < 2) { if (s.OverlayDockSize == 56) s.OverlayDockSize = 28; s.OverlayDockVersion = 2; }   // first dock was too big
            if (s.OverlayDockVersion < 3) { s.OverlayAutoHide = true; s.OverlayDockVersion = 3; }   // full screen: the dock appears only at the bottom edge
            if (s.OverlayDockVersion < 4) { if (s.OverlayDockSize <= 32) s.OverlayDockSize = 48; s.OverlayHotCorner = false; s.OverlayDockVersion = 4; }   // big icons; no surprise overview from the corner
            if (s.OverlayDockSize < 20 || s.OverlayDockSize > 128) s.OverlayDockSize = 48;
            if (s.OverlayDockEdge != "left" && s.OverlayDockEdge != "right") s.OverlayDockEdge = "bottom";
            if (string.IsNullOrEmpty(s.OverlayCorner)) s.OverlayCorner = "topleft";
            // New design: switch once to the Flow look (clean white sidebar, soft blue pill, colourful dashboard); SerQ, Soft, Nexus and Classic stay available in Settings and Look picker.
            if (string.IsNullOrEmpty(Settings.Design) || (Settings.DesignRev < 3 && (Settings.Design == "serq" || Settings.Design == "nexus"))) { Settings.Design = "flow"; Settings.Theme = Ui.FlowTheme; }
            Settings.DesignRev = 3;
            if (string.IsNullOrEmpty(Settings.NoteFontName)) Settings.NoteFontName = "Segoe UI";
            if (float.IsNaN(Settings.NoteFontSize) || float.IsInfinity(Settings.NoteFontSize) || Settings.NoteFontSize < 6 || Settings.NoteFontSize > 48) Settings.NoteFontSize = 11f;
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
            if (!string.IsNullOrEmpty(DirOverride)) return DirOverride;
            try
            {
                if (File.Exists(LocationFile))
                {
                    string p = File.ReadAllText(LocationFile).Trim();
                    if (p.Length > 0 && Path.IsPathRooted(p)) return p;
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
                DataFiles.CheckSeparate(Dir, newDir);
                if (Directory.Exists(newDir) && Directory.EnumerateFileSystemEntries(newDir).Any())
                    return "Choose an empty destination folder to avoid replacing existing data.";
                SaveNow(); FlushWrites();
                if (Pending) return "Current changes could not be saved. The data folder was not changed.";
                if (!AgentStore.FlushWrites()) return "Agent chats could not be saved. The data folder was not changed.";
                DataFiles.CopyTree(Dir, newDir);
                // The app keeps using the old folder until it restarts: remember what was copied, so exit can
                // carry over every later change (new, edited and deleted files) to the new folder.
                pendingMoveTo = newDir; pendingMoveAt = DateTime.UtcNow;
                pendingMoveFiles = new HashSet<string>(Directory.GetFiles(Dir, "*", SearchOption.AllDirectories).Select(f => f.Substring(Dir.TrimEnd('\\').Length + 1)), StringComparer.OrdinalIgnoreCase);
                if (string.Equals(newDir.TrimEnd('\\'), DefaultDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) { if (File.Exists(LocationFile)) File.Delete(LocationFile); }
                else
                {
                    string temporary = LocationFile + ".tmp";
                    File.WriteAllText(temporary, newDir, new UTF8Encoding(false));
                    if (File.Exists(LocationFile)) File.Replace(temporary, LocationFile, LocationFile + ".bak", true);
                    else File.Move(temporary, LocationFile);
                }
                if (!string.IsNullOrEmpty(DirOverride)) DirOverride = newDir;
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ---------- safe open: missing / busy / broken (docs/plans/never-lose-a-note.html) ----------
        enum ReadState { Ok, Missing, Busy }
        // One wait budget for ALL files at start (OneDrive sync, antivirus scan), not per file.
        static int busyBudgetMs = 5000;
        static readonly HashSet<string> writeLocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static System.Windows.Forms.Timer retryTimer;
        public static event Action LockedChanged;

        static ReadState TryReadText(string file, out string text)
        {
            text = null;
            int wait = 100;
            while (true)
            {
                try
                {
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8, true)) text = r.ReadToEnd();
                    return ReadState.Ok;
                }
                catch (FileNotFoundException) { return ReadState.Missing; }
                catch (DirectoryNotFoundException) { return ReadState.Missing; }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException)) throw;
                    if (busyBudgetMs <= 0) { App.Log("Data file busy: " + file + " (" + ex.Message + ")"); return ReadState.Busy; }
                    int d = Math.Min(wait, busyBudgetMs);
                    System.Threading.Thread.Sleep(d); busyBudgetMs -= d; wait = Math.Min(wait * 2, 1000);
                }
            }
        }

        static string pendingMoveTo; static DateTime pendingMoveAt; static HashSet<string> pendingMoveFiles;
        // Called on exit, after the last save: copy files changed since the data-folder move to the new folder and
        // remove the ones deleted since then, so nothing done between the move and the restart is lost.
        public static void ReconcilePendingMove()
        {
            string to = pendingMoveTo;
            if (string.IsNullOrEmpty(to) || !Directory.Exists(to)) return;
            string root = Dir.TrimEnd('\\');
            int copied = 0, removed = 0;
            try
            {
                var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(root.Length + 1); now.Add(rel);
                    string dest = Path.Combine(to, rel);
                    try
                    {
                        if (File.Exists(dest) && File.GetLastWriteTimeUtc(f) <= File.GetLastWriteTimeUtc(dest)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(f, dest, true); copied++;
                    }
                    catch (Exception ex) { App.Log("Move reconcile " + rel + ": " + ex.Message); }
                }
                if (pendingMoveFiles != null)
                    foreach (var rel in pendingMoveFiles.Where(r => !now.Contains(r)))
                    {
                        string dest = Path.Combine(to, rel);
                        try { if (File.Exists(dest) && File.GetLastWriteTimeUtc(dest) <= pendingMoveAt.AddMinutes(1)) { File.Delete(dest); removed++; } }
                        catch (Exception ex) { App.Log("Move reconcile delete " + rel + ": " + ex.Message); }
                    }
                if (copied + removed > 0) App.Log("Data folder move: carried " + copied + " changed and " + removed + " deleted file(s) over to " + to);
            }
            catch (Exception ex) { App.Log("Move reconcile: " + ex.Message); }
            pendingMoveTo = null;
        }

        static T Read<T>(string name) where T : class
        {
            string p = Path.Combine(Dir, name);
            bool primaryBusy = false;
            foreach (string file in new[] { p, p + ".bak" })
            {
                string text;
                ReadState state;
                try { state = TryReadText(file, out text); }
                catch (Exception ex) { App.Log("Could not read " + file + ": " + ex.Message); continue; }
                if (state == ReadState.Missing) continue;
                if (state == ReadState.Busy)
                {
                    // Busy is NOT broken: never fall back to the older .bak and write it over the newer file.
                    if (file == p) { primaryBusy = true; lock (recovered) writeLocked.Add(name); }
                    continue;
                }
                try
                {
                    var o = Js().Deserialize<T>(text);
                    if (o != null) { if (file != p && !primaryBusy) recovered.Add(name); return o; }
                }
                catch (Exception ex)
                {
                    App.Log("Could not read " + file + ": " + ex.Message);
                    try { File.Copy(file, file + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { }
                }
            }
            return null;
        }

        // Reads each write-protected file again. On success its items are merged with this session's (by id, nothing
        // is dropped), the section is unlocked and saved. Returns a short report, or null when nothing changed.
        public static string RetryLocked()
        {
            var names = LockedSections();
            if (names.Length == 0) return null;
            var opened = new List<string>();
            foreach (string name in names)
            {
                string text;
                busyBudgetMs = 1500;
                ReadState state;
                try { state = TryReadText(Path.Combine(Dir, name), out text); } catch (Exception ex) { App.Log("Retry " + name + ": " + ex.Message); continue; }
                if (state == ReadState.Busy) continue;
                try
                {
                    if (state == ReadState.Ok) MergeFromDisk(name, text);
                    lock (recovered) writeLocked.Remove(name);
                    MarkByName(name);
                    opened.Add(name);
                }
                catch (Exception ex)
                {
                    // Opened but unreadable = broken: keep a copy, then let the session's version be saved.
                    App.Log("Retry " + name + ": " + ex.Message);
                    try { File.Copy(Path.Combine(Dir, name), Path.Combine(Dir, name + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss")), true); } catch { }
                    lock (recovered) writeLocked.Remove(name);
                    MarkByName(name);
                    opened.Add(name);
                }
            }
            if (opened.Count == 0) return null;
            Normalize();
            RaiseAll();
            var h = LockedChanged; if (h != null) h();
            return "Data files opened again and merged: " + string.Join(", ", opened.ToArray());
        }

        static void MergeFromDisk(string name, string text)
        {
            var js = Js();
            switch (name)
            {
                case "notes.json": MergeList(Notes, js.Deserialize<List<Note>>(text)); break;
                case "prompts.json": MergeList(Prompts, js.Deserialize<List<Prompt>>(text)); break;
                case "clips.json": MergeList(Clips, js.Deserialize<List<Clip>>(text)); break;
                case "cards.json": MergeList(Cards, js.Deserialize<List<CardDef>>(text)); break;
                case "activity.json": MergeList(Activity, js.Deserialize<List<ActivityRecord>>(text)); break;
                case "tagged.json": MergeList(Tagged, js.Deserialize<List<TaggedFile>>(text)); break;
                case "tagcats.json": MergeList(TagCats, js.Deserialize<List<TagCategory>>(text)); break;
                case "tasks.json": MergeList(Tasks, js.Deserialize<List<TaskItem>>(text)); break;
                case "meta.json": MergeList(Meta, js.Deserialize<List<ItemMeta>>(text)); break;
                case "settings.json": MergeObject(Settings, js.Deserialize<Settings>(text), settingsDirty, name); break;
                case "history.json": MergeObject(History, js.Deserialize<SysHistory>(text), historyDirty, name); break;
            }
        }

        // Disk items this session does not have (same id) are added; items this session has win.
        static void MergeList<T>(List<T> live, List<T> disk) where T : class
        {
            if (live == null || disk == null) return;
            var have = new HashSet<string>(live.Where(x => x != null).Select(ItemKey));
            foreach (var item in disk) if (item != null && !have.Contains(ItemKey(item))) live.Add(item);
        }
        static string ItemKey(object o)
        {
            var t = o.GetType();
            var id = t.GetField("Id"); var scope = t.GetField("Scope"); var nm = t.GetField("Name");
            if (id != null) return (scope != null ? Convert.ToString(scope.GetValue(o)) + "|" : "") + Convert.ToString(id.GetValue(o));
            if (nm != null) return Convert.ToString(nm.GetValue(o));
            return Js().Serialize(o);
        }
        // Settings-like objects: untouched this session -> take the disk values; changed -> keep the session's
        // values and keep the disk version as a side file.
        static void MergeObject<T>(T live, T disk, bool dirty, string name) where T : class
        {
            if (live == null || disk == null) return;
            if (dirty)
            {
                File.Copy(Path.Combine(Dir, name), Path.Combine(Dir, Path.GetFileNameWithoutExtension(name) + ".disk-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), true);
                return;
            }
            foreach (var f in typeof(T).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)) f.SetValue(live, f.GetValue(disk));
        }
        static void MarkByName(string name)
        {
            switch (name)
            {
                case "tagged.json": case "tagcats.json": taggedDirty = true; break;
                case "settings.json": if (!settingsDirty) return; settingsDirty = true; break;
                case "history.json": if (!historyDirty) return; historyDirty = true; break;
                default: Remark(name); break;
            }
            Kick();
        }
        static void RaiseAll()
        {
            foreach (var h in new[] { NotesChanged, PromptsChanged, ClipsChanged, CardsChanged, ActivityChanged, TaggedChanged, TasksChanged, MetaChanged })
                if (h != null) try { h(); } catch (Exception ex) { App.Log("Data refresh: " + ex.Message); }
        }

        public static string[] LockedSections() { lock (recovered) return writeLocked.ToArray(); }
        static bool IsLocked(string name) { lock (recovered) return writeLocked.Contains(name); }

        // Daily snapshot (first save of each day, last 10 days) + shrink guard (a save that would cut a big file
        // to less than half takes a snapshot first). Text data files only, no images.
        static readonly object snapLock = new object();
        static string snapDay;
        public static string SnapshotDir { get { return Path.Combine(Dir, "snapshots"); } }
        static void Snapshot(string folderName, string onlyFile)
        {
            try
            {
                string dir = Path.Combine(SnapshotDir, folderName);
                Directory.CreateDirectory(dir);
                foreach (var f in onlyFile != null ? new[] { Path.Combine(Dir, onlyFile) } : Directory.GetFiles(Dir, "*.json"))
                    if (File.Exists(f)) File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                var all = new DirectoryInfo(SnapshotDir).GetDirectories().OrderByDescending(d => d.Name, StringComparer.Ordinal).ToList();
                foreach (var old in all.Where(d => d.Name.Length == 10).Skip(10).Concat(all.Where(d => d.Name.Length != 10).Skip(10)))
                    try { old.Delete(true); } catch { }
            }
            catch (Exception ex) { App.Log("Snapshot: " + ex.Message); }
        }
        static void GuardBeforeWrite(string name, string p, int newLength)
        {
            lock (snapLock)
            {
                string today = DateTime.Now.ToString("yyyy-MM-dd");
                if (snapDay != today) { snapDay = today; if (!Directory.Exists(Path.Combine(SnapshotDir, today))) Snapshot(today, null); }
                try
                {
                    var fi = new FileInfo(p);
                    if (fi.Exists && fi.Length > 4096 && newLength < fi.Length / 2)
                        Snapshot(DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + "-" + Path.GetFileNameWithoutExtension(name), name);
                }
                catch (Exception ex) { App.Log("Shrink guard: " + ex.Message); }
            }
        }

        static void WriteText(string name, string json)
        {
            string p = Path.Combine(Dir, name);
            if (IsLocked(name)) throw new IOException(name + " could not be read at start; it is protected from writing until it opens.");
            GuardBeforeWrite(name, p, json.Length);
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
        static readonly HashSet<string> failedWrites = new HashSet<string>();
        static bool WritePending { get { lock (ioLock) return writerBusy || pendingWrites.Count > 0 || failedWrites.Count > 0; } }
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
                try { WriteText(name, Js().Serialize(snapshot)); lock (ioLock) failedWrites.Remove(name); }
                catch (Exception ex)
                {
                    lock (ioLock) failedWrites.Add(name);
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
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (writerBusy && wait.ElapsedMilliseconds < 10000) System.Threading.Monitor.Wait(ioLock, 100);
                if (writerBusy) throw new IOException("Timed out waiting for pending saves; changes are still pending.");
            }
        }

        static bool asyncSave;
        static void Write(string name, object o)
        {
            // Async: o is already a snapshot, serialized on the worker. Sync (exit, backup, restore): serialize here.
            if (asyncSave) QueueWrite(name, o);
            else { string json = Js().Serialize(o); FlushWrites(); WriteText(name, json); lock (ioLock) failedWrites.Remove(name); }
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
            string[] failed; lock (ioLock) failed = failedWrites.ToArray();
            foreach (string name in failed) Remark(name);
            if (notesDirty) notesDirty = !Safe("notes.json", Notes.ToList());
            if (promptsDirty) promptsDirty = !Safe("prompts.json", Prompts.ToList());
            if (clipsDirty) clipsDirty = !Safe("clips.json", Clips.ToList());
            if (settingsDirty) settingsDirty = !Safe("settings.json", Settings);
            if (cardsDirty) cardsDirty = !Safe("cards.json", Cards.ToList());
            if (historyDirty) historyDirty = !Safe("history.json", History);
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
            if (IsLocked(name))
            {
                if (asyncSave) return false; // keep the change in memory (still dirty); the retry timer merges later
                // Exit / backup: never touch the unread file, but keep this session's version next to it.
                try
                {
                    string side = Path.Combine(Dir, Path.GetFileNameWithoutExtension(name) + ".session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                    File.WriteAllText(side, Js().Serialize(o), new UTF8Encoding(false));
                    App.Log(name + " was still busy: this session's changes were kept in " + side);
                    return true;
                }
                catch (Exception ex) { App.Log("Save failed " + name + ": " + ex.Message); return false; }
            }
            try { Write(name, asyncSave ? DataSnapshot.Copy(o) : o); return true; }
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
                if (newDue <= oldDue) { t.Done = true; t.DoneAt = Now(); TouchTask(t); return; }
                t.Due = newDue; t.Done = false; t.DoneAt = 0;
                long reminder = t.RemindAt > 0 ? t.RemindAt : (t.DueHasTime ? oldDue : 0);
                long delta = newDue - oldDue;
                t.RemindAt = reminder == 0 ? 0 : reminder <= DateTime.MaxValue.Ticks - delta ? reminder + delta : newDue;
                foreach (var s in t.Steps) s.Done = false;
                t.Snoozes = 0;
            }
            else { t.Done = done; t.DoneAt = done ? Now() : 0; }
            TouchTask(t);
        }

        public static long NextOccurrence(long ticks, string repeat) { return NextOccurrenceAt(ticks, repeat, DateTime.Now); }

        public static long NextOccurrenceAt(long ticks, string repeat, DateTime now)
        {
            if (ticks <= 0 || ticks > DateTime.MaxValue.Ticks) return ticks;
            var due = new DateTime(ticks);
            try
            {
                switch (repeat)
                {
                    case "daily": case "weekly":
                        long period = TimeSpan.TicksPerDay * (repeat == "weekly" ? 7 : 1);
                        long count = Math.Max(1, (now.Ticks - ticks) / period + 1);
                        return due.AddTicks(checked(count * period)).Ticks;
                    case "weekdays":
                        var day = due.AddDays(1);
                        if (day <= now) { day = now.Date.Add(due.TimeOfDay); if (day <= now) day = day.AddDays(1); }
                        while (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday) day = day.AddDays(1);
                        return day.Ticks;
                    case "monthly":
                        int months = Math.Max(1, (now.Year - due.Year) * 12 + now.Month - due.Month);
                        var month = due.AddMonths(months);
                        return (month > now ? month : due.AddMonths(months + 1)).Ticks;
                    case "yearly":
                        int years = Math.Max(1, now.Year - due.Year);
                        var year = due.AddYears(years);
                        return (year > now ? year : due.AddYears(years + 1)).Ticks;
                    default: return ticks;
                }
            }
            catch (ArgumentOutOfRangeException) { return ticks; }
            catch (OverflowException) { return ticks; }
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
        class Request
        {
            public string Key, Path;
            public int Width, Height;
            public readonly HashSet<System.Windows.Forms.Control> Views = new HashSet<System.Windows.Forms.Control>();
        }

        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        static readonly Dictionary<string, Request> pending = new Dictionary<string, Request>();
        static readonly Queue<Request> queue = new Queue<Request>();
        static bool decoding;

        public static Image Get(Clip c, int maxW, int maxH, System.Windows.Forms.Control view)
        {
            Image t;
            string key = c.Id + "|" + maxW + "|" + maxH;
            if (cache.TryGetValue(key, out t)) return t;
            if (App.I == null) return null;
            Request request;
            if (!pending.TryGetValue(key, out request))
            {
                if (queue.Count >= 48)
                {
                    var dropped = queue.Dequeue();
                    Request waiting;
                    if (pending.TryGetValue(dropped.Key, out waiting) && waiting == dropped) pending.Remove(dropped.Key);
                }
                request = new Request { Key = key, Path = Store.ImagePath(c), Width = maxW, Height = maxH };
                pending[key] = request;
                queue.Enqueue(request);
            }
            request.Views.Add(view);
            DecodeNext();
            return null;
        }

        static void DecodeNext()
        {
            if (decoding || queue.Count == 0) return;
            var request = queue.Dequeue();
            var sync = App.I.Sync;
            decoding = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                var image = LoadScaled(request.Path, request.Width, request.Height);
                try
                {
                    sync.BeginInvoke(new Action(() =>
                    {
                        decoding = false;
                        Request waiting;
                        if (pending.TryGetValue(request.Key, out waiting) && waiting == request)
                        {
                            pending.Remove(request.Key);
                            if (cache.Count >= 400) ClearImages();
                            cache[request.Key] = image;
                            foreach (var view in request.Views) if (!view.IsDisposed && view.Visible) view.Invalidate();
                        }
                        else if (image != null) image.Dispose();
                        DecodeNext();
                    }));
                }
                catch (InvalidOperationException) { if (image != null) image.Dispose(); }
            });
        }

        internal static Image LoadScaled(string path, int maxW, int maxH)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Bitmap scaled = null;
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var image = Image.FromStream(file))
                {
                    double scale = Math.Min(1, Math.Min((double)maxW / image.Width, (double)maxH / image.Height));
                    scaled = new Bitmap(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                    using (var graphics = Graphics.FromImage(scaled))
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(image, 0, 0, scaled.Width, scaled.Height);
                    }
                }
                return scaled;
            }
            catch { if (scaled != null) scaled.Dispose(); return null; }
        }

        public static void Forget(string id)
        {
            foreach (var k in cache.Keys.Where(k => k.StartsWith(id + "|")).ToList())
            {
                if (cache[k] != null) cache[k].Dispose();
                cache.Remove(k);
            }
            foreach (var k in pending.Keys.Where(k => k.StartsWith(id + "|", StringComparison.Ordinal)).ToList()) pending.Remove(k);
        }

        public static void Clear()
        {
            ClearImages();
            queue.Clear();
            pending.Clear();
        }

        static void ClearImages()
        {
            foreach (var v in cache.Values) if (v != null) v.Dispose();
            cache.Clear();
        }
    }
}
