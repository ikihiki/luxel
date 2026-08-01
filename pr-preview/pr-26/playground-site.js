(() => {
  "use strict";
  const protocol = "luxel-playground";
  const sessions = new Map();

  function text(target, value) { if (target) target.textContent = value ?? ""; }
  function status(root, value, error = false) {
    const target = root.querySelector("[data-playground-status]");
    text(target, value);
    if (target) target.setAttribute("role", error ? "alert" : "status");
  }
  function empty(target, message) {
    if (!target) return;
    target.replaceChildren();
    const paragraph = document.createElement("p");
    paragraph.className = "playground-empty";
    paragraph.textContent = message;
    target.append(paragraph);
  }
  function destroy(root) {
    const session = sessions.get(root);
    if (session?.timeout) clearTimeout(session.timeout);
    session?.frame?.remove();
    sessions.delete(root);
  }
  function appendDiagnostics(root, diagnostics, failure) {
    const target = root.querySelector("[data-playground-diagnostics]");
    if (!target) return;
    target.replaceChildren();
    if (Array.isArray(diagnostics) && diagnostics.length) {
      const list = document.createElement("ul");
      for (const diagnostic of diagnostics.slice(0, 200)) {
        const item = document.createElement("li");
        item.dataset.severity = String(diagnostic.severity || "error").toLowerCase();
        const code = document.createElement("strong");
        code.textContent = diagnostic.code || diagnostic.id || "Diagnostic";
        item.append(code, document.createTextNode(" " + String(diagnostic.message || "")));
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
  function renderPreview(root, frame) {
    const preview = root.querySelector("[data-playground-preview]");
    preview?.replaceChildren(frame);
  }
  function createSession(root, detail) {
    destroy(root);
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
    const session = { frame, instanceId, revision, detail, ready: false, timeout: null };
    session.timeout = setTimeout(() => {
      if (sessions.get(root) !== session) return;
      destroy(root);
      status(root, "Timed out", true);
      appendDiagnostics(root, [], { message: "Script execution exceeded the 5 second timeout." });
    }, 5000);
    sessions.set(root, session);
    renderPreview(root, frame);
    status(root, "Starting fresh runtime…");
    appendDiagnostics(root, [], null);
    appendOutput(root, []);
    return session;
  }
  function postExecute(root, session) {
    session.frame.contentWindow?.postMessage({
      protocol,
      protocolVersion: Number(root.dataset.playgroundProtocol),
      type: "run",
      instanceId: session.instanceId,
      revision: session.revision,
      source: session.detail.request.source
    }, location.origin);
    status(root, "Running…");
  }
  function bind(root) {
    if (root.dataset.playgroundSiteBound === "true") return;
    root.dataset.playgroundSiteBound = "true";
    root.addEventListener("luxel-playground:execute", event => createSession(root, event.detail));
    root.addEventListener("luxel-playground:cancel", () => {
      const session = sessions.get(root);
      session?.frame.contentWindow?.postMessage({ protocol, protocolVersion: Number(root.dataset.playgroundProtocol), type: "cancel", instanceId: session.instanceId, revision: session.revision }, location.origin);
      destroy(root);
      status(root, "Canceled");
    });
    root.addEventListener("luxel-playground:reset", () => {
      destroy(root);
      empty(root.querySelector("[data-playground-preview]"), "Run the example to see a preview.");
      appendDiagnostics(root, [], null);
      appendOutput(root, []);
      status(root, root.dataset.playgroundRuntimeUrl ? "Ready" : "Playground runtime unavailable.", !root.dataset.playgroundRuntimeUrl);
    });
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
    else if (message.type === "runtime-error") { if (session.timeout) { clearTimeout(session.timeout); session.timeout = null; } appendDiagnostics(root, message.diagnostics, message.failure || message.error); status(root, "Runtime failed", true); }
    else if (message.type === "run-result") {
      if (session.timeout) { clearTimeout(session.timeout); session.timeout = null; }
      appendDiagnostics(root, message.diagnostics, message.failure);
      appendOutput(root, message.logs || message.entries);
      status(root, message.outcome || (message.success ? "Succeeded" : "Failed"), !message.success);
    }
  });
  window.LuxelGalleryPlayground = Object.freeze({ bind, bindAll, destroy });
})();