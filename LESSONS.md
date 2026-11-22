# Lessons (All in One)

Short, proven lessons. Read before working on the app; add a new one whenever something is learned, fixed or improved.
Format: what happened -> cause (proven) -> rule.

## CLI account status

- Cline 3.0.74 can report an active Cline Pass sign-in with an empty legacy secrets file. Use the installed CLI's provider status, expose provider names only, and distinguish an unavailable check from a confirmed sign-out. A configured provider can exist without credentials.

## Release checks

- The Team page can open from the sidebar and `--page team` even when the local API rejects it. The API uses a separate page list. Verify the actual Team page through a normal entry point before treating the API response as a desktop startup failure.

## Operating environment & automation
- **Security audits repeatedly flagged open local MCP Named Pipe, Assistant `Full Auto`, and unsanitized local `MdWeb`/`Mail` HTML.** -> The app runs inside a closed, isolated corporate/factory local intranet behind a strict enterprise firewall, and the owner intentionally wants maximum unrestricted automation across local tools. -> **Rule:** Treat unrestricted local MCP, Assistant `Full Auto`, and raw local `MdWeb`/`Mail` rendering as intentional by design. Never flag them as open security issues, never add ACL/confirmation gates or restrictive sanitizers for them, and never ask the owner again whether to close them. Keep focus on reliability, saved-work safety, UX, performance, and new capabilities.

## Saved work
- **SnipEditor `Copy file` / `Copy path` shared the unredacted raw capture after Blur/edits.** Auto-save creates `filePath` immediately, so checking `!File.Exists(filePath)` skipped `Save(false)`. -> Track a `dirty` flag in `SnipEditor` and flush edits before copying paths/files or closing.
- Gate Markdown tab close, restart and theme rebuild on a successful save. Write through a sibling temporary file so a failed write preserves the previous file. Hold the original against replacement in the regression fixture and verify the edit survives in the open tab.
- Schedule a data-folder move and copy after the old process exits. Exercise a late edit, addition and deletion, plus a target that becomes occupied before startup. A locked primary is a read failure even when its older backup is accessible. A malformed primary can use a validated backup.
- **Closing an Office tab could discard edits after a failed save.** Check Cancel and the save result before removing the tab or disposing the editor. Apply the same check to reload, preview mode, theme rebuild and normal exit. In tests, make a stand-in document reject Save and verify it remains dirty.
- **Office can land behind the host after focus changes.** Verify the native window order again and position the editor directly above its host. Throttle retries. Put both test windows on a private desktop and establish the incorrect order from the editor's own UI thread before testing recovery.
- **Unsent drafts disappeared after restart.** Draft text lived only in the pane. Save it separately from the transcript so typing doesn't rewrite a long chat. Exercise the first draft, clearing it, switching chats, and reopening saved data.
- **A damaged primary chat file blocked recovery.** Try the backup and preserve the known-good copy when replacing a damaged primary. Test recovery and ensure a cleared draft stays cleared.
- **Nested Markdown fences could truncate a file card.** Match the outer fence length and wait for its closing marker before offering a save.
- Terminal history previously joined pane commands into one display string. Keep the original pane records for replay and treat older summaries as display text.
- A label docked inside a multiline edit control covers the native caret. Hide it as soon as the editor receives focus, including when the draft is empty.

- A saved schedule needs recorded execution state as well as an alert. Write the queued state first, and mark an in-flight action interrupted when startup finds it. When restoring an older backup, leave unfinished jobs disabled until reviewed so the backup cannot replay them.
- Copying an active `vault.db` alongside its journal files can miss committed data or capture different points in time. Use the SQLite backup API and verify a committed WAL row in the copied database.
- Treat a missing provider and a nonzero exit as failures. Fixtures that intentionally simulate a run declare that in the result.

## Performance
- **Transcript hit-testing scanned every preceding row.** Binary-search cached row bounds and visit only the viewport during painting. Benchmark near the end of a long transcript to exercise the previous worst case.
- **Ctrl+A took about 10 seconds with 20,000 clipboard rows.** WinForms updated selection state for every row, and the
  preview reacted to every change. Use native list-box bulk selection, notify once and bound preview text.
- **Image rows stalled on a cold cache.** Thumbnail paint decoded files synchronously. Load scaled images on a worker
  and discard results superseded by a selection, deletion or cache clear.
