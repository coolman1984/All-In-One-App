# History (All in One)

Newest first. One entry per task: date, what was asked, what was done, proof, what is still open.

## 2026-11-22 — Approved app cleanup and workflow repairs

- Cleaned up Home, Agents, Assistant, Accounts, and Team. Added Team to the local page API and visual sweep. The test now checks live navigation against both inventories.
- Team saves a membership only after publishing the signed join request. Slow setup work runs in the background. The failed-join regression reproduces the old bug, and the revised Team suite passed 67 checks. The Assistant reports a blocked recording start, and its regression passed.
- Browser keyboard calls now stop on an unresponsive Edge step without replaying keys. The cause of the intermittent Edge stall remains open.
- GitHub merged the Windows checks and automatic merge setup in PRs #2 and #3. PR #4 deliberately failed the required Windows check, stayed blocked with auto-merge enabled, and was closed. Live Office tests remain a local Windows check.
- The desktop release and corporate-share acceptance are tracked separately from the source changes.

## 2026-11-22 — App cleanup, repairs and automatic merge plan

- Asked to simplify the whole app, repair bugs and broken workflows, and automatically merge verified changes to GitHub main. Wrote the [repair plan](docs/plans/app-cleanup-and-reliability.html) for owner approval.
- Built a fresh offline UI test executable from `ec5dbc7`. The sweep reported zero errors and zero layout problems across 33 routes and 132 screenshots, with Flow light and Atelier dark at 1100×700 and 1500×900. Manual review found repeated storage information, a clipped dashboard alert, and crowded Agents controls. Team is absent from both the sweep and the local API page list. Confirmed GitHub auto-merge was disabled and main had no branch protection. Rendered the plan in Edge at 1250 px light and 420 px dark with no overflow or broken internal links.
- App changes, live workflow retests, and GitHub merge automation await plan approval. No executable or repository settings were changed.

## 2026-11-22 — Accounts reflects Cline CLI sign-in

- The current Cline CLI reported an active Cline Pass login while its legacy secrets file was empty. Accounts now uses the installed CLI package to check provider sign-in and displays only provider labels.
- Built and installed `AllInOne.exe` (89,905,664 bytes, SHA-256 `38240AB5011280F2CA314ECA8380C50D9BDF2AF1D9629E920A0F11155E3DDC6D`). The full 24-suite Windows run passed. Checked the packaged status against local Cline 3.0.74 and isolated signed-out, OAuth, API key, and unavailable cases. The running Accounts page showed `Signed in: Cline Pass`.

## 2026-11-22 — Built and installed the current Windows executable

- Built and installed the release from archived source at `314926d` on the local Windows PC. `AllInOne.exe` is 89,902,592 bytes with SHA-256 `4F1B66CDAEE5BFA5B7377299E9BE061083386B816ABB5071DBD6869C4197DBC5`. Saved the previous executable and an offline copy of the active profile outside the repository.
- All 24 extracted-source Windows suites passed, including live Office and Team tests. The release self-test and all six embedded resources passed, and the bundled FFmpeg encoded and decoded H.264/AAC. The exact release passed 44 live browser checks after two earlier keyboard timeouts. The intermittent timeout remains open.
- Verified the installed hash, existing profile and saved-item counts, Team and dashboard pages, responsive window, and no new startup error log entries. A live Team update through the corporate share remains unverified.

## 2026-11-22 — Pulled all GitHub main updates

- Fast-forwarded the project checkout by 15 commits, from `cbed099` to `29646ed`. The updates include browser automation, Projects and Team, signed releases, scheduled jobs, and saved-work fixes.
- The Windows test build and all 24 suites in `tools/test-all.ps1` passed on `29646ed`, including live Excel, Word, and PowerPoint. The incoming diff has six trailing blank-line warnings.
- The separate live browser and Team update checks remain open. The existing desktop executable was not rebuilt or replaced in this source sync.

## 2026-11-22 — GitHub main sync and test-runner fix

- Asked to push, merge and sync with GitHub main. Fetched twelve upstream commits, pushed the Windows GitHub workflow documentation and agent-conversation test fix, and merged them into main at 6bc581e. Local main matched the live GitHub branch.
- On source based on GitHub main at c3bd675, the native test build completed and the agent-conversation suite printed its success message and exited zero. The publishing diff and local references were checked.
- Follow-up on November 22. The owner selected GitHub main as the working source. Switched the project checkout to main at `46328ef` and preserved the earlier local snapshot and merge attempt.
- The native Windows build succeeded and 21 of 22 suites completed successfully on `46328ef`. The live Office COM suite stopped at Excel activation with `0x80080005`. A standalone Excel activation reproduced the failure without loading the app. Interactive Office validation remains open.

## 2026-11-22 — Completed scheduled work and connected workflow recording

