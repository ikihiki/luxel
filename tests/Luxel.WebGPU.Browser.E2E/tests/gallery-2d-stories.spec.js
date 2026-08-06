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

const pipelineStateStories = [
  'Examples/3D/PipelineState/Topology',
  'Examples/3D/PipelineState/Rasterizer',
  'Examples/3D/PipelineState/Depth',
  'Examples/3D/PipelineState/Blend',
  'Examples/3D/PipelineState/Stencil',
  'Examples/3D/PipelineState/ViewportScissor',
  'Examples/3D/PipelineState/Separation',
  'Examples/3D/Depth',
  'Examples/3D/Blend'
];

const animationStories = [
  'Examples/Animation/Curves',
  'Examples/Animation/Tween',
  'Examples/Animation/CssKeyframes',
  'Examples/Animation/StateMachine',
  'Examples/Animation/EcsClip',
  'Examples/Animation/Graph'
];

const animationGpuStories = new Set([
  'Examples/Animation/CssKeyframes',
  'Examples/Animation/StateMachine',
  'Examples/Animation/EcsClip',
  'Examples/Animation/Graph'
]);

const animationMotionStories = new Set(animationStories.filter(story => story !== 'Examples/Animation/StateMachine'));

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

for (const story of pipelineStateStories) {
  test(`browser-WASM renders ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(`/samples/webgpu-browser/?story=${encodeURIComponent(story)}`);
    await expectRuntimeStory(page, story);
    await expect.poll(() => page.evaluate(() =>
      globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''),
      { timeout: 90_000 }).toContain('Ready');
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

for (const story of animationStories) {
  test(`browser-WASM renders ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(`/samples/webgpu-browser/?story=${encodeURIComponent(story)}`);
    await expectRuntimeStory(page, story);
    if (animationGpuStories.has(story)) {
      await expect.poll(() => page.evaluate(() =>
        globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''),
        { timeout: 90_000 }).toContain('Ready');
    }
    if (animationMotionStories.has(story)) {
      const canvas = page.locator('#luxel-canvas');
      const firstRevision = await page.evaluate(() => globalThis.luxelBrowserState.renderRevision);
      await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState.renderRevision),
        { timeout: 30_000 }).toBeGreaterThan(firstRevision + 2);
      const before = await canvas.screenshot();
      const secondRevision = await page.evaluate(() => globalThis.luxelBrowserState.renderRevision);
      await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState.renderRevision),
        { timeout: 30_000 }).toBeGreaterThan(secondRevision + 2);
      const after = await canvas.screenshot();
      expect(before.equals(after), `${story} should visibly animate`).toBe(false);
    }
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

test('browser-WASM StateMachine responds to press and done triggers', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto(`/samples/webgpu-browser/?story=${encodeURIComponent('Examples/Animation/StateMachine')}`);
  await expectRuntimeStory(page, 'Examples/Animation/StateMachine');

  const canvas = page.locator('#luxel-canvas');
  const idle = await canvas.screenshot();
  const press = await page.evaluate(() => globalThis.luxelBrowserState.widgets
    .find(widget => widget.type?.endsWith('.Button') && widget.detail === 'press'));
  expect(press).toBeTruthy();
  await page.mouse.click(press.x + press.width / 2, press.y + press.height / 2);
  const pressRevision = await page.evaluate(() => globalThis.luxelBrowserState.renderRevision);
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState.renderRevision),
    { timeout: 30_000 }).toBeGreaterThan(pressRevision + 2);
  const jumping = await canvas.screenshot();
  expect(idle.equals(jumping), 'press should change the StateMachine rendering').toBe(false);

  const done = await page.evaluate(() => globalThis.luxelBrowserState.widgets
    .find(widget => widget.type?.endsWith('.Button') && widget.detail === 'done'));
  expect(done).toBeTruthy();
  await page.mouse.click(done.x + done.width / 2, done.y + done.height / 2);
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState.events
    .filter(entry => String(entry.message || entry).includes('done')).length),
    { timeout: 30_000 }).toBeGreaterThan(0);

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});

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

test('exported Animation lesson boots its browser-WASM live sample through an iframe', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto('/index.html#story=Learn%2FAnimation%2FCurvesAndTweens');

  const frame = page.locator('iframe[data-luxel-runtime-story="Examples/Animation/Curves"]');
  await expect(frame).toBeVisible();
  await expectRuntimeStory(frame.contentFrame(), 'Examples/Animation/Curves');

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});
