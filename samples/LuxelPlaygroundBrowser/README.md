# Luxel browser playground runtime

A static .NET 10 browser-WASM iframe runtime that compiles a revisioned C# method body with `WebScriptCompiler`, executes the generated `ILuxelWebScriptProgram` with `WebScriptExecutor`, and renders the returned `Widget` through the existing Luxel WebGPU/`UiHost` browser stack.

## Build and serve

```bash
dotnet workload install wasm-tools
dotnet publish samples/LuxelPlaygroundBrowser/LuxelPlaygroundBrowser.csproj -c Release
python3 -m http.server 8080 -d samples/LuxelPlaygroundBrowser/bin/Release/net10.0/publish/wwwroot
```

Open an iframe URL with an instance and explicit parent origin, for example:

```text
http://localhost:8080/?instance=preview-1&parentOrigin=http%3A%2F%2Flocalhost%3A8080
```

WebGPU requires a secure context (HTTPS remotely; localhost is suitable for development). The runtime is interpreter-oriented: AOT, trimming, and managed linking are disabled because Roslyn emits and loads assemblies at runtime. Native relinking remains enabled so the browser build includes HarfBuzz; it does not AOT-compile managed code.

## Protocol

The checked-in `wwwroot/playground-runtime-manifest.json` describes protocol version 2 and workspace schema version 2. Parent messages and runtime messages carry `protocol: "luxel-playground"`, `protocolVersion: 2`, the iframe `instanceId`, an execution `revision`, and a bounded full workspace snapshot. The runtime accepts only `run` messages from its actual parent window and exact configured parent origin; stale/non-increasing revisions, wrong instances, wrong protocols, mismatched workspace revisions, and malformed snapshots are ignored.

A run message is:

```js
iframe.contentWindow.postMessage({
  protocol: "luxel-playground",
  protocolVersion: 2,
  type: "run",
  instanceId: "preview-1",
  revision: 1,
  workspaceRevision: 3,
  workspace: {
    schemaVersion: 2,
    revision: 3,
    entryFileId: "main",
    activeFileId: "main",
    files: [{ id: "main", path: "Main.csx", language: "csharp-script", source: 'return Kit.Button(_ => { }, "Click me");', version: 2 }]
  }
}, iframeOrigin);
```

The child emits `ready`, optional `diagnostics`, live `output` entries, `runtime-error`, and a terminal `run-result`. A successful `run-result` is sent only after the first rendered frame is presented. Script callbacks can call `Log(...)`; the host forwards those messages to the Gallery Story's Output panel.

## Metadata references and reset model

Roslyn metadata images are copied at build/publish time into `wwwroot/references/` from resolved framework and project reference assemblies. The fixed list is in `wwwroot/references/manifest.json`; browser code never relies on `Assembly.Location`.

This MVP intentionally runs compilation and execution on the browser main runtime, not in a Web Worker. It does not claim same-runtime unload or cancellation isolation. The parent must force-reset a run by removing and recreating the iframe, which creates a fresh browser-WASM runtime. User code is trusted playground input, not a security sandbox.
