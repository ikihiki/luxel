const { test, expect } = require('@playwright/test');

const runtimeUrl = story => `/?story=${encodeURIComponent(story)}&embed=1`;

function collectFailures(page) {
  const consoleErrors = [];
  const pageErrors = [];
  const failedResponses = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  page.on('pageerror', error => pageErrors.push(String(error?.stack || error)));
  page.on('response', response => {
    if (response.status() >= 400) failedResponses.push(`${response.status()} ${response.url()}`);
  });
  return { consoleErrors, pageErrors, failedResponses };
}

async function expectRuntimeStory(page, story, { gpu = false } = {}) {
  const status = page.locator('#status');
  await expect(status).toHaveAttribute('data-story', story, { timeout: 90_000 });
  await expect(status).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect(page.locator('#error')).toBeHidden();
  await expect(page.locator('#luxel-canvas')).toBeVisible();
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.renderRevision || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.widgets?.length || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
  if (gpu) {
    await expect.poll(() => page.evaluate(() =>
      globalThis.luxelBrowserState?.widgets?.find(widget => widget.type?.endsWith('.GpuView'))?.detail || ''),
      { timeout: 90_000 }).toContain('Ready');
    const webGpu = await page.evaluate(() => globalThis.luxelBrowserState?.webGpu);
    expect(webGpu?.adapter).toBeTruthy();
    expect(webGpu?.device?.status).toBe('ready');
    expect(webGpu?.surface?.presentCount).toBeGreaterThan(0);
    expect(webGpu?.lastError).toBeNull();
  }
}

async function expectNoFailures(failures) {
  expect(failures.consoleErrors).toEqual([]);
  expect(failures.pageErrors).toEqual([]);
  expect(failures.failedResponses).toEqual([]);
}

