using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace StickyPro
{
    public class CronJob
    {
        public string id = "";
        public string name = "";
        public string schedule = "every:15m"; // every:15m | daily:08:00 | once:2026-11-21T20:00
        public string prompt = "";
        public string skill = "";
        public string precheck_type = "none"; // none | folder_changed | sql_nonzero | overdue_tasks
        public string precheck_arg = "";
        public bool enabled = true;
        public string last_run_utc = "";
        public string last_hash = "";
        public bool one_shot;
        public string tool = "";
        public Dictionary<string, object> tool_args;
        public string agent = "";
        public string folder = "";
        public string last_run_id = "";
        public string run_status = "";
        public string run_output = "";
        public string run_error = "";
        public string run_finished_utc = "";
    }

    // Single lightweight tray timer + FileSystemWatcher event hooks:
    // - Standing routines + one-shot dynamic follow-ups (data\assistant\cron\jobs.json)
    // - 0-token C# pre-checks with [SILENT] suppression + SqliteVault.alert_dedup
    // - Idle-only HEARTBEAT.md + GOALS.md evaluator returning HEARTBEAT_OK when clean
    // - FileSystemWatcher folder hooks (1.5 s debounce -> SqliteVault.QueueExtract)
    static class CronEngine
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(6);

        static readonly object stateLock = new object();
        static readonly object watchLock = new object();

        static System.Threading.Timer tickTimer;
        static System.Threading.Timer debounceTimer;
        static volatile bool running;
        static int tickBusy;
        static readonly Queue<CronJob> actions = new Queue<CronJob>();
        static CancellationTokenSource runCancellation = new CancellationTokenSource();
        static volatile bool actionWorker;
        static bool actionsRecovered;

        static readonly Dictionary<string, FileSystemWatcher> watchers = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, DateTime> pendingWatchFiles = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        static int recentWatchedEvents;

        static string lastHeartbeatStatus = "HEARTBEAT_OK";
        static string lastHeartbeatUtc = "";
        static List<string> lastHeartbeatAlerts = new List<string>();

        public static event Action<CronJob, string> JobTriggered;

        public static string CronDir { get { return Path.Combine(AssistantHarness.StorageDir, "cron"); } }
        public static string JobsFile { get { return Path.Combine(CronDir, "jobs.json"); } }
        public static string WatchesFile { get { return Path.Combine(CronDir, "watches.json"); } }

        // =====================================================================
        // Lifecycle
        // =====================================================================

        public static void Start()
        {
            lock (stateLock)
            {
                if (running) return;
                if (runCancellation.IsCancellationRequested) runCancellation = new CancellationTokenSource();
                RecoverActionsLocked();
                running = true;
                tickTimer = new System.Threading.Timer(OnTick, null, 15000, 60000);
            }
            RestoreWatchers();
        }

        public static void Stop()
        {
            lock (stateLock)
            {
                running = false;
                actionsRecovered = false;
                runCancellation.Cancel();
                actions.Clear();
                if (tickTimer != null)
                {
                    try { tickTimer.Dispose(); } catch (Exception) { }
                    tickTimer = null;
                }
            }
            lock (watchLock)
            {
                if (debounceTimer != null)
                {
                    try { debounceTimer.Dispose(); } catch (Exception) { }
                    debounceTimer = null;
                }
                pendingWatchFiles.Clear();
                foreach (var kv in watchers)
                {
                    try { kv.Value.EnableRaisingEvents = false; kv.Value.Dispose(); }
                    catch (Exception) { }
                }
                watchers.Clear();
            }
        }

        static void OnTick(object state)
        {
            if (!running) return;
            if (Interlocked.CompareExchange(ref tickBusy, 1, 0) != 0) return;
            try
            {
                RunDueJobs();
                RunHeartbeat(false);
            }
            catch (Exception ex)
            {
                App.Log("CronEngine tick: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref tickBusy, 0);
            }
        }

        // =====================================================================
        // Cron CRUD & Management (cron_manage MCP tool)
        // =====================================================================

        public static List<CronJob> LoadJobs()
        {
            lock (stateLock)
            {
                return LoadJobsLocked();
            }
        }

        static List<CronJob> LoadJobsLocked()
        {
            string json;
            try { json = File.ReadAllText(JobsFile, Encoding.UTF8); }
            catch (FileNotFoundException) { return new List<CronJob>(); }
            catch (DirectoryNotFoundException) { return new List<CronJob>(); }
            var jobs = Js.Deserialize<List<CronJob>>(json);
            if (jobs == null) throw new InvalidDataException("Empty schedule data in " + JobsFile);
            jobs.RemoveAll(j => j == null);
            return jobs;
        }

        static void SaveJobsLocked(List<CronJob> jobs)
        {
            Directory.CreateDirectory(CronDir);
            string tmp = JobsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, Js.Serialize(jobs), Utf8NoBom);
                if (File.Exists(JobsFile)) File.Replace(tmp, JobsFile, null, true);
                else File.Move(tmp, JobsFile);
            }
            finally { try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        public static Dictionary<string, object> Manage(
            string action,
            string id = "",
            string name = "",
            string schedule = "",
            string prompt = null,
            string skill = null,
            string precheckType = "",
            string precheckArg = null,
            bool? enabled = null,
            bool? oneShot = null,
            int inMinutes = 0,
            string tool = null,
            Dictionary<string, object> toolArgs = null,
            string agent = null,
            string folder = null)
        {
            string act = string.IsNullOrWhiteSpace(action) ? "list" : action.Trim().ToLowerInvariant();

            if (act == "list")
            {
                var jobs = LoadJobs();
                return new Dictionary<string, object>
                {
                    { "action", "list" },
                    { "count", jobs.Count },
                    { "jobs", jobs.Select(JobToDict).ToList() },
                    { "watched_folders", WatchedFolders() },
                    { "last_heartbeat_status", lastHeartbeatStatus },
                    { "last_heartbeat_utc", lastHeartbeatUtc }
                };
            }

            if (act == "create" || act == "follow_up")
            {
                bool isFollowUp = act == "follow_up";
                string sched = (schedule ?? "").Trim();
                if (isFollowUp && sched.Length == 0)
                {
                    int mins = inMinutes > 0 ? inMinutes : 15;
                    sched = "once:" + DateTime.Now.AddMinutes(mins).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
                }
                if (sched.Length == 0) sched = "every:15m";
                ValidateSchedule(sched);

                string pcType = NormalizePrecheckType(precheckType);
                var job = new CronJob
                {
                    id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N").Substring(0, 10) : id.Trim(),
                    name = string.IsNullOrWhiteSpace(name) ? (isFollowUp ? "Follow-up check" : "Scheduled check") : name.Trim(),
                    schedule = sched,
                    prompt = (prompt ?? "").Trim(),
                    skill = (skill ?? "").Trim(),
                    tool = (tool ?? "").Trim(),
                    tool_args = toolArgs,
                    agent = (agent ?? "").Trim(),
                    folder = (folder ?? "").Trim(),
                    precheck_type = pcType,
                    precheck_arg = (precheckArg ?? "").Trim(),
                    enabled = enabled ?? true,
                    last_run_utc = "",
                    last_hash = "",
                    one_shot = oneShot ?? (isFollowUp || sched.StartsWith("once:", StringComparison.OrdinalIgnoreCase))
                };

                lock (stateLock)
                {
                    var jobs = LoadJobsLocked();
                    EnsureEditable(FindJob(jobs, job.id));
                    ValidateAction(job);
                    jobs.RemoveAll(j => string.Equals(j.id, job.id, StringComparison.OrdinalIgnoreCase));
                    jobs.Add(job);
                    SaveJobsLocked(jobs);
                }

                AssistantHarness.Report("Scheduled cron: " + job.name + " (" + job.schedule + ")");
                return new Dictionary<string, object>
                {
                    { "action", act },
                    { "created", true },
                    { "job", JobToDict(job) }
                };
            }

            if (act == "update")
            {
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required for cron_manage update.");
                CronJob updated = null;
                lock (stateLock)
                {
                    var jobs = LoadJobsLocked();
                    updated = jobs.FirstOrDefault(j => string.Equals(j.id, id.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (updated == null) throw new KeyNotFoundException("No cron job with id: " + id);
                    EnsureEditable(updated);
                    if (!string.IsNullOrWhiteSpace(name)) updated.name = name.Trim();
                    if (!string.IsNullOrWhiteSpace(schedule) && updated.schedule != schedule.Trim())
                    {
                        ValidateSchedule(schedule.Trim());
                        updated.schedule = schedule.Trim();
                        updated.last_run_utc = "";
                    }
                    if (prompt != null) updated.prompt = prompt.Trim();
                    if (skill != null) updated.skill = skill.Trim();
                    if (tool != null) updated.tool = tool.Trim();
                    if (toolArgs != null) updated.tool_args = toolArgs;
                    if (agent != null) updated.agent = agent.Trim();
                    if (folder != null) updated.folder = folder.Trim();
                    string check = string.IsNullOrWhiteSpace(precheckType) ? updated.precheck_type : NormalizePrecheckType(precheckType);
                    string checkArg = precheckArg == null ? updated.precheck_arg : precheckArg.Trim();
                    if (check != updated.precheck_type || checkArg != updated.precheck_arg) updated.last_hash = "";
                    updated.precheck_type = check;
                    updated.precheck_arg = checkArg;
                    if (enabled == true && !updated.enabled && updated.one_shot) updated.last_run_utc = "";
                    if (enabled.HasValue) updated.enabled = enabled.Value;
                    if (oneShot.HasValue) updated.one_shot = oneShot.Value;
                    ValidateAction(updated);
                    SaveJobsLocked(jobs);
                }
                return new Dictionary<string, object>
                {
                    { "action", "update" },
                    { "updated", true },
                    { "job", JobToDict(updated) }
                };
            }

            if (act == "delete")
            {
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required for cron_manage delete.");
                int removed;
                lock (stateLock)
                {
                    var jobs = LoadJobsLocked();
                    EnsureEditable(FindJob(jobs, id.Trim()));
                    removed = jobs.RemoveAll(j => string.Equals(j.id, id.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (removed > 0) SaveJobsLocked(jobs);
                }
                return new Dictionary<string, object>
                {
                    { "action", "delete" },
                    { "id", id.Trim() },
                    { "deleted", removed > 0 }
                };
            }

            if (act == "run_now" || act == "retry")
            {
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required for cron_manage run_now.");
                CronJob target;
                lock (stateLock)
                {
                    if (!runCancellation.IsCancellationRequested) RecoverActionsLocked();
                    var jobs = LoadJobsLocked();
                    target = jobs.FirstOrDefault(j => string.Equals(j.id, id.Trim(), StringComparison.OrdinalIgnoreCase));
                }
                if (target == null) throw new KeyNotFoundException("No cron job with id: " + id);
                if (act == "retry" && target.run_status != "failed" && target.run_status != "interrupted")
                    throw new InvalidOperationException("Retry is available for failed or interrupted runs.");
                return EvaluateJobCore(target, true, act == "retry");
            }

            throw new ArgumentException("Unknown cron_manage action: " + action + ". Use list, create, follow_up, update, delete, run_now, or retry.");
        }

        // =====================================================================
        // 0-Token Pre-Check + [SILENT] Evaluation Engine
        // =====================================================================

        public static List<Dictionary<string, object>> RunDueJobs()
        {
            var results = new List<Dictionary<string, object>>();
            var jobs = LoadJobs();
            DateTime nowUtc = DateTime.UtcNow;
            for (int i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                if (job == null || !job.enabled) continue;
                try
                {
                    if (IsJobDue(job, nowUtc)) results.Add(EvaluateJob(job, false));
                }
                catch (Exception ex) { App.Log("Cron job " + job.id + ": " + ex.Message); }
            }
            return results;
        }

        public static Dictionary<string, object> EvaluateJob(CronJob job, bool ignoreSchedule)
        {
            return EvaluateJobCore(job, ignoreSchedule, false);
        }

        static Dictionary<string, object> EvaluateJobCore(CronJob job, bool ignoreSchedule, bool retry)
        {
            if (job == null) throw new ArgumentNullException("job");
            lock (stateLock) if (!runCancellation.IsCancellationRequested) RecoverActionsLocked();
            if (IsActive(job)) return NotTriggered(job, "BUSY", "A run is already queued or running.");

            string pcType = NormalizePrecheckType(job.precheck_type);
            bool hasSignal;
            string precheckHash, detail;
            RunZeroTokenPrecheck(job, pcType, out hasSignal, out precheckHash, out detail, retry);
            if (!hasSignal) return NotTriggered(job, "[SILENT]", detail);

            bool execute = CronExecution.HasAction(job);
            string alertHash = Sha256Short("cron|" + job.id + "|" + precheckHash);
            lock (stateLock)
            {
                var jobs = LoadJobsLocked();
                var stored = FindJob(jobs, job.id);
                if (stored == null || Js.Serialize(stored) != Js.Serialize(job))
                    return NotTriggered(job, "BUSY", "The schedule changed while it was being checked.");
                if (!ignoreSchedule && (!stored.enabled || !IsJobDue(stored, DateTime.UtcNow)))
                    return NotTriggered(job, "[SILENT]", "The schedule is no longer due.");
                if (runCancellation.IsCancellationRequested)
                    return NotTriggered(job, "BUSY", "The scheduler is stopping.");

                bool deduplicate = !retry && pcType != "none";
                if (deduplicate && SqliteVault.HasAlerted(alertHash, AlertCooldown))
                {
                    var silent = NotTriggered(job, "[SILENT]", "Already alerted within cooldown window.");
                    silent["deduped"] = true;
                    silent["hash"] = alertHash;
                    return silent;
                }

                stored.last_run_utc = UtcNow();
                stored.last_hash = precheckHash;
                stored.last_run_id = Guid.NewGuid().ToString("N");
                stored.run_status = execute ? "queued" : "notified";
                stored.run_output = "";
                stored.run_error = "";
                stored.run_finished_utc = execute ? "" : stored.last_run_utc;
                if (stored.one_shot) stored.enabled = false;
                SaveJobsLocked(jobs);
                job = stored;
                if (deduplicate)
                {
                    try { SqliteVault.MarkAlerted(alertHash, "cron:" + job.id); }
                    catch (Exception ex) { App.Log("Record schedule alert: " + ex.Message); }
                }
                if (execute) QueueActionLocked(job);
            }

            if (!execute)
            {
                AssistantHarness.Report("Cron triggered: " + job.name);
                NotifySafe("Scheduled routine: " + job.name, detail);
            }
            var handler = JobTriggered;
            if (handler != null)
                try { handler(job, detail); }
                catch (Exception ex) { App.Log("Cron notification: " + ex.Message); }

            return new Dictionary<string, object>
            {
                { "job_id", job.id }, { "name", job.name }, { "triggered", true },
                { "status", "TRIGGERED" }, { "detail", detail }, { "hash", alertHash },
                { "run_id", job.last_run_id }, { "run_status", job.run_status },
                { "tokens_used", execute && string.IsNullOrWhiteSpace(job.tool) ? (object)null : 0 }
            };
        }

        static Dictionary<string, object> NotTriggered(CronJob job, string status, string reason)
        {
            return new Dictionary<string, object>
            {
                { "job_id", job.id }, { "name", job.name }, { "triggered", false },
                { "status", status }, { "reason", reason }, { "tokens_used", 0 }
            };
        }

        static CronJob FindJob(List<CronJob> jobs, string id)
        {
            return jobs.FirstOrDefault(j => string.Equals(j.id, id, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsActive(CronJob job) { return job != null && (job.run_status == "queued" || job.run_status == "running"); }

        static void EnsureEditable(CronJob job)
        {
            if (IsActive(job)) throw new InvalidOperationException("Wait for the queued or running job to finish first.");
        }

        static void ValidateAction(CronJob job)
        {
            if (!string.IsNullOrWhiteSpace(job.tool) && (!string.IsNullOrWhiteSpace(job.prompt) || !string.IsNullOrWhiteSpace(job.skill)))
                throw new ArgumentException("Choose a registered tool or a prompt/skill for each job.");
            string check = NormalizePrecheckType(job.precheck_type);
            if (check != "none" && check != "folder_changed" && check != "sql_nonzero" && check != "overdue_tasks")
                throw new ArgumentException("Unknown pre-check: " + job.precheck_type);
        }

        static string UtcNow() { return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture); }

        static void RecoverActionsLocked()
        {
            if (actionsRecovered) return;
            var jobs = LoadJobsLocked();
            var interrupted = jobs.Where(j => j.run_status == "running").ToList();
            foreach (var job in interrupted)
            {
                job.run_status = "interrupted";
                job.run_error = "The app stopped during execution. Check any output before retrying.";
                job.run_finished_utc = UtcNow();
            }
            if (interrupted.Count > 0) SaveJobsLocked(jobs);
            foreach (var job in jobs.Where(j => j.run_status == "queued")) QueueActionLocked(job);
            actionsRecovered = true;
        }

        static void QueueActionLocked(CronJob job)
        {
            actions.Enqueue(job);
            if (actionWorker) return;
            actionWorker = true;
            ThreadPool.QueueUserWorkItem(delegate { WorkActions(); });
        }

        static void WorkActions()
        {
            while (true)
            {
                CronJob job;
                CancellationToken cancellation;
                lock (stateLock)
                {
                    if (actions.Count == 0 || runCancellation.IsCancellationRequested)
                    {
                        actionWorker = false;
                        return;
                    }
                    job = actions.Dequeue();
                    cancellation = runCancellation.Token;
                }

                try
                {
                    if (!SaveRunState(job, "queued", "running", "", "")) continue;
                    string output = "", error = "", status = "completed";
                    try { output = CronExecution.Run(job, cancellation); }
                    catch (OperationCanceledException) { status = "interrupted"; error = "The app stopped during execution."; }
                    catch (Exception ex) { status = "failed"; error = ex.Message; }
                    if (SaveRunState(job, "running", status, output, error) && !cancellation.IsCancellationRequested)
                    {
                        AssistantHarness.Report("Scheduled job " + status + ": " + job.name);
                        NotifySafe("Scheduled job " + status + ": " + job.name, error.Length > 0 ? error : output);
                    }
                }
                catch (Exception ex) { App.Log("Save scheduled run " + job.id + ": " + ex.Message); }
            }
        }

        static bool SaveRunState(CronJob run, string from, string status, string output, string error)
        {
            lock (stateLock)
            {
                var jobs = LoadJobsLocked();
                var job = FindJob(jobs, run.id);
                if (job == null || job.last_run_id != run.last_run_id || job.run_status != from) return false;
                job.run_status = status;
                job.run_output = Cut(output, 24000);
                job.run_error = Cut(error, 4000);
                if (status != "running") job.run_finished_utc = UtcNow();
                SaveJobsLocked(jobs);
                return true;
            }
        }

        static void RunZeroTokenPrecheck(CronJob job, string pcType, out bool hasSignal, out string fingerprint, out string detail, bool retry)
        {
            hasSignal = false;
            fingerprint = "";
            detail = "";

            if (pcType == "none")
            {
                hasSignal = true;
                fingerprint = DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
                detail = "Schedule matched";
                return;
            }

            if (pcType == "folder_changed")
            {
                string folder = (job.precheck_arg ?? "").Trim();
                if (folder.Length == 0 || !Directory.Exists(folder))
                {
                    detail = "Watched folder missing: " + folder;
                    return;
                }
                var entries = new List<string>();
                foreach (var f in Directory.GetFiles(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        entries.Add(fi.Name + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks);
                    }
                    catch (Exception) { }
                }
                fingerprint = Sha256Short(string.Join("\n", entries.ToArray()));
                if (!retry && !string.IsNullOrEmpty(job.last_hash) && string.Equals(job.last_hash, fingerprint, StringComparison.Ordinal))
                {
                    detail = "No changes in " + folder;
                    return;
                }
                hasSignal = entries.Count > 0 || !string.IsNullOrEmpty(job.last_hash);
                detail = hasSignal ? ("Folder changed: " + entries.Count + " file(s) in " + Path.GetFileName(folder.TrimEnd('\\', '/'))) : ("Folder empty: " + folder);
                return;
            }

            if (pcType == "sql_nonzero")
            {
                string sql = (job.precheck_arg ?? "").Trim();
                if (sql.Length == 0)
                {
                    detail = "No SQL pre-check query configured.";
                    return;
                }
                var res = SqliteVault.QuerySql(sql, 50);
                var rows = res["rows"] as List<List<object>>;
                if (rows == null || rows.Count == 0)
                {
                    detail = "SQL pre-check returned 0 rows.";
                    return;
                }
                if (rows.Count == 1 && rows[0].Count == 1)
                {
                    object v = rows[0][0];
                    double num;
                    if (v == null || (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out num) && Math.Abs(num) < 1e-12))
                    {
                        detail = "SQL pre-check returned zero.";
                        return;
                    }
                }
                hasSignal = true;
                fingerprint = Sha256Short(Js.Serialize(rows));
                detail = "SQL pre-check matched " + rows.Count + " row(s)";
                return;
            }

            if (pcType == "overdue_tasks")
            {
                long now = DateTime.Now.Ticks;
                var overdue = (Store.Tasks ?? new List<TaskItem>())
                    .Where(t => t != null && !t.Done && t.Due > 0 && t.Due < now)
                    .OrderBy(t => t.Due)
                    .ToList();
                if (overdue.Count == 0)
                {
                    detail = "No overdue tasks.";
                    return;
                }
                hasSignal = true;
                fingerprint = Sha256Short(string.Join("|", overdue.Select(t => (t.Id ?? t.Title) + ":" + t.Due).ToArray()));
                detail = overdue.Count + " overdue task(s)";
                return;
            }

            detail = "Unknown precheck_type: " + pcType;
        }

        // =====================================================================
        // Idle-Only HEARTBEAT.md / GOALS.md Evaluator
        // =====================================================================

        public static bool IsAssistantBusy()
        {
            if (actionWorker) return true;
            try
            {
                if (AgentStore.Busy == null || AgentStore.Busy.Count == 0) return false;
                var chats = AgentStore.Chats;
                if (chats == null) return false;
                for (int i = 0; i < chats.Count; i++)
                {
                    var c = chats[i];
                    if (c != null && AssistantHarness.IsAssistant(c) && AgentStore.Busy.Contains(c.Id))
                        return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        public static Dictionary<string, object> RunHeartbeat(bool force = false)
        {
            AgentMemory.EnsureSeeded();
            if (!force && IsAssistantBusy())
            {
                return new Dictionary<string, object>
                {
                    { "status", "SKIPPED_BUSY" },
                    { "idle", false },
                    { "alerts", new List<string>() },
                    { "tokens_used", 0 }
                };
            }

            var newAlerts = new List<string>();
            long now = DateTime.Now.Ticks;

            // 1. Overdue tasks signal (0 tokens)
            try
            {
                var overdue = (Store.Tasks ?? new List<TaskItem>())
                    .Where(t => t != null && !t.Done && t.Due > 0 && t.Due < now)
                    .ToList();
                if (overdue.Count > 0)
                {
                    string h = Sha256Short("hb:overdue:" + string.Join(",", overdue.Select(t => t.Id ?? t.Title).OrderBy(x => x).ToArray()));
                    if (!SqliteVault.HasAlerted(h, AlertCooldown))
                    {
                        SqliteVault.MarkAlerted(h, "heartbeat:overdue_tasks");
                        newAlerts.Add(overdue.Count + " overdue task(s): " + overdue[0].Title);
                    }
                }
            }
            catch (Exception) { }

            // 2. Fixed local drives > 90% full (never touches network drives!)
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed) continue;
                    if (!d.IsReady || d.TotalSize <= 0) continue;
                    double usedPct = (1.0 - ((double)d.AvailableFreeSpace / d.TotalSize)) * 100.0;
                    if (usedPct >= 90.0)
                    {
                        string h = Sha256Short("hb:disk:" + d.Name + ":" + Math.Floor(usedPct));
                        if (!SqliteVault.HasAlerted(h, TimeSpan.FromHours(12)))
                        {
                            SqliteVault.MarkAlerted(h, "heartbeat:disk");
                            double freeGb = d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                            newAlerts.Add("Drive " + d.Name + " is " + Math.Round(usedPct, 1).ToString(CultureInfo.InvariantCulture) + "% full (" + freeGb.ToString("0.0", CultureInfo.InvariantCulture) + " GB free)");
                        }
                    }
                }
            }
            catch (Exception) { }

            // 3. Watched folders activity
            int watchedDelta = Interlocked.Exchange(ref recentWatchedEvents, 0);
            if (watchedDelta > 0)
            {
                string h = Sha256Short("hb:watched:" + DateTime.UtcNow.ToString("yyyyMMddHH", CultureInfo.InvariantCulture) + ":" + watchedDelta);
                if (!SqliteVault.HasAlerted(h, TimeSpan.FromMinutes(30)))
                {
                    SqliteVault.MarkAlerted(h, "heartbeat:watched");
                    newAlerts.Add("Auto-queued " + watchedDelta + " updated document(s) from watched folders.");
                }
            }

            lastHeartbeatUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            lastHeartbeatAlerts = newAlerts;
            lastHeartbeatStatus = newAlerts.Count == 0 ? "HEARTBEAT_OK" : "ALERT";

            if (newAlerts.Count > 0)
            {
                string joined = string.Join(" · ", newAlerts.ToArray());
                AssistantHarness.Report("Heartbeat: " + joined);
                NotifySafe("All in One Companion", joined);
            }

            return new Dictionary<string, object>
            {
                { "status", lastHeartbeatStatus },
                { "idle", true },
                { "checked_utc", lastHeartbeatUtc },
                { "alerts", newAlerts },
                { "watched_folders", WatchedFolders() },
                { "jobs_count", LoadJobs().Count },
                { "tokens_used", 0 }
            };
        }

        public static Dictionary<string, object> HeartbeatStatus(bool runNow = false, bool force = false)
        {
            if (runNow) return RunHeartbeat(force);
            AgentMemory.EnsureSeeded();
            var snap = AgentMemory.GetSnapshot("__heartbeat__");
            return new Dictionary<string, object>
            {
                { "status", lastHeartbeatStatus },
                { "idle", !IsAssistantBusy() },
                { "last_checked_utc", lastHeartbeatUtc },
                { "last_alerts", lastHeartbeatAlerts.ToList() },
                { "watched_folders", WatchedFolders() },
                { "jobs_count", LoadJobs().Count },
                { "goals_preview", Cut(snap.Goals, 1000) },
                { "heartbeat_rules_preview", Cut(snap.Heartbeat, 1000) }
            };
        }

        // =====================================================================
        // FileSystemWatcher Event Hooks (1.5 s Debounce -> SqliteVault)
        // =====================================================================

        public static Dictionary<string, object> WatchFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.");
            string full = Path.GetFullPath(path.Trim());
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Folder not found: " + full);

            lock (watchLock)
            {
                AttachWatcherLocked(full);
                SaveWatchedFoldersLocked();
            }
            AssistantHarness.Report("Watching folder: " + Path.GetFileName(full.TrimEnd('\\', '/')));
            return new Dictionary<string, object>
            {
                { "watching", true },
                { "path", full },
                { "watched_folders", WatchedFolders() }
            };
        }

        public static Dictionary<string, object> UnwatchFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.");
            string full = Path.GetFullPath(path.Trim());
            bool removed = false;
            lock (watchLock)
            {
                FileSystemWatcher w;
                if (watchers.TryGetValue(full, out w))
                {
                    watchers.Remove(full);
                    try { w.EnableRaisingEvents = false; w.Dispose(); } catch (Exception) { }
                    removed = true;
                }
                SaveWatchedFoldersLocked();
            }
            return new Dictionary<string, object>
            {
                { "watching", false },
                { "path", full },
                { "removed", removed },
                { "watched_folders", WatchedFolders() }
            };
        }

        public static List<string> WatchedFolders()
        {
            lock (watchLock)
            {
                return watchers.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        static void RestoreWatchers()
        {
            try
            {
                if (!File.Exists(WatchesFile)) return;
                var list = Js.Deserialize<List<string>>(File.ReadAllText(WatchesFile, Encoding.UTF8));
                if (list == null) return;
                lock (watchLock)
                {
                    foreach (var p in list)
                    {
                        if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                            AttachWatcherLocked(Path.GetFullPath(p.Trim()));
                    }
                }
            }
            catch (Exception) { }
        }

        static void AttachWatcherLocked(string fullPath)
        {
            if (watchers.ContainsKey(fullPath)) return;
            var w = new FileSystemWatcher(fullPath)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            w.Created += OnWatchEvent;
            w.Changed += OnWatchEvent;
            w.Renamed += OnWatchRename;
            w.Error += delegate { };
            w.EnableRaisingEvents = true;
            watchers[fullPath] = w;
        }

        static void SaveWatchedFoldersLocked()
        {
            try
            {
                Directory.CreateDirectory(CronDir);
                string tmp = WatchesFile + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 6);
                File.WriteAllText(tmp, Js.Serialize(watchers.Keys.ToList()), Utf8NoBom);
                if (File.Exists(WatchesFile)) File.Replace(tmp, WatchesFile, null, true);
                else File.Move(tmp, WatchesFile);
            }
            catch (Exception) { }
        }

        static void OnWatchEvent(object sender, FileSystemEventArgs e)
        {
            ScheduleWatchDebounce(e.FullPath);
        }

        static void OnWatchRename(object sender, RenamedEventArgs e)
        {
            ScheduleWatchDebounce(e.FullPath);
        }

        static void ScheduleWatchDebounce(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            string name = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(name) || name.StartsWith("~$") || name.StartsWith(".")) return;
            if (!SqliteVault.IsSupportedDocument(filePath)) return;

            lock (watchLock)
            {
                pendingWatchFiles[filePath] = DateTime.UtcNow.AddMilliseconds(1500);
                if (debounceTimer == null)
                    debounceTimer = new System.Threading.Timer(FlushDebouncedWatchFiles, null, 1600, Timeout.Infinite);
                else
                    debounceTimer.Change(1600, Timeout.Infinite);
            }
        }

        static void FlushDebouncedWatchFiles(object state)
        {
            var ready = new List<string>();
            DateTime now = DateTime.UtcNow;
            DateTime nextDue = DateTime.MaxValue;

            lock (watchLock)
            {
                foreach (var kv in pendingWatchFiles.ToList())
                {
                    if (kv.Value <= now)
                    {
                        ready.Add(kv.Key);
                        pendingWatchFiles.Remove(kv.Key);
                    }
                    else if (kv.Value < nextDue)
                    {
                        nextDue = kv.Value;
                    }
                }
                if (pendingWatchFiles.Count > 0 && debounceTimer != null)
                {
                    int delay = Math.Max(200, (int)Math.Ceiling((nextDue - now).TotalMilliseconds));
                    debounceTimer.Change(delay, Timeout.Infinite);
                }
            }

            for (int i = 0; i < ready.Count; i++)
            {
                if (File.Exists(ready[i]))
                {
                    Interlocked.Increment(ref recentWatchedEvents);
                    SqliteVault.QueueExtract(ready[i]);
                }
            }
        }

        // =====================================================================
        // Schedule Parsing & Helpers
        // =====================================================================

        static void ValidateSchedule(string sched)
        {
            TimeSpan time;
            if (TryInterval(sched, out time) || TryDailyTime(sched, out time)) return;
            if (sched.StartsWith("once:", StringComparison.OrdinalIgnoreCase))
            {
                DateTime dt;
                if (DateTime.TryParse(sched.Substring(5).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                    return;
            }
            throw new ArgumentException("Invalid schedule \"" + sched + "\". Use every:15m, daily:08:00, or once:YYYY-MM-DDTHH:mm.");
        }

        static bool IsJobDue(CronJob job, DateTime nowUtc)
        {
            string sched = (job.schedule ?? "").Trim();
            DateTime lastRunUtc = DateTime.MinValue;
            if (!string.IsNullOrWhiteSpace(job.last_run_utc))
                DateTime.TryParse(job.last_run_utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out lastRunUtc);

            TimeSpan time;
            if (TryInterval(sched, out time))
                return lastRunUtc == DateTime.MinValue || (nowUtc - lastRunUtc) >= time;

            DateTime nowLocal = nowUtc.ToLocalTime();
            if (TryDailyTime(sched, out time))
            {
                DateTime todayTarget = nowLocal.Date.Add(time);
                if (nowLocal < todayTarget) return false;
                return lastRunUtc == DateTime.MinValue || lastRunUtc.ToLocalTime() < todayTarget;
            }

            if (sched.StartsWith("once:", StringComparison.OrdinalIgnoreCase))
            {
                DateTime target;
                if (DateTime.TryParse(sched.Substring(5).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out target))
                    return nowLocal >= target && lastRunUtc == DateTime.MinValue;
            }

            return false;
        }

        static bool TryInterval(string schedule, out TimeSpan interval)
        {
            interval = TimeSpan.Zero;
            var match = Regex.Match(schedule, @"^every:([0-9]+)([smhd])$", RegexOptions.IgnoreCase);
            long count;
            if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count == 0)
                return false;
            char unit = char.ToLowerInvariant(match.Groups[2].Value[0]);
            long ticks = unit == 's' ? TimeSpan.TicksPerSecond : unit == 'm' ? TimeSpan.TicksPerMinute
                : unit == 'h' ? TimeSpan.TicksPerHour : TimeSpan.TicksPerDay;
            if (count > TimeSpan.MaxValue.Ticks / ticks) return false;
            interval = TimeSpan.FromTicks((unit == 's' ? Math.Max(10, count) : count) * ticks);
            return true;
        }

        static bool TryDailyTime(string schedule, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            return schedule.StartsWith("daily:", StringComparison.OrdinalIgnoreCase)
                && TimeSpan.TryParseExact(schedule.Substring(6), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time);
        }

        static string NormalizePrecheckType(string raw)
        {
            string s = (raw ?? "").Trim().ToLowerInvariant();
            if (s == "folder_changed" || s == "sql_nonzero" || s == "overdue_tasks") return s;
            return "none";
        }

        static Dictionary<string, object> JobToDict(CronJob j)
        {
            return new Dictionary<string, object>
            {
                { "id", j.id },
                { "name", j.name },
                { "schedule", j.schedule },
                { "prompt", j.prompt },
                { "skill", j.skill },
                { "precheck_type", j.precheck_type },
                { "precheck_arg", j.precheck_arg },
                { "enabled", j.enabled },
                { "last_run_utc", j.last_run_utc },
                { "last_hash", j.last_hash },
                { "one_shot", j.one_shot },
                { "tool", j.tool }, { "tool_args", j.tool_args },
                { "agent", j.agent }, { "folder", j.folder },
                { "last_run_id", j.last_run_id }, { "run_status", j.run_status },
                { "run_output", j.run_output }, { "run_error", j.run_error },
                { "run_finished_utc", j.run_finished_utc }
            };
        }

        static string Sha256Short(string input)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8 && i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        static void NotifySafe(string title, string text)
        {
            try
            {
                if (App.I != null && App.I.Sync != null && !App.I.Sync.IsDisposed)
                    App.I.Sync.BeginInvoke(new Action(delegate { App.I.Balloon(title, Cut(text, 180)); }));
            }
            catch (Exception) { }
        }

        static string Cut(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }
    }
}
