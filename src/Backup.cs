using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    static class Backup
    {
        public static void Export(IWin32Window owner)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose where to put the backup folder", ShowNewFolderButton = true })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    if (App.I != null) App.I.FlushAll(); else Store.SaveNow();
                    if (!AgentStore.FlushWrites() || Store.Pending) throw new IOException("Pending changes could not be saved. Please retry after checking folder permissions and free space.");
                    string target = Path.Combine(dialog.SelectedPath, "All in One backup " + Stamp());
                    CopyData(Store.Dir, target);
                    MessageBox.Show(owner, "Backup saved to:\n" + target, App.Name);
                }
                catch (Exception ex) { MessageBox.Show(owner, "Backup failed: " + ex.Message, App.Name); }
            }
        }

        public static bool Import(IWin32Window owner)
        {
            using (var dialog = new FolderBrowserDialog { Description = "Choose an All in One backup folder" })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return false;
                try
                {
                    Validate(dialog.SelectedPath);
                    if (MessageBox.Show(owner, "Restore this backup and restart?\n\nYour current data folder will be kept beside it with a .before-restore suffix.", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
                    Stage(dialog.SelectedPath, Store.Dir);
                    App.I.RestartNow();
                    return true;
                }
                catch (Exception ex) { MessageBox.Show(owner, "Restore failed: " + ex.Message, App.Name); return false; }
            }
        }

        static string Stamp() { return DateTime.Now.ToString("yyyy-MM-dd HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8); }
        public static void CopyData(string from, string to) { DataFiles.CopyBackupTree(from, to); }

        // Validate before staging and again at startup: malformed JSON never replaces live data.
        public static void Validate(string folder)
        {
            if (!File.Exists(Path.Combine(folder, "notes.json")) && !File.Exists(Path.Combine(folder, "settings.json")))
                throw new IOException("That folder is not a backup (no notes.json or settings.json).");
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
            foreach (string name in new[] { "notes", "settings", "prompts", "clips", "cards", "history", "activity", "tagged", "tagcats", "tasks", "meta" })
            {
                string path = Path.Combine(folder, name + ".json");
                if (!File.Exists(path)) continue;
                Type type;
                switch (name)
                {
                    case "settings": type = typeof(Settings); break;
                    case "history": type = typeof(SysHistory); break;
                    case "notes": type = typeof(System.Collections.Generic.List<Note>); break;
                    case "prompts": type = typeof(System.Collections.Generic.List<Prompt>); break;
                    case "clips": type = typeof(System.Collections.Generic.List<Clip>); break;
                    case "cards": type = typeof(System.Collections.Generic.List<CardDef>); break;
                    case "activity": type = typeof(System.Collections.Generic.List<ActivityRecord>); break;
                    case "tagged": type = typeof(System.Collections.Generic.List<TaggedFile>); break;
                    case "tagcats": type = typeof(System.Collections.Generic.List<TagCategory>); break;
                    case "tasks": type = typeof(System.Collections.Generic.List<TaskItem>); break;
                    default: type = typeof(System.Collections.Generic.List<ItemMeta>); break;
                }
                try
                {
                    if (serializer.Deserialize(File.ReadAllText(path), type) == null) throw new IOException("Empty data.");
                }
                catch (Exception ex) { throw new IOException("Invalid backup file: " + name + ".json", ex); }
            }
        }

        public static void Stage(string from, string dir)
        {
            DataFiles.CheckSeparate(from, dir);
            if (File.Exists(Store.MoveMarker(dir))) throw new IOException("Finish the pending data-folder move before restoring a backup.");
            string pending = Path.Combine(dir, "restore-pending");
            if (Directory.Exists(pending)) throw new IOException("A restore is already pending. Restart before choosing another backup.");
            string stage = dir.TrimEnd('\\', '/') + ".restore-stage-" + Stamp();
            CopyData(from, stage);
            Validate(stage);
            Directory.Move(stage, pending);
        }

        // Recover a process interruption between the two directory renames.
        static void RecoverSwap(string root)
        {
            string marker = root + ".restore-location";
            if (!File.Exists(marker)) return;
            string previous = Path.GetFullPath(File.ReadAllText(marker));
            if (!previous.StartsWith(root + ".before-restore-", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(previous), Path.GetDirectoryName(root), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Invalid restore recovery location.");
            bool current = File.Exists(Path.Combine(root, "notes.json")) || File.Exists(Path.Combine(root, "settings.json"));
            if (!current && Directory.Exists(previous))
            {
                if (Directory.Exists(root)) Directory.Move(root, root + ".restore-aborted-" + Stamp());
                Directory.Move(previous, root);
            }
            if (!Directory.Exists(root)) throw new IOException("Restore recovery needs the previous data folder: " + previous);
            File.Delete(marker);
        }

        public static void ApplyPending(string dir)
        {
            string root = Path.GetFullPath(dir).TrimEnd('\\', '/');
            string marker = root + ".restore-location";
            RecoverSwap(root);
            string pending = Path.Combine(dir, "restore-pending");
            if (!Directory.Exists(pending)) return;
            string stage = root + ".restore-stage-" + Stamp();
            string previous = root + ".before-restore-" + Stamp();
            bool moved = false;
            try
            {
                if (new DirectoryInfo(root).Parent == null) throw new IOException("Cannot restore into a drive root.");
                Validate(pending);
                CopyData(pending, stage);
                Validate(stage);
                File.WriteAllText(marker, previous);
                Directory.Move(root, previous);
                moved = true;
                try { Directory.Move(stage, root); }
                catch { Directory.Move(previous, root); moved = false; throw; }
                File.Delete(marker);
            }
            catch (Exception ex)
            {
                // Leave the recovery marker if rollback cannot finish; never initialize empty data.
                if (moved && !Directory.Exists(root)) throw new IOException("Restore interrupted. Previous data: " + previous, ex);
                App.Log("Restore failed" + (moved ? "; previous data: " + previous : "") + ": " + ex.Message);
            }
        }
    }
}
