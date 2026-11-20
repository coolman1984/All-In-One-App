using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace StickyPro
{
    // Emails as web pages for the Browser page: .eml is read here (MIME), .msg through Outlook (COM, read only).
    // The page is written to %TEMP%\AllInOne-mail and opened as a file.
    static class Mail
    {
        static string OutDir { get { string d = Path.Combine(Path.GetTempPath(), "AllInOne-mail"); Directory.CreateDirectory(d); return d; } }

        public static string EmlToHtml(string path) { return EmlToHtmlFrom(path, path); }
        static string EmlToHtmlFrom(string path, string source)
        {
            var root = Part.Parse(File.ReadAllBytes(path));
            var all = root.Flatten().ToList();
            var html = all.FirstOrDefault(p => p.Type == "text/html" && !p.IsAttachment);
            var text = all.FirstOrDefault(p => p.Type == "text/plain" && !p.IsAttachment);
            string body = html != null ? html.Text() : text != null ? "<pre style='white-space:pre-wrap;font:14px Segoe UI'>" + WebUtility.HtmlEncode(text.Text()) + "</pre>" : "<p>(no text)</p>";
            // inline pictures (cid:) become data addresses
            foreach (var p in all.Where(p => p.ContentId != null && p.Type.StartsWith("image/")))
                body = body.Replace("cid:" + p.ContentId, "data:" + p.Type + ";base64," + Convert.ToBase64String(p.Data()));
            var files = all.Where(p => p.IsAttachment).Select(p => p.FileName ?? "attachment").ToList();
            return Write(source, root.Header("Subject"), root.Header("From"), root.Header("To"), root.Header("Cc"), root.Header("Date"), files, body);
        }

        // True when a company DRM (NASCA) wraps the file: only an authorized app (Office) can read what is inside.
        public static bool IsDrm(string path)
        {
            try { using (var fs = File.OpenRead(path)) { var b = new byte[9]; int n = fs.Read(b, 0, 9); return n == 9 && Encoding.ASCII.GetString(b) == "<## NASCA"; } }
            catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
        }

        // Mysingle mail (.mysingle, Samsung): a MIME e-mail, usually NASCA-wrapped. Wrapped files are read through Word
        // (authorized, hidden, read only, never saved) as Latin-1 text, which gives the original bytes back; then parsed here.
        public static string MysingleToHtml(string path)
        {
            byte[] bytes = IsDrm(path) ? ReadThroughWord(path) : File.ReadAllBytes(path);
            string tmp = Path.Combine(OutDir, Path.GetFileNameWithoutExtension(path) + ".eml");
            File.WriteAllBytes(tmp, bytes);
            try { return EmlToHtmlFrom(tmp, path); } finally { try { File.Delete(tmp); } catch { } }
        }

        static byte[] ReadThroughWord(string path)
        {
            var type = Type.GetTypeFromProgID("Word.Application");
            if (type == null) throw new InvalidOperationException("This file is protected (NASCA DRM) and Word is needed to read it.");
            // A copy with a .txt name: Word opens it as plain text and never touches the original.
            string copy = Path.Combine(OutDir, Guid.NewGuid().ToString("N") + ".txt");
            File.Copy(path, copy, true);
            object app = Activator.CreateInstance(type);   // its own hidden Word process (Word starts one per request)
            object doc = null;
            try
            {
                Set(app, "Visible", false); Set(app, "DisplayAlerts", 0);
                object docs = Get(app, "Documents");
                // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles, ..., Format=wdOpenFormatText(4), Encoding=28591 Latin-1)
                object missing = Type.Missing;
                doc = docs.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, docs,
                    new object[] { copy, false, true, false, missing, missing, missing, missing, missing, 4, 28591 });
                string text = Get(Get(doc, "Content"), "Text") as string ?? "";
                text = text.Replace("\r", "\r\n");   // Word keeps paragraphs as CR
                return Encoding.GetEncoding(28591).GetBytes(text);
            }
            finally
            {
                try { if (doc != null) Call(doc, "Close", 0 /* do not save */); } catch { }
                try { Call(app, "Quit", 0); } catch { }
                try { File.Delete(copy); } catch { }
            }
        }
        static void Set(object o, string name, object v) { o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { v }); }

        public static string MsgToHtml(string path)
        {
            var type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null) throw new InvalidOperationException("Outlook is not installed; .msg emails need it.");
            object app = Activator.CreateInstance(type);   // attaches to the running Outlook (never closed here)
            object session = Get(app, "Session");
            object item = Call(session, "OpenSharedItem", path);
            try
            {
                string html = Get(item, "HTMLBody") as string;
                if (string.IsNullOrEmpty(html)) html = "<pre style='white-space:pre-wrap;font:14px Segoe UI'>" + WebUtility.HtmlEncode(Get(item, "Body") as string ?? "") + "</pre>";
                var files = new List<string>();
                object atts = Get(item, "Attachments");
                int n = Convert.ToInt32(Get(atts, "Count"));
                for (int i = 1; i <= n; i++) files.Add(Get(Call(atts, "Item", i), "FileName") as string);
                object sent = Get(item, "SentOn");
                return Write(path, Get(item, "Subject") as string, (Get(item, "SenderName") as string) + " <" + (Get(item, "SenderEmailAddress") as string) + ">",
                    Get(item, "To") as string, Get(item, "CC") as string, sent is DateTime ? ((DateTime)sent).ToString("ddd d MMM yyyy HH:mm") : "", files, html);
            }
            finally { try { Call(item, "Close", 1 /* olDiscard */); } catch { } }
        }

        static object Get(object o, string name) { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }
        static object Call(object o, string name, params object[] args) { return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args); }

        static string Write(string source, string subject, string from, string to, string cc, string date, List<string> files, string body)
        {
            Func<string, string> e = s => WebUtility.HtmlEncode(s ?? "");
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'><title>").Append(e(subject)).Append("</title><style>")
              .Append("body{margin:0;font:14px 'Segoe UI',sans-serif;color:#1d1d20;background:#f4f4f6}")
              .Append(".h{background:#fff;border-bottom:1px solid #e3e3e7;padding:18px 28px}.h h1{font-size:20px;margin:0 0 10px}")
              .Append(".h td{padding:2px 12px 2px 0;vertical-align:top}.h td:first-child{color:#6c6c75}.att{margin-top:8px;color:#6c6c75}")
              .Append(".b{background:#fff;margin:16px 28px;padding:20px;border-radius:10px;border:1px solid #e3e3e7}")
              .Append("@media (prefers-color-scheme:dark){body{background:#121214;color:#eee}.h,.b{background:#1c1c20;border-color:#2b2b31}}")
              .Append("</style></head><body><div class='h'><h1>").Append(e(subject)).Append("</h1><table>");
            foreach (var row in new[] { new[] { "From", from }, new[] { "To", to }, new[] { "Cc", cc }, new[] { "Date", date } })
                if (!string.IsNullOrWhiteSpace(row[1])) sb.Append("<tr><td>").Append(row[0]).Append("</td><td>").Append(e(row[1])).Append("</td></tr>");
            sb.Append("</table>");
            if (files.Count > 0) sb.Append("<div class='att'>Attachments: ").Append(e(string.Join(", ", files))).Append("</div>");
            sb.Append("</div><div class='b'>").Append(body).Append("</div></body></html>");
            string name = Regex.Replace(Path.GetFileNameWithoutExtension(source), "[^\\w\\- ]", "_");
            string outFile = Path.Combine(OutDir, name + "-" + Math.Abs(source.ToLowerInvariant().GetHashCode()).ToString("x") + ".html");
            File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
            return outFile;
        }

        // ---------- a small MIME reader ----------
        class Part
        {
            readonly Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            byte[] raw;
            readonly List<Part> children = new List<Part>();
            public string Header(string name) { string v; return headers.TryGetValue(name, out v) ? Decode(v) : ""; }
            string Raw(string name) { string v; return headers.TryGetValue(name, out v) ? v : ""; }
            public string Type { get { var t = Raw("Content-Type").Split(';')[0].Trim().ToLowerInvariant(); return t.Length == 0 ? "text/plain" : t; } }
            string Param(string header, string key) { var m = Regex.Match(Raw(header), key + @"\*?=""?([^"";]+)""?", RegexOptions.IgnoreCase); return m.Success ? Decode(m.Groups[1].Value.Trim()) : null; }
            public string FileName { get { return Param("Content-Disposition", "filename") ?? Param("Content-Type", "name"); } }
            public string ContentId { get { var v = Raw("Content-ID").Trim().Trim('<', '>'); return v.Length == 0 ? null : v; } }
            public bool IsAttachment { get { return Raw("Content-Disposition").StartsWith("attachment", StringComparison.OrdinalIgnoreCase) || (FileName != null && !Type.StartsWith("text/") && ContentId == null); } }
            public IEnumerable<Part> Flatten() { yield return this; foreach (var c in children) foreach (var p in c.Flatten()) yield return p; }

            public static Part Parse(byte[] bytes)
            {
                var p = new Part();
                string text = Encoding.GetEncoding(28591).GetString(bytes);   // bytes 1:1 as chars
                int split = text.IndexOf("\r\n\r\n"); int skip = 4;
                if (split < 0) { split = text.IndexOf("\n\n"); skip = 2; }
                string head = split < 0 ? text : text.Substring(0, split);
                string body = split < 0 ? "" : text.Substring(split + skip);
                string key = null;
                foreach (var line in head.Replace("\r\n", "\n").Split('\n'))
                {
                    if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && key != null) { p.headers[key] += " " + line.Trim(); continue; }
                    int c = line.IndexOf(':'); if (c <= 0) continue;
                    key = line.Substring(0, c).Trim(); p.headers[key] = line.Substring(c + 1).Trim();
                }
                p.raw = Encoding.GetEncoding(28591).GetBytes(body);
                if (p.Type.StartsWith("multipart/"))
                {
                    string b = p.Param("Content-Type", "boundary");
                    if (b != null)
                        foreach (var chunk in body.Split(new[] { "--" + b }, StringSplitOptions.None).Skip(1))
                        {
                            if (chunk.StartsWith("--")) break;
                            p.children.Add(Parse(Encoding.GetEncoding(28591).GetBytes(chunk.TrimStart('\r', '\n'))));
                        }
                }
                return p;
            }

            public byte[] Data()
            {
                string enc = Raw("Content-Transfer-Encoding").Trim().ToLowerInvariant();
                string s = Encoding.GetEncoding(28591).GetString(raw);
                if (enc == "base64") { try { return Convert.FromBase64String(Regex.Replace(s, @"[^A-Za-z0-9+/=]", "")); } catch { return raw; } }
                if (enc == "quoted-printable") return QP(s);
                return raw;
            }

            public string Text()
            {
                string cs = Param("Content-Type", "charset") ?? "utf-8";
                Encoding encoding; try { encoding = Encoding.GetEncoding(cs); } catch { encoding = Encoding.UTF8; }
                return encoding.GetString(Data());
            }

            static byte[] QP(string s)
            {
                var o = new List<byte>(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    if (s[i] == '=' && i + 2 < s.Length)
                    {
                        if (s[i + 1] == '\r' && s[i + 2] == '\n') { i += 2; continue; }
                        if (s[i + 1] == '\n') { i += 1; continue; }
                        int v; if (int.TryParse(s.Substring(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out v)) { o.Add((byte)v); i += 2; continue; }
                    }
                    o.Add((byte)s[i]);
                }
                return o.ToArray();
            }

            // =?charset?B|Q?text?= words in headers.
            static string Decode(string v)
            {
                return Regex.Replace(v, @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=\s*", m =>
                {
                    Encoding enc; try { enc = Encoding.GetEncoding(m.Groups[1].Value); } catch { enc = Encoding.UTF8; }
                    try
                    {
                        if (m.Groups[2].Value.ToUpperInvariant() == "B") return enc.GetString(Convert.FromBase64String(m.Groups[3].Value));
                        return enc.GetString(QP(m.Groups[3].Value.Replace('_', ' ')));
                    }
                    catch { return m.Value; }
                });
            }
        }
    }
}
