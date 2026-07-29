import { dotnet } from "./_framework/dotnet.js";
import * as webgpu from "./luxel-webgpu-browser.js";

const status = document.getElementById("status");
const errorOverlay = document.getElementById("error");
const app = new URLSearchParams(location.search).get("app") || "triangle";
const runtimeState = { state: "loading", summary: "", story: app === "counter" ? "Controls/Button/Counter" : "Examples/3D/Triangle", app, count: app === "counter" ? 0 : null, renderRevision: 0, presentedCount: app === "counter" ? 0 : null, pointerDownCount: 0, pointerUpCount: 0, minusBounds: null, plusBounds: null };
globalThis.luxelBrowserState = runtimeState;
const host = {
  getApp: () => app,
  nextFrame: () => new Promise(resolve => requestAnimationFrame(resolve)),
  setCounterState: (count, renderRevision, presentedCount, pointerDownCount, pointerUpCount, minusX, minusY, minusW, minusH, plusX, plusY, plusW, plusH) => Object.assign(runtimeState, {
    count, renderRevision, presentedCount, pointerDownCount, pointerUpCount,
    minusBounds: { x: minusX, y: minusY, width: minusW, height: minusH },
    plusBounds: { x: plusX, y: plusY, width: plusW, height: plusH },
  }),
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
