const { test, expect } = require('@playwright/test');
const {
  gotoGalleryStory,
  clickCanvasWidget,
  expectWidgetDetail
} = require('@luxel/browser-e2e-support/gallery').createGalleryHelpers(expect);

const boot = (page, story) => gotoGalleryStory(page, story, { noCapabilityFallback: true });
const clickButton = (page, detail, index = 0) => clickCanvasWidget(page, { detail, index });
const clickNthButton = (page, index) => clickCanvasWidget(page, { index });
const expectDetail = (page, text) => expectWidgetDetail(page, text);

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
