const dbName = "luxel-editor-v1";
const storeName = "files";
const directoryHandles = new Map();
let dirty = false;

const openDb = () => new Promise((resolve, reject) => {
  if (!globalThis.indexedDB) { reject(new DOMException("IndexedDB unavailable", "InvalidStateError")); return; }
  const request = indexedDB.open(dbName, 1);
  request.onupgradeneeded = () => request.result.createObjectStore(storeName, { keyPath: "key" });
  request.onsuccess = () => resolve(request.result);
  request.onerror = () => reject(request.error || new DOMException("IndexedDB unavailable", "InvalidStateError"));
});
const requestResult = value => new Promise((resolve, reject) => {
  value.onsuccess = () => resolve(value.result);
  value.onerror = () => reject(value.error || new DOMException("IndexedDB request failed", "UnknownError"));
});
const transactionComplete = tx => new Promise((resolve, reject) => {
  tx.addEventListener("complete", resolve, { once: true });
  tx.addEventListener("abort", () => reject(tx.error || new DOMException("IndexedDB transaction aborted", "AbortError")), { once: true });
  tx.addEventListener("error", () => reject(tx.error || new DOMException("IndexedDB transaction failed", "UnknownError")), { once: true });
});
async function withStore(mode, action) {
  const db = await openDb();
  const tx = db.transaction(storeName, mode);
  const completed = transactionComplete(tx);
  try {
    const result = await action(tx.objectStore(storeName));
    await completed;
    return result;
  } finally {
    db.close();
  }
}
const prefix = workspace => `${workspace}\0`;

export async function loadWorkspace(workspace) {
  const rows = await withStore("readonly", store => requestResult(store.getAll()));
  const result = {};
  for (const row of rows) if (row.key.startsWith(prefix(workspace))) result[row.path] = row.content;
  return JSON.stringify(result);
}
export async function saveFile(workspace, path, content) {
  await withStore("readwrite", store => requestResult(store.put({ key: prefix(workspace) + path, workspace, path, content })));
}
export async function deleteFile(workspace, path) {
  await withStore("readwrite", store => requestResult(store.delete(prefix(workspace) + path)));
}

export const readSetting = key => localStorage.getItem(`luxel.editor.${key}`);
export const writeSetting = (key, value) => localStorage.setItem(`luxel.editor.${key}`, value);
export function pickProject() {
  const choice = prompt("Open project: enter 'demo' for the built-in project or 'workspace' for the persistent IndexedDB workspace.", "demo");
  if (choice == null) return null;
  return choice.trim().toLowerCase() === "workspace" ? "indexeddb:default" : "builtin:demo";
}
export const hasFileSystemAccess = () => typeof globalThis.showDirectoryPicker === "function";

const chooseFile = accept => new Promise((resolve, reject) => {
  const input = document.createElement("input");
  input.type = "file"; input.accept = accept;
  input.onchange = () => resolve(input.files?.[0] || null);
  input.oncancel = () => reject(new DOMException("File selection cancelled", "AbortError"));
  input.click();
});
export async function importArchive() {
  const file = await chooseFile(".luxel-project.json,application/json");
  if (!file) throw new DOMException("File selection cancelled", "AbortError");
  const archive = JSON.parse(await file.text());
  return JSON.stringify({ name: file.name.replace(/\.luxel-project\.json$/i, "") || "archive", files: archive.files || archive });
}
export async function exportArchive(workspace, filesJson) {
  const blob = new Blob([JSON.stringify({ format: "luxel-project-v1", workspace, files: JSON.parse(filesJson) }, null, 2)], { type: "application/json" });
  const link = document.createElement("a");
  link.href = URL.createObjectURL(blob); link.download = `${workspace || "project"}.luxel-project.json`; link.click();
  setTimeout(() => URL.revokeObjectURL(link.href), 0);
}
async function readDirectory(handle, base = "", result = {}) {
  for await (const [name, child] of handle.entries()) {
    const path = base ? `${base}/${name}` : name;
    if (child.kind === "directory") await readDirectory(child, path, result);
    else result[path] = await (await child.getFile()).text();
  }
  return result;
}
const newSourceId = () => `folder:${globalThis.crypto?.randomUUID?.() || `${Date.now()}-${Math.random()}`}`;
export async function openFileSystemFolder() {
  if (!hasFileSystemAccess()) throw new DOMException("File System Access API unavailable", "NotSupportedError");
  const handle = await showDirectoryPicker({ mode: "readwrite" });
  const sourceId = newSourceId();
  const files = await readDirectory(handle);
  directoryHandles.set(sourceId, handle);
  return JSON.stringify({ name: handle.name || "folder", sourceId, files });
}
async function clearDirectory(handle) {
  const names = [];
  for await (const name of handle.keys()) names.push(name);
  for (const name of names) await handle.removeEntry(name, { recursive: true });
}
async function writeDirectory(handle, files) {
  for (const [path, content] of Object.entries(files)) {
    const parts = path.split("/"); let directory = handle;
    for (const part of parts.slice(0, -1)) directory = await directory.getDirectoryHandle(part, { create: true });
    const file = await directory.getFileHandle(parts.at(-1), { create: true });
    const writer = await file.createWritable();
    try { await writer.write(content); } finally { await writer.close(); }
  }
}
export async function saveFileSystemFolder(sourceId, filesJson) {
  if (!hasFileSystemAccess()) throw new DOMException("File System Access API unavailable", "NotSupportedError");
  let handle = sourceId ? directoryHandles.get(sourceId) : null;
  if (!handle) {
    handle = await showDirectoryPicker({ mode: "readwrite" });
    sourceId = newSourceId();
    directoryHandles.set(sourceId, handle);
  }
  const files = JSON.parse(filesJson);
  await clearDirectory(handle);
  await writeDirectory(handle, files);
  return sourceId;
}
export function setDirty(value) { dirty = Boolean(value); globalThis.luxelEditorState && (globalThis.luxelEditorState.dirty = dirty); }
addEventListener("beforeunload", event => { if (dirty) { event.preventDefault(); event.returnValue = ""; } });
