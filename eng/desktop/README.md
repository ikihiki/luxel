# Wayland web desktop for Linux UI development

These scripts create a native Wayland desktop inside the workspace and expose it through Coder's authenticated HTTP port preview:

```text
Wayland app -> headless Sway -> wayvnc -> websockify/noVNC
```

Sway starts with `xwayland disable`; the generated application environment unsets `DISPLAY`. The desktop therefore has no Xorg, Xvfb, Openbox, x11vnc, or Xwayland fallback path.

wayvnc and websockify communicate through a mode-`0600` Unix socket. Authentication is provided by the Coder preview for noVNC port 6080. **noVNC transports pixels and input only; it does not transport audio.** The optional audio service is independent of Wayland and is intended for application testing and WAV capture inside the workspace.

## Dev Container prerequisites

Sway, wayvnc, Vulkan, audio, Native AOT, and noVNC dependencies are installed by `.devcontainer/Dockerfile`. Rebuild the Dev Container after changing these dependencies; runtime helper scripts do not install or modify system packages.

## Start and open

```bash
eng/desktop/start.sh
eng/desktop/url.sh
```

Open the printed URL in a browser where you are signed in to Coder. The default desktop is `WAYLAND_DISPLAY=wayland-1`, output `HEADLESS-1`, `1280x900 @ 60 Hz`, with noVNC on loopback port 6080.

Load the GUI environment in another shell:

```bash
source "${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/environment"
```

The Dev Container defaults to `LUXEL_DESKTOP_RENDERER=auto`: it uses hardware Vulkan through `/dev/dri/renderD128` when that render node is available, and automatically selects pixman plus lavapipe on GPU-less builders such as GitHub Actions. Set `LUXEL_DESKTOP_RENDERER=hardware` to require the render node explicitly. `LUXEL_REQUIRE_HARDWARE_VULKAN=1` makes `healthcheck.sh` require a non-CPU Vulkan device whenever hardware mode is selected. `LUXEL_VULKAN_VENDOR_ID` can restrict selection to a vendor such as Intel (`0x8086`).

For an explicit deterministic software fallback, use pixman and lavapipe:

```bash
LUXEL_DESKTOP_RENDERER=lavapipe eng/desktop/start.sh
```

## Current Luxel application support

The container-owned desktop is native Wayland-only. `vkcube-wayland` is the presentation baseline and is fully supported by these helpers.

`Luxel.Platform.Silk` supports native Wayland through GLFW and automatically selects it when `WAYLAND_DISPLAY` is available. Vulkan and WebGPU window presentation, the Native Gallery, file-based apps, and Linux window/presentation integration suites can run directly in this desktop without Xwayland. Explicit X11 remains available for compatible environments through `SilkWindowPlatform.X11` or `LuxelWindowBackend.SilkX11`.

## Optional Linux audio

Audio defaults to `off`. Select `off`, `null`, or `system` with `LUXEL_DESKTOP_AUDIO`. Null mode starts a repository-owned PulseAudio-compatible server and 48 kHz stereo null sink; system mode connects to an existing PipeWire Pulse or PulseAudio server.

```bash
LUXEL_DESKTOP_AUDIO=null eng/desktop/start.sh
source "${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/environment"
eng/desktop/capture-audio-start.sh /tmp/luxel-output.wav
# Run the application that produces audio.
eng/desktop/capture-audio-stop.sh
python3 eng/desktop/analyze-wav.py /tmp/luxel-output.wav --min-rms 0.01
```

Audio can run without the desktop through `audio-start.sh`, `healthcheck.sh --audio-only`, and `audio-stop.sh`.

## Baseline Vulkan smoke

```bash
eng/desktop/run-vkcube.sh
eng/desktop/healthcheck.sh
eng/desktop/screenshot.sh
```

Pass a frame count to run a bounded smoke, for example `eng/desktop/run-vkcube.sh 120`. The helper uses `vkcube-wayland`, FIFO present mode, and the Vulkan GPU selected by the health check.

## Status, logs, and stop

```bash
eng/desktop/status.sh
eng/desktop/healthcheck.sh
eng/desktop/stop.sh
```

Runtime files live under `${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/` and include the generated environment, Sway configuration, PID files, logs, screenshots, captures, and private VNC sockets. Stop GUI applications before stopping the desktop. Lifecycle scripts validate PID command lines and never use broad `pkill` calls.

## Configuration

| Variable | Default | Purpose |
|---|---|---|
| `LUXEL_DESKTOP_GEOMETRY` | `1280x900` | Wayland output size |
| `LUXEL_DESKTOP_REFRESH` | `60` | Wayland output refresh rate |
| `LUXEL_WAYLAND_DISPLAY` | `wayland-1` | Wayland socket name |
| `LUXEL_WAYLAND_OUTPUT` | `HEADLESS-1` | Sway output captured by wayvnc |
| `LUXEL_VNC_SOCKET` | state directory `/vnc.sock` | private wayvnc/websockify Unix socket |
| `LUXEL_NOVNC_PORT` | `6080` | loopback noVNC HTTP/WebSocket port |
| `LUXEL_DESKTOP_RENDERER` | Dev Container: `auto` | `auto`, `hardware`, or `lavapipe`; auto prefers the DRM render node and otherwise uses software |
| `LUXEL_DRM_RENDER_NODE` | `/dev/dri/renderD128` | DRM render node used by hardware Sway |
| `LUXEL_REQUIRE_HARDWARE_VULKAN` | `0` | require and select a hardware Vulkan device |
| `LUXEL_VULKAN_VENDOR_ID` | empty | optional required hexadecimal vendor ID |
| `LUXEL_LAVAPIPE_ICD` | `/usr/share/vulkan/icd.d/lvp_icd.json` | software Vulkan ICD |
| `LUXEL_DESKTOP_STATE_DIR` | runtime directory | PID/log/screenshot state |
| `LUXEL_NOVNC_WEBROOT` | auto-detected | directory containing `vnc.html` |
| `LUXEL_WAYVNC_MAX_FPS` | `60` | VNC frame-rate limit |
| `LUXEL_DESKTOP_AUDIO` | `off` | `off`, `null`, or `system` audio mode |

## Security assumptions

- Sway creates only a private Wayland socket under the mode-`0700` runtime directory.
- wayvnc exposes only a mode-`0600` Unix socket; no VNC TCP port is opened.
- noVNC binds to `127.0.0.1` only.
- wayvnc has no separate password; Coder's authenticated preview is the trust boundary.
- Only share the noVNC or Luxel DebugServer preview with users authorized for the workspace.
