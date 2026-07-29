# Luxel browser WebGPU sample

A .NET 10 browser-WASM sample using `Luxel.Platform.Web`, `Luxel.Graphics.WebGPU.Browser`, and `Luxel.Graphics` abstractions. It asynchronously creates the DOM canvas window and WebGPU device, runs embedded fixed-ABI WGSL compute plus textured offscreen rendering, validates GPU readback, presents to a canvas that fills the browser viewport, and then pumps resize/pointer/key events on `requestAnimationFrame`. Browser layout owns the canvas CSS size; `ResizeObserver` updates its backing-store dimensions only when the content box or device-pixel ratio changes. Pass/debug state is exposed through `globalThis.luxelBrowserState` and a hidden `#status` marker rather than visible page text.

```bash
dotnet workload install wasm-tools
dotnet publish samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj -c Release
python3 -m http.server 8080 -d samples/LuxelWebGpuBrowser/bin/Release/net10.0/publish/wwwroot
```

Open `http://localhost:8080/?story=Examples%2F3D%2FTriangle` or `http://localhost:8080/?story=Controls%2FButton%2FCounter`. Canonical story routing is declared by `wwwroot/browser-runtime-manifest.json`; optional canonical JSON args use the `args` query parameter. WebGPU requires a secure context: use HTTPS for remote hosting; browsers treat `localhost` as trustworthy for development. The app is subpath-safe: all runtime and module URLs are relative, so the published `AppBundle` can be hosted below paths such as `/samples/webgpu-browser/`.

The sample never references native WebGPU, Silk, Windows, or Vulkan projects. Browser GPU/DOM objects remain in JavaScript registries and .NET stores only integer handles. Promise-based initialization/submission and animation frames are awaited rather than synchronously blocking the browser main thread.

The iframe contract is version 1. Child runtimes post same-origin `ready` or `story-error` messages containing the canonical story path, instance ID, seeded args, and schema placeholder.
