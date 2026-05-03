# Repository Map

> Scope: where things live and which layer owns what. This is the quickest orientation document after `AGENTS.md`.

## Top level

- `src/` contains the interactive CLI application project.
- `tests/` contains the unit test project.
- `docs/` contains repo-specific guidance for structure and verification.

## Project map

The application now uses one source project with internal folder boundaries.

### `src/JiraBridge/Host`

- entry point in `Program.cs`
- terminal loop in `Host/Terminal/`
- startup and DI wiring in `Bootstrap/`

This area owns app startup and terminal interaction only.

### `src/JiraBridge/Navigation`

- menu state and navigation
- command palette and command suggestion
- keyboard-driven movement between interactive elements

### `src/JiraBridge/Screens`

- user-facing screens grouped by feature
- screen-specific view models
- no direct Jira or file-system access

### `src/JiraBridge/Application`

- command/query contracts and handlers
- application service abstractions
- orchestration of repository, sync, and Jira workflows

Current feature slices:

- `Configuration/`
- `Validation/`
- `Planning/`
- `Sync/`

### `src/JiraBridge/Domain`

- repository layout defaults
- sync conflict models
- future domain invariants for artifact metadata and synchronization rules

### `src/JiraBridge/Infrastructure`

- file-system implementations
- Jira API adapters
- conflict storage and sync execution adapters
- DI composition for concrete implementations

This area now contains the active repository, Jira, sync, and conflict implementations used by the interactive app.

## Test map

- `tests/JiraBridge.UnitTests`
  - parser behavior
  - domain defaults
  - application and infrastructure helpers that do not need live Jira access
  - smoke-style fixture scenarios for nested backlog structures and sync relationships

There is still no separate integration test project. Broader repository fixtures currently live in the unit test project and use stubbed Jira HTTP responses.
