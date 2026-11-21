# All in One

A small Windows tray app (no installs) that puts many daily tools in one window: tasks and reminders, sticky notes, prompts and
snippets, clipboard history, a multi-pane file explorer, Chrome bookmarks and extensions, a system monitor with a deep uninstaller,
screen capture and recording, power tools and a shortcut manager. Start it with `All in One.lnk`.
Built with the C# compiler that ships with Windows.

Open the [interactive customer map](docs/customer-map.html) in a browser for the Arabic walkthrough, original Windows control labels, seven guided workflows and the November 20 audit. Keep its toolbar image beside it when copying the repository edition.

## Agent accounts and leader teams

- In **Agents**, click a Codex or Claude Code chip and open **Account**. Choose an existing HERDR profile or select **Sign in to a named account** and complete the provider's login. The app passes the profile directory to that CLI process. Each profile keeps its own resume IDs. The team setup also lets two members use the same CLI with different accounts.
- Choose **Leader team**, add a leader and one or more workers, select their models, and save. Send your request in the chat. The leader proposes a task for each worker. Review or edit the assignments, then choose **Send plan** to run them and have the leader review their output. **Send assignment**, **Stop worker**, **Leader review** and **Final answer** also work individually.
- The workspace shows output from the selected worker. To include an existing HERDR terminal, select the worker, choose **Attach HERDR screen**, enter its pane ID, and review the captured text. Repeat the capture for a newer snapshot.
- The Cline picker searches the installed npm CLI's model catalogue. You can also enter a provider and model ID directly, such as `provider: model-id`. The selected provider account determines which models can run.

## Shortcuts (work in any app, change them in Settings)
| Keys | Action |
|---|---|
| Ctrl+Alt+V | Clipboard history popup: type to search, Enter pastes into the app you were in |
| Ctrl+Alt+P | Prompts popup |
| Ctrl+Alt+N | New sticky note |
| Ctrl+Alt+A | Save the selected text (in any app) as a new prompt (category "Inbox") |
| Ctrl+Alt+T | Paste the current clipboard as plain text (no Excel/Word/web formatting) |
| Ctrl+Alt+Q | Paste queue: start / stop collecting what you copy |
| Ctrl+Alt+W | Paste queue: paste the next item, in the order copied |
| Ctrl+Alt+H | Show / hide all open notes |
| Ctrl+Alt+S | Open the main window |
| Ctrl+Alt+J | New task / reminder ("call Ali tomorrow 3pm !high #work every week") |
| Ctrl+Alt+G | Quick run box: apps, folders, web, `=` math, `?` web search, `>` command |
| Ctrl+Alt+U | Pin the window you are in "always on top" (press again to unpin) |
| Ctrl+Alt+L | Launch your terminals workspace (for example 4 PWSH / Claude terminals side by side) |
| Ctrl+Alt+E | PWSH in the folder open (or selected) in File Explorer |
| (your keys) | Launch shortcuts: Shortcuts page > My launch shortcuts |

Popup keys: Enter paste (always plain text), Ctrl+Enter or right-click "Paste as" (UPPERCASE, lowercase, Title Case,
Sentence case, trim spaces, remove empty lines, join into one line), Shift+Enter copy only, Ctrl+1..9 quick paste,
Tab switch Clipboard/Prompts, Ctrl+P pin, Del delete, Esc close.

In the main window, Ctrl+K opens **Search this app**. Results are grouped by type and ranked within each group.
Use the arrow keys to select, Enter to open or copy, and Esc to close.

Note keys: Ctrl+B/I/U, Ctrl+T strikethrough, Ctrl+Shift+L bullets, Ctrl+Shift+R right-to-left (Arabic),
Ctrl+Shift+V paste plain, Ctrl+ +/- zoom, Ctrl+N new, Ctrl+W close, Ctrl+D delete. Double-click the top bar to collapse.

