#!/bin/bash
# Scan the last assistant turn in the transcript for the banned excuse phrase.
# If found, surface a reminder to the user (non-blocking).

input=$(cat)
transcript=$(echo "$input" | jq -r '.transcript_path // empty')

if [ -z "$transcript" ] || [ ! -f "$transcript" ]; then
  exit 0
fi

pattern='pre-\?existing'

reader=""
if command -v tac >/dev/null 2>&1; then
  reader="tac"
elif tail -r /dev/null >/dev/null 2>&1; then
  reader="tail -r"
fi

last_text=""
if [ -n "$reader" ]; then
  last_text=$($reader "$transcript" 2>/dev/null | awk 'NR<=50' | \
    while IFS= read -r line; do
      echo "$line" | jq -r 'select(.type == "assistant") | [(.message.content[]? | (.text // .thinking // empty))] | join(" ")' 2>/dev/null
    done | head -1)
fi

if echo "$last_text" | grep -qi "$pattern"; then
  cat >&2 <<'EOF'
REMINDER: that phrase is not an accepted excuse.

Main is always clean -- CI enforces this. Any failure on the branch was introduced by us and must be fixed before any approval, handoff, or commit. The Boy Scout Rule applies: leave the code in a better state than you found it.

Pull the andon cord: stop and fix the failure. If it is outside your scope and you are in multi-agent mode, escalate to the team lead. Never approve, hand off, or commit with known failures.
EOF
  exit 2
fi

exit 0
