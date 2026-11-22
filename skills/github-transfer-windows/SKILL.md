---
name: github-transfer-windows
description: Use GitHub from this Windows PC to check live repository state, connect or troubleshoot Git and gh, clone, publish, merge pull requests, synchronize branches, and verify release downloads.
---

# GitHub on this Windows PC

Start with the requested repository and action. For a status question, query the server and compare it with the checkout. For a push or merge, review what will be published and verify the result against the server. Installing this skill gives an agent instructions. Each operation uses the permissions and tools available to its current session.

| Task | First choice | What to verify |
|---|---|---|
| Compare the checkout with GitHub | Local Git and a live Git ref or GitHub branch API read | Branch name, full commit ID and local file status |
| Clone, fetch, commit, push or update files | Local Git using the repository's configured transport | Destination, reviewed changes and live destination ref |
| Check branches, PRs, visibility, reviews or releases | Connected GitHub app when available, or authenticated GitHub CLI | Repository owner, requested ref and result |
| Create a repository or merge a PR | Supported connector operation or GitHub CLI | Visibility on creation, exact head on merge and final server state |
| Diagnose HTTPS, SSH, authentication or proxy errors | Read-only probes using the affected client | The precise command and route that succeeded |
| Inspect a page or download a release | Official repository or release via GitHub CLI or Chrome | Exact asset, publisher and expected digest |

- [Connection guide](references/connections.md) covers HTTPS, credentials, the API, SSH, browser access and the existing Windows helper.
- [GitHub operations](references/operations.md) gives PowerShell examples for status, upload, PRs, synchronization and release downloads.
- [PC history](references/HISTORY.md) records dated outcomes and evidence. [Lessons](references/LESSONS.md) explains which observations change the next decision.
- [Distribution and sources](references/distribution.md) lists the CLI skill directories, how to refresh them and links to the vendors' documentation.

Keep tokens, private links and local recordings out of published notes. Use the user's existing authorization for the requested operation. A timeout after a remote write calls for a live check before any retry.
