# Part 2 - Flexible layout: fill the space, draggable dividers, nothing crammed

Read `tools/PLAN-allinone.md` first (hard rules). Folder: `D:\WORK\Software Development\GitHub\Sticky note`.
Do the tasks in order. After each task: `pwsh -NoProfile -File tools\test-build.ps1` -> `BUILD OK`, then commit.

Already in place (do not redo): `PaneGrip` (src/Layout.cs) draggable dividers on the main sidebar, the Prompts
categories and list, the Clipboard preview (`ResponsivePane` with key "clips.preview") and the snip editor OCR
panel; sizes persist in `Store.Settings.PaneSizes`.

Part 1 is done (worker-thread sampling, background image clips, double-buffered containers, `Ui.Batch` page
switch, `--perf` probe in src/Perf.cs). The lead ran the probe: normal use p95 < 1 ms, BUT
- first visit of a page stalls 40-255 ms (prompts 255, snip 242, startup 125, drives 119, activity 113, tagged 117);
- live window resize on the Home page: p95 32 ms, worst 57 ms (target < 16 ms).
Tasks 2.0a-2.0d fix that first.

---------------------------------------------------------------------------------------------------------------
## Task 2.0a - Home page resize cost (src/Dashboard.cs)
1. `TaskHome.OnPaint`: skip any section header and any task row whose rectangle does not intersect
   `e.ClipRectangle` (check with `r.IntersectsWith(e.ClipRectangle)` before any TextRenderer call). Same for the
   title and the welcome box.
2. `DashboardPanel.OnLayout`: remember the last width given to `tasks.Measure`; only call `Measure`,
   `tasks.SetBounds` and `tasks.Invalidate()` when the width changed OR the welcome box visibility changed
   (`Store.Settings.WelcomeHidden`); otherwise only move it (`tasks.Location = ...`) if the y position changed.
   `liveHead` and `grid` likewise: set bounds only when they differ (`if (grid.Bounds != newBounds) grid.Bounds = newBounds;`).
3. `TileGrid.OnLayout`: only resize children when the computed tile width changed (keep a `lastTileWidth`).

## Task 2.0b - Pre-warm hidden pages at idle (src/MainForm.cs)
The first visit of a page creates hundreds of native handles. Do it ahead of time, in small steps, when the app
is idle:
```csharp
// Creates the native handles of hidden pages one at a time after the window is shown, so the first visit of a
// page only has to paint. Each step is small; stops as soon as the window hides.
void PrewarmPages()
{
    var pages = new List<Control> { notes, prompts, clips, system, cards, snipPage, activityPage, taggedPage };
    int i = 0;
    var t = new Timer { Interval = 250 };
    t.Tick += delegate
    {
        if (IsDisposed || !Visible || i >= pages.Count) { t.Stop(); t.Dispose(); return; }
        try { CreateHandles(pages[i++]); } catch (Exception ex) { App.Log("Prewarm: " + ex.Message); }
    };
    t.Start();
}
static void CreateHandles(Control c)
{
    var unused = c.Handle;            // forces the handle even while hidden
    foreach (Control child in c.Controls) CreateHandles(child);
}
```
Call `PrewarmPages()` once, the first time the form is shown (`OnShown` override, after `base.OnShown(e)`, via
`Ui.Later(1500, PrewarmPages)`). Do not pre-load data (no RefreshList calls) - only handles.

## Task 2.0c - Startup apps list off the UI thread (src/SystemPages.cs class StartupPanel, src/SysInfo.cs StartupManager)
Find where the Startup tab loads its items (registry + StartupApproved + packaged apps). Run the collection
(`StartupManager` list building, icon paths, publisher lookups) on a worker thread and fill the FastList on the UI
thread through `BeginInvoke` (guard `IsHandleCreated`/`IsDisposed`). While loading show "Loading..." in the
panel's status/info label. Keep the toggle/enable/disable actions working exactly as before (they may stay
synchronous; after a toggle reload asynchronously). Icons from `ProcMeta.IconFor` must be requested on the UI
thread (ImageList) - call it while filling the list, not in the worker.

