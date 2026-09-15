---
name: blazor-publish
description: Publish the Blazor host as a trimmed Release build, serve it in Production behind the gateway, and run the browser harness scripts in blazor/tests (trimmed smoke test, public-page measurement, shell policy).
---

# Blazor publish, serve and harness

```bash
dotnet run --project developer-cli -- start-stack --without-blazor-host [--timeout <seconds>]
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
2. `dotnet run --project developer-cli -- start-stack --without-blazor-host`. It starts a fresh AppHost without the dashboard and without the `blazor-host` resource, with Google, Entra, MitID and Stripe turned off, and returns once the gateway answers over HTTPS and the account API is ready (`--timeout <seconds>`, default 600; on timeout it stops the AppHost and exits 1). The AppHost writes the token signing key the published host reads from the shared user secrets store. The gateway routes `/blazor` to the Blazor host port, which is now free for `blazor-serve`.
3. `blazor-publish`, then `blazor-serve` in the background.
4. `blazor-harness trimmed-smoke --browser all` and any other script.
5. Stop `blazor-serve`, stop the stack with **aspire-stop**, and start the everyday stack again with **aspire-restart**.

The `smoke` job in `.github/workflows/blazor.yml` runs the same order with `--browser chromium`.

## Scripts

- `trimmed-smoke` - signs up through the Blazor pages with the code read from the local mail server, opens the users page on the shared DataList and proves a FluentButton click reaches .NET, with 0 page errors. Fails when the gateway does not serve this publish in Production. On failure it also writes a browser trace (`trimmed-smoke-<browser>-signup-trace.zip` or `trimmed-smoke-<browser>-trace.zip`).
- `public-pages` - measures the six public pages: `--profile unthrottled|throttled|all`, `--samples 7`, `--observe-ms 3000`, `--label <name>`, `--check-budget` (Chromium with the throttled profile only).
- `interactive-load` - cold and warm time to interactive of the authenticated WebAssembly page with the cache outcome of every runtime resource: `--samples 7`, `--label <name>`, `--firefox-preferences <name=value,...>`.
- `shell-policy` - the content security policy cases; `--environment production` for the Production check.

Each script writes a JSON result under `.workspace/blazor-tests/` and prints one line per browser.
