# GitHub history on this PC

## 2026-11-21 — Connection checks and skill update

- Queried the public All in One repository through Git HTTPS, GitHub REST and GitHub CLI GraphQL. Git and REST returned the same full SHA for `main` as local HEAD, `ffbf6dc1dcb63a1e5d674912400d21bc67d7a1c5`. GraphQL reported public visibility and default branch `main`. REST confirmed that PR #1 was merged.
- Commands used Git 2.55.0.windows.2 and GitHub CLI 2.96.0. Git used the command-scoped direct HTTPS/OpenSSL route. CLI queries used a process-scoped direct connection. These reads contacted GitHub without publishing anything.
- Retained the original helper in its existing installations. Inspection confirmed the three actions documented in the [connection guide](connections.md). SSH, legacy API routing, repository creation and asset downloads were outside this read-only verification.
- Shared the six Markdown documents across the seven identified agent CLIs, five named profiles and the mandatory-skills source. All 14 installed sets matched the source by SHA-256. Fresh Claude Code, Codex and OpenCode catalogs listed the skill. The other CLI installations were checked by path and file contents. See [distribution](distribution.md) for the locations.

## 2026-11-21 — Publishing the planning guides

- Local `main` and the cached `origin/main` both showed `faf5559`. Live Git refs showed GitHub `main` at `5c5d555` and documentation work at `7de5271`.
- Default Git HTTPS returned `SEC_E_NO_CREDENTIALS`. Default GitHub CLI GraphQL returned a TLS handshake timeout. The command-scoped Git override and a separate short-lived CLI process both worked. The root cause of either failure was never isolated.
- Fast-forwarded local `main`, prepared the documentation on a separate branch, omitted private artifact links and kept the untracked screen recording local. The original public branch retained its history.
- Pushed documentation head `05ef22d` and merged PR #1 with a matching-head condition. GitHub returned merge commit `ffbf6dc`. After fetching, local HEAD, `origin/main` and the live API agreed. Ahead/behind was 0/0. The merge passed a whitespace check and 14 documentation links were verified. [Merged pull request](https://github.com/coolman1984/All-In-One-App/pull/1)

## 2026-11-20 — Initial publication

- The project history records an empty GitHub remote, an initial push of source snapshot `9f75158` and matching local, remote-tracking and live refs after the push. A default proxy route returned HTTP 502. Direct HTTPS worked.
- The bundled Microsoft license retained its existing whitespace. The screen recording stayed local. App regression tests were outside that publication.

## Earlier Windows transfer instructions

The existing mandatory skill recorded a DNS/HTTPS route for the API, private repository creation, preservation of nested repositories, release downloads, SHA-256 and Authenticode checks. The original document supplied no dated result for each operation. Treat those entries as prior procedures. Add a dated result after a real use, with the public ref or asset, verification, and any remaining uncertainty.
