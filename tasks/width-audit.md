# Responsive audit: target widths 700 / 1000 / 1600 px

Inspect with the rebuilt assembly loaded into Windows PowerShell, never Program.Main or Store.Load. Fixture settings point at build-test only. Hidden forms must never be shown and live views must not start.

| Surface | 700 px | 1000 px | 1600 px |
| --- | --- | --- | --- |
| Main navigation | compact icon rail, labelled tooltips | full sidebar | full sidebar |
| Notes | wrap filters/actions; list fills | wrap as needed | one row when possible |
| Prompts | category picker; list/editor stack only below useful pane width | picker or sidebar according to content width | full category/list/editor arrangement |
| Clipboard | preview below list | split preview | split preview |
| System | wrapping tabs/actions/KPIs; horizontal data scrolling | flowing KPIs | four KPI tiles share available space |
| Cards manager | wrapping actions/help; fill list | same | same |
| Snip page | capture/accessory action groups wrap; fill recent list | same | same |
| Activity/tagged | wrapping filters/actions/category bar; lists scroll | same | same |
| Settings | proportional or stacked field layout, wrapped labels, docked action row | fields grow | fields grow |
| Card editor / reminder / ask / variables | docked fields, wrapping actions, scrolling when height is constrained | same | same |
| Snip editor | wrapping ToolStrip and OCR below canvas | OCR side pane if canvas retains useful width | OCR side pane |
| Floating features | layout responds to actual card content width, including 240 px cards | same feature policies | same feature policies |

No fixed-position action may extend beyond its client area. Long file-data columns retain native horizontal/vertical scrolling. All accessories get fit-to-working-area defaults and disposal tests; live timers stop when hidden or closed.
