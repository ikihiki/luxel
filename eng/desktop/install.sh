#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "This installer currently supports Linux only." >&2
    exit 1
fi

if ! command -v apt-get >/dev/null 2>&1; then
    echo "apt-get is required; install the desktop dependencies manually." >&2
    exit 1
fi

run=()
if [[ "${EUID}" -ne 0 ]]; then
    command -v sudo >/dev/null 2>&1 || { echo "root or sudo is required" >&2; exit 1; }
    run=(sudo)
fi

export DEBIAN_FRONTEND=noninteractive
"${run[@]}" apt-get update
"${run[@]}" apt-get install -y --no-install-recommends \
    xvfb openbox x11vnc novnc websockify \
    x11-utils x11-xserver-utils xauth xdotool scrot \
    dbus-x11 fonts-dejavu-core fonts-noto-cjk \
    clang zlib1g-dev binutils file

printf '%s\n' "Installed commands:"
for command_name in Xvfb openbox x11vnc websockify xdpyinfo xwininfo xdotool scrot; do
    printf '  %-12s %s\n' "${command_name}" "$(command -v "${command_name}" || echo missing)"
done

for webroot in /usr/share/novnc /usr/share/noVNC; do
    if [[ -f "${webroot}/vnc.html" ]]; then
        printf '  %-12s %s\n' "noVNC web" "${webroot}"
        exit 0
    fi
done

echo "noVNC was installed but vnc.html was not found in a known location." >&2
exit 1
