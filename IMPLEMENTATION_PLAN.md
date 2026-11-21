# All in One — Master Architecture & Agent Handoff Specification ("الـ Agent العام")

> **Single Source of Truth:** Always update THIS root file (`IMPLEMENTATION_PLAN.md`) after every phase or architectural change. Never duplicate the master plan elsewhere.
>
> **Target Audience:** Designed both for the owner (clear Arabic overview + [docs/architecture-visual.html](docs/architecture-visual.html)) AND as a **complete, self-contained engineering handoff blueprint** so any AI coding agent (**Claude Code / Opus 5.5 / Codex Sol 6.2 / Antigravity**) can resume mid-project with 100% fidelity to our vision, exact C# 5 APIs, SQLite schema, COM invariants, performance budget, and design craft standards.

---

## 0. End-State Vision & Non-Negotiable Owner Rules

### End-State Goal ("Zero-Exit Native Windows Super-Workspace")
Turn **All in One** into an ultra-fast, native Windows desktop workspace where the user **never needs to leave the app**, powered by an **Active, Proactive, Autonomous Companion ("الـ Agent العام")** docked on the right across every page — completely independent from the coding `Agents` page — that:
1. **Sees all 20 pages live** (`PageContext` + `app_screenshot`) and **flips pages autonomously** so the user watches it work alongside them.
2. **Supports `Solo`, `Council`, and `Leader Team`** right inside the side companion, with full MCP app access for every member.
3. **Controls Excel, Word, and PowerPoint live via COM** (100% compatible with **NASCA DRM** protected corporate files).
4. **Automatically extracts data** from every opened/previewed Office or PDF file in the background via COM into a permanent, ultra-fast native Windows **`SQLite` database (`data\vault.db`)**, enabling instant cross-report **`SQL JOIN`s** even after files are closed.
5. **Never forgets and improves itself** via a combined **Hermes Agent + OpenClaw + DeepSeek Harness** architecture (3-Tier Prompt Builder, Frozen Memory Snapshots, Daily Logs, `FTS5` + Temporal Decay + MMR session search, Self-Improving `SKILL.md`, Dynamic Tool Registry + Creator Mode `tool_create`, Context Compaction + Memory Flush, Auto Account/Model Fallback, `Cron` + `HEARTBEAT.md` + `GOALS.md` with 0-token pre-checks and alert deduplication).
6. **Renders studio-grade Visual Explanations & Live Canvas views** in the embedded `Browser` as its **primary Visual Communication Channel** (`SOUL.md` + `AssistantHarness.cs` + `canvas_update`, strictly following [skills/anti-slop-web-design/SKILL.md](skills/anti-slop-web-design/SKILL.md) — Obsidian Dark + Warm Drafting Paper, interactive SVG diagrams, animated flows, minimal Egyptian Arabic labels, ZERO AI slop!).
7. **Records any human workflow once (`WorkflowRecorder.cs`)** using lightweight Semantic UIA Events + Smart Keyframe JPEGs (NO heavy video) and compiles it into native COM/SQL/CDP/UIA code + `SKILL.md` + Dynamic Tool (**100% automated forever**).

### Non-Negotiable Rules
1. **Closed Corporate Network & Open Automation:** Runs inside an isolated corporate/factory local intranet behind a strict enterprise firewall. **NEVER** add MCP ACL lock-downs, confirmation prompts, Assistant restrictions, or `MdWeb`/`Mail` HTML sanitizers, and **NEVER** ask the owner to close them.
2. **Zero-Bloat & Self-Contained Single-EXE Budget:** Native **C# 5 / .NET Framework 4.8** (`csc.exe`, `/platform:x64`) + built-in Windows **`winsqlite3.dll`** ONLY. Every user-facing feature must work immediately when double-clicking `AllInOne.exe` on a clean PC with **zero extra installs** (verify all 6 embedded resources including real standalone `ffmpeg.exe.gz` — NEVER a Scoop `.shim` stub! — plus WebView2 DLLs, `marked.umd.js`, `ChatSlides.pptx`, and zero runtime dependency on Python/Node/Vector DBs).
3. **Automatic Documentation & GitHub `main` Sync:** Every agent MUST update `IMPLEMENTATION_PLAN.md`, `HISTORY.md`, `LESSONS.md`, relevant `skills/*/SKILL.md`, and `docs/architecture-visual.html` automatically, AND `git commit` + `git push origin main` after every verified phase/milestone so GitHub `main` always stays synced.
4. **C# 5 Compiler Guard:** NO `$""` string interpolation, NO `?.` null-conditional, NO `out var`, NO `=>` expression-bodied members, NO `nameof`, NO tuples, NO pattern matching.
5. **Anti-AI-Slop UI Craft:** Every generated HTML/Dashboard/Canvas MUST follow [skills/anti-slop-web-design/SKILL.md](skills/anti-slop-web-design/SKILL.md) (committed studio aesthetic, OKLCH palettes, custom interactive SVG/Canvas, spring-physics animations, minimal scannable text, mandatory critique pass).

