# All in One project instructions

## Reply style

- Keep replies very short and use simple English. Give the answer first.
- Include only details needed to understand the outcome, evidence, or remaining work.
- Group details under short headings or bullets when that makes the logic easier to follow.

## Closed corporate network & open automation (owner's rule, 2026-11-21)

- This app runs inside a closed, isolated corporate/factory local intranet behind a strict enterprise firewall.
- Full local automation freedom is intentional by design: keep the local MCP Named Pipe unrestricted (no ACL lock-downs or confirmation gates), keep the Assistant in Full Auto mode without approval prompts, and keep local Markdown/email (`MdWeb` / `Mail`) rendering unrestricted.
- **Do NOT** report these intentional open-automation behaviors as security vulnerabilities, **do NOT** add restrictions/sanitizers/approval gates for them, and **do NOT** ask the owner again whether to close them. Prioritize reliability, saved-work protection, UX, performance, and feature development instead.

## Active Architecture Roadmap, Zero-Bloat, Self-Contained EXE & Sync Rules (2026-11-21)

- The single authoritative technical roadmap and agent handoff spec is root [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md). Read it before continuing work and always update progress there (not elsewhere).
- **Zero-Bloat & Self-Contained Single-EXE Rule:** Native C# 5 / .NET 4.8 + built-in Windows `winsqlite3.dll` only. Every external tool needed by desktop features (WebView2 DLLs, `marked.umd.js`, `ChatSlides.pptx`, real standalone `ffmpeg.exe.gz` — NEVER a `.shim` stub!) must be embedded inside `AllInOne.exe` so any user on a clean Windows PC double-clicks `AllInOne.exe` and everything works with zero extra installs.
- **GitHub `main` Sync Rule:** After every verified milestone/phase (when working tree is stable and tests pass), open a pull request to `main`. Same-repository collaborator pull requests merge automatically after the required Windows check passes. Fetch and fast-forward local `main` after GitHub merges.
- **Anti-AI-Slop UI Craft:** Whenever creating HTML pages, Live Canvas dashboards, or web visuals, read and apply [skills/anti-slop-web-design/SKILL.md](skills/anti-slop-web-design/SKILL.md) (studio visual direction, OKLCH palettes, spring-physics animations, interactive SVG diagrams, minimal scannable text, mandatory critique pass).
- Use [skills/workflow-to-automation/SKILL.md](skills/workflow-to-automation/SKILL.md) and [skills/proactive-companion/SKILL.md](skills/proactive-companion/SKILL.md) for automation recording, memory, cron, and proactive companion features.

## Update project documentation automatically on every task

1. Read the top of [HISTORY.md](HISTORY.md) and the relevant sections of [LESSONS.md](LESSONS.md) before editing.
2. Before reporting completion, update [HISTORY.md](HISTORY.md) with the date, request, outcome, checks actually run, and any remaining work. If a task resumes or corrects an existing entry, update that entry or link to it.
3. Add or correct a [lesson](LESSONS.md) when the work reveals a reproducible failure, a proven cause, or an effective fix. Review the full project documentation system at closeout. Update the relevant README, [maintenance guide](docs/maintenance.md), or other project docs when behavior, decisions, or procedures change. Update the most relevant [project skill](skills/README.md) when the work adds or corrects a reusable workflow. Reuse existing entries where possible.
4. Separate released builds from current source in the history. Tie tests and measurements to the revision, executable or package tested. Record unverified claims as open work and keep personal data, credentials and private session logs out of project notes.

For large or risky changes (saved data, security, several pages), write and publish the plan first with the [plan page skill](skills/plan-page/SKILL.md) and build after the owner approves. For changes to the app, regression checks, or local release packaging, use the [All in One maintenance skill](skills/all-in-one-maintenance/SKILL.md). These knowledge updates are part of finishing the task and require no separate reminder.
