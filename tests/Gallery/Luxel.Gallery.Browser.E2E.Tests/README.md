# Luxel Gallery Browser E2E

This xUnit project follows the Playwright .NET xUnit runner pattern. Focused story classes derive from `PageTest`, which reuses Playwright/browser workers while giving every test a fresh browser context and page. xUnit classes run concurrently; methods within a class remain sequential.

The 87 discoverable tests cover representative category stories, Markdown iframes, WebGPU diagnostics, canvas interactions, shared Args/Output/Source panels, Audio, Resources, and browser scripting.

From the repository root:

```bash
dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release -o artifacts/gallery-browser
dotnet build tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/Luxel.Gallery.Browser.E2E.Tests.csproj -c Release
pwsh tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
dotnet test tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/Luxel.Gallery.Browser.E2E.Tests.csproj -c Release --no-build
```

`GalleryBrowser.runsettings` is selected automatically by the project. It configures Chromium with deterministic SwiftShader WebGPU arguments, a 90-second Playwright assertion timeout, and two xUnit class workers to limit software-GPU oversubscription.

`GalleryTestHost` starts one process-wide Python static server lazily and safely when test classes initialize concurrently. The static root defaults to `artifacts/gallery-browser/wwwroot`; override it with `LUXEL_GALLERY_BROWSER_ROOT`. `LUXEL_WEBGPU_E2E_PORT` selects the static-server port, and `LUXEL_WEBGPU_E2E_REUSE_SERVER=1` reuses a server already listening there.
