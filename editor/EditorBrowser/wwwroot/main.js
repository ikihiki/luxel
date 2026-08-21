const status = document.getElementById("status");
const error = document.getElementById("error");
const state = globalThis.luxelEditorState = { state: "loading", summary: "", dirty: false, capabilities: {} };
export const nextFrame = () => new Promise(resolve => requestAnimationFrame(resolve));
export function setReady(summary) {
  state.state = "ready"; state.summary = summary;
  status.textContent = summary; status.dataset.status = "ready"; error.hidden = true;
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
