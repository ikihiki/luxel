const { test, expect } = require('@playwright/test');

const twoDStories = [
  'Examples/2D/SceneRender',
  'Examples/2D/Shapes',
  'Examples/2D/VectorPaths',
  'Examples/2D/CameraRig',
  'Examples/2D/Sprites',
  'Examples/2D/Rasterizer/InputPathsLive',
  'Examples/2D/Rasterizer/EncodedSceneLive',
  'Examples/2D/Rasterizer/BoundsLive',
  'Examples/2D/Rasterizer/TileBinsLive',
  'Examples/2D/Rasterizer/CoverageLive',
  'Examples/2D/Rasterizer/StrokeLive',
  'Examples/2D/Rasterizer/CompositeLive',
  'Examples/2D/Rasterizer/DispatchLive',
  'Examples/2D/Rasterizer/RetainedUpdatesLive'
];

function collectErrors(page) {
  const consoleErrors = [];
  const pageErrors = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => pageErrors.push(String(error?.stack || error)));
  return { consoleErrors, pageErrors };
}

async function expectRuntimeStory(frame, story) {
  const status = frame.locator('#status');
  await expect(status).toHaveAttribute('data-story', story, { timeout: 90_000 });
  await expect(status).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect(status).toContainText(`story=${story}`);
  await expect(frame.locator('#error')).toBeHidden();
  await expect(frame.locator('#luxel-canvas')).toBeVisible();

  const documentRoot = frame.locator('html');
  await expect.poll(() => documentRoot.evaluate(() => globalThis.luxelBrowserState?.renderRevision || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
  await expect.poll(() => documentRoot.evaluate(() => globalThis.luxelBrowserState?.widgets?.length || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
}

for (const story of twoDStories) {
  test(`browser-WASM renders ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(`/samples/webgpu-browser/?story=${encodeURIComponent(story)}`);
    await expectRuntimeStory(page, story);
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

test('exported 2D overview boots its browser-WASM live samples through iframes', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/index.html#story=Learn%2FGraphics%2F2D%2FOverview');

  const frames = page.locator('iframe[data-luxel-runtime-story]');
  await expect(frames.first()).toBeVisible();
  const count = await frames.count();
  expect(count).toBeGreaterThan(0);

  for (let index = 0; index < count; index++) {
    const iframe = frames.nth(index);
    const story = await iframe.getAttribute('data-luxel-runtime-story');
    expect(twoDStories).toContain(story);
    await expectRuntimeStory(iframe.contentFrame(), story);
  }

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});
