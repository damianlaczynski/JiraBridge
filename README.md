# JiraBridge

Interactive .NET CLI that synchronizes a **Git repository’s markdown backlog** with **Jira** (pull/push, validation, conflicts). The UI is keyboard-driven: command palette, screens per workflow, and explicit dry-run for pushes.

Extended instructions for users (environment, API token, workflows, troubleshooting) are in **[docs/user-guide.md](docs/user-guide.md)**.

## Prerequisites

- **Git**: Run inside a clone; the tool resolves the repo root by walking up to a `.git` directory.
- **.NET**: Use a .NET SDK/runtime compatible with the tool target (`net10.0` today).
- **Terminal**: Interactive stdin is required. If stdin is redirected, the app renders once and exits.

## Install

### From NuGet

```powershell
dotnet tool install --global JiraBridge
```

Command name: **`jirabridge`**.

Update:

```powershell
dotnet tool update --global JiraBridge
```

### From source

```powershell
dotnet build JiraBridge.sln
dotnet run --project src/JiraBridge
dotnet test tests/JiraBridge.UnitTests
```

## Quick start

1. `cd` into your Git repository.
2. Create `.env` in the **repository root** (see [Environment](#environment)). You can copy `.env.example` and fill in real values. Do **not** commit secrets.
3. Run `jirabridge`.
4. Choose **`configure`**, enter your **Jira project key** (e.g. `SCRUM`), confirm. This writes `.jirabridge/settings.json`, prepares the backlog folder, and refreshes Jira metadata cache.
5. Use **`pull`** / **`push`** / **`validate`** as needed. Use **`push`** dry-run before applying changes.

Full scenarios, troubleshooting, and `settings.json` fields: **[docs/user-guide.md](docs/user-guide.md)**.

## Environment

Required variables (system environment or repo-root `.env`; `.env` does not override variables already set in the process):

| Variable | Description |
|----------|-------------|
| `JIRABRIDGE_JIRA_BASE_URL` | HTTPS base URL, e.g. `https://your-domain.atlassian.net` |
| `JIRABRIDGE_JIRA_EMAIL` | Atlassian account email |
| `JIRABRIDGE_JIRA_API_TOKEN` | Atlassian API token (not your password) |

**Getting a token:** In [Atlassian account security](https://id.atlassian.com/manage-profile/security/api-tokens), create an API token and paste it into `JIRABRIDGE_JIRA_API_TOKEN`. Step-by-step notes and links: **[docs/user-guide.md](docs/user-guide.md#how-to-get-a-jira-api-token)**.

On first **configure**, `.env.example` is created in the repo root if missing.

## Commands (interactive home screen)

Filter with the keyboard; **Enter** runs or opens a screen; **Esc** / **Q** as shown on the home screen.

| Command | Purpose |
|---------|---------|
| `configure` | Save repo settings, backlog layout, refresh cached Jira metadata |
| `validate` | Validate local backlog artifacts |
| `push` | Push local changes to Jira (dry-run or apply; arrows / Tab) |
| `push-issue` | Push a single linked issue by key (dry-run or apply) |
| `pull` | Pull Jira changes into the repository |
| `pull-issue` | Pull a single issue by key |
| `conflicts` | List sync conflicts |
| `resolve` | Resolve selected conflict (repository / Jira / merge strategies) |

All of these run inside the interactive UI only; the `jirabridge` executable does not take subcommands like `pull` or `configure` on the command line.

## Repository layout (defaults)

- Settings: `.jirabridge/settings.json`
- Metadata cache: `.jirabridge/project-metadata.json` (path configurable in settings)
- Conflicts store: `.jirabridge/conflicts.json`
- Default backlog root: `docs/jira-bridge`

## Capabilities (summary)

- Interactive home with filterable command palette  
- Configure flow with Jira metadata refresh  
- Local validation  
- Optional sprint-aware backlog tree (`SprintMappingEnabled` in settings)  
- Push with dry-run and step-by-step progress  
- Pull from Jira into repo artifacts  
- Conflict listing, diff preview, interactive resolve strategies  

## License

MIT — see [LICENSE](LICENSE).
