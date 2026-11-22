using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace StickyPro
{
    // Types a prompt's text when you type its snippet code (e.g. ";sig") in any app.
    // The keyboard hook is installed only while at least one prompt has a code and snippets are on.
    static class Snippets
    {
        static Native.HookProc proc;
        static IntPtr hook = IntPtr.Zero;
        static readonly StringBuilder buf = new StringBuilder();
        static IntPtr lastWindow;
        static int generation;
        static int bufferLimit = 65;
        static List<KeyValuePair<string, Prompt>> triggers = new List<KeyValuePair<string, Prompt>>();
        static readonly uint myPid = (uint)Process.GetCurrentProcess().Id;

        public static bool Active { get { return hook != IntPtr.Zero; } }

        public static void Update()
        {
            triggers = Store.Prompts
                .Where(p => !string.IsNullOrWhiteSpace(p.Trigger) && p.Trigger.Trim().Length >= 2)
                .Select(p => new KeyValuePair<string, Prompt>(p.Trigger.Trim(), p))
                .OrderByDescending(k => k.Key.Length)
                .ToList();
            bufferLimit = triggers.Count == 0 ? 65 : Math.Max(65, triggers[0].Key.Length + 1);
            buf.Clear(); generation++;
            bool want = Store.Settings.Snippets && triggers.Count > 0;
            if (want && hook == IntPtr.Zero)
            {
                proc = Proc;
                hook = Native.SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, Native.GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) App.Log("Snippet keyboard hook failed: " + Marshal.GetLastWin32Error());
            }
            else if (!want && hook != IntPtr.Zero) Stop();
        }

        public static void Stop()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            buf.Clear();
            generation++;
            lastWindow = IntPtr.Zero;
        }

        static IntPtr Proc(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                int msg = wParam.ToInt32();
                if (code >= 0 && (msg == 0x100 || msg == 0x104)) OnKey(lParam);
            }
            catch { }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        static void OnKey(IntPtr lParam)
        {
            var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
            if ((k.flags & 0x10) != 0) return;                       // injected by software (also our own typing)
            int currentGeneration = ++generation;
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground != lastWindow) { buf.Clear(); lastWindow = foreground; }
            uint vk = k.vkCode;
            if (vk == 0x10 || vk == 0x11 || vk == 0x12 || vk == 0x14 || (vk >= 0xA0 && vk <= 0xA5)) return;
            bool ctrl = Native.Down(0x11), alt = Native.Down(0x12);
            bool altGr = ctrl && alt && Native.Down(0xA5);
            if (((ctrl || alt) && !altGr) || Native.Down(0x5B) || Native.Down(0x5C)) { buf.Clear(); return; }
            if (vk == 0x08) { if (buf.Length > 0) buf.Length--; return; }

            uint pid;
            uint tid = Native.GetWindowThreadProcessId(foreground, out pid);
            if (pid == myPid) { buf.Clear(); return; }               // never expand inside this app (e.g. while editing codes)

            var state = new byte[256];
            if (Native.Down(0x10)) state[0x10] = 0x80;
            if (altGr) { state[0x11] = 0x80; state[0x12] = 0x80; state[0xA5] = 0x80; }
            if ((Native.GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
            var sb = new StringBuilder(8);
            int n = Native.ToUnicodeEx(vk, k.scanCode, state, sb, sb.Capacity, 4 /* keep keyboard state */, Native.GetKeyboardLayout(tid));
            if (n != 1) { buf.Clear(); return; }
            char c = sb[0];
            if (char.IsControl(c)) { buf.Clear(); return; }
            buf.Append(c);
            if (buf.Length > bufferLimit) buf.Remove(0, buf.Length - bufferLimit);

            string s = buf.ToString();
            foreach (var t in triggers)
            {
                if (MatchesTrigger(s, t.Key))
                {
                    buf.Clear();
                    var p = t.Value;
                    int len = t.Key.Length;
                    App.I.Sync.BeginInvoke(new Action(() => Ui.Later(40, () =>
                    {
                        if (generation == currentGeneration && Native.IsWindow(foreground) && Native.GetForegroundWindow() == foreground) Expand(p, len, foreground);
                    })));
                    return;
                }
            }
        }

        static bool MatchesTrigger(string buffer, string trigger)
        {
            if (!buffer.EndsWith(trigger, StringComparison.Ordinal)) return false;
            int start = buffer.Length - trigger.Length;
            return start == 0 || !WordChar(trigger[0]) || !WordChar(buffer[start - 1]);
        }

        static bool WordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        static void Expand(Prompt p, int len, IntPtr target)
        {
            try
            {
                string text = p.Body ?? "";
                if (Ui.HasVariables(text))
                {
                    text = Ui.FillVariables(text, null);
                    if (text == null) return;
                    Native.SetForegroundWindow(target);
                }
                if (!Native.IsWindow(target) || Native.GetForegroundWindow() != target) return;
                SendKeys.SendWait("{BACKSPACE " + len + "}");
                App.I.PasteKeepingClipboard(text);
                p.Uses++;
                p.LastUsed = Store.Now();
                Store.MarkPrompts();
            }
            catch (Exception ex) { App.Log("Snippet failed: " + ex.Message); }
        }
    }

    // Global shortcut definitions and parsing.
    static class Hotkeys
    {
        // id key, description, default, short help
        public static readonly string[][] Actions =
        {
            new[] { "clips",     "Clipboard history popup",          "Ctrl+Alt+V", "Clipboard" },
            new[] { "prompts",   "Prompts popup",                    "Ctrl+Alt+P", "Prompts" },
            new[] { "note",      "New sticky note",                  "Ctrl+Alt+N", "New note" },
            new[] { "addprompt", "Save selected text as prompt",     "Ctrl+Alt+A", "Selection to prompt" },
            new[] { "plain",     "Paste clipboard as plain text",    "Ctrl+Alt+T", "Paste plain text" },
            new[] { "queue",     "Start / stop paste queue",         "Ctrl+Alt+Q", "Start/stop queue" },
            new[] { "next",      "Paste next item from queue",       "Ctrl+Alt+W", "Paste next in queue" },
            new[] { "toggle",    "Show / hide all notes",            "Ctrl+Alt+H", "Show/hide notes" },
            new[] { "cards",     "Show / hide floating cards",       "Ctrl+Alt+C", "Show/hide cards" },
            new[] { "snip",      "New snip (screenshot)",            "Ctrl+Alt+X", "Snip" },
            new[] { "repeat",    "Repeat last capture",              "Ctrl+Alt+D", "Repeat capture" },
            new[] { "ocr",       "Copy text from the screen (OCR)",  "Ctrl+Alt+O", "Text from screen" },
            new[] { "record",    "Start / stop screen recording",    "Ctrl+Alt+R", "Record screen" },
            new[] { "tagfile",   "Tag files selected in Explorer",   "Ctrl+Alt+F", "Tag Explorer files" },
            new[] { "main",      "Open main window",                 "Ctrl+Alt+S", "This window" },
            new[] { "picker",    "Color Picker",                     "Ctrl+Alt+K", "Color picker" },
            new[] { "magnifier", "Magnifier",                        "Ctrl+Alt+M", "Magnifier" },
            new[] { "whiteboard","WhiteBoard (draw on the screen)",  "Ctrl+Alt+B", "WhiteBoard" },
            new[] { "newtask",   "New task / reminder",              "Ctrl+Alt+J", "New task" },
            new[] { "run",       "Quick run box",                    "Ctrl+Alt+G", "Quick run" },
            new[] { "ontop",     "Pin the current window on top",    "Ctrl+Alt+U", "Always on top" },
            new[] { "awake",     "Keep awake on / off",              "",           "Keep awake" },
            new[] { "pwshhere",  "PWSH in the File Explorer folder", "Ctrl+Alt+E", "PWSH here" },
            new[] { "terminals", "Launch my terminals workspace",    "Ctrl+Alt+L", "Terminals" },
            new[] { "overview",  "Show all windows (overview)",      "Ctrl+Alt+Up", "All windows" },
            new[] { "overlay",   "Desktop overlay on / off",         "Ctrl+Alt+Z", "Overlay" },
        };

        public static string Get(string action)
        {
            string v;
            if (Store.Settings.Hotkeys != null && Store.Settings.Hotkeys.TryGetValue(action, out v)) return v ?? "";
            foreach (var a in Actions) if (a[0] == action) return a[2];
            return "";
        }

        public static bool Parse(string s, out uint mods, out Keys key)
        {
            mods = 0; key = Keys.None;
            if (string.IsNullOrWhiteSpace(s)) return false;
            foreach (var raw in s.Split('+'))
            {
                string part = raw.Trim();
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win": mods |= Native.MOD_WIN; break;
                    default:
                        Keys k;
                        if (key != Keys.None) return false;
                        if (part.Length == 1 && char.IsDigit(part[0])) key = Keys.D0 + (part[0] - '0');
                        else if (Enum.TryParse(part, true, out k) && Enum.IsDefined(typeof(Keys), k) && (k & Keys.KeyCode) == k && (int)k > 0 && (int)k <= 254) key = k;
                        else return false;
                        if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.LShiftKey || key == Keys.RShiftKey || key == Keys.LMenu || key == Keys.RMenu || key == Keys.LWin || key == Keys.RWin) return false;
                        break;
                }
            }
            return key != Keys.None && (mods != 0 || (key >= Keys.F1 && key <= Keys.F24));
        }

        public static string Format(bool ctrl, bool alt, bool shift, bool win, Keys key)
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            if (key != Keys.None)
            {
                string name = key.ToString();
                if (key >= Keys.D0 && key <= Keys.D9) name = ((int)(key - Keys.D0)).ToString();
                parts.Add(name);
            }
            return string.Join("+", parts);
        }
    }
}