## Features
- Documents: preview Office and PDF files in tabs. Edit mode opens Excel, Word or PowerPoint in the document pane. The app toolbar, sidebar and document tabs stay available during editing.
- Notes: 8 colors, rich text, images, links, keep on top, reminders (the note pops up and flashes), tags, archive, Arabic RTL.
- Prompts: categories, tags, favorites, {{blanks}} filled when used, snippet codes (type `;sig` anywhere to insert the text),
  version history with restore, import/export.
- Clipboard: text, images and files; folders and color labels; pinned or foldered items are never auto-deleted;
  "Copy as" text actions; multi-select copy; paste queue; skips copies marked private by password managers.
- Light and dark theme, note font and size, history size, all shortcuts: Settings (main window, bottom left, or tray menu).

## Share it with other people
- Run `powershell -ExecutionPolicy Bypass -File tools\make-dist.ps1`. It makes `dist\All-in-One-portable.zip` (one exe plus
  READ ME FIRST.txt) without touching the running app or your data.
- The desktop shell targets 64-bit Windows with .NET Framework 4.8. Web pages use the WebView2 runtime. Office editing and some protected-file previews need the corresponding installed desktop programs. AI accounts need their provider tools. Verify the recorder on a clean PC before distributing it.
- First start of a copied exe offers to install it for that Windows user (`%LocalAppData%\Programs\All in One`, Start menu,
  Desktop, start with Windows, Settings > Apps entry that uninstalls it). Saying No keeps it portable.
- Data: next to the exe when that folder is writable; otherwise (Program Files, run from inside a zip, installed copy)
  `%LocalAppData%\AllInOne\data`.
- The exe is not code-signed, so Windows SmartScreen may ask once: More info > Run anyway.

## New in this version
- **Desktop overlay** (button at the top of the main window, tray menu, or Settings > Desktop overlay): a dock with
  magnifying icons, running-app dots, pinned apps (drop files on it to pin) and right-click window lists; a menu bar at
  the top with the active app's menu, CPU / RAM / battery and the clock (maximized windows stay below it); a hot corner
  and Ctrl+Alt+Up for a window overview with live thumbnails. It is drawn by the app above Windows; turning it off gives
  everything back.
- **Agents** (first page under Tools): chat with the Claude Code, Codex, Cline, Antigravity and OpenCode CLIs in 1, 2, 3, 4
  or 6 side-by-side panes or a 2 x 2 / 3 x 2 grid. Put several agents in one chat: they answer in turn and see each other's
  replies. Each chat has a folder and a mode (Ask = read only, Edit, Auto). Chats, favorites and history are saved in
  `<data>\agents`; agent commands are in `agents.json` (edit it to add another CLI). "Chat with agents here" on folder menus.
- **AI agents can use the app (MCP)**: `AllInOne.exe --mcp` is an MCP server (stdio). Settings > AI agents has copy buttons, or run
  `claude mcp add --scope user all-in-one -- "C:\path\AllInOne.exe" --mcp`. Tools: app status, search everything, notes
  (search, read, create, update, archive), tasks (search, create, update, finish), prompts (search, read, save, fill
  blanks; also offered as MCP prompts), clipboard history (search, read, pin, folder, label, copy), favorite and recent
  folders (favorite, color, tag, note, rename), coding agents (list, chats, read, ask one or more agents in a folder and
  wait for the answers). The server talks to the running app over a local named pipe and starts the app when needed.
- **Your look**: custom accent color, heading font and size, code font and size, corner roundness, card shadows, your app
  name (title bar and sidebar) and your name.
- **Sidebar you arrange**: right-click a page to rename, move, pin to Favorites or hide it; Settings > Customize sidebar.
  The sidebar folds to icons with the round edge button; the quick launch bar hides the same way.
- **Project folders shelf** (Terminals), **places sidebar** (Explorer: favorites, drives with free space, recent), and
  shared favorites, colors, tags and notes for folders across Terminals, Explorer and Agents.
- **Data folder**: Settings > Your data > Move data folder copies everything to a folder you choose (used after a restart).
  `AllInOne.exe --data <folder>` uses a folder for one run (portable use).
