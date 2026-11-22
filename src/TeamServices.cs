using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace StickyPro
{
    // ===================================================================================================
    // Agents in team rooms. Each person shares the agents they choose (Local.MyAgents); a room message that
    // mentions "@<handle>" makes the OWNER's PC run that agent (their account, their limits) and post the answer.
    // Per-room permission set by the owner: off / ask (read-only, default) / edit (may change files in the room's
    // project folder). Loop guard: at most 6 agent answers in a row without a human message.
    // ===================================================================================================
    static class TeamAgents
    {
        static TeamClient c;
        public static void Attach(TeamClient client) { c = client; c.Incoming += Consider; }
        public static string Mode(TeamClient client, string room) { string m; return client.Local.AgentMode.TryGetValue(room, out m) ? m : "ask"; }

        public static void Consider(TeamMsg m)
        {
            if (c == null || m.Kind != "text" || m.Ts < c.Local.Joined - 60000) return;
            if (!c.Can("agents", "*") || !c.Can("room", "room:" + m.Room) && !c.AdminRole && c.Room(m.Room) != null && c.Room(m.Room).Kind != "dm") return;   // policy: my agents may not work here
            foreach (var agentId in c.Local.MyAgents.ToList())
            {
                string handle = c.Handle(agentId);
                if (m.Agent == handle) continue;
                if (m.Text.IndexOf("@" + handle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                lock (c.Local.Handled)
                {
                    string key = m.Id + "|" + agentId;
                    if (c.Local.Handled.Contains(key)) continue;
                    c.Local.Handled.Add(key);
                    while (c.Local.Handled.Count > 500) c.Local.Handled.RemoveAt(0);
                }
                c.SaveLocal();
                string mode = Mode(c, m.Room);
                if (mode == "off") { c.Post(m.Room, "text", "@" + m.Name + " my agent " + handle + " is turned off in this room.", x => { x.Agent = handle; x.Name = handle + " (" + c.Local.Name + ")"; }); continue; }
                var rows = c.View(m.Room);
                int agentRun = 0; for (int i = rows.Count - 1; i >= 0 && !string.IsNullOrEmpty(rows[i].M.Agent); i--) agentRun++;
                if (agentRun >= 6) { TeamClient.Log("Team agents: loop guard stopped " + handle); continue; }
                string a = agentId, h = handle, room = m.Room;
                ThreadPool.QueueUserWorkItem(delegate { Run(room, a, h, mode); });
            }
        }

        static void Run(string room, string agentId, string handle, string mode)
        {
            var r = c.Room(room);
            string title = c.RoomTitle(r);
            var rows = c.View(room);
            string lastText = rows.Count > 0 ? rows[rows.Count - 1].Text : "";
            string folder = r != null && Directory.Exists(r.Project) ? r.Project : null;
            if (folder == null) { var t = Projects.Target(lastText); if (t != null) folder = t.Path; }
            if (folder == null) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sb = new StringBuilder();
            sb.Append("You are ").Append(handle).Append(", the AI agent of ").Append(c.Local.Name).Append(" in the All in One team room \"").Append(title).Append("\". People and other AI agents talk here; every answer you give is posted to the room.\n");
            if (r != null && r.Kind == "project") sb.Append("This room is about the project in ").Append(r.Project).Append(".\n");
            sb.Append(mode == "edit" ? "You may read and change files in the working folder when asked.\n" : "Read-only: do not change any files; answer, explain, review or plan.\n");
            sb.Append("Recent room messages (oldest first):\n\n");
            foreach (var v in rows.Skip(Math.Max(0, rows.Count - 25)))
            {
                if (v.Deleted) continue;
                string t = v.M.Kind == "file" ? "[shared a file: " + v.M.FileName + " -> " + Path.Combine(c.Root, "files", v.M.FileId, v.M.FileName) + "]" : v.Text;
                if (t.Length > 4000) t = t.Substring(0, 4000) + " [...]";
                sb.Append("[").Append(v.M.Name).Append("] ").Append(t).Append("\n\n");
            }
            sb.Append("Answer the latest message that mentions @").Append(handle).Append(". Keep it short and clear unless asked for detail. To ask another agent, mention its @handle.");

            c.Working = handle + " is working"; c.TypingRoom = room; c.Heartbeat(true);
            string answer; string error = null;
            try { answer = Ask(agentId, folder, mode, title, room, sb.ToString(), out error); }
            catch (Exception ex) { answer = ""; error = ex.Message; }
            finally { c.Working = ""; c.Heartbeat(true); }
            string text = answer.Trim().Length > 0 ? answer.Trim() : "(no answer" + (error != null ? ": " + error : "") + ")";
            c.Post(room, "text", text, x => { x.Agent = handle; x.Name = handle + " (" + c.Local.Name + ")"; });
        }

        // Runs one agent turn in a persistent chat per room and agent (the session continues between mentions).
        static string Ask(string agentId, string folder, string mode, string title, string room, string prompt, out string error)
        {
            error = null;
            if (!AgentStore.Loaded) { var done = new ManualResetEvent(false); App.I.Sync.BeginInvoke(new Action(() => AgentStore.LoadAll(() => done.Set()))); done.WaitOne(30000); }
            AgentChat chat = null; string err = null;
            var texts = new List<string>();
            var exited = new ManualResetEvent(false);
            var run = new AgentRun();
            run.Event += (kind, text) =>
            {
                lock (texts) { if (kind == "text") texts.Add(text); else if (kind == "error") err = text; }
                if (kind == "session") App.I.Sync.BeginInvoke(new Action(() => { if (chat != null) chat.Sessions[run.ActiveSessionKey] = text; }));
            };
            run.Exited += code => exited.Set();
            bool started = (bool)App.I.Sync.Invoke(new Func<bool>(() =>
            {
                string key = room + "|" + agentId, id;
                if (c.Local.AgentChats.TryGetValue(key, out id)) chat = AgentStore.Find(id);
                if (chat == null || !string.Equals(chat.Folder, folder, StringComparison.OrdinalIgnoreCase))
                {
                    chat = AgentStore.New(folder, new[] { agentId });
                    chat.Collection = "Team"; chat.Title = "Team: " + title;
                    c.Local.AgentChats[key] = chat.Id; c.SaveLocal();
                }
                if (AgentStore.Busy.Contains(chat.Id)) { err = "busy with an earlier question"; return false; }
                chat.Mode = mode == "edit" ? "edit" : "ask";
                chat.Messages.Add(new AgentMsg { Role = "user", Text = prompt, Time = Store.Now() });
                AgentStore.Busy.Add(chat.Id);
                var def = AgentStore.Def(agentId);
                if (def == null) { AgentStore.Busy.Remove(chat.Id); err = "unknown agent " + agentId; return false; }
                if (!run.Start(def, chat, AgentStore.PromptFor(chat, agentId), out err)) { AgentStore.Busy.Remove(chat.Id); return false; }
                return true;
            }));
            if (!started) { error = err; return ""; }
            if (!exited.WaitOne(15 * 60 * 1000)) { run.Stop(); error = "stopped after 15 minutes"; exited.WaitOne(5000); }
            string answer; lock (texts) answer = string.Join("\n\n", texts);
            App.I.Sync.BeginInvoke(new Action(() =>
            {
                chat.Messages.Add(new AgentMsg { Role = "agent", Agent = agentId, Text = answer, Time = Store.Now() });
                chat.Seen[run.ActiveSessionKey] = chat.Messages.Count;
                AgentStore.Busy.Remove(chat.Id); AgentStore.Save(chat);
            }));
            if (answer.Length == 0 && err != null) error = err;
            return answer;
        }
    }

    // ===================================================================================================
    // Build & Publish (admin): source checks -> compile check -> team tests (-> full suite) -> release build ->
    // self-test of the built exe -> signed publish. Any failure stops before anything is published.
    // ===================================================================================================
    static class TeamPublish
    {
        public static string Run(string exe, string args, string dir, int timeoutMs, out int code)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = dir, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            using (var p = Process.Start(psi))
            {
                var sb = new StringBuilder();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } code = -1; return sb + "\n(timed out)"; }
                p.WaitForExit();
                code = p.ExitCode; return sb.ToString();
            }
        }
        static string Ps(string script, string args, string dir, int timeoutMs, out int code)
        {
            return Run("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File " + AgentRun.Quote(script) + " " + args, dir, timeoutMs, out code);
        }
        static string Tail(string s, int lines) { var l = (s ?? "").Replace("\r", "").Split('\n').Where(x => x.Trim().Length > 0).ToList(); return string.Join("\n", l.Skip(Math.Max(0, l.Count - lines))); }

        public static string Commit(string dir) { int code; string o = Run("git", "rev-parse --short HEAD", dir, 15000, out code).Trim(); return code == 0 ? o : ""; }
        // Release notes: commit subjects since the last published release (or the last 10).
        public static string DefaultNotes(TeamClient c, string dir)
        {
            var last = Releases.History(c).FirstOrDefault(m => !string.IsNullOrEmpty(m.Commit));
            int code; string range = last != null ? last.Commit + "..HEAD" : "-10";
            string o = Run("git", "log --no-merges --pretty=format:\"- %s\" " + range, dir, 15000, out code);
            if (code != 0 || o.Trim().Length == 0) o = Run("git", "log --no-merges --pretty=format:\"- %s\" -10", dir, 15000, out code);
            var lines = o.Replace("\r", "").Split('\n').Where(x => x.Trim().Length > 0).Take(30).ToList();
            // Problems reported in the team and closed as tasks since then.
            try
            {
                long since = last != null ? last.Published : 0;
                foreach (var t in Store.Tasks.Where(t => t.Done && t.Notes != null && t.Notes.Contains("team-bug:")))
                    if (since == 0 || t.Modified > TeamClient.FromMs(since).Ticks) lines.Add("- Fixed: " + t.Title);
            }
            catch { }
            return lines.Count == 0 ? "- Improvements and fixes." : string.Join("\n", lines);
        }

        public static ReleaseManifest BuildAndPublish(TeamClient c, string source, string channel, string notes, bool fullTests, bool forced, Action<string> say)
        {
            if (!File.Exists(Path.Combine(source, "build.ps1")) || !Directory.Exists(Path.Combine(source, "src"))) throw new InvalidOperationException("Not an All in One source folder (build.ps1 and src are missing): " + source);
            if (!c.IsAdmin) throw new UnauthorizedAccessException("Only the team admin's PC can publish.");
            int code; string o;
            say("1/5 Compile check...");
            o = Ps(Path.Combine(source, "tools", "test-build.ps1"), "", source, 600000, out code);
            if (code != 0) throw new InvalidOperationException("Compile check failed - nothing published.\n" + Tail(o, 15));
            say("2/5 Team and update tests...");
            o = Ps(Path.Combine(source, "tools", "test-team.ps1"), "", source, 900000, out code);
            if (code != 0) throw new InvalidOperationException("Team tests failed - nothing published.\n" + Tail(o, 15));
            o = Ps(Path.Combine(source, "tools", "test-team-security.ps1"), "", source, 900000, out code);
            if (code != 0) throw new InvalidOperationException("Security tests failed - nothing published.\n" + Tail(o, 15));
            if (fullTests)
            {
                say("2/5 Full test suite (slow)...");
                o = Ps(Path.Combine(source, "tools", "test-all.ps1"), "", source, 3600000, out code);
                if (code != 0) throw new InvalidOperationException("Full test suite failed - nothing published.\n" + Tail(o, 20));
            }
            say("3/5 Release build...");
            string outDir = Path.Combine(Path.GetTempPath(), "AllInOne-release-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            string exe = Path.Combine(outDir, "AllInOne.exe");
            Directory.CreateDirectory(outDir);
            o = Ps(Path.Combine(source, "build.ps1"), "-Out " + AgentRun.Quote(exe), source, 900000, out code);
            if (code != 0 || !File.Exists(exe)) throw new InvalidOperationException("Release build failed - nothing published.\n" + Tail(o, 15));
            say("4/5 Self-test of the built app...");
            Run(exe, "--selftest", outDir, 60000, out code);
            if (code != 0) throw new InvalidOperationException("The built app failed its self-test (exit " + code + ") - nothing published.");
            say("5/5 Signing and publishing to " + channel + "...");
            var m = Releases.Publish(c, exe, notes, Commit(source), channel, forced, say);
            try { Directory.Delete(outDir, true); } catch { }
            say("Published " + m.Version + " to " + channel + ".");
            return m;
        }
    }

    // ===================================================================================================
    // Admin commands typed in chat ("/devices", "show computers still using the old version", "rollback pilot"...).
    // Read commands answer at once; changes ask for confirmation first.
    // ===================================================================================================
    static class TeamAdmin
    {
        public const string Help = "Admin commands: /devices (PCs on an old version), /failed, /status, /pilot <version|latest> (publish to pilot), /promote [percent], /pause, /resume, /rollback pilot|stable.";

        // Returns null when the text is not a command; otherwise the answer. confirm(question) asks before changes.
        public static string Handle(TeamClient c, string text, Func<string, bool> confirm)
        {
            string t = text.Trim().ToLowerInvariant();
            bool slash = t.StartsWith("/");
            Func<string[], bool> has = words => words.All(w => t.Contains(w));
            string cmd =
                t.StartsWith("/help") ? "help" :
                t.StartsWith("/devices") || has(new[] { "old version" }) || has(new[] { "not updated" }) ? "devices" :
                t.StartsWith("/failed") || has(new[] { "failed", "install" }) ? "failed" :
                t.StartsWith("/status") || has(new[] { "release", "status" }) ? "status" :
                t.StartsWith("/pilot") || has(new[] { "publish", "pilot" }) ? "pilot" :
                t.StartsWith("/promote") || has(new[] { "promote" }) || has(new[] { "publish", "stable" }) ? "promote" :
                t.StartsWith("/pause") || has(new[] { "pause", "deploy" }) ? "pause" :
                t.StartsWith("/resume") || has(new[] { "resume", "deploy" }) ? "resume" :
                t.StartsWith("/rollback") || has(new[] { "rollback" }) || has(new[] { "roll back" }) ? "rollback" : null;
            if (cmd == null) return slash ? Help : null;
            string err; var ch = Releases.Channels(c, out err);
            if (ch == null) return "Channels unreadable: " + err;
            var devs = c.Devices(true);
            Func<TeamDevice, string> target = d => d.Channel == "dev" ? ch.Dev : d.Channel == "pilot" ? (string.IsNullOrEmpty(ch.Pilot) ? ch.Stable : ch.Pilot) : ch.Stable;
            switch (cmd)
            {
                case "help": return Help;
                case "devices":
                {
                    var old = devs.Where(d => !string.IsNullOrEmpty(target(d)) && d.Version != target(d)).ToList();
                    return old.Count == 0 ? "Every PC runs its channel's version." : "PCs not on their channel's version:\n" + string.Join("\n", old.Select(d => "- " + d.Name + " (" + d.Machine + "): " + (d.Version.Length > 0 ? d.Version : "unknown") + " -> " + target(d) + " · " + d.State + (TeamClient.Online(d) ? "" : " · offline")));
                }
                case "failed":
                {
                    var f = devs.Where(d => d.State == "failed").ToList();
                    return f.Count == 0 ? "No failed installations." : "Failed:\n" + string.Join("\n", f.Select(d => "- " + d.Name + ": " + d.Error));
                }
                case "status":
                    return "dev " + Or(ch.Dev) + " · pilot " + Or(ch.Pilot) + " (" + ch.PilotDevices.Count + " PCs) · stable " + Or(ch.Stable) + " (" + ch.Rollout + "%)" + (ch.Paused ? " · PAUSED" : "") + " · " + devs.Count(TeamClient.Online) + "/" + devs.Count + " PCs online";
            }
            if (!c.IsAdmin) return "Only the team admin can do that.";
            switch (cmd)
            {
                case "pilot":
                {
                    var latest = Releases.History(c).FirstOrDefault();
                    string v = latest != null ? latest.Version : null;
                    var parts = t.Split(' '); if (parts.Length > 1 && parts[1] != "latest" && parts[1].Length > 6) v = parts[1];
                    if (v == null) return "No release to publish.";
                    if (!confirm("Send version " + v + " to the " + ch.PilotDevices.Count + " pilot PC(s)?")) return "Cancelled.";
                    Releases.ChangeChannels(c, x => x.Pilot = v, "pilot " + v);
                    c.Post("updates", "release", "Version " + v + " goes to the pilot PCs.", x => x.Version = v);
                    return "Pilot now gets " + v + ".";
                }
                case "promote":
                {
                    int pct = 100; var m = System.Text.RegularExpressions.Regex.Match(t, @"(\d{1,3})\s*%?"); if (m.Success) pct = Math.Max(1, Math.Min(100, int.Parse(m.Groups[1].Value)));
                    if (string.IsNullOrEmpty(ch.Pilot)) return "No pilot release to promote.";
                    if (!confirm("Make pilot version " + ch.Pilot + " the stable release for " + pct + "% of PCs?")) return "Cancelled.";
                    Releases.Promote(c, ch.Pilot, pct);
                    return "Stable is now " + ch.Pilot + " (" + pct + "%).";
                }
                case "pause":
                    if (!confirm("Pause all update deployment?")) return "Cancelled.";
                    Releases.ChangeChannels(c, x => x.Paused = true, "pause");
                    c.Post("updates", "release", "Update deployment paused by the admin.");
                    return "Deployment paused.";
                case "resume":
                    if (!confirm("Resume update deployment?")) return "Cancelled.";
                    Releases.ChangeChannels(c, x => x.Paused = false, "resume");
                    c.Post("updates", "release", "Update deployment resumed.");
                    return "Deployment resumed.";
                case "rollback":
                {
                    string which = t.Contains("stable") ? "stable" : t.Contains("dev") ? "dev" : "pilot";
                    if (!confirm("Roll the " + which + " channel back to the previous release?")) return "Cancelled.";
                    return "Rolled " + which + " back to " + Releases.Rollback(c, which) + ".";
                }
            }
            return Help;
        }
        static string Or(string v) { return string.IsNullOrEmpty(v) ? "-" : v; }
    }
}
