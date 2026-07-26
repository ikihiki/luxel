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

printf '\nVNC socket: %s (%s)\n' "${VNC_SOCKET}" "$([[ -S "${VNC_SOCKET}" ]] && echo ready || echo missing)"
printf 'Listeners:\n'
ss -ltn 2>/dev/null | awk -v novnc=":${NOVNC_PORT}" 'NR == 1 || index($4, novnc)'

if [[ -d "${LOG_DIR}" ]]; then
    printf '\nLogs: %s\n' "${LOG_DIR}"
fi

exit "${failed}"
