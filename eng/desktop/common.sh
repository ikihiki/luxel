#!/usr/bin/env bash

set -o errexit
set -o nounset
set -o pipefail

HOST_XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-}"
HOST_PULSE_SERVER="${PULSE_SERVER:-}"
DESKTOP_GEOMETRY="${LUXEL_DESKTOP_GEOMETRY:-1280x900}"
DESKTOP_GEOMETRY="${DESKTOP_GEOMETRY%x24}"
DESKTOP_WIDTH="${DESKTOP_GEOMETRY%x*}"
DESKTOP_HEIGHT="${DESKTOP_GEOMETRY#*x}"
DESKTOP_REFRESH="${LUXEL_DESKTOP_REFRESH:-60}"
WAYLAND_OUTPUT="${LUXEL_WAYLAND_OUTPUT:-HEADLESS-1}"
WAYLAND_DISPLAY_NAME="${LUXEL_WAYLAND_DISPLAY:-wayland-1}"
STATE_DIR="${LUXEL_DESKTOP_STATE_DIR:-${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}}"
VNC_SOCKET="${LUXEL_VNC_SOCKET:-${STATE_DIR}/vnc.sock}"
WAYVNC_CONTROL_SOCKET="${STATE_DIR}/wayvnc-control.sock"
NOVNC_HOST="${LUXEL_NOVNC_HOST:-127.0.0.1}"
NOVNC_PORT="${LUXEL_NOVNC_PORT:-6080}"
PID_DIR="${STATE_DIR}/pids"
LOG_DIR="${STATE_DIR}/logs"
SCREENSHOT_DIR="${STATE_DIR}/screenshots"
RUNTIME_DIR="${STATE_DIR}/runtime"
ENV_FILE="${STATE_DIR}/environment"
LOCK_FILE="${STATE_DIR}/desktop.lock"
AUDIO_LOCK_FILE="${STATE_DIR}/audio.lock"
SWAY_CONFIG="${STATE_DIR}/sway.conf"
DESKTOP_RENDERER_REQUESTED="${LUXEL_DESKTOP_RENDERER:-auto}"
DRM_RENDER_NODE="${LUXEL_DRM_RENDER_NODE:-/dev/dri/renderD128}"
case "${DESKTOP_RENDERER_REQUESTED}" in
    auto)
        if [[ -c "${DRM_RENDER_NODE}" ]]; then
            DESKTOP_RENDERER=hardware
        else
            DESKTOP_RENDERER=lavapipe
        fi
        ;;
    hardware|lavapipe)
        DESKTOP_RENDERER="${DESKTOP_RENDERER_REQUESTED}"
        ;;
    *)
        printf '[luxel-desktop] error: invalid LUXEL_DESKTOP_RENDERER %q (expected auto, hardware, or lavapipe)\n' "${DESKTOP_RENDERER_REQUESTED}" >&2
        exit 1
        ;;
esac
REQUIRE_HARDWARE_VULKAN="${LUXEL_REQUIRE_HARDWARE_VULKAN:-0}"
if [[ "${DESKTOP_RENDERER}" == "lavapipe" ]]; then
    REQUIRE_HARDWARE_VULKAN=0
fi
export LUXEL_REQUIRE_HARDWARE_VULKAN="${REQUIRE_HARDWARE_VULKAN}"
LAVAPIPE_ICD="${LUXEL_LAVAPIPE_ICD:-/usr/share/vulkan/icd.d/lvp_icd.json}"
AUDIO_MODE="${LUXEL_DESKTOP_AUDIO:-off}"
AUDIO_MODE_FILE="${STATE_DIR}/audio.mode"
AUDIO_MODULE_FILE="${STATE_DIR}/audio.module"
AUDIO_SERVER_FILE="${STATE_DIR}/audio.server"
AUDIO_SINK_FILE="${STATE_DIR}/audio.sink"
AUDIO_SERVER_SOCKET="${LUXEL_PULSE_SERVER_SOCKET:-${RUNTIME_DIR}/pulse/native}"
AUDIO_SINK="${LUXEL_AUDIO_SINK:-luxel_null}"
AUDIO_RATE=48000
AUDIO_CHANNELS=2
CAPTURE_DIR="${STATE_DIR}/captures"

if [[ ! "${DESKTOP_WIDTH}" =~ ^[1-9][0-9]*$ || ! "${DESKTOP_HEIGHT}" =~ ^[1-9][0-9]*$ ]]; then
    printf '[luxel-desktop] error: invalid LUXEL_DESKTOP_GEOMETRY %q (expected WIDTHxHEIGHT)\n' "${DESKTOP_GEOMETRY}" >&2
    exit 1
fi

case "${AUDIO_MODE}" in
    off|null|system) ;;
    *) printf '[luxel-desktop] error: invalid LUXEL_DESKTOP_AUDIO %q (expected off, null, or system)\n' "${AUDIO_MODE}" >&2; exit 1 ;;
esac