---

## 1. Handoff Protocol & Where to Search (For Future Agents: Claude / Codex / Opus)

### Start-of-Session Checklist (Do First, Every Time)
1. Read: `AGENTS.md`, `CLAUDE.md`, top of `HISTORY.md`, `LESSONS.md`, and this `IMPLEMENTATION_PLAN.md`.
2. Read the 3 installed craft skills when relevant:
   - `skills/anti-slop-web-design/SKILL.md`
   - `skills/workflow-to-automation/SKILL.md`
   - `skills/proactive-companion/SKILL.md`
   - `skills/all-in-one-maintenance/SKILL.md`
3. Check current workspace state: `git status --short` and check the Phase Status table in Section 4 below.
4. Verify compiler baseline before editing: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-build.ps1`.

### External Research Reference Map (If an Agent Needs Deep Reference Patterns)
| Source Repo / System | Exact Modules & Patterns Adopted (Native C# Adaptation) | Explicitly Rejected (Zero-Bloat Rule) |
|---|---|---|
| **`github.com/NousResearch/hermes-agent`** | `agent/prompt_builder.py` (3-Tier `Stable→Context→Volatile`), `MEMORY.md`/`USER.md`/`SOUL.md` Frozen Snapshot + `memory_tool`, `state.db` `SQLite FTS5` `session_search`, `skill_manage` + Progressive Disclosure, `agent/context_compressor.py` (Memory Flush + boundary-safe compaction), `fallback_providers` (Auto Account/Model Fallback), `cron/scheduler.py` (`[SILENT]` pre-check), `delegate_task`, `todo` | External messaging gateways (Telegram/Slack), Docker/container sandboxes |
| **`github.com/openclaw/openclaw`** | `HEARTBEAT.md` + `HEARTBEAT_OK` (idle-only proactive checks), `memory/YYYY-MM-DD.md` (auto-load today + yesterday), `TOOLS.md` + Lean Subagents (strip persona/memory for workers), `BM25 + Temporal Decay + MMR` ranking, `sessions_spawn` (non-blocking async + `maxSpawnDepth=2`), `Live Canvas` (in embedded `WebView2` Browser) | Node.js Gateway Daemon (port 18789), local Vector DB / embedding models |
| **`github.com/deepseek-ai/deepseek-harness`** (`dsh`) | Dynamic Tool Registry (`data\assistant\tools\*.json` hot-loaded into MCP without rebuild), Creator Mode (`tool_create` agent builds/tests/registers tools in chat), Event-Sourced `Session→Turn→Step` log in SQLite (Fork/Rollback/Crash Recovery), Full-Duplex concurrent tool execution | Node.js / TypeScript `Cordis` runtime |
| **`github.com/microsoft/skill-recorder`** | Multi-signal lightweight capture (Window/App + UIA + URL + Clipboard + Low-rate Keyframe JPEGs) → Intent generalization to native code/COM/SQL/CDP + `SKILL.md` instead of brittle coordinate macros | Electron runtime, 30fps raw video encoding |
| **`github.com/obra/superpowers`** & **`github.com/anthropics/skills`** | Progressive-disclosure `SKILL.md` structure (`SKILL.md` + `references/` + `scripts/`), explicit plan → execute → `verification-before-completion` gates, studio-grade `frontend-design` anti-slop craft | — |
| **Impeccable** (Paul Bakaus) & **ui-ux-pro-max** | OKLCH perceptual palettes, deliberate typography, spring motion (`cubic-bezier(0.22,1,0.36,1)`), `/critique` → `/polish` self-review loop | Heavy external design frameworks when clean native HTML/SVG renders in <30ms |
| **2026 Proactive Agents** (OpenAI Dots, xAI Grok Bot, Claude Code Routines/Daemons) | Standing `GOALS.md`, 0-token local hash/SQL pre-check before waking any LLM, rolling `alert_dedup` hashes (never nag twice), two-clock scheduling (recurring routines + dynamic one-shot self-follow-ups) | Cloud VMs, mandatory human approval gates |

---

## 2. Detailed Technical Architecture & Code Blueprint

### Layer 1: Standalone "الـ Agent العام" + Deep Harness (`src/AssistantHarness.cs`, `src/Assistant.cs`, `src/Agents.cs`)
- **Status:** Phase 1 core DONE ✓; Phase 3 adds 3-Tier Prompt Builder + `AgentMemory.cs` + Compaction + Auto Fallback + `Lean Subagents`.
- **Chat Isolation:**
  - `AssistantHarness.StorageDir` = `<Store.Dir>\assistant` (default `I:\ALL In One\data\assistant`).
  - `AssistantHarness.ChatsDir` = `<Store.Dir>\assistant\chats`.
  - `AgentStore` routes `Collection == "Assistant"` saves/drafts/deletes to `AssistantHarness.ChatsDir`, reads both folders on load (dedup preferring assistant dir), and `ChatHistory` + `AgentsPage` exclude Assistant chats completely.
- **Modes (`Solo` / `Council` / `Leader`):**
  - `AssistantPanel` header exposes 1-click `Solo`, `Council`, `Leader`, `Team…`, `Workspace` buttons + live 1-line `Seeing: <page> · <detail>` status strip wired to `AssistantHarness.Activity`.
  - `ChatPane.StartMember()` wraps Council and Leader Team prompts via `AssistantHarness.WrapTeamPrompt(Chat, prompt)` so EVERY leader and worker inherits MCP + Live Context (with `Lean Subagents` mode in Phase 3 stripping `SOUL.md` from workers to save tokens).
- **3-Tier Prompt Assembly (Phase 3 upgrade inside `AssistantHarness.cs` — shared by Assistant AND `Agents` page opt-in):**
  1. **Tier 1 — Stable (Cache-Pinned):** Identity (`SOUL.md`, omitted for Lean Subagents) + Open-Automation rules + MCP tool registry summary + `TOOLS.md`.
  2. **Tier 2 — Project Context:** Auto-read `CLAUDE.md` / `AGENTS.md` from active folder + brief git branch/status when in a repo.
  3. **Tier 3 — Volatile:** Frozen Session Snapshot (`USER.md`, `MEMORY.md`, `GOALS.md`, today + yesterday `daily\YYYY-MM-DD.md`), compact Skills Index (name + 1-line description ONLY — `Progressive Disclosure`), active `todo_plan`, and live 20-page `PageContext`.
- **Smart Context Compaction + Auto Fallback (Phase 3):**
  - Before a chat exceeds ~75% context window (tracked via `AgentLimits` / message char budget): flush durable facts to `MEMORY.md` / daily log first, preserve last N turns (`protect_last_n = 6`), summarize middle turns safely without splitting tool call/result pairs.
  - Auto Fallback Chain: when `AgentRun` detects rate-limit / quota / transient failure (reusing `AgentLimits` + `AgentAccounts`), automatically retry on the configured fallback account or backup model while preserving conversation history.

### Layer 2: Live 20-Page Awareness & Autonomous Navigation (`src/MainForm.cs`, `src/Explorer.cs`, `src/DocumentsPage.cs`, `src/Browser.cs`)
- **Status:** Phase 1 DONE ✓ (Phase 4 adds `app_screenshot` + Explorer write ops + Browser CDP).
- `MainForm.PageContext()` reads **in-memory state ONLY** (zero disk/network/WMI stalls on UI thread!) across all 20 pages, always supplying cross-page state (`page`, `loaded_pages`, `documents_current`, `documents_tabs`, `documents_editing`, `explorer_folder`, `explorer_selected`, `browser_title`, `browser_url`, `markdown_note`).
- Navigation MCP tools (`open_page`, `document_open`, `document_switch`, `document_save`, `explorer_open`, `browser_open`, `markdown_open`, `terminal_open`) flip screens live and call `AssistantHarness.Report(...)`.

### Layer 3: Live Office COM Control + Background NASCA-Safe Extractor (`src/OfficeCom.cs`, `src/OfficeEdit.cs`)
- **Status:** Phase 2 IN PROGRESS.
- **Why COM:** Corporate files are **NASCA DRM** encrypted on disk (`<## NASCA` header). Calling Office via `COM` lets authorized Excel/Word/PowerPoint decrypt in RAM transparently.
- **Live Controls (operate on `OfficeEdit.ComApp` / `OfficeEdit.ComDoc`):**
  - Excel: `excel_info`, `excel_read` (single bulk `UsedRange.Value2` call; handle 1-based `object[,]` and single-cell scalar), `excel_write` (bulk 2D array; supports `=` formulas), `excel_formula`, `excel_sheet_add`, `excel_format` (RGB `#RRGGBB` → OLE BGR int, bold, number format, AutoFit).
  - Word: `word_read` (paragraphs + tables), `word_write` (`append`, `replace`, `insert_table`).
  - PowerPoint: `ppt_read` (slides, titles, texts, tables, speaker notes), `ppt_write` (edit or add slide).
