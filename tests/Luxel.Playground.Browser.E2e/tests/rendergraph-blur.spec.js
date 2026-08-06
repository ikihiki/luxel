const { test, expect } = require('@playwright/test');

test('runs the RenderGraph Blur story through the WebAssembly runtime', async ({ page }) => {
  const consoleErrors = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => consoleErrors.push(String(error)));

  await page.setViewportSize({ width: 320, height: 320 });
  await page.goto('/samples/webgpu-browser/?story=Examples%2FRenderGraph%2FBlur');
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.state),
    { timeout: 90_000 }).toBe('pass');
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.renderRevision),
    { timeout: 60_000 }).toBeGreaterThanOrEqual(2);

  await expect(page.locator('#luxel-canvas')).toBeVisible();
  expect(consoleErrors).toEqual([]);
});
