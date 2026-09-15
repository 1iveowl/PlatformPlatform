---
name: blazor-publish
description: Publish the Blazor host as a trimmed Release build, serve it in Production behind the gateway, and run the browser harness scripts in blazor/tests (trimmed smoke test, public-page measurement, shell policy).
---

# Blazor publish, serve and harness

```bash
dotnet run --project developer-cli -- blazor-publish [--quiet]
dotnet run --project developer-cli -- blazor-serve
dotnet run --project developer-cli -- blazor-harness <script> [--browser chromium|firefox|webkit|all] [script options]
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

- `blazor-publish` - publishes `blazor/Blazor.Host` from `blazor/` (its own SDK) in Release with `MetricsSupport=false`, `MetadataUpdaterSupport=false` and `WasmEnableHotReload=false`; trimming and Brotli are the SDK publish defaults. The output replaces `.workspace/blazor-publish/`.
- `blazor-serve` - runs that publish with `ASPNETCORE_ENVIRONMENT=Production` on the Blazor host port (base port + 17), with the account API URL and the public and CDN URLs the AppHost would set. The `PUBLIC_*_ENABLED` feature variables are left unset. It keeps running until stopped, so start it in the background.
- `blazor-harness <script>` - runs `node blazor/tests/<script>.mjs` one browser at a time; `all` runs the three browsers in turn and fails if any fails. Options it does not know are passed to the script.

## Order

1. Start the stack with the **aspire-restart** skill. It writes the token signing key the published host reads from the shared user secrets store.
2. Stop the Aspire resource `blazor-host` (Aspire MCP `execute_resource_command`, command `stop`), because the gateway routes `/blazor` to that port. `blazor-serve` refuses to start while the port is taken.
3. `blazor-publish`, then `blazor-serve` in the background.
4. `blazor-harness trimmed-smoke --browser all` and any other script.
5. Stop `blazor-serve`, then start the `blazor-host` resource again (command `start`).

## Scripts

- `trimmed-smoke` - signs up through the Blazor pages with the code read from the local mail server, opens the users page on the shared DataList and proves a FluentButton click reaches .NET, with 0 page errors. Fails when the gateway does not serve this publish in Production.
- `public-pages` - measures the six public pages: `--profile unthrottled|throttled|all`, `--samples 7`, `--observe-ms 3000`, `--label <name>`, `--check-budget` (Chromium with the throttled profile only).
- `interactive-load` - cold and warm time to interactive of the authenticated WebAssembly page with the cache outcome of every runtime resource: `--samples 7`, `--label <name>`, `--firefox-preferences <name=value,...>`.
- `shell-policy` - the content security policy cases; `--environment production` for the Production check.

Each script writes a JSON result under `.workspace/blazor-tests/` and prints one line per browser.
