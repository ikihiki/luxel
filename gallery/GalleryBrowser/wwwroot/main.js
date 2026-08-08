const protocolVersion = 2;
const status = document.getElementById("status");
const errorOverlay = document.getElementById("error");
const query = new URLSearchParams(location.search);
const story = query.get("story") || "";
const instanceId = query.get("instance") || crypto.randomUUID();
let args = {};
try { args = JSON.parse(query.get("args") || "{}"); } catch { throw new Error("story args must be a JSON object"); }
if (!args || Array.isArray(args) || typeof args !== "object") throw new Error("story args must be a JSON object");
let revision = 0;
let setArgsExport = null;
let pendingSetArgs = null;
const runtimeState = { state: "loading", summary: "", story, instanceId, args, schema: [], revision, renderRevision: 0, lastRequestId: null, events: [], widgets: [], webGpu: null, pointerDownCount: 0, pointerUpCount: 0 };
const updateCountState = () => {
  if (Number.isFinite(Number(args.count))) runtimeState.count = runtimeState.presentedCount = Number(args.count);
};
updateCountState();
globalThis.luxelBrowserState = runtimeState;
const canvas = document.getElementById("luxel-canvas");
canvas?.addEventListener("pointerdown", () => runtimeState.pointerDownCount += 1);
canvas?.addEventListener("pointerup", () => runtimeState.pointerUpCount += 1);
const post = (type, payload = {}) => parent !== window && parent.postMessage({ luxelGallery: true, protocolVersion, type, story, instanceId, revision, args, ...payload }, location.origin);
const parseObject = (json, label) => {
  const value = JSON.parse(json || "{}");
  if (!value || Array.isArray(value) || typeof value !== "object") throw new Error(`${label} must be a JSON object`);
  return value;
};
const applySetArgs = message => {
  if (!setArgsExport) { pendingSetArgs = message; return; }
  try {
    const response = JSON.parse(setArgsExport(story, instanceId, JSON.stringify(message.args), message.revision, message.requestId));
    args = response.args || args;
    revision = message.revision;
    Object.assign(runtimeState, { args, revision, lastRequestId: message.requestId });
    updateCountState();
    if (response.errors?.length) post("arg-error", { requestId: message.requestId, errors: response.errors });
    else post("args-changed", { requestId: message.requestId, source: "parent" });
  } catch (error) {
    post("arg-error", { requestId: message.requestId, errors: [String(error?.message || error)] });
  }
};
window.addEventListener("message", event => {
  const message = event.data;
  if (event.source !== parent || event.origin !== location.origin || !message?.luxelGallery || message.protocolVersion !== protocolVersion) return;
  if (message.story !== story || message.instanceId !== instanceId || message.type !== "set-args") return;
  if (!Number.isSafeInteger(message.revision) || message.revision <= revision || typeof message.requestId !== "string") return;
  if (!message.args || Array.isArray(message.args) || typeof message.args !== "object") {
    post("arg-error", { requestId: message.requestId, errors: ["set-args requires a canonical args object."] });
    return;
  }
  applySetArgs(message);
});
const host = {
  getStory: () => story,
  nextFrame: () => new Promise(resolve => requestAnimationFrame(resolve)),
  getArgsJson: () => JSON.stringify(args),
  publishFrame: renderRevision => { runtimeState.renderRevision = renderRevision; },
  publishDiagnostics: widgetsJson => {
    const widgets = JSON.parse(widgetsJson || "[]");
    runtimeState.widgets = Array.isArray(widgets) ? widgets : [];
    runtimeState.minusBounds = runtimeState.widgets.find(widget => widget.type?.endsWith(".Button") && widget.detail === "-") || null;
    runtimeState.plusBounds = runtimeState.widgets.find(widget => widget.type?.endsWith(".Button") && widget.detail === "+") || null;
  },
  publishWebGpuDiagnostics: diagnosticsJson => {
    runtimeState.webGpu = parseObject(diagnosticsJson, "WebGPU diagnostics");
  },
  publishArgsChanged: argsJson => {
    args = parseObject(argsJson, "args snapshot");
    revision += 1;
    Object.assign(runtimeState, { args, revision });
    updateCountState();
    post("args-changed", { requestId: null, source: "child" });
  },
  publishEvent: entryJson => {
    const entry = JSON.parse(entryJson);
    runtimeState.events.push(entry);
    if (runtimeState.events.length > 200) runtimeState.events.shift();
    post("event", { entry });
  },
  setReady: (summary, argsJson, schemaJson) => {
    args = parseObject(argsJson, "ready args");
    const schema = JSON.parse(schemaJson || "[]");
    Object.assign(runtimeState, { state: "pass", summary, args, schema, revision });
    updateCountState();
    status.dataset.status = "pass";
    status.dataset.story = story;
    status.textContent = summary;
    errorOverlay.hidden = true;
    post("ready", { schema });
  },
  setStatus: (state, summary) => {
    Object.assign(runtimeState, { state, summary });
    status.dataset.status = state;
    status.dataset.story = story;
    status.textContent = summary;
    const failed = state === "fail";
    errorOverlay.hidden = !failed;
    errorOverlay.textContent = failed ? summary : "";
    post(failed ? "story-error" : "ready", failed ? { error: summary } : { schema: [] });
  },
};

try {
  const runtime = globalThis.getDotnetRuntime?.(0);
  if (runtime) {
    globalThis.luxelDotnetRuntime = runtime;
    const exports = await runtime.getAssemblyExports("Luxel.Gallery.Browser.dll");
    globalThis.luxelBrowserExports = exports;
    setArgsExport = exports?.Luxel?.Gallery?.Browser?.BrowserGalleryApplication?.SetArgsSnapshot;
    if (pendingSetArgs) { const pending = pendingSetArgs; pendingSetArgs = null; applySetArgs(pending); }
  }
} catch (error) {
  console.warn("Luxel Gallery JS export discovery failed", error);
}

export const getArgsJson = host.getArgsJson;
export const getStory = host.getStory;
export const nextFrame = host.nextFrame;
export const setStatus = host.setStatus;
export const setReady = host.setReady;
export const publishArgsChanged = host.publishArgsChanged;
export const publishEvent = host.publishEvent;
export const publishDiagnostics = host.publishDiagnostics;
export const publishWebGpuDiagnostics = host.publishWebGpuDiagnostics;
export const publishFrame = host.publishFrame;
