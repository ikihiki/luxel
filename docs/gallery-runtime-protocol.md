# Gallery browser runtime protocol v2

The static Gallery renders semantic Markdown in the parent document and runs browser-owned Widget stories in isolated WebAssembly iframes. The implementation is split across:

- `src/Luxel.Gallery.Stories.CoreUi`: browser-safe authored stories plus generated production component `Overview` / `Basic` pairs.
- `src/Luxel.Gallery.RuntimeManifest`: emits the catalog-backed runtime descriptor manifest.
- `samples/LuxelWebGpuBrowser`: resolves canonical story paths from the CoreUi catalog and hosts Widget results on WebGPU.
- `src/Luxel.Gallery.Site`: owns HTML args controls, iframe lifecycle, hash state, and protocol validation.

## Canonical production inventory

Source generation emits one `GeneratedComponentStoryDescriptor` for every production `[UiComponent]`. Each descriptor has a unique component type, category, exact `Controls/{category}/Overview` path, and exact `Controls/{category}/Basic` path.

The current authoritative inventory is 60 descriptors:

- every Overview returns semantic Markdown and references its matching Basic;
- every Basic returns a Widget or deterministic `StoryCapabilityFallback`;
- every Basic is owned by runtime bundle `webgpu-browser-v1`;
- only an explicit authored exact-path registration may replace a generated fallback;
- unrelated duplicate paths, cross-project duplicates, and attempts to replace authored stories remain composition errors.

Tests derive coverage from `CoreUiStoryProject.ProductionComponents` and manifest `componentType` identities, not from a naive path count that could include authored extras.

## Runtime manifest

`browser-runtime-manifest.json` is generated before browser publish and uses this shape:

```json
{
  "bundleId": "webgpu-browser-v1",
  "protocolVersion": 2,
  "entryUrl": "./",
  "stories": [
    {
      "path": "Controls/Button/Basic",
      "width": 480,
      "height": 320,
      "args": [],
      "capabilityNote": "...",
      "componentType": "global::Luxel.Controls.Button"
    }
  ]
}
```

`args` is the complete static `StoryArgDefinition` schema, including canonical default values, type hints, descriptions, range/step constraints, and enum options. `componentType` is non-null only for generated production Basics. The Site exporter rejects protocol mismatches, duplicate paths, absolute entry URLs, and catalog/manifest disagreement in viewport, schema, capability note, or component identity.

Regenerate the checked-in manifest with:

```bash
dotnet run --project src/Luxel.Gallery.RuntimeManifest -- \
  samples/LuxelWebGpuBrowser/wwwroot/browser-runtime-manifest.json
```

`dotnet publish samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj -c Release` also runs the generator before publish.

## Iframe URL

The browser entry accepts:

- `story`: canonical catalog path;
- `args`: canonical JSON object seeded before story build;
- `instance`: stable iframe instance ID generated from the containing location and referenced story path.

All URLs are relative so the application can be hosted under a Gallery subpath.

## Message envelope

Every parent/child message contains:

```js
{
  luxelGallery: true,
  protocolVersion: 2,
  type,
  story,
  instanceId,
  revision,
  args,
  requestId
}
```

Both sides validate same origin, the expected source window, protocol version, canonical story path, instance ID, and monotonic revision. Parent-originated updates also use a request ID so acknowledgements cannot be applied to the wrong edit.

### Parent to child

- `set-args`: replace the canonical args snapshot for an already-running instance. The child applies values through `StoryContext.ApplyArgs` without reloading the iframe.

### Child to parent

- `ready`: Widget runtime is initialized; includes the canonical args snapshot and schema.
- `args-changed`: a parent edit was accepted or an in-canvas action changed an arg.
- `arg-error`: one or more arg values were rejected.
- `story-error`: story lookup, build, device setup, or runtime execution failed.

The runtime additionally exposes `globalThis.luxelBrowserState` for manual runtime diagnostics and source-contract observability. This is diagnostic state, not the cross-frame protocol.

## Parent-owned args and hash state

The parent document renders an accessible args table with stable labels, control IDs, defaults, descriptions, constraints, reset buttons, and live status regions. Controls remain disabled until the matching iframe reports ready.

Non-default state is persisted in the Gallery hash:

- `args`: top-level runtime story args;
- `embeds`: a JSON object keyed by stable containing-story/embed location.

Parent edits are sent live to the child. Child actions update the parent control and hash without iframe reload. Duplicate references to the same canonical story receive distinct but deterministic instance and args-table IDs.
