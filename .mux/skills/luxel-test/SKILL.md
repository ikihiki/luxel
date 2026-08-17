---
name: luxel-test
description: Run any Luxel test project or CI-style test suite, including Audio, Gallery, WebGPU, Terminal, and browser E2E tests
argument-hint: "<list|all|suite|project> [filter or extra test arguments]"
when_to_use: Use when the user asks to run, rerun, select, diagnose, or validate Luxel tests.
---

# Luxel Test Runner

Run the Luxel tests requested by the user. Invocation arguments are:

```text
$ARGUMENTS
```

Work from the repository root. Read `references/test-suites.md` when choosing commands or suite membership. Read `references/environment-setup.md` whenever the selected test needs a workload, browser, native library, GPU/display service, audio service, OS-specific runtime, or another external prerequisite.

## Resolve the request

Interpret the first argument as one of:

- `list`: show the available suite aliases and discovered test projects; do not run tests.
- `all`: run every ordinary managed `.csproj` test. Do not silently include hardware, display, browser-WASM, Playwright, or output-capture tests.
- a suite alias from `references/test-suites.md`, such as `audio`, `audio-browser`, `audio-silk`, `gallery`, `webgpu`, `webgpu-browser`, `terminal`, `platform`, `shader`, `native-e2e`, `gallery-browser-e2e`, or `playground-browser-e2e`.
- a test project directory, project filename, or unambiguous short name under `tests/`; run that project only.
- a fully qualified test class or test method; locate its project and use a `FullyQualifiedName` filter.

If the target is ambiguous, list the matching projects and ask the user to choose. Do not guess between similarly named native, browser, presentation, or platform suites.

## Managed test command

Prefer the narrowest command that answers the request:

```bash
dotnet test <project.csproj> --configuration Release --logger 'console;verbosity=minimal'
```

For a class or method:

```bash
dotnet test <project.csproj> --configuration Release \
  --filter 'FullyQualifiedName~<class-or-method>' \
  --logger 'console;verbosity=minimal'
```

Pass additional user arguments through only when their meaning is clear. Use `--no-restore` or `--no-build` only after the corresponding restore/build has succeeded in the current workspace. Never hide a stale-build risk just to make a run faster.

For several independent managed projects, run them in parallel when resources permit. Keep environment-sensitive suites sequential.

## Mandatory GPU adapter policy

Apply this policy to every native GPU, WebGPU, Vulkan, presentation, Gallery GPU, and browser WebGPU test:

1. Detect accessible adapters before configuring a fallback.
2. Prefer a usable real hardware adapter: discrete GPU first when the runtime normally selects it, otherwise an integrated GPU.
3. Clear inherited software-forcing settings before a hardware run. In particular, do not force lavapipe, llvmpipe, or SwiftShader merely because CI uses them for determinism.
4. Use a software adapter only when no compatible hardware adapter is accessible, the user explicitly requests software rendering, or the selected test verifies fallback behavior.
5. If hardware is detected but initialization fails, preserve and report that hardware failure. Do not silently rerun on software. Offer or announce the fallback separately.
6. Report the adapter name, graphics backend, and classification (`hardware` or `software`) with the test result. A software-adapter pass is not hardware-GPU validation.

For Browser E2E, leave `LUXEL_E2E_SOFTWARE_GPU` unset for the normal hardware-preferred run. Set `LUXEL_E2E_SOFTWARE_GPU=1` only for the explicit SwiftShader fallback. For native Linux GPU tests, do not set lavapipe-specific `VK_ICD_FILENAMES`, `LIBGL_ALWAYS_SOFTWARE`, `LUXEL_WEBGPU_FORCE_FALLBACK_ADAPTER`, or `WGPU_ADAPTER_NAME=llvmpipe` during a hardware run.

Use `references/environment-setup.md` for adapter detection, hardware-mode environment cleanup, display setup, and explicit software fallback commands.

## Suite behavior

- Follow the suite definitions and prerequisites in `references/test-suites.md`.
- `audio` means both managed/browser audio contracts and Silk/OpenAL tests. Also run the JavaScript Web Audio queue contracts.
- Browser E2E aliases must prepare their runtime unless the user explicitly says artifacts are already prepared.
- Before running an environment-sensitive suite, check its prerequisites with the commands in `references/environment-setup.md`.
- For GPU tests, detect available adapters first and prefer a real hardware GPU. Do not set software/fallback adapter variables or force lavapipe/SwiftShader when a usable hardware adapter is available. Use software rendering only when no compatible hardware GPU is accessible, the user explicitly requests it, or a test specifically validates fallback behavior.
- Report the selected GPU name, backend, and whether it is hardware or software. If hardware detection succeeds but initialization fails, report that failure before offering a software fallback; do not silently change adapters.
- If preparation is needed, show the user the exact preparation commands, what they install or start, whether elevated privileges are required, and the cleanup command. If the user already asked to run the suite, ask approval before performing privileged or machine-wide installation.
- Tests requiring `wasm-tools`, Chromium, Vulkan/lavapipe, OpenAL/PulseAudio, X11, Windows, or another unavailable dependency must not be reported as test failures. Report the exact missing prerequisite, the command that exposed it, and the preparation method from `references/environment-setup.md`.
- Do not install system packages, .NET workloads, or Playwright browsers without user approval. `npm ci` is allowed when the user asked for the corresponding browser suite and the lockfile exists.
- Preserve failure artifacts and mention their paths. Relevant defaults include `test-results/`, `playwright-report/`, temporary WAV captures, and desktop/audio logs.
- Always stop virtual audio, Wayland desktop helpers, servers, and background processes started by the run, including after failures. Legacy X11-only suites may still start their own Xvfb and must clean it up separately.

## Verification and report

After execution, report:

1. the resolved suite/project(s),
2. the exact commands run,
3. passed, failed, and skipped counts per project,
4. unavailable prerequisites or environmental blockers,
5. for every blocker, exact environment preparation and cleanup commands,
6. failure artifact paths and the first actionable error when anything fails.

Do not claim that `all` passed if optional/environment-sensitive suites were not run. Say exactly which scope passed.
