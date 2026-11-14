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

    public class Settings
    {
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
        public string OcrLang = "";
        public bool RecordMic;
        public int RecordFps = 30;
        public bool ActivityOn = true;
        public bool ActivityAutoBackupFav;
    }

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

        public static event Action NotesChanged, PromptsChanged, ClipsChanged, CardsChanged, ActivityChanged, TaggedChanged;

        static bool notesDirty, promptsDirty, clipsDirty, settingsDirty, cardsDirty, historyDirty, activityDirty, taggedDirty;
        public static void MarkActivity() { activityDirty = true; Kick(); }
        public static void MarkTagged() { taggedDirty = true; Kick(); }
        public static void RaiseActivity() { var h = ActivityChanged; if (h != null) h(); }
        public static void RaiseTagged() { var h = TaggedChanged; if (h != null) h(); }
        public static void MarkCards() { cardsDirty = true; Kick(); }
        public static void MarkHistory() { historyDirty = true; Kick(); }
        public static void RaiseCards() { var h = CardsChanged; if (h != null) h(); }
        static System.Windows.Forms.Timer saveTimer;
        static readonly HashSet<string> recovered = new HashSet<string>();
        public static bool Pending { get { return notesDirty || promptsDirty || clipsDirty || settingsDirty || cardsDirty || historyDirty || activityDirty || taggedDirty; } }
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
            Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            ImgDir = Path.Combine(Dir, "images");
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
            Notes.RemoveAll(n => n == null);
            Prompts.RemoveAll(p => p == null);
            Clips.RemoveAll(c => c == null || (c.Kind == "image" && (!ValidImageName(c.Image) || !File.Exists(ImagePath(c)))));
            foreach (var n in Notes) { if (n.Id == null) n.Id = NewId(); if (n.Rtf == null) n.Rtf = ""; if (n.Text == null) n.Text = ""; if (float.IsNaN(n.Zoom) || float.IsInfinity(n.Zoom) || n.Zoom < 0.5f || n.Zoom > 4f) n.Zoom = 1f; if (n.Tags == null) n.Tags = ""; if (n.RemindAt < 0 || n.RemindAt > DateTime.MaxValue.Ticks) n.RemindAt = 0; }
            foreach (var p in Prompts) { NormalizePrompt(p); }
            foreach (var c in Clips) { if (c.Id == null) c.Id = NewId(); if (c.Text == null) c.Text = ""; if (c.Source == null) c.Source = ""; if (c.Folder == null) c.Folder = ""; if (c.Label == null) c.Label = ""; }
            if (Settings.Hotkeys == null) Settings.Hotkeys = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(Settings.NoteFontName)) Settings.NoteFontName = "Segoe UI";
            if (float.IsNaN(Settings.NoteFontSize) || float.IsInfinity(Settings.NoteFontSize) || Settings.NoteFontSize < 6 || Settings.NoteFontSize > 48) Settings.NoteFontSize = 11f;

            saveTimer = new System.Windows.Forms.Timer();
            saveTimer.Interval = 700;
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveNow(); };
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

        static void Write(string name, object o)
        {
            string p = Path.Combine(Dir, name);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, Js().Serialize(o), new UTF8Encoding(false));
            // Do not replace a known-good recovery backup with the corrupt primary.
            if (File.Exists(p)) File.Replace(tmp, p, recovered.Contains(name) ? null : p + ".bak", true);
            else File.Move(tmp, p);
            recovered.Remove(name);
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
            if (settingsDirty) settingsDirty = !Safe("settings.json", Settings);
            if (cardsDirty) cardsDirty = !Safe("cards.json", Cards.ToList());
            if (historyDirty) historyDirty = !Safe("history.json", History);
            if (activityDirty) activityDirty = !Safe("activity.json", Activity.ToList());
            if (taggedDirty)
            {
                bool filesOk = Safe("tagged.json", Tagged.ToList());
                bool catsOk = Safe("tagcats.json", TagCats.ToList());
                taggedDirty = !filesOk || !catsOk;
            }
            if (saveTimer != null && (notesDirty || promptsDirty || clipsDirty || settingsDirty || cardsDirty || historyDirty || activityDirty || taggedDirty))
            {
                saveTimer.Interval = 5000;
                saveTimer.Start();
            }
            else if (saveTimer != null) saveTimer.Interval = 700;
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

        public static Clip MakeImageClip(Image img)
        {
            using (var ms = new MemoryStream())
            {
                img.Save(ms, ImageFormat.Png);
                byte[] bytes = ms.ToArray();
                string hash;
                using (var sha = SHA1.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
                var existing = Clips.FirstOrDefault(x => x.Kind == "image" && x.Hash == hash);
                var c = new Clip();
                c.Id = NewId();
                c.Kind = "image";
                c.Hash = hash;
                c.ImgW = img.Width;
                c.ImgH = img.Height;
                c.Text = "Image " + img.Width + " x " + img.Height;
                if (existing != null) c.Image = existing.Image;
                else
                {
                    c.Image = c.Id + ".png";
                    File.WriteAllBytes(Path.Combine(ImgDir, c.Image), bytes);
                }
                return c;
            }
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
            foreach (var line in (text ?? "").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length > 0) return t.Length > 70 ? t.Substring(0, 70) + "..." : t;
            }
            return "";
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
