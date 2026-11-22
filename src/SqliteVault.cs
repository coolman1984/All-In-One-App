using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace StickyPro
{
    // Native Windows SQLite Vault over System32\winsqlite3.dll (zero external NuGet/DLLs).
    // Stores extracted Office/PDF documents, dynamic per-sheet SQL tables (for cross-report SQL JOINs),
    // full-text indexes (FTS5 with transparent FTS4/LIKE fallback), session message transcripts with
    // BM25 + Temporal Decay + MMR search, event-sourced step_events (fork/inspect), and alert_dedup.
    static class SqliteVault
    {
        const string DllName = "winsqlite3.dll";

        const int SQLITE_OK = 0;
        const int SQLITE_ROW = 100;
        const int SQLITE_DONE = 101;

        const int SQLITE_OPEN_READWRITE = 0x00000002;
        const int SQLITE_OPEN_CREATE = 0x00000004;
        const int SQLITE_OPEN_FULLMUTEX = 0x00010000;

        const int SQLITE_INTEGER = 1;
        const int SQLITE_FLOAT = 2;
        const int SQLITE_TEXT = 3;
        const int SQLITE_BLOB = 4;
        const int SQLITE_NULL = 5;

        static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);
        static readonly JavaScriptSerializer Js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        static readonly Regex WordTokenRx = new Regex(@"[\p{L}\p{N}_]+", RegexOptions.Compiled);

        static readonly HashSet<string> SupportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xlsx", ".xlsm", ".xlsb", ".xls", ".csv",
            ".docx", ".docm", ".doc", ".rtf",
            ".pptx", ".pptm", ".ppt", ".ppsx",
            ".pdf"
        };

        // =====================================================================
        // x64 P/Invoke Declarations (winsqlite3.dll, UTF-8 byte arrays only)
        // =====================================================================

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_open_v2(byte[] filename, out IntPtr ppDb, int flags, IntPtr zVfs);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_close_v2(IntPtr db);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern void sqlite3_free(IntPtr ptr);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_prepare_v2(IntPtr db, byte[] zSql, int nByte, out IntPtr ppStmt, out IntPtr pzTail);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] val, int nBytes, IntPtr destructor);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_bind_int64(IntPtr stmt, int index, long val);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_bind_double(IntPtr stmt, int index, double val);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_bind_null(IntPtr stmt, int index);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_step(IntPtr stmt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_reset(IntPtr stmt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_clear_bindings(IntPtr stmt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr sqlite3_column_name(IntPtr stmt, int iCol);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_column_type(IntPtr stmt, int iCol);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_column_bytes(IntPtr stmt, int iCol);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern long sqlite3_column_int64(IntPtr stmt, int iCol);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern double sqlite3_column_double(IntPtr stmt, int iCol);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern long sqlite3_last_insert_rowid(IntPtr db);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr sqlite3_backup_init(IntPtr destination, byte[] destName, IntPtr source, byte[] sourceName);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_backup_step(IntPtr backup, int pages);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        static extern int sqlite3_backup_finish(IntPtr backup);

        public static void CopySnapshot(string source, string destination)
        {
            IntPtr src = IntPtr.Zero, dest = IntPtr.Zero;
            bool ownsDestination = false, complete = false;
            try
            {
                int code = sqlite3_open_v2(ToUtf8Z(source), out src, 1 /* read only */ | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
                if (code != SQLITE_OK) throw new IOException("Cannot read the vault for backup: " + GetError(src));
                using (File.Open(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { ownsDestination = true; }
                code = sqlite3_open_v2(ToUtf8Z(destination), out dest, SQLITE_OPEN_READWRITE | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
                if (code != SQLITE_OK) throw new IOException("Cannot create the vault backup: " + GetError(dest));

                byte[] main = ToUtf8Z("main");
                IntPtr backup = sqlite3_backup_init(dest, main, src, main);
                if (backup == IntPtr.Zero) throw new IOException("Cannot start the vault backup: " + GetError(dest));
                int finish;
                try
                {
                    for (int attempt = 0; ; attempt++)
                    {
                        code = sqlite3_backup_step(backup, -1);
                        if ((code != 5 /* busy */ && code != 6 /* locked */) || attempt >= 25) break;
                        Thread.Sleep(100);
                    }
                }
                finally { finish = sqlite3_backup_finish(backup); }
                if (code != SQLITE_DONE || finish != SQLITE_OK) throw new IOException("Could not finish a consistent vault backup: " + GetError(dest));
                complete = true;
            }
            finally
            {
                if (dest != IntPtr.Zero) sqlite3_close_v2(dest);
                if (src != IntPtr.Zero) sqlite3_close_v2(src);
                if (ownsDestination && !complete)
                    try { File.Delete(destination); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        // =====================================================================
        // State & Lifecycle
        // =====================================================================

        static readonly object dbLock = new object();
        static IntPtr dbHandle = IntPtr.Zero;
        static string openPath;
        static string ftsMode = "plain";
        static readonly Dictionary<string, int> indexedChatCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Lazy background extraction queue (single low-overhead worker, deduped in-flight paths)
        static readonly object queueLock = new object();
        static readonly Queue<string> extractQueue = new Queue<string>();
        static readonly HashSet<string> inFlightPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly AutoResetEvent queueSignal = new AutoResetEvent(false);
        static Thread extractWorker;
        static volatile bool queueStopping;

        public static string DbPath
        {
            get
            {
                string dir = string.IsNullOrEmpty(Store.Dir) ? Store.DefaultDir : Store.Dir;
                return Path.Combine(dir, "vault.db");
            }
        }

        public static string FtsMode
        {
            get
            {
                lock (dbLock)
                {
                    EnsureOpenLocked();
                    return ftsMode;
                }
            }
        }

        public static bool IsSupportedDocument(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && SupportedExts.Contains(ext);
        }

        public static string JournalMode()
        {
            lock (dbLock)
            {
                EnsureOpenLocked();
                var res = QuerySqlLocked("PRAGMA journal_mode;", 1);
                var rows = res["rows"] as List<List<object>>;
                if (rows != null && rows.Count > 0 && rows[0].Count > 0 && rows[0][0] != null)
                    return Convert.ToString(rows[0][0], CultureInfo.InvariantCulture);
                return "";
            }
        }

        public static void Close()
        {
            lock (queueLock)
            {
                queueStopping = true;
                extractQueue.Clear();
                inFlightPaths.Clear();
                queueSignal.Set();
            }
            lock (dbLock)
            {
                CloseLocked();
            }
        }

        static void CloseLocked()
        {
            indexedChatCounts.Clear();
            if (dbHandle != IntPtr.Zero)
            {
                try { ExecSilentLocked("PRAGMA wal_checkpoint(TRUNCATE);"); }
                catch (Exception) { }
                try { sqlite3_close_v2(dbHandle); }
                catch (Exception) { }
                dbHandle = IntPtr.Zero;
            }
            openPath = null;
            ftsMode = "plain";
        }

        static void EnsureOpenLocked()
        {
            string target = DbPath;
            if (dbHandle != IntPtr.Zero && string.Equals(openPath, target, StringComparison.OrdinalIgnoreCase))
                return;

            if (dbHandle != IntPtr.Zero)
                CloseLocked();

            string dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            IntPtr h;
            int rc = sqlite3_open_v2(ToUtf8Z(target), out h, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, IntPtr.Zero);
            if (rc != SQLITE_OK || h == IntPtr.Zero)
            {
                string err = h != IntPtr.Zero ? GetError(h) : ("sqlite3_open_v2 failed with code " + rc);
                if (h != IntPtr.Zero) sqlite3_close_v2(h);
                throw new IOException("Could not open vault.db: " + err);
            }

            dbHandle = h;
            openPath = target;

            try
            {
                ExecLocked(
                    "PRAGMA journal_mode=WAL;\n" +
                    "PRAGMA synchronous=NORMAL;\n" +
                    "PRAGMA temp_store=MEMORY;\n" +
                    "PRAGMA busy_timeout=5000;\n" +
                    "PRAGMA foreign_keys=ON;");
                InitSchemaLocked();
            }
            catch
            {
                CloseLocked();
                throw;
            }
        }

        static void InitSchemaLocked()
        {
            ExecLocked(
                "CREATE TABLE IF NOT EXISTS vault_docs (\n" +
                "  id INTEGER PRIMARY KEY AUTOINCREMENT,\n" +
                "  path TEXT UNIQUE NOT NULL,\n" +
                "  file_name TEXT NOT NULL,\n" +
                "  kind TEXT NOT NULL,\n" +
                "  is_drm INTEGER NOT NULL DEFAULT 0,\n" +
                "  file_size INTEGER NOT NULL,\n" +
                "  modified_ticks INTEGER NOT NULL,\n" +
                "  extracted_utc TEXT NOT NULL,\n" +
                "  sheet_count INTEGER NOT NULL DEFAULT 0,\n" +
                "  row_count INTEGER NOT NULL DEFAULT 0,\n" +
                "  summary TEXT,\n" +
                "  full_text TEXT\n" +
                ");\n" +
                "CREATE TABLE IF NOT EXISTS vault_tables (\n" +
                "  id INTEGER PRIMARY KEY AUTOINCREMENT,\n" +
                "  doc_id INTEGER NOT NULL REFERENCES vault_docs(id) ON DELETE CASCADE,\n" +
                "  sheet_name TEXT NOT NULL,\n" +
                "  sql_table TEXT UNIQUE NOT NULL,\n" +
                "  columns_json TEXT NOT NULL,\n" +
                "  row_count INTEGER NOT NULL\n" +
                ");\n" +
                "CREATE TABLE IF NOT EXISTS session_msgs (\n" +
                "  id INTEGER PRIMARY KEY AUTOINCREMENT,\n" +
                "  chat_id TEXT NOT NULL,\n" +
                "  scope TEXT NOT NULL,\n" +
                "  role TEXT NOT NULL,\n" +
                "  agent TEXT,\n" +
                "  text TEXT NOT NULL,\n" +
                "  time_ticks INTEGER NOT NULL\n" +
                ");\n" +
                "CREATE INDEX IF NOT EXISTS idx_session_msgs_chat_time ON session_msgs(chat_id, time_ticks);\n" +
                "CREATE INDEX IF NOT EXISTS idx_session_msgs_scope ON session_msgs(scope, time_ticks);\n" +
                "CREATE TABLE IF NOT EXISTS step_events (\n" +
                "  id INTEGER PRIMARY KEY AUTOINCREMENT,\n" +
                "  chat_id TEXT NOT NULL,\n" +
                "  turn_idx INTEGER NOT NULL,\n" +
                "  step_idx INTEGER NOT NULL,\n" +
                "  kind TEXT NOT NULL,\n" +
                "  tool_name TEXT,\n" +
                "  payload_json TEXT,\n" +
                "  time_ticks INTEGER NOT NULL\n" +
                ");\n" +
                "CREATE INDEX IF NOT EXISTS idx_step_events_chat ON step_events(chat_id, id);\n" +
                "CREATE TABLE IF NOT EXISTS alert_dedup (\n" +
                "  hash TEXT PRIMARY KEY,\n" +
                "  source TEXT NOT NULL,\n" +
                "  first_seen_utc TEXT NOT NULL,\n" +
                "  last_seen_utc TEXT NOT NULL\n" +
                ");");

            // Detect existing FTS table definition or create best available (FTS5 -> FTS4 -> plain table).
            string existingFts = ScalarTextLocked("SELECT sql FROM sqlite_master WHERE name='vault_fts' LIMIT 1;");
            if (!string.IsNullOrEmpty(existingFts))
            {
                if (existingFts.IndexOf("fts5", StringComparison.OrdinalIgnoreCase) >= 0) ftsMode = "fts5";
                else if (existingFts.IndexOf("fts4", StringComparison.OrdinalIgnoreCase) >= 0) ftsMode = "fts4";
                else ftsMode = "plain";
                EnsureSessionFtsMatchingModeLocked();
                return;
            }

            if (TryExecLocked("CREATE VIRTUAL TABLE IF NOT EXISTS vault_fts USING fts5(doc_id UNINDEXED, file_name, sheet_name, content);")
                && TryExecLocked("CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts5(msg_id UNINDEXED, chat_id UNINDEXED, scope UNINDEXED, text);"))
            {
                ftsMode = "fts5";
                return;
            }

            ExecSilentLocked("DROP TABLE IF EXISTS vault_fts; DROP TABLE IF EXISTS session_fts;");
            if (TryExecLocked("CREATE VIRTUAL TABLE IF NOT EXISTS vault_fts USING fts4(doc_id, file_name, sheet_name, content);")
                && TryExecLocked("CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts4(msg_id, chat_id, scope, text);"))
            {
                ftsMode = "fts4";
                return;
            }

            ExecSilentLocked("DROP TABLE IF EXISTS vault_fts; DROP TABLE IF EXISTS session_fts;");
            ExecLocked(
                "CREATE TABLE IF NOT EXISTS vault_fts (doc_id INTEGER, file_name TEXT, sheet_name TEXT, content TEXT);\n" +
                "CREATE INDEX IF NOT EXISTS idx_vault_fts_doc ON vault_fts(doc_id);\n" +
                "CREATE TABLE IF NOT EXISTS session_fts (msg_id INTEGER, chat_id TEXT, scope TEXT, text TEXT);\n" +
                "CREATE INDEX IF NOT EXISTS idx_session_fts_msg ON session_fts(msg_id);");
            ftsMode = "plain";
        }

        static void EnsureSessionFtsMatchingModeLocked()
        {
            string existingSess = ScalarTextLocked("SELECT sql FROM sqlite_master WHERE name='session_fts' LIMIT 1;");
            if (!string.IsNullOrEmpty(existingSess)) return;
            if (ftsMode == "fts5")
                ExecLocked("CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts5(msg_id UNINDEXED, chat_id UNINDEXED, scope UNINDEXED, text);");
            else if (ftsMode == "fts4")
                ExecLocked("CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts4(msg_id, chat_id, scope, text);");
            else
                ExecLocked("CREATE TABLE IF NOT EXISTS session_fts (msg_id INTEGER, chat_id TEXT, scope TEXT, text TEXT);");
        }

        // =====================================================================
        // Document Ingestion (Dynamic Per-Sheet SQL Tables + FTS)
        // =====================================================================

        public static Dictionary<string, object> IngestDoc(ExtractedDoc doc)
        {
            if (doc == null) throw new ArgumentNullException("doc");
            string normPath = NormalizePath(doc.Path);
            string fileName = Path.GetFileName(normPath);
            if (string.IsNullOrEmpty(fileName)) fileName = normPath;
            string kind = string.IsNullOrWhiteSpace(doc.Kind) ? Path.GetExtension(normPath).TrimStart('.').ToLowerInvariant() : doc.Kind.Trim().ToLowerInvariant();

            var tables = doc.Tables ?? new List<ExtractedTable>();
            var sections = doc.Sections ?? new List<ExtractedSection>();
            int totalRows = 0;
            for (int i = 0; i < tables.Count; i++)
                if (tables[i] != null && tables[i].Rows != null) totalRows += tables[i].Rows.Count;

            string summary = BuildSummary(fileName, kind, tables, sections, totalRows);
            string fullText = doc.FullText ?? "";
            if (fullText.Length > 250000) fullText = fullText.Substring(0, 250000);
            string extractedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

            lock (dbLock)
            {
                EnsureOpenLocked();
                ExecLocked("BEGIN IMMEDIATE;");
                try
                {
                    long docId = FindDocIdByPathLocked(normPath);
                    if (docId > 0)
                    {
                        DropDocDynamicTablesLocked(docId);
                        ExecParamLocked("DELETE FROM vault_tables WHERE doc_id = ?;", docId);
                        ExecParamLocked("DELETE FROM vault_fts WHERE doc_id = ?;", docId);
                        ExecParamLocked(
                            "UPDATE vault_docs SET path=?, file_name=?, kind=?, is_drm=?, file_size=?, modified_ticks=?, extracted_utc=?, sheet_count=?, row_count=?, summary=?, full_text=? WHERE id=?;",
                            normPath, fileName, kind, doc.IsDrm ? 1L : 0L, doc.FileSize, doc.ModifiedTicks, extractedUtc, (long)tables.Count, (long)totalRows, summary, fullText, docId);
                    }
                    else
                    {
                        ExecParamLocked(
                            "INSERT INTO vault_docs(path, file_name, kind, is_drm, file_size, modified_ticks, extracted_utc, sheet_count, row_count, summary, full_text) VALUES(?,?,?,?,?,?,?,?,?,?,?);",
                            normPath, fileName, kind, doc.IsDrm ? 1L : 0L, doc.FileSize, doc.ModifiedTicks, extractedUtc, (long)tables.Count, (long)totalRows, summary, fullText);
                        docId = sqlite3_last_insert_rowid(dbHandle);
                    }

                    var usedTableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var tablesOut = new List<Dictionary<string, object>>();

                    for (int tIdx = 0; tIdx < tables.Count; tIdx++)
                    {
                        var tbl = tables[tIdx];
                        if (tbl == null) continue;
                        string rawSheet = string.IsNullOrWhiteSpace(tbl.Name) ? ("sheet_" + (tIdx + 1)) : tbl.Name.Trim();
                        string slug = SanitizeIdentifier(rawSheet, "sheet_" + (tIdx + 1), "s_").ToLowerInvariant();
                        string baseSqlName = "data_" + docId.ToString(CultureInfo.InvariantCulture) + "_" + slug;
                        string sqlTable = baseSqlName;
                        int suffix = 2;
                        while (!usedTableNames.Add(sqlTable))
                        {
                            sqlTable = baseSqlName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                            suffix++;
                        }

                        var rawCols = tbl.Columns ?? new List<string>();
                        var rows = tbl.Rows ?? new List<List<string>>();
                        int colCount = rawCols.Count;
                        for (int r = 0; r < rows.Count; r++)
                            if (rows[r] != null && rows[r].Count > colCount) colCount = rows[r].Count;
                        if (colCount <= 0) colCount = 1;

                        var cols = new List<string>(colCount);
                        var usedCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (int c = 0; c < colCount; c++)
                        {
                            string hdr = (c < rawCols.Count && !string.IsNullOrWhiteSpace(rawCols[c])) ? rawCols[c] : ("col_" + (c + 1));
                            string colName = SanitizeIdentifier(hdr, "col_" + (c + 1), "c_");
                            string uniqueCol = colName;
                            int cs = 2;
                            while (!usedCols.Add(uniqueCol))
                            {
                                uniqueCol = colName + "_" + cs.ToString(CultureInfo.InvariantCulture);
                                cs++;
                            }
                            cols.Add(uniqueCol);
                        }

                        var createSql = new StringBuilder();
                        createSql.Append("CREATE TABLE \"").Append(sqlTable).Append("\" (");
                        for (int c = 0; c < cols.Count; c++)
                        {
                            if (c > 0) createSql.Append(", ");
                            createSql.Append('"').Append(cols[c]).Append('"');
                        }
                        createSql.Append(");");
                        ExecLocked(createSql.ToString());

                        var insSql = new StringBuilder();
                        insSql.Append("INSERT INTO \"").Append(sqlTable).Append("\" VALUES (");
                        for (int c = 0; c < cols.Count; c++)
                        {
                            if (c > 0) insSql.Append(", ");
                            insSql.Append('?');
                        }
                        insSql.Append(");");

                        var ftsChunk = new StringBuilder();
                        ftsChunk.AppendLine(string.Join(" | ", cols.ToArray()));

                        IntPtr stmt = PrepareLocked(insSql.ToString());
                        try
                        {
                            for (int r = 0; r < rows.Count; r++)
                            {
                                var row = rows[r];
                                sqlite3_reset(stmt);
                                sqlite3_clear_bindings(stmt);
                                for (int c = 0; c < cols.Count; c++)
                                {
                                    string cell = (row != null && c < row.Count) ? row[c] : null;
                                    BindSmartCell(stmt, c + 1, cell);
                                    if (ftsChunk.Length < 180000 && !string.IsNullOrEmpty(cell))
                                    {
                                        if (c > 0) ftsChunk.Append(" | ");
                                        ftsChunk.Append(cell);
                                    }
                                }
                                if (ftsChunk.Length < 180000) ftsChunk.Append('\n');
                                int stepRc = sqlite3_step(stmt);
                                if (stepRc != SQLITE_DONE)
                                    throw new IOException("Insert failed in " + sqlTable + ": " + GetError(dbHandle));
                            }
                        }
                        finally
                        {
                            sqlite3_finalize(stmt);
                        }

                        string colsJson = Js.Serialize(cols);
                        ExecParamLocked(
                            "INSERT INTO vault_tables(doc_id, sheet_name, sql_table, columns_json, row_count) VALUES(?,?,?,?,?);",
                            docId, rawSheet, sqlTable, colsJson, (long)rows.Count);

                        ExecParamLocked(
                            "INSERT INTO vault_fts(doc_id, file_name, sheet_name, content) VALUES(?,?,?,?);",
                            docId, fileName, rawSheet, ftsChunk.ToString());

                        tablesOut.Add(new Dictionary<string, object>
                        {
                            { "sheet_name", rawSheet },
                            { "sql_table", sqlTable },
                            { "columns", cols },
                            { "row_count", rows.Count }
                        });
                    }

                    if (sections.Count > 0)
                    {
                        for (int i = 0; i < sections.Count; i++)
                        {
                            var sec = sections[i];
                            if (sec == null || string.IsNullOrWhiteSpace(sec.Text)) continue;
                            string title = string.IsNullOrWhiteSpace(sec.Title) ? ("Section " + (i + 1)) : sec.Title;
                            string secText = sec.Text.Length > 100000 ? sec.Text.Substring(0, 100000) : sec.Text;
                            ExecParamLocked(
                                "INSERT INTO vault_fts(doc_id, file_name, sheet_name, content) VALUES(?,?,?,?);",
                                docId, fileName, title, secText);
                        }
                    }
                    else if (tables.Count == 0 && !string.IsNullOrWhiteSpace(fullText))
                    {
                        ExecParamLocked(
                            "INSERT INTO vault_fts(doc_id, file_name, sheet_name, content) VALUES(?,?,?,?);",
                            docId, fileName, kind, fullText);
                    }

                    ExecLocked("COMMIT;");

                    var result = new Dictionary<string, object>
                    {
                        { "doc_id", docId },
                        { "path", normPath },
                        { "file_name", fileName },
                        { "kind", kind },
                        { "is_drm", doc.IsDrm },
                        { "file_size", doc.FileSize },
                        { "modified_ticks", doc.ModifiedTicks },
                        { "extracted_utc", extractedUtc },
                        { "sheet_count", tables.Count },
                        { "row_count", totalRows },
                        { "summary", summary },
                        { "tables", tablesOut },
                        { "cached", false }
                    };
                    if (!string.IsNullOrEmpty(doc.Error)) result["warning"] = doc.Error;
                    return result;
                }
                catch
                {
                    ExecSilentLocked("ROLLBACK;");
                    throw;
                }
            }
        }

        // =====================================================================
        // Smart Cache & Lazy Background STA Queue
        // =====================================================================

        public static Dictionary<string, object> ExtractAndStore(string path, bool force = false, int maxRowsPerSheet = 25000)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is required.");
            string normPath = NormalizePath(path);
            if (!File.Exists(normPath)) throw new FileNotFoundException("File not found: " + normPath, normPath);

            var fi = new FileInfo(normPath);
            long size = fi.Length;
            long modTicks = fi.LastWriteTimeUtc.Ticks;

            if (!force)
            {
                var cached = TryGetCachedDoc(normPath, size, modTicks);
                if (cached != null) return cached;
            }

            ExtractedDoc doc = OfficeCom.ExtractFile(normPath, maxRowsPerSheet);
            if (!string.IsNullOrEmpty(doc.Error) && doc.Tables.Count == 0 && doc.Sections.Count == 0 && string.IsNullOrWhiteSpace(doc.FullText))
            {
                return new Dictionary<string, object>
                {
                    { "path", normPath },
                    { "file_name", Path.GetFileName(normPath) },
                    { "kind", doc.Kind ?? "" },
                    { "error", doc.Error },
                    { "cached", false }
                };
            }

            lock (dbLock)
            {
                fi.Refresh();
                if (!fi.Exists || fi.Length != size || fi.LastWriteTimeUtc.Ticks != modTicks)
                    return new Dictionary<string, object> { { "path", normPath }, { "error", "The document changed during extraction. Try again." }, { "cached", false } };
                doc.Path = normPath;
                doc.FileSize = size;
                doc.ModifiedTicks = modTicks;
                return IngestDoc(doc);
            }
        }

        public static void QueueExtract(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            string normPath = NormalizePath(path);
            if (!IsSupportedDocument(normPath)) return;
            try { if (!File.Exists(normPath)) return; }
            catch (Exception) { return; }

            lock (queueLock)
            {
                if (!inFlightPaths.Add(normPath)) return;
                queueStopping = false;
                extractQueue.Enqueue(normPath);
                if (extractWorker == null || !extractWorker.IsAlive)
                {
                    extractWorker = new Thread(ExtractQueueLoop)
                    {
                        IsBackground = true,
                        Name = "SqliteVault.ExtractQueue"
                    };
                    extractWorker.Start();
                }
                queueSignal.Set();
            }
        }

        static void ExtractQueueLoop()
        {
            while (true)
            {
                string next = null;
                lock (queueLock)
                {
                    if (queueStopping) return;
                    if (extractQueue.Count > 0) next = extractQueue.Dequeue();
                }

                if (next == null)
                {
                    queueSignal.WaitOne(5000);
                    lock (queueLock)
                    {
                        if (queueStopping) return;
                        if (extractQueue.Count == 0) continue;
                    }
                    continue;
                }

                try
                {
                    if (File.Exists(next))
                    {
                        var res = ExtractAndStore(next, false);
                        bool cached = res.ContainsKey("cached") && true.Equals(res["cached"]);
                        bool hasErr = res.ContainsKey("error") && !string.IsNullOrEmpty(Convert.ToString(res["error"], CultureInfo.InvariantCulture));
                        if (!cached && !hasErr)
                        {
                            string fn = Path.GetFileName(next);
                            int sheets = res.ContainsKey("sheet_count") ? Convert.ToInt32(res["sheet_count"], CultureInfo.InvariantCulture) : 0;
                            int rows = res.ContainsKey("row_count") ? Convert.ToInt32(res["row_count"], CultureInfo.InvariantCulture) : 0;
                            AssistantHarness.Report("Indexed " + fn + " (" + sheets + (sheets == 1 ? " table, " : " tables, ") + rows.ToString("N0", CultureInfo.InvariantCulture) + " rows) -> vault.db");
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Log("SqliteVault queue extract (" + Path.GetFileName(next) + "): " + ex.Message);
                }
                finally
                {
                    lock (queueLock) { inFlightPaths.Remove(next); }
                }
            }
        }

        static Dictionary<string, object> TryGetCachedDoc(string normPath, long size, long modTicks)
        {
            lock (dbLock)
            {
                EnsureOpenLocked();
                IntPtr stmt = PrepareLocked(
                    "SELECT id, path, file_name, kind, is_drm, file_size, modified_ticks, extracted_utc, sheet_count, row_count, summary " +
                    "FROM vault_docs WHERE LOWER(path) = LOWER(?) LIMIT 1;");
                try
                {
                    BindText(stmt, 1, normPath);
                    if (sqlite3_step(stmt) != SQLITE_ROW) return null;
                    long docId = sqlite3_column_int64(stmt, 0);
                    long dbSize = sqlite3_column_int64(stmt, 5);
                    long dbMod = sqlite3_column_int64(stmt, 6);
                    if (dbSize != size || dbMod != modTicks) return null;

                    var res = new Dictionary<string, object>
                    {
                        { "doc_id", docId },
                        { "path", ColText(stmt, 1) },
                        { "file_name", ColText(stmt, 2) },
                        { "kind", ColText(stmt, 3) },
                        { "is_drm", sqlite3_column_int64(stmt, 4) != 0 },
                        { "file_size", dbSize },
                        { "modified_ticks", dbMod },
                        { "extracted_utc", ColText(stmt, 7) },
                        { "sheet_count", (int)sqlite3_column_int64(stmt, 8) },
                        { "row_count", (int)sqlite3_column_int64(stmt, 9) },
                        { "summary", ColText(stmt, 10) },
                        { "cached", true }
                    };
                    res["tables"] = GetTablesForDocLocked(docId);
                    return res;
                }
                finally
                {
                    sqlite3_finalize(stmt);
                }
            }
        }

        // =====================================================================
        // Query, Schema & Full-Text Search
        // =====================================================================

        public static List<Dictionary<string, object>> ListDocs(string query = "", int limit = 50)
        {
            if (limit <= 0) limit = 50;
            if (limit > 500) limit = 500;
            string q = (query ?? "").Trim();

            lock (dbLock)
            {
                EnsureOpenLocked();
                var list = new List<Dictionary<string, object>>();
                string sql = q.Length == 0
                    ? "SELECT id, path, file_name, kind, is_drm, file_size, modified_ticks, extracted_utc, sheet_count, row_count, summary FROM vault_docs ORDER BY extracted_utc DESC, id DESC LIMIT ?;"
                    : "SELECT id, path, file_name, kind, is_drm, file_size, modified_ticks, extracted_utc, sheet_count, row_count, summary FROM vault_docs WHERE file_name LIKE ? OR path LIKE ? OR kind LIKE ? OR IFNULL(summary,'') LIKE ? ORDER BY extracted_utc DESC, id DESC LIMIT ?;";

                IntPtr stmt = PrepareLocked(sql);
                try
                {
                    if (q.Length == 0)
                    {
                        sqlite3_bind_int64(stmt, 1, limit);
                    }
                    else
                    {
                        string pat = "%" + q + "%";
                        BindText(stmt, 1, pat);
                        BindText(stmt, 2, pat);
                        BindText(stmt, 3, pat);
                        BindText(stmt, 4, pat);
                        sqlite3_bind_int64(stmt, 5, limit);
                    }

                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        long docId = sqlite3_column_int64(stmt, 0);
                        list.Add(new Dictionary<string, object>
                        {
                            { "doc_id", docId },
                            { "path", ColText(stmt, 1) },
                            { "file_name", ColText(stmt, 2) },
                            { "kind", ColText(stmt, 3) },
                            { "is_drm", sqlite3_column_int64(stmt, 4) != 0 },
                            { "file_size", sqlite3_column_int64(stmt, 5) },
                            { "modified_ticks", sqlite3_column_int64(stmt, 6) },
                            { "extracted_utc", ColText(stmt, 7) },
                            { "sheet_count", (int)sqlite3_column_int64(stmt, 8) },
                            { "row_count", (int)sqlite3_column_int64(stmt, 9) },
                            { "summary", ColText(stmt, 10) }
                        });
                    }
                }
                finally
                {
                    sqlite3_finalize(stmt);
                }

                foreach (var d in list)
                {
                    long docId = Convert.ToInt64(d["doc_id"], CultureInfo.InvariantCulture);
                    d["tables"] = GetTablesForDocLocked(docId);
                }
                return list;
            }
        }

        public static List<Dictionary<string, object>> GetSchema(long docId = 0)
        {
            lock (dbLock)
            {
                EnsureOpenLocked();
                var list = new List<Dictionary<string, object>>();
                string sql = docId > 0
                    ? "SELECT t.doc_id, d.file_name, d.path, t.sheet_name, t.sql_table, t.columns_json, t.row_count FROM vault_tables t JOIN vault_docs d ON d.id = t.doc_id WHERE t.doc_id = ? ORDER BY t.id;"
                    : "SELECT t.doc_id, d.file_name, d.path, t.sheet_name, t.sql_table, t.columns_json, t.row_count FROM vault_tables t JOIN vault_docs d ON d.id = t.doc_id ORDER BY t.doc_id DESC, t.id;";

                IntPtr stmt = PrepareLocked(sql);
                try
                {
                    if (docId > 0) sqlite3_bind_int64(stmt, 1, docId);
                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        list.Add(new Dictionary<string, object>
                        {
                            { "doc_id", sqlite3_column_int64(stmt, 0) },
                            { "file_name", ColText(stmt, 1) },
                            { "path", ColText(stmt, 2) },
                            { "sheet_name", ColText(stmt, 3) },
                            { "sql_table", ColText(stmt, 4) },
                            { "columns", ParseStringList(ColText(stmt, 5)) },
                            { "row_count", (int)sqlite3_column_int64(stmt, 6) }
                        });
                    }
                }
                finally
                {
                    sqlite3_finalize(stmt);
                }
                return list;
            }
        }

        public static Dictionary<string, object> QuerySql(string sql, int maxRows = 500)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("sql is required.");
            if (maxRows <= 0) maxRows = 500;
            if (maxRows > 10000) maxRows = 10000;

            lock (dbLock)
            {
                EnsureOpenLocked();
                return QuerySqlLocked(sql, maxRows);
            }
        }

        static Dictionary<string, object> QuerySqlLocked(string sql, int maxRows)
        {
            IntPtr stmt = PrepareLocked(sql);
            try
            {
                int colCount = sqlite3_column_count(stmt);
                var cols = new List<string>(colCount);
                for (int c = 0; c < colCount; c++)
                    cols.Add(PtrToUtf8(sqlite3_column_name(stmt, c), -1) ?? ("col_" + (c + 1)));

                var rows = new List<List<object>>();
                bool truncated = false;
                while (true)
                {
                    int rc = sqlite3_step(stmt);
                    if (rc == SQLITE_ROW)
                    {
                        if (rows.Count >= maxRows)
                        {
                            truncated = true;
                            break;
                        }
                        var row = new List<object>(colCount);
                        for (int c = 0; c < colCount; c++)
                            row.Add(ReadColumnValue(stmt, c));
                        rows.Add(row);
                    }
                    else if (rc == SQLITE_DONE)
                    {
                        break;
                    }
                    else
                    {
                        throw new IOException("SQL error (" + rc + "): " + GetError(dbHandle));
                    }
                }

                return new Dictionary<string, object>
                {
                    { "columns", cols },
                    { "rows", rows },
                    { "row_count", rows.Count },
                    { "truncated", truncated }
                };
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        public static List<Dictionary<string, object>> SearchVault(string query, int limit = 20)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0) return new List<Dictionary<string, object>>();
            if (limit <= 0) limit = 20;
            if (limit > 100) limit = 100;

            lock (dbLock)
            {
                EnsureOpenLocked();
                var results = new List<Dictionary<string, object>>();
                var tokens = ExtractTokens(q);
                if (tokens.Count == 0) return results;

                if (ftsMode == "fts5" || ftsMode == "fts4")
                {
                    string ftsQuery = BuildFtsQuery(tokens);
                    string sql = ftsMode == "fts5"
                        ? "SELECT f.doc_id, f.file_name, IFNULL(d.path,''), f.sheet_name, snippet(vault_fts, 3, '[', ']', '...', 18), bm25(vault_fts) " +
                          "FROM vault_fts f LEFT JOIN vault_docs d ON d.id = f.doc_id WHERE vault_fts MATCH ? ORDER BY bm25(vault_fts) LIMIT ?;"
                        : "SELECT f.doc_id, f.file_name, IFNULL(d.path,''), f.sheet_name, f.content, 1.0 " +
                          "FROM vault_fts f LEFT JOIN vault_docs d ON d.id = f.doc_id WHERE vault_fts MATCH ? LIMIT ?;";

                    try
                    {
                        IntPtr stmt = PrepareLocked(sql);
                        try
                        {
                            BindText(stmt, 1, ftsQuery);
                            sqlite3_bind_int64(stmt, 2, limit);
                            while (sqlite3_step(stmt) == SQLITE_ROW)
                            {
                                string snip = ColText(stmt, 4);
                                if (ftsMode != "fts5") snip = MakeSnippet(snip, tokens, 220);
                                double rawBm25 = sqlite3_column_double(stmt, 5);
                                double score = ftsMode == "fts5" ? Math.Round(1.0 + Math.Max(0.0, -rawBm25), 4) : 1.0;
                                results.Add(new Dictionary<string, object>
                                {
                                    { "doc_id", sqlite3_column_int64(stmt, 0) },
                                    { "file_name", ColText(stmt, 1) },
                                    { "path", ColText(stmt, 2) },
                                    { "sheet_name", ColText(stmt, 3) },
                                    { "snippet", snip },
                                    { "score", score }
                                });
                            }
                        }
                        finally
                        {
                            sqlite3_finalize(stmt);
                        }
                        if (results.Count > 0) return results;
                    }
                    catch (Exception)
                    {
                        // Fall through cleanly to LIKE search if FTS syntax rejects unusual characters.
                    }
                }

                // Fallback LIKE query across vault_fts
                var sb = new StringBuilder();
                sb.Append("SELECT f.doc_id, f.file_name, IFNULL(d.path,''), f.sheet_name, f.content FROM vault_fts f LEFT JOIN vault_docs d ON d.id = f.doc_id WHERE ");
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (i > 0) sb.Append(" AND ");
                    sb.Append("(f.content LIKE ? OR f.file_name LIKE ? OR f.sheet_name LIKE ?)");
                }
                sb.Append(" LIMIT ?;");

                IntPtr likeStmt = PrepareLocked(sb.ToString());
                try
                {
                    int p = 1;
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        string pat = "%" + tokens[i] + "%";
                        BindText(likeStmt, p++, pat);
                        BindText(likeStmt, p++, pat);
                        BindText(likeStmt, p++, pat);
                    }
                    sqlite3_bind_int64(likeStmt, p, limit);
                    while (sqlite3_step(likeStmt) == SQLITE_ROW)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            { "doc_id", sqlite3_column_int64(likeStmt, 0) },
                            { "file_name", ColText(likeStmt, 1) },
                            { "path", ColText(likeStmt, 2) },
                            { "sheet_name", ColText(likeStmt, 3) },
                            { "snippet", MakeSnippet(ColText(likeStmt, 4), tokens, 220) },
                            { "score", 1.0 }
                        });
                    }
                }
                finally
                {
                    sqlite3_finalize(likeStmt);
                }
                return results;
            }
        }

        // =====================================================================
        // Session Indexing + Hybrid Search (FTS5 + Temporal Decay + MMR Dedup)
        // =====================================================================

        public static long IndexSessionMessage(string chatId, string scope, string role, string agent, string text, long timeTicks)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return 0;
            string cleanRole = (role ?? "").Trim().ToLowerInvariant();
            if (cleanRole == "info" || cleanRole == "tool") return 0;

            string shown = ChatView.ShownText(text ?? "").Trim();
            if (shown.Length == 0) return 0;
            if (shown.Length > 80000) shown = shown.Substring(0, 80000);

            string cleanScope = string.IsNullOrWhiteSpace(scope) ? "agents" : scope.Trim().ToLowerInvariant();
            string cleanAgent = agent ?? "";
            long ticks = timeTicks > 0 ? timeTicks : Store.Now();

            lock (dbLock)
            {
                EnsureOpenLocked();
                return IndexSessionMessageLocked(chatId.Trim(), cleanScope, cleanRole, cleanAgent, shown, ticks);
            }
        }

        static long IndexSessionMessageLocked(string chatId, string scope, string role, string agent, string shownText, long ticks)
        {
            // Deduplicate exact repeat saves of the same message.
            IntPtr chk = PrepareLocked("SELECT id FROM session_msgs WHERE chat_id=? AND role=? AND IFNULL(agent,'')=? AND time_ticks=? AND text=? LIMIT 1;");
            try
            {
                BindText(chk, 1, chatId);
                BindText(chk, 2, role);
                BindText(chk, 3, agent);
                sqlite3_bind_int64(chk, 4, ticks);
                BindText(chk, 5, shownText);
                if (sqlite3_step(chk) == SQLITE_ROW) return sqlite3_column_int64(chk, 0);
            }
            finally
            {
                sqlite3_finalize(chk);
            }

            ExecParamLocked(
                "INSERT INTO session_msgs(chat_id, scope, role, agent, text, time_ticks) VALUES(?,?,?,?,?,?);",
                chatId, scope, role, agent, shownText, ticks);
            long msgId = sqlite3_last_insert_rowid(dbHandle);
            ExecParamLocked(
                "INSERT INTO session_fts(msg_id, chat_id, scope, text) VALUES(?,?,?,?);",
                msgId, chatId, scope, shownText);
            return msgId;
        }

        public static void IndexChatAsync(AgentChat chat)
        {
            if (chat == null || string.IsNullOrWhiteSpace(chat.Id) || chat.Messages == null || chat.Messages.Count == 0)
                return;

            string chatId = chat.Id;
            string scope = AssistantHarness.IsAssistant(chat) ? "assistant" : "agents";
            string targetDb = DbPath;

            // Snapshot new messages safely on caller thread (UI thread safe, zero lock stalls).
            int startAt = 0;
            lock (dbLock)
            {
                if (string.Equals(openPath, targetDb, StringComparison.OrdinalIgnoreCase))
                {
                    int prev;
                    if (indexedChatCounts.TryGetValue(chatId, out prev) && prev >= 0 && prev <= chat.Messages.Count)
                        startAt = prev;
                }
            }
            if (startAt >= chat.Messages.Count) return;

            var batch = new List<Tuple<string, string, string, long>>();
            int totalCount = chat.Messages.Count;
            for (int i = startAt; i < totalCount; i++)
            {
                var m = chat.Messages[i];
                if (m == null) continue;
                string r = (m.Role ?? "").Trim().ToLowerInvariant();
                if (r != "user" && r != "agent") continue;
                string shown = ChatView.ShownText(m.Text ?? "").Trim();
                if (shown.Length == 0) continue;
                if (shown.Length > 80000) shown = shown.Substring(0, 80000);
                batch.Add(Tuple.Create(r, m.Agent ?? "", shown, m.Time > 0 ? m.Time : Store.Now()));
            }
            if (batch.Count == 0) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    // Skip if Store.Dir already moved away (e.g., temporary test fixture cleaned up).
                    if (!string.Equals(DbPath, targetDb, StringComparison.OrdinalIgnoreCase)) return;
                    string dir = Path.GetDirectoryName(targetDb);
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

                    lock (dbLock)
                    {
                        if (!string.Equals(DbPath, targetDb, StringComparison.OrdinalIgnoreCase)) return;
                        EnsureOpenLocked();
                        ExecLocked("BEGIN IMMEDIATE;");
                        try
                        {
                            for (int i = 0; i < batch.Count; i++)
                            {
                                var item = batch[i];
                                IndexSessionMessageLocked(chatId, scope, item.Item1, item.Item2, item.Item3, item.Item4);
                            }
                            ExecLocked("COMMIT;");
                            indexedChatCounts[chatId] = totalCount;
                        }
                        catch
                        {
                            ExecSilentLocked("ROLLBACK;");
                            throw;
                        }
                    }
                }
                catch (Exception)
                {
                    // Never crash background indexing if test folder was deleted mid-flight.
                }
            });
        }

        class SessionCandidate
        {
            public long MsgId;
            public string ChatId, Scope, Role, Agent, Text;
            public long TimeTicks;
            public double Score, AgeDays;
            public HashSet<string> Tokens;
        }

        public static List<Dictionary<string, object>> SearchSessions(string query, string scope = "", int limit = 15)
        {
            string q = (query ?? "").Trim();
            if (q.Length == 0) return new List<Dictionary<string, object>>();
            if (limit <= 0) limit = 15;
            if (limit > 100) limit = 100;
            string cleanScope = (scope ?? "").Trim().ToLowerInvariant();
            if (cleanScope == "all" || cleanScope == "*") cleanScope = "";

            var tokens = ExtractTokens(q);
            if (tokens.Count == 0) return new List<Dictionary<string, object>>();

            int fetchLimit = Math.Max(60, limit * 4);
            var candidates = new List<SessionCandidate>();
            long nowTicks = Store.Now();

            lock (dbLock)
            {
                EnsureOpenLocked();
                bool ftsOk = false;

                if (ftsMode == "fts5" || ftsMode == "fts4")
                {
                    string ftsQuery = BuildFtsQuery(tokens);
                    string scopeFilter = cleanScope.Length > 0 ? " AND m.scope = ? " : " ";
                    string sql = ftsMode == "fts5"
                        ? "SELECT m.id, m.chat_id, m.scope, m.role, IFNULL(m.agent,''), m.text, m.time_ticks, bm25(session_fts) " +
                          "FROM session_fts f JOIN session_msgs m ON m.id = f.msg_id WHERE session_fts MATCH ?" + scopeFilter + "ORDER BY bm25(session_fts) LIMIT ?;"
                        : "SELECT m.id, m.chat_id, m.scope, m.role, IFNULL(m.agent,''), m.text, m.time_ticks, 0.0 " +
                          "FROM session_fts f JOIN session_msgs m ON m.id = f.msg_id WHERE session_fts MATCH ?" + scopeFilter + "LIMIT ?;";

                    try
                    {
                        IntPtr stmt = PrepareLocked(sql);
                        try
                        {
                            int p = 1;
                            BindText(stmt, p++, ftsQuery);
                            if (cleanScope.Length > 0) BindText(stmt, p++, cleanScope);
                            sqlite3_bind_int64(stmt, p, fetchLimit);

                            while (sqlite3_step(stmt) == SQLITE_ROW)
                            {
                                string txt = ColText(stmt, 5);
                                long ticks = sqlite3_column_int64(stmt, 6);
                                double rawBm25 = sqlite3_column_double(stmt, 7);
                                double baseScore = ftsMode == "fts5"
                                    ? (1.0 + Math.Max(0.0, -rawBm25))
                                    : (1.0 + CountTokenMatches(txt, tokens));
                                double ageDays = Math.Max(0.0, (nowTicks - ticks) / (double)TimeSpan.TicksPerDay);
                                double decayed = baseScore * Math.Exp(-ageDays / 30.0);

                                candidates.Add(new SessionCandidate
                                {
                                    MsgId = sqlite3_column_int64(stmt, 0),
                                    ChatId = ColText(stmt, 1),
                                    Scope = ColText(stmt, 2),
                                    Role = ColText(stmt, 3),
                                    Agent = ColText(stmt, 4),
                                    Text = txt,
                                    TimeTicks = ticks,
                                    Score = decayed,
                                    AgeDays = ageDays,
                                    Tokens = TokenSet(txt)
                                });
                            }
                            ftsOk = candidates.Count > 0;
                        }
                        finally
                        {
                            sqlite3_finalize(stmt);
                        }
                    }
                    catch (Exception)
                    {
                        ftsOk = false;
                    }
                }

                if (!ftsOk)
                {
                    var sb = new StringBuilder();
                    sb.Append("SELECT id, chat_id, scope, role, IFNULL(agent,''), text, time_ticks FROM session_msgs WHERE ");
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        if (i > 0) sb.Append(" AND ");
                        sb.Append("text LIKE ?");
                    }
                    if (cleanScope.Length > 0) sb.Append(" AND scope = ?");
                    sb.Append(" ORDER BY time_ticks DESC LIMIT ?;");

                    IntPtr stmt = PrepareLocked(sb.ToString());
                    try
                    {
                        int p = 1;
                        for (int i = 0; i < tokens.Count; i++) BindText(stmt, p++, "%" + tokens[i] + "%");
                        if (cleanScope.Length > 0) BindText(stmt, p++, cleanScope);
                        sqlite3_bind_int64(stmt, p, fetchLimit);

                        while (sqlite3_step(stmt) == SQLITE_ROW)
                        {
                            string txt = ColText(stmt, 5);
                            long ticks = sqlite3_column_int64(stmt, 6);
                            double baseScore = 1.0 + CountTokenMatches(txt, tokens);
                            double ageDays = Math.Max(0.0, (nowTicks - ticks) / (double)TimeSpan.TicksPerDay);
                            double decayed = baseScore * Math.Exp(-ageDays / 30.0);
                            candidates.Add(new SessionCandidate
                            {
                                MsgId = sqlite3_column_int64(stmt, 0),
                                ChatId = ColText(stmt, 1),
                                Scope = ColText(stmt, 2),
                                Role = ColText(stmt, 3),
                                Agent = ColText(stmt, 4),
                                Text = txt,
                                TimeTicks = ticks,
                                Score = decayed,
                                AgeDays = ageDays,
                                Tokens = TokenSet(txt)
                            });
                        }
                    }
                    finally
                    {
                        sqlite3_finalize(stmt);
                    }
                }
            }

            // Rank by temporal-decayed relevance score descending, then apply MMR Jaccard dedup (> 0.75 overlap).
            candidates.Sort(delegate(SessionCandidate a, SessionCandidate b)
            {
                int cmp = b.Score.CompareTo(a.Score);
                if (cmp != 0) return cmp;
                return b.TimeTicks.CompareTo(a.TimeTicks);
            });

            var selected = new List<SessionCandidate>();
            for (int i = 0; i < candidates.Count && selected.Count < limit; i++)
            {
                var cand = candidates[i];
                bool redundant = false;
                for (int j = 0; j < selected.Count; j++)
                {
                    if (JaccardSimilarity(cand.Tokens, selected[j].Tokens) > 0.75)
                    {
                        redundant = true;
                        break;
                    }
                }
                if (!redundant) selected.Add(cand);
            }

            var output = new List<Dictionary<string, object>>(selected.Count);
            for (int i = 0; i < selected.Count; i++)
            {
                var s = selected[i];
                output.Add(new Dictionary<string, object>
                {
                    { "msg_id", s.MsgId },
                    { "chat_id", s.ChatId },
                    { "scope", s.Scope },
                    { "role", s.Role },
                    { "agent", s.Agent },
                    { "text", s.Text },
                    { "snippet", MakeSnippet(s.Text, tokens, 240) },
                    { "time_ticks", s.TimeTicks },
                    { "age_days", Math.Round(s.AgeDays, 2) },
                    { "score", Math.Round(s.Score, 4) }
                });
            }
            return output;
        }

        // =====================================================================
        // Event-Sourced Step Log (step_events + ForkAt)
        // =====================================================================

        public static long RecordStep(string chatId, int turnIdx, int stepIdx, string kind, string toolName, string payloadJson, long timeTicks = 0)
        {
            if (string.IsNullOrWhiteSpace(chatId)) throw new ArgumentException("chatId is required.");
            long ticks = timeTicks > 0 ? timeTicks : Store.Now();
            lock (dbLock)
            {
                EnsureOpenLocked();
                ExecParamLocked(
                    "INSERT INTO step_events(chat_id, turn_idx, step_idx, kind, tool_name, payload_json, time_ticks) VALUES(?,?,?,?,?,?,?);",
                    chatId.Trim(), (long)turnIdx, (long)stepIdx, kind ?? "event", toolName ?? "", payloadJson ?? "", ticks);
                return sqlite3_last_insert_rowid(dbHandle);
            }
        }

        public static List<Dictionary<string, object>> GetSteps(string chatId, int limit = 200)
        {
            if (string.IsNullOrWhiteSpace(chatId)) return new List<Dictionary<string, object>>();
            if (limit <= 0) limit = 200;
            if (limit > 2000) limit = 2000;

            lock (dbLock)
            {
                EnsureOpenLocked();
                var list = new List<Dictionary<string, object>>();
                IntPtr stmt = PrepareLocked(
                    "SELECT id, chat_id, turn_idx, step_idx, kind, IFNULL(tool_name,''), IFNULL(payload_json,''), time_ticks " +
                    "FROM step_events WHERE chat_id = ? ORDER BY id ASC LIMIT ?;");
                try
                {
                    BindText(stmt, 1, chatId.Trim());
                    sqlite3_bind_int64(stmt, 2, limit);
                    while (sqlite3_step(stmt) == SQLITE_ROW)
                    {
                        list.Add(new Dictionary<string, object>
                        {
                            { "id", sqlite3_column_int64(stmt, 0) },
                            { "chat_id", ColText(stmt, 1) },
                            { "turn_idx", (int)sqlite3_column_int64(stmt, 2) },
                            { "step_idx", (int)sqlite3_column_int64(stmt, 3) },
                            { "kind", ColText(stmt, 4) },
                            { "tool_name", ColText(stmt, 5) },
                            { "payload_json", ColText(stmt, 6) },
                            { "time_ticks", sqlite3_column_int64(stmt, 7) }
                        });
                    }
                }
                finally
                {
                    sqlite3_finalize(stmt);
                }
                return list;
            }
        }

        public static Dictionary<string, object> ForkAt(string chatId, long stepId, string newChatId = null)
        {
            if (string.IsNullOrWhiteSpace(chatId)) throw new ArgumentException("chatId is required.");
            string src = chatId.Trim();
            string dst = string.IsNullOrWhiteSpace(newChatId)
                ? (src + "-fork-" + Guid.NewGuid().ToString("N").Substring(0, 6))
                : newChatId.Trim();

            lock (dbLock)
            {
                EnsureOpenLocked();
                ExecLocked("BEGIN IMMEDIATE;");
                try
                {
                    var copied = new List<Tuple<long, long, string, string, string, long>>();
                    IntPtr sel = PrepareLocked(
                        "SELECT turn_idx, step_idx, kind, IFNULL(tool_name,''), IFNULL(payload_json,''), time_ticks " +
                        "FROM step_events WHERE chat_id = ? AND id <= ? ORDER BY id ASC;");
                    try
                    {
                        BindText(sel, 1, src);
                        sqlite3_bind_int64(sel, 2, stepId);
                        while (sqlite3_step(sel) == SQLITE_ROW)
                        {
                            copied.Add(Tuple.Create(
                                sqlite3_column_int64(sel, 0),
                                sqlite3_column_int64(sel, 1),
                                ColText(sel, 2),
                                ColText(sel, 3),
                                ColText(sel, 4),
                                sqlite3_column_int64(sel, 5)));
                        }
                    }
                    finally
                    {
                        sqlite3_finalize(sel);
                    }

                    IntPtr ins = PrepareLocked(
                        "INSERT INTO step_events(chat_id, turn_idx, step_idx, kind, tool_name, payload_json, time_ticks) VALUES(?,?,?,?,?,?,?);");
                    try
                    {
                        for (int i = 0; i < copied.Count; i++)
                        {
                            var row = copied[i];
                            sqlite3_reset(ins);
                            sqlite3_clear_bindings(ins);
                            BindText(ins, 1, dst);
                            sqlite3_bind_int64(ins, 2, row.Item1);
                            sqlite3_bind_int64(ins, 3, row.Item2);
                            BindText(ins, 4, row.Item3);
                            BindText(ins, 5, row.Item4);
                            BindText(ins, 6, row.Item5);
                            sqlite3_bind_int64(ins, 7, row.Item6);
                            if (sqlite3_step(ins) != SQLITE_DONE)
                                throw new IOException("Fork insert failed: " + GetError(dbHandle));
                        }
                    }
                    finally
                    {
                        sqlite3_finalize(ins);
                    }

                    ExecLocked("COMMIT;");
                    return new Dictionary<string, object>
                    {
                        { "source_chat_id", src },
                        { "new_chat_id", dst },
                        { "fork_step_id", stepId },
                        { "copied_steps", copied.Count }
                    };
                }
                catch
                {
                    ExecSilentLocked("ROLLBACK;");
                    throw;
                }
            }
        }

        // =====================================================================
        // Alert Deduplication (alert_dedup)
        // =====================================================================

        public static bool HasAlerted(string hash, TimeSpan cooldown)
        {
            if (string.IsNullOrWhiteSpace(hash)) return false;
            lock (dbLock)
            {
                EnsureOpenLocked();
                IntPtr stmt = PrepareLocked("SELECT last_seen_utc FROM alert_dedup WHERE hash = ? LIMIT 1;");
                try
                {
                    BindText(stmt, 1, hash.Trim());
                    if (sqlite3_step(stmt) != SQLITE_ROW) return false;
                    string lastStr = ColText(stmt, 0);
                    DateTime lastUtc;
                    if (!DateTime.TryParse(lastStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out lastUtc))
                        return true;
                    return (DateTime.UtcNow - lastUtc) < cooldown;
                }
                finally
                {
                    sqlite3_finalize(stmt);
                }
            }
        }

        public static void MarkAlerted(string hash, string source)
        {
            if (string.IsNullOrWhiteSpace(hash)) return;
            string nowUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            lock (dbLock)
            {
                EnsureOpenLocked();
                ExecParamLocked(
                    "INSERT INTO alert_dedup(hash, source, first_seen_utc, last_seen_utc) VALUES(?,?,?,?) " +
                    "ON CONFLICT(hash) DO UPDATE SET source=excluded.source, last_seen_utc=excluded.last_seen_utc;",
                    hash.Trim(), source ?? "system", nowUtc, nowUtc);
            }
        }

        // =====================================================================
        // Internal SQLite Helpers
        // =====================================================================

        static long FindDocIdByPathLocked(string normPath)
        {
            IntPtr stmt = PrepareLocked("SELECT id FROM vault_docs WHERE LOWER(path) = LOWER(?) LIMIT 1;");
            try
            {
                BindText(stmt, 1, normPath);
                if (sqlite3_step(stmt) == SQLITE_ROW) return sqlite3_column_int64(stmt, 0);
                return 0;
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        static void DropDocDynamicTablesLocked(long docId)
        {
            var toDrop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IntPtr s1 = PrepareLocked("SELECT sql_table FROM vault_tables WHERE doc_id = ?;");
            try
            {
                sqlite3_bind_int64(s1, 1, docId);
                while (sqlite3_step(s1) == SQLITE_ROW)
                {
                    string tn = ColText(s1, 0);
                    if (!string.IsNullOrWhiteSpace(tn)) toDrop.Add(tn);
                }
            }
            finally
            {
                sqlite3_finalize(s1);
            }

            string prefix = "data_" + docId.ToString(CultureInfo.InvariantCulture) + "_";
            IntPtr s2 = PrepareLocked("SELECT name FROM sqlite_master WHERE type='table' AND name LIKE ?;");
            try
            {
                BindText(s2, 1, prefix + "%");
                while (sqlite3_step(s2) == SQLITE_ROW)
                {
                    string tn = ColText(s2, 0);
                    if (!string.IsNullOrWhiteSpace(tn)) toDrop.Add(tn);
                }
            }
            finally
            {
                sqlite3_finalize(s2);
            }

            foreach (string tn in toDrop)
            {
                if (Regex.IsMatch(tn, @"^[A-Za-z0-9_]+$"))
                    ExecLocked("DROP TABLE IF EXISTS \"" + tn + "\";");
            }
        }

        static List<Dictionary<string, object>> GetTablesForDocLocked(long docId)
        {
            var list = new List<Dictionary<string, object>>();
            IntPtr stmt = PrepareLocked("SELECT sheet_name, sql_table, columns_json, row_count FROM vault_tables WHERE doc_id = ? ORDER BY id;");
            try
            {
                sqlite3_bind_int64(stmt, 1, docId);
                while (sqlite3_step(stmt) == SQLITE_ROW)
                {
                    list.Add(new Dictionary<string, object>
                    {
                        { "sheet_name", ColText(stmt, 0) },
                        { "sql_table", ColText(stmt, 1) },
                        { "columns", ParseStringList(ColText(stmt, 2)) },
                        { "row_count", (int)sqlite3_column_int64(stmt, 3) }
                    });
                }
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
            return list;
        }

        static void ExecLocked(string sql)
        {
            IntPtr errPtr;
            int rc = sqlite3_exec(dbHandle, ToUtf8Z(sql), IntPtr.Zero, IntPtr.Zero, out errPtr);
            if (rc != SQLITE_OK)
            {
                string msg = errPtr != IntPtr.Zero ? PtrToUtf8(errPtr, -1) : GetError(dbHandle);
                if (errPtr != IntPtr.Zero) sqlite3_free(errPtr);
                throw new IOException("SQLite exec error (" + rc + "): " + msg);
            }
            if (errPtr != IntPtr.Zero) sqlite3_free(errPtr);
        }

        static bool TryExecLocked(string sql)
        {
            IntPtr errPtr;
            int rc = sqlite3_exec(dbHandle, ToUtf8Z(sql), IntPtr.Zero, IntPtr.Zero, out errPtr);
            if (errPtr != IntPtr.Zero) sqlite3_free(errPtr);
            return rc == SQLITE_OK;
        }

        static void ExecSilentLocked(string sql)
        {
            try { TryExecLocked(sql); }
            catch (Exception) { }
        }

        static IntPtr PrepareLocked(string sql)
        {
            byte[] bytes = ToUtf8Z(sql);
            IntPtr stmt, tail;
            int rc = sqlite3_prepare_v2(dbHandle, bytes, -1, out stmt, out tail);
            if (rc != SQLITE_OK || stmt == IntPtr.Zero)
                throw new IOException("SQLite prepare error (" + rc + "): " + GetError(dbHandle));
            return stmt;
        }

        static void ExecParamLocked(string sql, params object[] values)
        {
            IntPtr stmt = PrepareLocked(sql);
            try
            {
                for (int i = 0; i < values.Length; i++)
                    BindObject(stmt, i + 1, values[i]);
                int rc = sqlite3_step(stmt);
                if (rc != SQLITE_DONE && rc != SQLITE_ROW)
                    throw new IOException("SQLite step error (" + rc + "): " + GetError(dbHandle));
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        static string ScalarTextLocked(string sql)
        {
            IntPtr stmt = PrepareLocked(sql);
            try
            {
                if (sqlite3_step(stmt) == SQLITE_ROW) return ColText(stmt, 0);
                return null;
            }
            finally
            {
                sqlite3_finalize(stmt);
            }
        }

        static void BindObject(IntPtr stmt, int idx, object val)
        {
            if (val == null) { sqlite3_bind_null(stmt, idx); return; }
            if (val is long) { sqlite3_bind_int64(stmt, idx, (long)val); return; }
            if (val is int) { sqlite3_bind_int64(stmt, idx, (int)val); return; }
            if (val is double) { sqlite3_bind_double(stmt, idx, (double)val); return; }
            if (val is float) { sqlite3_bind_double(stmt, idx, (float)val); return; }
            if (val is bool) { sqlite3_bind_int64(stmt, idx, ((bool)val) ? 1L : 0L); return; }
            BindText(stmt, idx, Convert.ToString(val, CultureInfo.InvariantCulture));
        }

        static void BindSmartCell(IntPtr stmt, int idx, string cell)
        {
            if (cell == null) { sqlite3_bind_null(stmt, idx); return; }
            string trimmed = cell.Trim();
            if (trimmed.Length == 0)
            {
                BindText(stmt, idx, "");
                return;
            }
            // Preserve leading-zero identifiers like "00123" as TEXT.
            bool leadingZero = trimmed.Length > 1 && trimmed[0] == '0' && trimmed[1] != '.';
            bool negLeadingZero = trimmed.Length > 2 && trimmed[0] == '-' && trimmed[1] == '0' && trimmed[2] != '.';
            if (!leadingZero && !negLeadingZero)
            {
                long iv;
                if (long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out iv))
                {
                    sqlite3_bind_int64(stmt, idx, iv);
                    return;
                }
                double dv;
                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out dv) && !double.IsNaN(dv) && !double.IsInfinity(dv))
                {
                    sqlite3_bind_double(stmt, idx, dv);
                    return;
                }
            }
            BindText(stmt, idx, cell);
        }

        static void BindText(IntPtr stmt, int idx, string val)
        {
            if (val == null) { sqlite3_bind_null(stmt, idx); return; }
            if (val.Length == 0)
            {
                sqlite3_bind_text(stmt, idx, new byte[] { 0 }, 0, SQLITE_TRANSIENT);
                return;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(val);
            sqlite3_bind_text(stmt, idx, bytes, bytes.Length, SQLITE_TRANSIENT);
        }

        static object ReadColumnValue(IntPtr stmt, int col)
        {
            int t = sqlite3_column_type(stmt, col);
            switch (t)
            {
                case SQLITE_NULL: return null;
                case SQLITE_INTEGER: return sqlite3_column_int64(stmt, col);
                case SQLITE_FLOAT: return sqlite3_column_double(stmt, col);
                default: return ColText(stmt, col);
            }
        }

        static string ColText(IntPtr stmt, int col)
        {
            if (sqlite3_column_type(stmt, col) == SQLITE_NULL) return "";
            IntPtr ptr = sqlite3_column_text(stmt, col);
            int len = sqlite3_column_bytes(stmt, col);
            return PtrToUtf8(ptr, len) ?? "";
        }

        static byte[] ToUtf8Z(string s)
        {
            if (string.IsNullOrEmpty(s)) return new byte[] { 0 };
            byte[] raw = Encoding.UTF8.GetBytes(s);
            byte[] z = new byte[raw.Length + 1];
            Buffer.BlockCopy(raw, 0, z, 0, raw.Length);
            z[raw.Length] = 0;
            return z;
        }

        static string PtrToUtf8(IntPtr ptr, int byteLen)
        {
            if (ptr == IntPtr.Zero) return null;
            int len = byteLen;
            if (len < 0)
            {
                len = 0;
                while (Marshal.ReadByte(ptr, len) != 0) len++;
            }
            if (len == 0) return "";
            byte[] buf = new byte[len];
            Marshal.Copy(ptr, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        static string GetError(IntPtr db)
        {
            if (db == IntPtr.Zero) return "Database handle is closed.";
            return PtrToUtf8(sqlite3_errmsg(db), -1) ?? "Unknown SQLite error.";
        }

        static string NormalizePath(string path)
        {
            string p = (path ?? "").Trim();
            if (p.Length == 0) return "";
            try { return Path.GetFullPath(p); }
            catch (Exception) { return p; }
        }

        public static string SanitizeIdentifier(string raw, string fallback, string digitPrefix)
        {
            string s = (raw ?? "").Trim();
            s = Regex.Replace(s, @"[^A-Za-z0-9_]+", "_");
            s = Regex.Replace(s, @"_+", "_").Trim('_');
            if (s.Length == 0) s = fallback;
            if (s.Length > 48) s = s.Substring(0, 48).TrimEnd('_');
            if (s.Length == 0) s = fallback;
            if (char.IsDigit(s[0])) s = digitPrefix + s;
            return s;
        }

        static string BuildSummary(string fileName, string kind, List<ExtractedTable> tables, List<ExtractedSection> sections, int totalRows)
        {
            var parts = new List<string>();
            parts.Add(fileName);
            if (!string.IsNullOrEmpty(kind)) parts.Add(kind);
            if (tables.Count > 0)
            {
                parts.Add(tables.Count + (tables.Count == 1 ? " table" : " tables"));
                parts.Add(totalRows.ToString("N0", CultureInfo.InvariantCulture) + " rows");
                if (tables[0] != null && tables[0].Columns != null && tables[0].Columns.Count > 0)
                    parts.Add("cols: " + string.Join(", ", tables[0].Columns.Take(6).ToArray()));
            }
            else if (sections.Count > 0)
            {
                parts.Add(sections.Count + (sections.Count == 1 ? " section" : " sections"));
            }
            return string.Join(" · ", parts.ToArray());
        }

        static List<string> ParseStringList(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<string>();
            try
            {
                var arr = Js.Deserialize<List<string>>(json);
                return arr ?? new List<string>();
            }
            catch (Exception) { return new List<string>(); }
        }

        static List<string> ExtractTokens(string query)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in WordTokenRx.Matches(query ?? ""))
            {
                string tok = m.Value.Trim();
                if (tok.Length > 0 && seen.Add(tok)) list.Add(tok);
            }
            return list;
        }

        static string BuildFtsQuery(List<string> tokens)
        {
            var parts = new List<string>(tokens.Count);
            for (int i = 0; i < tokens.Count; i++)
            {
                string esc = tokens[i].Replace("\"", "");
                if (esc.Length > 0) parts.Add("\"" + esc + "\"*");
            }
            return string.Join(" ", parts.ToArray());
        }

        static int CountTokenMatches(string text, List<string> tokens)
        {
            if (string.IsNullOrEmpty(text) || tokens == null) return 0;
            int hits = 0;
            for (int i = 0; i < tokens.Count; i++)
                if (text.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0) hits++;
            return hits;
        }

        static HashSet<string> TokenSet(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(text)) return set;
            foreach (Match m in WordTokenRx.Matches(text))
            {
                string v = m.Value.ToLowerInvariant();
                if (v.Length >= 2) set.Add(v);
                if (set.Count >= 200) break;
            }
            return set;
        }

        static double JaccardSimilarity(HashSet<string> a, HashSet<string> b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0) return 0.0;
            int inter = 0;
            var small = a.Count <= b.Count ? a : b;
            var big = a.Count <= b.Count ? b : a;
            foreach (string tok in small)
                if (big.Contains(tok)) inter++;
            int union = a.Count + b.Count - inter;
            return union > 0 ? (double)inter / union : 0.0;
        }

        static string MakeSnippet(string text, List<string> tokens, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string flat = Regex.Replace(text, @"\s+", " ").Trim();
            if (flat.Length <= maxLen) return flat;
            int pos = -1;
            if (tokens != null)
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    int idx = flat.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && (pos < 0 || idx < pos)) pos = idx;
                }
            }
            if (pos < 0) return flat.Substring(0, maxLen) + "...";
            int start = Math.Max(0, pos - maxLen / 3);
            int take = Math.Min(maxLen, flat.Length - start);
            string slice = flat.Substring(start, take);
            if (start > 0) slice = "..." + slice;
            if (start + take < flat.Length) slice += "...";
            return slice;
        }
    }
}
