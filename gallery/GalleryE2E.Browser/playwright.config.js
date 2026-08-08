const { defineConfig } = require('@playwright/test');

const port = Number(process.env.LUXEL_WEBGPU_E2E_PORT || 4193);
const useSoftwareGpu = ['1', 'true'].includes((process.env.LUXEL_E2E_SOFTWARE_GPU || '').toLowerCase());
const gpuArgs = useSoftwareGpu
  ? ['--enable-unsafe-webgpu', '--use-angle=swiftshader', '--enable-features=Vulkan', '--disable-vulkan-surface']
  : ['--enable-unsafe-webgpu'];

module.exports = defineConfig({
  testDir: './tests',
  timeout: 90_000,
  expect: { timeout: 30_000 },
  fullyParallel: false,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI
    ? [['line'], ['html', { open: 'never', outputFolder: 'playwright-report' }]]
    : 'line',
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    launchOptions: { args: gpuArgs }
  },
  webServer: {
    command: `python3 -m http.server ${port} --bind 127.0.0.1 --directory ../../artifacts/gallery-browser/wwwroot`,
    url: `http://127.0.0.1:${port}/index.html`,
    reuseExistingServer: !process.env.CI,
    stdout: 'ignore',
    stderr: 'ignore',
    timeout: 30_000
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }]
});
