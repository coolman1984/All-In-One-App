---
name: all-in-one-maintenance
description: Maintain the All in One WinForms repository when debugging, testing, packaging or documenting changes to agent chats, file exports, terminals, Explorer and saved work. Use the repository's isolated checks and record evidence as part of every task.
---

# Maintain All in One

Read [recent history](../../HISTORY.md) and the relevant [lessons](../../LESSONS.md) before working. Use the [maintenance guide](../../docs/maintenance.md) to find the implementation, tests and release steps.

## Preserve the behavior that matters

- Keep transcript display data lightweight. Use cached row bounds to find visible rows and retain the full message in storage, including text omitted from the preview. Use isolated fixtures for draft save, clearing, chat switching and reload.
- Preserve the structured state needed on replay. Terminal history stores each pane's command, shell and folder. For legacy entries, recover the folders and show the combined command as historical text. Keep chat resume state scoped to the provider account.
- Test file exports as files. Exercise nested Markdown fences and multipage Unicode output, and verify spreadsheet cell values, Word tables and slide order with independent readers when available.
- In WinForms, exercise saved layouts and scrolled states. Keep file, directory and provider I/O off the UI thread, and reject stale results once the active selection or query has changed.
- Keep Office edit windows independent of the WinForms input queue. Use `tools/test-office-edit.ps1` for a separate UI thread, including the busy editor case. Test real Office clicks separately when a disposable file is available.
- For whole-app visual audits, use `tools/test-ui-sweep.ps1`. It walks 33 screen routes with synthetic content and captures labeled contact sheets. Review those images, including the narrow dark layouts. Test document close and save with `tools/test-document-close.ps1`.
- For releases, start from an extracted source archive. Use Windows PowerShell and the current test binary, check embedded resources, and verify the shipped executable separately from the workspace source.

Update the history before replying. When you discover a proven rule or a procedure worth reusing, update the relevant lesson, guide or skill at the same time. Include any checks still open and distinguish synthetic tests from live behavior.
