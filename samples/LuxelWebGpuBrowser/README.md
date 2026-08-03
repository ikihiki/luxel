# Luxel browser WebGPU sample

A .NET 10 browser-WASM sample using `Luxel.Platform.Web`, `Luxel.Graphics.WebGPU.Browser`, and `Luxel.Graphics` abstractions. It asynchronously creates the DOM canvas window and WebGPU device, runs embedded fixed-ABI WGSL compute plus textured offscreen rendering, validates GPU readback, presents to a canvas that fills the browser viewport, and then pumps resize/pointer/key events on `requestAnimationFrame`. Browser layout owns the canvas CSS size; `ResizeObserver` updates its backing-store dimensions only when the content box or device-pixel ratio changes. Pass/debug state is exposed through `globalThis.luxelBrowserState` and a hidden `#status` marker rather than visible page text.

```bash
dotnet workload install wasm-tools
dotnet publish samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj -c Release
python3 -m http.server 8080 -d samples/LuxelWebGpuBrowser/bin/Release/net10.0/publish/wwwroot
```

Open `http://localhost:8080/?story=Examples%2F3D%2FTriangle`, `http://localhost:8080/?story=Controls%2FButton%2FCounter`, or any generated production `Controls/{category}/Basic` path declared by `wwwroot/browser-runtime-manifest.json`. Optional canonical JSON args use the `args` query parameter and `instance` identifies one iframe instance. WebGPU requires a secure context: use HTTPS for remote hosting; browsers treat `localhost` as trustworthy for development. The app is subpath-safe: all runtime and module URLs are relative, so the published `AppBundle` can be hosted below paths such as `/samples/webgpu-browser/`.

The sample references the browser-safe `Luxel.Gallery.Stories.CoreUi` project and never reaches native WebGPU, Silk, Windows, Vulkan, terminal, scripting, Skia, or ICU projects. Browser GPU/DOM objects remain in JavaScript registries and .NET stores only integer handles. Promise-based initialization/submission and animation frames are awaited rather than synchronously blocking the browser main thread.

To add another runnable WASM story, add a normal `[Story]` method under `src/Luxel.Gallery.Stories.CoreUi`. That assembly is the browser-safe Gallery boundary: its generated catalog automatically assigns the browser runtime bundle, the manifest generator discovers the story, and the shared runtime executes its returned `Widget`. `GpuView` stories can allocate scoped GPU resources through `ctx.ScopedResources` and capture the handles in the render callback. Do not add a route switch, manifest descriptor, or browser-project source link.

The iframe contract is **protocol version 2**. Every message carries the canonical story path, instance ID, revision, and same-origin marker. Parent Gallery pages send revisioned/request-ID `set-args`; children send `ready`, `args-changed`, `arg-error`, or `story-error`. Both sides validate origin, source window, protocol version, story, instance, and monotonic revision. The parent owns the accessible HTML args table and persists non-default top-level/embed snapshots in the Gallery hash. See `docs/gallery-runtime-protocol.md`.
