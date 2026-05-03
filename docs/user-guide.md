# JiraBridge user guide

This document covers end-to-end usage: installation, first run, Jira setup, and typical sync workflows. Architecture and contributor docs live in [`AGENTS.md`](../AGENTS.md) and [`repo-map.md`](repo-map.md).

## Requirements

| Requirement | Details |
|-------------|---------|
| Git repository | The tool walks up from the current working directory until it finds a `.git` directory. Run it **inside your clone**, not from an arbitrary folder. |
| .NET | For the **global tool** (`dotnet tool`), you need a runtime/SDK compatible with the target framework used to build the package (currently `net10.0`). From source: .NET 10 SDK installed. |
| Jira (HTTPS) | Uses the REST API; base URL must be **HTTPS** (e.g. `https://your-company.atlassian.net`). Authentication: Atlassian account **email + API token** (HTTP Basic). |
| Interactive terminal | Keyboard-driven UI (arrows, Enter, Esc). If **stdin is redirected** (non-interactive), the app renders once and exits — not suitable for CI until a non-interactive mode exists. |

## Installation

### From NuGet.org (after the package is published)

```powershell
dotnet tool install --global JiraBridge
```

Update:

```powershell
dotnet tool update --global JiraBridge
```

Console command after install: **`jirabridge`** (see `ToolCommandName` in the project file).

### From a local `.nupkg` (pre-publish testing)

```powershell
dotnet tool install --global JiraBridge --add-source C:\path\to\folder\with\nupkg --prerelease
```

### From source (contributors)

From the repository root:

```powershell
dotnet run --project src/JiraBridge
```

## How to get a Jira API token

JiraBridge uses your **Atlassian account API token**, not your normal login password.

1. Sign in to your Atlassian account (the same account you use for Jira Cloud).
2. Open **Atlassian account** security settings for API tokens:
   - Direct link: [Atlassian account — API tokens](https://id.atlassian.com/manage-profile/security/api-tokens)
3. Click **Create API token**, give it a label (e.g. `JiraBridge`), and create the token.
4. **Copy the token immediately** — Atlassian shows it only once. If you lose it, revoke the old token and create a new one.
5. Put the token in `JIRABRIDGE_JIRA_API_TOKEN` (environment variable or repo-root `.env`). Treat it like a password: do not commit it, paste it into chat, or share screenshots containing it.

Official reference: [Manage API tokens for your Atlassian account](https://support.atlassian.com/atlassian-account/docs/manage-api-tokens-for-your-atlassian-account/).

**`JIRABRIDGE_JIRA_EMAIL`** must be the email address of the Atlassian account that owns that token. **`JIRABRIDGE_JIRA_BASE_URL`** is your site URL (e.g. `https://your-site.atlassian.net` — no trailing path like `/jira` unless your organization uses a non-standard host; for typical Cloud sites use the `*.atlassian.net` root).

If your organization uses SSO or enforced policies, your admin may restrict token creation — request access or use a service account according to company policy.

## Jira configuration (environment variables)

Set these in the process environment or in a **`.env`** file at the **Git repository root**:

| Variable | Meaning |
|----------|---------|
| `JIRABRIDGE_JIRA_BASE_URL` | HTTPS base URL of your Jira site, e.g. `https://your-company.atlassian.net` |
| `JIRABRIDGE_JIRA_EMAIL` | Atlassian account email linked to the API token |
| `JIRABRIDGE_JIRA_API_TOKEN` | API token from the steps above (never your account password) |

The `.env` file is loaded when Jira is contacted; variables **already set** in the process are **not** overwritten by `.env`.

On first **configure** in a repo, `.env.example` is created if missing. Copy it to `.env`, fill in values, and **do not commit** `.env` with real secrets.

## First run in a repository

1. `cd` into your Git repo root (or a subdirectory under it).
2. Create and fill `.env` (see above and [How to get a Jira API token](#how-to-get-a-jira-api-token)).
3. Run `jirabridge` (or `dotnet run --project ...` from source).
4. On the home screen choose **`configure`**:
   - Enter your **Jira project key** (e.g. `SCRUM`, `OPS`),
   - Confirm with Enter.

Configure writes `.jirabridge/settings.json`, prepares the backlog tree under `BacklogRoot`, and refreshes Jira metadata cache (including `.jirabridge/project-metadata.json` as configured).

5. Then use **`validate`**, **`pull`**, **`push`**, and other commands as needed.

## `.jirabridge/settings.json`

Typical fields (schema version is currently `SchemaVersion: 1`):

| Field | Description |
|-------|-------------|
| `SchemaVersion` | Settings file format version. |
| `JiraProjectKey` | Jira project key. |
| `BacklogRoot` | Repo-relative path to backlog artifacts (default `docs/jira-bridge`). |
| `MetadataFile` | Path to metadata cache (default `.jirabridge/project-metadata.json`). |
| `SprintMappingEnabled` | `true`: load and use sprint mapping; `false`: omit that layer for a simpler backlog. |

## In-app commands (home screen)

Choose commands with arrows or by typing to filter; **Enter** runs or opens a screen.

| Command | Summary |
|---------|---------|
| **configure** | Save repo settings, backlog folders, refresh Jira metadata for the chosen project. |
| **validate** | Validate local artifacts against rules and cached metadata. |
| **push** | Push local changes to Jira; **dry-run** or apply — arrows / Tab switch mode, Enter runs. |
| **push-issue** | Push one issue by key; artifact must already carry a Jira issue key in metadata. Same dry-run modes as **push**. |
| **pull** | Pull Jira changes into the repository. |
| **pull-issue** | Pull one issue by key (e.g. `SCRUM-21`). |
| **conflicts** | List sync conflicts; Enter opens resolution; **R** refreshes the list. |
| **resolve** | Resolve the selected conflict: **Repository**, **Jira**, or **Merge** (arrows + Enter). |

Home screen: arrows navigate, type to filter, **Esc** clears filter, **Q** quits.

## Typical workflow

1. **configure** when settings are missing or the project changes.
2. **pull** — align the repo with Jira.
3. Edit markdown / backlog layout locally.
4. **validate** — check consistency.
5. **push** — **dry-run** first, then apply.
6. On conflicts: **conflicts** → **resolve** with the right strategy.

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| “Git repository root could not be found” | Current directory is not under a tree that contains `.git`. |
| “Missing required environment variable” | Missing `.env` or vars; or vars not visible to the process. |
| “JIRABRIDGE_JIRA_BASE_URL must be … HTTPS” | Used `http://` or an invalid URL. |
| No interaction / immediate exit | Stdin redirected; use an interactive terminal. |
| Jira API errors | Wrong token, revoked token, missing project access, or rate limits — the message usually includes HTTP status and part of the response body. |

## Distribution notes

For a .NET CLI, the usual distribution is a **`dotnet tool`** package on **NuGet.org** (`JiraBridge`, command `jirabridge`). Alternatives: GitHub Releases with a `.nupkg`, or an internal feed (`dotnet tool install --add-source ...`).

Bump **`PackageVersion`** in `src/JiraBridge/JiraBridge.csproj` before each publish.
