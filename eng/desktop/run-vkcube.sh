#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command vkcube-wayland

gpu_arguments=()
if [[ "${LUXEL_REQUIRE_HARDWARE_VULKAN:-0}" == "1" ]]; then
    gpu_index="$(cat "${STATE_DIR}/vulkan-gpu.index" 2>/dev/null || true)"
    if [[ -z "${gpu_index}" ]]; then
        require_command vulkaninfo
        env -u DISPLAY XDG_RUNTIME_DIR="${RUNTIME_DIR}" WAYLAND_DISPLAY="${WAYLAND_DISPLAY_NAME}" \
            vulkaninfo --summary >"${LOG_DIR}/vulkaninfo.log" 2>&1
        gpu_index="$(hardware_vulkan_device_index "${LOG_DIR}/vulkaninfo.log")"
    fi
    [[ -n "${gpu_index}" ]] || fail "no matching hardware Vulkan device is available"
    gpu_arguments=(--gpu_number "${gpu_index}")
fi

if pid_is_running vkcube vkcube-wayland; then
    log "vkcube-wayland already running (pid $(read_pid vkcube))"
    exit 0
fi

cube_environment=(env -u DISPLAY XDG_RUNTIME_DIR="${RUNTIME_DIR}" WAYLAND_DISPLAY="${WAYLAND_DISPLAY_NAME}")
if [[ -n "${1:-}" ]]; then
    frames="$1"
    log "running vkcube-wayland for ${frames} frames"
    "${cube_environment[@]}" vkcube-wayland "${gpu_arguments[@]}" --present_mode 2 --c "${frames}"
    log "vkcube-wayland completed ${frames} frames"
    exit 0
fi

start_process vkcube vkcube-wayland \
    "${cube_environment[@]}" vkcube-wayland "${gpu_arguments[@]}" --present_mode 2
log "vkcube-wayland is visible at $(coder_preview_url)/vnc_lite.html?path=websockify"