- **Background Extractor (`OfficeCom.ExtractFile(path)`):**
  - Dedicated `ApartmentState.STA` worker thread with `CultureInfo("en-US")`.
  - Non-DRM `.csv` → direct RFC-4180 parser (zero COM).
  - Excel / DRM `.csv` → hidden `Excel.Application` (`Visible=false`, `DisplayAlerts=false`, `ScreenUpdating=false`, `EnableEvents=false`), ReadOnly open, bulk `UsedRange.Value2` per sheet, `Close(false)`, `Quit()`, `Marshal.FinalReleaseComObject`.
  - Word → hidden `Word.Application`, ReadOnly open, paragraphs + tables, `Close(0)`, `Quit(0)`.
  - PowerPoint → check `Process.GetProcessesByName("POWERPNT").Length == 0` BEFORE spawning (`startedByUs`); open `ReadOnly=-1, WithWindow=0`; close ONLY this presentation; `Quit()` ONLY if `startedByUs` and `Presentations.Count == 0`.
  - PDF → hidden `Word.Application` with `ConfirmConversions=false` AND PDF warning suppression (`DisableConvertPdfWarning` / strict STA timeout killing only the spawned hidden PID if a modal stalls) + clean fallback for image-only PDFs via `Snip.cs` Windows OCR.

