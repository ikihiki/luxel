(() => {
  "use strict";
  const protocol = "luxel-playground";
  const languageSessions = new Map();
  let nextLanguageRequestId = 1;
  const sessions = new Map();

  function text(target, value) { if (target) target.textContent = value ?? ""; }
  function status(root, value, error = false) {
    const target = root.querySelector("[data-playground-status]");
    text(target, value);
    if (target) target.setAttribute("role", error ? "alert" : "status");
  }
  function running(root, value) {
    const run = root.querySelector("[data-playground-run]");
    const cancel = root.querySelector("[data-playground-cancel]");
    if (run) run.disabled = Boolean(value);
    if (cancel) cancel.disabled = !value;
  }
  function empty(target, message) {
    if (!target) return;
    target.replaceChildren();
    const paragraph = document.createElement("p");
    paragraph.className = "playground-empty";
    paragraph.textContent = message;
    target.append(paragraph);
  }
  function destroy(root, removePublished = true) {
    const session = sessions.get(root);
    if (!session) return;
    if (session.timeout) clearTimeout(session.timeout);
    session.frame.contentWindow?.postMessage({ protocol, protocolVersion: Number(root.dataset.playgroundProtocol), type: "cancel", instanceId: session.instanceId, revision: session.revision }, location.origin);
    if (removePublished || !session.published) session.frame.remove();
    sessions.delete(root);
  }
  function appendDiagnostics(root, diagnostics, failure) {
    window.LuxelPlayground?.setDiagnostics(root, Array.isArray(diagnostics) ? diagnostics : []);
    const target = root.querySelector("[data-playground-diagnostics]");
    if (!target) return;
    target.replaceChildren();
    if (Array.isArray(diagnostics) && diagnostics.length) {
      const list = document.createElement("ul");
      for (const diagnostic of diagnostics.slice(0, 200)) {
        const item = document.createElement("li");
        item.dataset.severity = String(diagnostic.severity || "error").toLowerCase();
        const button = document.createElement("button");
        const code = document.createElement("strong");
        code.textContent = diagnostic.code || diagnostic.id || "Diagnostic";
        button.append(code, document.createTextNode(" " + String(diagnostic.message || "")));
        const path = diagnostic.path || diagnostic.fileName;
        if (path) button.append(document.createTextNode(` ${path}:${diagnostic.line || diagnostic.startLine || 1}:${diagnostic.column || diagnostic.startColumn || 1}`));
        button.addEventListener("click", () => {
          const workspace = window.LuxelPlayground?.getWorkspace(root);
          const file = workspace?.files?.find(candidate => candidate.id === diagnostic.fileId || candidate.path === path);
          if (file) window.LuxelPlayground?.selectFile(root, file.id, diagnostic);
        });
        item.append(button);
        list.append(item);
      }
      target.append(list);
    }
    if (failure) {
      const alert = document.createElement("div");
      alert.className = "playground-failure";
      alert.setAttribute("role", "alert");
      alert.textContent = String(failure.message || failure.error || failure);
      target.append(alert);
    }
    if (!target.childElementCount) empty(target, "No diagnostics.");
  }
  function appendOutput(root, entries) {
    const target = root.querySelector("[data-playground-output]");
    if (!target) return;
    target.replaceChildren();
    if (!Array.isArray(entries) || !entries.length) return empty(target, "No output.");
    const list = document.createElement("ol");
    list.className = "playground-log";
    for (const entry of entries.slice(0, 200)) {
      const item = document.createElement("li");
      item.dataset.level = String(entry.level || "info").toLowerCase();
      item.textContent = String(entry.message ?? entry);
      list.append(item);
    }
    target.append(list);
  }
  function stagePreview(root, frame) {
    const preview = root.querySelector("[data-playground-preview]");
    if (!preview) return;
    preview.style.position = "relative";
    Object.assign(frame.style, { position: "absolute", inset: "0", width: "100%", height: "100%", opacity: "0", pointerEvents: "none" });
    preview.append(frame);
  }
  function publishPreview(root, session) {
    const preview = root.querySelector("[data-playground-preview]");
    if (!preview) return;
    for (const child of [...preview.children])
      if (child !== session.frame) child.remove();
    Object.assign(session.frame.style, { position: "", inset: "", width: "", height: "", opacity: "", pointerEvents: "" });
    session.published = true;
  }
  function createSession(root, detail) {
    destroy(root, false);
    const runtimeUrl = root.dataset.playgroundRuntimeUrl;
    if (!runtimeUrl) { status(root, "Playground runtime unavailable.", true); return null; }
    const revision = Number(detail?.executionId || 0);
    const instanceId = crypto.randomUUID();
    const url = new URL(runtimeUrl, location.href);
    url.searchParams.set("instance", instanceId);
    url.searchParams.set("revision", String(revision));
    url.searchParams.set("parentOrigin", location.origin);
    const frame = document.createElement("iframe");
    frame.title = "Luxel playground preview";
    frame.src = url.href;
    frame.dataset.playgroundInstance = instanceId;
    frame.setAttribute("allow", "webgpu");
    frame.setAttribute("sandbox", "allow-scripts allow-same-origin");
    const session = { frame, instanceId, revision, detail, ready: false, published: false, timeout: null };
    const startupTimeoutMs = Number(root.dataset.playgroundStartupTimeoutMs || 30000);
    session.timeout = setTimeout(() => {
      if (sessions.get(root) !== session) return;
      destroy(root, false);
      running(root, false);
      status(root, "Timed out", true);
      appendDiagnostics(root, [], { message: "Playground runtime did not become ready within 30 seconds." });
    }, startupTimeoutMs);
    sessions.set(root, session);
    stagePreview(root, frame);
    running(root, true);
    status(root, "Starting fresh runtime…");
    appendDiagnostics(root, [], null);
    appendOutput(root, []);
    return session;
  }
  function postExecute(root, session) {
    if (session.timeout) clearTimeout(session.timeout);
    const hasSlang = session.detail?.request?.workspace?.files?.some(file => file.language === "slang");
    const executionTimeoutMs = Number(root.dataset.playgroundExecutionTimeoutMs || (hasSlang ? 20000 : 5000));
    session.timeout = setTimeout(() => {
      if (sessions.get(root) !== session) return;
      destroy(root, false);
      running(root, false);
      status(root, "Timed out", true);
      appendDiagnostics(root, [], { message: `Script execution exceeded the ${executionTimeoutMs / 1000} second timeout.` });
    }, executionTimeoutMs);
    session.frame.contentWindow?.postMessage({
      protocol,
      protocolVersion: Number(root.dataset.playgroundProtocol),
      type: "run",
      instanceId: session.instanceId,
      revision: session.revision,
      workspaceRevision: session.detail.request.workspace.revision,
      workspace: session.detail.request.workspace
    }, location.origin);
    status(root, "Running…");
  }
  function handleLanguageMessage(root, session, message) {
    if (!message || message.protocol !== protocol || !Number.isSafeInteger(message.revision)) return;
    if (message.protocolVersion !== Number(root.dataset.playgroundProtocol) || message.instanceId !== session.instanceId) return;
    if (message.type === "language-ready") {
      session.ready = true;
      root.dataset.playgroundLanguageService = "roslyn-worker";
      for (const request of session.queue.splice(0)) session.worker.postMessage(request);
    } else if (message.type === "language-response" && Number.isSafeInteger(message.requestId)) {
      const pending = session.pending.get(message.requestId);
      if (!pending) return;
      session.pending.delete(message.requestId);
      clearTimeout(pending.timeout);
      if (message.workspaceRevision !== pending.workspaceRevision || message.fileId !== pending.fileId || message.fileVersion !== pending.fileVersion)
        pending.reject(new Error("Stale language service response."));
      else if (message.error || message.result?.error) pending.reject(new Error(message.error || message.result.error));
      else pending.resolve(message.result);
    }
  }
  function createLanguageSession(root) {
    const runtimeUrl = root.dataset.playgroundRuntimeUrl;
    if (!runtimeUrl || languageSessions.has(root)) return languageSessions.get(root) || null;
    const instanceId = crypto.randomUUID();
    const url = new URL("language-worker.js", new URL(runtimeUrl, location.href));
    url.searchParams.set("instance", instanceId);
    const worker = new Worker(url, { type: "module", name: "luxel-playground-roslyn" });
    const session = { worker, instanceId, ready: false, queue: [], pending: new Map() };
    worker.addEventListener("message", event => handleLanguageMessage(root, session, event.data));
    worker.addEventListener("error", event => {
      for (const pending of session.pending.values()) { clearTimeout(pending.timeout); pending.reject(new Error(event.message || "Roslyn worker failed.")); }
      session.pending.clear();
    });
    languageSessions.set(root, session);
    return session;
  }
  function postLanguageRequest(root, detail) {
    const session = createLanguageSession(root);
    if (!session) { detail.reject?.(new Error("Playground language services are unavailable.")); return; }
    const requestId = nextLanguageRequestId++;
    const request = {
      protocol,
      protocolVersion: Number(root.dataset.playgroundProtocol),
      type: "language-request",
      instanceId: session.instanceId,
      revision: Number.isSafeInteger(detail.workspaceRevision) ? detail.workspaceRevision : Number(root.dataset.languageRevision || "0") + 1,
      workspaceRevision: detail.workspaceRevision,
      requestId,
      kind: detail.kind,
      workspace: detail.workspace,
      fileId: detail.fileId,
      fileVersion: detail.fileVersion,
      path: detail.path,
      position: detail.position
    };
    root.dataset.languageRevision = String(request.workspaceRevision);
    const timeout = setTimeout(() => {
      const pending = session.pending.get(requestId);
      if (!pending) return;
      session.pending.delete(requestId);
      pending.reject(new Error("Roslyn language service request timed out."));
    }, 30000);
    session.pending.set(requestId, {
      resolve: detail.resolve || (() => {}),
      reject: detail.reject || (() => {}),
      workspaceRevision: request.workspaceRevision,
      fileId: request.fileId,
      fileVersion: request.fileVersion,
      timeout
    });
    if (session.ready) session.worker.postMessage(request);
    else session.queue.push(request);
  }
  function bind(root) {
    if (root.dataset.playgroundSiteBound === "true") return;
    root.dataset.playgroundSiteBound = "true";
    createLanguageSession(root);
    root.addEventListener("luxel-playground:language-request", event => postLanguageRequest(root, event.detail));
    root.addEventListener("luxel-playground:execute", event => createSession(root, event.detail));
    root.addEventListener("luxel-playground:cancel", () => {
      destroy(root, false);
      running(root, false);
      status(root, "Canceled");
    });
    root.addEventListener("luxel-playground:reset", () => {
      destroy(root);
      running(root, false);
      empty(root.querySelector("[data-playground-preview]"), "Run the example to see a preview.");
      appendDiagnostics(root, [], null);
      appendOutput(root, []);
      status(root, root.dataset.playgroundRuntimeUrl ? "Ready" : "Playground runtime unavailable.", !root.dataset.playgroundRuntimeUrl);
    });
  }
  function dispose(root) {
    destroy(root);
    const language = languageSessions.get(root);
    if (language) {
      language.worker.terminate();
      for (const pending of language.pending.values()) { clearTimeout(pending.timeout); pending.reject(new Error("Playground was disposed.")); }
      languageSessions.delete(root);
    }
  }
  function bindAll(scope = document) {
    window.LuxelPlayground?.bindAll(scope);
    scope.querySelectorAll("[data-playground]").forEach(bind);
  }
  window.addEventListener("message", event => {
    const message = event.data;
    if (event.origin !== location.origin || message?.protocol !== protocol || !Number.isSafeInteger(message.revision)) return;
    const pair = [...sessions].find(([, session]) => session.frame.contentWindow === event.source);
    if (!pair) return;
    const [root, session] = pair;
    if (message.protocolVersion !== Number(root.dataset.playgroundProtocol) || message.instanceId !== session.instanceId) return;
    if (message.type === "ready" && message.revision === 0 && !session.ready) { session.ready = true; postExecute(root, session); }
    else if (message.revision !== session.revision) return;
    else if (message.type === "status") status(root, String(message.status || "Running…"));
    else if (message.type === "diagnostics") appendDiagnostics(root, message.diagnostics, message.failure);
    else if (message.type === "output") appendOutput(root, message.entries || message.logs);
    else if (message.type === "runtime-error") { if (session.timeout) { clearTimeout(session.timeout); session.timeout = null; } running(root, false); appendDiagnostics(root, message.diagnostics, message.failure || message.error); status(root, "Runtime failed", true); }
    else if (message.type === "run-result") {
      if (session.timeout) { clearTimeout(session.timeout); session.timeout = null; }
      appendDiagnostics(root, message.diagnostics, message.failure);
      if (Array.isArray(message.logs) || Array.isArray(message.entries))
        appendOutput(root, message.logs || message.entries);
      if (message.success) publishPreview(root, session);
      else destroy(root, false);
      running(root, false);
      status(root, message.outcome || (message.success ? "Succeeded" : "Failed"), !message.success);
    }
  });
  window.LuxelGalleryPlayground = Object.freeze({ bind, bindAll, destroy, dispose });
})();