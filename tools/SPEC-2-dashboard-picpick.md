# Job 2: dashboard home, responsive layout, PicPick-style tools and design

Same folder, same hard constraints as `tools\REVIEW-SPEC.md` (C# 5, no new DLLs, light on RAM, keep every feature
and the JSON format, never run/kill StickyNotesPro.exe, never run build.ps1, never touch data\, check with
`pwsh -NoProfile -File tools\test-build.ps1` -> BUILD OK, commit in small steps).

## 1. Snip changes (owner request, do first)
- **Copy as path**: copy the saved snip's full file path as text (for pasting into chats and CLIs).
  - Editor toolbar button "Copy path" (saves first if the snip is not saved yet) and Ctrl+Shift+C.
  - Recent snips list (SnipPanel) right-click menu: "Copy as path" (multi-select joins paths, one per line).
  - Settings: "After a snip, copy:" Image / File path / Both (default Image). Auto-save must be on for path; if off,
    save to the snip folder anyway when Path or Both is chosen.
- **Default editor tool = Box** (`rect`), not Pen.

## 2. Responsive layout everywhere (owner request)
The owner must never need to resize a window to see a control or data. Every page, card, dialog and toolbar must
reflow: toolbars wrap to more rows, side panels collapse or stack under the main list when narrow, lists fill the
remaining space, nothing is clipped or hidden off the right edge, minimum sizes are sensible, floating cards reflow
inside small sizes. Test by reasoning about widths 700, 1000 and 1600 px for every page. Remove fixed absolute
positions where they cause clipping (e.g. SystemPage header, Settings dialog, CardsPage edit dialog).

## 3. Dashboard (owner request)
Important live data must be visible on big dashboard cards:
- New first page **Home / Dashboard** in the main window (also available as a floating-card feature "dashboard"),
  a responsive grid of tiles (FlowLayout that reflows 1-4 columns):
  CPU % and RAM % (with a small 60-sample sparkline each), drive C/D free space bars, top 3 memory apps,
  clipboard items today, prompts count + most used, notes with reminders due today, files opened today
  (activity), tagged Urgent count, recording status, and quick action buttons (New note, Snip, Record,
  Text from screen, Cleanup). Clicking a tile opens its page.
- Use the empty top-right space of the System page (owner circled it) for live KPI tiles: CPU, RAM, disk C, disk D.
- Live tiles refresh only while visible (reuse ProcessSampler.Watch and the visible-only pattern of LivePanel).

## 4. PicPick features (add all, in the Snip & record page and the dashboard actions)
Screen capture: Full-screen, Window, Region, **Fixed Region** (ask W x H once, remember it, place the frame with
the mouse), **Window Control** (capture the child control under the cursor, highlight it while hovering),
**Scrolling Window** (capture a scrollable window by scrolling it with mouse-wheel messages and stitching frames
by overlap matching; stop at the end or on Esc; keep it simple and robust), Freehand, **Repeat Last Capture**
(same mode and same rectangle again).
Graphic accessories:
- Screen Recorder (already exists - link it)
- **Color Picker**: pick a pixel anywhere with a live magnified loupe; copy as HEX / RGB / HSL; history of picks
- **Color Palette**: tune a color (H/S/L and R/G/B sliders), saved swatches, copy codes
- **Magnifier**: small always-on-top window that zooms around the cursor (2x-8x), follows the mouse, Esc closes
- **Pixel Ruler**: draggable, resizable on-screen ruler (horizontal/vertical) in px with tick marks
- **CrossHair**: full-screen crosshair showing X,Y and the distance from a reference point you click
- **Protractor**: on-screen protractor to measure an angle between two drags
- **WhiteBoard**: transparent full-screen overlay to draw on the screen (pen, highlighter, colors, clear, Esc)
Editor extras like PicPick: New blank image, Open image file, Print, Share (copy file / open mail with attachment
via mailto is not possible -> just "Copy file" + "Show in folder").
Every accessory is light: created only when opened, disposed on close, no background timers when closed.
Add hotkeys only where useful and unused (check Hotkeys.Actions), settable in Settings.

## 5. Design: take PicPick's look and feel
- Left navigation like PicPick: solid accent-blue sidebar (#1A6EC2-ish; darker variant in dark mode) with white
  text, icon + label, selected item highlighted by a darker band; app logo/back button on top.
- Home page like PicPick "Pick a task...": big light title, two responsive columns of sections with blue section
  headers ("New task", "Screen Capture", "Graphic Accessories", "Notes & prompts", "System") and task rows of
  icon (32 px) + bold title + one grey description line, generous spacing (rows ~56 px, section gap ~24 px),
  thin divider between columns, Segoe UI (Segoe UI Variable Display for titles if installed).
- Same airy spacing, font scale and blue headers used on every page; consistent in light and dark.
- Icons: Segoe Fluent Icons / MDL2 glyphs drawn in the accent color (no image files needed).

## Answer format
Same as before: changes per part with file names, what you could not do and why, last BUILD OK line,
`git log --oneline` of your new commits.
