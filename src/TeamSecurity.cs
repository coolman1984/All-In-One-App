using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace StickyPro
{
    // ===================================================================================================
    // Team security: identities, the signed policy (default deny), room keys and encryption.
    //
    // There is no server: the shared folder can be read by anyone who has access to it. So enforcement is
    // cryptographic, and every app enforces the same signed policy:
    //  - Device identity: each PC has its own RSA-3072 key in the Windows key store (never exported). It signs
    //    everything the PC writes (messages, presence, key files, remote-session files).
    //  - Admin identity: the admin key (Releases.cs) signs policy.json. Admin mode also needs the admin passphrase
    //    (PBKDF2, never stored in clear, nothing hardcoded); it unlocks for 30 minutes.
    //  - Policy: members (status pending / approved / suspended / revoked / blocked, role, groups, public key),
    //    grants (subject -> feature -> target), session limits. Signed, sequence-numbered (no replay).
    //  - Access to a room = holding its key. Room keys (AES-256 + HMAC-SHA256) are wrapped per permitted device
    //    with that device's public key. When someone loses access, the admin's PC rotates the key (new epoch),
    //    so they cannot read anything new. Messages from senders without permission are dropped by receivers.
    // ===================================================================================================
    public class TeamMember
    {
        public string Id = "", Name = "", Machine = "", User = "", Status = "pending", Role = "user", PublicKey = "";
        public List<string> Groups = new List<string>();
        public long Since; public int Terminate;
    }
    public class TeamGrant { public string Subject = "*", Feature = "", Target = "*", Note = ""; }
    public class TeamPolicyDoc
    {
        public long Seq; public int SessionMinutes = 30; public long Updated; public string By = "";
        public List<TeamMember> Members = new List<TeamMember>();
        public List<TeamGrant> Grants = new List<TeamGrant>();
        public List<string> GroupNames = new List<string>();
    }
    public class TeamJoin { public string Id = "", Name = "", Machine = "", User = "", PublicKey = ""; public long Ts; }
    public class TeamKeyFile { public string Room = "", Device = "", Wrapped = "", By = ""; public int Epoch; }

    static class TeamSecurity
    {
        // Features a grant can give. Targets: "*", a device id, "group:<name>", "room:<id>", "project:<path>".
        public static readonly string[] Features = { "room", "dm", "files", "agents", "project", "see", "screen.view", "screen.control", "updates" };
        public static string Describe(string f)
        {
            switch (f)
            {
                case "room": return "Read and write a room";
                case "dm": return "Private messages with a person";
                case "files": return "Send and receive files";
                case "agents": return "Share and call AI agents";
                case "project": return "Project rooms and project folders";
                case "see": return "See a person or PC in the lists";
                case "screen.view": return "View someone's screen (with their approval)";
                case "screen.control": return "Control someone's mouse and keyboard (with their approval)";
                case "updates": return "Receive app updates";
            }
            return f;
        }

        // ---------- device key ----------
        const int ProvRsaAes = 24;
        static CspParameters DeviceCsp(TeamClient c, bool existing)
        {
            var p = new CspParameters(ProvRsaAes) { KeyContainerName = c.Local.DeviceKeyContainer };
            if (existing) p.Flags = CspProviderFlags.UseExistingKey;
            return p;
        }
        public static string EnsureDeviceKey(TeamClient c)
        {
            try { using (var rsa = new RSACryptoServiceProvider(DeviceCsp(c, true)) { PersistKeyInCsp = true }) return rsa.ToXmlString(false); }
            catch (CryptographicException) { using (var rsa = new RSACryptoServiceProvider(3072, DeviceCsp(c, false)) { PersistKeyInCsp = true }) return rsa.ToXmlString(false); }
        }
        public static void DeleteDeviceKey(TeamClient c) { try { using (var rsa = new RSACryptoServiceProvider(DeviceCsp(c, true))) { rsa.PersistKeyInCsp = false; rsa.Clear(); } } catch { } }
        public static string SignDevice(TeamClient c, string text)
        {
            using (var rsa = new RSACryptoServiceProvider(DeviceCsp(c, true)) { PersistKeyInCsp = true }) return Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(text), "SHA256"));
        }
        public static bool VerifyDevice(string publicXml, string text, string sig)
        {
            if (string.IsNullOrEmpty(publicXml) || string.IsNullOrEmpty(sig)) return false;
            try { return Releases.Verify(publicXml, Encoding.UTF8.GetBytes(text), Convert.FromBase64String(sig)); } catch { return false; }
        }
        public static string Wrap(string publicXml, byte[] key)
        {
            using (var rsa = new RSACryptoServiceProvider(new CspParameters(ProvRsaAes)) { PersistKeyInCsp = false }) { rsa.FromXmlString(publicXml); return Convert.ToBase64String(rsa.Encrypt(key, true)); }
        }
        public static byte[] Unwrap(TeamClient c, string wrapped)
        {
            using (var rsa = new RSACryptoServiceProvider(DeviceCsp(c, true)) { PersistKeyInCsp = true }) return rsa.Decrypt(Convert.FromBase64String(wrapped), true);
        }
        // Short code to compare by phone or in person before approving a PC ("7F3A-91C2").
        public static string Fingerprint(string publicXml)
        {
            if (string.IsNullOrEmpty(publicXml)) return "----";
            using (var sha = SHA256.Create()) { var h = TeamClient.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(publicXml))).ToUpperInvariant(); return h.Substring(0, 4) + "-" + h.Substring(4, 4); }
        }

        // ---------- symmetric encryption: AES-256-CBC + HMAC-SHA256 (encrypt then MAC) ----------
        public static byte[] NewKey() { var k = new byte[64]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(k); return k; }
        static byte[] EncKey(byte[] k) { var e = new byte[32]; Buffer.BlockCopy(k, 0, e, 0, 32); return e; }
        static byte[] MacKey(byte[] k) { var m = new byte[32]; Buffer.BlockCopy(k, 32, m, 0, 32); return m; }
        public static byte[] Seal(byte[] key, byte[] plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = EncKey(key); aes.GenerateIV(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                byte[] ct; using (var e = aes.CreateEncryptor()) ct = e.TransformFinalBlock(plain, 0, plain.Length);
                var body = new byte[16 + ct.Length]; Buffer.BlockCopy(aes.IV, 0, body, 0, 16); Buffer.BlockCopy(ct, 0, body, 16, ct.Length);
                byte[] mac; using (var h = new HMACSHA256(MacKey(key))) mac = h.ComputeHash(body);
                var all = new byte[body.Length + 32]; Buffer.BlockCopy(body, 0, all, 0, body.Length); Buffer.BlockCopy(mac, 0, all, body.Length, 32);
                return all;
            }
        }
        public static byte[] Open(byte[] key, byte[] sealedData)
        {
            if (sealedData == null || sealedData.Length < 16 + 16 + 32) throw new CryptographicException("Damaged data.");
            int n = sealedData.Length - 32;
            byte[] mac; using (var h = new HMACSHA256(MacKey(key))) mac = h.ComputeHash(sealedData, 0, n);
            int diff = 0; for (int i = 0; i < 32; i++) diff |= mac[i] ^ sealedData[n + i];
            if (diff != 0) throw new CryptographicException("Data was changed or the key is wrong.");
            using (var aes = Aes.Create())
            {
                aes.Key = EncKey(key); var iv = new byte[16]; Buffer.BlockCopy(sealedData, 0, iv, 0, 16); aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var d = aes.CreateDecryptor()) return d.TransformFinalBlock(sealedData, 16, n - 16);
            }
        }
        public static string SealText(byte[] key, string text) { return Convert.ToBase64String(Seal(key, Encoding.UTF8.GetBytes(text ?? ""))); }
        public static string OpenText(byte[] key, string b64) { return Encoding.UTF8.GetString(Open(key, Convert.FromBase64String(b64))); }

        // Files: [IV 16][ciphertext][HMAC 32], streamed (big files never load into memory).
        public static void SealFile(byte[] key, string src, string dst)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = EncKey(key); aes.GenerateIV(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                using (var h = new HMACSHA256(MacKey(key)))
                {
                    using (var output = new FileStream(dst, FileMode.Create, FileAccess.ReadWrite))
                    {
                        output.Write(aes.IV, 0, 16);
                        using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var enc = aes.CreateEncryptor())
                        using (var cs = new CryptoStream(new NonClosing(output), enc, CryptoStreamMode.Write)) input.CopyTo(cs, 1 << 20);
                        output.Position = 0; var mac = h.ComputeHash(output);
                        output.Position = output.Length; output.Write(mac, 0, 32);
                    }
                }
            }
        }
        public static void OpenFile(byte[] key, string src, string dst)
        {
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                long n = input.Length - 32;
                if (n < 32) throw new CryptographicException("Damaged file.");
                byte[] mac; using (var h = new HMACSHA256(MacKey(key))) mac = h.ComputeHash(new LimitedStream(input, n));
                var stored = new byte[32]; input.Position = n; input.Read(stored, 0, 32);
                int diff = 0; for (int i = 0; i < 32; i++) diff |= mac[i] ^ stored[i];
                if (diff != 0) throw new CryptographicException("The file was changed or damaged (integrity check failed).");
                input.Position = 0; var iv = new byte[16]; input.Read(iv, 0, 16);
                using (var aes = Aes.Create())
                {
                    aes.Key = EncKey(key); aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
                    using (var output = new FileStream(dst, FileMode.Create, FileAccess.Write))
                    using (var dec = aes.CreateDecryptor())
                    using (var cs = new CryptoStream(new LimitedStream(input, n - 16), dec, CryptoStreamMode.Read)) cs.CopyTo(output, 1 << 20);
                }
            }
        }
        class NonClosing : Stream
        {
            readonly Stream s; public NonClosing(Stream s) { this.s = s; }
            public override bool CanRead { get { return false; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return true; } }
            public override long Length { get { return s.Length; } } public override long Position { get { return s.Position; } set { } }
            public override void Flush() { s.Flush(); } public override int Read(byte[] b, int o, int c) { throw new NotSupportedException(); }
            public override long Seek(long o, SeekOrigin r) { throw new NotSupportedException(); } public override void SetLength(long v) { throw new NotSupportedException(); }
            public override void Write(byte[] b, int o, int c) { s.Write(b, o, c); }
            protected override void Dispose(bool d) { s.Flush(); }
        }
        class LimitedStream : Stream
        {
            readonly Stream s; long left; public LimitedStream(Stream s, long count) { this.s = s; left = count; }
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override int Read(byte[] b, int o, int c) { if (left <= 0) return 0; int n = s.Read(b, o, (int)Math.Min(c, left)); left -= n; return n; }
            public override long Seek(long o, SeekOrigin r) { throw new NotSupportedException(); } public override void SetLength(long v) { throw new NotSupportedException(); }
            public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
            protected override void Dispose(bool d) { }
        }

        // ---------- admin passphrase (PBKDF2-SHA1 200k, salted; stored only on the admin PC) ----------
        static string AdminFile(TeamClient c) { return Path.Combine(c.LocalDir, "admin.json"); }
        public static bool HasPassphrase(TeamClient c) { return File.Exists(AdminFile(c)); }
        public static void SetPassphrase(TeamClient c, string pass)
        {
            if (string.IsNullOrEmpty(pass) || pass.Length < 8) throw new ArgumentException("The admin passphrase needs at least 8 characters.");
            var salt = new byte[16]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(salt);
            using (var kdf = new Rfc2898DeriveBytes(pass, salt, 200000))
                TeamClient.WriteAtomic(AdminFile(c), TeamClient.ToJson(new Dictionary<string, object> { { "salt", Convert.ToBase64String(salt) }, { "hash", Convert.ToBase64String(kdf.GetBytes(32)) }, { "iter", 200000 } }));
        }
        public static bool Unlock(TeamClient c, string pass)
        {
            try
            {
                var d = TeamClient.FromJson<Dictionary<string, object>>(File.ReadAllText(AdminFile(c)));
                var salt = Convert.FromBase64String((string)d["salt"]); var want = Convert.FromBase64String((string)d["hash"]);
                byte[] got; using (var kdf = new Rfc2898DeriveBytes(pass ?? "", salt, Convert.ToInt32(d["iter"]))) got = kdf.GetBytes(32);
                int diff = 0; for (int i = 0; i < 32; i++) diff |= got[i] ^ want[i];
                if (diff != 0) { c.Audit("admin-unlock-failed", ""); return false; }
                c.AdminUntil = DateTime.Now.AddMinutes(30); c.Audit("admin-unlock", "");
                return true;
            }
            catch { return false; }
        }
        public static bool Unlocked(TeamClient c) { return c.AdminUntil > DateTime.Now; }
        public static void Lock(TeamClient c) { c.AdminUntil = DateTime.MinValue; }

        // ---------- policy ----------
        public static string PolicyPath(TeamClient c) { return Path.Combine(c.Root, "policy.json"); }
        public static TeamPolicyDoc Load(TeamClient c, out string error)
        {
            var p = Releases.ReadSignedDoc<TeamPolicyDoc>(c, PolicyPath(c), out error);
            if (p == null) return null;
            if (p.Members == null) p.Members = new List<TeamMember>(); if (p.Grants == null) p.Grants = new List<TeamGrant>(); if (p.GroupNames == null) p.GroupNames = new List<string>();
            foreach (var m in p.Members) if (m.Groups == null) m.Groups = new List<string>();
            if (p.Seq < c.Local.PolicySeq) { error = "old policy replayed (seq " + p.Seq + " < " + c.Local.PolicySeq + ")"; return null; }
            if (p.Seq > c.Local.PolicySeq) { c.Local.PolicySeq = p.Seq; c.SaveLocal(); }
            return p;
        }
        public static TeamPolicyDoc ChangePolicy(TeamClient c, Action<TeamPolicyDoc> change, string what)
        {
            Releases.RequireAdmin(c);
            TeamPolicyDoc p;
            using (Releases.Lock(c))
            {
                string err; p = Load(c, out err);
                if (p == null)
                {
                    // Tampered or replayed file: rebuild from the last good policy this admin PC verified.
                    var good = c.LastGoodPolicy;
                    if (good == null || good.Seq < c.Local.PolicySeq) throw new InvalidOperationException("policy.json is unreadable: " + err);
                    p = TeamClient.FromJson<TeamPolicyDoc>(TeamClient.ToJson(good));
                    c.Audit("policy-repair", "replaced a bad policy file (" + err + ")");
                }
                change(p);
                if (!p.Members.Any(m => m.Role == "admin" && m.Status == "approved")) throw new InvalidOperationException("The admin cannot remove their own access.");
                p.Seq++; p.Updated = TeamClient.NowMs(); p.By = c.Local.Name;
                Releases.WriteSignedDoc(c, PolicyPath(c), p);
                c.Local.PolicySeq = p.Seq; c.SaveLocal();
            }
            c.Audit("policy", what + " -> seq " + p.Seq);
            c.SetPolicy(p);
            SyncKeys(c, null, true);
            return p;
        }
        public static TeamPolicyDoc InitialPolicy(TeamClient c, string devicePub)
        {
            var p = new TeamPolicyDoc { Seq = 1, Updated = TeamClient.NowMs(), By = c.Local.Name };
            p.Members.Add(new TeamMember { Id = c.Me, Name = c.Local.Name, Machine = Environment.MachineName, User = Environment.UserName, Status = "approved", Role = "admin", PublicKey = devicePub, Since = TeamClient.NowMs() });
            p.GroupNames.Add("everyone");
            // Sensible starting rights; screen viewing and control stay admin-only until the admin grants them.
            foreach (var g in new[] { new[] { "room", "room:general" }, new[] { "room", "room:updates" }, new[] { "dm", "*" }, new[] { "files", "*" }, new[] { "agents", "*" }, new[] { "see", "*" }, new[] { "updates", "*" } })
                p.Grants.Add(new TeamGrant { Subject = "*", Feature = g[0], Target = g[1], Note = "default" });
            return p;
        }

        public static TeamMember Member(TeamPolicyDoc p, string id) { return p == null ? null : p.Members.FirstOrDefault(m => m.Id == id); }
        public static bool Approved(TeamPolicyDoc p, string id) { var m = Member(p, id); return m != null && m.Status == "approved"; }
        public static bool IsAdminMember(TeamPolicyDoc p, string id) { var m = Member(p, id); return m != null && m.Status == "approved" && m.Role == "admin"; }

        // The one permission check. Default deny; admins may do everything; suspended / revoked / blocked = nothing.
        public static bool Allowed(TeamPolicyDoc p, string device, string feature, string target)
        {
            var m = Member(p, device);
            if (m == null || m.Status != "approved") return false;
            if (m.Role == "admin") return true;
            foreach (var g in p.Grants)
            {
                bool covers = g.Feature == feature || g.Feature == "*" || (feature == "screen.view" && g.Feature == "screen.control");   // control includes viewing
                if (!covers) continue;
                if (!SubjectMatch(p, g.Subject, m)) continue;
                if (TargetMatch(p, g.Target, target)) return true;
            }
            return false;
        }
        static bool SubjectMatch(TeamPolicyDoc p, string subject, TeamMember m)
        {
            if (subject == "*" || subject == m.Id) return true;
            return subject.StartsWith("group:") && m.Groups.Contains(subject.Substring(6));
        }
        static bool TargetMatch(TeamPolicyDoc p, string grant, string target)
        {
            if (grant == "*" || string.Equals(grant, target, StringComparison.OrdinalIgnoreCase)) return true;
            if (grant.StartsWith("group:")) { var t = Member(p, target); return t != null && t.Groups.Contains(grant.Substring(6)); }
            if (grant.StartsWith("project:") && target != null && target.StartsWith("project:"))
                return target.Substring(8).StartsWith(grant.Substring(8).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (grant == "room:*" && target != null && target.StartsWith("room:")) return true;
            return false;
        }
        // Devices that may read and write a room.
        public static List<string> RoomMembers(TeamPolicyDoc p, TeamRoom r)
        {
            if (p == null || r == null) return new List<string>();
            var ok = p.Members.Where(m => m.Status == "approved").Select(m => m.Id);
            if (r.Kind == "dm") return ok.Where(id => r.Members.Contains(id) && r.Members.Where(x => x != id).All(o => Allowed(p, id, "dm", o))).ToList();
            return ok.Where(id => Allowed(p, id, "room", "room:" + r.Id) && (r.Kind != "project" || Allowed(p, id, "project", "project:" + r.Project))).ToList();
        }

        // ---------- room keys ----------
        static string KeyDir(TeamClient c, string room, int epoch) { return Path.Combine(c.Root, "keys", room, epoch.ToString("D4")); }
        public static int LatestEpoch(TeamClient c, string room)
        {
            try { var d = Path.Combine(c.Root, "keys", room); if (!Directory.Exists(d)) return 0; return Directory.GetDirectories(d).Select(x => { int n; return int.TryParse(Path.GetFileName(x), out n) ? n : 0; }).DefaultIfEmpty(0).Max(); }
            catch { return 0; }
        }
        static string KeyText(TeamKeyFile k) { return k.Room + "|" + k.Epoch + "|" + k.Device + "|" + k.Wrapped + "|" + k.By; }
        static void WriteKey(TeamClient c, string room, int epoch, TeamMember to, byte[] key)
        {
            var k = new TeamKeyFile { Room = room, Epoch = epoch, Device = to.Id, Wrapped = Wrap(to.PublicKey, key), By = c.Me };
            var doc = new Dictionary<string, object> { { "key", k }, { "sig", SignDevice(c, KeyText(k)) } };
            TeamClient.WriteAtomic(Path.Combine(KeyDir(c, room, epoch), Safe(to.Id) + ".key"), TeamClient.ToJson(doc));
        }
        static string Safe(string id) { var sb = new StringBuilder(); foreach (char ch in id ?? "") sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? char.ToLowerInvariant(ch) : '-'); return sb.ToString(); }
        // My key for a room epoch; only accepted when written by a PC that is itself allowed in the room (or the admin).
        public static byte[] RoomKey(TeamClient c, string room, int epoch)
        {
            string ck = room + "|" + epoch; byte[] key;
            lock (c.KeyCache) if (c.KeyCache.TryGetValue(ck, out key)) return key;
            try
            {
                string f = Path.Combine(KeyDir(c, room, epoch), Safe(c.Me) + ".key");
                if (!File.Exists(f)) return null;
                var raw = TeamClient.FromJson<Dictionary<string, object>>(TeamClient.ReadShared(f));
                var k = TeamClient.FromJson<TeamKeyFile>(TeamClient.ToJson(raw["key"]));
                var p = c.Policy; var r = c.Room(room);
                var by = Member(p, k.By);
                if (by == null || k.Device != c.Me || k.Room != room || k.Epoch != epoch) return null;
                if (!VerifyDevice(by.PublicKey, KeyText(k), (string)raw["sig"])) return null;
                if (!IsAdminMember(p, by.Id) && !RoomMembers(p, r).Contains(by.Id)) return null;
                key = Unwrap(c, k.Wrapped);
                lock (c.KeyCache) c.KeyCache[ck] = key;
                return key;
            }
            catch (Exception ex) { TeamClient.Log("Team key " + room + ": " + ex.Message); return null; }
        }
        // Gives every permitted PC the room key; the admin (rotate=true) also rotates when someone lost access.
        public static void SyncKeys(TeamClient c, string onlyRoom, bool rotate)
        {
            var p = c.Policy; if (p == null) return;
            foreach (var r in c.AllRooms().Where(x => onlyRoom == null || x.Id == onlyRoom))
            {
                try
                {
                    var members = RoomMembers(p, r);
                    if (!members.Contains(c.Me) && !IsAdminMember(p, c.Me)) continue;
                    int epoch = LatestEpoch(c, r.Id);
                    byte[] key = epoch > 0 ? RoomKey(c, r.Id, epoch) : null;
                    if (epoch == 0) { epoch = 1; key = NewKey(); }
                    else if (key == null) continue;   // I cannot manage a key I do not hold
                    var holders = Directory.Exists(KeyDir(c, r.Id, epoch)) ? Directory.GetFiles(KeyDir(c, r.Id, epoch), "*.key").Select(Path.GetFileNameWithoutExtension).ToList() : new List<string>();
                    var allowed = members.Concat(p.Members.Where(m => m.Role == "admin" && m.Status == "approved").Select(m => m.Id)).Distinct().ToList();
                    if (rotate && IsAdminMember(p, c.Me) && holders.Any(h => !allowed.Any(a => Safe(a) == h)))
                    {
                        epoch++; key = NewKey(); holders.Clear();
                        c.Audit("key-rotate", r.Id + " epoch " + epoch);
                    }
                    foreach (var id in allowed)
                    {
                        if (holders.Contains(Safe(id))) continue;
                        var m = Member(p, id); if (m == null || string.IsNullOrEmpty(m.PublicKey)) continue;
                        WriteKey(c, r.Id, epoch, m, key);
                    }
                    lock (c.KeyCache) c.KeyCache[r.Id + "|" + epoch] = key;
                }
                catch (Exception ex) { TeamClient.Log("Team key sync " + r.Id + ": " + ex.Message); }
            }
        }

        // ---------- admin actions ----------
        public static List<TeamJoin> PendingJoins(TeamClient c)
        {
            var list = new List<TeamJoin>(); var p = c.Policy;
            try
            {
                string dir = Path.Combine(c.Root, "joins");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.json"))
                    {
                        var raw = TeamClient.ReadJson<Dictionary<string, object>>(f); if (raw == null) continue;
                        var j = TeamClient.FromJson<TeamJoin>((string)raw["data"]);
                        if (!VerifyDevice(j.PublicKey, (string)raw["data"], (string)raw["sig"])) continue;   // forged join request
                        var m = Member(p, j.Id);
                        if (m == null || m.Status == "pending") list.Add(j);
                    }
            }
            catch (Exception ex) { TeamClient.Log("Team joins: " + ex.Message); }
            return list;
        }
        public static void Approve(TeamClient c, string deviceId)
        {
            var j = PendingJoins(c).FirstOrDefault(x => x.Id == deviceId);
            ChangePolicy(c, p =>
            {
                var m = Member(p, deviceId);
                if (m == null)
                {
                    if (j == null) throw new InvalidOperationException("No signed join request from this PC.");
                    m = new TeamMember { Id = j.Id, Name = j.Name, Machine = j.Machine, User = j.User, PublicKey = j.PublicKey };
                    p.Members.Add(m);
                }
                m.Status = "approved"; m.Since = TeamClient.NowMs();
            }, "approve " + deviceId);
        }
        public static void SetStatus(TeamClient c, string deviceId, string status, bool terminate)
        {
            if (status != "approved" && status != "suspended" && status != "revoked" && status != "blocked") throw new ArgumentException("Unknown status " + status);
            ChangePolicy(c, p =>
            {
                var m = Member(p, deviceId); if (m == null) throw new InvalidOperationException("Unknown PC.");
                if (m.Id == c.Me && status != "approved") throw new InvalidOperationException("The admin cannot block their own PC.");
                m.Status = status; if (terminate) m.Terminate++;
            }, status + (terminate ? " + terminate " : " ") + deviceId);
        }
        public static void Grant(TeamClient c, string subject, string feature, string target, string note)
        {
            if (!Features.Contains(feature) && feature != "*") throw new ArgumentException("Unknown feature " + feature);
            ChangePolicy(c, p => { if (!p.Grants.Any(g => g.Subject == subject && g.Feature == feature && g.Target == target)) p.Grants.Add(new TeamGrant { Subject = subject, Feature = feature, Target = target ?? "*", Note = note ?? "" }); }, "grant " + subject + " " + feature + " " + target);
        }
        public static void Revoke(TeamClient c, string subject, string feature, string target)
        {
            ChangePolicy(c, p => p.Grants.RemoveAll(g => g.Subject == subject && g.Feature == feature && g.Target == target), "revoke " + subject + " " + feature + " " + target);
        }
        public static void SetGroups(TeamClient c, string deviceId, IEnumerable<string> groups)
        {
            var list = groups.Select(g => g.Trim()).Where(g => g.Length > 0).Distinct().ToList();
            ChangePolicy(c, p =>
            {
                var m = Member(p, deviceId); if (m == null) throw new InvalidOperationException("Unknown PC.");
                m.Groups = list; foreach (var g in list) if (!p.GroupNames.Contains(g)) p.GroupNames.Add(g);
            }, "groups " + deviceId + " = " + string.Join(",", list));
        }
    }
}
