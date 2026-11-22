using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    static class Program
    {
        const string MutexName = "StickyNotesPro_SingleInstance_v1";
        const string ShowEventName = "StickyNotesPro_Show_v1";
        const string ExitEventName = "StickyNotesPro_Exit_v1";

        // When the app is started from inside an AI agent session (for example a Claude Code terminal), it inherits that
        // session's variables (CLAUDE_CODE_CHILD_SESSION, CLAUDE_CODE_SESSION_ID, messaging socket and token...).
        // Every terminal and agent the app opens would inherit them too, and Claude there would think it is a child
        // session ("Transcript saving is off"). Remove them, but keep anything set in your permanent Windows settings.
        static void CleanInheritedAgentEnv()
        {
            try
            {
                foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
                {
                    string name = e.Key as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    string up = name.ToUpperInvariant();
                    // Agent session variables, plus the "plain output" ones an agent's shell sets (NO_COLOR made every
                    // terminal and CLI opened from the app lose its colors) and a parent terminal's own ids.
                    bool agent = up.StartsWith("CLAUDE") || up.StartsWith("CODEX_");
                    bool plain = up == "NO_COLOR" || up == "FORCE_COLOR" || up == "CLICOLOR" || up == "CLICOLOR_FORCE" || up == "TERM" || up == "WT_SESSION" || up == "WT_PROFILE_ID";
                    if (!agent && !plain) continue;
                    if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User) != null) continue;
                    if (Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine) != null) continue;
                    Environment.SetEnvironmentVariable(name, null);
                }
            }
            catch { }
        }

        [STAThread]   // must stay directly on Main: the UI, drag and drop and the clipboard need it
        static void Main(string[] args)
        {
            WebViewHost.Hook();   // browser engine DLLs are packed in the exe
            CleanInheritedAgentEnv();
            bool startup = false, exit = false;
            string page = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--page") page = args[i + 1];
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--data") try { Store.DirOverride = Path.GetFullPath(args[i + 1]); } catch { }
            foreach (var a in args)
            {
                string x = a.ToLowerInvariant();
                if (x == "/startup" || x == "--startup") startup = true;
                else if (x == "/exit" || x == "--exit") exit = true;
                else if (x == "--make-icon" || x == "/make-icon") { }
            }
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--make-icon") { File.WriteAllBytes(args[i + 1], Ui.IcoBytes()); return; }

            // Updater: this (new) exe replaces the running app's exe, restarts it and rolls back if it does not start.
            for (int i = 0; i < args.Length - 3; i++)
                if (args[i] == "--apply-update")
                {
                    int pid; int.TryParse(args[i + 2], out pid);
                    Environment.Exit(Releases.ApplyUpdate(Application.ExecutablePath, args[i + 1], pid, args[i + 3], Releases.UpdatesDir, Releases.Launch, 90000));
                    return;
                }
            // Release check before publishing: the exe starts, finds its packed resources and exits 0.
            if (Array.Exists(args, a => a.ToLowerInvariant() == "--selftest"))
            {
                var names = typeof(Program).Assembly.GetManifestResourceNames();
                bool ok = Array.Exists(names, n => n == "ChatSlides.pptx") && Array.Exists(names, n => n == "md.marked.js") && Array.Exists(names, n => n.StartsWith("webview2."));
                Environment.Exit(ok ? 0 : 3);
                return;
            }
            string updatedTo = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--updated") updatedTo = args[i + 1];

            // MCP server for AI agents (stdio). Runs as its own small process and talks to the running app.
            if (Array.Exists(args, a => a.ToLowerInvariant() == "--mcp")) { McpServer.Run(args); return; }

            if (Array.Exists(args, a => a.ToLowerInvariant() == "--uninstall"))
            {
                Application.EnableVisualStyles();
                Install.Uninstall();
                return;
            }

            if (exit)
            {
                try { using (var ev = EventWaitHandle.OpenExisting(ExitEventName)) ev.Set(); } catch { }
                return;
            }

            bool elevated = Array.Exists(args, a => a.ToLowerInvariant() == "--elevated" || a.ToLowerInvariant() == "--after-install");
            bool perf = Array.Exists(args, a => a.ToLowerInvariant() == "--perf");
            var mutex = new Mutex(false, MutexName);
            bool created;
            try { created = mutex.WaitOne(elevated ? 15000 : 0); }
            catch (AbandonedMutexException) { created = true; }
            if (!created)
            {
                try { using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set(); } catch { }
                return;
            }

            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => App.Log("UI error: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => App.Log("Fatal: " + e.ExceptionObject);

            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = g.DpiX / 96f;

            var showEv = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var exitEv = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
            App app;
            try { app = new App(startup); }
            catch (IOException ex) { MessageBox.Show(ex.Message, "All in One could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            catch (UnauthorizedAccessException ex) { MessageBox.Show(ex.Message, "All in One could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (page != null) app.ShowMain(page);
            try { AppApi.Start(); } catch (Exception ex) { App.Log("App API: " + ex.Message); }
            if (Store.Settings.OverlayOn) Ui.Later(1500, () => { try { Overlay.Set(true); } catch (Exception ex) { App.Log("Overlay: " + ex.Message); } });
            else if (Store.Settings.OverlayTaskbarHidden) try { Taskbar.Restore(); } catch (Exception ex) { App.Log("Taskbar restore: " + ex.Message); }   // after a crash
            if (perf) Ui.Later(2500, Perf.Run);
            // Team chat and updates (only when a team folder was set up). After an update: tell the updater we run fine.
            Ui.Later(3000, TeamNet.StartIfConfigured);
            if (updatedTo != null) { string v = updatedTo; Ui.Later(20000, () => Releases.SignalHealthy(v)); }
            var updateTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            updateTimer.Tick += delegate { try { Releases.MaybeInstall(); } catch (Exception ex) { App.Log("Update install: " + ex.Message); } };
            updateTimer.Start();
            ThreadPool.RegisterWaitForSingleObject(showEv, delegate
            {
                try { app.Sync.BeginInvoke(new Action(() => app.ShowMain(null))); } catch { }
            }, null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(exitEv, delegate
            {
                try { app.Sync.BeginInvoke(new Action(() => app.Exit())); } catch { }
            }, null, -1, false);

            Application.Run(app);
            GC.KeepAlive(mutex);
            GC.KeepAlive(showEv);
            GC.KeepAlive(exitEv);
        }
    }
}
