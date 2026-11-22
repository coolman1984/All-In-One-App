using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StickyPro
{
    // ===================================================================================================
    // Project registry for the agent harness: many projects in many folders, never mixed.
    // The list is built automatically from every folder the app knows (agent chats, folder sets, terminals,
    // recent folders) plus the sibling projects next to them (git repos or folders with CLAUDE.md / AGENTS.md /
    // README.md). Each agent turn gets a short scope block: which project it works on, the project the
    // message names (if different), and the known projects so a name like "the HERDR project" resolves exactly.
    // ===================================================================================================
    class Project
    {
        public string Name = "", Path = "";
        public List<string> Aliases = new List<string>();
        internal Regex Match;
        internal int Rank;   // git repo > has CLAUDE.md/AGENTS.md > local drive: wins when two projects share a name
    }

    static class Projects
    {
        static List<Project> cache; static DateTime cacheTime;
        static readonly object gate = new object();

        public static List<Project> All()
        {
            lock (gate)
            {
                if (cache != null && (DateTime.Now - cacheTime).TotalSeconds < 120) return cache;
                try { cache = Discover(); } catch (Exception ex) { App.Log("Projects: " + ex.Message); if (cache == null) cache = new List<Project>(); }
                cacheTime = DateTime.Now;
                return cache;
            }
        }
        public static void Invalidate() { lock (gate) cache = null; }

        static readonly HashSet<string> Common = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "app", "apps", "src", "lib", "bin", "test", "tests", "docs", "doc", "data", "tools", "tool", "temp", "tmp", "work", "project", "projects",
            "agent", "agents", "note", "notes", "code", "main", "new", "old", "backup", "files", "file", "web", "site", "demo", "scripts", "build", "dist", "skills", "memory" };
        static readonly Regex Junk = new Regex(@"\\(site-packages|node_modules|venv|\.venv|AppData|\$Recycle\.Bin|Downloads|Documents|Desktop)\\", RegexOptions.IgnoreCase);
        static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\'); } }

        static List<Project> Discover()
        {
            // Strong seeds: folders the user works in with agents / terminals / folder sets. Weak: any recently opened folder
            // (Downloads, a shared drive) - those count only when they look like a project.
            var seeds = new List<string>(); var weak = new List<string>();
            try { seeds.AddRange(AgentStore.Chats.Select(c => c.Folder)); } catch { }
            try { foreach (var s in FolderSets.All) seeds.AddRange(s.Folders); } catch { }
            try { foreach (var w in Store.Settings.TermWorkspaces) seeds.AddRange(w.Panes.Select(p => p.Folder)); } catch { }
            try { seeds.AddRange(Store.Settings.TermRecent); } catch { }
            try { weak.AddRange(MetaStore.Recent("folders", 80).Select(m => m.Id)); } catch { }

            var roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // project root -> root
            foreach (var s in seeds.Select(x => new { f = x, strong = true }).Concat(weak.Select(x => new { f = x, strong = false })))
            {
                string r = RootOf(s.f);
                if (r == null || Junk.IsMatch(r + "\\")) continue;
                if (s.strong || LooksLikeProject(r)) roots[r] = r;
            }
            // Sibling projects: a parent that already holds two known projects is a "projects folder"; list all of its projects.
            foreach (var parent in roots.Keys.Select(p => System.IO.Path.GetDirectoryName(p)).Where(p => !string.IsNullOrEmpty(p))
                         .GroupBy(p => p, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2).Select(g => g.Key).ToList())
            {
                try
                {
                    foreach (var d in Directory.GetDirectories(parent).Take(200))
                        if (LooksLikeProject(d) && !Junk.IsMatch(d + "\\")) roots[d.TrimEnd('\\')] = d.TrimEnd('\\');
                }
                catch { }
            }
            // A folder that only holds other projects (GitHub, WORK, a shared drive root) is a container, not a project.
            foreach (var r in roots.Keys.ToList())
                if (!Directory.Exists(System.IO.Path.Combine(r, ".git")) && roots.Keys.Any(o => o.Length > r.Length && o.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase)))
                    roots.Remove(r);

            var list = new List<Project>();
            foreach (var r in roots.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var p = new Project { Path = r, Name = System.IO.Path.GetFileName(r) };
                p.Rank = (Directory.Exists(System.IO.Path.Combine(r, ".git")) ? 4 : 0) + (File.Exists(System.IO.Path.Combine(r, "CLAUDE.md")) || File.Exists(System.IO.Path.Combine(r, "AGENTS.md")) ? 2 : 0) + (r.StartsWith(@"\\") ? 0 : 1);
                p.Aliases.Add(p.Name);
                string remote = RemoteName(r);
                if (remote != null && !p.Aliases.Contains(remote, StringComparer.OrdinalIgnoreCase)) p.Aliases.Add(remote);
                // "Sticky note" also matches "sticky-note", "sticky_note", "stickynote" and "Sticky notes".
                // Everyday words ("docs", "test", "agents") would match ordinary sentences: those projects match only by full path.
                var alts = p.Aliases.Where(a => a.Length >= 3 && !Common.Contains(a.Trim())).Select(a =>
                    string.Join(@"[\s_\-.]*", Regex.Split(a.Trim(), @"[\s_\-.]+").Where(w => w.Length > 0).Select(Regex.Escape)) + "s?").ToList();
                alts.Add(Regex.Escape(r));
                p.Match = new Regex(@"(?<![\w])(" + string.Join("|", alts) + @")(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
                list.Add(p);
            }
            return list;
        }

        // The project a folder belongs to: the nearest folder up with .git (4 levels), else the folder itself.
        // Drive roots and the user's home are never projects.
        public static string RootOf(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return null;
            try
            {
                string f = System.IO.Path.GetFullPath(folder).TrimEnd('\\');
                if (!Directory.Exists(f)) return null;
                string d = f;
                for (int i = 0; i < 4 && d != null; i++)
                {
                    if (Directory.Exists(System.IO.Path.Combine(d, ".git"))) { f = d; break; }
                    d = System.IO.Path.GetDirectoryName(d);
                }
                if (f.Length <= 3 || string.Equals(f, Home, StringComparison.OrdinalIgnoreCase)) return null;
                if (string.Equals(f, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), StringComparison.OrdinalIgnoreCase)) return null;
                if (f.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return null;
                return f;
            }
            catch { return null; }
        }

        static bool LooksLikeProject(string d)
        {
            try
            {
                string n = System.IO.Path.GetFileName(d);
                if (n.StartsWith(".") || n.StartsWith("$")) return false;
                return Directory.Exists(System.IO.Path.Combine(d, ".git")) || File.Exists(System.IO.Path.Combine(d, "CLAUDE.md")) ||
                       File.Exists(System.IO.Path.Combine(d, "AGENTS.md")) || File.Exists(System.IO.Path.Combine(d, "README.md"));
            }
            catch { return false; }
        }

        // GitHub repository name from .git/config (origin url), e.g. "sticky-note".
        static string RemoteName(string root)
        {
            try
            {
                string cfg = System.IO.Path.Combine(root, ".git", "config");
                if (!File.Exists(cfg)) return null;
                var m = Regex.Match(File.ReadAllText(cfg), @"url\s*=\s*\S*?[/:]([^/\s]+?)(\.git)?\s*$", RegexOptions.Multiline);
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        public static Project Of(string folder)
        {
            string r = RootOf(folder);
            if (r == null) return null;
            return All().FirstOrDefault(p => string.Equals(p.Path, r, StringComparison.OrdinalIgnoreCase)) ?? new Project { Path = r, Name = System.IO.Path.GetFileName(r) };
        }

        // Projects the text names, best first (longest match wins: "Excel Agent" over "Agent").
        public static List<Project> Mentioned(string text)
        {
            var hits = new List<KeyValuePair<Project, int>>();
            if (string.IsNullOrWhiteSpace(text)) return new List<Project>();
            if (text.Length > 20000) text = text.Substring(0, 20000);
            foreach (var p in All())
            {
                try { var m = p.Match.Match(text); if (m.Success) hits.Add(new KeyValuePair<Project, int>(p, m.Length)); } catch { }
            }
            // Drop a hit that is only part of a longer hit ("Agent" inside "Excel Agent").
            var ordered = hits.OrderByDescending(h => h.Value).Select(h => h.Key).ToList();
            // Same name in two places ("Sticky note" repo and a shared-drive copy): keep the stronger one, unless the path was written.
            ordered = ordered.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .SelectMany(g => g.Count() == 1 ? g.ToList() : g.Where(p => text.IndexOf(p.Path, StringComparison.OrdinalIgnoreCase) >= 0).DefaultIfEmpty(g.OrderByDescending(p => p.Rank).First()).ToList())
                .ToList();
            return ordered.Where(p => !ordered.Any(q => q != p && q.Name.Length > p.Name.Length && q.Name.IndexOf(p.Name, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }

        // The one project a message is about, or null when none or more than one is named.
        public static Project Target(string text)
        {
            var m = Mentioned(text);
            return m.Count == 1 ? m[0] : null;
        }

        // A new chat whose folder is not a project yet: start it in the project the first message names.
        public static bool AutoFolder(AgentChat c, string text)
        {
            if (c == null || c.Messages.Count > 0 || c.Sessions.Count > 0) return false;
            if (Of(c.Folder) != null && Mentioned(text).Count == 0) return false;
            var t = Target(text);
            if (t == null || string.Equals(RootOf(c.Folder), t.Path, StringComparison.OrdinalIgnoreCase)) return false;
            if (Of(c.Folder) != null && !string.IsNullOrEmpty(c.Folder) && c.Folder.StartsWith(t.Path, StringComparison.OrdinalIgnoreCase)) return false;
            c.Folder = t.Path;
            return true;
        }

        // Scope block prepended to an agent's prompt. Full block on a fresh session (or for agents that cannot resume),
        // and whenever the message names a project other than the chat's folder; nothing on a plain resumed turn.
        public static string ScopeBlock(AgentChat chat, string prompt, bool freshSession)
        {
            try
            {
                var latest = chat.Messages.LastOrDefault(m => m.Role == "user");
                string userText = latest != null ? latest.Text : prompt;
                var here = Of(chat.Folder);
                var named = Mentioned(userText);
                var other = named.FirstOrDefault(p => here == null || !string.Equals(p.Path, here.Path, StringComparison.OrdinalIgnoreCase));
                if (!freshSession && other == null) return "";
                var sb = new StringBuilder("[Project scope - All in One harness]\n");
                sb.Append("Working folder: ").Append(here != null ? here.Name + " - " + here.Path : (string.IsNullOrEmpty(chat.Folder) ? Home : chat.Folder) + " (not a project folder)").Append("\n");
                if (named.Count == 1 && other != null)
                    sb.Append("This message is about project ").Append(other.Name).Append(" - ").Append(other.Path).Append(". Work on that project in that folder (use full paths), not on the working folder's project.\n");
                else if (named.Count > 1)
                    sb.Append("This message names several projects: ").Append(string.Join("; ", named.Select(p => p.Name + " - " + p.Path))).Append(". Keep each one's work in its own folder.\n");
                sb.Append("Rules: work only on the project the user means. Read and change files only inside that project's folder. Follow that project's own CLAUDE.md / AGENTS.md, history and lessons. Never mix files, history, memory, notes or results from a different project unless the user names it. If it is unclear which project is meant, ask one short question first.\n");
                var all = All();
                if (all.Count > 0)
                {
                    sb.Append("Known projects (name - folder):\n");
                    foreach (var p in all.OrderByDescending(x => x.Rank).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(90)) sb.Append("- ").Append(p.Name).Append(p.Aliases.Count > 1 ? " (also: " + string.Join(", ", p.Aliases.Skip(1)) + ")" : "").Append(" - ").Append(p.Path).Append("\n");
                }
                sb.Append("[End of project scope]\n\n");
                return sb.ToString();
            }
            catch (Exception ex) { App.Log("Project scope: " + ex.Message); return ""; }
        }
    }
}
