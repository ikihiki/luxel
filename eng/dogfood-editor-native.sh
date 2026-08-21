#!/usr/bin/env bash
set -euo pipefail

project="${1:-editor/EditorNative/Luxel.Editor.Native.csproj}"
screenshot="${2:-/tmp/luxel-editor-native.png}"
log="${LUXEL_EDITOR_NATIVE_LOG:-/tmp/luxel-editor-native.log}"

: "${XDG_RUNTIME_DIR:?Source the Luxel desktop environment before running this script.}"
: "${WAYLAND_DISPLAY:?Source the Luxel desktop environment before running this script.}"
if [[ -z "${SWAYSOCK:-}" ]]; then
  SWAYSOCK="$(find "$XDG_RUNTIME_DIR" -maxdepth 1 -type s -name 'sway-ipc.*.sock' -print -quit)"
  export SWAYSOCK
fi
[[ -n "${SWAYSOCK:-}" && -S "$SWAYSOCK" ]] || { echo "Sway IPC socket was not found." >&2; exit 1; }

rm -f "$log" "$screenshot"
dotnet run --project "$project" --configuration Release --no-restore >"$log" 2>&1 &
editor_pid=$!
cleanup() {
  kill "$editor_pid" 2>/dev/null || true
  wait "$editor_pid" 2>/dev/null || true
}
trap cleanup EXIT

for _ in $(seq 1 160); do
  if ! kill -0 "$editor_pid" 2>/dev/null; then
    tail -100 "$log" >&2
    exit 1
  fi
  if swaymsg -t get_tree -r | python3 -c '
import json, sys
queue = [json.load(sys.stdin)]
while queue:
    node = queue.pop()
    if node.get("name") == "Luxel Editor":
        raise SystemExit(0)
    queue += node.get("nodes", []) + node.get("floating_nodes", [])
raise SystemExit(1)
'; then
    sleep 2
    grim "$screenshot"
    test -s "$screenshot"
    echo "Native Editor window is healthy: $screenshot"
    exit 0
  fi
  sleep 0.25
done

tail -100 "$log" >&2
echo "Luxel Editor window did not appear before the timeout." >&2
exit 1
