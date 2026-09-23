#!/usr/bin/env bash
# Read-only setup probe for the device pass. Run on the macOS host, not in the development container; run.mjs runs it
# first: bash blazor/tests/device/probe.sh
# It changes nothing on the machine. The Simulator tools are read through Xcode (DEVELOPER_DIR, defaulting to
# /Applications/Xcode.app) even when the Mac's active developer folder is the command line tools, as the runner does. It writes its report to .workspace/blazor-tests/device/probe.txt so the
# container session can read it through the shared workspace folder.

set -u

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
out_dir="$repo_root/.workspace/blazor-tests/device"
out_file="$out_dir/probe.txt"
mkdir -p "$out_dir"

report() { printf '%s: %s\n' "$1" "$2"; }
run() { local value; value="$("$@" 2>&1 | head -n 20)"; printf '%s' "${value:-<empty>}"; }
have() { command -v "$1" >/dev/null 2>&1; }
xcode_dir="${DEVELOPER_DIR:-/Applications/Xcode.app/Contents/Developer}"
xcode() { DEVELOPER_DIR="$xcode_dir" "$@"; }

{
  report "probed at" "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  report "repository commit" "$(git -C "$repo_root" rev-parse HEAD 2>/dev/null || echo unknown)"
  report "host" "$(uname -s) $(uname -m)"

  echo "== macOS and Safari"
  report "macOS" "$(run sw_vers -productVersion) ($(run sw_vers -buildVersion))"
  report "Safari" "$(run defaults read /Applications/Safari.app/Contents/Info.plist CFBundleShortVersionString)"
  report "Safari Technology Preview" "$([ -d '/Applications/Safari Technology Preview.app' ] && echo present || echo absent)"
  report "safaridriver" "$(have safaridriver && run safaridriver --version || echo absent)"
  report "Safari develop menu" "$(run defaults read com.apple.Safari IncludeDevelopMenu)"

  echo "== Node"
  report "node" "$(have node && run node --version || echo absent)"

  echo "== Xcode and the iOS Simulator"
  report "xcode-select (the Mac's active folder)" "$(run xcode-select -p)"
  report "Xcode folder the runner uses" "$xcode_dir ($([ -d "$xcode_dir" ] && echo present || echo absent))"
  report "xcodebuild" "$(run xcode xcodebuild -version | tr '\n' ' ')"
  echo "simctl runtimes:"
  if have xcrun; then xcode xcrun simctl list runtimes 2>&1 | sed 's/^/  /'; else echo "  xcrun absent"; fi
  echo "simctl available iPhone devices:"
  if have xcrun; then xcode xcrun simctl list devices available 2>&1 | grep -i iphone | sed 's/^/  /'; fi

  echo "== Android"
  sdk="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Library/Android/sdk}}"
  report "sdk folder" "$sdk ($([ -d "$sdk" ] && echo present || echo absent))"
  adb_bin="$(command -v adb || echo "$sdk/platform-tools/adb")"
  emulator_bin="$(command -v emulator || echo "$sdk/emulator/emulator")"
  report "adb" "$([ -x "$adb_bin" ] && run "$adb_bin" version | head -n 1 || echo absent)"
  report "emulator" "$([ -x "$emulator_bin" ] && run "$emulator_bin" -version | head -n 1 || echo absent)"
  echo "virtual devices:"
  if [ -x "$emulator_bin" ]; then "$emulator_bin" -list-avds 2>/dev/null | sed 's/^/  /'; fi
  echo "system images:"
  if [ -d "$sdk/system-images" ]; then (cd "$sdk/system-images" && find . -mindepth 3 -maxdepth 3 -type d | sed 's/^/  /'); fi
  report "chromedriver" "$(have chromedriver && run chromedriver --version || echo absent)"

  echo "== The forwarded stack"
  # The runner owns 9000 while it runs and reaches the stack through the editor's forward on 19000 (README.md)
  upstream="${DEVICE_PASS_UPSTREAM:-19000}"
  report "listener on 9000 (free before a run)" "$(run lsof -nP -iTCP:9000 -sTCP:LISTEN | awk 'NR>1 {print $1" pid "$2" "$9}' | sort -u | tr '\n' ' ')"
  report "listener on $upstream (the editor's forward)" "$(run lsof -nP -iTCP:"$upstream" -sTCP:LISTEN | awk 'NR>1 {print $1" pid "$2" "$9}' | sort -u | tr '\n' ' ')"
  through_forward=(--connect-to "app.dev.localhost:9000:127.0.0.1:$upstream")
  report "https://app.dev.localhost:9000/blazor/ status through the forward" "$(run curl -s "${through_forward[@]}" -o /dev/null -w '%{http_code}' --max-time 10 https://app.dev.localhost:9000/blazor/)"
  report "certificate verified by curl" "$(curl -s "${through_forward[@]}" -o /dev/null --max-time 10 https://app.dev.localhost:9000/blazor/ && echo yes || echo 'no (or unreachable)')"
  report "served worker cacheVersion" "$(curl -sk "${through_forward[@]}" --max-time 10 https://app.dev.localhost:9000/blazor/service-worker.js | grep -o 'cacheVersion[^,;]*' | head -n 1)"
} | tee "$out_file"

echo
echo "Report written to $out_file"
