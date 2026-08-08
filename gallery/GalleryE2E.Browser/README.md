# Luxel Gallery Browser E2E

This Playwright suite launches the statically published Blazor Gallery in Chromium. Local runs prefer Chromium's accessible hardware WebGPU adapter; set `LUXEL_E2E_SOFTWARE_GPU=1` to use the deterministic SwiftShader fallback. It verifies browser-registered Graphics stories and their embedded iframes, plus interactive Audio/Web Audio stories including lifecycle, tone, bus, spatial, and queued-buffer controls.

## Run locally

Run the dedicated browser E2E project from the repository root:

```bash
cd gallery/GalleryE2E.Browser
npm ci
npx playwright install --with-deps chromium
npm run run
```

`npm run run` publishes `GalleryBrowser` and then executes the Playwright suite directly against its static `wwwroot`. When the Gallery artifacts are already prepared, use `npm test` to run only Playwright.
