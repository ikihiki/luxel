#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=common.sh
source "${SCRIPT_DIR}/common.sh"

require_command flock
exec 8>"${AUDIO_LOCK_FILE}"
flock -w 10 8 || fail "could not acquire audio operation lock"

cleanup_on_error() {
    local status=$?
    if [[ ${status} -ne 0 ]]; then
        log "audio startup failed; cleaning repository-owned audio state"
        "${SCRIPT_DIR}/audio-stop.sh" --no-lock || true
    fi
    exit "${status}"
}
trap cleanup_on_error EXIT

current_mode="$(cat "${AUDIO_MODE_FILE}" 2>/dev/null || echo off)"
if [[ "${current_mode}" != "${AUDIO_MODE}" || "${AUDIO_MODE}" == "off" ]]; then
    "${SCRIPT_DIR}/audio-stop.sh" --no-lock
fi

case "${AUDIO_MODE}" in
    off)
        printf '%s\n' off > "${AUDIO_MODE_FILE}"
        rm -f "${AUDIO_SERVER_FILE}" "${AUDIO_MODULE_FILE}"
        ;;
    system)
        require_command pactl
        printf '%s\n' "${AUDIO_PULSE_SERVER}" > "${AUDIO_SERVER_FILE}"
        if ! audio_server_ready; then
            rm -f "${AUDIO_SERVER_FILE}"
            fail "system PulseAudio/PipeWire-Pulse server is unavailable at ${AUDIO_PULSE_SERVER}"
        fi
        printf '%s\n' system > "${AUDIO_MODE_FILE}"
        log "using system PulseAudio-compatible server ${AUDIO_PULSE_SERVER}"
        ;;
    null)
        require_command pactl
        require_command pulseaudio
        mkdir -p "$(dirname -- "${AUDIO_SERVER_SOCKET}")"
        chmod 700 "$(dirname -- "${AUDIO_SERVER_SOCKET}")"
        printf '%s\n' "${AUDIO_PULSE_SERVER}" > "${AUDIO_SERVER_FILE}"
        start_process pulseaudio pulseaudio \
            pulseaudio --daemonize=no --use-pid-file=no --exit-idle-time=-1 \
                --disallow-exit --disable-shm=yes --log-target=stderr \
                --load="module-native-protocol-unix socket=${AUDIO_SERVER_SOCKET} auth-anonymous=1"
        wait_until "PulseAudio-compatible server" audio_server_ready
        if ! audio_sink_ready; then
            module_id="$(audio_pactl load-module module-null-sink \
                "sink_name=${AUDIO_SINK}" \
                "rate=${AUDIO_RATE}" \
                "channels=${AUDIO_CHANNELS}" \
                "channel_map=front-left,front-right" \
                "sink_properties=device.description=Luxel_Null_Output")"
            [[ "${module_id}" =~ ^[0-9]+$ ]] || fail "pactl returned invalid module id: ${module_id}"
            printf '%s\n' "${module_id}" > "${AUDIO_MODULE_FILE}"
        fi
        wait_until "null sink ${AUDIO_SINK}" audio_sink_ready
        audio_pactl set-default-sink "${AUDIO_SINK}"
        printf '%s\n' "${AUDIO_SINK}" > "${AUDIO_SINK_FILE}"
        printf '%s\n' null > "${AUDIO_MODE_FILE}"
        log "audio null sink ${AUDIO_SINK} is ready at ${AUDIO_RATE} Hz stereo"
        ;;
esac

write_environment_file
trap - EXIT
log "audio mode: ${AUDIO_MODE}"
