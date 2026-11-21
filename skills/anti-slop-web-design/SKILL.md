---
name: anti-slop-web-design
description: Studio-grade web, dashboard, and interactive visual design skill that eliminates generic "AI slop". Combines Anthropic frontend-design studio craft with Impeccable OKLCH color precision, intentional typography, spring-physics motion, interactive SVG/Canvas storytelling, and mandatory self-critique before finishing.
---

# Anti-Slop Web & Dashboard Design Craft

Act like a principal design engineer at an independent craft studio — NEVER like a generic template generator.

## 1. Hard Anti-Slop Ban List (NEVER DO THESE)
- **NO generic AI color cliches:** No purple-to-blue neon gradients, no sterile default Bootstrap blue-on-gray cards, no rainbow badge vomit, no random emoji soup in headings.
- **NO cookie-cutter layouts:** No repetitive 3-equal-card rows stacked identically down a page, no giant centered marketing hero on top of a utility dashboard, no walls of paragraphs where a diagram or metric belongs.
- **NO flat dead visuals:** Never ship lifeless static tables when a crisp interactive SVG diagram, sparkline, animated flow, or tactile filterable surface tells the story in 2 seconds.
- **NO default typography laziness:** Always establish clear hierarchy with deliberate scale contrast (display title, compact tabular numerals `font-variant-numeric: tabular-nums`, quiet metadata, generous negative space).

## 2. Studio Design Pillars (ALWAYS APPLY)
1. **Committed Aesthetic Direction:** Pick ONE distinct visual personality grounded in the domain before writing CSS:
   - *Industrial Control Room / Swiss Precision* (high-contrast graphite/porcelain, razor 1px architectural hairlines, crisp signal accent, monospace telemetry, zero fluff)
   - *Editorial Data Journal* (warm paper ground, deep ink typography, asymmetrical bento grid, refined muted earth/cobalt palette)
   - *Tactile Glass & Depth* (subtle layered elevation, OKLCH perceptually balanced surface tones, crisp inner highlights, dark-first or true dual-theme parity)
2. **OKLCH Perceptual Palette:** Define semantic CSS variables using balanced lightness/chroma (`oklch(...)` with hex fallbacks): 1 neutral ground, 2 elevated surfaces, 1 dominant signal accent, and strict semantic status tones (only where status actually exists).
3. **Visual First, Minimal Words:** Communicate architecture, pipelines, and reports with **custom interactive SVG diagrams, animated data pulses, live charts, and short scannable labels (2–6 words)** — reveal deeper logic progressively on hover/click instead of dumping paragraphs on screen.
4. **Purposeful Motion & Physics:**
   - Use physical easing (`cubic-bezier(0.22, 1, 0.36, 1)`) for choreographed staggered entrances, smooth tab/panel morphs, animated SVG data packets traveling along flow paths, and responsive micro-interactions on hover/active states.
   - Respect `prefers-reduced-motion: reduce`.
   - Zero heavy JS frameworks when clean native HTML5 + CSS + inline SVG/Canvas renders in < 30 ms offline inside WebView2.
5. **Dual Arabic RTL / English Precision:** In RTL layouts, align arrows, flow direction, grids, numbers (`tabular-nums`), and technical code badges (`dir="ltr"`) cleanly without broken alignment.

## 3. Mandatory Critique Pass (`/critique` → `/polish`) Before Shipping
Before declaring any HTML page, dashboard, or Live Canvas finished:
1. **Squint Test:** Does the eye land immediately on the primary insight/flow within 1 second?
2. **Word Diet:** Can every caption or card be cut by 40% while keeping 100% of the logic?
3. **Motion Check:** Are transitions smooth, meaningful (showing direction/state), and 60 fps?
4. **Theme & Viewport Check:** Does it look equally crafted in Light and Dark mode, and both wide desktop and side-pane widths?
