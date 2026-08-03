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
  await expect.poll(() => root.evaluate(element => element.dataset.playgroundLanguageService), { timeout: 60_000 }).toBe('roslyn-worker');
  return { root, consoleErrors };
}

async function setSource(root, source) {
  await root.evaluate((element, value) => globalThis.LuxelPlayground.setValue(element, value), source);
}

async function getSource(root) {
  return root.evaluate(element => globalThis.LuxelPlayground.getValue(element));
}

async function countColoredCanvasSamples(canvas) {
  return canvas.evaluate(async source => {
    const image = new Image();
    image.src = source.toDataURL('image/png');
    await image.decode();
    const copy = document.createElement('canvas');
    copy.width = source.width;
    copy.height = source.height;
    const context = copy.getContext('2d', { willReadFrequently: true });
    context.drawImage(image, 0, 0);
    const pixels = context.getImageData(0, 0, copy.width, copy.height).data;
    let colored = 0;
    for (let y = 0; y < copy.height; y += 8) {
      for (let x = 0; x < copy.width; x += 8) {
        const offset = (y * copy.width + x) * 4;
        const red = pixels[offset], green = pixels[offset + 1], blue = pixels[offset + 2], alpha = pixels[offset + 3];
        if (alpha > 200 && Math.max(red, green, blue) - Math.min(red, green, blue) > 20) colored++;
      }
    }
    return colored;
  });
}

async function runSource(root, source) {
  await setSource(root, source);
  await root.locator('[data-playground-run]').click();
  const frame = root.locator('iframe[data-playground-instance]').last();
  await expect(frame).toBeVisible();
  return frame;
}

test('renders and parses the untouched default csx template', async ({ page }) => {
  const { root } = await openPlayground(page);
  const workspace = await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  expect(workspace.files.find(file => file.id === workspace.entryFileId)).toMatchObject({ path: 'Button.csx', language: 'csharp-script' });

  await root.locator('[data-playground-run]').click();

  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('No diagnostics.');
});

test('adapts the editor and preview layout from desktop through iPad to mobile', async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  const { root } = await openPlayground(page);
  await page.evaluate(() => {
    document.body.classList.add('sidebar-collapsed');
    document.body.classList.remove('review-open');
  });

  const editor = root.locator('.playground-editor');
  const preview = root.locator('.playground-preview');
  const monaco = root.locator('[data-playground-monaco]');
  let editorBox = await editor.boundingBox();
  let previewBox = await preview.boundingBox();
  let monacoBox = await monaco.boundingBox();
  expect(Math.abs(editorBox.y - previewBox.y)).toBeLessThan(4);
  expect(editorBox.width).toBeGreaterThan(previewBox.width);
  expect(monacoBox.width).toBeGreaterThan(450);

  await setSource(root, 'return Kit.Text("responsive draft");');
  await page.setViewportSize({ width: 1024, height: 768 });
  editorBox = await editor.boundingBox();
  previewBox = await preview.boundingBox();
  monacoBox = await monaco.boundingBox();
  expect(previewBox.y).toBeGreaterThan(editorBox.y + editorBox.height - 2);
  expect(monacoBox.width).toBeGreaterThanOrEqual(480);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);

  await page.setViewportSize({ width: 768, height: 1024 });
  const filesBox = await root.locator('.playground-files').boundingBox();
  monacoBox = await monaco.boundingBox();
  previewBox = await preview.boundingBox();
  editorBox = await editor.boundingBox();
  expect(previewBox.y).toBeGreaterThan(editorBox.y + editorBox.height - 2);
  expect(monacoBox.x).toBeGreaterThan(filesBox.x + filesBox.width - 2);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);

  await page.setViewportSize({ width: 390, height: 844 });
  await root.evaluate(element => {
    globalThis.LuxelPlayground.addFile(element, 'A-very-long-support-file-name-for-horizontal-scrolling.cs', 'csharp', 'class Support {}');
    globalThis.LuxelPlayground.addFile(element, 'Another-long-support-file-name-for-horizontal-scrolling.cs', 'csharp', 'class OtherSupport {}');
  });
  const fileList = root.locator('[data-playground-file-list]');
  const narrowFilesBox = await root.locator('.playground-files').boundingBox();
  monacoBox = await monaco.boundingBox();
  expect(monacoBox.y).toBeGreaterThan(narrowFilesBox.y + narrowFilesBox.height - 2);
  expect(monacoBox.width).toBeGreaterThan(narrowFilesBox.width * 0.9);
  expect(await fileList.evaluate(element => getComputedStyle(element).overflowX)).toBe('auto');
  expect(await fileList.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  for (const control of await root.locator('.playground-actions button, .playground-file-actions button, [data-playground-file-list] [role="tab"]').all())
    expect((await control.boundingBox()).height).toBeGreaterThanOrEqual(44);
  expect(await getSource(root)).toBe('class OtherSupport {}');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
});

