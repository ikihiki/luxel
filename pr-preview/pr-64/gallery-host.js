const protocolVersion = 2;
let receiver = null;
let expected = null;
let runtimeReady = false;
let pendingArgs = null;
const wiredSplitters = new WeakSet();

function postArgs(message) {
  expected?.frame?.contentWindow?.postMessage(message, location.origin);
}

function runtimeMessage(event) {
  const message = event.data;
  if (!expected || event.origin !== location.origin || event.source !== expected.frame.contentWindow) return;
  if (!message?.luxelGallery || message.protocolVersion !== protocolVersion) return;
  if (message.story !== expected.story || message.instanceId !== expected.instanceId) return;
  if (message.type === "ready") {
    runtimeReady = true;
    if (pendingArgs) {
      const message = pendingArgs;
      pendingArgs = null;
      postArgs(message);
    }
  }
  receiver?.invokeMethodAsync("OnRuntimeMessage", message).catch(error => console.error("Gallery host message failed", error));
}

export function initialize(dotNetReceiver) {
  receiver = dotNetReceiver;
  window.addEventListener("message", runtimeMessage);
}

export function configure(frame, story, instanceId) {
  expected = { frame, story, instanceId };
  runtimeReady = false;
  pendingArgs = null;
}

export function setArgs(frame, story, instanceId, revision, requestId, argsJson) {
  const message = {
    luxelGallery: true,
    protocolVersion,
    type: "set-args",
    story,
    instanceId,
    revision,
    requestId,
    args: JSON.parse(argsJson)
  };
  if (!expected || frame !== expected.frame || story !== expected.story || instanceId !== expected.instanceId)
    return;
  if (!runtimeReady) {
    pendingArgs = message;
    return;
  }
  postArgs(message);
}

export function wireSplitter(workspace, splitter) {
  if (!workspace || !splitter || wiredSplitters.has(splitter)) return;
  wiredSplitters.add(splitter);
  const minPreview = 160;
  const minPanel = 150;
  let dragging = false;

  const resize = clientY => {
    const bounds = workspace.getBoundingClientRect();
    const available = Math.max(0, bounds.height - splitter.offsetHeight);
    const panelHeight = Math.min(Math.max(bounds.bottom - clientY, minPanel), Math.max(minPanel, available - minPreview));
    workspace.style.setProperty("--story-panel-height", `${panelHeight}px`);
    splitter.setAttribute("aria-valuenow", String(Math.round(panelHeight)));
  };

  splitter.addEventListener("pointerdown", event => {
    if (event.button !== 0) return;
    dragging = true;
    splitter.setPointerCapture?.(event.pointerId);
    document.body.classList.add("resizing-story-panel");
    event.preventDefault();
  });
  splitter.addEventListener("pointermove", event => {
    if (dragging) resize(event.clientY);
  });
  const stop = event => {
    if (!dragging) return;
    dragging = false;
    splitter.releasePointerCapture?.(event.pointerId);
    document.body.classList.remove("resizing-story-panel");
  };
  splitter.addEventListener("pointerup", stop);
  splitter.addEventListener("pointercancel", stop);
  splitter.addEventListener("keydown", event => {
    const current = Number.parseFloat(getComputedStyle(workspace).getPropertyValue("--story-panel-height")) || 260;
    const step = event.shiftKey ? 50 : 10;
    if (event.key === "ArrowUp") resize(workspace.getBoundingClientRect().bottom - current - step);
    else if (event.key === "ArrowDown") resize(workspace.getBoundingClientRect().bottom - current + step);
    else return;
    event.preventDefault();
  });
}

export function dispose() {
  window.removeEventListener("message", runtimeMessage);
  receiver = null;
  expected = null;
  runtimeReady = false;
  pendingArgs = null;
  document.body.classList.remove("resizing-story-panel");
}
