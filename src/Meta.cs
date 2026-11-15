using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyPro
{
    // Management data for items in any tab: favourites, manual order, colour labels, tags, notes, your own names and
    // usage history (recent / most used). One list in Store.Meta, saved to data\meta.json in the background.
    // Scope = tab key ("explorer", "terminals", "agents", "chrome", "uninstall", ...); Id = stable item id (path, URL...).
    static class MetaStore
    {
        static readonly Dictionary<string, ItemMeta> index = new Dictionary<string, ItemMeta>(StringComparer.OrdinalIgnoreCase);
        static int indexedCount = -1;
        public static readonly string[] Colors = { "Red", "Orange", "Yellow", "Green", "Blue", "Purple", "Grey" };

        static string Key(string scope, string id) { return scope + "\u0001" + id; }
        static void Index()
        {
            if (indexedCount == Store.Meta.Count) return;
            index.Clear();
            foreach (var m in Store.Meta) index[Key(m.Scope, m.Id)] = m;
            indexedCount = Store.Meta.Count;
        }

        // Existing record or null.
        public static ItemMeta Get(string scope, string id)
        {
            if (string.IsNullOrEmpty(scope) || string.IsNullOrEmpty(id)) return null;
            Index();
            ItemMeta m; return index.TryGetValue(Key(scope, id), out m) ? m : null;
        }
        // Record, created when missing (call Changed() after editing it).
        public static ItemMeta Edit(string scope, string id)
        {
            var m = Get(scope, id);
            if (m != null) return m;
            m = new ItemMeta { Scope = scope, Id = id, Created = Store.Now() };
            Store.Meta.Add(m); Index();
            return m;
        }
        public static void Changed() { Store.MarkMeta(); Store.RaiseMeta(); }

        public static IEnumerable<ItemMeta> All(string scope) { return Store.Meta.Where(m => string.Equals(m.Scope, scope, StringComparison.OrdinalIgnoreCase)); }
        public static List<ItemMeta> Favorites(string scope) { return All(scope).Where(m => m.Fav).OrderBy(m => m.Order == 0 ? int.MaxValue : m.Order).ThenBy(m => DisplayName(m), StringComparer.CurrentCultureIgnoreCase).ToList(); }
        public static List<ItemMeta> Recent(string scope, int max) { return All(scope).Where(m => m.Used > 0).OrderByDescending(m => m.Used).Take(max).ToList(); }
        public static List<ItemMeta> MostUsed(string scope, int max) { return All(scope).Where(m => m.Uses > 0).OrderByDescending(m => m.Uses).ThenByDescending(m => m.Used).Take(max).ToList(); }

        public static bool IsFav(string scope, string id) { var m = Get(scope, id); return m != null && m.Fav; }
        public static void SetFav(string scope, string id, bool fav)
        {
            var m = fav ? Edit(scope, id) : Get(scope, id);
            if (m == null || m.Fav == fav) return;
            m.Fav = fav;
            if (fav && m.Order == 0) m.Order = All(scope).Where(x => x.Fav).Select(x => x.Order).DefaultIfEmpty(0).Max() + 1;
            Changed();
        }
        public static bool ToggleFav(string scope, string id) { bool v = !IsFav(scope, id); SetFav(scope, id, v); return v; }

        // Records one use (opened, launched, ...): feeds Recent and Most used.
        public static void Touch(string scope, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            var m = Edit(scope, id); m.Uses++; m.Used = Store.Now();
            // Keep history bounded: drop the oldest non-favourite, unlabelled records beyond 2000 per scope.
            var extra = All(scope).Where(x => !x.Fav && x.Color == "" && x.Note == "" && x.Tags == "" && x.Name == "").OrderByDescending(x => x.Used).Skip(2000).ToList();
            foreach (var x in extra) Store.Meta.Remove(x);
            Changed();
        }

        public static void SetColor(string scope, string id, string color) { var m = Edit(scope, id); m.Color = color ?? ""; Changed(); }
        public static void SetNote(string scope, string id, string note) { var m = Edit(scope, id); m.Note = note ?? ""; Changed(); }
        public static void SetTags(string scope, string id, string tags) { var m = Edit(scope, id); m.Tags = tags ?? ""; Changed(); }
        public static void SetName(string scope, string id, string name) { var m = Edit(scope, id); m.Name = name ?? ""; Changed(); }
        public static void Remove(string scope, string id) { var m = Get(scope, id); if (m != null) { Store.Meta.Remove(m); indexedCount = -1; Changed(); } }
        public static void ClearHistory(string scope) { foreach (var m in All(scope).ToList()) { m.Uses = 0; m.Used = 0; if (!m.Fav && m.Color == "" && m.Note == "" && m.Tags == "" && m.Name == "") Store.Meta.Remove(m); } indexedCount = -1; Changed(); }

        // Moves a favourite one place up (-1) or down (+1) in the manual order.
        public static void MoveFav(string scope, string id, int dir)
        {
            var favs = Favorites(scope);
            int i = favs.FindIndex(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
            int j = i + dir;
            if (i < 0 || j < 0 || j >= favs.Count) return;
            var t = favs[i]; favs[i] = favs[j]; favs[j] = t;
            for (int k = 0; k < favs.Count; k++) favs[k].Order = k + 1;
            Changed();
        }

        public static string DisplayName(ItemMeta m)
        {
            if (m == null) return "";
            if (!string.IsNullOrEmpty(m.Name)) return m.Name;
            string id = m.Id.TrimEnd('\\', '/');
            int cut = Math.Max(id.LastIndexOf('\\'), id.LastIndexOf('/'));
            return cut >= 0 && cut < id.Length - 1 ? id.Substring(cut + 1) : m.Id;
        }

        public static Color ColorOf(string name)
        {
            switch ((name ?? "").ToLowerInvariant())
            {
                case "red": return Color.FromArgb(229, 72, 77);
                case "orange": return Color.FromArgb(247, 107, 21);
                case "yellow": return Color.FromArgb(245, 176, 40);
                case "green": return Color.FromArgb(18, 168, 110);
                case "blue": return Color.FromArgb(51, 102, 240);
                case "purple": return Color.FromArgb(139, 92, 246);
                case "grey": case "gray": return Color.FromArgb(120, 128, 140);
                case "": return Color.Empty;
                default: try { return ColorTranslator.FromHtml(name); } catch { return Color.Empty; }
            }
        }

        // Standard "manage" submenu for any item: favourite, colour label, rename, note, tags, forget.
        // onChange runs after any edit (refresh the list).
        public static ToolStripMenuItem Menu(string scope, string id, string defaultName, Action onChange)
        {
            var root = new ToolStripMenuItem("Organize");
            Action done = () => { if (onChange != null) onChange(); };
            bool fav = IsFav(scope, id);
            root.DropDownItems.Add(fav ? "Remove from favorites" : "Add to favorites", null, delegate { SetFav(scope, id, !fav); done(); });
            var colors = new ToolStripMenuItem("Color label");
            var cur = Get(scope, id);
            foreach (var c in Colors)
            {
                string cc = c;
                colors.DropDownItems.Add(new ToolStripMenuItem(c, Ui.Swatch(ColorOf(c), Ui.S(14)), delegate { SetColor(scope, id, cc); done(); }) { Checked = cur != null && cur.Color == c });
            }
            colors.DropDownItems.Add("None", null, delegate { SetColor(scope, id, ""); done(); });
            root.DropDownItems.Add(colors);
            root.DropDownItems.Add("Rename (display name)...", null, delegate
            {
                string v = Ui.Ask("Display name", "Name shown for this item (empty = " + defaultName + "):", cur != null ? cur.Name : "", null);
                if (v != null) { SetName(scope, id, v.Trim()); done(); }
            });
            root.DropDownItems.Add("Note...", null, delegate
            {
                string v = Ui.Ask("Note", "Note for " + defaultName + ":", cur != null ? cur.Note : "", null);
                if (v != null) { SetNote(scope, id, v); done(); }
            });
            root.DropDownItems.Add("Tags...", null, delegate
            {
                string v = Ui.Ask("Tags", "Tags, separated by commas:", cur != null ? cur.Tags : "", null);
                if (v != null) { SetTags(scope, id, v); done(); }
            });
            if (cur != null && cur.Fav)
            {
                root.DropDownItems.Add(new ToolStripSeparator());
                root.DropDownItems.Add("Move up in favorites", null, delegate { MoveFav(scope, id, -1); done(); });
                root.DropDownItems.Add("Move down in favorites", null, delegate { MoveFav(scope, id, 1); done(); });
            }
            if (cur != null)
            {
                root.DropDownItems.Add(new ToolStripSeparator());
                root.DropDownItems.Add("Forget (remove label, note, history)", null, delegate { Remove(scope, id); done(); });
            }
            return root;
        }
    }
}
