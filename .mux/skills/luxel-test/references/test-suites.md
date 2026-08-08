# Luxel test suites

This reference maps `/luxel-test` selectors to repository test entry points. Re-check that each referenced file exists before running it because projects may be reorganized.

## Discover current managed projects

```bash
find tests -mindepth 2 -maxdepth 2 -name '*.csproj' -print | sort
```

Every discovered project can be selected by its directory name, project filename, or full path.

## Environment preparation

Before running an environment-sensitive alias, read `environment-setup.md` and check the corresponding section:

| Suite | Preparation section |
| --- | --- |
| `audio-browser` full publish | Baseline tools; Browser WebAssembly workload |
| `audio-silk` | Ubuntu OpenAL and virtual PulseAudio |
| `webgpu` / `native-e2e` | Hardware GPU selection, Vulkan fallback, and X11; or Windows-specific WebGPU |
| `webgpu-browser` full CI | Baseline tools; Browser WebAssembly workload; Playwright and Chromium; Browser hardware WebGPU selection |
| `gallery-browser-e2e` | Browser WebAssembly workload; Playwright and Chromium |
| `playground-browser-e2e` | Browser WebAssembly workload; Playwright and Chromium |
| `platform` Silk tests | Hardware GPU selection, Vulkan fallback, and X11; or Full Luxel desktop helper environment |
| Native AOT validation | Full Luxel desktop helper environment |

When a prerequisite is missing, show its detection, preparation, privilege/effect, and cleanup commands. Ask before running `sudo`, installing a .NET workload, or installing Playwright system dependencies.

## Suite aliases

### `all`

Run every discovered `tests/*/*.csproj` except environment-sensitive projects listed below. Execute projects independently so one failure does not prevent collecting the remaining results.

Environment-sensitive exclusions from plain `all`:

- `Luxel.Audio.Silk.Tests` — OpenAL/PulseAudio integration.
- `Luxel.E2e.Tests` — native GPU Gallery E2E.
- `Luxel.Platform.Silk.Tests` — native Silk/window environment.
- `Luxel.Vulkan.Present.Tests` and `Luxel.WebGPU.Present.Tests` — presentation/display and GPU runtime.
- JavaScript/Playwright suites — separate aliases below.

State these exclusions in the final report.

### `audio`

Run all of `audio-browser` and `audio-silk`. Also run core audio tests from the main project:

```bash
dotnet test tests/Luxel.Tests/Luxel.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~Audio' --logger 'console;verbosity=minimal'
```

### `audio-browser`

```bash
dotnet test tests/Luxel.Audio.Browser.Tests/Luxel.Audio.Browser.Tests.csproj \
  -c Release --logger 'console;verbosity=minimal'
node --test tests/Luxel.Audio.Browser.Tests/luxel-audio-browser.test.mjs
```

For the complete CI contract, also run Gallery documentation tests and publish the browser sample. Publishing requires the `wasm-tools` workload:

```bash
dotnet test tests/Luxel.Gallery.Site.Tests/Luxel.Gallery.Site.Tests.csproj \
  -c Release --logger 'console;verbosity=minimal'
dotnet publish samples/LuxelAudioBrowser/LuxelAudioBrowser.csproj -c Release
```

Validate these files in the publish `wwwroot`: `index.html`, `main.js`, `luxel-audio-browser.js`, and `_framework/dotnet.js`.

### `audio-silk`

Follow `.github/workflows/test-audio-silk.yml`. On Linux, OpenAL and PulseAudio are prerequisites. Validate helper scripts first:

```bash
bash -n eng/desktop/*.sh
python3 eng/desktop/test-wav-analyzer.py
```

Start isolated audio, source its environment, run tests excluding optional output capture, and always stop audio:

```bash
export LUXEL_DESKTOP_AUDIO=null
export LUXEL_AUDIO_SINK=luxel_null
export LUXEL_DESKTOP_STATE_DIR="${TMPDIR:-/tmp}/luxel-audio-skill"
eng/desktop/audio-start.sh
source "$LUXEL_DESKTOP_STATE_DIR/environment"
eng/desktop/healthcheck.sh --audio-only
dotnet test tests/Luxel.Audio.Silk.Tests/Luxel.Audio.Silk.Tests.csproj \
  -c Release --filter 'FullyQualifiedName!~OutputCapture' \
  --logger 'console;verbosity=minimal'
eng/desktop/audio-stop.sh
```

Use a shell trap so `audio-stop.sh` also runs after a failure. Run `OutputCapture` only when explicitly requested; it records and analyzes WAV output as defined in the workflow.

### `gallery`

Run:

```text
tests/Luxel.Gallery.Generators.Tests
tests/Luxel.Gallery.Playground.Tests
tests/Luxel.Gallery.Site.Tests
tests/Luxel.WebGPU.Browser.Tests
```

These are managed contract tests and normally require no browser workload.

### `webgpu`

Run:

```text
tests/Luxel.WebGPU.Tests
tests/Luxel.WebGPU.Present.Tests
tests/Luxel.Framework.UI.Tests
```

Detect available GPU adapters first. Prefer a usable hardware Vulkan adapter on Linux or hardware DX12 adapter on Windows. Provide an X11 display for presentation tests when needed. Use the lavapipe/fallback environment from `.github/workflows/test-webgpu.yml` only when no compatible hardware GPU is accessible, the user explicitly requests software rendering, or fallback behavior is the subject of the test. If prerequisites are absent, run only explicitly requested managed/source-contract tests and report the reduced scope.