### Layer 4: Native Windows `SQLite Vault` + Hermes/OpenClaw/DeepSeek Persistence (`src/SqliteVault.cs`, `src/AgentMemory.cs`, `src/CronEngine.cs`)
- **Status:** Phase 3 NEXT.
- **Zero-Dependency P/Invoke (`src/SqliteVault.cs`):**
  - Loads `C:\Windows\System32\winsqlite3.dll` (verified present on Windows 10/11, 0 KB exe overhead).
  - Database file: `<Store.Dir>\vault.db` (default `I:\ALL In One\data\vault.db` — persists permanently, automatically included in Backups & Move Data Folder).
  - Startup PRAGMAs: `PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA busy_timeout=5000;`.
  - **Core Schema:**
    ```sql
    CREATE TABLE IF NOT EXISTS vault_docs (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      path TEXT UNIQUE NOT NULL,
      file_name TEXT NOT NULL,
      kind TEXT NOT NULL,
      is_drm INTEGER NOT NULL DEFAULT 0,
      file_size INTEGER NOT NULL,
      modified_ticks INTEGER NOT NULL,
      extracted_utc TEXT NOT NULL,
      sheet_count INTEGER NOT NULL DEFAULT 0,
      row_count INTEGER NOT NULL DEFAULT 0,
      summary TEXT,
      full_text TEXT
    );
    CREATE TABLE IF NOT EXISTS vault_tables (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      doc_id INTEGER NOT NULL REFERENCES vault_docs(id) ON DELETE CASCADE,
      sheet_name TEXT NOT NULL,
      sql_table TEXT UNIQUE NOT NULL,
      columns_json TEXT NOT NULL,
      row_count INTEGER NOT NULL
    );
    CREATE VIRTUAL TABLE IF NOT EXISTS vault_fts USING fts5(doc_id UNINDEXED, file_name, sheet_name, content);
    CREATE TABLE IF NOT EXISTS session_msgs (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      chat_id TEXT NOT NULL,
      scope TEXT NOT NULL, -- 'assistant' | 'agents'
      role TEXT NOT NULL,
      agent TEXT,
      text TEXT NOT NULL,
      time_ticks INTEGER NOT NULL
    );
    CREATE VIRTUAL TABLE IF NOT EXISTS session_fts USING fts5(msg_id UNINDEXED, chat_id UNINDEXED, scope UNINDEXED, text);
    CREATE TABLE IF NOT EXISTS step_events (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      chat_id TEXT NOT NULL,
      turn_idx INTEGER NOT NULL,
      step_idx INTEGER NOT NULL,
      kind TEXT NOT NULL, -- 'user'|'tool_call'|'tool_result'|'agent'|'checkpoint'
      tool_name TEXT,
      payload_json TEXT,
      time_ticks INTEGER NOT NULL
    );
    CREATE TABLE IF NOT EXISTS alert_dedup (
      hash TEXT PRIMARY KEY,
      source TEXT NOT NULL,
      first_seen_utc TEXT NOT NULL,
      last_seen_utc TEXT NOT NULL
    );
    ```
  - **Real Per-Sheet SQL Tables:** Every extracted Excel sheet creates/replaces a sanitized SQL table `data_<docId>_<slug>` with typed/text columns (`TEXT` / numeric affinity) so the Assistant can run **arbitrary cross-report `SQL JOIN`s** (`SELECT a.*, b.* FROM data_1_sales a JOIN data_2_stock b ON a.sku = b.sku`) in < 5 ms even after files are closed!
  - **Auto-Extraction Hook:** `DocumentsPage.Show(path)` enqueues `SqliteVault.QueueExtract(path)` on a single background STA worker. If `vault_docs` already matches `(path, file_size, modified_ticks)`, returns immediately (0 ms, no Office launch!). Updates `AssistantHarness.Report("Indexed Report.xlsx (3 sheets, 1,420 rows) -> vault.db")`.
  - **Hybrid Ranking (`session_search`):** Query `session_fts` with `bm25(session_fts)` → apply **Temporal Decay** (`score * exp(-ageDays / 30.0)`) + **MMR deduplication** (Jaccard token similarity filter > 0.75) in C# (0 MB overhead!).
  - **MCP Tools (`ApiTools.cs`):**
    - Vault: `vault_list`, `vault_extract`, `vault_schema`, `vault_query` (full SQL including `JOIN`), `vault_search` (`FTS5`).
    - Memory & Skills (`AgentMemory.cs`): `memory_manage` (`read`/`add`/`replace`/`remove`/`daily_log`), `session_search`, `skill_manage` (`list`/`read`/`save`/`patch`/`delete`), `tool_create` / `tool_list` / `tool_delete` (Dynamic Tool Registry in `data\assistant\tools\*.json`, hot-registered into `ApiTools`!), `todo_plan`.
    - Proactive Engine (`CronEngine.cs`): `cron_manage` (`list`/`create`/`update`/`delete`/`run_now`/`follow_up`), `heartbeat_status` (`GOALS.md` + `HEARTBEAT.md` + `HEARTBEAT_OK` + `FileSystemWatcher` Event Hooks).

