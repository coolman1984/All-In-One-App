# Job 3: finish everything that is not complete, choose the best for the app

Owner's words: "make all the not complete and choose for me the best for the app".
Same hard constraints as `tools\REVIEW-SPEC.md` (C# 5, no new DLLs, light on RAM, keep every feature and the JSON
format, never run/kill StickyNotesPro.exe, never run build.ps1, never touch data\, check with
`pwsh -NoProfile -File tools\test-build.ps1` -> BUILD OK, commit in small steps). Do not ask questions: when a
choice is needed, pick what is best for a fast, light, reliable daily tool and write the choice in your answer.

## 1. Close every open item
- Anything from `tools\SPEC-2-dashboard-picpick.md` that is missing, partial, stubbed or simplified: finish it.
- Search the code for TODO, "not implemented", empty handlers, disabled menu items, dead buttons, settings that are
  saved but never used, features in README.md that the code does not do. Finish or remove each one (prefer finish).
- Every hotkey in Hotkeys.Actions works and is settable; every accessory and capture mode is reachable from the
  Home page, the Snip page, the tray menu and (where useful) a floating card.

## 2. Paths not yet exercised - review the code paths carefully and fix
Snippet expansion while typing, paste queue and plain-text paste, deep scan + delete, end task, startup toggle,
card collapse to pin chip and restore, snip overlay + editor drawing/undo, scrolling capture, Explorer tagging
(Ctrl+Alt+F), file-activity Closed events and SAVE! backups, reminders after sleep/resume, multi-monitor and
mixed DPI (negative coordinates, per-monitor bounds), Arabic RTL in notes/prompts/cards.

## 3. Your best choices (do them)
- First-run experience: short welcome on the Home page with the 5 main shortcuts; sensible defaults.
- Global search (Ctrl+K in the main window): one box that searches notes, prompts, clips and tagged files.
- Settings: export/import all data to one .zip-free folder backup (JSON copy) and restore.
- Tray menu tidy: grouped, with accessories submenu.
- Anything else you judge clearly valuable and cheap in RAM; list it in the answer.

## Answer format
What you finished, what you chose and why (one line each), what still cannot work and why, last BUILD OK line,
`git log --oneline` of new commits.
