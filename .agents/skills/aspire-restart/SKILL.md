---
name: aspire-restart
description: Start or restart the Aspire AppHost via the developer CLI. Always use this, never `run`, `aspire run` or `aspire restart`.
---

# Restart Aspire

```bash
dotnet run --project developer-cli -- restart
```

Use `developer-cli` exactly as written - do not expand to an absolute worktree path.

Stops any running Aspire AppHost and starts a fresh instance. Detached by default - the CLI returns immediately while Aspire keeps running.

Always use `restart`, even when nothing is running yet. It is a no-op when Aspire is not up, and the safe default in every other case. Never use the developer CLI's `run` command, `aspire run`, or `aspire restart`.

- `--public-url <url>` - set `PUBLIC_URL` (e.g. an ngrok URL)

## When to use

- After backend or frontend changes - `restart` is the safe default; it works whether Aspire is up or not.
- To run database migrations.
- When hot reload breaks or stops picking up changes.
- Before running e2e tests on a fresh stack.

## Port allocation

Fully automatic. On a fresh checkout the developer CLI bootstraps `.workspace/port.txt`: the root gets the default base port; worktrees scan a fixed list of candidates and pick the first one whose ports are all free. Once written, the file is the authoritative allocation for the lifetime of the checkout.

## Output is fire-and-forget

The CLI prints `Aspire is restarting on https://app.dev.localhost:<port>` and exits before Aspire is fully ready.

## Stopping Aspire

To stop without restarting, run `dotnet run --project developer-cli -- stop`. Rarely needed - prefer `restart` for everyday use.
