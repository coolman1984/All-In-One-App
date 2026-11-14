# All in One - master plan (owner request 2026-11-14)

Owner's request: better name (done: "All in One") and better icons everywhere; full optimization, review and
improvement so the app runs smoothly above 60 FPS; a one-click launcher file in the project root (done:
build.ps1 creates "All in One.lnk"); every part flexible: drag dividers to give any section more space, and all
sections reflow to fill the window instead of being crammed.

Work is done in 4 parts by one worker, one part at a time; the lead reviews after every part.

| Part | Theme | Spec |
|---|---|---|
| 1 | Smoothness: nothing heavy on the UI thread, flicker-free paints, a built-in FPS/stall probe | tools/PART-1-performance.md |
| 2 | Flexible layout: fill the space, draggable dividers, stretching list columns, wider tile grids | tools/PART-2-layout.md (written after part 1 review) |
| 3 | Name and icons everywhere, launcher, PicPick consistency | tools/PART-3-identity.md (written after part 2 review) |
| 4 | Full code review and bug sweep, README, final polish | tools/PART-4-review.md (written after part 3 review) |

## Hard rules for every part (read before any change)
1. C# 5 only (.NET Framework 4 csc). NOT allowed: `$"..."` strings, `?.`, `?[`, `nameof`, `=>` on methods or
   properties (lambdas are fine), `out var`, tuples, `is X x` patterns, local functions, `default` literals,
   expression-bodied members, `using static`, string interpolation of any kind. Use `string.Format` or `+`.
2. No new DLLs or NuGet. Only System, System.Core, System.Drawing, System.Windows.Forms, System.Web.Extensions.
3. Never run `AllInOne.exe`, `StickyNotesPro.exe` or `build.ps1`. Never kill any process. Never touch `data\`,
   the registry or user files. The owner's real app is running.
4. The only build check: `pwsh -NoProfile -File tools\test-build.ps1` must print `BUILD OK` with no new warnings.
5. Keep every existing feature and the JSON file format (new fields only, with defaults; null-check new lists in
   `Store.Load`).
6. Keep RAM light: no background timers while a view is hidden; create tools only when opened; dispose GDI objects
   (`Font`, `Brush`, `Pen`, `Bitmap`, `Icon`) you create; reuse cached fonts (`Ui.Base`, `Ui.Bold`, `Ui.Small`,
   `Ui.Big`, `Ui.Icon(size)`).
7. UI controls may only be touched on the UI thread. From a worker thread use
   `App.I.Sync.BeginInvoke(new Action(() => { ... }))` and wrap it in try/catch (the app may be closing).
8. Commit after each numbered task with a clear English message: `git add -A src tools` then `git commit -m "..."`.
   Never commit `data\`, exe files or `build-test\`.
9. If something in a spec is wrong or impossible, do the closest safe thing and write it in your final answer.
   Do not invent extra features.
10. Final answer format: per task: done / changed files / anything not done and why; last `BUILD OK` line;
    `git log --oneline` of your commits.
