#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

check() {
    local label="$1"
    shift
    printf '%-24s' "${label}"
    if "$@" >/dev/null 2>&1; then
        printf 'ok\n'
    else
        printf 'FAILED\n'
        return 1
    fi
}

failures=0
check "X display" env DISPLAY="${DESKTOP_DISPLAY}" xdpyinfo || failures=$((failures + 1))
check "Window manager/root" env DISPLAY="${DESKTOP_DISPLAY}" xwininfo -root -tree || failures=$((failures + 1))
check "VNC Unix socket" test -S "${VNC_SOCKET}" || failures=$((failures + 1))
check "noVNC HTTP" curl --fail --silent --show-error "http://${NOVNC_HOST}:${NOVNC_PORT}/vnc.html" || failures=$((failures + 1))

if command -v vulkaninfo >/dev/null 2>&1; then
    printf '%-24s' "Vulkan device"
    if env DISPLAY="${DESKTOP_DISPLAY}" XDG_RUNTIME_DIR="${RUNTIME_DIR}" \
        vulkaninfo --summary >"${LOG_DIR}/vulkaninfo.log" 2>&1; then
        device="$(grep -m1 'deviceName' "${LOG_DIR}/vulkaninfo.log" | sed 's/^[[:space:]]*//' || true)"
        printf 'ok%s\n' "${device:+ (${device})}"
    else
        printf 'FAILED (see %s)\n' "${LOG_DIR}/vulkaninfo.log"
        failures=$((failures + 1))
    fi
else
    printf '%-24s%s\n' "Vulkan device" "skipped (vulkaninfo missing)"
fi

if [[ -n "${LUXEL_DEBUG_SERVER_URL:-}" ]]; then
    check "Luxel DebugServer" curl --fail --silent --show-error \
        "${LUXEL_DEBUG_SERVER_URL%/}/windows" || failures=$((failures + 1))
fi

if [[ ${failures} -ne 0 ]]; then
    printf '%s\n' "${failures} health check(s) failed" >&2
    exit 1
fi

printf 'noVNC: %s/vnc.html?autoconnect=1&resize=scale\n' "$(coder_preview_url)"
