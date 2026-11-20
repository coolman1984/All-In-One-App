using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace StickyPro
{
    static class ChatFiles
    {
        public static readonly string[] Extensions = { "md", "xlsx", "docx", "pptx", "pdf", "txt", "csv" };
        public static readonly string[] Names = { "Markdown", "Excel workbook", "Word document", "PowerPoint presentation", "PDF document", "Plain text", "CSV table" };
        const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        const string Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string Types = "http://schemas.openxmlformats.org/package/2006/content-types";
        const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static bool IsName(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 120 || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.StartsWith(".") || value != value.Trim()) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^(CON|NUL|PRN|AUX|COM[1-9]|LPT[1-9])(\.|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
            return Extensions.Contains(Path.GetExtension(value).TrimStart('.'), StringComparer.OrdinalIgnoreCase);
        }

        // The sentence the Files menu adds to the message. It names the format clearly so the agent replies with a
        // ```file:name.ext block (see AgentStore.DocumentPrompt) and the chat shows a Save card.
        public static string Request(string ext)
        {
            int index = Array.IndexOf(Extensions, ext);
            string name = Names[index];
            string article = "AEIOU".IndexOf(char.ToUpperInvariant(name[0])) >= 0 ? "an " : "a ";
            return "Create " + article + name + " file (." + ext + ") with:";
        }

        // Short text for the Save card: what kind of file and how big.
        public static string Describe(string name, string content)
        {
            int index = Array.IndexOf(Extensions, Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
            int lines = 1;
            foreach (char c in content ?? "") if (c == '\n') lines++;
            return (index >= 0 ? Names[index] : "File") + ", " + lines.ToString("N0", Invariant) + (lines == 1 ? " line" : " lines");
        }

        internal static string ReadAttachment(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > 256 * 1024) throw new InvalidDataException("Choose a text file up to 256 KB, or paste the relevant section.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), true))
                {
                    string value = reader.ReadToEnd();
                    if (value.IndexOf((char)0) >= 0) throw new InvalidDataException("Choose a text file for the message.");
                    return Path.GetFileName(path) + "\r\n\r\n" + value;
                }
            }
        }

        public static async void Choose(Control owner, string name, string content, bool sourceFile = false)
        {
            if (!IsName(name)) return;
            string path;
            string ext = Path.GetExtension(name);
            using (var dialog = new SaveFileDialog { FileName = name, DefaultExt = ext.Substring(1), Filter = sourceFile ? "Source file|*.*" : ext.Substring(1).ToUpperInvariant() + " file|*" + ext, AddExtension = !sourceFile })
            {
                if (dialog.ShowDialog(owner.FindForm()) != DialogResult.OK) return;
                path = dialog.FileName;
            }
            // The user may type another known extension (report.docx while the card says .xlsx): write that format,
            // so the file content always matches its extension. Source files are always written as plain text.
            string chosen = Path.GetExtension(path);
            if (sourceFile) ext = ".txt";
            else if (Extensions.Contains(chosen.TrimStart('.'), StringComparer.OrdinalIgnoreCase)) ext = chosen;
            try
            {
                await Task.Run(() => Write(path, ext, content));
                if (App.I != null) App.I.Status("Saved " + Path.GetFileName(path));
            }
            catch (Exception ex) { if (!owner.IsDisposed) MessageBox.Show(owner.FindForm(), ex.Message, "Could not save file"); }
        }

        public static void Write(string path, string extension, string content)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (content.Length > 4000000) throw new InvalidDataException("Split this file into parts smaller than four million characters.");
            string temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    switch (extension.ToLowerInvariant())
                    {
                        case ".md": case ".txt": Text(output, content); break;
                        case ".csv": Csv(output, Md.Parse(content)); break;
                        case ".pdf": Pdf(output, content); break;
                        case ".pptx": Slides(output, Md.Parse(content)); break;
                        case ".docx": case ".xlsx":
                            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
                            {
                                var blocks = Md.Parse(content);
                                if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)) Document(zip, blocks); else Workbook(zip, blocks);
                            }
                            break;
                        default: throw new InvalidDataException("Unknown chat file format.");
                    }
                }
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static void Text(Stream s, string value) { var data = Encoding.UTF8.GetBytes(value); s.Write(data, 0, data.Length); }
        static void Part(ZipArchive zip, string path, string value)
        { using (var s = zip.CreateEntry(path).Open()) Text(s, value); }
        static string Esc(string value) { return SecurityElement.Escape(XmlSafe(value)); }
        // XML cannot hold most control characters (pasted text often has vertical tabs or form feeds).
        // VerifyXmlChars threw on the first one and the whole save failed; drop them instead. Surrogate pairs are kept.
        static string XmlSafe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder b = null;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool ok = System.Xml.XmlConvert.IsXmlChar(c) ||
                    (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]));
                if (ok && char.IsHighSurrogate(c)) { if (b != null) b.Append(c).Append(value[i + 1]); i++; continue; }
                if (!ok) { if (b == null) b = new StringBuilder(value, 0, i, value.Length); continue; }
                if (b != null) b.Append(c);
            }
            return b == null ? value : b.ToString();
        }
        // A CSV cell that starts with = + - @ or a tab runs as a formula when opened in Excel (OWASP "CSV injection").
        // Numbers such as -5 stay numbers; other such text gets a leading apostrophe.
        static string CsvCell(string value)
        {
            decimal number;
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0 &&
                !decimal.TryParse(value, NumberStyles.Number, Invariant, out number)) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        static string Plain(IEnumerable<MdRun> runs) { return string.Concat(runs.Select(r => r.Text)); }
        static IEnumerable<string[]> Rows(IEnumerable<MdBlock> blocks)
        {
            foreach (var b in blocks)
            {
                if (b.Kind == "table") { foreach (var row in b.Rows) yield return row.Select(Plain).ToArray(); }
                else if (b.Runs.Count > 0) yield return Plain(b.Runs).Split('\t');
            }
        }
        static void Csv(Stream output, List<MdBlock> blocks)
        {
            using (var w = new StreamWriter(output, new UTF8Encoding(true), 4096, true))
                foreach (var row in Rows(blocks)) w.WriteLine(string.Join(",", row.Select(CsvCell)));
        }
        static void Manifest(ZipArchive zip, string main, string type, string more)
        {
            Part(zip, "_rels/.rels", "<Relationships xmlns=\"" + PackageRel + "\"><Relationship Id=\"rId1\" Type=\"" + Rel + "officeDocument\" Target=\"" + main + "\"/></Relationships>");
            Part(zip, "[Content_Types].xml", "<Types xmlns=\"" + Types + "\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/" + main + "\" ContentType=\"application/vnd.openxmlformats-officedocument." + type + ".main+xml\"/>" + more + "</Types>");
        }
        static void Document(ZipArchive zip, List<MdBlock> blocks)
        {
            Manifest(zip, "word/document.xml", "wordprocessingml.document", "");
            var b = new StringBuilder("<w:document xmlns:w=\"" + Word + "\"><w:body>");
            foreach (var block in blocks)
            {
                if (block.Kind == "table")
                {
                    b.Append("<w:tbl><w:tblPr><w:tblBorders>");
                    foreach (var edge in new[] { "top", "left", "bottom", "right", "insideH", "insideV" }) b.Append("<w:" + edge + " w:val=\"single\" w:sz=\"4\" w:color=\"CCCCCC\"/>");
                    b.Append("</w:tblBorders></w:tblPr><w:tblGrid>");
                    int columns = block.Rows.Max(row => row.Count);
                    for (int i = 0; i < columns; i++) b.Append("<w:gridCol/>");
                    b.Append("</w:tblGrid>");
                    foreach (var row in block.Rows)
                    {
                        b.Append("<w:tr>");
                        foreach (var cell in row) b.Append("<w:tc><w:p>").Append(Runs(cell, 0)).Append("</w:p></w:tc>");
                        b.Append("</w:tr>");
                    }
                    b.Append("</w:tbl>");
                }
                else
                {
                    b.Append("<w:p>");
                    if (block.Kind == "li") b.Append("<w:r><w:t xml:space=\"preserve\">").Append(Esc(block.Bullet)).Append(" </w:t></w:r>");
                    b.Append(Runs(block.Runs, block.Kind == "h" ? Math.Max(24, 40 - block.Level * 3) : 0)).Append("</w:p>");
                }
            }
            Part(zip, "word/document.xml", b.Append("<w:sectPr/></w:body></w:document>").ToString());
        }
        static string Runs(List<MdRun> runs, int size)
        {
            var s = new StringBuilder();
            foreach (var r in runs)
            {
                s.Append("<w:r><w:rPr>");
                if ((r.Style & 1) != 0) s.Append("<w:b/>");
                if ((r.Style & 2) != 0) s.Append("<w:i/>");
                if (size > 0) s.Append("<w:sz w:val=\"").Append(size).Append("\"/>");
                s.Append("</w:rPr><w:t xml:space=\"preserve\">").Append(Esc(r.Text)).Append("</w:t></w:r>");
            }
            return s.ToString();
        }
        static string Column(int index)
        {
            string s = "";
            for (int n = index + 1; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s;
            return s;
        }
        static void Workbook(ZipArchive zip, List<MdBlock> blocks)
        {
            Manifest(zip, "xl/workbook.xml", "spreadsheetml.sheet", "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            Part(zip, "xl/workbook.xml", "<workbook xmlns=\"" + Sheet + "\" xmlns:r=\"" + Rel.TrimEnd('/') + "\"><sheets><sheet name=\"Chat\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Part(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"" + PackageRel + "\"><Relationship Id=\"rId1\" Type=\"" + Rel + "worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var b = new StringBuilder("<worksheet xmlns=\"" + Sheet + "\"><sheetData>");
            int n = 0;
            foreach (var row in Rows(blocks))
            {
                if (++n > 1048576 || row.Length > 16384) throw new InvalidDataException("The table exceeds Excel's worksheet size.");
                b.Append("<row r=\"").Append(n).Append("\">");
                for (int i = 0; i < row.Length; i++)
                {
                    string value = row[i]; decimal number;
                    if (value.Length > 32767) throw new InvalidDataException("An Excel cell exceeds 32,767 characters.");
                    b.Append("<c r=\"").Append(Column(i)).Append(n).Append("\"");
                    if (decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Invariant, out number) && number.ToString(Invariant) == value && value.Count(char.IsDigit) <= 15)
                        b.Append("><v>").Append(value).Append("</v>");
                    else b.Append(" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(Esc(value)).Append("</t></is>");
                    b.Append("</c>");
                }
                b.Append("</row>");
            }
            Part(zip, "xl/worksheets/sheet1.xml", b.Append("</sheetData></worksheet>").ToString());
        }

        static XDocument Read(ZipArchive zip, string name)
        { using (var stream = zip.GetEntry(name).Open()) return XDocument.Load(stream); }

        static void Slides(Stream output, List<MdBlock> blocks)
        {
            XNamespace p = "http://schemas.openxmlformats.org/presentationml/2006/main", a = "http://schemas.openxmlformats.org/drawingml/2006/main", r = Rel.TrimEnd('/');
            var pages = new List<KeyValuePair<string, List<string>>>();
            foreach (var block in blocks)
            {
                if (block.Kind == "h") pages.Add(new KeyValuePair<string, List<string>>(Plain(block.Runs), new List<string>()));
                else
                {
                    if (pages.Count == 0) pages.Add(new KeyValuePair<string, List<string>>("Chat", new List<string>()));
                    pages[pages.Count - 1].Value.AddRange(Rows(new[] { block }).Select(row => string.Join("   |   ", row)));
                }
            }
            if (pages.Count == 0) throw new InvalidDataException("There is no slide content.");
            using (var template = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChatSlides.pptx"))
            using (var src = new ZipArchive(template, ZipArchiveMode.Read))
            using (var dst = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                const string presentation = "ppt/presentation.xml", rels = "ppt/_rels/presentation.xml.rels", types = "[Content_Types].xml";
                var pres = Read(src, presentation); var relationships = Read(src, rels); var manifest = Read(src, types);
                var sourceSlide = Read(src, "ppt/slides/slide1.xml");
                var slideIds = pres.Descendants(p + "sldIdLst").Single(); slideIds.RemoveNodes();
                relationships.Root.Elements().Where(x => (string)x.Attribute("Type") == Rel + "slide").Remove();
                manifest.Root.Elements().Where(x => (string)x.Attribute("PartName") == "/ppt/slides/slide1.xml").Remove();
                foreach (var entry in src.Entries)
                {
                    if (entry.FullName == presentation || entry.FullName == rels || entry.FullName == types || entry.FullName.StartsWith("ppt/slides/")) continue;
                    using (var from = entry.Open()) using (var to = dst.CreateEntry(entry.FullName).Open()) from.CopyTo(to);
                }
                for (int i = 0; i < pages.Count; i++)
                {
                    string name = "slide" + (i + 1) + ".xml", id = "chatSlide" + (i + 1);
                    var slide = new XDocument(sourceSlide);
                    var bodies = slide.Descendants(p + "txBody").ToArray();
                    bodies[0].Descendants(a + "t").First().Value = XmlSafe(pages[i].Key);
                    var body = bodies[1]; body.Elements(a + "p").Remove();
                    foreach (var line in pages[i].Value.DefaultIfEmpty(""))
                        body.Add(new XElement(a + "p", new XElement(a + "r", new XElement(a + "t", XmlSafe(line)))));
                    Part(dst, "ppt/slides/" + name, slide.ToString(SaveOptions.DisableFormatting));
                    using (var from = src.GetEntry("ppt/slides/_rels/slide1.xml.rels").Open())
                    using (var to = dst.CreateEntry("ppt/slides/_rels/" + name + ".rels").Open()) from.CopyTo(to);
                    slideIds.Add(new XElement(p + "sldId", new XAttribute("id", 256 + i), new XAttribute(r + "id", id)));
                    relationships.Root.Add(new XElement(XName.Get("Relationship", PackageRel), new XAttribute("Id", id), new XAttribute("Type", Rel + "slide"), new XAttribute("Target", "slides/" + name)));
                    manifest.Root.Add(new XElement(XName.Get("Override", Types), new XAttribute("PartName", "/ppt/slides/" + name), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml")));
                }
                Part(dst, presentation, pres.ToString()); Part(dst, rels, relationships.ToString()); Part(dst, types, manifest.ToString());
            }
        }

        // Render one page at a time. GDI+ supplies font fallback for non-Latin scripts.
        static string PdfTextLayer(Graphics g, Font font, StringFormat fmt, RectangleF box, string page)
        {
            var stream = new StringBuilder();
            float height = font.GetHeight(g);
            int line = 0;
            for (int pos = 0; pos < page.Length; line++)
            {
                int chars, lines;
                // One line never holds more than a few hundred characters. Measuring the whole rest of the page for
                // every line made each page cost (lines x page length).
                string remaining = page.Substring(pos, Math.Min(1500, page.Length - pos));
                g.MeasureString(remaining, font, new SizeF(box.Width, height * 1.4f), fmt, out chars, out lines);
                if (chars == 0) break;
                string text = remaining.Substring(0, chars).TrimEnd('\r', '\n');
                pos += chars;
                if (text.Length == 0) continue;
                string unicode = BitConverter.ToString(Encoding.BigEndianUnicode.GetBytes(text)).Replace("-", "");
                var plain = new string(text.Select(c => c >= 32 && c < 127 ? c : '?').ToArray())
                    .Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                float baseline = 842 - (box.Top + height * (line + 0.85f)) * 842 / 1754;
                stream.Append("/Span << /ActualText <FEFF").Append(unicode).Append("> >> BDC\nBT /F1 10 Tf 3 Tr 1 0 0 1 46 ")
                    .Append(baseline.ToString("0.##", Invariant)).Append(" Tm (").Append(plain).Append(") Tj ET\nEMC\n");
            }
            return stream.ToString();
        }
        static void Pdf(Stream output, string source)
        {
            var offsets = new List<long> { 0, 0, 0 }; var ids = new List<int>();
            Action<string> put = s => Text(output, s);
            Action<int> obj = id => { while (offsets.Count <= id) offsets.Add(0); offsets[id] = output.Position; put(id + " 0 obj\n"); };
            put("%PDF-1.4\n");
            string content = string.Join("\n", Md.Parse(source).SelectMany(b => Rows(new[] { b }).Select(row => (b.Kind == "li" ? b.Bullet + " " : "") + string.Join("    |    ", row))));
            using (var bmp = new Bitmap(1240, 1754))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", 21, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fmt = new StringFormat(StringFormatFlags.LineLimit))
            {
                int cursor = 0;
                var box = new RectangleF(95, 100, 1050, 1540);
                do
                {
                    int count, lines;
                    string page = content.Substring(cursor, Math.Min(16000, content.Length - cursor));
                    g.MeasureString(page, font, box.Size, fmt, out count, out lines);
                    if (count == 0 && page.Length != 0) throw new InvalidDataException("Cannot fit the text on a PDF page.");
                    g.Clear(Color.White); g.DrawString(page.Substring(0, count), font, Brushes.Black, box, fmt);
                    cursor += count;
                    int id = 3 + ids.Count * 3; ids.Add(id);
                    obj(id); put("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> /XObject << /Im " + (id + 2) + " 0 R >> >> /Contents " + (id + 1) + " 0 R >>\nendobj\n");
                    string draw = "q 595 0 0 842 0 0 cm /Im Do Q\n" + PdfTextLayer(g, font, fmt, box, page.Substring(0, count));
                    obj(id + 1); put("<< /Length " + draw.Length + " >>\nstream\n" + draw + "endstream\nendobj\n");
                    using (var jpeg = new MemoryStream())
                    {
                        bmp.Save(jpeg, ImageFormat.Jpeg);
                        obj(id + 2); put("<< /Type /XObject /Subtype /Image /Width 1240 /Height 1754 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpeg.Length + " >>\nstream\n");
                        jpeg.Position = 0; jpeg.CopyTo(output); put("\nendstream\nendobj\n");
                    }
                } while (cursor < content.Length);
            }
            obj(1); put("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            obj(2); put("<< /Type /Pages /Count " + ids.Count + " /Kids [" + string.Join(" ", ids.Select(i => i + " 0 R")) + "] >>\nendobj\n");
            long xref = output.Position;
            put("xref\n0 " + offsets.Count + "\n0000000000 65535 f \n");
            foreach (long pos in offsets.Skip(1)) put(pos.ToString("D10", Invariant) + " 00000 n \n");
            put("trailer\n<< /Size " + offsets.Count + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        }
    }
}