- **Unavailable folder paths held the UI and old requests could win.** Validate explorer paths and read/index Markdown
  on workers. Publish only the latest applicable result and update navigation history after success.
- **Broad clipboard searches spent time formatting discarded hits.** Format match previews only after ranking the limited
  results. Scan stored text in short slices so changing the query or closing the dialog stays responsive.
- **The Home dashboard and toolbar grouped the same process list independently.** Compute the app memory totals once
  on the sampler worker and share the published snapshot. Keep sampler reset requests off the UI's lock path.
- **Explorer froze 12.5 s while resizing.** The places bar asked every drive (network S:, card reader E:) for label and
  free space while painting; the bar hides/shows at 900 px, so every crossing re-ran it. -> Never touch drives,
  network paths or disk in OnPaint / layout. Use `DriveCache` (worker threads, per-drive time limit) and
  `DriveCache.IsSlowPath` for "does it exist" on network folders.
- **Terminals took 2.4 s to open.** Folder tiles ran `Directory.Exists` on favorites on S:. -> Check network paths on a
  worker, update the tile later.
- **Global hooks freeze the whole PC.** A system-wide `EVENT_OBJECT_SHOW` WinEvent hook received every menu/tooltip of
  every app; mouse and right-click stalled. -> No global high-volume hooks; poll with FindWindowEx (300 ms) instead.
- `timeBeginPeriod(1)` changes timer resolution for the whole system. -> Do not use it; 16 ms timers + time-based easing.
- Chat view repainted whole chats 2.5x a second while an agent worked. -> Invalidate only the changed region
  (`InvalidateWorking`), split long answers into paragraphs and draw only what intersects the clip rectangle.
- Self-painted AutoScroll panels leave blank/overlapping copies when scrolled (ScrollWindow moves pixels, only a
  strip repaints). -> Override OnScroll and OnMouseWheel and Invalidate().
- Measure, do not guess: `AllInOne.exe --data <copy of data> --perf` writes `perf.txt` (worst / p95 UI stall per page
  and while resizing). Delete the old perf.txt first: a copied data folder may already contain one.
- Resize hot spots were per-step remeasure/repaint: ComboFrame redraw, FluentButton text measuring, TextBox border on
  every WM_PAINT, re-placing unchanged controls. -> Cache by size/text key, paint borders only on NC messages, change
  Location only when it differs, Ui.Debounce heavy layouts (~90 ms). Profile results vary: use the median of 3 runs.

## Agent CLIs (Agents page)
- A provider can appear more than once with different logins. Apply its account home to the child process, and key
  both resume IDs and seen-message offsets by account. Keep the original key for chats using the CLI default.
- The installed Cline npm package exposed 6,772 provider/model pairs through its local registry. Enumerate the
  registry on a worker when the picker opens. Keep manual model entry for other installations and new models.
- **Cline 3.x**: `--id <session>` makes JSON mode reject the prompt ("requires a prompt argument") in every form
  (arg, stdin). -> No resume for Cline; send recent history inside the prompt (`AgentStore.History`).
- Cline flags: `-P <provider> -m <model> --thinking none|low|medium|high|xhigh --json`. Cline Pass model list is
  only on Cline's server; users add theirs with "Your models".
- Retired models return 404 ("failed to invoke model ... testing period"). -> Auto-switch to the replacement named in
  the error (`AgentModels.Retire`), hide the dead name.
- **Antigravity (agy)**: models named `...-high` already carry the effort; adding `--effort` errors. In headless
  accept-edits/plan mode every command is auto-denied and it then answers nothing. -> Full auto adds
  `--dangerously-skip-permissions`; Ask/Edit pass `--settings {"permissions":{"allow":["command(<prefix>)",...]}}`
  with read-only prefixes (rule syntax from agy itself). `agy models` lists models.
- Codex: models and effort levels are in `~/.codex/models_cache.json` (`slug`, `supported_reasoning_levels`);
  effort flag `-c model_reasoning_effort=<x>`; limits in session logs `rate_limits.primary/secondary`
  (business plans may report none).
- Claude Code: `--model`, `--effort low|medium|high|xhigh|max`; headless web search needs
  `--allowedTools WebSearch WebFetch`. 5-hour/weekly limits come from the status-line files in
  `%APPDATA%\herdr\claude-cost\*.json` (`rate_limits.five_hour/seven_day`).
