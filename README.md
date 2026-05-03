# JiraBridge

JiraBridge is a .NET interactive CLI for synchronizing a repository backlog with Jira. The app now runs as a single-project interactive terminal application with explicit boundaries between host flow, screens, application orchestration, domain rules, and infrastructure adapters.

## Current Capabilities

- interactive command home screen with filterable command palette
- repository `configure` flow with Jira metadata refresh
- local `validate`
- optional sprint-aware backlog tree controlled by `SprintMappingEnabled` in `.jirabridge/settings.json`
- `push` with interactive dry-run preview and step-by-step progress feedback
- `pull` from Jira into repository artifacts
- conflict listing, full diff inspection, and interactive resolve strategies
- smoke-style fixture coverage for nested backlog hierarchies and multi-level relationships

## Current Structure

- `src/JiraBridge` - single application project
- `src/JiraBridge/Host` - startup, terminal loop, rendering, and progress state
- `src/JiraBridge/Navigation` - menu and command suggestion flow
- `src/JiraBridge/Screens` - user-facing screens and view models
- `src/JiraBridge/Application` - use-case orchestration
- `src/JiraBridge/Domain` - repository layout and sync domain types
- `src/JiraBridge/Infrastructure` - repository, Jira, parsing, and sync adapters
- `tests/JiraBridge.UnitTests` - unit and smoke-style fixture tests

## Commands

Run from the repository root:

```powershell
dotnet build JiraBridge.sln
dotnet run --project src/JiraBridge
dotnet test tests/JiraBridge.UnitTests
```

## Interactive Surface

- `configure`
- `validate`
- `push`
- `pull`
- `conflicts`
- `resolve`

The interactive app in `src/` is now the only maintained command surface in this repository.

## Documentation

- `docs/repo-map.md` - where code lives and which folder owns what
- `docs/backend-coding-guidelines.md` - conventions for the interactive CLI solution
- `docs/testing-playbook.md` - how to verify CLI and migration work
- `AGENTS.md` - fast-start guide for contributors and AI agents

## License

This project is licensed under the MIT License. See [`LICENSE`](LICENSE) for details.
