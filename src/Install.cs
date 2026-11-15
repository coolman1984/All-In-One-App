using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StickyPro
{
    // ---------------------------------------------------------------------------------------------------
    // Runs on any Windows 10 / 11 PC with nothing to install: the exe needs only .NET Framework 4.x, which is
    // part of Windows. Two ways to use it:
    //  - Portable: run AllInOne.exe from any writable folder; data is kept in a "data" folder next to it.
    //    In a read-only folder (Program Files) or a temporary folder (opened straight from a zip) the data goes
    //    to %LocalAppData%\AllInOne\data instead, so nothing is lost.
    //  - Installed for one Windows user (no admin): %LocalAppData%\Programs\All in One, with Start menu and
    //    Desktop shortcuts and an entry in Settings > Apps that uninstalls it.
    // ---------------------------------------------------------------------------------------------------
    static class Install
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AllInOne";
        public static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "All in One"); } }
        public static string InstalledExe { get { return Path.Combine(InstallDir, "AllInOne.exe"); } }
        public static string UserDataRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllInOne"); } }
        static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }

        public static bool IsInstalledCopy { get { return Same(ExeDir, InstallDir); } }
        // The source folder (src next to the exe): never offer to install the developer build.
        public static bool IsDevCopy { get { return Directory.Exists(Path.Combine(ExeDir, "src")) && File.Exists(Path.Combine(ExeDir, "build.ps1")); } }
        public static bool InTemp { get { return IsTempPath(ExeDir); } }

        static bool Same(string a, string b) { try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); } catch { return false; } }

        public static bool IsTempPath(string dir)
        {
            try
            {
                string full = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
                return full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || full.IndexOf(@"\Temporary Internet Files\", StringComparison.OrdinalIgnoreCase) >= 0
                    || full.IndexOf(@"\INetCache\", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        // Folder that holds "data" and "data-location.txt": next to the exe when it can be written there.
        public static string DataRoot(string exeDir)
        {
            if (Same(exeDir, InstallDir)) return UserDataRoot;   // installed: data lives apart from the program folder
            if (!IsTempPath(exeDir) && CanWrite(exeDir)) return exeDir;
            return UserDataRoot;
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".write-test-" + Process.GetCurrentProcess().Id + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // First run of a downloaded copy: offer to install it for this Windows user.
        public static bool OfferOnFirstRun()
        {
            if (IsInstalledCopy || IsDevCopy) return false;
            var r = MessageBox.Show(
                "Install " + App.Name + " for your Windows account?\n\n" +
                "It copies the app to your user folder, adds it to the Start menu and the Desktop and starts it with Windows. " +
                "No administrator rights are needed, and you can remove it in Settings > Apps.\n\n" +
                "Choose No to run it from this folder only (portable).", App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return false;
            return Run(true);
        }

        // Copies the running exe (and its data, if the installed copy has none) to the install folder, adds the
        // shortcuts and the Apps entry, then starts the installed copy. Returns true when this process should exit.
        public static bool Run(bool launch)
        {
            try
            {
                string me = Application.ExecutablePath;
                if (IsInstalledCopy) { Shortcuts(); Register(); return false; }
                Directory.CreateDirectory(InstallDir);
                // An older installed copy may be running: ask it to save and exit first.
                if (File.Exists(InstalledExe))
                {
                    var running = Process.GetProcessesByName("AllInOne").Where(p => { try { return Same(p.MainModule.FileName, InstalledExe); } catch { return false; } }).ToList();
                    foreach (var p in running) { try { Process.Start(new ProcessStartInfo(InstalledExe, "--exit") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(5000); p.WaitForExit(10000); } catch { } }
                }
                File.Copy(me, InstalledExe, true);
                CopyDataIfNew();
                Shortcuts();
                Register();
                if (launch)
                {
                    App.I.ExitForInstall(InstalledExe);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                App.Log("Install failed: " + ex);
                MessageBox.Show("Could not install: " + ex.Message + "\n\nThe app keeps running from this folder.", App.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        static void CopyDataIfNew()
        {
            string from = Store.Dir;
            string to = Path.Combine(DataRoot(InstallDir), "data");
            if (string.IsNullOrEmpty(from) || !Directory.Exists(from) || Same(from, to)) return;
            if (Directory.Exists(to) && Directory.EnumerateFileSystemEntries(to).Any()) return;   // keep data that is already there
            Store.SaveNow(); Store.FlushWrites();
            foreach (var f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(from.TrimEnd('\\').Length + 1);
                if (rel.Equals("error.log", StringComparison.OrdinalIgnoreCase)) continue;
                string dest = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                if (!File.Exists(dest)) try { File.Copy(f, dest); } catch { }
            }
        }

        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), App.Name + ".lnk"); } }
        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), App.Name + ".lnk"); } }

        static void Shortcuts()
        {
            foreach (var lnk in new[] { StartMenuLink, DesktopLink })
            {
                try
                {
                    var type = Type.GetTypeFromProgID("WScript.Shell");
                    object shell = Activator.CreateInstance(type);
                    object s = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    var st = s.GetType();
                    st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, s, new object[] { InstalledExe });
                    st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, s, new object[] { InstallDir });
                    st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, s, new object[] { InstalledExe + ",0" });
                    st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, s, new object[] { "Notes, prompts, clipboard, agents and system tools" });
                    st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, s, null);
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(s); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
                catch (Exception ex) { App.Log("Shortcut " + lnk + ": " + ex.Message); }
            }
        }

        // Entry in Settings > Apps (per user, no admin).
        static void Register()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", App.Name);
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("Publisher", App.Name);
                k.SetValue("DisplayVersion", File.GetLastWriteTime(InstalledExe).ToString("yyyy.M.d"));
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try { k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord); } catch { }
            }
        }

        // "AllInOne.exe --uninstall" (from Settings > Apps): close the app, remove shortcuts, startup entry and the
        // Apps entry, optionally the data, then delete the program folder after this process ends.
        public static void Uninstall()
        {
            var r = MessageBox.Show("Remove " + App.Name + " from this PC?\n\nYes = remove the app and keep your notes and data.\nNo = remove the app and delete your data too.\nCancel = keep everything.",
                App.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--exit") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(5000); } catch { }
            foreach (var p in Process.GetProcessesByName("AllInOne")) { if (p.Id == Process.GetCurrentProcess().Id) continue; try { p.WaitForExit(10000); } catch { } }
            foreach (var lnk in new[] { StartMenuLink, DesktopLink }) try { if (File.Exists(lnk)) File.Delete(lnk); } catch { }
            try { using (var k = Registry.CurrentUser.OpenSubKey(App.RunKey, true)) if (k != null) { string v = k.GetValue(App.RunName) as string; if (v != null && v.IndexOf(InstallDir, StringComparison.OrdinalIgnoreCase) >= 0) k.DeleteValue(App.RunName, false); } } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            var targets = new System.Collections.Generic.List<string> { InstallDir };
            if (r == DialogResult.No) { targets.Add(UserDataRoot); }
            // The exe cannot delete itself: a hidden cmd waits for this process to end, then removes the folders.
            string cmd = "/c ping 127.0.0.1 -n 3 >nul & " + string.Join(" & ", targets.Select(t => "rmdir /s /q \"" + t + "\""));
            try { Process.Start(new ProcessStartInfo("cmd.exe", cmd) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() }); } catch { }
            MessageBox.Show(App.Name + " was removed." + (r == DialogResult.Yes ? "\n\nYour data is kept in:\n" + Path.Combine(UserDataRoot, "data") + "\nInstall the app again to use it." : ""), App.Name);
        }
    }
}