- npm `.cmd` shims go through cmd.exe, which cuts arguments at a newline. -> Resolve to node + script (FixShim) or
  pass the prompt on stdin; long inline prompts (> 6000 chars) go through a temp file.
- Continue/resume: Claude `--continue` / `--resume`; Codex `codex resume --last` / `codex resume` (picker).
- Codex 'routing discovery timed out' / 403 HTML (Skyhigh gateway) are company-network errors, not app bugs. -> Retry
  once, show a short cleaned message (AgentRun.Clean), never raw HTML.
- Steer: Claude Code `-p --input-format stream-json --output-format stream-json` keeps stdin open; each line
  `{"type":"user","message":{"role":"user","content":...}}` reaches it after the current tool calls (official docs; not yet
  tested live). Stdin stays open until closed, so close it at the first `result` to let the CLI exit. Codex `exec` cannot steer (only app-server `turn/steer`): queue instead.
- Codex limits/context per account live in `<CODEX_HOME>\sessions\**\rollout-*<thread>.jsonl` (`token_count`);
  Claude sends `rate_limit_event` (utilization 0..1) and `result.modelUsage[*].contextWindow` in stream-json.
- `agents.json` on disk overrides built-in args: add new CLI flags at Start time, not only in Defaults().
- .NET HttpWebRequest ignores HTTPS_PROXY. -> Set `req.Proxy = new WebProxy(env)` + Tls12 (npm registry check 8 s).

## Windows / UI
- Excel COM registration can be present while `Excel.Application` activation fails with `0x80080005`. Run a standalone activation without a workbook or app assembly to check the host before attributing an Office fixture failure to the app.
- A saved Markdown Split view tried to set its divider before docking and prevented the page from opening. Wait until
  both panels fit before positioning the divider. Exercise saved modes through the main window as well as standalone controls.
- **Explorer could leave blank rows above the first filename.** Shrinking a scrolled virtual ListView from 80
  entries to 10 placed row zero 189 pixels below its normal position. Move to row zero while the old items still
  exist, then publish the new count. Restore a valid page position for background refreshes.
- **Settings combo labels looked overprinted in Nexus with Calibri.** The cached face repainted a native combo's
  selection. Let Windows paint the light face. Paint the dark face directly on its DC and check both modes.
- Icons from `IShellItemImageFactory` often have straight (not premultiplied) alpha; reading them as PArgb gives
  coloured speckles. -> Detect (a colour channel above alpha) and convert; draw icons 1:1 from a per-size cache.
- Layered windows (UpdateLayeredWindow): alpha 0 pixels are click-through; a 1 px alpha-1 line makes an edge
  trigger without polling.
- Hiding the taskbar: ABM_SETSTATE auto-hide frees its space, then ShowWindow(SW_HIDE) twice (Windows re-shows a
  2 px strip a moment later). Save and restore the user's state (also after a crash on next start).
- Security agent hides its process from process lists (only WMI sees it).
- File search like Everything: WM_COPYDATA query (dwData 2, EVERYTHING_IPC_QUERYW) to window
  EVERYTHING_TASKBAR_NOTIFICATION, reply to a message-only window (~90 ms). Fallback: ADODB Search.CollatorDSO.
- Offscreen captures hide stale-paint bugs: PrintWindow / DrawToBitmap repaint everything, and BitBlt from an
  offscreen window DC returns blank. -> Fix stale paint at the source (repaint after the change settles).
- Changing a TextBox frame with WM_NCCALCSIZE (to make it taller) rendered a stray inner border. -> For icons in a
  TextBox use EM_SETMARGINS + paint in the client after WM_PAINT; leave the frame alone.
- Adding a row to a docked area (Agents usage line) squeezes the rest unless the minimum height counts it. -> Keep
  one MinH function that sums every row; use it for default, drag and wrap limits.
- Embedded browser: WebView2 works from csc/.NET 4.8 with Core + WinForms DLLs (lib\net462) and WebView2Loader.dll;
  call CoreWebView2Environment.SetLoaderDllFolderPath and give it its own UserDataFolder. Full CDP via
  CallDevToolsProtocolMethodAsync. Runtime is preinstalled on Windows 11.
- Packing managed DLLs in the exe: hook AppDomain.AssemblyResolve first in Main (Main itself must not use their types),
  unpack to %LocalAppData% and Assembly.LoadFrom. PowerShell 5 has no Invoke-RestMethod -NoProxy: use WebClient with
  Proxy = null for localhost.
