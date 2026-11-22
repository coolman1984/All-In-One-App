# Customer workflow audit · November 20, 2026

[Open the interactive map](customer-map.html). Source revision `faf55591832a34f82dc97507222c52ca55bd4487`. Existing local executable SHA-256 `B011E1718AFB0F4E8EA7585919572FADF9E34E9DB59772BB34C43BCA4D717E1B`.

**Verdict.** Two reproduced workflows can hide the customer's latest work. Resolve these and the read-only routing mismatch before relying on this build for customer editing. The recorder packaging needs a clean-PC acceptance test before portable distribution.

| Priority and evidence | Customer action and consequence | Change and acceptance check |
|---|---|---|
| First · reproduced on local executable | Edit a Markdown note while its file rejects writes, then close the last tab. Save returned false, tab count became zero, the active path cleared and the file retained the old text. | Gate tab removal on a successful save or an explicit discard. Test last tab, multiple tabs, theme rebuild and exit. [Editor](../src/MarkdownPage.cs) · [tab close](../src/EditorTabs.cs) |
| First · reproduced on local executable | Copy the app data folder from Settings, defer restart, then save a new note. The original folder contained it. The selected next-start folder did not. | Reconcile pending changes at the switch or defer the copy to shutdown. Test create, edit and delete between move and restart. [Move](../src/Data.cs) · [restart choice](../src/SettingsForm.cs) |
| First · source trace | The shared Send to menu labels Documents as read only. It calls the overload without an explicit edit choice. The receiver uses the per-file choice or the global setting, which defaults to editing. The dedicated Explorer preview action passes false. | Pass the stated mode end to end. Check both entry points with both global settings and an existing edited tab. [Shared action](../src/AppLinks.cs) · [receiver](../src/DocumentsPage.cs) |
| Before distribution · reproduced on local executable | SHA-256 of the decompressed recording engine matched the local Scoop forwarding executable. On a PC without its backing installation, recording is at risk. The recording test covers stop and queued stop with a stand-in process. | Package an independently runnable encoder and verify a saved, playable recording on a clean PC. [Build](../build.ps1) · [recorder](../src/Snip.cs) |
| Next · source trace | Browser clipping awaits text from the active view, then reads the active tab again for the title and URL. Switching tabs during that interval can misattribute the text. | Snapshot the originating tab and navigation identity. Test two distinct local reports, a delayed result and a tab switch. [SaveAsNote](../src/Browser.cs) |
| Next · source trace | Ask the Assistant opens the pane on Documents. The page context supplies the page name but omits the selected document path. A request referring to “this file” has no explicit document identity. | Show the intended file before sending. Switch between same-named files in different folders and verify the supplied context. [Context](../src/MainForm.cs) · [button](../src/DocumentsPage.cs) |

## What ran

| Check | Result and scope |
|---|---|
| Current-source regression suite | Windows PowerShell built the test executable. 15 of 16 suites passed, covering regression, reliability, forms, dashboard, layout, document close, capture, snip output, recorder shutdown, themes, responsiveness, Explorer, agent conversations, leader teams and saved work. The private-desktop Office test hit Windows access denied. Retrying on the local executable also failed to complete. |
| Screen coverage | 20 main page constructors plus Settings, with nested routes in Chrome, System, Power and Shortcuts. All 33 sweep routes were captured across five looks and three sizes, yielding 495 PNGs. The sweep also ran 300 switches, 280 resizes and 14 searches. Browser and Markdown ran in offline fixture mode. |
| Diagnostics | 32 log entries arose from registry read denial for startup entries and packages. Startup inventory may be partial. Eleven sparse-color warnings came from the Classic look. Narrow dark and Classic contact sheets showed visible controls. The sweep checks presentation and navigation, and skips executing destructive actions. |
| Independent reader | The exported sample workbook opened with openpyxl. The decimal 12.5 was numeric and the ID 001 retained its leading zeroes. The saved-work suite also checked the other six formats. |
| Workload timing | The slowest switch in the 300-switch run took 455 ms on Terminals. Settings construction reached 2,857 ms in one large dark layout. These are individual measurements during a busy sweep. Repeat under controlled load before setting a performance target. |

