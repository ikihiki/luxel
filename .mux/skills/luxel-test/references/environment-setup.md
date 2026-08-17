# Environment preparation

Use this reference whenever a Luxel test requires more than the checked-out repository and the installed .NET SDK. Commands below mirror the repository's GitHub Actions workflows, primarily on Ubuntu 24.04.

Do not perform privileged or machine-wide installation without user approval. It is always acceptable to run the detection commands first. When preparation is blocked, provide the relevant setup commands instead of classifying the suite as failed.

## Baseline tools

### Detect

```bash
dotnet --info
node --version
npm --version
python3 --version
```

Luxel managed projects target .NET 10. Browser suites use Node.js 24 in CI.

### Prepare

In GitHub Actions:

```yaml
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: 10.0.x
- uses: actions/setup-node@v4
  with:
    node-version: 24
```

On a local machine, install the .NET 10 SDK and Node.js 24 using the platform's approved package manager or version manager. Do not replace an existing system SDK automatically.

Restore only the selected project when possible:

```bash
dotnet restore <project.csproj>
```

For locked JavaScript suites:

```bash
cd <suite-directory>
npm ci
```

`npm ci` replaces that suite's `node_modules`; it does not install system packages.

## Browser WebAssembly workload

Needed by `dotnet publish` for browser-WASM projects such as Gallery Browser, Playground Browser, and Audio Browser.

### Detect

```bash
dotnet workload list | grep -E '^wasm-tools([[:space:]]|$)'
```

A publish failure with `NETSDK1147` also indicates that the workload is missing.

### Prepare

```bash
dotnet workload install wasm-tools
```

This modifies the active .NET SDK installation and may download a substantial workload, so ask for approval first.

### Remove if requested

```bash
dotnet workload uninstall wasm-tools
```

Do not uninstall a pre-existing workload during normal cleanup.

## Playwright and Chromium

Needed by `gallery-browser-e2e` and `playground-browser-e2e`.

The Gallery suite uses Microsoft.Playwright from its C# test project. Build it before invoking the generated installer:

```bash
dotnet build tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/Luxel.Gallery.Browser.E2E.Tests.csproj -c Release
pwsh tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/bin/Release/net10.0/playwright.ps1 --version
pwsh tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/bin/Release/net10.0/playwright.ps1 install chromium
```

For CI-equivalent Ubuntu installation, include system libraries:

```bash
pwsh tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
```

The Playground smoke suite remains Node-based. From `tests/Gallery/Luxel.Playground.Browser.E2e` use `npm ci`, `npx playwright --version`, and `npm run install:browsers`.

`--with-deps` may invoke the OS package manager and require elevated privileges. Ask for approval before running it.

### Artifacts and cleanup

Playwright failure artifacts normally remain under:

```text
test-results/
playwright-report/
```

Do not delete them after a failure. Browser binaries are cached outside the repository; do not remove shared caches unless explicitly requested.

## Browser hardware WebGPU selection

The Gallery and Playground Playwright configurations prefer Chromium's normal hardware adapter by default. Detect host GPU access using the platform tools in the GPU section below, ensure no inherited software-rendering variables remain, then run normally:

```bash
unset LUXEL_E2E_SOFTWARE_GPU
npm test
```

To explicitly request deterministic SwiftShader software rendering:

```bash
LUXEL_E2E_SOFTWARE_GPU=1 npm test
```

Only set `LUXEL_E2E_SOFTWARE_GPU=1` when no compatible hardware WebGPU adapter is accessible, the user requests software rendering, or fallback behavior is being tested. Hosted CI sets it for deterministic execution.

If Chromium cannot expose a hardware adapter in the current container/session, report that limitation before retrying with SwiftShader. Do not describe a SwiftShader pass as hardware-GPU validation.

## Ubuntu OpenAL and virtual PulseAudio

Needed by `audio-silk` and optional output-capture tests.

### Detect

```bash
command -v pulseaudio
command -v paplay
ldconfig -p 2>/dev/null | grep -E 'libopenal\.so'
```

### Install Ubuntu dependencies

```bash
sudo apt-get update
sudo apt-get install -y --no-install-recommends \
  pulseaudio pulseaudio-utils libopenal1 libopenal-dev
```