### `webgpu-browser`

Managed/source contracts:

```text
tests/Luxel.WebGPU.Browser.Tests
tests/Luxel.Gallery.Generators.Tests
tests/Luxel.Gallery.Site.Tests
tests/Luxel.Gallery.Playground.Tests
tests/Luxel.Scripting.Roslyn.Web.Tests
```

The full CI suite additionally requires `wasm-tools`, Chromium/Playwright, static Blazor Gallery publication, Playground publication, and both browser E2E aliases. Prefer Chromium's accessible hardware GPU/WebGPU adapter. Use SwiftShader or lavapipe only when hardware WebGPU is unavailable, explicitly requested, or the test targets fallback behavior. `.github/workflows/test-webgpu-browser.yml` documents the deterministic software-GPU CI fallback, not the preferred local adapter.

### `terminal`

Run:

```text
tests/Luxel.Terminal.Tests
tests/Luxel.Terminal.UI.Tests
```

Also run `tests/Luxel.Terminal.Linux.Tests` on Linux. Run `tests/Luxel.Terminal.Windows.Tests` only on Windows unless the user explicitly requests a cross-platform build-only check. Validate the dependency graph with:

```bash
python3 eng/check-project-dependencies.py
```

### `platform`

Run:

```text
tests/Luxel.Platform.Web.Tests
tests/Luxel.Platform.Silk.Tests
```

The Silk project may require a native display/window environment.

### `shader`

Run:

```bash
dotnet test tests/Luxel.Shaders.Tests/Luxel.Shaders.Tests.csproj \
  -c Release --logger 'console;verbosity=minimal'
dotnet msbuild shaders/Luxel.ShaderCache.proj -t:ValidateSlangShaderCache
```

### `native-e2e`

Run the xUnit integration project when the requested GPU backend is available. Detect and prefer a real hardware GPU; use a software Vulkan/DX fallback only when hardware is unavailable or explicitly requested:

```bash
dotnet test tests/Luxel.E2e.Tests/Luxel.E2e.Tests.csproj \
  -c Release --logger 'console;verbosity=minimal'
```

For the dedicated Gallery executable, use the backend requested by the user:

```bash
dotnet run --project gallery/GalleryE2E.Native/GalleryE2E.Native.csproj \
  -c Release -- <vk|dx> [story-filter] [--update] [--rasterizer skia]
```

Never use `--update` unless the user explicitly asks to update goldens.

### `gallery-browser-e2e`

From `gallery/GalleryE2E.Browser`:

```bash
npm ci
npm run install:browsers
npm run run
```

`npm run run` publishes the Blazor `GalleryBrowser` directly and executes Playwright against its static `wwwroot`. It requires `wasm-tools` and Chromium. If artifacts are already prepared, use `npm test` instead.

### `playground-browser-e2e`

Publish the standalone Playground browser from the repository root:

```bash
dotnet publish samples/LuxelPlaygroundBrowser/LuxelPlaygroundBrowser.csproj -c Release
```

Then from `tests/Luxel.Playground.Browser.E2e`:

```bash
npm ci
npm run install:browsers
npm test
```

Use `npm run test:slang` when only the Slang runtime smoke contracts are requested.

## Current managed project inventory

- `tests/Luxel.Audio.Browser.Tests/Luxel.Audio.Browser.Tests.csproj`
- `tests/Luxel.Audio.Silk.Tests/Luxel.Audio.Silk.Tests.csproj`
- `tests/Luxel.E2e.Tests/Luxel.E2e.Tests.csproj`
- `tests/Luxel.Framework.UI.Tests/Luxel.Framework.UI.Tests.csproj`
- `tests/Luxel.Gallery.Generators.Tests/Luxel.Gallery.Generators.Tests.csproj`
- `tests/Luxel.Gallery.Playground.Tests/Luxel.Gallery.Playground.Tests.csproj`
- `tests/Luxel.Gallery.Site.Tests/Luxel.Gallery.Site.Tests.csproj`
- `tests/Luxel.Platform.Silk.Tests/Luxel.Platform.Silk.Tests.csproj`
- `tests/Luxel.Platform.Web.Tests/Luxel.Platform.Web.Tests.csproj`
- `tests/Luxel.Scripting.Roslyn.Web.Tests/Luxel.Scripting.Roslyn.Web.Tests.csproj`
- `tests/Luxel.Shaders.Tests/Luxel.Shaders.Tests.csproj`
- `tests/Luxel.Terminal.Linux.Tests/Luxel.Terminal.Linux.Tests.csproj`
- `tests/Luxel.Terminal.Tests/Luxel.Terminal.Tests.csproj`
- `tests/Luxel.Terminal.UI.Tests/Luxel.Terminal.UI.Tests.csproj`
- `tests/Luxel.Terminal.Windows.Tests/Luxel.Terminal.Windows.Tests.csproj`
- `tests/Luxel.Tests/Luxel.Tests.csproj`
- `tests/Luxel.Vulkan.Present.Tests/Luxel.Vulkan.Present.Tests.csproj`
- `tests/Luxel.Vulkan.Tests/Luxel.Vulkan.Tests.csproj`
- `tests/Luxel.WebGPU.Browser.Tests/Luxel.WebGPU.Browser.Tests.csproj`
- `tests/Luxel.WebGPU.Present.Tests/Luxel.WebGPU.Present.Tests.csproj`
- `tests/Luxel.WebGPU.Tests/Luxel.WebGPU.Tests.csproj`