test('formats active C# files and disables formatting for Slang', async ({ page }) => {
  const { root } = await openPlayground(page);
  const format = root.locator('[data-playground-file-format]');

  await setSource(root, 'if(true){return Kit.Text("ok");}');
  await format.click();
  await expect.poll(() => getSource(root)).toBe('if (true) { return Kit.Text("ok"); }');

  const supportId = await root.evaluate(element => globalThis.LuxelPlayground.addFile(element, 'Support.cs', 'csharp', 'public static class Support{public static int Value=>1;}'));
  await expect(format).toBeEnabled();
  await format.click();
  await expect.poll(() => getSource(root)).toBe('public static class Support { public static int Value => 1; }');
  expect((await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element))).activeFileId).toBe(supportId);

  await root.locator('[data-playground-sample-select]').selectOption('slang-cube');
  page.once('dialog', dialog => dialog.accept());
  await root.locator('[data-playground-sample-load]').click();
  const shaderId = await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element).files.find(file => file.language === 'slang').id);
  await root.evaluate((element, fileId) => globalThis.LuxelPlayground.selectFile(element, fileId), shaderId);
  const shader = await getSource(root);
  await expect(format).toBeDisabled();
  await expect(format).toHaveAttribute('title', 'Formatting is available for C# files.');
  expect(await getSource(root)).toBe(shader);
});

test('supports roving keyboard focus across workspace file tabs', async ({ page }) => {
  const { root } = await openPlayground(page);
  const secondId = await root.evaluate(element => globalThis.LuxelPlayground.addFile(element, 'Second.cs', 'csharp', 'class Second {}'));
  const firstId = await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element).entryFileId);
  const selected = root.locator(`[data-playground-file-list] [data-file-id="${secondId}"]`);
  await selected.focus();
  await selected.press('Home');
  await expect(root.locator(`[data-playground-file-list] [data-file-id="${firstId}"]`)).toHaveAttribute('aria-selected', 'true');
  await expect(root.locator(`[data-playground-file-list] [data-file-id="${firstId}"]`)).toBeFocused();
  await page.keyboard.press('End');
  await expect(root.locator(`[data-playground-file-list] [data-file-id="${secondId}"]`)).toHaveAttribute('aria-selected', 'true');
  await expect(root.locator(`[data-playground-file-list] [data-file-id="${secondId}"]`)).toHaveAttribute('tabindex', '0');
  await expect(root.locator(`[data-playground-file-list] [data-file-id="${firstId}"]`)).toHaveAttribute('tabindex', '-1');
});

