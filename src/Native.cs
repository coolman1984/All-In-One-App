using System;
using System.Runtime.InteropServices;

namespace StickyPro
{
    static class Native
    {
        public const int WM_SETREDRAW = 0x000B;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int WM_NCHITTEST = 0x0084;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCAPTION = 2;
        public const int HTTRANSPARENT = -1;
        public const int EM_SETCUEBANNER = 0x1501;
        public const int CS_DROPSHADOW = 0x00020000;

        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Windows applies the launcher's show mode (e.g. minimized) to the first window; undo that.
        public static void EnsureRestored(System.Windows.Forms.Form f)
        {
            if (f.IsHandleCreated && IsIconic(f.Handle)) ShowWindow(f.Handle, 9 /* SW_RESTORE */);
        }

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern int GetScrollPos(IntPtr hWnd, int bar);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ToUnicodeEx(uint vk, uint scan, byte[] keyState, [Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder buf, int bufSize, uint flags, IntPtr hkl);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        public static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        [DllImport("psapi.dll")]
        public static extern bool EmptyWorkingSet(IntPtr hProcess);
    }
}