### Layer 5: `Live Canvas` Dashboards + `WorkflowRecorder` + Full PC Control (`src/DashboardGen.cs`, `src/WorkflowRecorder.cs`, `src/ApiTools.cs`)
- **Status:** `DashboardGen.cs` in Phase 3; `WorkflowRecorder.cs` + full PC control in Phase 4.
- **`src/DashboardGen.cs` (OpenClaw Live Canvas):**
  - Stores interactive HTML dashboards under `<Store.Dir>\dashboards\<slug>.html`.
  - MCP tools: `dashboard_publish` (`slug`, `title`, `html`, `open=true`), `canvas_update` (live updates active canvas in Browser without flicker), `dashboard_list`.
  - Harness automatically injects `skills/anti-slop-web-design/SKILL.md` rules whenever generating dashboards/visuals!
- **`src/WorkflowRecorder.cs` (Show-Me-Once → 100% Automated — `microsoft/skill-recorder` + `obra/superpowers`):**
  - Zero overhead when idle. When `record_start` (or UI button) is active:
    - Installs lightweight mouse/keyboard hook ONLY during recording.
    - On clicks: queries `UIAutomationClient` (`AutomationElement.FromPoint`) for `ProcessName`, `WindowTitle`, `ControlType`, `AutomationId`, `Name` + enriches with All in One context (Explorer selection, Excel cell, Browser URL/selector) → writes compact JSON line to `data\assistant\recordings\<id>\events.jsonl`.
    - Captures small compressed keyframe JPEG (~40 KB with click reticle) ONLY on click/window change.
    - On `record_stop`: returns manifest to **الـ Agent العام** which applies `skills/workflow-to-automation/SKILL.md` to strip noise, upgrade clicks to native COM/SQL/CDP/UIA, generate `SKILL.md` + Dynamic Tool (`tool_create`), and verify!
