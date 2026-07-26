#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

if [[ "${1:-}" != "--no-lock" ]]; then
    exec 9>"${LOCK_FILE}"
    flock -w 10 9 || fail "could not acquire desktop operation lock"
fi

# Applications launched by developers are intentionally not killed here. The
# repository-owned Vulkan smoke is tracked and stopped before desktop services.
stop_process vkcube vkcube
stop_process novnc websockify
stop_process x11vnc x11vnc
rm -f "${VNC_SOCKET}"
stop_process openbox openbox
stop_process xvfb Xvfb

rm -f "${ENV_FILE}"
log "desktop is stopped"
