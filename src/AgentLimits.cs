using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // 5-hour and weekly usage limits of the agent CLIs, read from what the CLIs themselves write (read-only,
    // no sign-in files are opened):
    //  - Claude Code: the status line saves rate_limits.five_hour / seven_day per session in
    //    %APPDATA%\herdr\claude-cost\*.json (newest file per account wins).
    //  - Codex: rate_limits.primary / secondary (window_minutes 300 = 5 hours, 10080 = 1 week) in its session
    //    logs under %USERPROFILE%\.codex\sessions.
    //  - Antigravity, Cline, OpenCode: they do not write their limits anywhere readable: shown as "not reported".
    // ===================================================================================================

    class LimitRow
    {
        public string Agent = "", Account = "", Note = "";
        public double? FiveHour, Weekly;
        public DateTime? FiveResets, WeekResets, Observed;
        public bool Known { get { return FiveHour != null || Weekly != null; } }
        public bool High { get { return (FiveHour ?? 0) >= 90 || (Weekly ?? 0) >= 90; } }
    }

    static class AgentLimits
    {
        static List<LimitRow> last = new List<LimitRow>();
        static DateTime lastAt;
        static bool reading;
        public static event Action Updated;
        public static List<LimitRow> Last
        {
            get
            {
                List<LimitRow> rows; lock (last) rows = last.ToList();
                lock (live)
                    foreach (var r in live.Values)
                    {
                        int i = rows.FindIndex(x => x.Agent == r.Agent && x.Account == r.Account);
                        if (i < 0) rows.Add(r);
                        else if (!rows[i].Known || (rows[i].Observed ?? DateTime.MinValue) < (r.Observed ?? DateTime.MinValue)) rows[i] = r;
                    }
                return rows;
            }
        }

        // Readings a running agent reports itself (Claude's rate_limit_event, Codex's token_count): newer than any file.
        static readonly Dictionary<string, LimitRow> live = new Dictionary<string, LimitRow>();
        public static void Report(LimitRow r)
        {
            if (r == null || !r.Known) return;
            if (r.Account.Length == 0) r.Account = "main";
            if (r.Observed == null) r.Observed = DateTime.Now;
            lock (live)
            {
                LimitRow old;
                if (live.TryGetValue(r.Agent + "|" + r.Account, out old))
                {   // one event may carry only one window: keep the other one
                    if (r.FiveHour == null) { r.FiveHour = old.FiveHour; r.FiveResets = old.FiveResets; }
                    if (r.Weekly == null) { r.Weekly = old.Weekly; r.WeekResets = old.WeekResets; }
                }
                live[r.Agent + "|" + r.Account] = r;
            }
            var h = Updated; if (h != null) try { App.I.Sync.BeginInvoke(h); } catch { }
        }
        // The limits of one agent CLI and account ("" = the default account), or null when nothing is known.
        public static LimitRow Find(string agentId, string account)
        {
            string name = agentId == "claude" ? "Claude Code" : agentId == "codex" ? "Codex" : null;
            if (name == null) return null;
            string acc = string.IsNullOrEmpty(account) ? "main" : account;
            return Last.FirstOrDefault(x => x.Agent == name && x.Account == acc && x.Known);
        }

        // Fresh enough (1 minute) or read again in the background; Updated fires on the UI thread.
        public static void Refresh(bool force)
        {
            if (reading || (!force && DateTime.Now - lastAt < TimeSpan.FromSeconds(60))) return;
            reading = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LimitRow> rows;
                try { rows = ReadAll(); } catch (Exception ex) { App.Log("Limits: " + ex.Message); rows = new List<LimitRow>(); }
                lock (last) { last = rows; }
                lastAt = DateTime.Now; reading = false;
                var h = Updated; if (h != null) try { App.I.Sync.BeginInvoke(h); } catch { }
            });
        }

        public static List<LimitRow> ReadAll()
        {
            var rows = new List<LimitRow>();
            rows.AddRange(Claude());
            rows.Add(Codex(AgentAccounts.Home("codex", ""), "main"));
            foreach (var name in AgentAccounts.Names("codex")) rows.Add(Codex(AgentAccounts.Home("codex", name), name));
            foreach (var n in new[] { "Antigravity", "Cline", "OpenCode" })
                rows.Add(new LimitRow { Agent = n, Account = "", Note = "not reported: this CLI does not save its limits where they can be read (see its own app or website)" });
            return rows;
        }

        static IEnumerable<LimitRow> Claude()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "herdr", "claude-cost");
            var js = new JavaScriptSerializer();
            var best = new Dictionary<string, Tuple<DateTime, Dictionary<string, object>>>();
            if (Directory.Exists(dir))
                foreach (var f in new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(x => x.LastWriteTime).Take(300))
                {
                    Dictionary<string, object> d;
                    try { d = js.DeserializeObject(File.ReadAllText(f.FullName)) as Dictionary<string, object>; } catch { continue; }
                    if (d == null || !d.ContainsKey("rate_limits") || !(d["rate_limits"] is Dictionary<string, object>)) continue;
                    object cd; d.TryGetValue("config_dir", out cd);
                    string acc = Convert.ToString(cd ?? "").TrimEnd('\\', '/');
                    acc = acc.Length == 0 ? "main" : Path.GetFileName(acc);
                    if (!best.ContainsKey(acc)) best[acc] = Tuple.Create(f.LastWriteTime, d);
                }
            if (best.Count == 0) { yield return new LimitRow { Agent = "Claude Code", Account = "main", Note = "no reading yet: Claude Code's status line has not saved one (use Claude Code once)" }; yield break; }
            foreach (var kv in best.OrderBy(k => k.Key == "main" ? "" : k.Key))
            {
                var rl = (Dictionary<string, object>)kv.Value.Item2["rate_limits"];
                var r = new LimitRow { Agent = "Claude Code", Account = kv.Key, Observed = kv.Value.Item1 };
                Window(rl, "five_hour", out r.FiveHour, out r.FiveResets);
                Window(rl, "seven_day", out r.Weekly, out r.WeekResets);
                yield return r;
            }
        }

        static void Window(Dictionary<string, object> rl, string key, out double? used, out DateTime? resets)
        {
            used = null; resets = null;
            object o; if (!rl.TryGetValue(key, out o)) return;
            var w = o as Dictionary<string, object>; if (w == null) return;
            object u, at;
            if (w.TryGetValue("used_percentage", out u) && u != null) used = Convert.ToDouble(u, System.Globalization.CultureInfo.InvariantCulture);
            if (w.TryGetValue("resets_at", out at) && at != null) { long s = Convert.ToInt64(at); if (s > 0) resets = DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime; }
            if (resets != null && resets <= DateTime.Now) used = 0;   // the window has reset since the reading
        }

        // One Codex account: the newest rate_limits in the session logs under its home folder.
        static LimitRow Codex(string home, string account)
        {
            var r = new LimitRow { Agent = "Codex", Account = account };
            string dir = Path.Combine(home, "sessions");
            if (!Directory.Exists(dir)) { r.Note = "no Codex session logs yet for this account"; return r; }
            var files = new DirectoryInfo(dir).GetFiles("rollout-*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).Take(25);
            string plan = null;
            foreach (var f in files)
            {
                foreach (var line in Tail(f.FullName, 1024 * 1024).Where(l => l.Contains("\"rate_limits\"")).Reverse())
                {
                    var read = CodexLine(line, f.LastWriteTime, account);
                    if (read == null) continue;
                    if (read.Note.Length > 0 && plan == null) plan = read.Note;
                    if (read.Known) { if (plan != null) read.Note = "plan: " + plan; return read; }
                }
            }
            r.Note = "Codex reports no 5-hour or weekly window" + (plan != null ? " for this plan (" + plan + ")" : "") + " in its recent logs";
            return r;
        }

        // A rollout line with payload.rate_limits -> a row (Note = plan type), or null. Also used for live readings.
        internal static LimitRow CodexLine(string line, DateTime written, string account)
        {
            Dictionary<string, object> ev;
            try { ev = Json.DeserializeObject(line) as Dictionary<string, object>; } catch { return null; }
            var rl = Get(Get(ev, "payload"), "rate_limits");
            if (rl == null) return null;
            var r = new LimitRow { Agent = "Codex", Account = account, Observed = written };
            object pt; if (rl.TryGetValue("plan_type", out pt) && pt != null) r.Note = Convert.ToString(pt);
            foreach (var name in new[] { "primary", "secondary" })
            {
                var w = Get(rl, name); if (w == null) continue;
                object u, mins, at, inSec;
                if (!w.TryGetValue("used_percent", out u) || u == null) continue;
                double used = Convert.ToDouble(u, System.Globalization.CultureInfo.InvariantCulture);
                int m = w.TryGetValue("window_minutes", out mins) && mins != null ? Convert.ToInt32(mins) : 0;
                DateTime? reset = null;
                if (w.TryGetValue("resets_at", out at) && at != null && Convert.ToInt64(at) > 0) reset = DateTimeOffset.FromUnixTimeSeconds(Convert.ToInt64(at)).LocalDateTime;
                else if (w.TryGetValue("resets_in_seconds", out inSec) && inSec != null) reset = written.AddSeconds(Convert.ToDouble(inSec));
                if (reset != null && reset <= DateTime.Now) used = 0;
                if (m == 300 || (m == 0 && name == "primary")) { r.FiveHour = used; r.FiveResets = reset; }
                else { r.Weekly = used; r.WeekResets = reset; }
            }
            return r;
        }
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // Codex context of one thread: the newest token_count in its rollout file -> (tokens in context, window).
        // Also reports that account's limits from the same line. Runs on a worker thread.
        public static bool CodexContext(string account, string threadId, out long used, out long window)
        {
            used = 0; window = 0;
            if (string.IsNullOrEmpty(threadId) || threadId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            string dir = Path.Combine(AgentAccounts.Home("codex", account), "sessions");
            if (!Directory.Exists(dir)) return false;
            FileInfo file;
            lock (rollouts) rollouts.TryGetValue(threadId, out file);
            if (file == null || !file.Exists)
            {
                file = new DirectoryInfo(dir).GetFiles("rollout-*" + threadId + ".jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (file == null) return false;
                lock (rollouts) rollouts[threadId] = file;
            }
            file.Refresh();
            foreach (var line in Tail(file.FullName, 512 * 1024).Where(l => l.Contains("\"token_count\"")).Reverse())
            {
                Dictionary<string, object> ev;
                try { ev = Json.DeserializeObject(line) as Dictionary<string, object>; } catch { continue; }
                var info = Get(Get(ev, "payload"), "info");
                var lastUse = Get(info, "last_token_usage");
                object w, t;
                if (lastUse == null || !lastUse.TryGetValue("total_tokens", out t) || t == null) continue;
                used = Convert.ToInt64(t);
                if (info.TryGetValue("model_context_window", out w) && w != null) window = Convert.ToInt64(w);
                var limits = CodexLine(line, file.LastWriteTime, string.IsNullOrEmpty(account) ? "main" : account);
                if (limits != null && limits.Known) { limits.Note = ""; Report(limits); }
                return true;
            }
            return false;
        }
        static readonly Dictionary<string, FileInfo> rollouts = new Dictionary<string, FileInfo>();

        static Dictionary<string, object> Get(Dictionary<string, object> d, string k) { object o; return d != null && d.TryGetValue(k, out o) ? o as Dictionary<string, object> : null; }

        // The last part of a (possibly huge) log file, as lines.
        static IEnumerable<string> Tail(string file, int bytes)
        {
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - bytes); fs.Position = start;
                    var buf = new byte[fs.Length - start]; int n = 0; while (n < buf.Length) { int k = fs.Read(buf, n, buf.Length - n); if (k <= 0) break; n += k; }
                    var lines = Encoding.UTF8.GetString(buf, 0, n).Split('\n').ToList();
                    if (start > 0 && lines.Count > 0) lines.RemoveAt(0);   // cut in the middle
                    return lines;
                }
            }
            catch { return new string[0]; }
        }

        // "Claude 5h 52% · wk 89%   Codex 5h 10% · wk 60%"
        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var r in Last.Where(x => x.Known))
                parts.Add(r.Agent.Replace(" Code", "") + (r.Account != "main" && r.Account.Length > 0 ? " (" + r.Account + ")" : "") + "  5h " + Pct(r.FiveHour) + " · wk " + Pct(r.Weekly));
            return parts.Count == 0 ? "Limits: no reading yet" : string.Join("     ", parts);
        }
        public static string Pct(double? v) { return v == null ? "-" : Math.Round(v.Value) + "%"; }
        public static string When(DateTime? t) { return t == null ? "-" : t.Value.Date == DateTime.Today ? t.Value.ToString("HH:mm") : t.Value.ToString("ddd HH:mm"); }
    }

    // ---------- the limits window ----------
    class LimitsDialog : Form
    {
        readonly ListView lv;
        public LimitsDialog()
        {
            Text = "Agent usage limits"; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            ClientSize = new Size(Ui.S(980), Ui.S(360)); Font = Ui.Base; BackColor = Ui.Bg; ForeColor = Ui.Text;
            lv = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None };
            foreach (var c in new[] { new[] { "Agent", "130" }, new[] { "Account", "110" }, new[] { "5 hours", "80" }, new[] { "resets", "100" }, new[] { "Weekly", "80" }, new[] { "resets", "110" }, new[] { "Reading from", "110" }, new[] { "Note", "300" } })
                lv.Columns.Add(c[0], Ui.S(int.Parse(c[1])));
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Ui.S(46), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.S(6)) };
            bar.Controls.Add(Ui.Btn("Close", delegate { Close(); }));
            bar.Controls.Add(Ui.Btn("Refresh", delegate { AgentLimits.Refresh(true); }, true));
            bar.Controls.Add(new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, Ui.S(9), Ui.S(12), 0), Text = "Red = 90% or more" });
            Controls.Add(lv); Controls.Add(bar);
            Ui.Theme(this);
            Action fill = Fill;
            AgentLimits.Updated += fill;
            FormClosed += delegate { AgentLimits.Updated -= fill; };
            Fill();
            AgentLimits.Refresh(true);
        }
        void Fill()
        {
            if (IsDisposed) return;
            lv.BeginUpdate(); lv.Items.Clear();
            foreach (var r in AgentLimits.Last)
            {
                var it = new ListViewItem(new[] { r.Agent, r.Account, AgentLimits.Pct(r.FiveHour), AgentLimits.When(r.FiveResets), AgentLimits.Pct(r.Weekly), AgentLimits.When(r.WeekResets), r.Observed == null ? "-" : AgentLimits.When(r.Observed), r.Note });
                if (r.High) it.ForeColor = Ui.Danger; else if (!r.Known) it.ForeColor = Ui.Muted;
                lv.Items.Add(it);
            }
            lv.EndUpdate();
        }
    }
}
