# Sticky Notes Pro

A tiny Windows app (about 150 KB, no installs) with three tools in one: sticky notes, a prompt and snippet library, and clipboard history.
Built with the C# compiler that ships with Windows.

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

Popup keys: Enter paste (always plain text), Ctrl+Enter or right-click "Paste as" (UPPERCASE, lowercase, Title Case,
Sentence case, trim spaces, remove empty lines, join into one line), Shift+Enter copy only, Ctrl+1..9 quick paste,
Tab switch Clipboard/Prompts, Ctrl+P pin, Del delete, Esc close.

Note keys: Ctrl+B/I/U, Ctrl+T strikethrough, Ctrl+Shift+L bullets, Ctrl+Shift+R right-to-left (Arabic),
Ctrl+Shift+V paste plain, Ctrl+ +/- zoom, Ctrl+N new, Ctrl+W close, Ctrl+D delete. Double-click the top bar to collapse.

## Features
- Notes: 8 colors, rich text, images, links, keep on top, reminders (the note pops up and flashes), tags, archive, Arabic RTL.
- Prompts: categories, tags, favorites, {{blanks}} filled when used, snippet codes (type `;sig` anywhere to insert the text),
  version history with restore, import/export.
- Clipboard: text, images and files; folders and color labels; pinned or foldered items are never auto-deleted;
  "Copy as" text actions; multi-select copy; paste queue; skips copies marked private by password managers.
- Light and dark theme, note font and size, history size, all shortcuts: Settings (main window, bottom left, or tray menu).

## System monitor (main window > System monitor, or a floating card)
- Processes: live CPU, memory (private working set, same as Task Manager), disk I/O; group by app; Delete key = End task.
  Sampling runs only while a process view is visible (one NtQuerySystemInformation call every 2 s).
- Startup apps: registry, startup folders and Store apps; enable/disable the same way Task Manager does.
- Drives & cleanup: live C:/D: free space with change since the previous day; cleanup list (temp, browser and app caches,
  crash dumps, Windows Update downloads, Recycle Bin, developer caches) with sizes and change since last check;
  Deep scan lists files of 100 MB+ and the biggest folders with growth since the last deep scan. Tick, then Delete selected.
  Windows-owned items need "Run as admin".

## Floating cards (main window > Floating cards, Ctrl+Alt+C)
Any feature (Clipboard, Prompts, Notes, Processes, Drives & cleanup, Startup apps) can live in a floating card;
a card can hold several features as tabs. Cards can stay on top, change opacity, and shrink to a small pin
(double-click the top bar or the - button; click the pin to open again).

## Snip & record (Ctrl+Alt+X snip, Ctrl+Alt+O text from screen, Ctrl+Alt+R record)
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
Saved in the `data` folder next to the exe (JSON + PNG images) with an automatic `.bak` copy of each file.

## Build
`powershell -ExecutionPolicy Bypass -File build.ps1` (asks the running copy to save and exit, then rebuilds).
