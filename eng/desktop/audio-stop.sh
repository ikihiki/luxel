#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

if [[ "${1:-}" != "--no-lock" ]]; then
    exec 8>"${AUDIO_LOCK_FILE}"
    flock -w 10 8 || fail "could not acquire audio operation lock"
fi

"${SCRIPT_DIR}/capture-audio-stop.sh" --no-lock || true

mode="$(cat "${AUDIO_MODE_FILE}" 2>/dev/null || echo off)"
if [[ "${mode}" == "null" && -f "${AUDIO_MODULE_FILE}" ]]; then
    module_id="$(cat "${AUDIO_MODULE_FILE}")"
    if [[ "${module_id}" =~ ^[0-9]+$ ]] && audio_server_ready; then
        audio_pactl unload-module "${module_id}" >/dev/null 2>&1 || \
            log "null sink module ${module_id} was already absent"
    fi
fi
rm -f "${AUDIO_MODULE_FILE}"
stop_process pulseaudio pulseaudio || true
rm -f "${AUDIO_SERVER_FILE}" "${AUDIO_SINK_FILE}" "${AUDIO_MODE_FILE}" "${AUDIO_SERVER_SOCKET}"
AUDIO_MODE=off
AUDIO_PULSE_SERVER=""
write_environment_file
log "audio is stopped"
