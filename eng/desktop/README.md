# Web desktop for Linux UI development

These scripts create an isolated X11 desktop inside the workspace and expose it through Coder's authenticated HTTP port preview:

```text
GUI app -> Xvfb -> openbox -> x11vnc (Unix socket, no password)
        -> websockify/noVNC (loopback HTTP) -> authenticated Coder preview
```

The VNC server intentionally has no separate password and exposes no TCP listener; x11vnc and websockify communicate through a mode-`0600` Unix socket. Authentication is provided by the Coder preview for noVNC port 6080.

## Install

```bash
eng/desktop/install.sh
```

The installer currently targets Ubuntu/Debian environments with `apt-get`. For reproducible workspaces, bake these packages into the Coder or container image and use the installer only as a bootstrap.

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

The default Vulkan renderer is Mesa lavapipe for deterministic remote development. To use the GPU exposed to the workspace:

```bash
LUXEL_DESKTOP_RENDERER=hardware eng/desktop/start.sh
```

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
| `LUXEL_DESKTOP_GEOMETRY` | `1280x900x24` | Xvfb screen |
| `LUXEL_VNC_SOCKET` | state directory `/vnc.sock` | private x11vnc/websockify Unix socket |
| `LUXEL_NOVNC_PORT` | `6080` | loopback noVNC HTTP/WebSocket port |
| `LUXEL_DESKTOP_RENDERER` | `lavapipe` | `lavapipe` or `hardware` |
| `LUXEL_LAVAPIPE_ICD` | `/usr/share/vulkan/icd.d/lvp_icd.json` | software Vulkan ICD |
| `LUXEL_DESKTOP_STATE_DIR` | runtime directory | PID/log/screenshot state |
| `LUXEL_NOVNC_WEBROOT` | auto-detected | directory containing `vnc.html` |
| `LUXEL_DEBUG_SERVER_URL` | unset | optional Luxel DebugServer base URL |
| `LUXEL_WINDOW_ID` | unset | optional window ID for direct frame capture |

## Security assumptions

- Xvfb disables TCP with `-nolisten tcp`.
- x11vnc exposes only a mode-`0600` Unix socket; no VNC TCP port is opened.
- noVNC binds to `127.0.0.1` only.
- x11vnc uses `-nopw`; Coder's authenticated preview is the trust boundary.
- Only share the noVNC or Luxel DebugServer preview with users authorized for the workspace.
- Luxel DebugServer `/cmd` is an execution/control surface and must remain behind the same authenticated preview.

## Luxel integration

The desktop stack can be tested now with `vkcube`. Luxel itself still needs a Linux window backend and Vulkan X11/XCB surface support. Once implemented, launch Luxel after sourcing the generated environment file. Use noVNC for OS/window behavior and Luxel DebugServer endpoints for framebuffer, UI tree, GPU, and performance inspection.
