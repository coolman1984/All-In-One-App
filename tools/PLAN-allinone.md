# All in One - master plan (owner requests 2026-11-14)

Owner's requests:
- Better name (done: "All in One") and better icons everywhere; one-click launcher in the project root (done:
  build.ps1 creates "All in One.lnk").
- Full optimization, review and improvement so the app runs smoothly above 60 FPS.
- Every part flexible: drag dividers to give any section more space; all sections reflow and fill the window.
- A compact, colourful top bar across the main window that shows ALL important dashboard values (CPU, RAM,
  disk C, disk D, top apps, clips today, prompts, reminders due, files opened, tagged urgent, recorder) as small
  modern 2026-style chips - always visible, click to navigate.
- New apps inside the app: top-level Reminders + Tasks manager; a very deep uninstaller; Chrome bookmarks manager
  (read all, manage, click to open in Chrome); Chrome extensions manager (own tab); a full Explorer with up to 4
  panes (4 vertical columns or 2x2 quarters) and fast copy/paste between panes; a narrow, customisable quick
  launch dock on the right side (apps, folders, files, URLs, commands).
- Quick toggles: change date/time, Wi-Fi on/off, keep awake (no screensaver / no sleep).
- Lots of right-click functions everywhere; a full shortcut maker/manager for everything in the app and in
  Windows, in one tab, in full detail.
- Full settings: 6 themes, font family, font size, dark/light switch icon.
- Research-based extras (PowerToys-style, see part 8): Always on Top for any window, quick Run launcher,
  "What is using this file?", hosts file editor, Awake.

Work is done by ONE worker (Sonnet 5.5, medium effort), one part at a time; the lead (Opus 5.5) writes each
part's detailed spec, reviews and fixes after every part, then sends the next part.

| Part | Theme | Spec |
|---|---|---|
| 1 | Smoothness: nothing heavy on the UI thread, flicker-free paints, built-in stall probe | tools/PART-1-performance.md |
| 2 | Flexible layout: card grids, background thumbnails, stretching columns, 3-column home, sizable dialogs | tools/PART-2-layout.md |
| 3 | Top status bar (all KPIs as chips) + quick toggles: theme, keep awake, Wi-Fi, date/time | tools/PART-3-topbar.md |
| 4 | Reminders & Tasks app (top-level page, due dates, repeat, snooze, popups) | tools/PART-4-tasks.md |
| 5 | Deep uninstaller (programs + Store apps, leftovers scan, safe removal with backups) | tools/PART-5-uninstaller.md |
| 6 | Chrome page: bookmarks manager + extensions manager | tools/PART-6-chrome.md |
| 7 | Explorer page (1-4 panes, 2x2), right-side quick launch dock | tools/PART-7-explorer.md |
| 8 | Shortcut manager (app + Windows actions), Settings overhaul (6 themes, fonts, sizes), PowerToys-style extras | tools/PART-8-shortcuts-settings.md |
| 9 | Icons and right-click menus everywhere, full code review and bug sweep, README | tools/PART-9-review.md |

Each part's spec is written after the previous part is reviewed, so it matches the real code.

## Hard rules for every part (read before any change)
1. C# 5 only (.NET Framework 4 csc). NOT allowed: `$"..."` strings, `?.`, `?[`, `nameof`, `=>` on methods or
   properties (lambdas are fine), `out var`, tuples, `is X x` patterns, local functions, `default` literals,
   expression-bodied members, `using static`, string interpolation of any kind. Use `string.Format` or `+`.
2. No new DLLs or NuGet. Only System, System.Core, System.Drawing, System.Windows.Forms, System.Web.Extensions.
   Windows features that need WinRT are reached through a small PowerShell 5.1 script written to `data\tools\`
   (same pattern as the OCR feature in src/Snip.cs `Ocr`), run hidden.
3. Never run `AllInOne.exe`, `StickyNotesPro.exe` or `build.ps1`. Never kill any process. Never touch `data\`,
   the registry or user files while working. The owner's real app is running.
   (The FEATURES may read/change the registry or files at run time when the user clicks - that is fine; you
   just must not do it yourself during development.)
4. The only build check: `pwsh -NoProfile -File tools\test-build.ps1` must print `BUILD OK` with no new warnings.
5. Keep every existing feature and the JSON file format (new fields only, with defaults; null-check new lists in
   `Store.Load`). New data goes in new JSON files in `data\` saved through the `Store` pattern (dirty flag +
   `Kick()` + `Write`).
6. Keep RAM light: no background timers while a view is hidden (use `LivePanel` and its `OnLive`); create tools
   only when opened; dispose GDI objects (`Font`, `Brush`, `Pen`, `Bitmap`, `Icon`) you create; reuse cached fonts
   (`Ui.Base`, `Ui.Bold`, `Ui.Small`, `Ui.Big`, `Ui.Icon(size)`).
7. UI controls may only be touched on the UI thread. From a worker thread use
   `App.I.Sync.BeginInvoke(new Action(() => { ... }))` and wrap it in try/catch (the app may be closing).
   Slow work (disk scans, registry walks, process starts that wait, file copies) runs on a worker thread.
8. Destructive actions (delete files, uninstall, registry delete, disable network) always ask for confirmation,
   prefer the Recycle Bin (`Microsoft.VisualBasic` is NOT referenced - use SHFileOperation with FOF_ALLOWUNDO via
   P/Invoke) and back up registry keys (`reg.exe export`) before deleting them.
9. Commit after each numbered task with a clear English message: `git add -A src tools` then `git commit -m "..."`.
   Never commit `data\`, exe files or `build-test\`.
10. If something in a spec is wrong or impossible, do the closest safe thing and write it in your final answer.
    Do not invent extra features.
11. Final answer format: per task: done / changed files / anything not done and why; last `BUILD OK` line;
    `git log --oneline` of your commits.
