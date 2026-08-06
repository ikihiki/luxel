#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

AUDIO_ONLY=false
if [[ "${1:-}" == "--audio-only" ]]; then
    AUDIO_ONLY=true
elif [[ $# -ne 0 ]]; then
    fail "usage: healthcheck.sh [--audio-only]"
fi

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
if [[ "${AUDIO_ONLY}" == false ]]; then
    check "X display" env DISPLAY="${DESKTOP_DISPLAY}" xdpyinfo || failures=$((failures + 1))
    check "Window manager/root" env DISPLAY="${DESKTOP_DISPLAY}" xwininfo -root -tree || failures=$((failures + 1))
    check "VNC Unix socket" test -S "${VNC_SOCKET}" || failures=$((failures + 1))
    check "noVNC HTTP" curl --fail --silent --show-error "http://${NOVNC_HOST}:${NOVNC_PORT}/vnc.html" || failures=$((failures + 1))
fi

audio_mode="$(cat "${AUDIO_MODE_FILE}" 2>/dev/null || echo off)"
if [[ "${audio_mode}" != "off" ]]; then
    check "Audio server" audio_server_ready || failures=$((failures + 1))
    if [[ "${audio_mode}" == "null" ]]; then
        active_sink="$(cat "${AUDIO_SINK_FILE}" 2>/dev/null || printf '%s' "${AUDIO_SINK}")"
        check "Audio null sink" audio_sink_ready "${active_sink}" || failures=$((failures + 1))
    fi
    check "OpenAL Soft library" openal_library_ready || failures=$((failures + 1))
else
    printf '%-24s%s\n' "Audio" "skipped (mode off)"
fi

if [[ "${AUDIO_ONLY}" == false ]]; then
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
fi

if [[ ${failures} -ne 0 ]]; then
    printf '%s\n' "${failures} health check(s) failed" >&2
    exit 1
fi

if [[ "${AUDIO_ONLY}" == false ]]; then
    printf 'noVNC: %s/vnc.html?autoconnect=1&resize=scale\n' "$(coder_preview_url)"
fi
