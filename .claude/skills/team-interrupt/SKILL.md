---
name: team-interrupt
description: Interrupt a working team agent with a message it must read before finishing its current task.
---

# Team Interrupt

`SendMessage` is for idle/hibernated agents only. If the agent is working, the message sits in the queue and is processed later against stale context - answers will be obsolete and queued instructions may already be wrong. To reach a working agent, use Interrupt + `SendMessage`. Never queue status checks, redirects, or corrections behind active work.

## Procedure

1. Run (use `developer-cli` exactly as written - do not expand to an absolute worktree path):
   ```bash
   dotnet run --project developer-cli -- claude-command send-interrupt-signal --team <team> --agent <agent>
   ```
2. Capture the stdout line - a single `#<id>` interrupt ID.
3. Send a follow-up `SendMessage` to the same agent with the body prefixed by that ID:
   ```
   #<id> <your instruction>
   ```
4. STOP - no follow-ups.

The ID links the signal to the correct queued message; the active agent skips stale messages until it sees the matching ID.
