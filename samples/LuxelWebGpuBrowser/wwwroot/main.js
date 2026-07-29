import { dotnet } from "./_framework/dotnet.js";
import * as webgpu from "./luxel-webgpu-browser.js";

const protocolVersion = 1;
const status = document.getElementById("status");
const errorOverlay = document.getElementById("error");
const query = new URLSearchParams(location.search);
const manifest = await fetch("./browser-runtime-manifest.json").then(response => {
  if (!response.ok) throw new Error(`browser runtime manifest failed: ${response.status}`);
  return response.json();
});
if (manifest.protocolVersion !== protocolVersion) throw new Error(`unsupported browser runtime protocol ${manifest.protocolVersion}`);
const story = query.get("story") || manifest.stories[0];
if (!manifest.stories.includes(story)) throw new Error(`unsupported browser story '${story}'`);
const instanceId = query.get("instance") || crypto.randomUUID();
let args = {};
try { args = JSON.parse(query.get("args") || "{}"); } catch { throw new Error("story args must be a JSON object"); }
if (!args || Array.isArray(args) || typeof args !== "object") throw new Error("story args must be a JSON object");
const runtimeState = { state: "loading", summary: "", story, instanceId, args, count: story === "Controls/Button/Counter" ? Number(args.count || 0) : null, renderRevision: 0, presentedCount: story === "Controls/Button/Counter" ? Number(args.count || 0) : null, pointerDownCount: 0, pointerUpCount: 0, minusBounds: null, plusBounds: null };
globalThis.luxelBrowserState = runtimeState;
const post = (type, payload = {}) => parent !== window && parent.postMessage({ luxelGallery: true, protocolVersion, type, story, instanceId, ...payload }, location.origin);
const host = {
  getStory: () => story,
  nextFrame: () => new Promise(resolve => requestAnimationFrame(resolve)),
  getArgsJson: () => JSON.stringify(args),
  setCounterState: (count, renderRevision, presentedCount, pointerDownCount, pointerUpCount, minusX, minusY, minusW, minusH, plusX, plusY, plusW, plusH) => {
    const changed = runtimeState.count !== count;
    Object.assign(runtimeState, {
      count, renderRevision, presentedCount, pointerDownCount, pointerUpCount,
      minusBounds: { x: minusX, y: minusY, width: minusW, height: minusH },
      plusBounds: { x: plusX, y: plusY, width: plusW, height: plusH },
    });
    if (changed) { args = { ...args, count }; runtimeState.args = args; post("arg-value-changed", { name: "count", value: count }); }
  },
  setStatus: (state, summary) => {
    runtimeState.state = state;
    runtimeState.summary = summary;
    status.dataset.status = state;
    status.dataset.story = story;
    status.textContent = summary;
    const failed = state === "fail";
    errorOverlay.hidden = !failed;
    errorOverlay.textContent = failed ? summary : "";
    post(failed ? "story-error" : "ready", failed ? { error: summary } : { args, schema: story === "Controls/Button/Counter" ? [{ name: "count", type: "int", defaultValue: 0 }] : [] });
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
  host.setStatus("fail", `browser-webgpu: status=fail, story=${story}, error=${error?.stack || error}`);
  console.error(error);
}
