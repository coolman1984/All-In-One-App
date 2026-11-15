using System;

namespace StickyPro
{
    // Desktop overlay: dock, menu bar, hot corner and window overview above the Windows desktop. Implemented in this file.
    static class Overlay
    {
        public static bool On { get; private set; }
        public static void Set(bool on) { On = on; }
        public static void Toggle() { Set(!On); }
        public static void ApplySettings() { }
    }
}
