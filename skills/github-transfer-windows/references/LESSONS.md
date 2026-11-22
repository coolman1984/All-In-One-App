# GitHub lessons on this PC

| Observation | Decision for the next task |
|---|---|
| Both local refs said `faf5559` while live GitHub said `5c5d555`. | Read the live branch ref before declaring a checkout current. Fetch before reporting ahead/behind counts. |
| The default proxy returned 502. Later Git and GitHub CLI produced different TLS errors. | Test the failing client against the same read-only target. Keep adjustments local to one command or child process. Record the combination that succeeded and which variables were actually isolated. |
| A browser lookup missed the repository while authenticated Git and the API returned its live state. | Verify owner, name, identity and visibility with a direct repository query. Browser search indexing cannot establish whether a repo exists. |
| Git could read the server but fetching failed to write `.git/FETCH_HEAD`. | Use the host's scoped approval for updating local Git metadata. Confirm the new ref after the fetch. |
| The branch had commits with private links and an unrelated recording was untracked. | Review the proposed file diff and the commit history that will become reachable. Stage explicit paths. A cleaned destination commit still leaves any earlier public branch intact. |
| PR creation, push and merge each have state that can be read back. | After a timeout, query the live branch or the existing PR by repository, base and head before retrying. Pin a merge to the reviewed head and confirm the merged state. |
| The documentation merge advanced local source without building the executable. | Report branch SHA, tracked changes and local exceptions. Tie test, binary and release claims to the artifact actually checked. |
| The legacy helper accepts a creation request after some ambiguous lookup failures. | Prefer a fresh authenticated repo lookup and the normal GitHub CLI creation path. Keep secrets out of stdout, process arguments and diagnostic traces. |

Use the [connection guide](connections.md) for the exact probes and [PC history](HISTORY.md) for the dated evidence.