## Task 2.0d - Probe resizes three pages (src/Perf.cs)
Change the end of `Run()` so the resize test runs on three pages one after another: "dashboard", "prompts",
"system:procs" (show the page, wait 800 ms, run `Resize(...)`, label the phase "resize <page>"), then write the
report. Keep everything else.

---------------------------------------------------------------------------------------------------------------
## Task 2.1 - Shared card painter (src/Ui.cs)

Move the body of `CardList.OnDrawItem` (everything after it gets `ci` and `sel`) into a public static method so a
second control can paint identical cards:
```csharp
// Paints one card (background, colour strip, border, thumbnail, title, meta, sub text) inside 'bounds'.
public static void DrawCard(Graphics g, Rectangle bounds, CardInfo ci, bool sel, Color back)
```
- `bounds` = the item cell. Fill it with `back` first (today: `BackColor`), then the exact same drawing code as
  today (rounded rect `r` inset by S(6)/S(3)/S(13)/S(6), strip, border, thumbnail, meta, title, sub).
- `CardList.OnDrawItem` becomes: guard checks, get `ci` (try/catch as today), `sel`, then
  `DrawCard(e.Graphics, e.Bounds, ci, sel, BackColor);`. Visual result must be identical.

## Task 2.2 - CardGrid: cards that flow into as many columns as fit (new file src/CardGrid.cs)

Notes and the recent snips gallery are single long columns today: on a wide window most of the space is empty.
Create `public class CardGrid : Control` in namespace `StickyPro` - a light, owner-drawn, virtual grid.

Public API (same names as CardList/ListBox so pages switch with minimal edits):
```csharp
public Func<object, CardInfo> Info;
public int ItemHeight { get; set; }          // row height in px, default Ui.S(78); setting it re-lays out
public int MinColumnWidth { get; set; }      // default Ui.S(300); columns = clamp(width / MinColumnWidth, 1, MaxColumns)
public int MaxColumns { get; set; }          // default 6
public SelectionMode SelectionMode { get; set; }   // One (default) or MultiExtended
public List<object> Items { get { return items; } }   // read-only use by callers; change content with SetItems
public void SetItems(IEnumerable<object> list)      // keeps selected objects that are still present and the scroll position
public int SelectedIndex { get; set; }       // -1 = none; setting selects only that one, scrolls it into view, raises SelectedIndexChanged
public object SelectedItem { get; set; }     // get: item at SelectedIndex (focus item) or null; set: selects that object if present
public List<object> SelectedItems { get; }   // in item order
public List<T> Selected<T>()                 // SelectedItems.OfType<T>().ToList()
public bool GetSelected(int index)
public void SetSelected(int index, bool value)
public void ClearSelected()
public int IndexFromPoint(Point p)           // client point -> index or -1
public event EventHandler SelectedIndexChanged;
```
Behaviour:
- Constructor: `SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true); TabStop = true; BackColor = Ui.Bg; Font = Ui.Base;`
  Keep a `VScrollBar scroll` child docked right (Width = SystemInformation.VerticalScrollBarWidth), visible only
  when content is taller than the client; `Ui.DarkScroll(scroll)` when `Ui.Dark`. Scroll offset = `scroll.Value`.
  (A child scrollbar is simpler and smoother than AutoScroll for a virtual control.)
- Layout: `cols = Math.Max(1, Math.Min(MaxColumns, (ClientSize.Width - scrollWidthIfVisible) / MinColumnWidth))`,
  `cellW = usableWidth / cols`, `rows = ceil(count / cols)`, content height = rows * ItemHeight.
  Item i cell: `new Rectangle((i % cols) * cellW, (i / cols) * ItemHeight - offset, cellW, ItemHeight)`.
  Recompute on resize, SetItems, ItemHeight/MinColumnWidth change; set `scroll.Maximum = contentHeight`,
  `scroll.LargeChange = Math.Max(1, ClientSize.Height)`, `scroll.SmallChange = ItemHeight`, clamp Value.