## Remaining customer checks

1. Try a disposable protected spreadsheet, document, presentation, PDF and saved company email with the customer's installed handlers. Exercise editing, tab switches, save, close and reopen.
2. Run one signed-in provider request, export its response and reopen it. Test actual WebView2 page display, navigation and clipping with two distinguishable reports.
3. Test startup inventory as a standard user and surface partial reads. Use a disposable system for startup changes, cleanup and uninstall.
4. Test recording on a clean machine and verify playback. Test the two save-related fixes with denied writes and a deferred restart before release.

No app behavior or release package was changed by this audit. The earlier tests in project history belong to their recorded revisions and builds.

## November 22 repair pass

Base revision `cbed099`. The earlier November 20 measurements above describe the then-current executable.

| Area | Repaired behavior | Verification |
|---|---|---|
| Startup and data relocation | A temporarily unreadable primary blocks startup before it can be overwritten. Corruption can recover from a readable backup. A data move copies the final state on the next launch, refuses occupied targets, and retains the source. | Isolated locked and corrupt inputs, late file edits, adds and deletes, and a target that became occupied. |
| Markdown and Office handoffs | Failed saves veto Markdown close and normal rebuild or exit. LF files keep their line endings. The shared Documents action opens a preview, the tab remembers its choice, and ambiguous save names require a path. | Native Windows document-close tests passed, including a locked Markdown file and LF preservation. Live Office and protected files still need customer fixtures. |
| Browser clipping | The title, URL and text come from one page. Re-navigation cancels the capture. Equal titles receive distinct filenames and the reserved Windows device names are escaped. | Isolated clipping file checks passed. Test two real browser tabs and a navigation during capture on Windows. |
| Chats and typing | Chat switches detach old callbacks. A failed agent attempt retains the question for retry. Stopping prevents retry. Snippets respect a leading word boundary and distinguish AltGr from shortcuts. | Native assistant and layout fixtures passed. Live provider stop and retry and actual keyboard layouts remain open. |
| Schedules and data vault | Saved schedules survive unreadable or malformed files. Invalid times fail validation. One broken job cannot stop the rest. The vault retries failed initialization and skips files that change during extraction. | Native vault and memory suites passed. Invalid and corrupt schedules and CSV refresh were also checked in isolation. |
| Workflow recording and release | Finalization reconstructs a damaged event log and surfaces save errors. Queued events stay with their original recording. Both release scripts reject missing or unsuitable encoders and embed the same verified artifact. | The native workflow and recorder suites passed. Both release executables had six embedded resources. The embedded encoder recorded and decoded a desktop MP4. Live recorder controls and keyboard and mouse hooks still need customer desktop verification. |

At the time of this repair pass, scheduled jobs only notified. The workflow completion below records the later implementation.

Verification records: [Windows regression suites](https://github.com/coolman1984/All-In-One-App/actions/runs/44983887686), [layout fixture](https://github.com/coolman1984/All-In-One-App/actions/runs/44984052358), and [packaging and encoding](https://github.com/coolman1984/All-In-One-App/actions/runs/44984180068).


## Workflow completion

| Prior gap | Delivered |
|---|---|
| Schedules notified without executing | The timer dispatches Assistant prompts, saved skills or registered tools. The Scheduled jobs window exposes state, results and retries. |
| Recordings stopped at a draft | Build last sends the latest saved recording through the configured Assistant to create and verify a reusable tool and skill. |
| Unavailable agent looked successful | Real provider failures surface as failures. Explicit test simulations are labeled. |
| Live backups copied database journal files | The backup includes an independent SQLite snapshot. Restore parks unfinished scheduled work for review. |
| Floating cards lost the last paste target | Cards track the last external foreground window and verify the target and clipboard before sending paste. |

Customer checks still include protected Office files, two-tab WebView2 clipping, live provider credentials and site behavior, and real keyboard and floating-card behavior.

The [Windows workflow verification](https://github.com/coolman1984/All-In-One-App/actions/runs/44986545611) passed 12 suites, including the real scheduler clock, a local Assistant fixture, duplicate and retry handling, and backup of an open vault.
