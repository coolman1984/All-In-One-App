using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StickyPro
{
    // ===================================================================================================
    // Markdown for agent answers, drawn like a chat web app: headings, bold / italic / inline code, bullet and
    // numbered lists (nested), quotes, horizontal lines, tables (header, borders, wrapped cells, column widths
    // that fit the pane) and clickable links (web links and file paths). Layout is measured once per width and
    // cached; drawing skips what is off screen. Code blocks (```) are handled by the chat view itself.
    // ===================================================================================================

    class MdRun { public string Text = ""; public int Style; public string Link; }   // Style: 1 bold, 2 italic, 4 code

    class MdPiece { public string Text; public Font Font; public int X, W; public int Style; public string Link; }
    class MdLine { public List<MdPiece> Pieces = new List<MdPiece>(); public int Y, H; }

    class MdBlock
    {
        public string Kind = "p";   // p, h, li, quote, hr, table
        public int Level, Indent;
        public string Bullet = "";
        public List<MdRun> Runs = new List<MdRun>();
        public List<List<List<MdRun>>> Rows;      // table: rows -> cells -> runs (row 0 = header)
        public List<int> Align;                   // table: -1 left, 0 center, 1 right
        // layout
        public int Y, H;
        public List<MdLine> Lines = new List<MdLine>();
        public List<int> ColX, ColW, RowY, RowH;
        public List<List<List<MdLine>>> CellLines;
    }

    class MdLink { public Rectangle R; public string Url; }

    static class Md
    {
        // ---------- parsing ----------
        static bool TableRow(string text) { return text.IndexOf('|') >= 0; }
        static readonly Regex TableSep = new Regex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$");
        static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
        static readonly Regex Rule = new Regex(@"^\s*([-*_])\s*(\1\s*){2,}$");
        static readonly Regex Quote = new Regex(@"^\s*>\s?(.*)$");
        static readonly Regex Item = new Regex(@"^(\s*)([-*+•]|\d{1,3}[.)])\s+(.*)$");

        public static bool LooksRich(string text)
        {
            return text.IndexOf('*') >= 0 || text.IndexOf('#') >= 0 || text.IndexOf('|') >= 0 || text.IndexOf('`') >= 0 || text.IndexOf('[') >= 0 || text.Contains("http") || Regex.IsMatch(text, @"(^|\n)\s*([-*>]|\d+[.)])\s");
        }

        public static List<MdBlock> Parse(string text)
        {
            var blocks = new List<MdBlock>();
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            MdBlock para = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.Trim().Length == 0) { para = null; continue; }
                // table: a row of pipes followed by a --- separator row
                if (TableRow(l) && i + 1 < lines.Length && TableRow(lines[i + 1]) && TableSep.IsMatch(lines[i + 1]))
                {
                    para = null;
                    var b = new MdBlock { Kind = "table", Rows = new List<List<List<MdRun>>>(), Align = new List<int>() };
                    b.Rows.Add(Cells(l).Select(c => Inline(c, 0)).ToList());
                    foreach (var c in Cells(lines[i + 1])) { string t = c.Trim(); b.Align.Add(t.StartsWith(":") && t.EndsWith(":") ? 0 : t.EndsWith(":") ? 1 : -1); }
                    i += 2;
                    while (i < lines.Length && TableRow(lines[i])) { b.Rows.Add(Cells(lines[i]).Select(c => Inline(c, 0)).ToList()); i++; }
                    i--;
                    blocks.Add(b);
                    continue;
                }
                Match m;
                if ((m = Heading.Match(l)).Success) { para = null; blocks.Add(new MdBlock { Kind = "h", Level = m.Groups[1].Length, Runs = Inline(m.Groups[2].Value, 1) }); continue; }
                if (Rule.IsMatch(l)) { para = null; blocks.Add(new MdBlock { Kind = "hr" }); continue; }
                if ((m = Quote.Match(l)).Success)
                {
                    if (para != null && para.Kind == "quote") { AddText(para, m.Groups[1].Value); continue; }
                    para = new MdBlock { Kind = "quote" }; AddText(para, m.Groups[1].Value); blocks.Add(para); continue;
                }
                if ((m = Item.Match(l)).Success)
                {
                    int indent = m.Groups[1].Value.Replace("\t", "    ").Length / 2;
                    string bullet = m.Groups[2].Value;
                    string body = m.Groups[3].Value;
                    string bl = char.IsDigit(bullet[0]) ? bullet.TrimEnd(')').TrimEnd('.') + "." : "•";
                    var task = Regex.Match(body, @"^\[( |x|X)\]\s+");   // - [ ] task / - [x] done
                    if (task.Success) { bl = task.Groups[1].Value == " " ? "☐" : "☑"; body = body.Substring(task.Length); }
                    para = new MdBlock { Kind = "li", Indent = Math.Min(4, indent), Bullet = bl };
                    AddText(para, body);
                    blocks.Add(para);
                    continue;
                }
                // a plain line: continues the paragraph / list item / quote above, else starts a paragraph
                if (para != null && (para.Kind == "p" || para.Kind == "li" || para.Kind == "quote")) { para.Runs.Add(new MdRun { Text = "\n" }); AddText(para, l.Trim()); continue; }
                para = new MdBlock { Kind = "p" }; AddText(para, l.Trim()); blocks.Add(para);
            }
            return blocks;
        }

        static void AddText(MdBlock b, string s) { b.Runs.AddRange(Inline(s, 0)); }

        static List<string> Cells(string row)
        {
            string t = row.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|") && !t.EndsWith("\\|")) t = t.Substring(0, t.Length - 1);
            var cells = new List<string>(); var sb = new StringBuilder();
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == '\\' && i + 1 < t.Length && t[i + 1] == '|') { sb.Append('|'); i++; continue; }
                if (t[i] == '|') { cells.Add(sb.ToString().Trim()); sb.Length = 0; continue; }
                sb.Append(t[i]);
            }
            cells.Add(sb.ToString().Trim());
            return cells;
        }

        // **bold**, __bold__, *italic*, _italic_, `code`, [text](link), bare http(s) links, \ escapes.
        public static List<MdRun> Inline(string s, int style)
        {
            var runs = new List<MdRun>();
            var sb = new StringBuilder();
            Action flush = () => { if (sb.Length > 0) { runs.Add(new MdRun { Text = sb.ToString(), Style = style }); sb.Length = 0; } };
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length && "*_`[]()#|\\>-".IndexOf(s[i + 1]) >= 0) { sb.Append(s[i + 1]); i += 2; continue; }
                if (c == '`')
                {
                    int e = s.IndexOf('`', i + 1);
                    if (e > i) { flush(); runs.Add(new MdRun { Text = s.Substring(i + 1, e - i - 1), Style = style | 4 }); i = e + 1; continue; }
                }
                if ((c == '*' || c == '_') && i + 1 < s.Length && s[i + 1] == c)
                {
                    string mark = new string(c, 2);
                    int e = s.IndexOf(mark, i + 2);
                    if (e > i + 2) { flush(); runs.AddRange(Inline(s.Substring(i + 2, e - i - 2), style | 1)); i = e + 2; continue; }
                }
                if ((c == '*' || c == '_') && i + 1 < s.Length && !char.IsWhiteSpace(s[i + 1]) && (i == 0 || !char.IsLetterOrDigit(s[i - 1])))
                {
                    int e = s.IndexOf(c, i + 1);
                    if (e > i + 1 && !char.IsWhiteSpace(s[e - 1]) && (e + 1 >= s.Length || !char.IsLetterOrDigit(s[e + 1]))) { flush(); runs.AddRange(Inline(s.Substring(i + 1, e - i - 1), style | 2)); i = e + 1; continue; }
                }
                // [[note]] / [[note|text]]: a link to another note (wiki style).
                if (c == '[' && i + 1 < s.Length && s[i + 1] == '[')
                {
                    int e = s.IndexOf("]]", i + 2);
                    if (e > i + 2)
                    {
                        flush();
                        string inner = s.Substring(i + 2, e - i - 2); int bar = inner.IndexOf('|');
                        string target = bar >= 0 ? inner.Substring(0, bar) : inner, label = bar >= 0 ? inner.Substring(bar + 1) : inner;
                        runs.Add(new MdRun { Text = label, Style = style, Link = "wiki:" + target.Trim() });
                        i = e + 2; continue;
                    }
                }
                // ![alt](image): a link that opens the picture.
                if (c == '!' && i + 1 < s.Length && s[i + 1] == '[')
                {
                    int close = s.IndexOf("](", i + 2), end = close > 0 ? FindClose(s, close + 2) : -1;
                    if (close > i && end > close) { flush(); runs.Add(new MdRun { Text = "[picture: " + s.Substring(i + 2, close - i - 2) + "]", Style = style | 2, Link = s.Substring(close + 2, end - close - 2).Trim() }); i = end + 1; continue; }
                }
                // #tag (not a heading: those are handled per line).
                if (c == '#' && (i == 0 || char.IsWhiteSpace(s[i - 1])) && i + 1 < s.Length && char.IsLetter(s[i + 1]))
                {
                    int e = i + 1; while (e < s.Length && (char.IsLetterOrDigit(s[e]) || s[e] == '_' || s[e] == '-' || s[e] == '/')) e++;
                    flush(); runs.Add(new MdRun { Text = s.Substring(i, e - i), Style = style | 1, Link = "tag:" + s.Substring(i + 1, e - i - 1) }); i = e; continue;
                }
                if (c == '[')
                {
                    int close = s.IndexOf("](", i + 1);
                    int end = close > 0 ? FindClose(s, close + 2) : -1;
                    if (close > i && end > close)
                    {
                        flush();
                        string label = s.Substring(i + 1, close - i - 1), url = s.Substring(close + 2, end - close - 2).Trim().Trim('<', '>');
                        foreach (var r in Inline(label, style)) { r.Link = url; runs.Add(r); }
                        i = end + 1; continue;
                    }
                }
                if ((c == 'h' || c == 'H') && (i == 0 || char.IsWhiteSpace(s[i - 1]) || s[i - 1] == '(') && Regex.IsMatch(s.Substring(i, Math.Min(8, s.Length - i)), "^https?://", RegexOptions.IgnoreCase))
                {
                    int e = i; while (e < s.Length && !char.IsWhiteSpace(s[e]) && s[e] != ')' && s[e] != '>') e++;
                    string url = s.Substring(i, e - i).TrimEnd('.', ',', ';', ':');
                    flush(); runs.Add(new MdRun { Text = url, Style = style, Link = url }); i += url.Length; continue;
                }
                sb.Append(c); i++;
            }
            flush();
            return runs;
        }

        static int FindClose(string s, int from)
        {
            int depth = 0;
            for (int i = from; i < s.Length; i++) { if (s[i] == '(') depth++; else if (s[i] == ')') { if (depth == 0) return i; depth--; } }
            return -1;
        }

        // ---------- fonts ----------
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static Font baseFont;
        // Text size factor for the next Layout / Draw (the Agents chat sets its own size, everything else uses 1).
        public static float Zoom = 1f;
        public static Font FontFor(int style, float scale)
        {
            if (baseFont != Ui.Base) { fonts.Clear(); baseFont = Ui.Base; }
            string key = style + "|" + scale + "|" + Zoom;
            Font font;
            if (fonts.TryGetValue(key, out font)) return font;
            if ((style & 4) != 0) font = Zoom == 1f ? ChatView.Mono : new Font(ChatView.Mono.FontFamily, ChatView.Mono.Size * Zoom);
            else
            {
                var fs = FontStyle.Regular; if ((style & 1) != 0) fs |= FontStyle.Bold; if ((style & 2) != 0) fs |= FontStyle.Italic;
                font = new Font(Ui.Base.FontFamily, Ui.Base.Size * scale * Zoom, fs);
            }
            fonts[key] = font;
            return font;
        }
        static float HeadScale(int level) { return level <= 1 ? 1.45f : level == 2 ? 1.25f : level == 3 ? 1.1f : 1f; }

        // ---------- measuring ----------
        static readonly Dictionary<string, int> widths = new Dictionary<string, int>();
        const TextFormatFlags Measure = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        static int W(Graphics g, string s, Font f)
        {
            string key = f.GetHashCode() + "\u0001" + s;
            int w;
            if (widths.TryGetValue(key, out w)) return w;
            w = s.Length == 0 ? 0 : TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue), Measure).Width;
            if (widths.Count > 60000) widths.Clear();
            widths[key] = w;
            return w;
        }

        // Words of the runs placed on lines no wider than width.
        static List<MdLine> Wrap(Graphics g, List<MdRun> runs, float scale, int width, int forcedStyle)
        {
            var lines = new List<MdLine>(); var line = new MdLine(); int x = 0; int lineH = 0;
            Action newLine = () => { line.H = Math.Max(lineH, (int)(Ui.Base.Height * scale)); lines.Add(line); line = new MdLine(); x = 0; lineH = 0; };
            foreach (var r in runs)
            {
                if (r.Text == "\n") { newLine(); continue; }
                int st = r.Style | forcedStyle;
                var font = FontFor(st, scale);
                foreach (var word in Regex.Split(r.Text, @"(?<=\s)"))
                {
                    if (word.Length == 0) continue;
                    int w = W(g, word, font);
                    if (x > 0 && x + w - (word.EndsWith(" ") ? W(g, " ", font) : 0) > width) newLine();
                    if (x == 0 && word.Trim().Length == 0) continue;   // no spaces at the start of a line
                    // a single word wider than the line: cut it in pieces
                    string rest = word;
                    while (w > width && rest.Length > 1)
                    {
                        int n = Math.Max(1, (int)(rest.Length * (double)(width - x) / w));
                        while (n > 1 && W(g, rest.Substring(0, n), font) > width - x) n--;
                        string part = rest.Substring(0, n);
                        line.Pieces.Add(new MdPiece { Text = part, Font = font, X = x, W = W(g, part, font), Style = st, Link = r.Link });
                        lineH = Math.Max(lineH, font.Height);
                        newLine(); rest = rest.Substring(n); w = W(g, rest, font);
                    }
                    line.Pieces.Add(new MdPiece { Text = rest, Font = font, X = x, W = w, Style = st, Link = r.Link });
                    lineH = Math.Max(lineH, font.Height + ((st & 4) != 0 ? 2 : 0));
                    x += w;
                }
            }
            if (line.Pieces.Count > 0 || lines.Count == 0) newLine();
            int y = 0; foreach (var l in lines) { l.Y = y; y += l.H; }
            return lines;
        }

        public static int Layout(Graphics g, List<MdBlock> blocks, int width)
        {
            int y = 0, gap = Ui.S(6);
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                var b = blocks[bi];
                if (bi > 0) y += b.Kind == "h" ? Ui.S(10) : b.Kind == "li" && blocks[bi - 1].Kind == "li" ? Ui.S(2) : gap;
                b.Y = y;
                switch (b.Kind)
                {
                    case "h": b.Lines = Wrap(g, b.Runs, HeadScale(b.Level), width, 1); b.H = Sum(b.Lines) + (b.Level <= 2 ? Ui.S(6) : 0); break;
                    case "hr": b.H = Ui.S(10); break;
                    case "li": { int ind = Ui.S(18) + b.Indent * Ui.S(18); b.Lines = Wrap(g, b.Runs, 1f, Math.Max(40, width - ind), 0); b.H = Sum(b.Lines); break; }
                    case "quote": b.Lines = Wrap(g, b.Runs, 1f, Math.Max(40, width - Ui.S(16)), 2); b.H = Sum(b.Lines) + Ui.S(4); break;
                    case "table": LayoutTable(g, b, width); break;
                    default: b.Lines = Wrap(g, b.Runs, 1f, width, 0); b.H = Sum(b.Lines); break;
                }
                y += b.H;
            }
            return y;
        }
        static int Sum(List<MdLine> l) { return l.Count == 0 ? 0 : l[l.Count - 1].Y + l[l.Count - 1].H; }

        // Column widths: each column's natural (one-line) width; when the table is wider than the pane, wide columns
        // give up space first, so short columns stay readable and long text wraps.
        static void LayoutTable(Graphics g, MdBlock b, int width)
        {
            int cols = b.Rows.Max(r => r.Count), pad = Ui.S(8);
            var natural = new int[cols];
            for (int r = 0; r < b.Rows.Count; r++)
                for (int c = 0; c < b.Rows[r].Count; c++)
                {
                    int w = 0;
                    foreach (var run in b.Rows[r][c]) w += W(g, run.Text, FontFor(run.Style | (r == 0 ? 1 : 0), 1f));
                    natural[c] = Math.Max(natural[c], w + pad * 2);
                }
            int total = natural.Sum(), avail = Math.Max(cols * Ui.S(40), width);
            var colW = natural.ToArray();
            if (total > avail)
            {
                int min = Ui.S(70);
                int fixedSum = colW.Where(w => w <= min).Sum();
                int flex = colW.Where(w => w > min).Sum();
                int room = Math.Max(cols * Ui.S(30), avail - fixedSum);
                for (int c = 0; c < cols; c++) if (colW[c] > min) colW[c] = Math.Max(min, (int)((long)colW[c] * room / Math.Max(1, flex)));
                int over = colW.Sum() - avail;
                for (int c = cols - 1; c >= 0 && over > 0; c--) { int take = Math.Min(over, Math.Max(0, colW[c] - Ui.S(30))); colW[c] -= take; over -= take; }
            }
            b.ColW = colW.ToList(); b.ColX = new List<int>(); int x = 0; foreach (var w in colW) { b.ColX.Add(x); x += w; }
            b.CellLines = new List<List<List<MdLine>>>(); b.RowY = new List<int>(); b.RowH = new List<int>();
            int y = 0;
            for (int r = 0; r < b.Rows.Count; r++)
            {
                var row = new List<List<MdLine>>(); int h = 0;
                for (int c = 0; c < cols; c++)
                {
                    var runs = c < b.Rows[r].Count ? b.Rows[r][c] : new List<MdRun>();
                    var lines = Wrap(g, runs, 1f, Math.Max(20, colW[c] - pad * 2), r == 0 ? 1 : 0);
                    row.Add(lines); h = Math.Max(h, Sum(lines));
                }
                h += Ui.S(10);
                b.CellLines.Add(row); b.RowY.Add(y); b.RowH.Add(h); y += h;
            }
            b.H = y + 1;
        }

        // ---------- drawing ----------
        public static void Draw(Graphics g, List<MdBlock> blocks, int x, int y, int width, Rectangle clip, Color text, List<MdLink> links)
        {
            foreach (var b in blocks)
            {
                int top = y + b.Y;
                if (top > clip.Bottom) break;
                if (top + b.H < clip.Top) continue;
                switch (b.Kind)
                {
                    case "h":
                        DrawLines(g, b.Lines, x, top, text, links);
                        if (b.Level <= 2) using (var p = new Pen(Ui.Line)) g.DrawLine(p, x, top + b.H - Ui.S(3), x + width, top + b.H - Ui.S(3));
                        break;
                    case "hr": using (var p = new Pen(Ui.Line)) g.DrawLine(p, x, top + b.H / 2, x + width, top + b.H / 2); break;
                    case "li":
                        {
                            int ind = Ui.S(18) + b.Indent * Ui.S(18);
                            var bf = FontFor(0, 1f);
                            string bullet = b.Bullet == "•" ? (b.Indent % 2 == 0 ? "•" : "◦") : b.Bullet;
                            if (b.Bullet == "☑") { DrawLines(g, b.Lines, x + ind, top, Ui.Muted, links); TextRenderer.DrawText(g, bullet, bf, new Rectangle(x + ind - Ui.S(22), top, Ui.S(20), bf.Height), Ui.Accent, TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding); break; }
                            TextRenderer.DrawText(g, bullet, bf, new Rectangle(x + ind - Ui.S(18) - Ui.S(4), top, Ui.S(20), bf.Height), b.Bullet == "•" ? Ui.Accent : Ui.Muted, TextFormatFlags.Right | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                            DrawLines(g, b.Lines, x + ind, top, text, links);
                            break;
                        }
                    case "quote":
                        using (var br = new SolidBrush(Color.FromArgb(160, Ui.Accent))) g.FillRectangle(br, x + Ui.S(2), top, Ui.S(3), b.H - Ui.S(2));
                        DrawLines(g, b.Lines, x + Ui.S(14), top + Ui.S(2), Ui.Muted, links);
                        break;
                    case "table": DrawTable(g, b, x, top, text, links); break;
                    default: DrawLines(g, b.Lines, x, top, text, links); break;
                }
            }
        }

        static void DrawLines(Graphics g, List<MdLine> lines, int x, int y, Color text, List<MdLink> links)
        {
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            foreach (var l in lines)
                foreach (var p in l.Pieces)
                {
                    int py = y + l.Y + (l.H - p.Font.Height) / 2;
                    var r = new Rectangle(x + p.X, py, p.W + ((p.Style & 2) != 0 ? Ui.S(4) : 1), p.Font.Height);   // italic letters lean past their width
                    Color c = text;
                    if ((p.Style & 4) != 0)
                    {
                        var bg = new Rectangle(r.X - 1, r.Y, r.Width + 2, r.Height);
                        using (var b = new SolidBrush(Ui.Dark ? Color.FromArgb(48, 52, 62) : Color.FromArgb(236, 239, 245))) g.FillRectangle(b, bg);
                        c = Ui.Dark ? Color.FromArgb(255, 170, 120) : Color.FromArgb(180, 60, 40);
                    }
                    if (p.Link != null) { c = Ui.Accent; links.Add(new MdLink { R = r, Url = p.Link }); }
                    TextRenderer.DrawText(g, p.Text, p.Font, r, c, flags);
                    if (p.Link != null) using (var pen = new Pen(Color.FromArgb(140, Ui.Accent))) g.DrawLine(pen, r.X, r.Bottom - 1, r.X + p.W, r.Bottom - 1);
                }
        }

        static void DrawTable(Graphics g, MdBlock b, int x, int y, Color text, List<MdLink> links)
        {
            int w = b.ColW.Sum(), pad = Ui.S(8);
            var outer = new Rectangle(x, y, w, b.H - 1);
            using (var bg = new SolidBrush(Ui.Card)) g.FillRectangle(bg, outer);
            for (int r = 0; r < b.RowY.Count; r++)
            {
                var rr = new Rectangle(x, y + b.RowY[r], w, b.RowH[r]);
                if (r == 0) using (var hb = new SolidBrush(Ui.Dark ? Color.FromArgb(44, 48, 58) : Color.FromArgb(238, 241, 247))) g.FillRectangle(hb, rr);
                else if (r % 2 == 0) using (var zb = new SolidBrush(Ui.Dark ? Color.FromArgb(14, 255, 255, 255) : Color.FromArgb(250, 251, 253))) g.FillRectangle(zb, rr);
                for (int c = 0; c < b.ColW.Count; c++)
                {
                    var lines = b.CellLines[r][c];
                    int lw = lines.Count == 0 ? 0 : lines.Max(l => l.Pieces.Count == 0 ? 0 : l.Pieces[l.Pieces.Count - 1].X + l.Pieces[l.Pieces.Count - 1].W);
                    int al = c < b.Align.Count ? b.Align[c] : -1;
                    int cx = x + b.ColX[c] + pad + (al == 1 ? b.ColW[c] - pad * 2 - lw : al == 0 ? (b.ColW[c] - pad * 2 - lw) / 2 : 0);
                    DrawLines(g, lines, cx, rr.Y + Ui.S(5), text, links);
                }
            }
            using (var p = new Pen(Ui.Line))
            {
                g.DrawRectangle(p, outer);
                for (int r = 1; r < b.RowY.Count; r++) g.DrawLine(p, x, y + b.RowY[r], x + w, y + b.RowY[r]);
                for (int c = 1; c < b.ColX.Count; c++) g.DrawLine(p, x + b.ColX[c], y, x + b.ColX[c], y + b.H - 1);
            }
        }

        // Opens a link: web address in the browser, file or folder path in its app / File Explorer.
        public static void Open(string url)
        {
            try
            {
                string u = url.Trim();
                if (Regex.IsMatch(u, "^(https?|mailto):", RegexOptions.IgnoreCase)) { System.Diagnostics.Process.Start(u); return; }
                u = Uri.UnescapeDataString(u.Replace("file:///", "").TrimStart('/'));
                u = Regex.Replace(u, @"#L?\d+.*$", "").Replace('/', '\\');
                if (System.IO.Directory.Exists(u)) { System.Diagnostics.Process.Start("explorer.exe", "\"" + u + "\""); return; }
                if (System.IO.File.Exists(u)) { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + u + "\""); return; }
                App.I.Status("Not found: " + u);
            }
            catch (Exception ex) { App.I.Status("Could not open the link: " + ex.Message); }
        }
    }
}