- Paint: clear with BackColor, then ONLY the visible rows: first row = offset / ItemHeight, last = (offset +
  height) / ItemHeight. For each item call `Info(item)` inside try/catch (skip on error) and
  `Ui.DrawCard(g, cell, ci, GetSelected(i), BackColor)`. If the control has focus, draw a 1 px dotted
  `Ui.Accent` rectangle inside the focus item's cell (inset S(3)).
- Mouse: wheel scrolls 3 rows per notch (`e.Delta / 120 * 3 * ItemHeight`). Left click: no modifier = select only
  that item; Ctrl = toggle (multi mode); Shift = range from anchor (multi mode). Right click on an unselected item
  selects only it (context menus then act on it). Click on empty space clears selection (only in One mode leave
  as is). Double click on an item raises the normal `DoubleClick` event (base behaviour) - make sure a double
  click on empty space does NOT raise it (override `OnDoubleClick`/`OnMouseDoubleClick` and check
  `IndexFromPoint`). Focus the control on mouse down.
- Keyboard (override `IsInputKey` to accept arrows, PageUp/Down, Home, End): Left/Right = -1/+1, Up/Down = -cols/
  +cols, PageUp/PageDown = -/+ (visible rows * cols), Home/End; with Shift in multi mode extend from anchor;
  Ctrl+A selects all in multi mode. Always scroll the focus item into view. Let other keys (Enter, Delete, Ctrl+C,
  ...) go to the normal `KeyDown` event so pages keep their handlers.
- Selection storage: `HashSet<object>` of selected items + `int focus` index + `int anchor` index. Raise
  `SelectedIndexChanged` once per user action that changed the selection.
- RTL text and dark mode come from `DrawCard`/`Ui` colours; nothing extra.
- Dispose the scrollbar with the control (it is a child; default disposal is fine).
- No timers, no per-item controls, no allocations in paint beyond what DrawCard does.

## Task 2.3 - Notes page as a card grid (src/MainForm.cs class NotesPage)

Replace `CardList list` with `CardGrid list` (ItemHeight stays `Ui.S(78)`, `MinColumnWidth = Ui.S(320)`).
Find every use of `list.` in NotesPage (grep) and adapt: `Items`, `SelectedItem(s)`, `SelectedIndex`,
`SetItems`, `Selected<T>()`, `SelectionMode`, `ContextMenuStrip`, `DoubleClick`, `KeyDown`, `Focus()`.
If NotesPage uses something CardGrid lacks (for example `TopIndex`, `BeginUpdate`), add the smallest equivalent to
CardGrid (BeginUpdate/EndUpdate can be no-ops that suspend Invalidate) rather than changing behaviour.
Notes keep their colours (DrawCard uses `ci.Back` / `ci.Strip`).

## Task 2.4 - Recent snips as a thumbnail gallery with background thumbnails (src/Snip.cs class SnipPanel)

1. Replace `CardList list` with `CardGrid list`: `SelectionMode = MultiExtended`, `ItemHeight = Ui.S(96)`,
   `MinColumnWidth = Ui.S(300)`. Adapt every `list.` use as in 2.3.
2. Thumbnails must not be loaded during paint on the UI thread (today `Thumb(path)` reads the full image file
   inside `Info`, which stutters while scrolling). New logic:
   - `Thumb(path)` returns the cached image if present; otherwise returns null immediately and, if the path is
     not already queued, adds it to a `HashSet<string> loading` and queues a worker:
     `ThreadPool.QueueUserWorkItem(delegate { Image t = MakeThumb(path, h); try { BeginInvoke(new Action(() => ThumbReady(path, t))); } catch { if (t != null) t.Dispose(); } });`
     where `h = list.ItemHeight - Ui.S(18)` captured on the UI thread before queueing, and `MakeThumb` is the
     existing load + scale code moved into a static method (it only uses files and GDI+ on its own bitmaps - safe
     on a worker thread).
   - `ThumbReady(path, t)`: if the panel is disposed, dispose `t` and return; remove from `loading`; store in
     `thumbs` (dispose any old value) and `list.Invalidate()`.
   - `Fill()` clears `thumbs` as today (dispose them) - also clear `loading` (late results for paths no longer
     in the list are simply stored; they are cleared on the next Fill).
   - While a thumbnail is loading, `Info` shows the card without a thumbnail (no placeholder needed).
   - Guard `BeginInvoke` when the handle is not created (`if (!IsHandleCreated) { dispose; return; }` inside the
     worker try/catch).

