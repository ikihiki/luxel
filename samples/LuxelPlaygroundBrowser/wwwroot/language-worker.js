import { dotnet } from "./_framework/dotnet.js";

const protocol = "luxel-playground";
const protocolVersion = 1;
const query = new URLSearchParams(location.search);
const instanceId = query.get("instance") || crypto.randomUUID();
let ready = false;
let completeExport = null;
let hoverExport = null;
let analyzeExport = null;
let operation = Promise.resolve();
const queue = [];

function post(type, revision, payload = {}) {
  self.postMessage({ protocol, protocolVersion, type, instanceId, revision, ...payload });
}

async function apply(message) {
  if (!ready) { queue.push(message); return; }
  try {
    let json;
    if (message.kind === "completion" && Number.isInteger(message.position))
      json = await completeExport(message.source, message.position, message.revision || 0);
    else if (message.kind === "hover" && Number.isInteger(message.position))
      json = await hoverExport(message.source, message.position, message.revision || 0);
    else if (message.kind === "analysis")
      json = await analyzeExport(message.source, message.revision || 0);
    else
      throw new Error(`Unsupported language request '${message.kind}'.`);
    post("language-response", message.revision || 0, { requestId: message.requestId, kind: message.kind, result: JSON.parse(json) });
  } catch (error) {
    post("language-response", message.revision || 0, { requestId: message.requestId, kind: message.kind, error: String(error?.message || error) });
  }
}

self.addEventListener("message", event => {
  const message = event.data;
  if (!message || message.protocol !== protocol || message.protocolVersion !== protocolVersion) return;
  if (message.instanceId !== instanceId || message.type !== "language-request") return;
  if (!Number.isSafeInteger(message.requestId) || typeof message.source !== "string") return;
  operation = operation.then(() => apply(message));
});

const host = {
  getMode: () => "language",
  getBaseUrl: () => new URL("./", location.href).href,
  setLanguageReady: () => {
    ready = true;
    post("language-ready", 0, { capabilities: { completion: true, hover: true, diagnostics: true } });
    for (const message of queue.splice(0)) operation = operation.then(() => apply(message));
  },
  nextFrame: () => Promise.resolve(0),
  setReady: () => {},
  setFatalError: error => post("runtime-error", 0, { error: String(error) }),
  publishLog: () => {},
  publishFirstFrame: () => {}
};

try {
  const runtime = await dotnet.create();
  runtime.setModuleImports("luxel-playground-host", host);
  const exports = await runtime.getAssemblyExports("LuxelPlaygroundBrowser.dll");
  const program = exports?.LuxelPlaygroundBrowser?.Program || exports?.Program;
  completeExport = program?.Complete;
  hoverExport = program?.Hover;
  analyzeExport = program?.Analyze;
  if ([completeExport, hoverExport, analyzeExport].some(value => typeof value !== "function"))
    throw new Error("Managed Playground language-service exports were not found.");
  await runtime.runMain();
} catch (error) {
  host.setFatalError(error?.stack || error);
}
