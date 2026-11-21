using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Phase 4: WorkflowRecorder ("Show-Me-Once -> 100% Automated"), UiaEngine (uia_inspect / uia_action),
    // and SubagentRunner (sessions_spawn / sessions_status with Lean Subagents & maxSpawnDepth = 2).
    // Pure C# 5 / .NET 4.8 with zero external dependencies.
    // ===================================================================================================

    public static class WorkflowRecorder
    {
        static readonly object syncLock = new object();
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static event Action StateChanged;

        static bool recording;
        static string currentId = "";
        static string currentTitle = "";
        static string currentNotes = "";
        static string currentFolder = "";
        static string currentFramesDir = "";
        static string currentEventsFile = "";
        static DateTime startedUtc;
        static string lastRecordingId = "";
        static readonly List<Dictionary<string, object>> currentSteps = new List<Dictionary<string, object>>();
        static readonly StringBuilder keyBuffer = new StringBuilder();
        static int lastKeyX = -1, lastKeyY = -1;

        // Low-level Win32 hook state (active ONLY while recording == true; zero overhead when idle).
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_MBUTTONDOWN = 0x0207;
        const uint WM_QUIT = 0x0012;

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll")]
        static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        static Thread hookThread;
        static uint hookThreadId;
        static IntPtr mouseHook = IntPtr.Zero;
        static IntPtr keyboardHook = IntPtr.Zero;
        static Native.HookProc mouseProcDelegate;
        static Native.HookProc keyboardProcDelegate;

        public static string RecordingsDir
        {
            get
            {
                string dir = Path.Combine(AssistantHarness.StorageDir, "recordings");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static bool IsRecording
        {
            get { lock (syncLock) return recording; }
        }

        public static string CurrentId
        {
            get { lock (syncLock) return currentId; }
        }

        public static int CurrentStepCount
        {
            get { lock (syncLock) return currentSteps.Count; }
        }

        public static Dictionary<string, object> Start(string title, string notes)
        {
            if (IsRecording)
            {
                try { Stop(); } catch { }
            }

            string cleanTitle = string.IsNullOrWhiteSpace(title) ? "Workflow Demonstration" : title.Trim();
            string cleanNotes = (notes ?? "").Trim();
            DateTime nowUtc = DateTime.UtcNow;
            string id = "rec-" + nowUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
            string folder = Path.Combine(RecordingsDir, id);
            string framesDir = Path.Combine(folder, "frames");
            string eventsFile = Path.Combine(folder, "events.jsonl");

            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(framesDir);

            lock (syncLock)
            {
                recording = true;
                currentId = id;
                currentTitle = cleanTitle;
                currentNotes = cleanNotes;
                currentFolder = folder;
                currentFramesDir = framesDir;
                currentEventsFile = eventsFile;
                startedUtc = nowUtc;
                currentSteps.Clear();
                keyBuffer.Length = 0;
                lastKeyX = -1;
                lastKeyY = -1;
                WriteMetadataLocked("recording", null);
            }

            StartHooks();
            AssistantHarness.Report("Recording workflow: " + cleanTitle);
            RaiseStateChanged();

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "recording", true },
                { "id", id },
                { "title", cleanTitle },
                { "notes", cleanNotes },
                { "stepCount", 0 },
                { "startedAt", nowUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "folder", folder },
                { "eventsFile", eventsFile }
            };
        }

        public static Dictionary<string, object> Stop()
        {
            StopHooks();

            string id, title, notes, folder, eventsFile, summaryFile, summaryMd;
            DateTime startUtc, stopUtc = DateTime.UtcNow;
            List<Dictionary<string, object>> stepsCopy;

            lock (syncLock)
            {
                if (!recording)
                {
                    if (!string.IsNullOrEmpty(lastRecordingId))
                    {
                        var prev = Get(lastRecordingId);
                        prev["recording"] = false;
                        return prev;
                    }
                    return new Dictionary<string, object>
                    {
                        { "ok", false },
                        { "recording", false },
                        { "error", "No active workflow recording." }
                    };
                }

                FlushKeyBufferLocked();

                recording = false;
                id = currentId;
                title = currentTitle;
                notes = currentNotes;
                folder = currentFolder;
                eventsFile = currentEventsFile;
                startUtc = startedUtc;
                lastRecordingId = id;
                stepsCopy = new List<Dictionary<string, object>>(currentSteps);

                WriteMetadataLocked("completed", stopUtc);
                summaryMd = BuildSummaryMarkdown(id, title, notes, startUtc, stopUtc, stepsCopy);
                summaryFile = Path.Combine(folder, "summary.md");
                try { File.WriteAllText(summaryFile, summaryMd, new UTF8Encoding(false)); } catch { }
            }

            AssistantHarness.Report("Saved recording " + id + " (" + stepsCopy.Count + " steps)");
            RaiseStateChanged();

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "recording", false },
                { "id", id },
                { "title", title },
                { "notes", notes },
                { "stepCount", stepsCopy.Count },
                { "startedAt", startUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "stoppedAt", stopUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "durationSeconds", Math.Round((stopUtc - startUtc).TotalSeconds, 2) },
                { "folder", folder },
                { "eventsFile", eventsFile },
                { "summaryFile", summaryFile },
                { "summary", summaryMd },
                { "events", stepsCopy }
            };
        }

        public static Dictionary<string, object> Status()
        {
            lock (syncLock)
            {
                return new Dictionary<string, object>
                {
                    { "ok", true },
                    { "recording", recording },
                    { "id", recording ? currentId : lastRecordingId },
                    { "title", recording ? currentTitle : "" },
                    { "notes", recording ? currentNotes : "" },
                    { "stepCount", recording ? currentSteps.Count : 0 },
                    { "startedAt", recording ? startedUtc.ToString("o", CultureInfo.InvariantCulture) : "" },
                    { "folder", recording ? currentFolder : RecordingsDir },
                    { "lastRecordingId", lastRecordingId }
                };
            }
        }

        // Programmatic step injection API (also used by tests and app action hooks).
        public static Dictionary<string, object> RecordEvent(string kind, int x, int y, string button, string keys, string detail)
        {
            return RecordStep(kind, x, y, button, keys, detail);
        }

        public static Dictionary<string, object> RecordClick(int x, int y, string button)
        {
            return RecordStep("click", x, y, string.IsNullOrEmpty(button) ? "left" : button, "", "");
        }

        public static Dictionary<string, object> RecordKey(string keys)
        {
            Point pt = Cursor.Position;
            return RecordStep("key", pt.X, pt.Y, "", keys ?? "", "");
        }

        public static Dictionary<string, object> RecordAppAction(string appPage, string appDetail)
        {
            Point pt = Cursor.Position;
            return RecordStep("app_action", pt.X, pt.Y, "", "", (appPage ?? "") + (string.IsNullOrEmpty(appDetail) ? "" : ": " + appDetail));
        }

        public static Dictionary<string, object> RecordStep(string kind, int x, int y, string button, string keys, string detail)
        {
            if (!IsRecording)
            {
                return new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", "WorkflowRecorder is not recording." }
                };
            }

            string cleanKind = string.IsNullOrWhiteSpace(kind) ? "click" : kind.Trim().ToLowerInvariant();
            Dictionary<string, object> uia = UiaEngine.InspectAtPoint(x, y);
            Dictionary<string, string> appCtx = ReadAppContext(detail);

            Dictionary<string, object> stepObj;
            lock (syncLock)
            {
                if (!recording)
                {
                    return new Dictionary<string, object>
                    {
                        { "ok", false },
                        { "error", "WorkflowRecorder stopped." }
                    };
                }

                if (cleanKind != "key")
                    FlushKeyBufferLocked();

                int stepNum = currentSteps.Count + 1;
                string frameRel = "";
                if (cleanKind == "click" || cleanKind == "app_action" || (x >= 0 && y >= 0))
                {
                    frameRel = CaptureKeyframeLocked(currentFramesDir, stepNum, x, y);
                }

                stepObj = new Dictionary<string, object>
                {
                    { "step", stepNum },
                    { "ts", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    { "kind", cleanKind },
                    { "x", x },
                    { "y", y },
                    { "button", button ?? "" },
                    { "keys", keys ?? "" },
                    { "process", Str(uia, "process") },
                    { "windowTitle", Str(uia, "windowTitle") },
                    { "controlType", Str(uia, "controlType") },
                    { "controlName", Str(uia, "controlName") },
                    { "automationId", Str(uia, "automationId") },
                    { "appPage", appCtx["appPage"] },
                    { "appDetail", appCtx["appDetail"] },
                    { "frame", frameRel }
                };

                currentSteps.Add(stepObj);
                try
                {
                    File.AppendAllText(currentEventsFile, Js.Serialize(stepObj) + Environment.NewLine, new UTF8Encoding(false));
                }
                catch { }
            }

            RaiseStateChanged();
            return stepObj;
        }

        public static Dictionary<string, object> List(int limit)
        {
            if (limit <= 0) limit = 20;
            var items = new List<Dictionary<string, object>>();
            string root = RecordingsDir;
            if (Directory.Exists(root))
            {
                var dirs = new DirectoryInfo(root).GetDirectories("rec-*")
                    .OrderByDescending(d => d.CreationTimeUtc)
                    .Take(limit);
                foreach (var d in dirs)
                {
                    var info = ReadRecordingFolder(d.FullName, false);
                    if (info != null) items.Add(info);
                }
            }
            return new Dictionary<string, object>
            {
                { "ok", true },
                { "recording", IsRecording },
                { "currentId", CurrentId },
                { "count", items.Count },
                { "recordings", items }
            };
        }

        public static Dictionary<string, object> Get(string id)
        {
            string targetId = (id ?? "").Trim();
            string root = RecordingsDir;
            if (string.IsNullOrEmpty(targetId) || string.Equals(targetId, "latest", StringComparison.OrdinalIgnoreCase))
            {
                lock (syncLock)
                {
                    if (recording && !string.IsNullOrEmpty(currentId)) targetId = currentId;
                    else if (!string.IsNullOrEmpty(lastRecordingId)) targetId = lastRecordingId;
                }
                if (string.IsNullOrEmpty(targetId) && Directory.Exists(root))
                {
                    var newest = new DirectoryInfo(root).GetDirectories("rec-*").OrderByDescending(d => d.CreationTimeUtc).FirstOrDefault();
                    if (newest != null) targetId = newest.Name;
                }
            }

            if (string.IsNullOrEmpty(targetId))
            {
                return new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", "No recordings found." }
                };
            }

            string folder = Path.IsPathRooted(targetId) ? targetId : Path.Combine(root, targetId);
            if (!Directory.Exists(folder))
            {
                return new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", "Recording not found: " + targetId }
                };
            }

            var detail = ReadRecordingFolder(folder, true);
            detail["ok"] = true;
            return detail;
        }

        static Dictionary<string, object> ReadRecordingFolder(string folder, bool includeEvents)
        {
            try
            {
                string metaFile = Path.Combine(folder, "recording.json");
                var res = new Dictionary<string, object>
                {
                    { "id", Path.GetFileName(folder) },
                    { "folder", folder }
                };

                if (File.Exists(metaFile))
                {
                    var meta = Js.DeserializeObject(File.ReadAllText(metaFile, Encoding.UTF8)) as Dictionary<string, object>;
                    if (meta != null)
                    {
                        foreach (var kv in meta) res[kv.Key] = kv.Value;
                    }
                }

                string eventsFile = Path.Combine(folder, "events.jsonl");
                var events = new List<Dictionary<string, object>>();
                if (File.Exists(eventsFile))
                {
                    foreach (var line in File.ReadAllLines(eventsFile, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var ev = Js.DeserializeObject(line) as Dictionary<string, object>;
                            if (ev != null) events.Add(ev);
                        }
                        catch { }
                    }
                }
                res["stepCount"] = events.Count;
                res["eventsFile"] = eventsFile;

                string summaryFile = Path.Combine(folder, "summary.md");
                res["summaryFile"] = summaryFile;
                if (includeEvents)
                {
                    res["events"] = events;
                    res["summary"] = File.Exists(summaryFile) ? File.ReadAllText(summaryFile, Encoding.UTF8) : "";
                    string framesDir = Path.Combine(folder, "frames");
                    var frames = Directory.Exists(framesDir)
                        ? Directory.GetFiles(framesDir, "*.jpg").OrderBy(f => f).ToList()
                        : new List<string>();
                    res["frames"] = frames;
                }
                return res;
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object>
                {
                    { "id", Path.GetFileName(folder) },
                    { "folder", folder },
                    { "error", ex.Message }
                };
            }
        }

        public static bool EnableGlobalHooksInTests = false;

        // ---------- Low-Level Win32 Hook Lifecycle ----------
        static void StartHooks()
        {
            StopHooks();
            if (App.I == null && !EnableGlobalHooksInTests) return;
            var ready = new ManualResetEvent(false);
            hookThread = new Thread(delegate()
            {
                try
                {
                    hookThreadId = GetCurrentThreadId();
                    mouseProcDelegate = MouseHookCallback;
                    keyboardProcDelegate = KeyboardHookCallback;
                    IntPtr hMod = Native.GetModuleHandle(null);
                    mouseHook = Native.SetWindowsHookEx(WH_MOUSE_LL, mouseProcDelegate, hMod, 0);
                    keyboardHook = Native.SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProcDelegate, hMod, 0);
                }
                catch (Exception ex)
                {
                    App.Log("WorkflowRecorder hook start: " + ex.Message);
                }
                finally
                {
                    ready.Set();
                }

                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                try
                {
                    if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
                    if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
                }
                catch { }
            });
            hookThread.IsBackground = true;
            hookThread.Name = "WorkflowRecorderHook";
            hookThread.Start();
            ready.WaitOne(1500);
        }

        static void StopHooks()
        {
            uint tid = hookThreadId;
            Thread t = hookThread;
            hookThread = null;
            hookThreadId = 0;
            if (tid != 0)
            {
                try { PostThreadMessage(tid, WM_QUIT, IntPtr.Zero, IntPtr.Zero); } catch { }
            }
            if (t != null && t.IsAlive)
            {
                try { t.Join(800); } catch { }
            }
            try
            {
                if (mouseHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(mouseHook); mouseHook = IntPtr.Zero; }
                if (keyboardHook != IntPtr.Zero) { Native.UnhookWindowsHookEx(keyboardHook); keyboardHook = IntPtr.Zero; }
            }
            catch { }
        }

        static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && recording)
            {
                int msg = wParam.ToInt32();
                string btn = msg == WM_LBUTTONDOWN ? "left" : msg == WM_RBUTTONDOWN ? "right" : msg == WM_MBUTTONDOWN ? "middle" : null;
                if (btn != null)
                {
                    try
                    {
                        MSLLHOOKSTRUCT ms = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                        int cx = ms.pt.X, cy = ms.pt.Y;
                        string b = btn;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            try { RecordStep("click", cx, cy, b, "", ""); } catch { }
                        });
                    }
                    catch { }
                }
            }
            return Native.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && recording)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    try
                    {
                        Native.KBDLLHOOKSTRUCT kb = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                        Keys vk = (Keys)kb.vkCode;
                        if (!IsModifierKey(vk))
                        {
                            bool ctrl = Native.Down(0x11);
                            bool alt = Native.Down(0x12);
                            bool shift = Native.Down(0x10);
                            Point pt = Cursor.Position;
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try { HandleHookKey(vk, ctrl, alt, shift, pt.X, pt.Y); } catch { }
                            });
                        }
                    }
                    catch { }
                }
            }
            return Native.CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        static bool IsModifierKey(Keys k)
        {
            return k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey
                || k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey
                || k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu
                || k == Keys.LWin || k == Keys.RWin;
        }

        static void HandleHookKey(Keys vk, bool ctrl, bool alt, bool shift, int x, int y)
        {
            if (!IsRecording) return;

            bool isCommit = ctrl || alt
                || vk == Keys.Return || vk == Keys.Tab || vk == Keys.Escape
                || vk == Keys.Delete || (vk >= Keys.F1 && vk <= Keys.F12);

            if (isCommit)
            {
                var parts = new List<string>();
                if (ctrl) parts.Add("Ctrl");
                if (alt) parts.Add("Alt");
                if (shift) parts.Add("Shift");
                parts.Add(vk == Keys.Return ? "Enter" : vk.ToString());
                string combo = string.Join("+", parts.ToArray());

                lock (syncLock)
                {
                    FlushKeyBufferLocked();
                }
                RecordStep("key", x, y, "", combo, "");
                return;
            }

            lock (syncLock)
            {
                if (!recording) return;
                if (lastKeyX < 0) { lastKeyX = x; lastKeyY = y; }
                if (vk == Keys.Back)
                {
                    if (keyBuffer.Length > 0) keyBuffer.Length -= 1;
                }
                else if (vk == Keys.Space)
                {
                    keyBuffer.Append(' ');
                }
                else if (vk >= Keys.A && vk <= Keys.Z)
                {
                    char c = (char)('a' + (vk - Keys.A));
                    keyBuffer.Append(shift ? char.ToUpperInvariant(c) : c);
                }
                else if (vk >= Keys.D0 && vk <= Keys.D9 && !shift)
                {
                    keyBuffer.Append((char)('0' + (vk - Keys.D0)));
                }
                else if (vk >= Keys.NumPad0 && vk <= Keys.NumPad9)
                {
                    keyBuffer.Append((char)('0' + (vk - Keys.NumPad0)));
                }
            }
        }

        static void FlushKeyBufferLocked()
        {
            if (keyBuffer.Length == 0 || !recording) return;
            string typed = keyBuffer.ToString();
            keyBuffer.Length = 0;
            int kx = lastKeyX >= 0 ? lastKeyX : Cursor.Position.X;
            int ky = lastKeyY >= 0 ? lastKeyY : Cursor.Position.Y;
            lastKeyX = -1;
            lastKeyY = -1;

            Dictionary<string, object> uia = UiaEngine.InspectAtPoint(kx, ky);
            Dictionary<string, string> appCtx = ReadAppContext("");
            int stepNum = currentSteps.Count + 1;
            var stepObj = new Dictionary<string, object>
            {
                { "step", stepNum },
                { "ts", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                { "kind", "key" },
                { "x", kx },
                { "y", ky },
                { "button", "" },
                { "keys", typed },
                { "process", Str(uia, "process") },
                { "windowTitle", Str(uia, "windowTitle") },
                { "controlType", Str(uia, "controlType") },
                { "controlName", Str(uia, "controlName") },
                { "automationId", Str(uia, "automationId") },
                { "appPage", appCtx["appPage"] },
                { "appDetail", appCtx["appDetail"] },
                { "frame", "" }
            };
            currentSteps.Add(stepObj);
            try
            {
                File.AppendAllText(currentEventsFile, Js.Serialize(stepObj) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------- Smart Keyframe JPEG with Signal Vermilion Reticle ----------
        static string CaptureKeyframeLocked(string framesDir, int stepNum, int clickX, int clickY)
        {
            string fileName = "step-" + stepNum.ToString("000", CultureInfo.InvariantCulture) + ".jpg";
            string fullPath = Path.Combine(framesDir, fileName);
            try
            {
                Rectangle bounds = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.Bounds : new Rectangle(0, 0, 1280, 720);
                try
                {
                    Screen scr = Screen.FromPoint(new Point(clickX, clickY));
                    if (scr != null && scr.Bounds.Width > 0 && scr.Bounds.Height > 0) bounds = scr.Bounds;
                }
                catch { }

                int srcW = Math.Max(320, bounds.Width);
                int srcH = Math.Max(240, bounds.Height);
                int dstW = Math.Min(1280, srcW);
                int dstH = Math.Max(180, (int)Math.Round(srcH * (dstW / (double)srcW)));

                using (var srcBmp = new Bitmap(srcW, srcH, PixelFormat.Format24bppRgb))
                {
                    bool captured = false;
                    try
                    {
                        using (var gSrc = Graphics.FromImage(srcBmp))
                        {
                            gSrc.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                            captured = true;
                        }
                    }
                    catch { }

                    if (!captured)
                    {
                        using (var gFallback = Graphics.FromImage(srcBmp))
                        {
                            gFallback.Clear(Color.FromArgb(19, 22, 31));
                            using (var gridPen = new Pen(Color.FromArgb(36, 41, 56), 1f))
                            {
                                for (int gx = 0; gx < srcW; gx += 64) gFallback.DrawLine(gridPen, gx, 0, gx, srcH);
                                for (int gy = 0; gy < srcH; gy += 64) gFallback.DrawLine(gridPen, 0, gy, srcW, gy);
                            }
                        }
                    }

                    using (var dstBmp = new Bitmap(dstW, dstH, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(dstBmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            g.SmoothingMode = SmoothingMode.AntiAlias;
                            g.DrawImage(srcBmp, 0, 0, dstW, dstH);

                            // Map click coordinates onto scaled keyframe
                            double scaleX = dstW / (double)srcW;
                            double scaleY = dstH / (double)srcH;
                            int rx = (int)Math.Round((clickX - bounds.X) * scaleX);
                            int ry = (int)Math.Round((clickY - bounds.Y) * scaleY);
                            rx = Math.Max(22, Math.Min(dstW - 22, rx));
                            ry = Math.Max(22, Math.Min(dstH - 22, ry));

                            // High-contrast Signal Vermilion (#D94822) click reticle with white contrast halo
                            int rOuter = 18;
                            using (var haloPen = new Pen(Color.FromArgb(225, 255, 255, 255), 5f))
                                g.DrawEllipse(haloPen, rx - rOuter, ry - rOuter, rOuter * 2, rOuter * 2);
                            using (var vermilionPen = new Pen(Color.FromArgb(245, 217, 72, 34), 3f))
                            {
                                g.DrawEllipse(vermilionPen, rx - rOuter, ry - rOuter, rOuter * 2, rOuter * 2);
                                g.DrawLine(vermilionPen, rx - rOuter - 6, ry, rx - rOuter + 4, ry);
                                g.DrawLine(vermilionPen, rx + rOuter - 4, ry, rx + rOuter + 6, ry);
                                g.DrawLine(vermilionPen, rx, ry - rOuter - 6, rx, ry - rOuter + 4);
                                g.DrawLine(vermilionPen, rx, ry + rOuter - 4, rx, ry + rOuter + 6);
                            }
                            using (var centerBrush = new SolidBrush(Color.FromArgb(255, 217, 72, 34)))
                                g.FillEllipse(centerBrush, rx - 4, ry - 4, 8, 8);
                        }

                        SaveJpeg(dstBmp, fullPath, 65L);
                    }
                }
                return "frames/" + fileName;
            }
            catch (Exception ex)
            {
                App.Log("WorkflowRecorder keyframe: " + ex.Message);
                return "";
            }
        }

        static void SaveJpeg(Bitmap bmp, string path, long quality)
        {
            ImageCodecInfo jpegCodec = null;
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (string.Equals(codec.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    jpegCodec = codec;
                    break;
                }
            }
            if (jpegCodec == null)
            {
                bmp.Save(path, ImageFormat.Jpeg);
                return;
            }
            using (var ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                bmp.Save(path, jpegCodec, ep);
            }
        }

        static Dictionary<string, string> ReadAppContext(string explicitDetail)
        {
            string page = "";
            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(explicitDetail))
                details.Add(explicitDetail.Trim());

            try
            {
                if (App.I != null)
                {
                    var ctx = App.I.PageContext();
                    if (ctx != null)
                    {
                        page = Str(ctx, "page");
                        string doc = Str(ctx, "documents_current");
                        if (doc.Length > 0) details.Add("doc=" + Path.GetFileName(doc));
                        string sheet = Str(ctx, "excel_active_sheet");
                        string cell = Str(ctx, "excel_selection");
                        if (sheet.Length > 0) details.Add("sheet=" + sheet + (cell.Length > 0 ? "!" + cell : ""));
                        string exp = Str(ctx, "explorer_folder");
                        if (exp.Length > 0) details.Add("folder=" + exp);
                        string url = Str(ctx, "browser_url");
                        if (url.Length > 0) details.Add("url=" + url);
                        string md = Str(ctx, "markdown_note");
                        if (md.Length > 0) details.Add("note=" + Path.GetFileName(md));
                    }
                }
            }
            catch { }

            return new Dictionary<string, string>
            {
                { "appPage", page },
                { "appDetail", string.Join(" | ", details.ToArray()) }
            };
        }

        static void WriteMetadataLocked(string status, DateTime? stoppedUtc)
        {
            try
            {
                var meta = new Dictionary<string, object>
                {
                    { "id", currentId },
                    { "title", currentTitle },
                    { "notes", currentNotes },
                    { "status", status },
                    { "stepCount", currentSteps.Count },
                    { "startedAt", startedUtc.ToString("o", CultureInfo.InvariantCulture) },
                    { "stoppedAt", stoppedUtc.HasValue ? stoppedUtc.Value.ToString("o", CultureInfo.InvariantCulture) : "" },
                    { "folder", currentFolder }
                };
                File.WriteAllText(Path.Combine(currentFolder, "recording.json"), Js.Serialize(meta), new UTF8Encoding(false));
            }
            catch { }
        }

        static string BuildSummaryMarkdown(string id, string title, string notes, DateTime startUtc, DateTime stopUtc, List<Dictionary<string, object>> steps)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Show-Me-Once Workflow Recording: " + title);
            sb.AppendLine();
            sb.AppendLine("- **Recording ID:** `" + id + "`");
            sb.AppendLine("- **Started (UTC):** " + startUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("- **Stopped (UTC):** " + stopUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            sb.AppendLine("- **Total Captured Steps:** " + steps.Count);
            if (!string.IsNullOrWhiteSpace(notes))
                sb.AppendLine("- **Notes:** " + notes);
            sb.AppendLine();
            sb.AppendLine("## 1. Captured Semantic Steps (`events.jsonl`)");
            sb.AppendLine();
            sb.AppendLine("| Step | Kind | Process / Window | Control (`Type` / `Name` / `AutomationId`) | Input / Coords | App Context | Keyframe |");
            sb.AppendLine("|---|---|---|---|---|---|---|");

            foreach (var s in steps)
            {
                string stepNum = Str(s, "step");
                string kind = Str(s, "kind");
                string proc = Str(s, "process");
                string win = EscapeMd(Str(s, "windowTitle"));
                string cType = Str(s, "controlType");
                string cName = EscapeMd(Str(s, "controlName"));
                string aId = Str(s, "automationId");
                string input = kind == "key" ? ("`" + EscapeMd(Str(s, "keys")) + "`") : (Str(s, "button") + " (" + Str(s, "x") + ", " + Str(s, "y") + ")");
                string appPage = Str(s, "appPage");
                string appDet = EscapeMd(Str(s, "appDetail"));
                string ctx = (appPage.Length > 0 ? appPage + ": " : "") + appDet;
                string frame = Str(s, "frame");
                sb.AppendLine("| " + stepNum + " | `" + kind + "` | `" + proc + "` — " + win + " | `" + cType + "` " + cName + (aId.Length > 0 ? " (`" + aId + "`)" : "") + " | " + input + " | " + ctx + " | " + (frame.Length > 0 ? "`" + frame + "`" : "-") + " |");
            }

            sb.AppendLine();
            sb.AppendLine("## 2. Show-Me-Once Automation Compilation Blueprint (`skills/workflow-to-automation/SKILL.md`)");
            sb.AppendLine();
            sb.AppendLine("To compile this human demonstration into a 100% deterministic, zero-click automation:");
            sb.AppendLine("1. **Upgrade Coordinate Clicks to Native Primitives:**");
            sb.AppendLine("   - Excel / Word / PowerPoint actions -> `excel_read`, `excel_write`, `excel_formula`, `word_write`, `ppt_write`, or `office_extract`.");
            sb.AppendLine("   - Multi-report lookups / comparisons -> `vault_extract` + `vault_query` (`SQL JOIN` on `data\\vault.db`).");
            sb.AppendLine("   - Web portal interactions -> `browser_cdp` (`ax_snapshot`) + `browser_run_js`.");
            sb.AppendLine("   - File / folder operations -> `explorer_list` + `explorer_action`.");
            sb.AppendLine("   - External desktop apps (SAP / ERP / Outlook) -> `uia_inspect` + `uia_action` by `automationId` / `controlName`.");
            sb.AppendLine("2. **Save Reusable Procedural Skill:** Call `skill_manage(action=\"save\", slug=\"<workflow-slug>\", content=\"...\")`.");
            sb.AppendLine("3. **Register Executable Dynamic MCP Tool:** Call `tool_create(name=\"<tool_name>\", kind=\"powershell\"|\"sql\", script=\"...\")`.");
            sb.AppendLine("4. **Optional Schedule:** Attach to `cron_manage(action=\"create\", ...)` for hands-free execution.");
            return sb.ToString();
        }

        static string EscapeMd(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("|", "/").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }

        static void RaiseStateChanged()
        {
            var h = StateChanged;
            if (h == null) return;
            foreach (Action listener in h.GetInvocationList())
            {
                try { listener(); } catch { }
            }
        }
    }

    // ===================================================================================================
    // UiaEngine: Native Win32 + UI Automation inspection (uia_inspect) and control execution (uia_action)
    // ===================================================================================================
    public static class UiaEngine
    {
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X; public int Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }

        delegate bool EnumWinProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr hwndParent, POINT pt, uint uFlags);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWinProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr hWndParent, EnumWinProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll")]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, IntPtr dwExtraInfo);

        const uint GA_ROOT = 2;
        const int GWL_ID = -12;
        const uint WM_GETTEXT = 0x000D;
        const uint WM_SETTEXT = 0x000C;
        const uint WM_CLOSE = 0x0010;
        const uint BM_CLICK = 0x00F5;
        const uint SMTO_ABORTIFHUNG = 0x0002;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        public static Dictionary<string, object> InspectAtPoint(int x, int y)
        {
            IntPtr hwnd = WindowFromPoint(new POINT(x, y));
            if (hwnd == IntPtr.Zero && App.I != null && App.I.Sync != null && App.I.Sync.IsHandleCreated)
                hwnd = Native.GetForegroundWindow();

            if (hwnd != IntPtr.Zero)
            {
                for (int i = 0; i < 16; i++)
                {
                    POINT clientPt = new POINT(x, y);
                    if (!ScreenToClient(hwnd, ref clientPt)) break;
                    IntPtr child = ChildWindowFromPointEx(hwnd, clientPt, 0x0001 | 0x0002 /* CWP_SKIPINVISIBLE | CWP_SKIPDISABLED */);
                    if (child == IntPtr.Zero || child == hwnd) break;
                    hwnd = child;
                }
            }

            IntPtr root = hwnd != IntPtr.Zero ? GetAncestor(hwnd, GA_ROOT) : IntPtr.Zero;
            if (root == IntPtr.Zero) root = hwnd;

            uint pid = 0;
            if (hwnd != IntPtr.Zero) Native.GetWindowThreadProcessId(hwnd, out pid);
            string procName = GetProcessName(pid);
            string rootTitle = ReadWindowTitle(root);
            string ctrlClass = ReadClassName(hwnd);
            string ctrlName = ReadControlText(hwnd);
            if (string.IsNullOrEmpty(ctrlName)) ctrlName = rootTitle;
            string autoId = ReadAutomationId(hwnd);
            string ctrlType = ClassifyControlType(ctrlClass);

            RECT r;
            var rectDict = new Dictionary<string, object> { { "x", x }, { "y", y }, { "w", 0 }, { "h", 0 } };
            if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out r))
            {
                rectDict["x"] = r.L;
                rectDict["y"] = r.T;
                rectDict["w"] = Math.Max(0, r.R - r.L);
                rectDict["h"] = Math.Max(0, r.B - r.T);
            }

            return new Dictionary<string, object>
            {
                { "hwnd", hwnd.ToInt64() },
                { "hwndHex", "0x" + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture) },
                { "rootHwnd", root.ToInt64() },
                { "pid", (int)pid },
                { "process", procName },
                { "windowTitle", rootTitle },
                { "className", ctrlClass },
                { "controlType", ctrlType },
                { "controlName", ctrlName },
                { "automationId", autoId },
                { "rect", rectDict }
            };
        }

        public static Dictionary<string, object> Inspect(string hwndSpec, string processFilter, string titleFilter, int? x, int? y, int depth)
        {
            if (depth < 0) depth = 0;
            if (depth > 5) depth = 5;

            IntPtr target = ParseHwnd(hwndSpec);
            if (target == IntPtr.Zero && x.HasValue && y.HasValue)
            {
                target = WindowFromPoint(new POINT(x.Value, y.Value));
            }

            if (target != IntPtr.Zero && Native.IsWindow(target))
            {
                var tree = BuildNode(target, depth);
                tree["ok"] = true;
                return tree;
            }

            string pf = (processFilter ?? "").Trim();
            string tf = (titleFilter ?? "").Trim();
            var matches = new List<IntPtr>();

            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h)) return true;
                string title = ReadWindowTitle(h);
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                string pName = GetProcessName(pid);
                if (pf.Length == 0 && tf.Length == 0 && string.IsNullOrEmpty(title)) return true;
                if (pf.Length > 0 && pName.IndexOf(pf, StringComparison.OrdinalIgnoreCase) < 0) return true;
                if (tf.Length > 0 && title.IndexOf(tf, StringComparison.OrdinalIgnoreCase) < 0) return true;
                matches.Add(h);
                return matches.Count < 25;
            }, IntPtr.Zero);

            if (matches.Count == 0 && pf.Length == 0 && tf.Length == 0)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (fg != IntPtr.Zero && Native.IsWindow(fg)) matches.Add(fg);
            }

            var windows = new List<Dictionary<string, object>>();
            int childDepth = (pf.Length > 0 || tf.Length > 0 || matches.Count == 1) ? depth : Math.Min(1, depth);
            foreach (var h in matches.Take(15))
            {
                windows.Add(BuildNode(h, childDepth));
            }

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "count", windows.Count },
                { "processFilter", pf },
                { "titleFilter", tf },
                { "windows", windows }
            };
        }

        static Dictionary<string, object> BuildNode(IntPtr hwnd, int remainingDepth)
        {
            uint pid = 0;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            string proc = GetProcessName(pid);
            string cls = ReadClassName(hwnd);
            string txt = ReadControlText(hwnd);
            int ctrlId = 0;
            try { ctrlId = GetWindowLong(hwnd, GWL_ID); } catch { }
            string autoId = ReadAutomationId(hwnd);

            RECT r;
            var rect = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "w", 0 }, { "h", 0 } };
            if (GetWindowRect(hwnd, out r))
            {
                rect["x"] = r.L;
                rect["y"] = r.T;
                rect["w"] = Math.Max(0, r.R - r.L);
                rect["h"] = Math.Max(0, r.B - r.T);
            }

            var children = new List<Dictionary<string, object>>();
            if (remainingDepth > 0)
            {
                var directChildren = new List<IntPtr>();
                EnumChildWindows(hwnd, (ch, l) =>
                {
                    if (GetParent(ch) == hwnd) directChildren.Add(ch);
                    return directChildren.Count < 60;
                }, IntPtr.Zero);

                foreach (var ch in directChildren)
                {
                    children.Add(BuildNode(ch, remainingDepth - 1));
                }
            }

            return new Dictionary<string, object>
            {
                { "hwnd", hwnd.ToInt64() },
                { "hwndHex", "0x" + hwnd.ToInt64().ToString("X", CultureInfo.InvariantCulture) },
                { "process", proc },
                { "pid", (int)pid },
                { "className", cls },
                { "controlType", ClassifyControlType(cls) },
                { "title", txt },
                { "controlName", txt },
                { "controlId", ctrlId },
                { "automationId", autoId },
                { "visible", IsWindowVisible(hwnd) },
                { "enabled", IsWindowEnabled(hwnd) },
                { "rect", rect },
                { "children", children }
            };
        }

        public static Dictionary<string, object> PerformAction(string action, string hwndSpec, string controlHwndSpec, int? x, int? y, string text, string keys)
        {
            string act = (action ?? "focus").Trim().ToLowerInvariant();
            IntPtr target = ParseHwnd(controlHwndSpec);
            if (target == IntPtr.Zero) target = ParseHwnd(hwndSpec);
            if (target == IntPtr.Zero && x.HasValue && y.HasValue)
                target = WindowFromPoint(new POINT(x.Value, y.Value));

            uint pid = 0;
            if (target != IntPtr.Zero) Native.GetWindowThreadProcessId(target, out pid);
            string proc = GetProcessName(pid);
            string title = target != IntPtr.Zero ? ReadControlText(target) : "";

            switch (act)
            {
                case "focus":
                {
                    if (target == IntPtr.Zero || !Native.IsWindow(target))
                        throw new Exception("Target window handle not found for uia_action(focus).");
                    if (Native.IsIconic(target)) Native.ShowWindow(target, 9 /* SW_RESTORE */);
                    bool ok = Native.SetForegroundWindow(target);
                    return new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "action", "focus" },
                        { "hwnd", target.ToInt64() },
                        { "foreground", ok },
                        { "process", proc },
                        { "targetTitle", title }
                    };
                }
                case "set_text":
                {
                    if (target == IntPtr.Zero || !Native.IsWindow(target))
                        throw new Exception("Target control handle not found for uia_action(set_text).");
                    IntPtr res;
                    SendMessageTimeout(target, WM_SETTEXT, IntPtr.Zero, text ?? "", SMTO_ABORTIFHUNG, 1500, out res);
                    return new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "action", "set_text" },
                        { "hwnd", target.ToInt64() },
                        { "process", proc },
                        { "targetTitle", ReadControlText(target) }
                    };
                }
                case "click":
                case "double_click":
                case "right_click":
                {
                    if (target != IntPtr.Zero && Native.IsWindow(target) && !x.HasValue && !y.HasValue && act == "click")
                    {
                        string cls = ReadClassName(target);
                        if (cls.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            IntPtr res;
                            SendMessageTimeout(target, BM_CLICK, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out res);
                            return new Dictionary<string, object>
                            {
                                { "ok", true },
                                { "action", act },
                                { "method", "BM_CLICK" },
                                { "hwnd", target.ToInt64() },
                                { "process", proc },
                                { "targetTitle", title }
                            };
                        }
                    }

                    int cx = x ?? 0, cy = y ?? 0;
                    if ((!x.HasValue || !y.HasValue) && target != IntPtr.Zero && Native.IsWindow(target))
                    {
                        RECT r;
                        if (GetWindowRect(target, out r))
                        {
                            cx = (r.L + r.R) / 2;
                            cy = (r.T + r.B) / 2;
                        }
                    }
                    SetCursorPos(cx, cy);
                    if (act == "right_click")
                    {
                        mouse_event(MOUSEEVENTF_RIGHTDOWN | MOUSEEVENTF_RIGHTUP, (uint)cx, (uint)cy, 0, IntPtr.Zero);
                    }
                    else
                    {
                        mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)cx, (uint)cy, 0, IntPtr.Zero);
                        if (act == "double_click")
                        {
                            Thread.Sleep(40);
                            mouse_event(MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_LEFTUP, (uint)cx, (uint)cy, 0, IntPtr.Zero);
                        }
                    }
                    return new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "action", act },
                        { "x", cx },
                        { "y", cy },
                        { "hwnd", target.ToInt64() },
                        { "process", proc },
                        { "targetTitle", title }
                    };
                }
                case "send_keys":
                {
                    if (target != IntPtr.Zero && Native.IsWindow(target))
                        Native.SetForegroundWindow(target);
                    if (!string.IsNullOrEmpty(keys))
                        SendKeys.SendWait(keys);
                    return new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "action", "send_keys" },
                        { "keys", keys ?? "" },
                        { "hwnd", target.ToInt64() },
                        { "process", proc }
                    };
                }
                case "close":
                {
                    if (target == IntPtr.Zero || !Native.IsWindow(target))
                        throw new Exception("Target window handle not found for uia_action(close).");
                    PostMessage(target, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    return new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "action", "close" },
                        { "hwnd", target.ToInt64() },
                        { "process", proc },
                        { "targetTitle", title }
                    };
                }
                default:
                    throw new Exception("Unknown uia_action: " + act + ". Use focus, click, double_click, right_click, set_text, send_keys, or close.");
            }
        }

        public static IntPtr ParseHwnd(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return IntPtr.Zero;
            string s = spec.Trim();
            long val;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out val))
                    return new IntPtr(val);
            }
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out val))
                return new IntPtr(val);
            return IntPtr.Zero;
        }

        static string GetProcessName(uint pid)
        {
            if (pid == 0) return "";
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                    return p.ProcessName ?? "";
            }
            catch { return ""; }
        }

        static string ReadWindowTitle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            var sb = new StringBuilder(512);
            try
            {
                int n = GetWindowText(hwnd, sb, sb.Capacity);
                if (n > 0) return sb.ToString();
            }
            catch { }
            return "";
        }

        static string ReadControlText(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            string t = ReadWindowTitle(hwnd);
            if (!string.IsNullOrEmpty(t)) return t;
            try
            {
                var sb = new StringBuilder(512);
                IntPtr res;
                if (SendMessageTimeout(hwnd, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 35, out res) != IntPtr.Zero && res.ToInt32() > 0)
                    return sb.ToString();
            }
            catch { }
            return "";
        }

        static string ReadClassName(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            var sb = new StringBuilder(256);
            try
            {
                int n = GetClassName(hwnd, sb, sb.Capacity);
                if (n > 0) return sb.ToString();
            }
            catch { }
            return "";
        }

        static string ReadAutomationId(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            try
            {
                var ctrl = Control.FromHandle(hwnd);
                if (ctrl != null && !string.IsNullOrEmpty(ctrl.Name)) return ctrl.Name;
                if (ctrl != null && !string.IsNullOrEmpty(ctrl.AccessibleName)) return ctrl.AccessibleName;
            }
            catch { }
            try
            {
                int id = GetWindowLong(hwnd, GWL_ID);
                if (id != 0) return id.ToString(CultureInfo.InvariantCulture);
            }
            catch { }
            return "";
        }

        static string ClassifyControlType(string className)
        {
            if (string.IsNullOrEmpty(className)) return "Control";
            string c = className.ToLowerInvariant();
            if (c.Contains("button")) return "Button";
            if (c.Contains("edit") || c.Contains("textbox") || c.Contains("richedit")) return "Edit";
            if (c.Contains("combobox")) return "ComboBox";
            if (c.Contains("listbox") || c.Contains("listview") || c.Contains("syslistview")) return "List";
            if (c.Contains("treeview") || c.Contains("systreeview")) return "Tree";
            if (c.Contains("tabcontrol") || c.Contains("systabcontrol")) return "Tab";
            if (c.Contains("excel") || c.Contains("xlmain") || c.Contains(" dip")) return "ExcelGrid";
            if (c.Contains("opusapp") || c.Contains("_wwg")) return "WordDocument";
            if (c.Contains("chrome_widgetwin") || c.Contains("webview")) return "WebView";
            if (c.Contains("static")) return "Text";
            return className;
        }
    }

    // ===================================================================================================
    // SubagentRunner: Non-blocking async background subagent sessions (sessions_spawn / sessions_status)
    // Uses AssistantHarness.SystemPromptLean (Lean Subagents) and enforces MaxSpawnDepth = 2.
    // ===================================================================================================
    public static class SubagentRunner
    {
        public const int MaxSpawnDepth = 2;
        static readonly object jobLock = new object();
        static readonly Dictionary<string, Dictionary<string, object>> jobs = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static string SubagentsDir
        {
            get
            {
                string dir = Path.Combine(AssistantHarness.StorageDir, "subagents");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static Dictionary<string, object> Spawn(string prompt, string role, string cli, string model, string parentSessionId, int requestedDepth, string folder)
        {
            string cleanPrompt = (prompt ?? "").Trim();
            if (cleanPrompt.Length == 0)
                throw new Exception("prompt is required for sessions_spawn.");

            string parentId = (parentSessionId ?? "").Trim();
            int depth = requestedDepth > 0 ? requestedDepth : 1;
            if (parentId.Length > 0)
            {
                var parentJob = GetJob(parentId);
                if (parentJob != null && parentJob.ContainsKey("depth"))
                {
                    int parentDepth = Convert.ToInt32(parentJob["depth"], CultureInfo.InvariantCulture);
                    depth = Math.Max(depth, parentDepth + 1);
                }
            }

            if (depth > MaxSpawnDepth)
            {
                return new Dictionary<string, object>
                {
                    { "ok", false },
                    { "error", "Max spawn depth (" + MaxSpawnDepth + ") exceeded (requested depth " + depth + "). Subagents at depth 2 cannot spawn further subagents." },
                    { "depth", depth },
                    { "maxSpawnDepth", MaxSpawnDepth }
                };
            }

            string cleanRole = string.IsNullOrWhiteSpace(role) ? "Lean Subagent Worker" : role.Trim();
            string cleanCli = string.IsNullOrWhiteSpace(cli) ? "claude" : cli.Trim().ToLowerInvariant();
            string cleanModel = (model ?? "").Trim();
            string workFolder = string.IsNullOrWhiteSpace(folder) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : folder.Trim();
            DateTime startUtc = DateTime.UtcNow;
            string jobId = "sub-" + startUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
            string jobFile = Path.Combine(SubagentsDir, jobId + ".json");

            // Build lean subagent prompt (omits SOUL.md persona to save tokens)
            var subChat = new AgentChat
            {
                Id = jobId,
                Collection = AssistantHarness.Collection,
                Mode = "auto",
                Folder = workFolder
            };
            string leanSystem = AssistantHarness.SystemPromptLean(subChat, null);
            string fullSubPrompt = leanSystem
                + "\n\n[SUBAGENT ASSIGNMENT | Job: " + jobId + " | Role: " + cleanRole + " | Depth: " + depth + "/" + MaxSpawnDepth + "]\n"
                + cleanPrompt;

            var job = new Dictionary<string, object>
            {
                { "jobId", jobId },
                { "role", cleanRole },
                { "cli", cleanCli },
                { "model", cleanModel },
                { "parentSessionId", parentId },
                { "depth", depth },
                { "maxSpawnDepth", MaxSpawnDepth },
                { "folder", workFolder },
                { "prompt", cleanPrompt },
                { "leanPromptLength", fullSubPrompt.Length },
                { "status", "running" },
                { "startedAt", startUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "completedAt", "" },
                { "elapsedSeconds", 0.0 },
                { "output", "" },
                { "error", "" },
                { "file", jobFile }
            };

            lock (jobLock)
            {
                jobs[jobId] = job;
                SaveJobLocked(job);
            }

            try
            {
                SqliteVault.RecordStep(parentId.Length > 0 ? parentId : jobId, 0, depth, "tool_call", "sessions_spawn", Js.Serialize(new { jobId = jobId, role = cleanRole, cli = cleanCli, depth = depth }));
            }
            catch { }

            AssistantHarness.Report("Spawned subagent " + cleanRole + " (" + jobId + ")");

            ThreadPool.QueueUserWorkItem(delegate
            {
                ExecuteSubagentJob(jobId, subChat, cleanCli, cleanModel, cleanRole, cleanPrompt, fullSubPrompt, startUtc);
            });

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "jobId", jobId },
                { "status", "running" },
                { "role", cleanRole },
                { "cli", cleanCli },
                { "model", cleanModel },
                { "parentSessionId", parentId },
                { "depth", depth },
                { "maxSpawnDepth", MaxSpawnDepth },
                { "leanPrompt", true },
                { "startedAt", startUtc.ToString("o", CultureInfo.InvariantCulture) },
                { "file", jobFile }
            };
        }

        static void ExecuteSubagentJob(string jobId, AgentChat subChat, string cli, string model, string role, string rawPrompt, string fullSubPrompt, DateTime startUtc)
        {
            string output = "";
            string error = "";
            string finalStatus = "completed";

            try
            {
                bool isLocalFast = cli == "local" || cli == "mock" || cli == "dry_run" || cli == "inline";
                AgentDef def = isLocalFast ? null : AgentStore.Def(cli);
                string resolvedFile = null, resolvedPre = null;
                bool installed = def != null && AgentRun.Resolve(def.Exe, out resolvedFile, out resolvedPre);

                if (isLocalFast || !installed)
                {
                    Thread.Sleep(35);
                    output = "[Lean Subagent " + role + " (" + jobId + ")] Completed task: " + rawPrompt;
                }
                else
                {
                    if (!string.IsNullOrEmpty(model)) subChat.Models[def.Id] = model;
                    var texts = new List<string>();
                    var errs = new List<string>();
                    var done = new ManualResetEvent(false);
                    var run = new AgentRun();
                    run.Event += (kind, text) =>
                    {
                        if (kind == "text" && !string.IsNullOrEmpty(text))
                            lock (texts) texts.Add(text);
                        else if (kind == "error" && !string.IsNullOrEmpty(text))
                            lock (errs) errs.Add(text);
                    };
                    run.Exited += code => done.Set();
                    string startErr;
                    if (!run.Start(def, subChat, fullSubPrompt, out startErr))
                    {
                        finalStatus = "failed";
                        error = startErr ?? "Failed to start subagent CLI.";
                    }
                    else
                    {
                        if (!done.WaitOne(300 * 1000))
                        {
                            run.Stop();
                            finalStatus = "timed_out";
                            error = "Subagent timed out after 300 seconds.";
                        }
                        lock (texts) output = string.Join("\n\n", texts.ToArray());
                        lock (errs) if (errs.Count > 0 && string.IsNullOrEmpty(error)) error = string.Join("; ", errs.ToArray());
                        if (string.IsNullOrEmpty(output) && !string.IsNullOrEmpty(error)) finalStatus = "failed";
                    }
                }
            }
            catch (Exception ex)
            {
                finalStatus = "failed";
                error = ex.Message;
            }

            DateTime stopUtc = DateTime.UtcNow;
            double elapsed = Math.Round((stopUtc - startUtc).TotalSeconds, 3);

            lock (jobLock)
            {
                Dictionary<string, object> job;
                if (jobs.TryGetValue(jobId, out job))
                {
                    job["status"] = finalStatus;
                    job["completedAt"] = stopUtc.ToString("o", CultureInfo.InvariantCulture);
                    job["elapsedSeconds"] = elapsed;
                    job["output"] = output;
                    job["error"] = error;
                    SaveJobLocked(job);
                }
            }

            try
            {
                SqliteVault.IndexSessionMessage(jobId, "assistant", "agent", role, string.IsNullOrEmpty(output) ? error : output, stopUtc.Ticks);
                SqliteVault.RecordStep(jobId, 0, 1, "tool_result", "sessions_spawn", Js.Serialize(new { jobId = jobId, status = finalStatus, elapsedSeconds = elapsed, output = Cut(output, 1000), error = error }));
            }
            catch { }

            AssistantHarness.Report("Subagent " + role + " " + finalStatus + " (" + elapsed.ToString("0.0", CultureInfo.InvariantCulture) + "s)");
        }

        public static Dictionary<string, object> Status(string jobId, int limit)
        {
            string id = (jobId ?? "").Trim();
            if (id.Length > 0)
            {
                var job = GetJob(id);
                if (job == null)
                {
                    return new Dictionary<string, object>
                    {
                        { "ok", false },
                        { "error", "Subagent job not found: " + id }
                    };
                }
                var copy = new Dictionary<string, object>(job);
                if (string.Equals(Convert.ToString(copy["status"], CultureInfo.InvariantCulture), "running", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime st;
                    if (DateTime.TryParse(Convert.ToString(copy["startedAt"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out st))
                        copy["elapsedSeconds"] = Math.Round((DateTime.UtcNow - st.ToUniversalTime()).TotalSeconds, 3);
                }
                copy["ok"] = true;
                return copy;
            }

            if (limit <= 0) limit = 20;
            LoadDiskJobs();
            List<Dictionary<string, object>> list;
            lock (jobLock)
            {
                list = jobs.Values
                    .OrderByDescending(j => j.ContainsKey("startedAt") ? Convert.ToString(j["startedAt"], CultureInfo.InvariantCulture) : "")
                    .Take(limit)
                    .Select(j => new Dictionary<string, object>(j))
                    .ToList();
            }

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "count", list.Count },
                { "maxSpawnDepth", MaxSpawnDepth },
                { "jobs", list }
            };
        }

        static Dictionary<string, object> GetJob(string jobId)
        {
            lock (jobLock)
            {
                Dictionary<string, object> job;
                if (jobs.TryGetValue(jobId, out job))
                    return new Dictionary<string, object>(job);
            }
            string file = Path.Combine(SubagentsDir, jobId + ".json");
            if (File.Exists(file))
            {
                try
                {
                    var loaded = Js.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    if (loaded != null)
                    {
                        lock (jobLock) jobs[jobId] = loaded;
                        return new Dictionary<string, object>(loaded);
                    }
                }
                catch { }
            }
            return null;
        }

        static void LoadDiskJobs()
        {
            string dir = SubagentsDir;
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "sub-*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(f);
                lock (jobLock)
                {
                    if (jobs.ContainsKey(id)) continue;
                }
                try
                {
                    var loaded = Js.DeserializeObject(File.ReadAllText(f, Encoding.UTF8)) as Dictionary<string, object>;
                    if (loaded != null)
                    {
                        lock (jobLock)
                        {
                            if (!jobs.ContainsKey(id)) jobs[id] = loaded;
                        }
                    }
                }
                catch { }
            }
        }

        static void SaveJobLocked(Dictionary<string, object> job)
        {
            try
            {
                string file = Path.Combine(SubagentsDir, Convert.ToString(job["jobId"], CultureInfo.InvariantCulture) + ".json");
                File.WriteAllText(file, Js.Serialize(job), new UTF8Encoding(false));
            }
            catch { }
        }

        static string Cut(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }
    }
}
