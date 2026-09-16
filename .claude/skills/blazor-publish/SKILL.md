---
name: blazor-publish
description: Publish the Blazor host as a trimmed Release build, serve it in Production behind the gateway, and run the browser harness scripts in blazor/tests (trimmed smoke test, public-page measurement, shell policy).
---

# Blazor publish, serve and harness

```bash
dotnet run --project developer-cli -- start-stack --without-blazor-host [--fresh-database] [--timeout <seconds>]
dotnet run --project developer-cli -- blazor-publish [--quiet]
dotnet run --project developer-cli -- blazor-serve
dotnet run --project developer-cli -- blazor-harness <script> [--browser chromium|firefox|webkit|all] [script options]
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

- `blazor-publish` - publishes `blazor/Blazor.Host` from `blazor/` (its own SDK) in Release with `MetricsSupport=false`, `MetadataUpdaterSupport=false` and `WasmEnableHotReload=false`; trimming and Brotli are the SDK publish defaults. The output replaces `.workspace/blazor-publish/`.
- `blazor-serve` - runs that publish with `ASPNETCORE_ENVIRONMENT=Production` on the Blazor host port (base port + 17), with the account API URL and the public and CDN URLs the AppHost would set. The `PUBLIC_*_ENABLED` feature variables are left unset. It keeps running until stopped, so start it in the background.
- `blazor-harness <script>` - runs `node blazor/tests/<script>.mjs` one browser at a time; `all` runs the three browsers in turn and fails if any fails. Options it does not know are passed to the script.

## Order

1. Stop this worktree's stack with the **aspire-stop** skill. `start-stack` never reuses a running stack and refuses while any of its ports is taken.
2. `dotnet run --project developer-cli -- start-stack --without-blazor-host`. It starts a fresh AppHost without the dashboard and without the `blazor-host` resource, with Google, Entra, MitID and Stripe turned off. It returns once one poll finds every resource ready together: both workers listening (they open their port only after migrations and feature flag reconciliation), `/internal-api/ready` returning 200 on the account and main APIs, and the gateway root returning 200 (plus `/blazor/` when the blazor-host resource is included). A worker process that exits, an AppHost that exits, or the timeout (`--timeout <seconds>`, default 600) prints each resource that was not ready, stops the stack and its containers the way `stop` does, and exits 1. `--fresh-database` runs Postgres on a disposable volume of its own that is removed before every start, so the run migrates an empty database and never touches the worktree's everyday database. The AppHost writes the token signing key the published host reads from the shared user secrets store. The gateway routes `/blazor` to the Blazor host port, which is now free for `blazor-serve`.
3. `blazor-publish`, then `blazor-serve` in the background.
4. `blazor-harness trimmed-smoke --browser all` and any other script.
5. Stop `blazor-serve`, stop the stack with **aspire-stop**, and start the everyday stack again with **aspire-restart**.

The `published-security` job in `.github/workflows/blazor.yml` runs the same order with `--fresh-database`: `trimmed-smoke` in en-US and da-DK, `antiforgery`, `authentication-state` and `shell-policy --environment production`, then `verify-results`. Its Chromium leg, on every trigger, then runs `public-pages --browser chromium --profile throttled --check-budget --label budget` (the frozen public-page budget and zero WebAssembly runtime requests) and `interactive-load --browser chromium --label authenticated-startup-baseline` (a labelled reference, no budget), verifies both with `verify-results --label measurements`, and uploads the two result files as `public-page-budget-<commit>`; a missing, incomplete, failed or other-commit result fails the job. The `development-shell-policy` job runs `shell-policy` against the AppHost's Development host as a separate fixture check. Pull requests and pushes run Chromium; the nightly schedule, and a dispatch with `browsers: all` (required on the final stage candidate), run Chromium, Firefox and WebKit. Only the JSON results and the two process logs are uploaded; browser traces hold cookies and typed one-time passwords and stay on the runner.

## Scripts

- `trimmed-smoke` - `--culture en-US|da-DK`. Signs up through the Blazor signup and welcome pages with the code read from the local mail server, opens the users page on the shared DataList and proves typing in the FluentTextInput search box reaches .NET. The verdict is strict: any policy violation, console error, page error or HTTP error response on the signup, welcome or users pages fails it, with nothing allowlisted. Fails when the gateway does not serve this publish in Production. On failure it also writes a local browser trace (`trimmed-smoke-<browser>-<culture>-signup-trace.zip` or `trimmed-smoke-<browser>-<culture>-trace.zip`).
- `antiforgery` and `authentication-state` - the antiforgery round trip with its missing and cross-browser token rejections, and session expiry, revocation, malformed token, two-user isolation with no-store and no token headers, tenant switch and logout.
- `verify-results` - `--label <name> --require <name,...>` with `{browser}` placeholders; fails on a missing result, one from another commit, one that ran fewer cases than expected, or a failed one, and writes `security-matrix-<browser>-<label>.json`.
- `public-pages` - measures the six public pages: `--profile unthrottled|throttled|all`, `--samples 7`, `--observe-ms 3000`, `--label <name>`, `--check-budget` (Chromium with the throttled profile only). A case per profile and page fails on a status other than 200, a host not in Production, a landing on any other URL (a redirect never passes), a WebAssembly runtime request or a page error; a case per profile covers the enhanced navigation to terms; the budget adds a transfer and a first contentful paint case per page. The result records the commit, the publish identity (client assembly fingerprint, endpoint manifest hash) and the runner.
- `interactive-load` - cold and warm time to interactive of the authenticated WebAssembly page with the cache outcome of every runtime resource: `--samples 7`, `--label <name>`, `--firefox-preferences <name=value,...>`. Fails only when a load never becomes interactive, lands elsewhere or raises a page error, never on a time.
- `shell-policy` - the content security policy cases; `--environment production` for the Production check.

Each script writes a JSON result under `.workspace/blazor-tests/` and prints one line per browser.
