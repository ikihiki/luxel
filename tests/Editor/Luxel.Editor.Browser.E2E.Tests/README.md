# Luxel Editor browser E2E

Playwright acceptance coverage for `samples/LuxelEditorBrowser`. The suite uses the stable `globalThis.luxelEditorAutomation` JSON contract instead of canvas coordinates or localized display text.

## Run

```bash
dotnet publish samples/LuxelEditorBrowser/LuxelEditorBrowser.csproj \
  --configuration Release \
  --output artifacts/editor-browser

dotnet build tests/Editor/Luxel.Editor.Browser.E2E.Tests/Luxel.Editor.Browser.E2E.Tests.csproj \
  --configuration Release
pwsh tests/Editor/Luxel.Editor.Browser.E2E.Tests/bin/Release/net10.0/playwright.ps1 install chromium
LUXEL_E2E_SOFTWARE_GPU=1 dotnet test \
  tests/Editor/Luxel.Editor.Browser.E2E.Tests/Luxel.Editor.Browser.E2E.Tests.csproj \
  --configuration Release --no-build
```

The checked-in run settings use Chromium SwiftShader because the dedicated GitHub-hosted runner has no hardware GPU. This is an explicit software-GPU acceptance run, not hardware validation. Set `LUXEL_EDITOR_BROWSER_ROOT` to test a previously published `wwwroot` directory.
