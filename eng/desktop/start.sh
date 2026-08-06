#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command flock
require_command setsid
require_command Xvfb
require_command openbox
require_command x11vnc
require_command websockify
require_command xdpyinfo
require_command curl

NOVNC_WEBROOT="$(find_novnc_webroot)" || fail "noVNC web root not found (run eng/desktop/install.sh)"

exec 9>"${LOCK_FILE}"
flock -n 9 || fail "another desktop start/stop operation is active"

cleanup_on_error() {
    local status=$?
    if [[ ${status} -ne 0 ]]; then
        log "startup failed; stopping processes started by this desktop stack"
        "${SCRIPT_DIR}/stop.sh" --no-lock || true
    fi
    exit "${status}"
}
trap cleanup_on_error EXIT

"${SCRIPT_DIR}/audio-start.sh"
write_environment_file

start_process xvfb Xvfb \
    Xvfb "${DESKTOP_DISPLAY}" -screen 0 "${DESKTOP_GEOMETRY}" -nolisten tcp -noreset
wait_until "X display ${DESKTOP_DISPLAY}" env DISPLAY="${DESKTOP_DISPLAY}" xdpyinfo

start_process openbox openbox \
    env DISPLAY="${DESKTOP_DISPLAY}" XDG_RUNTIME_DIR="${RUNTIME_DIR}" openbox

rm -f "${VNC_SOCKET}"
start_process x11vnc x11vnc \
    env DISPLAY="${DESKTOP_DISPLAY}" x11vnc \
        -display "${DESKTOP_DISPLAY}" \
        -unixsock "${VNC_SOCKET}" \
        -rfbport 0 -rfbportv6 0 -no6 -noipv6 \
        -nopw -forever -shared -noxdamage
wait_until "VNC Unix socket ${VNC_SOCKET}" test -S "${VNC_SOCKET}"
chmod 600 "${VNC_SOCKET}"

start_process novnc websockify \
    websockify --web="${NOVNC_WEBROOT}" --unix-target="${VNC_SOCKET}" \
        "${NOVNC_HOST}:${NOVNC_PORT}"
wait_until "noVNC HTTP endpoint" curl --fail --silent --show-error \
    "http://${NOVNC_HOST}:${NOVNC_PORT}/vnc.html"

trap - EXIT
log "desktop is ready"
log "source ${ENV_FILE} before launching Linux GUI applications"
log "noVNC: $(coder_preview_url)/vnc.html?autoconnect=1&resize=scale"
log "local: http://${NOVNC_HOST}:${NOVNC_PORT}/vnc.html?autoconnect=1&resize=scale"
