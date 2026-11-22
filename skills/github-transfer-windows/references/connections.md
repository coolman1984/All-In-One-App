# Connecting to GitHub from Windows

Use the repository already selected by the user. Examples below use the public All in One repository. Substitute the owner, repository and branch before using them on another project.

## Determine which connection failed

Start with `git status --short --branch`. Confirm the intended origin locally and redact any credentials embedded in a URL before sharing it. These probes answer different questions.

```powershell
git ls-remote --exit-code origin refs/heads/main
gh auth status --hostname github.com
gh repo view coolman1984/All-In-One-App --json nameWithOwner,defaultBranchRef,visibility
gh api repos/coolman1984/All-In-One-App/branches/main --jq '.commit.sha'
```

The Git probe reads a live ref. A public repository can answer anonymously. The auth check tests the current CLI identity. The last two commands exercise GitHub CLI's GraphQL and REST paths. A plain web page or a cached search result cannot confirm a private repo, push permission or the current branch tip. Check the API and Git transport independently.

## Git over HTTPS

Start with normal HTTPS and the configured credential helper. Git Credential Manager can broker interactive sign-in and store a credential for Git. GitHub CLI can also act as Git's credential helper. Check the active identity and repository access before considering a setup change. If sign-in is genuinely needed, use the approved interactive login flow. Run `gh auth setup-git` only when configuring that helper is part of the request. Never print tokens, credential-store contents or authenticated traces.

On November 21 the default Git client returned a Windows Schannel credential error. This **single-command** configuration succeeded for this repository on a network where direct GitHub access worked.

```powershell
git -c http.proxy= -c http.sslBackend=openssl ls-remote --exit-code origin refs/heads/main
```

Use it only where direct HTTPS is permitted. Keep TLS verification on. Use the same Git options for the authorized fetch or push if the read probe succeeds. The test changed two settings together, so it cannot identify which one caused the failure.

## GitHub CLI, REST and GraphQL

The Git transport options above apply to Git. On this PC the default CLI route also returned a GraphQL TLS timeout. A short-lived PowerShell process with a direct route succeeded. In a PowerShell prompt, the child process below preserves the parent shell's proxy configuration and returns the CLI exit code.

```powershell
pwsh -NoProfile -Command {
    $env:HTTPS_PROXY = $null
    $env:HTTP_PROXY = $null
    $env:ALL_PROXY = $null
    gh api repos/coolman1984/All-In-One-App/branches/main --jq '.commit.sha'
    exit $LASTEXITCODE
}
```

Use the default route first on a new network. Read an HTTP 401 as an authentication signal. For a 403, inspect the API's message for access, organization policy or rate limits. For a 404, check spelling, identity and repository visibility. A timeout during TLS happens before a GitHub API response. Repeating device authorization for that symptom can waste a successful sign-in.

## Other connection methods

- **Connected GitHub app.** Check the tools available in this session. Use a supported repository read or write when it gives the required evidence. The connector identity may differ from the shell identity. Git handles the local index and working tree.
- **Browser.** Use the owner's Windows Chrome launcher for a requested visible repository, official release page or browser sign-in. Use the profile selected for that account. Read private state through an authorized login. Keep authorization codes and private URLs out of notes.
- **Existing SSH remotes.** With a configured key, `ssh.exe -T git@github.com` checks the account without changing origin. GitHub's successful authentication greeting normally comes with exit code 1 because shell access is unavailable. Check the server fingerprint against GitHub's published list on first use. If approved SSH traffic on port 22 is unavailable, GitHub also documents `ssh.exe -T -p 443 git@ssh.github.com`. Use `git ls-remote` on the intended repository to check access. SSH is a documented option here. These Windows sessions verified HTTPS.
- **Existing Windows transfer helper.** The earlier PC skill ships `scripts/github_transfer.ps1` under the mandatory-skills source and the original Codex installation. It supports `-Action test-api` (anonymous), `create-repo` and `download`. Its special API route resolves the API hostname using a public resolver and supplies the result to curl with TLS hostname verification. Use it only where that route is authorized. An anonymous test checks reachability only. The legacy creation path retrieves a stored Git credential, passes authorization to curl as a process argument and can mistake a failed lookup for an absent repository. Prefer the connector or `gh repo create` for authenticated creation. The included documentation can be used by every CLI without distributing that executable helper. For downloads, select a new destination and supply the publisher's checksum when available.
- **Sandbox error.** Local `.git/FETCH_HEAD: Permission denied` indicates Git could not write local metadata. Use the host's normal approval for the scoped fetch. More network retries will not fix a local write denial.

The legacy helper is normally at `$env:USERPROFILE\.codex\skills\github-transfer-windows\scripts\github_transfer.ps1`. Find the current mandatory-skills copy under `AI CREW\Mandatory To Use Skills\github-transfer-windows\scripts\` if the Codex path is absent.

When the user asks to open GitHub visibly, use the owner's Chrome launcher. Select its installed fallback if the primary copy is absent.

```powershell
$launcher = 'D:\WORK\Software Development\GitHub\AI CREW\Mandatory To Use Skills\windows-chrome-launcher\scripts\open_chrome.py'
if (-not (Test-Path -LiteralPath $launcher)) {
    $launcher = Join-Path $env:USERPROFILE '.codex\skills\windows-chrome-launcher\scripts\open_chrome.py'
}
python $launcher 'https://github.com/'
```

The older helper provides an anonymous API diagnostic for approved networks.

```powershell
& "$env:USERPROFILE\.codex\skills\github-transfer-windows\scripts\github_transfer.ps1" -Action test-api
```

Official references include [GitHub CLI authentication](https://docs.github.com/en/github-cli/github-cli/authenticating-with-github-cli), [Git Credential Manager](https://github.com/git-ecosystem/git-credential-manager), [GitHub API access through gh](https://cli.github.com/manual/gh_api), [Git's HTTP settings](https://git-scm.com/docs/git-config), [GitHub SSH fingerprints](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/githubs-ssh-key-fingerprints) and [GitHub SSH on port 443](https://docs.github.com/en/authentication/troubleshooting-ssh/using-ssh-over-the-https-port).