This is privileged and machine-wide; ask for approval first.

### Start the isolated Luxel audio environment

```bash
export LUXEL_DESKTOP_AUDIO=null
export LUXEL_AUDIO_SINK=luxel_null
export LUXEL_DESKTOP_STATE_DIR="${TMPDIR:-/tmp}/luxel-audio-skill"
eng/desktop/audio-start.sh
source "$LUXEL_DESKTOP_STATE_DIR/environment"
eng/desktop/healthcheck.sh --audio-only
```

Use a trap so cleanup happens after success or failure:

```bash
trap 'eng/desktop/audio-stop.sh || true' EXIT
```

### Stop and inspect diagnostics

```bash
eng/desktop/audio-stop.sh
trap - EXIT
```

Logs are normally under:

```text
$LUXEL_DESKTOP_STATE_DIR/logs/
```

Optional output capture additionally uses `eng/desktop/capture-audio-start.sh`, `capture-audio-stop.sh`, and `eng/desktop/analyze-wav.py`. Do not enable `LUXEL_AUDIO_OUTPUT_CAPTURE=1` unless output capture was explicitly requested.

## Hardware GPU selection, Vulkan fallback, and X11

Needed by native `webgpu`, Vulkan/WebGPU presentation tests, browser WebGPU E2E, some platform tests, and native Gallery E2E.

### Detect hardware adapters first

```bash
command -v vulkaninfo
vulkaninfo --summary
```

Inspect the reported `deviceName` and `deviceType`. Prefer a usable `PHYSICAL_DEVICE_TYPE_DISCRETE_GPU` or `PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU`. Treat `PHYSICAL_DEVICE_TYPE_CPU` and names such as `llvmpipe`, `lavapipe`, `SwiftShader`, and `software rasterizer` as software adapters.

Optional vendor checks:

```bash
lspci 2>/dev/null | grep -Ei 'vga|3d|display' || true
nvidia-smi --query-gpu=name,driver_version --format=csv,noheader 2>/dev/null || true
```

For a hardware run, do **not** set `VK_ICD_FILENAMES` to lavapipe, `LIBGL_ALWAYS_SOFTWARE`, `LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER`, or `WGPU_ADAPTER_NAME=llvmpipe`. Clear inherited fallback settings before the run:

```bash
unset VK_ICD_FILENAMES LIBGL_ALWAYS_SOFTWARE
unset LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER WGPU_ADAPTER_NAME
export LUXEL_WEBGPU_REQUIRE_ADAPTER=1
export WGPU_BACKEND=vulkan
```

If several real adapters exist, use the backend's supported adapter-selection setting when the user names one. Otherwise allow the runtime to select its normal high-performance hardware adapter. Record the selected adapter from test/application output.

### Display detection

The Dev Container desktop is pure Wayland. Detect it with:

```bash
command -v sway wayvnc wlr-randr wayland-info
if [ -n "${WAYLAND_DISPLAY:-}" ]; then wlr-randr >/dev/null; fi
```

Current Luxel Linux window and presentation integration tests remain X11-only. They cannot use the repository Wayland desktop because it starts Sway with Xwayland disabled. When those legacy suites are explicitly requested, detect or provision an isolated X11 display separately:

```bash
command -v Xvfb xdpyinfo
if [ -n "${DISPLAY:-}" ]; then xdpyinfo -display "$DISPLAY" >/dev/null; fi
```

### Install Ubuntu runtime and display tools

For the Wayland desktop and Vulkan baseline:

```bash
sudo apt-get update
sudo apt-get install -y libvulkan1 vulkan-tools mesa-vulkan-drivers sway wayvnc wayland-utils grim wlr-randr novnc websockify
```

For legacy X11-only Luxel presentation tests, install Xvfb and X11 utilities separately rather than adding them to the Dev Container image.

Install the correct vendor GPU driver through the machine's approved provisioning method. Do not replace or modify GPU drivers automatically. This is privileged and machine-wide; ask for approval first.

### Software fallback only when needed

If no compatible hardware GPU is accessible, the user explicitly requests software rendering, or fallback behavior is under test, install and select lavapipe:

