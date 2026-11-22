using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace StickyPro
{
    // ===================================================================================================
    // One-click update distribution through the team folder.
    //  - The admin's PC holds an RSA-3072 release key in the Windows user key store (never in the share).
    //    team.json carries the public key; every client pins it when it joins.
    //  - A release = releases\<version>\AllInOne.exe + manifest.json + manifest.sig (signed: version, SHA-256, size, notes).
    //  - channels.json + channels.sig (signed, with an increasing sequence number) say which version each channel
    //    gets: dev (the admin's PC), pilot (chosen PCs), stable (everyone, with a rollout percent), and a pause switch.
    //  - Clients check every 2 minutes: verify signatures, download in chunks with resume, verify the checksum,
    //    stage, and install when the app is idle (or at once for a forced release, or when the user clicks).
    //  - Install: the new exe itself is the updater ("--apply-update"): it waits for the old app to exit, keeps a
    //    backup, replaces the exe, starts it and waits for its health signal; no signal = automatic rollback.
    //    Settings and data are never touched (they live outside the exe); a copy of the settings is kept anyway.
    // ===================================================================================================
    public class ReleaseManifest
    {
        public string Version = "", Sha = "", Notes = "", Commit = "", Publisher = "", Channel = "";
        public long Size, Published; public bool Forced;
    }
    public class ReleaseChannels
    {
        public long Seq; public string DevDevice = "", Dev = "", Pilot = "", Stable = "";
        public List<string> PilotDevices = new List<string>();
        public List<string> Forced = new List<string>();
        public int Rollout = 100; public bool Paused; public long Updated; public string By = "";
    }

    static class Releases
    {
        // ---------- keys (Windows key store, provider with SHA-256 support) ----------
        const int ProvRsaAes = 24;
        static CspParameters Csp(TeamClient c, bool existing)
        {
            var p = new CspParameters(ProvRsaAes) { KeyContainerName = c.Local.KeyContainer };
            if (existing) p.Flags = CspProviderFlags.UseExistingKey;
            return p;
        }
        public static string CreateKey(TeamClient c)
        {
            using (var rsa = new RSACryptoServiceProvider(3072, Csp(c, false)) { PersistKeyInCsp = true }) return rsa.ToXmlString(false);
        }
        public static bool HoldsKey(TeamClient c, string publicXml)
        {
            try { using (var rsa = new RSACryptoServiceProvider(Csp(c, true)) { PersistKeyInCsp = true }) return rsa.ToXmlString(false) == publicXml; }
            catch { return false; }
        }
        public static void DeleteKey(TeamClient c)   // tests only
        {
            try { using (var rsa = new RSACryptoServiceProvider(Csp(c, true))) { rsa.PersistKeyInCsp = false; rsa.Clear(); } } catch { }
        }
        static byte[] Sign(TeamClient c, byte[] data)
        {
            using (var rsa = new RSACryptoServiceProvider(Csp(c, true)) { PersistKeyInCsp = true }) return rsa.SignData(data, "SHA256");
        }
        public static bool Verify(string publicXml, byte[] data, byte[] sig)
        {
            try
            {
                using (var rsa = new RSACryptoServiceProvider(new CspParameters(ProvRsaAes)) { PersistKeyInCsp = false })
                {
                    rsa.FromXmlString(publicXml);
                    return rsa.VerifyData(data, "SHA256", sig);
                }
            }
            catch { return false; }
        }

        // Signed JSON: file.json holds the exact bytes, file.sig their signature (base64).
        static void WriteSigned(TeamClient c, string jsonPath, object value)
        {
            var bytes = new UTF8Encoding(false).GetBytes(TeamClient.ToJson(value));
            string sig = Convert.ToBase64String(Sign(c, bytes));
            // Signature first: a reader that sees the new json with the old signature rejects it and simply retries later.
            TeamClient.WriteAtomic(Path.ChangeExtension(jsonPath, ".sig"), sig);
            TeamClient.WriteAtomic(jsonPath, Encoding.UTF8.GetString(bytes));
        }
        static T ReadSigned<T>(TeamClient c, string jsonPath, out string error) where T : class
        {
            error = null;
            try
            {
                if (!File.Exists(jsonPath)) { error = "missing"; return null; }
                var bytes = new UTF8Encoding(false).GetBytes(TeamClient.ReadShared(jsonPath));
                var sig = Convert.FromBase64String(TeamClient.ReadShared(Path.ChangeExtension(jsonPath, ".sig")).Trim());
                if (string.IsNullOrEmpty(c.Local.PinnedKey)) { error = "no pinned release key"; return null; }
                if (!Verify(c.Local.PinnedKey, bytes, sig)) { error = "signature check failed (" + Path.GetFileName(jsonPath) + ")"; return null; }
                return TeamClient.FromJson<T>(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        public static void WriteSignedDoc(TeamClient c, string jsonPath, object value) { WriteSigned(c, jsonPath, value); }
        public static T ReadSignedDoc<T>(TeamClient c, string jsonPath, out string error) where T : class { return ReadSigned<T>(c, jsonPath, out error); }
        public static string ChannelsPath(TeamClient c) { return Path.Combine(c.Root, "channels.json"); }
        public static ReleaseChannels Channels(TeamClient c, out string error)
        {
            var ch = ReadSigned<ReleaseChannels>(c, ChannelsPath(c), out error);
            if (ch == null && error == "missing") { error = null; return new ReleaseChannels(); }
            if (ch != null) { if (ch.PilotDevices == null) ch.PilotDevices = new List<string>(); if (ch.Forced == null) ch.Forced = new List<string>(); }
            return ch;
        }
        public static ReleaseManifest Manifest(TeamClient c, string version, out string error)
        {
            var m = ReadSigned<ReleaseManifest>(c, Path.Combine(c.Root, "releases", version, "manifest.json"), out error);
            if (m != null && m.Version != version) { error = "manifest version mismatch"; return null; }
            return m;
        }
        public static List<ReleaseManifest> History(TeamClient c)
        {
            var list = new List<ReleaseManifest>();
            try
            {
                string dir = Path.Combine(c.Root, "releases");
                if (Directory.Exists(dir))
                    foreach (var d in Directory.GetDirectories(dir)) { string e; var m = Manifest(c, Path.GetFileName(d), out e); if (m != null) list.Add(m); }
            }
            catch { }
            return list.OrderByDescending(m => m.Published).ToList();
        }

        // ---------- admin: publish and channel control ----------
        public static void RequireAdmin(TeamClient c)
        {
            var t = c.Team;
            if (t == null) throw new InvalidOperationException("Not connected to a team folder.");
            if (!t.Admins.Contains(c.Me) || !HoldsKey(c, t.ReleaseKey)) { c.Audit("denied", "admin action by a non-admin device"); throw new UnauthorizedAccessException("Only the team admin's PC can do this."); }
            if (!c.KeyTrusted) throw new InvalidOperationException("The team release key does not match the pinned key.");
            if (!TeamSecurity.Unlocked(c)) { c.Audit("denied", "admin action while admin mode is locked"); throw new UnauthorizedAccessException("Unlock admin mode first (admin passphrase)."); }
        }

        // Changes the signed channel pointers under a lock file (two admin windows can never interleave).
        public static ReleaseChannels ChangeChannels(TeamClient c, Action<ReleaseChannels> change, string what)
        {
            RequireAdmin(c);
            using (Lock(c))
            {
                string err; var ch = Channels(c, out err);
                if (ch == null) throw new InvalidOperationException("channels.json is unreadable: " + err);
                change(ch);
                ch.Seq++; ch.Updated = TeamClient.NowMs(); ch.By = c.Local.Name;
                if (string.IsNullOrEmpty(ch.DevDevice)) ch.DevDevice = c.Me;
                WriteSigned(c, ChannelsPath(c), ch);
                c.Audit("channels", what + " -> seq " + ch.Seq);
                return ch;
            }
        }
        public static IDisposable Lock(TeamClient c)
        {
            string f = Path.Combine(c.Root, "publish.lock");
            for (int i = 0; i < 50; i++)
            {
                try { return new FileStream(f, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); }
                catch (IOException)
                {
                    try { if (File.Exists(f) && DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > TimeSpan.FromMinutes(30)) File.Delete(f); } catch { }
                    Thread.Sleep(200);
                }
            }
            throw new IOException("Another publish is running (publish.lock). Try again in a minute.");
        }

        // Publishes a built exe as a new release on a channel ("dev" or "pilot"; "stable" only by promotion).
        public static ReleaseManifest Publish(TeamClient c, string exe, string notes, string commit, string channel, bool forced, Action<string> status)
        {
            RequireAdmin(c);
            if (channel != "dev" && channel != "pilot") throw new ArgumentException("Publish to dev or pilot; stable is reached only by promoting a tested pilot release.");
            if (!File.Exists(exe)) throw new FileNotFoundException("Built exe not found.", exe);
            string version = DateTime.Now.ToString("yyyy.MM.dd.HHmm");
            string dir = Path.Combine(c.Root, "releases", version);
            for (int n = 2; Directory.Exists(dir); n++) { version = DateTime.Now.ToString("yyyy.MM.dd.HHmm") + "-" + n; dir = Path.Combine(c.Root, "releases", version); }
            if (status != null) status("Copying " + version + " to the team folder...");
            string staging = Path.Combine(c.Root, "releases", "_staging-" + version);
            ReleaseManifest m;
            try
            {
                string sha = TeamClient.CopyResumable(exe, Path.Combine(staging, "AllInOne.exe"), null, null);
                if (!string.Equals(sha, TeamClient.FileSha(exe), StringComparison.OrdinalIgnoreCase)) throw new IOException("Published copy differs from the built exe.");
                m = new ReleaseManifest { Version = version, Sha = sha, Size = new FileInfo(exe).Length, Notes = notes ?? "", Commit = commit ?? "", Publisher = c.Local.Name, Channel = channel, Published = TeamClient.NowMs(), Forced = forced };
                WriteSigned(c, Path.Combine(staging, "manifest.json"), m);
                TeamClient.Retry(() => Directory.Move(staging, dir));   // the release appears complete or not at all
            }
            catch { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } throw; }
            string err; if (Manifest(c, version, out err) == null) throw new IOException("Published release failed its own check: " + err);
            ChangeChannels(c, ch =>
            {
                if (channel == "dev") ch.Dev = version; else { ch.Pilot = version; if (string.IsNullOrEmpty(ch.Dev)) ch.Dev = version; }
                if (forced && !ch.Forced.Contains(version)) ch.Forced.Add(version);
            }, "publish " + version + " to " + channel);
            c.Audit("publish", version + " " + channel + " " + m.Sha);
            c.Post("updates", "release", "Version " + version + " published to " + channel + ".\n\n" + (string.IsNullOrWhiteSpace(notes) ? "(no notes)" : notes), x => { x.Version = version; });
            return m;
        }
        public static void Promote(TeamClient c, string version, int rollout)
        {
            var failed = c.Devices(true).Where(d => d.Target == version && d.State == "failed").Select(d => d.Name).ToList();
            if (failed.Count > 0) throw new InvalidOperationException("Not promoted: " + failed.Count + " pilot PC(s) failed to install " + version + " (" + string.Join(", ", failed) + "). Fix or roll back first.");
            ChangeChannels(c, ch => { ch.Stable = version; ch.Rollout = Math.Max(1, Math.Min(100, rollout)); }, "promote " + version + " to stable " + rollout + "%");
            c.Post("updates", "release", "Version " + version + " is now the stable release (" + rollout + "% of PCs).", x => { x.Version = version; });
        }
        public static string Rollback(TeamClient c, string channel)
        {
            string target = null;
            ChangeChannels(c, ch =>
            {
                string cur = channel == "stable" ? ch.Stable : channel == "pilot" ? ch.Pilot : ch.Dev;
                var older = History(c).Where(m => string.CompareOrdinal(m.Version, cur ?? "") < 0).Select(m => m.Version).FirstOrDefault();
                if (older == null) throw new InvalidOperationException("No older release to roll back to.");
                target = older;
                if (channel == "stable") ch.Stable = older; else if (channel == "pilot") ch.Pilot = older; else ch.Dev = older;
            }, "rollback " + channel);
            c.Post("updates", "release", "Rolled back " + channel + " to version " + target + ".", x => { x.Version = target; });
            return target;
        }

        // ---------- client ----------
        public static string State = "idle", Error = "", Target = "", MySha = "", MyVersion = "";
        public static int Progress;
        public static bool Ready, ForcedReady;
        public static string StagedExe;
        static DateTime lastCheck = DateTime.MinValue;
        static TeamClient client;
        public static event Action StateChanged;
        static void Set(string state, string error = "") { State = state; Error = error ?? ""; var h = StateChanged; if (h != null) try { h(); } catch { } }
        public static string UpdatesDir { get { return Path.Combine(Install.UserDataRoot, "updates"); } }

        public static void Attach(TeamClient c)
        {
            client = c;
            c.FillState = d => { d.Version = MyVersion; d.Sha = MySha; d.State = State; d.Error = Error; d.Progress = Progress; d.Target = Target; d.Channel = MyChannel(c, LastChannels); d.StateTs = TeamClient.NowMs(); return d; };
            c.Tick = () => { if ((DateTime.Now - lastCheck).TotalSeconds >= 120) { lastCheck = DateTime.Now; try { Check(c, System.Windows.Forms.Application.ExecutablePath, UpdatesDir, Install.IsDevCopy); } catch (Exception ex) { Set("failed", ex.Message); } } };
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { MySha = TeamClient.FileSha(System.Windows.Forms.Application.ExecutablePath); } catch { }
                ReportLastResult(c);
                lastCheck = DateTime.Now.AddSeconds(-100);   // first check ~20 s after start
            });
        }
        public static ReleaseChannels LastChannels;
        public static string MyChannel(TeamClient c, ReleaseChannels ch)
        {
            if (ch == null) return "stable";
            if (ch.DevDevice == c.Me) return "dev";
            if (ch.PilotDevices.Contains(c.Me)) return "pilot";
            return "stable";
        }
        static bool InRollout(string deviceId, int percent)
        {
            if (percent >= 100) return true;
            using (var sha = SHA256.Create()) { var b = sha.ComputeHash(Encoding.UTF8.GetBytes(deviceId)); return (b[0] * 256 + b[1]) % 100 < percent; }
        }

        // One update round: returns the state reached. exePath = the running exe; dir = local updates folder.
        public static string Check(TeamClient c, string exePath, string dir, bool devCopy)
        {
            if (!c.Reachable) { Set("offline"); return State; }
            if (!c.KeyTrusted) { Set("failed", "Team release key changed: updates are blocked until the admin confirms."); return State; }
            if (!TeamSecurity.Allowed(c.Policy, c.Me, "updates", c.Me)) { Set("not allowed", "This PC is not approved for team updates."); return State; }
            string err; var ch = Channels(c, out err);
            if (ch == null) { Set("failed", "Release channels rejected: " + err); return State; }
            if (ch.Seq < c.Local.ChannelsSeq) { Set("failed", "Old channel data replayed (seq " + ch.Seq + " < " + c.Local.ChannelsSeq + "): ignored."); return State; }
            if (ch.Seq > c.Local.ChannelsSeq) { c.Local.ChannelsSeq = ch.Seq; c.SaveLocal(); }
            LastChannels = ch;
            string channel = MyChannel(c, ch);
            string version = channel == "dev" ? ch.Dev : channel == "pilot" ? (string.IsNullOrEmpty(ch.Pilot) ? ch.Stable : ch.Pilot) : ch.Stable;
            Target = version ?? "";
            if (string.IsNullOrEmpty(version)) { Set("up to date"); return State; }
            if (devCopy) { Set("dev build"); return State; }   // never overwrite the developer's own build
            bool forced = ch.Forced.Contains(version);
            if (ch.Paused && !forced) { Set("paused"); return State; }
            if (channel == "stable" && !InRollout(c.Me, ch.Rollout) && !forced) { Set("waiting (rollout " + ch.Rollout + "%)"); return State; }
            var m = Manifest(c, version, out err);
            if (m == null) { Set("failed", "Release " + version + " rejected: " + err); return State; }
            if (string.IsNullOrEmpty(MySha)) try { MySha = TeamClient.FileSha(exePath); } catch { }
            if (string.Equals(MySha, m.Sha, StringComparison.OrdinalIgnoreCase)) { MyVersion = version; if (c.Local.InstalledVersion != version) { c.Local.InstalledVersion = version; c.SaveLocal(); } Ready = false; Set("up to date"); return State; }
            if (c.Local.FailedVersions.Contains(version) && !forced) { Set("failed", "Version " + version + " failed to start here and was rolled back."); return State; }
            string staged = Path.Combine(dir, version, "AllInOne.exe");
            if (!File.Exists(staged) || !string.Equals(TeamClient.FileSha(staged), m.Sha, StringComparison.OrdinalIgnoreCase))
            {
                Set("downloading"); Progress = 0;
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                string sha = TeamClient.CopyResumable(Path.Combine(c.Root, "releases", version, "AllInOne.exe"), staged, (d, t) => { int p = t > 0 ? (int)(d * 100 / t) : 0; if (p != Progress) { Progress = p; var h = StateChanged; if (h != null) try { h(); } catch { } } }, null);
                if (!string.Equals(sha, m.Sha, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(staged); } catch { } Set("failed", "Downloaded file does not match the signed checksum: deleted."); return State; }
            }
            Progress = 100; StagedExe = staged; Ready = true; ForcedReady = forced;
            Set("staged");
            return State;
        }

        // Starts the staged exe as the updater and exits this app. Returns false when nothing is staged.
        public static bool InstallNow()
        {
            if (!Ready || string.IsNullOrEmpty(StagedExe) || !File.Exists(StagedExe)) return false;
            try
            {
                Store.SaveNow();
                string backup = Path.Combine(UpdatesDir, "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(backup);
                foreach (var f in Directory.GetFiles(Store.Dir, "*.json")) try { File.Copy(f, Path.Combine(backup, Path.GetFileName(f)), true); } catch { }
                string target = System.Windows.Forms.Application.ExecutablePath;
                Set("installing");
                if (client != null) client.Heartbeat(true);
                Process.Start(new ProcessStartInfo(StagedExe, "--apply-update " + AgentRun.Quote(target) + " " + Process.GetCurrentProcess().Id + " " + AgentRun.Quote(Target)) { UseShellExecute = false, CreateNoWindow = true });
                App.I.Exit();
                return true;
            }
            catch (Exception ex) { Set("failed", "Could not start the update: " + ex.Message); return false; }
        }

        // Idle install: no input for 10 minutes and no agent working. Called from a UI timer.
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        public static int IdleSeconds() { var l = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) }; return GetLastInputInfo(ref l) ? (int)(((uint)Environment.TickCount - l.dwTime) / 1000) : 0; }
        public static void MaybeInstall()
        {
            if (!Ready) return;
            if (ForcedReady || (IdleSeconds() > 600 && AgentStore.Busy.Count == 0)) InstallNow();
        }

        // ---------- the updater (runs from the staged new exe, before any UI) ----------
        public static int ApplyUpdate(string self, string target, int pid, string version, string dir, Func<string, string, Process> launch, int healthMs)
        {
            Directory.CreateDirectory(dir);
            string resultFile = Path.Combine(dir, "result.json");
            Action<bool, string> result = (ok, msg) => { try { TeamClient.WriteAtomic(resultFile, TeamClient.ToJson(new { version = version, ok = ok, error = msg ?? "", ts = TeamClient.NowMs() })); } catch { } };
            try { if (pid > 0) { var p = Process.GetProcessById(pid); p.WaitForExit(60000); } } catch { }
            string backup = Path.Combine(dir, "previous", Path.GetFileName(target));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                Retry(() => File.Copy(target, backup, true));
                Retry(() => File.Copy(self, target, true));
                if (!string.Equals(TeamClient.FileSha(target), TeamClient.FileSha(self), StringComparison.OrdinalIgnoreCase)) throw new IOException("Replaced exe does not match the update.");
            }
            catch (Exception ex)
            {
                try { if (File.Exists(backup)) Retry(() => File.Copy(backup, target, true)); } catch { }
                result(false, "Install failed: " + ex.Message);
                try { launch(target, "--update-failed " + AgentRun.Quote(version)); } catch { }
                return 2;
            }
            string health = Path.Combine(dir, "health-" + version + ".ok");
            try { if (File.Exists(health)) File.Delete(health); } catch { }
            Process started = null;
            try { started = launch(target, "--updated " + AgentRun.Quote(version)); } catch { }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < healthMs && !File.Exists(health))
            {
                if (started != null) { try { if (started.HasExited && !File.Exists(health)) break; } catch { } }
                Thread.Sleep(250);
            }
            if (File.Exists(health)) { result(true, ""); return 0; }
            // No health signal: put the previous version back and start it.
            try { if (started != null && !started.HasExited) started.Kill(); } catch { }
            Thread.Sleep(500);
            try { Retry(() => File.Copy(backup, target, true)); } catch (Exception ex) { result(false, "New version did not start and the rollback failed: " + ex.Message); return 3; }
            result(false, "Version " + version + " did not start correctly; the previous version was restored.");
            try { launch(target, "--update-failed " + AgentRun.Quote(version)); } catch { }
            return 1;
        }
        static void Retry(Action a) { TeamClient.Retry(a); }
        public static Process Launch(string exe, string args) { return Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) }); }

        // The new version is running fine: tell the updater (called ~20 s after start with --updated).
        public static void SignalHealthy(string version)
        {
            try { Directory.CreateDirectory(UpdatesDir); File.WriteAllText(Path.Combine(UpdatesDir, "health-" + version + ".ok"), DateTime.Now.ToString("s")); } catch { }
        }
        static void ReportLastResult(TeamClient c)
        {
            string f = Path.Combine(UpdatesDir, "result.json");
            try
            {
                if (!File.Exists(f)) return;
                var r = TeamClient.FromJson<Dictionary<string, object>>(File.ReadAllText(f));
                File.Delete(f);
                string v = Convert.ToString(r["version"]); bool ok = Convert.ToBoolean(r["ok"]); string e = Convert.ToString(r["error"]);
                if (ok) { c.Local.InstalledVersion = v; MyVersion = v; Set("updated", ""); c.Audit("update-ok", v); }
                else { if (!c.Local.FailedVersions.Contains(v)) c.Local.FailedVersions.Add(v); Target = v; Set("failed", e); c.Audit("update-failed", v + " " + e); }
                c.SaveLocal(); c.Heartbeat(true);
                try { foreach (var d in Directory.GetDirectories(UpdatesDir).Where(x => Path.GetFileName(x) != "previous" && Path.GetFileName(x) != v)) if (!Path.GetFileName(d).StartsWith("backup-")) Directory.Delete(d, true); } catch { }
            }
            catch (Exception ex) { App.Log("Update result: " + ex.Message); }
        }
    }
}
