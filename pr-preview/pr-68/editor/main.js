const status = document.getElementById("status");
const error = document.getElementById("error");
const state = globalThis.luxelEditorState = { state: "loading", summary: "", dirty: false, capabilities: {} };
let automationExports;
async function getAutomationExports() {
  if (automationExports) return automationExports;
  const runtime = globalThis.getDotnetRuntime?.(0);
  if (!runtime) throw new Error("The .NET runtime is not ready.");
  const exports = await runtime.getAssemblyExports("Luxel.Editor.Browser.dll");
  automationExports = exports?.Luxel?.Editor?.Browser?.EditorBrowserApplication;
  if (!automationExports) throw new Error("Luxel Editor automation exports are unavailable.");
  return automationExports;
}
function applyAutomationSnapshot(json) {
  state.automation = JSON.parse(json);
  return state.automation;
}
export const nextFrame = () => new Promise(resolve => requestAnimationFrame(resolve));
globalThis.luxelEditorAutomation = {
  snapshot: async () => applyAutomationSnapshot((await getAutomationExports()).AutomationSnapshot()),
  invoke: async (action, value = "") => {
    const exports = await getAutomationExports();
    if (action === "save-active" || action === "reset-demo") await exports.AutomationInvokeAsync(action, value);
    else exports.AutomationInvoke(action, value);
    await nextFrame();
    return applyAutomationSnapshot(exports.AutomationSnapshot());
  },
};
export function setReady(summary) {
  state.state = "ready"; state.summary = summary;
  status.textContent = summary; status.dataset.status = "ready"; error.hidden = true;
  void globalThis.luxelEditorAutomation.snapshot().catch(error => console.warn("Editor automation snapshot failed", error));
}
export function setFailure(message) {
  state.state = "failed"; state.summary = String(message);
  status.dataset.status = "failed"; status.textContent = "Luxel Editor failed to start";
  error.hidden = false; error.textContent = String(message);
}
globalThis.luxelEditorStartupFailure = reason => setFailure(
  `Luxel Editor failed to start.\nReason: ${String(reason)}\nFallback: use a browser/device with WebGPU enabled, or open this project in the native Editor.`);
state.capabilities = {
  indexedDb: Boolean(globalThis.indexedDB),
  fileSystemAccess: typeof globalThis.showDirectoryPicker === "function",
  archive: true,
  assetImport: false,
  processBuild: false,
  reveal: false
};