- **Two New Studio App Designs (Light + Dark each) + Full Visual Screenshot Anti-Slop Audit (Phase 4):**
  - Inspect `src/Ui.cs`, `src/LookPicker.cs`, `src/SettingsForm.cs`, `src/TopBar.cs`, `src/MainForm.cs`, `src/Dashboard.cs`, and `src/NexusBoard.cs` to preserve the exact existing Look Picker architecture (`Flow`, `SerQ`, `Soft`, `Nexus`, `Classic`).
  - Add 2 new studio-crafted designs inspired by [skills/anti-slop-web-design/SKILL.md](skills/anti-slop-web-design/SKILL.md), each with full Light & Dark palettes:
    1. **`atelier` ("Atelier" — Obsidian & Warm Drafting Paper):** Light (`#F4F0E8` warm paper, `#FCFAF5` cards, `#16181D` ink, `#D94822` Signal Vermilion accent) + Dark (`#0C0E13` Obsidian, `#13161F` cards, `#F1EFE9` ink, `#FF6238` Signal Vermilion accent).
    2. **`nordic` ("Nordic" — Porcelain Sage & Carbon Emerald):** Light (`#F2F5F3` cool mist, `#FFFFFF` cards, `#14211C` pine ink, `#0D8A64` Emerald accent) + Dark (`#0D1311` carbon pine, `#141D1A` cards, `#ECF3F0` ink, `#20C997` Telemetry Emerald accent).
  - Run `tools/test-ui-sweep.ps1` across all 33 screens (including both new themes in Light & Dark), **visually inspect the generated PNG contact sheets & screenshots**, catch every AI-slop / contrast / clipping / dark-mode defect across all screens, and fix them cleanly before building `AllInOne.exe`.

- **Remaining Zero-Exit PC Tools (Phase 4):**
  - `explorer_list`, `explorer_action` (`rename`/`copy`/`move`/`mkdir`/`delete_recycle`)
  - `system_processes`, `system_drives`, `process_kill`, `browser_cdp` (raw WebView2 `CallDevToolsProtocolMethodAsync`), `chrome_bookmarks`, `tagged_list`, `tagged_add`, `activity_recent`, `snip_ocr`, `app_screenshot`
  - `uia_inspect`, `uia_action` (native Windows UI Automation for SAP/ERP/Outlook/external apps)
  - `sessions_spawn` (non-blocking async subagent runner with `maxSpawnDepth = 2` + `sessions_status`)

---

## 3. File Map (Modular, Low-Bloat Structure)

