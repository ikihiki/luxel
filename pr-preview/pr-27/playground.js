(() => {
  "use strict";

  // Workspace source is persisted only in same-origin storage and carried in event detail to an
  // injected executor bridge. It is never placed in a URL, DOM attribute, telemetry, or developer output.
  const schemaVersion = 2;
  const memoryDrafts = new Map();
  const states = new WeakMap();
  const modelRoots = new Map();
  const storagePrefixV2 = "luxel.playground.workspace.v2:";
  const storagePrefixV1 = "luxel.playground.draft.v1:";
  const markerOwner = "luxel-language-service";
  const maxFiles = 128;
  const maxCSharpFileBytes = 128 * 1024;
  const maxWorkspaceBytes = 2 * 1024 * 1024;
  const supportedLanguages = new Set(["csharp-script", "csharp", "slang", "text", "plaintext", "json", "markdown", "xml", "html", "css", "javascript", "typescript"]);
  const utf8 = new TextEncoder();

  function storageKey(root, prefix = storagePrefixV2) { return prefix + (root.dataset.templateId || "default"); }
  function stored(key) { try { return localStorage.getItem(key) ?? memoryDrafts.get(key) ?? null; } catch { return memoryDrafts.get(key) ?? null; } }
  function store(key, value) { memoryDrafts.set(key, value); try { localStorage.setItem(key, value); } catch { /* memory fallback */ } }
  function removeStored(key) { memoryDrafts.delete(key); try { localStorage.removeItem(key); } catch { /* storage may be unavailable */ } }
  function emit(root, name, detail) { root.dispatchEvent(new CustomEvent(name, { bubbles: true, detail })); }
  function newId() { return globalThis.crypto?.randomUUID?.() || `file-${Date.now()}-${Math.random().toString(16).slice(2)}`; }
  function inferLanguage(path) {
    const lower = String(path).toLowerCase();
    if (lower.endsWith(".csx")) return "csharp-script";
    if (lower.endsWith(".cs")) return "csharp";
    if (lower.endsWith(".slang") || lower.endsWith(".slangh")) return "slang";
    return "text";
  }
  function monacoLanguage(language) { return language === "csharp" || language === "csharp-script" ? "csharp" : language === "slang" ? "slang" : "plaintext"; }
  function normalizePath(value) {
    const path = String(value || "").replaceAll("\\", "/").trim();
    if (!path || path.startsWith("/") || path.includes(":") || /[\u0000-\u001f\u007f]/.test(path))
      throw new Error("File path must be a relative workspace path.");
    const parts = path.split("/");
    if (parts.some(part => !part || part === "." || part === "..")) throw new Error("File path contains an invalid segment.");
    return parts.join("/");
  }
  function assertWorkspace(workspace) {
    if (!workspace || workspace.schemaVersion !== schemaVersion || !Array.isArray(workspace.files) || !workspace.files.length || workspace.files.length > maxFiles)
      throw new Error("Invalid Playground workspace.");
    const ids = new Set(), paths = new Set();
    let total = 0;
    for (const file of workspace.files) {
      if (!file || typeof file.id !== "string" || !file.id || ids.has(file.id)) throw new Error("Workspace file identity is invalid.");
      const path = normalizePath(file.path);
      const folded = path.toLowerCase();
      if (paths.has(folded)) throw new Error(`Workspace already contains '${path}'.`);
      if (typeof file.source !== "string") throw new Error(`Workspace file '${path}' has invalid source.`);
      file.path = path;
      file.language = typeof file.language === "string" && file.language ? file.language : inferLanguage(path);
      if (!supportedLanguages.has(file.language)) throw new Error(`Workspace file '${path}' has unsupported language '${file.language}'.`);
      const bytes = utf8.encode(file.source).byteLength;
      if ((file.language === "csharp" || file.language === "csharp-script") && bytes > maxCSharpFileBytes) throw new Error(`C# file '${path}' is too large.`);
      file.version = Number.isSafeInteger(file.version) && file.version >= 0 ? file.version : 1;
      total += bytes;
      ids.add(file.id); paths.add(folded);
    }
    if (total > maxWorkspaceBytes || !ids.has(workspace.entryFileId) || !ids.has(workspace.activeFileId)) throw new Error("Workspace entry, active file, or total size is invalid.");
    workspace.revision = Number.isSafeInteger(workspace.revision) && workspace.revision >= 0 ? workspace.revision : 0;
    return workspace;
  }
  function cloneWorkspace(workspace) { return { schemaVersion, revision: workspace.revision, entryFileId: workspace.entryFileId, activeFileId: workspace.activeFileId, files: workspace.files.map(file => ({ ...file })) }; }
  function initialWorkspace(root, sources) {
    const files = sources.map((source, index) => ({
      id: source.dataset.fileId || `template-file-${index}`,
      path: normalizePath(source.dataset.fileName || `File${index + 1}.cs`),
      language: source.dataset.fileLanguage || inferLanguage(source.dataset.fileName),
      source: source.value,
      version: Number(source.dataset.fileVersion ?? "1")
    }));
    const entryFileId = root.dataset.entryFileId || files[0].id;
    return assertWorkspace({ schemaVersion, revision: Number(root.dataset.workspaceRevision ?? "0"), entryFileId, activeFileId: root.dataset.activeFileId || entryFileId, files });
  }
  function loadWorkspace(root, initial) {
    const v2 = stored(storageKey(root));
    if (v2 !== null) {
      try { return assertWorkspace(JSON.parse(v2)); } catch { removeStored(storageKey(root)); }
    }
    const v1Key = storageKey(root, storagePrefixV1);
    const v1 = stored(v1Key);
    if (v1 !== null) {
      const migrated = cloneWorkspace(initial);
      const entry = migrated.files.find(file => file.id === migrated.entryFileId);
      if (entry) { entry.source = v1; entry.version++; migrated.revision++; }
      removeStored(v1Key);
      saveWorkspace(root, migrated);
      return migrated;
    }
    return cloneWorkspace(initial);
  }
  function saveWorkspace(root, workspace) { store(storageKey(root), JSON.stringify(cloneWorkspace(workspace))); }
  function clearWorkspace(root) { removeStored(storageKey(root)); removeStored(storageKey(root, storagePrefixV1)); }
  function activeFile(state) { return state.workspace.files.find(file => file.id === state.workspace.activeFileId); }
  function fileById(state, id) { return state.workspace.files.find(file => file.id === id); }
  function workspaceChanged(root, state, file, source) {
    if (source !== undefined && source !== file.source) {
      const candidate = cloneWorkspace(state.workspace), candidateFile = candidate.files.find(item => item.id === file.id);
      candidateFile.source = source; candidateFile.version++;
      assertWorkspace(candidate);
      file.source = source; file.version++; state.workspace.revision++;
    }
    state.source.value = file.source;
    state.source.dataset.fileId = file.id;
    state.source.dataset.fileName = file.path;
    state.source.dataset.fileLanguage = file.language;
    state.source.dataset.fileVersion = String(file.version);
    saveWorkspace(root, state.workspace);
    emit(root, "luxel-playground:draft-changed", { workspace: cloneWorkspace(state.workspace), fileId: file.id, fileVersion: file.version, path: file.path });
  }
  function languageRequest(root, kind, state, file, position = null) {
    const requestId = ++state.languageRequestId;
    const snapshot = cloneWorkspace(state.workspace);
    return new Promise((resolve, reject) => emit(root, "luxel-playground:language-request", {
      kind, requestId, workspace: snapshot, workspaceRevision: snapshot.revision, fileId: file.id,
      fileVersion: file.version, path: file.path, position, resolve, reject
    }));
  }
  function responseIsCurrent(state, file, result) {
    return result && Number(result.workspaceRevision ?? state.workspace.revision) === state.workspace.revision &&
      String(result.fileId ?? file.id) === file.id && Number(result.fileVersion ?? file.version) === file.version;
  }

  function completionKind(monaco, kind) {
    const value = String(kind || "").toLowerCase();
    if (value.includes("method")) return monaco.languages.CompletionItemKind.Method;
    if (value.includes("property")) return monaco.languages.CompletionItemKind.Property;
    if (value.includes("field")) return monaco.languages.CompletionItemKind.Field;
    if (value.includes("class") || value.includes("type")) return monaco.languages.CompletionItemKind.Class;
    if (value.includes("namespace")) return monaco.languages.CompletionItemKind.Module;
    if (value.includes("keyword")) return monaco.languages.CompletionItemKind.Keyword;
    return monaco.languages.CompletionItemKind.Text;
  }
  function registerLanguageSupport(monaco) {
    if (window.LuxelPlaygroundLanguageRegistered) return;
    window.LuxelPlaygroundLanguageRegistered = true;
    if (!monaco.languages.getLanguages().some(language => language.id === "slang")) {
      monaco.languages.register({ id: "slang", extensions: [".slang", ".slangh"] });
      monaco.languages.setMonarchTokensProvider("slang", { tokenizer: { root: [[/[a-zA-Z_]\w*/, { cases: { "@keywords": "keyword", "@default": "identifier" } }], [/\d+(\.\d+)?/, "number"], [/"([^"\\]|\\.)*$/, "string.invalid"], [/"/, { token: "string.quote", bracket: "@open", next: "@string" }], [/\/\//, "comment", "@lineComment"]], string: [[/[^\\"]+/, "string"], [/\\./, "string.escape"], [/"/, { token: "string.quote", bracket: "@close", next: "@pop" }]], lineComment: [[/.*/, "comment"]] }, keywords: ["struct", "class", "interface", "import", "return", "if", "else", "for", "while", "let", "var", "static", "public", "private", "void", "float", "float2", "float3", "float4"] });
    }
    for (const language of ["csharp", "slang"]) {
      monaco.languages.registerCompletionItemProvider(language, { triggerCharacters: [".", " "], async provideCompletionItems(model, position) {
        const pair = modelRoots.get(model.uri.toString());
        if (!pair) return { suggestions: [] };
        const { root, fileId } = pair, state = states.get(root), file = state && fileById(state, fileId);
        if (!state || !file) return { suggestions: [] };
        const offset = model.getOffsetAt(position);
        const result = await languageRequest(root, "completion", state, file, offset).catch(() => null);
        if (!responseIsCurrent(state, file, result) || !result?.items) return { suggestions: [] };
        const start = model.getPositionAt(Number(result.replacementStart ?? offset));
        const end = model.getPositionAt(Number(result.replacementStart ?? offset) + Number(result.replacementLength || 0));
        const range = new monaco.Range(start.lineNumber, start.column, end.lineNumber, end.column);
        return { suggestions: result.items.map(item => ({ label: item.label, insertText: item.insertText || item.label, documentation: item.documentation || undefined, detail: item.detail || `${file.language} language service`, kind: completionKind(monaco, item.kind), range })) };
      }});
      monaco.languages.registerHoverProvider(language, { async provideHover(model, position) {
        const pair = modelRoots.get(model.uri.toString());
        if (!pair) return null;
        const { root, fileId } = pair, state = states.get(root), file = state && fileById(state, fileId);
        if (!state || !file) return null;
        const result = await languageRequest(root, "hover", state, file, model.getOffsetAt(position)).catch(() => null);
        if (!responseIsCurrent(state, file, result) || !result?.markdown) return null;
        const start = model.getPositionAt(Number(result.start || 0)), end = model.getPositionAt(Number(result.start || 0) + Number(result.length || 0));
        return { range: new monaco.Range(start.lineNumber, start.column, end.lineNumber, end.column), contents: [{ value: `\`\`\`${language}\n${result.markdown}\n\`\`\`` }] };
      }});
    }
  }
  function renderFileList(root, state) {
    const list = root.querySelector("[data-playground-file-list]");
    if (!list) return;
    list.replaceChildren();
    for (const file of state.workspace.files) {
      const selected = file.id === state.workspace.activeFileId;
      const button = document.createElement("button");
      button.type = "button"; button.setAttribute("role", "tab"); button.dataset.playgroundFileSelect = ""; button.dataset.fileId = file.id;
      button.title = file.path; button.tabIndex = selected ? 0 : -1;
      button.setAttribute("aria-controls", `${root.id}-file-editor`);
      button.setAttribute("aria-selected", String(selected));
      button.textContent = file.path + (file.id === state.workspace.entryFileId ? " ●" : "");
      button.addEventListener("click", () => selectFile(root, file.id));
      list.append(button);
    }
    const selectedTab = list.querySelector('[role="tab"][aria-selected="true"]');
    selectedTab?.scrollIntoView({ block: "nearest", inline: "nearest" });
    const active = activeFile(state);
    const label = root.querySelector("[data-playground-active-file-label]");
    if (label && active) label.textContent = active.path;
    root.dataset.activeFileId = active?.id || "";
  }
  function createModel(root, state, file) {
    const uri = state.monaco.Uri.parse(`inmemory://luxel/${encodeURIComponent(root.dataset.templateId || "default")}/${encodeURIComponent(file.id)}`);
    const model = state.monaco.editor.createModel(file.source, monacoLanguage(file.language), uri);
    modelRoots.set(uri.toString(), { root, fileId: file.id });
    const record = { model, subscription: null, viewState: null };
    record.subscription = model.onDidChangeContent(() => {
      workspaceChanged(root, state, file, model.getValue());
      clearTimeout(record.analysisTimer);
      record.analysisTimer = setTimeout(() => analyzeFile(root, state, file), 450);
    });
    state.models.set(file.id, record);
    return record;
  }
  async function analyzeFile(root, state, file) {
    if (!state.models.has(file.id) || !["csharp", "csharp-script", "slang"].includes(file.language)) return;
    const result = await languageRequest(root, "analysis", state, file).catch(() => null);
    if (responseIsCurrent(state, file, result) && Array.isArray(result.diagnostics)) setDiagnostics(root, result.diagnostics, result);
  }
  function selectFile(root, fileId, reveal = null, recordChange = true) {
    const state = states.get(root), file = state && fileById(state, fileId);
    if (!state || !file) return false;
    const previous = state.models.get(state.workspace.activeFileId);
    if (state.editor && previous) previous.viewState = state.editor.saveViewState();
    state.workspace.activeFileId = file.id;
    if (recordChange) state.workspace.revision++;
    state.source.value = file.source; state.source.dataset.fileId = file.id; state.source.dataset.fileName = file.path; state.source.dataset.fileLanguage = file.language; state.source.dataset.fileVersion = String(file.version);
    if (state.editor) {
      const record = state.models.get(file.id) || createModel(root, state, file);
      state.editor.setModel(record.model);
      if (record.viewState) state.editor.restoreViewState(record.viewState);
      state.editor.updateOptions({ ariaLabel: `${file.path} code editor` });
      if (reveal) { const position = { lineNumber: Math.max(1, Number(reveal.line || reveal.startLine || 1)), column: Math.max(1, Number(reveal.column || reveal.startColumn || 1)) }; state.editor.setPosition(position); state.editor.revealPositionInCenter(position); state.editor.focus(); }
    }
    renderFileList(root, state);
    if (recordChange) {
      saveWorkspace(root, state.workspace);
      emit(root, "luxel-playground:file-selected", { fileId: file.id, path: file.path, workspaceRevision: state.workspace.revision });
    }
    return true;
  }
  function addFile(root, path, language = null, source = "") {
    const state = states.get(root); if (!state) return null;
    path = normalizePath(path);
    if (state.workspace.files.length >= maxFiles || state.workspace.files.some(file => file.path.toLowerCase() === path.toLowerCase())) throw new Error(`Cannot add '${path}'.`);
    const file = { id: newId(), path, language: language || inferLanguage(path), source: String(source), version: 1 };
    const candidate = cloneWorkspace(state.workspace); candidate.files.push({ ...file });
    assertWorkspace(candidate);
    state.workspace.files.push(file); state.workspace.revision++;
    if (state.monaco) createModel(root, state, file);
    selectFile(root, file.id); saveWorkspace(root, state.workspace);
    emit(root, "luxel-playground:file-added", { file: { ...file }, workspaceRevision: state.workspace.revision });
    return file.id;
  }
  function renameFile(root, fileId, path) {
    const state = states.get(root), file = state && fileById(state, fileId); if (!state || !file) return false;
    path = normalizePath(path);
    if (state.workspace.files.some(other => other.id !== file.id && other.path.toLowerCase() === path.toLowerCase())) throw new Error(`Workspace already contains '${path}'.`);
    const oldPath = file.path;
    const language = file.language === inferLanguage(oldPath) ? inferLanguage(path) : file.language;
    const candidate = cloneWorkspace(state.workspace), candidateFile = candidate.files.find(item => item.id === file.id);
    candidateFile.path = path; candidateFile.language = language;
    assertWorkspace(candidate);
    file.path = path; file.language = language; file.version++; state.workspace.revision++;
    const record = state.models.get(file.id); if (record) state.monaco.editor.setModelLanguage(record.model, monacoLanguage(file.language));
    workspaceChanged(root, state, file); renderFileList(root, state);
    emit(root, "luxel-playground:file-renamed", { fileId, oldPath, path, fileVersion: file.version, workspaceRevision: state.workspace.revision });
    return true;
  }
  function deleteFile(root, fileId, replacementEntryFileId = null) {
    const state = states.get(root), file = state && fileById(state, fileId); if (!state || !file || state.workspace.files.length === 1) return false;
    if (file.id === state.workspace.entryFileId) {
      const replacement = replacementEntryFileId && fileById(state, replacementEntryFileId);
      if (!replacement || replacement.id === file.id || replacement.language !== "csharp-script") throw new Error("Deleting the entry file requires a replacement C# script entry file.");
      state.workspace.entryFileId = replacement.id;
    }
    const index = state.workspace.files.indexOf(file); state.workspace.files.splice(index, 1); state.workspace.revision++;
    const record = state.models.get(file.id); if (record) { clearTimeout(record.analysisTimer); record.subscription?.dispose(); modelRoots.delete(record.model.uri.toString()); record.model.dispose(); state.models.delete(file.id); }
    if (state.workspace.activeFileId === file.id) state.workspace.activeFileId = state.workspace.files[Math.min(index, state.workspace.files.length - 1)].id;
    selectFile(root, state.workspace.activeFileId); saveWorkspace(root, state.workspace);
    emit(root, "luxel-playground:file-deleted", { fileId, path: file.path, workspaceRevision: state.workspace.revision });
    return true;
  }
  async function initializeMonaco(root, state) {
    const mount = root.querySelector("[data-playground-monaco]"); if (!mount || !window.LuxelMonacoReady) return;
    try {
      const monaco = await window.LuxelMonacoReady; if (!root.isConnected || states.get(root) !== state) return;
      state.monaco = monaco; registerLanguageSupport(monaco);
      for (const file of state.workspace.files) createModel(root, state, file);
      state.editor = monaco.editor.create(mount, { model: state.models.get(state.workspace.activeFileId).model, theme: "vs-dark", automaticLayout: true, ariaLabel: `${activeFile(state).path} code editor`, minimap: { enabled: false }, fontSize: 13, tabSize: 4, insertSpaces: true, scrollBeyondLastLine: false, quickSuggestions: true, suggestOnTriggerCharacters: true });
      root.querySelector("[data-playground-editor-host]")?.classList.add("monaco-ready"); root.dataset.playgroundEditor = "monaco";
      for (const file of state.workspace.files) setTimeout(() => analyzeFile(root, state, file), 0);
      emit(root, "luxel-playground:editor-ready", { editor: "monaco", languages: [...new Set(state.workspace.files.map(file => file.language))] });
    } catch { root.dataset.playgroundEditor = "textarea"; }
  }
  function getValue(root) { const state = states.get(root); return activeFile(state || { workspace: { files: [] } })?.source ?? ""; }
  function setValue(root, value) {
    const state = states.get(root), file = state && activeFile(state); if (!state || !file) return;
    const text = String(value ?? ""), record = state.models.get(file.id);
    if (record) record.model.setValue(text); else workspaceChanged(root, state, file, text);
  }
  function getWorkspace(root) { const state = states.get(root); return state ? cloneWorkspace(state.workspace) : null; }
  function diagnosticFile(state, diagnostic) { return fileById(state, diagnostic.fileId) || state.workspace.files.find(file => file.path === diagnostic.path || file.path === diagnostic.fileName) || activeFile(state); }
  function setDiagnostics(root, diagnostics, guard = null) {
    const state = states.get(root); if (!state?.monaco) return;
    if (guard && Number(guard.workspaceRevision ?? state.workspace.revision) !== state.workspace.revision) return;
    const grouped = new Map(state.workspace.files.map(file => [file.id, []]));
    for (const diagnostic of Array.isArray(diagnostics) ? diagnostics : []) {
      const file = diagnosticFile(state, diagnostic); if (!file || (diagnostic.fileVersion && Number(diagnostic.fileVersion) !== file.version)) continue;
      grouped.get(file.id).push({ severity: String(diagnostic.severity || "error").toLowerCase() === "warning" ? state.monaco.MarkerSeverity.Warning : state.monaco.MarkerSeverity.Error, message: String(diagnostic.message || ""), code: String(diagnostic.code || diagnostic.id || ""), startLineNumber: Math.max(1, Number(diagnostic.line || diagnostic.startLine || 1)), startColumn: Math.max(1, Number(diagnostic.column || diagnostic.startColumn || 1)), endLineNumber: Math.max(1, Number(diagnostic.endLine || diagnostic.line || diagnostic.startLine || 1)), endColumn: Math.max(2, Number(diagnostic.endColumn || 0) || Number(diagnostic.column || diagnostic.startColumn || 1) + Math.max(1, Number(diagnostic.length || 1))) });
    }
    const guardedFileId = guard?.fileId || null;
    for (const file of state.workspace.files) {
      if (guardedFileId && file.id !== guardedFileId) continue;
      const record = state.models.get(file.id);
      if (record) state.monaco.editor.setModelMarkers(record.model, markerOwner, grouped.get(file.id));
    }
  }
  function diagnostics(root) { const state = states.get(root); return state?.monaco ? state.monaco.editor.getModelMarkers({ owner: markerOwner }) : []; }
  function triggerSuggest(root) { const editor = states.get(root)?.editor; if (!editor) return false; editor.focus(); editor.setPosition(editor.getModel().getPositionAt(editor.getModel().getValueLength())); editor.trigger("luxel-playground", "editor.action.triggerSuggest", {}); return true; }
  function navigateFileTabs(root, event) {
    if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) return;
    const current = event.target.closest?.('[role="tab"][data-playground-file-select]');
    const tabs = [...root.querySelectorAll('[data-playground-file-list] [role="tab"]')];
    const index = tabs.indexOf(current);
    if (index < 0 || !tabs.length) return;
    event.preventDefault();
    let nextIndex;
    if (event.key === "Home") nextIndex = 0;
    else if (event.key === "End") nextIndex = tabs.length - 1;
    else nextIndex = (index + (["ArrowRight", "ArrowDown"].includes(event.key) ? 1 : -1) + tabs.length) % tabs.length;
    const fileId = tabs[nextIndex].dataset.fileId;
    if (!selectFile(root, fileId)) return;
    const selected = [...root.querySelectorAll('[data-playground-file-list] [role="tab"]')]
      .find(tab => tab.dataset.fileId === fileId);
    selected?.focus();
  }
  function bind(root) {
    if (root.dataset.playgroundBound === "true") return; root.dataset.playgroundBound = "true";
    const sources = [...root.querySelectorAll("[data-playground-source]")], run = root.querySelector("[data-playground-run]"), cancel = root.querySelector("[data-playground-cancel]"), reset = root.querySelector("[data-playground-reset]");
    if (!sources.length || !run || !cancel || !reset) return;
    const initial = initialWorkspace(root, sources), workspace = loadWorkspace(root, initial), source = sources.find(item => item.dataset.fileId === workspace.activeFileId) || sources[0];
    for (const extra of sources) if (extra !== source) extra.remove();
    const state = { source, initial, workspace, monaco: null, models: new Map(), editor: null, languageRequestId: 0 };
    states.set(root, state); renderFileList(root, state); selectFile(root, workspace.activeFileId, null, false);
    source.addEventListener("input", () => { const file = activeFile(state); if (file) workspaceChanged(root, state, file, source.value); });
    run.addEventListener("click", () => {
      const executionId = Number(root.dataset.executionId || "0") + 1;
      root.dataset.executionId = String(executionId);
      run.disabled = true;
      cancel.disabled = false;
      setDiagnostics(root, []);
      emit(root, "luxel-playground:execute", { executionId, request: { workspace: cloneWorkspace(state.workspace) } });
    });
    cancel.addEventListener("click", () => {
      cancel.disabled = true;
      run.disabled = false;
      emit(root, "luxel-playground:cancel", { executionId: Number(root.dataset.executionId || "0"), workspaceRevision: state.workspace.revision });
    });
    reset.addEventListener("click", () => { clearWorkspace(root); for (const record of state.models.values()) { clearTimeout(record.analysisTimer); record.subscription?.dispose(); modelRoots.delete(record.model.uri.toString()); record.model.dispose(); } state.models.clear(); state.workspace = cloneWorkspace(state.initial); if (state.monaco) for (const file of state.workspace.files) createModel(root, state, file); selectFile(root, state.workspace.activeFileId); setDiagnostics(root, []); emit(root, "luxel-playground:reset", { templateId: root.dataset.templateId, workspace: cloneWorkspace(state.workspace) }); });
    root.querySelector("[data-playground-file-list]")?.addEventListener("keydown", event => navigateFileTabs(root, event));
    root.querySelector("[data-playground-file-add]")?.addEventListener("click", () => { const path = prompt("New workspace file path (for example, Helper.cs)"); if (path) try { addFile(root, path); } catch (error) { alert(error.message); } });
    root.querySelector("[data-playground-file-rename]")?.addEventListener("click", () => { const file = activeFile(state), path = file && prompt("Rename workspace file", file.path); if (path) try { renameFile(root, file.id, path); } catch (error) { alert(error.message); } });
    root.querySelector("[data-playground-file-delete]")?.addEventListener("click", () => { const file = activeFile(state); if (!file || !confirm(`Delete ${file.path}?`)) return; try { deleteFile(root, file.id); } catch (error) { alert(error.message); } });
    initializeMonaco(root, state);
  }
  function bindAll(scope = document) { scope.querySelectorAll("[data-playground]").forEach(bind); }
  function dispose(root) { const state = states.get(root); state?.editor?.dispose(); for (const record of state?.models?.values?.() || []) { clearTimeout(record.analysisTimer); record.subscription?.dispose(); modelRoots.delete(record.model.uri.toString()); record.model.dispose(); } states.delete(root); }
  window.LuxelPlayground = Object.freeze({ bind, bindAll, dispose, getValue, setValue, getWorkspace, setDiagnostics, diagnostics, triggerSuggest, selectFile, addFile, renameFile, deleteFile });
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", () => bindAll(), { once: true }); else bindAll();
})();