test('ResourceSystem Learn renders TOC, course navigation, live examples, and Back navigation', async ({ page }) => {
  const failures = collectFailures(page);
  const story = 'Learn/Resources/LoadingAndHandles';
  await page.goto(`/?story=${encodeURIComponent(story)}`);

  await expect(page.locator('.markdown-document h1')).toHaveText('読み込みとResourceHandle');
  await expect(page.locator('.markdown-document a[href^="#"]').filter({ hasText: 'ResourceSystemを構築する' })).toBeVisible();
  await expect(page.locator('.markdown-document a[href*="Learn%2FResources%2FOverview"]')).toContainText('Resources学習ガイド');
  await expect(page.locator('.markdown-document a[href*="Learn%2FResources%2FSourcesAndUris"]')).toContainText('SourceとリソースURI');

  const embeds = page.locator('.markdown-story-embed');
  await expect(embeds).toHaveCount(1);
  await expect(embeds.locator('header')).toContainText('Examples/Resources/HelloTextAsset');
  const embedded = page.frameLocator('.markdown-story-embed iframe').first();
  await expect(embedded.getByRole('tab', { name: '引数' })).toBeVisible();
  await embedded.getByRole('tab', { name: 'ソース' }).click();
  await expect(embedded.locator('.story-source')).toContainText('resources.Load<TextAsset>');
  await expect(embedded.locator('.story-source')).not.toContainText('ResourceScenarios.Create');
  await expect(embedded.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await embedded.getByRole('tab', { name: '出力' }).click();
  await expect(embedded.locator('.output-list')).toContainText('テキストアセットの読み込み: 準備完了');

  await embeds.nth(0).getByRole('link', { name: 'Storyを開く' }).click();
  await expect(page).toHaveURL(/story=Examples%2FResources%2FHelloTextAsset/);
  await expect(page.locator('.story-toolbar h1')).toHaveText('HelloTextAsset');
  await page.goBack();
  await expect(page).toHaveURL(/story=Learn%2FResources%2FLoadingAndHandles/);
  await expect(page.locator('.markdown-document h1')).toHaveText('読み込みとResourceHandle');

  await expectNoFailures(failures);
});

test('Assets Learn embeds one concept-focused CPU example', async ({ page }) => {
  const failures = collectFailures(page);
  const story = 'Learn/Resources/Assets/Overview';
  await page.goto(`/?story=${encodeURIComponent(story)}`);

  await expect(page.locator('.markdown-document h1')).toHaveText('アセットの概要');
  const embeds = page.locator('.markdown-story-embed');
  await expect(embeds).toHaveCount(1);
  await expect(embeds.locator('header')).toContainText('Examples/Resources/Assets/DocumentInspector');

  await embeds.getByRole('link', { name: 'Storyを開く' }).click();
  await expect(page).toHaveURL(/story=Examples%2FResources%2FAssets%2FDocumentInspector/);
  const runtime = page.frameLocator('.story-runtime-frame');
  await expect(runtime.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await page.goBack();
  await expect(page.locator('.markdown-document h1')).toHaveText('アセットの概要');

  await expectNoFailures(failures);
});

test('glTF Learn exposes loading, URI, and diagnostic examples without fixture failures', async ({ page }) => {
  const failures = collectFailures(page);

  await page.goto(`/?story=${encodeURIComponent('Learn/Resources/Gltf/RegistrationAndLoading')}`);
  await expect(page.locator('.markdown-document h1')).toHaveText('glTFの登録と読み込み');
  await expect(page.locator('.markdown-story-embed')).toHaveCount(1);
  await expect(page.locator('.markdown-story-embed header')).toContainText('Examples/Resources/Gltf/BoxDocumentLoad');

  await page.goto(`/?story=${encodeURIComponent('Learn/Resources/Gltf/ExternalBuffersImagesAndUris')}`);
  await expect(page.locator('.markdown-document h1')).toHaveText('外部バッファ、画像、URI');
  await expect(page.locator('.markdown-story-embed')).toHaveCount(1);
  await expect(page.locator('.markdown-story-embed header')).toContainText('Examples/Resources/Gltf/ExternalBufferTrace');

  await page.goto(`/?story=${encodeURIComponent('Learn/Resources/Gltf/ValidationAndDiagnostics')}`);
  await expect(page.locator('.markdown-document h1')).toHaveText('検証と診断');
  await expect(page.locator('.markdown-story-embed header').first()).toContainText('Examples/Resources/Gltf/MalformedAccessorDiagnostics');

  await expectNoFailures(failures);
});

const cpuResourceStories = [
  'Examples/Resources/ReadyBuilder',
  'Examples/Resources/HelloTextAsset',
  'Examples/Resources/CustomPackageSource',
  'Examples/Resources/PlayerStatsPipeline',
  'Examples/Resources/ExtensionSelection',
  'Examples/Resources/SharedDependencyGraph',
  'Examples/Resources/ScopedRuntimeValues',
  'Examples/Resources/HotReloadRecovery',
  'Examples/Resources/BrowserHttpAssets',
  'Examples/Resources/Assets/DocumentInspector',
  'Examples/Resources/Assets/MeshPrimitiveInspector',
  'Examples/Resources/Assets/MaterialTextureInspector',
  'Examples/Resources/Assets/AnimatedSceneGraph',
  'Examples/Resources/Assets/GpuAssetRegistry',
  'Examples/Resources/Assets/ShaderBufferInspector',
  'Examples/Resources/Gltf/BoxDocumentLoad',
  'Examples/Resources/Gltf/ExternalBufferTrace',
  'Examples/Resources/Gltf/MalformedAccessorDiagnostics',
  'Examples/Resources/Gltf/ExternalDependencyReload'
];

for (const story of cpuResourceStories) {
  test(`browser-WASM executes ${story} with a private ResourceSystem`, async ({ page }) => {
    const failures = collectFailures(page);
    await page.goto(runtimeUrl(story));
    await expectRuntimeStory(page, story);
    await expect.poll(() => page.evaluate(() =>
      globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail === '準備完了') || false),
      { timeout: 30_000 }).toBe(true);
    await expectNoFailures(failures);
  });
}

test('Resource widget publishes its result to the shared Output and Source panels', async ({ page }) => {
  const failures = collectFailures(page);
  const story = 'Examples/Resources/HelloTextAsset';
  await page.goto(`/?story=${encodeURIComponent(story)}`);

  const runtime = page.frameLocator('.story-runtime-frame');
  await expect(runtime.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await page.getByRole('tab', { name: '出力' }).click();
  await expect(page.locator('.output-list')).toContainText('テキストアセットの読み込み: 準備完了');
  await expect(page.locator('.output-list')).toContainText('こんにちは、RESOURCES');
  await page.getByRole('tab', { name: 'ソース' }).click();
  await expect(page.locator('.story-source')).toContainText('public static Widget HelloTextAsset');
  await expect(page.locator('.story-source')).toContainText('resources.AddStep<byte[], TextAsset>');
  await expect(page.locator('.story-source')).not.toContainText('ResourceScenarios.Create');
  await expectNoFailures(failures);
});

for (const story of [
  'Examples/Resources/Gltf/BoxScene',
  'Examples/Resources/Gltf/AnimatedBox',
  'Examples/Resources/Gltf/RiggedSimpleSkinning',
  'Examples/Resources/Gltf/MorphWeights'
]) {
  test(`browser-WASM renders ${story}`, async ({ page }) => {
    const failures = collectFailures(page);
    await page.goto(runtimeUrl(story));
    await expectRuntimeStory(page, story, { gpu: true });

    if (story === 'Examples/Resources/Gltf/AnimatedBox') {
      const revision = await page.evaluate(() => globalThis.luxelBrowserState.renderRevision);
      await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState.renderRevision), {
        timeout: 30_000
      }).toBeGreaterThan(revision + 9);
    }

    await expectNoFailures(failures);
  });
}
