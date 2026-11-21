using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace StickyPro
{
    public class DashboardMeta
    {
        public string slug = "";
        public string title = "";
        public string file = "";
        public string updated_utc = "";
    }

    // OpenClaw-style Live Canvas & interactive HTML Dashboard publisher (<Store.Dir>\dashboards\).
    // Writes studio-grade offline HTML5 visuals and opens/refreshes them seamlessly in the embedded WebView2 Browser,
    // reusing an existing tab for live-canvas.html (or any published dashboard slug) rather than spawning duplicate tabs.
    static class DashboardGen
    {
        public const string LiveCanvasSlug = "live-canvas";
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        static readonly object ioLock = new object();

        public static string Dir
        {
            get
            {
                string root = string.IsNullOrEmpty(Store.Dir) ? Store.DefaultDir : Store.Dir;
                return Path.Combine(root, "dashboards");
            }
        }

        public static string IndexFile { get { return Path.Combine(Dir, "index.json"); } }
        public static string LiveCanvasPath { get { return Path.Combine(Dir, LiveCanvasSlug + ".html"); } }

        public static Dictionary<string, object> Publish(string slug, string title, string html, bool open = true)
        {
            if (string.IsNullOrWhiteSpace(html)) throw new ArgumentException("html content is required.");

            string safeSlug = SanitizeSlug(slug);
            string displayTitle = string.IsNullOrWhiteSpace(title) ? safeSlug : title.Trim();
            string finalHtml = EnsureFullDocument(html, displayTitle);
            string filePath = Path.Combine(Dir, safeSlug + ".html");
            string updatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            lock (ioLock)
            {
                Directory.CreateDirectory(Dir);
                WriteAtomic(filePath, finalHtml);
                UpdateIndexLocked(safeSlug, displayTitle, safeSlug + ".html", updatedUtc);
            }

            bool opened = false;
            if (open && App.I != null)
            {
                try
                {
                    if (App.I.Sync != null && !App.I.Sync.IsDisposed)
                    {
                        if (App.I.Sync.InvokeRequired)
                            App.I.Sync.Invoke(new Action(delegate { App.I.OpenOrReloadInBrowser(filePath); }));
                        else
                            App.I.OpenOrReloadInBrowser(filePath);
                        opened = true;
                    }
                }
                catch (Exception ex)
                {
                    App.Log("DashboardGen open: " + ex.Message);
                }
            }

            AssistantHarness.Report("Published Live Canvas: " + displayTitle);

            long size = 0;
            try { size = new FileInfo(filePath).Length; }
            catch (Exception) { }

            return new Dictionary<string, object>
            {
                { "slug", safeSlug },
                { "title", displayTitle },
                { "path", filePath },
                { "url", new Uri(Path.GetFullPath(filePath)).AbsoluteUri },
                { "size", size },
                { "updated_utc", updatedUtc },
                { "opened", opened },
                { "is_live_canvas", string.Equals(safeSlug, LiveCanvasSlug, StringComparison.OrdinalIgnoreCase) }
            };
        }

        public static Dictionary<string, object> UpdateCanvas(string html, string title = "Live Canvas", bool open = true)
        {
            string displayTitle = string.IsNullOrWhiteSpace(title) ? "Live Canvas" : title.Trim();
            return Publish(LiveCanvasSlug, displayTitle, html, open);
        }

        public static List<Dictionary<string, object>> ListDashboards()
        {
            var result = new List<Dictionary<string, object>>();
            if (!Directory.Exists(Dir)) return result;

            Dictionary<string, DashboardMeta> index;
            lock (ioLock) { index = LoadIndexLocked(); }

            string[] files;
            try { files = Directory.GetFiles(Dir, "*.html"); }
            catch (Exception) { return result; }

            var items = new List<Tuple<DateTime, Dictionary<string, object>>>();
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    var fi = new FileInfo(files[i]);
                    string slug = Path.GetFileNameWithoutExtension(fi.Name);
                    DashboardMeta meta;
                    string title = (index.TryGetValue(slug, out meta) && meta != null && !string.IsNullOrWhiteSpace(meta.title))
                        ? meta.title
                        : slug;
                    var entry = new Dictionary<string, object>
                    {
                        { "slug", slug },
                        { "title", title },
                        { "path", fi.FullName },
                        { "url", new Uri(fi.FullName).AbsoluteUri },
                        { "size", fi.Length },
                        { "modified", fi.LastWriteTimeUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                        { "is_live_canvas", string.Equals(slug, LiveCanvasSlug, StringComparison.OrdinalIgnoreCase) }
                    };
                    items.Add(Tuple.Create(fi.LastWriteTimeUtc, entry));
                }
                catch (Exception) { }
            }

            items.Sort(delegate(Tuple<DateTime, Dictionary<string, object>> a, Tuple<DateTime, Dictionary<string, object>> b)
            {
                return b.Item1.CompareTo(a.Item1);
            });
            for (int i = 0; i < items.Count; i++) result.Add(items[i].Item2);
            return result;
        }

        public static string SanitizeSlug(string slug)
        {
            string s = (slug ?? "").Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"[^a-z0-9_-]+", "-").Trim('-', '_');
            if (s.Length > 64) s = s.Substring(0, 64).TrimEnd('-', '_');
            return s.Length > 0 ? s : "dashboard";
        }

        static string EnsureFullDocument(string html, string title)
        {
            string trimmed = html.Trim();
            if (trimmed.IndexOf("<!doctype", StringComparison.OrdinalIgnoreCase) >= 0
                || trimmed.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0)
                return html;

            bool rtl = Ui.IsRtl(trimmed) || Ui.IsRtl(title);
            string escTitle = (title ?? "Live Canvas").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            return
                "<!doctype html>\n" +
                "<html lang=\"" + (rtl ? "ar" : "en") + "\" dir=\"" + (rtl ? "rtl" : "ltr") + "\">\n" +
                "<head>\n" +
                "  <meta charset=\"utf-8\">\n" +
                "  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
                "  <title>" + escTitle + "</title>\n" +
                "  <style>\n" +
                "    :root {\n" +
                "      --bg: #0f1217; --bg-ok: oklch(18% 0.012 250);\n" +
                "      --surface: #171c24; --surface-2: #1f2630; --line: rgba(255,255,255,0.09);\n" +
                "      --ink: #eef2f7; --muted: #93a1b5; --accent: #d97745; --signal: #4f8cff;\n" +
                "      --ease: cubic-bezier(0.22, 1, 0.36, 1);\n" +
                "    }\n" +
                "    @media (prefers-color-scheme: light) {\n" +
                "      :root { --bg: #f6f3ec; --surface: #fffdf8; --surface-2: #efeae0; --line: rgba(25,28,34,0.10); --ink: #1a1f28; --muted: #5a6474; --accent: #c25926; --signal: #2458d6; }\n" +
                "    }\n" +
                "    * { box-sizing: border-box; }\n" +
                "    body { margin: 0; padding: 28px; background: var(--bg); color: var(--ink); font: 15px/1.6 'Segoe UI', system-ui, sans-serif; font-variant-numeric: tabular-nums; }\n" +
                "    .canvas-shell { max-width: 1180px; margin: 0 auto; animation: rise 420ms var(--ease) both; }\n" +
                "    code, pre { dir: ltr; unicode-bidi: isolate; font-family: 'Cascadia Mono', Consolas, monospace; }\n" +
                "    @keyframes rise { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }\n" +
                "    @media (prefers-reduced-motion: reduce) { .canvas-shell { animation: none; } }\n" +
                "  </style>\n" +
                "</head>\n" +
                "<body>\n" +
                "  <main class=\"canvas-shell\">\n" +
                trimmed + "\n" +
                "  </main>\n" +
                "</body>\n" +
                "</html>\n";
        }

        static Dictionary<string, DashboardMeta> LoadIndexLocked()
        {
            var map = new Dictionary<string, DashboardMeta>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(IndexFile)) return map;
                var loaded = Js.Deserialize<Dictionary<string, DashboardMeta>>(File.ReadAllText(IndexFile, Encoding.UTF8));
                if (loaded != null)
                {
                    foreach (var kv in loaded)
                        if (kv.Value != null) map[kv.Key] = kv.Value;
                }
            }
            catch (Exception) { }
            return map;
        }

        static void UpdateIndexLocked(string slug, string title, string file, string updatedUtc)
        {
            try
            {
                var map = LoadIndexLocked();
                map[slug] = new DashboardMeta { slug = slug, title = title, file = file, updated_utc = updatedUtc };
                WriteAtomic(IndexFile, Js.Serialize(map));
            }
            catch (Exception) { }
        }

        static void WriteAtomic(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp." + Guid.NewGuid().ToString("N").Substring(0, 6);
            File.WriteAllText(tmp, content ?? "", Utf8NoBom);
            if (File.Exists(path)) File.Replace(tmp, path, null, true);
            else File.Move(tmp, path);
        }
    }
}
