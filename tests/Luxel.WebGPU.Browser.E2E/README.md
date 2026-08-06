# Luxel browser WebGPU E2E

This Playwright suite launches the exported static Gallery in Chromium with SwiftShader WebGPU. It verifies browser-safe Graphics stories and their embedded iframes, plus interactive Audio/Web Audio stories including lifecycle, tone, bus, spatial, and queued-buffer controls.

## Run locally

Publish the browser runtime and export the Gallery first:

```bash
dotnet publish samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj -c Release

dotnet run --project src/Luxel.Gallery.Site/Luxel.Gallery.Site.csproj -c Release -- \
  artifacts/gallery-site \
  --browser-webgpu-root samples/LuxelWebGpuBrowser/bin/Release/net10.0/publish/wwwroot \
  --static-capture golden-only
```

Then run Chromium:

```bash
cd tests/Luxel.WebGPU.Browser.E2E
npm ci
npx playwright install --with-deps chromium
npm test
```
