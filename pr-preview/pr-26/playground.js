(() => {
  "use strict";

  // Source is persisted only in same-origin storage and carried in event detail to an injected
  // executor bridge. It is never placed in a URL, DOM attribute, telemetry, or console output.
  const memoryDrafts = new Map();
  const states = new WeakMap();
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

  function registerLanguageSupport(monaco) {
    if (window.LuxelPlaygroundLanguageRegistered) return;
    window.LuxelPlaygroundLanguageRegistered = true;
    const items = [
      ["Kit.Button", "Kit.Button(_ => Log(\"Button clicked.\"), \"Click me\")", "Create a Luxel button Widget."],
      ["Kit.Text", "Kit.Text(\"Hello Luxel\")", "Create a Luxel text Widget."],
      ["Kit.VStack", "Kit.VStack(8)[${1:children}]", "Arrange child Widgets vertically."],
      ["Kit.HStack", "Kit.HStack(8)[${1:children}]", "Arrange child Widgets horizontally."],
      ["Log", "Log(${1:\"message\"})", "Write a message to the Playground Output panel."]
    ];
    monaco.languages.registerCompletionItemProvider("csharp", {
      triggerCharacters: ["."],
      provideCompletionItems(model, position) {
        const word = model.getWordUntilPosition(position);
        return { suggestions: items.map(([label, insertText, documentation]) => ({
          label,
          insertText,
          documentation,
          detail: "Luxel Playground API",
          kind: monaco.languages.CompletionItemKind.Function,
          insertTextRules: monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet,
          range: new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn)
        })) };
      }
    });
    const hover = new Map(items.map(([label, , documentation]) => [label.split(".").at(-1), `**${label}**\n\n${documentation}`]));
    monaco.languages.registerHoverProvider("csharp", {
      provideHover(model, position) {
        const word = model.getWordAtPosition(position)?.word;
        const value = word ? hover.get(word) : null;
        return value ? { contents: [{ value }] } : null;
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
      state.changeSubscription = model.onDidChangeContent(() => changed(root, state.source, model.getValue()));
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
    const state = { source, initialSource, monaco: null, model: null, editor: null, changeSubscription: null };
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
    state?.changeSubscription?.dispose();
    state?.editor?.dispose();
    state?.model?.dispose();
    states.delete(root);
  }

  window.LuxelPlayground = Object.freeze({ bind, bindAll, dispose, getValue, setValue, setDiagnostics, diagnostics, triggerSuggest });
  if (document.readyState === "loading")
    document.addEventListener("DOMContentLoaded", () => bindAll(), { once: true });
  else
    bindAll();
})();
