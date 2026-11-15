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

        [STAThread]
        static void Main(string[] args)
        {
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
            var app = new App(startup);
            if (page != null) app.ShowMain(page);
            try { AppApi.Start(); } catch (Exception ex) { App.Log("App API: " + ex.Message); }
            if (Store.Settings.OverlayOn) Ui.Later(1500, () => { try { Overlay.Set(true); } catch (Exception ex) { App.Log("Overlay: " + ex.Message); } });
            if (perf) Ui.Later(2500, Perf.Run);
            ThreadPool.RegisterWaitForSingleObject(showEv, delegate
            {
                try { app.Sync.BeginInvoke(new Action(() => app.ShowMain(null))); } catch { }
            }, null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(exitEv, delegate
            {
                try { app.Sync.BeginInvoke(new Action(() => app.Exit())); } catch { }
            }, null, -1, true);

            Application.Run(app);
            GC.KeepAlive(mutex);
            GC.KeepAlive(showEv);
            GC.KeepAlive(exitEv);
        }
    }
}
