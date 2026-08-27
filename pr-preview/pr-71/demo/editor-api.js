const API_VERSION = 1;
let browserExports;

async function getBrowserExports() {
  if (browserExports) return browserExports;
  const runtime = globalThis.getDotnetRuntime?.(0);
  if (!runtime) throw new Error("The .NET runtime is not ready.");
  const exports = await runtime.getAssemblyExports("Luxel.Editor.Browser.dll");
  browserExports = exports?.Luxel?.Editor?.Browser?.EditorBrowserApplication;
  if (!browserExports) throw new Error("Luxel Editor browser exports are unavailable.");
  return browserExports;
}

export function installLuxelEditorApi(applyAutomationSnapshot) {
  let resolveReady;
  let rejectReady;
  let settled = false;
  const ready = new Promise((resolve, reject) => {
    resolveReady = resolve;
    rejectReady = reject;
  });

  const invoke = async (operation, args = {}) => {
    await ready;
    const exports = await getBrowserExports();
    const json = await exports.BrowserApiInvokeAsync(JSON.stringify({
      version: API_VERSION,
      operation,
      arguments: args
    }));
    return JSON.parse(json);
  };

  const api = {
    version: API_VERSION,
    ready,
    commands: {
      list: () => invoke("commands.list"),
      run: (commandId, args) => invoke("commands.run", { commandId, args })
    },
    keybindings: {
      get: () => invoke("keybindings.get"),
      update: json => invoke("keybindings.update", {
        json: typeof json === "string" ? json : JSON.stringify(json)
      }),
      reset: commandId => invoke("keybindings.reset", commandId ? { commandId } : {})
    },
    macros: {
      run: macro => invoke("macros.run", { macro })
    },
    snapshot: () => invoke("snapshot"),
    lifecycle: () => invoke("lifecycle.get"),
    dispose: () => invoke("dispose")
  };
  globalThis.luxelEditor = Object.freeze(api);

  globalThis.luxelEditorAutomation = {
    snapshot: async () => {
      await ready;
      const exports = await getBrowserExports();
      return applyAutomationSnapshot(exports.AutomationSnapshot());
    },
    invoke: async (action, value = "") => {
      await ready;
      const exports = await getBrowserExports();
      const json = await exports.AutomationInvokeAsync(action, value);
      return applyAutomationSnapshot(json);
    }
  };

  return {
    markReady(summary) {
      if (settled) return;
      settled = true;
      resolveReady({ version: API_VERSION, summary });
    },
    markFailed(error) {
      if (settled) return;
      settled = true;
      rejectReady(error instanceof Error ? error : new Error(String(error)));
    }
  };
}
