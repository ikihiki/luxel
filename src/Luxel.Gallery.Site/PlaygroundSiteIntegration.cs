using System.Net;
using System.Text;
using System.Text.Json;
using Luxel.Gallery.Playground;

namespace Luxel.Gallery.Site;

public static partial class GallerySiteExporter
{
    private const string PlaygroundRuntimeBaseUrl = "samples/luxel-playground/";
    private const int PlaygroundProtocolVersion = 1;
    private static readonly string[] PlaygroundRuntimeRequiredFiles =
        ["index.html", "main.js", "language-worker.js", "playground-runtime-manifest.json", Path.Combine("_framework", "dotnet.js")];

    private sealed record PlaygroundRuntimeManifest(string Protocol, int ProtocolVersion, string EntryUrl);

    private const string MonacoBootstrapScript = """
(() => {
  "use strict";
  const vs = new URL("vendor/monaco/vs", document.baseURI).href.replace(/\/$/, "");
  window.MonacoEnvironment = { getWorkerUrl: () => `${vs}/editor/editor.worker.js` };
  window.LuxelMonacoReady = new Promise((resolve, reject) => {
    if (typeof window.require !== "function") { reject(new Error("Monaco AMD loader is unavailable.")); return; }
    window.require.config({ paths: { vs } });
    window.require(["vs/editor/editor.main"], () => resolve(window.monaco), reject);
  });
})();
""";

