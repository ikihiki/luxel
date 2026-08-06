#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command parec
mode="$(cat "${AUDIO_MODE_FILE}" 2>/dev/null || echo off)"
[[ "${mode}" != "off" ]] || fail "audio is off; start with LUXEL_DESKTOP_AUDIO=null or system"
audio_server_ready || fail "PulseAudio-compatible server is unavailable"

output="${1:-${CAPTURE_DIR}/audio.wav}"
if pid_is_running audio-capture parec; then
    fail "audio capture is already running (pid $(read_pid audio-capture))"
fi
mkdir -p "$(dirname -- "${output}")"
rm -f "${output}"
printf '%s\n' "${output}" > "${STATE_DIR}/audio-capture.output"

device="${LUXEL_AUDIO_CAPTURE_DEVICE:-}"
if [[ -z "${device}" ]]; then
    if [[ "${mode}" == "null" ]]; then
        sink="$(cat "${AUDIO_SINK_FILE}" 2>/dev/null || printf '%s' "${AUDIO_SINK}")"
        device="${sink}.monitor"
    else
        device="@DEFAULT_MONITOR@"
    fi
fi
server="$(cat "${AUDIO_SERVER_FILE}")"
start_process audio-capture parec \
    env PULSE_SERVER="${server}" parec \
        --device="${device}" --file-format=wav --format=s16le \
        --rate="${AUDIO_RATE}" --channels="${AUDIO_CHANNELS}" "${output}"
log "capturing ${device} to ${output}"
