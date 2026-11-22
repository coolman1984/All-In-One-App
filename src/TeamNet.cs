using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace StickyPro
{
    // ===================================================================================================
    // Team: chat, files, presence, agents and app updates between everyone who runs All in One, through one
    // shared network folder (no ports, no server, works behind the corporate firewall).
    //
    // Shared folder layout (Root = the team folder, for example S:\Public_Folder\AllInOneTeam):
    //   team.json                         team id, name, release public key, admin device ids
    //   devices\<device>.json             presence + update state, written ONLY by that device
    //   rooms\<room>\room.json            room name, kind (general / updates / group / dm / project), project folder
    //   rooms\<room>\msgs\<device>.jsonl  append-only messages, written ONLY by that device (no write conflicts)
    //   rooms\<room>\reads\<device>.json  read receipt of that device
    //   files\<file id>\<name>            shared files (copied in chunks, resumable, SHA-256 checked)
    //   releases\<version>\...            signed app releases (see Releases.cs)
    //   channels.json / channels.sig      signed release channel pointers (dev / pilot / stable)
    //   audit\<device>.jsonl              audit log of that device
    // Every device writes only its own files, so many PCs never corrupt shared data. Messages carry unique ids
    // (no duplicates when a write is retried) and are ordered by time, device and sequence. Messages written
    // while the share is unreachable wait in a local outbox and are sent when it comes back.
    // ===================================================================================================
    public class TeamMsg
    {
        public string Id = "", Room = "", From = "", Name = "", Kind = "text", Text = "", ReplyTo = "", Target = "", Agent = "";
        public string FileId = "", FileName = "", Sha = "", Version = "";
        public long Ts, Size; public int Seq, Epoch;
    }
    // On disk only the routing header is readable; everything else is sealed with the room key and signed by the sender.
    public class TeamWire { public string Id = "", Room = "", From = "", Body = "", Sig = ""; public long Ts; public int Seq, Epoch; }
    public class TeamBody
    {
        public string Name = "", Kind = "text", Text = "", ReplyTo = "", Target = "", Agent = "", FileId = "", FileName = "", Sha = "", Version = "";
        public long Size;
    }
    public class TeamRoom { public string Id = "", Name = "", Kind = "group", Project = "", Creator = ""; public List<string> Members = new List<string>(); public long Created; }
    public class TeamAgent { public string Handle = "", Agent = "", Owner = ""; }
    public class TeamDevice
    {
        public string Id = "", Name = "", User = "", Machine = "", Version = "", Sha = "", Channel = "", State = "", Error = "", Target = "", TypingRoom = "", Working = "", AppPath = "";
        public int Progress; public long LastSeen, TypingTs, StateTs; public bool Admin;
        public List<TeamAgent> Agents = new List<TeamAgent>();
    }
    public class TeamInfo { public string TeamId = "", Name = "", ReleaseKey = ""; public long Created; public List<string> Admins = new List<string>(); }
    public class TeamUpload { public string Id = "", Path = "", Room = "", EncPath = ""; public int Epoch; }
    public class TeamLocal
    {
        public string DeviceId = "", Share = "", Name = "", PinnedKey = "", TeamId = "", KeyContainer = "AllInOneTeamRelease", DeviceKeyContainer = "";
        public int Seq, HandledTerminate; public long ChannelsSeq, Joined, PolicySeq;
        public List<TeamMsg> Outbox = new List<TeamMsg>();
        public List<TeamUpload> Uploads = new List<TeamUpload>();
        public Dictionary<string, long> ReadTs = new Dictionary<string, long>();
        public Dictionary<string, string> AgentMode = new Dictionary<string, string>();   // room -> off / ask / edit (default ask)
        public Dictionary<string, string> AgentChats = new Dictionary<string, string>();  // room|agent -> agent chat id (keeps the session)
        public List<string> MyAgents = new List<string>();                                 // agents I share with the team
        public List<string> Handled = new List<string>();                                  // messages already routed to my agents
        public List<string> FailedVersions = new List<string>();                           // updates that failed and were rolled back
        public string InstalledVersion = "";

        internal TeamLocal WithMembership(TeamInfo team, string folder, string name, long joined)
        {
            var next = (TeamLocal)MemberwiseClone();
            if (next.TeamId != team.TeamId) { next.PolicySeq = 0; next.ChannelsSeq = 0; next.HandledTerminate = 0; }
            next.Share = folder;
            next.PinnedKey = team.ReleaseKey;
            next.TeamId = team.TeamId;
            next.Name = name;
            next.Joined = joined;
            return next;
        }
    }

    // One view row of a room: the message after edits, deletes, reactions and pins are applied.
    public class TeamView
    {
        public TeamMsg M; public string Text = ""; public bool Deleted, Edited, Pinned, Pending;
        public Dictionary<string, List<string>> Reactions = new Dictionary<string, List<string>>();
        public TeamMsg Reply;
    }

    public class TeamClient
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 32 };
        public static string ToJson(object o) { return Js.Serialize(o); }
        public static T FromJson<T>(string s) { return Js.Deserialize<T>(s); }

        public readonly string LocalDir;
        public TeamLocal Local = new TeamLocal();
        readonly object gate = new object();
        public event Action Changed;                 // rooms, messages, devices or state changed (any thread)
        public event Action<TeamMsg> Incoming;       // a new message from someone else (any thread)

        public TeamClient(string localDir)
        {
            LocalDir = localDir;
            Directory.CreateDirectory(localDir);
            string f = Path.Combine(localDir, "local.json");
            try { if (File.Exists(f)) Local = FromJson<TeamLocal>(File.ReadAllText(f)) ?? new TeamLocal(); } catch (Exception ex) { Log("Team local state: " + ex.Message); }
            if (string.IsNullOrEmpty(Local.DeviceId)) Local.DeviceId = Environment.MachineName.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            if (string.IsNullOrEmpty(Local.Name)) Local.Name = Environment.UserName;
            if (string.IsNullOrEmpty(Local.DeviceKeyContainer)) Local.DeviceKeyContainer = "AIOTeamDevice-" + Local.DeviceId;
            FixNulls();
            SaveLocal();
        }
        void FixNulls()
        {
            if (Local.Outbox == null) Local.Outbox = new List<TeamMsg>();
            if (Local.Uploads == null) Local.Uploads = new List<TeamUpload>();
            if (Local.ReadTs == null) Local.ReadTs = new Dictionary<string, long>();
            if (Local.AgentMode == null) Local.AgentMode = new Dictionary<string, string>();
            if (Local.AgentChats == null) Local.AgentChats = new Dictionary<string, string>();
            if (Local.MyAgents == null) Local.MyAgents = new List<string>();
            if (Local.Handled == null) Local.Handled = new List<string>();
            if (Local.FailedVersions == null) Local.FailedVersions = new List<string>();
        }
        public void SaveLocal() { lock (gate) WriteAtomic(Path.Combine(LocalDir, "local.json"), ToJson(Local)); }
        public static void Log(string s) { try { App.Log(s); } catch { } }
        public void Raise() { var h = Changed; if (h != null) try { h(); } catch (Exception ex) { Log("Team event: " + ex.Message); } }

        public string Me { get { return Local.DeviceId; } }

        // ---------- security state ----------
        public readonly Dictionary<string, byte[]> KeyCache = new Dictionary<string, byte[]>();
        public DateTime AdminUntil = DateTime.MinValue;
        TeamPolicyDoc policy; DateTime policyAt; long policyStamp; public string PolicyError = "";
        public event Action<string> Terminated;   // the admin ended this PC's session ("terminated" / "revoked" / "blocked")
        // The verified policy (re-read when the file changes, at most every 2 s).
        public TeamPolicyDoc Policy
        {
            get
            {
                if (!Reachable) return policy;
                if ((DateTime.Now - policyAt).TotalSeconds < 2 && policy != null) return policy;
                policyAt = DateTime.Now;
                try
                {
                    var fi = new FileInfo(TeamSecurity.PolicyPath(this));
                    long stamp = fi.Exists ? fi.LastWriteTimeUtc.Ticks ^ fi.Length : 0;
                    if (stamp == policyStamp && policy != null) return policy;
                    string err; var p = TeamSecurity.Load(this, out err);
                    if (p != null) { policy = p; policyStamp = stamp; PolicyError = ""; } else PolicyError = err ?? "";
                }
                catch (Exception ex) { PolicyError = ex.Message; }
                return policy;
            }
        }
        public void SetPolicy(TeamPolicyDoc p) { policy = p; policyAt = DateTime.Now; policyStamp = 0; }
        public TeamPolicyDoc LastGoodPolicy { get { return policy; } }
        public void RefreshPolicy() { policyAt = DateTime.MinValue; policyStamp = -1; var p = Policy; }
        public string MyStatus { get { var m = TeamSecurity.Member(Policy, Me); return m == null ? "pending" : m.Status; } }
        public bool Approved { get { return MyStatus == "approved"; } }
        public bool AdminRole { get { return TeamSecurity.IsAdminMember(Policy, Me); } }
        public bool Can(string feature, string target) { return TeamSecurity.Allowed(Policy, Me, feature, target); }
        public string DeviceCode { get { return TeamSecurity.Fingerprint(TeamSecurity.EnsureDeviceKey(this)); } }
        public string Root { get { return Local.Share ?? ""; } }
        public bool Configured { get { return !string.IsNullOrEmpty(Local.Share); } }
        public bool Reachable { get { try { return Configured && File.Exists(Path.Combine(Root, "team.json")); } catch { return false; } } }
        public static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        public static DateTime FromMs(long ms) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime(); }

        // ---------- files: atomic write, shared read ----------
        public static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            for (int i = 0; ; i++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(tmp, path, null, true); else File.Move(tmp, path);
                    return;
                }
                catch (Exception ex)
                {
                    bool retry = i < 8 && (ex is IOException || ex is UnauthorizedAccessException);
                    if (!retry) { try { File.Delete(tmp); } catch { } throw; }
                    Thread.Sleep(120);
                }
            }
        }
        public static string ReadShared(string path)
        {
            for (int i = 0; ; i++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8)) return r.ReadToEnd();
                }
                catch (IOException)
                {
                    if (i >= 5 || !File.Exists(path)) throw;
                    Thread.Sleep(100);
                }
            }
        }
        public static T ReadJson<T>(string path) where T : class
        {
            try { return File.Exists(path) ? FromJson<T>(ReadShared(path)) : null; } catch (Exception ex) { Log("Team read " + Path.GetFileName(path) + ": " + ex.Message); return null; }
        }
        static string SafeId(string s) { var sb = new StringBuilder(); foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? char.ToLowerInvariant(c) : '-'); return sb.ToString().Trim('-'); }

        // ---------- team: create / join ----------
        public TeamInfo Team { get { return Reachable ? ReadJson<TeamInfo>(Path.Combine(Root, "team.json")) : null; } }
        public bool IsAdmin { get { var t = Team; return t != null && t.Admins.Contains(Me) && Releases.HoldsKey(this, t.ReleaseKey); } }

        public void Create(string folder, string teamName, string adminPassphrase)
        {
            folder = Path.GetFullPath(folder.Trim());
            if (File.Exists(Path.Combine(folder, "team.json"))) throw new InvalidOperationException("A team already exists in this folder. Use Join.");
            TeamSecurity.SetPassphrase(this, adminPassphrase);
            foreach (var d in new[] { "devices", "rooms", "files", "releases", "audit", "joins", "keys", "sessions" }) Directory.CreateDirectory(Path.Combine(folder, d));
            string pub = Releases.CreateKey(this);
            string devicePub = TeamSecurity.EnsureDeviceKey(this);
            var t = new TeamInfo { TeamId = Guid.NewGuid().ToString("N"), Name = string.IsNullOrWhiteSpace(teamName) ? "All in One team" : teamName.Trim(), ReleaseKey = pub, Created = NowMs() };
            t.Admins.Add(Me);
            Local.Share = folder; Local.PinnedKey = pub; Local.TeamId = t.TeamId; Local.Joined = NowMs(); Local.PolicySeq = 0; SaveLocal();
            var p = TeamSecurity.InitialPolicy(this, devicePub);
            Releases.WriteSignedDoc(this, TeamSecurity.PolicyPath(this), p);
            WriteAtomic(Path.Combine(folder, "team.json"), ToJson(t));
            SetPolicy(p); Local.PolicySeq = p.Seq; SaveLocal();
            EnsureRoom("general", "General", "general", "");
            EnsureRoom("updates", "Updates & Releases", "updates", "");
            Audit("team-create", t.Name);
            Heartbeat(true);
            Raise();
        }

        public void Join(string folder) { Join(folder, Local.Name); }

        internal void Join(string folder, string name)
        {
            folder = Path.GetFullPath(folder.Trim());
            var t = ReadJson<TeamInfo>(Path.Combine(folder, "team.json"));
            if (t == null) throw new InvalidOperationException("No team in this folder (team.json is missing). Ask the admin for the team folder, or create a team.");
            if (!string.IsNullOrEmpty(Local.PinnedKey) && Local.TeamId == t.TeamId && Local.PinnedKey != t.ReleaseKey)
                throw new InvalidOperationException("The team's release key changed since you joined. This can mean tampering; ask the admin before joining again.");
            string pub = TeamSecurity.EnsureDeviceKey(this);
            long joined = NowMs();
            string data = ToJson(new TeamJoin { Id = Me, Name = name, Machine = Environment.MachineName, User = Environment.UserName, PublicKey = pub, Ts = joined });
            WriteAtomic(Path.Combine(folder, "joins", SafeId(Me) + ".json"), ToJson(new Dictionary<string, object> { { "data", data }, { "sig", TeamSecurity.SignDevice(this, data) } }));

            // A failed share or local write must leave the previous membership active.
            lock (gate)
            {
                var next = Local.WithMembership(t, folder, name, joined);
                WriteAtomic(Path.Combine(LocalDir, "local.json"), ToJson(next));
                Local = next;
            }
            Audit("join-request", name + " " + TeamSecurity.Fingerprint(pub));
            Raise();
        }
        public void Leave() { Local.Share = ""; SaveLocal(); Raise(); }

        // The release key in team.json must still be the one pinned at join time.
        public bool KeyTrusted { get { var t = Team; return t != null && !string.IsNullOrEmpty(Local.PinnedKey) && t.ReleaseKey == Local.PinnedKey; } }

        public void Audit(string action, string detail)
        {
            try
            {
                string f = Path.Combine(Root, "audit", SafeId(Me) + ".jsonl");
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.AppendAllText(f, ToJson(new { ts = NowMs(), device = Me, name = Local.Name, user = Environment.UserName, action = action, detail = detail }) + "\n");
            }
            catch (Exception ex) { Log("Team audit: " + ex.Message); }
        }

        // ---------- rooms ----------
        List<TeamRoom> rooms = new List<TeamRoom>(); DateTime roomsAt;
        // Rooms this PC may use (the policy decides; the admin sees all).
        public List<TeamRoom> Rooms(bool refresh = false)
        {
            var p = Policy;
            if (p == null || !Approved) return new List<TeamRoom>();
            return AllRooms(refresh).Where(r => AdminRole || TeamSecurity.RoomMembers(p, r).Contains(Me)).ToList();
        }
        public List<TeamRoom> AllRooms(bool refresh = false)
        {
            if (!refresh && (DateTime.Now - roomsAt).TotalSeconds < 5) return rooms;
            var list = new List<TeamRoom>();
            try
            {
                string dir = Path.Combine(Root, "rooms");
                if (Directory.Exists(dir))
                    foreach (var d in Directory.GetDirectories(dir))
                    {
                        var r = ReadJson<TeamRoom>(Path.Combine(d, "room.json"));
                        if (r == null) continue;
                        if (r.Members == null) r.Members = new List<string>();
                        list.Add(r);
                    }
            }
            catch (Exception ex) { Log("Team rooms: " + ex.Message); }
            Func<TeamRoom, int> order = r => r.Kind == "general" ? 0 : r.Kind == "updates" ? 1 : r.Kind == "project" ? 2 : r.Kind == "group" ? 3 : 4;
            rooms = list.OrderBy(order).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList(); roomsAt = DateTime.Now;
            return rooms;
        }
        public TeamRoom Room(string id) { return AllRooms().FirstOrDefault(r => r.Id == id) ?? AllRooms(true).FirstOrDefault(r => r.Id == id); }

        public TeamRoom EnsureRoom(string id, string name, string kind, string project, IEnumerable<string> members = null)
        {
            string f = Path.Combine(Root, "rooms", id, "room.json");
            var r = ReadJson<TeamRoom>(f);
            if (r != null) return r;
            r = new TeamRoom { Id = id, Name = name, Kind = kind, Project = project ?? "", Creator = Me, Created = NowMs() };
            if (members != null) r.Members.AddRange(members.Distinct());
            Directory.CreateDirectory(Path.Combine(Root, "rooms", id, "msgs"));
            WriteAtomic(f, ToJson(r));
            Audit("room-create", id + " " + name);
            AllRooms(true);
            TeamSecurity.SyncKeys(this, id, false);   // keys for everyone the policy allows in
            Raise();
            return r;
        }
        public TeamRoom NewGroup(string name, string project)
        {
            if (!string.IsNullOrEmpty(project) && !Can("project", "project:" + project)) throw new UnauthorizedAccessException("You have no access to this project.");
            string id = (string.IsNullOrEmpty(project) ? "g-" : "p-") + SafeId(name) + "-" + Guid.NewGuid().ToString("N").Substring(0, 5);
            return EnsureRoom(id, name.Trim(), string.IsNullOrEmpty(project) ? "group" : "project", project);
        }
        public TeamRoom DirectWith(TeamDevice other)
        {
            if (!Can("dm", other.Id)) throw new UnauthorizedAccessException("You are not allowed to message " + other.Name + ".");
            var ids = new[] { Me, other.Id }.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            return EnsureRoom("dm-" + SafeId(ids[0]) + "--" + SafeId(ids[1]), "", "dm", "", ids);
        }
        public string RoomTitle(TeamRoom r)
        {
            if (r == null) return "";
            if (r.Kind != "dm") return r.Name;
            string other = r.Members.FirstOrDefault(m => m != Me) ?? Me;
            var d = Devices().FirstOrDefault(x => x.Id == other);
            return d != null ? d.Name : other;
        }

        // ---------- messages ----------
        class RoomCache { public Dictionary<string, long> Offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); public Dictionary<string, TeamMsg> ById = new Dictionary<string, TeamMsg>(); public List<TeamMsg> Sorted; }
        readonly Dictionary<string, RoomCache> cache = new Dictionary<string, RoomCache>();

        public TeamMsg Post(string room, string kind, string text, Action<TeamMsg> fill = null)
        {
            TeamMsg m;
            lock (gate)
            {
                m = new TeamMsg { Id = Guid.NewGuid().ToString("N"), Room = room, From = Me, Name = Local.Name, Kind = kind, Text = text ?? "", Ts = NowMs(), Seq = ++Local.Seq };
                if (fill != null) fill(m);
                Local.Outbox.Add(m);
                SaveLocal();
            }
            Flush();
            Raise();
            return m;
        }

        // Sends the outbox. A message stays queued (on disk) until its append to the share succeeded.
        public string LastSendError = "";
        static string WireText(TeamWire w) { return w.Id + "|" + w.Room + "|" + w.From + "|" + w.Ts + "|" + w.Seq + "|" + w.Epoch + "|" + w.Body; }
        public int Flush()
        {
            if (!Reachable || !Approved) return 0;
            int sent = 0;
            List<TeamMsg> pending; lock (gate) pending = Local.Outbox.ToList();
            foreach (var m in pending)
            {
                try
                {
                    // Sealed with the newest room key this PC holds; no key = no access (the message waits).
                    int epoch = TeamSecurity.LatestEpoch(this, m.Room);
                    byte[] key = epoch > 0 ? TeamSecurity.RoomKey(this, m.Room, epoch) : null;
                    if (key == null) { TeamSecurity.SyncKeys(this, m.Room, false); epoch = TeamSecurity.LatestEpoch(this, m.Room); key = epoch > 0 ? TeamSecurity.RoomKey(this, m.Room, epoch) : null; }
                    if (key == null) { LastSendError = "No access to room " + m.Room + " (waiting for the admin)."; continue; }
                    var body = new TeamBody { Name = m.Name, Kind = m.Kind, Text = m.Text, ReplyTo = m.ReplyTo, Target = m.Target, Agent = m.Agent, FileId = m.FileId, FileName = m.FileName, Sha = m.Sha, Version = m.Version, Size = m.Size };
                    var w = new TeamWire { Id = m.Id, Room = m.Room, From = Me, Ts = m.Ts, Seq = m.Seq, Epoch = epoch, Body = TeamSecurity.SealText(key, ToJson(body)) };
                    w.Sig = TeamSecurity.SignDevice(this, WireText(w));
                    string dir = Path.Combine(Root, "rooms", m.Room, "msgs");
                    Directory.CreateDirectory(dir);
                    using (var fs = new FileStream(Path.Combine(dir, SafeId(Me) + ".jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                    {
                        var b = new UTF8Encoding(false).GetBytes(ToJson(w) + "\n");
                        fs.Write(b, 0, b.Length); fs.Flush(true);
                    }
                    LastSendError = "";
                    lock (gate) Local.Outbox.RemoveAll(x => x.Id == m.Id);
                    sent++;
                }
                catch (Exception ex) { Log("Team send (queued): " + ex.Message); break; }
            }
            if (sent > 0) SaveLocal();
            return sent;
        }

        // Reads only what was appended since the last read. Returns the new messages.
        public List<TeamMsg> Poll(string room)
        {
            var fresh = new List<TeamMsg>();
            RoomCache c;
            lock (gate) { if (!cache.TryGetValue(room, out c)) cache[room] = c = new RoomCache(); }
            string dir = Path.Combine(Root, "rooms", room, "msgs");
            try
            {
                if (!Directory.Exists(dir)) return fresh;
                foreach (var f in Directory.GetFiles(dir, "*.jsonl"))
                {
                    long off; lock (gate) c.Offsets.TryGetValue(f, out off);
                    long len = new FileInfo(f).Length;
                    if (len < off) off = 0;   // file replaced: read again (ids prevent duplicates)
                    if (len == off) continue;
                    byte[] buf;
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(off, SeekOrigin.Begin);
                        buf = new byte[len - off]; int read = 0;
                        while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
                        if (read < buf.Length) Array.Resize(ref buf, read);
                    }
                    int last = Array.LastIndexOf(buf, (byte)'\n');
                    if (last < 0) continue;   // a line still being written: read it next time
                    string text = Encoding.UTF8.GetString(buf, 0, last + 1);
                    bool retry = false;
                    foreach (var line in text.Split('\n'))
                    {
                        if (line.Trim().Length == 0) continue;
                        TeamWire w; try { w = FromJson<TeamWire>(line); } catch { continue; }
                        if (w == null || string.IsNullOrEmpty(w.Id)) continue;
                        lock (gate) { if (c.ById.ContainsKey(w.Id)) continue; }
                        string why; var m = Accept(w, room, out why);
                        if (m == null) { if (why == "nokey") retry = true; else { Dropped++; LastDrop = why; } continue; }
                        lock (gate) { if (c.ById.ContainsKey(m.Id)) continue; c.ById[m.Id] = m; c.Sorted = null; }
                        fresh.Add(m);
                    }
                    if (!retry) lock (gate) c.Offsets[f] = off + last + 1;   // a key that is not here yet: read the lines again later
                }
            }
            catch (Exception ex) { Log("Team poll " + room + ": " + ex.Message); }
            return fresh;
        }

        public int Dropped; public string LastDrop = "";
        // A message counts only when: the sender is an approved member, the signature matches the sender's approved
        // key, the sender may use this room, and it decrypts with the room key.
        TeamMsg Accept(TeamWire w, string room, out string why)
        {
            why = null;
            var p = Policy;
            if (w.Room != room) { why = "wrong room"; return null; }
            var sender = TeamSecurity.Member(p, w.From);
            if (sender == null) { why = "unknown sender " + w.From; return null; }
            if (!TeamSecurity.VerifyDevice(sender.PublicKey, WireText(w), w.Sig)) { why = "bad signature from " + w.From; return null; }
            var r = Room(room);
            if (!TeamSecurity.IsAdminMember(p, w.From) && !TeamSecurity.RoomMembers(p, r).Contains(w.From)) { why = "sender not allowed in room"; return null; }
            byte[] key = TeamSecurity.RoomKey(this, room, w.Epoch);
            if (key == null) { why = "nokey"; return null; }
            TeamBody b;
            try { b = FromJson<TeamBody>(TeamSecurity.OpenText(key, w.Body)); } catch { why = "cannot decrypt"; return null; }
            if (b.Kind == "file" && !TeamSecurity.Allowed(p, w.From, "files", "*")) { why = "sender may not share files"; return null; }
            if (!string.IsNullOrEmpty(b.Agent) && !TeamSecurity.Allowed(p, w.From, "agents", "*")) { why = "sender may not use agents"; return null; }
            return new TeamMsg { Id = w.Id, Room = w.Room, From = w.From, Ts = w.Ts, Seq = w.Seq, Epoch = w.Epoch, Name = sender.Name.Length > 0 && string.IsNullOrEmpty(b.Agent) ? sender.Name : b.Name, Kind = b.Kind, Text = b.Text ?? "", ReplyTo = b.ReplyTo ?? "", Target = b.Target ?? "", Agent = b.Agent ?? "", FileId = b.FileId ?? "", FileName = b.FileName ?? "", Sha = b.Sha ?? "", Version = b.Version ?? "", Size = b.Size };
        }

        // All messages of a room in order: shared ones plus my queued (pending) ones.
        public List<TeamMsg> Messages(string room, out HashSet<string> pending)
        {
            RoomCache c; pending = new HashSet<string>();
            lock (gate)
            {
                if (!cache.TryGetValue(room, out c)) cache[room] = c = new RoomCache();
                if (c.Sorted == null) c.Sorted = c.ById.Values.OrderBy(m => m.Ts).ThenBy(m => m.From, StringComparer.Ordinal).ThenBy(m => m.Seq).ToList();
                var list = c.Sorted.ToList();
                foreach (var m in Local.Outbox.Where(x => x.Room == room && !c.ById.ContainsKey(x.Id))) { list.Add(m); pending.Add(m.Id); }
                return list;
            }
        }

        static readonly HashSet<string> Events = new HashSet<string> { "reaction", "edit", "delete", "pin", "unpin" };
        public List<TeamView> View(string room)
        {
            HashSet<string> pending;
            var all = Messages(room, out pending);
            var rows = new List<TeamView>(); var byId = new Dictionary<string, TeamView>();
            foreach (var m in all.Where(x => !Events.Contains(x.Kind)))
            {
                var v = new TeamView { M = m, Text = m.Text, Pending = pending.Contains(m.Id) };
                rows.Add(v); byId[m.Id] = v;
            }
            foreach (var e in all.Where(x => Events.Contains(x.Kind)))
            {
                TeamView v; if (!byId.TryGetValue(e.Target, out v)) continue;
                if (e.Kind == "reaction")
                {
                    List<string> who; if (!v.Reactions.TryGetValue(e.Text, out who)) v.Reactions[e.Text] = who = new List<string>();
                    if (who.Contains(e.Name)) who.Remove(e.Name); else who.Add(e.Name);   // same reaction again = remove
                    if (who.Count == 0) v.Reactions.Remove(e.Text);
                }
                else if (e.From != v.M.From && (e.Kind == "edit" || e.Kind == "delete")) continue;   // only the author edits or deletes
                else if (e.Kind == "edit") { v.Text = e.Text; v.Edited = true; }
                else if (e.Kind == "delete") { v.Deleted = true; v.Text = ""; }
                else if (e.Kind == "pin") v.Pinned = true;
                else if (e.Kind == "unpin") v.Pinned = false;
            }
            foreach (var v in rows) if (!string.IsNullOrEmpty(v.M.ReplyTo)) { TeamView r; if (byId.TryGetValue(v.M.ReplyTo, out r)) v.Reply = r.M; }
            return rows;
        }

        public List<KeyValuePair<TeamRoom, TeamMsg>> Search(string query)
        {
            var hits = new List<KeyValuePair<TeamRoom, TeamMsg>>();
            if (string.IsNullOrWhiteSpace(query)) return hits;
            foreach (var r in Rooms())
            {
                Poll(r.Id);
                HashSet<string> p;
                foreach (var m in Messages(r.Id, out p).Where(x => !Events.Contains(x.Kind) && (x.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || x.FileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)))
                    hits.Add(new KeyValuePair<TeamRoom, TeamMsg>(r, m));
            }
            return hits.OrderByDescending(h => h.Value.Ts).Take(100).ToList();
        }

        public int Unread(string room)
        {
            long seen; Local.ReadTs.TryGetValue(room, out seen);
            HashSet<string> p;
            return Messages(room, out p).Count(m => m.Ts > seen && m.From != Me && !Events.Contains(m.Kind));
        }
        public void MarkRead(string room)
        {
            HashSet<string> p;
            var last = Messages(room, out p).LastOrDefault();
            if (last == null) return;
            long old; Local.ReadTs.TryGetValue(room, out old);
            if (last.Ts <= old) return;
            Local.ReadTs[room] = last.Ts; SaveLocal();
            try { WriteAtomic(Path.Combine(Root, "rooms", room, "reads", SafeId(Me) + ".json"), ToJson(new { ts = last.Ts, name = Local.Name })); } catch { }
        }
        // Who has read a room up to which time (device id -> ms).
        public Dictionary<string, long> Reads(string room)
        {
            var d = new Dictionary<string, long>();
            try
            {
                string dir = Path.Combine(Root, "rooms", room, "reads");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        var x = ReadJson<Dictionary<string, object>>(f);
                        if (x != null && x.ContainsKey("ts")) d[Path.GetFileNameWithoutExtension(f)] = Convert.ToInt64(x["ts"]);
                    }
            }
            catch { }
            return d;
        }

        // ---------- devices: presence, typing, update state ----------
        List<TeamDevice> devices = new List<TeamDevice>(); DateTime devicesAt;
        public List<TeamDevice> Devices(bool refresh = false)
        {
            if (!refresh && (DateTime.Now - devicesAt).TotalSeconds < 4) return devices;
            var list = new List<TeamDevice>();
            try
            {
                string dir = Path.Combine(Root, "devices");
                var p = Policy;
                if (Directory.Exists(dir) && p != null)
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        // Signed by the device itself; only policy members count, and only those this PC may see.
                        var raw = ReadJson<Dictionary<string, object>>(f);
                        if (raw == null || !raw.ContainsKey("data") || !raw.ContainsKey("sig")) continue;
                        string data = raw["data"] as string;
                        var d = data == null ? null : FromJson<TeamDevice>(data);
                        if (d == null || d.Id.Length == 0) continue;
                        var m = TeamSecurity.Member(p, d.Id);
                        if (m == null || !TeamSecurity.VerifyDevice(m.PublicKey, data, raw["sig"] as string)) continue;
                        if (d.Id != Me && !TeamSecurity.Allowed(p, Me, "see", d.Id)) continue;
                        if (d.Agents == null) d.Agents = new List<TeamAgent>();
                        d.Name = m.Name.Length > 0 ? m.Name : d.Name; d.Admin = m.Role == "admin";
                        list.Add(d);
                    }
            }
            catch (Exception ex) { Log("Team devices: " + ex.Message); }
            devices = list.OrderByDescending(d => Online(d)).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList(); devicesAt = DateTime.Now;
            return devices;
        }
        public static bool Online(TeamDevice d) { return NowMs() - d.LastSeen < 60000; }

        public string TypingRoom = "", Working = ""; long typingTs; long lastBeat;
        public Func<TeamDevice, TeamDevice> FillState;   // the updater adds version and update state
        public void SetTyping(string room) { if (TypingRoom == room && NowMs() - typingTs < 3000) return; TypingRoom = room; typingTs = NowMs(); Heartbeat(true); }

        public void Heartbeat(bool force = false)
        {
            if (!Reachable) return;
            if (!force && NowMs() - lastBeat < 15000) return;
            lastBeat = NowMs();
            try
            {
                var t = Team;
                var d = new TeamDevice
                {
                    Id = Me, Name = Local.Name, User = Environment.UserName, Machine = Environment.MachineName, LastSeen = NowMs(),
                    TypingRoom = NowMs() - typingTs < 8000 ? TypingRoom : "", TypingTs = typingTs, Working = Working ?? "",
                    Admin = t != null && t.Admins.Contains(Me), AppPath = SafeExe()
                };
                foreach (var a in Local.MyAgents) d.Agents.Add(new TeamAgent { Agent = a, Owner = Local.Name, Handle = Handle(a) });
                if (FillState != null) d = FillState(d) ?? d;
                string data = ToJson(d);
                WriteAtomic(Path.Combine(Root, "devices", SafeId(Me) + ".json"), ToJson(new Dictionary<string, object> { { "data", data }, { "sig", TeamSecurity.SignDevice(this, data) } }));
            }
            catch (Exception ex) { Log("Team heartbeat: " + ex.Message); }
        }
        static string SafeExe() { try { return System.Windows.Forms.Application.ExecutablePath; } catch { return ""; } }
        public string Handle(string agentId) { return SafeId(Local.Name).Replace("-", "") + "-" + SafeId(agentId); }

        // ---------- files: chunked, resumable, SHA-256 checked ----------
        public const int Chunk = 4 << 20;
        // Copies src to dest through dest.part, continuing a .part left by an interrupted copy. Returns the SHA-256 (hex).
        public static string CopyResumable(string src, string dest, Action<long, long> progress, Func<bool> cancel)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            string part = dest + ".part";
            long total = new FileInfo(src).Length;
            using (var sha = SHA256.Create())
            {
                long done = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (done > total) { File.Delete(part); done = 0; }
                var buf = new byte[Chunk];
                using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Chunk))
                {
                    // Hash the bytes already copied (read back from the source; the end check compares the destination).
                    long hashed = 0;
                    while (hashed < done) { int n = input.Read(buf, 0, (int)Math.Min(buf.Length, done - hashed)); if (n <= 0) break; sha.TransformBlock(buf, 0, n, null, 0); hashed += n; }
                    using (var output = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.Read, Chunk))
                    {
                        while (done < total)
                        {
                            if (cancel != null && cancel()) throw new OperationCanceledException("Transfer paused.");
                            int n = input.Read(buf, 0, buf.Length);
                            if (n <= 0) break;
                            output.Write(buf, 0, n); output.Flush(true);
                            sha.TransformBlock(buf, 0, n, null, 0);
                            done += n;
                            if (progress != null) progress(done, total);
                        }
                    }
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                string hash = Hex(sha.Hash);
                if (!string.Equals(FileSha(part), hash, StringComparison.OrdinalIgnoreCase)) { File.Delete(part); throw new IOException("The copy did not match the original (checksum). It was deleted; try again."); }
                Retry(() => { if (File.Exists(dest)) File.Delete(dest); File.Move(part, dest); });
                return hash;
            }
        }
        // A new exe is often held for a few seconds by the antivirus scan: retry moves and replaces for up to a minute.
        public static void Retry(Action a)
        {
            for (int i = 0; ; i++)
            {
                try { a(); return; }
                catch (Exception ex) { if (i >= 120 || !(ex is IOException || ex is UnauthorizedAccessException)) throw; Thread.Sleep(500); }
            }
        }
        public static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2")); return sb.ToString(); }
        public static string FileSha(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Chunk)) return Hex(sha.ComputeHash(fs));
        }

        // Uploads a file into the team folder and posts it in the room. Resumes after a restart (Local.Uploads).
        // Files are encrypted with the room key on this PC first; only the encrypted copy goes to the team folder.
        public TeamMsg SendFile(string room, string path, Action<long, long> progress, Func<bool> cancel, string uploadId = null)
        {
            if (!Can("files", "*")) throw new UnauthorizedAccessException("You are not allowed to send files.");
            var up = Local.Uploads.FirstOrDefault(u => u.Id == uploadId);
            if (up == null)
            {
                int epoch = TeamSecurity.LatestEpoch(this, room);
                byte[] key = epoch > 0 ? TeamSecurity.RoomKey(this, room, epoch) : null;
                if (key == null) throw new UnauthorizedAccessException("No access to this room.");
                up = new TeamUpload { Id = Guid.NewGuid().ToString("N"), Path = path, Room = room, Epoch = epoch, EncPath = Path.Combine(LocalDir, "uploads", Guid.NewGuid().ToString("N") + ".enc") };
                Directory.CreateDirectory(Path.GetDirectoryName(up.EncPath));
                TeamSecurity.SealFile(key, path, up.EncPath);
                lock (gate) Local.Uploads.Add(up); SaveLocal();
            }
            string name = Path.GetFileName(path);
            string plainSha = FileSha(path);
            long size = new FileInfo(path).Length;
            CopyResumable(up.EncPath, Path.Combine(Root, "files", up.Id, "data.bin"), progress, cancel);
            lock (gate) Local.Uploads.RemoveAll(u => u.Id == up.Id);
            SaveLocal();
            try { File.Delete(up.EncPath); } catch { }
            Audit("file-send", up.Id + " " + size);
            int fileEpoch = up.Epoch;
            return Post(room, "file", "", m => { m.FileId = up.Id; m.FileName = name; m.Size = size; m.Sha = plainSha; m.Version = "e" + fileEpoch; });
        }
        public void ResumeUploads(Action<string> status)
        {
            foreach (var u in Local.Uploads.ToList())
            {
                if (!File.Exists(u.Path)) { lock (gate) Local.Uploads.Remove(u); SaveLocal(); continue; }
                try { SendFile(u.Room, u.Path, null, null, u.Id); if (status != null) status("Sent " + Path.GetFileName(u.Path)); }
                catch (Exception ex) { Log("Team resume upload: " + ex.Message); }
            }
        }
        // Downloads a shared file to a folder (resumable); refuses a file whose checksum does not match.
        public string Download(TeamMsg m, string folder, Action<long, long> progress, Func<bool> cancel)
        {
            if (!Can("files", "*")) throw new UnauthorizedAccessException("You are not allowed to receive files.");
            string src = Path.Combine(Root, "files", m.FileId, "data.bin");
            if (!File.Exists(src)) throw new FileNotFoundException("The file is no longer in the team folder.", m.FileName);
            int epoch; if (!int.TryParse((m.Version ?? "").TrimStart('e'), out epoch)) epoch = m.Epoch;
            byte[] key = TeamSecurity.RoomKey(this, m.Room, epoch);
            if (key == null) throw new UnauthorizedAccessException("No key for this file (no access to the room).");
            string name = Path.GetFileName(m.FileName);   // never a path from the sender
            string dest = Path.Combine(folder, name);
            if (File.Exists(dest) && string.Equals(FileSha(dest), m.Sha, StringComparison.OrdinalIgnoreCase)) return dest;
            if (File.Exists(dest)) dest = Path.Combine(folder, Path.GetFileNameWithoutExtension(name) + " (" + DateTime.Now.ToString("HHmmss") + ")" + Path.GetExtension(name));
            string enc = Path.Combine(LocalDir, "downloads", m.FileId + ".enc");
            CopyResumable(src, enc, progress, cancel);
            try { TeamSecurity.OpenFile(key, enc, dest); }
            catch (System.Security.Cryptography.CryptographicException ex) { try { File.Delete(dest); } catch { } try { File.Delete(enc); } catch { } throw new IOException("The shared file is damaged or was changed. Not saved. (" + ex.Message + ")"); }
            try { File.Delete(enc); } catch { }
            if (!string.IsNullOrEmpty(m.Sha) && !string.Equals(FileSha(dest), m.Sha, StringComparison.OrdinalIgnoreCase)) { File.Delete(dest); throw new IOException("The shared file does not match its checksum. Not saved."); }
            return dest;
        }

        // ---------- polling loop (background) ----------
        Thread loop; volatile bool running; public string ActiveRoom = "";
        public readonly AutoResetEvent Wake = new AutoResetEvent(false);
        public Action Tick;   // extra work each round (updater)
        public void Start()
        {
            if (running) return;
            running = true;
            loop = new Thread(Run) { IsBackground = true, Name = "Team" };
            loop.Start();
        }
        public void Stop() { running = false; Wake.Set(); }
        public Action RemoteTick;   // remote assistance work each round
        long lastKeySync; string lastStatus = "";
        // Terminate / revoke / block from the admin take effect here within seconds.
        public void CheckMyAccess()
        {
            var m = TeamSecurity.Member(Policy, Me);
            if (m == null) return;
            if (m.Terminate > Local.HandledTerminate)
            {
                Local.HandledTerminate = m.Terminate; SaveLocal();
                Audit("terminated", "by admin");
                var h = Terminated; if (h != null) try { h("terminated"); } catch { }
            }
            if (m.Status != "approved") lock (KeyCache) KeyCache.Clear();
            if (m.Status != lastStatus)
            {
                string old = lastStatus; lastStatus = m.Status;
                if (old.Length > 0 && (m.Status == "revoked" || m.Status == "blocked" || m.Status == "suspended")) { var h = Terminated; if (h != null) try { h(m.Status); } catch { } }
                Raise();
            }
        }
        void Run()
        {
            bool first = true;
            while (running)
            {
                try
                {
                    if (Reachable) CheckMyAccess();
                    if (Reachable && Approved)
                    {
                        if (AdminRole && NowMs() - lastKeySync > 15000) { lastKeySync = NowMs(); TeamSecurity.SyncKeys(this, null, true); }
                        if (RemoteTick != null) try { RemoteTick(); } catch (Exception ex) { Log("Team remote: " + ex.Message); }
                        Flush();
                        if (first) { ResumeUploads(null); first = false; }
                        bool any = false;
                        foreach (var r in Rooms())
                        {
                            var fresh = Poll(r.Id);
                            if (fresh.Count == 0) continue;
                            any = true;
                            var h = Incoming;
                            foreach (var m in fresh.Where(x => x.From != Me && x.Ts > Local.Joined - 60000))
                                if (h != null) try { h(m); } catch (Exception ex) { Log("Team incoming: " + ex.Message); }
                        }
                        Heartbeat();
                        if (Tick != null) Tick();
                        if (any) Raise();
                        else if ((DateTime.Now - devicesAt).TotalSeconds > 5) { Devices(true); Raise(); }
                    }
                }
                catch (Exception ex) { Log("Team loop: " + ex.Message); }
                Wake.WaitOne(ActiveRoom.Length > 0 ? 1500 : 4000);
            }
        }
    }

    // The app's one team connection.
    static class TeamNet
    {
        static TeamClient me;
        public static TeamClient Me { get { if (me == null) me = new TeamClient(Path.Combine(Store.Dir, "team")); return me; } }
        public static bool Started;
        internal static bool SetupInProgress;
        public static void StartIfConfigured()
        {
            try
            {
                if (SetupInProgress || !Me.Configured || Started) return;
                Started = true;
                Releases.Attach(Me);
                TeamAgents.Attach(Me);
                TeamRemote.Attach(Me);
                TeamRemoteUi.Attach(Me);
                Me.Incoming += Notify;
                Me.Terminated += OnTerminated;
                Me.Start();
            }
            catch (Exception ex) { App.Log("Team start: " + ex.Message); }
        }
        // The admin ended this PC's session: stop remote help, save and close (terminate), or just lock Team (revoke / block / suspend).
        static void OnTerminated(string why)
        {
            lock (TeamRemote.Hosts) foreach (var h in TeamRemote.Hosts.ToList()) h.Stop("team access ended");
            try
            {
                App.I.Sync.BeginInvoke(new Action(() =>
                {
                    if (why == "terminated")
                    {
                        App.I.Balloon("All in One", "The team admin closed All in One on this PC. Your work was saved.");
                        Ui.Later(3000, () => App.I.Exit());
                    }
                    else App.I.Balloon("Team", why == "suspended" ? "The team admin paused your team access." : "The team admin removed your team access.");
                }));
            }
            catch { }
        }
        // Balloon for direct messages, mentions and release news when the Team page is not in front.
        static void Notify(TeamMsg m)
        {
            var c = Me;
            if (m.Room == c.ActiveRoom) return;
            var r = c.Room(m.Room);
            bool dm = r != null && r.Kind == "dm";
            bool mention = m.Text.IndexOf("@" + c.Local.Name, StringComparison.OrdinalIgnoreCase) >= 0 || m.Text.IndexOf("@everyone", StringComparison.OrdinalIgnoreCase) >= 0;
            bool release = m.Kind == "release";
            if (!dm && !mention && !release) return;
            string title = release ? "New All in One version" : (dm ? m.Name : m.Name + " in " + c.RoomTitle(r));
            string text = m.Kind == "file" ? "Sent a file: " + m.FileName : Ui.OneLine(m.Text, 120);
            try { App.I.Sync.BeginInvoke(new Action(() => App.I.Balloon(title, text))); } catch { }
        }
    }
}
