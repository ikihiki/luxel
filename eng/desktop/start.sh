#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command flock
require_command setsid
require_command openbox
require_command x11vnc
require_command websockify
require_command xdpyinfo
require_command curl
if [[ "${DESKTOP_SERVER}" == "xorg" ]]; then
    require_command Xorg
    require_command xrandr
    require_command gtf
else
    require_command Xvfb
fi

NOVNC_WEBROOT="$(find_novnc_webroot)" || fail "noVNC web root not found (rebuild the Dev Container image)"

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

if [[ "${DESKTOP_SERVER}" == "xorg" ]]; then
    drm_device="$(readlink -f /sys/class/drm/card0/device 2>/dev/null || true)"
    [[ -n "${drm_device}" ]] || fail "hardware Xorg requires /sys/class/drm/card0/device"
    pci_address="$(basename "${drm_device}")"
    IFS=':.' read -r _ pci_bus pci_device pci_function <<< "${pci_address}"
    [[ -n "${pci_function:-}" ]] || fail "could not parse GPU PCI address ${pci_address}"
    xorg_config="${STATE_DIR}/xorg.conf"
    cat >"${xorg_config}" <<EOF
Section "ServerFlags"
    Option "AutoAddDevices" "false"
    Option "AllowEmptyInput" "true"
EndSection
Section "Device"
    Identifier "LuxelGPU"
    Driver "intel"
    BusID "PCI:$((16#${pci_bus})):$((16#${pci_device})):$((16#${pci_function}))"
    Option "VirtualHeads" "1"
    Option "DRI" "3"
EndSection
Section "Screen"
    Identifier "Screen0"
    Device "LuxelGPU"
    DefaultDepth 24
EndSection
EOF
    start_process xorg Xorg \
        Xorg "${DESKTOP_DISPLAY}" -config "${xorg_config}" -noreset -nolisten tcp -ac \
            -logfile "${LOG_DIR}/Xorg.log"
    wait_until "X display ${DESKTOP_DISPLAY}" env DISPLAY="${DESKTOP_DISPLAY}" xdpyinfo

    geometry="${DESKTOP_GEOMETRY%x*}"
    width="${geometry%x*}"
    height="${geometry#*x}"
    modeline="$(gtf "${width}" "${height}" 60 | awk '/Modeline/ { sub(/^[[:space:]]*Modeline[[:space:]]+/, ""); print; exit }')"
    mode_name="$(printf '%s\n' "${modeline}" | awk -F'"' '{ print $2 }')"
    mode_values="$(printf '%s\n' "${modeline}" | sed 's/^[[:space:]]*"[^"]*"[[:space:]]*//')"
    read -r -a mode_arguments <<< "${mode_values}"
    env DISPLAY="${DESKTOP_DISPLAY}" xrandr --newmode "${mode_name}" "${mode_arguments[@]}" 2>/dev/null || true
    env DISPLAY="${DESKTOP_DISPLAY}" xrandr --addmode VIRTUAL1 "${mode_name}" 2>/dev/null || true
    env DISPLAY="${DESKTOP_DISPLAY}" xrandr --output VIRTUAL1 --mode "${mode_name}" --primary
else
    start_process xvfb Xvfb \
        Xvfb "${DESKTOP_DISPLAY}" -screen 0 "${DESKTOP_GEOMETRY}" -nolisten tcp -noreset
    wait_until "X display ${DESKTOP_DISPLAY}" env DISPLAY="${DESKTOP_DISPLAY}" xdpyinfo
fi

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
