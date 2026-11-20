# All in One maintenance

## Where to start

| Work area | Implementation | Isolated checks |
|---|---|---|
| Agent chat, drafts and transcript rendering | [Agents.cs](../src/Agents.cs), [ChatMarkdown.cs](../src/ChatMarkdown.cs) | [Agent conversations](../tools/test-agent-conversations.ps1), [saved work](../tools/test-saved-work.ps1) |
| Generated chat files | [ChatFiles.cs](../src/ChatFiles.cs) and the bundled PowerPoint template | [Saved work](../tools/test-saved-work.ps1) |
| Account selection and leader teams | [AgentAccounts.cs](../src/AgentAccounts.cs), [LeaderTeam.cs](../src/LeaderTeam.cs) | [Leader teams](../tools/test-leader-team.ps1) |
| Terminal launches and history | [Terminal.cs](../src/Terminal.cs), [History.cs](../src/History.cs) | [Saved work](../tools/test-saved-work.ps1) |
| Explorer, app search and responsiveness | [Explorer.cs](../src/Explorer.cs), [AppSearch.cs](../src/AppSearch.cs), [DriveCache.cs](../src/DriveCache.cs) | [Explorer paint](../tools/test-explorer-paint.ps1), [forms and app search](../tools/test-forms.ps1), [responsiveness](../tools/test-responsiveness.ps1) |
| Document editing and navigation | [DocumentsPage.cs](../src/DocumentsPage.cs), [OfficeEdit.cs](../src/OfficeEdit.cs) | [Office editor window](../tools/test-office-edit.ps1), [closing and saving](../tools/test-document-close.ps1) |

## Regression checks

Run the app's tests under Windows PowerShell. The build targets .NET Framework and uses C# 5. The full suite builds `build-test/StickyNotesPro.test.exe` before running the checks.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-all.ps1
```

Use temporary fixtures for saved chats, settings, folders and attachments. The provider tests use local stand-ins. Record live provider behavior separately when it has been exercised.

For a UI regression, test the state that triggers it. Restore a saved Split view during startup, shrink Explorer while it is scrolled, or focus an empty chat composer. Benchmark the same fixture and operation before and after a performance change. A row lookup timing measures lookup cost. Whole-app CPU, memory and responsiveness require their own measurements.

For Documents edit mode, use `tools/test-office-edit.ps1 -AssemblyPath <exe>` to check the released binary on a private desktop with an offscreen editor on a separate UI thread. It checks late window stacking and a busy editor. For a live check, use a disposable Office file and switch between the ribbon, document tabs, sidebar and another app. Record native placement checks and live input checks separately.

For generated files, check content as well as structure. Open the workbook and verify typed cells and IDs with leading zeroes. Inspect Word tables and slide order. Check that text extraction from PDF returns the expected characters as well as the page count. A chat file card with nested Markdown fences needs a longer outer fence and a complete closing fence.

## Full UI sweep (every page, every look, stress)

Run before every release and after any UI change:

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File tools/test-ui-sweep.ps1
```

It captures 33 screens across five looks and three window sizes. The list includes Settings and the sub-tabs in System,
Chrome, Power tools and Shortcuts. It saves PNGs and labeled contact sheets under `build-test\sweep-*`, checks the
visible controls, opens attached context menus, and stresses 300 page switches, 280 resizes and 14 searches. Review
`report.md` and the screenshots together. `-Quick` selects one look and size.

The sweep compiles an offline test executable and seeds sample notes, tasks, clipboard entries and files. It disables
Chrome profile access, agent account status and the embedded browser in the fixture. Browser screenshots show the shell
and Markdown uses the native preview. System screens can show local machine inventory. When using `-Data`, pass a
disposable copy of an approved fixture folder, never the live `data\` folder. Verify real editing, sign-in and web page
rendering separately when they are in scope.

## Package and verify a release

1. Stage the source and needed resources in an allowlisted release folder. Include build scripts, tests and their fixtures, documentation, and relevant project skills. Exclude generated test data, app data, logs, recordings and credentials.
2. Extract the source ZIP into a fresh directory, compare included files with the staged source, and run the full suite there. Confirm the tests load the executable built in that extraction.
3. Build the release from the verified source. Check the resources needed by the features being shipped, including `ChatSlides.pptx` and ffmpeg when distributing the bundled recorder. Smoke-test the exact executable with temporary fixtures.
4. When updating the main executable, keep the previous build available for rollback. Verify the replacement hash and confirm the project's shortcut points to that executable. Record the release filenames, checks and any real-user scenarios still open in [HISTORY.md](../HISTORY.md).

## Plan large changes as a page

Write large or risky changes as a short plan page in `docs/plans/` and publish it for review before building. Follow the [plan page skill](../skills/plan-page/SKILL.md). The first plan is [never lose a note](plans/never-lose-a-note.html), which covers the silent data wipe when a data file is busy at startup.

## Carry the findings forward

The [customer map](customer-map.html) covers the screen routes, shared tools, dependencies and acceptance checks in Egyptian Arabic. The [November 20 audit](customer-audit.md) gives the evidence behind its findings. Keep the route IDs aligned with the sweep and the page constructors. The repository HTML loads `customer-map-tools.png` beside it. For a single-file handout, embed that PNG as a data URL in the HTML's `sprite` constant and verify the handout with networking disabled.

The [project instructions](../AGENTS.md) make knowledge updates part of every task. [HISTORY.md](../HISTORY.md) is the record of what changed and how far verification went. [LESSONS.md](../LESSONS.md) captures proven causes and rules. [Project skills](../skills/README.md) hold procedures that should be reused. Update the relevant source when evidence changes.
