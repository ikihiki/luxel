#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command flock
require_command setsid
require_command sway
require_command wayvnc
require_command websockify
require_command wlr-randr
require_command curl

NOVNC_WEBROOT="$(find_novnc_webroot)" || fail "noVNC web root not found (rebuild the Dev Container image)"

exec 9>"${LOCK_FILE}"
flock -n 9 || fail "another desktop start/stop operation is active"

if pid_is_running sway sway && pid_is_running wayvnc wayvnc && pid_is_running novnc websockify && \
    [[ -S "${RUNTIME_DIR}/${WAYLAND_DISPLAY_NAME}" && -S "${VNC_SOCKET}" ]] && \
    curl --fail --silent --show-error "http://${NOVNC_HOST}:${NOVNC_PORT}/vnc_lite.html" >/dev/null; then
    write_environment_file
    log "Wayland desktop is already ready"
    log "noVNC: $(coder_preview_url)/vnc_lite.html?path=websockify"
    exit 0
fi

cleanup_on_error() {
    local status=$?
    if [[ ${status} -ne 0 ]]; then
        log "startup failed; stopping processes started by this desktop stack"
        "${SCRIPT_DIR}/stop.sh" --no-lock || true
    fi
    exit "${status}"
}
trap cleanup_on_error EXIT

# A failed or interrupted prior start may have left only part of the managed stack alive.
# Stop tracked remnants before recreating sockets and compositor state.
"${SCRIPT_DIR}/stop.sh" --no-lock
"${SCRIPT_DIR}/audio-start.sh"
rm -f "${VNC_SOCKET}" "${WAYVNC_CONTROL_SOCKET}" "${RUNTIME_DIR}/${WAYLAND_DISPLAY_NAME}" \
    "${RUNTIME_DIR}/${WAYLAND_DISPLAY_NAME}.lock"

cat >"${SWAY_CONFIG}" <<EOF
xwayland disable
output ${WAYLAND_OUTPUT} mode ${DESKTOP_WIDTH}x${DESKTOP_HEIGHT}@${DESKTOP_REFRESH}Hz position 0 0 bg #20242b solid_color
seat seat0 fallback true
default_border pixel 1
focus_follows_mouse no
font monospace 10
EOF

sway_environment=(
    env -u DISPLAY -u WAYLAND_DISPLAY -u SWAYSOCK
    XDG_RUNTIME_DIR="${RUNTIME_DIR}"
    WLR_BACKENDS=headless
    WLR_LIBINPUT_NO_DEVICES=1
)
if [[ "${DESKTOP_RENDERER}" == "hardware" ]]; then
    [[ -c "${DRM_RENDER_NODE}" ]] || fail "hardware Wayland requires DRM render node ${DRM_RENDER_NODE}"
    sway_environment+=(WLR_RENDERER=vulkan WLR_RENDER_DRM_DEVICE="${DRM_RENDER_NODE}")
else
    sway_environment+=(WLR_RENDERER=pixman)
fi
start_process sway sway \
    "${sway_environment[@]}" sway --unsupported-gpu --config "${SWAY_CONFIG}" --debug
wait_until "Wayland socket ${WAYLAND_DISPLAY_NAME}" test -S "${RUNTIME_DIR}/${WAYLAND_DISPLAY_NAME}"
write_environment_file
wait_until "Wayland output ${WAYLAND_OUTPUT}" env XDG_RUNTIME_DIR="${RUNTIME_DIR}" \
    WAYLAND_DISPLAY="${WAYLAND_DISPLAY_NAME}" wlr-randr

start_process wayvnc wayvnc \
    env -u DISPLAY XDG_RUNTIME_DIR="${RUNTIME_DIR}" WAYLAND_DISPLAY="${WAYLAND_DISPLAY_NAME}" \
    wayvnc --unix-socket --output="${WAYLAND_OUTPUT}" --max-fps="${LUXEL_WAYVNC_MAX_FPS:-60}" \
        --render-cursor --socket="${WAYVNC_CONTROL_SOCKET}" "${VNC_SOCKET}"
wait_until "VNC Unix socket ${VNC_SOCKET}" test -S "${VNC_SOCKET}"
chmod 600 "${VNC_SOCKET}" "${WAYVNC_CONTROL_SOCKET}"

start_process novnc websockify \
    websockify --web="${NOVNC_WEBROOT}" --unix-target="${VNC_SOCKET}" \
        "${NOVNC_HOST}:${NOVNC_PORT}"
wait_until "noVNC HTTP endpoint" curl --fail --silent --show-error \
    "http://${NOVNC_HOST}:${NOVNC_PORT}/vnc_lite.html"

trap - EXIT
log "Wayland desktop is ready"
log "source ${ENV_FILE} before launching Linux GUI applications"
log "noVNC: $(coder_preview_url)/vnc_lite.html?path=websockify"
log "local: http://${NOVNC_HOST}:${NOVNC_PORT}/vnc_lite.html?path=websockify"
