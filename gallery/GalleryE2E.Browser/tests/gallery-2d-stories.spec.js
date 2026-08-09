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

const ecsStories = [
  'Examples/3D/EcsCubes',
  'Examples/3D/PhysicsFalling',
  'Examples/3D/PhysicsPlayground',
  'Examples/3D/PhysicsGizmos',
  'Examples/3D/PhysicsTrigger',
  'Examples/3D/PhysicsMesh'
];

const ecsGpuViewStories = new Set([
  'Examples/3D/EcsCubes',
  'Examples/3D/PhysicsFalling',
  'Examples/3D/PhysicsPlayground'
]);

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
  'Examples/Animation/StateMachine'
]);

const animationMotionStories = new Set(animationStories.filter(story => story !== 'Examples/Animation/StateMachine'));

const runtimeUrl = story => `/?story=${encodeURIComponent(story)}&embed=1`;

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
  const webGpu = await documentRoot.evaluate(() => globalThis.luxelBrowserState?.webGpu);
  expect(webGpu?.adapter).toBeTruthy();
  expect(typeof webGpu.adapter.isFallbackAdapter).toBe('boolean');
  expect(webGpu?.device?.status).toBe('ready');
  expect(webGpu?.surface?.configured).toBe(true);
  expect(webGpu?.surface?.presentCount).toBeGreaterThan(0);
  expect(webGpu?.lastError).toBeNull();
}

