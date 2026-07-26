#!/usr/bin/env bash

set -o errexit
set -o nounset
set -o pipefail

DESKTOP_DISPLAY="${LUXEL_DESKTOP_DISPLAY:-:99}"
DESKTOP_NUMBER="${DESKTOP_DISPLAY#:}"
DESKTOP_GEOMETRY="${LUXEL_DESKTOP_GEOMETRY:-1280x900x24}"
STATE_DIR="${LUXEL_DESKTOP_STATE_DIR:-${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}}"
VNC_SOCKET="${LUXEL_VNC_SOCKET:-${STATE_DIR}/vnc.sock}"
NOVNC_HOST="${LUXEL_NOVNC_HOST:-127.0.0.1}"
NOVNC_PORT="${LUXEL_NOVNC_PORT:-6080}"
PID_DIR="${STATE_DIR}/pids"
LOG_DIR="${STATE_DIR}/logs"
SCREENSHOT_DIR="${STATE_DIR}/screenshots"
RUNTIME_DIR="${STATE_DIR}/runtime"
ENV_FILE="${STATE_DIR}/environment"
LOCK_FILE="${STATE_DIR}/desktop.lock"
DESKTOP_RENDERER="${LUXEL_DESKTOP_RENDERER:-lavapipe}"
LAVAPIPE_ICD="${LUXEL_LAVAPIPE_ICD:-/usr/share/vulkan/icd.d/lvp_icd.json}"

export DISPLAY="${DESKTOP_DISPLAY}"
export XDG_RUNTIME_DIR="${RUNTIME_DIR}"
if [[ "${DESKTOP_RENDERER}" == "lavapipe" && -f "${LAVAPIPE_ICD}" ]]; then
    export VK_ICD_FILENAMES="${VK_ICD_FILENAMES:-${LAVAPIPE_ICD}}"
fi

mkdir -p "${PID_DIR}" "${LOG_DIR}" "${SCREENSHOT_DIR}" "${RUNTIME_DIR}"
chmod 700 "${STATE_DIR}" "${PID_DIR}" "${LOG_DIR}" "${SCREENSHOT_DIR}" "${RUNTIME_DIR}"

log() {
    printf '[luxel-desktop] %s\n' "$*"
}

fail() {
    printf '[luxel-desktop] error: %s\n' "$*" >&2
    exit 1
}

require_command() {
    command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1 (run eng/desktop/install.sh)"
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
    setsid "$@" >>"${LOG_DIR}/${name}.log" 2>&1 < /dev/null 9>&- &
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
export DISPLAY='${DESKTOP_DISPLAY}'
export XDG_RUNTIME_DIR='${RUNTIME_DIR}'
export LUXEL_DESKTOP_STATE_DIR='${STATE_DIR}'
export LUXEL_VNC_SOCKET='${VNC_SOCKET}'
export LUXEL_NOVNC_PORT='${NOVNC_PORT}'
export LUXEL_DESKTOP_RENDERER='${DESKTOP_RENDERER}'
${VK_ICD_FILENAMES:+export VK_ICD_FILENAMES='${VK_ICD_FILENAMES}'}
EOF
    chmod 600 "${ENV_FILE}"
}
