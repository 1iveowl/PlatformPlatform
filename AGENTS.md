## Behavioral Guidelines

1. Think before coding: state assumptions; if uncertain, ask rather than guess; when several interpretations exist, present them rather than pick one silently.
2. Define success criteria before iterating, then loop until they are verified.
3. Read before you write: the file's exports, its immediate caller and the shared utilities it uses.
4. Checkpoint after each step: what was done, what is verified, what is left.
5. Fail loud: "completed", "tests pass" and "works" are wrong if anything was skipped or left unverified.

## Build, Test, and Format

Use the developer CLI skills (`build`, `test`, `format`, `lint`, `e2e`, `aspire-restart`, `team-interrupt`) for all code workflows; they wrap `dotnet run --project developer-cli -- <command>`. Never run `dotnet`, `npm` or `npx` directly and never `cd`; the pre-tool-use hook blocks both. The excuse-check and interrupt hooks explain themselves when they fire; do what their message says.

Aspire: start or restart the AppHost only through the `aspire-restart` skill. Local ports derive from the base in `.workspace/port.txt` (offsets in `application/shared-kernel/SharedKernel/Configuration/PortAllocation.cs`); never take a port from the Aspire MCP, which may describe another worktree's stack. Call `mcp__aspire__select_apphost` with the cwd before any other Aspire MCP tool.

Never commit, amend or revert without an explicit user instruction each time. Commit messages: one descriptive line in imperative form, no body.

## Product Management Tool

Whenever you see `[PRODUCT_MANAGEMENT_TOOL]`, replace it with the configured value.

```
PRODUCT_MANAGEMENT_TOOL="Linear"
```

When working with [features] or [tasks], read `.claude/reference/product-management/[PRODUCT_MANAGEMENT_TOOL].md` for lookups, status updates and how [Active], [Review] and [Completed] map to the tool. Read the [feature] for context and the [task] for its requirements.

## Auto Memory

Never write to or edit auto memory files (MEMORY.md or anything in a memory directory); the user manages them.

## Source of Truth

**Verify every path, name and API route against the codebase before stating it, even when asked to answer from memory or to skip the lookup.** Never rely on memory, cached context or a prior session for these. Only read files inside this repository unless explicitly asked to look elsewhere.

## Project Structure

- `application/`: one folder per self-contained system, plus `shared-kernel/` and `shared-webapp/`.
- `cloud-infrastructure/`: Bash and Azure Bicep infrastructure as code.
- `developer-cli/`: the .NET developer CLI.
