const { test, expect } = require('@playwright/test');

async function openPlayground(page) {
  const consoleErrors = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  await page.goto('/index.html#story=Examples%2FScripting%2FPlayground');
  const root = page.locator('[data-playground]');
  await expect(root).toBeVisible();
  await expect(page.locator('#stories a.active')).toHaveText('Playground');
  await expect(root.locator('[data-playground-status]')).toHaveText('Ready');
  await expect.poll(() => root.evaluate(element => element.dataset.playgroundEditor)).toBe('monaco');
  await expect(root.locator('[data-playground-monaco]')).toBeVisible();
  await expect.poll(() => page.evaluate(() => globalThis.monaco?.editor.getModels()[0]?.getLanguageId())).toBe('csharp');
  return { root, consoleErrors };
}

async function setSource(root, source) {
  await root.evaluate((element, value) => globalThis.LuxelPlayground.setValue(element, value), source);
}

async function getSource(root) {
  return root.evaluate(element => globalThis.LuxelPlayground.getValue(element));
}

async function runSource(root, source) {
  await setSource(root, source);
  await root.locator('[data-playground-run]').click();
  const frame = root.locator('iframe[data-playground-instance]');
  await expect(frame).toBeVisible();
  return frame;
}

test('compiles C# and renders a real Luxel button', async ({ page }) => {
  const { root, consoleErrors } = await openPlayground(page);
  await setSource(root, 'Kit.');
  expect(await root.evaluate(element => globalThis.LuxelPlayground.triggerSuggest(element))).toBe(true);
  await expect(page.locator('.suggest-widget')).toBeVisible();
  await expect(page.locator('.suggest-widget')).toContainText('Kit.Button');
  const source = 'return Kit.Button(_ => Log("Button clicked."), "Playwright button");';

  const frame = await runSource(root, source);
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('No diagnostics.');

  const runtime = page.frames().find(candidate => candidate.url().includes('/samples/luxel-playground/'));
  expect(runtime).toBeTruthy();
  await expect.poll(() => runtime.evaluate(() => globalThis.luxelPlaygroundRuntimeState?.ready)).toBe(true);
  await expect.poll(() => runtime.evaluate(() => globalThis.luxelPlaygroundRuntimeState?.latestRevision)).toBe(1);
  const canvas = runtime.locator('#luxel-canvas');
  await canvas.click({ position: { x: 60, y: 20 } });
  await expect(root.locator('[data-playground-output]')).toContainText('Button clicked.');
  await expect(frame).toHaveAttribute('allow', 'webgpu');
  expect(consoleErrors).toEqual([]);
});

test('shows compiler diagnostics and replaces the runtime iframe on rerun', async ({ page }) => {
  const { root } = await openPlayground(page);

  const firstFrame = await runSource(root, 'return Kit.Button(_ => { }, "First");');
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  const firstInstance = await firstFrame.getAttribute('data-playground-instance');

  const secondFrame = await runSource(root, 'return missingName;');
  await expect(root.locator('[data-playground-status]')).toHaveText('compilation-failed', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('CS0103');
  const markers = await root.evaluate(element => globalThis.LuxelPlayground.diagnostics(element));
  expect(markers.some(marker => String(marker.code) === 'CS0103')).toBe(true);
  const secondInstance = await secondFrame.getAttribute('data-playground-instance');

  expect(secondInstance).not.toBe(firstInstance);
  await expect(root.locator('iframe[data-playground-instance]')).toHaveCount(1);
});

test('removes a runtime that never becomes ready after the startup timeout', async ({ page }) => {
  const { root } = await openPlayground(page);

  await root.evaluate(element => {
    element.dataset.playgroundRuntimeUrl = 'index.html';
    element.dataset.playgroundStartupTimeoutMs = '500';
  });
  await runSource(root, 'return Kit.Text("never executed");');
  await expect(root.locator('[data-playground-status]')).toHaveText('Timed out', { timeout: 15_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('did not become ready within 30 seconds');
  await expect(root.locator('iframe[data-playground-instance]')).toHaveCount(0);
});

test('rejects an obvious unbounded loop without freezing the gallery', async ({ page }) => {
  const { root } = await openPlayground(page);

  await runSource(root, 'while (true) { }');
  await expect(root.locator('[data-playground-status]')).toHaveText('compilation-failed', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('LUXWEB003');

  await runSource(root, 'return Kit.Button(_ => { }, "recovered");');
  await expect.poll(() => root.evaluate(element => globalThis.LuxelPlayground.diagnostics(element))).toEqual([]);
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
});

test('restores the local draft after a page reload without putting source in the URL', async ({ page }) => {
  const { root } = await openPlayground(page);
  const source = 'return Kit.Text("persisted draft");';
  await setSource(root, source);

  await page.reload();
  const restoredRoot = page.locator('[data-playground]');
  await expect.poll(() => restoredRoot.evaluate(element => element.dataset.playgroundEditor)).toBe('monaco');
  await expect.poll(() => getSource(restoredRoot)).toBe(source);
  expect(page.url()).not.toContain(encodeURIComponent(source));
  expect(page.url()).not.toContain('persisted%20draft');
});
