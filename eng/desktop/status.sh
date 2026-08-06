#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

printf 'State directory: %s\n' "${STATE_DIR}"
printf 'DISPLAY:         %s\n' "${DESKTOP_DISPLAY}"
printf 'Geometry:        %s\n' "${DESKTOP_GEOMETRY}"
printf 'noVNC URL:       %s/vnc.html?autoconnect=1&resize=scale\n' "$(coder_preview_url)"
printf 'Vulkan ICD:      %s\n' "${VK_ICD_FILENAMES:-auto (host GPU)}"
printf '\n%-10s %-8s %s\n' SERVICE STATUS PID

failed=0
for spec in "xvfb:Xvfb" "openbox:openbox" "x11vnc:x11vnc" "novnc:websockify"; do
    name="${spec%%:*}"
    expected="${spec#*:}"
    if pid_is_running "${name}" "${expected}"; then
        printf '%-10s %-8s %s\n' "${name}" running "$(read_pid "${name}")"
    else
        printf '%-10s %-8s %s\n' "${name}" stopped '-'
        failed=1
    fi
done

audio_mode="$(cat "${AUDIO_MODE_FILE}" 2>/dev/null || echo off)"
printf '\nAudio mode:      %s\n' "${audio_mode}"
if [[ "${audio_mode}" != "off" ]]; then
    if audio_server_ready; then
        printf 'Audio server:    ready\n'
    else
        printf 'Audio server:    unavailable\n'
        failed=1
    fi
    if [[ "${audio_mode}" == "null" ]]; then
        active_sink="$(cat "${AUDIO_SINK_FILE}" 2>/dev/null || printf '%s' "${AUDIO_SINK}")"
        if audio_sink_ready "${active_sink}"; then
            printf 'Null sink:       %s (ready)\n' "${active_sink}"
        else
            printf 'Null sink:       %s (missing)\n' "${active_sink}"
            failed=1
        fi
    fi
fi
if pid_is_running audio-capture parec; then
    printf 'Audio capture:   running (pid %s)\n' "$(read_pid audio-capture)"
fi

printf '\nVNC socket: %s (%s)\n' "${VNC_SOCKET}" "$([[ -S "${VNC_SOCKET}" ]] && echo ready || echo missing)"
printf 'Listeners:\n'
ss -ltn 2>/dev/null | awk -v novnc=":${NOVNC_PORT}" 'NR == 1 || index($4, novnc)'

if [[ -d "${LOG_DIR}" ]]; then
    printf '\nLogs: %s\n' "${LOG_DIR}"
fi

exit "${failed}"
