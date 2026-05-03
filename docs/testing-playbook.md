# Testing Playbook

> Scope: how to verify interactive CLI work and migration steps in this repository.

## Test project

- Unit tests: `tests/JiraBridge.UnitTests`

## What belongs in unit tests

- command suggestion and navigation behavior
- domain defaults and invariants
- small application handlers with fake abstractions
- small infrastructure helpers that do not require live Jira access
- smoke-style repository fixtures using stubbed Jira responses

## Commands

Run from:

```powershell
repository root
```

Build:

```powershell
dotnet build JiraBridge.sln
```

Tests:

```powershell
dotnet test tests/JiraBridge.UnitTests
```

## Change-based checklist

### CLI command change

- update parser tests
- update router behavior if needed
- update help text

### Domain change

- add or update unit tests for path defaults, marker rules, or sync invariants

### Infrastructure migration from `poc`

- add targeted unit tests around extracted helpers before or during the move
- prefer larger fixture scenarios when migrating sync, conflict, parent/child, or relationship behavior

### Interactive CLI change

- verify screen rendering still exposes the intended state and keyboard flow
- cover progress or outcome changes when user-facing steps change
- update docs if command UX or conflict handling expectations change

## Verification rule

For any non-trivial change, run at least:

- `dotnet build JiraBridge.sln`
- `dotnet test tests/JiraBridge.UnitTests`

## Current note

`dotnet build JiraBridge.sln` may emit `NU1900` warnings when the environment cannot reach `https://api.nuget.org/v3/index.json` for package vulnerability audit data. Treat that as environment noise unless it turns into a restore or compile failure.
