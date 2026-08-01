(() => {
  "use strict";

  // Source is persisted only in same-origin storage and carried in event detail to an injected
  // executor bridge. It is never placed in a URL, DOM attribute, telemetry, or console output.
  const memoryDrafts = new Map();
  const storagePrefix = "luxel.playground.draft.v1:";

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

    source.addEventListener("input", () => {
      saveDraft(root, source.value);
      emit(root, "luxel-playground:draft-changed", {
        fileName: source.dataset.fileName,
        source: source.value
      });
    });

    run.addEventListener("click", () => {
      emit(root, "luxel-playground:execute", {
        executionId: Number(root.dataset.executionId || "0") + 1,
        request: {
          fileName: source.dataset.fileName || "script.csx",
          source: source.value,
          files: []
        }
      });
    });

    cancel.addEventListener("click", () => {
      emit(root, "luxel-playground:cancel", {
        executionId: Number(root.dataset.executionId || "0")
      });
    });

    reset.addEventListener("click", () => {
      clearDraft(root);
      source.value = initialSource;
      emit(root, "luxel-playground:reset", { templateId: root.dataset.templateId });
    });
  }

  function bindAll(scope = document) {
    scope.querySelectorAll("[data-playground]").forEach(bind);
  }

  window.LuxelPlayground = Object.freeze({ bind, bindAll });
  if (document.readyState === "loading")
    document.addEventListener("DOMContentLoaded", () => bindAll(), { once: true });
  else
    bindAll();
})();