- Saved pane sizes outlive the window size they came from (Tasks detail 1,481 px hid the list). -> Clamp every docked
  pane on parent resize to what the page can give; never trust a stored width alone.
- Audit by rendering every page with a copy of real data (MainForm offscreen + PrintWindow): it found a broken page
  that tests with empty fixtures never hit.
- Ui.Menu() disposes the menu 250 ms after it closes. A menu kept in a field and shown again crashes the app
  (ObjectDisposedException in ToolStripManager). -> Fields use Ui.Menu(true); one-shot menus use Ui.Menu().
- Read data\error.log when checking the app: it held a crash and a page error no test had shown.
- Native AutoScroll on self-painted panels keeps leaving stale copies (Terminals shelf), even with Invalidate on scroll.
  -> Scroll in code: an offset, a drawn thumb, full repaint.
- Cline provider ids are case-sensitive catalog ids (nousResearch, openai-compatible, cline-pass/<model>). -> Never guess
  from globalState keys; list configured providers' catalogs through Cline's own Node modules.
- Codex under enterprise requirements prints 'disallowed by requirements' and falls back. -> Ask only for allowed values
  (workspace-write, approval on-request).
- rep.ps1 newline detection must use the majority: one stray CRLF in an LF file broke every multi-line swap.
- Checking a few pages by eye missed dark-mode faults on 3 pages. -> Run tools\test-ui-sweep.ps1 (all pages x looks x
  sizes + automatic checks) before saying a UI change is done.
- Disabled WinForms labels draw etched grey text that is unreadable in dark mode. -> Hide an empty form behind a hint
  instead of disabling it.
