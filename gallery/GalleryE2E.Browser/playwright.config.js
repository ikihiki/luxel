const { createStaticWebGpuConfig } = require('@luxel/browser-e2e-support/config');

module.exports = createStaticWebGpuConfig(require('@playwright/test').defineConfig, {
  staticRoot: '../../artifacts/gallery-browser/wwwroot'
});