test('loads, persists, resets, and renders the 3D Slang cube sample', async ({ page }) => {
  await page.setViewportSize({ width: 768, height: 1024 });
  const { root, consoleErrors } = await openPlayground(page);
  const select = root.locator('[data-playground-sample-select]');
  await expect(select).toContainText('3D Slang Cube');

  await setSource(root, 'return Kit.Text("edited draft");');
  await select.selectOption('slang-cube');
  page.once('dialog', dialog => dialog.dismiss());
  await root.locator('[data-playground-sample-load]').click();
  expect((await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element))).sampleId).toBe('button');

  page.once('dialog', dialog => dialog.accept());
  await root.locator('[data-playground-sample-load]').click();
  await expect(root.locator('[data-playground-title]')).toHaveText('3D Slang Cube');
  let workspace = await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  expect(workspace.sampleId).toBe('slang-cube');
  expect(workspace.files.map(file => file.path)).toEqual(['Cube.csx', 'SlangCubeRenderer.cs', 'Shaders/cube.slang']);
  expect(workspace.files.find(file => file.path === 'Shaders/cube.slang').source).toContain('[shader("vertex")]');

  await root.locator('[data-playground-run]').click();
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 90_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('No diagnostics.');
  await expect(root.locator('[data-playground-output]')).toContainText('Loaded Shaders/cube.slang as wgsl.');
  const frame = root.locator('iframe[data-playground-instance]').last();
  await expect(frame.contentFrame().locator('#status')).toContainText('rendered');
  const canvas = frame.contentFrame().locator('#luxel-canvas');
  await expect(canvas).toBeVisible();
  await expect.poll(() => countColoredCanvasSamples(canvas), { timeout: 30_000 }).toBeGreaterThan(100);

  await page.reload();
  const restored = page.locator('[data-playground]');
  await expect.poll(() => restored.evaluate(element => element.dataset.playgroundEditor)).toBe('monaco');
  workspace = await restored.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  expect(workspace.sampleId).toBe('slang-cube');
  await setSource(restored, '// edited shader');
  await restored.locator('[data-playground-reset]').click();
  workspace = await restored.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  expect(workspace.sampleId).toBe('slang-cube');
  expect(workspace.files.find(file => file.path === 'Shaders/cube.slang').source).toContain('[shader("vertex")]');
  expect(consoleErrors).toEqual([]);
});

test('browser workspace validators reject unsafe paths, case aliases, and UTF-8 C# overflow', async ({ page }) => {
  const { root } = await openPlayground(page);

  for (const path of ['/root.cs', 'C:\\root.cs', 'https://example.test/main.cs', 'folder:name.cs', 'folder/../main.cs', 'line\nbreak.cs']) {
    await expect(root.evaluate((element, candidate) => {
      try { globalThis.LuxelPlayground.addFile(element, candidate); return null; }
      catch (error) { return String(error.message); }
    }, path)).resolves.toBeTruthy();
  }

  await root.evaluate(element => globalThis.LuxelPlayground.addFile(element, 'Folder/Helper.cs', 'csharp', 'class Helper {}'));
  expect(await root.evaluate(element => {
    try { globalThis.LuxelPlayground.addFile(element, 'folder/helper.CS', 'csharp', 'class Other {}'); return null; }
    catch (error) { return String(error.message); }
  })).toBeTruthy();

  const oversized = 'é'.repeat(128 * 1024 / 2 + 1);
  expect(await root.evaluate((element, source) => {
    try { globalThis.LuxelPlayground.addFile(element, 'TooLarge.cs', 'csharp', source); return null; }
    catch (error) { return String(error.message); }
  }, oversized)).toContain('too large');
});

test('compiles C# and renders a real Luxel button', async ({ page }) => {
  const { root, consoleErrors } = await openPlayground(page);
  await setSource(root, 'Kit.');
  expect(await root.evaluate(element => globalThis.LuxelPlayground.triggerSuggest(element))).toBe(true);
  await expect(page.locator('.suggest-widget')).toBeVisible();
  await expect(page.locator('.suggest-widget')).toContainText('Button');
  const source = 'Log("Button rendered."); return Kit.Button(_ => Log("Button clicked."), "Playwright button");';

  const frame = await runSource(root, source);
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('No diagnostics.');

  const runtime = frame.contentFrame();
  await expect(runtime.locator('#status')).toContainText('rendered');
  await expect(runtime.locator('#luxel-canvas')).toBeVisible();
  await expect(root.locator('[data-playground-output]')).toContainText('Button rendered.');
  const previewWidth = await root.locator('[data-playground-preview]').evaluate(element => element.clientWidth);
  const frameWidth = await frame.evaluate(element => element.getBoundingClientRect().width);
  expect(Math.abs(previewWidth - frameWidth)).toBeLessThanOrEqual(2);
  const canvas = runtime.locator('#luxel-canvas');
  await canvas.click({ position: { x: 24, y: 24 } });
  await expect(root.locator('[data-playground-output]')).toContainText('Button clicked.');
  await expect(frame).toHaveAttribute('allow', 'webgpu');
  expect(consoleErrors).toEqual([]);
});

