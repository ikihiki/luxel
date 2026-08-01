import { dotnet } from "./_framework/dotnet.js";
import * as webgpu from "./luxel-webgpu-browser.js";

const protocol = "luxel-playground";
const protocolVersion = 1;
const query = new URLSearchParams(location.search);
const instanceId = query.get("instance") || crypto.randomUUID();
const requestedParentOrigin = query.get("parentOrigin") || location.origin;
const parentOrigin = new URL(requestedParentOrigin, location.href).origin;
const status = document.getElementById("status");
const errorOverlay = document.getElementById("error");
let latestRevision = 0;
let ready = false;
let runExport = null;
let pendingRun = null;
const pendingDiagnostics = new Map();

const state = { protocol, protocolVersion, instanceId, parentOrigin, ready, latestRevision, device: null };
globalThis.luxelPlaygroundRuntimeState = state;

function post(type, revision, payload = {}) {
  if (parent === window) return;
  parent.postMessage({ protocol, protocolVersion, type, instanceId, revision, ...payload }, parentOrigin);
}

function setError(message) {
  const text = String(message || "Unknown playground runtime error.");
  status.textContent = "Runtime failed";
  errorOverlay.hidden = false;
  errorOverlay.textContent = text;
}

function publishDiagnostics(revision, diagnostics) {
  if (diagnostics.length > 0) post("diagnostics", revision, { diagnostics });
}

function applyRun(message) {
  if (!runExport || !ready) { pendingRun = message; return; }
  status.textContent = `Compiling revision ${message.revision}…`;
  try {
    const result = JSON.parse(runExport(message.source, message.revision));
    const diagnostics = Array.isArray(result.diagnostics) ? result.diagnostics : [];
    publishDiagnostics(message.revision, diagnostics);
    if (result.outcome === "diagnostics") {
      status.textContent = `Revision ${message.revision} has compile errors`;
      post("run-result", message.revision, { success: false, outcome: "compilation-failed", diagnostics });
      return;
    }
    if (result.outcome === "runtime-error") {
      status.textContent = `Revision ${message.revision} failed`;
      post("runtime-error", message.revision, { error: result.failure || { kind: "runtime", message: "Script execution failed." } });
      post("run-result", message.revision, { success: false, outcome: "runtime-failed", diagnostics });
      return;
    }
    if (result.outcome !== "render-pending") throw new Error(`Unknown managed run outcome '${result.outcome}'.`);
    pendingDiagnostics.set(message.revision, diagnostics);
    status.textContent = `Rendering revision ${message.revision}…`;
  } catch (error) {
    const failure = { kind: "infrastructure", message: String(error?.message || error), exceptionType: error?.name || null, line: null };
    status.textContent = `Revision ${message.revision} failed`;
    post("runtime-error", message.revision, { error: failure });
    post("run-result", message.revision, { success: false, outcome: "runtime-failed", diagnostics: [] });
  }
}

window.addEventListener("message", event => {
  const message = event.data;
  if (event.source !== parent || event.origin !== parentOrigin) return;
  if (!message || message.protocol !== protocol || message.protocolVersion !== protocolVersion) return;
  if (message.instanceId !== instanceId || message.type !== "run") return;
  if (!Number.isSafeInteger(message.revision) || message.revision <= latestRevision || message.revision > 2147483647) return;
  if (typeof message.source !== "string") return;
  latestRevision = message.revision;
  state.latestRevision = latestRevision;
  applyRun(message);
});

const host = {
  getBaseUrl: () => new URL("./", location.href).href,
  nextFrame: () => new Promise(resolve => requestAnimationFrame(resolve)),
  setReady: deviceName => {
    ready = true;
    Object.assign(state, { ready, device: deviceName });
    status.textContent = "Playground runtime ready";
    errorOverlay.hidden = true;
    post("ready", 0, { capabilities: { compile: true, webgpu: true, workerIsolation: false }, device: deviceName });
    if (pendingRun) { const message = pendingRun; pendingRun = null; applyRun(message); }
  },
  setFatalError: error => {
    setError(error);
    post("runtime-error", latestRevision, { error: { kind: "infrastructure", message: String(error), exceptionType: null, line: null } });
  },
  publishLog: (revision, level, message) => {
    if (!Number.isSafeInteger(revision) || revision !== latestRevision) return;
    const entries = state.logs ||= [];
    entries.push({ level: String(level || "information"), message: String(message || ""), timestamp: new Date().toISOString() });
    while (entries.length > 200) entries.shift();
    post("output", revision, { entries: [...entries] });
  },
  publishFirstFrame: revision => {
    if (!Number.isSafeInteger(revision) || revision !== latestRevision) return;
    const diagnostics = pendingDiagnostics.get(revision) || [];
    pendingDiagnostics.delete(revision);
    status.textContent = `Revision ${revision} rendered`;
    post("run-result", revision, { success: true, outcome: "rendered", firstFrame: true, diagnostics });
  },
};

try {
  const runtime = await dotnet.create();
  runtime.setModuleImports("./luxel-webgpu-browser.js", webgpu);
  runtime.setModuleImports("luxel-playground-host", host);
  const exports = await runtime.getAssemblyExports("LuxelPlaygroundBrowser.dll");
  runExport = exports?.LuxelPlaygroundBrowser?.Program?.Run || exports?.Program?.Run;
  if (typeof runExport !== "function") throw new Error("Managed playground Run export was not found.");
  runtime.runMain().catch(error => {
    host.setFatalError(error?.stack || error);
    console.error(error);
  });
} catch (error) {
  host.setFatalError(error?.stack || error);
  console.error(error);
}
