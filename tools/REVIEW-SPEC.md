# Job: deep code review, bug fixing and modern redesign of Sticky Notes Pro

You are a senior Windows desktop engineer. Work only inside this folder:
`D:\WORK\Software Development\GitHub\Sticky note` (git repo, baseline commit `bbc43ea`).

## What the app is
A single small WinForms app (`StickyNotesPro.exe`, ~150 KB) that the owner uses all day. Features:
sticky notes (rich text, colors, reminders, tags, archive, Arabic RTL), prompts & snippets (categories,
{{variables}}, snippet codes expanded by a low-level keyboard hook, versions), clipboard history (text/images/files,
folders, color labels, paste queue, quick popup Ctrl+Alt+V/P), system monitor (processes via
NtQuerySystemInformation, startup apps, drives & cleanup + deep scan), floating cards (any feature in an
always-on-top card, collapse to a pin chip), Snip & record (overlay, editor, Windows OCR via a PowerShell
script, ffmpeg recording), file activity logger (Office via COM, others via Recent items), tagged files.
Global hotkeys, tray icon, single instance, settings, light/dark theme.

Source: `src\*.cs` (App, Program, Data, Ui, Native, NoteForm, MainForm, QuickPopup, SettingsForm, Snippets,
SysInfo, SystemPages, Cards, Snip, Activity, Tagged). Data model and JSON storage: `src\Data.cs`.

## Hard constraints (do not break)
- Compiler is the .NET Framework 4 `csc.exe` = **C# 5 only**: no `$""`, no `?.`, no `nameof`, no `=>` members,
  no `out var`, no tuples, no pattern matching, no auto-property initializers.
- References only: System, System.Core, System.Drawing, System.Windows.Forms, System.Web.Extensions. No NuGet, no new DLLs.
- Must stay **light on RAM and CPU** (owner's PC is at 85 %+ memory). No WebView, no WPF, no polling faster than today.
- Keep every feature, hotkey and menu working. Keep the JSON data format **backward compatible**
  (new fields allowed with defaults; never rename/remove fields). The owner has real data in `data\`.
- Office files may be DRM-protected (NASCA): Office access stays through COM automation only.
- **Do NOT** run, kill or restart `StickyNotesPro.exe`, do NOT run `build.ps1` (it closes the owner's running app),
  do NOT open, edit or delete anything in `data\`, do NOT delete user files, do NOT touch the registry.
- Build check (the only build you run): `pwsh -NoProfile -File tools\test-build.ps1` -> must print `BUILD OK`
  with no new warnings. It writes `build-test\StickyNotesPro.test.exe` only. Do not launch that exe.

## Part 1 - Deep review and bug fixes (most important)
Read every file fully. Look for real defects, for example:
- crashes / unhandled exceptions on UI and background threads, cross-thread UI access, races, deadlocks
- GDI/handle/memory leaks (Fonts, Bitmaps, Icons, Brushes, Pens, ContextMenuStrip, Timers, Process objects,
  COM objects not released, event handlers never unsubscribed, images kept in caches forever)
- low-level keyboard hook correctness and timeouts, clipboard races, hotkey re-registration
- wrong P/Invoke signatures / struct layouts on x64 (two were already found and fixed: WIN32_FIND_DATA Pack=4,
  SHQUERYRBINFO natural packing; check all others the same way)
- data loss risks in save/load, file locking, atomic writes, corrupted JSON recovery
- logic bugs: selection after refresh, virtual ListView index errors, sorting, filters, DPI scaling, multi-monitor,
  dark mode leftovers, RTL Arabic, focus/activation problems, windows opening off-screen
- performance: work done on the UI thread that should not be, repeated full refreshes, O(n^2) loops
Fix each real bug in the code. Do not "fix" style-only things in Part 1.

## Part 2 - Modern design and style
Give the whole app a clean, modern Windows 11 (Fluent) look, consistent in light AND dark theme:
- one consistent spacing scale, corner radius and font scale (Segoe UI / Segoe UI Variable if present)
- modern buttons (rounded, subtle hover/pressed states, accent primary button), modern text boxes (flat with
  rounded border and focus accent), consistent toolbars that wrap on narrow windows instead of being cut off
- list views / cards / headers styled consistently; dark mode must not leave white headers, borders or controls
  where it can be avoided (ListView column headers, ComboBox, CheckBox, scrollbars, menus)
- floating cards and note windows: polished header, shadows, hover feedback
- keep it fast: owner-drawn controls are fine, no heavy effects, no animations that cost CPU
Centralize design tokens in `Ui.cs` instead of hard-coded colors spread around.

## How to work
- Commit in small logical steps with clear messages (`git add -A; git commit -m "..."`), Part 1 commits first.
- Run `pwsh -NoProfile -File tools\test-build.ps1` after each step; never leave the tree failing to build.
- Update `README.md` only if behaviour visible to the owner changes.

## Answer format (final message)
1. Bugs fixed: a list, each with `file:line`, what was wrong, the effect for the user, the fix.
2. Design changes: a short list of what looks different.
3. Things you found but did not change, and why.
4. Last build result (paste the `BUILD OK` line) and `git log --oneline bbc43ea..HEAD`.
