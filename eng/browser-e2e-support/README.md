# @luxel/browser-e2e-support

Private support package for browser E2E suites in this repository.

- `config`: deterministic Chromium + SwiftShader Playwright configuration for browser-WASM suites.
- `gallery`: Gallery story URLs, runtime/WebGPU assertions, page-failure collection, and canvas-widget interaction.

The package receives Playwright's `defineConfig` and `expect` from the consumer so a local `file:` dependency never loads a second Playwright instance.

```js
const { test, expect } = require('@playwright/test');
const { createGalleryHelpers } = require('@luxel/browser-e2e-support/gallery');
const { gotoGalleryStory, clickCanvasWidget } = createGalleryHelpers(expect);

test('runs a story', async ({ page }) => {
  await gotoGalleryStory(page, 'Examples/Scripting/LiveCsx');
  await clickCanvasWidget(page, { detail: 'Run' });
});
```
