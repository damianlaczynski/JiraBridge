# Backend Coding Guidelines

> Scope: current conventions for implementing the JiraBridge interactive CLI.

## Stack summary

.NET 10 CLI application in one source project with strong folder boundaries:

- `Host`
- `Navigation`
- `Screens`
- `Application`
- `Domain`
- `Infrastructure`
- `Shared`

The template intentionally does not include HTTP, database, auth, or frontend concerns.

## Layer ownership

### Host

- owns `Program.cs`
- owns terminal loop and startup wiring
- owns terminal output and interactive shell behavior
- does not own business logic

### Navigation

- owns menu models, focus, command palette, and suggestions
- translates keyboard intent into UI navigation actions
- does not own sync logic

### Screens

- owns screen rendering state and feature-specific interaction flow
- calls into `Application`
- does not perform direct file-system or Jira operations

### Application

- owns commands, queries, handlers, and application-facing abstractions
- orchestrates flows like `configure`, `validate`, `push`, `pull`, and `resolve`
- decides which infrastructure abstractions are needed
- should not contain direct file-system or Jira SDK calls

### Domain

- owns durable concepts such as repository layout, sync conflicts, marker rules, and artifact invariants
- should stay free of CLI and infrastructure concerns

### Infrastructure

- owns file-system access
- owns Jira API integration
- owns conflict persistence and sync execution adapters
- wires concrete implementations through DI

## Migration rules from `poc`

1. Do not copy whole folders blindly.
2. Move startup and terminal concerns into `Host`.
3. Move keyboard flow, menu state, and command suggestions into `Navigation` and `Screens`.
4. Move orchestration decisions into `Application`.
5. Move stable business concepts into `Domain`.
6. Move direct I/O and Jira calls into `Infrastructure`.

## Command conventions

- Keep one command or query per file in `Application/<Feature>/`.
- Keep the handler in the same file as the command or query.
- Prefer feature folders aligned to the tool surface:
  - `Configuration`
  - `Validation`
  - `Sync`

## Guardrails

- Do not reintroduce a monolithic `Features/*` bag inside the new project.
- Do not let `Host`, `Navigation`, or `Screens` call file APIs or Jira APIs directly.
- Do not bury repository path rules inside infrastructure classes when they belong in `Domain`.
- Before adding a new abstraction, check whether it is only a temporary wrapper for logic that still lives in `poc`.