- **Wide-screen layout**: System > Processes has a Task-Manager-style performance strip (CPU and memory charts, disk bars)
  shown only on that tab, heat-map CPU / Memory / Disk cells, and Threads, Process and Location columns. Lists share extra width
  between their text columns (Processes, Startup, Cleanup, Big files, Chrome bookmarks, Uninstall, Activity, Tagged files).
  Drive tiles, Quick actions and home cards stretch to the window width; the home task cards use a masonry layout (4 columns when very wide); the Terminals preview moves to the right at full height; the
  Floating cards page is a grid; detail panes (Clips preview and others) grow to about a third of a wide window.
- **Faster**: saving serializes on a background thread; note typing, window moves, copies and Office events no longer re-save or
  re-scan everything; the top bar and home recompute data once after a burst of changes; search boxes are debounced; Chrome
  bookmark search, Tagged files, Explorer icons and watchers, and the Uninstall list (registry programs first, Store apps after) no
  longer block the window; drive queries run off the UI thread.
- **Fixes**: the task detail no longer overwrites changes made from menus; a bookmark folder cannot be moved into itself; folder
  menus act on the right-clicked folder; Snip delete uses the Recycle Bin for all selected snips; Explorer re-selects the folder you
  came from and keeps cut files when a move is cancelled; Clear temp files skips files in use silently; leftover deletes keep failed
  rows; Store-app leftovers are no longer pre-ticked on name-only matches; safer handling of odd paths and empty file clips.
- **SerQ design** (default): floating navy sidebar grouped into General / Tools / System / Support, blue-grey canvas,
  white rounded cards, one blue accent; red and amber appear only when something needs attention. Home shows only real
  data: the date and a status line (tasks due, overdue, drives over 90 % full); CPU and memory for the last 2 minutes on a
  fixed 0-100 % scale; Up next (your next tasks, click one to open it); today's counts (due, copied, files opened, urgent
  tags); every fixed drive with its free space; memory by app with the app icons and share of RAM.
  Settings > Design also offers Nexus (white sidebar) and Classic (blue sidebar).
- **Less noise**: no emoji icons, gradients, fake drop-downs, decorative charts or made-up "Heavy / Light" labels. The top
  bar keeps CPU, RAM, drives, top app and due tasks; the urgent and recorder chips appear only when they have something to say.
- **Chat files.** Under **Files** next to Send, choose a document format, describe the document, and click the filename in the reply to save it. Right-click any agent reply and choose **Save reply as** to export existing work. **Save code block as file** lets you choose a source filename and extension. Excel and Word support Markdown tables. PowerPoint uses a heading for each slide. PDF, Markdown, CSV and plain text are also available. Use **Files → Insert text file into draft** to include a local text, Markdown, code or CSV file before sending. The writers run locally without Office.
- **Chat library.** Right-click a conversation to move it into a named folder, rename or dissolve the folder, favorite the chat, or archive and restore it. Use the library selector for folders, Favorites, Drafts and Archived. Unsent text is saved separately from the conversation. Sent messages and active replies are checkpointed.
- **Terminal history.** Select **Edit as workspace** to inspect or change a previous launch before opening it. New multi-pane records retain each shell, startup command and folder. Favorited commands appear in the startup-command dropdown. History covers launches made through the app. Interactive shell scrollback stays with the terminal.
- **Terminals** page: open 1, 2, 3, 4, 2 x 2, 5, 6, 3 x 2 or 4 x 2 terminals at once. Each pane has its own folder, shell
  (PWSH 7, Windows PowerShell, cmd) and start command (`claude`, `claude --continue`, `codex`, ...). "Separate windows" tiles
  real Windows Terminal windows side by side on the screen under the mouse; "One window, split panes" uses Windows Terminal
  panes. Save several workspaces (one per project).
- **PWSH here everywhere**: every folder menu in the app (Explorer panes and bar, quick launch bar, Tagged files, File
  activity, Uninstall, Drives & cleanup, Snip & record) has "PWSH here", "Claude here (in PWSH)" and "Add to my terminals
  workspace". Explorer also has "Terminal per pane". The tray menu has a Terminals submenu.
- **Top bar**: chips for CPU, RAM, disks, top app and tasks due (click to open the page); theme, keep-awake and Wi-Fi
  switches; clock (click = date and time menu). Right-click for themes and copy values.
