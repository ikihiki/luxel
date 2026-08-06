const DEFAULT_TIMEOUT_MS = 15_000;
const LANGUAGE_TIMEOUT_MS = 10_000;
let worker;
let nextId = 1;
const pending = new Map();
const compileRequests = new Map();

function resetWorker(reason) {
  const failed = worker;
  worker = null;
  try { failed?.terminate(); } catch { }
  for (const request of pending.values()) {
    clearTimeout(request.timer);
    request.reject(reason instanceof Error ? reason : new Error(String(reason)));
  }
  pending.clear();
  compileRequests.clear();
}

function slangWorker() {
  if (worker) return worker;
  worker = new Worker(new URL("./slang-worker.js", import.meta.url), { type: "module", name: "luxel-slang" });
  worker.addEventListener("message", event => {
    const message = event.data;
    const request = pending.get(message?.id);
    if (!request) return;
    pending.delete(message.id);
    clearTimeout(request.timer);
    if (request.compileRequestId != null) compileRequests.delete(request.compileRequestId);
    if (message.error) request.reject(new Error(message.error));
    else request.resolve(message.result);
  });
  worker.addEventListener("error", event => resetWorker(new Error(event.message || "The Slang worker failed.")));
  return worker;
}

function invoke(method, args, timeoutMs, compileRequestId = null) {
  const id = nextId++;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => resetWorker(new Error(`Slang ${method} timed out after ${timeoutMs} ms; the compiler worker was restarted.`)), timeoutMs);
    pending.set(id, { resolve, reject, timer, compileRequestId });
    if (compileRequestId != null) compileRequests.set(compileRequestId, id);
    slangWorker().postMessage({ id, method, args });
  });
}

export async function compile(requestJson) {
  const request = JSON.parse(requestJson);
  const timeoutMs = Math.max(1, Math.min(DEFAULT_TIMEOUT_MS, Number(request.timeoutMs) || DEFAULT_TIMEOUT_MS));
  return invoke("compile", [requestJson], timeoutMs, request.requestId);
}

export function cancel(requestId) {
  if (!compileRequests.has(requestId)) return;
  resetWorker(new DOMException("Slang compilation was canceled; the compiler worker was restarted.", "AbortError"));
}

export const analyzeWorkspace = (workspace, file) => invoke("analyzeWorkspace", [workspace, file], LANGUAGE_TIMEOUT_MS);
export const completeWorkspace = (workspace, file, sourceOffset) => invoke("completeWorkspace", [workspace, file, sourceOffset], LANGUAGE_TIMEOUT_MS);
export const hoverWorkspace = (workspace, file, sourceOffset) => invoke("hoverWorkspace", [workspace, file, sourceOffset], LANGUAGE_TIMEOUT_MS);
export const capabilities = () => invoke("capabilities", [], DEFAULT_TIMEOUT_MS);
