---
name: test
description: Run .NET xUnit unit and integration tests via the developer CLI - the application backend, the Blazor build root and the developer CLI itself.
---

# Test

```bash
dotnet run --project developer-cli -- test [--backend] [--self-contained-system <name>] [--gateway] [--blazor] [--cli] [--filter <expr>] [--no-build] [--exclude-category <cat>] [--verbose]
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

.NET only - there is no frontend test runner.

- `--backend` - only the application solution in `application/`, without the Blazor build root
- `--self-contained-system <name>` - narrows to one SCS (e.g. `account`, `main`)
- `--gateway` - only `AppGateway.Tests`
- `--blazor` - the tests of the Blazor build root in `blazor/`, with the SDK from its own `global.json`; not combinable with `--self-contained-system` or `--gateway`
- `--cli` - the developer CLI tests in `developer-cli/Tests`
- `--filter <expr>` - forwarded to `dotnet test --filter` for every selected target
- `--no-build` - skip rebuild before running (faster after a recent build)
- `--exclude-category <cat>` - defaults to `Noisy`; pass an empty string to include them. Applies to every selected target

No target flag runs every test in the application solution and then the Blazor build root. The developer CLI tests run only with `--cli`. Target flags combine, for example `--backend --cli`. Every selected target runs even when an earlier one fails, and the command exits non-zero when any failed.

After `build` succeeds, run `format`, `lint`, `test` in parallel with `--no-build`.

## Examples

```bash
dotnet run --project developer-cli -- test                                                # application solution and Blazor
dotnet run --project developer-cli -- test --backend                                      # application solution only
dotnet run --project developer-cli -- test --self-contained-system account                # one SCS
dotnet run --project developer-cli -- test --blazor                                       # Blazor build root only
dotnet run --project developer-cli -- test --cli                                          # developer CLI
dotnet run --project developer-cli -- test --filter "FullyQualifiedName~LoginTests"       # filter by name
dotnet run --project developer-cli -- test --no-build                                     # after a recent build
```

## Output

By default a passing run prints one summary line (totals and duration) per target, prefixed with the target name when more than one target runs. A failing run prints the summary, the failed test names (capped at 30) and the path to the full log; read the log for stack traces. Pass `--verbose` only when you need every test result streamed; it floods the conversation.