case "${AUDIO_MODE}" in
    null)
        AUDIO_PULSE_SERVER="unix:${AUDIO_SERVER_SOCKET}"
        ;;
    system)
        if [[ -n "${LUXEL_SYSTEM_PULSE_SERVER:-}" ]]; then
            AUDIO_PULSE_SERVER="${LUXEL_SYSTEM_PULSE_SERVER}"
        elif [[ -n "${HOST_PULSE_SERVER}" ]]; then
            AUDIO_PULSE_SERVER="${HOST_PULSE_SERVER}"
        elif [[ -n "${HOST_XDG_RUNTIME_DIR}" ]]; then
            AUDIO_PULSE_SERVER="unix:${HOST_XDG_RUNTIME_DIR}/pulse/native"
        else
            AUDIO_PULSE_SERVER="unix:/run/user/${UID}/pulse/native"
        fi
        ;;
    *)
        AUDIO_PULSE_SERVER=""
        ;;
esac

unset DISPLAY
export XDG_RUNTIME_DIR="${RUNTIME_DIR}"
export WAYLAND_DISPLAY="${WAYLAND_DISPLAY_NAME}"
if [[ "${DESKTOP_RENDERER}" == "lavapipe" && -f "${LAVAPIPE_ICD}" ]]; then
    export VK_ICD_FILENAMES="${VK_ICD_FILENAMES:-${LAVAPIPE_ICD}}"
fi

mkdir -p "${PID_DIR}" "${LOG_DIR}" "${SCREENSHOT_DIR}" "${RUNTIME_DIR}" "${CAPTURE_DIR}"
chmod 700 "${STATE_DIR}" "${PID_DIR}" "${LOG_DIR}" "${SCREENSHOT_DIR}" "${RUNTIME_DIR}" "${CAPTURE_DIR}"

log() {
    printf '[luxel-desktop] %s\n' "$*"
}

fail() {
    printf '[luxel-desktop] error: %s\n' "$*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1 (rebuild the Dev Container image)"
}

pid_file() {
    printf '%s/%s.pid\n' "${PID_DIR}" "$1"
}

read_pid() {
    local file
    file="$(pid_file "$1")"
    [[ -f "${file}" ]] && cat "${file}"
}

pid_is_running() {
    local name="$1"
    local expected="${2:-}"
    local pid
    pid="$(read_pid "${name}" || true)"
    [[ -n "${pid}" && -d "/proc/${pid}" ]] || return 1
    if [[ -n "${expected}" ]]; then
        tr '\0' ' ' < "/proc/${pid}/cmdline" | grep -F -- "${expected}" >/dev/null
    fi
}

start_process() {
    local name="$1"
    local expected="$2"
    shift 2

    if pid_is_running "${name}" "${expected}"; then
        log "${name} already running (pid $(read_pid "${name}"))"
        return
    fi

    rm -f "$(pid_file "${name}")"
    setsid "$@" >>"${LOG_DIR}/${name}.log" 2>&1 < /dev/null 8>&- 9>&- &
    local pid=$!
    printf '%s\n' "${pid}" > "$(pid_file "${name}")"
    sleep 0.2
    if ! pid_is_running "${name}" "${expected}"; then
        tail -n 40 "${LOG_DIR}/${name}.log" >&2 || true
        fail "${name} failed to start"
    fi
    log "started ${name} (pid ${pid})"
}

stop_process() {
    local name="$1"
    local expected="$2"
    local pid
    pid="$(read_pid "${name}" || true)"
    if [[ -z "${pid}" ]]; then
        return
    fi
    if [[ ! -d "/proc/${pid}" ]]; then
        rm -f "$(pid_file "${name}")"
        return
    fi
    if ! tr '\0' ' ' < "/proc/${pid}/cmdline" | grep -F -- "${expected}" >/dev/null; then
        log "refusing to stop ${name}: pid ${pid} command does not contain '${expected}'"
        return 1
    fi

    kill -TERM -- "-${pid}" 2>/dev/null || kill -TERM "${pid}" 2>/dev/null || true
    for _ in $(seq 1 50); do
        [[ ! -d "/proc/${pid}" ]] && break
        sleep 0.1
    done
    if [[ -d "/proc/${pid}" ]]; then
        log "${name} did not stop after 5 seconds; sending KILL"
        kill -KILL -- "-${pid}" 2>/dev/null || kill -KILL "${pid}" 2>/dev/null || true
    fi
    rm -f "$(pid_file "${name}")"
    log "stopped ${name}"
}

wait_until() {
    local description="$1"
    shift
    for _ in $(seq 1 100); do
        if "$@" >/dev/null 2>&1; then
            return 0
        fi
        sleep 0.1
    done
    fail "timed out waiting for ${description}"
}

find_novnc_webroot() {
    local candidate
    for candidate in \
        "${LUXEL_NOVNC_WEBROOT:-}" \
        /usr/share/novnc \
        /usr/share/noVNC \
        /opt/novnc; do
        if [[ -n "${candidate}" && -f "${candidate}/vnc.html" ]]; then
            printf '%s\n' "${candidate}"
            return 0
        fi
    done
    return 1
}

