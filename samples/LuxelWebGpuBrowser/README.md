# Luxel browser WebGPU sample

A .NET 10 browser-WASM sample using `Luxel.Platform.Web`, `Luxel.Graphics.WebGPU.Browser`, and `Luxel.Graphics` abstractions. It asynchronously creates the DOM canvas window and WebGPU device, runs embedded fixed-ABI WGSL compute plus textured offscreen rendering, validates GPU readback, presents to the canvas, and then pumps resize/pointer/key events on `requestAnimationFrame`.

```bash
dotnet workload install wasm-tools
dotnet publish samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj -c Release
python3 -m http.server 8080 -d samples/LuxelWebGpuBrowser/bin/Release/net10.0/publish/wwwroot
```

Open `http://localhost:8080/`. WebGPU requires a secure context: use HTTPS for remote hosting; browsers treat `localhost` as trustworthy for development. The app is subpath-safe: all runtime and module URLs are relative, so the published `AppBundle` can be hosted below paths such as `/samples/webgpu-browser/`.

The sample never references native WebGPU, Silk, Windows, or Vulkan projects. Browser GPU/DOM objects remain in JavaScript registries and .NET stores only integer handles. Promise-based initialization/submission and animation frames are awaited rather than synchronously blocking the browser main thread.
