import createSlangModule from "./slang/slang-wasm.js";

const SLANG_VERSION = "2026.14";
const stageValues = { vertex: 1, pixel: 5, fragment: 5, compute: 6 };
let modulePromise;
let compilerPromise;
let languagePromise;

function dispose(value) { try { value?.delete?.(); } catch { } }
function text(value) { return value == null ? "" : String(value); }
function moduleName(path) { return path.replace(/\\/g, "/").split("/").pop().replace(/\.(slang|slangh)$/i, ""); }
function uri(path) { return `file:///${path.replace(/^\/+/, "")}`; }
function position(source, offset) {
  offset = Math.max(0, Math.min(Number(offset) || 0, source.length));
  const before = source.slice(0, offset).split("\n");
  return { line: before.length - 1, character: before[before.length - 1].length };
}
function offset(source, point) {
  const lines = source.split("\n");
  const line = Math.max(0, Math.min(Number(point?.line) || 0, lines.length - 1));
  let result = 0;
  for (let i = 0; i < line; i++) result += lines[i].length + 1;
  return result + Math.max(0, Math.min(Number(point?.character) || 0, lines[line].length));
}
function values(list) {
  if (!list) return [];
  const result = [];
  try { for (let index = 0; index < list.size(); index++) result.push(list.get(index)); }
  finally { dispose(list); }
  return result;
}

const MAX_FILES = 128;
const MAX_SOURCE_BYTES = 2 * 1024 * 1024;
const MAX_OUTPUT_BYTES = 4 * 1024 * 1024;
let workspaceSequence = 0;
const encoder = new TextEncoder();

function normalizePath(path) {
  const normalized = text(path).replace(/\\/g, "/").replace(/^\/+/, "");
  const segments = normalized.split("/");
  if (!normalized || segments.some(segment => !segment || segment === "." || segment === "..") || normalized.includes(":"))
    throw new Error(`Invalid workspace path '${path}'.`);
  return normalized;
}
function validateFiles(files) {
  if (files.length > MAX_FILES) throw new Error(`Slang workspace has ${files.length} files; the limit is ${MAX_FILES}.`);
  let bytes = 0;
  const paths = new Set();
  for (const file of files) {
    file.path = normalizePath(file.path);
    if (paths.has(file.path)) throw new Error(`Duplicate Slang workspace path '${file.path}'.`);
    paths.add(file.path);
    bytes += encoder.encode(file.source).byteLength;
    if (bytes > MAX_SOURCE_BYTES) throw new Error(`Slang workspace source exceeds the ${MAX_SOURCE_BYTES} byte limit.`);
  }
}
function writeWorkspace(module, files) {
  validateFiles(files);
  const root = `/luxel-workspace-${++workspaceSequence}`;
  module.FS.mkdirTree(root);
  for (const file of files) {
    const fullPath = `${root}/${file.path}`;
    const slash = fullPath.lastIndexOf("/");
    module.FS.mkdirTree(fullPath.slice(0, slash));
    module.FS.writeFile(fullPath, file.source, { encoding: "utf8" });
  }
  return root;
}
function removeTree(fs, path) {
  try {
    for (const name of fs.readdir(path)) {
      if (name === "." || name === "..") continue;
      const child = `${path}/${name}`;
      try { fs.readdir(child); removeTree(fs, child); }
      catch { try { fs.unlink(child); } catch { } }
    }
    fs.rmdir(path);
  } catch { }
}
function sourceWithDefines(source, defines) {
  const lines = [];
  for (const [name, value] of Object.entries(defines || {}).sort(([a], [b]) => a.localeCompare(b))) {
    if (!/^[A-Za-z_]\w*$/.test(name)) throw new Error(`Invalid Slang define name '${name}'.`);
    lines.push(`#define ${name}${value == null || value === "" ? "" : ` ${value}`}`);
  }
  return lines.length ? `${lines.join("\n")}\n#line 1\n${source}` : source;
}

async function slangModule() {
  return modulePromise ||= createSlangModule({
    locateFile: path => new URL(`./slang/${path}`, import.meta.url).href,
    printErr: message => console.error(`[Slang ${SLANG_VERSION}]`, message)
  });
}

