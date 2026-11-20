using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace StickyPro
{
    // Markdown notes shown as a real web page (Edge engine + marked, packed in the exe; works offline):
    // GitHub-style tables, code, quotes, images from the note's folder, front matter as a properties card,
    // [[wiki links]] and #tags as links, tick boxes that save back to the file, headings the outline can jump to.
    class MdWeb : Panel
    {
        readonly WebView2 web;
        bool ready;
        string pending, folder = "";
        public event Action<string> LinkClicked;
        public event Action<int, bool> TaskToggled;
        public event Action<int> HeadingSeen;   // n-th "- [ ]" in the note, new state
        static string marked;

        public static bool Available { get { try { return WebViewHost.Runtime() != null && Script() != null; } catch { return false; } } }

        static string Script()
        {
            if (marked != null) return marked;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("md.marked.js"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) marked = r.ReadToEnd();
            }
            return marked;
        }

        public MdWeb()
        {
            Dock = DockStyle.Fill; BackColor = Ui.Card;
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Ui.Card };
            Controls.Add(web);
            Init();
        }

        async void Init()
        {
            try
            {
                string profile = Path.Combine(Store.Dir, "browser", "reader");
                Directory.CreateDirectory(profile);
                var env = await CoreWebView2Environment.CreateAsync(null, profile, null);
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.NavigationStarting += (s, e) =>
                {
                    string u = e.Uri ?? "";
                    if (u.StartsWith("data:") || u.StartsWith("about:") || u.StartsWith("file:///" + TempName().Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
                    e.Cancel = true;
                    var h = LinkClicked; if (h != null) h(FromPage(u));
                };
                core.NewWindowRequested += (s, e) => { e.Handled = true; var h = LinkClicked; if (h != null) h(FromPage(e.Uri)); };
                core.WebMessageReceived += (s, e) =>
                {
                    try
                    {
                        var o = new JavaScriptSerializer().DeserializeObject(e.WebMessageAsJson) as Dictionary<string, object>;
                        if (o != null && o.ContainsKey("heading")) { var hs = HeadingSeen; if (hs != null) hs(Convert.ToInt32(o["heading"])); }
                        if (o != null && o.ContainsKey("task")) { var h = TaskToggled; if (h != null) h(Convert.ToInt32(o["task"]), Convert.ToBoolean(o["done"])); }
                        if (o != null && o.ContainsKey("hover") && PreviewFor != null)
                        {
                            var pv = PreviewFor(Convert.ToString(o["hover"]));
                            if (pv != null)
                            {
                                var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                                web.CoreWebView2.ExecuteScriptAsync("showPreview(" + js.Serialize(pv.Item1) + "," + js.Serialize(pv.Item2) + "," + Convert.ToInt32(o["x"]) + "," + Convert.ToInt32(o["y"]) + ")");
                            }
                        }
                    }
                    catch { }
                };
                ready = true;
                if (pending != null) { string p = pending; pending = null; Navigate(p); }
            }
            catch (Exception ex) { App.Log("Markdown reader: " + ex.Message); }
        }

        // Links come back as the page made them: wiki:, tag:, https:, or https://note.local/<relative path>.
        static string FromPage(string u)
        {
            if (u == null) return "";
            if (u.StartsWith("https://note.local/", StringComparison.OrdinalIgnoreCase)) return Uri.UnescapeDataString(u.Substring("https://note.local/".Length));
            if (u.StartsWith("wiki:") || u.StartsWith("tag:")) return Uri.UnescapeDataString(u);
            return u;
        }

        static string TempName() { return Path.Combine(Path.GetTempPath(), "AllInOne-md"); }

        public void Show(string text, string path)
        {
            folder = path != null ? Path.GetDirectoryName(path) : "";
            string html = Page(text ?? "");
            if (!ready) { pending = html; return; }
            Navigate(html);
        }

        void Navigate(string html)
        {
            var core = web.CoreWebView2;
            try { core.ClearVirtualHostNameToFolderMapping("note.local"); } catch { }
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                core.SetVirtualHostNameToFolderMapping("note.local", folder, CoreWebView2HostResourceAccessKind.Allow);   // images next to the note
            try { core.ClearVirtualHostNameToFolderMapping("vault.local"); } catch { }
            if (MdVault.Current.Length > 0 && Directory.Exists(MdVault.Current))
                core.SetVirtualHostNameToFolderMapping("vault.local", MdVault.Current, CoreWebView2HostResourceAccessKind.Allow);   // ![[picture]] anywhere in the vault
            if (html.Length < 1500000) { core.NavigateToString(html); return; }
            Directory.CreateDirectory(TempName());   // very long notes: NavigateToString has a 2 MB limit
            string f = Path.Combine(TempName(), "note.html");
            File.WriteAllText(f, html, new UTF8Encoding(false));
            core.Navigate(new Uri(f).AbsoluteUri);
        }

        public void Print() { if (ready) try { web.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); } catch (Exception ex) { App.Log("Markdown print: " + ex.Message); } }
        public async void ExportPdf(string file, Action<bool> done)
        {
            bool ok = false;
            try { if (ready) ok = await web.CoreWebView2.PrintToPdfAsync(file, null); } catch (Exception ex) { App.Log("Markdown PDF: " + ex.Message); }
            done(ok);
        }

        // Re-render without reloading the page (keeps scroll); used while an answer streams in.
        public async void Update(string text, bool toEnd)
        {
            if (!ready) { Show(text, null); return; }
            try { await web.CoreWebView2.ExecuteScriptAsync("render(" + new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(text ?? "") + ");" + (toEnd ? "scrollTo(0,document.body.scrollHeight);" : "")); } catch { }
        }
        public async void ScrollToEnd() { if (ready) try { await web.CoreWebView2.ExecuteScriptAsync("scrollTo(0,document.body.scrollHeight)"); } catch { } }
        // Hover over a [[link]]: the page asks, this returns (title, markdown) for the preview card.
        public Func<string, Tuple<string, string>> PreviewFor;

        public async void ScrollToHeading(int index)
        {
            if (!ready) return;
            try { await web.CoreWebView2.ExecuteScriptAsync("(function(){var h=H()[" + index + "];if(h)h.scrollIntoView({behavior:'smooth',block:'start'});})()"); } catch { }
        }

        static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        static string Page(string text)
        {
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string bg = Hex(Ui.Card), fg = Hex(Ui.Text), muted = Hex(Ui.Muted), line = Hex(Ui.Line), accent = Hex(Ui.Accent);
            string code = Ui.Dark ? "#16181d" : "#f5f6f8", quote = Ui.Dark ? "#1f2229" : "#f7f7f9", hover = Hex(Ui.HoverBg);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'><base href='https://note.local/'><style>")
              .Append(":root{color-scheme:").Append(Ui.Dark ? "dark" : "light").Append("}")
              .Append("body{margin:0;background:").Append(bg).Append(";color:").Append(fg).Append(";font:15px/1.65 'Segoe UI Variable Text','Segoe UI',sans-serif}")
              .Append("main{max-width:860px;margin:0 auto;padding:28px 40px 80px}")
              .Append("h1,h2,h3,h4{font-family:'Segoe UI Variable Display','Segoe UI',sans-serif;font-weight:600;line-height:1.25;margin:1.6em 0 .55em;scroll-margin-top:16px}")
              .Append("h1{font-size:2em;margin-top:.2em}h2{font-size:1.5em;padding-bottom:.3em;border-bottom:1px solid ").Append(line).Append("}h3{font-size:1.22em}h4{font-size:1.05em}")
              .Append("a{color:").Append(accent).Append(";text-decoration:none}a:hover{text-decoration:underline}")
              .Append("a.wiki{border-bottom:1px dashed ").Append(accent).Append("}a.wiki:hover{text-decoration:none;background:").Append(hover).Append("}")
              .Append("a.tag{font-size:.88em;padding:1px 8px;border-radius:10px;background:").Append(hover).Append(";color:").Append(accent).Append("}")
              .Append("code{font:13px 'Cascadia Mono',Consolas,monospace;background:").Append(code).Append(";padding:2px 5px;border-radius:5px}")
              .Append("pre{background:").Append(code).Append(";padding:14px 16px;border-radius:10px;overflow:auto;border:1px solid ").Append(line).Append("}pre code{padding:0;background:none}")
              .Append("blockquote{margin:1em 0;padding:.6em 1em;border-left:4px solid ").Append(accent).Append(";background:").Append(quote).Append(";border-radius:0 8px 8px 0;color:").Append(muted).Append("}")
              .Append("table{border-collapse:collapse;margin:1em 0;display:block;overflow:auto}th,td{border:1px solid ").Append(line).Append(";padding:7px 12px;text-align:left}th{background:").Append(quote).Append(";font-weight:600}")
              .Append("tr:nth-child(even) td{background:").Append(Ui.Dark ? "#191b20" : "#fafafb").Append("}")
              .Append("img{max-width:100%;border-radius:8px}hr{border:0;border-top:1px solid ").Append(line).Append(";margin:2em 0}")
              .Append("li{margin:.2em 0}li.task{list-style:none;margin-left:-1.4em}li.task input{margin-right:.55em;transform:scale(1.15);cursor:pointer;accent-color:").Append(accent).Append("}")
              .Append("li.task.done{color:").Append(muted).Append(";text-decoration:line-through}")
              .Append(".props{border:1px solid ").Append(line).Append(";border-radius:10px;padding:10px 14px;margin-bottom:18px;font-size:.9em}")
              .Append(".props div{display:flex;gap:12px;padding:2px 0}.props b{min-width:110px;color:").Append(muted).Append(";font-weight:500}")
              .Append("mark{background:").Append(Ui.Dark ? "#6b5a00" : "#fff1a8").Append(";color:inherit;padding:0 2px;border-radius:3px}")
              .Append("blockquote.callout{border-left-color:").Append(accent).Append(";color:").Append(fg).Append("}.ctitle{font-weight:600;margin-bottom:4px;cursor:pointer}")
              .Append("blockquote.callout.folded>*:not(.ctitle){display:none}.c-warning,.c-caution,.c-attention{border-left-color:#e3a008!important}.c-danger,.c-error,.c-failure,.c-fail,.c-bug{border-left-color:#e5484d!important}")
              .Append(".c-tip,.c-hint,.c-success,.c-check,.c-done{border-left-color:#2fa36b!important}")
              .Append("h1,h2,h3,h4,h5,h6{position:relative}.fold{position:absolute;left:-1.1em;opacity:0;cursor:pointer;color:").Append(muted).Append(";font-size:.8em}h1:hover .fold,h2:hover .fold,h3:hover .fold,h4:hover .fold,.closed .fold{opacity:1}")
              .Append("#pop{position:fixed;display:none;max-height:260px;overflow:auto;background:").Append(bg).Append(";border:1px solid ").Append(line).Append(";border-radius:10px;padding:10px 14px;box-shadow:0 8px 24px rgba(0,0,0,.18);font-size:13px;z-index:9}")
              .Append(".ptitle{font-weight:600;margin-bottom:6px;color:").Append(accent).Append("}")
              .Append("</style></head><body><main id='doc'></main><script>").Append(Script()).Append("</script><script>")
              .Append("const SRC=").Append(js.Serialize(text)).Append(";")
              .Append(@"
const esc=s=>String(s).replace(/[&<>""]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','""':'&quot;'}[c]));
marked.use({gfm:true,breaks:false,extensions:[
 {name:'wiki',level:'inline',start(s){const i=s.indexOf('[[');return i<0?undefined:i},
  tokenizer(s){const r=/^\[\[([^\]|#]+)(#[^\]|]*)?(?:\|([^\]]+))?\]\]/.exec(s);if(r)return{type:'wiki',raw:r[0],target:r[1].trim()+(r[2]||''),label:(r[3]||(r[1]+(r[2]?' > '+r[2].slice(1):''))).trim()}},
  renderer(t){return '<a class=""wiki"" href=""wiki:'+encodeURIComponent(t.target)+'"">'+esc(t.label)+'</a>'}},
 {name:'mark',level:'inline',start(s){const i=s.indexOf('==');return i<0?undefined:i},
  tokenizer(s){const r=/^==([^=\n]+)==/.exec(s);if(r)return{type:'mark',raw:r[0],text:r[1]}},
  renderer(t){return '<mark>'+esc(t.text)+'</mark>'}},
 {name:'tag',level:'inline',start(s){const m=/(^|[\s(])#[A-Za-z][\w\-\/]*/.exec(s);return m?m.index+m[1].length:undefined},
  tokenizer(s){const r=/^#([A-Za-z][\w\-\/]*)/.exec(s);if(r)return{type:'tag',raw:r[0],tag:r[1]}},
  renderer(t){return '<a class=""tag"" href=""tag:'+encodeURIComponent(t.tag)+'"">#'+esc(t.tag)+'</a>'}}]});
const ICONS={note:'✎',info:'ℹ',tip:'💡',hint:'💡',important:'❗',success:'✔',check:'✔',done:'✔',question:'?',help:'?',faq:'?',warning:'⚠',caution:'⚠',attention:'⚠',danger:'⛔',error:'⛔',bug:'🐞',example:'≡',quote:'❝',abstract:'≡',summary:'≡',tldr:'≡',todo:'☐',failure:'✖',fail:'✖',missing:'✖'};
function render(src){
 let body=src.replace(/\r/g,''),props='';
 const fm=body.match(/^---\n([\s\S]*?)\n---\n/);
 if(fm){body=body.slice(fm[0].length);props='<div class=""props"">'+fm[1].split('\n').filter(l=>l.trim()).map(l=>{const i=l.indexOf(':');return i>0?'<div><b>'+esc(l.slice(0,i))+'</b><span>'+esc(l.slice(i+1).trim())+'</span></div>':'<div>'+esc(l)+'</div>'}).join('')+'</div>';}
 const doc=document.getElementById('doc');doc.innerHTML=props+marked.parse(body);
 // callouts: > [!type] Title
 doc.querySelectorAll('blockquote').forEach(q=>{const p=q.querySelector('p');if(!p)return;const m=/^\[!(\w+)\]([+-]?)\s*([^\n]*)/.exec(p.innerHTML);if(!m)return;
  const type=m[1].toLowerCase();p.innerHTML=p.innerHTML.slice(m[0].length).replace(/^\s*<br>/,'');q.classList.add('callout','c-'+type);
  const h=document.createElement('div');h.className='ctitle';const dt=document.createElement('textarea');dt.innerHTML=m[3]||'';h.textContent=(ICONS[type]||'✎')+'  '+(dt.value||type.charAt(0).toUpperCase()+type.slice(1));q.insertBefore(h,q.firstChild);
  if(m[2]==='-'){q.classList.add('folded')}h.onclick=()=>q.classList.toggle('folded');});
 // tasks save back to the file
 let n=0;doc.querySelectorAll('li input[type=checkbox]').forEach(cb=>{const i=n++;const li=cb.closest('li');li.classList.add('task');if(cb.checked)li.classList.add('done');cb.disabled=false;
  cb.addEventListener('change',()=>{li.classList.toggle('done',cb.checked);window.chrome.webview.postMessage({task:i,done:cb.checked});});});
 // headings fold: click the arrow
 doc.querySelectorAll('h1,h2,h3,h4,h5,h6').forEach(h=>{const t=document.createElement('span');t.className='fold';t.textContent='▾';h.insertBefore(t,h.firstChild);
  t.onclick=e=>{e.stopPropagation();const lv=+h.tagName[1],closed=!h.classList.contains('closed');h.classList.toggle('closed',closed);t.textContent=closed?'▸':'▾';
   let x=h.nextElementSibling;while(x&&!(/^H[1-6]$/.test(x.tagName)&&+x.tagName[1]<=lv)){x.style.display=closed?'none':'';x=x.nextElementSibling}}});
 // hover preview of [[links]]
 doc.querySelectorAll('a.wiki').forEach(a=>{a.addEventListener('mouseenter',e=>{window.__hoverEl=a;window.chrome.webview.postMessage({hover:decodeURIComponent(a.getAttribute('href').slice(5)),x:e.clientX,y:e.clientY})});
  a.addEventListener('mouseleave',()=>{window.__hoverEl=null;setTimeout(()=>{if(!window.__hoverEl&&!window.__inPop)hidePreview()},250)})});
}
function showPreview(title,html,x,y){let p=document.getElementById('pop');if(!p){p=document.createElement('div');p.id='pop';document.body.appendChild(p);
  p.onmouseenter=()=>window.__inPop=true;p.onmouseleave=()=>{window.__inPop=false;hidePreview()}}
 p.innerHTML='<div class=""ptitle"">'+esc(title)+'</div>'+marked.parse(html);p.style.display='block';
 const w=Math.min(460,innerWidth-24);p.style.width=w+'px';p.style.left=Math.max(8,Math.min(x+12,innerWidth-w-12))+'px';p.style.top=Math.min(y+16,innerHeight-260)+'px';}
function hidePreview(){const p=document.getElementById('pop');if(p)p.style.display='none'}
function H(){return [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].filter(h=>!h.closest('blockquote'))}
let lastH=-1,hsT=0;addEventListener('scroll',()=>{clearTimeout(hsT);hsT=setTimeout(()=>{const hs=H();let i=0;for(let k=0;k<hs.length;k++){if(hs[k].getBoundingClientRect().top<90)i=k;else break}if(i!==lastH){lastH=i;window.chrome.webview.postMessage({heading:i})}},120)});
render(SRC);
")              .Append("</script></body></html>");
            return sb.ToString();
        }

        // ---------- embeds: ![[note]], ![[note#heading]], ![[picture.png]] (resolved here: the page cannot read files) ----------
        static readonly Regex Embed = new Regex(@"!\[\[([^\]|#]+)(#[^\]|]*)?(?:\|([^\]]*))?\]\]");
        static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp" };
        static readonly Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string Expand(string text, string notePath, int depth = 0)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("![[", StringComparison.Ordinal) < 0 || depth > 1) return text;
            var lines = text.Replace("\r", "").Split('\n'); bool code = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("```")) { code = !code; continue; }
                if (code || lines[i].IndexOf("![[", StringComparison.Ordinal) < 0) continue;
                lines[i] = Embed.Replace(lines[i], m =>
                {
                    string target = m.Groups[1].Value.Trim(), heading = m.Groups[2].Value.TrimStart('#').Trim();
                    if (ImageExt.Contains(Path.GetExtension(target).ToLowerInvariant()))
                    {
                        string file = FindInVault(target);
                        if (file == null) return "*(missing picture: " + target + ")*";
                        string rel = file.Substring(MdVault.Current.TrimEnd('\\').Length).TrimStart('\\');
                        return "![" + target + "](https://vault.local/" + string.Join("/", rel.Split('\\').Select(Uri.EscapeDataString)) + ")";
                    }
                    string p = MdVault.Resolve(target);
                    if (p == null) return "*(no note named " + target + " yet)*";
                    string body = MdVault.TextOf(p) ?? "";
                    body = Regex.Replace(body.Replace("\r", ""), "^---\n[\\s\\S]*?\n---\n", "");
                    if (heading.Length > 0) body = Section(body, heading);
                    var part = body.Split('\n').Take(60).ToList();
                    body = Expand(string.Join("\n", part), p, depth + 1);
                    return "\n> [!quote] " + Path.GetFileNameWithoutExtension(p) + (heading.Length > 0 ? " > " + heading : "") + "\n" + string.Join("\n", body.Split('\n').Select(l => "> " + l)) + "\n";
                });
            }
            return string.Join("\n", lines);
        }

        // The part of a note under one heading (until the next heading of the same or a higher level).
        public static string Section(string body, string heading)
        {
            var lines = body.Split('\n'); int start = -1, level = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var m = Regex.Match(lines[i], @"^(#{1,6})\s+(.+?)\s*$");
                if (!m.Success) continue;
                if (start < 0) { if (string.Equals(m.Groups[2].Value.Trim(), heading, StringComparison.OrdinalIgnoreCase)) { start = i; level = m.Groups[1].Length; } }
                else if (m.Groups[1].Length <= level) return string.Join("\n", lines.Skip(start).Take(i - start));
            }
            return start < 0 ? body : string.Join("\n", lines.Skip(start));
        }

        static string FindInVault(string name)
        {
            string vault = MdVault.Current;
            if (vault.Length == 0) return null;
            string hit;
            lock (found) if (found.TryGetValue(vault + "|" + name, out hit) && File.Exists(hit)) return hit;
            try { hit = Directory.EnumerateFiles(vault, Path.GetFileName(name), SearchOption.AllDirectories).FirstOrDefault(); } catch { hit = null; }
            if (hit != null) lock (found) found[vault + "|" + name] = hit;
            return hit;
        }

        // The n-th task box ("- [ ]" / "* [x]" outside code) set to done or open; returns the new text.
        public static string SetTask(string text, int index, bool done)
        {
            int n = 0; bool code = false;
            var lines = (text ?? "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("```")) { code = !code; continue; }
                if (code) continue;
                var m = Regex.Match(lines[i], @"^(\s*(?:[-*+]|\d+[.)])\s+\[)( |x|X)(\])");
                if (!m.Success) continue;
                if (n++ == index) { lines[i] = m.Groups[1].Value + (done ? "x" : " ") + m.Groups[3].Value + lines[i].Substring(m.Length); break; }
            }
            return string.Join("\n", lines);
        }
    }
}
