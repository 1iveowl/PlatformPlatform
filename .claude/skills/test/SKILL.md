---
name: test
description: Run backend (.NET) xUnit unit and integration tests via the developer CLI.
---

# Test

```bash
dotnet run --project developer-cli -- test [--self-contained-system <name>] [--filter <expr>] [--no-build] [--exclude-category <cat>] [--verbose]
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

Backend only - there is no frontend test runner.

- `--self-contained-system <name>` - narrows to one SCS (e.g. `account`, `main`)
- `--filter <expr>` - forwarded to `dotnet test --filter` to scope to a subset of tests
- `--no-build` - skip rebuild before running (faster after a recent build)
- `--exclude-category <cat>` - defaults to `Noisy`; pass an empty string to include them

No arguments runs every test across every SCS.

After `build` succeeds, run `format`, `lint`, `test` in parallel with `--no-build`.

## Examples

```bash
dotnet run --project developer-cli -- test                                                # all tests
dotnet run --project developer-cli -- test --self-contained-system account                # one SCS
dotnet run --project developer-cli -- test --filter "FullyQualifiedName~LoginTests"       # filter by name
dotnet run --project developer-cli -- test --no-build                                     # after a recent build
```

## Output

By default a passing run prints one summary line (totals and duration). A failing run prints the summary, the failed test names (capped at 30) and the path to the full log; read the log for stack traces. Pass `--verbose` only when you need every test result streamed; it floods the conversation.
