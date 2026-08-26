# Luxel Editor Browser demo

Fixed Phase 5 acceptance fixture using the production `Luxel.Editor.Browser` host. On first load the files under `wwwroot/demo` are copied into a writable IndexedDB workspace. **Reset Demo** replaces that workspace with the checked-in seed.

## DevTools and CDP API

The production browser host installs the versioned, Promise-based `globalThis.luxelEditor` facade. It can be used directly from the browser DevTools console or from Chrome DevTools Protocol `Runtime.evaluate`; callers never need to discover .NET assembly exports.

```js
await luxelEditor.ready;

const commands = await luxelEditor.commands.list();
await luxelEditor.commands.run("file.save");
await luxelEditor.commands.run("browser.demo.selectEntity", { entityId: 2 });

await luxelEditor.macros.run({
  version: 2,
  steps: [
    { commandId: "browser.demo.openPath", args: { path: "Scripts/Player.cs" } },
    { commandId: "browser.demo.editActiveText", args: { text: "\n// edited by macro\n" } }
  ]
});

await luxelEditor.keybindings.update([
  {
    key: "ctrl+k ctrl+e",
    command: "browser.demo.selectEntity",
    args: { entityId: 2 }
  },
  { key: "ctrl+s", command: "-file.save" }
]);
const keybindings = await luxelEditor.keybindings.get();
await luxelEditor.snapshot();
```

Every operation resolves to a JSON-serializable envelope with `version`, `operation`, `ok`, and either `result` or a structured `error` containing `code`, `message`, and optional `details`. `luxelEditor.version` is the browser API contract version. Command, macro, and keybinding arguments are JSON data; the API never evaluates supplied source text.

`commands.list()` exposes each command's current enablement, effective key descriptor, surfaces, and argument metadata. Argument-capable commands include an `arguments` descriptor with `required`, human-readable `help`, a JSON-schema-like `schema`, `hasDefaultValue`, `defaultValue`, and `paletteExecutable`. Calls that omit required arguments without a usable default, pass arguments to a parameterless command, or fail command-specific validation return an `invalid_arguments` error envelope.

### Macros and compatibility

- Macro schema version `2` adds an optional `args` value to each ordered step.
- Macro schema version `1` remains supported for compatibility with existing parameterless macros. Version 1 rejects steps that contain `args` rather than silently ignoring them.
- Macro execution stops on the first failed step by default. Set `stopOnError: false` to collect later indexed step results.
- Macro steps contain command IDs and JSON data only. They cannot execute arbitrary JavaScript or C#.
- The browser API envelope remains version `1`; the macro's own `version` is a separate compatibility field.

### Keybindings and chords

- Keybindings use an ordered VS Code-like JSON array with `key`, `command`, and optional `args`. Prefix a command with `-` to remove its binding; removal entries cannot contain `args`.
- Multi-stroke chords such as `ctrl+k ctrl+e` are supported. Returned binding and command descriptors use the canonical display form, such as `Ctrl+K Ctrl+E`, and preserve the JSON `args` value for round trips.
- Chord dispatch is stateful and uses a bounded timeout. `Escape` cancels a pending chord; an unmatched continuation cancels or retries according to the core keymap dispatcher.
- The VS Code `when` field is **not supported**. The browser contract is strict, so entries containing `when` are rejected instead of being accepted and ignored.
- Chord state advances only from input timestamps; no global timer is installed. A pending prefix expires after one second, `Escape` cancels it, and a mismatching continuation is retried once as a fresh first stroke.
- This sample's E2E coverage verifies chord configuration and descriptor/argument round trips through CDP-compatible evaluation. Deterministic core and shell-dispatch tests cover completion, timeout, prefix precedence, mismatch retry, removal, disabled commands, persistence, and late registration; browser keyboard injection is intentionally not treated as a reliable acceptance signal.

### Command palette limitation

The command palette does not provide an argument editor. Commands that require arguments can be discovered there, but they are not directly executable unless their registration supplies a safe default value. Use `commands.run(id, args)`, a version 2 macro step, or keybinding `args` when an explicit value is required.

`globalThis.luxelEditorAutomation` remains available as a thin compatibility adapter for the existing Phase 5 acceptance suite. New DevTools/CDP integrations should use `globalThis.luxelEditor`.
