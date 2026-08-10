const { test, expect } = require('@playwright/test');

const nativeOnlyRoutes = [
  'Apps/Studio/Shell',
  'Learn/Production/StudioToPlayer',
  'Learn/Production/ValidateAndShip',
  'Learn/Production/Workbench',
  'Learn/Scripting/Overview',
  'Learn/Scripting/ReloadAndIsolation'
];

const workerCount = 6;
const approvedFallbacks = new Set([
  // Browser-incompatible authored demos isolated by the runtime error boundary.
  'Apps/Player/Basic',
  'Apps/Player/ScriptEditor',
  'Apps/Player/ThreeD',
  'Controls/TextEditorView/Code',
  'Controls/TextEditorView/Completion',
  'Examples/2D/Backends',
  'Examples/3D/TexturedQuad',
  'Examples/Scripting/LiveCsx',
  'Examples/Scripting/HotReload',
  'Examples/Scripting/Playground',
  'Examples/Scripting/Repl',
  'Game/Cavern',
  'Internals/Authoring',
  'Learn/Graphics/2D/Backends',
  'Reference/Luxel.Controls',
  // Generated production components whose factories require host-specific construction.
  'Controls/Canvas2D/Basic',
  'Controls/Canvas2D/Overview',
  'Controls/Grid/Basic',
  'Controls/Grid/Overview',
  'Controls/GpuView/Basic',
  'Controls/GpuView/Overview',
  'Controls/ImageBlock/Basic',
  'Controls/ImageBlock/Overview',
  'Controls/KnobsTable/Basic',
  'Controls/KnobsTable/Overview',
  'Controls/ParticleView/Basic',
  'Controls/ParticleView/Overview',
  'Controls/RichTextView/Basic',
  'Controls/RichTextView/Overview',
  'Controls/SceneInspector/Basic',
  'Controls/SceneInspector/Overview'
]);

async function waitForRuntime(status, story) {
  await expect(status).toHaveAttribute('data-story', story, { timeout: 90_000 });
  await expect(status).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  const fallback = await status.evaluate(() =>
    globalThis.luxelBrowserState?.widgets?.some(widget => widget.type?.endsWith('.StoryCapabilityFallback')) || false);
  if (fallback && !approvedFallbacks.has(story))
    throw new Error('unexpected StoryCapabilityFallback');
}

test('every Blazor Gallery page renders or reaches a browser-safe runtime fallback', async ({ browser, baseURL }) => {
  test.setTimeout(20 * 60_000);

  const discovery = await browser.newPage();
  await discovery.goto(baseURL);
  await expect.poll(() => discovery.locator('.story-link').count(), { timeout: 90_000 }).toBeGreaterThan(0);
  const routes = [...new Set(await discovery.locator('.story-link').evaluateAll(links => links.map(link => link.title)))].sort();
  await discovery.close();

  expect(routes).toHaveLength(504);
  for (const nativeOnlyRoute of nativeOnlyRoutes)
    expect(routes, `${nativeOnlyRoute} must only be registered by Gallery.Native`).not.toContain(nativeOnlyRoute);
  const failures = [];
  let next = 0;

  async function auditPages() {
    const context = await browser.newContext();
    try {
      while (true) {
        const index = next++;
        if (index >= routes.length) return;
        const story = routes[index];
        const page = await context.newPage();
        try {
          await page.goto(`${baseURL}?story=${encodeURIComponent(story)}`, { waitUntil: 'domcontentloaded', timeout: 90_000 });
          await page.locator('.gallery-shell,.gallery-compact,.gallery-embed').first().waitFor({ timeout: 90_000 });

          if (await page.locator('.markdown-document').count()) {
            const unavailable = await page.locator('.markdown-embed-unavailable').count();
            if (unavailable) failures.push(`${story}: ${unavailable} unavailable Markdown embed(s)`);
            const frames = page.locator('.markdown-story-embed iframe');
            for (let frameIndex = 0; frameIndex < await frames.count(); frameIndex++) {
              const frame = frames.nth(frameIndex);
              const embeddedStory = new URL(await frame.getAttribute('src'), baseURL).searchParams.get('story');
              const status = page.frameLocator('.markdown-story-embed iframe').nth(frameIndex).locator('#status');
              try { await waitForRuntime(status, embeddedStory); }
              catch (error) { failures.push(`${story}: embed ${frameIndex}: ${error.message}`); }
            }
          } else {
            try { await waitForRuntime(page.frameLocator('.story-runtime-frame').locator('#status'), story); }
            catch (error) { failures.push(`${story}: ${error.message}`); }
          }
        } catch (error) {
          failures.push(`${story}: ${error.message}`);
        } finally {
          await page.close();
        }
      }
    } finally {
      await context.close();
    }
  }

  await Promise.all(Array.from({ length: workerCount }, auditPages));
  expect(failures, failures.join('\n')).toEqual([]);
});