- Windows PowerShell 5 reads BOM-less UTF-8 scripts as ANSI: non-ASCII literals break parsing. -> Build them from [char] codes.
- Agent CLI sign-in status without touching tokens: `claude auth status` (JSON with email, plan) and`n  `codex login status`; run them with the account's CLAUDE_CONFIG_DIR / CODEX_HOME for named accounts.
- Background results must come back through the page itself (Control.BeginInvoke), not App.I: offscreen tests have no App
  and the update silently never arrived.
- HISTORY.md was found empty (0 bytes) on 2026-11-19; cause not proven (another agent edits it too). It is untracked, so git
  could not restore it; it was rebuilt from Claude and Codex session logs. -> Commit HISTORY.md / LESSONS.md, and never
  write a doc back after a failed parse (check that the anchor was found first).
- Control.Visible reads false while any parent is hidden. -> When code decides a layout, assign Visible; never skip on
  'already equal' (a hidden page kept three columns and gave the note negative width).
- PowerShell treats curly quotes (typographic apostrophes and double quotes) as quote marks: keep them out of scripts.
- A test that loads the app's code in PowerShell has no App object; an unhandled error there showed the .NET error dialog
  ON THE OWNER'S SCREEN. -> Every harness sets Application.SetUnhandledExceptionMode(ThrowException) right after loading
  WinForms, so errors reach the script, never the desktop.
- Antigravity CLI sign-in: Windows Credential Manager entry "gemini:antigravity" (check existence with CredEnumerate) and
  the email in ~\.gemini\antigravity-cli\cli.log ("authenticated successfully as"). It has no status/login command.
- An agent CLI can work the app itself: give the run this app's MCP server (Claude: --mcp-config <json> --allowedTools
  mcp__all-in-one; Codex: -c mcp_servers.<name>.command/args). Proven with a read-only page_context + app_status call.
- Claude's sign-in prompt ('Paste code here if prompted') ends without a newline: line-based readers never see it. ->
  Show the code box from the start for a Claude sign-in.
- Subscribing to a child control's event in a constructor before that control is created crashes on open (null). ->
  Wire events right after the control is created.
- A headless agent started in one folder cannot read files elsewhere (Claude: 'only allows file access under ...').
  -> Pass --add-dir for every folder the user has open (note, Explorer folder, vaults); do not rely on the chat folder.
- [regex]::Replace(text, pattern, repl, 1) treats 1 as RegexOptions, not a count. -> Use ([regex]'p').Replace(text, repl, 1).
- Office files on this PC are NASCA-wrapped ('<## NASCA DRM' header). Office's own preview handlers (CLSIDs under
  HKLM\...\PreviewHandlers, created with CoCreateInstance LOCAL_SERVER then INPROC) draw them inside our window. The
  HKCR\.ext ShellEx links pointed to unregistered CLSIDs: try the known Office previewer CLSIDs too.
- A NASCA-wrapped text/MIME file can be read by Word COM: open a .txt copy ReadOnly, Format=4, Encoding=28591, then
  Content.Text (CR -> CRLF) gives the original bytes. Never save; quit the Word you started.
- An AutoSize FlowLayoutPanel can grow tall and cover controls below it (Accounts first row lost its buttons). -> Set
  MaximumSize height for one-row button bars.
- PrintWindow on an off-screen form cannot capture GPU-drawn child windows (PowerPoint previewer, Foxit): a blank
  capture is not proof of failure; check the child windows instead.
- A preview handler hosted in our window runs in another process: the host must give a mouse pointer (handle WM_SETCURSOR
  from the child with the arrow) or the pointer vanishes over the document.
- Keep the Office editor as a separate top-level window. Both cross-thread parenting and cross-thread ownership link the
  input queues. This can block the host when Office handles focus or stops responding. Place the editor with asynchronous,
  non-activating window moves and hide it when its tab, page, or window loses visibility. Keep its own dialogs above it.
  The regression check uses a separate UI thread and pauses its message pump. [Microsoft window ownership guidance](https://devblogs.microsoft.com/oldnewthing/20130412-00/?p=4683%2F)
- For Office edit mode, open via COM and find the document window (Excel: Application.Hwnd; Word: set Application.Caption
  to a tag and find OpusApp by title; PowerPoint: PPTFrameClass of POWERPNT with the file name).
  Quit only the app you started; PowerPoint is shared, close only your presentation. EXCEL -Embedding processes
  without a window can be the read-only previewer's server, not a leak.
- **Hidden Word COM hung when opening `.pdf` files even with `DisplayAlerts = 0` (`wdAlertsNone`).** Word 16.0/365 shows a PDF-to-Word conversion modal dialog unless `DisableConvertPdfWarning = 1` is set under `HKCU\Software\Microsoft\Office\16.0\Word\Options`. -> Temporarily set `DisableConvertPdfWarning = 1` (and restore the prior value in `finally`), track the spawned hidden `WINWORD` PID with a hard timeout, and keep a pure-C# fallback for non-DRM PDFs.
- GDI text drawn into a transparent (PArgb) bitmap smears. -> Use an opaque 24bpp bitmap for cached text.
- `XmlConvert.VerifyXmlChars` throws on the first invalid character (documented); pasted text with a vertical tab
  made a whole Office save fail. -> Filter with `XmlConvert.IsXmlChar` (keep surrogate pairs) before writing XML.
- CSV cells starting with `= + - @` run as formulas in Excel (OWASP CSV injection). -> Prefix `'` unless the cell is a number.
- Never measure or paint unbounded user text in a chat view: cap what is shown, keep the full text in the message.
- `Environment.TickCount` turns negative after 24.9 days uptime -> use a Stopwatch for timeouts.

## Build / tests
- Verify the decompressed recorder resource against the intended executable. A successful build packed the package manager's forwarding executable. A successful recorder shutdown test exercises process lifecycle, while a portable acceptance test must produce and reopen a recording on a clean machine.
- **Passing tests describe the artifact that ran.** Record the tested build or source ZIP, verify an extracted source tree, and smoke-test the exact release executable. Keep later workspace changes and provider tests with stand-ins distinct from shipped and live evidence.
- **The capture test passed against an old executable in the workspace.** Use the current test-build filename and
  run release checks from a clean extraction to expose stale build artifacts.
- C# 5 compiler from .NET Framework (no `$""`, `?.`, `=>` members, `out var`, tuples, `nameof`).
- PowerShell unrolls a one-item nested array: @(@('a','b')) becomes @('a','b'), so a loop over pairs walks the
  strings and p[0]/p[1] become characters (a replace of every 'C' with 'h' wrecked a file). -> Write @(,@('a','b')), check
  the count before replacing, and keep a backup: untracked files are not in git.
- PowerShell: never name helpers `R` or `Move` (aliases win: Invoke-History, Move-Item). Variables are
  case-insensitive (`$s` = `$S`). In single-quoted strings a `'` inside must be doubled (`''\n''`).
