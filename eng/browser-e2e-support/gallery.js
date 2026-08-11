let expect;

function createGalleryHelpers(playwrightExpect) {
  expect = playwrightExpect;
  return module.exports;
}

const galleryStoryPath = (story, { embed = true } = {}) =>
  `/?story=${encodeURIComponent(story)}${embed ? '&embed=1' : ''}`;

function collectPageFailures(page, { responses = false } = {}) {
  const failures = { consoleErrors: [], pageErrors: [], failedResponses: [] };
  page.on('console', message => {
    if (message.type() === 'error') failures.consoleErrors.push(message.text());
  });
  page.on('pageerror', error => failures.pageErrors.push(String(error?.stack || error)));
  if (responses) {
    page.on('response', response => {
      if (response.status() >= 400) failures.failedResponses.push(`${response.status()} ${response.url()}`);
    });
  }
  return failures;
}

async function expectNoPageFailures(failures) {
  expect(failures.consoleErrors).toEqual([]);
  expect(failures.pageErrors).toEqual([]);
  expect(failures.failedResponses).toEqual([]);
}

async function expectRuntimeStory(target, story, {
  webGpu = false,
  gpuView = false,
  statusText = false,
  noCapabilityFallback = false
} = {}) {
  const status = target.locator('#status');
  await expect(status).toHaveAttribute('data-story', story, { timeout: 90_000 });
  await expect(status).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  if (statusText) await expect(status).toContainText(`story=${story}`);
  await expect(target.locator('#error')).toBeHidden();
  await expect(target.locator('#luxel-canvas')).toBeVisible();
  const root = target.locator('html');
  await expect.poll(() => root.evaluate(() => globalThis.luxelBrowserState?.renderRevision || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
  await expect.poll(() => root.evaluate(() => globalThis.luxelBrowserState?.widgets?.length || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
  if (noCapabilityFallback) {
    await expect.poll(() => root.evaluate(() =>
      globalThis.luxelBrowserState?.widgets?.some(widget => widget.type?.endsWith('.StoryCapabilityFallback')) || false))
      .toBe(false);
  }
  if (gpuView) {
    await expect.poll(() => root.evaluate(() =>
      globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''),
      { timeout: 90_000 }).toContain('Ready');
  }
  if (webGpu) {
    const gpu = await root.evaluate(() => globalThis.luxelBrowserState?.webGpu);
    expect(gpu?.adapter).toBeTruthy();
    expect(gpu?.device?.status).toBe('ready');
    expect(gpu?.surface?.presentCount).toBeGreaterThan(0);
    expect(gpu?.lastError).toBeNull();
  }
}

async function gotoGalleryStory(page, story, options = {}) {
  await page.goto(galleryStoryPath(story, options));
  await expectRuntimeStory(page, story, options);
}

async function findCanvasWidget(page, { detail, type = 'Button', index = 0 }) {
  const query = { detail, type, index };
  await expect.poll(() => page.evaluate(({ detail, type, index }) =>
    globalThis.luxelBrowserState?.widgets?.filter(widget =>
      widget.type?.endsWith(`.${type}`) && (detail === undefined || widget.detail === detail))[index] || null,
  query), { timeout: 30_000 }).not.toBeNull();
  return page.evaluate(({ detail, type, index }) => globalThis.luxelBrowserState.widgets.filter(widget =>
    widget.type?.endsWith(`.${type}`) && (detail === undefined || widget.detail === detail))[index], query);
}

async function clickCanvasWidget(page, options) {
  const widget = await findCanvasWidget(page, options);
  await page.locator('#luxel-canvas').click({
    position: { x: widget.x + widget.width / 2, y: widget.y + widget.height / 2 }
  });
}

async function expectWidgetDetail(page, text, { timeout = 90_000 } = {}) {
  await expect.poll(() => page.evaluate(expected =>
    globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail?.includes(expected)) || false, text),
  { timeout }).toBe(true);
}

module.exports = {
  createGalleryHelpers,
  galleryStoryPath,
  collectPageFailures,
  expectNoPageFailures,
  expectRuntimeStory,
  gotoGalleryStory,
  clickCanvasWidget,
  expectWidgetDetail
};
