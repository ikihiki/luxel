import { dotnet } from "./_framework/dotnet.js";

const protocol = "luxel-playground";
const protocolVersion = 2;
const query = new URLSearchParams(location.search);
const instanceId = query.get("instance") || crypto.randomUUID();
let ready = false;
let latestWorkspaceRevision = -1;
let completeExport = null, completeProjectExport = null;
let hoverExport = null, hoverProjectExport = null;
let analyzeExport = null, analyzeProjectExport = null;
let operation = Promise.resolve();
const queue = [];

function post(type, revision, payload = {}) { self.postMessage({ protocol, protocolVersion, type, instanceId, revision, ...payload }); }
function workspaceFrom(message) {
  const workspace = message?.workspace;
  if (!workspace || workspace.schemaVersion !== 2 || !Number.isSafeInteger(workspace.revision) || workspace.revision !== message.workspaceRevision || !Array.isArray(workspace.files) || !workspace.files.length)
    throw new Error("Invalid protocol v2 workspace snapshot.");
  const ids = new Set();
  for (const file of workspace.files) {
    if (!file || typeof file.id !== "string" || !file.id || ids.has(file.id) || typeof file.path !== "string" || typeof file.source !== "string" || !Number.isSafeInteger(file.version)) throw new Error("Invalid workspace file snapshot.");
    ids.add(file.id);
  }
  if (!ids.has(workspace.entryFileId) || !ids.has(workspace.activeFileId)) throw new Error("Workspace identity is invalid.");
  return workspace;
}
function decorateDiagnostics(diagnostics, workspace, fallbackFile) {
  return (Array.isArray(diagnostics) ? diagnostics : []).map(diagnostic => ({ ...diagnostic, workspaceRevision: Number(diagnostic.workspaceRevision ?? workspace.revision), fileId: diagnostic.fileId || fallbackFile.id, fileVersion: Number(diagnostic.fileVersion ?? fallbackFile.version), path: diagnostic.path || diagnostic.fileName || fallbackFile.path }));
}
async function apply(message) {
  if (!ready) { queue.push(message); return; }
  try {
    const workspace = workspaceFrom(message);
    if (workspace.revision < latestWorkspaceRevision) throw new Error("Stale workspace revision.");
    latestWorkspaceRevision = workspace.revision;
    const file = workspace.files.find(candidate => candidate.id === message.fileId);
    if (!file || file.version !== message.fileVersion) throw new Error("Language request file/version is stale or missing.");
    const projectJson = JSON.stringify(workspace);
    let json;
    if (message.kind === "completion" && Number.isInteger(message.position))
      json = completeProjectExport ? await completeProjectExport(projectJson, file.id, message.position, workspace.revision) : await completeExport(file.source, message.position, workspace.revision);
    else if (message.kind === "hover" && Number.isInteger(message.position))
      json = hoverProjectExport ? await hoverProjectExport(projectJson, file.id, message.position, workspace.revision) : await hoverExport(file.source, message.position, workspace.revision);
    else if (message.kind === "analysis")
      json = analyzeProjectExport ? await analyzeProjectExport(projectJson, file.id, workspace.revision) : await analyzeExport(file.source, workspace.revision);
    else throw new Error(`Unsupported language request '${message.kind}'.`);
    const result = JSON.parse(json);
    if (Array.isArray(result?.diagnostics)) result.diagnostics = decorateDiagnostics(result.diagnostics, workspace, file);
    post("language-response", workspace.revision, { requestId: message.requestId, kind: message.kind, workspaceRevision: workspace.revision, fileId: file.id, fileVersion: file.version, result: { ...result, workspaceRevision: workspace.revision, fileId: file.id, fileVersion: file.version, path: file.path } });
  } catch (error) {
    post("language-response", message.workspaceRevision || 0, { requestId: message.requestId, kind: message.kind, workspaceRevision: message.workspaceRevision, fileId: message.fileId, fileVersion: message.fileVersion, error: String(error?.message || error) });
  }
}
self.addEventListener("message", event => {
  const message = event.data;
  if (!message || message.protocol !== protocol || message.protocolVersion !== protocolVersion || message.instanceId !== instanceId || message.type !== "language-request") return;
  if (!Number.isSafeInteger(message.requestId) || !Number.isSafeInteger(message.workspaceRevision) || !message.workspace || typeof message.fileId !== "string" || !Number.isSafeInteger(message.fileVersion)) return;
  operation = operation.then(() => apply(message));
});
const host = {
  getMode: () => "language",
  getBaseUrl: () => new URL("./", location.href).href,
  setLanguageReady: () => {
    ready = true;
    post("language-ready", 0, { capabilities: { languages: { "csharp-script": { completion: true, hover: true, diagnostics: true }, csharp: { completion: true, hover: true, diagnostics: true }, slang: { completion: false, hover: false, diagnostics: false } } } });
    for (const message of queue.splice(0)) operation = operation.then(() => apply(message));
  },
  nextFrame: () => Promise.resolve(0), setReady: () => {},
  setFatalError: error => post("runtime-error", 0, { error: String(error) }), publishLog: () => {}, publishFirstFrame: () => {}
};
try {
  const runtime = await dotnet.create();
  runtime.setModuleImports("luxel-playground-host", host);
  const exports = await runtime.getAssemblyExports("LuxelPlaygroundBrowser.dll");
  const program = exports?.LuxelPlaygroundBrowser?.Program || exports?.Program;
  completeExport = program?.Complete; completeProjectExport = program?.CompleteProject || program?.CompleteWorkspace;
  hoverExport = program?.Hover; hoverProjectExport = program?.HoverProject || program?.HoverWorkspace;
  analyzeExport = program?.Analyze; analyzeProjectExport = program?.AnalyzeProject || program?.AnalyzeWorkspace;
  if ([[completeExport, completeProjectExport], [hoverExport, hoverProjectExport], [analyzeExport, analyzeProjectExport]].some(pair => pair.every(value => typeof value !== "function"))) throw new Error("Managed Playground language-service exports were not found.");
  await runtime.runMain();
} catch (error) { host.setFatalError(error?.stack || error); }
