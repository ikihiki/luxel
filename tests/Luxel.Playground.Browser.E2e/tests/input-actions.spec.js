const { test, expect } = require('@playwright/test');

const detail = (page, prefix) => page.evaluate(prefix =>
  globalThis.luxelBrowserState?.widgets?.find(widget => widget.detail?.startsWith(prefix))?.detail || '', prefix);

test('runs canonical input actions through the Gallery WebAssembly runtime', async ({ page }) => {
  const consoleErrors = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => consoleErrors.push(String(error)));

  await page.setViewportSize({ width: 680, height: 430 });
  await page.goto('/samples/webgpu-browser/?story=Examples%2FInput%2FWindowActions');
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.state),
    { timeout: 90_000 }).toBe('pass');

  const canvas = page.locator('#luxel-canvas');
  await canvas.click({ position: { x: 40, y: 40 } });

  await page.keyboard.down('w');
  await expect.poll(() => detail(page, 'Move:')).toContain('1.00');
  await page.keyboard.up('w');
  await expect.poll(() => detail(page, 'Move:')).toContain('0.00, 0.00');

  await canvas.hover({ position: { x: 300, y: 180 } });
  await page.mouse.down();
  await expect.poll(() => detail(page, 'Fire:')).toContain('held');
  await page.mouse.up();
  await expect.poll(() => detail(page, 'Pressed:')).toContain('Released: 1');

  await page.keyboard.down('d');
  await expect.poll(() => detail(page, 'Move:')).toContain('1.00, 0.00');
  await canvas.evaluate(element => element.blur());
  await expect.poll(() => detail(page, 'Move:')).toContain('0.00, 0.00');
  await page.keyboard.up('d');

  expect(consoleErrors).toEqual([]);
});