    private static PlaygroundRuntimeManifest? ExportPlaygroundAssets(string output, string? runtimeRoot)
    {
        PlaygroundRuntimeManifest? runtime = runtimeRoot is null ? null : LoadPlaygroundRuntime(runtimeRoot);
        if (runtime is not null)
            CopyPlaygroundRuntime(runtimeRoot!, Path.Combine(output, PlaygroundRuntimeBaseUrl.Replace('/', Path.DirectorySeparatorChar)));

        string monacoSource = Path.Combine(AppContext.BaseDirectory, "Assets", "monaco");
        if (!Directory.Exists(monacoSource))
            throw new DirectoryNotFoundException($"Vendored Monaco assets are missing: {monacoSource}");
        CopyDirectory(Path.Combine(monacoSource, "vs"), Path.Combine(output, "vendor", "monaco", "vs"));
        File.Copy(Path.Combine(monacoSource, "LICENSE"), Path.Combine(output, "licenses", "monaco-editor-LICENSE.txt"), overwrite: true);
        File.WriteAllText(Path.Combine(output, "monaco-bootstrap.js"), MonacoBootstrapScript, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "playground.css"), PlaygroundAssets.ReadStyle(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "playground.js"), PlaygroundAssets.ReadScript(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "playground-site.js"), PlaygroundBridgeScript, new UTF8Encoding(false));
        return runtime;
    }

    private static string PlaygroundStoryFragment(PlaygroundRuntimeManifest? runtime)
    {
        var state = new PlaygroundState { Draft = PlaygroundTemplates.Button.CreateDraft() };
        string markup = PlaygroundWorkspace.Render(state, "gallery-playground");
        string runtimeUrl = runtime is null ? "" : PlaygroundRuntimeBaseUrl + RuntimeEntry(runtime.EntryUrl);
        markup = markup.Replace("data-playground ",
            $"data-playground data-playground-runtime-url=\"{WebUtility.HtmlEncode(runtimeUrl)}\" data-playground-protocol=\"{PlaygroundProtocolVersion}\" ",
            StringComparison.Ordinal);
        if (runtime is null)
        {
            markup = markup.Replace(">Ready</p>", ">Playground runtime unavailable. Publish and pass --playground-browser-root to enable execution.</p>", StringComparison.Ordinal)
                .Replace("data-playground-run>", "data-playground-run disabled>", StringComparison.Ordinal);
        }
        return $"<header><h1>C# Playground</h1></header><div class=\"playground-page\">{markup}</div>";
    }

    private static PlaygroundRuntimeManifest LoadPlaygroundRuntime(string source)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Playground browser publish root is missing: {source}");
        string path = Path.Combine(source, "playground-runtime-manifest.json");
        if (!File.Exists(path))
            throw new FileNotFoundException("Playground runtime manifest is missing.", path);
        PlaygroundRuntimeManifest manifest = JsonSerializer.Deserialize<PlaygroundRuntimeManifest>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Playground runtime manifest is empty.");
        if (!string.Equals(manifest.Protocol, "luxel-playground", StringComparison.Ordinal))
            throw new InvalidDataException($"Playground runtime protocol name '{manifest.Protocol}' is unsupported; expected 'luxel-playground'.");
        if (manifest.ProtocolVersion != PlaygroundProtocolVersion)
            throw new InvalidDataException($"Playground runtime protocol {manifest.ProtocolVersion} is unsupported; expected {PlaygroundProtocolVersion}.");
        if (!string.Equals(manifest.EntryUrl, "./", StringComparison.Ordinal))
            throw new InvalidDataException("Playground runtime entry URL must be './'.");
        foreach (string relative in PlaygroundRuntimeRequiredFiles)
        {
            RequireSafeRelativePath(relative, "playground runtime required file");
            string required = Path.Combine(source, relative);
            if (!File.Exists(required))
                throw new FileNotFoundException($"Playground browser publish root is incomplete; required app file is missing: {relative}", required);
        }
        return manifest;
    }

    private static void CopyPlaygroundRuntime(string source, string destination)
    {
        CopyDirectory(source, destination);
        foreach (string relative in PlaygroundRuntimeRequiredFiles)
            if (!File.Exists(Path.Combine(destination, relative)))
                throw new FileNotFoundException($"Copied playground browser app is incomplete: {relative}", Path.Combine(destination, relative));
    }

    private static void RequireSafeRelativePath(string relative, string kind)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || Uri.TryCreate(relative, UriKind.Absolute, out _))
            throw new InvalidDataException($"Root/absolute path is not allowed for {kind}: {relative}");
        string normalized = relative.Replace('\\', '/');
        if (normalized.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException($"Unsafe relative path for {kind}: {relative}");
    }

    private static string RuntimeEntry(string entryUrl) => entryUrl == "./" ? "" : entryUrl.TrimStart('.', '/').TrimEnd('/') + "/";

    internal static string PlaygroundClientScript => PlaygroundBridgeScript;

    private const string PlaygroundBridgeScript = """
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
    window.LuxelPlayground?.setDiagnostics(root, Array.isArray(diagnostics) ? diagnostics : []);
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
    const startupTimeoutMs = Number(root.dataset.playgroundStartupTimeoutMs || 30000);
    session.timeout = setTimeout(() => {
      if (sessions.get(root) !== session) return;
      destroy(root);
      status(root, "Timed out", true);
      appendDiagnostics(root, [], { message: "Playground runtime did not become ready within 30 seconds." });
    }, startupTimeoutMs);
    sessions.set(root, session);
    renderPreview(root, frame);
    status(root, "Starting fresh runtime…");
    appendDiagnostics(root, [], null);
    appendOutput(root, []);
    return session;
  }
  function postExecute(root, session) {
    if (session.timeout) clearTimeout(session.timeout);
    const executionTimeoutMs = Number(root.dataset.playgroundExecutionTimeoutMs || 5000);
    session.timeout = setTimeout(() => {
      if (sessions.get(root) !== session) return;
      destroy(root);
      status(root, "Timed out", true);
      appendDiagnostics(root, [], { message: "Script execution exceeded the 5 second timeout." });
    }, executionTimeoutMs);
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
      if (message.error || message.result?.error) pending.reject(new Error(message.error || message.result.error));
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
      revision: Number.isSafeInteger(detail.revision) ? detail.revision : Number(root.dataset.languageRevision || "0") + 1,
      requestId,
      kind: detail.kind,
      source: detail.source,
      position: detail.position
    };
    root.dataset.languageRevision = String(request.revision);
    const timeout = setTimeout(() => {
      const pending = session.pending.get(requestId);
      if (!pending) return;
      session.pending.delete(requestId);
      pending.reject(new Error("Roslyn language service request timed out."));
    }, 30000);
    session.pending.set(requestId, {
      resolve: detail.resolve || (() => {}),
      reject: detail.reject || (() => {}),
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
    else if (message.type === "runtime-error") { if (session.timeout) { clearTimeout(session.timeout); session.timeout = null; } appendDiagnostics(root, message.diagnostics, message.failure || message.error); status(root, "Runtime failed", true); }
    else if (message.type === "run-result") {
      if (session.timeout) { clearTimeout(session.timeout); session.timeout = null; }
      appendDiagnostics(root, message.diagnostics, message.failure);
      appendOutput(root, message.logs || message.entries);
      status(root, message.outcome || (message.success ? "Succeeded" : "Failed"), !message.success);
    }
  });
  window.LuxelGalleryPlayground = Object.freeze({ bind, bindAll, destroy, dispose });
})();
""";
}
