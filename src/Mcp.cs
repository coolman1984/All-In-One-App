using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StickyPro
{
    class McpError : Exception { public int Code; public McpError(int code, string msg) : base(msg) { Code = code; } }

    // ---------------------------------------------------------------------------------------------------
    // "AllInOne.exe --mcp": MCP server over stdio (JSON-RPC 2.0, one message per line) for any AI agent.
    // It forwards every tool call to the running app through a named pipe (AppApi) and starts the app
    // in the tray when it is not running. Add it to an agent, e.g.:
    //   claude mcp add all-in-one -- "C:\path\AllInOne.exe" --mcp
    // ---------------------------------------------------------------------------------------------------
    static class McpServer
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        static readonly object writeLock = new object();
        static StreamWriter stdout;
        static readonly string[] Versions = { "2025-06-18", "2025-03-26", "2024-11-05" };

        public static void Run(string[] args)
        {
            var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            var pending = new CountdownEvent(1);
            string line;
            while ((line = stdin.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0) continue;
                Dictionary<string, object> req;
                try { req = Js.Deserialize<Dictionary<string, object>>(line); }
                catch { Send(Error(null, -32700, "Parse error")); continue; }
                if (req == null) continue;
                object id; req.TryGetValue("id", out id);
                if (id == null) continue;   // notifications (initialized, cancelled) need no answer
                string method = Str(req, "method");
                var prms = (req.ContainsKey("params") ? req["params"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();
                // Tool calls can take minutes (agent_ask); answer each request on its own thread.
                pending.AddCount();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { Send(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", Handle(method, prms) } }); }
                    catch (McpError e) { Send(Error(id, e.Code, e.Message)); }
                    catch (Exception e) { Send(Error(id, -32603, e.Message)); }
                    finally { pending.Signal(); }
                });
            }
            pending.Signal();
            pending.Wait(TimeSpan.FromMinutes(2));
        }

        static void Send(object msg) { string s = Js.Serialize(msg); lock (writeLock) stdout.WriteLine(s); }
        static Dictionary<string, object> Error(object id, int code, string msg)
        {
            return new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "error", new Dictionary<string, object> { { "code", code }, { "message", msg } } } };
        }
        internal static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }

        static object Handle(string method, Dictionary<string, object> p)
        {
            switch (method)
            {
                case "initialize":
                    string want = Str(p, "protocolVersion");
                    return new Dictionary<string, object>
                    {
                        { "protocolVersion", Versions.Contains(want) ? want : Versions[0] },
                        { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object>() }, { "prompts", new Dictionary<string, object>() } } },
                        { "serverInfo", new Dictionary<string, object> { { "name", "all-in-one" }, { "title", "All in One" }, { "version", "1.0" } } },
                        { "instructions", "Local desktop app of the user: sticky notes, tasks, prompt library, clipboard history, favorite folders and multi-agent coding chats (Claude Code, Codex, Cline, Antigravity). Read before you write; prefer updating an item over creating a duplicate. Times are local, ISO 8601." }
                    };
                case "ping": return new Dictionary<string, object>();
                case "tools/list": return new Dictionary<string, object> { { "tools", ApiTools.Schema() } };
                case "tools/call":
                {
                    string name = Str(p, "name");
                    var a = (p.ContainsKey("arguments") ? p["arguments"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();
                    if (!ApiTools.Names.Contains(name)) throw new McpError(-32602, "Unknown tool: " + name);
                    var r = CallApp(name, a);
                    bool ok = r.ContainsKey("ok") && true.Equals(r["ok"]);
                    string text = ok ? Str(r, "text") : "Error: " + Str(r, "error");
                    return new Dictionary<string, object> { { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } }, { "isError", !ok } };
                }
                case "prompts/list":
                {
                    var r = CallApp("_prompts", new Dictionary<string, object>());
                    var list = Js.Deserialize<List<Dictionary<string, object>>>(Str(r, "text"));
                    return new Dictionary<string, object> { { "prompts", list.Select(x => new Dictionary<string, object> {
                        { "name", Str(x, "name") }, { "title", Str(x, "title") }, { "description", Str(x, "description") },
                        { "arguments", ((System.Collections.ArrayList)x["vars"]).Cast<object>().Select(v => new Dictionary<string, object> { { "name", Convert.ToString(v) }, { "required", false } }).ToArray() } }).ToArray() } };
                }
                case "prompts/get":
                {
                    var a = (p.ContainsKey("arguments") ? p["arguments"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();
                    var r = CallApp("_prompt_fill", new Dictionary<string, object> { { "name", Str(p, "name") }, { "values", a } });
                    if (!(r.ContainsKey("ok") && true.Equals(r["ok"]))) throw new McpError(-32602, Str(r, "error"));
                    return new Dictionary<string, object> { { "messages", new object[] { new Dictionary<string, object> { { "role", "user" }, { "content", new Dictionary<string, object> { { "type", "text" }, { "text", Str(r, "text") } } } } } } };
                }
                case "resources/list": return new Dictionary<string, object> { { "resources", new object[0] } };
                case "resources/templates/list": return new Dictionary<string, object> { { "resourceTemplates", new object[0] } };
                default: throw new McpError(-32601, "Method not found: " + method);
            }
        }

        // One request per pipe connection: {"tool":..., "args":{...}} -> {"ok":true,"text":...} | {"ok":false,"error":...}
        static Dictionary<string, object> CallApp(string tool, Dictionary<string, object> args)
        {
            NamedPipeClientStream pipe = null;
            var until = DateTime.UtcNow.AddSeconds(25);
            bool started = false;
            while (true)
            {
                pipe = new NamedPipeClientStream(".", AppApi.PipeName, PipeDirection.InOut);
                try { pipe.Connect(1200); break; }
                catch (Exception)
                {
                    pipe.Dispose(); pipe = null;
                    if (!started)
                    {
                        started = true;
                        try { Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--startup") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath) }); }
                        catch (Exception ex) { return Fail("The All in One app is not running and could not be started: " + ex.Message); }
                    }
                    if (DateTime.UtcNow > until) return Fail("The All in One app did not answer. Start it and try again.");
                    Thread.Sleep(400);
                }
            }
            using (pipe)
            {
                var w = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                var r = new StreamReader(pipe, new UTF8Encoding(false));
                w.WriteLine(Js.Serialize(new Dictionary<string, object> { { "tool", tool }, { "args", args } }));
                string line = r.ReadLine();
                if (line == null) return Fail("The app closed the connection.");
                return Js.Deserialize<Dictionary<string, object>>(line) ?? Fail("Empty answer from the app.");
            }
        }
        static Dictionary<string, object> Fail(string e) { return new Dictionary<string, object> { { "ok", false }, { "error", e } }; }
    }

    // ---------------------------------------------------------------------------------------------------
    // Local API inside the running app (named pipe, current session). Used by the MCP server and scripts.
    // ---------------------------------------------------------------------------------------------------
    static class AppApi
    {
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        public static string PipeName { get { return "AllInOne.Api.v1." + Process.GetCurrentProcess().SessionId; } }
        static Thread listener;
        static volatile bool stopping;

        public static void Start()
        {
            if (listener != null) return;
            listener = new Thread(Listen) { IsBackground = true, Name = "App API" };
            listener.Start();
        }
        public static void Stop() { stopping = true; }

        static void Listen()
        {
            while (!stopping)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    server.WaitForConnection();
                    var s = server; server = null;
                    new Thread(() => Serve(s)) { IsBackground = true, Name = "App API client" }.Start();
                }
                catch (Exception ex)
                {
                    if (server != null) server.Dispose();
                    App.Log("App API: " + ex.Message);
                    Thread.Sleep(2000);
                }
            }
        }

        static void Serve(NamedPipeServerStream s)
        {
            using (s)
            {
                try
                {
                    var r = new StreamReader(s, new UTF8Encoding(false));
                    var w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                    string line = r.ReadLine();
                    if (line == null) return;
                    Dictionary<string, object> answer;
                    try
                    {
                        var req = Js.Deserialize<Dictionary<string, object>>(line);
                        string tool = McpServer.Str(req, "tool");
                        var args = (req.ContainsKey("args") ? req["args"] as Dictionary<string, object> : null) ?? new Dictionary<string, object>();
                        answer = new Dictionary<string, object> { { "ok", true }, { "text", ApiTools.Run(tool, args) } };
                    }
                    catch (Exception ex)
                    {
                        var inner = ex is System.Reflection.TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                        answer = new Dictionary<string, object> { { "ok", false }, { "error", inner.Message } };
                    }
                    w.WriteLine(Js.Serialize(answer));
                }
                catch (Exception ex) { App.Log("App API client: " + ex.Message); }
            }
        }
    }
}