test('shows compiler diagnostics while retaining the last successful runtime iframe', async ({ page }) => {
  const { root } = await openPlayground(page);

  await setSource(root, 'return missingName;');
  await expect.poll(() => root.evaluate(element => globalThis.LuxelPlayground.diagnostics(element)), { timeout: 15_000 })
    .toEqual(expect.arrayContaining([expect.objectContaining({ code: 'CS0103' })]));

  const firstFrame = await runSource(root, 'return Kit.Button(_ => { }, "First");');
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  const firstInstance = await firstFrame.getAttribute('data-playground-instance');

  await runSource(root, 'return missingName;');
  await expect(root.locator('[data-playground-status]')).toHaveText('compilation-failed', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('CS0103');
  const markers = await root.evaluate(element => globalThis.LuxelPlayground.diagnostics(element));
  expect(markers.some(marker => String(marker.code) === 'CS0103')).toBe(true);

  await expect(root.locator('iframe[data-playground-instance]')).toHaveCount(1);
  await expect(root.locator('iframe[data-playground-instance]')).toHaveAttribute('data-playground-instance', firstInstance);
});

test('supersedes an overlapping run and explicit stop retains the last good preview', async ({ page }) => {
  const { root } = await openPlayground(page);

  const firstFrame = await runSource(root, 'return Kit.Button(_ => { }, "last good");');
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  const firstInstance = await firstFrame.getAttribute('data-playground-instance');

  await setSource(root, 'return Kit.Button(_ => { }, "superseded");');
  await root.locator('[data-playground-run]').click();
  await setSource(root, 'return Kit.Button(_ => { }, "latest");');
  await root.locator('[data-playground-run]').click();
  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  await expect(root.locator('iframe[data-playground-instance]')).toHaveCount(1);
  const latestInstance = await root.locator('iframe[data-playground-instance]').getAttribute('data-playground-instance');
  expect(latestInstance).not.toBe(firstInstance);

  await setSource(root, 'return Kit.Button(_ => { }, "canceled");');
  await root.locator('[data-playground-run]').click();
  await root.locator('[data-playground-cancel]').click();
  await expect(root.locator('[data-playground-status]')).toHaveText('Canceled');
  await expect(root.locator('iframe[data-playground-instance]')).toHaveCount(1);
  await expect(root.locator('iframe[data-playground-instance]')).toHaveAttribute('data-playground-instance', latestInstance);
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

test('adds, selects, renames, deletes, and restores multiple workspace files', async ({ page }) => {
  const { root } = await openPlayground(page);
  const helperId = await root.evaluate(element => globalThis.LuxelPlayground.addFile(element, 'Helpers/Message.cs', 'csharp', 'public static class Message { public const string Value = "hello"; }'));
  let workspace = await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  expect(workspace.schemaVersion).toBe(2);
  expect(workspace.activeFileId).toBe(helperId);
  expect(workspace.files.map(file => file.path)).toContain('Helpers/Message.cs');
  const modelUri = await page.evaluate(id => globalThis.monaco.editor.getModels().find(model => model.uri.toString().includes(encodeURIComponent(id)))?.uri.toString(), helperId);

  await root.evaluate((element, id) => globalThis.LuxelPlayground.renameFile(element, id, 'Helpers/Renamed.cs'), helperId);
  expect(await page.evaluate(id => globalThis.monaco.editor.getModels().find(model => model.uri.toString().includes(encodeURIComponent(id)))?.uri.toString(), helperId)).toBe(modelUri);
  await page.reload();

  const restoredRoot = page.locator('[data-playground]');
  await expect.poll(() => restoredRoot.evaluate(element => element.dataset.playgroundEditor)).toBe('monaco');
  workspace = await restoredRoot.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  expect(workspace.files.find(file => file.id === helperId)).toMatchObject({ path: 'Helpers/Renamed.cs', source: 'public static class Message { public const string Value = "hello"; }' });
  expect(workspace.activeFileId).toBe(helperId);

  expect(await restoredRoot.evaluate((element, id) => globalThis.LuxelPlayground.deleteFile(element, id), helperId)).toBe(true);
  workspace = await restoredRoot.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  expect(workspace.files.some(file => file.id === helperId)).toBe(false);
  expect(workspace.files).toHaveLength(1);
});

test('compiles a multi-document C# workspace through protocol v2', async ({ page }) => {
  const { root } = await openPlayground(page);
  const workspace = await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  const entryId = workspace.entryFileId;
  await root.evaluate(element => globalThis.LuxelPlayground.addFile(
    element,
    'Helpers/Message.cs',
    'csharp',
    'public static class Message { public const string Value = "multi-file"; }'));
  await root.evaluate((element, id) => globalThis.LuxelPlayground.selectFile(element, id), entryId);

  await runSource(root, 'return Kit.Button(_ => { }, Message.Value);');

  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('No diagnostics.');
});

test('exposes a compiled workspace GpuShaderCode and metadata to C#', async ({ page }) => {
  const { root } = await openPlayground(page);
  const workspace = await root.evaluate(element => globalThis.LuxelPlayground.getWorkspace(element));
  const entryId = workspace.entryFileId;
  await root.evaluate(element => globalThis.LuxelPlayground.addFile(
    element,
    'Shaders/workspace.slang',
    'slang',
    '[shader("compute")]\n[numthreads(1, 1, 1)]\nvoid main(uint3 tid : SV_DispatchThreadID) {}'));
  await root.evaluate((element, id) => globalThis.LuxelPlayground.selectFile(element, id), entryId);

  await runSource(root, `
var shader = WebScriptResources.Get<GpuShaderCode>("Shaders/workspace.slang");
if (shader.Value.Wgsl is null || shader.Metadata.Properties["target"] != "wgsl")
    throw new InvalidOperationException("Compiled shader metadata is unavailable.");
Log(shader.Metadata.Uri);
return Kit.Button(_ => { }, "shader resource ready");`);

  await expect(root.locator('[data-playground-status]')).toHaveText('rendered', { timeout: 60_000 });
  await expect(root.locator('[data-playground-diagnostics]')).toContainText('No diagnostics.');
});

test('provides Slang diagnostics and completion through the worker', async ({ page }) => {
  const { root } = await openPlayground(page);
  const slangId = await root.evaluate(element => globalThis.LuxelPlayground.addFile(element, 'shader.slang', 'slang', '\nvoid'));
  await root.evaluate((element, id) => globalThis.LuxelPlayground.selectFile(element, id), slangId);
  await expect.poll(() => page.evaluate(() => globalThis.monaco?.editor.getModels().find(model => model.getLanguageId() === 'slang')?.getLanguageId())).toBe('slang');

  expect(await root.evaluate(element => globalThis.LuxelPlayground.triggerSuggest(element))).toBe(true);
  await expect(page.locator('.suggest-widget')).toBeVisible({ timeout: 30_000 });
  await expect(page.locator('.suggest-widget')).toContainText('__Addr');

  await setSource(root, 'struct Payload { float3 color; };\nvoid test(Payload value) { }');
  await expect.poll(() => root.evaluate(element => globalThis.LuxelPlayground.diagnostics(element)), { timeout: 30_000 }).toEqual([]);
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
