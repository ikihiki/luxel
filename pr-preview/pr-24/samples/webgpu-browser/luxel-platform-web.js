const windows = new Map();
const events = [];
let currentEvent = null;
let nextWindowId = 1;
let clipboardCache = "";

const EventKind = Object.freeze({
    resize: 1,
    focus: 2,
    pointerMove: 3,
    pointerDown: 4,
    pointerUp: 5,
    wheel: 6,
    keyDown: 7,
    keyUp: 8,
    textInput: 9,
    close: 10,
});

function enqueue(windowId, kind, numbers = [], integers = [], text = null) {
    events.push({ windowId, kind, numbers, integers, text });
}

function requireWindow(windowId) {
    const state = windows.get(windowId);
    if (!state) throw new Error(`Unknown Luxel canvas window id ${windowId}.`);
    return state;
}

function modifiers(event) {
    return (event.ctrlKey ? 1 : 0) |
        (event.shiftKey ? 2 : 0) |
        (event.altKey ? 4 : 0) |
        (event.metaKey ? 8 : 0);
}

function pointerButton(button) {
    switch (button) {
        case 0: return 1; // left
        case 1: return 3; // middle
        case 2: return 2; // right
        case 3: return 4; // X1
        case 4: return 5; // X2
        default: return 0;
    }
}

function pointerPayload(state, event, button) {
    const rect = state.canvas.getBoundingClientRect();
    return {
        numbers: [event.clientX, event.clientY, rect.left, rect.top, rect.width, rect.height],
        integers: [state.canvas.width, state.canvas.height, button, modifiers(event)],
    };
}

function emitResize(state) {
    const canvas = state.canvas;
    const rect = canvas.getBoundingClientRect();
    const ratio = globalThis.devicePixelRatio || 1;
    if (rect.width <= 0 || rect.height <= 0) return;
    const width = Math.max(1, Math.round(rect.width * ratio));
    const height = Math.max(1, Math.round(rect.height * ratio));
    if (canvas.width !== width) canvas.width = width;
    if (canvas.height !== height) canvas.height = height;
    if (state.lastWidth === width && state.lastHeight === height && state.lastRatio === ratio) return;
    state.lastWidth = width;
    state.lastHeight = height;
    state.lastRatio = ratio;
    enqueue(state.id, EventKind.resize, [ratio], [width, height]);
}

function installEvents(state) {
    const canvas = state.canvas;
    const signal = state.abort.signal;
    const on = (name, callback, options) => canvas.addEventListener(name, callback, { ...options, signal });

    on("focus", () => {
        if (state.title) document.title = state.title;
        enqueue(state.id, EventKind.focus, [], [1]);
    });
    on("blur", () => enqueue(state.id, EventKind.focus, [], [0]));
    on("pointermove", event => {
        const payload = pointerPayload(state, event, 0);
        enqueue(state.id, EventKind.pointerMove, payload.numbers, payload.integers);
    });
    on("pointerdown", event => {
        canvas.focus({ preventScroll: true });
        try { canvas.setPointerCapture(event.pointerId); } catch { }
        const payload = pointerPayload(state, event, pointerButton(event.button));
        enqueue(state.id, EventKind.pointerDown, payload.numbers, payload.integers);
    });
    on("pointerup", event => {
        const payload = pointerPayload(state, event, pointerButton(event.button));
        enqueue(state.id, EventKind.pointerUp, payload.numbers, payload.integers);
        try { canvas.releasePointerCapture(event.pointerId); } catch { }
    });
    on("wheel", event => {
        event.preventDefault();
        const payload = pointerPayload(state, event, 0);
        payload.numbers.push(-event.deltaY);
        enqueue(state.id, EventKind.wheel, payload.numbers, payload.integers);
    }, { passive: false });
    on("keydown", event => {
        enqueue(state.id, EventKind.keyDown, [], [modifiers(event), event.repeat ? 1 : 0], event.code || "");
        if (!event.isComposing && event.key && event.key.length > 0 && [...event.key].length === 1 &&
            !event.ctrlKey && !event.altKey && !event.metaKey) {
            enqueue(state.id, EventKind.textInput, [], [], event.key);
        }
    });
    on("keyup", event => enqueue(state.id, EventKind.keyUp, [], [modifiers(event)], event.code || ""));
    on("compositionend", event => {
        if (event.data) enqueue(state.id, EventKind.textInput, [], [], event.data);
    });
    on("contextmenu", event => event.preventDefault());
    on("luxelclose", () => queueClose(state));

    state.resizeObserver = new ResizeObserver(() => emitResize(state));
    state.resizeObserver.observe(canvas);
}