- **Tasks & reminders** (Ctrl+9): smart lists (Today, Upcoming, Overdue, Reminders, Completed), your own lists, priorities,
  due dates, repeats, steps, notes; reminder pop-ups with snooze; typed dates are understood.
- **Explorer**: 1, 2, 3 or 4 panes side by side or 2 x 2. F5 copy / F6 move to the next pane, drag and drop, Ctrl+C/X/V
  (also with Windows Explorer), F2 rename, F7 new folder, Del to the Recycle Bin, right-click menu.
- **Chrome**: all bookmarks of every profile (search, open, add, edit, move, delete, export, find duplicates; saving needs
  Chrome closed and makes a backup first) and an Extensions tab.
- **Uninstall** (System monitor tab): all programs including Store apps; after the uninstaller runs, a leftover scan of
  folders, registry keys, shortcuts and startup entries; registry keys are exported and files go to the Recycle Bin.
- **Quick launch bar** on the right: drop files on it, add apps, folders, web pages, commands; right-click to manage.
- **Power tools**: always on top / see-through for any window, quick actions (awake, Wi-Fi, lock, sleep, flush DNS,
  restart Explorer, clear temp), hosts file editor (backup first, admin to save), "what is using this file".
- **Shortcuts** page: edit every app key, make your own global launch keys, reference lists of in-app and Windows keys.
- **Settings**: 8 color themes or your own accent, light/dark, 3 designs, app / heading / code fonts, corners, shadows, names, quick launch on/off, keep awake.

## System monitor (main window > System monitor, or a floating card)
- Processes: live CPU, memory (private working set, same as Task Manager), disk I/O; group by app; Delete key = End task.
  Sampling runs only while a process view is visible (one NtQuerySystemInformation call every 2 s).
- Startup apps: registry, startup folders and Store apps; enable/disable the same way Task Manager does.
- Drives & cleanup: live C:/D: free space with change since the previous day; cleanup list (temp, browser and app caches,
  crash dumps, Windows Update downloads, Recycle Bin, developer caches) with sizes and change since last check;
  Deep scan lists files of 100 MB+ and the biggest folders with growth since the last deep scan. Tick, then Delete selected
  (big files go to the Recycle Bin). Right-click any list for tick all, copy path, show in folder.
  Windows-owned items need "Run as admin".

## Floating cards (main window > Floating cards, Ctrl+Alt+C)
Any feature (Clipboard, Prompts, Notes, Processes, Drives & cleanup, Startup apps) can live in a floating card;
a card can hold several features as tabs. Cards can stay on top, change opacity, and shrink to a small pin
(double-click the top bar or the - button; click the pin to open again).

## Snip & record (Ctrl+Alt+X snip, Ctrl+Alt+O text from screen, Ctrl+Alt+R record)
- Snips open with the Box tool selected. Copy path (Ctrl+Shift+C) saves an unsaved snip first; recent snips support multi-select Copy as path.
- Settings > After a snip, copy: Image, File path, or Both. File path and Both save the snip even when auto-save is off. Both supplies image, text path, and file formats to receiving apps.
- Modes: rectangle, window, full screen, free-form; delay 3/5/10 s. Snips auto-save to Pictures\Screenshots and auto-copy.
- Editor: pen, highlighter, eraser, box, arrow, text, hide (pixelate), crop, colors, thickness, undo/redo, zoom,
  copy, save / save as (PNG, JPG, BMP, GIF), pin on screen, Text extract (Windows OCR: English, Arabic, Korean...).
- Recording (ffmpeg): area / window / full screen, optional microphone, pause/resume, stop & save as MP4 in
  Videos\Screen Recordings (copied to the clipboard as a file). The control bar and red frame are not recorded.

## File activity
Logs files opened and closed: Excel / Word / PowerPoint through their COM automation, other apps through Windows
Recent items and window titles. Search, type and time filters, favorites, Backup (copies the file to data\backups),
export to CSV, open file / folder, tag file.

