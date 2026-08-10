const { test, expect } = require('@playwright/test');

const runtimeUrl = story => `/?story=${encodeURIComponent(story)}&embed=1`;

async function boot(page, story) {
  await page.goto(runtimeUrl(story));
  await expect(page.locator('#status')).toHaveAttribute('data-story', story, { timeout: 90_000 });
  await expect(page.locator('#status')).toHaveAttribute('data-status', 'pass', { timeout: 90_000 });
  await expect.poll(() => page.evaluate(() => globalThis.luxelBrowserState?.widgets?.length || 0), {
    timeout: 30_000
  }).toBeGreaterThan(0);
  await expect.poll(() => page.evaluate(() =>
    globalThis.luxelBrowserState?.widgets?.some(widget => widget.type?.endsWith('.StoryCapabilityFallback')) || false))
    .toBe(false);
}

async function clickButton(page, detail, index = 0) {
  await expect.poll(() => page.evaluate(({ label, position }) =>
    globalThis.luxelBrowserState?.widgets?.filter(widget =>
      widget.type?.endsWith('.Button') && widget.detail === label)[position] || null,
    { label: detail, position: index }), { timeout: 30_000 }).not.toBeNull();
  const button = await page.evaluate(({ label, position }) => globalThis.luxelBrowserState.widgets.filter(widget =>
    widget.type?.endsWith('.Button') && widget.detail === label)[position],
  { label: detail, position: index });
  await page.locator('#luxel-canvas').click({
    position: { x: button.x + button.width / 2, y: button.y + button.height / 2 }
  });
}

async function clickNthButton(page, index) {
  await expect.poll(() => page.evaluate(position =>
    globalThis.luxelBrowserState?.widgets?.filter(widget => widget.type?.endsWith('.Button'))[position] || null,
  index), { timeout: 30_000 }).not.toBeNull();
  const button = await page.evaluate(position => globalThis.luxelBrowserState.widgets.filter(widget =>
    widget.type?.endsWith('.Button'))[position], index);
  await page.locator('#luxel-canvas').click({
    position: { x: button.x + button.width / 2, y: button.y + button.height / 2 }
  });
}

async function expectDetail(page, text) {
  await expect.poll(() => page.evaluate(expected =>
    globalThis.luxelBrowserState?.widgets?.some(widget => widget.detail?.includes(expected)) || false, text),
  { timeout: 90_000 }).toBe(true);
}

test('LiveCsx compiles and renders a Widget with Roslyn Web', async ({ page }) => {
  await boot(page, 'Examples/Scripting/LiveCsx');
  await clickButton(page, 'Run');
  await expectDetail(page, 'こんにちは Luxel + Roslyn + csx');
});

test('browser hot reload publishes a successful Roslyn Web preview', async ({ page }) => {
  await boot(page, 'Examples/Scripting/HotReload');
  await clickButton(page, 'Apply');
  await expectDetail(page, 'version 1');
});

test('multi-file Playground executes through the browser Roslyn runner', async ({ page }) => {
  await boot(page, 'Examples/Scripting/Playground');
  await clickButton(page, 'Run');
  await expectDetail(page, 'Workspace ready');
});

test('Notebook code cells execute through Roslyn Web', async ({ page }) => {
  await boot(page, 'Examples/Scripting/Notebook');
  await clickNthButton(page, 0);
  await expectDetail(page, 'sum = 385');
});