async function compiler() {
  return compilerPromise ||= (async () => {
    const module = await slangModule();
    const targets = module.getCompileTargets();
    const target = Array.from(targets).find(candidate => candidate.name === "WGSL")?.value;
    if (target == null) throw new Error(`Slang ${SLANG_VERSION} WASM does not expose the WGSL target.`);
    const globalSession = module.createGlobalSession();
    if (!globalSession) throw new Error("Failed to create the Slang global session.");
    return { module, target, globalSession };
  })();
}

function lastError(module, fallback) {
  const error = module.getLastError?.();
  const message = text(error?.message || error);
  dispose(error);
  return message || fallback;
}

export async function compile(requestJson) {
  const request = JSON.parse(requestJson);
  const diagnostics = [];
  let session, workspaceRoot;
  const owned = [];
  try {
    const state = await compiler();
    const path = normalizePath(request.path);
    const files = Object.entries(request.supportingFiles || {}).map(([supportPath, source]) => ({ path: supportPath, source: text(source) }));
    files.push({ path, source: sourceWithDefines(text(request.source), request.defines) });
    workspaceRoot = writeWorkspace(state.module, files);
    session = state.globalSession.createSession(state.target);
    if (!session) throw new Error("Failed to create a Slang WGSL session.");
    for (const file of files.slice(0, -1)) {
      const support = session.loadModuleFromSource(file.source, moduleName(file.path), `${workspaceRoot}/${file.path}`);
      if (!support) throw new Error(lastError(state.module, `Failed to load '${file.path}'.`));
      owned.push(support);
    }
    const rootSource = files[files.length - 1].source;
    const root = session.loadModuleFromSource(rootSource, moduleName(path), `${workspaceRoot}/${path}`);
    if (!root) throw new Error(lastError(state.module, `Failed to load '${path}'.`));
    owned.push(root);
    const components = [root];
    for (const entry of request.entryPoints || []) {
      const stage = stageValues[String(entry.stage).toLowerCase()];
      if (stage == null) throw new Error(`Unsupported Slang stage '${entry.stage}'.`);
      const point = root.findAndCheckEntryPoint(entry.name, stage);
      if (!point) throw new Error(lastError(state.module, `Entry point '${entry.name}' was not found.`));
      owned.push(point); components.push(point);
    }
    const composite = session.createCompositeComponentType(components);
    if (!composite) throw new Error(lastError(state.module, "Failed to create the Slang program."));
    owned.push(composite);
    const linked = composite.link();
    if (!linked) throw new Error(lastError(state.module, "Failed to link the Slang program."));
    owned.push(linked);
    const wgsl = linked.getTargetCode(0);
    if (!wgsl) throw new Error(lastError(state.module, "Slang produced no WGSL output."));
    if (encoder.encode(wgsl).byteLength > MAX_OUTPUT_BYTES) throw new Error(`Slang output exceeds the ${MAX_OUTPUT_BYTES} byte limit.`);
    return JSON.stringify({ success: true, wgsl, diagnostics, error: null });
  } catch (error) {
    return JSON.stringify({ success: false, wgsl: null, diagnostics, error: text(error?.message || error) });
  } finally {
    for (const value of owned.reverse()) dispose(value);
    dispose(session);
    if (workspaceRoot) { const module = await slangModule(); removeTree(module.FS, workspaceRoot); }
  }
}

async function languageServer() {
  return languagePromise ||= (async () => {
    const module = await slangModule();
    const server = module.createLanguageServer();
    if (!server) throw new Error("Failed to create the Slang language server.");
    return { module, server, documents: new Map(), fsFiles: new Set() };
  })();
}

async function syncWorkspace(workspace) {
  const state = await languageServer();
  const files = workspace.files.filter(file => file.language === "slang").map(file => ({ path: file.path, source: text(file.source) }));
  validateFiles(files);
  const next = new Map(files.map(file => [uri(file.path), file.source]));
  const nextPaths = new Set(files.map(file => `/${file.path}`));
  for (const existingPath of state.fsFiles) {
    if (!nextPaths.has(existingPath)) { try { state.module.FS.unlink(existingPath); } catch { } }
  }
  for (const file of files) {
    const fullPath = `/${file.path}`;
    state.module.FS.mkdirTree(fullPath.slice(0, fullPath.lastIndexOf("/")) || "/");
    state.module.FS.writeFile(fullPath, file.source, { encoding: "utf8" });
  }
  state.fsFiles = nextPaths;
  for (const existing of state.documents.keys()) if (!next.has(existing)) { state.server.didCloseTextDocument(existing); state.documents.delete(existing); }
  for (const [documentUri, source] of next) {
    if (state.documents.get(documentUri) === source) continue;
    if (state.documents.has(documentUri)) state.server.didCloseTextDocument(documentUri);
    state.server.didOpenTextDocument(documentUri, source);
    state.documents.set(documentUri, source);
  }
  return state;
}

