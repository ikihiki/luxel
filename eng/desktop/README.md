# Web desktop for Linux UI development

These scripts create an isolated X11 desktop inside the workspace and expose it through Coder's authenticated HTTP port preview:

```text
software: GUI app -> Xvfb -> openbox -> x11vnc -> websockify/noVNC
hardware: GUI app -> Xorg + Intel virtual head (DRI3) -> openbox -> x11vnc -> websockify/noVNC
```

The VNC server intentionally has no separate password and exposes no TCP listener; x11vnc and websockify communicate through a mode-`0600` Unix socket. Authentication is provided by the Coder preview for noVNC port 6080. **noVNC transports pixels and input only; it does not transport audio.** The optional audio service is independent of X11 and is intended for application testing and WAV capture inside the workspace.

## Dev Container prerequisites

Desktop, Xorg/DRI3, Vulkan, audio, Native AOT, and noVNC dependencies are installed by `.devcontainer/Dockerfile`. Rebuild the Dev Container after changing these dependencies; runtime helper scripts do not install or modify system packages.

## Start and open

```bash
eng/desktop/start.sh
eng/desktop/url.sh
```

Open the printed URL in a browser where you are signed in to Coder. The default desktop is `DISPLAY=:99`, `1280x900x24`, with noVNC on loopback port 6080.

Load the GUI environment in another shell:

```bash
source "${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/environment"
```

The Luxel Dev Container defaults to hardware rendering. It starts Xorg with a DRI3-capable Intel virtual head and uses the Intel GPU exposed to the workspace:

```bash
eng/desktop/start.sh
```

For an explicit deterministic software fallback, start Xvfb with lavapipe:

```bash
LUXEL_DESKTOP_RENDERER=lavapipe LUXEL_DESKTOP_SERVER=xvfb eng/desktop/start.sh
```

The Xorg hardware path requires `/dev/dri/card0`, the Intel Xorg driver, and an Intel GPU that supports `VirtualHeads`.

Hardware-backed workspaces can set `LUXEL_REQUIRE_HARDWARE_VULKAN=1`. With this strict check,
`healthcheck.sh` requires at least one non-CPU Vulkan device and records its GPU index for
`run-vkcube.sh`. CPU devices such as llvmpipe may coexist with the Intel iGPU; they only cause a
failure when no hardware device is available. This prevents a missing `/dev/dri` passthrough from
silently falling back to software.

Set `LUXEL_VULKAN_VENDOR_ID` to require a specific hardware vendor. Use `0x8086` for an Intel
workspace so a different discrete GPU cannot be selected accidentally.

## Optional Linux audio

Audio defaults to `off` so starting the web desktop never changes the host audio configuration. Select one of these modes with `LUXEL_DESKTOP_AUDIO`:

| Mode | Behavior |
|---|---|
| `off` | Default. Do not connect to or start an audio server. |
| `null` | Start a repository-owned PulseAudio-compatible server and a named 48 kHz stereo `module-null-sink`. This is the deterministic CI/headless mode. |
| `system` | Connect to the existing PipeWire Pulse compatibility or PulseAudio server. No server or sink is created or stopped. |

Start the complete desktop with a virtual sink:

```bash
LUXEL_DESKTOP_AUDIO=null eng/desktop/start.sh
source "${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/environment"
```

Audio can also run without Xvfb, openbox, or noVNC:

```bash
LUXEL_DESKTOP_AUDIO=null eng/desktop/audio-start.sh
LUXEL_DESKTOP_AUDIO=null eng/desktop/healthcheck.sh --audio-only
source "${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/environment"
# Run the application or tests here.
eng/desktop/audio-stop.sh
```

For an existing desktop audio service, preserve the shell's `PULSE_SERVER` or identify it explicitly:

```bash
LUXEL_DESKTOP_AUDIO=system \
LUXEL_SYSTEM_PULSE_SERVER="unix:${XDG_RUNTIME_DIR}/pulse/native" \
eng/desktop/audio-start.sh
```

The generated environment sets `ALSOFT_DRIVERS=pulse` in `null` and `system` modes. Null mode also sets `PULSE_SINK=luxel_null`. OpenAL Soft therefore writes to the selected Pulse-compatible server rather than silently choosing another backend.

### Capture and analyze output

Capture the null sink monitor (or `@DEFAULT_MONITOR@` in system mode) to a 48 kHz stereo PCM WAV:

```bash
eng/desktop/capture-audio-start.sh /tmp/luxel-output.wav
# Run the application that produces audio.
eng/desktop/capture-audio-stop.sh
python3 eng/desktop/analyze-wav.py /tmp/luxel-output.wav \
  --min-rms 0.01 \
  --expect-frequency 1000 --frequency-tolerance 10 \
  --expect-pan 0 --pan-tolerance 0.05
```

