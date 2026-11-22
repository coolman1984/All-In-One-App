using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Remote assistance through the team folder: screen viewing and (optional) mouse/keyboard control.
    //  - Always needs the explicit approval of the person whose screen is shared, every time (a dialog on their
    //    PC; asking for help with "Get remote help" is that approval). No unattended or hidden access.
    //  - The policy must allow it (screen.view / screen.control from the helper to that person); checked when
    //    asked, when approved, and again on every frame. Revoking the permission ends the session at once.
    //  - Visible on the shared PC the whole time: red frame + banner with a Stop button and the time left.
    //  - Session key (AES-256 + HMAC) made by the shared PC and wrapped for the helper's device key only;
    //    frames and input events are encrypted with it. Requests, approvals and stops are device-signed.
    //  - Automatic timeout (policy SessionMinutes), stop from either side or by the admin, full audit.
    //  - Windows protects UAC prompts and the sign-in screen: they are not captured and cannot be controlled.
    // Files: sessions\<sid>\request.json, accept.json, end.json (signed), frame.bin, input.jsonl (encrypted).
    // ===================================================================================================
    public class RemoteRequest { public string Sid = "", Viewer = "", Sharer = "", Mode = "view", By = "", Note = ""; public long Ts; }
    public class RemoteAccept { public string Sid = "", Mode = "view", WrappedKey = ""; public long Ts, Expires; }
    public class RemoteEnd { public string Sid = "", By = "", Reason = ""; public long Ts; }
    public class RemoteInput { public int N; public string Kind = ""; public double X, Y; public int Button, Delta, Vk; }

    static class TeamRemote
    {
        public static bool ManualHosts;                          // tests drive RemoteHost.Step themselves
        public static Func<byte[]> Capture = CaptureScreen;     // tests may replace
        public static Action<RemoteInput> Inject = InjectInput;  // tests may replace
        public static event Action<RemoteRequest> RequestReceived;   // someone asks to see my screen (needs my answer)
        public static event Action<RemoteRequest> ShareOffered;      // someone shares their screen with me (open the viewer?)
        public static event Action<RemoteHost> HostStarted, HostStopped;

        static TeamClient c;
        static readonly HashSet<string> seen = new HashSet<string>();
        public static readonly List<RemoteHost> Hosts = new List<RemoteHost>();
        public static void Attach(TeamClient client) { c = client; client.RemoteTick = () => Tick(client); }

        static string Dir(TeamClient c, string sid) { return Path.Combine(c.Root, "sessions", sid); }
        public static string Feature(string mode) { return mode == "control" ? "screen.control" : "screen.view"; }

        static void WriteSigned(TeamClient c, string path, object o)
        {
            string data = TeamClient.ToJson(o);
            TeamClient.WriteAtomic(path, TeamClient.ToJson(new Dictionary<string, object> { { "data", data }, { "sig", TeamSecurity.SignDevice(c, data) } }));
        }
        // Reads a signed session file; signer must be one of the allowed devices.
        static T ReadSigned<T>(TeamClient c, string path, IEnumerable<string> signers, out string signer) where T : class
        {
            signer = null;
            var raw = TeamClient.ReadJson<Dictionary<string, object>>(path);
            if (raw == null || !raw.ContainsKey("data") || !raw.ContainsKey("sig")) return null;
            string data = raw["data"] as string, sig = raw["sig"] as string;
            var p = c.Policy;
            foreach (var id in signers.Distinct())
            {
                var m = TeamSecurity.Member(p, id);
                if (m != null && TeamSecurity.VerifyDevice(m.PublicKey, data, sig)) { signer = id; return TeamClient.FromJson<T>(data); }
            }
            return null;
        }
        static IEnumerable<string> Admins(TeamClient c) { var p = c.Policy; return p == null ? new string[0] : p.Members.Where(m => m.Role == "admin" && m.Status == "approved").Select(m => m.Id); }

        // ---------- starting ----------
        // As the helper: ask to view or control someone's screen. They must approve on their PC.
        public static RemoteRequest Ask(TeamClient c, string sharerId, string mode, string note)
        {
            if (!c.Can(Feature(mode), sharerId)) { c.Audit("remote-denied", "ask " + mode + " " + sharerId + " (policy)"); throw new UnauthorizedAccessException("You are not allowed to " + (mode == "control" ? "control" : "view") + " this person's screen."); }
            var r = new RemoteRequest { Sid = NewSid(), Viewer = c.Me, Sharer = sharerId, Mode = mode, By = c.Me, Note = note ?? "", Ts = TeamClient.NowMs() };
            WriteSigned(c, Path.Combine(Dir(c, r.Sid), "request.json"), r);
            c.Audit("remote-ask", r.Sid + " " + mode + " screen of " + sharerId);
            return r;
        }
        // As the person who needs help: share my screen with a helper (this click is my approval).
        public static RemoteHost Offer(TeamClient c, string viewerId, string mode)
        {
            if (!TeamSecurity.Allowed(c.Policy, viewerId, Feature(mode), c.Me)) throw new UnauthorizedAccessException("This person is not allowed to " + (mode == "control" ? "control" : "view") + " your screen. Ask the admin.");
            var r = new RemoteRequest { Sid = NewSid(), Viewer = viewerId, Sharer = c.Me, Mode = mode, By = c.Me, Ts = TeamClient.NowMs() };
            WriteSigned(c, Path.Combine(Dir(c, r.Sid), "request.json"), r);
            c.Audit("remote-offer", r.Sid + " " + mode + " to " + viewerId);
            return Approve(c, r, mode);
        }
        static string NewSid() { return DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N").Substring(0, 8); }

        // The shared PC's approval (granted mode may be lower than asked: control -> view).
        public static RemoteHost Approve(TeamClient c, RemoteRequest r, string mode)
        {
            if (r.Sharer != c.Me) throw new InvalidOperationException("Only the person whose screen is shared can approve.");
            if (r.Mode == "view" && mode == "control") mode = "view";
            if (!TeamSecurity.Allowed(c.Policy, r.Viewer, Feature(mode), c.Me)) throw new UnauthorizedAccessException("The policy does not allow this person to " + mode + " your screen.");
            var viewer = TeamSecurity.Member(c.Policy, r.Viewer);
            byte[] key = TeamSecurity.NewKey();
            int minutes = Math.Max(1, Math.Min(240, c.Policy.SessionMinutes));
            var a = new RemoteAccept { Sid = r.Sid, Mode = mode, Ts = TeamClient.NowMs(), Expires = TeamClient.NowMs() + minutes * 60000L, WrappedKey = TeamSecurity.Wrap(viewer.PublicKey, key) };
            WriteSigned(c, Path.Combine(Dir(c, r.Sid), "accept.json"), a);
            c.Audit("remote-approve", r.Sid + " " + mode + " by " + r.Viewer + " for " + minutes + " min");
            var host = new RemoteHost(c, r, a, key);
            lock (Hosts) Hosts.Add(host);
            if (ManualHosts) host.StartManual(); else host.Start();
            var h = HostStarted; if (h != null) try { h(host); } catch { }
            return host;
        }
        public static void Deny(TeamClient c, RemoteRequest r) { End(c, r.Sid, "denied"); c.Audit("remote-deny", r.Sid + " " + r.Viewer); }

        // Stop a session (viewer, sharer or admin).
        public static void End(TeamClient c, string sid, string reason)
        {
            try { WriteSigned(c, Path.Combine(Dir(c, sid), "end.json"), new RemoteEnd { Sid = sid, By = c.Me, Reason = reason ?? "stopped", Ts = TeamClient.NowMs() }); } catch (Exception ex) { TeamClient.Log("Remote end: " + ex.Message); }
            c.Audit("remote-end", sid + " " + reason);
        }
        public static bool Ended(TeamClient c, RemoteRequest r, out string reason)
        {
            reason = null; string by;
            var e = ReadSigned<RemoteEnd>(c, Path.Combine(Dir(c, r.Sid), "end.json"), new[] { r.Viewer, r.Sharer }.Concat(Admins(c)), out by);
            if (e != null && e.Sid == r.Sid) { reason = e.Reason + (by == r.Sharer ? "" : by == r.Viewer ? " (helper)" : " (admin)"); return true; }
            return false;
        }

        // ---------- listing ----------
        public class SessionInfo { public RemoteRequest R; public RemoteAccept A; public bool Ended; public string Reason; }
        public static List<SessionInfo> Sessions(TeamClient c, int maxAgeHours)
        {
            var list = new List<SessionInfo>();
            try
            {
                string root = Path.Combine(c.Root, "sessions");
                if (!Directory.Exists(root)) return list;
                long cut = TeamClient.NowMs() - maxAgeHours * 3600000L;
                foreach (var d in Directory.GetDirectories(root))
                {
                    string by;
                    var raw = TeamClient.ReadJson<Dictionary<string, object>>(Path.Combine(d, "request.json")); if (raw == null) continue;
                    var guess = TeamClient.FromJson<RemoteRequest>((string)raw["data"]);
                    var r = ReadSigned<RemoteRequest>(c, Path.Combine(d, "request.json"), new[] { guess.By }, out by);
                    if (r == null || r.Ts < cut || r.By != by || (r.By != r.Viewer && r.By != r.Sharer)) continue;
                    var a = ReadSigned<RemoteAccept>(c, Path.Combine(d, "accept.json"), new[] { r.Sharer }, out by);
                    string reason; bool ended = Ended(c, r, out reason);
                    if (a != null && TeamClient.NowMs() > a.Expires && !ended) { ended = true; reason = "timed out"; }
                    list.Add(new SessionInfo { R = r, A = a, Ended = ended, Reason = reason });
                }
            }
            catch (Exception ex) { TeamClient.Log("Remote sessions: " + ex.Message); }
            return list.OrderByDescending(s => s.R.Ts).ToList();
        }

        // Each team round: new requests for me, screens offered to me.
        static void Tick(TeamClient c)
        {
            foreach (var s in Sessions(c, 1))
            {
                if (s.Ended) continue;
                lock (seen) { if (seen.Contains(s.R.Sid)) continue; }
                if (s.R.Sharer == c.Me && s.A == null && s.R.By == s.R.Viewer && TeamClient.NowMs() - s.R.Ts < 120000)
                {
                    lock (seen) seen.Add(s.R.Sid);
                    if (!TeamSecurity.Allowed(c.Policy, s.R.Viewer, Feature(s.R.Mode), c.Me)) { End(c, s.R.Sid, "not allowed by policy"); continue; }
                    var h = RequestReceived; if (h != null) h(s.R);
                }
                else if (s.R.Viewer == c.Me && s.A != null)
                {
                    lock (seen) seen.Add(s.R.Sid);
                    if (s.R.By == s.R.Sharer) { var h = ShareOffered; if (h != null) h(s.R); }
                }
            }
        }

        // ---------- capture and input (Windows) ----------
        static byte[] lastFrame;
        public static byte[] CaptureScreen()
        {
            var vs = SystemInformation.VirtualScreen;
            using (var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
                    var p = Cursor.Position; int x = p.X - vs.Left, y = p.Y - vs.Top;   // the mouse pointer
                    using (var pen = new Pen(Color.Red, 2)) { g.DrawLine(pen, x - 8, y, x + 8, y); g.DrawLine(pen, x, y - 8, x, y + 8); }
                }
                int w = Math.Min(1600, vs.Width), h = (int)((long)vs.Height * w / vs.Width);
                using (var small = new Bitmap(bmp, w, h))
                using (var ms = new MemoryStream())
                {
                    var codec = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
                    var ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 55L);
                    small.Save(ms, codec, ep);
                    return ms.ToArray();
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)] struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public INPUTUNION u; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        const uint MOVE = 0x0001, LDOWN = 0x0002, LUP = 0x0004, RDOWN = 0x0008, RUP = 0x0010, MDOWN = 0x0020, MUP = 0x0040, WHEEL = 0x0800, VIRTUALDESK = 0x4000, ABSOLUTE = 0x8000, KEYUP = 0x0002;
        public static void InjectInput(RemoteInput e)
        {
            var list = new List<INPUT>();
            if (e.Kind == "keydown" || e.Kind == "keyup")
            {
                list.Add(new INPUT { type = 1, u = new INPUTUNION { ki = new KEYBDINPUT { wVk = (ushort)e.Vk, dwFlags = e.Kind == "keyup" ? KEYUP : 0 } } });
            }
            else
            {
                int ax = (int)Math.Round(Math.Max(0, Math.Min(1, e.X)) * 65535), ay = (int)Math.Round(Math.Max(0, Math.Min(1, e.Y)) * 65535);
                uint flags = MOVE | ABSOLUTE | VIRTUALDESK; uint data = 0;
                if (e.Kind == "down") flags |= e.Button == 2 ? RDOWN : e.Button == 3 ? MDOWN : LDOWN;
                if (e.Kind == "up") flags |= e.Button == 2 ? RUP : e.Button == 3 ? MUP : LUP;
                if (e.Kind == "wheel") { flags |= WHEEL; data = unchecked((uint)e.Delta); }
                list.Add(new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dx = ax, dy = ay, dwFlags = flags, mouseData = data } } });
            }
            SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        // ---------- the viewer side ----------
        public class Viewer
        {
            readonly TeamClient c; public readonly RemoteRequest R; public RemoteAccept A; byte[] key; int n; long lastLen = -1; DateTime lastWrite;
            public string Reason;
            public Viewer(TeamClient c, RemoteRequest r) { this.c = c; R = r; }
            // Waits for the approval (call repeatedly). True once the session is open.
            public bool Ready()
            {
                if (key != null) return true;
                string by;
                A = ReadSigned<RemoteAccept>(c, Path.Combine(Dir(c, R.Sid), "accept.json"), new[] { R.Sharer }, out by);
                if (A == null || A.Sid != R.Sid) return false;
                key = TeamSecurity.Unwrap(c, A.WrappedKey);
                c.Audit("remote-open", R.Sid + " " + A.Mode);
                return true;
            }
            public bool Ended() { if (A != null && TeamClient.NowMs() > A.Expires) { Reason = "time is up"; return true; } return TeamRemote.Ended(c, R, out Reason); }
            // The newest frame (JPEG bytes) or null when nothing changed.
            public byte[] Frame()
            {
                if (key == null) return null;
                var f = new FileInfo(Path.Combine(Dir(c, R.Sid), "frame.bin"));
                if (!f.Exists || (f.Length == lastLen && f.LastWriteTimeUtc == lastWrite)) return null;
                try
                {
                    var all = TeamSecurity.Open(key, File.ReadAllBytes(f.FullName));
                    lastLen = f.Length; lastWrite = f.LastWriteTimeUtc;
                    var jpg = new byte[all.Length - 8]; Buffer.BlockCopy(all, 8, jpg, 0, jpg.Length); return jpg;
                }
                catch { return null; }   // being replaced right now: next round
            }
            public void Send(RemoteInput e)
            {
                if (key == null || A == null || A.Mode != "control") return;
                e.N = ++n;
                string line = TeamSecurity.SealText(key, TeamClient.ToJson(e));
                File.AppendAllText(Path.Combine(Dir(c, R.Sid), "input.jsonl"), line + "\n");
            }
            public void Stop(string reason) { End(c, R.Sid, reason ?? "stopped by the helper"); }
        }
    }

    // The shared PC's side: sends frames, applies input (control mode only), watches stop / timeout / policy.
    public class RemoteHost
    {
        readonly TeamClient c; public readonly RemoteRequest R; public readonly RemoteAccept A; readonly byte[] key;
        volatile bool running; long inputOffset; int lastN; long frameNo; string lastHash = "";
        public string StopReason = ""; public int Frames, Inputs, Rejected;
        public bool Running { get { return running; } }
        public string ViewerName { get { var m = TeamSecurity.Member(c.Policy, R.Viewer); return m != null ? m.Name : R.Viewer; } }
        public RemoteHost(TeamClient c, RemoteRequest r, RemoteAccept a, byte[] key) { this.c = c; R = r; A = a; this.key = key; }
        public void Start() { running = true; new Thread(Loop) { IsBackground = true, Name = "Remote host" }.Start(); }
        public void StartManual() { running = true; }
        public void Stop(string reason) { if (!running) return; TeamRemote.End(c, R.Sid, reason); Finish(reason); }
        void Finish(string reason)
        {
            if (!running) return;
            running = false; StopReason = reason;
            c.Audit("remote-stop", R.Sid + " " + reason + " · " + Frames + " frames, " + Inputs + " inputs");
            lock (TeamRemote.Hosts) TeamRemote.Hosts.Remove(this);
            try { File.Delete(Path.Combine(c.Root, "sessions", R.Sid, "frame.bin")); } catch { }
            var h = Stopped; if (h != null) try { h(this); } catch { }
        }
        public event Action<RemoteHost> Stopped;
        public long SecondsLeft { get { return Math.Max(0, (A.Expires - TeamClient.NowMs()) / 1000); } }

        // One round: checks, frame, input. Public so tests can drive it without a thread.
        public bool Step(bool sendFrame)
        {
            if (!running) return false;
            string why;
            if (TeamRemote.Ended(c, R, out why)) { Finish(why); return false; }
            if (TeamClient.NowMs() > A.Expires) { Stop("time is up"); return false; }
            if (!TeamSecurity.Allowed(c.Policy, R.Viewer, TeamRemote.Feature(A.Mode), c.Me)) { Stop("permission removed by the admin"); return false; }
            if (!c.Approved) { Stop("this PC lost team access"); return false; }
            if (sendFrame)
            {
                var jpg = TeamRemote.Capture();
                string hash; using (var md5 = System.Security.Cryptography.MD5.Create()) hash = TeamClient.Hex(md5.ComputeHash(jpg));
                if (hash != lastHash)
                {
                    lastHash = hash; frameNo++;
                    var all = new byte[8 + jpg.Length]; Buffer.BlockCopy(BitConverter.GetBytes(frameNo), 0, all, 0, 8); Buffer.BlockCopy(jpg, 0, all, 8, jpg.Length);
                    string path = Path.Combine(c.Root, "sessions", R.Sid, "frame.bin"), tmp = path + ".tmp";
                    File.WriteAllBytes(tmp, TeamSecurity.Seal(key, all));
                    TeamClient.Retry(() => { if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path); });
                    Frames++;
                }
            }
            ReadInput();
            return true;
        }
        void ReadInput()
        {
            string f = Path.Combine(c.Root, "sessions", R.Sid, "input.jsonl");
            if (!File.Exists(f)) return;
            long len = new FileInfo(f).Length; if (len <= inputOffset) return;
            byte[] buf;
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { fs.Seek(inputOffset, SeekOrigin.Begin); buf = new byte[len - inputOffset]; int r = 0; while (r < buf.Length) { int k = fs.Read(buf, r, buf.Length - r); if (k <= 0) break; r += k; } }
            int last = Array.LastIndexOf(buf, (byte)'\n'); if (last < 0) return;
            inputOffset += last + 1;
            foreach (var line in Encoding.UTF8.GetString(buf, 0, last + 1).Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                RemoteInput e;
                try { e = TeamClient.FromJson<RemoteInput>(TeamSecurity.OpenText(key, line.Trim())); } catch { Rejected++; continue; }   // not from the helper's key
                if (A.Mode != "control" || e.N <= lastN) { Rejected++; continue; }   // view only, or replayed
                lastN = e.N; Inputs++;
                try { TeamRemote.Inject(e); } catch (Exception ex) { TeamClient.Log("Remote input: " + ex.Message); }
            }
        }
        void Loop()
        {
            while (running)
            {
                try { if (!Step(true)) break; } catch (Exception ex) { TeamClient.Log("Remote host: " + ex.Message); }
                Thread.Sleep(A.Mode == "control" ? 150 : 300);
            }
        }
    }
}
