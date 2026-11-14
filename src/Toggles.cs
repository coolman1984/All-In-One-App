using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StickyPro
{
    // Runs a console program with no window and returns its output (worker threads only).
    static class Hidden
    {
        public static string Run(string file, string args, int timeoutMs, out int exitCode)
        {
            exitCode = -1;
            var psi = new ProcessStartInfo(file, args)
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var p = new Process { StartInfo = psi })
            {
                var sb = new StringBuilder();
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { };
                p.Start();
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                p.WaitForExit();
                exitCode = p.ExitCode;
                lock (sb) return sb.ToString();
            }
        }
        public static string Run(string file, string args, int timeoutMs) { int c; return Run(file, args, timeoutMs, out c); }

        public static string PowerShell(string script, int timeoutMs)
        {
            string enc = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; " + script));
            return Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + enc, timeoutMs);
        }

        public static void Async(Func<string> work, Action<string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string r;
                try { r = work(); } catch (Exception ex) { r = "Error: " + ex.Message; }
                try { App.I.Sync.BeginInvoke(new Action(() => done(r))); } catch { }
            });
        }

        public static void Open(string file, string args = "")
        {
            try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); }
            catch (Exception ex) { App.Log("Open " + file + ": " + ex.Message); App.I.Balloon("Could not open", file); }
        }
    }

    // Keep the screen and PC awake (no sleep, no screensaver) while the app runs.
    static class Awake
    {
        [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
        const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x1, ES_DISPLAY_REQUIRED = 0x2;
        public static bool On { get; private set; }
        public static event Action Changed;
        // UI thread only: the state belongs to the calling thread, which lives as long as the app.
        public static void Set(bool on)
        {
            SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED : ES_CONTINUOUS);
            On = on;
            if (Store.Settings.KeepAwake != on) { Store.Settings.KeepAwake = on; Store.MarkSettings(); }
            var h = Changed; if (h != null) h();
        }
    }

    // Wi-Fi radio on/off through Windows.Devices.Radios (no admin needed).
    static class WiFi
    {
        public static string State = "?";   // On | Off | None | ? | Error...
        public static bool Busy;
        public static event Action Changed;
        const string Script = @"param([string]$Action = 'get')
$ErrorActionPreference = 'Stop'
try {
  Add-Type -AssemblyName System.Runtime.WindowsRuntime
  $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
  function Await($op, [Type]$type) { $t = $asTask.MakeGenericMethod($type).Invoke($null, @($op)); $t.Wait(-1) | Out-Null; $t.Result }
  [Windows.Devices.Radios.Radio, Windows.System.Devices, ContentType = WindowsRuntime] | Out-Null
  [Windows.Devices.Radios.RadioAccessStatus, Windows.System.Devices, ContentType = WindowsRuntime] | Out-Null
  [Windows.Devices.Radios.RadioState, Windows.System.Devices, ContentType = WindowsRuntime] | Out-Null
  Await ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null
  $radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
  $wifi = $radios | Where-Object { $_.Kind -eq 'WiFi' } | Select-Object -First 1
  if (-not $wifi) { 'None'; exit 0 }
  if ($Action -eq 'on')  { Await ($wifi.SetStateAsync([Windows.Devices.Radios.RadioState]::On))  ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null }
  if ($Action -eq 'off') { Await ($wifi.SetStateAsync([Windows.Devices.Radios.RadioState]::Off)) ([Windows.Devices.Radios.RadioAccessStatus]) | Out-Null }
  if ($wifi.State -eq 'On') { 'On' } else { 'Off' }
} catch { 'Error: ' + $_.Exception.Message }
";
        public static void Refresh() { Run("get"); }
        public static void Toggle() { Run(State == "On" ? "off" : "on"); }

        static void Run(string arg)
        {
            if (Busy) return;
            Busy = true; Raise();
            Hidden.Async(() =>
            {
                string dir = Path.Combine(Store.Dir, "tools"); Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "wifi.ps1");
                if (!File.Exists(file) || File.ReadAllText(file) != Script) File.WriteAllText(file, Script, new UTF8Encoding(true));
                string o = Hidden.Run("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + file + "\" " + arg, 20000);
                if (o == null) return "Error: timed out";
                string last = "";
                foreach (var line in o.Split('\n')) if (line.Trim().Length > 0) last = line.Trim();
                return last.Length == 0 ? "Error: no answer" : last;
            }, r =>
            {
                Busy = false;
                if (r.StartsWith("Error")) App.I.Balloon("Wi-Fi", r); else State = r;
                Raise();
            });
        }
        static void Raise() { var h = Changed; if (h != null) h(); }
    }

    static class DateTimeTools
    {
        public static void OpenSettings() { Hidden.Open("ms-settings:dateandtime"); }
        public static void OpenClassic() { Hidden.Open("control.exe", "timedate.cpl"); }
        public static void OpenRegion() { Hidden.Open("ms-settings:regionformatting"); }
        public static void SyncNow()
        {
            try { Process.Start(new ProcessStartInfo("cmd.exe", "/c w32tm /resync & timeout /t 3") { Verb = "runas", UseShellExecute = true }); } catch { }
        }
        public static ContextMenuStrip Menu()
        {
            var m = Ui.Menu();
            m.Items.Add("Date & time settings", null, delegate { OpenSettings(); });
            m.Items.Add("Change date / time (classic)", null, delegate { OpenClassic(); });
            m.Items.Add("Region & format", null, delegate { OpenRegion(); });
            m.Items.Add("Sync time now (admin)", null, delegate { SyncNow(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Copy date and time", null, delegate { App.I.CopyText(DateTime.Now.ToString("yyyy-MM-dd HH:mm")); });
            Ui.ThemeMenu(m);
            return m;
        }
    }
}
