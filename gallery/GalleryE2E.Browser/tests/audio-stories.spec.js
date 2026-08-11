const { test, expect } = require('@playwright/test');
const {
  galleryStoryPath,
  collectPageFailures,
  expectRuntimeStory,
  clickCanvasWidget,
  expectWidgetDetail
} = require('@luxel/browser-e2e-support/gallery').createGalleryHelpers(expect);

const audioStories = [
  'Examples/Audio/BackendLifecycle',
  'Examples/Audio/WaveformAndVoice',
  'Examples/Audio/Buses',
  'Examples/Audio/SpatialAttenuation',
  'Examples/Audio/StreamingQueue'
];

const runtimeUrl = galleryStoryPath;
const collectErrors = collectPageFailures;
const clickWidget = (page, detail) => clickCanvasWidget(page, { detail });

for (const story of audioStories) {
  test(`browser-WASM boots ${story}`, async ({ page }) => {
    const errors = collectErrors(page);
    await page.goto(runtimeUrl(story));
    await expectRuntimeStory(page, story);
    expect(errors.consoleErrors).toEqual([]);
    expect(errors.pageErrors).toEqual([]);
  });
}

test('Web Audio lifecycle resumes and suspends from Gallery buttons', async ({ page }) => {
  const errors = collectErrors(page);
  const story = 'Examples/Audio/BackendLifecycle';
  await page.goto(runtimeUrl(story));
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
  await page.goto(runtimeUrl(story));
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

  await page.goto(runtimeUrl('Examples/Audio/Buses'));
  await expectRuntimeStory(page, 'Examples/Audio/Buses');
  await clickWidget(page, 'loopを再生');
  await expectWidgetDetail(page, 'voice 30%');
  await clickWidget(page, 'Music 15%');
  await expectWidgetDetail(page, 'voice 8%');

  await page.goto(runtimeUrl('Examples/Audio/SpatialAttenuation'));
  await expectRuntimeStory(page, 'Examples/Audio/SpatialAttenuation');
  await clickWidget(page, '右・遠い');
  await expectWidgetDetail(page, 'gain=0.25 / pan=+1.00');

  await page.goto(runtimeUrl('Examples/Audio/StreamingQueue'));
  await expectRuntimeStory(page, 'Examples/Audio/StreamingQueue');
  await clickWidget(page, '3 chunkを再生');
  await expectWidgetDetail(page, '330 → 440 → 660 Hz / queued=3 / playing=True');
  await clickWidget(page, '停止');
  await expectWidgetDetail(page, '停止してqueueを破棄しました。');

  expect(errors.consoleErrors).toEqual([]);
  expect(errors.pageErrors).toEqual([]);
});
