---
name: format
description: Auto-format code via the developer CLI - backend (.NET via JetBrains cleanupcode), frontend (oxfmt + oxlint --fix), and the developer CLI itself.
---

# Format

```bash
dotnet run --project developer-cli -- format [--backend] [--frontend] [--cli] [--blazor] [--self-contained-system <name>] [--no-build] [--all-files] [--verbose]
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

- `--backend` - .NET (JetBrains cleanupcode)
- `--frontend` - React/TypeScript (oxfmt + oxlint --fix)
- `--cli` - the developer CLI itself
- `--blazor` - the Blazor build root in `blazor/`, which resolves the SDK in its own `global.json`
- `--self-contained-system <name>` - narrows backend formatting to one SCS (e.g. `account`, `main`)
- `--no-build` - skip the `dotnet tool restore` step (faster after a recent run)
- `--all-files` - format every file in the solution. Default is to format only `.cs` files changed against `origin/main` (faster). For `--blazor` the default also formats untracked `.cs` and `.razor` files under `blazor/`, so a new file is formatted before it is committed
- `--verify-build` - with `--blazor`, build the Blazor build root after formatting and fail when it no longer builds. CI runs `format --blazor --no-build --all-files --verify-build` and fails on a diff

No arguments formats everything (changed-only by default). Unformatted code fails CI - commit all changes, never revert.

After `build` succeeds, run `format`, `lint`, `test` in parallel with `--no-build`.

## Examples

```bash
dotnet run --project developer-cli -- format                                            # everything
dotnet run --project developer-cli -- format --backend                                  # all backend
dotnet run --project developer-cli -- format --frontend                                 # frontend
dotnet run --project developer-cli -- format --backend --self-contained-system account  # one SCS
```

## Output

By default the CLI prints a single line on success and a short error summary with the log path on failure; read the log if you need details. Pass `--verbose` for the full output. Backend is slow - run last. Frontend is fast.