| File | Type | Phase | Responsibility |
|---|---|---|---|
| `src/AssistantHarness.cs` | New | 1 ✓ / 3 | Standalone companion identity, 3-Tier Prompt Builder, `Lean Subagents`, activity narration, MCP CLI wiring |
| `src/OfficeCom.cs` | New | 2 | Live Excel/Word/PPT COM controls + background NASCA-safe Bulk COM Extractor |
| `src/SqliteVault.cs` | New | 3 | Zero-dep P/Invoke wrapper over `winsqlite3.dll` (`data\vault.db`, WAL, dynamic SQL sheet tables, FTS5 + Decay/MMR, step events, alert dedup) |
| `src/AgentMemory.cs` | New | 3 | `SOUL/USER/TOOLS/MEMORY/GOALS/HEARTBEAT.md` + `daily/YYYY-MM-DD.md` + Frozen Snapshot + `skill_manage` + Dynamic Tool Registry (`tool_create`) + Compaction |
| `src/CronEngine.cs` | New | 3 | Single Tray Timer: `cron/jobs.json`, `HEARTBEAT.md` idle-only 0-token pre-check, `HEARTBEAT_OK` / `[SILENT]`, dynamic follow-ups, `FileSystemWatcher` hooks |
| `src/DashboardGen.cs` | New | 3 | `data\dashboards\` manager + `Live Canvas` auto-flip into `Browser` |
| `src/WorkflowRecorder.cs` | New | 4 | Lightweight Semantic UIA + Smart Keyframe recorder (`events.jsonl` + ~40 KB JPEGs) → Show-Me-Once compiler |
| `src/Assistant.cs` | Edit | 1 ✓ / 3 | Standalone Companion Panel: Solo/Council/Leader switcher, live status strip, Record/Canvas/Memory quick controls |
| `src/MainForm.cs` | Edit | 1 ✓ / 2 / 4 | 20-page `PageContext()`, live Excel sheet/selection context, `CaptureAppScreenshot()` |
| `src/DocumentsPage.cs` + `src/OfficeEdit.cs` | Edit | 1 ✓ / 2 / 3 | Expose tabs & live COM handles, trigger lazy `SqliteVault.QueueExtract(path)` on Preview/Open |
| `src/Explorer.cs`, `src/Browser.cs` | Edit | 1 ✓ / 4 | `SelectedPaths()`, `OpenTabsInfo()`, Explorer file ops, Browser CDP bridge |
| `src/ApiTools.cs` | Edit | 1 ✓ / 2–4 | All new MCP tool suites + hot-loaded Dynamic Tools |
| `src/Agents.cs`, `src/LeaderTeam.cs` | Edit | 1 ✓ / 3 | Assistant isolation, Council/Leader harness wrapping, Auto Account/Model Fallback, `session_msgs` FTS5 indexing |

---

## 4. Phase Execution & Verification Matrix

| Phase | Status | Scope | Required Proof Before Closing |
|---|---|---|---|
| **Phase 1** | **DONE ✓** | `AssistantHarness.cs`, isolated `data\assistant\chats\`, `Solo/Council/Leader` bar, live 20-page `PageContext()`, navigation narration, `documents_list/switch/save` | `test-build.ps1` + `test-assistant-harness.ps1` + `regressions` + `forms` + `leader-team` + `document-close` — **ALL PASSED ✓** |
| **Phase 2** | **DONE ✓** | `OfficeCom.cs` live Excel/Word/PPT COM control (11 MCP tools) + NASCA-safe background Bulk COM Extractor + live Excel context in `PageContext()` | `test-build.ps1` + `test-office-com.ps1` (real Excel/Word/PPT/PDF COM + 0 leaked processes) + `assistant-harness` + `regressions` + `document-close` — **ALL PASSED ✓** |
| **Phase 3** | **DONE ✓** | `SqliteVault.cs` (`winsqlite3.dll` + WAL + dynamic SQL sheet tables + FTS5 + Decay/MMR + step events + alert dedup) + auto-extract on Preview/Open + `AgentMemory.cs` (Hermes/OpenClaw/DeepSeek memory, Visual Canvas `SOUL.md`, skills, Dynamic Tool Registry, Creator Mode, Compaction, Auto Fallback) + `CronEngine.cs` (Cron + `HEARTBEAT.md` + `GOALS.md` + 0-token pre-check + `FileSystemWatcher` Hooks) + `DashboardGen.cs` (`Live Canvas` reusing Browser tabs) | `test-build.ps1` + `test-sqlite-vault.ps1` (`SQL JOIN` + `FTS5` + Decay/MMR + UTF-8 Arabic) + `test-hermes-memory.ps1` (Frozen Snapshot, daily logs, Lean Subagents, `skill_manage`, `tool_create`, `Cron`/`HEARTBEAT_OK`, `Live Canvas`) + Phases 1–2 suites — **ALL PASSED ✓** |
| **Phase 4** | **IN PROGRESS** | `WorkflowRecorder.cs` (`record_start/stop` semantic UIA + keyframes) + **2 New Studio App Designs (`Atelier` & `Nordic`, Light + Dark each)** + full PC tools (Explorer ops, System/Process, Browser CDP, Chrome, Tagged/Activity, Snip/OCR, `app_screenshot`, `uia_*`, `sessions_spawn`) + **visual PNG screenshot audit across all 33 screens fixing any AI slop/UI flaws** + full test suite + self-contained `build.ps1` release build (verified standalone `ffmpeg.exe.gz`) + auto-doc closeout & GitHub `main` sync | `test-all.ps1` + `test-workflow-recorder.ps1` + `test-ui-sweep.ps1` (visually inspected PNGs across all screens + new Light/Dark themes) + `build.ps1` + `AllInOne.exe --data <temp>` smoke test + GitHub `origin/main` sync |