## Tagged files (Ctrl+Alt+F tags what is selected in File Explorer)
Urgent / Review / Important (plus your own categories), add files or folders, drag and drop, search, select all,
move selected, notes, remove from list (files are never deleted).

## Data
Saved in the `data` folder next to the exe (JSON + PNG images) with an automatic `.bak` copy of each file, plus:
- `data\vault.db` — native Windows SQLite (`winsqlite3.dll`, WAL mode + FTS5) storing auto-extracted Office/PDF tables (including NASCA DRM via COM), `session_fts`, `step_events`, and `alert_dedup`.
- `data\assistant\` — isolated standalone Assistant Companion storage (`chats\`, `memory\` markdown files & daily logs, `skills\`, `tools\` dynamic MCP registry, `cron\jobs.json`).
- `data\dashboards\` — interactive HTML dashboards and the `Live Canvas` rendered inside the embedded Browser.

## Interface and reliability
- Main-window pages are created on first use and keep their state when you switch away. Unused tools do not allocate controls or start their page services. Closing windows no longer forces full garbage collection or empties the process working set.
- Backups include nested data such as agent chats, layouts, and saved file backups. Restore validates the main JSON files before swapping folders, retains the previous folder beside the data folder, and recovers interrupted folder swaps on startup. Linked folders/files and overlapping destinations are rejected; temporary save files and internal restore staging folders are excluded.
- Moving the data folder requires an empty destination and successful saves first. Restart immediately after the copy. The active session continues to write to the old folder, and the next start reads the new copy. Edits made in between stay in the old folder.
- Agent-chat saves coalesce in order on a background worker, retain a previous-file backup, and finish before normal exit or backup. Deleting a chat cancels its queued saves.
- Recurring tasks jump directly past overdue occurrences, handle weekends and date limits, and keep the original month-day anchor when skipping missed months.
- Rounded buttons and inputs, keyboard focus feedback, and consistent light/dark controls. Windows 11 supplies rounded window corners where supported.
- Toolbars, note formatting tools, and card tabs wrap in narrow windows. Prompts use a category dropdown when the sidebar would crowd the editor; short windows can scroll the main navigation.
- Failed saves are retried. If changes cannot be saved when you choose Exit, the app stays open and explains why. Recovery preserves the last good backup.
- Delayed pastes and snippet expansion check their target window. Restoring clipboard text never replaces a newer copy; failed queue copies stay queued.
- Microphone discovery and recording shutdown run in the background. Recordings finish saving before a normal app exit. If merging fails, the original segments are kept and their locations are logged. Rapid snips and backups receive distinct filenames.

Review checks (leave the running app open): `powershell -NoProfile -ExecutionPolicy Bypass -File tools\test-all.ps1` compiles the test build and runs the isolated suites listed in [tools/test-all.ps1](tools/test-all.ps1). Tests use temporary fixtures under `build-test`, hidden controls, and an owned recorder helper; they do not start the app or load production data. `tools\measure-ui.ps1` measures hidden main-window construction; `tools\measure-process.ps1 -ProcessId <id>` samples a running app without changing it. Benchmark scope and review findings are recorded in [the review](tasks/review-2026-11-15.md).

## Operating environment and open automation
- This app is built for a closed, isolated corporate/factory local intranet behind a strict enterprise firewall.
- Full local automation freedom is intentional: the local MCP Named Pipe, Assistant `Full Auto` mode, and local Markdown/email web views stay open without confirmation gates, ACL lock-downs, or restrictive HTML stripping.

## Maintaining this project

Read the authoritative [implementation plan](IMPLEMENTATION_PLAN.md) (plus visual guides [architecture-visual.html](docs/architecture-visual.html) and [implementation-plan.html](docs/implementation-plan.html)) before continuing architecture or companion work. See the [maintenance guide](docs/maintenance.md) for code and test entry points and release checks. The [project instructions](AGENTS.md) explain how to keep the [history](HISTORY.md), [lessons](LESSONS.md) and [skills](skills/README.md) updated with every task.

## Build
`powershell -ExecutionPolicy Bypass -File build.ps1` (asks the running copy to save and exit, then rebuilds).