## Task 2.5 - List columns stretch to fill (src/SystemPages.cs class FastList)

Every FastList (processes, startup apps, cleanup, big files, folders, file activity, tagged files) leaves a grey
gap on the right on wide windows. Make the LAST column stretch:
```csharp
bool filling; ColumnHeader minFor; int minWidth;
void FillLastColumn()
{
    if (filling || Columns.Count == 0 || !IsHandleCreated || View != View.Details) return;
    filling = true;
    try
    {
        var last = Columns[Columns.Count - 1];
        if (!ReferenceEquals(last, minFor)) { minFor = last; minWidth = last.Width; }
        int used = 0;
        for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
        int w = Math.Max(minWidth, ClientSize.Width - used - 1);
        if (last.Width != w) last.Width = w;
    }
    finally { filling = false; }
}
protected override void OnResize(EventArgs e) { base.OnResize(e); FillLastColumn(); }
protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e) { base.OnColumnWidthChanged(e); if (e.ColumnIndex < Columns.Count - 1) FillLastColumn(); }
```
and call `BeginInvoke(new Action(FillLastColumn))` at the end of the existing `OnHandleCreated` (guarded by
try/catch). If a FastList subclass or user already overrides these methods, merge, do not duplicate.

## Task 2.6 - Home page fills wide screens (src/Dashboard.cs)

1. `TaskHome.Measure(width)`: three columns when `width >= Ui.S(1380)`, two when `>= Ui.S(720)`, else one.
   Assign sections to columns for 3-column mode: "New task" and "Screen Capture" -> 0, "Graphic Accessories" -> 1,
   "Notes & prompts" and "System" -> 2. In 2-column mode keep today's mapping (0,0,1,1,1). The `Section.Column`
   field can hold the 3-column index; map with `c = columns == 3 ? s.Column : columns == 2 ? (s.Column == 0 ? 0 : 1) : 0`
   - change the section creation calls so the five sections have Column values 0,0,1,2,2.
   Dividers: draw a thin `Ui.Line` divider between every pair of columns (store a `List<Rectangle> dividers`
   instead of one rectangle).
2. `DashboardPanel`: the live tile grid `new TileGrid(Ui.S(220), Ui.S(136), 4)` -> max 6 columns.

## Task 2.7 - Floating cards and dialogs reflow (src/Cards.cs, src/SettingsForm.cs, src/Ui.cs Dialog)

1. Read src/Cards.cs `CardForm` and `QuickListPanel`. Make sure every inner toolbar is a wrapping
   `FlowLayoutPanel` (`WrapContents = true`, `AutoSize = true`, docked Top) and lists are `Dock = Fill`, so a
   floating card can be made small or large and its content follows. Set a sensible `MinimumSize` on CardForm
   (`Ui.S(220, 160)`) if none.
2. `Ui.Dialog(...)`: dialogs are `FixedDialog`; change to `FormBorderStyle.Sizable` with
   `MinimumSize = f.Size` set AFTER the caller sets ClientSize is not possible in Dialog itself - so: set
   `f.FormBorderStyle = FormBorderStyle.Sizable; f.SizeGripStyle = SizeGripStyle.Show;` and add
   `f.Shown += delegate { f.MinimumSize = f.Size; };` so a dialog can grow (long prompts, many variables) but not
   shrink below its designed size. Check every caller still looks right (fields docked Fill/Top).
3. SettingsForm: give it `MinimumSize = Ui.S(420, 360)`; its table already uses percent columns - verify the hotkey
   boxes and checkboxes stretch (Dock Fill) and nothing is cut at 420 px wide.

## Task 2.8 - Self-review of part 2
Re-read all part-2 diffs. Check C# 5, disposal, no per-paint file I/O, keyboard and mouse selection in CardGrid
(single and multi), context menus on Notes and Snips still act on the right items, `BUILD OK`. Final answer in the
PLAN rule 10 format.