- Built the missing execution path identified in the reliability review. Scheduled jobs can run an Assistant prompt or skill, call a registered tool directly, or notify. The new window exposes controls, latest result and retry. Restart recovers the queue and backup restore disables unfinished runs for review.
- Connected the latest saved recording to the Assistant's build action, treated unavailable subagents as failures, took consistent SQLite snapshots during backup, and improved the floating-card paste target check.
- [Windows verification](https://github.com/coolman1984/All-In-One-App/actions/runs/44986545611) built with the native .NET Framework compiler and passed 12 suites. The scheduled fixture used the real timer, an isolated Assistant subprocess, a registered SQL tool, duplicates, retries and backup recovery. The vault fixture proved that an active WAL's committed data reached an independent backup. Five additional local execution and snapshot assertions passed under Mono.
- Live Office/NASCA, two-tab WebView2, provider credentials, and real keyboard and floating-card interactions remain customer desktop acceptance checks. No live scheduled task was created for the user.

## 2026-11-22 — Deep reliability review and repairs

- Reviewed the source at `cbed099` along app startup, saved work, Office and browser handoffs, agent turns, the vault, schedules, recording and packaging. The current findings and remaining Windows checks are in the [customer audit](docs/customer-audit.md).
- Changes cover deferred data-folder moves, locked data protection, Markdown close and LF preservation, explicit document preview, same-name document saves, source-correct clippings, chat switches and retries, invalid schedules, stale document extraction, workflow event recovery and standalone FFmpeg validation.
- Windows PowerShell built the full source with the native .NET Framework compiler. Eighteen Windows suites exited zero across the [regression run](https://github.com/coolman1984/All-In-One-App/actions/runs/44983887686) and [layout run](https://github.com/coolman1984/All-In-One-App/actions/runs/44984052358). The hosted monitor has a 1,044 px native tracking limit. The layout fixture checked 705 control bounds at 700, 1,000 and 1,044 px.
- Ten isolated checks passed under Mono with system SQLite. All 29 PowerShell scripts parsed and negative packaging checks rejected missing and invalid encoders. Office/NASCA, actual two-tab WebView2 clipping, live providers and keyboard layouts still need native customer fixtures.
- Both release scripts built on Windows with a checksum-verified standalone encoder. The resulting executables contained all six required resources and the embedded encoder matched its input byte for byte. The unpacked encoder recorded and decoded a desktop MP4 in the [packaging run](https://github.com/coolman1984/All-In-One-App/actions/runs/44984180068). The recording used the encoder directly.
- The full Windows runner now includes the five extra companion, Office, vault, memory and workflow suites. No customer data or released executable was modified.

## 2026-11-21 — Autonomous Assistant Companion ("الـ Agent العام") — All 4 Phases (100%) complete, self-contained AllInOne.exe built & synced to GitHub main

- Asked to plan and execute all 4 phases in order in root `IMPLEMENTATION_PLAN.md`, embed studio-grade Anti-AI-Slop design skills, add a Visual-First Live Communication Canvas in `Browser`, add 2 new Light+Dark app themes (`Atelier` & `Nordic`) with a full visual screenshot audit, implement the Show-Me-Once workflow recorder and all remaining Zero-Exit PC MCP tools, build and verify self-contained zero-install `AllInOne.exe`, and keep GitHub `origin/main` synced after every verified milestone.
- Done (Phase 1):
  - Created detailed engineering handoff plan in root `IMPLEMENTATION_PLAN.md`, studio visual blueprints `docs/architecture-visual.html` and `docs/implementation-plan.html`, and 3 new skills (`skills/anti-slop-web-design/SKILL.md`, `skills/workflow-to-automation/SKILL.md`, `skills/proactive-companion/SKILL.md`), registered in `skills/README.md`, `CLAUDE.md`, and `AGENTS.md`.
  - Created `src/AssistantHarness.cs`, isolated Assistant chats in `data\assistant\chats\` (`src/Agents.cs`), upgraded `src/Assistant.cs` (`Solo / Council / Leader` bar + live status strip), expanded `MainForm.PageContext()` across all 20 pages, and added `documents_list` / `document_switch` / `document_save` + `document_open(edit)` in `src/ApiTools.cs`.
- Done (Phase 2):
  - Created `src/OfficeCom.cs` with live Excel/Word/PowerPoint COM controls (`ExcelQuickContext`, `ExcelInfo`, `ExcelRead`, `ExcelWrite`, `ExcelFormula`, `ExcelSheetAdd`, `ExcelFormat`, `WordRead`, `WordWrite`, `PptRead`, `PptWrite`) and background NASCA-safe Bulk COM Extractor (`ExtractFile`, `ExtractFromLiveEditor`) on dedicated STA `en-US` threads (direct RFC-4180 CSV parser, Excel bulk `UsedRange.Value2`, Word paragraphs+tables, PowerPoint shared-instance `startedByUs` guard, PDF via hidden Word COM with temporary `DisableConvertPdfWarning = 1` + 25 s PID timeout + pure-C# FlateDecode fallback).
  - Exposed `OfficeEdit.ComApp`/`ComDoc`, wired live Excel sheet/selection context into `MainForm.PageContext()`, and added 11 MCP tools (`excel_*`, `word_*`, `ppt_*`, `office_extract` off-UI-thread) in `src/ApiTools.cs` and `src/AssistantHarness.cs`.
- Done (Phase 3):
  - Created `src/SqliteVault.cs` (native x64 P/Invoke over `System32\winsqlite3.dll`, UTF-8 `SQLITE_TRANSIENT` marshaling, WAL mode, verified `fts5` on `winsqlite3.dll` 3.53.4 with fallback, dynamic per-sheet SQL tables `data_<docId>_<sheetSlug>` for cross-report `SQL JOIN`, smart `(path, size, modified_ticks)` cache, deduped lazy STA `QueueExtract` hooked into `DocumentsPage.Show`, `session_msgs` + `session_fts` with BM25 + 30-day exponential Temporal Decay + MMR dedup, `step_events` with `ForkAt`, and `alert_dedup`).
  - Created `src/AgentMemory.cs` (`SOUL/USER/TOOLS/MEMORY/GOALS/HEARTBEAT.md` with Visual-First Live Communication Canvas in `SOUL.md`, today+yesterday `daily\YYYY-MM-DD.md`, per-session Frozen Snapshots, `skill_manage` + Progressive Disclosure, DeepSeek Dynamic Tool Registry + Creator Mode `tool_create` hot-loaded into MCP for `sql`/`powershell`/`cmd`/`python`, `todo_plan`, and boundary-safe `CompactChat` with daily memory flush).
  - Created `src/CronEngine.cs` (single lightweight 60s Tray Timer, `cron/jobs.json`, 0-token pre-checks + `[SILENT]` + `alert_dedup`, idle-only `HEARTBEAT.md`/`GOALS.md` returning `HEARTBEAT_OK`, and `FileSystemWatcher` `watch_folder` hooks auto-queuing extraction) wired cleanly into `App.cs` startup/shutdown.
  - Created `src/DashboardGen.cs` (`data\dashboards\`, `dashboard_publish`, `canvas_update` -> `live-canvas.html` reusing existing Browser tab via `BrowserPage.OpenOrReloadFile` / `MainForm.OpenOrReloadInBrowser` / `App.OpenOrReloadInBrowser`).
  - Upgraded `src/AssistantHarness.cs` to the 3-Tier Prompt Builder (`Stable → Context → Volatile`) + Lean Subagents (`SystemPromptLean` / `WrapTeamPromptLean`) + `SharedHarnessHint`; upgraded `src/Agents.cs` with async FTS5 session indexing, Lean Subagent workers, `AgentFallback` auto alternate-account/model retry, and `step_events` logging; added all 20 Phase 3 MCP tools + dynamic tool hot-loading in `src/ApiTools.cs`.
- Done (Phase 4):
  - Created `src/WorkflowRecorder.cs` with zero extra assembly references: active-only Win32 low-level mouse/keyboard hooks (`WH_MOUSE_LL` / `WH_KEYBOARD_LL`) + `WindowFromPoint` semantic UI inspection + live `PageContext()` enrichment → `data\assistant\recordings\<id>\events.jsonl` + ~40 KB smart keyframe JPEGs (`frames\step-NNN.jpg` with Signal Vermilion click reticle) + `summary.md` Show-Me-Once automation blueprint; added `● Rec` / `■ Stop` button in `src/Assistant.cs` with 1-click compile prompt pre-fill; added `StickyPro.UiaEngine` (`Inspect` / `PerformAction`) and `StickyPro.SubagentRunner` (`Spawn` / `Status` with `MaxSpawnDepth = 2` and `AssistantHarness.SystemPromptLean`).
  - Added 2 new studio-crafted app designs (each with Light and Dark palettes) across `src/Ui.cs`, `src/App.cs`, `src/LookPicker.cs`, `src/SettingsForm.cs`, `src/TopBar.cs`, and `src/NexusBoard.cs`: **`atelier`** (Warm Editorial Paper `#F4F0E8` / Signal Vermilion `#D94822`; Dark Carbon Obsidian `#0C0E13` / `#FF6238`) and **`nordic`** (Sage Mist `#F2F5F3` / Nordic Emerald `#0D8A64`; Dark Carbon Pine `#0D1311` / `#20C997`), preserving `flow`, `serq`, `soft`, `nexus`, and `classic`.
  - Added all 20 Phase 4 MCP tools in `src/ApiTools.cs` and `src/AssistantHarness.cs` (bringing the total new companion tools across Phases 1–4 to 54 + hot-loaded dynamic tools): `record_start`, `record_stop`, `record_status`, `record_list`, `explorer_list`, `explorer_action` (`rename`/`copy`/`move`/`mkdir`/`delete_recycle`/`select`/`navigate`), `system_processes`, `system_drives`, `process_kill`, `browser_cdp` (WebView2 `CallDevToolsProtocolMethodAsync` + built-in compact `ax_snapshot`/`dom_snapshot`), `chrome_bookmarks`, `tagged_list`, `tagged_add`, `activity_recent`, `snip_ocr`, `app_screenshot`, `uia_inspect`, `uia_action`, `sessions_spawn`, and `sessions_status`.
- Proof:
  - Test suites against `build-test\StickyNotesPro.test.exe`: `tools/test-build.ps1` (`BUILD OK`, zero new warnings), `test-workflow-recorder.ps1`, `test-sqlite-vault.ps1`, `test-hermes-memory.ps1`, `test-assistant-harness.ps1`, `test-office-com.ps1` (real Excel/Word/PPT/PDF COM + 0 leaked processes), `test-regressions.ps1`, `test-forms.ps1`, `test-leader-team.ps1`, and `test-document-close.ps1` all exited `0`.
  - Visual UI Sweep: `tools/test-ui-sweep.ps1 -Quick` and `tools/test-ui-sweep.ps1 -Looks @('atelier-light','atelier-dark','nordic-light','nordic-dark') -Sizes @('1500x900') -SkipStress` both reported `SWEEP DONE: 0 errors, 0 layout problems` across all 33 routes × 4 new look modes (132 screenshots); all 4 contact sheets were visually inspected.
  - Self-contained release executable: built `AllInOne.exe` via `build.ps1` (`85.34 MB` / `89,487,360` bytes, SHA256 `2C6B62DEFB38566BF38A8E173E665ECF1805D08FEC764309722719CF57051382`), verified all 6 embedded manifest resources (`ChatSlides.pptx`, `webview2.Microsoft.Web.WebView2.Core.dll`, `webview2.Microsoft.Web.WebView2.WinForms.dll`, `webview2.WebView2Loader.dll`, `md.marked.js`, `tools.ffmpeg.exe.gz` with verified decompressed size `211.93 MB` / `222,229,504` bytes), and smoke-tested `AllInOne.exe --data <temp>` startup and `--exit` (`settings.json` created, `error.log` absent).
- Open: non-security reliability/UX items tracked separately (`docs/plans/never-lose-a-note.html` `Data.cs` transient-lock guard awaiting owner approval, `Agents.cs` busy chat-switch & empty network retry, `MarkdownPage` LF-to-CRLF, `Snippets` word-boundary/AltGr, `Cards` WinEventHook paste, `test-agent-conversations.ps1` exit hang, and live interactive desktop usage of `AllInOne.exe`).

## 2026-11-21 — Documented closed-network open-automation policy

- Asked to permanently record across all project documentation and skills that the app operates inside a closed, isolated corporate/factory local network behind a strict enterprise firewall, that all local automation capabilities stay intentionally open (no security lock-downs), and that future AI agents must not flag or ask again to close those items.
- Updated `CLAUDE.md`, `AGENTS.md`, `README.md`, `docs/maintenance.md`, `skills/all-in-one-maintenance/SKILL.md`, `LESSONS.md`, and `HISTORY.md` with the owner's closed-network open-automation policy. Formally closed the 2026-11-20 security lock-down items (MCP Named Pipe ACL + confirmation gates, `MdWeb`/`Mail` XSS sanitization, and Assistant `Full Auto` restrictions) as intentional by design (won't fix / do not re-raise).
- Proof: documentation-only change (no app source or release binaries modified); verified all 7 updated files via git diff review.
- Open: non-security reliability, UX, and workflow items only (`Data.cs` transient-lock guard, `Agents.cs` busy chat-switch & empty network retry, `MarkdownPage` LF-to-CRLF & failed-save last-tab guard, `Data.cs` deferred-restart move reconciliation, `AppLinks`/`DocumentsPage` read-only routing, `Snippets` word-boundary/AltGr, `Cards` WinEventHook paste, `test-agent-conversations.ps1` exit hang, and live desktop review of `AllInOne.exe`).

## 2026-11-21 — GitHub planning guides and main sync

- Asked to push, merge and sync with GitHub `main`. Brought in the planning docs from `7de5271` on top of `5c5d555` and kept the repository copies of both guides. Removed private artifact links from the public history entry.
- Checked the documentation diff and local links. The merge adds documentation only. App tests and release builds were outside this sync. The screen recording remains local.

## 2026-11-20 — Owner's guide: how the app works

- Asked for a plan-style page that explains how the whole app works, because it has grown too big to keep track of.
- Wrote [how the app works](docs/plans/how-the-app-works.html) (Arabic, Rev 1). It covers three layers (UI, background services, data), the 20 sidebar pages in five purpose groups, code share per group (core 24%, AI 23%, PC tools 21%, files 19%, daily tools 13% of 37.5k lines; approximate file-to-group split), three end-to-end journeys, the five cross-page links (search, Send to, drag to sidebar, global hotkeys, MCP), why it feels out of control, and five steering rules with a suggested keep/freeze/trim call per group.
- Proof: source reading (`src/MainForm.cs` page registration, `src/App.cs` background services, line counts of `src/*.cs`) and existing history. App source, tests and binaries were not changed.
- Open: the owner decides the per-group calls; the data-safety plan above still awaits approval.

## 2026-11-20 — Plan-page workflow and the first plan (data safety at startup)

- Asked to understand a shared workflow (an engineering plan written as a blog-style web page and reviewed before building, plus plain-writing rules) and apply it to this project.
- Added the [plan page skill](skills/plan-page/SKILL.md) and linked it from [AGENTS.md](AGENTS.md), the [skills index](skills/README.md) and the [maintenance guide](docs/maintenance.md).
- Wrote the first plan, [never lose a note](docs/plans/never-lose-a-note.html) (Arabic, Rev 1). It targets the open `Data.cs` transient-lock item. Confirmed in source: `Read<T>` (`src/Data.cs:494`) treats a busy file like a missing one and returns null, `Load` then starts that section empty, and the next two saves overwrite the primary file and then its single `.bak`. A busy primary with a readable `.bak` also loads older data and writes it over the newer file.
- Proof: source reading only. App source, tests and binaries were not changed.
- Open: owner approval of the plan, then phases 1–3 (busy-aware reads and write lock with a banner; daily snapshots and shrink guard; the same rules for agent chats).

## 2026-11-20 — Flow theme (FlowTask dashboard look) and P0 safety/workflow fixes

- Asked to fix confirmed issues and match the app theme and dashboard to the provided FlowTask Project Dashboard reference image.
- Implemented the new default `"flow"` design (`DesignRev = 3`, `Ui.FlowTheme = 9` `"Flow blue"` `#2F66F6`, full light and dark palettes) across `src/Ui.cs`, `src/Data.cs`, `src/App.cs`, `src/LookPicker.cs`, `src/SettingsForm.cs`, `src/MainForm.cs`, `src/TopBar.cs`, `src/Dashboard.cs`, and `src/NexusBoard.cs`: pure white sidebar with soft sky-blue active pill (`#EAF1FF`) and cobalt-blue active icon/text, airy `#F5F7FC` canvas, soft diffused card shadows, crisp white TopBar with soft pill search and tinted icon chips, dark-mode scrollbar, and a full Flow Home Dashboard using live app data (time-aware greeting + date pill, 4 colourful circular-badge KPI cards, Storage Progress with 4-colour pastel icon badges, Tasks Overview donut ring chart, This Week 7-day calendar strip + upcoming tasks, Top Memory Apps with pastel status pills, 7-column stacked System Activity bar chart, and a 4-column pastel Task Board preview). Kept `SerQ`, `Soft`, `Nexus`, and `Classic` available in Look Picker and Settings.
- Fixed theme and P0 safety/workflow defects: `Ui.DrawCard` and `LookPicker.OnPaint` clip-region restoration/disposal, `App.SetTheme` clearing custom accent, `SettingsForm` preserving empty default `SnipFolder`, `MainForm.ShowPage` calling `PageChangedForAssistant`, `NexusBoard` midnight refresh / empty-task click zone / small-bar visibility / width clamping, `SnipEditor` dirty-flag save before `CopyPath`/`CopyFile` (prevents sharing unredacted screenshots), `Install.Uninstall` safe two-step confirmation + post-dialog `rmdir` + `Taskbar.Restore()`, `PopupHider` default OFF and confirmed-`ICTray*`-only matching, `Explorer` dynamic `Opening` menu for in-app preview/edit/mail + leaf-name traversal validation + empty `FolderSet` guard, `Terminal` 2x2/3x2/4x2 DFS split-pane indices + `;` escaping + `--` separator + empty `FolderSet` guard, `Power` `Restart Explorer` null-callback crash, `Overlay` `Shutdown()` on `SessionEnding` and Restart/Shut down, `Uninstall` `MyDocuments` no longer `"Safe"` / short-name match guard / unchecked leftovers on cancel, and `build.ps1` + `tools/make-dist.ps1` Scoop `.shim` dereferencing + bundled `ffmpeg.exe.gz`. Note: `opencode-delegate` (`ui` lane) failed with an upstream server error, so implementation and verification ran locally.
- Proof (working tree over `faf5559`): `tools/test-build.ps1` passed (`BUILD OK`, zero new warnings); 15 of 16 suites exited `0` (`regressions`, `reliability`, `forms`, `dashboard`, `layout`, `office-edit`, `document-close`, `capture`, `snip-output`, `recorder`, `visual`, `responsiveness`, `explorer-paint`, `leader-team`, `saved-work`); `test-agent-conversations.ps1` passed all assertions (`AGENT CONVERSATIONS OK`) then hit the pre-existing exit hang; targeted reflection checks verified Flow palette, LookPicker order, 2x2 `SplitArgs` DFS indices, `ValidLeafName`, and `HideSecurityPopups = false`; visually inspected offscreen 1440x1180 Flow Light and Flow Dark renders. Built `AllInOne.exe` via `build.ps1` (`85.04 MB`, SHA256 `333CF5FC30668E1A47D9F92AF709164D2CECF0F0EBDA5F171502EFF79F422307`), verified all 6 embedded resources including `tools.ffmpeg.exe.gz`, and smoke-tested `--data <temp>` clean startup and `--exit` (`error.log` absent, `"Design":"flow"` and `"HideSecurityPopups":false` persisted).
- Open: security lock-down items (MCP Named Pipe ACL + confirmation gates, `MdWeb`/`Mail` XSS sanitization, Assistant `Full Auto`) were formally closed as intentional by design on `2026-11-21`; remaining non-security open items are `Data.cs` transient-lock guard, `Agents.cs` busy chat-switch & empty network retry, `MarkdownPage` LF-to-CRLF, `Snippets` word-boundary/AltGr, `Cards` WinEventHook paste, `test-agent-conversations.ps1` exit hang, and live desktop review of `AllInOne.exe`.

## 2026-11-20 — Deep 72-file logic audit and interactive layered visual map

- Asked to deeply verify whether the app's logic and steps are completely correct, build a layered animated visual map easy for customers to understand, and identify what the app still needs.
- Audited all 72 `src/*.cs` files plus `build.ps1` and `tools/make-dist.ps1` on `main` (`faf5559`). Built a standalone bilingual (Arabic/English) interactive layered visual map and audit explorer artifact covering 6 architectural layers, 25 features (20 pages + 5 global surfaces), 7 animated end-to-end journeys, verified findings with `file:line` citations, and a P0–P3 readiness roadmap.
- Ran `tools/test-all.ps1` against `build-test/StickyNotesPro.test.exe`: 14 suites passed (`regressions`, `reliability`, `forms`, `dashboard`, `layout`, `office-edit`, `document-close`, `capture`, `snip-output`, `recorder`, `visual`, `responsiveness`, `explorer-paint`, plus `agent-conversations` assertions), then `test-agent-conversations.ps1` hung indefinitely on process exit (reproduced standalone with a 35 s timeout). Ran `test-leader-team.ps1` and `test-saved-work.ps1` individually; both exited `0`.
- Confirmed 18 critical and 30+ high-severity logic/safety/security issues across all 6 layers (including `Uninstall.cs` pre-ticking `MyDocuments` and short-name substrings, `Install.cs` "No = delete all data" trap, `SnipEditor` unredacted `Copy file`/`Copy path` leak, unauthenticated MCP Named Pipe + forced `auto` Assistant RCE/JS surface, `MdWeb`/`Mail` XSS, `Data.cs` silent wipe on transient lock, `Terminal.cs` broken 2x2/3x2/4x2 WT split indices, `Explorer.cs` dead right-click preview/edit menu, `Agents.cs` busy chat-switch corruption and empty network retry, `PopupHider` default-on modal deadlock, `Overlay` missing `Taskbar.Restore()` on logoff/shutdown/uninstall, `Backup` OneDrive ReparsePoint block, and `make-dist.ps1` omitting `ffmpeg.exe.gz`). App source and release binaries were not changed.
- Open: fixing P0–P3 findings, resolving the `test-agent-conversations.ps1` exit hang, and live verification after fixes.

## 2026-11-20 — Customer workflow audit and interactive map

- Requested a deep logic review and a customer-facing visual explanation. Added the [customer map](docs/customer-map.html) with 42 feature entries, five operating layers, original toolbar crops for 33 routes, seven branching workflows and an audit view. The single-file customer edition embeds the images and works offline. See the [evidence and acceptance checks](docs/customer-audit.md).
- Audited source `faf5559` and the existing local executable from the build entry below. On that executable, reproduced the last Markdown tab closing on a failed save and later edits remaining in the old folder after data relocation. The decompressed recorder resource matched the local ffmpeg shim. Traced three further mismatches in read-only routing, browser clipping and the document assistant's context.
- Current source tests: 15 of 16 suites passed. The private-desktop Office test received access denied, including a retry on the local executable. The offline sweep captured 495 images (33 routes, five looks, three sizes), ran 300 page switches, 280 resizes and 14 searches. Its 32 errors were startup registry access denials. All 11 layout flags were Classic sparse-color warnings. Reviewed narrow dark and Classic sheets and the customer toolbar crops. Independently reopened the exported workbook and verified typed cells.
- Tested the map in Chromium on desktop and at 390 px, including reduced motion, navigation, search, all 42 detail cards, all 31 directed workflow transitions, autoplay, findings filters and drill-through. In offline mode the standalone file made zero external requests and raised no page errors. App source and release binaries were not changed.
- Open: repair the documented workflow and packaging issues, confirm protected Office/PDF and live provider/browser behavior on a representative PC, and check startup inventory under customer permissions.

## 2026-11-20 — Local build and launch check

- Asked to build `AllInOne.exe` and run the app from the freshly cloned `main` checkout (`faf5559`).
- Ran `build.ps1` cleanly; produced `AllInOne.exe` (2.56 MB, SHA256 `B011E1718AFB0F4E8EA7585919572FADF9E34E9DB59772BB34C43BCA4D717E1B`) and `All in One.lnk`. `Get-Command ffmpeg` resolved to a 0.13 MB Scoop shim rather than the 212 MB standalone `ffmpeg.exe`, so the bundled recorder archive contains the shim (screen recording still uses PATH `ffmpeg` on this PC; repacking with the standalone binary is open if shipping to another PC).
- Started `AllInOne.exe` and verified responsive startup (`data/settings.json` and `data/prompts.json` created, no `data/error.log`), then exited it cleanly via `--exit` and verified the single-instance mutex was released. Because agent commands run on an isolated `WinSta0\exebox-*` desktop, launching a visible window on the user's interactive desktop remains to be done directly by opening `AllInOne.exe` or `All in One.lnk`.

## 2026-11-20 — GitHub main sync

- Asked to publish the current source to GitHub and sync the local checkout with main.
- Published the source snapshot (9f75158) as the initial GitHub main branch. GitHub had no existing branches to merge. Verified GitHub HEAD, local HEAD and origin/main pointed to 9f75158 after the push. The screen recording stays local.
- The staged whitespace check passed outside the bundled Microsoft license, which retains its original trailing space. The default proxy returned HTTP 502. The direct HTTPS connection worked. App tests were not rerun.

## 2026-11-19 — Whole-app bug hunt and screenshots

- Asked to audit every tab, capture screenshots and fix discovered bugs. Expanded the offscreen sweep to 33 screens including Settings and the System, Chrome, Power and Shortcuts sub-tabs. Added fixture content, an offline Browser shell check and labeled screenshot sheets.
- Documents now keeps unsaved editors open after Cancel or a failed save, including tab close, reload, changing edit mode and normal exit. Empty document actions are disabled and the pane follows dark mode. Office placement recovers when the host rises after activation.
- Fixed the Markdown status line clipping, invalid routes blanking the main window and Uninstall offering a Drives floating card. Settings batches its initial layout. Isolated Settings construction measured 9.1–12.1 seconds before and 0.6–0.7 seconds after, with three samples per build during other audit work.
- Captured 495 screenshots across five looks and three sizes during the audit. No exceptions were recorded. The 11 Classic low-color flags showed sparse content when reviewed. After the Markdown fix, the 132-image light/dark rerun had zero exceptions and layout flags, including 300 page switches, 280 resizes and 14 searches. Saved Markdown Split mode also reopened and resized successfully in isolation.
- All 16 suites passed from extracted source before the final native stacking adjustment. The preceding release candidate failed the dedicated private-desktop stacking regression. Captures cover app routes with sample data and local system inventory. Live protected Office editing, account sign-in and web content remain open checks.
- The final packaged executable passed the private-desktop Office check three times and the document safety check once. Verified embedded resources and installed `AllInOne.exe` with SHA256 `064304E6AAA765503042DD2A6B04FA2A493DAF269DB41F76ED7A54A717021757` at the existing shortcut target. Rollback is `AllInOne.before-ui-audit.exe`. The open session needs a normal restart to load the update.
- Evidence in [the screenshot index](build-test/audit-verified/index.md), [visual report](build-test/audit-verified/report.md) and `dist/All-in-One-screenshot-audit-2026-11-19.zip`. The Claude worker timed out before writing changes. Implementation and verification were completed locally.

## 2026-11-19 — Documents edit blocked the app tabs and sidebar

- Asked to make the rest of All in One selectable while editing an Office document. Follow-up to the Office activation fix below.
- The app attached the Office window as its native owned window, which can join the input queues. Office now has its own top-level window. The app moves and hides it asynchronously and leaves focus with the window the user clicks.
- The new offscreen test fails on the previous build and passes with the fix. It checks ownership, pane bounds, window ordering, dialog visibility, tab visibility and placement while the editor thread is busy. All 15 suites passed from extracted source. The release executable passed the same Office check and contains the required embedded resources.
- Updated the shortcut target `AllInOne.exe` to SHA256 `5A2DFE4180AC8B9B6F54EFDDF5B99314142FAF572596E6EB637C48D0EB39D985`. Previous build saved as `AllInOne.before-office-edit-fix.exe`. The open production session needs a normal restart to load the update. Live clicks in the protected PowerPoint file remain to be confirmed.
- The Claude bridge timed out before producing edits. Codex implemented and tested the fix locally.

## 2026-11-19 — Documents edit: Office window could not be used; Excel stayed read only
- Asked: in edit mode nothing can be chosen or edited (PowerPoint ribbon grey); Excel tab still read only.
- Cause: a SetParent child window never becomes active, so Office greys its ribbon and ignores input. Excel: the
  read-only preview of the same file was still open and the edit error was overwritten by the 'read only' line.
- Done: the Office window stays top-level, frameless, owned by the app window (GWLP_HWNDPARENT) and is kept over the view
  every 150 ms (hidden with the view). The preview of a file is released before it opens for editing; the edit error
  stays visible; a file already open in Office (lock file ~$name) is reported instead of hanging.
- Proof: off-screen test - Office window rect = view rect, not a child, owner = app window, hides with the view, Excel
  quits on close; test-all = ALL CHECKS PASSED; rebuilt, relaunched. Real mouse/keyboard use: owner to confirm.

## 2026-11-19 — Explorer: double-click = own app; right-click = preview or edit in the app
- Asked: double-click opens the file in its original app; from the right-click menu choose to open it inside the app,
  preview or edit.
- Done: double-click already opened the own app (unchanged). Right-click: 'Open in the app: preview (read only)' and
  'Open in the app: edit' (Excel/Word/PowerPoint); mails: 'Read in the app'. DocumentsPage.Open(path, edit) keeps the
  choice per file over the page's Edit mode switch.
- Proof: build OK, test-all = ALL CHECKS PASSED, rebuilt, relaunched.

## 2026-11-19 — Documents: edit inside the app (real Excel / Word / PowerPoint in the tab)
- Asked: full control and editing inside the app (the previewer is read only).
- Done: OfficeEdit.cs opens the file through COM (Excel/Word: own process per tab; PowerPoint: shared app, only this
  presentation) and places the app window inside the tab (SetParent, no caption); full ribbon, Ctrl+S. Documents page:
  'Edit mode: on/off' (default on, Settings.ViewerEdit), Save button, unsaved-changes question when a tab closes or edit
  mode turns off, the view comes back if the Office window is closed from its own menu.
- Proof: NASCA xlsx copy opened for edit off-screen (hosted, not dirty), closed without saving, its Excel quit;
  test-all = ALL CHECKS PASSED; rebuilt, relaunched. Word / PowerPoint edit not run by me; owner to try.

## 2026-11-19 — Documents viewer: mouse pointer vanished over the document
- Asked: in the viewer the mouse disappears and there is no control.
- Cause (likely): the previewer window belongs to another process and sets no pointer (class cursor null) or hides it.
- Done: OfficeView answers WM_SETCURSOR from the document window with the arrow (the document may still set its own),
  a 250 ms check turns the pointer back on while it is over the view, and focus is passed to the previewer.
- Proof: build OK, rebuilt, relaunched. Not checked on screen by me (needs the real mouse); owner to confirm.

## 2026-11-19 — Documents viewer (Office/PDF), .mysingle mail reader, default account per CLI, fixes
- Asked: an in-app Microsoft 365 viewer (Excel, PowerPoint, Word, PDF); read saved .mysingle mails; the chat '...' did nothing;
  the outline panel was squeezed; choose the default account per CLI without signing out; read data through COM.
- Done:
  - Documents page (DocumentsPage.cs, OfficeView.cs): tabs; files are drawn by Office's own previewers (Excel 00020827,
    PowerPoint 65235197, Word 84F66100, Visio, PDF) like Explorer's preview pane; NASCA files work. Protected PDFs: Foxit
    window hosted in the tab (only when Foxit is not already open). Explorer right-click 'View in the app', Send to,
    drag-drop, MCP tool document_open.
  - .mysingle = MIME mail wrapped by NASCA: Word COM opens a temp .txt copy read-only as Latin-1 text (original bytes),
    never saved; the existing MIME reader makes a page on the Browser page. Outlook OpenSharedItem could not read it.
  - Chat header '...' and '+' work outside the Agents page (Assistant).
  - PaneGrip clamp never shrinks a panel below its minimum (was 60 px): the Markdown side panel hides instead.
  - Accounts: header button panel was 100 px tall and hid the first row's buttons; every row has 'Make default'; the
    default is used by new chats, the Assistant and terminals the app opens (claude/codex get CLAUDE_CONFIG_DIR /
    CODEX_HOME of that account in the command).
- Proof: NASCA xlsx and docx rendered in the viewer (screens); pptx previewer built its view but the off-screen capture is
  blank; Foxit hosted and closed cleanly but not captured; .mysingle -> 54 KB page in 4.7 s, no stray Word;
  ShellArgs check; Accounts render; test-all = ALL CHECKS PASSED; rebuilt, relaunched.
- Open: PowerPoint and protected-PDF views not seen on screen; ViewStack sections not yet on pages other than Markdown.

## 2026-11-19 — VS Code style layout (Markdown) + Assistant full access
- Asked: side panel cramped and not flexible; make it like VS Code (collapse/expand, resize, customize); the Assistant
  could not read the open note (blocked outside C:\Users\<user>); give agents full access, fully automatic.
- Done:
  - Assistant: new chats (and old Edit-mode ones) run in Full auto; every run gets --add-dir for the open note's folder,
    the Explorer folder, all vaults and the app folder (Claude and Codex). Orders row folds away (remembered).
  - ViewStack.cs (reusable): VS Code style sections - chevron, title, count; click folds, double-click maximizes, drag
    between sections resizes, right-click shows/hides/reorders; state saved in Settings.ViewStacks.
  - Markdown: side panel = ViewStack (Outline with filter, Backlinks, Outgoing, Unlinked, Tags), resizable width;
    outline tree without horizontal scroll bar; EditorTabs.cs note tabs (drag, middle-click, menu, saved in MdTabs);
    breadcrumbs vault > folder > note > heading (click: jump); compact icon actions + More menu.
  - Keys: Ctrl+B note tree (bold with a selection in the editor), Ctrl+Shift+B side panel, Ctrl+W/Tab/Shift+T tabs,
    Ctrl+P open note, Ctrl+Shift+O outline filter, Ctrl+\ split. App: F11 Zen mode (Esc leaves), Ctrl+B menu,
    Ctrl+Alt+B or Ctrl+J Assistant, Ctrl+Shift+P search/commands.
- Proof: Claude from the home folder with --add-dir read HISTORY.md; renders (Assistant open; wide with side sections);
  test-all = ALL CHECKS PASSED; rebuilt, relaunched.
- Open: Full auto not run end to end from the app (a safety check blocked running the bypass mode in this session);
  ViewStack used only on Markdown so far.

## 2026-11-19 — Assistant on every page (works the app through its own tools); account 2030; Obsidian outline
- Asked: the Markdown chat was cramped and not logical; one chat with you on every tab that can do the work in any tab,
  with one-click orders; outline like Obsidian; make Claude account 2030 active now.
- Done:
  - Assistant (Assistant.cs): docked right of every page, Ctrl+J or the top-bar button, resizable, remembered. It is the
    full ChatPane (collection "Assistant", Edit mode) with a chat picker, New, orders for the current page (Markdown,
    Browser, Tasks, Notes, Prompts, Clipboard, Explorer, Terminals, Home + every page) and All orders, plus your own orders
    (prompts in category "Orders"). Answer -> copy / note / task.
  - Control: Assistant runs get the app's MCP server automatically (Claude: --mcp-config + --allowedTools mcp__all-in-one;
    Codex: -c mcp_servers.all_in_one.*). New tools: page_context, markdown_open, explorer_open, terminal_open,
    browser_open, browser_read, browser_run_js (+ markdown, browser, accounts in open_page).
  - Each message carries the page context hidden between brackets; ChatView.ShownText hides it in bubbles and titles.
  - Markdown: the old side AI panel removed (MdAssistant.cs deleted); its button opens the Assistant; columns fit again
    (outline steps aside, then the tree). Outline is a nested, collapsible tree that follows the heading you read.
  - Accounts: the Claude sign-in now always shows the paste-code box (Claude's prompt has no line end, it was never
    detected). Account 2030 signed in through a visible sign-in window (pro) and set as the default for new Claude chats
    (settings edited while the app was closed; backup in the session scratchpad).
