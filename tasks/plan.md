# Job 2 implementation plan

Baseline: 78c01ff. Follow tools/SPEC-2-dashboard-picpick.md in order. All builds use tools/test-build.ps1; do not launch either application executable, inspect data/, change the registry, or run build.ps1.

1. Snip behavior: copy path in editor/recent list, Image/Path/Both preference with forced save for paths, Box default. Verify settings compatibility and output policy with isolated fixtures; compile and commit.
2. Responsive layout: inventory every page/card/dialog at 700/1000/1600 px, replace clipping absolute/fixed layouts, stack detail panes and wrap actions. Verify offscreen component geometry; compile and commit.
3. Dashboard: visible-only LivePanel with shared ProcessSampler, 60-point CPU/RAM histories, bounded owner-drawn KPI tiles, all requested counters/navigation/actions, System KPI strip, floating dashboard. Verify model calculations and visibility lifecycle; compile and commit.
4. PicPick tools, in bounded slices: fixed/control/repeat capture; scrolling capture with overlap-matching stitch and cancellation; color picker/palette/magnifier; ruler/crosshair/protractor/whiteboard; editor blank/open/print/copy-file/reveal. Use on-demand disposable windows, no persistent accessory polling. Verify pure geometry/color/stitching with generated fixtures and offscreen layout; compile/commit each slice.
5. PicPick design: accent sidebar, responsive task sections and dashboard cards, shared fonts/colors/spacing, both themes. Render offscreen and inspect, run regressions, update README, final build and commit.

Architecture: small source modules for dashboard, capture coordination/stitching, and accessories; existing native WinForms and storage remain. New settings fields only, with defaults. Scrolling has frame/height/memory caps and preserves scroll position where possible. Timers run only while their view/accessory is visible; process sampling remains 2 s.

Acceptance: every named action is reachable from Snip page and dashboard; existing features/shortcuts remain; JSON fields are preserved; controls remain reachable at the three requested widths; final BUILD OK without warnings. Live desktop/corporate interactions are excluded by the owner's hard constraints; test through isolated models, hidden controls, and synthetic images.
