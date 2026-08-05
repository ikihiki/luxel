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

  const coloredSamples = await page.locator('#luxel-canvas').evaluate(async source => {
    const image = new Image();
    image.src = source.toDataURL('image/png');
    await image.decode();
    const copy = document.createElement('canvas');
    copy.width = source.width;
    copy.height = source.height;
    const context = copy.getContext('2d', { willReadFrequently: true });
    context.drawImage(image, 0, 0);
    const pixels = context.getImageData(0, 0, copy.width, copy.height).data;
    let colored = 0;
    for (let y = 0; y < copy.height; y += 8) {
      for (let x = 0; x < copy.width; x += 8) {
        const offset = (y * copy.width + x) * 4;
        const red = pixels[offset], green = pixels[offset + 1], blue = pixels[offset + 2];
        if (pixels[offset + 3] > 200 && Math.max(red, green, blue) - Math.min(red, green, blue) > 20) colored++;
      }
    }
    return colored;
  });

  expect(coloredSamples).toBeGreaterThan(100);
  expect(consoleErrors).toEqual([]);
});
