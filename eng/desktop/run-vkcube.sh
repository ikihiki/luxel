#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command vkcube

if pid_is_running vkcube vkcube; then
    log "vkcube already running (pid $(read_pid vkcube))"
    exit 0
fi

if [[ -n "${1:-}" ]]; then
    frames="$1"
    log "running vkcube for ${frames} frames"
    env DISPLAY="${DESKTOP_DISPLAY}" XDG_RUNTIME_DIR="${RUNTIME_DIR}" \
        vkcube --c "${frames}"
    log "vkcube completed ${frames} frames"
    exit 0
fi

start_process vkcube vkcube \
    env DISPLAY="${DESKTOP_DISPLAY}" XDG_RUNTIME_DIR="${RUNTIME_DIR}" vkcube
log "vkcube is visible at $(coder_preview_url)/vnc.html?autoconnect=1&resize=scale"
