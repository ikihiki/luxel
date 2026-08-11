const SWIFTSHADER_WEBGPU_ARGS = [
  '--enable-unsafe-webgpu',
  '--use-angle=swiftshader',
  '--enable-features=Vulkan',
  '--disable-vulkan-surface'
];

function createStaticWebGpuConfig(defineConfig, {
  testDir = './tests',
  port = Number(process.env.LUXEL_WEBGPU_E2E_PORT || 4193),
  staticRoot,
  timeout = 90_000,
  expectTimeout = 30_000
}) {
  if (!staticRoot) throw new Error('staticRoot is required');
  const baseURL = `http://127.0.0.1:${port}`;
  return defineConfig({
    testDir,
    timeout,
    expect: { timeout: expectTimeout },
    fullyParallel: false,
    retries: process.env.CI ? 1 : 0,
    reporter: process.env.CI
      ? [['line'], ['html', { open: 'never', outputFolder: 'playwright-report' }]]
      : 'line',
    use: {
      baseURL,
      trace: 'retain-on-failure',
      screenshot: 'only-on-failure',
      video: 'retain-on-failure',
      launchOptions: { args: SWIFTSHADER_WEBGPU_ARGS }
    },
    webServer: {
      command: `python3 -m http.server ${port} --bind 127.0.0.1 --directory ${staticRoot}`,
      url: `${baseURL}/index.html`,
      reuseExistingServer: !process.env.CI,
      stdout: 'ignore',
      stderr: 'ignore',
      timeout: 30_000
    },
    projects: [{ name: 'chromium', use: { browserName: 'chromium' } }]
  });
}

module.exports = { SWIFTSHADER_WEBGPU_ARGS, createStaticWebGpuConfig };
