using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace StickyPro
{
    // Serializable extraction DTOs shared by OfficeCom (Phase 2) and SQLite Vault (Phase 3).
    class ExtractedTable
    {
        public string Name = "";
        public List<string> Columns = new List<string>();
        public List<List<string>> Rows = new List<List<string>>();
        public int TotalRows, TotalCols;
    }

    class ExtractedSection
    {
        public string Title = "";
        public string Text = "";
        public int Index;
    }

    class ExtractedDoc
    {
        public string Path = "", Kind = "", Error = "";
        public bool IsDrm;
        public long FileSize, ModifiedTicks;
        public List<ExtractedTable> Tables = new List<ExtractedTable>();
        public List<ExtractedSection> Sections = new List<ExtractedSection>();
        public string FullText = "";
    }

    // Live Office COM automation (Excel / Word / PowerPoint tabs inside DocumentsPage)
    // and background read-only bulk COM extraction (Excel / CSV / Word / PowerPoint / PDF, NASCA DRM-safe).
    static class OfficeCom
    {
        static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
        const int ExtractTimeoutMs = 90000;
        const int PdfExtractTimeoutMs = 25000;
        const int MaxFullTextChars = 250000;
        const int RpcCallRejected = unchecked((int)0x80010001);
        const int RpcRetryLater = unchecked((int)0x8001010A);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        // =====================================================================
        // Excel Live COM
        // =====================================================================

        // Fast, non-blocking Excel snapshot for MainForm.PageContext(). Never sleeps or retries.
        public static Dictionary<string, object> ExcelQuickContext(OfficeEdit ed)
        {
            if (ed == null || ed.Kind != "excel" || ed.ComApp == null || ed.ComDoc == null) return null;
            try
            {
                object app = ed.ComApp;
                object wb = ed.ComDoc;
                string activeSheet = "";
                try
                {
                    object act = GetOnce(wb, "ActiveSheet");
                    if (act != null) activeSheet = Convert.ToString(GetOnce(act, "Name"), CultureInfo.InvariantCulture) ?? "";
                }
                catch (Exception) { }

                string selection = "";
                try
                {
                    object sel = GetOnce(app, "Selection");
                    if (sel != null) selection = GetRangeAddressFast(sel);
                }
                catch (Exception) { }

                var sheetNames = new List<string>();
                try
                {
                    object sheets = GetOnce(wb, "Worksheets");
                    int count = Convert.ToInt32(GetOnce(sheets, "Count"), CultureInfo.InvariantCulture);
                    for (int i = 1; i <= count; i++)
                    {
                        object ws = GetOnce(sheets, "Item", i);
                        string nm = Convert.ToString(GetOnce(ws, "Name"), CultureInfo.InvariantCulture) ?? "";
                        if (nm.Length > 0) sheetNames.Add(nm);
                    }
                }
                catch (Exception) { }

                return new Dictionary<string, object>
                {
                    { "excel_active_sheet", activeSheet },
                    { "excel_selection", selection },
                    { "excel_sheets", sheetNames }
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static Dictionary<string, object> ExcelInfo(OfficeEdit ed)
        {
            RequireEditor(ed, "excel");
            object app = ed.ComApp;
            object wb = ed.ComDoc;

            string activeSheet = GetActiveSheetName(wb);
            string selection = GetSelectionAddress(app);
            var sheetsOut = new List<Dictionary<string, object>>();

            object sheets = Get(wb, "Worksheets");
            int count = Convert.ToInt32(Get(sheets, "Count"), CultureInfo.InvariantCulture);
            for (int i = 1; i <= count; i++)
            {
                object ws = Get(sheets, "Item", i);
                string name = Convert.ToString(Get(ws, "Name"), CultureInfo.InvariantCulture) ?? "";
                int rows = 0, cols = 0;
                try
                {
                    object used = Get(ws, "UsedRange");
                    rows = Convert.ToInt32(Get(Get(used, "Rows"), "Count"), CultureInfo.InvariantCulture);
                    cols = Convert.ToInt32(Get(Get(used, "Columns"), "Count"), CultureInfo.InvariantCulture);
                    if (rows == 1 && cols == 1 && Get(used, "Value2") == null) { rows = 0; cols = 0; }
                }
                catch (Exception) { }
                sheetsOut.Add(new Dictionary<string, object>
                {
                    { "name", name },
                    { "rows", rows },
                    { "cols", cols }
                });
            }

            return new Dictionary<string, object>
            {
                { "path", ed.Path ?? "" },
                { "dirty", ed.Dirty },
                { "active_sheet", activeSheet },
                { "selection", selection },
                { "sheets", sheetsOut }
            };
        }

        public static Dictionary<string, object> ExcelRead(OfficeEdit ed, string sheet, string range, int maxRows, int maxCols)
        {
            RequireEditor(ed, "excel");
            if (maxRows <= 0) maxRows = 200;
            if (maxCols <= 0) maxCols = 40;

            object app = ed.ComApp;
            object wb = ed.ComDoc;
            object ws = ResolveWorksheet(wb, sheet);
            string sheetName = Convert.ToString(Get(ws, "Name"), CultureInfo.InvariantCulture) ?? "";
            string activeSheet = GetActiveSheetName(wb);
            string selection = GetSelectionAddress(app);

            bool usedDefault = string.IsNullOrWhiteSpace(range);
            object rng = usedDefault ? Get(ws, "UsedRange") : Get(ws, "Range", range.Trim());
            string rangeAddr = GetRangeAddress(rng);

            // Single bulk COM read of the entire range.
            object raw = Get(rng, "Value2");
            int totalRows = 0, totalCols = 0;
            var rows = new List<List<string>>();
            var headers = new List<string>();

            if (raw != null)
            {
                Array arr = raw as Array;
                if (arr != null && arr.Rank == 2)
                {
                    int r0 = arr.GetLowerBound(0), r1 = arr.GetUpperBound(0);
                    int c0 = arr.GetLowerBound(1), c1 = arr.GetUpperBound(1);
                    totalRows = r1 - r0 + 1;
                    totalCols = c1 - c0 + 1;
                    int takeRows = Math.Min(totalRows, maxRows);
                    int takeCols = Math.Min(totalCols, maxCols);
                    for (int r = 0; r < takeRows; r++)
                    {
                        var row = new List<string>(takeCols);
                        for (int c = 0; c < takeCols; c++)
                            row.Add(FormatCellValue(arr.GetValue(r0 + r, c0 + c)));
                        rows.Add(row);
                    }
                    if (rows.Count > 0) headers = new List<string>(rows[0]);
                }
                else
                {
                    totalRows = 1;
                    totalCols = 1;
                    string s = FormatCellValue(raw);
                    headers.Add(s);
                    rows.Add(new List<string> { s });
                }
            }
            else if (!usedDefault)
            {
                // Explicit empty cell or empty range requested: report its dimensions with empty strings.
                try
                {
                    totalRows = Convert.ToInt32(Get(Get(rng, "Rows"), "Count"), CultureInfo.InvariantCulture);
                    totalCols = Convert.ToInt32(Get(Get(rng, "Columns"), "Count"), CultureInfo.InvariantCulture);
                    int takeRows = Math.Min(totalRows, maxRows);
                    int takeCols = Math.Min(totalCols, maxCols);
                    for (int r = 0; r < takeRows; r++)
                    {
                        var row = new List<string>(takeCols);
                        for (int c = 0; c < takeCols; c++) row.Add("");
                        rows.Add(row);
                    }
                    if (rows.Count > 0) headers = new List<string>(rows[0]);
                }
                catch (Exception) { }
            }

            return new Dictionary<string, object>
            {
                { "sheet", sheetName },
                { "range", rangeAddr },
                { "active_sheet", activeSheet },
                { "selection", selection },
                { "total_rows", totalRows },
                { "total_cols", totalCols },
                { "headers", headers },
                { "rows", rows }
            };
        }

        public static Dictionary<string, object> ExcelWrite(OfficeEdit ed, string sheet, string startCell, List<List<object>> values, bool save)
        {
            RequireEditor(ed, "excel");
            if (values == null || values.Count == 0) throw new ArgumentException("values must contain at least one row.");

            int numRows = values.Count;
            int numCols = 0;
            for (int r = 0; r < numRows; r++)
            {
                if (values[r] != null && values[r].Count > numCols) numCols = values[r].Count;
            }
            if (numCols <= 0) throw new ArgumentException("values must contain at least one cell.");

            object wb = ed.ComDoc;
            object ws = ResolveWorksheet(wb, sheet);
            string sheetName = Convert.ToString(Get(ws, "Name"), CultureInfo.InvariantCulture) ?? "";

            string cellAddr = string.IsNullOrWhiteSpace(startCell) ? "A1" : startCell.Trim();
            object startRange = Get(ws, "Range", cellAddr);
            object origin = Get(Get(startRange, "Cells"), "Item", 1, 1);
            object target = (numRows == 1 && numCols == 1) ? origin : Get(origin, "Resize", numRows, numCols);

            bool hasFormula = false;
            object[,] grid = new object[numRows, numCols];
            for (int r = 0; r < numRows; r++)
            {
                var row = values[r];
                for (int c = 0; c < numCols; c++)
                {
                    object raw = (row != null && c < row.Count) ? row[c] : null;
                    object norm = NormalizeExcelInput(raw);
                    string strVal = norm as string;
                    if (strVal != null && strVal.Length > 1 && strVal[0] == '=') hasFormula = true;
                    grid[r, c] = norm;
                }
            }

            // One bulk assignment to Excel COM (Formula handles both values and "=..." formulas; Value2 is fastest for pure data).
            object payload = (numRows == 1 && numCols == 1) ? grid[0, 0] : (object)grid;
            if (hasFormula) Set(target, "Formula", payload);
            else Set(target, "Value2", payload);

            string writtenRange = GetRangeAddress(target);
            bool saved = false;
            if (save) saved = ed.Save();

            var res = new Dictionary<string, object>
            {
                { "sheet", sheetName },
                { "range", writtenRange },
                { "rows_written", numRows },
                { "cols_written", numCols },
                { "saved", saved }
            };
            if (save && !saved && !string.IsNullOrEmpty(ed.Error)) res["save_error"] = ed.Error;
            return res;
        }

        public static Dictionary<string, object> ExcelFormula(OfficeEdit ed, string sheet, string cell, string formula, bool save)
        {
            RequireEditor(ed, "excel");
            if (string.IsNullOrWhiteSpace(cell)) throw new ArgumentException("cell is required.");
            if (string.IsNullOrWhiteSpace(formula)) throw new ArgumentException("formula is required.");

            string f = formula.Trim();
            if (!f.StartsWith("=")) f = "=" + f;

            object wb = ed.ComDoc;
            object ws = ResolveWorksheet(wb, sheet);
            string sheetName = Convert.ToString(Get(ws, "Name"), CultureInfo.InvariantCulture) ?? "";
            object rng = Get(ws, "Range", cell.Trim());

            Set(rng, "Formula", f);
            try { Call(rng, "Calculate"); } catch (Exception) { }

            object val = Get(rng, "Value2");
            string text = "";
            try { text = Convert.ToString(Get(rng, "Text"), CultureInfo.InvariantCulture) ?? ""; } catch (Exception) { }

            bool saved = false;
            if (save) saved = ed.Save();

            var res = new Dictionary<string, object>
            {
                { "sheet", sheetName },
                { "cell", GetRangeAddress(rng) },
                { "formula", f },
                { "value", FormatCellValue(val) },
                { "text", text },
                { "saved", saved }
            };
            if (save && !saved && !string.IsNullOrEmpty(ed.Error)) res["save_error"] = ed.Error;
            return res;
        }

        public static Dictionary<string, object> ExcelSheetAdd(OfficeEdit ed, string name, bool activate)
        {
            RequireEditor(ed, "excel");
            object wb = ed.ComDoc;
            object prevActive = null;
            if (!activate)
            {
                try { prevActive = Get(wb, "ActiveSheet"); } catch (Exception) { }
            }

            object sheets = Get(wb, "Worksheets");
            int count = Convert.ToInt32(Get(sheets, "Count"), CultureInfo.InvariantCulture);
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i <= count; i++)
            {
                object s = Get(sheets, "Item", i);
                string nm = Convert.ToString(Get(s, "Name"), CultureInfo.InvariantCulture) ?? "";
                if (nm.Length > 0) existing.Add(nm);
            }

            object lastSheet = Get(sheets, "Item", count);
            object newSheet = Call(sheets, "Add", Type.Missing, lastSheet);

            string safeName = SanitizeSheetName(name, existing);
            if (!string.IsNullOrEmpty(safeName))
            {
                Set(newSheet, "Name", safeName);
            }
            string finalName = Convert.ToString(Get(newSheet, "Name"), CultureInfo.InvariantCulture) ?? "";

            if (activate)
            {
                try { Call(newSheet, "Activate"); } catch (Exception) { }
            }
            else if (prevActive != null)
            {
                try { Call(prevActive, "Activate"); } catch (Exception) { }
            }

            var allNames = new List<string>();
            int newCount = Convert.ToInt32(Get(sheets, "Count"), CultureInfo.InvariantCulture);
            for (int i = 1; i <= newCount; i++)
            {
                object s = Get(sheets, "Item", i);
                allNames.Add(Convert.ToString(Get(s, "Name"), CultureInfo.InvariantCulture) ?? "");
            }

            return new Dictionary<string, object>
            {
                { "sheet", finalName },
                { "sheets", allNames }
            };
        }

        public static Dictionary<string, object> ExcelFormat(OfficeEdit ed, string sheet, string range, bool? bold, string bgHex, string fontHex, string numberFormat, bool autoFit)
        {
            RequireEditor(ed, "excel");
            object wb = ed.ComDoc;
            object ws = ResolveWorksheet(wb, sheet);
            string sheetName = Convert.ToString(Get(ws, "Name"), CultureInfo.InvariantCulture) ?? "";
            object rng = string.IsNullOrWhiteSpace(range) ? Get(ws, "UsedRange") : Get(ws, "Range", range.Trim());

            if (bold.HasValue)
            {
                Set(Get(rng, "Font"), "Bold", bold.Value);
            }
            if (!string.IsNullOrWhiteSpace(bgHex))
            {
                string trimmed = bgHex.Trim().ToLowerInvariant();
                if (trimmed == "none" || trimmed == "clear")
                {
                    Set(Get(rng, "Interior"), "ColorIndex", -4142 /* xlColorIndexNone */);
                }
                else
                {
                    Set(Get(rng, "Interior"), "Color", ParseHexToOleColor(bgHex));
                }
            }
            if (!string.IsNullOrWhiteSpace(fontHex))
            {
                Set(Get(rng, "Font"), "Color", ParseHexToOleColor(fontHex));
            }
            if (!string.IsNullOrWhiteSpace(numberFormat))
            {
                Set(rng, "NumberFormat", numberFormat);
            }
            if (autoFit)
            {
                Call(Get(rng, "Columns"), "AutoFit");
            }

            return new Dictionary<string, object>
            {
                { "sheet", sheetName },
                { "range", GetRangeAddress(rng) },
                { "formatted", true }
            };
        }

        // =====================================================================
        // Word Live COM
        // =====================================================================

        public static Dictionary<string, object> WordRead(OfficeEdit ed, int maxChars)
        {
            RequireEditor(ed, "word");
            if (maxChars <= 0) maxChars = 30000;
            object wdoc = ed.ComDoc;

            string raw = Get(Get(wdoc, "Content"), "Text") as string ?? "";
            string clean = CleanWordBodyText(raw);
            int totalChars = clean.Length;
            bool truncated = totalChars > maxChars;
            string shown = truncated ? clean.Substring(0, maxChars) : clean;

            var tables = ExtractWordTables(wdoc, false);
            var tablesOut = new List<Dictionary<string, object>>();
            foreach (var t in tables)
            {
                var allRows = new List<List<string>>();
                if (t.Columns.Count > 0) allRows.Add(t.Columns);
                allRows.AddRange(t.Rows);
                tablesOut.Add(new Dictionary<string, object>
                {
                    { "name", t.Name },
                    { "rows", allRows }
                });
            }

            return new Dictionary<string, object>
            {
                { "path", ed.Path ?? "" },
                { "dirty", ed.Dirty },
                { "total_chars", totalChars },
                { "truncated", truncated },
                { "text", shown },
                { "table_count", tablesOut.Count },
                { "tables", tablesOut }
            };
        }

        public static Dictionary<string, object> WordWrite(OfficeEdit ed, string mode, string text, string findText, List<List<string>> tableRows, bool save)
        {
            RequireEditor(ed, "word");
            string m = string.IsNullOrWhiteSpace(mode) ? "append" : mode.Trim().ToLowerInvariant();
            object wdoc = ed.ComDoc;
            bool replacedAny = false;
            int rowsInserted = 0, colsInserted = 0;

            if (m == "append")
            {
                if (text == null) throw new ArgumentException("text is required for mode=append.");
                object content = Get(wdoc, "Content");
                string existing = (Get(content, "Text") as string ?? "").Trim('\r', '\n', '\a');
                string norm = text.Replace("\r\n", "\r").Replace("\n", "\r");
                if (existing.Length > 0 && !norm.StartsWith("\r")) norm = "\r" + norm;
                Call(content, "InsertAfter", norm);
            }
            else if (m == "replace")
            {
                if (string.IsNullOrEmpty(findText)) throw new ArgumentException("find is required for mode=replace.");
                string rep = text ?? "";
                if (findText.Length > 255 || rep.Length > 255)
                    throw new ArgumentException("Word Find/Replace text cannot exceed 255 characters.");
                object content = Get(wdoc, "Content");
                object find = Get(content, "Find");
                Call(find, "ClearFormatting");
                Call(Get(find, "Replacement"), "ClearFormatting");
                // Execute(FindText, MatchCase, MatchWholeWord, MatchWildcards, MatchSoundsLike, MatchAllWordForms, Forward, Wrap, Format, ReplaceWith, Replace)
                object ok = Call(find, "Execute",
                    findText, false, false, false, false, false, true, 1 /* wdFindContinue */, false, rep, 2 /* wdReplaceAll */);
                replacedAny = ok is bool && (bool)ok;
            }
            else if (m == "insert_table")
            {
                if (tableRows == null || tableRows.Count == 0) throw new ArgumentException("rows is required for mode=insert_table.");
                rowsInserted = tableRows.Count;
                for (int r = 0; r < rowsInserted; r++)
                    if (tableRows[r] != null && tableRows[r].Count > colsInserted) colsInserted = tableRows[r].Count;
                if (colsInserted <= 0) throw new ArgumentException("rows must contain at least one cell.");

                object content = Get(wdoc, "Content");
                string existing = (Get(content, "Text") as string ?? "").Trim('\r', '\n', '\a');
                if (existing.Length > 0) Call(content, "InsertAfter", "\r");
                Call(content, "Collapse", 0 /* wdCollapseEnd */);

                object tbl = Call(Get(wdoc, "Tables"), "Add", content, rowsInserted, colsInserted);
                try { Set(Get(tbl, "Borders"), "Enable", 1); } catch (Exception) { }
                for (int r = 0; r < rowsInserted; r++)
                {
                    var row = tableRows[r];
                    for (int c = 0; c < colsInserted; c++)
                    {
                        string val = (row != null && c < row.Count && row[c] != null) ? row[c] : "";
                        object cell = Call(tbl, "Cell", r + 1, c + 1);
                        object cr = Get(cell, "Range");
                        Set(cr, "Text", val);
                        if (r == 0)
                        {
                            try { Set(Get(cr, "Font"), "Bold", -1); } catch (Exception) { }
                        }
                    }
                }
            }
            else
            {
                throw new ArgumentException("Unknown Word write mode: " + mode + ". Use append, replace, or insert_table.");
            }

            bool saved = false;
            if (save) saved = ed.Save();

            var res = new Dictionary<string, object>
            {
                { "path", ed.Path ?? "" },
                { "mode", m },
                { "saved", saved }
            };
            if (m == "replace") res["replaced"] = replacedAny;
            if (m == "insert_table") { res["rows_inserted"] = rowsInserted; res["cols_inserted"] = colsInserted; }
            if (save && !saved && !string.IsNullOrEmpty(ed.Error)) res["save_error"] = ed.Error;
            return res;
        }

        // =====================================================================
        // PowerPoint Live COM
        // =====================================================================

        public static Dictionary<string, object> PptRead(OfficeEdit ed)
        {
            RequireEditor(ed, "powerpoint");
            object pres = ed.ComDoc;
            var slidesOut = ReadPptSlides(pres);
            return new Dictionary<string, object>
            {
                { "path", ed.Path ?? "" },
                { "dirty", ed.Dirty },
                { "slide_count", slidesOut.Count },
                { "slides", slidesOut }
            };
        }

        public static Dictionary<string, object> PptWrite(OfficeEdit ed, int slideIndex, string title, string body, bool addSlide, bool save)
        {
            RequireEditor(ed, "powerpoint");
            object pres = ed.ComDoc;
            object slides = Get(pres, "Slides");
            int count = Convert.ToInt32(Get(slides, "Count"), CultureInfo.InvariantCulture);

            object slide;
            int targetIdx;
            bool added = addSlide || count == 0;
            if (added)
            {
                targetIdx = (slideIndex > 0 && slideIndex <= count + 1) ? slideIndex : (count + 1);
                slide = Call(slides, "Add", targetIdx, 2 /* ppLayoutText */);
            }
            else
            {
                targetIdx = slideIndex > 0 ? slideIndex : 1;
                if (targetIdx < 1 || targetIdx > count)
                    throw new ArgumentOutOfRangeException("slideIndex", "Slide index " + targetIdx + " is out of range (1.." + count + ").");
                slide = Call(slides, "Item", targetIdx);
            }

            object shapes = Get(slide, "Shapes");
            object titleShape = null;

            if (title != null)
            {
                if (IsMsoTrue(Get(shapes, "HasTitle")))
                {
                    titleShape = Get(shapes, "Title");
                    Set(Get(Get(titleShape, "TextFrame"), "TextRange"), "Text", NormalizePptNewlines(title));
                }
                else
                {
                    titleShape = Call(shapes, "AddTextbox", 1 /* msoTextOrientationHorizontal */, 36f, 24f, 648f, 60f);
                    Set(Get(Get(titleShape, "TextFrame"), "TextRange"), "Text", NormalizePptNewlines(title));
                }
            }
            else if (IsMsoTrue(Get(shapes, "HasTitle")))
            {
                try { titleShape = Get(shapes, "Title"); } catch (Exception) { }
            }

            if (body != null)
            {
                object bodyShape = FindPptBodyShape(shapes, titleShape);
                if (bodyShape == null)
                {
                    bodyShape = Call(shapes, "AddTextbox", 1 /* msoTextOrientationHorizontal */, 48f, 110f, 624f, 360f);
                }
                Set(Get(Get(bodyShape, "TextFrame"), "TextRange"), "Text", NormalizePptNewlines(body));
            }

            bool saved = false;
            if (save) saved = ed.Save();

            int newCount = Convert.ToInt32(Get(slides, "Count"), CultureInfo.InvariantCulture);
            var res = new Dictionary<string, object>
            {
                { "path", ed.Path ?? "" },
                { "slide", targetIdx },
                { "slide_count", newCount },
                { "added", added },
                { "saved", saved }
            };
            if (save && !saved && !string.IsNullOrEmpty(ed.Error)) res["save_error"] = ed.Error;
            return res;
        }

        // =====================================================================
        // Background Read-Only Bulk COM Extractor + Live Editor Extractor
        // =====================================================================

        public static ExtractedDoc ExtractFromLiveEditor(OfficeEdit ed, int maxRowsPerSheet = 25000)
        {
            var doc = new ExtractedDoc();
            if (ed == null || ed.ComDoc == null)
            {
                doc.Error = "No active Office editor.";
                return doc;
            }
            if (maxRowsPerSheet <= 0) maxRowsPerSheet = 25000;
            doc.Path = ed.Path ?? "";
            doc.Kind = ed.Kind ?? "";
            FillFileMeta(doc, doc.Path);
            try
            {
                if (ed.Kind == "excel") ExtractExcelWorkbook(ed.ComDoc, doc, maxRowsPerSheet, false);
                else if (ed.Kind == "word") ExtractWordDocument(ed.ComDoc, doc, false);
                else if (ed.Kind == "powerpoint") ExtractPptPresentation(ed.ComDoc, doc);
                else doc.Error = "Unsupported live editor kind: " + ed.Kind;
            }
            catch (Exception ex)
            {
                doc.Error = UnwrapError(ex);
            }
            return doc;
        }

        public static ExtractedDoc ExtractFile(string path, int maxRowsPerSheet = 25000)
        {
            var doc = new ExtractedDoc { Path = path ?? "" };
            if (maxRowsPerSheet <= 0) maxRowsPerSheet = 25000;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                doc.Error = "File not found: " + (path ?? "");
                return doc;
            }

            FillFileMeta(doc, path);
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // Plain non-DRM CSV: fast direct RFC-4180 parser with zero COM overhead.
            if (ext == ".csv" && !doc.IsDrm)
            {
                doc.Kind = "csv";
                try { ExtractCsvDirect(path, doc, maxRowsPerSheet); }
                catch (Exception ex) { doc.Error = UnwrapError(ex); }
                return doc;
            }

            string mode = null;
            switch (ext)
            {
                case ".xlsx": case ".xlsm": case ".xlsb": case ".xls": case ".csv":
                    mode = "excel"; doc.Kind = "excel"; break;
                case ".docx": case ".docm": case ".doc": case ".rtf":
                    mode = "word"; doc.Kind = "word"; break;
                case ".pptx": case ".pptm": case ".ppt": case ".ppsx":
                    mode = "powerpoint"; doc.Kind = "powerpoint"; break;
                case ".pdf":
                    mode = "pdf"; doc.Kind = "pdf"; break;
                default:
                    doc.Kind = ext.TrimStart('.');
                    doc.Error = "Unsupported file type for Office extraction: " + ext;
                    return doc;
            }

            // Always run hidden COM work on a dedicated STA thread with en-US culture.
            int privatePid = 0;
            int timeoutMs = mode == "pdf" ? PdfExtractTimeoutMs : ExtractTimeoutMs;
            var thread = new Thread(delegate()
            {
                Thread.CurrentThread.CurrentCulture = EnUs;
                Thread.CurrentThread.CurrentUICulture = EnUs;
                try
                {
                    if (mode == "excel")
                    {
                        ExtractExcelHiddenCore(path, doc, maxRowsPerSheet, pid => Interlocked.Exchange(ref privatePid, pid));
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                    else if (mode == "word" || mode == "pdf")
                    {
                        ExtractWordOrPdfHiddenCore(path, doc, mode == "pdf", pid => Interlocked.Exchange(ref privatePid, pid));
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                    }
                    else if (mode == "powerpoint")
                    {
                        bool quitPpt = ExtractPptHiddenCore(path, doc);
                        if (quitPpt)
                        {
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                        }
                    }
                }
                catch (Exception ex)
                {
                    doc.Error = UnwrapError(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Name = "OfficeCom.Extract";
            thread.Start();

            if (!thread.Join(timeoutMs))
            {
                doc.Error = "Extraction timed out after " + (timeoutMs / 1000) + " s.";
                int pid = Interlocked.CompareExchange(ref privatePid, 0, 0);
                if (pid > 0)
                {
                    try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
                    // Give the worker thread a brief moment to run its finally block (e.g. registry restore).
                    thread.Join(2000);
                }
                // The timed-out worker may still be filling `doc`: never hand that half-built, still-changing object
                // to the caller (it would be ingested while being modified). Return a clean error-only copy.
                var failed = new ExtractedDoc { Path = doc.Path, Kind = doc.Kind, IsDrm = doc.IsDrm, Error = "Extraction timed out after " + (timeoutMs / 1000) + " s." };
                failed.FileSize = doc.FileSize; failed.ModifiedTicks = doc.ModifiedTicks;
                return failed;
            }

            if (mode == "pdf" && !doc.IsDrm && string.IsNullOrWhiteSpace(doc.FullText) && doc.Tables.Count == 0)
            {
                TryExtractPdfTextFallback(path, doc);
            }

            return doc;
        }

        // ---------- Hidden Excel Extraction ----------
        static void ExtractExcelHiddenCore(string path, ExtractedDoc doc, int maxRowsPerSheet, Action<int> reportPid)
        {
            object app = null, workbooks = null, wb = null;
            try
            {
                app = Create("Excel.Application");
                try
                {
                    IntPtr hwnd = new IntPtr(Convert.ToInt64(Get(app, "Hwnd"), CultureInfo.InvariantCulture));
                    if (hwnd != IntPtr.Zero)
                    {
                        uint upid;
                        GetWindowThreadProcessId(hwnd, out upid);
                        if (upid != 0 && reportPid != null) reportPid((int)upid);
                    }
                }
                catch (Exception) { }

                Set(app, "Visible", false);
                Set(app, "DisplayAlerts", false);
                TrySet(app, "ScreenUpdating", false);
                TrySet(app, "AskToUpdateLinks", false);
                TrySet(app, "EnableEvents", false);
                TrySet(app, "AutomationSecurity", 3 /* msoAutomationSecurityForceDisable */);

                workbooks = Get(app, "Workbooks");
                object m = Type.Missing;
                // Open(Filename, UpdateLinks=0, ReadOnly=true, Format, Password, WriteResPassword, IgnoreReadOnlyRecommended=true, Origin, Delimiter, Editable, Notify=false, Converter, AddToMru=false)
                wb = Call(workbooks, "Open", path, 0, true, m, m, m, true, m, m, m, false, m, false);

                ExtractExcelWorkbook(wb, doc, maxRowsPerSheet, true);
            }
            finally
            {
                try { if (wb != null) Call(wb, "Close", false); } catch (Exception) { }
                FinalRelease(ref wb);
                FinalRelease(ref workbooks);
                try { if (app != null) Call(app, "Quit"); } catch (Exception) { }
                FinalRelease(ref app);
            }
        }

        static void ExtractExcelWorkbook(object wb, ExtractedDoc doc, int maxRowsPerSheet, bool releaseCom)
        {
            object sheets = null;
            var full = new StringBuilder();
            try
            {
                sheets = Get(wb, "Worksheets");
                int count = Convert.ToInt32(Get(sheets, "Count"), CultureInfo.InvariantCulture);
                for (int i = 1; i <= count; i++)
                {
                    object ws = null, used = null;
                    try
                    {
                        ws = Get(sheets, "Item", i);
                        string name = Convert.ToString(Get(ws, "Name"), CultureInfo.InvariantCulture) ?? ("Sheet" + i);
                        used = Get(ws, "UsedRange");
                        object raw = Get(used, "Value2");

                        var rawRows = new List<List<string>>();
                        int totalRawRows = 0;

                        if (raw != null)
                        {
                            Array arr = raw as Array;
                            if (arr != null && arr.Rank == 2)
                            {
                                int r0 = arr.GetLowerBound(0), r1 = arr.GetUpperBound(0);
                                int c0 = arr.GetLowerBound(1), c1 = arr.GetUpperBound(1);
                                totalRawRows = r1 - r0 + 1;
                                int cols = c1 - c0 + 1;
                                // Read up to maxRowsPerSheet + 1 (header + data rows).
                                int take = Math.Min(totalRawRows, maxRowsPerSheet + 1);
                                for (int r = 0; r < take; r++)
                                {
                                    var row = new List<string>(cols);
                                    for (int c = 0; c < cols; c++)
                                        row.Add(FormatCellValue(arr.GetValue(r0 + r, c0 + c)));
                                    rawRows.Add(row);
                                }
                            }
                            else
                            {
                                totalRawRows = 1;
                                rawRows.Add(new List<string> { FormatCellValue(raw) });
                            }
                        }

                        var table = BuildExtractedTable(name, rawRows, totalRawRows, maxRowsPerSheet);
                        doc.Tables.Add(table);
                        AppendTableFullText(full, table);
                    }
                    finally
                    {
                        if (releaseCom)
                        {
                            FinalRelease(ref used);
                            FinalRelease(ref ws);
                        }
                    }
                }
            }
            finally
            {
                if (releaseCom) FinalRelease(ref sheets);
            }
            doc.FullText = TruncateText(full.ToString().Trim(), MaxFullTextChars);
        }

        // ---------- Hidden Word & PDF Extraction ----------
        static readonly object wordLaunchLock = new object();

        static void ExtractWordOrPdfHiddenCore(string path, ExtractedDoc doc, bool isPdf, Action<int> reportPid)
        {
            object app = null, docs = null, wdoc = null, opts = null;
            List<KeyValuePair<string, object>> savedPdfReg = isPdf ? SuppressWordPdfWarning() : null;
            try
            {
                // Snapshot WINWORD PIDs around private Word launch so timeout can kill ONLY our hidden instance.
                lock (wordLaunchLock)
                {
                    var before = GetProcessIdSet("WINWORD");
                    app = Create("Word.Application");
                    if (reportPid != null)
                    {
                        int spawned = FindNewProcessId("WINWORD", before);
                        if (spawned > 0) reportPid(spawned);
                    }
                }

                Set(app, "Visible", false);
                Set(app, "DisplayAlerts", 0 /* wdAlertsNone */);
                TrySet(app, "AutomationSecurity", 3 /* msoAutomationSecurityForceDisable */);
                try
                {
                    opts = Get(app, "Options");
                    TrySet(opts, "ConfirmConversions", false);
                }
                catch (Exception) { }

                docs = Get(app, "Documents");
                object m = Type.Missing;
                // Open(FileName, ConfirmConversions=false, ReadOnly=true, AddToRecentFiles=false, ..., Visible=false, OpenAndRepair=false, ..., NoEncodingDialog=true)
                wdoc = Call(docs, "Open", path, false, true, false, m, m, m, m, m, m, m, false, false, m, true);

                ExtractWordDocument(wdoc, doc, true);
                if (isPdf && string.IsNullOrWhiteSpace(doc.FullText) && doc.Tables.Count == 0 && string.IsNullOrEmpty(doc.Error))
                {
                    doc.Error = "PDF contains no extractable text (possibly image-only/scanned PDF).";
                }
            }
            finally
            {
                try { if (wdoc != null) Call(wdoc, "Close", 0 /* wdDoNotSaveChanges */); } catch (Exception) { }
                FinalRelease(ref wdoc);
                FinalRelease(ref docs);
                FinalRelease(ref opts);
                try { if (app != null) Call(app, "Quit", 0); } catch (Exception) { }
                FinalRelease(ref app);
                if (savedPdfReg != null) RestoreWordPdfWarning(savedPdfReg);
            }
        }

        static HashSet<int> GetProcessIdSet(string procName)
        {
            var set = new HashSet<int>();
            try
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try { set.Add(p.Id); }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception) { }
            return set;
        }

        static int FindNewProcessId(string procName, HashSet<int> before)
        {
            int found = 0;
            try
            {
                var procs = Process.GetProcessesByName(procName);
                DateTime newest = DateTime.MinValue;
                for (int i = 0; i < procs.Length; i++)
                {
                    var p = procs[i];
                    try
                    {
                        if (before.Contains(p.Id)) continue;
                        DateTime st = DateTime.MinValue;
                        try { st = p.StartTime; } catch (Exception) { }
                        if (found == 0 || st >= newest) { found = p.Id; newest = st; }
                    }
                    finally { p.Dispose(); }
                }
            }
            catch (Exception) { }
            return found;
        }

        // Suppresses Word's "Word will now convert your PDF to an editable Word document" modal prompt
        // (which DisplayAlerts=wdAlertsNone does not suppress in Office 15/16/365) and restores original state in finally.
        static List<KeyValuePair<string, object>> SuppressWordPdfWarning()
        {
            var saved = new List<KeyValuePair<string, object>>();
            string[] keys = { @"Software\Microsoft\Office\16.0\Word\Options", @"Software\Microsoft\Office\15.0\Word\Options" };
            for (int i = 0; i < keys.Length; i++)
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(keys[i], true))
                    {
                        if (k == null) continue;
                        object prev = k.GetValue("DisableConvertPdfWarning", null);
                        if (prev is int && (int)prev == 1) continue;
                        saved.Add(new KeyValuePair<string, object>(keys[i], prev));
                        k.SetValue("DisableConvertPdfWarning", 1, RegistryValueKind.DWord);
                    }
                }
                catch (Exception) { }
            }
            return saved;
        }

        static void RestoreWordPdfWarning(List<KeyValuePair<string, object>> saved)
        {
            for (int i = 0; i < saved.Count; i++)
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(saved[i].Key, true))
                    {
                        if (k == null) continue;
                        if (saved[i].Value == null) k.DeleteValue("DisableConvertPdfWarning", false);
                        else k.SetValue("DisableConvertPdfWarning", saved[i].Value, RegistryValueKind.DWord);
                    }
                }
                catch (Exception) { }
            }
        }

        // Pure managed fallback for plain (non-DRM) PDFs if Word PDF conversion is unavailable or stalled.
        static void TryExtractPdfTextFallback(string path, ExtractedDoc doc)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                string latin = Encoding.GetEncoding(28591).GetString(bytes);
                var sb = new StringBuilder();
                int pos = 0;
                while (pos < latin.Length && sb.Length < MaxFullTextChars)
                {
                    int sIdx = latin.IndexOf("stream", pos, StringComparison.Ordinal);
                    if (sIdx < 0) break;
                    int dataStart = sIdx + 6;
                    if (dataStart < latin.Length && latin[dataStart] == '\r') dataStart++;
                    if (dataStart < latin.Length && latin[dataStart] == '\n') dataStart++;
                    int eIdx = latin.IndexOf("endstream", dataStart, StringComparison.Ordinal);
                    if (eIdx < 0) break;
                    pos = eIdx + 9;

                    int dictStart = Math.Max(0, sIdx - 512);
                    string header = latin.Substring(dictStart, sIdx - dictStart);
                    int dataLen = eIdx - dataStart;
                    if (dataLen <= 0 || dataLen > 16 * 1024 * 1024) continue;

                    string content = null;
                    if (header.IndexOf("/FlateDecode", StringComparison.Ordinal) >= 0)
                    {
                        byte[] inflated = InflateZlib(bytes, dataStart, dataLen);
                        if (inflated != null) content = Encoding.GetEncoding(28591).GetString(inflated);
                    }
                    else if (header.IndexOf("/DCTDecode", StringComparison.Ordinal) < 0 && header.IndexOf("/JPXDecode", StringComparison.Ordinal) < 0)
                    {
                        content = latin.Substring(dataStart, dataLen);
                    }

                    if (!string.IsNullOrEmpty(content))
                        ExtractPdfStreamText(content, sb);
                }

                string extracted = sb.ToString().Trim();
                if (extracted.Length > 0)
                {
                    doc.FullText = TruncateText(extracted, MaxFullTextChars);
                    doc.Sections.Clear();
                    doc.Sections.Add(new ExtractedSection { Index = 1, Title = Path.GetFileNameWithoutExtension(path), Text = doc.FullText });
                    doc.Error = "";
                }
            }
            catch (Exception) { }
        }

        static byte[] InflateZlib(byte[] data, int offset, int length)
        {
            if (length <= 2) return null;
            // Skip 2-byte zlib header (typically 0x78 0x9C / 0x78 0x01 / 0x78 0xDA).
            int skip = (data[offset] == 0x78) ? 2 : 0;
            try
            {
                using (var ms = new MemoryStream(data, offset + skip, length - skip))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    var buf = new byte[8192];
                    int read;
                    int total = 0;
                    while ((read = ds.Read(buf, 0, buf.Length)) > 0)
                    {
                        outMs.Write(buf, 0, read);
                        total += read;
                        if (total > 8 * 1024 * 1024) break;
                    }
                    return outMs.ToArray();
                }
            }
            catch (Exception) { return null; }
        }

        static void ExtractPdfStreamText(string content, StringBuilder sb)
        {
            if (content.IndexOf("BT", StringComparison.Ordinal) < 0) return;
            int i = 0, len = content.Length;
            while (i < len && sb.Length < MaxFullTextChars)
            {
                if (content[i] == '(')
                {
                    var token = new StringBuilder();
                    int depth = 1;
                    i++;
                    while (i < len && depth > 0)
                    {
                        char c = content[i++];
                        if (c == '\\' && i < len)
                        {
                            char next = content[i++];
                            if (next == 'n') token.Append('\n');
                            else if (next == 'r') token.Append('\r');
                            else if (next == 't') token.Append('\t');
                            else if (next >= '0' && next <= '7')
                            {
                                int val = next - '0';
                                for (int k = 0; k < 2 && i < len && content[i] >= '0' && content[i] <= '7'; k++)
                                    val = val * 8 + (content[i++] - '0');
                                token.Append((char)val);
                            }
                            else token.Append(next);
                        }
                        else if (c == '(') { depth++; token.Append(c); }
                        else if (c == ')') { depth--; if (depth > 0) token.Append(c); }
                        else token.Append(c);
                    }
                    string s = token.ToString();
                    if (IsMostlyPrintable(s))
                    {
                        if (sb.Length > 0 && sb[sb.Length - 1] != '\n' && sb[sb.Length - 1] != ' ') sb.Append(' ');
                        sb.Append(s);
                    }
                }
                else
                {
                    i++;
                }
            }
            if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
        }

        static bool IsMostlyPrintable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            int good = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 32 && c < 127 || c == '\t' || c == '\n' || c == '\r' || c >= 0x0600) good++;
            }
            return good * 4 >= s.Length * 3;
        }

        static void ExtractWordDocument(object wdoc, ExtractedDoc doc, bool releaseCom)
        {
            object content = null;
            string rawText = "";
            try
            {
                content = Get(wdoc, "Content");
                rawText = Get(content, "Text") as string ?? "";
            }
            finally
            {
                if (releaseCom) FinalRelease(ref content);
            }

            string clean = CleanWordBodyText(rawText);
            doc.FullText = TruncateText(clean, MaxFullTextChars);

            // Split paragraphs into structured sections (~2000 char chunks or logical blocks).
            var lines = clean.Split('\n');
            var buf = new StringBuilder();
            string curTitle = "";
            int idx = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (buf.Length > 0 && buf.Length + line.Length > 2000)
                {
                    idx++;
                    doc.Sections.Add(new ExtractedSection
                    {
                        Index = idx,
                        Title = curTitle.Length > 0 ? curTitle : ("Section " + idx),
                        Text = buf.ToString().Trim()
                    });
                    buf.Clear();
                    curTitle = "";
                }
                if (curTitle.Length == 0) curTitle = line.Length > 80 ? line.Substring(0, 80) : line;
                if (buf.Length > 0) buf.Append('\n');
                buf.Append(line);
            }
            if (buf.Length > 0)
            {
                idx++;
                doc.Sections.Add(new ExtractedSection
                {
                    Index = idx,
                    Title = curTitle.Length > 0 ? curTitle : ("Section " + idx),
                    Text = buf.ToString().Trim()
                });
            }

            foreach (var tbl in ExtractWordTables(wdoc, releaseCom))
                doc.Tables.Add(tbl);
        }

        static List<ExtractedTable> ExtractWordTables(object wdoc, bool releaseCom)
        {
            var result = new List<ExtractedTable>();
            object tables = null;
            try
            {
                tables = Get(wdoc, "Tables");
                int count = Convert.ToInt32(Get(tables, "Count"), CultureInfo.InvariantCulture);
                for (int t = 1; t <= count; t++)
                {
                    object tbl = null;
                    try
                    {
                        tbl = Call(tables, "Item", t);
                        var rawRows = ReadSingleWordTable(tbl, releaseCom);
                        result.Add(BuildExtractedTable("Table " + t, rawRows, rawRows.Count, 25000));
                    }
                    catch (Exception) { }
                    finally { if (releaseCom) FinalRelease(ref tbl); }
                }
            }
            catch (Exception) { }
            finally { if (releaseCom) FinalRelease(ref tables); }
            return result;
        }

        static List<List<string>> ReadSingleWordTable(object tbl, bool releaseCom)
        {
            var rowsOut = new List<List<string>>();
            // Primary path: Table.Rows -> Row.Cells (fast and exact for unmerged tables).
            try
            {
                object rows = Get(tbl, "Rows");
                try
                {
                    int rCount = Convert.ToInt32(Get(rows, "Count"), CultureInfo.InvariantCulture);
                    for (int r = 1; r <= rCount; r++)
                    {
                        object rowObj = null, cells = null;
                        try
                        {
                            rowObj = Call(rows, "Item", r);
                            cells = Get(rowObj, "Cells");
                            int cCount = Convert.ToInt32(Get(cells, "Count"), CultureInfo.InvariantCulture);
                            var rowVals = new List<string>(cCount);
                            for (int c = 1; c <= cCount; c++)
                            {
                                object cell = null, cr = null;
                                try
                                {
                                    cell = Call(cells, "Item", c);
                                    cr = Get(cell, "Range");
                                    rowVals.Add(CleanWordCellText(Get(cr, "Text") as string ?? ""));
                                }
                                finally { if (releaseCom) { FinalRelease(ref cr); FinalRelease(ref cell); } }
                            }
                            rowsOut.Add(rowVals);
                        }
                        finally { if (releaseCom) { FinalRelease(ref cells); FinalRelease(ref rowObj); } }
                    }
                    return rowsOut;
                }
                finally { if (releaseCom) FinalRelease(ref rows); }
            }
            catch (Exception)
            {
                // Fallback for tables with vertically merged cells: iterate tbl.Range.Cells by RowIndex.
                rowsOut.Clear();
                object range = null, cells = null;
                try
                {
                    range = Get(tbl, "Range");
                    cells = Get(range, "Cells");
                    int total = Convert.ToInt32(Get(cells, "Count"), CultureInfo.InvariantCulture);
                    var byRow = new SortedDictionary<int, List<string>>();
                    for (int i = 1; i <= total; i++)
                    {
                        object cell = null, cr = null;
                        try
                        {
                            cell = Call(cells, "Item", i);
                            int rIdx = Convert.ToInt32(Get(cell, "RowIndex"), CultureInfo.InvariantCulture);
                            cr = Get(cell, "Range");
                            string txt = CleanWordCellText(Get(cr, "Text") as string ?? "");
                            List<string> rowList;
                            if (!byRow.TryGetValue(rIdx, out rowList)) { rowList = new List<string>(); byRow[rIdx] = rowList; }
                            rowList.Add(txt);
                        }
                        finally { if (releaseCom) { FinalRelease(ref cr); FinalRelease(ref cell); } }
                    }
                    foreach (var kv in byRow) rowsOut.Add(kv.Value);
                }
                finally { if (releaseCom) { FinalRelease(ref cells); FinalRelease(ref range); } }
            }
            return rowsOut;
        }

        // ---------- Hidden PowerPoint Extraction (Shared Process Safe) ----------
        // Returns true if PowerPoint was started by us and quit cleanly.
        static bool ExtractPptHiddenCore(string path, ExtractedDoc doc)
        {
            // CRITICAL: Check BEFORE Create("PowerPoint.Application") so we never quit a user's PowerPoint.
            bool startedByUs = Process.GetProcessesByName("POWERPNT").Length == 0;
            object app = null, presentations = null, pres = null;
            bool quitDone = false;
            try
            {
                app = Create("PowerPoint.Application");
                TrySet(app, "DisplayAlerts", 1 /* ppAlertsNone */);
                TrySet(app, "AutomationSecurity", 3 /* msoAutomationSecurityForceDisable */);

                presentations = Get(app, "Presentations");
                // Open(FileName, ReadOnly=-1 [msoTrue], Untitled=0 [msoFalse], WithWindow=0 [msoFalse])
                pres = Call(presentations, "Open", path, -1, 0, 0);

                ExtractPptPresentation(pres, doc);
            }
            finally
            {
                try
                {
                    if (pres != null)
                    {
                        TrySet(pres, "Saved", -1);
                        Call(pres, "Close");
                    }
                }
                catch (Exception) { }
                FinalRelease(ref pres);

                try
                {
                    if (app != null && startedByUs)
                    {
                        int remaining = 0;
                        try
                        {
                            if (presentations == null) presentations = Get(app, "Presentations");
                            remaining = Convert.ToInt32(Get(presentations, "Count"), CultureInfo.InvariantCulture);
                        }
                        catch (Exception) { }
                        if (remaining == 0)
                        {
                            Call(app, "Quit");
                            quitDone = true;
                        }
                    }
                }
                catch (Exception) { }

                // If PowerPoint was already running (e.g. user or OfficeEdit), NEVER FinalRelease the shared app RCW.
                if (quitDone)
                {
                    FinalRelease(ref presentations);
                    FinalRelease(ref app);
                }
                else
                {
                    SoftRelease(ref presentations);
                    SoftRelease(ref app);
                }
            }
            return quitDone;
        }

        static void ExtractPptPresentation(object pres, ExtractedDoc doc)
        {
            var slides = ReadPptSlides(pres);
            var full = new StringBuilder();
            for (int i = 0; i < slides.Count; i++)
            {
                var s = slides[i];
                int idx = Convert.ToInt32(s["index"], CultureInfo.InvariantCulture);
                string title = s["title"] as string ?? "";
                var texts = s["texts"] as List<string> ?? new List<string>();
                var tables = s["tables"] as List<List<List<string>>> ?? new List<List<List<string>>>();
                string notes = s["notes"] as string ?? "";

                var sb = new StringBuilder();
                if (title.Length > 0) sb.AppendLine(title);
                foreach (var t in texts)
                {
                    if (!string.IsNullOrWhiteSpace(t)) sb.AppendLine(t);
                }
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    sb.AppendLine("[Notes] " + notes);
                }

                string secText = sb.ToString().Trim();
                doc.Sections.Add(new ExtractedSection
                {
                    Index = idx,
                    Title = title.Length > 0 ? title : ("Slide " + idx),
                    Text = secText
                });

                if (full.Length < MaxFullTextChars)
                {
                    if (full.Length > 0) full.Append("\n\n");
                    full.Append("--- Slide ").Append(idx);
                    if (title.Length > 0) full.Append(": ").Append(title);
                    full.Append(" ---\n").Append(secText);
                }

                for (int ti = 0; ti < tables.Count; ti++)
                {
                    var tbl = BuildExtractedTable("Slide " + idx + " Table " + (ti + 1), tables[ti], tables[ti].Count, 25000);
                    doc.Tables.Add(tbl);
                    AppendTableFullText(full, tbl);
                }
            }
            doc.FullText = TruncateText(full.ToString().Trim(), MaxFullTextChars);
        }

        static List<Dictionary<string, object>> ReadPptSlides(object pres)
        {
            var result = new List<Dictionary<string, object>>();
            object slides = Get(pres, "Slides");
            int count = Convert.ToInt32(Get(slides, "Count"), CultureInfo.InvariantCulture);

            for (int i = 1; i <= count; i++)
            {
                object slide = Call(slides, "Item", i);
                object shapes = Get(slide, "Shapes");
                int sc = Convert.ToInt32(Get(shapes, "Count"), CultureInfo.InvariantCulture);

                string title = "";
                try
                {
                    if (IsMsoTrue(Get(shapes, "HasTitle")))
                    {
                        title = CleanPptText(Get(Get(Get(Get(shapes, "Title"), "TextFrame"), "TextRange"), "Text") as string ?? "");
                    }
                }
                catch (Exception) { }

                var texts = new List<string>();
                var tables = new List<List<List<string>>>();

                for (int j = 1; j <= sc; j++)
                {
                    object sh = Call(shapes, "Item", j);
                    try
                    {
                        if (IsMsoTrue(Get(sh, "HasTextFrame")))
                        {
                            object tf = Get(sh, "TextFrame");
                            if (IsMsoTrue(Get(tf, "HasText")))
                            {
                                string txt = CleanPptText(Get(Get(tf, "TextRange"), "Text") as string ?? "");
                                if (txt.Length > 0)
                                {
                                    if (title.Length == 0 && IsPptTitlePlaceholder(sh))
                                        title = txt;
                                    else if (!string.Equals(txt, title, StringComparison.Ordinal))
                                        texts.Add(txt);
                                }
                            }
                        }
                    }
                    catch (Exception) { }

                    try
                    {
                        if (IsMsoTrue(Get(sh, "HasTable")))
                        {
                            object tbl = Get(sh, "Table");
                            int rc = Convert.ToInt32(Get(Get(tbl, "Rows"), "Count"), CultureInfo.InvariantCulture);
                            int cc = Convert.ToInt32(Get(Get(tbl, "Columns"), "Count"), CultureInfo.InvariantCulture);
                            var rows = new List<List<string>>(rc);
                            for (int r = 1; r <= rc; r++)
                            {
                                var row = new List<string>(cc);
                                for (int c = 1; c <= cc; c++)
                                {
                                    string cellText = "";
                                    try
                                    {
                                        object cell = Call(tbl, "Cell", r, c);
                                        cellText = CleanPptText(Get(Get(Get(Get(cell, "Shape"), "TextFrame"), "TextRange"), "Text") as string ?? "");
                                    }
                                    catch (Exception) { }
                                    row.Add(cellText);
                                }
                                rows.Add(row);
                            }
                            tables.Add(rows);
                        }
                    }
                    catch (Exception) { }
                }

                string notes = ReadPptSlideNotes(slide);
                result.Add(new Dictionary<string, object>
                {
                    { "index", i },
                    { "title", title },
                    { "texts", texts },
                    { "tables", tables },
                    { "notes", notes }
                });
            }
            return result;
        }

        static string ReadPptSlideNotes(object slide)
        {
            try
            {
                if (!IsMsoTrue(Get(slide, "HasNotesPage"))) return "";
                object notesPage = Get(slide, "NotesPage");
                object nShapes = Get(notesPage, "Shapes");
                int nCount = Convert.ToInt32(Get(nShapes, "Count"), CultureInfo.InvariantCulture);
                for (int k = 1; k <= nCount; k++)
                {
                    object sh = Call(nShapes, "Item", k);
                    try
                    {
                        if (Convert.ToInt32(Get(sh, "Type"), CultureInfo.InvariantCulture) == 14 /* msoPlaceholder */)
                        {
                            int pt = Convert.ToInt32(Get(Get(sh, "PlaceholderFormat"), "Type"), CultureInfo.InvariantCulture);
                            if (pt == 2 /* ppPlaceholderBody */ && IsMsoTrue(Get(sh, "HasTextFrame")))
                            {
                                object tf = Get(sh, "TextFrame");
                                if (IsMsoTrue(Get(tf, "HasText")))
                                    return CleanPptText(Get(Get(tf, "TextRange"), "Text") as string ?? "");
                            }
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return "";
        }

        static bool IsPptTitlePlaceholder(object shape)
        {
            try
            {
                if (Convert.ToInt32(Get(shape, "Type"), CultureInfo.InvariantCulture) != 14) return false;
                int pt = Convert.ToInt32(Get(Get(shape, "PlaceholderFormat"), "Type"), CultureInfo.InvariantCulture);
                return pt == 1 /* ppPlaceholderTitle */ || pt == 3 /* ppPlaceholderCenterTitle */;
            }
            catch (Exception) { return false; }
        }

        static object FindPptBodyShape(object shapes, object titleShape)
        {
            int sc = Convert.ToInt32(Get(shapes, "Count"), CultureInfo.InvariantCulture);
            object fallbackText = null;
            int titleId = -1;
            if (titleShape != null)
            {
                try { titleId = Convert.ToInt32(Get(titleShape, "Id"), CultureInfo.InvariantCulture); } catch (Exception) { }
            }
            for (int j = 1; j <= sc; j++)
            {
                object sh = Call(shapes, "Item", j);
                try
                {
                    int shId = Convert.ToInt32(Get(sh, "Id"), CultureInfo.InvariantCulture);
                    if (titleId >= 0 && shId == titleId) continue;
                    if (Convert.ToInt32(Get(sh, "Type"), CultureInfo.InvariantCulture) == 14)
                    {
                        int pt = Convert.ToInt32(Get(Get(sh, "PlaceholderFormat"), "Type"), CultureInfo.InvariantCulture);
                        if (pt == 2 /* ppPlaceholderBody */ || pt == 7 /* ppPlaceholderObject */ || pt == 4 /* ppPlaceholderSubtitle */)
                            return sh;
                    }
                    if (fallbackText == null && IsMsoTrue(Get(sh, "HasTextFrame"))) fallbackText = sh;
                }
                catch (Exception) { }
            }
            return fallbackText;
        }

        // ---------- Direct RFC-4180 CSV Extraction ----------
        static void ExtractCsvDirect(string path, ExtractedDoc doc, int maxRows)
        {
            var rawRows = new List<List<string>>();
            int totalRawRows = 0;

            using (var sr = new StreamReader(path, Encoding.UTF8, true))
            {
                var curRow = new List<string>();
                var field = new StringBuilder();
                bool inQuotes = false;
                bool fieldStarted = false;
                int ch;

                while ((ch = sr.Read()) != -1)
                {
                    char c = (char)ch;
                    if (inQuotes)
                    {
                        if (c == '"')
                        {
                            if (sr.Peek() == '"')
                            {
                                sr.Read();
                                field.Append('"');
                            }
                            else
                            {
                                inQuotes = false;
                            }
                        }
                        else
                        {
                            field.Append(c);
                        }
                    }
                    else
                    {
                        if (c == '"' && !fieldStarted && field.Length == 0)
                        {
                            inQuotes = true;
                            fieldStarted = true;
                        }
                        else if (c == ',')
                        {
                            curRow.Add(field.ToString());
                            field.Clear();
                            fieldStarted = false;
                        }
                        else if (c == '\r' || c == '\n')
                        {
                            if (c == '\r' && sr.Peek() == '\n') sr.Read();
                            curRow.Add(field.ToString());
                            field.Clear();
                            fieldStarted = false;
                            totalRawRows++;
                            if (rawRows.Count < maxRows + 1) rawRows.Add(curRow);
                            curRow = new List<string>();
                        }
                        else
                        {
                            field.Append(c);
                            fieldStarted = true;
                        }
                    }
                }

                // Flush last line if non-empty.
                if (field.Length > 0 || curRow.Count > 0 || fieldStarted)
                {
                    curRow.Add(field.ToString());
                    totalRawRows++;
                    if (rawRows.Count < maxRows + 1) rawRows.Add(curRow);
                }
            }

            string name = Path.GetFileNameWithoutExtension(path);
            var table = BuildExtractedTable(string.IsNullOrEmpty(name) ? "CSV" : name, rawRows, totalRawRows, maxRows);
            doc.Tables.Add(table);
            var full = new StringBuilder();
            AppendTableFullText(full, table);
            doc.FullText = TruncateText(full.ToString().Trim(), MaxFullTextChars);
        }

        // =====================================================================
        // Table Normalization & Formatting Helpers
        // =====================================================================

        static ExtractedTable BuildExtractedTable(string name, List<List<string>> rawRows, int totalRawRows, int maxDataRows)
        {
            var table = new ExtractedTable { Name = name ?? "" };
            if (rawRows == null || rawRows.Count == 0) return table;

            // Find first non-empty row as header.
            int hdrIdx = -1;
            for (int i = 0; i < rawRows.Count; i++)
            {
                if (HasAnyNonEmpty(rawRows[i])) { hdrIdx = i; break; }
            }
            if (hdrIdx < 0) return table;

            int maxCols = 0;
            for (int i = hdrIdx; i < rawRows.Count; i++)
                if (rawRows[i] != null && rawRows[i].Count > maxCols) maxCols = rawRows[i].Count;
            if (maxCols <= 0) return table;

            // Build unique, non-empty SQL-ready column names.
            var seenCols = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var hdrRow = rawRows[hdrIdx];
            for (int c = 0; c < maxCols; c++)
            {
                string rawName = (hdrRow != null && c < hdrRow.Count && hdrRow[c] != null) ? hdrRow[c].Trim() : "";
                string baseName = rawName.Length > 0 ? rawName : ("Col" + (c + 1));
                int dup;
                if (seenCols.TryGetValue(baseName, out dup))
                {
                    dup++;
                    seenCols[baseName] = dup;
                    string candidate = baseName + "_" + dup;
                    while (seenCols.ContainsKey(candidate)) { dup++; candidate = baseName + "_" + dup; }
                    seenCols[candidate] = 1;
                    table.Columns.Add(candidate);
                }
                else
                {
                    seenCols[baseName] = 1;
                    table.Columns.Add(baseName);
                }
            }

            // Populate data rows after header row, padded to maxCols.
            int keptDataRows = 0;
            for (int i = hdrIdx + 1; i < rawRows.Count; i++)
            {
                var r = rawRows[i];
                if (!HasAnyNonEmpty(r)) continue;
                keptDataRows++;
                if (table.Rows.Count >= maxDataRows) continue;
                var padded = new List<string>(maxCols);
                for (int c = 0; c < maxCols; c++)
                    padded.Add((r != null && c < r.Count && r[c] != null) ? r[c] : "");
                table.Rows.Add(padded);
            }

            int skippedByCap = Math.Max(0, totalRawRows - rawRows.Count);
            table.TotalRows = keptDataRows + skippedByCap;
            table.TotalCols = maxCols;
            return table;
        }

        static bool HasAnyNonEmpty(List<string> row)
        {
            if (row == null) return false;
            for (int i = 0; i < row.Count; i++)
                if (!string.IsNullOrWhiteSpace(row[i])) return true;
            return false;
        }

        static void AppendTableFullText(StringBuilder sb, ExtractedTable table)
        {
            if (table == null || (table.Columns.Count == 0 && table.Rows.Count == 0)) return;
            if (sb.Length >= MaxFullTextChars) return;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append("=== ").Append(table.Name).Append(" (").Append(table.TotalRows).Append(" rows x ").Append(table.TotalCols).Append(" cols) ===\n");
            if (table.Columns.Count > 0) sb.Append(string.Join("\t", table.Columns)).Append('\n');
            for (int i = 0; i < table.Rows.Count && sb.Length < MaxFullTextChars; i++)
            {
                sb.Append(string.Join("\t", table.Rows[i])).Append('\n');
            }
        }

        static string TruncateText(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > max ? s.Substring(0, max) : s;
        }

        static void FillFileMeta(ExtractedDoc doc, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists)
                {
                    doc.FileSize = fi.Length;
                    doc.ModifiedTicks = fi.LastWriteTimeUtc.Ticks;
                }
            }
            catch (Exception) { }
            try { doc.IsDrm = Mail.IsDrm(path); } catch (Exception) { }
        }

        static string FormatCellValue(object v)
        {
            if (v == null || v == DBNull.Value) return "";
            string s = v as string;
            if (s != null) return s;
            if (v is bool) return ((bool)v) ? "TRUE" : "FALSE";
            if (v is DateTime)
            {
                DateTime dt = (DateTime)v;
                return dt.TimeOfDay == TimeSpan.Zero
                    ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            if (v is int)
            {
                int iv = (int)v;
                switch (iv)
                {
                    case -2146826288: return "#NULL!";
                    case -2146826281: return "#DIV/0!";
                    case -2146826273: return "#VALUE!";
                    case -2146826265: return "#REF!";
                    case -2146826259: return "#NAME?";
                    case -2146826252: return "#NUM!";
                    case -2146826246: return "#N/A";
                    case -2146826245: return "#GETTING_DATA";
                }
                return iv.ToString(CultureInfo.InvariantCulture);
            }
            if (v is double)
            {
                double d = (double)v;
                if (double.IsNaN(d) || double.IsInfinity(d)) return d.ToString(CultureInfo.InvariantCulture);
                if (Math.Floor(d) == d && Math.Abs(d) < 1e15) return d.ToString("0", CultureInfo.InvariantCulture);
                return d.ToString("G15", CultureInfo.InvariantCulture);
            }
            if (v is decimal) return ((decimal)v).ToString(CultureInfo.InvariantCulture);
            if (v is float) return ((float)v).ToString("G7", CultureInfo.InvariantCulture);
            return Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        }

        static object NormalizeExcelInput(object raw)
        {
            if (raw == null || raw == DBNull.Value) return null;
            if (raw is string || raw is bool || raw is int || raw is double) return raw;
            if (raw is decimal) return Convert.ToDouble((decimal)raw, CultureInfo.InvariantCulture);
            if (raw is float) return (double)(float)raw;
            if (raw is long)
            {
                long l = (long)raw;
                return (l >= int.MinValue && l <= int.MaxValue) ? (object)(int)l : (double)l;
            }
            if (raw is short || raw is byte || raw is sbyte || raw is ushort) return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            if (raw is DateTime) return ((DateTime)raw).ToOADate();
            return Convert.ToString(raw, CultureInfo.InvariantCulture);
        }

        static int ParseHexToOleColor(string hex)
        {
            string s = (hex ?? "").Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length == 3)
            {
                s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            }
            if (s.Length == 6)
            {
                int r, g, b;
                if (int.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                    && int.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                    && int.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                {
                    return (b << 16) | (g << 8) | r;
                }
            }
            Color named = Color.FromName(hex.Trim());
            if (named.IsKnownColor) return (named.B << 16) | (named.G << 8) | named.R;
            throw new ArgumentException("Invalid color: \"" + hex + "\". Use #RRGGBB hex format.");
        }

        static string SanitizeSheetName(string name, HashSet<string> existing)
        {
            string s = (name ?? "").Trim();
            if (s.Length == 0) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' || c == '/' || c == '?' || c == '*' || c == '[' || c == ']' || c == ':') sb.Append('_');
                else sb.Append(c);
            }
            s = sb.ToString().Trim('\'', ' ');
            if (s.Length == 0) s = "Sheet";
            if (s.Length > 31) s = s.Substring(0, 31).Trim();
            if (existing == null || !existing.Contains(s)) return s;

            for (int n = 2; n < 1000; n++)
            {
                string suffix = " " + n;
                string stem = s.Length + suffix.Length > 31 ? s.Substring(0, 31 - suffix.Length).Trim() : s;
                string candidate = stem + suffix;
                if (!existing.Contains(candidate)) return candidate;
            }
            return s;
        }

        static string CleanWordBodyText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // Word tables delimit cell ends with "\r\a" and row ends with "\r\a\r\a".
            string t = s.Replace("\r\a\r\a", "\n").Replace("\r\a", "\t").Replace("\a", "").Replace("\f", "\n").Replace("\r\n", "\n").Replace("\r", "\n");
            return t.TrimEnd('\n');
        }

        static string CleanWordCellText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\a", "").Replace("\a", "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        }

        static string CleanPptText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\v", "\n").Trim();
        }

        static string NormalizePptNewlines(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r\n", "\r").Replace("\n", "\r");
        }

        // =====================================================================
        // Excel Navigation Helpers
        // =====================================================================

        static void RequireEditor(OfficeEdit ed, string expectedKind)
        {
            if (ed == null) throw new InvalidOperationException("No active " + expectedKind + " editor is open.");
            if (!string.Equals(ed.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active editor is " + (ed.Kind ?? "unknown") + ", expected " + expectedKind + ".");
            if (ed.ComApp == null || ed.ComDoc == null)
                throw new InvalidOperationException("The " + expectedKind + " COM session is not connected.");
        }

        static object ResolveWorksheet(object wb, string sheet)
        {
            object sheets = Get(wb, "Worksheets");
            int count = Convert.ToInt32(Get(sheets, "Count"), CultureInfo.InvariantCulture);
            if (count <= 0) throw new InvalidOperationException("Workbook contains no worksheets.");

            if (string.IsNullOrWhiteSpace(sheet))
            {
                try
                {
                    object act = Get(wb, "ActiveSheet");
                    if (act != null)
                    {
                        // Verify ActiveSheet is a worksheet (has UsedRange), otherwise fall back to Worksheets[1].
                        Get(act, "UsedRange");
                        return act;
                    }
                }
                catch (Exception) { }
                return Get(sheets, "Item", 1);
            }

            string want = sheet.Trim();
            var names = new List<string>(count);
            for (int i = 1; i <= count; i++)
            {
                object ws = Get(sheets, "Item", i);
                string nm = Convert.ToString(Get(ws, "Name"), CultureInfo.InvariantCulture) ?? "";
                names.Add(nm);
                if (string.Equals(nm, want, StringComparison.OrdinalIgnoreCase)) return ws;
            }
            throw new ArgumentException("Worksheet \"" + want + "\" not found. Available sheets: " + string.Join(", ", names));
        }

        static string GetActiveSheetName(object wb)
        {
            try
            {
                object act = Get(wb, "ActiveSheet");
                return act != null ? (Convert.ToString(Get(act, "Name"), CultureInfo.InvariantCulture) ?? "") : "";
            }
            catch (Exception) { return ""; }
        }

        static string GetSelectionAddress(object app)
        {
            try
            {
                object sel = Get(app, "Selection");
                return sel != null ? GetRangeAddress(sel) : "";
            }
            catch (Exception) { return ""; }
        }

        static string GetRangeAddress(object rng)
        {
            try
            {
                object a = Get(rng, "Address", false, false);
                if (a != null) return Convert.ToString(a, CultureInfo.InvariantCulture) ?? "";
            }
            catch (Exception) { }
            try
            {
                object a = Get(rng, "Address");
                return a != null ? (Convert.ToString(a, CultureInfo.InvariantCulture) ?? "").Replace("$", "") : "";
            }
            catch (Exception) { return ""; }
        }

        static string GetRangeAddressFast(object rng)
        {
            try
            {
                object a = GetOnce(rng, "Address", false, false);
                if (a != null) return Convert.ToString(a, CultureInfo.InvariantCulture) ?? "";
            }
            catch (Exception) { }
            try
            {
                object a = GetOnce(rng, "Address");
                return a != null ? (Convert.ToString(a, CultureInfo.InvariantCulture) ?? "").Replace("$", "") : "";
            }
            catch (Exception) { return ""; }
        }

        static bool IsMsoTrue(object v)
        {
            if (v == null) return false;
            if (v is bool) return (bool)v;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture) != 0; }
            catch (Exception) { return false; }
        }

        // =====================================================================
        // Late-Bound COM Primitives (C# 5, en-US Culture, Retry on Busy)
        // =====================================================================

        static object Create(string progId)
        {
            var t = Type.GetTypeFromProgID(progId);
            if (t == null) throw new InvalidOperationException(progId.Split('.')[0] + " is not installed.");
            return Activator.CreateInstance(t);
        }

        static object GetOnce(object o, string name, params object[] args)
        {
            object[] a = (args != null && args.Length > 0) ? args : null;
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, a, null, EnUs, null);
        }

        static object Get(object o, string name, params object[] args)
        {
            object[] a = (args != null && args.Length > 0) ? args : null;
            return InvokeCom(o, name, BindingFlags.GetProperty, a);
        }

        static void Set(object o, string name, object val)
        {
            InvokeCom(o, name, BindingFlags.SetProperty, new object[] { val });
        }

        static void TrySet(object o, string name, object val)
        {
            try { Set(o, name, val); } catch (Exception) { }
        }

        static object Call(object o, string name, params object[] args)
        {
            object[] a = (args != null && args.Length > 0) ? args : null;
            return InvokeCom(o, name, BindingFlags.InvokeMethod, a);
        }

        static object InvokeCom(object o, string name, BindingFlags flags, object[] args)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    return o.GetType().InvokeMember(name, flags, null, o, args, null, EnUs, null);
                }
                catch (TargetInvocationException ex)
                {
                    var comEx = ex.InnerException as COMException;
                    if (comEx != null && (comEx.ErrorCode == RpcCallRejected || comEx.ErrorCode == RpcRetryLater) && attempt < 4)
                    {
                        Thread.Sleep(150);
                        continue;
                    }
                    if (ex.InnerException != null) throw new InvalidOperationException(ex.InnerException.Message, ex.InnerException);
                    throw;
                }
                catch (COMException ex)
                {
                    if ((ex.ErrorCode == RpcCallRejected || ex.ErrorCode == RpcRetryLater) && attempt < 4)
                    {
                        Thread.Sleep(150);
                        continue;
                    }
                    throw;
                }
            }
            return null;
        }

        static void FinalRelease(ref object o)
        {
            if (o != null)
            {
                try { if (Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); }
                catch (Exception) { }
                o = null;
            }
        }

        static void SoftRelease(ref object o)
        {
            if (o != null)
            {
                try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
                catch (Exception) { }
                o = null;
            }
        }

        static string UnwrapError(Exception ex)
        {
            if (ex == null) return "";
            Exception cur = ex;
            while (cur is TargetInvocationException && cur.InnerException != null) cur = cur.InnerException;
            return cur.Message;
        }
    }
}