`analyze-wav.py` prints JSON containing duration, per-channel RMS and energy, stereo pan (`-1` left to `+1` right), and dominant frequency. It supports uncompressed 8-, 16-, 24-, and 32-bit integer PCM without third-party Python packages. Run its deterministic fixture test with:

```bash
python3 eng/desktop/test-wav-analyzer.py
```

The capture helper has one tracked recorder at a time and uses the same PID ownership checks as the desktop services. Stop scripts unload only the persisted null-sink module ID and terminate only repository-owned process groups; they do not use broad `pkill` calls.

## Baseline Vulkan smoke

Before testing Luxel's Linux backend, verify the desktop and Vulkan WSI independently:

```bash
eng/desktop/run-vkcube.sh
eng/desktop/healthcheck.sh
eng/desktop/screenshot.sh
```

`run-vkcube.sh` uses the desktop's selected Vulkan ICD and makes the cube visible through noVNC.

## Status and logs

```bash
eng/desktop/status.sh
eng/desktop/healthcheck.sh
```

Runtime files live outside the repository:

```text
${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/
  environment
  pids/
  logs/
  screenshots/
  captures/
  audio.mode, audio.server, audio.module, audio.sink
```

If startup fails, inspect the per-process logs in that directory.

## Stop

Stop GUI applications first, then stop the desktop services:

```bash
eng/desktop/stop.sh
```

The stop script validates PID command lines before terminating processes and does not use broad `pkill` calls.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `LUXEL_DESKTOP_DISPLAY` | `:99` | X display |
| `LUXEL_DESKTOP_GEOMETRY` | `1280x900x24` | virtual screen size |
| `LUXEL_DESKTOP_SERVER` | `auto` | `xvfb`, `xorg`, or automatic selection from renderer |
| `LUXEL_VNC_SOCKET` | state directory `/vnc.sock` | private x11vnc/websockify Unix socket |
| `LUXEL_NOVNC_PORT` | `6080` | loopback noVNC HTTP/WebSocket port |
| `LUXEL_DESKTOP_RENDERER` | Dev Container: `hardware`; script fallback: `lavapipe` | `lavapipe` or `hardware` |
| `LUXEL_REQUIRE_HARDWARE_VULKAN` | `0` | Set to `1` to require and select at least one hardware Vulkan device |
| `LUXEL_VULKAN_VENDOR_ID` | empty | Optional hexadecimal vendor ID required of the selected hardware device |
| `LUXEL_LAVAPIPE_ICD` | `/usr/share/vulkan/icd.d/lvp_icd.json` | software Vulkan ICD |
| `LUXEL_DESKTOP_STATE_DIR` | runtime directory | PID/log/screenshot state |
| `LUXEL_NOVNC_WEBROOT` | auto-detected | directory containing `vnc.html` |
| `LUXEL_DESKTOP_AUDIO` | `off` | `off`, `null`, or `system` audio mode |
| `LUXEL_SYSTEM_PULSE_SERVER` | inherited/auto | Pulse-compatible server address for `system` mode |
| `LUXEL_PULSE_SERVER_SOCKET` | state runtime `/pulse/native` | private server socket for `null` mode |
| `LUXEL_AUDIO_SINK` | `luxel_null` | null sink name |
| `LUXEL_AUDIO_CAPTURE_DEVICE` | mode default | explicit Pulse source passed to `parec` |
| `LUXEL_DEBUG_SERVER_URL` | unset | optional Luxel DebugServer base URL |
| `LUXEL_WINDOW_ID` | unset | optional window ID for direct frame capture |

## Security assumptions

- Xvfb and Xorg disable TCP with `-nolisten tcp`.
- x11vnc exposes only a mode-`0600` Unix socket; no VNC TCP port is opened.
- noVNC binds to `127.0.0.1` only.
- x11vnc uses `-nopw`; Coder's authenticated preview is the trust boundary.
- Only share the noVNC or Luxel DebugServer preview with users authorized for the workspace.
- Luxel DebugServer `/cmd` is an execution/control surface and must remain behind the same authenticated preview.

## Luxel integration

After sourcing the generated environment file, Luxel can create X11 windows through `Luxel.Platform.Silk` and present with the Vulkan window-surface backend. For a minimal smoke test, run `dotnet run --project samples/LuxelTriangle -- vk --frames 3`. Use noVNC for OS/window behavior and Luxel DebugServer endpoints for framebuffer, UI tree, GPU, and performance inspection.
