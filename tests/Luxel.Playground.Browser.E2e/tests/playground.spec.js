const { test, expect } = require('@playwright/test');

async function openPlayground(page) {
  const consoleErrors = [];
  page.on('console', message => {
    if (message.type() === 'error') consoleErrors.push(message.text());
  });
  await page.goto('/index.html#playground');
  const root = page.locator('[data-playground]');
  await expect(root).toBeVisible();
  await expect(root.locator('[data-playground-status]')).toHaveText('Ready');
  return { root, consoleErrors };
}

async function runSource(root, source) {
  await root.locator('[data-playground-source]').fill(source);
  await root.locator('[data-playground-run]').click();
  const frame = root.locator('iframe[data-playground-instance]');
  await expect(frame).toBeVisible();
  return frame;
}

test('compiles C# and renders a real Luxel button', async ({ page }) => {
  const { root, consoleErrors } = await openPlayground(page);
  const source = 'return Kit.Button(_ => { }, "Playwright button");';

  const frame = await runSource(root, source);
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('No diagnostics.');

  const runtime = page.frames().find(candidate => candidate.url().includes('/samples/luxel-playground/'));
  expect(runtime).toBeTruthy();
  await expect.poll(() => runtime.evaluate(() => globalThis.luxelPlaygroundRuntimeState?.ready)).toBe(true);
  await expect.poll(() => runtime.evaluate(() => globalThis.luxelPlaygroundRuntimeState?.latestRevision)).toBe(1);
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
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
});

test('restores the local draft after a page reload without putting source in the URL', async ({ page }) => {
  const { root } = await openPlayground(page);
  const source = 'return Kit.Text("persisted draft");';
  await root.locator('[data-playground-source]').fill(source);

  await page.reload();
  const restored = page.locator('[data-playground-source]');
  await expect(restored).toHaveValue(source);
  expect(page.url()).not.toContain(encodeURIComponent(source));
  expect(page.url()).not.toContain('persisted%20draft');
});
