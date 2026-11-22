using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace StickyPro
{
    // Full automation for the app's own browser (Edge WebView2), used by agents through MCP / the assistant:
    //   snapshot with element refs, real (trusted) mouse and keyboard input through the DevTools protocol,
    //   fill / select / hover / scroll / wait, tabs, screenshots, PDF, file upload, downloads, dialogs,
    //   console and network logs, cookies, device emulation and data extraction.
    // The user always sees it: a red bar names the action and has a Stop button; the element acted on flashes.
    partial class BrowserPage
    {
        internal class TabLog
        {
            public readonly List<Dictionary<string, object>> Console = new List<Dictionary<string, object>>();
            public readonly List<Dictionary<string, object>> Net = new List<Dictionary<string, object>>();
            public readonly Dictionary<string, Dictionary<string, object>> ById = new Dictionary<string, Dictionary<string, object>>();
            public int Inflight; public DateTime LastNet = DateTime.Now;
            public bool Loading; public bool LastOk = true; public string LastError = ""; public int LastStatus; public string OriginalAgent;
        }

        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        public static bool AgentsPaused;
        public string DialogMode = "user";          // user | accept | dismiss
        public string DialogPromptText;
        readonly List<Dictionary<string, object>> dialogLog = new List<Dictionary<string, object>>();
        readonly List<Dictionary<string, object>> downloadLog = new List<Dictionary<string, object>>();
        static readonly List<string> actionLog = new List<string>();
        int pendingDialogs;
        public bool DialogOpen { get { return pendingDialogs > 0; } }
        Panel autoBar; Label autoText; Button autoStop; System.Windows.Forms.Timer autoHide; DateTime lastAction;

        // ---------- wiring (called from Init for every tab) ----------
        void AutoAttach(Tab t, CoreWebView2 core)
        {
            var log = t.Log;
            core.NavigationStarting += delegate { log.Loading = true; };
            core.NavigationCompleted += (s, a) =>
            {
                log.Loading = false; log.LastOk = a.IsSuccess; log.LastError = a.IsSuccess ? "" : a.WebErrorStatus.ToString();
                try { log.LastStatus = a.HttpStatusCode; } catch { }
            };
            try { core.Settings.AreDefaultScriptDialogsEnabled = false; core.ScriptDialogOpening += (s, a) => OnDialog(a); } catch { }
            core.DownloadStarting += (s, a) => OnDownload(a);
            try
            {
                Hook(core, "Runtime.consoleAPICalled", j => OnConsole(log, j, false));
                Hook(core, "Runtime.exceptionThrown", j => OnConsole(log, j, true));
                Hook(core, "Network.requestWillBeSent", j => OnNet(log, "start", j));
                Hook(core, "Network.responseReceived", j => OnNet(log, "response", j));
                Hook(core, "Network.loadingFinished", j => OnNet(log, "done", j));
                Hook(core, "Network.loadingFailed", j => OnNet(log, "failed", j));
                EnableLogs(core);
            }
            catch (Exception ex) { App.Log("Browser automation logs: " + ex.Message); }
        }

        static void Hook(CoreWebView2 core, string ev, Action<Dictionary<string, object>> on)
        {
            core.GetDevToolsProtocolEventReceiver(ev).DevToolsProtocolEventReceived += (s, e) =>
            {
                try { var d = Ser.DeserializeObject(e.ParameterObjectAsJson) as Dictionary<string, object>; if (d != null) on(d); } catch { }
            };
        }

        static async void EnableLogs(CoreWebView2 core)
        {
            try { await core.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}"); await core.CallDevToolsProtocolMethodAsync("Network.enable", "{}"); }
            catch (Exception ex) { App.Log("Browser automation enable: " + ex.Message); }
        }

        static object Get(object o, params string[] path)
        {
            foreach (var p in path) { var d = o as Dictionary<string, object>; if (d == null || !d.TryGetValue(p, out o)) return null; }
            return o;
        }
        static string GetS(object o, params string[] path) { var v = Get(o, path); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }

        static void Trim(List<Dictionary<string, object>> l, int max) { if (l.Count > max) l.RemoveRange(0, l.Count - max); }

        void OnConsole(TabLog log, Dictionary<string, object> j, bool exception)
        {
            string level, text;
            if (exception)
            {
                level = "error";
                text = GetS(j, "exceptionDetails", "exception", "description");
                if (text.Length == 0) text = GetS(j, "exceptionDetails", "text");
            }
            else
            {
                level = GetS(j, "type");
                var parts = new List<string>();
                var args = Get(j, "args") as object[] ?? (Get(j, "args") is System.Collections.ArrayList ? ((System.Collections.ArrayList)Get(j, "args")).ToArray() : new object[0]);
                foreach (var arg in args.Take(8)) { string v = GetS(arg, "value"); if (v.Length == 0) v = GetS(arg, "description"); if (v.Length == 0) v = GetS(arg, "type"); parts.Add(v); }
                text = string.Join(" ", parts);
            }
            log.Console.Add(new Dictionary<string, object> { { "time", DateTime.Now.ToString("HH:mm:ss") }, { "level", level }, { "text", Cut(text, 600) } });
            Trim(log.Console, 300);
        }

        void OnNet(TabLog log, string kind, Dictionary<string, object> j)
        {
            string id = GetS(j, "requestId");
            Dictionary<string, object> r;
            if (kind == "start")
            {
                string url = GetS(j, "request", "url");
                if (url.StartsWith("data:")) return;
                if (log.ById.TryGetValue(id, out r)) { r["url"] = Cut(url, 300); return; }   // redirect keeps the same id
                r = new Dictionary<string, object> { { "time", DateTime.Now.ToString("HH:mm:ss") }, { "method", GetS(j, "request", "method") }, { "url", Cut(url, 300) }, { "type", GetS(j, "type") }, { "status", 0 } };
                log.ById[id] = r; log.Net.Add(r); log.Inflight++; log.LastNet = DateTime.Now;
                if (log.Net.Count > 400) { var gone = log.Net[0]; log.Net.RemoveAt(0); foreach (var k in log.ById.Where(p => p.Value == gone).Select(p => p.Key).ToList()) log.ById.Remove(k); }
                return;
            }
            if (!log.ById.TryGetValue(id, out r)) return;
            if (kind == "response") { int st; int.TryParse(GetS(j, "response", "status"), out st); r["status"] = st; r["mime"] = GetS(j, "response", "mimeType"); return; }
            if (r.ContainsKey("done")) return;
            r["done"] = true; log.Inflight = Math.Max(0, log.Inflight - 1); log.LastNet = DateTime.Now;
            if (kind == "failed") r["error"] = GetS(j, "errorText");
            else { long b; long.TryParse(GetS(j, "encodedDataLength"), out b); r["bytes"] = b; }
        }

        void OnDialog(CoreWebView2ScriptDialogOpeningEventArgs a)
        {
            var rec = new Dictionary<string, object> { { "time", DateTime.Now.ToString("HH:mm:ss") }, { "kind", a.Kind.ToString().ToLowerInvariant() }, { "message", Cut(a.Message, 500) }, { "url", a.Uri } };
            dialogLog.Add(rec); Trim(dialogLog, 50);
            if (DialogMode == "accept") { if (a.Kind == CoreWebView2ScriptDialogKind.Prompt) a.ResultText = DialogPromptText ?? a.DefaultText; a.Accept(); rec["result"] = "accepted by agent setting"; return; }
            if (DialogMode == "dismiss") { rec["result"] = "dismissed by agent setting"; return; }
            // Like a normal browser: the person answers.
            var deferral = a.GetDeferral();
            pendingDialogs++; rec["result"] = "waiting for the user";
            BeginInvoke((Action)(() =>
            {
                try
                {
                    var owner = FindForm(); string site = ""; try { site = new Uri(a.Uri).Host; } catch { }
                    switch (a.Kind)
                    {
                        case CoreWebView2ScriptDialogKind.Alert: MessageBox.Show(owner, a.Message, site + " says", MessageBoxButtons.OK); a.Accept(); rec["result"] = "ok"; break;
                        case CoreWebView2ScriptDialogKind.Confirm:
                            if (MessageBox.Show(owner, a.Message, site + " asks", MessageBoxButtons.OKCancel) == DialogResult.OK) { a.Accept(); rec["result"] = "ok"; } else rec["result"] = "cancel"; break;
                        case CoreWebView2ScriptDialogKind.Prompt:
                            string v = Ui.Ask(site + " asks", a.Message, a.DefaultText, owner);
                            if (v != null) { a.ResultText = v; a.Accept(); rec["result"] = "ok"; } else rec["result"] = "cancel"; break;
                        default:
                            if (MessageBox.Show(owner, "Leave this page? Changes you made may not be saved.", site, MessageBoxButtons.YesNo) == DialogResult.Yes) { a.Accept(); rec["result"] = "leave"; } else rec["result"] = "stay"; break;
                    }
                }
                catch (Exception ex) { App.Log("Browser dialog: " + ex.Message); }
                finally { pendingDialogs--; try { deferral.Complete(); } catch { } }
            }));
        }

        void OnDownload(CoreWebView2DownloadStartingEventArgs a)
        {
            var op = a.DownloadOperation;
            var rec = new Dictionary<string, object> { { "time", DateTime.Now.ToString("HH:mm:ss") }, { "file", a.ResultFilePath }, { "url", Cut(op.Uri, 300) }, { "state", "downloading" }, { "bytes", 0L }, { "total", (long)(op.TotalBytesToReceive ?? 0) }, { "started", DateTime.Now.Ticks } };
            downloadLog.Add(rec); Trim(downloadLog, 60);
            op.BytesReceivedChanged += delegate { rec["bytes"] = op.BytesReceived; };
            op.StateChanged += delegate
            {
                rec["bytes"] = op.BytesReceived; rec["file"] = op.ResultFilePath;
                if (op.State == CoreWebView2DownloadState.Completed) rec["state"] = "completed";
                else if (op.State == CoreWebView2DownloadState.Interrupted) { rec["state"] = "failed"; rec["error"] = op.InterruptReason.ToString(); }
            };
        }

        // ---------- the visible "agent is using the browser" bar ----------
        void Act(string what)
        {
            lastAction = DateTime.Now;
            lock (actionLog) { actionLog.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + what); if (actionLog.Count > 300) actionLog.RemoveAt(0); }
            if (autoBar == null)
            {
                autoBar = new Panel { Dock = DockStyle.Top, Height = Ui.S(34), BackColor = Color.FromArgb(196, 43, 48), Padding = new Padding(Ui.S(10), Ui.S(4), Ui.S(4), Ui.S(4)) };
                autoText = new Label { Dock = DockStyle.Fill, ForeColor = Color.White, Font = Ui.Base, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false };
                autoStop = new Button { Dock = DockStyle.Right, Width = Ui.S(130), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.FromArgb(196, 43, 48), Font = Ui.Base, Text = "Stop agent" };
                autoStop.FlatAppearance.BorderSize = 0;
                autoStop.Click += delegate
                {
                    AgentsPaused = !AgentsPaused;
                    Act(AgentsPaused ? "You stopped browser automation. Agents cannot use the browser until you allow it." : "You allowed agents to use the browser again.");
                };
                autoBar.Controls.Add(autoText); autoBar.Controls.Add(autoStop);
                Controls.Add(autoBar); Controls.SetChildIndex(autoBar, 1);   // right above the page
                autoHide = new System.Windows.Forms.Timer { Interval = 1000 };
                autoHide.Tick += delegate { if (!AgentsPaused && (DateTime.Now - lastAction).TotalSeconds > 8) { autoBar.Visible = false; autoHide.Stop(); } };
            }
            autoText.Text = AgentsPaused ? "Browser automation is stopped. " + what : "Agent is using the browser:  " + what;
            autoStop.Text = AgentsPaused ? "Allow agents" : "Stop agent";
            autoBar.Visible = true; autoHide.Start();
        }

        public static string[] ActionLog() { lock (actionLog) return actionLog.ToArray(); }

        // ---------- entry point (UI thread) ----------
        public static BrowserPage ForAutomation()
        {
            var m = App.I.MainView;
            var p = m != null ? m.BrowserView : null;
            if (p == null || p.tabs.Count == 0) { App.I.ShowMain("browser"); m = App.I.MainView; p = m != null ? m.BrowserView : null; }
            else if (!p.Visible) m.ShowPage("browser");   // show the page, without pulling the window to the front
            if (p == null) throw new Exception("The Browser page could not be opened.");
            return p;
        }

        public async Task<object> Automate(string tool, Dictionary<string, object> a, CancellationToken stop)
        {
            if (AgentsPaused) throw new Exception("The user stopped browser automation (red bar on the Browser page). Ask them to click 'Allow agents'.");
            if (WebViewHost.Runtime() == null) throw new Exception(WebViewHost.Error);
            switch (tool)
            {
                case "browser_tabs": return await Tabs(a);
                case "browser_navigate": return await Navigate(a);
                case "browser_snapshot": return await Snapshot(a);
                case "browser_click": return await Click(a);
                case "browser_hover": return await Hover(a);
                case "browser_fill": return await Fill(a, stop);
                case "browser_select": return await SelectOption(a);
                case "browser_key": return await Key(a, stop);
                case "browser_scroll": return await Scroll(a);
                case "browser_wait": return await Wait(a);
                case "browser_screenshot": return await Screenshot(a);
                case "browser_pdf": return await Pdf(a);
                case "browser_upload": return await Upload(a);
                case "browser_downloads": return await Downloads(a);
                case "browser_dialogs": return Dialogs(a);
                case "browser_console": return await ConsoleLog(a);
                case "browser_network": return await Network(a);
                case "browser_cookies": return await Cookies(a);
                case "browser_emulate": return await Emulate(a);
                case "browser_extract": return await Extract(a);
            }
            throw new Exception("Unknown browser tool " + tool);
        }

        // ---------- argument helpers ----------
        static string Str(Dictionary<string, object> a, string k) { return McpServer.Str(a, k); }
        static bool Has(Dictionary<string, object> a, string k) { return a != null && a.ContainsKey(k) && a[k] != null && Str(a, k).Length > 0; }
        static int Int(Dictionary<string, object> a, string k, int def) { double v; return Has(a, k) && double.TryParse(Str(a, k), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (int)v : def; }
        static double Num(Dictionary<string, object> a, string k, double def) { double v; return Has(a, k) && double.TryParse(Str(a, k), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
        static bool Flag(Dictionary<string, object> a, string k, bool def) { if (!Has(a, k)) return def; string s = Str(a, k).ToLowerInvariant(); return s == "true" || s == "1" || s == "yes"; }
        static string Cut(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) + "..." : s; }
        static int TimeoutOf(Dictionary<string, object> a, int def) { return Math.Max(500, Math.Min(110000, Int(a, "timeout_ms", def))); }
        static List<string> Strings(Dictionary<string, object> a, string k)
        {
            object v; var l = new List<string>();
            if (a == null || !a.TryGetValue(k, out v) || v == null) return l;
            var e = v as System.Collections.IEnumerable;
            if (v is string) l.AddRange(((string)v).Split('|').Select(x => x.Trim()).Where(x => x.Length > 0));
            else if (e != null) foreach (var x in e) if (x != null) l.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            return l;
        }

        // Which element: ref (from browser_snapshot), CSS selector, or visible text / label.
        static Dictionary<string, object> Spec(Dictionary<string, object> a)
        {
            var s = new Dictionary<string, object>();
            if (Has(a, "ref")) s["ref"] = Str(a, "ref").Trim();
            else if (Has(a, "selector")) s["selector"] = Str(a, "selector");
            else if (Has(a, "text")) s["text"] = Str(a, "text");
            else if (Has(a, "label")) s["text"] = Str(a, "label");
            return s;
        }
        static string Who(Dictionary<string, object> spec) { object v; return spec.TryGetValue("ref", out v) || spec.TryGetValue("selector", out v) || spec.TryGetValue("text", out v) ? Convert.ToString(v) : "?"; }

        async Task<Tab> Ready(Dictionary<string, object> a)
        {
            int index = Int(a, "tab", -1);
            var deadline = DateTime.Now.AddSeconds(20);
            while (true)
            {
                if (index >= tabs.Count) throw new Exception("There is no tab " + index + " (tabs: " + tabs.Count + ", 0-based).");
                Tab t = index >= 0 ? tabs[index] : active;
                if (t != null && t.View.CoreWebView2 != null) return t;
                if (DateTime.Now > deadline) throw new Exception("The browser tab did not start (Edge WebView2).");
                await Task.Delay(150);
            }
        }

        static Task<string> Cdp(Tab t, string method, object args) { return t.View.CoreWebView2.CallDevToolsProtocolMethodAsync(method, Ser.Serialize(args ?? new Dictionary<string, object>())); }

        async Task<object> Eval(Tab t, string body)
        {
            string js = AutoLib + ";(function(){var A=window.__aio;try{return JSON.stringify({ok:true,v:(" + body + ")});}catch(e){return JSON.stringify({ok:false,e:String(e&&e.message||e)});}})()";
            string raw = await t.View.CoreWebView2.ExecuteScriptAsync(js);
            string inner = null;
            try { inner = Ser.Deserialize<string>(raw); } catch { }
            if (inner == null) throw new Exception("The page did not answer (it may be loading or showing a dialog). Try browser_wait, then again.");
            var d = Ser.DeserializeObject(inner) as Dictionary<string, object>;
            if (d == null) return null;
            if (!(d["ok"] is bool && (bool)d["ok"])) throw new Exception(Convert.ToString(Get(d, "e")));
            return Get(d, "v");
        }
        static string JsArg(object o) { return Ser.Serialize(o); }

        object PageInfo(Tab t)
        {
            return new Dictionary<string, object> { { "tab", tabs.IndexOf(t) }, { "url", t.View.CoreWebView2 != null ? t.View.CoreWebView2.Source : t.Url }, { "title", t.View.CoreWebView2 != null ? t.View.CoreWebView2.DocumentTitle : t.Name } };
        }

        async Task WaitLoad(Tab t, int timeoutMs)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (t.Log.Loading && DateTime.Now < end && !AgentsPaused) await Task.Delay(100);
        }

        // After a click or key: if it started a navigation or opened a tab, wait for it (short) and report.
        async Task<Dictionary<string, object>> Settle(Tab t, int rev, int tabCount, string urlBefore, int timeoutMs)
        {
            await Task.Delay(250);
            var r = new Dictionary<string, object>();
            if (tabs.Count > tabCount && active != t)
            {
                r["opened_tab"] = tabs.IndexOf(active);
                await WaitLoad(active, Math.Min(timeoutMs, 15000));
                t = active;
            }
            else if (t.NavigationRevision != rev || t.Log.Loading)
            {
                await WaitLoad(t, Math.Min(timeoutMs, 15000));
                r["navigated"] = true;
            }
            if (DialogOpen) r["dialog"] = "The page opened a dialog; it waits for the user (browser_dialogs sets accept/dismiss for the next ones).";
            var info = (Dictionary<string, object>)PageInfo(t);
            foreach (var k in info) r[k.Key] = k.Value;
            if (!t.Log.LastOk) r["load_error"] = t.Log.LastError;
            return r;
        }

        // ---------- tools ----------
        async Task<object> Tabs(Dictionary<string, object> a)
        {
            string act = Has(a, "action") ? Str(a, "action").ToLowerInvariant() : "list";
            int i = Int(a, "index", -1);
            if (act == "new") { Act("opening a new tab"); AddTab(Has(a, "url") ? Url(Str(a, "url")) : Home, true); var t = await Ready(new Dictionary<string, object>()); await Task.Delay(200); await WaitLoad(t, TimeoutOf(a, 20000)); }
            else if (act == "switch" || act == "activate") { if (i < 0 || i >= tabs.Count) throw new Exception("Give index 0.." + (tabs.Count - 1)); Act("switching to tab " + i); Activate(tabs[i]); }
            else if (act == "close") { var t = i >= 0 && i < tabs.Count ? tabs[i] : active; Act("closing tab " + tabs.IndexOf(t)); CloseTab(t); }
            else if (act != "list") throw new Exception("action: list, new, switch or close");
            return new Dictionary<string, object>
            {
                { "active", tabs.IndexOf(active) },
                { "tabs", tabs.Select((x, n) => new Dictionary<string, object> { { "index", n }, { "title", x.Name ?? "" }, { "url", x.Url ?? "" }, { "loading", x.Log.Loading } }).ToList() }
            };
        }

        async Task<object> Navigate(Dictionary<string, object> a)
        {
            string act = Has(a, "action") ? Str(a, "action").ToLowerInvariant() : "go";
            Tab t;
            if (act == "go" && Flag(a, "new_tab", false)) { AddTab(Url(Str(a, "url")), true); t = await Ready(new Dictionary<string, object>()); }
            else t = await Ready(a);
            var core = t.View.CoreWebView2;
            int rev = t.NavigationRevision;
            switch (act)
            {
                case "go":
                    if (!Has(a, "url")) throw new Exception("Give url.");
                    Act("opening " + Cut(Str(a, "url"), 80));
                    if (!Flag(a, "new_tab", false)) { t.Url = Url(Str(a, "url")); core.Navigate(t.Url); }
                    break;
                case "back": Act("going back"); if (!core.CanGoBack) throw new Exception("No page to go back to."); core.GoBack(); break;
                case "forward": Act("going forward"); if (!core.CanGoForward) throw new Exception("No page to go forward to."); core.GoForward(); break;
                case "reload": Act("reloading"); core.Reload(); break;
                case "stop": Act("stopping the page load"); core.Stop(); return PageInfo(t);
                default: throw new Exception("action: go, back, forward, reload or stop");
            }
            if (Has(a, "wait") && Str(a, "wait") == "none") return PageInfo(t);
            var end = DateTime.Now.AddMilliseconds(1500);
            while (t.NavigationRevision == rev && !t.Log.Loading && DateTime.Now < end) await Task.Delay(50);
            await WaitLoad(t, TimeoutOf(a, 30000));
            var r = (Dictionary<string, object>)PageInfo(t);
            r["ok"] = t.Log.LastOk && !t.Log.Loading;
            if (t.Log.Loading) r["note"] = "Still loading after the timeout.";
            if (!t.Log.LastOk) r["error"] = t.Log.LastError;
            if (t.Log.LastStatus > 0) r["http_status"] = t.Log.LastStatus;
            return r;
        }

        async Task<object> Snapshot(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            return await Eval(t, "A.snapshot(" + Int(a, "max_items", 250) + "," + Int(a, "text_chars", 0) + "," + JsArg(Has(a, "selector") ? Str(a, "selector") : "") + ")");
        }

        // Returns the element's centre in viewport CSS pixels (scrolled into view) plus its name.
        async Task<Dictionary<string, object>> Point(Tab t, Dictionary<string, object> spec)
        {
            var p = await Eval(t, "A.point(" + JsArg(spec) + ")") as Dictionary<string, object>;
            if (p == null) throw new Exception("Element not found.");
            await Eval(t, "A.mark(" + JsArg(p) + ")");
            return p;
        }
        static double D(Dictionary<string, object> d, string k) { return Convert.ToDouble(d[k], CultureInfo.InvariantCulture); }

        async Task Mouse(Tab t, string type, double x, double y, string button, int clicks, int buttons)
        {
            double z = t.View.ZoomFactor;   // CDP takes device-independent pixels; the page measures zoomed CSS pixels
            var args = new Dictionary<string, object> { { "type", type }, { "x", x * z }, { "y", y * z }, { "button", button }, { "buttons", buttons }, { "clickCount", clicks } };
            await Cdp(t, "Input.dispatchMouseEvent", args);
        }

        async Task<object> Click(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            string button = Has(a, "button") ? Str(a, "button").ToLowerInvariant() : "left";
            if (button != "left" && button != "right" && button != "middle") throw new Exception("button: left, right or middle");
            int clicks = Flag(a, "double", false) ? 2 : 1;
            double x, y; string name;
            var spec = Spec(a);
            Dictionary<string, object> p = null;
            if (spec.Count == 0)
            {
                if (!Has(a, "x") || !Has(a, "y")) throw new Exception("Give ref (from browser_snapshot), selector, text, or x and y.");
                x = Num(a, "x", 0); y = Num(a, "y", 0); name = "point " + x + "," + y;
                Act("clicking at " + x + ", " + y);
            }
            else
            {
                p = await Point(t, spec);
                if (p.ContainsKey("disabled") && !Flag(a, "force", false)) throw new Exception("'" + p["name"] + "' is disabled (pass force=true to click anyway).");
                x = D(p, "x"); y = D(p, "y"); name = Convert.ToString(p["name"]);
                Act((clicks == 2 ? "double-clicking " : button == "right" ? "right-clicking " : "clicking ") + "\"" + Cut(name, 60) + "\"");
            }
            int rev = t.NavigationRevision, count = tabs.Count; string before = t.View.CoreWebView2.Source;
            int mask = button == "left" ? 1 : button == "right" ? 2 : 4;
            await Mouse(t, "mouseMoved", x, y, "none", 0, 0);
            for (int i = 1; i <= clicks; i++)
            {
                await Mouse(t, "mousePressed", x, y, button, i, mask);
                await Mouse(t, "mouseReleased", x, y, button, i, 0);
            }
            var r = await Settle(t, rev, count, before, TimeoutOf(a, 15000));
            r["clicked"] = name;
            if (p != null && p.ContainsKey("covered")) r["warning"] = "Another element was on top of it: " + p["covered"] + ". If nothing happened, close that first.";
            return r;
        }

        async Task<object> Hover(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            var spec = Spec(a);
            if (spec.Count == 0) throw new Exception("Give ref, selector or text.");
            var p = await Point(t, spec);
            Act("pointing at \"" + Cut(Convert.ToString(p["name"]), 60) + "\"");
            await Mouse(t, "mouseMoved", D(p, "x"), D(p, "y"), "none", 0, 0);
            await Task.Delay(300);
            return new Dictionary<string, object> { { "hovered", p["name"] } };
        }

        async Task<object> Fill(Dictionary<string, object> a, CancellationToken stop)
        {
            var t = await Ready(a);
            var done = new List<object>();
            object fo; var many = a != null && a.TryGetValue("fields", out fo) ? fo as Dictionary<string, object> : null;
            if (many != null && many.Count > 0)
            {
                foreach (var f in many)
                {
                    var spec = new Dictionary<string, object>();
                    if (System.Text.RegularExpressions.Regex.IsMatch(f.Key, @"^e\d+$")) spec["ref"] = f.Key;
                    else if (System.Text.RegularExpressions.Regex.IsMatch(f.Key, @"^[#.\[]|^\w+\[|^(input|textarea|select)\b")) spec["selector"] = f.Key;
                    else spec["text"] = f.Key;
                    done.Add(await FillOne(t, spec, Convert.ToString(f.Value, CultureInfo.InvariantCulture), true, stop));
                }
            }
            else
            {
                var spec = Spec(a);
                if (spec.Count == 0) throw new Exception("Give ref (from browser_snapshot), selector or label, and value. Or fields: {ref/selector/label: value}.");
                done.Add(await FillOne(t, spec, Str(a, "value"), Flag(a, "clear", true), stop));
            }
            var r = new Dictionary<string, object> { { "filled", done } };
            if (Flag(a, "submit", false))
            {
                int rev = t.NavigationRevision, count = tabs.Count; string before = t.View.CoreWebView2.Source;
                Act("pressing Enter");
                await PressOne(t, "Enter", stop);
                foreach (var k in await Settle(t, rev, count, before, TimeoutOf(a, 15000))) r[k.Key] = k.Value;
            }
            return r;
        }

        async Task<object> FillOne(Tab t, Dictionary<string, object> spec, string value, bool clear, CancellationToken stop)
        {
            var info = await Eval(t, "A.focusFor(" + JsArg(spec) + "," + (clear ? "true" : "false") + ")") as Dictionary<string, object>;
            if (info == null) throw new Exception("Element not found.");
            if (Convert.ToString(info["tag"]) == "select") return await SelectIn(t, spec, new List<string> { value });
            string type = Convert.ToString(info["type"]).ToLowerInvariant();
            if (type == "checkbox" || type == "radio" || type == "file") throw new Exception("'" + info["name"] + "' is a " + type + ": use " + (type == "file" ? "browser_upload" : "browser_click") + ".");
            if (!(info["editable"] is bool && (bool)info["editable"])) throw new Exception("'" + info["name"] + "' cannot take text (" + info["tag"] + ").");
            Act("typing into \"" + Cut(Convert.ToString(info["name"]), 50) + "\"");
            await Eval(t, "A.mark(A.point(" + JsArg(spec) + "))");
            if (value.Length == 0) { if (clear) await PressOne(t, "Delete", stop); }
            else await KeyboardStep(() => Cdp(t, "Input.insertText", new Dictionary<string, object> { { "text", value } }), "typing text", stop);
            var after = await Eval(t, "A.after()") as Dictionary<string, object>;
            return new Dictionary<string, object> { { "field", info["name"] }, { "value", after != null ? after["value"] : "" } };
        }

        async Task<object> SelectOption(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            var spec = Spec(a);
            if (spec.Count == 0) throw new Exception("Give ref, selector or label of the <select>.");
            var values = Strings(a, "values"); if (Has(a, "value")) values.Add(Str(a, "value"));
            if (values.Count == 0) throw new Exception("Give value (option text or value; several with |).");
            return await SelectIn(t, spec, values);
        }
        async Task<object> SelectIn(Tab t, Dictionary<string, object> spec, List<string> values)
        {
            Act("choosing \"" + Cut(string.Join(", ", values), 60) + "\"");
            await Eval(t, "A.mark(A.point(" + JsArg(spec) + "))");
            return await Eval(t, "A.select(" + JsArg(spec) + "," + JsArg(values) + ")");
        }

        // ---------- keyboard ----------
        static readonly Dictionary<string, int> Named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Enter", 13 }, { "Tab", 9 }, { "Escape", 27 }, { "Esc", 27 }, { "Backspace", 8 }, { "Delete", 46 }, { "Del", 46 }, { "Insert", 45 },
            { "Space", 32 }, { "ArrowUp", 38 }, { "ArrowDown", 40 }, { "ArrowLeft", 37 }, { "ArrowRight", 39 }, { "Up", 38 }, { "Down", 40 }, { "Left", 37 }, { "Right", 39 },
            { "Home", 36 }, { "End", 35 }, { "PageUp", 33 }, { "PageDown", 34 },
            { "F1", 112 }, { "F2", 113 }, { "F3", 114 }, { "F4", 115 }, { "F5", 116 }, { "F6", 117 }, { "F7", 118 }, { "F8", 119 }, { "F9", 120 }, { "F10", 121 }, { "F11", 122 }, { "F12", 123 }
        };
        static readonly Dictionary<string, string> Canon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { { "Esc", "Escape" }, { "Del", "Delete" }, { "Up", "ArrowUp" }, { "Down", "ArrowDown" }, { "Left", "ArrowLeft" }, { "Right", "ArrowRight" } };

        static async Task<T> KeyboardStep<T>(Func<Task<T>> start, string action, CancellationToken stop)
        {
            stop.ThrowIfCancellationRequested();
            if (AgentsPaused) throw new InvalidOperationException("Browser automation is stopped.");
            var pending = start();
            using (var timer = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                var deadline = Task.Delay(5000, timer.Token);
                if (await Task.WhenAny(pending, deadline) != pending)
                {
                    // WebView2 cannot cancel a protocol call. Observe a late failure and stop the sequence.
                    GC.KeepAlive(pending.ContinueWith(task => GC.KeepAlive(task.Exception), CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));
                    throw new TimeoutException("Edge did not finish " + action + ". Check the page before sending more keys.");
                }
                timer.Cancel();
                return await pending;
            }
        }

        async Task PressOne(Tab t, string combo, CancellationToken stop)
        {
            var parts = combo.Split('+').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (combo.EndsWith("++")) parts.Add("+");
            if (parts.Count == 0) return;
            int mods = 0;
            foreach (var m in parts.Take(parts.Count - 1))
            {
                string k = m.ToLowerInvariant();
                if (k == "alt") mods |= 1; else if (k == "ctrl" || k == "control") mods |= 2; else if (k == "meta" || k == "win" || k == "cmd") mods |= 4; else if (k == "shift") mods |= 8;
                else throw new Exception("Unknown modifier '" + m + "' (Alt, Control, Shift, Meta).");
            }
            string key = parts[parts.Count - 1], code, text = null; int vk;
            if (Named.TryGetValue(key, out vk))
            {
                string c; if (Canon.TryGetValue(key, out c)) key = c;
                if (key.Equals("Space", StringComparison.OrdinalIgnoreCase)) { key = " "; code = "Space"; text = " "; }
                else { code = key; if (key == "Enter") text = "\r"; }
            }
            else if (key.Length == 1)
            {
                char ch = key[0];
                if (char.IsLetter(ch)) { code = "Key" + char.ToUpperInvariant(ch); vk = char.ToUpperInvariant(ch); if ((mods & 8) != 0) key = char.ToUpperInvariant(ch).ToString(); }
                else if (char.IsDigit(ch)) { code = "Digit" + ch; vk = ch; }
                else { code = ""; vk = 0; }
                text = key;
            }
            else throw new Exception("Unknown key '" + key + "'. Use names like Enter, Tab, Escape, ArrowDown, PageDown, F5, or one character.");
            if ((mods & 7) != 0) text = null;   // shortcuts type nothing
            var down = new Dictionary<string, object> { { "type", text != null ? "keyDown" : "rawKeyDown" }, { "modifiers", mods }, { "key", key }, { "code", code }, { "windowsVirtualKeyCode", vk }, { "nativeVirtualKeyCode", vk } };
            if (text != null) { down["text"] = text; down["unmodifiedText"] = text; }
            await KeyboardStep(() => Cdp(t, "Input.dispatchKeyEvent", down), "pressing " + combo, stop);
            var up = new Dictionary<string, object> { { "type", "keyUp" }, { "modifiers", mods }, { "key", key }, { "code", code }, { "windowsVirtualKeyCode", vk }, { "nativeVirtualKeyCode", vk } };
            await KeyboardStep(() => Cdp(t, "Input.dispatchKeyEvent", up), "releasing " + combo, stop);
        }

        async Task<object> Key(Dictionary<string, object> a, CancellationToken stop)
        {
            var t = await Ready(a);
            var spec = Spec(a); spec.Remove("text");
            if (Has(a, "ref") || Has(a, "selector"))
                await KeyboardStep(() => Eval(t, "A.focusFor(" + JsArg(spec) + ",false)"), "focusing the keyboard target", stop);
            int rev = t.NavigationRevision, count = tabs.Count; string before = t.View.CoreWebView2.Source;
            if (Has(a, "type_text"))
            {
                Act("typing text");
                await KeyboardStep(() => Cdp(t, "Input.insertText", new Dictionary<string, object> { { "text", Str(a, "type_text") } }), "typing text", stop);
            }
            if (Has(a, "keys"))
            {
                var seq = System.Text.RegularExpressions.Regex.Split(Str(a, "keys").Trim(), @"\s+").Where(x => x.Length > 0).ToList();
                int repeat = Math.Max(1, Math.Min(100, Int(a, "repeat", 1)));
                Act("pressing " + string.Join(" ", seq) + (repeat > 1 ? " x" + repeat : ""));
                for (int r = 0; r < repeat; r++) foreach (var k in seq) { await PressOne(t, k, stop); await Task.Delay(30, stop); }
            }
            if (!Has(a, "keys") && !Has(a, "type_text")) throw new Exception("Give keys (e.g. 'Enter', 'Control+A', 'Tab Tab Enter') or type_text.");
            var res = await Settle(t, rev, count, before, TimeoutOf(a, 15000));
            res["pressed"] = Str(a, "keys");
            return res;
        }

        async Task<object> Scroll(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            var spec = Spec(a);
            if (spec.Count > 0) { var p = await Point(t, spec); Act("scrolling to \"" + Cut(Convert.ToString(p["name"]), 60) + "\""); }
            else if (Has(a, "to"))
            {
                string to = Str(a, "to").ToLowerInvariant();
                Act("scrolling to the " + to);
                await Eval(t, to == "top" ? "(scrollTo(0,0),1)" : "(scrollTo(0,document.documentElement.scrollHeight),1)");
            }
            else
            {
                var vp = await Eval(t, "({w:innerWidth,h:innerHeight})") as Dictionary<string, object>;
                double w = D(vp, "w"), h = D(vp, "h");
                double dy = Num(a, "dy", h * 0.8), dx = Num(a, "dx", 0);
                Act("scrolling " + (dy >= 0 ? "down" : "up"));
                double z = t.View.ZoomFactor;
                await Cdp(t, "Input.dispatchMouseEvent", new Dictionary<string, object> { { "type", "mouseWheel" }, { "x", Num(a, "x", w / 2) * z }, { "y", Num(a, "y", h / 2) * z }, { "deltaX", dx }, { "deltaY", dy } });
                await Task.Delay(250);
            }
            return await Eval(t, "({scroll_y:Math.round(scrollY),page_height:document.documentElement.scrollHeight,view_height:innerHeight,at_bottom:Math.ceil(scrollY+innerHeight)>=document.documentElement.scrollHeight-2})");
        }

        async Task<object> Wait(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            if (Has(a, "ms")) { int ms = Math.Max(0, Math.Min(60000, Int(a, "ms", 1000))); Act("waiting " + ms + " ms"); await Task.Delay(ms); return new Dictionary<string, object> { { "waited_ms", ms } }; }
            int timeout = TimeoutOf(a, 10000);
            var cond = new Dictionary<string, object>();
            string what;
            if (Has(a, "selector")) { cond["selector"] = Str(a, "selector"); what = "element " + Str(a, "selector"); }
            else if (Has(a, "text")) { cond["text"] = Str(a, "text"); what = "text \"" + Cut(Str(a, "text"), 40) + "\""; }
            else if (Has(a, "url_contains")) { cond["url"] = Str(a, "url_contains"); what = "address containing " + Str(a, "url_contains"); }
            else if (Has(a, "state") && Str(a, "state") == "network_idle") what = "network idle";
            else what = "page load";
            bool gone = Flag(a, "gone", false); if (gone) { cond["gone"] = true; what += " to disappear"; }
            Act("waiting for " + what);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                if (AgentsPaused) throw new Exception("The user stopped browser automation.");
                bool ok = false;
                try
                {
                    if (what == "network idle") ok = !t.Log.Loading && t.Log.Inflight == 0 && (DateTime.Now - t.Log.LastNet).TotalMilliseconds > 500;
                    else if (cond.Count == 0) ok = !t.Log.Loading && Convert.ToString(await Eval(t, "document.readyState")) == "complete";
                    else { var v = t.Log.Loading ? null : await Eval(t, "A.check(" + JsArg(cond) + ")"); ok = v is bool && (bool)v; }
                }
                catch { ok = false; }   // page in between documents
                if (ok)
                {
                    var r = (Dictionary<string, object>)PageInfo(t);
                    r["waited_ms"] = sw.ElapsedMilliseconds; r["found"] = what;
                    return r;
                }
                if (sw.ElapsedMilliseconds > timeout) throw new Exception("Timed out after " + timeout + " ms waiting for " + what + (DialogOpen ? " (the page shows a dialog to the user)" : "") + ".");
                await Task.Delay(200);
            }
        }

        static string OutFile(Dictionary<string, object> a, string ext)
        {
            string p = Has(a, "path") ? Str(a, "path") : Path.Combine(Store.Dir, "browser", "shots", "page-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ext);
            p = Path.GetFullPath(Environment.ExpandEnvironmentVariables(p));
            if (!p.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) p += ext;
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            return p;
        }

        async Task<object> Screenshot(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            string file = OutFile(a, ".png");
            var args = new Dictionary<string, object> { { "format", "png" } };
            var spec = Spec(a);
            if (spec.Count > 0)
            {
                var r = await Eval(t, "A.box(" + JsArg(spec) + ")") as Dictionary<string, object>;
                args["clip"] = new Dictionary<string, object> { { "x", D(r, "x") }, { "y", D(r, "y") }, { "width", Math.Max(1, D(r, "w")) }, { "height", Math.Max(1, D(r, "h")) }, { "scale", 1 } };
                args["captureBeyondViewport"] = true;
                Act("taking a picture of \"" + Cut(Who(spec), 40) + "\"");
            }
            else if (Flag(a, "full_page", false))
            {
                var m = Ser.DeserializeObject(await Cdp(t, "Page.getLayoutMetrics", null));
                double w = Convert.ToDouble(Get(m, "cssContentSize", "width") ?? Get(m, "contentSize", "width"), CultureInfo.InvariantCulture);
                double h = Convert.ToDouble(Get(m, "cssContentSize", "height") ?? Get(m, "contentSize", "height"), CultureInfo.InvariantCulture);
                args["clip"] = new Dictionary<string, object> { { "x", 0 }, { "y", 0 }, { "width", w }, { "height", Math.Min(h, 16000) }, { "scale", 1 } };
                args["captureBeyondViewport"] = true;
                Act("taking a full-page picture");
            }
            else Act("taking a picture of the page");
            var res = Ser.DeserializeObject(await Cdp(t, "Page.captureScreenshot", args));
            byte[] png = Convert.FromBase64String(GetS(res, "data"));
            File.WriteAllBytes(file, png);
            int width = 0, height = 0;
            try { using (var ms = new MemoryStream(png)) using (var img = Image.FromStream(ms)) { width = img.Width; height = img.Height; } } catch { }
            var o = (Dictionary<string, object>)PageInfo(t);
            o["path"] = file; o["width"] = width; o["height"] = height; o["bytes"] = png.Length;
            return o;
        }

        async Task<object> Pdf(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            string file = OutFile(a, ".pdf");
            Act("saving the page as PDF");
            bool ok = await t.View.CoreWebView2.PrintToPdfAsync(file, null);
            if (!ok) throw new Exception("Saving the PDF failed.");
            var o = (Dictionary<string, object>)PageInfo(t); o["path"] = file; o["bytes"] = new FileInfo(file).Length;
            return o;
        }

        async Task<object> Upload(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            var spec = Spec(a);
            if (spec.Count == 0) throw new Exception("Give ref or selector of the file input (input[type=file]).");
            var files = Strings(a, "paths"); if (Has(a, "path")) files.Add(Str(a, "path"));
            if (files.Count == 0) throw new Exception("Give paths (full file paths).");
            for (int i = 0; i < files.Count; i++) { files[i] = Path.GetFullPath(Environment.ExpandEnvironmentVariables(files[i])); if (!File.Exists(files[i])) throw new Exception("File not found: " + files[i]); }
            var info = await Eval(t, "A.fileInput(" + JsArg(spec) + ")") as Dictionary<string, object>;
            Act("attaching " + string.Join(", ", files.Select(Path.GetFileName)));
            var obj = Ser.DeserializeObject(await Cdp(t, "Runtime.evaluate", new Dictionary<string, object> { { "expression", "window.__aioEl" } }));
            string id = GetS(obj, "result", "objectId");
            if (id.Length == 0) throw new Exception("Could not reach the file input.");
            await Cdp(t, "DOM.setFileInputFiles", new Dictionary<string, object> { { "files", files }, { "objectId", id } });
            var after = await Eval(t, "(function(){var e=window.__aioEl;e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));return [].map.call(e.files||[],function(f){return f.name;});})()");
            return new Dictionary<string, object> { { "input", info != null ? info["name"] : "" }, { "attached", after } };
        }

        async Task<object> Downloads(Dictionary<string, object> a)
        {
            string act = Has(a, "action") ? Str(a, "action").ToLowerInvariant() : "list";
            if (act == "wait")
            {
                int timeout = TimeoutOf(a, 60000);
                long since = DateTime.Now.AddMilliseconds(-Math.Max(0, Int(a, "since_ms", 15000))).Ticks;
                Act("waiting for a download");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    var d = downloadLog.LastOrDefault(x => (long)x["started"] >= since);
                    if (d != null && (string)d["state"] != "downloading") return Clean(d);
                    if (AgentsPaused) throw new Exception("The user stopped browser automation.");
                    if (sw.ElapsedMilliseconds > timeout) throw new Exception(d == null ? "No download started in " + timeout + " ms." : "The download is still running after " + timeout + " ms: " + d["file"]);
                    await Task.Delay(250);
                }
            }
            var all = downloadLog.Select(Clean).ToList(); all.Reverse();
            var earlier = Store.Settings.BrowserDownloads.Take(20).Select(f => new Dictionary<string, object> { { "file", f }, { "exists", File.Exists(f) } }).ToList();
            return new Dictionary<string, object> { { "this_session", all }, { "recent_files", earlier } };
        }
        static Dictionary<string, object> Clean(Dictionary<string, object> d) { var c = new Dictionary<string, object>(d); c.Remove("started"); if (c.ContainsKey("file")) c["exists"] = File.Exists(Convert.ToString(c["file"])); return c; }

        object Dialogs(Dictionary<string, object> a)
        {
            if (Has(a, "mode"))
            {
                string m = Str(a, "mode").ToLowerInvariant();
                if (m != "user" && m != "accept" && m != "dismiss") throw new Exception("mode: user (the person answers), accept or dismiss");
                DialogMode = m; DialogPromptText = Has(a, "prompt_text") ? Str(a, "prompt_text") : null;
                Act(m == "user" ? "page dialogs go to you again" : "page dialogs will be " + (m == "accept" ? "accepted" : "dismissed") + " automatically");
            }
            var l = dialogLog.ToList(); l.Reverse();
            return new Dictionary<string, object> { { "mode", DialogMode }, { "open_now", pendingDialogs }, { "recent", l.Take(Math.Max(1, Int(a, "limit", 20))).ToList() } };
        }

        async Task<object> ConsoleLog(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            string level = Has(a, "level") ? Str(a, "level").ToLowerInvariant() : "all";
            var l = t.Log.Console.Where(x => level == "all" || (level == "error" ? ((string)x["level"] == "error" || (string)x["level"] == "assert") : (string)x["level"] == level)).ToList();
            int limit = Math.Max(1, Math.Min(300, Int(a, "limit", 50)));
            var r = new Dictionary<string, object> { { "count", l.Count }, { "messages", l.Skip(Math.Max(0, l.Count - limit)).ToList() } };
            if (Flag(a, "clear", false)) t.Log.Console.Clear();
            return r;
        }

        async Task<object> Network(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            string filter = Has(a, "filter") ? Str(a, "filter") : "";
            bool failed = Flag(a, "failed_only", false);
            var l = t.Log.Net.Where(x => (filter.Length == 0 || ((string)x["url"]).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                && (!failed || x.ContainsKey("error") || Convert.ToInt32(x["status"]) >= 400)).ToList();
            int limit = Math.Max(1, Math.Min(400, Int(a, "limit", 60)));
            var r = new Dictionary<string, object> { { "in_flight", t.Log.Inflight }, { "count", l.Count }, { "requests", l.Skip(Math.Max(0, l.Count - limit)).ToList() } };
            if (Flag(a, "clear", false)) { t.Log.Net.Clear(); t.Log.ById.Clear(); t.Log.Inflight = 0; }
            return r;
        }

        async Task<object> Cookies(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            var cm = t.View.CoreWebView2.CookieManager;
            string act = Has(a, "action") ? Str(a, "action").ToLowerInvariant() : "list";
            string url = Has(a, "url") ? Str(a, "url") : t.View.CoreWebView2.Source;
            switch (act)
            {
                case "list":
                    var list = await cm.GetCookiesAsync(url);
                    return new Dictionary<string, object> { { "url", url }, { "cookies", list.Select(c => new Dictionary<string, object> { { "name", c.Name }, { "value", Cut(c.Value, 300) }, { "domain", c.Domain }, { "path", c.Path }, { "expires", c.IsSession ? "session" : c.Expires.ToString("s") }, { "http_only", c.IsHttpOnly }, { "secure", c.IsSecure } }).ToList() } };
                case "set":
                {
                    if (!Has(a, "name")) throw new Exception("Give name and value.");
                    string domain = Has(a, "domain") ? Str(a, "domain") : new Uri(url).Host;
                    var c = cm.CreateCookie(Str(a, "name"), Str(a, "value"), domain, Has(a, "path") ? Str(a, "path") : "/");
                    cm.AddOrUpdateCookie(c);
                    Act("setting cookie " + Str(a, "name"));
                    return new Dictionary<string, object> { { "set", Str(a, "name") }, { "domain", domain } };
                }
                case "delete":
                    if (!Has(a, "name")) throw new Exception("Give name.");
                    cm.DeleteCookies(Str(a, "name"), url); Act("deleting cookie " + Str(a, "name"));
                    return new Dictionary<string, object> { { "deleted", Str(a, "name") } };
                case "clear":
                    cm.DeleteAllCookies(); Act("clearing all cookies (you are signed out of sites)");
                    return new Dictionary<string, object> { { "cleared", true } };
            }
            throw new Exception("action: list, set, delete or clear");
        }

        async Task<object> Emulate(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            if (t.Log.OriginalAgent == null) t.Log.OriginalAgent = Convert.ToString(await Eval(t, "navigator.userAgent"));
            string dev = Has(a, "device") ? Str(a, "device").ToLowerInvariant() : "";
            var r = new Dictionary<string, object>();
            if (dev == "desktop" || dev == "reset")
            {
                await Cdp(t, "Emulation.clearDeviceMetricsOverride", null);
                await Cdp(t, "Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { { "enabled", false } });
                await Cdp(t, "Network.setUserAgentOverride", new Dictionary<string, object> { { "userAgent", t.Log.OriginalAgent } });
                await Cdp(t, "Network.emulateNetworkConditions", new Dictionary<string, object> { { "offline", false }, { "latency", 0 }, { "downloadThroughput", -1 }, { "uploadThroughput", -1 } });
                Act("back to the normal desktop view"); r["device"] = "desktop";
            }
            else if (dev.Length > 0 || Has(a, "width"))
            {
                int w = 0, h = 0; double scale = 1; bool mobile = false; string ua = null;
                if (dev == "phone" || dev == "iphone") { w = 390; h = 844; scale = 3; mobile = true; ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1"; }
                else if (dev == "android") { w = 412; h = 915; scale = 2.625; mobile = true; ua = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Mobile Safari/537.36"; }
                else if (dev == "tablet" || dev == "ipad") { w = 820; h = 1180; scale = 2; mobile = true; ua = "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1"; }
                else if (dev.Length > 0) throw new Exception("device: phone, android, tablet, desktop (reset), or give width and height.");
                w = Int(a, "width", w); h = Int(a, "height", h); scale = Num(a, "scale", scale); mobile = Flag(a, "mobile", mobile);
                await Cdp(t, "Emulation.setDeviceMetricsOverride", new Dictionary<string, object> { { "width", w }, { "height", h }, { "deviceScaleFactor", scale }, { "mobile", mobile } });
                await Cdp(t, "Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { { "enabled", mobile } });
                if (Has(a, "user_agent")) ua = Str(a, "user_agent");
                if (ua != null) await Cdp(t, "Network.setUserAgentOverride", new Dictionary<string, object> { { "userAgent", ua } });
                Act("showing the page as " + (dev.Length > 0 ? dev : w + "x" + h)); r["width"] = w; r["height"] = h; r["mobile"] = mobile;
            }
            else if (Has(a, "user_agent")) { await Cdp(t, "Network.setUserAgentOverride", new Dictionary<string, object> { { "userAgent", Str(a, "user_agent") } }); r["user_agent"] = Str(a, "user_agent"); }
            if (Has(a, "offline"))
            {
                bool off = Flag(a, "offline", false);
                await Cdp(t, "Network.emulateNetworkConditions", new Dictionary<string, object> { { "offline", off }, { "latency", 0 }, { "downloadThroughput", -1 }, { "uploadThroughput", -1 } });
                Act(off ? "simulating no internet" : "internet back on"); r["offline"] = off;
            }
            if (r.Count == 0) throw new Exception("Give device (phone, android, tablet, desktop), width+height, user_agent or offline.");
            r["note"] = "Applies to this tab until reset (device=desktop) or the tab closes.";
            return r;
        }

        async Task<object> Extract(Dictionary<string, object> a)
        {
            var t = await Ready(a);
            string what = Has(a, "what") ? Str(a, "what").ToLowerInvariant() : "text";
            if (what != "links" && what != "tables" && what != "forms" && what != "meta" && what != "text" && what != "images" && what != "headings") throw new Exception("what: text, links, tables, forms, images, headings or meta");
            return await Eval(t, "A.extract(" + JsArg(what) + "," + Int(a, "max", 200) + "," + JsArg(Has(a, "selector") ? Str(a, "selector") : "") + ")");
        }

        // ---------- page-side library (idempotent; same-origin iframes and open shadow roots included) ----------
        const string AutoLib = @"(function(){if(window.__aio&&window.__aio.v===4)return;var A={v:4};window.__aio=A;
var INTER='a[href],button,input:not([type=hidden]),select,textarea,summary,[role=button],[role=link],[role=checkbox],[role=radio],[role=tab],[role=menuitem],[role=menuitemcheckbox],[role=menuitemradio],[role=option],[role=switch],[role=combobox],[role=textbox],[role=searchbox],[role=slider],[role=spinbutton],[role=treeitem],[contenteditable=""""],[contenteditable=true],[onclick],[tabindex]:not([tabindex=""-1""])';
function vis(el){if(!el||!el.getBoundingClientRect)return false;var r=el.getBoundingClientRect();if(r.width<=0||r.height<=0)return false;var s=el.ownerDocument.defaultView.getComputedStyle(el);return s.visibility!=='hidden'&&s.display!=='none'&&parseFloat(s.opacity||'1')>0.02;}
function docs(){var out=[];function walk(d,depth){out.push(d);if(depth>3)return;var fs=d.querySelectorAll('iframe,frame');for(var i=0;i<fs.length;i++){try{var cd=fs[i].contentDocument;if(cd&&cd.documentElement)walk(cd,depth+1);}catch(e){}}}walk(document,0);return out;}
function all(root,sel,out){var l=root.querySelectorAll(sel);for(var i=0;i<l.length;i++)out.push(l[i]);var h=root.querySelectorAll('*');for(var j=0;j<h.length;j++){if(h[j].shadowRoot)all(h[j].shadowRoot,sel,out);}return out;}
function allDocs(sel){var out=[];docs().forEach(function(d){all(d,sel,out);});return out;}
function clean(s,n){return (s||'').replace(/\s+/g,' ').trim().slice(0,n||100);}
function role(el){var r=el.getAttribute('role');if(r)return r;var t=el.tagName.toLowerCase();if(t==='a')return 'link';if(t==='button'||t==='summary')return 'button';if(t==='select')return 'combobox';if(t==='textarea')return 'textbox';if(t==='input'){var ty=(el.type||'text').toLowerCase();if(ty==='checkbox'||ty==='radio')return ty;if(ty==='submit'||ty==='button'||ty==='reset'||ty==='image')return 'button';if(ty==='range')return 'slider';if(ty==='file')return 'file';return 'textbox';}if(el.isContentEditable)return 'textbox';return t;}
function name(el){var n=el.getAttribute('aria-label');if(n&&n.trim())return n;var lb=el.getAttribute('aria-labelledby');if(lb){var s='';lb.split(/\s+/).forEach(function(id){var x=el.ownerDocument.getElementById(id);if(x)s+=' '+x.innerText;});if(s.trim())return s;}
var t=el.tagName.toLowerCase();if(t==='input'||t==='textarea'||t==='select'){if(el.labels&&el.labels.length&&el.labels[0].innerText.trim())return el.labels[0].innerText;var ty=(el.type||'').toLowerCase();if(ty==='submit'||ty==='button'||ty==='reset')return el.value;}
else{var tx=el.innerText||el.textContent||'';if(tx.trim())return tx;}var img=el.querySelector&&el.querySelector('img[alt]');return el.getAttribute('placeholder')||el.getAttribute('title')||el.getAttribute('alt')||(img?img.alt:'')||el.getAttribute('name')||el.id||'';}
A.name=name;A.vis=vis;
function off(el){var x=0,y=0,w=el.ownerDocument.defaultView;while(w&&w!==window&&w.frameElement){var f=w.frameElement.getBoundingClientRect();x+=f.left+w.frameElement.clientLeft;y+=f.top+w.frameElement.clientTop;w=w.parent;}return {x:x,y:y};}
A.snapshot=function(max,textChars,scope){var items=[],n=0;allDocs('[data-aio-ref]').forEach(function(e){e.removeAttribute('data-aio-ref');});
var els=allDocs(INTER);if(scope){var sc=allDocs(scope);els=els.filter(function(e){return sc.some(function(s){return s.contains(e);});});}
for(var i=0;i<els.length&&items.length<max;i++){var el=els[i];if(!vis(el))continue;var t=el.tagName.toLowerCase();var r=el.getBoundingClientRect(),o=off(el);n++;var ref='e'+n;el.setAttribute('data-aio-ref',ref);
var it={ref:ref,role:role(el),name:clean(name(el),100)};
if(t==='input'||t==='textarea'||t==='select'){var ty=(el.type||'').toLowerCase();it.value=ty==='password'?(el.value?'(hidden)':''):clean(t==='select'&&el.selectedIndex>=0?el.options[el.selectedIndex].text:el.value,80);if(ty&&ty!=='text'&&t==='input')it.type=ty;if(el.required)it.required=true;if(el.readOnly)it.readonly=true;}
else if(el.isContentEditable)it.value=clean(el.innerText,80);
if(el.checked||el.getAttribute('aria-checked')==='true')it.checked=true;if(el.disabled||el.getAttribute('aria-disabled')==='true')it.disabled=true;
var ex=el.getAttribute('aria-expanded');if(ex)it.expanded=ex==='true';if(el.getAttribute('aria-selected')==='true')it.selected=true;
if(t==='a'&&el.href&&el.href.indexOf('javascript:')!==0)it.href=el.href.slice(0,160);
if(t==='select')it.options=[].slice.call(el.options,0,30).map(function(op){return clean(op.text,40);});
var cy=r.top+o.y,cx=r.left+o.x;if(cy+r.height<0||cy>innerHeight||cx+r.width<0||cx>innerWidth)it.offscreen=true;if(el.ownerDocument!==document)it.frame=true;
items.push(it);}
var heads=[];var hs=document.querySelectorAll('h1,h2,h3');for(var h=0;h<hs.length&&heads.length<25;h++){if(vis(hs[h]))heads.push(hs[h].tagName.toLowerCase()+': '+clean(hs[h].innerText,100));}
var res={title:document.title,url:location.href,ready:document.readyState,scroll:{y:Math.round(scrollY),page_height:document.documentElement.scrollHeight,view_height:innerHeight},headings:heads,count:items.length,elements:items};
if(els.length>items.length&&items.length>=max)res.more='Only the first '+max+' elements; pass max_items or selector to see others.';
var dl=document.querySelector('dialog[open],[role=dialog],[role=alertdialog],[aria-modal=true]');if(dl&&vis(dl))res.modal=clean(dl.getAttribute('aria-label')||dl.innerText,160);
if(textChars>0)res.text=(document.body?document.body.innerText:'').replace(/\n{3,}/g,'\n\n').slice(0,textChars);
var cross=0;document.querySelectorAll('iframe').forEach(function(f){try{if(!f.contentDocument)cross++;}catch(e){cross++;}});if(cross)res.cross_origin_frames=cross;
return res;};
A.find=function(s){var el=null,i,f;
if(s.ref){f=allDocs('[data-aio-ref=""'+s.ref+'""]');el=f[0];if(!el)throw new Error('Element '+s.ref+' is gone (the page changed): take a new browser_snapshot.');}
else if(s.selector){f=allDocs(s.selector);for(i=0;i<f.length;i++)if(vis(f[i])){el=f[i];break;}if(!el)el=f[0];if(!el)throw new Error('No element matches '+s.selector);}
else if(s.text){var q=clean(s.text,300).toLowerCase(),best=null,bs=0,c=allDocs(INTER+',label');for(i=0;i<c.length;i++){if(!vis(c[i]))continue;var nm=clean(name(c[i]),300).toLowerCase();var sc=nm===q?3:nm.indexOf(q)===0?2:nm.indexOf(q)>=0?1:0;if(sc>bs){bs=sc;best=c[i];if(sc===3)break;}}
if(best&&best.tagName.toLowerCase()==='label'&&best.control)best=best.control;
if(!best){var w=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT),nd;while((nd=w.nextNode())){if(nd.nodeValue.toLowerCase().indexOf(q)>=0&&vis(nd.parentElement)){best=nd.parentElement;break;}}}
el=best;if(!el)throw new Error('Nothing on the page matches ""'+s.text+'"". Take a browser_snapshot.');}
else throw new Error('Give ref, selector or text.');window.__aioEl=el;return el;};
A.point=function(s){var el=A.find(s);el.scrollIntoView({block:'center',inline:'center',behavior:'instant'});var r=el.getBoundingClientRect(),o=off(el);
var rs=[].filter.call(el.getClientRects(),function(q){return q.width>0&&q.height>0;});if(rs.length>1){var d=el.ownerDocument;for(var k=0;k<rs.length;k++){var hx=rs[k].left+rs[k].width/2,hy=rs[k].top+rs[k].height/2,at=d.elementFromPoint(hx,hy);if(at&&(at===el||el.contains(at))){r=rs[k];break;}}}
var x=o.x+r.left+r.width/2,y=o.y+r.top+r.height/2;
var res={x:x,y:y,w:r.width,h:r.height,name:clean(name(el),80),role:role(el)};if(el.disabled||el.getAttribute('aria-disabled')==='true')res.disabled=true;
if(el.ownerDocument===document){var top=document.elementFromPoint(x,y);if(top&&top!==el&&!el.contains(top)&&!top.contains(el)&&!(el.labels&&[].some.call(el.labels,function(l){return l.contains(top);})))res.covered=clean(name(top)||top.tagName,60);}return res;};
A.box=function(s){var el=A.find(s);el.scrollIntoView({block:'center',behavior:'instant'});var r=el.getBoundingClientRect(),o=off(el);return {x:o.x+r.left+scrollX,y:o.y+r.top+scrollY,w:r.width,h:r.height};};
A.mark=function(p){try{var d=document.createElement('div');d.style.cssText='position:fixed;z-index:2147483647;pointer-events:none;border:2px solid #e5484d;border-radius:4px;box-shadow:0 0 0 4px rgba(229,72,77,.28);transition:opacity .4s;left:'+(p.x-p.w/2-3)+'px;top:'+(p.y-p.h/2-3)+'px;width:'+(p.w+6)+'px;height:'+(p.h+6)+'px';document.documentElement.appendChild(d);setTimeout(function(){d.style.opacity='0';},700);setTimeout(function(){d.remove();},1200);}catch(e){}return 1;};
A.focusFor=function(s,clear){var el=A.find(s);el.scrollIntoView({block:'center',behavior:'instant'});var t=el.tagName.toLowerCase();if(t==='label'&&el.control){el=el.control;window.__aioEl=el;t=el.tagName.toLowerCase();}
var ty=(el.type||'').toLowerCase(),editable=((t==='input'&&['checkbox','radio','file','submit','button','reset','image','range','color'].indexOf(ty)<0)||t==='textarea'||el.isContentEditable);
if(editable||t==='select')el.focus();
if(editable){if(clear){if(t==='input'||t==='textarea'){try{el.select();}catch(e){}}else{var rg=el.ownerDocument.createRange();rg.selectNodeContents(el);var sl=el.ownerDocument.defaultView.getSelection();sl.removeAllRanges();sl.addRange(rg);}}
else if((t==='input'||t==='textarea')){try{var L=el.value.length;el.setSelectionRange(L,L);}catch(e){}}}
return {tag:t,type:ty,editable:editable,name:clean(name(el),80)};};
A.after=function(){var el=window.__aioEl;if(!el)return null;try{el.dispatchEvent(new Event('change',{bubbles:true}));}catch(e){}var t=el.tagName.toLowerCase();return {value:el.type==='password'?'(hidden)':clean(t==='input'||t==='textarea'?el.value:el.innerText,200)};};
A.select=function(s,vals){var el=A.find(s);if(el.tagName.toLowerCase()==='label'&&el.control)el=el.control;if(el.tagName.toLowerCase()!=='select')throw new Error('Not a dropdown list (<select>): click it with browser_click, then click the option.');
var want=vals.map(function(v){return String(v).toLowerCase().trim();}),picked=[];function m(o,exact){var tx=clean(o.text,300).toLowerCase(),va=String(o.value).toLowerCase();return want.some(function(w){return exact?(tx===w||va===w):(tx.indexOf(w)>=0);});}
var hits=[].filter.call(el.options,function(o){return m(o,true);});if(!hits.length)hits=[].filter.call(el.options,function(o){return m(o,false);});
if(!hits.length)throw new Error('No option matches '+vals.join(', ')+'. Options: '+[].slice.call(el.options,0,30).map(function(o){return clean(o.text,40);}).join(' | '));
if(el.multiple){[].forEach.call(el.options,function(o){o.selected=hits.indexOf(o)>=0;});}else{el.value=hits[0].value;hits=[hits[0]];}
el.dispatchEvent(new Event('input',{bubbles:true}));el.dispatchEvent(new Event('change',{bubbles:true}));return {selected:hits.map(function(o){return clean(o.text,80);})};};
A.fileInput=function(s){var el=A.find(s);if(el.tagName.toLowerCase()==='label'&&el.control){el=el.control;window.__aioEl=el;}if(!(el.tagName.toLowerCase()==='input'&&el.type==='file')){var inner=el.querySelector&&el.querySelector('input[type=file]');if(inner){el=inner;window.__aioEl=el;}else throw new Error('Not a file input. Look for input[type=file] in browser_snapshot (role file).');}return {name:clean(name(el),80),multiple:!!el.multiple};};
A.check=function(c){if(c.selector){var any=allDocs(c.selector).some(vis);return c.gone?!any:any;}
if(c.text){var q=c.text.toLowerCase(),has=docs().some(function(d){return d.body&&(d.body.innerText||'').toLowerCase().indexOf(q)>=0;});return c.gone?!has:has;}
if(c.url){var u=location.href.indexOf(c.url)>=0;return c.gone?!u:u;}return document.readyState==='complete';};
A.extract=function(what,max,scope){var root=scope?document.querySelector(scope):document;if(!root)throw new Error('No element matches '+scope);var out=[];
if(what==='text')return {url:location.href,title:document.title,text:((root.body||root).innerText||'').replace(/\n{3,}/g,'\n\n').slice(0,max*100)};
if(what==='links'){root.querySelectorAll('a[href]').forEach(function(a){if(out.length<max&&a.href.indexOf('javascript:')!==0)out.push({text:clean(a.innerText||a.title,100),href:a.href});});return {count:out.length,links:out};}
if(what==='images'){root.querySelectorAll('img').forEach(function(i){if(out.length<max)out.push({alt:clean(i.alt,100),src:(i.currentSrc||i.src).slice(0,300),width:i.naturalWidth,height:i.naturalHeight});});return {count:out.length,images:out};}
if(what==='headings'){root.querySelectorAll('h1,h2,h3,h4,h5,h6').forEach(function(h){if(out.length<max)out.push(h.tagName.toLowerCase()+': '+clean(h.innerText,140));});return {headings:out};}
if(what==='tables'){root.querySelectorAll('table').forEach(function(tb,ti){if(out.length>=20)return;var rows=[];[].slice.call(tb.rows,0,max).forEach(function(r){rows.push([].map.call(r.cells,function(c){return clean(c.innerText,200);}));});out.push({index:ti,caption:tb.caption?clean(tb.caption.innerText,100):'',rows:rows,total_rows:tb.rows.length});});return {count:out.length,tables:out};}
if(what==='forms'){var fs=root.querySelectorAll('form');var groups=fs.length?[].slice.call(fs):[root];groups.forEach(function(f,fi){var fields=[];f.querySelectorAll('input:not([type=hidden]),select,textarea').forEach(function(e){if(fields.length>=max)return;var ty=(e.type||e.tagName).toLowerCase();var it={name:clean(name(e),80),field:e.name||e.id||'',type:ty,value:ty==='password'?(e.value?'(hidden)':''):clean(e.value,80)};if(e.required)it.required=true;if(e.tagName.toLowerCase()==='select')it.options=[].slice.call(e.options,0,30).map(function(o){return clean(o.text,40);});if(e.checked)it.checked=true;fields.push(it);});out.push({form:fi,action:f.action||'',method:(f.method||'').toLowerCase(),fields:fields});});return {forms:out};}
var meta={title:document.title,url:location.href,lang:document.documentElement.lang||''};document.querySelectorAll('meta[name],meta[property]').forEach(function(m){var k=m.getAttribute('name')||m.getAttribute('property');if(Object.keys(meta).length<60)meta[k]=clean(m.content,300);});var can=document.querySelector('link[rel=canonical]');if(can)meta.canonical=can.href;return meta;};
})()";
    }

    static partial class ApiTools
    {
        static readonly HashSet<string> BrowserAutoNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "browser_tabs", "browser_navigate", "browser_snapshot", "browser_click", "browser_hover", "browser_fill", "browser_select", "browser_key",
            "browser_scroll", "browser_wait", "browser_screenshot", "browser_pdf", "browser_upload", "browser_downloads", "browser_dialogs",
            "browser_console", "browser_network", "browser_cookies", "browser_emulate", "browser_extract"
        };

        static void AddBrowserTools(List<Dictionary<string, object>> l)
        {
            const string tab = "tab|integer|Optional 0-based tab index (default: the active tab)";
            const string rf = "ref|string|Element ref from browser_snapshot (e.g. e12) - preferred";
            const string sel = "selector|string|Or a CSS selector";
            const string txt = "text|string|Or the element's visible text / label";
            const string to = "timeout_ms|integer|Max wait in ms";
            l.Add(T("browser_snapshot", "Browser: page snapshot with element refs", "Start here for any web task in the app browser. Lists every visible clickable / typeable element with a ref (e1, e2...), role, name, value and state (checked, disabled, expanded), plus title, address, headings, open modal and optional page text. Same-origin frames and shadow DOM included. Refs are valid until the page changes; take a new snapshot after navigation.", true,
                new[] { tab, "max_items|integer|Max elements (default 250)", "text_chars|integer|Also return this much page text (default 0)", "selector|string|Only elements inside this CSS selector" }));
            l.Add(T("browser_navigate", "Browser: go to a page / back / forward / reload", "Navigate the app browser and wait until the page has loaded. Returns address, title, HTTP status and load errors.", false,
                new[] { "url|string|Web address or search words (action=go)", "action|string|go (default), back, forward, reload, stop", "new_tab|boolean|Open in a new tab", "wait|string|load (default) or none", to, tab }));
            l.Add(T("browser_click", "Browser: click an element", "Real mouse click (trusted input, like a person) on an element by ref, selector or visible text, or at x,y. Scrolls it into view, warns if another element covers it, waits for a page change it causes and reports a new tab or dialog.", false,
                new[] { rf, sel, txt, "x|number|Or viewport x (CSS pixels)", "y|number|Viewport y", "button|string|left (default), right, middle", "double|boolean|Double-click", "force|boolean|Click even if disabled", to, tab }));
            l.Add(T("browser_hover", "Browser: point at an element", "Move the mouse over an element (opens hover menus and tooltips).", false, new[] { rf, sel, txt, tab }));
            l.Add(T("browser_fill", "Browser: type into fields", "Type text into an input, text area or editable element (real keyboard text; replaces the old value unless clear=false). A <select> gets the option chosen. Fill a whole form at once with fields: {ref | selector | label: value}. submit=true presses Enter after.", false,
                new[] { rf, sel, "label|string|Or the field's label / placeholder", "value|string|Text to type", "fields|any_object|Many fields at once: {\"e3\": \"Ahmed\", \"Email\": \"a@b.com\"}", "clear|boolean|Replace the current value (default true)", "submit|boolean|Press Enter after typing", to, tab }));
            l.Add(T("browser_select", "Browser: choose in a dropdown list", "Choose option(s) of a <select> by visible text or value.", false,
                new[] { rf, sel, "label|string|Or the list's label", "value|string|Option text or value", "values|array|Several options (multi-select)", tab }));
            l.Add(T("browser_key", "Browser: press keys / type", "Press keys in the page like a keyboard: 'Enter', 'Tab', 'Escape', 'ArrowDown', 'Control+A', 'Shift+Tab', several separated by spaces ('Tab Tab Enter'). type_text types text into the focused element. ref/selector focuses an element first.", false,
                new[] { "keys|string|Key names or combinations, space separated", "type_text|string|Text to type", "repeat|integer|Repeat the keys (default 1)", rf, sel, to, tab }));
            l.Add(T("browser_scroll", "Browser: scroll", "Scroll an element into view (ref/selector/text), to the top/bottom, or by dy pixels with the mouse wheel (default: one screen down). Returns the scroll position and whether the bottom is reached.", false,
                new[] { rf, sel, txt, "to|string|top or bottom", "dy|number|Pixels down (negative = up)", "dx|number|Pixels right", tab }));
            l.Add(T("browser_wait", "Browser: wait for something", "Wait until an element appears (selector), text appears, the address contains something, the page loaded (default) or the network is idle (state=network_idle); gone=true waits for it to disappear. ms just waits.", true,
                new[] { "selector|string|CSS selector to wait for", "text|string|Text to wait for", "url_contains|string|Address part to wait for", "state|string|load (default) or network_idle", "gone|boolean|Wait until it disappears", "ms|integer|Just wait this long", "timeout_ms|integer|Give up after (default 10000)", tab }));
            l.Add(T("browser_screenshot", "Browser: screenshot", "Save a PNG of the visible page, the full page (full_page=true) or one element (ref/selector). Returns the file path and size.", false,
                new[] { "path|string|Optional output .png path", "full_page|boolean|Whole page, not only the visible part", rf, sel, tab }));
            l.Add(T("browser_pdf", "Browser: save page as PDF", "Save the current page as a PDF file.", false, new[] { "path|string|Optional output .pdf path", tab }));
            l.Add(T("browser_upload", "Browser: attach files to an upload field", "Put local files into a file input (input[type=file]) without the file dialog.", false,
                new[] { rf, sel, "path|string|Full path of one file", "paths|array|Full paths of several files", tab }));
            l.Add(T("browser_downloads", "Browser: downloads", "List downloads (file, state, size) or wait for the current / next download to finish (action=wait).", true,
                new[] { "action|string|list (default) or wait", "since_ms|integer|For wait: count downloads started this many ms ago (default 15000)", "timeout_ms|integer|For wait (default 60000)" }));
            l.Add(T("browser_dialogs", "Browser: page dialogs (alert / confirm / prompt)", "See recent page dialogs and choose who answers new ones: user (default: the person sees them), accept or dismiss (automatic). prompt_text answers prompts in accept mode.", false,
                new[] { "mode|string|user, accept or dismiss", "prompt_text|string|Text for prompt dialogs when mode=accept", "limit|integer|Recent dialogs to list" }));
            l.Add(T("browser_console", "Browser: console messages and errors", "Recent console messages and JavaScript errors of the tab.", true,
                new[] { "level|string|all (default), error, warning, log, info", "limit|integer|Max messages (default 50)", "clear|boolean|Clear after reading", tab }));
            l.Add(T("browser_network", "Browser: network requests", "Recent requests of the tab: method, address, status, type, size, errors; failed_only shows errors and HTTP >= 400.", true,
                new[] { "filter|string|Only addresses containing this", "failed_only|boolean|Only failures", "limit|integer|Max (default 60)", "clear|boolean|Clear after reading", tab }));
            l.Add(T("browser_cookies", "Browser: cookies", "List, set, delete or clear cookies of the app browser.", false,
                new[] { "action|string|list (default), set, delete, clear", "url|string|Site address (default: current page)", "name|string|Cookie name", "value|string|Cookie value (set)", "domain|string|Domain (set)", "path|string|Path (set)", tab }));
            l.Add(T("browser_emulate", "Browser: phone / tablet view, user agent, offline", "Show the tab as a phone, android, tablet or a custom size; set the user agent; simulate offline. device=desktop resets.", false,
                new[] { "device|string|phone, android, tablet, desktop (reset)", "width|integer|Custom width", "height|integer|Custom height", "scale|number|Device pixel ratio", "mobile|boolean|Mobile mode", "user_agent|string|User agent text", "offline|boolean|Simulate no internet", tab }));
            l.Add(T("browser_extract", "Browser: extract data", "Get structured data from the page: text, links, tables (rows of cells), forms (fields, values, options), images, headings or meta tags.", true,
                new[] { "what|string|text (default), links, tables, forms, images, headings, meta", "max|integer|Max items / rows (default 200)", "selector|string|Only inside this CSS selector", tab }));
        }

        static string BrowserAutoTool(string tool, Dictionary<string, object> a)
        {
            if (App.I == null || App.I.Sync == null || !App.I.Sync.IsHandleCreated) throw new Exception("The app browser needs the desktop app running (not the headless MCP mode).");
            if (!App.I.Sync.InvokeRequired) throw new Exception("Browser automation must be called from a background thread.");
            int wait = Math.Max(5000, Math.Min(110000, I(a, "timeout_ms", tool == "browser_downloads" ? 60000 : 30000))) + 15000;
            var response = new TaskCompletionSource<object>();
            BrowserPage page = null;
            using (var timer = new CancellationTokenSource(wait))
            {
                var stop = timer.Token;
                var deadline = Task.Delay(Timeout.Infinite, stop);
                App.I.Sync.BeginInvoke(new Action(async () =>
                {
                    if (stop.IsCancellationRequested) return;
                    try { page = BrowserPage.ForAutomation(); response.TrySetResult(await page.Automate(tool, a ?? new Dictionary<string, object>(), stop)); }
                    catch (Exception ex) { response.TrySetException(ex); }
                }));
                if (Task.WhenAny(response.Task, deadline).GetAwaiter().GetResult() != response.Task && response.TrySetCanceled())
                    throw new TimeoutException("The browser did not answer in " + (wait / 1000) + " s" + (page != null && page.DialogOpen ? ": the page shows a dialog that waits for the user (see browser_dialogs)." : "."));
                timer.Cancel();
                return J(response.Task.GetAwaiter().GetResult());
            }
        }
    }
}
