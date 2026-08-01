(() => {
  "use strict";

  // Source is persisted only in same-origin storage and carried in event detail to an injected
  // executor bridge. It is never placed in a URL, DOM attribute, telemetry, or console output.
  const memoryDrafts = new Map();
  const states = new WeakMap();
  const modelRoots = new Map();
  const storagePrefix = "luxel.playground.draft.v1:";
  const markerOwner = "luxel-roslyn";

  function storageKey(root) {
    return storagePrefix + (root.dataset.templateId || "default");
  }

  function loadDraft(root) {
    const key = storageKey(root);
    try { return localStorage.getItem(key) ?? memoryDrafts.get(key) ?? null; }
    catch { return memoryDrafts.get(key) ?? null; }
  }

  function saveDraft(root, source) {
    const key = storageKey(root);
    memoryDrafts.set(key, source);
    try { localStorage.setItem(key, source); } catch { /* memory fallback remains available */ }
  }

  function clearDraft(root) {
    const key = storageKey(root);
    memoryDrafts.delete(key);
    try { localStorage.removeItem(key); } catch { /* storage may be unavailable */ }
  }

  function emit(root, name, detail) {
    root.dispatchEvent(new CustomEvent(name, { bubbles: true, detail }));
  }

  function changed(root, source, value) {
    source.value = value;
    saveDraft(root, value);
    emit(root, "luxel-playground:draft-changed", { fileName: source.dataset.fileName, source: value });
  }

  function languageRequest(root, kind, source, position = null, revision = 0) {
    return new Promise((resolve, reject) => emit(root, "luxel-playground:language-request", {
      kind, source, position, revision, resolve, reject
    }));
  }

  function completionKind(monaco, kind) {
    const value = String(kind || "").toLowerCase();
    if (value.includes("method") || value.includes("extensionmethod")) return monaco.languages.CompletionItemKind.Method;
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
    monaco.languages.registerCompletionItemProvider("csharp", {
      triggerCharacters: [".", " "],
      async provideCompletionItems(model, position) {
        const root = modelRoots.get(model.uri.toString());
        if (!root) return { suggestions: [] };
        const offset = model.getOffsetAt(position);
        const revision = model.getVersionId();
        const result = await languageRequest(root, "completion", model.getValue(), offset, revision).catch(() => null);
        if (!result?.items || result.revision !== model.getVersionId()) return { suggestions: [] };
        const start = model.getPositionAt(Number(result.replacementStart || offset));
        const end = model.getPositionAt(Number(result.replacementStart || offset) + Number(result.replacementLength || 0));
        const range = new monaco.Range(start.lineNumber, start.column, end.lineNumber, end.column);
        return { suggestions: result.items.map(item => ({
          label: item.label,
          insertText: item.insertText || item.label,
          documentation: item.documentation || undefined,
          detail: item.detail || "Roslyn C#",
          kind: completionKind(monaco, item.kind),
          range
        })) };
      }
    });
    monaco.languages.registerHoverProvider("csharp", {
      async provideHover(model, position) {
        const root = modelRoots.get(model.uri.toString());
        if (!root) return null;
        const revision = model.getVersionId();
        const result = await languageRequest(root, "hover", model.getValue(), model.getOffsetAt(position), revision).catch(() => null);
        if (!result?.markdown || result.revision !== model.getVersionId()) return null;
        const start = model.getPositionAt(Number(result.start || 0));
        const end = model.getPositionAt(Number(result.start || 0) + Number(result.length || 0));
        return { range: new monaco.Range(start.lineNumber, start.column, end.lineNumber, end.column), contents: [{ value: `\`\`\`csharp\n${result.markdown}\n\`\`\`` }] };
      }
    });
  }

  async function initializeMonaco(root, state) {
    const mount = root.querySelector("[data-playground-monaco]");
    if (!mount || !window.LuxelMonacoReady) return;
    try {
      const monaco = await window.LuxelMonacoReady;
      if (!root.isConnected || states.get(root) !== state) return;
      registerLanguageSupport(monaco);
      const uri = monaco.Uri.parse(`inmemory://luxel/${encodeURIComponent(state.source.dataset.fileName || "script.csx")}`);
      const model = monaco.editor.createModel(state.source.value, "csharp", uri);
      modelRoots.set(model.uri.toString(), root);
      const editor = monaco.editor.create(mount, {
        model,
        theme: "vs-dark",
        automaticLayout: true,
        ariaLabel: `${state.source.dataset.fileName || "C#"} code editor`,
        minimap: { enabled: false },
        fontSize: 13,
        tabSize: 4,
        insertSpaces: true,
        scrollBeyondLastLine: false,
        quickSuggestions: true,
        suggestOnTriggerCharacters: true
      });
      state.monaco = monaco;
      state.model = model;
      state.editor = editor;
      const analyze = () => {
        const revision = model.getVersionId();
        return languageRequest(root, "analysis", model.getValue(), null, revision).then(result => {
          if (states.get(root) === state && result?.revision === model.getVersionId() && result?.diagnostics)
            setDiagnostics(root, result.diagnostics);
        }).catch(() => {});
      };
      state.changeSubscription = model.onDidChangeContent(() => {
        changed(root, state.source, model.getValue());
        clearTimeout(state.analysisTimer);
        state.analysisTimer = setTimeout(analyze, 450);
      });
      state.analysisTimer = setTimeout(analyze, 0);
      root.querySelector("[data-playground-editor-host]")?.classList.add("monaco-ready");
      root.dataset.playgroundEditor = "monaco";
      emit(root, "luxel-playground:editor-ready", { editor: "monaco", language: model.getLanguageId() });
    } catch {
      root.dataset.playgroundEditor = "textarea";
    }
  }

  function getValue(root) {
    const state = states.get(root);
    return state?.model?.getValue() ?? state?.source?.value ?? "";
  }

  function setValue(root, value) {
    const state = states.get(root);
    if (!state) return;
    const text = String(value ?? "");
    if (state.model) state.model.setValue(text);
    else changed(root, state.source, text);
  }

  function setDiagnostics(root, diagnostics) {
    const state = states.get(root);
    if (!state?.monaco || !state.model) return;
    const markers = (Array.isArray(diagnostics) ? diagnostics : []).filter(d => Number.isInteger(d.line) && Number.isInteger(d.column)).map(d => ({
      severity: String(d.severity || "error").toLowerCase() === "warning" ? state.monaco.MarkerSeverity.Warning : state.monaco.MarkerSeverity.Error,
      message: String(d.message || ""),
      code: String(d.code || d.id || ""),
      startLineNumber: Math.max(1, Number(d.line)),
      startColumn: Math.max(1, Number(d.column)),
      endLineNumber: Math.max(1, Number(d.line)),
      endColumn: Math.max(2, Number(d.column) + Math.max(1, Number(d.length || 1)))
    }));
    state.monaco.editor.setModelMarkers(state.model, markerOwner, markers);
  }

  function triggerSuggest(root) {
    const editor = states.get(root)?.editor;
    if (!editor) return false;
    editor.focus();
    editor.setPosition(editor.getModel().getPositionAt(editor.getModel().getValueLength()));
    editor.trigger("luxel-playground", "editor.action.triggerSuggest", {});
    return true;
  }

  function diagnostics(root) {
    const state = states.get(root);
    return state?.monaco && state.model ? state.monaco.editor.getModelMarkers({ resource: state.model.uri, owner: markerOwner }) : [];
  }

  function bind(root) {
    if (root.dataset.playgroundBound === "true") return;
    root.dataset.playgroundBound = "true";

    const source = root.querySelector("[data-playground-source]");
    const run = root.querySelector("[data-playground-run]");
    const cancel = root.querySelector("[data-playground-cancel]");
    const reset = root.querySelector("[data-playground-reset]");
    if (!source || !run || !cancel || !reset) return;

    const initialSource = source.value;
    const persisted = loadDraft(root);
    if (persisted !== null) source.value = persisted;
    const state = { source, initialSource, monaco: null, model: null, editor: null, changeSubscription: null, analysisTimer: null };
    states.set(root, state);

    source.addEventListener("input", () => changed(root, source, source.value));
    run.addEventListener("click", () => {
      setDiagnostics(root, []);
      emit(root, "luxel-playground:execute", {
        executionId: Number(root.dataset.executionId || "0") + 1,
        request: { fileName: source.dataset.fileName || "script.csx", source: getValue(root), files: [] }
      });
    });
    cancel.addEventListener("click", () => emit(root, "luxel-playground:cancel", {
      executionId: Number(root.dataset.executionId || "0")
    }));
    reset.addEventListener("click", () => {
      clearDraft(root);
      setValue(root, initialSource);
      setDiagnostics(root, []);
      emit(root, "luxel-playground:reset", { templateId: root.dataset.templateId });
    });
    initializeMonaco(root, state);
  }

  function bindAll(scope = document) {
    scope.querySelectorAll("[data-playground]").forEach(bind);
  }

  function dispose(root) {
    const state = states.get(root);
    if (state?.analysisTimer) clearTimeout(state.analysisTimer);
    state?.changeSubscription?.dispose();
    state?.editor?.dispose();
    if (state?.model) modelRoots.delete(state.model.uri.toString());
    state?.model?.dispose();
    states.delete(root);
  }

  window.LuxelPlayground = Object.freeze({ bind, bindAll, dispose, getValue, setValue, setDiagnostics, diagnostics, triggerSuggest });
  if (document.readyState === "loading")
    document.addEventListener("DOMContentLoaded", () => bindAll(), { once: true });
  else
    bindAll();
})();
