#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

if [[ "${1:-}" != "--no-lock" ]]; then
    exec 8>"${AUDIO_LOCK_FILE}"
    flock -w 10 8 || fail "could not acquire audio operation lock"
fi

output="$(cat "${STATE_DIR}/audio-capture.output" 2>/dev/null || true)"
stop_process audio-capture parec
rm -f "${STATE_DIR}/audio-capture.output"
if [[ -n "${output}" ]]; then
    log "audio capture stopped: ${output}"
fi