function diagnosticSeverity(value) { return Number(value) === 2 ? "warning" : Number(value) >= 3 ? "info" : "error"; }
function parseCompilerDiagnostics(message, fallbackPath) {
  const lines = text(message).split(/\r?\n/), diagnostics = [];
  for (let index = 0; index < lines.length; index++) {
    const header = /^(fatal error|error|warning|note)(?:\[([^\]]+)\])?:\s*(.+)$/i.exec(lines[index]);
    if (!header) continue;
    const location = /^\s*-->\s*\/?(.+?):(\d+):(\d+)\s*$/.exec(lines[index + 1] || "");
    diagnostics.push({
      id: header[2] || "SLANG", message: header[3], severity: /warning/i.test(header[1]) ? "warning" : /note/i.test(header[1]) ? "info" : "error",
      line: location ? Number(location[2]) : null, column: location ? Number(location[3]) : null, length: 1,
      fileName: location?.[1] || fallbackPath
    });
  }
  return diagnostics.filter((diagnostic, index) => index === 0 || diagnostic.id !== diagnostics[index - 1].id || diagnostic.message !== diagnostics[index - 1].message);
}
async function compilerDiagnostics(workspace, file) {
  const state = await compiler(); let session; const owned = [];
  try {
    session = state.globalSession.createSession(state.target);
    if (!session) return [];
    for (const support of workspace.files.filter(candidate => candidate.language === "slang" && candidate.id !== file.id)) {
      const loaded = session.loadModuleFromSource(support.source, moduleName(support.path), `/${support.path}`);
      if (loaded) owned.push(loaded);
    }
    const root = session.loadModuleFromSource(file.source, moduleName(file.path), `/${file.path}`);
    if (root) { owned.push(root); return []; }
    return parseCompilerDiagnostics(lastError(state.module, ""), file.path);
  } finally {
    for (const value of owned.reverse()) dispose(value);
    dispose(session);
  }
}
export async function analyzeWorkspace(workspace, file) {
  const state = await syncWorkspace(workspace), documentUri = uri(file.path);
  let diagnostics = values(state.server.getDiagnostics(documentUri)).map(item => ({
    id: text(item.code) || "SLANG", message: text(item.message), severity: diagnosticSeverity(item.severity),
    line: Number(item.range?.start?.line ?? 0) + 1, column: Number(item.range?.start?.character ?? 0) + 1,
    length: Math.max(1, offset(file.source, item.range?.end) - offset(file.source, item.range?.start)), fileName: file.path
  }));
  if (diagnostics.length === 0) diagnostics = await compilerDiagnostics(workspace, file);
  return { revision: workspace.revision, diagnostics };
}
export async function completeWorkspace(workspace, file, sourceOffset) {
  const state = await syncWorkspace(workspace), documentUri = uri(file.path), pos = position(file.source, sourceOffset);
  const items = values(state.server.completion(documentUri, pos, { triggerKind: 1, triggerCharacter: "" })).slice(0, 200).map(item => ({
    label: text(item.label), insertText: text(item.textEdit?.text) || text(item.label), kind: String(item.kind || "Text"), detail: text(item.detail) || null,
    documentation: text(item.documentation?.value) || null
  }));
  return { revision: workspace.revision, replacementStart: sourceOffset, replacementLength: 0, items };
}
export async function hoverWorkspace(workspace, file, sourceOffset) {
  const state = await syncWorkspace(workspace), item = state.server.hover(uri(file.path), position(file.source, sourceOffset));
  if (!item) return null;
  const result = { revision: workspace.revision, markdown: text(item.contents?.value), start: offset(file.source, item.range?.start), length: Math.max(1, offset(file.source, item.range?.end) - offset(file.source, item.range?.start)) };
  return result;
}
export async function capabilities() {
  try { await languageServer(); return { completion: true, hover: true, diagnostics: true, version: SLANG_VERSION }; }
  catch (error) { console.error(error); return { completion: false, hover: false, diagnostics: false, version: SLANG_VERSION, error: text(error?.message || error) }; }
}
