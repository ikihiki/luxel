import { dotnet } from "./_framework/dotnet.js";
import * as webgpu from "./luxel-webgpu-browser.js";

const protocol = "luxel-playground";
const protocolVersion = 2;
const query = new URLSearchParams(location.search);
const instanceId = query.get("instance") || crypto.randomUUID();
const requestedParentOrigin = query.get("parentOrigin") || location.origin;
const parentOrigin = new URL(requestedParentOrigin, location.href).origin;
const mode = query.get("mode") === "language" ? "language" : "preview";
const status = document.getElementById("status");
const errorOverlay = document.getElementById("error");
let latestRevision = 0;
let ready = false;
let runExport = null;
let runProjectExport = null;
let completeExport = null;
let completeProjectExport = null;
let hoverExport = null;
let hoverProjectExport = null;
let analyzeExport = null;
let analyzeProjectExport = null;
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

function workspaceFrom(message) {
  const workspace = message?.workspace;
  if (!workspace || workspace.schemaVersion !== 2 || !Number.isSafeInteger(workspace.revision) || !Array.isArray(workspace.files) || !workspace.files.length) throw new Error("Invalid protocol v2 workspace snapshot.");
  const ids = new Set();
  for (const file of workspace.files) {
    if (!file || typeof file.id !== "string" || !file.id || ids.has(file.id) || typeof file.path !== "string" || typeof file.source !== "string" || !Number.isSafeInteger(file.version)) throw new Error("Invalid workspace file snapshot.");
    ids.add(file.id);
  }
  if (!ids.has(workspace.entryFileId) || !ids.has(workspace.activeFileId) || message.workspaceRevision !== workspace.revision) throw new Error("Workspace identity or revision mismatch.");
  return workspace;
}

function fileFrom(workspace, fileId) {
  return workspace.files.find(file => file.id === fileId) || null;
}

function decorateDiagnostics(diagnostics, workspace, fallbackFile) {
  return (Array.isArray(diagnostics) ? diagnostics : []).map(diagnostic => ({
    ...diagnostic,
    workspaceRevision: Number(diagnostic.workspaceRevision ?? workspace.revision),
    fileId: diagnostic.fileId || fallbackFile?.id || null,
    fileVersion: Number(diagnostic.fileVersion ?? fallbackFile?.version ?? 0),
    path: diagnostic.path || diagnostic.fileName || fallbackFile?.path || null
  }));
}

async function invokeLanguage(message) {
  const workspace = workspaceFrom(message);
  const file = fileFrom(workspace, message.fileId);
  if (!file || file.version !== message.fileVersion) throw new Error("Language request file/version is stale or missing.");
  const projectJson = JSON.stringify(workspace);
  let json;
  if (message.kind === "completion" && Number.isInteger(message.position))
    json = completeProjectExport ? await completeProjectExport(projectJson, file.id, message.position, workspace.revision) : await completeExport(file.source, message.position, workspace.revision);
  else if (message.kind === "hover" && Number.isInteger(message.position))
    json = hoverProjectExport ? await hoverProjectExport(projectJson, file.id, message.position, workspace.revision) : await hoverExport(file.source, message.position, workspace.revision);
  else if (message.kind === "analysis")
    json = analyzeProjectExport ? await analyzeProjectExport(projectJson, file.id, workspace.revision) : await analyzeExport(file.source, workspace.revision);
  else
    throw new Error(`Unsupported language request '${message.kind}'.`);
  const result = JSON.parse(json);
  if (Array.isArray(result?.diagnostics)) result.diagnostics = decorateDiagnostics(result.diagnostics, workspace, file);
  return { ...result, workspaceRevision: workspace.revision, fileId: file.id, fileVersion: file.version, path: file.path };
}

async function applyRun(message) {
  if ((!runExport && !runProjectExport) || !ready) { pendingRun = message; return; }
  status.textContent = `Compiling revision ${message.revision}…`;
  try {
    const workspace = workspaceFrom(message);
    const entry = fileFrom(workspace, workspace.entryFileId);
    if (!entry || entry.language !== "csharp-script") throw new Error("Workspace entry file must be a C# script.");
    const json = runProjectExport ? await runProjectExport(JSON.stringify(workspace), message.revision) : await runExport(entry.source, message.revision);
    const result = JSON.parse(json);
    const diagnostics = decorateDiagnostics(result.diagnostics, workspace, entry);
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

window.addEventListener("message", async event => {
  const message = event.data;
  if (event.source !== parent || event.origin !== parentOrigin) return;
  if (!message || message.protocol !== protocol || message.protocolVersion !== protocolVersion) return;
  if (message.instanceId !== instanceId) return;
  if (mode === "language" && message.type === "language-request") {
    if (!ready || !Number.isSafeInteger(message.requestId) || !Number.isSafeInteger(message.workspaceRevision)) return;
    try {
      const result = await invokeLanguage(message);
      post("language-response", message.workspaceRevision, { requestId: message.requestId, kind: message.kind, workspaceRevision: message.workspaceRevision, fileId: message.fileId, fileVersion: message.fileVersion, result });
    } catch (error) {
      post("language-response", message.workspaceRevision || 0, { requestId: message.requestId, kind: message.kind, workspaceRevision: message.workspaceRevision, fileId: message.fileId, fileVersion: message.fileVersion, error: String(error?.message || error) });
    }
    return;
  }
  if (mode !== "preview" || message.type !== "run") return;
  if (!Number.isSafeInteger(message.revision) || message.revision <= latestRevision || message.revision > 2147483647) return;
  if (!message.workspace || !Number.isSafeInteger(message.workspaceRevision)) return;
  latestRevision = message.revision;
  state.latestRevision = latestRevision;
  applyRun(message);
});

const host = {
  getMode: () => mode,
  setLanguageReady: () => {
    ready = true;
    state.ready = true;
    status.textContent = "Playground language services ready";
    post("language-ready", 0, { capabilities: { languages: { "csharp-script": { completion: true, hover: true, diagnostics: true }, csharp: { completion: true, hover: true, diagnostics: true }, slang: { completion: false, hover: false, diagnostics: false } } } });
  },
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
  const program = exports?.LuxelPlaygroundBrowser?.Program || exports?.Program;
  runExport = program?.Run;
  runProjectExport = program?.RunProject || program?.RunWorkspace;
  completeExport = program?.Complete;
  completeProjectExport = program?.CompleteProject || program?.CompleteWorkspace;
  hoverExport = program?.Hover;
  hoverProjectExport = program?.HoverProject || program?.HoverWorkspace;
  analyzeExport = program?.Analyze;
  analyzeProjectExport = program?.AnalyzeProject || program?.AnalyzeWorkspace;
  if (mode === "preview" && typeof runExport !== "function" && typeof runProjectExport !== "function") throw new Error("Managed playground Run export was not found.");
  if (mode === "language" && [[completeExport, completeProjectExport], [hoverExport, hoverProjectExport], [analyzeExport, analyzeProjectExport]].some(pair => pair.every(value => typeof value !== "function")))
    throw new Error("Managed Playground language-service exports were not found.");
  runtime.runMain().catch(error => {
    host.setFatalError(error?.stack || error);
    console.error(error);
  });
} catch (error) {
  host.setFatalError(error?.stack || error);
  console.error(error);
}
