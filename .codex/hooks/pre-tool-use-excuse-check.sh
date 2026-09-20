#!/bin/bash
# If a tool call contains the banned excuse phrase, inject a reminder
# (non-blocking) to the agent. The tool call still proceeds.

input=$(cat)
pattern='pre-\?existing'

if echo "$input" | jq -r '.tool_input | .. | strings' 2>/dev/null | grep -qi "$pattern"; then
  jq -n --arg msg 'REMINDER: that phrase is not an accepted excuse.

Main is always clean -- CI enforces this. Any failure on the branch was introduced by us and must be fixed before any approval, handoff, or commit. The Boy Scout Rule applies: leave the code in a better state than you found it.

Pull the andon cord: stop and fix the failure. If it is outside your scope and you are in multi-agent mode, escalate to the team lead. Never approve, hand off, or commit with known failures.' \
    '{"hookSpecificOutput":{"hookEventName":"PreToolUse","additionalContext":$msg}}'
fi

exit 0
