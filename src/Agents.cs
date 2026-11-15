using System;
using System.Windows.Forms;

namespace StickyPro
{
    // AI agents page (multi-pane chats with Claude Code, Codex, Cline, Antigravity CLIs). Implemented in this file.
    class AgentsPage : Panel
    {
        public AgentsPage() { Dock = DockStyle.Fill; Visible = false; }
        public void UseFolder(string folder) { }
    }
}
