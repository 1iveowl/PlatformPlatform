---
name: build
description: Build (compile) the solution via the developer CLI - backend (.NET), frontend (React/TypeScript), and the developer CLI itself.
---

# Build

```bash
dotnet run --project developer-cli -- build [--backend] [--frontend] [--cli] [--blazor] [--self-contained-system <name>] [--verbose]
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

- `--backend` - .NET
- `--frontend` - React/TypeScript
- `--cli` - the developer CLI itself
- `--blazor` - the Blazor build root in `blazor/`, which resolves the SDK in its own `global.json`
- `--self-contained-system <name>` - narrows the backend build to one SCS (e.g. `account`, `main`)

No arguments builds everything.

## Examples

```bash
dotnet run --project developer-cli -- build                                           # everything
dotnet run --project developer-cli -- build --backend                                 # all backend
dotnet run --project developer-cli -- build --frontend                                # frontend
dotnet run --project developer-cli -- build --backend --self-contained-system main    # one SCS
```

## Output

By default the CLI prints only failures (distinct compiler errors, which include warnings treated as errors, capped at 20 lines) and the path to the full log, or one line on success. Read the log for anything more. Pass `--verbose` only when you need the full streaming output; it floods the conversation.
