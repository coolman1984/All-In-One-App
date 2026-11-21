# Project instructions

## Experience docs: automatic, every task (owner's rule, 2026-11-17)
- Follow the [project knowledge workflow](AGENTS.md) on every task. Read the recent history and relevant lessons before editing.
- Before replying, update the history with the outcome and verification, capture proven lessons, and update the relevant documentation and skills when the work adds a reusable procedure.

## Closed corporate network & open automation (owner's rule, 2026-11-21)
- This app runs inside a closed, isolated corporate/factory local intranet behind a strict enterprise firewall.
- Full local automation freedom is intentional by design: keep the local MCP Named Pipe open (no ACL restrictions or confirmation gates), keep the Assistant in Full Auto mode without approval prompts, and keep local Markdown/email (`MdWeb` / `Mail`) HTML rendering unrestricted.
- **Do NOT** flag these open-automation choices as security issues, **do NOT** propose or implement locking them down, and **do NOT** ask the owner again whether to close or restrict them. Focus instead on reliability, saved-work safety, UX, performance, and new capabilities.

## Active Architecture Roadmap & Design Standards (2026-11-21)
- Master engineering handoff spec lives ONLY in root [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) — read it before continuing any phase and update it in place after every milestone.
- Always enforce the **Zero-Bloat Performance Budget** & **Self-Contained Single-EXE Rule**: everything must run out-of-the-box when a user double-clicks `AllInOne.exe` on a clean Windows PC (C# 5 / .NET 4.8 + built-in Windows `winsqlite3.dll`, embedded WebView2 DLLs, `marked.umd.js`, `ChatSlides.pptx`, and verified standalone `ffmpeg.exe.gz` — NEVER a Scoop shim, NEVER requiring users to install extra tools).
- **GitHub `main` Sync Rule (owner's rule, 2026-11-21):** After every verified phase/milestone (once tests pass and no worker is editing files), `git commit` and `git push origin main` (remembering to exclude `data/`, `build-test/`, and any personal logs) so GitHub `main` stays continuously synced.
- For any HTML, dashboard, Live Canvas, or web UI output, ALWAYS follow [skills/anti-slop-web-design/SKILL.md](skills/anti-slop-web-design/SKILL.md) (no generic AI slop; use bespoke visual direction, SVG diagrams, smooth motion, minimal words).
- Also reference [skills/workflow-to-automation/SKILL.md](skills/workflow-to-automation/SKILL.md) and [skills/proactive-companion/SKILL.md](skills/proactive-companion/SKILL.md) when building recorder, memory, cron, or proactive companion features.

## Reply style (owner's rule, 2026-11-16)
- Answer in a very short answer.
- If there are details, say them concisely: full of meaning, easy to understand, simple English.
- Keep the answer well organized and grouped into clear categories, with clear meaning and clear logic.