function queueClose(state) {
    if (state.closePending) return;
    state.closePending = true;
    enqueue(state.id, EventKind.close);
}

export function createWindow(selector, title, width, height, visible) {
    const canvas = selector.startsWith("id:")
        ? document.getElementById(selector.slice(3))
        : document.querySelector(selector);
    if (!(canvas instanceof HTMLCanvasElement))
        throw new Error(`Luxel.Platform.Web selector '${selector}' did not resolve to an HTMLCanvasElement.`);
    for (const state of windows.values()) {
        if (state.canvas === canvas) throw new Error(`Canvas '${selector}' is already assigned to a Luxel window.`);
    }

    const ratio = globalThis.devicePixelRatio || 1;
    const id = nextWindowId++;
    const state = {
        id,
        canvas,
        abort: new AbortController(),
        resizeObserver: null,
        previousDisplay: canvas.style.display,
        closePending: false,
        lastWidth: width,
        lastHeight: height,
        lastRatio: ratio,
    };
    windows.set(id, state);

    if (!canvas.hasAttribute("tabindex")) canvas.tabIndex = 0;
    canvas.width = Math.max(1, width);
    canvas.height = Math.max(1, height);
    canvas.style.width = `${Math.max(1, width) / ratio}px`;
    canvas.style.height = `${Math.max(1, height) / ratio}px`;
    canvas.style.display = visible
        ? (state.previousDisplay === "none" ? "" : state.previousDisplay)
        : "none";
    setTitle(id, title);
    installEvents(state);
    enqueue(id, EventKind.resize, [ratio], [canvas.width, canvas.height]);
    return id;
}

export function destroyWindow(windowId) {
    const state = windows.get(windowId);
    if (!state) return;
    state.resizeObserver?.disconnect();
    state.abort.abort();
    windows.delete(windowId);
}

export function setTitle(windowId, title) {
    const state = requireWindow(windowId);
    state.title = title;
    state.canvas.title = title;
    state.canvas.setAttribute("aria-label", title);
    if (windows.size === 1 || document.activeElement === state.canvas) document.title = title;
}

export function setBounds(windowId, width, height, setWidth, setHeight) {
    const state = requireWindow(windowId);
    const ratio = globalThis.devicePixelRatio || 1;
    if (setWidth) {
        state.canvas.width = Math.max(1, width);
        state.canvas.style.width = `${Math.max(1, width) / ratio}px`;
    }
    if (setHeight) {
        state.canvas.height = Math.max(1, height);
        state.canvas.style.height = `${Math.max(1, height) / ratio}px`;
    }
    state.lastWidth = state.canvas.width;
    state.lastHeight = state.canvas.height;
    state.lastRatio = ratio;
    enqueue(windowId, EventKind.resize, [ratio], [state.canvas.width, state.canvas.height]);
}

export function showWindow(windowId) {
    const state = requireWindow(windowId);
    state.canvas.style.display = state.previousDisplay === "none" ? "" : state.previousDisplay;
    emitResize(state);
}

export function hideWindow(windowId) {
    const state = requireWindow(windowId);
    if (state.canvas.style.display !== "none") state.previousDisplay = state.canvas.style.display;
    state.canvas.style.display = "none";
}

export function focusWindow(windowId) {
    requireWindow(windowId).canvas.focus({ preventScroll: true });
}

export function closeWindow(windowId) {
    queueClose(requireWindow(windowId));
}

export function setCursor(windowId, cursorKind) {
    const cursor = ["default", "text", "pointer", "ew-resize", "ns-resize"][cursorKind] || "default";
    requireWindow(windowId).canvas.style.cursor = cursor;
}

export function dequeueEventKind() {
    currentEvent = events.shift() || null;
    return currentEvent?.kind || 0;
}

export function eventWindowId() { return currentEvent?.windowId || 0; }
export function eventNumber(index) { return currentEvent?.numbers[index] || 0; }
export function eventInteger(index) { return currentEvent?.integers[index] || 0; }
export function eventText() { return currentEvent?.text ?? null; }

export function setClipboardText(text) {
    clipboardCache = text || "";
    if (globalThis.navigator?.clipboard?.writeText) {
        void navigator.clipboard.writeText(clipboardCache).catch(() => { });
    }
}

export function requestClipboardRead() {
    if (globalThis.navigator?.clipboard?.readText) {
        void navigator.clipboard.readText().then(text => { clipboardCache = text || ""; }).catch(() => { });
    }
}

export function clipboardText() { return clipboardCache; }

globalThis.addEventListener?.("resize", () => {
    for (const state of windows.values()) emitResize(state);
});
globalThis.addEventListener?.("pagehide", () => {
    for (const state of windows.values()) queueClose(state);
});
