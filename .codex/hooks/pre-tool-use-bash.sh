#!/bin/bash

# Pre-tool-use hook for Bash commands
# Blocks direct dotnet/npm/playwright invocations and points at the developer CLI skills.

# Read the JSON input from stdin
input=$(cat)

# Extract the command from the JSON input
cmd=$(echo "$input" | sed -n 's/.*"command":"\([^"]*\)".*/\1/p')

# Check the command and decide whether to block it
case "$cmd" in
    "cd "*|*" && cd "*|*";cd "*|*"; cd "*) echo "❌ Do not change directory. Use --project flags or relative paths from the repo root." >&2; exit 2 ;;
    *"dotnet build"*) echo "❌ Use the **build** skill (\`dotnet run --project developer-cli -- build --quiet\`)" >&2; exit 2 ;;
    *"dotnet test"*) echo "❌ Use the **test** skill (\`dotnet run --project developer-cli -- test --quiet\`)" >&2; exit 2 ;;
    *"dotnet format"*) echo "❌ Use the **format** skill (\`dotnet run --project developer-cli -- format --quiet\`)" >&2; exit 2 ;;
    *"npm run format"*) echo "❌ Use the **format** skill (\`dotnet run --project developer-cli -- format --quiet\`)" >&2; exit 2 ;;
    *"npm test"*) echo "❌ Use the **test** skill (\`dotnet run --project developer-cli -- test --quiet\`)" >&2; exit 2 ;;
    *"npm run build"*) echo "❌ Use the **build** skill (\`dotnet run --project developer-cli -- build --quiet\`)" >&2; exit 2 ;;
    *"npx playwright test"*) echo "❌ Use the **e2e** skill (\`dotnet run --project developer-cli -- e2e --quiet\`)" >&2; exit 2 ;;
    *) exit 0 ;;
esac
