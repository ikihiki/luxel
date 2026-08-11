# Luxel Gallery Browser E2E

This xUnit project drives the statically published Blazor Gallery with Microsoft Playwright and deterministic Chromium SwiftShader WebGPU arguments. It contains 88 discoverable tests covering the complete Gallery route audit, category stories, Markdown iframes, WebGPU diagnostics, canvas interactions, shared Args/Output/Source panels, Audio, Resources, and browser scripting.

From the repository root:

```bash
dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release -o artifacts/gallery-browser
dotnet build tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/Luxel.Gallery.Browser.E2E.Tests.csproj -c Release
pwsh tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
dotnet test tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/Luxel.Gallery.Browser.E2E.Tests.csproj -c Release --no-build
```

The static root defaults to `artifacts/gallery-browser/wwwroot`. Override it with `LUXEL_GALLERY_BROWSER_ROOT`. `LUXEL_WEBGPU_E2E_PORT` selects the static-server port, `LUXEL_WEBGPU_E2E_REUSE_SERVER=1` reuses a server already listening there, and `LUXEL_WEBGPU_E2E_HEADED=1` launches headed Chromium. Failure screenshots and traces are written under `artifacts/gallery-browser-e2e`.
