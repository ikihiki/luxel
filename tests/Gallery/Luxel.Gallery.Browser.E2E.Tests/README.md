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

The Luxel Dev Container already includes PowerShell, the matching Chromium revision, and its Linux
dependencies. The install command therefore completes without downloading in the normal case, but
also updates the shared browser directory when this project moves to a newer Playwright version.

`PlaywrightEnvironmentTests` launches Chromium and renders a standalone in-memory page. Keep this
test independent of authored stories so the container and browser integration remains verifiable
even while gallery content changes.

`GalleryBrowser.runsettings` is selected automatically by the project. It configures Chromium with deterministic SwiftShader WebGPU arguments, a 90-second Playwright assertion timeout, and two xUnit class workers to limit software-GPU oversubscription.

`GalleryTestHost` starts one process-wide Python static server lazily and safely when test classes initialize concurrently. The static root defaults to `artifacts/gallery-browser/wwwroot`; override it with `LUXEL_GALLERY_BROWSER_ROOT`. `LUXEL_WEBGPU_E2E_PORT` selects the static-server port, and `LUXEL_WEBGPU_E2E_REUSE_SERVER=1` reuses a server already listening there.
