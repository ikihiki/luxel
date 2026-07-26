#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command scrot

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
output="${1:-${SCREENSHOT_DIR}/desktop-${timestamp}.png}"
mkdir -p "$(dirname -- "${output}")"
env DISPLAY="${DESKTOP_DISPLAY}" scrot "${output}"
printf '%s\n' "${output}"

if [[ -n "${LUXEL_DEBUG_SERVER_URL:-}" && -n "${LUXEL_WINDOW_ID:-}" ]]; then
    frame_output="${SCREENSHOT_DIR}/luxel-window-${LUXEL_WINDOW_ID}-${timestamp}.png"
    curl --fail --silent --show-error \
        "${LUXEL_DEBUG_SERVER_URL%/}/winframe?id=${LUXEL_WINDOW_ID}&format=png" \
        --output "${frame_output}"
    printf '%s\n' "${frame_output}"
fi
