# Luxel Editor Browser demo

Fixed Phase 5 acceptance fixture using the production `Luxel.Editor.Browser` host. On first load the files under `wwwroot/demo` are copied into a writable IndexedDB workspace. **Reset Demo** replaces that workspace with the checked-in seed.

The stable automation API is `globalThis.luxelEditorAutomation`; tests use action IDs and the JSON snapshot rather than display text or Gallery story paths.