```bash
sudo apt-get update
sudo apt-get install -y mesa-vulkan-drivers libvulkan1 vulkan-tools
export VK_ICD_FILENAMES="$(find /usr/share/vulkan/icd.d -maxdepth 1 -name 'lvp_icd*.json' -print -quit)"
test -n "$VK_ICD_FILENAMES"
export LIBGL_ALWAYS_SOFTWARE=true
export LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER=1
export LUXEL_WEBGPU_REQUIRE_ADAPTER=1
export WGPU_BACKEND=vulkan
export WGPU_ADAPTER_NAME=llvmpipe
```

Never switch from a detected hardware adapter to this fallback silently. Report the hardware initialization failure and ask or clearly announce the fallback according to the user's request.

### Start an isolated X11 display for legacy Luxel suites

```bash
export DISPLAY=:99
Xvfb "$DISPLAY" -screen 0 1280x720x24 > /tmp/luxel-xvfb.log 2>&1 &
LUXEL_XVFB_PID=$!
for attempt in {1..20}; do
  xdpyinfo -display "$DISPLAY" >/dev/null 2>&1 && break
  sleep 0.25
done
xdpyinfo -display "$DISPLAY" >/dev/null
openbox > /tmp/luxel-openbox.log 2>&1 &
LUXEL_OPENBOX_PID=$!
```

### Cleanup

```bash
kill "$LUXEL_OPENBOX_PID" 2>/dev/null || true
kill "$LUXEL_XVFB_PID" 2>/dev/null || true
```

Preserve `/tmp/luxel-xvfb.log` and `/tmp/luxel-openbox.log` when a test fails.

## Full Luxel desktop helper environment

Wayland desktop infrastructure and Vulkan baseline tests use the repository helper scripts. Native AOT validation builds and inspects Linux artifacts but does not execute the current X11-only Luxel window binary inside this desktop.

### Dev Container packages

The complete desktop environment is provisioned by `.devcontainer/Dockerfile`, including headless Sway with Xwayland disabled, wayvnc/noVNC, Vulkan tooling, audio libraries, and Native AOT prerequisites. Xorg, Xvfb, Openbox, and x11vnc are intentionally absent. Rebuild the Dev Container when these dependencies change; do not install them from the runtime test workflow.

### Start, verify, and stop

```bash
eng/desktop/start.sh
source "${XDG_RUNTIME_DIR:-/tmp}/luxel-desktop-${UID}/environment"
eng/desktop/healthcheck.sh
```

Always clean up:

```bash
eng/desktop/stop.sh
```

Use a shell trap when running tests between start and stop.

## Windows-specific WebGPU

Windows WebGPU validation uses DX12 and must run on Windows. Prefer the normal hardware adapter and do not force fallback by default:

```powershell
Remove-Item Env:LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER -ErrorAction SilentlyContinue
Remove-Item Env:WGPU_ADAPTER_NAME -ErrorAction SilentlyContinue
$env:LUXEL_WEBGPU_REQUIRE_ADAPTER = '1'
$env:WGPU_BACKEND = 'dx12'
$env:WGPU_DX12_COMPILER = 'fxc'
dotnet test tests/Graphics/Luxel.WebGPU.Tests/Luxel.WebGPU.Tests.csproj -c Release
```

Use `$env:LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER = '1'` only for an explicit fallback-adapter test or when no compatible hardware adapter is accessible.

Do not attempt to emulate Windows-only tests on Linux. Report that a Windows environment is required. The repository currently keeps Windows Terminal CI validation disabled; do not claim it passed unless it was explicitly run on a suitable Windows machine.

## Platform and OS checks

Detect the current OS before selecting native projects:

```bash
uname -s
```

- Run `Luxel.Terminal.Linux.Tests` on Linux.
- Run `Luxel.Terminal.Windows.Tests` on Windows.
- `Luxel.Platform.Silk.Tests` may require the X11/desktop setup above.
- Presentation tests require a real or virtual display plus the matching graphics runtime.

## Reporting a preparation requirement

Use this structure before installation or when blocked:

```text
Required environment: <dependency or service>
Detected by: <command and relevant output>
Preparation:
  <exact commands>
Privileges/effect: <sudo, SDK workload modification, browser download, or local-only npm install>
Cleanup:
  <exact cleanup command, or “none”>
```
