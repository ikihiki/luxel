import { dotnet } from "./_framework/dotnet.js";
import * as webgpu from "./luxel-webgpu-browser.js";

const status = document.getElementById("status");
const errorOverlay = document.getElementById("error");
const runtimeState = { state: "loading", summary: "" };
globalThis.luxelBrowserState = runtimeState;
const host = {
  nextFrame: () => new Promise(resolve => requestAnimationFrame(resolve)),
  setStatus: (state, summary) => {
    runtimeState.state = state;
    runtimeState.summary = summary;
    status.dataset.status = state;
    status.textContent = summary;
    const failed = state === "fail";
    errorOverlay.hidden = !failed;
    errorOverlay.textContent = failed ? summary : "";
  },
};

try {
  const runtime = await dotnet.create();
  runtime.setModuleImports("./luxel-webgpu-browser.js", webgpu);
  runtime.setModuleImports("luxel-browser-host", host);
  const exports = await runtime.getAssemblyExports("LuxelWebGpuBrowser.dll");
  globalThis.luxelBrowserExports = exports;
  await runtime.runMain();
} catch (error) {
  host.setStatus("fail", `browser-webgpu: status=fail, error=${error?.stack || error}`);
  console.error(error);
}
