# Finding and maintaining the shared skill

Open `github-transfer-windows` in a fresh agent session or ask the agent to use it to check whether a checkout matches GitHub. In Claude Code the personal skill can also be invoked as `/github-transfer-windows`. Agents that expose a skills panel can show the loaded path. A model profile in a CLI normally uses that CLI's skill directory.

| Agent or installation | Personal discovery root used on this PC |
|---|---|
| Codex CLI | `~/.agents/skills/` and the existing `~/.codex/skills/` installation |
| Claude Code CLI | `~/.claude/skills/` |
| Cline CLI, including its DeepSeek and other model profiles | `~/.cline/skills/` |
| Antigravity CLI, including its Gemini models | `~/.gemini/config/skills/` |
| OpenCode | `~/.config/opencode/skills/` |
| Grok Build | `~/.grok/skills/` |
| GitHub Copilot CLI | `~/.copilot/skills/` |
| All in One named Claude and Codex accounts | The existing account home's `skills/` directory under `~/.herdr-accounts/<provider>/<profile>/` |

Gemini CLI also documents the shared `~/.agents/skills/` path. The executable identified on this PC for the Gemini profiles was Antigravity's `agy`. A source checkout or an old settings directory alone does not establish that a CLI is installed.

## Refresh after a change

The All in One project keeps an editable documentation copy under `skills/github-transfer-windows/`. The existing PC-wide source is `AI CREW/Mandatory To Use Skills/github-transfer-windows/` beside this project under the same GitHub workspace parent. Check for changes at both locations first and reconcile them. Replicate `SKILL.md` and the Markdown files in `references/` into the skill folder at each discovery root above. This preserves the existing helper and UI metadata in the installations that already have them.

Compare the source and destination files with `Get-FileHash -Algorithm SHA256 -LiteralPath <file>`. If an installed file differs unexpectedly, inspect it before replacing it. To check actual discovery, OpenCode has `opencode debug skill`, Antigravity has an interactive `/skills` view and Codex supports `skills/list` in its app-server protocol. File hashes confirm installed contents. A runtime listing additionally confirms which copy the CLI selected.

## Official sources

- GitHub access and operations are documented in the [connection guide](connections.md) and [operations guide](operations.md). The [public pull request](https://github.com/coolman1984/All-In-One-App/pull/1) is the evidence for the November 21 merge.
- Personal and shared discovery rules are documented by [Codex](https://developers.openai.com/codex/skills), [Claude Code](https://code.claude.com/docs/en/skills), [Cline](https://docs.cline.bot/customization/skills) and [OpenCode](https://opencode.ai/docs/skills/).
- Additional discovery rules are documented by [Antigravity](https://antigravity.google/docs/customization-discovery/), [Gemini CLI](https://geminicli.com/docs/cli/skills/), [Grok Build](https://docs.x.ai/build/features/skills-plugins-marketplaces) and [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-skills).