test('Blazor Gallery renders generated Markdown overviews as HTML with navigation and search', async ({ page }) => {
  const story = 'Controls/Accordion/Overview';
  const errors = collectErrors(page);
  await page.goto(`/?story=${encodeURIComponent(story)}`);

  await expect(page.locator('.gallery-sidebar')).toBeVisible();
  await expect(page.locator('.story-link.active')).toHaveText(/Overview/);
  await expect(page.locator('.story-tree summary').filter({ hasText: 'Accordion' })).toBeVisible();
  await expect(page.locator('.markdown-document h1')).toHaveText('Accordion');
  await expect(page.locator('.markdown-document')).toContainText('Implementation');
  await expect(page.locator('.markdown-story-embed iframe')).toHaveCount(1);
  const embedded = page.frameLocator('.markdown-story-embed iframe');
  await expect(embedded.getByRole('tab', { name: '引数' })).toBeVisible();
  await expect(embedded.getByRole('tab', { name: '出力' })).toBeVisible();
  await expect(embedded.getByRole('tab', { name: 'ソース' })).toBeVisible();
  await expect(embedded.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect(embedded.locator('#status')).toHaveAttribute('data-story', 'Controls/Accordion/Basic');

  const search = page.getByRole('searchbox', { name: 'Storyを検索' });
  await search.fill('Accordion');
  await expect(page.locator('.story-link')).toHaveCount(2);
  await expect(page.locator('.story-link')).toContainText(['Overview', 'Basic']);

  await page.locator('.story-link[title="Controls/Accordion/Basic"]').click();
  await expect(page).toHaveURL(/story=Controls%2FAccordion%2FBasic/);
  await expect(search).toHaveValue('Accordion');
  await expect(page.locator('.story-toolbar h1')).toHaveText('Basic');
  await expect(page.locator('.gallery-sidebar')).toBeVisible();
  const runtime = page.frameLocator('.story-runtime-frame');
  await expect(runtime.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect(runtime.locator('#status')).toHaveAttribute('data-story', 'Controls/Accordion/Basic');

  await page.goBack();
  await expect(page).toHaveURL(/story=Controls%2FAccordion%2FOverview/);
  await expect(search).toHaveValue('Accordion');
  await expect(page.locator('.markdown-document h1')).toHaveText('Accordion');

  await search.fill('no-such-luxel-story');
  await expect(page.locator('.empty-search')).toBeVisible();

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});

test('Blazor Gallery exposes Args, Output, Source, and a resizable preview panel for widget stories', async ({ page }) => {
  const story = 'Controls/Button/Counter';
  const errors = collectErrors(page);
  await page.goto(`/?story=${encodeURIComponent(story)}`);

  const runtime = page.frameLocator('.story-runtime-frame');
  await expect(runtime.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect(page.getByRole('tab', { name: '引数' })).toHaveAttribute('aria-selected', 'true');
  const count = page.locator('#story-arg-count');
  await expect(count).toHaveValue('0');
  await count.fill('7');
  await count.blur();
  await expect.poll(() => runtime.locator('html').evaluate(() => globalThis.luxelBrowserState?.count), {
    timeout: 30_000
  }).toBe(7);

  await page.getByRole('tab', { name: '出力' }).click();
  await expect(page.locator('.output-list')).toContainText('引数を変更しました');
  await page.getByRole('tab', { name: 'ソース' }).click();
  await expect(page.locator('.story-source')).toContainText('ButtonCounter');

  const splitter = page.getByRole('separator', { name: 'Storyプレビューと詳細の大きさを変更' });
  const panel = page.locator('.story-lower-panel');
  const before = await panel.boundingBox();
  const handle = await splitter.boundingBox();
  expect(before).toBeTruthy();
  expect(handle).toBeTruthy();
  await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
  await page.mouse.down();
  await page.mouse.move(handle.x + handle.width / 2, handle.y - 70, { steps: 4 });
  await page.mouse.up();
  const after = await panel.boundingBox();
  expect(after.height).toBeGreaterThan(before.height + 40);
  expect(errors.pageErrors).toEqual([]);

  await page.locator('.story-link[title="Controls/Button/Primary"]').click();
  await expect(page.locator('.story-toolbar h1')).toHaveText('Primary');
  await expect(runtime.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect(runtime.locator('#status')).toHaveAttribute('data-story', 'Controls/Button/Primary');
  await expect(page.getByRole('tab', { name: 'ソース' })).toHaveAttribute('aria-selected', 'true');
  await page.getByRole('tab', { name: '出力' }).click();
  await expect(page.locator('.output-list')).toHaveCount(0);
});

test('compact embedded stories expose interactive Args, Output, and Source panels', async ({ page }) => {
  const story = 'Controls/Button/Counter';
  await page.goto(`/?story=${encodeURIComponent(story)}&compact=1`);

  await expect(page.locator('.gallery-compact')).toBeVisible();
  await expect(page.locator('.gallery-sidebar')).toHaveCount(0);
  await expect(page.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });

  const count = page.locator('#story-arg-count');
  await count.fill('4');
  await count.blur();
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.count), {
    timeout: 30_000
  }).toBe(4);

  await page.getByRole('tab', { name: '出力' }).click();
  await expect(page.locator('.output-list')).toContainText('引数を変更しました');
  await page.getByRole('tab', { name: 'ソース' }).click();
  await expect(page.locator('.story-source')).toContainText('ButtonCounter');
});

test('embedded widget stories remain canvas-only', async ({ page }) => {
  await page.goto(runtimeUrl('Controls/Button/Counter'));
  await expect(page.locator('.gallery-embed')).toBeVisible();
  await expect(page.locator('.gallery-sidebar')).toHaveCount(0);
  await expect(page.getByRole('tab')).toHaveCount(0);
  await expect(page.getByRole('separator')).toHaveCount(0);
});

for (const story of twoDStories) {
  test(`browser-WASM renders ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(runtimeUrl(story));
    await expectRuntimeStory(page, story);
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

for (const story of ecsStories) {
  test(`browser-WASM renders ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(runtimeUrl(story));
    await expectRuntimeStory(page, story);
    if (ecsGpuViewStories.has(story)) {
      await expect.poll(() => page.evaluate(() =>
        globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''),
        { timeout: 90_000 }).toContain('Ready');
    }
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

for (const story of pipelineStateStories) {
  test(`browser-WASM renders ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(runtimeUrl(story));
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
    await page.goto(runtimeUrl(story));
    await expectRuntimeStory(page, story);
    if (animationGpuStories.has(story)) {
      await expect.poll(() => page.evaluate(() =>
        globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''),
        { timeout: 90_000 }).toContain('Ready');
    }
    if (animationMotionStories.has(story)) {
      for (let sample = 0; sample < 4; sample++) {
        const revision = await page.evaluate(() => globalThis.luxelBrowserState.renderRevision);
        await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState.renderRevision),
          { timeout: 30_000 }).toBeGreaterThan(revision + 9);
      }
      // SwiftShader with --disable-vulkan-surface can present fresh frames while headless screenshots
      // remain stale, so render revision progress is the deterministic animation contract.
    }
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

test('browser-WASM StateMachine responds to press and done triggers', async ({ page }) => {
  const errors = collectErrors(page);
  await page.goto(runtimeUrl('Examples/Animation/StateMachine'));
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
