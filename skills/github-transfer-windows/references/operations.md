# GitHub operations on this PC

Run each step after checking the preceding result. Replace `OWNER/REPO` and angle-bracket placeholders with the actual values before executing an example. Use full SHAs for comparisons and short SHAs in conversation. These examples assume the default branch is `main`. Read the actual default and confirm the remote for other repositories.

## Check whether local is current

```powershell
git branch --show-current
git status --short --branch
git rev-parse HEAD
git ls-remote --exit-code origin refs/heads/main
```

Compare the full SHA in the final command with local HEAD. Also report modified or untracked files shown by Git. Ignored files, build artifacts and the installed executable are outside this comparison. For a read-only question, these checks suffice when the SHAs match. To measure a difference, fetch the branch and read `git rev-list --left-right --count main...origin/main`. The left count is local-only commits, the right count is remote-only commits. A failed remote query leaves the live state unknown.

## Clone or publish a project

- For a new checkout use `git clone <repo-url> <new-folder>`. Use `--recurse-submodules` when the project uses submodules. Leave an existing checkout and its unsaved files in place.
- For an upload, inventory the requested root, branch, origin, tracked changes, untracked files, nested Git worktrees and large files. Stage only intended paths. Check file contents **and commit messages** entering the destination history for confidential material. Keep credentials, private session links, machine settings and unrelated local media out. A public project may already expose earlier refs even when the new PR is clean.
- Preserve intentional submodules with their own remote and commit. A nested repository may have unpublished commits or dirty files. Establish how it should be shared before publishing a parent reference to it. GitHub's regular Git storage blocks files over 100 MiB. Use the project's Git LFS or release workflow where appropriate.
- For a new repository, confirm owner and visibility. Default new backups to private on this PC unless the user specifies otherwise. Look up the repo first. Treat an ambiguous network or access failure as unresolved. When authorized, create with a command such as `gh repo create OWNER/REPO --private`. Verify the returned owner/name and visibility before configuring origin or pushing.
- Before committing run `git diff --cached --stat`, `git diff --cached --check` and review the staged diff. Run checks appropriate to the change. Push the intended branch explicitly. On an initial empty remote a push creates the first branch. A later push rejection calls for a fresh fetch and comparison.

## Push a branch and merge a PR

1. Fetch and inspect `git log --left-right --oneline origin/main...HEAD`, `git diff --stat origin/main...HEAD` and `git diff --check origin/main...HEAD`. Check the intended base and head, open PRs and checks. Preserve existing local work and other users' branches.
2. Push the reviewed branch with `git push -u origin <branch>`. Query `gh pr list --repo OWNER/REPO --base main --head <branch>` before creating a PR so a retry can reuse it. For a multiline description use a prepared UTF-8 text file and `gh pr create --repo OWNER/REPO --base main --head <branch> --title 'Title' --body-file <file>`.
3. Inspect `gh pr view <number> --repo OWNER/REPO --json baseRefName,headRefOid,isDraft,mergeStateStatus,reviewDecision,statusCheckRollup`. Wait for required reviews, checks or the merge queue. Follow the repository's merge method. For a merge commit use the following PowerShell sequence after reviewing the local HEAD.

```powershell
$reviewed = git rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Could not read reviewed HEAD' }
gh pr merge <number> --repo OWNER/REPO --merge --match-head-commit $reviewed
```

4. Query `gh pr view <number> --repo OWNER/REPO --json state,mergedAt,mergeCommit,url`. If the request timed out or went into a queue, read its state before retrying. If the head changed, review the new commits. A queued merge still has work pending.
5. Once GitHub confirms the merge, fetch, switch to local `main` when safe, then `git merge --ff-only origin/main`. A divergent checkout needs a separate history decision. Verify the live SHA again. If GitHub advanced in the meantime, confirm the merge commit is an ancestor with `git merge-base --is-ancestor <merge-sha> origin/main` and sync to the newer tip. Retain user files throughout.

## Download a release

Use the official repository and exact tag. List assets with `gh release view <tag> --repo OWNER/REPO --json tagName,assets,url`. Select the exact platform and filename, then use `gh release download <tag> --repo OWNER/REPO --pattern <asset-name> --dir <new-folder>`. Avoid overwriting an unrelated download. Compare `Get-FileHash -Algorithm SHA256 -LiteralPath <file>` with a publisher-provided digest. A locally computed hash alone identifies the bytes. For a signed Windows binary inspect `Get-AuthenticodeSignature -LiteralPath <file>`. Report any mismatch and leave execution to a separate install or run request.

References include [git ls-remote](https://git-scm.com/docs/git-ls-remote), [fetch and merge](https://docs.github.com/en/get-started/using-git/getting-changes-from-a-remote-repository), [GitHub file limits](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github), [PR merge options](https://cli.github.com/manual/gh_pr_merge) and [release downloads](https://cli.github.com/manual/gh_release_download).