coder_preview_url() {
    local template="${VSCODE_PROXY_URI:-}"
    if [[ -n "${template}" && "${template}" == *'{{port}}'* ]]; then
        printf '%s\n' "${template//\{\{port\}\}/${NOVNC_PORT}}"
        return
    fi
    printf 'http://%s:%s\n' "${NOVNC_HOST}" "${NOVNC_PORT}"
}

write_environment_file() {
    cat > "${ENV_FILE}" <<EOF
unset DISPLAY
export XDG_RUNTIME_DIR='${RUNTIME_DIR}'
export WAYLAND_DISPLAY='${WAYLAND_DISPLAY_NAME}'
export LUXEL_DESKTOP_STATE_DIR='${STATE_DIR}'
export LUXEL_VNC_SOCKET='${VNC_SOCKET}'
export LUXEL_NOVNC_PORT='${NOVNC_PORT}'
export LUXEL_DESKTOP_RENDERER='${DESKTOP_RENDERER}'
export LUXEL_REQUIRE_HARDWARE_VULKAN='${REQUIRE_HARDWARE_VULKAN}'
export LUXEL_WAYLAND_OUTPUT='${WAYLAND_OUTPUT}'
export LUXEL_DESKTOP_AUDIO='${AUDIO_MODE}'
export LUXEL_AUDIO_SINK='${AUDIO_SINK}'
export LUXEL_AUDIO_RATE='${AUDIO_RATE}'
export LUXEL_AUDIO_CHANNELS='${AUDIO_CHANNELS}'
unset PULSE_SERVER PULSE_SINK ALSOFT_DRIVERS
${VK_ICD_FILENAMES:+export VK_ICD_FILENAMES='${VK_ICD_FILENAMES}'}
EOF
    if [[ "${AUDIO_MODE}" != "off" ]]; then
        printf "export PULSE_SERVER='%s'\n" "${AUDIO_PULSE_SERVER}" >> "${ENV_FILE}"
        printf "export ALSOFT_DRIVERS='pulse'\n" >> "${ENV_FILE}"
    fi
    if [[ "${AUDIO_MODE}" == "null" ]]; then
        printf "export PULSE_SINK='%s'\n" "${AUDIO_SINK}" >> "${ENV_FILE}"
    fi
    chmod 600 "${ENV_FILE}"
}

audio_pactl() {
    local server
    server="$(cat "${AUDIO_SERVER_FILE}" 2>/dev/null || true)"
    [[ -n "${server}" ]] || server="${AUDIO_PULSE_SERVER}"
    [[ -n "${server}" ]] || return 1
    env PULSE_SERVER="${server}" pactl "$@"
}

audio_server_ready() {
    audio_pactl info >/dev/null 2>&1
}

audio_sink_ready() {
    local sink="${1:-${AUDIO_SINK}}"
    audio_pactl list short sinks | awk -v sink="${sink}" '$2 == sink { found=1 } END { exit !found }'
}

openal_library_ready() {
    ldconfig -p 2>/dev/null | grep -F 'libopenal.so.1' >/dev/null || \
        find /usr/lib /lib -name 'libopenal.so.1' -print -quit 2>/dev/null | grep -q .
}

hardware_vulkan_device_index() {
    local summary_file="$1"
    local preferred_vendor="${LUXEL_VULKAN_VENDOR_ID:-}"
    awk -F= -v preferred_vendor="${preferred_vendor,,}" '
        /^GPU[0-9]+:/ {
            gpu = $0
            sub(/^GPU/, "", gpu)
            sub(/:.*/, "", gpu)
            type = ""
            vendor = ""
        }
        /^[[:space:]]*vendorID[[:space:]]*=/ {
            vendor = tolower($2)
            gsub(/[[:space:]]/, "", vendor)
        }
        /^[[:space:]]*deviceType[[:space:]]*=/ {
            type = $2
        }
        /^[[:space:]]*deviceName[[:space:]]*=/ {
            name = tolower($2)
            hardware = type !~ /PHYSICAL_DEVICE_TYPE_CPU/ && name !~ /(lavapipe|llvmpipe|swiftshader)/
            vendor_matches = preferred_vendor == "" || vendor == preferred_vendor
            if (hardware && vendor_matches) {
                print gpu
                exit
            }
        }
    ' "${summary_file}"
}

vulkan_device_name() {
    local summary_file="$1"
    local target_index="$2"
    awk -F= -v target="${target_index}" '
        /^GPU[0-9]+:/ {
            gpu = $0
            sub(/^GPU/, "", gpu)
            sub(/:.*/, "", gpu)
        }
        gpu == target && /^[[:space:]]*deviceName[[:space:]]*=/ {
            name = $2
            sub(/^[[:space:]]*/, "", name)
            print name
            exit
        }
    ' "${summary_file}"
}
