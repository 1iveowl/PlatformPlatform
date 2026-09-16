---
name: e2e
description: Run end-to-end Playwright tests via the developer CLI.
---

# End-to-end Tests

```bash
dotnet run --project developer-cli -- e2e [search-terms...] [--smoke] [--browser <name>] [--retries <n>] [--last-failed] [--only-changed] [--stop-on-first-failure] [--include-slow] [--self-contained-system <name>] [--blazor] [--no-wait-for-aspire] --quiet
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

- `search-terms` - filter by test name, tag, or spec file
- `--smoke` - smoke suite only
- `--browser <name>` - `chromium` (default), `firefox`, `webkit`, `safari`, `all`
- `--retries <n>` - max retries for flaky tests
- `--last-failed` - re-run only the previous run's failures
- `--only-changed` - only spec files with uncommitted changes
- `--stop-on-first-failure`, `-x` - exit on first failure
- `--include-slow` - include `@slow`-tagged tests (excluded by default)
- `--self-contained-system <name>` - narrows to one SCS (e.g. `main`, `account`)
- `--blazor` - runs the specs of the Blazor build root in `blazor/tests/e2e` with `blazor/tests/playwright.config.ts` instead of the self-contained systems; not combinable with `--self-contained-system`. Every other option works the same
- `--no-wait-for-aspire` - skip the Aspire readiness check (use only when Aspire is already up)

Aspire must be running. If not, start it via the `aspire-restart` skill first. The command never starts it.

## Blazor

- Prerequisites: the stack is running with the `blazor-host` resource, so the gateway serves `/blazor/`; the server check probes that URL. The specs live in `blazor/tests/e2e`; when that folder is missing the command fails.
- After `build --blazor`, restart the stack with the aspire-restart skill before running the specs: a `blazor-host` started before the build can reference framework files the build replaced, and the WebAssembly runtime then fails to start ("Interactive: False").
- It runs from `blazor/` with the Playwright CLI pinned in `application/package.json` (from `application/node_modules`), so `application/` needs its npm packages installed.
- The report is written to `blazor/tests/test-results/playwright-report`. `--delete-artifacts --blazor` also deletes `blazor/tests/test-results`.

```bash
dotnet run --project developer-cli -- e2e --blazor --smoke --quiet
```

## Strategy: pick the smallest run that proves the fix

E2E runs are slow. Never blindly run the full suite to verify a small change.

- Verifying a recent edit? `--only-changed`.
- After a fix? `--last-failed`.
- Iterating on one test? Pass a search term: a name (`"user management"`), a tag, or a spec file.
- Add `--stop-on-first-failure` (`-x`) when iterating so the run aborts on the first failure.
- Expecting failures? Start with `--smoke`, get those green, then run the rest.
- Only widen to `firefox` / `webkit` after `chromium` passes - and skip `chromium` on those runs (`--browser firefox`).

## Flaky tests need load

A flaky test run in isolation often passes - the bug only shows under parallel load. Reproduce with the full file or suite before declaring it fixed.

## Always pass --quiet

Without `--quiet` Playwright streams every step to the conversation. On success the CLI prints a single summary; on failure it prints the failed tests and where to find the report.
