using System;

namespace StickyPro
{
    // "AllInOne.exe --mcp": MCP server over stdio for any AI agent; forwards tool calls to the running app (AppApi).
    static class McpServer
    {
        public static void Run(string[] args) { }
    }

    // Local API inside the running app (named pipe), used by the MCP server and other tools.
    static class AppApi
    {
        public static void Start() { }
        public static void Stop() { }
    }
}
