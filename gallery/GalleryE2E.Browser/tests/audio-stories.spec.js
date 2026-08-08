const { test, expect } = require('@playwright/test');

const audioStories = [
  'Examples/Audio/BackendLifecycle',
  'Examples/Audio/WaveformAndVoice',
  'Examples/Audio/Buses',
  'Examples/Audio/SpatialAttenuation',
  'Examples/Audio/StreamingQueue'
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

async function expectRuntimeStory(page, story) {
  const status = page.locator('#status');
  await expect(status).toHaveAttribute('data-story', story, { timeout: 90_000 });
  await expect(status).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.widgets?.length || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
}

async function clickWidget(page, detail) {
  await expect.poll(() => page.evaluate(label =>
    globalThis.luxelBrowserState?.widgets?.find(value =>
      value.type?.endsWith('.Button') && value.detail === label) || null, detail), {
    timeout: 30_000
  }).not.toBeNull();
  const match = await page.evaluate(label => globalThis.luxelBrowserState.widgets.find(value =>
    value.type?.endsWith('.Button') && value.detail === label), detail);
  const canvas = page.locator('#luxel-canvas');
  await canvas.click({ position: { x: match.x + match.width / 2, y: match.y + match.height / 2 } });
}

async function expectWidgetDetail(page, expected) {
  await expect.poll(() => page.evaluate(text =>
    globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail?.includes(text)) || false, expected), {
    timeout: 30_000
  }).toBe(true);
}

for (const story of audioStories) {
  test(`browser-WASM boots ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(`/?story=${encodeURIComponent(story)}`);
    await expectRuntimeStory(page, story);
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

test('Web Audio lifecycle resumes and suspends from Gallery buttons', async ({ page }) => {
  const errors = collectErrors(page);
  const story = 'Examples/Audio/BackendLifecycle';
  await page.goto(`/?story=${encodeURIComponent(story)}`);
  await expectRuntimeStory(page, story);

  await clickWidget(page, 'Audioを有効化');
  await expectWidgetDetail(page, 'ResumeAsync完了: Running');
  await clickWidget(page, 'Audioを一時停止');
  await expectWidgetDetail(page, 'SuspendAsync完了: Suspended');

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});

test('Web Audio tone submits, plays, and clears its queue', async ({ page }) => {
  const errors = collectErrors(page);
  const story = 'Examples/Audio/WaveformAndVoice';
  await page.goto(`/?story=${encodeURIComponent(story)}`);
  await expectRuntimeStory(page, story);

  await clickWidget(page, '440 Hzを再生');
  await expectWidgetDetail(page, '再生中: 440 Hz / queued=1 / playing=True');
  await clickWidget(page, '停止');
  await expectWidgetDetail(page, '停止しました。queueは破棄されます。');

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});

test('Web Audio bus, spatial, and queued-buffer controls update observable state', async ({ page }) => {
  const errors = collectErrors(page);

  await page.goto(`/?story=${encodeURIComponent('Examples/Audio/Buses')}`);
  await expectRuntimeStory(page, 'Examples/Audio/Buses');
  await clickWidget(page, 'loopを再生');
  await expectWidgetDetail(page, 'voice 30%');
  await clickWidget(page, 'Music 15%');
  await expectWidgetDetail(page, 'voice 8%');

  await page.goto(`/?story=${encodeURIComponent('Examples/Audio/SpatialAttenuation')}`);
  await expectRuntimeStory(page, 'Examples/Audio/SpatialAttenuation');
  await clickWidget(page, '右・遠い');
  await expectWidgetDetail(page, 'gain=0.25 / pan=+1.00');

  await page.goto(`/?story=${encodeURIComponent('Examples/Audio/StreamingQueue')}`);
  await expectRuntimeStory(page, 'Examples/Audio/StreamingQueue');
  await clickWidget(page, '3 chunkを再生');
  await expectWidgetDetail(page, '330 → 440 → 660 Hz / queued=3 / playing=True');
  await clickWidget(page, '停止');
  await expectWidgetDetail(page, '停止してqueueを破棄しました。');

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});