- Function output inside `$x = Func ...` is captured, not printed: use Write-Host for reports.
- App control blocks running copied exes from Temp / build-test: test with the real exe and `--data <folder>`.
- `tools\test-build.ps1` writes `build-test\StickyNotesPro.test.exe` (not AllInOne.test.exe).
- **A session could not read or write `%USERPROFILE%\.claude\skills`.** File access is limited to the project
  folder, the project's `.claude/` is write-protected, and the shell refused `$()`, .NET calls and registry
  paths. -> Write new skills to the project's `skills/` folder and tell the owner to copy them; say plainly
  which snippets were not run.
- Another agent may edit the same files at the same time: check git status / duplicates before building.
- ffmpeg (GPL, ~212 MB) is packed gzip-compressed into the exe (~83 MB); `build-cache\ffmpeg.exe.gz` is reused.
- **`build.ps1` packed a 0.13 MB Scoop shim instead of `ffmpeg.exe`, and `tools/make-dist.ps1` omitted `ffmpeg.exe.gz` entirely.** `Get-Command ffmpeg` resolves to Scoop's `.exe` stub without reading its sibling `.shim` target, and `make-dist.ps1` did not pass `/resource:...,tools.ffmpeg.exe.gz`. -> Resolve the shim in the shared bundling script. Probe the Windows x64 encoder from an isolated directory and require H.264 and AAC before embedding it.
- **The agent-conversation test hung on exit in Windows PowerShell 5.1.** It assigned a WinForms text box to the automatic variable `$input`. Use a distinct variable name and verify that the process exits after printing the success message.
- **Headless test scripts calling `ApiTools.Dispatch` crashed with a `NullReferenceException` when `App.I` was not created.** `ApiTools.UI(...)` marshaled directly through `App.I.MainView.Invoke`. -> Guard `ApiTools.UI(...)` so it executes the callback directly on the caller thread when `App.I == null` or `App.I.MainView == null`, allowing all MCP tools to run both inside the live app and inside standalone PowerShell harnesses.
- **Workflow recording and external app inspection do not need `UIAutomationClient.dll` `/r:` compiler flags.** Win32 `WH_MOUSE_LL`/`WH_KEYBOARD_LL` (active only while `WorkflowRecorder.IsRecording == true`) combined with `WindowFromPoint`, `GetAncestor`, `EnumWindows`/`EnumChildWindows`, `GetClassName`, `GetWindowText`, and `SendMessage(WM_GETTEXT)` inspect and control internal and external windows with zero extra assembly references and zero idle overhead.

- Project-name matching: everyday folder names (docs, agents, test) give false matches, and the same name can exist in two places (repo + shared-drive copy). Keep a stop-word list, drop container folders, and rank duplicates (git > CLAUDE/AGENTS.md > local drive). (2026-11-22)


- Any test that launches a real copy of AllInOne.exe outside the project folder must put portable.txt next to it: otherwise the first-run install prompt appears on the owner's desktop and one click installs the test copy over the user's install. Also assert the app path the copy reports. (2026-11-22)
- A freshly written 89 MB exe is held by the antivirus scan for seconds: renames and moves of release files need retries (TeamClient.Retry); random-byte test files never show this. (2026-11-22)
- Tests that load build-test\StickyNotesPro.test.exe must load a copy when anything rebuilds it during the run. Run reflection tests with Windows PowerShell 5.1, not pwsh 7 (System.Web.Extensions fails on .NET Core). (2026-11-22)


- PowerShell test scripts: variable names are case-insensitive (a helper parameter \ hides the client \), commas bind tighter than + inside @(...), and Assert (x) -eq y 'msg' passes three arguments. Build reflection arguments explicitly for one-element arrays. (2026-11-22)
- Without a server, 'central enforcement' must be cryptographic: signed policy checked by every receiver plus keys only for permitted devices, with key rotation on revoke. A rejected replayed policy also locks out the admin unless there is a repair path from the last verified copy. (2026-11-22)


## 2026-11-22
- Company file servers can block executable extensions (S:\Public_Folder refuses .exe, .ps1, .zip with Access denied even for rename). Store packages under a neutral extension (.bin) on shares and rename locally.
- PowerShell variable names ignore case: a script parameter $Share and a local $share are the same variable. Use distinct names.
- Inline elements that wrap have a bounding box whose centre can be empty page; aim at a client rect that elementFromPoint confirms.
- In mobile emulation a page without a viewport meta lays out at 980 px; check screen.width, not innerWidth.
