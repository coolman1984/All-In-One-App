using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace StickyPro
{
    // Graph of the vault: every note is a dot, every [[link]] (or relative .md link) a line. Drawn with a small force
    // layout in the Edge engine (no libraries). Click a dot to open the note; the current note is highlighted; the box
    // filters by name; "Around this note" shows only its neighbours (two steps).
    class MdGraph : Form
    {
        readonly WebView2 web;
        readonly Action<string> open;
        readonly string current;
        static readonly Regex Wiki = new Regex(@"\[\[([^\]|#]+)(?:#[^\]|]*)?(?:\|[^\]]*)?\]\]");
        static readonly Regex MdLink = new Regex(@"\]\(([^)\s]+?\.md)(?:#[^)]*)?\)", RegexOptions.IgnoreCase);

        public MdGraph(string currentNote, Action<string> openNote)
        {
            current = currentNote; open = openNote;
            Text = "Note graph"; Icon = App.AppIcon; StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            ClientSize = Ui.S(1000, 700); MinimumSize = Ui.S(500, 360); BackColor = Ui.Card;
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Ui.Card };
            Controls.Add(web);
            Ui.DarkTitle(this);
            Shown += async delegate { await Load0(); };
        }

        async Task Load0()
        {
            try
            {
                string json = await Task.Run(() => Build(current));
                string profile = Path.Combine(Store.Dir, "browser", "reader");
                Directory.CreateDirectory(profile);
                var env = await CoreWebView2Environment.CreateAsync(null, profile, null);
                await web.EnsureCoreWebView2Async(env);
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.WebMessageReceived += (s, e) =>
                {
                    string p = null; try { p = e.TryGetWebMessageAsString(); } catch { }
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) { open(p); Close(); }
                };
                web.CoreWebView2.NavigateToString(Page(json));
            }
            catch (Exception ex) { App.Log("Note graph: " + ex.Message); Controls.Add(new Label { Dock = DockStyle.Fill, Text = "The graph needs the Edge engine: " + ex.Message, TextAlign = ContentAlignment.MiddleCenter }); }
        }

        // nodes: [{p: path, n: name, d: degree}], edges: [[i, j]]
        static string Build(string current)
        {
            var files = MdVault.Files.Take(1500).ToList();
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++) { index[files[i]] = i; string n = Path.GetFileNameWithoutExtension(files[i]); if (!byName.ContainsKey(n)) byName[n] = i; }
            var edges = new HashSet<long>();
            var degree = new int[files.Count];
            for (int i = 0; i < files.Count; i++)
            {
                string text = MdVault.TextOf(files[i]);
                if (string.IsNullOrEmpty(text)) continue;
                var targets = new List<int>();
                foreach (Match m in Wiki.Matches(text)) { int j; if (byName.TryGetValue(Path.GetFileNameWithoutExtension(m.Groups[1].Value.Trim()), out j)) targets.Add(j); }
                foreach (Match m in MdLink.Matches(text))
                {
                    try { string full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(files[i]), Uri.UnescapeDataString(m.Groups[1].Value).Replace('/', '\\'))); int j; if (index.TryGetValue(full, out j)) targets.Add(j); } catch { }
                }
                foreach (int j in targets)
                {
                    if (j == i) continue;
                    long key = Math.Min(i, j) * 100000L + Math.Max(i, j);
                    if (edges.Add(key)) { degree[i]++; degree[j]++; }
                }
            }
            var nodes = files.Select((f, i) => new Dictionary<string, object> { { "p", f }, { "n", Path.GetFileNameWithoutExtension(f) }, { "d", degree[i] }, { "c", string.Equals(f, current, StringComparison.OrdinalIgnoreCase) } }).ToList();
            var list = edges.Select(k => new[] { (int)(k / 100000L), (int)(k % 100000L) }).ToList();
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new Dictionary<string, object> { { "nodes", nodes }, { "edges", list } });
        }

        static string Hex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        static string Page(string json)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;height:100%;overflow:hidden;background:").Append(Hex(Ui.Card))
              .Append(";color:").Append(Hex(Ui.Text)).Append(";font:13px 'Segoe UI',sans-serif}#bar{position:fixed;top:10px;left:10px;display:flex;gap:8px;align-items:center;z-index:2}")
              .Append("input{padding:6px 10px;border-radius:8px;border:1px solid ").Append(Hex(Ui.Line)).Append(";background:").Append(Hex(Ui.Field)).Append(";color:inherit;width:220px}")
              .Append("label{color:").Append(Hex(Ui.Muted)).Append("}#info{position:fixed;bottom:8px;left:12px;color:").Append(Hex(Ui.Muted)).Append("}canvas{display:block}</style></head><body>")
              .Append("<div id='bar'><input id='q' placeholder='Find a note'><label><input type='checkbox' id='near' style='width:auto'> Around this note</label></div><div id='info'></div><canvas id='c'></canvas><script>")
              .Append("const G=").Append(json).Append(";const ACC='").Append(Hex(Ui.Accent)).Append("',MUT='").Append(Hex(Ui.Muted)).Append("',LINE='").Append(Hex(Ui.Line)).Append("',TXT='").Append(Hex(Ui.Text)).Append("';")
              .Append(@"
const cv=document.getElementById('c'),cx=cv.getContext('2d');let W,H,scale=1,ox=0,oy=0;
function size(){W=cv.width=innerWidth*devicePixelRatio;H=cv.height=innerHeight*devicePixelRatio;cv.style.width=innerWidth+'px';cv.style.height=innerHeight+'px';}
size();addEventListener('resize',size);
const N=G.nodes,E=G.edges,adj=N.map(()=>[]);E.forEach(([a,b])=>{adj[a].push(b);adj[b].push(a)});
N.forEach((n,i)=>{const a=i*2.399,r=30*Math.sqrt(i+1);n.x=Math.cos(a)*r;n.y=Math.sin(a)*r;n.vx=0;n.vy=0;n.show=true});
const cur=N.findIndex(n=>n.c);
function visible(){const q=document.getElementById('q').value.toLowerCase(),near=document.getElementById('near').checked&&cur>=0;
 let keep=null;if(near){keep=new Set([cur]);adj[cur].forEach(j=>{keep.add(j);adj[j].forEach(k=>keep.add(k))})}
 N.forEach((n,i)=>n.show=(!keep||keep.has(i))&&(!q||n.n.toLowerCase().includes(q)||n.c));
 document.getElementById('info').textContent=N.filter(n=>n.show).length+' notes · '+E.length+' links · click a note to open it · drag to move · wheel to zoom';}
document.getElementById('q').oninput=visible;document.getElementById('near').onchange=visible;visible();
let t=0;function step(){const k=Math.max(.02,1-t/400);t++;
 for(let i=0;i<N.length;i++){const a=N[i];if(!a.show)continue;for(let j=i+1;j<N.length;j++){const b=N[j];if(!b.show)continue;let dx=a.x-b.x,dy=a.y-b.y,d2=dx*dx+dy*dy+.01;if(d2>90000)continue;const f=900/d2*k;a.vx+=dx*f;a.vy+=dy*f;b.vx-=dx*f;b.vy-=dy*f}}
 E.forEach(([i,j])=>{const a=N[i],b=N[j];if(!a.show||!b.show)return;const dx=b.x-a.x,dy=b.y-a.y,d=Math.sqrt(dx*dx+dy*dy)||1,f=(d-70)*.004*k;a.vx+=dx/d*f*d;a.vy+=dy/d*f*d;b.vx-=dx/d*f*d;b.vy-=dy/d*f*d});
 N.forEach(n=>{n.vx-=n.x*.0009;n.vy-=n.y*.0009;n.x+=n.vx*.5;n.y+=n.vy*.5;n.vx*=.6;n.vy*=.6});}
function tx(x){return (x*scale+ox)*devicePixelRatio+W/2}function ty(y){return (y*scale+oy)*devicePixelRatio+H/2}
let hover=-1;function draw(){if(t<600)step();cx.clearRect(0,0,W,H);cx.lineWidth=devicePixelRatio;cx.strokeStyle=LINE;cx.beginPath();
 E.forEach(([i,j])=>{const a=N[i],b=N[j];if(!a.show||!b.show)return;cx.moveTo(tx(a.x),ty(a.y));cx.lineTo(tx(b.x),ty(b.y))});cx.stroke();
 const hi=hover>=0?new Set([hover,...adj[hover]]):null;
 N.forEach((n,i)=>{if(!n.show)return;const r=(3+Math.min(9,Math.sqrt(n.d)*2))*devicePixelRatio*Math.max(.6,Math.min(1.6,scale));cx.beginPath();cx.arc(tx(n.x),ty(n.y),r,0,7);
  cx.fillStyle=n.c||i===hover?ACC:hi&&hi.has(i)?ACC:MUT;cx.globalAlpha=hi&&!hi.has(i)?.35:1;cx.fill();cx.globalAlpha=1;
  if(scale>.7||n.c||i===hover||n.d>4){cx.fillStyle=TXT;cx.font=(12*devicePixelRatio)+'px Segoe UI';cx.fillText(n.n,tx(n.x)+r+3*devicePixelRatio,ty(n.y)+4*devicePixelRatio)}});
 requestAnimationFrame(draw)}draw();
function at(e){const x=((e.clientX*devicePixelRatio-W/2)/devicePixelRatio-ox)/scale,y=((e.clientY*devicePixelRatio-H/2)/devicePixelRatio-oy)/scale;
 let best=-1,bd=144/scale/scale;N.forEach((n,i)=>{if(!n.show)return;const d=(n.x-x)**2+(n.y-y)**2;if(d<bd){bd=d;best=i}});return best}
let drag=null,moved=false,last=null;
cv.onmousedown=e=>{drag=at(e);last=[e.clientX,e.clientY];moved=false};
cv.onmousemove=e=>{if(last&&e.buttons){const dx=e.clientX-last[0],dy=e.clientY-last[1];if(Math.abs(dx)+Math.abs(dy)>2)moved=true;
  if(drag>=0){N[drag].x+=dx/scale;N[drag].y+=dy/scale}else{ox+=dx;oy+=dy}last=[e.clientX,e.clientY]}else{hover=at(e);cv.style.cursor=hover>=0?'pointer':'default'}};
cv.onmouseup=e=>{if(!moved&&drag>=0)window.chrome.webview.postMessage(N[drag].p);drag=null;last=null};
cv.onwheel=e=>{scale*=e.deltaY<0?1.12:1/1.12;scale=Math.max(.15,Math.min(5,scale));e.preventDefault()};
")
              .Append("</script></body></html>");
            return sb.ToString();
        }
    }
}