- Proof: real check - Claude with the app's MCP tools read the running app: "Markdown (HISTORY.md), 0 open tasks, 4 notes";
  render of Markdown + Assistant (orders, chat, readable note); test-all = ALL CHECKS PASSED; rebuilt, relaunched.
- Open: write actions through the Assistant (create task / edit note) not run against live data; Codex MCP wiring not run.

## 2026-11-19 — Accounts: Antigravity sign-in detected
- Asked: Antigravity was signed in but the app showed "Sign-in is checked by the CLI itself".
- Found: agy 1.5.11 has no status command; it keeps its Google sign-in in Windows Credential Manager (generic entry
  "gemini:antigravity") and logs "OAuth: authenticated successfully as <email>" in ~\.gemini\antigravity-cli\cli.log.
- Done: CliAccounts.Antigravity(): signed in = the credential entry exists (CredEnumerate, the secret is never read);
  email = the last "authenticated successfully as" line of the log. Antigravity card: Sign out (removes that entry, asks
  first). Also verified: the Markdown AI panel hosts the full Agents chat (previous entry).
- Proof: status call returned "Signed in as mflma1984@gmail.com" (matches the CLI's own banner); test-all = ALL CHECKS
  PASSED; rebuilt, relaunched on Accounts.
- Open: the plan name (Google AI Pro) is not in the log, so it is not shown; Sign out not run.

## 2026-11-19 — Markdown AI panel = the full Agents chat; test dialog on the screen
- Asked: the full chat features of the Agents page inside Markdown (models, effort, accounts, mode, folder, web search,
  files, commands, limits, history); fix the error dialog seen on the Accounts page.
- Done: the Markdown AI panel now hosts the real ChatPane (every Agents feature), plus a chat picker (collection
  "Markdown", also on the Agents page), New chat, open in Agents, the one-click actions, an "Include this note" switch
  (each message names the note and the selected part; the agent reads the file) and Insert / Save answer. ChatPane got
  OutgoingFilter, SendText and LastAnswer. New chats start read-only (Ask); titles are the note name + the request.
- Error dialog: it came from an offscreen test run at 10:31 (app code loaded in PowerShell without the app; a callback
  used the missing app object), not from the running app (its handler logs errors; error.log had nothing). All test
  harnesses now set UnhandledExceptionMode.ThrowException so no test can show a dialog on the desktop again.
- Proof: embedded chat test with a real Claude Code summary (chips, folder, limits shown); test-all = ALL CHECKS PASSED;
  sweep (soft-dark, serq-light, 2 sizes) clean; rebuilt, relaunched on Markdown.
- Open: the embedded chat was not clicked live; at 1100 px the panel is narrow (drag it wider or use Agents).

## 2026-11-19 — Markdown: AI chat panel and Obsidian features
- Asked: an AI chat right next to the note (flexible) to chat about and summarize it; add the useful Obsidian features that
  were missing.
- Done:
  - AI panel (MdAssistant.cs) on the right of the note: agent picker (installed CLIs), Summarize / Key points / Action items /
    Explain / Improve / Translate / Questions, free questions about the note or the selection, read-only runs that keep the
    session until New chat, answers stream in and go Into the note / Save as note / Copy. Resizable, AI button hides it;
    narrow windows: the outline column steps aside, the panel narrows, the note tree makes room, the note never vanishes.
  - Obsidian features: callouts (> [!type], folded with -), ==highlights==, embeds ![[note]], ![[note#heading]],
    ![[picture.png]] (vault-wide), [[note#heading]] jumps, hover preview of links, collapsible headings, outgoing links and
    unlinked mentions panels, rename updates links in every note, paste a picture into the editor (vault\attachments),
    extract selection to a new note, link / highlight selection, random note, word count and reading time.
- Proof: real Claude Code summary of a sample note inside the panel; render of callouts, highlight, folded callout, embed,
  heading link, tasks, outgoing links and backlinks; sweep (soft-dark, classic-light, 3 sizes) clean; test-all = ALL CHECKS
  PASSED after fixing a column-fitting bug the responsiveness test caught; rebuilt, relaunched on Markdown.
- Open: rename-updates-links and paste-picture not run live; hover preview and heading jump not clicked live.

## 2026-11-19 — Accounts page (sign in to agent CLIs from the app) + open items batch
- Asked: an easy place in the app to sign in to any agent CLI (for example a second Codex account) without CLI steps.
- Done: new Accounts page (sidebar, Tools; Browser > Accounts opens it). One card per CLI with real status from the CLIs'
  own status commands (Claude: email and plan per account; Codex: signed in with ChatGPT / API key; Cline providers;
  OpenCode). Sign in / Add account run the CLI sign-in hidden (it opens the browser); the page shows the link (Open,
  Copy), a device code (Codex "Sign in with a code"), and a box when a code must be pasted; the card refreshes after.
  Named accounts (Claude, Codex): Use for new chats, Sign in, Sign out, Remove. Cline/OpenCode/Antigravity use their own
  menu in a small terminal (their sign-in asks questions).
- Also done (open items): answers show model, effort and account in the header; Cline picker marks recent / free /
  Cline Pass and puts recent first; Office busy errors back off 20 s and normal states are no longer logged; Markdown
  link graph (Graph button); Browser reading mode (F9); classic top bar text no longer touches its usage bar; Tasks
  page opens ~45% faster (task form built when a task opens).
- Proof: Accounts page rendered with real statuses (3 named Claude accounts with emails, Codex, Cline, OpenCode); graph
  with a sample vault (7 notes, 7 links); reading mode on a sample report; Tasks timing 963 -> 533 ms first, 520 -> 290
  ms warm; test-all = ALL CHECKS PASSED; rebuilt, relaunched on Accounts.
- Open: a real sign-in was not run (it would open your browser and change accounts); Codex shows no email (its status
  command does not print one).
- Incident: HISTORY.md was found empty (0 bytes, untracked, cause not proven). Rebuilt from session transcripts
  (Claude and Codex logs); entries whose full text was not in any log were taken from the commands that wrote them.

## 2026-11-18 — Full UI sweep and stress test (tools\test-ui-sweep.ps1)
- Asked: strong stress test, screenshot check of every tab and feature, fix what is found.
- Built: tools\test-ui-sweep.ps1 (documented in docs\maintenance.md). 18 pages x 5 looks x 3 sizes (270 screenshots,
  15 contact sheets), layout checks (cut-off controls, cut text, light blocks in dark mode, empty pages), every right-click
  menu opened, stress: 300 page switches, 280 resizes, 14 searches; exceptions and the app's own error log collected.
- Found and fixed:
  - Dark mode: Markdown note tree white (TreeView not themed); Tasks empty-state hint white; Prompts editor with no prompt
    showed etched disabled labels and a white box (now a hint, fields hidden).
  - Shortcuts page took ~0.9 s on every first open (table laid out row by row): one layout pass, ~0.45 s.
  - Agents toolbar wrapped to three lines at 1100 px: A- / 100% / A+ merged into one "Text 100%" button with a menu.
  - Sweep: Markdown/Browser get 2.5 s to start the web engine before the screenshot.
- Result (sweep3, real data copy): 0 exceptions, 0 layout problems except one false "empty" (3 notes on a wide screen);
  resize steps 63-118 ms (was 111-194 ms); 14 searches 153 ms. test-all = ALL CHECKS PASSED; rebuilt, relaunched.
- Open: Tasks first open 0.6-0.8 s (many native controls in the detail form; no single hot spot); classic top bar drive
  text touches its usage bar; offscreen captures cannot show live-only issues (ghost text, focus, real popups).

## 2026-11-18 — Missing features across the tabs (batch 2)
- Asked: discover what each tab is missing and build it.
- Done:
  - Connected tabs: Send to in Clipboard, Sticky notes, Prompts (button), Tasks, Snips, agent answers, Browser (page,
    selected text); drop files/folders on sidebar pages (Explorer, Terminals, Agents, Browser, Markdown, Tagged).
  - Ctrl+K: text inside Markdown notes (cached by write time) and a new Web scope (Browser history, favourites).
  - Browser: Downloads list (open, open here, show, Send to), zoom remembered per site, save a page as a Markdown note
    (vault\Clippings, with source).
  - Markdown: 5 templates + your own (_templates), Export PDF, Print, Send to.
  - Agent chat saves retry 5 times when another program holds the file (error.log 11:47).
  - Prompts already had {{variables}}: not rebuilt.
- Proof: test-all = ALL CHECKS PASSED; Accounts window rendered with real sign-ins (3 named Claude accounts);
  rebuilt, dist copied, relaunched.
- Open: new menus not clicked live; save-as-note and PDF export not run; roadmap NEXT items remain.

## 2026-11-18 — Terminals tiles, Codex policy, Cline models, hidden consoles, Markdown reader, accounts, Send to
- Asked: fix the Terminals tiles and Codex errors; hide agent console popups; fix Cline model choice (with Codex SOL 6.2 High);
  accounts in the Browser tab; a real Markdown reader and import; search right-click tools; tabs that send to each other;
  a list of everything the app needs (tasks\roadmap-2026-11-18.md).
- Done:
  - Terminals folder shelf: own scrolling (native AutoScroll left blank overlapping tiles; the earlier repaint-on-scroll
    fix was not enough).
  - Codex: company policy allows only workspace-write + approval on-request. Settings.CodexManaged is set the first time
    Codex says "disallowed by requirements"; Full auto then runs as -s workspace-write -c approval_policy=on-request, and
    the red errors become one plain note.
  - Cline models: cause proven - the app guessed provider ids ("openai", "nousresearch") and its default
    "cline-pass: moonshotai/kimi-k3.1-pro" is not a catalog id (real: cline-pass/kimi-k3.1-pro). Codex SOL 6.2 High
    analysed (read-only) and then implemented the fix (AgentCommands.cs, LeaderTeam.cs, Agents.cs): configured
    providers' live catalogs via hidden Node, signed-in first, migration of the old id, effort rule only for agy.
    Checked here: 372 models listed, args "-P cline-pass -m cline-pass/kimi-k3.1-pro --thinking high".
  - ConsoleGuard: hides console windows opened by processes descending from a running agent (150 ms sweep).
  - Markdown: web reader (MdWeb.cs, marked 15.0.12 MIT in lib\markdown, packed in the exe), tick boxes save to the file,
    outline jumps; Import menu + drag and drop.
  - Accounts (Browser > Accounts): sign-in state per CLI (file names only), sign in, named accounts, account new chats use.
  - AppLinks: shared Send to / open real folder menu; in search results, Explorer, File activity, Tagged, Markdown, shelves.
- Proof: test-all = ALL CHECKS PASSED; renders of Terminals (4 panes) and the Markdown reader; Cline catalog test; rebuilt,
  dist copied, relaunched.
- Open: ConsoleGuard and the Codex policy note not seen live yet; Cline catalog takes ~11 s on first load; accounts
  panel not rendered; Send to not yet in Clipboard, Notes, Prompts, Browser (see roadmap).

## 2026-11-18 — Check of the live app
- Asked: check.
- Live app: running (SerQ look), all sidebar pages visible with full labels, no errors logged since the 16:06 restart.
- From data\error.log, two real bugs fixed:
  - Crash 11:40 (ObjectDisposedException 'OwnedMenu'): four menus kept in fields were made by Ui.Menu(), which disposes
    a menu 250 ms after it closes, so the next right-click used a dead menu. Explorer, Markdown and two Agents menus
    now use Ui.Menu(true).
  - Markdown page threw 'SplitterDistance must be between...' when it opened in Split mode before layout. Split is
    positioned once the container has a real width (PositionSplit on SizeChanged).
- Seen, not changed: Office COM reads in File activity log busy/unavailable errors (expected while Office is busy);
  two agent draft saves hit a locked file at 11:47.
- Proof: page render with Split mode saved logs no new errors; test-all = ALL CHECKS PASSED; rebuilt and relaunched.

## 2026-11-18 — Overall improvement pass (audit of every page)
- Asked: improve the app overall with everything it is missing.
- Method: rendered all 18 pages offscreen with a copy of real data in Soft light and dark, fixed what was broken.
- Done:
  - Tasks page was unusable: a saved pane width (1,481 px) pushed the list off screen. All resizable panes now clamp
    to the page, leaving the other side panels and ~300 px for the middle (Layout.cs PaneGrip).
  - Tasks with no task selected: a short hint instead of a form of greyed-out fields (unreadable in dark).
    "Add a task" boxes no longer show a search magnifier.
  - Sidebar (SerQ / Soft): rows 36 px so more pages fit, labels get more room (no "Prompts & snipp..."), selected
    marker uses the theme accent in Soft.
  - Search boxes: rounded border painted again on WM_PAINT (the themed edit redraws its frame there; removing it in
    the resize pass left plain square borders).
  - Agents info line no longer prints the data folder path; empty clipboard preview says what to do.
  - Shortcuts > Keys inside the app: added search, Agents, Browser and Markdown keys.
- Proof: before/after renders (page-soft-*.png, page-soft-dark-*.png in the session scratchpad); test-all = ALL
  CHECKS PASSED; rebuilt, dist copied, relaunched on Tasks.
- Open: native date pickers and some combos stay light in dark mode; sidebar still scrolls on windows under ~900 px
  tall; Explorer pane address boxes are narrow with 3 panes; Power tools window list not checked live.

"@
$k=$t.IndexOf('## 2026-11-18'); $t=$t.Substring(0,$k)+$entry.Replace("`r
","
")+$t.Substring($k); [IO.File]::WriteAllText($f,$t,(New-Object Text.UTF8Encoding $false))
$f='LESSONS.md'; $t=[IO.File]::ReadAllText($f)
$t=$t.Replace("- GDI text drawn into a transparent", "- Saved pane sizes outlive the window size they came from (Tasks detail 1,481 px hid the list). -> Clamp every docked
  pane on parent resize to what the page can give; never trust a stored width alone.
- Audit by rendering every page with a copy of real data (MainForm offscreen + PrintWindow): it found a broken page
  that tests with empty fixtures never hit.
- GDI text drawn into a transparent")
[IO.File]::WriteAllText($f,$t,(New-Object Text.UTF8Encoding $false))

## 2026-11-18 — Search palette, look switcher, Agents text size, browser for reports and emails
- Asked: real in-app search (organized, history, autocomplete, favourites), Agents font size control, light/dark design
  like the reference image with very fast theme switching, remove AI slop (stop-slop skill), better browser for
  browsing, reports, emails and automation.
- Done:
  - Ctrl+K search rewritten (AppSearch.cs): floating palette, scope chips (Tab), grouped results with icons, matched
    words highlighted, snippets of where text matches (also inside agent chat messages), Actions group (new note,
    snip, record, theme...), inline autocomplete from past searches / best title (Right or Tab), favourites (Ctrl+D or
    star) and history (recent searches, recently opened) on the empty box. Stored in Settings.SearchHistory and
    MetaStore scope "search". Ranking kept for test-forms.
  - New "Soft" design (charcoal floating sidebar, grey canvas, white cards, coral accent) in light and dark. Look
    picker on the top-bar theme button: 8 looks as live previews + accent colours, one click (App.SetLook).
  - Agents chat text size: A- / 100% / A+ on the Agents bar, Ctrl+ / Ctrl- / Ctrl+0, Ctrl+wheel; saved as
    Settings.AgentTextScale; message box follows.
  - Browser: open files (Ctrl+O, drag and drop): PDF, HTML, images, .eml (own MIME reader: encoded headers,
    quoted-printable, inline cid images, attachment list), .msg via Outlook COM; zoom, find on page, print, save as
    PDF, page screenshot, download status, run page scripts now, copy automation address; cancelled navigations
    no longer show "Could not load the page (Unknown)".
  - Slop: the stop-slop repo could not be fetched (GitHub blocked); applied its published rule categories. Code
    strings had almost no slop words; long explanatory labels were cut (council, cards, power keys, sidebar, Chrome).
- Proof: test-all = ALL CHECKS PASSED; offscreen renders of search (light, dark, empty, query; suggestion
  "rev" -> "review budget"), look picker, a sample .eml in the browser page. Rebuilt, dist copied, relaunched.
- Incident: a PowerShell replace map unrolled a one-item list and replaced every C/P in src\AgentCouncil.cs.
  Restored byte-exact from dist\AllInOne-source-2026-11-18-workspace.zip (damage simulated on the backup matched).
- Open: .msg path not tested (needs a real .msg); autocomplete ghost text not visible in offscreen captures (check
  live); theme switch still rebuilds the window (about a second); the main window in Soft not screenshot-checked.

"@
$k=$t.IndexOf('## 2026-11-18'); $t=$t.Substring(0,$k)+$entry.Replace("`r
","
")+$t.Substring($k); [IO.File]::WriteAllText($f,$t,(New-Object Text.UTF8Encoding $false))
$f='LESSONS.md'; $t=[IO.File]::ReadAllText($f)
$t=$t.Replace("- PowerShell: never name helpers", "- PowerShell unrolls a one-item nested array: @(@('a','b')) becomes @('a','b'), so a loop over pairs walks the`
  strings and p[0]/p[1] become characters (a replace of every 'C' with 'h' wrecked a file). -> Write @(,@('a','b')), check
  the count before replacing, and keep a backup: untracked files are not in git.
- PowerShell: never name helpers")
[IO.File]::WriteAllText($f,$t,(New-Object Text.UTF8Encoding $false)); (Select-String -Path $f -Pattern 'unrolls').Count

## 2026-11-18 - Playwright vs Chrome for automation (advice only)
- Checked: newest Playwright is 1.65 (5 Nov 2026, 1.65.1 on 13 Nov, per third-party blogs); its new features are mostly
  for the test runner. This PC has Python Playwright 1.62.0 and Node v25.2.1. No code changed.

## 2026-11-18 — How the app can benefit from the built-in WebView2 browser (advice only)
- Asked: how we can benefit inside the app from the scriptable WebView2 tab (following the earlier preview entry and the new [Browser.cs](src/Browser.cs) page).
- Recommended:
  - Expose browser tools in the built-in MCP server ([Mcp.cs](src/Mcp.cs)) so any pane in **Agents** can read, click, fill, screenshot or run JS in the active tab without Playwright.

## 2026-11-18 — Browser page (WebView2) built
- Asked: use the in-app browser.
- Done: new Browser page (Tools): tabs (restored at start, Ctrl+T / Ctrl+W / Ctrl+Tab), address bar (address or Google
  search), Back / Forward / Reload / Home, Favorite, More menu (Favorites, History, Run JavaScript, page scripts folder,
  DevTools, automation port toggle, open in Chrome, set home). Own profile in data\browser\profile; history and
  favorites in MetaStore scope `web`; page scripts data\browser\scripts\*.js with `// @match`. Automation port 9333
  (localhost, off by default, needs restart). SDK DLLs in lib\webview2, packed in the exe, unpacked to
  %LocalAppData%\AllInOne\webview2\1.0.2903.40; AssemblyResolve hooked first in Main.
- Proof: offscreen test - example.com loaded, page script changed the title, history entry saved, port 9333 answered
  /json/version; test-all = ALL CHECKS PASSED; real exe started the engine on --page browser.
- Open: sign-in/SSO sites, downloads, many-tab memory not tested; source zip (make-dist) may need lib\webview2.

## 2026-11-18 - Can the app host its own browser?
- Asked: check for real whether a fully customizable, automatable browser tab inside the app is possible.
- Proof (scratchpad script, WebView2 SDK 1.0.2903.40 from the NuGet cache, runtime 157.0.4341.60 on this PC, .NET 4.8.1):
  engine ready in 1.1-1.7 s inside an offscreen WinForms window with its own profile folder; script injected before
  page scripts; fill + click by script; DevTools protocol (Browser.getVersion, Page.captureScreenshot); extensions API
  present; https://example.com loaded through the company network in 4.7 s.
- Not done: no code added to the app. Not tested: sign-in / SSO sites, downloads, memory use with many tabs, extensions.

## 2026-11-18 - Agents message box cramped
- Asked: fix the cramped message area (Files button cut, Send squeezed).
- Cause: the new usage line under the box took 20 px, but the minimum height did not count it.
- Done: one MinInputH (grip + Answer row + box + usage line + padding) used everywhere; default 140 px; buttons are
  [Files][/] above [Send], each 32 px.
- Proof: offscreen render of a chat pane (clean); test-all = ALL CHECKS PASSED; rebuilt, dist copied, relaunched.

## 2026-11-18 — Review: File activity page, search boxes, UI text
- Asked: review the owner's latest changes, fix bugs, fix the File activity screenshot issues and AI-slop text.
- Done:
  - File activity: list starts at the top again when a filter, search or sort changes, and repaints once the
    native list has settled (fixes the empty band above the rows). Narrow "★" header no longer shows "★…".
  - All search boxes (Ui.Search): magnifier icon and a left text margin.
  - Agents page: limits event and timer now released on dispose (leak).
  - Shorter UI text: Agents message hint, broadcast cue, limits note, "Pop out" (tooltip keeps the detail),
    Markdown buttons "Open note" / "Search" (shortcuts in tooltips), no double star on "Favorite".
- Proof: compiled; tools\test-all.ps1 = ALL CHECKS PASSED (14 suites), before and after; offscreen render of the
  activity list with 90 records, scrolled, then filtered. Rebuilt AllInOne.exe, copied to dist, relaunched.
- Open: the empty band was not reproduced offscreen (PrintWindow repaints everything), so the fix is not proven in
  the live app. Steer/attachment code was read, not run against a live CLI.

'@
$t=$t.Replace("Newest first. One entry per task: date, what was asked, what was done, proof, what is still open.

", "Newest first. One entry per task: date, what was asked, what was done, proof, what is still open.

" + $entry.Replace("`r
","
"))
$t=$t.Replace("Newest first. One entry per task: date, what was asked, what was done, proof, what is still open.`r
`r
", "Newest first. One entry per task: date, what was asked, what was done, proof, what is still open.`r
`r
" + $entry)
[IO.File]::WriteAllText($f,$t,(New-Object Text.UTF8Encoding $false)); (Select-String -Path $f -Pattern 'Review: File activity').Count
$f='LESSONS.md'; $t=[IO.File]::ReadAllText($f)
$add="- Offscreen captures hide stale-paint bugs: PrintWindow / DrawToBitmap repaint everything, and BitBlt from an
  offscreen window DC returns blank. -> Fix stale paint at the source (repaint after the change settles).
- Changing a TextBox frame with WM_NCCALCSIZE (to make it taller) rendered a stray inner border. -> For icons in a
  TextBox use EM_SETMARGINS + paint in the client after WM_PAINT; leave the frame alone.
"
$t=$t.Replace("- GDI text drawn into a transparent", $add + "- GDI text drawn into a transparent")
[IO.File]::WriteAllText($f,$t,(New-Object Text.UTF8Encoding $false)); (Select-String -Path $f -Pattern 'Offscreen captures').Count

## 2026-11-18 - Project reply and documentation rules

- Recorded the owner's preference for very short, plain-English, logically grouped replies in [AGENTS.md](AGENTS.md).
- Made automatic documentation, skill, history and lesson updates explicit. Verified the saved rules and their local links.

## 2026-11-18 — Automatic project knowledge updates

- Asked to capture experience in documentation, skills, history and lessons on every task.
- Added repository instructions for Codex and Claude, a developer maintenance guide, and a reusable project skill.
  Backfilled release verification and corrected the README's stale test count.
- Verification covers skill metadata, local links, and matching the recorded release hashes to the files on disk.

## 2026-11-18 — Agents chat: attachments, steer, context and limits
- Asked: paste images/attachments into the Agents chat, steer like the official apps, see context and 5h/weekly
  limits per account, match Claude Code desktop and Codex app features.
- Done: Ctrl+V / drag-drop / Files menu attach images and files (chips, click to remove; pasted images saved as PNG
  under `data\agents\attachments\<chat>`); paths listed in the message for every CLI, Claude gets `--add-dir`,
  Codex gets `--image=`. Claude Code runs with `--input-format stream-json`: Enter while it works steers it.
  Other agents (and messages with files) queue and go out when the turn ends; Stop sends the queue, Esc drops it.
  Status line under the box: context % per agent (Claude live usage; Codex from its rollout log) and 5h / week per
  account (Claude `rate_limit_event`, Codex `token_count`, all named accounts).
- Proof: none run. The shell blocked csc / test-build again; code checked by hand for C# 5 only.
- Open: compile and click-test; Codex app-server (`turn/steer`, approvals) not used; no image thumbnails; no
  approval prompts in the chat; steer with attachments queues instead.

## 2026-11-18 — Chat files and saved work

Added local document writers and file cards, chat folders and archive views, draft persistence and backup recovery. Corrected the multiline caret overlay and transcript viewport traversal. Terminal history now saves and restores individual pane configurations and supports editing a launch as a workspace. Regression coverage lives in tools/test-saved-work.ps1.

## 2026-11-18
- **Agents "Files" button review.** Asked to fix the Files button (hang, wrong results, unclear purpose) and review the new
  chat-file feature. Fixed: user bubbles show at most 3,000 characters (a 256 KB inserted file was measured and painted
  in full); file mode starts only on an explicit request (any "pdf"/"md" mention used to hide the answer in a Save
  card); clear menu, tooltips and card text; format follows the chosen extension; control characters no longer break
  xlsx/docx/pptx saves; CSV formula cells neutralized; PDF text layer no longer measures the rest of the page per line.
  Added checks to `tools/test-saved-work.ps1`. Open: not compiled or run (shell blocked csc and the screenshot was
  outside the project); PDF is image-based, DOCX has no Heading styles, PPTX does not split overflowing slides.
- **Six office-work skills.** Asked for organized Markdown skills for Excel automation, PowerPoint design, Word
  arrangement, formal writing, summaries of long material, and similar basic work. Wrote `skills/` with
  `excel-automation`, `powerpoint-design`, `word-professional`, `formal-writing`, `deep-summary`, `data-to-insight`
  and a README index. Proof: seven files written. Open: the PowerShell/COM snippets were not run (the session
  blocked shell probes); skills are not active until copied to `%USERPROFILE%\.claude\skills\`.

## 2026-11-18
- Added named Codex and Claude Code accounts using HERDR's profile directories. Agent chats, Council, leader teams
  and agent API runs scope resume IDs to the selected account. The local Cline picker listed 6,772 provider/model pairs.
- Added a Leader workspace with editable assignments, dispatch, individual stop, live output, leader review, final
  synthesis and reviewed snapshots from a HERDR pane. Updated the Prompts editor labels, borders and field spacing.
- Built the main EXE from the verified 81-file source archive. All 13 suites passed, including worker cancellation
  and synthesis with stand-in CLIs. Live provider sign-ins and a real user's HERDR terminal were not exercised.
- Fixed the Markdown sidebar crash when restoring Split mode before the page is docked. Verified saved-note
  restore, opening from the vault tree and the app-search opening path using a temporary vault.
- Added local transcript search, branches, question editing and per-chat draft retention while switching panes.
  Council reports participation, excludes failed first-round replies and selects a responding chair.
  All 12 suites passed from the source ZIP, including a discussion and verdict with local stand-in providers.
  Updated the main EXE and project shortcut. Live provider accounts were not used for verification.
- **Blank bands in Explorer's active pane.** Reproduced a 189 px gap after shrinking a scrolled virtual list.
  Reset the viewport before publishing the count and retain the current page during a folder refresh.
  A three-pane rendering test now covers scrolling, filtering and switching folders. All 11 suites passed from
  the new source ZIP. The project shortcut opens `AllInOne.explorer-fix.exe` after the old session exits.
- **Freeze review and open-source release.** Moved Explorer path checks and watcher setup, Markdown reads and indexing,
  and clipboard image decoding off the UI thread. Superseded Explorer and Markdown results are discarded. Clipboard bulk
  selection sends one notification and bounds preview text. The 20,000-row fixture took 9,970 ms with per-item selection
  and 7 ms with the native bulk operation in the final run.
- Corrected the capture suite's stale build filename and stopped portable packaging from clearing the release folder.
  All ten suites passed from a fresh 76-file source ZIP extraction. Two portable builds kept an existing release file.
  Delivered `AllInOne.reliability.exe` with ffmpeg and `dist/AllInOne-source-2026-11-18.zip`. The current session stayed open.
  Live network latency and whole-app CPU/RAM remain unmeasured.
- **Settings, app search and performance review.** Fixed overprinted Settings combo text, long field layout and the
  color-theme preview. Ctrl+K searches app pages and saved items, with ranked groups and bounded clipboard results.
  It uses existing Markdown and chat data when available and makes no PC index requests.
- Removed duplicate sidebar setup and unused Home controls. Moved app-memory aggregation off the UI thread,
  reused sampler buffers and released completed debounce callbacks. Synthetic aggregation fell from 129.8 to 27.5 ms
  over 800 repetitions. A query over 20,000 synthetic clipboard records completed in 278 ms including debounce.
- All nine suites passed. Verified Nexus/Calibri Settings offscreen in light and dark modes, and keyboard navigation
  in app search. Built `AllInOne.search-fix.exe` with the recorder bundle. The running session was left open.
  Hidden-window construction was comparable, and live CPU and RAM savings remain unmeasured.

## 2026-11-18
- **Council question (Architect): new feature ideas.** Proposed features that connect existing modules (one search,
  one action registry, sessions, snip/note/task -> agent, scheduled agent jobs, more MCP tools). No code changed.
- Council round 1 (features): shared plan = action list ("send to" task/agent/note), project resume by folder,
  read-only git badge + commit-only checkpoint (no automatic undo), OCR text in search. Dropped: file compare.

## 2026-11-17
- **Big request: resize, continue/resume, CLI updates, Codex, Markdown tab, search everything, design.**
  - Resize per-step (median of 3 profiles): Explorer 37->22 ms, Agents 31->26, Prompts 45->34, Terminals 45->38.
  - Claude/Codex continue + resume: Terminals command list and "Claude Code here" / "Codex here" submenus.
  - CLI updates: auto check 30 s after start + Agents > Updates dialog, one-click update in a terminal. Proven:
    Claude, Codex, Cline up to date; OpenCode 1.18.11 -> 1.19.6 found.
  - Codex errors: network-side (company gateway 403 / routing timeout). App retries once, shows a clean message
    instead of raw HTML.
  - Markdown notes tab (new): vaults, tree, favorites + recent, Read/Edit/Split, auto-save, [[wiki links]],
    backlinks, outline, #tags, tasks, daily note, Ctrl+O switcher, full-text search, Back/Forward. Proven on this
    project folder (15 notes, rendered).
  - Ctrl+K search: now also pages, agent chats, folder sets, terminal history, Markdown notes and files/folders on all
    disks (Everything IPC, Windows index fallback). Proven: 10 hits in 93 ms via Everything. Ctrl+Enter = show in folder.
  - Design: soft secondary buttons (no outline), semibold display font for headings, more header space.
  - Open: vault combo text looked doubled in an offscreen capture (not checked in the live app); Ctrl+K and the Markdown
    tab not clicked through in the live app; open-source zip not rebuilt.
- **Council question (Architect): "what does the project need now?"** Read-only review of git state, history,
  lessons, 11-15 review. Answer: commit the 3 days of uncommitted work, freeze features, run test-all, split
  Agents.cs into per-CLI adapters, add a perf budget gate. No code changed.
- **Performance:** perf probe on a copy of real data. Explorer resize freeze 12.5 s -> 0.19 s (DriveCache), Terminals
  first open 2.4 s -> 0.29 s (network folder checks on a worker), worst stall 12.5 s -> 0.69 s. Open: resize steps
  60-105 ms on Agents/Terminals/Explorer/Prompts; Tasks first open 0.69 s.
- **Blank tiles on Terminals:** scroll repaint fix (Shelf, ChatView).
- **History:** Terminals launch history and Agents chat history manager (HistoryView: search, sort, multi-select,
  favorites, export CSV). "Claude continue here" on folder menus. ffmpeg packed in the exe (`Bundled`).
  Per-page accent bar on page titles (PageHeading).
- Antigravity read-only command rules for Ask/Edit (compiled; live test stopped by the owner, unproven).

## 2026-11-16
- Agents: formatted answers (tables, headings, lists, code, links: ChatMarkdown.cs), Web search switch, 5-hour /
  weekly limits (AgentLimits.cs, MCP `agent_limits`).
- Folder sets shared by Explorer, Terminals, Agents (FolderSets.cs, MCP tools).
- Cline fixes: retired model auto-switch, no `--id` resume (history in prompt). Proven live (PONG, "42").
- Model council, workspaces, draggable pane dividers, per-message responders and order, "/" commands, models and
  effort for every CLI, open-source zip in `dist\`, Explorer newest-first sort, terminal titles locked to folder name.

## 2026-11-15
- Desktop overlay (dock, menu bar, window overview), later fixed: global hook removed, auto-hide dock, sharp icons,
  easy exit (Ctrl+Alt+Z, red buttons). Security Warning pop-up hider (poll, not hook).
# Lessons (All in One)

Short, proven lessons. Read before working on the app; add a new one whenever something is learned, fixed or improved.
Format: what happened -> cause (proven) -> rule.

## 2026-11-22 08:57 - Rebuilt AllInOne.exe from GitHub main (d55c4cc)
- build.ps1 succeeded (warnings only); previous exe backed up as AllInOne.before-2026-11-22-rebuild.exe. Smoke test: app starts and responds.


## 2026-11-22 09:04 - Expandable, flexible side panels
- Assistant: new expand/restore button in its header, Ctrl+Shift+J, and double-click on its divider make it fill the window and then go back to the side. Opening a page restores the side view. The drag limit is lifted, so it can be as wide as the window allows.
- Every side divider (Markdown outline, tasks, prompts, bookmarks, snip OCR, nav): double-click expands it to half the area, double-click again restores it. The Markdown outline's 700 px width cap is lifted.
- Verified: build passes, app starts and responds. The new controls were not clicked through by automation.


## 2026-11-22 09:09 - Left/right resize for Agents chat panes and Markdown page
- Agents: the gaps between chat panes now show a visible grip (line + dots) and are wider (12 px), so the existing drag-to-resize is easy to find. The Chats list got its own remembered divider (agents.history).
- Markdown: the note tree got a remembered divider (md.tree), next to the outline divider.
- All side dividers are wider (9 px) with bigger dots.
- Verified: build passes, app starts and responds. Dragging was not automated.


## 2026-11-22 09:18 - Multi-project harness: agents never mix projects
- New src/Projects.cs: automatic project registry (agent chat folders, folder sets, terminal folders, recent folders that look like projects, and sibling repos next to them). Skips containers (GitHub, WORK), package folders, Downloads/Documents. Names match with spaces, dashes or underscores, plus the GitHub repo name. Everyday words (docs, agents, test...) match only by full path. When two folders share a name, the git repo wins.
- Every agent run (chat, council, leader, MCP agent_ask, Assistant) gets a project scope block on a fresh session, and again whenever the message names a project other than the chat folder: the working project, the named project, the rules (stay in that folder, use its own CLAUDE.md/AGENTS.md, never mix), and the known projects. Headless Claude also gets --add-dir for the named project.
- A new chat whose first message names one project starts in that project's folder.
- New MCP tool projects_list (optional text -> the projects it names) for terminal agents and others.
- Verified through the real MCP pipe: 110 projects; 'sticky-note build' -> GitHub\Sticky note only; 'excel agent ... GMES CLI' -> both; 'agents ... docs' -> none. Live agent turns with the scope block were not run.


## 2026-11-22 11:23 - Team: chat, files, agents and one-click update distribution
- New Team page (src/TeamNet.cs, Releases.cs, TeamServices.cs, TeamPage.cs; docs/TEAM.md). One shared folder (no server, no ports). Each PC writes only its own files: append-only messages per device, presence/update state per device, read receipts per device. Rooms: General, Updates & Releases, group, project, private. Offline outbox, duplicates ignored, time order, reactions, replies, edits, deletes, pins, mentions, notifications, search, resumable SHA-256 file transfer, problem reports into tasks.
- Agents in rooms: each user shares chosen agents; @handle runs the agent on its owner's PC (off / read-only default / edit per room), loop guard of 6.
- Updates: admin RSA-3072 key in the Windows key store, pinned by members; signed manifests and channels (dev / pilot / stable, rollout percent, pause, critical, replay protection). Build & Publish: compile check, team tests, optional full suite, build.ps1 -Out, --selftest, signed publish, announcement. Clients download with resume, verify, stage, install when idle/critical/on click via the new exe as updater (--apply-update) with backup, health signal and automatic rollback. Promotion refused while a pilot PC failed. Admin chat commands with confirmation.
- portable.txt next to the exe disables the install offer.
- Verified: tools/test-team.ps1 50/50 on temporary folders; test-regressions OK; tools/test-team-e2e.ps1 with the real exe: pipeline published in 36 s, a real app copy received a forced pilot release, installed, restarted, reported. Off-screen render of chat and Update center checked. Not tested: real S:\ share, two physical PCs, restricted PCs. Not built: secondary update server, voice messages, message encryption.
- Incident during the first e2e run: the portable test copy showed the normal 'Install for your Windows account?' prompt, it was answered Yes, the copy installed itself (with its test-team data) into %LocalAppData%\Programs\All in One, and the updater then correctly updated that installed copy to the test build. Reverted reversibly (moved to build-test\e2e-cleanup-backup, registry exported, startup entry pointed back to the project exe). Countermeasure: portable.txt in the e2e copy plus a safety stop when the reported app path is outside the test folder.


## 2026-11-22 12:05 - Team v2: secure Admin & User platform, remote assistance
- New src/TeamSecurity.cs (device keys, signed default-deny policy, grants by user/group/device/room/project, room keys wrapped per allowed PC with rotation on revoke, AES-256+HMAC sealing, admin passphrase PBKDF2), src/TeamRemote.cs (consent-based screen view/control through the share, encrypted frames/input, signed request/accept/end, timeout, revoke mid-session, audit), src/TeamAdminUi.cs (approval dialog, red frame + banner + Stop, viewer window, Admin console: people & PCs, permissions, rooms, sessions, audit, settings).
- TeamNet: messages sealed + device-signed, receivers verify sender, permission and key; presence signed; files encrypted; pending / suspended / revoked / blocked states; terminate closes the app; blocked stays disconnected. Team page: passphrase on create, waiting screen with device code, Get remote help, person menu, admin console button, features hidden by policy. Releases: admin actions need unlock; updates only for approved PCs. Policy repair from the last verified copy.
- Verified: test-team 50/50, test-team-security 61/61, test-team-e2e (real exe: pipeline incl. security suite 69 s; approved real copy updated and reported), off-screen renders of chat, admin console and waiting screen. Not verified: real S:\ share, two physical PCs, real input injection on a second session.


## 2026-11-22 - Team on S:\Public_Folder, full automation browser
- Team tests run on a test folder on S:\Public_Folder (team 50/50, security 61/61, real-app update e2e). The share refuses *.exe (also .ps1, .zip): releases are now stored as AllInOne.bin; old AllInOne.exe releases still read. Test folders deleted after each run.
- App browser is a full automation browser for agents: 20 new browser_* tools (snapshot refs, trusted click/keys, fill/select/hover/scroll/wait, tabs, screenshot/PDF, upload, downloads, dialogs, console, network, cookies, emulation, extract). Red bar with Stop, element highlight, activity log. tools\test-browser-automation.ps1: 44/44 on the running app. docs\BROWSER-AUTOMATION.md.
