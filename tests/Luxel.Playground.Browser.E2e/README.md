# Luxel Playground browser E2E

This suite launches the exported static Gallery in Chromium and verifies the C# scripting playground end to end.

## Run locally

Publish the runtime and export the Gallery first:

```bash
dotnet publish samples/LuxelPlaygroundBrowser/LuxelPlaygroundBrowser.csproj -c Release

dotnet run --project src/Luxel.Gallery.Site/Luxel.Gallery.Site.csproj -c Release -- \
  artifacts/gallery-site \
  --rasterizer skia \
  --playground-browser-root \
  samples/LuxelPlaygroundBrowser/bin/Release/net10.0/publish/wwwroot
```

Install and run Playwright:

```bash
cd tests/Luxel.Playground.Browser.E2e
npm ci
npx playwright install --with-deps chromium
npm test
```

The Chromium configuration enables SwiftShader WebGPU. On failure, Playwright retains a screenshot, video, and trace under `test-results/`.
