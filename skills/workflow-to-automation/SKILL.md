---
name: workflow-to-automation
description: Turn a single human demonstration or multi-step desktop workflow into a verified, reusable, 100% automated SKILL.md and native Dynamic Tool. Combines microsoft/skill-recorder intent generalization with obra/superpowers planning and verification gates.
---

# Workflow-to-Automation (Show Me Once → 100% Automated)

Follow this engineering pipeline whenever converting a recorded user demonstration (`WorkflowRecorder`) or recurring desktop task into permanent automation inside All in One.

## 1. Capture Principle (Lightweight, Semantic, Zero Video Bloat)
- **Never record raw 30fps video** for automation training.
- Record **Semantic Signals** (`events.jsonl`): active process, window title, `Windows UI Automation` (`ControlType`, `AutomationId`, `Name`), Explorer selections, Excel active sheet/cell, Browser URL/DOM selector, clipboard transitions, grouped text input, and hotkeys.
- Capture **Smart Keyframe JPEGs** (~40 KB with click reticle) ONLY on meaningful clicks or window transitions so visual intent is preserved at < 1% of video size.

## 2. Intent Generalization (Upgrade Clicks → Native Code!)
Never generate brittle coordinate macros (`click(412, 380)`). Upgrade every recorded step to the strongest deterministic primitive available in All in One:

| Recorded Human Action | Upgraded Native Automation Primitive |
|---|---|
| Browsing/copying/moving files in Explorer | Direct `.NET` / PowerShell file operations |
| Opening/reading/editing Excel, Word, PowerPoint (incl. NASCA DRM) | `OfficeCom` (`COM Automation` + `UsedRange.Value2`) |
| Cross-referencing or summarizing multiple reports | `SqliteVault` (`data\vault.db` + `SQL JOIN` / `FTS5`) |
| Clicking/filling/scraping inside web portals | `Browser CDP` / `browser_run_js` (CSS/ARIA selectors) |
| Interacting with legacy desktop apps (SAP, ERP, Outlook) | `Windows UI Automation (UIA)` via `AutomationId` + `Name` |
| Building a final report or visual view | `DashboardGen` (`Live Canvas` in Browser) + `anti-slop-web-design` |

## 3. Compile, Plan, Verify (`obra/superpowers` Discipline)
1. **Clean & Parameterize:** Strip accidental clicks/backtracks; extract dynamic parameters (dates, file paths, sheet names, filters).
2. **Present Short Step Plan:** Confirm clear input → steps → output logic.
3. **Generate Dual Artifact:**
   - Human-readable procedure in `data\assistant\skills\<name>\SKILL.md` (with triggers, parameters, failure modes).
   - Executable Dynamic Tool in `data\assistant\tools\<name>.json` (registered immediately in MCP without rebuilding `AllInOne.exe`).
4. **Verify Before Completion:** Dry-run or test on a disposable copy, verify actual output files/SQL rows/COM cleanup (zero stray Office processes), and optionally attach to `CronEngine` or `HEARTBEAT.md`.

Use **Build workflow** after saving a recording to start the build through the configured provider. When a tool is verified on disposable inputs, use its registered name and arguments in **Scheduled jobs**.
