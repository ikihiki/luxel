# Luxel Gallery Browser E2E

This project keeps the Playwright host and browser configuration available for gallery-level smoke tests that do not depend on authored stories.

Do not add tests for fixed story routes, story text, story counts, source panels, or the implementation of a production story. Authored stories are intentionally free to change without requiring test updates. Story framework behavior belongs in focused unit tests that build synthetic `StoryInfo` values.

From the repository root:

```bash
dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release -o artifacts/gallery-browser
dotnet build tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/Luxel.Gallery.Browser.E2E.Tests.csproj -c Release
pwsh tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
dotnet test tests/Gallery/Luxel.Gallery.Browser.E2E.Tests/Luxel.Gallery.Browser.E2E.Tests.csproj -c Release --no-build
```

`GalleryBrowser.runsettings` is selected automatically by the project. It configures Chromium with deterministic SwiftShader WebGPU arguments, a 90-second Playwright assertion timeout, and two xUnit class workers to limit software-GPU oversubscription.

`.github/workflows/test-gallery-browser-e2e.yml` is the CI entry point. It restores and publishes the Gallery, builds this project, installs Chromium with the generated Playwright script, and preserves `playwright-report`, `test-results` (including teardown screenshots), and test logs on failure. This is explicitly deterministic software-GPU coverage; it does not claim hardware WebGPU validation. A hardware-preferred job remains a follow-up for a runner that exposes a usable browser GPU.

`GalleryTestHost` starts one process-wide Python static server lazily and safely when test classes initialize concurrently. The static root defaults to `artifacts/gallery-browser/wwwroot`; override it with `LUXEL_GALLERY_BROWSER_ROOT`. `LUXEL_WEBGPU_E2E_PORT` selects the static-server port, and `LUXEL_WEBGPU_E2E_REUSE_SERVER=1` reuses a server already listening there.
