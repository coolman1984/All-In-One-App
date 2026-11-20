# Exercise the native window layout with a stand-in editor on a separate UI thread.
param([string]$AssemblyPath = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing @'
using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public sealed class OfficeTestWindow : Form
{
    public OfficeTestWindow()
    {
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(SystemInformation.VirtualScreen.Left - 2000, 100, 560, 380);
    }
    protected override bool ShowWithoutActivation { get { return true; } }
}

public sealed class OfficeTestPeer : IDisposable
{
    readonly ManualResetEvent ready = new ManualResetEvent(false);
    readonly ManualResetEvent release = new ManualResetEvent(false);
    readonly Thread thread;
    OfficeTestWindow editor, dialog;
    public IntPtr Editor { get; private set; }
    public IntPtr Dialog { get; private set; }

    public OfficeTestPeer(IntPtr desktop)
    {
        thread = new Thread(delegate()
        {
            if (!OfficeTestNative.SetThreadDesktop(desktop)) { ready.Set(); return; }
            using (editor = new OfficeTestWindow())
            using (dialog = new OfficeTestWindow())
            {
                editor.ShowInTaskbar = true;
                Editor = editor.Handle;
                dialog.Owner = editor;
                Dialog = dialog.Handle;
                editor.BeginInvoke(new Action(() => ready.Set()));
                Application.Run();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!ready.WaitOne(5000) || Editor == IntPtr.Zero) throw new InvalidOperationException("Editor fixture did not start on its test desktop.");
    }

    public void Pause()
    {
        ready.Reset();
        editor.BeginInvoke(new Action(delegate { ready.Set(); release.WaitOne(4000); }));
        if (!ready.WaitOne(5000)) throw new InvalidOperationException("Editor fixture did not pause.");
    }

    public void Resume() { release.Set(); }

    public void PlaceBehind(IntPtr host)
    {
        editor.BeginInvoke(new Action(() => OfficeTestNative.SetWindowPos(Editor, host, 0, 0, 0, 0, 0x13)));
    }

    public void Dispose()
    {
        release.Set();
        editor.BeginInvoke(new Action(Application.ExitThread));
        if (!thread.Join(5000)) throw new InvalidOperationException("Editor fixture did not exit.");
        release.Dispose();
        ready.Dispose();
    }
}

public static class OfficeTestNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr desktop);
    public static IntPtr TestDesktop()
    {
        IntPtr desktop = CreateDesktop("OfficeTest-" + Guid.NewGuid().ToString("N"), IntPtr.Zero, IntPtr.Zero, 0, 0x1FF, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return desktop;
    }
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    public static Rectangle Bounds(IntPtr window)
    {
        RECT rect;
        if (!GetWindowRect(window, out rect)) throw new InvalidOperationException("Fixture window was lost.");
        return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }
    public static bool Above(IntPtr window, IntPtr other)
    {
        for (IntPtr next = GetWindow(other, 3); next != IntPtr.Zero; next = GetWindow(next, 3))
            if (next == window) return true;
        return false;
    }
}
public static class OfficeTestRun
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Assert(bool value, string failure) { if (!value) throw new InvalidOperationException(failure); }
    static void WaitFor(Func<bool> check, string failure)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 3000)
        {
            Application.DoEvents();
            if (check()) return;
            Thread.Sleep(20);
        }
        throw new InvalidOperationException(failure);
    }
    public static void Run(string path)
    {
        IntPtr desktop = OfficeTestNative.TestDesktop();
        Exception failure = null;
        var thread = new Thread(delegate()
        {
            try
            {
                if (!OfficeTestNative.SetThreadDesktop(desktop)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Cases(Assembly.LoadFile(path), desktop);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!thread.Join(30000)) throw new InvalidOperationException("Office editor fixture timed out.");
        OfficeTestNative.CloseDesktop(desktop);
        if (failure != null) throw new InvalidOperationException("Office editor check failed.", failure);
    }
    static void Cases(Assembly assembly, IntPtr desktop)
    {
        var type = assembly.GetType("StickyPro.OfficeEdit", true);
        var window = type.GetField("wnd", Hidden);
        var place = type.GetMethod("Place", Hidden, null, new[] { typeof(IntPtr) }, null);
        using (var host = new OfficeTestWindow())
        using (var peer = new OfficeTestPeer(desktop))
        {
            var edit = (Control)Activator.CreateInstance(type, true);
            try
            {
                edit.Bounds = new Rectangle(120, 52, 405, 260);
                host.Controls.Add(edit);
                host.Show();
                var hostHandle = host.Handle;
                window.SetValue(edit, peer.Editor);
                type.GetMethod("Host", Hidden).Invoke(edit, null);
                Assert(OfficeTestNative.GetWindow(peer.Editor, 4) == IntPtr.Zero, "The editor has a Win32 owner.");
                Assert((OfficeTestNative.GetWindowLong(peer.Editor, -16) & 0x40000000) == 0, "The editor is a child window.");
                var follow = (System.Windows.Forms.Timer)type.GetField("follow", Hidden).GetValue(edit);
                follow.Stop();
                Action<IntPtr> position = handle => place.Invoke(edit, new object[] { handle });
                position(hostHandle);
                var expected = edit.RectangleToScreen(edit.ClientRectangle);
                WaitFor(() => OfficeTestNative.IsWindowVisible(peer.Editor) && OfficeTestNative.Bounds(peer.Editor) == expected, "The editor did not fit the pane.");
                Assert((OfficeTestNative.GetWindowLong(peer.Editor, -20) & 0x8) == 0, "The editor became topmost.");
                Assert(OfficeTestNative.IsWindowEnabled(hostHandle), "The host became disabled.");

                position(peer.Dialog);
                Assert(OfficeTestNative.IsWindowVisible(peer.Editor), "An editor dialog hid its document.");
                position(IntPtr.Zero);
                WaitFor(() => !OfficeTestNative.IsWindowVisible(peer.Editor), "The editor remained visible after switching away.");
                position(hostHandle);
                WaitFor(() => OfficeTestNative.IsWindowVisible(peer.Editor), "The editor did not return with the host.");

                // Raise the host after activation was already handled.
                peer.PlaceBehind(hostHandle);
                WaitFor(() => OfficeTestNative.Above(hostHandle, peer.Editor), "Could not establish the host-in-front fixture state.");
                WaitFor(delegate { position(hostHandle); return OfficeTestNative.Above(peer.Editor, hostHandle); }, "The editor did not recover from a late host raise.");

                edit.Visible = false;
                WaitFor(() => !OfficeTestNative.IsWindowVisible(peer.Editor), "The editor stayed above another page or tab.");
                edit.Visible = true;
                follow.Stop();
                position(hostHandle);
                WaitFor(() => OfficeTestNative.IsWindowVisible(peer.Editor), "The editor did not return to its tab.");

                peer.Pause();
                var clock = Stopwatch.StartNew();
                position(IntPtr.Zero);
                Assert(clock.ElapsedMilliseconds < 2000, "Hiding the busy editor blocked the host.");
                peer.Resume();
                WaitFor(() => !OfficeTestNative.IsWindowVisible(peer.Editor), "The queued hide did not take effect.");
            }
            finally { window.SetValue(edit, IntPtr.Zero); }
        }
    }
}
'@

if (-not $AssemblyPath) {
    $root = Split-Path -Parent $PSScriptRoot
    $AssemblyPath = Join-Path $root 'build-test\StickyNotesPro.test.exe'
}
try { [OfficeTestRun]::Run([IO.Path]::GetFullPath($AssemblyPath)) }
catch { throw $_.Exception.GetBaseException() }
Write-Host 'OFFICE EDIT OK: isolated desktop, separate input queues, pane bounds, late stacking, dialog visibility and nonblocking hide'
